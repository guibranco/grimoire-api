using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Grimoire.Api.Audit;
using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Infrastructure.Audit;
using Microsoft.Extensions.DependencyInjection;

namespace Grimoire.IntegrationTests.Management;

public class AuditTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private HttpClient Client => factory.CreateManagementClient();

    private async Task<JsonNode> GetAuditAsync(string query)
    {
        var response = await Client.GetAsync($"/management/audit?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    private static List<JsonNode> Items(JsonNode page) =>
        page["items"]!.AsArray().Select(i => i!).ToList();

    /// <summary>Events are written in the background; wait until the expected ones are stored.</summary>
    private async Task<List<JsonNode>> WaitForAsync(string query, Func<List<JsonNode>, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var items = Items(await GetAuditAsync(query));
            if (done(items) || DateTime.UtcNow > deadline)
                return items;
            await Task.Delay(50);
        }
    }

    private static JsonNode? Find(List<JsonNode> items, string action, string? target = null) =>
        items.FirstOrDefault(i =>
            i["action"]!.GetValue<string>() == action
            && (target is null || i["target"]?.GetValue<string>() == target)
        );

    [Fact]
    public async Task List_WithoutAdminKey_Returns401_AndIsAudited()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("audit-probe/1.0");
        var response = await client.GetAsync("/management/audit");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var items = await WaitForAsync("action=auth.failed&actor=anonymous", l =>
            l.Any(i => i["userAgent"]?.GetValue<string>() == "audit-probe/1.0")
        );
        var item = items.First(i => i["userAgent"]?.GetValue<string>() == "audit-probe/1.0");
        Assert.Equal("forbidden", item["outcome"]!.GetValue<string>());
        Assert.Equal("GET /management/audit", item["target"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=201")]
    [InlineData("page=0")]
    [InlineData("outcome=maybe")]
    [InlineData("from=2026-02-01T00:00:00Z&to=2026-01-01T00:00:00Z")]
    public async Task List_WithInvalidQuery_Returns400(string query)
    {
        var response = await Client.GetAsync($"/management/audit?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ManagementChanges_AreRecorded_WithApplicationEnvironmentAndTarget()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var prod = await TestHelpers.CreateEnvironmentAsync(Client, slug, "production");
        await TestHelpers.CreateSecretWithValueAsync(
            Client,
            slug,
            "DB_PASSWORD",
            prod,
            "VALUE-MARKER-pw"
        );
        await TestHelpers.CreateConfigurationAsync(
            Client,
            slug,
            "local",
            "SMTP_HOST",
            "VALUE-MARKER-host"
        );

        var items = await WaitForAsync($"application={slug}", l =>
            Find(l, AuditActions.ConfigurationCreate) is not null
            && Find(l, AuditActions.SecretWrite) is not null
        );

        var created = Find(items, AuditActions.ApplicationCreate)!;
        Assert.Equal("admin", created["actor"]!.GetValue<string>());
        Assert.Equal("Created application", created["description"]!.GetValue<string>());
        Assert.Equal("success", created["outcome"]!.GetValue<string>());

        Assert.Equal(
            "production",
            Find(items, AuditActions.EnvironmentCreate)!["target"]!.GetValue<string>()
        );
        Assert.NotNull(Find(items, AuditActions.SecretCreate, "DB_PASSWORD"));

        var write = Find(items, AuditActions.SecretWrite, "DB_PASSWORD")!;
        Assert.Equal(prod, write["environment"]!.GetValue<string>());
        Assert.Equal("secret", write["targetType"]!.GetValue<string>());

        var config = Find(items, AuditActions.ConfigurationCreate, "SMTP_HOST")!;
        Assert.Equal("local", config["environment"]!.GetValue<string>());
        Assert.Equal(201, config["statusCode"]!.GetValue<int>());

        Assert.DoesNotContain(
            "VALUE-MARKER",
            (await GetAuditAsync($"application={slug}")).ToJsonString()
        );
    }

    [Fact]
    public async Task ConsumerReads_AreRecorded_AsTheApplication_IncludingFailures()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Client);
        await TestHelpers.CreateSecretWithValueAsync(
            Client,
            slug,
            "API_TOKEN",
            "local",
            "VALUE-MARKER-token"
        );
        var consumer = factory.CreateConsumerClient(apiKey);

        (
            await consumer.GetAsync("/consumer/secrets/API_TOKEN?environment=local")
        ).EnsureSuccessStatusCode();
        (
            await consumer.GetAsync("/consumer/configurations?environment=local")
        ).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await consumer.GetAsync("/consumer/secrets/MISSING?environment=local")).StatusCode
        );

        var items = await WaitForAsync($"application={slug}&actor=app:{slug}", l => l.Count >= 3);

        var read = Find(items, AuditActions.SecretRead, "API_TOKEN")!;
        Assert.Equal("local", read["environment"]!.GetValue<string>());
        Assert.Equal("success", read["outcome"]!.GetValue<string>());
        Assert.Equal(
            "notfound",
            Find(items, AuditActions.SecretRead, "MISSING")!["outcome"]!.GetValue<string>()
        );
        Assert.NotNull(Find(items, AuditActions.ConfigurationRead));
        Assert.DoesNotContain("VALUE-MARKER", JsonSerializerNodes(items));
    }

    [Fact]
    public async Task Filters_ActionPrefix_ExcludeReads_Outcome_AndPaging()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Client);
        await TestHelpers.CreateSecretWithValueAsync(Client, slug, "S1", "local", "v");
        (
            await factory
                .CreateConsumerClient(apiKey)
                .GetAsync("/consumer/secrets/S1?environment=local")
        ).EnsureSuccessStatusCode();
        await WaitForAsync($"application={slug}", l =>
            Find(l, AuditActions.SecretRead) is not null
        );

        var secretEvents = Items(await GetAuditAsync($"application={slug}&action=secret.*"));
        Assert.Equal(3, secretEvents.Count); // create, write, read
        Assert.All(secretEvents, i =>
            Assert.StartsWith("secret.", i["action"]!.GetValue<string>())
        );

        var changesOnly = Items(
            await GetAuditAsync($"application={slug}&action=secret.*&includeReads=false")
        );
        Assert.Equal(2, changesOnly.Count);

        Assert.Empty(Items(await GetAuditAsync($"application={slug}&outcome=error")));

        var page = await GetAuditAsync($"application={slug}&pageSize=2&page=2");
        Assert.Equal(2, page["pageSize"]!.GetValue<int>());
        Assert.True(page["totalCount"]!.GetValue<int>() >= 4);
        Assert.Equal(2, Items(page).Count);
    }

    [Fact]
    public async Task Dashboard_RecentActivity_ShowsChangesButNotReads()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Client);
        await TestHelpers.CreateSecretWithValueAsync(Client, slug, "FEED_SECRET", "local", "v");
        (
            await factory
                .CreateConsumerClient(apiKey)
                .GetAsync("/consumer/secrets/FEED_SECRET?environment=local")
        ).EnsureSuccessStatusCode();
        await WaitForAsync($"application={slug}", l =>
            Find(l, AuditActions.SecretRead) is not null
        );

        var dashboard = JsonNode.Parse(await Client.GetStringAsync("/management/dashboard"))!;
        var activity = dashboard["recentActivity"]!.AsArray().Select(a => a!).ToList();

        var mine = activity.Where(a => a["application"]?.GetValue<string>() == slug).ToList();
        Assert.Contains(mine, a =>
            a["action"]!.GetValue<string>() == "Added secret version"
            && a["code"]!.GetValue<string>() == AuditActions.SecretWrite
        );
        Assert.DoesNotContain(activity, a => a["code"]!.GetValue<string>().EndsWith(".read"));
        Assert.DoesNotContain(activity, a =>
            a["code"]!.GetValue<string>() == AuditActions.AuthFailed
        );
    }

    [Fact]
    public async Task Retention_DeletesEventsOlderThanTheWindow()
    {
        using (var scope = factory.Services.CreateScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<AuditStore>()
                .SaveAsync(
                    [
                        new AuditEvent
                        {
                            Id = Guid.NewGuid(),
                            OccurredAt = DateTimeOffset.UtcNow.AddDays(-400),
                            Actor = "admin",
                            Action = AuditActions.BackupCreate,
                            Target = "ancient-retention-test",
                            Outcome = AuditOutcome.Success,
                            StatusCode = 201,
                        },
                    ],
                    default
                );
        }
        Assert.Single(Items(await GetAuditAsync("target=ancient-retention-test")));

        var deleted = await factory
            .Services.GetRequiredService<AuditRetentionService>()
            .PurgeAsync(default);

        Assert.True(deleted >= 1);
        Assert.Empty(Items(await GetAuditAsync("target=ancient-retention-test")));
    }

    private static string JsonSerializerNodes(List<JsonNode> items) =>
        new JsonArray(items.Select(i => i.DeepClone()).ToArray()).ToJsonString();
}
