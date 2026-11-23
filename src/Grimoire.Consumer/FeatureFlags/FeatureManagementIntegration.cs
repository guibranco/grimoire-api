using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.FeatureFilters;

namespace Grimoire.Consumer.FeatureFlags;

/// <summary>
/// Supplies the <see cref="FlagContext"/> for checks that don't pass one, such as
/// <c>[FeatureGate]</c>, <c>&lt;feature&gt;</c> tag helpers and <c>IFeatureManager.IsEnabledAsync(name)</c>.
/// Register one (e.g. built from the current user) to target users in those checks.
/// </summary>
public interface IFlagContextAccessor
{
    ValueTask<FlagContext> GetContextAsync();
}

/// <summary>
/// Exposes Grimoire flags as <c>Microsoft.FeatureManagement</c> features: every flag is a feature
/// with the same name, enabled when it serves the JSON value <c>true</c> (see
/// <see cref="GrimoireFeatureFilter"/>).
/// </summary>
public sealed class GrimoireFeatureDefinitionProvider(IGrimoireFeatureFlagClient client)
    : IFeatureDefinitionProvider
{
    public async Task<FeatureDefinition?> GetFeatureDefinitionAsync(string featureName) =>
        (await client.GetFlagKeysAsync()).Contains(featureName, StringComparer.Ordinal)
            ? Define(featureName)
            : null;

    public async IAsyncEnumerable<FeatureDefinition> GetAllFeatureDefinitionsAsync()
    {
        foreach (var key in await client.GetFlagKeysAsync())
            yield return Define(key);
    }

    private static FeatureDefinition Define(string name) =>
        new()
        {
            Name = name,
            RequirementType = RequirementType.Any,
            EnabledFor = [new FeatureFilterConfiguration { Name = GrimoireFeatureFilter.Alias }],
        };
}

/// <summary>
/// Evaluates a Grimoire flag for <c>Microsoft.FeatureManagement</c> checks without a context
/// (<c>IsEnabledAsync(name)</c>, <c>[FeatureGate]</c>, tag helpers). The context comes from a
/// registered <see cref="IFlagContextAccessor"/>, else a registered
/// <see cref="ITargetingContextAccessor"/>, else it is empty.
/// </summary>
[FilterAlias(Alias)]
public sealed class GrimoireFeatureFilter(
    IGrimoireFeatureFlagClient client,
    IFlagContextAccessor? contextAccessor = null,
    ITargetingContextAccessor? targetingAccessor = null
) : IFeatureFilter
{
    public const string Alias = "Grimoire";

    public async Task<bool> EvaluateAsync(FeatureFilterEvaluationContext context)
    {
        FlagContext flagContext;
        if (contextAccessor is not null)
            flagContext = await contextAccessor.GetContextAsync();
        else if (targetingAccessor is not null)
            flagContext = FromTargeting(await targetingAccessor.GetContextAsync());
        else
            flagContext = FlagContext.Empty;
        return await client.IsEnabledAsync(
            context.FeatureName,
            flagContext,
            context.CancellationToken
        );
    }

    /// <summary>
    /// A <see cref="FlagContext"/> as is; an <see cref="ITargetingContext"/> with <c>UserId</c> as
    /// <c>userId</c> and <c>Groups</c> as <c>groups</c> (comma-separated); anything else is empty.
    /// </summary>
    public static FlagContext ToFlagContext(object? appContext) =>
        appContext switch
        {
            FlagContext flagContext => flagContext,
            ITargetingContext targeting => FromTargeting(targeting),
            _ => FlagContext.Empty,
        };

    /// <summary><c>UserId</c> → <c>userId</c>; <c>Groups</c> → <c>groups</c> (comma-separated).</summary>
    public static FlagContext FromTargeting(ITargetingContext? targeting)
    {
        var context = new FlagContext(targeting?.UserId);
        if (targeting?.Groups?.Any() == true)
            context.With("groups", string.Join(',', targeting.Groups));
        return context;
    }
}

/// <summary>
/// Evaluates a Grimoire flag for <c>IsEnabledAsync(name, context)</c>: a <see cref="FlagContext"/>,
/// or an <see cref="ITargetingContext"/> (see <see cref="GrimoireFeatureFilter.ToFlagContext"/>).
/// (Microsoft.FeatureManagement allows one filter interface per class, hence two filters.)
/// </summary>
[FilterAlias(GrimoireFeatureFilter.Alias)]
public sealed class GrimoireContextualFeatureFilter(IGrimoireFeatureFlagClient client)
    : IContextualFeatureFilter<object>
{
    public Task<bool> EvaluateAsync(
        FeatureFilterEvaluationContext featureFilterContext,
        object appContext
    ) =>
        client.IsEnabledAsync(
            featureFilterContext.FeatureName,
            GrimoireFeatureFilter.ToFlagContext(appContext),
            featureFilterContext.CancellationToken
        );
}

public static class GrimoireFeatureFlagServiceCollectionExtensions
{
    /// <summary>Registers a singleton <see cref="IGrimoireFeatureFlagClient"/>.</summary>
    public static IServiceCollection AddGrimoireFeatureFlags(
        this IServiceCollection services,
        Action<GrimoireFeatureFlagOptions> configure
    )
    {
        var options = new GrimoireFeatureFlagOptions();
        configure(options);
        options.Validate();
        services.TryAddSingleton<IGrimoireFeatureFlagClient>(_ => new GrimoireFeatureFlagClient(
            options
        ));
        return services;
    }

    /// <summary>
    /// Feature management backed by Grimoire: registers the client, the definition provider and
    /// the Grimoire filters, so <c>IFeatureManager</c>, <c>[FeatureGate]</c> and
    /// <c>&lt;feature&gt;</c> tag helpers use Grimoire flags.
    /// </summary>
    public static IFeatureManagementBuilder AddGrimoireFeatureManagement(
        this IServiceCollection services,
        Action<GrimoireFeatureFlagOptions> configure
    )
    {
        services.AddGrimoireFeatureFlags(configure);
        return services.AddGrimoireFeatureManagementCore();
    }

    /// <summary>Like <see cref="AddGrimoireFeatureManagement"/> with an already registered client.</summary>
    public static IFeatureManagementBuilder AddGrimoireFeatureManagementCore(
        this IServiceCollection services
    )
    {
        services.TryAddSingleton<IFeatureDefinitionProvider, GrimoireFeatureDefinitionProvider>();
        return services
            .AddFeatureManagement()
            .AddFeatureFilter<GrimoireFeatureFilter>()
            .AddFeatureFilter<GrimoireContextualFeatureFilter>();
    }
}
