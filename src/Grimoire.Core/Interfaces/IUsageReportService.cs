using Grimoire.Core.Reports;

namespace Grimoire.Core.Interfaces;

/// <summary>Secret usage reports built from the Consumer API read rollup. Never returns values.</summary>
public interface IUsageReportService
{
    /// <summary>Secrets (application and global) never read, or not read since <paramref name="since"/>.</summary>
    Task<IReadOnlyList<UnusedSecret>> ListUnusedSecretsAsync(
        DateTimeOffset since,
        CancellationToken ct = default
    );

    /// <summary>Applications, secrets and configuration keys, and how they depend on each other.</summary>
    Task<DependencyGraph> GetDependencyGraphAsync(CancellationToken ct = default);
}
