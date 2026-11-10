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
*   - text is cut short or runs past the panel, with every build tile hovered in turn (shrink-to-fit classes and player-chosen
*     names are excepted)
*   - (round 624) a tooltip icon key row passes its box, or a number stands apart from its unit (an ASCII space before a hubRules
*     UNIT_WORDS entry) in the panel or a tooltip: harness/layout-check.js tipPass hovers every row of the Attraction view (each fold
*     open in turn) and the six tiles, at text scale 1, 1.25 and 1.5
* Before the browser runs, the test checks a stylesheet rule. The note classes (NOTE_CLASSES) wrap. Every other class that cuts text with an
* ellipsis is a written decision (CUT_DECIDED). So a new one-line note with an ellipsis fails at once.
* The Runs and Lifts views are the exception. They list every row and may scroll (their list carries class listAll). For them
* the check fails on a drawn pager row instead.
*
* The test needs `npm run build` first (it mounts build/SkiResortMod.mjs). It also needs network access for the React UMD scripts
* that the harness loads from cdnjs.
*
* Speed (2026-11-09: 1591 s serial before, about 65 s now, same report): the check and the tooltip pass run in units of one size x
* scenario x text scale, spread over --workers headless browsers (DEFAULT_WORKERS), and the report merges them in the serial order
* (runSharded). layout-check.js settle() waits for a quiet page instead of fixed timers. Only one run at a time on this machine (the
* LOCK file in %TEMP%): a second run waits for it.
* Usage:   node tools/test-hub-layout.js [--verbose]                    # the full check: 3 sizes, the gate before a release / merge
*          node tools/test-hub-layout.js --quick [--scenario=<regex>]   # 1080 only (the tightest window), for iteration in a round
*            --height=1080|1440|2160   one size     --scenario=<regex>   only the scenarios it matches (case-insensitive; a partial run)
*            --workers=<n>   browsers in parallel  --no-wait   fail (exit 3) when another run holds the lock, instead of waiting
*            --fixed-settle   the old fixed waits in settle(), to cross-check a doubt about the quiet condition
*          node tools/test-hub-layout.js --shots=<dir> [--before | --list=t142 | --list=intuitive | --list=merge | --list=merge2 | --list=tips | --list=icons | --list=icons2 | --list=tiers | --list=polish | --list=r1f | --list=r2b]   # the harness screenshots listed in SHOTS / BEFORE / T142
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
// Workers (measured 2026-11-09, 8 cores / 16 threads, full run of 141 units): 4 -> 181 s, 6 -> 124 s, 8 -> 93 s, 12 -> 63 s,
// 16 -> 51 s, 24 -> 38 s. A page mostly waits for drawn frames, so more pages keep paying; RAM is the limit: 12 workers take about
// 5 GB, 16 about 7 GB. The default keeps 12 and fewer when free RAM is short (WORKER_GB each, FREE_GB_KEEP left over) or a game runs
// (the machine rule in CLAUDE.md: one light background job while a game runs). --workers=<n> overrides both.
const DEFAULT_WORKERS = 12, WORKER_GB = 0.45, FREE_GB_KEEP = 6, GAME_WORKERS = 2;
function gameRunning() {
  if (process.platform !== "win32") return null;
  try {
    const out = require("child_process").execFileSync("tasklist", ["/FO", "CSV", "/NH"], { encoding: "utf8", timeout: 5000 });
    const m = /^"(Cities2|bf6|battlefield)[^"]*.exe"/im.exec(out);
    return m ? m[1] : null;
  } catch { return null; }
}
function defaultWorkers() {
  const game = gameRunning();
  if (game) return { n: GAME_WORKERS, why: `${game} runs: ${GAME_WORKERS} workers (--workers=<n> overrides)` };
  const freeGb = os.freemem() / 2 ** 30;
  const n = Math.max(1, Math.min(DEFAULT_WORKERS, Math.floor((freeGb - FREE_GB_KEEP) / WORKER_GB)));
  return { n, why: n < DEFAULT_WORKERS ? `${freeGb.toFixed(1)} GB free RAM: ${n} workers (--workers=<n> overrides)` : "" };
}
const PLAYER_NAMES = new Set(["areaNameText", "rowName", "overlayItemName", "lodgeNameLabel"]);
// Decided ellipsis classes whose line may hold a name the player chose (a lift or run name in a problem line). Its cut is allowed only
// when the cut line holds a name of the scenario: the name's length is the player's, and the tooltip has the whole text. Round 624: the
// harness used to wrap every LocText in a <span> (the game renders a bare text node, index.js@434221), and the span hid every cut of a
// placeholder sentence from this check. LocText now renders as in the game, so the check sees them, and this keeps the old meaning.
const NAME_LINES = new Set(["problemText"]);

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

/** hubRules UNIT_WORDS (the units unitNbsp glues to a number), read from the source for the page's number-unit scan. */
function unitWords() {
  const src = fs.readFileSync(path.join(ROOT, "src", "hub", "hubRules.ts"), "utf8");
  const m = /export const UNIT_WORDS: readonly string\[\] = \[([^\]]*)\]/.exec(src);
  const words = m ? [...m[1].matchAll(/"([^"]+)"/g)].map((x) => x[1]) : [];
  if (words.length < 5) throw new Error("test-hub-layout: UNIT_WORDS not found in src/hub/hubRules.ts");
  return words;
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

// One run at a time on this machine (2026-11-09): two runs halve each other's speed. The lock is a file in %TEMP% that holds the
// owner's pid. A run waits for it (--no-wait: fail at once, exit 3). A lock whose pid is gone is stale and taken over.
const LOCK = path.join(os.tmpdir(), "skiresort-test-hub-layout.lock");
const LOCK_WAIT_MS = 45 * 60 * 1000;
function pidAlive(pid) { try { process.kill(pid, 0); return true; } catch (e) { return e.code === "EPERM"; } }
async function takeLock(noWait) {
  const me = JSON.stringify({ pid: process.pid, since: new Date().toISOString(), cwd: ROOT });
  const t0 = Date.now();
  let told = false;
  for (;;) {
    try { fs.writeFileSync(LOCK, me, { flag: "wx" }); break; } catch (e) { if (e.code !== "EEXIST") throw e; }
    let held = null;
    try { held = JSON.parse(fs.readFileSync(LOCK, "utf8")); } catch { /* being written or removed: look again */ }
    if (held && !pidAlive(held.pid)) { try { fs.unlinkSync(LOCK); } catch { /* another waiter took it over first */ } continue; }
    const who = held ? `pid ${held.pid} since ${held.since} in ${held.cwd}` : "another run";
    if (noWait) throw Object.assign(new Error(`test-hub-layout: another run holds the lock (${who}); ${LOCK}. Run again when it ends, or drop --no-wait to wait.`), { exitCode: 3 });
    if (Date.now() - t0 > LOCK_WAIT_MS) throw Object.assign(new Error(`test-hub-layout: waited ${LOCK_WAIT_MS / 60000} min for the lock (${who}); ${LOCK}`), { exitCode: 3 });
    if (!told) { console.log(`test-hub-layout: waiting for another run to end (${who}). --no-wait fails at once instead.`); told = true; }
    await sleep(1000);
  }
  const release = () => { try { if (JSON.parse(fs.readFileSync(LOCK, "utf8")).pid === process.pid) fs.unlinkSync(LOCK); } catch { /* gone */ } };
  process.on("exit", release);
  return release;
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
  ["resort-planner-lifts-building-1080", 1080, { scenario: "Resort Planner lifts, queue building (every row state), Ash Flat (runs, lifts, problems)", view: 1, tab: "runs" }],   // R1-d
  ["resort-planner-lifts-stopping-1080", 1080, { scenario: "Resort Planner lifts, queue stopping (planner open), Ash Flat (runs, lifts, problems)", view: 1, tab: "runs" }],
  ["resort-planner-lifts-idle-1080", 1080, { scenario: "Resort Planner lifts, idle with a refusal and a notice, Ash Flat (runs, lifts, problems)", view: 1, tab: "runs" }],
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
  ["merge-09-card-no-geometry-1080", 1080, { scenario: "Ash Flat, merge card no geometry", view: 0, tab: "buildings", whole: 1 }],
  ["merge-10-tab-buildings-armed-no-geometry-1080", 1080, { scenario: "Ash Flat, merge overlay armed no geometry", view: 0, tab: "buildings" }],
  ["merge-12-tab-buildings-armed-no-geometry-1080-fs125", 1080, { scenario: "Ash Flat, merge overlay armed no geometry", view: 0, tab: "buildings", fs: 1.25 }],
  ["merge-11-chip-hover-no-geometry-1080", 1080, { scenario: "Ash Flat, merge overlay armed no geometry", view: 0, tab: "buildings", hover: "Mergeable buildings" }],
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
// 2026-11-08 round 616: the Attraction receipt's row tooltips, reworded (no "counts", no bare "×"). The Runs, lifts and school fold
// is open (fold 0), so its rows can be hovered. `--list=tips` picks this list.
const TIPS616 = [
  ["tips-01-attraction-tile-1080", 1080, { scenario: ASH, view: 0, hover: "Attraction" }],
  ["tips-02-lodge-base-1080", 1080, { scenario: ASH, view: 0, fold: 0, hover: "Lodge base" }],
  ["tips-03-terrain-1080", 1080, { scenario: ASH, view: 0, fold: 0, hover: "Runs, lifts and school" }],
  ["tips-04-school-1080", 1080, { scenario: ASH, view: 0, fold: 0, hover: "Ski school" }],
  ["tips-05-variety-1080", 1080, { scenario: ASH, view: 0, fold: 0, hover: "Variety" }],
  ["tips-06-vertical-1080", 1080, { scenario: ASH, view: 0, fold: 0, hover: "Vertical" }],
  ["tips-07-efficiency-1080", 1080, { scenario: ASH, view: 0, fold: 0, hover: "Efficiency" }],
  ["tips-08-share-1080", 1080, { scenario: ASH, view: 0, fold: 0, hover: "Share of city tourism" }],
  ["tips-09-variety-1440-fs125", 1440, { scenario: ASH, view: 0, fold: 0, hover: "Variety", fs: 1.25 }],
];
// 2026-11-08 icon scheme (hub contract 40, DECISIONS 620): the receipt with the Variety flag (no black run open or patchy), its
// tooltip with the missing line, the symbol legend (the header's Info glyph, `hoverSel` = a CSS selector to point at; `hoverIn` limits
// the `hover` text search to a selector, so "Runs" finds the receipt row and not the footer tab), and the
// Runs / Lifts rows with the half-filled Patchy and No-power dots. Every shot at text scale 1 and 1.25. `--list=icons` picks this list.
const NOBLACK = "Ash Flat, no black run open";
const ICONS = [];
for (const [scale, tag] of [[1, ""], [1.25, "-fs125"]]) {
  ICONS.push(
    [`icons-01-receipt-variety-flag-1080${tag}`, 1080, { scenario: NOBLACK, view: 0, fold: 0, fs: scale }],
    [`icons-02-tip-variety-missing-1080${tag}`, 1080, { scenario: NOBLACK, view: 0, fold: 0, hover: "Variety", fs: scale }],
    [`icons-03-tip-runs-missing-1080${tag}`, 1080, { scenario: NOBLACK, view: 0, fold: 0, hover: "Runs", hoverIn: '[class*="sr-infoLabel"]', fs: scale }],
    [`icons-04-tip-status-marks-1080${tag}`, 1080, { scenario: ASH, view: 0, hover: "Open", fs: scale }],
    [`icons-05-legend-1080${tag}`, 1080, { scenario: ASH, view: 0, hoverSel: '.h-panel [class*="legendInfoBox"]', fs: scale }],
    [`icons-06-runs-patchy-dot-1080${tag}`, 1080, { scenario: ASH, view: 1, fs: scale }],
    [`icons-07-lifts-half-dot-1080${tag}`, 1080, { scenario: ASH, view: 2, fs: scale }],
    [`icons-08-tip-school-1080${tag}`, 1080, { scenario: NOBLACK, view: 0, fold: 0, hover: "Ski school", fs: scale }],
    [`icons-09-tip-lifts-missing-1080${tag}`, 1080, { scenario: NOBLACK, view: 0, fold: 0, hover: "Lifts", hoverIn: '[class*="sr-infoLabel"]', fs: scale }],
    [`icons-10-tip-lodge-base-1080${tag}`, 1080, { scenario: NOBLACK, view: 0, fold: 0, hover: "Lodge base", fs: scale }],
    [`icons-11-tip-efficiency-1080${tag}`, 1080, { scenario: NOBLACK, view: 0, fold: 0, hover: "Efficiency", fs: scale }],
  );
}
// Icons v2 + the NBSP rescue (round 622): the ski school and lift icons in their missing lines, the legend with the Ski area group,
// and the inline (cohinline) path with its U+00A0 spaces. `iconMode` sets window.skiResortIconMode before the shot. Chrome ignores
// cohinline, so an "inline" shot shows the content and the spacing, not the Gameface wrap (that is the in-game pass).
// `--list=icons2` picks this list.
const NOSCHOOL = "Ash Flat, no ski school";
const ICONS2 = [];
for (const [scale, tag] of [[1, ""], [1.25, "-fs125"]]) {
  ICONS2.push(
    [`icons2-01-tip-school-missing-1080${tag}`, 1080, { scenario: NOSCHOOL, view: 0, fold: 0, hover: "Ski school", fs: scale }],
    [`icons2-02-tip-runs-no-lift-1080${tag}`, 1080, { scenario: NOBLACK, view: 0, fold: 0, hover: "Runs", hoverIn: '[class*="sr-infoLabel"]', fs: scale }],
    [`icons2-03-legend-1080${tag}`, 1080, { scenario: ASH, view: 0, hoverSel: '.h-panel [class*="legendInfoBox"]', fs: scale }],
    [`icons2-04-tip-variety-inline-1080${tag}`, 1080, { scenario: NOBLACK, view: 0, fold: 0, hover: "Variety", iconMode: "inline", fs: scale }],
    [`icons2-05-tip-school-inline-1080${tag}`, 1080, { scenario: NOSCHOOL, view: 0, fold: 0, hover: "Ski school", iconMode: "inline", fs: scale }],
  );
}
// 2026-11-08 DECISIONS 621: lodge tier names (role names) apart from building names. The merge card's tier picker with all five
// tiers, the Merged buildings fold, the lodge-base tooltip's best tier, and the own-lodge rows that keep building names (Buildings
// tab tiles, car spaces). Every shot at text scale 1 and 1.25. `--list=tiers` picks this list.
// Round 624 polish: the Vertical and Variety tooltips (inline icons) at text scale 1, 1.25 and 1.5 (number and unit on one line, the key
// rows inside the box), and the low-span problem line in the band with the +N popover open. `--list=polish` picks this list.
const POLISH = [];
for (const [scale, tag] of [[1, "-fs100"], [1.25, "-fs125"], [1.5, "-fs150"]]) {
  POLISH.push(
    [`polish-01-tip-vertical-1080${tag}`, 1080, { scenario: NOBLACK, view: 0, fold: 0, hover: "Vertical", iconMode: "inline", fs: scale }],
    [`polish-02-tip-variety-1080${tag}`, 1080, { scenario: NOBLACK, view: 0, fold: 0, hover: "Variety", iconMode: "inline", fs: scale }],
  );
}
POLISH.push(["polish-03-low-span-popover-1080-fs125", 1080, { scenario: "Ash Flat (runs, lifts, problems), a low span", view: 0, problems: 1, fs: 1.25 }]);
const TIERS = [];
for (const [scale, tag] of [[1, ""], [1.25, "-fs125"]]) {
  TIERS.push(
    [`tiers-01-card-every-tier-1080${tag}`, 1080, { scenario: "Ash Flat, merge card every tier", view: 0, tab: "buildings", whole: 1, fs: scale }],
    [`tiers-02-card-four-tiers-1080${tag}`, 1080, { scenario: ASH, view: 0, tab: "buildings", whole: 1, fs: scale }],
    [`tiers-03-merged-fold-1080${tag}`, 1080, { scenario: ASH, view: 0, fold: 2, fs: scale }],
    // Text scale 1 only: at 1.25 the harness puts the merge card over the Lodge base label, so the hover cannot reach it.
    ...(scale === 1 ? [[`tiers-04-tip-best-tier-1080`, 1080, { scenario: ASH, view: 0, fold: 0, hover: "Lodge base", fs: 1 }]] : []),
    [`tiers-05-tab-buildings-1080${tag}`, 1080, { scenario: ASH, view: 0, tab: "buildings", fs: scale }],
    [`tiers-06-guests-car-spaces-1080${tag}`, 1080, { scenario: ASH, view: 3, fold: 3, fs: scale }],
  );
}

// R1-f: the sheet rows with a refusal (warning color) and a wait in the lift tool (its own reason). `--list=r1f` picks this list.
const R1F = [
  ["r1f-lifts-refused-and-waiting-1080", 1080, { scenario: "Resort Planner lifts, idle with a refusal and a notice, Ash Flat (runs, lifts, problems)", view: 1, tab: "runs" }],
];

// R2-b: the runs-first plan at its largest (15 lifts, groups, flagged and waiting lines, cat tracks in the totals), its sheet with the planner closed, and the knob
// rows without the hidden knobs. `--list=r2b` picks this list.
const R2B = [
  ["r2b-plan-15-lifts-top-1080", 1080, { scenario: "Resort Planner plan, 15 lifts (flagged, waiting, other runs), Ash Flat (runs, lifts, problems)", view: 1, tab: "runs" }],
  ["r2b-sheet-15-rows-flagged-1080", 1080, { scenario: "Resort Planner lifts, 15 rows (flagged tower, waiting), Ash Flat (runs, lifts, problems)", view: 1, tab: "runs" }],
  ["r2b-plan-15-lifts-rows-1080", 1080, { scenario: "Resort Planner plan, 15 lifts (flagged, waiting, other runs), Ash Flat (runs, lifts, problems)", view: 1, tab: "runs", scrollY: 1000, scrollIn: "planList", scrollInY: 1000 }],
  ["r2b-sheet-15-rows-flagged-lower-1080", 1080, { scenario: "Resort Planner lifts, 15 rows (flagged tower, waiting), Ash Flat (runs, lifts, problems)", view: 1, tab: "runs", scrollY: 1000 }],
  ["r2b-sheet-flagged-tooltip-1080", 1080, { scenario: "Resort Planner lifts, 15 rows (flagged tower, waiting), Ash Flat (runs, lifts, problems)", view: 1, tab: "runs", hover: "Built. Tower 7", hoverIn: "div" }],
  ["r2b-knob-row-1080", 1080, { scenario: "Resort Planner plan, Ash Flat (runs, lifts, problems)", view: 1, tab: "runs" }],
];

async function shots(b, base, dir, list) {
  fs.mkdirSync(dir, { recursive: true });
  for (const [file, h, p0] of list) {
    const w = Math.round(h * 16 / 9);
    const p = Object.assign({}, p0);
    const { hover, hoverIn, hoverSel, click, clickRange, hoverPlot, whole, iconMode, scrollY, scrollIn, scrollInY } = p;
    delete p.hover; delete p.hoverIn; delete p.hoverSel; delete p.click; delete p.clickRange; delete p.hoverPlot; delete p.whole; delete p.iconMode; delete p.scrollY; delete p.scrollIn; delete p.scrollInY;
    await open(b, base, w, h);
    if (iconMode) await evalAsync(b, `(() => { window.skiResortIconMode = ${JSON.stringify(iconMode)}; return true; })()`, 10000);
    const m = await evalAsync(b, `(async () => { const p = new URLSearchParams(${JSON.stringify(new URLSearchParams(Object.assign({ clean: 1 }, p)).toString())});
      await window.SkiLayout.shot(p); return document.body.getAttribute("data-shot-ready"); })()`, 60000);
    for (const want of [clickRange, click]) {
      if (!want) continue;
      const ok = await evalAsync(b, `(async () => { const want = ${JSON.stringify(want)};
        const el = Array.from(document.querySelectorAll('.h-panel button')).find((e) => (e.textContent || '').trim() === want);
        if (!el) return false; el.click(); await new Promise((r) => setTimeout(r, 300)); return true; })()`, 10000);
      if (!ok) console.log(`shot ${file}: button "${want}" not found`);
    }
    if (scrollY) {   // scroll the panel's content (a long Runs view: the lower rows) by this many px
      const n = await evalAsync(b, `(() => { const e = document.querySelector('.h-panel [class*="panelContent"]'); if (!e) return 0; e.scrollTop = ${Number(scrollY)}; return 1; })()`, 10000);
      if (!n) console.log(`shot ${file}: no panel content to scroll`);
      await sleep(300);
    }
    if (scrollIn) {   // then one named box inside the panel (a class name part, as the plan list's)
      const n = await evalAsync(b, `(() => { const e = document.querySelector('.h-panel [class*="${scrollIn}"]'); if (!e) return 0; e.scrollTop = ${Number(scrollInY || 0)}; return 1; })()`, 10000);
      if (!n) console.log(`shot ${file}: no box named ${scrollIn}`);
      await sleep(300);
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
    if (hoverSel) {
      const at = await evalAsync(b, `(() => { const e = document.querySelector(${JSON.stringify(hoverSel)}); if (!e) return null;
        const r = e.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; })()`, 10000);
      if (at) {
        await b.send("Input.dispatchMouseEvent", { type: "mouseMoved", x: at.x, y: at.y });
        await sleep(400);
      } else console.log(`shot ${file}: hover selector "${hoverSel}" not found`);
    }
    if (hover) {
      const at = await evalAsync(b, `(() => { const want = ${JSON.stringify(hover)};
        const els = Array.from(document.querySelectorAll('.h-panel ' + ${JSON.stringify(hoverIn || "*")})).filter((e) => (e.textContent || '').trim().startsWith(want) && e.getBoundingClientRect().width > 0);
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
    const shot = await b.send("Page.captureScreenshot", { format: "png", clip: Object.assign({ scale: 1 }, rect), captureBeyondViewport: !hover && !hoverSel && hoverPlot === undefined && !scrollY && !scrollIn });   // a pointer shot stays in the viewport. Growing it re-lays the page under the pointer, and the bucket hover drops
    const out = path.join(dir, file + ".png");
    fs.writeFileSync(out, Buffer.from(shot.data, "base64"));
    console.log(`shot ${out} (${w}x${h}) ${m}`);
  }
}

/** One unit's result appended to the size's merged result. Units arrive in report order (scenario, then text scale), as one run() makes them. */
function mergeInto(r, u) {
  r.remPx = u.remPx;
  for (const k of ["cases", "fails", "pagers", "buildings", "liftsTile", "band", "liftTool"]) r[k].push(...u[k]);
  for (const [k, v] of Object.entries(u.clipped)) if (!r.clipped[k]) r.clipped[k] = v;   // first sighting wins, as in one page
  for (const [fs, x] of Object.entries(u.fullest)) if (!r.fullest[fs] || x.full > r.fullest[fs].full) r.fullest[fs] = x;
  r.maxOverRem = r.cases.reduce((m, c) => Math.max(m, c.overRem || 0), 0);
  r.minSlackRem = r.cases.reduce((m, c) => Math.min(m, c.slackRem == null ? m : c.slackRem), 1e9);
  for (const k of ["ms", "settles", "frames", "capped"]) r.timing[k] += u.timing[k];
  if (u.timing.capped) r.timing.cappedWhere.push(`${u.timing.where}: ${u.timing.capped} settle(s), last change ${u.timing.lastCapped}`);
  r.tips.n += u.tips.n; r.tips.keys += u.tips.keys; r.tips.keyRows = Math.max(r.tips.keyRows, u.tips.keyRows);
  for (const n of u.tips.scenarios) if (!r.tips.scenarios.includes(n)) r.tips.scenarios.push(n);
}
const emptyResult = () => ({ remPx: 0, cases: [], fails: [], pagers: [], buildings: [], liftsTile: [], band: [], liftTool: [], clipped: {}, fullest: {},
  maxOverRem: 0, minSlackRem: 1e9, timing: { ms: 0, settles: 0, frames: 0, capped: 0, cappedWhere: [] }, tips: { n: 0, keys: 0, keyRows: 0, scenarios: [] } });

/**
 * The check, sharded (2026-11-09). A unit is one size x scenario x text scale, of the check (checkOne) or of the tooltip pass (tipPass);
 * `units` lists them in the serial run's order: every check unit, then every tooltip unit. Each worker is its own headless browser (its
 * own renderer process, so the pages run in parallel). A worker keeps its page between units of one size, so most units start from the
 * state the unit before left, as in the serial run. A page that starts mid-list gets that state from SkiLayout.prime first. The first
 * unit of each size runs on a fresh page, as it did. The results merge in that order, so the report reads as the serial run's.
 */
async function runSharded(browsers, base, sizes, units, opts) {
  const results = new Map(sizes.map(([, h]) => [h, emptyResult()]));
  const unitsBySize = new Map(sizes.map(([w, h]) => [h, units.map((x, idx) => Object.assign({ w, h, idx }, x))]));
  const done = new Map(sizes.map(([, h]) => [h, []]));
  const queue = new Map(sizes.map(([, h]) => [h, unitsBySize.get(h).slice()]));
  const prev = (u) => { const all = unitsBySize.get(u.h); return u.idx > 0 ? all[u.idx - 1] : null; };
  async function worker(b, k) {
    let size = null, last = null;
    // Spread the workers over the sizes first, then let each one stay on its size and help where the most work is left.
    let pick = sizes[k % sizes.length][1];
    for (;;) {
      if (!queue.get(pick) || queue.get(pick).length === 0) {
        pick = null;
        for (const [, h] of sizes) if (queue.get(h).length > 0 && (pick === null || queue.get(h).length > queue.get(pick).length)) pick = h;
        if (pick === null) return;
      }
      const u = queue.get(pick).shift();
      if (size !== u.h) { await open(b, base, u.w, u.h); size = u.h; last = null; }
      try {
        const p = prev(u);
        if (p && last !== p) await evalAsync(b, `window.SkiLayout.prime(${JSON.stringify(p.name)}, ${p.fs})`, 120000);
        else if (!p && last !== null) { await open(b, base, u.w, u.h); }   // the size's first unit: a fresh page, as in the serial run
        const o = u.kind === "tip"
          ? { scenarios: [], units: opts.units, tipScenarios: [u.name], tipScales: [u.fs] }
          : { scenarios: [u.name], scales: [u.fs] };
        const r = await evalAsync(b, `window.SkiLayout.run(${JSON.stringify(Object.assign(o, { keepScale: true, fixedSettle: opts.fixedSettle }))})`, 600000);
        r.timing.where = `${u.kind === "tip" ? "tooltip pass" : "check"} [${u.name}] fs ${u.fs}`;
        done.get(u.h)[u.idx] = r;
        last = u;
      } catch (e) {
        const r = emptyResult();
        r.fails.push({ scenario: u.name, fs: u.fs, what: (u.kind === "tip" ? "tooltip pass exception: " : "exception: ") + (e && e.message || e) });
        done.get(u.h)[u.idx] = r;
        await open(b, base, u.w, u.h); last = null;
      }
      if (opts.progress) opts.progress();
    }
  }
  await Promise.all(browsers.map((b, k) => worker(b, k)));
  for (const [, h] of sizes) for (const u of done.get(h)) mergeInto(results.get(h), u);
  return results;
}

/** --name=value */
const arg = (name) => { const a = process.argv.find((x) => x.startsWith(`--${name}=`)); return a ? a.slice(name.length + 3) : undefined; };

async function main() {
  const shotsArg = arg("shots");
  if (!fs.existsSync(path.join(ROOT, "build", "SkiResortMod.mjs"))) throw new Error("build/SkiResortMod.mjs missing: run npm run build first");
  await takeLock(process.argv.includes("--no-wait"));
  const t0 = Date.now();   // after the lock wait
  const quick = process.argv.includes("--quick");
  const height = arg("height") || (quick ? "1080" : undefined);
  const sizes = SIZES.filter(([, hh]) => !height || String(hh) === height);
  if (sizes.length === 0) throw new Error(`test-hub-layout: no size with height ${height} (${SIZES.map(([, hh]) => hh).join(", ")})`);
  const filter = arg("scenario");
  const auto = shotsArg || arg("workers") ? { n: Math.max(1, Number(arg("workers") || 1)), why: "" } : defaultWorkers();
  const nWorkers = auto.n;
  if (auto.why) console.log(`test-hub-layout: ${auto.why}`);
  const server = await startServer();
  const browsers = [];
  const cleanup = () => { for (const b of browsers) b.close(); server.child.kill(); };
  process.on("SIGINT", () => { cleanup(); process.exit(130); });
  const base = `http://127.0.0.1:${server.port}/harness/`;
  try {
    browsers.push(await startBrowser());
    const b = browsers[0];
    if (shotsArg) {
      await shots(b, base, path.resolve(shotsArg),
        process.argv.includes("--list=r2b") ? R2B : process.argv.includes("--list=r1f") ? R1F : process.argv.includes("--list=polish") ? POLISH : process.argv.includes("--list=icons2") ? ICONS2 : process.argv.includes("--list=tiers") ? TIERS : process.argv.includes("--list=icons") ? ICONS : process.argv.includes("--list=tips") ? TIPS616 : process.argv.includes("--list=t142") ? T142 : process.argv.includes("--list=intuitive") ? INTUITIVE : process.argv.includes("--list=merge2") ? MERGE2 : process.argv.includes("--list=merge") ? MERGE : process.argv.includes("--before") ? BEFORE : SHOTS);
      return 0;
    }
    const styleFails = styleFailures();
    console.log(`test-hub-layout: stylesheet: ${NOTE_CLASSES.length} note class(es) wrap, ${Object.keys(CUT_DECIDED).length} decided ellipsis class(es), ` +
      `${styleFails.length} failure(s)`);
    for (const f of styleFails) console.log(`  FAIL stylesheet: ${f}`);
    let failures = styleFails.length;
    // The scenario lists come from the page (harness/scenarios.js)
    await open(b, base, sizes[0][0], sizes[0][1]);
    const lists = await evalAsync(b, "({ check: window.SkiLayout.checkScenarios(), tip: window.SkiLayout.tipScenarios(), tipScales: window.SkiLayout.TIP_SCALES })", 10000);
    let names = lists.check, tipNames = lists.tip;
    if (filter) {
      const re = new RegExp(filter, "i");
      names = names.filter((n) => re.test(n));
      tipNames = tipNames.filter((n) => re.test(n));
      if (names.length + tipNames.length === 0) throw new Error(`test-hub-layout: --scenario=${filter} matches no scenario`);
    }
    const scales = [1, 1.25];
    const units = [...names.flatMap((name) => scales.map((fs) => ({ kind: "check", name, fs }))),
      ...tipNames.flatMap((name) => lists.tipScales.map((fs) => ({ kind: "tip", name, fs })))];
    const nUnits = sizes.length * units.length;
    const workers = Math.min(nWorkers, nUnits);
    browsers.push(...await Promise.all(Array.from({ length: workers - 1 }, () => startBrowser())));
    console.log(`test-hub-layout: ${sizes.length} size(s) x (${names.length} scenario(s) x ${scales.length} text scale(s) + tooltip pass ${tipNames.length} x ` +
      `${lists.tipScales.length}) = ${nUnits} unit(s) on ${workers} worker(s)` + (filter ? ` (scenario filter /${filter}/i: a partial run)` : ""));
    let finished = 0, lastTick = Date.now();
    const progress = () => { finished++; if (process.stdout.isTTY && Date.now() - lastTick > 5000) { lastTick = Date.now(); process.stdout.write(`  ... ${finished} / ${nUnits} unit(s), ${Math.round((Date.now() - t0) / 1000)} s\r`); } };
    const results = await runSharded(browsers, base, sizes, units, { units: unitWords(), fixedSettle: process.argv.includes("--fixed-settle"), progress });
    let settles = 0, capped = 0;
    for (const [w, h] of sizes) {
      const r = results.get(h);
      settles += r.timing.settles; capped += r.timing.capped;
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
      const clipped = Object.values(r.clipped).filter((c) => !shrinks.has(c.cls) && !PLAYER_NAMES.has(c.cls) && !(NAME_LINES.has(c.cls) && c.playerName));
      for (const c of Object.values(r.clipped).filter((x) => NAME_LINES.has(x.cls) && x.playerName)) {
        console.log(`  cut name line (allowed, the name "${c.playerName}" is the player's): [${c.cls}] +${c.over}px "${c.text}" (${c.where})`);
      }
      console.log(`  cut text: ${clipped.length} real, ${Object.keys(r.clipped).length - clipped.length} allowed (shrink-to-fit classes, player names)`);
      for (const [fs, x] of Object.entries(r.fullest)) console.log(`  fullest footer line at text scale ${fs}: ${x.full} % "${x.text}"`);
      for (const c of clipped) r.fails.push({ scenario: c.where, what: `text cut [${c.cls}] ${c.kind} +${c.over}px: "${c.text}"` });
      console.log(`  tooltips (text scale ${[1, 1.25, 1.5].join(" / ")}, ${r.tips.scenarios.length} scenario(s)): ${r.tips.n} hovered, ${r.tips.keys} with an icon key ` +
        `(at most ${r.tips.keyRows} key row(s)); key rows inside the box and every number with its unit, else FAIL below`);
      if (r.tips.n === 0 && tipNames.length > 0) r.fails.push({ scenario: "tooltip pass", what: "no tooltip opened: the pass checked nothing" });
      for (const f of r.fails) console.log(`  FAIL ${f.scenario || ""} fs ${f.fs || ""}: ${f.what}` + (f.overRem !== undefined ? ` (over ${f.overRem} rem, slack ${f.slackRem} rem)` : ""));
      if (process.argv.includes("--verbose")) for (const c of r.cases) console.log(`    ${c.scenario.slice(0, 22)} fs ${c.fs} view ${c.view} ${c.state}: over ${c.overRem} slack ${c.slackRem} header ${c.headerRem}`);
      failures += r.fails.length;
    }
    console.log(`test-hub-layout: ${nUnits} unit(s) on ${workers} worker(s) in ${Math.round((Date.now() - t0) / 1000)} s; ${settles} settle(s)` +
      (capped ? `, ${capped} never went quiet (capped at 60 frames: the hub pager kept changing the page)` : ""));
    // A settle that never went quiet is the hub's pager changing the page on and on (seen 2026-11-09: a divider row toggling every two
    // frames in the tooltip pass, mostly at text scale 1.5). It is measured as the old fixed waits measured it (at whatever state the page
    // was in), so it is reported, not failed. --verbose names the units.
    const where = sizes.flatMap(([, h]) => results.get(h).timing.cappedWhere.map((x) => `${h}: ${x}`));
    if (where.length) console.log(`  never quiet in ${where.length} unit(s)` + (process.argv.includes("--verbose") ? ":" : " (--verbose names them)"));
    if (process.argv.includes("--verbose")) for (const x of where) console.log(`    ${x}`);
    console.log(`test-hub-layout: ${failures === 0 ? "PASS" : "FAIL"} (${failures} failure(s))`);
    return failures === 0 ? 0 : 1;
  } finally {
    cleanup();
  }
}

main().then((code) => setTimeout(() => process.exit(code), 600), (e) => { console.error(e && e.exitCode ? e.message : String(e && e.stack || e)); setTimeout(() => process.exit(e && e.exitCode || 2), 600); });
