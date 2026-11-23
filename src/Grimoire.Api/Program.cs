using FluentValidation;
using FluentValidation.AspNetCore;
using Grimoire.Api;
using Grimoire.Api.AccessTokens;
using Grimoire.Api.Audit;
using Grimoire.Api.Backups;
using Grimoire.Api.ConsumerTracking;
using Grimoire.Api.Logging;
using Grimoire.Api.Middleware;
using Grimoire.Api.Oidc;
using Grimoire.Api.Rotation;
using Grimoire.Api.Swagger;
using Grimoire.Core.Entities;
using Grimoire.Infrastructure;
using Grimoire.Infrastructure.Backups;
using Grimoire.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;

BackupCommand? backupCommand;
try
{
    (backupCommand, args) = BackupCommandLine.Parse(args);
}
catch (ArgumentException ex)
{
    await Console.Error.WriteLineAsync($"❌ {ex.Message}");
    Environment.ExitCode = 2;
    return;
}

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

Log.Information("LogStream shipping is {LogStreamState}", logStreamOptions.State);

builder.Host.UseSerilog();

builder
    .Services.AddControllers()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    );

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddScoped<IPasswordHasher<Application>, PasswordHasher<Application>>();
builder.Services.AddScoped<IPasswordHasher<AccessToken>, PasswordHasher<AccessToken>>();
builder.Services.AddSingleton<AccessTokenUsageThrottle>();
builder.Services.AddSingleton<IssuedTokenCleanup>();
builder.Services.AddScoped<AccessTokenFactory>();
builder.Services.AddSingleton<Grimoire.Api.Import.Importer>();
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IConfiguration>()
        .GetSection(GitHubOidcOptions.SectionName)
        .Get<GitHubOidcOptions>()
    ?? new GitHubOidcOptions()
);
builder.Services.AddSingleton<IGitHubOidcKeyProvider>(sp => new GitHubOidcKeyProvider(
    sp.GetRequiredService<GitHubOidcOptions>(),
    sp.GetRequiredService<TimeProvider>()
));
builder.Services.AddScoped<GitHubOidcValidator>();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<BackupScheduler>();
builder.Services.AddSingleton<ConsumerActivityFlusher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ConsumerActivityFlusher>());
builder.Services.AddSingleton<AuditWriter>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AuditWriter>());
builder.Services.AddSingleton<AuditRetentionService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AuditRetentionService>());
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IConfiguration>()
        .GetSection(RotationReminderOptions.SectionName)
        .Get<RotationReminderOptions>()
    ?? new RotationReminderOptions()
);
builder.Services.AddHttpClient(RotationReminderNotifier.HttpClientName);
builder.Services.AddScoped<RotationReminderNotifier>();
builder.Services.AddHostedService<RotationReminderService>();

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

// The server and --backup create backups, so they must have a valid backup configuration.
// --restore and --verify-backup only read existing files and stay available regardless.
var createsBackups = backupCommand is null or { Kind: BackupCommandKind.Backup };
var backupOptions = app.Services.GetRequiredService<BackupOptions>();
var backupErrors = createsBackups ? backupOptions.Validate().ToList() : [];
if (
    createsBackups
    && backupOptions.Enabled
    && backupOptions.Key == app.Configuration["Encryption:MasterKey"]
)
    backupErrors.Add("'Backup:Key' must differ from 'Encryption:MasterKey'.");
if (backupErrors.Count > 0)
{
    var message = "Invalid backup configuration: " + string.Join(" ", backupErrors);
    if (backupCommand is null)
        throw new InvalidOperationException(message);

    // Maintenance modes obey the same rules (key separation included), but fail with a message.
    await Console.Error.WriteLineAsync($"❌ {message}");
    Environment.ExitCode = 2;
    await Log.CloseAndFlushAsync();
    return;
}

// Backup maintenance modes run before migrations: a restore must find the database untouched.
if (backupCommand is not null)
{
    try
    {
        Environment.ExitCode = await BackupCommandLine.RunAsync(backupCommand, app.Services);
    }
    finally
    {
        await Log.CloseAndFlushAsync();
    }
    return;
}

if (app.Environment.IsProduction())
{
    EnsureStrongSecret(app.Configuration, "Management:AdminApiKey", 32);
    EnsureStrongSecret(app.Configuration, "Encryption:MasterKey", 32);
}

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();
    await db.Database.MigrateAsync();
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
    await app.RunAsync();
}
finally
{
    // Flushes batched sinks (LogStream) so the last events before shutdown are not lost.
    await Log.CloseAndFlushAsync();
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
