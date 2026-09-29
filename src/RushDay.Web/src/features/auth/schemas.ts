import { z } from 'zod'

/** `/login` (05-frontend.md section 10): username trimmed 1..64, password 1..128. No case transform. */
export const loginSchema = z.object({
  username: z
    .string()
    .trim()
    .min(1, 'Enter your student number, staff number or admin username.')
    .max(64, 'Usernames are at most 64 characters.'),
  password: z
    .string()
    .min(1, 'Enter your password.')
    .max(128, 'Passwords are at most 128 characters.'),
})

export type LoginFormValues = z.infer<typeof loginSchema>

/** The second step of sign-in and MFA setup: six digits. */
export const mfaCodeSchema = z.object({
  code: z
    .string()
    .trim()
    .regex(/^\d{6}$/, 'Enter the 6-digit code from your authenticator app.'),
})

export type MfaCodeFormValues = z.infer<typeof mfaCodeSchema>

export const PASSWORD_MIN = 12
export const PASSWORD_MAX = 128
export const PASSWORD_MIN_DISTINCT = 4

/** `/account/password`: 12..128, different from the current password, confirmed. */
export const changePasswordSchema = z
  .object({
    currentPassword: z.string().min(1, 'Enter your current password.').max(PASSWORD_MAX),
    newPassword: z
      .string()
      .min(PASSWORD_MIN, `Use at least ${PASSWORD_MIN} characters.`)
      .max(PASSWORD_MAX, `Use ${PASSWORD_MAX} characters or fewer.`),
    confirmPassword: z.string().min(1, 'Type the new password again.'),
  })
  .refine((values) => values.newPassword !== values.currentPassword, {
    path: ['newPassword'],
    message: 'Choose a password different from your current one.',
  })
  .refine((values) => values.confirmPassword === values.newPassword, {
    path: ['confirmPassword'],
    message: "The passwords don't match.",
  })

export type ChangePasswordFormValues = z.infer<typeof changePasswordSchema>

export interface PasswordCheck {
  id: string
  label: string
  met: boolean
}

/**
 * The live checklist under the new-password field: the rules of the password policy (D22) the
 * browser can check without the server. The blocklist stays server-side.
 */
export function passwordChecks(
  newPassword: string,
  currentPassword: string,
  username: string,
): PasswordCheck[] {
  const lower = newPassword.toLowerCase()
  return [
    {
      id: 'length',
      label: `At least ${PASSWORD_MIN} characters`,
      met: newPassword.length >= PASSWORD_MIN,
    },
    {
      id: 'distinct',
      label: `At least ${PASSWORD_MIN_DISTINCT} different characters`,
      met: new Set(newPassword).size >= PASSWORD_MIN_DISTINCT,
    },
    {
      id: 'username',
      label: "Doesn't contain your username",
      met: newPassword.length > 0 && (username === '' || !lower.includes(username.toLowerCase())),
    },
    {
      id: 'product',
      label: 'Doesn\'t contain "rushday"',
      met: newPassword.length > 0 && !lower.includes('rushday'),
    },
    {
      id: 'different',
      label: 'Different from your current password',
      met: newPassword.length > 0 && newPassword !== currentPassword,
    },
  ]
}
