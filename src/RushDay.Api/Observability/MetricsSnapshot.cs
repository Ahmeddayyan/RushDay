namespace RushDay.Api.Observability;

/// <summary>
/// <c>OpsSnapshot</c> of 04-performance-and-ops.md section 6.3, assembled by <see cref="MetricsSnapshotService"/>
/// without touching the database; <c>GET /api/admin/ops/metrics</c> (S6) returns it as is. Counters are totals since
/// process start; <see cref="Http"/>.<c>Last60s</c> is the last complete one-minute bucket.
/// </summary>
public sealed record MetricsSnapshot(
    DateTimeOffset SampledAt,
    DateTimeOffset StartedAt,
    long UptimeSeconds,
    string Commit,
    string Environment,
    RuntimeSnapshot Runtime,
    ProcessSnapshot Process,
    HttpSnapshot Http,
    DbSnapshot Db,
    IReadOnlyList<CacheSnapshot> Cache,
    EnrolmentSnapshot Enrolment,
    DashboardSnapshot Dashboard,
    AuthSnapshot Auth,
    IReadOnlyList<SeriesPoint> Series,
    IReadOnlyList<BackfillSnapshot> Backfills,
    DataQualitySnapshot DataQuality);

public sealed record RuntimeSnapshot(string DotnetVersion, string GcMode, int MaxPoolSize, RateLimitingSnapshot RateLimiting);

public sealed record RateLimitingSnapshot(int MaxConcurrent, int MaxQueued, int LoginPerUserPerMinute, int EnrolPerUserPer10s, int WritePerUserPerMinute);

public sealed record ProcessSnapshot(long WorkingSetBytes, long GcHeapBytes, int ThreadPoolThreads);

public sealed record HttpSnapshot(long InFlight, HttpWindowSnapshot Last60s);

public sealed record HttpWindowSnapshot(
    long Requests,
    double PerSecond,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    long Status2xx,
    long Status4xx,
    long Status5xx,
    long RateLimited429,
    long Shed503);

public sealed record DbSnapshot(long PoolMax, long PoolBusy, long PoolIdle, long PendingRequests, long WaitTimeoutsTotal);

public sealed record CacheSnapshot(string Name, long Hits, long Misses);

public sealed record EnrolmentSnapshot(long Accepted, EnrolmentRejections Rejected, double P95Ms);

/// <summary><see cref="Other"/> = student_left + module_inactive.</summary>
public sealed record EnrolmentRejections(long ModuleFull, long AlreadyEnrolled, long WindowClosed, long CreditLimit, long ResultsExist, long Other);

public sealed record DashboardSnapshot(double P95Ms, double QueriesPerRequest);

public sealed record AuthSnapshot(long LoginsSucceeded, long LoginsFailed, long Lockouts);

/// <summary>One minute of the ring buffer; 60 points, oldest first.</summary>
public sealed record SeriesPoint(DateTimeOffset Minute, long Requests, double P95Ms, double P99Ms, long Status5xx, long Shed503, long RateLimited429);

public sealed record BackfillSnapshot(string Name, DateTimeOffset CompletedAt, int RowsAffected, string? Notes);

public sealed record DataQualitySnapshot(
    DateTimeOffset? RefreshedAt,
    DateTimeOffset? StaleSince,
    IReadOnlyList<OverCapacityModule> ModulesOverCapacity,
    int EnrolledCountDrift);

public sealed record OverCapacityModule(string Code, int Capacity, int EnrolledCount);
