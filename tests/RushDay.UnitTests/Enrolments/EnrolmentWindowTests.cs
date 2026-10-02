using RushDay.Domain.Enrolments;
using RushDay.Domain.Modules;

namespace RushDay.UnitTests.Enrolments;

public sealed class EnrolmentWindowTests
{
    private static readonly DateTimeOffset Opens = new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Closes = new(2027, 1, 29, 17, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WithdrawalDeadline = new(2027, 2, 26, 17, 0, 0, TimeSpan.Zero);

    private static EnrolmentWindow Spring() => new()
    {
        Id = Guid.NewGuid(),
        AcademicYear = "2026/27",
        Semester = Semester.Spring,
        OpensAt = Opens,
        ClosesAt = Closes,
        WithdrawalDeadlineAt = WithdrawalDeadline,
        UpdatedAt = Opens,
    };

    [Fact]
    public void Is_closed_before_it_opens()
    {
        Assert.False(Spring().IsOpenAt(Opens.AddSeconds(-1)));
    }

    [Fact]
    public void Is_open_at_the_opening_instant()
    {
        Assert.True(Spring().IsOpenAt(Opens));
    }

    [Fact]
    public void Is_open_in_the_middle()
    {
        Assert.True(Spring().IsOpenAt(new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Is_open_just_before_it_closes()
    {
        Assert.True(Spring().IsOpenAt(Closes.AddSeconds(-1)));
    }

    [Fact]
    public void Is_closed_at_the_closing_instant()
    {
        Assert.False(Spring().IsOpenAt(Closes));
    }

    [Fact]
    public void Compares_instants_not_offsets()
    {
        // 10:00 at +01:00 is 09:00 UTC, exactly the opening instant.
        Assert.True(Spring().IsOpenAt(new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.FromHours(1))));
        Assert.False(Spring().IsOpenAt(new DateTimeOffset(2026, 9, 14, 9, 59, 59, TimeSpan.FromHours(1))));
    }

    [Fact]
    public void Allows_withdrawal_after_the_window_closes_but_before_the_deadline()
    {
        Assert.True(Spring().AllowsWithdrawalAt(Closes.AddDays(1)));
    }

    [Fact]
    public void Allows_withdrawal_just_before_the_deadline()
    {
        Assert.True(Spring().AllowsWithdrawalAt(WithdrawalDeadline.AddSeconds(-1)));
    }

    [Fact]
    public void Blocks_withdrawal_at_and_after_the_deadline()
    {
        Assert.False(Spring().AllowsWithdrawalAt(WithdrawalDeadline));
        Assert.False(Spring().AllowsWithdrawalAt(WithdrawalDeadline.AddDays(30)));
    }
}
