/**
 * Request and response shapes of the `/api/auth` routes (02-api.md sections 2.3, 2.4 and 8.1-8.2).
 * `Me`, `MfaChallenge` and `LoginResponse` are shared shapes and live in `common.ts`.
 */

export type { LoginResponse, Me, MfaChallenge, Role } from './common'

/** `POST /api/auth/login`: username 1..64, password 1..128. */
export interface LoginRequest {
  username: string
  password: string
}

/** `POST /api/auth/mfa/verify` and `POST /api/auth/mfa/enable`: six digits. */
export interface MfaCodeRequest {
  code: string
}

/** `POST /api/auth/change-password` */
export interface ChangePasswordRequest {
  currentPassword: string
  newPassword: string
}

/** `GET /api/auth/csrf` */
export interface CsrfResponse {
  csrfToken: string
}

/** `POST /api/auth/mfa/setup`: base32 key grouped in fours, and the otpauth:// URI for the QR code. */
export interface MfaSetupResponse {
  sharedKey: string
  otpauthUri: string
}
