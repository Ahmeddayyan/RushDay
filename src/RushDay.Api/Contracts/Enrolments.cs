using System.ComponentModel.DataAnnotations;
using RushDay.Api.Security;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Enrolments;
using RushDay.Infrastructure.Queries;

namespace RushDay.Api.Contracts;

/// <summary>
/// <c>POST /api/me/enrolments</c>. The code must be two ASCII letters and four ASCII digits, surrounding white space
/// allowed; it is trimmed and upper-cased server-side before lookup. Anything else (a NUL character, a non-ASCII digit)
/// is 400 <c>validation</c> before any query runs.
/// </summary>
public sealed record EnrolRequest
{
    /// <summary>The body pattern: case-insensitive letters by listing both cases, digits as <c>[0-9]</c> (never <c>\d</c>, which is Unicode).</summary>
    public const string ModuleCodePattern = @"^\s*[A-Za-z]{2}[0-9]{4}\s*$";

    [Required]
    [StringLength(16, MinimumLength = 1)]
    [RegularExpression(ModuleCodePattern)]
    public string ModuleCode { get; init; } = string.Empty;
}

/// <summary>
/// <c>201</c> of <c>POST /api/me/enrolments</c>: <c>placesRemaining</c> comes from the claiming statement's
/// <c>RETURNING</c>, so it is exact at the instant of the enrolment (no <c>Location</c> header: there is no
/// single-enrolment GET).
/// </summary>
public sealed record EnrolResponse(string ModuleCode, DateTimeOffset EnrolledAt, int PlacesRemaining)
{
    public static EnrolResponse From(EnrolmentReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new EnrolResponse(receipt.ModuleCode, receipt.EnrolledAt, receipt.PlacesRemaining);
    }
}

/// <summary><c>MyEnrolment</c> of 02-api.md section 7.</summary>
public sealed record MyEnrolment(
    string ModuleCode,
    string Title,
    int Credits,
    Semester Semester,
    string AcademicYear,
    EnrolmentStatus Status,
    DateTimeOffset EnrolledAt,
    DateTimeOffset? WithdrawnAt,
    bool CanWithdraw,
    WithdrawBlock? WithdrawBlockedReason,
    DateTimeOffset? WithdrawalDeadlineAt)
{
    public static MyEnrolment From(MyEnrolmentItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var row = item.Row;
        return new MyEnrolment(
            row.ModuleCode,
            row.Title,
            row.Credits,
            row.Semester,
            row.AcademicYear,
            row.Status,
            row.EnrolledAt,
            row.WithdrawnAt,
            item.Withdrawal.CanWithdraw,
            item.Withdrawal.BlockedReason,
            item.Withdrawal.WithdrawalDeadlineAt);
    }
}

/// <summary>Maps an enrolment or withdrawal refusal to its ProblemDetails (02-api.md sections 6 and 8.3), extensions included.</summary>
public static class EnrolmentProblems
{
    public static IResult For(EnrolmentFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return failure.Error switch
        {
            EnrolmentError.ModuleNotFound => ProblemResults.Problem(ProblemTypes.ModuleNotFound, "No module has that code."),
            EnrolmentError.StudentNotFound => ProblemResults.Problem(ProblemTypes.StudentNotFound, "No student has that number."),
            EnrolmentError.ModuleInactive => ProblemResults.Problem(ProblemTypes.ModuleInactive, "This module is no longer running."),
            EnrolmentError.ModuleFull => ProblemResults.Problem(ProblemTypes.ModuleFull, "The module has no places left."),
            EnrolmentError.AlreadyEnrolled => ProblemResults.Problem(ProblemTypes.AlreadyEnrolled, "You are already enrolled on this module."),
            EnrolmentError.WindowClosed => ProblemResults.Problem(
                ProblemTypes.EnrolmentWindowClosed,
                "Enrolment for this semester is not open.",
                new Dictionary<string, object?>
                {
                    ["semester"] = failure.Semester,
                    ["opensAt"] = failure.OpensAt,
                    ["closesAt"] = failure.ClosesAt,
                }),
            EnrolmentError.CreditLimitExceeded => ProblemResults.Problem(
                ProblemTypes.CreditLimitExceeded,
                $"Enrolling would take you over {failure.Limit} credits for the semester.",
                new Dictionary<string, object?>
                {
                    ["currentCredits"] = failure.CurrentCredits,
                    ["moduleCredits"] = failure.ModuleCredits,
                    ["limit"] = failure.Limit,
                    ["semester"] = failure.Semester,
                }),
            EnrolmentError.ResultsExist => ProblemResults.Problem(ProblemTypes.ResultsExist, "A mark is already recorded for this module."),
            EnrolmentError.StudentLeft => ProblemResults.Problem(ProblemTypes.StudentLeft, "The student has left the university."),
            EnrolmentError.NotEnrolled => ProblemResults.Problem(ProblemTypes.NotEnrolled, "There is no active enrolment on this module."),
            EnrolmentError.WithdrawalDeadlinePassed => ProblemResults.Problem(
                ProblemTypes.WithdrawalDeadlinePassed,
                "The withdrawal deadline for this enrolment has passed.",
                new Dictionary<string, object?> { ["withdrawalDeadlineAt"] = failure.WithdrawalDeadlineAt }),
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure.Error, "Not a refusal."),
        };
    }
}
