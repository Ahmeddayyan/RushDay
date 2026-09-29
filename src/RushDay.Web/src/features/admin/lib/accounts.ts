import type { Role } from '@/api/types/common'

/** `lockoutEnd = 9999-12-31` is the administrator's lock; anything sooner is Identity's lockout. */
export function isAdministratorLock(lockoutEnd: string | null): boolean {
  return lockoutEnd !== null && new Date(lockoutEnd).getUTCFullYear() >= 9999
}

export const ROLE_OPTIONS: readonly { value: Role; label: string }[] = [
  { value: 'Student', label: 'Student' },
  { value: 'Lecturer', label: 'Lecturer' },
  { value: 'Admin', label: 'Administrator' },
]
