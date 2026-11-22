using Grimoire.Core.Dashboard;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Interfaces.Repositories;
using Grimoire.Core.Services;
using Grimoire.Infrastructure.Backups;
using Grimoire.Infrastructure.ConsumerTracking;
using Grimoire.Infrastructure.Persistence;
using Grimoire.Infrastructure.Persistence.Repositories;
using Grimoire.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Grimoire.Infrastructure;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddDbContext<GrimoireDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("Default"))
        );

        services.AddScoped<IApplicationRepository, ApplicationRepository>();
        services.AddScoped<IEnvironmentRepository, EnvironmentRepository>();
        services.AddScoped<ISecretRepository, SecretRepository>();
        services.AddScoped<IConfigurationRepository, ConfigurationRepository>();
        services.AddScoped<IGlobalConfigurationRepository, GlobalConfigurationRepository>();
        services.AddScoped<IGlobalSecretRepository, GlobalSecretRepository>();
        services.AddScoped<IEffectiveConfigurationService, EffectiveConfigurationService>();
        services.AddSingleton<IEncryptionService, AesGcmEncryptionService>();
        services.AddBackups();

        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        // Resolved lazily so configuration added after registration (e.g. by test hosts) applies.
        services.AddSingleton<IOptions<DashboardOptions>>(sp =>
            Options.Create(ReadDashboardOptions(sp.GetRequiredService<IConfiguration>()))
        );
        services.AddConsumerActivity();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IMetadataSearchService, MetadataSearchService>();

        return services;
    }

    private static void AddConsumerActivity(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var section = sp.GetRequiredService<IConfiguration>()
                .GetSection(ConsumerActivityOptions.SectionName);
            return new ConsumerActivityOptions
            {
                FlushIntervalSeconds = ReadInt(
                    section,
                    nameof(ConsumerActivityOptions.FlushIntervalSeconds)
                ) ?? new ConsumerActivityOptions().FlushIntervalSeconds,
            };
        });
        services.AddSingleton<ConsumerActivityTracker>();
        services.AddSingleton<IConsumerActivityTracker>(sp =>
            sp.GetRequiredService<ConsumerActivityTracker>()
        );
        services.AddScoped<ConsumerActivityStore>();
        services.TryAddScoped<IConsumerActivityReader, ConsumerActivityReader>();
    }

    private static DashboardOptions ReadDashboardOptions(IConfiguration configuration)
    {
        var defaults = new DashboardOptions();
        var section = configuration.GetSection(DashboardOptions.SectionName);

        return new DashboardOptions
        {
            ExpiringWithinDays = ReadInt(section, nameof(DashboardOptions.ExpiringWithinDays))
                ?? defaults.ExpiringWithinDays,
            StaleAfterDays = ReadInt(section, nameof(DashboardOptions.StaleAfterDays))
                ?? defaults.StaleAfterDays,
            CacheSeconds = ReadInt(section, nameof(DashboardOptions.CacheSeconds))
                ?? defaults.CacheSeconds,
        };
    }

    private static int? ReadInt(IConfigurationSection section, string key) =>
        int.TryParse(section[key], out var value) ? value : null;
}
