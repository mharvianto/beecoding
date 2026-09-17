#!/usr/bin/env node
// Concurrency stress test for the clangd LSP bridge (WebSocket GET /lsp/cpp).
//
// Usage:
//   node scripts/lsp-stress-test.mjs [--n 8] [--base http://127.0.0.1:5048]
//                                    [--times 3] [--duration 30s] [--gap 500]
//
// Needs `npm install` in this scripts/ folder first (uses the `ws` package —
// Node's built-in WebSocket has no way to attach a Cookie header for auth,
// unlike a real browser, which sends it automatically on the WS handshake).
//
// LSP only needs an authenticated cookie (no board membership), so accounts
// register but don't join a board. Each simulated user opens one WebSocket
// session, does initialize -> didOpen -> one completion request, and measures
// each round-trip. LspOptions.MaxConcurrent (default 4) caps how many real
// clangd processes run at once — sessions beyond that wait up to 2s for a
// slot, then get closed as "server busy"; --n above 4 is the interesting case,
// since that's the behavior this script is really here to verify.

import { WebSocket } from 'ws';

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
const WS_BASE = BASE.replace(/^http/, 'ws');
const N = parseInt(args.n, 10) || 8;
const TIMES = args.times ? parseInt(args.times, 10) : null;
const DURATION_MS = args.duration ? parseDuration(args.duration) : null;
const GAP_MS = args.gap !== undefined ? parseInt(args.gap, 10) : 500;
if (TIMES && DURATION_MS) throw new Error('pass either --times or --duration, not both');

const SAMPLE_CPP = `#include <vector>\n#include <string>\n\nint main() {\n  std::vector<int> v;\n  v.\n  return 0;\n}\n`;
// position after "v." on line 5 (0-indexed line 4) — a real completion trigger point.
const COMPLETION_POS = { line: 4, character: 4 };

function percentile(sorted, p) {
  if (!sorted.length) return 0;
  return sorted[Math.min(sorted.length - 1, Math.floor(p * sorted.length))];
}

function stat(label, values) {
  const v = values.filter((x) => x != null).sort((a, b) => a - b);
  if (!v.length) return `${label}: n/a`;
  const sum = v.reduce((a, b) => a + b, 0);
  return `${label} min=${v[0]} p50=${percentile(v, 0.5)} p95=${percentile(v, 0.95)} max=${v.at(-1)} avg=${Math.round(sum / v.length)}`;
}

function summarize(label, samples) {
  console.log(`\n-- ${label} --`);
  const outcomeCounts = {};
  for (const s of samples) outcomeCounts[s.outcome] = (outcomeCounts[s.outcome] || 0) + 1;
  console.log(`  sessions: ${samples.length}`, outcomeCounts);
  const completed = samples.filter((s) => s.outcome === 'completed');
  console.log('  ' + stat('connect ms  ', completed.map((s) => s.connectMs)));
  console.log('  ' + stat('initialize ms', completed.map((s) => s.initMs)));
  console.log('  ' + stat('completion ms', completed.map((s) => s.completionMs)));
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
  const email = `lspstress${Date.now()}_${i}@test.local`;
  const password = 'stresstest123';
  const { res: rr, setCookie: rc } = await req('POST', '/api/auth/register', null, {
    email, password, displayName: `LSP Stress ${i}`, role: 'Student',
  });
  if (!rr.ok) throw new Error(`register failed for user ${i}: HTTP ${rr.status}`);
  return { email, password, cookie: cookieFromSetCookie(rc) };
}

async function deleteUser(u) {
  try { await req('DELETE', '/api/auth/account', u.cookie, { password: u.password, deleteOwnedBoards: true }); }
  catch { /* best-effort cleanup */ }
}

async function checkLspEnabled(cookie) {
  const { json } = await req('GET', '/api/lsp/enabled', cookie);
  return json?.enabled === true;
}

function lspSession(u) {
  return new Promise((resolvePromise) => {
    const t0 = performance.now();
    let connectMs = null, initMs = null, completionMs = null, uri = null, id = 0, finished = false;
    const pending = new Map();

    const finish = (outcome) => {
      if (finished) return;
      finished = true;
      clearTimeout(timeout);
      try { ws.close(); } catch { /* ignore */ }
      resolvePromise({ ok: outcome === 'completed', outcome, connectMs, initMs, completionMs, totalMs: Math.round(performance.now() - t0) });
    };
    const timeout = setTimeout(() => finish('timeout'), 15000);

    const ws = new WebSocket(`${WS_BASE}/lsp/cpp?lang=cpp&style=LLVM`, { headers: { Cookie: u.cookie } });

    ws.on('unexpected-response', (_req, res) => finish(`http-${res.statusCode}`));
    ws.on('error', (e) => finish(`ws-error: ${e.message}`));
    ws.on('close', (code, reasonBuf) => {
      if (finished) return;
      const reason = reasonBuf?.toString() || '';
      finish(reason.includes('busy') || code === 1008 ? 'server-busy' : `closed-${code}`);
    });

    ws.on('message', async (data) => {
      let m;
      try { m = JSON.parse(data.toString()); } catch { return; }

      if (m.beecoding === 'ready') {
        connectMs = Math.round(performance.now() - t0);
        uri = m.uri;
        const initId = ++id;
        pending.set(initId, performance.now());
        ws.send(JSON.stringify({
          jsonrpc: '2.0', id: initId, method: 'initialize',
          params: {
            processId: null, rootUri: m.rootUri,
            capabilities: { textDocument: { completion: { completionItem: { snippetSupport: true } }, hover: {} } },
          },
        }));
        return;
      }

      if (m.id != null && pending.has(m.id)) {
        const rtMs = Math.round(performance.now() - pending.get(m.id));
        pending.delete(m.id);

        if (initMs === null) {
          initMs = rtMs;
          ws.send(JSON.stringify({ jsonrpc: '2.0', method: 'initialized', params: {} }));
          ws.send(JSON.stringify({
            jsonrpc: '2.0', method: 'textDocument/didOpen',
            params: { textDocument: { uri, languageId: 'cpp', version: 1, text: SAMPLE_CPP } },
          }));
          await new Promise((r) => setTimeout(r, 300));   // let clangd index the file first
          const compId = ++id;
          pending.set(compId, performance.now());
          ws.send(JSON.stringify({
            jsonrpc: '2.0', id: compId, method: 'textDocument/completion',
            params: { textDocument: { uri }, position: COMPLETION_POS, context: { triggerKind: 1 } },
          }));
          return;
        }

        completionMs = rtMs;
        finish('completed');
      }
    });
  });
}

async function loopBatches(label, fireBatch) {
  const deadline = DURATION_MS ? Date.now() + DURATION_MS : null;
  const iterations = TIMES ?? (deadline ? Infinity : 1);
  const all = [];
  const overallStart = performance.now();
  let iter = 0;
  while (iter < iterations && !(deadline && Date.now() >= deadline)) {
    iter++;
    const t0 = performance.now();
    const results = await fireBatch();
    const dt = Math.round(performance.now() - t0);
    const ok = results.filter((r) => r.ok).length;
    console.log(`  [iter ${iter}] ${ok}/${results.length} completed, batch wall=${dt}ms`);
    all.push(...results);

    const done = iter >= iterations || (deadline && Date.now() >= deadline);
    if (!done && dt < GAP_MS) await new Promise((r) => setTimeout(r, GAP_MS - dt));
  }
  console.log(`Total: ${iter} iteration(s), ${Math.round(performance.now() - overallStart)}ms wall clock.`);
  summarize(label, all);
}

async function main() {
  console.log(`LSP stress test: N=${N} base=${BASE}`);

  const created = [];
  try {
    console.log('Registering 1 probe account to check the LSP bridge is on...');
    const probe = await makeUser('probe');
    created.push(probe);
    if (!await checkLspEnabled(probe.cookie)) {
      console.error('\nLsp:Enabled is off on this server (Admin panel -> Reports, or Lsp:Enabled config) — nothing to test.');
      return;
    }

    console.log(`Registering ${N} throwaway accounts...`);
    const settled = await Promise.allSettled(Array.from({ length: N }, (_, i) => makeUser(i)));
    const users = [];
    for (const s of settled) {
      if (s.status === 'fulfilled') { created.push(s.value); users.push(s.value); }
      else console.error('  registration failed:', s.reason?.message || s.reason);
    }
    if (!users.length) throw new Error('no accounts registered successfully — aborting');
    console.log(`Ready: ${users.length}/${N} accounts.`);

    const iterLabel = TIMES ? ` (${TIMES}x)` : DURATION_MS ? ` (for ${DURATION_MS}ms)` : '';
    console.log(`\nOpening ${users.length} concurrent LSP sessions${iterLabel} (MaxConcurrent default is 4 — expect "server-busy" above that)...`);
    await loopBatches('LSP session (connect -> initialize -> completion)', () => Promise.all(users.map((u) => lspSession(u))));
  } finally {
    console.log(`\nCleaning up ${created.length} throwaway accounts...`);
    await Promise.all(created.map(deleteUser));
    console.log('Done.');
  }
}

main().catch((e) => { console.error(e); process.exit(1); });
