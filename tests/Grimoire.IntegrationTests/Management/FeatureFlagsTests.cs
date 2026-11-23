using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Grimoire.IntegrationTests.Management;

public class FeatureFlagsTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private HttpClient Mgmt => factory.CreateManagementClient();

    private static string Flags(string slug) => $"/management/applications/{slug}/feature-flags";

    private static async Task<JsonNode> JsonOf(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    private async Task<(string Slug, string ApiKey)> AppWithStagingAsync()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Mgmt);
        await TestHelpers.CreateEnvironmentAsync(Mgmt, slug, "staging");
        return (slug, apiKey);
    }

    private async Task<JsonNode> CreateMultivariateAsync(string slug, string key = "checkout")
    {
        var response = await Mgmt.PostAsJsonAsync(
            Flags(slug),
            new
            {
                key,
                description = "New checkout",
                type = "multivariate",
                variants = new object[]
                {
                    new { key = "control", value = new { layout = "classic" } },
                    new { key = "treatment", value = new { layout = "one-page" } },
                },
                tags = new[] { "q4", "checkout", "q4" },
            }
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await JsonOf(response);
    }

    private static object Targeting(bool enabled = true) =>
        new
        {
            enabled,
            offVariant = "control",
            rules = new object[]
            {
                new
                {
                    conditions = new object[]
                    {
                        new
                        {
                            attribute = "userId",
                            @operator = "in",
                            values = new[] { "u-123" },
                        },
                    },
                    serve = new { variant = "treatment" },
                },
                new
                {
                    conditions = new object[]
                    {
                        new
                        {
                            attribute = "email",
                            @operator = "endsWith",
                            values = new[] { "@straccini.com" },
                        },
                        new
                        {
                            attribute = "appVersion",
                            @operator = "semVerGte",
                            values = new[] { "2.0.0" },
                        },
                    },
                    serve = new
                    {
                        rollout = new object[]
                        {
                            new { variant = "control", weight = 50 },
                            new { variant = "treatment", weight = 50 },
                        },
                    },
                },
            },
            fallthrough = new
            {
                rollout = new object[]
                {
                    new { variant = "control", weight = 87.5 },
                    new { variant = "treatment", weight = 12.5 },
                },
                bucketBy = "accountId",
            },
        };

    [Fact]
    public async Task CreateBoolean_IsOffEverywhere_WithImplicitVariants()
    {
        var (slug, _) = await AppWithStagingAsync();
        var response = await Mgmt.PostAsJsonAsync(Flags(slug), new { key = "new-pdf-parser" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith($"{Flags(slug)}/new-pdf-parser", response.Headers.Location!.ToString());

        var flag = await JsonOf(response);
        Assert.Equal("boolean", flag["type"]!.GetValue<string>());
        Assert.Equal("off", flag["defaultVariant"]!.GetValue<string>());
        var variants = flag["variants"]!.AsArray();
        Assert.Equal(["on", "off"], variants.Select(v => v!["key"]!.GetValue<string>()));
        Assert.True(variants[0]!["value"]!.GetValue<bool>());
        var envs = flag["environments"]!.AsArray();
        Assert.Equal(2, envs.Count);
        Assert.All(envs, e =>
        {
            Assert.False(e!["enabled"]!.GetValue<bool>());
            Assert.Equal(0, e["version"]!.GetValue<int>());
        });
    }

    [Fact]
    public async Task CreateMultivariate_NormalisesTags_AndRejectsDuplicates()
    {
        var (slug, _) = await AppWithStagingAsync();
        var flag = await CreateMultivariateAsync(slug);
        Assert.Equal(
            ["checkout", "q4"],
            flag["tags"]!.AsArray().Select(t => t!.GetValue<string>())
        );
        Assert.Equal("control", flag["defaultVariant"]!.GetValue<string>());
        Assert.Equal("one-page", flag["variants"]![1]!["value"]!["layout"]!.GetValue<string>());

        var again = await Mgmt.PostAsJsonAsync(Flags(slug), new { key = "checkout" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    public static TheoryData<string> InvalidCreates() =>
        new()
        {
            """{"key":"has space"}""",
            """{"key":"/slash"}""",
            """{"key":"x","type":"ternary"}""",
            """{"key":"x","variants":[{"key":"a","value":1},{"key":"b","value":2}]}""",
            """{"key":"x","type":"multivariate","variants":[{"key":"a","value":1}]}""",
            """{"key":"x","type":"multivariate"}""",
            """{"key":"x","type":"multivariate","variants":[{"key":"a","value":1},{"key":"a","value":2}]}""",
            """{"key":"x","type":"multivariate","variants":[{"key":"a b","value":1},{"key":"c","value":2}]}""",
            """{"key":"x","type":"multivariate","variants":[{"key":"a","value":1},{"key":"b","value":2}],"defaultVariant":"z"}""",
            """{"key":"x","defaultVariant":"maybe"}""",
            """{"key":"x","tags":[" "]}""",
        };

    [Theory]
    [MemberData(nameof(InvalidCreates))]
    public async Task Create_RejectsInvalidRequests(string body)
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Mgmt);
        var response = await Mgmt.PostAsync(
            Flags(slug),
            new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Targeting_IsStoredPerEnvironment_AndVersioned()
    {
        var (slug, _) = await AppWithStagingAsync();
        await CreateMultivariateAsync(slug);

        var put = await Mgmt.PutAsJsonAsync(
            $"{Flags(slug)}/checkout/environments/staging",
            Targeting()
        );
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var flag = await JsonOf(put);
        var staging = flag["environments"]!
            .AsArray()
            .Single(e => e!["environment"]!.GetValue<string>() == "staging")!;
        Assert.True(staging["enabled"]!.GetValue<bool>());
        Assert.Equal(1, staging["version"]!.GetValue<int>());
        Assert.Equal(2, staging["rules"]!.AsArray().Count);
        Assert.Equal(
            "semVerGte",
            staging["rules"]![1]!["conditions"]![1]!["operator"]!.GetValue<string>()
        );
        Assert.Equal(12.5m, staging["fallthrough"]!["rollout"]![1]!["weight"]!.GetValue<decimal>());
        Assert.Equal("accountId", staging["fallthrough"]!["bucketBy"]!.GetValue<string>());

        var local = flag["environments"]!
            .AsArray()
            .Single(e => e!["environment"]!.GetValue<string>() == "local")!;
        Assert.False(local["enabled"]!.GetValue<bool>());

        var again = await JsonOf(
            await Mgmt.PutAsJsonAsync(
                $"{Flags(slug)}/checkout/environments/staging",
                Targeting(false)
            )
        );
        Assert.Equal(
            2,
            again["environments"]!
                .AsArray()
                .Single(e => e!["environment"]!.GetValue<string>() == "staging")![
                "version"
            ]!.GetValue<int>()
        );
    }

    public static TheoryData<string> InvalidTargeting() =>
        new()
        {
            // Weights don't sum to 100.
            """{"enabled":true,"fallthrough":{"rollout":[{"variant":"control","weight":60},{"variant":"treatment","weight":30}]}}""",
            // Three decimals.
            """{"enabled":true,"fallthrough":{"rollout":[{"variant":"control","weight":99.995},{"variant":"treatment","weight":0.005}]}}""",
            // Both a variant and a rollout.
            """{"enabled":true,"fallthrough":{"variant":"control","rollout":[{"variant":"control","weight":100}]}}""",
            // Neither.
            """{"enabled":true,"fallthrough":{}}""",
            // Enabled is required (no silent "off").
            """{"offVariant":"control"}""",
            // Unknown variant.
            """{"enabled":true,"offVariant":"nope"}""",
            """{"enabled":true,"rules":[{"conditions":[{"attribute":"a","operator":"in","values":["x"]}],"serve":{"variant":"nope"}}]}""",
            // Bad regex, bad operator, semver with two values, invalid semver, no conditions, no values.
            """{"enabled":true,"rules":[{"conditions":[{"attribute":"a","operator":"matches","values":["("]}],"serve":{"variant":"control"}}]}""",
            """{"enabled":true,"rules":[{"conditions":[{"attribute":"a","operator":"like","values":["x"]}],"serve":{"variant":"control"}}]}""",
            """{"enabled":true,"rules":[{"conditions":[{"attribute":"a","operator":"semVerGte","values":["1.0.0","2.0.0"]}],"serve":{"variant":"control"}}]}""",
            """{"enabled":true,"rules":[{"conditions":[{"attribute":"a","operator":"semVerLt","values":["1.0"]}],"serve":{"variant":"control"}}]}""",
            """{"enabled":true,"rules":[{"conditions":[],"serve":{"variant":"control"}}]}""",
            """{"enabled":true,"rules":[{"conditions":[{"attribute":"a","operator":"in","values":[]}],"serve":{"variant":"control"}}]}""",
            // Duplicate rollout variant.
            """{"enabled":true,"fallthrough":{"rollout":[{"variant":"control","weight":50},{"variant":"control","weight":50}]}}""",
        };

    [Theory]
    [MemberData(nameof(InvalidTargeting))]
    public async Task Targeting_RejectsInvalidState(string body)
    {
        var (slug, _) = await AppWithStagingAsync();
        await CreateMultivariateAsync(slug);
        var response = await Mgmt.PutAsync(
            $"{Flags(slug)}/checkout/environments/staging",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Nothing was stored.
        var flag = await JsonOf(await Mgmt.GetAsync($"{Flags(slug)}/checkout"));
        Assert.All(flag["environments"]!.AsArray(), e =>
            Assert.Equal(0, e!["version"]!.GetValue<int>())
        );
    }

    [Fact]
    public async Task Toggle_FlipsOrSets_KeepingTargeting()
    {
        var (slug, _) = await AppWithStagingAsync();
        await CreateMultivariateAsync(slug);
        await Mgmt.PutAsJsonAsync($"{Flags(slug)}/checkout/environments/staging", Targeting());
        var url = $"{Flags(slug)}/checkout/environments/staging/toggle";

        JsonNode Staging(JsonNode flag) =>
            flag["environments"]!
                .AsArray()
                .Single(e => e!["environment"]!.GetValue<string>() == "staging")!;

        var flipped = Staging(await JsonOf(await Mgmt.PostAsync(url, null)));
        Assert.False(flipped["enabled"]!.GetValue<bool>());
        Assert.Equal(2, flipped["rules"]!.AsArray().Count);
        Assert.Equal(2, flipped["version"]!.GetValue<int>());

        var set = Staging(await JsonOf(await Mgmt.PostAsJsonAsync(url, new { enabled = true })));
        Assert.True(set["enabled"]!.GetValue<bool>());

        var local = await JsonOf(
            await Mgmt.PostAsync($"{Flags(slug)}/checkout/environments/local/toggle", null)
        );
        Assert.True(
            local["environments"]!
                .AsArray()
                .Single(e => e!["environment"]!.GetValue<string>() == "local")![
                "enabled"
            ]!.GetValue<bool>()
        );
    }

    [Fact]
    public async Task Evaluate_ExplainsTheResult_WithoutStoringTheContext()
    {
        var (slug, _) = await AppWithStagingAsync();
        await CreateMultivariateAsync(slug);
        await Mgmt.PutAsJsonAsync($"{Flags(slug)}/checkout/environments/staging", Targeting());
        var url = $"{Flags(slug)}/checkout/evaluate";

        var rule = await JsonOf(
            await Mgmt.PostAsJsonAsync(
                url,
                new { environment = "staging", context = new { userId = "u-123" } }
            )
        );
        Assert.Equal("treatment", rule["variant"]!.GetValue<string>());
        Assert.Equal("RULE_MATCH", rule["reason"]!.GetValue<string>());
        Assert.Equal(0, rule["matchedRuleIndex"]!.GetValue<int>());
        Assert.Equal("one-page", rule["value"]!["layout"]!.GetValue<string>());

        var rollout = await JsonOf(
            await Mgmt.PostAsJsonAsync(
                url,
                new
                {
                    environment = "staging",
                    context = new
                    {
                        userId = "u-9",
                        email = "g@straccini.com",
                        appVersion = "2.1.0",
                    },
                }
            )
        );
        Assert.Equal(1, rollout["matchedRuleIndex"]!.GetValue<int>());
        Assert.InRange(rollout["bucket"]!.GetValue<int>(), 0, 9999);

        var numeric = await JsonOf(
            await Mgmt.PostAsJsonAsync(
                url,
                new { environment = "staging", context = new { accountId = 42, beta = true } }
            )
        );
        Assert.Equal("FALLTHROUGH", numeric["reason"]!.GetValue<string>());
        Assert.NotNull(numeric["bucket"]);

        var missing = await JsonOf(
            await Mgmt.PostAsJsonAsync(url, new { environment = "staging" })
        );
        Assert.Equal("MISSING_BUCKET_BY", missing["reason"]!.GetValue<string>());
        Assert.Equal("control", missing["variant"]!.GetValue<string>());

        var off = await JsonOf(
            await Mgmt.PostAsJsonAsync(
                url,
                new { environment = "local", context = new { userId = "u-123" } }
            )
        );
        Assert.Equal("OFF", off["reason"]!.GetValue<string>());

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Mgmt.PostAsJsonAsync(url, new { environment = "nope" })).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await Mgmt.PostAsJsonAsync(url, new { environment = "" })).StatusCode
        );
    }

    [Fact]
    public async Task Update_ReplacesMetadata_ButKeepsVariantsInUse()
    {
        var (slug, _) = await AppWithStagingAsync();
        await CreateMultivariateAsync(slug);
        await Mgmt.PutAsJsonAsync($"{Flags(slug)}/checkout/environments/staging", Targeting());

        var renamed = await Mgmt.PutAsJsonAsync(
            $"{Flags(slug)}/checkout",
            new
            {
                description = "Checkout v2",
                tags = new[] { "growth" },
                variants = new object[]
                {
                    new { key = "control", value = new { layout = "classic" } },
                    new { key = "treatment", value = new { layout = "two-step" } },
                    new { key = "holdout", value = (object?)null },
                },
                defaultVariant = "holdout",
            }
        );
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var flag = await JsonOf(renamed);
        Assert.Equal("Checkout v2", flag["description"]!.GetValue<string>());
        Assert.Equal(["growth"], flag["tags"]!.AsArray().Select(t => t!.GetValue<string>()));
        Assert.Equal(3, flag["variants"]!.AsArray().Count);
        Assert.Equal("holdout", flag["defaultVariant"]!.GetValue<string>());

        var dropUsed = await Mgmt.PutAsJsonAsync(
            $"{Flags(slug)}/checkout",
            new
            {
                variants = new object[]
                {
                    new { key = "control", value = 1 },
                    new { key = "holdout", value = 2 },
                },
                defaultVariant = "control",
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, dropUsed.StatusCode);
        Assert.Contains("treatment", await dropUsed.Content.ReadAsStringAsync());

        var unknownDefault = await Mgmt.PutAsJsonAsync(
            $"{Flags(slug)}/checkout",
            new { defaultVariant = "zzz" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, unknownDefault.StatusCode);
    }

    [Fact]
    public async Task Update_BooleanVariantsAreFixed()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Mgmt);
        await Mgmt.PostAsJsonAsync(Flags(slug), new { key = "kill" });
        var response = await Mgmt.PutAsJsonAsync(
            $"{Flags(slug)}/kill",
            new
            {
                variants = new object[]
                {
                    new { key = "a", value = 1 },
                    new { key = "b", value = 2 },
                },
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var on = await JsonOf(
            await Mgmt.PutAsJsonAsync($"{Flags(slug)}/kill", new { defaultVariant = "on" })
        );
        Assert.Equal("on", on["defaultVariant"]!.GetValue<string>());
    }

    [Fact]
    public async Task List_FiltersByTagAndEnvironmentState()
    {
        var (slug, _) = await AppWithStagingAsync();
        await CreateMultivariateAsync(slug, "checkout");
        await Mgmt.PostAsJsonAsync(Flags(slug), new { key = "beta", tags = new[] { "beta" } });
        await Mgmt.PostAsync($"{Flags(slug)}/beta/environments/staging/toggle", null);

        async Task<string[]> Keys(string query) =>
            (await JsonOf(await Mgmt.GetAsync(Flags(slug) + query)))
                .AsArray()
                .Select(f => f!["key"]!.GetValue<string>())
                .ToArray();

        Assert.Equal(["beta", "checkout"], await Keys(""));
        Assert.Equal(["checkout"], await Keys("?tag=q4"));
        Assert.Equal(["beta"], await Keys("?environment=staging&enabled=true"));
        Assert.Equal(["checkout"], await Keys("?environment=staging&enabled=false"));
        Assert.Equal(["beta", "checkout"], await Keys("?environment=local"));
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Mgmt.GetAsync(Flags(slug) + "?environment=nope")).StatusCode
        );
    }

    [Fact]
    public async Task History_RecordsEveryChange_AndSurvivesDelete()
    {
        var (slug, _) = await AppWithStagingAsync();
        await CreateMultivariateAsync(slug);
        await Mgmt.PutAsJsonAsync($"{Flags(slug)}/checkout/environments/staging", Targeting());
        await Mgmt.PostAsync($"{Flags(slug)}/checkout/environments/staging/toggle", null);
        await Mgmt.PutAsJsonAsync($"{Flags(slug)}/checkout", new { description = "v2" });
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await Mgmt.DeleteAsync($"{Flags(slug)}/checkout")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Mgmt.GetAsync($"{Flags(slug)}/checkout")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Mgmt.DeleteAsync($"{Flags(slug)}/checkout")).StatusCode
        );

        var history = (
            await JsonOf(await Mgmt.GetAsync($"{Flags(slug)}/checkout/history"))
        ).AsArray();
        Assert.Equal(
            ["delete", "update", "toggle", "environment", "create"],
            history.Select(h => h!["change"]!.GetValue<string>())
        );
        var toggle = history[2]!;
        Assert.Equal("staging", toggle["environment"]!.GetValue<string>());
        Assert.Equal("admin", toggle["actor"]!.GetValue<string>());
        Assert.True(toggle["before"]!["enabled"]!.GetValue<bool>());
        Assert.False(toggle["after"]!["enabled"]!.GetValue<bool>());
        Assert.Null(history[3]!["before"]);
        Assert.Null(history[0]!["after"]);
        Assert.Equal("New checkout", history[1]!["before"]!["description"]!.GetValue<string>());

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Mgmt.GetAsync($"{Flags(slug)}/never/history")).StatusCode
        );
    }

    [Fact]
    public async Task Changes_AreInTheAuditLog()
    {
        var (slug, _) = await AppWithStagingAsync();
        await Mgmt.PostAsJsonAsync(Flags(slug), new { key = "audited" });
        await Mgmt.PostAsync($"{Flags(slug)}/audited/environments/staging/toggle", null);

        // Events are written in the background: poll until both are stored.
        JsonNode audit = null!;
        List<string> actions = [];
        var deadline = DateTime.UtcNow.AddSeconds(10);
        do
        {
            audit = await JsonOf(await Mgmt.GetAsync($"/management/audit?application={slug}"));
            actions = audit["items"]!
                .AsArray()
                .Select(i => i!["action"]!.GetValue<string>())
                .ToList();
            if (actions.Contains("feature-flag.toggle") && actions.Contains("feature-flag.create"))
                break;
            await Task.Delay(50);
        } while (DateTime.UtcNow < deadline);
        Assert.Contains("feature-flag.create", actions);
        Assert.Contains("feature-flag.toggle", actions);
        var toggle = audit["items"]!
            .AsArray()
            .First(i => i!["action"]!.GetValue<string>() == "feature-flag.toggle")!;
        Assert.Equal("audited", toggle["target"]!.GetValue<string>());
        Assert.Equal("staging", toggle["environment"]!.GetValue<string>());
    }

    [Fact]
    public async Task UnknownApplicationOrFlag_Is404()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Mgmt);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Mgmt.GetAsync(Flags("no-such-app"))).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Mgmt.PostAsJsonAsync(Flags("no-such-app"), new { key = "x" })).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Mgmt.GetAsync($"{Flags("no-such-app")}/x")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Mgmt.DeleteAsync($"{Flags("no-such-app")}/x")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Mgmt.GetAsync($"{Flags("no-such-app")}/x/history")).StatusCode
        );
        Assert.Equal(HttpStatusCode.NotFound, (await Mgmt.GetAsync($"{Flags(slug)}/x")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Mgmt.PutAsJsonAsync($"{Flags(slug)}/x", new { })).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Mgmt.PostAsync($"{Flags(slug)}/x/environments/local/toggle", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await Mgmt.PostAsync($"{Flags("no-such-app")}/x/environments/local/toggle", null)
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await Mgmt.PostAsJsonAsync(
                    $"{Flags(slug)}/x/evaluate",
                    new { environment = "local" }
                )
            ).StatusCode
        );

        await Mgmt.PostAsJsonAsync(Flags(slug), new { key = "x" });
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Mgmt.PostAsync($"{Flags(slug)}/x/environments/nope/toggle", null)).StatusCode
        );
    }
}
