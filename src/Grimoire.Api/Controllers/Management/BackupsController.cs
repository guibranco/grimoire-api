using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Backups;
using Grimoire.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Management;

[ApiController]
[Route("management/backups")]
[Tags("Management - Backups")]
public class BackupsController(IBackupService backups, ILogger<BackupsController> logger)
    : ControllerBase
{
    /// <summary>Lists encrypted backups across all enabled targets, newest first.</summary>
    [HttpGet]
    [ProducesResponseType<IEnumerable<BackupResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!backups.IsConfigured)
            return NotConfigured();

        var list = await backups.ListBackupsAsync(ct);
        return Ok(list.Select(ToResponse));
    }

    /// <summary>Takes a backup now and writes it to every enabled target.</summary>
    [HttpPost]
    [ProducesResponseType<BackupResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        if (!backups.IsConfigured)
            return NotConfigured();

        try
        {
            var backup = await backups.CreateBackupAsync(ct);
            return Created($"/management/backups/{backup.Name}", ToResponse(backup));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Details (paths, endpoints) stay in the server log.
            logger.LogError(ex, "On-demand backup failed");
            return Problem(
                title: "Backup failed",
                detail: "The backup did not complete on every target. Check the server logs.",
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }

    private ObjectResult NotConfigured() =>
        Conflict(
            new ProblemDetails
            {
                Title = "Backups are not configured",
                Detail = "Set 'Backup:Enabled', 'Backup:Key' and at least one backup target.",
                Status = StatusCodes.Status409Conflict,
            }
        );

    private static BackupResponse ToResponse(BackupInfo b) =>
        new(b.Name, b.SizeBytes, b.CreatedAt, b.Targets);
}
