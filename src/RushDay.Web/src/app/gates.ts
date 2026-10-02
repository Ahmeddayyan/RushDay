import type { Me } from '@/api/types/common'
import { sanitizeReturnTo } from '@/lib/returnTo'

/**
 * The two gates of 05-frontend.md section 5.1 step 6, in order: a forced password change, then a
 * forced MFA setup. Returns where a signed-in user who may not see `pathname` yet must go, or null
 * when they may. The original destination travels as `returnTo` (on a gate page, its own
 * `returnTo` is carried on to the next gate).
 */
export function gateRedirect(user: Me, pathname: string, search: string): string | null {
  const onPasswordGate = pathname === '/account/password'
  const onMfaGate = pathname === '/account/mfa'
  const destination =
    onPasswordGate || onMfaGate
      ? sanitizeReturnTo(new URLSearchParams(search).get('returnTo'))
      : sanitizeReturnTo(pathname + search)
  const carry = destination ? `&returnTo=${encodeURIComponent(destination)}` : ''

  if (user.mustChangePassword) return onPasswordGate ? null : `/account/password?required=1${carry}`
  if (user.mfaSetupRequired) return onMfaGate ? null : `/account/mfa?required=1${carry}`
  return null
}

/**
 * Where to send a user who has just signed in (or opened `/`) and is headed for `destination`: the
 * first gate that applies, carrying the destination, else the destination itself. Applying the
 * gates here, not only on guarded routes, means a gated user never lands anywhere else first.
 */
export function gatedDestination(user: Me, destination: string): string {
  const url = new URL(destination, 'http://rushday.invalid')
  return gateRedirect(user, url.pathname, url.search) ?? destination
}
