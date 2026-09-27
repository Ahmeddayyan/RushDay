using RushDay.Domain.Modules;

namespace RushDay.Domain.Enrolments;

/// <summary>
/// Pure enrolment policy. Callers supply the current counts; this class decides.
/// Keeping it free of I/O makes it trivially unit-testable.
/// </summary>
public static class EnrolmentRules
{
    public const int MaxCreditsPerSemester = 60;

    public static EnrolmentDecision Evaluate(
        Module module,
        int currentEnrolledCount,
        int studentCreditsInSemester,
        bool alreadyEnrolled)
    {
        ArgumentNullException.ThrowIfNull(module);

        if (alreadyEnrolled)
        {
            return EnrolmentDecision.AlreadyEnrolled;
        }

        if (currentEnrolledCount >= module.Capacity)
        {
            return EnrolmentDecision.ModuleFull;
        }

        if (studentCreditsInSemester + module.Credits > MaxCreditsPerSemester)
        {
            return EnrolmentDecision.CreditLimitExceeded;
        }

        return EnrolmentDecision.Accepted;
    }
}
