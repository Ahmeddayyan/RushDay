using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Audit;
using RushDay.Infrastructure.Audit;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Seeding;

namespace RushDay.Infrastructure.Ops;

/// <summary>A module whose <c>enrolled_count</c> the reconciliation changed.</summary>
public sealed record ModuleCorrection(string Code, int Before, int After);

/// <summary>
/// <c>POST /api/admin/ops/reconcile</c> (02-api.md section 8.5, 04-performance-and-ops.md section 2.3): the two
/// <c>reconcile_enrolled_count</c> statements of the last backfill step (active enrolments of the current academic
/// year, D28), reporting each corrected module with its count before and after, audited as <c>ops.reconciled</c> in
/// the same transaction. The module rows are locked first, so the before values are exactly what was replaced.
/// </summary>
public sealed class ReconcileService(RushDayDbContext db, AuditWriter audit)
{
    public async Task<IReadOnlyList<ModuleCorrection>> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var corrections = await ReconcileInTransactionAsync(db, cancellationToken);
        audit.Record(db, AuditActions.OpsReconciled, AuditSubjects.System, subjectId: null, new { modulesCorrected = corrections.Select(c => new { code = c.Code, before = c.Before, after = c.After }).ToArray() });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return corrections;
    }

    /// <summary>Runs the statements inside the caller's transaction and reports what changed.</summary>
    public static async Task<IReadOnlyList<ModuleCorrection>> ReconcileInTransactionAsync(RushDayDbContext db, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var before = await db.Modules
            .FromSql($"SELECT * FROM modules ORDER BY id FOR UPDATE")
            .AsNoTracking()
            .Select(m => new { m.Id, m.Code, m.EnrolledCount })
            .ToListAsync(cancellationToken);

        await StartupBackfills.ReconcileEnrolledCountAsync(db, cancellationToken);

        var after = await db.Modules.AsNoTracking().Select(m => new { m.Id, m.EnrolledCount }).ToDictionaryAsync(m => m.Id, m => m.EnrolledCount, cancellationToken);
        return
        [
            .. before
                .Where(m => after.TryGetValue(m.Id, out var count) && count != m.EnrolledCount)
                .OrderBy(m => m.Code, StringComparer.Ordinal)
                .Select(m => new ModuleCorrection(m.Code, m.EnrolledCount, after[m.Id])),
        ];
    }
}
