using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Grimoire.IntegrationTests.Consumer;

public class ConsumerSecretsTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private HttpClient MgmtClient => factory.CreateManagementClient();

    [Fact]
    public async Task GetSecret_Returns200_WithDecryptedValue()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);
        var secretName = TestHelpers.UniqueName("db-pass");
        await TestHelpers.CreateSecretWithValueAsync(MgmtClient, slug, secretName, "local", "s3cr3tP@ss!");

        var consumer = factory.CreateConsumerClient(apiKey);
        var response = await consumer.GetAsync($"/api/consumer/secrets/{secretName}?environment=local");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(secretName, json["name"]!.GetValue<string>());
        Assert.Equal("s3cr3tP@ss!", json["value"]!.GetValue<string>());
        Assert.NotNull(json["properties"]);
        Assert.True(json["properties"]!["enabled"]!.GetValue<bool>());
        Assert.Equal(1, json["properties"]!["version"]!.GetValue<int>());
    }

    [Fact]
    public async Task GetSecret_Returns404_WhenNoActiveVersion()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);
        var secretName = TestHelpers.UniqueName("no-val");
        await MgmtClient.PostAsJsonAsync($"/api/management/applications/{slug}/secrets",
            new { name = secretName });

        var consumer = factory.CreateConsumerClient(apiKey);
        var response = await consumer.GetAsync($"/api/consumer/secrets/{secretName}?environment=local");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSecret_Returns404_WhenSecretDoesNotExist()
    {
        var (_, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);

        var consumer = factory.CreateConsumerClient(apiKey);
        var response = await consumer.GetAsync("/api/consumer/secrets/does-not-exist?environment=local");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSecret_Returns404_WhenEnvironmentNotFound()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);
        var secretName = TestHelpers.UniqueName("env-miss");
        await TestHelpers.CreateSecretWithValueAsync(MgmtClient, slug, secretName, "local", "val");

        var consumer = factory.CreateConsumerClient(apiKey);
        var response = await consumer.GetAsync($"/api/consumer/secrets/{secretName}?environment=no-env");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSecret_Returns404_WhenVersionDisabled()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);
        var secretName = TestHelpers.UniqueName("disabled");

        await MgmtClient.PostAsJsonAsync($"/api/management/applications/{slug}/secrets",
            new { name = secretName });
        await MgmtClient.PostAsJsonAsync(
            $"/api/management/applications/{slug}/secrets/{secretName}/values",
            new[] { new { environmentSlug = "local", value = "x", isEnabled = false } });

        var consumer = factory.CreateConsumerClient(apiKey);
        var response = await consumer.GetAsync($"/api/consumer/secrets/{secretName}?environment=local");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSecret_Returns404_WhenExpired()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);
        var secretName = TestHelpers.UniqueName("expired");
        var pastDate = DateTimeOffset.UtcNow.AddDays(-1);

        await MgmtClient.PostAsJsonAsync($"/api/management/applications/{slug}/secrets",
            new { name = secretName });
        await MgmtClient.PostAsJsonAsync(
            $"/api/management/applications/{slug}/secrets/{secretName}/values",
            new[] { new { environmentSlug = "local", value = "x", isEnabled = true, expiresAt = pastDate } });

        var consumer = factory.CreateConsumerClient(apiKey);
        var response = await consumer.GetAsync($"/api/consumer/secrets/{secretName}?environment=local");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSecret_ReturnsLatestActiveVersion()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);
        var secretName = TestHelpers.UniqueName("latest");
        await TestHelpers.CreateSecretWithValueAsync(MgmtClient, slug, secretName, "local", "v1");
        await MgmtClient.PostAsJsonAsync(
            $"/api/management/applications/{slug}/secrets/{secretName}/values",
            new[] { new { environmentSlug = "local", value = "v2", isEnabled = true } });

        var consumer = factory.CreateConsumerClient(apiKey);
        var response = await consumer.GetAsync($"/api/consumer/secrets/{secretName}?environment=local");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal("v2", json["value"]!.GetValue<string>());
        Assert.Equal(2, json["properties"]!["version"]!.GetValue<int>());
    }
}
