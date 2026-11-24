using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Grimoire.IntegrationTests.Management;

/// <summary>Sentinel keys, auto-publish and explicit publishing (guibranco/grimoire-api#95).</summary>
public class PublishingTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private const string Env = "local";
    private HttpClient MgmtClient => factory.CreateManagementClient();

    private async Task<JsonNode> GetPublishingAsync(string slug)
    {
        var response = await MgmtClient.GetAsync(
            $"/management/applications/{slug}/environments/{Env}/publishing"
        );
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    private static async Task<string?> SentinelAsync(HttpClient consumer, string key = "Sentinel")
    {
        var response = await consumer.GetAsync($"/consumer/configurations/{key}?environment={Env}");
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        return json["value"]!.GetValue<string>();
    }

    private async Task SetAutoPublishAsync(string slug, bool autoPublish)
    {
        var response = await MgmtClient.PutAsJsonAsync(
            $"/management/applications/{slug}/environments/{Env}/publishing",
            new { autoPublish }
        );
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task NewEnvironment_AutoPublishes_AndHasNoSentinelYet()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);

        var status = await GetPublishingAsync(slug);

        Assert.True(status["autoPublish"]!.GetValue<bool>());
        Assert.Equal("Sentinel", status["sentinelKey"]!.GetValue<string>());
        Assert.Null(status["sentinelValue"]);
        Assert.False(status["hasPendingChanges"]!.GetValue<bool>());
        Assert.Null(await SentinelAsync(factory.CreateConsumerClient(apiKey)));
    }

    [Fact]
    public async Task AutoPublish_BumpsSentinel_OnCreateUpdateAndDelete()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);
        var consumer = factory.CreateConsumerClient(apiKey);

        await TestHelpers.CreateConfigurationAsync(MgmtClient, slug, Env, "MaxRetries", "3");
        var afterCreate = await SentinelAsync(consumer);
        Assert.NotNull(afterCreate);

        (
            await MgmtClient.PutAsJsonAsync(
                $"/management/applications/{slug}/configurations/{Env}/MaxRetries",
                new { value = "5" }
            )
        ).EnsureSuccessStatusCode();
        var afterUpdate = await SentinelAsync(consumer);
        Assert.True(long.Parse(afterUpdate!) > long.Parse(afterCreate));

        (
            await MgmtClient.DeleteAsync(
                $"/management/applications/{slug}/configurations/{Env}/MaxRetries"
            )
        ).EnsureSuccessStatusCode();
        var afterDelete = await SentinelAsync(consumer);
        Assert.True(long.Parse(afterDelete!) > long.Parse(afterUpdate!));
    }

    [Fact]
    public async Task AutoPublish_BumpsSentinel_WhenASecretValueIsAdded()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);
        var consumer = factory.CreateConsumerClient(apiKey);

        await TestHelpers.CreateSecretWithValueAsync(MgmtClient, slug, "db-password", Env, "x");

        Assert.NotNull(await SentinelAsync(consumer));
    }

    [Fact]
    public async Task AutoPublishOff_StagesChanges_UntilPublished()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);
        var consumer = factory.CreateConsumerClient(apiKey);
        await TestHelpers.CreateConfigurationAsync(MgmtClient, slug, Env, "A", "1");
        var published = await SentinelAsync(consumer);
        await SetAutoPublishAsync(slug, false);

        await TestHelpers.CreateConfigurationAsync(MgmtClient, slug, Env, "B", "2");
        await TestHelpers.CreateConfigurationAsync(MgmtClient, slug, Env, "C", "3");

        Assert.Equal(published, await SentinelAsync(consumer));
        var pending = await GetPublishingAsync(slug);
        Assert.False(pending["autoPublish"]!.GetValue<bool>());
        Assert.True(pending["hasPendingChanges"]!.GetValue<bool>());

        var publish = await MgmtClient.PostAsync(
            $"/management/applications/{slug}/environments/{Env}/publish",
            null
        );
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        var status = JsonNode.Parse(await publish.Content.ReadAsStringAsync())!;
        Assert.False(status["hasPendingChanges"]!.GetValue<bool>());
        var bumped = await SentinelAsync(consumer);
        Assert.NotEqual(published, bumped);
        Assert.Equal(bumped, status["sentinelValue"]!.GetValue<string>());
    }

    [Fact]
    public async Task TurningAutoPublishBackOn_PublishesPendingChanges()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);
        var consumer = factory.CreateConsumerClient(apiKey);
        await SetAutoPublishAsync(slug, false);
        await TestHelpers.CreateConfigurationAsync(MgmtClient, slug, Env, "A", "1");
        Assert.Null(await SentinelAsync(consumer));

        await SetAutoPublishAsync(slug, true);

        Assert.NotNull(await SentinelAsync(consumer));
        Assert.False((await GetPublishingAsync(slug))["hasPendingChanges"]!.GetValue<bool>());
    }

    [Fact]
    public async Task WritingTheSentinelDirectly_CountsAsAPublish()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(MgmtClient);
        await SetAutoPublishAsync(slug, false);
        await TestHelpers.CreateConfigurationAsync(MgmtClient, slug, Env, "A", "1");

        await TestHelpers.CreateConfigurationAsync(MgmtClient, slug, Env, "Sentinel", "manual");

        Assert.False((await GetPublishingAsync(slug))["hasPendingChanges"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ChangingGlobalInheritance_BumpsEnvironmentSentinels()
    {
        var name = TestHelpers.UniqueName("app");
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient, name);
        var consumer = factory.CreateConsumerClient(apiKey);

        (
            await MgmtClient.PutAsJsonAsync(
                $"/management/applications/{slug}",
                new { name, inheritGlobals = false }
            )
        ).EnsureSuccessStatusCode();

        Assert.NotNull(await SentinelAsync(consumer));
    }

    [Fact]
    public async Task UpdatePublishing_Returns400_WhenAutoPublishIsMissing()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(MgmtClient);

        var response = await MgmtClient.PutAsJsonAsync(
            $"/management/applications/{slug}/environments/{Env}/publishing",
            new { }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await GetPublishingAsync(slug))["autoPublish"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Publishing_Returns404_ForUnknownEnvironment()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(MgmtClient);

        var response = await MgmtClient.PostAsync(
            $"/management/applications/{slug}/environments/nope/publish",
            null
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GlobalChange_BumpsGlobalSentinel_AtTheStarLayer_EvenForASlugLayerChange()
    {
        var (_, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);
        var consumer = factory.CreateConsumerClient(apiKey);
        var before = await SentinelAsync(consumer, "Global:Sentinel");

        await TestHelpers.CreateGlobalConfigurationAsync(
            MgmtClient,
            TestHelpers.UniqueName("Shared"),
            "x",
            Env
        );

        var after = await SentinelAsync(consumer, "Global:Sentinel");
        Assert.NotNull(after);
        Assert.NotEqual(before, after);
        var status = JsonNode.Parse(
            await MgmtClient.GetStringAsync("/management/global/publishing")
        )!;
        Assert.Equal("Global:Sentinel", status["sentinelKey"]!.GetValue<string>());
        Assert.Equal(after, status["sentinelValue"]!.GetValue<string>());
    }

    [Fact]
    public async Task GlobalAutoPublishOff_StagesGlobalChanges_UntilPublished()
    {
        var (_, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);
        var consumer = factory.CreateConsumerClient(apiKey);
        try
        {
            (
                await MgmtClient.PutAsJsonAsync(
                    "/management/global/publishing",
                    new { autoPublish = false }
                )
            ).EnsureSuccessStatusCode();
            var before = await SentinelAsync(consumer, "Global:Sentinel");

            await TestHelpers.CreateGlobalConfigurationAsync(
                MgmtClient,
                TestHelpers.UniqueName("Staged"),
                "x"
            );

            Assert.Equal(before, await SentinelAsync(consumer, "Global:Sentinel"));
            var pending = JsonNode.Parse(
                await MgmtClient.GetStringAsync("/management/global/publishing")
            )!;
            Assert.True(pending["hasPendingChanges"]!.GetValue<bool>());

            var publish = await MgmtClient.PostAsync("/management/global/publish", null);
            publish.EnsureSuccessStatusCode();
            Assert.NotEqual(before, await SentinelAsync(consumer, "Global:Sentinel"));
            var status = JsonNode.Parse(await publish.Content.ReadAsStringAsync())!;
            Assert.False(status["hasPendingChanges"]!.GetValue<bool>());
        }
        finally
        {
            // Globals are shared by every test in the fixture: restore the default.
            (
                await MgmtClient.PutAsJsonAsync(
                    "/management/global/publishing",
                    new { autoPublish = true }
                )
            ).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Sentinels_AreLeftOutOfTheConsumerListAndExport()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);
        await TestHelpers.CreateConfigurationAsync(MgmtClient, slug, Env, "A", "1");
        await TestHelpers.CreateGlobalConfigurationAsync(
            MgmtClient,
            TestHelpers.UniqueName("G"),
            "g"
        );
        var consumer = factory.CreateConsumerClient(apiKey);
        Assert.NotNull(await SentinelAsync(consumer));
        Assert.NotNull(await SentinelAsync(consumer, "Global:Sentinel"));

        var list = await consumer.GetStringAsync($"/consumer/configurations?environment={Env}");
        var export = await consumer.GetStringAsync(
            $"/consumer/export?environment={Env}&include=configurations&format=env"
        );

        Assert.DoesNotContain("Sentinel", list);
        Assert.DoesNotContain("SENTINEL", export);
        Assert.Contains("\"A\"", list);
    }

    [Fact]
    public async Task DeletingASecret_BumpsTheSentinel()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);
        var consumer = factory.CreateConsumerClient(apiKey);
        await TestHelpers.CreateSecretWithValueAsync(MgmtClient, slug, "db-password", Env, "x");
        var before = await SentinelAsync(consumer);

        (
            await MgmtClient.DeleteAsync($"/management/applications/{slug}/secrets/db-password")
        ).EnsureSuccessStatusCode();

        Assert.NotEqual(before, await SentinelAsync(consumer));
    }

    [Fact]
    public async Task SentinelKeys_AreReservedOutsideTheirPlace()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(MgmtClient);

        var appGlobalSentinel = await MgmtClient.PostAsJsonAsync(
            $"/management/applications/{slug}/configurations",
            new
            {
                environmentSlug = Env,
                key = "Global:Sentinel",
                value = "1",
            }
        );
        var globalSlugLayer = await MgmtClient.PostAsJsonAsync(
            "/management/global/configurations",
            new
            {
                environmentSlug = Env,
                key = "Global:Sentinel",
                value = "1",
            }
        );
        var globalAppSentinel = await MgmtClient.PostAsJsonAsync(
            "/management/global/configurations",
            new { key = "Sentinel", value = "1" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, appGlobalSentinel.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, globalSlugLayer.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, globalAppSentinel.StatusCode);
    }

    [Fact]
    public async Task ConcurrentChanges_EachGetADistinctSentinelValue()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(MgmtClient);
        var consumer = factory.CreateConsumerClient(apiKey);
        await TestHelpers.CreateConfigurationAsync(MgmtClient, slug, Env, "Seed", "0");

        await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(i =>
                    TestHelpers.CreateConfigurationAsync(MgmtClient, slug, Env, $"K{i}", $"{i}")
                )
        );

        var history = JsonNode
            .Parse(
                await MgmtClient.GetStringAsync(
                    $"/management/applications/{slug}/configurations/{Env}/Sentinel/history"
                )
            )!
            .AsArray();
        var values = history.Select(r => r!["value"]!.GetValue<string>()).ToList();
        Assert.Equal(9, values.Count);
        Assert.Equal(values.Count, values.Distinct().Count());
        Assert.Equal(values.Max(long.Parse).ToString(), await SentinelAsync(consumer));
    }
}
