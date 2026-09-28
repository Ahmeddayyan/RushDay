using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using RushDay.Api.Observability;

namespace RushDay.UnitTests.Observability;

/// <summary>Every instrument of 04-performance-and-ops.md section 6.1 is declared once, on <c>Meter "RushDay"</c>.</summary>
public sealed class RushDayMetricsTests : IDisposable
{
    private readonly TestMeterFactory _meters = new();
    private readonly RushDayMetrics _metrics;

    public RushDayMetricsTests() => _metrics = new RushDayMetrics(_meters);

    public void Dispose() => _meters.Dispose();

    [Fact]
    public void Declares_exactly_the_instruments_of_the_spec()
    {
        var published = new List<Instrument>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (ReferenceEquals(instrument.Meter, _metrics.Meter))
                {
                    published.Add(instrument);
                }
            },
        };
        listener.Start();

        Assert.Equal(RushDayMetrics.MeterName, _metrics.Meter.Name);
        Assert.Equal(
            new[]
            {
                "rushday.auth.lockouts", "rushday.auth.logins", "rushday.cache.requests", "rushday.dashboard.duration",
                "rushday.dashboard.queries", "rushday.db.pool_wait_timeouts", "rushday.enrolments.accepted",
                "rushday.enrolments.duration", "rushday.enrolments.rejected", "rushday.grades.saved",
                "rushday.load_shed.rejected", "rushday.results.published",
            },
            published.Select(i => i.Name).Order(StringComparer.Ordinal));
        Assert.IsType<Histogram<int>>(published.Single(i => i.Name == RushDayMetrics.DashboardQueriesName));
        Assert.Equal("ms", published.Single(i => i.Name == RushDayMetrics.EnrolmentsDurationName).Unit);
    }

    [Fact]
    public void Login_outcomes_and_shed_policies_are_tagged()
    {
        using var logins = new MetricCollector<long>(_metrics.AuthLogins);
        using var shed = new MetricCollector<long>(_metrics.LoadShedRejected);

        _metrics.Login(RushDayMetrics.LoginOutcomes.MfaRequired);
        _metrics.LoadShed(RushDayMetrics.ShedPolicies.HealthReady);

        Assert.Equal(RushDayMetrics.LoginOutcomes.MfaRequired, Assert.Single(logins.GetMeasurementSnapshot()).Tags["outcome"]);
        Assert.Equal("health_ready", Assert.Single(shed.GetMeasurementSnapshot()).Tags["policy"]);
    }

    [Fact]
    public void Cache_requests_carry_cache_and_result()
    {
        using var cache = new MetricCollector<long>(_metrics.CacheRequests);

        _metrics.CacheRequest("settings", hit: true);
        _metrics.CacheRequest("settings", hit: false);

        var measurements = cache.GetMeasurementSnapshot();
        Assert.Equal(["hit", "miss"], measurements.Select(m => m.Tags["result"]));
        Assert.All(measurements, m => Assert.Equal("settings", m.Tags["cache"]));
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
