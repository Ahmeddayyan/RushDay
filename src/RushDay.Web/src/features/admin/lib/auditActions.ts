/**
 * The audit action catalogue of 03-security.md section 7 (`Domain/Audit/AuditActions.cs`) with a
 * plain label for each, in the order the catalogue lists them. The audit log's action filter offers
 * exactly these; a row whose action is not listed (a newer server) shows its raw key.
 */
export const AUDIT_ACTIONS: readonly { action: string; label: string }[] = [
  { action: 'auth.locked_out', label: 'Locked out after failed sign-ins' },
  { action: 'auth.password_changed', label: 'Password changed' },
  { action: 'account.mfa_setup_started', label: 'Two-step verification setup started' },
  { action: 'account.mfa_enabled', label: 'Two-step verification turned on' },
  { action: 'enrolment.created', label: 'Enrolled' },
  { action: 'enrolment.withdrawn', label: 'Withdrew' },
  { action: 'enrolment.admin_created', label: 'Enrolled by an administrator' },
  { action: 'enrolment.admin_withdrawn', label: 'Withdrawn by an administrator' },
  { action: 'grade.entered', label: 'Mark entered' },
  { action: 'grade.changed', label: 'Mark changed' },
  { action: 'grade.corrected', label: 'Mark corrected' },
  { action: 'module.marks_submitted', label: 'Marks submitted' },
  { action: 'module.returned_to_draft', label: 'Returned to draft' },
  { action: 'results.published', label: 'Results published' },
  { action: 'results.rescheduled', label: 'Publication rescheduled' },
  { action: 'results.cancelled', label: 'Scheduled publication cancelled' },
  { action: 'results.unpublished', label: 'Results unpublished' },
  { action: 'announcement.created', label: 'Announcement posted' },
  { action: 'announcement.updated', label: 'Announcement edited' },
  { action: 'announcement.deleted', label: 'Announcement deleted' },
  { action: 'account.provisioned', label: 'Account provisioned' },
  { action: 'account.locked', label: 'Account locked' },
  { action: 'account.unlocked', label: 'Account unlocked' },
  { action: 'account.disabled', label: 'Account disabled' },
  { action: 'account.enabled', label: 'Account enabled' },
  { action: 'account.password_reset', label: 'Password reset' },
  { action: 'account.mfa_reset', label: 'Two-step verification reset' },
  { action: 'settings.changed', label: 'Settings changed' },
  { action: 'window.created', label: 'Enrolment window added' },
  { action: 'window.updated', label: 'Enrolment window changed' },
  { action: 'window.deleted', label: 'Enrolment window deleted' },
  { action: 'module.created', label: 'Module created' },
  { action: 'module.updated', label: 'Module edited' },
  { action: 'module.lecturers_set', label: 'Lecturers assigned' },
  { action: 'module.trimmed', label: 'Trimmed to capacity' },
  { action: 'student.created', label: 'Student created' },
  { action: 'student.updated', label: 'Student edited' },
  { action: 'student.left', label: 'Student marked as left' },
  { action: 'student.viewed', label: 'Student record viewed' },
  { action: 'student.exported', label: 'Student data exported' },
  { action: 'student.exported_self', label: 'Student downloaded their data' },
  { action: 'lecturer.created', label: 'Lecturer created' },
  { action: 'lecturer.updated', label: 'Lecturer edited' },
  { action: 'lecturer.left', label: 'Lecturer marked as left' },
  { action: 'audit.exported', label: 'Audit log exported' },
  { action: 'ops.reconciled', label: 'Places re-counted' },
  { action: 'system.demo_reset', label: 'Demo module reset' },
  { action: 'system.demo_accounts_disabled', label: 'Demo accounts disabled' },
]

const labels = new Map(AUDIT_ACTIONS.map((entry) => [entry.action, entry.label]))

export function auditActionLabel(action: string): string {
  return labels.get(action) ?? action
}

export function isAuditAction(action: string): boolean {
  return labels.has(action)
}
