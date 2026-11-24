using Grimoire.Api.Audit;
using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces.Repositories;
using Grimoire.Core.Publishing;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Management;

/// <summary>
/// Auto-publish and explicit publishing of an environment's configuration. Clients watch the
/// <c>Sentinel</c> key and reload when it changes: with auto-publish on (the default) every
/// change bumps it; with it off, only a publish does, so running clients reload once after a
/// batch of edits. Saved values are served straight away either way.
/// </summary>
[ApiController]
[Route("management/applications/{slug}/environments/{environmentSlug}")]
[Tags("Management - Publishing")]
public class PublishingController(
    IApplicationRepository appRepo,
    IEnvironmentRepository envRepo,
    IPublishingService publishing
) : ControllerBase
{
    /// <summary>Auto-publish setting, current sentinel and unpublished changes.</summary>
    [HttpGet("publishing")]
    [ProducesResponseType<PublishingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string slug, string environmentSlug, CancellationToken ct)
    {
        var (env, missing) = await FindAsync(slug, environmentSlug, ct);
        if (missing is not null)
            return missing;
        return Ok(PublishingResponse.From(await publishing.GetAsync(env, ct)));
    }

    /// <summary>Turns auto-publish on or off. Turning it on publishes pending changes.</summary>
    [Audited(AuditActions.PublishingUpdate)]
    [HttpPut("publishing")]
    [ProducesResponseType<PublishingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        string slug,
        string environmentSlug,
        [FromBody] UpdatePublishingRequest req,
        CancellationToken ct
    )
    {
        var (env, missing) = await FindAsync(slug, environmentSlug, ct);
        if (missing is not null)
            return missing;
        AuditContext.SetTarget(HttpContext, $"{slug}/{environmentSlug}");
        return Ok(
            PublishingResponse.From(await publishing.SetAutoPublishAsync(env, req.AutoPublish, ct))
        );
    }

    /// <summary>
    /// Bumps the environment's <c>Sentinel</c>, so its clients reload. Works whether or not
    /// auto-publish is on.
    /// </summary>
    [Audited(AuditActions.Publish)]
    [HttpPost("publish")]
    [ProducesResponseType<PublishingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Publish(
        string slug,
        string environmentSlug,
        CancellationToken ct
    )
    {
        var (env, missing) = await FindAsync(slug, environmentSlug, ct);
        if (missing is not null)
            return missing;
        AuditContext.SetTarget(HttpContext, $"{slug}/{environmentSlug}");
        return Ok(PublishingResponse.From(await publishing.PublishAsync(env, ct)));
    }

    private async Task<(AppEnvironment Env, IActionResult? Missing)> FindAsync(
        string slug,
        string environmentSlug,
        CancellationToken ct
    )
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return (null!, Problem(ProblemMessages.ApplicationNotFound, statusCode: 404));
        var env = await envRepo.GetBySlugAsync(app.Id, environmentSlug, ct);
        if (env is null)
            return (null!, Problem(ProblemMessages.EnvironmentNotFound, statusCode: 404));
        return (env, null);
    }
}
