using Grimoire.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Grimoire.Infrastructure.Persistence.Configurations;

public class GlobalConfigurationEntryConfiguration
    : IEntityTypeConfiguration<GlobalConfigurationEntry>
{
    public void Configure(EntityTypeBuilder<GlobalConfigurationEntry> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EnvironmentSlug).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Key).IsRequired().HasMaxLength(500);
        builder.Property(x => x.Value).IsRequired();
        builder
            .Property(x => x.ValueType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(ConfigurationValueType.String);
        builder.Property(x => x.Description).HasMaxLength(1000);

        // "*" (not NULL) marks "all environments" so this unique index holds:
        // SQLite treats NULLs as distinct in unique indexes.
        builder.HasIndex(x => new { x.EnvironmentSlug, x.Key }).IsUnique();
    }
}
