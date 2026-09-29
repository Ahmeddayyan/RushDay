using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RushDay.Domain.Audit;
using RushDay.Domain.Lecturers;
using RushDay.Infrastructure.Audit;
using RushDay.Infrastructure.Identity;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Queries;

namespace RushDay.Infrastructure.Lecturers;

/// <summary>Why a lecturer mutation was refused.</summary>
public enum LecturerAdminError
{
    None,
    LecturerNotFound,
    StaffNumberTaken,
    DemoAccount,
}

/// <summary>A lecturer record's editable fields.</summary>
public sealed record LecturerChange(string FullName, string Title, string Department, string? Email);

/// <summary>One line of <c>GET /api/admin/lecturers</c>.</summary>
public sealed record LecturerRow(
    string StaffNumber,
    string FullName,
    string Title,
    string Department,
    string? Email,
    DateTimeOffset? LeftAt,
    bool HasAccount,
    IReadOnlyList<string> ModuleCodes);

public sealed record LecturerAdminResult<T>(T? Value, LecturerAdminError Error)
{
    public bool Succeeded => Error == LecturerAdminError.None;

    public static LecturerAdminResult<T> Success(T value) => new(value, LecturerAdminError.None);

    public static LecturerAdminResult<T> Fail(LecturerAdminError error) => new(default, error);
}

/// <summary>
/// The registry's lecturer records (02-api.md section 8.5): list (staff-number prefix or name fragment), create, edit
/// (the linked login's <c>display_name</c> follows), and mark as left, which disables the linked account and keeps
/// every assignment (shown with <c>left: true</c>); leaving twice returns the record unchanged without an audit row.
/// As for students, a leave that would disable a demo account, or a real account by a demo actor, is
/// <c>demo-account</c>.
/// </summary>
public sealed class LecturerAdminService(
    RushDayDbContext db,
    UserManager<ApplicationUser> users,
    AuditWriter audit,
    IAuditContext actor,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<LecturerRow>> ListAsync(string? q, CancellationToken cancellationToken = default)
    {
        var lecturers = db.Lecturers.AsNoTracking();
        if (SearchText.Normalise(q) is { } term)
        {
            var prefix = SearchText.Prefix(term);
            var fragment = SearchText.Fragment(term);
            lecturers = lecturers.Where(l => EF.Functions.ILike(l.StaffNumber, prefix, SearchText.EscapeCharacter)
                || EF.Functions.ILike(l.FullName, fragment, SearchText.EscapeCharacter));
        }

        var rows = await lecturers.OrderBy(l => l.StaffNumber).ToListAsync(cancellationToken);
        return await RowsAsync(rows, cancellationToken);
    }

    /// <summary>One lecturer as the list shows them, or null.</summary>
    public async Task<LecturerRow?> RowAsync(string staffNumber, CancellationToken cancellationToken = default)
    {
        var number = Normalise(staffNumber);
        var lecturer = await db.Lecturers.AsNoTracking().SingleOrDefaultAsync(l => l.StaffNumber == number, cancellationToken);
        return lecturer is null ? null : (await RowsAsync([lecturer], cancellationToken))[0];
    }

    public async Task<LecturerAdminResult<string>> CreateAsync(string staffNumber, LecturerChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        var number = Normalise(staffNumber);

        if (await db.Lecturers.AnyAsync(l => l.StaffNumber == number, cancellationToken))
        {
            return LecturerAdminResult<string>.Fail(LecturerAdminError.StaffNumberTaken);
        }

        var lecturer = new Lecturer
        {
            Id = Guid.CreateVersion7(),
            StaffNumber = number,
            FullName = change.FullName,
            Title = change.Title,
            Department = change.Department,
            Email = change.Email,
        };
        db.Lecturers.Add(lecturer);
        var entry = audit.Record(db, AuditActions.LecturerCreated, AuditSubjects.Lecturer, number, new { staffNumber = number, after = Snapshot(lecturer) });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(lecturer).State = EntityState.Detached;
            db.Entry(entry).State = EntityState.Detached;
            return LecturerAdminResult<string>.Fail(LecturerAdminError.StaffNumberTaken);
        }

        return LecturerAdminResult<string>.Success(number);
    }

    public async Task<LecturerAdminResult<string>> UpdateAsync(string staffNumber, LecturerChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        var number = Normalise(staffNumber);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lecturer = await db.Lecturers.SingleOrDefaultAsync(l => l.StaffNumber == number, cancellationToken);
        if (lecturer is null)
        {
            return LecturerAdminResult<string>.Fail(LecturerAdminError.LecturerNotFound);
        }

        var before = Snapshot(lecturer);
        lecturer.FullName = change.FullName;
        lecturer.Title = change.Title;
        lecturer.Department = change.Department;
        lecturer.Email = change.Email;

        audit.Record(db, AuditActions.LecturerUpdated, AuditSubjects.Lecturer, number, new { staffNumber = number, before, after = Snapshot(lecturer) });
        await db.SaveChangesAsync(cancellationToken);
        await db.Users.Where(u => u.LecturerId == lecturer.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DisplayName, change.FullName), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return LecturerAdminResult<string>.Success(number);
    }

    public async Task<LecturerAdminResult<string>> LeaveAsync(string staffNumber, string reason, CancellationToken cancellationToken = default)
    {
        var number = Normalise(staffNumber);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var locked = await db.Lecturers.FromSql($"SELECT * FROM lecturers WHERE staff_number = {number} FOR UPDATE").ToListAsync(cancellationToken);
        var lecturer = locked.SingleOrDefault();
        if (lecturer is null)
        {
            return LecturerAdminResult<string>.Fail(LecturerAdminError.LecturerNotFound);
        }

        if (lecturer.LeftAt is not null)
        {
            // Idempotent: the record as it is, and no second audit row.
            return LecturerAdminResult<string>.Success(number);
        }

        var account = await db.Users.SingleOrDefaultAsync(u => u.LecturerId == lecturer.Id, cancellationToken);
        if (account is not null && account.DisabledAt is null && (account.IsDemo || actor.ActorIsDemo))
        {
            return LecturerAdminResult<string>.Fail(LecturerAdminError.DemoAccount);
        }

        var now = clock.GetUtcNow();
        lecturer.LeftAt = now;
        audit.Record(db, AuditActions.LecturerLeft, AuditSubjects.Lecturer, number, new { staffNumber = number, reason });

        if (account is not null && account.DisabledAt is null)
        {
            account.DisabledAt = now;
            audit.Record(db, AuditActions.AccountDisabled, AuditSubjects.Account, account.Id.ToString(), new { username = account.UserName });
            var saved = await users.UpdateSecurityStampAsync(account);
            if (!saved.Succeeded)
            {
                throw new InvalidOperationException($"Could not disable account {account.Id}: {string.Join(", ", saved.Errors.Select(e => e.Code))}");
            }
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return LecturerAdminResult<string>.Success(number);
    }

    private static string Normalise(string staffNumber)
    {
        ArgumentNullException.ThrowIfNull(staffNumber);
        return staffNumber.Trim().ToUpperInvariant();
    }

    private static object Snapshot(Lecturer l) => new
    {
        fullName = l.FullName,
        title = l.Title,
        department = l.Department,
        email = l.Email,
    };

    private async Task<IReadOnlyList<LecturerRow>> RowsAsync(IReadOnlyList<Lecturer> lecturers, CancellationToken cancellationToken)
    {
        if (lecturers.Count == 0)
        {
            return [];
        }

        var ids = lecturers.Select(l => l.Id).ToArray();
        var withAccount = await db.Users.AsNoTracking()
            .Where(u => u.LecturerId != null && ids.Contains(u.LecturerId.Value))
            .Select(u => u.LecturerId!.Value)
            .ToListAsync(cancellationToken);
        var accountSet = withAccount.ToHashSet();

        var assignments = await (
            from ml in db.ModuleLecturers.AsNoTracking()
            join m in db.Modules.AsNoTracking() on ml.ModuleId equals m.Id
            where ids.Contains(ml.LecturerId)
            select new { ml.LecturerId, m.Code })
            .ToListAsync(cancellationToken);
        var codes = assignments
            .GroupBy(a => a.LecturerId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)[.. g.Select(a => a.Code).Order(StringComparer.Ordinal)]);

        return
        [
            .. lecturers.Select(l => new LecturerRow(
                l.StaffNumber,
                l.FullName,
                l.Title,
                l.Department,
                l.Email,
                l.LeftAt,
                accountSet.Contains(l.Id),
                codes.TryGetValue(l.Id, out var assigned) ? assigned : [])),
        ];
    }
}
