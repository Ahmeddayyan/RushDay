using System.ComponentModel.DataAnnotations;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Modules;

namespace RushDay.Api.Contracts;

/// <summary><c>GET /api/admin/modules</c>: <c>ModuleSummary &amp; { description, marks }</c>.</summary>
public sealed record AdminModuleView : ModuleSummary
{
    public AdminModuleView(ModuleSummary summary, string? description, MarksStatus marks)
        : base(summary)
    {
        Description = description;
        Marks = marks;
    }

    public string? Description { get; init; }

    public MarksStatus Marks { get; init; }
}

/// <summary>The query of <c>GET /api/admin/modules</c>.</summary>
public sealed record ModuleListParameters
{
    public bool? IncludeInactive { get; init; }
}

/// <summary>The query of the administrator's roster and marks routes: the lecturer shape plus <c>academicYear</c>.</summary>
public sealed record AdminModuleReadParameters
{
    [StringLength(StaffPatterns.SearchMaxLength)]
    [NoNul]
    public string? Q { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    [RegularExpression(StaffPatterns.AcademicYear)]
    public string? AcademicYear { get; init; }
}

/// <summary>The fields a create and an update share.</summary>
public abstract record ModuleFields
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    [NoNul]
    public string? Title { get; init; }

    [StringLength(2000)]
    [NoNul]
    public string? Description { get; init; }

    [Required]
    [Range(5, 60)]
    public int? Credits { get; init; }

    [Required]
    [Range(0, 10_000)]
    public int? Capacity { get; init; }

    [Required]
    [EnumDataType(typeof(Semester))]
    public Semester? Semester { get; init; }
}

/// <summary><c>POST /api/admin/modules</c>.</summary>
public sealed record CreateModuleRequest : ModuleFields
{
    [Required]
    [RegularExpression(StaffPatterns.ModuleCode)]
    public string? Code { get; init; }

    public ModuleChange ToChange() => new(Title!.Trim(), string.IsNullOrWhiteSpace(Description) ? null : Description, Credits!.Value, Capacity!.Value, Semester!.Value, IsActive: true);
}

/// <summary><c>PUT /api/admin/modules/{code}</c>.</summary>
public sealed record UpdateModuleRequest : ModuleFields
{
    [Required]
    public bool? IsActive { get; init; }

    public ModuleChange ToChange() => new(Title!.Trim(), string.IsNullOrWhiteSpace(Description) ? null : Description, Credits!.Value, Capacity!.Value, Semester!.Value, IsActive!.Value);
}

/// <summary>One assignment of <c>PUT /api/admin/modules/{code}/lecturers</c>.</summary>
public sealed record LecturerAssignmentRequest
{
    [Required]
    [RegularExpression(StaffPatterns.StaffNumber)]
    public string? StaffNumber { get; init; }

    [Required]
    [EnumDataType(typeof(ModuleLecturerRole))]
    public ModuleLecturerRole? Role { get; init; }
}

/// <summary><c>PUT /api/admin/modules/{code}/lecturers</c>: exactly one leader, no duplicates, nobody who has left (422 otherwise).</summary>
public sealed record SetLecturersRequest
{
    [Required]
    [MaxLength(50)]
    [NoNullElements]
    public List<LecturerAssignmentRequest>? Assignments { get; init; }

    public IReadOnlyList<LecturerAssignment> ToAssignments() =>
        [.. (Assignments ?? []).Select(a => new LecturerAssignment(a.StaffNumber!, a.Role!.Value))];
}

/// <summary><c>POST /api/admin/modules/{code}/trim-to-capacity</c>.</summary>
public sealed record TrimResponse(string Code, int Capacity, int Before, int After, IReadOnlyList<string> Withdrawn)
{
    public static TrimResponse From(TrimOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return new TrimResponse(outcome.Code, outcome.Capacity, outcome.Before, outcome.After, outcome.Withdrawn);
    }
}
