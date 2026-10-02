/**
 * The returnTo sanitiser (05-frontend.md section 5.2). A returnTo value is honoured only when it
 * resolves to a path on this origin that is not the sign-in page itself; anything else (another
 * origin, a protocol-relative `//evil`, a backslash trick such as `/\evil.com`, control characters)
 * is rejected so the login page can never become an open redirect.
 *
 * The SPA then navigates only through the router (`navigate(result)`), never `location.assign`.
 */

// Tab, newline, NUL and the rest of C0, plus DEL and C1: browsers strip or reinterpret these in URLs.
// eslint-disable-next-line no-control-regex
const controlCharacters = /[\u0000-\u001f\u007f-\u009f]/

/** Returns `pathname + search + hash` when `value` is a safe same-origin destination, else null. */
export function sanitizeReturnTo(
  value: string | null | undefined,
  origin: string = window.location.origin,
): string | null {
  if (!value) return null
  if (value.includes('\\') || controlCharacters.test(value)) return null

  let url: URL
  try {
    url = new URL(value, origin)
  } catch {
    return null
  }

  if (url.origin !== origin) return null
  if (!url.pathname.startsWith('/')) return null
  if (url.pathname.startsWith('/login')) return null
  return url.pathname + url.search + url.hash
}

/** `/login?returnTo=...` for a guarded location the visitor could not see yet. */
export function loginPathFor(pathname: string, search = ''): string {
  return `/login?returnTo=${encodeURIComponent(pathname + search)}`
}
