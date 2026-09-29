import { z } from '@/lib/zod'

/**
 * Client validation for the administrator forms. Each mirrors the request rules of 02-api.md
 * section 8.5, so the browser catches what the server would answer with 400 `validation` or 422;
 * the server stays the authority and its field errors map onto the same names.
 */

export const REASON_MIN = 10
export const REASON_MAX = 400

/** Every override, leave, trim, unpublish, return-to-draft and correction carries a reason (10..400). */
export const reasonField = z
  .string()
  .trim()
  .min(REASON_MIN, `Give a reason of at least ${REASON_MIN} characters.`)
  .max(REASON_MAX, `Keep the reason to ${REASON_MAX} characters or fewer.`)

export const reasonSchema = z.object({ reason: reasonField })
export type ReasonFormValues = z.infer<typeof reasonSchema>

const optionalEmail = z
  .string()
  .trim()
  .max(256, 'Use 256 characters or fewer.')
  .refine((value) => value === '' || z.email().safeParse(value).success, {
    message: 'Enter an email address like name@example.ac.uk.',
  })

/** Whole numbers typed into `type="text" inputMode="numeric"` fields. */
function wholeNumber(label: string, min: number, max: number) {
  return z
    .string()
    .trim()
    .regex(/^\d+$/, `Enter ${label} as a whole number.`)
    .refine((value) => Number(value) >= min && Number(value) <= max, {
      message: `Enter ${label} between ${min} and ${max}.`,
    })
}

export const studentNumberField = z
  .string()
  .trim()
  .transform((value) => value.toUpperCase())
  .pipe(z.string().regex(/^S\d{6}$/, 'Student numbers are S followed by six digits, like S000123.'))

export const studentSchema = z.object({
  fullName: z
    .string()
    .trim()
    .min(1, 'Enter the full name.')
    .max(200, 'Use 200 characters or fewer.'),
  programme: z
    .string()
    .trim()
    .min(1, 'Enter the programme.')
    .max(200, 'Use 200 characters or fewer.'),
  yearOfStudy: wholeNumber('the year of study', 1, 6),
  email: optionalEmail,
})
export type StudentFormValues = z.infer<typeof studentSchema>

export const createStudentSchema = studentSchema.extend({
  studentNumber: studentNumberField,
  provision: z.boolean(),
})
export type CreateStudentFormValues = z.input<typeof createStudentSchema>

export const LECTURER_TITLES = ['Dr', 'Prof', 'Mr', 'Ms', 'Mx'] as const

export const lecturerSchema = z.object({
  fullName: z
    .string()
    .trim()
    .min(1, 'Enter the full name.')
    .max(200, 'Use 200 characters or fewer.'),
  title: z.enum(LECTURER_TITLES, 'Choose a title.'),
  department: z
    .string()
    .trim()
    .min(1, 'Enter the department code, for example CS.')
    .max(8, 'Department codes are at most 8 characters.'),
  email: optionalEmail,
})
export type LecturerFormValues = z.infer<typeof lecturerSchema>

export const createLecturerSchema = lecturerSchema.extend({
  staffNumber: z
    .string()
    .trim()
    .transform((value) => value.toUpperCase())
    .pipe(
      z.string().regex(/^L\d{5}$/, 'Staff numbers are L followed by five digits, like L00042.'),
    ),
})
export type CreateLecturerFormValues = z.input<typeof createLecturerSchema>

export const moduleSchema = z.object({
  title: z.string().trim().min(1, 'Enter the title.').max(200, 'Use 200 characters or fewer.'),
  description: z.string().trim().max(2000, 'Use 2,000 characters or fewer.'),
  credits: wholeNumber('credits', 5, 60),
  capacity: wholeNumber('the capacity', 0, 10000),
  semester: z.enum(['autumn', 'spring']),
  isActive: z.boolean(),
})
export type ModuleFormValues = z.infer<typeof moduleSchema>

export const createModuleSchema = moduleSchema.extend({
  code: z
    .string()
    .trim()
    .transform((value) => value.toUpperCase())
    .pipe(
      z
        .string()
        .regex(/^[A-Z]{2}\d{4}$/, 'Module codes are two letters and four digits, like CS3099.'),
    ),
})
export type CreateModuleFormValues = z.input<typeof createModuleSchema>

/** Editing keeps the module's code as it is (the route names it), so the field is not validated. */
export const editModuleSchema = moduleSchema.extend({ code: z.string() })

/** Identity's allowed user-name characters (02-api.md section 2.1). */
export const USERNAME_PATTERN = /^[A-Za-z0-9._-]+$/

export const provisionSchema = z.object({
  username: z
    .string()
    .trim()
    .min(1, 'Enter a username.')
    .max(64, 'Usernames are at most 64 characters.')
    .regex(USERNAME_PATTERN, 'Use letters, digits, dots, hyphens and underscores only.'),
  displayName: z
    .string()
    .trim()
    .min(1, 'Enter the name to display.')
    .max(200, 'Use 200 characters or fewer.'),
  email: optionalEmail,
  temporaryPassword: z
    .string()
    .refine((value) => value === '' || (value.length >= 12 && value.length <= 128), {
      message: 'Use 12 to 128 characters, or leave it blank to generate one.',
    }),
})
export type ProvisionFormValues = z.infer<typeof provisionSchema>

export const ACADEMIC_YEAR_PATTERN = /^\d{4}\/\d{2}$/

/** "2026/27" → true; the second half must follow the first ("2026/28" is a typo). */
export function isAcademicYear(value: string): boolean {
  if (!ACADEMIC_YEAR_PATTERN.test(value)) return false
  const start = Number(value.slice(0, 4))
  return (start + 1) % 100 === Number(value.slice(5, 7))
}

export const academicYearField = z
  .string()
  .trim()
  .refine(isAcademicYear, { message: 'Write the academic year like 2026/27.' })

/** An IANA zone id the browser can format with (and the server's shape check accepts). */
export function isTimeZone(value: string): boolean {
  if (!/^[A-Za-z]+(\/[A-Za-z_+-]+){1,2}$/.test(value)) return false
  try {
    new Intl.DateTimeFormat('en-GB', { timeZone: value })
    return true
  } catch {
    return false
  }
}

export const settingsSchema = z.object({
  academicYear: academicYearField,
  currentSemester: z.enum(['autumn', 'spring']),
  institutionName: z
    .string()
    .trim()
    .min(1, 'Enter the institution name.')
    .max(200, 'Use 200 characters or fewer.'),
  institutionShortName: z
    .string()
    .trim()
    .min(1, 'Enter a short name.')
    .max(32, 'Use 32 characters or fewer.'),
  timeZone: z
    .string()
    .trim()
    .refine(isTimeZone, { message: 'Enter an IANA time zone id, for example Europe/London.' }),
  supportEmail: optionalEmail,
  supportUrl: z
    .string()
    .trim()
    .max(400, 'Use 400 characters or fewer.')
    .refine((value) => value === '' || /^https:\/\/[^\s/$.?#].[^\s]*$/i.test(value), {
      message: 'Enter a full https:// address.',
    }),
})
export type SettingsFormValues = z.infer<typeof settingsSchema>

/** Correcting one mark: outcome, a 0..100 whole mark when the outcome is Mark, and a reason. */
export const correctMarkSchema = z
  .object({
    outcome: z.enum(['mark', 'absent', 'deferred']),
    mark: z.string().trim(),
    reason: reasonField,
  })
  .superRefine((values, context) => {
    if (values.outcome !== 'mark') return
    if (!/^\d{1,3}$/.test(values.mark) || Number(values.mark) > 100) {
      context.addIssue({
        code: 'custom',
        path: ['mark'],
        message: 'Enter a whole mark from 0 to 100.',
      })
    }
  })
export type CorrectMarkFormValues = z.infer<typeof correctMarkSchema>

export const ANNOUNCEMENT_TITLE_MAX = 120
export const ANNOUNCEMENT_BODY_MAX = 4000

/** Empty text for an optional field, else the trimmed value. */
export function orNull(value: string): string | null {
  const trimmed = value.trim()
  return trimmed === '' ? null : trimmed
}
