import type { GradeOutcome } from '@/api/types/common'

/** "68", "Absent", "Deferred" or "no mark": a grade's value in one word, for dialogs and toasts. */
export function describeMark(value: { outcome: GradeOutcome | null; mark: number | null }): string {
  if (value.outcome === 'absent') return 'Absent'
  if (value.outcome === 'deferred') return 'Deferred'
  return value.mark === null ? 'no mark' : String(value.mark)
}

/** One row of the lecturer assignment editor: the chosen lecturer (null while choosing) and the role. */
export interface AssignmentRow {
  key: number
  lecturer: { staffNumber: string; name: string; left: boolean } | null
  role: 'leader' | 'teacher'
}

/**
 * The rules of `PUT /api/admin/modules/{code}/lecturers` (02-api.md section 8.5), checked before
 * sending: exactly one leader, each lecturer once, nobody who has left, no empty rows.
 */
export function assignmentProblems(rows: readonly AssignmentRow[]): string[] {
  const problems: string[] = []
  if (rows.length === 0) problems.push('Assign at least the module leader.')
  if (rows.some((row) => row.lecturer === null)) problems.push('Choose a lecturer on every row.')
  const leaders = rows.filter((row) => row.role === 'leader').length
  if (rows.length > 0 && leaders !== 1) {
    problems.push(
      leaders === 0 ? 'Choose one leader.' : `Only one leader is allowed; ${leaders} are chosen.`,
    )
  }
  const numbers = rows.flatMap((row) => (row.lecturer ? [row.lecturer.staffNumber] : []))
  if (new Set(numbers).size !== numbers.length) problems.push('List each lecturer once.')
  if (rows.some((row) => row.lecturer?.left)) {
    problems.push("Remove lecturers who have left; they can't be assigned.")
  }
  return problems
}
