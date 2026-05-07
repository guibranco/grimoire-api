using Grimoire.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Grimoire.Infrastructure.Persistence.Configurations;

public class AppEnvironmentConfiguration : IEntityTypeConfiguration<AppEnvironment>
{
    public void Configure(EntityTypeBuilder<AppEnvironment> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Slug).IsRequired().HasMaxLength(100);

        builder.HasIndex(x => new { x.ApplicationId, x.Slug }).IsUnique();

        builder.HasOne(x => x.Application)
            .WithMany(x => x.Environments)
            .HasForeignKey(x => x.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
