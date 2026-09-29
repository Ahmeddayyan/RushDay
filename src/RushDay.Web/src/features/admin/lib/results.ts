import type { AdminResultsModule, PublishExclusion } from '@/api/types/admin'
import type { MarksStatusValue } from '@/api/types/common'

/**
 * What a publish would do now (02-api.md section 8.5 `publish` rules), computed from the progress
 * table so the dialog can preview it: publishable modules are `submitted` with no mark missing;
 * `draft` modules are excluded as not submitted and `submitted` ones with missing marks as such;
 * modules without students, already scheduled or published are not listed.
 */
export interface PublishPreview {
  modules: AdminResultsModule[]
  grades: number
  excluded: PublishExclusion[]
}

export function isPublishable(module: AdminResultsModule): boolean {
  return module.marks.status === 'submitted' && module.marks.missing === 0
}

export function publishPreview(modules: readonly AdminResultsModule[]): PublishPreview {
  const publishable = modules.filter(isPublishable)
  const excluded: PublishExclusion[] = []
  for (const module of modules) {
    if (module.marks.status === 'draft') {
      excluded.push({
        code: module.code,
        status: 'draft',
        reason: 'notSubmitted',
        marksMissing: module.marks.missing,
      })
    } else if (module.marks.status === 'submitted' && module.marks.missing > 0) {
      excluded.push({
        code: module.code,
        status: 'submitted',
        reason: 'marksMissing',
        marksMissing: module.marks.missing,
      })
    }
  }
  return {
    modules: publishable,
    grades: publishable.reduce((sum, module) => sum + module.marks.entered, 0),
    excluded,
  }
}

/** "not submitted" / "{k} marks missing" (05-frontend.md section 10). */
export function exclusionReason(exclusion: PublishExclusion): string {
  if (exclusion.reason === 'notSubmitted') return 'not submitted'
  return `${exclusion.marksMissing} ${exclusion.marksMissing === 1 ? 'mark' : 'marks'} missing`
}

/** Progress table order: what needs the registry first, modules without students last. */
const STATUS_ORDER: Record<MarksStatusValue, number> = {
  submitted: 0,
  draft: 1,
  scheduled: 2,
  published: 3,
  noStudents: 4,
}

export function sortProgress(modules: readonly AdminResultsModule[]): AdminResultsModule[] {
  return [...modules].sort(
    (a, b) =>
      STATUS_ORDER[a.marks.status] - STATUS_ORDER[b.marks.status] || a.code.localeCompare(b.code),
  )
}

/** "2026/27" and the three years before it, for the year picker. */
export function recentAcademicYears(current: string, count = 4): string[] {
  const start = Number(current.slice(0, 4))
  if (!Number.isFinite(start) || current.length !== 7) return current ? [current] : []
  return Array.from({ length: count }, (_, index) => {
    const year = start - index
    return `${year}/${String((year + 1) % 100).padStart(2, '0')}`
  })
}
