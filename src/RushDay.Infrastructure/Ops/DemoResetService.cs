using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Audit;
using RushDay.Infrastructure.Audit;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Seeding;

namespace RushDay.Infrastructure.Ops;

/// <summary>
/// <c>POST /api/admin/ops/demo-reset</c>, mapped only in demo mode (02-api.md section 8.5): backfill step 11 on demand
/// (every self-service CS3099 enrolment without a mark is withdrawn; admin overrides and marked students stay), then
/// the reconciliation, so the hot module's places come back without a restart. One transaction, audited as
/// <c>system.demo_reset</c> with the caller as actor (and, like the backfill's row, the module id for filtering).
/// </summary>
public sealed class DemoResetService(RushDayDbContext db, AuditWriter audit)
{
    public async Task<int> ResetAsync(CancellationToken cancellationToken = default)
    {
        var hotModuleId = await db.Modules.AsNoTracking()
            .Where(m => m.Code == DatabaseSeeder.HotModuleCode)
            .Select(m => (Guid?)m.Id)
            .SingleOrDefaultAsync(cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var withdrawn = await StartupBackfills.WithdrawDemoHotModuleEnrolmentsAsync(db, cancellationToken);
        await ReconcileService.ReconcileInTransactionAsync(db, cancellationToken);

        audit.Record(db, AuditActions.SystemDemoReset, AuditSubjects.System, DatabaseSeeder.HotModuleCode, new { moduleCode = DatabaseSeeder.HotModuleCode, withdrawn }, moduleId: hotModuleId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return withdrawn;
    }
}
