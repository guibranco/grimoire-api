using Grimoire.Api.Audit;
using Grimoire.Api.DTOs.Management;
using Grimoire.Api.Secrets;
using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Management;

[ApiController]
[Route("management/applications/{slug}/secrets")]
[Tags("Management - Secrets")]
public class SecretsController(
    IApplicationRepository appRepo,
    IEnvironmentRepository envRepo,
    ISecretRepository secretRepo,
    IEncryptionService encryption,
    IEffectiveConfigurationService effective
) : ControllerBase
{
    /// <summary>
    /// The secrets the application can read, with inherited globals and each one's origin.
    /// Metadata only, never values.
    /// </summary>
    [HttpGet("effective")]
    public async Task<IActionResult> Effective(string slug, CancellationToken ct)
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return NotFound(Problem("Application not found.", statusCode: 404));

        var secrets = await effective.GetSecretsAsync(app, ct);
        return Ok(
            secrets.Select(s => new EffectiveSecretResponse(
                s.Name,
                s.Description,
                ValueSourceNames.ToName(s.Source),
                s.OverridesGlobal
            ))
        );
    }

    [HttpGet]
    public async Task<IActionResult> List(string slug, CancellationToken ct)
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return NotFound(Problem("Application not found.", statusCode: 404));

        var secrets = await secretRepo.GetByApplicationAsync(app.Id, ct);
        return Ok(
            secrets.Select(s => new SecretResponse(
                s.Id,
                s.Name,
                s.Description,
                s.CreatedAt,
                s.UpdatedAt
            ))
        );
    }

    [Audited(AuditActions.SecretCreate)]
    [HttpPost]
    public async Task<IActionResult> Create(
        string slug,
        [FromBody] CreateSecretRequest req,
        CancellationToken ct
    )
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return NotFound(Problem("Application not found.", statusCode: 404));

        var existing = await secretRepo.GetByNameAsync(app.Id, req.Name, ct);
        if (existing is not null)
            return Conflict(Problem("Secret already exists.", statusCode: 409));

        var secret = new Secret
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            Name = req.Name,
            Description = req.Description,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await secretRepo.AddAsync(secret, ct);

        var envs = await envRepo.GetByApplicationAsync(app.Id, ct);
        var required = envs.Select(e => new RequiredEnvironment(e.Slug, e.Name, false)).ToList();

        return CreatedAtAction(
            nameof(GetByName),
            new { slug, name = secret.Name },
            new CreateSecretResponse(
                secret.Id,
                secret.Name,
                secret.Description,
                secret.CreatedAt,
                required
            )
        );
    }

    [HttpGet("{name}")]
    public async Task<IActionResult> GetByName(string slug, string name, CancellationToken ct)
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return NotFound(Problem("Application not found.", statusCode: 404));

        var secret = await secretRepo.GetByNameAsync(app.Id, name, ct);
        if (secret is null)
            return NotFound(Problem("Secret not found.", statusCode: 404));

        return Ok(
            new SecretResponse(
                secret.Id,
                secret.Name,
                secret.Description,
                secret.CreatedAt,
                secret.UpdatedAt
            )
        );
    }

    [Audited(AuditActions.SecretWrite)]
    [HttpPost("{name}/values")]
    public async Task<IActionResult> SetValues(
        string slug,
        string name,
        [FromBody] List<SetSecretValueRequest> req,
        CancellationToken ct
    )
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return NotFound(Problem("Application not found.", statusCode: 404));

        var secret = await secretRepo.GetByNameAsync(app.Id, name, ct);
        if (secret is null)
            return NotFound(Problem("Secret not found.", statusCode: 404));

        var now = DateTimeOffset.UtcNow;
        foreach (var item in req)
        {
            var env = await envRepo.GetBySlugAsync(app.Id, item.EnvironmentSlug, ct);
            if (env is null)
                return NotFound(
                    Problem($"Environment '{item.EnvironmentSlug}' not found.", statusCode: 404)
                );

            var nextVersion = await secretRepo.GetNextVersionNumberAsync(secret.Id, env.Id, ct);
            var sv = new SecretVersion
            {
                Id = Guid.NewGuid(),
                SecretId = secret.Id,
                EnvironmentId = env.Id,
                EncryptedValue = encryption.Encrypt(item.Value),
                IsEnabled = item.IsEnabled,
                ExpiresAt = item.ExpiresAt,
                NotBefore = item.NotBefore,
                Version = nextVersion,
                CreatedAt = now,
            };
            await secretRepo.AddVersionAsync(
                sv,
                GraceExpiry(item.PreviousVersionGraceDays, now),
                ct
            );
            if (req.Count == 1)
                AuditContext.SetVersion(HttpContext, nextVersion);
        }

        return Ok();
    }

    [HttpGet("{name}/versions/{environmentSlug}")]
    public async Task<IActionResult> GetVersions(
        string slug,
        string name,
        string environmentSlug,
        CancellationToken ct
    )
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return NotFound(Problem("Application not found.", statusCode: 404));

        var secret = await secretRepo.GetByNameAsync(app.Id, name, ct);
        if (secret is null)
            return NotFound(Problem("Secret not found.", statusCode: 404));

        var env = await envRepo.GetBySlugAsync(app.Id, environmentSlug, ct);
        if (env is null)
            return NotFound(Problem("Environment not found.", statusCode: 404));

        var versions = await secretRepo.GetVersionsAsync(secret.Id, env.Id, ct);
        return Ok(SecretVersionEditor.ToMetadata(versions, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Enables or disables a version, or changes its start/expiry. The value is immutable.
    /// Rejected with 409 when it would leave the secret with no valid version, unless
    /// <paramref name="force"/> is <c>true</c>.
    /// </summary>
    [Audited(AuditActions.SecretVersionUpdate)]
    [HttpPatch("{name}/versions/{environmentSlug}/{version:int}")]
    [ProducesResponseType<SecretVersionMetadata>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateVersion(
        string slug,
        string name,
        string environmentSlug,
        int version,
        [FromBody] UpdateSecretVersionRequest req,
        [FromQuery] bool force,
        CancellationToken ct
    )
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return Problem("Application not found.", statusCode: 404);

        var secret = await secretRepo.GetByNameAsync(app.Id, name, ct);
        if (secret is null)
            return Problem("Secret not found.", statusCode: 404);

        var env = await envRepo.GetBySlugAsync(app.Id, environmentSlug, ct);
        if (env is null)
            return Problem("Environment not found.", statusCode: 404);

        // Read, guard and save under one write lock so two concurrent edits can't each pass
        // the "no valid version" check against the same snapshot.
        return await secretRepo.InWriteTransactionAsync(
            async token =>
                await SecretVersionEditor.UpdateAsync(
                    this,
                    await secretRepo.GetVersionsAsync(secret.Id, env.Id, token),
                    version,
                    req,
                    force,
                    secretRepo.SaveChangesAsync,
                    token
                ),
            ct
        );
    }

    internal static DateTimeOffset? GraceExpiry(int? graceDays, DateTimeOffset now) =>
        graceDays is { } days ? now.AddDays(days) : null;

    [Audited(AuditActions.SecretDelete)]
    [HttpDelete("{name}")]
    public async Task<IActionResult> Delete(string slug, string name, CancellationToken ct)
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return NotFound(Problem("Application not found.", statusCode: 404));

        var secret = await secretRepo.GetByNameAsync(app.Id, name, ct);
        if (secret is null)
            return NotFound(Problem("Secret not found.", statusCode: 404));

        await secretRepo.DeleteAsync(secret, ct);
        return NoContent();
    }
}
