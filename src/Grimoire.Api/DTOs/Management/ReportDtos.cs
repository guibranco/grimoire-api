namespace Grimoire.Api.DTOs.Management;

/// <param name="Days">The window applied: secrets not read within this many days are listed.</param>
/// <param name="Since">Start of the window (<paramref name="GeneratedAt"/> minus <paramref name="Days"/>).</param>
public record UnusedSecretsResponse(
    DateTimeOffset GeneratedAt,
    int Days,
    DateTimeOffset Since,
    IReadOnlyList<UnusedSecretResponse> Items
);

/// <summary>A secret with no Consumer API read in the window. Never contains a value.</summary>
/// <param name="Application">Owning application slug; <c>null</c> for a global secret.</param>
/// <param name="LastReadAt">Most recent read by any application environment; <c>null</c> if never read.</param>
/// <param name="ReadCount">Approximate total reads recorded.</param>
/// <param name="Reads">Last read per reading application and environment, most recent first.</param>
public record UnusedSecretResponse(
    string? Application,
    string? ApplicationName,
    string Secret,
    bool IsGlobal,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastReadAt,
    long ReadCount,
    IReadOnlyList<SecretReadResponse> Reads
);

/// <param name="Application">Slug of the application that read the secret.</param>
/// <param name="Environment">That application's environment slug.</param>
public record SecretReadResponse(
    string Application,
    string Environment,
    DateTimeOffset LastReadAt,
    long ReadCount
);

/// <param name="Nodes">Applications, secrets, global secrets, configuration keys and global keys.</param>
/// <param name="Edges"><c>owns</c>, <c>reads</c>, <c>overrides</c> and <c>inherits</c> relations.</param>
public record DependencyGraphResponse(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<DependencyNodeResponse> Nodes,
    IReadOnlyList<DependencyEdgeResponse> Edges
);

/// <param name="Id">Stable id: <c>app:{slug}</c>, <c>secret:{slug}/{name}</c>, <c>global-secret:{name}</c>, <c>config:{slug}/{key}</c> or <c>global-config:{key}</c>.</param>
/// <param name="Type"><c>application</c>, <c>secret</c>, <c>global-secret</c>, <c>config-key</c> or <c>global-config-key</c>.</param>
/// <param name="Application">Owning application slug for secrets and keys; otherwise <c>null</c>.</param>
/// <param name="Environments">Environment slugs a configuration key exists in (<c>*</c> for every environment).</param>
public record DependencyNodeResponse(
    string Id,
    string Type,
    string Label,
    string? Application,
    IReadOnlyList<string> Environments
);

/// <param name="Kind"><c>owns</c>, <c>reads</c>, <c>overrides</c> or <c>inherits</c>.</param>
/// <param name="Environments">For <c>reads</c>, the reading environments.</param>
/// <param name="LastReadAt">For <c>reads</c>, the most recent read.</param>
/// <param name="ReadCount">For <c>reads</c>, the approximate number of reads.</param>
public record DependencyEdgeResponse(
    string Source,
    string Target,
    string Kind,
    IReadOnlyList<string> Environments,
    DateTimeOffset? LastReadAt,
    long? ReadCount
);
