using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Management;

[ApiController]
[Route("management/reports")]
[Tags("Management - Reports")]
public class ReportsController(IUsageReportService reports, TimeProvider time) : ControllerBase
{
    public const int DefaultDays = 90;
    public const int MaxDays = 3650;

    /// <summary>
    /// Application and global secrets with no successful Consumer API read in the last
    /// <paramref name="days"/> days, including secrets never read. Never-read first, then the
    /// longest unread. Reads are tracked from the audit log's history onward. Metadata only.
    /// </summary>
    /// <param name="days">Window in days (1–3650, default 90).</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpGet("unused-secrets")]
    [ProducesResponseType<UnusedSecretsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UnusedSecrets([FromQuery] int? days, CancellationToken ct)
    {
        var window = days ?? DefaultDays;
        if (window is < 1 or > MaxDays)
        {
            ModelState.AddModelError(nameof(days), $"Must be between 1 and {MaxDays}.");
            return ValidationProblem(ModelState);
        }

        var now = time.GetUtcNow();
        var since = now.AddDays(-window);
        var items = await reports.ListUnusedSecretsAsync(since, ct);
        Response.Headers.CacheControl = "private, no-store";

        return Ok(
            new UnusedSecretsResponse(
                now,
                window,
                since,
                items
                    .Select(u => new UnusedSecretResponse(
                        u.Secret.ApplicationSlug,
                        u.Secret.ApplicationName,
                        u.Secret.Name,
                        u.Secret.IsGlobal,
                        u.Secret.CreatedAt,
                        u.LastReadAt,
                        u.ReadCount,
                        u.Reads.Select(r => new SecretReadResponse(
                                r.ApplicationSlug,
                                r.Environment,
                                r.LastReadAt,
                                r.ReadCount
                            ))
                            .ToList()
                    ))
                    .ToList()
            )
        );
    }

    /// <summary>
    /// Dependency graph of applications, secrets, global secrets and configuration keys. Edges:
    /// <c>owns</c> (application → its entries), <c>reads</c> (application → secret, from Consumer
    /// API reads), <c>overrides</c> (application entry → global of the same name) and
    /// <c>inherits</c> (application → global key it receives). Metadata only.
    /// </summary>
    /// <param name="ct">Request cancellation.</param>
    [HttpGet("dependencies")]
    [ProducesResponseType<DependencyGraphResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Dependencies(CancellationToken ct)
    {
        var graph = await reports.GetDependencyGraphAsync(ct);
        Response.Headers.CacheControl = "private, no-store";

        return Ok(
            new DependencyGraphResponse(
                time.GetUtcNow(),
                graph
                    .Nodes.Select(n => new DependencyNodeResponse(
                        n.Id,
                        n.Type,
                        n.Label,
                        n.Application,
                        n.Environments
                    ))
                    .ToList(),
                graph
                    .Edges.Select(e => new DependencyEdgeResponse(
                        e.Source,
                        e.Target,
                        e.Kind,
                        e.Environments,
                        e.LastReadAt,
                        e.ReadCount
                    ))
                    .ToList()
            )
        );
    }
}
