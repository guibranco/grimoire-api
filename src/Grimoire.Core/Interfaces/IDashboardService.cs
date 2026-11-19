using Grimoire.Core.Dashboard;

namespace Grimoire.Core.Interfaces;

/// <summary>Builds the value-free dashboard summary for the management UI home page.</summary>
public interface IDashboardService
{
    /// <param name="expiringWithinDays">Overrides <see cref="DashboardOptions.ExpiringWithinDays"/>.</param>
    /// <param name="staleAfterDays">Overrides <see cref="DashboardOptions.StaleAfterDays"/>.</param>
    Task<DashboardSummary> GetSummaryAsync(
        int? expiringWithinDays = null,
        int? staleAfterDays = null,
        CancellationToken ct = default
    );
}

/// <summary>
/// Source of "last seen" timestamps for consumers. Until consumer activity is tracked (#103),
/// the registered implementation returns <c>null</c>, which turns the consumer rules off.
/// </summary>
public interface IConsumerActivityReader
{
    Task<IReadOnlyDictionary<
        (Guid ApplicationId, Guid EnvironmentId),
        DateTimeOffset
    >?> GetLastSeenAsync(CancellationToken ct = default);
}
