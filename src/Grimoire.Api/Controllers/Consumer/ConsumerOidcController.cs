using Grimoire.Api.AccessTokens;
using Grimoire.Api.DTOs.Consumer;
using Grimoire.Api.Oidc;
using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Consumer;

/// <summary>
/// Exchanges a GitHub Actions OIDC token for a short-lived access token (guibranco/grimoire-api#75),
/// so workflows need no stored Grimoire secret. Needs no <c>X-Api-Key</c>: the OIDC token, checked
/// against the application's trust policies, is the credential.
/// </summary>
[ApiController]
[Route("consumer/oidc")]
[Tags("Consumer - OIDC")]
public class ConsumerOidcController(
    GitHubOidcOptions options,
    GitHubOidcValidator validator,
    IApplicationRepository appRepo,
    ITrustPolicyRepository policies,
    IAccessTokenRepository tokens,
    AccessTokenFactory factory,
    IssuedTokenCleanup cleanup,
    IAuditLog auditLog,
    TimeProvider time
) : ControllerBase
{
    /// <summary>
    /// A token for the first (most specific) trust policy the run's <c>repository</c>, <c>ref</c>
    /// and <c>environment</c> claims satisfy. Any failure is a <c>401</c> that doesn't say why.
    /// </summary>
    [HttpPost("github")]
    [ProducesResponseType<GitHubOidcExchangeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExchangeGitHub(
        [FromBody] GitHubOidcExchangeRequest req,
        CancellationToken ct
    )
    {
        if (!options.Enabled)
            return Problem("GitHub OIDC is disabled on this instance.", statusCode: 404);

        var claims = await validator.ValidateAsync(req.Token, ct);
        if (claims is null)
            return Reject(null, req.Application);
        var app = await appRepo.GetBySlugAsync(req.Application, ct);
        if (app is null)
            return Reject(claims, req.Application);
        var policy = TrustPolicyMatcher.BestMatch(await policies.ListAsync(app.Id, ct), claims);
        if (policy is null)
            return Reject(claims, app.Slug);

        var now = time.GetUtcNow();
        await cleanup.RunAsync(tokens, ct);
        var expiresAt = now.AddMinutes(policy.TokenLifetimeMinutes);
        var (token, plain) = factory.Create(
            app.Id,
            $"github-oidc/{policy.Name}/{claims.RunId ?? "run"}/{Guid.NewGuid():N}",
            policy.EnvironmentSlugs,
            policy.Scopes,
            expiresAt,
            policy.Id
        );
        await tokens.AddAsync(token, ct);
        await policies.TouchAsync(policy.Id, now, ct);

        Record(claims, app.Slug, policy.Name, 200, AuditOutcome.Success);
        return Ok(
            new GitHubOidcExchangeResponse(
                plain,
                expiresAt,
                policy.EnvironmentSlugs,
                AccessTokenScopeNames.Names(policy.Scopes),
                policy.Name
            )
        );
    }

    /// <summary>The same <c>401</c> whatever failed, so callers can't probe which part did.</summary>
    private ObjectResult Reject(GitHubOidcClaims? claims, string? application)
    {
        Record(claims, application, null, 401, AuditOutcome.Forbidden);
        return Problem(
            "The OIDC token is invalid, or no trust policy allows it.",
            statusCode: 401,
            title: "Unauthorized"
        );
    }

    private void Record(
        GitHubOidcClaims? claims,
        string? application,
        string? policy,
        int status,
        AuditOutcome outcome
    ) =>
        auditLog.Record(
            new AuditEvent
            {
                OccurredAt = time.GetUtcNow(),
                Actor = claims is null ? "anonymous" : $"github:{claims.Repository}",
                Action = AuditActions.AccessTokenOidcExchange,
                ApplicationSlug = string.IsNullOrWhiteSpace(application) ? null : application,
                TargetType = AuditActions.TargetType(AuditActions.AccessTokenOidcExchange),
                Target = policy ?? claims?.Ref,
                StatusCode = status,
                Outcome = outcome,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString(),
            }
        );
}
