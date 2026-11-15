using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Interfaces.Repositories;

namespace Grimoire.Core.Services;

public class EffectiveConfigurationService(
    IConfigurationRepository configurations,
    IGlobalConfigurationRepository globalConfigurations,
    ISecretRepository secrets,
    IGlobalSecretRepository globalSecrets
) : IEffectiveConfigurationService
{
    public async Task<IReadOnlyList<EffectiveConfigurationEntry>> GetConfigurationAsync(
        Application application,
        AppEnvironment environment,
        CancellationToken ct = default
    )
    {
        var appEntries = await configurations.GetByEnvironmentAsync(
            application.Id,
            environment.Id,
            ct
        );
        var globalEntries = application.InheritGlobals
            ? await globalConfigurations.GetApplicableAsync(environment.Slug, ct)
            : [];

        return ConfigurationPrecedence.Merge(
            appEntries,
            globalEntries,
            environment.Slug,
            application.InheritGlobals
        );
    }

    public async Task<EffectiveConfigurationEntry?> GetConfigurationValueAsync(
        Application application,
        AppEnvironment environment,
        string key,
        CancellationToken ct = default
    )
    {
        // Resolve through the same merge as GetConfigurationAsync so a single-key read can
        // never disagree with the full list.
        var entries = await GetConfigurationAsync(application, environment, ct);
        return entries.FirstOrDefault(e => e.Key == key);
    }

    public async Task<IReadOnlyList<EffectiveSecret>> GetSecretsAsync(
        Application application,
        CancellationToken ct = default
    )
    {
        var appSecrets = await secrets.GetByApplicationAsync(application.Id, ct);
        var globals = application.InheritGlobals ? await globalSecrets.GetAllAsync(ct) : [];
        var globalNames = globals.Select(g => g.Name).ToHashSet(StringComparer.Ordinal);
        var appNames = appSecrets.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

        return appSecrets
            .Select(s => new EffectiveSecret(
                s.Name,
                s.Description,
                ValueSource.Application,
                globalNames.Contains(s.Name)
            ))
            .Concat(
                globals
                    .Where(g => !appNames.Contains(g.Name))
                    .Select(g => new EffectiveSecret(
                        g.Name,
                        g.Description,
                        ValueSource.Global,
                        false
                    ))
            )
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<SecretResolution> ResolveSecretAsync(
        Application application,
        AppEnvironment environment,
        string name,
        DateTimeOffset now,
        CancellationToken ct = default
    )
    {
        var appSecret = await secrets.GetByNameAsync(application.Id, name, ct);
        if (appSecret is not null)
        {
            // The application owns this name: never fall back to a global, even when it has
            // no active version (an explicit disable must not be silently replaced).
            var version = await secrets.GetActiveVersionAsync(
                appSecret.Id,
                environment.Id,
                now,
                ct
            );
            return version is null
                ? SecretResolution.NoActiveVersion
                : Resolved(
                    appSecret.Name,
                    version.EncryptedValue,
                    version.IsEnabled,
                    version.ExpiresAt,
                    version.NotBefore,
                    version.Version,
                    version.CreatedAt,
                    ValueSource.Application
                );
        }

        if (!application.InheritGlobals)
            return SecretResolution.NotFound;

        var globalSecret = await globalSecrets.GetByNameAsync(name, ct);
        if (globalSecret is null)
            return SecretResolution.NotFound;

        var globalVersion = await globalSecrets.GetActiveVersionAsync(
            globalSecret.Id,
            environment.Slug,
            now,
            ct
        );
        return globalVersion is null
            ? SecretResolution.NoActiveVersion
            : Resolved(
                globalSecret.Name,
                globalVersion.EncryptedValue,
                globalVersion.IsEnabled,
                globalVersion.ExpiresAt,
                globalVersion.NotBefore,
                globalVersion.Version,
                globalVersion.CreatedAt,
                ValueSource.Global
            );
    }

    private static SecretResolution Resolved(
        string name,
        string encryptedValue,
        bool isEnabled,
        DateTimeOffset? expiresAt,
        DateTimeOffset? notBefore,
        int version,
        DateTimeOffset createdAt,
        ValueSource source
    ) =>
        new(
            SecretResolutionStatus.Resolved,
            new ResolvedSecretVersion(
                name,
                encryptedValue,
                isEnabled,
                expiresAt,
                notBefore,
                version,
                createdAt,
                source
            )
        );
}
