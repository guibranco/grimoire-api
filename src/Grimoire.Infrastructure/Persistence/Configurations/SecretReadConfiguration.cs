using Grimoire.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Grimoire.Infrastructure.Persistence.Configurations;

public class SecretReadConfiguration : IEntityTypeConfiguration<SecretRead>
{
    public void Configure(EntityTypeBuilder<SecretRead> builder)
    {
        builder.ToTable("SecretReads", t =>
            t.HasCheckConstraint(
                "CK_SecretReads_OneSecret",
                "(\"SecretId\" IS NULL) <> (\"GlobalSecretId\" IS NULL)"
            )
        );
        builder.HasKey(x => x.Id);

        // Same encoding as AuditEvents.OccurredAt, so reads can be backfilled from the audit log.
        builder.Property(x => x.LastReadAt).HasConversion(new DateTimeOffsetToBinaryConverter());

        // One row per reading environment and secret (NULLs never collide, hence the filters).
        builder
            .HasIndex(x => new { x.EnvironmentId, x.SecretId })
            .IsUnique()
            .HasFilter("\"SecretId\" IS NOT NULL");
        builder
            .HasIndex(x => new { x.EnvironmentId, x.GlobalSecretId })
            .IsUnique()
            .HasFilter("\"GlobalSecretId\" IS NOT NULL");

        builder
            .HasOne(x => x.Application)
            .WithMany()
            .HasForeignKey(x => x.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasOne(x => x.Environment)
            .WithMany()
            .HasForeignKey(x => x.EnvironmentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasOne<Secret>()
            .WithMany()
            .HasForeignKey(x => x.SecretId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasOne<GlobalSecret>()
            .WithMany()
            .HasForeignKey(x => x.GlobalSecretId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
