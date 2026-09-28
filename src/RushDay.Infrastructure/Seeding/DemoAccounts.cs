namespace RushDay.Infrastructure.Seeding;

/// <summary>
/// The public demo credentials (01-domain-and-data.md section 7). Shown on the login page only when
/// <c>Demo:Enabled</c> is true; every student and lecturer gets a login with the role's shared password.
/// </summary>
public static class DemoAccounts
{
    public const string StudentUsername = "S000001";
    public const string StudentPassword = "Student-Demo-2026!";
    public const string StudentHint = "60 autumn credits, results publish 28 Sep 2026 at 10:00 (Europe/London), can enrol on CS3099";

    public const string LecturerUsername = "L00001";
    public const string LecturerPassword = "Lecturer-Demo-2026!";
    public const string LecturerHint = "Leads CS3099 (30 places) and other CS modules; enters and submits marks";

    public const string AdminUsername = "admin";
    public const string AdminPassword = "Admin-Demo-2026!";
    public const string AdminHint = "Windows, results publication, accounts, audit, ops";

    public const string AdminDisplayName = "Demo Administrator";
    public const string NonDemoAdminDisplayName = "Administrator";
}
