using Serilog.Events;

namespace Grimoire.Api.Logging;

/// <summary>
/// Settings for shipping logs to a logstream-server (section <c>LogStream</c>).
/// Uses the server's per-client auth: <c>X-Api-Key</c> (the registered app_key)
/// and <c>X-Api-Token</c>.
/// </summary>
public sealed class LogStreamOptions
{
    public const string SectionName = "LogStream";

    /// <summary>Kill switch: set to <c>false</c> to stop all LogStream traffic, e.g. while the server is down.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Base URL of the logstream-server, e.g. <c>https://logs.example.com</c>.</summary>
    public string? Server { get; set; }

    /// <summary>The client's app_key, sent as <c>X-Api-Key</c>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The client's API token, sent as <c>X-Api-Token</c>.</summary>
    public string? ApiToken { get; set; }

    /// <summary>Instance identifier stored as <c>app_id</c>. Defaults to the running version.</summary>
    public string? AppId { get; set; }

    /// <summary>Lowest level shipped to LogStream (console/file logging is unaffected).</summary>
    public LogEventLevel MinimumLevel { get; set; } = LogEventLevel.Information;

    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(Server)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ApiToken);

    /// <summary>The ingest endpoint, accepting a <see cref="Server"/> with or without the <c>/api/logs</c> path.</summary>
    public Uri IngestUri
    {
        get
        {
            var server = (Server ?? string.Empty).Trim().TrimEnd('/');
            if (!server.EndsWith("/api/logs", StringComparison.OrdinalIgnoreCase))
                server += "/api/logs";
            return new Uri(server, UriKind.Absolute);
        }
    }
}
