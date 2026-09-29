using RushDay.Api.Auth;
using RushDay.Api.Contracts;
using RushDay.Api.Security;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Settings;

namespace RushDay.Api.Endpoints;

/// <summary>
/// <c>GET</c> and <c>PUT /api/admin/settings</c> (02-api.md section 8.5). An update invalidates <c>settings</c>; a
/// change of academic year also recomputed <c>enrolled_count</c> in its transaction, so it invalidates
/// <c>catalogue:all</c> and <c>windows:all</c> too (04-performance-and-ops.md section 4).
/// </summary>
public static class AdminSettingsEndpoints
{
    public static RouteGroupBuilder MapAdminSettingsEndpoints(this RouteGroupBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        admin.MapGet("/settings", GetAsync).WithName("AdminSettings");
        admin.MapPut("/settings", UpdateAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("UpdateSettings");
        return admin;
    }

    private static async Task<IResult> GetAsync(SettingsService settings, CancellationToken cancellationToken)
    {
        var row = await settings.GetAsync(cancellationToken);
        return row is null ? ProblemResults.Problem(ProblemTypes.NotFound, "The academic settings have not been created yet.") : TypedResults.Ok(SettingsResponse.From(row));
    }

    private static async Task<IResult> UpdateAsync(
        UpdateSettingsRequest request,
        CurrentUser user,
        SettingsService settings,
        SettingsCache settingsCache,
        CatalogueCache catalogue,
        EnrolmentWindowCache windows,
        CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
        {
            return ProblemResults.Problem(ProblemTypes.Forbidden);
        }

        var updated = await settings.UpdateAsync(request.ToChange(), userId, cancellationToken);
        if (updated is null)
        {
            return ProblemResults.Problem(ProblemTypes.NotFound, "The academic settings have not been created yet.");
        }

        await settingsCache.InvalidateAsync(cancellationToken);
        if (updated.YearChanged)
        {
            await catalogue.InvalidateAsync(cancellationToken);
            await windows.InvalidateAsync(cancellationToken);
        }

        return TypedResults.Ok(SettingsResponse.From(updated));
    }
}
