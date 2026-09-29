import type { Semester } from './types/common'
import type { PublicStatus, SupportInfo } from './types/public'
import { ApiError } from './client'
import { queryKeys } from './keys'
import { queryClient } from './queryClient'
import {
  capitalise,
  DEFAULT_TIME_ZONE,
  formatDateTime,
  formatSemester,
  supportText,
} from '@/lib/format'

/**
 * ProblemDetails → user-facing copy (05-frontend.md section 6.4). `describeProblem` resolves by slug
 * first, then by status, then the generic 4xx/5xx rows. Every slug of the closed catalogue of
 * 02-api.md section 6 has a row here (`problem.test.ts` iterates `PROBLEM_CATALOGUE`).
 *
 * Placeholders the problem itself cannot supply (the module code, the leader's name, ...) come
 * from the caller through `ProblemContext`; `{support}` and the time zone default to the cached
 * `['public','status']`.
 */

export interface ProblemDescription {
  title: string
  message: string
  action?: 'retry' | 'login' | 'wait'
  /** For `wait`: how long to keep Retry disabled, from `Retry-After`. */
  retryAfterSeconds?: number
}

export interface ProblemContext {
  /** Module code the request was about, for "{code} is full." */
  code?: string
  /** Module leader's display name, for `not-module-leader`. */
  leader?: string
  /** The value that is already taken (module code, student or staff number). */
  value?: string
  /** The student or staff number of `principal-has-account`. */
  number?: string
  semester?: Semester
  academicYear?: string
  /** `module-locked` reads differently for a lecturer and for the registry. */
  audience?: 'lecturer' | 'admin'
  /** `student-left` about the signed-in student's own record. */
  ownSession?: boolean
  support?: SupportInfo | null
  timeZone?: string
}

/** The closed slug catalogue of 02-api.md section 6 with each slug's status (62 slugs). */
export const PROBLEM_CATALOGUE = {
  validation: 400,
  antiforgery: 400,
  'invalid-current-password': 400,
  'weak-password': 400,
  'invalid-mfa-code': 400,
  unauthenticated: 401,
  'invalid-credentials': 401,
  forbidden: 403,
  'not-your-module': 403,
  'not-module-leader': 403,
  'password-change-required': 403,
  'mfa-setup-required': 403,
  'not-found': 404,
  'module-not-found': 404,
  'student-not-found': 404,
  'lecturer-not-found': 404,
  'account-not-found': 404,
  'window-not-found': 404,
  'publication-not-found': 404,
  'announcement-not-found': 404,
  'grade-not-found': 404,
  'not-enrolled': 404,
  'method-not-allowed': 405,
  'already-enrolled': 409,
  'module-full': 409,
  'module-inactive': 409,
  'enrolment-window-closed': 409,
  'withdrawal-deadline-passed': 409,
  'results-exist': 409,
  'student-left': 409,
  'module-locked': 409,
  'module-not-submitted': 409,
  'already-submitted': 409,
  'nothing-to-submit': 409,
  'stale-mark': 409,
  'nothing-to-publish': 409,
  'publication-live': 409,
  'publication-scheduled': 409,
  'username-taken': 409,
  'principal-has-account': 409,
  'module-code-taken': 409,
  'student-number-taken': 409,
  'staff-number-taken': 409,
  'window-exists': 409,
  'demo-account': 409,
  'mfa-already-enabled': 409,
  'payload-too-large': 413,
  'unsupported-media-type': 415,
  'credit-limit-exceeded': 422,
  'marks-incomplete': 422,
  'not-enrolled-students': 422,
  'capacity-below-enrolled': 422,
  'semester-change-with-enrolments': 422,
  'publish-too-far-ahead': 422,
  'invalid-lecturer-assignment': 422,
  'window-dates-invalid': 422,
  'self-lockout': 422,
  'role-principal-mismatch': 422,
  'rate-limited': 429,
  'internal-error': 500,
  'server-busy': 503,
  timeout: 503,
} as const

export type ProblemSlug = keyof typeof PROBLEM_CATALOGUE

export function isProblemSlug(value: string | undefined): value is ProblemSlug {
  return value !== undefined && Object.hasOwn(PROBLEM_CATALOGUE, value)
}

/** True when `error` is an ApiError whose slug is one of `slugs`. */
export function isProblem(error: unknown, ...slugs: ProblemSlug[]): error is ApiError {
  return (
    error instanceof ApiError &&
    error.kind !== undefined &&
    (slugs as string[]).includes(error.kind)
  )
}

// ---------------------------------------------------------------------------------------------
// Password policy codes (RushDayPasswordValidator and Identity) → sentences.

const passwordReasons: Record<string, string> = {
  'same-as-current': 'Choose a password different from your current one.',
  PasswordTooShort: 'Use at least 12 characters.',
  PasswordTooLong: 'Use 128 characters or fewer.',
  PasswordRequiresUniqueChars: 'Use at least 4 different characters.',
  PasswordContainsUsername: "Don't include your username.",
  PasswordContainsProductName: 'Don\'t include the word "rushday".',
  PasswordBlocked: 'That password appears in lists of breached passwords. Choose another.',
}

/** The policy failures of a `weak-password` problem as sentences (`errors.newPassword`). */
export function weakPasswordReasons(error: unknown): string[] {
  const codes = error instanceof ApiError ? (error.problem?.errors?.newPassword ?? []) : []
  const reasons = codes.map((code) => passwordReasons[code] ?? 'Choose a stronger password.')
  return reasons.length > 0 ? [...new Set(reasons)] : ['Choose a stronger password.']
}

/** Field errors of a `validation` problem, keyed by camelCase property. */
export function fieldErrors(error: unknown): Record<string, string[]> {
  if (!(error instanceof ApiError) || error.kind !== 'validation') return {}
  return error.problem?.errors ?? {}
}

/**
 * Splits a validation problem into the fields a form knows (for `setError`) and the rest (for
 * `FormError`), as 05-frontend.md section 9.3 asks: server `errors` map by camelCase key.
 */
export function mapFieldErrors<K extends string>(
  error: unknown,
  knownFields: readonly K[],
): { fields: Partial<Record<K, string>>; other: string[] } {
  const fields: Partial<Record<K, string>> = {}
  const other: string[] = []
  for (const [key, messages] of Object.entries(fieldErrors(error))) {
    const camel = key.charAt(0).toLowerCase() + key.slice(1)
    const message = messages.join(' ')
    if ((knownFields as readonly string[]).includes(camel)) fields[camel as K] = message
    else if (message) other.push(message)
  }
  return { fields, other }
}

// ---------------------------------------------------------------------------------------------

interface Parts {
  error: ApiError
  ext: Record<string, unknown>
  ctx: ProblemContext
  support: string
  zone: string
  traceRef: string
}

type Row = (parts: Parts) => Omit<ProblemDescription, 'title'> & { title?: string }

function str(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined
}

function num(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined
}

function list(value: unknown): string[] {
  return Array.isArray(value)
    ? value.filter((item): item is string => typeof item === 'string')
    : []
}

function semesterOf(p: Parts): string {
  const semester = str(p.ext.semester) ?? p.ctx.semester
  return semester === 'autumn' || semester === 'spring' ? formatSemester(semester) : 'this semester'
}

function codeOr(p: Parts, fallback: string): string {
  return p.ctx.code ?? fallback
}

function plural(n: number, one: string, many: string): string {
  return `${n} ${n === 1 ? one : many}`
}

const somethingWrong: Row = (p) => ({
  message: `Something went wrong on our side. Reference ${p.traceRef}.`,
  action: 'retry',
})
const notFound: Row = () => ({ message: "That page or record doesn't exist." })
const busy: Row = (p) => ({
  message: 'The portal is very busy right now. Try again in a moment.',
  action: 'wait',
  retryAfterSeconds: p.error.retryAfterSeconds ?? 2,
})

/** One row per slug of 02-api.md section 6, copy exactly as 05-frontend.md section 6.4. */
export const PROBLEM_COPY: Record<ProblemSlug, Row> = {
  validation: (p) => {
    const messages = Object.values(p.error.problem?.errors ?? {}).flat()
    return {
      message:
        messages.length > 0 ? messages.join(' ') : 'Check the highlighted fields and try again.',
    }
  },
  antiforgery: () => ({
    message: 'Your page is out of date. Reload and try again.',
    action: 'retry',
  }),
  'invalid-current-password': () => ({ message: 'Your current password is incorrect.' }),
  'weak-password': (p) => ({ message: weakPasswordReasons(p.error).join(' ') }),
  'invalid-mfa-code': () => ({
    message: "That code didn't work. Check the time on your phone and try the newest code.",
  }),
  unauthenticated: () => ({ message: 'Your session has ended. Sign in again.', action: 'login' }),
  'invalid-credentials': () => ({ message: 'Incorrect username or password.' }),
  forbidden: () => ({ message: "You don't have access to this page." }),
  'not-your-module': () => ({ message: "This module isn't assigned to you." }),
  'not-module-leader': (p) => ({
    message: p.ctx.leader
      ? `Only the module leader, ${p.ctx.leader}, can submit these marks.`
      : 'Only the module leader can submit these marks.',
  }),
  'password-change-required': () => ({ message: 'Choose a new password to continue.' }),
  'mfa-setup-required': () => ({ message: 'Set up two-step verification to continue.' }),
  'not-found': notFound,
  'module-not-found': notFound,
  'student-not-found': notFound,
  'lecturer-not-found': notFound,
  'account-not-found': notFound,
  'window-not-found': notFound,
  'publication-not-found': notFound,
  'announcement-not-found': notFound,
  'grade-not-found': notFound,
  'not-enrolled': (p) => ({ message: `You're not enrolled on ${codeOr(p, 'this module')}.` }),
  'method-not-allowed': somethingWrong,
  'unsupported-media-type': somethingWrong,
  'payload-too-large': () => ({
    message: "That's too much to send at once. Save fewer changes and try again.",
  }),
  'already-enrolled': (p) => ({
    message: `You're already enrolled on ${codeOr(p, 'this module')}.`,
  }),
  'module-full': (p) => ({ message: `${codeOr(p, 'This module')} is full.` }),
  'module-inactive': (p) => ({ message: `${codeOr(p, 'This module')} is no longer running.` }),
  'enrolment-window-closed': (p) => {
    const semester = semesterOf(p)
    const opensAt = str(p.ext.opensAt)
    const closesAt = str(p.ext.closesAt)
    const now = Date.now()
    if (opensAt && Date.parse(opensAt) > now) {
      return { message: `Enrolment for ${semester} opens ${formatDateTime(opensAt, p.zone)}.` }
    }
    if (closesAt && Date.parse(closesAt) <= now) {
      return { message: `Enrolment for ${semester} closed on ${formatDateTime(closesAt, p.zone)}.` }
    }
    if (!opensAt && !closesAt) {
      return { message: `Enrolment dates for ${semester} have not been announced yet.` }
    }
    return { message: `Enrolment for ${semester} is closed.` }
  },
  'withdrawal-deadline-passed': (p) => {
    const deadline = str(p.ext.withdrawalDeadlineAt)
    const when = deadline ? formatDateTime(deadline, p.zone) : 'earlier this semester'
    return {
      message: `The withdrawal deadline for ${codeOr(p, 'this module')} was ${when}. To withdraw now, ${p.support}.`,
    }
  },
  'results-exist': (p) => ({
    message: `${codeOr(p, 'This module')} already has a submitted or published mark, so it can't be changed here. ${capitalise(p.support)}.`,
  }),
  'student-left': (p) => ({
    message: p.ctx.ownSession
      ? `Your record is marked as left. ${capitalise(p.support)}.`
      : "This student has left, so they can't be enrolled.",
  }),
  'module-locked': (p) => ({
    message:
      p.ctx.audience === 'admin'
        ? 'Students can already see these marks. Unpublish the semester or correct single marks.'
        : "Marks for this module are submitted and can't be changed. Ask the academic office to return it to draft.",
  }),
  'module-not-submitted': () => ({
    message: "This module is still in draft; there's nothing to return or correct yet.",
  }),
  'already-submitted': () => ({ message: 'This module has already been submitted.' }),
  'nothing-to-submit': (p) => ({
    message: `No students are enrolled on ${codeOr(p, 'this module')} this year, so there's nothing to submit.`,
  }),
  'stale-mark': (p) => {
    const n = list(p.ext.studentNumbers).length
    return {
      message: `Someone else changed ${plural(n, 'mark', 'marks')}. Review the highlighted rows and save again.`,
    }
  },
  'nothing-to-publish': (p) => {
    const target = [
      p.ctx.semester ? formatSemester(p.ctx.semester) : null,
      p.ctx.academicYear ?? null,
    ]
      .filter(Boolean)
      .join(' ')
    return {
      message: `No submitted modules are ready to publish for ${target || 'this semester'}. Lecturers submit modules from their Marks page.`,
    }
  },
  'publication-live': () => ({
    message: 'These results are already live. Unpublish them instead.',
  }),
  'publication-scheduled': () => ({
    message: 'These results are not live yet. Cancel the scheduled publication instead.',
  }),
  'username-taken': () => ({ message: 'That username is already in use.' }),
  'principal-has-account': (p) => ({
    message: `${p.ctx.number ?? p.ctx.value ?? 'This person'} already has an account.`,
  }),
  'module-code-taken': (p) => ({ message: `${p.ctx.value ?? 'That module code'} already exists.` }),
  'student-number-taken': (p) => ({
    message: `${p.ctx.value ?? 'That student number'} already exists.`,
  }),
  'staff-number-taken': (p) => ({
    message: `${p.ctx.value ?? 'That staff number'} already exists.`,
  }),
  'window-exists': (p) => {
    const target = [
      p.ctx.academicYear ?? null,
      p.ctx.semester ? formatSemester(p.ctx.semester) : null,
    ]
      .filter(Boolean)
      .join(' ')
    return {
      message: `A window for ${target || 'that semester'} already exists. Edit it instead.`,
    }
  },
  'demo-account': () => ({
    message: 'Demo accounts are read-only, so the demo stays usable for the next visitor.',
  }),
  'mfa-already-enabled': () => ({
    message: 'Two-step verification is already on for this account.',
  }),
  'credit-limit-exceeded': (p) => ({
    message: `That would take you over ${num(p.ext.limit) ?? 60} credits for ${semesterOf(p)}.`,
  }),
  'marks-incomplete': (p) => {
    const n = list(p.ext.missing).length
    return { message: `${plural(n, 'student has', 'students have')} no mark or outcome yet.` }
  },
  'not-enrolled-students': (p) => {
    const numbers = list(p.ext.studentNumbers)
    return {
      message: `${plural(numbers.length, 'student is', 'students are')} no longer enrolled: ${numbers.join(', ')}.`,
    }
  },
  'capacity-below-enrolled': (p) => ({
    message: `Capacity can't go below the ${num(p.ext.enrolledCount) ?? 'number of'} students already enrolled.`,
  }),
  'semester-change-with-enrolments': (p) => ({
    message: `The semester can't change while ${num(p.ext.enrolledCount) ?? 'some'} students are enrolled.`,
  }),
  'publish-too-far-ahead': () => ({ message: 'Choose a date within the next 90 days.' }),
  'invalid-lecturer-assignment': () => ({
    message:
      "Assign exactly one leader, list each lecturer once, and don't assign lecturers who have left.",
  }),
  'window-dates-invalid': () => ({
    message: "Opens must be before closes, and the withdrawal deadline can't be before closes.",
  }),
  'self-lockout': () => ({ message: "You can't lock or disable your own account." }),
  'role-principal-mismatch': () => ({
    message:
      'A Student account needs a student number, a Lecturer account a staff number, an Admin account neither.',
  }),
  'rate-limited': (p) => ({
    message: `Too many attempts. Try again in ${p.error.retryAfterSeconds ?? 60}s.`,
    action: 'wait',
    retryAfterSeconds: p.error.retryAfterSeconds ?? 60,
  }),
  'internal-error': somethingWrong,
  'server-busy': busy,
  timeout: busy,
}

function titleFor(status: number): string {
  if (status === 0) return "Can't reach the server"
  if (status === 400) return 'Check your details'
  if (status === 401) return 'Sign in again'
  if (status === 403) return "You don't have access"
  if (status === 404) return 'Not found'
  if (status === 409 || status === 422) return "That couldn't be done"
  if (status === 413) return 'Too much at once'
  if (status === 429) return 'Too many attempts'
  if (status === 503) return 'The portal is busy'
  return 'Something went wrong'
}

/** Status-level fallbacks for a problem whose slug is missing or unknown. */
const statusRows: Partial<Record<number, ProblemSlug>> = {
  401: 'unauthenticated',
  403: 'forbidden',
  404: 'not-found',
  405: 'method-not-allowed',
  413: 'payload-too-large',
  415: 'unsupported-media-type',
  429: 'rate-limited',
  500: 'internal-error',
  503: 'server-busy',
}

function cachedStatus(): PublicStatus | undefined {
  return queryClient.getQueryData<PublicStatus>(queryKeys.publicStatus)
}

export function describeProblem(error: unknown, context: ProblemContext = {}): ProblemDescription {
  if (!(error instanceof ApiError)) {
    if (error instanceof Error && error.message) {
      return { title: 'Something went wrong', message: error.message }
    }
    return { title: 'Something went wrong', message: 'Try again in a moment.' }
  }

  if (error.status === 0) {
    return {
      title: titleFor(0),
      message: 'The server could not be reached. Check your connection and try again.',
      action: 'retry',
    }
  }

  const status = cachedStatus()
  const support = context.support !== undefined ? context.support : status?.institution.support
  const parts: Parts = {
    error,
    ext: error.problem ?? {},
    ctx: context,
    support: supportText(support),
    zone: context.timeZone ?? status?.institution.timeZone ?? DEFAULT_TIME_ZONE,
    traceRef: error.problem?.traceId ?? 'unavailable',
  }

  const slug: ProblemSlug | undefined = isProblemSlug(error.kind)
    ? error.kind
    : statusRows[error.status]
  if (slug) {
    const row = PROBLEM_COPY[slug](parts)
    return { ...row, title: row.title ?? titleFor(error.status) }
  }

  if (error.status >= 500) {
    return { title: titleFor(error.status), ...somethingWrong(parts) }
  }
  return {
    title: titleFor(error.status),
    message: error.problem?.detail ?? error.problem?.title ?? error.message,
  }
}
