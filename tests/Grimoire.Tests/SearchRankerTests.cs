using Grimoire.Core.Search;
using Xunit;

namespace Grimoire.Tests;

public class SearchRankerTests
{
    private static SearchHit Hit(
        string type,
        string name,
        string? app = "app",
        string? description = null
    ) => new(type, name, app, [], description);

    [Fact]
    public void Rank_OrdersExactThenPrefixThenContainsThenDescription()
    {
        var hits = new[]
        {
            Hit(SearchResultTypes.Configuration, "MY_SMTP_HOST"),
            Hit(SearchResultTypes.Configuration, "PORT", description: "smtp port"),
            Hit(SearchResultTypes.Configuration, "SMTP_HOST"),
            Hit(SearchResultTypes.Secret, "smtp"),
        };

        var result = SearchRanker.Rank("smtp", hits, 10);

        Assert.Equal(
            ["smtp", "SMTP_HOST", "MY_SMTP_HOST", "PORT"],
            result.Results.Select(r => r.Name)
        );
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Rank_TiesGoToApplicationsFirst_ThenByType()
    {
        var hits = new[]
        {
            Hit(SearchResultTypes.GlobalConfiguration, "billing", app: null),
            Hit(SearchResultTypes.Secret, "billing"),
            Hit(SearchResultTypes.Application, "Billing", app: "billing"),
        };

        var result = SearchRanker.Rank("billing", hits, 10);

        Assert.Equal(
            [
                SearchResultTypes.Application,
                SearchResultTypes.Secret,
                SearchResultTypes.GlobalConfiguration,
            ],
            result.Results.Select(r => r.Type)
        );
    }

    [Fact]
    public void Rank_MatchesApplicationsOnSlugToo()
    {
        var hits = new[]
        {
            Hit(SearchResultTypes.Application, "Payslip Archiver", "payslip-archiver"),
        };

        var result = SearchRanker.Rank("payslip-arch", hits, 10);

        Assert.Single(result.Results);
    }

    [Fact]
    public void Rank_DropsNonMatches_AndReportsTruncation()
    {
        var hits = Enumerable
            .Range(0, 5)
            .Select(i => Hit(SearchResultTypes.Configuration, $"KEY_{i}"))
            .Append(Hit(SearchResultTypes.Configuration, "UNRELATED"));

        var result = SearchRanker.Rank("key", hits, 3);

        Assert.Equal(3, result.Results.Count);
        Assert.True(result.Truncated);
        Assert.DoesNotContain(result.Results, r => r.Name == "UNRELATED");
    }

    [Fact]
    public void Rank_TrimsAndIgnoresCase()
    {
        var result = SearchRanker.Rank(
            "  Db_Pass ",
            [Hit(SearchResultTypes.Secret, "DB_PASSWORD")],
            10
        );

        Assert.Equal("Db_Pass", result.Query);
        Assert.Single(result.Results);
    }
}
