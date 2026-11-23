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
    [HttpGet]
    [ProducesResponseType<AuditPageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] AuditListRequest query, CancellationToken ct)
    {
        var (application, action, actor, environment, target, outcome, from, to) = (
            query.Application,
            query.Action,
            query.Actor,
            query.Environment,
            query.Target,
            query.Outcome,
            query.From,
            query.To
        );
        var (includeReads, page, pageSize) = (query.IncludeReads, query.Page, query.PageSize);

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
            e.Version,
            e.Outcome.ToString().ToLowerInvariant(),
            e.StatusCode,
            e.IpAddress,
            e.UserAgent
        );
}
