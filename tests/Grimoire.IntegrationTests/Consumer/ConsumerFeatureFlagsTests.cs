using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Grimoire.IntegrationTests.Consumer;

public class ConsumerFeatureFlagsTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private HttpClient Mgmt => factory.CreateManagementClient();

    private async Task<(string Slug, HttpClient Consumer)> AppWithFlagAsync()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Mgmt);
        (
            await Mgmt.PostAsJsonAsync(
                $"/management/applications/{slug}/feature-flags",
                new { key = "beta", description = "Beta dashboard" }
            )
        ).EnsureSuccessStatusCode();
        (
            await Mgmt.PutAsJsonAsync(
                $"/management/applications/{slug}/feature-flags/beta/environments/local",
                new
                {
                    enabled = true,
                    rules = new object[]
                    {
                        new
                        {
                            conditions = new object[]
                            {
                                new
                                {
                                    attribute = "country",
                                    @operator = "in",
                                    values = new[] { "IE" },
                                },
                            },
                            serve = new { variant = "on" },
                        },
                    },
                    fallthrough = new { variant = "off" },
                }
            )
        ).EnsureSuccessStatusCode();
        return (slug, factory.CreateConsumerClient(apiKey));
    }

    [Fact]
    public async Task List_ReturnsDefinitionsForLocalEvaluation()
    {
        var (_, consumer) = await AppWithFlagAsync();
        var response = await consumer.GetAsync("/consumer/feature-flags?environment=local");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal("local", body["environment"]!.GetValue<string>());
        var flag = Assert.Single(body["flags"]!.AsArray())!;
        Assert.Equal("beta", flag["key"]!.GetValue<string>());
        Assert.True(flag["enabled"]!.GetValue<bool>());
        Assert.Equal(16, flag["salt"]!.GetValue<string>().Length);
        Assert.Equal("off", flag["defaultVariant"]!.GetValue<string>());
        Assert.Equal("in", flag["rules"]![0]!["conditions"]![0]!["operator"]!.GetValue<string>());
        Assert.Equal(1, flag["version"]!.GetValue<int>());
        Assert.Null(flag["description"]);
    }

    [Fact]
    public async Task Etag_AnswersNotModified_UntilSomethingChanges()
    {
        var (slug, consumer) = await AppWithFlagAsync();
        var first = await consumer.GetAsync("/consumer/feature-flags?environment=local");
        var etag = first.Headers.ETag!;

        var conditional = new HttpRequestMessage(
            HttpMethod.Get,
            "/consumer/feature-flags?environment=local"
        );
        conditional.Headers.IfNoneMatch.Add(etag);
        var notModified = await consumer.SendAsync(conditional);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
        Assert.Empty(await notModified.Content.ReadAsByteArrayAsync());

        await Mgmt.PostAsync(
            $"/management/applications/{slug}/feature-flags/beta/environments/local/toggle",
            null
        );

        var again = new HttpRequestMessage(
            HttpMethod.Get,
            "/consumer/feature-flags?environment=local"
        );
        again.Headers.IfNoneMatch.Add(etag);
        var changed = await consumer.SendAsync(again);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.NotEqual(etag, changed.Headers.ETag);

        var wildcard = new HttpRequestMessage(
            HttpMethod.Get,
            "/consumer/feature-flags/beta?environment=local"
        );
        wildcard.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any);
        Assert.Equal(HttpStatusCode.NotModified, (await consumer.SendAsync(wildcard)).StatusCode);
    }

    [Fact]
    public async Task NotModified_StillCountsAsConsumerActivity()
    {
        var (slug, consumer) = await AppWithFlagAsync();
        var etag = (await consumer.GetAsync("/consumer/feature-flags?environment=local"))
            .Headers
            .ETag!;
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/consumer/feature-flags?environment=local"
        );
        request.Headers.IfNoneMatch.Add(etag);
        Assert.Equal(HttpStatusCode.NotModified, (await consumer.SendAsync(request)).StatusCode);

        var app = JsonNode.Parse(await Mgmt.GetStringAsync($"/management/applications/{slug}"))!;
        Assert.NotNull(app["lastSeenAt"]);
    }

    [Fact]
    public async Task Single_ReturnsOneDefinition_InAnyEnvironment()
    {
        var (slug, consumer) = await AppWithFlagAsync();
        await TestHelpers.CreateEnvironmentAsync(Mgmt, slug, "production");

        var local = JsonNode.Parse(
            await consumer.GetStringAsync("/consumer/feature-flags/beta?environment=local")
        )!;
        Assert.True(local["enabled"]!.GetValue<bool>());

        var production = JsonNode.Parse(
            await consumer.GetStringAsync("/consumer/feature-flags/beta?environment=production")
        )!;
        Assert.False(production["enabled"]!.GetValue<bool>());
        Assert.Equal(0, production["version"]!.GetValue<int>());
        Assert.Empty(production["rules"]!.AsArray());
    }

    [Fact]
    public async Task Errors()
    {
        var (_, consumer) = await AppWithFlagAsync();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await consumer.GetAsync("/consumer/feature-flags/nope?environment=local")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await consumer.GetAsync("/consumer/feature-flags?environment=nope")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await consumer.GetAsync("/consumer/feature-flags/beta?environment=nope")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (
                await factory.CreateClient().GetAsync("/consumer/feature-flags?environment=local")
            ).StatusCode
        );
    }
}
