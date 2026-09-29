using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Queries;

/// <summary>The overview's stat tiles.</summary>
public sealed record OverviewCounts(int Students, int Lecturers, int Modules, int ActiveEnrolments, int Accounts, int LockedAccounts, int DisabledAccounts);

/// <summary>Submission progress of one semester of the current year; <see cref="ModulesTotal"/> excludes modules without students.</summary>
public sealed record SubmissionProgress(Semester Semester, int ModulesTotal, int NoStudents, int Draft, int Submitted, int Scheduled, int Published);

public sealed record OverviewData(OverviewCounts Counts, IReadOnlyList<SubmissionProgress> SubmissionProgress, IReadOnlyList<AuditEventRow> RecentAudit);

/// <summary>
/// <c>GET /api/admin/overview</c>: table counts (active enrolments of the current academic year only; locked and
/// disabled accounts by the <c>AccountView.state</c> rule), submission progress per semester from the
/// <see cref="MarksStatusQuery"/> aggregate, and the ten most recent audit rows. Windows, publications and the
/// database check are added by the endpoint.
/// </summary>
public sealed class OverviewQuery(RushDayDbContext db)
{
    public const int RecentAuditCount = 10;

    public async Task<OverviewData> ExecuteAsync(string academicYear, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var counts = new OverviewCounts(
            await db.Students.CountAsync(cancellationToken),
            await db.Lecturers.CountAsync(cancellationToken),
            await db.Modules.CountAsync(cancellationToken),
            await db.Enrolments.CountAsync(e => e.Status == EnrolmentStatus.Active && e.AcademicYear == academicYear, cancellationToken),
            await db.Users.CountAsync(cancellationToken),
            await db.Users.CountAsync(u => u.DisabledAt == null && u.LockoutEnd > now, cancellationToken),
            await db.Users.CountAsync(u => u.DisabledAt != null, cancellationToken));

        var modules = await db.Modules.AsNoTracking().Select(m => new { m.Id, m.Semester }).ToListAsync(cancellationToken);
        var statuses = await MarksStatusQuery.ForModulesAsync(db, academicYear, now, moduleIds: null, cancellationToken);

        var progress = new List<SubmissionProgress>();
        foreach (var semester in new[] { Semester.Autumn, Semester.Spring })
        {
            var states = modules.Where(m => m.Semester == semester).Select(m => MarksStatusQuery.Of(statuses, m.Id).Status).ToList();
            int Count(MarksState state) => states.Count(s => s == state);
            progress.Add(new SubmissionProgress(
                semester,
                states.Count(s => s != MarksState.NoStudents),
                Count(MarksState.NoStudents),
                Count(MarksState.Draft),
                Count(MarksState.Submitted),
                Count(MarksState.Scheduled),
                Count(MarksState.Published)));
        }

        var recent = await AuditQuery.Project(db, db.AuditEvents.AsNoTracking().OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id).Take(RecentAuditCount))
            .ToListAsync(cancellationToken);

        return new OverviewData(counts, progress, [.. recent.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)]);
    }
}
