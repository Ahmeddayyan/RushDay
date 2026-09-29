using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Grades;
using RushDay.Infrastructure.Queries;

namespace RushDay.IntegrationTests.Student;

/// <summary>
/// The grouping rule of <c>ResultsQuery</c> (02-api.md section 8.3, 04-performance-and-ops.md section 3), as a pure unit
/// test without a database: a pair is published when a visible grade exists, else scheduled (earliest instant), else
/// pending; pairs with none are omitted; newest year first, autumn before spring.
/// </summary>
public sealed class ResultsGroupingTests
{
    private static readonly DateTimeOffset Past = new(2026, 1, 26, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Each_pair_takes_the_first_state_that_applies()
    {
        VisibleGradeRow[] visible =
        [
            Grade("MA1001", "2025/26", Semester.Autumn, 71),
            Grade("CS1001", "2025/26", Semester.Autumn, 55),
            Grade("PH1001", "2026/27", Semester.Autumn, 40),
        ];
        ScheduledPair[] scheduled =
        [
            new("2026/27", Semester.Autumn, new DateTimeOffset(2027, 1, 20, 9, 0, 0, TimeSpan.Zero)),
            new("2026/27", Semester.Spring, new DateTimeOffset(2027, 6, 20, 9, 0, 0, TimeSpan.Zero)),
            new("2026/27", Semester.Spring, new DateTimeOffset(2027, 6, 10, 9, 0, 0, TimeSpan.Zero)),
        ];
        YearSemester[] pending =
        [
            new("2025/26", Semester.Autumn),
            new("2026/27", Semester.Autumn),
            new("2026/27", Semester.Spring),
            new("2027/28", Semester.Autumn),
        ];

        var groups = ResultsQuery.Group(visible, scheduled, pending);

        Assert.Equal(
            [
                ("2027/28", Semester.Autumn, ResultsState.Pending),
                ("2026/27", Semester.Autumn, ResultsState.Published),
                ("2026/27", Semester.Spring, ResultsState.Scheduled),
                ("2025/26", Semester.Autumn, ResultsState.Published),
            ],
            groups.Select(g => (g.AcademicYear, g.Semester, g.State)));

        // A published group lists its visible results by code and carries no instant, even when more are scheduled.
        Assert.Equal(["CS1001", "MA1001"], groups[3].Results.Select(r => r.ModuleCode));
        Assert.Null(groups[1].PublishAt);

        // A scheduled group carries the earliest instant and no result.
        Assert.Equal(new DateTimeOffset(2027, 6, 10, 9, 0, 0, TimeSpan.Zero), groups[2].PublishAt);
        Assert.Empty(groups[2].Results);

        Assert.Null(groups[0].PublishAt);
        Assert.Empty(groups[0].Results);
    }

    [Fact]
    public void Pairs_with_nothing_are_omitted()
    {
        Assert.Empty(ResultsQuery.Group([], [], []));

        // A visible grade whose enrolment pair is not otherwise known still forms its own group.
        var only = Assert.Single(ResultsQuery.Group([Grade("EE2001", "2024/25", Semester.Spring, 62)], [], []));
        Assert.Equal(("2024/25", Semester.Spring, ResultsState.Published), (only.AcademicYear, only.Semester, only.State));
    }

    [Fact]
    public void Averages_count_marks_only()
    {
        VisibleGradeRow[] visible =
        [
            Grade("MA1001", "2025/26", Semester.Autumn, 70, credits: 30),
            Grade("CS1001", "2025/26", Semester.Autumn, 40, credits: 15),
            Grade("PH1001", "2025/26", Semester.Autumn, null, outcome: GradeOutcome.Absent),
            Grade("EE1001", "2025/26", Semester.Autumn, null, outcome: GradeOutcome.Deferred),
        ];

        var (average, classification) = GradeQueries.Summarise(visible);

        Assert.Equal(60, average);
        Assert.Equal("Upper Second (2:1)", classification);
        Assert.Equal((null, null), GradeQueries.Summarise([Grade("PH1001", "2025/26", Semester.Autumn, null, outcome: GradeOutcome.Absent)]));
    }

    private static VisibleGradeRow Grade(string code, string year, Semester semester, int? mark, int credits = 15, GradeOutcome outcome = GradeOutcome.Mark) =>
        new(Guid.NewGuid(), code, "Title of " + code, credits, semester, year, outcome, mark, Past, null, 1);
}
