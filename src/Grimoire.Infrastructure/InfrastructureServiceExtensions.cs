using Grimoire.Core.Interfaces;
using Grimoire.Core.Interfaces.Repositories;
using Grimoire.Infrastructure.Persistence;
using Grimoire.Infrastructure.Persistence.Repositories;
using Grimoire.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddSingleton<IEncryptionService, AesGcmEncryptionService>();

        return services;
    }
}
