using System.Net;
using System.Text.Json.Nodes;
using Grimoire.Api.ConsumerTracking;
using Grimoire.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Grimoire.IntegrationTests.Management;

public class ReportsTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private HttpClient MgmtClient => factory.CreateManagementClient();

    private async Task<JsonNode> GetJsonAsync(string url) =>
        JsonNode.Parse(await MgmtClient.GetStringAsync(url))!;

    private static JsonNode? FindUnused(JsonNode report, string? app, string secret) =>
        report["items"]!
            .AsArray()
            .SingleOrDefault(i =>
                i!["secret"]!.GetValue<string>() == secret
                && i["application"]?.GetValue<string>() == app
            );

    [Fact]
    public async Task UnusedSecrets_ListsNeverReadSecrets_AndDropsThemOnceRead()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);
        var used = TestHelpers.UniqueName("used");
        var idle = TestHelpers.UniqueName("idle");
        await TestHelpers.CreateSecretWithValueAsync(MgmtClient, slug, used, "local", "v");
        await TestHelpers.CreateSecretWithValueAsync(MgmtClient, slug, idle, "local", "v");

        var before = await GetJsonAsync("/management/reports/unused-secrets");
        Assert.Equal(90, before["days"]!.GetValue<int>());
        var neverRead = FindUnused(before, slug, used)!;
        Assert.Null(neverRead["lastReadAt"]);
        Assert.Empty(neverRead["reads"]!.AsArray());
        Assert.Null(neverRead["value"]);

        var consumer = factory.CreateConsumerClient(apiKey);
        (
            await consumer.GetAsync($"/consumer/secrets/{used}?environment=local")
        ).EnsureSuccessStatusCode();

        // Buffered reads count straight away, before any flush.
        var after = await GetJsonAsync("/management/reports/unused-secrets");
        Assert.Null(FindUnused(after, slug, used));
        Assert.NotNull(FindUnused(after, slug, idle));
    }

    [Fact]
    public async Task UnusedSecrets_ReadsSurviveAFlush_AndShowPerEnvironment()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);
        var secret = TestHelpers.UniqueName("flushed");
        await TestHelpers.CreateSecretWithValueAsync(MgmtClient, slug, secret, "local", "v");

        var consumer = factory.CreateConsumerClient(apiKey);
        for (var i = 0; i < 2; i++)
            (
                await consumer.GetAsync($"/consumer/secrets/{secret}?environment=local")
            ).EnsureSuccessStatusCode();
        await factory.Services.GetRequiredService<ConsumerActivityFlusher>().FlushAsync(default);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();
            var secretId = (await db.Secrets.SingleAsync(s => s.Name == secret)).Id;
            var row = await db.SecretReads.SingleAsync(r => r.SecretId == secretId);
            Assert.Equal(2, row.ReadCount);
            Assert.Null(row.GlobalSecretId);

            // Pretend the last read was long ago, so it falls outside a 1-day window.
            row.LastReadAt = DateTimeOffset.UtcNow.AddDays(-10);
            await db.SaveChangesAsync();
        }

        var report = await GetJsonAsync("/management/reports/unused-secrets?days=1");
        var item = FindUnused(report, slug, secret)!;
        Assert.NotNull(item["lastReadAt"]);
        Assert.Equal(2, item["readCount"]!.GetValue<long>());
        var read = Assert.Single(item["reads"]!.AsArray())!;
        Assert.Equal(slug, read["application"]!.GetValue<string>());
        Assert.Equal("local", read["environment"]!.GetValue<string>());

        var wide = await GetJsonAsync("/management/reports/unused-secrets?days=30");
        Assert.Null(FindUnused(wide, slug, secret));
    }

    [Fact]
    public async Task GlobalSecretRead_IsAttributedToTheGlobal_AndShowsAsAReadEdge()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);
        var global = TestHelpers.UniqueName("gread");
        await TestHelpers.CreateGlobalSecretAsync(MgmtClient, global, ("*", "v", true));

        var before = await GetJsonAsync("/management/reports/unused-secrets");
        var item = FindUnused(before, null, global)!;
        Assert.True(item["isGlobal"]!.GetValue<bool>());

        var consumer = factory.CreateConsumerClient(apiKey);
        (
            await consumer.GetAsync($"/consumer/secrets/{global}?environment=local")
        ).EnsureSuccessStatusCode();

        Assert.Null(
            FindUnused(await GetJsonAsync("/management/reports/unused-secrets"), null, global)
        );

        var graph = await GetJsonAsync("/management/reports/dependencies");
        var edge = graph["edges"]!
            .AsArray()
            .Single(e =>
                e!["source"]!.GetValue<string>() == $"app:{slug}"
                && e["target"]!.GetValue<string>() == $"global-secret:{global}"
            )!;
        Assert.Equal("reads", edge["kind"]!.GetValue<string>());
        Assert.Equal("local", Assert.Single(edge["environments"]!.AsArray())!.GetValue<string>());
        Assert.Equal(1, edge["readCount"]!.GetValue<long>());
    }

    [Fact]
    public async Task Dependencies_ShowOwnershipOverridesAndInheritance()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(MgmtClient);
        var secret = TestHelpers.UniqueName("dep");
        var key = TestHelpers.UniqueName("depkey");
        var globalKey = TestHelpers.UniqueName("gkey");
        await TestHelpers.CreateSecretWithValueAsync(MgmtClient, slug, secret, "local", "v");
        await TestHelpers.CreateGlobalSecretAsync(MgmtClient, secret, ("*", "g", true));
        await TestHelpers.CreateConfigurationAsync(MgmtClient, slug, "local", key, "1");
        await TestHelpers.CreateGlobalConfigurationAsync(MgmtClient, key, "0");
        await TestHelpers.CreateGlobalConfigurationAsync(MgmtClient, globalKey, "x");

        var graph = await GetJsonAsync("/management/reports/dependencies");
        var nodes = graph["nodes"]!.AsArray();
        var edges = graph["edges"]!.AsArray();

        bool HasEdge(string source, string target, string kind) =>
            edges.Any(e =>
                e!["source"]!.GetValue<string>() == source
                && e["target"]!.GetValue<string>() == target
                && e["kind"]!.GetValue<string>() == kind
            );

        var configNode = nodes.Single(n => n!["id"]!.GetValue<string>() == $"config:{slug}/{key}")!;
        Assert.Equal("config-key", configNode["type"]!.GetValue<string>());
        Assert.Equal(slug, configNode["application"]!.GetValue<string>());
        Assert.Equal(
            "local",
            Assert.Single(configNode["environments"]!.AsArray())!.GetValue<string>()
        );

        Assert.True(HasEdge($"app:{slug}", $"secret:{slug}/{secret}", "owns"));
        Assert.True(HasEdge($"secret:{slug}/{secret}", $"global-secret:{secret}", "overrides"));
        Assert.True(HasEdge($"app:{slug}", $"config:{slug}/{key}", "owns"));
        Assert.True(HasEdge($"config:{slug}/{key}", $"global-config:{key}", "overrides"));
        Assert.True(HasEdge($"app:{slug}", $"global-config:{globalKey}", "inherits"));
        Assert.False(HasEdge($"app:{slug}", $"global-config:{key}", "inherits"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3651)]
    public async Task UnusedSecrets_RejectsAnOutOfRangeWindow(int days)
    {
        var response = await MgmtClient.GetAsync($"/management/reports/unused-secrets?days={days}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reports_RequireTheAdminKey()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/management/reports/unused-secrets")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/management/reports/dependencies")).StatusCode
        );
    }
}
