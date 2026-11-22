using Grimoire.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Grimoire.Infrastructure.Persistence.Configurations;

public class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public const int ShortTextMaxLength = 100;
    public const int TargetMaxLength = 256;
    public const int UserAgentMaxLength = 256;
    public const int IpAddressMaxLength = 45;
    public const int EnvironmentMaxLength = 512;

    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("AuditEvents");
        builder.HasKey(x => x.Id);

        // Stored as an integer so SQLite can filter and order by time. Always written in UTC,
        // which keeps the binary encoding monotonic.
        builder.Property(x => x.OccurredAt).HasConversion(new DateTimeOffsetToBinaryConverter());

        builder.Property(x => x.Actor).IsRequired().HasMaxLength(ShortTextMaxLength);
        builder.Property(x => x.Action).IsRequired().HasMaxLength(ShortTextMaxLength);
        builder.Property(x => x.ApplicationSlug).HasMaxLength(ShortTextMaxLength);
        builder.Property(x => x.Environment).HasMaxLength(EnvironmentMaxLength);
        builder.Property(x => x.TargetType).HasMaxLength(ShortTextMaxLength);
        builder.Property(x => x.Target).HasMaxLength(TargetMaxLength);
        builder.Property(x => x.IpAddress).HasMaxLength(IpAddressMaxLength);
        builder.Property(x => x.UserAgent).HasMaxLength(UserAgentMaxLength);

        builder.HasIndex(x => x.OccurredAt);
        builder.HasIndex(x => new { x.ApplicationSlug, x.OccurredAt });
        builder.HasIndex(x => new { x.Action, x.OccurredAt });
    }
}
