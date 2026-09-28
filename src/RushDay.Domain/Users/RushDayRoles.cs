namespace RushDay.Domain.Users;

/// <summary>The three roles. Every user holds exactly one; the ids are fixed so backfills and tests can reference them.</summary>
public static class RushDayRoles
{
    public const string Student = "Student";
    public const string Lecturer = "Lecturer";
    public const string Admin = "Admin";

    public static readonly Guid StudentId = new("00000000-0000-0000-0000-000000000001");
    public static readonly Guid LecturerId = new("00000000-0000-0000-0000-000000000002");
    public static readonly Guid AdminId = new("00000000-0000-0000-0000-000000000003");

    public static IReadOnlyList<string> All { get; } = [Student, Lecturer, Admin];

    public static Guid IdOf(string role) => role switch
    {
        Student => StudentId,
        Lecturer => LecturerId,
        Admin => AdminId,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role."),
    };
}
