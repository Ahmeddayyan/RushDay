namespace RushDay.Domain.Settings;

/// <summary>Singleton row (id = 1): the current academic year, the institution's name and its display time zone.</summary>
public sealed class AcademicSettings
{
    public const int SingletonId = 1;

    public int Id { get; init; } = SingletonId;
    public required string AcademicYear { get; set; }
    public required string InstitutionName { get; set; }
    public required string InstitutionShortName { get; set; }

    /// <summary>IANA time zone id, e.g. Europe/London.</summary>
    public required string TimeZone { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
