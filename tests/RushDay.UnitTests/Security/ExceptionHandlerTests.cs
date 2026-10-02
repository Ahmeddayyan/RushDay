using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RushDay.Api.Observability;
using RushDay.Api.Security;

namespace RushDay.UnitTests.Security;

/// <summary>
/// <c>RushDayExceptionHandler</c> (04-performance-and-ops.md section 5; review S4 findings C10 and D1):
/// <c>rushday.db.pool_wait_timeouts</c> counts only Npgsql's pool-exhaustion timeout, not a command that timed out
/// waiting on a lock; and text PostgreSQL cannot store (22021) is a 400 validation problem, not a 500.
/// </summary>
public sealed class ExceptionHandlerTests : IDisposable
{
    private readonly TestMeterFactory _meters = new();
    private readonly RushDayMetrics _metrics;
    private readonly RushDayExceptionHandler _handler;

    public ExceptionHandlerTests()
    {
        _metrics = new RushDayMetrics(_meters);
        _handler = new RushDayExceptionHandler(_metrics, NullLogger<RushDayExceptionHandler>.Instance);
    }

    public void Dispose() => _meters.Dispose();

    [Fact]
    public async Task Pool_exhaustion_is_503_and_counted_as_a_pool_wait_timeout()
    {
        using var timeouts = new MetricCollector<long>(_metrics.DbPoolWaitTimeouts);
        var exhausted = new NpgsqlException(
            "The connection pool has been exhausted, either raise 'Max Pool Size' (currently 20) or 'Timeout' (currently 5 seconds) in your connection string.",
            new TimeoutException());

        var (handled, status, body) = await HandleAsync(new InvalidOperationException("wrapped", exhausted));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
        Assert.Equal("urn:rushday:server-busy", body.GetProperty("type").GetString());
        Assert.Equal(1, timeouts.GetMeasurementSnapshot().Sum(m => m.Value));
    }

    [Fact]
    public async Task A_command_timeout_is_503_but_not_a_pool_wait_timeout()
    {
        using var timeouts = new MetricCollector<long>(_metrics.DbPoolWaitTimeouts);
        var commandTimeout = new NpgsqlException("Exception while reading from stream", new TimeoutException("Timeout during reading attempt"));

        var (handled, status, _) = await HandleAsync(commandTimeout);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
        Assert.Empty(timeouts.GetMeasurementSnapshot());
    }

    [Fact]
    public async Task Text_postgres_cannot_store_is_a_validation_problem()
    {
        var nul = new PostgresException("invalid byte sequence for encoding \"UTF8\": 0x00", "ERROR", "ERROR", "22021");

        var (handled, status, body) = await HandleAsync(new InvalidOperationException("An exception occurred while iterating over the results of a query.", nul));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal("urn:rushday:validation", body.GetProperty("type").GetString());
        Assert.DoesNotContain("0x00", body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Any_other_database_error_is_left_to_the_default_500()
    {
        var (handled, _, _) = await HandleAsync(new PostgresException("relation does not exist", "ERROR", "ERROR", "42P01"));
        Assert.False(handled);
    }

    private async Task<(bool Handled, int Status, JsonElement Body)> HandleAsync(Exception exception)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = "/api/me/enrolments";
        await using var body = new MemoryStream();
        context.Response.Body = body;

        var handled = await _handler.TryHandleAsync(context, exception, CancellationToken.None);
        var text = System.Text.Encoding.UTF8.GetString(body.ToArray());
        return (handled, context.Response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _created = [];

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(new MeterOptions(options.Name) { Version = options.Version, Tags = options.Tags, Scope = this });
            _created.Add(meter);
            return meter;
        }

        public void Dispose() => _created.ForEach(m => m.Dispose());
    }
}
