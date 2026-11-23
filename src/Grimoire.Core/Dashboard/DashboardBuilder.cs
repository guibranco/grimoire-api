using Grimoire.Core.Secrets;

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
        var context = new BuildContext(
            snapshot,
            now,
            now.AddDays(expiringWithinDays),
            now.AddDays(-staleAfterDays)
        );

        var applications = snapshot
            .Applications.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(context.BuildApplication)
            .ToList();

        var global = BuildGlobal(snapshot, now, context.ExpiringBefore, context.Attention);

        var totals = new DashboardTotals(
            applications.Count,
            applications.Sum(a => a.Environments.Count),
            applications.Sum(a => a.SecretCount),
            applications.Sum(a => a.ConfigurationCount)
        );

        return new DashboardSummary(now, totals, applications, global, Order(context.Attention));
    }

    /// <summary>Lookups over the snapshot plus the attention items collected so far.</summary>
    private sealed class BuildContext(
        DashboardSnapshot snapshot,
        DateTimeOffset now,
        DateTimeOffset expiringBefore,
        DateTimeOffset staleBefore
    )
    {
        private readonly ILookup<Guid, EnvironmentRow> _envsByApp = snapshot.Environments.ToLookup(
            e => e.ApplicationId
        );
        private readonly ILookup<Guid, SecretRow> _secretsByApp = snapshot.Secrets.ToLookup(s =>
            s.ApplicationId
        );
        private readonly ILookup<Guid, ConfigurationRow> _configsByApp =
            snapshot.Configurations.ToLookup(c => c.ApplicationId);
        private readonly ILookup<(Guid, Guid), SecretVersionRow> _versionsBySecretEnv =
            snapshot.SecretVersions.ToLookup(v => (v.SecretId, v.EnvironmentId));
        private readonly ILookup<Guid, SecretVersionRow> _versionsByEnv =
            snapshot.SecretVersions.ToLookup(v => v.EnvironmentId);
        private readonly HashSet<Guid> _secretsWithVersions = snapshot
            .SecretVersions.Select(v => v.SecretId)
            .ToHashSet();

        public DateTimeOffset ExpiringBefore => expiringBefore;

        private static DateTimeOffset Max(
            DateTimeOffset seed,
            IEnumerable<DateTimeOffset> values
        ) => values.Aggregate(seed, (max, v) => v > max ? v : max);

        public List<AttentionItem> Attention { get; } = [];

        public DashboardApplication BuildApplication(ApplicationRow app)
        {
            var appSecrets = _secretsByApp[app.Id].ToList();
            var appConfigs = _configsByApp[app.Id].ToList();
            var configsByEnv = appConfigs.ToLookup(c => c.EnvironmentId);

            var environments = _envsByApp[app.Id]
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(env =>
                    BuildEnvironment(app, env, appSecrets, configsByEnv[env.Id].ToList())
                )
                .ToList();

            Attention.AddRange(
                appSecrets
                    .Where(secret => !_secretsWithVersions.Contains(secret.Id))
                    .Select(secret => new AttentionItem(
                        AttentionKinds.SecretNoValidVersion,
                        AttentionSeverity.Error,
                        app.Slug,
                        null,
                        secret.Name
                    ))
            );

            return new DashboardApplication(
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
            );
        }

        private DashboardEnvironment BuildEnvironment(
            ApplicationRow app,
            EnvironmentRow env,
            List<SecretRow> appSecrets,
            List<ConfigurationRow> envConfigs
        )
        {
            var envVersions = _versionsByEnv[env.Id].ToList();
            var secretCount = envVersions.Select(v => v.SecretId).Distinct().Count();
            var lastChanged = Max(
                env.UpdatedAt,
                envVersions.Select(v => v.CreatedAt).Concat(envConfigs.Select(c => c.UpdatedAt))
            );

            var lastSeen = AddConsumerAttention(app, env, secretCount + envConfigs.Count > 0);
            AddSecretAttention(app, env, appSecrets);
            Attention.AddRange(
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

            return new DashboardEnvironment(
                env.Slug,
                env.Name,
                secretCount,
                envConfigs.Count,
                lastChanged,
                lastSeen
            );
        }

        /// <returns>The environment's last-seen time, or <c>null</c> if never seen or not tracked.</returns>
        private DateTimeOffset? AddConsumerAttention(
            ApplicationRow app,
            EnvironmentRow env,
            bool hasValues
        )
        {
            if (snapshot.LastSeen is not { } lastSeenMap)
                return null;

            if (!lastSeenMap.TryGetValue((app.Id, env.Id), out var seen))
            {
                if (hasValues)
                    Attention.Add(
                        new AttentionItem(
                            AttentionKinds.ConsumerNeverSeen,
                            AttentionSeverity.Info,
                            app.Slug,
                            env.Slug,
                            null
                        )
                    );
                return null;
            }

            if (seen < staleBefore)
                Attention.Add(
                    new AttentionItem(
                        AttentionKinds.ConsumerStale,
                        AttentionSeverity.Warning,
                        app.Slug,
                        env.Slug,
                        null,
                        LastSeenAt: seen
                    )
                );
            return seen;
        }

        /// <summary>
        /// Only environments the secret has been given a value in are evaluated: every
        /// application gets a "local" environment that often never receives values.
        /// </summary>
        private void AddSecretAttention(
            ApplicationRow app,
            EnvironmentRow env,
            List<SecretRow> appSecrets
        )
        {
            foreach (var secret in appSecrets)
            {
                var versions = _versionsBySecretEnv[(secret.Id, env.Id)]
                    .Cast<ISecretVersionRecord>()
                    .ToList();

                if (
                    versions.Count > 0
                    && EvaluateSecret(versions, secret.RotationIntervalDays, now, expiringBefore)
                        is { } found
                )
                    Attention.Add(
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
        }
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
                    g => g.Cast<ISecretVersionRecord>().ToList(),
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
            var wildcardActive = wildcard is null
                ? null
                : SecretVersionRules.Current(wildcard, now);

            foreach (var (slug, versions) in bySlug)
            {
                // Consumers of a specific environment fall back to the "*" value, so a slug
                // without its own active version is only a problem when "*" has none either.
                // Problems with the "*" value itself are reported under "*".
                if (
                    slug != AllEnvironmentsSlug
                    && SecretVersionRules.Current(versions, now) is null
                    && wildcardActive is not null
                )
                    continue;

                if (
                    EvaluateSecret(versions, secret.RotationIntervalDays, now, expiringBefore) is
                    { } found
                )
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

    private readonly record struct SecretFinding(
        string Kind,
        AttentionSeverity Severity,
        DateTimeOffset? DueAt
    );

    /// <summary>
    /// Expiry and rotation come from <see cref="SecretDueRules"/>, so the dashboard, the
    /// expiring-secrets list and the reminders agree on what is due.
    /// </summary>
    private static SecretFinding? EvaluateSecret(
        IReadOnlyCollection<ISecretVersionRecord> versions,
        int? rotationIntervalDays,
        DateTimeOffset now,
        DateTimeOffset expiringBefore
    )
    {
        var due = SecretDueRules.Evaluate(versions, rotationIntervalDays, now);

        if (SecretVersionRules.Current(versions, now) is not null)
        {
            if (due is null || due.DueAt > expiringBefore)
                return null;
            var kind =
                due.Reason == SecretDueReasons.Rotation
                    ? AttentionKinds.SecretRotationDue
                    : AttentionKinds.SecretExpiring;
            return new SecretFinding(kind, AttentionSeverity.Warning, due.DueAt);
        }

        return due?.Reason == SecretDueReasons.Expired
            ? new SecretFinding(AttentionKinds.SecretExpired, AttentionSeverity.Error, due.DueAt)
            : new SecretFinding(AttentionKinds.SecretNoValidVersion, AttentionSeverity.Error, null);
    }

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
