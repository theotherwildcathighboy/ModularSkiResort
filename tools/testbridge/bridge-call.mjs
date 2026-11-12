#!/usr/bin/env node
// bridge-call.mjs: one bridge call from a script, over the same path the gameface MCP's game_eval uses (Chrome DevTools Protocol on the
// Gameface debug port, Runtime.evaluate). For runners such as tools/loadcheck.ps1. Node 22+ (global fetch and WebSocket), no packages.
//
// Gameface does not await a promise for Runtime.evaluate (README "Practical limits": game_eval returns {} for a promise), so an async
// expression is parked on a window slot and polled until it settles.
//
// Arguments are base64 (UTF-8) so PowerShell 5.1 cannot strip the quotes of a JSON string on the way to a native command.
//   --op <b64 json>        engine.call("skitest.cmd", json); prints the bridge's reply (a JSON string) as is.
//   --js <b64 expression>  evaluates the expression (a value or a promise); prints {"ok":true,"value":...} or {"ok":false,"error":...}.
//   --inject <file>        evaluates the file (tools/testbridge/helpers.js) unless window.__st already exists; prints {"ok":true,"injected":bool}.
//   --timeout <s>          how long to wait for a promise (default 60).
//   --host, --port         default localhost:9444 (GAMEFACE_HOST / GAMEFACE_PORT).
// Exit: 0 ok, 1 the call failed or timed out, 2 the game's debug port does not answer.
import fs from 'node:fs';

const args = process.argv.slice(2);
const opt = (name, def) => { const i = args.indexOf('--' + name); return i >= 0 ? args[i + 1] : def; };
const b64 = (s) => (s == null ? null : Buffer.from(s, 'base64').toString('utf8'));
const host = opt('host', process.env.GAMEFACE_HOST || 'localhost');
const port = opt('port', process.env.GAMEFACE_PORT || '9444');
const timeoutMs = 1000 * Number(opt('timeout', '60'));

function out(obj, code) {
  process.stdout.write((typeof obj === 'string' ? obj : JSON.stringify(obj)) + '\n');
  process.exit(code);
}

async function connect() {
  let list;
  try {
    const res = await fetch(`http://${host}:${port}/json/list`, { signal: AbortSignal.timeout(5000) });
    list = await res.json();
  } catch (e) {
    out({ ok: false, error: `game debug port ${host}:${port} does not answer: ${e.message}` }, 2);
  }
  // Build the socket URL ourselves: Gameface returns webSocketDebuggerUrl relative to the request path (the gameface MCP, src/cdp.ts).
  const page = (Array.isArray(list) ? list : []).find((t) => t && t.type === 'page' && t.id != null);
  if (!page) out({ ok: false, error: `no page target at ${host}:${port}` }, 2);
  const ws = new WebSocket(`ws://${host}:${port}/devtools/page/${page.id}`);
  await new Promise((resolve, reject) => { ws.onopen = resolve; ws.onerror = () => reject(new Error('WebSocket error')); });
  let next = 1;
  const pending = new Map();
  ws.onmessage = (m) => {
    const msg = JSON.parse(typeof m.data === 'string' ? m.data : Buffer.from(m.data).toString('utf8'));
    if (msg.id && pending.has(msg.id)) { pending.get(msg.id)(msg); pending.delete(msg.id); }
  };
  const evaluate = (expression) => new Promise((resolve, reject) => {
    const id = next++;
    const timer = setTimeout(() => { pending.delete(id); reject(new Error('Runtime.evaluate timed out')); }, 15000);
    pending.set(id, (msg) => {
      clearTimeout(timer);
      if (msg.error) return reject(new Error(msg.error.message || JSON.stringify(msg.error)));
      const r = msg.result || {};
      if (r.exceptionDetails) return reject(new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text || 'exception'));
      resolve(r.result ? r.result.value : undefined);
    });
    ws.send(JSON.stringify({ id, method: 'Runtime.evaluate', params: { expression, returnByValue: true } }));
  });
  return { ws, evaluate };
}

/** Parks the expression's settled value on a window slot and polls it. The value comes back as a string (JSON for non-strings). */
async function awaitExpression(evaluate, expression) {
  const slot = `__bc${Date.now()}${Math.floor(Math.random() * 1e6)}`;
  await evaluate(`(() => { window.${slot} = { done: false }; Promise.resolve().then(() => (${expression})).then(` +
    `v => { window.${slot} = { done: true, v: typeof v === 'string' ? v : JSON.stringify(v) }; }, ` +
    `e => { window.${slot} = { done: true, err: String(e && e.stack || e) }; }); return 1; })()`);
  const t0 = Date.now();
  while (Date.now() - t0 < timeoutMs) {
    await new Promise((r) => setTimeout(r, 150));
    const s = await evaluate(`JSON.stringify(window.${slot} === undefined ? { lost: true } : window.${slot})`);
    const st = JSON.parse(s);
    if (st.lost) throw new Error('the page reloaded while waiting (window slot gone)');
    if (st.done) {
      await evaluate(`delete window.${slot}`);
      if (st.err) throw new Error(st.err);
      return st.v;
    }
  }
  throw new Error(`no result within ${timeoutMs / 1000} s`);
}

const { ws, evaluate } = await connect();
try {
  if (opt('op') != null) {
    const json = b64(opt('op'));
    JSON.parse(json); // fail early on a malformed request
    const reply = await awaitExpression(evaluate, `engine.call("skitest.cmd", ${JSON.stringify(json)})`);
    ws.close();
    out(reply, 0);
  } else if (opt('js') != null) {
    const v = await awaitExpression(evaluate, b64(opt('js')));
    ws.close();
    let value = v;
    try { value = JSON.parse(v); } catch { /* a plain string */ }
    out({ ok: true, value }, 0);
  } else if (opt('inject') != null) {
    const has = await evaluate(`typeof window.__st === 'function'`);
    if (!has) await evaluate(fs.readFileSync(opt('inject'), 'utf8') + '\n;1');
    ws.close();
    out({ ok: true, injected: !has }, 0);
  } else {
    ws.close();
    out({ ok: false, error: 'pass --op, --js or --inject' }, 1);
  }
} catch (e) {
  try { ws.close(); } catch { /* closing */ }
  out({ ok: false, error: e.message }, 1);
}
