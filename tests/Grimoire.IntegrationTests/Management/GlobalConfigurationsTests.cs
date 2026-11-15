using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Grimoire.IntegrationTests.Management;

public class GlobalConfigurationsTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private HttpClient Client => factory.CreateManagementClient();

    private static string Path(string envSlug, string key) =>
        $"/management/global/configurations/{envSlug}/{Uri.EscapeDataString(key)}";

    [Fact]
    public async Task Create_WithoutEnvironment_DefaultsToStar()
    {
        var key = TestHelpers.UniqueName("LogStream:Url");

        var response = await Client.PostAsJsonAsync(
            "/management/global/configurations",
            new
            {
                key,
                value = "https://logs",
                description = "shared",
            }
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal("*", json["environmentSlug"]!.GetValue<string>());
        Assert.Equal(key, json["key"]!.GetValue<string>());
        Assert.Equal("https://logs", json["value"]!.GetValue<string>());
        Assert.Equal("shared", json["description"]!.GetValue<string>());
        Assert.NotNull(json["id"]);
    }

    [Fact]
    public async Task Create_Duplicate_Returns409()
    {
        var key = TestHelpers.UniqueName("dup");
        await TestHelpers.CreateGlobalConfigurationAsync(Client, key, "a", "production");

        var response = await Client.PostAsJsonAsync(
            "/management/global/configurations",
            new
            {
                environmentSlug = "production",
                key,
                value = "b",
            }
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_SameKeyDifferentEnvironment_IsAllowed()
    {
        var key = TestHelpers.UniqueName("multi");
        await TestHelpers.CreateGlobalConfigurationAsync(Client, key, "star");

        var response = await Client.PostAsJsonAsync(
            "/management/global/configurations",
            new
            {
                environmentSlug = "production",
                key,
                value = "prod",
            }
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("not a slug")]
    public async Task Create_InvalidEnvironmentSlug_Returns400(string environmentSlug)
    {
        var response = await Client.PostAsJsonAsync(
            "/management/global/configurations",
            new
            {
                environmentSlug,
                key = "k",
                value = "v",
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_FiltersByEnvironment()
    {
        var key = TestHelpers.UniqueName("filter");
        await TestHelpers.CreateGlobalConfigurationAsync(Client, key, "star");
        await TestHelpers.CreateGlobalConfigurationAsync(Client, key, "stg", "staging");

        var all = JsonNode
            .Parse(await Client.GetStringAsync("/management/global/configurations"))!
            .AsArray();
        Assert.Equal(2, all.Count(e => e!["key"]!.GetValue<string>() == key));

        var staging = JsonNode
            .Parse(
                await Client.GetStringAsync("/management/global/configurations?environment=staging")
            )!
            .AsArray();
        Assert.All(staging, e =>
            Assert.Equal("staging", e!["environmentSlug"]!.GetValue<string>())
        );
        Assert.Single(staging, e => e!["key"]!.GetValue<string>() == key);

        var star = JsonNode
            .Parse(await Client.GetStringAsync("/management/global/configurations?environment=*"))!
            .AsArray();
        Assert.All(star, e => Assert.Equal("*", e!["environmentSlug"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Update_ChangesValue()
    {
        var key = TestHelpers.UniqueName("Upd:Key");
        await TestHelpers.CreateGlobalConfigurationAsync(Client, key, "old");

        var response = await Client.PutAsJsonAsync(
            Path("*", key),
            new { value = "new", description = "changed" }
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal("new", json["value"]!.GetValue<string>());
        Assert.Equal("changed", json["description"]!.GetValue<string>());
        Assert.Equal("*", json["environmentSlug"]!.GetValue<string>());
    }

    [Fact]
    public async Task Update_Missing_Returns404()
    {
        var response = await Client.PutAsJsonAsync(Path("*", "no-such-key"), new { value = "x" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Returns204_ThenUpdateReturns404()
    {
        var key = TestHelpers.UniqueName("del");
        await TestHelpers.CreateGlobalConfigurationAsync(Client, key, "v", "production");

        var del = await Client.DeleteAsync(Path("production", key));
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var again = await Client.DeleteAsync(Path("production", key));
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task Environments_ListsDistinctSlugs_WithApplicationCount()
    {
        var envName = TestHelpers.UniqueName("Region");
        var (slugA, _) = await TestHelpers.CreateApplicationAsync(Client);
        var (slugB, _) = await TestHelpers.CreateApplicationAsync(Client);
        var (slugDeleted, _) = await TestHelpers.CreateApplicationAsync(Client);
        var envSlug = await TestHelpers.CreateEnvironmentAsync(Client, slugA, envName);
        await TestHelpers.CreateEnvironmentAsync(Client, slugB, envName);
        await TestHelpers.CreateEnvironmentAsync(Client, slugDeleted, envName);
        await Client.DeleteAsync($"/management/applications/{slugDeleted}");

        var response = await Client.GetAsync("/management/global/environments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var arr = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray();
        var env = Assert.Single(arr, e => e!["slug"]!.GetValue<string>() == envSlug);
        Assert.Equal(envName, env!["name"]!.GetValue<string>());
        Assert.Equal(2, env["applicationCount"]!.GetValue<int>());
        Assert.Contains(arr, e => e!["slug"]!.GetValue<string>() == "local");
    }
}
