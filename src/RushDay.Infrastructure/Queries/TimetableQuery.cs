using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Queries;

/// <summary>One weekly slot joined to its module: the data behind <c>TimetableEntry</c>.</summary>
public sealed record TimetableSlotRow(
    string ModuleCode,
    string ModuleTitle,
    Semester Semester,
    DayOfWeek Day,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string Room)
{
    /// <summary><c>kind</c> is <c>lab</c> when the room contains <c>-Lab</c>, else <c>lecture</c> (02-api.md section 7).</summary>
    public bool IsLab => Room.Contains("-Lab", StringComparison.Ordinal);

    /// <summary>Monday first, then by start time and module code: the order every timetable is listed in.</summary>
    public static IReadOnlyList<TimetableSlotRow> InWeekOrder(IEnumerable<TimetableSlotRow> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        return [.. slots.OrderBy(s => ((int)s.Day + 6) % 7).ThenBy(s => s.StartTime).ThenBy(s => s.ModuleCode, StringComparer.Ordinal)];
    }
}

/// <summary>
/// <c>GET /api/me/timetable</c> (04-performance-and-ops.md section 3): one query, slots ⋈ modules for the student's
/// active enrolments of the current academic year on modules of the current semester (00-overview.md section 4.5).
/// </summary>
public sealed class TimetableQuery(RushDayDbContext db)
{
    /// <summary>Slots of the given modules that run in <paramref name="semester"/> (the dashboard's query (3), module detail).</summary>
    public static IQueryable<TimetableSlotRow> SlotsOf(RushDayDbContext db, IQueryable<Module> modules)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(modules);

        return from t in db.TimetableSlots.AsNoTracking()
               join m in modules on t.ModuleId equals m.Id
               select new TimetableSlotRow(m.Code, m.Title, m.Semester, t.Day, t.StartTime, t.EndTime, t.Room);
    }

    public async Task<IReadOnlyList<TimetableSlotRow>> ExecuteAsync(Guid studentId, string currentYear, Semester currentSemester, CancellationToken cancellationToken = default)
    {
        var slots = await (
            from e in db.Enrolments.AsNoTracking()
            join m in db.Modules.AsNoTracking() on e.ModuleId equals m.Id
            join t in db.TimetableSlots.AsNoTracking() on m.Id equals t.ModuleId
            where e.StudentId == studentId && e.Status == EnrolmentStatus.Active && e.AcademicYear == currentYear && m.Semester == currentSemester
            select new TimetableSlotRow(m.Code, m.Title, m.Semester, t.Day, t.StartTime, t.EndTime, t.Room))
            .ToListAsync(cancellationToken);

        return TimetableSlotRow.InWeekOrder(slots);
    }
}
