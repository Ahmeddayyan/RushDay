using System.Data.Common;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Persistence;

namespace RushDay.IntegrationTests.Caching;

/// <summary>
/// HybridCache runs one factory for every caller waiting on a key; the factory must not borrow the first caller's
/// scoped context, which dies with that caller's request (04-performance-and-ops.md section 4).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CacheFillTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task A_fill_survives_the_request_that_started_it()
    {
        // The column only the settings cache selects (the ops data-quality query also reads academic_settings).
        var gate = new QueryGate("institution_short_name");
        await using var host = factory.Derive(b => b.ConfigureTestServices(s => s.ConfigureDbContext<RushDayDbContext>((_, o) => o.AddInterceptors(gate))));

        // The first request starts the fill; its query is held mid-flight.
        var first = host.Services.CreateAsyncScope();
        using var firstAborted = new CancellationTokenSource();
        var firstRead = first.ServiceProvider.GetRequiredService<SettingsCache>().GetAsync(firstAborted.Token).AsTask();
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // A second request waits on the same key.
        await using var second = host.Services.CreateAsyncScope();
        var secondRead = second.ServiceProvider.GetRequiredService<SettingsCache>().GetAsync().AsTask();

        // The first request is aborted and its scope disposed (with any context it resolved) before the query returns.
        await firstAborted.CancelAsync();
        await first.DisposeAsync();
        gate.Release.TrySetResult();

        var settings = await secondRead.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.NotNull(settings);
        Assert.Equal("2026/27", settings.AcademicYear);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstRead);
    }

    /// <summary>Holds the first reader command whose text contains a marker until released.</summary>
    private sealed class QueryGate(string marker) : DbCommandInterceptor
    {
        private int _held;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(marker, StringComparison.Ordinal) && Interlocked.Exchange(ref _held, 1) == 0)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
            }

            return result;
        }
    }
}
