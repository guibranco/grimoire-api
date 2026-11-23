using Grimoire.Core.Secrets;

namespace Grimoire.Core.Dashboard;

/// <summary>Value-free secret metadata: what the expiring-secrets list and reminders work from.</summary>
public sealed record SecretMetadataSnapshot(
    IReadOnlyList<ApplicationRow> Applications,
    IReadOnlyList<EnvironmentRow> Environments,
    IReadOnlyList<SecretRow> Secrets,
    IReadOnlyList<SecretVersionRow> SecretVersions,
    IReadOnlyList<GlobalSecretRow> GlobalSecrets,
    IReadOnlyList<GlobalSecretVersionRow> GlobalSecretVersions
);

/// <summary>A secret, in one environment, that is due for rotation or has expired.</summary>
/// <param name="Application">Application slug; <c>null</c> for a global secret.</param>
/// <param name="Environment">Environment slug (<c>*</c> for a global value targeting every environment).</param>
/// <param name="Reason">One of <see cref="SecretDueReasons"/>.</param>
public sealed record ExpiringSecret(
    Guid SecretId,
    string? Application,
    string? ApplicationName,
    string Secret,
    string Environment,
    int Version,
    string Reason,
    DateTimeOffset DueAt,
    int? RotationIntervalDays
)
{
    public bool IsGlobal => Application is null;
}

public static class ExpiringSecretsBuilder
{
    /// <summary>
    /// Every (secret, environment) whose due date is on or before <paramref name="dueBefore"/>,
    /// soonest first. Overdue and expired entries are always included. Application secrets are
    /// only evaluated in environments they have a value in; a global value for a specific
    /// environment is skipped when it has no valid version but the <c>*</c> value covers it,
    /// matching what consumers get.
    /// </summary>
    public static List<ExpiringSecret> Build(
        SecretMetadataSnapshot snapshot,
        DateTimeOffset now,
        DateTimeOffset dueBefore
    )
    {
        var items = new List<ExpiringSecret>();
        AddApplicationSecrets(snapshot, now, dueBefore, items);
        AddGlobalSecrets(snapshot, now, dueBefore, items);

        return items
            .OrderBy(i => i.DueAt)
            .ThenBy(i => i.Application is null)
            .ThenBy(i => i.Application, StringComparer.Ordinal)
            .ThenBy(i => i.Secret, StringComparer.Ordinal)
            .ThenBy(i => i.Environment, StringComparer.Ordinal)
            .ToList();
    }

    private static void AddApplicationSecrets(
        SecretMetadataSnapshot snapshot,
        DateTimeOffset now,
        DateTimeOffset dueBefore,
        List<ExpiringSecret> items
    )
    {
        var apps = snapshot.Applications.ToDictionary(a => a.Id);
        var envs = snapshot.Environments.ToDictionary(e => e.Id);
        var secrets = snapshot.Secrets.ToDictionary(s => s.Id);

        foreach (var group in snapshot.SecretVersions.GroupBy(v => (v.SecretId, v.EnvironmentId)))
        {
            if (
                !secrets.TryGetValue(group.Key.SecretId, out var secret)
                || !envs.TryGetValue(group.Key.EnvironmentId, out var env)
                || !apps.TryGetValue(secret.ApplicationId, out var app)
            )
                continue;

            if (Due(group.ToList(), secret.RotationIntervalDays, now, dueBefore) is { } due)
                items.Add(
                    new ExpiringSecret(
                        secret.Id,
                        app.Slug,
                        app.Name,
                        secret.Name,
                        env.Slug,
                        due.Version,
                        due.Reason,
                        due.DueAt,
                        secret.RotationIntervalDays
                    )
                );
        }
    }

    private static void AddGlobalSecrets(
        SecretMetadataSnapshot snapshot,
        DateTimeOffset now,
        DateTimeOffset dueBefore,
        List<ExpiringSecret> items
    )
    {
        var versionsBySecret = snapshot.GlobalSecretVersions.ToLookup(v => v.GlobalSecretId);

        foreach (var secret in snapshot.GlobalSecrets)
        {
            var bySlug = versionsBySecret[secret.Id]
                .GroupBy(v => v.EnvironmentSlug, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            var wildcardCovers =
                bySlug.TryGetValue(Entities.GlobalConfigurationEntry.AllEnvironments, out var all)
                && SecretVersionRules.Current(all, now) is not null;

            foreach (var (slug, versions) in bySlug)
            {
                if (
                    slug != Entities.GlobalConfigurationEntry.AllEnvironments
                    && wildcardCovers
                    && SecretVersionRules.Current(versions, now) is null
                )
                    continue;

                if (Due(versions, secret.RotationIntervalDays, now, dueBefore) is { } due)
                    items.Add(
                        new ExpiringSecret(
                            secret.Id,
                            null,
                            null,
                            secret.Name,
                            slug,
                            due.Version,
                            due.Reason,
                            due.DueAt,
                            secret.RotationIntervalDays
                        )
                    );
            }
        }
    }

    private static SecretDue? Due<T>(
        IReadOnlyCollection<T> versions,
        int? rotationIntervalDays,
        DateTimeOffset now,
        DateTimeOffset dueBefore
    )
        where T : class, ISecretVersionRecord =>
        SecretDueRules.Evaluate(versions, rotationIntervalDays, now) is { } due
        && due.DueAt <= dueBefore
            ? due
            : null;
}
