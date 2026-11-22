using Grimoire.Api.Audit;
using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

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
            return Conflict(Problem("Global secret already exists.", statusCode: 409));

        var secret = new GlobalSecret
        {
            Id = Guid.NewGuid(),
            Name = req.Name,
            Description = req.Description,
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
            return NotFound(Problem("Global secret not found.", statusCode: 404));

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
            return NotFound(Problem("Global secret not found.", statusCode: 404));

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
                CreatedAt = DateTimeOffset.UtcNow,
            };
            await globalSecretRepo.AddVersionAsync(version, ct);
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
            return NotFound(Problem("Global secret not found.", statusCode: 404));

        var versions = await globalSecretRepo.GetVersionsAsync(secret.Id, environmentSlug, ct);
        return Ok(
            versions.Select(v => new SecretVersionMetadata(
                v.Id,
                v.Version,
                v.IsEnabled,
                v.ExpiresAt,
                v.NotBefore,
                v.CreatedAt
            ))
        );
    }

    [Audited(AuditActions.GlobalSecretDelete)]
    [HttpDelete("{name}")]
    public async Task<IActionResult> Delete(string name, CancellationToken ct)
    {
        var secret = await globalSecretRepo.GetByNameAsync(name, ct);
        if (secret is null)
            return NotFound(Problem("Global secret not found.", statusCode: 404));

        await globalSecretRepo.DeleteAsync(secret, ct);
        return NoContent();
    }

    private static GlobalSecretResponse ToResponse(GlobalSecret s) =>
        new(s.Id, s.Name, s.Description, s.CreatedAt, s.UpdatedAt);
}
