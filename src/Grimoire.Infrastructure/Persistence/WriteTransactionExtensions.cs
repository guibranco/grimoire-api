using Microsoft.EntityFrameworkCore;

namespace Grimoire.Infrastructure.Persistence;

internal static class WriteTransactionExtensions
{
    /// <summary>
    /// Runs <paramref name="work"/> in a transaction and commits it. On SQLite,
    /// Microsoft.Data.Sqlite opens transactions with <c>BEGIN IMMEDIATE</c>, which takes the
    /// database write lock up front: a second writer waits until this one commits, so reads made
    /// inside <paramref name="work"/> stay current until its save. Joins an already-open
    /// transaction instead of nesting.
    /// </summary>
    public static async Task<T> InWriteTransactionAsync<T>(
        this GrimoireDbContext db,
        Func<CancellationToken, Task<T>> work,
        CancellationToken ct
    )
    {
        if (db.Database.CurrentTransaction is not null)
            return await work(ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var result = await work(ct);
        await transaction.CommitAsync(ct);
        return result;
    }
}
