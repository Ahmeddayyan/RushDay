using RushDay.Api.Contracts;
using RushDay.Api.Security;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Lecturers;

namespace RushDay.Api.Endpoints;

/// <summary>
/// Lecturer records, <c>/api/admin/lecturers</c> (02-api.md section 8.5): list, create, edit (the linked login's
/// display name follows; a demo actor may not rename a real account's login), and mark as left (the linked account is
/// disabled; assignments stay with <c>left: true</c> but carry no authority, and the lecturer's cached module codes
/// are dropped; idempotent).
/// </summary>
public static class AdminLecturerEndpoints
{
    public static RouteGroupBuilder MapAdminLecturerEndpoints(this RouteGroupBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        admin.MapGet("/lecturers", ListAsync).WithName("AdminLecturers");
        admin.MapPost("/lecturers", CreateAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("CreateLecturer");

        var lecturer = admin.MapGroup("/lecturers/" + StaffPatterns.StaffNumberRoute);
        lecturer.MapPut("/", UpdateAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("UpdateLecturer");
        lecturer.MapPost("/leave", LeaveAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("LecturerLeave");
        return admin;
    }

    private static async Task<IResult> ListAsync([AsParameters] LecturerListParameters parameters, LecturerAdminService lecturers, CancellationToken cancellationToken)
    {
        var rows = await lecturers.ListAsync(parameters.Q, cancellationToken);
        return TypedResults.Ok(rows.Select(AdminLecturerView.From).ToList());
    }

    private static async Task<IResult> CreateAsync(CreateLecturerRequest request, LecturerAdminService lecturers, CancellationToken cancellationToken)
    {
        var result = await lecturers.CreateAsync(request.StaffNumber!, request.ToChange(), cancellationToken);
        return result.Succeeded
            ? TypedResults.Created((string?)null, AdminLecturerView.From((await lecturers.RowAsync(result.Value!, cancellationToken))!))
            : Problem(result.Error);
    }

    private static async Task<IResult> UpdateAsync(string staffNumber, UpdateLecturerRequest request, LecturerAdminService lecturers, CancellationToken cancellationToken)
    {
        var result = await lecturers.UpdateAsync(staffNumber, request.ToChange(), cancellationToken);
        return result.Succeeded
            ? TypedResults.Ok(AdminLecturerView.From((await lecturers.RowAsync(result.Value!, cancellationToken))!))
            : Problem(result.Error);
    }

    private static async Task<IResult> LeaveAsync(string staffNumber, ReasonRequest request, LecturerAdminService lecturers, LecturerModuleCache lecturerModules, CancellationToken cancellationToken)
    {
        var result = await lecturers.LeaveAsync(staffNumber, request.Reason!, cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(result.Error);
        }

        // A lecturer who has left teaches nothing (review S6 E5): drop their cached module codes after the commit.
        if (await lecturers.IdOfAsync(result.Value!, cancellationToken) is { } lecturerId)
        {
            await lecturerModules.InvalidateAsync(lecturerId, cancellationToken);
        }

        return TypedResults.Ok(AdminLecturerView.From((await lecturers.RowAsync(result.Value!, cancellationToken))!));
    }

    private static IResult Problem(LecturerAdminError error) => error switch
    {
        LecturerAdminError.LecturerNotFound => ProblemResults.Problem(ProblemTypes.LecturerNotFound, "No lecturer has that staff number."),
        LecturerAdminError.StaffNumberTaken => ProblemResults.Problem(ProblemTypes.StaffNumberTaken, "A lecturer already has that staff number."),
        LecturerAdminError.DemoAccount => ProblemResults.Problem(ProblemTypes.DemoAccount),
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Not a refusal."),
    };
}
