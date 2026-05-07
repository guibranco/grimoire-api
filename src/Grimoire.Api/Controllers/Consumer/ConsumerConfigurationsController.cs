using Grimoire.Api.DTOs.Consumer;
using Grimoire.Api.Middleware;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Consumer;

[ApiController]
[Route("api/consumer/configurations")]
[Tags("Consumer - Configurations")]
public class ConsumerConfigurationsController(
    IConfigurationRepository configRepo,
    IEnvironmentRepository envRepo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string environment, CancellationToken ct)
    {
        var app = HttpContext.Items[ConsumerApiKeyMiddleware.AppContextKey] as Application;
        if (app is null) return Unauthorized();

        var env = await envRepo.GetBySlugAsync(app.Id, environment, ct);
        if (env is null) return NotFound(Problem("Environment not found.", statusCode: 404));

        var entries = await configRepo.GetByEnvironmentAsync(app.Id, env.Id, ct);
        var items = entries.Select(e => new ConfigurationItem(e.Key, e.Value, environment)).ToList();
        return Ok(new ConfigurationListResponse(items));
    }

    [HttpGet("{key}")]
    public async Task<IActionResult> GetByKey(string key, [FromQuery] string environment, CancellationToken ct)
    {
        var app = HttpContext.Items[ConsumerApiKeyMiddleware.AppContextKey] as Application;
        if (app is null) return Unauthorized();

        var env = await envRepo.GetBySlugAsync(app.Id, environment, ct);
        if (env is null) return NotFound(Problem("Environment not found.", statusCode: 404));

        var entry = await configRepo.GetByKeyAsync(app.Id, env.Id, key, ct);
        if (entry is null) return NotFound(Problem("Configuration key not found.", statusCode: 404));

        return Ok(new ConfigurationItem(entry.Key, entry.Value, environment));
    }
}
