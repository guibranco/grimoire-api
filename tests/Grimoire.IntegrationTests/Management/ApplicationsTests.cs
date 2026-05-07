using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Grimoire.IntegrationTests.Management;

public class ApplicationsTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private HttpClient Client => factory.CreateManagementClient();

    [Fact]
    public async Task GetApplications_ReturnsEmptyListInitially()
    {
        var response = await Client.GetAsync("/api/management/applications");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray();
        Assert.NotNull(json);
    }

    [Fact]
    public async Task CreateApplication_Returns201_WithPlainApiKey()
    {
        var name = TestHelpers.UniqueName("create-app");
        var response = await Client.PostAsJsonAsync("/api/management/applications",
            new { name, description = "Integration test app" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.NotNull(json["plainApiKey"]?.GetValue<string>());
        Assert.StartsWith("grm_", json["plainApiKey"]!.GetValue<string>());
        Assert.NotNull(json["slug"]?.GetValue<string>());
    }

    [Fact]
    public async Task CreateApplication_AutoGeneratesSlugFromName()
    {
        var response = await Client.PostAsJsonAsync("/api/management/applications",
            new { name = "My Test Application", description = "" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal("my-test-application", json["slug"]!.GetValue<string>());
    }

    [Fact]
    public async Task GetApplicationBySlug_Returns200_WhenExists()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);

        var response = await Client.GetAsync($"/api/management/applications/{slug}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(slug, json["slug"]!.GetValue<string>());
    }

    [Fact]
    public async Task GetApplicationBySlug_Returns404_WhenNotFound()
    {
        var response = await Client.GetAsync("/api/management/applications/nonexistent-slug-xyz");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateApplication_Returns200_WithUpdatedName()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var newName = TestHelpers.UniqueName("updated");

        var response = await Client.PutAsJsonAsync($"/api/management/applications/{slug}",
            new { name = newName, description = "updated desc" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(newName, json["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task DeleteApplication_Returns204_AndThenNotFound()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);

        var deleteResponse = await Client.DeleteAsync($"/api/management/applications/{slug}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await Client.GetAsync($"/api/management/applications/{slug}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task RotateKey_Returns200_WithNewPlainKey()
    {
        var (slug, originalKey) = await TestHelpers.CreateApplicationAsync(Client);

        var response = await Client.PostAsync($"/api/management/applications/{slug}/rotate-key", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var newKey = json["plainApiKey"]!.GetValue<string>();
        Assert.NotNull(newKey);
        Assert.NotEqual(originalKey, newKey);
    }

    [Fact]
    public async Task CreateApplication_SeedsLocalEnvironmentAutomatically()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);

        var response = await Client.GetAsync($"/api/management/applications/{slug}/environments");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envs = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray();
        Assert.Contains(envs, e => e!["slug"]!.GetValue<string>() == "local");
    }

    [Fact]
    public async Task CreateApplication_DuplicateName_Returns409()
    {
        var name = TestHelpers.UniqueName("dup-app");
        await Client.PostAsJsonAsync("/api/management/applications", new { name });

        var response = await Client.PostAsJsonAsync("/api/management/applications", new { name });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
