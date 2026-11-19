namespace Grimoire.Core.Dashboard;

/// <summary>
/// Turns a value-free <see cref="DashboardSnapshot"/> into a <see cref="DashboardSummary"/>:
/// counts, last-changed/last-seen timestamps and attention items. Pure, so the rules can be
/// unit tested without a database.
/// </summary>
public static class DashboardBuilder
{
    public static DashboardSummary Build(
        DashboardSnapshot snapshot,
        DateTimeOffset now,
        int expiringWithinDays,
        int staleAfterDays
    )
    {
        var expiringBefore = now.AddDays(expiringWithinDays);
        var staleBefore = now.AddDays(-staleAfterDays);
        var attention = new List<AttentionItem>();

        var envsByApp = snapshot.Environments.ToLookup(e => e.ApplicationId);
        var secretsByApp = snapshot.Secrets.ToLookup(s => s.ApplicationId);
        var configsByApp = snapshot.Configurations.ToLookup(c => c.ApplicationId);
        var versionsBySecretEnv = snapshot.SecretVersions.ToLookup(v =>
            (v.SecretId, v.EnvironmentId)
        );
        var versionsByEnv = snapshot.SecretVersions.ToLookup(v => v.EnvironmentId);
        var secretsWithVersions = snapshot.SecretVersions.Select(v => v.SecretId).ToHashSet();

        var applications = new List<DashboardApplication>();
        foreach (
            var app in snapshot.Applications.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
        )
        {
            var appSecrets = secretsByApp[app.Id].ToList();
            var appConfigs = configsByApp[app.Id].ToList();
            var configsByEnv = appConfigs.ToLookup(c => c.EnvironmentId);

            var environments = new List<DashboardEnvironment>();
            foreach (
                var env in envsByApp[app.Id].OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            )
            {
                var envVersions = versionsByEnv[env.Id].ToList();
                var envConfigs = configsByEnv[env.Id].ToList();
                var secretCount = envVersions.Select(v => v.SecretId).Distinct().Count();
                var lastChanged = Max(
                    env.UpdatedAt,
                    envVersions.Select(v => v.CreatedAt).Concat(envConfigs.Select(c => c.UpdatedAt))
                );

                DateTimeOffset? lastSeen = null;
                if (snapshot.LastSeen is { } lastSeenMap)
                {
                    if (lastSeenMap.TryGetValue((app.Id, env.Id), out var seen))
                    {
                        lastSeen = seen;
                        if (seen < staleBefore)
                            attention.Add(
                                new AttentionItem(
                                    AttentionKinds.ConsumerStale,
                                    AttentionSeverity.Warning,
                                    app.Slug,
                                    env.Slug,
                                    null,
                                    LastSeenAt: seen
                                )
                            );
                    }
                    else if (secretCount + envConfigs.Count > 0)
                    {
                        attention.Add(
                            new AttentionItem(
                                AttentionKinds.ConsumerNeverSeen,
                                AttentionSeverity.Info,
                                app.Slug,
                                env.Slug,
                                null
                            )
                        );
                    }
                }

                // Only environments the secret has been given a value in are evaluated: every
                // application gets a "local" environment that often never receives values.
                foreach (var secret in appSecrets)
                {
                    var versions = versionsBySecretEnv[(secret.Id, env.Id)]
                        .Select(v => new VersionInfo(
                            v.IsEnabled,
                            v.ExpiresAt,
                            v.NotBefore,
                            v.Version
                        ))
                        .ToList();
                    if (versions.Count == 0)
                        continue;

                    if (EvaluateSecret(versions, now, expiringBefore) is { } found)
                        attention.Add(
                            new AttentionItem(
                                found.Kind,
                                found.Severity,
                                app.Slug,
                                env.Slug,
                                secret.Name,
                                found.DueAt
                            )
                        );
                }

                attention.AddRange(
                    envConfigs
                        .Where(c => c.IsBlank)
                        .Select(c => new AttentionItem(
                            AttentionKinds.ConfigEmptyValue,
                            AttentionSeverity.Info,
                            app.Slug,
                            env.Slug,
                            c.Key
                        ))
                );

                environments.Add(
                    new DashboardEnvironment(
                        env.Slug,
                        env.Name,
                        secretCount,
                        envConfigs.Count,
                        lastChanged,
                        lastSeen
                    )
                );
            }

            attention.AddRange(
                appSecrets
                    .Where(secret => !secretsWithVersions.Contains(secret.Id))
                    .Select(secret => new AttentionItem(
                        AttentionKinds.SecretNoValidVersion,
                        AttentionSeverity.Error,
                        app.Slug,
                        null,
                        secret.Name
                    ))
            );

            applications.Add(
                new DashboardApplication(
                    app.Slug,
                    app.Name,
                    app.InheritGlobals,
                    environments,
                    appSecrets.Count,
                    appConfigs.Count,
                    Max(
                        app.UpdatedAt,
                        environments
                            .Select(e => e.LastChangedAt)
                            .Concat(appSecrets.Select(s => s.UpdatedAt))
                    ),
                    environments.Select(e => e.LastSeenAt).Max()
                )
            );
        }

        var global = BuildGlobal(snapshot, now, expiringBefore, attention);

        var totals = new DashboardTotals(
            applications.Count,
            applications.Sum(a => a.Environments.Count),
            applications.Sum(a => a.SecretCount),
            applications.Sum(a => a.ConfigurationCount)
        );

        return new DashboardSummary(now, totals, applications, global, Order(attention));
    }

    private static DashboardGlobal BuildGlobal(
        DashboardSnapshot snapshot,
        DateTimeOffset now,
        DateTimeOffset expiringBefore,
        List<AttentionItem> attention
    )
    {
        var versionsBySecret = snapshot.GlobalSecretVersions.ToLookup(v => v.GlobalSecretId);

        foreach (var secret in snapshot.GlobalSecrets)
        {
            var bySlug = versionsBySecret[secret.Id]
                .GroupBy(v => v.EnvironmentSlug, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g =>
                        g.Select(v => new VersionInfo(
                                v.IsEnabled,
                                v.ExpiresAt,
                                v.NotBefore,
                                v.Version
                            ))
                            .ToList(),
                    StringComparer.Ordinal
                );

            if (bySlug.Count == 0)
            {
                attention.Add(
                    new AttentionItem(
                        AttentionKinds.SecretNoValidVersion,
                        AttentionSeverity.Error,
                        null,
                        null,
                        secret.Name
                    )
                );
                continue;
            }

            bySlug.TryGetValue(AllEnvironmentsSlug, out var wildcard);
            var wildcardActive = wildcard is null ? null : Active(wildcard, now);

            foreach (var (slug, versions) in bySlug)
            {
                // Consumers of a specific environment fall back to the "*" value, so a slug
                // without its own active version is only a problem when "*" has none either.
                // Problems with the "*" value itself are reported under "*".
                if (
                    slug != AllEnvironmentsSlug
                    && Active(versions, now) is null
                    && wildcardActive is not null
                )
                    continue;

                if (EvaluateSecret(versions, now, expiringBefore) is { } found)
                    attention.Add(
                        new AttentionItem(
                            found.Kind,
                            found.Severity,
                            null,
                            slug,
                            secret.Name,
                            found.DueAt
                        )
                    );
            }
        }

        attention.AddRange(
            snapshot
                .GlobalConfigurations.Where(c => c.IsBlank)
                .Select(c => new AttentionItem(
                    AttentionKinds.ConfigEmptyValue,
                    AttentionSeverity.Info,
                    null,
                    c.EnvironmentSlug,
                    c.Key
                ))
        );

        var changes = snapshot
            .GlobalSecrets.Select(s => s.UpdatedAt)
            .Concat(snapshot.GlobalSecretVersions.Select(v => v.CreatedAt))
            .Concat(snapshot.GlobalConfigurations.Select(c => c.UpdatedAt))
            .ToList();

        return new DashboardGlobal(
            snapshot.GlobalSecrets.Count,
            snapshot.GlobalConfigurations.Count,
            changes.Count == 0 ? null : changes.Max()
        );
    }

    private const string AllEnvironmentsSlug = Entities.GlobalConfigurationEntry.AllEnvironments;

    private readonly record struct VersionInfo(
        bool IsEnabled,
        DateTimeOffset? ExpiresAt,
        DateTimeOffset? NotBefore,
        int Version
    );

    private readonly record struct SecretFinding(
        string Kind,
        AttentionSeverity Severity,
        DateTimeOffset? DueAt
    );

    /// <summary>Same rule as the repositories' <c>GetActiveVersionAsync</c>.</summary>
    private static VersionInfo? Active(IEnumerable<VersionInfo> versions, DateTimeOffset now) =>
        versions
            .Where(v =>
                v.IsEnabled
                && (v.NotBefore == null || v.NotBefore <= now)
                && (v.ExpiresAt == null || v.ExpiresAt >= now)
            )
            .OrderByDescending(v => v.Version)
            .Cast<VersionInfo?>()
            .FirstOrDefault();

    private static SecretFinding? EvaluateSecret(
        IReadOnlyCollection<VersionInfo> versions,
        DateTimeOffset now,
        DateTimeOffset expiringBefore
    )
    {
        if (Active(versions, now) is { } active)
        {
            return active.ExpiresAt is { } expiresAt && expiresAt <= expiringBefore
                ? new SecretFinding(
                    AttentionKinds.SecretExpiring,
                    AttentionSeverity.Warning,
                    expiresAt
                )
                : null;
        }

        var latest = versions
            .Where(v => v.IsEnabled)
            .OrderByDescending(v => v.Version)
            .Cast<VersionInfo?>()
            .FirstOrDefault();

        return latest?.ExpiresAt is { } expiredAt && expiredAt <= now
            ? new SecretFinding(AttentionKinds.SecretExpired, AttentionSeverity.Error, expiredAt)
            : new SecretFinding(AttentionKinds.SecretNoValidVersion, AttentionSeverity.Error, null);
    }

    private static DateTimeOffset Max(DateTimeOffset seed, IEnumerable<DateTimeOffset> values) =>
        values.Aggregate(seed, (max, v) => v > max ? v : max);

    private static List<AttentionItem> Order(List<AttentionItem> items) =>
        items
            .OrderBy(i => i.Severity)
            .ThenBy(i => i.Application is null)
            .ThenBy(i => i.Application, StringComparer.Ordinal)
            .ThenBy(i => i.Environment, StringComparer.Ordinal)
            .ThenBy(i => i.Target, StringComparer.Ordinal)
            .ThenBy(i => i.Kind, StringComparer.Ordinal)
            .ToList();
}
