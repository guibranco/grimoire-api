using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Management;

[ApiController]
[Route("api/management/applications/{slug}/configurations")]
[Tags("Management - Configurations")]
public class ConfigurationsController(
    IApplicationRepository appRepo,
    IEnvironmentRepository envRepo,
    IConfigurationRepository configRepo
) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(string slug, CancellationToken ct)
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return NotFound(Problem("Application not found.", statusCode: 404));

        var entries = await configRepo.GetByApplicationAsync(app.Id, ct);
        return Ok(
            entries.Select(e => new ConfigurationEntryResponse(
                e.Id,
                e.Environment.Slug,
                e.Key,
                e.Value,
                e.Description,
                e.CreatedAt,
                e.UpdatedAt
            ))
        );
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

        var entry = new ConfigurationEntry
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            EnvironmentId = env.Id,
            Key = req.Key,
            Value = req.Value,
            Description = req.Description,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await configRepo.AddAsync(entry, ct);

        return CreatedAtAction(
            nameof(List),
            new { slug },
            new ConfigurationEntryResponse(
                entry.Id,
                req.EnvironmentSlug,
                entry.Key,
                entry.Value,
                entry.Description,
                entry.CreatedAt,
                entry.UpdatedAt
            )
        );
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

        entry.Value = req.Value;
        entry.Description = req.Description ?? entry.Description;
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        await configRepo.UpdateAsync(entry, ct);

        return Ok(
            new ConfigurationEntryResponse(
                entry.Id,
                environmentSlug,
                entry.Key,
                entry.Value,
                entry.Description,
                entry.CreatedAt,
                entry.UpdatedAt
            )
        );
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
}
