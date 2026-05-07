using Grimoire.Consumer;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Grimoire.Tests;

public class ConfigurationProviderTests
{
    [Fact]
    public void AddGrimoire_RegistersSource()
    {
        var builder = new ConfigurationBuilder();
        builder.AddGrimoire("http://localhost:5000", "test-key", "local");

        var sources = builder.Sources;
        Assert.Contains(sources, s => s is GrimoireConfigurationClient);
    }

    [Fact]
    public void GrimoireConfigurationClient_ImplementsIConfigurationSource()
    {
        var client = new GrimoireConfigurationClient("http://localhost:5000", "key", "local");
        Assert.IsAssignableFrom<IConfigurationSource>(client);
    }

    [Fact]
    public void GrimoireConfigurationClient_BuildReturnsSelf()
    {
        var client = new GrimoireConfigurationClient("http://localhost:5000", "key", "local");
        var builder = new ConfigurationBuilder();
        var provider = client.Build(builder);
        Assert.Same(client, provider);
    }
}
