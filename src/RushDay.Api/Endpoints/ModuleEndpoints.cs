using RushDay.Api.Contracts;
using RushDay.Api.Security;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Enrolments;
using RushDay.Infrastructure.Queries;

namespace RushDay.Api.Endpoints;

/// <summary>
/// The module catalogue for every signed-in role (02-api.md section 8.2, D13): the list is viewer-agnostic and served
/// from <c>catalogue:all</c> (30 s), so a rush on it does no database work; the detail reads the module row uncached so
/// its <c>enrolledCount</c> is live. A student's own state comes from <c>GET /api/me/enrolments</c>.
/// </summary>
public static class ModuleEndpoints
{
    public static RouteGroupBuilder MapModuleEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet("/modules", CatalogueAsync).WithTags("Modules").WithName("Catalogue");
        api.MapGet("/modules/" + MeEndpoints.ModuleCodeRoute, DetailAsync).WithTags("Modules").WithName("ModuleDetail");

        return api;
    }

    // GET /api/modules: 0 queries on a cache hit; window state is evaluated against the request's clock.
    private static async Task<IResult> CatalogueAsync(CatalogueCache catalogue, EnrolmentWindowService windows, TimeProvider clock, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var calendar = await windows.CurrentAsync(cancellationToken);
        var allWindows = await windows.AllAsync(cancellationToken);
        var modules = await catalogue.GetAsync(cancellationToken);
        return TypedResults.Ok(ModuleSummary.From(modules, allWindows, calendar.AcademicYear, now));
    }

    // GET /api/modules/{code}: three queries (row, slots, lecturers); an inactive module is returned with isActive false.
    private static async Task<IResult> DetailAsync(string code, ModuleDetailQuery query, EnrolmentWindowService windows, TimeProvider clock, CancellationToken cancellationToken)
    {
        var data = await query.ExecuteAsync(code, cancellationToken);
        if (data is null)
        {
            return ProblemResults.Problem(ProblemTypes.ModuleNotFound, "No module has that code.");
        }

        var calendar = await windows.CurrentAsync(cancellationToken);
        var window = await windows.FindAsync(calendar.AcademicYear, data.Module.Semester, cancellationToken);
        return TypedResults.Ok(ModuleDetail.From(data, window, clock.GetUtcNow()));
    }
}
