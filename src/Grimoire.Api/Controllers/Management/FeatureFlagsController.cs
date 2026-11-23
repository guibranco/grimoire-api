using System.Security.Cryptography;
using Grimoire.Api.Audit;
using Grimoire.Api.DTOs.Management;
using Grimoire.Api.FeatureFlags;
using Grimoire.Api.Validators.Management;
using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Core.FeatureFlags;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

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
) : ControllerBase
{
    private const string Actor = "admin";

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
    )
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return Problem(ProblemMessages.ApplicationNotFound, statusCode: 404);
        var envs = await envRepo.GetByApplicationAsync(app.Id, ct);

        Guid? envId = null;
        if (environment is not null)
        {
            var env = envs.FirstOrDefault(e => e.Slug == environment);
            if (env is null)
                return Problem(ProblemMessages.EnvironmentNotFound, statusCode: 404);
            envId = env.Id;
        }

        var all = await flags.ListAsync(app.Id, ct);
        var result = all.Where(f =>
                (tag is null || f.Tags.Contains(tag, StringComparer.Ordinal))
                && (
                    envId is null
                    || enabled is null
                    || (
                        f.Environments.FirstOrDefault(s => s.EnvironmentId == envId)?.Enabled
                        ?? false
                    ) == enabled
                )
            )
            .Select(f => FeatureFlagMapper.ToResponse(f, envs))
            .ToList();
        return Ok(result);
    }

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
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return Problem(ProblemMessages.ApplicationNotFound, statusCode: 404);

        var type = request.Type ?? FlagTypes.Boolean;
        var variants =
            type == FlagTypes.Boolean
                ? FlagTypes.BooleanVariants.ToList()
                : request.Variants!.Select(FeatureFlagMapper.ToModel).ToList();
        var defaultVariant = request.DefaultVariant
            ?? (type == FlagTypes.Boolean ? FlagTypes.Off : variants[0].Key);
        if (!variants.Any(v => v.Key == defaultVariant))
            return UnknownVariant(nameof(request.DefaultVariant), defaultVariant);

        var now = time.GetUtcNow();
        var flag = new FeatureFlag
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            Key = request.Key,
            Description = request.Description,
            Type = type,
            Variants = variants,
            DefaultVariant = defaultVariant,
            Tags = Tags(request.Tags),
            Salt = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)),
            CreatedAt = now,
            UpdatedAt = now,
        };

        var created = await flags.InWriteTransactionAsync(
            async token =>
            {
                if (await flags.GetAsync(app.Id, request.Key, token) is not null)
                    return false;
                await flags.SaveAsync(
                    flag,
                    Audit(app, flag.Key, null, "create", null, FeatureFlagMapper.Snapshot(flag)),
                    token
                );
                return true;
            },
            ct
        );
        if (!created)
            return Problem(ProblemMessages.FeatureFlagExists, statusCode: 409);

        var envs = await envRepo.GetByApplicationAsync(app.Id, ct);
        return CreatedAtAction(
            nameof(Get),
            new { slug, key = flag.Key },
            FeatureFlagMapper.ToResponse(flag, envs)
        );
    }

    /// <summary>One flag with its state in every environment.</summary>
    [HttpGet("{key}")]
    [ProducesResponseType<FeatureFlagResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string slug, string key, CancellationToken ct)
    {
        var (app, flag, missing) = await FindAsync(slug, key, ct);
        if (missing is not null)
            return missing;
        return Ok(
            FeatureFlagMapper.ToResponse(flag, await envRepo.GetByApplicationAsync(app.Id, ct))
        );
    }

    /// <summary>
    /// Replaces the description, tags and, for multivariate flags, the variants and default
    /// variant. Variants still used by an environment's targeting can't be removed.
    /// </summary>
    [Audited(AuditActions.FeatureFlagUpdate)]
    [HttpPut("{key}")]
    [ProducesResponseType<FeatureFlagResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Update(
        string slug,
        string key,
        [FromBody] UpdateFeatureFlagRequest request,
        CancellationToken ct
    ) =>
        MutateAsync(
            slug,
            key,
            (app, flag) =>
            {
                if (flag.Type == FlagTypes.Boolean && request.Variants is not null)
                    return Invalid(
                        nameof(request.Variants),
                        "Boolean flags have the fixed variants 'on' and 'off'."
                    );

                var variants = request.Variants?.Select(FeatureFlagMapper.ToModel).ToList()
                    ?? flag.Variants;
                var keys = variants.Select(v => v.Key).ToHashSet(StringComparer.Ordinal);
                var defaultVariant = request.DefaultVariant ?? flag.DefaultVariant;
                if (!keys.Contains(defaultVariant))
                    return UnknownVariant(nameof(request.DefaultVariant), defaultVariant);

                var inUse = flag
                    .Environments.SelectMany(UsedVariants)
                    .Where(v => !keys.Contains(v))
                    .Distinct()
                    .ToList();
                if (inUse.Count > 0)
                    return Invalid(
                        nameof(request.Variants),
                        $"Variants still used by environment targeting: {string.Join(", ", inUse)}."
                    );

                var before = FeatureFlagMapper.Snapshot(flag);
                flag.Description = request.Description;
                flag.Variants = variants;
                flag.DefaultVariant = defaultVariant;
                flag.Tags = Tags(request.Tags);
                flag.UpdatedAt = time.GetUtcNow();
                return Change(app, flag, null, "update", before, FeatureFlagMapper.Snapshot(flag));
            },
            ct
        );

    /// <summary>Deletes the flag and its state in every environment. Its history is kept.</summary>
    [Audited(AuditActions.FeatureFlagDelete)]
    [HttpDelete("{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string slug, string key, CancellationToken ct)
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return Problem(ProblemMessages.ApplicationNotFound, statusCode: 404);

        var deleted = await flags.InWriteTransactionAsync(
            async token =>
            {
                var flag = await flags.GetAsync(app.Id, key, token);
                if (flag is null)
                    return false;
                await flags.DeleteAsync(
                    flag,
                    Audit(app, flag.Key, null, "delete", FeatureFlagMapper.Snapshot(flag), null),
                    token
                );
                return true;
            },
            ct
        );
        return deleted
            ? NoContent()
            : Problem(ProblemMessages.FeatureFlagNotFound, statusCode: 404);
    }

    /// <summary>
    /// Replaces the flag's state in one environment: on/off, off variant, ordered targeting rules
    /// and the fallthrough. Increments the state's version. Returns the whole flag.
    /// </summary>
    [Audited(AuditActions.FeatureFlagEnvironmentUpdate)]
    [HttpPut("{key}/environments/{environmentSlug}")]
    [ProducesResponseType<FeatureFlagResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> PutEnvironment(
        string slug,
        string key,
        string environmentSlug,
        [FromBody] FlagEnvironmentStateRequest request,
        CancellationToken ct
    ) =>
        MutateEnvironmentAsync(
            slug,
            key,
            environmentSlug,
            "environment",
            (flag, state) =>
            {
                var keys = flag.Variants.Select(v => v.Key).ToHashSet(StringComparer.Ordinal);
                var unknown = FeatureFlagRules.UnknownVariants(request, keys).ToList();
                if (unknown.Count > 0)
                    return Invalid("Variant", $"Unknown variants: {string.Join(", ", unknown)}.");

                state.Enabled = request.Enabled;
                state.OffVariant = request.OffVariant;
                state.Rules = (request.Rules ?? []).Select(FeatureFlagMapper.ToModel).ToList();
                state.Fallthrough = request.Fallthrough is null
                    ? null
                    : FeatureFlagMapper.ToModel(request.Fallthrough);
                return null;
            },
            ct
        );

    /// <summary>
    /// Kill switch: turns the flag on or off in one environment (flips it without a body),
    /// keeping its targeting. Returns the whole flag.
    /// </summary>
    [Audited(AuditActions.FeatureFlagToggle)]
    [HttpPost("{key}/environments/{environmentSlug}/toggle")]
    [ProducesResponseType<FeatureFlagResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Toggle(
        string slug,
        string key,
        string environmentSlug,
        [FromBody(
            EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow
        )]
            ToggleFeatureFlagRequest? request,
        CancellationToken ct
    ) =>
        MutateEnvironmentAsync(
            slug,
            key,
            environmentSlug,
            "toggle",
            (_, state) =>
            {
                state.Enabled = request?.Enabled ?? !state.Enabled;
                return null;
            },
            ct
        );

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
    )
    {
        var (app, flag, missing) = await FindAsync(slug, key, ct);
        if (missing is not null)
            return missing;
        var env = await envRepo.GetBySlugAsync(app.Id, request.Environment, ct);
        if (env is null)
            return Problem(ProblemMessages.EnvironmentNotFound, statusCode: 404);

        var state = flag.Environments.FirstOrDefault(s => s.EnvironmentId == env.Id)?.ToState();
        var result = FlagEvaluator.Evaluate(
            flag.ToDefinition(),
            state,
            FeatureFlagMapper.ToContext(request.Context)
        );
        var value = flag.Variants.First(v => v.Key == result.Variant).Value;
        return Ok(
            new FlagEvaluationResponse(
                result.Variant,
                FeatureFlagMapper.ToJson(value),
                result.Reason,
                result.MatchedRuleIndex,
                result.Bucket
            )
        );
    }

    /// <summary>Every change to the flag, newest first, with before/after JSON. Survives delete.</summary>
    [HttpGet("{key}/history")]
    [ProducesResponseType<List<FeatureFlagHistoryEntryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> History(string slug, string key, CancellationToken ct)
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return Problem(ProblemMessages.ApplicationNotFound, statusCode: 404);
        var entries = await flags.HistoryAsync(app.Id, key, ct);
        if (entries.Count == 0)
            return Problem(ProblemMessages.FeatureFlagNotFound, statusCode: 404);
        return Ok(entries.Select(FeatureFlagMapper.ToResponse).ToList());
    }

    // ---- helpers ----

    private async Task<(Application App, FeatureFlag Flag, IActionResult? Missing)> FindAsync(
        string slug,
        string key,
        CancellationToken ct
    )
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return (null!, null!, Problem(ProblemMessages.ApplicationNotFound, statusCode: 404));
        var flag = await flags.GetAsync(app.Id, key, ct);
        return flag is null
            ? (app, null!, Problem(ProblemMessages.FeatureFlagNotFound, statusCode: 404))
            : (app, flag, null);
    }

    /// <summary>
    /// Loads the flag under the write lock, applies <paramref name="change"/> (which returns an
    /// error result or the audit entry to save) and returns the updated flag.
    /// </summary>
    private async Task<IActionResult> MutateAsync(
        string slug,
        string key,
        Func<Application, FeatureFlag, object> change,
        CancellationToken ct
    )
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return Problem(ProblemMessages.ApplicationNotFound, statusCode: 404);

        var result = await flags.InWriteTransactionAsync<object?>(
            async token =>
            {
                var flag = await flags.GetAsync(app.Id, key, token);
                if (flag is null)
                    return null;
                var outcome = change(app, flag);
                if (outcome is not FeatureFlagAuditEntry audit)
                    return outcome;
                await flags.SaveAsync(flag, audit, token);
                return flag;
            },
            ct
        );

        return result switch
        {
            null => Problem(ProblemMessages.FeatureFlagNotFound, statusCode: 404),
            FeatureFlag flag => Ok(
                FeatureFlagMapper.ToResponse(flag, await envRepo.GetByApplicationAsync(app.Id, ct))
            ),
            IActionResult error => error,
            _ => throw new InvalidOperationException("Unexpected mutation result."),
        };
    }

    /// <summary>
    /// Like <see cref="MutateAsync"/> for one environment's state (created on first change).
    /// <paramref name="change"/> returns an error result, or <c>null</c> to save.
    /// </summary>
    private async Task<IActionResult> MutateEnvironmentAsync(
        string slug,
        string key,
        string environmentSlug,
        string changeKind,
        Func<FeatureFlag, FeatureFlagEnvironmentState, IActionResult?> change,
        CancellationToken ct
    )
    {
        var app = await appRepo.GetBySlugAsync(slug, ct);
        if (app is null)
            return Problem(ProblemMessages.ApplicationNotFound, statusCode: 404);
        var env = await envRepo.GetBySlugAsync(app.Id, environmentSlug, ct);
        if (env is null)
            return Problem(ProblemMessages.EnvironmentNotFound, statusCode: 404);

        return await MutateAsync(
            slug,
            key,
            (_, flag) =>
            {
                var state = flag.Environments.FirstOrDefault(s => s.EnvironmentId == env.Id);
                var before = FeatureFlagMapper.Snapshot(state);
                if (state is null)
                {
                    // No Id: EF generates it, so the new row is inserted (a preset key on a
                    // child of a tracked flag would be taken for an existing row).
                    state = new FeatureFlagEnvironmentState
                    {
                        FeatureFlagId = flag.Id,
                        EnvironmentId = env.Id,
                    };
                    flag.Environments.Add(state);
                }

                if (change(flag, state) is { } error)
                {
                    flag.Environments.Remove(state);
                    return error;
                }

                state.Version++;
                state.UpdatedAt = time.GetUtcNow();
                return Change(
                    app,
                    flag,
                    env.Slug,
                    changeKind,
                    before,
                    FeatureFlagMapper.Snapshot(state)
                );
            },
            ct
        );
    }

    private FeatureFlagAuditEntry Change(
        Application app,
        FeatureFlag flag,
        string? environment,
        string change,
        string? before,
        string? after
    ) => Audit(app, flag.Key, environment, change, before, after);

    private FeatureFlagAuditEntry Audit(
        Application app,
        string key,
        string? environment,
        string change,
        string? before,
        string? after
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            FlagKey = key,
            Environment = environment,
            Change = change,
            Actor = Actor,
            ChangedAt = time.GetUtcNow(),
            Before = before,
            After = after,
        };

    private static IEnumerable<string> UsedVariants(FeatureFlagEnvironmentState state)
    {
        IEnumerable<string?> serve(FlagServe? s) =>
            s is null ? [] : [s.Variant, .. (s.Rollout ?? []).Select(r => r.Variant)];
        return new[] { state.OffVariant }
            .Concat(serve(state.Fallthrough))
            .Concat(state.Rules.SelectMany(r => serve(r.Serve)))
            .OfType<string>();
    }

    private static List<string> Tags(List<string>? tags) =>
        (tags ?? [])
            .Select(t => t.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    private IActionResult UnknownVariant(string property, string variant) =>
        Invalid(property, $"Unknown variant '{variant}'.");

    private IActionResult Invalid(string property, string message)
    {
        ModelState.AddModelError(property, message);
        return ValidationProblem(ModelState);
    }
}
