using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RushDay.Api.Observability;
using RushDay.Api.Options;

namespace RushDay.UnitTests.Observability;

/// <summary>Bucketing, percentiles and the snapshot composition of <see cref="MetricsSnapshotService"/> (04 section 6.2).</summary>
public sealed class MetricsSnapshotServiceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly ManualClock _clock = new(Start);
    private readonly TestMeterFactory _meters = new();
    private readonly MetricsSnapshotService _service;
    private readonly Histogram<double> _requestDuration;

    public MetricsSnapshotServiceTests()
    {
        _service = new MetricsSnapshotService(
            _meters,
            _clock,
            scopeFactory: null,
            Options.Create(new DatabaseOptions { MaxPoolSize = 20 }),
            Options.Create(new RateLimitingOptions()),
            new HostingEnvironment { EnvironmentName = "Test" },
            new ConfigurationBuilder().Build(),
            NullLogger<MetricsSnapshotService>.Instance);
        _service.StartListening();

        _requestDuration = _meters.Create(new MeterOptions("Microsoft.AspNetCore.Hosting"))
            .CreateHistogram<double>("http.server.request.duration", unit: "s");
    }

    public void Dispose()
    {
        _service.Dispose();
        _meters.Dispose();
    }

    [Fact]
    public void Request_durations_land_in_one_minute_buckets()
    {
        _clock.Set(Start.AddSeconds(10));
        Request(10, 200);
        Request(20, 404);
        Request(30, 500);

        _clock.Set(Start.AddSeconds(65));
        Request(5, 503);

        var snapshot = _service.GetSnapshot();

        // The last complete minute (12:00) is "the last 60 s".
        Assert.Equal(3, snapshot.Http.Last60s.Requests);
        Assert.Equal(1, snapshot.Http.Last60s.Status2xx);
        Assert.Equal(1, snapshot.Http.Last60s.Status4xx);
        Assert.Equal(1, snapshot.Http.Last60s.Status5xx);
        Assert.Equal(0.05, snapshot.Http.Last60s.PerSecond);

        Assert.Equal(MetricsSnapshotService.BucketCount, snapshot.Series.Count);
        Assert.Equal(Start.AddMinutes(1), snapshot.Series[^1].Minute);
        Assert.Equal(1, snapshot.Series[^1].Requests);
        Assert.Equal(1, snapshot.Series[^1].Status5xx);
        Assert.Equal(Start, snapshot.Series[^2].Minute);
        Assert.Equal(3, snapshot.Series[^2].Requests);
        Assert.True(snapshot.Series.Zip(snapshot.Series.Skip(1)).All(p => p.First.Minute < p.Second.Minute), "series is oldest first");
    }

    [Fact]
    public void Seconds_are_converted_to_milliseconds_and_percentiles_are_interpolated()
    {
        for (var ms = 1; ms <= 100; ms++)
        {
            Request(ms, 200);
        }

        _clock.Set(Start.AddMinutes(1));
        var window = _service.GetSnapshot().Http.Last60s;

        Assert.Equal(100, window.Requests);
        Assert.Equal(50, window.P50Ms, 3);
        Assert.Equal(95, window.P95Ms, 3);
        Assert.Equal(99, window.P99Ms, 3);
    }

    [Fact]
    public void Percentiles_are_clamped_to_the_observed_range()
    {
        for (var i = 0; i < 20; i++)
        {
            Request(3, 200);
        }

        _clock.Set(Start.AddMinutes(1));
        var window = _service.GetSnapshot().Http.Last60s;

        Assert.Equal(3, window.P50Ms, 3);
        Assert.Equal(3, window.P99Ms, 3);
    }

    [Fact]
    public void Slow_requests_beyond_the_last_boundary_interpolate_to_the_maximum()
    {
        Request(12_000, 200);
        Request(30_000, 200);

        _clock.Set(Start.AddMinutes(1));
        var window = _service.GetSnapshot().Http.Last60s;

        // Both land in the open-ended bucket above 10 s, which interpolates up to the observed maximum.
        Assert.InRange(window.P50Ms, 12_000, 30_000);
        Assert.InRange(window.P99Ms, window.P50Ms, 30_000);
        Assert.True(window.P99Ms > 25_000);
    }

    [Fact]
    public void In_the_first_minute_the_window_is_the_minute_so_far()
    {
        _clock.Set(Start.AddSeconds(20));
        Request(10, 200);
        Request(10, 200);

        var window = _service.GetSnapshot().Http.Last60s;

        Assert.Equal(2, window.Requests);
        Assert.Equal(0.1, window.PerSecond);
    }

    /// <summary>After a cold start mid-minute, the last complete minute only covers the seconds the process was up.</summary>
    [Fact]
    public void After_a_cold_start_the_rate_divides_by_the_covered_seconds()
    {
        _clock.Set(Start.AddSeconds(30));
        using var meters = new TestMeterFactory();
        using var service = new MetricsSnapshotService(
            meters,
            _clock,
            scopeFactory: null,
            Options.Create(new DatabaseOptions()),
            Options.Create(new RateLimitingOptions()),
            new HostingEnvironment { EnvironmentName = "Test" },
            new ConfigurationBuilder().Build(),
            NullLogger<MetricsSnapshotService>.Instance);
        service.StartListening();
        var duration = meters.Create(new MeterOptions("Microsoft.AspNetCore.Hosting")).CreateHistogram<double>("http.server.request.duration", unit: "s");
        _clock.Set(Start.AddSeconds(40));
        for (var i = 0; i < 3; i++)
        {
            duration.Record(0.01, new KeyValuePair<string, object?>("http.response.status_code", 200));
        }

        _clock.Set(Start.AddSeconds(65));
        var window = service.GetSnapshot().Http.Last60s;

        // 12:00:30 to 12:01:00 is 30 s of uptime in that minute: 3 requests / 30 s.
        Assert.Equal(3, window.Requests);
        Assert.Equal(0.1, window.PerSecond);
    }

    [Fact]
    public void Enrolment_rejections_are_read_by_the_catalogue_reasons()
    {
        var metrics = new RushDayMetrics(_meters);
        foreach (var reason in RushDayMetrics.RejectionReasons.All)
        {
            metrics.EnrolmentsRejected.Add(1, new KeyValuePair<string, object?>("reason", reason));
        }

        var rejected = _service.GetSnapshot().Enrolment.Rejected;

        Assert.Equal(new EnrolmentRejections(1, 1, 1, 1, 1, 2), rejected);
        Assert.Equal(["module_full", "already_enrolled", "window_closed", "credit_limit", "results_exist", "student_left", "module_inactive"], RushDayMetrics.RejectionReasons.All);
    }

    /// <summary>
    /// The data-quality queries run only while someone polls the ops snapshot, so an idle deployment lets Neon's
    /// compute suspend: no attempt before the first poll, one at once on it, and none once polling stopped.
    /// </summary>
    [Fact]
    public async Task Data_quality_is_refreshed_only_while_the_snapshot_is_polled()
    {
        var clock = new FakeTimeProvider(Start);
        var scopes = new CountingScopeFactory();
        using var meters = new TestMeterFactory();
        using var service = new MetricsSnapshotService(
            meters,
            clock,
            scopes,
            Options.Create(new DatabaseOptions()),
            Options.Create(new RateLimitingOptions()),
            new HostingEnvironment { EnvironmentName = "Test" },
            new ConfigurationBuilder().Build(),
            NullLogger<MetricsSnapshotService>.Instance);
        await service.StartAsync(CancellationToken.None);
        try
        {
            for (var tick = 0; tick < 5; tick++)
            {
                await Task.Delay(50);
                clock.Advance(MetricsSnapshotService.DataQualityInterval);
            }

            await Task.Delay(200);
            Assert.Equal(0, scopes.Created);

            _ = service.GetSnapshot();
            await WaitUntilAsync(() => scopes.Created >= 1);
            await WaitUntilAsync(() => service.GetSnapshot().DataQuality.StaleSince is not null);

            // Nobody polls any more: after the polling window the timer stops querying.
            clock.Advance(MetricsSnapshotService.PollingWindow + TimeSpan.FromSeconds(1));
            await Task.Delay(200);
            var afterIdle = scopes.Created;
            for (var tick = 0; tick < 5; tick++)
            {
                clock.Advance(MetricsSnapshotService.DataQualityInterval);
                await Task.Delay(50);
            }

            await Task.Delay(200);
            Assert.Equal(afterIdle, scopes.Created);
            Assert.False(service.IsBeingPolled(clock.GetUtcNow()));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void Buckets_older_than_an_hour_are_recycled_but_totals_remain()
    {
        var metrics = new RushDayMetrics(_meters);
        Request(10, 200);
        metrics.Login(RushDayMetrics.LoginOutcomes.Success);

        _clock.Set(Start.AddMinutes(61));
        Request(10, 200);
        metrics.Login(RushDayMetrics.LoginOutcomes.Success);

        var snapshot = _service.GetSnapshot();

        Assert.Equal(1, snapshot.Series.Sum(p => p.Requests));
        Assert.Equal(2, snapshot.Auth.LoginsSucceeded);
    }

    [Fact]
    public void RushDay_counters_are_totals_and_rejections_are_split_into_shed_and_limited()
    {
        var metrics = new RushDayMetrics(_meters);
        metrics.Login(RushDayMetrics.LoginOutcomes.Success);
        metrics.Login(RushDayMetrics.LoginOutcomes.Failed);
        metrics.Login(RushDayMetrics.LoginOutcomes.LockedOut);
        metrics.Login(RushDayMetrics.LoginOutcomes.MfaRequired);
        metrics.Lockout();
        metrics.LoadShed(RushDayMetrics.ShedPolicies.Api);
        metrics.LoadShed(RushDayMetrics.ShedPolicies.Api);
        metrics.LoadShed(RushDayMetrics.ShedPolicies.HealthReady);
        metrics.LoadShed(RushDayMetrics.ShedPolicies.Login);
        metrics.PoolWaitTimeout();
        metrics.EnrolmentsAccepted.Add(30);
        metrics.EnrolmentsRejected.Add(470, new KeyValuePair<string, object?>("reason", "module_full"));
        metrics.EnrolmentsRejected.Add(2, new KeyValuePair<string, object?>("reason", "student_left"));
        metrics.EnrolmentsRejected.Add(1, new KeyValuePair<string, object?>("reason", "module_inactive"));

        _clock.Set(Start.AddMinutes(1));
        var snapshot = _service.GetSnapshot();

        Assert.Equal(new AuthSnapshot(1, 2, 1), snapshot.Auth);
        Assert.Equal(3, snapshot.Http.Last60s.Shed503);
        Assert.Equal(1, snapshot.Http.Last60s.RateLimited429);
        Assert.Equal(1, snapshot.Db.WaitTimeoutsTotal);
        Assert.Equal(30, snapshot.Enrolment.Accepted);
        Assert.Equal(470, snapshot.Enrolment.Rejected.ModuleFull);
        Assert.Equal(3, snapshot.Enrolment.Rejected.Other);
    }

    [Fact]
    public void Cache_requests_are_reported_per_cache()
    {
        var metrics = new RushDayMetrics(_meters);
        metrics.CacheRequest("catalogue:all", hit: false);
        metrics.CacheRequest("catalogue:all", hit: true);
        metrics.CacheRequest("catalogue:all", hit: true);
        metrics.CacheRequest("settings", hit: true);

        var caches = _service.GetSnapshot().Cache;

        Assert.Equal([new CacheSnapshot("catalogue:all", 2, 1), new CacheSnapshot("settings", 1, 0)], caches);
    }

    [Fact]
    public void Dashboard_queries_per_request_is_the_measured_mean()
    {
        var metrics = new RushDayMetrics(_meters);
        metrics.DashboardQueries.Record(5);
        metrics.DashboardQueries.Record(5);
        metrics.DashboardQueries.Record(6);
        metrics.DashboardDuration.Record(4);

        var dashboard = _service.GetSnapshot().Dashboard;

        Assert.Equal(5.33, dashboard.QueriesPerRequest);
        Assert.Equal(4, dashboard.P95Ms, 3);
    }

    [Fact]
    public void In_flight_requests_follow_the_up_down_counter()
    {
        var active = _meters.Create(new MeterOptions("Microsoft.AspNetCore.Hosting")).CreateUpDownCounter<long>("http.server.active_requests");
        active.Add(1);
        active.Add(1);
        active.Add(1);
        active.Add(-1);

        Assert.Equal(2, _service.GetSnapshot().Http.InFlight);
    }

    [Fact]
    public void Observable_pool_instruments_are_sampled_as_last_values()
    {
        using var npgsql = new Meter("Npgsql");
        var used = 3;
        npgsql.CreateObservableUpDownCounter("db.client.connection.count", () => new[]
        {
            new Measurement<int>(used, new KeyValuePair<string, object?>("db.client.connection.state", "used"), new KeyValuePair<string, object?>("db.client.connection.pool.name", "rushday")),
            new Measurement<int>(5, new KeyValuePair<string, object?>("db.client.connection.state", "idle"), new KeyValuePair<string, object?>("db.client.connection.pool.name", "rushday")),
        });
        npgsql.CreateObservableUpDownCounter("db.client.connection.max", () => new Measurement<int>(20, new KeyValuePair<string, object?>("db.client.connection.pool.name", "rushday")));

        var first = _service.GetSnapshot().Db;
        used = 7;
        var second = _service.GetSnapshot().Db;

        Assert.Equal((3, 5, 20), (first.PoolBusy, first.PoolIdle, first.PoolMax));
        Assert.Equal(7, second.PoolBusy);
    }

    [Fact]
    public void Meters_of_another_application_are_ignored()
    {
        using var otherApplication = new TestMeterFactory();
        var foreign = otherApplication.Create(new MeterOptions("Microsoft.AspNetCore.Hosting"))
            .CreateHistogram<double>("http.server.request.duration", unit: "s");
        foreign.Record(0.010, new KeyValuePair<string, object?>("http.response.status_code", 200));
        Request(10, 200);

        _clock.Set(Start.AddMinutes(1));

        Assert.Equal(1, _service.GetSnapshot().Http.Last60s.Requests);
    }

    [Fact]
    public void Snapshot_describes_the_runtime()
    {
        var snapshot = _service.GetSnapshot();

        Assert.Equal("Test", snapshot.Environment);
        Assert.Equal("local", snapshot.Commit);
        Assert.Equal("workstation", snapshot.Runtime.GcMode);
        Assert.Equal(20, snapshot.Runtime.MaxPoolSize);
        Assert.Equal(24, snapshot.Runtime.RateLimiting.MaxConcurrent);
        Assert.True(snapshot.Process.WorkingSetBytes > 0);
        Assert.Null(snapshot.DataQuality.RefreshedAt);
    }

    [Fact]
    public void Histogram_percentile_of_an_empty_histogram_is_zero() =>
        Assert.Equal(0, new HistogramAggregate().Percentile(0.95));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not become true within 10 s.");
            await Task.Delay(20);
        }
    }

    private void Request(double milliseconds, int status) =>
        _requestDuration.Record(milliseconds / 1000d, new KeyValuePair<string, object?>("http.response.status_code", status));

    /// <summary>Counts refresh attempts; its scopes resolve nothing, so every attempt fails and marks the data stale.</summary>
    private sealed class CountingScopeFactory : IServiceScopeFactory
    {
        private int _created;

        public int Created => Volatile.Read(ref _created);

        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref _created);
            return new EmptyScope();
        }

        private sealed class EmptyScope : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type serviceType) => null;

            public void Dispose()
            {
            }
        }
    }

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Set(DateTimeOffset now) => _now = now;
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

        public void Dispose()
        {
            foreach (var meter in _created)
            {
                meter.Dispose();
            }
        }
    }
}
