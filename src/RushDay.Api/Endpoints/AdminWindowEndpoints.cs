using RushDay.Api.Auth;
using RushDay.Api.Contracts;
using RushDay.Api.Security;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Enrolments;

namespace RushDay.Api.Endpoints;

/// <summary>
/// The enrolment windows, <c>/api/admin/enrolment-windows</c> (02-api.md section 8.5): list every year, create one per
/// (academic year, semester), change its instants, delete it. Each mutation audits <c>window.*</c> and invalidates
/// <c>windows:all</c>, so the catalogue, the dashboards and enrolment itself see the change within the request.
/// </summary>
public static class AdminWindowEndpoints
{
    public static RouteGroupBuilder MapAdminWindowEndpoints(this RouteGroupBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        admin.MapGet("/enrolment-windows", ListAsync).WithName("AdminWindows");
        admin.MapPost("/enrolment-windows", CreateAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("CreateWindow");
        admin.MapPut("/enrolment-windows/{id:guid}", UpdateAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("UpdateWindow");
        admin.MapDelete("/enrolment-windows/{id:guid}", DeleteAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("DeleteWindow");
        return admin;
    }

    private static async Task<IResult> ListAsync(EnrolmentWindowAdminService windows, TimeProvider clock, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var list = await windows.ListAsync(cancellationToken);
        return TypedResults.Ok(list.Select(w => WindowInfo.From(w, now)).ToList());
    }

    private static async Task<IResult> CreateAsync(
        CreateWindowRequest request,
        CurrentUser user,
        EnrolmentWindowAdminService windows,
        EnrolmentWindowCache cache,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
        {
            return ProblemResults.Problem(ProblemTypes.Forbidden);
        }

        var result = await windows.CreateAsync(request.AcademicYear!, request.Semester!.Value, request.ToDates(), userId, cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(result.Error);
        }

        await cache.InvalidateAsync(cancellationToken);
        return TypedResults.Created((string?)null, WindowInfo.From(result.Window!, clock.GetUtcNow()));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateWindowRequest request,
        EnrolmentWindowAdminService windows,
        EnrolmentWindowCache cache,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var result = await windows.UpdateAsync(id, request.ToDates(), cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(result.Error);
        }

        await cache.InvalidateAsync(cancellationToken);
        return TypedResults.Ok(WindowInfo.From(result.Window!, clock.GetUtcNow()));
    }

    private static async Task<IResult> DeleteAsync(Guid id, EnrolmentWindowAdminService windows, EnrolmentWindowCache cache, CancellationToken cancellationToken)
    {
        var result = await windows.DeleteAsync(id, cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(result.Error);
        }

        await cache.InvalidateAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static IResult Problem(WindowError error) => error switch
    {
        WindowError.WindowNotFound => ProblemResults.Problem(ProblemTypes.WindowNotFound, "No enrolment window has that id."),
        WindowError.WindowExists => ProblemResults.Problem(ProblemTypes.WindowExists, "A window already exists for that academic year and semester."),
        WindowError.WindowDatesInvalid => ProblemResults.Problem(ProblemTypes.WindowDatesInvalid, "A window must open before it closes, and close no later than its withdrawal deadline."),
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Not a refusal."),
    };
}
