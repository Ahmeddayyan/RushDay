using System.Diagnostics;
using System.Globalization;
using Npgsql;

namespace RushDay.IntegrationTests.Student;

/// <summary>
/// Raw-connection helpers for the lock-order tests: hold a row lock in a transaction of the test's own, wait until a
/// backend of the API queues behind a lock, and read counts on a connection that shares nothing with the host. Every
/// interleaving these tests build is deterministic: they wait on <c>pg_stat_activity</c>, never on a delay.
/// </summary>
internal static class DbProbe
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(20);

    /// <summary>Opens a transaction and runs <paramref name="sql"/> in it (a <c>FOR ... UPDATE</c> or a <c>LOCK TABLE</c>); the lock is held until release or dispose.</summary>
    public static async Task<HeldLock> HoldAsync(RushDayApiFactory factory, string sql, params (string Name, object Value)[] parameters)
    {
        var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            await command.ExecuteNonQueryAsync();
        }

        return new HeldLock(connection, transaction);
    }

    /// <summary>Waits until at least <paramref name="count"/> backends wait on a heavyweight lock with a query matching <paramref name="queryLike"/> (ILIKE).</summary>
    public static async Task WaitForLockWaitAsync(RushDayApiFactory factory, string queryLike, int count = 1) =>
        await WaitUntilAsync(
            factory,
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND pid <> pg_backend_pid() AND wait_event_type = 'Lock' AND query ILIKE @q",
            queryLike,
            count,
            "a lock wait on " + queryLike);

    /// <summary>Waits until a backend is in the given wait event (for example <c>PgSleep</c>).</summary>
    public static async Task WaitForWaitEventAsync(RushDayApiFactory factory, string waitEvent) =>
        await WaitUntilAsync(
            factory,
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND pid <> pg_backend_pid() AND wait_event = @q",
            waitEvent,
            1,
            "the wait event " + waitEvent);

    public static async Task ExecAsync(RushDayApiFactory factory, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    public static async Task<T> ScalarAsync<T>(RushDayApiFactory factory, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T), CultureInfo.InvariantCulture);
    }

    /// <summary><c>(enrolled_count, active rows of <paramref name="academicYear"/>, capacity)</c> of a module, in SQL.</summary>
    public static async Task<(int EnrolledCount, int Active, int Capacity)> CountsAsync(RushDayApiFactory factory, Guid moduleId, string academicYear = StudentData.CurrentYear)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT enrolled_count, (SELECT count(*) FROM enrolments e WHERE e.module_id = m.id AND e.status = 'Active' AND e.academic_year = @y)::int, capacity FROM modules m WHERE id = @m",
            connection);
        command.Parameters.AddWithValue("m", moduleId);
        command.Parameters.AddWithValue("y", academicYear);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
    }

    /// <summary>The SQLSTATE of the first <see cref="PostgresException"/> in the chain, else the exception's type and message.</summary>
    public static string Describe(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres.SqlState + " " + postgres.MessageText;
            }
        }

        return exception is null ? "none" : exception.GetType().Name + " " + exception.Message;
    }

    private static async Task WaitUntilAsync(RushDayApiFactory factory, string sql, string argument, int count, string what)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < WaitLimit)
        {
            await using var connection = new NpgsqlConnection(factory.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("q", argument);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) >= count)
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"No backend reached {what} within {WaitLimit.TotalSeconds} s.");
    }
}

/// <summary>A lock held in a transaction of the test's own; released by commit, or rolled back on dispose.</summary>
internal sealed class HeldLock(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
{
    private bool _released;

    public async Task ReleaseAsync()
    {
        if (!_released)
        {
            _released = true;
            await transaction.RollbackAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ReleaseAsync();
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
    }
}
