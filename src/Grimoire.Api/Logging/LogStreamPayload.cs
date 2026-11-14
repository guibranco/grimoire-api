using System.Globalization;
using System.Text.Json.Nodes;
using Serilog.Events;

namespace Grimoire.Api.Logging;

/// <summary>
/// Maps Serilog events to the logstream-server batch ingest payload
/// (<c>POST /api/logs</c>, see logstream-server README "Ingest logs").
/// </summary>
internal static class LogStreamPayload
{
    internal const int MaxCategoryLength = 100;
    internal const string DefaultCategory = "grimoire";
    private const string SourceContextProperty = "SourceContext";

    public static JsonObject Build(
        IEnumerable<LogEvent> events,
        string appKey,
        string appId,
        Guid batchId
    )
    {
        var logs = new JsonArray();
        foreach (var logEvent in events)
            logs.Add(BuildEntry(logEvent));

        return new JsonObject
        {
            ["app_key"] = appKey,
            ["app_id"] = appId,
            ["batch_id"] = batchId.ToString("D"),
            ["logs"] = logs,
        };
    }

    public static JsonObject BuildEntry(LogEvent logEvent)
    {
        var entry = new JsonObject
        {
            ["level"] = MapLevel(logEvent.Level),
            ["category"] = Category(logEvent),
            ["message"] = logEvent.RenderMessage(CultureInfo.InvariantCulture),
            ["message_template"] = logEvent.MessageTemplate.Text,
            ["timestamp"] = logEvent.Timestamp.UtcDateTime.ToString(
                "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                CultureInfo.InvariantCulture
            ),
        };

        var traceId = TraceId(logEvent);
        if (traceId is not null)
            entry["trace_id"] = traceId;

        var context = Context(logEvent);
        if (context.Count > 0)
            entry["context"] = context;

        return entry;
    }

    public static string MapLevel(LogEventLevel level) =>
        level switch
        {
            LogEventLevel.Verbose or LogEventLevel.Debug => "debug",
            LogEventLevel.Information => "info",
            LogEventLevel.Warning => "warning",
            LogEventLevel.Error => "error",
            LogEventLevel.Fatal => "critical",
            _ => "info",
        };

    private static string Category(LogEvent logEvent)
    {
        if (
            logEvent.Properties.TryGetValue(SourceContextProperty, out var value)
            && value is ScalarValue { Value: string source }
            && !string.IsNullOrWhiteSpace(source)
        )
        {
            return source.Length <= MaxCategoryLength ? source : source[^MaxCategoryLength..];
        }

        return DefaultCategory;
    }

    /// <summary>W3C trace id (32 hex chars) formatted as the UUID logstream expects, so entries of one request share it.</summary>
    private static string? TraceId(LogEvent logEvent)
    {
        if (logEvent.TraceId is not { } traceId || traceId == default)
            return null;

        return Guid.TryParseExact(traceId.ToHexString(), "N", out var guid)
            ? guid.ToString("D")
            : null;
    }

    private static JsonObject Context(LogEvent logEvent)
    {
        var context = new JsonObject();
        foreach (var (name, value) in logEvent.Properties)
        {
            if (name == SourceContextProperty)
                continue;
            context[name] = ToJson(value);
        }

        if (logEvent.Exception is { } exception)
        {
            context["exception"] = new JsonObject
            {
                ["type"] = exception.GetType().FullName,
                ["message"] = exception.Message,
                ["stack_trace"] = exception.ToString(),
            };
        }

        return context;
    }

    internal static JsonNode? ToJson(LogEventPropertyValue value) =>
        value switch
        {
            ScalarValue scalar => Scalar(scalar.Value),
            SequenceValue sequence => new JsonArray(sequence.Elements.Select(ToJson).ToArray()),
            StructureValue structure => new JsonObject(
                structure.Properties.Select(p => KeyValuePair.Create(p.Name, ToJson(p.Value)))
            ),
            DictionaryValue dictionary => new JsonObject(
                dictionary.Elements.Select(e =>
                    KeyValuePair.Create(
                        Convert.ToString(e.Key.Value, CultureInfo.InvariantCulture) ?? "null",
                        ToJson(e.Value)
                    )
                )
            ),
            _ => JsonValue.Create(value.ToString()),
        };

    private static JsonNode? Scalar(object? value) =>
        value switch
        {
            null => null,
            string s => JsonValue.Create(s),
            bool b => JsonValue.Create(b),
            int or long or short or byte or sbyte or uint or ushort or ulong => JsonValue.Create(
                Convert.ToDecimal(value, CultureInfo.InvariantCulture)
            ),
            double d when double.IsFinite(d) => JsonValue.Create(d),
            float f when float.IsFinite(f) => JsonValue.Create(f),
            decimal m => JsonValue.Create(m),
            DateTime dt => JsonValue.Create(dt.ToString("O", CultureInfo.InvariantCulture)),
            DateTimeOffset dto => JsonValue.Create(dto.ToString("O", CultureInfo.InvariantCulture)),
            TimeSpan ts => JsonValue.Create(ts.ToString("c", CultureInfo.InvariantCulture)),
            _ => JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture)),
        };
}
