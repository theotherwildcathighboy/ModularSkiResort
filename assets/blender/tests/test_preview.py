"""
test_preview.py - exercises cs2_preview on a 512-textured box in every mode at 400x300.

Run:  blender --background --factory-startup --python tests\test_preview.py
Exits 1 on failure, prints one PASS line on success. Small by design: 512 px maps, a
12-tri box (+ a 12-tri LOD1 and a re-imported "previous generation" copy), 4 samples.
"""
import os
import sys
import tempfile

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import cs2_asset    # noqa: E402
import cs2_preview  # noqa: E402

FAILS = []


def check(cond, msg):
    if not cond:
        FAILS.append(msg)
        print(f"[test_preview] FAIL: {msg}")


def load_png(path):
    img = bpy.data.images.load(path)
    w, h = img.size
    buf = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(buf)
    bpy.data.images.remove(img)
    return buf.reshape(h, w, 4)


def main():
    # --- lod_switch_table sanity (spec: csum 32, tris 3740/1682/500 -> LOD1 in 100..200 m)
    t = cs2_preview.lod_switch_table((12.0, 10.0, 10.0), (3740, 1682, 500), lod_factor=652.0)
    print(f"[test_preview] switch table: {t}")
    check(100.0 < t["switch"][1] < 200.0, f"LOD1 switch {t['switch'][1]:.1f} m not in 100..200")
    check(t["switch"][2] > t["switch"][1], "LOD2 switch must be farther than LOD1")
    check(t["cull"] > t["switch"][2], "cull must be farther than LOD2 switch")
    check(abs(t["distances"][0] - 652.0 * 32.0 * 0.57735 / (3.0 * 3740) ** 0.5) < 1e-6, "d0 formula")
    thin = cs2_preview.lod_switch_table((12.0, 10.0, 10.0), (3740, 1682), area=100.0)
    check(thin["bias"] > 0.0 and thin["switch"][1] < t["switch"][1], "thin-mesh bias must pull distances in")

    # --- scene: textured box LOD0, a LOD1 copy, a "previous generation" export
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    name = "SkiPreviewBoxT"
    work = tempfile.mkdtemp(prefix="cs2_preview_test_")
    asset_dir = os.path.join(work, name)
    prev_dir = os.path.join(work, "prev", name)
    out_dir = os.path.join(work, "preview")
    os.makedirs(prev_dir, exist_ok=True)

    bpy.ops.mesh.primitive_cube_add(size=2.0, location=(0.0, 0.0, 1.0))
    box = bpy.context.active_object
    box.name = name
    box.data.name = name
    box.scale = (1.5, 1.0, 1.0)
    bpy.ops.object.transform_apply(scale=True)
    cs2_asset.shade_by_angle(box, 66.0)
    cs2_asset.make_texture_set(asset_dir, name, size=512, base_rgb=(0.80, 0.80, 0.80),
                               metallic=0.2, smoothness=0.4, control_rgb=(1.0, 0.0, 0.0))
    # a tinted "previous generation" (green slot-2 mask) so the ab frame differs
    cs2_asset.make_texture_set(prev_dir, name, size=512, base_rgb=(0.80, 0.80, 0.80),
                               control_rgb=(0.0, 1.0, 0.0))
    cs2_asset.export_fbx(box, os.path.join(prev_dir, f"{name}.fbx"))

    lod1 = box.copy()
    lod1.data = box.data.copy()
    lod1.name = f"{name}_LOD1"
    lod1.data.name = lod1.name
    bpy.context.collection.objects.link(lod1)
    lod1.scale = (1.0, 1.15, 0.85)      # different silhouette -> non-zero XOR
    bpy.ops.object.select_all(action="DESELECT")
    lod1.select_set(True)
    bpy.context.view_layer.objects.active = lod1
    bpy.ops.object.transform_apply(scale=True)

    n_objects = len(bpy.data.objects)
    paths = cs2_preview.preview(box, asset_dir, name, out_dir,
                                modes=("pbr", "dusk", "normals", "lod", "density", "ab"),
                                lods=[lod1], prev_generation=prev_dir,
                                resolution=(400, 300), samples=4)
    print(f"[test_preview] {len(paths)} frames written to {out_dir}")

    expected_modes = ["pbr", "dusk", "normals", "density", "ab", "lod1"]
    for mode in expected_modes:
        hits = [p for p in paths if f"{name}_{mode}_" in os.path.basename(p)]
        check(len(hits) >= 1, f"no frame for mode {mode}")
    check(len([p for p in paths if "_pbr_" in p]) == 3, "pbr must write the three default views")
    for p in paths:
        check(os.path.exists(p), f"missing {p}")
        if not os.path.exists(p):
            continue
        px = load_png(p)
        std = float(px[..., :3].std())
        check(std > 0.01, f"uniform image (std {std:.4f}): {p}")
        check(px.shape[1] >= 400 or "_lod1_" in p, f"unexpected width {px.shape[1]}: {p}")
        print(f"[test_preview]   {os.path.basename(p)}  {px.shape[1]}x{px.shape[0]}  std {std:.3f}")

    lod_frames = [p for p in paths if "_lod1_" in p]
    check(len(lod_frames) == 2, f"lod mode should write d and d/2 frames, got {len(lod_frames)}")
    check(all("m_xor" in p and "pct.png" in p for p in lod_frames), "lod caption filename")
    pcts = []
    for p in lod_frames:
        try:
            pcts.append(float(os.path.basename(p).split("_xor")[1].split("pct")[0]))
        except (IndexError, ValueError):
            check(False, f"cannot parse xor percentage from {p}")
    check(all(0.0 < v < 100.0 for v in pcts), f"xor percentages {pcts} must be non-trivial")

    # pbr frame carries the steel tint: slot-1 mask -> darker than the 0.80 albedo would give
    ab = [p for p in paths if "_ab_three_quarter" in p]
    if ab:
        px = load_png(ab[0])
        w = px.shape[1] // 2
        left, right = px[:, :w - 8, :3], px[:, w + 8:, :3]
        check(abs(float(left.mean()) - float(right.mean())) > 0.01, "ab halves should differ (steel vs pad tint)")

    # --- the rig cleaned up after itself
    check(len(bpy.data.objects) == n_objects, f"rig left objects behind: {[o.name for o in bpy.data.objects]}")
    check(len(box.data.materials) == 0, "object material slots not restored")
    check("cs2_preview_density" not in box.data.color_attributes, "density color attribute not removed")
    check(bpy.context.scene.camera is None, "scene camera not restored")
    check(not any(m.name.startswith(name + "_Preview") for m in bpy.data.materials), "preview materials not removed")
    check(not lod1.hide_render, "hide_render not restored on the LOD")

    if FAILS:
        print(f"[test_preview] FAIL ({len(FAILS)} problems)")
        sys.exit(1)
    print(f"[test_preview] PASS: {len(paths)} preview frames, 6 modes, switch table LOD1 {t['switch'][1]:.0f} m")


if __name__ == "__main__":
    main()
