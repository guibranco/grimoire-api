using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Grimoire.IntegrationTests.Consumer;

public class ConsumerConfigurationsTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private HttpClient MgmtClient => factory.CreateManagementClient();

    [Fact]
    public async Task GetAllConfigurations_ReturnsItems_WithEnvironmentLabel()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);
        var key = TestHelpers.UniqueName("Feature");
        await MgmtClient.PostAsJsonAsync($"/api/management/applications/{slug}/configurations",
            new { environmentSlug = "local", key, value = "true" });

        var consumer = factory.CreateConsumerClient(apiKey);
        var response = await consumer.GetAsync("/api/consumer/configurations?environment=local");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var items = json["items"]!.AsArray();
        Assert.Contains(items, i =>
            i!["key"]!.GetValue<string>() == key &&
            i["value"]!.GetValue<string>() == "true" &&
            i["label"]!.GetValue<string>() == "local");
    }

    [Fact]
    public async Task GetAllConfigurations_ReturnsEmpty_WhenNoEntries()
    {
        var (_, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);

        var consumer = factory.CreateConsumerClient(apiKey);
        var response = await consumer.GetAsync("/api/consumer/configurations?environment=local");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Empty(json["items"]!.AsArray());
    }

    [Fact]
    public async Task GetConfigurationByKey_Returns200_WithValue()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);
        var key = TestHelpers.UniqueName("conn-str");
        await MgmtClient.PostAsJsonAsync($"/api/management/applications/{slug}/configurations",
            new { environmentSlug = "local", key, value = "Server=localhost" });

        var consumer = factory.CreateConsumerClient(apiKey);
        var response = await consumer.GetAsync($"/api/consumer/configurations/{key}?environment=local");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(key, json["key"]!.GetValue<string>());
        Assert.Equal("Server=localhost", json["value"]!.GetValue<string>());
        Assert.Equal("local", json["label"]!.GetValue<string>());
    }

    [Fact]
    public async Task GetConfigurationByKey_Returns404_WhenNotFound()
    {
        var (_, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);

        var consumer = factory.CreateConsumerClient(apiKey);
        var response = await consumer.GetAsync("/api/consumer/configurations/no-such-key?environment=local");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAllConfigurations_Returns404_ForUnknownEnvironment()
    {
        var (_, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);

        var consumer = factory.CreateConsumerClient(apiKey);
        var response = await consumer.GetAsync("/api/consumer/configurations?environment=no-env");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
