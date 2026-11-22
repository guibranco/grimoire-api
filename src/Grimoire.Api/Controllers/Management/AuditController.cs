using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Management;

[ApiController]
[Route("management/audit")]
[Tags("Management - Audit")]
public class AuditController(IAuditQueryService audit) : ControllerBase
{
    public const int MaxPageSize = 200;

    /// <summary>
    /// Audit log, newest first: who read or changed what, where and how it ended. Never contains
    /// secret or configuration values.
    /// </summary>
    /// <param name="application">Application slug.</param>
    /// <param name="action">Action code, or a prefix ending in <c>*</c> (e.g. <c>secret.*</c>).</param>
    /// <param name="actor"><c>admin</c>, <c>app:&lt;slug&gt;</c> or <c>anonymous</c>.</param>
    /// <param name="environment">Environment slug.</param>
    /// <param name="target">Secret name or configuration key.</param>
    /// <param name="outcome"><c>success</c>, <c>notfound</c>, <c>forbidden</c>, <c>invalid</c>, <c>conflict</c> or <c>error</c>.</param>
    /// <param name="from">Inclusive lower bound (ISO 8601).</param>
    /// <param name="to">Inclusive upper bound (ISO 8601).</param>
    /// <param name="includeReads">Include consumer reads (<c>*.read</c>). Default <c>true</c>.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Items per page, 1–200 (default 50).</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpGet]
    [ProducesResponseType<AuditPageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] string? application,
        [FromQuery] string? action,
        [FromQuery] string? actor,
        [FromQuery] string? environment,
        [FromQuery] string? target,
        [FromQuery] string? outcome,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] bool includeReads = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default
    )
    {
        AuditOutcome? parsedOutcome = null;
        if (!string.IsNullOrWhiteSpace(outcome))
        {
            if (
                Enum.TryParse<AuditOutcome>(outcome, ignoreCase: true, out var o)
                && Enum.IsDefined(o)
            )
                parsedOutcome = o;
            else
                ModelState.AddModelError(nameof(outcome), "Unknown outcome.");
        }
        if (page < 1)
            ModelState.AddModelError(nameof(page), "Must be 1 or more.");
        if (pageSize is < 1 or > MaxPageSize)
            ModelState.AddModelError(nameof(pageSize), $"Must be between 1 and {MaxPageSize}.");
        if (from > to)
            ModelState.AddModelError(nameof(from), "Must not be after 'to'.");
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var result = await audit.QueryAsync(
            new AuditQuery(
                application,
                action,
                actor,
                environment,
                target,
                parsedOutcome,
                from,
                to,
                includeReads,
                page,
                pageSize
            ),
            ct
        );

        Response.Headers.CacheControl = "private, no-store";
        return Ok(
            new AuditPageResponse(
                result.Items.Select(ToResponse).ToList(),
                result.Page,
                result.PageSize,
                result.TotalCount
            )
        );
    }

    internal static AuditEventResponse ToResponse(AuditEvent e) =>
        new(
            e.Id,
            e.OccurredAt,
            e.Actor,
            e.Action,
            AuditActions.Label(e.Action),
            e.ApplicationSlug,
            e.Environment,
            e.TargetType,
            e.Target,
            e.Outcome.ToString().ToLowerInvariant(),
            e.StatusCode,
            e.IpAddress,
            e.UserAgent
        );
}
