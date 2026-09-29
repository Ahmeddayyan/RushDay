using RushDay.Api.Auth;
using RushDay.Api.Contracts;
using RushDay.Api.Security;
using RushDay.Domain.Audit;
using RushDay.Infrastructure.Audit;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Enrolments;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Queries;
using RushDay.Infrastructure.Students;

namespace RushDay.Api.Endpoints;

/// <summary>
/// Student records, <c>/api/admin/students</c> (02-api.md section 8.5): the searchable list, create, the student view
/// (audited <c>student.viewed</c>, committed before the body is sent), edit, mark as left, the export on the student's
/// behalf (<c>student.exported</c>, under the <c>write</c> limiter), and the override enrolment and withdrawal through
/// <see cref="EnrolmentService"/> (windows and the credit limit ignored, capacity kept unless <c>forceCapacity</c>
/// raises it by one when full; <c>results-exist</c> and <c>student-left</c> still apply).
/// </summary>
public static class AdminStudentEndpoints
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public static RouteGroupBuilder MapAdminStudentEndpoints(this RouteGroupBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        admin.MapGet("/students", ListAsync).WithName("AdminStudents");
        admin.MapPost("/students", CreateAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("CreateStudent");

        var student = admin.MapGroup("/students/" + StaffPatterns.StudentNumberRoute);
        student.MapGet("/", ViewAsync).WithName("AdminStudentView");
        student.MapPut("/", UpdateAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("UpdateStudent");
        student.MapPost("/leave", LeaveAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("StudentLeave");
        student.MapGet("/export.json", ExportAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("AdminStudentExport");
        student.MapPost("/enrolments", EnrolAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("OverrideEnrol");
        student.MapPost("/enrolments/" + StaffPatterns.ModuleCodeRoute + "/withdraw", WithdrawAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("OverrideWithdraw");
        return admin;
    }

    private static async Task<IResult> ListAsync([AsParameters] StudentListParameters parameters, AdminStudentQuery query, TimeProvider clock, CancellationToken cancellationToken)
    {
        var page = PageRequest.Of(parameters.Page, parameters.PageSize, DefaultPageSize, MaxPageSize);
        var rows = await query.ListAsync(parameters.Q, parameters.State(), page, clock.GetUtcNow(), cancellationToken);
        return TypedResults.Ok(new Paged<AdminStudentListItem>([.. rows.Items.Select(AdminStudentListItem.From)], rows.Page, rows.PageSize, rows.Total));
    }

    private static async Task<IResult> CreateAsync(CreateStudentRequest request, StudentAdminService students, AdminStudentQuery query, TimeProvider clock, CancellationToken cancellationToken)
    {
        var result = await students.CreateAsync(request.StudentNumber!, request.ToChange(), cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(result.Error);
        }

        var row = await query.RowAsync(result.Value!, clock.GetUtcNow(), cancellationToken);
        return TypedResults.Created((string?)null, AdminStudentListItem.From(row!));
    }

    // GET /api/admin/students/{n}: five queries, then the student.viewed row is committed before the body is sent.
    private static async Task<IResult> ViewAsync(string studentNumber, AdminStudentQuery query, AuditWriter audit, RushDayDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var data = await query.ViewAsync(studentNumber, clock.GetUtcNow(), cancellationToken);
        if (data is null)
        {
            return StudentNotFound();
        }

        var number = data.Student.StudentNumber;
        audit.Record(db, AuditActions.StudentViewed, AuditSubjects.Student, number, new { studentNumber = number }, data.Student.Id);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(AdminStudentView.From(data));
    }

    private static async Task<IResult> UpdateAsync(string studentNumber, UpdateStudentRequest request, StudentAdminService students, AdminStudentQuery query, TimeProvider clock, CancellationToken cancellationToken)
    {
        var result = await students.UpdateAsync(studentNumber, request.ToChange(), cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(result.Error);
        }

        var row = await query.RowAsync(result.Value!, clock.GetUtcNow(), cancellationToken);
        return TypedResults.Ok(AdminStudentListItem.From(row!));
    }

    private static async Task<IResult> LeaveAsync(string studentNumber, ReasonRequest request, CurrentUser user, StudentAdminService students, CatalogueCache catalogue, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
        {
            return ProblemResults.Problem(ProblemTypes.Forbidden);
        }

        var result = await students.LeaveAsync(studentNumber, request.Reason!, userId, cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(result.Error);
        }

        if (result.Value!.Withdrawn > 0)
        {
            await catalogue.InvalidateAsync(cancellationToken);
        }

        return TypedResults.Ok(new StudentLeftResponse(result.Value.StudentNumber, result.Value.LeftAt, result.Value.Withdrawn));
    }

    // GET /api/admin/students/{n}/export.json: what the student would export, audited as student.exported.
    private static async Task<IResult> ExportAsync(
        string studentNumber,
        AdminStudentQuery query,
        StudentExportQuery export,
        AuditWriter audit,
        RushDayDbContext db,
        HttpContext http,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var studentId = await query.IdOfAsync(studentNumber, cancellationToken);
        if (studentId is not { } id)
        {
            return StudentNotFound();
        }

        var now = clock.GetUtcNow();
        var data = await export.ExecuteAsync(id, now, cancellationToken);
        if (data is null)
        {
            return StudentNotFound();
        }

        var number = data.Student.StudentNumber;
        audit.Record(db, AuditActions.StudentExported, AuditSubjects.Student, number, new { studentNumber = number }, id);
        await db.SaveChangesAsync(cancellationToken);

        // The stored number, never the route value (which matched case-insensitively).
        http.Response.Headers.ContentDisposition = StudentExport.ContentDisposition(number);
        return TypedResults.Ok(StudentExport.From(data, now));
    }

    private static async Task<IResult> EnrolAsync(
        string studentNumber,
        OverrideEnrolRequest request,
        CurrentUser user,
        AdminStudentQuery query,
        EnrolmentService enrolments,
        CatalogueCache catalogue,
        CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
        {
            return ProblemResults.Problem(ProblemTypes.Forbidden);
        }

        if (await query.IdOfAsync(studentNumber, cancellationToken) is not { } studentId)
        {
            return StudentNotFound();
        }

        var options = new EnrolOptions(Override: true, ForceCapacity: request.ForceCapacity ?? false, Reason: request.Reason);
        var result = await enrolments.EnrolAsync(studentId, request.ModuleCode!, userId, options, cancellationToken);
        if (!result.Succeeded)
        {
            return EnrolmentProblems.For(result.Failure!);
        }

        if (result.Receipt!.CapacityRaised)
        {
            await catalogue.InvalidateAsync(cancellationToken);
        }

        return TypedResults.Created((string?)null, OverrideEnrolResponse.From(result.Receipt));
    }

    private static async Task<IResult> WithdrawAsync(
        string studentNumber,
        string code,
        ReasonRequest request,
        CurrentUser user,
        AdminStudentQuery query,
        EnrolmentService enrolments,
        CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
        {
            return ProblemResults.Problem(ProblemTypes.Forbidden);
        }

        if (await query.IdOfAsync(studentNumber, cancellationToken) is not { } studentId)
        {
            // Nobody is enrolled for a student who does not exist.
            return ProblemResults.Problem(ProblemTypes.NotEnrolled, "There is no active enrolment on this module.");
        }

        var result = await enrolments.WithdrawAsync(studentId, code, userId, new WithdrawOptions(Override: true, Reason: request.Reason), cancellationToken);
        return result.Succeeded ? TypedResults.NoContent() : EnrolmentProblems.For(result.Failure!);
    }

    private static IResult StudentNotFound() => ProblemResults.Problem(ProblemTypes.StudentNotFound, "No student has that number.");

    private static IResult Problem(StudentAdminError error) => error switch
    {
        StudentAdminError.StudentNotFound => StudentNotFound(),
        StudentAdminError.StudentNumberTaken => ProblemResults.Problem(ProblemTypes.StudentNumberTaken, "A student already has that number."),
        StudentAdminError.StudentLeft => ProblemResults.Problem(ProblemTypes.StudentLeft, "The student has already left."),
        StudentAdminError.DemoAccount => ProblemResults.Problem(ProblemTypes.DemoAccount),
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Not a refusal."),
    };
}
