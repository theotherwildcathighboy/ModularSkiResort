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
// Usage: node tools/check-lift-align.js [--root <ImportedData dir>]   (default: gamedata-mirror/ImportedData; preflight passes the game's)
// Exit code = number of failures. Last line: "check-lift-align: OK ..." on success.

'use strict';
const fs = require('fs');
const path = require('path');

const repo = path.resolve(__dirname, '..');
const argRoot = process.argv.indexOf('--root');
const root = argRoot > 0 ? path.resolve(process.argv[argRoot + 1]) : path.join(repo, 'gamedata-mirror', 'ImportedData');
const systems = path.join(repo, 'mod', 'SkiResortMod', 'Systems');

const failures = [];
const fail = (code, msg) => failures.push(`${code} ${msg}`);

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

if (failures.length) {
    for (const l of failures) console.log(l);
    process.exit(failures.length);
}
console.log(`check-lift-align: OK (PINDROP ${pinScanned} systems, ROPELANE ${ropeFiles} prefabs in ${groups.length} lift types, ` +
            `BONEROOT ${skinned} skinned meshes under ${path.relative(repo, root) || root}, CARRIERPASS ${passRules.length} tool hooks)`);
