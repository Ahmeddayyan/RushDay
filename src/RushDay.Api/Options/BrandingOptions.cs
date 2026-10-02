using RushDay.Infrastructure.Seeding;

namespace RushDay.Api.Options;

/// <summary>
/// Institution branding, bound from "Branding". Name, short name and time zone only create the settings row; the
/// privacy notice and the results footnote are read at request time.
/// </summary>
public sealed record BrandingOptions
{
    public const string SectionName = "Branding";

    public const string DefaultResultsFootnote = "Below 40? Your personal tutor or the academic office can explain resit options.";

    public string InstitutionName { get; init; } = StartupBackfillOptions.DefaultInstitutionName;

    /// <summary>Also the TOTP issuer shown in authenticator apps.</summary>
    public string InstitutionShortName { get; init; } = StartupBackfillOptions.DefaultInstitutionShortName;

    public string TimeZone { get; init; } = StartupBackfillOptions.DefaultTimeZone;

    /// <summary>A customer's privacy notice, rendered in the login footer and the user menu when set.</summary>
    public string? PrivacyNoticeUrl { get; init; }

    public string ResultsFootnote { get; init; } = DefaultResultsFootnote;
}
