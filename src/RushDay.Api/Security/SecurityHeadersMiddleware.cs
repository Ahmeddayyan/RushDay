using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Options;

namespace RushDay.Api.Security;

/// <summary>
/// The response headers of 03-security.md section 4 on every response, static files and errors included, and
/// <c>Strict-Transport-Security</c> outside Development. Everything is written in an <c>OnStarting</c> callback: the
/// exception handler clears the response headers before it writes a 500 or 503, and a header set on the way in (as
/// <c>UseHsts()</c> does) would be lost with them. HSTS follows <c>UseHsts()</c>'s rules: HTTPS requests only (after
/// forwarded headers), never for the excluded loopback hosts, value from <see cref="HstsOptions"/>. <c>/api</c>
/// responses are never stored by any cache.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    public const string ContentSecurityPolicyDevelopment =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

    public const string ContentSecurityPolicy = ContentSecurityPolicyDevelopment + "; upgrade-insecure-requests";

    public const string PermissionsPolicy = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

    private readonly RequestDelegate _next;
    private readonly string _csp;
    private readonly string? _hsts;
    private readonly HashSet<string> _hstsExcludedHosts;

    public SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment, IOptions<HstsOptions> hsts)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(hsts);

        _next = next;
        _csp = environment.IsDevelopment() ? ContentSecurityPolicyDevelopment : ContentSecurityPolicy;
        _hsts = environment.IsDevelopment() ? null : HstsValue(hsts.Value);
        _hstsExcludedHosts = new HashSet<string>(hsts.Value.ExcludedHosts, StringComparer.OrdinalIgnoreCase);
    }

    public static string HstsValue(HstsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = "max-age=" + (long)options.MaxAge.TotalSeconds;
        if (options.IncludeSubDomains)
        {
            value += "; includeSubDomains";
        }

        if (options.Preload)
        {
            value += "; preload";
        }

        return value;
    }

    public Task InvokeAsync(HttpContext context)
    {
        // OnStarting callbacks run last-registered-first, so this one (registered first) has the final word, after
        // antiforgery's own Cache-Control and anything a handler set.
        context.Response.OnStarting(static state =>
        {
            var (httpContext, middleware) = ((HttpContext, SecurityHeadersMiddleware))state;
            var headers = httpContext.Response.Headers;
            headers.ContentSecurityPolicy = middleware._csp;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = PermissionsPolicy;
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";

            if (middleware._hsts is { } hsts && httpContext.Request.IsHttps && !middleware._hstsExcludedHosts.Contains(httpContext.Request.Host.Host))
            {
                headers.StrictTransportSecurity = hsts;
            }

            if (httpContext.Request.Path.StartsWithSegments("/api"))
            {
                headers.CacheControl = "no-store";
                headers.Remove("Pragma");
            }

            return Task.CompletedTask;
        }, (context, this));

        return _next(context);
    }
}
