using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Services;

namespace Grimoire.Api.Configuration;

internal static class ConfigurationHistoryMapper
{
    public static ConfigurationRevisionResponse ToResponse(ConfigurationRevision r) =>
        new(
            r.Revision,
            r.Change,
            r.Value,
            ConfigurationValueNormalizer.ToName(r.ValueType),
            r.Description,
            r.RolledBackFrom,
            r.ChangedBy,
            r.ChangedAt
        );

    public static ConfigurationDiffItem ToResponse(ConfigurationDiffEntry d) =>
        new(d.Key, d.Status, Side(d.From), Side(d.To));

    private static ConfigurationDiffSide? Side(EffectiveConfigurationEntry? e) =>
        e is null
            ? null
            : new(
                e.Value,
                ConfigurationValueNormalizer.ToName(e.ValueType),
                ValueSourceNames.ToName(e.Source),
                e.GlobalEnvironmentSlug
            );
}
