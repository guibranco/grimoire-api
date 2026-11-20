using Grimoire.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Grimoire.Infrastructure.Backups;

public static class BackupServiceExtensions
{
    public static IServiceCollection AddBackups(this IServiceCollection services)
    {
        // Bound lazily from the final IConfiguration, so test hosts and late sources are honoured.
        services.AddSingleton(sp =>
            sp.GetRequiredService<IConfiguration>()
                .GetSection(BackupOptions.SectionName)
                .Get<BackupOptions>()
            ?? new BackupOptions()
        );
        services.AddSingleton<IReadOnlyList<IBackupTarget>>(sp =>
            CreateTargets(sp.GetRequiredService<BackupOptions>())
        );
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(sp => new HealthcheckPinger(
            new HttpClient { Timeout = TimeSpan.FromSeconds(10) },
            sp.GetRequiredService<ILogger<HealthcheckPinger>>()
        ));
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton(sp => new BackupRestorer(
            sp.GetRequiredService<BackupOptions>(),
            sp.GetRequiredService<IReadOnlyList<IBackupTarget>>(),
            sp.GetRequiredService<IEncryptionService>(),
            sp.GetRequiredService<IConfiguration>().GetConnectionString("Default") ?? string.Empty
        ));

        return services;
    }

    /// <summary>Enabled targets in read-preference order: local first, then S3.</summary>
    public static IReadOnlyList<IBackupTarget> CreateTargets(BackupOptions options)
    {
        var targets = new List<IBackupTarget>();
        if (options.Local.Enabled && !string.IsNullOrWhiteSpace(options.Local.Directory))
            targets.Add(new LocalDirectoryBackupTarget(options.Local.Directory));
        if (
            options.S3.Enabled
            && !string.IsNullOrWhiteSpace(options.S3.ServiceUrl)
            && !string.IsNullOrWhiteSpace(options.S3.Bucket)
        )
            targets.Add(new S3BackupTarget(options.S3));
        return targets;
    }
}
