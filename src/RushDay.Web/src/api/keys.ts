/**
 * Query keys (05-frontend.md section 6.2). This is the S3 placeholder holding only what stage S3's
 * own code needs; the full catalogue is written once, in full, in stage S5 and stages S7-S10 add
 * nothing here (they read from their own endpoint modules).
 */
export const queryKeys = {
  index: ['index'] as const,
  publicStatus: ['public', 'status'] as const,
  authMe: ['auth', 'me'] as const,
}
