using System.Security.Cryptography;
using Grimoire.Api.AccessTokens;
using Grimoire.Api.Audit;
using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Management;

/// <summary>
/// Consumer API access tokens for an application (guibranco/grimoire-api#75): each one limited
/// to some environments and scopes, optionally expiring, and revocable without touching the
/// application's API key or its other tokens.
/// </summary>
[ApiController]
[Route("management/applications/{slug}/access-tokens")]
[Tags("Management - Access Tokens")]
public class AccessTokensController(
    IApplicationRepository appRepo,
    IEnvironmentRepository envRepo,
    IAccessTokenRepository tokens,
    IPasswordHasher<AccessToken> hasher,
    TimeProvider time
) : ControllerBase
{
    private const string Base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const int SecretLength = 40;

    /// <summary>The application's tokens, newest first; never their values or hashes.</summary>
    [HttpGet]
    [ProducesResponseType<IEnumerable<AccessTokenResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(string slug, CancellationToken ct)
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return Problem(ProblemMessages.ApplicationNotFound, statusCode: 404);

        var now = time.GetUtcNow();
        var list = await tokens.ListAsync(app.Id, ct);
        return Ok(list.Select(t => ToResponse(t, now)));
    }

    /// <summary>Creates a token; the plain token is in this response only.</summary>
    [Audited(AuditActions.AccessTokenCreate)]
    [HttpPost]
    [ProducesResponseType<CreateAccessTokenResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        string slug,
        [FromBody] CreateAccessTokenRequest req,
        CancellationToken ct
    )
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return Problem(ProblemMessages.ApplicationNotFound, statusCode: 404);

        var name = req.Name.Trim();
        if (await tokens.NameExistsAsync(app.Id, name, ct))
            return Problem(ProblemMessages.AccessTokenExists, statusCode: 409, title: "Conflict");

        List<string>? environments = null;
        if (req.Environments is not null)
        {
            var known = (await envRepo.GetByApplicationAsync(app.Id, ct))
                .Select(e => e.Slug)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unknown = req.Environments.Where(e => !known.Contains(e)).ToList();
            if (unknown.Count > 0)
            {
                ModelState.AddModelError(
                    nameof(req.Environments),
                    $"Unknown environment(s): {string.Join(", ", unknown)}."
                );
                return ValidationProblem(ModelState);
            }
            environments = req
                .Environments.Select(e => e.ToLowerInvariant())
                .Distinct()
                .Order(StringComparer.Ordinal)
                .ToList();
        }

        var secret = RandomNumberGenerator.GetString(Base62, SecretLength);
        var plain = AccessToken.TokenPrefix + secret;
        var token = new AccessToken
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            Name = name,
            Prefix = secret[..AccessToken.LookupLength],
            EnvironmentSlugs = environments,
            Scopes = AccessTokenScopeNames.Parse(req.Scopes),
            ExpiresAt = req.ExpiresAt?.ToUniversalTime(),
            CreatedAt = time.GetUtcNow(),
        };
        token.TokenHash = hasher.HashPassword(token, plain);
        await tokens.AddAsync(token, ct);

        return CreatedAtAction(
            nameof(List),
            new { slug },
            new CreateAccessTokenResponse(ToResponse(token, token.CreatedAt), plain)
        );
    }

    /// <summary>Revokes a token at once; revoking it again changes nothing.</summary>
    [Audited(AuditActions.AccessTokenRevoke)]
    [HttpPost("{id:guid}/revoke")]
    [ProducesResponseType<AccessTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(string slug, Guid id, CancellationToken ct)
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return Problem(ProblemMessages.ApplicationNotFound, statusCode: 404);

        var token = await tokens.GetAsync(app.Id, id, ct);
        if (token is null)
            return Problem(ProblemMessages.AccessTokenNotFound, statusCode: 404);

        AuditContext.SetTarget(HttpContext, token.Name);
        var now = time.GetUtcNow();
        if (!token.IsRevoked)
        {
            token.RevokedAt = now;
            await tokens.UpdateAsync(token, ct);
        }
        return Ok(ToResponse(token, now));
    }

    private static AccessTokenResponse ToResponse(AccessToken t, DateTimeOffset now) =>
        new(
            t.Id,
            t.Name,
            t.Prefix,
            t.EnvironmentSlugs,
            AccessTokenScopeNames.Names(t.Scopes),
            t.ExpiresAt,
            t.LastUsedAt,
            t.RevokedAt,
            t.CreatedAt,
            StatusOf(t, now)
        );

    private static string StatusOf(AccessToken t, DateTimeOffset now) =>
        t.IsRevoked ? "revoked"
        : t.IsExpired(now) ? "expired"
        : "active";
}
