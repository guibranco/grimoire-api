using Grimoire.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Grimoire.Infrastructure.Persistence.Configurations;

public class TrustPolicyConfiguration : IEntityTypeConfiguration<TrustPolicy>
{
    public void Configure(EntityTypeBuilder<TrustPolicy> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Repository).IsRequired().HasMaxLength(200);
        builder.Property(x => x.RefPattern).HasMaxLength(255);
        builder.Property(x => x.GitHubEnvironment).HasMaxLength(255);
        builder.Property(x => x.EnvironmentSlugs).HasJsonConversion();
        builder.Property(x => x.Scopes).IsRequired();
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeOffsetToBinaryConverter());

        builder.HasIndex(x => new { x.ApplicationId, x.Name }).IsUnique();

        builder
            .HasOne(x => x.Application)
            .WithMany()
            .HasForeignKey(x => x.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
