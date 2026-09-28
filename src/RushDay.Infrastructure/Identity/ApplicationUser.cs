using Microsoft.AspNetCore.Identity;

namespace RushDay.Infrastructure.Identity;

/// <summary>
/// The login record (table <c>users</c>). It lives in Infrastructure because the login store is a persistence
/// concern; the domain links to it only through <see cref="StudentId"/> and <see cref="LecturerId"/>.
/// A user has at most one of those (CHECK <c>ck_users_one_principal</c>) and exactly one role.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>Shown in the top bar and the audit log.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public Guid? StudentId { get; set; }

    public Guid? LecturerId { get; set; }

    /// <summary>Provisioned and reset accounts start true; demo accounts false.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>Created by the demo backfill with a public password.</summary>
    public bool IsDemo { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Disabled users cannot sign in; existing sessions die at the next security-stamp check.</summary>
    public DateTimeOffset? DisabledAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }
}
