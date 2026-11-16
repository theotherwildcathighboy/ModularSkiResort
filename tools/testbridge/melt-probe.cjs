#!/usr/bin/env node
// melt-probe.cjs (T222, round 2): drives the bridge op "meltprobe" for one forced-temperature melt run and writes the samples as JSON.
// Usage: node tools/testbridge/melt-probe.cjs <plan.json> <out.json>
// plan: { label, temperature (global C, forced), precipitation (forced, default 0), speed (default 2), every (frames, default 128),
//         frames (sim frames to run, default 8192), stopBelowM (end early when every 1.0 m patch is under this, default 0.02),
//         rects: [{ name, x0, z0, x1, z1, depthM }] }
// Each run: pause, force the climate, lay the patches, start sampling, run the sim, pause, stop sampling, read everything back.
// The temperature and precipitation overrides stay set after the run (the next plan sets its own); `--release` hands both back.
// Dev only: the probe writes the loaded city's snow map. Never save the city after a probe.
'use strict';
const { execFileSync } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');

const bridgeCall = path.join(__dirname, 'bridge-call.mjs');

function call(op) {
  const b64 = Buffer.from(JSON.stringify(op), 'utf8').toString('base64');
  const out = execFileSync(process.execPath, [bridgeCall, '--op', b64, '--timeout', '60'], { encoding: 'utf8' });
  const reply = JSON.parse(out.trim());
  if (!reply.ok) throw new Error(`${op.op} failed: ${reply.error}`);
  return reply;
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function main() {
  const [planPath, outPath] = process.argv.slice(2);
  if (planPath === '--release') {
    call({ op: 'climate', temperature: null, precipitation: null });
    console.log('climate released');
    return;
  }
  const plan = JSON.parse(fs.readFileSync(planPath, 'utf8'));
  const every = plan.every ?? 128;
  const frames = plan.frames ?? 8192;
  const stopBelow = plan.stopBelowM ?? 0.02;
  call({ op: 'sim', speed: 0 });
  call({ op: 'climate', temperature: plan.temperature, precipitation: plan.precipitation ?? 0 });
  const written = call({ op: 'meltprobe', action: 'write', rects: plan.rects });
  const started = call({ op: 'meltprobe', action: 'start', every, max: Math.ceil(frames / every) + 8,
    rects: plan.rects.map(({ name, x0, z0, x1, z1 }) => ({ name, x0, z0, x1, z1 })) });
  const s0 = call({ op: 'sim' });
  call({ op: 'sim', speed: plan.speed ?? 2 });
  const deep = plan.rects.filter((r) => r.depthM >= 0.9).map((r) => r.name);
  let last = null;
  for (;;) {
    await sleep(1500);
    const st = call({ op: 'sim' });
    const rd = call({ op: 'meltprobe', action: 'read', from: Math.max(0, (last?.count ?? 1) - 1) });
    last = rd;
    const tail = rd.samples[rd.samples.length - 1];
    const done = st.frameIndex - s0.frameIndex >= frames || !rd.on ||
      (tail && tail.d && deep.length && deep.every((n) => tail.d[n] && tail.d[n][0] < stopBelow));
    if (done) break;
  }
  call({ op: 'sim', speed: 0 });
  call({ op: 'meltprobe', action: 'stop' });
  const all = call({ op: 'meltprobe', action: 'read', from: 0 });
  const result = { plan, written, started, frameStart: s0.frameIndex, samples: all.samples, at: new Date().toISOString() };
  fs.writeFileSync(outPath, JSON.stringify(result, null, 1));
  // Quick fit: per rect, the least-squares slope of depth (m) on game hours over samples with depth above 0.03 m (and below the start).
  const FPH = 262144 / 24;
  for (const r of started.rects) {
    const pts = all.samples.filter((s) => s.d && s.d[r.name]).map((s) => [(s.frame - all.samples[0].frame) / FPH, s.d[r.name][0]])
      .filter(([, d]) => d > 0.03);
    if (pts.length < 3) { console.log(`${r.name}: too few points`); continue; }
    const n = pts.length, mx = pts.reduce((a, p) => a + p[0], 0) / n, my = pts.reduce((a, p) => a + p[1], 0) / n;
    const sxy = pts.reduce((a, p) => a + (p[0] - mx) * (p[1] - my), 0), sxx = pts.reduce((a, p) => a + (p[0] - mx) ** 2, 0);
    const slope = sxy / sxx;
    const k = r.airC > 0 ? -slope / r.airC : NaN;
    console.log(`${r.name}: h ${r.heightM} m, air ${r.airC} C, grad (${r.gradX}, ${r.gradZ}), ${n} pts, melt ${(-slope).toFixed(3)} m/h, k ${k.toFixed(3)} m/h/C`);
  }
}

main().catch((e) => { console.error(e.message); process.exit(1); });
