namespace Grimoire.Api.DTOs.Management;

/// <summary>A file to import into one environment.</summary>
/// <param name="Format"><c>env</c>, <c>appsettings</c>, <c>php</c> or <c>json</c>.</param>
/// <param name="Content">The file's text. PHP is read as data and never executed.</param>
/// <param name="SecretKeys">
/// Globs (<c>*</c> = anything, case-insensitive) for keys to import as secrets; the rest become
/// configuration. Defaults to <c>*Password*</c>, <c>*Secret*</c>, <c>*Token*</c>, <c>ConnectionStrings:*</c>.
/// </param>
/// <param name="OnConflict"><c>skip</c> (default) keeps keys that already have another value; <c>overwrite</c> replaces them.</param>
/// <param name="Kinds">Per-key overrides of the classification: key → <c>secret</c> or <c>configuration</c>.</param>
/// <param name="Exclude">Keys to leave out.</param>
public record ImportRequest(
    string Format,
    string Content,
    List<string>? SecretKeys = null,
    string? OnConflict = null,
    Dictionary<string, string>? Kinds = null,
    List<string>? Exclude = null
);

/// <param name="Key">The key (sections joined with <c>:</c>).</param>
/// <param name="Kind"><c>secret</c> or <c>configuration</c>.</param>
/// <param name="Action"><c>create</c>, <c>update</c>, <c>unchanged</c>, <c>skip</c>, <c>conflict</c> (exists as the other kind) or <c>excluded</c>.</param>
/// <param name="Value">The configuration value; always <c>null</c> for secrets.</param>
/// <param name="ValueType">The configuration value type; <c>null</c> for secrets.</param>
public record ImportPlanItemResponse(
    string Key,
    string Kind,
    string Action,
    string? Value,
    string? ValueType
);

/// <param name="Create">Keys created (secrets also count when they get their first value here).</param>
/// <param name="Update">Keys whose value is replaced.</param>
/// <param name="Unchanged">Keys that already have the imported value.</param>
/// <param name="Skipped">Keys left alone: other value with <c>skip</c>, conflicts and exclusions.</param>
public record ImportSummary(int Create, int Update, int Unchanged, int Skipped);

/// <param name="Environment">The environment imported into (<c>*</c> for all, on globals).</param>
/// <param name="DryRun">Whether this is only the plan (nothing was written).</param>
/// <param name="SecretKeys">The globs used to classify secrets.</param>
/// <param name="Items">Every key, in file order.</param>
/// <param name="Skipped">Keys whose value isn't plain data (e.g. a PHP function call), so weren't read.</param>
public record ImportPlanResponse(
    string Environment,
    bool DryRun,
    IReadOnlyList<string> SecretKeys,
    IReadOnlyList<ImportPlanItemResponse> Items,
    ImportSummary Summary,
    IReadOnlyList<string> Skipped
);
