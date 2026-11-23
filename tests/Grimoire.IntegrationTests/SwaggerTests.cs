using System.Net;
using System.Text.Json.Nodes;

namespace Grimoire.IntegrationTests;

public class SwaggerTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    [Theory]
    [InlineData("/management/global/configurations")]
    [InlineData("/management/global/configurations/{environmentSlug}/{key}")]
    [InlineData("/management/global/secrets")]
    [InlineData("/management/global/secrets/{name}/values")]
    [InlineData("/management/global/secrets/{name}/versions/{environmentSlug}")]
    [InlineData("/management/global/environments")]
    [InlineData("/management/applications/{slug}/configurations/effective")]
    [InlineData("/management/applications/{slug}/secrets/effective")]
    [InlineData("/management/dashboard")]
    [InlineData("/management/search")]
    [InlineData("/management/audit")]
    [InlineData("/management/secrets/expiring")]
    [InlineData("/management/applications/{slug}/configurations/diff")]
    [InlineData("/management/applications/{slug}/configurations/{environmentSlug}/{key}/history")]
    [InlineData("/management/global/configurations/{environmentSlug}/{key}/history")]
    [InlineData("/management/secrets/expiring/notify")]
    [InlineData("/management/reports/unused-secrets")]
    [InlineData("/management/reports/dependencies")]
    [InlineData("/management/applications/{slug}/feature-flags")]
    [InlineData("/management/applications/{slug}/feature-flags/{key}")]
    [InlineData(
        "/management/applications/{slug}/feature-flags/{key}/environments/{environmentSlug}"
    )]
    [InlineData(
        "/management/applications/{slug}/feature-flags/{key}/environments/{environmentSlug}/toggle"
    )]
    [InlineData("/management/applications/{slug}/feature-flags/{key}/evaluate")]
    [InlineData("/management/applications/{slug}/feature-flags/{key}/history")]
    [InlineData(
        "/management/applications/{slug}/secrets/{name}/versions/{environmentSlug}/{version}"
    )]
    [InlineData("/management/global/secrets/{name}/versions/{environmentSlug}/{version}")]
    public async Task ManagementDocument_IncludesGlobalRoutes(string path)
    {
        var response = await factory.CreateClient().GetAsync("/swagger/management/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.NotNull(doc["paths"]![path]);
    }
}
