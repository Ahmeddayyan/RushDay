// Exploratory probe: hold the authenticated dashboard at a fixed arrival rate for 30s and see what gives.
// No thresholds: the numbers are the finding. The finding v1 is after is "excess is shed fast" (503 with
// Retry-After inside a second) rather than v0's "nothing fails until it fails very slowly".
//   k6 run -e RATE=2000 load/k6/dashboard-knee.js
import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';
import { loginMany, params, studentNumber } from './lib/auth.js';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5080';
const LOGIN_POOL = Number(__ENV.LOGIN_POOL || 200);
const STUDENT_COUNT = Number(__ENV.STUDENT_COUNT || 300);
const RATE = Number(__ENV.RATE || 1000);

const shed503 = new Counter('shed_503');
const rateLimited429 = new Counter('rate_limited_429');

export const options = {
  scenarios: {
    knee: {
      executor: 'constant-arrival-rate',
      rate: RATE,
      timeUnit: '1s',
      duration: '30s',
      preAllocatedVUs: Math.min(RATE, 2000),
      maxVUs: 4000,
    },
  },
  setupTimeout: '5m',
  summaryTrendStats: ['avg', 'med', 'p(95)', 'p(99)', 'max'],
  // Report-only (always true): the dashboard requests' own latency and failure rate, apart from setup()'s logins.
  thresholds: {
    'http_req_duration{endpoint:dashboard}': ['p(95)>=0'],
    'http_req_failed{endpoint:dashboard}': ['rate>=0'],
  },
};

export function setup() {
  const usernames = [];
  const pool = Math.min(LOGIN_POOL, STUDENT_COUNT);
  const stride = Math.max(1, Math.floor(STUDENT_COUNT / pool));
  for (let i = 0; i < pool; i++) {
    usernames.push(studentNumber(1 + ((i * stride) % STUDENT_COUNT)));
  }

  const sessions = loginMany(usernames);
  if (sessions.length === 0) {
    throw new Error('dashboard-knee setup: every login failed; nothing to measure.');
  }

  return { sessions };
}

export default function (data) {
  const session = data.sessions[Math.floor(Math.random() * data.sessions.length)];
  const res = http.get(`${BASE_URL}/api/me/dashboard`, params(session, { tags: { endpoint: 'dashboard' } }));

  if (res.status === 503) {
    shed503.add(1);
  } else if (res.status === 429) {
    rateLimited429.add(1);
  }

  check(res, { 'dashboard 200': (r) => r.status === 200 });
}
