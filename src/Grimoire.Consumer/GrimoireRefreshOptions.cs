namespace Grimoire.Consumer;

/// <summary>
/// How a Grimoire configuration provider watches for changes, modelled on Azure App
/// Configuration's sentinel refresh. Every <see cref="RefreshInterval"/> (plus up to 20 %
/// random jitter) the provider makes a conditional request for each watched key; unchanged
/// keys cost a <c>304</c>. When a key registered with <c>refreshAll: true</c> changes, the
/// provider reloads every value and raises the configuration reload token, so
/// <c>IOptionsMonitor&lt;T&gt;</c> and <c>IConfiguration</c> change callbacks fire.
/// <para>
/// By default the provider watches <c>Sentinel</c> (the application's) and
/// <c>Global:Sentinel</c> (inherited globals), both with <c>refreshAll: true</c>. Grimoire
/// bumps them on every change while auto-publish is on, or when changes are published.
/// </para>
/// </summary>
public sealed class GrimoireRefreshOptions
{
    /// <summary>The application sentinel key.</summary>
    public const string SentinelKey = "Sentinel";

    /// <summary>The global sentinel key.</summary>
    public const string GlobalSentinelKey = "Global:Sentinel";

    /// <summary>Default time between checks.</summary>
    public static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromSeconds(30);

    /// <summary>Shortest allowed interval.</summary>
    public static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromSeconds(1);

    /// <summary>Longest wait between attempts while Grimoire is failing.</summary>
    public static readonly TimeSpan MaximumBackoff = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, bool> _watched = new(StringComparer.Ordinal);

    /// <summary>Time between checks (default 30 seconds).</summary>
    public TimeSpan RefreshInterval { get; private set; } = DefaultRefreshInterval;

    /// <summary>
    /// Keys being watched, and whether a change reloads everything (<c>true</c>) or only that
    /// key. Empty means the defaults: <c>Sentinel</c> and <c>Global:Sentinel</c>, reloading all.
    /// </summary>
    public IReadOnlyDictionary<string, bool> WatchedKeys =>
        _watched.Count > 0
            ? _watched
            : new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                [SentinelKey] = true,
                [GlobalSentinelKey] = true,
            };

    /// <summary>
    /// Watches <paramref name="key"/>. With <paramref name="refreshAll"/> a change reloads every
    /// value; without it only <paramref name="key"/> is updated. Registering any key replaces
    /// the default sentinel pair, so register both sentinels if you still want them.
    /// </summary>
    public GrimoireRefreshOptions Register(string key, bool refreshAll = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _watched[key] = refreshAll;
        return this;
    }

    /// <summary>Sets the time between checks (at least one second).</summary>
    public GrimoireRefreshOptions SetRefreshInterval(TimeSpan interval)
    {
        if (interval < MinimumRefreshInterval)
            throw new ArgumentOutOfRangeException(
                nameof(interval),
                interval,
                $"The refresh interval must be at least {MinimumRefreshInterval.TotalSeconds:0} second."
            );
        RefreshInterval = interval;
        return this;
    }
}

/// <summary>Options for <c>AddGrimoire(o =&gt; …)</c>.</summary>
public sealed class GrimoireConfigurationOptions
{
    internal string? BaseUrl { get; private set; }
    internal string? ApiKey { get; private set; }
    internal string? Environment { get; private set; }
    internal GrimoireRefreshOptions? Refresh { get; private set; }

    /// <summary>Where Grimoire is, the application's API key and the environment to load.</summary>
    public GrimoireConfigurationOptions Connect(string baseUrl, string apiKey, string environment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);
        BaseUrl = baseUrl;
        ApiKey = apiKey;
        Environment = environment;
        return this;
    }

    /// <summary>
    /// Reloads configuration when Grimoire publishes changes. Without this call, values are
    /// loaded once at startup.
    /// </summary>
    public GrimoireConfigurationOptions ConfigureRefresh(
        Action<GrimoireRefreshOptions>? configure = null
    )
    {
        Refresh ??= new GrimoireRefreshOptions();
        configure?.Invoke(Refresh);
        return this;
    }

    internal void Validate()
    {
        if (BaseUrl is null || ApiKey is null || Environment is null)
            throw new InvalidOperationException(
                "Call Connect(baseUrl, apiKey, environment) when configuring AddGrimoire."
            );
    }
}
