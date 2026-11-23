using Grimoire.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Grimoire.Infrastructure.Persistence.Configurations;

public class ConfigurationRevisionConfiguration : IEntityTypeConfiguration<ConfigurationRevision>
{
    public void Configure(EntityTypeBuilder<ConfigurationRevision> builder)
    {
        builder.ToTable("ConfigurationRevisions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EnvironmentKey).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Key).IsRequired().HasMaxLength(500);
        // Same text representation as the configuration entries, so history copies them as is.
        builder.Property(x => x.ValueType).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Change).IsRequired().HasMaxLength(20);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.ChangedBy).IsRequired().HasMaxLength(100);

        builder
            .HasIndex(x => new
            {
                x.Scope,
                x.EnvironmentKey,
                x.Key,
                x.Revision,
            })
            .IsUnique();
        builder.HasIndex(x => x.ApplicationId);
    }
}
