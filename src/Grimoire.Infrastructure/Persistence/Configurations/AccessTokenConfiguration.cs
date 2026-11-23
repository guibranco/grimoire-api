using Grimoire.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Grimoire.Infrastructure.Persistence.Configurations;

public class AccessTokenConfiguration : IEntityTypeConfiguration<AccessToken>
{
    public void Configure(EntityTypeBuilder<AccessToken> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.Property(x => x.TokenHash).IsRequired().HasMaxLength(500);
        builder.Property(x => x.Prefix).IsRequired().HasMaxLength(AccessToken.LookupLength);
        builder.Property(x => x.EnvironmentSlugs).HasJsonConversion();
        builder.Property(x => x.Scopes).IsRequired();
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeOffsetToBinaryConverter());

        builder.HasIndex(x => x.Prefix);
        builder.HasIndex(x => x.TrustPolicyId);
        builder.HasIndex(x => new { x.ApplicationId, x.Name }).IsUnique();

        builder
            .HasOne(x => x.Application)
            .WithMany()
            .HasForeignKey(x => x.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
