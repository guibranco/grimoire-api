using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Grimoire.Api;
using Grimoire.Api.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.LogStream;

namespace Grimoire.Tests;

public class LogStreamTests
{
    private static LogStreamOptions ValidOptions() =>
        new()
        {
            Server = "https://logs.example.test",
            ApiKey = "grimoire-api",
            ApiToken = "secret-token",
            Transport = LogStreamTransport.Http,
        };

    // ── Options ───────────────────────────────────────────────────────────────

    [Fact]
    public void Options_Complete_IsConfigured()
    {
        Assert.True(ValidOptions().IsConfigured);
    }

    [Theory]
    [InlineData(null, "key", "token")]
    [InlineData("https://logs", "", "token")]
    [InlineData("https://logs", "key", "  ")]
    public void Options_MissingValue_IsNotConfigured(string? server, string? key, string? token)
    {
        var options = new LogStreamOptions
        {
            Server = server,
            ApiKey = key,
            ApiToken = token,
        };
        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void Options_Disabled_IsNotConfigured()
    {
        var options = ValidOptions();
        options.Enabled = false;
        Assert.False(options.IsConfigured);
    }

    [Theory]
    [InlineData("https://logs.example.test", "https://logs.example.test/")]
    [InlineData("https://logs.example.test/", "https://logs.example.test/")]
    [InlineData("https://logs.example.test/api/logs", "https://logs.example.test/")]
    [InlineData("https://host/prefix", "https://host/prefix/")]
    public void Options_ServerUri_DropsTheIngestPath(string server, string expected)
    {
        var options = new LogStreamOptions { Server = server };
        Assert.Equal(new Uri(expected), options.ServerUri);
    }

    [Fact]
    public void Options_DefaultToUdpOnPort8082()
    {
        var options = new LogStreamOptions();
        Assert.Equal(LogStreamTransport.Udp, options.Transport);
        Assert.Equal(8082, options.UdpPort);
        Assert.Null(options.UdpHost);
    }

    // ── Sink ──────────────────────────────────────────────────────────────────

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(status)
            {
                Content = new StringContent("{\"saved\":1,\"entries\":[],\"errors\":null}"),
            };
        }
    }

    /// <summary>Logs through a logger writing to LogStream, then disposes it so the batch is sent.</summary>
    private static RecordingHandler LogAndFlush(LogStreamOptions options, Action<ILogger> log)
    {
        var handler = new RecordingHandler(HttpStatusCode.Accepted);
        using (
            var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteToLogStream(options, handler)
                .CreateLogger()
        )
        {
            log(logger);
        }

        return handler;
    }

    [Fact]
    public void Sink_IncompleteOptions_LeavesConfigurationUnchanged()
    {
        var handler = LogAndFlush(new LogStreamOptions(), logger => logger.Information("Hello"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Sink_PostsBatchWithAuthHeaders()
    {
        var handler = LogAndFlush(ValidOptions(), logger =>
            logger
                .ForContext("SourceContext", "Grimoire.Api.Test")
                .Information("Secret {Name} read by {App}", "db-password", "billing")
        );

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("https://logs.example.test/api/logs"), request.RequestUri);
        Assert.Equal("grimoire-api", request.Headers.GetValues("X-Api-Key").Single());
        Assert.Equal("secret-token", request.Headers.GetValues("X-Api-Token").Single());
        Assert.Equal(
            $"Grimoire.Api/{AppVersion.Current}",
            string.Join(" ", request.Headers.GetValues("User-Agent"))
        );

        var body = JsonNode.Parse(Assert.Single(handler.Bodies))!;
        Assert.Equal(AppVersion.Current, body["app_id"]!.GetValue<string>());
        var entry = body["logs"]![0]!;
        Assert.Equal("info", entry["level"]!.GetValue<string>());
        Assert.Equal("Grimoire.Api.Test", entry["category"]!.GetValue<string>());
        Assert.Equal(
            "Secret \"db-password\" read by \"billing\"",
            entry["message"]!.GetValue<string>()
        );
        Assert.Equal("Secret {Name} read by {App}", entry["message_template"]!.GetValue<string>());
        Assert.Equal("db-password", entry["context"]!["Name"]!.GetValue<string>());
    }

    [Fact]
    public void Sink_UsesConfiguredAppId()
    {
        var options = ValidOptions();
        options.AppId = "production";

        var handler = LogAndFlush(options, logger => logger.Information("Hello"));

        Assert.Equal(
            "production",
            JsonNode.Parse(Assert.Single(handler.Bodies))!["app_id"]!.GetValue<string>()
        );
    }

    [Fact]
    public void Sink_ShipsOnlyFromTheMinimumLevel()
    {
        var options = ValidOptions();
        options.MinimumLevel = LogEventLevel.Warning;

        var handler = LogAndFlush(options, logger =>
        {
            logger.Information("dropped");
            logger.Warning("kept");
        });

        var logs = JsonNode.Parse(Assert.Single(handler.Bodies))!["logs"]!.AsArray();
        Assert.Equal("kept", Assert.Single(logs)!["message"]!.GetValue<string>());
    }

    [Fact]
    public void Sink_ShipsExceptionAndTraceId()
    {
        using var activity = new Activity("test").SetIdFormat(ActivityIdFormat.W3C).Start();

        var handler = LogAndFlush(ValidOptions(), logger =>
            logger.Error(new InvalidOperationException("boom"), "Failed")
        );

        var entry = JsonNode.Parse(Assert.Single(handler.Bodies))!["logs"]![0]!;
        Assert.Equal("error", entry["level"]!.GetValue<string>());
        Assert.Equal(activity.TraceId.ToHexString(), entry["trace_id"]!.GetValue<string>());
        var exception = entry["context"]!["exception"]!;
        Assert.Equal(
            typeof(InvalidOperationException).FullName,
            exception["type"]!.GetValue<string>()
        );
        Assert.Equal("boom", exception["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Sink_Udp_SendsSignedDatagramToUdpHost()
    {
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var options = ValidOptions();
        options.Transport = LogStreamTransport.Udp;
        options.UdpHost = "127.0.0.1";
        options.UdpPort = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;

        var handler = LogAndFlush(options, logger => logger.Information("Hello {Who}", "udp"));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var datagram = await receiver.ReceiveAsync(timeout.Token);
        Assert.Empty(handler.Requests);

        using var packet = JsonDocument.Parse(datagram.Buffer);
        var root = packet.RootElement;
        Assert.Equal("grimoire-api", root.GetProperty("key").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("sig").GetString()));
        Assert.Equal("Hello \"udp\"", root.GetProperty("log").GetProperty("message").GetString());
    }
}
