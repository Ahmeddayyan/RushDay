using RushDay.Domain.Modules;

namespace RushDay.Domain.Enrolments;

/// <summary>
/// Pure enrolment policy. Callers supply the current counts and the window state; this class decides.
/// Keeping it free of I/O makes it trivially unit-testable.
/// </summary>
public static class EnrolmentRules
{
    public const int MaxCreditsPerSemester = 60;

    /// <summary>
    /// Decides in a fixed order: already enrolled, window closed, credit limit, module full, accepted.
    /// The cheap and student-specific reasons come first so a rejected student hears the reason that
    /// would still apply after a place frees up. The admin override passes <c>windowOpen: true</c> and
    /// <c>ignoreCreditLimit: true</c>; capacity is still respected unless the service forces it.
    /// </summary>
    public static EnrolmentDecision Evaluate(
        Module module,
        int currentEnrolledCount,
        int studentCreditsInSemester,
        bool alreadyEnrolled,
        bool windowOpen,
        bool ignoreCreditLimit = false)
    {
        ArgumentNullException.ThrowIfNull(module);

        if (alreadyEnrolled)
        {
            return EnrolmentDecision.AlreadyEnrolled;
        }

        if (!windowOpen)
        {
            return EnrolmentDecision.WindowClosed;
        }

        if (!ignoreCreditLimit && studentCreditsInSemester + module.Credits > MaxCreditsPerSemester)
        {
            return EnrolmentDecision.CreditLimitExceeded;
        }

        if (currentEnrolledCount >= module.Capacity)
        {
            return EnrolmentDecision.ModuleFull;
        }

        return EnrolmentDecision.Accepted;
    }
}
