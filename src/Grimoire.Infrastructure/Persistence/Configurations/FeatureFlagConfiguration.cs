using System.Text.Json;
using Grimoire.Core.Entities;
using Grimoire.Core.FeatureFlags;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Grimoire.Infrastructure.Persistence.Configurations;

/// <summary>Stores a value as a JSON text column, compared by its JSON for change tracking.</summary>
internal static class JsonColumn
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;

    public static PropertyBuilder<T> HasJsonConversion<T>(this PropertyBuilder<T> property)
    {
        property.HasConversion(
            new ValueConverter<T, string>(v => Serialize(v), v => Deserialize<T>(v)),
            new ValueComparer<T>(
                (a, b) => Serialize(a) == Serialize(b),
                v => Serialize(v).GetHashCode(),
                v => Deserialize<T>(Serialize(v))
            )
        );
        return property;
    }
}

public class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlag>
{
    public const int KeyMaxLength = 500;
    public const int SaltLength = 16;

    public void Configure(EntityTypeBuilder<FeatureFlag> builder)
    {
        builder.ToTable("FeatureFlags");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Key).IsRequired().HasMaxLength(KeyMaxLength);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.Type).IsRequired().HasMaxLength(20);
        builder.Property(x => x.DefaultVariant).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Salt).IsRequired().HasMaxLength(SaltLength);
        builder.Property(x => x.Variants).HasJsonConversion().IsRequired();
        builder.Property(x => x.Tags).HasJsonConversion().IsRequired();
        builder.HasIndex(x => new { x.ApplicationId, x.Key }).IsUnique();

        builder
            .HasOne(x => x.Application)
            .WithMany()
            .HasForeignKey(x => x.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasMany(x => x.Environments)
            .WithOne(x => x.FeatureFlag)
            .HasForeignKey(x => x.FeatureFlagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class FeatureFlagEnvironmentStateConfiguration
    : IEntityTypeConfiguration<FeatureFlagEnvironmentState>
{
    public void Configure(EntityTypeBuilder<FeatureFlagEnvironmentState> builder)
    {
        builder.ToTable("FeatureFlagEnvironmentStates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OffVariant).HasMaxLength(100);
        builder.Property(x => x.Rules).HasJsonConversion().IsRequired();
        builder.Property(x => x.Fallthrough).HasJsonConversion();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.FeatureFlagId, x.EnvironmentId }).IsUnique();

        builder
            .HasOne(x => x.Environment)
            .WithMany()
            .HasForeignKey(x => x.EnvironmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class FeatureFlagAuditEntryConfiguration : IEntityTypeConfiguration<FeatureFlagAuditEntry>
{
    public void Configure(EntityTypeBuilder<FeatureFlagAuditEntry> builder)
    {
        builder.ToTable("FeatureFlagAuditEntries");
        builder.HasKey(x => x.Id);
        builder
            .Property(x => x.FlagKey)
            .IsRequired()
            .HasMaxLength(FeatureFlagConfiguration.KeyMaxLength);
        builder.Property(x => x.Environment).HasMaxLength(100);
        builder.Property(x => x.Change).IsRequired().HasMaxLength(20);
        builder.Property(x => x.Actor).IsRequired().HasMaxLength(100);
        // Integer, so SQLite can order by time. Always written in UTC.
        builder.Property(x => x.ChangedAt).HasConversion(new DateTimeOffsetToBinaryConverter());
        builder.HasIndex(x => new
        {
            x.ApplicationId,
            x.FlagKey,
            x.ChangedAt,
        });

        builder
            .HasOne<Application>()
            .WithMany()
            .HasForeignKey(x => x.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
