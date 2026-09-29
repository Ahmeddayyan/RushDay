using RushDay.Api.Auth;
using RushDay.Api.Contracts;
using RushDay.Api.Observability;
using RushDay.Api.Security;
using RushDay.Infrastructure.Announcements;
using RushDay.Infrastructure.Audit;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Enrolments;
using RushDay.Infrastructure.Grades;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Queries;

namespace RushDay.Api.Endpoints;

/// <summary>
/// The lecturer surface, <c>/api/lecturer</c> (02-api.md section 8.4), behind <c>LecturerOnly</c>; every route with a
/// <c>{code}</c> also requires <c>TeachesModule</c>. Beyond the policy, every handler resolves the module
/// <b>through</b> the caller's <c>module_lecturers</c> row (<see cref="StaffModules.TaughtAsync"/>) and every roster
/// and marks query repeats that join, so neither a policy bug nor another code in the URL can reach another module's
/// students, marks or announcements. Reads are scoped to the current academic year; mutations are under the
/// <c>write</c> limiter.
/// </summary>
public static class LecturerEndpoints
{
    public const int RosterPageSize = 50;
    public const int RosterMaxPageSize = 200;
    public const int MarksPageSize = 100;
    public const int MarksMaxPageSize = 500;

    public static RouteGroupBuilder MapLecturerEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var lecturer = api.MapGroup("/lecturer").RequireAuthorization(Policies.LecturerOnly).WithTags("Lecturer");
        lecturer.MapGet("/modules", ModulesAsync).WithName("LecturerModules");

        var module = lecturer.MapGroup("/modules/" + StaffPatterns.ModuleCodeRoute).RequireAuthorization(Policies.TeachesModule);
        module.MapGet("/roster", RosterAsync).WithName("LecturerRoster");
        module.MapGet("/roster.csv", RosterCsvAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("LecturerRosterCsv");
        module.MapGet("/marks", MarksAsync).WithName("LecturerMarks");
        module.MapPut("/marks", SaveMarksAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("SaveMarks");
        module.MapPost("/marks/submit", SubmitAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("SubmitMarks");
        module.MapGet("/announcements", AnnouncementsAsync).WithName("LecturerAnnouncements");
        module.MapPost("/announcements", CreateAnnouncementAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("CreateModuleAnnouncement");
        module.MapPut("/announcements/{id:guid}", UpdateAnnouncementAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("UpdateModuleAnnouncement");
        module.MapDelete("/announcements/{id:guid}", DeleteAnnouncementAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("DeleteModuleAnnouncement");

        return api;
    }

    // GET /api/lecturer/modules: assignments ⋈ modules, their lecturers, and this year's marks status per module.
    private static async Task<IResult> ModulesAsync([AsParameters] Staff s, LecturerModulesQuery query, CancellationToken cancellationToken)
    {
        if (s.User.LecturerId is not { } lecturerId)
        {
            return ProblemResults.Problem(ProblemTypes.Forbidden);
        }

        var now = s.Clock.GetUtcNow();
        var calendar = await s.Windows.CurrentAsync(cancellationToken);
        var windows = await s.Windows.AllAsync(cancellationToken);
        var modules = await query.ExecuteAsync(lecturerId, calendar.AcademicYear, now, cancellationToken);

        return TypedResults.Ok(modules.Select(m => new LecturerModuleView(
            ModuleSummary.From(m.Module, EnrolmentWindowService.Find(windows, calendar.AcademicYear, m.Module.Semester), now),
            m.MyRole,
            MarksStatus.From(m.Marks))).ToList());
    }

    // GET /api/lecturer/modules/{code}/roster: count and page, active first, through module_lecturers.
    private static async Task<IResult> RosterAsync(string code, [AsParameters] RosterParameters parameters, [AsParameters] Staff s, RosterQuery query, CancellationToken cancellationToken)
    {
        var taught = await TaughtAsync(s, code, cancellationToken);
        if (taught is null)
        {
            return NotYourModule();
        }

        var now = s.Clock.GetUtcNow();
        var calendar = await s.Windows.CurrentAsync(cancellationToken);
        var page = PageRequest.Of(parameters.Page, parameters.PageSize, RosterPageSize, RosterMaxPageSize);
        var rows = await query.ExecuteAsync(taught.Id, calendar.AcademicYear, s.User.LecturerId, parameters.Q, page, cancellationToken);
        var summary = await SummaryAsync(s, taught.Id, calendar.AcademicYear, now, cancellationToken);
        return TypedResults.Ok(RosterResponse.From(summary, rows));
    }

    // GET /api/lecturer/modules/{code}/roster.csv (Should): the whole roster of the year as an attachment.
    private static async Task<IResult> RosterCsvAsync(string code, [AsParameters] Staff s, RosterQuery query, HttpContext http, CancellationToken cancellationToken)
    {
        var taught = await TaughtAsync(s, code, cancellationToken);
        if (taught is null)
        {
            return NotYourModule();
        }

        var calendar = await s.Windows.CurrentAsync(cancellationToken);
        var rows = await query.AllAsync(taught.Id, calendar.AcademicYear, s.User.LecturerId, cancellationToken);

        var csv = new System.Text.StringBuilder(AuditCsvWriter.Line(["studentNumber", "fullName", "programme", "yearOfStudy", "status", "enrolledAt", "withdrawnAt"]));
        foreach (var row in rows)
        {
            csv.Append(AuditCsvWriter.Line(
            [
                row.StudentNumber,
                row.FullName,
                row.Programme,
                row.YearOfStudy.ToString(System.Globalization.CultureInfo.InvariantCulture),
                row.Status == Domain.Enrolments.EnrolmentStatus.Active ? "active" : "withdrawn",
                GradeNames.Instant(row.EnrolledAt),
                row.WithdrawnAt is { } withdrawnAt ? GradeNames.Instant(withdrawnAt) : null,
            ]));
        }

        // The stored, canonical code: never the route value, which matched case-insensitively.
        http.Response.Headers.ContentDisposition = $"attachment; filename=\"roster-{taught.Code}.csv\"";
        return TypedResults.Text(csv.ToString(), AuditCsvWriter.ContentType);
    }

    // GET /api/lecturer/modules/{code}/marks: the sheet (summary of the whole module, one page of rows).
    private static async Task<IResult> MarksAsync(string code, [AsParameters] RosterParameters parameters, [AsParameters] Staff s, MarksSheetQuery query, CancellationToken cancellationToken)
    {
        var taught = await TaughtAsync(s, code, cancellationToken);
        if (taught is null)
        {
            return NotYourModule();
        }

        var calendar = await s.Windows.CurrentAsync(cancellationToken);
        var page = PageRequest.Of(parameters.Page, parameters.PageSize, MarksPageSize, MarksMaxPageSize);
        var sheet = await query.ExecuteAsync(taught.Id, taught.Code, taught.Title, calendar.AcademicYear, s.User.LecturerId, taught.Role, parameters.Q, page, s.Clock.GetUtcNow(), cancellationToken);
        return TypedResults.Ok(MarksSheet.From(sheet));
    }

    // PUT /api/lecturer/modules/{code}/marks: all-or-nothing per request, optimistic versions, audited per row.
    private static async Task<IResult> SaveMarksAsync(
        string code,
        SaveMarksRequest request,
        [AsParameters] Staff s,
        MarksService marks,
        RushDayMetrics metrics,
        CancellationToken cancellationToken)
    {
        if (s.User.LecturerId is not { } lecturerId || s.User.UserId is not { } userId)
        {
            return ProblemResults.Problem(ProblemTypes.Forbidden);
        }

        var result = await marks.SaveAsync(lecturerId, code, [.. request.Rows!.Select(r => r.ToEntry())], userId, cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(result.Failure!);
        }

        var saved = result.Value!;
        metrics.GradesSaved.Add(saved.Written);

        var calendar = await s.Windows.CurrentAsync(cancellationToken);
        var status = await MarksStatusQuery.ForModuleAsync(s.Db, saved.ModuleId, calendar.AcademicYear, s.Clock.GetUtcNow(), cancellationToken);
        return TypedResults.Ok(new SaveMarksResponse(MarksSummary.From(status), [.. saved.Rows.Select(MarksSheetRow.From)]));
    }

    // POST /api/lecturer/modules/{code}/marks/submit: the leader's alone.
    private static async Task<IResult> SubmitAsync(string code, [AsParameters] Staff s, MarksService marks, CancellationToken cancellationToken)
    {
        if (s.User.LecturerId is not { } lecturerId)
        {
            return ProblemResults.Problem(ProblemTypes.Forbidden);
        }

        var result = await marks.SubmitAsync(lecturerId, code, cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(result.Failure!);
        }

        var submitted = result.Value!;
        return TypedResults.Ok(new SubmitMarksResponse(submitted.Code, MarksState.Submitted, submitted.SubmittedAt, submitted.GradeCount));
    }

    // GET /api/lecturer/modules/{code}/announcements: the module's own, including future and expired, never deleted.
    private static async Task<IResult> AnnouncementsAsync(string code, [AsParameters] Staff s, AnnouncementService announcements, CancellationToken cancellationToken)
    {
        var taught = await TaughtAsync(s, code, cancellationToken);
        if (taught is null)
        {
            return NotYourModule();
        }

        return TypedResults.Ok(AnnouncementView.From(await announcements.ListForModuleAsync(taught.Id, cancellationToken)));
    }

    // POST /api/lecturer/modules/{code}/announcements: always on the module resolved through module_lecturers.
    private static async Task<IResult> CreateAnnouncementAsync(string code, AnnouncementRequest request, [AsParameters] Staff s, AnnouncementService announcements, CancellationToken cancellationToken)
    {
        var scope = await ModuleScopeAsync(s, code, cancellationToken);
        if (scope is null || s.User.UserId is not { } userId)
        {
            return NotYourModule();
        }

        var created = await announcements.CreateAsync(request.ToDraft(), scope.Value, userId, cancellationToken);
        return TypedResults.Created((string?)null, AnnouncementView.From(created));
    }

    // PUT /api/lecturer/modules/{code}/announcements/{id}: resolved as id AND scope = Module AND module_id = this module.
    private static async Task<IResult> UpdateAnnouncementAsync(string code, Guid id, AnnouncementRequest request, [AsParameters] Staff s, AnnouncementService announcements, CancellationToken cancellationToken)
    {
        var scope = await ModuleScopeAsync(s, code, cancellationToken);
        if (scope is null)
        {
            return NotYourModule();
        }

        var updated = await announcements.UpdateAsync(id, request.ToDraft(), scope.Value, cancellationToken);
        return updated is null
            ? ProblemResults.Problem(ProblemTypes.AnnouncementNotFound, "No announcement of this module has that id.")
            : TypedResults.Ok(AnnouncementView.From(updated));
    }

    // DELETE /api/lecturer/modules/{code}/announcements/{id}: the same resolution; a university or another module's row is 404.
    private static async Task<IResult> DeleteAnnouncementAsync(string code, Guid id, [AsParameters] Staff s, AnnouncementService announcements, CancellationToken cancellationToken)
    {
        var scope = await ModuleScopeAsync(s, code, cancellationToken);
        if (scope is null)
        {
            return NotYourModule();
        }

        return await announcements.DeleteAsync(id, scope.Value, cancellationToken)
            ? TypedResults.NoContent()
            : ProblemResults.Problem(ProblemTypes.AnnouncementNotFound, "No announcement of this module has that id.");
    }

    /// <summary>
    /// The one place lecturer announcement calls get their scope: the id of the module the caller teaches, never null
    /// (a null scope would mean "university" to create and "any scope" to update and delete).
    /// </summary>
    private static async Task<Guid?> ModuleScopeAsync(Staff s, string code, CancellationToken cancellationToken) =>
        (await TaughtAsync(s, code, cancellationToken))?.Id;

    private static async Task<TaughtModule?> TaughtAsync(Staff s, string code, CancellationToken cancellationToken) =>
        s.User.LecturerId is { } lecturerId ? await StaffModules.TaughtAsync(s.Db, lecturerId, code, cancellationToken) : null;

    private static async Task<ModuleSummary> SummaryAsync(Staff s, Guid moduleId, string academicYear, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var module = await StaffModules.CatalogueModuleAsync(s.Db, moduleId, cancellationToken);
        var window = await s.Windows.FindAsync(academicYear, module.Semester, cancellationToken);
        return ModuleSummary.From(module, window, now);
    }

    private static IResult NotYourModule() => ProblemResults.Problem(ProblemTypes.NotYourModule);

    private static IResult Problem(MarksFailure failure) => failure.Error switch
    {
        MarksError.NotYourModule => NotYourModule(),
        MarksError.NotModuleLeader => ProblemResults.Problem(ProblemTypes.NotModuleLeader, "Only the module leader can submit marks"),
        MarksError.ModuleLocked => ProblemResults.Problem(ProblemTypes.ModuleLocked, "Marks for this module are submitted, scheduled or published and can no longer be edited."),
        MarksError.NotEnrolledStudents => ProblemResults.Problem(
            ProblemTypes.NotEnrolledStudents,
            "Some students have no active enrolment on this module this year.",
            new Dictionary<string, object?> { ["studentNumbers"] = failure.StudentNumbers }),
        MarksError.StaleMark => ProblemResults.Problem(
            ProblemTypes.StaleMark,
            "Someone else changed some of these marks; reload them and try again. Nothing was saved.",
            new Dictionary<string, object?> { ["studentNumbers"] = failure.StudentNumbers }),
        MarksError.NothingToSubmit => ProblemResults.Problem(ProblemTypes.NothingToSubmit, "No students are enrolled on this module this year."),
        MarksError.AlreadySubmitted => ProblemResults.Problem(ProblemTypes.AlreadySubmitted, "This module's marks are already submitted."),
        MarksError.MarksIncomplete => ProblemResults.Problem(
            ProblemTypes.MarksIncomplete,
            "Every enrolled student needs a mark or an outcome before the module can be submitted.",
            new Dictionary<string, object?> { ["missing"] = failure.StudentNumbers }),
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure.Error, "Not a refusal."),
    };

    /// <summary>What every lecturer handler needs.</summary>
    private readonly record struct Staff(CurrentUser User, EnrolmentWindowService Windows, RushDayDbContext Db, TimeProvider Clock);
}
