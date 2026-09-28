using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RushDay.Infrastructure.Seeding;

namespace RushDay.IntegrationTests.Security;

/// <summary>The exact headers of 03-security.md section 4 and the cookie names of D4 (T2, T9, T20).</summary>
[Collection(ApiCollection.Name)]
public sealed class SecurityHeadersTests(RushDayApiFactory factory)
{
    private const string Csp = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'; upgrade-insecure-requests";
    private const string DevelopmentCsp = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

    public static TheoryData<string> Paths => new() { "/", "/api", "/api/nope", "/api/health/live", "/api/public/status", "/student/results" };

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Every_response_carries_the_security_headers(string path)
    {
        using var client = factory.CreateCookieClient();

        using var response = await client.GetAsync(path);

        AssertCommonHeaders(response, DevelopmentCsp);
        Assert.False(response.Headers.Contains("Strict-Transport-Security"), "No HSTS in Development.");
        if (path.StartsWith("/api", StringComparison.Ordinal))
        {
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        }
    }

    [Fact]
    public async Task Production_adds_hsts_and_upgrade_insecure_requests()
    {
        await using var production = factory.Production();
        using var client = production.CreateCookieClient(baseAddress: TestClients.ProductionHttps);

        using var response = await client.GetAsync("/api");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertCommonHeaders(response, Csp);
        Assert.Equal("max-age=31536000", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Production_cookie_names_have_host_prefix()
    {
        await using var production = factory.Production();
        using var client = production.CreateCookieClient(baseAddress: TestClients.ProductionHttps);

        using (var csrf = await client.GetAsync("/api/auth/csrf"))
        {
            AssertHostCookie(Assert.Single(csrf.SetCookieHeaders()), "__Host-rushday.csrf");
        }

        await client.RefreshCsrfAsync();
        using var login = await client.PostLoginAsync("S000010", DemoAccounts.StudentPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        AssertHostCookie(Assert.Single(login.SetCookieHeaders(), c => c.Contains("rushday.auth", StringComparison.Ordinal)), "__Host-rushday.auth");

        var cookies = production.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        Assert.Equal("__Host-rushday.mfa", cookies.Get(IdentityConstants.TwoFactorUserIdScheme).Cookie.Name);
        Assert.Equal("__Host-rushday.auth", cookies.Get(IdentityConstants.ApplicationScheme).Cookie.Name);
    }

    [Fact]
    public void Development_cookie_names_have_no_prefix()
    {
        var cookies = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();

        Assert.Equal("rushday.auth", cookies.Get(IdentityConstants.ApplicationScheme).Cookie.Name);
        Assert.Equal("rushday.mfa", cookies.Get(IdentityConstants.TwoFactorUserIdScheme).Cookie.Name);
        Assert.Equal(TimeSpan.FromHours(12), cookies.Get(IdentityConstants.ApplicationScheme).ExpireTimeSpan);
        Assert.False(cookies.Get(IdentityConstants.ApplicationScheme).SlidingExpiration);
        Assert.Equal(TimeSpan.FromMinutes(5), cookies.Get(IdentityConstants.TwoFactorUserIdScheme).ExpireTimeSpan);
    }

    [Fact]
    public async Task Static_files_are_cached_by_fingerprint()
    {
        var webRoot = factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider;
        var asset = webRoot.GetDirectoryContents("assets").FirstOrDefault(f => !f.IsDirectory && !f.Name.EndsWith(".gz", StringComparison.Ordinal) && !f.Name.EndsWith(".br", StringComparison.Ordinal));
        var favicon = webRoot.GetFileInfo("favicon.svg");
        using var client = factory.CreateCookieClient();

        if (asset is not null)
        {
            using var response = await client.GetAsync("/assets/" + asset.Name);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
            AssertCommonHeaders(response, DevelopmentCsp);
        }

        if (favicon.Exists)
        {
            using var response = await client.GetAsync("/favicon.svg");
            Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
        }
    }

    private static void AssertCommonHeaders(HttpResponseMessage response, string csp)
    {
        Assert.Equal(csp, Header(response, "Content-Security-Policy"));
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("strict-origin-when-cross-origin", Header(response, "Referrer-Policy"));
        Assert.Equal("camera=(), microphone=(), geolocation=(), payment=(), usb=()", Header(response, "Permissions-Policy"));
        Assert.Equal("same-origin", Header(response, "Cross-Origin-Opener-Policy"));
        Assert.Equal("same-origin", Header(response, "Cross-Origin-Resource-Policy"));
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values)
            ? Assert.Single(values)
            : throw new Xunit.Sdk.XunitException($"Missing header {name} on {response.RequestMessage?.RequestUri}.");

    private static void AssertHostCookie(string setCookie, string name)
    {
        Assert.StartsWith(name + "=", setCookie, StringComparison.Ordinal);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase);
    }
}
