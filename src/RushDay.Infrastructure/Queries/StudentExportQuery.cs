using Microsoft.EntityFrameworkCore;
using RushDay.Infrastructure.Grades;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Queries;

/// <summary>What a personal data export holds: the student, every enrolment row, and the visible grades only.</summary>
public sealed record StudentExportData(
    StudentProfileRow Student,
    IReadOnlyList<StudentEnrolmentRow> Enrolments,
    IReadOnlyList<VisibleGradeRow> Grades,
    double? WeightedAverage,
    string? Classification);

/// <summary>
/// <c>GET /api/me/export.json</c> and the administrator's export on a student's behalf, in three queries
/// (04-performance-and-ops.md section 3): the student; enrolments ⋈ modules of every year and status; the visible grades
/// ⋈ modules ⋈ enrolments (<see cref="GradeQueries.VisibleResultsFor"/>), so no draft, submitted or future mark is ever
/// exported. The caller writes the audit row (<c>student.exported_self</c> or <c>student.exported</c>).
/// </summary>
public sealed class StudentExportQuery(RushDayDbContext db)
{
    /// <summary>Null when the student does not exist.</summary>
    public async Task<StudentExportData?> ExecuteAsync(Guid studentId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var student = await DashboardQuery.ProfileAsync(db, studentId, cancellationToken);
        if (student is null)
        {
            return null;
        }

        var enrolments = await MyEnrolmentsQuery.RowsFor(db, studentId, activeOnly: false).ToListAsync(cancellationToken);
        var grades = await GradeQueries.VisibleResultsFor(db, studentId, now).ToListAsync(cancellationToken);
        var (average, classification) = GradeQueries.Summarise(grades);

        return new StudentExportData(
            student,
            [.. enrolments.OrderByDescending(e => e.AcademicYear, StringComparer.Ordinal).ThenBy(e => e.Semester).ThenBy(e => e.ModuleCode, StringComparer.Ordinal)],
            grades,
            average,
            classification);
    }
}
