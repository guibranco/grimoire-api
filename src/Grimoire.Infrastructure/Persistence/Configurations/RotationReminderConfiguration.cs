using Grimoire.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Grimoire.Infrastructure.Persistence.Configurations;

public class RotationReminderConfiguration : IEntityTypeConfiguration<RotationReminder>
{
    public const int KeyMaxLength = 512;

    public void Configure(EntityTypeBuilder<RotationReminder> builder)
    {
        builder.ToTable("RotationReminders");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Key).IsRequired().HasMaxLength(KeyMaxLength);
        builder.HasIndex(x => x.Key).IsUnique();

        // Stored as an integer so the cleanup can compare in SQL (always written in UTC).
        builder.Property(x => x.SentAt).HasConversion(new DateTimeOffsetToBinaryConverter());
        builder.HasIndex(x => x.SentAt);
    }
}
