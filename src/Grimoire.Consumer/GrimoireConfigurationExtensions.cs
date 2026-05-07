using Microsoft.Extensions.Configuration;

namespace Grimoire.Consumer;

public static class GrimoireConfigurationExtensions
{
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
}
