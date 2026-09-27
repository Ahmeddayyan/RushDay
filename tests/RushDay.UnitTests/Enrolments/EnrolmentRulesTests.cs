using RushDay.Domain.Enrolments;
using RushDay.Domain.Modules;

namespace RushDay.UnitTests.Enrolments;

public sealed class EnrolmentRulesTests
{
    private static Module HotModule(int capacity = 30, int credits = 15) => new()
    {
        Id = Guid.NewGuid(),
        Code = "CS3099",
        Title = "Advanced Machine Learning",
        Credits = credits,
        Capacity = capacity,
        Semester = Semester.Spring,
    };

    [Fact]
    public void Accepts_when_places_remain_and_credits_allow()
    {
        var decision = EnrolmentRules.Evaluate(HotModule(), currentEnrolledCount: 29, studentCreditsInSemester: 45, alreadyEnrolled: false);

        Assert.Equal(EnrolmentDecision.Accepted, decision);
    }

    [Fact]
    public void Rejects_when_module_is_full()
    {
        var decision = EnrolmentRules.Evaluate(HotModule(capacity: 30), currentEnrolledCount: 30, studentCreditsInSemester: 0, alreadyEnrolled: false);

        Assert.Equal(EnrolmentDecision.ModuleFull, decision);
    }

    [Fact]
    public void Already_enrolled_wins_over_every_other_rule()
    {
        var decision = EnrolmentRules.Evaluate(HotModule(capacity: 30), currentEnrolledCount: 30, studentCreditsInSemester: 60, alreadyEnrolled: true);

        Assert.Equal(EnrolmentDecision.AlreadyEnrolled, decision);
    }

    [Theory]
    [InlineData(45, EnrolmentDecision.Accepted)]
    [InlineData(46, EnrolmentDecision.CreditLimitExceeded)]
    [InlineData(60, EnrolmentDecision.CreditLimitExceeded)]
    public void Enforces_the_semester_credit_limit_at_the_boundary(int existingCredits, EnrolmentDecision expected)
    {
        var decision = EnrolmentRules.Evaluate(HotModule(credits: 15), currentEnrolledCount: 0, studentCreditsInSemester: existingCredits, alreadyEnrolled: false);

        Assert.Equal(expected, decision);
    }

    [Fact]
    public void Throws_when_module_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => EnrolmentRules.Evaluate(null!, 0, 0, false));
    }
}
