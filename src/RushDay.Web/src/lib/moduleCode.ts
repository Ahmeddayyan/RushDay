/**
 * Module codes are two department letters and four digits (`CS3099`); the first digit is the level
 * (02-api.md route constraint `^[A-Z]{2}\d{4}$`). The catalogue's department and level filters are
 * derived from the code client-side (05-frontend.md section 10, `/student/modules`).
 */

export const MODULE_CODE_PATTERN = /^[A-Z]{2}\d{4}$/

export const DEPARTMENTS = {
  CS: 'Computer Science',
  MA: 'Mathematics',
  PH: 'Physics',
  EE: 'Electrical Engineering',
} as const

export type DepartmentCode = keyof typeof DEPARTMENTS

export interface ParsedModuleCode {
  department: string
  level: number
  number: string
}

export function isModuleCode(value: string): boolean {
  return MODULE_CODE_PATTERN.test(value)
}

/** Normalises user input the way the server does: trimmed and upper-cased. */
export function normaliseModuleCode(value: string): string {
  return value.trim().toUpperCase()
}

/** `CS3099` → `{ department: 'CS', level: 3, number: '3099' }`; null when the code is malformed. */
export function parseModuleCode(code: string): ParsedModuleCode | null {
  const normalised = normaliseModuleCode(code)
  if (!isModuleCode(normalised)) return null
  const digits = normalised.slice(2)
  return { department: normalised.slice(0, 2), level: Number(digits[0]), number: digits }
}

export function departmentOf(code: string): string | null {
  return parseModuleCode(code)?.department ?? null
}

export function levelOf(code: string): number | null {
  return parseModuleCode(code)?.level ?? null
}

/** "Computer Science" for `CS`, or the code itself for a department this list does not name. */
export function departmentName(department: string): string {
  return department in DEPARTMENTS ? DEPARTMENTS[department as DepartmentCode] : department
}
