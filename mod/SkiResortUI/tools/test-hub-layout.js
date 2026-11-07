/*
* tools/test-hub-layout.js - the "never a scrollbar" layout test. It uses no npm package. Node's own WebSocket (Node 22) talks the
* DevTools protocol to a headless Chrome (else Edge). The test starts that browser here with a throw-away profile. It deploys
* nothing and writes nothing outside --shots=<dir>.
*
* The test starts the harness (harness/serve.js on a free port). It opens the harness at 1920x1080, 2560x1440 and 3840x2160 with
* the game's rem rule. There it runs harness/layout-check.js (window.SkiLayout.run) for every view x fold x page x build tab x overlay
* at text scale 1.0 and 1.25. Both are CSS variables, as the game's applyTextScale sets them. The test FAILS (exit 1) on any case
* with one of these faults:
*   - content overflows its box (the Scrollable would show its bar)
*   - a header does not fit
*   - more than one Buildings tile row
*   - text is cut short or runs past the panel
* The check hovers every build tile in turn. It excepts shrink-to-fit classes and player-chosen names.
* Before the browser runs, a stylesheet rule applies. The note classes (NOTE_CLASSES) wrap. Every other class that cuts text with an
* ellipsis is a written decision (CUT_DECIDED). So a new one-line note with an ellipsis fails at once.
* The Runs and Lifts views are the exception. They list every row and may scroll (their list carries class listAll). For them
* the check fails on a drawn pager row instead.
*
* The test needs `npm run build` first (it mounts build/SkiResortMod.mjs). It also needs network access for the React UMD scripts
* that the harness loads from cdnjs.
* Usage:   node tools/test-hub-layout.js [--verbose]
*          node tools/test-hub-layout.js --shots=<dir> [--before | --list=t142 | --list=intuitive | --list=merge | --list=merge2]   # the harness screenshots listed in SHOTS / BEFORE / T142
* The harness is not the game's chrome (harness/README.md). The panel frame is a stand-in. So the report prints the smallest slack
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
const PLAYER_NAMES = new Set(["areaNameText", "rowName", "overlayItemName", "lodgeNameLabel"]);

// Notes wrap, never cut (2026-11-02 round 2: the Guests view's ticket note showed half its sentence and an ellipsis in game). A note
// class must not carry white-space: nowrap, text-overflow: ellipsis, or a fixed height with overflow hidden. It keeps a min-height.
const NOTE_CLASSES = ["note1", "note2", "subLine"];
// Besides the shrink-to-fit classes and PLAYER_NAMES, every other class that cuts text with an ellipsis is a decision. It is written
// here with its reason. A new ellipsis class fails until it is a note (wrap) or listed here. The cut-text check still fails any of them whose
// text is cut in a scenario.
const CUT_DECIDED = {
  switcherName: "the ski area name in the switcher, a name the player chose",
  statusText: "the header's status sentence beside its hint, one line by design",
  statusHint: "one status word beside the status sentence (Thawing, Powder day)",
  problemText: "a problem band row, one line each; may hold lift or run names; its tooltip has the whole text",
  infoLabel: "a data row's label beside its value (fixed row height, pager math)",
  infoLabelText: "a data row's label beside its value (fixed row height, pager math)",
  flagText: "a short flag word after a row label",
  liftLine2: "a lift row's second line (names, state) in a list of fixed rows",
  startText: "a numbered start step beside its button",
  buildStatusLine: "the build footer's one status line; every tile is hovered in turn and the fullest line is reported",
  quickLineText: "the picked run's name above its feature rows, a name the player chose (whole in the run row's tooltip)",
};

/** The top-level single-class rules of hub.module.scss: class -> rule body. */
function styleRules() {
  const rules = {};
  let current = null;
  for (const line of fs.readFileSync(path.join(ROOT, "src", "hub", "hub.module.scss"), "utf8").split(/\r?\n/)) {
    const m = /^\.([A-Za-z0-9]+)\s*\{/.exec(line);
    if (m) { current = m[1]; rules[current] = (rules[current] || ""); continue; }
    if (/^\}/.test(line)) { current = null; continue; }
    if (current) rules[current] += line + "\n";
  }
  return rules;
}

/** Static half of the cut-text rule. Notes wrap. Any other ellipsis class is decided (CUT_DECIDED, shrink, player names). */
function styleFailures() {
  const rules = styleRules();
  const shrinks = shrinkClasses();
  const fails = [];
  for (const c of NOTE_CLASSES) {
    const body = rules[c];
    if (body === undefined) { fails.push(`note class .${c} is missing from hub.module.scss`); continue; }
    if (/white-space:\s*nowrap/.test(body)) fails.push(`note class .${c} has white-space: nowrap (a note wraps)`);
    if (/text-overflow:\s*ellipsis/.test(body)) fails.push(`note class .${c} cuts with an ellipsis (a note wraps)`);
    if (/(^|\n)\s*height:/.test(body) && /overflow:\s*hidden/.test(body)) fails.push(`note class .${c} has a fixed height with overflow hidden (use min-height)`);
  }
  for (const [c, body] of Object.entries(rules)) {
    if (!/text-overflow:\s*ellipsis/.test(body)) continue;
    if (NOTE_CLASSES.includes(c) || shrinks.has(c) || PLAYER_NAMES.has(c) || CUT_DECIDED[c]) continue;
    fails.push(`.${c} cuts text with an ellipsis and is undecided: make it wrap (a note) or add it to CUT_DECIDED with the reason`);
  }
  return fails;
}

/** The hub classes whose rule carries coh-font-fit-mode: shrink. The function reads them from the stylesheet, so a new one needs no list here. */
function shrinkClasses() {
  const set = new Set();
  let current = "";
  for (const line of fs.readFileSync(path.join(ROOT, "src", "hub", "hub.module.scss"), "utf8").split("\n")) {
    const m = /^\.([A-Za-z0-9]+)\b.*\{/.exec(line);
    if (m) current = m[1];
    if (line.includes("coh-font-fit-mode: shrink") && current) set.add(current);
  }
  return set;
}
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
  // Wait for the harness to mount the bundle (its status line names the bundle stamp)
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
  // [file, height, params]
  ["quick-runs-open-1080", 1080, { scenario: "Quick runs open, Ash Flat (runs, lifts, problems)", view: 1, tab: "runs" }],
  ["resort-planner-plan-1080", 1080, { scenario: "Resort Planner plan, Ash Flat (runs, lifts, problems)", view: 1, tab: "runs" }],
  ["resort-planner-plan-1440-fs125", 1440, { scenario: "Resort Planner plan, Ash Flat (runs, lifts, problems)", view: 1, tab: "runs", fs: 1.25 }],
  ["quick-runs-building-1080", 1080, { scenario: "Quick runs building, Ash Flat (runs, lifts, problems)", view: 1, tab: "runs" }],
  ["quick-runs-planting-1080", 1080, { scenario: "Quick runs planting, Ash Flat (runs, lifts, problems)", view: 1, tab: "runs" }],
  ["start-card-1080", 1080, { scenario: "connected, empty city" }],
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
  ["runs-tab-1080", 1080, { scenario: ASH, view: 0, tab: "runs" }],                       // 2026-10-24: no grading chip (the Cat track tile opens the tool)
  ["runs-tab-1440-fs125", 1440, { scenario: ASH, view: 0, tab: "runs", fs: 1.25 }],
  ["problem-lift-bend-1080", 1080, { scenario: "Ash Flat (runs, lifts, problems), a lift bend", view: 0 }],   // code 30 LiftBend
  ["problem-low-span-1080", 1080, { scenario: "Ash Flat (runs, lifts, problems), a low span", view: 0 }],   // code 32 LiftLowSpan
  ["problem-fold-low-span-1080", 1080, { scenario: "Ash Flat (runs, lifts, problems), four lifts without power and a low span", view: 0 }],   // 588 f
];
const BEFORE = [
  ["before-attraction-field-case-1080", 1080, { scenario: FIELD, view: 0 }],
  ["before-attraction-field-case-1080-uncut", 1080, { scenario: FIELD, view: 0, free: 1 }],
  ["before-attraction-field-case-1440-fs125-uncut", 1440, { scenario: FIELD, view: 0, fs: 1.25, free: 1 }],
  ["before-header-long-name-1080", 1080, { scenario: BIG, view: 0 }],
  ["before-buildings-tab-1080", 1080, { scenario: ASH, view: 0, tab: "buildings" }],
  ["before-guests-2160-uncut", 2160, { scenario: ASH, view: 3, free: 1 }],
];

// T142 whole-hub text round (2026-10-24): every view top-down, its folds, and the tooltips the round reworded. `hover` = the
// start of an element's visible text inside the panel. The smallest such element gets a real mouse move (CDP), so the stand-in
// Tooltip opens and the crop grows to hold it. `--list=t142` picks this list.
const T142 = [
  ["attraction-1080", 1080, { scenario: ASH, view: 0 }],
  ["attraction-fold0-1080", 1080, { scenario: ASH, view: 0, fold: 0 }],
  ["attraction-fold1-1080", 1080, { scenario: ASH, view: 0, fold: 1 }],
  ["attraction-fold2-1080", 1080, { scenario: ASH, view: 0, fold: 2 }],
  ["attraction-field-1080", 1080, { scenario: FIELD, view: 0 }],
  ["attraction-big-problems-1440", 1440, { scenario: BIG, view: 0, problems: 1 }],
  ["runs-1080", 1080, { scenario: ASH, view: 1 }],
  ["lifts-1080", 1080, { scenario: ASH, view: 2 }],
  ["guests-1080", 1080, { scenario: ASH, view: 3 }],
  ["guests-fold0-1080", 1080, { scenario: ASH, view: 3, fold: 0 }],
  ["guests-fold1-1080", 1080, { scenario: ASH, view: 3, fold: 1 }],
  ["guests-fold2-1080", 1080, { scenario: ASH, view: 3, fold: 2 }],
  ["finances-1080", 1080, { scenario: ASH, view: 4 }],
  ["finances-fold0-1080", 1080, { scenario: ASH, view: 4, fold: 0 }],
  ["finances-fold1-1080", 1080, { scenario: ASH, view: 4, fold: 1 }],
  ["history-1080", 1080, { scenario: ASH, view: 5 }],
  ["history-1440-fs125", 1440, { scenario: ASH, view: 5, fs: 1.25 }],
  ["tab-buildings-1080", 1080, { scenario: ASH, view: 0, tab: "buildings" }],
  ["tab-lifts-1080", 1080, { scenario: ASH, view: 0, tab: "lifts" }],
  ["tab-runs-1080", 1080, { scenario: ASH, view: 0, tab: "runs" }],
  ["tip-status-1080", 1080, { scenario: ASH, view: 0, hover: "Open" }],
  ["tip-attraction-tile-1080", 1080, { scenario: ASH, view: 0, hover: "Attraction" }],
  ["tip-history-attraction-1080", 1080, { scenario: ASH, view: 5, hover: "Attraction" }],
  // B7b: the Attraction strip switched on, the pointer on a winter month (strip plot = the last plot of the view, x = share of width)
  ["tip-history-strip-snow-1080", 1080, { scenario: ASH, view: 5, click: "Attraction", hoverPlot: 0.72 }],
  ["tip-history-strip-snow-byseason-1080", 1080, { scenario: ASH, view: 5, click: "Attraction", clickRange: "By season", hoverPlot: 0.5 }],
];

// 2026-11-02 intuitiveness pass: every view top-down at 1080 (the tightest window), the build tabs, the band popover, the start card and the
// field screen that ran off the panel. `--list=intuitive` picks this list. Two builds render the before / after sets.
const F1102 = "Field 2026-11-02: closed, no lift";
const INTUITIVE = [
  ["01-field-guests", 1080, { scenario: F1102, view: 3 }],
  ["01b-field-guests-car-spaces", 1080, { scenario: F1102, view: 3, fold: 3 }],
  ["02-field-attraction", 1080, { scenario: F1102, view: 0 }],
  ["03-field-tab-lifts", 1080, { scenario: F1102, view: 0, tab: "lifts" }],
  ["04-attraction", 1080, { scenario: ASH, view: 0 }],
  ["05-attraction-terrain", 1080, { scenario: ASH, view: 0, fold: 0 }],
  ["06-runs", 1080, { scenario: ASH, view: 1 }],
  ["07-lifts", 1080, { scenario: ASH, view: 2 }],
  ["08-guests", 1080, { scenario: ASH, view: 3 }],
  ["09-guests-tickets", 1080, { scenario: ASH, view: 3, fold: 2 }],
  ["10-finances", 1080, { scenario: ASH, view: 4 }],
  ["11-history", 1080, { scenario: ASH, view: 5 }],
  ["12-band-popover", 1080, { scenario: BIG, view: 0, problems: 1 }],
  ["13-tab-buildings", 1080, { scenario: ASH, view: 0, tab: "buildings" }],
  ["14-tab-runs", 1080, { scenario: ASH, view: 0, tab: "runs" }],
  ["15-start-card", 1080, { scenario: "connected, empty city" }],
];

// 2026-11-06 merge discoverability round: the Overview, its Merged buildings fold, the overlay armed, the Buildings tab and the merge card
// beside the info panel (`whole: 1` = the whole game viewport, not the panel crop). `--list=merge` picks this list.
const MERGE = [
  ["merge-01-overview-1080", 1080, { scenario: ASH, view: 0 }],
  ["merge-02-overview-fold-1080", 1080, { scenario: ASH, view: 0, fold: 2 }],
  ["merge-03-overlay-armed-fold-1080", 1080, { scenario: "Ash Flat, merge overlay armed", view: 0, fold: 2 }],
  ["merge-04-tab-buildings-1080", 1080, { scenario: ASH, view: 0, tab: "buildings" }],
  ["merge-05-tab-buildings-armed-1080", 1080, { scenario: "Ash Flat, merge overlay armed", view: 0, tab: "buildings" }],
  ["merge-06-card-viewport-1080", 1080, { scenario: ASH, view: 0, tab: "buildings", whole: 1 }],
  ["merge-07-tile-hover-1080", 1080, { scenario: ASH, view: 0, tab: "buildings", hover: "Merge" }],
  ["merge-08-card-too-far-1080", 1080, { scenario: "Ash Flat, merge card too far", view: 0, tab: "buildings", whole: 1 }],
];
// 2026-11-06 follow-up: the Buildings tab in two rows with the Mergeable buildings toggle (`--list=merge2`).
const MERGE2 = [
  ["buildings-idle-1080", 1080, { scenario: ASH, view: 0, tab: "buildings" }],
  ["buildings-hover-lodge-1080", 1080, { scenario: ASH, view: 0, tab: "buildings", hover: "Chalet" }],
  ["buildings-merge-armed-1080", 1080, { scenario: "Ash Flat, merge overlay armed", view: 0, tab: "buildings" }],
  ["buildings-merge-hover-1080", 1080, { scenario: ASH, view: 0, tab: "buildings", hover: "Mergeable buildings" }],
  ["buildings-idle-1080-fs125", 1080, { scenario: ASH, view: 0, tab: "buildings", fs: 1.25 }],
  ["runs-tab-merge-armed-1080", 1080, { scenario: "Ash Flat, merge overlay armed", view: 0, tab: "runs" }],
];

async function shots(b, base, dir, list) {
  fs.mkdirSync(dir, { recursive: true });
  for (const [file, h, p0] of list) {
    const w = Math.round(h * 16 / 9);
    const p = Object.assign({}, p0);
    const { hover, click, clickRange, hoverPlot, whole } = p;
    delete p.hover; delete p.click; delete p.clickRange; delete p.hoverPlot; delete p.whole;
    await open(b, base, w, h);
    const m = await evalAsync(b, `(async () => { const p = new URLSearchParams(${JSON.stringify(new URLSearchParams(Object.assign({ clean: 1 }, p)).toString())});
      await window.SkiLayout.shot(p); return document.body.getAttribute("data-shot-ready"); })()`, 60000);
    for (const want of [clickRange, click]) {
      if (!want) continue;
      const ok = await evalAsync(b, `(async () => { const want = ${JSON.stringify(want)};
        const el = Array.from(document.querySelectorAll('.h-panel button')).find((e) => (e.textContent || '').trim() === want);
        if (!el) return false; el.click(); await new Promise((r) => setTimeout(r, 300)); return true; })()`, 10000);
      if (!ok) console.log(`shot ${file}: button "${want}" not found`);
    }
    if (hoverPlot !== undefined) {
      const at = await evalAsync(b, `(() => { const ps = Array.from(document.querySelectorAll('.h-panel [class*="sr-histPlot_"]'));
        const e = ps[ps.length - 1]; if (!e) return null; const r = e.getBoundingClientRect();
        return { x: r.left + r.width * ${Number(hoverPlot)}, y: r.top + r.height / 2 }; })()`, 10000);
      if (at) {   // two moves. The first enters the plot (the tooltip opens). The second is the mousemove that picks the bucket
        await b.send("Input.dispatchMouseEvent", { type: "mouseMoved", x: at.x - 2, y: at.y });
        await sleep(200);
        await b.send("Input.dispatchMouseEvent", { type: "mouseMoved", x: at.x, y: at.y });
        await sleep(400);
      } else console.log(`shot ${file}: no chart plot to point at`);
    }
    if (hover) {
      const at = await evalAsync(b, `(() => { const want = ${JSON.stringify(hover)};
        const els = Array.from(document.querySelectorAll('.h-panel *')).filter((e) => (e.textContent || '').trim().startsWith(want) && e.getBoundingClientRect().width > 0);
        els.sort((a, c) => a.textContent.length - c.textContent.length || c.querySelectorAll('*').length - a.querySelectorAll('*').length);
        const e = els[0]; if (!e) return null; const r = e.getBoundingClientRect(); return { x: r.left + Math.min(r.width / 2, 20), y: r.top + r.height / 2 }; })()`, 10000);
      if (at) {
        await b.send("Input.dispatchMouseEvent", { type: "mouseMoved", x: at.x, y: at.y });
        await sleep(400);
      } else console.log(`shot ${file}: hover target "${hover}" not found`);
    }
    // Crop to the panel (+ margin), grown to hold an open tooltip
    const rect = whole ? { x: 0, y: 0, width: w, height: h } : await evalAsync(b, `(() => { const r = document.querySelector('.h-panel').getBoundingClientRect();
      const t = document.querySelector('.h-tip'); const tr = t ? t.getBoundingClientRect() : null;
      const left = Math.min(r.left, tr ? tr.left : r.left), right = Math.max(r.right, tr ? tr.right : r.right);
      const top = Math.min(r.top, tr ? tr.top : r.top), bottom = Math.max(r.bottom, tr ? tr.bottom : r.bottom);
      return { x: Math.max(0, left - 20), y: Math.max(0, top - 20), width: Math.min(innerWidth, right + 20) - Math.max(0, left - 20), height: Math.min(innerHeight, bottom + 20) - Math.max(0, top - 20) }; })()`, 10000);
    const shot = await b.send("Page.captureScreenshot", { format: "png", clip: Object.assign({ scale: 1 }, rect), captureBeyondViewport: !hover && hoverPlot === undefined });   // a pointer shot stays in the viewport. Growing it re-lays the page under the pointer, and the bucket hover drops
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
      await shots(b, base, path.resolve(shotsArg.slice("--shots=".length)),
        process.argv.includes("--list=t142") ? T142 : process.argv.includes("--list=intuitive") ? INTUITIVE : process.argv.includes("--list=merge2") ? MERGE2 : process.argv.includes("--list=merge") ? MERGE : process.argv.includes("--before") ? BEFORE : SHOTS);
      return 0;
    }
    const styleFails = styleFailures();
    console.log(`test-hub-layout: stylesheet: ${NOTE_CLASSES.length} note class(es) wrap, ${Object.keys(CUT_DECIDED).length} decided ellipsis class(es), ` +
      `${styleFails.length} failure(s)`);
    for (const f of styleFails) console.log(`  FAIL stylesheet: ${f}`);
    let failures = styleFails.length;
    const only = process.argv.find((a) => a.startsWith("--height="));
    for (const [w, h] of SIZES.filter(([, hh]) => !only || String(hh) === only.slice(9))) {
      await open(b, base, w, h);
      const r = await evalAsync(b, "window.SkiLayout.run()", 600000);
      const pagers = r.pagers.filter((p) => p.fs === 1).map((p) => `${p.scenario.split(" ")[0]} view ${p.view}: ${p.pager}`).join("; ");
      console.log(`test-hub-layout: ${w}x${h} (rem ${r.remPx} px): ${r.cases.length} case(s), ${r.fails.length} failure(s); max over ${r.maxOverRem} rem, ` +
        `min slack ${r.minSlackRem} rem`);
      console.log(`  pagers (text scale 1): ${pagers}`);
      for (const x of r.buildings.filter((x) => x.fs === 1)) console.log(`  buildings tab [${x.scenario}]: ${x.tiles} tiles in ${x.rows} row(s), chips "${x.caption.trim()}", cut labels: ${x.cut || "none"}`);
      for (const lt of r.liftsTile.filter((x) => x.fs === 1)) console.log(`  lifts tile [${lt.scenario}]: ${lt.text}`);
      for (const x of r.liftTool) console.log(`  lift generator [${x.scenario.split(" ")[0]} fs ${x.fs}]: ${x.present ? "present" : "MISSING"}, family row ${x.rowRem} rem`);
      for (const bd of r.band.filter((x) => x.fs === 1)) console.log(`  band [${bd.scenario}]: ${bd.text.join(" | ")}`);
      const sc = r.cases.filter((c) => c.scrolls && c.fs === 1 && c.overPx > 1).map((c) => `${c.scenario.split(" ")[0]} view ${c.view} +${c.overRem} rem`);
      console.log(`  full lists that scroll (text scale 1, allowed: T131): ${sc.length ? Array.from(new Set(sc)).join("; ") : "none"}`);
      // Cut text fails unless the game shrinks that class to fit (the harness browser cannot) or it is a name the player chose (ellipsis + tooltip).
      const shrinks = shrinkClasses();
      const clipped = Object.values(r.clipped).filter((c) => !shrinks.has(c.cls) && !PLAYER_NAMES.has(c.cls));
      console.log(`  cut text: ${clipped.length} real, ${Object.keys(r.clipped).length - clipped.length} allowed (shrink-to-fit classes, player names)`);
      for (const [fs, x] of Object.entries(r.fullest)) console.log(`  fullest footer line at text scale ${fs}: ${x.full} % "${x.text}"`);
      for (const c of clipped) r.fails.push({ scenario: c.where, what: `text cut [${c.cls}] ${c.kind} +${c.over}px: "${c.text}"` });
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
