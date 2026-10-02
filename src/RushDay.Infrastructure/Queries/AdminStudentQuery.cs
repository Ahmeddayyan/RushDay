using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.Domain.Users;
using RushDay.Infrastructure.Grades;
using RushDay.Infrastructure.Identity;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Queries;

/// <summary>
/// An account's state (02-api.md section 7): <c>disabled</c> when <c>disabled_at</c> is set, else <c>locked</c> while
/// <c>lockout_end &gt; now</c>, else <c>active</c>; <c>none</c> only on the student list, for a student without a login.
/// </summary>
public enum AccountState
{
    None,
    Active,
    Locked,
    Disabled,
}

/// <summary>The data behind <c>AccountView</c>.</summary>
public sealed record AccountRow(
    Guid Id,
    string Username,
    string DisplayName,
    string Role,
    string? StudentNumber,
    string? StaffNumber,
    string? Email,
    AccountState State,
    DateTimeOffset? LockoutEnd,
    bool MustChangePassword,
    bool MfaEnabled,
    bool IsDemo,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

/// <summary>
/// The accounts list and single-account reads (02-api.md section 8.5): <c>q</c> is a username prefix or a display name
/// fragment, <c>role</c> and <c>state</c> filter; ordered by the normalised username (its unique index).
/// </summary>
public sealed class AdminAccountQuery(RushDayDbContext db)
{
    public async Task<PagedResult<AccountRow>> ListAsync(string? q, string? role, AccountState? state, PageRequest page, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var users = db.Users.AsNoTracking();
        if (SearchText.Normalise(q) is { } term)
        {
            var prefix = SearchText.Prefix(term.ToUpperInvariant());
            var fragment = SearchText.Fragment(term);
            users = users.Where(u => EF.Functions.Like(u.NormalizedUserName!, prefix, SearchText.EscapeCharacter)
                || EF.Functions.ILike(u.DisplayName, fragment, SearchText.EscapeCharacter));
        }

        if (RushDayRoles.All.FirstOrDefault(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase)) is { } roleName)
        {
            var roleId = RushDayRoles.IdOf(roleName);
            users = users.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == roleId));
        }

        users = state switch
        {
            AccountState.Disabled => users.Where(u => u.DisabledAt != null),
            AccountState.Locked => users.Where(u => u.DisabledAt == null && u.LockoutEnd > now),
            AccountState.Active => users.Where(u => u.DisabledAt == null && (u.LockoutEnd == null || u.LockoutEnd <= now)),
            _ => users,
        };

        var total = await users.CountAsync(cancellationToken);
        var rows = await Project(users.OrderBy(u => u.NormalizedUserName).Skip(page.Skip).Take(page.PageSize))
            .ToListAsync(cancellationToken);
        return new PagedResult<AccountRow>([.. rows.OrderBy(r => r.NormalizedUserName, StringComparer.Ordinal).Select(r => r.ToRow(now))], page.Page, page.PageSize, total);
    }

    public async Task<AccountRow?> FindAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var row = await Project(db.Users.AsNoTracking().Where(u => u.Id == userId)).SingleOrDefaultAsync(cancellationToken);
        return row?.ToRow(now);
    }

    public async Task<AccountRow?> ForStudentAsync(Guid studentId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var row = await Project(db.Users.AsNoTracking().Where(u => u.StudentId == studentId)).SingleOrDefaultAsync(cancellationToken);
        return row?.ToRow(now);
    }

    public static AccountState StateOf(DateTimeOffset? disabledAt, DateTimeOffset? lockoutEnd, DateTimeOffset now) =>
        disabledAt is not null ? AccountState.Disabled : lockoutEnd > now ? AccountState.Locked : AccountState.Active;

    private IQueryable<AccountSource> Project(IQueryable<ApplicationUser> users) =>
        from u in users
        from s in db.Students.AsNoTracking().Where(s => s.Id == u.StudentId).DefaultIfEmpty()
        from l in db.Lecturers.AsNoTracking().Where(l => l.Id == u.LecturerId).DefaultIfEmpty()
        select new AccountSource
        {
            Id = u.Id,
            Username = u.UserName!,
            DisplayName = u.DisplayName,
            Role = (from ur in db.UserRoles where ur.UserId == u.Id join r in db.Roles on ur.RoleId equals r.Id select r.Name).FirstOrDefault(),
            StudentNumber = s == null ? null : s.StudentNumber,
            StaffNumber = l == null ? null : l.StaffNumber,
            Email = u.Email,
            DisabledAt = u.DisabledAt,
            LockoutEnd = u.LockoutEnd,
            MustChangePassword = u.MustChangePassword,
            MfaEnabled = u.TwoFactorEnabled,
            IsDemo = u.IsDemo,
            CreatedAt = u.CreatedAt,
            LastLoginAt = u.LastLoginAt,
            NormalizedUserName = u.NormalizedUserName,
        };

    private sealed class AccountSource
    {
        public Guid Id { get; init; }

        public string Username { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string? Role { get; init; }

        public string? StudentNumber { get; init; }

        public string? StaffNumber { get; init; }

        public string? Email { get; init; }

        public DateTimeOffset? DisabledAt { get; init; }

        public DateTimeOffset? LockoutEnd { get; init; }

        public bool MustChangePassword { get; init; }

        public bool MfaEnabled { get; init; }

        public bool IsDemo { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset? LastLoginAt { get; init; }

        public string? NormalizedUserName { get; init; }

        public AccountRow ToRow(DateTimeOffset now) => new(
            Id,
            Username,
            DisplayName,
            Role ?? string.Empty,
            StudentNumber,
            StaffNumber,
            Email,
            StateOf(DisabledAt, LockoutEnd, now),
            LockoutEnd,
            MustChangePassword,
            MfaEnabled,
            IsDemo,
            CreatedAt,
            LastLoginAt);
    }
}

/// <summary>One line of <c>GET /api/admin/students</c>.</summary>
public sealed record AdminStudentRow(
    Guid Id,
    string StudentNumber,
    string FullName,
    string Programme,
    int YearOfStudy,
    string? Email,
    DateTimeOffset? LeftAt,
    AccountState AccountState);

/// <summary>One grade of <c>AdminStudentView.grades[]</c>, whatever its status, with the student's visibility of it.</summary>
public sealed record AdminGradeRow(
    string ModuleCode,
    string ModuleTitle,
    int Credits,
    Semester Semester,
    string AcademicYear,
    GradeOutcome Outcome,
    int? Mark,
    GradeStatus Status,
    DateTimeOffset? PublishedAt,
    bool VisibleToStudent,
    int Version,
    DateTimeOffset? CorrectedAt);

/// <summary>Everything <c>AdminStudentView</c> shows.</summary>
public sealed record AdminStudentViewData(
    StudentProfileRow Student,
    AccountRow? Account,
    IReadOnlyList<StudentEnrolmentRow> Enrolments,
    IReadOnlyList<AdminGradeRow> Grades,
    double? WeightedAverage,
    string? Classification,
    IReadOnlyList<AuditEventRow> RecentAudit);

/// <summary>
/// The administrator's student reads (02-api.md section 8.5, 04-performance-and-ops.md section 3): the searchable
/// list with each student's account state, and the student view in five queries (student; account; enrolments ⋈
/// modules; grades ⋈ modules ⋈ enrolments; the last ten audit rows). <c>visibleToStudent</c> is evaluated with
/// <see cref="GradeQueries.VisibleToStudents"/> itself, the one predicate every student read uses, and the average is
/// computed from those grades only, as the student sees it.
/// </summary>
public sealed class AdminStudentQuery(RushDayDbContext db, AdminAccountQuery accounts)
{
    public const int RecentAuditCount = 10;

    public async Task<PagedResult<AdminStudentRow>> ListAsync(string? q, AccountState? accountState, PageRequest page, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var students = db.Students.AsNoTracking();
        if (SearchText.Normalise(q) is { } term)
        {
            var prefix = SearchText.Prefix(term);
            var fragment = SearchText.Fragment(term);
            students = students.Where(s => EF.Functions.ILike(s.StudentNumber, prefix, SearchText.EscapeCharacter)
                || EF.Functions.ILike(s.FullName, fragment, SearchText.EscapeCharacter));
        }

        var rows = from s in students
                   from u in db.Users.AsNoTracking().Where(u => u.StudentId == s.Id).DefaultIfEmpty()
                   select new { Student = s, UserId = u == null ? (Guid?)null : u.Id, DisabledAt = u == null ? null : u.DisabledAt, LockoutEnd = u == null ? null : u.LockoutEnd };

        rows = accountState switch
        {
            AccountState.None => rows.Where(r => r.UserId == null),
            AccountState.Disabled => rows.Where(r => r.UserId != null && r.DisabledAt != null),
            AccountState.Locked => rows.Where(r => r.UserId != null && r.DisabledAt == null && r.LockoutEnd > now),
            AccountState.Active => rows.Where(r => r.UserId != null && r.DisabledAt == null && (r.LockoutEnd == null || r.LockoutEnd <= now)),
            _ => rows,
        };

        var total = await rows.CountAsync(cancellationToken);
        var pageRows = await rows
            .OrderBy(r => r.Student.StudentNumber)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(r => new { r.Student.Id, r.Student.StudentNumber, r.Student.FullName, r.Student.Programme, r.Student.YearOfStudy, r.Student.Email, r.Student.LeftAt, r.UserId, r.DisabledAt, r.LockoutEnd })
            .ToListAsync(cancellationToken);

        return new PagedResult<AdminStudentRow>(
            [
                .. pageRows.Select(r => new AdminStudentRow(
                    r.Id,
                    r.StudentNumber,
                    r.FullName,
                    r.Programme,
                    r.YearOfStudy,
                    r.Email,
                    r.LeftAt,
                    r.UserId is null ? AccountState.None : AdminAccountQuery.StateOf(r.DisabledAt, r.LockoutEnd, now))),
            ],
            page.Page,
            page.PageSize,
            total);
    }

    /// <summary>One list line by student number (the create and update answers), or null.</summary>
    public async Task<AdminStudentRow?> RowAsync(string studentNumber, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var number = studentNumber.Trim().ToUpperInvariant();
        var row = await (
            from s in db.Students.AsNoTracking().Where(s => s.StudentNumber == number)
            from u in db.Users.AsNoTracking().Where(u => u.StudentId == s.Id).DefaultIfEmpty()
            select new { s.Id, s.StudentNumber, s.FullName, s.Programme, s.YearOfStudy, s.Email, s.LeftAt, UserId = u == null ? (Guid?)null : u.Id, DisabledAt = u == null ? null : u.DisabledAt, LockoutEnd = u == null ? null : u.LockoutEnd })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new AdminStudentRow(row.Id, row.StudentNumber, row.FullName, row.Programme, row.YearOfStudy, row.Email, row.LeftAt, row.UserId is null ? AccountState.None : AdminAccountQuery.StateOf(row.DisabledAt, row.LockoutEnd, now));
    }

    /// <summary>The student's id by number, or null.</summary>
    public Task<Guid?> IdOfAsync(string studentNumber, CancellationToken cancellationToken = default)
    {
        var number = studentNumber.Trim().ToUpperInvariant();
        return db.Students.AsNoTracking().Where(s => s.StudentNumber == number).Select(s => (Guid?)s.Id).SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>The student view; null when no student has the number. The caller writes <c>student.viewed</c>.</summary>
    public async Task<AdminStudentViewData?> ViewAsync(string studentNumber, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var number = studentNumber.Trim().ToUpperInvariant();

        // (1)
        var student = await db.Students.AsNoTracking()
            .Where(s => s.StudentNumber == number)
            .Select(s => new StudentProfileRow(s.Id, s.StudentNumber, s.FullName, s.Programme, s.YearOfStudy, s.Email, s.LeftAt))
            .SingleOrDefaultAsync(cancellationToken);
        if (student is null)
        {
            return null;
        }

        // (2)
        var account = await accounts.ForStudentAsync(student.Id, now, cancellationToken);

        // (3)
        var enrolments = await MyEnrolmentsQuery.RowsFor(db, student.Id, activeOnly: false).ToListAsync(cancellationToken);

        // (4) Every grade with its year and whether the student can see it, by the visibility rule itself.
        var visible = GradeQueries.VisibleToStudents(db, now);
        var grades = await (
            from g in db.Grades.AsNoTracking().Where(g => g.StudentId == student.Id)
            join e in db.Enrolments.AsNoTracking() on new { g.StudentId, g.ModuleId } equals new { e.StudentId, e.ModuleId }
            join m in db.Modules.AsNoTracking() on g.ModuleId equals m.Id
            orderby e.AcademicYear descending, m.Semester, m.Code
            select new AdminGradeRow(
                m.Code,
                m.Title,
                m.Credits,
                m.Semester,
                e.AcademicYear,
                g.Outcome,
                g.Mark,
                g.Status,
                g.PublishedAt,
                visible.Any(v => v.Id == g.Id),
                g.Version,
                g.CorrectedAt))
            .ToListAsync(cancellationToken);

        // (5)
        var recent = await AuditQuery.Project(db, db.AuditEvents.AsNoTracking().Where(a => a.StudentId == student.Id).OrderByDescending(a => a.OccurredAt).Take(RecentAuditCount))
            .ToListAsync(cancellationToken);

        var graded = Classification.Graded(grades.Where(g => g.VisibleToStudent).Select(g => (g.Outcome, g.Mark, g.Credits)));
        var average = Classification.WeightedAverage(graded);

        return new AdminStudentViewData(
            student,
            account,
            [.. enrolments.OrderByDescending(e => e.AcademicYear, StringComparer.Ordinal).ThenBy(e => e.Semester).ThenBy(e => e.ModuleCode, StringComparer.Ordinal)],
            grades,
            average,
            average is { } value ? Classification.FromAverage(value) : null,
            [.. recent.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)]);
    }
}
