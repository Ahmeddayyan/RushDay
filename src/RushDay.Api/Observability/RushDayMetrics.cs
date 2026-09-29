using System.Diagnostics.Metrics;
using RushDay.Infrastructure.Caching;

namespace RushDay.Api.Observability;

/// <summary>
/// Every instrument of 04-performance-and-ops.md section 6.1 on <c>Meter "RushDay"</c> (D18). Later stages only
/// record; they never declare instruments. <see cref="MetricsSnapshotService"/> aggregates them in-process.
/// </summary>
public sealed class RushDayMetrics : ICacheMetrics
{
    public const string MeterName = "RushDay";

    public const string EnrolmentsAcceptedName = "rushday.enrolments.accepted";
    public const string EnrolmentsRejectedName = "rushday.enrolments.rejected";
    public const string EnrolmentsDurationName = "rushday.enrolments.duration";
    public const string DashboardDurationName = "rushday.dashboard.duration";
    public const string DashboardQueriesName = "rushday.dashboard.queries";
    public const string AuthLoginsName = "rushday.auth.logins";
    public const string AuthLockoutsName = "rushday.auth.lockouts";
    public const string CacheRequestsName = "rushday.cache.requests";
    public const string LoadShedRejectedName = "rushday.load_shed.rejected";
    public const string DbPoolWaitTimeoutsName = "rushday.db.pool_wait_timeouts";
    public const string GradesSavedName = "rushday.grades.saved";
    public const string ResultsPublishedName = "rushday.results.published";

    /// <summary><c>rushday.auth.logins{outcome}</c> values.</summary>
    public static class LoginOutcomes
    {
        public const string Success = "success";
        public const string Failed = "failed";
        public const string LockedOut = "locked_out";
        public const string MfaRequired = "mfa_required";
    }

    /// <summary><c>rushday.enrolments.rejected{reason}</c> values: the closed list of 04 section 2.1 (S4 records them).</summary>
    public static class RejectionReasons
    {
        public const string ModuleFull = "module_full";
        public const string AlreadyEnrolled = "already_enrolled";
        public const string WindowClosed = "window_closed";
        public const string CreditLimit = "credit_limit";
        public const string ResultsExist = "results_exist";
        public const string StudentLeft = "student_left";
        public const string ModuleInactive = "module_inactive";

        public static IReadOnlyList<string> All { get; } = [ModuleFull, AlreadyEnrolled, WindowClosed, CreditLimit, ResultsExist, StudentLeft, ModuleInactive];
    }

    /// <summary>
    /// <c>rushday.load_shed.rejected{policy}</c> values: the closed list of 04 section 6.1. The <c>password-change</c>
    /// limiter reports as <see cref="Login"/>, the family it protects.
    /// </summary>
    public static class ShedPolicies
    {
        public const string Api = "api";
        public const string Login = "login";
        public const string Enrol = "enrol";
        public const string Write = "write";
        public const string HealthReady = "health_ready";
        public const string OpsMetrics = "ops_metrics";
    }

    public RushDayMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        Meter = meterFactory.Create(MeterName);

        EnrolmentsAccepted = Meter.CreateCounter<long>(EnrolmentsAcceptedName, description: "Enrolments accepted.");
        EnrolmentsRejected = Meter.CreateCounter<long>(EnrolmentsRejectedName, description: "Enrolments rejected, by reason.");
        EnrolmentsDuration = Meter.CreateHistogram<double>(EnrolmentsDurationName, unit: "ms", description: "Enrolment request duration.");
        DashboardDuration = Meter.CreateHistogram<double>(DashboardDurationName, unit: "ms", description: "Student dashboard duration.");
        DashboardQueries = Meter.CreateHistogram<int>(DashboardQueriesName, unit: "{command}", description: "Database commands per student dashboard, measured by DbCommandCounter.");
        AuthLogins = Meter.CreateCounter<long>(AuthLoginsName, description: "Sign-in attempts, by outcome.");
        AuthLockouts = Meter.CreateCounter<long>(AuthLockoutsName, description: "Accounts locked out by failed sign-ins.");
        CacheRequests = Meter.CreateCounter<long>(CacheRequestsName, description: "In-process cache reads, by cache and result.");
        LoadShedRejected = Meter.CreateCounter<long>(LoadShedRejectedName, description: "Requests rejected by a limiter, by policy.");
        DbPoolWaitTimeouts = Meter.CreateCounter<long>(DbPoolWaitTimeoutsName, description: "Requests that timed out waiting for a pooled database connection.");
        GradesSaved = Meter.CreateCounter<long>(GradesSavedName, description: "Grade rows written by lecturers.");
        ResultsPublished = Meter.CreateCounter<long>(ResultsPublishedName, description: "Results publications created.");
    }

    public Meter Meter { get; }

    public Counter<long> EnrolmentsAccepted { get; }

    /// <summary>Tag <c>reason</c>: one of <see cref="RejectionReasons"/>.</summary>
    public Counter<long> EnrolmentsRejected { get; }

    public Histogram<double> EnrolmentsDuration { get; }

    public Histogram<double> DashboardDuration { get; }

    public Histogram<int> DashboardQueries { get; }

    public Counter<long> AuthLogins { get; }

    public Counter<long> AuthLockouts { get; }

    public Counter<long> CacheRequests { get; }

    public Counter<long> LoadShedRejected { get; }

    public Counter<long> DbPoolWaitTimeouts { get; }

    public Counter<long> GradesSaved { get; }

    public Counter<long> ResultsPublished { get; }

    public void Login(string outcome) => AuthLogins.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void Lockout() => AuthLockouts.Add(1);

    public void LoadShed(string policy) => LoadShedRejected.Add(1, new KeyValuePair<string, object?>("policy", policy));

    public void PoolWaitTimeout() => DbPoolWaitTimeouts.Add(1);

    public void CacheRequest(string cache, bool hit) =>
        CacheRequests.Add(1, new KeyValuePair<string, object?>("cache", cache), new KeyValuePair<string, object?>("result", hit ? "hit" : "miss"));
}
