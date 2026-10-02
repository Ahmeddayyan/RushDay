namespace RushDay.Domain.Grades;

public static class Classification
{
    public static string FromAverage(double weightedAverage) => weightedAverage switch
    {
        >= 70 => "First",
        >= 60 => "Upper Second (2:1)",
        >= 50 => "Lower Second (2:2)",
        >= 40 => "Third",
        _ => "Fail",
    };

    /// <summary>The band label of one mark.</summary>
    public static string Band(int mark) => FromAverage(mark);

    /// <summary>Credit-weighted average mark, or null when nothing has been graded.</summary>
    public static double? WeightedAverage(IReadOnlyCollection<(int Mark, int Credits)> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var totalCredits = results.Sum(r => r.Credits);
        if (totalCredits == 0)
        {
            return null;
        }

        return results.Sum(r => (double)r.Mark * r.Credits) / totalCredits;
    }

    /// <summary>
    /// The (mark, credits) pairs that count towards an average: only <see cref="GradeOutcome.Mark"/> outcomes.
    /// Absences and deferrals never count, whatever their credits.
    /// </summary>
    public static IReadOnlyList<(int Mark, int Credits)> Graded(IEnumerable<(GradeOutcome Outcome, int? Mark, int Credits)> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var graded = new List<(int Mark, int Credits)>();
        foreach (var (outcome, mark, credits) in results)
        {
            if (outcome == GradeOutcome.Mark && mark is { } value)
            {
                graded.Add((value, credits));
            }
        }

        return graded;
    }
}
