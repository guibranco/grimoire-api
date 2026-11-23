using Grimoire.Api.Audit;
using Grimoire.Api.DTOs.Management;
using Grimoire.Api.FeatureFlags;
using Grimoire.Core.Audit;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Grimoire.Api.Controllers.Management;

/// <summary>
/// Feature flags of an application: definitions, per-environment state and targeting.
/// Evaluation happens in the consumers; <c>evaluate</c> is a dry run for testing rules.
/// </summary>
[ApiController]
[Route("management/applications/{slug}/feature-flags")]
[Tags("Management - Feature Flags")]
public class FeatureFlagsController(
    IApplicationRepository appRepo,
    IEnvironmentRepository envRepo,
    IFeatureFlagRepository flags,
    TimeProvider time
) : FeatureFlagControllerBase(flags, time)
{
    /// <summary>The application's flags with their state in every environment, by key.</summary>
    /// <param name="slug">Application slug.</param>
    /// <param name="tag">Only flags with this tag.</param>
    /// <param name="environment">With <paramref name="enabled"/>: the environment to check.</param>
    /// <param name="enabled">With <paramref name="environment"/>: only flags enabled (true) or disabled (false) there.</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpGet]
    [ProducesResponseType<List<FeatureFlagResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        string slug,
        [FromQuery] string? tag,
        [FromQuery] string? environment,
        [FromQuery] bool? enabled,
        CancellationToken ct
    ) =>
        await ScopeAsync(slug, ct) is { } scope
            ? await ListCore(scope, tag, environment, enabled, ct)
            : AppNotFound();

    /// <summary>Creates a flag, disabled in every environment.</summary>
    [Audited(AuditActions.FeatureFlagCreate)]
    [HttpPost]
    [ProducesResponseType<FeatureFlagResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        string slug,
        [FromBody] CreateFeatureFlagRequest request,
        CancellationToken ct
    )
    {
        if (await ScopeAsync(slug, ct) is not { } scope)
            return AppNotFound();
        var (flag, error) = await CreateCore(scope, request, ct);
        return error
            ?? CreatedAtAction(
                nameof(Get),
                new { slug, key = flag!.Key },
                FeatureFlagMapper.ToResponse(flag, scope)
            );
    }

    /// <summary>One flag with its state in every environment.</summary>
    [HttpGet("{key}")]
    [ProducesResponseType<FeatureFlagResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string slug, string key, CancellationToken ct) =>
        await ScopeAsync(slug, ct) is { } scope ? await GetCore(scope, key, ct) : AppNotFound();

    /// <summary>
    /// Replaces the description, tags and, for multivariate flags, the variants and default
    /// variant. Variants still used by an environment's targeting can't be removed.
    /// </summary>
    [Audited(AuditActions.FeatureFlagUpdate)]
    [HttpPut("{key}")]
    [ProducesResponseType<FeatureFlagResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        string slug,
        string key,
        [FromBody] UpdateFeatureFlagRequest request,
        CancellationToken ct
    ) =>
        await ScopeAsync(slug, ct) is { } scope
            ? await UpdateCore(scope, key, request, ct)
            : AppNotFound();

    /// <summary>Deletes the flag and its state in every environment. Its history is kept.</summary>
    [Audited(AuditActions.FeatureFlagDelete)]
    [HttpDelete("{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string slug, string key, CancellationToken ct) =>
        await ScopeAsync(slug, ct) is { } scope ? await DeleteCore(scope, key, ct) : AppNotFound();

    /// <summary>
    /// Replaces the flag's state in one environment: on/off, off variant, ordered targeting rules
    /// and the fallthrough. Increments the state's version. Returns the whole flag.
    /// </summary>
    [Audited(AuditActions.FeatureFlagEnvironmentUpdate)]
    [HttpPut("{key}/environments/{environmentSlug}")]
    [ProducesResponseType<FeatureFlagResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PutEnvironment(
        string slug,
        string key,
        string environmentSlug,
        [FromBody] FlagEnvironmentStateRequest request,
        CancellationToken ct
    ) =>
        await ScopeAsync(slug, ct) is { } scope
            ? await PutEnvironmentCore(scope, key, environmentSlug, request, ct)
            : AppNotFound();

    /// <summary>
    /// Kill switch: turns the flag on or off in one environment (flips it without a body),
    /// keeping its targeting. Returns the whole flag.
    /// </summary>
    [Audited(AuditActions.FeatureFlagToggle)]
    [HttpPost("{key}/environments/{environmentSlug}/toggle")]
    [ProducesResponseType<FeatureFlagResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Toggle(
        string slug,
        string key,
        string environmentSlug,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ToggleFeatureFlagRequest? request,
        CancellationToken ct
    ) =>
        await ScopeAsync(slug, ct) is { } scope
            ? await ToggleCore(scope, key, environmentSlug, request, ct)
            : AppNotFound();

    /// <summary>
    /// Dry run: which variant the flag serves for a context in an environment, and why. The
    /// context is not stored.
    /// </summary>
    [HttpPost("{key}/evaluate")]
    [ProducesResponseType<FlagEvaluationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Evaluate(
        string slug,
        string key,
        [FromBody] EvaluateFeatureFlagRequest request,
        CancellationToken ct
    ) =>
        await ScopeAsync(slug, ct) is { } scope
            ? await EvaluateCore(scope, key, request, ct)
            : AppNotFound();

    /// <summary>Every change to the flag, newest first, with before/after JSON. Survives delete.</summary>
    [HttpGet("{key}/history")]
    [ProducesResponseType<List<FeatureFlagHistoryEntryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> History(string slug, string key, CancellationToken ct) =>
        await ScopeAsync(slug, ct) is { } scope ? await HistoryCore(scope, key, ct) : AppNotFound();

    private async Task<FlagScope?> ScopeAsync(string slug, CancellationToken ct)
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        return app is null
            ? null
            : FlagScope.For(app, await envRepo.GetByApplicationAsync(app.Id, ct));
    }

    private ObjectResult AppNotFound() =>
        Problem(ProblemMessages.ApplicationNotFound, statusCode: 404);
}
