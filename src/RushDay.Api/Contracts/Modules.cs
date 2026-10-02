using System.Globalization;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Enrolments;
using RushDay.Infrastructure.Queries;

namespace RushDay.Api.Contracts;

/// <summary><c>TimetableEntry.kind</c>: <c>lab</c> when the room contains <c>-Lab</c>.</summary>
public enum TimetableKind
{
    Lecture,
    Lab,
}

/// <summary><c>TimetableEntry</c> of 02-api.md section 7: times of day as <c>"HH:mm"</c>, <c>day</c> as <c>"monday"</c>..</summary>
public sealed record TimetableEntry(
    string ModuleCode,
    string ModuleTitle,
    Semester Semester,
    DayOfWeek Day,
    string StartTime,
    string EndTime,
    string Room,
    TimetableKind Kind)
{
    public static TimetableEntry From(TimetableSlotRow slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        return new TimetableEntry(
            slot.ModuleCode,
            slot.ModuleTitle,
            slot.Semester,
            slot.Day,
            slot.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            slot.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            slot.Room,
            slot.IsLab ? TimetableKind.Lab : TimetableKind.Lecture);
    }

    public static IReadOnlyList<TimetableEntry> From(IEnumerable<TimetableSlotRow> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        return [.. slots.Select(From)];
    }
}

/// <summary>The <c>Lecturer</c> shape of 02-api.md section 7 (<c>role</c> is <c>leader</c> or <c>teacher</c>).</summary>
public sealed record ModuleLecturerView(string StaffNumber, string FullName, string Title, ModuleLecturerRole Role, bool Left)
{
    public static ModuleLecturerView From(ModuleLecturerInfo lecturer)
    {
        ArgumentNullException.ThrowIfNull(lecturer);
        return new ModuleLecturerView(lecturer.StaffNumber, lecturer.FullName, lecturer.Title, lecturer.Role, lecturer.Left);
    }
}

/// <summary>
/// <c>ModuleSummary</c> of 02-api.md section 7. <c>enrolledCount</c> and <c>placesRemaining</c> are the current academic
/// year's; <c>enrolmentState</c> and the window instants come from the cached windows for the current year and the
/// module's semester, evaluated at request time. Not sealed: <see cref="ModuleDetail"/> and the staff shapes extend it.
/// </summary>
public record ModuleSummary(
    string Code,
    string Title,
    string Department,
    int Level,
    int Credits,
    Semester Semester,
    int Capacity,
    int EnrolledCount,
    int PlacesRemaining,
    bool IsActive,
    IReadOnlyList<ModuleLecturerView> Lecturers,
    EnrolmentState EnrolmentState,
    DateTimeOffset? WindowOpensAt,
    DateTimeOffset? WindowClosesAt,
    DateTimeOffset? WithdrawalDeadlineAt)
{
    /// <summary><paramref name="window"/> is the window of (current academic year, the module's semester), or null.</summary>
    public static ModuleSummary From(CatalogueModule module, EnrolmentWindowSnapshot? window, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(module);

        var state = ModuleWindow.Of(window, now);
        return new ModuleSummary(
            module.Code,
            module.Title,
            module.Department,
            module.Level,
            module.Credits,
            module.Semester,
            module.Capacity,
            module.EnrolledCount,
            module.PlacesRemaining,
            module.IsActive,
            [.. module.Lecturers.Select(ModuleLecturerView.From)],
            state.State,
            state.OpensAt,
            state.ClosesAt,
            state.WithdrawalDeadlineAt);
    }

    /// <summary>Every module with the window of (<paramref name="academicYear"/>, its semester) from <paramref name="windows"/>.</summary>
    public static IReadOnlyList<ModuleSummary> From(IEnumerable<CatalogueModule> modules, IReadOnlyList<EnrolmentWindowSnapshot> windows, string academicYear, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(modules);
        return [.. modules.Select(m => From(m, EnrolmentWindowService.Find(windows, academicYear, m.Semester), now))];
    }
}

/// <summary><c>ModuleDetail = ModuleSummary &amp; { description, timetable }</c>.</summary>
public sealed record ModuleDetail : ModuleSummary
{
    public ModuleDetail(ModuleSummary summary, string? description, IReadOnlyList<TimetableEntry> timetable)
        : base(summary)
    {
        Description = description;
        Timetable = timetable;
    }

    public string? Description { get; init; }

    public IReadOnlyList<TimetableEntry> Timetable { get; init; }

    public static ModuleDetail From(ModuleDetailData data, EnrolmentWindowSnapshot? window, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new ModuleDetail(ModuleSummary.From(data.Module, window, now), data.Module.Description, TimetableEntry.From(data.Timetable));
    }
}
