---
title: Feature Flags
nav_order: 8
has_children: true
permalink: /feature-flags/
---

# 🚩 Feature Flags

Feature flags sit next to Secrets and Configuration. A flag is switched on or off per environment and can target an audience: specific users or attribute values, a percentage rollout, or an A/B/n split.

**Grimoire stores and serves flag definitions; consumers evaluate them.** Checking a flag never needs a network call and keeps working if Grimoire is down. Grimoire never receives end-user identifiers. The management API's `evaluate` endpoint is only a dry run for testing rules.

- [Evaluation spec](evaluation-spec/): the rules every client implements, with shared [test vectors](https://github.com/guibranco/grimoire-api/blob/main/docs/feature-flags/test-vectors.json)
- [Management API](../api-reference/management-api/#feature-flags): create, target, toggle, dry-run and history
- [Consumer API](../api-reference/consumer-api/#feature-flags): definitions with `ETag` support

## Concepts

| Term | Meaning |
| :--- | :------ |
| **Flag** | Belongs to one application. Its `key` is unique in that application: letters, digits, `.`, `_`, `:` and `-`. |
| **Type** | `boolean` flags have the fixed variants `on` (`true`) and `off` (`false`). `multivariate` flags have 2–20 named variants, each with any JSON value. |
| **Default variant** | Served when nothing more specific applies (see the [spec](evaluation-spec/)). `off` for boolean flags, the first variant otherwise. |
| **Environment state** | Per environment: `enabled`, the `offVariant`, ordered targeting `rules` and the `fallthrough`. A flag never configured in an environment is disabled there. Every change increments the state's `version`. |
| **Rule** | One or more conditions that must all match (AND), then a fixed `variant` or a weighted `rollout`. The first matching rule wins. |
| **Condition** | `attribute` (e.g. `userId`, `email`, `tenant`, `country`, `appVersion`), an `operator` and `values`. |
| **Rollout** | `[{ variant, weight }]` with weights summing to 100 (two decimals at most), bucketed by a context attribute (`bucketBy`, default `userId`). Raising a percentage keeps everyone who already had the variant. |
| **Salt** | Random per flag and served to consumers, so two flags at 10 % don't select the same users. |

## Operators

| Operator | Matches when the attribute… |
| :------- | :-------------------------- |
| `in` / `equals` | equals one of the values |
| `notIn` | is present and equals none of the values |
| `contains` | contains one of the values |
| `startsWith` / `endsWith` | starts / ends with one of the values |
| `matches` | matches one of the regular expressions (portable syntax only; 100 ms timeout) |
| `semVerGte` / `semVerLt` | is a [semantic version](https://semver.org) ≥ / < the single value |

Comparisons are ordinal and case-sensitive. A missing attribute never matches, not even `notIn`.

## Audit

Every create, update, targeting change, toggle and delete is kept in the flag's history (`GET …/feature-flags/{key}/history`) with the actor and the JSON before and after. It is also written to the [audit log](../api-reference/management-api/#audit-log) as `feature-flag.*`. The history survives deleting the flag.
