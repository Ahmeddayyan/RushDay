using System.Diagnostics.Metrics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RushDay.Api.Options;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Api.Observability;

/// <summary>
/// The in-process metrics store behind the ops page (D18, 04-performance-and-ops.md section 6.2). A
/// <see cref="MeterListener"/> subscribes to the RushDay, ASP.NET Core hosting, Kestrel, rate-limiting and Npgsql
/// meters and aggregates into one-minute buckets, 60 deep (a ring buffer of a few hundred KB at most). Counters keep
/// per-bucket deltas and totals; histograms keep count, sum, min, max and a fixed-boundary histogram from which
/// p50/p95/p99 are interpolated; observable instruments are sampled every 5 s (and at snapshot time) as last values.
/// Data quality and backfill status are refreshed on a 60 s background timer, never per request, so
/// <see cref="GetSnapshot"/> touches no database.
/// </summary>
public sealed class MetricsSnapshotService : IHostedService, IDisposable
{
    public const int BucketCount = 60;

    /// <summary>Upper bounds (ms) of the fixed histogram buckets; one more bucket holds everything above the last.</summary>
    public static readonly double[] BucketBoundariesMs = [1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000];

    public static readonly IReadOnlyList<string> ListenedMeters =
    [
        RushDayMetrics.MeterName,
        "Microsoft.AspNetCore.Hosting",
        "Microsoft.AspNetCore.Server.Kestrel",
        "Microsoft.AspNetCore.RateLimiting",
        "Npgsql",
    ];

    public static readonly TimeSpan GaugeSampleInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DataQualityInterval = TimeSpan.FromSeconds(60);

    internal const string RequestDuration = "http.server.request.duration";
    internal const string ActiveRequests = "http.server.active_requests";

    private const string DriftSql = """
        SELECT count(*)::int AS "Value" FROM modules m
        WHERE m.enrolled_count <> (SELECT count(*) FROM enrolments e JOIN academic_settings s ON s.id = 1 AND e.academic_year = s.academic_year
                                   WHERE e.module_id = m.id AND e.status = 'Active')
        """;

    /// <summary>Tags kept per instrument; every other tag is merged away.</summary>
    private static readonly Dictionary<string, string[]> KeptTags = new(StringComparer.Ordinal)
    {
        [RequestDuration] = ["http.response.status_code"],
        [RushDayMetrics.EnrolmentsRejectedName] = ["reason"],
        [RushDayMetrics.AuthLoginsName] = ["outcome"],
        [RushDayMetrics.CacheRequestsName] = ["cache", "result"],
        [RushDayMetrics.LoadShedRejectedName] = ["policy"],
        ["db.client.connection.count"] = ["db.client.connection.state", "db.client.connection.pool.name"],
        ["db.client.connection.max"] = ["db.client.connection.pool.name"],
        ["db.client.connection.npgsql.pending_requests"] = ["db.client.connection.pool.name"],
        ["db.client.connections.usage"] = ["state", "pool.name"],
        ["db.client.connections.max"] = ["pool.name"],
        ["db.client.connections.pending_requests"] = ["pool.name"],
        ["aspnetcore.rate_limiting.requests"] = ["aspnetcore.rate_limiting.result"],
    };

    private readonly IMeterFactory _meterFactory;
    private readonly TimeProvider _clock;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly DatabaseOptions _database;
    private readonly RateLimitingOptions _rateLimiting;
    private readonly string _environment;
    private readonly string _commit;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly Bucket[] _buckets = new Bucket[BucketCount];
    private readonly Aggregates _totals = new();
    private readonly MeterListener _listener;
    private readonly DateTimeOffset _startedAt;
    private readonly long _startMinute;

    private CancellationTokenSource? _stopping;
    private Task? _refreshLoop;
    private ITimer? _gaugeTimer;
    private bool _started;
    private volatile DataQualitySnapshot _dataQuality = new(null, null, [], 0);
    private volatile IReadOnlyList<BackfillSnapshot> _backfills = [];

    public MetricsSnapshotService(
        IMeterFactory meterFactory,
        TimeProvider clock,
        IServiceScopeFactory? scopeFactory,
        IOptions<DatabaseOptions> database,
        IOptions<RateLimitingOptions> rateLimiting,
        IHostEnvironment environment,
        IConfiguration configuration,
        ILogger<MetricsSnapshotService> logger)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(rateLimiting);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        _meterFactory = meterFactory;
        _clock = clock;
        _scopeFactory = scopeFactory;
        _database = database.Value;
        _rateLimiting = rateLimiting.Value;
        _environment = environment.EnvironmentName;
        _commit = configuration["RENDER_GIT_COMMIT"] is { Length: > 0 } commit ? commit : "local";
        _logger = logger;
        _startedAt = clock.GetUtcNow();
        _startMinute = MinuteOf(_startedAt);

        for (var i = 0; i < BucketCount; i++)
        {
            _buckets[i] = new Bucket();
        }

        _listener = new MeterListener { InstrumentPublished = OnInstrumentPublished };
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) => OnMeasurement(state, value, tags));
        _listener.SetMeasurementEventCallback<float>((instrument, value, tags, state) => OnMeasurement(state, value, tags));
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) => OnMeasurement(state, value, tags));
        _listener.SetMeasurementEventCallback<int>((instrument, value, tags, state) => OnMeasurement(state, value, tags));
        _listener.SetMeasurementEventCallback<short>((instrument, value, tags, state) => OnMeasurement(state, value, tags));
        _listener.SetMeasurementEventCallback<byte>((instrument, value, tags, state) => OnMeasurement(state, value, tags));
    }

    private enum InstrumentKind
    {
        Counter,
        UpDownCounter,
        Histogram,
        LastValue,
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        StartListening();
        _stopping = new CancellationTokenSource();
        _gaugeTimer = _clock.CreateTimer(_ => SampleObservables(), null, GaugeSampleInterval, GaugeSampleInterval);
        _refreshLoop = Task.Run(() => RefreshLoopAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stopping is null)
        {
            return;
        }

        await _stopping.CancelAsync();
        if (_refreshLoop is not null)
        {
            await Task.WhenAny(_refreshLoop, Task.Delay(Timeout.Infinite, cancellationToken));
        }
    }

    public void Dispose()
    {
        _gaugeTimer?.Dispose();
        _listener.Dispose();
        _stopping?.Dispose();
    }

    /// <summary>Starts the listener only (no timers, no database); <see cref="StartAsync"/> calls it.</summary>
    public void StartListening()
    {
        if (!_started)
        {
            _started = true;
            _listener.Start();
        }
    }

    public MetricsSnapshot GetSnapshot()
    {
        SampleObservables();

        var now = _clock.GetUtcNow();
        var minute = MinuteOf(now);

        HttpWindowSnapshot last60s;
        HttpSnapshot http;
        DbSnapshot db;
        List<CacheSnapshot> caches;
        EnrolmentSnapshot enrolment;
        DashboardSnapshot dashboard;
        AuthSnapshot auth;
        var series = new List<SeriesPoint>(BucketCount);

        lock (_gate)
        {
            // "Last 60 s" is the last complete minute; in the process's first minute it is the minute so far.
            var windowMinute = minute - 1 >= _startMinute ? minute - 1 : minute;
            var window = BucketAt(windowMinute)?.Values ?? Aggregates.Empty;
            var seconds = windowMinute == minute
                ? Math.Max(1, (now - Max(_startedAt, StartOf(minute))).TotalSeconds)
                : 60;
            last60s = HttpWindow(window, seconds);

            http = new HttpSnapshot((long)_totals.Current(ActiveRequests), last60s);

            db = new DbSnapshot(
                PoolMax: (long)(_totals.Current("db.client.connection.max") + _totals.Current("db.client.connections.max")),
                PoolBusy: (long)(_totals.Current("db.client.connection.count", tag0: "used") + _totals.Current("db.client.connections.usage", tag0: "used")),
                PoolIdle: (long)(_totals.Current("db.client.connection.count", tag0: "idle") + _totals.Current("db.client.connections.usage", tag0: "idle")),
                PendingRequests: (long)(_totals.Current("db.client.connection.npgsql.pending_requests") + _totals.Current("db.client.connections.pending_requests")),
                WaitTimeoutsTotal: (long)_totals.Current(RushDayMetrics.DbPoolWaitTimeoutsName));

            caches = _totals.TagValues(RushDayMetrics.CacheRequestsName, 0)
                .Order(StringComparer.Ordinal)
                .Select(name => new CacheSnapshot(
                    name,
                    (long)_totals.Current(RushDayMetrics.CacheRequestsName, tag0: name, tag1: "hit"),
                    (long)_totals.Current(RushDayMetrics.CacheRequestsName, tag0: name, tag1: "miss")))
                .ToList();

            double Rejected(string reason) => _totals.Current(RushDayMetrics.EnrolmentsRejectedName, tag0: reason);
            enrolment = new EnrolmentSnapshot(
                (long)_totals.Current(RushDayMetrics.EnrolmentsAcceptedName),
                new EnrolmentRejections(
                    (long)Rejected("module_full"),
                    (long)Rejected("already_enrolled"),
                    (long)Rejected("window_closed"),
                    (long)Rejected("credit_limit"),
                    (long)Rejected("results_exist"),
                    (long)(Rejected("student_left") + Rejected("module_inactive"))),
                _totals.MergedHistogram(RushDayMetrics.EnrolmentsDurationName).Percentile(0.95));

            var queries = _totals.MergedHistogram(RushDayMetrics.DashboardQueriesName);
            dashboard = new DashboardSnapshot(
                _totals.MergedHistogram(RushDayMetrics.DashboardDurationName).Percentile(0.95),
                queries.Count == 0 ? 0 : Math.Round(queries.Sum / queries.Count, 2));

            auth = new AuthSnapshot(
                (long)_totals.Current(RushDayMetrics.AuthLoginsName, tag0: RushDayMetrics.LoginOutcomes.Success),
                (long)(_totals.Current(RushDayMetrics.AuthLoginsName, tag0: RushDayMetrics.LoginOutcomes.Failed)
                     + _totals.Current(RushDayMetrics.AuthLoginsName, tag0: RushDayMetrics.LoginOutcomes.LockedOut)),
                (long)_totals.Current(RushDayMetrics.AuthLockoutsName));

            for (var m = minute - BucketCount + 1; m <= minute; m++)
            {
                var values = BucketAt(m)?.Values ?? Aggregates.Empty;
                var requests = values.MergedHistogram(RequestDuration);
                var (_, _, status5xx) = StatusClasses(values);
                var (shed, limited) = Rejections(values);
                series.Add(new SeriesPoint(StartOf(m), requests.Count, requests.Percentile(0.95), requests.Percentile(0.99), status5xx, shed, limited));
            }
        }

        return new MetricsSnapshot(
            SampledAt: now,
            StartedAt: _startedAt,
            UptimeSeconds: (long)Math.Max(0, (now - _startedAt).TotalSeconds),
            Commit: _commit,
            Environment: _environment,
            Runtime: new RuntimeSnapshot(
                RuntimeInformation.FrameworkDescription,
                GCSettings.IsServerGC ? "server" : "workstation",
                _database.MaxPoolSize,
                new RateLimitingSnapshot(_rateLimiting.MaxConcurrent, _rateLimiting.MaxQueued, _rateLimiting.LoginPerUserPerMinute, _rateLimiting.EnrolPerUserPer10s, _rateLimiting.WritePerUserPerMinute)),
            Process: new ProcessSnapshot(Environment.WorkingSet, GC.GetTotalMemory(false), ThreadPool.ThreadCount),
            Http: http,
            Db: db,
            Cache: caches,
            Enrolment: enrolment,
            Dashboard: dashboard,
            Auth: auth,
            Series: series,
            Backfills: _backfills,
            DataQuality: _dataQuality);
    }

    /// <summary>Refreshes data quality and backfill status once; the background loop calls it every 60 s.</summary>
    public async Task RefreshDataQualityAsync(CancellationToken cancellationToken)
    {
        if (_scopeFactory is null)
        {
            return;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();
            db.Database.SetCommandTimeout(2);

            var overCapacity = await db.Modules.AsNoTracking()
                .Where(m => m.EnrolledCount > m.Capacity)
                .OrderBy(m => m.Code)
                .Select(m => new OverCapacityModule(m.Code, m.Capacity, m.EnrolledCount))
                .ToListAsync(cancellationToken);
            var drift = await db.Database.SqlQueryRaw<int>(DriftSql).SingleAsync(cancellationToken);
            var backfills = await db.DataBackfills.AsNoTracking()
                .OrderBy(b => b.CompletedAt)
                .Select(b => new BackfillSnapshot(b.Name, b.CompletedAt, b.RowsAffected, b.Notes))
                .ToListAsync(cancellationToken);

            _dataQuality = new DataQualitySnapshot(_clock.GetUtcNow(), null, overCapacity, drift);
            _backfills = backfills;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Data-quality refresh failed; the ops page keeps the last values.");
            var last = _dataQuality;
            _dataQuality = last with { StaleSince = last.StaleSince ?? _clock.GetUtcNow() };
        }
    }

    private static long MinuteOf(DateTimeOffset instant) => (long)Math.Floor(instant.ToUnixTimeSeconds() / 60d);

    private static DateTimeOffset StartOf(long minute) => DateTimeOffset.FromUnixTimeSeconds(minute * 60);

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private static HttpWindowSnapshot HttpWindow(Aggregates values, double seconds)
    {
        var requests = values.MergedHistogram(RequestDuration);
        var (status2xx, status4xx, status5xx) = StatusClasses(values);
        var (shed, limited) = Rejections(values);
        return new HttpWindowSnapshot(
            requests.Count,
            Math.Round(requests.Count / seconds, 2),
            requests.Percentile(0.50),
            requests.Percentile(0.95),
            requests.Percentile(0.99),
            status2xx,
            status4xx,
            status5xx,
            limited,
            shed);
    }

    private static (long Status2xx, long Status4xx, long Status5xx) StatusClasses(Aggregates values)
    {
        long s2 = 0, s4 = 0, s5 = 0;
        foreach (var (tags, histogram) in values.HistogramsOf(RequestDuration))
        {
            if (tags.Length == 0 || !int.TryParse(tags[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var status))
            {
                continue;
            }

            switch (status / 100)
            {
                case 2: s2 += histogram.Count; break;
                case 4: s4 += histogram.Count; break;
                case 5: s5 += histogram.Count; break;
            }
        }

        return (s2, s4, s5);
    }

    /// <summary>Shed = the global limiter and the readiness probe (503); limited = every other limiter (429).</summary>
    private static (long Shed503, long RateLimited429) Rejections(Aggregates values)
    {
        var shed = values.Current(RushDayMetrics.LoadShedRejectedName, tag0: RushDayMetrics.ShedPolicies.Api)
                 + values.Current(RushDayMetrics.LoadShedRejectedName, tag0: RushDayMetrics.ShedPolicies.HealthReady);
        var all = values.Current(RushDayMetrics.LoadShedRejectedName);
        return ((long)shed, (long)(all - shed));
    }

    private void OnInstrumentPublished(Instrument instrument, MeterListener listener)
    {
        var meter = instrument.Meter;
        if (!ListenedMeters.Contains(meter.Name) || (meter.Scope is not null && !ReferenceEquals(meter.Scope, _meterFactory)))
        {
            return;
        }

        var definition = instrument.GetType().IsGenericType ? instrument.GetType().GetGenericTypeDefinition() : null;
        var kind = definition == typeof(Histogram<>) ? InstrumentKind.Histogram
            : definition == typeof(Counter<>) ? InstrumentKind.Counter
            : definition == typeof(UpDownCounter<>) ? InstrumentKind.UpDownCounter
            : InstrumentKind.LastValue;
        var scale = kind == InstrumentKind.Histogram && instrument.Unit == "s" ? 1000d : 1d;

        listener.EnableMeasurementEvents(instrument, new InstrumentSpec(instrument.Name, kind, KeptTags.GetValueOrDefault(instrument.Name, []), scale));
    }

    private void OnMeasurement(object? state, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (state is not InstrumentSpec spec)
        {
            return;
        }

        var tagValues = spec.TagNames.Length == 0 ? [] : new string[spec.TagNames.Length];
        for (var i = 0; i < spec.TagNames.Length; i++)
        {
            tagValues[i] = string.Empty;
            foreach (var tag in tags)
            {
                if (tag.Key == spec.TagNames[i])
                {
                    tagValues[i] = Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty;
                    break;
                }
            }
        }

        var key = new SeriesKey(spec.Name, tagValues);
        var scaled = value * spec.Scale;
        var minute = MinuteOf(_clock.GetUtcNow());

        lock (_gate)
        {
            var bucket = BucketFor(minute);
            switch (spec.Kind)
            {
                case InstrumentKind.Histogram:
                    bucket.Values.Histogram(key).Add(scaled);
                    _totals.Histogram(key).Add(scaled);
                    break;
                case InstrumentKind.Counter:
                case InstrumentKind.UpDownCounter:
                    bucket.Values.Add(key, scaled);
                    _totals.Add(key, scaled);
                    break;
                default:
                    bucket.Values.Set(key, scaled);
                    _totals.Set(key, scaled);
                    break;
            }
        }
    }

    private void SampleObservables()
    {
        try
        {
            _listener.RecordObservableInstruments();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Sampling observable instruments failed.");
        }
    }

    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await RefreshDataQualityAsync(cancellationToken);
            try
            {
                await Task.Delay(DataQualityInterval, _clock, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private Bucket BucketFor(long minute)
    {
        var bucket = _buckets[(int)(minute % BucketCount)];
        if (bucket.Minute != minute)
        {
            bucket.Reset(minute);
        }

        return bucket;
    }

    private Bucket? BucketAt(long minute)
    {
        var bucket = _buckets[(int)(((minute % BucketCount) + BucketCount) % BucketCount)];
        return bucket.Minute == minute ? bucket : null;
    }

    private sealed record InstrumentSpec(string Name, InstrumentKind Kind, string[] TagNames, double Scale);

    private sealed class Bucket
    {
        public long Minute { get; private set; } = long.MinValue;

        public Aggregates Values { get; } = new();

        public void Reset(long minute)
        {
            Minute = minute;
            Values.Clear();
        }
    }

    /// <summary>Series name plus the kept tag values; equality is by value.</summary>
    private readonly record struct SeriesKey(string Name, string[] Tags)
    {
        public bool Equals(SeriesKey other) =>
            string.Equals(Name, other.Name, StringComparison.Ordinal) && Tags.AsSpan().SequenceEqual(other.Tags);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Name, StringComparer.Ordinal);
            foreach (var tag in Tags)
            {
                hash.Add(tag, StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }
    }

    private sealed class Aggregates
    {
        public static readonly Aggregates Empty = new();

        private readonly Dictionary<SeriesKey, double> _sums = [];
        private readonly Dictionary<SeriesKey, double> _last = [];
        private readonly Dictionary<SeriesKey, HistogramAggregate> _histograms = [];

        public void Clear()
        {
            _sums.Clear();
            _last.Clear();
            _histograms.Clear();
        }

        public void Add(SeriesKey key, double value) => _sums[key] = _sums.GetValueOrDefault(key) + value;

        public void Set(SeriesKey key, double value) => _last[key] = value;

        public HistogramAggregate Histogram(SeriesKey key)
        {
            if (!_histograms.TryGetValue(key, out var histogram))
            {
                histogram = new HistogramAggregate();
                _histograms[key] = histogram;
            }

            return histogram;
        }

        /// <summary>Sum (counters, up-down counters) or last value (observables) across series matching the tag filter.</summary>
        public double Current(string name, string? tag0 = null, string? tag1 = null)
        {
            double total = 0;
            foreach (var (key, value) in _sums)
            {
                if (Matches(key, name, tag0, tag1))
                {
                    total += value;
                }
            }

            foreach (var (key, value) in _last)
            {
                if (Matches(key, name, tag0, tag1))
                {
                    total += value;
                }
            }

            return total;
        }

        public IEnumerable<string> TagValues(string name, int index) =>
            _sums.Keys.Concat(_last.Keys)
                .Where(k => k.Name == name && k.Tags.Length > index)
                .Select(k => k.Tags[index])
                .Distinct(StringComparer.Ordinal)
                .ToList();

        public IEnumerable<(string[] Tags, HistogramAggregate Histogram)> HistogramsOf(string name) =>
            _histograms.Where(h => h.Key.Name == name).Select(h => (h.Key.Tags, h.Value)).ToList();

        public HistogramAggregate MergedHistogram(string name)
        {
            var merged = new HistogramAggregate();
            foreach (var (key, histogram) in _histograms)
            {
                if (key.Name == name)
                {
                    merged.Merge(histogram);
                }
            }

            return merged;
        }

        private static bool Matches(SeriesKey key, string name, string? tag0, string? tag1) =>
            key.Name == name
            && (tag0 is null || (key.Tags.Length > 0 && key.Tags[0] == tag0))
            && (tag1 is null || (key.Tags.Length > 1 && key.Tags[1] == tag1));
    }
}

/// <summary>Count, sum, min, max and fixed-boundary bucket counts of one histogram series.</summary>
public sealed class HistogramAggregate
{
    private readonly long[] _counts = new long[MetricsSnapshotService.BucketBoundariesMs.Length + 1];

    public long Count { get; private set; }

    public double Sum { get; private set; }

    public double Min { get; private set; } = double.PositiveInfinity;

    public double Max { get; private set; } = double.NegativeInfinity;

    public void Add(double value)
    {
        Count++;
        Sum += value;
        Min = Math.Min(Min, value);
        Max = Math.Max(Max, value);
        _counts[IndexOf(value)]++;
    }

    public void Merge(HistogramAggregate other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.Count == 0)
        {
            return;
        }

        Count += other.Count;
        Sum += other.Sum;
        Min = Math.Min(Min, other.Min);
        Max = Math.Max(Max, other.Max);
        for (var i = 0; i < _counts.Length; i++)
        {
            _counts[i] += other._counts[i];
        }
    }

    /// <summary>
    /// Linear interpolation inside the bucket that holds the rank, clamped to the observed min and max; 0 when empty.
    /// </summary>
    public double Percentile(double quantile)
    {
        if (Count == 0)
        {
            return 0;
        }

        var boundaries = MetricsSnapshotService.BucketBoundariesMs;
        var rank = Math.Clamp(quantile, 0, 1) * Count;
        long cumulative = 0;
        for (var i = 0; i < _counts.Length; i++)
        {
            var inBucket = _counts[i];
            if (inBucket == 0)
            {
                continue;
            }

            if (cumulative + inBucket >= rank)
            {
                var lower = i == 0 ? 0 : boundaries[i - 1];
                var upper = i < boundaries.Length ? boundaries[i] : Math.Max(Max, boundaries[^1]);
                var value = lower + ((upper - lower) * (rank - cumulative) / inBucket);
                return Math.Round(Math.Clamp(value, Min, Max), 3);
            }

            cumulative += inBucket;
        }

        return Math.Round(Max, 3);
    }

    private static int IndexOf(double value)
    {
        var boundaries = MetricsSnapshotService.BucketBoundariesMs;
        for (var i = 0; i < boundaries.Length; i++)
        {
            if (value <= boundaries[i])
            {
                return i;
            }
        }

        return boundaries.Length;
    }
}
