using System.Diagnostics;
using RushDay.Api.Auth;
using RushDay.Api.Observability;

namespace RushDay.Api.Startup;

/// <summary>
/// One log line per request (03-security.md section 6, 04 section 7): <c>traceId</c>, <c>userId</c>, <c>role</c>, the
/// route <b>template</b> (never the raw path or query string), <c>statusCode</c> and <c>elapsedMs</c>; Information,
/// health checks at Debug. It also starts the request's <see cref="DbCommandCounter"/>. Nothing personal is logged:
/// no names, usernames, bodies, cookies, tokens or addresses.
/// </summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger, DbCommandCounter commandCounter)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        commandCounter.Reset();
        var started = Stopwatch.GetTimestamp();
        var failed = false;
        try
        {
            await next(context);
        }
        catch
        {
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
        var endpoint = context.GetEndpoint();
        var route = (endpoint as RouteEndpoint)?.RoutePattern.RawText
            ?? (context.Request.Path.StartsWithSegments("/api") ? "/api/{unmatched}" : "{static}");
        var level = context.Request.Path.StartsWithSegments("/api/health") ? LogLevel.Debug : LogLevel.Information;
        if (!logger.IsEnabled(level))
        {
            return;
        }

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
