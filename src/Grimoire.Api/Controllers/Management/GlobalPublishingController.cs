using Grimoire.Api.Audit;
using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Audit;
using Grimoire.Core.Publishing;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Management;

/// <summary>
/// Auto-publish and explicit publishing of global configuration. There is one global sentinel,
/// <c>Global:Sentinel</c> at the <c>*</c> layer, so every application that inherits globals
/// sees it whichever environment-slug layer a change targeted.
/// </summary>
[ApiController]
[Route("management/global")]
[Tags("Management - Publishing")]
public class GlobalPublishingController(IPublishingService publishing) : ControllerBase
{
    /// <summary>Global auto-publish setting, current sentinel and unpublished changes.</summary>
    [HttpGet("publishing")]
    [ProducesResponseType<PublishingResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Ok(PublishingResponse.From(await publishing.GetGlobalAsync(ct)));

    /// <summary>Turns global auto-publish on or off. Turning it on publishes pending changes.</summary>
    [Audited(AuditActions.GlobalPublishingUpdate)]
    [HttpPut("publishing")]
    [ProducesResponseType<PublishingResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(
        [FromBody] UpdatePublishingRequest req,
        CancellationToken ct
    )
    {
        AuditContext.SetTarget(HttpContext, "global");
        return Ok(
            PublishingResponse.From(await publishing.SetGlobalAutoPublishAsync(req.AutoPublish, ct))
        );
    }

    /// <summary>
    /// Bumps <c>Global:Sentinel</c>: the clients of every application that inherits globals
    /// reload.
    /// </summary>
    [Audited(AuditActions.GlobalPublish)]
    [HttpPost("publish")]
    [ProducesResponseType<PublishingResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Publish(CancellationToken ct)
    {
        AuditContext.SetTarget(HttpContext, "global");
        return Ok(PublishingResponse.From(await publishing.PublishGlobalAsync(ct)));
    }
}
