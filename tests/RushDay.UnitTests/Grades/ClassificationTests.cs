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
}
