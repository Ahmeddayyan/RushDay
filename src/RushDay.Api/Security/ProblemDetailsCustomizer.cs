using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using RushDay.Api.Observability;

namespace RushDay.Api.Security;

/// <summary>
/// <c>AddProblemDetails(o =&gt; o.CustomizeProblemDetails = ProblemDetailsCustomizer.Customize)</c> (02-api.md section
/// 6): every problem gets <c>traceId</c>; a problem the framework produced (validation, 404 routing, 405, 413, 415,
/// status-code pages, the exception handler) gets the catalogue <c>type</c> for its status; validation error keys are
/// camelCased like the JSON they describe.
/// </summary>
public static class ProblemDetailsCustomizer
{
    public const string TraceIdExtension = "traceId";

    public static void Customize(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Apply(context.HttpContext, context.ProblemDetails);
    }

    public static void Apply(HttpContext httpContext, ProblemDetails problem)
    {
        var status = problem.Status ?? httpContext.Response.StatusCode;
        problem.Status = status;

        if (problem.Type is null || !problem.Type.StartsWith(ProblemTypes.UrnPrefix, StringComparison.Ordinal))
        {
            var slug = ProblemTypes.ForStatus(status);
            problem.Type = ProblemTypes.Urn(slug);
            problem.Title = ProblemTypes.TitleOf(slug);

            // Framework text can carry exception or type names; the caller gets safe copy only.
            problem.Detail = slug == ProblemTypes.Validation ? "One or more fields are invalid." : ProblemResults.Create(slug).Detail;
        }

        problem.Instance ??= httpContext.Request.Path.Value;
        problem.Extensions[TraceIdExtension] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        if (problem is HttpValidationProblemDetails validation && validation.Errors.Count > 0)
        {
            var camelCased = validation.Errors.ToDictionary(e => CamelCase(e.Key), e => e.Value, StringComparer.Ordinal);
            validation.Errors.Clear();
            foreach (var (key, value) in camelCased)
            {
                validation.Errors[key] = value;
            }
        }
    }

    /// <summary><c>Password</c> → <c>password</c>, <c>Rows[0].Mark</c> → <c>rows[0].mark</c>.</summary>
    public static string CamelCase(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return key;
        }

        var parts = key.Split('.');
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0 && char.IsUpper(parts[i][0]))
            {
                parts[i] = char.ToLowerInvariant(parts[i][0]) + parts[i][1..];
            }
        }

        return string.Join('.', parts);
    }
}

/// <summary>
/// A transient <see cref="NpgsqlException"/> (pool wait timeout, a command timeout, a connection that died during a
/// Neon suspend, 53300) becomes 503 <c>server-busy</c> with <c>Retry-After: 2</c> instead of a 500
/// (04-performance-and-ops.md section 5); <c>rushday.db.pool_wait_timeouts</c> counts only the pool-wait case, the
/// exception Npgsql throws when no pooled connection became free within <c>Timeout</c>
/// (<see cref="IsPoolExhaustion"/>), never a command that timed out waiting on a lock. Text PostgreSQL cannot store
/// (SQLSTATE 22021, a NUL character) that slipped past validation is 400 <c>validation</c>, not a 500. Anything else
/// falls through to the default handler: 500 <c>internal-error</c> with the <c>traceId</c> and no exception text.
/// </summary>
public sealed class RushDayExceptionHandler(RushDayMetrics metrics, ILogger<RushDayExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>The start of the message of Npgsql's pool-exhaustion exception (Npgsql 10, <c>PoolingDataSource</c>).</summary>
    public const string PoolExhaustedMessage = "The connection pool has been exhausted";

    /// <summary>SQLSTATE 22021 <c>character_not_in_repertoire</c>: PostgreSQL rejects a NUL byte in text.</summary>
    public const string CharacterNotInRepertoire = "22021";

    /// <summary>True only for the pool-wait timeout: a transient exception whose inner exception is a timeout and whose message is Npgsql's pool-exhaustion text.</summary>
    public static bool IsPoolExhaustion(NpgsqlException exception) =>
        exception is { InnerException: TimeoutException } && exception.Message.StartsWith(PoolExhaustedMessage, StringComparison.Ordinal);

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        if (Find<PostgresException>(exception) is { SqlState: CharacterNotInRepertoire })
        {
            logger.LogInformation("Request text PostgreSQL cannot store (SQLSTATE 22021) answered 400 validation.");
            await ProblemResults.WriteAsync(httpContext, StatusCodes.Status400BadRequest, ProblemTypes.Validation);
            return true;
        }

        var npgsql = Find<NpgsqlException>(exception);
        if (npgsql is not { IsTransient: true })
        {
            return false;
        }

        if (IsPoolExhaustion(npgsql))
        {
            metrics.PoolWaitTimeout();
        }

        logger.LogWarning("Transient database failure answered 503 server-busy: {Kind}", npgsql.InnerException?.GetType().Name ?? npgsql.GetType().Name);
        await ProblemResults.WriteAsync(httpContext, StatusCodes.Status503ServiceUnavailable, ProblemTypes.ServerBusy, retryAfterSeconds: 2);
        return true;
    }

    private static T? Find<T>(Exception? exception)
        where T : Exception
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }
}
