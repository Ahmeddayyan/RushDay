namespace RushDay.Infrastructure.Seeding;

/// <summary>Inputs to <see cref="StartupBackfills"/>, bound in Program.cs from Demo, Bootstrap, Branding and Database settings.</summary>
public sealed record StartupBackfillOptions
{
    public const string DefaultInstitutionName = "RushDay Demo University";
    public const string DefaultInstitutionShortName = "RushDay";
    public const string DefaultTimeZone = "Europe/London";

    /// <summary><c>Demo:Enabled</c>. Off by default; a customer deployment never turns it on.</summary>
    public bool DemoEnabled { get; init; }

    /// <summary><c>Bootstrap:AdminPassword</c>, consumed only when no Admin exists.</summary>
    public string? BootstrapAdminPassword { get; init; }

    public string InstitutionName { get; init; } = DefaultInstitutionName;

    public string InstitutionShortName { get; init; } = DefaultInstitutionShortName;

    public string TimeZone { get; init; } = DefaultTimeZone;

    /// <summary><c>Database:SeedResultsDay</c>: the instant the seeded autumn results publish.</summary>
    public DateTimeOffset SeedResultsDay { get; init; } = new(2026, 9, 28, 9, 0, 0, TimeSpan.Zero);
}
