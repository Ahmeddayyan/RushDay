using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Queries;

/// <summary>
/// A clamped page of a paged read (02-api.md section 1): <c>page</c> starts at 1 and <c>pageSize</c> is clamped to
/// 1..<c>maxSize</c> (100 in general, 200 for rosters and the audit log, 500 for the marks sheet). A page that would
/// end beyond row <see cref="MaxRows"/> is refused (400 <c>validation</c>, review S6 E14): an OFFSET that deep costs
/// a full scan per page, and the filters and the CSV exports are the way to reach older rows.
/// </summary>
public readonly record struct PageRequest(int Page, int PageSize)
{
    /// <summary>Far beyond any real page; keeps <see cref="Skip"/> inside <see cref="int"/>.</summary>
    public const int MaxPage = 100_000;

    /// <summary>The deepest row a page may reach: <c>page x pageSize</c> at most 10,000.</summary>
    public const int MaxRows = 10_000;

    public int Skip => (Page - 1) * PageSize;

    /// <summary>True when the page ends beyond <see cref="MaxRows"/> (the route answers 400 <c>validation</c>).</summary>
    public bool IsTooDeep => (long)Page * PageSize > MaxRows;

    public static PageRequest Of(int? page, int? pageSize, int defaultSize, int maxSize) =>
        new(Math.Clamp(page ?? 1, 1, MaxPage), Math.Clamp(pageSize ?? defaultSize, 1, maxSize));
}

/// <summary>One page of rows with the total that matched the filter (the API's <c>Paged&lt;T&gt;</c>).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

/// <summary>
/// The free-text search of 02-api.md section 8.4 (<c>student_number ILIKE @q || '%' OR full_name ILIKE '%' || @q ||
/// '%'</c>, reused for accounts and lecturers): the term is bound as a parameter with <c>%</c>, <c>_</c> and the escape
/// character itself escaped, so a search never becomes a pattern of the caller's making.
/// </summary>
public static class SearchText
{
    /// <summary>The escape character passed to <c>ILIKE ... ESCAPE</c>.</summary>
    public const string EscapeCharacter = "\\";

    /// <summary>The trimmed term, or null when there is nothing to search for.</summary>
    public static string? Normalise(string? q) => string.IsNullOrWhiteSpace(q) ? null : q.Trim();

    public static string Escape(string term)
    {
        ArgumentNullException.ThrowIfNull(term);
        return term
            .Replace(EscapeCharacter, EscapeCharacter + EscapeCharacter, StringComparison.Ordinal)
            .Replace("%", EscapeCharacter + "%", StringComparison.Ordinal)
            .Replace("_", EscapeCharacter + "_", StringComparison.Ordinal);
    }

    /// <summary><c>term%</c>: a prefix match.</summary>
    public static string Prefix(string term) => Escape(term) + "%";

    /// <summary><c>%term%</c>: a fragment match.</summary>
    public static string Fragment(string term) => "%" + Escape(term) + "%";
}

/// <summary>A module a lecturer is assigned to, with the lecturer's role on it.</summary>
public sealed record TaughtModule(Guid Id, string Code, string Title, ModuleLecturerRole Role);

/// <summary>Module lookups shared by the staff reads and services.</summary>
public static class StaffModules
{
    /// <summary>
    /// The module of <paramref name="code"/> resolved <b>through</b> <c>module_lecturers</c> (<c>WHERE ml.lecturer_id =
    /// @me AND m.code = @code</c>, 02-api.md section 4): null for a module the lecturer does not teach, so a policy bug
    /// yields "not found", never another module's data. A lecturer who has left (<c>lecturers.left_at</c> set) teaches
    /// nothing: their assignments stay for the record but carry no authority (review S6 E5).
    /// </summary>
    public static Task<TaughtModule?> TaughtAsync(RushDayDbContext db, Guid lecturerId, string code, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(code);
        var normalised = NormaliseCode(code);

        return (from ml in db.ModuleLecturers.AsNoTracking()
                join l in db.Lecturers.AsNoTracking() on ml.LecturerId equals l.Id
                join m in db.Modules.AsNoTracking() on ml.ModuleId equals m.Id
                where ml.LecturerId == lecturerId && l.LeftAt == null && m.Code == normalised
                select new TaughtModule(m.Id, m.Code, m.Title, ml.Role))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>The id, code and title of any module (administrators' reads), or null.</summary>
    public static async Task<(Guid Id, string Code, string Title)?> FindAsync(RushDayDbContext db, string code, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(code);
        var normalised = NormaliseCode(code);

        var row = await db.Modules.AsNoTracking()
            .Where(m => m.Code == normalised)
            .Select(m => new { m.Id, m.Code, m.Title })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : (row.Id, row.Code, row.Title);
    }

    /// <summary>One module as the module shapes show it (the row read uncached, so its count is live, and its lecturers).</summary>
    public static async Task<CatalogueModule> CatalogueModuleAsync(RushDayDbContext db, Guid moduleId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var modules = await CatalogueModulesAsync(db, db.Modules.AsNoTracking().Where(m => m.Id == moduleId), cancellationToken);
        return modules.Single();
    }

    /// <summary>The given modules with their lecturers, ordered by code, in two queries (modules; module_lecturers ⋈ lecturers).</summary>
    public static async Task<IReadOnlyList<CatalogueModule>> CatalogueModulesAsync(RushDayDbContext db, IQueryable<Module> modules, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(modules);

        var rows = await modules
            .OrderBy(m => m.Code)
            .Select(m => new { m.Id, m.Code, m.Title, m.Department, m.Description, m.Credits, m.Semester, m.Capacity, m.EnrolledCount, m.IsActive })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var lecturers = await CatalogueCache.LoadLecturersAsync(db, [.. rows.Select(r => r.Id)], cancellationToken);
        return
        [
            .. rows.Select(m => new CatalogueModule(
                m.Id,
                m.Code,
                m.Title,
                m.Department,
                m.Description,
                m.Credits,
                m.Semester,
                m.Capacity,
                m.EnrolledCount,
                m.IsActive,
                lecturers.TryGetValue(m.Id, out var assigned) ? assigned : [])),
        ];
    }

    /// <summary>The display name of the module's leader (<c>Dr Aisha Khan</c>), for "Ask {leader} to submit", or null.</summary>
    public static Task<string?> LeaderNameAsync(RushDayDbContext db, Guid moduleId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        return (from ml in db.ModuleLecturers.AsNoTracking()
                join l in db.Lecturers.AsNoTracking() on ml.LecturerId equals l.Id
                where ml.ModuleId == moduleId && ml.Role == ModuleLecturerRole.Leader
                orderby l.StaffNumber
                select l.Title + " " + l.FullName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public static string NormaliseCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return code.Trim().ToUpperInvariant();
    }
}

/// <summary>One roster line (02-api.md section 8.4).</summary>
public sealed record RosterRow(
    string StudentNumber,
    string FullName,
    string Programme,
    int YearOfStudy,
    EnrolmentStatus Status,
    DateTimeOffset EnrolledAt,
    DateTimeOffset? WithdrawnAt);

/// <summary>
/// A module's roster for one academic year (04-performance-and-ops.md section 3): a count and a page of <c>enrolments
/// e JOIN students s WHERE e.module_id = @m AND e.academic_year = @year [AND the q predicate] ORDER BY e.status,
/// s.student_number</c>, active rows first. For a lecturer (<paramref name="lecturerId"/> set) the join to
/// <c>module_lecturers</c> is part of the <c>WHERE</c>, so a policy bug returns nothing rather than another module's
/// students; administrators read without it (D33).
/// </summary>
public sealed class RosterQuery(RushDayDbContext db)
{
    /// <summary>The CSV export's ceiling; no module holds anywhere near this many enrolments in a year.</summary>
    public const int ExportLimit = 10_000;

    public async Task<PagedResult<RosterRow>> ExecuteAsync(
        Guid moduleId,
        string academicYear,
        Guid? lecturerId,
        string? q,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        var rows = Rows(moduleId, academicYear, lecturerId, q);
        var total = await rows.CountAsync(cancellationToken);
        var items = await Ordered(rows).Skip(page.Skip).Take(page.PageSize).Select(Project()).ToListAsync(cancellationToken);
        return new PagedResult<RosterRow>(items, page.Page, page.PageSize, total);
    }

    /// <summary>Every roster line of the year (the lecturer's CSV export), in roster order.</summary>
    public async Task<IReadOnlyList<RosterRow>> AllAsync(Guid moduleId, string academicYear, Guid? lecturerId, CancellationToken cancellationToken = default) =>
        await Ordered(Rows(moduleId, academicYear, lecturerId, q: null)).Take(ExportLimit).Select(Project()).ToListAsync(cancellationToken);

    private static IQueryable<RosterSource> Ordered(IQueryable<RosterSource> rows) =>
        rows.OrderBy(r => r.Enrolment.Status).ThenBy(r => r.Student.StudentNumber);

    private static System.Linq.Expressions.Expression<Func<RosterSource, RosterRow>> Project() =>
        r => new RosterRow(
            r.Student.StudentNumber,
            r.Student.FullName,
            r.Student.Programme,
            r.Student.YearOfStudy,
            r.Enrolment.Status,
            r.Enrolment.EnrolledAt,
            r.Enrolment.WithdrawnAt);

    private IQueryable<RosterSource> Rows(Guid moduleId, string academicYear, Guid? lecturerId, string? q)
    {
        var enrolments = db.Enrolments.AsNoTracking().Where(e => e.ModuleId == moduleId && e.AcademicYear == academicYear);
        if (lecturerId is { } me)
        {
            enrolments = enrolments.Where(e => db.ModuleLecturers.Any(ml => ml.ModuleId == e.ModuleId && ml.LecturerId == me));
        }

        var rows = from e in enrolments
                   join s in db.Students.AsNoTracking() on e.StudentId equals s.Id
                   select new RosterSource { Enrolment = e, Student = s };

        if (SearchText.Normalise(q) is { } term)
        {
            var prefix = SearchText.Prefix(term);
            var fragment = SearchText.Fragment(term);
            rows = rows.Where(r => EF.Functions.ILike(r.Student.StudentNumber, prefix, SearchText.EscapeCharacter)
                || EF.Functions.ILike(r.Student.FullName, fragment, SearchText.EscapeCharacter));
        }

        return rows;
    }

    /// <summary>A member-initialised pair, so EF can bind the later operators back to the columns.</summary>
    private sealed class RosterSource
    {
        public required Enrolment Enrolment { get; init; }

        public required Domain.Students.Student Student { get; init; }
    }
}
