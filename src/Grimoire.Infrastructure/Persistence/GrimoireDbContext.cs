using Grimoire.Core.Entities;
using Grimoire.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Infrastructure.Persistence;

public class GrimoireDbContext(DbContextOptions<GrimoireDbContext> options) : DbContext(options)
{
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<AppEnvironment> Environments => Set<AppEnvironment>();
    public DbSet<Secret> Secrets => Set<Secret>();
    public DbSet<SecretVersion> SecretVersions => Set<SecretVersion>();
    public DbSet<ConfigurationEntry> ConfigurationEntries => Set<ConfigurationEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ApplicationConfiguration());
        modelBuilder.ApplyConfiguration(new AppEnvironmentConfiguration());
        modelBuilder.ApplyConfiguration(new SecretConfiguration());
        modelBuilder.ApplyConfiguration(new SecretVersionConfiguration());
        modelBuilder.ApplyConfiguration(new ConfigurationEntryConfiguration());
    }
}
