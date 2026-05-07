using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Management;

[ApiController]
[Route("api/management/applications/{slug}/secrets")]
[Tags("Management - Secrets")]
public class SecretsController(
    IApplicationRepository appRepo,
    IEnvironmentRepository envRepo,
    ISecretRepository secretRepo,
    IEncryptionService encryption
) : ControllerBase
{
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
                CreatedAt = DateTimeOffset.UtcNow,
            };
            await secretRepo.AddVersionAsync(sv, ct);
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
