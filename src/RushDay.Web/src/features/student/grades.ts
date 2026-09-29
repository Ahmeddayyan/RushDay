import type { GradeOutcome, GradeResult } from '@/api/types/common'
import type { CompletedModule } from '@/api/types/student'

/**
 * Mark bands as students read them (05-frontend.md section 10, `/student/results`), matching
 * `RushDay.Domain.Grades.Classification`: 70 and above First, 60 to 69 2:1, 50 to 59 2:2, 40 to 49
 * Third, below 40 Fail. The server's `classification` and `band` strings ("Upper Second (2:1)") are
 * shown where a sentence has room; tables and captions use the short form.
 */

export type MarkBandName = 'First' | '2:1' | '2:2' | 'Third' | 'Fail'

export function markBand(mark: number): MarkBandName {
  if (mark >= 70) return 'First'
  if (mark >= 60) return '2:1'
  if (mark >= 50) return '2:2'
  if (mark >= 40) return 'Third'
  return 'Fail'
}

const shortForms: Record<string, MarkBandName> = {
  First: 'First',
  'Upper Second (2:1)': '2:1',
  'Lower Second (2:2)': '2:2',
  Third: 'Third',
  Fail: 'Fail',
}

/** "Upper Second (2:1)" → "2:1"; a label the SPA does not know is derived from the mark instead. */
export function shortBand(band: string | null, mark: number | null): string | null {
  if (band && band in shortForms) return shortForms[band] ?? band
  if (mark !== null) return markBand(mark)
  return band
}

/** Grades that count towards the average: `mark` outcomes only; absences and deferrals never count. */
export function gradedResults(results: readonly GradeResult[]): GradeResult[] {
  return results.filter((result) => result.outcome === 'mark' && result.mark !== null)
}

export function outcomeLabel(outcome: Exclude<GradeOutcome, 'mark'>): string {
  return outcome === 'absent' ? 'Absent' : 'Deferred'
}

/**
 * A completed module's result in words: "Mark 68 (2:1)", "Absent", "Deferred", or "Result not yet
 * published" while nothing is visible.
 */
export function completedResultText(
  completed: Pick<CompletedModule, 'outcome' | 'mark' | 'band'>,
): string {
  if (completed.outcome === 'absent' || completed.outcome === 'deferred') {
    return outcomeLabel(completed.outcome)
  }
  if (completed.outcome === 'mark' && completed.mark !== null) {
    const band = shortBand(completed.band, completed.mark)
    return band ? `Mark ${completed.mark} (${band})` : `Mark ${completed.mark}`
  }
  return 'Result not yet published'
}

/** "3 modules graded" */
export function gradedCountText(count: number): string {
  return `${count} ${count === 1 ? 'module' : 'modules'} graded`
}

/**
 * `Classification.WeightedAverage` over `Classification.Graded`: each mark weighted by its module's
 * credits, absences and deferrals left out; null when nothing is graded.
 */
export function weightedAverage(results: readonly GradeResult[]): number | null {
  let credits = 0
  let total = 0
  for (const result of gradedResults(results)) {
    credits += result.credits
    total += (result.mark ?? 0) * result.credits
  }
  return credits === 0 ? null : total / credits
}
