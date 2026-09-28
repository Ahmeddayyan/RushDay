using RushDay.Domain.Users;

namespace RushDay.Api.Options;

/// <summary>Session lifetimes and the second-factor rule (02-api.md sections 2.1 and 2.4), bound from "Auth".</summary>
public sealed record AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Students and lecturers: inactivity limit.</summary>
    public int SessionSlidingHours { get; init; } = 8;

    /// <summary>Students and lecturers: limit since sign-in; also the cookie ticket's own ceiling.</summary>
    public int SessionAbsoluteHours { get; init; } = 12;

    public int AdminSessionSlidingMinutes { get; init; } = 60;

    public int AdminSessionAbsoluteHours { get; init; } = 8;

    /// <summary>How often a session re-reads its user (0 in tests and Playwright).</summary>
    public int SecurityStampIntervalMinutes { get; init; } = 5;

    /// <summary>
    /// Roles that must enrol a TOTP factor. Left null by default so a configured list replaces the default instead of
    /// being appended to it by the configuration binder; <see cref="MfaRoles"/> applies the default.
    /// </summary>
    public string[]? RequireMfaForRoles { get; init; }

    /// <summary>Lifetime of the cookie that carries the user between the password and the code.</summary>
    public int MfaCookieMinutes { get; init; } = 5;

    public IReadOnlyList<string> MfaRoles => RequireMfaForRoles is { Length: > 0 } roles ? roles : [RushDayRoles.Admin];

    public TimeSpan SlidingLifetimeFor(string? role) =>
        role == RushDayRoles.Admin ? TimeSpan.FromMinutes(AdminSessionSlidingMinutes) : TimeSpan.FromHours(SessionSlidingHours);

    public TimeSpan AbsoluteLifetimeFor(string? role) =>
        role == RushDayRoles.Admin ? TimeSpan.FromHours(AdminSessionAbsoluteHours) : TimeSpan.FromHours(SessionAbsoluteHours);
}
