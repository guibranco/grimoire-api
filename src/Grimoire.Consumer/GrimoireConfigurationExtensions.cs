using Microsoft.Extensions.Configuration;

namespace Grimoire.Consumer;

public static class GrimoireConfigurationExtensions
{
    /// <summary>Loads every configuration value of <paramref name="environment"/> once, at startup.</summary>
    public static IConfigurationBuilder AddGrimoire(
        this IConfigurationBuilder builder,
        string baseUrl,
        string apiKey,
        string environment
    )
    {
        builder.Add(new GrimoireConfigurationClient(baseUrl, apiKey, environment));
        return builder;
    }

    /// <summary>
    /// Loads configuration from Grimoire with options, for example reloading when changes are
    /// published:
    /// <code>
    /// builder.Configuration.AddGrimoire(o => o
    ///     .Connect(url, apiKey, "production")
    ///     .ConfigureRefresh(r => r.SetRefreshInterval(TimeSpan.FromSeconds(30))));
    /// </code>
    /// </summary>
    public static IConfigurationBuilder AddGrimoire(
        this IConfigurationBuilder builder,
        Action<GrimoireConfigurationOptions> configure
    )
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new GrimoireConfigurationOptions();
        configure(options);
        options.Validate();
        builder.Add(
            new GrimoireConfigurationClient(
                options.BaseUrl!,
                options.ApiKey!,
                options.Environment!,
                options.Refresh
            )
        );
        return builder;
    }
}
