using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RushDay.Infrastructure.Caching;

namespace RushDay.Api.Observability;

/// <summary>
/// Counts the database commands a request's endpoint executes (04-performance-and-ops.md section 6.1), so
/// <c>rushday.dashboard.queries</c> is measured rather than asserted by hand. <c>EndpointCommandCounterMiddleware</c>
/// calls <see cref="Reset"/> after authentication and authorization, so the security-stamp re-check is not counted;
/// commands issued inside a cache factory (<see cref="CacheFill"/>) are skipped too. Handlers read <see cref="Count"/>.
/// </summary>
/// <remarks>
/// The async-local holds a box rather than a bare <c>int</c>: a value assigned inside an awaited EF method would not
/// flow back to the caller, but a mutation of the box shared by the request's whole execution context does.
/// </remarks>
public sealed class DbCommandCounter : DbCommandInterceptor
{
    private readonly AsyncLocal<StrongBox<int>?> _current = new();

    /// <summary>Commands executed since the last <see cref="Reset"/> in this execution context; 0 outside a request.</summary>
    public int Count => _current.Value is { } box ? Volatile.Read(ref box.Value) : 0;

    /// <summary>Starts a new count for the current execution context and everything it awaits or starts.</summary>
    public void Reset() => _current.Value = new StrongBox<int>(0);

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Increment();
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Increment();
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Increment();
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Increment();
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Increment();
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Increment();
        return ValueTask.FromResult(result);
    }

    private void Increment()
    {
        if (!CacheFill.InProgress && _current.Value is { } box)
        {
            Interlocked.Increment(ref box.Value);
        }
    }
}
