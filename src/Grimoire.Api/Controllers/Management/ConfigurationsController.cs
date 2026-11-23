using Grimoire.Api.Audit;
using Grimoire.Api.Configuration;
using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Interfaces.Repositories;
using Grimoire.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Management;

[ApiController]
[Route("management/applications/{slug}/configurations")]
[Tags("Management - Configurations")]
public class ConfigurationsController(
    IApplicationRepository appRepo,
    IEnvironmentRepository envRepo,
    IConfigurationRepository configRepo,
    IEffectiveConfigurationService effective,
    IConfigurationHistoryRepository history,
    IConfigurationChangeContext changes
) : ControllerBase
{
    /// <summary>
    /// Compares the effective configuration (globals merged in) of two environments: keys only in
    /// <paramref name="to"/> are <c>added</c>, only in <paramref name="from"/> <c>removed</c>, and
    /// keys whose value or value type differ <c>changed</c>.
    /// </summary>
    /// <param name="slug">Application slug.</param>
    /// <param name="from">Environment to compare from, e.g. <c>staging</c>.</param>
    /// <param name="to">Environment to compare to, e.g. <c>production</c>.</param>
    /// <param name="includeUnchanged">Also list keys that are the same in both.</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpGet("diff")]
    [ProducesResponseType<ConfigurationDiffResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Diff(
        string slug,
        [FromQuery] string from,
        [FromQuery] string to,
        [FromQuery] bool includeUnchanged,
        CancellationToken ct
    )
    {
        var (app, fromEnv, missing) = await FindAsync(slug, from, ct);
        if (missing is not null)
            return missing;
        var toEnv = await envRepo.GetBySlugAsync(app.Id, to, ct);
        if (toEnv is null)
            return Problem(ProblemMessages.EnvironmentNotFound, statusCode: 404);

        var diff = ConfigurationDiff.Compare(
            await effective.GetConfigurationAsync(app, fromEnv, ct),
            await effective.GetConfigurationAsync(app, toEnv, ct),
            includeUnchanged
        );
        return Ok(
            new ConfigurationDiffResponse(
                from,
                to,
                diff.Select(ConfigurationHistoryMapper.ToResponse).ToList()
            )
        );
    }

    /// <summary>Every change to a key in an environment, newest first, including deletes.</summary>
    [HttpGet("{environmentSlug}/{key}/history")]
    [ProducesResponseType<List<ConfigurationRevisionResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> History(
        string slug,
        string environmentSlug,
        string key,
        CancellationToken ct
    )
    {
        var (_, env, missing) = await FindAsync(slug, environmentSlug, ct);
        if (missing is not null)
            return missing;

        var revisions = await history.GetHistoryAsync(
            ConfigurationScope.Application,
            ConfigurationRevision.EnvironmentKeyFor(env.Id),
            key,
            ct
        );
        return revisions.Count == 0
            ? Problem(ProblemMessages.ConfigurationNotFound, statusCode: 404)
            : Ok(revisions.Select(ConfigurationHistoryMapper.ToResponse));
    }

    /// <summary>
    /// Restores the value, value type and description a key had at <paramref name="revision"/>,
    /// as a new revision. Re-creates the key if it was deleted since.
    /// </summary>
    [Audited(AuditActions.ConfigurationRollback)]
    [HttpPost("{environmentSlug}/{key}/rollback/{revision:int}")]
    [ProducesResponseType<ConfigurationEntryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Rollback(
        string slug,
        string environmentSlug,
        string key,
        int revision,
        CancellationToken ct
    )
    {
        var (app, env, missing) = await FindAsync(slug, environmentSlug, ct);
        if (missing is not null)
            return missing;

        var target = await history.GetRevisionAsync(
            ConfigurationScope.Application,
            ConfigurationRevision.EnvironmentKeyFor(env.Id),
            key,
            revision,
            ct
        );
        if (target is null)
            return Problem(ProblemMessages.RevisionNotFound, statusCode: 404);
        if (target.Value is null)
            return Problem(ProblemMessages.RevisionIsDelete(revision), statusCode: 400);

        changes.RollbackOf = revision;
        var now = DateTimeOffset.UtcNow;
        var entry = await configRepo.GetByKeyAsync(app.Id, env.Id, key, ct);
        if (entry is null)
        {
            entry = new ConfigurationEntry
            {
                Id = Guid.NewGuid(),
                ApplicationId = app.Id,
                EnvironmentId = env.Id,
                Key = key,
                CreatedAt = now,
            };
            RestoreFrom(entry, target, now);
            await configRepo.AddAsync(entry, ct);
        }
        else
        {
            RestoreFrom(entry, target, now);
            await configRepo.UpdateAsync(entry, ct);
        }

        return Ok(ToResponse(entry, environmentSlug));
    }

    private static void RestoreFrom(
        ConfigurationEntry entry,
        ConfigurationRevision revision,
        DateTimeOffset now
    )
    {
        entry.Value = revision.Value!;
        entry.ValueType = revision.ValueType;
        entry.Description = revision.Description;
        entry.UpdatedAt = now;
    }

    /// <summary>
    /// The configuration the application receives for an environment, with globals merged in
    /// and each value's origin.
    /// </summary>
    [HttpGet("effective")]
    public async Task<IActionResult> Effective(
        string slug,
        [FromQuery] string environment,
        CancellationToken ct
    )
    {
        var (app, env, missing) = await FindAsync(slug, environment, ct);
        if (missing is not null)
            return missing;

        var entries = await effective.GetConfigurationAsync(app, env, ct);
        return Ok(
            entries.Select(e => new EffectiveConfigurationResponse(
                e.Key,
                e.Value,
                e.Description,
                ValueSourceNames.ToName(e.Source),
                e.GlobalEnvironmentSlug,
                e.OverridesGlobal,
                ConfigurationValueNormalizer.ToName(e.ValueType)
            ))
        );
    }

    [HttpGet]
    public async Task<IActionResult> List(string slug, CancellationToken ct)
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return Problem(ProblemMessages.ApplicationNotFound, statusCode: 404);

        var entries = await configRepo.GetByApplicationAsync(app.Id, ct);
        return Ok(entries.Select(e => ToResponse(e, e.Environment.Slug)));
    }

    [Audited(AuditActions.ConfigurationCreate)]
    [HttpPost]
    public async Task<IActionResult> Create(
        string slug,
        [FromBody] CreateConfigurationRequest req,
        CancellationToken ct
    )
    {
        var (app, env, missing) = await FindAsync(slug, req.EnvironmentSlug, ct);
        if (missing is not null)
            return missing;

        var existing = await configRepo.GetByKeyAsync(app.Id, env.Id, req.Key, ct);
        if (existing is not null)
            return Problem("Configuration key already exists.", statusCode: 409);

        if (
            !this.TryResolveConfigurationValue(
                req.ValueType,
                ConfigurationValueType.String,
                req.Value,
                out var valueType,
                out var value
            )
        )
            return ValidationProblem(ModelState);

        var entry = new ConfigurationEntry
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            EnvironmentId = env.Id,
            Key = req.Key,
            Value = value,
            ValueType = valueType,
            Description = req.Description,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await configRepo.AddAsync(entry, ct);

        return CreatedAtAction(nameof(List), new { slug }, ToResponse(entry, req.EnvironmentSlug));
    }

    [Audited(AuditActions.ConfigurationUpdate)]
    [HttpPut("{environmentSlug}/{key}")]
    public async Task<IActionResult> Update(
        string slug,
        string environmentSlug,
        string key,
        [FromBody] UpdateConfigurationRequest req,
        CancellationToken ct
    )
    {
        var (app, env, missing) = await FindAsync(slug, environmentSlug, ct);
        if (missing is not null)
            return missing;

        var entry = await configRepo.GetByKeyAsync(app.Id, env.Id, key, ct);
        if (entry is null)
            return Problem(ProblemMessages.ConfigurationNotFound, statusCode: 404);

        if (
            !this.TryResolveConfigurationValue(
                req.ValueType,
                entry.ValueType,
                req.Value,
                out var valueType,
                out var value
            )
        )
            return ValidationProblem(ModelState);

        entry.Value = value;
        entry.ValueType = valueType;
        entry.Description = req.Description ?? entry.Description;
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        await configRepo.UpdateAsync(entry, ct);

        return Ok(ToResponse(entry, environmentSlug));
    }

    [Audited(AuditActions.ConfigurationDelete)]
    [HttpDelete("{environmentSlug}/{key}")]
    public async Task<IActionResult> Delete(
        string slug,
        string environmentSlug,
        string key,
        CancellationToken ct
    )
    {
        var (app, env, missing) = await FindAsync(slug, environmentSlug, ct);
        if (missing is not null)
            return missing;

        var entry = await configRepo.GetByKeyAsync(app.Id, env.Id, key, ct);
        if (entry is null)
            return Problem(ProblemMessages.ConfigurationNotFound, statusCode: 404);

        await configRepo.DeleteAsync(entry, ct);
        return NoContent();
    }

    /// <summary>The application and environment, or the 404 to return.</summary>
    private async Task<(Application App, AppEnvironment Env, IActionResult? Missing)> FindAsync(
        string slug,
        string environmentSlug,
        CancellationToken ct
    )
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return (null!, null!, Problem(ProblemMessages.ApplicationNotFound, statusCode: 404));
        var env = await envRepo.GetBySlugAsync(app.Id, environmentSlug, ct);
        return env is null
            ? (app, null!, Problem(ProblemMessages.EnvironmentNotFound, statusCode: 404))
            : (app, env, null);
    }

    private static ConfigurationEntryResponse ToResponse(
        ConfigurationEntry e,
        string environmentSlug
    ) =>
        new(
            e.Id,
            environmentSlug,
            e.Key,
            e.Value,
            e.Description,
            e.CreatedAt,
            e.UpdatedAt,
            ConfigurationValueNormalizer.ToName(e.ValueType)
        );
}
