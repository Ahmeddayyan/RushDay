import { z } from 'zod'

/** Username 1..64, password 1..128 (05-frontend.md section 10, `/login`). */
export const loginSchema = z.object({
  username: z
    .string()
    .trim()
    .min(1, 'Enter your student number, staff number or admin username')
    .max(64, 'That is too long'),
  password: z.string().min(1, 'Enter your password').max(128, 'That password is too long'),
})

export type LoginFormValues = z.infer<typeof loginSchema>
