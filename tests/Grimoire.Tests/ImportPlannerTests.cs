using Grimoire.Core.Entities;
using Grimoire.Core.Import;

namespace Grimoire.Tests;

public class ImportPlannerTests
{
    private static ImportedValue Text(string key, string value) =>
        new(key, value, ConfigurationValueType.String);

    private static readonly ImportSnapshot Existing = new(
        new Dictionary<string, (string, ConfigurationValueType)>
        {
            ["Mode"] = ("safe", ConfigurationValueType.String),
            ["Region"] = ("eu", ConfigurationValueType.String),
            ["ApiToken"] = ("x", ConfigurationValueType.String),
        },
        new Dictionary<string, string?>
        {
            ["DbPassword"] = "old",
            ["SmtpPassword"] = "same",
            ["CachePassword"] = null,
            ["Feature"] = "on",
        }
    );

    private static Dictionary<string, ImportPlanItem> Plan(
        bool overwrite,
        Dictionary<string, ImportKind>? kinds = null,
        HashSet<string>? excluded = null
    ) =>
        ImportPlanner
            .Plan(
                [
                    Text("Mode", "safe"),
                    Text("Region", "us"),
                    Text("NewKey", "1"),
                    Text("DbPassword", "new"),
                    Text("SmtpPassword", "same"),
                    Text("CachePassword", "c"),
                    Text("ApiToken", "t"),
                    Text("Feature", "off"),
                    Text("ConnectionStrings:Main", "Server=db"),
                ],
                Existing,
                ImportPlanner.DefaultSecretKeys,
                kinds ?? [],
                excluded ?? [],
                overwrite
            )
            .ToDictionary(i => i.Key);

    [Fact]
    public void ClassifiesByGlobAndComparesWithWhatExists()
    {
        var plan = Plan(overwrite: false);

        Assert.Equal(
            (ImportKind.Configuration, ImportAction.Unchanged),
            (plan["Mode"].Kind, plan["Mode"].Action)
        );
        Assert.Equal(ImportAction.Skip, plan["Region"].Action);
        Assert.Equal(ImportAction.Create, plan["NewKey"].Action);
        Assert.Equal(
            (ImportKind.Secret, ImportAction.Skip),
            (plan["DbPassword"].Kind, plan["DbPassword"].Action)
        );
        Assert.Equal(ImportAction.Unchanged, plan["SmtpPassword"].Action);
        Assert.Equal(ImportAction.Create, plan["CachePassword"].Action);
        Assert.Equal(
            (ImportKind.Secret, ImportAction.Conflict),
            (plan["ApiToken"].Kind, plan["ApiToken"].Action)
        );
        Assert.Equal(
            (ImportKind.Configuration, ImportAction.Conflict),
            (plan["Feature"].Kind, plan["Feature"].Action)
        );
        Assert.Equal(ImportKind.Secret, plan["ConnectionStrings:Main"].Kind);
    }

    [Fact]
    public void Overwrite_UpdatesKeysWithAnotherValue_ButNeverConflicts()
    {
        var plan = Plan(overwrite: true);
        Assert.Equal(ImportAction.Update, plan["Region"].Action);
        Assert.Equal(ImportAction.Update, plan["DbPassword"].Action);
        Assert.Equal(ImportAction.Conflict, plan["ApiToken"].Action);
    }

    [Fact]
    public void KindsAndExclusions_OverrideTheGlobs()
    {
        var plan = Plan(
            overwrite: false,
            kinds: new()
            {
                ["ApiToken"] = ImportKind.Configuration,
                ["NewKey"] = ImportKind.Secret,
            },
            excluded: ["Region"]
        );
        // Now configuration, ApiToken matches the existing entry's kind: just a different value.
        Assert.Equal(
            (ImportKind.Configuration, ImportAction.Skip),
            (plan["ApiToken"].Kind, plan["ApiToken"].Action)
        );
        Assert.Equal(ImportKind.Secret, plan["NewKey"].Kind);
        Assert.Equal(ImportAction.Excluded, plan["Region"].Action);
    }

    [Fact]
    public void ConfigurationValuesCompareTheirTypeToo()
    {
        var plan = ImportPlanner.Plan(
            [new ImportedValue("Mode", "safe", ConfigurationValueType.Multiline)],
            Existing,
            [],
            new Dictionary<string, ImportKind>(),
            new HashSet<string>(),
            overwrite: true
        );
        Assert.Equal(ImportAction.Update, plan[0].Action);
    }

    [Theory]
    [InlineData("DB_PASSWORD", "*password*", true)]
    [InlineData("ConnectionStrings:Default", "ConnectionStrings:*", true)]
    [InlineData("MyConnectionStrings:Default", "ConnectionStrings:*", false)]
    [InlineData("abc", "a*b*c", true)]
    [InlineData("acb", "a*b*c", false)]
    [InlineData("anything", "*", true)]
    [InlineData("Token", "Token", true)]
    [InlineData("Tokens", "Token", false)]
    public void Globs_MatchCaseInsensitively(string key, string glob, bool expected) =>
        Assert.Equal(expected, ImportPlanner.Matches(key, [glob]));
}
