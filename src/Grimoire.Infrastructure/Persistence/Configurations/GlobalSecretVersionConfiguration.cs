using Grimoire.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Grimoire.Infrastructure.Persistence.Configurations;

public class GlobalSecretVersionConfiguration : IEntityTypeConfiguration<GlobalSecretVersion>
{
    public void Configure(EntityTypeBuilder<GlobalSecretVersion> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EnvironmentSlug).IsRequired().HasMaxLength(100);
        builder.Property(x => x.EncryptedValue).IsRequired();

        builder.HasIndex(x => new
        {
            x.GlobalSecretId,
            x.EnvironmentSlug,
            x.Version,
        });

        builder
            .HasOne(x => x.GlobalSecret)
            .WithMany(x => x.Versions)
            .HasForeignKey(x => x.GlobalSecretId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
