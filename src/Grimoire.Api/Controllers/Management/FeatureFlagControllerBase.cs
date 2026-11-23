using System.Security.Cryptography;
using Grimoire.Api.DTOs.Management;
using Grimoire.Api.FeatureFlags;
using Grimoire.Api.Validators.Management;
using Grimoire.Core.Entities;
using Grimoire.Core.FeatureFlags;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Management;

/// <summary>
/// Feature flag operations shared by application flags and global flags. Derived controllers
/// resolve the <see cref="FlagScope"/> (and carry the routes and audit actions); everything else
/// is the same in both scopes.
/// </summary>
public abstract class FeatureFlagControllerBase(IFeatureFlagRepository flags, TimeProvider time)
    : ControllerBase
{
    private const string Actor = "admin";

    protected IFeatureFlagRepository Flags => flags;

    protected async Task<IActionResult> ListCore(
        FlagScope scope,
        string? tag,
        string? environment,
        bool? enabled,
        CancellationToken ct
    )
    {
        FlagScopeEnvironment? env = null;
        if (environment is not null)
        {
            env = scope.Resolve(environment, GlobalEnvironmentSlug.IsValid);
            if (env is null)
                return Problem(ProblemMessages.EnvironmentNotFound, statusCode: 404);
        }

        var all = await flags.ListAsync(scope.ApplicationId, ct);
        return Ok(
            all.Where(f =>
                    (tag is null || f.Tags.Contains(tag, StringComparer.Ordinal))
                    && (
                        env is null
                        || enabled is null
                        || (FlagScope.StateOf(f, env)?.Enabled ?? false) == enabled
                    )
                )
                .Select(f => FeatureFlagMapper.ToResponse(f, scope))
                .ToList()
        );
    }

    /// <summary>Creates the flag; the caller returns <c>201</c> with its own route.</summary>
    protected async Task<(FeatureFlag? Created, IActionResult? Error)> CreateCore(
        FlagScope scope,
        CreateFeatureFlagRequest request,
        CancellationToken ct
    )
    {
        var type = request.Type ?? FlagTypes.Boolean;
        var variants =
            type == FlagTypes.Boolean
                ? FlagTypes.BooleanVariants.ToList()
                : request.Variants!.Select(FeatureFlagMapper.ToModel).ToList();
        var defaultVariant = request.DefaultVariant
            ?? (type == FlagTypes.Boolean ? FlagTypes.Off : variants[0].Key);
        if (!variants.Exists(v => v.Key == defaultVariant))
            return (null, UnknownVariant(nameof(request.DefaultVariant), defaultVariant));

        var now = time.GetUtcNow();
        var flag = new FeatureFlag
        {
            Id = Guid.NewGuid(),
            ApplicationId = scope.ApplicationId,
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
                if (await flags.GetAsync(scope.ApplicationId, request.Key, token) is not null)
                    return false;
                await flags.SaveAsync(
                    flag,
                    Audit(scope, flag.Key, null, "create", null, FeatureFlagMapper.Snapshot(flag)),
                    token
                );
                return true;
            },
            ct
        );
        return created
            ? (flag, null)
            : (null, Problem(ProblemMessages.FeatureFlagExists, statusCode: 409));
    }

    protected async Task<IActionResult> GetCore(FlagScope scope, string key, CancellationToken ct)
    {
        var flag = await flags.GetAsync(scope.ApplicationId, key, ct);
        return flag is null
            ? Problem(ProblemMessages.FeatureFlagNotFound, statusCode: 404)
            : Ok(FeatureFlagMapper.ToResponse(flag, scope));
    }

    protected Task<IActionResult> UpdateCore(
        FlagScope scope,
        string key,
        UpdateFeatureFlagRequest request,
        CancellationToken ct
    ) =>
        MutateAsync(
            scope,
            key,
            flag =>
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
                return Audit(
                    scope,
                    flag.Key,
                    null,
                    "update",
                    before,
                    FeatureFlagMapper.Snapshot(flag)
                );
            },
            ct
        );

    protected async Task<IActionResult> DeleteCore(
        FlagScope scope,
        string key,
        CancellationToken ct
    )
    {
        var deleted = await flags.InWriteTransactionAsync(
            async token =>
            {
                var flag = await flags.GetAsync(scope.ApplicationId, key, token);
                if (flag is null)
                    return false;
                await flags.DeleteAsync(
                    flag,
                    Audit(scope, flag.Key, null, "delete", FeatureFlagMapper.Snapshot(flag), null),
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

    protected Task<IActionResult> PutEnvironmentCore(
        FlagScope scope,
        string key,
        string environmentSlug,
        FlagEnvironmentStateRequest request,
        CancellationToken ct
    ) =>
        MutateEnvironmentAsync(
            scope,
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

    protected Task<IActionResult> ToggleCore(
        FlagScope scope,
        string key,
        string environmentSlug,
        ToggleFeatureFlagRequest? request,
        CancellationToken ct
    ) =>
        MutateEnvironmentAsync(
            scope,
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

    protected async Task<IActionResult> EvaluateCore(
        FlagScope scope,
        string key,
        EvaluateFeatureFlagRequest request,
        CancellationToken ct
    )
    {
        var flag = await flags.GetAsync(scope.ApplicationId, key, ct);
        if (flag is null)
            return Problem(ProblemMessages.FeatureFlagNotFound, statusCode: 404);
        var env = scope.Resolve(request.Environment, GlobalEnvironmentSlug.IsValid);
        if (env is null)
            return Problem(ProblemMessages.EnvironmentNotFound, statusCode: 404);

        var result = FlagEvaluator.Evaluate(
            flag.ToDefinition(),
            scope.EffectiveStateOf(flag, env)?.ToState(),
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

    protected async Task<IActionResult> HistoryCore(
        FlagScope scope,
        string key,
        CancellationToken ct
    )
    {
        var entries = await flags.HistoryAsync(scope.ApplicationId, key, ct);
        return entries.Count == 0
            ? Problem(ProblemMessages.FeatureFlagNotFound, statusCode: 404)
            : Ok(entries.Select(FeatureFlagMapper.ToResponse).ToList());
    }

    /// <summary>
    /// Loads the flag under the write lock, applies <paramref name="change"/> (which returns an
    /// error result or the audit entry to save) and returns the updated flag.
    /// </summary>
    private async Task<IActionResult> MutateAsync(
        FlagScope scope,
        string key,
        Func<FeatureFlag, object> change,
        CancellationToken ct
    )
    {
        var result = await flags.InWriteTransactionAsync<object?>(
            async token =>
            {
                var flag = await flags.GetAsync(scope.ApplicationId, key, token);
                if (flag is null)
                    return null;
                var outcome = change(flag);
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
                FeatureFlagMapper.ToResponse(flag, WithEnvironmentsOf(scope, flag))
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
        FlagScope scope,
        string key,
        string environmentSlug,
        string changeKind,
        Func<FeatureFlag, FeatureFlagEnvironmentState, IActionResult?> change,
        CancellationToken ct
    )
    {
        var env = scope.Resolve(environmentSlug, GlobalEnvironmentSlug.IsValid);
        if (env is null)
            return Problem(ProblemMessages.EnvironmentNotFound, statusCode: 404);

        return await MutateAsync(
            scope,
            key,
            flag =>
            {
                var state = FlagScope.StateOf(flag, env);
                var before = FeatureFlagMapper.Snapshot(state);
                if (state is null)
                {
                    // No Id: EF generates it, so the new row is inserted (a preset key on a
                    // child of a tracked flag would be taken for an existing row).
                    state = env.NewState(flag.Id);
                    flag.Environments.Add(state);
                }

                if (change(flag, state) is { } error)
                {
                    flag.Environments.Remove(state);
                    return error;
                }

                state.Version++;
                state.UpdatedAt = time.GetUtcNow();
                return Audit(
                    scope,
                    flag.Key,
                    env.Slug,
                    changeKind,
                    before,
                    FeatureFlagMapper.Snapshot(state)
                );
            },
            ct
        );
    }

    /// <summary>The scope plus any global slug the flag has state in but the scope doesn't list.</summary>
    private static FlagScope WithEnvironmentsOf(FlagScope scope, FeatureFlag flag) =>
        scope.IsGlobal ? FlagScope.Global(scope.Environments.Select(e => e.Slug), [flag]) : scope;

    private FeatureFlagAuditEntry Audit(
        FlagScope scope,
        string key,
        string? environment,
        string change,
        string? before,
        string? after
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            ApplicationId = scope.ApplicationId,
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
        static IEnumerable<string?> serve(FlagServe? s) =>
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

    private ActionResult Invalid(string property, string message)
    {
        ModelState.AddModelError(property, message);
        return ValidationProblem(ModelState);
    }
}
