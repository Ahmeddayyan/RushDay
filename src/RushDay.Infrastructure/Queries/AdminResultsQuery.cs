using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Grades;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Queries;

/// <summary>One module of <c>GET /api/admin/results</c>; <c>enrolledCount</c> is <c>marks.total</c>.</summary>
public sealed record AdminResultsModule(string Code, string Title, string? Leader, MarksStatusData Marks);

public sealed record AdminResultsData(string AcademicYear, Semester Semester, IReadOnlyList<AdminResultsModule> Modules, IReadOnlyList<PublicationRecord> Publications);

/// <summary>
/// Submission progress for one (academic year, semester) (02-api.md section 8.5, 04-performance-and-ops.md section 3):
/// the semester's modules with their leader, the <see cref="MarksStatusQuery"/> aggregate over that year's active
/// enrolments, and the (year, semester)'s publications. Sorted by status (draft, submitted, scheduled, published, and
/// modules without students last), then by code.
/// </summary>
public sealed class AdminResultsQuery(RushDayDbContext db, ResultsPublicationService publications)
{
    public async Task<AdminResultsData> ExecuteAsync(string academicYear, Semester semester, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var modules = await (
            from m in db.Modules.AsNoTracking().Where(m => m.Semester == semester)
            select new
            {
                m.Id,
                m.Code,
                m.Title,
                Leader = (from ml in db.ModuleLecturers
                          join l in db.Lecturers on ml.LecturerId equals l.Id
                          where ml.ModuleId == m.Id && ml.Role == ModuleLecturerRole.Leader
                          orderby l.StaffNumber
                          select l.Title + " " + l.FullName).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var statuses = await MarksStatusQuery.ForModulesAsync(db, academicYear, now, [.. modules.Select(m => m.Id)], cancellationToken);
        var rows = modules
            .Select(m => new AdminResultsModule(m.Code, m.Title, m.Leader, MarksStatusQuery.Of(statuses, m.Id)))
            .OrderBy(m => SortKey(m.Marks.Status))
            .ThenBy(m => m.Code, StringComparer.Ordinal)
            .ToList();

        var history = await publications.ListAsync(academicYear, semester, cancellationToken);
        return new AdminResultsData(academicYear, semester, rows, history);
    }

    /// <summary>Lifecycle order with <c>noStudents</c> last.</summary>
    public static int SortKey(MarksState state) => state switch
    {
        MarksState.Draft => 0,
        MarksState.Submitted => 1,
        MarksState.Scheduled => 2,
        MarksState.Published => 3,
        _ => 4,
    };
}
