using Grimoire.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Grimoire.Infrastructure.Persistence.Configurations;

public class GlobalSecretConfiguration : IEntityTypeConfiguration<GlobalSecret>
{
    public void Configure(EntityTypeBuilder<GlobalSecret> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(1000);

        builder.HasIndex(x => x.Name).IsUnique();
    }
}
