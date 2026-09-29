using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Grades;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Queries;

/// <summary><c>results.semesters[].state</c> (02-api.md section 8.3).</summary>
public enum ResultsState
{
    Published,
    Scheduled,
    Pending,
}

/// <summary>An (academic year, semester) pair; grades take their year from their enrolment.</summary>
public sealed record YearSemester(string AcademicYear, Semester Semester);

/// <summary>The earliest future instant at which a pair's published grades become visible.</summary>
public sealed record ScheduledPair(string AcademicYear, Semester Semester, DateTimeOffset PublishAt);

/// <summary>One group of the results page. <see cref="PublishAt"/> is set only for a scheduled group; the mark of a scheduled grade is never sent.</summary>
public sealed record ResultsSemester(string AcademicYear, Semester Semester, ResultsState State, DateTimeOffset? PublishAt, IReadOnlyList<VisibleGradeRow> Results);

public sealed record ResultsData(IReadOnlyList<ResultsSemester> Semesters, double? WeightedAverage, string? Classification);

/// <summary>
/// <c>GET /api/me/results</c> in three queries (04-performance-and-ops.md section 3): the visible grades, the earliest
/// scheduled instant per (year, semester), and the (year, semester) pairs of the student's active enrolments; then
/// <see cref="Group"/> decides each pair's state.
/// </summary>
public sealed class ResultsQuery(RushDayDbContext db)
{
    public async Task<ResultsData> ExecuteAsync(Guid studentId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var visible = await GradeQueries.VisibleResultsFor(db, studentId, now).ToListAsync(cancellationToken);

        // Instants only: a scheduled grade's mark never leaves the database on a student route.
        var scheduled = await GradeQueries.ScheduledInstantsFor(db, studentId, now).ToListAsync(cancellationToken);

        var pending = await (
            from e in db.Enrolments.AsNoTracking()
            join m in db.Modules.AsNoTracking() on e.ModuleId equals m.Id
            where e.StudentId == studentId && e.Status == EnrolmentStatus.Active
            select new { e.AcademicYear, m.Semester })
            .Distinct()
            .ToListAsync(cancellationToken);

        var semesters = Group(visible, scheduled, pending.Select(p => new YearSemester(p.AcademicYear, p.Semester)));
        var (average, classification) = GradeQueries.Summarise(visible);
        return new ResultsData(semesters, average, classification);
    }

    /// <summary>
    /// The grouping rule of 02-api.md section 8.3: every (academic year, semester) pair is <c>published</c> when at
    /// least one visible grade exists for it; else <c>scheduled</c> when a published grade on it becomes visible later
    /// (<c>publishAt</c> = the earliest such instant); else <c>pending</c> when the student has an active enrolment in
    /// it; pairs with none of these are omitted. Newest year first, autumn before spring; results by module code.
    /// </summary>
    public static IReadOnlyList<ResultsSemester> Group(
        IEnumerable<VisibleGradeRow> visible,
        IEnumerable<ScheduledPair> scheduled,
        IEnumerable<YearSemester> pending)
    {
        ArgumentNullException.ThrowIfNull(visible);
        ArgumentNullException.ThrowIfNull(scheduled);
        ArgumentNullException.ThrowIfNull(pending);

        var visibleByPair = visible
            .GroupBy(r => new YearSemester(r.AcademicYear, r.Semester))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<VisibleGradeRow>)[.. g.OrderBy(r => r.ModuleCode, StringComparer.Ordinal)]);
        var scheduledByPair = scheduled
            .GroupBy(s => new YearSemester(s.AcademicYear, s.Semester))
            .ToDictionary(g => g.Key, g => g.Min(s => s.PublishAt));
        var pendingPairs = pending.ToHashSet();

        var pairs = visibleByPair.Keys.Concat(scheduledByPair.Keys).Concat(pendingPairs).Distinct();
        var groups = new List<ResultsSemester>();
        foreach (var pair in pairs)
        {
            if (visibleByPair.TryGetValue(pair, out var results))
            {
                groups.Add(new ResultsSemester(pair.AcademicYear, pair.Semester, ResultsState.Published, null, results));
            }
            else if (scheduledByPair.TryGetValue(pair, out var publishAt))
            {
                groups.Add(new ResultsSemester(pair.AcademicYear, pair.Semester, ResultsState.Scheduled, publishAt, []));
            }
            else if (pendingPairs.Contains(pair))
            {
                groups.Add(new ResultsSemester(pair.AcademicYear, pair.Semester, ResultsState.Pending, null, []));
            }
        }

        return [.. groups.OrderByDescending(g => g.AcademicYear, StringComparer.Ordinal).ThenBy(g => g.Semester)];
    }
}
