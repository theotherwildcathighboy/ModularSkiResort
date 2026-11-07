"""
T121 preview gate (2026-10-20): before / after / diff renders of the stations that
station_cable_strip.py stripped.

The script loads the shipped FBX (before) and the stripped FBX (after) in the game frame (cm, Y up).
A parent maps them to Blender (m, Z up). The station's hosted terminal tower stays at its prefab row
(context only, untouched). "diff" is the before mesh with every triangle that the after mesh lacks
painted red. The render uses Workbench and the station's own BaseColor.

    blender -b --factory-startup --python-exit-code 1 --python assets\\blender\\station_cable_preview.py --
        --stripped <strip out dir> --out <preview dir> [--names ...]
"""
import argparse
import math
import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
ST = os.path.join(REPO, "assets", "import", "stations")
TW = os.path.join(REPO, "assets", "import", "towers", "P")
SHIPPED = {"SkiDQBaseStationB": ("detachable/B", "SkiLiftTowerETDQP", -11.0),
           "SkiDQTopStationB": ("detachable/B", None, None),
           "SkiGondolaBaseStationB": ("gondola/B", "SkiLiftTowerETGDP", -11.0),
           "SkiGondolaTopStationB": ("gondola/B", None, None),
           "SkiLiftBaseStationN": ("fixedgrip/N", "SkiLiftTowerET68P", -6.9),
           "SkiLiftTopStationN": ("fixedgrip/N", None, None)}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def load(path, parent_loc=(0, 0, 0), yaw=0.0):
    bpy.ops.import_scene.fbx(filepath=path)
    o = [x for x in bpy.context.selected_objects if x.type == "MESH"][0]
    root = bpy.data.objects.new(o.name + "_frame", None)
    bpy.context.scene.collection.objects.link(root)
    root.rotation_euler = (math.radians(90.0), 0.0, yaw)
    root.scale = (0.01, 0.01, 0.01)
    root.location = parent_loc
    o.parent = root
    return o


def tex_mat(name, png):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    img = nt.nodes.new("ShaderNodeTexImage")
    img.image = bpy.data.images.load(png)
    nt.links.new(img.outputs["Color"], bsdf.inputs["Base Color"])
    nt.nodes.active = img
    return m


def red_mat():
    m = bpy.data.materials.new("removed")
    m.diffuse_color = (1.0, 0.0, 0.0, 1.0)
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (1, 0, 0, 1)
    return m


def tri_keys(o):
    me = o.data
    me.calc_loop_triangles()
    keys = {}
    for lt in me.loop_triangles:
        k = tuple(tuple(round(c, 2) for c in me.vertices[v].co) for v in lt.vertices)
        keys.setdefault(k, []).append(lt.polygon_index)
    return keys


def paint_removed(before, after_path):
    a = load(after_path)
    ak = tri_keys(a)
    bpy.data.objects.remove(a.parent, do_unlink=True)
    bpy.data.objects.remove(a, do_unlink=True)
    red = red_mat()
    before.data.materials.append(red)
    idx = len(before.data.materials) - 1
    n = 0
    for k, polys in tri_keys(before).items():
        keep = len(ak.get(k, []))
        for p in polys[keep:]:
            before.data.polygons[p].material_index = idx
            n += 1
    return n


def camera(loc, target, lens=35):
    cam = bpy.data.cameras.new("cam")
    cam.lens = lens
    co = bpy.data.objects.new("cam", cam)
    bpy.context.scene.collection.objects.link(co)
    co.location = loc
    co.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = co
    return co


def render(path, views, bb_lo, bb_hi, rope_h):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "TEXTURE"
    sc.display.shading.show_shadows = True
    sc.display.shading.show_cavity = True
    sc.render.resolution_x, sc.render.resolution_y = 1400, 900
    sc.render.film_transparent = False
    sc.world = bpy.data.worlds.new("w")
    sc.world.color = (0.62, 0.68, 0.74)
    ground = bpy.data.meshes.new("ground")
    ground.from_pydata([(-80, -80, -0.02), (80, -80, -0.02), (80, 80, -0.02), (-80, 80, -0.02)], [], [(0, 1, 2, 3)])
    g = bpy.data.objects.new("ground", ground)
    gm = bpy.data.materials.new("g")
    gm.diffuse_color = (0.45, 0.5, 0.42, 1)
    ground.materials.append(gm)
    sc.collection.objects.link(g)
    c = [(a + b) / 2 for a, b in zip(bb_lo, bb_hi)]
    size = max(b - a for a, b in zip(bb_lo, bb_hi))
    nose_y = bb_hi[1]
    shots = {
        "three_quarter": ((c[0] + 0.95 * size, c[1] + 0.95 * size, c[2] + 0.65 * size), c, 35),
        "nose": ((c[0] - 10.0, nose_y + 13.0, rope_h + 5.0), (0.0, nose_y - 4.0, rope_h), 30),
        "side_low": ((c[0] - 1.25 * size, c[1] + 0.2 * size, 2.0), (c[0], c[1], rope_h), 32),
    }
    out = []
    for v in views:
        loc, tgt, lens = shots[v]
        cam = camera(loc, tgt, lens)
        sc.render.filepath = path.format(view=v)
        bpy.ops.render.render(write_still=True)
        out.append(sc.render.filepath)
        bpy.data.objects.remove(cam, do_unlink=True)
    return out


def world_bbox(o):
    pts = [o.matrix_world @ Vector(c) for c in o.bound_box]
    return [min(p[i] for p in pts) for i in range(3)], [max(p[i] for p in pts) for i in range(3)]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--stripped", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--names", nargs="*", default=None)
    args = ap.parse_args(sys.argv[sys.argv.index("--") + 1:])
    args.out = os.path.abspath(args.out)          # A relative render path resolves against Blender's own directory.
    os.makedirs(args.out, exist_ok=True)
    views = ("three_quarter", "nose", "side_low")
    for name in (args.names or list(SHIPPED)):
        fam, tower, tz = SHIPPED[name]
        d = os.path.join(ST, fam, name)
        png = os.path.join(d, name + "_BaseColor.png")
        for state in ("before", "after", "diff"):
            reset()
            src = os.path.join(d if state != "after" else os.path.join(args.stripped, name), name + ".fbx")
            st = load(src)
            st.data.materials.clear()
            st.data.materials.append(tex_mat("st", png))
            for sub in ("_Win",):
                wp = os.path.join(d, name + sub + ".fbx")
                if os.path.exists(wp):
                    w = load(wp)
                    w.data.materials.append(tex_mat("win", png))
            if tower:
                # Hosted row: game (0, 0, tz), turned 180 deg about game Y, is Blender (0, -tz, 0)
                # with yaw 180.
                t = load(os.path.join(TW, tower, tower + ".fbx"), parent_loc=(0.0, -tz, 0.0), yaw=math.radians(180.0))
                t.data.materials.append(tex_mat("tw", os.path.join(TW, tower, tower + "_BaseColor.png")))
            n = paint_removed(st, os.path.join(args.stripped, name, name + ".fbx")) if state == "diff" else 0
            bpy.context.view_layer.update()
            lo, hi = world_bbox(st)
            rope_h = {"SkiDQ": 3.7, "SkiGo": 4.85, "SkiLi": 5.4}[name[:5]]
            files = render(os.path.join(args.out, f"{name}_{state}_{{view}}.png"), views, lo, hi, rope_h)
            print(f"[preview] {name} {state}: {len(files)} renders" + (f", {n} faces red" if state == "diff" else ""))


main()
