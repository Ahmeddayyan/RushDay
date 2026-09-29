using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RushDay.Api.Observability;
using RushDay.Infrastructure.Caching;

namespace RushDay.UnitTests.Observability;

public sealed class DbCommandCounterTests
{
    [Fact]
    public void Counts_every_kind_of_command_after_reset()
    {
        var counter = new DbCommandCounter();
        counter.Reset();

        ExecuteReader(counter);
        ExecuteScalar(counter);
        ExecuteNonQuery(counter);

        Assert.Equal(3, counter.Count);
    }

    [Fact]
    public async Task Counts_async_commands_awaited_in_child_methods()
    {
        var counter = new DbCommandCounter();
        counter.Reset();

        // EF runs the interceptor inside its own awaited methods; the increment must still be visible here.
        await ChildAsync(counter);
        await ChildAsync(counter);

        Assert.Equal(4, counter.Count);
    }

    [Fact]
    public void Reset_starts_from_zero()
    {
        var counter = new DbCommandCounter();
        counter.Reset();
        ExecuteReader(counter);
        ExecuteReader(counter);

        counter.Reset();
        ExecuteScalar(counter);

        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public void Commands_outside_a_request_are_not_counted()
    {
        var counter = new DbCommandCounter();

        ExecuteReader(counter);

        Assert.Equal(0, counter.Count);
    }

    /// <summary>A request that fills a cache is measured by its own commands only (04 section 6.1).</summary>
    [Fact]
    public async Task Commands_inside_a_cache_fill_are_not_counted()
    {
        var counter = new DbCommandCounter();
        counter.Reset();

        await ChildAsync(counter);
        var filled = await CacheFill.RunAsync(
            async _ =>
            {
                await ChildAsync(counter);
                return CacheFill.InProgress;
            },
            CancellationToken.None);
        ExecuteReader(counter);

        Assert.True(filled);
        Assert.False(CacheFill.InProgress);
        Assert.Equal(3, counter.Count);
    }

    [Fact]
    public async Task Concurrent_requests_keep_separate_counts()
    {
        var counter = new DbCommandCounter();
        using var ready = new Barrier(2);

        async Task<int> RequestAsync(int commands)
        {
            await Task.Yield();
            counter.Reset();
            ready.SignalAndWait(TimeSpan.FromSeconds(5));
            for (var i = 0; i < commands; i++)
            {
                await counter.ReaderExecutingAsync(null!, null!, default);
                await Task.Yield();
            }

            return counter.Count;
        }

        var results = await Task.WhenAll(Task.Run(() => RequestAsync(5)), Task.Run(() => RequestAsync(3)));

        Assert.Equal([5, 3], results);
    }

    private static async Task ChildAsync(DbCommandCounter counter)
    {
        await Task.Yield();
        await counter.ReaderExecutingAsync(null!, null!, default);
        await counter.NonQueryExecutingAsync(null!, null!, default);
    }

    private static void ExecuteReader(DbCommandCounter counter) =>
        counter.ReaderExecuting(null!, null!, default(InterceptionResult<DbDataReader>));

    private static void ExecuteScalar(DbCommandCounter counter) =>
        counter.ScalarExecuting(null!, null!, default(InterceptionResult<object>));

    private static void ExecuteNonQuery(DbCommandCounter counter) =>
        counter.NonQueryExecuting(null!, null!, default(InterceptionResult<int>));
}
