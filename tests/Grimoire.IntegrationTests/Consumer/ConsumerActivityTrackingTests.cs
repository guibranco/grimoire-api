using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Grimoire.Api.ConsumerTracking;
using Grimoire.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Grimoire.IntegrationTests.Consumer;

public class ConsumerActivityTrackingTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private HttpClient MgmtClient => factory.CreateManagementClient();

    private async Task<JsonNode> GetJsonAsync(string url) =>
        JsonNode.Parse(await MgmtClient.GetStringAsync(url))!;

    [Fact]
    public async Task SuccessfulRead_ShowsUpAsLastSeen_InApplicationEnvironmentsAndDashboard()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);
        var secret = TestHelpers.UniqueName("seen");
        await TestHelpers.CreateSecretWithValueAsync(MgmtClient, slug, secret, "local", "v");

        var before = await GetJsonAsync($"/management/applications/{slug}");
        Assert.Null(before["lastSeenAt"]);

        var consumer = factory.CreateConsumerClient(apiKey);
        consumer.DefaultRequestHeaders.UserAgent.ParseAdd("grimoire-tests/1.0");
        var read = await consumer.GetAsync($"/consumer/secrets/{secret}?environment=local");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var app = await GetJsonAsync($"/management/applications/{slug}");
        Assert.NotNull(app["lastSeenAt"]);

        var list = (await GetJsonAsync("/management/applications")).AsArray();
        Assert.NotNull(list.Single(a => a!["slug"]!.GetValue<string>() == slug)!["lastSeenAt"]);

        var envs = (await GetJsonAsync($"/management/applications/{slug}/environments")).AsArray();
        Assert.NotNull(envs.Single(e => e!["slug"]!.GetValue<string>() == "local")!["lastSeenAt"]);

        var dashboard = await GetJsonAsync("/management/dashboard");
        var dashApp = dashboard["applications"]!
            .AsArray()
            .Single(a => a!["slug"]!.GetValue<string>() == slug)!;
        Assert.NotNull(dashApp["lastSeenAt"]);
        Assert.DoesNotContain(dashboard["attention"]!.AsArray(), i =>
            i!["application"]?.GetValue<string>() == slug
            && i["kind"]!.GetValue<string>() == "consumer-never-seen"
        );
    }

    [Fact]
    public async Task ConfigurationRead_IsTrackedToo()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);

        var consumer = factory.CreateConsumerClient(apiKey);
        var read = await consumer.GetAsync("/consumer/configurations?environment=local");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var app = await GetJsonAsync($"/management/applications/{slug}");
        Assert.NotNull(app["lastSeenAt"]);
    }

    [Fact]
    public async Task FailedRead_IsNotTracked()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);

        var consumer = factory.CreateConsumerClient(apiKey);
        var read = await consumer.GetAsync("/consumer/secrets/missing?environment=local");
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);

        var app = await GetJsonAsync($"/management/applications/{slug}");
        Assert.Null(app["lastSeenAt"]);
    }

    [Fact]
    public async Task Flush_PersistsActivity_WithClientDetailsButNoValues()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);
        var secret = TestHelpers.UniqueName("flush");
        await TestHelpers.CreateSecretWithValueAsync(MgmtClient, slug, secret, "local", "v");

        var consumer = factory.CreateConsumerClient(apiKey);
        consumer.DefaultRequestHeaders.UserAgent.ParseAdd("grimoire-tests/2.0");
        for (var i = 0; i < 3; i++)
            (
                await consumer.GetAsync($"/consumer/secrets/{secret}?environment=local")
            ).EnsureSuccessStatusCode();

        await factory.Services.GetRequiredService<ConsumerActivityFlusher>().FlushAsync(default);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();
        var appId = (await db.Applications.SingleAsync(a => a.Slug == slug)).Id;
        var row = await db.ConsumerActivities.SingleAsync(a => a.ApplicationId == appId);
        Assert.Equal("grimoire-tests/2.0", row.LastUserAgent);
        Assert.Equal(3, row.RequestCount);
    }
}
