namespace Grimoire.Api.Rotation;

/// <summary>Settings bound from the <c>RotationReminders</c> configuration section.</summary>
public sealed class RotationReminderOptions
{
    public const string SectionName = "RotationReminders";

    /// <summary>Runs the reminder job. With no webhook configured it only logs what is due.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Hours between runs (the first run is shortly after startup).</summary>
    public int IntervalHours { get; set; } = 24;

    public static readonly IReadOnlyList<int> DefaultThresholds = [30, 7, 1];

    /// <summary>
    /// Days before the due date at which a reminder goes out (default 30, 7, 1). Each threshold
    /// is announced once per due date, and once more when the secret becomes due or expires.
    /// </summary>
    /// <remarks>Empty by default: the configuration binder appends to array initialisers.</remarks>
    public int[] Thresholds { get; set; } = [];

    /// <summary>Generic JSON POST target (gstraccini bot, Discord, Slack, n8n...). Empty disables delivery.</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>When set, the body is signed: <c>X-Grimoire-Signature-256: sha256=&lt;hex HMAC-SHA256&gt;</c>.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>healthchecks.io (or compatible) URL pinged after every run (<c>/fail</c> when delivery fails).</summary>
    public string? HealthcheckUrl { get; set; }

    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Delivered reminders are remembered this long, then forgotten.</summary>
    public int KeepSentDays { get; set; } = 400;

    public bool HasWebhook =>
        Uri.TryCreate(WebhookUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    /// <summary>Positive thresholds, largest first, without duplicates; the defaults when none are set.</summary>
    public IReadOnlyList<int> GetEffectiveThresholds() =>
        (Thresholds.Any(d => d > 0) ? Thresholds : DefaultThresholds)
            .Where(d => d > 0)
            .Distinct()
            .OrderDescending()
            .ToList();
}
