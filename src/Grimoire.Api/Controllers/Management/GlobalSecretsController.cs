using Grimoire.Api.Audit;
using Grimoire.Api.DTOs.Management;
using Grimoire.Api.Secrets;
using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Grimoire.Api.Controllers.Management;

[ApiController]
[Route("management/global/secrets")]
[Tags("Management - Global Secrets")]
public class GlobalSecretsController(
    IGlobalSecretRepository globalSecretRepo,
    IEncryptionService encryption
) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var secrets = await globalSecretRepo.GetAllAsync(ct);
        return Ok(secrets.Select(ToResponse));
    }

    [Audited(AuditActions.GlobalSecretCreate)]
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateGlobalSecretRequest req,
        CancellationToken ct
    )
    {
        var existing = await globalSecretRepo.GetByNameAsync(req.Name, ct);
        if (existing is not null)
            return Problem("Global secret already exists.", statusCode: 409);

        var secret = new GlobalSecret
        {
            Id = Guid.NewGuid(),
            Name = req.Name,
            Description = req.Description,
            RotationIntervalDays = req.RotationIntervalDays,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await globalSecretRepo.AddAsync(secret, ct);

        return CreatedAtAction(nameof(GetByName), new { name = secret.Name }, ToResponse(secret));
    }

    [HttpGet("{name}")]
    public async Task<IActionResult> GetByName(string name, CancellationToken ct)
    {
        var secret = await globalSecretRepo.GetByNameAsync(name, ct);
        if (secret is null)
            return Problem(ProblemMessages.GlobalSecretNotFound, statusCode: 404);

        return Ok(ToResponse(secret));
    }

    /// <summary>Changes the description and rotation schedule. Values are changed through <c>/values</c>.</summary>
    [Audited(AuditActions.GlobalSecretUpdate)]
    [HttpPatch("{name}")]
    [ProducesResponseType<GlobalSecretResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        string name,
        [FromBody] UpdateSecretRequest req,
        CancellationToken ct
    )
    {
        var secret = await globalSecretRepo.GetByNameAsync(name, ct);
        if (secret is null)
            return Problem(ProblemMessages.GlobalSecretNotFound, statusCode: 404);

        secret.Description = req.ClearDescription ? null : req.Description ?? secret.Description;
        secret.RotationIntervalDays = req.ClearRotationInterval
            ? null
            : req.RotationIntervalDays ?? secret.RotationIntervalDays;
        secret.UpdatedAt = DateTimeOffset.UtcNow;
        await globalSecretRepo.SaveChangesAsync(ct);
        return Ok(ToResponse(secret));
    }

    [Audited(AuditActions.GlobalSecretWrite)]
    [HttpPost("{name}/values")]
    public async Task<IActionResult> SetValues(
        string name,
        [FromBody] List<SetGlobalSecretValueRequest> req,
        CancellationToken ct
    )
    {
        var secret = await globalSecretRepo.GetByNameAsync(name, ct);
        if (secret is null)
            return Problem(ProblemMessages.GlobalSecretNotFound, statusCode: 404);

        var now = DateTimeOffset.UtcNow;
        foreach (var item in req)
        {
            var nextVersion = await globalSecretRepo.GetNextVersionNumberAsync(
                secret.Id,
                item.EnvironmentSlug,
                ct
            );
            var version = new GlobalSecretVersion
            {
                Id = Guid.NewGuid(),
                GlobalSecretId = secret.Id,
                EnvironmentSlug = item.EnvironmentSlug,
                EncryptedValue = encryption.Encrypt(item.Value),
                IsEnabled = item.IsEnabled,
                ExpiresAt = item.ExpiresAt,
                NotBefore = item.NotBefore,
                Version = nextVersion,
                CreatedAt = now,
            };
            await globalSecretRepo.AddVersionAsync(
                version,
                SecretsController.GraceExpiry(item.PreviousVersionGraceDays, now),
                ct
            );
            if (req.Count == 1)
                AuditContext.SetVersion(HttpContext, nextVersion);
        }

        return Ok();
    }

    [HttpGet("{name}/versions/{environmentSlug}")]
    public async Task<IActionResult> GetVersions(
        string name,
        string environmentSlug,
        CancellationToken ct
    )
    {
        var secret = await globalSecretRepo.GetByNameAsync(name, ct);
        if (secret is null)
            return Problem(ProblemMessages.GlobalSecretNotFound, statusCode: 404);

        var versions = await globalSecretRepo.GetVersionsAsync(secret.Id, environmentSlug, ct);
        return Ok(SecretVersionEditor.ToMetadata(versions, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Enables or disables a Global secret version, or changes its start/expiry. Same rules as
    /// application secret versions; the guardrail considers this environment slug's versions.
    /// </summary>
    [Audited(AuditActions.GlobalSecretVersionUpdate)]
    [HttpPatch("{name}/versions/{environmentSlug}/{version:int}")]
    [ProducesResponseType<SecretVersionMetadata>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateVersion(
        string name,
        string environmentSlug,
        int version,
        [FromBody] UpdateSecretVersionRequest req,
        [FromQuery] bool force,
        CancellationToken ct
    )
    {
        var secret = await globalSecretRepo.GetByNameAsync(name, ct);
        if (secret is null)
            return Problem(ProblemMessages.GlobalSecretNotFound, statusCode: 404);

        // See SecretsController.UpdateVersion: the guard and the save share one write lock.
        return await globalSecretRepo.InWriteTransactionAsync(
            async token =>
                await SecretVersionEditor.UpdateAsync(
                    this,
                    await globalSecretRepo.GetVersionsAsync(secret.Id, environmentSlug, token),
                    version,
                    req,
                    force,
                    globalSecretRepo.SaveChangesAsync,
                    token
                ),
            ct
        );
    }

    /// <summary>Same as the application secret rollback, for one environment slug of a Global secret.</summary>
    [Audited(AuditActions.GlobalSecretRollback)]
    [HttpPost("{name}/versions/{environmentSlug}/{version:int}/rollback")]
    [ProducesResponseType<SecretVersionMetadata>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Rollback(
        string name,
        string environmentSlug,
        int version,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RollbackSecretRequest? req,
        CancellationToken ct
    )
    {
        var secret = await globalSecretRepo.GetByNameAsync(name, ct);
        if (secret is null)
            return Problem(ProblemMessages.GlobalSecretNotFound, statusCode: 404);

        var source = await globalSecretRepo.GetVersionAsync(
            secret.Id,
            environmentSlug,
            version,
            ct
        );
        if (source is null)
            return Problem(ProblemMessages.SecretVersionNotFound, statusCode: 404);

        var now = DateTimeOffset.UtcNow;
        var next = await globalSecretRepo.GetNextVersionNumberAsync(secret.Id, environmentSlug, ct);
        secret.UpdatedAt = now;
        await globalSecretRepo.AddVersionAsync(
            new GlobalSecretVersion
            {
                Id = Guid.NewGuid(),
                GlobalSecretId = secret.Id,
                EnvironmentSlug = environmentSlug,
                EncryptedValue = source.EncryptedValue,
                IsEnabled = true,
                Version = next,
                CreatedAt = now,
            },
            SecretsController.GraceExpiry(req?.PreviousVersionGraceDays, now),
            ct
        );
        AuditContext.SetVersion(HttpContext, next);

        var versions = await globalSecretRepo.GetVersionsAsync(secret.Id, environmentSlug, ct);
        return Ok(SecretVersionEditor.ToMetadata(versions, now).Single(v => v.Version == next));
    }

    [Audited(AuditActions.GlobalSecretDelete)]
    [HttpDelete("{name}")]
    public async Task<IActionResult> Delete(string name, CancellationToken ct)
    {
        var secret = await globalSecretRepo.GetByNameAsync(name, ct);
        if (secret is null)
            return Problem(ProblemMessages.GlobalSecretNotFound, statusCode: 404);

        await globalSecretRepo.DeleteAsync(secret, ct);
        return NoContent();
    }

    private static GlobalSecretResponse ToResponse(GlobalSecret s) =>
        new(s.Id, s.Name, s.Description, s.CreatedAt, s.UpdatedAt, s.RotationIntervalDays);
}
