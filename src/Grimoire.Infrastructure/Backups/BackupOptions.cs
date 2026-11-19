namespace Grimoire.Infrastructure.Backups;

/// <summary>Settings bound from the <c>Backup</c> configuration section.</summary>
public sealed class BackupOptions
{
    public const string SectionName = "Backup";
    public const int MinimumKeyLength = 32;

    /// <summary>Turns on scheduled backups and the backup endpoints.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Passphrase the backup encryption key is derived from (HKDF-SHA256). Must differ from
    /// <c>Encryption:MasterKey</c>, so one leaked key does not expose both the backups and the secrets.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>Five-field cron expression for scheduled backups. Empty disables the schedule.</summary>
    public string Schedule { get; set; } = "0 3 * * *";

    /// <summary>IANA or Windows time zone the schedule is evaluated in.</summary>
    public string TimeZone { get; set; } = "UTC";

    /// <summary>How many backups each target keeps; older ones are deleted after every backup.</summary>
    public int RetentionCount { get; set; } = 14;

    /// <summary>healthchecks.io (or compatible) ping URL, called after every scheduled or manual backup.</summary>
    public string? HealthcheckUrl { get; set; }

    /// <summary>Ping URL for the restore test (<c>--verify-backup</c>).</summary>
    public string? VerifyHealthcheckUrl { get; set; }

    public LocalTargetOptions Local { get; set; } = new();
    public S3TargetOptions S3 { get; set; } = new();

    public bool HasStrongKey =>
        !string.IsNullOrWhiteSpace(Key)
        && Key.Length >= MinimumKeyLength
        && !Key.StartsWith("change-me", StringComparison.OrdinalIgnoreCase);

    public bool HasAnyTarget => Local.Enabled || S3.Enabled;

    /// <summary>Lists what is wrong with an enabled configuration; empty when it is usable.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!Enabled)
            return errors;

        if (!HasStrongKey)
            errors.Add(
                $"'Backup:Key' must be set to a non-default value of at least {MinimumKeyLength} characters."
            );
        if (!HasAnyTarget)
            errors.Add("Enable at least one backup target ('Backup:Local' or 'Backup:S3').");
        if (RetentionCount < 1)
            errors.Add("'Backup:RetentionCount' must be at least 1.");
        if (Local.Enabled && string.IsNullOrWhiteSpace(Local.Directory))
            errors.Add("'Backup:Local:Directory' is required when the local target is enabled.");
        if (S3.Enabled)
        {
            if (string.IsNullOrWhiteSpace(S3.ServiceUrl))
                errors.Add("'Backup:S3:ServiceUrl' is required when the S3 target is enabled.");
            if (string.IsNullOrWhiteSpace(S3.Bucket))
                errors.Add("'Backup:S3:Bucket' is required when the S3 target is enabled.");
            if (string.IsNullOrWhiteSpace(S3.AccessKey) || string.IsNullOrWhiteSpace(S3.SecretKey))
                errors.Add(
                    "'Backup:S3:AccessKey' and 'Backup:S3:SecretKey' are required when the S3 target is enabled."
                );
        }

        return errors;
    }
}

public sealed class LocalTargetOptions
{
    public bool Enabled { get; set; }

    /// <summary>Directory the encrypted backups are written to. Created (mode 700) if missing.</summary>
    public string? Directory { get; set; }
}

/// <summary>Any S3-compatible store: OCI Object Storage, AWS S3, MinIO, Cloudflare R2…</summary>
public sealed class S3TargetOptions
{
    public bool Enabled { get; set; }

    /// <summary>
    /// Endpoint URL. For OCI Object Storage:
    /// <c>https://&lt;namespace&gt;.compat.objectstorage.&lt;region&gt;.oraclecloud.com</c>.
    /// </summary>
    public string? ServiceUrl { get; set; }

    public string? Region { get; set; }
    public string? Bucket { get; set; }

    /// <summary>Object key prefix, e.g. <c>grimoire/</c>.</summary>
    public string Prefix { get; set; } = "grimoire/";

    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }

    /// <summary>Path-style addressing; required by OCI Object Storage and MinIO.</summary>
    public bool ForcePathStyle { get; set; } = true;
}
