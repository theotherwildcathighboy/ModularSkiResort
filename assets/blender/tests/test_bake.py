"""
test_bake.py - contract test for cs2_bake (asset round 2026-10, P0; ROUND-SPEC.md section 6).

Run:  blender --background --factory-startup --python-exit-code 1 --python tests\\test_bake.py
Exit code 1 on any failure; prints one PASS line on success. 256 px bakes, AO 16 samples.

(1) bake_unique on a 2-material cube (material 0 constants, material 1 A x A images with a
    DirectX normal), a bystander plane 5 cm from the cube (would darken AO if the bake did
    not isolate the object), one reserved rect holding a checker strip:
      * BaseColor texels of every face == the source constants within 2/255 after the sRGB
        round trip; the reserved rect == its source texels 1:1 (within 1/255);
      * Normal: material 0 flat, material 1 == the DX source with G flipped (GL);
      * MaskMap R = metal, A = 1 - rough; ControlMask R/G/B exclusive, R on slot-1 faces,
        A 0; BaseColor and Normal alpha 255 everywhere;
      * one UV layer ("UVUnique"), no material slots, no CS2BAKE_/CS2SRC_ datablocks left;
      * every render / bake / Cycles / tool / color setting, the selection, the active
        object and hide_render equal their values before the bake.
(2) Two identical bakes in one process write byte-identical PNGs.
(3) recolor_albedo: slot-1 faces ship RECOLOR_ALBEDO (0.80 -> 204) neutral gray (the
    reserved strip does not steer the mean); a constant emissive lands in `_Emissive`
    (alpha 255); an "unknown" normal is dropped and rebuilt from a height ramp (G < 0.5).
(4) bake_high_to_low from a bevelled cube onto a plain cube: a non-flat normal map whose
    covered-texel mean is (0.5, 0.5, ~1.0); bake_unique(high=...) reproduces it.
(5) Ramp: a high plane tilted 10 deg about X baked onto a flat plane has G < 0.5, the same
    sign and value as cs2_compose.height_to_normal on the synthetic height ramp (GL).
(6) find_set_map / material_sources: strict ambientCG names (no *_Albedo_Roughness match),
    a missing set fails before any mesh changes, UVTile in tiles of metres_per_tile, and a
    bake through the tiling layer (cross-layer tangent NORMAL bake) writes a sane set.
(7) Our own CS2-packed maps as a source (mask / control / emissive images, GL normal)
    come through unchanged; MaskMap G/B are forced to 0.
(8) A mid-bake failure (reserved rect over a 16 px source) raises and restores the UV
    layers, material slots, polygon material indices, settings and datablocks; a
    pre-existing UVUnique comes back with its loops unchanged; a used material index
    without a source raises KeyError up front.
P0a review additions: a reserved rect whose source names the layer by its ORIGINAL name
(uv="UVMap") bakes byte-identically to uv=None (2); recolor_albedo None is auto (slot > 0,
no control image) (3); project_world_uv and material_sources read the CURRENT world matrix
after a bare `.location =` edit (6).
"""

import dataclasses
import hashlib
import math
import os
import shutil
import sys
import tempfile
import time
import traceback

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

import bpy  # noqa: E402
import bmesh  # noqa: E402

import cs2_asset  # noqa: E402
import cs2_bake  # noqa: E402
import cs2_compose  # noqa: E402
import cs2_preview  # noqa: E402
import cs2_uv  # noqa: E402

A = 256
AO_SAMPLES = 16
RECT = (0.375, 0.75, 0.625, 1.0)       # the top face of the default cube's UV cross
C0_LIN = (0.20, 0.45, 0.70)            # material 0 base, linear
C1_BYTES = (180, 90, 40)               # material 1 base, as stored in its sRGB PNG
CHECK_A, CHECK_B = (230, 30, 30), (30, 30, 230)
N_DX_BYTES = (166, 153, 236)           # material 1 normal, DirectX-encoded
ROUGH0, ROUGH1_BYTE = 0.30, 179
FAILS = []


def check(cond, msg):
    if cond:
        print(f"  ok   {msg}")
    else:
        print(f"  FAIL {msg}")
        FAILS.append(msg)


# --------------------------------------------------------------------------------------
# Helpers
# --------------------------------------------------------------------------------------

def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.images):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def write_png(path, rgba_bytes, is_data):
    arr = np.asarray(rgba_bytes, dtype=np.float32) / 255.0
    cs2_asset._write_png_raw(path, "testsrc", arr, arr.shape[0], is_data)
    return path


def read_png(path):
    img = bpy.data.images.load(path, check_existing=False)
    img.colorspace_settings.name = "Non-Color"
    img.alpha_mode = "CHANNEL_PACKED"
    w, h = img.size
    buf = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(buf)
    bpy.data.images.remove(img)
    return np.rint(buf.reshape(h, w, 4) * 255.0).astype(np.int32)


def srgb_bytes(lin):
    return np.rint(cs2_preview._linear_to_srgb(np.array(lin, np.float32)) * 255.0).astype(int)


def face_texels(mesh, poly, arr, inset=3):
    """Texels of a face's UVUnique bbox (cube faces are axis-aligned squares), inset."""
    uv = cs2_uv.loop_uvs(mesh.uv_layers["UVUnique"])
    p = mesh.polygons[poly]
    q = uv[p.loop_start:p.loop_start + p.loop_total] * A
    r0, r1 = int(math.ceil(q[:, 1].min())) + inset, int(math.floor(q[:, 1].max())) - inset
    c0, c1 = int(math.ceil(q[:, 0].min())) + inset, int(math.floor(q[:, 0].max())) - inset
    return arr[r0:r1, c0:c1].reshape(-1, arr.shape[-1])


def dump_settings():
    """Independent of cs2_bake.scene_settings: every non-pointer RNA value, read-only too."""
    sc = bpy.context.scene
    out = {}
    for label, owner in (("render", sc.render), ("bake", sc.render.bake), ("cycles", sc.cycles),
                         ("tool", sc.tool_settings), ("view", sc.view_settings),
                         ("display", sc.display_settings)):
        for p in owner.bl_rna.properties:
            if p.type in ("POINTER", "COLLECTION") or p.identifier == "rna_type":
                continue
            try:
                v = getattr(owner, p.identifier)
            except Exception:  # noqa: BLE001 - context-dependent props
                continue
            if isinstance(v, set):
                v = frozenset(v)
            elif hasattr(v, "__len__") and not isinstance(v, str):
                v = tuple(v)
            out[f"{label}.{p.identifier}"] = v
    vl = bpy.context.view_layer
    out["active"] = vl.objects.active.name if vl.objects.active else None
    out["selected"] = tuple(sorted(o.name for o in vl.objects if o.select_get()))
    out["hide_render"] = tuple(sorted((o.name, o.hide_render) for o in sc.objects))
    return out


def leftovers():
    return sorted([i.name for i in bpy.data.images if i.name.startswith(cs2_bake.BAKE_PREFIX)]
                  + [m.name for m in bpy.data.materials
                     if m.name.startswith((cs2_bake.BAKE_PREFIX, cs2_bake.SRC_MAT_PREFIX))])


def sha1(path):
    with open(path, "rb") as fh:
        return hashlib.sha1(fh.read()).hexdigest()


# --------------------------------------------------------------------------------------
# (1)-(3) two-material cube
# --------------------------------------------------------------------------------------

def make_sources(src_dir):
    """Material 1's A x A images: base (C1 + checker in RECT), DX normal, roughness."""
    base = np.zeros((A, A, 4), np.int32)
    base[..., 0:3] = C1_BYTES
    base[..., 3] = 255
    r0, r1 = int(RECT[1] * A), int(RECT[3] * A)
    c0, c1 = int(RECT[0] * A), int(RECT[2] * A)
    yy, xx = np.mgrid[r0:r1, c0:c1]
    chk = ((yy // 8 + xx // 8) % 2).astype(bool)
    base[r0:r1, c0:c1, 0:3] = np.where(chk[..., None], CHECK_A, CHECK_B)
    nrm = np.zeros((A, A, 4), np.int32)
    nrm[..., 0:3] = N_DX_BYTES
    nrm[..., 3] = 255
    rgh = np.full((A, A, 4), ROUGH1_BYTE, np.int32)
    rgh[..., 3] = 255
    return (write_png(os.path.join(src_dir, "m1_base.png"), base, False),
            write_png(os.path.join(src_dir, "m1_normal.png"), nrm, True),
            write_png(os.path.join(src_dir, "m1_rough.png"), rgh, True), base)


def build_cube():
    """Default cube + a bystander plane 5 cm off its +X face; returns (cube, mat1 faces)."""
    clear_scene()
    bpy.ops.mesh.primitive_plane_add(size=3.0, location=(1.05, 0.0, 0.0),
                                     rotation=(0.0, math.radians(90.0), 0.0))
    bystander = bpy.context.active_object
    bystander.name = "Bystander"
    bpy.ops.mesh.primitive_cube_add(size=2.0)
    cube = bpy.context.active_object
    cube.name = "BakeCube"
    me = cube.data
    uv = cs2_uv.loop_uvs(me.uv_layers["UVMap"])
    in_rect = [p.index for p in me.polygons
               if all(RECT[0] - 1e-4 <= uv[li, 0] <= RECT[2] + 1e-4 and
                      RECT[1] - 1e-4 <= uv[li, 1] <= RECT[3] + 1e-4 for li in p.loop_indices)]
    if len(in_rect) != 1:
        raise RuntimeError(f"default cube UV cross changed: faces in {RECT}: {in_rect}")
    plusx = max(range(len(me.polygons)), key=lambda i: me.polygons[i].normal.x)
    mat1 = sorted({in_rect[0], plusx} | {next(i for i in range(6) if i not in (in_rect[0], plusx))})
    for nm in ("SrcMat0", "SrcMat1"):
        me.materials.append(bpy.data.materials.get(nm) or bpy.data.materials.new(nm))
    for p in me.polygons:
        p.material_index = 1 if p.index in mat1 else 0
    # leave a non-trivial selection / active object for the restore check
    bpy.ops.object.select_all(action="DESELECT")
    bystander.select_set(True)
    bpy.context.view_layer.objects.active = bystander
    return cube, mat1, in_rect[0], plusx


def sources_for(paths, recolor=False):
    b, n, r, _ = paths
    return {
        0: cs2_bake.SourceSet(base=C0_LIN, rough=ROUGH0, metal=0.0, slot=0),
        1: cs2_bake.SourceSet(base=b, normal=n, normal_convention="DX", rough=r, metal=1.0,
                              slot=1, recolor_albedo=recolor),
    }


def test_cube(paths, out_a, out_b):
    print("[1] bake_unique: 2-material cube, constants + images, reserved rect, isolation")
    cube, mat1, rect_face, plusx = build_cube()
    sc = bpy.context.scene
    sc.cycles.samples = 77                      # non-factory values the bake must give back
    sc.cycles.seed = 5
    sc.render.bake.margin = 3
    sc.tool_settings.use_uv_select_sync = False
    before = dump_settings()
    leftover0 = leftovers()
    n_images = len(bpy.data.images)
    rep = cs2_bake.bake_unique(cube, out_a, "BakeCubeA", A, sources_for(paths),
                               reserved=[RECT], ao_samples=AO_SAMPLES)
    after = dump_settings()
    diff = sorted(k for k in before if before[k] != after.get(k))
    check(not diff, f"settings / selection / hide_render restored ({len(before)} values"
                    f"{'; changed: ' + ', '.join(diff[:8]) if diff else ''})")
    check(leftovers() == leftover0, f"no CS2BAKE_/CS2SRC_ datablocks left ({leftovers()})")
    check(len(bpy.data.images) == n_images, "image datablock count unchanged")
    me = cube.data
    check([l.name for l in me.uv_layers] == ["UVUnique"], f"one UV layer UVUnique "
                                                           f"({[l.name for l in me.uv_layers]})")
    check(len(me.materials) == 0, "material slots removed")
    check(rep["files"] == ["BakeCubeA_BaseColor.png", "BakeCubeA_Normal.png",
                           "BakeCubeA_MaskMap.png", "BakeCubeA_ControlMask.png"],
          f"four PNGs written {rep['files']}")
    check(rep["ao"] == "self" and rep["reserved_restored"]
          and rep["reserved_restored"][0][1] == 1, f"AO self, rect restored from material 1 "
                                                   f"({rep['reserved_restored']})")

    base = read_png(os.path.join(out_a, "BakeCubeA_BaseColor.png"))
    nrm = read_png(os.path.join(out_a, "BakeCubeA_Normal.png"))
    msk = read_png(os.path.join(out_a, "BakeCubeA_MaskMap.png"))
    ctl = read_png(os.path.join(out_a, "BakeCubeA_ControlMask.png"))
    c0 = srgb_bytes(C0_LIN)
    n_dx = np.array(N_DX_BYTES, np.float64) / 255.0 * 2.0 - 1.0
    n_gl = np.array((n_dx[0], -n_dx[1], n_dx[2]))
    n_gl = np.rint((n_gl / np.linalg.norm(n_gl) * 0.5 + 0.5) * 255.0).astype(int)
    for poly in range(6):
        is1 = poly in mat1
        tb, tn = face_texels(me, poly, base), face_texels(me, poly, nrm)
        tm, tc = face_texels(me, poly, msk), face_texels(me, poly, ctl)
        tag = f"face {poly} (mat {int(is1)}{', +X next to bystander' if poly == plusx else ''})"
        if poly == rect_face:
            continue                        # checked 1:1 below
        want = np.array(C1_BYTES if is1 else c0)
        err = int(np.abs(tb[:, 0:3] - want).max())
        check(err <= 2, f"{tag}: BaseColor == source {tuple(want)} within 2/255 (max err {err})")
        want_n = n_gl if is1 else np.array((128, 128, 255))
        err = int(np.abs(tn[:, 0:3] - want_n).max())
        check(err <= 3, f"{tag}: Normal == {tuple(want_n)} within 3/255 (max err {err})")
        want_m = (255, 255 - ROUGH1_BYTE) if is1 else (0, int(round((1 - ROUGH0) * 255)))
        err = int(max(np.abs(tm[:, 0] - want_m[0]).max(), np.abs(tm[:, 3] - want_m[1]).max()))
        check(err <= 2, f"{tag}: MaskMap R/A == {want_m} within 2/255 (max err {err})")
        want_c = (255, 0, 0) if is1 else (0, 0, 0)
        check(bool((tc[:, 0:3] == want_c).all()), f"{tag}: ControlMask == {want_c}")
    # reserved rect: 1:1 copy of the source's checker strip (the top face's texels)
    src = paths[3]
    tb, ts = face_texels(me, rect_face, base, 2), face_texels(me, rect_face, src, 2)
    err = int(np.abs(tb[:, 0:3] - ts[:, 0:3]).max())
    check(err <= 1 and len(tb) > 1000, f"reserved rect: BaseColor == source checker 1:1 "
                                       f"({len(tb)} texels, max err {err})")
    check(bool((base[..., 3] == 255).all()), "BaseColor alpha 255 everywhere")
    check(bool((nrm[..., 3] == 255).all()), "Normal alpha 255 everywhere")
    check(bool((ctl[..., 3] == 0).all()), "ControlMask alpha 0 everywhere")
    check(bool((msk[..., 2] == 0).all()), "MaskMap B 0 everywhere")
    nz = (ctl[..., 0:3] > 0).sum(axis=-1)
    check(int(nz.max()) <= 1, "ControlMask R/G/B exclusive on every texel")

    print("[2] determinism: the same bake again, byte-identical PNGs")
    cube, *_ = build_cube()
    cs2_bake.bake_unique(cube, out_b, "BakeCubeA", A, sources_for(paths), reserved=[RECT],
                         ao_samples=AO_SAMPLES)
    for f in rep["files"]:
        ha, hb = sha1(os.path.join(out_a, f)), sha1(os.path.join(out_b, f))
        check(ha == hb, f"{f}: {ha[:12]} == {hb[:12]}")

    print("    the same sources as a foreign duck-typed class keyed by material NAME")

    class Foreign:          # cs2_ingest.SourceSet shape: the bake's fields + an extra `key`
        def __init__(self, s):
            for k in ("base", "normal", "normal_convention", "height", "rough", "metal",
                      "slot", "recolor_albedo", "emissive"):
                setattr(self, k, getattr(s, k))
            self.key = "foreign"
    by_name = {"SrcMat0": Foreign(sources_for(paths)[0]),
               "SrcMat1": Foreign(sources_for(paths)[1]),
               "NotOnThisMesh": Foreign(sources_for(paths)[0])}
    cube, *_ = build_cube()
    out_n = out_b + "_byname"
    rep_n = cs2_bake.bake_unique(cube, out_n, "BakeCubeA", A, by_name, reserved=[RECT],
                                 ao_samples=AO_SAMPLES)
    same = all(sha1(os.path.join(out_a, f)) == sha1(os.path.join(out_n, f))
               for f in rep["files"])
    check(same and rep_n["unused_source_names"] == ["NotOnThisMesh"],
          f"name-keyed foreign sources == int-keyed SourceSets byte for byte; unused names "
          f"reported {rep_n['unused_source_names']}")

    print("    reserved rect sampled through the source layer's ORIGINAL name (uv='UVMap')")
    srcs_uv = sources_for(paths)
    srcs_uv[1] = dataclasses.replace(srcs_uv[1], uv="UVMap")
    cube, *_ = build_cube()
    out_u = out_b + "_uvname"
    rep_u = cs2_bake.bake_unique(cube, out_u, "BakeCubeA", A, srcs_uv, reserved=[RECT],
                                 ao_samples=AO_SAMPLES)
    same = all(sha1(os.path.join(out_a, f)) == sha1(os.path.join(out_u, f))
               for f in rep["files"])
    check(same and rep_u["reserved_restored"] and rep_u["reserved_restored"][0][1] == 1,
          "uv='UVMap' (the source layer by its original name) restores the rect and bakes "
          "byte-identically to uv=None")

    print("[3] recolor_albedo (slot 1), emissive constant, height route (unknown normal)")
    auto = {
        "slot 1, flag unset": cs2_bake.as_source_set(cs2_bake.SourceSet(base=C0_LIN, slot=1)),
        "slot 0, flag unset": cs2_bake.as_source_set(cs2_bake.SourceSet(base=C0_LIN)),
        "slot 2, explicit False": cs2_bake.as_source_set(
            cs2_bake.SourceSet(base=C0_LIN, slot=2, recolor_albedo=False)),
        "slot 1 set later (replace)": cs2_bake.as_source_set(
            dataclasses.replace(cs2_bake.SourceSet(base=C0_LIN), slot=1)),
    }
    got = {k: v.recolor_albedo for k, v in auto.items()}
    check(got == {"slot 1, flag unset": True, "slot 0, flag unset": False,
                  "slot 2, explicit False": False, "slot 1 set later (replace)": True},
          f"recolor_albedo None = auto (slot > 0, no control): {got}")
    cube, mat1, rect_face, _ = build_cube()
    out_c = out_b + "_recolor"
    srcs = sources_for(paths, recolor=True)
    ramp = np.zeros((A, A, 4), np.int32)
    ramp[..., 0:3] = np.arange(A)[:, None, None]          # height rises with source v
    ramp[..., 3] = 255
    h_path = write_png(os.path.join(os.path.dirname(paths[0]), "m0_height.png"), ramp, True)
    srcs[0] = cs2_bake.SourceSet(base=C0_LIN, rough=ROUGH0, normal=paths[1],
                                 normal_convention="unknown", height=h_path, height_depth_m=1.0,
                                 emissive=(1.0, 0.5, 0.0))
    rep = cs2_bake.bake_unique(cube, out_c, "BakeCubeA", A, srcs, reserved=[RECT],
                               ao_samples=0, emissive=True)
    base = read_png(os.path.join(out_c, "BakeCubeA_BaseColor.png"))
    nrm = read_png(os.path.join(out_c, "BakeCubeA_Normal.png"))
    emi = read_png(os.path.join(out_c, "BakeCubeA_Emissive.png"))
    want = int(round(cs2_asset.RECOLOR_ALBEDO * 255))
    want_e = srgb_bytes((1.0, 0.5, 0.0))
    for poly in range(6):
        tb, te = face_texels(cube.data, poly, base), face_texels(cube.data, poly, emi)
        if poly in mat1:
            if poly == rect_face:
                continue
            err = int(np.abs(tb[:, 0:3] - want).max())
            check(err <= 2, f"face {poly}: BaseColor == {want} gray within 2/255 (max err {err})")
            check(bool((te[:, 0:3] == 0).all()), f"face {poly}: no emission")
        else:
            err = int(np.abs(te[:, 0:3] - want_e).max())
            check(err <= 2, f"face {poly}: Emissive == {tuple(want_e)} within 2/255 ({err})")
            tn = face_texels(cube.data, poly, nrm)[:, 0:3]
            g, r = float(tn[:, 1].mean()), float(tn[:, 0].mean())
            check(g < 120 and abs(r - 128) <= 2, f"face {poly}: height route G {g:.0f} < 128 on a "
                                                 f"+V ramp, R {r:.0f} ~ 128 (source normal dropped)")
    check(bool((emi[..., 3] == 255).all()), "Emissive alpha 255 everywhere")
    check(rep["height_route"] == [0] and rep["materials"][0]["normal"] == "height",
          f"report: height route {rep['height_route']}")
    k = rep["materials"][1]["recolor_scale"]
    check(rep["ao"] is None and k is not None,
          f"ao_samples=0 skips AO; recolor scale {k if k is None else round(k, 3)}")


# --------------------------------------------------------------------------------------
# (7) our own CS2 set as a source: mask / control / emissive images, GL normal
# --------------------------------------------------------------------------------------

def test_atlas_sources(out_dir):
    print("[7] CS2-packed sources: MaskMap + ControlMask + Emissive images, GL normal")
    src = os.path.join(out_dir, "src")
    os.makedirs(src, exist_ok=True)

    def const(path, rgba, is_data):
        arr = np.zeros((A, A, 4), np.int32)
        arr[...] = rgba
        return write_png(os.path.join(src, path), arr, is_data)
    s = cs2_bake.SourceSet(base=const("b.png", (100, 150, 200, 255), False),
                           normal=const("n.png", (150, 110, 240, 255), True),
                           normal_convention="GL",
                           mask=const("m.png", (255, 77, 77, 64), True),
                           control=const("c.png", (0, 255, 0, 0), True),
                           emissive=const("e.png", (255, 200, 0, 255), False))
    clear_scene()
    bpy.ops.mesh.primitive_cube_add(size=2.0)
    cube = bpy.context.active_object
    rep = cs2_bake.bake_unique(cube, out_dir, "AtlasCubeA", A, {0: s}, ao_samples=0,
                               emissive=True)
    got = {k: read_png(os.path.join(out_dir, f"AtlasCubeA_{k}.png"))
           for k in ("BaseColor", "Normal", "MaskMap", "ControlMask", "Emissive")}
    n = np.array((150, 110, 240), np.float64) / 255.0 * 2.0 - 1.0
    want_n = np.rint((n / np.linalg.norm(n) * 0.5 + 0.5) * 255.0).astype(int)
    ok = {"BaseColor": [], "Normal": [], "MaskMap": [], "ControlMask": [], "Emissive": []}
    for poly in range(6):
        t = {k: face_texels(cube.data, poly, v) for k, v in got.items()}
        ok["BaseColor"].append(int(np.abs(t["BaseColor"][:, 0:3] - (100, 150, 200)).max()))
        ok["Normal"].append(int(np.abs(t["Normal"][:, 0:3] - want_n).max()))
        ok["MaskMap"].append(int(max(np.abs(t["MaskMap"][:, 0] - 255).max(),
                                     np.abs(t["MaskMap"][:, 3] - 64).max(),
                                     np.abs(t["MaskMap"][:, 1:3]).max())))
        ok["ControlMask"].append(int(np.abs(t["ControlMask"] - (0, 255, 0, 0)).max()))
        ok["Emissive"].append(int(np.abs(t["Emissive"][:, 0:3] - (255, 200, 0)).max()))
    for k, errs in ok.items():
        lim = 3 if k == "Normal" else 2
        check(max(errs) <= lim, f"{k} == source on all 6 faces within {lim}/255 (max err "
                                f"{max(errs)}{'; MaskMap G/B forced 0' if k == 'MaskMap' else ''})")
    check(rep["files"][-1] == "AtlasCubeA_Emissive.png", "Emissive written last")


# --------------------------------------------------------------------------------------
# (8) failure path: a mid-bake error restores the mesh and the scene
# --------------------------------------------------------------------------------------

def test_failure_restores(paths, out_dir):
    print("[8] failure path: reserved rect over a non-A x A source raises, nothing leaks")
    small = np.zeros((16, 16, 4), np.int32)
    small[...] = (180, 90, 40, 255)
    p16 = write_png(os.path.join(out_dir, "small.png"), small, False)
    cube, mat1, *_ = build_cube()
    me = cube.data
    idx0 = [p.material_index for p in me.polygons]
    mats0 = [m.name for m in me.materials]
    before, left0 = dump_settings(), leftovers()
    srcs = sources_for(paths)
    srcs[1] = cs2_bake.SourceSet(base=p16, slot=1)
    try:
        cs2_bake.bake_unique(cube, out_dir, "FailCubeA", A, srcs, reserved=[RECT],
                             ao_samples=AO_SAMPLES)
        check(False, "bake_unique raised")
    except ValueError as e:
        check("needs an" in str(e), f"bake_unique raised ValueError ({str(e)[:70]}...)")
    check([l.name for l in me.uv_layers] == ["UVMap"], f"UV layers restored "
                                                       f"{[l.name for l in me.uv_layers]}")
    check([m.name for m in me.materials] == mats0, f"material slots restored "
                                                   f"{[m.name for m in me.materials]}")
    check([p.material_index for p in me.polygons] == idx0, "polygon material indices kept")
    diff = sorted(k for k in before if before[k] != dump_settings().get(k))
    check(not diff, f"settings restored after the failure{(': ' + ', '.join(diff[:6])) if diff else ''}")
    check(leftovers() == left0, f"no bake datablocks left ({leftovers()})")

    print("    a pre-existing UVUnique survives the failed bake unchanged")
    cube, *_ = build_cube()
    me = cube.data
    pre = me.uv_layers.new(name="UVUnique", do_init=False)
    me.uv_layers.active = me.uv_layers["UVMap"]
    want = cs2_uv.loop_uvs(me.uv_layers["UVMap"]) * 0.5 + 0.25
    cs2_uv.set_loop_uvs(pre, want)
    try:
        cs2_bake.bake_unique(cube, out_dir, "FailCubeA", A, srcs, reserved=[RECT],
                             ao_samples=AO_SAMPLES)
        check(False, "bake_unique raised (pre-existing UVUnique)")
    except ValueError:
        pass
    names = [l.name for l in me.uv_layers]
    err = (float(np.abs(cs2_uv.loop_uvs(me.uv_layers["UVUnique"]) - want).max())
           if "UVUnique" in me.uv_layers else 1.0)
    check(names == ["UVMap", "UVUnique"] and err < 1e-6 and me.uv_layers.active.name == "UVMap",
          f"pre-existing UVUnique restored after the failure (layers {names}, max err {err:.2e})")
    try:
        cs2_bake.bake_unique(cube, out_dir, "FailCubeA", A, {0: srcs[0]}, ao_samples=0)
        check(False, "a used index without a source raises")
    except KeyError as e:
        check("[1]" in str(e), f"used index without a SourceSet raises KeyError ({str(e)[:60]})")


# --------------------------------------------------------------------------------------
# (4) bevelled cube -> plain cube
# --------------------------------------------------------------------------------------

def make_bevel_pair():
    clear_scene()
    bpy.ops.mesh.primitive_cube_add(size=2.0)
    high = bpy.context.active_object
    high.name = "BevelHigh"
    bm = bmesh.new()
    bm.from_mesh(high.data)
    bmesh.ops.bevel(bm, geom=list(bm.edges), offset=0.12, segments=3, affect="EDGES",
                    profile=0.5)
    bm.to_mesh(high.data)
    bm.free()
    for p in high.data.polygons:
        p.use_smooth = True
    bpy.ops.mesh.primitive_cube_add(size=2.0)
    low = bpy.context.active_object
    low.name = "BevelLow"
    return high, low


def test_high_to_low(out_dir):
    print("[4] bake_high_to_low: bevelled cube -> plain cube")
    high, low = make_bevel_pair()
    cs2_uv.pack_unique(low, A, [])
    before = dump_settings()
    npath, apath = cs2_bake.bake_high_to_low(high, low, out_dir, "BevelLowA", A,
                                             ao_samples=AO_SAMPLES,
                                             out_dir=os.path.join(out_dir, "h2l"))
    check(dump_settings() == before, "settings restored after bake_high_to_low")
    check(len(low.data.materials) == 0, "low's (empty) material list restored")
    check(not os.path.exists(os.path.join(out_dir, "BevelLowA_HighNormal.png")),
          "intermediate PNGs kept out of the asset folder")
    _parts, ppm = cs2_compose.rasterise_face_fields(low.data, A, "UVUnique")
    cov = ppm > 0
    n = read_png(npath)[cov][:, 0:3] / 255.0
    mean = n.mean(axis=0)
    dev = np.abs(n[:, 0:2] - 0.5).max(axis=1)
    check(abs(mean[0] - 0.5) < 0.02 and abs(mean[1] - 0.5) < 0.02 and mean[2] > 0.95,
          f"covered mean ({mean[0]:.3f}, {mean[1]:.3f}, {mean[2]:.3f}) ~ (0.5, 0.5, ~1.0)")
    check(float(dev.max()) > 0.2 and float((dev > 0.05).mean()) > 0.02,
          f"non-flat: max |RG - 0.5| {dev.max():.3f}, {100 * (dev > 0.05).mean():.1f} % tilted")
    ao = read_png(apath)[cov][:, 0] / 255.0
    check(apath is not None and 0.5 < float(ao.mean()) <= 1.0,
          f"high-to-low AO sane (covered mean {ao.mean():.3f}, min {ao.min():.3f})")

    print("    bake_unique(high=...) feeds the same normal into its compose")
    rep = cs2_bake.bake_unique(low, out_dir, "BevelLowA", A,
                               {0: cs2_bake.SourceSet(base=(0.5, 0.5, 0.5))},
                               ao_samples=AO_SAMPLES, high=high)
    n2 = read_png(os.path.join(out_dir, "BevelLowA_Normal.png"))[cov][:, 0:3] / 255.0

    def unit(x):        # Cycles leaves a few corner texels at |n| ~ 0.9; blend_rnm renormalises
        v = x * 2.0 - 1.0
        return v / np.linalg.norm(v, axis=1, keepdims=True)
    err = float(np.abs(unit(n2) - unit(n)).max()) * 0.5
    check(rep["ao"] == "high" and err <= 2.5 / 255.0,
          f"bake_unique(high) Normal == standalone high-to-low, as unit vectors "
          f"(max err {err * 255:.1f}/255)")


# --------------------------------------------------------------------------------------
# (5) GL sign: tilted plane vs height_to_normal ramp
# --------------------------------------------------------------------------------------

def test_ramp_sign(out_dir):
    print("[5] GL sign: 10 deg ramp, Cycles high-to-low vs cs2_compose.height_to_normal")
    clear_scene()
    theta = math.radians(10.0)
    bpy.ops.mesh.primitive_plane_add(size=2.0)
    low = bpy.context.active_object
    low.name = "RampLow"
    me = low.data
    uv = np.empty((len(me.loops), 2))
    for li, lp in enumerate(me.loops):
        co = me.vertices[lp.vertex_index].co
        uv[li] = ((co.x + 1.0) / 2.0, (co.y + 1.0) / 2.0)     # T = +X, B = +Y
    cs2_uv.set_loop_uvs(me.uv_layers["UVMap"], uv)
    bpy.ops.mesh.primitive_plane_add(size=3.0, rotation=(theta, 0.0, 0.0))
    high = bpy.context.active_object                         # height rises toward +Y (+V)
    high.name = "RampHigh"
    npath, apath = cs2_bake.bake_high_to_low(high, low, out_dir, "RampLowA", A,
                                             cage_extrusion=0.3, max_ray=0.6, ao_samples=0,
                                             out_dir=os.path.join(out_dir, "ramp"),
                                             uv_layer="UVMap")
    n = read_png(npath)[8:-8, 8:-8].reshape(-1, 4)[:, 0:3] / 255.0
    ppm = A / 2.0
    rows = np.arange(A, dtype=np.float32)[:, None] * np.ones((1, A), np.float32)
    ramp = math.tan(theta) * rows / ppm                      # meters, rising with v
    ref = cs2_compose.height_to_normal(ramp, ppm)[8:-8, 8:-8].reshape(-1, 3)
    g_bake, g_ref = float(n[:, 1].mean()), float(ref[:, 1].mean())
    r_bake = float(n[:, 0].mean())
    check(apath is None, "ao_samples=0 -> no AO map")
    check(g_bake < 0.5 and g_ref < 0.5, f"both G < 0.5 on a +V-rising ramp (bake {g_bake:.4f}, "
                                        f"height_to_normal {g_ref:.4f})")
    check(abs(g_bake - g_ref) < 0.01, f"same magnitude: |dG| {abs(g_bake - g_ref):.4f} < 0.01 "
                                      f"(expected 0.5 - sin(10)/2 = {0.5 - math.sin(theta) / 2:.4f})")
    check(abs(r_bake - 0.5) < 0.01, f"R == 0.5 (no slope along u): {r_bake:.4f}")


# --------------------------------------------------------------------------------------
# (6) find_set_map / material_sources
# --------------------------------------------------------------------------------------

def test_material_sources(out_dir):
    print("[6] find_set_map / material_sources / tiling bake")
    conc = os.path.join(cs2_bake.TEXTURE_ROOT, "Concrete034")
    hit = cs2_bake.find_set_map(conc, "Color")
    check(hit is not None and os.path.basename(hit) == "Concrete034_1K-JPG_Color.jpg",
          f"Concrete034 Color -> {hit and os.path.basename(hit)}")
    hit = cs2_bake.find_set_map(conc, "NormalGL")
    check(hit is not None and hit.endswith("_NormalGL.jpg"), "NormalGL, never NormalDX")
    check(cs2_bake.find_set_map(conc, "Metalness") is None, "absent map -> None")
    fake = os.path.join(out_dir, "Siege")
    os.makedirs(fake, exist_ok=True)
    for f in ("Siege_Albedo.png", "Siege_Albedo_Roughness.png"):
        open(os.path.join(fake, f), "wb").close()
    check(cs2_bake.find_set_map(fake, "Roughness") is None,
          "*_Albedo_Roughness does NOT match Roughness (pipeline-gap-skeptic verdict 3)")

    clear_scene()
    # P0a review: a bare .location edit leaves matrix_world stale until the depsgraph runs
    bpy.ops.mesh.primitive_cube_add(size=2.0)
    probe = bpy.context.active_object
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(-3.0, 0.0, 0.0))
    probe.location = (10.0, 0.0, 1.0)
    cs2_bake.project_world_uv(probe, 1.0, "box", "UVProbe")
    top = max(probe.data.polygons, key=lambda p: p.normal.z)
    puv = cs2_uv.loop_uvs(probe.data.uv_layers["UVProbe"])[top.loop_start:
                                                          top.loop_start + top.loop_total]
    check(abs(float(puv[:, 0].mean()) - 10.0) < 1e-4 and abs(float(puv[:, 1].mean())) < 1e-4,
          f"project_world_uv after `.location = (10, 0, 1)`: top face centered at u "
          f"{puv[:, 0].mean():.3f} (10 tiles, not the stale 0)")
    clear_scene()
    bpy.ops.mesh.primitive_cube_add(size=2.0, location=(0.0, 0.0, 1.0))
    wall = bpy.context.active_object
    wall.name = "Wall"
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(3.0, 0.0, 0.5))
    trim = bpy.context.active_object
    trim.name = "Trim"
    parts = [(wall, "concrete"), (trim, "paint")]
    try:
        cs2_bake.material_sources({"concrete": cs2_bake.MaterialSpec(texture_set="Wood999"),
                                   "paint": {"albedo": (0.5, 0.5, 0.5)}}, parts)
        check(False, "missing set raises")
    except FileNotFoundError as e:
        check("Wood999" in str(e), f"missing set raises FileNotFoundError ({str(e)[:60]}...)")
    check("UVTile" not in wall.data.uv_layers and len(wall.data.materials) == 0,
          "nothing touched before the failure")

    table = {"concrete": cs2_bake.MaterialSpec(texture_set="Concrete034", metres_per_tile=2.0,
                                               tint=0.6),
             "paint": cs2_bake.MaterialSpec(albedo=(0.3, 0.1, 0.1), slot=1,
                                            recolor_albedo=True, projection="planar")}
    wall.location = (0.0, 4.0, 1.0)         # 2 tiles in y; stale matrix_world would say 0
    srcs = cs2_bake.material_sources(table, parts)
    wtop = max(wall.data.polygons, key=lambda p: p.normal.z)
    wuv = cs2_uv.loop_uvs(wall.data.uv_layers["UVTile"])[wtop.loop_start:
                                                         wtop.loop_start + wtop.loop_total]
    check(abs(float(wuv[:, 1].mean()) - 2.0) < 1e-4,
          f"material_sources after `wall.location = (0, 4, 1)`: top face v center "
          f"{wuv[:, 1].mean():.3f} tiles (2.0 at 2 m/tile)")
    check(sorted(srcs) == [0, 1] and srcs[0].normal_convention == "GL" and srcs[0].uv == "UVTile"
          and srcs[1].slot == 1, "indices by sorted key, GL normal, uv = UVTile")
    tile = cs2_uv.loop_uvs(wall.data.uv_layers["UVTile"])
    span = []
    for p in wall.data.polygons:
        q = tile[p.loop_start:p.loop_start + p.loop_total]
        span.append(tuple(np.round(q.max(axis=0) - q.min(axis=0), 4)))
    check(all(s == (1.0, 1.0) for s in span), f"2 m faces span 1 tile at 2 m/tile ({span[:2]}..)")
    check(wall.data.uv_layers.active.name == "UVMap", "active (pack source) layer unchanged")
    bpy.ops.object.select_all(action="DESELECT")
    wall.select_set(True)
    trim.select_set(True)
    bpy.context.view_layer.objects.active = wall
    bpy.ops.object.join()
    idx = [p.material_index for p in wall.data.polygons]
    check(sorted(set(idx)) == [0, 1] and len(wall.data.materials) == 2,
          f"join keeps the indices ({len(wall.data.materials)} slots)")
    rep = cs2_bake.bake_unique(wall, out_dir, "TileWallA", A, srcs, ao_samples=0)
    base = read_png(os.path.join(out_dir, "TileWallA_BaseColor.png"))
    nrm = read_png(os.path.join(out_dir, "TileWallA_Normal.png"))
    ctl = read_png(os.path.join(out_dir, "TileWallA_ControlMask.png"))
    _parts, ppm = cs2_compose.rasterise_face_fields(wall.data, A, "UVUnique")
    cov = ppm > 0
    conc_tex = np.concatenate([face_texels(wall.data, i, base) for i, m in enumerate(idx)
                               if m == 0])
    paint_tex = np.concatenate([face_texels(wall.data, i, base) for i, m in enumerate(idx)
                                if m == 1])
    paint_ctl = np.concatenate([face_texels(wall.data, i, ctl) for i, m in enumerate(idx)
                                if m == 1])
    check(float(conc_tex[:, 0:3].std()) > 2.0, f"concrete faces carry the photo texture "
                                               f"(std {conc_tex[:, 0:3].std():.1f})")
    want = int(round(cs2_asset.RECOLOR_ALBEDO * 255))
    check(int(np.abs(paint_tex[:, 0:3] - want).max()) <= 2, "paint faces neutral 0.80 gray")
    check(bool((paint_ctl[:, 0:3] == (255, 0, 0)).all()), "paint faces ControlMask R")
    nc = nrm[cov][:, 0:3] / 255.0
    check(float(nc[:, 2].min()) > 0.5 and abs(float(nc[:, 0].mean()) - 0.5) < 0.05,
          f"tiling NormalGL through the cross-layer NORMAL bake: B min {nc[:, 2].min():.3f}, "
          f"R mean {nc[:, 0].mean():.3f}")
    check(rep["layers_sampled"] == ["UVTile"] and [l.name for l in wall.data.uv_layers]
          == ["UVUnique"], f"sampled {rep['layers_sampled']}, shipped one layer")
    check(leftovers() == [], f"placeholders removed ({leftovers()})")


# --------------------------------------------------------------------------------------

def main():
    t0 = time.perf_counter()
    root = tempfile.mkdtemp(prefix="cs2_test_bake_")
    try:
        src_dir = os.path.join(root, "src")
        os.makedirs(src_dir)
        paths = make_sources(src_dir)
        for fn, args in ((test_cube, (paths, os.path.join(root, "BakeCubeA"),
                                      os.path.join(root, "BakeCubeA_run2"))),
                         (test_high_to_low, (os.path.join(root, "BevelLowA"),)),
                         (test_ramp_sign, (os.path.join(root, "RampLowA"),)),
                         (test_material_sources, (os.path.join(root, "TileWallA"),)),
                         (test_atlas_sources, (os.path.join(root, "AtlasCubeA"),)),
                         (test_failure_restores, (paths, os.path.join(root, "FailCubeA")))):
            for a in args[1:] if fn in (test_cube, test_failure_restores) else args:
                os.makedirs(a, exist_ok=True)
            try:
                fn(*args)
            except Exception:  # noqa: BLE001 - report and keep going
                traceback.print_exc()
                FAILS.append(f"{fn.__name__} raised")
    finally:
        shutil.rmtree(root, ignore_errors=True)
    dt = time.perf_counter() - t0
    if FAILS:
        print(f"FAIL test_bake: {len(FAILS)} check(s) failed in {dt:.1f} s: {FAILS}")
        sys.exit(1)
    print(f"PASS test_bake: all checks passed in {dt:.1f} s")


main()
