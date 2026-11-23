using Grimoire.Core.FeatureFlags;

namespace Grimoire.Core.Entities;

/// <summary>
/// A feature flag of one application. Grimoire stores and serves definitions; consumers
/// evaluate them locally (see <c>docs/feature-flags/evaluation-spec.md</c>).
/// </summary>
public class FeatureFlag
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }

    /// <summary>Unique per application; same format rules as configuration keys.</summary>
    public string Key { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary><c>boolean</c> or <c>multivariate</c> (<see cref="FlagTypes"/>).</summary>
    public string Type { get; set; } = FlagTypes.Boolean;

    /// <summary>For boolean flags always <c>on</c>/<c>off</c>.</summary>
    public List<FlagVariant> Variants { get; set; } = [];
    public string DefaultVariant { get; set; } = FlagTypes.Off;
    public List<string> Tags { get; set; } = [];

    /// <summary>Random per flag, mixed into bucketing so flags roll out independently.</summary>
    public string Salt { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Application Application { get; set; } = null!;
    public ICollection<FeatureFlagEnvironmentState> Environments { get; set; } = [];

    public FlagDefinition ToDefinition() => new(Key, Salt, Variants, DefaultVariant);
}

/// <summary>A flag's state in one environment. No row means disabled, serving the default variant.</summary>
public class FeatureFlagEnvironmentState
{
    public Guid Id { get; set; }
    public Guid FeatureFlagId { get; set; }
    public Guid EnvironmentId { get; set; }
    public bool Enabled { get; set; }
    public string? OffVariant { get; set; }
    public List<TargetingRule> Rules { get; set; } = [];
    public FlagServe? Fallthrough { get; set; }

    /// <summary>Incremented on every change; used for caching and ETags.</summary>
    public int Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public FeatureFlag FeatureFlag { get; set; } = null!;
    public AppEnvironment Environment { get; set; } = null!;

    public FlagEnvironmentState ToState() => new(Enabled, OffVariant, Rules, Fallthrough, Version);
}

/// <summary>
/// One change to a flag: who, when, which environment, and the flag (or environment state)
/// before and after, as JSON. Kept after the flag is deleted.
/// </summary>
public class FeatureFlagAuditEntry
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string FlagKey { get; set; } = string.Empty;

    /// <summary>Environment slug for environment changes; <c>null</c> for the flag itself.</summary>
    public string? Environment { get; set; }

    /// <summary><c>create</c>, <c>update</c>, <c>environment</c>, <c>toggle</c> or <c>delete</c>.</summary>
    public string Change { get; set; } = string.Empty;

    /// <summary><c>admin</c> for now (per-user once RBAC lands).</summary>
    public string Actor { get; set; } = string.Empty;
    public DateTimeOffset ChangedAt { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
}
