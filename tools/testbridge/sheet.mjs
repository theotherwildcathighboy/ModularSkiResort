// sheet.mjs - runs a TEST SHEET (a JSON file) through the bridge, step by step (DECISIONS 669 b). One command re-runs a pass:
//
//   node tools/testbridge/sheet.mjs <sheet.json> [--out <dir>] [--var name=value ...] [--dry]
//
// Needs the game running with the bridge READY (tools/testbridge/wait-ready.ps1). It never starts, builds or deploys anything and takes no OS
// input: every step is a bridge op, a page call (DOM, see helpers.js) or a poll. The report is <out>/<sheet name>-<time>.json plus a
// table on stdout. Exit 0 = every assertion held, 1 = an assertion failed, 2 = a step could not run (bridge down, bad sheet), 3 = broke off.
//
// A sheet:
//   { "name": "ashflat-soak", "description": "...", "vars": { "save": "SRM Baseline 1.0.1 ashflat", "days": 2 },
//     "steps": [ <step>, ... ] }
//
// Strings in a step may use ${name}: a sheet var, a --var, an `as` / `save` result of an earlier step, or ${out} (the report folder).
// A whole-string "${name}" keeps the value's type (a number stays a number).
//
// Steps ("do"):
//   op      { "do":"op", "op": {"op":"tourism","action":"snapshot"}, "as":"snap", "expect": { "path": <check> ... }, "file":"snap.json" }
//           Calls one bridge op. "as" keeps the reply for later ${as.path} / assert; "file" writes the reply into <out>.
//   js      { "do":"js", "expr":"__optToggle('Measurement diagnostics')", "as":"r", "timeoutSec":60 }   one page expression (helpers.js is injected first)
//   load    { "do":"load", "save":"<name in the game's Load Game list>", "discard": true }   loads by the pause menu (T224), waits for READY,
//           re-injects helpers, switches autosave off. A copy staged by tools/loadcheck.ps1 -StageOnly is listed as loadcheck_<stem>.
//   saveas  { "do":"saveas", "name":"SRM Test after-soak" }   saves under a NEW name through the pause menu (an existing name stops)
//   soak    { "do":"soak", "days":2, "speed":8, "every":30, "sample":{"op":"tourism"}, "as":"series", "expect":{...on the last sample} }
//           Runs the sim `days` in-game days (1 day = 262144 frames = 1 game month) at `speed`, polling every `every` seconds; samples the op
//           each poll into an array (${series}); a stalled sim gets its speed set again. Speed returns to 0 at the end unless "keep":true.
//   wait    { "do":"wait", "ms":1500 }
//   assert  { "do":"assert", "from":"snap", "expect": { "a.b.c": {"min":1,"max":9}, "x": {"eq":3} } }   no call, checks a kept value
//   repeat  { "do":"repeat", "over":[0,5,10], "as":"pct", "steps":[ ... ] }   the steps once per value (${pct}, ${pct_i} the index)
//   note    { "do":"note", "text":"..." }   a line in the report
//
// "expect" is { "<path>": <check> }. A path is dotted with array indexes (census.persisted.SkiRunBinding, samples.0.a, samples.-1.a for the last).
// A check is a literal (equals) or an object with: eq, ne, min, max, between:[lo,hi], near:[value,tolerance], exists:true|false, in:[...],
// gt, lt, contains:"text", matches:"regex". Every check is one row in the report; a step with none just has to run.
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const CALL = path.join(here, 'bridge-call.mjs');
const HELPERS = path.join(here, 'helpers.js');
const WAIT = path.join(here, 'wait-ready.ps1');
const PER_DAY = 262144;

const argv = process.argv.slice(2);
const sheetPath = argv.find(a => !a.startsWith('--') && argv[argv.indexOf(a) - 1] !== '--out' && argv[argv.indexOf(a) - 1] !== '--var');
const flag = n => argv.includes('--' + n);
const val = n => { const i = argv.indexOf('--' + n); return i >= 0 ? argv[i + 1] : undefined; };
if (!sheetPath) { console.error('usage: node sheet.mjs <sheet.json> [--out <dir>] [--var name=value ...] [--dry]'); process.exit(2); }
let sheet;
try { sheet = JSON.parse(fs.readFileSync(sheetPath, 'utf8')); } catch (e) { console.error('sheet: ' + e.message); process.exit(2); }

const stamp = new Date().toISOString().replace(/[-:]/g, '').replace(/\..*/, '').replace('T', '-');
const outDir = path.resolve(val('out') ?? path.join(path.dirname(path.resolve(sheetPath)), 'out'));
fs.mkdirSync(outDir, { recursive: true });
const vars = { ...(sheet.vars ?? {}), out: outDir };
argv.forEach((a, i) => { if (a === '--var' && argv[i + 1]) { const [k, ...v] = argv[i + 1].split('='); vars[k] = v.join('='); } });

const report = { sheet: sheet.name ?? path.basename(sheetPath), started: new Date().toISOString(), vars: { ...vars }, steps: [], checks: [], notes: [] };
let failed = 0;

// ---- helpers -----------------------------------------------------------------------------------------------------------------
function getPath(obj, p) {
    let cur = obj;
    for (const seg of String(p).split('.')) {
        if (cur == null) return undefined;
        if (Array.isArray(cur) && /^-?\d+$/.test(seg)) cur = cur.at(Number(seg));
        else cur = cur[seg];
    }
    return cur;
}
function subst(x) {
    if (typeof x === 'string') {
        const whole = x.match(/^\$\{([^}]+)\}$/);
        if (whole) { const v = lookup(whole[1]); return v === undefined ? x : v; }
        return x.replace(/\$\{([^}]+)\}/g, (m, k) => { const v = lookup(k); return v === undefined ? m : (typeof v === 'object' ? JSON.stringify(v) : String(v)); });
    }
    if (Array.isArray(x)) return x.map(subst);
    if (x && typeof x === 'object') return Object.fromEntries(Object.entries(x).map(([k, v]) => [k, subst(v)]));
    return x;
}
function lookup(k) { const [head, ...rest] = k.split('.'); return rest.length ? getPath(vars[head], rest.join('.')) : vars[head]; }

function bridge(args, timeoutMs = 120000) {
    try {
        const out = execFileSync('node', [CALL, ...args], { encoding: 'utf8', timeout: timeoutMs, windowsHide: true });
        return JSON.parse(out);
    } catch (e) {
        const text = (e.stdout || '') + (e.stderr || '');
        try { return JSON.parse(text); } catch { throw new Error('bridge: ' + (text || e.message).slice(0, 300)); }
    }
}
const op = (o, t) => bridge(['--op', Buffer.from(JSON.stringify(o)).toString('base64')], t);
const page = (expr, sec = 60) => bridge(['--js', Buffer.from(expr).toString('base64'), '--timeout', String(sec)], (sec + 30) * 1000);
const inject = () => bridge(['--inject', HELPERS, '--force']);
const sleep = ms => new Promise(r => setTimeout(r, ms));

function check(stepNo, label, actual, spec) {
    const c = typeof spec === 'object' && spec !== null && !Array.isArray(spec) ? spec : { eq: spec };
    const bad = [];
    const num = Number(actual);
    if ('exists' in c && (actual !== undefined) !== c.exists) bad.push(`exists ${c.exists}`);
    if ('eq' in c && JSON.stringify(actual) !== JSON.stringify(c.eq) && String(actual) !== String(c.eq)) bad.push(`eq ${JSON.stringify(c.eq)}`);
    if ('ne' in c && String(actual) === String(c.ne)) bad.push(`ne ${JSON.stringify(c.ne)}`);
    if ('min' in c && !(num >= c.min)) bad.push(`min ${c.min}`);
    if ('max' in c && !(num <= c.max)) bad.push(`max ${c.max}`);
    if ('gt' in c && !(num > c.gt)) bad.push(`gt ${c.gt}`);
    if ('lt' in c && !(num < c.lt)) bad.push(`lt ${c.lt}`);
    if ('between' in c && !(num >= c.between[0] && num <= c.between[1])) bad.push(`between ${c.between}`);
    if ('near' in c && !(Math.abs(num - c.near[0]) <= c.near[1])) bad.push(`near ${c.near[0]} +-${c.near[1]}`);
    if ('in' in c && !c.in.map(String).includes(String(actual))) bad.push(`in ${JSON.stringify(c.in)}`);
    if ('contains' in c && !String(actual).includes(c.contains)) bad.push(`contains "${c.contains}"`);
    if ('matches' in c && !new RegExp(c.matches).test(String(actual))) bad.push(`matches /${c.matches}/`);
    const pass = bad.length === 0;
    if (!pass) failed++;
    const row = { step: stepNo, check: label, actual, expected: c, pass, why: bad.join('; ') };
    report.checks.push(row);
    console.log(`    ${pass ? 'PASS' : 'FAIL'}  ${label} = ${JSON.stringify(actual)}${pass ? '' : '   (wanted ' + bad.join('; ') + ')'}`);
}
function runExpect(stepNo, value, expect) {
    for (const [p, spec] of Object.entries(expect ?? {})) check(stepNo, p, getPath(value, p), spec);
}
const need = (x, k) => { if (x[k] === undefined) throw new Error(`step needs "${k}"`); return x[k]; };

async function frameIndex() { const s = op({ op: 'status' }); return s?.sim?.frameIndex ?? -1; }

// ---- step runners ------------------------------------------------------------------------------------------------------------
async function runStep(raw, no, depth) {
    const st = subst(raw);
    const kind = st.do;
    const t0 = Date.now();
    const rec = { n: no, do: kind, label: st.label ?? st.name ?? st.save ?? (st.op && st.op.op) ?? '', ok: true };
    console.log(`${no}. ${kind} ${rec.label}`);
    report.steps.push(rec);
    if (flag('dry')) { rec.dry = true; return; }
    switch (kind) {
        case 'note': console.log('    ' + st.text); report.notes.push(st.text); break;
        case 'wait': await sleep(st.ms ?? 1000); break;
        case 'op': {
            const r = op(need(st, 'op'), (st.timeoutSec ?? 120) * 1000);
            if (r.ok === false) throw new Error(`op ${st.op.op}: ${r.error}`);
            if (st.as) vars[st.as] = r;
            if (st.file) fs.writeFileSync(path.join(outDir, st.file), JSON.stringify(r, null, 1));
            runExpect(no, r, st.expect);
            rec.reply = JSON.stringify(r).slice(0, 300);
            break;
        }
        case 'js': {
            inject();
            const r = page(need(st, 'expr'), st.timeoutSec ?? 60);
            if (r.ok === false) throw new Error('js: ' + r.error);
            if (st.as) vars[st.as] = r.value;
            runExpect(no, r.value, st.expect);
            rec.reply = JSON.stringify(r.value).slice(0, 300);
            break;
        }
        case 'load': {
            inject();
            op({ op: 'autosave', on: false });
            const since = new Date(Date.now() - 2000);
            const pad = n => String(n).padStart(2, '0');
            const sinceText = `${since.getFullYear()}-${pad(since.getMonth() + 1)}-${pad(since.getDate())}T${pad(since.getHours())}:${pad(since.getMinutes())}:${pad(since.getSeconds())}.000`;
            const r = page(`__dload(${JSON.stringify(need(st, 'save'))})`, 90);
            rec.reply = String(r.value);
            if (!/^loading /.test(String(r.value))) throw new Error(`load did not start: ${r.value}`);
            try { execFileSync('powershell', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', WAIT, '-Since', sinceText, '-TimeoutSec', '900'], { stdio: 'pipe', timeout: 960000 }); }
            catch (e) { throw new Error('the game did not report READY after the load: ' + String(e.stdout || e.message).slice(0, 200)); }
            inject();
            op({ op: 'autosave', on: false });
            const pop = page('__dclearPopups()', 120); if (pop.value && !/ 0$/.test(String(pop.value))) console.log('    ' + pop.value);
            break;
        }
        case 'saveas': {
            inject();
            const r = page(`__dsave(${JSON.stringify(need(st, 'name'))})`, 90);
            rec.reply = String(r.value);
            if (!/^saved /.test(String(r.value))) throw new Error(`save failed: ${r.value}`);
            vars.lastSave = st.name;
            break;
        }
        case 'soak': {
            const days = need(st, 'days'), speed = st.speed ?? 8, every = st.every ?? 30;
            op({ op: 'sim', speed });
            const f0 = await frameIndex();
            const goal = f0 + Math.round(days * PER_DAY);
            const samples = [];
            let last = f0, stall = 0;
            const deadline = Date.now() + (st.timeoutMinutes ?? 120) * 60000;
            while (true) {
                await sleep(every * 1000);
                const f = await frameIndex();
                if (f < 0) { if (++stall > 10) throw new Error('soak: the bridge is unreachable'); continue; }
                if (f <= last) { if (++stall > 6) { op({ op: 'sim', speed }); stall = 0; } } else stall = 0;
                last = f;
                if (st.sample) { const r = op(st.sample); r.frame = f; samples.push(r); }
                console.log(`    frame ${f} / ${goal} (${Math.round(100 * (f - f0) / (goal - f0))} %)`);
                if (f >= goal) break;
                if (Date.now() > deadline) throw new Error(`soak: not done after ${st.timeoutMinutes ?? 120} min (frame ${f} of ${goal})`);
            }
            if (!st.keep) op({ op: 'sim', speed: 0 });
            if (st.as) vars[st.as] = samples;
            rec.samples = samples.length;
            runExpect(no, { samples, last: samples.at(-1), first: samples[0], frames: last - f0 }, st.expect);
            break;
        }
        case 'assert': {
            const v = st.from ? lookup(st.from) : undefined;
            if (v === undefined && st.from) throw new Error(`assert: no value "${st.from}"`);
            runExpect(no, v, need(st, 'expect'));
            break;
        }
        case 'repeat': {
            if (depth > 3) throw new Error('repeat nested too deep');
            const items = need(st, 'over'), name = st.as ?? 'item';
            let i = 0;
            for (const it of items) {
                vars[name] = it; vars[name + '_i'] = i++;
                // the inner steps are NOT pre-substituted (raw), so ${name} sees this round's value
                let j = 0;
                for (const inner of raw.steps) await runStep(inner, `${no}.${i}.${++j}`, depth + 1);
            }
            break;
        }
        default: throw new Error(`unknown step "${kind}"`);
    }
    rec.seconds = Math.round((Date.now() - t0) / 100) / 10;
}

// ---- main --------------------------------------------------------------------------------------------------------------------
let code = 0;
try {
    if (!flag('dry')) {
        const s = op({ op: 'status' });
        if (s.ok === false) throw new Error('bridge status: ' + s.error);
        report.bridge = { build: s.bridgeBuild, mode: s.gameMode };
        console.log(`sheet "${report.sheet}": bridge ${s.bridgeBuild}, mode ${s.gameMode}, out ${outDir}`);
    }
    let n = 0;
    for (const step of sheet.steps ?? []) await runStep(step, String(++n), 0);
    code = failed ? 1 : 0;
} catch (e) {
    console.error('BROKE OFF: ' + e.message);
    report.error = e.message;
    code = /unknown step|needs "|usage/.test(e.message) ? 2 : 3;
}
report.finished = new Date().toISOString();
report.summary = { checks: report.checks.length, failed, exit: code };
const file = path.join(outDir, `${report.sheet.replace(/[^\w.-]+/g, '_')}-${stamp}.json`);
fs.writeFileSync(file, JSON.stringify(report, null, 1));
console.log(`\n${report.checks.length - failed} / ${report.checks.length} check(s) PASS${code === 3 ? ', run BROKE OFF' : ''}; report ${file}`);
process.exit(code);
