"""
test_validate.py - exercises cs2_validate against a tiny generated asset folder.

    blender --background --factory-startup --python tests\\test_validate.py

1. A valid folder validates with zero ERRORs. The folder holds a LOD0 cylinder, a LOD1 box, 512 px
   maps from make_texture_set, settings.json with LOD1 Skip, and a manifest.
2. Four corruptions are each caught with the expected code: an underscore in the name, a BaseColor
   alpha 0.3 patch, MaskMap B non-zero, and a LOD1 FBX declared but missing.

Per-part texel density (props): with LOD0 UVs shrunk to 1/10, the check gives a texel-density ERROR.
It gives no ERROR on kind building. Texture floor (wiki 512): a 256 px set gives a texture-size ERROR
on a prop AND on a building (REDO 2026-10-17).

3. validate_mesh(kind="building"): a closed box on z=0 reports engine base BLOCKED. An open-bottom box
   reports OK (ground_ring_report verdicts likewise).

The test prints one PASS line and exits 0. Any failure prints FAIL and exits 1.
"""

import os
import shutil
import sys
import tempfile
import traceback

import bpy
import bmesh
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import cs2_asset      # noqa: E402
import cs2_validate   # noqa: E402

NAME = "SkiValTestA"
TEX = 512            # the wiki floor (cs2_validate DEFAULT_SPEC min_texture)
CHECKS = 0


def check(cond, msg):
    global CHECKS
    CHECKS += 1
    if not cond:
        raise AssertionError(msg)


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for me in list(bpy.data.meshes):
        if me.users == 0:
            bpy.data.meshes.remove(me)


def box(name, size=(1.0, 1.0, 1.0), open_bottom=False):
    """Axis-aligned box sitting on z=0 (bottom edges exactly at z=0)."""
    bpy.ops.mesh.primitive_cube_add(size=1.0)
    o = bpy.context.active_object
    o.name = name
    o.data.name = name
    o.scale = size
    o.location = (0.0, 0.0, size[2] * 0.5)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if open_bottom:
        bm = bmesh.new()
        bm.from_mesh(o.data)
        kill = [f for f in bm.faces if f.normal.z < -0.9]
        bmesh.ops.delete(bm, geom=kill, context="FACES")
        bm.to_mesh(o.data)
        bm.free()
    return o


def build_folder(root):
    """LOD0 is a 12-seg cylinder (44 tris). LOD1 is a box (12 tris). The maps are 512 px. The folder also
    gets settings."""
    clear_scene()
    os.makedirs(root, exist_ok=True)
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.4, depth=1.2, location=(0, 0, 0.6))
    lod0 = bpy.context.active_object
    lod0.name = NAME
    lod0.data.name = NAME
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    cs2_asset.shade_by_angle(lod0, 66.0, weighted=False)
    lod1 = box(f"{NAME}_LOD1", (0.8, 0.8, 1.2))
    cs2_asset.shade_by_angle(lod1, 60.0, weighted=False)
    cs2_asset.export_fbx(lod0, os.path.join(root, f"{NAME}.fbx"))
    cs2_asset.export_fbx(lod1, os.path.join(root, f"{NAME}_LOD1.fbx"))
    cs2_asset.make_texture_set(root, NAME, size=TEX, control_rgb=(1.0, 0.0, 0.0))
    cs2_asset.write_settings(root, NAME, supplied_lods=(1,))
    return lod0, lod1


def rewrite_png(root, suffix, mutate, is_data):
    path = os.path.join(root, f"{NAME}_{suffix}.png")
    px = cs2_validate.load_png_pixels(path)
    mutate(px)
    cs2_asset._write_png_raw(path, f"{NAME}_{suffix}", px, px.shape[0], is_data)


def codes(findings, level="ERROR"):
    return {c for l, c, _m in findings if l == level}


def run():
    tmp = tempfile.mkdtemp(prefix="cs2_validate_test_")
    root = os.path.join(tmp, NAME)
    try:
        # -- 1. valid folder ---------------------------------------------------------
        lod0, lod1 = build_folder(root)
        n_before = len(bpy.data.objects)
        cs2_validate.write_manifest(root, NAME, {"generator": "test_validate", "spec": {"lod1": 1}})
        check(os.path.isfile(os.path.join(root, f"{NAME}.manifest.json")), "manifest not written")
        findings = cs2_validate.validate_asset_folder(root, NAME, {"kind": "prop", "hero": False})
        check(not codes(findings), f"valid folder produced errors {codes(findings)}")
        check(len(bpy.data.objects) == n_before, "temporary FBX imports were not removed")
        check(any(c == "budget" for _l, c, _m in findings), "no budget findings")
        check(not any(c == "folder" and l == "WARN" and "manifest" in m
                      for l, c, m in findings), "manifest flagged as unexpected")
        print(f"[test] valid folder: {len(findings)} findings, 0 errors")

        # -- 2a. underscore in the name ------------------------------------------------
        try:
            cs2_validate.validate_asset_folder(root, "Ski_ValTestA", {"kind": "prop"})
            check(False, "underscore name did not raise")
        except cs2_validate.AssetValidationError as e:
            check("name" in codes(e.findings), f"underscore name: codes {codes(e.findings)}")
            check("Ski_ValTestA" in str(e), "exception message lacks the name")
        print("[test] underscore name caught")

        # -- 2b. BaseColor alpha 0.3 patch ---------------------------------------------
        def alpha_patch(px):
            px[8:24, 8:24, 3] = 0.3
        rewrite_png(root, "BaseColor", alpha_patch, is_data=False)
        try:
            cs2_validate.validate_asset_folder(root, NAME, {"kind": "prop"})
            check(False, "alpha patch did not raise")
        except cs2_validate.AssetValidationError as e:
            check(codes(e.findings) == {"basecolor-alpha"},
                  f"alpha patch: codes {codes(e.findings)}")
        # cutout spec accepts it
        findings = cs2_validate.validate_asset_folder(root, NAME, {"kind": "prop", "cutout": True})
        check(not codes(findings), "cutout spec still errored on alpha")
        cs2_asset.make_texture_set(root, NAME, size=TEX, control_rgb=(1.0, 0.0, 0.0))
        print("[test] BaseColor alpha patch caught")

        # -- 2c. MaskMap B non-zero ----------------------------------------------------
        def mask_b(px):
            px[40:60, 40:60, 2] = 0.5
        rewrite_png(root, "MaskMap", mask_b, is_data=True)
        try:
            cs2_validate.validate_asset_folder(root, NAME, {"kind": "prop"})
            check(False, "MaskMap B did not raise")
        except cs2_validate.AssetValidationError as e:
            check(codes(e.findings) == {"maskmap-b"}, f"MaskMap B: codes {codes(e.findings)}")
        cs2_asset.make_texture_set(root, NAME, size=TEX, control_rgb=(1.0, 0.0, 0.0))
        print("[test] MaskMap B caught")

        # -- 2d. declared LOD1 FBX missing ---------------------------------------------
        os.remove(os.path.join(root, f"{NAME}_LOD1.fbx"))
        try:
            cs2_validate.validate_asset_folder(root, NAME, {"kind": "prop"})
            check(False, "missing LOD1 did not raise")
        except cs2_validate.AssetValidationError as e:
            check("lod-missing" in codes(e.findings), f"missing LOD1: codes {codes(e.findings)}")
        cs2_asset.export_fbx(lod1, os.path.join(root, f"{NAME}_LOD1.fbx"))
        findings = cs2_validate.validate_asset_folder(root, NAME, {"kind": "prop"})
        check(not codes(findings), "restored folder still errors")
        print("[test] missing declared LOD1 caught")

        # -- 2e. extra: stray png + unknown fbx suffix + ControlMask alpha ----------------
        stray = os.path.join(root, f"{NAME}_Preview.png")
        shutil.copyfile(os.path.join(root, f"{NAME}_Normal.png"), stray)
        shutil.copyfile(os.path.join(root, f"{NAME}_LOD1.fbx"), os.path.join(root, f"{NAME}_Foo.fbx"))

        def ctl_a(px):
            px[..., 3] = 1.0
        rewrite_png(root, "ControlMask", ctl_a, is_data=True)
        try:
            cs2_validate.validate_asset_folder(root, NAME, {"kind": "prop"})
            check(False, "stray files did not raise")
        except cs2_validate.AssetValidationError as e:
            c = codes(e.findings)
            check({"folder", "controlmask-a"} <= c, f"stray/ctl: codes {c}")
        os.remove(stray)
        os.remove(os.path.join(root, f"{NAME}_Foo.fbx"))
        cs2_asset.make_texture_set(root, NAME, size=TEX, control_rgb=(1.0, 0.0, 0.0))
        print("[test] stray PNG / unknown FBX / ControlMask alpha caught")

        # -- 2f. per-part texel density (2026-10-17, snow props B: shells at 12-22 px/m) ----
        info = [m for l, c, m in findings if c == "texel-density"]
        check(info, "no texel-density finding on a prop folder")
        uvl = lod0.data.uv_layers.active
        saved = [tuple(d.uv) for d in uvl.data]
        for d in uvl.data:
            d.uv = (d.uv[0] * 0.1, d.uv[1] * 0.1)          # 1/10 of the UV area per axis
        cs2_asset.export_fbx(lod0, os.path.join(root, f"{NAME}.fbx"))
        try:
            cs2_validate.validate_asset_folder(root, NAME, {"kind": "prop"})
            check(False, "a 4 m2 part at ~4 px/m did not raise")
        except cs2_validate.AssetValidationError as e:
            check("texel-density" in codes(e.findings), f"density: codes {codes(e.findings)}")
        f_bld = cs2_validate.validate_asset_folder_ex(root, NAME, {"kind": "building"})[0]
        check("texel-density" not in codes(f_bld), "density gate fired on a building (props only)")
        for d, uv in zip(uvl.data, saved):
            d.uv = uv
        cs2_asset.export_fbx(lod0, os.path.join(root, f"{NAME}.fbx"))
        findings = cs2_validate.validate_asset_folder(root, NAME, {"kind": "prop"})
        check(not codes(findings), "restored UVs still error")
        print("[test] per-part texel density floor caught")

        # -- 2g. texture floor (REDO 2026-10-17: the wiki's 512 minimum, an ERROR for every kind) ----
        cs2_asset.make_texture_set(root, NAME, size=256, control_rgb=(1.0, 0.0, 0.0))
        for kind in ("prop", "building"):
            f_small = cs2_validate.validate_asset_folder_ex(root, NAME, {"kind": kind})[0]
            check("texture-size" in codes(f_small), f"256 px set on a {kind}: codes {codes(f_small)}")
        cs2_asset.make_texture_set(root, NAME, size=TEX, control_rgb=(1.0, 0.0, 0.0))
        findings = cs2_validate.validate_asset_folder(root, NAME, {"kind": "prop"})
        check(not codes(findings), "512 px set still errors")
        print("[test] texture floor 512 caught (prop + building)")

        # -- 3. engine-base rule -------------------------------------------------------
        clear_scene()
        closed = box("SkiValClosed", (3.0, 3.0, 1.0), open_bottom=False)
        r = cs2_validate.ground_ring_report(closed)
        # 4 ring edges plus the bottom face's triangulation diagonal (the engine sees triangles)
        check(r["verdict"] == "BLOCKED" and r["blocked_edges"] >= 4 and r["ok_edges"] == 0,
              f"closed box report {r}")
        f_closed = cs2_validate.validate_mesh(closed, kind="building")
        check(("ERROR", "engine-base") in {(l, c) for l, c, _m in f_closed},
              f"closed box findings {f_closed}")
        check(any("BLOCKED" in m for l, c, m in f_closed if c == "engine-base"), "no BLOCKED text")

        opened = box("SkiValOpen", (3.0, 3.0, 1.0), open_bottom=True)
        r = cs2_validate.ground_ring_report(opened)
        check(r["verdict"] == "OK" and r["ok_edges"] == 4 and r["blocked_edges"] == 0,
              f"open box report {r}")
        f_open = cs2_validate.validate_mesh(opened, kind="building")
        check(("INFO", "engine-base") in {(l, c) for l, c, _m in f_open}, f"open box findings {f_open}")
        check(any("OK" in m for l, c, m in f_open if c == "engine-base"), "no OK text")
        check("engine-base" not in codes(f_open), "open box reported an engine-base ERROR")

        # a skirt continuing below z=0 blocks the ring too (the K-generation pattern)
        skirt = box("SkiValSkirt", (3.0, 3.0, 1.0), open_bottom=True)
        bm = bmesh.new()
        bm.from_mesh(skirt.data)
        bottom = [e for e in bm.edges if all(abs(v.co.z) < 1e-6 for v in e.verts)]
        res = bmesh.ops.extrude_edge_only(bm, edges=bottom)
        verts = [g for g in res["geom"] if isinstance(g, bmesh.types.BMVert)]
        bmesh.ops.translate(bm, verts=verts, vec=(0.0, 0.0, -0.5))
        bm.to_mesh(skirt.data)
        bm.free()
        r = cs2_validate.ground_ring_report(skirt)
        check(r["verdict"] == "BLOCKED" and r["below_verts"] == 4, f"skirt report {r}")

        # prop ground contact + contracts
        f_prop = cs2_validate.validate_mesh(opened, kind="prop",
                                            contracts={"one meter tall": lambda o: abs(o.dimensions.z - 1.0) < 1e-6,
                                                       "never": lambda o: (False, "by design")})
        check("ground" not in codes(f_prop), f"prop on ground errored {f_prop}")
        check(sum(1 for l, c, m in f_prop if c == "contract" and l == "ERROR") == 1, "contract count")
        opened.location.z = 0.2
        f_float = cs2_validate.validate_mesh(opened, kind="prop")
        check("ground" in codes(f_float), "floating prop not caught")
        print("[test] engine-base rule: closed BLOCKED, open OK, skirt BLOCKED; prop ground rule")

        # -- 4. uv rasterizer sanity ---------------------------------------------------
        cov = cs2_validate.uv_coverage(opened.data, 64)
        check(cov.max() >= 1 and cov.shape == (64, 64), "coverage raster empty")
        covered, over, frac = cs2_validate.uv_overlap_fraction(opened.data, 64)
        check(frac < 0.01, f"default cube UVs reported overlapping {frac}")
        print("[test] uv raster ok")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    try:
        run()
    except Exception:
        traceback.print_exc()
        print("FAIL: test_validate")
        sys.exit(1)
    print(f"PASS: test_validate ({CHECKS} checks)")
    sys.exit(0)
