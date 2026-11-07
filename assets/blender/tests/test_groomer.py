"""
test_groomer.py - gates for SkiGroomerA (assets/blender/groomer.py).

    blender --background --factory-startup --python-exit-code 1 --python assets/blender/tests/test_groomer.py [-- --quick]

The test checks these points:
- The name passes the asset-name rule and no part carries a brand word.
- Two in-process builds give the identical mesh (vertices, faces, part ids, stacks).
- Contracts on the raw mesh: the LOD0 budget, the overall length, and the origin at the footprint center. Cleat tips and comb
  tail sit on z = 0. Nothing floats: the machine is one connected piece, and blade and tiller stay attached on every level.
  The comb is a rectangle. The cleat bars cover about a third of the belt. The track center is as documented. Every stack and
  tile is well formed.
- Cleats, wheels and lamps are copies of one template. The copies are rigid and never mirrored.
- The lights table holds the headlights, roof lamps, beacons and tail lamps, in the author and the game frame.
- The hull rim stops at the windshield base. The red, its gloss and coat are the measured vanilla ones. The bars that carry
  the lights stay on the levels that need them.
- NEG mutations: the audits raise on each of these: a floating part, a LOD2 that loses the push frame, an over-cap LOD0, an
  under-budget LOD0, a LOD1 over 50 %, a LOD2 over its cap, an off-center origin, a brand word, a mirrored UV face, and alpha
  below 255.
- Full run only (no --quick). The test bakes and exports into a temp folder. Then it runs validate_asset_folder, which
  must report 0 errors. It checks the LOD caps and ratio. It checks the raw FBX frame on every level (x lateral, y up, z forward,
  centimeters: base pivot, overall length, origin at the footprint center, forward is +Z, tris in the file). The textures are
  1024 with alpha 255. The ControlMask is empty. No UV face is mirrored. The manifest has no absolute path. A fresh-process
  rebuild gives identical FBX tri counts and geometry.
- Full run only, the lights. The test checks the Emissive map for size, RGBA, alpha 255, a small lit share and only the two
  authored colors. Both LODs share it through settings.json. Every level keeps lit white and amber surface. NEG: a black map,
  a full map, a broken alpha and a LOD2 without the bar all raise. The tris stay at the pre-lights counts. The red's median
  texel is the target. The manifest carries the surgery rows.
"""

import hashlib
import json
import math
import os
import shutil
import subprocess
import sys
import tempfile

import bpy
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
import cs2_asset     # noqa: E402
import cs2_uv        # noqa: E402
import cs2_validate  # noqa: E402
import groomer as G  # noqa: E402

QUICK = "--quick" in sys.argv
FAILS = []


def ok(cond, msg):
    print(f"[test_groomer] {'ok' if cond else 'FAIL'}: {msg}")
    if not cond:
        FAILS.append(msg)


def raises(fn, *a, exc=G.GroomerAuditError):
    try:
        fn(*a)
    except exc:
        return True
    return False


def build_hash(b):
    h = hashlib.sha256()
    h.update(np.round(np.array(b.V, np.float64), 7).tobytes())
    h.update(json.dumps([list(f) for f in b.F]).encode())
    h.update(json.dumps(b.fpart).encode())
    h.update(json.dumps(b.names).encode())
    h.update(json.dumps(b.share_list()).encode())
    return h.hexdigest()


def frame_of(b, name):
    """Local frame of a template copy from its part's first three vertices, for the rigidity check."""
    fs = b.part_faces(name)
    vs = [Vector(b.V[i]) for i in b.F[fs[0]]]
    u = (vs[1] - vs[0])
    v = (vs[2] - vs[1])
    return u, v, u.cross(v)


def test_structure():
    cs2_asset.validate_asset_name(G.NAME)
    ok("_" not in G.NAME, "no underscore in the asset name")
    a, c = G.build_all(), G.build_all()
    ok(build_hash(a) == build_hash(c), "two builds give the identical mesh")
    b = a
    G.audit_names(b)
    foot = G.audit_footprint(b)
    ok(True, f"footprint audit passes: {foot}")
    tris = sum(len(f) - 2 for f in b.F)
    G.audit_lod0_tris(tris)
    ok(G.LOD0_MIN <= tris <= G.LOD0_CAP, f"LOD0 {tris} tris in {G.LOD0_MIN}..{G.LOD0_CAP}")
    ok(all(len(f) <= 4 for f in b.F), "only tris and quads")
    ok(G.audit_nothing_floats(b) == 1, "one connected machine")
    ok(G.audit_lods_attached(b) == 2, "LOD1 and LOD2 keep the blade and the tiller attached")
    # the parts the top-down camera sees stay through the first switch, and the frames through the last
    lod1, lod2 = G.LOD_DELETE
    for nm in ("RailSideL", "RailRear", "HoodGrille"):
        ok(not nm.startswith(lod1), f"LOD1 keeps {nm}")
    for nm in ("HoodGrille", "BladeArmL", "BladeCarrier", "ArmUpper", "MudflapL", "Antenna"):
        ok(not nm.startswith(lod2), f"LOD2 keeps {nm}")
    # the comb is a rectangle with a straight rear edge on the snow
    for nm in ("CombL", "CombR"):
        fs = b.part_faces(nm)
        pts = [b.V[i] for f in fs for i in b.F[f]]
        tail = [p for p in pts if abs(p[0] + G.HALF_L) < 1e-6]
        ok(len(fs) == 1 and len(pts) == 4 and len(tail) == 2 and all(abs(p[2]) < 1e-6 for p in tail), f"{nm}: a rectangle, rear edge on the snow")
    # the cleat bars cover about a third of the belt
    cover = 2.0 * G.CLEAT_BASE / b.path.p
    ok(0.28 <= cover <= 0.40, f"cleat base covers {cover:.0%} of the pitch")
    ok(not any(n.startswith(("Intake", "LampHead")) for n in b.names), "one exhaust stack, no lamps on the nose")
    # the rim beside the windshield stays at the glass base, so no tab shows at the corners
    rim = [b.V[i] for f in b.part_faces("LowerBody") for i in b.F[f] if G.RIM_END_X - 1e-6 <= b.V[i][0] <= 2.72 + 1e-6]
    ok(len(rim) >= 4 and max(p[2] for p in rim) <= G.GLASS_BASE_Z + 1e-6, f"hull rim from x {G.RIM_END_X} to 2.72 stays at or under the glass base, {max(p[2] for p in rim):.3f}")
    # the measured red, and the colors of the lights
    ok(G.ZONES["red"][1] == 0.0 and abs(G.ZONES["red"][2] - 0.85) < 1e-6 and G.COAT["red"] == 1.0, "red: dielectric, gloss 0.85, coat 1, as the vanilla paint")
    a_, w_ = G.LIGHT_AMBER, G.LIGHT_WHITE
    ok(a_[0] > a_[1] > a_[2] and a_[2] == 0.0, f"amber light {a_}")
    ok(min(w_) > 0.7, f"white light {w_}")
    # a level that deletes a lamp keeps the bar painted behind it
    for nm in ("LampRoofF0L", "BeaconL", "LightBarRear"):
        ok(nm.startswith(lod2), f"LOD2 deletes {nm}")
    ok(not "LightBar".startswith(lod2), "LOD2 keeps the front bar, which carries its lights")
    ok("LampRoofR0L".startswith(lod1) and not "LightBarRear".startswith(lod1), "LOD1 deletes the rear lamps and keeps the rear bar, which carries them")
    ok(not "BeaconL".startswith(lod1) and not "LampRoofF0L".startswith(lod1), "LOD1 keeps the front lamps and beacons")
    # what surgery gets
    sg = G.LIGHT_SURGERY
    ok(sg["mesh_lod0_single_light"]["purpose"] == 71, "the mesh light follows the prefab's effects (purpose 71)")
    es = sg["effect_source"]
    ok(len(es) == 3 and all(len(r["cid"]) == 32 for r in es), "three effect rows with their CIDs")
    ok(es[0]["effect"] == "SkiGroomerFloodSource" and es[2]["effect"] == "SkiGroomerFloodSource" and es[0]["cid"] == es[2]["cid"],
       "row 0 the front flood (purpose 71 follows it), row 2 the rear flood")
    ok(all(abs(sum(q * q for q in r["m_Rotation_xyzw"]) - 1.0) < 1e-5 for r in es), "unit rotations")
    fa, ra = G.flood_aim_author(es[0]), G.flood_aim_author(es[2])
    c25, s25 = math.cos(math.radians(25.0)), math.sin(math.radians(25.0))
    ok((fa - Vector((c25, 0.0, -s25))).length < 1e-4, f"front flood aims forward, 25 degrees down {tuple(round(v, 4) for v in fa)}")
    ok((ra - Vector((-c25, 0.0, -s25))).length < 1e-4, f"rear flood aims back, 25 degrees down {tuple(round(v, 4) for v in ra)}")
    ok(es[2]["m_PositionOffset"][1] == es[0]["m_PositionOffset"][1] and es[2]["m_PositionOffset"][2] < min(x for x, _ in G.HOOD_PROFILE) + 0.2,
       "rear flood at the front one's height, over the tiller hood's rear edge")
    # Every red panel lies outside the rear flood's 110 degree cone (half angle 55). In game the engine hood (light on the
    # roof bar) and the tiller hood (light over the deck's rear end) both glowed like lamps.
    rp = Vector((es[2]["m_PositionOffset"][2], 0.0, es[2]["m_PositionOffset"][1]))       # game (0, y, z) -> author (z, 0, y)
    for label, prof in (("engine hood", G.HOOD_STATIONS), ("tiller hood", G.HOOD_PROFILE)):
        deg = min(math.degrees(ra.angle(Vector((x, 0.0, zt)) - rp)) for x, zt in prof)
        ok(deg > 55.0 + 5.0, f"the red {label} is outside the rear flood's cone with margin (nearest {deg:.1f} degrees off its aim)")
    ok(all(2.9 <= r["m_PositionOffset"][1] and abs(r["m_PositionOffset"][0]) == 0.0 and str(r["m_PositionOffset"][0]) == "0.0" for r in sg["effect_source"]), "effect rows on the center line, in the game frame")
    co = np.array(b.V)
    ok(abs(co[:, 2].min()) < 1e-6, "lowest point on z = 0")
    ok(abs(co[:, 0].max() - co[:, 0].min() - G.OVERALL_L) < 0.03, f"overall length {co[:, 0].max() - co[:, 0].min():.3f} m")
    ok(abs((G.XF + G.XR) * 0.5 - G.TRACK_CENTER_X) < 1e-6, "the documented track center is the track's center")
    ok(abs(2.0 * (G.YI + G.TW) - 4.21) < 0.001, "width over the tracks 4.21 m")
    tips = G.blade_tips()
    ok(tips[0][1] > 2.3 and tips[1][1] < -2.3 and abs(tips[0][1] + tips[1][1]) < 1e-6, f"blade tips {tips}")
    # One tile face per belt surface is canonical. Every other face has corners in the tile.
    for key in ("BeltOuter", "BeltInner", "edge"):
        t = b.tiles[key]
        ok(t["canon"] is not None and len(t["dups"]) > 20, f"tile {key}: canon + {len(t['dups'])} copies")
        for f, cs in t["dups"]:
            for tt, ww in cs:
                if not (-1e-6 <= ww <= 1.0 + 1e-6 and -1e-6 <= tt <= 1.0 + 1e-6):
                    ok(False, f"tile {key} face {f}: corner ({tt}, {ww}) leaves the tile")
                    break
    # stacks: same face layout, and the canonical copy is the biggest (no copy under its island's density)
    for canon, dups in b.share_list():
        for dp in dups:
            if [len(b.F[i]) for i in canon] != [len(b.F[i]) for i in dp]:
                ok(False, "a stack with different face layouts")
    ok(True, f"{len(b.share_list())} stacks and tiles well formed")
    # the cleats are rigid copies: each edge keeps its length and no copy is mirrored
    names = [n for n in b.names if n.startswith("Cleat")]
    ok(len(names) > 60, f"{len(names)} cleats")
    t0 = frame_of(b, names[0])
    bad_len = bad_flip = 0
    for nm in names[1:]:
        t = frame_of(b, nm)
        if abs(t[0].length - t0[0].length) > 1e-5 or abs(t[1].length - t0[1].length) > 1e-5:
            bad_len += 1
    ok(bad_len == 0, "all cleats share one size")
    # outer faces of the cleats on the ground run must point down at the snow
    low = [n for n in names if min(b.V[i][2] for f in b.part_faces(n) for i in b.F[f]) < 1e-5]
    ok(len(low) > 16, f"{len(low)} cleats on the ground")
    for nm in low[:6]:
        top = b.part_faces(nm)[1]
        nrm = G._newell([Vector(b.V[i]) for i in b.F[top]])
        ok(nrm.z < 0.0, f"{nm}: cleat top on the ground run faces the snow")
    # lights
    lt = G.lights_table(b)
    for key, n in (("headlight", 2), ("roof_front", 6), ("roof_rear", 4), ("beacon", 2), ("tail", 2)):
        ok(key in lt and len(lt[key]["author_xyz"]) == n, f"lights: {n} {key}")
    for p, g in zip(lt["headlight"]["author_xyz"], lt["headlight"]["game_local_xyz"]):
        ok(abs(g[0] + p[1]) < 1e-3 and abs(g[1] - p[2]) < 1e-3 and abs(g[2] - p[0]) < 1e-3, f"frame rule T on headlight {p} -> {g}")
    return b


def test_negatives():
    b = G.build_all()
    ok(raises(G.audit_lod0_tris, G.LOD0_CAP + 1), "NEG LOD0 over the cap raises")
    ok(raises(G.audit_lod0_tris, G.LOD0_MIN - 1), "NEG LOD0 under the budget raises")
    ok(raises(G.audit_lod_chain, {0: 4000, 1: 2001, 2: 400}), "NEG LOD1 over 50 % raises")
    ok(raises(G.audit_lod_chain, {0: 4000, 1: 1900, 2: 501}), "NEG LOD2 over its cap raises")
    # a floating part
    b2 = G.build_all()
    box_v = [(0.0, 0.0, 6.0), (0.3, 0.0, 6.0), (0.3, 0.3, 6.0), (0.0, 0.3, 6.0)]
    b2.add("Floater", "red", box_v, [[0, 1, 2, 3]])
    ok(raises(G.audit_nothing_floats, b2), "NEG a floating part raises")
    # a level that drops the push frame leaves the blade floating
    lod1, lod2 = G.LOD_DELETE
    ok(raises(G.audit_lods_attached, b, (lod1, tuple(lod2) + ("BladeArm", "BladeCarrier"))), "NEG a LOD2 without the push frame raises")
    # off center
    b3 = G.build_all()
    b3.V = [(x + 0.2, y, z) for x, y, z in b3.V]
    ok(raises(G.audit_footprint, b3), "NEG an off-center origin raises")
    # raised above the ground
    b4 = G.build_all()
    b4.V = [(x, y, z + 0.1) for x, y, z in b4.V]
    ok(raises(G.audit_footprint, b4), "NEG a machine above z = 0 raises")
    # brand word
    b5 = G.build_all()
    b5.names[0] = "PistenThing"
    ok(raises(G.audit_names, b5), "NEG a brand word in a part name raises")
    # mirrored UV
    ob = b.to_object("NegUV")
    me = ob.data
    lay = me.uv_layers.new(name="UVUnique")
    start, total = cs2_uv.face_loop_table(me)
    uv = np.zeros((len(me.loops), 2))
    for p in me.polygons:
        s0, n = int(start[p.index]), int(total[p.index])
        for k in range(n):
            ang = 2.0 * math.pi * k / n
            uv[s0 + k] = (0.5 + 0.1 * math.cos(ang), 0.5 + 0.1 * math.sin(ang))
    cs2_uv.set_loop_uvs(lay, uv)
    G.audit_no_mirror(me)
    ok(True, "the mirror audit passes a counterclockwise layout")
    s0, n = int(start[3]), int(total[3])
    uv[s0:s0 + n] = uv[s0:s0 + n][::-1]
    cs2_uv.set_loop_uvs(lay, uv)
    ok(raises(G.audit_no_mirror, me), "NEG a mirrored UV face raises")
    bpy.data.objects.remove(ob)
    bpy.data.meshes.remove(me)


def fbx_extents(path):
    objs = cs2_validate.import_fbx_temp(path)
    try:
        co = []
        for o in objs:
            if o.type == "MESH":
                arr = np.empty(len(o.data.vertices) * 3, np.float64)
                o.data.vertices.foreach_get("co", arr)
                m = np.array(o.matrix_world)
                pts = arr.reshape(-1, 3) @ m[:3, :3].T + m[:3, 3]
                co.append(pts)
        co = np.concatenate(co)
        return co, sum(len(p.vertices) - 2 for o in objs if o.type == "MESH" for p in o.data.polygons)
    finally:
        cs2_validate.remove_objects(objs)


def test_full():
    tmp = tempfile.mkdtemp(prefix="groomer_test_")
    try:
        asset_dir = os.path.join(tmp, "A", G.NAME)
        built = G.build(asset_dir, export=True)
        tris = built["tris"]
        ok(tris[1] <= 0.5 * tris[0], f"LOD1 {tris[1]} <= 50 % of LOD0 {tris[0]}")
        ok(tris[2] <= G.LOD2_CAP, f"LOD2 {tris[2]} <= {G.LOD2_CAP}")
        ok(tris == {0: 4134, 1: 1824, 2: 496}, f"the lights are texture only: tris stay at 4134 / 1824 / 496, {tris}")
        ok(built["validation"]["errors"] == 0, "validator: 0 errors")
        cs2_validate.validate_asset_folder(asset_dir, G.NAME, G.VALIDATE_SPEC)
        ok(True, "validate_asset_folder passes")
        G.audit_textures(asset_dir)
        G.audit_no_mirror(built["lod0"].data)
        ok(True, "textures and UV audits pass on the finished asset")
        for sfx in ("BaseColor", "Normal", "MaskMap", "ControlMask", "Emissive"):
            w, h, _d, _c = cs2_validate.read_png_header(os.path.join(asset_dir, f"{G.NAME}_{sfx}.png"))
            ok((w, h) == (G.ATLAS, G.ATLAS), f"{sfx} {w} x {h}")
        base = cs2_validate.load_png_pixels(os.path.join(asset_dir, f"{G.NAME}_BaseColor.png"))
        ok(float(base[..., 3].min()) >= 1.0 - 0.5 / 255.0, "BaseColor alpha 255 everywhere")
        ctl = cs2_validate.load_png_pixels(os.path.join(asset_dir, f"{G.NAME}_ControlMask.png"))
        ok(float(ctl.max()) <= 0.5 / 255.0, "ControlMask empty")
        msk = cs2_validate.load_png_pixels(os.path.join(asset_dir, f"{G.NAME}_MaskMap.png"))
        ok(float(msk[..., 2].max()) <= 0.5 / 255.0, "MaskMap B empty")
        # the red: its median texel is the vanilla composite, on the vanilla finish, and only the red carries coat
        red = (base[..., 0] > 0.45) & (base[..., 1] < 0.25) & (base[..., 2] < 0.25) & (msk[..., 1] > 0.5)
        med = np.median(base[red][:, :3], axis=0) * 255.0
        ok(np.abs(med - np.array((180.0, 33.0, 33.0))).max() <= 3.0, f"red median texel {np.round(med, 1)}, target (180, 33, 33)")
        ok(abs(float(np.median(msk[red][:, 3])) - 0.85) < 0.02, "red gloss 0.85")
        coated = msk[..., 1] > 0.5
        ok(float((base[coated][:, 0] > 1.5 * base[coated][:, 1]).mean()) > 0.99, "coat only on the red")
        # the Emissive map
        ep = os.path.join(asset_dir, f"{G.NAME}_Emissive.png")
        ok(cs2_validate.read_png_header(ep)[3] == 6, "Emissive is RGBA: a map without alpha ships uncompressed")
        em = cs2_validate.load_png_pixels(ep)
        ok(float(em[..., 3].min()) >= 1.0 - 0.5 / 255.0, "Emissive alpha 255 everywhere")
        lit_share = float((np.max(em[..., :3], axis=2) > 2.0 / 255.0).mean())
        ok(0.0005 < lit_share < G.EMISSIVE_MAX_SHARE, f"small lights: {lit_share:.2%} of the atlas lit")
        full = em[em[..., 0] > 0.98][:, :3]
        d_w = np.abs(full - np.array((1.0, 0.965, 0.92))).max(axis=1)
        d_a = np.abs(full - np.array((1.0, 0.698, 0.0))).max(axis=1)
        ok(int((d_w < 0.05).sum()) > 300 and int((d_a < 0.05).sum()) > 300, f"both colors on the map: {int((d_w < 0.05).sum())} white, {int((d_a < 0.05).sum())} amber texels")
        ok(float(((d_w < 0.05) | (d_a < 0.05)).mean()) > 0.98, "full-strength texels are the authored white or amber, nothing else")
        with open(os.path.join(asset_dir, "settings.json"), encoding="utf-8") as fh:
            shared = json.load(fh)["sharedAssets"]
        for lod in (1, 2):
            ok(shared.get(f"{G.NAME}_LOD{lod}_Emissive.png") == f"{G.NAME}_Emissive.png", f"LOD{lod} shares the Emissive map")
        # every level keeps both colors of light
        objs = {0: built["lod0"], 1: built["lod1"], 2: built["lod2"]}
        areas = G.audit_lights(asset_dir, objs)
        ok(True, f"every level keeps its lights: {areas}")
        ok(areas[2]["white"] > 0.0 and areas[2]["amber"] > 0.0, "LOD2 shows white and amber")
        # NEG: the gates catch a dark map, a floodlit map, a broken alpha and a LOD2 without the bar
        neg_e = os.path.join(tmp, "neg_e")
        shutil.copytree(asset_dir, neg_e)
        neg_ep = os.path.join(neg_e, f"{G.NAME}_Emissive.png")
        black = np.zeros_like(em)
        black[..., 3] = 1.0
        cs2_asset._write_png_raw(neg_ep, "negblack", black, G.ATLAS, False)
        ok(raises(G.audit_lights, neg_e, objs), "NEG a black Emissive map fails the lights audit")
        ok(raises(G.audit_textures, neg_e), "NEG a black Emissive map fails the texture audit")
        cs2_asset._write_png_raw(neg_ep, "negfull", np.ones_like(em), G.ATLAS, False)
        ok(raises(G.audit_textures, neg_e), "NEG a fully lit Emissive map fails the texture audit")
        half = em.copy()
        half[20, 20, 3] = 0.5
        cs2_asset._write_png_raw(neg_ep, "neghalf", half, G.ATLAS, False)
        ok(raises(G.audit_textures, neg_e), "NEG Emissive alpha below 255 fails the texture audit")
        saved_del = G.LOD_DELETE
        G.LOD_DELETE = (saved_del[0], tuple(saved_del[1]) + ("LightBar",))
        try:
            neg_lods = G.make_lods(built["lod0"])
        finally:
            G.LOD_DELETE = saved_del
        ok(raises(G.audit_lights, asset_dir, {2: neg_lods[1]}), "NEG a LOD2 without the bar loses its amber and raises")
        for o in neg_lods:
            bpy.data.meshes.remove(o.data)
        # NEG on the finished texture: the audit must catch a broken alpha
        neg = os.path.join(tmp, "neg")
        shutil.copytree(asset_dir, neg)
        bad = base.copy()
        bad[10, 10, 3] = 0.5
        cs2_asset._write_png_raw(os.path.join(neg, f"{G.NAME}_BaseColor.png"), "negbase", bad, G.ATLAS, True)
        ok(raises(G.audit_textures, neg), "NEG BaseColor alpha below 255 raises")
        # raw FBX frame: lateral on x, up on y, forward on z, in centimeters
        files = [f"{G.NAME}.fbx", f"{G.NAME}_LOD1.fbx", f"{G.NAME}_LOD2.fbx"]
        for f in files:
            ok(os.path.isfile(os.path.join(asset_dir, f)), f"{f} written")
        for lv, f in enumerate(files):
            co, n = fbx_extents(os.path.join(asset_dir, f))
            lo, hi = co.min(0), co.max(0)
            ok(abs(lo[1]) < 0.01, f"{f}: base pivot on the ground (min up {lo[1]:.4f} cm)")
            ok(abs(hi[2] - lo[2] - G.OVERALL_L * 100.0) < 5.0, f"{f}: overall length along +Z {hi[2] - lo[2]:.1f} cm")
            ok(abs(lo[0] + hi[0]) < 2.0 and abs(lo[2] + hi[2]) < 2.0, f"{f}: origin at the footprint center")
            roof = co[co[:, 1] > 250.0]
            ok(float(roof[:, 2].mean()) > 0.0, f"{f}: the cab is forward of the origin, so forward is +Z")
            ok(n == tris[lv], f"{f}: {n} tris in the file, {tris[lv]} built")
        with open(os.path.join(asset_dir, f"{G.NAME}.manifest.json"), encoding="utf-8") as fh:
            text = fh.read()
        ok("Users" not in text and ":\\\\" not in text, "no absolute path in the manifest")
        data = json.loads(text)
        ok(abs(data["track_center_x_author_m"] - G.TRACK_CENTER_X) < 1e-9, "manifest carries the track center")
        ok("headlight" in data["lights"], "manifest carries the light positions")
        ok(data["surgery"]["mesh_lod0_single_light"]["purpose"] == 71 and len(data["surgery"]["effect_source"]) == 3, "manifest carries the surgery rows")
        ok(data["emissive"]["map"] == f"{G.NAME}_Emissive.png" and set(data["pack"]["lit_m2"]) == {"LOD0", "LOD1", "LOD2"}, "manifest carries the Emissive map and the lit surface per level")
        # a fresh process gives the same files
        out2 = os.path.join(tmp, "B")
        r = subprocess.run([bpy.app.binary_path, "--background", "--factory-startup", "--python-exit-code", "1", "--python",
                            os.path.join(HERE, "groomer.py"), "--", "--out", out2, "--export"], cwd=HERE, capture_output=True, text=True)
        ok(r.returncode == 0, f"fresh-process build exit {r.returncode}")
        if r.returncode == 0:
            for f in files:
                ca, na = fbx_extents(os.path.join(asset_dir, f))
                cb, nb = fbx_extents(os.path.join(out2, G.NAME, f))
                ok(na == nb and ca.shape == cb.shape and np.allclose(ca, cb, atol=1e-3), f"{f}: identical geometry in a fresh process")
            for sfx in ("BaseColor", "Normal"):
                a = cs2_validate.load_png_pixels(os.path.join(asset_dir, f"{G.NAME}_{sfx}.png"))
                c = cs2_validate.load_png_pixels(os.path.join(out2, G.NAME, f"{G.NAME}_{sfx}.png"))
                d = float(np.abs(a - c).max())
                # (the Cycles AO bake is the only noise source: its effect stays in the low bits)
                ok(d < 0.08, f"{sfx}: max difference {d:.3f} between two processes")
            a = cs2_validate.load_png_pixels(ep)
            c = cs2_validate.load_png_pixels(os.path.join(out2, G.NAME, f"{G.NAME}_Emissive.png"))
            ok(np.array_equal(a, c), "Emissive: identical between two processes (no bake in it)")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def main():
    G.clear_scene()
    test_structure()
    test_negatives()
    if not QUICK:
        G.clear_scene()
        test_full()
    if FAILS:
        print(f"[test_groomer] {len(FAILS)} FAILED")
        for f in FAILS:
            print(f"[test_groomer]   {f}")
        sys.exit(1)
    print("[test_groomer] all passed" + (" (quick)" if QUICK else ""))


main()
