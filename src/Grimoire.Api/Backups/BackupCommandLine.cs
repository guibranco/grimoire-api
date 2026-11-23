using Grimoire.Core.Interfaces;
using Grimoire.Infrastructure.Backups;

namespace Grimoire.Api.Backups;

public enum BackupCommandKind
{
    Backup,
    Restore,
    Verify,
}

public sealed record BackupCommand(BackupCommandKind Kind, string? Source, bool Force);

/// <summary>
/// Maintenance modes of <c>Grimoire.Api.dll</c>. They use the normal configuration (appsettings,
/// environment variables) but never start the web server:
/// <code>
/// dotnet Grimoire.Api.dll --backup                              # take a backup now
/// dotnet Grimoire.Api.dll --restore &lt;file|latest&gt; [--force]  # replace the database (service stopped!)
/// dotnet Grimoire.Api.dll --verify-backup &lt;file|latest&gt;       # restore test into a throwaway copy
/// </code>
/// </summary>
public static class BackupCommandLine
{
    /// <summary>
    /// Extracts a backup command from <paramref name="args"/>. The remaining arguments are returned so the
    /// configuration command-line provider never sees the backup flags.
    /// </summary>
    public static (BackupCommand? Command, string[] Remaining) Parse(string[] args)
    {
        BackupCommand? command = null;
        var force = false;
        var remaining = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--backup":
                    command = new BackupCommand(BackupCommandKind.Backup, null, false);
                    break;
                case "--restore":
                    command = new BackupCommand(
                        BackupCommandKind.Restore,
                        RequireValue(args, ref i),
                        false
                    );
                    break;
                case "--verify-backup":
                    command = new BackupCommand(
                        BackupCommandKind.Verify,
                        RequireValue(args, ref i),
                        false
                    );
                    break;
                case "--force":
                    force = true;
                    break;
                default:
                    remaining.Add(args[i]);
                    break;
            }
        }

        if (force && command is { Kind: BackupCommandKind.Restore })
            command = command with { Force = true };

        return (command, remaining.ToArray());
    }

    public static async Task<int> RunAsync(BackupCommand command, IServiceProvider services)
    {
        var ct = CancellationToken.None;
        try
        {
            switch (command.Kind)
            {
                case BackupCommandKind.Backup:
                {
                    var backup = await services
                        .GetRequiredService<IBackupService>()
                        .CreateBackupAsync(ct);
                    Console.WriteLine(
                        $"✅ Backup {backup.Name} ({backup.SizeBytes} bytes) written to {string.Join(", ", backup.Targets)}"
                    );
                    return 0;
                }
                case BackupCommandKind.Restore:
                {
                    var restorer = services.GetRequiredService<BackupRestorer>();
                    var source = await restorer.RestoreAsync(command.Source!, command.Force, ct);
                    Console.WriteLine(
                        $"✅ Database restored from {source}. Start the service to apply pending migrations."
                    );
                    return 0;
                }
                case BackupCommandKind.Verify:
                    return await VerifyAsync(command.Source!, services, ct);
                default:
                    return 2;
            }
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"❌ {ex.GetType().Name}: {ex.Message}");
            if (command.Kind == BackupCommandKind.Verify)
                await services
                    .GetRequiredService<HealthcheckPinger>()
                    .FailureAsync(
                        services.GetRequiredService<BackupOptions>().VerifyHealthcheckUrl,
                        ct
                    );
            return 1;
        }
    }

    private static async Task<int> VerifyAsync(
        string source,
        IServiceProvider services,
        CancellationToken ct
    )
    {
        var options = services.GetRequiredService<BackupOptions>();
        var pinger = services.GetRequiredService<HealthcheckPinger>();
        var result = await services.GetRequiredService<BackupRestorer>().VerifyAsync(source, ct);

        Console.WriteLine($"Backup:                 {result.Source}");
        Console.WriteLine($"Applications:           {result.Applications}");
        Console.WriteLine($"Secret versions:        {result.SecretVersions}");
        Console.WriteLine($"Global secret versions: {result.GlobalSecretVersions}");
        Console.WriteLine($"Configuration entries:  {result.ConfigurationEntries}");
        Console.WriteLine($"Secret decrypts:        {(result.SecretDecrypted ? "yes" : "NO")}");

        if (result.Passed)
        {
            Console.WriteLine("✅ Restore test passed");
            await pinger.SuccessAsync(options.VerifyHealthcheckUrl, ct);
            return 0;
        }

        await Console.Error.WriteLineAsync(
            "❌ Restore test failed: expected at least one application and one secret version, and a secret that decrypts with the master key"
        );
        await pinger.FailureAsync(options.VerifyHealthcheckUrl, ct);
        return 1;
    }

    private static string RequireValue(string[] args, ref int i)
    {
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"{args[i]} needs a backup file path or 'latest'.");
        return args[++i];
    }
}
