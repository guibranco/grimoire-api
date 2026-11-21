using Serilog.Events;
using Serilog.Sinks.LogStream;

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

    /// <summary>
    /// <c>Udp</c> (default): one HMAC-signed, fire-and-forget datagram per event, signed with
    /// <see cref="ApiToken"/>; events too large for one datagram go over HTTPS.
    /// <c>Http</c>: batched POSTs to <c>/api/logs</c> with retries.
    /// </summary>
    public LogStreamTransport Transport { get; set; } = LogStreamTransport.Udp;

    /// <summary>
    /// Host receiving the UDP datagrams. Defaults to the host of <see cref="Server"/>;
    /// set <c>127.0.0.1</c> when Grimoire runs on the same machine as the logstream-server.
    /// </summary>
    public string? UdpHost { get; set; }

    /// <summary>The logstream-server's <c>UDP_PORT</c>.</summary>
    public int UdpPort { get; set; } = 8082;

    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(Server)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ApiToken);

    /// <summary>
    /// The server's base address, accepting a <see cref="Server"/> with or without the
    /// <c>/api/logs</c> path (the sink appends it).
    /// </summary>
    public Uri ServerUri
    {
        get
        {
            var server = (Server ?? string.Empty).Trim().TrimEnd('/');
            if (server.EndsWith("/api/logs", StringComparison.OrdinalIgnoreCase))
                server = server[..^"/api/logs".Length];
            return new Uri(server + "/", UriKind.Absolute);
        }
    }
}
