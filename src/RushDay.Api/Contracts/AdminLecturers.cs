using System.ComponentModel.DataAnnotations;
using RushDay.Infrastructure.Lecturers;

namespace RushDay.Api.Contracts;

/// <summary>One line of <c>GET /api/admin/lecturers</c> (also the answer of create, update and leave).</summary>
public sealed record AdminLecturerView(
    string StaffNumber,
    string FullName,
    string Title,
    string Department,
    string? Email,
    DateTimeOffset? LeftAt,
    bool HasAccount,
    IReadOnlyList<string> ModuleCodes)
{
    public static AdminLecturerView From(LecturerRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new AdminLecturerView(row.StaffNumber, row.FullName, row.Title, row.Department, row.Email, row.LeftAt, row.HasAccount, row.ModuleCodes);
    }
}

/// <summary>The query of <c>GET /api/admin/lecturers</c>: a staff-number prefix or a name fragment.</summary>
public sealed record LecturerListParameters
{
    [StringLength(StaffPatterns.SearchMaxLength)]
    [NoNul]
    public string? Q { get; init; }
}

/// <summary>The fields a create and an update share.</summary>
public record LecturerFields
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    [NoNul]
    public string? FullName { get; init; }

    [Required]
    [RegularExpression("^(Dr|Prof|Mr|Ms|Mx)$")]
    public string? Title { get; init; }

    [Required]
    [RegularExpression("^[A-Za-z]{1,8}$")]
    public string? Department { get; init; }

    [EmailAddress]
    [StringLength(256)]
    [NoNul]
    public string? Email { get; init; }

    public LecturerChange ToChange() => new(FullName!.Trim(), Title!, Department!.ToUpperInvariant(), string.IsNullOrWhiteSpace(Email) ? null : Email.Trim());
}

/// <summary><c>POST /api/admin/lecturers</c>.</summary>
public sealed record CreateLecturerRequest : LecturerFields
{
    [Required]
    [RegularExpression(StaffPatterns.StaffNumber)]
    public string? StaffNumber { get; init; }
}

/// <summary><c>PUT /api/admin/lecturers/{staffNumber}</c>.</summary>
public sealed record UpdateLecturerRequest : LecturerFields;
