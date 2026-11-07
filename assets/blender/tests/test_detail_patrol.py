"""
test_detail_patrol - detail pass 1 contracts of the patrol huts P1 (SkiPatrolHutCommercialLowA) and P2
(SkiPatrolHQCommercialLowA): design/detail-pass-1.md 6.2 D1-D10 as corrected by design/detail-pass-1-skeptic.md (M4
boards, M6 margin, M7 LOD2, M8 flags, M9 facing, S2 / S6 clearances) on the kit of build/detail-pass-1-kit.md. Report
build/detail-pass-1-patrol.md.

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_detail_patrol.py
        [-- --only P1|P2]    one recipe
        [-- --export]        also: the shipped folders under assets/import/buildings/A carry
                             the tested build (mesh hashes + PNG sha1s equal W1) and the
                             preview LOD frames' XOR (D9)

Per recipe three finishes at the shipping size (tex 1024, AO 48, LODs, no previews, no validate) into a temp dir: W1 and
W2 with the recipe's weather, N with weather None.
  D1  detail counts: every design item's parts by exact name. sub_objects 2 / 4 with the
      design 1.6 GUIDs. P2 repack (no gable face looks into the hut, floor / deck undersides
      are soffit_ parts).
  D2  _Emissive: lit texels <= 0.5 % of the map and within +-25 % of the UV area (texels) of
      the emissive faces. Every emissive lamp face >= 2.5 texels across. Beacons red (R >= 4 G).
  D3  labels: one part / one UV island each (quads 1 face, discs 16). Stamp rects pairwise
      disjoint. The word boards T1 (cap >= 0.40 m, stroke >= 4 texels at the shipped px/m).
      Roof-cross arm >= 4 texels. The roof crosses are on LOD2.
  D4  weathering: manifest weather.vanilla.ok True at the recipe intensity, free slots, snow 0
      (VL5 0). ControlMask byte-identical to N. BaseColor identical to N inside every stamp rect.
  D5  determinism: W2 == W1 (five PNGs, LOD0_prebake / Win / LOD0 / LOD1 / LOD2 hashes).
  D6  budgets: LOD0 <= 1,400 / 1,750, LOD1 <= min(650, 50 % LOD0), LOD2 <= 200. w = 1 px/m
      >= 42 / 28. P2 frames (w 0.75) >= 20. tex 1024. 9 batches. 7.0 MB.
  D7  engine base OK on LOD0 / 1 / 2. Every new part starts at z >= 0.02 (the ground band).
  D8  lot margins >= 0.25 m and the arrival centroid (the builder raises on coplanar overlaps
      and faces inside the massing).
  D9  LOD: the new dressing is gone at LOD1 except masts, board faces (+ their frames: P2 keeps
      its frame since check-patrol F1, the two gable struts beam_strutL / R pay for it) and
      roof crosses. It is gone at LOD2 except the roof crosses (on the LOD2 mesh). Every part
      that LOD1 deletes, LOD2 deletes too. P2's front disc leaves LOD2 (M7). LOD2 not collapsed.
  D10 sub-objects: a wall prop's pivot lies on one of our wall faces (<= 0.05 m) facing its
      normal. A deck prop's pivot is on a deck top (+-0.01). Every prop's box (the union of
      its vanilla variants' render bounds, dump.zip) inflated by 0.05 m touches no face of
      ours other than its mounting surface (skeptic S6).
  D11 LEAN (fifteenth look: "a few of the toboggans lean against nothing"): recipes/_lean.py
      measures every leaning part (akja_, gear_, pole_) of the built recipe. The top of its
      back face has a SOLID face of the EXPECTED support part <= 0.02 m behind it over >= 80 %
      of its width. Its foot is on grade / the deck within 0.02 m. The set of leaning parts is
      exactly the recipe's declared one. Moving a part 0.10 m off its support or lifting it
      0.05 m makes _lean.check raise. The builder itself fails the build (CONTRACT LEAN) when
      its rest gap is sabotaged to 0.10 m.
FINE-DETAIL STEP 3d (2026-10-12, build/fine-detail-step3d-patrol-sign.md): the shipping recipes are FineRecipes, so
D1-D11 run on the FINE build (the name / info boards in the sign set, the akjas / spine boards / hose reel / door
lanterns as rows. SPEC below lists the fine parts) and:
  D1  rows: the approved vanilla rows + the fine rows (1 wall light, kit lean rows, P2 the hose reel) + the
      sign-set identity row lodge appends. Every vanilla GUID as listed, every mod CID = fine_detail.KIT's
  D3  host labels only (the routed boards are gone). The boards' T1 is read from the sheet (owner manifest)
  D10 also the kit hose reel (its shipped bounds_m) and pairwise: no two rows' boxes overlap
  D11 LEAN on the 6 P1 poles (modeled) AND on every kit lean row's REAL mesh (recipes/_lean.check_rows): the
      declared support (the part the modeled akja / board leaned on), foot on grade / the deck, no host
      overlap. Negatives: a row moved 0.10 m off / lifted 0.05 m / pushed 0.03 m into its support / with a
      foreign declared support raises. The builder fails the build with the rows 0.10 m off
  D12 sign set: SG1 UV = the painted rect, SG2 boards within 1 mm of the --no-fine host's label quads, SG3
      none left in LOD0-2 / labels, SG4 one identity row (staged CID), SG5 inside the lot, SG6 <= 400 tris,
      sharer settings = the five owner keys, no PNG
  D13 legacy: `--no-fine` (fine_detail.effective(fine=False)) blockout = the approved shacks' hashes
      (LOD0_prebake / Win of the lean-fix export: P1 b0d4f1b4dfa7 / 463b9012de23, P2 11cb23e4083a /
      38666256c858). The build report proves the full 13-file identity
  D14 the stale-map rule (lodge.remove_stale_maps): a finish into a folder seeded with a stale
      `<name>_LOD3_BaseColor.png` + `<name>_Emissive2.png` (maps this build never writes) and a foreign PNG
      removes the two, keeps the foreign one, and neither reaches settings.json nor the manifest's files
Exit 1 on the first failure. One PASS line otherwise.
"""

import glob
import hashlib
import importlib.util
import json
import math
import os
import re
import shutil
import sys
import tempfile
import time
import traceback
from dataclasses import replace

import bpy
import numpy as np

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir))
sys.path.insert(0, HERE)
import bldg_recipes as R   # noqa: E402
import cs2_asset           # noqa: E402
import cs2_signset as SS   # noqa: E402  (fine-detail step 3d)
import fine_detail as FD   # noqa: E402
import cs2_bldg as K       # noqa: E402
import cs2_compose         # noqa: E402
import cs2_uv              # noqa: E402
import lodge               # noqa: E402

CHECKS = []
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
EXPORT = "--export" in ARGV
ONLY = ARGV[ARGV.index("--only") + 1].upper() if "--only" in ARGV else None


def ok(msg):
    CHECKS.append(msg)
    print(f"ok {msg}")


def _patrol_module():
    spec = importlib.util.spec_from_file_location("test_detail_patrol_recipes",
                                                  os.path.join(HERE, "recipes", "patrol.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


PT = _patrol_module()
GUIDS = {"WallLightShortRangeRandom01": "cfc12fde01d9da24b86ddedd657eaae1",
         "ACVentWallRandom01": "b99325ff4ff4dfd459734d8f765c2597",
         "GardenBenchRandom01": "8df0703be09426948b5bc3262d86bc04"}
# union of each family's variant render bounds, prop-local (x across, y up, z out of the
# wall / depth), from %TEMP%\pltar\dump.zip RenderPrefab m_Bounds (2026-10-11, scratch dp1p/zb.py):
# WallLightShortRange01-03, ACVentWall01-05, GardenBench01-04
PROP_BOUNDS = {"WallLightShortRangeRandom01": ((-0.14, -0.06, 0.0), (0.14, 0.06, 0.11)),
               "ACVentWallRandom01": ((-0.19, -0.20, 0.0), (0.19, 0.28, 0.25)),
               "GardenBenchRandom01": ((-1.06, 0.0, -0.50), (1.06, 1.05, 0.50))}
DECK_PROPS = ("GardenBenchRandom01",)
CLEAR_M = 0.05
# fine-detail step 3d: the kit hose reel's box = its shipped bounds_m (props/B manifest), prop-local
_KIT_MAN = os.path.join(REPO, "assets", "import", "props", "B", "SkiDressHoseReelB", "SkiDressHoseReelB.manifest.json")
_BM = json.load(open(_KIT_MAN, encoding="utf-8"))["bounds_m"]
PROP_BOUNDS["SkiDressHoseReel"] = (tuple(_BM["min"]), tuple(_BM["max"]))
# the approved shacks' blockout hashes (lean fix export, 2026-10-11 21:19, test_detail_kit K4 before step 3d)
# P2 LOD0_prebake re-pinned 2026-10-24 (T81): was 11cb23e4083a. T94 (2026-10-19) moved P2's toboggan rack + akja
# (P2_RACK / P2_AKJA) from south to north of the door, which the --no-fine path shares. `Win` is unchanged
LEGACY_HASH = {"P1": ("b0d4f1b4dfa7", "463b9012de23"), "P2": ("79d939910e08", "38666256c858")}

SPEC = {
    # fine-detail step 3d: the FINE build's dressing parts (the boards' faces went to the sign set. The akjas, spine
    # boards, hose reel and door lanterns are rows: they must NOT be in the host, `gone`)
    "P1": dict(
        new=("sign_p1name_frame", "bench_p1", "bench_p1legs",
             "emblem_p1roof_face", "snowguard_p1E", "snowguard_p1Eb", "mast_p1", "mast_p1br",
             "lamp_p1beacon", "tobrack_p1", "stripe_p1step"),
        gone=("sign_p1name_face", "sign_p1info_face", "akja_p1", "akja_p1b", "gear_p1board", "lamp_p1door",
              "lamp_p1doorarm"),
        lod1_kept=("sign_p1name_frame", "emblem_p1roof_face", "mast_p1", "mast_p1br"),
        lod2_kept=("emblem_p1roof_face",),
        boards=(("p1.name", "SKI / PATROL"),), crosses=("emblem_p1roof_face",), beacon="lamp_p1beacon",
        lod0_cap=1400, ppm_w1=42.0, n_sub=6, n_vanilla=3, n_mod=3, sign_faces=2),
    "P2": dict(
        new=("sign_hqname_frame", "emblem_hqroof_face", "mast_hq", "mast_hqbr",
             "lamp_hqbeacon", "flag_hq", "emblem_hqflagA_face", "emblem_hqflagB_face",
             "tobrack_hq", "wood_hq",
             "sign_hqlogs_face", "snowguard_hqN", "snowguard_hqNb", "snowguard_hqS",
             "snowguard_hqSb", "gutter_hqS", "downspout_hqS", "stripe_hq0", "stripe_hq1",
             "stripe_hq2", "stripe_hq3", "soffit_hqfloor", "soffit_deckhq"),
        gone=("sign_hqname_face", "akja_hqa", "akja_hqb", "akja_hqc", "gear_hqboard", "hosereel_hq",
              "lamp_hqdoor", "lamp_hqdoorarm"),
        lod1_kept=("sign_hqname_frame", "emblem_hqroof_face", "mast_hq",
                   "mast_hqbr", "soffit_hqfloor", "soffit_deckhq"),
        lod2_kept=("emblem_hqroof_face",),
        boards=(("p2.name", "SKI PATROL / FIRST AID"),), crosses=("emblem_hqroof_face",), beacon="lamp_hqbeacon",
        lod0_cap=1750, ppm_w1=28.0, n_sub=10, n_vanilla=5, n_mod=5, sign_faces=1, frames_w=0.75,
        frames_ppm=20.0),
}
# D11: leaning part / kit row -> the part it must rest on (lean fix, fifteenth look. Step 3d: the kit rows keep the
# modeled parts' supports)
SPEC["P1"]["lean"] = {f"pole_{k}": "wall_p1" for k in range(6)}
SPEC["P1"]["lean_rows"] = {"akja_p1": "wall_p1", "akja_p1b": "tobrack_p1", "gear_p1board": "tobrack_p1"}
SPEC["P1"]["lean_foot"] = ("grade",)
SPEC["P2"]["lean"] = {}
SPEC["P2"]["lean_rows"] = {"akja_hqa": "wall_hqs", "akja_hqb": "wall_hqs", "akja_hqc": "tobrack_hq",
                           "gear_hqboard": "gable_hqfront"}
SPEC["P2"]["lean_foot"] = ("grade", "deck_hq")


# ======================================================================================
# helpers
# ======================================================================================

def sha1_file(p, n=12):
    return hashlib.sha1(open(p, "rb").read()).hexdigest()[:n]


def png(path):
    img = bpy.data.images.load(path, check_existing=False)
    img.colorspace_settings.name = "Non-Color"
    a = np.array(img.pixels[:], np.float32).reshape(img.size[1], img.size[0], -1)
    bpy.data.images.remove(img)
    return a                       # row 0 = v 0 (the bake arrays and uv_bbox_px rows)


def finish(recipe, out):
    args = lodge.parse_args(["--recipe", recipe.key, "--no-validate", "--out",
                             os.path.join(out, recipe.letter)])
    res = lodge.run(recipe, args)
    pngs = {os.path.basename(p)[len(res["name"]) + 1:-4]: p
            for p in glob.glob(os.path.join(res["asset_dir"], "*.png"))}
    man = json.load(open(os.path.join(res["asset_dir"], res["name"] + ".manifest.json"),
                         encoding="utf-8"))
    return res, pngs, man


def table_of(obj):
    return json.loads(obj.data[cs2_asset.PART_TABLE_PROP])


def part_ids(obj):
    me = obj.data
    a = me.attributes.get("part")
    assert a is not None, f"{obj.name}: no part attribute"
    pv = np.empty(len(me.polygons), np.int32)
    a.data.foreach_get("value", pv)
    return pv


def names_present(obj, table):
    ids = set(int(v) for v in part_ids(obj))
    return {table[i - 1] for i in ids if 0 < i <= len(table)}


def part_keys(rec):
    """{part name: material key} of the recipe (its builder run once more, the parts removed again). The finished mesh no
    longer carries the keys."""
    parts = lodge.build_parts(rec, {"tower": True, "roof_windows": "win"})
    out = {o.name: k for o, k in parts}
    for o, _k in parts:
        me = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if me is not None and me.users == 0:
            bpy.data.meshes.remove(me)
    return out


def authoring(obj):
    """Vertices (n, 3) and per-polygon (normal, vertex index list) in the AUTHORING frame:
    the exported frame is orient_for_net's (x, y, z) -> (y, -x, z) of it (yaw 0)."""
    W = K._world_coords(obj)
    V = np.column_stack([-W[:, 1], W[:, 0], W[:, 2]])
    R3 = obj.matrix_world.to_3x3()
    polys = []
    for p in obj.data.polygons:
        n = R3 @ p.normal
        polys.append((np.array((-n.y, n.x, n.z)), list(p.vertices)))
    return V, polys


def quat_rot(q, v):
    x, y, z, w = q
    u = np.array((x, y, z))
    v = np.asarray(v, float)
    return v + 2.0 * np.cross(u, np.cross(u, v) + w * v)


def g2a(v):
    """game local (gx, gy, gz) -> authoring (x, y, z) = (gz, gx, gy)."""
    return np.array((v[2], v[0], v[1]), float)


def _tri_box(tri, c, axes, h):
    """SAT: does the triangle (3, 3) intersect the oriented box (center c, unit axes (3, 3),
    half extents h)?"""
    T = (tri - c) @ axes.T                     # box-local
    e = [T[1] - T[0], T[2] - T[1], T[0] - T[2]]
    I = np.eye(3)
    tests = [I[0], I[1], I[2], np.cross(e[0], e[1])]
    for a in range(3):
        for ed in e:
            tests.append(np.cross(I[a], ed))
    for ax in tests:
        if np.linalg.norm(ax) < 1e-12:
            continue
        p = T @ ax
        r = float(np.abs(ax) @ h)
        if p.min() > r or p.max() < -r:
            return False
    return True


def _point_in_poly(P, pts, n):
    """P in the convex-ish polygon pts (k, 3) projected along n (2D crossing test)."""
    ax = int(np.argmax(np.abs(n)))
    keep = [i for i in range(3) if i != ax]
    q = P[keep]
    poly = pts[:, keep]
    inside = False
    k = len(poly)
    for i in range(k):
        a, b = poly[i], poly[(i + 1) % k]
        if (a[1] > q[1]) != (b[1] > q[1]):
            xi = a[0] + (q[1] - a[1]) * (b[0] - a[0]) / (b[1] - a[1])
            if xi > q[0]:
                inside = not inside
    return inside


# ======================================================================================
# checks on the W1 build (objects alive in the scene)
# ======================================================================================

def d1_counts(key, rec, res, man, obj, table):
    sp = SPEC[key]
    missing = [n for n in sp["new"] if n not in table]
    assert not missing, f"{key} D1: missing parts {missing}"
    left = [n for n in sp["gone"] if n in table]
    assert not left, f"{key} D1: parts that rows / the sign set replace are still in the host: {left}"
    subs = man.get("sub_objects", {}).get("objects", [])
    # + 1: the sign-set identity row lodge appends at export
    assert len(subs) == sp["n_sub"] + 1 == len(rec.sub_objects) + 1, f"{key} D1: sub_objects {len(subs)}"
    kinds = man["sub_objects"].get("kinds")
    assert kinds == {"vanilla": sp["n_vanilla"], "mod": sp["n_mod"] + 1}, f"{key} D1: kinds {kinds}"
    for s in subs:
        if s.get("kind") == "mod":
            k = FD.KIT.get(s["prefab"])
            assert (k is not None and FD.row_colour_error(s) is None) or s.get("role") == "signset", \
                f"{key} D1: mod row {s['prefab']} CID {s['m_Object']}"
        else:
            assert GUIDS.get(s["prefab"]) == s["m_Object"], f"{key} D1: GUID of {s['prefab']}"
        assert s["m_ParentMesh"] == 0, f"{key} D1: {s['prefab']} m_ParentMesh {s['m_ParentMesh']}"
    extra = ""
    if key == "P2":
        V, polys = authoring(obj)
        pv = part_ids(obj)
        inner = 0
        for i, (n, vs) in enumerate(polys):
            nm = table[pv[i] - 1]
            if not nm.startswith("gable_hq"):
                continue
            x = V[vs, 0]
            if (abs(x.mean() - (PT.P2_X[1] - PT.P2_GABLE_T)) < 1e-3 and n[0] < -0.99) or \
                    (abs(x.mean() - (PT.P2_X[0] + PT.P2_GABLE_T)) < 1e-3 and n[0] > 0.99):
                inner += 1
        assert inner == 0, f"{key} D1: {inner} gable faces look into the hut"
        by = man["area_report"]["by_prefix"]
        assert "floor_" in by and "soffit_" in by, by
        extra = (f"; repack: 0 inner gable faces, soffit_ {by['soffit_']} m2 / w2a, floor_ "
                 f"{by['floor_']}")
    ok(f"{key} D1: {len(sp['new'])} new parts present, sub_objects {len(subs)} "
       f"({sorted(set(s['prefab'] for s in subs))}, m_ParentMesh 0){extra}")


def d2_emissive(key, rec, res, man, obj, table, pngs):
    A = res["tex"]
    em = png(pngs["Emissive"])
    lit = em[..., :3].max(-1) > 0.0
    n_lit = int(lit.sum())
    share = n_lit / float(A * A)
    me = obj.data
    lay = me.uv_layers[cs2_uv.UNIQUE_LAYER]
    # the bake leaves one material per mesh: the emissive faces are found by part (the
    # recipe's own part -> material key table)
    pk = part_keys(rec)
    emissive = {i + 1 for i, n in enumerate(table)
                if K.BLDG_MATERIALS[pk[n]].emissive_srgb is not None}
    assert emissive, f"{key} D2: no emissive part"
    margin = int(man["pack_margin"]["margin_px"])
    allowed = np.zeros((A, A), bool)
    face_tx = face_lit = 0
    narrow = []
    pv = part_ids(obj)
    for p in me.polygons:
        if int(pv[p.index]) not in emissive:
            continue
        q = np.array([tuple(lay.data[li].uv) for li in p.loop_indices]) * A
        x, y = q[:, 0], q[:, 1]
        across = min(float(x.max() - x.min()), float(y.max() - y.min()))
        if across < 2.5:
            narrow.append((table[pv[p.index] - 1], round(across, 2)))
        c0, c1 = int(math.floor(x.min())), int(math.ceil(x.max()))
        r0, r1 = int(math.floor(y.min())), int(math.ceil(y.max()))
        allowed[max(0, r0 - margin - 1):r1 + margin + 1, max(0, c0 - margin - 1):c1 + margin + 1] = True
        # texel centers inside the face (crossing test in UV texels)
        cc, rr_ = np.meshgrid(np.arange(c0, c1) + 0.5, np.arange(r0, r1) + 0.5)
        ins = np.zeros(cc.shape, bool)
        k = len(x)
        for i in range(k):
            xa, ya, xb, yb = x[i], y[i], x[(i + 1) % k], y[(i + 1) % k]
            if ya == yb:
                continue
            cross = ((ya > rr_) != (yb > rr_)) & (cc < xa + (rr_ - ya) * (xb - xa) / (yb - ya))
            ins ^= cross
        face_tx += int(ins.sum())
        face_lit += int(lit[r0:r1, c0:c1][ins].sum())
    stray = int((lit & ~allowed).sum())
    assert share <= 0.005, f"{key} D2: lit share {share:.4f} > 0.5 %"
    assert face_tx and face_lit >= 0.95 * face_tx,         f"{key} D2: {face_lit} of the {face_tx} emissive-face texels lit"
    assert stray == 0, f"{key} D2: {stray} lit texels outside the emissive islands (+ margin)"
    assert not narrow, f"{key} D2: emissive faces under 2.5 texels across: {narrow[:6]}"
    # the beacon's own texels are red
    rr = cs2_compose.uv_bbox_px(me, table, SPEC[key]["beacon"], A)
    sub = em[rr[0]:rr[1], rr[2]:rr[3], :3]
    m = sub.max(-1) > 0.0
    r_, g_ = float(sub[m][:, 0].mean()), float(sub[m][:, 1].mean())
    assert r_ >= 4.0 * g_, f"{key} D2: beacon not red (R {r_:.3f} G {g_:.3f})"
    ok(f"{key} D2: _Emissive {n_lit} lit texels ({100 * share:.3f} % <= 0.5 %; the faces' own "
       f"{face_tx} texels {face_lit / face_tx:.1%} lit, the rest is the {margin} px gutter "
       f"dilation, 0 stray), every lamp face >= 2.5 texels across, beacon R {r_:.3f} >= 4 G "
       f"{g_:.3f}")
    return n_lit


def d3_labels(key, rec, res, man, obj, table, lods):
    A = res["tex"]
    me = obj.data
    lay = me.uv_layers[cs2_uv.UNIQUE_LAYER]
    rects = {}
    host = SS.host_recipe(rec, rec.sign_set) if getattr(rec, "sign_set", None) else rec
    for prefix, _p, _kw in host.labels:
        parts = [n for n in table if n.startswith(prefix)]
        faces = cs2_compose.faces_of_prefix(me, table, prefix)
        isl = cs2_uv.uv_islands(me, lay, faces=faces)
        want = 16 if prefix.startswith("emblem_") and prefix.endswith(("_p1_face", "_hq_face")) else 1
        assert len(parts) == 1 and len(faces) == want and len(isl) == 1, \
            f"{key} D3: {prefix}: {len(parts)} parts / {len(faces)} faces / {len(isl)} islands"
        rects[prefix] = cs2_compose.uv_bbox_px(me, table, prefix, A)
    names = sorted(rects)
    for i, a in enumerate(names):
        for b in names[i + 1:]:
            ra, rb = rects[a], rects[b]
            assert ra[1] <= rb[0] or rb[1] <= ra[0] or ra[3] <= rb[2] or rb[3] <= ra[2], \
                f"{key} D3: stamp rects of {a} {ra} and {b} {rb} overlap"
    info = man.get("labels", {})
    tiers = []
    # fine-detail step 3d: the word boards are on the shared sheet (300 px/m): T1 from the owner. Nitpick C (2026-10-13,
    # build/nitpick-c-lodges.md): their typeface is cs2_signset.NC_UTILITY_FACE (Franklin Gothic Demi Cond), was Bfont
    om = json.load(open(os.path.join(SS.owner_dir_shipped(), f"{SS.SHEET_OWNER}.manifest.json"), encoding="utf-8"))
    for kk, words in SPEC[key]["boards"]:
        met = om["sheet"]["metrics"][kk]
        d = [x for x in met["draws"] if x["kind"] == "text"][0]
        assert d["tier"] == "T1" and d["cap_m"] >= 0.40 and d["stroke_px"] >= 4.0 and \
            " / ".join(d["lines"]) == words and d["font"] == SS.typeface(SS.NC_UTILITY_FACE).name, (kk, d)
        tiers.append(f"{kk} {words} cap {d['cap_m']:.3f} m stroke {d['stroke_px']:.2f} px @ {met['ppm']:.0f} "
                     f"px/m T1 (sheet, vector, {d['font']})")
    for c in SPEC[key]["crosses"]:
        d = [x for x in info[c]["draws"] if x["kind"] == "cross_square"][0]
        assert d["arm_width_px"] >= 4.0, (c, d)
        tiers.append(f"{c} arm {d['arm_width_m']:.2f} m = {d['arm_width_px']:.1f} tx")
    l2 = lods[1]
    present2 = names_present(l2, table)
    for c in SPEC[key]["crosses"]:
        assert c in present2, f"{key} D3: roof cross {c} not on LOD2"
    ok(f"{key} D3: {len(rects)} labels, 1 part / 1 island each, stamp rects disjoint; "
       f"{'; '.join(tiers)}; roof cross on LOD2")
    return rects


def d6_budgets(key, rec, res, man):
    sp = SPEC[key]
    t = res["tris"]
    assert t[0] <= sp["lod0_cap"], f"{key} D6: LOD0 {t[0]} > {sp['lod0_cap']}"
    assert t[1] <= min(rec.budget.lod1, 0.5 * t[0]), f"{key} D6: LOD1 {t[1]}"
    assert t[2] <= rec.budget.lod2_cap, f"{key} D6: LOD2 {t[2]}"
    d = man["shipped_density"]
    assert d["median_w1"] >= sp["ppm_w1"], f"{key} D6: w=1 {d['median_w1']} < {sp['ppm_w1']}"
    fr = ""
    if "frames_w" in sp:
        f = d["by_weight"][f"{sp['frames_w']:g}"]
        assert f >= sp["frames_ppm"], f"{key} D6: frames {f} < {sp['frames_ppm']}"
        fr = f", frames (w {sp['frames_w']}) {f} >= {sp['frames_ppm']}"
    assert res["tex"] == rec.tex == 1024, res["tex"]
    assert man["batches_per_mesh_group"] == 9 and man["vram_mb"] == 7.0, \
        (man["batches_per_mesh_group"], man["vram_mb"])
    ok(f"{key} D6: tris {t[0]} / {t[1]} / {t[2]} (caps {sp['lod0_cap']} / {rec.budget.lod1} / "
       f"{rec.budget.lod2_cap}), w=1 {d['median_w1']} >= {sp['ppm_w1']} px/m{fr}, tex 1024, "
       f"batches 9, {man['vram_mb']} MB; pack {man['pack_margin']['islands']} islands, margin "
       f"{man['pack_margin']['margin_px']} px")


def d7_d8_base(key, rec, res, obj, table):
    for lvl in (0, 1, 2):
        assert res["base"][lvl]["verdict"] == "OK", f"{key} D7: LOD{lvl} {res['base'][lvl]}"
    V, polys = authoring(obj)
    pv = part_ids(obj)
    zmin = {}
    for i, (_n, vs) in enumerate(polys):
        nm = table[pv[i] - 1]
        zmin[nm] = min(zmin.get(nm, 1e9), float(V[vs, 2].min()))
    low = [(n, round(zmin[n], 3)) for n in SPEC[key]["new"] if zmin.get(n, 1e9) < K.GROUND_LIFT - 1e-6]
    assert not low, f"{key} D7: new parts in the ground band: {low}"
    lot = res["lot"]
    assert lot["margin_x"] >= 0.25 - 1e-6 and lot["margin_y"] >= 0.25 - 1e-6, lot
    fc = res["facing"]
    ok(f"{key} D7/D8: engine base OK on LOD0-2, new parts start >= {K.GROUND_LIFT} m; lot margins "
       f"x {lot['margin_x']} / y {lot['margin_y']} m, arrival centroid x {fc['arrival_centroid_x']} "
       f">= {fc['need']}")


def make_lods(rec, res, obj):
    table = table_of(obj)
    (t1, t2), _plan = lodge.plan_lod_targets(obj, table, rec)
    d1, d2 = lodge.delete_lists(rec, table)
    lods, infos = K.lod_chain(obj, res["name"] + "T", (t1, t2), (d1, d2), rec.ground_prefixes)
    for lvl, lod in enumerate(lods, start=1):
        h = lodge.mesh_hash(lod)
        assert h == res["hashes"][f"LOD{lvl}"], f"rebuilt LOD{lvl} {h} != shipped {res['hashes']}"
    return lods, infos, (d1, d2)


def d9_lods(key, rec, res, obj, table, lods, infos, dl):
    sp = SPEC[key]
    d1, d2 = dl
    p1, p2 = names_present(lods[0], table), names_present(lods[1], table)
    gone1 = [n for n in sp["new"] if n not in sp["lod1_kept"]]
    bad1 = [n for n in gone1 if not n.startswith(d1) or n in p1]
    assert not bad1, f"{key} D9: on LOD1 / not in its delete list: {bad1}"
    gone2 = [n for n in sp["new"] if n not in sp["lod2_kept"]]
    bad2 = [n for n in gone2 if not n.startswith(d2) or n in p2]
    assert not bad2, f"{key} D9: on LOD2 / not in its delete list: {bad2}"
    back = [n for n in table if n.startswith(d1) and not n.startswith(d2)]
    assert not back, f"{key} D9: LOD1 deletes but LOD2 keeps {back}"
    kept1 = [n for n in sp["lod1_kept"] if n in p1]
    if key == "P2":
        assert "emblem_hq_face" not in p2 and "emblem_hq_rim" not in p2, "P2 D9: disc on LOD2 (M7)"
        # detail pass 1b (check-patrol F1): the name board keeps its FRAME at LOD1 (the face floated 0.08 m without it).
        # The two decorative gable struts pay for it, LOD1 stays <= 650
        p0 = names_present(obj, table)
        assert "sign_hqname_frame" in p1, "P2 D9: name board frame (its face is in the sign set)"
        assert {"beam_strutL", "beam_strutR"} <= p0 and not ({"beam_strutL", "beam_strutR"} & p1),             "P2 D9: the gable struts must be on LOD0 and gone at LOD1"
        assert "sign_hqname_frame" not in p2, "P2 D9: the frame goes at LOD2 with the face"
        assert infos[0]["tris"] <= rec.budget.lod1, f"P2 D9: LOD1 {infos[0]['tris']} > {rec.budget.lod1}"
    assert infos[1]["ratio"] == 1.0, f"{key} D9: LOD2 collapsed ({infos[1]})"
    ok(f"{key} D9: {len(gone1)} new parts gone at LOD1 (kept {kept1}), {len(gone2)} gone at LOD2 "
       f"(kept {sp['lod2_kept']}); nothing LOD1 deletes returns at LOD2; LOD2 not collapsed "
       f"(achievable {infos[1]['tris']}){'; front disc off LOD2 (M7)' if key == 'P2' else ''}")


def d10_props(key, rec, obj, table):
    V, polys = authoring(obj)
    pv = part_ids(obj)
    tris = []
    for i, (n, vs) in enumerate(polys):
        for k in range(1, len(vs) - 1):
            tris.append((i, n, V[[vs[0], vs[k], vs[k + 1]]]))
    rows = []
    boxes = []
    for e in rec.sub_objects:
        if isinstance(e, dict):
            # fine-detail step 3d: lean kit rows are D11's (the real mesh). The hose reel is a wall prop here
            if e.get("lean") is not None or e.get("role") == "signset":
                continue
            prefab, guid, pos, q, parent = e["prefab"], e["m_Object"], e["position"], e["rotation"], \
                e["m_ParentMesh"]
        else:
            prefab, guid, pos, q, parent = e
        P = g2a(pos)
        ex, ey, ez = (g2a(quat_rot(q, a)) for a in ((1, 0, 0), (0, 1, 0), (0, 0, 1)))
        lo, hi = (np.array(v, float) for v in PROP_BOUNDS[prefab])
        deck = prefab in DECK_PROPS
        mount = ey if deck else ez                      # the surface normal the prop sits on
        hosts = []
        for i, n, pts in ((i, n, V[vs]) for i, (n, vs) in enumerate(polys)):
            if float(n @ mount) < 0.999:
                continue
            dist = float((P - pts[0]) @ mount)
            if abs(dist) <= (0.01 if deck else CLEAR_M) and _point_in_poly(P, pts, mount):
                hosts.append((table[pv[i] - 1], round(dist, 4)))
        assert hosts, f"{key} D10: {prefab} at {tuple(P)} sits on none of our faces " \
                      f"({'deck top +-0.01' if deck else 'wall <= 0.05'})"
        # the box: inflated by CLEAR_M, in front of / above the mounting surface only
        lo2, hi2 = lo - CLEAR_M, hi + CLEAR_M
        if deck:
            lo2[1] = 0.005
        else:
            lo2[2] = 0.005
        cl = (lo2 + hi2) / 2
        h = (hi2 - lo2) / 2
        axes = np.stack([ex, ey, ez])
        c = P + axes.T @ cl
        hits = []
        for i, n, tri in tris:
            if float(n @ mount) > 0.999 and abs(float((tri[0] - P) @ mount)) <= 0.011:
                continue                                 # the mounting surface itself
            if _tri_box(tri, c, axes, h):
                hits.append(table[pv[i] - 1])
        assert not hits, f"{key} D10: {prefab} at {tuple(np.round(P, 3))} within {CLEAR_M} m " \
                         f"of {sorted(set(hits))}"
        rows.append(f"{prefab.replace('Random01', '')} on {hosts[0][0]}")
        corners = np.array([P + axes.T @ np.array((x, y, z)) for x in (lo[0], hi[0]) for y in (lo[1], hi[1])
                            for z in (lo[2], hi[2])])
        boxes.append((prefab, corners.min(0), corners.max(0)))
    # step 3d: no two rows overlap (vanilla + kit wall rows by their boxes, kit lean rows by their real mesh)
    L = PT.load_lean()
    for e in PT.lean_rows(rec):
        A, _t = L.row_authoring(e)
        boxes.append((e["lean"]["replaces"], A.min(0), A.max(0)))
    clash = [(a[0], b[0]) for i, a in enumerate(boxes) for b in boxes[i + 1:]
             if np.all(a[1] < b[2]) and np.all(b[1] < a[2])]
    assert not clash, f"{key} D10: rows overlap {clash}"
    ok(f"{key} D10: {len(rows)} props on our surfaces, facing their normals, >= {CLEAR_M} m clear "
       f"of every other face (variant-union bounds): {rows}; {len(boxes)} rows pairwise clear")


def _remove_new_objects(keep):
    for o in [o for o in bpy.data.objects if o.name not in keep]:
        me = o.data if o.type == "MESH" else None
        bpy.data.objects.remove(o, do_unlink=True)
        if me is not None and me.users == 0:
            bpy.data.meshes.remove(me)


def d11_lean(key, rec):
    """CONTRACT LEAN (lean fix, fifteenth look), measured on a fresh build of the
    recipe's parts (the builder runs the contract itself) + the negative cases. Step 3d: also on the
    kit lean rows (the real kit mesh), with their own negatives."""
    from mathutils import Vector
    sp = SPEC[key]
    L = PT.load_lean()
    opts = {"tower": True, "roof_windows": "win"}
    keep = set(bpy.data.objects.keys())
    try:
        parts = lodge.build_parts(rec, opts)
        rout = d11_rows(key, rec, parts, L)
        rows = L.measure(parts, PT.LEAN_PREFIXES)
        names = sorted(r["name"] for r in rows)
        assert names == sorted(sp["lean"]), f"{key} D11: leaning parts {names} != {sorted(sp['lean'])}"
        out = []
        for r in rows:
            assert "error" not in r, f"{key} D11: {r}"
            want = sp["lean"][r["name"]]
            assert r["ok"] and r["support"] == want and r["cover"] >= L.COVER and \
                -L.GAP_PEN <= r["gap"] <= L.GAP_MAX, f"{key} D11: {L.fmt(r)} (support must be {want})"
            assert r["foot"] in sp["lean_foot"] and r["foot_cover"] >= L.COVER and \
                -L.GAP_PEN <= r["foot_gap"] <= L.GAP_MAX + 1e-4, f"{key} D11 foot: {L.fmt(r)}"
            out.append(f"{r['name']} on {r['support']} {r['gap']:+.3f} m {100 * r['cover']:.0f} % "
                       f"z {r['z']:.2f}, foot {r['foot']} {r['foot_gap']:+.3f}")
        # negative cases: 0.10 m off its support along the lean normal, then 0.05 m lifted
        objs = {o.name: o for o, _k in parts}
        neg = []
        for r in rows:
            o = objs[r["name"]]
            home = o.location.copy()
            for d in (Vector((r["n"][0], r["n"][1], 0.0)) * 0.10, Vector((0.0, 0.0, 0.05))):
                o.location = home + d
                try:
                    L.check(parts, PT.LEAN_PREFIXES, f"{key}-negative")
                    raise AssertionError(f"{key} D11: {r['name']} moved by {tuple(d)} still passes")
                except RuntimeError as e:
                    assert "CONTRACT LEAN" in str(e) and r["name"] in str(e), e
                finally:
                    o.location = home
            neg.append(r["name"])
    finally:
        _remove_new_objects(keep)
    # the builder fails the build: (legacy) its rest gap sabotaged to 0.10 m. (fine) its kit rows 0.10 m off
    saved = PT.REST_GAP
    PT.REST_GAP = 0.10
    try:
        (PT.build_p1 if key == "P1" else PT.build_p2)(FD.effective(rec, fine=False), opts)
        raise AssertionError(f"{key} D11: the legacy builder passed with a 0.10 m rest gap")
    except RuntimeError as e:
        assert str(e).startswith(f"CONTRACT LEAN {key}:"), e
        sab = str(e).split(": ", 2)[-1]
    finally:
        PT.REST_GAP = saved
        _remove_new_objects(keep)
    off = replace(rec, sub_objects=tuple(_shift_row(e, 0.10) if isinstance(e, dict) and e.get("lean") else e
                                         for e in rec.sub_objects))
    try:
        (PT.build_p1 if key == "P1" else PT.build_p2)(off, opts)
        raise AssertionError(f"{key} D11: the fine builder passed with its kit rows 0.10 m off")
    except RuntimeError as e:
        assert str(e).startswith(f"CONTRACT LEAN {key}:") and "kit row" in str(e), e
        sabf = str(e).split(": ", 2)[-1]
    finally:
        _remove_new_objects(keep)
    ok(f"{key} D11 LEAN: {len(rows)} leaning parts rest on their declared support (gap <= "
       f"{L.GAP_MAX} m over >= {L.COVER:.0%} of the width) and stand on {sp['lean_foot']}: "
       f"{out}; moved 0.10 m off / lifted 0.05 m -> check raises for {neg}; legacy builder with a 0.10 m "
       f"rest gap fails ({sab[:100]}); kit rows: {rout}; fine builder with the rows 0.10 m off fails "
       f"({sabf[:100]})")


def _shift_row(e, d=0.0, lift=0.0):
    """A copy of a lean row moved d m along its support's outward normal (off it) and lifted `lift` m."""
    nx, nz = e["lean"]["wall_normal"]
    x, y, z = e["position"]
    return dict(e, position=(x + nx * d, y + lift, z + nz * d))


def d11_rows(key, rec, parts, L):
    """The kit lean rows on the REAL kit mesh: declared support = the modeled part's, gap <= GAP_MAX over >= COVER, foot on
    grade / the deck, no host overlap. Negatives (each must raise CONTRACT LEAN)."""
    sp = SPEC[key]
    rows = PT.lean_rows(rec)
    res = L.check_rows(parts, rows, f"{key}-test", PT.LEAN_PREFIXES, expect=sorted(sp["lean_rows"]))
    out = []
    for m in res:
        want = sp["lean_rows"][m["replaces"]]
        assert m["ok"] and m["support"] == want == m["declared"] and not m["overlaps"] and \
            -L.GAP_PEN <= m["gap"] <= L.GAP_MAX and m["foot"] in sp["lean_foot"] and \
            -L.GAP_PEN <= m["foot_gap"] <= L.GAP_MAX + 1e-4, f"{key} D11 row: {L.fmt(m)} (support must be {want})"
        out.append(f"{m['prefab']}({m['replaces']}) on {m['support']} {m['gap']:+.3f} m "
                   f"{100 * m['cover']:.0f} % z {m['z']:.2f}, foot {m['foot']} {m['foot_gap']:+.3f}")
    negs = {"0.10 m off": lambda e: _shift_row(e, 0.10), "lifted 0.05 m": lambda e: _shift_row(e, 0.0, 0.05),
            "0.03 m into its support": lambda e: _shift_row(e, -0.03),
            "a foreign support": lambda e: dict(e, lean=dict(e["lean"], support="logpost_0"))}
    caught = 0
    for i, e in enumerate(rows):
        for tag, fn in negs.items():
            bad = rows[:i] + (fn(e),) + rows[i + 1:]
            try:
                L.check_rows(parts, bad, f"{key}-negative", PT.LEAN_PREFIXES)
                raise AssertionError(f"{key} D11: row {e['lean']['replaces']} {tag} still passes")
            except RuntimeError as err:
                assert "CONTRACT LEAN" in str(err) and e["lean"]["replaces"] in str(err), err
                caught += 1
    return f"{out}; {caught} negatives raise ({len(rows)} rows x {list(negs)})"


def d12_signset(key, rec, res, man, tmp):
    """The sign set (fine-detail step 3d): SG1 UV = the painted rect, SG2 boards within 1 mm of the --no-fine host's own
    label quads, SG3 none left in LOD0-2 / labels, SG4 one identity row (staged CID), SG5 inside the lot, SG6 <= 400
    tris. The sharer folder = five owner keys, no PNG."""
    spec = rec.sign_set
    sobj = res["sign_set"]["obj"]
    names = spec.face_names()
    bad = SS.sg1_uv_in_rect(sobj, spec)
    assert not bad, f"{key} D12 SG1 {bad}"
    # SG2 against the legacy (--no-fine) host's label quads, game frame (export (x, y, z) -> (x, z, -y))
    keep = set(bpy.data.objects.keys())
    try:
        leg = lodge.build_parts(FD.effective(rec, fine=False), {"tower": True, "roof_windows": "win"})
        objs = {o.name: o for o, _k in leg}

        def game(o):
            W = K._world_coords(o)                               # authoring (pre-orient)
            B = np.column_stack([W[:, 1], -W[:, 0], W[:, 2]])   # orient_for_net: (x, y) -> (y, -x)
            return np.column_stack([-B[:, 0], B[:, 2], -B[:, 1]])   # gx negated (x-sign gate N), as SS.game_coords
        host_q = {f: game(objs[f]) for f in names}
    finally:
        _remove_new_objects(keep)
    me = sobj.data
    table = json.loads(me[cs2_asset.PART_TABLE_PROP])
    pv = np.zeros(len(me.polygons), np.int32)
    me.attributes["part"].data.foreach_get("value", pv)
    G = SS.game_coords(sobj)
    set_q = {table[pv[p.index] - 1]: G[list(p.vertices)] for p in me.polygons}
    bad = SS.sg2_positions(set_q, host_q)
    assert not bad, f"{key} D12 SG2 {bad}"
    moved = {f: q + np.array((0.002, 0.0, 0.0)) for f, q in set_q.items()}
    assert SS.sg2_positions(moved, host_q), f"{key} D12 SG2 negative (2 mm) not caught"
    tables = {"LOD0": table_of(bpy.data.objects[res["name"]])}
    labels = [e[0] for e in SS.host_recipe(rec, spec).labels]
    bad = SS.sg3_no_routed_left(tables, labels + list(man.get("labels", {})), spec)
    assert not bad, f"{key} D12 SG3 {bad}"
    cid = FD.staged_cid(spec.prop, spec.letter, kind="signs")
    want_cid = cid or SS.PLACEHOLDER_CID
    assert not SS.sg4_identity_row(man["sub_objects"], spec, want_cid), f"{key} D12 SG4"
    bounds = SS.bounds_game(sobj)
    W_, D_ = rec.lot
    assert not SS.sg5_in_lot(bounds, (4.0 * W_, 4.0 * D_)), f"{key} D12 SG5 {bounds}"
    n = cs2_asset.tri_count(sobj)
    assert not SS.sg6_tris(n) and n == 2 * len(names) == 2 * SPEC[key]["sign_faces"], f"{key} D12 SG6 {n}"
    sd = os.path.join(os.path.dirname(res["asset_dir"]), os.pardir, "signs", spec.letter, spec.mesh)
    sett = json.load(open(os.path.join(sd, "settings.json"), encoding="utf-8"))
    want = {f"{spec.mesh}_{m}.png": f"../{SS.SHEET_OWNER}/{SS.SHEET_OWNER}_{m}.png" for m in SS.MAPS}
    assert sett == {"sharedAssets": want} and not glob.glob(os.path.join(sd, "*.png")), sett
    ok(f"{key} D12 sign set {spec.mesh}: {len(names)} faces {names} -> {[k for _f, k in spec.faces]}; SG1 UV = "
       f"rect, SG2 <= 1 mm from the --no-fine host quads (2 mm negative caught), SG3 none left, SG4 one identity "
       f"row CID {want_cid[:8]}, SG5 {bounds} in the lot, SG6 {n} tris; sharer settings = the five owner keys")


def d13_legacy(key, rec, tmp):
    """`--no-fine` = the approved shack: blockout LOD0_prebake / Win of fine_detail.effective(fine=False)."""
    args = lodge.parse_args(["--recipe", rec.key, "--blockout", "--no-fine", "--out",
                             os.path.join(tmp, key, "legacy", rec.letter)])
    r = lodge.run(rec, args)
    got = (r["hashes"]["LOD0_prebake"], r["hashes"]["Win"])
    assert got == LEGACY_HASH[key], f"{key} D13: --no-fine blockout {got} != the approved {LEGACY_HASH[key]}"
    ok(f"{key} D13 legacy: --no-fine blockout {got} = the approved shack (lean-fix export)")


def d14_stale(key, rec, tmp):
    """lodge.remove_stale_maps on a finish: seeded stale maps go, a foreign PNG stays, neither stale name is in
    settings.json or the manifest's files."""
    d = os.path.join(tmp, key, "stale", rec.letter)
    name = f"{rec.asset}{rec.letter}"
    ad = os.path.join(d, name)
    os.makedirs(ad, exist_ok=True)
    src = glob.glob(os.path.join(REPO, "assets", "import", rec.category, rec.letter, name, f"{name}_Normal.png"))[0]
    seeds = (f"{name}_LOD3_BaseColor.png", f"{name}_Emissive2.png")      # LOD3: no level 3 is built
    for f in seeds + ("Other_BaseColor.png",):
        shutil.copy2(src, os.path.join(ad, f))
    args = lodge.parse_args(["--recipe", rec.key, "--no-validate", "--tex", "1024", "--ao-samples", "2", "--out", d])
    r = lodge.run(rec, args)
    left = sorted(os.listdir(ad))
    assert not any(f in left for f in seeds) and "Other_BaseColor.png" in left, left
    assert sorted(r["bake"].get("stale_maps_removed", [])) == sorted(seeds), r["bake"].get("stale_maps_removed")
    sett = open(os.path.join(ad, "settings.json"), encoding="utf-8").read()
    man = json.load(open(os.path.join(ad, f"{name}.manifest.json"), encoding="utf-8"))
    assert not any(f in sett for f in seeds) and not any(f in man["files"] for f in seeds), "stale names listed"
    assert sorted(man["bake"]["stale_maps_removed"]) == sorted(seeds)
    # unit-level: an _Emissive this build no longer writes goes. The build's own maps and non-maps stay
    u = os.path.join(tmp, key, "stale_unit")
    os.makedirs(u, exist_ok=True)
    for f in (f"{name}_BaseColor.png", f"{name}_Emissive.png", f"{name}.fbx", "settings.json", f"{name}_x.png"):
        open(os.path.join(u, f), "wb").write(b"x")
    gone = lodge.remove_stale_maps(u, name, [f"{name}_BaseColor.png"])
    assert gone == [f"{name}_Emissive.png"] and sorted(os.listdir(u)) == sorted(
        [f"{name}_BaseColor.png", f"{name}.fbx", "settings.json", f"{name}_x.png"]), (gone, os.listdir(u))
    ok(f"{key} D14 stale maps: {list(seeds)} seeded -> removed by the finish (Other_BaseColor.png kept), in neither "
       f"settings.json nor the manifest's files; unit: a stale {name}_Emissive.png removed, maps written / FBX / "
       f"settings / a stray PNG kept")


# ======================================================================================
# cross-build checks (D4, D5) and the shipped export
# ======================================================================================

def d4_d5(key, rec, man1, png1, man2, png2, res1, res2, manN, pngN, rects):
    w = man1["weather"]
    v = w["vanilla"]
    assert v["ok"] and not v["failed"], f"{key} D4: vanilla budget {v['failed']}"
    assert w["intensity"] == rec.weather["intensity"] and w["slot_wear"] == "free" and \
        w["snow"] == 0.0 and v.get("VL5_snow_share", 0.0) == 0.0, \
        {k: w[k] for k in ("intensity", "slot_wear", "snow")}
    g = w["guards"]
    assert g["G5_ctl_identity"] and g["G6_protect_identical"], g
    assert sha1_file(png1["ControlMask"]) == sha1_file(pngN["ControlMask"]), \
        f"{key} D4: ControlMask differs from the weather=None finish"
    b1, bN = png(png1["BaseColor"]), png(pngN["BaseColor"])
    for prefix, r in rects.items():
        assert np.array_equal(b1[r[0]:r[1], r[2]:r[3]], bN[r[0]:r[1], r[2]:r[3]]), \
            f"{key} D4: label rect {prefix} {r} changed under the weathering"
    assert sha1_file(png1["BaseColor"]) != sha1_file(pngN["BaseColor"]), f"{key} D4: no weathering"
    ok(f"{key} D4: weather {w['intensity']} (free slots, snow 0): vanilla ok, VL1 "
       f"{v['VL1_mean_rel_dL']} VL2 {v['VL2_share_rel_dL_gt_0.15']} VL4 {v['VL4_max']}; "
       f"ControlMask == weather None; {len(rects)} label rects identical")
    s1 = {k: sha1_file(p) for k, p in sorted(png1.items())}
    s2 = {k: sha1_file(p) for k, p in sorted(png2.items())}
    assert s1 == s2, f"{key} D5: PNGs {s1} vs {s2}"
    hk = ("LOD0_prebake", "Win", "LOD0", "LOD1", "LOD2")
    h1 = {k: res1["hashes"][k] for k in hk}
    h2 = {k: res2["hashes"][k] for k in hk}
    assert h1 == h2, f"{key} D5: hashes {h1} vs {h2}"
    ok(f"{key} D5: two finishes in one process identical: {len(s1)} PNGs, {h1}")
    return s1, h1


def check_export(key, rec, name, s1, h1):
    d = os.path.join(REPO, "assets", "import", rec.category, rec.letter, name)
    man = json.load(open(os.path.join(d, name + ".manifest.json"), encoding="utf-8"))
    hs = {k: man["mesh_hash"][k] for k in h1}
    assert hs == h1, f"{key} export: mesh hashes {hs} != tested {h1}"
    ss = {k: sha1_file(os.path.join(d, f"{name}_{k}.png")) for k in s1}
    assert ss == s1, f"{key} export: PNGs {ss} != tested {s1}"
    frames = glob.glob(os.path.join(REPO, "assets", "preview", rec.category, f"{name}_lod*_xor*pct.png"))
    xor = {}
    for f in frames:
        m = re.search(r"_lod(\d)_(\d+)m_xor([\d.]+)pct\.png$", f)
        if m:
            xor.setdefault(int(m.group(1)), []).append((int(m.group(2)), float(m.group(3))))
    assert 1 in xor and 2 in xor, f"{key} export: LOD preview frames {sorted(xor)}"
    # the gate reads the frame AT the switch (the farther of the two. The nearer one is half the switch distance, where
    # that LOD is never shown). Design D9: <= 8 %, P2 <= 9 % (WARN)
    at = {lvl: max(v)[1] for lvl, v in xor.items()}
    gate, warn = (9.0, 8.0) if key == "P2" else (8.0, 8.0)
    assert at[2] <= gate, f"{key} D9: preview LOD2 XOR at the switch {at[2]} % > {gate} %"
    flag = f" WARN (> {warn} %, inside the P2 allowance {gate} %)" if at[2] > warn else ""
    ok(f"{key} export: shipped folder = the tested build (hashes + 5 PNGs); preview XOR at the "
       f"switch LOD1 {at[1]} % / LOD2 {at[2]} % (<= {gate}){flag}; all frames "
       f"{ {lvl: sorted(v) for lvl, v in xor.items()} }")


# ======================================================================================

def run_recipe(key, tmp):
    rec = R.get_recipe(key)
    t0 = time.perf_counter()
    res1, png1, man1 = finish(rec, os.path.join(tmp, key, "w1"))
    obj = bpy.data.objects[res1["name"]]
    table = table_of(obj)
    lods, infos, dl = make_lods(rec, res1, obj)
    d1_counts(key, rec, res1, man1, obj, table)
    d2_emissive(key, rec, res1, man1, obj, table, png1)
    rects = d3_labels(key, rec, res1, man1, obj, table, lods)
    d6_budgets(key, rec, res1, man1)
    d7_d8_base(key, rec, res1, obj, table)
    d9_lods(key, rec, res1, obj, table, lods, infos, dl)
    d10_props(key, rec, obj, table)
    d11_lean(key, rec)
    d12_signset(key, rec, res1, man1, tmp)
    res2, png2, man2 = finish(rec, os.path.join(tmp, key, "w2"))
    resN, pngN, manN = finish(replace(rec, weather=None), os.path.join(tmp, key, "none"))
    s1, h1 = d4_d5(key, rec, man1, png1, man2, png2, res1, res2, manN, pngN, rects)
    if EXPORT:
        check_export(key, rec, res1["name"], s1, h1)
    d13_legacy(key, rec, tmp)
    if key == "P1":
        d14_stale(key, rec, tmp)
    print(f"[test] {key}: {time.perf_counter() - t0:.0f} s")


def main():
    t0 = time.perf_counter()
    tmp = tempfile.mkdtemp(prefix="detail_patrol_")
    try:
        for key in ("P1", "P2"):
            if ONLY and key != ONLY:
                continue
            run_recipe(key, tmp)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    print(f"PASS test_detail_patrol: {len(CHECKS)} checks ({', '.join(k for k in ('P1', 'P2') if not ONLY or k == ONLY)}"
          f"{', export' if EXPORT else ''}) ({time.perf_counter() - t0:.0f} s)")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        print("FAIL test_detail_patrol")
        sys.exit(1)
