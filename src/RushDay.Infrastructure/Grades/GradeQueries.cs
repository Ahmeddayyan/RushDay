using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Queries;

namespace RushDay.Infrastructure.Grades;

/// <summary>
/// One visible grade as a student sees it: joined to its module and to the active enrolment that gives it its academic
/// year. <see cref="Mark"/> is null iff <see cref="Outcome"/> is not <see cref="GradeOutcome.Mark"/>.
/// </summary>
public sealed record VisibleGradeRow(
    Guid ModuleId,
    string ModuleCode,
    string ModuleTitle,
    int Credits,
    Semester Semester,
    string AcademicYear,
    GradeOutcome Outcome,
    int? Mark,
    DateTimeOffset PublishedAt,
    DateTimeOffset? CorrectedAt,
    int Version);

/// <summary>
/// The visibility rule that must be impossible to get wrong (00-overview.md section 4.3, 02-api.md section 8.3, T6): a
/// student sees a grade iff it is Published, its <c>published_at</c> has passed, and the student's enrolment on that
/// module is Active. Every student route reads grade rows through this class only: marks through
/// <see cref="VisibleResultsFor"/> (dashboard, results, export), which is built on <see cref="VisibleToStudents"/>;
/// scheduled instants, never marks, through <see cref="ScheduledInstantsFor"/>; statuses, never marks, through
/// <see cref="WithResults"/>.
/// Draft, Submitted and future-published marks, and the marks of a withdrawn enrolment, never leave the database on a
/// student route.
/// </summary>
public static class GradeQueries
{
    /// <summary>
    /// <c>g.status = 'Published' AND g.published_at &lt;= @now AND EXISTS (SELECT 1 FROM enrolments e WHERE
    /// e.student_id = g.student_id AND e.module_id = g.module_id AND e.status = 'Active')</c>.
    /// </summary>
    public static IQueryable<Grade> VisibleToStudents(RushDayDbContext db, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(db);

        return db.Grades.AsNoTracking()
            .Where(g => g.Status == GradeStatus.Published
                && g.PublishedAt <= now
                && db.Enrolments.Any(e => e.StudentId == g.StudentId && e.ModuleId == g.ModuleId && e.Status == EnrolmentStatus.Active));
    }

    /// <summary>
    /// One student's visible grades joined to modules and to the active enrolment for the academic year (one command):
    /// the dashboard's query (4), the results page's first query and the export's grade query.
    /// </summary>
    public static IQueryable<VisibleGradeRow> VisibleResultsFor(RushDayDbContext db, Guid studentId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(db);

        return from g in VisibleToStudents(db, now).Where(g => g.StudentId == studentId)
               join e in db.Enrolments.AsNoTracking() on new { g.StudentId, g.ModuleId } equals new { e.StudentId, e.ModuleId }
               join m in db.Modules.AsNoTracking() on g.ModuleId equals m.Id
               where e.Status == EnrolmentStatus.Active
               orderby e.AcademicYear descending, m.Semester, m.Code
               select new VisibleGradeRow(
                   m.Id,
                   m.Code,
                   m.Title,
                   m.Credits,
                   m.Semester,
                   e.AcademicYear,
                   g.Outcome,
                   g.Mark,
                   g.PublishedAt!.Value,
                   g.CorrectedAt,
                   g.Version);
    }

    /// <summary>
    /// The earliest future instant per (academic year, semester) at which one student's published grades become
    /// visible: <c>SELECT e.academic_year, m.semester, min(g.published_at) FROM grades g JOIN modules m JOIN enrolments e
    /// ... WHERE g.student_id = @s AND e.status = 'Active' AND g.status = 'Published' AND g.published_at &gt; @now
    /// GROUP BY e.academic_year, m.semester</c>. It projects instants only, never a mark, so the results page can say
    /// "scheduled" without a scheduled mark leaving the database; a withdrawn enrolment's grade is not even scheduled.
    /// </summary>
    public static IQueryable<ScheduledPair> ScheduledInstantsFor(RushDayDbContext db, Guid studentId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(db);

        return from g in db.Grades.AsNoTracking()
               join m in db.Modules.AsNoTracking() on g.ModuleId equals m.Id
               join e in db.Enrolments.AsNoTracking() on new { g.StudentId, g.ModuleId } equals new { e.StudentId, e.ModuleId }
               where g.StudentId == studentId && e.Status == EnrolmentStatus.Active && g.Status == GradeStatus.Published && g.PublishedAt > now
               group g.PublishedAt by new { e.AcademicYear, m.Semester } into pair
               select new ScheduledPair(pair.Key.AcademicYear, pair.Key.Semester, pair.Min()!.Value);
    }

    /// <summary>
    /// True when a Submitted or Published grade exists for (student, module): the "results exist" rule of enrolment,
    /// withdrawal and <c>canWithdraw</c>. It reads the status only, never the mark.
    /// </summary>
    public static IQueryable<Grade> WithResults(RushDayDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Grades.AsNoTracking().Where(g => g.Status == GradeStatus.Submitted || g.Status == GradeStatus.Published);
    }

    /// <summary>
    /// The credit-weighted average and classification of visible grades whose outcome is a mark (absences and deferrals
    /// never count): <c>Classification.Graded</c>, <c>WeightedAverage</c>, <c>FromAverage</c>.
    /// </summary>
    public static (double? WeightedAverage, string? Classification) Summarise(IEnumerable<VisibleGradeRow> visible)
    {
        ArgumentNullException.ThrowIfNull(visible);

        var graded = Classification.Graded(visible.Select(r => (r.Outcome, r.Mark, r.Credits)));
        var average = Classification.WeightedAverage(graded);
        return (average, average is { } value ? Classification.FromAverage(value) : null);
    }
}
