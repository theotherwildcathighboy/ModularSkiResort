/*
 * tools/check-us-english.js - US English in every player-facing text (T130, 2026-10-20; field test note 18, Richard: "remove ALL British
 * English, here and everywhere", he is American; DECISIONS 350). Written for this repo (MIT). Reads only; writes nothing.
 *
 * Scans the text a player can read:
 *   - mod/SkiResortMod/LocaleEN.cs and mod/SkiResortMod/Settings/LocaleEN.cs: every string literal outside a // comment;
 *   - mod/SkiResortUI/src/**: the English of every [key, english] locale tuple (hubText.ts and the views);
 *   - mod/SkiResortMod/**.cs (not obj/, bin/): the string literals of every Mod.Log / log Info / Warn / Error call (the mod log is
 *     something a player opens and posts);
 *   - docs-site/src/content/**.md(x): the prose (code fences skipped; LOCK regions are scanned too: a hit there is reported for
 *     Richard, never edited by an agent, CLAUDE.md docs-site rule).
 * T130 part 2 (2026-10-20) adds the text a developer reads:
 *   - the comments and docstrings of every .cs .ts .tsx .js .scss .py .ps1 .sh file under mod/, tests/, tools/, assets/blender, docs/;
 *   - the prose of every .md under docs/ (not docs/archive/), README / release / .claude / assets notes, code fences included.
 *   In comments and prose a token is NOT checked when it is an identifier or a path: it holds _ or a digit or an inner capital
 *   (colour_set, GladeOutlineColour), sits inside `backticks`, a URL, a file name or a path, or is followed by = (a parameter), or sits inside a quotation (a
 *   double-quoted span of 3+ words: Richard's words, a wiki page, a field-log line: never edited).
 *   A prose line that discusses spellings (it says "British", "US English", "T130", "spelling", or holds a British word AND its
 *   US form) is skipped. NOT scanned: string literals outside the player-facing places above, identifiers, docs/archive/, JSON data,
 *   tools/loop-identity/baseline-* (a frozen copy of an old source file), and the PENDING files below.
 * FAILS (exit 1) on a word of BRITISH below, matched case-insensitively at a word start; ALLOW holds the proper names that contain one
 * (another mod's or a vanilla name). Prints "file:line: word -> us" per hit.
 * Usage: node tools/check-us-english.js [--verbose]
 */
"use strict";
const fs = require("fs");
const path = require("path");

const ROOT = path.resolve(__dirname, "..");

// British form (a word start; "*" = any ending) -> the US form. Keep the list to spellings, not vocabulary.
const BRITISH = [
  ["colour*", "color"], ["grey*", "gray"], ["centre*", "center"], ["metre*", "meter"], ["kilometre*", "kilometer"],
  ["centimetre*", "centimeter"], ["litre*", "liter"], ["modelled", "modeled"], ["modelling", "modeling"], ["cancelled", "canceled"],
  ["cancelling", "canceling"], ["travelled", "traveled"], ["travelling", "traveling"], ["traveller*", "traveler"], ["labelled", "labeled"],
  ["labelling", "labeling"], ["levelled", "leveled"], ["levelling", "leveling"], ["signalled", "signaled"], ["signalling", "signaling"],
  ["fuelled", "fueled"], ["totalled", "totaled"], ["marvellous", "marvelous"], ["behaviour*", "behavior"], ["favour*", "favor"],
  ["honour*", "honor"], ["neighbour*", "neighbor"], ["harbour*", "harbor"], ["labour*", "labor"], ["flavour*", "flavor"],
  ["organis*", "organiz-"], ["realise*", "realize"], ["realised", "realized"], ["realising", "realizing"], ["recognis*", "recogniz-"],
  ["optimis*", "optimiz-"], ["minimis*", "minimiz-"], ["maximis*", "maximiz-"], ["prioritis*", "prioritiz-"], ["normalis*", "normaliz-"],
  ["initialis*", "initializ-"], ["customis*", "customiz-"], ["visualis*", "visualiz-"], ["utilis*", "utiliz-"], ["summaris*", "summariz-"],
  ["categoris*", "categoriz-"], ["finalis*", "finaliz-"], ["stabilis*", "stabiliz-"], ["synchronis*", "synchroniz-"], ["apologis*", "apologiz-"],
  ["analyse*", "analyze"], ["analysed", "analyzed"], ["analysing", "analyzing"], ["catalogue*", "catalog"], ["dialogue*", "dialog"],
  ["licence*", "license"], ["defence*", "defense"], ["offence*", "offense"], ["programme*", "program"], ["tyre*", "tire"], ["kerb*", "curb"],
  ["aluminium", "aluminum"], ["storey*", "story"], ["whilst", "while"], ["amongst", "among"], ["plough*", "plow"], ["mould*", "mold"],
  ["manoeuvr*", "maneuver"], ["artefact*", "artifact"], ["judgement*", "judgment"], ["ageing", "aging"], ["sceptic*", "skeptic"],
  ["fulfil", "fulfill"], ["enrol", "enroll"], ["cheque*", "check"], ["jewellery", "jewelry"], ["aeroplane*", "airplane"],
  // T130 part 2 additions: more -our / -re words, -ise verbs with their endings spelled out (a bare "stem*" would hit organism, optimism,
  // finalist), doubled-l forms, and the odd spellings found in docs and comments. "re:" = a raw regex (a word start is added).
  ["recolour*", "recolor"], ["armour*", "armor"], ["humour*", "humor"], ["rumour*", "rumor"], ["vigour*", "vigor"], ["odour*", "odor"],
  ["theatre*", "theater"], ["fibre*", "fiber"], ["calibre*", "caliber"], ["millimetre*", "millimeter"], ["decimetre*", "decimeter"],
  ["sulphur*", "sulfur"], ["analogue*", "analog"], ["maths", "math"], ["per cent", "percent"], ["practise*", "practice"],
  ["channelled", "channeled"], ["tunnelled", "tunneled"], ["equalled", "equaled"], ["dialled", "dialed"], ["shovelled", "shoveled"],
  ["greyscale", "grayscale"], ["centring", "centering"], ["acknowledgement*", "acknowledgment"], ["fulfilment", "fulfillment"],
  ["learnt", "learned"], ["spelt", "spelled"], ["burnt", "burned"], ["dreamt", "dreamed"], ["pretence", "pretense"], ["skilful", "skillful"],
  ["re:(?:raster|equal|quant|general|random|local|parameter|vector|serial|author|subsid|material|sanit|capital|neutral|memor|special|standard|character|digit|token|central|formal|legal|moder|harmon|scrutin|sympath|theor|fertil|industrial|hospital|monopol)is(?:e|es|ed|ing|er|ers|ation|ations|able|ability)", "-ize forms"],
  ["re:(?:emphas|parameter|paralys|catalys)(?:ise|ised|ises|ising|yse|ysed|yses|ysing)", "-ize / -yze forms"],
];
// Proper names that contain a British form (another mod's name, a vanilla prefab or locale key), and valid US words the word list's
// prefixes would catch (analyses = the plural noun; programmer; organism; optimism). Matched before the word list.
const ALLOW = [
  /Realistic Workplaces/g, /realisticWorkplaces/g,
  /\banalyses\b/gi, /\bprogrammers?\b/gi, /\borganisms?\b/gi, /\boptimis(?:m|t|ts|tic|tically)\b/gi, /\bfinalists?\b/gi,
];

const RE = BRITISH.map(([w, us]) => ({
  re: w.startsWith("re:") ? new RegExp("\\b" + w.slice(3), "gi")
    : new RegExp("\\b" + w.replace("*", "[a-z]*") + (w.endsWith("*") ? "" : "\\b"), "gi"),
  us,
}));

function hitsIn(text) {
  let t = text;
  for (const a of ALLOW) t = t.replace(a, (m) => " ".repeat(m.length));
  const out = [];
  for (const { re, us } of RE) {
    re.lastIndex = 0;
    let m;
    while ((m = re.exec(t))) out.push({ word: m[0], us });
  }
  return out;
}

/** C# string literals of one line (regular "..." and $"...", escapes skipped; verbatim @"" rare here and read the same way). */
function csLiterals(line) {
  const out = [];
  const re = /"((?:[^"\\]|\\.)*)"/g;
  let m;
  while ((m = re.exec(line))) out.push(m[1]);
  return out;
}
function stripLineComment(line) {
  // a // outside a string literal starts a comment
  let inStr = false;
  for (let i = 0; i < line.length; i++) {
    const c = line[i];
    if (c === "\\" && inStr) { i++; continue; }
    if (c === '"') inStr = !inStr;
    else if (!inStr && c === "/" && line[i + 1] === "/") return line.slice(0, i);
  }
  return line;
}
function walk(dir, pred, acc) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (e.name === "node_modules" || e.name === "obj" || e.name === "bin" || e.name.startsWith(".")) continue;
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, pred, acc);
    else if (pred(p)) acc.push(p);
  }
  return acc;
}

// ---- comments and prose (T130 part 2) -------------------------------------------------------------------------------------------
/** Offsets {s, e} of every comment (and, for Python, every bare-string docstring) in `text`. cs = C# string forms, rx = JS regex literals. */
function lexC(text, cs, rx) {
  const R = [], n = text.length; let i = 0;
  const canRegex = () => { let k = i - 1; while (k >= 0 && /[ \t]/.test(text[k])) k--; return k < 0 || "(,=:[!&|?{};+-*%<>~^".includes(text[k]) || /(?:return|typeof|case|in|of)$/.test(text.slice(Math.max(0, k - 6), k + 1)); };
  while (i < n) {
    const c = text[i], d = text[i + 1];
    if (c === "/" && d === "/") { let j = text.indexOf("\n", i); if (j < 0) j = n; R.push({ s: i + 2, e: j }); i = j; continue; }
    if (c === "/" && d === "*") { let j = text.indexOf("*/", i + 2); if (j < 0) j = n; R.push({ s: i + 2, e: j }); i = j + 2; continue; }
    if (cs && (c === "@" && d === '"' || c === "$" && d === "@" && text[i + 2] === '"' || c === "@" && d === "$" && text[i + 2] === '"')) {
      i = text.indexOf('"', i) + 1;
      for (;;) { const j = text.indexOf('"', i); if (j < 0) { i = n; break; } if (text[j + 1] === '"') { i = j + 2; continue; } i = j + 1; break; }
      continue;
    }
    if (cs && c === '"' && d === '"' && text[i + 2] === '"') { let q = 3; while (text[i + q] === '"') q++; const j = text.indexOf('"'.repeat(q), i + q); i = j < 0 ? n : j + q; continue; }
    if (c === '"' || (cs && c === "$" && d === '"')) { if (c === "$") i++; i++; while (i < n && text[i] !== '"' && text[i] !== "\n") { if (text[i] === "\\") i++; i++; } i++; continue; }
    if (c === "'") {
      if (cs) { i++; if (text[i] === "\\") i++; i += 2; continue; }
      i++; while (i < n && text[i] !== "'" && text[i] !== "\n") { if (text[i] === "\\") i++; i++; } i++; continue;
    }
    if (c === "`" && !cs) {
      i++; let depth = 0;
      while (i < n) { if (text[i] === "\\") { i += 2; continue; } if (text[i] === "`" && depth === 0) { i++; break; } if (text[i] === "$" && text[i + 1] === "{") { depth++; i += 2; continue; } if (text[i] === "}" && depth > 0) { depth--; i++; continue; } i++; }
      continue;
    }
    if (c === "/" && rx && canRegex()) {
      let j = i + 1, cls = false;
      while (j < n && text[j] !== "\n") { if (text[j] === "\\") { j += 2; continue; } if (text[j] === "[") cls = true; else if (text[j] === "]") cls = false; else if (text[j] === "/" && !cls) break; j++; }
      if (text[j] === "/") { i = j + 1; continue; }
    }
    i++;
  }
  return R;
}
function lexPy(text) {
  const R = [], n = text.length; let i = 0, depth = 0, stmtStart = true;
  while (i < n) {
    const c = text[i];
    if (c === "#") { let j = text.indexOf("\n", i); if (j < 0) j = n; R.push({ s: i + 1, e: j }); i = j; continue; }
    if (c === "\n") { if (depth === 0) stmtStart = true; i++; continue; }
    if (c === " " || c === "\t" || c === "\r") { i++; continue; }
    if (c === "\\" && (text[i + 1] === "\n" || text[i + 1] === "\r")) { i += 2; continue; }
    const m = /^([rRbBuUfF]{0,3})("""|'''|"|')/.exec(text.slice(i, i + 6));
    if (m) {
      const q = m[2], start = i + m[0].length; let j = start;
      for (;;) { if (j >= n) break; if (text[j] === "\\") { j += 2; continue; } if (text.startsWith(q, j)) break; if (q.length === 1 && text[j] === "\n") break; j++; }
      const after = j + q.length;
      if (stmtStart && depth === 0) { let k = after; while (text[k] === " " || text[k] === "\t") k++; if (k >= n || "\n\r#;".includes(text[k])) R.push({ s: start, e: j }); }
      stmtStart = false; i = after; continue;
    }
    stmtStart = false;
    if ("([{".includes(c)) depth++; else if (")]}".includes(c)) depth = Math.max(0, depth - 1); else if (c === ";" && depth === 0) stmtStart = true;
    i++;
  }
  return R;
}
function lexPs(text) {
  const R = [], n = text.length; let i = 0;
  while (i < n) {
    const c = text[i], d = text[i + 1];
    if (c === "<" && d === "#") { let j = text.indexOf("#>", i + 2); if (j < 0) j = n; R.push({ s: i + 2, e: j }); i = j + 2; continue; }
    if (c === "#") { let j = text.indexOf("\n", i); if (j < 0) j = n; R.push({ s: i + 1, e: j }); i = j; continue; }
    if (c === "@" && (d === "'" || d === '"') && /[\r\n]/.test(text[i + 2] || "")) { const j = text.indexOf("\n" + d + "@", i); i = j < 0 ? n : j + 3; continue; }
    if (c === "'") { i++; while (i < n) { if (text[i] === "'") { if (text[i + 1] === "'") { i += 2; continue; } break; } i++; } i++; continue; }
    if (c === '"') { i++; while (i < n) { if (text[i] === "`") { i += 2; continue; } if (text[i] === '"') { if (text[i + 1] === '"') { i += 2; continue; } break; } i++; } i++; continue; }
    i++;
  }
  return R;
}
function lexSh(text) {
  const R = [], n = text.length; let i = 0;
  while (i < n) {
    const c = text[i];
    if (c === "#" && (i === 0 || /[\s;]/.test(text[i - 1]))) { let j = text.indexOf("\n", i); if (j < 0) j = n; R.push({ s: i + 1, e: j }); i = j; continue; }
    if (c === "'") { const j = text.indexOf("'", i + 1); i = j < 0 ? n : j + 1; continue; }
    if (c === '"') { i++; while (i < n && text[i] !== '"') { if (text[i] === "\\") i++; i++; } i++; continue; }
    i++;
  }
  return R;
}
const SPELLING_TALK = /British|US English|T130|check-us-english|spelling/i;
/** Hits in one line of comment / prose. Identifiers, paths, `code` and lines that discuss spellings are not checked. */
function proseHits(line) {
  if (SPELLING_TALK.test(line)) return [];
  let text = line.replace(/`[^`]*`/g, (m) => " ".repeat(m.length));
  // a quotation (a double-quoted span of 3+ words: Richard's words, a wiki, a log line) keeps its spelling; an odd quote count = no span
  const qi = []; for (let i = 0; i < text.length; i++) if (text[i] === '"' && text[i - 1] !== "\\" && text[i + 1] !== '"' && text[i - 1] !== '"') qi.push(i);
  if (qi.length % 2 === 0) {
    for (let k = 0; k < qi.length; k += 2) {
      const inner = text.slice(qi[k] + 1, qi[k + 1]);
      if (inner.length >= 12 && inner.trim().split(/[ \t]+/).length >= 3) text = text.slice(0, qi[k]) + " ".repeat(qi[k + 1] - qi[k] + 1) + text.slice(qi[k + 1] + 1);
    }
  }
  const out = [];
  for (const m of text.matchAll(/[A-Za-z][A-Za-z0-9_]*/g)) {
    const tok = m[0];
    if (/[_0-9]/.test(tok) || /[a-z][A-Z]/.test(tok)) continue;
    const hs = hitsIn(tok);
    if (hs.length === 0) continue;
    let a = m.index; while (a > 0 && !/[\s|]/.test(text[a - 1])) a--;
    let b = m.index + tok.length; while (b < text.length && !/[\s|]/.test(text[b])) b++;
    const chunk = text.slice(a, b).replace(/^[("'*[{<]+|[)"'*\]}>,.;:!?]+$/g, "");
    if (/[=\\]/.test(chunk) || /^[./~]/.test(chunk) || /\w\/\w+\/\w/.test(chunk) || (/\.[A-Za-z]{1,5}$/.test(chunk) && chunk.includes("/")) ||
        (/\w\.\w/.test(chunk.replace(/[A-Za-z]\.$/, "")) && /\.[a-z]{2,4}\b/i.test(chunk))) continue;
    out.push(...hs);
  }
  if (/\bper cent\b/i.test(text)) out.push({ word: "per cent", us: "percent" });
  // a line holding a British word AND its US form is discussing spellings
  return out.filter((h) => h.us.includes("-") || !new RegExp("\\b" + h.us + "\\b", "i").test(text));
}
const SRC_SKIP = new Set(["node_modules", "obj", "bin", "build", "dist", "types", "archive", "__pycache__", "decompile"]);
function walkSrc(dir, pred, acc) {
  if (!fs.existsSync(dir)) return acc;
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (SRC_SKIP.has(e.name) || e.name.startsWith("baseline-") || (e.name.startsWith(".") && e.name !== ".claude")) continue;
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walkSrc(p, pred, acc); else if (pred(p)) acc.push(p);
  }
  return acc;
}
// Files another session still owns (the tower-number tiles awaited Richard's verdict when T130 part 2 ran): skipped, and counted in the
// last line. Delete an entry once its comments are converted.
// Documents about the spellings themselves (they list British words as their subject): not scanned.
const SPELLING_DOCS = ["docs/research/us-english-sweep-2026-10-20.md"];
const PENDING = ["assets/blender/number_tiles.py", "mod/SkiResortMod/Systems/TowerNumberTiles.cs", "tests/LiftBatch/TowerNumberTests.cs"];

// --self-test: the gate's own fixtures (each British sample must hit, each US / allowed sample must not).
if (process.argv.includes("--self-test")) {
  const bad = ["difficulty colours", "Too flat: grey.", "at its centre", "15 metres", "Some trees cancelled", "an aluminium tube", "modelled into"];
  const good = ["difficulty colors", "gray", "center", "meters", "canceled", "aluminum", "modeled", "Detected: Realistic Workplaces.", "greyhound"];
  const missed = bad.filter((s) => hitsIn(s).length === 0);
  const wrong = good.filter((s) => s !== "greyhound" && hitsIn(s).length > 0);
  // "greyhound" is a known false positive of the grey* prefix rule: it must hit, so a looser rule is noticed.
  if (hitsIn("greyhound").length === 0) wrong.push("greyhound (prefix rule changed)");
  // comment / prose fixtures (proseHits)
  const cBad = ["the centre of the tile", "kit colour variants", "rasterise the quad", "a two-storey lodge", "equalised areas", "texel centres, metres", "grey-white"];
  const cGood = ["the center of the tile", "colour_set keeps its name", "the `centre` argument", "GladeOutlineColour is a constant", "see kit-colour-variants.md",
    "docs/research/colour/x", "pass centre=True", "three analyses", "a programmer", "an organism", "optimism", "the British colour spelling", "colour / color", "colour->color",
    'Richard: "a colour that makes sense for that building" is the rule'];
  for (const s of cBad) if (proseHits(s).length === 0) missed.push("comment: " + s);
  for (const s of cGood) if (proseHits(s).length > 0) wrong.push("comment: " + s);
  console.log(`check-us-english --self-test: ${bad.length + cBad.length - missed.length} / ${bad.length + cBad.length} British samples caught, ${wrong.length} wrong`);
  for (const s of missed) console.log(`  MISSED ${s}`);
  for (const s of wrong) console.log(`  WRONG  ${s}`);
  process.exit(missed.length + wrong.length === 0 ? 0 : 1);
}

const hits = [];
const add =(file, line, list) => { for (const h of list) hits.push({ file: path.relative(ROOT, file), line, word: h.word, us: h.us }); };
let scanned = 0;

// 1. locale sources
for (const rel of ["mod/SkiResortMod/LocaleEN.cs", "mod/SkiResortMod/Settings/LocaleEN.cs"]) {
  const f = path.join(ROOT, rel);
  fs.readFileSync(f, "utf8").split(/\r?\n/).forEach((l, i) => {
    if (/^\s*\/\//.test(l)) return;
    for (const s of csLiterals(stripLineComment(l))) { scanned++; add(f, i + 1, hitsIn(s)); }
  });
}
// 2. hub tuples
for (const f of walk(path.join(ROOT, "mod/SkiResortUI/src"), (p) => /\.tsx?$/.test(p), [])) {
  fs.readFileSync(f, "utf8").split(/\r?\n/).forEach((l, i) => {
    const re = /\[\s*"[A-Za-z][\w.]*(?:\[[^\]"]*\])?(?::\d+)?"\s*,\s*"((?:[^"\\]|\\.)*)"\s*\]/g;
    let m;
    while ((m = re.exec(l))) { scanned++; add(f, i + 1, hitsIn(m[1])); }
  });
}
// 3. C# log calls (a call may span lines: from Log.<Level>( to the closing ");")
for (const f of walk(path.join(ROOT, "mod/SkiResortMod"), (p) => p.endsWith(".cs"), [])) {
  const lines = fs.readFileSync(f, "utf8").split(/\r?\n/);
  let inCall = false;
  lines.forEach((raw, i) => {
    const l = stripLineComment(raw);
    if (!inCall && /\b[Ll]og\.(Info|Warn|Error|Debug|Verbose|Critical|Fatal|Trace)(Format)?\s*\(/.test(l)) inCall = true;
    if (!inCall) return;
    for (const s of csLiterals(l)) { scanned++; add(f, i + 1, hitsIn(s)); }
    if (/\)\s*;\s*$/.test(l.trim())) inCall = false;
  });
}
// 4. docs-site prose
const docsDir = path.join(ROOT, "docs-site/src/content");
if (fs.existsSync(docsDir)) {
  for (const f of walk(docsDir, (p) => /\.mdx?$/.test(p), [])) {
    let fence = false;
    fs.readFileSync(f, "utf8").split(/\r?\n/).forEach((l, i) => {
      if (/^\s*```/.test(l)) { fence = !fence; return; }
      if (fence) return;
      scanned++;
      add(f, i + 1, hitsIn(l));
    });
  }
}

// 5. comments and docstrings of the source (T130 part 2)
const pending = new Set(PENDING);
let pendingSeen = 0;
const rel = (f) => path.relative(ROOT, f).split(path.sep).join("/");
const SRC_EXT = /\.(cs|tsx?|jsx?|mjs|cjs|scss|css|py|ps1|sh)$/i;
const srcFiles = [];
for (const d of ["mod", "tests", "tools", "assets/blender", "docs", "docs-site/src", "docs-site/tools"]) walkSrc(path.join(ROOT, d), (p) => SRC_EXT.test(p), srcFiles);
for (const f of srcFiles) {
  const r = rel(f);
  if (r === "tools/check-us-english.js") continue;
  if (pending.has(r)) { pendingSeen++; continue; }
  const text = fs.readFileSync(f, "utf8");
  let ranges;
  if (/\.cs$/i.test(f)) ranges = lexC(text, true, false);
  else if (/\.(tsx?|jsx?|mjs|cjs)$/i.test(f)) ranges = lexC(text, false, true);
  else if (/\.(scss|css)$/i.test(f)) ranges = lexC(text, false, false);
  else if (/\.py$/i.test(f)) ranges = lexPy(text);
  else if (/\.ps1$/i.test(f)) ranges = lexPs(text);
  else ranges = lexSh(text);
  for (const rg of ranges) {
    const seg = text.slice(rg.s, rg.e);
    const base = text.slice(0, rg.s).split("\n").length;
    seg.split("\n").forEach((l, i) => { scanned++; add(f, base + i, proseHits(l)); });
  }
}
// 6. prose of the markdown notes: docs/ (not docs/archive/), READMEs, release/, .claude/, assets/ and tests/ notes (code fences too)
const mdFiles = [];
for (const d of ["docs", "release", ".claude", "assets/blender", "assets/import", "assets/we", "assets/external", "mod", "tools", "tests"]) walkSrc(path.join(ROOT, d), (p) => /\.md$/i.test(p), mdFiles);
for (const f of ["README.md", "CLAUDE.md", "docs-site/README.md"]) if (fs.existsSync(path.join(ROOT, f))) mdFiles.push(path.join(ROOT, f));
for (const f of mdFiles) {
  if (SPELLING_DOCS.includes(rel(f))) continue;
  fs.readFileSync(f, "utf8").split(/\r?\n/).forEach((l, i) => { scanned++; add(f, i + 1, proseHits(l)); });
}

for (const h of hits) console.log(`${h.file}:${h.line}: "${h.word}" -> ${h.us}`);
if (pendingSeen) console.log(`check-us-english: ${pendingSeen} PENDING file(s) not scanned (see PENDING in this file)`);
console.log(`check-us-english: ${scanned} text(s) scanned, ${hits.length} British spelling(s) ${hits.length === 0 ? "- PASS" : "- FAIL"}`);
process.exit(hits.length === 0 ? 0 : 1);
