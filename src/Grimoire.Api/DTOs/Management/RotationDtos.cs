namespace Grimoire.Api.DTOs.Management;

/// <param name="WithinDays">The window that was applied; overdue and expired secrets are always included.</param>
public record ExpiringSecretsResponse(
    DateTimeOffset GeneratedAt,
    int WithinDays,
    IReadOnlyList<ExpiringSecretResponse> Items
);

/// <summary>A secret, in one environment, due for rotation. Never contains a value.</summary>
/// <param name="Application">Application slug; <c>null</c> for a global secret.</param>
/// <param name="Environment">Environment slug (<c>*</c> for a global value targeting every environment).</param>
/// <param name="Version">The version currently served (or, for <c>expired</c>, the last one).</param>
/// <param name="Reason"><c>expiry</c>, <c>rotation</c> or <c>expired</c>.</param>
/// <param name="DaysRemaining">Whole UTC calendar days until <paramref name="DueAt"/>; negative when overdue.</param>
public record ExpiringSecretResponse(
    string? Application,
    string? ApplicationName,
    string Secret,
    string Environment,
    bool IsGlobal,
    int Version,
    string Reason,
    DateTimeOffset DueAt,
    int DaysRemaining,
    int? RotationIntervalDays
);

/// <param name="Due">Secrets that crossed a reminder threshold or are overdue.</param>
/// <param name="Sent">Reminders delivered by this run.</param>
/// <param name="AlreadySent">Reminders skipped because they were delivered before.</param>
/// <param name="WebhookConfigured"><c>false</c> means nothing can be delivered until <c>RotationReminders:WebhookUrl</c> is set.</param>
/// <param name="Delivered"><c>false</c> when the webhook call failed.</param>
public record RotationReminderRunResponse(
    int Due,
    int Sent,
    int AlreadySent,
    bool WebhookConfigured,
    bool Delivered
);
