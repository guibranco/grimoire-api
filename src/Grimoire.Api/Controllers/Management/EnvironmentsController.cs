using Grimoire.Api.Audit;
using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Interfaces.Repositories;
using Grimoire.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Management;

[ApiController]
[Route("management/applications/{slug}/environments")]
[Tags("Management - Environments")]
public class EnvironmentsController(
    IApplicationRepository appRepo,
    IEnvironmentRepository envRepo,
    IConsumerActivityReader consumerActivity
) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(string slug, CancellationToken ct)
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return Problem(ProblemMessages.ApplicationNotFound, statusCode: 404);

        var envs = await envRepo.GetByApplicationAsync(app.Id, ct);
        var lastSeen = await consumerActivity.GetLastSeenAsync(ct);
        return Ok(
            envs.Select(e => new EnvironmentResponse(
                e.Id,
                e.Name,
                e.Slug,
                e.CreatedAt,
                lastSeen is not null && lastSeen.TryGetValue((app.Id, e.Id), out var seen)
                    ? seen
                    : null
            ))
        );
    }

    [Audited(AuditActions.EnvironmentCreate)]
    [HttpPost]
    public async Task<IActionResult> Create(
        string slug,
        [FromBody] CreateEnvironmentRequest req,
        CancellationToken ct
    )
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return Problem(ProblemMessages.ApplicationNotFound, statusCode: 404);

        var envSlug = SlugService.Generate(req.Name);
        var existing = await envRepo.GetBySlugAsync(app.Id, envSlug, ct);
        if (existing is not null)
            return Problem("Environment already exists.", statusCode: 409, title: "Conflict");

        var env = new AppEnvironment
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            Name = req.Name,
            Slug = envSlug,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await envRepo.AddAsync(env, ct);

        return CreatedAtAction(
            nameof(List),
            new { slug },
            new EnvironmentResponse(env.Id, env.Name, env.Slug, env.CreatedAt)
        );
    }

    [Audited(AuditActions.EnvironmentDelete)]
    [HttpDelete("{envSlug}")]
    public async Task<IActionResult> Delete(string slug, string envSlug, CancellationToken ct)
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return Problem(ProblemMessages.ApplicationNotFound, statusCode: 404);

        var env = await envRepo.GetBySlugAsync(app.Id, envSlug, ct);
        if (env is null)
            return Problem(ProblemMessages.EnvironmentNotFound, statusCode: 404);

        await envRepo.DeleteAsync(env, ct);
        return NoContent();
    }
}
