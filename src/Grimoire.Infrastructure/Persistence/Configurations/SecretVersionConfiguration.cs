using Grimoire.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Grimoire.Infrastructure.Persistence.Configurations;

public class SecretVersionConfiguration : IEntityTypeConfiguration<SecretVersion>
{
    public void Configure(EntityTypeBuilder<SecretVersion> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EncryptedValue).IsRequired();

        builder
            .HasOne(x => x.Secret)
            .WithMany(x => x.Versions)
            .HasForeignKey(x => x.SecretId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(x => x.Environment)
            .WithMany(x => x.SecretVersions)
            .HasForeignKey(x => x.EnvironmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
