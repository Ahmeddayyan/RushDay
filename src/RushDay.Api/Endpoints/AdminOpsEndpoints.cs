using RushDay.Api.Contracts;
using RushDay.Api.Observability;
using RushDay.Api.Security;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Ops;

namespace RushDay.Api.Endpoints;

/// <summary>
/// Operations, <c>/api/admin/ops</c> (02-api.md section 8.5, 04-performance-and-ops.md section 6): the in-process
/// snapshot (outside the global concurrency limiter, behind the per-user <c>ops-metrics</c> bucket, no database work,
/// so it answers while the API sheds load), the on-demand reconciliation of <c>enrolled_count</c>, and, only when
/// demo mode is on, the demo reset of CS3099. On a customer deployment the reset path is not mapped at all, so it
/// answers the <c>/api</c> fallback's 404 <c>not-found</c>.
/// </summary>
public static class AdminOpsEndpoints
{
    public static RouteGroupBuilder MapAdminOpsEndpoints(this RouteGroupBuilder admin, bool demoEnabled)
    {
        ArgumentNullException.ThrowIfNull(admin);

        admin.MapGet("/ops/metrics", (MetricsSnapshotService snapshots) => TypedResults.Ok(snapshots.GetSnapshot()))
            .RequireRateLimiting(RateLimitPolicies.OpsMetrics)
            .WithName("OpsMetrics");

        admin.MapPost("/ops/reconcile", ReconcileAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("OpsReconcile");

        if (demoEnabled)
        {
            admin.MapPost("/ops/demo-reset", DemoResetAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("OpsDemoReset");
        }

        return admin;
    }

    private static async Task<IResult> ReconcileAsync(ReconcileService reconcile, CatalogueCache catalogue, CancellationToken cancellationToken)
    {
        var corrections = await reconcile.ReconcileAsync(cancellationToken);
        if (corrections.Count > 0)
        {
            await catalogue.InvalidateAsync(cancellationToken);
        }

        return TypedResults.Ok(ReconcileResponse.From(corrections));
    }

    private static async Task<IResult> DemoResetAsync(DemoResetService reset, CatalogueCache catalogue, CancellationToken cancellationToken)
    {
        var withdrawn = await reset.ResetAsync(cancellationToken);
        await catalogue.InvalidateAsync(cancellationToken);
        return TypedResults.Ok(new DemoResetResponse(withdrawn));
    }
}
