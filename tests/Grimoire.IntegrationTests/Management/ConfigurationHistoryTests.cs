using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Grimoire.Core.Audit;

namespace Grimoire.IntegrationTests.Management;

public class ConfigurationHistoryTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private HttpClient Client => factory.CreateManagementClient();

    private static async Task<JsonArray> ArrayAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonArray>())!;
    }

    private static int Rev(JsonNode? n) => n!["revision"]!.GetValue<int>();

    private static string? Str(JsonNode? n, string field) => n![field]?.GetValue<string>();

    private Task<HttpResponseMessage> PutAsync(string slug, string key, string value) =>
        Client.PutAsJsonAsync(
            $"/management/applications/{slug}/configurations/local/{key}",
            new { value }
        );

    private async Task<JsonArray> HistoryAsync(string slug, string key) =>
        await ArrayAsync(
            await Client.GetAsync(
                $"/management/applications/{slug}/configurations/local/{key}/history"
            )
        );

    [Fact]
    public async Task EveryChangeIsARevision_NewestFirst()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        await TestHelpers.CreateConfigurationAsync(Client, slug, "local", "Timeout", "10");
        (await PutAsync(slug, "Timeout", "20")).EnsureSuccessStatusCode();
        (await PutAsync(slug, "Timeout", "20")).EnsureSuccessStatusCode(); // no change, no revision
        (
            await Client.DeleteAsync(
                $"/management/applications/{slug}/configurations/local/Timeout"
            )
        ).EnsureSuccessStatusCode();

        var history = await HistoryAsync(slug, "Timeout");
        Assert.Equal([3, 2, 1], history.Select(Rev));
        Assert.Equal(["delete", "update", "create"], history.Select(h => Str(h, "change")));
        Assert.Equal([null, "20", "10"], history.Select(h => Str(h, "value")));
        Assert.All(history, h => Assert.Equal("admin", Str(h, "changedBy")));
        Assert.All(history, h => Assert.Equal("string", Str(h, "valueType")));
    }

    [Fact]
    public async Task Rollback_RestoresAnOlderValue_AsANewRevision()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Client);
        await TestHelpers.CreateConfigurationAsync(Client, slug, "local", "Mode", "safe");
        (await PutAsync(slug, "Mode", "fast")).EnsureSuccessStatusCode();

        var rollback = await Client.PostAsync(
            $"/management/applications/{slug}/configurations/local/Mode/rollback/1",
            null
        );
        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        Assert.Equal(
            "safe",
            (await rollback.Content.ReadFromJsonAsync<JsonObject>())!["value"]!.GetValue<string>()
        );

        var latest = (await HistoryAsync(slug, "Mode"))[0];
        Assert.Equal(3, Rev(latest));
        Assert.Equal("rollback", Str(latest, "change"));
        Assert.Equal(1, latest!["rolledBackFrom"]!.GetValue<int>());

        var consumer = factory.CreateConsumerClient(apiKey);
        var read = await consumer.GetFromJsonAsync<JsonObject>(
            "/consumer/configurations/Mode?environment=local"
        );
        Assert.Equal("safe", read!["value"]!.GetValue<string>());

        var audit = await Client.WaitForAuditAsync(
            $"application={slug}&action={AuditActions.ConfigurationRollback}"
        );
        Assert.Single(audit);
    }

    [Fact]
    public async Task Rollback_ReCreatesADeletedKey()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        await TestHelpers.CreateConfigurationAsync(Client, slug, "local", "Gone", "v1");
        (
            await Client.DeleteAsync($"/management/applications/{slug}/configurations/local/Gone")
        ).EnsureSuccessStatusCode();

        var rollback = await Client.PostAsync(
            $"/management/applications/{slug}/configurations/local/Gone/rollback/1",
            null
        );
        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);

        var list = await Client.GetFromJsonAsync<JsonArray>(
            $"/management/applications/{slug}/configurations"
        );
        Assert.Contains(list!, e => Str(e, "key") == "Gone" && Str(e, "value") == "v1");
        Assert.Equal("rollback", Str((await HistoryAsync(slug, "Gone"))[0], "change"));
    }

    [Fact]
    public async Task Rollback_ToADelete_Or_UnknownRevision_IsRefused()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        await TestHelpers.CreateConfigurationAsync(Client, slug, "local", "K", "v");
        (
            await Client.DeleteAsync($"/management/applications/{slug}/configurations/local/K")
        ).EnsureSuccessStatusCode();
        var url = $"/management/applications/{slug}/configurations/local/K";

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await Client.PostAsync($"{url}/rollback/2", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Client.PostAsync($"{url}/rollback/9", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await Client.GetAsync(
                    $"/management/applications/{slug}/configurations/local/NEVER/history"
                )
            ).StatusCode
        );
    }

    [Fact]
    public async Task GlobalConfiguration_HasHistoryAndRollback()
    {
        var key = TestHelpers.UniqueName("G");
        await TestHelpers.CreateGlobalConfigurationAsync(Client, key, "one");
        (
            await Client.PutAsJsonAsync(
                $"/management/global/configurations/*/{key}",
                new { value = "two" }
            )
        ).EnsureSuccessStatusCode();

        var history = await ArrayAsync(
            await Client.GetAsync($"/management/global/configurations/*/{key}/history")
        );
        Assert.Equal(["two", "one"], history.Select(h => Str(h, "value")));

        var rollback = await Client.PostAsync(
            $"/management/global/configurations/*/{key}/rollback/1",
            null
        );
        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        Assert.Equal(
            "one",
            (await rollback.Content.ReadFromJsonAsync<JsonObject>())!["value"]!.GetValue<string>()
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await Client.GetAsync($"/management/global/configurations/qa/{key}/history")
            ).StatusCode
        );
    }

    [Fact]
    public async Task Diff_ComparesEffectiveConfigurationBetweenEnvironments()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        await TestHelpers.CreateEnvironmentAsync(Client, slug, "Staging");
        await TestHelpers.CreateEnvironmentAsync(Client, slug, "Production");
        await TestHelpers.CreateConfigurationAsync(Client, slug, "staging", "Same", "1");
        await TestHelpers.CreateConfigurationAsync(Client, slug, "production", "Same", "1");
        await TestHelpers.CreateConfigurationAsync(Client, slug, "staging", "Url", "stg");
        await TestHelpers.CreateConfigurationAsync(Client, slug, "production", "Url", "prd");
        await TestHelpers.CreateConfigurationAsync(Client, slug, "staging", "Debug", "true");
        await TestHelpers.CreateConfigurationAsync(Client, slug, "production", "Cdn", "on");

        var url = $"/management/applications/{slug}/configurations/diff?from=staging&to=production";
        var diff = await Client.GetFromJsonAsync<JsonObject>(url);
        var items = diff!["items"]!
            .AsArray()
            .Where(i => Str(i, "key") is "Same" or "Url" or "Debug" or "Cdn")
            .ToList();
        Assert.Equal(
            [("Cdn", "added"), ("Debug", "removed"), ("Url", "changed")],
            items.Select(i => (Str(i, "key"), Str(i, "status")))
        );
        var changed = items.Single(i => Str(i, "key") == "Url");
        Assert.Equal("stg", changed!["from"]!["value"]!.GetValue<string>());
        Assert.Equal("prd", changed["to"]!["value"]!.GetValue<string>());
        Assert.Null(items.Single(i => Str(i, "key") == "Cdn")!["from"]);

        var all = await Client.GetFromJsonAsync<JsonObject>(url + "&includeUnchanged=true");
        Assert.Contains(all!["items"]!.AsArray(), i =>
            Str(i, "key") == "Same" && Str(i, "status") == "unchanged"
        );

        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await Client.GetAsync(
                    $"/management/applications/{slug}/configurations/diff?from=staging&to=nope"
                )
            ).StatusCode
        );
    }
}
