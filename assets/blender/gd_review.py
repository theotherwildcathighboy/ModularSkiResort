"""
gd_review.py - fixed-camera review renders for the gondola lift family (r4 texture pass).

tex_review.py was written for the lodges: its shots assume an arrival side on -Y, one asset per
frame and a recipe tint. This script keeps its rig, loaders, camera store and sheet / diff code and
adds what the lift family needs:

  - shot sets per kind (station: four sides, top, entry and plinth close-ups, play distance, far;
    tower / lattice: along the line, across it, head and base close-ups, play, far; cabin: 6 m and
    30 m)
  - several assets in one frame (cabin body + grip, each with its own maps and offset)
  - the recolor tint given as the prefab's ColorProperties values. The game reads them as sRGB
    (docs/PIPELINE.md, "The game reads ColorProperties tint channels as sRGB"), so --tint-space srgb
    (default) converts them to linear for the Blender material: the frame shows the game's colors
  - a far ground plane for hanging assets (--ground-z)

Headless only:
  blender --background --factory-startup --python-exit-code 1 --python assets/blender/gd_review.py -- <mode> ...

render --out DIR --tag TAG --kind station|tower|cabin --asset "<asset_dir>|<Name>[|dx,dy,dz]" [--asset ...]
       --tint "R=r,g,b;G=r,g,b;B=r,g,b" [--tint-space srgb|linear] [--cams FILE] [--shots a,b] [--ground-z Z]
       [--lod N] (N = 1 | 2: draws <Name>_LOD<N>.fbx with the LOD0 maps)
render --out DIR --tag vanilla --kind station --vanilla <RenderPrefab base> --bench DIR --vanilla-scripts DIR
sheet  --out FILE --kind KIND --cols "LABEL=DIR,LABEL=DIR" [--shots a,b] [--scale 3]
diff   --a DIR_A --b DIR_B --out DIR

Read-only on the game, the mod and every asset folder; writes only under --out.
"""

import json
import os
import sys

import bpy
import mathutils
import numpy as np

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)
import cs2_preview as P   # noqa: E402
import tex_review as T    # noqa: E402

# (name, camera direction from the target, distance m, aim). Directions are in the EXPORTED frame
# (after cs2_asset.orient_for_net + export, Z up). A station's nose (line side) is +Y, its long
# glazed sides face +-X. A tower's line runs along Y. Aim kinds are tex_review.aim_point's, plus "body" (bounds center)
# and "bbox:<f>" (where the horizontal ray from the bounds center along the direction leaves the bounds, at f x the
# height: a glazed hall has no facade for a ray cast, the ray goes through the panes).
SHOTS = {
    "station": (
        ("side_p_d45", (1.0, 0.12, 0.28), 45.0, "center"),
        ("side_n_d45", (-1.0, -0.12, 0.28), 45.0, "center"),
        ("nose_d45", (0.15, 1.0, 0.25), 45.0, "center"),
        ("back_d45", (-0.15, -1.0, 0.25), 45.0, "center"),
        ("top_d55", (0.02, -0.05, 1.0), 55.0, "center"),
        ("q_d30", (0.8, 0.75, 0.42), 30.0, "center"),
        ("wall_d8", (1.0, 0.30, 0.10), 8.0, "bbox:0.30"),
        ("eave_d10", (1.0, 0.25, -0.72), 10.0, "bbox:0.83"),
        ("plinth_d6", (1.0, -0.55, 0.10), 6.0, "low"),
        ("portal_d14", (0.35, 1.0, 0.12), 14.0, "facade"),
        ("play_d60", (0.75, 0.85, 0.70), 60.0, "center"),
        ("play_back_d60", (-0.8, -0.75, 0.70), 60.0, "center"),
        ("far_d150", (0.75, 0.85, 0.55), 150.0, "center"),
    ),
    "tower": (
        ("line_d30", (0.08, -1.0, 0.18), 30.0, "center"),
        ("side_d30", (1.0, 0.08, 0.18), 30.0, "center"),
        ("back_d30", (-0.6, 1.0, 0.18), 30.0, "center"),
        ("top_d30", (0.03, -0.06, 1.0), 30.0, "roof"),
        ("head_d8", (0.8, -1.0, 0.25), 8.0, "roof"),
        ("base_d6", (0.8, -1.0, 0.20), 6.0, "low"),
        ("play_d55", (0.8, -0.9, 0.60), 55.0, "center"),
        ("far_d150", (0.8, -0.9, 0.45), 150.0, "center"),
    ),
    "cabin": (
        ("q_d6", (1.0, -1.1, 0.30), 6.0, "body"),
        ("side_d6", (1.0, 0.0, 0.08), 6.0, "body"),
        ("door_d6", (-1.0, 0.0, 0.08), 6.0, "body"),
        ("end_d6", (0.0, -1.0, 0.08), 6.0, "body"),
        ("top_d6", (0.25, -0.3, 1.0), 6.0, "body"),
        ("body_d3", (1.0, -0.8, 0.15), 3.0, "body"),
        ("q_d30", (1.0, -1.1, 0.30), 30.0, "body"),
        ("above_d30", (0.8, -0.9, 0.70), 30.0, "body"),
    ),
}


def _args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    import argparse
    ap = argparse.ArgumentParser(prog="gd_review.py")
    ap.add_argument("mode", choices=("render", "diff", "sheet"))
    ap.add_argument("--out", required=True)
    ap.add_argument("--tag", default="subject")
    ap.add_argument("--kind", default="station", choices=tuple(SHOTS))
    ap.add_argument("--asset", action="append", default=[])
    ap.add_argument("--tint", default=None)
    ap.add_argument("--tint-space", default="srgb", choices=("srgb", "linear"))
    ap.add_argument("--vanilla", default=None)
    ap.add_argument("--bench", default=None)
    ap.add_argument("--vanilla-scripts", default=T.VANILLA_SCRIPTS)
    ap.add_argument("--cams", default=None)
    ap.add_argument("--shots", default=None)
    ap.add_argument("--samples", type=int, default=T.SAMPLES)
    ap.add_argument("--ground-z", type=float, default=0.0)
    ap.add_argument("--lod", type=int, default=0)
    ap.add_argument("--a", default=None)
    ap.add_argument("--b", default=None)
    ap.add_argument("--cols", default=None)
    ap.add_argument("--scale", type=int, default=3)
    return ap.parse_args(a)


def parse_tint(text, space):
    """"R=r,g,b;G=r,g,b;B=r,g,b" -> {"R": (r, g, b), ...} in LINEAR values for the preview material."""
    if not text:
        return None
    out = {}
    for item in text.split(";"):
        k, v = item.split("=")
        rgb = np.array([float(x) for x in v.split(",")], np.float32)
        if space == "srgb":
            rgb = P._srgb_to_linear(rgb)
        out[k.strip().upper()] = tuple(float(x) for x in rgb)
    return out


def _load(adir, name, tag, lod):
    """LOD0 (tex_review.load_ours) or the LOD<n> FBX of the same asset (its `_LOD<n>_Win` too)."""
    if not lod:
        return T.load_ours(adir, name, tag)
    o = T._import(os.path.join(adir, f"{name}_LOD{lod}.fbx"))
    M = T._frame_fix(o)
    T._apply(o, M)
    o.name = tag
    for lay in list(o.data.uv_layers)[1:]:
        o.data.uv_layers.remove(lay)
    subs = []
    p = os.path.join(adir, f"{name}_LOD{lod}_Win.fbx")
    if os.path.exists(p):
        s = T._import(p)
        T._apply(s, M)
        s.data.materials.clear()
        s.data.materials.append(T._glass_material(f"{tag}_Win", True))
        s.name = f"{tag}_Win"
        subs.append(s)
    return o, subs


def _aim(o, kind, direction, gnd):
    if kind == "body":      # the first asset's bounds center (a hanging carrier has no facade)
        import lift_tower
        c, _size = lift_tower.world_bounds(o)
        return mathutils.Vector(c)
    if kind.startswith("bbox:"):
        import lift_tower
        c, size = lift_tower.world_bounds(o)
        dx, dy = float(direction[0]), float(direction[1])
        s = min((size.x / 2.0) / abs(dx) if abs(dx) > 1e-9 else 1e9, (size.y / 2.0) / abs(dy) if abs(dy) > 1e-9 else 1e9)
        return mathutils.Vector((c.x + dx * s, c.y + dy * s, c.z - size.z / 2.0 + float(kind[5:]) * size.z))
    return T.aim_point(o, kind, direction, gnd)


def resolve_cams(o, gnd, shots):
    cams = {}
    for name, direction, dist, kind in shots:
        t = _aim(o, kind, direction, gnd)
        d = mathutils.Vector(direction).normalized()
        loc = t + d * dist
        rot = (t - loc).to_track_quat("-Z", "Y").to_euler()
        cams[name] = {"loc": list(loc), "rot": list(rot), "dist": dist, "aim": kind, "target": list(t)}
    return cams


def render(a):
    out = os.path.join(a.out, a.tag)
    os.makedirs(out, exist_ok=True)
    T.clear()
    tint = parse_tint(a.tint, a.tint_space)
    drawn = []          # (object, maps dir, maps name, tint)
    extra = []
    if a.asset:
        for i, spec in enumerate(a.asset):
            parts = spec.split("|")
            adir, name = parts[0], parts[1]
            o, subs = _load(adir, name, f"{a.tag}_{i}", a.lod)
            if len(parts) >= 3 and parts[2]:
                off = mathutils.Vector([float(x) for x in parts[2].split(",")])
                for ob in [o] + subs:
                    ob.location = off
            drawn.append((o, adir, name, tint))
            extra += subs
    elif a.vanilla:
        o, mdir, mname, vtint = T.load_vanilla(a.vanilla, a.bench, a.vanilla_scripts)
        drawn.append((o, mdir, mname, vtint))
    else:
        raise SystemExit("render needs --asset or --vanilla")
    bpy.context.view_layer.update()
    main = drawn[0][0]
    rig = P._Rig(main, a.samples)
    cam = P._make_camera(rig)
    P._make_world(rig, **P.NOON)
    gnd = T.ground(rig, None, "flat")
    gnd.location.z = a.ground_z
    bpy.context.view_layer.update()
    sc = rig.scene
    sc.render.resolution_x, sc.render.resolution_y = T.RES
    for o, mdir, mname, tn in drawn:
        P._assign(o, P._pbr_material(rig, mdir, mname, tn, mat_name=f"{o.name}_PBR"))
        o.hide_render = False
    for s in extra:
        s.hide_render = False
    gnd.hide_render = False
    all_shots = SHOTS[a.kind]
    shots = all_shots if not a.shots else tuple(s for s in all_shots if s[0] in a.shots.split(","))
    cams_path = a.cams or os.path.join(a.out, "cams.json")
    key = ("vanilla:" + a.vanilla) if a.vanilla else "ours"
    store = json.load(open(cams_path)) if os.path.exists(cams_path) else {}
    cams = store.get(key) or {}
    missing = [s for s in shots if s[0] not in cams]
    if missing:
        cams.update(resolve_cams(main, gnd, missing))
        store[key] = cams
        json.dump(store, open(cams_path, "w"), indent=1)
        print(f"[gd_review] cameras resolved for {[s[0] for s in missing]} -> {cams_path}")
    written = {}
    for name, *_rest in shots:
        cdef = cams[name]
        cam.location = mathutils.Vector(cdef["loc"])
        cam.rotation_euler = mathutils.Euler(cdef["rot"])
        path = os.path.join(out, f"{name}.png")
        P._render(rig, path, engine="EEVEE")
        written[name] = os.path.basename(path)
        print(f"[gd_review] {a.tag} {name} -> {path}")
    json.dump({"tag": a.tag, "kind": a.kind, "assets": a.asset, "vanilla": a.vanilla, "tint": a.tint,
               "tint_space": a.tint_space, "lod": a.lod, "cams": cams_path, "samples": a.samples,
               "renders": written}, open(os.path.join(out, "renders.json"), "w"), indent=1)


if __name__ == "__main__":
    A = _args()
    if A.mode == "render":
        render(A)
    else:
        T.SHOTS = SHOTS[A.kind]     # tex_review.sheet orders its rows by SHOTS
        {"diff": T.diff, "sheet": T.sheet}[A.mode](A)
