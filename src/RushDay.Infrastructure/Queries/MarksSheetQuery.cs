using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Queries;

/// <summary><c>MarksStatus.status</c> (02-api.md section 7).</summary>
public enum MarksState
{
    NoStudents,
    Draft,
    Submitted,
    Scheduled,
    Published,
}

/// <summary>
/// <c>MarksStatus</c> of one module for one academic year (02-api.md section 7): <c>total</c> active enrolments,
/// <c>entered</c> of them with a grade row that belongs to the module's current stage (any grade while the module is in
/// draft; only a Submitted or Published one once it has left draft, so a Draft that reappeared on a submitted module
/// is <c>missing</c>, review S6 E1), and the status from those grades only (grades of withdrawn enrolments are
/// ignored). <see cref="PublishedAt"/> is the instant of the live or scheduled publication.
/// </summary>
public sealed record MarksStatusData(MarksState Status, int Entered, int Missing, int Total, DateTimeOffset? SubmittedAt, DateTimeOffset? PublishedAt)
{
    public static MarksStatusData NoStudents { get; } = new(MarksState.NoStudents, 0, 0, 0, null, null);

    /// <summary>
    /// A module is publishable iff <c>status = 'submitted' AND missing = 0</c>: every active enrolment's grade is
    /// Submitted, so a publish (which moves exactly the Submitted grades) leaves no active student's mark behind.
    /// </summary>
    public bool IsPublishable => Status == MarksState.Submitted && Missing == 0;

    /// <summary>Lecturers may save marks only while nothing has been submitted.</summary>
    public bool IsEditable => Status is MarksState.Draft or MarksState.NoStudents;
}

/// <summary>
/// Raw ADO.NET reads on the context's connection (and its transaction, when one is open), for the aggregates and
/// <c>RETURNING</c> statements EF cannot express: <c>SqlQuery</c> wraps SQL in a subquery, which PostgreSQL refuses for
/// a data-modifying statement. Parameters are always bound by name, never interpolated.
/// </summary>
public static class RawSql
{
    public static async Task<List<T>> QueryAsync<T>(
        RushDayDbContext db,
        string sql,
        IReadOnlyList<NpgsqlParameter> parameters,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(map);

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            foreach (var parameter in parameters)
            {
                command.Parameters.Add(parameter);
            }

            var rows = new List<T>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(map(reader));
            }

            return rows;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    public static DateTimeOffset? NullableInstant(DbDataReader reader, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    public static Guid? NullableGuid(DbDataReader reader, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }
}

/// <summary>
/// The <c>MarksStatus</c> aggregate (04-performance-and-ops.md section 3): one grouped query over the year's active
/// enrolments LEFT JOIN their grades, for any set of modules. A module absent from the result has no active
/// enrolment that year (<c>noStudents</c>).
/// </summary>
public static class MarksStatusQuery
{
    private const string AggregateSql = """
        SELECT e.module_id,
               count(*)::int,
               count(g.id)::int,
               (count(*) FILTER (WHERE g.status = 'Published' AND g.published_at <= @now))::int,
               (count(*) FILTER (WHERE g.status = 'Published' AND g.published_at > @now))::int,
               (count(*) FILTER (WHERE g.status = 'Submitted'))::int,
               max(g.submitted_at),
               max(g.published_at) FILTER (WHERE g.status = 'Published' AND g.published_at <= @now),
               min(g.published_at) FILTER (WHERE g.status = 'Published' AND g.published_at > @now),
               (count(*) FILTER (WHERE g.status = 'Draft'))::int
        FROM enrolments e
        LEFT JOIN grades g ON g.student_id = e.student_id AND g.module_id = e.module_id
        WHERE e.status = 'Active' AND e.academic_year = @year
        """;

    /// <summary>The status of every given module (all modules with enrolments that year when <paramref name="moduleIds"/> is null).</summary>
    public static async Task<IReadOnlyDictionary<Guid, MarksStatusData>> ForModulesAsync(
        RushDayDbContext db,
        string academicYear,
        DateTimeOffset now,
        IReadOnlyCollection<Guid>? moduleIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(academicYear);

        var parameters = new List<NpgsqlParameter>
        {
            new("year", academicYear),
            new("now", now),
        };
        var sql = AggregateSql;
        if (moduleIds is not null)
        {
            if (moduleIds.Count == 0)
            {
                return new Dictionary<Guid, MarksStatusData>();
            }

            sql += " AND e.module_id = ANY(@ids)";
            parameters.Add(new NpgsqlParameter("ids", moduleIds.ToArray()));
        }

        sql += " GROUP BY e.module_id";

        var rows = await RawSql.QueryAsync(
            db,
            sql,
            parameters,
            r => (Id: r.GetGuid(0), Status: From(
                r.GetInt32(1),
                r.GetInt32(2),
                r.GetInt32(3),
                r.GetInt32(4),
                r.GetInt32(5),
                RawSql.NullableInstant(r, 6),
                RawSql.NullableInstant(r, 7),
                RawSql.NullableInstant(r, 8),
                r.GetInt32(9))),
            cancellationToken);
        return rows.ToDictionary(r => r.Id, r => r.Status);
    }

    public static async Task<MarksStatusData> ForModuleAsync(RushDayDbContext db, Guid moduleId, string academicYear, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var statuses = await ForModulesAsync(db, academicYear, now, [moduleId], cancellationToken);
        return Of(statuses, moduleId);
    }

    public static MarksStatusData Of(IReadOnlyDictionary<Guid, MarksStatusData> statuses, Guid moduleId)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        return statuses.TryGetValue(moduleId, out var status) ? status : MarksStatusData.NoStudents;
    }

    /// <summary>
    /// <c>published</c> if any grade is live, else <c>scheduled</c> if any is published in the future, else
    /// <c>submitted</c> if any is Submitted, else <c>draft</c> (grades exist or students are enrolled), else
    /// <c>noStudents</c>. <paramref name="graded"/> counts active enrolments with any grade row, <paramref name="drafts"/>
    /// those whose grade is still Draft: once the module has left draft a Draft is not part of the submission (a student
    /// withdrawn before submit and enrolled again, or data from before review S6 E1), so it counts as missing and the
    /// module is not publishable until it is returned to draft.
    /// </summary>
    public static MarksStatusData From(
        int total,
        int graded,
        int live,
        int scheduled,
        int submitted,
        DateTimeOffset? submittedAt,
        DateTimeOffset? liveAt,
        DateTimeOffset? scheduledAt,
        int drafts = 0)
    {
        var leftDraft = live > 0 || scheduled > 0 || submitted > 0;
        var entered = leftDraft ? Math.Max(0, graded - drafts) : graded;
        var missing = Math.Max(0, total - entered);
        if (live > 0)
        {
            return new MarksStatusData(MarksState.Published, entered, missing, total, submittedAt, liveAt);
        }

        if (scheduled > 0)
        {
            return new MarksStatusData(MarksState.Scheduled, entered, missing, total, submittedAt, scheduledAt);
        }

        if (submitted > 0)
        {
            return new MarksStatusData(MarksState.Submitted, entered, missing, total, submittedAt, null);
        }

        return entered > 0 || total > 0
            ? new MarksStatusData(MarksState.Draft, entered, missing, total, null, null)
            : MarksStatusData.NoStudents;
    }
}

/// <summary>One row of the marks sheet (02-api.md section 8.4 <c>MarksSheet.rows[]</c>).</summary>
public sealed record MarksRowData(
    string StudentNumber,
    string FullName,
    EnrolmentStatus EnrolmentStatus,
    GradeOutcome? Outcome,
    int? Mark,
    GradeStatus? GradeStatus,
    int? Version,
    DateTimeOffset? UpdatedAt,
    string? EnteredBy,
    DateTimeOffset? CorrectedAt);

/// <summary>The marks sheet of one module and year: header, whole-module summary and one page of rows.</summary>
public sealed record MarksSheetData(
    Guid ModuleId,
    string Code,
    string Title,
    string AcademicYear,
    MarksStatusData Status,
    ModuleLecturerRole? MyRole,
    string? Leader,
    PagedResult<MarksRowData> Rows);

/// <summary>
/// <c>GET /api/lecturer/modules/{code}/marks</c> and the administrator's read-only copy (04-performance-and-ops.md
/// section 3): the status and summary aggregate over the year's active enrolments, and one page of every enrolment of
/// the year (active first, then withdrawn, by student number) LEFT JOIN its grade and the user who entered it. For a
/// lecturer the <c>module_lecturers</c> predicate is part of every query.
/// </summary>
public sealed class MarksSheetQuery(RushDayDbContext db)
{
    public async Task<MarksSheetData> ExecuteAsync(
        Guid moduleId,
        string code,
        string title,
        string academicYear,
        Guid? lecturerId,
        ModuleLecturerRole? myRole,
        string? q,
        PageRequest page,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var status = await MarksStatusQuery.ForModuleAsync(db, moduleId, academicYear, now, cancellationToken);
        var leader = await StaffModules.LeaderNameAsync(db, moduleId, cancellationToken);

        var enrolments = db.Enrolments.AsNoTracking().Where(e => e.ModuleId == moduleId && e.AcademicYear == academicYear);
        if (lecturerId is { } me)
        {
            enrolments = enrolments.Where(e => db.ModuleLecturers.Any(ml => ml.ModuleId == e.ModuleId && ml.LecturerId == me));
        }

        var students = db.Students.AsNoTracking();
        if (SearchText.Normalise(q) is { } term)
        {
            var prefix = SearchText.Prefix(term);
            var fragment = SearchText.Fragment(term);
            students = students.Where(s => EF.Functions.ILike(s.StudentNumber, prefix, SearchText.EscapeCharacter)
                || EF.Functions.ILike(s.FullName, fragment, SearchText.EscapeCharacter));
        }

        var joined = from e in enrolments
                     join s in students on e.StudentId equals s.Id
                     select new { e.Status, s.StudentNumber, s.FullName, e.StudentId, e.ModuleId };

        var total = await joined.CountAsync(cancellationToken);
        var rows = await (
            from x in joined.OrderBy(x => x.Status).ThenBy(x => x.StudentNumber).Skip(page.Skip).Take(page.PageSize)
            from g in db.Grades.AsNoTracking().Where(g => g.StudentId == x.StudentId && g.ModuleId == x.ModuleId).DefaultIfEmpty()
            from u in db.Users.AsNoTracking().Where(u => u.Id == g.EnteredByUserId).DefaultIfEmpty()
            orderby x.Status, x.StudentNumber
            select new MarksRowData(
                x.StudentNumber,
                x.FullName,
                x.Status,
                g == null ? null : g.Outcome,
                g == null ? null : g.Mark,
                g == null ? null : g.Status,
                g == null ? null : g.Version,
                g == null ? null : g.UpdatedAt,
                u == null ? null : u.DisplayName,
                g == null ? null : g.CorrectedAt))
            .ToListAsync(cancellationToken);

        return new MarksSheetData(
            moduleId,
            code,
            title,
            academicYear,
            status,
            myRole,
            leader,
            new PagedResult<MarksRowData>(rows, page.Page, page.PageSize, total));
    }
}

/// <summary>One module of <c>GET /api/lecturer/modules</c>: the module, the caller's role on it and its marks status.</summary>
public sealed record LecturerModuleData(CatalogueModule Module, ModuleLecturerRole MyRole, MarksStatusData Marks);

/// <summary>
/// <c>GET /api/lecturer/modules</c> (04-performance-and-ops.md section 3): the caller's assignments ⋈ modules, the
/// modules' lecturers, and the <see cref="MarksStatusQuery"/> aggregate for the current academic year.
/// </summary>
public sealed class LecturerModulesQuery(RushDayDbContext db)
{
    public async Task<IReadOnlyList<LecturerModuleData>> ExecuteAsync(Guid lecturerId, string academicYear, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        // A lecturer who has left keeps their assignments on record but lists nothing (review S6 E5).
        var roles = await (
            from ml in db.ModuleLecturers.AsNoTracking()
            join l in db.Lecturers.AsNoTracking() on ml.LecturerId equals l.Id
            where ml.LecturerId == lecturerId && l.LeftAt == null
            select new { ml.ModuleId, ml.Role })
            .ToDictionaryAsync(ml => ml.ModuleId, ml => ml.Role, cancellationToken);
        if (roles.Count == 0)
        {
            return [];
        }

        var ids = roles.Keys.ToArray();
        var modules = await StaffModules.CatalogueModulesAsync(db, db.Modules.AsNoTracking().Where(m => ids.Contains(m.Id)), cancellationToken);
        var statuses = await MarksStatusQuery.ForModulesAsync(db, academicYear, now, ids, cancellationToken);

        return [.. modules.Select(m => new LecturerModuleData(m, roles[m.Id], MarksStatusQuery.Of(statuses, m.Id)))];
    }
}
