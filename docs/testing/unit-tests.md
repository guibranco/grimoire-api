---
title: Unit Tests
parent: Testing
nav_order: 1
---

# 🔬 Unit Tests
{: .no_toc }

Fast, isolated tests with no external dependencies.

<details open markdown="block">
  <summary>Table of contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

## Running

```bash
dotnet test tests/Grimoire.Tests
```

**Expected output:** 43 tests, ~2 seconds.

---

## Test coverage

### `SlugServiceTests` (13 tests)

Tests for `SlugService.Generate()` — the utility that converts application names to URL-safe slugs.

| Test | Input | Expected output |
| :--- | :---- | :-------------- |
| `Generate_ProducesExpectedSlug` (×9) | `"My Application"` | `"my-application"` |
| — | `"Special!@#Chars"` | `"specialchars"` |
| — | `"Multiple   Spaces"` | `"multiple-spaces"` |
| — | `"a---b---c"` | `"a-b-c"` |
| `Generate_LowercasesAllCharacters` | `"ABC DEF"` | all lowercase |
| `Generate_NoLeadingOrTrailingDashes` | `"  test  "` | no leading/trailing `-` |
| `Generate_NoDuplicateDashes` | `"a   b   c"` | no `--` |

### `ValidatorTests` (30 tests)

Tests for all FluentValidation request validators using `AbstractValidator<T>.Validate()` directly (no TestHelper dependency).

| Validator | Scenarios covered |
| :-------- | :---------------- |
| `CreateApplicationRequestValidator` | Valid input · empty name · name too long (>200) |
| `UpdateApplicationRequestValidator` | Valid input · empty name |
| `CreateEnvironmentRequestValidator` | Valid input · empty name |
| `CreateSecretRequestValidator` | Empty name |
| `SetSecretValueRequestValidator` | Valid input · empty env slug · empty value · `ExpiresAt` before `NotBefore` |
| `CreateConfigurationRequestValidator` | Valid input · empty key · empty env slug |
| `UpdateConfigurationRequestValidator` | Valid input |

---

## Test helpers

```csharp
// AssertValid — expects no validation errors
private static void AssertValid<T>(AbstractValidator<T> v, T model)
{
    var result = v.Validate(model);
    Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
}

// AssertInvalidFor — expects a specific property to fail
private static void AssertInvalidFor<T>(AbstractValidator<T> v, T model, string propertyName)
{
    var result = v.Validate(model);
    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e =>
        e.PropertyName.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
}
```

---

## Project structure

```text
tests/Grimoire.Tests/
├── Grimoire.Tests.csproj    ← net10.0, references Grimoire.Api
├── SlugServiceTests.cs
└── ValidatorTests.cs
```

The test project references `Grimoire.Api` (for validators and DTOs) and `Grimoire.Infrastructure` (for `SlugService`). It does **not** start any web host.
