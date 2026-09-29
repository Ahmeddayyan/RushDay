using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Enrolments;
using RushDay.Infrastructure.Grades;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Queries;

/// <summary>
/// One of a student's enrolment rows joined to its module. <see cref="HasResult"/> is true when a Submitted or
/// Published grade exists for (student, module): the "results" condition of <c>canWithdraw</c>. It says nothing about
/// the mark, which only <see cref="GradeQueries.VisibleToStudents"/> reads.
/// </summary>
public sealed record StudentEnrolmentRow(
    Guid EnrolmentId,
    Guid ModuleId,
    string ModuleCode,
    string Title,
    int Credits,
    Semester Semester,
    string AcademicYear,
    EnrolmentStatus Status,
    EnrolmentSource Source,
    DateTimeOffset EnrolledAt,
    DateTimeOffset? WithdrawnAt,
    bool HasResult);

/// <summary>A <see cref="StudentEnrolmentRow"/> with the withdrawal rule of 02-api.md section 7 evaluated.</summary>
public sealed record MyEnrolmentItem(StudentEnrolmentRow Row, WithdrawalEligibility Withdrawal);

/// <summary>
/// <c>GET /api/me/enrolments</c> (04-performance-and-ops.md section 3): one query, enrolments ⋈ modules with a
/// per-row <c>EXISTS</c> on submitted or published grades, every status and year; current year first, then
/// <c>enrolledAt</c> descending.
/// </summary>
public sealed class MyEnrolmentsQuery(RushDayDbContext db)
{
    /// <summary>The student's enrolment rows (all, or only the active ones), one command.</summary>
    public static IQueryable<StudentEnrolmentRow> RowsFor(RushDayDbContext db, Guid studentId, bool activeOnly)
    {
        ArgumentNullException.ThrowIfNull(db);

        var enrolments = db.Enrolments.AsNoTracking().Where(e => e.StudentId == studentId);
        if (activeOnly)
        {
            enrolments = enrolments.Where(e => e.Status == EnrolmentStatus.Active);
        }

        var results = GradeQueries.WithResults(db);
        return from e in enrolments
               join m in db.Modules.AsNoTracking() on e.ModuleId equals m.Id
               select new StudentEnrolmentRow(
                   e.Id,
                   m.Id,
                   m.Code,
                   m.Title,
                   m.Credits,
                   m.Semester,
                   e.AcademicYear,
                   e.Status,
                   e.Source,
                   e.EnrolledAt,
                   e.WithdrawnAt,
                   results.Any(g => g.StudentId == e.StudentId && g.ModuleId == e.ModuleId));
    }

    public async Task<IReadOnlyList<MyEnrolmentItem>> ExecuteAsync(
        Guid studentId,
        string currentYear,
        IReadOnlyList<EnrolmentWindowSnapshot> windows,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(windows);

        var rows = await RowsFor(db, studentId, activeOnly: false).ToListAsync(cancellationToken);

        return
        [
            .. rows
                .OrderByDescending(r => string.Equals(r.AcademicYear, currentYear, StringComparison.Ordinal))
                .ThenByDescending(r => r.EnrolledAt)
                .ThenBy(r => r.ModuleCode, StringComparer.Ordinal)
                .Select(r => new MyEnrolmentItem(
                    r,
                    WithdrawalEligibility.Evaluate(r.Status, r.AcademicYear, r.HasResult, currentYear, EnrolmentWindowService.Find(windows, r.AcademicYear, r.Semester), now))),
        ];
    }
}
