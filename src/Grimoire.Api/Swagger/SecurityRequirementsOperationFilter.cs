using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Grimoire.Api.Swagger;

/// <summary>
/// Marks each operation with the security scheme enforced by the matching middleware,
/// so Swagger UI sends the right credentials when using "Try it out".
/// </summary>
public class SecurityRequirementsOperationFilter : IOperationFilter
{
    public const string BearerScheme = "Bearer";
    public const string ApiKeyScheme = "ApiKey";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath ?? string.Empty;

        string? scheme = null;
        if (path.StartsWith("management", StringComparison.OrdinalIgnoreCase))
            scheme = BearerScheme;
        else if (path.StartsWith("consumer", StringComparison.OrdinalIgnoreCase))
            scheme = ApiKeyScheme;

        if (scheme is null)
            return;

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(scheme, context.Document)] = [],
            },
        ];
    }
}
