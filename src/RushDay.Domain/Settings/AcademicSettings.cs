using RushDay.Domain.Modules;

namespace RushDay.Domain.Settings;

/// <summary>Singleton row (id = 1): the current academic year and semester, the institution's name, its display time zone and support contact.</summary>
public sealed class AcademicSettings
{
    public const int SingletonId = 1;

    public int Id { get; init; } = SingletonId;
    public required string AcademicYear { get; set; }
    public required string InstitutionName { get; set; }
    public required string InstitutionShortName { get; set; }

    /// <summary>IANA time zone id, e.g. Europe/London.</summary>
    public required string TimeZone { get; set; }

    /// <summary>Stored as an integer like modules.semester; decides which modules' slots the student timetable shows.</summary>
    public Semester CurrentSemester { get; set; }

    /// <summary>The academic office's address, rendered wherever the UI says "contact the academic office".</summary>
    public string? SupportEmail { get; set; }

    /// <summary>The academic office's help page (either or both may be set).</summary>
    public string? SupportUrl { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
