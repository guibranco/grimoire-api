using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Grimoire.IntegrationTests.Management;

public class EnvironmentsTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private HttpClient Client => factory.CreateManagementClient();

    [Fact]
    public async Task ListEnvironments_IncludesAutoCreatedLocal()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);

        var response = await Client.GetAsync($"/api/management/applications/{slug}/environments");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envs = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray();
        Assert.True(envs.Count >= 1);
        Assert.Contains(envs, e => e!["slug"]!.GetValue<string>() == "local");
    }

    [Fact]
    public async Task CreateEnvironment_Returns201_WithGeneratedSlug()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);

        var response = await Client.PostAsJsonAsync(
            $"/api/management/applications/{slug}/environments",
            new { name = "Production" }
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal("production", json["slug"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateEnvironment_DuplicateSlug_Returns409()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);

        await Client.PostAsJsonAsync(
            $"/api/management/applications/{slug}/environments",
            new { name = "Staging" }
        );
        var response = await Client.PostAsJsonAsync(
            $"/api/management/applications/{slug}/environments",
            new { name = "Staging" }
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DeleteEnvironment_Returns204()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        await Client.PostAsJsonAsync(
            $"/api/management/applications/{slug}/environments",
            new { name = "Temp-Env" }
        );

        var response = await Client.DeleteAsync(
            $"/api/management/applications/{slug}/environments/temp-env"
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ListEnvironments_ForNonExistentApp_Returns404()
    {
        var response = await Client.GetAsync(
            "/api/management/applications/no-app-xyz/environments"
        );
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteEnvironment_NotFound_Returns404()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var response = await Client.DeleteAsync(
            $"/api/management/applications/{slug}/environments/no-such-env"
        );
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
