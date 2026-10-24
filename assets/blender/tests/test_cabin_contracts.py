"""
Contract gate for lift_cabin.py (gondola SkiGondolaCabin8A, tram SkiTramCabinA; cabins/A).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_cabin_contracts.py [-- --full]

What it proves (one PASS line, exit 1 on any failure):
  1. Every geometry contract holds on LOD0 / LOD1 / LOD2 of each build: gondola G1-G10
     (hang 4.50 floor underside -> grip top, rope point under the grip, plumb, stalk
     continuity, outboard stalk + door CENTROID (skeptic MC2), extents, nothing below 0,
     single-quad stamps, carriage inside the Wilin mount 1.217 x 1.267); tram T1-T11
     (R27 hang 10.50 / rope 10.00, R28 wheels on the ONE HV track rope + haul grips at
     +-0.50 / -0.40, R29 mirror symmetry, R30 half-width 1.10 + tower keep-outs, clear zone,
     plumb, z coverage) - for the default, --hang 7.00 and --split builds, and the gondola's
     --outboard=+Y variant (the approved revision 1b side; nitpick A3 made -Y the default,
     lift_cabin.GD_OUTBOARD); the gondola --lod2 decimate A/B must RAISE (C6: the protected
     contract parts alone exceed the LOD2 budget, the shell collapses, G3/G6 break).
 11. Nitpick A3 (2026-10-13, the gondola's outboard side mirrored to -Y = the carrier's right on
     the stations' spin): the default build's stalk + doors sit on authoring -Y (G5 in the
     outboard-positive frame), and the -Y and +Y builds are EXACT mirrors: LOD0 / LOD2 vertex
     multisets equal under y -> -y (0.1 mm), the same tris on every LOD, the hang, the lateral
     bounds mirrored; the -Y build checked as +Y raises G5 (and the reverse).
  2. The asserts BITE: deliberately broken copies of the shipped LOD0 raise (lifted mesh,
     no grip, mirrored stalk, no hanger; plates in the saddle zone, wheels off the rope,
     one-sided haul grip, wide fork, sunk carriage top).
  3. Budgets: gondola LOD0 in G lod0_band (cap 2,500), LOD1 <= 0.48 x LOD0, LOD2 ~200 (<= 250);
     tram LOD0 within +-10 % of the projection (cap 8,000), LOD1 <= 0.48 x LOD0, LOD2 <= 500.
  4. UV layers (skeptic MC3): the ingested parts arrive with the Maya "map1" layer, leave
     the ingest with exactly ["UVMap"], every part (ingested + kit) carries exactly
     ["UVMap"] before material_sources, and the shipped mesh carries only "UVUnique", no
     material, no color attribute.
  5. Ingest facts: shell 739 -> 571 tris after clean, doors 114 + 114, the plug closes 3
     loops with 17 tris (0 boundary / non-manifold edges), the material split counts.
  6. Determinism per kind: two builds in one process hash identically (vertices, faces,
     UVUnique on every LOD) and write byte-identical PNGs. Draft bakes by default; --full
     repeats it with the shipped --finish bake (1024 / 2048, AO 32 / 48, relief). Both bakes
     also prove the recolor level: BaseColor p95 0.78-0.82 under every present ControlMask
     channel, ControlMask R + G + B <= 1, A 0 (review minor 6).
  7. Nothing is written into the repo: without --export every asset folder lives under
     <tempdir>/lift_cabin.
  8. Revision 1 (verdicts 2026-10-11 items 6 / 9): every logo quad shares ONE pinned
     square (identical, non-mirrored UVUnique) of >= 4x the shipped logo edge; the stamp rects
     are restored 1:1 by the bake; the shipped texels are not anisotropic (area-weighted
     median <= 1.05, p95 <= 1.35 - the stamp stripe's band squeeze is pre-compensated and the
     kit cylinders / wheels / body runs unwrap metric); with --full every hero face ships at
     >= 120 px/m; the rebuilt rollers are 10-segment, the door plug keeps its 17-tri ingest
     fact and ships softened.
  9. Revision 1b (build/revision1-check-cabins.md C1-C4): LOD0 holds G11 (the shell + door
     plug island has 0 open / 0 non-manifold edges) and G12 / T12 (no same-facing coplanar
     overlap > 1 cm2 between two parts or inside one); every builder cap passes cap_check (the
     signed-area test). Negatives: the grip cap triangulated the shipped way (before its
     normal) raises CAP; lifting the frame-lip angle cap re-creates the C3 head fold and
     raises G11 in soften_shell; a shell face deleted raises G11; the grip stem pushed back
     flush raises G12; the crossbar ends pushed back flush with the uprights raise T12.
 10. Revision 2 (tram, 2026-10-11: centered outward-opening sliding double doors + gondola detail
     parity): T13 on LOD0 / LOD1 (4 leaves meeting at the center, 0.75-0.90 wide, faces proud of
     the wall, sill at the boarding floor, hardware present, bounds still +-1.880 / +-4.030);
     the door kit's rollers / grab bars keep their segment counts. Negatives: a leaf pushed back
     flush with the wall raises T12 + T13, a leaf slid off the center raises T5 + T13, the grab
     bars deleted raise T13. Determinism covers the new parts (same two-build hash).
"""

import json
import os
import sys
import time

import bpy
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import lift_cabin as L  # noqa: E402

ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
FULL = "--full" in ARGV
FAILS = []

HERO_MIN_PPM = 120.0                     # Revision 1: hero faces at the shipped texture size
HERO = {"gondola": ("body_", "doorbody_", "trim_", "doortrim_", "roof_", "logo_"),
        "tram": ("band_", "doorband_", "trim_", "doortrim_", "frame_", "doorframe_", "sill_",
                 "reveal_", "logo_")}
ANISO_MEDIAN, ANISO_P95 = 1.05, 1.35

G_CODES = {0: {"G1", "G2", "G3", "G4", "G5", "G6", "G8", "G9", "G10", "G11", "G12"},
           1: {"G1", "G2", "G3", "G4", "G5", "G6", "G8", "G10"},
           2: {"G1", "G2", "G3", "G4", "G5", "G6", "G8", "G10"}}
T_CODES = {0: {"T1", "T2", "T3", "T3b", "T4", "T5", "T6", "T8", "T10", "T11a", "T11b", "T12",
               "T13"},
           1: {"T1", "T3", "T3b", "T4", "T5", "T6", "T8", "T11a", "T11b", "T13"},
           2: {"T1", "T3", "T3b", "T4", "T5", "T6", "T8", "T11a", "T11b"}}


def check(cond, msg):
    tag = "ok  " if cond else "FAIL"
    print(f"[cabin-test] {tag} {msg}")
    if not cond:
        FAILS.append(msg)
    return cond


def codes(rows):
    return {r["code"] for r in rows if r["ok"]}


def contract_rows_ok(res, want, label):
    for lvl, need in want.items():
        rows = res["contracts"].get(lvl)
        if rows is None:
            check(False, f"{label}: no contract rows for LOD{lvl}")
            continue
        have = codes(rows)
        bad = [r for r in rows if not r["ok"]]
        check(need <= have and not bad,
              f"{label} LOD{lvl}: contracts {sorted(have)} held (need {sorted(need)})")


def in_draft_root(res):
    return os.path.normcase(os.path.abspath(res["asset_dir"])).startswith(
        os.path.normcase(os.path.abspath(L.DRAFT_ROOT)))


# --------------------------------------------------------------------------------------
# Negative tests: perturbed copies of the shipped LOD0 must raise
# --------------------------------------------------------------------------------------

def _dup(name):
    return L._dup(bpy.data.objects[name], name + "Neg")


def _vidx(obj, prefixes):
    m = L.Mesh(obj)
    return sorted({v for f in m.faces_of(prefixes) for v in m.faces[f]})


def _move(obj, idx, d):
    d = Vector(d)
    for i in idx:
        obj.data.vertices[i].co += d
    obj.data.update()


def _delete(obj, prefixes):
    table = json.loads(obj.data[L.PART_TABLE_PROP])
    L._delete_parts(obj, table, tuple(prefixes))


def _mesh_verts(obj):
    """World-space vertices of a built LOD (net frame: Blender x = authoring y, z up)."""
    M = obj.matrix_world
    return np.array([tuple(M @ v.co) for v in obj.data.vertices], dtype=np.float64)


def _fbx_verts(path):
    """Raw FBX vertices (meters) of an exported LOD, read with the validator's temp import."""
    import cs2_validate
    objs = cs2_validate.import_fbx_temp(path)
    try:
        o = [x for x in objs if x.type == "MESH"][0]
        return np.array([v.co[:] for v in o.data.vertices], dtype=np.float64) / 100.0
    finally:
        cs2_validate.remove_objects(objs)


def _mirror_dist(a, b):
    """Worst nearest-vertex distance between `a` and `b` with x negated (None when the counts differ): the
    -Y / +Y builds are exact mirrors when this is ~0 (net / raw x = authoring y in both frames)."""
    from mathutils.kdtree import KDTree
    if a.shape != b.shape:
        return None
    m = b.copy()
    m[:, 0] = -m[:, 0]
    kd = KDTree(len(m))
    for i, v in enumerate(m):
        kd.insert(Vector(v), i)
    kd.balance()
    return max(kd.find(Vector(v))[2] for v in a)


def part_tris(name, prefixes):
    """Triangles of the faces whose part starts with `prefixes` on the shipped LOD0."""
    m = L.Mesh(bpy.data.objects[name])
    return int(sum(int(m.loop_total[f]) - 2 for f in m.faces_of(prefixes)))


def expect_raise(label, codes_any, fn):
    try:
        fn()
    except RuntimeError as e:
        msg = str(e)
        hit = [c for c in codes_any if f"{c} " in msg]
        check(bool(hit), f"negative {label}: raised on {hit} ({msg[:110]}...)")
        return
    check(False, f"negative {label}: a broken mesh PASSED the contracts")


def negatives_gondola(name):
    # net frame: authoring y (lateral) = Blender +X, authoring x (travel) = Blender -Y
    o = _dup(name)
    _move(o, range(len(o.data.vertices)), (0.0, 0.0, 0.01))
    expect_raise("gondola lifted 1 cm", ["G1"], lambda: L.gondola_contracts(o, 0))
    L._drop(o)
    o = _dup(name)
    _delete(o, ("grip_",))
    expect_raise("gondola without grip", ["G1", "G2"], lambda: L.gondola_contracts(o, 0))
    L._drop(o)
    o = _dup(name)
    for v in o.data.vertices:
        v.co.x = -v.co.x
    o.data.flip_normals()
    # nitpick A3: the shipped build is -Y (GD_OUTBOARD); its mirror (= the approved +Y cabin) checked against
    # the shipped side puts the stalk + doors inboard
    expect_raise("gondola mirrored (stalk + doors inboard)", ["G5"],
                 lambda: L.gondola_contracts(o, 0))
    L._drop(o)
    o = _dup(name)
    expect_raise("gondola -Y (shipped) checked as the +Y side (stalk + doors inboard of that frame)", ["G5"],
                 lambda: L.gondola_contracts(o, 0, "+Y"))
    L._drop(o)
    o = _dup(name)
    _delete(o, ("hanger_",))
    expect_raise("gondola without hanger", ["G4"], lambda: L.gondola_contracts(o, 0))
    L._drop(o)
    # Revision 1b: G11 (closed shell island) and G12 (no same-facing coplanar overlap)
    o = _dup(name)
    _delete_one_face(o, "trim_shell")
    expect_raise("gondola shell with one face deleted (open island)", ["G11"],
                 lambda: L.gondola_contracts(o, 0))
    L._drop(o)
    o = _dup(name)
    # outboard along net x: +x for the approved +Y cabin, -x for the shipped -Y one (nitpick A3)
    sgn = -1.0 if L.GD_OUTBOARD == "-Y" else 1.0
    _move(o, _vidx(o, ("grip_stem",)), (sgn * 0.002, 0.0, 0.0))
    expect_raise("gondola grip stem pushed back flush with the grip face (C4 as shipped)", ["G12"],
                 lambda: L.gondola_contracts(o, 0))
    L._drop(o)


def _delete_one_face(obj, part):
    """Delete the first face of `part` (FACES_ONLY: its edges stay, the island opens)."""
    import bmesh
    f = L.Mesh(obj).faces_of(part)[0]
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[f]], context="FACES_ONLY")
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


GRIP_PROFILE = [(-0.04, 4.455), (-0.04, 4.50), (0.30, 4.50), (0.30, 4.30), (0.115, 4.30),
                (0.10, 4.315), (0.10, 4.44), (-0.025, 4.44)]


def negative_caps():
    """Revision 1b C1: the grip caps built and triangulated the SHIPPED way (bmesh.ops.triangulate
    on faces fresh from bm.faces.new, before any normal exists) must fail cap_check; the fixed
    builder must pass on the same outline."""
    import bmesh
    prof = list(GRIP_PROFILE)
    n = len(prof)
    area = 0.5 * sum(prof[k][0] * prof[(k + 1) % n][1] - prof[(k + 1) % n][0] * prof[k][1]
                     for k in range(n))
    if area < 0.0:
        prof = list(reversed(prof))
    bm = bmesh.new()
    lo = [bm.verts.new((-0.15, p, q)) for p, q in prof]
    hi = [bm.verts.new((0.15, p, q)) for p, q in prof]
    caps = [bm.faces.new(list(reversed(lo))), bm.faces.new(hi)]
    rec = [(set(f.verts), [v.co.copy() for v in f.verts]) for f in caps]
    out = bmesh.ops.triangulate(bm, faces=caps, quad_method="BEAUTY", ngon_method="BEAUTY")["faces"]
    errors = []
    for k, (vs, outline) in enumerate(rec):
        try:
            L.cap_check(f"grip cap {k} (shipped order)", outline,
                        [[v.co for v in t.verts] for t in out if set(t.verts) <= vs])
        except RuntimeError as e:
            errors.append(str(e))
    bm.free()
    check(bool(errors) and errors[0].startswith("CAP "),
          f"negative grip caps triangulated before their normals (C1 as shipped): "
          f"{len(errors)} cap(s) raised ({errors[0][:120] if errors else 'none'}...)")
    try:
        o = L.extrude_profile("capprobe_grip", GRIP_PROFILE, -0.15, 0.15, axis="X")
        ok = True
        L._drop(o)
    except RuntimeError as e:
        ok = False
        print(f"[cabin-test] {e}")
    check(ok, "extrude_profile (normals first) passes cap_check on the same grip outline")


def negative_c3():
    """Revision 1b C3: lifting the frame-lip angle cap re-creates the shipped end-window head
    fold, which soften_shell must refuse (G11) before anything is built on it."""
    old = L.FRAME_LIP_MAX_DEG
    L.FRAME_LIP_MAX_DEG = 181.0
    try:
        expect_raise("gondola frame-lip bevel over the > 90 deg plug folds (C3 as shipped)",
                     ["G11"], lambda: L.run(["--kind", "gondola"]))
    finally:
        L.FRAME_LIP_MAX_DEG = old


def negatives_tram(name, dims):
    o = _dup(name)
    # Revision 1: the lighter plates' foot sits 0.02 above the rope; drag it 0.10 down
    idx = [i for i in _vidx(o, ("bogie_",)) if o.data.vertices[i].co.z < dims["rope"] + 0.05]
    _move(o, idx, (0.0, 0.0, -0.10))
    expect_raise("tram plates in the saddle zone", ["T3", "T11a"],
                 lambda: L.tram_contracts(o, 0, dims))
    L._drop(o)
    o = _dup(name)
    _move(o, _vidx(o, ("sheave_",)), (0.02, 0.0, 0.0))
    expect_raise("tram wheels off the track rope", ["T2"], lambda: L.tram_contracts(o, 0, dims))
    L._drop(o)
    o = _dup(name)
    m = L.Mesh(o)
    one_side = [n for n in m.table if n.startswith("clamp_haul") and
                m.co[sorted({v for f in m.faces_of(n) for v in m.faces[f]}), 1].mean() > 0]
    _delete(o, tuple(one_side))
    expect_raise("tram one-sided haul grips", ["T5", "T10"], lambda: L.tram_contracts(o, 0, dims))
    L._drop(o)
    o = _dup(name)
    for i in _vidx(o, ("fork_crossbar",)):
        o.data.vertices[i].co.x *= 1.10
    expect_raise("tram fork wider than 1.10", ["T6"], lambda: L.tram_contracts(o, 0, dims))
    L._drop(o)
    o = _dup(name)
    _move(o, [i for i in _vidx(o, ("carbeam_",)) if o.data.vertices[i].co.z > dims["hang"] - 0.001],
          (0.0, 0.0, -0.02))
    expect_raise("tram carriage top sunk 2 cm", ["T1", "T4"], lambda: L.tram_contracts(o, 0, dims))
    L._drop(o)
    # Revision 1b C4: the crossbar end rings (net frame: Blender X = lateral) back at +-1.08,
    # flush with the uprights' outer faces -> same-facing coplanar overlap, T12
    o = _dup(name)
    fh = L.T["fork_half"]
    for i in _vidx(o, ("fork_crossbar",)):
        v = o.data.vertices[i]
        if abs(v.co.x) > 1.0:
            v.co.x = fh if v.co.x > 0 else -fh
    o.data.update()
    expect_raise("tram crossbar ends pushed back flush with the uprights (C4 as shipped)", ["T12"],
                 lambda: L.tram_contracts(o, 0, dims))
    L._drop(o)
    # Revision 2 (net frame: Blender X = authoring lateral y, Blender -Y = authoring travel x):
    # one leaf's face pushed back flush with the 1.85 wall (no reveal) -> T12 + T13
    leaf = ("doorband_pb", "doortrim_pb", "doorframe_pb")
    o = _dup(name)
    wall = L.T["profile"][2][2]
    for i in _vidx(o, leaf):
        v = o.data.vertices[i]
        if v.co.x > wall + 0.005:
            v.co.x = wall
    o.data.update()
    expect_raise("tram door leaf pushed back flush with the wall (no reveal)", ["T12", "T13"],
                 lambda: L.tram_contracts(o, 0, dims))
    L._drop(o)
    o = _dup(name)
    _move(o, _vidx(o, leaf), (0.0, -0.05, 0.0))
    expect_raise("tram door leaf slid 5 cm off the centre", ["T5", "T13"],
                 lambda: L.tram_contracts(o, 0, dims))
    L._drop(o)
    o = _dup(name)
    _delete(o, ("doorgrab_",))
    expect_raise("tram door grab bars deleted", ["T13"], lambda: L.tram_contracts(o, 0, dims))
    L._drop(o)


# --------------------------------------------------------------------------------------
# Builds
# --------------------------------------------------------------------------------------

def gondola_checks(res, label):
    contract_rows_ok(res, G_CODES, label)
    t = res["tris"]
    b0, b1 = L.G["lod0_band"]
    check(b0 <= t[0] <= b1 and t[0] <= 2500,
          f"{label}: LOD0 {t[0]} tris in {b0}-{b1} (cap 2,500; Revision 1 rebuild)")
    check(t[1] <= 0.48 * t[0], f"{label}: LOD1 {t[1]} <= 0.48 x LOD0 ({0.48 * t[0]:.0f})")
    check(150 <= t[2] <= 250, f"{label}: LOD2 {t[2]} ~200 (<= 250)")
    ing = res["ingest"]
    check(ing["shell_raw_tris"] == 739 and ing["clean_tris"] == {"shell": 571, "door_l": 114,
                                                                  "door_r": 114},
          f"{label}: ingest 739 -> {ing['clean_tris']}")
    check(ing["plug_loops"] == 3 and ing["plug_tris"] == 17 and ing["plug_boundary_after"] == 0
          and ing["plug_nonmanifold_after"] == 0,
          f"{label}: door plug {ing['plug_loops']} loops / {ing['plug_tris']} tris, "
          f"{ing['plug_boundary_after']} boundary edges after")
    soft = ing.get("soften", {})
    bev = soft.get("bevels", {})
    check(soft.get("nonmanifold_before") == 0
          and all(bev.get(k, {}).get("edges", 0) > 0 for k in ("frames", "crown", "skirt")),
          f"{label}: Revision 1 shell soften on a closed shell + plug: {bev}")
    check(soft.get("open_after") == 0 and soft.get("nonmanifold_after") == 0
          and soft.get("welded_coincident", 99) <= 2,
          f"{label}: Revision 1b softened shell closed (0 open / 0 non-manifold), "
          f"{soft.get('welded_coincident')} collapsed bevel vertex welded")
    want = {k: v * (2 if k.startswith("door") else 1) for k, v in L.G_SPLIT_EXPECT.items()}
    got = {k: v for k, v in ing["split_tris"].items() if k in want}
    check(got == want, f"{label}: material split {got}")
    check(any("map1" in lays for lays in ing["uv_before"]),
          f"{label}: ingested parts arrived with the Maya map1 layer {ing['uv_before']}")
    check(all(v == ["UVMap"] for v in ing["uv_after"].values()),
          f"{label}: every ingested part leaves the ingest with exactly ['UVMap'] "
          f"({len(ing['uv_after'])} parts)")
    uv = res["uv_layers_before_sources"]
    check(all(v == ["UVMap"] for v in uv.values()),
          f"{label}: all {len(uv)} parts carry exactly ['UVMap'] before material_sources (MC3)")
    check(res["shipped_uv"] == ["UVUnique"] and res["shipped_materials"] == 0
          and not res["shipped_colour_attrs"],
          f"{label}: shipped mesh UV {res['shipped_uv']}, {res['shipped_materials']} materials, "
          f"colour attrs {res['shipped_colour_attrs']}")
    check(all(v["ok"] for v in res.get("verify", [])) and len(res.get("verify", [])) == 3,
          f"{label}: FBX re-import top 4.50 / min 0 on every LOD")
    check(res.get("validation", {}).get("errors") == 0, f"{label}: cs2_validate 0 errors")
    check(in_draft_root(res), f"{label}: draft written outside the repo ({res['asset_dir']})")


def tram_checks(res, label, role="whole", proj=None):
    want = {k: set(v) for k, v in T_CODES.items()}
    if role == "cabin":
        for v in want.values():
            v -= {"T1", "T2", "T4", "T10"}
            v.add("T9")
    elif role == "carriage":
        for v in want.values():
            v -= {"T1", "T3b", "T4", "T13"}
            v.add("T9")
    contract_rows_ok(res, want, label)
    t = res["tris"]
    if proj:
        check(abs(t[0] - proj) <= 0.10 * proj and t[0] <= 8000,
              f"{label}: LOD0 {t[0]} within +-10 % of {proj} (cap 8,000)")
    check(t[1] <= 0.48 * t[0], f"{label}: LOD1 {t[1]} <= 0.48 x LOD0 ({0.48 * t[0]:.0f})")
    check(t[2] <= 500 and t[2] <= t[1], f"{label}: LOD2 {t[2]} <= 500")
    uv = res["uv_layers_before_sources"]
    check(all(v == ["UVMap"] for v in uv.values()),
          f"{label}: all {len(uv)} parts carry exactly ['UVMap'] before material_sources (MC3)")
    check(res["shipped_uv"] == ["UVUnique"] and res["shipped_materials"] == 0
          and not res["shipped_colour_attrs"], f"{label}: shipped mesh UV {res['shipped_uv']}")
    check(all(v["ok"] for v in res.get("verify", [])) and len(res.get("verify", [])) == 3,
          f"{label}: FBX re-import top on every LOD")
    check(res.get("validation", {}).get("errors") == 0, f"{label}: cs2_validate 0 errors")
    check(in_draft_root(res), f"{label}: draft written outside the repo")


def uv_anisotropy(obj):
    """Area-weighted (median, p95, max) of the shipped UVUnique texel anisotropy: ratio of
    the singular values of the 3D -> UV map per face (first triangle)."""
    me = obj.data
    uvl = me.uv_layers[L.cs2_uv.UNIQUE_LAYER].data
    co = np.array([v.co[:] for v in me.vertices])
    r, a = [], []
    for p in me.polygons:
        li, vi = list(p.loop_indices), list(p.vertices)
        P = co[vi[:3]]
        U = np.array([uvl[i].uv[:] for i in li[:3]])
        e1, e2 = P[1] - P[0], P[2] - P[0]
        n = np.cross(e1, e2)
        an = np.linalg.norm(n)
        if an < 1e-10:
            continue
        x = e1 / np.linalg.norm(e1)
        y = np.cross(n / an, x)
        p2 = np.array([[0.0, 0.0], [e1 @ x, e1 @ y], [e2 @ x, e2 @ y]])
        J = np.linalg.lstsq(np.c_[p2, np.ones(3)], U, rcond=None)[0][:2]
        sv = np.linalg.svd(J, compute_uv=False)
        if sv[1] < 1e-12:
            continue
        r.append(sv[0] / sv[1])
        a.append(p.area)
    r, a = np.array(r), np.array(a)
    o = np.argsort(r)
    cw = np.cumsum(a[o]) / a.sum()
    return (float(r[o][np.searchsorted(cw, 0.5)]), float(r[o][np.searchsorted(cw, 0.95)]),
            float(r.max()))


def stamp_checks(res, label, kind):
    """Revision 1: the pinned logo square, the 1:1 stamp rects, isotropic texels, hero px/m."""
    info = res.get("stamp_layout") or {}
    A = res["texture_px"]
    K = L.G if kind == "gondola" else L.T
    ratio = K["logo_frac"] * K["tex"] / L.LOGO_SHIPPED_PX[kind]
    check(info.get("logo_px") == int(round(K["logo_frac"] * A)) and ratio >= 4.0,
          f"{label}: logo square {info.get('logo_px')} px @ {A} = {K['logo_frac'] * K['tex']:.0f} "
          f"px @ {K['tex']}, {ratio:.2f}x the shipped {L.LOGO_SHIPPED_PX[kind]:.0f} px (>= 4x)")
    obj = bpy.data.objects[res["name"]]
    me = obj.data
    m = L.Mesh(obj)
    uvl = me.uv_layers[L.cs2_uv.UNIQUE_LAYER].data
    boxes, mirrored, bad = [], 0, []
    for nm in [n for n in m.table if n.startswith("logo_")]:
        fs = m.faces_of(nm)
        if len(fs) != 1 or int(m.loop_total[fs[0]]) != 4:
            bad.append(nm)
            continue
        poly = me.polygons[fs[0]]
        q = np.array([uvl[li].uv[:] for li in poly.loop_indices])
        boxes.append(np.r_[q.min(axis=0), q.max(axis=0)])
        P = np.array([me.vertices[v].co[:] for v in poly.vertices])
        M = np.linalg.lstsq(np.c_[q, np.ones(4)], P, rcond=None)[0]
        if np.dot(np.cross(M[0], M[1]), np.array(poly.normal[:])) <= 0.0:
            mirrored += 1
    boxes = np.array(boxes)
    same = len(boxes) >= 2 and float(np.abs(boxes - boxes[0]).max()) * A < 0.51
    side = float((boxes[0][2] - boxes[0][0]) * A) if len(boxes) else 0.0
    check(same and not bad and mirrored == 0 and abs(side - info.get("logo_px", -1)) < 0.51,
          f"{label}: {len(boxes)} logo quads share ONE {side:.0f} px square, {mirrored} mirrored, "
          f"not one quad {bad}")
    check(bool(info.get("rects")) and info.get("reserved_restored") == len(info.get("rects", [])),
          f"{label}: {info.get('reserved_restored')} / {len(info.get('rects', []))} stamp rects "
          f"restored 1:1 by the bake (pinned, constant sources)")
    med, p95, mx = uv_anisotropy(obj)
    check(med <= ANISO_MEDIAN and p95 <= ANISO_P95,
          f"{label}: shipped texel anisotropy median {med:.3f} / p95 {p95:.3f} (max {mx:.2f}) "
          f"<= {ANISO_MEDIAN} / {ANISO_P95}")
    hero = (res.get("post") or {}).get("density_hero") or {}
    want = HERO[kind]
    got = {k: hero.get(k) for k in want}
    if FULL:
        low = {k: v for k, v in got.items() if v is None or v < HERO_MIN_PPM}
        check(not low, f"{label}: hero faces >= {HERO_MIN_PPM:.0f} px/m at {A}: {got}")
    else:
        print(f"[cabin-test] info {label}: draft hero px/m @ {A} (about x{2048 // A} at 2048): {got}")


def mask_levels(res, label):
    """Light albedo ships under the recolor masks (review minor 6): BaseColor p95 0.78-0.82
    (sRGB of the 0.604 linear slot albedo) under every present ControlMask channel; R + G + B
    <= 1 and A 0 on the ControlMask."""
    d, n = res["asset_dir"], res["name"]
    base = L.cs2_validate.load_png_pixels(os.path.join(d, f"{n}_BaseColor.png"))
    ctl = L.cs2_validate.load_png_pixels(os.path.join(d, f"{n}_ControlMask.png"))
    check(float(ctl[..., :3].sum(axis=-1).max()) <= 1.0 + 1e-3 and float(ctl[..., 3].max()) < 1e-3,
          f"{label}: ControlMask R + G + B <= 1, A 0")
    lum = base[..., :3].mean(axis=-1)
    seen = 0
    for ch, slot in enumerate("RGB"):
        under = lum[ctl[..., ch] > 0.5]
        if under.size < 50:
            continue
        seen += 1
        p95 = float(np.percentile(under, 95))
        check(0.78 <= p95 <= 0.82, f"{label}: BaseColor p95 under mask {slot} = {p95:.3f} "
                                   f"(0.78..0.82, {under.size} texels)")
    check(seen >= 1, f"{label}: at least one recolour mask present ({seen})")


def determinism(kind, extra):
    a = L.run(["--kind", kind, "--lods"] + extra)[0]
    ha, pa = dict(a["hashes"]), dict(a["png_sha1"])
    b = L.run(["--kind", kind, "--lods"] + extra)[0]
    hb, pb = dict(b["hashes"]), dict(b["png_sha1"])
    check(ha == hb, f"determinism {kind} {extra}: geometry hashes {ha} == {hb}")
    check(pa == pb and len(pa) == 4, f"determinism {kind} {extra}: {len(pa)} PNGs byte-identical")
    return b


def main():
    t0 = time.perf_counter()
    q = ["--finish"] if FULL else []

    # gondola: default (twice: determinism), contracts, budgets, MC3, ingest, negatives
    g = determinism("gondola", q + ["--verify"])
    gondola_checks(g, "gondola")
    mask_levels(g, "gondola")
    stamp_checks(g, "gondola", "gondola")
    # skeptic MC6 numbers, Revision 1: rollers are 10-segment cylinders with the outer cap only
    # (the inner cap sits inside the frame): 20 side + 10 cap tris; the door plug keeps its
    # 17-tri ingest fact (asserted above) and ships softened with the shell
    seg = L.G["roller_segs"]
    rollers = [part_tris(g["name"], f"roller_{k}") for k in range(8)]
    check(rollers == [3 * seg] * 8,
          f"gondola: 8 rollers x {3 * seg} tris ({seg} segments, skeptic MC6 recount) {rollers}")
    plug = part_tris(g["name"], "doorplug_")
    check(part_tris(g["name"], "plate_face") == 2 and plug == g["ingest"]["plug_tris_shipped"]
          and plug >= 17,
          f"gondola: plate_face 1 quad, doorplug_ {plug} tris shipped (17 at ingest)")
    negatives_gondola(g["name"])
    # nitpick A3: the shipped side and the exact-mirror proof against the approved +Y build
    check(L.GD_OUTBOARD == "-Y" and L.CARRIERS["gd8"]["outboard"] == "-X",
          f"nitpick A3: lift_cabin ships the gondola outboard {L.GD_OUTBOARD} (prefab "
          f"{L.CARRIERS['gd8']['outboard']}): the carrier's right on the stations' spin")
    gn_lod0 = _mesh_verts(bpy.data.objects[g["name"]])
    gn_lod2 = _fbx_verts(os.path.join(g["asset_dir"], f"{g['name']}_LOD2.fbx"))
    hz = gn_lod0[:, 2]                          # net frame: Blender z up, x = authoring y
    stalk = gn_lod0[(hz > 2.80) & (hz < 3.85)]
    check(len(stalk) > 0 and float(stalk[:, 0].max()) < -0.40,
          f"nitpick A3: the default gondola's stalk on authoring -Y (net x < 0): x "
          f"{stalk[:, 0].min():+.3f}..{stalk[:, 0].max():+.3f}" if len(stalk) else "nitpick A3: no stalk band")
    negative_caps()
    negative_c3()
    gy = L.run(["--kind", "gondola", "--lods", "--verify", "--outboard=+Y"])[0]
    contract_rows_ok(gy, G_CODES, "gondola --outboard +Y (the approved side)")
    gp_lod0 = _mesh_verts(bpy.data.objects[gy["name"]])
    gp_lod2 = _fbx_verts(os.path.join(gy["asset_dir"], f"{gy['name']}_LOD2.fbx"))
    for lvl, a, b in (("LOD0", gn_lod0, gp_lod0), ("LOD2", gn_lod2, gp_lod2)):
        d = _mirror_dist(a, b)
        check(d is not None and d <= 1e-4,
              f"nitpick A3: {lvl} of the -Y build = the +Y build mirrored (net x -> -x), vertex multiset "
              f"{len(a)} / {len(b)}, worst {d if d is None else round(d, 6)} m")
    check(g["tris"] == gy["tris"], f"nitpick A3: tris -Y {g['tris']} == +Y {gy['tris']}")
    ba, bb = g["bounds_authoring"], gy["bounds_authoring"]
    check(abs(ba[0][1] + bb[1][1]) <= 1e-4 and abs(ba[1][1] + bb[0][1]) <= 1e-4
          and all(abs(ba[k][j] - bb[k][j]) <= 1e-4 for k in (0, 1) for j in (0, 2)),
          f"nitpick A3: bounds mirrored in y only (-Y {ba} / +Y {bb})")
    # C6 measured: the contract-bearing parts alone (grip, hanger, carriage frame, yoke) are
    # ~244 tris > the ~200 LOD2 budget, so a decimated LOD2 collapses the shell and must FAIL
    expect_raise("gondola --lod2 decimate (why the authored proxy is the default)",
                 ["G3", "G6"], lambda: L.run(["--kind", "gondola", "--lods", "--lod2", "decimate"]))

    # tram: default (twice), compact hang, split, negatives
    tr = determinism("tram", q + ["--verify"])
    tram_checks(tr, "tram", proj=L.T["lod0_projection"])
    mask_levels(tr, "tram")
    stamp_checks(tr, "tram", "tram")
    dims = L.tram_dims(10.50)
    clamps = [part_tris(tr["name"], (f"clamp_haul{k}_", f"bolt_haul{k}_")) for k in range(4)]
    check(clamps == [48] * 4, f"tram: 4 haul clamps x 48 tris (skeptic MC6) {clamps}")
    # Revision 2 door kit: 8 rubber-tired rollers (8 segments + the outer fan cap: 16 + 8 tris),
    # 4 grab bars (6 segments, no caps: 12 tris), 2 safety-yellow thresholds (5 faces: 10)
    m = L.Mesh(bpy.data.objects[tr["name"]])
    rollers = [part_tris(tr["name"], n) for n in m.table if n.startswith("doorroller_")]
    grabs = [part_tris(tr["name"], n) for n in m.table if n.startswith("doorgrab_")]
    sills = [part_tris(tr["name"], n) for n in m.table if n.startswith("doorsill_")]
    check(rollers == [24] * 8 and grabs == [12] * 4 and sills == [10] * 2,
          f"tram Revision 2: rollers {rollers}, grab bars {grabs}, thresholds {sills}")
    negatives_tram(tr["name"], dims)
    tc = L.run(["--kind", "tram", "--lods", "--verify", "--hang", "7.0"])[0]
    tram_checks(tc, "tram --hang 7.00", proj=L.T["lod0_projection"])
    sp = L.run(["--kind", "tram", "--lods", "--verify", "--split"])
    check(len(sp) == 2 and [r["role"] for r in sp] == ["cabin", "carriage"],
          f"tram --split builds {[(r['name'], r['role']) for r in sp]}")
    for r in sp:
        tram_checks(r, f"tram --split {r['role']}", role=r["role"])

    dt = time.perf_counter() - t0
    if FAILS:
        print(f"FAIL test_cabin_contracts: {len(FAILS)} check(s) failed: {FAILS[:5]}")
        sys.exit(1)
    print(f"PASS test_cabin_contracts: gondola + tram contracts on LOD0/1/2 (default = outboard -Y since "
          f"nitpick A3, --outboard=+Y = the approved side, an exact mirror on LOD0 / LOD2; --hang 7.00, --split), "
          f"negatives + decimate-LOD2 A/B raise, budgets, "
          f"one UVMap (MC3), ingest counts, Revision 1 logo square / 1:1 stamp rects / "
          f"isotropic texels{' / hero >= 120 px/m' if FULL else ''}, Revision 1b closed shell "
          f"(G11) / no coplanar z-fight (G12, T12) / cap_check + C1 / C3 / C4 negatives, "
          f"Revision 2 tram doors (T13) + flush / off-centre / no-handle negatives, "
          f"{'finish' if FULL else 'draft'} determinism per kind ({dt:.0f} s)")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001
        import traceback
        traceback.print_exc()
        sys.exit(1)
