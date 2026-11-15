using System.Net;
using System.Net.Http.Json;

namespace Grimoire.IntegrationTests;

public class AuthenticationTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    [Fact]
    public async Task ManagementApi_WithoutAuthHeader_Returns401()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/management/applications");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ManagementApi_WithWrongBearerToken_Returns401()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer wrong-key");
        var response = await client.GetAsync("/management/applications");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ManagementApi_WithCorrectBearerToken_Returns200()
    {
        var client = factory.CreateManagementClient();
        var response = await client.GetAsync("/management/applications");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public static TheoryData<string, string> GlobalManagementRoutes =>
        new()
        {
            { "GET", "/management/global/configurations" },
            { "POST", "/management/global/configurations" },
            { "PUT", "/management/global/configurations/*/Some:Key" },
            { "DELETE", "/management/global/configurations/*/Some:Key" },
            { "GET", "/management/global/secrets" },
            { "POST", "/management/global/secrets" },
            { "GET", "/management/global/secrets/any" },
            { "POST", "/management/global/secrets/any/values" },
            { "GET", "/management/global/secrets/any/versions/*" },
            { "DELETE", "/management/global/secrets/any" },
            { "GET", "/management/global/environments" },
            { "GET", "/management/applications/any/configurations/effective?environment=local" },
            { "GET", "/management/applications/any/secrets/effective" },
        };

    [Theory]
    [MemberData(nameof(GlobalManagementRoutes))]
    public async Task GlobalManagementRoutes_WithoutAdminKey_Return401(string method, string url)
    {
        var response = await factory
            .CreateClient()
            .SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(GlobalManagementRoutes))]
    public async Task GlobalManagementRoutes_WithConsumerApiKey_Return401(string method, string url)
    {
        var (_, apiKey) = await TestHelpers.CreateApplicationAsync(
            factory.CreateManagementClient()
        );
        var consumer = factory.CreateConsumerClient(apiKey);
        consumer.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        var response = await consumer.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), url)
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ConsumerApi_WithoutApiKeyHeader_Returns401()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/consumer/secrets/anything?environment=local");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ConsumerApi_WithWrongApiKey_Returns401()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "grm_wrongkey");
        var response = await client.GetAsync("/consumer/secrets/anything?environment=local");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_Returns200_WithoutAuth()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("version").GetString()));
        Assert.True(
            DateTimeOffset.TryParse(body.GetProperty("time").GetString(), out _),
            "time must be an RFC 3339 timestamp"
        );
        Assert.Equal("ok", body.GetProperty("database").GetProperty("status").GetString());
    }
}
