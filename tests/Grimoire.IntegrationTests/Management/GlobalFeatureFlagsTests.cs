using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Grimoire.IntegrationTests.Management;

public class GlobalFeatureFlagsTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private const string Globals = "/management/global/feature-flags";
    private HttpClient Mgmt => factory.CreateManagementClient();

    private static async Task<JsonNode> JsonOf(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    private static readonly string[] SharedTags = ["shared"];

    private static JsonNode EnvOf(JsonNode flag, string env) =>
        flag["environments"]!.AsArray().Single(e => e!["environment"]!.GetValue<string>() == env)!;

    private async Task<string> CreateGlobalAsync(string? key = null)
    {
        key ??= TestHelpers.UniqueName("gflag");
        var response = await Mgmt.PostAsJsonAsync(Globals, new { key, tags = SharedTags });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return key;
    }

    private Task<HttpResponseMessage> SetAsync(
        string key,
        string env,
        bool enabled,
        string? variant = null
    ) =>
        Mgmt.PutAsJsonAsync(
            $"{Globals}/{key}/environments/{Uri.EscapeDataString(env)}",
            new { enabled, fallthrough = variant is null ? null : new { variant } }
        );

    [Fact]
    public async Task Create_ListsStarAndKnownSlugs_AndRejectsDuplicates()
    {
        await TestHelpers.CreateApplicationAsync(Mgmt); // has "local"
        var key = await CreateGlobalAsync();
        var flag = await JsonOf(await Mgmt.GetAsync($"{Globals}/{key}"));
        var envs = flag["environments"]!
            .AsArray()
            .Select(e => e!["environment"]!.GetValue<string>())
            .ToList();
        Assert.Equal("*", envs[0]);
        Assert.Contains("local", envs);
        Assert.All(flag["environments"]!.AsArray(), e =>
            Assert.False(e!["enabled"]!.GetValue<bool>())
        );

        Assert.Equal(
            HttpStatusCode.Conflict,
            (await Mgmt.PostAsJsonAsync(Globals, new { key })).StatusCode
        );
        Assert.Contains(
            key,
            (await JsonOf(await Mgmt.GetAsync($"{Globals}?tag=shared")))
                .AsArray()
                .Select(f => f!["key"]!.GetValue<string>())
        );
    }

    [Fact]
    public async Task State_IsPerSlug_WithStarAsTheFallback()
    {
        var key = await CreateGlobalAsync();
        Assert.Equal(HttpStatusCode.OK, (await SetAsync(key, "*", true)).StatusCode);
        // A slug no application has yet is accepted, and shows up afterwards.
        var flag = await JsonOf(await SetAsync(key, "future-env", false));
        Assert.True(EnvOf(flag, "*")["enabled"]!.GetValue<bool>());
        Assert.False(EnvOf(flag, "future-env")["enabled"]!.GetValue<bool>());
        Assert.Equal(1, EnvOf(flag, "future-env")["version"]!.GetValue<int>());

        async Task<string> Reason(string env) =>
            (
                await JsonOf(
                    await Mgmt.PostAsJsonAsync(
                        $"{Globals}/{key}/evaluate",
                        new { environment = env }
                    )
                )
            )["reason"]!.GetValue<string>();
        Assert.Equal("FALLTHROUGH", await Reason("staging"));
        Assert.Equal("OFF", await Reason("future-env"));
        Assert.Equal("FALLTHROUGH", await Reason("*"));

        Assert.Equal(HttpStatusCode.NotFound, (await SetAsync(key, "Not A Slug", true)).StatusCode);
        var listed = await JsonOf(await Mgmt.GetAsync($"{Globals}?environment=*&enabled=true"));
        Assert.Contains(key, listed.AsArray().Select(f => f!["key"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Toggle_Update_Delete_AndHistory()
    {
        var key = await CreateGlobalAsync();
        var toggled = await JsonOf(
            await Mgmt.PostAsync($"{Globals}/{key}/environments/*/toggle", null)
        );
        Assert.True(EnvOf(toggled, "*")["enabled"]!.GetValue<bool>());

        var updated = await JsonOf(
            await Mgmt.PutAsJsonAsync($"{Globals}/{key}", new { description = "Shared" })
        );
        Assert.Equal("Shared", updated["description"]!.GetValue<string>());

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await Mgmt.DeleteAsync($"{Globals}/{key}")).StatusCode
        );
        Assert.Equal(HttpStatusCode.NotFound, (await Mgmt.GetAsync($"{Globals}/{key}")).StatusCode);
        var history = (await JsonOf(await Mgmt.GetAsync($"{Globals}/{key}/history"))).AsArray();
        Assert.Equal(
            ["delete", "update", "toggle", "create"],
            history.Select(h => h!["change"]!.GetValue<string>())
        );
        Assert.Equal("*", history[2]!["environment"]!.GetValue<string>());
    }

    [Fact]
    public async Task GlobalAndApplicationFlags_CanShareAKey()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Mgmt);
        var key = await CreateGlobalAsync();
        var app = await Mgmt.PostAsJsonAsync(
            $"/management/applications/{slug}/feature-flags",
            new { key }
        );
        Assert.Equal(HttpStatusCode.Created, app.StatusCode);
        // The application's own list doesn't include globals.
        var list = await JsonOf(
            await Mgmt.GetAsync($"/management/applications/{slug}/feature-flags")
        );
        Assert.Single(list.AsArray());
        // Global history is separate from the application's.
        Assert.Single((await JsonOf(await Mgmt.GetAsync($"{Globals}/{key}/history"))).AsArray());
    }

    [Fact]
    public async Task Consumers_InheritGlobals_UnlessTheyOverrideOrOptOut()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Mgmt);
        var inherited = await CreateGlobalAsync();
        var shadowed = await CreateGlobalAsync();
        await SetAsync(inherited, "*", false);
        await SetAsync(inherited, "local", true, "on");
        await SetAsync(shadowed, "*", true, "on");
        await Mgmt.PostAsJsonAsync(
            $"/management/applications/{slug}/feature-flags",
            new { key = shadowed }
        );

        var consumer = factory.CreateConsumerClient(apiKey);
        var all = (
            await JsonOf(await consumer.GetAsync("/consumer/feature-flags?environment=local"))
        )["flags"]!.AsArray();
        var g = all.Single(f => f!["key"]!.GetValue<string>() == inherited)!;
        Assert.Equal("global", g["source"]!.GetValue<string>());
        Assert.True(g["enabled"]!.GetValue<bool>()); // the "local" state wins over "*"
        Assert.Equal("on", g["fallthrough"]!["variant"]!.GetValue<string>());
        var own = all.Single(f => f!["key"]!.GetValue<string>() == shadowed)!;
        Assert.Equal("application", own["source"]!.GetValue<string>());
        Assert.False(own["enabled"]!.GetValue<bool>());

        var single = await JsonOf(
            await consumer.GetAsync($"/consumer/feature-flags/{inherited}?environment=local")
        );
        Assert.Equal("global", single["source"]!.GetValue<string>());

        // Only "*" for another environment.
        await TestHelpers.CreateEnvironmentAsync(Mgmt, slug, "staging");
        var staging = await JsonOf(
            await consumer.GetAsync($"/consumer/feature-flags/{inherited}?environment=staging")
        );
        Assert.False(staging["enabled"]!.GetValue<bool>());

        // Opting out of globals hides them.
        (
            await Mgmt.PutAsJsonAsync(
                $"/management/applications/{slug}",
                new { name = slug, inheritGlobals = false }
            )
        ).EnsureSuccessStatusCode();
        var optedOut = (
            await JsonOf(await consumer.GetAsync("/consumer/feature-flags?environment=local"))
        )["flags"]!.AsArray();
        Assert.DoesNotContain(optedOut, f => f!["key"]!.GetValue<string>() == inherited);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await consumer.GetAsync($"/consumer/feature-flags/{inherited}?environment=local")
            ).StatusCode
        );
    }

    [Fact]
    public async Task Etag_ChangesWhenAGlobalChanges()
    {
        var (_, apiKey) = await TestHelpers.CreateApplicationAsync(Mgmt);
        var key = await CreateGlobalAsync();
        var consumer = factory.CreateConsumerClient(apiKey);
        var etag = (await consumer.GetAsync("/consumer/feature-flags?environment=local"))
            .Headers
            .ETag!;

        await Mgmt.PostAsync($"{Globals}/{key}/environments/*/toggle", null);

        var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/consumer/feature-flags?environment=local"
        );
        request.Headers.IfNoneMatch.Add(etag);
        Assert.Equal(HttpStatusCode.OK, (await consumer.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Changes_UseGlobalAuditCodes()
    {
        var key = await CreateGlobalAsync();
        await Mgmt.PostAsync($"{Globals}/{key}/environments/*/toggle", null);

        List<string> actions = [];
        var deadline = DateTime.UtcNow.AddSeconds(10);
        do
        {
            var audit = await JsonOf(await Mgmt.GetAsync("/management/audit?pageSize=200"));
            actions = audit["items"]!
                .AsArray()
                .Where(i => i!["target"]?.GetValue<string>() == key)
                .Select(i => i!["action"]!.GetValue<string>())
                .ToList();
            if (actions.Count >= 2)
                break;
            await Task.Delay(50);
        } while (DateTime.UtcNow < deadline);
        Assert.Contains("global-feature-flag.create", actions);
        Assert.Contains("global-feature-flag.toggle", actions);
    }

    [Fact]
    public async Task UnknownGlobalFlag_Is404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Mgmt.GetAsync($"{Globals}/nope")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Mgmt.DeleteAsync($"{Globals}/nope")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Mgmt.GetAsync($"{Globals}/nope/history")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Mgmt.PostAsync($"{Globals}/nope/environments/*/toggle", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await Mgmt.PostAsJsonAsync($"{Globals}/nope/evaluate", new { environment = "*" })
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Mgmt.GetAsync($"{Globals}?environment=Not A Slug")).StatusCode
        );
    }
}
