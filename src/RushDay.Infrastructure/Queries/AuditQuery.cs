using System.Data;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Audit;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Queries;

/// <summary>An audit row as the log shows it (the data behind <c>AuditEventView</c>); <see cref="Details"/> is the stored JSON text.</summary>
public sealed record AuditEventRow(
    Guid Id,
    DateTimeOffset OccurredAt,
    string? ActorUsername,
    string? ActorRole,
    string Action,
    string SubjectType,
    string? SubjectId,
    string? StudentNumber,
    string? ModuleCode,
    string? Details,
    string? RequestId);

/// <summary>
/// The audit filters of <c>GET /api/admin/audit</c> and its CSV export. Each maps to an indexed column: the actor's
/// username is resolved to <c>actor_user_id</c> first, the student number to <c>student_id</c>, the module code to
/// <c>module_id</c>; <c>action</c> is an exact match; <c>from</c> and <c>to</c> bound <c>occurred_at</c> inclusively.
/// </summary>
public sealed record AuditFilter(string? Actor, string? StudentNumber, string? ModuleCode, string? Action, DateTimeOffset? From, DateTimeOffset? To);

/// <summary>The row cap of an export and whether the filtered rows exceed it.</summary>
public sealed record AuditExportPlan(int Cap, int RowCount, bool Truncated);

/// <summary>
/// The audit log reads (02-api.md section 8.5, 04-performance-and-ops.md section 3): a count and a page, newest first,
/// and the CSV export's plan and rows. The table is append-only at the database; nothing here writes to it.
/// </summary>
public sealed class AuditQuery(RushDayDbContext db)
{
    /// <summary>The export cap when both bounds are given and span at most <see cref="WideRangeDays"/> days.</summary>
    public const int NarrowRangeCap = 50_000;

    /// <summary>The export cap otherwise.</summary>
    public const int DefaultCap = 10_000;

    public const int WideRangeDays = 31;

    public async Task<PagedResult<AuditEventRow>> ListAsync(AuditFilter filter, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var events = await FilterAsync(db, filter, cancellationToken);
        var total = await events.CountAsync(cancellationToken);
        var rows = await Project(db, events.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id).Skip(page.Skip).Take(page.PageSize))
            .ToListAsync(cancellationToken);
        return new PagedResult<AuditEventRow>([.. rows.OrderByDescending(r => r.OccurredAt).ThenByDescending(r => r.Id)], page.Page, page.PageSize, total);
    }

    /// <summary>50,000 rows for a bounded range of at most 31 days, otherwise 10,000.</summary>
    public static int CapFor(AuditFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return filter is { From: { } from, To: { } to } && to >= from && to - from <= TimeSpan.FromDays(WideRangeDays) ? NarrowRangeCap : DefaultCap;
    }

    /// <summary>
    /// How many rows the export will write (at most the cap) and whether more matched, counted on
    /// <paramref name="snapshot"/>, whose transaction the rows are later streamed from, so the count is exact.
    /// </summary>
    public static async Task<AuditExportPlan> PlanAsync(RushDayDbContext snapshot, AuditFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var cap = CapFor(filter);
        var events = await FilterAsync(snapshot, filter, cancellationToken);
        var matched = await events.OrderByDescending(a => a.OccurredAt).Take(cap + 1).CountAsync(cancellationToken);
        return new AuditExportPlan(cap, Math.Min(matched, cap), matched > cap);
    }

    /// <summary>The export's rows, newest first, streamed from <paramref name="snapshot"/>.</summary>
    public static async IAsyncEnumerable<AuditEventRow> StreamAsync(RushDayDbContext snapshot, AuditFilter filter, int cap, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var events = await FilterAsync(snapshot, filter, cancellationToken);
        var rows = Project(snapshot, events.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id).Take(cap)).AsAsyncEnumerable();
        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            yield return row;
        }
    }

    /// <summary>
    /// Opens a REPEATABLE READ transaction on <paramref name="snapshot"/>: every statement in it sees the same snapshot,
    /// so the plan's count and the streamed rows agree, and the <c>audit.exported</c> row committed in between on
    /// another connection is not part of its own export.
    /// </summary>
    public static Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginSnapshotAsync(RushDayDbContext snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
    }

    /// <summary>Projects audit rows with the student number and module code of their denormalised ids.</summary>
    public static IQueryable<AuditEventRow> Project(RushDayDbContext db, IQueryable<AuditEvent> events)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(events);

        return from a in events
               from s in db.Students.AsNoTracking().Where(s => s.Id == a.StudentId).DefaultIfEmpty()
               from m in db.Modules.AsNoTracking().Where(m => m.Id == a.ModuleId).DefaultIfEmpty()
               select new AuditEventRow(
                   a.Id,
                   a.OccurredAt,
                   a.ActorUsername,
                   a.ActorRole,
                   a.Action,
                   a.SubjectType,
                   a.SubjectId,
                   s == null ? null : s.StudentNumber,
                   m == null ? null : m.Code,
                   a.Details,
                   a.RequestId);
    }

    private static async Task<IQueryable<AuditEvent>> FilterAsync(RushDayDbContext db, AuditFilter filter, CancellationToken cancellationToken)
    {
        var events = db.AuditEvents.AsNoTracking();

        if (SearchText.Normalise(filter.Actor) is { } actor)
        {
            var normalised = actor.ToUpperInvariant();
            var actorId = await db.Users.AsNoTracking().Where(u => u.NormalizedUserName == normalised).Select(u => (Guid?)u.Id).SingleOrDefaultAsync(cancellationToken);
            events = actorId is { } id ? events.Where(a => a.ActorUserId == id) : events.Where(a => false);
        }

        if (SearchText.Normalise(filter.StudentNumber) is { } studentNumber)
        {
            var number = studentNumber.ToUpperInvariant();
            var studentId = await db.Students.AsNoTracking().Where(s => s.StudentNumber == number).Select(s => (Guid?)s.Id).SingleOrDefaultAsync(cancellationToken);
            events = studentId is { } id ? events.Where(a => a.StudentId == id) : events.Where(a => false);
        }

        if (SearchText.Normalise(filter.ModuleCode) is { } moduleCode)
        {
            var code = moduleCode.ToUpperInvariant();
            var moduleId = await db.Modules.AsNoTracking().Where(m => m.Code == code).Select(m => (Guid?)m.Id).SingleOrDefaultAsync(cancellationToken);
            events = moduleId is { } id ? events.Where(a => a.ModuleId == id) : events.Where(a => false);
        }

        if (SearchText.Normalise(filter.Action) is { } action)
        {
            events = events.Where(a => a.Action == action);
        }

        if (filter.From is { } from)
        {
            events = events.Where(a => a.OccurredAt >= from);
        }

        if (filter.To is { } to)
        {
            events = events.Where(a => a.OccurredAt <= to);
        }

        return events;
    }
}
