using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Grimoire.E2eTests;

[Collection("E2E")]
public class E2eTests(GrimoireApiFixture fixture) : IClassFixture<GrimoireApiFixture>
{
    private static string UniqueName(string prefix = "e2e") => $"{prefix}-{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task HealthEndpoint_Returns200()
    {
        var response = await fixture.HttpClient.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FullLifecycle_CreateApp_SetSecret_ConsumeSecret()
    {
        var mgmt = fixture.CreateManagementClient();
        var appName = UniqueName("e2e-app");

        // Create application
        var createResp = await mgmt.PostAsJsonAsync(
            "/api/management/applications",
            new { name = appName, description = "E2E test" }
        );
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var app = JsonNode.Parse(await createResp.Content.ReadAsStringAsync())!;
        var slug = app["slug"]!.GetValue<string>();
        var apiKey = app["plainApiKey"]!.GetValue<string>();

        // Create secret with value
        var secretName = UniqueName("db-pass");
        await mgmt.PostAsJsonAsync(
            $"/api/management/applications/{slug}/secrets",
            new { name = secretName }
        );
        var setVal = await mgmt.PostAsJsonAsync(
            $"/api/management/applications/{slug}/secrets/{secretName}/values",
            new[]
            {
                new
                {
                    environmentSlug = "local",
                    value = "e2e-secret-value",
                    isEnabled = true,
                },
            }
        );
        Assert.Equal(HttpStatusCode.OK, setVal.StatusCode);

        // Consume secret via Consumer API
        var consumer = fixture.CreateConsumerClient(apiKey);
        var secretResp = await consumer.GetAsync(
            $"/api/consumer/secrets/{secretName}?environment=local"
        );
        Assert.Equal(HttpStatusCode.OK, secretResp.StatusCode);
        var secretJson = JsonNode.Parse(await secretResp.Content.ReadAsStringAsync())!;
        Assert.Equal("e2e-secret-value", secretJson["value"]!.GetValue<string>());
        Assert.True(secretJson["properties"]!["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task FullLifecycle_CreateApp_SetConfig_ConsumeConfig()
    {
        var mgmt = fixture.CreateManagementClient();
        var appName = UniqueName("cfg-app");

        var createResp = await mgmt.PostAsJsonAsync(
            "/api/management/applications",
            new { name = appName }
        );
        var app = JsonNode.Parse(await createResp.Content.ReadAsStringAsync())!;
        var slug = app["slug"]!.GetValue<string>();
        var apiKey = app["plainApiKey"]!.GetValue<string>();

        var key = UniqueName("Feature");
        await mgmt.PostAsJsonAsync(
            $"/api/management/applications/{slug}/configurations",
            new
            {
                environmentSlug = "local",
                key,
                value = "true",
            }
        );

        var consumer = fixture.CreateConsumerClient(apiKey);
        var cfgResp = await consumer.GetAsync(
            $"/api/consumer/configurations/{key}?environment=local"
        );
        Assert.Equal(HttpStatusCode.OK, cfgResp.StatusCode);
        var cfgJson = JsonNode.Parse(await cfgResp.Content.ReadAsStringAsync())!;
        Assert.Equal("true", cfgJson["value"]!.GetValue<string>());
        Assert.Equal("local", cfgJson["label"]!.GetValue<string>());
    }

    [Fact]
    public async Task ManagementApi_RequiresAuthentication()
    {
        var response = await fixture.HttpClient.GetAsync("/api/management/applications");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ConsumerApi_RequiresApiKey()
    {
        var response = await fixture.HttpClient.GetAsync(
            "/api/consumer/secrets/anything?environment=local"
        );
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RotateKey_OldKeyInvalid_NewKeyWorks()
    {
        var mgmt = fixture.CreateManagementClient();
        var (slug, oldKey) = await CreateApplicationAsync(mgmt);

        var rotateResp = await mgmt.PostAsync(
            $"/api/management/applications/{slug}/rotate-key",
            null
        );
        var rotateJson = JsonNode.Parse(await rotateResp.Content.ReadAsStringAsync())!;
        var newKey = rotateJson["plainApiKey"]!.GetValue<string>();

        // Old key should no longer work
        var oldConsumer = fixture.CreateConsumerClient(oldKey);
        var oldResp = await oldConsumer.GetAsync("/api/consumer/configurations?environment=local");
        Assert.Equal(HttpStatusCode.Unauthorized, oldResp.StatusCode);

        // New key should work
        var newConsumer = fixture.CreateConsumerClient(newKey);
        var newResp = await newConsumer.GetAsync("/api/consumer/configurations?environment=local");
        Assert.Equal(HttpStatusCode.OK, newResp.StatusCode);
    }

    [Fact]
    public async Task DeleteApplication_RemovesAccess()
    {
        var mgmt = fixture.CreateManagementClient();
        var (slug, _) = await CreateApplicationAsync(mgmt);

        await mgmt.DeleteAsync($"/api/management/applications/{slug}");

        var getResp = await mgmt.GetAsync($"/api/management/applications/{slug}");
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    private static async Task<(string slug, string apiKey)> CreateApplicationAsync(HttpClient mgmt)
    {
        var name = UniqueName("e2e-lifecycle");
        var resp = await mgmt.PostAsJsonAsync("/api/management/applications", new { name });
        resp.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync())!;
        return (json["slug"]!.GetValue<string>(), json["plainApiKey"]!.GetValue<string>());
    }
}
