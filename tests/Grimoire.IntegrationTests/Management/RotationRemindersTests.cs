using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Grimoire.Api.Rotation;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Grimoire.IntegrationTests.Management;

public class RotationRemindersTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private const string WebhookUrl = "https://hooks.example.test/grimoire";
    private const string WebhookSecret = "test-webhook-secret";

    private HttpClient Client => factory.CreateManagementClient();

    private async Task<(string Slug, string Secret)> AppSecretAsync(
        int? rotationIntervalDays = null,
        DateTimeOffset? expiresAt = null
    )
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var secret = TestHelpers.UniqueName("S");
        var create = await Client.PostAsJsonAsync(
            $"/management/applications/{slug}/secrets",
            new { name = secret, rotationIntervalDays }
        );
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var value = await Client.PostAsJsonAsync(
            $"/management/applications/{slug}/secrets/{secret}/values",
            new[]
            {
                new
                {
                    environmentSlug = "local",
                    value = "v1",
                    expiresAt,
                },
            }
        );
        value.EnsureSuccessStatusCode();
        return (slug, secret);
    }

    private static async Task<List<JsonNode>> ExpiringAsync(
        HttpClient client,
        int? withinDays = null
    )
    {
        var json = JsonNode.Parse(
            await client.GetStringAsync(
                "/management/secrets/expiring"
                    + (withinDays is null ? "" : $"?withinDays={withinDays}")
            )
        )!;
        return json["items"]!.AsArray().Select(i => i!).ToList();
    }

    private static JsonNode? Find(List<JsonNode> items, string secret) =>
        items.SingleOrDefault(i => i["secret"]!.GetValue<string>() == secret);

    [Fact]
    public async Task RotationInterval_PutsTheSecretOnTheExpiringList()
    {
        var (slug, secret) = await AppSecretAsync(rotationIntervalDays: 10);

        var item = Find(await ExpiringAsync(Client), secret);
        Assert.NotNull(item);
        Assert.Equal(slug, item["application"]!.GetValue<string>());
        Assert.Equal("local", item["environment"]!.GetValue<string>());
        Assert.Equal("rotation", item["reason"]!.GetValue<string>());
        Assert.Equal(10, item["daysRemaining"]!.GetValue<int>());
        Assert.Equal(10, item["rotationIntervalDays"]!.GetValue<int>());
        Assert.Equal(1, item["version"]!.GetValue<int>());
        Assert.False(item["isGlobal"]!.GetValue<bool>());
        Assert.Null(item["value"]);

        Assert.Null(Find(await ExpiringAsync(Client, withinDays: 5), secret));
    }

    [Fact]
    public async Task ExpiryDate_PutsTheSecretOnTheExpiringList()
    {
        var (_, secret) = await AppSecretAsync(expiresAt: DateTimeOffset.UtcNow.AddDays(3));

        var item = Find(await ExpiringAsync(Client), secret);
        Assert.Equal("expiry", item!["reason"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3651)]
    public async Task WithinDaysOutOfRange_Returns400(int withinDays) =>
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await Client.GetAsync($"/management/secrets/expiring?withinDays={withinDays}")
            ).StatusCode
        );

    [Fact]
    public async Task PatchSecret_SetsAndClearsTheRotationInterval()
    {
        var (slug, secret) = await AppSecretAsync();
        var url = $"/management/applications/{slug}/secrets/{secret}";

        var set = await Client.PatchAsJsonAsync(url, new { rotationIntervalDays = 7 });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var body = await set.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(7, body!["rotationIntervalDays"]!.GetValue<int>());
        Assert.NotNull(Find(await ExpiringAsync(Client), secret));

        var description = await Client.PatchAsJsonAsync(url, new { description = "db" });
        var kept = await description.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(7, kept!["rotationIntervalDays"]!.GetValue<int>());
        Assert.Equal("db", kept["description"]!.GetValue<string>());

        var cleared = await Client.PatchAsJsonAsync(url, new { clearRotationInterval = true });
        var clearedBody = await cleared.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Null(clearedBody!["rotationIntervalDays"]);
        Assert.Null(Find(await ExpiringAsync(Client), secret));
    }

    [Theory]
    [InlineData("{\"rotationIntervalDays\":0}")]
    [InlineData("{\"rotationIntervalDays\":3651}")]
    [InlineData("{\"rotationIntervalDays\":5,\"clearRotationInterval\":true}")]
    [InlineData("{\"description\":\"x\",\"clearDescription\":true}")]
    public async Task PatchSecret_InvalidBody_Returns400(string json)
    {
        var (slug, secret) = await AppSecretAsync();
        var response = await Client.PatchAsync(
            $"/management/applications/{slug}/secrets/{secret}",
            new StringContent(json, Encoding.UTF8, "application/json")
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchUnknownSecret_Returns404WithProblemDetails()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        var response = await Client.PatchAsJsonAsync(
            $"/management/applications/{slug}/secrets/NOPE",
            new { rotationIntervalDays = 5 }
        );
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(404, problem!["status"]!.GetValue<int>());
    }

    [Fact]
    public async Task GlobalSecret_RotationIntervalOnCreateAndPatch()
    {
        var name = TestHelpers.UniqueName("G");
        var create = await Client.PostAsJsonAsync(
            "/management/global/secrets",
            new { name, rotationIntervalDays = 20 }
        );
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        (
            await Client.PostAsJsonAsync(
                $"/management/global/secrets/{name}/values",
                new[] { new { environmentSlug = "*", value = "g1" } }
            )
        ).EnsureSuccessStatusCode();

        var item = Find(await ExpiringAsync(Client), name);
        Assert.True(item!["isGlobal"]!.GetValue<bool>());
        Assert.Null(item["application"]);
        Assert.Equal("*", item["environment"]!.GetValue<string>());

        var patched = await Client.PatchAsJsonAsync(
            $"/management/global/secrets/{name}",
            new { rotationIntervalDays = 365 }
        );
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        Assert.Null(Find(await ExpiringAsync(Client), name));
    }

    // ---- Notifier --------------------------------------------------------------------------

    private sealed class FakeWebhook : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, byte[] Body)> Calls { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.NoContent;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            lock (Calls)
                Calls.Add((request, body));
            return new HttpResponseMessage(Status);
        }
    }

    private HttpClient NotifierClient(FakeWebhook webhook, string? webhookUrl = WebhookUrl)
    {
        var app = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RotationReminders:Enabled", "false");
            b.UseSetting("RotationReminders:WebhookUrl", webhookUrl ?? "");
            b.UseSetting("RotationReminders:WebhookSecret", WebhookSecret);
            b.ConfigureTestServices(services =>
                services
                    .AddHttpClient(RotationReminderNotifier.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => webhook)
            );
        });
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(
            "Authorization",
            $"Bearer {GrimoireWebApplicationFactory.AdminKey}"
        );
        return client;
    }

    private static JsonNode? ItemFor(byte[] body, string secret) =>
        JsonNode.Parse(body)!["items"]!
            .AsArray()
            .SingleOrDefault(i => i!["secret"]!.GetValue<string>() == secret);

    private static Task<HttpResponseMessage> NotifyAsync(HttpClient client) =>
        client.PostAsync("/management/secrets/expiring/notify", null);

    [Fact]
    public async Task Notify_PostsEachReminderOnce_Signed()
    {
        var webhook = new FakeWebhook();
        var client = NotifierClient(webhook);
        var (slug, secret) = await AppSecretAsync(rotationIntervalDays: 5);

        var first = await NotifyAsync(client);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var run = await first.Content.ReadFromJsonAsync<JsonObject>();
        Assert.True(run!["webhookConfigured"]!.GetValue<bool>());
        Assert.True(run["sent"]!.GetValue<int>() >= 1);

        var (request, body) = Assert.Single(webhook.Calls);
        Assert.Equal(WebhookUrl, request.RequestUri!.ToString());
        var expected =
            "sha256="
            + Convert.ToHexStringLower(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), body)
            );
        Assert.Equal(
            expected,
            request.Headers.GetValues(RotationReminderNotifier.SignatureHeader).Single()
        );

        var item = ItemFor(body, secret);
        Assert.NotNull(item);
        Assert.Equal(slug, item["application"]!.GetValue<string>());
        Assert.Equal(7, item["threshold"]!.GetValue<int>());
        Assert.Equal(5, item["daysRemaining"]!.GetValue<int>());
        var payload = JsonNode.Parse(body)!;
        Assert.Equal("rotation-reminder", payload["event"]!.GetValue<string>());
        Assert.Contains(secret, payload["text"]!.GetValue<string>());
        Assert.Equal(payload["text"]!.GetValue<string>(), payload["content"]!.GetValue<string>());
        Assert.Null(item["value"]);

        // Already delivered: a second run doesn't announce it again.
        var second = await NotifyAsync(client);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.All(webhook.Calls.Skip(1), call => Assert.Null(ItemFor(call.Body, secret)));
    }

    [Fact]
    public async Task Notify_WebhookFailure_Returns502AndRetriesNextRun()
    {
        var webhook = new FakeWebhook { Status = HttpStatusCode.InternalServerError };
        var client = NotifierClient(webhook);
        var (_, secret) = await AppSecretAsync(expiresAt: DateTimeOffset.UtcNow.AddDays(20));

        var failed = await NotifyAsync(client);
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.False(
            (await failed.Content.ReadFromJsonAsync<JsonObject>())!["delivered"]!.GetValue<bool>()
        );

        webhook.Status = HttpStatusCode.OK;
        Assert.Equal(HttpStatusCode.OK, (await NotifyAsync(client)).StatusCode);
        Assert.Equal(30, ItemFor(webhook.Calls[^1].Body, secret)!["threshold"]!.GetValue<int>());
    }

    [Fact]
    public async Task Notify_WithoutWebhook_ReportsDueButSendsNothing()
    {
        var webhook = new FakeWebhook();
        var client = NotifierClient(webhook, webhookUrl: null);
        await AppSecretAsync(rotationIntervalDays: 1);

        var response = await NotifyAsync(client);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.False(run!["webhookConfigured"]!.GetValue<bool>());
        Assert.True(run["due"]!.GetValue<int>() >= 1);
        Assert.Equal(0, run["sent"]!.GetValue<int>());
        Assert.Empty(webhook.Calls);
    }
}
