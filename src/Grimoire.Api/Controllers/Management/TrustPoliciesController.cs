using Grimoire.Api.AccessTokens;
using Grimoire.Api.Audit;
using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Management;

/// <summary>
/// GitHub OIDC trust policies for an application (guibranco/grimoire-api#75): which GitHub
/// Actions runs may exchange their OIDC token for a short-lived access token, and what it reads.
/// </summary>
[ApiController]
[Route("management/applications/{slug}/trust-policies")]
[Tags("Management - Trust Policies")]
public class TrustPoliciesController(
    IApplicationRepository appRepo,
    ITrustPolicyRepository policies,
    IAccessTokenRepository tokens,
    AccessTokenFactory factory,
    TimeProvider time
) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IEnumerable<TrustPolicyResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(string slug, CancellationToken ct)
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return Problem(ProblemMessages.ApplicationNotFound, statusCode: 404);
        return Ok((await policies.ListAsync(app.Id, ct)).Select(ToResponse));
    }

    [Audited(AuditActions.TrustPolicyCreate)]
    [HttpPost]
    [ProducesResponseType<TrustPolicyResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        string slug,
        [FromBody] TrustPolicyRequest req,
        CancellationToken ct
    )
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return Problem(ProblemMessages.ApplicationNotFound, statusCode: 404);

        var policy = new TrustPolicy
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            CreatedAt = time.GetUtcNow(),
        };
        var invalid = await ApplyAsync(policy, req, ct);
        if (invalid is not null)
            return invalid;

        await policies.AddAsync(policy, ct);
        return CreatedAtAction(nameof(List), new { slug }, ToResponse(policy));
    }

    [Audited(AuditActions.TrustPolicyUpdate)]
    [HttpPut("{id:guid}")]
    [ProducesResponseType<TrustPolicyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        string slug,
        Guid id,
        [FromBody] TrustPolicyRequest req,
        CancellationToken ct
    )
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return Problem(ProblemMessages.ApplicationNotFound, statusCode: 404);
        var policy = await policies.GetAsync(app.Id, id, ct);
        if (policy is null)
            return Problem(ProblemMessages.TrustPolicyNotFound, statusCode: 404);

        var invalid = await ApplyAsync(policy, req, ct);
        if (invalid is not null)
            return invalid;

        await policies.UpdateAsync(policy, ct);
        return Ok(ToResponse(policy));
    }

    /// <summary>Deletes the policy and revokes the tokens it issued that haven't expired yet.</summary>
    [Audited(AuditActions.TrustPolicyDelete)]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string slug, Guid id, CancellationToken ct)
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return Problem(ProblemMessages.ApplicationNotFound, statusCode: 404);
        var policy = await policies.GetAsync(app.Id, id, ct);
        if (policy is null)
            return Problem(ProblemMessages.TrustPolicyNotFound, statusCode: 404);

        AuditContext.SetTarget(HttpContext, policy.Name);
        await tokens.RevokeIssuedAsync(policy.Id, time.GetUtcNow(), ct);
        await policies.DeleteAsync(policy, ct);
        return NoContent();
    }

    /// <summary>Copies the request onto the policy, or the problem that prevents it.</summary>
    private async Task<IActionResult?> ApplyAsync(
        TrustPolicy policy,
        TrustPolicyRequest req,
        CancellationToken ct
    )
    {
        var name = req.Name.Trim();
        if (await policies.NameExistsAsync(policy.ApplicationId, name, policy.Id, ct))
            return Problem(ProblemMessages.TrustPolicyExists, statusCode: 409, title: "Conflict");

        var (environments, unknown) = await factory.ResolveEnvironmentsAsync(
            policy.ApplicationId,
            req.Environments,
            ct
        );
        if (unknown.Count > 0)
        {
            ModelState.AddModelError(
                nameof(req.Environments),
                $"Unknown environment(s): {string.Join(", ", unknown)}."
            );
            return ValidationProblem(ModelState);
        }

        policy.Name = name;
        policy.Repository = req.Repository.Trim();
        policy.RefPattern = string.IsNullOrWhiteSpace(req.RefPattern)
            ? null
            : req.RefPattern.Trim();
        policy.GitHubEnvironment = string.IsNullOrWhiteSpace(req.GitHubEnvironment)
            ? null
            : req.GitHubEnvironment.Trim();
        policy.EnvironmentSlugs = environments;
        policy.Scopes = AccessTokenScopeNames.Parse(req.Scopes);
        policy.TokenLifetimeMinutes = req.TokenLifetimeMinutes
            ?? TrustPolicy.DefaultLifetimeMinutes;
        return null;
    }

    private static TrustPolicyResponse ToResponse(TrustPolicy p) =>
        new(
            p.Id,
            p.Name,
            p.Repository,
            p.RefPattern,
            p.GitHubEnvironment,
            p.EnvironmentSlugs,
            AccessTokenScopeNames.Names(p.Scopes),
            p.TokenLifetimeMinutes,
            p.LastUsedAt,
            p.CreatedAt
        );
}
