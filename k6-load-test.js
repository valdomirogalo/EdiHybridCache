import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate, Trend } from 'k6/metrics';

// ─── Configuration ──────────────────────────────────────────────
export const options = {
  stages: [
    { duration: '10s', target: 1000 },  // Ramp-up: 0 → 1,000 VUs in 10s
    { duration: '20s', target: 5000 },  // Ramp-up: 1,000 → 5,000 VUs in 20s
    { duration: '30s', target: 5000 },  // Plateau: 5,000 VUs sustained
    { duration: '10s', target: 0 },     // Ramp-down: 5,000 → 0 VUs
  ],
  thresholds: {
    http_req_duration: ['p(95)<2000'],   // 95% of requests in < 2s
    http_req_failed: ['rate<0.10'],       // < 10% de falha
  },
};

// ─── Custom metrics ─────────────────────────────────────────────
const setDuration = new Trend('set_duration_ms');
const getDuration = new Trend('get_duration_ms');
const deleteDuration = new Trend('delete_duration_ms');
const setSuccessRate = new Rate('set_success');
const getSuccessRate = new Rate('get_success');
const deleteSuccessRate = new Rate('delete_success');
const getCacheHitRate = new Rate('get_cache_hit');
const getCacheMissRate = new Rate('get_cache_miss');

const BASE_URL = 'http://localhost:5060';

// ─── Payload generator ───────────────────────────────────────────
function randomPayload() {
  const sizes = ['small', 'medium'];
  const size = sizes[Math.floor(Math.random() * sizes.length)];
  const payloads = {
    small: { value: `data-${Math.random().toString(36).substring(2, 10)}` },
    medium: { value: 'x'.repeat(1000) + Math.random().toString(36).substring(2, 10) },
  };
  return { body: JSON.stringify(payloads[size]), size, key: `k6-${__VU}-${__ITER}-${Date.now()}` };
}

// ─── Main Test ───────────────────────────────────────────────────
export default function () {
  const payload = randomPayload();
  const key = payload.key;

  // 1. SET — Armazena valor no cache
  group('SetAsync', () => {
    const res = http.post(
      `${BASE_URL}/cache/${key}`,
      payload.body,
      { headers: { 'Content-Type': 'application/json' } }
    );
    const passed = check(res, {
      'Set status 200': (r) => r.status === 200,
      'Set has key': (r) => r.json('key') === key,
    });
    setDuration.add(res.timings.duration);
    setSuccessRate.add(passed);
  });

  // 2. GET — Read the newly written value (L1 Hit expected)
  group('GetAsync (Hit)', () => {
    const res = http.get(`${BASE_URL}/cache/${key}`);
    const isHit = res.status === 200;
    const isMiss = res.status === 404;
    const passed = check(res, {
      'Get status 200 or 404': (r) => isHit || isMiss,
    });
    getDuration.add(res.timings.duration);
    getSuccessRate.add(passed);
    getCacheHitRate.add(isHit);
    getCacheMissRate.add(isMiss);
  });

  // 3. GET — Second read (should be L1 Hit)
  group('GetAsync (L1 Hit)', () => {
    const res = http.get(`${BASE_URL}/cache/${key}`);
    const isHit = res.status === 200;
    check(res, {
      'L1 Hit status 200': () => isHit,
    });
    getDuration.add(res.timings.duration);
    getSuccessRate.add(isHit);
    getCacheHitRate.add(isHit);
  });

  // 4. INVALIDATE LOCAL — Clears L1
  group('InvalidateLocal', () => {
    const res = http.post(`${BASE_URL}/cache/invalidate-local/${key}`);
    const passed = check(res, {
      'Invalidate status 200': (r) => r.status === 200,
      'Invalidate action': (r) => r.json('action') === 'l1_invalidated',
    });
  });

  // 5. DELETE — Remove from cache
  group('RemoveAsync', () => {
    const res = http.del(`${BASE_URL}/cache/${key}`, null, { headers: {} });
    const passed = check(res, {
      'Delete status 204': (r) => r.status === 204,
    });
    deleteDuration.add(res.timings.duration);
    deleteSuccessRate.add(passed);
  });

  // Small pause between iterations to avoid overload
  sleep(0.1);
}
