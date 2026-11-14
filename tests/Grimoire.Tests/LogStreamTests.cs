using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Grimoire.Api;
using Grimoire.Api.Logging;
using Serilog.Events;
using Serilog.Parsing;

namespace Grimoire.Tests;

public class LogStreamTests
{
    private static readonly MessageTemplateParser Parser = new();

    private static LogStreamOptions ValidOptions() =>
        new()
        {
            Server = "https://logs.example.test",
            ApiKey = "grimoire-api",
            ApiToken = "secret-token",
        };

    private static LogEvent Event(
        string template,
        LogEventLevel level = LogEventLevel.Information,
        Exception? exception = null,
        ActivityTraceId traceId = default,
        params (string Name, object? Value)[] properties
    ) =>
        new(
            new DateTimeOffset(2026, 11, 14, 19, 30, 0, 123, TimeSpan.Zero),
            level,
            exception,
            Parser.Parse(template),
            properties.Select(p => new LogEventProperty(
                p.Name,
                p.Value as LogEventPropertyValue ?? new ScalarValue(p.Value)
            )),
            traceId,
            default
        );

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
        var options = new LogStreamOptions { Server = server, ApiKey = key, ApiToken = token };
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
    [InlineData("https://logs.example.test", "https://logs.example.test/api/logs")]
    [InlineData("https://logs.example.test/", "https://logs.example.test/api/logs")]
    [InlineData("https://logs.example.test/api/logs", "https://logs.example.test/api/logs")]
    [InlineData("https://host/prefix", "https://host/prefix/api/logs")]
    public void Options_IngestUri_AppendsPathOnce(string server, string expected)
    {
        var options = new LogStreamOptions { Server = server };
        Assert.Equal(new Uri(expected), options.IngestUri);
    }

    // ── Payload ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(LogEventLevel.Verbose, "debug")]
    [InlineData(LogEventLevel.Debug, "debug")]
    [InlineData(LogEventLevel.Information, "info")]
    [InlineData(LogEventLevel.Warning, "warning")]
    [InlineData(LogEventLevel.Error, "error")]
    [InlineData(LogEventLevel.Fatal, "critical")]
    public void MapLevel_UsesLogStreamLevels(LogEventLevel level, string expected)
    {
        Assert.Equal(expected, LogStreamPayload.MapLevel(level));
    }

    [Fact]
    public void BuildEntry_RendersMessageAndKeepsTemplate()
    {
        var entry = LogStreamPayload.BuildEntry(
            Event(
                "Secret {Name} read by {App}",
                properties: [("Name", "db-password"), ("App", "billing"), ("SourceContext", "Grimoire.Api.Test")]
            )
        );

        Assert.Equal("info", entry["level"]!.GetValue<string>());
        Assert.Equal("Secret \"db-password\" read by \"billing\"", entry["message"]!.GetValue<string>());
        Assert.Equal("Secret {Name} read by {App}", entry["message_template"]!.GetValue<string>());
        Assert.Equal("Grimoire.Api.Test", entry["category"]!.GetValue<string>());
        Assert.Equal("2026-11-14T19:30:00.123Z", entry["timestamp"]!.GetValue<string>());
        Assert.Equal("db-password", entry["context"]!["Name"]!.GetValue<string>());
        Assert.Null(entry["context"]!["SourceContext"]);
    }

    [Fact]
    public void BuildEntry_WithoutSourceContext_UsesDefaultCategory()
    {
        var entry = LogStreamPayload.BuildEntry(Event("Hello"));
        Assert.Equal(LogStreamPayload.DefaultCategory, entry["category"]!.GetValue<string>());
        Assert.Null(entry["context"]);
    }

    [Fact]
    public void BuildEntry_LongSourceContext_IsTruncatedTo100Chars()
    {
        var source = new string('a', 50) + "." + new string('b', 80);
        var entry = LogStreamPayload.BuildEntry(Event("Hello", properties: [("SourceContext", source)]));

        var category = entry["category"]!.GetValue<string>();
        Assert.Equal(LogStreamPayload.MaxCategoryLength, category.Length);
        Assert.EndsWith(new string('b', 80), category);
    }

    [Fact]
    public void BuildEntry_TraceId_IsFormattedAsUuid()
    {
        var traceId = ActivityTraceId.CreateFromString("4bf92f3577b34da6a3ce929d0e0e4736");
        var entry = LogStreamPayload.BuildEntry(Event("Hello", traceId: traceId));

        Assert.Equal("4bf92f35-77b3-4da6-a3ce-929d0e0e4736", entry["trace_id"]!.GetValue<string>());
    }

    [Fact]
    public void BuildEntry_WithoutTraceId_OmitsIt()
    {
        var entry = LogStreamPayload.BuildEntry(Event("Hello"));
        Assert.False(entry.ContainsKey("trace_id"));
    }

    [Fact]
    public void BuildEntry_Exception_IsAddedToContext()
    {
        var exception = new InvalidOperationException("boom");
        var entry = LogStreamPayload.BuildEntry(Event("Failed", LogEventLevel.Error, exception));

        var ex = entry["context"]!["exception"]!;
        Assert.Equal("error", entry["level"]!.GetValue<string>());
        Assert.Equal(typeof(InvalidOperationException).FullName, ex["type"]!.GetValue<string>());
        Assert.Equal("boom", ex["message"]!.GetValue<string>());
        Assert.Contains("boom", ex["stack_trace"]!.GetValue<string>());
    }

    [Fact]
    public void ToJson_ConvertsStructuredValues()
    {
        var value = new StructureValue(
            [
                new LogEventProperty("Count", new ScalarValue(3)),
                new LogEventProperty("Ok", new ScalarValue(true)),
                new LogEventProperty("Missing", new ScalarValue(null)),
                new LogEventProperty(
                    "Tags",
                    new SequenceValue([new ScalarValue("a"), new ScalarValue("b")])
                ),
                new LogEventProperty(
                    "Map",
                    new DictionaryValue([
                        new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue(1), new ScalarValue("one")),
                    ])
                ),
            ]
        );

        var json = LogStreamPayload.ToJson(value)!.ToJsonString();
        Assert.Equal("{\"Count\":3,\"Ok\":true,\"Missing\":null,\"Tags\":[\"a\",\"b\"],\"Map\":{\"1\":\"one\"}}", json);
    }

    [Fact]
    public void Build_WrapsEntriesInBatch()
    {
        var batchId = Guid.NewGuid();
        var payload = LogStreamPayload.Build([Event("One"), Event("Two")], "grimoire-api", "1.2.3", batchId);

        Assert.Equal("grimoire-api", payload["app_key"]!.GetValue<string>());
        Assert.Equal("1.2.3", payload["app_id"]!.GetValue<string>());
        Assert.Equal(batchId.ToString("D"), payload["batch_id"]!.GetValue<string>());
        Assert.Equal(2, payload["logs"]!.AsArray().Count);
    }

    // ── Sink ──────────────────────────────────────────────────────────────────

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent("{\"error\":\"x\"}") };
        }
    }

    [Fact]
    public void Sink_IncompleteOptions_Throws()
    {
        Assert.Throws<ArgumentException>(() => new LogStreamSink(new LogStreamOptions()));
    }

    [Fact]
    public async Task Sink_PostsBatchWithAuthHeaders()
    {
        var handler = new RecordingHandler(HttpStatusCode.Accepted);
        using var sink = new LogStreamSink(ValidOptions(), handler);

        await sink.EmitBatchAsync([Event("Hello {Who}", properties: [("Who", "world")])]);

        var request = handler.Request!;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("https://logs.example.test/api/logs"), request.RequestUri);
        Assert.Equal("grimoire-api", request.Headers.GetValues("X-Api-Key").Single());
        Assert.Equal("secret-token", request.Headers.GetValues("X-Api-Token").Single());
        Assert.Contains("Grimoire.Api/", request.Headers.UserAgent.ToString());

        var body = JsonNode.Parse(handler.Body!)!;
        Assert.Equal("grimoire-api", body["app_key"]!.GetValue<string>());
        Assert.Equal(AppVersion.Current, body["app_id"]!.GetValue<string>());
        Assert.Equal("Hello \"world\"", body["logs"]![0]!["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Sink_UsesConfiguredAppId()
    {
        var handler = new RecordingHandler(HttpStatusCode.Accepted);
        var options = ValidOptions();
        options.AppId = "production";
        using var sink = new LogStreamSink(options, handler);

        await sink.EmitBatchAsync([Event("Hello")]);

        Assert.Equal("production", JsonNode.Parse(handler.Body!)!["app_id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Sink_EmptyBatch_SendsNothing()
    {
        var handler = new RecordingHandler(HttpStatusCode.Accepted);
        using var sink = new LogStreamSink(ValidOptions(), handler);

        await sink.EmitBatchAsync([]);

        Assert.Null(handler.Request);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Sink_TransientFailure_ThrowsSoSerilogRetries(HttpStatusCode status)
    {
        using var sink = new LogStreamSink(ValidOptions(), new RecordingHandler(status));
        await Assert.ThrowsAsync<HttpRequestException>(() => sink.EmitBatchAsync([Event("Hello")]));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Sink_PermanentFailure_DropsBatchWithoutThrowing(HttpStatusCode status)
    {
        using var sink = new LogStreamSink(ValidOptions(), new RecordingHandler(status));
        await sink.EmitBatchAsync([Event("Hello")]);
    }
}
