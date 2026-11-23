using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Consumer.FeatureFlags.Evaluation;

namespace Grimoire.Consumer.FeatureFlags;

/// <summary>Evaluates Grimoire feature flags locally.</summary>
public interface IGrimoireFeatureFlagClient
{
    /// <summary>Which variant <paramref name="key"/> serves for <paramref name="context"/>, and why.</summary>
    Task<FeatureFlagResult> EvaluateAsync(
        string key,
        FlagContext? context = null,
        CancellationToken ct = default
    );

    /// <summary>
    /// Whether the flag serves the JSON value <c>true</c> (a boolean flag's <c>on</c>).
    /// Unknown flags are off.
    /// </summary>
    Task<bool> IsEnabledAsync(
        string key,
        FlagContext? context = null,
        CancellationToken ct = default
    );

    /// <summary>The served variant's key, or <c>null</c> for an unknown flag.</summary>
    Task<string?> GetVariantAsync(
        string key,
        FlagContext? context = null,
        CancellationToken ct = default
    );

    /// <summary>The served variant's value as <typeparamref name="T"/>, or <paramref name="fallback"/>.</summary>
    Task<T?> GetValueAsync<T>(
        string key,
        FlagContext? context = null,
        T? fallback = default,
        CancellationToken ct = default
    );

    /// <summary>Keys of every flag in the cached definitions.</summary>
    Task<IReadOnlyCollection<string>> GetFlagKeysAsync(CancellationToken ct = default);

    /// <summary>Fetches definitions now. Returns <c>true</c> when they changed.</summary>
    Task<bool> RefreshAsync(CancellationToken ct = default);
}

/// <summary>
/// Fetches flag definitions from the Consumer API (<c>GET /consumer/feature-flags</c>), keeps them
/// in memory and evaluates locally with the shared evaluation spec: checking a flag never waits
/// on the network once definitions are loaded, and keeps working with the last definitions if
/// Grimoire is unreachable. Stale definitions are refreshed in the background with a
/// conditional request (<c>If-None-Match</c>), so unchanged flags cost a <c>304</c>.
/// Create one per application and environment and reuse it (it is thread-safe).
/// </summary>
public sealed class GrimoireFeatureFlagClient : IGrimoireFeatureFlagClient, IDisposable
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly GrimoireFeatureFlagOptions _options;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private Snapshot? _snapshot;
    private int _backgroundRefresh;

    private sealed record CompiledFlag(
        FlagDefinition Definition,
        FlagEnvironmentState State,
        IReadOnlyDictionary<string, JsonElement> Values
    );

    private sealed record Snapshot(
        IReadOnlyDictionary<string, CompiledFlag> Flags,
        string? ETag,
        DateTimeOffset FetchedAt
    );

    public GrimoireFeatureFlagClient(GrimoireFeatureFlagOptions options)
        : this(options, new HttpClient(), TimeProvider.System, ownsHttp: true) { }

    internal GrimoireFeatureFlagClient(
        GrimoireFeatureFlagOptions options,
        HttpClient http,
        TimeProvider time,
        bool ownsHttp = false
    )
    {
        options.Validate();
        _options = options;
        _time = time;
        _http = http;
        _ownsHttp = ownsHttp;
        _http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Remove("X-Api-Key");
        _http.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
    }

    /// <summary>When definitions were last fetched or confirmed unchanged; <c>null</c> before the first load.</summary>
    public DateTimeOffset? LastRefreshedAt => _snapshot?.FetchedAt;

    public async Task<FeatureFlagResult> EvaluateAsync(
        string key,
        FlagContext? context = null,
        CancellationToken ct = default
    )
    {
        var snapshot = await CurrentAsync(ct);
        if (snapshot is null || !snapshot.Flags.TryGetValue(key, out var flag))
            return new FeatureFlagResult(key, null, default, FeatureFlagReasons.FlagNotFound);

        var result = FlagEvaluator.Evaluate(
            flag.Definition,
            flag.State,
            context ?? FlagContext.Empty
        );
        return new FeatureFlagResult(
            key,
            result.Variant,
            flag.Values.TryGetValue(result.Variant, out var value) ? value : default,
            result.Reason,
            result.MatchedRuleIndex,
            result.Bucket
        );
    }

    public async Task<bool> IsEnabledAsync(
        string key,
        FlagContext? context = null,
        CancellationToken ct = default
    ) => (await EvaluateAsync(key, context, ct)).IsEnabled;

    public async Task<string?> GetVariantAsync(
        string key,
        FlagContext? context = null,
        CancellationToken ct = default
    ) => (await EvaluateAsync(key, context, ct)).Variant;

    public async Task<T?> GetValueAsync<T>(
        string key,
        FlagContext? context = null,
        T? fallback = default,
        CancellationToken ct = default
    ) => (await EvaluateAsync(key, context, ct)).ValueAs(fallback);

    public async Task<IReadOnlyCollection<string>> GetFlagKeysAsync(
        CancellationToken ct = default
    ) => (await CurrentAsync(ct))?.Flags.Keys.ToList() ?? [];

    public async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            return await FetchAsync(ct);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Loaded definitions: the first call waits for them; later calls never wait, and refresh
    /// stale ones in the background. <c>null</c> only while nothing could be loaded yet.
    /// </summary>
    private async Task<Snapshot?> CurrentAsync(CancellationToken ct)
    {
        var snapshot = _snapshot;
        if (snapshot is null)
        {
            try
            {
                await RefreshAsync(ct);
            }
            catch (Exception ex) when (IsTransient(ex, ct))
            {
                // Unreachable: serve defaults now, try again on the next call.
            }
            return _snapshot;
        }

        if (_time.GetUtcNow() - snapshot.FetchedAt >= _options.RefreshInterval)
            StartBackgroundRefresh();
        return snapshot;
    }

    private void StartBackgroundRefresh()
    {
        if (Interlocked.CompareExchange(ref _backgroundRefresh, 1, 0) != 0)
            return;
        _ = Task.Run(async () =>
        {
            try
            {
                await RefreshAsync(CancellationToken.None);
            }
            catch (Exception ex) when (IsTransient(ex, CancellationToken.None))
            {
                // Keep serving the last definitions; the next stale call retries.
            }
            finally
            {
                Volatile.Write(ref _backgroundRefresh, 0);
            }
        });
    }

    /// <summary>Failures that leave the last definitions in use: network, HTTP, timeouts, bad JSON.</summary>
    private static bool IsTransient(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or JsonException
        || (ex is TaskCanceledException && !ct.IsCancellationRequested);

    /// <summary>Wait for a background refresh started by a stale read (tests).</summary>
    internal async Task WaitForBackgroundRefreshAsync()
    {
        while (Volatile.Read(ref _backgroundRefresh) != 0)
            await Task.Delay(5);
    }

    private async Task<bool> FetchAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"consumer/feature-flags?environment={Uri.EscapeDataString(_options.Environment)}"
        );
        var current = _snapshot;
        if (current?.ETag is { } etag && EntityTagHeaderValue.TryParse(etag, out var tag))
            request.Headers.IfNoneMatch.Add(tag);

        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotModified && current is not null)
        {
            _snapshot = current with { FetchedAt = _time.GetUtcNow() };
            return false;
        }
        response.EnsureSuccessStatusCode();

        var list = await response.Content.ReadFromJsonAsync<FlagListDto>(JsonOptions, ct)
            ?? throw new HttpRequestException("Empty feature flag response.");
        _snapshot = new Snapshot(
            list.Flags.ToDictionary(f => f.Key, Compile, StringComparer.Ordinal),
            response.Headers.ETag?.ToString(),
            _time.GetUtcNow()
        );
        return true;
    }

    private static CompiledFlag Compile(FlagDto f) =>
        new(
            new FlagDefinition(
                f.Key,
                f.Salt,
                f.Variants.Select(v => new FlagVariant(v.Key, v.Value.GetRawText())).ToList(),
                f.DefaultVariant
            ),
            new FlagEnvironmentState(
                f.Enabled,
                f.OffVariant,
                f.Rules ?? [],
                f.Fallthrough,
                f.Version
            ),
            f.Variants.ToDictionary(v => v.Key, v => v.Value.Clone(), StringComparer.Ordinal)
        );

    public void Dispose()
    {
        _refreshLock.Dispose();
        if (_ownsHttp)
            _http.Dispose();
    }

    private sealed record FlagListDto(string Environment, List<FlagDto> Flags);

    private sealed record VariantDto(string Key, JsonElement Value);

    private sealed record FlagDto(
        string Key,
        string Type,
        List<VariantDto> Variants,
        string DefaultVariant,
        string Salt,
        bool Enabled,
        string? OffVariant,
        List<TargetingRule>? Rules,
        FlagServe? Fallthrough,
        int Version
    );
}
