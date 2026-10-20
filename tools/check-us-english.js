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
 * Code comments and docs/ are NOT scanned (a later sweep, T130 part 2).
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
];
// Proper names that contain a British form (another mod's name, a vanilla prefab or locale key). Matched before the word list.
const ALLOW = [/Realistic Workplaces/g, /realisticWorkplaces/g];

const RE = BRITISH.map(([w, us]) => ({
  re: new RegExp("\\b" + w.replace("*", "[a-z]*") + (w.endsWith("*") ? "" : "\\b"), "gi"),
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

// --self-test: the gate's own fixtures (each British sample must hit, each US / allowed sample must not).
if (process.argv.includes("--self-test")) {
  const bad = ["difficulty colours", "Too flat: grey.", "at its centre", "15 metres", "Some trees cancelled", "an aluminium tube", "modelled into"];
  const good = ["difficulty colors", "gray", "center", "meters", "canceled", "aluminum", "modeled", "Detected: Realistic Workplaces.", "greyhound"];
  const missed = bad.filter((s) => hitsIn(s).length === 0);
  const wrong = good.filter((s) => s !== "greyhound" && hitsIn(s).length > 0);
  // "greyhound" is a known false positive of the grey* prefix rule: it must hit, so a looser rule is noticed.
  if (hitsIn("greyhound").length === 0) wrong.push("greyhound (prefix rule changed)");
  console.log(`check-us-english --self-test: ${bad.length - missed.length} / ${bad.length} British samples caught, ${wrong.length} wrong`);
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

for (const h of hits) console.log(`${h.file}:${h.line}: "${h.word}" -> ${h.us}`);
console.log(`check-us-english: ${scanned} text(s) scanned, ${hits.length} British spelling(s) ${hits.length === 0 ? "- PASS" : "- FAIL"}`);
process.exit(hits.length === 0 ? 0 : 1);
