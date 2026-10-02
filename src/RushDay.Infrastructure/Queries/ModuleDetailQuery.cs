using Microsoft.EntityFrameworkCore;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Queries;

/// <summary>A module with its lecturers and weekly slots: the data behind <c>ModuleDetail</c>.</summary>
public sealed record ModuleDetailData(CatalogueModule Module, IReadOnlyList<TimetableSlotRow> Timetable);

/// <summary>
/// <c>GET /api/modules/{code}</c> in three queries (04-performance-and-ops.md section 3): the module row read uncached,
/// so <c>enrolledCount</c> is live; its slots; its lecturers. An inactive module is returned too, so a link from a
/// dashboard or an old enrolment never dead-ends. Also used by the administrator's module routes (S6).
/// </summary>
public sealed class ModuleDetailQuery(RushDayDbContext db)
{
    /// <summary>Null when no module has the code (404 <c>module-not-found</c>).</summary>
    public async Task<ModuleDetailData?> ExecuteAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);
        var normalised = code.Trim().ToUpperInvariant();

        var row = await db.Modules.AsNoTracking()
            .Where(m => m.Code == normalised)
            .Select(m => new { m.Id, m.Code, m.Title, m.Department, m.Description, m.Credits, m.Semester, m.Capacity, m.EnrolledCount, m.IsActive })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var slots = await TimetableQuery.SlotsOf(db, db.Modules.AsNoTracking().Where(m => m.Id == row.Id)).ToListAsync(cancellationToken);
        var lecturers = await CatalogueCache.LoadLecturersAsync(db, [row.Id], cancellationToken);

        var module = new CatalogueModule(
            row.Id,
            row.Code,
            row.Title,
            row.Department,
            row.Description,
            row.Credits,
            row.Semester,
            row.Capacity,
            row.EnrolledCount,
            row.IsActive,
            lecturers.TryGetValue(row.Id, out var assigned) ? assigned : []);
        return new ModuleDetailData(module, TimetableSlotRow.InWeekOrder(slots));
    }
}
