#!/usr/bin/env node
// ARCHSWAP (T256 F1, P1 field test 2026-11-20): a released object prefab whose mesh rows now point to meshes that need more
// archetype components than its meshes did at a release tag.
//
// Why: the game builds an object's archetype from its prefab's meshes when the object is CREATED
// (Game.Prefabs/ObjectGeometryPrefab.cs:63-179, decompile 1.6.4f1): ColorProperties -> MeshColor + CustomMeshColor,
// StackProperties -> Stack, ProceduralAnimationProperties -> Skeleton + Bone + BoneHistory (+ Momentum for the look-at /
// rotation bone types, + PlaybackLayer for the playback layers), EmissiveProperties -> Emissive + LightState, an animated
// CharacterProperties -> Animated. A loaded entity keeps the archetype of its save. Vanilla's RequiredComponentSystem adds
// these only in narrow cases (Emissive + LightState for traffic lights and cars, :525-533, :1246-1249; BoneHistory to entities
// that have Bone, :494, :1187-1190; PlaybackLayer to skeletons with clips, :745, :1763-1780; MeshColor only for trees / plants /
// humans from old versions). So a mesh swap on a released building (release-update-constraints change-table row 3a) that
// adds such a component draws, but the feature never runs in a city saved before the swap: P1 measured 4 / 4 fixed-grip
// stations without a Skeleton, so their new bullwheels never turned.
// Covered: Skeleton + Bone + BoneHistory on a prefab listed in BoneRetrofitSystem.RetrofitPrefabs (type + name; the system adds
// them once per load), when no bone needs Momentum or PlaybackLayer; Emissive on a CarPrefab (vanilla, above).
// Also checked (critic-f1 change 4): the retrofit's one ComponentTypeSet is exactly Skeleton + Bone + BoneHistory (a dropped
// BoneHistory would crash at the first sizing, vanilla's Bone fix has already run), and every listed prefab is a released one.
// Reads: tools/shipped-prefabs.json (the released prefabs), the mirror (or --root) for now, and the mirror at every release tag
// v<major>.<minor>.<patch> (git). Vanilla meshes (CIDs not in our mirror) are not judged.
// Exit 0 = PASS, 1 = FAIL. --self-test runs planted cases.
'use strict';
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const REPO = path.resolve(__dirname, '..');
const MIRROR_REL = 'gamedata-mirror/ImportedData';
const MESH_DIR = 'c030ee3ff256df91';
// BoneType (Game.Prefabs/BoneType.cs, 1.6.4f1, enum order): the types that add Momentum, and the playback layers
const MOMENTUM_BONES = new Set([1, 2, 3, 6, 7, 18, 19, 23, 24, 25, 27, 28, 29, 30, 33]);
const PLAYBACK_BONES = new Set([35, 36, 37, 38, 39, 40, 41, 42]);
const COMPONENTS = {
    Color: 'MeshColor + CustomMeshColor', Stack: 'Stack', Skeleton: 'Skeleton + Bone + BoneHistory', Momentum: 'Momentum',
    Playback: 'PlaybackLayer', Emissive: 'Emissive + LightState', Animated: 'Animated',
};

function decl(text, type) {
    return new RegExp(`"\\d+\\|Game\\.Prefabs\\.${type.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}, Game"`).test(text);
}

/** The archetype flags one mesh prefab's text asks for (ObjectGeometryPrefab.cs:88-141). */
function meshFlags(text) {
    const f = new Set();
    if (decl(text, 'ColorProperties')) f.add('Color');
    if (decl(text, 'StackProperties')) f.add('Stack');
    if (decl(text, 'EmissiveProperties')) f.add('Emissive');
    if (decl(text, 'CharacterProperties') && /"m_AnimatedPropName":\s*"[^"]+"/.test(text)) f.add('Animated');
    const m = /"\d+\|Game\.Prefabs\.ProceduralAnimationProperties, Game"/.exec(text);
    if (m) {
        f.add('Skeleton');
        // the PAP block runs to the next declared Game.Prefabs type that is not one of its own nested types
        const rest = text.slice(m.index + m[0].length);
        const next = /"\d+\|Game\.Prefabs\.(?!ProceduralAnimationProperties)/.exec(rest);
        const block = next ? rest.slice(0, next.index) : rest;
        for (const t of block.matchAll(/"m_Type":\s*(\d+)/g)) {
            const v = Number(t[1]);
            if (MOMENTUM_BONES.has(v)) f.add('Momentum');
            if (PLAYBACK_BONES.has(v)) f.add('Playback');
        }
    }
    return f;
}

/** The mesh CIDs of an object prefab's ObjectMeshInfo rows, or null when the prefab has none (lanes, nets, ...). */
function meshCids(text) {
    if (!decl(text, 'ObjectMeshInfo[]')) return null;
    return [...text.matchAll(/"m_Mesh":\s*\$fstrref:"CID:([0-9a-f]{32})"/g)].map(x => x[1]);
}

/** Union of the flags of a prefab's meshes; `meshes` maps CID -> {name, text}. Unknown (vanilla) CIDs are counted. */
function prefabFlags(text, meshes) {
    const cids = meshCids(text);
    if (cids === null) return null;
    const flags = new Set(), names = [];
    let vanilla = 0;
    for (const c of cids) {
        const m = meshes.get(c);
        if (!m) { vanilla++; continue; }
        names.push(m.name);
        for (const x of meshFlags(m.text)) flags.add(x);
    }
    return { flags, names, vanilla };
}

/** One finding per prefab and release: the flags its meshes ask for now that they did not ask for then. */
function judge(prefab, now, then, tag, retrofit) {
    const added = [...now.flags].filter(x => !then.flags.has(x));
    if (!added.length) return null;
    const covered = [], open = [];
    for (const a of added) {
        if (a === 'Skeleton' && retrofit && retrofit.present && retrofit.names.get(prefab.name) === prefab.type)
            covered.push(`${a} (BoneRetrofitSystem)`);
        else if (a === 'Emissive' && prefab.type === 'CarPrefab') covered.push(`${a} (vanilla RequiredComponentSystem, cars)`);
        else open.push(a);
    }
    return { prefab: prefab.name, tag, covered, open, meshes: now.names };
}

function git(args, input) {
    return execFileSync('git', args, { cwd: REPO, input, maxBuffer: 1 << 30 });
}

/** {path -> sha} of the mirror at `tag`. */
function treeAt(tag) {
    const out = git(['ls-tree', '-r', '--full-tree', tag, '--', MIRROR_REL]).toString('utf8');
    const map = new Map();
    for (const line of out.split('\n')) {
        const tab = line.indexOf('\t');
        if (tab < 0) continue;
        const [, type, sha] = line.slice(0, tab).split(' ');
        if (type === 'blob') map.set(line.slice(tab + 1).replace(/^"|"$/g, ''), sha);
    }
    return map;
}

/** Contents of many blobs in one git process. */
function readBlobs(shas) {
    const uniq = [...new Set(shas)];
    const res = new Map();
    if (!uniq.length) return res;
    const buf = git(['cat-file', '--batch'], uniq.join('\n') + '\n');
    let pos = 0;
    while (pos < buf.length) {
        const nl = buf.indexOf(10, pos);
        const head = buf.slice(pos, nl).toString('utf8').split(' ');
        const size = Number(head[2]);
        res.set(head[0], buf.slice(nl + 1, nl + 1 + size).toString('utf8'));
        pos = nl + 1 + size + 1;
    }
    return res;
}

/** The mesh index (CID -> {name, text}) and the prefab texts at `tag`, for the prefabs in `files`. */
function sourceAtTag(tag, files) {
    const tree = treeAt(tag);
    const meshPrefix = `${MIRROR_REL}/${MESH_DIR}/`;
    const cidPaths = [...tree.keys()].filter(p => p.startsWith(meshPrefix) && p.endsWith('.Prefab.cid'));
    const cidText = readBlobs(cidPaths.map(p => tree.get(p)));
    const byCid = new Map();
    for (const p of cidPaths) {
        const cid = (cidText.get(tree.get(p)) || '').trim().toLowerCase();
        const pf = p.slice(0, -4);
        if (/^[0-9a-f]{32}$/.test(cid) && tree.has(pf)) byCid.set(cid, pf);
    }
    const prefabShas = new Map();
    for (const f of files) { const s = tree.get(`${MIRROR_REL}/${f}`); if (s) prefabShas.set(f, s); }
    const prefabText = readBlobs([...prefabShas.values()]);
    const prefabs = new Map([...prefabShas].map(([f, s]) => [f, prefabText.get(s)]));
    const wanted = new Set();
    for (const t of prefabs.values()) for (const c of (meshCids(t) || [])) if (byCid.has(c)) wanted.add(c);
    const meshText = readBlobs([...wanted].map(c => tree.get(byCid.get(c))));
    const meshes = new Map([...wanted].map(c => [c, { name: path.basename(byCid.get(c), '.Prefab'), text: meshText.get(tree.get(byCid.get(c))) }]));
    return { prefabs, meshes };
}

function sourceNow(root, files) {
    const meshes = new Map();
    const md = path.join(root, MESH_DIR);
    const prefabs = new Map();
    for (const f of files) { const p = path.join(root, f); if (fs.existsSync(p)) prefabs.set(f, fs.readFileSync(p, 'utf8')); }
    const wanted = new Set();
    for (const t of prefabs.values()) for (const c of (meshCids(t) || [])) wanted.add(c);
    if (fs.existsSync(md)) {
        for (const d of fs.readdirSync(md, { withFileTypes: true })) {
            if (!d.isDirectory()) continue;
            for (const f of fs.readdirSync(path.join(md, d.name))) {
                if (!f.endsWith('.Prefab.cid')) continue;
                const cid = fs.readFileSync(path.join(md, d.name, f), 'utf8').trim().toLowerCase();
                if (!wanted.has(cid)) continue;
                const pf = path.join(md, d.name, f.slice(0, -4));
                if (fs.existsSync(pf)) meshes.set(cid, { name: path.basename(pf, '.Prefab'), text: fs.readFileSync(pf, 'utf8') });
            }
        }
    }
    return { prefabs, meshes };
}

/** The retrofit as the source declares it: registered in Mod.cs, its prefab list and its component set. */
function parseRetrofit(sysText, modText) {
    const r = { present: false, names: new Map(), set: [], problems: [] };
    if (sysText == null || modText == null || !/UpdateAfter<BoneRetrofitSystem,/.test(modText)) return r;
    r.present = true;
    const list = /RetrofitPrefabs\s*=\s*\{([\s\S]*?)\};/.exec(sysText);
    if (!list) r.problems.push('BoneRetrofitSystem.RetrofitPrefabs not found');
    else for (const m of list[1].matchAll(/\(nameof\((\w+)\),\s*"(\w+)"\)/g)) r.names.set(m[2], m[1]);
    const sets = [...sysText.matchAll(/new ComponentTypeSet\(([\s\S]*?)\)\);/g)];
    if (sets.length !== 1) r.problems.push(`BoneRetrofitSystem has ${sets.length} ComponentTypeSet(s), want exactly one structural call`);
    else {
        r.set = [...sets[0][1].matchAll(/ComponentType\.\w+<(\w+)>\(\)/g)].map(x => x[1]);
        const want = ['Bone', 'BoneHistory', 'Skeleton'];
        if ([...r.set].sort().join(',') !== want.join(','))
            r.problems.push(`the retrofit adds [${r.set.join(', ')}], want exactly Skeleton + Bone + BoneHistory`);
    }
    return r;
}

function retrofitNow() {
    const rd = p => (fs.existsSync(p) ? fs.readFileSync(p, 'utf8') : null);
    return parseRetrofit(rd(path.join(REPO, 'mod/SkiResortMod/Systems/BoneRetrofitSystem.cs')), rd(path.join(REPO, 'mod/SkiResortMod/Mod.cs')));
}

function run(root) {
    const shipped = JSON.parse(fs.readFileSync(path.join(REPO, 'tools/shipped-prefabs.json'), 'utf8')).prefabs;
    const files = shipped.map(p => p.file);
    let tags;
    try {
        tags = git(['tag', '--list', 'v*']).toString('utf8').split(/\r?\n/).filter(t => /^v\d+\.\d+\.\d+$/.test(t));
    } catch (e) { tags = []; }
    if (!tags.length) { console.log('check-archetype-swap: SKIP (no release tags in this clone)'); return 0; }
    const retrofit = retrofitNow();
    const problems = [...retrofit.problems];
    for (const [name, type] of retrofit.names)
        if (!shipped.some(p => p.name === name && p.type === type)) problems.push(`RetrofitPrefabs lists ${type} ${name}, not a released prefab`);
    for (const p of problems) console.log(`FAIL  ${p}`);
    const now = sourceNow(root, files);
    const fails = [], covered = [];
    let judged = 0;
    for (const tag of tags) {
        const then = sourceAtTag(tag, files);
        for (const p of shipped) {
            const tn = now.prefabs.get(p.file), tt = then.prefabs.get(p.file);
            if (!tn || !tt) continue;
            const fn = prefabFlags(tn, now.meshes), ft = prefabFlags(tt, then.meshes);
            if (!fn || !ft) continue;
            judged++;
            const r = judge(p, fn, ft, tag, retrofit);
            if (!r) continue;
            if (r.open.length) fails.push(r);
            if (r.covered.length) covered.push(r);
        }
    }
    const once = new Map();
    for (const r of fails) {   // one line per prefab: the oldest release whose saves miss the components
        const k = `${r.prefab}|${r.open.join(',')}`;
        if (!once.has(k)) once.set(k, r);
    }
    for (const r of once.values()) {
        console.log(`FAIL  ${r.prefab}: its meshes (${r.meshes.join(', ')}) need ${r.open.map(x => COMPONENTS[x]).join(', ')}, which ` +
            `objects saved by ${r.tag} never get (ObjectGeometryPrefab.cs:88-179): retrofit them at load (as BoneRetrofitSystem) or keep the old mesh`);
    }
    const cov = new Set(covered.map(r => `${r.prefab} ${r.covered.join(', ')}`));
    const summary = `${shipped.length} released prefabs, ${tags.length} release tags (${tags.join(', ')}), ${judged} prefab x tag pairs with object meshes, ` +
        `${cov.size} covered swap(s)${cov.size ? ` [${[...cov].join('; ')}]` : ''}, retrofit ${retrofit.present ? `present (${retrofit.names.size} prefab(s), adds ${retrofit.set.join(' + ')})` : 'ABSENT'}`;
    const n = once.size + problems.length;
    console.log(`check-archetype-swap: ${n ? `FAIL (${n})` : 'PASS'} | ${summary}`);
    return n ? 1 : 0;
}

function selfTest() {
    const mesh = (extra) => `{"$type": "1|Game.Prefabs.RenderPrefab, Game", "components": [${extra}]}`;
    const pap = (types) => `{"$type": "2|Game.Prefabs.ProceduralAnimationProperties, Game", "m_Bones": {"$type": "3|Game.Prefabs.ProceduralAnimationProperties+BoneInfo[], Game", ` +
        types.map((t, i) => `{"$type": ${i ? 4 : '"4|Game.Prefabs.ProceduralAnimationProperties+BoneInfo, Game"'}, "m_Type": ${t}}`).join(', ') + '}}, ' +
        '{"$type": "5|Game.Prefabs.LodProperties, Game", "m_Type": 6}';
    const bldg = (cid) => `{"$type": "1|Game.Prefabs.BuildingPrefab, Game", "m_Meshes": {"$type": "19|Game.Prefabs.ObjectMeshInfo[], Game", "$rcontent": [{"$type": "20|Game.Prefabs.ObjectMeshInfo, Game", "m_Mesh": $fstrref:"CID:${cid}"}]}}`;
    const A = 'a'.repeat(32), B = 'b'.repeat(32), C = 'c'.repeat(32), D = 'd'.repeat(32), E = 'e'.repeat(32);
    const meshes = new Map([
        [A, { name: 'Old', text: mesh('') }],
        [B, { name: 'Bones', text: mesh(pap([0, 0])) }],
        [C, { name: 'Lit', text: mesh('{"$type": "2|Game.Prefabs.EmissiveProperties, Game"}') }],
        [D, { name: 'Spin', text: mesh(pap([0, 6])) }],
        [E, { name: 'Tinted', text: mesh('{"$type": "2|Game.Prefabs.ColorProperties, Game"}') }],
    ]);
    const flagsOf = (cid) => prefabFlags(bldg(cid), meshes);
    const R = { present: true, names: new Map([['SkiX', 'BuildingPrefab'], ['SkiBus', 'CarPrefab']]) }, NOR = { present: false, names: new Map() };
    const cases = [
        ['bones on a Ski building, retrofit present: covered', { name: 'SkiX', type: 'BuildingPrefab' }, B, A, R, r => r && !r.open.length && r.covered.length === 1],
        ['bones without the retrofit: FAIL', { name: 'SkiX', type: 'BuildingPrefab' }, B, A, NOR, r => r && r.open.includes('Skeleton')],
        ['bones on a prefab not in RetrofitPrefabs: FAIL', { name: 'SkiOther', type: 'BuildingPrefab' }, B, A, R, r => r && r.open.includes('Skeleton')],
        ['bones on a listed name of another type: FAIL', { name: 'SkiX', type: 'StaticObjectPrefab' }, B, A, R, r => r && r.open.includes('Skeleton')],
        ['emissive swap on a building: FAIL', { name: 'SkiX', type: 'BuildingPrefab' }, C, A, R, r => r && r.open.includes('Emissive')],
        ['emissive swap on a car: covered', { name: 'SkiBus', type: 'CarPrefab' }, C, A, R, r => r && !r.open.length],
        ['a PoweredRotation bone needs Momentum: FAIL', { name: 'SkiX', type: 'BuildingPrefab' }, D, A, R, r => r && r.open.includes('Momentum')],
        ['a LodProperties m_Type after the bones is not a bone', { name: 'SkiX', type: 'BuildingPrefab' }, B, A, R, r => r && !r.open.includes('Momentum')],
        ['color swap: FAIL', { name: 'SkiX', type: 'BuildingPrefab' }, E, A, R, r => r && r.open.includes('Color')],
        ['no change: no finding', { name: 'SkiX', type: 'BuildingPrefab' }, B, B, R, r => r === null],
        ['a mesh with fewer flags: no finding', { name: 'SkiX', type: 'BuildingPrefab' }, A, B, R, r => r === null],
    ];
    let bad = 0;
    for (const [label, p, now, then, retro, ok] of cases) {
        const r = judge(p, flagsOf(now), flagsOf(then), 'v0.0.0', retro);
        if (!ok(r)) { bad++; console.log(`FAIL  self-test: ${label} (${JSON.stringify(r)})`); }
    }
    if (meshCids('{"$type": "1|Game.Prefabs.NetLaneGeometryPrefab, Game", "m_Mesh": $fstrref:"CID:' + A + '"}') !== null) {
        bad++; console.log('FAIL  self-test: a prefab without ObjectMeshInfo rows is not judged');
    }
    const mod = 'updateSystem.UpdateAfter<BoneRetrofitSystem, X>(P);';
    const sys = (set) => 'RetrofitPrefabs = { (nameof(BuildingPrefab), "SkiA"), (nameof(StaticObjectPrefab), "SkiB"), };\n' +
        `EntityManager.AddComponent(fix.AsArray(), new ComponentTypeSet(${set}));`;
    const RW = (t) => `ComponentType.ReadWrite<${t}>()`;
    const parses = [
        ['the three buffers in one call parse clean', parseRetrofit(sys([RW('Skeleton'), RW('Bone'), RW('BoneHistory')].join(',\n ')), mod),
            r => r.present && !r.problems.length && r.names.get('SkiA') === 'BuildingPrefab' && r.names.get('SkiB') === 'StaticObjectPrefab'],
        ['a set without BoneHistory: FAIL', parseRetrofit(sys([RW('Skeleton'), RW('Bone')].join(', ')), mod), r => r.problems.length === 1],
        ['a fourth component: FAIL', parseRetrofit(sys([RW('Skeleton'), RW('Bone'), RW('BoneHistory'), RW('Momentum')].join(', ')), mod), r => r.problems.length === 1],
        ['not registered in Mod.cs: absent', parseRetrofit(sys(RW('Skeleton')), 'nothing'), r => !r.present],
    ];
    for (const [label, r, ok] of parses) if (!ok(r)) { bad++; console.log(`FAIL  self-test: ${label} (${JSON.stringify({ ...r, names: [...r.names] })})`); }
    console.log(`check-archetype-swap --self-test: ${bad ? `FAIL (${bad})` : `OK (${cases.length + 1 + parses.length} planted cases)`}`);
    return bad ? 1 : 0;
}

const argv = process.argv.slice(2);
if (argv.includes('--self-test')) process.exit(selfTest());
const ri = argv.indexOf('--root');
const root = ri >= 0 ? path.resolve(argv[ri + 1]) : path.join(REPO, MIRROR_REL);
process.exit(run(root));
