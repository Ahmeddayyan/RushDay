// Authenticated session helpers for every v1 k6 scenario (04-performance-and-ops.md section 8).
//
// v0's scripts hit unauthenticated `/students/{n}/...` routes. v1 removed those routes (D25), so every scenario now
// signs in for real: GET /api/auth/csrf -> POST /api/auth/login with the X-CSRF-TOKEN header -> a session cookie plus
// a fresh CSRF token bound to the signed-in identity (D5). Nothing here hard-codes a cookie name: the cookie is
// `rushday.auth` / `rushday.csrf` locally and `__Host-rushday.auth` / `__Host-rushday.csrf` against a deployed
// instance (D4), so every Set-Cookie of the csrf and login responses is collected and replayed verbatim.
//
// Sessions are built once in a scenario's setup() (outside the measured window; give setup a generous setupTimeout,
// e.g. '5m') and passed to the default function as data. k6's per-VU cookie jar does not carry cookies collected
// during setup(), so every request built from a session sends an explicit Cookie header (see params()) rather than
// relying on the jar.
import http from 'k6/http';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5080';
export const PASSWORD = __ENV.PASSWORD || 'Student-Demo-2026!';

/**
 * k6 gives every VU one shared, automatic cookie jar per host, which stores every Set-Cookie it sees and re-attaches
 * whatever it is currently holding to every later request to that host, *in addition to* an explicit Cookie header a
 * request sets. With many distinct logins in flight from one VU (setup()'s http.batch chunks) or many distinct
 * sessions reused by one VU across iterations (results-day.js, dashboard-knee.js picking a random session per
 * iteration), that shared jar mixes cookies across sessions: a request can end up carrying its own explicit
 * `rushday.csrf` alongside a stale one the jar remembers from a different session, and the server rejects the
 * mismatch as a 400 antiforgery error. Every request in this file supplies a throwaway, single-use jar instead, so
 * the explicit Cookie header this module builds is the *only* source of cookies on the wire.
 */
function noJar() {
  return new http.CookieJar();
}

/** `"a=1; b=2"` -> `{ a: '1', b: '2' }`. */
function parseCookieHeader(header) {
  const map = {};
  (header || '').split(';').forEach((part) => {
    const trimmed = part.trim();
    if (!trimmed) return;
    const eq = trimmed.indexOf('=');
    if (eq === -1) return;
    map[trimmed.slice(0, eq)] = trimmed.slice(eq + 1);
  });
  return map;
}

/**
 * The Cookie header for the next request: every cookie in `existing` (a `; `-joined Cookie string), with every
 * cookie `response` set overlaid by name. A login response rotates the antiforgery cookie alongside issuing the
 * session cookie (D5: "the token rotates with the principal"), so this must *replace* a same-named cookie rather
 * than append a second copy of it — a Cookie header carrying both the old and the new `rushday.csrf` would send a
 * stale antiforgery cookie value that no longer matches the fresh `csrfToken` in the response body, and every
 * following mutating request would fail antiforgery validation.
 */
function foldCookies(response, existing) {
  const map = parseCookieHeader(existing);
  const jar = response.cookies || {};
  for (const name of Object.keys(jar)) {
    const values = jar[name];
    if (values && values.length > 0) {
      map[name] = values[values.length - 1].value;
    }
  }

  return Object.keys(map)
    .map((name) => `${name}=${map[name]}`)
    .join('; ');
}

/** GET /api/auth/csrf -> { token, cookie }. Anonymous; tagged `login` so it is excluded from a scenario's own thresholds. */
export function csrf() {
  const res = http.get(`${BASE_URL}/api/auth/csrf`, { tags: { endpoint: 'login' }, jar: noJar() });
  if (res.status !== 200) {
    throw new Error(`GET /api/auth/csrf: ${res.status} ${res.body}`);
  }

  return { token: res.json('csrfToken'), cookie: foldCookies(res, '') };
}

/** csrf() -> POST /api/auth/login with X-CSRF-TOKEN -> a session { username, cookie, csrf }. Throws on MFA (not supported here: v1 never requires MFA for Student or Lecturer). */
export function login(username, password) {
  const anon = csrf();
  const res = http.post(
    `${BASE_URL}/api/auth/login`,
    JSON.stringify({ username, password: password || PASSWORD }),
    { headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': anon.token, Cookie: anon.cookie }, tags: { endpoint: 'login' }, jar: noJar() },
  );
  if (res.status !== 200) {
    throw new Error(`login ${username}: ${res.status} ${res.body}`);
  }

  const body = res.json();
  if (body.mfaRequired) {
    throw new Error(`login ${username}: mfaRequired (k6 scenarios sign in as Student or Lecturer, which never need a second factor)`);
  }

  return { username, cookie: foldCookies(res, anon.cookie), csrf: body.csrfToken };
}

/**
 * Signs in every username in `usernames` in `http.batch` chunks of `batchSize` (default 25), so hundreds of logins
 * are spread over many round trips instead of arriving as one instant of simultaneous connects (the Windows
 * client-edition listen backlog refuses a share of connections that all land in the same instant; batching is what
 * keeps that refusal rate at zero for setup()). A user whose login fails or comes back with an MFA challenge is
 * logged and skipped, not thrown, so one bad account does not abort the whole rush.
 */
export function loginMany(usernames, password, batchSize) {
  const size = batchSize || 25;
  const sessions = [];

  for (let i = 0; i < usernames.length; i += size) {
    const chunk = usernames.slice(i, i + size);

    const csrfResponses = http.batch(chunk.map((_username) => ['GET', `${BASE_URL}/api/auth/csrf`, null, { tags: { endpoint: 'login' }, jar: noJar() }]));
    const withToken = chunk.map((username, index) => {
      const res = csrfResponses[index];
      if (res.status !== 200) {
        console.warn(`csrf for ${username}: ${res.status}`);
        return null;
      }

      return { username, token: res.json('csrfToken'), cookie: foldCookies(res, '') };
    });

    const pending = withToken.filter((entry) => entry !== null);
    const loginResponses = http.batch(
      pending.map((entry) => [
        'POST',
        `${BASE_URL}/api/auth/login`,
        JSON.stringify({ username: entry.username, password: password || PASSWORD }),
        { headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': entry.token, Cookie: entry.cookie }, tags: { endpoint: 'login' }, jar: noJar() },
      ]),
    );

    pending.forEach((entry, index) => {
      const res = loginResponses[index];
      if (res.status !== 200) {
        console.warn(`login ${entry.username}: ${res.status} ${res.body}`);
        return;
      }

      const body = res.json();
      if (body.mfaRequired) {
        console.warn(`login ${entry.username}: mfaRequired, skipped`);
        return;
      }

      sessions.push({ username: entry.username, cookie: foldCookies(res, entry.cookie), csrf: body.csrfToken });
    });
  }

  return sessions;
}

/** Request params for an authenticated call: the session's Cookie and X-CSRF-TOKEN, JSON content type, and any extra headers/tags. */
export function params(session, extra) {
  const opts = extra || {};
  return {
    headers: { Cookie: session.cookie, 'X-CSRF-TOKEN': session.csrf, 'Content-Type': 'application/json', ...(opts.headers || {}) },
    tags: opts.tags,
    jar: noJar(),
  };
}

export function studentNumber(n) {
  return `S${String(n).padStart(6, '0')}`;
}
