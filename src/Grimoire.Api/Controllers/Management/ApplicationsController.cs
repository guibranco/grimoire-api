using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces.Repositories;
using Grimoire.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Management;

[ApiController]
[Route("api/management/applications")]
[Tags("Management - Applications")]
public class ApplicationsController(
    IApplicationRepository appRepo,
    IEnvironmentRepository envRepo,
    IPasswordHasher<Application> hasher
) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var apps = await appRepo.GetAllAsync(ct);
        return Ok(
            apps.Select(a => new ApplicationResponse(
                a.Id,
                a.Name,
                a.Slug,
                a.Description,
                a.CreatedAt,
                a.UpdatedAt
            ))
        );
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateApplicationRequest req,
        CancellationToken ct
    )
    {
        var slug = SlugService.Generate(req.Name);
        if (await appRepo.SlugExistsAsync(slug, ct))
            return Conflict(ProblemDetailsFor("An application with this name already exists."));

        var plainKey = GenerateApiKey();
        var app = new Application
        {
            Id = Guid.NewGuid(),
            Name = req.Name,
            Slug = slug,
            Description = req.Description,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        app.ApiKeyHash = hasher.HashPassword(app, plainKey);

        await appRepo.AddAsync(app, ct);

        // Seed "local" environment
        var localEnv = new AppEnvironment
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            Name = "Local",
            Slug = "local",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await envRepo.AddAsync(localEnv, ct);

        return CreatedAtAction(
            nameof(GetBySlug),
            new { slug = app.Slug },
            new CreateApplicationResponse(
                app.Id,
                app.Name,
                app.Slug,
                app.Description,
                plainKey,
                app.CreatedAt
            )
        );
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken ct)
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return NotFound(ProblemDetailsFor("Application not found."));
        return Ok(
            new ApplicationResponse(
                app.Id,
                app.Name,
                app.Slug,
                app.Description,
                app.CreatedAt,
                app.UpdatedAt
            )
        );
    }

    [HttpPut("{slug}")]
    public async Task<IActionResult> Update(
        string slug,
        [FromBody] UpdateApplicationRequest req,
        CancellationToken ct
    )
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return NotFound(ProblemDetailsFor("Application not found."));

        app.Name = req.Name;
        app.Description = req.Description;
        app.UpdatedAt = DateTimeOffset.UtcNow;
        await appRepo.UpdateAsync(app, ct);

        return Ok(
            new ApplicationResponse(
                app.Id,
                app.Name,
                app.Slug,
                app.Description,
                app.CreatedAt,
                app.UpdatedAt
            )
        );
    }

    [HttpDelete("{slug}")]
    public async Task<IActionResult> Delete(string slug, CancellationToken ct)
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return NotFound(ProblemDetailsFor("Application not found."));

        app.IsDeleted = true;
        app.UpdatedAt = DateTimeOffset.UtcNow;
        await appRepo.UpdateAsync(app, ct);

        return NoContent();
    }

    [HttpPost("{slug}/rotate-key")]
    public async Task<IActionResult> RotateKey(string slug, CancellationToken ct)
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return NotFound(ProblemDetailsFor("Application not found."));

        var plainKey = GenerateApiKey();
        app.ApiKeyHash = hasher.HashPassword(app, plainKey);
        app.UpdatedAt = DateTimeOffset.UtcNow;
        await appRepo.UpdateAsync(app, ct);

        return Ok(new RotateKeyResponse(plainKey));
    }

    private static string GenerateApiKey() =>
        $"grm_{Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).Replace("+", "").Replace("/", "").Replace("=", "")[..40]}";

    private static ProblemDetails ProblemDetailsFor(string detail) =>
        new()
        {
            Title = "Error",
            Detail = detail,
            Status = 400,
        };
}
