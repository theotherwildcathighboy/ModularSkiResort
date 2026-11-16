// T226 A/B analysis (round 2, tourism-lab): node tourism-ab-analyze.cjs <out dir> [TourismOverhaul.log]
// Reads ab-<pct>.json (bridge `tourism` samples) and, when given, the Tourism Overhaul diagnostics log ("tourists N / target M" blocks,
// assigned to a phase by the wall-clock window in ab-<pct>.window.json written by the driver). Prints one row per phase.
'use strict';
const fs = require('fs');
const path = require('path');
const dir = process.argv[2];
const toLog = process.argv[3];
const PER_DAY = 262144;

const toBlocks = [];
if (toLog && fs.existsSync(toLog)) {
  const lines = fs.readFileSync(toLog, 'utf8').split(/\r?\n/);
  for (let i = 0; i < lines.length; i++) {
    const h = /^\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d),\d+\] \[INFO\]\s+--- tourism diagnostics ---/.exec(lines[i]);
    if (!h) continue;
    const m = /tourists (\d+) \/ target (\d+)/.exec(lines[i + 1] || '');
    const intr = /intrinsic tourist target (\d+)/.exec(lines.slice(i, i + 20).join('\n'));
    const spawned = /spawned by mod since last snapshot: (\d+)\//.exec(lines.slice(i, i + 5).join('\n'));
    if (m) toBlocks.push({ t: new Date(h[1].replace(' ', 'T')), tourists: +m[1], target: +m[2], intrinsic: intr ? +intr[1] : null, spawned: spawned ? +spawned[1] : null });
  }
}

const mean = a => a.length ? a.reduce((s, x) => s + x, 0) / a.length : NaN;
const rows = [];
for (const f of fs.readdirSync(dir).filter(n => /^ab-[\d.]+\.json$/.test(n))) {
  const pct = parseFloat(f.slice(3, -5));
  const j = JSON.parse(fs.readFileSync(path.join(dir, f), 'utf8'));
  const s = j.samples;
  if (!s.length) continue;
  const frames = s[s.length - 1].frame - s[0].frame;
  const days = frames / PER_DAY;
  // Skip the first quarter day: TourismSystem updates every 32,768 frames and TO fills toward a new target.
  const settled = s.filter(x => x.frame - s[0].frame >= PER_DAY / 4);
  const cur = settled.map(x => x.people);
  const A = [...new Set(settled.map(x => x.A))].join('/');
  const row = {
    pct, days: +days.toFixed(2), samples: s.length, A, A0: settled.length ? settled[0].A0 : null, mod: settled.length ? settled[0].mod : null,
    vanillaTarget: [...new Set(settled.map(x => x.target))].join('/'),
    peopleMean: Math.round(mean(cur)), peopleMin: Math.min(...cur), peopleMax: Math.max(...cur),
    householdsMean: Math.round(mean(settled.map(x => x.households))),
    arrivalsPerDay: Math.round(s.reduce((a, x) => a + (x.newHouseholds || 0), 0) / days),
    arrivalPeoplePerDay: Math.round(s.reduce((a, x) => a + (x.newPeople || 0), 0) / days),
    lodging: settled.length ? settled[settled.length - 1].lodging : null,
  };
  const winPath = path.join(dir, `ab-${pct}.window.json`);
  if (toBlocks.length && fs.existsSync(winPath)) {
    const w = JSON.parse(fs.readFileSync(winPath, 'utf8'));
    const t0 = new Date(w.start), t1 = new Date(w.end);
    const span = t1 - t0;
    const b = toBlocks.filter(x => x.t >= new Date(+t0 + span / 8) && x.t <= t1);
    row.toTargetMean = Math.round(mean(b.map(x => x.target)));
    row.toTouristsMean = Math.round(mean(b.map(x => x.tourists)));
    row.toIntrinsic = Math.round(mean(b.filter(x => x.intrinsic != null).map(x => x.intrinsic)));
    row.toBlocks = b.length;
  }
  rows.push(row);
}
rows.sort((a, b) => a.pct - b.pct);
console.log(JSON.stringify(rows, null, 1));
