using System.Net;

namespace Grimoire.IntegrationTests;

public class AuthenticationTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    [Fact]
    public async Task ManagementApi_WithoutAuthHeader_Returns401()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/management/applications");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ManagementApi_WithWrongBearerToken_Returns401()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer wrong-key");
        var response = await client.GetAsync("/api/management/applications");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ManagementApi_WithCorrectBearerToken_Returns200()
    {
        var client = factory.CreateManagementClient();
        var response = await client.GetAsync("/api/management/applications");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ConsumerApi_WithoutApiKeyHeader_Returns401()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/consumer/secrets/anything?environment=local");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ConsumerApi_WithWrongApiKey_Returns401()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "grm_wrongkey");
        var response = await client.GetAsync("/api/consumer/secrets/anything?environment=local");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_Returns200_WithoutAuth()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
