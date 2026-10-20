/*
 * tools/test-hub-layout.js - the "never a scrollbar" layout test (T114, 2026-10-20; docs/research/ui/hub-round-t114-2026-10-20.md).
 * Written for this repo (MIT). Deploys nothing, writes nothing outside --shots=<dir>. No npm package: Node's own WebSocket (Node 22)
 * talks the DevTools protocol to a headless Chrome (else Edge) started here with a throw-away profile.
 *
 * Starts the harness (harness/serve.js on a free port), opens it at the game's common window sizes - 1920x1080, 2560x1440,
 * 3840x2160 - with the game's rem rule, and runs harness/layout-check.js (window.SkiLayout.run) there: every view x fold x page x
 * build tab x overlay at text scale 1.0 and 1.25 (both CSS variables, as the game's applyTextScale sets them). FAILS (exit 1) on any
 * case whose content overflows its box (the Scrollable would show its bar), a header that does not fit, or lodge chips on the
 * Buildings tab. T131 (2026-10-20, DECISIONS 350): the Runs and Lifts views are the one exception: they list every row and may
 * scroll (their list carries class listAll); for them the check FAILS on a drawn pager row instead (harness/layout-check.js).
 *
 * Needs `npm run build` first (it mounts build/SkiResortMod.mjs) and network for the React UMD scripts the harness loads from cdnjs.
 * Usage:   node tools/test-hub-layout.js [--verbose]
 *          node tools/test-hub-layout.js --shots=<dir> [--before]   # the harness screenshots listed in SHOTS / BEFORE
 * The harness is not the game's chrome (harness/README.md): the panel frame is a stand-in, so the report prints the smallest slack
 * (rem of content height left) next to the verdict.
 */
"use strict";
const { spawn } = require("child_process");
const fs = require("fs");
const os = require("os");
const path = require("path");
const net = require("net");
const http = require("http");

const ROOT = path.resolve(__dirname, "..");
const SIZES = [[1920, 1080], [2560, 1440], [3840, 2160]];
const BROWSERS = [
  "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
  "C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe",
  "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
  "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe",
];

function browserPath() {
  for (const b of BROWSERS) if (fs.existsSync(b)) return b;
  throw new Error("test-hub-layout: no Chrome / Edge found (" + BROWSERS.join(", ") + ")");
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
function freePort() {
  return new Promise((res, rej) => {
    const s = net.createServer();
    s.listen(0, "127.0.0.1", () => { const p = s.address().port; s.close(() => res(p)); });
    s.on("error", rej);
  });
}
function getJson(url) {
  return new Promise((res, rej) => {
    http.get(url, (r) => { let d = ""; r.on("data", (c) => (d += c)); r.on("end", () => { try { res(JSON.parse(d)); } catch (e) { rej(e); } }); }).on("error", rej);
  });
}

async function startServer() {
  const port = await freePort();
  const child = spawn(process.execPath, [path.join(ROOT, "harness", "serve.js"), String(port)], { stdio: ["ignore", "pipe", "pipe"] });
  await new Promise((res) => { child.stdout.once("data", res); setTimeout(res, 2000); });
  return { port, child };
}

/** A headless browser with one page, driven over CDP. */
async function startBrowser() {
  const port = await freePort();
  const profile = fs.mkdtempSync(path.join(os.tmpdir(), "hub-layout-"));
  const child = spawn(browserPath(), ["--headless=new", "--disable-gpu", "--hide-scrollbars", "--no-first-run", "--no-default-browser-check",
    "--force-device-scale-factor=1", `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`, "about:blank"], { stdio: "ignore" });
  let page = null;
  for (let i = 0; i < 100 && !page; i++) {
    await sleep(100);
    try { page = (await getJson(`http://127.0.0.1:${port}/json/list`)).find((t) => t.type === "page"); } catch { /* not up yet */ }
  }
  if (!page) { child.kill(); throw new Error("test-hub-layout: the headless browser did not open its DevTools port"); }
  const ws = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
  let id = 0;
  const pending = new Map();
  const events = [];
  ws.onmessage = (m) => {
    const msg = JSON.parse(typeof m.data === "string" ? m.data : m.data.toString());
    if (msg.id && pending.has(msg.id)) { const p = pending.get(msg.id); pending.delete(msg.id); msg.error ? p.rej(new Error(JSON.stringify(msg.error))) : p.res(msg.result); }
    else if (msg.method) events.push(msg);
  };
  const send = (method, params) => new Promise((res, rej) => { const i = ++id; pending.set(i, { res, rej }); ws.send(JSON.stringify({ id: i, method, params: params || {} })); });
  const close = () => { try { ws.close(); } catch { /* ignore */ } child.kill(); setTimeout(() => { try { fs.rmSync(profile, { recursive: true, force: true }); } catch { /* locked */ } }, 500); };
  await send("Page.enable");
  await send("Runtime.enable");
  return { send, close, events };
}

async function open(b, url, w, h) {
  await b.send("Emulation.setDeviceMetricsOverride", { width: w, height: h, deviceScaleFactor: 1, mobile: false });
  await b.send("Page.navigate", { url });
  // Wait for the harness to mount the bundle (its status line names the bundle stamp).
  for (let i = 0; i < 300; i++) {
    await sleep(100);
    const r = await b.send("Runtime.evaluate", { expression: "(document.getElementById('status')||{}).textContent||''", returnByValue: true });
    const s = r.result.value || "";
    if (/^bundle /.test(s)) return;
    if (/^failed/.test(s)) throw new Error("harness: " + s);
  }
  throw new Error("harness did not mount the bundle in 30 s (network for cdnjs? build/ present?)");
}

async function evalAsync(b, expr, timeoutMs) {
  const r = await Promise.race([
    b.send("Runtime.evaluate", { expression: expr, awaitPromise: true, returnByValue: true }),
    sleep(timeoutMs).then(() => { throw new Error("timeout: " + expr.slice(0, 60)); }),
  ]);
  if (r.exceptionDetails) throw new Error("page exception: " + JSON.stringify(r.exceptionDetails).slice(0, 400));
  return r.result.value;
}

const BIG = "Big resort (long name, 30 runs, 12 lifts, 5 unfinished)";
const FIELD = "Field case: 1 complete lift, 4 unfinished";
const ASH = "Ash Flat (runs, lifts, problems)";
const SHOTS = [
  // [file, height, params] - the AFTER set
  ["header-long-name-1080", 1080, { scenario: BIG, view: 0 }],
  ["attraction-field-case-1080", 1080, { scenario: FIELD, view: 0 }],
  ["attraction-fold-terrain-1080", 1080, { scenario: ASH, view: 0, fold: 0 }],
  ["attraction-field-case-1440-fs125", 1440, { scenario: FIELD, view: 0, fs: 1.25 }],
  ["area-list-open-1080", 1080, { scenario: ASH, view: 0, areas: 1 }],
  ["problems-popover-1440", 1440, { scenario: BIG, view: 0, problems: 1 }],
  ["runs-all-1440", 1440, { scenario: BIG, view: 1 }],
  ["lifts-all-1080", 1080, { scenario: BIG, view: 2 }],
  ["guests-fold-tickets-2160", 2160, { scenario: ASH, view: 3, fold: 2 }],
  ["finances-fold-costs-2160-fs125", 2160, { scenario: ASH, view: 4, fold: 0, fs: 1.25 }],
  ["history-1440-fs125", 1440, { scenario: ASH, view: 5, fs: 1.25 }],
  ["buildings-tab-1080", 1080, { scenario: ASH, view: 0, tab: "buildings" }],
  ["lifts-tab-generator-1080", 1080, { scenario: ASH, view: 0, tab: "lifts" }],
  ["lifts-tab-generator-1440-fs125", 1440, { scenario: ASH, view: 0, tab: "lifts", fs: 1.25 }],
  ["buildings-tab-2160-fs125", 2160, { scenario: ASH, view: 0, tab: "buildings", fs: 1.25 }],
];
const BEFORE = [
  ["before-attraction-field-case-1080", 1080, { scenario: FIELD, view: 0 }],
  ["before-attraction-field-case-1080-uncut", 1080, { scenario: FIELD, view: 0, free: 1 }],
  ["before-attraction-field-case-1440-fs125-uncut", 1440, { scenario: FIELD, view: 0, fs: 1.25, free: 1 }],
  ["before-header-long-name-1080", 1080, { scenario: BIG, view: 0 }],
  ["before-buildings-tab-1080", 1080, { scenario: ASH, view: 0, tab: "buildings" }],
  ["before-guests-2160-uncut", 2160, { scenario: ASH, view: 3, free: 1 }],
];

async function shots(b, base, dir, list) {
  fs.mkdirSync(dir, { recursive: true });
  for (const [file, h, p] of list) {
    const w = Math.round(h * 16 / 9);
    await open(b, base, w, h);
    const m = await evalAsync(b, `(async () => { const p = new URLSearchParams(${JSON.stringify(new URLSearchParams(Object.assign({ clean: 1 }, p)).toString())});
      await window.SkiLayout.shot(p); return document.body.getAttribute("data-shot-ready"); })()`, 60000);
    // Crop to the panel (+ margin) so the shot is readable at its native size.
    const rect = await evalAsync(b, `(() => { const r = document.querySelector('.h-panel').getBoundingClientRect();
      return { x: Math.max(0, r.left - 20), y: Math.max(0, r.top - 20), width: r.width + 40, height: Math.min(innerHeight, r.bottom + 20) - Math.max(0, r.top - 20) }; })()`, 10000);
    const shot = await b.send("Page.captureScreenshot", { format: "png", clip: Object.assign({ scale: 1 }, rect), captureBeyondViewport: true });
    const out = path.join(dir, file + ".png");
    fs.writeFileSync(out, Buffer.from(shot.data, "base64"));
    console.log(`shot ${out} (${w}x${h}) ${m}`);
  }
}

async function main() {
  const shotsArg = process.argv.find((a) => a.startsWith("--shots="));
  if (!fs.existsSync(path.join(ROOT, "build", "SkiResortMod.mjs"))) throw new Error("build/SkiResortMod.mjs missing: run npm run build first");
  const server = await startServer();
  const b = await startBrowser();
  const base = `http://127.0.0.1:${server.port}/harness/`;
  try {
    if (shotsArg) {
      await shots(b, base, path.resolve(shotsArg.slice("--shots=".length)), process.argv.includes("--before") ? BEFORE : SHOTS);
      return 0;
    }
    let failures = 0;
    const only = process.argv.find((a) => a.startsWith("--height="));
    for (const [w, h] of SIZES.filter(([, hh]) => !only || String(hh) === only.slice(9))) {
      await open(b, base, w, h);
      const r = await evalAsync(b, "window.SkiLayout.run()", 600000);
      const pagers = r.pagers.filter((p) => p.fs === 1).map((p) => `${p.scenario.split(" ")[0]} view ${p.view}: ${p.pager}`).join("; ");
      console.log(`test-hub-layout: ${w}x${h} (rem ${r.remPx} px): ${r.cases.length} case(s), ${r.fails.length} failure(s); max over ${r.maxOverRem} rem, ` +
        `min slack ${r.minSlackRem} rem`);
      console.log(`  pagers (text scale 1): ${pagers}`);
      for (const x of r.buildings.filter((x) => x.fs === 1)) console.log(`  buildings tab [${x.scenario}]: ${x.tiles} tiles, ${x.lodgeChips} lodge chip(s), caption "${x.caption.trim()}"`);
      for (const lt of r.liftsTile.filter((x) => x.fs === 1)) console.log(`  lifts tile [${lt.scenario}]: ${lt.text}`);
      for (const x of r.liftTool) console.log(`  lift generator [${x.scenario.split(" ")[0]} fs ${x.fs}]: ${x.present ? "present" : "MISSING"}, family row ${x.rowRem} rem`);
      for (const bd of r.band.filter((x) => x.fs === 1)) console.log(`  band [${bd.scenario}]: ${bd.text.join(" | ")}`);
      const sc = r.cases.filter((c) => c.scrolls && c.fs === 1 && c.overPx > 1).map((c) => `${c.scenario.split(" ")[0]} view ${c.view} +${c.overRem} rem`);
      console.log(`  full lists that scroll (text scale 1, allowed: T131): ${sc.length ? Array.from(new Set(sc)).join("; ") : "none"}`);
      for (const f of r.fails) console.log(`  FAIL ${f.scenario || ""} fs ${f.fs || ""}: ${f.what}` + (f.overRem !== undefined ? ` (over ${f.overRem} rem, slack ${f.slackRem} rem)` : ""));
      if (process.argv.includes("--verbose")) for (const c of r.cases) console.log(`    ${c.scenario.slice(0, 22)} fs ${c.fs} view ${c.view} ${c.state}: over ${c.overRem} slack ${c.slackRem} header ${c.headerRem}`);
      failures += r.fails.length;
    }
    console.log(`test-hub-layout: ${failures === 0 ? "PASS" : "FAIL"} (${failures} failure(s))`);
    return failures === 0 ? 0 : 1;
  } finally {
    b.close();
    server.child.kill();
  }
}

main().then((code) => setTimeout(() => process.exit(code), 600), (e) => { console.error(String(e && e.stack || e)); setTimeout(() => process.exit(2), 600); });
