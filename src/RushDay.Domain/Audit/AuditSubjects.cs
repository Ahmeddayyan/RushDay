namespace RushDay.Domain.Audit;

/// <summary>The closed set of audit_events.subject_type values (01-domain-and-data.md section 3).</summary>
public static class AuditSubjects
{
    public const string Enrolment = "Enrolment";
    public const string Grade = "Grade";
    public const string Module = "Module";
    public const string Publication = "Publication";
    public const string Announcement = "Announcement";
    public const string Account = "Account";
    public const string Settings = "Settings";
    public const string Window = "Window";
    public const string Student = "Student";
    public const string Lecturer = "Lecturer";
    public const string System = "System";

    public static IReadOnlyList<string> All { get; } =
    [
        Enrolment, Grade, Module, Publication, Announcement, Account, Settings, Window, Student, Lecturer, System,
    ];
}
