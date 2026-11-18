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
