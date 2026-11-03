#!/usr/bin/env node
// Gate GAMESURFACE (patch readiness, DECISIONS 581): the mod's whole dependency surface on Game.dll, measured from the code, frozen in
// tools/game-surface.lock, and compared with a decompile of whatever game is installed. It turns "somebody diffs the patch" into one
// command: on patch day it names what broke (FAIL) and which of the files we stand on changed at all (REVIEW), so a patch review reads
// the handful of files that touch us instead of the whole diff.
//
// WHAT IT READS. mod/SkiResortMod/**/*.cs (comments and strings stripped), Mod.cs's registration list, the [BurstCompile] jobs and the
// Game.* types and fields named by gamedata-mirror's prefab JSON. Read-only, node only, no game process and no game folder: the decompile
// tree is the only input from the game.
//
// WHAT IT CHECKS.
//   TYPES    every Game.* type the mod names still exists in the decompile (a removed one is a compile error, and for a deployed DLL a
//            TypeLoadException on the first call into the method that names it).
//   LAYOUT   the field list of every Game.* struct and the members of every Game.* enum we name, against the lock. A layout change of a
//            struct a Burst job reads is the worst break there is: the mod's Burst library has the old field offsets compiled in and
//            nothing checks them at run time, so it reads or writes the wrong bytes with no exception. Those rows carry burst:r / burst:w.
//   ANCHORS  every UpdateBefore / UpdateAfter<Ours, Vanilla>(phase) in Mod.cs: the vanilla system is still registered at that phase in
//            SystemOrder, and its GetUpdateInterval / GetUpdateOffset text is unchanged. UpdateSystem.AddSystemUpdate walks an anchor's list only
//            for rows of the anchor's own phase (Game/UpdateSystem.cs:399), so a vanilla system moved to another phase silently stops
//            every system anchored to it: no exception, no log line.
//   PREFABS  every Game.* type named in the prefab JSON exists and still declares (or inherits) each field the JSON sets. The prefab
//            loader skips an unknown field, so a renamed field falls back to its default silently.
//   GUIDS    every vanilla UnityGUID / UnityObjectsMap reference in the prefab JSON is a lock row, and with --guid-map=<json> (the output of
//            tools/vanilla-guid-map.py on the installed game) every UnityGUID still exists. A missing one loads as null with no log line
//            (Colossal.IO.AssetDatabase PrefabAsset.cs:278-288); in a net's m_Lane, m_Piece or m_Section that null is a hard crash (SPEC 8.3).
//   DRIFT    the code's surface equals the lock (a new vanilla dependency is one more lock line in the same round as the code, the rule of
//            tools/engine-contract-check.ps1), and the tested game version is the same everywhere it is written.
//   REVIEW   the decompiled files of the surface whose content changed since the lock: the patch's reading list, not a failure.
// NOT COVERED. Unity.Entities / Unity.Collections layouts (ComponentLookup, NativeArray... that every job holds): no decompile of those
// assemblies is kept, so an Entities bump in a patch is the compile's and the toolchain's to find. Colossal.* is the compile's too.
// The mod has no run-time check of the game version: between a game patch and our rebuild, players run the old DLL on the new game,
// and this tool exists to keep that window short.
//
// USAGE.  node tools/game-surface-check.js [--decompile=<folder>] [--lock=<file>] [--guid-map=<json>] [--write] [--list] [--require-decompile] [--self-test]
//   no flag          compare the code and the decompile of the lock's version (tools/decompile/<version> beside the repo) with the lock.
//                    No decompile there (CI, a fresh clone): the DRIFT part only, and one line says so.
//   --decompile=X    the tree to compare, normally the new patch's.
//   --write          rewrite the lock from the code and --decompile, after a review. --version=1.6.3f1 names the tree's game version when
//                    it is not the folder name. With --lock=<file> it writes that file instead (a scratch lock for a retro comparison).
//   --guid-map=X     also check every vanilla UnityGUID against X, the output of tools/vanilla-guid-map.py --out=X on the installed game.
//   --require-decompile  exit 100 instead of falling back to the code-only half when the tree is missing (release-check).
//   --self-test      the parser, the scanners, the patch mutations and the 1.6.2 retro fixture, with no tree and no lock.
//   --list           print the surface grouped by section and stop.
// EXIT. Number of FAIL lines, capped at 99 (REVIEW lines do not fail). 100 = the lock is missing or a path is wrong.
"use strict";

const fs = require("fs");
const path = require("path");
const crypto = require("crypto");

const ROOT = path.resolve(__dirname, "..");
const MOD_DIR = path.join(ROOT, "mod", "SkiResortMod");
const MOD_CS = path.join(MOD_DIR, "Mod.cs");
const MIRROR = path.join(ROOT, "gamedata-mirror", "ImportedData");
const DEFAULT_LOCK = path.join(__dirname, "game-surface.lock");
// An exit code wraps at 256, so a count alone could read as success.
const FATAL = 100;

// Every place the tested game version is written. The lock's `version` line is the one place to change it (--write); these must follow.
const VERSION_SITES = [
  { file: "mod/SkiResortMod/Properties/PublishConfiguration.xml", re: /<GameVersion Value="([^"]*)"/, what: "the Paradox Mods GameVersion label" },
  { file: "release/DESCRIPTION.md", re: /tested on ([0-9][0-9a-z.]*)\)/, what: "the listing's Requirements line" },
  { file: "docs-site/src/content/docs/index.mdx", re: /\*\*Game version:\*\* Cities: Skylines II ([0-9][0-9a-z.]*)/, what: "the docs site's front page" },
];

// ---- C# text helpers ----------------------------------------------------------------------------------------------------------------

/** The source with comments removed and every string or char literal emptied; line breaks kept, so offsets stay on the same line. */
function stripCs(src) {
  let out = "";
  let i = 0;
  const n = src.length;
  while (i < n) {
    const c = src[i];
    const d = src[i + 1];
    if (c === "/" && d === "/") {
      while (i < n && src[i] !== "\n") i++;
    } else if (c === "/" && d === "*") {
      i += 2;
      while (i < n && !(src[i] === "*" && src[i + 1] === "/")) {
        if (src[i] === "\n") out += "\n";
        i++;
      }
      i += 2;
    } else if (c === '"' || (c === "@" && d === '"') || (c === "$" && (d === '"' || (d === "@" && src[i + 2] === '"'))) || (c === "@" && d === "$" && src[i + 2] === '"')) {
      let verbatim = false;
      while (src[i] !== '"') {
        if (src[i] === "@") verbatim = true;
        i++;
      }
      i++;
      out += '""';
      while (i < n) {
        if (src[i] === "\n") out += "\n";
        if (!verbatim && src[i] === "\\") {
          i += 2;
          continue;
        }
        if (src[i] === '"') {
          if (verbatim && src[i + 1] === '"') {
            i += 2;
            continue;
          }
          i++;
          break;
        }
        i++;
      }
    } else if (c === "'") {
      i++;
      if (src[i] === "\\") i++;
      i++;
      while (i < n && src[i] !== "'" && src[i] !== "\n") i++;
      i++;
      out += "' '";
    } else {
      out += c;
      i++;
    }
  }
  return out;
}

/** The text between the brace at `open` and its partner (no strings left to fool it after stripCs; the raw decompile has none that matter). */
function blockFrom(text, open) {
  let depth = 0;
  for (let i = open; i < text.length; i++) {
    if (text[i] === "{") depth++;
    else if (text[i] === "}" && --depth === 0) return text.slice(open + 1, i);
  }
  return text.slice(open + 1);
}

/** Lines of a block at brace depth 0 of that block: its member declarations, never a nested type's or a method's. */
function topLevelLines(body) {
  const lines = [];
  let depth = 0;
  for (const raw of body.split(/\r?\n/)) {
    const line = raw.trim();
    const start = depth;
    for (const ch of line) {
      if (ch === "{") depth++;
      else if (ch === "}") depth--;
    }
    if (start === 0 && line) lines.push(line);
  }
  return lines;
}

const DECL_RE = (name) =>
  new RegExp(`\\b(?:public|internal)\\s+(?:(?:readonly|unsafe|sealed|abstract|static|partial|ref)\\s+)*(struct|class|enum|interface)\\s+${name}\\b([^{;]*)\\{`);

/** {kind, base, fields|members} of the named top-level type in a decompiled file; null when the file does not declare it. */
function parseVanillaType(text, name) {
  const m = DECL_RE(name).exec(text);
  if (!m) return null;
  const kind = m[1];
  const open = m.index + m[0].length - 1;
  const lines = topLevelLines(blockFrom(text, open));
  const out = { kind, base: "", fields: [] };
  const inherit = m[2].trim().replace(/^:\s*/, "");
  if (inherit) out.base = inherit.split(",")[0].trim().replace(/<.*$/, "");
  if (kind === "enum") {
    // name=value with the value evaluated: the C# compiler bakes it into our DLL, so the number is what matters.
    const values = new Map();
    let next = 0n;
    for (const line of lines) {
      if (line.startsWith("[")) continue;
      const e = /^(\w+)(?:\s*=\s*([^,]+?))?,?$/.exec(line);
      if (!e) continue;
      const v = e[2] === undefined ? next : evalEnumExpr(e[2].trim(), values);
      if (v === null) {
        out.fields.push(`${e[1]}=${e[2].trim().replace(/\s+/g, "")}`);
        continue;
      }
      values.set(e[1], v);
      out.fields.push(`${e[1]}=${v}`);
      next = v + 1n;
    }
  } else if (kind === "struct" || kind === "class") {
    for (let line of lines) {
      line = line.replace(/^(\[[^\]]*\]\s*)+/, "");
      // An auto-property is a hidden field: part of the layout.
      const auto = /^(?:(?:public|private|protected|internal|readonly)\s+)*([\w.]+(?:<[^;={}]*>)?(?:\[[,\s]*\])*)\s+(\w+)\s*\{\s*(?:(?:private|internal|protected)\s+)?get;(?:\s*(?:(?:private|internal|protected)\s+)?set;)?\s*\}/.exec(line);
      if (auto && !/\bstatic\b/.test(line)) {
        if (kind === "struct") out.fields.push(`${auto[2]}:${auto[1].replace(/\s+/g, "")}`);
        continue;
      }
      // A collection initializer spans lines: "public List<X> m_Y = new List<X>" then "{ ... };". Its head is all that reaches this depth.
      if (!line.endsWith(";") && /\s=\s/.test(line) && !line.includes("=>")) line = line.replace(/\s*\{$/, "") + ";";
      if (!line.endsWith(";")) continue;
      const head = line.split("=")[0];
      if (/\b(static|const|event|delegate|using)\b/.test(head) || head.includes("(") || line.includes("=>")) continue;
      const f = /^(?:(?:public|private|protected|internal|readonly|fixed|unsafe|volatile|new)\s+)*([\w.]+(?:<[^;=]*>)?(?:\[[,\s]*\])*\*?)\s+(\w+)(\[[^\]]*\])?(?:\s*=.*)?;$/.exec(line);
      if (f) out.fields.push(`${f[2]}:${f[1].replace(/\s+/g, "")}${f[3] || ""}`);
    }
  }
  return out;
}

/** The value of an enum member's initializer: a number, an earlier member, or an OR / shift / cast of those; null when not evaluable. */
function evalEnumExpr(expr, values) {
  const text = expr.replace(/\([a-z]+\)/g, "").replace(/\s+/g, "");
  let total = 0n;
  for (const part of text.split("|")) {
    let v = null;
    const shift = /^(.+)<<(\d+)$/.exec(part);
    const atom = shift ? shift[1] : part;
    if (/^-?\d+[uUlL]*$/.test(atom)) v = BigInt(atom.replace(/[uUlL]/g, ""));
    else if (/^0x[0-9a-fA-F]+[uUlL]*$/.test(atom)) v = BigInt(atom.replace(/[uUlL]/g, ""));
    else if (values.has(atom)) v = values.get(atom);
    if (v === null) return null;
    if (shift) v <<= BigInt(shift[2]);
    total |= v;
  }
  return total;
}

/** Line-trimmed content hash of a decompiled file: survives reindent and a moved using block, not a code change. */
function fileHash(text) {
  const norm = text.split(/\r?\n/).map((l) => l.trim()).filter((l) => l && !l.startsWith("using ")).join("\n");
  return crypto.createHash("sha1").update(norm).digest("hex").slice(0, 10);
}

/** GetUpdateInterval / GetUpdateOffset bodies of a vanilla system, whitespace collapsed; "" when it inherits the default. */
function cadenceOf(text) {
  const grab = (method) => {
    const m = new RegExp(`override int ${method}\\(SystemUpdatePhase \\w+\\)\\s*\\{`).exec(text);
    if (!m) return "";
    return blockFrom(text, m.index + m[0].length - 1).replace(/\s+/g, " ").trim();
  };
  const a = grab("GetUpdateInterval");
  const b = grab("GetUpdateOffset");
  return [a && `interval{${a}}`, b && `offset{${b}}`].filter(Boolean).join(" ");
}

// ---- the decompile tree ---------------------------------------------------------------------------------------------------------------

/** Maps a simple type name to the namespaces that declare it, from the tree's <namespace>/<Type>.cs layout. */
function indexDecompile(rootDir) {
  const byName = new Map();
  for (const e of fs.readdirSync(rootDir, { withFileTypes: true })) {
    if (!e.isDirectory() || !(e.name === "Game" || e.name.startsWith("Game."))) continue;
    for (const f of fs.readdirSync(path.join(rootDir, e.name))) {
      if (!f.endsWith(".cs")) continue;
      const type = f.slice(0, -3);
      if (!byName.has(type)) byName.set(type, []);
      byName.get(type).push(e.name);
    }
  }
  return byName;
}

// ---- the mod's source ------------------------------------------------------------------------------------------------------------------

function walkCs(dir, out) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (e.isDirectory()) {
      if (e.name !== "bin" && e.name !== "obj" && e.name !== "Package") walkCs(path.join(dir, e.name), out);
    } else if (e.name.endsWith(".cs")) out.push(path.join(dir, e.name));
  }
  return out;
}

function usingsOf(code) {
  const ns = new Set();
  const alias = new Map();
  for (const m of code.matchAll(/^\s*using\s+(?:static\s+)?([\w.]+)\s*;/gm)) ns.add(m[1]);
  for (const m of code.matchAll(/^\s*using\s+(\w+)\s*=\s*([\w.]+)\s*;/gm)) alias.set(m[1], m[2]);
  return { ns, alias };
}

/** Vanilla full names `text` refers to: a qualified Game.X.Y, an alias, or a bare token whose namespace the file imports. */
function vanillaRefs(text, usings, index, ownTypes) {
  const found = new Set();
  const addQualified = (q) => {
    const parts = q.split(".");
    for (let i = parts.length - 1; i >= 1; i--) {
      const list = index.get(parts[i]);
      if (list && list.includes(parts.slice(0, i).join("."))) {
        found.add(parts.slice(0, i + 1).join("."));
        return true;
      }
    }
    return false;
  };
  const rest = text.replace(/\bGame(?:\.\w+)+/g, (q) => {
    addQualified(q);
    return " ";
  });
  for (const [a, target] of usings.alias) if (new RegExp(`\\b${a}\\b`).test(rest) && target.startsWith("Game")) addQualified(target);
  for (const m of rest.matchAll(/(?<![.\w])[A-Za-z_]\w*/g)) {
    const t = m[0];
    if (ownTypes.has(t)) continue;
    const list = index.get(t);
    if (!list) continue;
    for (const ns of list) if (usings.ns.has(ns)) found.add(`${ns}.${t}`);
  }
  return found;
}

/** The [BurstCompile] jobs of a stripped file: [{name, body, writes:Set(token)}]. Attribute on a struct only (the job shape used here). */
function burstJobs(code) {
  const jobs = [];
  for (const m of code.matchAll(/\[BurstCompile[^\]]*\]/g)) {
    const after = code.slice(m.index + m[0].length, m.index + m[0].length + 400);
    const d = /^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|private|internal|readonly|unsafe|static|partial)\s+)*(struct|class)\s+(\w+)[^{]*\{/.exec(after);
    if (!d) continue;
    const open = m.index + m[0].length + d.index + d[0].length - 1;
    const body = blockFrom(code, open);
    const writes = new Set();
    // A writable handle: the field has no [ReadOnly] right before it.
    for (const w of body.matchAll(/(\[ReadOnly\]\s*)?(?:public\s+|internal\s+|private\s+)?(?:ComponentLookup|BufferLookup|ComponentTypeHandle|BufferTypeHandle)<([\w.]+)>\s+\w+\s*;/g)) {
      if (!w[1]) writes.add(w[2].split(".").pop());
    }
    // Command-buffer writes, typed or inferred from `new T`.
    for (const w of body.matchAll(/\.(?:SetComponent|AddComponent|AddBuffer|SetBuffer|AppendToBuffer)\s*<([\w.]+)>/g)) writes.add(w[1].split(".").pop());
    for (const w of body.matchAll(/\.(?:SetComponent|AddComponent|AppendToBuffer)\s*\([^;]*?new\s+([\w.]+)/g)) writes.add(w[1].split(".").pop());
    // The owner is the last class declared at namespace level before the attribute: the system, never a nested helper class.
    const before = code.slice(0, m.index);
    let owner = "";
    let depth = 0;
    let at = 0;
    for (const c of before.matchAll(/\bclass\s+(\w+)/g)) {
      for (; at < c.index; at++) {
        if (before[at] === "{") depth++;
        else if (before[at] === "}") depth--;
      }
      if (depth <= 1) owner = c[1];
    }
    jobs.push({ name: d[2], body, writes, owner });
  }
  return jobs;
}

// ---- prefab JSON ---------------------------------------------------------------------------------------------------------------------------

/**
 * Game.* type -> the fields our prefabs set on it, with a tolerant scanner: the files are Odin JSON with `$fstrref:"..."` values and bare
 * Color numbers, so not strict JSON, and each file's `"$type"` strings declare a positional table that later `"$type": 12` entries reuse.
 */
function scanPrefabText(text, into) {
  const table = new Map();
  const stack = [];
  const n = text.length;
  let i = 0;
  const readString = () => {
    let j = i + 1;
    while (j < n && text[j] !== '"') j += text[j] === "\\" ? 2 : 1;
    const s = text.slice(i + 1, j);
    i = j + 1;
    return s;
  };
  while (i < n) {
    const c = text[i];
    if (c === "{") {
      stack.push({ type: null, keys: [] });
      i++;
    } else if (c === "}") {
      const fr = stack.pop();
      if (fr && fr.type && /^Game[.\w]*\.\w+, Game$/.test(fr.type)) {
        const t = fr.type.replace(/, Game$/, "");
        if (!into.has(t)) into.set(t, new Set());
        for (const k of fr.keys) into.get(t).add(k);
      }
      i++;
    } else if (c === '"') {
      const s = readString();
      let j = i;
      while (j < n && /\s/.test(text[j])) j++;
      if (text[j] === ":" && stack.length) {
        i = j + 1;
        const top = stack[stack.length - 1];
        if (s === "$type") {
          while (i < n && /\s/.test(text[i])) i++;
          if (text[i] === '"') {
            const v = readString();
            const bar = v.indexOf("|");
            if (bar > 0) {
              table.set(v.slice(0, bar), v.slice(bar + 1));
              top.type = v.slice(bar + 1);
            }
          } else {
            let k = i;
            while (k < n && /\d/.test(text[k])) k++;
            top.type = table.get(text.slice(i, k)) || null;
            i = k;
          }
        } else if (!s.startsWith("$")) {
          top.keys.push(s);
        }
      }
    } else {
      i++;
    }
  }
}

function walkPrefabs(dir, out) {
  if (!fs.existsSync(dir)) return out;
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walkPrefabs(p, out);
    else if (e.name.endsWith(".Prefab")) out.push(p);
  }
  return out;
}

/** Fields of a vanilla class / struct with its base chain inside Game.*; a base outside the tree ends the walk. */
function fieldChain(treeRoot, index, full, cache) {
  if (cache.has(full)) return cache.get(full);
  const names = new Set();
  cache.set(full, names);
  const dot = full.lastIndexOf(".");
  const ns = full.slice(0, dot);
  const name = full.slice(dot + 1);
  const file = path.join(treeRoot, ns, name + ".cs");
  if (!fs.existsSync(file)) return names;
  const text = fs.readFileSync(file, "utf8");
  const t = parseVanillaType(text, name);
  if (!t) return names;
  for (const f of t.fields) names.add(f.split(":")[0]);
  if (t.base) {
    const list = index.get(t.base.split(".").pop());
    if (list) {
      const usings = usingsOf(text).ns;
      const baseNs = list.find((x) => x === ns) || list.find((x) => usings.has(x));
      if (baseNs) for (const b of fieldChain(treeRoot, index, `${baseNs}.${t.base.split(".").pop()}`, cache)) names.add(b);
    }
  }
  return names;
}

// ---- building the surface ------------------------------------------------------------------------------------------------------------------

/** The surface as the code and a decompile tree see it: types (with file hash and layout), anchors. */
function measure(treeRoot) {
  const index = indexDecompile(treeRoot);
  const sources = walkCs(MOD_DIR, []);
  const stripped = new Map(sources.map((f) => [f, stripCs(fs.readFileSync(f, "utf8"))]));
  const ownTypes = new Set();
  for (const code of stripped.values()) for (const m of code.matchAll(/\b(?:class|struct|enum|interface)\s+(\w+)/g)) ownTypes.add(m[1]);

  const types = new Set();
  const burst = new Map(); // full name -> "r" | "w"
  const burstOwners = new Map(); // full name -> Set of our system classes whose jobs name it
  const burstJobsSeen = [];
  const burstSystems = new Set(); // every class of ours with a [BurstCompile] job
  const byValue = []; // {job, field, type}: a vanilla struct held by value in a job struct, so every later job field moves with it
  for (const [file, code] of stripped) {
    const usings = usingsOf(code);
    for (const t of vanillaRefs(code, usings, index, ownTypes)) types.add(t);
    for (const job of burstJobs(code)) {
      burstJobsSeen.push(`${path.basename(file)}:${job.name}`);
      burstSystems.add(job.owner || path.basename(file).split(".")[0]);
      // A vanilla struct nested in a class (OverlayRenderSystem.Buffer): the job's bytes and the inlined helpers are that struct's.
      for (const n of job.body.matchAll(/(?<![.\w])([A-Z]\w*)\.([A-Z]\w*)\s+\w+\s*[;=,)]/g)) {
        if (ownTypes.has(n[1])) continue;
        for (const ns of index.get(n[1]) || []) {
          if (!usings.ns.has(ns)) continue;
          const nestedFile = path.join(treeRoot, ns, n[1] + ".cs");
          const parsed = fs.existsSync(nestedFile) ? parseVanillaType(fs.readFileSync(nestedFile, "utf8"), n[2]) : null;
          if (!parsed || parsed.kind !== "struct") continue;
          const full = ns + "." + n[1] + "." + n[2];
          types.add(full);
          if (burst.get(full) !== "w") burst.set(full, "r");
          if (!burstOwners.has(full)) burstOwners.set(full, new Set());
          burstOwners.get(full).add(job.owner || path.basename(file).split(".")[0]);
        }
      }
      for (const line of topLevelLines(job.body)) {
        const f = /^(?:\[[^\]]*\]\s*)*(?:(?:public|internal|private|readonly)\s+)*([A-Z][\w.]*)\s+(\w+)\s*;$/.exec(line);
        if (!f) continue;
        for (const t of vanillaRefs(f[1], usings, index, ownTypes)) byValue.push({ job: job.name, field: f[2], type: t });
      }
      for (const t of vanillaRefs(job.body, usings, index, ownTypes)) {
        const shortName = t.split(".").pop();
        const mark = job.writes.has(shortName) ? "w" : "r";
        if (burst.get(t) !== "w") burst.set(t, mark);
        if (!burstOwners.has(t)) burstOwners.set(t, new Set());
        burstOwners.get(t).add(job.owner || path.basename(file).split(".")[0]);
      }
    }
  }

  // Anchors from Mod.cs's registration list.
  const modCode = stripped.get(sources.find((f) => path.resolve(f) === path.resolve(MOD_CS)));
  const modUsings = usingsOf(modCode);
  const anchors = [];
  for (const m of modCode.matchAll(/updateSystem\.(UpdateBefore|UpdateAfter)<\s*(\w+)\s*,\s*([\w.]+)\s*>\s*\(\s*SystemUpdatePhase\.(\w+)\s*\)/g)) {
    const [, how, ours, other, phase] = m;
    if (ownTypes.has(other)) continue;
    let full = null;
    if (other.startsWith("Game")) full = other;
    else {
      const list = index.get(other);
      if (list) full = list.filter((ns) => modUsings.ns.has(ns)).map((ns) => `${ns}.${other}`)[0] || null;
    }
    anchors.push({ ours, how, anchor: full || other, phase });
    if (full) types.add(full);
  }

  // Files the code's comments and engine notes cite (Name.cs:123): what the mod's reasoning stands on, even when the code never names the
  // type (NetInitializeSystem reads our prefabs, nothing of ours calls it). Their hash goes in the REVIEW list; they have no layout row.
  const cited = new Set();
  const notes = fs.existsSync(path.join(MOD_DIR, "EngineNotes")) ? fs.readdirSync(path.join(MOD_DIR, "EngineNotes")).filter((f) => f.endsWith(".md")).map((f) => path.join(MOD_DIR, "EngineNotes", f)) : [];
  for (const f of [...sources, ...notes]) {
    for (const c of fs.readFileSync(f, "utf8").matchAll(/(?:([A-Za-z][\w.]*)[\\/])?\b([A-Z]\w+)\.cs:\d/g)) {
      const list = index.get(c[2]);
      if (!list || ownTypes.has(c[2])) continue;
      const hit = c[1] ? list.filter((ns) => ns === c[1] || ns.endsWith("." + c[1])) : [];
      for (const ns of hit.length ? hit : list) cited.add(ns + "." + c[2]);
    }
  }
  for (const c of cited) if (!types.has(c)) types.add(c);

  // Layout closure: a Game.* struct nested in a surface struct is part of its layout.
  const layouts = new Map();
  const resolveNested = (typeText, ns, usingsSet) => {
    const simple = typeText.replace(/\[.*$/, "").split(".").pop();
    const list = index.get(simple);
    if (!list) return null;
    const hit = list.find((x) => x === ns) || list.find((x) => usingsSet.has(x));
    return hit ? `${hit}.${simple}` : null;
  };
  const queue = [...types];
  while (queue.length) {
    const full = queue.pop();
    if (layouts.has(full)) continue;
    const parts = full.split(".");
    // A nested type (Game.Tools.ValidationSystem.Components): the file is the outer type's.
    let dotAt = parts.length - 1;
    let nsText = parts.slice(0, dotAt).join(".");
    let name = parts[dotAt];
    while (dotAt > 1 && !(index.get(name) || []).includes(nsText)) {
      dotAt--;
      nsText = parts.slice(0, dotAt).join(".");
      name = parts[dotAt];
    }
    const file = path.join(treeRoot, nsText, name + ".cs");
    if (!fs.existsSync(file)) {
      layouts.set(full, { missing: true });
      continue;
    }
    const text = fs.readFileSync(file, "utf8");
    // A nested type (the walk above found the outer file) parses under its own name.
    const parsed = parseVanillaType(text, parts[parts.length - 1]);
    const row = {
      kind: parsed ? parsed.kind : "?",
      hash: fileHash(text),
      fields: parsed && (parsed.kind === "struct" || parsed.kind === "enum") ? parsed.fields : [],
      cadence: cadenceOf(text),
    };
    layouts.set(full, row);
    if (parsed && parsed.kind === "struct") {
      const usingsSet = usingsOf(text).ns;
      for (const f of parsed.fields) {
        const nested = resolveNested(f.split(":")[1] || "", nsText, usingsSet);
        if (nested && !layouts.has(nested)) {
          types.add(nested);
          queue.push(nested);
          if (burst.has(full) && !burst.has(nested)) burst.set(nested, burst.get(full));
        }
      }
    }
  }
  // A Burst mark can reach a nested struct only after its parent was parsed in an earlier loop; settle it once more.
  for (const [full, mark] of [...burst]) {
    const row = layouts.get(full);
    if (!row || row.kind !== "struct") {
      if (row && row.kind !== "enum") burst.delete(full);
      continue;
    }
    const ns = full.slice(0, full.lastIndexOf("."));
    for (const f of row.fields) {
      const simple = (f.split(":")[1] || "").replace(/\[.*$/, "").split(".").pop();
      const list = index.get(simple);
      if (list && list.includes(ns) && !burst.has(`${ns}.${simple}`)) burst.set(`${ns}.${simple}`, mark);
    }
  }
  // A Burst mark reaches a nested struct with its owners: those systems' jobs hold its bytes too.
  for (const [full] of burst) {
    if (burstOwners.has(full)) continue;
    const parent = [...burstOwners.keys()].find((t) => {
      const r = layouts.get(t);
      const simple = full.split(".").pop();
      return r && r.fields && r.fields.some((f) => (f.split(":")[1] || "").replace(/\[.*$/, "").split(".").pop() === simple);
    });
    burstOwners.set(full, parent ? burstOwners.get(parent) : new Set());
  }
  return { index, types, layouts, burst, burstOwners, anchors, burstJobs: burstJobsSeen, burstSystems, cited, byValue };
}

// Registered phases of a vanilla system: from SystemOrder.cs, plus Game.Common's nested spelling.
function phasesInSystemOrder(systemOrderText, full) {
  const parts = full.split(".");
  // The decompile spells a type by its short name unless two namespaces clash; accept the last one or two segments, or the whole name.
  const last = parts[parts.length - 1];
  const last2 = parts.slice(-2).join("\\.");
  const re = new RegExp(`updateSystem\\.\\w+<(?:[\\w.]*\\.)?(?:${last2}|${last})(?:\\s*,[^>]*)?>\\(SystemUpdatePhase\\.(\\w+)\\)`, "g");
  const out = new Set();
  for (const m of systemOrderText.matchAll(re)) out.add(m[1]);
  return out;
}

// ---- the lock ------------------------------------------------------------------------------------------------------------------------------

/** Vanilla references in prefab text: "UnityGUID:hex" -> the prefab field names that hold it ("(list)" for an unnamed list item). */
function scanGuidRefs(text, into) {
  for (const m of text.matchAll(/(?:"(\w+)":\s*)?\$fstrref:"(UnityGUID|UnityObjectsMap):([0-9a-f]{32})"/g)) {
    const key = m[2] + ":" + m[3];
    if (!into.has(key)) into.set(key, new Set());
    into.get(key).add(m[1] || "(list)");
  }
  return into;
}

function measureGuids() {
  const refs = new Map();
  for (const f of walkPrefabs(MIRROR, [])) scanGuidRefs(fs.readFileSync(f, "utf8"), refs);
  return refs;
}

// A null in one of these fields is a hard crash in NetInitializeSystem (SPEC 8.3, the NETNULL gate of tools/preflight.ps1), not a missing prop.
const GUID_CRASH_FIELDS = new Set(["m_Lane", "m_Piece", "m_Section"]);

function compareGuids(lock, refs, map) {
  const fails = [];
  for (const key of refs.keys()) {
    if (!lock.guids.has(key)) fails.push("DRIFT    " + key + " (" + [...refs.get(key)].join(", ") + "): a vanilla reference the lock does not list (--write in the same round as the prefab)");
  }
  for (const key of lock.guids.keys()) {
    if (!refs.has(key)) fails.push("DRIFT    " + key + ": on the lock, no prefab holds it any more (--write)");
  }
  if (map) {
    for (const [key, fields] of refs) {
      if (!key.startsWith("UnityGUID:") || map[key.slice(10)] !== undefined) continue;
      const crash = [...fields].filter((f) => GUID_CRASH_FIELDS.has(f));
      fails.push("GUIDS    " + key + " (" + [...fields].join(", ") + ") is gone from this game's PrefabsMap: it loads as null with no log line" + (crash.length ? "; in " + crash.join(", ") + " that is a hard crash when the net initializes" : ""));
    }
  }
  return fails;
}

function renderLock(version, m, treeLabel) {
  const lines = [
    "# tools/game-surface.lock - the mod's dependency surface on Game.dll, as of the last reviewed game version. Written by",
    "# `node tools/game-surface-check.js --write --decompile=<tree>`; never by hand. Rows: type <full name> <kind> <file hash> [burst:r|w:<our systems>] [fields...];",
    "# anchor <our system> <before|after> <vanilla system> <phase> [cadence]; guid <ref> <prefab fields>; burstsystem <our class with a Burst job>. Header: tools/game-surface-check.js. Patch-day steps: docs/PIPELINE.md section 7.",
    `version ${version}`,
    `tree ${treeLabel}`,
  ];
  const typeRows = [...m.layouts.keys()].filter((t) => !m.layouts.get(t).missing).sort();
  for (const t of typeRows) {
    const r = m.layouts.get(t);
    const owners = m.burstOwners.get(t);
    const mark = m.burst.has(t) ? ` burst:${m.burst.get(t)}:${owners && owners.size ? [...owners].sort().join(",") : "-"}` : "";
    const fields = r.fields.length ? ` ${r.fields.join(" ")}` : "";
    lines.push(`type ${t} ${r.kind} ${r.hash}${mark}${fields}`);
  }
  const anchors = [...m.anchors].sort((a, b) => (a.ours + a.anchor).localeCompare(b.ours + b.anchor));
  for (const a of anchors) {
    const r = m.layouts.get(a.anchor);
    const cadence = r && r.cadence ? ` ${r.cadence}` : "";
    lines.push(`anchor ${a.ours} ${a.how === "UpdateBefore" ? "before" : "after"} ${a.anchor} ${a.phase}${cadence}`);
  }
  const refs = measureGuids();
  for (const key of [...refs.keys()].sort()) lines.push("guid " + key + " " + [...refs.get(key)].sort().join(","));
  for (const s of [...m.burstSystems].sort()) lines.push("burstsystem " + s);
  return lines.join("\n") + "\n";
}

function parseLock(text) {
  const lock = { version: null, tree: null, types: new Map(), anchors: [], guids: new Map(), burstSystems: [] };
  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trimEnd();
    if (!line || line.startsWith("#")) continue;
    if (line.startsWith("version ")) lock.version = line.slice(8).trim();
    else if (line.startsWith("tree ")) lock.tree = line.slice(5).trim();
    else if (line.startsWith("type ")) {
      const p = line.split(" ");
      const row = { kind: p[2], hash: p[3], burst: null, fields: [] };
      let i = 4;
      if (p[i] && p[i].startsWith("burst:")) {
        const b = p[i].split(":");
        row.burst = b[1];
        row.owners = b[2] && b[2] !== "-" ? b[2].split(",") : [];
        i++;
      }
      row.fields = p.slice(i);
      lock.types.set(p[1], row);
    } else if (line.startsWith("anchor ")) {
      const p = line.split(" ");
      lock.anchors.push({ ours: p[1], how: p[2], anchor: p[3], phase: p[4], cadence: p.slice(5).join(" ") });
    } else if (line.startsWith("guid ")) {
      const p = line.split(" ");
      lock.guids.set(p[1], new Set((p[2] || "").split(",")));
    } else if (line.startsWith("burstsystem ")) {
      lock.burstSystems.push(line.slice(12).trim());
    }
  }
  return lock;
}

// ---- compare -------------------------------------------------------------------------------------------------------------------------------

function diffFields(oldF, newF) {
  const a = new Set(oldF);
  const b = new Set(newF);
  const removed = oldF.filter((f) => !b.has(f));
  const added = newF.filter((f) => !a.has(f));
  const order = removed.length === 0 && added.length === 0 && oldF.join(" ") !== newF.join(" ");
  return { removed, added, order };
}

/** The ordered field types: equal = equal offsets, whatever the names. */
function typesOf(fields) {
  return fields.map((f) => f.slice(f.indexOf(":") + 1)).join(" ");
}

/** Pure compare of a lock against a measured tree; returns {fails, review}. */
function compareTree(lock, m, systemOrderText) {
  const fails = [];
  const review = [];
  for (const [t, row] of lock.types) {
    const now = m.layouts.get(t);
    if (!now || now.missing) {
      fails.push(`TYPES    ${t} (${row.kind}) is gone from this tree${row.burst ? " [a Burst job names it]" : ""}: compile error; a deployed DLL throws TypeLoadException on the first call of the method that names it`);
      continue;
    }
    if (row.kind === "enum") {
      const d = diffFields(row.fields, now.fields);
      if (d.removed.length) {
        fails.push(`LAYOUT   ${t} enum: value changed or gone: ${d.removed.join(", ")}${d.added.length ? `; now ${d.added.join(", ")}` : ""}. Our DLL has the old number compiled in everywhere it names it`);
      } else if (d.added.length) {
        review.push(`${t} (enum gained ${d.added.join(", ")})`);
      }
    } else if (row.kind === "struct") {
      const d = diffFields(row.fields, now.fields);
      if (d.removed.length || d.added.length || d.order) {
        const parts = [];
        if (d.removed.length) parts.push(`removed ${d.removed.join(", ")}`);
        if (d.added.length) parts.push(`added ${d.added.join(", ")}`);
        if (d.order) parts.push("fields reordered");
        const offsetsSame = typesOf(row.fields) === typesOf(now.fields);
        let danger = "";
        if (row.burst && !offsetsSame) danger = ` *** BURST ${row.burst === "w" ? "WRITE" : "READ"} by ${(row.owners || []).join(", ") || "?"}: a shipped Burst library has the old offsets compiled in, so those systems run on wrong bytes until a rebuild ships`;
        else if (row.burst) danger = " (Burst offsets unchanged: a rename only; check the meaning)";
        fails.push(`LAYOUT   ${t} struct: ${parts.join("; ")}${danger}`);
      }
    }
    if (row.hash !== now.hash) review.push(`${t} (${row.kind}${row.burst ? `, burst:${row.burst}` : ""})`);
  }
  for (const a of lock.anchors) {
    const now = m.layouts.get(a.anchor);
    if (!now || now.missing) {
      fails.push(`ANCHORS  ${a.ours} ${a.how} ${a.anchor}: the vanilla system is gone: a compile error, and for a deployed DLL OnLoad throws TypeLoadException at JIT, so no system of the mod registers (inferred)`);
      continue;
    }
    const phases = phasesInSystemOrder(systemOrderText, a.anchor);
    if (!phases.has(a.phase)) {
      fails.push(`ANCHORS  ${a.ours} ${a.how} ${a.anchor} at ${a.phase}: SystemOrder registers it at ${phases.size ? [...phases].join(", ") : "no phase"}. An anchor to a system of another phase never runs (UpdateSystem.cs:399) and nothing logs it`);
    }
    if ((now.cadence || "") !== (a.cadence || "")) {
      fails.push(`ANCHORS  ${a.ours} ${a.how} ${a.anchor}: its update cadence changed: was '${a.cadence || "default"}', now '${now.cadence || "default"}'. Equal intervals are what let an anchored system inherit the offset (UpdateSystem.cs:406, :431)`);
    }
  }
  return { fails, review };
}

/** Prefab JSON against the tree: every Game.* type exists, every field it sets is declared on it or a base. */
function comparePrefabs(treeRoot, index) {
  const fails = [];
  const seen = new Map();
  const files = walkPrefabs(MIRROR, []);
  for (const f of files) scanPrefabText(fs.readFileSync(f, "utf8"), seen);
  const cache = new Map();
  // name / active / ... belong to ComponentBase / PrefabBase's Unity base classes outside Game.*.
  const ambient = new Set(["name", "active", "hideFlags", "components", "isDirty", "version"]);
  let fieldCount = 0;
  for (const [t, keys] of [...seen].sort(([a], [b]) => a.localeCompare(b))) {
    const parts = t.split(".");
    const name = parts.pop();
    const ns = parts.join(".");
    const list = index.get(name.replace(/\+.*$/, ""));
    if (!list || !list.includes(ns)) {
      fails.push(`PREFABS  ${t}: named by the prefab JSON but gone from this tree: the prefab fails to load`);
      continue;
    }
    const fields = fieldChain(treeRoot, index, t, cache);
    for (const k of keys) {
      fieldCount++;
      if (!ambient.has(k) && !fields.has(k)) {
        fails.push(`PREFABS  ${t}.${k}: set by the prefab JSON, not declared in this tree: the loader skips it and the field stays at its default`);
      }
    }
  }
  return { fails, typeCount: seen.size, fieldCount, fileCount: files.length };
}

// Source-only drift: does not need any decompile.
function checkVersionSites(version) {
  const fails = [];
  for (const site of VERSION_SITES) {
    const p = path.join(ROOT, site.file);
    if (!fs.existsSync(p)) {
      fails.push(`DRIFT    ${site.file} is missing (${site.what})`);
      continue;
    }
    const m = site.re.exec(fs.readFileSync(p, "utf8"));
    if (!m) fails.push(`DRIFT    ${site.file}: no tested-version string found (${site.what})`);
    else if (m[1] !== version) fails.push(`DRIFT    ${site.file} says ${m[1]}, the lock says ${version} (${site.what})`);
  }
  return fails;
}

/** Names the code uses vs the lock, without a tree: every Game.X.Y qualified name and every Burst-job token must be on a lock row. */
function checkCodeAgainstLock(lock) {
  const fails = [];
  const index = new Map();
  for (const t of lock.types.keys()) {
    const dot = t.lastIndexOf(".");
    const n = t.slice(dot + 1);
    if (!index.has(n)) index.set(n, []);
    index.get(n).push(t.slice(0, dot));
  }
  const sources = walkCs(MOD_DIR, []);
  const modCode = stripCs(fs.readFileSync(MOD_CS, "utf8"));
  const modUsings = usingsOf(modCode);
  const anchorKeys = new Set(lock.anchors.map((a) => `${a.ours}|${a.anchor}|${a.phase}`));
  for (const m of modCode.matchAll(/updateSystem\.(UpdateBefore|UpdateAfter)<\s*(\w+)\s*,\s*([\w.]+)\s*>\s*\(\s*SystemUpdatePhase\.(\w+)\s*\)/g)) {
    const [, , ours, other, phase] = m;
    if (!other.startsWith("Game") && !(index.get(other) || []).some((ns) => modUsings.ns.has(ns))) continue;
    const full = other.startsWith("Game") ? other : `${(index.get(other) || []).find((ns) => modUsings.ns.has(ns))}.${other}`;
    if (!anchorKeys.has(`${ours}|${full}|${phase}`)) fails.push(`DRIFT    Mod.cs anchors ${ours} to ${full} at ${phase}: not in the lock. A new vanilla anchor goes in the lock in the same round (--write)`);
  }
  // Qualified names are cheap to check without a tree and catch most new dependencies in CI.
  const known = new Set(lock.types.keys());
  const missing = new Set();
  for (const f of sources) {
    const code = stripCs(fs.readFileSync(f, "utf8")).replace(/^\s*using\s[^;]*;/gm, "");
    for (const m of code.matchAll(/\bGame\.[A-Z]\w*(?:\.\w+)*\.[A-Z]\w*/g)) {
      const q = m[0];
      // The longest prefix on the lock counts; a member of a known type is fine.
      const parts = q.split(".");
      let ok = false;
      for (let i = parts.length; i >= 2; i--) if (known.has(parts.slice(0, i).join("."))) ok = true;
      if (!ok && /^Game\.[A-Z]\w*\.[A-Z]\w*$/.test(q) && !lockHasNamespaceMember(q, lock)) missing.add(`${q} (${path.basename(f)})`);
    }
  }
  for (const q of missing) fails.push(`DRIFT    ${q}: a Game.* type the lock does not list. Add it in the same round (--write)`);
  return fails;
}

function lockHasNamespaceMember(q, lock) {
  // `Game.Version.current`, `Game.Simulation.SimulationUtils` etc.: the pattern cannot tell a static member from a namespace type, so a
  // two-segment Game.<Type>.<member> whose first segment is a locked type in namespace Game passes.
  const parts = q.split(".");
  return lock.types.has(`Game.${parts[1]}`);
}

// ---- self-test -----------------------------------------------------------------------------------------------------------------------------

function selfTest() {
  let fails = 0;
  const check = (name, ok) => {
    if (!ok) {
      console.log(`FAIL  self-test ${name}`);
      fails++;
    }
  };
  const structText = `namespace Game.Net;
public struct Curve : IComponentData, ISerializable
{
	public const int kMax = 3;
	public static int s_Shared;
	public Bezier4x3 m_Bezier;

	[NonSerialized]
	public float m_Length;
	public int Count => 2;
	public void Serialize<T>(T w) { int local = 3; }
	private struct Inner { public int m_Hidden; }
}`;
  const s = parseVanillaType(structText, "Curve");
  check("struct kind", s && s.kind === "struct");
  check("struct fields skip const, static, property, method, nested", s && s.fields.join(" ") === "m_Bezier:Bezier4x3 m_Length:float");
  const e = parseVanillaType("public enum F\n{\n\tNone = 0,\n\tA = 1,\n\tB,\n}\n", "F");
  check("enum values evaluated", e && e.fields.join(" ") === "None=0 A=1 B=2");
  const e2 = parseVanillaType("public enum G : uint\n{\n\tA = 0x10,\n\tB = 2,\n\tC,\n\tBoth = A | B,\n\tTop = 1 << 4,\n}\n", "G");
  check("enum hex, OR, shift", e2 && e2.fields.join(" ") === "A=16 B=2 C=3 Both=18 Top=16");
  const init = parseVanillaType("public class P : ComponentBase\n{\n\tpublic Bounds1 m_Range = new Bounds1(0f, 1f);\n\tpublic int[] m_List;\n\tpublic int Prop { get; set; }\n}\n", "P");
  check("class field with a call initializer, auto-property left out of a class", init && init.fields.join(" ") === "m_Range:Bounds1 m_List:int[]" && init.base === "ComponentBase");
  const auto = parseVanillaType("public struct Q\n{\n\tpublic int Prop { get; set; }\n\tpublic int Calc => 1;\n}\n", "Q");
  check("struct auto-property is a field", auto && auto.fields.join(" ") === "Prop:int");
  check("strip keeps code, drops strings and comments", stripCs('int a = 1; // Game.Net.Curve\nstring s = "Game.Net.Curve"; /* x */ int b;') === 'int a = 1; \nstring s = "";  int b;');
  check("strip verbatim and interpolated", stripCs('var a = @"x""y"; var b = $"q{1}"; int c;') === 'var a = ""; var b = ""; int c;');

  const scanned = new Map();
  scanPrefabText('{"$id":0,"$type":"0|Game.Prefabs.NetPrefab, Game","name":"X","m_Foo":$fstrref:"CID:0",\n"m_Rows":{"$rcontent":[{"$type":"1|Game.Prefabs.Row, Game","m_A":1},{"$type":1,"m_B":2}]}}', scanned);
  check("prefab scan root fields", scanned.has("Game.Prefabs.NetPrefab") && scanned.get("Game.Prefabs.NetPrefab").has("m_Foo"));
  check("prefab scan positional table", scanned.has("Game.Prefabs.Row") && scanned.get("Game.Prefabs.Row").has("m_A") && scanned.get("Game.Prefabs.Row").has("m_B"));

  const jobs = burstJobs(stripCs("[BurstCompile] private struct J : IJob { [ReadOnly] public ComponentLookup<Curve> c; public ComponentLookup<Edge> e; public void Execute() { b.AddComponent<Owner>(x); b.SetComponent(y, new Temp()); } }"));
  check("burst job found", jobs.length === 1 && jobs[0].name === "J");
  check("burst writes", jobs[0].writes.has("Edge") && jobs[0].writes.has("Owner") && jobs[0].writes.has("Temp") && !jobs[0].writes.has("Curve"));
  const owned = burstJobs(stripCs("public partial class SkiX : GameSystemBase { [BurstCompile] private struct J : IJob { public Curve c; } }"));
  check("burst job owner", owned.length === 1 && owned[0].owner === "SkiX");

  const order = "updateSystem.UpdateAt<AttractionSystem>(SystemUpdatePhase.GameSimulation);\nupdateSystem.UpdateAt<Game.Events.InitializeSystem>(SystemUpdatePhase.Modification2);\nupdateSystem.UpdateAfter<ValidationSystem.Components, ValidationSystem>(SystemUpdatePhase.ModificationEnd);";
  check("phase by short name", phasesInSystemOrder(order, "Game.Simulation.AttractionSystem").has("GameSimulation"));
  check("phase by qualified name", phasesInSystemOrder(order, "Game.Events.InitializeSystem").has("Modification2"));
  check("phase of a nested system", phasesInSystemOrder(order, "Game.Tools.ValidationSystem.Components").has("ModificationEnd"));

  // The mutations a patch can make, against one hand-built lock.
  const lock = parseLock("version 1\ntype Game.Net.Curve struct aaa burst:r:SkiX m_Bezier:Bezier4x3 m_Length:float\ntype Game.Net.Flag enum eee A=1 B=2\ntype Game.Simulation.AttractionSystem class bbb\nanchor Mine after Game.Simulation.AttractionSystem GameSimulation interval{return 16;}\n");
  const tree = (over) => ({
    layouts: new Map([
      ["Game.Net.Curve", { kind: "struct", hash: "aaa", fields: ["m_Bezier:Bezier4x3", "m_Length:float"] }],
      ["Game.Net.Flag", { kind: "enum", hash: "eee", fields: ["A=1", "B=2"] }],
      ["Game.Simulation.AttractionSystem", { kind: "class", hash: "bbb", fields: [], cadence: "interval{return 16;}" }],
      ...over,
    ]),
  });
  const ok = compareTree(lock, tree([]), "updateSystem.UpdateAt<AttractionSystem>(SystemUpdatePhase.GameSimulation);");
  check("identical tree passes", ok.fails.length === 0 && ok.review.length === 0);
  const added = compareTree(lock, tree([["Game.Net.Curve", { kind: "struct", hash: "ccc", fields: ["m_Bezier:Bezier4x3", "m_Extra:int", "m_Length:float"] }]]), "updateSystem.UpdateAt<AttractionSystem>(SystemUpdatePhase.GameSimulation);");
  check("a field added to a Burst struct fails loudly", added.fails.length === 1 && added.fails[0].includes("BURST READ by SkiX") && added.review.length === 1);
  const renamed = compareTree(lock, tree([["Game.Net.Curve", { kind: "struct", hash: "ccc", fields: ["m_Bezier:Bezier4x3", "m_Len:float"] }]]), "updateSystem.UpdateAt<AttractionSystem>(SystemUpdatePhase.GameSimulation);");
  check("a rename fails, without the offset alarm", renamed.fails.length === 1 && renamed.fails[0].includes("offsets unchanged"));
  const grown = compareTree(lock, tree([["Game.Net.Flag", { kind: "enum", hash: "fff", fields: ["A=1", "B=2", "C=4"] }]]), "updateSystem.UpdateAt<AttractionSystem>(SystemUpdatePhase.GameSimulation);");
  check("an appended enum member is REVIEW", grown.fails.length === 0 && grown.review.length === 2);
  const shifted = compareTree(lock, tree([["Game.Net.Flag", { kind: "enum", hash: "fff", fields: ["A=1", "New=2", "B=4"] }]]), "updateSystem.UpdateAt<AttractionSystem>(SystemUpdatePhase.GameSimulation);");
  check("a renumbered enum fails", shifted.fails.length === 1 && shifted.fails[0].includes("B=2"));
  const moved = compareTree(lock, tree([]), "updateSystem.UpdateAt<AttractionSystem>(SystemUpdatePhase.GameSimulationB);");
  check("an anchor moved to another phase fails", moved.fails.length === 1 && moved.fails[0].includes("never runs"));
  const cadence = compareTree(lock, tree([["Game.Simulation.AttractionSystem", { kind: "class", hash: "bbb", fields: [], cadence: "interval{return 32;}" }]]), "updateSystem.UpdateAt<AttractionSystem>(SystemUpdatePhase.GameSimulation);");
  check("a changed interval fails", cadence.fails.length === 1 && cadence.fails[0].includes("cadence"));
  const gone = compareTree(lock, { layouts: new Map() }, "");
  check("a removed type and anchor fail", gone.fails.length === 4);
  const changed = compareTree(lock, tree([["Game.Simulation.AttractionSystem", { kind: "class", hash: "ddd", fields: [], cadence: "interval{return 16;}" }]]), "updateSystem.UpdateAt<AttractionSystem>(SystemUpdatePhase.GameSimulation);");
  check("changed code in a file is REVIEW, not FAIL", changed.fails.length === 0 && changed.review.length === 1);

  const refs = scanGuidRefs('{"m_Lane":$fstrref:"UnityGUID:' + "a".repeat(32) + '","m_Object":$fstrref:"UnityGUID:' + "b".repeat(32) + '","m_C":$fstrref:"CID:' + "c".repeat(32) + '"}', new Map());
  check("guid scan skips our own CIDs", refs.size === 2 && refs.get("UnityGUID:" + "a".repeat(32)).has("m_Lane"));
  const gLock = parseLock("version 1\nguid UnityGUID:" + "a".repeat(32) + " m_Lane\nguid UnityGUID:" + "b".repeat(32) + " m_Object\n");
  check("guid rows parse and match", compareGuids(gLock, refs, null).length === 0);
  check("a new guid is drift", compareGuids(parseLock("version 1\nguid UnityGUID:" + "a".repeat(32) + " m_Lane\n"), refs, null).length === 1);
  const gone2 = compareGuids(gLock, refs, { ["b".repeat(32)]: "Prop" });
  check("a guid gone from the game fails, and names the crash field", gone2.length === 1 && gone2[0].includes("hard crash"));
  check("a guid in the map passes", compareGuids(gLock, refs, { ["a".repeat(32)]: "Lane", ["b".repeat(32)]: "Prop" }).length === 0);

  // Fault fixture: the real 1.6.0f1 -> 1.6.2f1 patch, as two lock rows each (written by --write from both decompile trees). Three fields
  // were inserted mid-struct in the struct SkiDaySystem's jobs hold by value (Game.Prefabs/EconomyParameterData.cs:35-39 of 1.6.2f1), and
  // Household renamed a field. Compile and engine-contract-check passed on both trees; this gate is what saw it (patch-readiness section 2).
  const econ160 = ("m_ExtractorCompanyExportMultiplier:float m_ProductionStatisticsLerpFactor:float m_Wage0:int m_Wage1:int m_Wage2:int m_Wage3:int " +
    "m_Wage4:int m_CommuterWageMultiplier:float m_CompanyBankruptcyLimit:int m_ResidentialMinimumEarnings:int m_UnemploymentBenefit:int m_Pension:int " +
    "m_FamilyAllowance:int m_ResourceConsumptionMultiplier:float2 m_ResourceConsumptionPerCitizen:float m_TouristConsumptionMultiplier:float " +
    "m_TouristInitialWealthRange:int m_TouristInitialWealthOffset:int m_WorkDayStart:float m_WorkDayEnd:float m_IndustrialEfficiency:float " +
    "m_CommercialEfficiency:float m_ExtractorProductionEfficiency:float m_OfficeResourceConsumedPerIndustrialUnit:int m_TrafficReduction:float " +
    "m_MaxCitySpecializationBonus:float m_ResourceProductionCoefficient:int m_LandValueModifier:float3 m_RentPriceBuildingZoneTypeBase:float3 " +
    "m_MixedBuildingCompanyRentPercentage:float m_ResidentialUpkeepLevelExponent:float m_CommercialUpkeepLevelExponent:float " +
    "m_IndustrialUpkeepLevelExponent:float m_PerOfficeResourceNeededForIndustrial:int m_UnemploymentAllowanceMaxDays:float " +
    "m_ShopPossibilityIncreaseDivider:int m_CityServiceWageAdjustment:float m_PlayerStartMoney:int m_BuildRefundPercentage:float3 " +
    "m_BuildRefundTimeRange:float3 m_RelocationCostMultiplier:float m_RoadRefundPercentage:float3 m_RoadRefundTimeRange:float3 " +
    "m_TreeCostMultipliers:int3 m_MapTileUpkeepCostMultiplier:AnimationCurve1 m_LoanMinMaxInterestRate:float2 m_ProfitabilityRange:int2").split(" ");
  const econ162 = econ160.flatMap((f) =>
    f === "m_FamilyAllowance:int" ? [f, "m_NewHouseholdStartMoney:int", "m_MoveOutIncomeToRentRatio:float", "m_MinFreeResidentialProperties:int"]
      : f === "m_CityServiceWageAdjustment:float" ? [f, "m_TouristTravelSpendMultiplier:float"] : [f]);
  const house = (tenth) => `m_Flags:HouseholdFlags m_Resources:int m_ConsumptionPerDay:short m_ShoppedValuePerDay:uint m_ShoppedValueLastDay:uint m_LastDayFrameIndex:uint ${tenth}:int m_MoneySpendOnBuildingLevelingLastDay:int`;
  const retroLock = parseLock("version 1.6.0f1\n" +
    `type Game.Prefabs.EconomyParameterData struct f5ffc642a0 burst:r:SkiDaySystem ${econ160.join(" ")}\n` +
    `type Game.Citizens.Household struct c6d3d8a2a7 burst:r:SkiDaySystem,SkiGuestCounterSystem ${house("m_SalaryLastDay")}\n`);
  const retro = compareTree(retroLock, { layouts: new Map([
    ["Game.Prefabs.EconomyParameterData", { kind: "struct", hash: "730fb90cb5", fields: econ162 }],
    ["Game.Citizens.Household", { kind: "struct", hash: "778e5124b2", fields: house("m_Income").split(" ") }],
  ]) }, "");
  const econFail = retro.fails.find((f) => f.includes("EconomyParameterData")) || "";
  check("fixture 1.6.2: the EconomyParameterData insert fails and names SkiDaySystem", econFail.includes("*** BURST READ by SkiDaySystem") && econFail.includes("m_NewHouseholdStartMoney:int"));
  check("fixture 1.6.2: the Household rename fails without the offset alarm", retro.fails.length === 2 && retro.fails.some((f) => f.includes("Household") && f.includes("offsets unchanged")));
  check("fixture 1.6.2: both files on the reading list", retro.review.length === 2);

  if (fails === 0) console.log("game-surface-check --self-test: PASS (parser, prefab scan, burst scan, the nine patch mutations, vanilla references, the 1.6.2 retro fixture)");
  return fails;
}

// ---- main ------------------------------------------------------------------------------------------------------------------------------------

function arg(name) {
  const hit = process.argv.slice(2).find((a) => a.startsWith(`--${name}=`));
  return hit ? hit.slice(name.length + 3) : null;
}
const flag = (name) => process.argv.slice(2).includes(`--${name}`);

function main() {
  if (flag("self-test")) return selfTest();

  const lockPath = arg("lock") ? path.resolve(arg("lock")) : DEFAULT_LOCK;
  const lockText = fs.existsSync(lockPath) ? fs.readFileSync(lockPath, "utf8") : null;
  const lock = lockText ? parseLock(lockText) : null;

  // Beside the repo in a normal checkout; an extra level up from a worktree under ClaudeProjects is the same folder.
  const defaultTree = (v) => path.resolve(ROOT, "..", "tools", "decompile", v);

  if (flag("write") || flag("list")) {
    const tree = arg("decompile") ? path.resolve(arg("decompile")) : lock ? defaultTree(lock.version) : null;
    if (!tree || !fs.existsSync(tree)) {
      console.log(`game-surface-check: FATAL no decompile tree (${tree}): pass --decompile=<folder>`);
      return FATAL;
    }
    const m = measure(tree);
    const version = arg("version") || path.basename(tree);
    if (flag("list")) {
      console.log(`surface of ${tree}: ${m.layouts.size} Game.* types, ${m.burst.size} in Burst jobs, ${m.anchors.length} vanilla anchors, ${m.burstJobs.length} Burst jobs`);
      for (const [t, mark] of [...m.burst].sort()) console.log(`burst:${mark} ${t}`);
      for (const a of m.anchors) console.log(`anchor ${a.ours} ${a.how} ${a.anchor} ${a.phase}`);
      for (const b of m.byValue) console.log(`byvalue ${b.type} ${b.job}.${b.field}`);
      for (const t of [...m.layouts.keys()].sort()) console.log(`type ${t} ${m.layouts.get(t).kind || "MISSING"}`);
      return 0;
    }
    fs.writeFileSync(lockPath, renderLock(version, m, path.basename(tree)));
    console.log(`game-surface-check: wrote ${path.relative(ROOT, lockPath)} (${m.layouts.size} types, ${m.burst.size} in Burst jobs, ${m.anchors.length} anchors, version ${version})`);
    return 0;
  }

  if (!lock) {
    console.log(`game-surface-check: FATAL ${path.relative(ROOT, lockPath)} is missing (--write --decompile=<folder> makes it)`);
    return FATAL;
  }

  const fails = [];
  // The version sites belong to the repo's own lock, not to a scratch lock used for a retro comparison.
  if (!arg("lock")) fails.push(...checkVersionSites(lock.version));
  fails.push(...checkCodeAgainstLock(lock));
  let guidMap = null;
  if (arg("guid-map")) {
    const mapPath = path.resolve(arg("guid-map"));
    if (!fs.existsSync(mapPath)) {
      console.log("game-surface-check: FATAL no guid map at " + mapPath);
      return FATAL;
    }
    guidMap = JSON.parse(fs.readFileSync(mapPath, "utf8"));
  }
  fails.push(...compareGuids(lock, measureGuids(), guidMap));

  const tree = arg("decompile") ? path.resolve(arg("decompile")) : defaultTree(lock.version);
  let treeNote;
  let review = [];
  if (fs.existsSync(tree) && fs.existsSync(path.join(tree, "Game.Common", "SystemOrder.cs"))) {
    const m = measure(tree);
    const orderText = fs.readFileSync(path.join(tree, "Game.Common", "SystemOrder.cs"), "utf8");
    const cmp = compareTree(lock, m, orderText);
    fails.push(...cmp.fails);
    review = cmp.review;
    const prefab = comparePrefabs(tree, m.index);
    fails.push(...prefab.fails);
    // A type or anchor in the code but not in the lock: a new dependency nobody froze.
    for (const t of m.layouts.keys()) {
      if (!lock.types.has(t) && !m.layouts.get(t).missing) fails.push(`DRIFT    ${t} is used by the code but not in the lock (--write in the same round as the code)`);
    }
    for (const s of m.burstSystems) {
      if (!lock.burstSystems.includes(s)) fails.push("DRIFT    " + s + " has a [BurstCompile] job and no burstsystem row in the lock (--write)");
    }
    for (const a of m.anchors) {
      if (!lock.anchors.some((x) => x.ours === a.ours && x.anchor === a.anchor && x.phase === a.phase)) fails.push(`DRIFT    anchor ${a.ours} -> ${a.anchor} at ${a.phase} is not in the lock`);
    }
    treeNote = `${path.basename(tree)}: ${lock.types.size} types, ${[...lock.types.values()].filter((r) => r.burst).length} in Burst jobs, ${lock.anchors.length} anchors, ${prefab.typeCount} prefab types / ${prefab.fileCount} prefab files`;
  } else {
    if (flag("require-decompile")) {
      console.log(`game-surface-check: FATAL no decompile at ${tree}`);
      return FATAL;
    }
    treeNote = `no decompile at ${path.basename(tree)} here: the code-against-lock checks only (CI, a fresh clone)`;
  }

  for (const f of fails) console.log(`FAIL  ${f}`);
  if (review.length) {
    console.log(`REVIEW ${review.length} file(s) of the surface changed since the lock (${lock.version}): read their diff before debugging anything in game:`);
    for (const r of review.sort()) console.log(`  ${r}`);
  }
  console.log(`game-surface-check: ${fails.length ? `FAIL (${fails.length})` : "PASS"} | ${treeNote} | ${lock.guids.size} vanilla refs${guidMap ? " against the map" : ""}${review.length ? ` | ${review.length} to review` : ""}`);
  return Math.min(fails.length, 99);
}

process.exit(main());
