using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;

namespace Grimoire.Api.Logging;

/// <summary>
/// Batched Serilog sink that ships events to a logstream-server.
/// Runs in the background (Serilog batching), so a slow or unreachable server
/// never blocks request handling: transient failures (network, 5xx, 429) are
/// retried with backoff by Serilog and eventually dropped; permanent ones
/// (other 4xx) are dropped immediately. Failures are reported via SelfLog only,
/// never through the logging pipeline, to avoid feedback loops.
/// </summary>
public sealed class LogStreamSink : IBatchedLogEventSink, IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _ingestUri;
    private readonly string _appKey;
    private readonly string _appId;

    public LogStreamSink(LogStreamOptions options)
        : this(options, new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { }

    internal LogStreamSink(LogStreamOptions options, HttpMessageHandler handler)
    {
        if (!options.IsConfigured)
            throw new ArgumentException("LogStream options are incomplete.", nameof(options));

        _ingestUri = options.IngestUri;
        _appKey = options.ApiKey!.Trim();
        _appId = string.IsNullOrWhiteSpace(options.AppId) ? AppVersion.Current : options.AppId.Trim();

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.Add("X-Api-Key", _appKey);
        _http.DefaultRequestHeaders.Add("X-Api-Token", options.ApiToken!.Trim());
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Grimoire.Api", AppVersion.Current)
        );
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task EmitBatchAsync(IReadOnlyCollection<LogEvent> batch)
    {
        if (batch.Count == 0)
            return;

        var payload = LogStreamPayload.Build(batch, _appKey, _appId, Guid.NewGuid());
        using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(_ingestUri, content).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
            return;

        var status = (int)response.StatusCode;
        if (status >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            // Throwing makes Serilog retry the batch with backoff.
            throw new HttpRequestException($"LogStream ingest failed with HTTP {status}.", null, response.StatusCode);
        }

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        SelfLog.WriteLine(
            "LogStream rejected a batch of {0} event(s) with HTTP {1}: {2}",
            batch.Count,
            status,
            body.Length > 500 ? body[..500] : body
        );
    }

    public Task OnEmptyBatchAsync() => Task.CompletedTask;

    public void Dispose() => _http.Dispose();
}
