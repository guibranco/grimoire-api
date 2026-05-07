---
title: Getting Started
nav_order: 2
has_children: true
permalink: /getting-started/
---

# 🚀 Getting Started

Everything you need to go from zero to a running Grimoire instance.

---

## In this section

| Page | What you'll learn |
| :--- | :---------------- |
| [Installation]({{ site.baseurl }}/getting-started/installation/) | Docker Compose, dotnet run, and building from source |
| [Configuration]({{ site.baseurl }}/getting-started/configuration/) | All available settings and environment variables |
| [Quick Start]({{ site.baseurl }}/getting-started/quick-start/) | Create an app, store a secret, and read it back in 5 minutes |

---

## Prerequisites

| Requirement | Minimum version | Notes |
| :---------- | :-------------- | :---- |
| .NET SDK | 10.x | Only needed for `dotnet run` / building from source |
| Docker Desktop | any recent | Required for Docker Compose and E2E tests |
| Git | any | To clone the repository |

---

## Choose your path

### 🐳 I want to run it right now

Use [Docker Compose]({{ site.baseurl }}/getting-started/installation/#docker-compose-recommended) — no .NET SDK required.

### 💻 I want to develop or extend Grimoire

Follow [Building from source]({{ site.baseurl }}/getting-started/installation/#building-from-source), then come back to the [Architecture]({{ site.baseurl }}/architecture/) section to understand the codebase.

### 🔌 I want to integrate Grimoire into my .NET app

Skip straight to the [Client Library]({{ site.baseurl }}/client-library/) section.
