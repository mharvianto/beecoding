#!/usr/bin/env node
// Concurrency stress test for POST /api/run and POST /api/problems/:id/submit.
//
// Usage:
//   node scripts/stress-test.mjs [--n 20] [--mode both|run|submit] [--base http://127.0.0.1:5048] [--join DEMO01]
//                                [--times 5] [--duration 60s] [--gap ms]
//
// Registers N throwaway student accounts once, joins the given board by its join
// code, then fires N concurrent requests per mode from those N distinct sessions —
// either once, --times K times, or repeatedly until --duration elapses (accepts a
// plain number of seconds, or e.g. 30s / 5m / 1h). Reports aggregate latency stats
// across every iteration, then deletes the throwaway accounts. --times and
// --duration are mutually exclusive; neither given means one shot (the old
// behavior).
//
// Pacing is per-user, not per-batch: SubmissionsController.Submit enforces TWO
// separate per-user limits — RateLimiter (Judge:RateLimitMs, default 1.5s, shared
// with Run) AND SubmitCooldown (a hard-coded 10s, submit-specific, stricter). Each
// user tracks their own last-issue time and waits until --gap has elapsed since
// THEIR OWN previous request of that kind before firing the next one — independent
// of how long other users (or that user's own previous grading/poll) took, since
// pacing off the whole batch's wall-clock is bounded by the *slowest* straggler and
// can silently undershoot a fast user's own cooldown window. Defaults to 1800ms for
// Run and 10500ms for Submit unless --gap overrides both; set it lower than the
// real window if you want to deliberately test how the app behaves when a user
// hammers it too fast (expect 429s — that's the rate limit working, not a bug).

const args = {};
{
  const argv = process.argv.slice(2);
  for (let i = 0; i < argv.length; i++) {
    const m = argv[i].match(/^--(\w+)(?:=(.*))?$/);
    if (!m) continue;
    if (m[2] !== undefined) { args[m[1]] = m[2]; continue; }
    const next = argv[i + 1];
    if (next !== undefined && !next.startsWith('--')) { args[m[1]] = next; i++; }
    else args[m[1]] = true;
  }
}

function parseDuration(s) {
  const m = String(s).match(/^(\d+(?:\.\d+)?)(ms|s|m|h)?$/);
  if (!m) throw new Error(`invalid --duration "${s}" (examples: 30s, 5m, 1h, or a plain number of seconds)`);
  const mult = { ms: 1, s: 1000, m: 60_000, h: 3_600_000 }[m[2] || 's'];
  return Math.round(parseFloat(m[1]) * mult);
}

const BASE = args.base || process.env.STRESS_BASE || 'http://127.0.0.1:5048';
const N = parseInt(args.n, 10) || 10;
const MODE = args.mode || 'both';   // 'run' | 'submit' | 'both'
const JOIN_CODE = (args.join || 'DEMO01').toUpperCase();
const TIMES = args.times ? parseInt(args.times, 10) : null;
const DURATION_MS = args.duration ? parseDuration(args.duration) : null;
const GAP_OVERRIDE_MS = args.gap !== undefined ? parseInt(args.gap, 10) : null;
const RUN_GAP_MS = GAP_OVERRIDE_MS ?? 1800;       // just over RateLimiter's 1.5s
const SUBMIT_GAP_MS = GAP_OVERRIDE_MS ?? 10_500;  // just over SubmitCooldown's hard 10s
if (TIMES && DURATION_MS) throw new Error('pass either --times or --duration, not both');

const SAMPLE_CODE = `#include <stdio.h>\nint main(void) { printf("%d\\n", 2 + 2); int n=1000000; while(n-- > 0); return 0; }\n`;

function percentile(sorted, p) {
  if (!sorted.length) return 0;
  return sorted[Math.min(sorted.length - 1, Math.floor(p * sorted.length))];
}

function summarize(label, samples) {
  const ok = samples.filter((s) => s.ok);
  const lat = ok.map((s) => s.ms).sort((a, b) => a - b);
  const sum = lat.reduce((a, b) => a + b, 0);
  console.log(`\n-- ${label} --`);
  console.log(`  requests: ${samples.length}, ok: ${ok.length}, failed/rate-limited: ${samples.length - ok.length}`);
  if (lat.length) {
    console.log(`  latency ms  min=${lat[0]}  p50=${percentile(lat, 0.5)}  p95=${percentile(lat, 0.95)}  max=${lat.at(-1)}  avg=${Math.round(sum / lat.length)}`);
  }
  const statusCounts = {};
  for (const s of samples) statusCounts[s.status] = (statusCounts[s.status] || 0) + 1;
  console.log('  status codes:', statusCounts);
}

async function req(method, path, cookie, body) {
  const res = await fetch(`${BASE}${path}`, {
    method,
    headers: { 'Content-Type': 'application/json', ...(cookie ? { Cookie: cookie } : {}) },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  const setCookie = res.headers.get('set-cookie');
  const text = await res.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { /* not json */ }
  return { res, json, setCookie };
}

const cookieFromSetCookie = (setCookie) => (setCookie ? setCookie.split(';')[0] : null);

async function makeUser(i) {
  const email = `stress${Date.now()}_${i}@test.local`;
  const password = 'stresstest123';
  const { res: rr, setCookie: rc } = await req('POST', '/api/auth/register', null, {
    email, password, displayName: `Stress ${i}`, role: 'Student',
  });
  if (!rr.ok) throw new Error(`register failed for user ${i}: HTTP ${rr.status}`);
  const cookie = cookieFromSetCookie(rc);
  // The account row exists in the DB from here on, whatever happens next — attach it to
  // any error we throw below so the caller can still clean it up even though join failed.
  const account = { email, password, cookie };

  const { res: jr, json: board } = await req('POST', '/api/boards/join', cookie, { code: JOIN_CODE });
  if (!jr.ok) throw Object.assign(new Error(`join failed for user ${i}: HTTP ${jr.status}`), { account });

  return { ...account, boardSlug: board.slug };
}

async function deleteUser(u) {
  try { await req('DELETE', '/api/auth/account', u.cookie, { password: u.password, deleteOwnedBoards: true }); }
  catch { /* best-effort cleanup */ }
}

async function firstProblem(cookie, boardSlug) {
  const { json } = await req('GET', `/api/boards/${boardSlug}/problems`, cookie);
  if (!json?.length) throw new Error(`board "${boardSlug}" has no problems to test against`);
  return json[0];
}

async function runOnce(u, problem) {
  const t0 = performance.now();
  const { res } = await req('POST', '/api/run', u.cookie, { language: 'c', code: SAMPLE_CODE, stdin: '', problemId: problem.id });
  return { ok: res.ok, status: res.status, ms: Math.round(performance.now() - t0) };
}

async function submitOnce(u, problem) {
  const t0 = performance.now();
  const { res, json } = await req('POST', `/api/problems/${problem.id}/submit`, u.cookie, {
    code: SAMPLE_CODE, language: 'c', localDay: new Date().toISOString().slice(0, 10),
  });
  if (!res.ok || !json?.submissionId) return { ok: false, status: res.status, ms: Math.round(performance.now() - t0) };

  for (let i = 0; i < 150; i++) {
    await new Promise((r) => setTimeout(r, 200));
    const { json: s } = await req('GET', `/api/submissions/${json.submissionId}`, u.cookie);
    if (s?.status === 'Done') return { ok: true, status: res.status, ms: Math.round(performance.now() - t0), verdict: s.verdict };
  }
  return { ok: false, status: res.status, ms: Math.round(performance.now() - t0), timedOut: true };
}

// Fires `fireOne(u)` for every user once, --times K times, or until --duration
// elapses (whichever was passed; neither means one shot), printing a one-line
// result per iteration.
//
// Pacing is per-user, not per-batch: each user tracks their own last-issue time
// and waits until `gapMs` has elapsed since THEIR OWN previous request before
// firing the next one. Pacing off the whole batch's wall-clock (as this used to)
// was wrong for submit — that clock is bounded by the *slowest* straggler (e.g. a
// user whose grading took longer to poll for), so a fast user's next request
// could still land inside their own per-user cooldown window even though the
// batch "as a whole" took long enough. That's exactly what produced mass 429s
// under load: most of a batch failing even though those users hadn't raced each
// other at all — both RateLimiter and SubmitCooldown are keyed per user.
async function loopBatches(label, users, fireOne, gapMs) {
  const deadline = DURATION_MS ? Date.now() + DURATION_MS : null;
  const iterations = TIMES ?? (deadline ? Infinity : 1);
  const lastIssueAt = new Map(users.map((u) => [u, -Infinity]));
  const all = [];
  const overallStart = performance.now();
  let iter = 0;
  while (iter < iterations && !(deadline && Date.now() >= deadline)) {
    iter++;
    const t0 = performance.now();
    const results = await Promise.all(users.map(async (u) => {
      const wait = gapMs - (performance.now() - lastIssueAt.get(u));
      if (wait > 0) await new Promise((r) => setTimeout(r, wait));
      lastIssueAt.set(u, performance.now());
      return fireOne(u);
    }));
    const dt = Math.round(performance.now() - t0);
    const ok = results.filter((r) => r.ok).length;
    console.log(`  [iter ${iter}] ${ok}/${results.length} ok, batch wall=${dt}ms`);
    all.push(...results);
    if (iter >= iterations || (deadline && Date.now() >= deadline)) break;
  }
  console.log(`Total: ${iter} iteration(s), ${Math.round(performance.now() - overallStart)}ms wall clock.`);
  summarize(label, all);
}

async function main() {
  console.log(`Stress test: N=${N} mode=${MODE} base=${BASE} join=${JOIN_CODE}`);

  // Declared before the try so a partial failure still leaves us a list to clean up in
  // finally — Promise.allSettled rather than Promise.all so one failure doesn't lose
  // track of the others. `created` is everyone whose account row exists in the DB
  // (needs deleting) even if their join failed and they didn't make it into `users`
  // (usable for the test phases below).
  const created = [];
  const users = [];
  try {
    console.log(`Registering ${N} throwaway accounts and joining the board...`);
    const settled = await Promise.allSettled(Array.from({ length: N }, (_, i) => makeUser(i)));
    for (const s of settled) {
      if (s.status === 'fulfilled') { created.push(s.value); users.push(s.value); }
      else {
        console.error('  registration/join failed:', s.reason?.message || s.reason);
        if (s.reason?.account) created.push(s.reason.account);
      }
    }
    if (!users.length) throw new Error('no accounts registered successfully — aborting');
    console.log(`Ready: ${users.length}/${N} users joined "${users[0].boardSlug}".`);

    const problem = await firstProblem(users[0].cookie, users[0].boardSlug);
    console.log(`Testing against problem "${problem.title}" (id ${problem.id}).`);

    const iterLabel = TIMES ? ` (${TIMES}x)` : DURATION_MS ? ` (for ${DURATION_MS}ms)` : '';

    if (MODE === 'run' || MODE === 'both') {
      console.log(`\nFiring ${users.length} concurrent POST /api/run requests${iterLabel}...`);
      await loopBatches('POST /api/run', users, (u) => runOnce(u, problem), RUN_GAP_MS);
    }

    if (MODE === 'submit' || MODE === 'both') {
      if (MODE === 'both') {
        // loopBatches' per-user pacing starts fresh (each phase tracks its own
        // lastIssueAt map) — it has no memory of the Run phase's timestamps, so this
        // explicit wait is still needed before the first Submit iteration, or it'd
        // 429 against each user's own just-finished Run (and against SubmitCooldown,
        // the stricter 10s one — see the file header).
        console.log('\nWaiting out the per-user cooldowns before the submit batch...');
        await new Promise((r) => setTimeout(r, SUBMIT_GAP_MS));
      }
      console.log(`\nFiring ${users.length} concurrent POST /api/problems/:id/submit requests${iterLabel} (polling to grade)...`);
      await loopBatches('POST /api/problems/:id/submit (submit -> graded)', users, (u) => submitOnce(u, problem), SUBMIT_GAP_MS);
    }
  } finally {
    console.log(`\nCleaning up ${created.length} throwaway accounts...`);
    await Promise.all(created.map(deleteUser));
    console.log('Done.');
  }
}

main().catch((e) => { console.error(e); process.exit(1); });
