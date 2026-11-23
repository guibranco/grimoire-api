using Grimoire.Core.Dashboard;

namespace Grimoire.Core.Interfaces;

/// <summary>Secrets due for rotation, across applications and globals. Metadata only, never values.</summary>
public interface IExpiringSecretsService
{
    /// <summary>Everything due within <paramref name="withinDays"/> days, plus anything overdue or expired.</summary>
    Task<IReadOnlyList<ExpiringSecret>> ListAsync(int withinDays, CancellationToken ct = default);
}

/// <summary>Remembers which rotation reminders were delivered.</summary>
public interface IRotationReminderStore
{
    /// <returns>The subset of <paramref name="keys"/> already delivered.</returns>
    Task<IReadOnlySet<string>> GetSentAsync(
        IReadOnlyCollection<string> keys,
        CancellationToken ct = default
    );

    Task MarkSentAsync(
        IReadOnlyCollection<string> keys,
        DateTimeOffset sentAt,
        CancellationToken ct = default
    );

    /// <returns>Number of records removed.</returns>
    Task<int> DeleteSentBeforeAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
