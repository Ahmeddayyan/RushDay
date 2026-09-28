namespace RushDay.Domain.Audit;

/// <summary>The closed catalogue of audit actions (03-security.md section 7). Values are stored in audit_events.action.</summary>
public static class AuditActions
{
    public const string AuthLockedOut = "auth.locked_out";
    public const string AuthPasswordChanged = "auth.password_changed";

    public const string EnrolmentCreated = "enrolment.created";
    public const string EnrolmentWithdrawn = "enrolment.withdrawn";
    public const string EnrolmentAdminCreated = "enrolment.admin_created";
    public const string EnrolmentAdminWithdrawn = "enrolment.admin_withdrawn";

    public const string GradeEntered = "grade.entered";
    public const string GradeChanged = "grade.changed";

    public const string ModuleMarksSubmitted = "module.marks_submitted";
    public const string ModuleReturnedToDraft = "module.returned_to_draft";
    public const string ModuleCreated = "module.created";
    public const string ModuleUpdated = "module.updated";
    public const string ModuleLecturersSet = "module.lecturers_set";

    public const string ResultsPublished = "results.published";
    public const string ResultsRescheduled = "results.rescheduled";

    public const string AnnouncementCreated = "announcement.created";
    public const string AnnouncementUpdated = "announcement.updated";
    public const string AnnouncementDeleted = "announcement.deleted";

    public const string AccountProvisioned = "account.provisioned";
    public const string AccountLocked = "account.locked";
    public const string AccountUnlocked = "account.unlocked";
    public const string AccountDisabled = "account.disabled";
    public const string AccountEnabled = "account.enabled";
    public const string AccountPasswordReset = "account.password_reset";

    public const string SettingsChanged = "settings.changed";

    public const string WindowCreated = "window.created";
    public const string WindowUpdated = "window.updated";
    public const string WindowDeleted = "window.deleted";

    public const string StudentCreated = "student.created";
    public const string LecturerCreated = "lecturer.created";

    public const string OpsReconciled = "ops.reconciled";
}
