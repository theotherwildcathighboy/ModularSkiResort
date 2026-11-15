// Evaluate a JS expression in one Gameface CDP page chosen by URL substring (default: the in-game Paradox Mods view).
// The gameface MCP tools only reach the main game UI; the Paradox Mods screen is a second view (assetdb://modsui/index.html).
// Usage: node tools/testbridge/cdp-eval.mjs [--page modsui] [--await] "<expression>"
//        node tools/testbridge/cdp-eval.mjs --file script.js
import { readFileSync } from 'node:fs';

const args = process.argv.slice(2);
let page = 'modsui', awaitPromise = false, expr = null;
for (let i = 0; i < args.length; i++) {
  if (args[i] === '--page') page = args[++i];
  else if (args[i] === '--await') awaitPromise = true;
  else if (args[i] === '--file') expr = readFileSync(args[++i], 'utf8');
  else expr = args[i];
}
if (!expr) { console.error('no expression'); process.exit(2); }

const list = await (await fetch('http://127.0.0.1:9444/json')).json();
const target = list.find(p => p.url.includes(page));
if (!target) { console.error('no page matching', page, list.map(p => p.url)); process.exit(3); }

const ws = new WebSocket(target.webSocketDebuggerUrl);
const timer = setTimeout(() => { console.error('timeout'); process.exit(4); }, 30000);
ws.onopen = () => ws.send(JSON.stringify({ id: 1, method: 'Runtime.evaluate',
  params: { expression: expr, returnByValue: true, awaitPromise } }));
ws.onmessage = ev => {
  const m = JSON.parse(ev.data);
  if (m.id !== 1) return;
  clearTimeout(timer);
  const r = m.result || {};
  if (r.exceptionDetails) console.log('EXCEPTION', JSON.stringify(r.exceptionDetails).slice(0, 2000));
  else console.log(typeof r.result?.value === 'string' ? r.result.value : JSON.stringify(r.result?.value ?? r.result));
  ws.close(); process.exit(0);
};
ws.onerror = e => { console.error('ws error', e.message); process.exit(5); };
