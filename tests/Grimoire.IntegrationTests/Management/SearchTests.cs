using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Grimoire.IntegrationTests.Management;

public class SearchTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private HttpClient Client => factory.CreateManagementClient();

    /// <summary>A token unique to the test, so hits from other tests in the shared DB don't leak in.</summary>
    private static string Token() => $"zq{Guid.NewGuid():N}"[..10];

    private async Task<JsonNode> SearchAsync(string q, int? limit = null)
    {
        var url =
            $"/management/search?q={Uri.EscapeDataString(q)}"
            + (limit is null ? "" : $"&limit={limit}");
        var response = await Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    private static JsonArray Results(JsonNode json) => json["results"]!.AsArray();

    [Fact]
    public async Task Search_WithoutAdminKey_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/management/search?q=abc");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("q=a")]
    [InlineData("q=%20%20")]
    [InlineData("")]
    [InlineData("q=abc&limit=0")]
    [InlineData("q=abc&limit=51")]
    public async Task Search_WithInvalidQuery_Returns400(string query)
    {
        var response = await Client.GetAsync($"/management/search?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_FindsEveryKind_WithLocationsAndNoValues()
    {
        var token = Token();
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client, $"{token} App");
        var prod = await TestHelpers.CreateEnvironmentAsync(Client, slug, "production");
        await TestHelpers.CreateSecretWithValueAsync(
            Client,
            slug,
            $"{token}_PASSWORD",
            prod,
            "VALUE-MARKER-secret"
        );
        await TestHelpers.CreateConfigurationAsync(
            Client,
            slug,
            prod,
            $"{token}_HOST",
            "VALUE-MARKER-host"
        );
        await TestHelpers.CreateConfigurationAsync(
            Client,
            slug,
            "local",
            $"{token}_HOST",
            "localhost"
        );
        (
            await Client.PostAsJsonAsync(
                "/management/global/configurations",
                new { key = $"{token}_PORT", value = "VALUE-MARKER-port" }
            )
        ).EnsureSuccessStatusCode();
        (
            await Client.PostAsJsonAsync(
                "/management/global/secrets",
                new { name = $"{token}_RELAY", description = "relay" }
            )
        ).EnsureSuccessStatusCode();

        var json = await SearchAsync(token);
        var results = Results(json);

        Assert.Equal(5, results.Count);
        Assert.Equal("application", results[0]!["type"]!.GetValue<string>());
        Assert.Equal(slug, results[0]!["application"]!.GetValue<string>());

        var config = results.Single(r => r!["type"]!.GetValue<string>() == "configuration")!;
        Assert.Equal(slug, config["application"]!.GetValue<string>());
        Assert.Equal(
            ["local", "production"],
            config["environments"]!.AsArray().Select(e => e!.GetValue<string>())
        );
        Assert.Equal("string", config["valueType"]!.GetValue<string>());

        var secret = results.Single(r => r!["type"]!.GetValue<string>() == "secret")!;
        Assert.Equal(
            ["production"],
            secret["environments"]!.AsArray().Select(e => e!.GetValue<string>())
        );

        var global = results.Single(r => r!["type"]!.GetValue<string>() == "global-configuration")!;
        Assert.Null(global["application"]);
        Assert.Equal(["*"], global["environments"]!.AsArray().Select(e => e!.GetValue<string>()));
        Assert.Contains(results, r => r!["type"]!.GetValue<string>() == "global-secret");

        // Markers can't appear in the generated metadata (tokens are "zq" + hex).
        Assert.DoesNotContain("VALUE-MARKER", json.ToJsonString());
        Assert.All(results, r => Assert.Null(r!["value"]));
    }

    [Fact]
    public async Task Search_DescriptionMatchInOneEnvironment_ListsEveryEnvironmentOfTheKey()
    {
        var token = Token();
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var prod = await TestHelpers.CreateEnvironmentAsync(Client, slug, "production");
        (
            await Client.PostAsJsonAsync(
                $"/management/applications/{slug}/configurations",
                new
                {
                    environmentSlug = "local",
                    key = "PLAIN_KEY",
                    value = "x",
                    description = $"Used by {token}",
                }
            )
        ).EnsureSuccessStatusCode();
        await TestHelpers.CreateConfigurationAsync(Client, slug, prod, "PLAIN_KEY", "y");

        var results = Results(await SearchAsync(token));

        var hit = Assert.Single(results)!;
        Assert.Equal("PLAIN_KEY", hit["name"]!.GetValue<string>());
        Assert.Equal(
            ["local", "production"],
            hit["environments"]!.AsArray().Select(e => e!.GetValue<string>())
        );
        Assert.Contains(token, hit["description"]!.GetValue<string>());
    }

    [Fact]
    public async Task Search_GlobalDescriptionMatchInOneEnvironment_ListsEveryEnvironmentOfTheKey()
    {
        var token = Token();
        var key = $"GKEY_{Guid.NewGuid():N}"[..20];
        (
            await Client.PostAsJsonAsync(
                "/management/global/configurations",
                new
                {
                    key,
                    value = "a",
                    description = $"Shared {token}",
                }
            )
        ).EnsureSuccessStatusCode();
        (
            await Client.PostAsJsonAsync(
                "/management/global/configurations",
                new
                {
                    key,
                    value = "b",
                    environmentSlug = "production",
                }
            )
        ).EnsureSuccessStatusCode();

        var hit = Assert.Single(Results(await SearchAsync(token)))!;
        Assert.Equal(
            ["*", "production"],
            hit["environments"]!.AsArray().Select(e => e!.GetValue<string>())
        );
    }

    [Fact]
    public async Task Search_TermPresentOnlyInAValue_ReturnsNothing()
    {
        var token = Token();
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        await TestHelpers.CreateConfigurationAsync(
            Client,
            slug,
            "local",
            "PLAIN_KEY",
            $"value-{token}"
        );
        await TestHelpers.CreateSecretWithValueAsync(
            Client,
            slug,
            "PLAIN_SECRET",
            "local",
            $"secret-{token}"
        );

        var json = await SearchAsync(token);

        Assert.Empty(Results(json));
        Assert.False(json["truncated"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Search_MatchesDescriptions_AndIgnoresCase()
    {
        var token = Token();
        (
            await Client.PostAsJsonAsync(
                "/management/global/secrets",
                new
                {
                    name = "SOME_KEY_" + token[2..6],
                    description = $"Used by {token.ToUpperInvariant()}",
                }
            )
        ).EnsureSuccessStatusCode();

        var results = Results(await SearchAsync(token));

        Assert.Single(results);
    }

    [Fact]
    public async Task Search_EscapesLikeWildcards()
    {
        var token = Token();
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        await TestHelpers.CreateConfigurationAsync(Client, slug, "local", $"{token}_A", "x");
        await TestHelpers.CreateConfigurationAsync(Client, slug, "local", $"{token}XA", "x");

        // "_" must match a literal underscore, not any character.
        var results = Results(await SearchAsync($"{token}_"));

        Assert.Single(results);
        Assert.Equal($"{token}_A", results[0]!["name"]!.GetValue<string>());
        Assert.Empty(Results(await SearchAsync($"{token}%")));
    }

    [Fact]
    public async Task Search_ExcludesSoftDeletedApplications()
    {
        var token = Token();
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client, $"{token} gone");
        await TestHelpers.CreateConfigurationAsync(Client, slug, "local", $"{token}_KEY", "x");
        (await Client.DeleteAsync($"/management/applications/{slug}")).EnsureSuccessStatusCode();

        Assert.Empty(Results(await SearchAsync(token)));
    }

    [Fact]
    public async Task Search_RespectsLimit_AndReportsTruncation()
    {
        var token = Token();
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        for (var i = 0; i < 4; i++)
            await TestHelpers.CreateConfigurationAsync(Client, slug, "local", $"{token}_{i}", "x");

        var json = await SearchAsync(token, limit: 2);

        Assert.Equal(2, Results(json).Count);
        Assert.True(json["truncated"]!.GetValue<bool>());
    }
}
