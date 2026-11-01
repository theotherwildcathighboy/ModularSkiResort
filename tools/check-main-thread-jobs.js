#!/usr/bin/env node
// Gate JOBREAD (DECISIONS 521, PIPELINE "per-frame work at release scale"): no mod system reads or writes a vanilla component that the
// city's vehicle jobs write through EntityManager, and none writes AttractivenessProvider that way. Each such call completes the jobs on
// that type, so the main thread waits for the whole city: +4.8 ms per frame at 3x on a 355k city. Use a Burst job on Dependency.
// A rare path (a tool click, once per load) says so with "main-thread ok" on the same line.
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..', 'mod', 'SkiResortMod', 'Systems');
const NAV = 'CarCurrentLane|CarNavigation|CarNavigationLane|WatercraftCurrentLane|AircraftCurrentLane|TrainCurrentLane|TrainNavigation|Blocker';
const CALL = 'EntityManager\\.(?:GetComponentData|SetComponentData|TryGetComponent|GetBuffer)';
const RULES = [
    new RegExp(`${CALL}\\s*<\\s*(?:[\\w.]+\\.)?(?:${NAV})\\s*>`),
    new RegExp(`${CALL}\\s*\\([^;]*\\bout\\s+(?:[\\w.]+\\.)?(?:${NAV})\\b`),
    new RegExp(`EntityManager\\.SetComponentData\\s*(?:<\\s*(?:[\\w.]+\\.)?AttractivenessProvider\\s*>|\\([^;]*new\\s+(?:[\\w.]+\\.)?AttractivenessProvider\\b)`),
];

function walk(dir) {
    return fs.readdirSync(dir, { withFileTypes: true }).flatMap((e) =>
        e.isDirectory() ? walk(path.join(dir, e.name)) : e.name.endsWith('.cs') ? [path.join(dir, e.name)] : []);
}

const files = walk(root);
const hits = [];
for (const file of files) {
    fs.readFileSync(file, 'utf8').split(/\r?\n/).forEach((line, i) => {
        const code = line.replace(/\/\/(?!.*main-thread ok).*$/, '');
        if (!code.includes('main-thread ok') && RULES.some((r) => r.test(code)))
            hits.push(`${path.relative(root, file)}:${i + 1}: ${line.trim()}`);
    });
}
for (const hit of hits)
    console.log(hit);
if (hits.length > 0)
    process.exit(1);
console.log(`${files.length} system files, no main-thread access to job-written vehicle components or AttractivenessProvider writes`);
