using System.Globalization;
using Grimoire.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers;

/// <summary>
/// Public liveness/readiness probe, following the logstream-server convention:
/// <c>GET /health</c> → <c>{ "status": "ok", "time": "&lt;RFC 3339&gt;", ...components }</c>.
/// Answers 503 with <c>"status": "error"</c> when a component is unavailable.
/// </summary>
[ApiController]
[Route("health")]
[Tags("Health")]
public class HealthController(GrimoireDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        bool databaseOk;
        try
        {
            databaseOk = await db.Database.CanConnectAsync(ct);
        }
        catch (Exception)
        {
            databaseOk = false;
        }

        var status = databaseOk ? "ok" : "error";
        var body = new
        {
            status,
            version = AppVersion.Current,
            time =DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture),
            database = new { status },
        };

        return databaseOk ? Ok(body) : StatusCode(StatusCodes.Status503ServiceUnavailable, body);
    }
}
