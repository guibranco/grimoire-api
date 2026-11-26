using System.Text.Json;
using Grimoire.Api.Audit;
using Grimoire.Api.ConsumerTracking;
using Grimoire.Api.DTOs.Consumer;
using Grimoire.Api.Middleware;
using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Interfaces.Repositories;
using Grimoire.Core.Publishing;
using Grimoire.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Consumer;

[ApiController]
[TrackConsumerActivity]
[Route("consumer/configurations")]
[Tags("Consumer - Configurations")]
public class ConsumerConfigurationsController(
    IEnvironmentRepository envRepo,
    IEffectiveConfigurationService effective
) : ControllerBase
{
    private static readonly JsonSerializerOptions EtagOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Every configuration entry of the API key's application in <paramref name="environment"/>,
    /// globals merged in. Sends an <c>ETag</c>; with a matching <c>If-None-Match</c> the answer is
    /// <c>304</c> and no body, so polling for changes is cheap.
    /// </summary>
    [Audited(AuditActions.ConfigurationRead)]
    [HttpGet]
    [ProducesResponseType<ConfigurationListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAll([FromQuery] string environment, CancellationToken ct)
    {
        var app = HttpContext.Items[ConsumerApiKeyMiddleware.AppContextKey] as Application;
        if (app is null)
            return Unauthorized();

        var env = await envRepo.GetBySlugAsync(app.Id, environment, ct);
        if (env is null)
            return Problem(ProblemMessages.EnvironmentNotFound, statusCode: 404);
        HttpContext.Items[ConsumerApiKeyMiddleware.EnvironmentContextKey] = env;

        var entries = await effective.GetConfigurationAsync(app, env, ct);
        var items = entries
            .Where(e => !ConfigurationSentinel.IsSentinel(e.Key))
            .Select(e => new ConfigurationItem(
                e.Key,
                e.Value,
                environment,
                ConfigurationValueNormalizer.ToName(e.ValueType)
            ))
            .ToList();
        return WithEtag(new ConfigurationListResponse(items));
    }

    /// <summary>
    /// One configuration entry, falling back to an inherited global. Sends an <c>ETag</c>; with a
    /// matching <c>If-None-Match</c> the answer is <c>304</c>. Clients poll the <c>Sentinel</c> and
    /// <c>Global:Sentinel</c> keys this way to know when to reload; successful sentinel reads are
    /// not audited, only failures.
    /// </summary>
    [Audited(AuditActions.ConfigurationRead)]
    [HttpGet("{key}")]
    [ProducesResponseType<ConfigurationItem>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByKey(
        string key,
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

        var entry = await effective.GetConfigurationValueAsync(app, env, key, ct);
        if (entry is null)
            return Problem("Configuration key not found.", statusCode: 404);

        // Clients poll the sentinels constantly; a successful poll (200 or 304) would flood the
        // audit log, so only failed sentinel reads are recorded.
        if (ConfigurationSentinel.IsSentinel(entry.Key))
            AuditContext.Skip(HttpContext);

        return WithEtag(
            new ConfigurationItem(
                entry.Key,
                entry.Value,
                environment,
                ConfigurationValueNormalizer.ToName(entry.ValueType)
            )
        );
    }

    private IActionResult WithEtag<T>(T body)
    {
        var current = ConsumerEtag.IsCurrent(
            this,
            JsonSerializer.SerializeToUtf8Bytes(body, EtagOptions)
        );
        Response.Headers.CacheControl = "private, no-cache";
        if (current)
            return StatusCode(StatusCodes.Status304NotModified);
        return Ok(body);
    }
}
