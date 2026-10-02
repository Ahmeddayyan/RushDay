using System.ComponentModel.DataAnnotations;
using RushDay.Infrastructure.Enrolments;
using RushDay.Infrastructure.Queries;
using RushDay.Infrastructure.Students;

namespace RushDay.Api.Contracts;

/// <summary>A body carrying only a reason (10..400 characters): leave, override withdrawal, trim, unpublish.</summary>
public sealed record ReasonRequest
{
    [Required]
    [StringLength(StaffPatterns.ReasonMaxLength, MinimumLength = StaffPatterns.ReasonMinLength)]
    [NoNul]
    public string? Reason { get; init; }
}

/// <summary>One line of <c>GET /api/admin/students</c> (also the answer of create and update).</summary>
public sealed record AdminStudentListItem(
    string StudentNumber,
    string FullName,
    string Programme,
    int YearOfStudy,
    string? Email,
    DateTimeOffset? LeftAt,
    AccountState AccountState)
{
    public static AdminStudentListItem From(AdminStudentRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new AdminStudentListItem(row.StudentNumber, row.FullName, row.Programme, row.YearOfStudy, row.Email, row.LeftAt, row.AccountState);
    }
}

/// <summary>The query of <c>GET /api/admin/students</c>.</summary>
public sealed record StudentListParameters
{
    [StringLength(StaffPatterns.SearchMaxLength)]
    [NoNul]
    public string? Q { get; init; }

    [RegularExpression("^(none|active|locked|disabled)$")]
    public string? AccountState { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public RushDay.Infrastructure.Queries.AccountState? State() => AccountState switch
    {
        "none" => RushDay.Infrastructure.Queries.AccountState.None,
        "active" => RushDay.Infrastructure.Queries.AccountState.Active,
        "locked" => RushDay.Infrastructure.Queries.AccountState.Locked,
        "disabled" => RushDay.Infrastructure.Queries.AccountState.Disabled,
        _ => null,
    };
}

/// <summary>The editable fields of a student record.</summary>
public record StudentFields
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    [NoNul]
    public string? FullName { get; init; }

    [Required]
    [StringLength(200, MinimumLength = 1)]
    [NoNul]
    public string? Programme { get; init; }

    [Required]
    [Range(1, 6)]
    public int? YearOfStudy { get; init; }

    [EmailAddress]
    [StringLength(256)]
    [NoNul]
    public string? Email { get; init; }

    public StudentChange ToChange() => new(FullName!.Trim(), Programme!.Trim(), YearOfStudy!.Value, string.IsNullOrWhiteSpace(Email) ? null : Email.Trim());
}

/// <summary><c>POST /api/admin/students</c>.</summary>
public sealed record CreateStudentRequest : StudentFields
{
    [Required]
    [RegularExpression(StaffPatterns.StudentNumber)]
    public string? StudentNumber { get; init; }
}

/// <summary><c>PUT /api/admin/students/{studentNumber}</c>.</summary>
public sealed record UpdateStudentRequest : StudentFields;

/// <summary><c>POST /api/admin/students/{studentNumber}/leave</c>.</summary>
public sealed record StudentLeftResponse(string StudentNumber, DateTimeOffset LeftAt, int Withdrawn);

/// <summary><c>AdminStudentView</c> of 02-api.md section 8.5.</summary>
public sealed record AdminStudentView(
    StudentProfile Student,
    AccountView? Account,
    IReadOnlyList<StudentEnrolmentRecord> Enrolments,
    IReadOnlyList<StudentGradeRecord> Grades,
    double? WeightedAverage,
    string? Classification,
    IReadOnlyList<AuditEventView> RecentAudit)
{
    public static AdminStudentView From(AdminStudentViewData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new AdminStudentView(
            StudentProfile.From(data.Student),
            data.Account is null ? null : AccountView.From(data.Account),
            [.. data.Enrolments.Select(StudentEnrolmentRecord.From)],
            [
                .. data.Grades.Select(g => new StudentGradeRecord(
                    g.ModuleCode,
                    g.ModuleTitle,
                    g.Credits,
                    g.Semester,
                    g.AcademicYear,
                    g.Outcome,
                    g.Mark,
                    g.Status,
                    g.PublishedAt,
                    g.VisibleToStudent,
                    g.Version,
                    g.CorrectedAt)),
            ],
            data.WeightedAverage,
            data.Classification,
            [.. data.RecentAudit.Select(AuditEventView.From)]);
    }
}

/// <summary><c>POST /api/admin/students/{studentNumber}/enrolments</c>: the override enrolment.</summary>
public sealed record OverrideEnrolRequest
{
    [Required]
    [RegularExpression(StaffPatterns.ModuleCode)]
    public string? ModuleCode { get; init; }

    [Required]
    [StringLength(StaffPatterns.ReasonMaxLength, MinimumLength = StaffPatterns.ReasonMinLength)]
    [NoNul]
    public string? Reason { get; init; }

    /// <summary>Raises capacity by one, and only when the module is full.</summary>
    public bool? ForceCapacity { get; init; }
}

public sealed record OverrideEnrolResponse(string ModuleCode, DateTimeOffset EnrolledAt, int PlacesRemaining, bool CapacityRaised)
{
    public static OverrideEnrolResponse From(EnrolmentReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new OverrideEnrolResponse(receipt.ModuleCode, receipt.EnrolledAt, receipt.PlacesRemaining, receipt.CapacityRaised);
    }
}
