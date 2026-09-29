// The login endpoint itself, the one route every other scenario depends on and the one an attacker would hit first.
// Two independent stories, selected with -e MODE=guard (default) or -e MODE=spray:
//
//   guard: how many logins per second the PBKDF2 concurrency guard sustains. A ramping arrival rate of real logins,
//   5 -> 40/s over two minutes, each with a fresh csrf token and a distinct, random student number (D6's per-user
//   failure window never trips because no username repeats), against the production-strength CPU guard
//   (`scripts/load.ps1 login-storm -ProductionLoginGuard`, which restarts the API with LoginConcurrency=8,
//   LoginQueue=16 and every other login limiter opened up, per 04-performance-and-ops.md section 9 step 5). Counts
//   200/401/429 and records p95 latency per phase.
//
//   spray: D6's other half. One synthetic address (`X-Forwarded-For`, honoured because Development and this guard
//   run trust forwarded headers) sends wrong passwords for SPRAY_USERS distinct usernames; the per-IP failed-login
//   window (`RateLimiting:LoginFailuresPerIpPer10Minutes`, set to 20 for this run) should answer 429 well before the
//   end, and none of the sprayed usernames should be individually locked out (D6: lockout needs failures from 3+
//   addresses). A correct login for S000001 from a second, unrelated address must still succeed at 200: the spray
//   punished the attacker's address, not the victim's account.
import http from 'k6/http';
import exec from 'k6/execution';
import { Counter, Trend } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5080';
const MODE = __ENV.MODE || 'guard';
const PASSWORD = __ENV.PASSWORD || 'Student-Demo-2026!';
const STUDENT_COUNT = Number(__ENV.STUDENT_COUNT || 20000);
const SPRAY_ADDRESS = __ENV.SPRAY_ADDRESS || '203.0.113.10';
const SPRAY_SECOND_ADDRESS = __ENV.SPRAY_SECOND_ADDRESS || '203.0.113.11';
const SPRAY_USERS = Number(__ENV.SPRAY_USERS || 500);

const login200 = new Counter('login_200');
const login401 = new Counter('login_401');
const login429 = new Counter('login_429');
const loginOther = new Counter('login_other');
const loginLatency = new Trend('login_latency_ms', true);

function randomStudentNumber() {
  return 'S' + String(1 + Math.floor(Math.random() * STUDENT_COUNT)).padStart(6, '0');
}

function cookieHeaderFrom(response) {
  const parts = [];
  const jar = response.cookies || {};
  for (const name of Object.keys(jar)) {
    const values = jar[name];
    if (values && values.length > 0) {
      parts.push(`${name}=${values[values.length - 1].value}`);
    }
  }

  return parts.join('; ');
}

/**
 * One csrf + login round trip, tagged for the summary and, when `forwardedFor` is set, carrying X-Forwarded-For.
 * A fresh, throwaway `jar` on every request stops k6's automatic per-VU cookie jar from re-attaching an earlier
 * iteration's cookie alongside this one's explicit Cookie header — this VU makes hundreds of sequential login
 * attempts as distinct usernames (guard mode) or one address (spray mode), and without an isolated jar per request
 * the shared jar mixes an old `rushday.csrf` in with the fresh one, which the server correctly rejects as a mismatch.
 */
function attemptLogin(username, password, forwardedFor, tags) {
  const address = forwardedFor ? { 'X-Forwarded-For': forwardedFor } : {};
  const csrfRes = http.get(`${BASE_URL}/api/auth/csrf`, { headers: address, tags: { endpoint: 'login', ...tags }, jar: new http.CookieJar() });
  const token = csrfRes.status === 200 ? csrfRes.json('csrfToken') : '';

  return http.post(
    `${BASE_URL}/api/auth/login`,
    JSON.stringify({ username, password }),
    {
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token, Cookie: cookieHeaderFrom(csrfRes), ...address },
      tags: { endpoint: 'login', ...tags },
      jar: new http.CookieJar(),
    },
  );
}

function classify(res) {
  if (res.status === 200) login200.add(1);
  else if (res.status === 401) login401.add(1);
  else if (res.status === 429) login429.add(1);
  else {
    loginOther.add(1, { status: String(res.status) });
    console.warn(`login-storm: unexpected status ${res.status} ${res.body}`);
  }
}

/** A coarse phase label so the guard's three ramp stages show up as separate rows in the summary (rv4a house style: an always-true threshold per tag forces k6 to report it). */
function guardPhase() {
  const elapsedMs = Date.now() - exec.scenario.startTime;
  if (elapsedMs < 10000) return 'warmup_5rps';
  if (elapsedMs < 130000) return 'ramp_5to40rps';
  return 'sustained_40rps';
}

export const options = MODE === 'spray'
  ? {
      scenarios: {
        spray: { executor: 'shared-iterations', vus: 1, iterations: 1, maxDuration: '3m' },
      },
      setupTimeout: '30s',
    }
  : {
      scenarios: {
        guard: {
          executor: 'ramping-arrival-rate',
          startRate: 5,
          timeUnit: '1s',
          preAllocatedVUs: 50,
          maxVUs: 200,
          stages: [
            { target: 5, duration: '10s' },
            { target: 40, duration: '2m' },
            { target: 40, duration: '10s' },
          ],
        },
      },
      summaryTrendStats: ['avg', 'med', 'p(95)', 'p(99)', 'max'],
      thresholds: {
        'http_req_duration{endpoint:login,phase:warmup_5rps}': ['p(95)>=0'],
        'http_req_duration{endpoint:login,phase:ramp_5to40rps}': ['p(95)>=0'],
        'http_req_duration{endpoint:login,phase:sustained_40rps}': ['p(95)>=0'],
      },
    };

export default function () {
  if (MODE === 'spray') {
    runSpray();
    return;
  }

  runGuard();
}

function runGuard() {
  // Distinct random username per iteration: the per-user failure window (LoginPerUserPerMinute) never trips, so
  // every rejection this mode sees comes from the concurrency guard, not from per-account throttling.
  const username = randomStudentNumber();
  const phase = guardPhase();
  const start = Date.now();
  const res = attemptLogin(username, PASSWORD, null, { phase });
  loginLatency.add(Date.now() - start, { phase });
  classify(res);
}

function runSpray() {
  let firstThrottledAt = -1;
  for (let i = 0; i < SPRAY_USERS; i++) {
    const username = 'S' + String(1 + i).padStart(6, '0');
    const res = attemptLogin(username, 'wrong-password-on-purpose', SPRAY_ADDRESS, { phase: 'spray' });
    classify(res);
    if (res.status === 429 && firstThrottledAt === -1) {
      firstThrottledAt = i + 1;
    }
  }

  console.log(
    firstThrottledAt > 0
      ? `\nlogin-storm spray: 429 began at attempt #${firstThrottledAt} of ${SPRAY_USERS} from ${SPRAY_ADDRESS} (RateLimiting__LoginFailuresPerIpPer10Minutes controls this onset)\n`
      : `\nlogin-storm spray: never throttled across ${SPRAY_USERS} attempts from ${SPRAY_ADDRESS}; raise SPRAY_USERS or lower RateLimiting__LoginFailuresPerIpPer10Minutes\n`,
  );

  // The point of the whole scenario: a correct login for a real account from a second, unrelated address still
  // succeeds. The spray punished the attacker's address, not S000001's account (D6).
  const recovered = attemptLogin('S000001', PASSWORD, SPRAY_SECOND_ADDRESS, { phase: 'spray-recovery' });
  console.log(`\nlogin-storm spray: correct login for S000001 from ${SPRAY_SECOND_ADDRESS}: ${recovered.status}\n`);
  classify(recovered);
}
