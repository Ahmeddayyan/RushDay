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
}
