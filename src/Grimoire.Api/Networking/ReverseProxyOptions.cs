using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Grimoire.Api.Networking;

/// <summary>
/// Settings for running behind a reverse proxy (section <c>ReverseProxy</c>).
/// When enabled, the client IP and scheme are taken from <c>X-Forwarded-For</c> /
/// <c>X-Forwarded-Proto</c>, but only when the request comes from a trusted proxy.
/// Loopback (<c>127.0.0.0/8</c>, <c>::1</c>) is always trusted, so a proxy on the same
/// host works with no extra settings.
/// </summary>
public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    /// <summary>Set to <c>false</c> to ignore forwarded headers (Kestrel exposed directly).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Extra trusted proxy addresses, e.g. <c>10.0.0.5</c>.</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Extra trusted proxy networks in CIDR notation, e.g. <c>173.245.48.0/20</c>.</summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>
    /// How many proxy hops to unwind from <c>X-Forwarded-For</c>. Each hop must be trusted.
    /// Use <c>2</c> for Cloudflare → nginx → Grimoire with the Cloudflare ranges in
    /// <see cref="KnownNetworks"/>.
    /// </summary>
    public int ForwardLimit { get; set; } = 1;

    /// <summary>Copies these settings onto ASP.NET Core's <see cref="ForwardedHeadersOptions"/>.</summary>
    public void Apply(ForwardedHeadersOptions options)
    {
        if (ForwardLimit < 1)
        {
            throw new InvalidOperationException($"{SectionName}:ForwardLimit must be at least 1.");
        }

        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = ForwardLimit;

        foreach (var proxy in KnownProxies)
        {
            if (!IPAddress.TryParse(proxy, out var address))
            {
                throw new InvalidOperationException(
                    $"{SectionName}:KnownProxies contains an invalid IP address: '{proxy}'."
                );
            }

            options.KnownProxies.Add(address);
        }

        foreach (var network in KnownNetworks)
        {
            if (!System.Net.IPNetwork.TryParse(network, out var parsed))
            {
                throw new InvalidOperationException(
                    $"{SectionName}:KnownNetworks contains an invalid CIDR network: '{network}'."
                );
            }

            options.KnownIPNetworks.Add(parsed);
        }
    }
}
