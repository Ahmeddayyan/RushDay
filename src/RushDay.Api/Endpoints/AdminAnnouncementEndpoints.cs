using RushDay.Api.Auth;
using RushDay.Api.Contracts;
using RushDay.Api.Security;
using RushDay.Infrastructure.Announcements;

namespace RushDay.Api.Endpoints;

/// <summary>
/// Announcements for administrators, <c>/api/admin/announcements</c> (02-api.md section 8.5): every scope listed
/// (future and expired included, deleted never); new ones are university-wide; administrators may edit or delete an
/// announcement of any scope. <see cref="AnnouncementService"/> audits each mutation and invalidates
/// <c>announcements:university</c> for university rows.
/// </summary>
public static class AdminAnnouncementEndpoints
{
    public static RouteGroupBuilder MapAdminAnnouncementEndpoints(this RouteGroupBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        admin.MapGet("/announcements", ListAsync).WithName("AdminAnnouncements");
        admin.MapPost("/announcements", CreateAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("CreateUniversityAnnouncement");
        admin.MapPut("/announcements/{id:guid}", UpdateAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("UpdateAnnouncement");
        admin.MapDelete("/announcements/{id:guid}", DeleteAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("DeleteAnnouncement");
        return admin;
    }

    private static async Task<IResult> ListAsync(AnnouncementService announcements, CancellationToken cancellationToken) =>
        TypedResults.Ok(AnnouncementView.From(await announcements.ListAllAsync(cancellationToken)));

    // The administrator's calls to the service: moduleId null creates a university announcement, moduleScope null
    // resolves any scope (administrators only; the lecturer routes always pass their module).
    private static async Task<IResult> CreateAsync(AnnouncementRequest request, CurrentUser user, AnnouncementService announcements, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
        {
            return ProblemResults.Problem(ProblemTypes.Forbidden);
        }

        var created = await announcements.CreateAsync(request.ToDraft(), moduleId: null, userId, cancellationToken);
        return TypedResults.Created((string?)null, AnnouncementView.From(created));
    }

    private static async Task<IResult> UpdateAsync(Guid id, AnnouncementRequest request, AnnouncementService announcements, CancellationToken cancellationToken)
    {
        var updated = await announcements.UpdateAsync(id, request.ToDraft(), moduleScope: null, cancellationToken);
        return updated is null
            ? ProblemResults.Problem(ProblemTypes.AnnouncementNotFound, "No announcement has that id.")
            : TypedResults.Ok(AnnouncementView.From(updated));
    }

    private static async Task<IResult> DeleteAsync(Guid id, AnnouncementService announcements, CancellationToken cancellationToken) =>
        await announcements.DeleteAsync(id, moduleScope: null, cancellationToken)
            ? TypedResults.NoContent()
            : ProblemResults.Problem(ProblemTypes.AnnouncementNotFound, "No announcement has that id.");
}
