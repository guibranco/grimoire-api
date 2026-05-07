namespace Grimoire.Api.DTOs.Consumer;

public record SecretProperties(
    bool Enabled,
    DateTimeOffset? ExpiresOn,
    DateTimeOffset? NotBefore,
    int Version,
    DateTimeOffset CreatedOn,
    DateTimeOffset? UpdatedOn);

public record KeyVaultSecretResponse(string Name, string Value, SecretProperties Properties);

public record ConfigurationItem(string Key, string Value, string Label);
public record ConfigurationListResponse(IReadOnlyList<ConfigurationItem> Items);
