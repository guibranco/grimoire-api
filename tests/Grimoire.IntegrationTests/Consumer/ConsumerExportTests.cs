using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Grimoire.Core.Audit;

namespace Grimoire.IntegrationTests.Consumer;

public class ConsumerExportTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private HttpClient Mgmt => factory.CreateManagementClient();

    // Fixture values are built at runtime so secret scanners don't take them for credentials.
    private static readonly string SecretValue = string.Join('-', "not", "a", "real", "value");

    /// <summary>An application with a secret that has a value, one without, and two configuration entries in <c>local</c>.</summary>
    private async Task<(
        string Slug,
        string ApiKey,
        string WithValue,
        string WithoutValue
    )> SeedAsync()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Mgmt);
        var withValue = TestHelpers.UniqueName("api-key");
        var withoutValue = TestHelpers.UniqueName("unset");
        await TestHelpers.CreateSecretWithValueAsync(Mgmt, slug, withValue, "local", SecretValue);
        (
            await Mgmt.PostAsJsonAsync(
                $"/management/applications/{slug}/secrets",
                new { name = withoutValue }
            )
        ).EnsureSuccessStatusCode();
        await TestHelpers.CreateConfigurationAsync(Mgmt, slug, "local", "Feature:DarkMode", "true");
        await TestHelpers.CreateConfigurationAsync(Mgmt, slug, "local", "Greeting", "it's $HOME");
        return (slug, apiKey, withValue, withoutValue);
    }

    [Fact]
    public async Task ListSecrets_ReturnsNamesAndMetadata_NeverValues()
    {
        var (_, apiKey, withValue, withoutValue) = await SeedAsync();
        var global = TestHelpers.UniqueName("global");
        await TestHelpers.CreateGlobalSecretAsync(Mgmt, global, ("*", SecretValue, true));

        var response = await factory
            .CreateConsumerClient(apiKey)
            .GetAsync("/consumer/secrets?environment=local");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(SecretValue, text);
        var items = JsonNode.Parse(text)!["items"]!
            .AsArray()
            .ToDictionary(i => i!["name"]!.GetValue<string>(), i => i!);
        Assert.True(items[withValue]["active"]!.GetValue<bool>());
        Assert.Equal(1, items[withValue]["version"]!.GetValue<int>());
        Assert.Equal("application", items[withValue]["source"]!.GetValue<string>());
        Assert.False(items[withoutValue]["active"]!.GetValue<bool>());
        Assert.Null(items[withoutValue]["version"]);
        Assert.Equal("global", items[global]["source"]!.GetValue<string>());
    }

    [Fact]
    public async Task Export_Json_ReturnsValues_ListsSkippedSecrets_AndIsAudited()
    {
        var (slug, apiKey, withValue, withoutValue) = await SeedAsync();

        var response = await factory
            .CreateConsumerClient(apiKey)
            .GetAsync("/consumer/export?environment=local");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(SecretValue, json["secrets"]![withValue]!.GetValue<string>());
        Assert.Null(json["secrets"]![withoutValue]);
        Assert.Contains(
            withoutValue,
            json["skipped"]!.AsArray().Select(n => n!.GetValue<string>())
        );
        Assert.Equal("true", json["configurations"]!["Feature:DarkMode"]!.GetValue<string>());

        var audit = await Mgmt.WaitForAuditAsync(
            $"application={slug}&action={AuditActions.ExportRead}"
        );
        var item = Assert.Single(audit);
        Assert.Equal("json", item["target"]!.GetValue<string>());
        Assert.DoesNotContain(SecretValue, item.ToJsonString());
    }

    [Fact]
    public async Task Export_Env_NormalisesKeys_AndQuotesValues()
    {
        var (_, apiKey, withValue, _) = await SeedAsync();

        var response = await factory
            .CreateConsumerClient(apiKey)
            .GetAsync("/consumer/export?environment=local&format=env");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
        var lines = (await response.Content.ReadAsStringAsync()).Split('\n');
        Assert.Contains("FEATURE__DARKMODE='true'", lines);
        Assert.Contains("GREETING=\"it's \\$HOME\"", lines);
        Assert.Contains($"{withValue.ToUpperInvariant().Replace('-', '_')}='{SecretValue}'", lines);
    }

    [Fact]
    public async Task Export_Dotnet_IsAFlatObject_WithPrefixFilter()
    {
        var (_, apiKey, withValue, _) = await SeedAsync();

        var response = await factory
            .CreateConsumerClient(apiKey)
            .GetAsync("/consumer/export?environment=local&format=dotnet&prefix=feature:");

        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.Equal(["Feature:DarkMode"], json.Select(kv => kv.Key));
        Assert.Null(json[withValue]);
    }

    [Fact]
    public async Task Export_ConfigurationsOnly_SupportsIfNoneMatch()
    {
        var (_, apiKey, _, _) = await SeedAsync();
        var consumer = factory.CreateConsumerClient(apiKey);
        const string url = "/consumer/export?environment=local&include=configurations";

        var first = await consumer.GetAsync(url);
        var etag = first.Headers.ETag;
        Assert.NotNull(etag);
        Assert.Empty(
            JsonNode.Parse(await first.Content.ReadAsStringAsync())!["secrets"]!.AsObject()
        );

        using var conditional = new HttpRequestMessage(HttpMethod.Get, url);
        conditional.Headers.IfNoneMatch.Add(etag);
        Assert.Equal(
            HttpStatusCode.NotModified,
            (await consumer.SendAsync(conditional)).StatusCode
        );

        var withSecrets = await consumer.GetAsync("/consumer/export?environment=local");
        Assert.Null(withSecrets.Headers.ETag);
    }

    [Theory]
    [InlineData("format=yaml", HttpStatusCode.BadRequest)]
    [InlineData("include=flags", HttpStatusCode.BadRequest)]
    [InlineData("environment=nope", HttpStatusCode.NotFound)]
    public async Task Export_RejectsBadRequests(string query, HttpStatusCode expected)
    {
        var (_, apiKey) = await TestHelpers.CreateApplicationAsync(Mgmt);
        var url = query.StartsWith("environment")
            ? $"/consumer/export?{query}"
            : $"/consumer/export?environment=local&{query}";

        var response = await factory.CreateConsumerClient(apiKey).GetAsync(url);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Export_WithAnAccessToken_NeedsEveryIncludedScope()
    {
        var (slug, _, _, _) = await SeedAsync();
        var created = await Mgmt.PostAsJsonAsync(
            $"/management/applications/{slug}/access-tokens",
            new { name = "config-reader", scopes = new[] { "configurations" } }
        );
        created.EnsureSuccessStatusCode();
        var token = JsonNode.Parse(await created.Content.ReadAsStringAsync())![
            "plainToken"
        ]!.GetValue<string>();
        var consumer = factory.CreateConsumerClient(token);

        Assert.Equal(
            HttpStatusCode.OK,
            (
                await consumer.GetAsync("/consumer/export?environment=local&include=configurations")
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await consumer.GetAsync("/consumer/export?environment=local")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await consumer.GetAsync("/consumer/secrets?environment=local")).StatusCode
        );
    }
}
