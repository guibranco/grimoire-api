using Grimoire.Api.Swagger;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Grimoire.Tests;

public class SecurityRequirementsOperationFilterTests
{
    private static OperationFilterContext CreateContext(string? relativePath) =>
        new(
            new ApiDescription { RelativePath = relativePath },
            null!,
            new SchemaRepository(),
            new OpenApiDocument(),
            typeof(SecurityRequirementsOperationFilterTests).GetMethod(
                nameof(CreateContext),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
            )!
        );

    [Theory]
    [InlineData("management/applications", SecurityRequirementsOperationFilter.BearerScheme)]
    [InlineData("Management/environments/{id}", SecurityRequirementsOperationFilter.BearerScheme)]
    [InlineData("consumer/secrets/{name}", SecurityRequirementsOperationFilter.ApiKeyScheme)]
    [InlineData("CONSUMER/configurations", SecurityRequirementsOperationFilter.ApiKeyScheme)]
    public void Apply_AddsSchemeMatchingRoutePrefix(string path, string expectedScheme)
    {
        var operation = new OpenApiOperation();

        new SecurityRequirementsOperationFilter().Apply(operation, CreateContext(path));

        var requirement = Assert.Single(operation.Security!);
        var scheme = Assert.Single(requirement.Keys);
        Assert.Equal(expectedScheme, scheme.Reference.Id);
    }

    [Theory]
    [InlineData("health")]
    [InlineData("")]
    [InlineData(null)]
    public void Apply_LeavesUnprotectedRoutesAlone(string? path)
    {
        var operation = new OpenApiOperation();

        new SecurityRequirementsOperationFilter().Apply(operation, CreateContext(path));

        Assert.Null(operation.Security);
    }
}
