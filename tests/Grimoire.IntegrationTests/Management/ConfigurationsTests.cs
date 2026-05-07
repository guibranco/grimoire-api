using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Grimoire.IntegrationTests.Management;

public class ConfigurationsTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private HttpClient Client => factory.CreateManagementClient();

    [Fact]
    public async Task ListConfigurations_ReturnsEmpty_Initially()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);

        var response = await Client.GetAsync($"/api/management/applications/{slug}/configurations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var arr = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray();
        Assert.Empty(arr);
    }

    [Fact]
    public async Task CreateConfiguration_Returns201_WithKeyAndValue()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var key = TestHelpers.UniqueName("Feature");

        var response = await Client.PostAsJsonAsync(
            $"/api/management/applications/{slug}/configurations",
            new { environmentSlug = "local", key, value = "true", description = "flag" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(key, json["key"]!.GetValue<string>());
        Assert.Equal("true", json["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateConfiguration_DuplicateKey_Returns409()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var key = TestHelpers.UniqueName("dup-key");

        await Client.PostAsJsonAsync($"/api/management/applications/{slug}/configurations",
            new { environmentSlug = "local", key, value = "v1" });
        var response = await Client.PostAsJsonAsync($"/api/management/applications/{slug}/configurations",
            new { environmentSlug = "local", key, value = "v2" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateConfiguration_Returns200_WithNewValue()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var key = TestHelpers.UniqueName("upd-key");

        await Client.PostAsJsonAsync($"/api/management/applications/{slug}/configurations",
            new { environmentSlug = "local", key, value = "old" });

        var response = await Client.PutAsJsonAsync(
            $"/api/management/applications/{slug}/configurations/local/{key}",
            new { value = "new" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal("new", json["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task DeleteConfiguration_Returns204()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var key = TestHelpers.UniqueName("del-key");

        await Client.PostAsJsonAsync($"/api/management/applications/{slug}/configurations",
            new { environmentSlug = "local", key, value = "v" });

        var response = await Client.DeleteAsync(
            $"/api/management/applications/{slug}/configurations/local/{key}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task UpdateConfiguration_NotFound_Returns404()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var response = await Client.PutAsJsonAsync(
            $"/api/management/applications/{slug}/configurations/local/no-key",
            new { value = "x" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateConfiguration_ForNonexistentEnvironment_Returns404()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var response = await Client.PostAsJsonAsync($"/api/management/applications/{slug}/configurations",
            new { environmentSlug = "no-env", key = "k", value = "v" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
