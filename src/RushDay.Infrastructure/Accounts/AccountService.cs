using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Audit;
using RushDay.Domain.Users;
using RushDay.Infrastructure.Audit;
using RushDay.Infrastructure.Identity;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Accounts;

/// <summary>Why an account operation was refused; the API maps each to its ProblemDetails slug (02-api.md section 6).</summary>
public enum AccountError
{
    None,
    AccountNotFound,
    StudentNotFound,
    LecturerNotFound,
    DemoAccount,
    SelfLockout,
    UsernameTaken,
    PrincipalHasAccount,
    RolePrincipalMismatch,
    WeakPassword,
    InvalidCurrentPassword,
}

/// <summary>The outcome of an account operation: a value, or an error with optional policy codes (for <c>weak-password</c>).</summary>
public sealed record AccountResult<T>(T? Value, AccountError Error, IReadOnlyList<string> Codes)
{
    public bool Succeeded => Error == AccountError.None;

    public static AccountResult<T> Success(T value) => new(value, AccountError.None, []);

    public static AccountResult<T> Fail(AccountError error, IReadOnlyList<string>? codes = null) => new(default, error, codes ?? []);
}

public sealed record ProvisionAccountRequest(
    string Username,
    string DisplayName,
    string Role,
    string? StudentNumber = null,
    string? StaffNumber = null,
    string? Email = null,
    string? TemporaryPassword = null);

/// <summary>A provisioned account and the temporary password, which is shown once and never stored in clear.</summary>
public sealed record ProvisionedAccount(ApplicationUser User, string TemporaryPassword);

/// <summary>
/// Provisioning and the account lifecycle (02-api.md sections 2.3 and 8.5): provision, lock, unlock, disable, enable,
/// reset password, reset the second factor and change password. Every mutation of an <c>is_demo</c> account is
/// refused (<c>demo-account</c>); lock, disable, reset and reset-MFA rotate the security stamp so existing sessions end
/// at their next validation; each change writes its audit row in the same transaction.
/// </summary>
public sealed class AccountService(
    RushDayDbContext db,
    UserManager<ApplicationUser> users,
    AuditWriter audit,
    IAuditContext actor,
    TimeProvider clock)
{
    /// <summary>What an administrator's "lock" sets <c>lockout_end</c> to (01-domain-and-data.md section 2).</summary>
    public static readonly DateTimeOffset IndefiniteLockout = new(9999, 12, 31, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The <c>errors.newPassword</c> code for a new password equal to the current one.</summary>
    public const string SameAsCurrentCode = "same-as-current";

    public const int TemporaryPasswordLength = 16;

    /// <summary>A-Z, a-z and 2-9 without I, l and O (01-domain-and-data.md section 8): nothing that reads as another character.</summary>
    public const string TemporaryPasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    public static string GenerateTemporaryPassword()
    {
        while (true)
        {
            var password = RandomNumberGenerator.GetString(TemporaryPasswordAlphabet, TemporaryPasswordLength);

            // 16 characters from 57 almost always pass; redraw in the vanishing case that one does not (4 distinct, blocklist).
            if (RushDayPasswordValidator.Check(string.Empty, password).Count == 0)
            {
                return password;
            }
        }
    }

    public async Task<AccountResult<ProvisionedAccount>> ProvisionAsync(ProvisionAccountRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var role = RushDayRoles.All.FirstOrDefault(r => string.Equals(r, request.Role, StringComparison.OrdinalIgnoreCase));
        var needsStudent = role == RushDayRoles.Student;
        var needsLecturer = role == RushDayRoles.Lecturer;
        if (role is null
            || needsStudent != !string.IsNullOrWhiteSpace(request.StudentNumber)
            || needsLecturer != !string.IsNullOrWhiteSpace(request.StaffNumber))
        {
            return AccountResult<ProvisionedAccount>.Fail(AccountError.RolePrincipalMismatch);
        }

        Guid? studentId = null;
        Guid? lecturerId = null;
        if (needsStudent)
        {
            var number = request.StudentNumber!.Trim().ToUpperInvariant();
            studentId = await db.Students.AsNoTracking().Where(s => s.StudentNumber == number).Select(s => (Guid?)s.Id).SingleOrDefaultAsync(cancellationToken);
            if (studentId is null)
            {
                return AccountResult<ProvisionedAccount>.Fail(AccountError.StudentNotFound);
            }

            if (await db.Users.AnyAsync(u => u.StudentId == studentId, cancellationToken))
            {
                return AccountResult<ProvisionedAccount>.Fail(AccountError.PrincipalHasAccount);
            }
        }

        if (needsLecturer)
        {
            var number = request.StaffNumber!.Trim().ToUpperInvariant();
            lecturerId = await db.Lecturers.AsNoTracking().Where(l => l.StaffNumber == number).Select(l => (Guid?)l.Id).SingleOrDefaultAsync(cancellationToken);
            if (lecturerId is null)
            {
                return AccountResult<ProvisionedAccount>.Fail(AccountError.LecturerNotFound);
            }

            if (await db.Users.AnyAsync(u => u.LecturerId == lecturerId, cancellationToken))
            {
                return AccountResult<ProvisionedAccount>.Fail(AccountError.PrincipalHasAccount);
            }
        }

        if (await users.FindByNameAsync(request.Username) is not null)
        {
            return AccountResult<ProvisionedAccount>.Fail(AccountError.UsernameTaken);
        }

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = request.Username,
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            DisplayName = request.DisplayName,
            StudentId = studentId,
            LecturerId = lecturerId,
            MustChangePassword = true,
            IsDemo = false,
            LockoutEnabled = true,
            CreatedAt = clock.GetUtcNow(),
        };

        var password = request.TemporaryPassword;
        if (!string.IsNullOrEmpty(password))
        {
            var codes = await ValidatePasswordAsync(user, password);
            if (codes.Count > 0)
            {
                return AccountResult<ProvisionedAccount>.Fail(AccountError.WeakPassword, codes);
            }
        }
        else
        {
            password = GenerateTemporaryPassword();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            var codes = created.Errors.Select(e => e.Code).ToArray();
            return codes.Any(c => c is "DuplicateUserName")
                ? AccountResult<ProvisionedAccount>.Fail(AccountError.UsernameTaken)
                : AccountResult<ProvisionedAccount>.Fail(AccountError.WeakPassword, codes);
        }

        await users.AddToRoleAsync(user, role);
        audit.Record(
            db,
            AuditActions.AccountProvisioned,
            AuditSubjects.Account,
            user.Id.ToString(),
            new { username = user.UserName, role, studentNumber = needsStudent ? request.StudentNumber!.Trim().ToUpperInvariant() : null, staffNumber = needsLecturer ? request.StaffNumber!.Trim().ToUpperInvariant() : null },
            studentId: studentId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return AccountResult<ProvisionedAccount>.Success(new ProvisionedAccount(user, password));
    }

    public Task<AccountResult<ApplicationUser>> LockAsync(Guid userId, CancellationToken cancellationToken = default) =>
        MutateAsync(userId, AuditActions.AccountLocked, forbidSelf: true, rotateStamp: true, user =>
        {
            user.LockoutEnd = IndefiniteLockout;
            return Task.FromResult<IReadOnlyList<string>>([]);
        }, cancellationToken);

    public Task<AccountResult<ApplicationUser>> UnlockAsync(Guid userId, CancellationToken cancellationToken = default) =>
        MutateAsync(userId, AuditActions.AccountUnlocked, forbidSelf: false, rotateStamp: false, user =>
        {
            user.LockoutEnd = null;
            user.AccessFailedCount = 0;
            return Task.FromResult<IReadOnlyList<string>>([]);
        }, cancellationToken);

    public Task<AccountResult<ApplicationUser>> DisableAsync(Guid userId, CancellationToken cancellationToken = default) =>
        MutateAsync(userId, AuditActions.AccountDisabled, forbidSelf: true, rotateStamp: true, user =>
        {
            user.DisabledAt ??= clock.GetUtcNow();
            return Task.FromResult<IReadOnlyList<string>>([]);
        }, cancellationToken);

    public Task<AccountResult<ApplicationUser>> EnableAsync(Guid userId, CancellationToken cancellationToken = default) =>
        MutateAsync(userId, AuditActions.AccountEnabled, forbidSelf: false, rotateStamp: false, user =>
        {
            user.DisabledAt = null;
            return Task.FromResult<IReadOnlyList<string>>([]);
        }, cancellationToken);

    /// <summary>Sets a new temporary password (generated when omitted) and forces a change at the next sign-in.</summary>
    public async Task<AccountResult<string>> ResetPasswordAsync(Guid userId, string? temporaryPassword, CancellationToken cancellationToken = default)
    {
        var password = string.IsNullOrEmpty(temporaryPassword) ? GenerateTemporaryPassword() : temporaryPassword;
        var result = await MutateAsync(userId, AuditActions.AccountPasswordReset, forbidSelf: false, rotateStamp: true, async user =>
        {
            var codes = await ValidatePasswordAsync(user, password);
            if (codes.Count == 0)
            {
                user.PasswordHash = users.PasswordHasher.HashPassword(user, password);
                user.MustChangePassword = true;
            }

            return codes;
        }, cancellationToken);

        return result.Succeeded
            ? AccountResult<string>.Success(password)
            : AccountResult<string>.Fail(result.Error, result.Codes);
    }

    /// <summary>Turns the second factor off and discards the authenticator key; the user is gated again after signing in.</summary>
    public Task<AccountResult<ApplicationUser>> ResetMfaAsync(Guid userId, CancellationToken cancellationToken = default) =>
        MutateAsync(userId, AuditActions.AccountMfaReset, forbidSelf: false, rotateStamp: true, async user =>
        {
            user.TwoFactorEnabled = false;

            // A fresh key: the old one (still in the user's authenticator app) no longer verifies.
            var reset = await users.ResetAuthenticatorKeyAsync(user);
            return reset.Succeeded ? [] : [.. reset.Errors.Select(e => e.Code)];
        }, cancellationToken);

    /// <summary>
    /// The caller's own password change (02-api.md section 2.3). A wrong current password counts toward lockout; the
    /// security stamp rotates, so every other session ends at its next validation and the caller re-issues its own.
    /// </summary>
    public async Task<AccountResult<ApplicationUser>> ChangePasswordAsync(ApplicationUser user, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.IsDemo)
        {
            return AccountResult<ApplicationUser>.Fail(AccountError.DemoAccount);
        }

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            return AccountResult<ApplicationUser>.Fail(AccountError.WeakPassword, [SameAsCurrentCode]);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var changed = await users.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!changed.Succeeded)
        {
            if (changed.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
            {
                await users.AccessFailedAsync(user);
                await transaction.CommitAsync(cancellationToken);
                return AccountResult<ApplicationUser>.Fail(AccountError.InvalidCurrentPassword);
            }

            return AccountResult<ApplicationUser>.Fail(AccountError.WeakPassword, [.. changed.Errors.Select(e => e.Code)]);
        }

        var forced = user.MustChangePassword;
        user.MustChangePassword = false;
        audit.Record(db, AuditActions.AuthPasswordChanged, AuditSubjects.Account, user.Id.ToString(), new { forced }, studentId: user.StudentId);
        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            throw new InvalidOperationException($"Could not update the account after a password change: {string.Join(", ", updated.Errors.Select(e => e.Code))}");
        }

        await transaction.CommitAsync(cancellationToken);
        return AccountResult<ApplicationUser>.Success(user);
    }

    /// <summary>The failing codes of the whole password policy for <paramref name="user"/>, empty when acceptable.</summary>
    public async Task<IReadOnlyList<string>> ValidatePasswordAsync(ApplicationUser user, string password)
    {
        var codes = new List<string>();
        if (password.Length > RushDayPasswordValidator.MaximumLength)
        {
            codes.Add(RushDayPasswordValidator.TooLongCode);
        }

        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, user, password);
            codes.AddRange(result.Errors.Select(e => e.Code));
        }

        return codes;
    }

    private async Task<AccountResult<ApplicationUser>> MutateAsync(
        Guid userId,
        string action,
        bool forbidSelf,
        bool rotateStamp,
        Func<ApplicationUser, Task<IReadOnlyList<string>>> change,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return AccountResult<ApplicationUser>.Fail(AccountError.AccountNotFound);
        }

        if (user.IsDemo)
        {
            return AccountResult<ApplicationUser>.Fail(AccountError.DemoAccount);
        }

        if (forbidSelf && actor.ActorUserId == user.Id)
        {
            return AccountResult<ApplicationUser>.Fail(AccountError.SelfLockout);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var codes = await change(user);
        if (codes.Count > 0)
        {
            return AccountResult<ApplicationUser>.Fail(AccountError.WeakPassword, codes);
        }

        audit.Record(db, action, AuditSubjects.Account, user.Id.ToString(), new { username = user.UserName }, studentId: user.StudentId);

        // UpdateSecurityStampAsync saves the user (and the audit row) itself; otherwise UpdateAsync does.
        var saved = rotateStamp ? await users.UpdateSecurityStampAsync(user) : await users.UpdateAsync(user);
        if (!saved.Succeeded)
        {
            throw new InvalidOperationException($"Could not save account {user.Id}: {string.Join(", ", saved.Errors.Select(e => e.Code))}");
        }

        await transaction.CommitAsync(cancellationToken);
        return AccountResult<ApplicationUser>.Success(user);
    }
}
