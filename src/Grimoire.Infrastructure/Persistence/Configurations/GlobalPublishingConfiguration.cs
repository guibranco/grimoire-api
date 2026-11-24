using Grimoire.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Grimoire.Infrastructure.Persistence.Configurations;

public class GlobalPublishingConfiguration : IEntityTypeConfiguration<GlobalPublishing>
{
    public void Configure(EntityTypeBuilder<GlobalPublishing> builder)
    {
        builder.ToTable("GlobalPublishing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
    }
}
