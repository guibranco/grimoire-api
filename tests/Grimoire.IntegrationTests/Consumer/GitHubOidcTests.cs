using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Grimoire.IntegrationTests.Consumer;

public class GitHubOidcTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private HttpClient Mgmt => factory.CreateManagementClient();
    private static readonly string[] SecretsOnly = ["secrets"];
    private static readonly string[] StagingOnly = ["staging"];
    private static readonly string[] ProductionOnly = ["production"];
    private static readonly string[] UnknownEnvironment = ["nope"];
    private static readonly string[] ProductionMixedCase = ["Production"];
    private static readonly string[] SecretsAndFlags = ["secrets", "feature-flags"];

    private async Task<string> AppAsync()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Mgmt);
        await TestHelpers.CreateEnvironmentAsync(Mgmt, slug, "staging");
        await TestHelpers.CreateEnvironmentAsync(Mgmt, slug, "production");
        await TestHelpers.CreateSecretWithValueAsync(
            Mgmt,
            slug,
            "DB_PASSWORD",
            "staging",
            "staging-pw"
        );
        var values = await Mgmt.PostAsJsonAsync(
            $"/management/applications/{slug}/secrets/DB_PASSWORD/values",
            new[]
            {
                new
                {
                    environmentSlug = "production",
                    value = "prod-pw",
                    isEnabled = true,
                },
            }
        );
        values.EnsureSuccessStatusCode();
        return slug;
    }

    private async Task<JsonNode> PolicyAsync(string slug, object body)
    {
        var res = await Mgmt.PostAsJsonAsync(
            $"/management/applications/{slug}/trust-policies",
            body
        );
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
    }

    private Task<HttpResponseMessage> ExchangeAsync(string application, string token) =>
        factory.CreateClient().PostAsJsonAsync("/consumer/oidc/github", new { application, token });

    [Fact]
    public async Task Exchange_IssuesAShortLivedTokenLimitedByThePolicy()
    {
        var slug = await AppAsync();
        await PolicyAsync(
            slug,
            new
            {
                name = "deploy main",
                repository = "guibranco/demo",
                refPattern = "refs/heads/main",
                environments = StagingOnly,
                scopes = SecretsOnly,
                tokenLifetimeMinutes = 10,
            }
        );

        var res = await ExchangeAsync(slug, TestOidc.Token());
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
        var token = body["token"]!.GetValue<string>();
        Assert.StartsWith("grm_tok_", token);
        Assert.Equal("deploy main", body["policy"]!.GetValue<string>());
        var expiresIn = body["expiresAt"]!.GetValue<DateTimeOffset>() - DateTimeOffset.UtcNow;
        Assert.InRange(expiresIn.TotalMinutes, 9, 10.1);

        var consumer = factory.CreateConsumerClient(token);
        var ok = await consumer.GetAsync("/consumer/secrets/DB_PASSWORD?environment=staging");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await consumer.GetAsync("/consumer/secrets/DB_PASSWORD?environment=production")
            ).StatusCode
        );

        // Issued tokens don't clutter the token list; the policy shows its last use.
        var tokens = JsonNode
            .Parse(await Mgmt.GetStringAsync($"/management/applications/{slug}/access-tokens"))!
            .AsArray();
        Assert.Empty(tokens);
        var policies = JsonNode
            .Parse(await Mgmt.GetStringAsync($"/management/applications/{slug}/trust-policies"))!
            .AsArray();
        Assert.NotNull(policies[0]!["lastUsedAt"]);
    }

    [Fact]
    public async Task Exchange_PicksTheMostSpecificPolicy()
    {
        var slug = await AppAsync();
        await PolicyAsync(
            slug,
            new
            {
                name = "any ref",
                repository = "guibranco/demo",
                environments = StagingOnly,
                scopes = SecretsOnly,
            }
        );
        await PolicyAsync(
            slug,
            new
            {
                name = "production deploys",
                repository = "guibranco/demo",
                refPattern = "refs/heads/main",
                gitHubEnvironment = "production",
                environments = ProductionOnly,
                scopes = SecretsOnly,
            }
        );

        var prod = JsonNode.Parse(
            await (
                await ExchangeAsync(slug, TestOidc.Token(environment: "production"))
            ).Content.ReadAsStringAsync()
        )!;
        Assert.Equal("production deploys", prod["policy"]!.GetValue<string>());
        var read = await factory
            .CreateConsumerClient(prod["token"]!.GetValue<string>())
            .GetAsync("/consumer/secrets/DB_PASSWORD?environment=production");
        Assert.Contains("prod-pw", await read.Content.ReadAsStringAsync());

        var feature = JsonNode.Parse(
            await (
                await ExchangeAsync(slug, TestOidc.Token(@ref: "refs/heads/feature"))
            ).Content.ReadAsStringAsync()
        )!;
        Assert.Equal("any ref", feature["policy"]!.GetValue<string>());
    }

    [Fact]
    public async Task Exchange_RejectsTokensNoPolicyAllows_AndInvalidTokens()
    {
        var slug = await AppAsync();
        await PolicyAsync(
            slug,
            new
            {
                name = "main only",
                repository = "guibranco/demo",
                refPattern = "refs/heads/main",
                scopes = SecretsOnly,
            }
        );

        string[] rejected =
        [
            TestOidc.Token(@ref: "refs/heads/dev"),
            TestOidc.Token(repository: "someone/else"),
            TestOidc.Token(audience: "another-grimoire"),
            TestOidc.Token(issuer: "https://evil.example"),
            TestOidc.Token(expires: DateTime.UtcNow.AddMinutes(-5)),
            TestOidc.Token(wrongKey: true),
            "not-a-jwt",
            "",
        ];
        foreach (var token in rejected)
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await ExchangeAsync(slug, token)).StatusCode
            );

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await ExchangeAsync("missing-app", TestOidc.Token())).StatusCode
        );
        Assert.Equal(HttpStatusCode.OK, (await ExchangeAsync(slug, TestOidc.Token())).StatusCode);
    }

    [Fact]
    public async Task Exchange_IsAudited()
    {
        var slug = await AppAsync();
        await PolicyAsync(
            slug,
            new
            {
                name = "audited",
                repository = "guibranco/audited",
                scopes = SecretsOnly,
            }
        );
        (
            await ExchangeAsync(slug, TestOidc.Token(repository: "guibranco/audited"))
        ).EnsureSuccessStatusCode();
        await ExchangeAsync(
            slug,
            TestOidc.Token(repository: "guibranco/audited", @ref: "refs/x", wrongKey: true)
        );

        var deadline = DateTime.UtcNow.AddSeconds(10);
        List<JsonNode> items;
        do
        {
            var page = JsonNode.Parse(
                await Mgmt.GetStringAsync(
                    $"/management/audit?action=access-token.oidc-exchange&application={slug}"
                )
            )!;
            items = page["items"]!.AsArray().Select(i => i!).ToList();
            if (items.Count >= 2)
                break;
            await Task.Delay(50);
        } while (DateTime.UtcNow < deadline);

        Assert.Contains(items, e =>
            e["actor"]!.GetValue<string>() == "github:guibranco/audited"
            && e["target"]!.GetValue<string>() == "audited"
            && e["statusCode"]!.GetValue<int>() == 200
        );
        Assert.Contains(items, e =>
            e["actor"]!.GetValue<string>() == "anonymous" && e["statusCode"]!.GetValue<int>() == 401
        );
    }

    [Fact]
    public async Task DeletingAPolicy_RevokesTheTokensItIssued()
    {
        var slug = await AppAsync();
        var policy = await PolicyAsync(
            slug,
            new
            {
                name = "temp",
                repository = "guibranco/demo",
                scopes = SecretsOnly,
            }
        );
        var token = JsonNode.Parse(
            await (await ExchangeAsync(slug, TestOidc.Token())).Content.ReadAsStringAsync()
        )!["token"]!.GetValue<string>();
        var consumer = factory.CreateConsumerClient(token);
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await consumer.GetAsync("/consumer/secrets/DB_PASSWORD?environment=staging")
            ).StatusCode
        );

        var delete = await Mgmt.DeleteAsync(
            $"/management/applications/{slug}/trust-policies/{policy["id"]!.GetValue<string>()}"
        );
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (
                await consumer.GetAsync("/consumer/secrets/DB_PASSWORD?environment=staging")
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await ExchangeAsync(slug, TestOidc.Token())).StatusCode
        );
    }

    [Fact]
    public async Task Policies_CanBeUpdated_AndAreValidated()
    {
        var slug = await AppAsync();
        var url = $"/management/applications/{slug}/trust-policies";
        var policy = await PolicyAsync(
            slug,
            new
            {
                name = "p1",
                repository = "guibranco/demo",
                scopes = SecretsOnly,
            }
        );
        Assert.Equal(15, policy["tokenLifetimeMinutes"]!.GetValue<int>());
        Assert.Null(policy["environments"]);
        var id = policy["id"]!.GetValue<string>();

        var update = await Mgmt.PutAsJsonAsync(
            $"{url}/{id}",
            new
            {
                name = "p1 renamed",
                repository = "guibranco/demo",
                refPattern = "refs/tags/v*",
                gitHubEnvironment = "release",
                environments = ProductionMixedCase,
                scopes = SecretsAndFlags,
                tokenLifetimeMinutes = 60,
            }
        );
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = JsonNode.Parse(await update.Content.ReadAsStringAsync())!;
        Assert.Equal("p1 renamed", updated["name"]!.GetValue<string>());
        Assert.Equal("production", updated["environments"]![0]!.GetValue<string>());
        Assert.Equal(2, updated["scopes"]!.AsArray().Count);
        Assert.Equal(60, updated["tokenLifetimeMinutes"]!.GetValue<int>());

        await PolicyAsync(
            slug,
            new
            {
                name = "p2",
                repository = "guibranco/demo",
                scopes = SecretsOnly,
            }
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await Mgmt.PostAsJsonAsync(
                    url,
                    new
                    {
                        name = "p2",
                        repository = "a/b",
                        scopes = SecretsOnly,
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await Mgmt.PutAsJsonAsync(
                    $"{url}/{id}",
                    new
                    {
                        name = "p2",
                        repository = "a/b",
                        scopes = SecretsOnly,
                    }
                )
            ).StatusCode
        );

        object[] invalid =
        [
            new
            {
                name = "",
                repository = "a/b",
                scopes = SecretsOnly,
            },
            new
            {
                name = "x",
                repository = "not-a-repo",
                scopes = SecretsOnly,
            },
            new
            {
                name = "x",
                repository = "a/b",
                refPattern = "main",
                scopes = SecretsOnly,
            },
            new
            {
                name = "x",
                repository = "a/b",
                scopes = Array.Empty<string>(),
            },
            new
            {
                name = "x",
                repository = "a/b",
                scopes = SecretsOnly,
                tokenLifetimeMinutes = 61,
            },
            new
            {
                name = "x",
                repository = "a/b",
                scopes = SecretsOnly,
                tokenLifetimeMinutes = 0,
            },
            new
            {
                name = "x",
                repository = "a/b",
                scopes = SecretsOnly,
                environments = UnknownEnvironment,
            },
        ];
        foreach (var body in invalid)
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await Mgmt.PostAsJsonAsync(url, body)).StatusCode
            );

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Mgmt.GetAsync("/management/applications/missing/trust-policies")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await Mgmt.PutAsJsonAsync(
                    $"{url}/{Guid.NewGuid()}",
                    new
                    {
                        name = "x",
                        repository = "a/b",
                        scopes = SecretsOnly,
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Mgmt.DeleteAsync($"{url}/{Guid.NewGuid()}")).StatusCode
        );
    }

    [Fact]
    public async Task Exchange_NeedsNoApiKey_ButOtherConsumerRoutesStillDo()
    {
        var res = await factory.CreateClient().GetAsync("/consumer/secrets/X?environment=staging");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
