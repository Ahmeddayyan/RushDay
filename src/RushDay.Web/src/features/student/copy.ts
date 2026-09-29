import type {
  Lecturer,
  ModuleSummary,
  PublicationBrief,
  Semester,
  WindowInfo,
} from '@/api/types/common'
import { formatDateTime, formatSemester } from '@/lib/format'

/**
 * Sentences the student pages share (05-frontend.md section 10). Instants are formatted in the
 * institution's zone with the zone named, because windows and publication instants are where a
 * student in another zone could misread the time.
 */

/** "12 of 30 places left" */
export function placesLeftText(placesRemaining: number, capacity: number): string {
  return `${placesRemaining} of ${capacity} ${capacity === 1 ? 'place' : 'places'} left`
}

/** "12 places left" */
export function placesLeft(count: number): string {
  return `${count} ${count === 1 ? 'place' : 'places'} left`
}

/** "15 of 60 credits, Autumn 2026/27" */
export function creditBudgetText(
  used: number,
  limit: number,
  semester: Semester,
  academicYear?: string | null,
): string {
  const term = academicYear
    ? `${formatSemester(semester)} ${academicYear}`
    : formatSemester(semester)
  return `${used} of ${limit} credits, ${term}`
}

/** "Autumn 2025/26" */
export function termLabel(semester: Semester, academicYear: string): string {
  return `${formatSemester(semester)} ${academicYear}`
}

/**
 * "Autumn 2025/26 results publish 28 September 2026 at 10:00 (BST)" for a scheduled publication,
 * "… published …" for a live one.
 */
export function publicationSentence(publication: PublicationBrief, timeZone: string): string {
  const verb = publication.state === 'scheduled' ? 'publish' : 'published'
  return `${termLabel(publication.semester, publication.academicYear)} results ${verb} ${formatDateTime(publication.publishAt, timeZone)}`
}

/** The window fields a module carries (the same window for every module of a semester). */
export type ModuleWindow = Pick<
  ModuleSummary,
  'semester' | 'enrolmentState' | 'windowOpensAt' | 'windowClosesAt'
>

/**
 * Why a semester cannot be enrolled on right now, as the catalogue banner and the enrol button say
 * it; null while the window is open.
 */
export function windowClosedSentence(window: ModuleWindow, timeZone: string): string | null {
  const semester = formatSemester(window.semester)
  switch (window.enrolmentState) {
    case 'open':
      return null
    case 'notYetOpen':
      return window.windowOpensAt
        ? `Enrolment for ${semester} opens ${formatDateTime(window.windowOpensAt, timeZone)}.`
        : `Enrolment for ${semester} has not opened yet.`
    case 'closed':
      return window.windowClosesAt
        ? `Enrolment for ${semester} closed on ${formatDateTime(window.windowClosesAt, timeZone)}.`
        : `Enrolment for ${semester} is closed.`
    case 'noWindow':
      return `Enrolment dates for ${semester} have not been announced yet.`
  }
}

/** A `WindowInfo` (or its absence) in the shape modules carry. */
export function moduleWindowOf(semester: Semester, window: WindowInfo | undefined): ModuleWindow {
  return {
    semester,
    enrolmentState: window?.state ?? 'noWindow',
    windowOpensAt: window?.opensAt ?? null,
    windowClosesAt: window?.closesAt ?? null,
  }
}

/** "Dr Grace Hopper" */
export function lecturerName(lecturer: Pick<Lecturer, 'title' | 'fullName'>): string {
  return lecturer.title ? `${lecturer.title} ${lecturer.fullName}` : lecturer.fullName
}
