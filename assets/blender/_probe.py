import bpy, sys, addon_utils

print("=== PROBE START ===")
print("blender:", bpy.app.version_string, bpy.app.version)
print("python :", sys.version.split()[0])

# Which export operators exist?
exporters = [n for n in dir(bpy.ops.export_scene)] if hasattr(bpy.ops, "export_scene") else []
print("export_scene ops:", exporters)

wm = [n for n in dir(bpy.ops.wm) if "fbx" in n.lower() or "obj" in n.lower() or "gltf" in n.lower()]
print("wm io ops:", wm)

# Addon state for io_scene_fbx
for mod in addon_utils.modules():
    name = mod.__name__
    if "fbx" in name.lower():
        print("fbx addon module:", name, "enabled:", addon_utils.check(name))

# Operators we intend to use
checks = {
    "mesh.primitive_cylinder_add": hasattr(bpy.ops.mesh, "primitive_cylinder_add"),
    "mesh.primitive_cube_add":     hasattr(bpy.ops.mesh, "primitive_cube_add"),
    "mesh.primitive_torus_add":    hasattr(bpy.ops.mesh, "primitive_torus_add"),
    "uv.smart_project":            hasattr(bpy.ops.uv, "smart_project"),
    "object.modifier_add":         hasattr(bpy.ops.object, "modifier_add"),
    "object.join":                 hasattr(bpy.ops.object, "join"),
    "object.shade_smooth":         hasattr(bpy.ops.object, "shade_smooth"),
}
for k, v in checks.items():
    print(f"op {k}: {v}")

# FBX export signature
if hasattr(bpy.ops.export_scene, "fbx"):
    rna = bpy.ops.export_scene.fbx.get_rna_type()
    props = sorted(p.identifier for p in rna.properties if p.identifier != "rna_type")
    print("fbx props:", props)
else:
    print("!! export_scene.fbx MISSING")

# Render engines available
print("engines:", [e.bl_idname for e in bpy.types.RenderEngine.__subclasses__()])
print("=== PROBE END ===")
