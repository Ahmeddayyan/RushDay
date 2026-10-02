using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Options;
using RushDay.Api.Contracts;
using RushDay.Api.Options;
using RushDay.Domain.Modules;
using RushDay.Domain.Users;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Seeding;

namespace RushDay.Api.Endpoints;

/// <summary>
/// <c>GET /api/public/status</c> (02-api.md section 8.1): what the login page and the SPA shell need before anyone
/// signs in. The response never varies by user and sets no cookie, so it is output-cached for 10 s for everyone.
/// </summary>
public static class PublicEndpoints
{
    public const string StatusCachePolicy = "public-status";

    public static RouteGroupBuilder MapPublicEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet("/public/status", GetStatusAsync)
            .AllowAnonymous()
            .CacheOutput(StatusCachePolicy)
            .WithName("PublicStatus")
            .WithTags("Public");

        return api;
    }

    private static async Task<IResult> GetStatusAsync(
        TimeProvider clock,
        SettingsCache settingsCache,
        EnrolmentWindowCache windowCache,
        PublicationCache publicationCache,
        IOptions<BrandingOptions> brandingOptions,
        IOptions<DemoOptions> demoOptions,
        IOptions<BootstrapOptions> bootstrapOptions,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var branding = brandingOptions.Value;
        var settings = await settingsCache.GetAsync(cancellationToken);

        // Before the backfills have created the settings row, the configured branding stands in for it.
        var academicYear = settings?.AcademicYear ?? StartupBackfills.CurrentAcademicYear;
        var support = settings is { SupportEmail: null, SupportUrl: null } or null ? null : new SupportInfo(settings.SupportEmail, settings.SupportUrl);
        var institution = new InstitutionInfo(
            settings?.InstitutionName ?? branding.InstitutionName,
            settings?.InstitutionShortName ?? branding.InstitutionShortName,
            settings?.TimeZone ?? branding.TimeZone,
            string.IsNullOrWhiteSpace(branding.PrivacyNoticeUrl) ? null : branding.PrivacyNoticeUrl,
            branding.ResultsFootnote,
            support);

        var briefs = await publicationCache.GetBriefAsync(now, cancellationToken);
        var windows = await windowCache.ForYearAsync(academicYear, cancellationToken);

        var body = new PublicStatus(
            ServerTime: now,
            Institution: institution,
            AcademicYear: academicYear,
            CurrentSemester: settings?.CurrentSemester ?? Semester.Autumn,
            NextPublication: PublicationBrief.From(briefs.Next, now),
            LatestPublication: PublicationBrief.From(briefs.Latest, now),
            EnrolmentWindows: [.. windows.Select(w => WindowInfo.From(w, now))],
            Demo: demoOptions.Value.Enabled ? DemoAccountList(bootstrapOptions.Value.AdminUsername) : null);

        return TypedResults.Ok(body);
    }

    private static DemoInfo DemoAccountList(string adminUsername) => new(
    [
        new DemoAccountInfo(RushDayRoles.Student, DemoAccounts.StudentUsername, DemoAccounts.StudentPassword, DemoAccounts.StudentHint),
        new DemoAccountInfo(RushDayRoles.Lecturer, DemoAccounts.LecturerUsername, DemoAccounts.LecturerPassword, DemoAccounts.LecturerHint),
        new DemoAccountInfo(RushDayRoles.Admin, adminUsername, DemoAccounts.AdminPassword, DemoAccounts.AdminHint),
    ]);
}

/// <summary>
/// The <c>public-status</c> output-cache policy: 10 s for every caller, signed in or not (the named policy excludes the
/// default policy, which would refuse authenticated requests). A response that is not a 200 or that sets a cookie is
/// never stored.
/// </summary>
public sealed class PublicStatusCachePolicy : IOutputCachePolicy
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(10);

    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(context);
        var cacheable = HttpMethods.IsGet(context.HttpContext.Request.Method) || HttpMethods.IsHead(context.HttpContext.Request.Method);
        context.EnableOutputCaching = true;
        context.AllowCacheLookup = cacheable;
        context.AllowCacheStorage = cacheable;
        context.AllowLocking = true;
        context.ResponseExpirationTimeSpan = Lifetime;
        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellation) => ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(context);
        var response = context.HttpContext.Response;
        if (response.StatusCode != StatusCodes.Status200OK || response.Headers.SetCookie.Count > 0)
        {
            context.AllowCacheStorage = false;
        }

        return ValueTask.CompletedTask;
    }
}
