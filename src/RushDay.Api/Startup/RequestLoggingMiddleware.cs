using System.Diagnostics;
using RushDay.Api.Auth;
using RushDay.Api.Observability;

namespace RushDay.Api.Startup;

/// <summary>
/// One log line per request (03-security.md section 6, 04 section 7): <c>traceId</c>, <c>userId</c>, <c>role</c>, the
/// route <b>template</b> (never the raw path or query string), <c>statusCode</c> and <c>elapsedMs</c>; Information,
/// health checks at Debug. Nothing personal is logged: no names, usernames, bodies, cookies, tokens or addresses.
/// </summary>
/// <remarks>
/// It sits outside <c>UseExceptionHandler</c>, so the status it logs is the one the client received: 503 for a
/// transient database failure, 499 for a request the client abandoned, 500 only for a real failure. The exception
/// handler clears the endpoint before it writes, so the route template is taken from what
/// <see cref="EndpointCommandCounterMiddleware"/> recorded.
/// </remarks>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    /// <summary>The <c>HttpContext.Items</c> key holding the matched route template.</summary>
    public const string RouteItem = "RushDay.Route";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var started = Stopwatch.GetTimestamp();
        var failed = false;
        try
        {
            await next(context);
        }
        catch
        {
            // Only what the exception handler could not answer (the response had already started) reaches here.
            failed = true;
            throw;
        }
        finally
        {
            Log(context, Stopwatch.GetElapsedTime(started).TotalMilliseconds, failed);
        }
    }

    private void Log(HttpContext context, double elapsedMs, bool failed)
    {
        var level = context.Request.Path.StartsWithSegments("/api/health") ? LogLevel.Debug : LogLevel.Information;
        if (!logger.IsEnabled(level))
        {
            return;
        }

        var route = context.Items[RouteItem] as string
            ?? (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
            ?? (context.Request.Path.StartsWithSegments("/api") ? "/api/{unmatched}" : "{static}");
        var statusCode = failed ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
        var user = context.User;
        logger.Log(
            level,
            "HTTP {method} {route} responded {statusCode} in {elapsedMs} ms (traceId {traceId}, userId {userId}, role {role})",
            context.Request.Method,
            route,
            statusCode,
            Math.Round(elapsedMs, 1),
            Activity.Current?.Id ?? context.TraceIdentifier,
            user.FindFirst(RushDayClaims.Subject)?.Value,
            user.FindFirst(RushDayClaims.Role)?.Value);
    }
}

/// <summary>
/// The last middleware before the endpoint (after authentication, rate limiting, authorization and the output cache):
/// starts the request's <see cref="DbCommandCounter"/> so it measures the endpoint's own commands only (the
/// security-stamp re-check of the authentication step is not the handler's), and records the matched route template
/// for <see cref="RequestLoggingMiddleware"/>.
/// </summary>
public sealed class EndpointCommandCounterMiddleware(RequestDelegate next, DbCommandCounter commandCounter)
{
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { } route })
        {
            context.Items[RequestLoggingMiddleware.RouteItem] = route;
        }

        commandCounter.Reset();
        return next(context);
    }
}
