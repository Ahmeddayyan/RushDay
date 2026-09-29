using RushDay.Api.Auth;
using RushDay.Api.Contracts;
using RushDay.Domain.Users;
using RushDay.Infrastructure.Announcements;
using RushDay.Infrastructure.Enrolments;

namespace RushDay.Api.Endpoints;

/// <summary>
/// <c>GET /api/announcements</c> for every signed-in role (02-api.md section 8.2): visible now; a student sees university
/// announcements and those of modules they are actively enrolled on this year, a lecturer university and assigned
/// modules, an administrator all; pinned first, then newest first; at most 50. The viewer comes from the claims only.
/// </summary>
public static class AnnouncementEndpoints
{
    public static RouteGroupBuilder MapAnnouncementEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet("/announcements", ListAsync).WithTags("Announcements").WithName("Announcements");
        return api;
    }

    private static async Task<IResult> ListAsync(
        CurrentUser user,
        AnnouncementService announcements,
        EnrolmentWindowService windows,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var calendar = await windows.CurrentAsync(cancellationToken);
        var list = await announcements.ListVisibleAsync(ViewerOf(user), calendar.AcademicYear, clock.GetUtcNow(), cancellationToken);
        return TypedResults.Ok(AnnouncementView.From(list));
    }

    private static AnnouncementViewer ViewerOf(CurrentUser user)
    {
        if (string.Equals(user.Role, RushDayRoles.Admin, StringComparison.Ordinal))
        {
            return AnnouncementViewer.Admin;
        }

        if (string.Equals(user.Role, RushDayRoles.Student, StringComparison.Ordinal) && user.StudentId is { } studentId)
        {
            return AnnouncementViewer.Student(studentId);
        }

        if (string.Equals(user.Role, RushDayRoles.Lecturer, StringComparison.Ordinal) && user.LecturerId is { } lecturerId)
        {
            return AnnouncementViewer.Lecturer(lecturerId);
        }

        return AnnouncementViewer.UniversityOnly;
    }
}
