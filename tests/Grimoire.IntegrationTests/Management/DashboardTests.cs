using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Grimoire.Core.Interfaces;
using Grimoire.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Grimoire.IntegrationTests.Management;

public class DashboardTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private HttpClient Client => factory.CreateManagementClient();

    [Fact]
    public async Task Get_WithoutAdminKey_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/management/dashboard");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("?expiringWithinDays=-1")]
    [InlineData("?staleAfterDays=0")]
    public async Task Get_WithInvalidThresholds_Returns400(string query)
    {
        var response = await Client.GetAsync($"/management/dashboard{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_ReturnsCounts_AndIsNotPubliclyCacheable()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var prod = await TestHelpers.CreateEnvironmentAsync(Client, slug, "production");
        var staging = await TestHelpers.CreateEnvironmentAsync(Client, slug, "staging");
        await TestHelpers.CreateSecretWithValueAsync(Client, slug, "DB_PASSWORD", prod, "x1");
        await SetSecretValueAsync(slug, "DB_PASSWORD", staging, "x2");
        await TestHelpers.CreateConfigurationAsync(Client, slug, prod, "A", "1");
        await TestHelpers.CreateConfigurationAsync(Client, slug, prod, "B", "2");
        await TestHelpers.CreateConfigurationAsync(Client, slug, staging, "A", "1");

        var response = await Client.GetAsync("/management/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cacheControl = response.Headers.CacheControl!;
        Assert.True(cacheControl.Private);
        Assert.False(cacheControl.Public);

        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.NotNull(json["generatedAt"]);
        Assert.True(json["totals"]!["applications"]!.GetValue<int>() >= 1);
        Assert.Null(json["featureFlags"]);
        Assert.Null(json["recentActivity"]);
        Assert.NotNull(json["global"]);

        var app = App(json, slug);
        Assert.True(app["inheritGlobals"]!.GetValue<bool>());
        Assert.Equal(1, app["secretCount"]!.GetValue<int>());
        Assert.Equal(3, app["configurationCount"]!.GetValue<int>());
        Assert.NotNull(app["lastChangedAt"]);
        Assert.Null(app["lastSeenAt"]);

        // Applications are created with a "local" environment.
        var envs = app["environments"]!.AsArray();
        Assert.Equal(3, envs.Count);
        var prodEnv = envs.Single(e => e!["slug"]!.GetValue<string>() == prod)!;
        Assert.Equal(1, prodEnv["secretCount"]!.GetValue<int>());
        Assert.Equal(2, prodEnv["configurationCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task Get_ExcludesSoftDeletedApplications()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var env = await TestHelpers.CreateEnvironmentAsync(Client, slug, "production");
        await TestHelpers.CreateConfigurationAsync(Client, slug, env, "BLANK", "");
        (await Client.DeleteAsync($"/management/applications/{slug}")).EnsureSuccessStatusCode();

        var body = await Client.GetStringAsync("/management/dashboard");

        Assert.DoesNotContain(slug, body);
    }

    [Fact]
    public async Task Get_FlagsExpiredExpiringNoValidVersionAndEmptyConfig()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var env = await TestHelpers.CreateEnvironmentAsync(Client, slug, "production");
        var now = DateTimeOffset.UtcNow;

        await CreateSecretAsync(slug, "EXPIRING", env, "v", expiresAt: now.AddDays(5));
        await CreateSecretAsync(slug, "EXPIRED", env, "v", expiresAt: now.AddDays(-1));
        await CreateSecretAsync(slug, "DISABLED", env, "v", isEnabled: false);
        await CreateSecretAsync(slug, "HEALTHY", env, "v");
        await TestHelpers.CreateConfigurationAsync(Client, slug, env, "SMTP_HOST", "   ");
        await TestHelpers.CreateConfigurationAsync(Client, slug, env, "SMTP_PORT", "25");

        var json = JsonNode.Parse(await Client.GetStringAsync("/management/dashboard"))!;
        var items = Attention(json, slug);

        Assert.Equal(4, items.Count);
        AssertItem(items, "secret-expiring", "warning", "EXPIRING", env);
        AssertItem(items, "secret-expired", "error", "EXPIRED", env);
        AssertItem(items, "secret-no-valid-version", "error", "DISABLED", env);
        AssertItem(items, "config-empty-value", "info", "SMTP_HOST", env);
        Assert.NotNull(items.Single(i => Target(i) == "EXPIRING")["dueAt"]);
    }

    [Fact]
    public async Task Get_ExpiringThreshold_ComesFromQuery()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var env = await TestHelpers.CreateEnvironmentAsync(Client, slug, "production");
        await CreateSecretAsync(
            slug,
            "LATER",
            env,
            "v",
            expiresAt: DateTimeOffset.UtcNow.AddDays(30)
        );

        var byDefault = JsonNode.Parse(await Client.GetStringAsync("/management/dashboard"))!;
        var widened = JsonNode.Parse(
            await Client.GetStringAsync("/management/dashboard?expiringWithinDays=45")
        )!;

        Assert.Empty(Attention(byDefault, slug));
        AssertItem(Attention(widened, slug), "secret-expiring", "warning", "LATER", env);
    }

    [Fact]
    public async Task Get_ResponseNeverContainsSecretOrConfigurationValues()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var env = await TestHelpers.CreateEnvironmentAsync(Client, slug, "production");
        var secretValue = $"s3cr3t-{Guid.NewGuid():N}";
        var configValue = $"c0nf1g-{Guid.NewGuid():N}";
        var globalSecretValue = $"g10bal-{Guid.NewGuid():N}";
        var globalConfigValue = $"g10balcfg-{Guid.NewGuid():N}";

        await CreateSecretAsync(
            slug,
            "API_TOKEN",
            env,
            secretValue,
            expiresAt: DateTimeOffset.UtcNow.AddDays(2)
        );
        await TestHelpers.CreateConfigurationAsync(Client, slug, env, "ENDPOINT", configValue);
        await TestHelpers.CreateGlobalSecretAsync(
            Client,
            TestHelpers.UniqueName("GLOBAL"),
            ("*", globalSecretValue, true)
        );
        await TestHelpers.CreateGlobalConfigurationAsync(
            Client,
            TestHelpers.UniqueName("GCFG"),
            globalConfigValue
        );

        var body = await Client.GetStringAsync("/management/dashboard?expiringWithinDays=30");

        Assert.Contains(slug, body);
        Assert.Contains("API_TOKEN", body);
        Assert.DoesNotContain(secretValue, body);
        Assert.DoesNotContain(configValue, body);
        Assert.DoesNotContain(globalSecretValue, body);
        Assert.DoesNotContain(globalConfigValue, body);

        // Nor the ciphertext of any stored secret version.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();
        var ciphertexts = await db.SecretVersions.Select(v => v.EncryptedValue).ToListAsync();
        Assert.All(ciphertexts, c => Assert.DoesNotContain(c, body));
    }

    [Fact]
    public async Task Get_GlobalSection_ReportsGlobalAttentionWithoutApplication()
    {
        var name = TestHelpers.UniqueName("GEXP");
        var create = await Client.PostAsJsonAsync("/management/global/secrets", new { name });
        create.EnsureSuccessStatusCode();
        (
            await Client.PostAsJsonAsync(
                $"/management/global/secrets/{name}/values",
                new[]
                {
                    new
                    {
                        environmentSlug = "production",
                        value = "v",
                        isEnabled = true,
                        expiresAt = DateTimeOffset.UtcNow.AddDays(-2),
                    },
                }
            )
        ).EnsureSuccessStatusCode();

        var json = JsonNode.Parse(await Client.GetStringAsync("/management/dashboard"))!;

        Assert.True(json["global"]!["secretCount"]!.GetValue<int>() >= 1);
        Assert.NotNull(json["global"]!["lastChangedAt"]);
        var item = json["attention"]!.AsArray().Single(i => Target(i!) == name)!;
        Assert.Equal("secret-expired", item["kind"]!.GetValue<string>());
        Assert.Null(item["application"]);
        Assert.Equal("production", item["environment"]!.GetValue<string>());
    }

    [Fact]
    public async Task Get_WithConsumerActivity_FlagsStaleAndNeverSeen()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var prod = await TestHelpers.CreateEnvironmentAsync(Client, slug, "production");
        var staging = await TestHelpers.CreateEnvironmentAsync(Client, slug, "staging");
        await TestHelpers.CreateConfigurationAsync(Client, slug, prod, "A", "1");
        await TestHelpers.CreateConfigurationAsync(Client, slug, staging, "A", "1");

        Guid appId,
            prodId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();
            var env = await db
                .Environments.Where(e => e.Application.Slug == slug && e.Slug == prod)
                .SingleAsync();
            (appId, prodId) = (env.ApplicationId, env.Id);
        }

        var lastSeen = DateTimeOffset.UtcNow.AddDays(-10);
        var reader = new FakeConsumerActivityReader(
            new Dictionary<(Guid, Guid), DateTimeOffset> { [(appId, prodId)] = lastSeen }
        );
        using var tracked = factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddScoped<IConsumerActivityReader>(_ => reader))
        );
        var client = tracked.CreateClient();
        client.DefaultRequestHeaders.Add(
            "Authorization",
            $"Bearer {GrimoireWebApplicationFactory.AdminKey}"
        );

        var json = JsonNode.Parse(await client.GetStringAsync("/management/dashboard"))!;
        var items = Attention(json, slug);

        Assert.Equal(2, items.Count);
        AssertItem(items, "consumer-stale", "warning", null, prod);
        AssertItem(items, "consumer-never-seen", "info", null, staging);
        Assert.NotNull(
            items.Single(i => i["kind"]!.GetValue<string>() == "consumer-stale")["lastSeenAt"]
        );
        Assert.NotNull(App(json, slug)["lastSeenAt"]);

        var relaxed = JsonNode.Parse(
            await client.GetStringAsync("/management/dashboard?staleAfterDays=30")
        )!;
        AssertItem(Attention(relaxed, slug), "consumer-never-seen", "info", null, staging);
        Assert.Single(Attention(relaxed, slug));
    }

    private sealed class FakeConsumerActivityReader(
        IReadOnlyDictionary<(Guid, Guid), DateTimeOffset> lastSeen
    ) : IConsumerActivityReader
    {
        public Task<IReadOnlyDictionary<
            (Guid ApplicationId, Guid EnvironmentId),
            DateTimeOffset
        >?> GetLastSeenAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<(Guid, Guid), DateTimeOffset>?>(lastSeen);
    }

    private async Task CreateSecretAsync(
        string appSlug,
        string name,
        string envSlug,
        string value,
        DateTimeOffset? expiresAt = null,
        bool isEnabled = true
    )
    {
        (
            await Client.PostAsJsonAsync(
                $"/management/applications/{appSlug}/secrets",
                new { name }
            )
        ).EnsureSuccessStatusCode();
        await SetSecretValueAsync(appSlug, name, envSlug, value, expiresAt, isEnabled);
    }

    private async Task SetSecretValueAsync(
        string appSlug,
        string name,
        string envSlug,
        string value,
        DateTimeOffset? expiresAt = null,
        bool isEnabled = true
    ) =>
        (
            await Client.PostAsJsonAsync(
                $"/management/applications/{appSlug}/secrets/{name}/values",
                new[]
                {
                    new
                    {
                        environmentSlug = envSlug,
                        value,
                        isEnabled,
                        expiresAt,
                    },
                }
            )
        ).EnsureSuccessStatusCode();

    private static JsonNode App(JsonNode json, string slug) =>
        json["applications"]!.AsArray().Single(a => a!["slug"]!.GetValue<string>() == slug)!;

    private static List<JsonNode> Attention(JsonNode json, string slug) =>
        json["attention"]!
            .AsArray()
            .Where(i => i!["application"]?.GetValue<string>() == slug)
            .Select(i => i!)
            .ToList();

    private static string? Target(JsonNode item) => item["target"]?.GetValue<string>();

    private static void AssertItem(
        List<JsonNode> items,
        string kind,
        string severity,
        string? target,
        string environment
    )
    {
        var item = items.SingleOrDefault(i =>
            i["kind"]!.GetValue<string>() == kind
            && Target(i) == target
            && i["environment"]!.GetValue<string>() == environment
        );
        Assert.NotNull(item);
        Assert.Equal(severity, item["severity"]!.GetValue<string>());
    }
}
