#!/usr/bin/env node
// check-ste.js: the ASD-STE100 ratchet gate (DECISIONS 614).
//
// WHAT IT DOES. It runs the vendored structural linter (tools/ste-lint/ste-lint.py, MIT) over the player text and the agent-facing
// docs. It counts the hard ("advisory-free") findings per target: semicolon, long sentence, phrasal verb, nominalization, marketing
// adjective, synonym rotation, dangling conjunction. Passive voice and compound tenses are advisory and do not count.
// The count of a target must not rise above its number in tools/ste-lint/baseline.json. A lower count passes and prints a note.
// Run `node tools/check-ste.js --write` to lower the baseline after a cleanup. The gate never raises a number by itself.
//
// TARGETS.
// - "player-text": every value in locale/en-US.json. Each string is one paragraph, so the linter never joins two strings.
// - Tracked docs: README.md, CONTRIBUTING.md, release/DESCRIPTION.md, the docs-site pages (not changelog.mdx and timeline.mdx,
//   which are history), and mod/SkiResortMod/EngineNotes/*.md.
// - Local-only docs (git-ignored, so CI does not have them): CLAUDE.md, docs/PIPELINE.md, docs/GLOSSARY.md, docs/DESIGN-SYSTEM.md and the project
//   skills. The gate checks a local-only target only when the file exists.
// A target that is not in the baseline must have 0 hard findings. Code comments are not a target: they cite code and log text.
//
// PYTHON. The linter needs Python 3 (standard library only). The gate tries $env:STE_PYTHON, then Blender's bundled Python
// (the only real Python on the dev machine), then `python` and `py -3` (CI). It fails when it finds none.
//
// Usage: node tools/check-ste.js [--write] [--list]   (--list prints every hard finding)
'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');
const cp = require('child_process');

const repo = path.resolve(__dirname, '..');
const lintPy = path.join(__dirname, 'ste-lint', 'ste-lint.py');
const baselinePath = path.join(__dirname, 'ste-lint', 'baseline.json');
const args = new Set(process.argv.slice(2));

function findPython() {
  const cands = [];
  if (process.env.STE_PYTHON) cands.push([process.env.STE_PYTHON, []]);
  const blenderRoot = 'C:\\Program Files (x86)\\Steam\\steamapps\\common\\Blender';
  try {
    for (const v of fs.readdirSync(blenderRoot).sort().reverse()) {
      const p = path.join(blenderRoot, v, 'python', 'bin', 'python.exe');
      if (fs.existsSync(p)) cands.push([p, []]);
    }
  } catch { /* no Blender on this machine */ }
  cands.push(['python', []], ['python3', []], ['py', ['-3']]);
  for (const [exe, pre] of cands) {
    const r = cp.spawnSync(exe, [...pre, '-I', '-c', 'import sys; print(sys.version_info[0])'], { encoding: 'utf8' });
    if (r.status === 0 && r.stdout.trim() === '3') return [exe, pre];
  }
  return null;
}

function listTargets() {
  const t = [];
  const add = (rel, local) => { if (fs.existsSync(path.join(repo, rel))) t.push({ name: rel, file: path.join(repo, rel), local }); };
  for (const f of ['README.md', 'CONTRIBUTING.md', 'release/DESCRIPTION.md']) add(f, false);
  const walk = (dir) => {
    for (const e of fs.readdirSync(path.join(repo, dir), { withFileTypes: true })) {
      const rel = dir + '/' + e.name;
      if (e.isDirectory()) walk(rel);
      else if (/\.mdx?$/.test(e.name) && !/^(changelog|timeline)\.mdx$/.test(e.name)) add(rel, false);
    }
  };
  walk('docs-site/src/content/docs');
  walk('mod/SkiResortMod/EngineNotes');
  for (const f of ['CLAUDE.md', 'docs/PIPELINE.md', 'docs/GLOSSARY.md', 'docs/DESIGN-SYSTEM.md']) add(f, true);
  const skills = path.join(repo, '.claude', 'skills');
  if (fs.existsSync(skills)) {
    for (const s of fs.readdirSync(skills)) if (s !== 'asd-ste100') add(`.claude/skills/${s}/SKILL.md`, true);
  }
  return t;
}

function playerTextFile(tmpDir) {
  const j = JSON.parse(fs.readFileSync(path.join(repo, 'locale', 'en-US.json'), 'utf8'));
  const all = j.entries || j;
  const entries = Object.entries(all).filter(([, v]) => typeof v === 'string' && /[A-Za-z]/.test(v));
  // Hub icon tags (DECISIONS 620) are linted in their word form, the text a player reads in "words" mode: <icon:NAME> becomes the
  // SkiResort.Hub.ICON[NAME] word, and <mark:NAME> goes away with the spaces after it (the row label repeats the state in words).
  const wordForm = (s) => s
    .replace(/<mark:[A-Za-z0-9_./-]+>\s*/g, '')
    .replace(/<icon:([A-Za-z0-9_./-]+)>/g, (_, n) => (typeof all[`SkiResort.Hub.ICON[${n}]`] === 'string' ? all[`SkiResort.Hub.ICON[${n}]`] : n));
  const lines = [];
  const keyAt = new Map();
  for (const [k, v] of entries) {
    keyAt.set(lines.length + 1, k);
    lines.push(wordForm(v).replace(/\s*\n\s*/g, ' '), '');
  }
  const f = path.join(tmpDir, 'player-text.md');
  fs.writeFileSync(f, lines.join('\n'));
  return { name: 'player-text', file: f, local: false, keyAt };
}

const py = findPython();
if (!py) { console.log('FAIL check-ste: no Python 3 found (set STE_PYTHON)'); process.exit(1); }
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'check-ste-'));
const targets = [playerTextFile(tmp), ...listTargets()];
const r = cp.spawnSync(py[0], [...py[1], '-I', lintPy, '--json', '--baseline', '1000000', ...targets.map((t) => t.file)],
  { encoding: 'utf8', maxBuffer: 1 << 28 });
fs.rmSync(tmp, { recursive: true, force: true });
let out;
try { out = JSON.parse(r.stdout); } catch { console.log(`FAIL check-ste: linter exit ${r.status}: ${(r.stderr || r.stdout).slice(0, 300)}`); process.exit(1); }

const norm = (p) => path.resolve(p).toLowerCase();
const byFile = new Map(targets.map((t) => [norm(t.file), { t, hard: [] }]));
for (const v of out.violations || []) {
  if (v.level !== 'advisory-free') continue;
  const slot = byFile.get(norm(v.file));
  if (slot) slot.hard.push(v);
}

const baseline = fs.existsSync(baselinePath) ? JSON.parse(fs.readFileSync(baselinePath, 'utf8')) : {};
const next = {};
let fails = 0, lower = 0, total = 0;
for (const { t, hard } of byFile.values()) {
  const n = hard.length; total += n;
  const allowed = Object.prototype.hasOwnProperty.call(baseline, t.name) ? baseline[t.name] : 0;
  next[t.name] = args.has('--write') ? n : Math.min(n, allowed);
  if (n > allowed && !args.has('--write')) {
    fails++;
    console.log(`FAIL ${t.name}: ${n} hard STE finding(s), baseline ${allowed}`);
    for (const v of hard.slice(0, 40)) {
      const where = t.keyAt ? t.keyAt.get(v.line) || `line ${v.line}` : `${t.name}:${v.line}`;
      console.log(`     ${where}: ${v.rule} "${v.match}"`);
    }
  } else if (n < allowed) lower++;
  if (args.has('--list')) for (const v of hard) console.log(`  ${t.keyAt ? t.keyAt.get(v.line) : t.name + ':' + v.line}: ${v.rule} "${v.match}"`);
}
// Local-only targets keep their baseline row when the file is absent (CI), so a CI run never drops them.
for (const [k, v] of Object.entries(baseline)) if (!(k in next)) next[k] = v;
if (args.has('--write')) {
  const sorted = Object.fromEntries(Object.keys(next).sort().map((k) => [k, next[k]]));
  fs.writeFileSync(baselinePath, JSON.stringify(sorted, null, 2) + '\n');
  console.log(`check-ste: baseline written (${Object.keys(sorted).length} targets, ${total} hard findings)`);
  process.exit(0);
}
// Fixed wording rule (glossary section 6): the game draws no tab for a one-tab mod, so an Options path never names "Main"
// (verified in game, 2026-11-07). The only allowed form is "Options > Modular Ski Resort > <group>".
const enUS = JSON.parse(fs.readFileSync(path.join(repo, 'locale', 'en-US.json'), 'utf8'));
for (const [k, v] of Object.entries(enUS.entries || enUS)) {
  if (typeof v === 'string' && /Modular Ski Resort > Main\b/.test(v)) { fails++; console.log(`FAIL ${k}: Options path names "Main" (write Options > Modular Ski Resort > <group>)`); }
}
for (const t of targets) {
  if (t.keyAt) continue;
  fs.readFileSync(t.file, 'utf8').split('\n').forEach((l, i) => {
    if (/Modular Ski Resort > Main\b/.test(l)) { fails++; console.log(`FAIL ${t.name}:${i + 1}: Options path names "Main"`); }
  });
}
const note = lower ?` | ${lower} target(s) below baseline: run node tools/check-ste.js --write to lower it` : '';
console.log(`check-ste: ${byFile.size} targets, ${total} hard finding(s), ${fails} over baseline - ${fails ? 'FAIL' : 'PASS'}${note}`);
process.exit(fails ? 1 : 0);
