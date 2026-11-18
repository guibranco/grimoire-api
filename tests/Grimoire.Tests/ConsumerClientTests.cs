using System.Net;
using System.Text;
using Grimoire.Consumer;
using Microsoft.Extensions.Configuration;

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

    private const string StructuredItems = """
        {"items":[
          {"key":"AllowedIps","value":"[\"10.0.0.1\",\"10.0.0.2\"]","label":"local","valueType":"array"},
          {"key":"Smtp","value":"{\"host\":\"mail\",\"port\":25,\"tls\":true,\"tags\":[\"a\"]}","label":"local","valueType":"json"},
          {"key":"Banner","value":"line 1\nline 2","label":"local","valueType":"multiline"},
          {"key":"Empty","value":"[]","label":"local","valueType":"array"},
          {"key":"Legacy","value":"[\"not\",\"split\"]","label":"local"}
        ]}
        """;

    private sealed class SmtpOptions
    {
        public string Host { get; set; } = "";
        public int Port { get; set; }
        public bool Tls { get; set; }
        public string[] Tags { get; set; } = [];
    }

    [Fact]
    public void ConfigurationClient_Load_FlattensArrayAndJsonValues()
    {
        var client = new GrimoireConfigurationClient(
            "https://grimoire.test",
            "grm_key",
            "local",
            new StubHandler(HttpStatusCode.OK, StructuredItems)
        );

        var config = new ConfigurationBuilder().Add(client).Build();

        Assert.Equal(["10.0.0.1", "10.0.0.2"], config.GetSection("AllowedIps").Get<string[]>()!);
        Assert.Equal("10.0.0.2", config["allowedips:1"]);
        Assert.Null(config["AllowedIps"]);

        var smtp = config.GetSection("Smtp").Get<SmtpOptions>()!;
        Assert.Equal("mail", smtp.Host);
        Assert.Equal(25, smtp.Port);
        Assert.True(smtp.Tls);
        Assert.Equal(["a"], smtp.Tags);

        Assert.Equal("line 1\nline 2", config["Banner"]);
        Assert.Null(config["Empty"]);
        // Values without a value type (older servers, plain strings) are never parsed.
        Assert.Equal("[\"not\",\"split\"]", config["Legacy"]);
    }

    [Fact]
    public async Task ConfigurationClient_GetAllAsync_ReturnsRawJsonForStructuredValues()
    {
        var client = new GrimoireConfigurationClient(
            "https://grimoire.test",
            "grm_key",
            "local",
            new StubHandler(HttpStatusCode.OK, StructuredItems)
        );

        var all = await client.GetAllAsync();

        Assert.Equal("[\"10.0.0.1\",\"10.0.0.2\"]", all["AllowedIps"]);
    }

    [Fact]
    public async Task ConfigurationClient_GetAsyncOfT_DeserialisesArrayValue()
    {
        var client = new GrimoireConfigurationClient(
            "https://grimoire.test",
            "grm_key",
            "local",
            new StubHandler(
                HttpStatusCode.OK,
                """{"key":"AllowedIps","value":"[\"10.0.0.1\"]","label":"local","valueType":"array"}"""
            )
        );

        Assert.Equal(["10.0.0.1"], (await client.GetAsync<string[]>("AllowedIps"))!);
    }

    [Fact]
    public async Task ConfigurationClient_GetAsyncOfT_ReadsStringValueAsString()
    {
        var client = new GrimoireConfigurationClient(
            "https://grimoire.test",
            "grm_key",
            "local",
            new StubHandler(
                HttpStatusCode.OK,
                """{"key":"Name","value":"[not json]","label":"local","valueType":"string"}"""
            )
        );

        Assert.Equal("[not json]", await client.GetAsync<string>("Name"));
    }

    [Fact]
    public async Task ConfigurationClient_GetAsyncOfT_ReturnsDefaultWhenNotFound()
    {
        var client = new GrimoireConfigurationClient(
            "https://grimoire.test",
            "grm_key",
            "local",
            new StubHandler(HttpStatusCode.NotFound, "{}")
        );

        Assert.Null(await client.GetAsync<string[]>("missing"));
    }
}
