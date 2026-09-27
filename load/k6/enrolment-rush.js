// Enrolment opens for the oversubscribed elective (30 places). 500 students click at the same second.
// The interesting number is printed in teardown: how many places did the system actually hand out?
import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5080';
const MODULE_CODE = __ENV.MODULE_CODE || 'CS3099';
const RUSHERS = Number(__ENV.RUSHERS || 500);

const accepted = new Counter('enrolments_accepted');
const rejectedFull = new Counter('enrolments_rejected_full');
const errors = new Counter('enrolments_errored');

export const options = {
  scenarios: {
    rush: {
      executor: 'per-vu-iterations',
      vus: RUSHERS,
      iterations: 1,
      maxDuration: '2m',
    },
  },
};

export default function () {
  const studentNumber = `S${String(__VU).padStart(6, '0')}`;

  const res = http.post(
    `${BASE_URL}/students/${studentNumber}/enrolments`,
    JSON.stringify({ moduleCode: MODULE_CODE }),
    { headers: { 'Content-Type': 'application/json' }, tags: { endpoint: 'enrol' } },
  );

  if (res.status === 201) accepted.add(1);
  else if (res.status === 409) rejectedFull.add(1);
  else errors.add(1);

  check(res, { 'accepted or full': (r) => r.status === 201 || r.status === 409 });
}

export function teardown() {
  const res = http.get(`${BASE_URL}/modules/${MODULE_CODE}`);
  const m = res.json();
  const oversold = Math.max(0, m.enrolled - m.capacity);
  console.log(`\n${MODULE_CODE}: capacity=${m.capacity} enrolled=${m.enrolled} OVERSOLD=${oversold}\n`);
}
