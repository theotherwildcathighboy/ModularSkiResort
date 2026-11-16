/*
 * tools/test-hub-look.js - the hub look check (round 2, DECISIONS 669). It diffs what the hub DRAWS against a committed baseline.
 *
 *   node tools/test-hub-look.js                   run the audit states, compare, exit 1 on any difference (one line per element)
 *   node tools/test-hub-look.js --write-baseline  rewrite tools/hub-look-baseline.txt from the current build
 *   node tools/test-hub-look.js --from=<dir>      compare the *.json of an earlier `--list=uiaudit --measure` run (no browser)
 *
 * How: it runs `node tools/test-hub-layout.js --shots=<tmp> --list=uiaudit --measure` (20 states at 1080 and 2160, headless Chrome, the
 * harness only, no game process) and reads the measure JSON next to each shot (tools/ui-audit-measure.js). Per state it keeps, for every
 * drawn control, the height, padding, font size, radius, background, text color and border (sizes in rem), and every font size and text
 * color that draws text. Positions and widths are left out: a longer label moves them without a look change.
 * A difference fails unless the baseline changes in the same commit. So a button that grows from 22.4 to 26 rem, a new font size or a
 * new text color shows as one line, not as a screenshot hunt. Run it after `npm run build` (the harness mounts build/).
 *
 * It cannot run in CI. The harness needs the game's own index.css (served from the Steam folder, the theme variables and fonts) and the
 * bundle build needs the game's cs2 type declarations (sync-types). Both come from the game install. Run it locally before a UI merge.
 * Baseline: tools/hub-look-baseline.txt, one line per element: `<state> | <kind> | <key> | <values>`. Rewrite it when a look change is
 * meant (and say so in the commit), and at each release so it is the look of the release.
 */
"use strict";
const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync } = require("child_process");

const BASELINE = path.join(__dirname, "hub-look-baseline.txt");
const strip = (c) => String(c).replace(/_[A-Za-z0-9_-]{5}(?=\.|$)/g, "");   // the CSS module hash of a class (our probe cuts only some)

/** One measure JSON -> the lines of its state. Duplicate keys (two buttons of one class and text) get #2, #3. */
function linesOf(state, m) {
  const out = [];
  const seen = new Map();
  for (const c of m.controls || []) {
    const base = `${strip(c.cls)}${c.text ? ` "${c.text}"` : ""}`;
    const n = (seen.get(base) || 0) + 1;
    seen.set(base, n);
    out.push(`${state} | control | ${base}${n > 1 ? ` #${n}` : ""} | h=${c.h}; pad=${c.pad}; font=${c.font}; radius=${c.radius}; bg=${c.bg}; color=${c.color}; border=${c.border || "-"}`);
  }
  for (const [f, cls] of Object.entries(m.fonts || {}).sort((a, b) => Number(a[0]) - Number(b[0]))) out.push(`${state} | font | ${f} | ${cls.map(strip).sort().join(" ")}`);
  for (const [col, cls] of Object.entries(m.colors || {}).sort()) out.push(`${state} | color | ${col} | ${cls.map(strip).sort().join(" ")}`);
  return out;
}

function measureDir(dir) {
  const lines = [];
  for (const f of fs.readdirSync(dir).filter((n) => n.endsWith(".json")).sort()) {
    lines.push(...linesOf(f.replace(/\.json$/, ""), JSON.parse(fs.readFileSync(path.join(dir, f), "utf8"))));
  }
  return lines;
}

/** `<state> | <kind> | <key>` -> values, per line. */
function index(lines) {
  const m = new Map();
  for (const l of lines) {
    const p = l.split(" | ");
    m.set(p.slice(0, 3).join(" | "), p.slice(3).join(" | "));
  }
  return m;
}

function diff(baseLines, nowLines) {
  const a = index(baseLines), b = index(nowLines), out = [];
  for (const [k, v] of b) {
    if (!a.has(k)) out.push(`ADDED   ${k} | ${v}`);
    else if (a.get(k) !== v) {
      const kv = (x) => Object.fromEntries(x.split("; ").map((p) => { const i = p.indexOf("="); return i < 0 ? [p, ""] : [p.slice(0, i), p.slice(i + 1)]; }));
      const ctl = k.includes(" | control | ");
      const fa = ctl ? kv(a.get(k)) : {}, fb = ctl ? kv(v) : {};
      const what = Object.keys({ ...fa, ...fb }).filter((x) => fa[x] !== fb[x]).map((x) => `${x} ${fa[x]} -> ${fb[x]}`).join(", ");
      out.push(`CHANGED ${k} | ${what || `${a.get(k)} -> ${v}`}`);
    }
  }
  for (const k of a.keys()) if (!b.has(k)) out.push(`REMOVED ${k}`);
  return out;
}

function main() {
  const argv = process.argv.slice(2);
  const write = argv.includes("--write-baseline");
  const from = (argv.find((x) => x.startsWith("--from=")) || "").slice(7);
  let dir = from, tmp = null;
  if (!dir) {
    tmp = fs.mkdtempSync(path.join(os.tmpdir(), "hub-look-"));
    dir = tmp;
    const r = spawnSync(process.execPath, [path.join(__dirname, "test-hub-layout.js"), `--shots=${tmp}`, "--list=uiaudit", "--measure"], { stdio: ["ignore", "pipe", "inherit"], encoding: "utf8", maxBuffer: 1 << 26 });
    if (r.status !== 0) { console.error(`test-hub-look: the shot run failed (exit ${r.status})`); process.exit(2); }
  }
  try {
    const now = measureDir(dir);
    if (!now.length) { console.error("test-hub-look: no measure JSON found"); process.exit(2); }
    if (write) {
      fs.writeFileSync(BASELINE, now.join("\n") + "\n");
      console.log(`test-hub-look: wrote ${path.relative(process.cwd(), BASELINE)} (${now.length} line(s), ${new Set(now.map((l) => l.split(" | ")[0])).size} state(s))`);
      return;
    }
    if (!fs.existsSync(BASELINE)) { console.error("test-hub-look: no baseline: run with --write-baseline"); process.exit(2); }
    const base = fs.readFileSync(BASELINE, "utf8").split("\n").filter(Boolean);
    const d = diff(base, now);
    for (const l of d) console.log(`  ${l}`);
    console.log(`test-hub-look: ${now.length} element line(s), ${d.length} difference(s) from the baseline` +
      (d.length ? " - FAIL (a meant change: rerun with --write-baseline and commit the baseline with it)" : " - PASS"));
    process.exitCode = d.length ? 1 : 0;
  } finally {
    if (tmp) fs.rmSync(tmp, { recursive: true, force: true });
  }
}

if (require.main === module) main();
module.exports = { linesOf, diff, strip };
