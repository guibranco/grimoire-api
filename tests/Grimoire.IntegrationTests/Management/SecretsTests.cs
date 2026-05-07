using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Grimoire.IntegrationTests.Management;

public class SecretsTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private HttpClient Client => factory.CreateManagementClient();

    [Fact]
    public async Task ListSecrets_ReturnsEmptyList_Initially()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);

        var response = await Client.GetAsync($"/api/management/applications/{slug}/secrets");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var arr = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray();
        Assert.Empty(arr);
    }

    [Fact]
    public async Task CreateSecret_Returns201_WithRequiredEnvironments()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var secretName = TestHelpers.UniqueName("secret");

        var response = await Client.PostAsJsonAsync(
            $"/api/management/applications/{slug}/secrets",
            new { name = secretName, description = "test secret" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(secretName, json["name"]!.GetValue<string>());
        var requiredEnvs = json["requiredEnvironments"]!.AsArray();
        Assert.True(requiredEnvs.Count >= 1);
        Assert.All(requiredEnvs, e => Assert.False(e!["valueProvided"]!.GetValue<bool>()));
    }

    [Fact]
    public async Task CreateSecret_Duplicate_Returns409()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var name = TestHelpers.UniqueName("dup-secret");

        await Client.PostAsJsonAsync($"/api/management/applications/{slug}/secrets",
            new { name });
        var response = await Client.PostAsJsonAsync($"/api/management/applications/{slug}/secrets",
            new { name });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SetSecretValue_And_GetMetadata_Works()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var secretName = TestHelpers.UniqueName("sec");

        await TestHelpers.CreateSecretWithValueAsync(Client, slug, secretName, "local", "s3cr3t!");

        var response = await Client.GetAsync($"/api/management/applications/{slug}/secrets/{secretName}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(secretName, json["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task SetSecretValue_ForNonexistentEnvironment_Returns404()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var secretName = TestHelpers.UniqueName("sec");
        await Client.PostAsJsonAsync($"/api/management/applications/{slug}/secrets",
            new { name = secretName });

        var response = await Client.PostAsJsonAsync(
            $"/api/management/applications/{slug}/secrets/{secretName}/values",
            new[] { new { environmentSlug = "no-such-env", value = "x", isEnabled = true } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetVersionHistory_ReturnsVersions_WithoutValues()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var secretName = TestHelpers.UniqueName("ver");
        await TestHelpers.CreateSecretWithValueAsync(Client, slug, secretName, "local", "v1");

        var response = await Client.GetAsync(
            $"/api/management/applications/{slug}/secrets/{secretName}/versions/local");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var arr = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray();
        Assert.Single(arr);
        Assert.Equal(1, arr[0]!["version"]!.GetValue<int>());
        Assert.Null(arr[0]!["value"]);
    }

    [Fact]
    public async Task GetVersionHistory_IncrementsVersion_OnMultipleValueSets()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var secretName = TestHelpers.UniqueName("multi");
        await TestHelpers.CreateSecretWithValueAsync(Client, slug, secretName, "local", "v1");

        await Client.PostAsJsonAsync(
            $"/api/management/applications/{slug}/secrets/{secretName}/values",
            new[] { new { environmentSlug = "local", value = "v2", isEnabled = true } });

        var response = await Client.GetAsync(
            $"/api/management/applications/{slug}/secrets/{secretName}/versions/local");
        var arr = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray();
        Assert.Equal(2, arr.Count);
    }

    [Fact]
    public async Task DeleteSecret_Returns204_AndThenNotFound()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var name = TestHelpers.UniqueName("del");
        await Client.PostAsJsonAsync($"/api/management/applications/{slug}/secrets", new { name });

        var del = await Client.DeleteAsync($"/api/management/applications/{slug}/secrets/{name}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var get = await Client.GetAsync($"/api/management/applications/{slug}/secrets/{name}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task GetSecret_NotFound_Returns404()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var response = await Client.GetAsync($"/api/management/applications/{slug}/secrets/no-such-secret");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
