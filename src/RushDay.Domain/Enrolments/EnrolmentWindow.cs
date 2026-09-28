using RushDay.Domain.Modules;

namespace RushDay.Domain.Enrolments;

/// <summary>
/// The period in which students may self-enrol on a semester's modules, and the later deadline for
/// self-withdrawal. Stored instants evaluated at request time; there is no scheduler.
/// </summary>
public sealed class EnrolmentWindow
{
    public Guid Id { get; init; }
    public required string AcademicYear { get; init; }
    public Semester Semester { get; init; }
    public DateTimeOffset OpensAt { get; set; }
    public DateTimeOffset ClosesAt { get; set; }
    public DateTimeOffset WithdrawalDeadlineAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Self-enrolment is allowed iff <c>opens_at &lt;= now &lt; closes_at</c>.</summary>
    public bool IsOpenAt(DateTimeOffset now) => OpensAt <= now && now < ClosesAt;

    /// <summary>Self-withdrawal is allowed iff <c>now &lt; withdrawal_deadline_at</c>.</summary>
    public bool AllowsWithdrawalAt(DateTimeOffset now) => now < WithdrawalDeadlineAt;
}
