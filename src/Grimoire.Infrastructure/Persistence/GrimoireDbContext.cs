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
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<RotationReminder> RotationReminders => Set<RotationReminder>();
    public DbSet<ConfigurationRevision> ConfigurationRevisions => Set<ConfigurationRevision>();
    public DbSet<SecretRead> SecretReads => Set<SecretRead>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<FeatureFlagEnvironmentState> FeatureFlagEnvironmentStates =>
        Set<FeatureFlagEnvironmentState>();
    public DbSet<FeatureFlagAuditEntry> FeatureFlagAuditEntries => Set<FeatureFlagAuditEntry>();
    public DbSet<AccessToken> AccessTokens => Set<AccessToken>();
    public DbSet<TrustPolicy> TrustPolicies => Set<TrustPolicy>();
    public DbSet<GlobalPublishing> GlobalPublishing => Set<GlobalPublishing>();

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
        modelBuilder.ApplyConfiguration(new AuditEventConfiguration());
        modelBuilder.ApplyConfiguration(new RotationReminderConfiguration());
        modelBuilder.ApplyConfiguration(new ConfigurationRevisionConfiguration());
        modelBuilder.ApplyConfiguration(new SecretReadConfiguration());
        modelBuilder.ApplyConfiguration(new FeatureFlagConfiguration());
        modelBuilder.ApplyConfiguration(new FeatureFlagEnvironmentStateConfiguration());
        modelBuilder.ApplyConfiguration(new FeatureFlagAuditEntryConfiguration());
        modelBuilder.ApplyConfiguration(new AccessTokenConfiguration());
        modelBuilder.ApplyConfiguration(new TrustPolicyConfiguration());
        modelBuilder.ApplyConfiguration(new GlobalPublishingConfiguration());
    }
}
