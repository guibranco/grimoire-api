using Grimoire.Core.Dashboard;
using Grimoire.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Infrastructure.Services;

/// <summary>
/// Reads value-free projections of applications, environments, secrets and their versions (one
/// query per table, never the encrypted value). Rows of soft-deleted applications are excluded.
/// </summary>
public class SecretMetadataReader(GrimoireDbContext db)
{
    public async Task<SecretMetadataSnapshot> LoadAsync(CancellationToken ct)
    {
        // Applications carry a global query filter on IsDeleted; every other query joins through
        // the application so rows of soft-deleted applications are excluded too.
        var applications = await db
            .Applications.AsNoTracking()
            .Select(a => new ApplicationRow(a.Id, a.Slug, a.Name, a.InheritGlobals, a.UpdatedAt))
            .ToListAsync(ct);

        var environments = await db
            .Environments.AsNoTracking()
            .Where(e => !e.Application.IsDeleted)
            .Select(e => new EnvironmentRow(e.Id, e.ApplicationId, e.Slug, e.Name, e.UpdatedAt))
            .ToListAsync(ct);

        var secrets = await db
            .Secrets.AsNoTracking()
            .Where(s => !s.Application.IsDeleted)
            .Select(s => new SecretRow(
                s.Id,
                s.ApplicationId,
                s.Name,
                s.UpdatedAt,
                s.RotationIntervalDays
            ))
            .ToListAsync(ct);

        var secretVersions = await db
            .SecretVersions.AsNoTracking()
            .Where(v => !v.Secret.Application.IsDeleted)
            .Select(v => new SecretVersionRow(
                v.SecretId,
                v.EnvironmentId,
                v.IsEnabled,
                v.ExpiresAt,
                v.NotBefore,
                v.Version,
                v.CreatedAt
            ))
            .ToListAsync(ct);

        var globalSecrets = await db
            .GlobalSecrets.AsNoTracking()
            .Select(s => new GlobalSecretRow(s.Id, s.Name, s.UpdatedAt, s.RotationIntervalDays))
            .ToListAsync(ct);

        var globalSecretVersions = await db
            .GlobalSecretVersions.AsNoTracking()
            .Select(v => new GlobalSecretVersionRow(
                v.GlobalSecretId,
                v.EnvironmentSlug,
                v.IsEnabled,
                v.ExpiresAt,
                v.NotBefore,
                v.Version,
                v.CreatedAt
            ))
            .ToListAsync(ct);

        return new SecretMetadataSnapshot(
            applications,
            environments,
            secrets,
            secretVersions,
            globalSecrets,
            globalSecretVersions
        );
    }
}

public class ExpiringSecretsService(SecretMetadataReader reader, TimeProvider time)
    : Core.Interfaces.IExpiringSecretsService
{
    public async Task<IReadOnlyList<ExpiringSecret>> ListAsync(
        int withinDays,
        CancellationToken ct = default
    )
    {
        var snapshot = await reader.LoadAsync(ct);
        var now = time.GetUtcNow();
        return ExpiringSecretsBuilder.Build(snapshot, now, now.AddDays(withinDays));
    }
}
