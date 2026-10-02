using RushDay.Api.Observability;
using RushDay.Infrastructure.Announcements;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Enrolments;
using RushDay.Infrastructure.Queries;

namespace RushDay.Api.Startup;

/// <summary>Stage S4's services (the S2 hook <c>AddStudentSurface</c>).</summary>
public static partial class ServiceRegistration
{
    static partial void AddStudentSurface(IServiceCollection services)
    {
        // Caches read through CacheKeys.GetOrCreateAsync and open their own context from IDbContextFactory (04 section 4).
        services.AddScoped<CatalogueCache>();
        services.AddScoped<AnnouncementCache>();

        services.AddScoped<EnrolmentWindowService>();
        services.AddScoped<EnrolmentService>();
        services.AddSingleton<IEnrolmentMetrics, RushDayEnrolmentMetrics>();
        services.AddScoped<AnnouncementService>();

        services.AddScoped<DashboardQuery>();
        services.AddScoped<ResultsQuery>();
        services.AddScoped<TimetableQuery>();
        services.AddScoped<MyEnrolmentsQuery>();
        services.AddScoped<ModuleDetailQuery>();
        services.AddScoped<StudentExportQuery>();
    }
}

/// <summary>
/// Records the enrolment instruments S2 declared on <see cref="RushDayMetrics"/> (04-performance-and-ops.md section 6.1):
/// <c>rushday.enrolments.accepted</c>, <c>rushday.enrolments.rejected{reason}</c> with the closed reason list, and
/// <c>rushday.enrolments.duration</c> in milliseconds.
/// </summary>
internal sealed class RushDayEnrolmentMetrics(RushDayMetrics metrics) : IEnrolmentMetrics
{
    public void Accepted() => metrics.EnrolmentsAccepted.Add(1);

    public void Rejected(EnrolmentError reason) =>
        metrics.EnrolmentsRejected.Add(1, new KeyValuePair<string, object?>("reason", ReasonOf(reason)));

    public void Duration(double milliseconds) => metrics.EnrolmentsDuration.Record(milliseconds);

    public static string ReasonOf(EnrolmentError reason) => reason switch
    {
        EnrolmentError.ModuleFull => RushDayMetrics.RejectionReasons.ModuleFull,
        EnrolmentError.AlreadyEnrolled => RushDayMetrics.RejectionReasons.AlreadyEnrolled,
        EnrolmentError.WindowClosed => RushDayMetrics.RejectionReasons.WindowClosed,
        EnrolmentError.CreditLimitExceeded => RushDayMetrics.RejectionReasons.CreditLimit,
        EnrolmentError.ResultsExist => RushDayMetrics.RejectionReasons.ResultsExist,
        EnrolmentError.StudentLeft => RushDayMetrics.RejectionReasons.StudentLeft,
        EnrolmentError.ModuleInactive => RushDayMetrics.RejectionReasons.ModuleInactive,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Not an enrolment rejection reason."),
    };
}
