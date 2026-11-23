using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Grimoire.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Grimoire.IntegrationTests.Management;

public class AccessTokensTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private HttpClient Mgmt => factory.CreateManagementClient();

    private static readonly string[] SecretsOnly = ["secrets"];
    private static readonly string[] AllScopes = ["secrets", "configurations", "feature-flags"];
    private static readonly string[] StagingOnly = ["staging"];

    /// <summary>An app with a "staging" environment, a secret and a config entry in both.</summary>
    private async Task<(string Slug, string ApiKey)> AppAsync()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Mgmt);
        await TestHelpers.CreateEnvironmentAsync(Mgmt, slug, "staging");
        await TestHelpers.CreateSecretWithValueAsync(
            Mgmt,
            slug,
            "DB_PASSWORD",
            "staging",
            "s3cret"
        );
        var values = await Mgmt.PostAsJsonAsync(
            $"/management/applications/{slug}/secrets/DB_PASSWORD/values",
            new[]
            {
                new
                {
                    environmentSlug = "local",
                    value = "local-pw",
                    isEnabled = true,
                },
            }
        );
        values.EnsureSuccessStatusCode();
        await TestHelpers.CreateConfigurationAsync(Mgmt, slug, "staging", "Mode", "on");
        return (slug, apiKey);
    }

    private async Task<(JsonNode Token, string Plain)> CreateTokenAsync(string slug, object body)
    {
        var response = await Mgmt.PostAsJsonAsync(
            $"/management/applications/{slug}/access-tokens",
            body
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        return (json["token"]!, json["plainToken"]!.GetValue<string>());
    }

    [Fact]
    public async Task Create_ReturnsPlainTokenOnce_ListShowsMetadataOnly()
    {
        var (slug, _) = await AppAsync();
        var (token, plain) = await CreateTokenAsync(
            slug,
            new
            {
                name = "github-actions",
                environments = StagingOnly,
                scopes = SecretsOnly,
            }
        );

        Assert.StartsWith("grm_tok_", plain);
        Assert.Equal(48, plain.Length);
        Assert.Equal(plain.Substring(8, 8), token["prefix"]!.GetValue<string>());
        Assert.Equal("active", token["status"]!.GetValue<string>());
        Assert.Equal("staging", token["environments"]![0]!.GetValue<string>());
        Assert.Equal("secrets", token["scopes"]![0]!.GetValue<string>());

        var list = await Mgmt.GetStringAsync($"/management/applications/{slug}/access-tokens");
        Assert.DoesNotContain(plain, list);
        Assert.DoesNotContain("hash", list, StringComparison.OrdinalIgnoreCase);
        var items = JsonNode.Parse(list)!.AsArray();
        Assert.Single(items);
        Assert.Equal("github-actions", items[0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Token_ReadsInsideItsScope_AndIsForbiddenOutside()
    {
        var (slug, _) = await AppAsync();
        var (_, plain) = await CreateTokenAsync(
            slug,
            new
            {
                name = "ci",
                environments = StagingOnly,
                scopes = SecretsOnly,
            }
        );
        var consumer = factory.CreateConsumerClient(plain);

        var ok = await consumer.GetAsync("/consumer/secrets/DB_PASSWORD?environment=staging");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("s3cret", await ok.Content.ReadAsStringAsync());

        var otherEnv = await consumer.GetAsync("/consumer/secrets/DB_PASSWORD?environment=local");
        Assert.Equal(HttpStatusCode.Forbidden, otherEnv.StatusCode);
        Assert.DoesNotContain("local-pw", await otherEnv.Content.ReadAsStringAsync());

        var otherScope = await consumer.GetAsync("/consumer/configurations?environment=staging");
        Assert.Equal(HttpStatusCode.Forbidden, otherScope.StatusCode);

        var flags = await consumer.GetAsync("/consumer/feature-flags?environment=staging");
        Assert.Equal(HttpStatusCode.Forbidden, flags.StatusCode);
    }

    [Fact]
    public async Task TokenWithoutEnvironments_ReadsEveryEnvironment()
    {
        var (slug, _) = await AppAsync();
        var (_, plain) = await CreateTokenAsync(slug, new { name = "runtime", scopes = AllScopes });
        var consumer = factory.CreateConsumerClient(plain);

        Assert.Equal(
            HttpStatusCode.OK,
            (await consumer.GetAsync("/consumer/secrets/DB_PASSWORD?environment=local")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (await consumer.GetAsync("/consumer/configurations?environment=staging")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (await consumer.GetAsync("/consumer/feature-flags?environment=staging")).StatusCode
        );
    }

    [Fact]
    public async Task ApiKey_StillReadsEverything()
    {
        var (slug, apiKey) = await AppAsync();
        await CreateTokenAsync(
            slug,
            new
            {
                name = "ci",
                environments = StagingOnly,
                scopes = SecretsOnly,
            }
        );
        var consumer = factory.CreateConsumerClient(apiKey);
        Assert.Equal(
            HttpStatusCode.OK,
            (await consumer.GetAsync("/consumer/secrets/DB_PASSWORD?environment=local")).StatusCode
        );
    }

    [Fact]
    public async Task RevokedOrUnknownToken_IsUnauthorized()
    {
        var (slug, _) = await AppAsync();
        var (token, plain) = await CreateTokenAsync(
            slug,
            new { name = "old", scopes = SecretsOnly }
        );
        var id = token["id"]!.GetValue<string>();
        var consumer = factory.CreateConsumerClient(plain);
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await consumer.GetAsync("/consumer/secrets/DB_PASSWORD?environment=staging")
            ).StatusCode
        );

        var revoke = await Mgmt.PostAsync(
            $"/management/applications/{slug}/access-tokens/{id}/revoke",
            null
        );
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        var revoked = JsonNode.Parse(await revoke.Content.ReadAsStringAsync())!;
        Assert.Equal("revoked", revoked["status"]!.GetValue<string>());
        var again = await Mgmt.PostAsync(
            $"/management/applications/{slug}/access-tokens/{id}/revoke",
            null
        );
        Assert.Equal(
            revoked["revokedAt"]!.GetValue<DateTimeOffset>(),
            JsonNode.Parse(await again.Content.ReadAsStringAsync())![
                "revokedAt"
            ]!.GetValue<DateTimeOffset>()
        );

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (
                await consumer.GetAsync("/consumer/secrets/DB_PASSWORD?environment=staging")
            ).StatusCode
        );

        // Right prefix, wrong secret; and a value too short to hold a prefix.
        var forged = factory.CreateConsumerClient(plain[..16] + new string('x', 32));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await forged.GetAsync("/consumer/secrets/DB_PASSWORD?environment=staging")).StatusCode
        );
        var tooShort = factory.CreateConsumerClient("grm_tok_abc");
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (
                await tooShort.GetAsync("/consumer/secrets/DB_PASSWORD?environment=staging")
            ).StatusCode
        );
    }

    [Fact]
    public async Task ExpiredToken_IsUnauthorized_AndListedAsExpired()
    {
        var (slug, _) = await AppAsync();
        var (token, plain) = await CreateTokenAsync(
            slug,
            new
            {
                name = "short",
                scopes = SecretsOnly,
                expiresAt = DateTimeOffset.UtcNow.AddDays(1),
            }
        );
        var id = Guid.Parse(token["id"]!.GetValue<string>());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();
            var stored = await db.AccessTokens.SingleAsync(t => t.Id == id);
            stored.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var consumer = factory.CreateConsumerClient(plain);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (
                await consumer.GetAsync("/consumer/secrets/DB_PASSWORD?environment=staging")
            ).StatusCode
        );
        var list = JsonNode
            .Parse(await Mgmt.GetStringAsync($"/management/applications/{slug}/access-tokens"))!
            .AsArray();
        Assert.Equal("expired", list[0]!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Use_RecordsLastUsedAt()
    {
        var (slug, _) = await AppAsync();
        var (token, plain) = await CreateTokenAsync(
            slug,
            new { name = "used", scopes = SecretsOnly }
        );
        Assert.Null(token["lastUsedAt"]);

        var consumer = factory.CreateConsumerClient(plain);
        (
            await consumer.GetAsync("/consumer/secrets/DB_PASSWORD?environment=staging")
        ).EnsureSuccessStatusCode();

        var list = JsonNode
            .Parse(await Mgmt.GetStringAsync($"/management/applications/{slug}/access-tokens"))!
            .AsArray();
        Assert.NotNull(list[0]!["lastUsedAt"]);
    }

    [Fact]
    public async Task Reads_AreAuditedWithTheTokenPrefix_DenialsToo()
    {
        var (slug, _) = await AppAsync();
        var (token, plain) = await CreateTokenAsync(
            slug,
            new
            {
                name = "audited",
                environments = StagingOnly,
                scopes = SecretsOnly,
            }
        );
        var prefix = token["prefix"]!.GetValue<string>();
        var consumer = factory.CreateConsumerClient(plain);
        await consumer.GetAsync("/consumer/secrets/DB_PASSWORD?environment=staging");
        await consumer.GetAsync("/consumer/secrets/DB_PASSWORD?environment=local");

        var actor = $"app:{slug}/token:{prefix}";
        var events = await WaitForAuditAsync($"actor={Uri.EscapeDataString(actor)}", 2);
        Assert.Contains(events, e => e["action"]!.GetValue<string>() == "secret.read");
        Assert.Contains(events, e => e["action"]!.GetValue<string>() == "access-token.denied");
    }

    /// <summary>Audit events are written in the background; wait for them.</summary>
    private async Task<List<JsonNode>> WaitForAuditAsync(string query, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var page = JsonNode.Parse(await Mgmt.GetStringAsync($"/management/audit?{query}"))!;
            var items = page["items"]!.AsArray().Select(i => i!).ToList();
            if (items.Count >= count || DateTime.UtcNow > deadline)
                return items;
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task Create_Validates()
    {
        var (slug, _) = await AppAsync();
        var url = $"/management/applications/{slug}/access-tokens";

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await Mgmt.PostAsJsonAsync(url, new { name = "", scopes = SecretsOnly })).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await Mgmt.PostAsJsonAsync(url, new { name = "x", scopes = Array.Empty<string>() })
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await Mgmt.PostAsJsonAsync(url, new { name = "x", scopes = new[] { "write" } })
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await Mgmt.PostAsJsonAsync(
                    url,
                    new
                    {
                        name = "x",
                        scopes = SecretsOnly,
                        environments = Array.Empty<string>(),
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await Mgmt.PostAsJsonAsync(
                    url,
                    new
                    {
                        name = "x",
                        scopes = SecretsOnly,
                        environments = new[] { "nope" },
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await Mgmt.PostAsJsonAsync(
                    url,
                    new
                    {
                        name = "x",
                        scopes = SecretsOnly,
                        expiresAt = DateTimeOffset.UtcNow.AddDays(-1),
                    }
                )
            ).StatusCode
        );

        await CreateTokenAsync(slug, new { name = "dup", scopes = SecretsOnly });
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await Mgmt.PostAsJsonAsync(url, new { name = "dup", scopes = SecretsOnly })).StatusCode
        );
    }

    [Fact]
    public async Task UnknownApplicationOrToken_IsNotFound()
    {
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Mgmt.GetAsync("/management/applications/missing/access-tokens")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await Mgmt.PostAsJsonAsync(
                    "/management/applications/missing/access-tokens",
                    new { name = "x", scopes = SecretsOnly }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await Mgmt.PostAsync(
                    $"/management/applications/missing/access-tokens/{Guid.NewGuid()}/revoke",
                    null
                )
            ).StatusCode
        );
        var (slug, _) = await AppAsync();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await Mgmt.PostAsync(
                    $"/management/applications/{slug}/access-tokens/{Guid.NewGuid()}/revoke",
                    null
                )
            ).StatusCode
        );
    }

    [Fact]
    public async Task TokensOfADeletedApplication_StopWorking()
    {
        var (slug, _) = await AppAsync();
        var (_, plain) = await CreateTokenAsync(slug, new { name = "gone", scopes = SecretsOnly });
        (await Mgmt.DeleteAsync($"/management/applications/{slug}")).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (
                await factory
                    .CreateConsumerClient(plain)
                    .GetAsync("/consumer/secrets/DB_PASSWORD?environment=staging")
            ).StatusCode
        );
    }
}
