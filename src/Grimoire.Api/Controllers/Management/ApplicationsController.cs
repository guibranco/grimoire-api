using Grimoire.Api.Audit;
using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Interfaces.Repositories;
using Grimoire.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Management;

[ApiController]
[Route("management/applications")]
[Tags("Management - Applications")]
public class ApplicationsController(
    IApplicationRepository appRepo,
    IEnvironmentRepository envRepo,
    IPasswordHasher<Application> hasher,
    IConsumerActivityReader consumerActivity
) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var apps = await appRepo.GetAllAsync(ct);
        var lastSeen = await consumerActivity.GetLastSeenAsync(ct);
        return Ok(apps.Select(a => ToResponse(a, lastSeen)));
    }

    [Audited(AuditActions.ApplicationCreate)]
    [HttpPost]
    [ProducesResponseType<CreateApplicationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
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
            InheritGlobals = req.InheritGlobals ?? true,
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

        PreventCaching();
        return CreatedAtAction(
            nameof(GetBySlug),
            new { slug = app.Slug },
            new CreateApplicationResponse(
                app.Id,
                app.Name,
                app.Slug,
                app.Description,
                plainKey,
                app.CreatedAt,
                app.InheritGlobals
            )
        );
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken ct)
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return NotFound(ProblemDetailsFor("Application not found."));
        return Ok(ToResponse(app, await consumerActivity.GetLastSeenAsync(ct)));
    }

    [Audited(AuditActions.ApplicationUpdate)]
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
        if (req.InheritGlobals is { } inheritGlobals)
            app.InheritGlobals = inheritGlobals;
        app.UpdatedAt = DateTimeOffset.UtcNow;
        await appRepo.UpdateAsync(app, ct);

        return Ok(ToResponse(app, await consumerActivity.GetLastSeenAsync(ct)));
    }

    [Audited(AuditActions.ApplicationDelete)]
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

    [Audited(AuditActions.ApplicationRotateKey)]
    [HttpPost("{slug}/rotate-key")]
    [ProducesResponseType<RotateKeyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RotateKey(string slug, CancellationToken ct)
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return NotFound(ProblemDetailsFor("Application not found."));

        var plainKey = GenerateApiKey();
        app.ApiKeyHash = hasher.HashPassword(app, plainKey);
        app.UpdatedAt = DateTimeOffset.UtcNow;
        await appRepo.UpdateAsync(app, ct);

        PreventCaching();
        return Ok(new RotateKeyResponse(plainKey));
    }

    /// <summary>
    /// The plain API key is only returned once; make sure no proxy or browser cache keeps a copy of it.
    /// </summary>
    private void PreventCaching()
    {
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
    }

    private static ApplicationResponse ToResponse(
        Application app,
        IReadOnlyDictionary<(Guid ApplicationId, Guid EnvironmentId), DateTimeOffset>? lastSeen
    ) =>
        new(
            app.Id,
            app.Name,
            app.Slug,
            app.Description,
            app.InheritGlobals,
            app.CreatedAt,
            app.UpdatedAt,
            LatestFor(app.Id, lastSeen)
        );

    private static DateTimeOffset? LatestFor(
        Guid applicationId,
        IReadOnlyDictionary<(Guid ApplicationId, Guid EnvironmentId), DateTimeOffset>? lastSeen
    ) =>
        lastSeen
            ?.Where(p => p.Key.ApplicationId == applicationId)
            .Select(p => (DateTimeOffset?)p.Value)
            .Max();

    private static string GenerateApiKey()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return $"grm_{Convert.ToHexString(bytes).ToLowerInvariant()[..40]}";
    }

    private static ProblemDetails ProblemDetailsFor(string detail) =>
        new()
        {
            Title = "Error",
            Detail = detail,
            Status = 400,
        };
}
