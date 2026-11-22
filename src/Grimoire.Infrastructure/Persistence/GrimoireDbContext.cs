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
    public DbSet<GlobalConfigurationEntry> GlobalConfigurationEntries =>
        Set<GlobalConfigurationEntry>();
    public DbSet<GlobalSecret> GlobalSecrets => Set<GlobalSecret>();
    public DbSet<GlobalSecretVersion> GlobalSecretVersions => Set<GlobalSecretVersion>();
    public DbSet<ConsumerActivity> ConsumerActivities => Set<ConsumerActivity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ApplicationConfiguration());
        modelBuilder.ApplyConfiguration(new AppEnvironmentConfiguration());
        modelBuilder.ApplyConfiguration(new SecretConfiguration());
        modelBuilder.ApplyConfiguration(new SecretVersionConfiguration());
        modelBuilder.ApplyConfiguration(new ConfigurationEntryConfiguration());
        modelBuilder.ApplyConfiguration(new GlobalConfigurationEntryConfiguration());
        modelBuilder.ApplyConfiguration(new GlobalSecretConfiguration());
        modelBuilder.ApplyConfiguration(new GlobalSecretVersionConfiguration());
        modelBuilder.ApplyConfiguration(new ConsumerActivityConfiguration());
    }
}
