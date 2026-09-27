using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RushDay.Api.Contracts;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Api.Endpoints;

public static class ModuleEndpoints
{
    public static IEndpointRouteBuilder MapModuleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/modules").WithTags("Modules");

        group.MapGet("/", ListAsync).WithName("ListModules");
        group.MapGet("/{code}", GetAsync).WithName("GetModule");

        return app;
    }

    /// <summary>The catalogue: identical for every student, yet v0 hits the database on every call.</summary>
    private static async Task<Ok<List<ModuleCatalogueItem>>> ListAsync(RushDayDbContext db, CancellationToken cancellationToken)
    {
        var modules = await db.Modules.AsNoTracking()
            .OrderBy(m => m.Code)
            .ToListAsync(cancellationToken);

        var items = modules
            .Select(m => new ModuleCatalogueItem(m.Code, m.Title, m.Credits, m.Semester.ToString(), m.Capacity))
            .ToList();

        return TypedResults.Ok(items);
    }

    private static async Task<Results<Ok<ModuleDetail>, NotFound>> GetAsync(string code, RushDayDbContext db, CancellationToken cancellationToken)
    {
        var module = await db.Modules.AsNoTracking()
            .SingleOrDefaultAsync(m => m.Code == code, cancellationToken);
        if (module is null)
        {
            return TypedResults.NotFound();
        }

        var enrolled = await db.Enrolments.CountAsync(e => e.ModuleId == module.Id, cancellationToken);

        return TypedResults.Ok(new ModuleDetail(
            module.Code,
            module.Title,
            module.Credits,
            module.Semester.ToString(),
            module.Capacity,
            enrolled,
            Math.Max(0, module.Capacity - enrolled)));
    }
}
