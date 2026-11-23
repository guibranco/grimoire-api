namespace Grimoire.Core.Entities;

public enum ConfigurationScope
{
    Application = 0,
    Global = 1,
}

/// <summary>What a configuration revision records.</summary>
public static class ConfigurationChanges
{
    /// <summary>The value the entry had when history started (written by the migration).</summary>
    public const string Baseline = "baseline";
    public const string Create = "create";
    public const string Update = "update";
    public const string Delete = "delete";

    /// <summary>An earlier revision's value restored (see <see cref="ConfigurationRevision.RolledBackFrom"/>).</summary>
    public const string Rollback = "rollback";
}

/// <summary>
/// One change to a configuration key in one environment, appended in the same transaction as the
/// change. Revisions are numbered per key, so history survives deleting and re-creating a key.
/// </summary>
public class ConfigurationRevision
{
    public Guid Id { get; set; }
    public ConfigurationScope Scope { get; set; }

    /// <summary>Owning application; <c>null</c> for global configuration.</summary>
    public Guid? ApplicationId { get; set; }

    /// <summary>The environment's id (application) or slug, <c>*</c> included (global).</summary>
    public string EnvironmentKey { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    /// <summary>1, 2, 3… per (scope, environment, key).</summary>
    public int Revision { get; set; }

    /// <summary>One of <see cref="ConfigurationChanges"/>.</summary>
    public string Change { get; set; } = ConfigurationChanges.Update;

    /// <summary>The value after the change; <c>null</c> when the change deleted the key.</summary>
    public string? Value { get; set; }

    public ConfigurationValueType ValueType { get; set; }
    public string? Description { get; set; }

    /// <summary>For <see cref="ConfigurationChanges.Rollback"/>: the revision whose value was restored.</summary>
    public int? RolledBackFrom { get; set; }

    public string ChangedBy { get; set; } = string.Empty;
    public DateTimeOffset ChangedAt { get; set; }

    public static string EnvironmentKeyFor(Guid environmentId) => environmentId.ToString("N");
}
