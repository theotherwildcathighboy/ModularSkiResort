"""
T121 (2026-10-20, "extra cables are MODELED into the DQ stations and the gondola base and top stations
... the chairlift attachment already draws every rope"): the script strips ONLY the modeled rope
triangles out of the SHIPPED station FBX files. It touches nothing else.

The script does not re-run the generator. It imports the shipped FBX
(assets/import/stations/<family>/<letter>/<Name>/). It deletes the triangles that belong to a named rope
part. It writes the rest back with export_fbx's exact settings. A rope triangle has all three corners on
the corners of ONE face of a generator rope part (tolerance 0.5 cm). The part names come from the
generator's own part table (station_rope_parts.py output). Every part must match its full triangle count.

    blender -b --factory-startup --python-exit-code 1 --python assets\\blender\\station_cable_strip.py --
        --parts <rope_parts.json> --out <dir> [--names N1 N2 ...] [--write] [--preview DIR]

Without --write it only reports. --write saves <out>/<Name>/<file>.fbx (stripped) next to a control round
trip and the before / after diff (<out>/<Name>/diff.json). The preview gate holds: nothing lands in
assets/import from here.
"""
import argparse
import hashlib
import json
import math
import os
import sys

import bpy
import bmesh
import numpy as np
from mathutils import kdtree, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
ST = os.path.join(REPO, "assets", "import", "stations")
SHIPPED = {"SkiDQBaseStationB": "detachable/B", "SkiDQTopStationB": "detachable/B",
           "SkiGondolaBaseStationB": "gondola/B", "SkiGondolaTopStationB": "gondola/B",
           "SkiLiftBaseStationN": "fixedgrip/N", "SkiLiftTopStationN": "fixedgrip/N",
           "SkiTramBaseStationB": "tram/B", "SkiTramTopStationB": "tram/B"}
TOL = 0.5          # cm


def perms48():
    import itertools
    for p in itertools.permutations(range(3)):
        for s in itertools.product((1, -1), repeat=3):
            m = np.zeros((3, 3))
            for i in range(3):
                m[i, p[i]] = s[i]
            yield m


def import_fbx(path):
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes):
        bpy.data.meshes.remove(m)
    bpy.ops.import_scene.fbx(filepath=path)
    obs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if len(obs) != 1:
        raise RuntimeError(f"{path}: {len(obs)} mesh objects")
    o = obs[0]
    if np.abs(np.array(o.matrix_world) - np.eye(4)).max() > 1e-9:
        # The export wrote raw vertices with an identity node. The importer must hand them back
        # untouched.
        raise RuntimeError(f"{path}: imported node transform is not identity: {o.matrix_world}")
    return o


def tri_records(o, only=None):
    """Per triangle, the key is the sha1 of its corner positions (0.001 cm) + every UV layer (1e-6), in loop
    order. The value is the corner normals (full float). The comparison uses a tolerance for normals
    (corner_normals_max_delta). A pure Blender FBX import -> export round trip re-quantizes custom
    normals by up to ~1e-4."""
    me = o.data
    me.calc_loop_triangles()
    uvl = [l.data for l in me.uv_layers]
    nrm = me.corner_normals
    rec = {}
    for lt in (me.loop_triangles if only is None else [me.loop_triangles[i] for i in only]):
        key = repr([(tuple(round(x, 3) for x in me.vertices[vi].co),
                     tuple(tuple(round(x, 6) for x in L[li].uv) for L in uvl))
                    for li, vi in zip(lt.loops, lt.vertices)])
        k = hashlib.sha1(key.encode()).hexdigest()
        rec.setdefault(k, []).append(tuple(c for li in lt.loops for c in nrm[li].vector))
    return rec


def compare(a, b):
    """-> (identical multiset of triangles by position + UV, max corner-normal delta over the matched triangles)"""
    same = set(a) == set(b) and all(len(a[k]) == len(b[k]) for k in a)
    dmax = 0.0
    for k in set(a) & set(b):
        for x, y in zip(sorted(a[k]), sorted(b[k])):
            dmax = max(dmax, max(abs(p - q) for p, q in zip(x, y)))
    only_a = sum(max(0, len(v) - len(b.get(k, []))) for k, v in a.items())
    only_b = sum(max(0, len(v) - len(a.get(k, []))) for k, v in b.items())
    return same, round(dmax, 6), only_a, only_b


def fbx_names(path, stem):
    import re
    data = open(path, "rb").read()
    return sorted(set(m.decode() for m in re.findall(re.escape(stem.encode()) + rb"[A-Za-z0-9_.]*", data)))


def stats(o, path):
    """The rule names these values: object / mesh names (as imported and as written in the file), vertex and
    triangle counts, bounds, material slots, UV layers."""
    me = o.data
    me.calc_loop_triangles()
    co = np.array([v.co[:] for v in me.vertices]) if me.vertices else np.zeros((0, 3))
    return {"object": o.name, "mesh": me.name, "names_in_file": fbx_names(path, o.name.split("_")[0]),
            "verts": len(me.vertices), "tris": len(me.loop_triangles),
            "bbox_min": [round(float(x), 3) for x in co.min(0)] if len(co) else None,
            "bbox_max": [round(float(x), 3) for x in co.max(0)] if len(co) else None,
            "material_slots": [s.material.name if s.material else None for s in o.material_slots],
            "uv_layers": [l.name for l in me.uv_layers],
            "color_attributes": [a.name for a in me.color_attributes]}


def find_map(rope_pts, fbx_co, gen_lo, gen_hi, fbx_lo, fbx_hi):
    kd = kdtree.KDTree(len(fbx_co))
    for i, c in enumerate(fbx_co):
        kd.insert(c, i)
    kd.balance()
    best = None
    sample = rope_pts[:: max(1, len(rope_pts) // 400)]
    for m in perms48():
        a = (m @ np.array(gen_lo)) * 100.0
        b = (m @ np.array(gen_hi)) * 100.0
        t = np.array(fbx_lo) - np.minimum(a, b)
        hit = sum(1 for p in sample if kd.find((m @ p) * 100.0 + t)[2] < TOL)
        if best is None or hit > best[0]:
            best = (hit, m, t)
    return best[0] / len(sample), best[1], best[2]


def rope_tris(o, parts, m, t):
    """-> {part: [tri index, ...]}, {part: expected tri count}"""
    pts, owner = [], []
    for pname, pd in parts.items():
        for fi, face in enumerate(pd["verts"]):
            for c in face:
                pts.append((m @ np.array(c)) * 100.0 + t)
                owner.append((pname, fi))
    kd = kdtree.KDTree(len(pts))
    for i, p in enumerate(pts):
        kd.insert(p, i)
    kd.balance()
    me = o.data
    me.calc_loop_triangles()
    vfaces = []
    for v in me.vertices:
        hits = kd.find_range(v.co, TOL)
        vfaces.append({owner[i] for _c, i, _d in hits})
    found = {}
    for lt in me.loop_triangles:
        common = vfaces[lt.vertices[0]] & vfaces[lt.vertices[1]] & vfaces[lt.vertices[2]]
        if common:
            pname = sorted(common)[0][0]
            found.setdefault(pname, []).append(lt.index)
    expect = {p: sum(len(f) - 2 for f in pd["verts"]) for p, pd in parts.items()}
    return found, expect


def delete_tris(o, tri_idx):
    me = o.data
    me.calc_loop_triangles()
    polys = {me.loop_triangles[i].polygon_index for i in tri_idx}
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    doomed = [bm.faces[i] for i in sorted(polys)]
    bmesh.ops.delete(bm, geom=doomed, context="FACES_ONLY")
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(me)
    bm.free()
    me.update()
    return len(polys)


def export_like_shipped(o, path, geom_name):
    """This function makes the exporter call of cs2_asset.export_fbx, minus the frame bake. The imported
    vertices are already baked.

    geom_name is the geometry name that the shipped file carries ("<name>.001", once "<name>.002"). The
    exporter writes a mesh whose name equals its object's as "<name>.001", and it writes "<name>.001" as
    "<name>.002". So the code names the mesh one step back (checked by names_in_file before == after)."""
    stem, _dot, k = geom_name.rpartition(".")
    want = o.name if (stem == o.name and k == "001") else f"{stem}.{int(k) - 1:03d}"
    for other in bpy.data.meshes:
        if other != o.data and other.name == want:
            other.name = want + "_stale"
    o.data.name = want
    if o.data.name != want:
        raise RuntimeError(f"mesh name collision: {o.data.name} vs {want}")
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, apply_unit_scale=False,
                             global_scale=1.0, axis_forward="Y", axis_up="Z", use_triangles=True,
                             mesh_smooth_type="EDGE", use_tspace=True, bake_anim=False, path_mode="STRIP")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--parts", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--names", nargs="*", default=None)
    ap.add_argument("--write", action="store_true")
    args = ap.parse_args(sys.argv[sys.argv.index("--") + 1:])
    allparts = json.load(open(args.parts))
    report = {}
    for name in (args.names or list(SHIPPED)):
        d = os.path.join(ST, SHIPPED[name], name)
        gp = allparts[name]
        rope_pts = np.array([c for pd in gp["parts"].values() for f in pd["verts"] for c in f])
        rep = {"generator_rope_parts": {k: v["faces"] for k, v in gp["parts"].items()}, "files": {}}
        frame = None
        for fn in sorted(os.listdir(d)):
            if not fn.endswith(".fbx"):
                continue
            src = os.path.join(d, fn)
            o = import_fbx(src)
            co = np.array([v.co[:] for v in o.data.vertices])
            if frame is None:
                if fn != name + ".fbx":
                    raise RuntimeError("LOD0 must come first")
                frac, m, t = find_map(rope_pts, co, *gp["bbox"], co.min(0), co.max(0))
                frame = (m, t)
                rep["frame"] = {"match_fraction": round(frac, 4), "axes": m.tolist(), "t_cm": t.round(3).tolist()}
            m, t = frame
            found, expect = rope_tris(o, gp["parts"], m, t)
            before, rec_b = stats(o, src), tri_records(o)
            fr = {"before": before, "rope_tris_found": {k: len(v) for k, v in sorted(found.items())},
                  "rope_tris_expected_lod0": expect}
            if args.write:
                od = os.path.join(args.out, name)
                os.makedirs(od, exist_ok=True)
                # control: the same import -> export -> import with nothing deleted
                ctl = os.path.join(od, "_control_" + fn)
                export_like_shipped(o, ctl, before["mesh"])
                o2 = import_fbx(ctl)
                cs = stats(o2, ctl)
                same, dn, oa, ob = compare(rec_b, tri_records(o2))
                fr["control"] = {"tris_identical": same, "normal_max_delta": dn, "only_before": oa, "only_after": ob,
                                 "fields_differ": {k: [before[k], cs[k]] for k in before if before[k] != cs[k]}}
                os.remove(ctl)
                o = import_fbx(src)
                found, _e = rope_tris(o, gp["parts"], m, t)
                removed_ids = [i for v in found.values() for i in v]
                rem = tri_records(o, removed_ids)
                fr["polys_deleted"] = delete_tris(o, removed_ids) if removed_ids else 0
                outp = os.path.join(od, fn)
                export_like_shipped(o, outp, before["mesh"])
                o3 = import_fbx(outp)
                after = stats(o3, outp)
                want = {k: list(v) for k, v in rec_b.items()}
                for k, v in rem.items():
                    for n in v:
                        want[k].remove(n)
                    if not want[k]:
                        del want[k]
                same, dn, oa, ob = compare(want, tri_records(o3))
                fr["after"] = after
                fr["after_vs_before_minus_ropes"] = {"tris_identical": same, "normal_max_delta": dn,
                                                     "only_before": oa, "only_after": ob}
                fr["fields_differ"] = {k: [before[k], after[k]] for k in before if before[k] != after[k]}
                fr["tris_removed"] = before["tris"] - after["tris"]
                fr["verts_removed"] = before["verts"] - after["verts"]
            rep["files"][fn] = fr
            print(f"[strip] {name} {fn}: found {fr['rope_tris_found']} " +
                  (f"control {fr['control']['tris_identical']} / {fr['control']['normal_max_delta']} "
                   f"after {fr['after_vs_before_minus_ropes']} -{fr['tris_removed']} tris -{fr['verts_removed']} verts "
                   f"fields {fr['fields_differ']}" if args.write else ""))
        report[name] = rep
    os.makedirs(args.out, exist_ok=True)
    with open(os.path.join(args.out, "strip-report.json"), "w") as f:
        json.dump(report, f, indent=1)


main()
