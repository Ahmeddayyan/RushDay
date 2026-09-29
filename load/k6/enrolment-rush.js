// Enrolment opens for the oversubscribed elective (30 places). RUSHERS students click at the same second.
// v1 change from v0: every VU signs in for real (D25 removed the unauthenticated `/students/{n}/enrolments` route),
// so setup() logs everyone in first, spread across `http.batch` chunks (lib/auth.js), and only the enrolment POST
// itself is inside the measured window. The interesting number is still printed in teardown: how many places did
// the atomic conditional update actually hand out?
import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';
import { loginMany, params, studentNumber } from './lib/auth.js';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5080';
const MODULE_CODE = __ENV.MODULE_CODE || 'CS3099';
const RUSHERS = Number(__ENV.RUSHERS || 500);
const FIRST_STUDENT = Number(__ENV.FIRST_STUDENT || 1);
const LOGIN_BATCH_SIZE = Number(__ENV.LOGIN_BATCH_SIZE || 25);

const accepted = new Counter('enrolments_accepted');
const rejectedFull = new Counter('enrolments_rejected_full');
const rejectedOther = new Counter('enrolments_rejected_other');
const shed = new Counter('enrolments_shed');
const errored = new Counter('enrolments_errored');

export const options = {
  scenarios: {
    rush: {
      executor: 'per-vu-iterations',
      vus: RUSHERS,
      iterations: 1,
      maxDuration: '2m',
    },
  },
  setupTimeout: '5m',
  summaryTrendStats: ['avg', 'med', 'p(95)', 'p(99)', 'max'],
};

export function setup() {
  const usernames = [];
  for (let i = 0; i < RUSHERS; i++) {
    usernames.push(studentNumber(FIRST_STUDENT + i));
  }

  const sessions = loginMany(usernames, undefined, LOGIN_BATCH_SIZE);
  if (sessions.length < usernames.length) {
    console.warn(`enrolment-rush setup: ${usernames.length - sessions.length} of ${usernames.length} logins failed; the rush runs with fewer VUs than requested.`);
  }

  return { sessions };
}

export default function (data) {
  const session = data.sessions[__VU - 1];
  if (!session) {
    return; // fewer sessions than VUs (a login failed in setup); nothing to enrol with.
  }

  const res = http.post(
    `${BASE_URL}/api/me/enrolments`,
    JSON.stringify({ moduleCode: MODULE_CODE }),
    params(session, { tags: { endpoint: 'enrol' } }),
  );

  if (res.status === 201) {
    accepted.add(1);
  } else if (res.status === 409) {
    if (safeType(res) === 'urn:rushday:module-full') {
      rejectedFull.add(1);
    } else {
      rejectedOther.add(1, { type: String(safeType(res)) });
    }
  } else if (res.status === 422) {
    rejectedOther.add(1, { type: String(safeType(res)) });
  } else if (res.status === 429 || res.status === 503) {
    shed.add(1, { status: String(res.status) });
  } else {
    errored.add(1, { status: String(res.status) });
    console.warn(`enrol ${session.username}: ${res.status} ${res.body}`);
  }

  check(res, { 'accepted or full': (r) => r.status === 201 || r.status === 409 });
}

function safeType(res) {
  try {
    return res.json('type');
  } catch (e) {
    return '';
  }
}

export function teardown(data) {
  const first = data.sessions[0];
  if (!first) {
    console.log('\nenrolment-rush teardown: no session survived setup, cannot read the module.\n');
    return;
  }

  const res = http.get(`${BASE_URL}/api/modules/${MODULE_CODE}`, params(first, { tags: { endpoint: 'module-detail' } }));
  const m = res.json();
  const oversold = Math.max(0, m.enrolledCount - m.capacity);
  console.log(`\n${MODULE_CODE}: capacity=${m.capacity} enrolledCount=${m.enrolledCount} OVERSOLD=${oversold}\n`);
}
