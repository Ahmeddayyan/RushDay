using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Enrolments;
using RushDay.Infrastructure.Settings;

namespace RushDay.Api.Contracts;

/// <summary><c>GET</c> and <c>PUT /api/admin/settings</c>.</summary>
public sealed record SettingsResponse(
    string AcademicYear,
    Semester CurrentSemester,
    string InstitutionName,
    string InstitutionShortName,
    string TimeZone,
    string? SupportEmail,
    string? SupportUrl,
    DateTimeOffset UpdatedAt)
{
    public static SettingsResponse From(SettingsRecord settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new SettingsResponse(settings.AcademicYear, settings.CurrentSemester, settings.InstitutionName, settings.InstitutionShortName, settings.TimeZone, settings.SupportEmail, settings.SupportUrl, settings.UpdatedAt);
    }
}

/// <summary>
/// <c>PUT /api/admin/settings</c>. <c>timeZone</c> is checked against the runtime's zone database when it has one,
/// otherwise (invariant globalization, this project's builds) by the shape of an IANA id; only the browser formats
/// with it. <c>supportUrl</c> must be an absolute https URL.
/// </summary>
public sealed partial record UpdateSettingsRequest : IValidatableObject
{
    [Required]
    [RegularExpression(StaffPatterns.AcademicYear)]
    public string? AcademicYear { get; init; }

    [Required]
    [EnumDataType(typeof(Semester))]
    public Semester? CurrentSemester { get; init; }

    [Required]
    [StringLength(200, MinimumLength = 1)]
    [NoNul]
    public string? InstitutionName { get; init; }

    [Required]
    [StringLength(32, MinimumLength = 1)]
    [NoNul]
    public string? InstitutionShortName { get; init; }

    [Required]
    [StringLength(64, MinimumLength = 1)]
    [NoNul]
    public string? TimeZone { get; init; }

    [EmailAddress]
    [StringLength(256)]
    [NoNul]
    public string? SupportEmail { get; init; }

    [StringLength(400)]
    [NoNul]
    public string? SupportUrl { get; init; }

    public static bool IsValidTimeZone(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var invariant = AppContext.TryGetSwitch("System.Globalization.Invariant", out var on) && on;
        return invariant ? TimeZoneShape().IsMatch(id) : TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TimeZone is { Length: > 0 } zone && !IsValidTimeZone(zone))
        {
            yield return new ValidationResult("Use an IANA time zone id such as Europe/London.", [nameof(TimeZone)]);
        }

        if (!string.IsNullOrWhiteSpace(SupportUrl)
            && !(Uri.TryCreate(SupportUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps))
        {
            yield return new ValidationResult("The support URL must be an absolute https URL.", [nameof(SupportUrl)]);
        }
    }

    public SettingsChange ToChange() => new(
        AcademicYear!,
        CurrentSemester!.Value,
        InstitutionName!.Trim(),
        InstitutionShortName!.Trim(),
        TimeZone!.Trim(),
        string.IsNullOrWhiteSpace(SupportEmail) ? null : SupportEmail.Trim(),
        string.IsNullOrWhiteSpace(SupportUrl) ? null : SupportUrl.Trim());

    [GeneratedRegex("^[A-Za-z]+(/[A-Za-z_+-]+){1,2}$", RegexOptions.CultureInvariant)]
    private static partial Regex TimeZoneShape();
}

/// <summary><c>POST /api/admin/enrolment-windows</c>.</summary>
public sealed record CreateWindowRequest
{
    [Required]
    [RegularExpression(StaffPatterns.AcademicYear)]
    public string? AcademicYear { get; init; }

    [Required]
    [EnumDataType(typeof(Semester))]
    public Semester? Semester { get; init; }

    [Required]
    public DateTimeOffset? OpensAt { get; init; }

    [Required]
    public DateTimeOffset? ClosesAt { get; init; }

    [Required]
    public DateTimeOffset? WithdrawalDeadlineAt { get; init; }

    public WindowDates ToDates() => new(OpensAt!.Value, ClosesAt!.Value, WithdrawalDeadlineAt!.Value);
}

/// <summary><c>PUT /api/admin/enrolment-windows/{id}</c>.</summary>
public sealed record UpdateWindowRequest
{
    [Required]
    public DateTimeOffset? OpensAt { get; init; }

    [Required]
    public DateTimeOffset? ClosesAt { get; init; }

    [Required]
    public DateTimeOffset? WithdrawalDeadlineAt { get; init; }

    public WindowDates ToDates() => new(OpensAt!.Value, ClosesAt!.Value, WithdrawalDeadlineAt!.Value);
}
