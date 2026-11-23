using Grimoire.Api.Audit;
using Grimoire.Api.DTOs.Management;
using Grimoire.Api.Rotation;
using Grimoire.Core.Audit;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Secrets;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Management;

[ApiController]
[Route("management/secrets/expiring")]
[Tags("Management - Rotation")]
public class ExpiringSecretsController(IExpiringSecretsService expiring, TimeProvider time)
    : ControllerBase
{
    public const int DefaultWithinDays = 30;
    public const int MaxWithinDays = 3650;

    /// <summary>
    /// Secrets due for rotation across all applications and globals, soonest first: the current
    /// version's expiry or rotation date (whichever comes first) within <paramref name="withinDays"/>,
    /// plus anything overdue or expired. Metadata only, never values.
    /// </summary>
    /// <param name="withinDays">Look-ahead window in days (0–3650, default 30).</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpGet]
    [ProducesResponseType<ExpiringSecretsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] int? withinDays, CancellationToken ct)
    {
        var days = withinDays ?? DefaultWithinDays;
        if (days is < 0 or > MaxWithinDays)
        {
            ModelState.AddModelError(nameof(withinDays), $"Must be between 0 and {MaxWithinDays}.");
            return ValidationProblem(ModelState);
        }

        var now = time.GetUtcNow();
        var items = await expiring.ListAsync(days, ct);
        Response.Headers.CacheControl = "private, no-store";

        return Ok(
            new ExpiringSecretsResponse(
                now,
                days,
                items
                    .Select(i => new ExpiringSecretResponse(
                        i.Application,
                        i.ApplicationName,
                        i.Secret,
                        i.Environment,
                        i.IsGlobal,
                        i.Version,
                        i.Reason,
                        i.DueAt,
                        SecretDueRules.DaysRemaining(i.DueAt, now),
                        i.RotationIntervalDays
                    ))
                    .ToList()
            )
        );
    }

    /// <summary>
    /// Runs the rotation reminder job now (it also runs on its own schedule). Delivers each new
    /// reminder once to <c>RotationReminders:WebhookUrl</c>; already-delivered ones are skipped.
    /// </summary>
    [Audited(AuditActions.RotationRemindersRun)]
    [HttpPost("notify")]
    [ProducesResponseType<RotationReminderRunResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<RotationReminderRunResponse>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Notify(
        [FromServices] RotationReminderNotifier notifier,
        CancellationToken ct
    )
    {
        var run = await notifier.RunAsync(ct);
        var response = new RotationReminderRunResponse(
            run.Due,
            run.Sent,
            run.AlreadySent,
            run.WebhookConfigured,
            run.Delivered
        );
        return run.Delivered ? Ok(response) : StatusCode(StatusCodes.Status502BadGateway, response);
    }
}
