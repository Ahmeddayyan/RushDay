namespace RushDay.Domain.Modules;

/// <summary>Assignment of a lecturer to a module. Exactly one Leader per module is a service rule.</summary>
public sealed class ModuleLecturer
{
    public Guid ModuleId { get; init; }
    public Guid LecturerId { get; init; }
    public ModuleLecturerRole Role { get; set; }
    public DateTimeOffset AssignedAt { get; set; }

    /// <summary>Null when the assignment came from the seed.</summary>
    public Guid? AssignedByUserId { get; set; }
}
