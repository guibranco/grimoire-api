using System.Net;
using Grimoire.Api.Networking;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Grimoire.Tests;

public class ReverseProxyOptionsTests
{
    private static async Task<HttpContext> RunAsync(
        ReverseProxyOptions settings,
        string remoteIp,
        string? forwardedFor,
        string? forwardedProto = null
    )
    {
        var options = new ForwardedHeadersOptions();
        settings.Apply(options);

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        context.Request.Scheme = "http";
        if (forwardedFor is not null)
        {
            context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        }
        if (forwardedProto is not null)
        {
            context.Request.Headers["X-Forwarded-Proto"] = forwardedProto;
        }

        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            Options.Create(options)
        );
        await middleware.Invoke(context);
        return context;
    }

    [Fact]
    public async Task LoopbackProxy_UsesForwardedClientIp()
    {
        var context = await RunAsync(
            new ReverseProxyOptions(),
            "127.0.0.1",
            "203.0.113.7",
            "https"
        );

        Assert.Equal(IPAddress.Parse("203.0.113.7"), context.Connection.RemoteIpAddress);
        Assert.Equal("https", context.Request.Scheme);
    }

    [Fact]
    public async Task UntrustedProxy_KeepsConnectionIp()
    {
        var context = await RunAsync(new ReverseProxyOptions(), "198.51.100.20", "203.0.113.7");

        Assert.Equal(IPAddress.Parse("198.51.100.20"), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task KnownProxy_IsTrusted()
    {
        var settings = new ReverseProxyOptions { KnownProxies = ["10.0.0.5"] };

        var context = await RunAsync(settings, "10.0.0.5", "203.0.113.7");

        Assert.Equal(IPAddress.Parse("203.0.113.7"), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task SpoofedHeaderBeforeTrustedHop_IsIgnored()
    {
        // The client sent its own X-Forwarded-For; nginx appended the real address.
        var context = await RunAsync(
            new ReverseProxyOptions(),
            "127.0.0.1",
            "1.2.3.4, 203.0.113.7"
        );

        Assert.Equal(IPAddress.Parse("203.0.113.7"), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task CloudflareThenNginx_UnwindsBothHops()
    {
        var settings = new ReverseProxyOptions
        {
            KnownNetworks = ["173.245.48.0/20"],
            ForwardLimit = 2,
        };

        var context = await RunAsync(settings, "127.0.0.1", "203.0.113.7, 173.245.48.10");

        Assert.Equal(IPAddress.Parse("203.0.113.7"), context.Connection.RemoteIpAddress);
    }

    [Theory]
    [InlineData(new[] { "not-an-ip" }, new string[0], 1)]
    [InlineData(new string[0], new[] { "10.0.0.0/99" }, 1)]
    [InlineData(new string[0], new string[0], 0)]
    public void InvalidSettings_Throw(string[] proxies, string[] networks, int limit)
    {
        var settings = new ReverseProxyOptions
        {
            KnownProxies = proxies,
            KnownNetworks = networks,
            ForwardLimit = limit,
        };

        Assert.Throws<InvalidOperationException>(() =>
            settings.Apply(new ForwardedHeadersOptions())
        );
    }
}
