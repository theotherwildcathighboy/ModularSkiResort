/*
 * tools/export-locale.js - writes locale/en-US.json, the translation template: every key the mod registers -> its English text.
 *
 *   node tools/export-locale.js           write locale/en-US.json
 *   node tools/export-locale.js --check   change nothing; exit 1 when the file on disk differs from what the sources produce
 *
 * Sources: the two IDictionarySource classes that Mod.OnLoad registers with AddSource. Their dictionary initializers are read as
 * text (no game, no compiler): string literals joined with +, nameof(X.Y), SkiResortSettings.<Const>, and the ModSetting locale-id
 * helpers the settings file calls. Anything else is an error: a guessed key would ship a template that silently misses a string.
 * Also fails when a key repeats (the game drops a whole source on a duplicate), when the entry count disagrees with a separate
 * count of the initializer's entry braces, or when a hub text differs from its src/hub/hubText.ts tuple.
 */
"use strict";
const fs = require("fs");
const path = require("path");

const ROOT = path.resolve(__dirname, "..");
const MOD_DIR = path.join(ROOT, "mod", "SkiResortMod");
const MOD_CS = path.join(MOD_DIR, "Mod.cs");
const SETTINGS_CS = path.join(MOD_DIR, "Settings", "SkiResortSettings.cs");
const CSPROJ = path.join(MOD_DIR, "SkiResortMod.csproj");
const HUB_TEXT_TS = path.join(ROOT, "mod", "SkiResortUI", "src", "hub", "hubText.ts");
const OUT_DIR = path.join(ROOT, "locale");

// The dictionary sources Mod.OnLoad may register, by the class expression in its AddSource call. A third source, or one under
// another name, fails until it is added here: its keys would otherwise be missing from the template.
const SOURCES = {
  "LocaleEN": { file: path.join(MOD_DIR, "LocaleEN.cs"), settings: false },
  "SkiResortMod.Settings.LocaleEN": { file: path.join(MOD_DIR, "Settings", "LocaleEN.cs"), settings: true },
};

class ExportError extends Error {}
const fail = (msg) => { throw new ExportError(msg); };
const rel = (p) => path.relative(ROOT, p).split(path.sep).join("/");

function read(p) {
  try { return fs.readFileSync(p, "utf8").replace(/^﻿/, ""); } catch (e) { return fail(`cannot read ${rel(p)}: ${e.message}`); }
}

// ---- tokenizer --------------------------------------------------------------------------------------------------------------

// Token: { t: "str" | "id" | "num" | "chr" | "p", v, line }. Comments are skipped. The whole file is tokenized and the parser rejects
// any token outside the dictionary grammar; interpolated strings fail here, since their value needs running code.
function tokenize(text, file) {
  const toks = [];
  let i = 0, line = 1;
  const at = () => `${rel(file)}:${line}`;
  while (i < text.length) {
    const c = text[i];
    if (c === "\n") { line++; i++; continue; }
    if (/\s/.test(c)) { i++; continue; }
    if (c === "/" && text[i + 1] === "/") { while (i < text.length && text[i] !== "\n") i++; continue; }
    if (c === "/" && text[i + 1] === "*") {
      const end = text.indexOf("*/", i + 2);
      if (end < 0) fail(`${at()}: unterminated /* comment`);
      for (let k = i; k < end; k++) if (text[k] === "\n") line++;
      i = end + 2;
      continue;
    }
    if (c === "$" && (text[i + 1] === '"' || (text[i + 1] === "@" && text[i + 2] === '"')) || c === "@" && text[i + 1] === "$") {
      fail(`${at()}: interpolated string is not supported (the value cannot be read without running the code)`);
    }
    if (c === "@" && text[i + 1] === '"') {
      const startLine = line;
      let j = i + 2, s = "";
      for (;;) {
        if (j >= text.length) fail(`${rel(file)}:${startLine}: unterminated verbatim string`);
        if (text[j] === '"') {
          if (text[j + 1] === '"') { s += '"'; j += 2; continue; }
          break;
        }
        if (text[j] === "\n") line++;
        s += text[j++];
      }
      toks.push({ t: "str", v: s, line: startLine });
      i = j + 1;
      continue;
    }
    if (c === '"') {
      let j = i + 1, s = "";
      for (;;) {
        const d = text[j];
        if (j >= text.length || d === "\n") fail(`${at()}: unterminated string literal`);
        if (d === '"') break;
        if (d !== "\\") { s += d; j++; continue; }
        const e = text[j + 1];
        const simple = { n: "\n", t: "\t", r: "\r", "0": "\0", "\\": "\\", '"': '"', "'": "'", a: "\x07", b: "\b", f: "\f", v: "\v" };
        if (e === "u") {
          const h = text.slice(j + 2, j + 6);
          if (!/^[0-9a-fA-F]{4}$/.test(h)) fail(`${at()}: bad \\u escape`);
          s += String.fromCharCode(parseInt(h, 16)); j += 6;
        } else if (e === "U") {
          const h = text.slice(j + 2, j + 10);
          if (!/^[0-9a-fA-F]{8}$/.test(h)) fail(`${at()}: bad \\U escape`);
          s += String.fromCodePoint(parseInt(h, 16)); j += 10;
        } else if (e === "x") {
          const m = /^[0-9a-fA-F]{1,4}/.exec(text.slice(j + 2, j + 6));
          if (!m) fail(`${at()}: bad \\x escape`);
          s += String.fromCharCode(parseInt(m[0], 16)); j += 2 + m[0].length;
        } else if (Object.prototype.hasOwnProperty.call(simple, e)) {
          s += simple[e]; j += 2;
        } else {
          fail(`${at()}: unknown escape \\${e}`);
        }
      }
      toks.push({ t: "str", v: s, line });
      i = j + 1;
      continue;
    }
    if (/[A-Za-z_]/.test(c)) {
      let j = i + 1;
      while (j < text.length && /[A-Za-z0-9_]/.test(text[j])) j++;
      toks.push({ t: "id", v: text.slice(i, j), line });
      i = j;
      continue;
    }
    if (/[0-9]/.test(c)) {
      let j = i + 1;
      while (j < text.length && /[0-9A-Za-z_.]/.test(text[j])) j++;
      toks.push({ t: "num", v: text.slice(i, j), line });
      i = j;
      continue;
    }
    if (c === "'") {
      let j = i + 1;
      while (j < text.length && text[j] !== "'") j += text[j] === "\\" ? 2 : 1;
      toks.push({ t: "chr", v: text.slice(i, j + 1), line });
      i = j + 1;
      continue;
    }
    // Other punctuation (class headers, attributes) stays a token; the parser rejects it if it turns up inside the initializer.
    toks.push({ t: "p", v: c, line });
    i++;
  }
  return toks;
}

// ---- the game's key formats (Game.Modding/ModSetting.cs:303-371 in the decompile) ---------------------------------------------

// The helpers the two LocaleEN files call; a call to any other is an error. `argc` counts the arguments, `kind` is the kind of the
// one argument ("str" for a string, "enum" for an enum member). The binding component name is "binding": the Press component of
// the button composite (InputManager.GetBindingName, Game.Input/ButtonWithModifiersComposite.cs:44).
function makeHelpers(ctx) {
  const { id, name } = ctx;
  const BINDING = "binding";
  return {
    GetSettingsLocaleID:      { kind: null,   fn: () => `Options.SECTION[${id}]` },
    GetOptionLabelLocaleID:   { kind: "str",  fn: (o) => `Options.OPTION[${id}.${name}.${o}]` },
    GetOptionDescLocaleID:    { kind: "str",  fn: (o) => `Options.OPTION_DESCRIPTION[${id}.${name}.${o}]` },
    GetOptionWarningLocaleID: { kind: "str",  fn: (o) => `Options.WARNING[${id}.${name}.${o}]` },
    GetOptionTabLocaleID:     { kind: "str",  fn: (t) => `Options.TAB[${id}.${t}]` },
    GetOptionGroupLocaleID:   { kind: "str",  fn: (g) => `Options.GROUP[${id}.${g}]` },
    GetEnumValueLocaleID:     { kind: "enum", fn: (e) => `Options.${id}.${e.type.toUpperCase()}[${e.member}]` },
    GetBindingKeyLocaleID:    { kind: "str",  fn: (a) => `Options.OPTION[${id}/${a}/${BINDING}]` },
    GetBindingKeyHintLocaleID:{ kind: "str",  fn: (a) => `Common.ACTION[${id}/${a}]` },
    GetBindingMapLocaleID:    { kind: null,   fn: () => `Options.INPUT_MAP[${id}]` },
  };
}

// ---- facts read from the settings sources -------------------------------------------------------------------------------------

function readIdentity() {
  const modText = read(MOD_CS);
  const ns = /^\s*namespace\s+([\w.]+)/m.exec(modText);
  const cls = /\bclass\s+(\w+)\s*:\s*[^{]*\bIMod\b/.exec(modText);
  if (!ns || !cls) fail(`${rel(MOD_CS)}: cannot find the namespace or the IMod class`);
  const proj = read(CSPROJ);
  const asmMatch = /<AssemblyName>\s*([^<\s]+)\s*<\/AssemblyName>/.exec(proj);
  const assembly = asmMatch ? asmMatch[1] : path.basename(CSPROJ, ".csproj");
  // ModSetting.cs:39
  const id = `${assembly}.${ns[1]}.${cls[1]}`;
  const settingsText = read(SETTINGS_CS);
  const sc = /\bclass\s+(\w+)\s*:\s*ModSetting\b/.exec(settingsText);
  if (!sc) fail(`${rel(SETTINGS_CS)}: cannot find the class that derives from ModSetting`);
  // ModSetting.cs:40 sets name = GetType().Name, the runtime type: the sealed class itself.
  if (!/\bsealed\s+class\s+\w+\s*:\s*ModSetting\b/.test(settingsText)) fail(`${rel(SETTINGS_CS)}: ${sc[1]} is not sealed, so GetType().Name may be a subclass`);
  return { id, name: sc[1], assembly, ns: ns[1], modClass: cls[1] };
}

// `const string Name = <expr>;` -> map. The right side runs through the same expression reader (a literal or a + chain).
function readConsts(settingsClass) {
  const text = read(SETTINGS_CS);
  const consts = new Map();
  const lines = text.split(/\r?\n/);
  for (let n = 0; n < lines.length; n++) {
    const m = /^\s*(?:public|internal)\s+const\s+string\s+(\w+)\s*=(.*)$/.exec(lines[n]);
    if (!m) continue;
    const toks = tokenize(m[2], SETTINGS_CS);
    toks.forEach((t) => { t.line = n + 1; });
    const end = toks.findIndex((t) => t.t === "p" && t.v === ";");
    if (end < 0) fail(`${rel(SETTINGS_CS)}:${n + 1}: const ${m[1]} does not end on its line`);
    const parts = [];
    for (let k = 0; k < end; k++) {
      const t = toks[k];
      if (k % 2 === 0) { if (t.t !== "str") fail(`${rel(SETTINGS_CS)}:${n + 1}: const ${m[1]} is not a string literal chain`); parts.push(t.v); }
      else if (!(t.t === "p" && t.v === "+")) fail(`${rel(SETTINGS_CS)}:${n + 1}: const ${m[1]}: expected +`);
    }
    if (end === 0 || end % 2 === 0) fail(`${rel(SETTINGS_CS)}:${n + 1}: const ${m[1]} is malformed`);
    if (consts.has(m[1])) fail(`${rel(SETTINGS_CS)}:${n + 1}: const ${m[1]} declared twice`);
    consts.set(m[1], parts.join(""));
  }
  return consts;
}

// enum Name { A, B = 3, ... } -> Map<enum, Set<member>>. A member that shares a value with another prints as the first one
// (Enum.ToString), so explicit values must stay distinct.
function readEnums() {
  const text = read(SETTINGS_CS).replace(/\/\/[^\n]*/g, "").replace(/\/\*[\s\S]*?\*\//g, "");
  const enums = new Map();
  const re = /\benum\s+(\w+)\s*\{([^}]*)\}/g;
  let m;
  while ((m = re.exec(text))) {
    const members = new Set();
    const seen = new Set();
    let next = 0;
    for (const raw of m[2].split(",")) {
      const part = raw.trim();
      if (!part) continue;
      const mm = /^(\w+)(?:\s*=\s*(-?\d+))?$/.exec(part);
      if (!mm) fail(`${rel(SETTINGS_CS)}: enum ${m[1]}: cannot read member "${part}"`);
      const value = mm[2] !== undefined ? parseInt(mm[2], 10) : next;
      if (seen.has(value)) fail(`${rel(SETTINGS_CS)}: enum ${m[1]}: member ${mm[1]} repeats a value, so ToString may print another name`);
      seen.add(value);
      members.add(mm[1]);
      next = value + 1;
    }
    enums.set(m[1], members);
  }
  return enums;
}

// AddSource("en-US", new X(...)) calls in Mod.cs -> [{ locale, cls }] in call order.
function readRegistrations() {
  const text = read(MOD_CS).replace(/\/\/[^\n]*/g, "");
  const regs = [];
  const re = /AddSource\(\s*"([^"]+)"\s*,\s*new\s+([\w.]+)\s*\(/g;
  let m;
  while ((m = re.exec(text))) regs.push({ locale: m[1], cls: m[2] });
  const calls = (text.match(/\bAddSource\s*\(/g) || []).length;
  if (calls !== regs.length) fail(`${rel(MOD_CS)}: ${calls} AddSource call(s) but ${regs.length} readable as AddSource("<locale>", new <Class>(...))`);
  return regs;
}

// ---- dictionary initializer parser --------------------------------------------------------------------------------------------

// Reads `return new Dictionary<string, string> { { key, value }, ... };` out of ReadEntries. Returns [{ key, value, line }].
function parseDictionary(file, ctx) {
  const text = read(file);
  const toks = tokenize(text, file);
  const at = (t) => `${rel(file)}:${t ? t.line : "EOF"}`;
  let i = 0;
  const isP = (v) => toks[i] && toks[i].t === "p" && toks[i].v === v;
  const isId = (v) => toks[i] && toks[i].t === "id" && toks[i].v === v;
  const expectP = (v, what) => { if (!isP(v)) fail(`${at(toks[i])}: expected "${v}" ${what || ""}`.trim()); i++; };
  const expectId = () => { if (!toks[i] || toks[i].t !== "id") fail(`${at(toks[i])}: expected an identifier`); return toks[i++]; };

  // ReadEntries(...) { return new Dictionary<string, string> {
  const start = toks.findIndex((t, k) => t.t === "id" && t.v === "ReadEntries" && toks[k + 1] && toks[k + 1].v === "(");
  if (start < 0) fail(`${rel(file)}: no ReadEntries method`);
  if (toks.filter((t, k) => t.v === "new" && toks[k + 1] && toks[k + 1].v === "Dictionary").length !== 1) {
    fail(`${rel(file)}: expected exactly one "new Dictionary" in the file`);
  }
  i = start;
  while (!isP(")")) i++;
  i++;
  expectP("{", "to open ReadEntries");
  if (!isId("return")) fail(`${at(toks[i])}: ReadEntries must start with return new Dictionary<string, string> (anything before it would add entries unseen)`);
  i++;
  if (!isId("new")) fail(`${at(toks[i])}: expected new`);
  i++;
  if (!isId("Dictionary")) fail(`${at(toks[i])}: expected Dictionary`);
  i++;
  expectP("<"); if (!isId("string")) fail(`${at(toks[i])}: expected string`); i++;
  expectP(","); if (!isId("string")) fail(`${at(toks[i])}: expected string`); i++;
  expectP(">");
  expectP("{", "to open the initializer");

  const helpers = ctx ? makeHelpers(ctx) : null;
  const consts = ctx ? ctx.consts : null;

  // term := string | nameof(A.B) | SkiResortSettings.Const | Enum.Member | m_Setting.Helper(args)
  function parseTerm() {
    const t = toks[i];
    if (!t) fail(`${at(null)}: unexpected end of file`);
    if (t.t === "str") { i++; return { str: t.v }; }
    if (t.t !== "id") fail(`${at(t)}: unexpected "${t.v}" where a value belongs`);
    const first = expectId();
    if (first.v === "nameof") {
      expectP("(");
      const segs = [expectId().v];
      while (isP(".")) { i++; segs.push(expectId().v); }
      expectP(")");
      if (segs.length < 2) fail(`${at(first)}: nameof needs Type.Member`);
      return { str: segs[segs.length - 1] };
    }
    const segs = [first.v];
    while (isP(".")) { i++; segs.push(expectId().v); }
    if (isP("(")) {
      if (!helpers) fail(`${at(first)}: call ${segs.join(".")}(...) in a file with no settings helpers`);
      if (segs.length !== 2 || segs[0] !== "m_Setting") fail(`${at(first)}: call ${segs.join(".")}(...) is not an m_Setting helper`);
      const h = helpers[segs[1]];
      if (!h) fail(`${at(first)}: helper ${segs[1]} is not implemented in tools/export-locale.js (add it from ModSetting.cs, do not guess)`);
      i++;
      const args = [];
      if (!isP(")")) { args.push(parseExpr()); while (isP(",")) { i++; args.push(parseExpr()); } }
      expectP(")");
      const want = h.kind ? 1 : 0;
      if (args.length !== want) fail(`${at(first)}: ${segs[1]} takes ${want} argument(s), got ${args.length}`);
      if (h.kind === "str") {
        if (args[0].str === undefined) fail(`${at(first)}: ${segs[1]} argument is not a string`);
        return { str: h.fn(args[0].str) };
      }
      if (h.kind === "enum") {
        if (!args[0].enumMember) fail(`${at(first)}: ${segs[1]} argument is not an enum member`);
        return { str: h.fn(args[0].enumMember) };
      }
      return { str: h.fn() };
    }
    if (segs.length === 2 && ctx) {
      if (segs[0] === "SkiResortSettings") {
        if (!consts.has(segs[1])) fail(`${at(first)}: SkiResortSettings.${segs[1]} is not a const string in ${rel(SETTINGS_CS)}`);
        return { str: consts.get(segs[1]) };
      }
      if (ctx.enums.has(segs[0])) {
        if (!ctx.enums.get(segs[0]).has(segs[1])) fail(`${at(first)}: ${segs.join(".")} is not a member of enum ${segs[0]}`);
        return { enumMember: { type: segs[0], member: segs[1] } };
      }
    }
    return fail(`${at(first)}: cannot resolve ${segs.join(".")} (only literals, nameof, SkiResortSettings consts and known enums are read)`);
  }

  function parseExpr() {
    const first = parseTerm();
    if (!isP("+")) return first;
    if (first.str === undefined) fail(`${at(toks[i])}: + on a non-string`);
    let s = first.str;
    while (isP("+")) {
      i++;
      const nxt = parseTerm();
      if (nxt.str === undefined) fail(`${at(toks[i - 1])}: + on a non-string`);
      s += nxt.str;
    }
    return { str: s };
  }

  const entries = [];
  for (;;) {
    if (isP("}")) { i++; break; }
    const open = toks[i];
    expectP("{", "to open an entry");
    const k = parseExpr();
    if (k.str === undefined) fail(`${at(open)}: entry key is not a string`);
    expectP(",", `between key and value (entry at line ${open.line})`);
    const v = parseExpr();
    if (v.str === undefined) fail(`${at(open)}: value of "${k.str}" is not a string`);
    expectP("}", `to close the entry at line ${open.line}`);
    entries.push({ key: k.str, value: v.str, line: open.line });
    if (isP(",")) { i++; continue; }
    if (isP("}")) { i++; break; }
    fail(`${at(toks[i])}: expected "," or "}" after the entry at line ${open.line}`);
  }
  expectP(";", "after the initializer");
  return entries;
}

// A second, independent count: entry braces of the initializer, found by walking characters (comments and strings skipped) instead
// of reading tokens. A parser slip that drops or merges entries cannot also fool this.
function countEntryBraces(file) {
  const text = read(file);
  const anchor = text.indexOf("new Dictionary<string, string>");
  if (anchor < 0) fail(`${rel(file)}: no initializer to count`);
  let i = anchor, depth = 0, count = 0, begun = false;
  while (i < text.length) {
    const c = text[i];
    if (c === "/" && text[i + 1] === "/") { while (i < text.length && text[i] !== "\n") i++; continue; }
    if (c === "/" && text[i + 1] === "*") { i = text.indexOf("*/", i + 2) + 2; continue; }
    if (c === "@" && text[i + 1] === '"') {
      i += 2;
      for (;;) { if (text[i] === '"') { if (text[i + 1] === '"') { i += 2; continue; } break; } i++; }
      i++; continue;
    }
    if (c === '"') { i++; while (text[i] !== '"') i += text[i] === "\\" ? 2 : 1; i++; continue; }
    if (c === "{") { depth++; begun = true; if (depth === 2) count++; }
    else if (c === "}") { depth--; if (begun && depth === 0) return count; }
    i++;
  }
  return fail(`${rel(file)}: initializer braces do not balance`);
}

// ---- hub text parity ------------------------------------------------------------------------------------------------------------

// Every ["Prefix.KEY", "English"] tuple of hubText.ts must equal the dictionary text. The UI build's R4 gate checks the same through
// the TypeScript parser; this reader is a regex over the file's own tuple form and also fails on any tuple it cannot decode.
function checkHubText(map) {
  const text = read(HUB_TEXT_TS);
  const re = /\[\s*"((?:SkiResort|Assets|SubServices)\.[^"]*)"\s*,\s*("(?:[^"\\\n]|\\.)*")\s*\]/g;
  let m, tuples = 0;
  const problems = [];
  while ((m = re.exec(text))) {
    tuples++;
    const line = text.slice(0, m.index).split("\n").length;
    let en;
    try { en = JSON.parse(m[2]); } catch { fail(`${rel(HUB_TEXT_TS)}:${line}: cannot decode the string of "${m[1]}"`); }
    if (!map.has(m[1])) problems.push(`${rel(HUB_TEXT_TS)}:${line}: "${m[1]}" is not in the dictionaries`);
    else if (map.get(m[1]) !== en) problems.push(`${rel(HUB_TEXT_TS)}:${line}: "${m[1]}" differs: hubText "${en}" vs dictionary "${map.get(m[1])}"`);
  }
  const quoted = (text.match(/"(?:SkiResort|Assets|SubServices)\.[^"]*"/g) || []).length;
  if (tuples === 0) fail(`${rel(HUB_TEXT_TS)}: no tuples found; the reader no longer matches the file`);
  // Each tuple holds exactly one key literal, so a literal count above the tuple count means a tuple the reader skipped.
  if (quoted !== tuples) problems.push(`${rel(HUB_TEXT_TS)}: ${quoted} key literal(s) but ${tuples} readable tuple(s): a tuple has a form this reader does not know`);
  return { tuples, problems };
}

// ---- build -------------------------------------------------------------------------------------------------------------------

function build() {
  const identity = readIdentity();
  const ctx = { id: identity.id, name: identity.name, consts: readConsts(identity.name), enums: readEnums() };

  const regs = readRegistrations();
  if (regs.length === 0) fail(`${rel(MOD_CS)}: no AddSource call found`);
  const locales = new Set(regs.map((r) => r.locale));
  if (locales.size !== 1) fail(`${rel(MOD_CS)}: sources registered for more than one locale (${[...locales].join(", ")}); the template is for one`);
  for (const r of regs) if (!SOURCES[r.cls]) fail(`${rel(MOD_CS)}: AddSource of ${r.cls} is not a known source; add it to SOURCES in tools/export-locale.js`);
  if (new Set(regs.map((r) => r.cls)).size !== regs.length) fail(`${rel(MOD_CS)}: the same source is registered twice`);
  const locale = regs[0].locale;

  const all = [];
  const owner = new Map();
  const counts = [];
  for (const r of regs) {
    const src = SOURCES[r.cls];
    const entries = parseDictionary(src.file, src.settings ? ctx : null);
    const braces = countEntryBraces(src.file);
    if (braces !== entries.length) fail(`${rel(src.file)}: read ${entries.length} entries but the initializer has ${braces} entry braces`);
    for (const e of entries) {
      if (owner.has(e.key)) fail(`duplicate key "${e.key}": ${rel(src.file)}:${e.line} and ${owner.get(e.key)}`);
      owner.set(e.key, `${rel(src.file)}:${e.line}`);
      all.push(e);
    }
    counts.push({ file: rel(src.file), entries: entries.length, braces });
  }
  const map = new Map(all.map((e) => [e.key, e.value]));
  const hub = checkHubText(map);
  if (hub.problems.length) fail(hub.problems.join("\n"));
  const hubKeys = all.filter((e) => e.key.startsWith("SkiResort.Hub.")).length;

  // Manual serialization keeps source order whatever the keys look like (an object would move integer-like keys first).
  const body = all.map((e) => `  ${JSON.stringify(e.key)}: ${JSON.stringify(e.value)}`).join(",\n");
  const text = `{\n${body}\n}\n`;
  return { text, locale, identity, counts, total: all.length, hub: { tuples: hub.tuples, keys: hubKeys }, map };
}

function main() {
  const check = process.argv.slice(2).includes("--check");
  const unknown = process.argv.slice(2).filter((a) => a !== "--check");
  if (unknown.length) { console.error(`export-locale: unknown argument ${unknown.join(" ")}`); process.exit(2); }
  let res;
  try { res = build(); } catch (e) {
    if (!(e instanceof ExportError)) throw e;
    console.error(`export-locale: ERROR ${e.message}`);
    process.exit(1);
  }
  const outFile = path.join(OUT_DIR, `${res.locale}.json`);
  const buf = Buffer.from(res.text, "utf8");
  console.log(`export-locale: map id ${res.identity.id}, settings name ${res.identity.name}, locale ${res.locale}`);
  for (const c of res.counts) console.log(`  ${c.file}: ${c.entries} entries (${c.braces} entry braces)`);
  console.log(`  total ${res.total} keys; ${res.hub.tuples} hubText.ts tuple(s) equal their dictionary text (${res.hub.keys} SkiResort.Hub.* keys)`);
  if (check) {
    let disk = null;
    try { disk = fs.readFileSync(outFile); } catch { /* reported below */ }
    if (!disk) { console.error(`export-locale: FAIL ${rel(outFile)} is missing; run node tools/export-locale.js`); process.exit(1); }
    if (!disk.equals(buf)) { console.error(`export-locale: FAIL ${rel(outFile)} is stale; run node tools/export-locale.js and commit it`); process.exit(1); }
    console.log(`export-locale: ${rel(outFile)} is current - PASS`);
    return;
  }
  fs.mkdirSync(OUT_DIR, { recursive: true });
  fs.writeFileSync(outFile, buf);
  console.log(`export-locale: wrote ${rel(outFile)} (${buf.length} bytes)`);
}

main();
