namespace Grimoire.Consumer.FeatureFlags;

/// <summary>Settings for <see cref="GrimoireFeatureFlagClient"/>.</summary>
public sealed class GrimoireFeatureFlagOptions
{
    /// <summary>Grimoire's base URL, e.g. <c>https://grimoire.example.com</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>The application's Consumer API key.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Environment slug whose flag state is used, e.g. <c>production</c>.</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>
    /// How long fetched definitions are used before they are refreshed in the background
    /// (conditional request, so unchanged flags cost a <c>304</c>). Default 30 seconds.
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
            throw new ArgumentException("BaseUrl is required.", nameof(BaseUrl));
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new ArgumentException("ApiKey is required.", nameof(ApiKey));
        if (string.IsNullOrWhiteSpace(Environment))
            throw new ArgumentException("Environment is required.", nameof(Environment));
        if (RefreshInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RefreshInterval), "Must be positive.");
    }
}
