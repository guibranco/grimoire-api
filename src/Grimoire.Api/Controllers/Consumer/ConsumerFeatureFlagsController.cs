using System.Security.Cryptography;
using System.Text.Json;
using Grimoire.Api.Audit;
using Grimoire.Api.ConsumerTracking;
using Grimoire.Api.DTOs.Consumer;
using Grimoire.Api.FeatureFlags;
using Grimoire.Api.Middleware;
using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Grimoire.Api.Controllers.Consumer;

/// <summary>
/// Flag definitions for client-side evaluation. Grimoire never evaluates on behalf of the
/// caller and never receives end-user attributes.
/// </summary>
[ApiController]
[TrackConsumerActivity]
[Route("consumer/feature-flags")]
[Tags("Consumer - Feature Flags")]
public class ConsumerFeatureFlagsController(
    IEnvironmentRepository envRepo,
    IFeatureFlagRepository flags
) : ControllerBase
{
    private static readonly JsonSerializerOptions EtagOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Every flag of the API key's application in <paramref name="environment"/>, plus the global
    /// flags it inherits (an application flag wins over a global with the same key). Sends an
    /// <c>ETag</c>; with a matching <c>If-None-Match</c> the answer is <c>304</c> and no body.
    /// </summary>
    [Audited(AuditActions.FeatureFlagRead)]
    [HttpGet]
    [ProducesResponseType<ConsumerFeatureFlagList>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAll([FromQuery] string environment, CancellationToken ct)
    {
        var (app, env, missing) = await ResolveAsync(environment, ct);
        if (missing is not null)
            return missing;

        var body = new ConsumerFeatureFlagList(
            env.Slug,
            EffectiveFeatureFlags.For(
                app,
                env,
                await flags.ListAsync(app.Id, ct),
                app.InheritGlobals ? await flags.ListAsync(null, ct) : []
            )
        );
        return WithEtag(body);
    }

    /// <summary>One flag's definition in <paramref name="environment"/>, with an <c>ETag</c>.</summary>
    [Audited(AuditActions.FeatureFlagRead)]
    [HttpGet("{key}")]
    [ProducesResponseType<ConsumerFeatureFlag>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        string key,
        [FromQuery] string environment,
        CancellationToken ct
    )
    {
        var (app, env, missing) = await ResolveAsync(environment, ct);
        if (missing is not null)
            return missing;

        var own = await flags.GetAsync(app.Id, key, ct);
        var global = own is null && app.InheritGlobals ? await flags.GetAsync(null, key, ct) : null;
        var flag = EffectiveFeatureFlags
            .For(app, env, own is null ? [] : [own], global is null ? [] : [global])
            .SingleOrDefault();
        return flag is null
            ? Problem(ProblemMessages.FeatureFlagNotFound, statusCode: 404)
            : WithEtag(flag);
    }

    private async Task<(Application App, AppEnvironment Env, IActionResult? Missing)> ResolveAsync(
        string environment,
        CancellationToken ct
    )
    {
        if (HttpContext.Items[ConsumerApiKeyMiddleware.AppContextKey] is not Application app)
            return (null!, null!, Unauthorized());
        var env = await envRepo.GetBySlugAsync(app.Id, environment, ct);
        if (env is null)
            return (app, null!, Problem(ProblemMessages.EnvironmentNotFound, statusCode: 404));
        HttpContext.Items[ConsumerApiKeyMiddleware.EnvironmentContextKey] = env;
        return (app, env, null);
    }

    /// <summary>Strong ETag over the serialized body; 304 when the client already has it.</summary>
    private IActionResult WithEtag<T>(T body)
    {
        var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(body, EtagOptions));
        var etag = new EntityTagHeaderValue($"\"{Convert.ToHexStringLower(hash)[..32]}\"");
        Response.Headers.ETag = etag.ToString();
        Response.Headers.CacheControl = "private, no-cache";

        var ifNoneMatch = Request.GetTypedHeaders().IfNoneMatch;
        if (ifNoneMatch.Any(t => t.Tag == etag.Tag || t.Equals(EntityTagHeaderValue.Any)))
            return StatusCode(StatusCodes.Status304NotModified);
        return Ok(body);
    }
}
