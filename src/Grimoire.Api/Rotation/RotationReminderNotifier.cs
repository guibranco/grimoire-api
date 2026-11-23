using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Grimoire.Core.Dashboard;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Secrets;
using Grimoire.Infrastructure.Backups;

namespace Grimoire.Api.Rotation;

/// <summary>Outcome of one reminder run.</summary>
/// <param name="Due">Secrets that crossed a threshold or are overdue.</param>
/// <param name="Sent">Reminders delivered by this run.</param>
/// <param name="AlreadySent">Reminders skipped because they were delivered before.</param>
/// <param name="Delivered"><c>false</c> when the webhook call failed; nothing is marked sent then.</param>
public sealed record RotationReminderRun(
    int Due,
    int Sent,
    int AlreadySent,
    bool WebhookConfigured,
    bool Delivered
);

/// <summary>
/// Finds secrets that crossed a reminder threshold and announces each (secret, environment, due
/// date, threshold) once, in a single webhook POST per run. Names and dates only, never values.
/// </summary>
public sealed class RotationReminderNotifier(
    IExpiringSecretsService expiring,
    IRotationReminderStore store,
    IHttpClientFactory httpClients,
    HealthcheckPinger pinger,
    RotationReminderOptions options,
    TimeProvider time,
    ILogger<RotationReminderNotifier> logger
)
{
    public const string HttpClientName = "rotation-reminders";
    public const string SignatureHeader = "X-Grimoire-Signature-256";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public async Task<RotationReminderRun> RunAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var thresholds = options.EffectiveThresholds;
        var horizon = thresholds.Count == 0 ? 0 : thresholds[0];

        var pending = (await expiring.ListAsync(horizon, ct))
            .Select(item =>
                SecretDueRules.CrossedThreshold(item.DueAt, now, thresholds) is { } threshold
                    ? new PendingReminder(item, threshold, KeyOf(item, threshold))
                    : null
            )
            .OfType<PendingReminder>()
            .ToList();

        var sent = await store.GetSentAsync(pending.Select(p => p.Key).ToList(), ct);
        var fresh = pending.Where(p => !sent.Contains(p.Key)).ToList();
        var run = new RotationReminderRun(
            pending.Count,
            0,
            pending.Count - fresh.Count,
            options.HasWebhook,
            Delivered: true
        );

        if (fresh.Count > 0 && !options.HasWebhook)
        {
            if (logger.IsEnabled(LogLevel.Warning))
                logger.LogWarning(
                    "{Count} rotation reminders are due but RotationReminders:WebhookUrl is not configured",
                    fresh.Count
                );
        }
        else if (fresh.Count > 0)
        {
            if (!await DeliverAsync(fresh, now, ct))
            {
                await pinger.FailureAsync(options.HealthcheckUrl, CancellationToken.None);
                return run with { Delivered = false };
            }

            await store.MarkSentAsync(fresh.Select(p => p.Key).ToList(), now, ct);
            run = run with { Sent = fresh.Count };
            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("Sent {Count} rotation reminders", fresh.Count);
        }

        await store.DeleteSentBeforeAsync(now.AddDays(-options.KeepSentDays), ct);
        await pinger.SuccessAsync(options.HealthcheckUrl, ct);
        return run;
    }

    /// <summary>
    /// One reminder per (secret, environment, reason, due date, threshold): a new version or a
    /// changed expiry moves the due date and starts the thresholds over.
    /// </summary>
    internal static string KeyOf(ExpiringSecret item, int threshold) =>
        string.Join(
            ':',
            item.IsGlobal ? "g" : "a",
            item.SecretId.ToString("N"),
            item.Environment,
            item.Reason,
            item.DueAt.UtcTicks,
            threshold
        );

    private async Task<bool> DeliverAsync(
        List<PendingReminder> reminders,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        var text = Summary(reminders.Select(r => r.Item).ToList(), now);
        var payload = new
        {
            @event = "rotation-reminder",
            generatedAt = now,
            text,
            // Discord reads "content", Slack and most chat webhooks read "text".
            content = text,
            items = reminders.Select(r => new
            {
                application = r.Item.Application,
                secret = r.Item.Secret,
                environment = r.Item.Environment,
                version = r.Item.Version,
                reason = r.Item.Reason,
                dueAt = r.Item.DueAt,
                daysRemaining = SecretDueRules.DaysRemaining(r.Item.DueAt, now),
                threshold = r.Threshold,
            }),
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(payload, Json);

        using var request = new HttpRequestMessage(HttpMethod.Post, options.WebhookUrl)
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
        if (!string.IsNullOrEmpty(options.WebhookSecret))
            request.Headers.Add(SignatureHeader, Sign(body, options.WebhookSecret));

        try
        {
            var http = httpClients.CreateClient(HttpClientName);
            http.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
                return true;
            logger.LogWarning(
                "Rotation reminder webhook answered {StatusCode}",
                (int)response.StatusCode
            );
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (ct.IsCancellationRequested)
                throw;
            logger.LogWarning(ex, "Rotation reminder webhook failed");
        }
        return false;
    }

    internal static string Sign(byte[] body, string secret) =>
        "sha256="
        + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));

    internal static string Summary(IReadOnlyList<ExpiringSecret> items, DateTimeOffset now)
    {
        var lines = new StringBuilder();
        lines
            .Append("Grimoire: ")
            .Append(items.Count)
            .Append(items.Count == 1 ? " secret needs" : " secrets need")
            .Append(" rotation");
        foreach (var item in items)
            lines
                .Append('\n')
                .Append("- ")
                .Append(item.Application ?? "global")
                .Append('/')
                .Append(item.Secret)
                .Append(" (")
                .Append(item.Environment)
                .Append("): ")
                .Append(Describe(item, SecretDueRules.DaysRemaining(item.DueAt, now)))
                .Append(" (")
                .Append(item.DueAt.UtcDateTime.ToString("yyyy-MM-dd"))
                .Append(')');
        return lines.ToString();
    }

    private static string Describe(ExpiringSecret item, int days)
    {
        var what = item.Reason switch
        {
            SecretDueReasons.Rotation => "rotation due",
            SecretDueReasons.Expired => "expired",
            _ => "expires",
        };
        return item.Reason == SecretDueReasons.Expired
            ? days == 0
                ? "expired today"
                : $"expired {-days} day{(days == -1 ? "" : "s")} ago"
            : days switch
            {
                < 0 => $"{what} {-days} day{(days == -1 ? "" : "s")} ago",
                0 => $"{what} today",
                _ => $"{what} in {days} day{(days == 1 ? "" : "s")}",
            };
    }

    private sealed record PendingReminder(ExpiringSecret Item, int Threshold, string Key);
}
