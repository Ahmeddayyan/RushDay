import { createHmac } from 'node:crypto'

/**
 * RFC 6238 time-based one-time passwords (SHA-1, 30-second steps, 6 digits), the scheme of
 * `POST /api/auth/mfa/setup` (02-api.md section 2.4), computed from the base32 `sharedKey` the setup
 * page shows. The server accepts steps up to two either side of its clock but never a step it has
 * already accepted for the account, so every sign-in after `enable` needs a later step than the one
 * before: `freshTotp` picks it.
 */

const BASE32_ALPHABET = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
export const TOTP_STEP_SECONDS = 30

/** Decodes base32 as Identity writes it: any case, grouped with spaces, no padding needed. */
export function base32Decode(input: string): Buffer {
  const clean = input.replace(/[\s=-]/g, '').toUpperCase()
  const bytes: number[] = []
  let buffer = 0
  let bits = 0
  for (const char of clean) {
    const index = BASE32_ALPHABET.indexOf(char)
    if (index < 0) throw new Error(`"${char}" is not a base32 character`)
    buffer = ((buffer << 5) | index) & 0xffff
    bits += 5
    if (bits >= 8) {
      bytes.push((buffer >>> (bits - 8)) & 0xff)
      bits -= 8
    }
  }
  return Buffer.from(bytes)
}

/** The time step (30-second window since the Unix epoch) of an instant. */
export function totpStep(atMs: number = Date.now()): number {
  return Math.floor(atMs / 1000 / TOTP_STEP_SECONDS)
}

/** The 6-digit code of one time step. */
export function totpCode(sharedKey: string, step: number): string {
  const counter = Buffer.alloc(8)
  counter.writeBigUInt64BE(BigInt(step))
  const digest = createHmac('sha1', base32Decode(sharedKey)).update(counter).digest()
  const offset = digest.readUInt8(digest.length - 1) & 0x0f
  const binary = digest.readUInt32BE(offset) & 0x7fffffff
  return String(binary % 1_000_000).padStart(6, '0')
}

/**
 * A code the server will still accept: the current step, or the step after `lastUsedStep` when that
 * one was used within this window (the server takes steps up to two ahead of its clock).
 */
export function freshTotp(
  sharedKey: string,
  lastUsedStep?: number,
): { code: string; step: number } {
  const step = Math.max(totpStep(), (lastUsedStep ?? Number.NEGATIVE_INFINITY) + 1)
  return { code: totpCode(sharedKey, step), step }
}
