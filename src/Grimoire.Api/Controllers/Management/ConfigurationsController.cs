using Grimoire.Api.DTOs.Management;
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
    IEffectiveConfigurationService effective
) : ControllerBase
{
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
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return NotFound(Problem("Application not found.", statusCode: 404));

        var env = await envRepo.GetBySlugAsync(app.Id, environment, ct);
        if (env is null)
            return NotFound(Problem("Environment not found.", statusCode: 404));

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
            return NotFound(Problem("Application not found.", statusCode: 404));

        var entries = await configRepo.GetByApplicationAsync(app.Id, ct);
        return Ok(entries.Select(e => ToResponse(e, e.Environment.Slug)));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        string slug,
        [FromBody] CreateConfigurationRequest req,
        CancellationToken ct
    )
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return NotFound(Problem("Application not found.", statusCode: 404));

        var env = await envRepo.GetBySlugAsync(app.Id, req.EnvironmentSlug, ct);
        if (env is null)
            return NotFound(Problem("Environment not found.", statusCode: 404));

        var existing = await configRepo.GetByKeyAsync(app.Id, env.Id, req.Key, ct);
        if (existing is not null)
            return Conflict(Problem("Configuration key already exists.", statusCode: 409));

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

    [HttpPut("{environmentSlug}/{key}")]
    public async Task<IActionResult> Update(
        string slug,
        string environmentSlug,
        string key,
        [FromBody] UpdateConfigurationRequest req,
        CancellationToken ct
    )
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return NotFound(Problem("Application not found.", statusCode: 404));

        var env = await envRepo.GetBySlugAsync(app.Id, environmentSlug, ct);
        if (env is null)
            return NotFound(Problem("Environment not found.", statusCode: 404));

        var entry = await configRepo.GetByKeyAsync(app.Id, env.Id, key, ct);
        if (entry is null)
            return NotFound(Problem("Configuration not found.", statusCode: 404));

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

    [HttpDelete("{environmentSlug}/{key}")]
    public async Task<IActionResult> Delete(
        string slug,
        string environmentSlug,
        string key,
        CancellationToken ct
    )
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return NotFound(Problem("Application not found.", statusCode: 404));

        var env = await envRepo.GetBySlugAsync(app.Id, environmentSlug, ct);
        if (env is null)
            return NotFound(Problem("Environment not found.", statusCode: 404));

        var entry = await configRepo.GetByKeyAsync(app.Id, env.Id, key, ct);
        if (entry is null)
            return NotFound(Problem("Configuration not found.", statusCode: 404));

        await configRepo.DeleteAsync(entry, ct);
        return NoContent();
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
