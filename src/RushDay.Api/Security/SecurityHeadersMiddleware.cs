namespace RushDay.Api.Security;

/// <summary>
/// The response headers of 03-security.md section 4 on every response, static files and errors included. HSTS is
/// added separately by <c>UseHsts()</c> outside Development. <c>/api</c> responses are never stored by any cache.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    public const string ContentSecurityPolicyDevelopment =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

    public const string ContentSecurityPolicy = ContentSecurityPolicyDevelopment + "; upgrade-insecure-requests";

    public const string PermissionsPolicy = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

    private readonly string _csp = environment.IsDevelopment() ? ContentSecurityPolicyDevelopment : ContentSecurityPolicy;

    public Task InvokeAsync(HttpContext context)
    {
        // OnStarting callbacks run last-registered-first, so this one (registered first) has the final word, after
        // antiforgery's own Cache-Control and anything a handler set.
        context.Response.OnStarting(static state =>
        {
            var (httpContext, csp) = ((HttpContext, string))state;
            var headers = httpContext.Response.Headers;
            headers.ContentSecurityPolicy = csp;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = PermissionsPolicy;
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";

            if (httpContext.Request.Path.StartsWithSegments("/api"))
            {
                headers.CacheControl = "no-store";
                headers.Remove("Pragma");
            }

            return Task.CompletedTask;
        }, (context, _csp));

        return next(context);
    }
}
