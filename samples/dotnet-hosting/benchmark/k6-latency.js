// Latency benchmark for the hosting articles — run against each deployed platform:
//   k6 run -e TARGET=https://dotnet-samples-production.up.railway.app k6-latency.js
//   k6 run -e TARGET=https://jorgenhoc-hosting-sample.onrender.com k6-latency.js
//
// Fixed arrival rate on purpose: comparing latency distributions at the same load,
// NOT max throughput — free/hobby tiers throttle CPU, and client location dominates
// anyway. Numbers measure the path from the machine running k6 to the provider's
// region; disclose both when publishing results.
import http from 'k6/http';
import { check } from 'k6';

export const options = {
  scenarios: {
    steady: {
      executor: 'constant-arrival-rate',
      rate: 30,
      timeUnit: '1s',
      duration: '60s',
      preAllocatedVUs: 20,
      maxVUs: 60,
    },
  },
  summaryTrendStats: ['min', 'med', 'p(95)', 'p(99)', 'max'],
};

export default function () {
  const res = http.get(`${__ENV.TARGET}/`);
  check(res, { 'status 200': (r) => r.status === 200 });
}
