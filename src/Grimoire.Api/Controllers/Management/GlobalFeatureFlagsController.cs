using Grimoire.Api.Audit;
using Grimoire.Api.DTOs.Management;
using Grimoire.Api.FeatureFlags;
using Grimoire.Core.Audit;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Grimoire.Api.Controllers.Management;

/// <summary>
/// Global feature flags: every application that inherits globals receives them unless it has a
/// flag with the same key. State is per environment slug, with <c>*</c> for every environment;
/// a slug's own state wins over <c>*</c> (the same precedence as global configuration).
/// </summary>
[ApiController]
[Route("management/global/feature-flags")]
[Tags("Management - Global Feature Flags")]
public class GlobalFeatureFlagsController(
    IEnvironmentRepository envRepo,
    IFeatureFlagRepository flags,
    TimeProvider time
) : FeatureFlagControllerBase(flags, time)
{
    /// <summary>Global flags with their state for <c>*</c> and every known environment slug, by key.</summary>
    /// <param name="tag">Only flags with this tag.</param>
    /// <param name="environment">With <paramref name="enabled"/>: the slug (or <c>*</c>) to check.</param>
    /// <param name="enabled">With <paramref name="environment"/>: only flags enabled (true) or disabled (false) there.</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpGet]
    [ProducesResponseType<List<FeatureFlagResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        [FromQuery] string? tag,
        [FromQuery] string? environment,
        [FromQuery] bool? enabled,
        CancellationToken ct
    ) => await ListCore(await ScopeAsync(ct), tag, environment, enabled, ct);

    /// <summary>Creates a global flag, disabled everywhere.</summary>
    [Audited(AuditActions.GlobalFeatureFlagCreate)]
    [HttpPost]
    [ProducesResponseType<FeatureFlagResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateFeatureFlagRequest request,
        CancellationToken ct
    )
    {
        var scope = await ScopeAsync(ct);
        var (flag, error) = await CreateCore(scope, request, ct);
        return error
            ?? CreatedAtAction(
                nameof(Get),
                new { key = flag!.Key },
                FeatureFlagMapper.ToResponse(flag, scope)
            );
    }

    /// <summary>One global flag with its state for <c>*</c> and every known slug.</summary>
    [HttpGet("{key}")]
    [ProducesResponseType<FeatureFlagResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string key, CancellationToken ct) =>
        await GetCore(await ScopeAsync(ct), key, ct);

    /// <summary>Replaces the description, tags and (multivariate) variants and default variant.</summary>
    [Audited(AuditActions.GlobalFeatureFlagUpdate)]
    [HttpPut("{key}")]
    [ProducesResponseType<FeatureFlagResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        string key,
        [FromBody] UpdateFeatureFlagRequest request,
        CancellationToken ct
    ) => await UpdateCore(await ScopeAsync(ct), key, request, ct);

    /// <summary>Deletes the global flag. Its history is kept.</summary>
    [Audited(AuditActions.GlobalFeatureFlagDelete)]
    [HttpDelete("{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string key, CancellationToken ct) =>
        await DeleteCore(await ScopeAsync(ct), key, ct);

    /// <summary>
    /// Replaces the flag's state for an environment slug, or <c>*</c> for every environment.
    /// Any valid slug is accepted, including ones no application has yet.
    /// </summary>
    [Audited(AuditActions.GlobalFeatureFlagEnvironmentUpdate)]
    [HttpPut("{key}/environments/{environmentSlug}")]
    [ProducesResponseType<FeatureFlagResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PutEnvironment(
        string key,
        string environmentSlug,
        [FromBody] FlagEnvironmentStateRequest request,
        CancellationToken ct
    ) => await PutEnvironmentCore(await ScopeAsync(ct), key, environmentSlug, request, ct);

    /// <summary>Turns the flag on or off for an environment slug or <c>*</c> (flips it without a body).</summary>
    [Audited(AuditActions.GlobalFeatureFlagToggle)]
    [HttpPost("{key}/environments/{environmentSlug}/toggle")]
    [ProducesResponseType<FeatureFlagResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Toggle(
        string key,
        string environmentSlug,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ToggleFeatureFlagRequest? request,
        CancellationToken ct
    ) => await ToggleCore(await ScopeAsync(ct), key, environmentSlug, request, ct);

    /// <summary>
    /// Dry run for an environment slug: its own state, else the <c>*</c> state. The context is
    /// not stored.
    /// </summary>
    [HttpPost("{key}/evaluate")]
    [ProducesResponseType<FlagEvaluationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Evaluate(
        string key,
        [FromBody] EvaluateFeatureFlagRequest request,
        CancellationToken ct
    ) => await EvaluateCore(await ScopeAsync(ct), key, request, ct);

    /// <summary>Every change to the global flag, newest first. Survives delete.</summary>
    [HttpGet("{key}/history")]
    [ProducesResponseType<List<FeatureFlagHistoryEntryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> History(string key, CancellationToken ct) =>
        await HistoryCore(await ScopeAsync(ct), key, ct);

    private async Task<FlagScope> ScopeAsync(CancellationToken ct) =>
        FlagScope.Global(
            (await envRepo.GetAllForActiveApplicationsAsync(ct)).Select(e => e.Slug),
            await Flags.ListAsync(null, ct)
        );
}
