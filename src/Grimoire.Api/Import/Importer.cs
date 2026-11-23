using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Entities;
using Grimoire.Core.Import;
using Grimoire.Core.Services;

namespace Grimoire.Api.Import;

/// <summary>Reads an import file, plans it against a target and, unless it's a dry run, applies it.</summary>
public sealed class Importer(TimeProvider time)
{
    public const int MaxConfigurationKeyLength = 500;
    public const int MaxSecretNameLength = 200;

    /// <summary>The plan (applied when <paramref name="dryRun"/> is false), or the reason the file can't be imported.</summary>
    public async Task<(ImportPlanResponse? Plan, string? Error)> RunAsync(
        IImportTarget target,
        string environment,
        ImportRequest req,
        bool dryRun,
        CancellationToken ct
    )
    {
        ImportParser.TryParseFormat(req.Format, out var format);
        ImportParseResult parsed;
        try
        {
            parsed = ImportParser.Parse(format, req.Content);
        }
        catch (ImportParseException ex)
        {
            return (null, ex.Message);
        }

        var values = new List<ImportedValue>();
        foreach (var v in parsed.Values)
        {
            if (
                !ConfigurationValueNormalizer.TryNormalize(
                    v.Type,
                    v.Value,
                    out var normalized,
                    out var error
                )
            )
                return (null, $"'{v.Key}': {error}");
            values.Add(v with { Value = normalized });
        }

        var secretKeys = req.SecretKeys is { Count: > 0 }
            ? req.SecretKeys
            : ImportPlanner.DefaultSecretKeys.ToList();
        var kinds = (req.Kinds ?? []).ToDictionary(
            k => k.Key,
            k =>
                k.Value.Equals("secret", StringComparison.OrdinalIgnoreCase)
                    ? ImportKind.Secret
                    : ImportKind.Configuration,
            StringComparer.Ordinal
        );
        var excluded = (req.Exclude ?? []).ToHashSet(StringComparer.Ordinal);
        var overwrite = string.Equals(
            req.OnConflict,
            "overwrite",
            StringComparison.OrdinalIgnoreCase
        );
        var now = time.GetUtcNow();

        async Task<List<ImportPlanItem>> PlanAsync(CancellationToken token)
        {
            var snapshot = await target.SnapshotAsync(
                values.Select(v => v.Key).ToList(),
                now,
                token
            );
            return ImportPlanner.Plan(values, snapshot, secretKeys, kinds, excluded, overwrite);
        }

        var items = dryRun
            ? await PlanAsync(ct)
            : await target.InTransactionAsync(
                async token =>
                {
                    var plan = await PlanAsync(token);
                    if (TooLong(plan) is null)
                        foreach (
                            var item in plan.Where(i =>
                                i.Action is ImportAction.Create or ImportAction.Update
                            )
                        )
                            await target.ApplyAsync(item, now, token);
                    return plan;
                },
                ct
            );

        if (TooLong(items) is { } tooLong)
            return (null, tooLong);

        return (
            new ImportPlanResponse(
                environment,
                dryRun,
                secretKeys,
                items.Select(ToResponse).ToList(),
                new ImportSummary(
                    items.Count(i => i.Action == ImportAction.Create),
                    items.Count(i => i.Action == ImportAction.Update),
                    items.Count(i => i.Action == ImportAction.Unchanged),
                    items.Count(i =>
                        i.Action
                            is ImportAction.Skip
                                or ImportAction.Conflict
                                or ImportAction.Excluded
                    )
                ),
                parsed.Skipped
            ),
            null
        );
    }

    /// <summary>The first key longer than its kind allows, as an error message.</summary>
    private static string? TooLong(IEnumerable<ImportPlanItem> items) =>
        items
            .Where(i => i.Action != ImportAction.Excluded)
            .Where(i =>
                i.Key.Length
                > (i.Kind == ImportKind.Secret ? MaxSecretNameLength : MaxConfigurationKeyLength)
            )
            .Select(i =>
                $"'{i.Key[..40]}…' is longer than {(i.Kind == ImportKind.Secret ? MaxSecretNameLength : MaxConfigurationKeyLength)} characters."
            )
            .FirstOrDefault();

    private static ImportPlanItemResponse ToResponse(ImportPlanItem item) =>
        item.Kind == ImportKind.Secret
            ? new ImportPlanItemResponse(item.Key, "secret", ActionName(item.Action), null, null)
            : new ImportPlanItemResponse(
                item.Key,
                "configuration",
                ActionName(item.Action),
                item.Value,
                ConfigurationValueNormalizer.ToName(item.Type)
            );

    private static string ActionName(ImportAction action) => action.ToString().ToLowerInvariant();
}
