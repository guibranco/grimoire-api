namespace Grimoire.Api.DTOs.Consumer;

public record SecretProperties(
    bool Enabled,
    DateTimeOffset? ExpiresOn,
    DateTimeOffset? NotBefore,
    int Version,
    DateTimeOffset CreatedOn,
    DateTimeOffset? UpdatedOn
);

public record KeyVaultSecretResponse(string Name, string Value, SecretProperties Properties);

/// <param name="ValueType">
/// "string", "multiline", "array" (a JSON array of strings) or "json" (a JSON object or array).
/// </param>
public record ConfigurationItem(string Key, string Value, string Label, string ValueType);

public record ConfigurationListResponse(IReadOnlyList<ConfigurationItem> Items);

/// <summary>A secret the application can read in an environment, without its value.</summary>
/// <param name="Source"><c>application</c> or <c>global</c>.</param>
/// <param name="Active">Whether a version is active now; <paramref name="Version"/> and <paramref name="ExpiresOn"/> describe it.</param>
public record SecretListItem(
    string Name,
    string Source,
    bool Active,
    int? Version,
    DateTimeOffset? ExpiresOn
);

public record SecretListResponse(IReadOnlyList<SecretListItem> Items);

/// <summary>An export in the <c>json</c> format.</summary>
/// <param name="Skipped">Secret names left out because they have no active version.</param>
public record ExportJsonResponse(
    IReadOnlyDictionary<string, string> Secrets,
    IReadOnlyDictionary<string, string> Configurations,
    IReadOnlyList<string> Skipped
);
