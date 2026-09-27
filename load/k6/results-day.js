// Results day, 09:00. Marks have just been published and the whole cohort opens the dashboard.
// Arrival-rate executor: we model *requests per second arriving*, not a fixed number of users,
// because that is how a real spike behaves. If the API slows down, load does not politely wait.
import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5080';
const STUDENT_COUNT = Number(__ENV.STUDENT_COUNT || 20000);

export const options = {
  scenarios: {
    results_day: {
      executor: 'ramping-arrival-rate',
      startRate: 10,
      timeUnit: '1s',
      preAllocatedVUs: 200,
      maxVUs: 3000,
      stages: [
        { target: 50, duration: '30s' },   // early risers
        { target: 300, duration: '1m' },   // the email lands
        { target: 800, duration: '1m' },   // everyone
        { target: 800, duration: '1m' },   // sustained
        { target: 0, duration: '30s' },    // tail off
      ],
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<500', 'p(99)<1000'],
  },
};

export default function () {
  const n = 1 + Math.floor(Math.random() * STUDENT_COUNT);
  const studentNumber = `S${String(n).padStart(6, '0')}`;

  const res = http.get(`${BASE_URL}/students/${studentNumber}/dashboard`, {
    tags: { endpoint: 'dashboard' },
  });

  check(res, {
    'dashboard 200': (r) => r.status === 200,
    'has results': (r) => r.status === 200 && r.json('results').length > 0,
  });
}
