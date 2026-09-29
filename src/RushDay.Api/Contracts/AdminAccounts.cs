using System.ComponentModel.DataAnnotations;
using RushDay.Domain.Users;
using RushDay.Infrastructure.Queries;

namespace RushDay.Api.Contracts;

/// <summary><c>AccountView</c> of 02-api.md section 7.</summary>
public sealed record AccountView(
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
    DateTimeOffset? LastLoginAt)
{
    public static AccountView From(AccountRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new AccountView(
            row.Id,
            row.Username,
            row.DisplayName,
            row.Role,
            row.StudentNumber,
            row.StaffNumber,
            row.Email,
            row.State,
            row.LockoutEnd,
            row.MustChangePassword,
            row.MfaEnabled,
            row.IsDemo,
            row.CreatedAt,
            row.LastLoginAt);
    }
}

/// <summary>A role name of the three, compared case-insensitively.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class RoleNameAttribute : ValidationAttribute
{
    public RoleNameAttribute()
        : base("The role must be Student, Lecturer or Admin.")
    {
    }

    public static string? Canonical(string? value) =>
        RushDayRoles.All.FirstOrDefault(r => string.Equals(r, value, StringComparison.OrdinalIgnoreCase));

    public override bool IsValid(object? value) => value is null || (value is string text && Canonical(text) is not null);
}

/// <summary>The query of <c>GET /api/admin/accounts</c>.</summary>
public sealed record AccountListParameters
{
    [StringLength(StaffPatterns.SearchMaxLength)]
    [NoNul]
    public string? Q { get; init; }

    [RoleName]
    public string? Role { get; init; }

    [RegularExpression("^(active|locked|disabled)$")]
    public string? State { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public AccountState? ParsedState() => State switch
    {
        "active" => AccountState.Active,
        "locked" => AccountState.Locked,
        "disabled" => AccountState.Disabled,
        _ => null,
    };
}

/// <summary><c>POST /api/admin/accounts</c>.</summary>
public sealed record ProvisionAccountBody
{
    [Required]
    [RegularExpression("^[A-Za-z0-9._-]{1,64}$")]
    public string? Username { get; init; }

    [Required]
    [StringLength(200, MinimumLength = 1)]
    [NoNul]
    public string? DisplayName { get; init; }

    [Required]
    [RoleName]
    public string? Role { get; init; }

    [RegularExpression(StaffPatterns.StudentNumber)]
    public string? StudentNumber { get; init; }

    [RegularExpression(StaffPatterns.StaffNumber)]
    public string? StaffNumber { get; init; }

    [EmailAddress]
    [StringLength(256)]
    [NoNul]
    public string? Email { get; init; }

    /// <summary>Checked against the password policy (400 <c>weak-password</c>); generated when omitted.</summary>
    [StringLength(128)]
    [NoNul]
    public string? TemporaryPassword { get; init; }
}

/// <summary>The account and its temporary password, shown once.</summary>
public sealed record ProvisionAccountResponse(AccountView Account, string TemporaryPassword);

/// <summary><c>POST /api/admin/accounts/{id}/reset-password</c> (the body is optional).</summary>
public sealed record ResetPasswordBody
{
    [StringLength(128)]
    [NoNul]
    public string? TemporaryPassword { get; init; }
}

public sealed record TemporaryPasswordResponse(string TemporaryPassword);
