namespace RushDay.Domain.Audit;

/// <summary>The closed catalogue of audit actions (03-security.md section 7). Values are stored in audit_events.action.</summary>
public static class AuditActions
{
    public const string AuthLockedOut = "auth.locked_out";
    public const string AuthPasswordChanged = "auth.password_changed";

    public const string AccountMfaSetupStarted = "account.mfa_setup_started";
    public const string AccountMfaEnabled = "account.mfa_enabled";

    public const string EnrolmentCreated = "enrolment.created";
    public const string EnrolmentWithdrawn = "enrolment.withdrawn";
    public const string EnrolmentAdminCreated = "enrolment.admin_created";
    public const string EnrolmentAdminWithdrawn = "enrolment.admin_withdrawn";

    public const string GradeEntered = "grade.entered";
    public const string GradeChanged = "grade.changed";
    public const string GradeCorrected = "grade.corrected";

    public const string ModuleMarksSubmitted = "module.marks_submitted";
    public const string ModuleReturnedToDraft = "module.returned_to_draft";

    public const string ResultsPublished = "results.published";
    public const string ResultsRescheduled = "results.rescheduled";
    public const string ResultsCancelled = "results.cancelled";
    public const string ResultsUnpublished = "results.unpublished";

    public const string AnnouncementCreated = "announcement.created";
    public const string AnnouncementUpdated = "announcement.updated";
    public const string AnnouncementDeleted = "announcement.deleted";

    public const string AccountProvisioned = "account.provisioned";
    public const string AccountLocked = "account.locked";
    public const string AccountUnlocked = "account.unlocked";
    public const string AccountDisabled = "account.disabled";
    public const string AccountEnabled = "account.enabled";
    public const string AccountPasswordReset = "account.password_reset";
    public const string AccountMfaReset = "account.mfa_reset";

    public const string SettingsChanged = "settings.changed";

    public const string WindowCreated = "window.created";
    public const string WindowUpdated = "window.updated";
    public const string WindowDeleted = "window.deleted";

    public const string ModuleCreated = "module.created";
    public const string ModuleUpdated = "module.updated";
    public const string ModuleLecturersSet = "module.lecturers_set";
    public const string ModuleTrimmed = "module.trimmed";

    public const string StudentCreated = "student.created";
    public const string StudentUpdated = "student.updated";
    public const string StudentLeft = "student.left";
    public const string StudentViewed = "student.viewed";
    public const string StudentExported = "student.exported";
    public const string StudentExportedSelf = "student.exported_self";

    public const string LecturerCreated = "lecturer.created";
    public const string LecturerUpdated = "lecturer.updated";
    public const string LecturerLeft = "lecturer.left";

    public const string AuditExported = "audit.exported";
    public const string OpsReconciled = "ops.reconciled";
    public const string SystemDemoReset = "system.demo_reset";
    public const string SystemDemoAccountsDisabled = "system.demo_accounts_disabled";

    /// <summary>The single mapping from an action to its <see cref="AuditSubjects"/> member, by the action's prefix.</summary>
    public static string SubjectOf(string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        var dot = action.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown audit action.");
        }

        return action[..dot] switch
        {
            "auth" or "account" => AuditSubjects.Account,
            "enrolment" => AuditSubjects.Enrolment,
            "grade" => AuditSubjects.Grade,
            "module" => AuditSubjects.Module,
            "results" => AuditSubjects.Publication,
            "announcement" => AuditSubjects.Announcement,
            "settings" => AuditSubjects.Settings,
            "window" => AuditSubjects.Window,
            "student" => AuditSubjects.Student,
            "lecturer" => AuditSubjects.Lecturer,
            "audit" or "ops" or "system" => AuditSubjects.System,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown audit action."),
        };
    }
}
