"""
test_ingest.py - cs2_ingest contract test (headless Blender 5.2).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_ingest.py

Sources are the archived originals in assets/external/ (ROUND-SPEC R20; binaries are
gitignored, so on a fresh clone copy them back from SOURCE.md's download first).

Asserts (expected figures from understand/ref-models.md sec 1.2-1.4, sec 1.6, sec 2.1-2.3 and
ref-models-skeptic verdicts 1, 4, 5, 7, 8, 12, 14, miss 9):
  Kagelok .blend   58 meshes kept, the BezierCurve.001 path curve dropped, 30 constraints
                   baked away (10 chairs x 2 + 10 grips), nothing parented / constrained /
                   animated / hidden, 39,870 tris; chair "05" world extent 6.72 x 2.51 x
                   8.50 u (+-1 %); grip D05 top - chair 05 lowest = 9.091 u (+-0.5 %); the
                   line + entry tower head kits present (line kit 2,058 tris, its 16
                   sheaves centered at x = +-7.5 u).
  textures         30 of 33 used materials matched; exactly cable / gouttiere / val cenis
                   tex unmatched; keep= raises for them, an override clears it; the
                   aliases, the "et" drop, case-only collisions and truncated _DIS names
                   resolve; every one of the 117 files belongs to exactly one set and no
                   set borrows another set's roughness (the cs2_asset._load_map glob trap);
                   gray, gray+alpha, palette, RGB and RGBA PNGs load as (h, w, 4).
  gondola .fbx     522 meshes / 36,557 tris, 187 empties dropped; the cabin.001 subtree
                   filter; fence calibration factor 27 +-10 %; body "cabin" x27 =
                   2.62 x 2.31 x 1.82 m and the cabin.001 subtree x27 = 2.63 x 2.36 x 1.98 m.
  clean            chair 05: 0 n-gons (25 triangulated), 1 Material Output per material
                   targeting ALL, no color attributes, extents unchanged; gondola body:
                   the FBX custom normals and sharp_edge marks are cleared.
  frame / parts    reframe puts the lowest point at z 0 and the grip on the Z axis;
                   rescale 0.30 m/u gives a 2.727 m drop; to_parts splits 15 + 7 loose
                   parts, a classify rule moves the 2 small grip parts to griphw_NN, a bad
                   classify prefix raises and leaves the sources; tag_parts + join +
                   reduce(900, delete griphw_) lands +-25 %, deletes those parts, keeps the
                   mesh name (no .001) and ends weighted-shaded (custom normals).
  edge cases       a mirrored transform and an inside-out box both end outward; recalc
                   "none" keeps the inside-out box, an open reversed grid keeps its source
                   winding by default; a filtered .blend load (05 + D05) evaluates its
                   constraint targets, then drops them with their curve / action / mesh, and
                   restores the frame (37), the selection and the active object; an unknown
                   name raises and leaves the session untouched (.blend and .fbx).
  winding          chairs 01 / 03 / 05 / 07 (one local mesh, four poses) clean to the same
                   directed source edges and the same flips (recalc "all" flipped 4/34/0/0).
  fbx dedup        a second load of gondola.fbx while its objects are loaded, or with a
                   clashing name in the session, raises and leaves nothing behind.
  determinism      two load + clean runs of the whole Kagelok file in one process hash
                   identically (vertices + faces), with discard() between them.
NOTE the round text asked for chair 05 = 6.72 x 2.54 x 8.52 u and the cabin.001 subtree =
2.43 x 2.18 x 1.83 m at x27. Measured: 6.72 x 2.51 x 8.50 (= ref-models sec 1.4; 2.54 is
-1.01 % off) and 2.63 x 2.36 x 1.98 m (the round's figures are this subtree at x24.95). The
test pins the measured values and prints the comparison.
Prints one PASS line and exits 0, or lists the failures and exits 1.
"""

import collections
import hashlib
import os
import struct
import sys
import time
import traceback

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

import cs2_asset    # noqa: E402
import cs2_ingest   # noqa: E402

EXTERNAL = os.path.join(os.path.dirname(os.path.dirname(HERE)), "external")
KAGELOK = os.path.join(EXTERNAL, "kagelok-chair-lift", "source",
                       "Sketchfab_2017_09_02_17_41_10.blend")
KAGELOK_TEX = os.path.join(EXTERNAL, "kagelok-chair-lift", "textures")
GONDOLA = os.path.join(EXTERNAL, "wilin-gondola", "source", "gondola.fbx")

# Non-ASCII names come from the source data (French material / object names).
GOUTIERE = "goutière"
SIEGE = "siège"
LINE_KIT = ("Pil Fix Roues Haut", "Roues Pil Haut", "Plateforme Pil Haut",
            "Barriére Pil Haut")
ENTRY_KIT = ("Pil Fix Roues Bas", "Roues Pil bas", "Plateforme pil Bas", "Barrière Pil Bas")

FAILS = []


def check(cond, msg):
    print(("  ok    " if cond else "  FAIL  ") + msg)
    if not cond:
        FAILS.append(msg)
    return cond


def near(a, b, rel):
    return abs(a - b) <= rel * abs(b)


def extent(objs):
    lo, hi = cs2_ingest.world_bounds(objs)
    return hi - lo


def mesh_hash(objs):
    h = hashlib.sha1()
    for o in sorted(objs, key=lambda o: o[cs2_ingest.SRC_NAME_PROP]):
        me = o.data
        h.update(o[cs2_ingest.SRC_NAME_PROP].encode("utf-8"))
        for v in me.vertices:
            h.update(("%.5f %.5f %.5f;" % tuple(v.co)).encode())
        for p in me.polygons:
            h.update((",".join(str(i) for i in p.vertices) + ";").encode())
    return h.hexdigest()[:12]


def png_colour_type(path):
    with open(path, "rb") as f:
        head = f.read(26)
    if head[:8] != b"\x89PNG\r\n\x1a\n" or head[12:16] != b"IHDR":
        return None
    return head[25]


def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.curves, bpy.data.actions):
        for item in list(block):
            if item.users == 0:
                block.remove(item)
    for c in list(bpy.data.collections):
        if c.name.startswith(cs2_ingest.COLLECTION_PREFIX):
            bpy.data.collections.remove(c)


# --------------------------------------------------------------------------------------

def test_kagelok_load():
    print("[test] Kagelok load")
    rep = {}
    objs = cs2_ingest.load_external(KAGELOK, report=rep)
    by = {o.name: o for o in objs}
    check(len(objs) == 58 and all(o.type == "MESH" for o in objs),
          f"58 mesh objects kept (got {len(objs)})")
    check(rep["dropped"] == [("BezierCurve.001", "CURVE")],
          f"only the path curve dropped (got {rep['dropped']})")
    check(not any(o.type == "CURVE" for o in bpy.data.objects), "no curve object left in the scene")
    check(rep["constraints_baked"] == 30, f"30 constraints baked (got {rep['constraints_baked']})")
    check(rep["unhidden"] == 58, f"58 objects unhidden (got {rep['unhidden']})")
    check(rep["tris"] == 39870, f"39,870 tris (got {rep['tris']})")
    check(all(not o.constraints and o.parent is None and o.animation_data is None
              and not o.hide_viewport and not o.hide_get() for o in objs),
          "no constraint / parent / animation / hidden flag survives")
    check(all(o.matrix_basis == o.matrix_basis.Identity(4) for o in objs),
          "every transform baked into the vertices (identity matrices)")
    check([o.name for o in objs] == sorted(o.name for o in objs), "returned sorted by name")
    check(all(o[cs2_ingest.SRC_NAME_PROP] == o.name and o[cs2_ingest.SRC_PATH_PROP] == o.name
              for o in objs), "source name / path props are the library names (strings)")

    e = extent(by["05"])
    print(f"        chair 05 extent {e.x:.4f} x {e.y:.4f} x {e.z:.4f} u "
          f"(spec text 6.72 x 2.54 x 8.52: y {100 * (e.y / 2.54 - 1):+.2f} %, "
          f"z {100 * (e.z / 8.52 - 1):+.2f} %)")
    check(near(e.x, 6.72, 0.01) and near(e.y, 2.51, 0.01) and near(e.z, 8.50, 0.01),
          "chair 05 extent 6.72 x 2.51 x 8.50 u +-1 % (ref-models sec 1.4)")
    drop = cs2_ingest.world_bounds(by["D05"])[1].z - cs2_ingest.world_bounds(by["05"])[0].z
    check(near(drop, 9.09, 0.005), f"grip D05 top - chair 05 lowest = {drop:.4f} u (9.09 +-0.5 %)")

    missing = [n for n in LINE_KIT + ENTRY_KIT if n not in by]
    check(not missing, f"line + entry head kit objects present (missing {missing})")
    if not missing:
        kit_tris = sum(cs2_asset.tri_count(by[n]) for n in LINE_KIT)
        check(kit_tris == 2058, f"line head kit = {kit_tris} tris (2,058)")
        sheave = by["Roues Pil Haut"]
        co = cs2_ingest._coords(sheave)
        xs = sorted(round(float(co[c][:, 0].mean()), 3)
                    for c in cs2_ingest._components(sheave.data))
        check(len(xs) == 16 and all(abs(abs(x) - 7.5) < 0.01 for x in xs),
              f"16 line sheaves centred at x = +-7.5 u (got {len(xs)}: {sorted(set(xs))})")
    return objs


def test_textures(objs):
    print("[test] texture sets")
    mats = cs2_ingest.used_materials(objs)
    check(len(mats) == 33, f"33 materials used by faces (got {len(mats)})")
    sets, unmatched = cs2_ingest.match_texture_sets(mats, KAGELOK_TEX)
    check(len(sets) == 30, f"30 textured materials (got {len(sets)})")
    check(unmatched == ["cable", GOUTIERE, "val cenis tex"],
          f"exactly cable / gouttiere / val cenis tex unmatched (got {unmatched})")
    check(all(s.base for s in sets.values()), "every returned set has a base map")

    def files(name):
        return [os.path.basename(p) for p in sets[name].paths()] if name in sets else []

    check(sets.get("Fennetre Rectangle") and sets["Fennetre Rectangle"].key == "fenetrerect",
          "alias Fennetre Rectangle -> fenetre_rect")
    check(sets.get("socle betton") and sets["socle betton"].key == "soclebeton",
          "alias socle betton -> socle_beton")
    pst = files("pil st haut bas 01")
    check(len(pst) == 4 and all(f.startswith("pil_st_haut_et_bas_01_") for f in pst),
          "'et' dropped between haut/bas (pil st haut bas 01 -> 4 maps)")
    nei = sets.get("Neige")
    check(nei is not None and os.path.basename(nei.base) == "neige_Albedo.png"
          and os.path.basename(nei.normal) == "Neige_Albedo_Normal.png"
          and os.path.basename(nei.rough) == "neige_Albedo_Roughness.png",
          "case-only collision Neige / neige folds into one set")
    gc = sets.get("Guide cable")
    check(gc is not None and os.path.basename(gc.rough) == "Guide_Cable_Albedo_Roughness.png",
          "case-only collision Guide_Cable / Guide_cable folds into one set")
    ph = sets.get("pil haut 01")
    check(ph is not None and os.path.basename(ph.rough) == "pil_Haut_01_Albedo_Roughness.png",
          "case-only collision pil_Haut_01 / pil_haut_01 folds into one set")
    bp = sets.get("Barrière Pil Haut Bas")
    check(bp is not None and bp.height and bp.height.endswith("_Albedo_Normal_DIS.png"),
          "truncated _DIS displacement recognised")
    si = sets.get(SIEGE)
    check(si is not None and os.path.basename(si.rough) == "Siège_AlbedoRoughness.png",
          "_AlbedoRoughness spelling recognised (siege)")
    th = sets.get("Therm bas haut")
    check(th is not None and all((th.base, th.normal, th.height, th.rough)),
          "Therm bas haut has all four maps (pipeline-gap-skeptic miss 4)")
    sv = sets.get("st verre haut")
    check(sv is not None and sv.base and not (sv.normal or sv.height or sv.rough),
          "St_Verre_Haut is albedo-only")
    check(cs2_ingest.material_key(SIEGE + ".001") == cs2_ingest.material_key(SIEGE) == "siege",
          "the .NNN suffix is stripped before matching")

    # The _load_map trap: every map of a set must carry that set's own key.
    borrowed = []
    for name, s in sorted(sets.items()):
        for p in s.paths():
            k = cs2_ingest._texture_key(os.path.splitext(os.path.basename(p))[0])
            if k is None or k[0] != s.key:
                borrowed.append((name, os.path.basename(p)))
    check(not borrowed, f"no set borrows another set's map (got {borrowed[:3]})")
    all_files = sorted(f for f in os.listdir(KAGELOK_TEX) if f.lower().endswith(".png"))
    used = sorted(os.path.basename(p) for s in sets.values() for p in s.paths())
    check(len(all_files) == 117 and used == all_files,
          f"all 117 PNGs used exactly once ({len(used)} used of {len(all_files)})")

    for keep, label in ((["cable"], "keep=['cable']"), (True, "keep=True"),
                        ([GOUTIERE + ".001"], "keep=['gouttiere.001']")):
        try:
            cs2_ingest.match_texture_sets(mats, KAGELOK_TEX, keep=keep)
            check(False, f"{label} raises ValueError")
        except ValueError as exc:
            check("cable" in str(exc) or "gouti" in str(exc), f"{label} raises ValueError")
    textured = sorted(sets)
    try:
        cs2_ingest.match_texture_sets(mats, KAGELOK_TEX, keep=textured)
        check(True, "keep = the 30 textured materials does not raise")
    except ValueError as exc:
        check(False, f"keep = the 30 textured materials does not raise ({exc})")
    ovr = {"cable": cs2_ingest.SourceSet(base=(0.05, 0.05, 0.05), rough=0.6)}
    s2, u2 = cs2_ingest.match_texture_sets(mats, KAGELOK_TEX, overrides=ovr, keep=["cable"])
    check("cable" in s2 and "cable" not in u2, "an override SourceSet satisfies keep")

    by_type = {}
    for f in all_files:
        by_type.setdefault(png_colour_type(os.path.join(KAGELOK_TEX, f)), f)
    print(f"        PNG colour types present: {sorted(by_type)}")
    check({0, 2, 3, 4, 6} <= set(by_type), "grey, RGB, palette, grey+alpha and RGBA files present")
    for ct, f in sorted(by_type.items()):
        px = cs2_ingest.load_map(os.path.join(KAGELOK_TEX, f), size=64)
        ok = px.shape == (64, 64, 4) and bool(np.isfinite(px).all())
        if ct in (0, 4):
            ok = ok and bool(np.allclose(px[..., 0], px[..., 1]))
        if ct in (0, 2):
            ok = ok and bool(np.allclose(px[..., 3], 1.0))
        check(ok, f"load_map colour type {ct} ({f}) -> (64, 64, 4) RGBA")


def test_clean_frame_parts(objs):
    print("[test] clean / reframe / rescale / to_parts / reduce")
    by = {o.name: o for o in objs}
    chair, grip = by["05"], by["D05"]
    e0 = extent(chair)
    rep = cs2_ingest.clean(chair)
    check(rep["ngons"] == 0 and rep["ngons_triangulated"] == 25,
          f"chair 05: 25 n-gons triangulated, 0 left (got {rep['ngons_triangulated']}, {rep['ngons']})")
    check(rep["tris"] == 1452, f"chair 05 keeps 1,452 tris (got {rep['tris']})")
    outs = {m.name: [n for n in m.node_tree.nodes if n.type == "OUTPUT_MATERIAL"]
            for m in chair.data.materials}
    check(all(len(v) == 1 and v[0].target == "ALL" and v[0].is_active_output
              for v in outs.values()),
          f"1 Material Output (target ALL) per material {[(k, len(v)) for k, v in outs.items()]}")
    feed = [v[0].inputs["Surface"].links[0].from_node.type for v in outs.values()]
    check(feed == ["BSDF_PRINCIPLED"], f"the kept output is the Principled one (got {feed})")
    check(not chair.data.color_attributes, "no colour attributes")
    check((extent(chair) - e0).length < 1e-4, "clean leaves the extent unchanged")
    cs2_ingest.clean(grip)

    m = cs2_ingest.reframe([chair, grip], forward="+Y", up="+Z", pivot_ref=grip)
    lo, hi = cs2_ingest.world_bounds([chair, grip])
    glo, ghi = cs2_ingest.world_bounds(grip)
    check(abs(lo.z) < 1e-5, f"reframe: lowest point at z 0 (got {lo.z:.2e})")
    check(near(hi.z, 9.091, 0.005), f"reframe: grip top at {hi.z:.4f} u")
    check(abs(glo.x + ghi.x) < 1e-4 and abs(glo.y + ghi.y) < 1e-4,
          "reframe: grip XY centre on the Z axis")
    e = hi - lo
    check(near(e.y, 6.72, 0.01), f"reframe: chair width now along Y ({e.y:.3f} u)")
    check(isinstance(m, type(chair.matrix_basis)), "reframe returns the applied matrix")

    f = cs2_ingest.rescale_by_reference([chair, grip], 1.0, "Z", 0.30)
    lo, hi = cs2_ingest.world_bounds([chair, grip])
    check(abs(f - 0.30) < 1e-9 and near(hi.z - lo.z, 2.727, 0.005) and abs(lo.z) < 1e-5,
          f"rescale 0.30 m/u -> drop {hi.z - lo.z:.4f} m, lowest still at 0")

    rename = {"05": "chair_", "D05": "grip_"}
    n_obj = len(bpy.data.objects)
    try:
        cs2_ingest.to_parts([chair, grip], rename, classify=lambda part, src, bb: "Bad")
        check(False, "a bad classify prefix raises ValueError")
    except ValueError:
        check(len(bpy.data.objects) == n_obj and chair.name == "05" and grip.name == "D05"
              and not [o for o in bpy.data.objects if o.name.endswith("_tmp")],
              "a bad classify prefix raises ValueError and leaves the sources, no parts")

    def small_grip_hw(part, src, bb):       # P0a review: hardware inside a kit, own prefix
        lo_, hi_ = bb
        return "griphw_" if src == "D05" and max(h - l for l, h in zip(lo_, hi_)) < 0.10 else None

    parts = cs2_ingest.to_parts([chair, grip], rename, classify=small_grip_hw)
    names = [o.name for o, _ in parts]
    n_chair = sum(1 for n in names if n.startswith("chair_"))
    n_grip = sum(1 for n in names if n.startswith("grip_"))
    n_hw = sum(1 for n in names if n.startswith("griphw_"))
    check(n_chair == 15 and n_grip == 5 and n_hw == 2,
          f"to_parts: 15 chair + 7 grip loose parts, classify moves the 2 small grip parts "
          f"to griphw_ (got {n_chair} + {n_grip} + {n_hw})")
    check(names == sorted(names) and names[0] == "chair_00" and all(q is None for _, q in parts),
          "parts named <prefix>_NN, sorted, quad None")
    check(sum(cs2_asset.tri_count(o) for o, _ in parts) == 1452 + 702,
          "to_parts keeps every triangle (2,154)")
    try:
        cs2_ingest.to_parts([by["Pil Deco"]], {"05": "chair_"})
        check(False, "to_parts raises on an unmapped object")
    except ValueError:
        check(True, "to_parts raises on an unmapped object")

    table = cs2_asset.tag_parts(parts)
    bpy.ops.object.select_all(action="DESELECT")
    for o, _ in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()
    joined = bpy.context.active_object
    joined.name = "KagelokChair"
    joined.data.name = "KagelokChair"       # the pipeline habit: mesh name = object name
    cs2_asset.store_part_table(joined, table)
    hw_idx = {i for i, n in enumerate(table, start=1) if n.startswith("griphw_")}
    cs2_ingest.reduce(joined, 900, delete_prefixes=("griphw_",))
    tris = cs2_asset.tri_count(joined)
    check(near(tris, 900, 0.25) and joined.name == "KagelokChair",
          f"reduce(900): {tris} tris, object kept")
    left = {v.value for v in joined.data.attributes["part"].data} & hw_idx
    check(len(hw_idx) == 2 and not left, f"reduce deleted the griphw_ parts by prefix "
                                         f"(table {sorted(hw_idx)}, left {sorted(left)})")
    check(joined.data.name == "KagelokChair",
          f"reduce keeps mesh name = object name (got {joined.data.name!r})")
    check(joined.data.has_custom_normals and all(p.use_smooth for p in joined.data.polygons),
          "reduce ends weighted-shaded at 66 deg (smooth faces, weighted custom normals)")


def test_mirror_and_inside_out():
    print("[test] mirrored transform / inside-out mesh")
    import bmesh
    for label, scale, flip in (("mirror", (-1.0, 2.0, 1.0), False), ("inside-out", (1, 1, 1), True)):
        me = bpy.data.meshes.new(f"box_{label}")
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=2.0)
        if flip:
            bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
        bm.to_mesh(me)
        bm.free()
        o = bpy.data.objects.new(f"box_{label}", me)
        bpy.context.scene.collection.objects.link(o)
        o.scale = scale
        f = cs2_ingest.rescale_by_reference([o], 1.0, "X", 1.0)   # bakes the transform
        if flip:
            rep = cs2_ingest.clean(o)
            check(rep["normals_flipped"] == 6, f"clean turns an inside-out box outward "
                                               f"({rep['normals_flipped']} flipped)")
        outward = all(p.normal.dot(p.center) > 0 for p in o.data.polygons)
        check(f == 1.0 and outward and o.matrix_basis == o.matrix_basis.Identity(4),
              f"{label}: transform baked, every face points outward")
        bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.meshes.remove(me)

    def made(label, build):
        me = bpy.data.meshes.new(label)
        bm = bmesh.new()
        build(bm)
        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
        bm.to_mesh(me)
        bm.free()
        o = bpy.data.objects.new(label, me)
        bpy.context.scene.collection.objects.link(o)
        return o

    o = made("box_keep", lambda bm: bmesh.ops.create_cube(bm, size=2.0))
    rep = cs2_ingest.clean(o, recalc="none")
    check(rep["normals_flipped"] == 0 and all(p.normal.dot(p.center) < 0 for p in o.data.polygons),
          "recalc='none' keeps an inside-out box inside-out")
    try:
        cs2_ingest.clean(o, recalc="outward")
        check(False, "an unknown recalc mode raises ValueError")
    except ValueError:
        check(True, "an unknown recalc mode raises ValueError")
    g = made("grid_open", lambda bm: bmesh.ops.create_grid(bm, x_segments=3, y_segments=3,
                                                            size=1.0))
    rep = cs2_ingest.clean(g)
    check(rep["normals_flipped"] == 0 and rep["components_closed"] == 0
          and all(p.normal.z < -0.99 for p in g.data.polygons),
          f"default recalc keeps an open reversed grid's source winding "
          f"({rep['components']} component(s), {rep['components_closed']} closed)")
    for ob in (o, g):
        me = ob.data
        bpy.data.objects.remove(ob, do_unlink=True)
        bpy.data.meshes.remove(me)


def selection_probe():
    """An unrelated selected + active object at frame 37: load_external must give it back."""
    probe = bpy.data.objects.new("SessionProbe", None)
    bpy.context.scene.collection.objects.link(probe)
    for o in list(bpy.context.view_layer.objects):
        if o is not None:
            o.select_set(False)
    probe.select_set(True)
    bpy.context.view_layer.objects.active = probe
    bpy.context.scene.frame_set(37)
    return probe


def check_restored(probe, objs, what):
    vl = bpy.context.view_layer
    sel = sorted(o.name for o in vl.objects if o is not None and o.select_get())
    check(bpy.context.scene.frame_current == 37 and sel == [probe.name]
          and vl.objects.active == probe and not any(o.select_get() for o in objs),
          f"{what}: frame 37, selection {sel} and the active object restored")
    bpy.data.objects.remove(probe, do_unlink=True)
    bpy.context.scene.frame_set(1)


def test_filtered_blend():
    print("[test] filtered .blend load")
    rep = {}
    probe = selection_probe()
    objs = cs2_ingest.load_external(KAGELOK, objects=["05", "D05"], report=rep)
    check_restored(probe, objs, "filtered .blend load")
    by = {o.name: o for o in objs}
    check(sorted(by) == ["05", "D05"], f"objects=['05', 'D05'] returns just those (got {sorted(by)})")
    dropped = sorted(n for n, _ in rep["dropped"])
    check(dropped == ["BezierCurve.001", "Socle Beton"],
          f"constraint targets appended, evaluated, then dropped (got {dropped})")
    check("Socle Beton" not in bpy.data.objects and "BezierCurve.001" not in bpy.data.objects
          and len(bpy.data.curves) == 0 and len(bpy.data.actions) == 0
          and len(bpy.data.meshes) == 2,
          f"no dependency object, curve, action or mesh left behind "
          f"({len(bpy.data.meshes)} meshes, {len(bpy.data.curves)} curves, "
          f"{len(bpy.data.actions)} actions)")
    if sorted(by) == ["05", "D05"]:
        e = extent(by["05"])
        drop = cs2_ingest.world_bounds(by["D05"])[1].z - cs2_ingest.world_bounds(by["05"])[0].z
        check(near(e.x, 6.72, 0.01) and near(e.y, 2.51, 0.01) and near(drop, 9.09, 0.005),
              f"same chair pose as the full load ({e.x:.3f} x {e.y:.3f}, drop {drop:.4f})")
    cs2_ingest.discard(objs)
    for src in (KAGELOK, GONDOLA):
        n_obj, n_me = len(bpy.data.objects), len(bpy.data.meshes)
        colls0 = {c.name for c in bpy.data.collections}
        try:
            cs2_ingest.load_external(src, objects=["NoSuchObject"])
            check(False, f"{os.path.basename(src)}: an unknown object name raises ValueError")
        except ValueError:
            left = sorted({c.name for c in bpy.data.collections} - colls0)
            check(not left and len(bpy.data.objects) == n_obj and len(bpy.data.meshes) == n_me,
                  f"{os.path.basename(src)}: an unknown object name raises ValueError and "
                  f"leaves nothing behind (collections {left})")


def test_winding_poses():
    print("[test] winding is pose-invariant (P0a review)")
    names = ["01", "03", "05", "07"]
    objs = cs2_ingest.load_external(KAGELOK, objects=names)
    by = {o.name: o for o in objs}
    polys = {n: [tuple(p.vertices) for p in by[n].data.polygons] for n in names}
    check(all(polys[n] == polys["01"] for n in names)
          and len({tuple(round(c, 3) for c in by[n].matrix_world.translation) for n in names}) == 1
          and len({round(cs2_ingest.world_bounds(by[n])[0].z, 3) for n in names}) == 4,
          "chairs 01/03/05/07: one local mesh (same polygon lists), four poses")
    src_edges = {frozenset(e) for p in polys["01"] for e in zip(p, p[1:] + p[:1])}

    def directed(o):
        out = collections.Counter()
        for p in o.data.polygons:
            v = list(p.vertices)
            for a, b in zip(v, v[1:] + v[:1]):
                if frozenset((a, b)) in src_edges:
                    out[(a, b)] += 1
        return out

    reps = {n: cs2_ingest.clean(by[n]) for n in names}
    check(all(r["doubles"] == 0 and r["degenerate_faces"] == 0 for r in reps.values()),
          "no vertex merged or dropped (indices comparable across poses)")
    d = {n: directed(by[n]) for n in names}
    flips = {n: (reps[n]["normals_flipped"], reps[n]["flips_by_component"]) for n in names}
    check(all(d[n] == d["01"] for n in names),
          "every source edge keeps the same direction in all four poses after clean")
    check(len({repr(v) for v in flips.values()}) == 1,
          f"clean flips the same faces in every pose ({ {n: v[0] for n, v in flips.items()} })")
    cs2_ingest.discard(objs)


def run_load_clean():
    objs = cs2_ingest.load_external(KAGELOK)
    for o in objs:
        cs2_ingest.clean(o)
    digest = mesh_hash(objs)
    cs2_ingest.discard(objs)
    return digest, len(objs)


def test_determinism():
    print("[test] determinism")
    a = run_load_clean()
    b = run_load_clean()
    check(a == b, f"two load + clean runs hash identically ({a[0]} vs {b[0]}, {a[1]} meshes)")
    left = [o.name for o in bpy.data.objects if o.get(cs2_ingest.SRC_FILE_PROP)]
    check(not left, f"discard() removed every ingested object (left {left[:3]})")


def refused(label, *a, **kw):
    """load_external must raise ValueError and leave objects / collections as they were."""
    n_obj = len(bpy.data.objects)
    colls0 = {c.name for c in bpy.data.collections}
    try:
        cs2_ingest.load_external(*a, **kw)
        check(False, f"{label} raises ValueError")
        return
    except ValueError as exc:
        msg = str(exc)
    left = sorted({c.name for c in bpy.data.collections} - colls0)
    check(len(bpy.data.objects) == n_obj and not left,
          f"{label} raises ValueError and leaves nothing behind ({msg[:70]}...)")


def test_gondola():
    print("[test] gondola.fbx")
    probe = selection_probe()
    sub = cs2_ingest.load_external(GONDOLA, objects=["cabin.001"])
    check_restored(probe, sub, ".fbx subtree load")
    paths = [o[cs2_ingest.SRC_PATH_PROP] for o in sub]
    check(len(sub) == 35 and all("/cabin.001/" in p for p in paths)
          and sum(cs2_asset.tri_count(o) for o in sub) == 2299,
          f"objects=['cabin.001'] returns that subtree: 35 meshes, 2,299 tris (got {len(sub)})")
    check(all(o.data.has_custom_normals for o in sub), "the FBX meshes arrive with custom normals")
    # P0a review: FBX names are post-dedup names; a second load would return 1 mesh.
    refused("a second gondola.fbx load while its objects are loaded", GONDOLA,
            objects=["cabin.001"])
    cs2_ingest.discard(sub)
    clash = bpy.data.objects.new("cabin", None)
    bpy.context.scene.collection.objects.link(clash)
    refused("a gondola.fbx load with a clashing name ('cabin') in the session", GONDOLA,
            objects=["cabin.001"])
    bpy.data.objects.remove(clash, do_unlink=True)

    rep = {}
    objs = cs2_ingest.load_external(GONDOLA, report=rep)
    check(len(objs) == 522 and rep["tris"] == 36557,
          f"522 meshes / 36,557 tris (got {len(objs)} / {rep['tris']})")
    check(len(rep["dropped"]) == 187 and all(t == "EMPTY" for _, t in rep["dropped"]),
          f"187 empties dropped (got {len(rep['dropped'])})")
    check(all(o.parent is None and o.matrix_basis == o.matrix_basis.Identity(4) for o in objs),
          "0.01 root scale and Y-up turn baked into the vertices")
    whole = extent(objs)
    check(near(whole.x, 0.831, 0.01), f"whole file {whole.x:.3f} x {whole.y:.3f} x {whole.z:.3f} "
                                      f"imported m (0.831 x 0.278 x 0.248)")

    fence = [o for o in objs if "/fences/fences_01/" in o[cs2_ingest.SRC_PATH_PROP]]
    f_fence = cs2_ingest.rescale_by_reference([], fence, "Z", 1.0)
    check(near(f_fence, 27.0, 0.10), f"fence = 1.00 m calibrates x{f_fence:.2f} (27 +-10 %)")

    cab = sorted((o for o in objs if "/cabin.001/" in o[cs2_ingest.SRC_PATH_PROP]),
                 key=lambda o: o.name)
    body = next(o for o in cab if o[cs2_ingest.SRC_PATH_PROP] == "gondola/cabin.001/cabin")
    f = cs2_ingest.rescale_by_reference(cab, 1.0, "Z", 27.0)
    eb, es = extent(body), extent(cab)
    print(f"        cabin.001 subtree x27: {es.x:.3f} x {es.y:.3f} x {es.z:.3f} m "
          f"(spec text 2.43 x 2.18 x 1.83 = this subtree at x{27 * 2.43 / es.x:.2f})")
    check(f == 27.0 and near(eb.x, 2.62, 0.01) and near(eb.y, 2.31, 0.01) and near(eb.z, 1.82, 0.01),
          f"body 'cabin' x27 = {eb.x:.3f} x {eb.y:.3f} x {eb.z:.3f} m (2.62 x 2.31 x 1.82, "
          f"ref-models sec 2.2)")
    check(near(es.x, 2.63, 0.01) and near(es.y, 2.36, 0.01) and near(es.z, 1.98, 0.01),
          "cabin.001 subtree x27 = 2.63 x 2.36 x 1.98 m +-1 %")
    # The body carries zero-length edges (coincident Maya vertices): clean welds them, so
    # degenerate triangles go while the surface area stays.
    area0 = sum(p.area for p in body.data.polygons)
    tris0 = cs2_asset.tri_count(body)
    had_custom = body.data.has_custom_normals
    rep_c = cs2_ingest.clean(body)
    area1 = sum(p.area for p in body.data.polygons)
    check(tris0 == 739 and rep_c["ngons"] == 0 and rep_c["tris"] < tris0
          and near(area1, area0, 1e-4) and rep_c["doubles"] > 0,
          f"body clean (metres): {tris0} -> {rep_c['tris']} tris, {rep_c['doubles']} welded, "
          f"area {area0:.4f} -> {area1:.4f} m2 kept")
    check(had_custom and rep_c["custom_normals_cleared"] and not body.data.has_custom_normals
          and rep_c["sharp_edges_cleared"] > 0 and "sharp_edge" not in body.data.attributes,
          f"body clean clears the FBX custom normals and {rep_c['sharp_edges_cleared']} "
          f"sharp_edge marks (shade_by_angle shades it fresh)")
    cols = cs2_ingest.colour_sets(cs2_ingest.used_materials(cab))
    check(cols and all(isinstance(s.base, tuple) and len(s.base) == 3 for s in cols.values()),
          f"colour_sets gives a constant base for {len(cols)} flat materials")


def main():
    t0 = time.time()
    clear_scene()
    objs = test_kagelok_load()
    test_textures(objs)
    test_clean_frame_parts(objs)
    clear_scene()
    test_mirror_and_inside_out()
    test_filtered_blend()
    test_winding_poses()
    test_determinism()
    test_gondola()
    dt = time.time() - t0
    if FAILS:
        print(f"FAIL test_ingest: {len(FAILS)} check(s) failed in {dt:.1f} s")
        for f in FAILS:
            print(f"  - {f}")
        sys.exit(1)
    print(f"PASS test_ingest: Kagelok + gondola ingest, textures, clean, parts, reduce and "
          f"determinism all hold ({dt:.1f} s)")


try:
    main()
except SystemExit:
    raise
except Exception:
    traceback.print_exc()
    print("FAIL test_ingest: exception")
    sys.exit(1)
