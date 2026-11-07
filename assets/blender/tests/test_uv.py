"""
test_uv.py - contract test for cs2_uv (pack_unique / finish_packer / density_report / set_density).

Run:  blender --background --factory-startup --python tests\test_uv.py

Scene: six boxes of different sizes. The test smart-projects each box into an atlas quadrant the way
the lift generators do (cs2_asset.unwrap_into_quadrant). It also adds a strip plane whose UVs sit
inside the crash-pad reserved rect (the pinned island). The test joins all parts with part tags.

The test asserts:
- every loop is in [0, 1]
- pinned loops are byte-identical to their source UVs
- no non-pinned loop is inside a reserved rect
- the per-face px/m spread (max/min) of the non-pinned faces is within 2x after
  average_islands_scale
- two fresh builds pack to identical UV hashes
- finish_packer on a lift_tower-style pre-made "UVUnique" layer satisfies the same contract
- density_w scales an island's density
- density_report returns one row per part

The test exits 1 on any failure and prints one PASS line on success.
"""

import hashlib
import math
import os
import sys
import traceback

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import cs2_asset  # noqa: E402
import cs2_uv  # noqa: E402

A = 512
RESERVED = [(cs2_asset.PAD_U0, cs2_asset.PAD_V0, cs2_asset.PAD_U1, cs2_asset.PAD_V1)]
BOX_SIZES = [(0.5, 0.5, 0.5), (1.0, 1.0, 1.0), (2.0, 2.0, 2.0),
             (3.0, 1.0, 1.5), (0.3, 4.0, 0.3), (2.0, 0.2, 1.0)]


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for me in list(bpy.data.meshes):
        if me.users == 0:
            bpy.data.meshes.remove(me)


def add_box(name, size, loc):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.data.name = name
    o.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return o


def add_strip(name, w, h, loc):
    bpy.ops.mesh.primitive_plane_add(size=1.0, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.data.name = name
    o.scale = (w, h, 1.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return o


def build(weights=None):
    """Six boxes + pinned strip, unwrapped, tagged, joined. Returns (obj, table)."""
    clear_scene()
    parts = []
    for i, size in enumerate(BOX_SIZES):
        o = add_box(f"box{i}_", size, (i * 5.0, 0.0, 0.0))
        parts.append((o, i % 4))
    strip = add_strip("strip_", 4.0, 0.3, (0.0, 8.0, 0.0))
    parts.append((strip, 2))
    for obj, quad in parts[:-1]:
        cs2_asset.unwrap_into_quadrant(obj, quad)
    # the strip island lives INSIDE the reserved crash-pad rect (3 px inset @ A)
    cs2_asset.unwrap_into_rect(strip, RESERVED[0], A, inset_px=3)
    cs2_uv.set_density(parts, weights or {})
    table = cs2_asset.tag_parts(parts)
    bpy.ops.object.select_all(action="DESELECT")
    for obj, _q in parts:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()
    joined = bpy.context.active_object
    joined.name = "UvTest"
    cs2_asset.store_part_table(joined, table)
    joined.data.uv_layers.active.name = "UVAtlas"
    return joined, table


def uv_hash(layer):
    arr = np.round(cs2_uv.loop_uvs(layer), 6)
    return hashlib.sha1(arr.astype(np.float64).tobytes()).hexdigest()


def strip_face_indices(mesh, table):
    idx = table.index("strip_") + 1
    attr = mesh.attributes["part"]
    return [i for i, v in enumerate(attr.data) if v.value == idx]


def check_contract(obj, table, tag):
    """Common assertions on the 'UVUnique' layer against 'UVAtlas'."""
    mesh = obj.data
    src = cs2_uv.loop_uvs(mesh.uv_layers["UVAtlas"])
    out = cs2_uv.loop_uvs(mesh.uv_layers["UVUnique"])
    assert len(src) == len(out) and len(out) > 0, f"{tag}: loop count changed"
    # 1. all loops in [0, 1]
    assert out.min() >= -1e-6 and out.max() <= 1.0 + 1e-6, \
        f"{tag}: loops outside [0,1]: {out.min()} .. {out.max()}"
    # 2. pinned loops unchanged
    strip_faces = strip_face_indices(mesh, table)
    assert strip_faces, f"{tag}: strip part lost"
    pinned = cs2_uv.pinned_faces(mesh, mesh.uv_layers["UVUnique"], RESERVED)
    assert set(strip_faces) <= pinned, f"{tag}: strip faces not detected as pinned"
    start, total = cs2_uv.face_loop_table(mesh)
    pin_loops = np.concatenate([np.arange(start[f], start[f] + total[f]) for f in strip_faces])
    assert np.array_equal(src[pin_loops], out[pin_loops]), f"{tag}: pinned loops moved"
    # 3. no non-pinned loop inside a reserved rect
    mask = np.ones(len(out), dtype=bool)
    mask[pin_loops] = False
    for (u0, v0, u1, v1) in RESERVED:
        hit = mask & (out[:, 0] > u0) & (out[:, 0] < u1) & (out[:, 1] > v0) & (out[:, 1] < v1)
        assert not hit.any(), f"{tag}: {int(hit.sum())} non-pinned loops inside reserved rect"
    # non-pinned islands were moved by the pack (they were spread over 4 quadrants)
    assert not np.array_equal(src[mask], out[mask]), f"{tag}: non-pinned loops untouched"
    # 4. density spread of the non-pinned faces within 2x
    dens = cs2_uv.face_px_per_m(mesh, mesh.uv_layers["UVUnique"], A)
    free = np.array([i for i in range(len(mesh.polygons)) if i not in pinned])
    d = dens[free]
    assert np.isfinite(d).all() and d.min() > 0, f"{tag}: bad density values"
    spread = d.max() / d.min()
    print(f"[test] {tag}: px/m min {d.min():.1f} max {d.max():.1f} spread {spread:.3f}x")
    return spread


def main():
    # ---- run 1: pack_unique -------------------------------------------------------------
    obj, table = build()
    assert cs2_asset.tri_count(obj) < 5000
    report = cs2_uv.pack_unique(obj, A, RESERVED)
    assert report["pinned_faces"] >= 1 and report["margin_px"] == 16, report
    assert "UVUnique" in obj.data.uv_layers and obj.data.uv_layers.active.name == "UVUnique"
    spread1 = check_contract(obj, table, "pack_unique")
    assert spread1 <= 2.0, f"density spread {spread1:.2f}x > 2x"
    h1 = uv_hash(obj.data.uv_layers["UVUnique"])
    n_faces = len(obj.data.polygons)          # obj dies with the next clear_scene()
    rows = cs2_uv.density_report(obj, A, reserved_rects=RESERVED)
    assert len(rows) == len(table), f"density_report rows {len(rows)} != parts {len(table)}"
    assert all(len(r) == 3 and r[1] > 0 and r[2] > 0 for r in rows), rows

    # ---- run 2: determinism -------------------------------------------------------------
    obj2, table2 = build()
    cs2_uv.pack_unique(obj2, A, RESERVED)
    h2 = uv_hash(obj2.data.uv_layers["UVUnique"])
    assert h1 == h2, f"non-deterministic pack: {h1} != {h2}"

    # ---- run 3: finish_packer on a lift_tower-style pre-made UVUnique -------------------
    obj3, table3 = build()
    mesh3 = obj3.data
    uniq = mesh3.uv_layers.new(name="UVUnique", do_init=True)
    mesh3.uv_layers.active = uniq
    ret = cs2_uv.finish_packer(obj3, mesh3, A, RESERVED)
    assert ret is None
    assert bpy.context.object.mode == "OBJECT"
    spread3 = check_contract(obj3, table3, "finish_packer")
    assert spread3 <= 2.0
    h3 = uv_hash(mesh3.uv_layers["UVUnique"])
    assert h3 == h1, f"finish_packer differs from pack_unique: {h3} != {h1}"

    # ---- run 4: density_w doubles one box's density -------------------------------------
    obj4, table4 = build(weights={"box2_": 2.0})
    cs2_uv.pack_unique(obj4, A, RESERVED)
    mesh4 = obj4.data
    dens = cs2_uv.face_px_per_m(mesh4, mesh4.uv_layers["UVUnique"], A)
    part = np.zeros(len(mesh4.polygons), dtype=np.int32)
    mesh4.attributes["part"].data.foreach_get("value", part)
    hero = table4.index("box2_") + 1
    plain = table4.index("box1_") + 1
    ratio = float(np.median(dens[part == hero]) / np.median(dens[part == plain]))
    print(f"[test] density_w 2.0: hero/plain px/m ratio {ratio:.3f}")
    assert 1.7 <= ratio <= 2.3, f"density_w not honored: ratio {ratio:.3f}"
    check_contract(obj4, table4, "density_w")

    print(f"PASS test_uv: {len(table)} parts, {n_faces} faces, atlas {A}, "
          f"spread {spread1:.2f}x, hash {h1[:12]}, density_w ratio {ratio:.2f}")


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        print("FAIL test_uv")
        sys.exit(1)
    sys.exit(0)
