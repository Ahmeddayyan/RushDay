// Exploratory probe: hold the dashboard at a fixed arrival rate for 30s and see what gives.
// Run it at increasing RATE values to find the knee. No thresholds: the numbers are the finding.
//   k6 run -e RATE=2000 load/k6/dashboard-knee.js
import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5080';
const STUDENT_COUNT = Number(__ENV.STUDENT_COUNT || 20000);
const RATE = Number(__ENV.RATE || 1000);

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
  summaryTrendStats: ['avg', 'med', 'p(95)', 'p(99)', 'max'],
};

export default function () {
  const n = 1 + Math.floor(Math.random() * STUDENT_COUNT);
  const res = http.get(`${BASE_URL}/students/S${String(n).padStart(6, '0')}/dashboard`, {
    tags: { endpoint: 'dashboard' },
  });
  check(res, { 'dashboard 200': (r) => r.status === 200 });
}
