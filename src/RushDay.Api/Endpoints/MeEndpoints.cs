using System.Diagnostics;
using RushDay.Api.Auth;
using RushDay.Api.Contracts;
using RushDay.Api.Observability;
using RushDay.Api.Security;
using RushDay.Domain.Audit;
using RushDay.Infrastructure.Audit;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Enrolments;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Queries;

namespace RushDay.Api.Endpoints;

/// <summary>
/// The student's own routes, <c>/api/me</c> (02-api.md section 8.3), behind <c>StudentOnly</c>. No route takes a
/// student number: every query predicate uses <see cref="CurrentUser.StudentId"/> from the session's claims, so another
/// student's data is structurally out of reach (02-api.md section 4, D25).
/// </summary>
public static class MeEndpoints
{
    public const string ModuleCodeRoute = "{code:regex(^[A-Z]{{2}}\\d{{4}}$)}";

    public static RouteGroupBuilder MapMeEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var me = api.MapGroup("/me").RequireAuthorization(Policies.StudentOnly).WithTags("Student");

        me.MapGet("/dashboard", DashboardAsync).WithName("MyDashboard");
        me.MapGet("/results", ResultsAsync).WithName("MyResults");
        me.MapGet("/timetable", TimetableAsync).WithName("MyTimetable");
        me.MapGet("/enrolments", EnrolmentsAsync).WithName("MyEnrolments");

        me.MapPost("/enrolments", EnrolAsync)
            .RequireRateLimiting(RateLimitPolicies.Enrol)
            .WithName("Enrol");

        me.MapDelete("/enrolments/" + ModuleCodeRoute, WithdrawAsync)
            .RequireRateLimiting(RateLimitPolicies.Enrol)
            .WithName("Withdraw");

        // Streams personal data, so it is under the write limiter (02-api.md section 1) and audited as a read.
        me.MapGet("/export.json", ExportAsync)
            .RequireRateLimiting(RateLimitPolicies.Write)
            .WithName("MyExport");

        return api;
    }

    // GET /api/me/dashboard: five queries (DashboardQuery) plus cached settings, windows and publications. The command
    // count is measured by DbCommandCounter, which started after authorization and skips cache fills.
    private static async Task<IResult> DashboardAsync(
        [AsParameters] StudentRequest s,
        DashboardQuery query,
        PublicationCache publications,
        DbCommandCounter commandCounter,
        RushDayMetrics metrics,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        if (s.User.StudentId is not { } studentId)
        {
            return ProblemResults.Problem(ProblemTypes.Forbidden);
        }

        var now = s.Clock.GetUtcNow();
        var calendar = await s.Windows.CurrentAsync(cancellationToken);
        var windows = await s.Windows.AllAsync(cancellationToken);
        var briefs = await publications.GetBriefAsync(now, cancellationToken);

        var data = await query.ExecuteAsync(studentId, calendar.AcademicYear, calendar.CurrentSemester, now, cancellationToken);
        if (data is null)
        {
            return ProblemResults.Problem(ProblemTypes.Forbidden);
        }

        var body = DashboardResponse.From(data, calendar, windows, briefs, now);

        metrics.DashboardQueries.Record(commandCounter.Count);
        metrics.DashboardDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return TypedResults.Ok(body);
    }

    // GET /api/me/results: three queries; visible marks only, scheduled groups carry their instant and no mark.
    private static async Task<IResult> ResultsAsync([AsParameters] StudentRequest s, ResultsQuery query, CancellationToken cancellationToken)
    {
        if (s.User.StudentId is not { } studentId)
        {
            return ProblemResults.Problem(ProblemTypes.Forbidden);
        }

        var data = await query.ExecuteAsync(studentId, s.Clock.GetUtcNow(), cancellationToken);
        return TypedResults.Ok(ResultsResponse.From(data));
    }

    // GET /api/me/timetable: one query; this year's active enrolments on modules of the current semester.
    private static async Task<IResult> TimetableAsync([AsParameters] StudentRequest s, TimetableQuery query, CancellationToken cancellationToken)
    {
        if (s.User.StudentId is not { } studentId)
        {
            return ProblemResults.Problem(ProblemTypes.Forbidden);
        }

        var calendar = await s.Windows.CurrentAsync(cancellationToken);
        var slots = await query.ExecuteAsync(studentId, calendar.AcademicYear, calendar.CurrentSemester, cancellationToken);
        return TypedResults.Ok(TimetableEntry.From(slots));
    }

    // GET /api/me/enrolments: one query; every year and status, current year first, then enrolledAt descending.
    private static async Task<IResult> EnrolmentsAsync([AsParameters] StudentRequest s, MyEnrolmentsQuery query, CancellationToken cancellationToken)
    {
        if (s.User.StudentId is not { } studentId)
        {
            return ProblemResults.Problem(ProblemTypes.Forbidden);
        }

        var calendar = await s.Windows.CurrentAsync(cancellationToken);
        var windows = await s.Windows.AllAsync(cancellationToken);
        var items = await query.ExecuteAsync(studentId, calendar.AcademicYear, windows, s.Clock.GetUtcNow(), cancellationToken);
        return TypedResults.Ok(items.Select(MyEnrolment.From).ToList());
    }

    // POST /api/me/enrolments: the atomic enrolment of 04-performance-and-ops.md section 2.1.
    private static async Task<IResult> EnrolAsync(EnrolRequest request, CurrentUser user, EnrolmentService enrolments, CancellationToken cancellationToken)
    {
        if (user.StudentId is not { } studentId)
        {
            return ProblemResults.Problem(ProblemTypes.Forbidden);
        }

        var result = await enrolments.EnrolAsync(studentId, request.ModuleCode, user.UserId, EnrolOptions.Self, cancellationToken);
        return result.Succeeded
            ? TypedResults.Created((string?)null, EnrolResponse.From(result.Receipt!))
            : EnrolmentProblems.For(result.Failure!);
    }

    // DELETE /api/me/enrolments/{code}: the withdrawal of 04-performance-and-ops.md section 2.2.
    private static async Task<IResult> WithdrawAsync(string code, CurrentUser user, EnrolmentService enrolments, CancellationToken cancellationToken)
    {
        if (user.StudentId is not { } studentId)
        {
            return ProblemResults.Problem(ProblemTypes.Forbidden);
        }

        var result = await enrolments.WithdrawAsync(studentId, code, user.UserId, WithdrawOptions.Self, cancellationToken);
        return result.Succeeded ? TypedResults.NoContent() : EnrolmentProblems.For(result.Failure!);
    }

    // GET /api/me/export.json: three queries plus the audit row student.exported_self, committed before the body is sent.
    private static async Task<IResult> ExportAsync(
        [AsParameters] StudentRequest s,
        HttpContext http,
        StudentExportQuery query,
        AuditWriter audit,
        RushDayDbContext db,
        CancellationToken cancellationToken)
    {
        if (s.User.StudentId is not { } studentId)
        {
            return ProblemResults.Problem(ProblemTypes.Forbidden);
        }

        var now = s.Clock.GetUtcNow();
        var data = await query.ExecuteAsync(studentId, now, cancellationToken);
        if (data is null)
        {
            return ProblemResults.Problem(ProblemTypes.Forbidden);
        }

        var studentNumber = data.Student.StudentNumber;
        audit.Record(db, AuditActions.StudentExportedSelf, AuditSubjects.Student, studentNumber, new { studentNumber }, studentId);
        await db.SaveChangesAsync(cancellationToken);

        http.Response.Headers.ContentDisposition = StudentExport.ContentDisposition(studentNumber);
        return TypedResults.Ok(StudentExport.From(data, now));
    }

    /// <summary>What every student handler needs: the caller, the academic calendar and windows, and the clock.</summary>
    private readonly record struct StudentRequest(CurrentUser User, EnrolmentWindowService Windows, TimeProvider Clock);
}
