---
title: Testing
nav_order: 6
has_children: true
permalink: /testing/
---

# 🧪 Testing

Grimoire has three levels of testing coverage — unit, integration, and end-to-end.

---

## In this section

| Page | What you'll learn |
| :--- | :---------------- |
| [Unit Tests]({{ site.baseurl }}/testing/unit-tests/) | Fast tests for validators and the slug service |
| [Integration Tests]({{ site.baseurl }}/testing/integration-tests/) | Full HTTP tests using `WebApplicationFactory` |
| [E2E Tests]({{ site.baseurl }}/testing/e2e-tests/) | Real Docker container tests using Testcontainers |

---

## Test suite summary

| Suite | Project | Count | Speed | Requires |
| :---- | :------ | :---: | :---- | :------- |
| Unit | `Grimoire.Tests` | **43** | ~2 s | Nothing |
| Integration | `Grimoire.IntegrationTests` | **50** | ~10 s | .NET SDK |
| End-to-end | `Grimoire.E2eTests` | **7** | ~60 s | Docker |

---

## Running all tests

```bash
# Unit + integration only (CI default, no Docker required)
dotnet test --filter "FullyQualifiedName!~E2eTests"

# Everything including E2E (requires Docker)
docker build -t grimoire-api:e2e .
GRIMOIRE_TEST_IMAGE=grimoire-api:e2e dotnet test
```

---

## CI configuration

The GitHub Actions workflow runs two jobs:

- **`build-and-test`** — unit + integration tests on every push/PR
- **`e2e`** — Docker build + E2E tests, runs after `build-and-test` passes

See [`.github/workflows/build.yml`](https://github.com/guibranco/grimoire-api/blob/main/.github/workflows/build.yml).
