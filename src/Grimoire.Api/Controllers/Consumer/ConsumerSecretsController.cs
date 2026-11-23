using Grimoire.Api.Audit;
using Grimoire.Api.ConsumerTracking;
using Grimoire.Api.DTOs.Consumer;
using Grimoire.Api.Middleware;
using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Consumer;

[ApiController]
[TrackConsumerActivity]
[Route("consumer/secrets")]
[Tags("Consumer - Secrets")]
public class ConsumerSecretsController(
    IEnvironmentRepository envRepo,
    IEffectiveConfigurationService effective,
    IEncryptionService encryption
) : ControllerBase
{
    /// <summary>
    /// The secrets the application can read in <paramref name="environment"/> (its own and inherited
    /// globals), with the active version's metadata. Never returns values.
    /// </summary>
    [Audited(AuditActions.SecretList)]
    [HttpGet]
    [ProducesResponseType<SecretListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List([FromQuery] string environment, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var app = HttpContext.Items[ConsumerApiKeyMiddleware.AppContextKey] as Application;
        if (app is null)
            return Unauthorized();

        var env = await envRepo.GetBySlugAsync(app.Id, environment, ct);
        if (env is null)
            return Problem(ProblemMessages.EnvironmentNotFound, statusCode: 404);
        HttpContext.Items[ConsumerApiKeyMiddleware.EnvironmentContextKey] = env;

        var now = DateTimeOffset.UtcNow;
        var items = new List<SecretListItem>();
        foreach (var secret in await effective.GetSecretsAsync(app, ct))
        {
            var resolution = await effective.ResolveSecretAsync(app, env, secret.Name, now, ct);
            var active = resolution.Value;
            items.Add(
                new SecretListItem(
                    secret.Name,
                    secret.Source == ValueSource.Global ? "global" : "application",
                    active is not null,
                    active?.Version,
                    active?.ExpiresAt
                )
            );
        }
        return Ok(new SecretListResponse(items));
    }

    [Audited(AuditActions.SecretRead)]
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
            return Problem(ProblemMessages.EnvironmentNotFound, statusCode: 404);
        HttpContext.Items[ConsumerApiKeyMiddleware.EnvironmentContextKey] = env;

        var resolution = await effective.ResolveSecretAsync(
            app,
            env,
            name,
            DateTimeOffset.UtcNow,
            ct
        );

        switch (resolution.Status)
        {
            case SecretResolutionStatus.SecretNotFound:
                return Problem(ProblemMessages.SecretNotFound, statusCode: 404);
            case SecretResolutionStatus.NoActiveVersion:
                return Problem("No active secret version found.", statusCode: 404);
        }

        var version = resolution.Value!;
        HttpContext.Items[TrackConsumerActivityAttribute.SecretReadContextKey] = version;
        var plainValue = encryption.Decrypt(version.EncryptedValue);

        return Ok(
            new KeyVaultSecretResponse(
                version.Name,
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
