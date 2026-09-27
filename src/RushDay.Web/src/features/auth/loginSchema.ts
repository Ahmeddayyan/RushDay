import { z } from 'zod'

export const loginSchema = z.object({
  identifier: z
    .string()
    .trim()
    .min(1, 'Enter your student number or username')
    .max(64, 'That is too long for a student number or username'),
  password: z.string().min(1, 'Enter your password').max(256, 'That password is too long'),
})

export type LoginFormValues = z.infer<typeof loginSchema>
