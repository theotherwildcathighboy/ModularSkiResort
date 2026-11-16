// T226 paired A/B analysis: node tourism-alt-analyze.cjs <alt.jsonl> <TourismOverhaul.log>
// Per slice: the bonus, A at the slice end, and Tourism Overhaul's target / intrinsic target / tourists in the second half of the
// slice (TourismSystem updates A every 32,768 frames, so the first half may still hold the previous slice's A). Then each bonus slice
// against the mean of its two 0 % neighbors.
'use strict';
const fs = require('fs');
const slices = fs.readFileSync(process.argv[2], 'utf8').trim().split(/\r?\n/).map(l => JSON.parse(l));
const lines = fs.readFileSync(process.argv[3], 'utf8').split(/\r?\n/);
const blocks = [];
for (let i = 0; i < lines.length; i++) {
  const h = /^\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d),\d+\] \[INFO\]\s+--- tourism diagnostics ---/.exec(lines[i]);
  if (!h) continue;
  const m = /tourists (\d+) \/ target (\d+)/.exec(lines[i + 1] || '');
  const near = lines.slice(i, i + 20).join('\n');
  const intr = /intrinsic tourist target (\d+)/.exec(near);
  const rooms = /occupied of (\d+) total/.exec(near);
  if (m) blocks.push({ t: new Date(h[1].replace(' ', 'T')), tourists: +m[1], target: +m[2], intrinsic: intr ? +intr[1] : NaN, rooms: rooms ? +rooms[1] : NaN });
}
const mean = a => a.reduce((s, x) => s + x, 0) / a.length;
const rows = slices.map(s => {
  const t0 = new Date(s.start), t1 = new Date(s.end), mid = new Date((+t0 + +t1) / 2);
  const b = blocks.filter(x => x.t >= mid && x.t <= t1);
  return { slice: s.slice, pct: s.pct, A: s.snap.A, A0: s.snap.A0, people: s.snap.people, n: b.length,
    target: b.length ? Math.round(mean(b.map(x => x.target))) : null, intrinsic: b.length ? Math.round(mean(b.map(x => x.intrinsic))) : null,
    tourists: b.length ? Math.round(mean(b.map(x => x.tourists))) : null, rooms: b.length ? Math.round(mean(b.map(x => x.rooms))) : null };
});
console.log(JSON.stringify(rows));
const pairs = [];
for (let i = 1; i < rows.length - 1; i += 2) {
  const on = rows[i], a = rows[i - 1], c = rows[i + 1];
  if (on.target == null || a.target == null || c.target == null) continue;
  const off = k => (a[k] + c[k]) / 2;
  pairs.push({ slice: i, A: [off('A'), on.A], targetPct: +((on.target / off('target') - 1) * 100).toFixed(1),
    intrinsicPct: +((on.intrinsic / off('intrinsic') - 1) * 100).toFixed(1), APct: +((on.A / off('A') - 1) * 100).toFixed(1) });
}
console.log(JSON.stringify(pairs));
for (const k of ['APct', 'targetPct', 'intrinsicPct']) console.log(k, 'mean', pairs.length ? mean(pairs.map(p => p[k])).toFixed(1) : 'n/a');
