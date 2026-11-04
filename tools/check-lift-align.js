#!/usr/bin/env node
// check-lift-align.js - the lift alignment gates a file can show (docs: lift-alignment-2026-11-02; PIPELINE section 6 "Lift alignment").
//
//   PINDROP   (G2) every carrier pose drops by LiftMoverMath.CarrierDrop (grip top minus the jaw's rope point): no system subtracts a
//             grip top from a rope point by hand, the catalog's CarrierDrops view and the tram call the helper, ChairPinSystem reads
//             CarrierDrops. Bug class: the grip TOP rode the rope (round-2 contract D8) and the rope ran over the jaw.
//   ROPELANE  (G3) per loop lift type, the line piece, every line tower and both stations reference ONE haul-rope lane prefab (FindAnchor
//             matches anchors by lane prefab, LaneSystem.cs:4049, :2466-2505: a tower or station on another lane leaves the rope end
//             unanchored), that lane is ours (never the vanilla HV lane 5d88ac45..., m_Hanging 0.03), draws the still LOW-voltage
//             rope mesh SkiRopeStillLVA (the vanilla LV cable bbb9724d..., 0.12 m across, DECISIONS 573 h, with swaying off) and sags
//             exactly its type's value (sag below; = the catalog Envelope.LaneHanging, LiftBatch CT-12). The tram's track rope draws
//             SkiRopeStillHVA, its haul rope SkiRopeStillLVA, both at 0.04. Bug classes: the FG rope sagged like a power line on the vanilla
//             HV lane (0.03, 12.6 deg break-over per tower); a swaying cable mesh (vanilla m_HangingSwaying) moves the rope away from the
//             carriers that ride it in wind, by about the sag.
//   BONEROOT  every owned skinned mesh's root bone is identity (rotation 0,0,0,1, scale 1): the import gives it the FBX scene
//             conversion and the game composes it into every skin matrix. Bug class: the rig probe drew turned and 100x.
//   CARRIERPASS the lift-line tool keeps the carriers and their grips passable for its whole session (LiftLineToolSystem start / stop /
//             station hooks, the grip list). Bug class: re-drawing a bulldozed middle section was refused, OverlapExisting on the
//             neighbor section's chairs; a section delete next to riding chairs, OverlapExisting on their grips.
//
//   MANIFESTSHA a station manifest that was RE-FROZEN (prefab_contract.refrozen, written by assets/blender/refreeze_m_manifests.py) pins
//             the deployed building prefab it was frozen from: prefab_contract.source_sha256 must equal the sha256 of that prefab under
//             --root. test_fg_family_seams M1 checks the same thing but needs Blender; this finds the drift at preflight. Bug class:
//             lift alignment phase 1 (2026-11-02) repointed the FG stations' two HV sub-lanes and the M manifests stayed frozen on the
//             old prefab until the Blender suite failed (DECISIONS 592 q). Manifests without a "refrozen" note are import-time snapshots
//             (the prefab as it was BEFORE that import's repoint, e.g. fixedgrip/N) and legitimately differ from the deployed file.
//
// Usage: node tools/check-lift-align.js [--root <ImportedData dir>] [--self-test]   (default: gamedata-mirror/ImportedData; preflight passes the game's)
// Exit code = number of failures. Last line: "check-lift-align: OK ..." on success.

'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');
const crypto = require('crypto');

const repo = path.resolve(__dirname, '..');
const argRoot = process.argv.indexOf('--root');
const root = argRoot > 0 ? path.resolve(process.argv[argRoot + 1]) : path.join(repo, 'gamedata-mirror', 'ImportedData');
const systems = path.join(repo, 'mod', 'SkiResortMod', 'Systems');

const failures = [];
const fail = (code, msg) => failures.push(`${code} ${msg}`);

// ---- MANIFESTSHA (declared first: --self-test runs it on fixtures, with no tree) ---------------------------------------------------
const REFREEZE_CMD = '"C:\\Program Files (x86)\\Steam\\steamapps\\common\\Blender\\blender.exe" --background --factory-startup ' +
    '--python-exit-code 1 --python assets\\blender\\refreeze_m_manifests.py -- --why "<what changed in the prefab>" --write';
function* manifestFiles(dir) {
    if (!fs.existsSync(dir)) return;
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
        const p = path.join(dir, e.name);
        if (e.isDirectory()) { if (e.name !== 'archive') yield* manifestFiles(p); }
        else if (e.name.endsWith('.manifest.json')) yield p;
    }
}
// Every manifest under importDir that carries prefab_contract.source_sha256: a re-frozen one is checked against the prefab under rootDir.
function manifestShaCheck(importDir, rootDir) {
    const out = { problems: [], checked: 0, snapshots: 0 };
    for (const f of manifestFiles(importDir)) {
        let pc;
        try { pc = JSON.parse(fs.readFileSync(f, 'utf8')).prefab_contract; } catch (e) { continue; }
        if (!pc || !pc.source_sha256) continue;
        if (!pc.refrozen) { out.snapshots++; continue; }
        const parts = String(pc.source).split(/[\\/]/);
        const tail = parts.slice(-2).join('/');
        const prefab = path.join(rootDir, parts[parts.length - 2], parts[parts.length - 1]);
        const rel = path.relative(importDir, f).replace(/\\/g, '/');
        if (!fs.existsSync(prefab)) { out.problems.push(`${rel}: its frozen prefab ${tail} is not under ${rootDir}`); continue; }
        out.checked++;
        const sha = crypto.createHash('sha256').update(fs.readFileSync(prefab)).digest('hex');
        if (sha !== pc.source_sha256)
            out.problems.push(`${rel}: frozen on ${pc.source_sha256.slice(0, 12)} but ${tail} is now ${sha.slice(0, 12)}: the prefab ` +
                              `changed after the baseline was frozen. Re-freeze in the same round: ${REFREEZE_CMD}`);
    }
    return out;
}
// MANIFESTSHA self-test: returns the number of failed cases (a merged --self-test handler adds it to its own count).
function manifestShaSelfTest() {
    // Fixtures: a re-frozen manifest whose pinned sha is current / stale, an import-time snapshot (never pinned), a missing prefab.
    const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'lift-align-selftest-'));
    const imp = path.join(tmp, 'import'), rt = path.join(tmp, 'ImportedData');
    const dir = path.join(imp, 'stations', 'M', 'StM');
    fs.mkdirSync(dir, { recursive: true });
    fs.mkdirSync(path.join(imp, 'archive', 'old'), { recursive: true });
    fs.mkdirSync(path.join(rt, 'SkiFoo'), { recursive: true });
    const prefabBytes = Buffer.from('{ "name": "SkiFoo" }');
    fs.writeFileSync(path.join(rt, 'SkiFoo', 'SkiFoo.Prefab'), prefabBytes);
    const good = crypto.createHash('sha256').update(prefabBytes).digest('hex');
    const stale = '0'.repeat(64);
    const write = (d, name, pc) => fs.writeFileSync(path.join(d, name + '.manifest.json'), JSON.stringify({ name, prefab_contract: pc }));
    const src = 'gamedata-mirror/ImportedData/SkiFoo/SkiFoo.Prefab';
    const cases = [   // label, prefab_contract, expected problems, checked, snapshots
        ['current sha passes', { source: src, source_sha256: good, refrozen: {} }, 0, 1, 0],
        ['STALE sha fails', { source: src, source_sha256: stale, refrozen: {} }, 1, 1, 0],
        ['import-time snapshot (no refrozen) is not pinned', { source: src, source_sha256: stale }, 0, 0, 1],
        ['pinned prefab missing fails', { source: 'gamedata-mirror/ImportedData/SkiGone/SkiGone.Prefab', source_sha256: good, refrozen: {} }, 1, 0, 0],
    ];
    let bad = 0;
    for (const [label, pc, wantProblems, wantChecked, wantSnap] of cases) {
        write(dir, 'StM', pc);
        const r = manifestShaCheck(imp, rt);
        const ok = r.problems.length === wantProblems && r.checked === wantChecked && r.snapshots === wantSnap &&
                   (label !== 'STALE sha fails' || (r.problems[0].includes(stale.slice(0, 12)) && r.problems[0].includes(good.slice(0, 12)) &&
                                                   r.problems[0].includes('refreeze_m_manifests.py')));
        if (!ok) { console.log(`self-test FAIL: ${label}: ${JSON.stringify(r)}`); bad++; }
    }
    // a stale manifest under archive/ is history and is never checked
    write(dir, 'StM', { source: src, source_sha256: good, refrozen: {} });
    write(path.join(imp, 'archive', 'old'), 'old', { source: src, source_sha256: stale, refrozen: {} });
    if (manifestShaCheck(imp, rt).problems.length !== 0) { console.log('self-test FAIL: archive/ must be skipped'); bad++; }
    fs.rmSync(tmp, { recursive: true, force: true });
    console.log(`${bad ? 'FAIL' : 'PASS'}  MANIFESTSHA self-test: ${cases.length + 1} cases (a planted stale sha, a missing prefab, a snapshot, archive/)`);
    return bad;
}
if (process.argv.includes('--self-test')) {
    const bad = manifestShaSelfTest();
    console.log(`check-lift-align --self-test: ${bad ? bad + ' case(s) FAILED' : 'OK'}`);
    process.exit(bad ? 1 : 0);
}

// ---- PINDROP ------------------------------------------------------------------------------------------------------------------
function stripComments(src) {
    return src.replace(/\/\*[\s\S]*?\*\//g, '').replace(/\/\/[^\n]*/g, '');
}
const csFiles = fs.readdirSync(systems).filter(f => f.endsWith('.cs'));
let pinScanned = 0;
for (const f of csFiles) {
    const code = stripComments(fs.readFileSync(path.join(systems, f), 'utf8'));
    pinScanned++;
    // a grip top subtracted from a y by hand (the old pin: position.y -= gripTop)
    const bad = code.match(/\.y\s*-=\s*[\w.\[\]]*grip(Top)?\b[^;]*;/gi);
    if (bad) fail('PINDROP', `${f}: subtracts a grip top from a position by hand (${bad[0].trim()}); pose with LiftMoverMath.CarrierDrop`);
    // LiftLoopSampler.Pose takes the drop, never a grip top
    for (const m of code.matchAll(/LiftLoopSampler\.Pose\(([^;]*)\)/g))
        if (/grip/i.test(m[1])) fail('PINDROP', `${f}: LiftLoopSampler.Pose is passed a grip top (${m[1].trim()}), not the drop`);
}
const need = [
    ['SkiPrefabCatalogSystem.cs', /LiftMoverMath\.CarrierDrop\(/, 'the CarrierDrops view must be built with LiftMoverMath.CarrierDrop'],
    ['TramShuttleSystem.cs', /LiftMoverMath\.CarrierDrop\(/, 'the tram must drop its cabins with LiftMoverMath.CarrierDrop'],
    ['ChairPinSystem.cs', /CarrierDrops\b/, 'the pin and the mover must read the catalog CarrierDrops view'],
    ['LiftMoverMath.cs', /public static float CarrierDrop\(float gripTop, float gripTopAboveRopeM\)\s*\{\s*return gripTop - gripTopAboveRopeM;/,
        'CarrierDrop must stay grip top minus the rope point'],
];
for (const [f, re, why] of need) {
    const p = path.join(systems, f);
    if (!fs.existsSync(p)) { fail('PINDROP', `${f} missing`); continue; }
    if (!re.test(stripComments(fs.readFileSync(p, 'utf8')).replace(/\s+/g, ' ').replace(/\{ /g, '{').replace(/ \}/g, '}')) &&
        !re.test(stripComments(fs.readFileSync(p, 'utf8')))) fail('PINDROP', `${f}: ${why}`);
}

// ---- ROPELANE -------------------------------------------------------------------------------------------------------------------
const VANILLA_HV_LANE = '5d88ac45a40b30341a79535930e3b735';
const LV_WIRE = '37c8abdb015384f4e8b7ce9b5b2b6c9b';
// Still rope meshes (rope sag round): copies of the vanilla HV / LV cable lane meshes (463c051d... / bbb9724d..., same geometry and
// surface CIDs) with CurveProperties.m_HangingSwaying false, so the rope never sways away from its carriers. Every lift rope lane draws one.
const STILL = {
    hv: { name: 'SkiRopeStillHVA', geometry: 'fca90cab9a5b6a1f8aa088fe38f1dcb3' },   // the tram track rope
    lv: { name: 'SkiRopeStillLVA', geometry: '5d53b43c3c4f017f458bd94a1ab1275d' },   // the FG / DQ / GD haul ropes, the tram haul rope
};
const LIB = 'c030ee3ff256df91';
const groups = [
    { type: 'fixedGrip', lane: 'SkiFixedGripHaulRope', sag: 0.02,
      prefabs: ['SkiChairliftPiece', 'SkiLiftTowerFG12', 'SkiLiftTowerFG16', 'SkiLiftTowerFG20', `${LIB}/SkiLiftTowerFG2`,
                'SkiBaseStationBuilding', 'SkiTopStationBuilding'] },
    { type: 'detachable', lane: 'SkiDetachableHaulRope', sag: 0.025,
      prefabs: ['SkiDetachablePiece', 'SkiLiftTowerDQ10', 'SkiLiftTowerDQ12', 'SkiLiftTowerDQ14', 'SkiLiftTowerDQ16', 'SkiLiftTowerDQ18',
                'SkiLiftTowerDQ20', 'SkiDQBaseStationBuilding', 'SkiDQTopStationBuilding'] },
    { type: 'gondola', lane: 'SkiGondolaHaulRope', sag: 0.025,
      prefabs: ['SkiGondolaPiece', 'SkiLiftTowerGD12', 'SkiLiftTowerGD16', 'SkiLiftTowerGD20', 'SkiGondolaLattice25', 'SkiGondolaLattice32',
                'SkiGondolaLattice40', 'SkiGondolaBaseStationBuilding', 'SkiGondolaTopStationBuilding'] },
];
function prefabFile(rel) {
    const dir = path.join(root, rel);
    if (!fs.existsSync(dir)) return null;
    const base = path.basename(rel);
    const exact = path.join(dir, base + '.Prefab');
    if (fs.existsSync(exact)) return exact;
    // library layout: <Name>/<Name>_<hash>.Prefab
    const hit = fs.readdirSync(dir).find(f => f.startsWith(base + '_') && f.endsWith('.Prefab'));
    return hit ? path.join(dir, hit) : null;
}
let ropeFiles = 0;
// The still meshes: CurveProperties with m_HangingSwaying false (a swaying rope leaves its carriers in wind), the vanilla geometry.
const stillCid = {};
for (const [k, s] of Object.entries(STILL)) {
    const f = path.join(root, s.name, `${s.name} Mesh.Prefab`);
    if (!fs.existsSync(f)) { fail('ROPELANE', `still rope mesh ${s.name} Mesh not found under ${root}`); continue; }
    const t = fs.readFileSync(f, 'utf8');
    stillCid[k] = fs.readFileSync(f + '.cid', 'utf8').trim().toLowerCase();
    if (!/Game\.Prefabs\.CurveProperties/.test(t)) fail('ROPELANE', `${s.name} Mesh: no CurveProperties (ROPE-LANE INVARIANT: the rope draws as a rigid rod)`);
    if (!/"m_HangingSwaying":\s*false/.test(t)) fail('ROPELANE', `${s.name} Mesh: m_HangingSwaying is not false (the rope sways away from its carriers in wind)`);
    if (!t.includes(`CID:${s.geometry}`)) fail('ROPELANE', `${s.name} Mesh: geometry is not the vanilla cable ${s.geometry}`);
}
const laneMeshOk = (laneText, k, lane) => {
    if (!stillCid[k] || !laneText.includes(`CID:${stillCid[k]}`))
        fail('ROPELANE', `${lane}: mesh is not the still ${STILL[k].name} Mesh (${stillCid[k] || '?'}): every lift rope lane draws a still rope mesh`);
};
// The tram's two ropes: own lanes, the type's sag, the still meshes (track = HV, haul = LV).
for (const [lane, k] of [['SkiTramTrackRope', 'hv'], ['SkiTramHaulRope', 'lv']]) {
    const f = prefabFile(lane);
    if (!f) { fail('ROPELANE', `tram: lane prefab ${lane} not found under ${root}`); continue; }
    const t = fs.readFileSync(f, 'utf8');
    const hang = t.match(/"m_Hanging":\s*([0-9.]+)/);
    if (!hang || Math.abs(Number(hang[1]) - 0.04) > 1e-9) fail('ROPELANE', `${lane}: m_Hanging ${hang ? hang[1] : '?'} != the tram's 0.04 (rope sag round)`);
    laneMeshOk(t, k, lane);
}
for (const g of groups) {
    const laneFile = prefabFile(g.lane);
    if (!laneFile) { fail('ROPELANE', `${g.type}: lane prefab ${g.lane} not found under ${root}`); continue; }
    const laneText = fs.readFileSync(laneFile, 'utf8');
    const cid = fs.readFileSync(laneFile + '.cid', 'utf8').trim().toLowerCase();
    const hang = laneText.match(/"m_Hanging":\s*([0-9.]+)/);
    if (!hang || Math.abs(Number(hang[1]) - g.sag) > 1e-9) fail('ROPELANE', `${g.lane}: m_Hanging ${hang ? hang[1] : '?'} != the type's ${g.sag} (rope sag round, docs research rope-sag-2026-11-03)`);
    laneMeshOk(laneText, 'lv', g.lane);
    for (const rel of g.prefabs) {
        const f = prefabFile(rel);
        if (!f) { fail('ROPELANE', `${g.type}: ${rel} not found`); continue; }
        ropeFiles++;
        const text = fs.readFileSync(f, 'utf8');
        const refs = [...text.matchAll(/"m_Lane(?:Prefab)?":\s*\$fstrref:"(CID|UnityGUID):([0-9a-fA-F]{32})"/g)].map(m => m[2].toLowerCase());
        const rope = refs.filter(r => r !== LV_WIRE);
        if (rope.length < 2) fail('ROPELANE', `${rel}: ${rope.length} haul-rope lane reference(s), expected the pair`);
        for (const r of rope)
            if (r !== cid) fail('ROPELANE', `${rel}: rope lane ${r}${r === VANILLA_HV_LANE ? ' (the vanilla HV lane, sag 0.03)' : ''} is not ${g.lane} (${cid}): FindAnchor matches by lane prefab`);
    }
}

// ---- BONEROOT -----------------------------------------------------------------------------------------------------------------
// Every owned skinned mesh (a RenderPrefab with ProceduralAnimationProperties) has an identity ROOT bone: rotation (0, 0, 0, 1),
// scale (1, 1, 1), as every vanilla one (IndustrialACMachineRoof01 Mesh). The Editor import of our Blender FBX gives the root the
// armature's scene conversion (-90 deg about X, scale 100) while the bind poses assume identity, and the game composes the root into
// every skin matrix (ProceduralSkeletonSystem.GetSkinMatrices, ProceduralSkeletonSystem.cs:259-273): the mesh draws turned and 100x.
// The surgery sets the root to identity after each import (a re-import writes it back). Bug class: rig probe AlignRigProbeA, 2026-11-02.
const NUM = '(-?[0-9.]+(?:E[-+]?[0-9]+)?)';
// "$type": "6|UnityEngine.Quaternion, UnityEngine.CoreModule" (a comma inside the string) or a back-reference number
const TYPE = '"\\$type":\\s*(?:"[^"]*"|\\d+),\\s*';
const boneRe = new RegExp('"name":\\s*"([^"]+)",\\s*"position":\\s*\\{[^}]*\\},\\s*"rotation":\\s*\\{\\s*' + TYPE +
    `${NUM},\\s*${NUM},\\s*${NUM},\\s*${NUM}\\s*\\},\\s*"scale":\\s*\\{\\s*${TYPE}${NUM},\\s*${NUM},\\s*${NUM}\\s*\\}` +
    '[\\s\\S]*?"parentId":\\s*(-?\\d+)', 'gi');
function* ownedPrefabs(dir, top) {
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
        if (top && !(e.isDirectory() && (e.name.startsWith('Ski') || e.name === LIB))) continue;
        const p = path.join(dir, e.name);
        if (e.isDirectory()) yield* ownedPrefabs(p, false);
        else if (e.name.endsWith('.Prefab')) yield p;
    }
}
let skinned = 0;
for (const f of ownedPrefabs(root, true)) {
    const text = fs.readFileSync(f, 'utf8');
    if (!text.includes('Game.Prefabs.ProceduralAnimationProperties')) continue;
    skinned++;
    for (const m of text.matchAll(boneRe)) {
        if (Number(m[9]) !== -1) continue;
        const [qx, qy, qz, qw, sx, sy, sz] = m.slice(2, 9).map(Number);
        if (Math.abs(qx) > 1e-4 || Math.abs(qy) > 1e-4 || Math.abs(qz) > 1e-4 || Math.abs(qw - 1) > 1e-4 ||
            Math.abs(sx - 1) > 1e-4 || Math.abs(sy - 1) > 1e-4 || Math.abs(sz - 1) > 1e-4)
            fail('BONEROOT', `${path.relative(root, f)}: root bone ${m[1]} rotation (${qx}, ${qy}, ${qz}, ${qw}) scale (${sx}, ${sy}, ${sz}); ` +
                 'set it to (0, 0, 0, 1) and (1, 1, 1) after the import (the bind poses assume identity)');
    }
}

// ---- CARRIERPASS ---------------------------------------------------------------------------------------------------------------
// The carriers are passable (GeometryFlags.Marker on their prefabs, an empty collision mask) for the whole lift-line tool session, not
// only while a station is placed: a section drawn to an existing tower gives that tower a Temp copy, validated against the permanent
// chairs of the neighbor section, and OverlapExisting refused the redo of a bulldozed middle section (field/2026-11-02/lifticon,
// DECISIONS 582). LiftLineToolSystem: OnStartRunning sets them passable, OnStopRunning hands back to the station window, and
// SetStationPlacing leaves them alone while the tool runs.
function methodBody(code, name) {
    const at = code.search(new RegExp(`\\b${name}\\s*\\([^)]*\\)\\s*\\{`));
    if (at < 0) return null;
    let i = code.indexOf('{', at), depth = 0;
    for (let j = i; j < code.length; j++) {
        if (code[j] === '{') depth++;
        else if (code[j] === '}' && --depth === 0) return code.slice(i, j + 1);
    }
    return null;
}
const toolFile = path.join(systems, 'LiftLineToolSystem.cs');
const toolCode = fs.existsSync(toolFile) ? stripComments(fs.readFileSync(toolFile, 'utf8')) : '';
const passRules = [
    ['OnStartRunning', /SetCarriersPassable\(\s*true\s*\)/, 'sets the carriers passable for the tool session'],
    ['OnStopRunning', /SetCarriersPassable\(\s*m_StationPlacing\s*\)/, 'hands the carriers back to the station window (SetCarriersPassable(m_StationPlacing))'],
    ['SetStationPlacing', /if\s*\(\s*!IsActive\s*\)\s*\{[^}]*SetCarriersPassable\(\s*placing\s*\)/, 'changes the carriers only while the tool is not running'],
    // The grips of a split carrier are separate objects, not carriers: they need the flag too (bridge 2026-11-03, F1: a section
    // delete next to riding chairs was refused, OverlapExisting on SkiLiftChair4Grip / SkiTramCarriage).
    ['SetCarriersPassable', /GripTypes\.Keys/, 'marks the grips of split carriers (m_Catalog.GripTypes) as well as the carriers'],
];
for (const [name, re, why] of passRules) {
    const body = methodBody(toolCode, name);
    if (!body) fail('CARRIERPASS', `LiftLineToolSystem.${name} not found`);
    else if (!re.test(body)) fail('CARRIERPASS', `LiftLineToolSystem.${name} no longer ${why}: a section redo at an existing tower is refused (OverlapExisting on the neighbor's chairs)`);
}

// ---- MANIFESTSHA ----------------------------------------------------------------------------------------------------------------
const shaRes = manifestShaCheck(path.join(repo, 'assets', 'import'), root);
for (const m of shaRes.problems) fail('MANIFESTSHA', m);
if (shaRes.checked === 0 && fs.existsSync(path.join(repo, 'assets', 'import', 'stations')))
    fail('MANIFESTSHA', 'no re-frozen station manifest under assets/import: the FG M manifests (prefab_contract.refrozen) are the pinned baselines');

if (failures.length) {
    for (const l of failures) console.log(l);
    process.exit(failures.length);
}
console.log(`check-lift-align: OK (PINDROP ${pinScanned} systems, ROPELANE ${ropeFiles} prefabs in ${groups.length} lift types, ` +
            `BONEROOT ${skinned} skinned meshes under ${path.relative(repo, root) || root}, CARRIERPASS ${passRules.length} tool hooks, ` +
            `MANIFESTSHA ${shaRes.checked} re-frozen manifests pinned, ${shaRes.snapshots} import-time snapshots skipped)`);
