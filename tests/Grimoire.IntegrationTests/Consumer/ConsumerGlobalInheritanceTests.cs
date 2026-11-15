using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Grimoire.IntegrationTests.Consumer;

/// <summary>
/// Consumer-side merge of global configuration and secrets. Kept apart from the other consumer
/// test classes because globals apply to every app in the (per-class) database; every test here
/// uses unique keys and asserts on those keys only.
/// </summary>
public class ConsumerGlobalInheritanceTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private HttpClient MgmtClient => factory.CreateManagementClient();

    private static async Task<JsonArray> GetItemsAsync(HttpClient consumer, string environment)
    {
        var response = await consumer.GetAsync(
            $"/consumer/configurations?environment={environment}"
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!["items"]!.AsArray();
    }

    private static string? ValueOf(JsonArray items, string key) =>
        items
            .SingleOrDefault(i => i!["key"]!.GetValue<string>() == key)
            ?["value"]!.GetValue<string>();

    private async Task<(string slug, string apiKey)> CreateAppWithProductionAsync(
        bool? inheritGlobals = null
    )
    {
        var response = await MgmtClient.PostAsJsonAsync(
            "/management/applications",
            new { name = TestHelpers.UniqueName("app"), inheritGlobals }
        );
        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var slug = json["slug"]!.GetValue<string>();
        await TestHelpers.CreateEnvironmentAsync(MgmtClient, slug, "Production");
        await TestHelpers.CreateEnvironmentAsync(MgmtClient, slug, "Staging");
        return (slug, json["plainApiKey"]!.GetValue<string>());
    }

    [Fact]
    public async Task StarGlobal_IsReturnedToEveryInheritingApp()
    {
        var key = TestHelpers.UniqueName("LogStream:Url");
        await TestHelpers.CreateGlobalConfigurationAsync(MgmtClient, key, "https://logs");
        var (_, keyA) = await CreateAppWithProductionAsync();
        var (_, keyB) = await CreateAppWithProductionAsync();

        foreach (var apiKey in new[] { keyA, keyB })
        {
            var items = await GetItemsAsync(factory.CreateConsumerClient(apiKey), "production");
            Assert.Equal("https://logs", ValueOf(items, key));
            Assert.Equal(
                "production",
                items.Single(i => i!["key"]!.GetValue<string>() == key)![
                    "label"
                ]!.GetValue<string>()
            );
        }
    }

    [Fact]
    public async Task AppValue_OverridesGlobal_ForThatAppOnly()
    {
        var key = TestHelpers.UniqueName("Override");
        await TestHelpers.CreateGlobalConfigurationAsync(MgmtClient, key, "global");
        var (slugA, keyA) = await CreateAppWithProductionAsync();
        var (_, keyB) = await CreateAppWithProductionAsync();
        await TestHelpers.CreateConfigurationAsync(MgmtClient, slugA, "production", key, "app-a");

        var itemsA = await GetItemsAsync(factory.CreateConsumerClient(keyA), "production");
        var itemsB = await GetItemsAsync(factory.CreateConsumerClient(keyB), "production");

        Assert.Equal("app-a", ValueOf(itemsA, key));
        Assert.Single(itemsA, i => i!["key"]!.GetValue<string>() == key);
        Assert.Equal("global", ValueOf(itemsB, key));
    }

    [Fact]
    public async Task SlugGlobal_BeatsStar_AndDoesNotApplyToOtherEnvironments()
    {
        var key = TestHelpers.UniqueName("Tier");
        var prodOnly = TestHelpers.UniqueName("ProdOnly");
        await TestHelpers.CreateGlobalConfigurationAsync(MgmtClient, key, "star");
        await TestHelpers.CreateGlobalConfigurationAsync(MgmtClient, key, "prod", "production");
        await TestHelpers.CreateGlobalConfigurationAsync(MgmtClient, prodOnly, "x", "production");
        var (_, apiKey) = await CreateAppWithProductionAsync();
        var consumer = factory.CreateConsumerClient(apiKey);

        var prod = await GetItemsAsync(consumer, "production");
        var staging = await GetItemsAsync(consumer, "staging");

        Assert.Equal("prod", ValueOf(prod, key));
        Assert.Equal("x", ValueOf(prod, prodOnly));
        Assert.Equal("star", ValueOf(staging, key));
        Assert.Null(ValueOf(staging, prodOnly));
    }

    [Fact]
    public async Task GetByKey_FallsBackToGlobal()
    {
        var key = TestHelpers.UniqueName("Amqp:Uri");
        await TestHelpers.CreateGlobalConfigurationAsync(MgmtClient, key, "star");
        await TestHelpers.CreateGlobalConfigurationAsync(MgmtClient, key, "prod", "production");
        var (_, apiKey) = await CreateAppWithProductionAsync();
        var consumer = factory.CreateConsumerClient(apiKey);

        var prod = await consumer.GetAsync(
            $"/consumer/configurations/{Uri.EscapeDataString(key)}?environment=production"
        );
        var local = await consumer.GetAsync(
            $"/consumer/configurations/{Uri.EscapeDataString(key)}?environment=local"
        );

        Assert.Equal(HttpStatusCode.OK, prod.StatusCode);
        var prodJson = JsonNode.Parse(await prod.Content.ReadAsStringAsync())!;
        Assert.Equal("prod", prodJson["value"]!.GetValue<string>());
        Assert.Equal("production", prodJson["label"]!.GetValue<string>());
        var localJson = JsonNode.Parse(await local.Content.ReadAsStringAsync())!;
        Assert.Equal("star", localJson["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task InheritGlobalsFalse_SeesOnlyOwnValues()
    {
        var key = TestHelpers.UniqueName("NoInherit");
        await TestHelpers.CreateGlobalConfigurationAsync(MgmtClient, key, "global");
        var (slug, apiKey) = await CreateAppWithProductionAsync(inheritGlobals: false);
        var own = TestHelpers.UniqueName("Own");
        await TestHelpers.CreateConfigurationAsync(MgmtClient, slug, "production", own, "mine");
        var consumer = factory.CreateConsumerClient(apiKey);

        var items = await GetItemsAsync(consumer, "production");
        var byKey = await consumer.GetAsync(
            $"/consumer/configurations/{key}?environment=production"
        );

        Assert.Equal("mine", Assert.Single(items)!["value"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, byKey.StatusCode);
    }

    [Fact]
    public async Task TogglingInheritGlobals_TakesEffectImmediately()
    {
        var key = TestHelpers.UniqueName("Toggle");
        await TestHelpers.CreateGlobalConfigurationAsync(MgmtClient, key, "global");
        var (slug, apiKey) = await CreateAppWithProductionAsync();
        var consumer = factory.CreateConsumerClient(apiKey);
        Assert.Equal("global", ValueOf(await GetItemsAsync(consumer, "local"), key));

        var update = await MgmtClient.PutAsJsonAsync(
            $"/management/applications/{slug}",
            new { name = slug, inheritGlobals = false }
        );
        update.EnsureSuccessStatusCode();

        Assert.Null(ValueOf(await GetItemsAsync(consumer, "local"), key));
    }

    [Fact]
    public async Task Secret_FallsBackToGlobal_WhenAppHasNoSecretWithThatName()
    {
        var name = TestHelpers.UniqueName("amqp");
        await TestHelpers.CreateGlobalSecretAsync(
            MgmtClient,
            name,
            ("*", "star-value", true),
            ("production", "prod-value", true)
        );
        var (_, apiKey) = await CreateAppWithProductionAsync();
        var consumer = factory.CreateConsumerClient(apiKey);

        var prod = await consumer.GetAsync($"/consumer/secrets/{name}?environment=production");
        var staging = await consumer.GetAsync($"/consumer/secrets/{name}?environment=staging");

        Assert.Equal(HttpStatusCode.OK, prod.StatusCode);
        var prodJson = JsonNode.Parse(await prod.Content.ReadAsStringAsync())!;
        Assert.Equal(name, prodJson["name"]!.GetValue<string>());
        Assert.Equal("prod-value", prodJson["value"]!.GetValue<string>());
        Assert.True(prodJson["properties"]!["enabled"]!.GetValue<bool>());
        var stagingJson = JsonNode.Parse(await staging.Content.ReadAsStringAsync())!;
        Assert.Equal("star-value", stagingJson["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task Secret_AppSecretWithoutActiveVersion_DoesNotFallBackToGlobal()
    {
        var name = TestHelpers.UniqueName("disabled");
        await TestHelpers.CreateGlobalSecretAsync(MgmtClient, name, ("*", "global", true));
        var (slug, apiKey) = await CreateAppWithProductionAsync();
        await MgmtClient.PostAsJsonAsync($"/management/applications/{slug}/secrets", new { name });
        await MgmtClient.PostAsJsonAsync(
            $"/management/applications/{slug}/secrets/{name}/values",
            new[]
            {
                new
                {
                    environmentSlug = "production",
                    value = "app",
                    isEnabled = false,
                },
            }
        );
        var consumer = factory.CreateConsumerClient(apiKey);

        var disabled = await consumer.GetAsync($"/consumer/secrets/{name}?environment=production");
        var noVersion = await consumer.GetAsync($"/consumer/secrets/{name}?environment=local");

        Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noVersion.StatusCode);
    }

    [Fact]
    public async Task Secret_AppValue_BeatsGlobal()
    {
        var name = TestHelpers.UniqueName("shadow");
        await TestHelpers.CreateGlobalSecretAsync(MgmtClient, name, ("*", "global", true));
        var (slug, apiKey) = await CreateAppWithProductionAsync();
        await TestHelpers.CreateSecretWithValueAsync(MgmtClient, slug, name, "local", "app");

        var response = await factory
            .CreateConsumerClient(apiKey)
            .GetAsync($"/consumer/secrets/{name}?environment=local");

        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal("app", json["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task Secret_InheritGlobalsFalse_Returns404()
    {
        var name = TestHelpers.UniqueName("nope");
        await TestHelpers.CreateGlobalSecretAsync(MgmtClient, name, ("*", "global", true));
        var (_, apiKey) = await CreateAppWithProductionAsync(inheritGlobals: false);

        var response = await factory
            .CreateConsumerClient(apiKey)
            .GetAsync($"/consumer/secrets/{name}?environment=local");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EffectiveConfiguration_ReportsSourceAndOverrides()
    {
        var overridden = TestHelpers.UniqueName("Over");
        var inherited = TestHelpers.UniqueName("Inh");
        await TestHelpers.CreateGlobalConfigurationAsync(MgmtClient, overridden, "g");
        await TestHelpers.CreateGlobalConfigurationAsync(MgmtClient, inherited, "g", "production");
        var (slug, _) = await CreateAppWithProductionAsync();
        await TestHelpers.CreateConfigurationAsync(MgmtClient, slug, "production", overridden, "a");

        var response = await MgmtClient.GetAsync(
            $"/management/applications/{slug}/configurations/effective?environment=production"
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var arr = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray();
        var over = arr.Single(e => e!["key"]!.GetValue<string>() == overridden)!;
        Assert.Equal("a", over["value"]!.GetValue<string>());
        Assert.Equal("application", over["source"]!.GetValue<string>());
        Assert.True(over["overridesGlobal"]!.GetValue<bool>());
        Assert.Equal("*", over["globalEnvironmentSlug"]!.GetValue<string>());

        var inh = arr.Single(e => e!["key"]!.GetValue<string>() == inherited)!;
        Assert.Equal("global", inh["source"]!.GetValue<string>());
        Assert.False(inh["overridesGlobal"]!.GetValue<bool>());
        Assert.Equal("production", inh["globalEnvironmentSlug"]!.GetValue<string>());
    }

    [Fact]
    public async Task EffectiveConfiguration_UnknownEnvironment_Returns404()
    {
        var (slug, _) = await CreateAppWithProductionAsync();

        var response = await MgmtClient.GetAsync(
            $"/management/applications/{slug}/configurations/effective?environment=nope"
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EffectiveSecrets_ReportsSourceAndOverrides_WithoutValues()
    {
        var shared = TestHelpers.UniqueName("shared");
        var globalOnly = TestHelpers.UniqueName("gonly");
        await TestHelpers.CreateGlobalSecretAsync(MgmtClient, shared, ("*", "topsecret", true));
        await TestHelpers.CreateGlobalSecretAsync(MgmtClient, globalOnly, ("*", "topsecret", true));
        var (slug, _) = await CreateAppWithProductionAsync();
        await TestHelpers.CreateSecretWithValueAsync(
            MgmtClient,
            slug,
            shared,
            "local",
            "topsecret"
        );

        var response = await MgmtClient.GetAsync(
            $"/management/applications/{slug}/secrets/effective"
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("topsecret", body);
        var arr = JsonNode.Parse(body)!.AsArray();
        var s = arr.Single(e => e!["name"]!.GetValue<string>() == shared)!;
        Assert.Equal("application", s["source"]!.GetValue<string>());
        Assert.True(s["overridesGlobal"]!.GetValue<bool>());
        var g = arr.Single(e => e!["name"]!.GetValue<string>() == globalOnly)!;
        Assert.Equal("global", g["source"]!.GetValue<string>());
        Assert.False(g["overridesGlobal"]!.GetValue<bool>());
    }
}
