import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate, Trend } from 'k6/metrics';

// ─── Expected status codes ────────────────────────────────────────
// 404 is expected (L2 Miss), so it won't count toward http_req_failed
http.setResponseCallback(http.expectedStatuses(200, 204, 404));

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
const l1HitRate = new Rate('l1_hit');
const l2HitRate = new Rate('l2_hit');
const l2MissRate = new Rate('l2_miss');

const BASE_URL = 'http://localhost:5000';

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
  const missKey = `k6-miss-${__VU}-${__ITER}`;

  // ═══════════════════════════════════════════════════════════════
  //  1. SET — Write to L1 (memory) + L2 (Redis)
  // ═══════════════════════════════════════════════════════════════
  group('1 - SetAsync (L1 + L2)', () => {
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

  // ═══════════════════════════════════════════════════════════════
  //  2. GET (L1 Hit) — Key was just written to L1, read from memory
  //     Expected: 200, < 2ms
  // ═══════════════════════════════════════════════════════════════
  group('2 - GetAsync (L1 Hit)', () => {
    const res = http.get(`${BASE_URL}/cache/${key}`);
    const isHit = res.status === 200;
    check(res, {
      'L1 Hit status 200': () => isHit,
    });
    getDuration.add(res.timings.duration);
    getSuccessRate.add(isHit);
    l1HitRate.add(isHit);
  });

  // ═══════════════════════════════════════════════════════════════
  //  3. INVALIDATE LOCAL — Clears L1 only; L2 (Redis) still has data
  // ═══════════════════════════════════════════════════════════════
  group('3 - InvalidateLocal (clear L1)', () => {
    const res = http.post(`${BASE_URL}/cache/invalidate-local/${key}`);
    const passed = check(res, {
      'Invalidate status 200': (r) => r.status === 200,
      'Invalidate action': (r) => r.json('action') === 'l1_invalidated',
    });
  });

  // ═══════════════════════════════════════════════════════════════
  //  4. GET (L2 Hit) — L1 was cleared, falls back to Redis (L2)
  //     Expected: 200, < 10ms (Redis round-trip)
  // ═══════════════════════════════════════════════════════════════
  group('4 - GetAsync (L2 Hit)', () => {
    const res = http.get(`${BASE_URL}/cache/${key}`);
    const isHit = res.status === 200;
    check(res, {
      'L2 Hit status 200': () => isHit,
    });
    getDuration.add(res.timings.duration);
    getSuccessRate.add(isHit);
    l2HitRate.add(isHit);
  });

  // ═══════════════════════════════════════════════════════════════
  //  5. DELETE — Remove from L1 + L2 + publish invalidation
  // ═══════════════════════════════════════════════════════════════
  group('5 - RemoveAsync (L1 + L2)', () => {
    const res = http.del(`${BASE_URL}/cache/${key}`, null, { headers: {} });
    const passed = check(res, {
      'Delete status 204': (r) => r.status === 204,
    });
    deleteDuration.add(res.timings.duration);
    deleteSuccessRate.add(passed);
  });

  // ═══════════════════════════════════════════════════════════════
  //  6. GET (L2 Miss) — Key was deleted; neither L1 nor L2 has it
  //     Expected: 404
  // ═══════════════════════════════════════════════════════════════
  group('6 - GetAsync (L2 Miss)', () => {
    const res = http.get(`${BASE_URL}/cache/${missKey}`);
    const isMiss = res.status === 404;
    check(res, {
      'L2 Miss status 404': () => isMiss,
    });
    getDuration.add(res.timings.duration);
    getSuccessRate.add(isMiss);
    l2MissRate.add(isMiss);
  });

  // Small pause between iterations to avoid overload
  sleep(0.1);
}
