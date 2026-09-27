namespace RushDay.Api;

/// <summary>
/// Startup database behaviour, bound from the "Database" configuration section.
/// Locally we run migrations explicitly; on the free cloud tier there is no shell, so the app does it on boot.
/// </summary>
public sealed record DatabaseOptions
{
    public const string SectionName = "Database";

    public bool MigrateOnStartup { get; init; }

    public bool SeedOnStartup { get; init; }

    public int SeedStudentCount { get; init; } = 20_000;
}
