---
title: Architecture
nav_order: 3
has_children: true
permalink: /architecture/
---

# 🏗️ Architecture

How Grimoire is designed, what runs where, and why.

---

## In this section

| Page | What you'll learn |
| :--- | :---------------- |
| [Overview]({{ site.baseurl }}/architecture/overview/) | Solution structure, project dependencies, request pipeline |
| [Data Model]({{ site.baseurl }}/architecture/data-model/) | Entity relationships, EF Core configuration, soft deletes |
| [Security]({{ site.baseurl }}/architecture/security/) | Encryption scheme, API key lifecycle, middleware |

---

## Design goals

| Goal | How Grimoire achieves it |
| :--- | :----------------------- |
| **Zero external dependencies** | Uses SQLite — no Redis, no external KMS, no cloud required |
| **Separation of concerns** | Management plane (admin) and consumer plane (app) are fully separate |
| **Secrets never leave encrypted** | Encryption happens before any DB write; decryption only on consumer read |
| **Testable end-to-end** | `WebApplicationFactory` integration tests + Testcontainers E2E tests |
| **Drop-in for Azure** | Consumer response shape matches Azure Key Vault and App Configuration |
