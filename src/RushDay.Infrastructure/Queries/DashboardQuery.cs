using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Announcements;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Announcements;
using RushDay.Infrastructure.Grades;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Queries;

/// <summary>The student row as the dashboard and the export show it.</summary>
public sealed record StudentProfileRow(
    Guid Id,
    string StudentNumber,
    string FullName,
    string Programme,
    int YearOfStudy,
    string? Email,
    DateTimeOffset? LeftAt);

/// <summary>What the five dashboard queries return; the endpoint adds the cached windows and publications.</summary>
public sealed record DashboardData(
    StudentProfileRow Student,
    IReadOnlyList<StudentEnrolmentRow> Current,
    IReadOnlyList<StudentEnrolmentRow> Completed,
    IReadOnlyList<TimetableSlotRow> Timetable,
    IReadOnlyList<VisibleGradeRow> Results,
    IReadOnlyList<AnnouncementRecord> Announcements);

/// <summary>
/// <c>GET /api/me/dashboard</c> in exactly five set-based queries whatever the number of modules (04-performance-and-ops.md
/// section 3, D12): (1) the student; (2) active enrolments of every year ⋈ modules, partitioned in memory into this
/// year's modules and earlier years' completed modules; (3) this year's modules' slots in the current semester;
/// (4) the visible grades (<see cref="GradeQueries.VisibleResultsFor"/>); (5) the latest five visible announcements,
/// university or on this year's modules. Settings, windows and publications come from caches.
/// </summary>
public sealed class DashboardQuery(RushDayDbContext db)
{
    /// <summary>Null only when the student row does not exist (then only query (1) ran).</summary>
    public async Task<DashboardData?> ExecuteAsync(Guid studentId, string currentYear, Semester currentSemester, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        // (1)
        var student = await ProfileAsync(db, studentId, cancellationToken);
        if (student is null)
        {
            return null;
        }

        // (2) Every year's active rows; the "results exist" flag of canWithdraw rides along as a per-row EXISTS on the
        // grade status, so query (4) stays exactly the visibility rule.
        var active = await MyEnrolmentsQuery.RowsFor(db, studentId, activeOnly: true).ToListAsync(cancellationToken);
        var current = active
            .Where(r => string.Equals(r.AcademicYear, currentYear, StringComparison.Ordinal))
            .OrderBy(r => r.Semester).ThenBy(r => r.ModuleCode, StringComparer.Ordinal)
            .ToList();
        var completed = active
            .Where(r => !string.Equals(r.AcademicYear, currentYear, StringComparison.Ordinal))
            .OrderByDescending(r => r.AcademicYear, StringComparer.Ordinal).ThenBy(r => r.Semester).ThenBy(r => r.ModuleCode, StringComparer.Ordinal)
            .ToList();
        var currentIds = current.Select(r => r.ModuleId).ToArray();

        // (3) The whole week of this year's modules that run in the current semester.
        var slots = await TimetableQuery.SlotsOf(db, db.Modules.AsNoTracking().Where(m => currentIds.Contains(m.Id) && m.Semester == currentSemester))
            .ToListAsync(cancellationToken);

        // (4) Visible grades only, every year.
        var results = await GradeQueries.VisibleResultsFor(db, studentId, now).ToListAsync(cancellationToken);

        // (5) Latest five visible announcements: university, or on a module with an active enrolment this year.
        var announcements = await AnnouncementQueries.Project(
                db,
                AnnouncementQueries.VisibleAt(db, now).Where(a => a.Scope == AnnouncementScope.University || (a.ModuleId != null && currentIds.Contains(a.ModuleId.Value))))
            .Take(AnnouncementQueries.DashboardLimit)
            .ToListAsync(cancellationToken);

        return new DashboardData(student, current, completed, TimetableSlotRow.InWeekOrder(slots), results, announcements);
    }

    /// <summary>The student row (one command).</summary>
    public static Task<StudentProfileRow?> ProfileAsync(RushDayDbContext db, Guid studentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Students.AsNoTracking()
            .Where(s => s.Id == studentId)
            .Select(s => new StudentProfileRow(s.Id, s.StudentNumber, s.FullName, s.Programme, s.YearOfStudy, s.Email, s.LeftAt))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
