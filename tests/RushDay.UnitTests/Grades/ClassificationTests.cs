using RushDay.Domain.Grades;

namespace RushDay.UnitTests.Grades;

public sealed class ClassificationTests
{
    [Theory]
    [InlineData(70.0, "First")]
    [InlineData(69.9, "Upper Second (2:1)")]
    [InlineData(60.0, "Upper Second (2:1)")]
    [InlineData(59.9, "Lower Second (2:2)")]
    [InlineData(50.0, "Lower Second (2:2)")]
    [InlineData(40.0, "Third")]
    [InlineData(39.9, "Fail")]
    public void Maps_averages_to_uk_degree_classifications(double average, string expected)
    {
        Assert.Equal(expected, Classification.FromAverage(average));
    }

    [Theory]
    [InlineData(100, "First")]
    [InlineData(70, "First")]
    [InlineData(69, "Upper Second (2:1)")]
    [InlineData(50, "Lower Second (2:2)")]
    [InlineData(40, "Third")]
    [InlineData(39, "Fail")]
    [InlineData(0, "Fail")]
    public void Band_is_the_label_of_one_mark(int mark, string expected)
    {
        Assert.Equal(expected, Classification.Band(mark));
    }

    [Fact]
    public void Weighted_average_weights_marks_by_credits()
    {
        var results = new[] { (Mark: 80, Credits: 30), (Mark: 50, Credits: 15) };

        var average = Classification.WeightedAverage(results);

        Assert.NotNull(average);
        Assert.Equal(70.0, average.Value, precision: 5);
    }

    [Fact]
    public void Weighted_average_is_null_when_nothing_is_graded()
    {
        Assert.Null(Classification.WeightedAverage([]));
    }

    [Fact]
    public void Graded_keeps_only_mark_outcomes()
    {
        var graded = Classification.Graded(
        [
            (GradeOutcome.Mark, 80, 30),
            (GradeOutcome.Absent, null, 15),
            (GradeOutcome.Deferred, null, 15),
            (GradeOutcome.Mark, 50, 15),
        ]);

        Assert.Equal([(80, 30), (50, 15)], graded);
        Assert.Equal(70.0, Classification.WeightedAverage(graded)!.Value, precision: 5);
    }

    [Fact]
    public void Graded_is_empty_when_every_outcome_is_an_absence_or_deferral()
    {
        var graded = Classification.Graded([(GradeOutcome.Absent, null, 15), (GradeOutcome.Deferred, null, 15)]);

        Assert.Empty(graded);
        Assert.Null(Classification.WeightedAverage(graded));
    }

    [Fact]
    public void Graded_throws_when_results_are_null()
    {
        Assert.Throws<ArgumentNullException>(() => Classification.Graded(null!));
    }
}
