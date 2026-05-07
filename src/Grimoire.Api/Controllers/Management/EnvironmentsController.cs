using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces.Repositories;
using Grimoire.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Management;

[ApiController]
[Route("api/management/applications/{slug}/environments")]
[Tags("Management - Environments")]
public class EnvironmentsController(IApplicationRepository appRepo, IEnvironmentRepository envRepo)
    : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(string slug, CancellationToken ct)
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return NotFound(NotFoundProblem("Application not found."));

        var envs = await envRepo.GetByApplicationAsync(app.Id, ct);
        return Ok(envs.Select(e => new EnvironmentResponse(e.Id, e.Name, e.Slug, e.CreatedAt)));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        string slug,
        [FromBody] CreateEnvironmentRequest req,
        CancellationToken ct
    )
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return NotFound(NotFoundProblem("Application not found."));

        var envSlug = SlugService.Generate(req.Name);
        var existing = await envRepo.GetBySlugAsync(app.Id, envSlug, ct);
        if (existing is not null)
            return Conflict(
                new ProblemDetails
                {
                    Title = "Conflict",
                    Detail = "Environment already exists.",
                    Status = 409,
                }
            );

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

    [HttpDelete("{envSlug}")]
    public async Task<IActionResult> Delete(string slug, string envSlug, CancellationToken ct)
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return NotFound(NotFoundProblem("Application not found."));

        var env = await envRepo.GetBySlugAsync(app.Id, envSlug, ct);
        if (env is null)
            return NotFound(NotFoundProblem("Environment not found."));

        await envRepo.DeleteAsync(env, ct);
        return NoContent();
    }

    private static ProblemDetails NotFoundProblem(string detail) =>
        new()
        {
            Title = "Not Found",
            Detail = detail,
            Status = 404,
        };
}
