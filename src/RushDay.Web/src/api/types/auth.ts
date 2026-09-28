/**
 * Auth-area types. Placeholder for stage S3: the full request/response shapes of 02-api.md section
 * 2 (MfaChallenge, LoginResponse, ...) are added in stage S5, which owns this file from here on.
 */

export type Role = 'Student' | 'Lecturer' | 'Admin'

export interface LoginRequest {
  username: string
  password: string
}
