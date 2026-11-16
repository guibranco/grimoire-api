using FluentValidation;
using FluentValidation.AspNetCore;
using Grimoire.Api;
using Grimoire.Api.Logging;
using Grimoire.Api.Middleware;
using Grimoire.Api.Swagger;
using Grimoire.Core.Entities;
using Grimoire.Infrastructure;
using Grimoire.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var logStreamOptions = builder
    .Configuration.GetSection(LogStreamOptions.SectionName)
    .Get<LogStreamOptions>()
    ?? new LogStreamOptions();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/grimoire-.log", rollingInterval: RollingInterval.Day)
    .WriteToLogStream(logStreamOptions)
    .CreateLogger();

Log.Information(
    "LogStream shipping is {LogStreamState}",
    logStreamOptions.IsConfigured ? "enabled"
        : logStreamOptions.Enabled ? "not configured"
        : "disabled"
);

builder.Host.UseSerilog();

builder
    .Services.AddControllers()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    );

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddScoped<IPasswordHasher<Application>, PasswordHasher<Application>>();

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc(
        "management",
        new() { Title = "Grimoire Management API", Version = AppVersion.Current }
    );
    c.SwaggerDoc(
        "consumer",
        new() { Title = "Grimoire Consumer API", Version = AppVersion.Current }
    );
    c.DocInclusionPredicate(
        (docName, apiDesc) =>
        {
            var tags = apiDesc
                .ActionDescriptor.EndpointMetadata.OfType<TagsAttribute>()
                .SelectMany(t => t.Tags);
            return docName == "management"
                ? tags.Any(t => t.StartsWith("Management"))
                : tags.Any(t => t.StartsWith("Consumer"));
        }
    );
    c.AddSecurityDefinition(
        "Bearer",
        new Microsoft.OpenApi.OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = Microsoft.OpenApi.SecuritySchemeType.Http,
            Scheme = "bearer",
        }
    );
    c.AddSecurityDefinition(
        "ApiKey",
        new Microsoft.OpenApi.OpenApiSecurityScheme
        {
            Name = "X-Api-Key",
            Type = Microsoft.OpenApi.SecuritySchemeType.ApiKey,
            In = Microsoft.OpenApi.ParameterLocation.Header,
        }
    );
    c.OperationFilter<SecurityRequirementsOperationFilter>();
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()
    )
);

builder.Services.AddProblemDetails();

var app = builder.Build();

if (app.Environment.IsProduction())
{
    EnsureStrongSecret(app.Configuration, "Management:AdminApiKey", 32);
    EnsureStrongSecret(app.Configuration, "Encryption:MasterKey", 32);
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();
    db.Database.Migrate();
}

app.UseExceptionHandler();
app.UseSerilogRequestLogging();

// Serves the Grimoire favicon and Swagger UI branding from wwwroot. Runs before
// UseSwaggerUI so wwwroot/swagger/favicon-*.png replace the embedded Swagger icons.
app.UseStaticFiles();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.DocumentTitle = $"Grimoire API {AppVersion.Current}";
    c.InjectStylesheet("grimoire.css");
    c.SwaggerEndpoint("/swagger/management/swagger.json", "Management API");
    c.SwaggerEndpoint("/swagger/consumer/swagger.json", "Consumer API");
});

app.UseCors();
app.UseMiddleware<AdminApiKeyMiddleware>();
app.UseMiddleware<ConsumerApiKeyMiddleware>();
app.MapControllers();

try
{
    app.Run();
}
finally
{
    // Flushes batched sinks (LogStream) so the last events before shutdown are not lost.
    Log.CloseAndFlush();
}

static void EnsureStrongSecret(IConfiguration configuration, string key, int minLength)
{
    var value = configuration[key];
    if (
        string.IsNullOrWhiteSpace(value)
        || value.Length < minLength
        || value.StartsWith("change-me", StringComparison.OrdinalIgnoreCase)
    )
    {
        throw new InvalidOperationException(
            $"'{key}' must be set to a non-default value of at least {minLength} characters in Production."
        );
    }
}

public partial class Program { }
