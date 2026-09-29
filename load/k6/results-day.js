// Results day, 09:00. Marks have just been published and the whole cohort opens the dashboard.
// Arrival-rate executor: we model *requests per second arriving*, not a fixed number of users, because that is how
// a real spike behaves. If the API slows down, load does not politely wait.
//
// v1 change from v0: the route is authenticated (`GET /api/me/dashboard`, no student number in the URL, D25), so
// setup() signs in a pool of LOGIN_POOL students (spread across `http.batch` chunks, not one instant of connects)
// and every iteration reuses a random session from that pool instead of hitting a bare, unauthenticated route.
import http from 'k6/http';
import { check } from 'k6';
import { Rate } from 'k6/metrics';
import { loginMany, params, studentNumber } from './lib/auth.js';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5080';
const LOGIN_POOL = Number(__ENV.LOGIN_POOL || 200);
const STUDENT_COUNT = Number(__ENV.STUDENT_COUNT || 300);

const resultsVisible = new Rate('results_visible');

export const options = {
  scenarios: {
    results_day: {
      executor: 'ramping-arrival-rate',
      startRate: 10,
      timeUnit: '1s',
      preAllocatedVUs: 200,
      maxVUs: 3000,
      stages: [
        { target: 50, duration: '30s' }, // early risers
        { target: 300, duration: '1m' }, // the email lands
        { target: 800, duration: '1m' }, // everyone
        { target: 800, duration: '1m' }, // sustained
        { target: 0, duration: '30s' }, // tail off
      ],
    },
  },
  setupTimeout: '5m',
  summaryTrendStats: ['avg', 'med', 'p(95)', 'p(99)', 'max'],
  thresholds: {
    'http_req_failed{endpoint:dashboard}': ['rate<0.01'],
    'http_req_duration{endpoint:dashboard}': ['p(95)<500', 'p(99)<1000'],
  },
};

export function setup() {
  const usernames = [];
  const pool = Math.min(LOGIN_POOL, STUDENT_COUNT);
  // Spread the pool across the whole numeric range rather than the first N, so a small pool still resembles a random cohort.
  const stride = Math.max(1, Math.floor(STUDENT_COUNT / pool));
  for (let i = 0; i < pool; i++) {
    usernames.push(studentNumber(1 + ((i * stride) % STUDENT_COUNT)));
  }

  const sessions = loginMany(usernames);
  if (sessions.length === 0) {
    throw new Error('results-day setup: every login failed; nothing to measure.');
  }

  return { sessions };
}

export default function (data) {
  const session = data.sessions[Math.floor(Math.random() * data.sessions.length)];

  const res = http.get(`${BASE_URL}/api/me/dashboard`, params(session, { tags: { endpoint: 'dashboard' } }));

  const ok = check(res, { 'dashboard 200': (r) => r.status === 200 });
  if (ok) {
    // Informational: on the demo, the seeded autumn results are already published, so this should read 1 throughout.
    resultsVisible.add(res.json('results') !== null && res.json('results').length > 0);
  }
}
