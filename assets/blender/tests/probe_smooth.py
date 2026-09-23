import bpy, math, collections, os, sys
out_dir = os.path.dirname(os.path.abspath(__file__))
print("BLENDER", bpy.app.version_string)
print("has shade_smooth_by_angle:", hasattr(bpy.ops.object, "shade_smooth_by_angle"))
print("has shade_auto_smooth:", hasattr(bpy.ops.object, "shade_auto_smooth"))
bpy.ops.object.select_all(action="SELECT"); bpy.ops.object.delete(use_global=False)

def build(name, mode):
    bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.5, depth=4.0)
    o = bpy.context.active_object; o.name = name; o.data.name = name
    if mode == "by_angle":
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(60.0), keep_sharp_edges=True)
    elif mode == "auto_mod":
        bpy.ops.object.shade_auto_smooth(angle=math.radians(60.0))
    elif mode == "flat":
        pass
    m = o.data
    print(f"[{name}] modifiers={[md.type for md in o.modifiers]} sharp_edge_attr={'sharp_edge' in m.attributes} "
          f"smooth_faces={sum(p.use_smooth for p in m.polygons)}/{len(m.polygons)}")
    return o

def export(o, path):
    bpy.ops.object.select_all(action="DESELECT"); o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"},
        apply_unit_scale=False, global_scale=1.0, axis_forward="Y", axis_up="Z",
        use_triangles=True, mesh_smooth_type="EDGE", use_tspace=True, bake_anim=False,
        path_mode="STRIP", use_mesh_modifiers=True)

def inspect(path, tag):
    bpy.ops.object.select_all(action="SELECT"); bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=path)
    o = bpy.context.selected_objects[0]; m = o.data
    per_vert = collections.defaultdict(set)
    for lp in m.loops:
        per_vert[lp.vertex_index].add(tuple(round(c, 3) for c in lp.normal))
    multi = sum(1 for v in per_vert.values() if len(v) > 1)
    # side-face check: a side vertex on a smooth cylinder has exactly 2 normals (side + cap); flat has 3+
    hist = collections.Counter(len(v) for v in per_vert.values())
    print(f"[{tag}] reimport: verts={len(m.vertices)} loops={len(m.loops)} custom_normals={m.has_custom_normals} "
          f"normals-per-vertex histogram={dict(sorted(hist.items()))} smooth_faces={sum(p.use_smooth for p in m.polygons)}/{len(m.polygons)}")

for mode in ("flat", "by_angle", "auto_mod"):
    o = build("Probe" + mode, mode)
    p = os.path.join(out_dir, f"probe_{mode}.fbx")
    export(o, p)
    inspect(p, mode)
    bpy.ops.object.select_all(action="SELECT"); bpy.ops.object.delete(use_global=False)
