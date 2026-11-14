using System.Net;
using System.Text;
using Grimoire.Consumer;

namespace Grimoire.Tests;

public class ConsumerClientTests
{
    private sealed class StubHandler(HttpStatusCode status, string json) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            return Task.FromResult(
                new HttpResponseMessage(status)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                }
            );
        }
    }

    [Fact]
    public async Task SecretClient_GetSecretAsync_CallsConsumerRouteWithApiKey()
    {
        var handler = new StubHandler(
            HttpStatusCode.OK,
            """{"name":"db password","value":"s3cr3t","properties":{"enabled":true,"version":2}}"""
        );
        var client = new GrimoireSecretClient(
            "https://grimoire.test/",
            "grm_key",
            "prod env",
            new HttpClient(handler)
        );

        var secret = await client.GetSecretAsync("db password");

        Assert.Equal("s3cr3t", secret.Value);
        Assert.Equal(2, secret.Properties.Version);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://grimoire.test/consumer/secrets/db%20password?environment=prod%20env",
            request.RequestUri!.AbsoluteUri
        );
        Assert.Equal("grm_key", Assert.Single(request.Headers.GetValues("X-Api-Key")));
    }

    [Fact]
    public async Task ConfigurationClient_GetAsync_CallsConsumerRoute()
    {
        var handler = new StubHandler(
            HttpStatusCode.OK,
            """{"key":"Feature:Flag","value":"on","label":""}"""
        );
        var client = new GrimoireConfigurationClient(
            "https://grimoire.test",
            "grm_key",
            "local",
            handler
        );

        var value = await client.GetAsync("Feature:Flag");

        Assert.Equal("on", value);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://grimoire.test/consumer/configurations/Feature%3AFlag?environment=local",
            request.RequestUri!.AbsoluteUri
        );
        Assert.Equal("grm_key", Assert.Single(request.Headers.GetValues("X-Api-Key")));
    }

    [Fact]
    public async Task ConfigurationClient_GetAsync_ReturnsNullWhenNotFound()
    {
        var handler = new StubHandler(HttpStatusCode.NotFound, "{}");
        var client = new GrimoireConfigurationClient(
            "https://grimoire.test",
            "grm_key",
            "local",
            handler
        );

        Assert.Null(await client.GetAsync("missing"));
    }

    [Fact]
    public async Task ConfigurationClient_GetAllAsync_CallsConsumerRoute()
    {
        var handler = new StubHandler(
            HttpStatusCode.OK,
            """{"items":[{"key":"A","value":"1","label":""},{"key":"B","value":"2","label":""}]}"""
        );
        var client = new GrimoireConfigurationClient(
            "https://grimoire.test",
            "grm_key",
            "local",
            handler
        );

        var all = await client.GetAllAsync();

        Assert.Equal("1", all["A"]);
        Assert.Equal("2", all["B"]);
        Assert.Equal(
            "https://grimoire.test/consumer/configurations?environment=local",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public void ConfigurationClient_Load_PopulatesCaseInsensitiveData()
    {
        var handler = new StubHandler(
            HttpStatusCode.OK,
            """{"items":[{"key":"Feature:Flag","value":"on","label":""}]}"""
        );
        var client = new GrimoireConfigurationClient(
            "https://grimoire.test",
            "grm_key",
            "local",
            handler
        );

        client.Load();

        Assert.True(client.TryGet("feature:flag", out var value));
        Assert.Equal("on", value);
    }

    [Fact]
    public async Task SecretClient_PublicConstructor_UsesOwnHttpClient()
    {
        var client = new GrimoireSecretClient("http://127.0.0.1:1", "grm_key", "local");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetSecretAsync("anything"));
    }

    [Fact]
    public async Task ConfigurationClient_PublicConstructor_UsesOwnHttpClient()
    {
        var client = new GrimoireConfigurationClient("http://127.0.0.1:1", "grm_key", "local");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("anything"));
    }
}
