using RushDay.Domain.Enrolments;
using RushDay.Domain.Modules;

namespace RushDay.UnitTests.Enrolments;

public sealed class EnrolmentRulesTests
{
    private static Module HotModule(int capacity = 30, int credits = 15) => new()
    {
        Id = Guid.NewGuid(),
        Code = "CS3099",
        Department = "CS",
        Title = "Advanced Machine Learning",
        Credits = credits,
        Capacity = capacity,
        Semester = Semester.Spring,
    };

    [Fact]
    public void Accepts_when_the_window_is_open_places_remain_and_credits_allow()
    {
        var decision = EnrolmentRules.Evaluate(HotModule(), currentEnrolledCount: 29, studentCreditsInSemester: 45, alreadyEnrolled: false, windowOpen: true);

        Assert.Equal(EnrolmentDecision.Accepted, decision);
    }

    [Fact]
    public void Rejects_when_module_is_full()
    {
        var decision = EnrolmentRules.Evaluate(HotModule(capacity: 30), currentEnrolledCount: 30, studentCreditsInSemester: 0, alreadyEnrolled: false, windowOpen: true);

        Assert.Equal(EnrolmentDecision.ModuleFull, decision);
    }

    [Fact]
    public void Rejects_when_the_window_is_closed()
    {
        var decision = EnrolmentRules.Evaluate(HotModule(), currentEnrolledCount: 0, studentCreditsInSemester: 0, alreadyEnrolled: false, windowOpen: false);

        Assert.Equal(EnrolmentDecision.WindowClosed, decision);
    }

    [Fact]
    public void Already_enrolled_wins_over_every_other_rule()
    {
        var decision = EnrolmentRules.Evaluate(HotModule(capacity: 30), currentEnrolledCount: 30, studentCreditsInSemester: 60, alreadyEnrolled: true, windowOpen: false);

        Assert.Equal(EnrolmentDecision.AlreadyEnrolled, decision);
    }

    [Fact]
    public void Window_closed_wins_over_credit_limit_and_module_full()
    {
        var decision = EnrolmentRules.Evaluate(HotModule(capacity: 30), currentEnrolledCount: 30, studentCreditsInSemester: 60, alreadyEnrolled: false, windowOpen: false);

        Assert.Equal(EnrolmentDecision.WindowClosed, decision);
    }

    [Fact]
    public void Credit_limit_wins_over_module_full()
    {
        var decision = EnrolmentRules.Evaluate(HotModule(capacity: 30), currentEnrolledCount: 30, studentCreditsInSemester: 60, alreadyEnrolled: false, windowOpen: true);

        Assert.Equal(EnrolmentDecision.CreditLimitExceeded, decision);
    }

    [Theory]
    [InlineData(45, EnrolmentDecision.Accepted)]
    [InlineData(46, EnrolmentDecision.CreditLimitExceeded)]
    [InlineData(60, EnrolmentDecision.CreditLimitExceeded)]
    public void Enforces_the_semester_credit_limit_at_the_boundary(int existingCredits, EnrolmentDecision expected)
    {
        var decision = EnrolmentRules.Evaluate(HotModule(credits: 15), currentEnrolledCount: 0, studentCreditsInSemester: existingCredits, alreadyEnrolled: false, windowOpen: true);

        Assert.Equal(expected, decision);
    }

    [Fact]
    public void Max_credits_per_semester_is_sixty()
    {
        Assert.Equal(60, EnrolmentRules.MaxCreditsPerSemester);
    }

    [Fact]
    public void Throws_when_module_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => EnrolmentRules.Evaluate(null!, 0, 0, false, true));
    }
}
