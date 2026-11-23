namespace Grimoire.Core.Audit;

public enum AuditOutcome
{
    Success = 0,
    NotFound = 1,
    Forbidden = 2,
    Invalid = 3,
    Conflict = 4,
    Error = 5,
}

/// <summary>Audited action codes, with a human-readable label and target type for each.</summary>
public static class AuditActions
{
    public const string ApplicationCreate = "application.create";
    public const string ApplicationUpdate = "application.update";
    public const string ApplicationDelete = "application.delete";
    public const string ApplicationRotateKey = "application.rotate-key";
    public const string EnvironmentCreate = "environment.create";
    public const string EnvironmentDelete = "environment.delete";
    public const string SecretCreate = "secret.create";
    public const string SecretUpdate = "secret.update";
    public const string SecretWrite = "secret.write";
    public const string SecretDelete = "secret.delete";
    public const string SecretVersionUpdate = "secret.version-update";
    public const string SecretRead = "secret.read";
    public const string ConfigurationCreate = "configuration.create";
    public const string ConfigurationUpdate = "configuration.update";
    public const string ConfigurationDelete = "configuration.delete";
    public const string ConfigurationRead = "configuration.read";
    public const string GlobalSecretCreate = "global-secret.create";
    public const string GlobalSecretUpdate = "global-secret.update";
    public const string GlobalSecretWrite = "global-secret.write";
    public const string GlobalSecretDelete = "global-secret.delete";
    public const string GlobalSecretVersionUpdate = "global-secret.version-update";
    public const string GlobalConfigurationCreate = "global-configuration.create";
    public const string GlobalConfigurationUpdate = "global-configuration.update";
    public const string GlobalConfigurationDelete = "global-configuration.delete";
    public const string BackupCreate = "backup.create";
    public const string RotationRemindersRun = "rotation.remind";
    public const string AuthFailed = "auth.failed";

    private const string ApplicationTarget = "application";
    private const string EnvironmentTarget = "environment";
    private const string SecretTarget = "secret";
    private const string ConfigurationTarget = "configuration";

    private static readonly Dictionary<string, (string Label, string? TargetType)> Catalogue = new()
    {
        [ApplicationCreate] = ("Created application", ApplicationTarget),
        [ApplicationUpdate] = ("Updated application", ApplicationTarget),
        [ApplicationDelete] = ("Deleted application", ApplicationTarget),
        [ApplicationRotateKey] = ("Rotated API key", ApplicationTarget),
        [EnvironmentCreate] = ("Created environment", EnvironmentTarget),
        [EnvironmentDelete] = ("Deleted environment", EnvironmentTarget),
        [SecretCreate] = ("Created secret", SecretTarget),
        [SecretUpdate] = ("Updated secret", SecretTarget),
        [SecretWrite] = ("Added secret version", SecretTarget),
        [SecretDelete] = ("Deleted secret", SecretTarget),
        [SecretVersionUpdate] = ("Updated secret version", SecretTarget),
        [SecretRead] = ("Read secret", SecretTarget),
        [ConfigurationCreate] = ("Created configuration", ConfigurationTarget),
        [ConfigurationUpdate] = ("Updated configuration", ConfigurationTarget),
        [ConfigurationDelete] = ("Deleted configuration", ConfigurationTarget),
        [ConfigurationRead] = ("Read configuration", ConfigurationTarget),
        [GlobalSecretCreate] = ("Created global secret", SecretTarget),
        [GlobalSecretUpdate] = ("Updated global secret", SecretTarget),
        [GlobalSecretWrite] = ("Added global secret version", SecretTarget),
        [GlobalSecretDelete] = ("Deleted global secret", SecretTarget),
        [GlobalSecretVersionUpdate] = ("Updated global secret version", SecretTarget),
        [GlobalConfigurationCreate] = ("Created global configuration", ConfigurationTarget),
        [GlobalConfigurationUpdate] = ("Updated global configuration", ConfigurationTarget),
        [GlobalConfigurationDelete] = ("Deleted global configuration", ConfigurationTarget),
        [BackupCreate] = ("Created backup", "backup"),
        [RotationRemindersRun] = ("Sent rotation reminders", null),
        [AuthFailed] = ("Rejected unauthenticated request", null),
    };

    public static IReadOnlyCollection<string> All => Catalogue.Keys;

    public static bool IsKnown(string action) => Catalogue.ContainsKey(action);

    /// <summary>Human-readable label, e.g. "Added secret version"; the code itself if unknown.</summary>
    public static string Label(string action) =>
        Catalogue.TryGetValue(action, out var entry) ? entry.Label : action;

    public static string? TargetType(string action) =>
        Catalogue.TryGetValue(action, out var entry) ? entry.TargetType : null;

    /// <summary>Consumer reads: frequent and not changes, so kept out of "recent activity".</summary>
    public static bool IsRead(string action) => action.EndsWith(".read", StringComparison.Ordinal);

    public static AuditOutcome OutcomeFor(int statusCode) =>
        statusCode switch
        {
            >= 200 and < 400 => AuditOutcome.Success,
            401 or 403 => AuditOutcome.Forbidden,
            404 => AuditOutcome.NotFound,
            409 => AuditOutcome.Conflict,
            >= 400 and < 500 => AuditOutcome.Invalid,
            _ => AuditOutcome.Error,
        };
}
