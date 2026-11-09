/*
 * tools/export-hub-symbols.js - the docs-site symbol table, generated from the hub's icon table, so the docs and the hub never disagree.
 *
 *   node tools/export-hub-symbols.js           write docs-site/src/data/hub-symbols.json and docs-site/public/icons/hub/*.svg
 *   node tools/export-hub-symbols.js --check   change nothing, exit 1 when any output differs from what the sources produce
 *
 * Sources (read as text, no compiler):
 *   - mod/SkiResortUI/src/hub/hubIcons.ts: HUB_ICON (name, image, wide, word entry), HUB_ICON_GROUPS (legend groups, in order) and
 *     the `import x from "./icons/y.svg"` lines.
 *   - mod/SkiResortUI/src/hub/hubText.ts: the English text of each word entry and group title (the hub's own words).
 *   - docs-site/src/data/hub-symbol-meanings.json: the one hand-written part, one meaning per icon NAME. It must name exactly the
 *     HUB_ICON names (a new icon without a meaning fails, a removed icon with a meaning fails).
 * Outputs:
 *   - docs-site/src/data/hub-symbols.json: the groups and their symbols, in legend order. docs-site/src/components/HubSymbols.astro
 *     renders it.
 *   - docs-site/public/icons/hub/<file>.svg: byte copies of our own icon files. An icon the hub takes from the game (a `Media/` path)
 *     is NEVER copied: game art is not ours to publish. The docs show our own drawing of it instead,
 *     docs-site/src/hub-symbol-art/<NAME>.svg (required for every game icon, refused for any other).
 * Every copied file must come from src/hub/icons/ or the art folder, and none may equal an installed game file
 * (tools/check-no-vanilla-art.js checks the whole repo).
 * The check also fails when no docs page renders <HubSymbols />.
 */
"use strict";
const fs = require("fs");
const path = require("path");

const ROOT = path.resolve(__dirname, "..");
const HUB = path.join(ROOT, "mod", "SkiResortUI", "src", "hub");
const ICONS_TS = path.join(HUB, "hubIcons.ts");
const TEXT_TS = path.join(HUB, "hubText.ts");
const DOCS = path.join(ROOT, "docs-site");
const MEANINGS = path.join(DOCS, "src", "data", "hub-symbol-meanings.json");
const OUT_JSON = path.join(DOCS, "src", "data", "hub-symbols.json");
const OUT_ICONS = path.join(DOCS, "public", "icons", "hub");
const CONTENT = path.join(DOCS, "src", "content", "docs");
const DOCS_ART = path.join(DOCS, "src", "hub-symbol-art");

const rel = (p) => path.relative(ROOT, p).split(path.sep).join("/");
class ExportError extends Error {}
const fail = (msg) => { throw new ExportError(msg); };
const read = (p) => { try { return fs.readFileSync(p, "utf8").replace(/^﻿/, ""); } catch (e) { return fail(`cannot read ${rel(p)}: ${e.message}`); } };

/** The generated outputs: { json: string, svgs: Map<file, Buffer> }. */
function build() {
  const iconsTs = read(ICONS_TS);
  const textTs = read(TEXT_TS);

  const imports = new Map();
  for (const m of iconsTs.matchAll(/^import (\w+) from "\.\/icons\/([\w-]+\.svg)";/gm)) imports.set(m[1], m[2]);

  const tableBlock = (iconsTs.split("export const HUB_ICON:")[1] || "").split("};")[0];
  if (!tableBlock) fail(`${rel(ICONS_TS)}: HUB_ICON table not found`);
  const icons = new Map();
  for (const m of tableBlock.matchAll(/^\s*(\w+): \{ src: ([^,]+), wide: (true|false), word: HUB_LOC\.(\w+) \},?\s*$/gm)) {
    const src = m[2].trim();
    let file = null, game = null;
    const lit = /^"([^"]+)"$/.exec(src);
    if (lit) {
      if (!lit[1].startsWith("Media/")) fail(`HUB_ICON.${m[1]}: a string src must be a vanilla Media/ path`);
      game = path.basename(lit[1]).replace(/\.svg$/i, "");
    } else if (imports.has(src)) file = imports.get(src);
    else fail(`HUB_ICON.${m[1]}: src ${src} is neither a Media/ path nor an imported ./icons/*.svg`);
    icons.set(m[1], { file, game, wide: m[3] === "true", wordEntry: m[4] });
  }
  const entryCount = (tableBlock.match(/\{ src:/g) || []).length;
  if (!icons.size || entryCount !== icons.size) fail(`${rel(ICONS_TS)}: read ${icons.size} HUB_ICON entries of ${entryCount} (an entry is not on one line in the expected form)`);

  const loc = new Map();
  for (const m of textTs.matchAll(/^\s*(\w+): \["(SkiResort\.Hub\.[^"]+)", "((?:[^"\\]|\\.)*)"\],?\s*$/gm)) loc.set(m[1], { key: m[2], en: JSON.parse(`"${m[3]}"`) });
  const en = (name) => loc.get(name) || fail(`${rel(TEXT_TS)}: HUB_LOC.${name} not found`);

  const groupsBlock = (iconsTs.split("export const HUB_ICON_GROUPS")[1] || "").split("];")[0];
  const groups = [];
  const seen = new Set();
  for (const m of groupsBlock.matchAll(/\{ title: HUB_LOC\.(\w+), names: \[([^\]]*)\] \}/g)) {
    const names = [...m[2].matchAll(/"(\w+)"/g)].map((x) => x[1]);
    groups.push({ title: en(m[1]).en, titleKey: en(m[1]).key, names });
    for (const n of names) {
      if (!icons.has(n)) fail(`HUB_ICON_GROUPS names "${n}", which is not in HUB_ICON`);
      if (seen.has(n)) fail(`HUB_ICON_GROUPS lists "${n}" twice`);
      seen.add(n);
    }
  }
  for (const n of icons.keys()) if (!seen.has(n)) fail(`HUB_ICON.${n} is in no legend group (HUB_ICON_GROUPS)`);

  let meanings;
  try { meanings = JSON.parse(read(MEANINGS)); } catch (e) { if (e instanceof ExportError) throw e; fail(`${rel(MEANINGS)}: ${e.message}`); }
  const missing = [...icons.keys()].filter((n) => typeof meanings[n] !== "string" || !meanings[n].trim());
  const extra = Object.keys(meanings).filter((n) => !icons.has(n));
  if (missing.length) fail(`${rel(MEANINGS)}: no meaning for ${missing.join(", ")}`);
  if (extra.length) fail(`${rel(MEANINGS)}: meaning for unknown icon(s) ${extra.join(", ")}`);

  const art = fs.existsSync(DOCS_ART) ? fs.readdirSync(DOCS_ART).filter((f) => f.endsWith(".svg")).map((f) => f.slice(0, -4)) : [];
  for (const n of art) {
    if (!icons.has(n)) fail(`${rel(DOCS_ART)}/${n}.svg: no hub icon "${n}"`);
    if (!icons.get(n).game) fail(`${rel(DOCS_ART)}/${n}.svg: "${n}" is our own art already; the docs copy src/hub/icons/${icons.get(n).file}`);
  }
  for (const [n, ic] of icons) if (ic.game && !art.includes(n)) fail(`game icon "${n}" (${ic.game}) has no docs drawing ${rel(DOCS_ART)}/${n}.svg (game art is never copied)`);
  const svgs = new Map();
  const out = {
    _generated: "tools/export-hub-symbols.js from mod/SkiResortUI/src/hub/hubIcons.ts and hubText.ts. Do not edit: run the script.",
    groups: groups.map((g) => ({
      title: g.title,
      titleKey: g.titleKey,
      symbols: g.names.map((n) => {
        const ic = icons.get(n);
        const out = ic.file || `${n}.svg`;
        if (svgs.has(out)) fail(`two symbols write icons/hub/${out}`);
        svgs.set(out, fs.readFileSync(ic.file ? path.join(HUB, "icons", ic.file) : path.join(DOCS_ART, `${n}.svg`)));
        return { name: n, tag: `<icon:${n}>`, word: en(ic.wordEntry).en, wordKey: en(ic.wordEntry).key, meaning: meanings[n],
          image: `icons/hub/${out}`, inGame: ic.game ? `game icon ${ic.game}` : "this drawing", wide: ic.wide };
      }),
    })),
  };
  return { json: JSON.stringify(out, null, 2) + "\n", svgs, count: icons.size, groups: groups.length };
}

function pagesUsingTable() {
  const out = [];
  const walk = (dir) => {
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, e.name);
      if (e.isDirectory()) walk(full);
      else if (/\.mdx$/.test(e.name) && /<HubSymbols\s*\/>/.test(fs.readFileSync(full, "utf8"))) out.push(rel(full));
    }
  };
  walk(CONTENT);
  return out;
}

function main() {
  const check = process.argv.includes("--check");
  const r = build();
  const pages = pagesUsingTable();
  if (check) {
    const diffs = [];
    const onDisk = fs.existsSync(OUT_JSON) ? fs.readFileSync(OUT_JSON, "utf8") : null;
    if (onDisk !== r.json) diffs.push(`${rel(OUT_JSON)} ${onDisk === null ? "missing" : "differs"}`);
    const have = fs.existsSync(OUT_ICONS) ? fs.readdirSync(OUT_ICONS).filter((f) => f.endsWith(".svg")) : [];
    for (const [f, buf] of r.svgs) {
      const p = path.join(OUT_ICONS, f);
      if (!fs.existsSync(p)) diffs.push(`${rel(p)} missing`);
      else if (!fs.readFileSync(p).equals(buf)) diffs.push(`${rel(p)} differs from src/hub/icons/${f}`);
    }
    for (const f of have) if (!r.svgs.has(f)) diffs.push(`${rel(path.join(OUT_ICONS, f))} is not a hub icon any more`);
    if (!pages.length) diffs.push("no docs page renders <HubSymbols />");
    if (diffs.length) {
      for (const d of diffs) console.error(`export-hub-symbols: FAIL ${d}`);
      console.log(`export-hub-symbols: FAIL ${diffs.length} difference(s); run node tools/export-hub-symbols.js`);
      process.exit(1);
    }
    console.log(`export-hub-symbols: PASS ${r.count} symbols in ${r.groups} groups, ${r.svgs.size} SVG copies current, shown on ${pages.join(", ")}`);
    return;
  }
  fs.mkdirSync(path.dirname(OUT_JSON), { recursive: true });
  fs.mkdirSync(OUT_ICONS, { recursive: true });
  fs.writeFileSync(OUT_JSON, r.json);
  for (const f of fs.readdirSync(OUT_ICONS)) if (f.endsWith(".svg") && !r.svgs.has(f)) fs.unlinkSync(path.join(OUT_ICONS, f));
  for (const [f, buf] of r.svgs) fs.writeFileSync(path.join(OUT_ICONS, f), buf);
  console.log(`export-hub-symbols: wrote ${rel(OUT_JSON)} (${r.count} symbols, ${r.groups} groups) and ${r.svgs.size} SVG(s) to ${rel(OUT_ICONS)}` +
    (pages.length ? "" : " (warning: no docs page renders <HubSymbols /> yet)"));
}

try { main(); } catch (e) {
  if (e instanceof ExportError) { console.log(`export-hub-symbols: FAIL ${e.message}`); process.exit(1); }
  throw e;
}
