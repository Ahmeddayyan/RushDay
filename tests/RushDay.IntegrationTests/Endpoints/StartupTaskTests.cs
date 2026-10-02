using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using RushDay.Api.Startup;
using RushDay.Infrastructure.Persistence;

namespace RushDay.IntegrationTests.Endpoints;

/// <summary>
/// Startup statements get <c>Database:StartupCommandTimeoutSeconds</c> (600 s): the request path's 10 s is too short
/// for a backfill over 80,000 grades on Neon's smallest compute, and a timeout there aborts every restart (04 section 7).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class StartupTaskTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task Startup_statements_use_the_startup_command_timeout_and_requests_keep_theirs()
    {
        var recorder = new TimeoutRecorder();
        await using var host = factory.Derive(b =>
        {
            b.UseSetting("Database:MigrateOnStartup", "true");
            b.UseSetting("Database:StartupCommandTimeoutSeconds", "321");
            b.ConfigureTestServices(s => s.ConfigureDbContext<RushDayDbContext>((_, o) => o.AddInterceptors(recorder)));
        });

        // The migration's statements (history table, lock) run on StartupTasks' context. Other work the host starts on
        // its own, such as Data Protection loading the key ring, is not a startup task and keeps 10 s.
        using var client = host.CreateCookieClient();
        var migration = recorder.Drain().Where(c => c.Text.Contains("__EFMigrationsHistory", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(migration);
        Assert.All(migration, c => Assert.Equal(321, c.Timeout));

        // A request's queries (here the cold caches behind the public status) keep the connection string's 10 s.
        using var response = await client.GetAsync("/api/public/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var requests = recorder.Drain();
        Assert.Contains(requests, c => c.Text.Contains("academic_settings", StringComparison.Ordinal));
        Assert.All(requests, c => Assert.Equal(10, c.Timeout));
    }

    [Fact]
    public void The_migrations_connection_gets_the_startup_timeout_too()
    {
        var options = StartupTasks.MigrationsContextOptions(factory.ConnectionString, TimeSpan.FromSeconds(600));

        Assert.Equal(600, RelationalOptionsExtension.Extract(options).CommandTimeout);
    }

    private sealed class TimeoutRecorder : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<(string Text, int Timeout)> _commands = new();

        public List<(string Text, int Timeout)> Drain()
        {
            var drained = new List<(string Text, int Timeout)>();
            while (_commands.TryDequeue(out var command))
            {
                drained.Add(command);
            }

            return drained;
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            _commands.Enqueue((command.CommandText, command.CommandTimeout));
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            _commands.Enqueue((command.CommandText, command.CommandTimeout));
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            _commands.Enqueue((command.CommandText, command.CommandTimeout));
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            _commands.Enqueue((command.CommandText, command.CommandTimeout));
            return ValueTask.FromResult(result);
        }
    }
}
