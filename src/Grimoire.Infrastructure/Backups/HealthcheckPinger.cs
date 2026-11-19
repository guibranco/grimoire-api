using Microsoft.Extensions.Logging;

namespace Grimoire.Infrastructure.Backups;

/// <summary>
/// Dead man's switch pings (healthchecks.io convention): <c>GET {url}</c> on success,
/// <c>GET {url}/fail</c> on failure. A failed ping is logged, never thrown.
/// </summary>
public sealed class HealthcheckPinger(HttpClient http, ILogger<HealthcheckPinger> logger)
{
    public Task SuccessAsync(string? url, CancellationToken ct) => PingAsync(url, null, ct);

    public Task FailureAsync(string? url, CancellationToken ct) => PingAsync(url, "fail", ct);

    private async Task PingAsync(string? url, string? suffix, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        var target = suffix is null ? url : $"{url.TrimEnd('/')}/{suffix}";
        try
        {
            using var response = await http.GetAsync(target, ct);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning(
                    "Healthcheck ping answered {StatusCode}",
                    (int)response.StatusCode
                );
        }
        catch (Exception ex)
            when (ex
                    is HttpRequestException
                        or TaskCanceledException
                        or InvalidOperationException // relative URL
                        or UriFormatException
            )
        {
            logger.LogWarning(ex, "Healthcheck ping failed");
        }
    }
}
