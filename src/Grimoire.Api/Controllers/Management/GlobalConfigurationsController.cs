using Grimoire.Api.Audit;
using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces.Repositories;
using Grimoire.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Management;

[ApiController]
[Route("management/global/configurations")]
[Tags("Management - Global Configurations")]
public class GlobalConfigurationsController(IGlobalConfigurationRepository globalConfigRepo)
    : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? environment, CancellationToken ct)
    {
        var entries = await globalConfigRepo.GetAllAsync(environment, ct);
        return Ok(entries.Select(ToResponse));
    }

    [Audited(AuditActions.GlobalConfigurationCreate)]
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateGlobalConfigurationRequest req,
        CancellationToken ct
    )
    {
        var environmentSlug = req.EnvironmentSlug ?? GlobalConfigurationEntry.AllEnvironments;

        var existing = await globalConfigRepo.GetByKeyAsync(environmentSlug, req.Key, ct);
        if (existing is not null)
            return Problem("Global configuration key already exists.", statusCode: 409);

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

        var entry = new GlobalConfigurationEntry
        {
            Id = Guid.NewGuid(),
            EnvironmentSlug = environmentSlug,
            Key = req.Key,
            Value = value,
            ValueType = valueType,
            Description = req.Description,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await globalConfigRepo.AddAsync(entry, ct);

        return CreatedAtAction(nameof(List), null, ToResponse(entry));
    }

    [Audited(AuditActions.GlobalConfigurationUpdate)]
    [HttpPut("{environmentSlug}/{key}")]
    public async Task<IActionResult> Update(
        string environmentSlug,
        string key,
        [FromBody] UpdateGlobalConfigurationRequest req,
        CancellationToken ct
    )
    {
        var entry = await globalConfigRepo.GetByKeyAsync(environmentSlug, key, ct);
        if (entry is null)
            return Problem("Global configuration not found.", statusCode: 404);

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
        await globalConfigRepo.UpdateAsync(entry, ct);

        return Ok(ToResponse(entry));
    }

    [Audited(AuditActions.GlobalConfigurationDelete)]
    [HttpDelete("{environmentSlug}/{key}")]
    public async Task<IActionResult> Delete(
        string environmentSlug,
        string key,
        CancellationToken ct
    )
    {
        var entry = await globalConfigRepo.GetByKeyAsync(environmentSlug, key, ct);
        if (entry is null)
            return Problem("Global configuration not found.", statusCode: 404);

        await globalConfigRepo.DeleteAsync(entry, ct);
        return NoContent();
    }

    private static GlobalConfigurationEntryResponse ToResponse(GlobalConfigurationEntry e) =>
        new(
            e.Id,
            e.EnvironmentSlug,
            e.Key,
            e.Value,
            e.Description,
            e.CreatedAt,
            e.UpdatedAt,
            ConfigurationValueNormalizer.ToName(e.ValueType)
        );
}
