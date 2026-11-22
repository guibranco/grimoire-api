using Grimoire.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Grimoire.Infrastructure.Persistence.Configurations;

public class ConsumerActivityConfiguration : IEntityTypeConfiguration<ConsumerActivity>
{
    public const int UserAgentMaxLength = 256;
    public const int IpAddressMaxLength = 45;

    public void Configure(EntityTypeBuilder<ConsumerActivity> builder)
    {
        builder.ToTable("ConsumerActivities");
        builder.HasKey(x => new { x.ApplicationId, x.EnvironmentId });
        builder.Property(x => x.LastUserAgent).HasMaxLength(UserAgentMaxLength);
        builder.Property(x => x.LastIpAddress).HasMaxLength(IpAddressMaxLength);

        builder
            .HasOne(x => x.Application)
            .WithMany()
            .HasForeignKey(x => x.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(x => x.Environment)
            .WithMany()
            .HasForeignKey(x => x.EnvironmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
