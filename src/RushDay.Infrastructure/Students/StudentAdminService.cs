using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RushDay.Domain.Audit;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Students;
using RushDay.Infrastructure.Audit;
using RushDay.Infrastructure.Enrolments;
using RushDay.Infrastructure.Identity;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Students;

/// <summary>Why a student mutation was refused.</summary>
public enum StudentAdminError
{
    None,
    StudentNotFound,
    StudentNumberTaken,
    StudentLeft,
    DemoAccount,
}

/// <summary>A student record's editable fields.</summary>
public sealed record StudentChange(string FullName, string Programme, int YearOfStudy, string? Email);

public sealed record StudentLeaving(string StudentNumber, DateTimeOffset LeftAt, int Withdrawn);

public sealed record StudentAdminResult<T>(T? Value, StudentAdminError Error)
{
    public bool Succeeded => Error == StudentAdminError.None;

    public static StudentAdminResult<T> Success(T value) => new(value, StudentAdminError.None);

    public static StudentAdminResult<T> Fail(StudentAdminError error) => new(default, error);
}

/// <summary>
/// The registry's student records (02-api.md section 8.5): create, edit (the linked login's <c>display_name</c>
/// follows the name in the same transaction) and mark as left. Leaving withdraws every active enrolment of the current
/// year through <see cref="EnrolmentService.WithdrawAsync"/> (each with its own <c>enrolment.admin_withdrawn</c> row,
/// <c>left: true</c>), disables the linked account (<c>account.disabled</c>, security stamp rotated so its sessions
/// end) and audits <c>student.left</c>, all in one transaction. A demo account is read-only and a demo actor may not
/// disable a real one (02-api.md section 8.5), so a leave that would do either is refused as <c>demo-account</c>.
/// </summary>
public sealed class StudentAdminService(
    RushDayDbContext db,
    EnrolmentService enrolments,
    EnrolmentWindowService windows,
    UserManager<ApplicationUser> users,
    AuditWriter audit,
    IAuditContext actor,
    TimeProvider clock)
{
    public async Task<StudentAdminResult<string>> CreateAsync(string studentNumber, StudentChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(studentNumber);
        ArgumentNullException.ThrowIfNull(change);
        var number = studentNumber.Trim().ToUpperInvariant();

        if (await db.Students.AnyAsync(s => s.StudentNumber == number, cancellationToken))
        {
            return StudentAdminResult<string>.Fail(StudentAdminError.StudentNumberTaken);
        }

        var student = new Student
        {
            Id = Guid.CreateVersion7(),
            StudentNumber = number,
            FullName = change.FullName,
            Programme = change.Programme,
            YearOfStudy = change.YearOfStudy,
            Email = change.Email,
        };
        db.Students.Add(student);
        var entry = audit.Record(db, AuditActions.StudentCreated, AuditSubjects.Student, number, new { studentNumber = number, after = Snapshot(student) }, student.Id);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(student).State = EntityState.Detached;
            db.Entry(entry).State = EntityState.Detached;
            return StudentAdminResult<string>.Fail(StudentAdminError.StudentNumberTaken);
        }

        return StudentAdminResult<string>.Success(number);
    }

    public async Task<StudentAdminResult<string>> UpdateAsync(string studentNumber, StudentChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(studentNumber);
        ArgumentNullException.ThrowIfNull(change);
        var number = studentNumber.Trim().ToUpperInvariant();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var student = await db.Students.SingleOrDefaultAsync(s => s.StudentNumber == number, cancellationToken);
        if (student is null)
        {
            return StudentAdminResult<string>.Fail(StudentAdminError.StudentNotFound);
        }

        var before = Snapshot(student);
        student.FullName = change.FullName;
        student.Programme = change.Programme;
        student.YearOfStudy = change.YearOfStudy;
        student.Email = change.Email;

        audit.Record(db, AuditActions.StudentUpdated, AuditSubjects.Student, number, new { studentNumber = number, before, after = Snapshot(student) }, student.Id);
        await db.SaveChangesAsync(cancellationToken);

        // The name the top bar and the audit log show follows the record.
        await db.Users.Where(u => u.StudentId == student.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DisplayName, change.FullName), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return StudentAdminResult<string>.Success(number);
    }

    public async Task<StudentAdminResult<StudentLeaving>> LeaveAsync(string studentNumber, string reason, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(studentNumber);
        var number = studentNumber.Trim().ToUpperInvariant();

        var calendar = await windows.CurrentAsync(cancellationToken);
        var year = calendar.AcademicYear;
        var now = clock.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // The student row first (the enrolment lock order), so a concurrent self-enrolment waits for the leave.
        var locked = await db.Students.FromSql($"SELECT * FROM students WHERE student_number = {number} FOR UPDATE").ToListAsync(cancellationToken);
        var student = locked.SingleOrDefault();
        if (student is null)
        {
            return StudentAdminResult<StudentLeaving>.Fail(StudentAdminError.StudentNotFound);
        }

        if (student.LeftAt is not null)
        {
            return StudentAdminResult<StudentLeaving>.Fail(StudentAdminError.StudentLeft);
        }

        var account = await db.Users.SingleOrDefaultAsync(u => u.StudentId == student.Id, cancellationToken);
        if (account is not null && account.DisabledAt is null && (account.IsDemo || actor.ActorIsDemo))
        {
            return StudentAdminResult<StudentLeaving>.Fail(StudentAdminError.DemoAccount);
        }

        var withdrawn = await WithdrawForLeaveAsync(student.Id, year, reason, actorUserId, cancellationToken);

        student.LeftAt = now;
        audit.Record(db, AuditActions.StudentLeft, AuditSubjects.Student, number, new { studentNumber = number, reason }, student.Id);

        if (account is not null && account.DisabledAt is null)
        {
            account.DisabledAt = now;
            audit.Record(db, AuditActions.AccountDisabled, AuditSubjects.Account, account.Id.ToString(), new { username = account.UserName }, studentId: student.Id);

            // Saves the account and everything above; the rotated stamp ends the student's sessions at their next check.
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
        return StudentAdminResult<StudentLeaving>.Success(new StudentLeaving(number, now, withdrawn));
    }

    /// <summary>
    /// The one place a leave withdraws enrolments: every active enrolment of <paramref name="academicYear"/>, each an
    /// administrator's override withdrawal audited <c>enrolment.admin_withdrawn</c> with <c>left: true</c>, inside the
    /// caller's transaction, in module-code order. Returns how many were withdrawn.
    /// </summary>
    private async Task<int> WithdrawForLeaveAsync(Guid studentId, string academicYear, string reason, Guid actorUserId, CancellationToken cancellationToken)
    {
        var codes = await (
            from e in db.Enrolments.AsNoTracking()
            join m in db.Modules.AsNoTracking() on e.ModuleId equals m.Id
            where e.StudentId == studentId && e.Status == EnrolmentStatus.Active && e.AcademicYear == academicYear
            orderby m.Code
            select m.Code)
            .ToListAsync(cancellationToken);

        var withdrawn = 0;
        foreach (var code in codes)
        {
            var result = await enrolments.WithdrawAsync(studentId, code, actorUserId, new WithdrawOptions(Override: true, Reason: reason, Left: true), cancellationToken);
            if (result.Succeeded)
            {
                withdrawn++;
            }
        }

        return withdrawn;
    }

    private static object Snapshot(Student s) => new
    {
        fullName = s.FullName,
        programme = s.Programme,
        yearOfStudy = s.YearOfStudy,
        email = s.Email,
    };
}
