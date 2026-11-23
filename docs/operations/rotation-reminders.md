---
title: Rotation reminders
parent: Operations
nav_order: 2
---

# ⏰ Rotation reminders
{: .no_toc }

<details open markdown="block">
  <summary>Table of contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

Grimoire warns before a secret has to be rotated, so an expired password doesn't take an application down. Reminders contain names and dates only, never values.

## What counts as due

Each secret is evaluated per environment, on the version consumers get now (the [current version](../api-reference/management-api.md#how-the-served-version-is-chosen)):

| Reason | Due date |
| :----- | :------- |
| `expiry` | The current version's `expiresAt`. |
| `rotation` | `rotationIntervalDays` after the current version's `notBefore` (else its creation). Set the interval when creating the secret or with `PATCH` on the secret. |
| `expired` | The latest enabled version has expired and nothing else is valid. |

The earlier of `expiry` and `rotation` wins. If a newer version is already scheduled (`notBefore`) to take over before the due date, the rotation is planned and nothing is due. Adding a new value resets the clock: the new version becomes current and gets its own due date.

Everything due is visible in `GET /management/secrets/expiring` and on the UI dashboard (`secret-expiring` / `secret-rotation-due`).

## The reminder job

A background job runs a minute after startup and then every `RotationReminders:IntervalHours` (24). For each secret and environment it picks the tightest threshold reached (by default 30, 7 and 1 days before the due date, and `0` once it is due) and announces it **once**. A reminder is identified by secret, environment, reason, due date and threshold, so:

- a secret first seen 5 days before its due date gets the 7-day reminder only, not 30 and 7 at once;
- the 1-day and due/overdue reminders still follow;
- rotating the secret (or changing its expiry) moves the due date and starts the thresholds over.

All new reminders of a run go out in a single webhook call. If the call fails, nothing is marked as sent and the next run retries. To run the job now, call `POST /management/secrets/expiring/notify`.

## Configuration

```json
"RotationReminders": {
  "WebhookUrl": "https://example.com/hooks/grimoire",
  "WebhookSecret": "a-long-random-string",
  "HealthcheckUrl": "https://hc-ping.com/<uuid>",
  "Thresholds": [ 30, 7, 1 ]
}
```

All settings are listed in [Configuration](../getting-started/configuration.md#rotation-reminders). Without `WebhookUrl` the job still runs and logs a warning with the number of reminders it could not send.

`HealthcheckUrl` is pinged after every run, and `/fail` is pinged when the webhook call fails, so a dead man's switch notices both a stopped job and a broken webhook.

## Webhook payload

`POST {WebhookUrl}` with `Content-Type: application/json`:

```json
{
  "event": "rotation-reminder",
  "generatedAt": "2026-11-23T09:00:00Z",
  "text": "Grimoire: 2 secrets need rotation\n- billing/DB_PASSWORD (production): rotation due in 7 days (2026-11-30)\n- global/SMTP_PASSWORD (*): expires in 1 day (2026-11-24)",
  "content": "…same as text…",
  "items": [
    {
      "application": "billing",
      "secret": "DB_PASSWORD",
      "environment": "production",
      "version": 3,
      "reason": "rotation",
      "dueAt": "2026-11-30T09:00:00Z",
      "daysRemaining": 7,
      "threshold": 7
    }
  ]
}
```

`text` (Slack and most chat webhooks) and `content` (Discord) carry the same human-readable summary, so the URL of a chat webhook works as is. `application` is `null` for global secrets. Any `2xx` answer counts as delivered.

## Verifying the signature

With `WebhookSecret` set, every call carries:

```
X-Grimoire-Signature-256: sha256=<hex HMAC-SHA256 of the raw body, keyed with WebhookSecret>
```

Compute the HMAC over the raw request body bytes and compare in constant time:

```csharp
var expected = "sha256=" + Convert.ToHexStringLower(
    HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));
var valid = CryptographicOperations.FixedTimeEquals(
    Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(header));
```

## Not yet

- Email (SMTP) delivery.
- Automatic rotation providers (for example rotating a MySQL password), behind an `IRotationProvider` interface.
