namespace Grimoire.Core.Reports;

/// <summary>A secret as the usage reports see it: identity and age, never a value.</summary>
/// <param name="ApplicationSlug"><c>null</c> for a global secret.</param>
public sealed record SecretUsageSubject(
    Guid Id,
    bool IsGlobal,
    string? ApplicationSlug,
    string? ApplicationName,
    string Name,
    DateTimeOffset CreatedAt
);

/// <summary>Reads of one secret by one application environment.</summary>
public sealed record SecretReadSummary(
    Guid SecretId,
    bool IsGlobal,
    string ApplicationSlug,
    string Environment,
    DateTimeOffset LastReadAt,
    long ReadCount
);

/// <summary>A secret with no consumer read within the report window.</summary>
/// <param name="LastReadAt"><c>null</c> when it has never been read.</param>
public sealed record UnusedSecret(
    SecretUsageSubject Secret,
    DateTimeOffset? LastReadAt,
    long ReadCount,
    IReadOnlyList<SecretReadSummary> Reads
);

public static class UnusedSecretsBuilder
{
    /// <summary>
    /// Secrets never read, or not read since <paramref name="since"/>: never-read first, then the
    /// longest unread, then by application and name. Reads are listed most recent first.
    /// </summary>
    public static IReadOnlyList<UnusedSecret> Build(
        IEnumerable<SecretUsageSubject> secrets,
        IEnumerable<SecretReadSummary> reads,
        DateTimeOffset since
    )
    {
        var readsBySecret = reads.ToLookup(r => (r.IsGlobal, r.SecretId));
        return secrets
            .Select(s =>
            {
                var list = readsBySecret[(s.IsGlobal, s.Id)]
                    .OrderByDescending(r => r.LastReadAt)
                    .ThenBy(r => r.ApplicationSlug, StringComparer.Ordinal)
                    .ThenBy(r => r.Environment, StringComparer.Ordinal)
                    .ToList();
                return new UnusedSecret(
                    s,
                    list.Count == 0 ? null : list[0].LastReadAt,
                    list.Sum(r => r.ReadCount),
                    list
                );
            })
            .Where(u => u.LastReadAt is null || u.LastReadAt < since)
            .OrderBy(u => u.LastReadAt.HasValue)
            .ThenBy(u => u.LastReadAt)
            .ThenBy(u => u.Secret.IsGlobal)
            .ThenBy(u => u.Secret.ApplicationSlug, StringComparer.Ordinal)
            .ThenBy(u => u.Secret.Name, StringComparer.Ordinal)
            .ToList();
    }
}
