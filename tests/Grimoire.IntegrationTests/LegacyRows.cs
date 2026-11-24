using Grimoire.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.IntegrationTests;

/// <summary>
/// Rows for databases migrated only part of the way, where the current model has columns the
/// table doesn't have yet (so <c>db.Environments.Add</c> would fail).
/// </summary>
internal static class LegacyRows
{
    /// <summary>An environment as it was before publishing settings (AddPublishing).</summary>
    public static Task InsertEnvironmentAsync(
        GrimoireDbContext db,
        Guid id,
        Guid applicationId,
        string name,
        string slug,
        DateTimeOffset at
    ) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "Environments" ("Id", "ApplicationId", "Name", "Slug", "CreatedAt", "UpdatedAt")
            VALUES ({Text(id)}, {Text(applicationId)}, {name}, {slug}, {at}, {at})
            """
        );

    // EF stores Guids as upper-case TEXT on SQLite.
    private static string Text(Guid id) => id.ToString().ToUpperInvariant();
}
