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
    /// would still apply after a place frees up.
    /// </summary>
    public static EnrolmentDecision Evaluate(
        Module module,
        int currentEnrolledCount,
        int studentCreditsInSemester,
        bool alreadyEnrolled,
        bool windowOpen)
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

        if (studentCreditsInSemester + module.Credits > MaxCreditsPerSemester)
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
