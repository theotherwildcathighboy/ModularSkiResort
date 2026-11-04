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
//   LADDER    the tower placeholders' members: tops unique by >= 0.1 m, a twin head's E within the line's m_ElevationRange.max, a twin's
//             rope anchors = its support member's, membership, the twins imported together (none = pending). Bug class: two members
//             0.05 m apart read back as each other (MemberOf) and the engine spawns either; a twin whose lanes moved lifts the rope.
//
// Usage: node tools/check-lift-align.js [--root <ImportedData dir>] [--self-test]   (default root: gamedata-mirror/ImportedData;
//        preflight passes the game's). --self-test plants faults in the LADDER model (duplicate top, E past range, offset anchor,
//        missing membership, partial import, unknown member) and fails unless each is caught.
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

// ---- LADDER --------------------------------------------------------------------------------------------------------------------
// The tower placeholders' members (breakover-holddown-plan-2026-11-03 section 3.4; PIPELINE idiom "A tower head variant is a twin
// MEMBER"). The engine picks a placeholder member by its top alone (SubObjectSystem.cs:2189-2200: spare = top - (E + spec y)), and
// LiftTowerEdit.MemberOf reads a node's (member, head) back from its elevation at 0.05 m, so:
//   - every member top in a placeholder is unique by >= 0.1 m (the ladder, the twins, the GD lattice towers);
//   - a twin's E = its top - ladder[0] top stays within the line prefab's m_ElevationRange.max (beyond it a later vanilla edit is
//     unverified);
//   - a twin's rope anchors (ObjectSubLanes: lane prefab and curve) equal its support member's within 1 mm (the rope must not move
//     when the head changes; FindAnchor matches by lane prefab, LaneSystem.cs:4049);
//   - a twin is a member of its type's placeholder; the twins arrive together (none = pending import, OK; some = FAIL).
// Tops and twins come from LiftTowerPlanner.cs (the ladder and twin tables the planner uses); members and anchors from the root.
const LADDER_TYPES = [
    { ladder: 'FixedGripLadder', twins: 'FixedGripTwins', placeholder: 'SkiLiftTowerPlaceholder', line: 'SkiChairliftHV' },
    { ladder: 'DetachableLadder', twins: 'DetachableTwins', placeholder: 'SkiLiftTowerDQPlaceholder', line: 'SkiDetachableHV' },
    // The gondola placeholder also holds the lattice towers (not planner members): tops from lift_tower.py GD_LATTICE (25 / 32 / 40).
    { ladder: 'GondolaLadder', twins: 'GondolaTwins', placeholder: 'SkiGondolaTowerPlaceholder', line: 'SkiGondolaHV',
      extra: { SkiGondolaLattice25: 25, SkiGondolaLattice32: 32, SkiGondolaLattice40: 40 } },
];
const LADDER_MIN_GAP_M = 0.1, ANCHOR_TOL_M = 0.001;

function csTable(src, field, re) {
    const at = src.indexOf(` ${field} =`);
    if (at < 0) return null;
    const end = src.indexOf('};', at);
    return [...src.slice(at, end).matchAll(re)];
}
/** Sub-lane anchors of a prefab text: [{lane, pts: [12 numbers]}], sorted. */
function anchorsOf(text) {
    const out = [];
    for (const m of text.matchAll(/"m_LanePrefab":\s*\$fstrref:"(?:CID|UnityGUID):([0-9a-fA-F]{32})"[\s\S]*?"m_BezierCurve":\s*\{([\s\S]*?)"m_NodeIndex"/g)) {
        const pts = [...m[2].matchAll(/"[xyz]":\s*(-?[0-9.]+(?:E[-+]?[0-9]+)?)/gi)].map(x => Number(x[1]));
        out.push({ lane: m[1].toLowerCase(), pts });
    }
    return out.sort((a, b) => (a.lane + a.pts.join()).localeCompare(b.lane + b.pts.join()));
}
function placeholdersOf(text) {
    const m = text.match(/"m_Placeholders":\s*\{[\s\S]*?"\$rcontent":\s*\[([\s\S]*?)\]/);
    return m ? [...m[1].matchAll(/CID:([0-9a-fA-F]{32})/g)].map(x => x[1].toLowerCase()) : [];
}

/** The model the LADDER rules run on (loaded from the root and the C# tables; --self-test plants faults in a copy). */
function loadLadderModel() {
    const src = fs.readFileSync(path.join(systems, 'LiftTowerPlanner.cs'), 'utf8');
    const prefabs = {};   // name -> { placeholders: [cid], anchors }
    for (const f of ownedPrefabs(root, true)) {
        const text = fs.readFileSync(f, 'utf8');
        if (!/"m_Placeholders"/.test(text)) continue;
        const name = path.basename(f, '.Prefab').replace(/_[0-9a-f]{32}$/, '');
        prefabs[name] = { placeholders: placeholdersOf(text), anchors: anchorsOf(text) };
    }
    const types = [];
    for (const t of LADDER_TYPES) {
        const ladder = (csTable(src, t.ladder, /new LiftTowerMember\("([^"]+)",\s*([0-9.]+)f\)/g) || []).map(m => ({ name: m[1], top: Number(m[2]) }));
        const twins = (csTable(src, t.twins, /new LiftTowerTwin\((\d+),\s*LiftTowerHead\.(\w+),\s*"([^"]+)",\s*([0-9.]+)f\)/g) || [])
            .map(m => ({ member: Number(m[1]), head: m[2], name: m[3], top: Number(m[4]) }));
        const phFile = prefabFile(t.placeholder);
        const lineFile = prefabFile(t.line);
        const range = lineFile && fs.readFileSync(lineFile, 'utf8').match(/"m_ElevationRange":\s*\{[^}]*"max":\s*(-?[0-9.]+)/);
        types.push({ placeholder: t.placeholder, ladderField: t.ladder, ladder, twins, extra: t.extra || {},
                     cid: phFile ? fs.readFileSync(phFile + '.cid', 'utf8').trim().toLowerCase() : null,
                     elevMax: range ? Number(range[1]) : NaN });
    }
    return { types, prefabs };
}

/** The LADDER rules on a model: returns the failure messages and a summary. */
function ladderRules(model) {
    const out = [];
    let pending = 0, present = 0, compared = 0;
    for (const t of model.types) {
        const P = t.placeholder;
        if (!t.cid) { out.push(`${P}: placeholder prefab not found under the root`); continue; }
        if (!t.ladder.length) { out.push(`${P}: no ${t.ladderField} table in LiftTowerPlanner.cs`); continue; }
        if (!Number.isFinite(t.elevMax)) out.push(`${P}: the line prefab's m_ElevationRange.max not found`);
        const tops = new Map();   // name -> top
        for (const m of t.ladder) tops.set(m.name, m.top);
        for (const tw of t.twins) tops.set(tw.name, tw.top);
        for (const [n, top] of Object.entries(t.extra)) tops.set(n, top);
        const members = Object.keys(model.prefabs).filter(n => model.prefabs[n].placeholders.includes(t.cid));
        for (const m of t.ladder)
            if (!members.includes(m.name)) out.push(`${P}: ladder member ${m.name} is not a member of the placeholder`);
        for (const n of members)
            if (!tops.has(n)) out.push(`${P}: member ${n} has no known top (not in ${t.ladderField} / the twin table / the lattice list): ` +
                                      'the engine picks members by top, so every member top must be known and unique');
        // unique tops >= 0.1 m over every member the engine can see (the mirror's) and every twin the planner may write
        const all = [...new Set([...members.filter(n => tops.has(n)), ...t.twins.map(x => x.name)])].map(n => [n, tops.get(n)])
            .sort((a, b) => a[1] - b[1]);
        for (let i = 1; i < all.length; i++)
            if (all[i][1] - all[i - 1][1] < LADDER_MIN_GAP_M - 1e-6)
                out.push(`${P}: tops of ${all[i - 1][0]} (${all[i - 1][1]}) and ${all[i][0]} (${all[i][1]}) are ${(all[i][1] - all[i - 1][1]).toFixed(3)} m apart ` +
                         `(< ${LADDER_MIN_GAP_M}): MemberOf (0.05 m) and the engine's score cannot tell them apart`);
        const top0 = t.ladder[0].top;
        const here = t.twins.filter(tw => model.prefabs[tw.name]);
        for (const tw of t.twins) {
            const support = t.ladder[tw.member];
            if (!support) { out.push(`${P}: twin ${tw.name} names ladder member ${tw.member}, which does not exist`); continue; }
            const e = tw.top - top0;
            if (Number.isFinite(t.elevMax) && e > t.elevMax + 1e-6)
                out.push(`${P}: twin ${tw.name} E ${e.toFixed(4)} is past the line's m_ElevationRange.max ${t.elevMax}`);
            if (!(tw.top > support.top)) out.push(`${P}: twin ${tw.name} top ${tw.top} is not above its member ${support.name} (${support.top})`);
            const p = model.prefabs[tw.name];
            if (!p) continue;
            if (!p.placeholders.includes(t.cid)) out.push(`${P}: twin ${tw.name} is not a member of the placeholder (SpawnableObject m_Placeholders)`);
            const ms = model.prefabs[support.name];
            if (!ms) { out.push(`${P}: twin ${tw.name}: its member ${support.name} not found`); continue; }
            compared++;
            const a = p.anchors, b = ms.anchors;
            let same = a.length === b.length && a.length > 0;
            for (let i = 0; same && i < a.length; i++)
                same = a[i].lane === b[i].lane && a[i].pts.length === b[i].pts.length && a[i].pts.every((v, k) => Math.abs(v - b[i].pts[k]) <= ANCHOR_TOL_M);
            if (!same) out.push(`${P}: twin ${tw.name} anchors (ObjectSubLanes lane + curve) differ from ${support.name}'s: the rope moves when the head changes`);
        }
        if (here.length === 0) pending += t.twins.length;
        else if (here.length < t.twins.length)
            out.push(`${P}: ${here.length} of ${t.twins.length} twins imported (missing ${t.twins.filter(tw => !model.prefabs[tw.name]).map(x => x.name).join(', ')}): ` +
                     'the twins arrive together');
        present += here.length;
    }
    return { out, summary: `LADDER ${model.types.length} placeholders, ${present} twins present (${compared} anchor sets compared), ${pending} pending import` };
}

function ladderSelfTest() {
    const base = loadLadderModel();
    const clone = () => JSON.parse(JSON.stringify(base));
    // a model with every twin imported as a faithful copy of its member (the surgery's target)
    const imported = () => {
        const m = clone();
        for (const t of m.types)
            for (const tw of t.twins) {
                const support = m.prefabs[t.ladder[tw.member].name];
                m.prefabs[tw.name] = { placeholders: [t.cid], anchors: JSON.parse(JSON.stringify(support ? support.anchors : [])) };
            }
        return m;
    };
    const cases = [
        ['clean, pending import', clone, 0],
        ['clean, every twin imported', imported, 0],
        ['duplicate top (a twin 0.05 m over another member)', () => { const m = clone(); const t = m.types[1]; t.twins[0].top = t.twins[1].top + 0.05; return m; }, 1],
        ['E past m_ElevationRange.max', () => { const m = clone(); const t = m.types[1]; t.twins[2].top = t.ladder[0].top + t.elevMax + 0.5; return m; }, 1],
        ['offset anchor (5 mm)', () => { const m = imported(); const p = m.prefabs[m.types[0].twins[0].name]; p.anchors[0].pts[1] += 0.005; return m; }, 1],
        ['missing placeholder membership', () => { const m = imported(); m.prefabs[m.types[2].twins[1].name].placeholders = []; return m; }, 1],
        ['a partial import', () => { const m = clone(); const t = m.types[0]; const s = m.prefabs[t.ladder[0].name];
            m.prefabs[t.twins[0].name] = { placeholders: [t.cid], anchors: s.anchors }; return m; }, 1],
        ['an unknown member', () => { const m = clone(); m.prefabs.SkiLiftTowerDQ10Stray = { placeholders: [m.types[1].cid], anchors: [] }; return m; }, 1],
    ];
    let bad = 0;
    for (const [label, make, wantFail] of cases) {
        const r = ladderRules(make());
        const ok = wantFail ? r.out.length >= 1 : r.out.length === 0;
        console.log(`${ok ? 'PASS' : 'FAIL'}  LADDER self-test: ${label}: ${r.out.length} failure(s)${r.out.length ? ' (' + r.out[0] + ')' : ''}`);
        if (!ok) bad++;
    }
    console.log(bad ? `check-lift-align --self-test: ${bad} case(s) FAILED` : `check-lift-align --self-test: OK (${cases.length} cases, every planted fault caught)`);
    process.exit(bad);
}

if (process.argv.includes('--self-test'))
    ladderSelfTest();
const ladder = ladderRules(loadLadderModel());
for (const m of ladder.out) fail('LADDER', m);

if (failures.length) {
    for (const l of failures) console.log(l);
    process.exit(failures.length);
}
console.log(`check-lift-align: OK (PINDROP ${pinScanned} systems, ROPELANE ${ropeFiles} prefabs in ${groups.length} lift types, ` +
            `BONEROOT ${skinned} skinned meshes under ${path.relative(repo, root) || root}, CARRIERPASS ${passRules.length} tool hooks, ${ladder.summary})`);
