using RushDay.Domain.Modules;
using RushDay.Infrastructure.Queries;

namespace RushDay.Api.Contracts;

/// <summary><c>database</c> of the overview: the result of the <c>/api/health/ready</c> check, run in-process.</summary>
public enum DatabaseState
{
    Ok,
    Degraded,
}

public sealed record OverviewCountsView(int Students, int Lecturers, int Modules, int ActiveEnrolments, int Accounts, int LockedAccounts, int DisabledAccounts)
{
    public static OverviewCountsView From(OverviewCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        return new OverviewCountsView(counts.Students, counts.Lecturers, counts.Modules, counts.ActiveEnrolments, counts.Accounts, counts.LockedAccounts, counts.DisabledAccounts);
    }
}

/// <summary><c>submissionProgress[]</c>: <c>modulesTotal</c> excludes modules without students.</summary>
public sealed record SubmissionProgressView(Semester Semester, int ModulesTotal, int NoStudents, int Draft, int Submitted, int Scheduled, int Published)
{
    public static SubmissionProgressView From(SubmissionProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return new SubmissionProgressView(progress.Semester, progress.ModulesTotal, progress.NoStudents, progress.Draft, progress.Submitted, progress.Scheduled, progress.Published);
    }
}

/// <summary><c>GET /api/admin/overview</c> (02-api.md section 8.5).</summary>
public sealed record OverviewResponse(
    OverviewCountsView Counts,
    string AcademicYear,
    IReadOnlyList<WindowInfo> EnrolmentWindows,
    PublicationInfo? NextPublication,
    PublicationInfo? LatestPublication,
    IReadOnlyList<SubmissionProgressView> SubmissionProgress,
    IReadOnlyList<AuditEventView> RecentAudit,
    DatabaseState Database);
