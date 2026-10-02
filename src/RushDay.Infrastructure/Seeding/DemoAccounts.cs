namespace RushDay.Infrastructure.Seeding;

/// <summary>
/// The public demo credentials (01-domain-and-data.md section 7). Shown on the login page only when
/// <c>Demo:Enabled</c> is true; every student and lecturer gets a login with the role's shared password.
/// The hints are static and carry no date, so they are never stale; the login page renders the results line
/// from the public status endpoint instead.
/// </summary>
public static class DemoAccounts
{
    public const string StudentUsername = "S000001";
    public const string StudentPassword = "Student-Demo-2026!";
    public const string StudentHint = "Completed Autumn 2025/26 with marks published; enrolled on CS3001 for Autumn 2026/27; can enrol on CS3099";

    public const string LecturerUsername = "L00001";
    public const string LecturerPassword = "Lecturer-Demo-2026!";
    public const string LecturerHint = "Leads CS3001 (100 students, marks in draft) and CS3099 (30 places); enters and submits marks; the admin guide shows how to re-run results day";

    /// <summary>The demo administrator's username as shown on the login page; the backfill creates it under <c>StartupBackfillOptions.BootstrapAdminUsername</c>.</summary>
    public const string AdminUsername = "admin";
    public const string AdminPassword = "Admin-Demo-2026!";
    public const string AdminHint = "Windows, results publication and corrections, accounts, audit, operations";

    public const string AdminDisplayName = "Demo Administrator";
    public const string NonDemoAdminDisplayName = "Administrator";
}
