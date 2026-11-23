using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Grimoire.Core.Audit;

namespace Grimoire.IntegrationTests.Management;

public class SecretVersionLifecycleTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private HttpClient Client => factory.CreateManagementClient();

    private async Task<(string Slug, string ApiKey, string Secret)> AppWithSecretAsync(
        string firstValue = "v1"
    )
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Client);
        var secret = TestHelpers.UniqueName("S");
        await TestHelpers.CreateSecretWithValueAsync(Client, slug, secret, "local", firstValue);
        return (slug, apiKey, secret);
    }

    private Task<HttpResponseMessage> AddValueAsync(
        string slug,
        string secret,
        string value,
        int? graceDays = null,
        DateTimeOffset? expiresAt = null,
        bool isEnabled = true
    ) =>
        Client.PostAsJsonAsync(
            $"/management/applications/{slug}/secrets/{secret}/values",
            new[]
            {
                new
                {
                    environmentSlug = "local",
                    value,
                    isEnabled,
                    expiresAt,
                    previousVersionGraceDays = graceDays,
                },
            }
        );

    private async Task<List<JsonNode>> VersionsAsync(string slug, string secret) =>
        JsonNode
            .Parse(
                await Client.GetStringAsync(
                    $"/management/applications/{slug}/secrets/{secret}/versions/local"
                )
            )!
            .AsArray()
            .Select(v => v!)
            .ToList();

    private static JsonNode Version(List<JsonNode> versions, int n) =>
        versions.Single(v => v["version"]!.GetValue<int>() == n);

    private Task<HttpResponseMessage> PatchAsync(
        string slug,
        string secret,
        int version,
        object body,
        bool force = false
    ) =>
        Client.PatchAsJsonAsync(
            $"/management/applications/{slug}/secrets/{secret}/versions/local/{version}{(force ? "?force=true" : "")}",
            body
        );

    private async Task<string?> ConsumerValueAsync(string apiKey, string secret)
    {
        var response = await factory
            .CreateConsumerClient(apiKey)
            .GetAsync($"/consumer/secrets/{secret}?environment=local");
        return response.IsSuccessStatusCode
            ? JsonNode.Parse(await response.Content.ReadAsStringAsync())![
                "value"
            ]!.GetValue<string>()
            : null;
    }

    [Fact]
    public async Task Versions_ShowWhichOneIsCurrent_AndEachStatus()
    {
        var (slug, _, secret) = await AppWithSecretAsync();
        (await AddValueAsync(slug, secret, "v2")).EnsureSuccessStatusCode();
        (
            await Client.PostAsJsonAsync(
                $"/management/applications/{slug}/secrets/{secret}/values",
                new[]
                {
                    new
                    {
                        environmentSlug = "local",
                        value = "v3",
                        notBefore = DateTimeOffset.UtcNow.AddDays(1),
                    },
                }
            )
        ).EnsureSuccessStatusCode();

        var versions = await VersionsAsync(slug, secret);

        Assert.Equal(new[] { 3, 2, 1 }, versions.Select(v => v["version"]!.GetValue<int>()));
        Assert.Equal("scheduled", Version(versions, 3)["status"]!.GetValue<string>());
        Assert.Equal("current", Version(versions, 2)["status"]!.GetValue<string>());
        Assert.True(Version(versions, 2)["isCurrent"]!.GetValue<bool>());
        Assert.Equal("valid", Version(versions, 1)["status"]!.GetValue<string>());
        Assert.False(Version(versions, 1)["isCurrent"]!.GetValue<bool>());
    }

    [Fact]
    public async Task DisablingTheCurrentVersion_FallsBackToThePreviousOne()
    {
        var (slug, apiKey, secret) = await AppWithSecretAsync();
        (await AddValueAsync(slug, secret, "v2")).EnsureSuccessStatusCode();
        Assert.Equal("v2", await ConsumerValueAsync(apiKey, secret));

        var response = await PatchAsync(slug, secret, 2, new { isEnabled = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal("disabled", body["status"]!.GetValue<string>());
        Assert.Equal("v1", await ConsumerValueAsync(apiKey, secret));
        Assert.True(Version(await VersionsAsync(slug, secret), 1)["isCurrent"]!.GetValue<bool>());
    }

    [Fact]
    public async Task RemovingTheOnlyValidVersion_Returns409_UnlessForced()
    {
        var (slug, apiKey, secret) = await AppWithSecretAsync();

        var refused = await PatchAsync(slug, secret, 1, new { isEnabled = false });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var problem = await refused.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(409, problem!["status"]!.GetValue<int>());
        Assert.Contains("no valid version", problem["detail"]!.GetValue<string>());
        var expiredRefused = await PatchAsync(
            slug,
            secret,
            1,
            new { expiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) }
        );
        Assert.Equal(HttpStatusCode.Conflict, expiredRefused.StatusCode);
        Assert.Equal("v1", await ConsumerValueAsync(apiKey, secret));

        var forced = await PatchAsync(slug, secret, 1, new { isEnabled = false }, force: true);
        Assert.Equal(HttpStatusCode.OK, forced.StatusCode);
        Assert.Null(await ConsumerValueAsync(apiKey, secret));

        // Re-enabling needs no force.
        Assert.Equal(
            HttpStatusCode.OK,
            (await PatchAsync(slug, secret, 1, new { isEnabled = true })).StatusCode
        );
        Assert.Equal("v1", await ConsumerValueAsync(apiKey, secret));
    }

    [Fact]
    public async Task ConcurrentDisables_CannotBothPassTheGuard()
    {
        var (slug, apiKey, secret) = await AppWithSecretAsync();
        await AddValueAsync(slug, secret, "v2");

        // Each request alone leaves one valid version; together they would leave none.
        for (var round = 0; round < 5; round++)
        {
            var results = await Task.WhenAll(
                PatchAsync(slug, secret, 1, new { isEnabled = false }),
                PatchAsync(slug, secret, 2, new { isEnabled = false })
            );
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
            Assert.NotNull(await ConsumerValueAsync(apiKey, secret));

            await PatchAsync(slug, secret, 1, new { isEnabled = true });
            await PatchAsync(slug, secret, 2, new { isEnabled = true });
        }
    }

    [Fact]
    public async Task ExpiryCanBeSetAndCleared()
    {
        var (slug, _, secret) = await AppWithSecretAsync();
        var expiresAt = DateTimeOffset.UtcNow.AddDays(10);

        var set = JsonNode.Parse(
            await (await PatchAsync(slug, secret, 1, new { expiresAt })).Content.ReadAsStringAsync()
        )!;
        Assert.Equal(
            expiresAt.ToUnixTimeSeconds(),
            set["expiresAt"]!.GetValue<DateTimeOffset>().ToUnixTimeSeconds()
        );

        var cleared = JsonNode.Parse(
            await (
                await PatchAsync(slug, secret, 1, new { clearExpiresAt = true })
            ).Content.ReadAsStringAsync()
        )!;
        Assert.Null(cleared["expiresAt"]);
    }

    [Fact]
    public async Task InvalidUpdates_AreRejected()
    {
        var (slug, _, secret) = await AppWithSecretAsync();
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await PatchAsync(
                    slug,
                    secret,
                    1,
                    new { notBefore = now.AddDays(2), expiresAt = now.AddDays(1) }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await PatchAsync(
                    slug,
                    secret,
                    1,
                    new { expiresAt = now.AddDays(1), clearExpiresAt = true }
                )
            ).StatusCode
        );
        // Inverted only after merging with the stored window.
        (
            await PatchAsync(slug, secret, 1, new { expiresAt = now.AddDays(5) })
        ).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await PatchAsync(slug, secret, 1, new { notBefore = now.AddDays(6) })).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await PatchAsync(slug, secret, 99, new { isEnabled = false })).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await Client.PatchAsJsonAsync(
                    $"/management/applications/{slug}/secrets/{secret}/versions/nope/1",
                    new { isEnabled = false }
                )
            ).StatusCode
        );
    }

    [Fact]
    public async Task GracePeriod_ExpiresPreviousVersions_InTheSameRequest()
    {
        var (slug, apiKey, secret) = await AppWithSecretAsync();
        (
            await AddValueAsync(slug, secret, "v2", expiresAt: DateTimeOffset.UtcNow.AddDays(60))
        ).EnsureSuccessStatusCode();
        (await AddValueAsync(slug, secret, "v3", isEnabled: false)).EnsureSuccessStatusCode();
        var before = DateTimeOffset.UtcNow;

        (await AddValueAsync(slug, secret, "v4", graceDays: 7)).EnsureSuccessStatusCode();

        var versions = await VersionsAsync(slug, secret);
        foreach (var n in new[] { 1, 2 })
        {
            var expires = Version(versions, n)["expiresAt"]!.GetValue<DateTimeOffset>();
            Assert.InRange(
                expires,
                before.AddDays(7).AddSeconds(-5),
                DateTimeOffset.UtcNow.AddDays(7).AddSeconds(5)
            );
            Assert.Equal("valid", Version(versions, n)["status"]!.GetValue<string>());
        }
        Assert.Null(Version(versions, 3)["expiresAt"]); // disabled: untouched
        Assert.Null(Version(versions, 4)["expiresAt"]);
        Assert.Equal("v4", await ConsumerValueAsync(apiKey, secret));
    }

    [Fact]
    public async Task GracePeriod_OutOfRange_Returns400()
    {
        var (slug, _, secret) = await AppWithSecretAsync();
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await AddValueAsync(slug, secret, "v2", graceDays: -1)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await AddValueAsync(slug, secret, "v2", graceDays: 3651)).StatusCode
        );
    }

    [Fact]
    public async Task GlobalSecretVersions_HaveTheSameLifecycle()
    {
        var name = TestHelpers.UniqueName("G");
        (
            await Client.PostAsJsonAsync("/management/global/secrets", new { name })
        ).EnsureSuccessStatusCode();
        async Task SetAsync(string value, int? grace = null) =>
            (
                await Client.PostAsJsonAsync(
                    $"/management/global/secrets/{name}/values",
                    new[]
                    {
                        new
                        {
                            environmentSlug = "*",
                            value,
                            previousVersionGraceDays = grace,
                        },
                    }
                )
            ).EnsureSuccessStatusCode();
        await SetAsync("g1");
        await SetAsync("g2", grace: 1);

        var versions = JsonNode
            .Parse(await Client.GetStringAsync($"/management/global/secrets/{name}/versions/*"))!
            .AsArray()
            .Select(v => v!)
            .ToList();
        Assert.Equal("current", Version(versions, 2)["status"]!.GetValue<string>());
        Assert.NotNull(Version(versions, 1)["expiresAt"]);

        var url = $"/management/global/secrets/{name}/versions/*/2";
        Assert.Equal(
            HttpStatusCode.OK,
            (await Client.PatchAsJsonAsync(url, new { isEnabled = false })).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await Client.PatchAsJsonAsync(
                    $"/management/global/secrets/{name}/versions/*/1",
                    new { isEnabled = false }
                )
            ).StatusCode
        );
    }

    [Fact]
    public async Task VersionChanges_AreAudited_WithTheVersionNumber()
    {
        var (slug, _, secret) = await AppWithSecretAsync();
        (await AddValueAsync(slug, secret, "v2")).EnsureSuccessStatusCode();
        (await PatchAsync(slug, secret, 1, new { isEnabled = false })).EnsureSuccessStatusCode();

        List<JsonNode> items = [];
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            items = JsonNode.Parse(
                await Client.GetStringAsync($"/management/audit?application={slug}&target={secret}")
            )!["items"]!
                .AsArray()
                .Select(i => i!)
                .ToList();
            if (items.Any(i => i["action"]!.GetValue<string>() == AuditActions.SecretVersionUpdate))
                break;
            await Task.Delay(50);
        }

        var update = items.Single(i =>
            i["action"]!.GetValue<string>() == AuditActions.SecretVersionUpdate
        );
        Assert.Equal(1, update["version"]!.GetValue<int>());
        Assert.Equal("local", update["environment"]!.GetValue<string>());
        Assert.Equal("Updated secret version", update["description"]!.GetValue<string>());
        var writes = items
            .Where(i => i["action"]!.GetValue<string>() == AuditActions.SecretWrite)
            .Select(i => i["version"]!.GetValue<int>());
        Assert.Equal(new[] { 1, 2 }, writes.OrderBy(v => v));
    }
}
