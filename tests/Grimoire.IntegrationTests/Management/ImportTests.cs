using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Grimoire.Core.Audit;

namespace Grimoire.IntegrationTests.Management;

public class ImportTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private HttpClient Mgmt => factory.CreateManagementClient();

    private const string DotEnv = """
        APP_MODE=safe
        DB_PASSWORD="s3cret"
        ORIGINS="a
        b"
        """;

    private Task<HttpResponseMessage> ImportAsync(
        string slug,
        object body,
        bool dryRun = true,
        string environment = "local"
    ) =>
        Mgmt.PostAsJsonAsync(
            $"/management/applications/{slug}/import?environment={environment}&dryRun={dryRun.ToString().ToLowerInvariant()}",
            body
        );

    private static async Task<JsonNode> JsonAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    private static Dictionary<string, JsonNode> Items(JsonNode plan) =>
        plan["items"]!.AsArray().ToDictionary(i => i!["key"]!.GetValue<string>(), i => i!);

    private static string Action(JsonNode plan, string key) =>
        Items(plan)[key]["action"]!.GetValue<string>();

    private async Task<string?> ConsumerValueAsync(
        string apiKey,
        string kind,
        string key,
        string environment = "local"
    )
    {
        var response = await factory
            .CreateConsumerClient(apiKey)
            .GetAsync($"/consumer/{kind}/{key}?environment={environment}");
        return response.StatusCode == HttpStatusCode.OK
            ? JsonNode.Parse(await response.Content.ReadAsStringAsync())![
                "value"
            ]!.GetValue<string>()
            : null;
    }

    [Fact]
    public async Task DryRun_ReturnsThePlan_WithoutSecretValues_AndWritesNothing()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Mgmt);

        var plan = await JsonAsync(
            await ImportAsync(slug, new { format = "env", content = DotEnv })
        );

        Assert.True(plan["dryRun"]!.GetValue<bool>());
        Assert.Equal("local", plan["environment"]!.GetValue<string>());
        var items = Items(plan);
        Assert.Equal("configuration", items["APP_MODE"]["kind"]!.GetValue<string>());
        Assert.Equal("safe", items["APP_MODE"]["value"]!.GetValue<string>());
        Assert.Equal("secret", items["DB_PASSWORD"]["kind"]!.GetValue<string>());
        Assert.Null(items["DB_PASSWORD"]["value"]);
        Assert.Equal("multiline", items["ORIGINS"]["valueType"]!.GetValue<string>());
        Assert.Equal(3, plan["summary"]!["create"]!.GetValue<int>());
        Assert.DoesNotContain("s3cret", plan.ToJsonString());

        Assert.Null(await ConsumerValueAsync(apiKey, "configurations", "APP_MODE"));
        var audit = await Mgmt.WaitForAuditAsync(
            $"application={slug}&action={AuditActions.ImportApply}",
            count: 0
        );
        Assert.Empty(audit);
    }

    [Fact]
    public async Task Apply_CreatesSecretsAndConfiguration_ThenReportsUnchangedSkipsOrOverwrites()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Mgmt);

        var applied = await JsonAsync(
            await ImportAsync(slug, new { format = "env", content = DotEnv }, dryRun: false)
        );
        Assert.False(applied["dryRun"]!.GetValue<bool>());
        Assert.Equal("safe", await ConsumerValueAsync(apiKey, "configurations", "APP_MODE"));
        Assert.Equal("s3cret", await ConsumerValueAsync(apiKey, "secrets", "DB_PASSWORD"));
        var audit = await Mgmt.WaitForAuditAsync(
            $"application={slug}&action={AuditActions.ImportApply}"
        );
        Assert.Equal("local", audit[0]["environment"]!.GetValue<string>());
        Assert.Equal("env", audit[0]["target"]!.GetValue<string>());

        var again = await JsonAsync(
            await ImportAsync(slug, new { format = "env", content = DotEnv })
        );
        Assert.Equal(3, again["summary"]!["unchanged"]!.GetValue<int>());

        const string changed = "APP_MODE=fast\nDB_PASSWORD=rotated";
        var skipped = await JsonAsync(
            await ImportAsync(slug, new { format = "env", content = changed }, dryRun: false)
        );
        Assert.Equal("skip", Action(skipped, "APP_MODE"));
        Assert.Equal("safe", await ConsumerValueAsync(apiKey, "configurations", "APP_MODE"));

        var overwritten = await JsonAsync(
            await ImportAsync(
                slug,
                new
                {
                    format = "env",
                    content = changed,
                    onConflict = "overwrite",
                },
                dryRun: false
            )
        );
        Assert.Equal("update", Action(overwritten, "DB_PASSWORD"));
        Assert.Equal("fast", await ConsumerValueAsync(apiKey, "configurations", "APP_MODE"));
        Assert.Equal("rotated", await ConsumerValueAsync(apiKey, "secrets", "DB_PASSWORD"));

        var history = await Mgmt.GetFromJsonAsync<JsonArray>(
            $"/management/applications/{slug}/configurations/local/APP_MODE/history"
        );
        Assert.Equal(2, history!.Count);
    }

    [Fact]
    public async Task KindsSecretKeysAndExclusions_ChangeWhatIsImported()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Mgmt);
        var body = new
        {
            format = "appsettings",
            content = """{ "ConnectionStrings": { "Main": "Server=db" }, "Api": { "Key": "k", "Url": "u" }, "Debug": true }""",
            secretKeys = new[] { "Api:Key" },
            kinds = new Dictionary<string, string> { ["ConnectionStrings:Main"] = "configuration" },
            exclude = new[] { "Debug" },
        };

        var plan = await JsonAsync(await ImportAsync(slug, body, dryRun: false));

        var items = Items(plan);
        Assert.Equal("configuration", items["ConnectionStrings:Main"]["kind"]!.GetValue<string>());
        Assert.Equal("secret", items["Api:Key"]["kind"]!.GetValue<string>());
        Assert.Equal("excluded", items["Debug"]["action"]!.GetValue<string>());
        Assert.Equal(["Api:Key"], plan["secretKeys"]!.AsArray().Select(k => k!.GetValue<string>()));
        Assert.Equal(
            "Server=db",
            await ConsumerValueAsync(apiKey, "configurations", "ConnectionStrings:Main")
        );
        Assert.Equal("k", await ConsumerValueAsync(apiKey, "secrets", "Api:Key"));
        Assert.Null(await ConsumerValueAsync(apiKey, "configurations", "Debug"));
    }

    [Fact]
    public async Task Php_ReportsSkippedExpressions()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Mgmt);
        var plan = await JsonAsync(
            await ImportAsync(
                slug,
                new
                {
                    format = "php",
                    content = "<?php return ['site' => 'x', 'key' => getenv('KEY')];",
                }
            )
        );
        Assert.Equal(["key"], plan["skipped"]!.AsArray().Select(k => k!.GetValue<string>()));
        Assert.Single(plan["items"]!.AsArray());
    }

    [Fact]
    public async Task ExistingKeyOfTheOtherKind_IsAConflictAndLeftAlone()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Mgmt);
        await TestHelpers.CreateConfigurationAsync(Mgmt, slug, "local", "SMTP_PASSWORD", "plain");

        var plan = await JsonAsync(
            await ImportAsync(
                slug,
                new
                {
                    format = "env",
                    content = "SMTP_PASSWORD=x",
                    onConflict = "overwrite",
                },
                dryRun: false
            )
        );

        Assert.Equal("conflict", Action(plan, "SMTP_PASSWORD"));
        Assert.Equal("plain", await ConsumerValueAsync(apiKey, "configurations", "SMTP_PASSWORD"));
        Assert.Null(await ConsumerValueAsync(apiKey, "secrets", "SMTP_PASSWORD"));
    }

    [Fact]
    public async Task InvalidRequests_AreRejected_AndNothingIsWritten()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Mgmt);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await ImportAsync(slug, new { format = "yaml", content = "a: 1" })).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await ImportAsync(slug, new { format = "env", content = "" })).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await ImportAsync(
                    slug,
                    new
                    {
                        format = "env",
                        content = "A=1",
                        onConflict = "merge",
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await ImportAsync(
                    slug,
                    new
                    {
                        format = "env",
                        content = "A=1",
                        kinds = new Dictionary<string, string> { ["A"] = "flag" },
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await ImportAsync(
                    slug,
                    new { format = "env", content = "A=1" },
                    environment: "nope"
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await ImportAsync("no-such-app", new { format = "env", content = "A=1" })).StatusCode
        );

        var unparsable = await ImportAsync(slug, new { format = "json", content = "[1]" });
        Assert.Equal(HttpStatusCode.BadRequest, unparsable.StatusCode);
        Assert.Contains("Expected a JSON object", await unparsable.Content.ReadAsStringAsync());

        var longName = new string('P', 190) + "_PASSWORD_" + new string('X', 10);
        var tooLong = await ImportAsync(
            slug,
            new { format = "env", content = $"FIRST=1\n{longName}=x" },
            dryRun: false
        );
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Null(await ConsumerValueAsync(apiKey, "configurations", "FIRST"));
    }

    [Fact]
    public async Task Global_ImportsIntoAnEnvironmentOrAll()
    {
        var key = TestHelpers.UniqueName("IMPORT").Replace("-", "_");
        var content = $"{key}_URL=https://logs\n{key}_TOKEN=t0k";

        var dry = await Mgmt.PostAsJsonAsync(
            "/management/global/import?environment=*",
            new { format = "env", content }
        );
        var plan = await JsonAsync(dry);
        Assert.Equal("*", plan["environment"]!.GetValue<string>());
        Assert.Equal("secret", Items(plan)[$"{key}_TOKEN"]["kind"]!.GetValue<string>());

        var applied = await Mgmt.PostAsJsonAsync(
            "/management/global/import?environment=staging&dryRun=false",
            new { format = "env", content }
        );
        Assert.Equal(2, (await JsonAsync(applied))["summary"]!["create"]!.GetValue<int>());

        var configs = await Mgmt.GetFromJsonAsync<JsonArray>(
            "/management/global/configurations?environment=staging"
        );
        Assert.Contains(configs!, c => c!["key"]!.GetValue<string>() == $"{key}_URL");
        var versions = await Mgmt.GetFromJsonAsync<JsonArray>(
            $"/management/global/secrets/{key}_TOKEN/versions/staging"
        );
        Assert.Single(versions!);

        var again = await JsonAsync(
            await Mgmt.PostAsJsonAsync(
                "/management/global/import?environment=staging",
                new { format = "env", content }
            )
        );
        Assert.Equal(2, again["summary"]!["unchanged"]!.GetValue<int>());

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await Mgmt.PostAsJsonAsync(
                    "/management/global/import?environment=Not%20A%20Slug",
                    new { format = "env", content }
                )
            ).StatusCode
        );
    }
}
