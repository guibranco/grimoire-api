---
title: Refresh spec (all clients)
parent: Client Library
nav_order: 5
---

# 🔁 Configuration refresh spec
{: .no_toc }

How every Grimoire client (.NET, PHP, Node, Python) picks up configuration changes without a restart. The model follows Azure App Configuration's **sentinel key**: clients poll one small key and reload everything only when it changes. Every client follows this spec, so they all behave the same way (guibranco/grimoire-api#95).

<details open markdown="block">
  <summary>Table of contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

## Sentinel keys

| Key | Scope | Bumped by |
| :-- | :---- | :-------- |
| `Sentinel` | One per application environment | Any change to that environment's configuration or secret versions, or to the application's global inheritance |
| `Global:Sentinel` | One for all globals, stored at the `*` layer | Any change to global configuration or global secret versions, whichever environment-slug layer it targets |

Both are ordinary configuration keys. Clients read them with the single-key endpoint, `GET /consumer/configurations/{key}`. They are **left out** of the configuration list, the export, environment diffs and dashboard counts. Their values are increasing numbers (Unix milliseconds), but clients only compare them for equality.

Applications that don't inherit globals never see `Global:Sentinel` and get `404` for it. Clients treat that as "absent", not as an error.

## Auto-publish

Grimoire decides when to bump a sentinel; clients don't choose a mode:

- **Auto-publish on** (default): every change bumps the sentinel in the same transaction, so clients pick it up on their next check.
- **Auto-publish off:** the sentinel changes only when someone publishes (the UI "Publish changes" button or `POST …/publish`). Running clients then reload once, after the whole batch of edits, instead of after every edit.

{: .warning }
Auto-publish controls **when running clients reload**, not what Grimoire serves. Edits are saved and served immediately. A process that starts, or a reload triggered by the *other* sentinel (for example a global publish), reads every saved value, including edits in an environment that hasn't been published yet. Keep half-finished edits out of environments where that matters.

There is one setting per application environment and one for globals. Turning auto-publish back on publishes any pending changes. See [Publishing](../api-reference/management-api/#publishing).

## Client algorithm

1. **Startup:** load every value (`GET /consumer/configurations`), then read each watched key once to record its value and `ETag`. Recording a value never triggers a reload.
2. **Every interval** (default 30 s) plus 0–20 % random jitter: send a conditional request for each watched key with `If-None-Match: <last ETag>`.
   - `304`: unchanged.
   - `200` with the same value: unchanged (store the new `ETag`).
   - `200` with a different value, or a key that was absent and now exists, or one that existed and is now `404`: **changed**.
3. **On a change** to a key watched with *refresh all* (both sentinels by default): fetch the full list, build a new dictionary and **swap it in atomically**. Never mutate the live dictionary in place. Then notify the application (reload token, event or callback).
4. **Secret cache:** a client that caches secrets clears that cache when a sentinel changes. Secrets are fetched again lazily on the next read, not eagerly.
5. **On failure** (network, HTTP 5xx, bad JSON): keep the last good values and back off exponentially (interval × 2ⁿ, at most 5 minutes). After the next success, go back to the normal interval. Never clear configuration because of a failed check.
6. **Shutdown:** stop the timer and dispose the HTTP client.

Optional: a key watched *without* refresh all updates only that key (Azure's `refreshAll: false`).

## Cost

An unchanged check is one request per watched key, each answered with `304` and no body. With the default two sentinels and a 30-second interval, that's about 4 requests per minute per process.

Each poll counts as a consumer read for activity tracking and the audit log, the same as feature-flag polling.

## Clients

| Client | Status |
| :----- | :----- |
| .NET `Grimoire.Consumer` | `AddGrimoire(o => o.Connect(…).ConfigureRefresh())`. See [Configuration Provider](configuration-provider/#reloading-on-changes) |
| PHP (Pancake `GuiBranco\Pancake\Grimoire`) | guibranco/pancake#688 |
| Node | Planned (new package) |
| Python | Planned (new package) |
