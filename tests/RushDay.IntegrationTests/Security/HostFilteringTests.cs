using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace RushDay.IntegrationTests.Security;

/// <summary>Host filtering and forwarded headers (03-security.md section 3, T20).</summary>
[Collection(ApiCollection.Name)]
public sealed class HostFilteringTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task Production_accepts_only_the_configured_hosts()
    {
        await using var production = factory.Production(b => b.UseSetting("Security:AllowedHosts", "portal.example.ac.uk"));

        using var allowed = production.CreateCookieClient(baseAddress: new Uri("https://portal.example.ac.uk"));
        using var ok = await allowed.GetAsync("/api/health/live");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        using var other = production.CreateCookieClient(baseAddress: new Uri("https://evil.example"));
        using var refused = await other.GetAsync("/api/health/live");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task Production_falls_back_to_the_render_hostname()
    {
        await using var production = factory.Production(b => b.UseSetting("RENDER_EXTERNAL_HOSTNAME", "rushday-api.onrender.com"));

        using var allowed = production.CreateCookieClient(baseAddress: new Uri("https://rushday-api.onrender.com"));
        using var ok = await allowed.GetAsync("/api/health/live");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        using var other = production.CreateCookieClient(baseAddress: new Uri("https://rushday.test"));
        using var refused = await other.GetAsync("/api/health/live");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task Development_accepts_any_host()
    {
        using var client = factory.CreateCookieClient(baseAddress: new Uri("http://anything.example"));

        using var response = await client.GetAsync("/api/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Forwarded_headers_are_trusted_only_when_configured(bool trusted)
    {
        // TestServer has no peer address; give every request a non-loopback one, as Render's proxy would be.
        await using var production = factory.Production(b =>
        {
            b.UseSetting("Security:TrustForwardedHeaders", trusted ? "true" : "false");
            b.ConfigureTestServices(s => s.AddTransient<IStartupFilter, SimulatedPeer>());
        });
        using var client = production.CreateCookieClient(baseAddress: new Uri("http://rushday.test"));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health/live");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add(TestClients.ForwardedForHeader, "203.0.113.9");
        using var response = await client.SendAsync(request);

        // HSTS is only sent on HTTPS requests, so it shows whether X-Forwarded-Proto was believed.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(trusted, response.Headers.Contains("Strict-Transport-Security"));
    }

    private sealed class SimulatedPeer : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.7");
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
