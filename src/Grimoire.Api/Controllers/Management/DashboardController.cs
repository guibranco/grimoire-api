using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Audit;
using Grimoire.Core.Dashboard;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Management;

[ApiController]
[Route("management/dashboard")]
[Tags("Management - Dashboard")]
public class DashboardController(
    IDashboardService dashboard,
    IAuditQueryService audit,
    IFeatureFlagRepository flags,
    Grimoire.Infrastructure.Audit.AuditOptions auditOptions
) : ControllerBase
{
    private const int MaxDays = 3650;

    /// <summary>
    /// Aggregate, read-only summary for the UI home page: counts, last changed / last seen per
    /// application and environment, and items that need attention. Metadata only, never values.
    /// </summary>
    /// <param name="expiringWithinDays">Flag active secret versions expiring within this many days (default <c>Dashboard:ExpiringWithinDays</c>, 14).</param>
    /// <param name="staleAfterDays">Flag consumers not seen for more than this many days (default <c>Dashboard:StaleAfterDays</c>, 7).</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpGet]
    [ProducesResponseType<DashboardResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get(
        [FromQuery] int? expiringWithinDays,
        [FromQuery] int? staleAfterDays,
        CancellationToken ct
    )
    {
        if (expiringWithinDays is < 0 or > MaxDays)
            ModelState.AddModelError(
                nameof(expiringWithinDays),
                $"Must be between 0 and {MaxDays}."
            );
        if (staleAfterDays is < 1 or > MaxDays)
            ModelState.AddModelError(nameof(staleAfterDays), $"Must be between 1 and {MaxDays}.");
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var summary = await dashboard.GetSummaryAsync(expiringWithinDays, staleAfterDays, ct);

        // No values in here, but it still describes the estate: keep it out of shared caches.
        Response.Headers.CacheControl = "private, no-store";

        var recentActivity = auditOptions.Enabled ? await RecentActivityAsync(ct) : null;

        var (flagTotal, flagsEnabled) = await flags.CountAsync(ct);

        return Ok(
            ToResponse(
                summary,
                new DashboardFeatureFlagsResponse(flagTotal, flagsEnabled),
                recentActivity
            )
        );
    }

    private const int RecentActivityCount = 20;

    private async Task<IReadOnlyList<DashboardActivityResponse>> RecentActivityAsync(
        CancellationToken ct
    )
    {
        var page = await audit.QueryAsync(
            new AuditQuery(IncludeReads: false, PageSize: RecentActivityCount),
            ct
        );
        return page
            .Items.Where(e => e.Action != AuditActions.AuthFailed)
            .Select(e => new DashboardActivityResponse(
                e.Id,
                e.OccurredAt,
                e.Actor,
                AuditActions.Label(e.Action),
                e.Action,
                e.ApplicationSlug,
                e.Environment,
                e.Target,
                e.TargetType,
                e.Outcome.ToString().ToLowerInvariant()
            ))
            .ToList();
    }

    private static DashboardResponse ToResponse(
        DashboardSummary s,
        DashboardFeatureFlagsResponse featureFlags,
        IReadOnlyList<DashboardActivityResponse>? recentActivity
    ) =>
        new(
            s.GeneratedAt,
            new DashboardTotalsResponse(
                s.Totals.Applications,
                s.Totals.Environments,
                s.Totals.Secrets,
                s.Totals.Configurations
            ),
            s.Applications.Select(a => new DashboardApplicationResponse(
                    a.Slug,
                    a.Name,
                    a.InheritGlobals,
                    a.Environments.Select(e => new DashboardEnvironmentResponse(
                            e.Slug,
                            e.Name,
                            e.SecretCount,
                            e.ConfigurationCount,
                            e.LastChangedAt,
                            e.LastSeenAt
                        ))
                        .ToList(),
                    a.SecretCount,
                    a.ConfigurationCount,
                    a.LastChangedAt,
                    a.LastSeenAt
                ))
                .ToList(),
            new DashboardGlobalResponse(
                s.Global.SecretCount,
                s.Global.ConfigurationCount,
                s.Global.LastChangedAt
            ),
            featureFlags,
            RecentActivity: recentActivity,
            s.Attention.Select(i => new AttentionItemResponse(
                    i.Kind,
                    i.Severity.ToString().ToLowerInvariant(),
                    i.Application,
                    i.Environment,
                    i.Target,
                    i.DueAt,
                    i.LastSeenAt
                ))
                .ToList()
        );
}
