using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace Grimoire.Consumer;

/// <summary>
/// Configuration provider (and client) for Grimoire. Loads every value at startup; with
/// <see cref="GrimoireRefreshOptions"/> it also watches the sentinel keys and reloads when
/// Grimoire publishes changes (see <c>AddGrimoire(o =&gt; o.Connect(…).ConfigureRefresh())</c>).
/// </summary>
public class GrimoireConfigurationClient : ConfigurationProvider, IConfigurationSource, IDisposable
{
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly string _environment;
    private readonly HttpMessageHandler? _handler;
    private readonly GrimoireRefreshOptions? _refresh;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Dictionary<string, WatchedKey> _watched = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stopping = new();
    private HttpClient? _http;
    private int _started;
    private bool _disposed;

    /// <summary>
    /// Set when the sentinel baseline couldn't be read at startup: the first successful check
    /// then reloads once, so a publish made around startup is never missed.
    /// </summary>
    private bool _reloadOnFirstCheck;

    /// <summary>The last seen state of a watched key.</summary>
    private sealed record WatchedKey(bool Exists, string? Value, string? ValueType, string? ETag);

    public GrimoireConfigurationClient(string baseUrl, string apiKey, string environment)
        : this(baseUrl, apiKey, environment, refresh: null) { }

    /// <summary>A provider that also watches for published changes (<paramref name="refresh"/>).</summary>
    public GrimoireConfigurationClient(
        string baseUrl,
        string apiKey,
        string environment,
        GrimoireRefreshOptions? refresh
    )
        : this(baseUrl, apiKey, environment, null, refresh, null) { }

    internal GrimoireConfigurationClient(
        string baseUrl,
        string apiKey,
        string environment,
        HttpMessageHandler? handler,
        GrimoireRefreshOptions? refresh = null,
        TimeProvider? time = null
    )
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _environment = environment;
        _handler = handler;
        _refresh = refresh;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>When watched keys were last checked successfully; <c>null</c> before the first check.</summary>
    public DateTimeOffset? LastRefreshedAt { get; private set; }

    /// <summary>
    /// Checks the watched keys now (conditional requests) and reloads when one changed. Returns
    /// <c>true</c> when configuration was reloaded. Network and HTTP errors are thrown and leave
    /// the current values in place. Without refresh options, nothing is watched and this
    /// returns <c>false</c>.
    /// </summary>
    public async Task<bool> TryRefreshAsync(CancellationToken ct = default)
    {
        if (_refresh is null)
            return false;
        await _refreshLock.WaitAsync(ct);
        try
        {
            return await RefreshWatchedAsync(ct);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

    /// <summary>
    /// All configuration values as stored in Grimoire. Array and JSON values are returned as their
    /// raw JSON text; use <see cref="Load"/> (through <c>AddGrimoire</c>) to get them flattened
    /// into configuration sections.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(
        CancellationToken ct = default
    ) => (await FetchAllAsync(ct)).ToDictionary(i => i.Key, i => i.Value);

    /// <summary>The raw value of <paramref name="key"/> (JSON text for array and JSON values).</summary>
    public async Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        (await FetchAsync(key, ct))?.Value;

    /// <summary>
    /// The value of <paramref name="key"/> deserialised to <typeparamref name="T"/>: JSON for
    /// array and JSON values (for example <c>string[]</c> or an options class), and a JSON
    /// string literal for string and multiline values. Returns <c>default</c> when the key is missing.
    /// </summary>
    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var item = await FetchAsync(key, ct);
        if (item is null)
            return default;
        return IsStructured(item.ValueType)
            ? JsonSerializer.Deserialize<T>(item.Value, JsonOptions)
            : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(item.Value), JsonOptions);
    }

    private async Task<ConfigItem?> FetchAsync(string key, CancellationToken ct)
    {
        using var response = await Http.GetAsync(KeyUrl(key), ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ConfigItem>(ct);
    }

    private string KeyUrl(string key) =>
        $"consumer/configurations/{Uri.EscapeDataString(key)}?environment={Uri.EscapeDataString(_environment)}";

    /// <summary>
    /// Loads every value into configuration. Array and JSON values are flattened the same way as
    /// <c>appsettings.json</c>, so <c>AllowedIps</c> = <c>["a","b"]</c> becomes
    /// <c>AllowedIps:0</c> and <c>AllowedIps:1</c>, and binds with
    /// <c>GetSection("AllowedIps").Get&lt;string[]&gt;()</c>.
    /// </summary>
    public override void Load()
    {
        // With refresh, read the sentinel baseline *before* the values: a publish in between
        // then shows up as a change on the first check instead of being missed.
        var watching = _refresh is not null && Interlocked.Exchange(ref _started, 1) == 0;
        if (watching)
            RecordBaseline();
        var items = FetchAllAsync(CancellationToken.None).GetAwaiter().GetResult();
        Data = ToData(items);
        if (watching)
        {
            var stopping = _stopping.Token;
            _ = Task.Run(() => WatchAsync(stopping), stopping);
        }
    }

    private static Dictionary<string, string?> ToData(IEnumerable<ConfigItem> items)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
            Add(item, data);
        return data;
    }

    private static void Add(ConfigItem item, Dictionary<string, string?> data)
    {
        if (IsStructured(item.ValueType) && TryFlatten(item.Key, item.Value, data))
            return;
        data[item.Key] = item.Value;
    }

    /// <summary>Records the watched keys' current state (no reload).</summary>
    private void RecordBaseline()
    {
        try
        {
            var states = new Dictionary<string, WatchedKey>(StringComparer.Ordinal);
            foreach (var key in _refresh!.WatchedKeys.Keys)
                states[key] = CheckAsync(key, null, _stopping.Token).GetAwaiter().GetResult().State;
            foreach (var (key, state) in states)
                _watched[key] = state;
        }
        catch (Exception ex) when (IsTransient(ex, _stopping.Token))
        {
            _reloadOnFirstCheck = true;
        }
    }

    private async Task WatchAsync(CancellationToken ct)
    {
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(NextDelay(failures), _time, ct);
                await TryRefreshAsync(ct);
                failures = 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (IsTransient(ex, ct))
            {
                // Keep the last good values and back off.
                failures++;
            }
        }
    }

    /// <summary>The interval plus 0-20 % jitter, or exponential backoff (capped) after failures.</summary>
    internal TimeSpan NextDelay(int failures)
    {
        var interval = _refresh!.RefreshInterval;
        if (failures > 0)
        {
            var backoff = interval * Math.Pow(2, Math.Min(failures, 16));
            return backoff < GrimoireRefreshOptions.MaximumBackoff
                ? backoff
                : GrimoireRefreshOptions.MaximumBackoff;
        }
        return interval * (1 + Random.Shared.NextDouble() * 0.2);
    }

    private static bool IsTransient(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or JsonException
        || (ex is TaskCanceledException && !ct.IsCancellationRequested);

    private async Task<bool> RefreshWatchedAsync(CancellationToken ct)
    {
        // Read every watched key first; nothing is committed until the reload has succeeded,
        // so a failure (here or while reloading) is retried on the next check.
        var states = new Dictionary<string, WatchedKey>(StringComparer.Ordinal);
        var reloadAll = _reloadOnFirstCheck;
        var changedKeys = new List<(string Key, ConfigItem? Item)>();
        foreach (var (key, refreshAll) in _refresh!.WatchedKeys)
        {
            _watched.TryGetValue(key, out var previous);
            var (changed, state, item) = await CheckAsync(key, previous, ct);
            states[key] = state;
            if (!changed)
                continue;
            if (refreshAll)
                reloadAll = true;
            else
                changedKeys.Add((key, item));
        }

        var reloaded = false;
        if (reloadAll)
        {
            Data = ToData(await FetchAllAsync(ct));
            reloaded = true;
        }
        else if (changedKeys.Count > 0)
        {
            Data = WithKeys(Data, changedKeys);
            reloaded = true;
        }

        foreach (var (key, state) in states)
            _watched[key] = state;
        _reloadOnFirstCheck = false;
        LastRefreshedAt = _time.GetUtcNow();
        if (reloaded)
            OnReload();
        return reloaded;
    }

    /// <summary>A copy of <paramref name="current"/> with each key (and its flattened children) replaced.</summary>
    private static Dictionary<string, string?> WithKeys(
        IDictionary<string, string?> current,
        List<(string Key, ConfigItem? Item)> changes
    )
    {
        var data = new Dictionary<string, string?>(current, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, item) in changes)
        {
            var stale = data
                .Keys.Where(k =>
                    string.Equals(k, key, StringComparison.OrdinalIgnoreCase)
                    || k.StartsWith(
                        key + ConfigurationPath.KeyDelimiter,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .ToList();
            foreach (var k in stale)
                data.Remove(k);
            if (item is not null)
                Add(item, data);
        }
        return data;
    }

    /// <summary>
    /// Conditional read of one watched key. A change is a different value or value type, or the
    /// key appearing or disappearing, compared with <paramref name="previous"/>; without a
    /// previous state the read only records it. Returns the new state without storing it.
    /// </summary>
    private async Task<(bool Changed, WatchedKey State, ConfigItem? Item)> CheckAsync(
        string key,
        WatchedKey? previous,
        CancellationToken ct
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, KeyUrl(key));
        if (previous?.ETag is { } etag && EntityTagHeaderValue.TryParse(etag, out var tag))
            request.Headers.IfNoneMatch.Add(tag);

        using var response = await Http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotModified && previous is not null)
            return (false, previous, null);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return (previous is { Exists: true }, new WatchedKey(false, null, null, null), null);
        response.EnsureSuccessStatusCode();

        var item = await response.Content.ReadFromJsonAsync<ConfigItem>(ct)
            ?? throw new HttpRequestException($"Empty response for {key}.");
        var state = new WatchedKey(
            true,
            item.Value,
            item.ValueType,
            response.Headers.ETag?.ToString()
        );
        var changed =
            previous is not null
            && (
                !previous.Exists
                || !string.Equals(previous.Value, item.Value, StringComparison.Ordinal)
                || !string.Equals(
                    previous.ValueType,
                    item.ValueType,
                    StringComparison.OrdinalIgnoreCase
                )
            );
        return (changed, state, item);
    }

    /// <summary>Stops watching for changes.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;
        _disposed = true;
        if (!disposing)
            return;
        _stopping.Cancel();
        _stopping.Dispose();
        _http?.Dispose();
    }

    private static bool IsStructured(string? valueType) =>
        string.Equals(valueType, "array", StringComparison.OrdinalIgnoreCase)
        || string.Equals(valueType, "json", StringComparison.OrdinalIgnoreCase);

    private static bool TryFlatten(string key, string json, IDictionary<string, string?> data)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            // Not valid JSON after all: fall back to the raw value rather than failing to start.
            return false;
        }

        using (doc)
            Flatten(key, doc.RootElement, data);
        return true;
    }

    private static void Flatten(string path, JsonElement element, IDictionary<string, string?> data)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var hasProperties = false;
                foreach (var property in element.EnumerateObject())
                {
                    hasProperties = true;
                    Flatten(ConfigurationPath.Combine(path, property.Name), property.Value, data);
                }
                // Like the JSON file provider: an empty object still shows up as a section.
                if (!hasProperties)
                    data[path] = null;
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                    Flatten(ConfigurationPath.Combine(path, index++.ToString()), item, data);
                if (index == 0)
                    data[path] = null;
                break;
            case JsonValueKind.String:
                data[path] = element.GetString();
                break;
            case JsonValueKind.Null:
                data[path] = null;
                break;
            default:
                // Numbers and booleans keep their JSON text ("3", "true"), which binds as usual.
                data[path] = element.GetRawText();
                break;
        }
    }

    private async Task<IReadOnlyList<ConfigItem>> FetchAllAsync(CancellationToken ct)
    {
        using var response = await Http.GetAsync(
            $"consumer/configurations?environment={Uri.EscapeDataString(_environment)}",
            ct
        );
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ConfigListResponse>(ct);
        return result?.Items ?? [];
    }

    /// <summary>One client for the provider's lifetime: polling reuses its connections.</summary>
    private HttpClient Http => LazyInitializer.EnsureInitialized(ref _http, CreateClient);

    private HttpClient CreateClient()
    {
        var http = _handler is null ? new HttpClient() : new HttpClient(_handler, false);
        http.BaseAddress = new Uri(_baseUrl + "/");
        http.DefaultRequestHeaders.Add("X-Api-Key", _apiKey);
        return http;
    }

    private sealed record ConfigItem(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("value")] string Value,
        [property: JsonPropertyName("label")] string Label,
        // Missing from servers older than value types: treated as "string".
        [property: JsonPropertyName("valueType")] string? ValueType = null
    );

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record ConfigListResponse(
        [property: JsonPropertyName("items")] List<ConfigItem> Items
    );
}
