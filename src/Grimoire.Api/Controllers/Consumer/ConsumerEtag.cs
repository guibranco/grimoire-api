using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Grimoire.Api.Controllers.Consumer;

/// <summary>Strong ETags for Consumer API responses that pollers re-read.</summary>
internal static class ConsumerEtag
{
    /// <summary>
    /// Sets an <c>ETag</c> over <paramref name="body"/> and reports whether the request's
    /// <c>If-None-Match</c> already names it (the caller then answers <c>304</c>).
    /// </summary>
    public static bool IsCurrent(ControllerBase controller, byte[] body)
    {
        var hash = SHA256.HashData(body);
        var etag = new EntityTagHeaderValue($"\"{Convert.ToHexStringLower(hash)[..32]}\"");
        controller.Response.Headers.ETag = etag.ToString();
        var ifNoneMatch = controller.Request.GetTypedHeaders().IfNoneMatch;
        return ifNoneMatch.Any(t => t.Tag == etag.Tag || t.Equals(EntityTagHeaderValue.Any));
    }
}
