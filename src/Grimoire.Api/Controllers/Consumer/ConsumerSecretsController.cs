using Grimoire.Api.DTOs.Consumer;
using Grimoire.Api.Middleware;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Consumer;

[ApiController]
[Route("api/consumer/secrets")]
[Tags("Consumer - Secrets")]
public class ConsumerSecretsController(
    ISecretRepository secretRepo,
    IEnvironmentRepository envRepo,
    IEncryptionService encryption
) : ControllerBase
{
    [HttpGet("{name}")]
    public async Task<IActionResult> GetSecret(
        string name,
        [FromQuery] string environment,
        CancellationToken ct
    )
    {
        var app = HttpContext.Items[ConsumerApiKeyMiddleware.AppContextKey] as Application;
        if (app is null)
            return Unauthorized();

        var env = await envRepo.GetBySlugAsync(app.Id, environment, ct);
        if (env is null)
            return NotFound(Problem("Environment not found.", statusCode: 404));

        var secret = await secretRepo.GetByNameAsync(app.Id, name, ct);
        if (secret is null)
            return NotFound(Problem("Secret not found.", statusCode: 404));

        var now = DateTimeOffset.UtcNow;
        var version = await secretRepo.GetActiveVersionAsync(secret.Id, env.Id, now, ct);
        if (version is null)
            return NotFound(Problem("No active secret version found.", statusCode: 404));

        var plainValue = encryption.Decrypt(version.EncryptedValue);

        return Ok(
            new KeyVaultSecretResponse(
                secret.Name,
                plainValue,
                new SecretProperties(
                    version.IsEnabled,
                    version.ExpiresAt,
                    version.NotBefore,
                    version.Version,
                    version.CreatedAt,
                    null
                )
            )
        );
    }
}
