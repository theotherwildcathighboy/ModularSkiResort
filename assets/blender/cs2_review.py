"""
cs2_review.py - headless review packs for the live Blender review (ROUND-SPEC R10).

WHY
---
Generator code must never run in Richard's live Blender session: every generator's
clear_scene() deletes all objects and purges orphans (lift_chair.py:158-165,
lift_terminal.py:103-110), the finish pass leaks scene settings (lift_tower.py:929,
982-984), export_fbx renames sources (cs2_asset.py:1217-1218) and Cycles bakes block the
UI thread (pipeline-gap.md section 6). So generators build HEADLESS as always and, with
`--review`, also write a small library file: `assets/preview/review/<Name>.blend`.
`review_live.py` (sent verbatim through the MCP) links that file into the live scene.
Linked data is read-only by design, so the generator stays the single source.

Contract
--------
    write_pack(name, asset_dir, lod_objs, submeshes=None, *, meta=None, out_dir=None) -> path
    write_standin_pack(fbx_path=STANDIN_FBX, name=None, out_dir=None) -> path
        A SHIPPED asset as an LOD0-only pack (review_live.stand_ins links
        SkiLiftTowerFG8N.blend): cs2_preview.import_generation + the Y-up fix for a
        below-grade pier run here, headless, never in the live session (R10). CLI:
        blender --background --factory-startup --python cs2_review.py -- --stand-in [FBX]
    write_folder_pack(asset_dir, out_dir=None, name=None) -> path
        An EXPORTED asset folder (FBX LOD0/1/2 + subs + maps + manifest + settings.json) as a
        full pack, no generator run. CLI:
        blender --background --factory-startup --python cs2_review.py -- --from-folder DIR [DIR ...]
    texture_sets(name, asset_dir, manifest=None) -> {"tiled", "LOD0", "LOD1", "LOD2", "sheet", ...}

`lod_objs` = {0: LOD0 obj, 1: LOD1 obj, 2: LOD2 obj} (levels may be missing except 0),
`submeshes` = {suffix: obj} (e.g. {"Win": obj}); all are the generator's already-built,
finished objects in meters, Z-up, net-oriented. The pack holds DUPLICATES only:

    CS2R_<Name>                     collection, custom props below
      LOD0 / LOD1 / LOD2 / SUB      child collections (always all four; prop cs2r_role)
        CS2R_<Name>_LOD<k>          mesh copies (prop cs2r_role, tris)
        CS2R_<Name>_<suffix>        sub-mesh copies
        CS2R_<Name>_prop<NN>        SUB: one semi-transparent BOX per vanilla sub-object row (below)
        CS2R_<Name>_prop<NN>_label[k] SUB: its text label, 4 faces 90 degrees apart (cs2r_prop "label")
      SUB_NOPROPS                   only when the asset has rows: SUB's sub-meshes WITHOUT the
                                    stand-ins (review_live.props swaps the SUB instance to it)

Extra meshes (fine-detail pass, opt-in `extra_meshes=`; None = the pack above unchanged): OUR OWN
sub-object props that the pack shows as the real textured mesh instead of a box, e.g. a host's
sign set (cs2_signset; identity row, so the mesh is already in the host's pack frame):
{suffix: (obj, texture_name, texture_dir)} -> CS2R_<Name>_<suffix> in SUB (cs2r_role "SUB",
cs2r_extra = texture_name) with its own PBR material from <texture_dir>/<texture_name>_<Map>.png
(the sheet owner's maps); listed in the collection prop `extra_meshes`. SUB_NOPROPS keeps them
(they are the asset's look, not stand-ins).

Vanilla-prop stand-ins (DECISIONS 183: props are REFERENCED as sub-objects, never modeled):
write_pack reads the asset's manifest sub-object rows (cs2_subobj.collect_rows: the explicit
`manifest=` dict, else <asset_dir>/<name>.manifest.json; every block shape the generators write,
plus the beacon addendum rows of a tower) and adds one labeled box of the prop's union bounds per
row at its position / rotation, colored per family (lights, bins, seating, trees, fences, other).
Pack-only: nothing is exported, no manifest or PNG changes. Collection props: n_prop_standins,
prop_families (JSON), prop_bounds_unknown, subobj_source, subobj_replaced. A failure here is
printed and never stops the pack.

Material (one per pack, shared by every LOD - the importer maps LOD textures to LOD0,
cs2_asset.write_settings): BaseColor x mix(1, slotRGB, ControlMask R/G/B), multiplied in
linear space like cs2_preview.py:428-454 (the in-game picker tint multiplies). Each slot
color is an RGB node named "Slot1".."Slot3" (defaults SLOT_DEFAULTS) overridden per placed
instance by an INSTANCER Attribute node reading the instancing empty's custom property
"cs2r_slot<k>" (3 floats; missing -> Alpha 0 -> the node default; probed on 5.2.2, plain
3-float array only - a 4-float or COLOR-subtype property reads back gray). That is how
review_live.tint() recolors a LINKED, read-only material. The defaults are the PNG preview
rig's (cs2_preview._tint_colours, cs2_preview.py:421-425: cs2_asset.STEEL_DEFAULT_RGB /
PAD_RGB / white), because the live view is there to match those previews (pipeline-gap.md
section 6); the prefab's "compensated" steel 0.25/0.26/0.275 (PIPELINE.md:113) is an
in-game VariationSet value. Normal map (GL, tangent space),
MaskMap R -> Metallic, G -> Coat Weight, 1 - A -> Roughness (cs2_preview.py:489-502),
optional `_Emissive` -> emission 5.0 (cs2_preview.EMISSIVE_STRENGTH). `_Gls` subs get a
Glass BSDF; `_Win`-family subs dark glass plus a warm emission whose strength is
max(node "WinEmission", instancer "cs2r_win_emit") x WIN_EMIT_STRENGTH - a mock, the game's
parallax interior cannot be reproduced.

Images are COPIED from `asset_dir` to `<out_dir>/_tex/<Name>/` and loaded from there, then
written with path_remap="RELATIVE_ALL", so the pack references `//_tex/<Name>/<file>.png`:
valid wherever the review folder lives and independent of the build folder (T63, DECISIONS
270: packs built into the scratchpad lost every texture in the live scene). A rebuild +
Library.reload() refreshes them. Files in `_tex/<Name>/` that the new pack no longer uses are
deleted after a successful write.

TILED buildings (texture-parity rollout, T63-tiled; texture_sets()): a build whose folder holds
the far set `<Name>_LOD1_<Map>.png` AND whose LOD0 maps are the family sheet gets TWO texture
sets, exactly as the importer binds them (rollout doc section 7, settings.json sharedAssets):
    LOD0          the sheet: `<asset_dir>/<tiled.sheet.workspace>/<sheet>_<Map>.png` (manifest), else
                  `<asset_dir>/../_SkiTileSheet<L>/SkiTileSheet<L>_<Map>.png`, else the settings.json
                  cross-asset target / the owner's own `<Name>_<Map>.png`; Emissive only when the
                  import binds it (settings key for a sharer, the file for the owner). Material
                  CS2R_<Name>_PBR, staged under the sheet's own file names.
    LOD1 / LOD2   `<Name>_LOD1_<Map>.png` (LOD2: its settings target, i.e. the same files); material
                  CS2R_<Name>_PBR_LOD1, no emissive (DECISIONS 77). The far UVs address the far set.
`_Win` subs keep the window mock. A non-tiled asset is unchanged (one material, own maps).
Gate: a pack of an EXPORTED folder (settings.json or an FBX in `asset_dir`) whose LOD materials
end up with ZERO images raises (no pack is written), stand-ins excepted.

From an exported folder (no generator run; write_folder_pack, CLI --from-folder): the folder's
FBXs (LOD0 / LOD1 / LOD2 / `<Name>_<suffix>.fbx` subs, the manifest's sign set as the SignSet extra)
are imported headless, the export's (x, y, z) -> (x, z, -y) x 100 is undone on the mesh data (5.x
wm.fbx_import keeps raw vertices; checked against the manifest's extent_game_m), and write_pack
runs with the folder's manifest, so the pack is built from exactly what Richard imports.

Collection custom props (flat, UI-readable; everything again as JSON in "cs2r_meta_json"):
tris_LOD<k> / tris_<suffix>, cap_LOD<k> (meta["caps"]), lod_switch_m_LOD<k> / lod_cull_m /
lod_bias (cs2_preview.lod_switch_table WITH the LOD0 surface area: pipeline-gap-skeptic
verdict 13 - the thin-mesh term moves the chair from 38/69 m to ~50/89 m; still a formula
prediction until the SkiProbeLodA verdict), lod_switch_m_noarea_LOD<k> (the manifest's
variant, lift_terminal.py:1297-1300), density_px_per_m_median / _min / density_min_part /
texture_px (cs2_uv.density_report on the shipped UV layer), bbox_min / bbox_max /
surface_area_m2, generator / args / git_sha / letter / kind / submeshes / blender.

Side effects: NONE on the build session. Only the data API is used (no operator, no
selection change, nothing linked to a scene); every datablock created during the call
(duplicates, collections, materials, images) is removed again in a `finally`, found by an
ID-set snapshot diff; the session is never saved (libraries.write writes only the listed
collection and what it references). Gate: tests/test_review_side_effects.py.

Derived from: pipeline-gap.md section 6 (pack layout, material, props), ROUND-SPEC.md R10,
cs2_preview.py:421-425 (slot defaults), cs2_preview.py:114-148 (lod_switch_table),
cs2_uv.py:435 (density_report), cs2_submesh.py:47-48 (suffix families), cs2_preview.py:
736-768 (import_generation; its "min y ~ 0" Y-up test misreads a below-grade pier), P0a
review fixes (p0a-integration.md "Fixes applied": slot defaults, stand-in pack). No
top-level side effects beyond the sibling-import path (as cs2_preview.py:59-61); the
`__main__` block runs only as a Blender --python script.
"""

import contextlib
import io
import json
import math
import os
import shutil
import struct
import subprocess
import sys
import time

import bpy
import mathutils

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)
import cs2_asset    # noqa: E402
import cs2_preview  # noqa: E402  (lod_switch_table, EMISSIVE_STRENGTH)
import cs2_uv       # noqa: E402  (density_report, face_world_areas)


# --------------------------------------------------------------------------------------
# Constants
# --------------------------------------------------------------------------------------

REPO_ROOT = os.path.normpath(os.path.join(_HERE, "..", ".."))
#: default pack folder; gitignored through `assets/preview/` (.gitignore:5)
DEFAULT_OUT_DIR = os.path.join(REPO_ROOT, "assets", "preview", "review")
#: the shipped FG8N tower: review_live.stand_ins' scale stand-in (pipeline-gap.md section 6)
STANDIN_FBX = os.path.join(REPO_ROOT, "assets", "import", "towers", "N", "SkiLiftTowerFG8N",
                           "SkiLiftTowerFG8N.fbx")
PACK_VERSION = 1
PACK_PREFIX = "CS2R_"
ROLES = ("LOD0", "LOD1", "LOD2", "SUB")

#: recolor slot defaults (linear multipliers): the PNG preview rig's, so the live view
#: matches the previews (cs2_preview.py:421-425; cs2_asset.STEEL_DEFAULT_RGB is the single
#: source of truth, cs2_asset.py:445). Slot 3 is reserved and defaults to white.
SLOT_DEFAULTS = {1: tuple(cs2_asset.STEEL_DEFAULT_RGB), 2: tuple(cs2_asset.PAD_RGB),
                 3: (1.0, 1.0, 1.0)}
SLOT_LABELS = {1: "Slot 1 (ControlMask R) steel", 2: "Slot 2 (ControlMask G) pad",
               3: "Slot 3 (ControlMask B) reserved"}
#: instancer custom-property names read by the material (review_live.py uses the same)
SLOT_ATTR = "cs2r_slot{}"
WIN_EMIT_ATTR = "cs2r_win_emit"

#: sub-mesh families (cs2_submesh.py:47-48)
WIN_FAMILY = ("Win", "Wim", "Wif", "Wio")
GLASS_FAMILY = ("Gls", "Glf", "Glm")

GLASS_RGB = (0.92, 0.95, 0.97)
GLASS_IOR = 1.45
GLASS_ROUGHNESS = 0.02
WIN_GLASS_RGB = (0.03, 0.035, 0.04)
WIN_GLASS_ROUGHNESS = 0.08
WIN_WARM_RGB = (1.0, 0.70, 0.42)     # ~3000 K interior light (mock)
WIN_EMIT_STRENGTH = 3.0

LOD_FACTOR = 652.0                   # cs2_preview.lod_switch_table default (unverified)

#: texture maps (the CS2 import naming, cs2_asset.write_settings); the far set has no emissive
MAPS = ("BaseColor", "ControlMask", "MaskMap", "Normal", "Emissive")
FAR_MAPS = ("BaseColor", "ControlMask", "MaskMap", "Normal")
#: the family sheet workspace beside a tiled building (lodge.py: <out root>/_SkiTileSheet<L>)
SHEET_DIR_PREFIX = "_SkiTileSheet"
#: export_fbx bakes (x, y, z) -> (x, z, -y) x CS2_UNIT_SCALE into the vertices; this undoes it
FBX_TO_BLENDER = (mathutils.Matrix(((1.0, 0.0, 0.0, 0.0), (0.0, 0.0, -1.0, 0.0),
                                    (0.0, 1.0, 0.0, 0.0), (0.0, 0.0, 0.0, 1.0)))
                  @ mathutils.Matrix.Scale(1.0 / cs2_asset.CS2_UNIT_SCALE, 4))
#: manifest extent_game_m vs the re-imported LOD0 (m): an axis or scale slip is meters off
EXTENT_TOL_M = 0.05

#: ID collections snapshotted before and diffed after write_pack (anything new is removed)
_ID_COLLECTIONS = ("objects", "meshes", "materials", "images", "collections", "node_groups",
                   "textures", "cameras", "lights", "worlds", "actions", "curves", "libraries",
                   "fonts")        # fonts: the built-in one a first text label creates


# --------------------------------------------------------------------------------------
# Small helpers
# --------------------------------------------------------------------------------------

def _snapshot():
    return {attr: set(getattr(bpy.data, attr)) for attr in _ID_COLLECTIONS}


def _purge_new(before):
    """Remove every ID created since `before` (one bpy.data.batch_remove call)."""
    new = []
    for attr in _ID_COLLECTIONS:
        new.extend(i for i in getattr(bpy.data, attr) if i not in before[attr])
    if new:
        bpy.data.batch_remove(new)
    return len(new)


def png_size(path):
    """(width, height) from the PNG IHDR - no image datablock is created."""
    with open(path, "rb") as fh:
        head = fh.read(24)
    if len(head) < 24 or head[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError(f"not a PNG: {path}")
    return struct.unpack(">II", head[16:24])


def git_sha(repo=REPO_ROOT):
    """Short HEAD sha (+ '-dirty' when tracked files changed), or 'unknown'."""
    try:
        sha = subprocess.run(["git", "-C", repo, "rev-parse", "--short", "HEAD"],
                             capture_output=True, text=True, timeout=10).stdout.strip()
        if not sha:
            return "unknown"
        dirty = subprocess.run(["git", "-C", repo, "status", "--porcelain",
                                "--untracked-files=no"],
                               capture_output=True, text=True, timeout=10).stdout.strip()
        return sha + ("-dirty" if dirty else "")
    except (OSError, subprocess.SubprocessError):
        return "unknown"


def letter_of(name, asset_dir):
    """Generation letter: the import-tree folder (`.../<category>/<letter>/<Name>`) when it
    is one or two capitals, else the trailing capital of the name (SkiLiftChair6L -> L)."""
    parent = os.path.basename(os.path.dirname(os.path.abspath(asset_dir)))
    if 1 <= len(parent) <= 2 and parent.isalpha() and parent.isupper():
        return parent
    return name[-1] if name[-1:].isupper() else ""


def _jsonable(v):
    if isinstance(v, dict):
        return {str(k): _jsonable(x) for k, x in sorted(v.items(), key=lambda kv: str(kv[0]))}
    if isinstance(v, (list, tuple)):
        return [_jsonable(x) for x in v]
    if isinstance(v, (str, int, float, bool)) or v is None:
        return v
    return str(v)


def _caps(meta):
    """meta["caps"] as {level: cap}; keys may be 0 / "0" / "LOD0" / "lod0"."""
    out = {}
    for k, v in (meta.get("caps") or {}).items():
        key = str(k).upper().replace("LOD", "")
        if key.isdigit() and v is not None:
            out[int(key)] = int(v)
    return out


def _world_bbox(objs):
    lo = [float("inf")] * 3
    hi = [float("-inf")] * 3
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ mathutils.Vector(c)
            for i in range(3):
                lo[i] = min(lo[i], w[i])
                hi[i] = max(hi[i], w[i])
    return lo, hi


def _shipped_uv(mesh):
    """The UV layer the textures map through: UVUnique after --finish, else the active."""
    if cs2_uv.UNIQUE_LAYER in mesh.uv_layers:
        return cs2_uv.UNIQUE_LAYER
    act = mesh.uv_layers.active
    return act.name if act is not None else None


# --------------------------------------------------------------------------------------
# Measurements (on the DUPLICATES - read-only either way)
# --------------------------------------------------------------------------------------

def density_stats(obj, A):
    """
    Shipped texel density per part via cs2_uv.density_report with the shipped layer as
    both source and target (the finish pass deletes "UVAtlas", lift_tower.py:1077), its
    per-part table swallowed. Returns {median, min, min_part, max, parts} in px/m or None.
    """
    layer = _shipped_uv(obj.data)
    if layer is None or A <= 0:
        return None
    with contextlib.redirect_stdout(io.StringIO()):
        rows = cs2_uv.density_report(obj, A, source_layer=layer, shipped_layer=layer)
    if not rows:
        return None
    try:
        names = json.loads(obj.data.get(cs2_asset.PART_TABLE_PROP, "[]"))
    except (TypeError, ValueError):
        names = []
    vals = sorted(r[2] for r in rows)
    mid = len(vals) // 2
    median = vals[mid] if len(vals) % 2 else 0.5 * (vals[mid - 1] + vals[mid])
    worst = min(rows, key=lambda r: (r[2], r[0]))
    p = worst[0]
    return {"median": round(median, 2), "min": round(worst[2], 2),
            "min_part": names[p - 1] if 1 <= p <= len(names) else f"part{p}",
            "max": round(vals[-1], 2), "parts": len(rows), "uv_layer": layer}


def switch_tables(size, tris_by_level, area):
    """cs2_preview.lod_switch_table with and without the thin-mesh area term."""
    with_area = cs2_preview.lod_switch_table(size, tris_by_level, lod_factor=LOD_FACTOR,
                                             area=area)
    no_area = cs2_preview.lod_switch_table(size, tris_by_level, lod_factor=LOD_FACTOR)
    return with_area, no_area


# --------------------------------------------------------------------------------------
# Materials
# --------------------------------------------------------------------------------------

def _fresh_tree(mat):
    if mat.node_tree is None:          # 5.x materials come with a tree; 4.x needs the flag
        mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    out.location = (600, 0)
    return nt, out


def _sock(sockets, name):
    """First ENABLED socket called `name` (ShaderNodeMix carries one A/B per data type)."""
    for s in sockets:
        if s.name == name and getattr(s, "enabled", True):
            return s
    return sockets[name]


def _mix(nt, factor, a, b, blend="MIX", loc=(0, 0)):
    """ShaderNodeMix in RGBA mode; `factor`/`a`/`b` are sockets or constants."""
    m = nt.nodes.new("ShaderNodeMix")
    m.data_type = "RGBA"
    m.blend_type = blend
    m.location = loc
    for key, val in (("Factor", factor), ("A", a), ("B", b)):
        sock = _sock(m.inputs, key)
        if isinstance(val, bpy.types.NodeSocket):
            nt.links.new(val, sock)
        elif key == "Factor":
            sock.default_value = float(val)
        else:
            sock.default_value = tuple(val) + (1.0,) * (4 - len(val))
    return _sock(m.outputs, "Result")


_STAGE_DIR = None   # set by write_pack: <out_dir>/_tex/<Name>; images are copied there first (T63)
_STAGED = {}        # basename -> source path, of what this write_pack call staged


def _load_image(path, img_name, colorspace, packed=False):
    """Stage `path` under _STAGE_DIR (T63) and load the staged copy as `img_name`."""
    if _STAGE_DIR:
        # T63 (DECISIONS 270): a pack must not reference its build folder (usually a temp dir
        # that moves or vanishes, and whose 8.3 short-name path Blender's relative resolver
        # fails on). Copy the PNG beside the packs and load THAT, so RELATIVE_ALL writes
        # //_tex/<Name>/<file>.png - valid wherever the review folder lives.
        base = os.path.basename(path)
        prev = _STAGED.get(base)
        if prev is not None and os.path.normcase(os.path.abspath(prev)) != \
                os.path.normcase(os.path.abspath(path)):
            raise ValueError(f"write_pack: two textures stage as {base}: {prev} and {path}")
        os.makedirs(_STAGE_DIR, exist_ok=True)
        staged = os.path.join(_STAGE_DIR, base)
        if prev is None:
            shutil.copyfile(path, staged)
            _STAGED[base] = path
        path = staged
    img = bpy.data.images.load(path, check_existing=False)
    img.name = img_name
    img.colorspace_settings.name = colorspace
    if packed:
        img.alpha_mode = "CHANNEL_PACKED"     # MaskMap A is smoothness, not coverage
    return img


def _image(name, asset_dir, suffix, colorspace, packed=False):
    path = os.path.join(asset_dir, f"{name}_{suffix}.png")
    if not os.path.isfile(path):
        return None
    return _load_image(path, f"{PACK_PREFIX}{name}_{suffix}", colorspace, packed)


# --------------------------------------------------------------------------------------
# Texture sets (what the importer binds per LOD; T63-tiled)
# --------------------------------------------------------------------------------------

def _read_json(path):
    try:
        with open(path, "r", encoding="utf-8") as fh:
            return json.load(fh)
    except (OSError, ValueError):
        return None


def _shared_assets(asset_dir):
    """settings.json sharedAssets of the folder ({} when absent)."""
    s = _read_json(os.path.join(asset_dir, "settings.json"))
    return dict((s or {}).get("sharedAssets") or {}) if isinstance(s, dict) else {}


def _tiled_block(name, asset_dir, manifest):
    """The manifest's `tiled` block: from the `manifest` argument when it carries one, else the
    folder's <name>.manifest.json (a generator hands write_pack only {"sub_objects": ...}, and
    writes the manifest after the pack; a previous build's manifest still names the sheet)."""
    if isinstance(manifest, str):
        manifest = _read_json(manifest)
    if isinstance(manifest, dict) and isinstance(manifest.get("tiled"), dict):
        return manifest["tiled"]
    disk = _read_json(os.path.join(asset_dir, f"{name}.manifest.json"))
    if isinstance(disk, dict) and isinstance(disk.get("tiled"), dict):
        return disk["tiled"]
    return None


def _maps_in(folder, prefix, maps):
    out = {}
    for m in maps:
        p = os.path.join(folder, f"{prefix}_{m}.png")
        if os.path.isfile(p):
            out[m] = os.path.normpath(p)
    return out


def _sheet_candidates(name, asset_dir, tiled):
    """[(source label, folder, file prefix)] for the family sheet, best first."""
    out = []
    sheet = (tiled or {}).get("sheet") or {}
    if sheet.get("workspace") and sheet.get("name"):
        out.append(("manifest", os.path.normpath(os.path.join(asset_dir, sheet["workspace"])),
                    sheet["name"]))
    parent = os.path.dirname(asset_dir)
    letter = letter_of(name, asset_dir)
    if letter:
        sname = SHEET_DIR_PREFIX[1:] + letter
        out.append(("letter", os.path.join(parent, SHEET_DIR_PREFIX + letter), sname))
    try:
        dirs = sorted(d for d in os.listdir(parent) if d.startswith(SHEET_DIR_PREFIX)
                      and not d.endswith(".aux") and os.path.isdir(os.path.join(parent, d)))
    except OSError:
        dirs = []
    if len(dirs) == 1:
        out.append(("sibling", os.path.join(parent, dirs[0]), dirs[0][1:]))
    return out


def texture_sets(name, asset_dir, manifest=None):
    """
    The texture files each LOD material uses, as the importer binds them.

    Non-tiled (the default): {"tiled": False, "LOD0": <asset_dir>/<name>_<Map>.png that exist,
    "LOD1": None, "LOD2": None} - one material on every LOD (the importer maps LOD textures to
    LOD0, cs2_asset.write_settings).
    Tiled (far set <name>_LOD1_<Map>.png present AND the LOD0 maps resolve to the family sheet):
    "LOD0" = the sheet maps (module docstring), "LOD1" = the far set, "LOD2" = its settings.json
    target (the far set); plus "sheet" (file prefix), "sheet_source" (manifest | letter | sibling |
    settings | owner), "bound" (the import-bound LOD0 files, for the cross-check) and "notes".
    """
    asset_dir = os.path.abspath(asset_dir)
    own = _maps_in(asset_dir, name, MAPS)
    far = _maps_in(asset_dir, f"{name}_LOD1", FAR_MAPS)
    plain = {"tiled": False, "LOD0": own, "LOD1": None, "LOD2": None, "sheet": None,
             "sheet_source": None, "bound": own, "notes": []}
    if "BaseColor" not in far:
        return plain
    shared = _shared_assets(asset_dir)
    tiled = _tiled_block(name, asset_dir, manifest)
    notes = []

    # what the import binds for LOD0: the settings key (a sharer) else the own file (the owner)
    bound = {}
    for m in MAPS:
        tgt = shared.get(f"{name}_{m}.png")
        if tgt:
            p = os.path.normpath(os.path.join(asset_dir, tgt))
            if os.path.isfile(p):
                bound[m] = p
            else:
                notes.append(f"settings binds {name}_{m}.png -> {tgt} (not on disk)")
        elif m in own:
            bound[m] = own[m]
    has_keys = any(f"{name}_{m}.png" in shared for m in MAPS)
    if has_keys:                    # a sharer: bound exactly when settings.json carries the key
        emissive = f"{name}_Emissive.png" in shared
    elif own:                       # the owner: the importer picks <name>_Emissive.png by name
        emissive = "Emissive" in own
    else:
        emissive = bool(((tiled or {}).get("sheet") or {}).get("emissive_bound", True))

    sheet = src = None
    lod0 = {}
    for label, folder, prefix in _sheet_candidates(name, asset_dir, tiled):
        found = _maps_in(folder, prefix, MAPS)
        if "BaseColor" in found:
            sheet, src, lod0 = prefix, label, found
            break
    if not lod0 and "BaseColor" in bound and (has_keys or tiled is not None):
        # no sheet workspace beside the folder: the bound copies ARE the sheet (owner / sharer)
        src = "settings" if has_keys else "owner"
        lod0 = dict(bound)
        sheet = os.path.basename(bound["BaseColor"])[:-len("_BaseColor.png")]
    if not lod0:
        if tiled is not None:
            notes.append("manifest says tiled but no sheet map was found")
        plain["notes"] = notes
        return plain
    if not emissive:
        lod0.pop("Emissive", None)
    for m, p in bound.items():                 # the workspace must be what the import binds
        if m in lod0 and os.path.getsize(p) != os.path.getsize(lod0[m]):
            notes.append(f"{m}: sheet {lod0[m]} differs from the bound {p}; using the bound copy")
            lod0[m] = p

    lod2 = {}
    for m in FAR_MAPS:
        tgt = shared.get(f"{name}_LOD2_{m}.png")
        p = os.path.normpath(os.path.join(asset_dir, tgt)) if tgt else None
        lod2[m] = p if p and os.path.isfile(p) else far.get(m)
    lod2 = {m: p for m, p in lod2.items() if p}
    return {"tiled": True, "LOD0": lod0, "LOD1": far, "LOD2": lod2, "sheet": sheet,
            "sheet_source": src, "bound": bound, "notes": notes}


def pbr_material(name, asset_dir, uv_name, slot_defaults, *, maps=None, mat_name=None,
                 img_tag=None):
    """The recolor material described in the module docstring. `maps` ({Map: path}; default
    <asset_dir>/<name>_<Map>.png) and `mat_name` (default CS2R_<name>_PBR) select the set; images
    are named CS2R_<name>_<img_tag_><Map>."""
    mat = bpy.data.materials.new(mat_name or f"{PACK_PREFIX}{name}_PBR")
    if maps is None:
        maps = _maps_in(asset_dir, name, MAPS)
    tag = f"{img_tag}_" if img_tag else ""

    def _img(suffix, colorspace, packed=False):
        p = maps.get(suffix)
        return _load_image(p, f"{PACK_PREFIX}{name}_{tag}{suffix}", colorspace, packed) if p else None

    nt, out = _fresh_tree(mat)
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (300, 0)
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    uvn = nt.nodes.new("ShaderNodeUVMap")
    uvn.location = (-1500, 0)
    if uv_name:
        uvn.uv_map = uv_name

    def tex(img, y, label):
        node = nt.nodes.new("ShaderNodeTexImage")
        node.image = img
        node.name = node.label = label
        node.location = (-1250, y)
        nt.links.new(uvn.outputs["UV"], node.inputs["Vector"])
        return node

    base_img = _img("BaseColor", "sRGB")
    acc = tex(base_img, 500, "BaseColor").outputs["Color"] if base_img else None
    if acc is None:
        bsdf.inputs["Base Color"].default_value = (0.8, 0.8, 0.8, 1.0)
    ctl_img = _img("ControlMask", "Non-Color")
    if acc is not None and ctl_img is not None:
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        sep.location = (-950, 300)
        nt.links.new(tex(ctl_img, 250, "ControlMask").outputs["Color"], sep.inputs["Color"])
        for slot, ch in ((1, "Red"), (2, "Green"), (3, "Blue")):
            y = 700 - slot * 260
            rgb = nt.nodes.new("ShaderNodeRGB")
            rgb.name = f"Slot{slot}"
            rgb.label = SLOT_LABELS[slot]
            rgb.outputs[0].default_value = tuple(slot_defaults[slot]) + (1.0,)
            rgb.location = (-950, y)
            attr = nt.nodes.new("ShaderNodeAttribute")
            attr.name = f"Slot{slot}Attr"
            attr.attribute_type = "INSTANCER"
            attr.attribute_name = SLOT_ATTR.format(slot)
            attr.location = (-950, y - 120)
            pick = _mix(nt, attr.outputs["Alpha"], rgb.outputs[0], attr.outputs["Color"],
                        loc=(-650, y))
            mask = _mix(nt, sep.outputs[ch], (1.0, 1.0, 1.0), pick, loc=(-400, y))
            acc = _mix(nt, 1.0, acc, mask, blend="MULTIPLY", loc=(-150, y))
    if acc is not None:
        nt.links.new(acc, bsdf.inputs["Base Color"])

    nrm_img = _img("Normal", "Non-Color")
    if nrm_img is not None:
        nmap = nt.nodes.new("ShaderNodeNormalMap")
        nmap.location = (-150, -350)
        if uv_name:
            nmap.uv_map = uv_name
        nt.links.new(tex(nrm_img, -350, "Normal").outputs["Color"], nmap.inputs["Color"])
        nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])

    msk_img = _img("MaskMap", "Non-Color", packed=True)
    if msk_img is not None:
        msk = tex(msk_img, -650, "MaskMap")
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        sep.location = (-950, -650)
        nt.links.new(msk.outputs["Color"], sep.inputs["Color"])
        nt.links.new(sep.outputs["Red"], bsdf.inputs["Metallic"])
        nt.links.new(sep.outputs["Green"], bsdf.inputs["Coat Weight"])
        inv = nt.nodes.new("ShaderNodeMath")
        inv.operation = "SUBTRACT"
        inv.inputs[0].default_value = 1.0
        inv.location = (-650, -800)
        nt.links.new(msk.outputs["Alpha"], inv.inputs[1])
        nt.links.new(inv.outputs["Value"], bsdf.inputs["Roughness"])
    else:
        bsdf.inputs["Roughness"].default_value = 0.7

    emi_img = _img("Emissive", "sRGB")
    if emi_img is not None:
        nt.links.new(tex(emi_img, -1000, "Emissive").outputs["Color"],
                     bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = cs2_preview.EMISSIVE_STRENGTH
    return mat


def glass_material(name):
    """`_Gls` mock: a Glass BSDF (the game uses DefaultGlassDoubleSided, no textures)."""
    mat = bpy.data.materials.new(f"{PACK_PREFIX}{name}_Gls")
    nt, out = _fresh_tree(mat)
    g = nt.nodes.new("ShaderNodeBsdfGlass")
    g.inputs["Color"].default_value = GLASS_RGB + (1.0,)
    g.inputs["Roughness"].default_value = GLASS_ROUGHNESS
    g.inputs["IOR"].default_value = GLASS_IOR
    nt.links.new(g.outputs["BSDF"], out.inputs["Surface"])
    for attr, val in (("use_raytrace_refraction", True), ("use_backface_culling", False)):
        if hasattr(mat, attr):
            setattr(mat, attr, val)
    return mat


def window_material(name):
    """`_Win` mock: dark glass + warm emission x max(node WinEmission, instancer prop)."""
    mat = bpy.data.materials.new(f"{PACK_PREFIX}{name}_Win")
    nt, out = _fresh_tree(mat)
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (300, 0)
    bsdf.inputs["Base Color"].default_value = WIN_GLASS_RGB + (1.0,)
    bsdf.inputs["Roughness"].default_value = WIN_GLASS_ROUGHNESS
    bsdf.inputs["Emission Color"].default_value = WIN_WARM_RGB + (1.0,)
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    toggle = nt.nodes.new("ShaderNodeValue")
    toggle.name = "WinEmission"
    toggle.label = "Window emission (0 off / 1 on)"
    toggle.outputs[0].default_value = 0.0
    toggle.location = (-500, -200)
    attr = nt.nodes.new("ShaderNodeAttribute")
    attr.name = "WinEmissionAttr"
    attr.attribute_type = "INSTANCER"
    attr.attribute_name = WIN_EMIT_ATTR
    attr.location = (-500, -350)
    mx = nt.nodes.new("ShaderNodeMath")
    mx.operation = "MAXIMUM"
    mx.location = (-250, -250)
    nt.links.new(toggle.outputs[0], mx.inputs[0])
    nt.links.new(attr.outputs["Fac"], mx.inputs[1])
    mul = nt.nodes.new("ShaderNodeMath")
    mul.operation = "MULTIPLY"
    mul.inputs[1].default_value = WIN_EMIT_STRENGTH
    mul.location = (0, -250)
    nt.links.new(mx.outputs[0], mul.inputs[0])
    nt.links.new(mul.outputs[0], bsdf.inputs["Emission Strength"])
    return mat


# --------------------------------------------------------------------------------------
# The pack
# --------------------------------------------------------------------------------------

def _duplicate(src, new_name, material, role):
    """Object + mesh copy, unparented at the same world matrix, one material slot."""
    dup = src.copy()
    dup.data = src.data.copy()
    dup.name = new_name
    dup.data.name = new_name
    mw = src.matrix_world.copy()
    dup.parent = None
    dup.matrix_world = mw
    dup.animation_data_clear()
    for m in list(dup.modifiers):
        dup.modifiers.remove(m)
    dup.data.materials.clear()
    dup.data.materials.append(material)
    dup.hide_render = False
    dup.hide_viewport = False
    dup["cs2r_role"] = role
    dup["tris"] = cs2_asset.tri_count(dup)
    return dup


def _collection_props(coll, props):
    for key in sorted(props):
        val = props[key]
        if val is None:
            continue
        if isinstance(val, dict):
            val = json.dumps(_jsonable(val), sort_keys=True)
        elif isinstance(val, (list, tuple)):
            val = [float(x) if isinstance(x, float) else x for x in val]
        coll[key] = val


def _mat_files(mat):
    """Sorted file names of the images a material's texture nodes use."""
    if mat is None or mat.node_tree is None:
        return []
    return sorted({os.path.basename(bpy.path.abspath(n.image.filepath))
                   for n in mat.node_tree.nodes if n.type == "TEX_IMAGE" and n.image is not None})


def _prune_stage(stage_dir, keep):
    """Delete PNGs in <out_dir>/_tex/<Name>/ the pack just written does not use (a renamed or
    re-sourced set must not leave a stale copy that looks current)."""
    if not stage_dir or not os.path.isdir(stage_dir) \
            or os.path.basename(os.path.dirname(stage_dir)) != "_tex":
        return 0
    n = 0
    for f in os.listdir(stage_dir):
        p = os.path.join(stage_dir, f)
        if f.lower().endswith(".png") and f not in keep and os.path.isfile(p):
            os.remove(p)
            n += 1
    return n


def write_pack(name, asset_dir, lod_objs, submeshes=None, *, meta=None, out_dir=None,
               manifest=None, extra_meshes=None):
    """
    Write `<out_dir>/<name>.blend` holding collection CS2R_<name> built from DUPLICATES of
    `lod_objs` ({0: obj, 1: obj, 2: obj}) and `submeshes` ({suffix: obj}); see the module
    docstring. `meta` keys used: generator, args, kind, caps ({level: tris}), letter,
    tint ({"R"|"G"|"B"|1|2|3: rgb} slot-default overrides, e.g. the chair's bench slot 3).
    `manifest`: the asset's manifest for its vanilla sub-object rows (a dict, or just
    {"sub_objects": block}; a path; {} = none; None = read <asset_dir>/<name>.manifest.json when
    the generator has written it already). Returns the absolute pack path. The build session
    is left exactly as it was.
    """
    t0 = time.perf_counter()
    name = cs2_asset.validate_asset_name(name)
    lods = {int(k): o for k, o in (lod_objs or {}).items() if o is not None}
    if 0 not in lods:
        raise ValueError("write_pack: lod_objs needs level 0")
    subs = {str(k): o for k, o in (submeshes or {}).items() if o is not None}
    meta = dict(meta or {})
    asset_dir = os.path.abspath(asset_dir)
    out_dir = os.path.abspath(out_dir or DEFAULT_OUT_DIR)
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, f"{name}.blend")
    global _STAGE_DIR
    _STAGE_DIR = os.path.join(out_dir, "_tex", name)     # T63: textures live beside the pack

    slot_defaults = dict(SLOT_DEFAULTS)
    for k, rgb in (meta.get("tint") or {}).items():
        slot = {"R": 1, "G": 2, "B": 3}.get(str(k).upper(), None) or int(k)
        slot_defaults[slot] = tuple(float(c) for c in rgb)

    tex = texture_sets(name, asset_dir, manifest)
    for note in tex["notes"]:
        print(f"[review] WARN {name}: {note}")
    _STAGED.clear()
    before = _snapshot()
    try:
        uv_name = _shipped_uv(lods[0].data)
        pbr = pbr_material(name, asset_dir, uv_name, slot_defaults, maps=tex["LOD0"])
        lod_mat = {0: pbr}
        if tex["tiled"]:
            # LOD1 / LOD2 wear the far set through their own UVs (one material per distinct
            # map set + UV layer: LOD2's settings target is LOD1's files, so normally one)
            far_mats = {}
            for level in sorted(k for k in lods if k > 0):
                maps = tex.get(f"LOD{level}") or tex["LOD1"]
                uv = _shipped_uv(lods[level].data)
                key = (tuple(sorted(maps.items())), uv)
                if key not in far_mats:
                    far_mats[key] = pbr_material(
                        name, asset_dir, uv, slot_defaults, maps=maps,
                        mat_name=f"{PACK_PREFIX}{name}_PBR_LOD{level}", img_tag=f"LOD{level}")
                lod_mat[level] = far_mats[key]
        glass = window = None

        root = bpy.data.collections.new(f"{PACK_PREFIX}{name}")
        children = {}
        for role in ROLES:
            c = bpy.data.collections.new(role)
            c["cs2r_role"] = role
            root.children.link(c)
            children[role] = c

        dups = {}
        for level in sorted(lods):
            role = f"LOD{level}"
            if role not in children:
                raise ValueError(f"write_pack: LOD level {level} is outside {ROLES[:3]}")
            dup = _duplicate(lods[level], f"{PACK_PREFIX}{name}_{role}",
                             lod_mat.get(level, pbr), role)
            children[role].objects.link(dup)
            dups[role] = dup
        for suffix in sorted(subs):
            if suffix.startswith(WIN_FAMILY):
                window = window or window_material(name)
                mat = window
            elif suffix.startswith(GLASS_FAMILY):
                glass = glass or glass_material(name)
                mat = glass
            else:
                mat = pbr
            dup = _duplicate(subs[suffix], f"{PACK_PREFIX}{name}_{suffix}", mat, "SUB")
            dup["cs2r_suffix"] = suffix
            children["SUB"].objects.link(dup)
            dups[suffix] = dup
        extra_names = []
        for suffix in sorted(extra_meshes or {}):
            src, tex_name, tex_dir = extra_meshes[suffix]
            if src is None:
                continue
            emat = pbr_material(tex_name, os.path.abspath(tex_dir), _shipped_uv(src.data),
                                slot_defaults)
            dup = _duplicate(src, f"{PACK_PREFIX}{name}_{suffix}", emat, "SUB")
            dup["cs2r_suffix"] = suffix
            dup["cs2r_extra"] = tex_name
            children["SUB"].objects.link(dup)
            extra_names.append(f"{suffix}:{tex_name}")

        # -- vanilla sub-object stand-ins (DECISIONS 183): boxes + labels in SUB, pack only -----
        standin = {"n": 0, "families": {}, "unknown": 0, "replaced": [], "manifest": None}
        try:
            import cs2_subobj
            rows, rinfo = cs2_subobj.collect_rows(name, asset_dir, manifest)
            _l0, _h0 = _world_bbox([dups["LOD0"]])           # labels scale with the pack
            standin = cs2_subobj.add_standins(name, rows, root, children["SUB"], rinfo,
                                              ref_size=math.dist(_l0, _h0))
        except Exception as exc:  # noqa: BLE001 - a stand-in problem must never lose the pack
            import traceback
            traceback.print_exc()
            standin["error"] = f"{type(exc).__name__}: {exc}"
            print(f"[review] WARN {name}: sub-object stand-ins failed ({standin['error']}); "
                  f"the pack is written without them")

        # -- measurements (duplicates) ----------------------------------------------
        lo, hi = _world_bbox([dups["LOD0"]])
        size = [hi[i] - lo[i] for i in range(3)]
        area = float(cs2_uv.face_world_areas(dups["LOD0"].data).sum())
        tris = {k: int(d["tris"]) for k, d in dups.items()}
        levels = sorted(int(k[3:]) for k in tris if k.startswith("LOD"))
        sw, sw_noarea = switch_tables(size, [tris[f"LOD{k}"] for k in levels], area)
        base_png = tex["LOD0"].get("BaseColor")
        A = png_size(base_png)[0] if base_png else 0
        far_png = (tex["LOD1"] or {}).get("BaseColor")
        lod_tex = {f"LOD{k}": _mat_files(lod_mat.get(k, pbr)) for k in sorted(lods)}
        n_images = sum(1 for i in bpy.data.images if i not in before["images"])
        exported = os.path.isfile(os.path.join(asset_dir, "settings.json")) or any(
            f.lower().endswith(".fbx") for f in (os.listdir(asset_dir) if os.path.isdir(asset_dir) else []))
        bare = sorted(r for r, files in lod_tex.items() if not files)
        if bare and exported and meta.get("kind") != "stand-in":
            # T63-tiled gate: an exported asset never gets a silently untextured pack (the W
            # lodges reviewed on 2026-10-17 were flat meshes because nothing matched)
            raise ValueError(
                f"write_pack {name}: {', '.join(bare)} material(s) have NO image (asset folder "
                f"{asset_dir}: own maps {sorted(tex['LOD0'])}, far set "
                f"{sorted(tex['LOD1'] or {})}, tiled {tex['tiled']}, notes {tex['notes']}); "
                f"no pack written")
        dens = density_stats(dups["LOD0"], A) if A else None
        caps = _caps(meta)
        letter = meta.get("letter") or letter_of(name, asset_dir)
        try:
            rel_asset = os.path.relpath(asset_dir, REPO_ROOT)
        except ValueError:                      # another drive: keep it absolute
            rel_asset = asset_dir

        props = {
            "cs2r_pack": PACK_VERSION,
            "name": name,
            "letter": letter,
            "kind": meta.get("kind"),
            "generator": meta.get("generator"),
            "args": json.dumps(_jsonable(meta.get("args")), sort_keys=True),
            "git_sha": git_sha(),
            "blender": bpy.app.version_string,
            "asset_dir": rel_asset.replace("\\", "/"),
            "written_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "submeshes": ",".join(sorted(subs)),
            "bbox_min": [round(v, 4) for v in lo],
            "bbox_max": [round(v, 4) for v in hi],
            "surface_area_m2": round(area, 3),
            "texture_px": A,
            "lod_factor": LOD_FACTOR,
            "lod_bias": round(sw["bias"], 4),
            "lod_cull_m": round(sw["cull"], 2),
            "uv_layer": uv_name or "",
            "tiled": int(bool(tex["tiled"])),
            "n_images": n_images,
            "pack_from": meta.get("pack_from") or "generator",
        }
        for role, files in lod_tex.items():
            props[f"tex_{role}"] = ",".join(files)
        if tex["tiled"]:
            props["sheet"] = tex["sheet"]
            props["sheet_source"] = tex["sheet_source"]
            props["far_px"] = png_size(far_png)[0] if far_png else 0
        if tex["notes"]:
            props["tex_notes"] = "; ".join(tex["notes"])
        for k, n in tris.items():
            props[f"tris_{k}"] = n
        for k, cap in caps.items():
            props[f"cap_LOD{k}"] = cap
        for k, d in sw["switch"].items():
            props[f"lod_switch_m_LOD{levels[k]}"] = round(d, 2)
        for k, d in sw_noarea["switch"].items():
            props[f"lod_switch_m_noarea_LOD{levels[k]}"] = round(d, 2)
        if dens:
            props["density_px_per_m_median"] = dens["median"]
            props["density_px_per_m_min"] = dens["min"]
            props["density_min_part"] = dens["min_part"]
        for slot, rgb in slot_defaults.items():
            props[f"slot{slot}_default"] = [round(float(c), 4) for c in rgb]
        props["n_prop_standins"] = int(standin["n"])
        if extra_meshes:
            props["extra_meshes"] = ",".join(extra_names)
        if standin.get("signset_rows"):
            props["signset_rows"] = json.dumps(standin["signset_rows"], sort_keys=True)
        if standin["n"]:
            props["prop_families"] = standin["families"]
            props["prop_bounds_unknown"] = int(standin["unknown"])
            props["subobj_source"] = os.path.basename(str(standin.get("manifest") or ""))
            if standin.get("replaced"):
                props["subobj_replaced"] = "; ".join(standin["replaced"])
        if standin.get("error"):
            props["subobj_error"] = standin["error"]
        full = dict(props)
        full.update({"tris": tris, "caps": caps, "density": dens,
                     "lod_switch": {"with_area": sw, "no_area": sw_noarea},
                     "sub_objects": standin.get("rows", []),
                     "meta": {k: v for k, v in meta.items() if k not in ("args",)}})
        props["cs2r_meta_json"] = json.dumps(_jsonable(full), sort_keys=True)
        _collection_props(root, props)

        bpy.data.libraries.write(path, {root}, path_remap="RELATIVE_ALL", fake_user=True,
                                 compress=True)
        pruned = _prune_stage(_STAGE_DIR, set(_STAGED))
    finally:
        _STAGE_DIR = None
        _STAGED.clear()
        removed = _purge_new(before)
    sw_txt = ", ".join(f"LOD{levels[k]} {d:.0f} m" for k, d in sw["switch"].items()) or "none"
    print(f"[review] {name}: pack -> {path} ({os.path.getsize(path)} bytes, "
          f"{time.perf_counter() - t0:.2f} s); tris {tris}; switch {sw_txt}; density "
          f"{'median %.0f / min %.0f px/m' % (dens['median'], dens['min']) if dens else 'n/a'}; "
          + (f"{standin['n']} prop stand-ins {standin['families']}"
             f"{' (%d unknown bounds)' % standin['unknown'] if standin['unknown'] else ''}; "
             if standin["n"] else "")
          + (f"tiled: LOD0 on {props.get('sheet')} ({props.get('sheet_source')}), far set "
             f"{props.get('far_px')} px; " if tex["tiled"] else "")
          + f"{n_images} images ({'; '.join(f'{r} {len(f)}' for r, f in lod_tex.items())})"
          + (f", {pruned} stale _tex files removed" if pruned else "")
          + f"; {removed} temporary datablocks removed")
    return path


def write_standin_pack(fbx_path=STANDIN_FBX, name=None, out_dir=None):
    """
    Write a SHIPPED asset's FBX as an LOD0-only review pack `<out_dir>/<name>.blend`
    (name: the FBX stem), textured from the FBX's own folder. Headless only: the FBX is
    re-imported with cs2_preview.import_generation and stood up when it comes back lying
    on its back (its "min y ~ 0" Y-up test, cs2_preview.py:757, misreads FG8N's pier that
    runs 4 m below grade; +90 deg X undoes export_fbx's (x, y, z) -> (x, z, -y)), then
    removed again with everything the import created; selection and active object are
    restored. review_live.stand_ins links the result, so the live session never imports.
    """
    if not bpy.app.background:
        raise RuntimeError("write_standin_pack runs headless only (ROUND-SPEC.md R10)")
    fbx_path = os.path.abspath(fbx_path)
    if not os.path.isfile(fbx_path):
        raise FileNotFoundError(fbx_path)
    name = cs2_asset.validate_asset_name(name or os.path.splitext(os.path.basename(fbx_path))[0])
    before = _snapshot()
    vl = bpy.context.view_layer
    active = vl.objects.active
    selected = [o for o in vl.objects if o is not None and o.select_get()]
    try:
        obj = cs2_preview.import_generation(fbx_path)
        if obj.dimensions.y > obj.dimensions.z:
            obj.data.transform(mathutils.Matrix.Rotation(math.radians(90.0), 4, "X"))
            obj.data.update()
            vl.update()
        try:
            rel = os.path.relpath(fbx_path, REPO_ROOT).replace("\\", "/")
        except ValueError:
            rel = fbx_path
        return write_pack(name, os.path.dirname(fbx_path), {0: obj},
                          meta={"generator": "cs2_review.write_standin_pack",
                                "kind": "stand-in", "args": {"fbx": rel}},
                          out_dir=out_dir)
    finally:
        _purge_new(before)
        for o in list(vl.objects):
            if o is not None and o.select_get():
                o.select_set(False)
        for o in selected:
            try:
                o.select_set(True)
            except (ReferenceError, RuntimeError):
                pass
        try:
            vl.objects.active = active
        except (ReferenceError, RuntimeError):
            vl.objects.active = None


# --------------------------------------------------------------------------------------
# From an exported folder (no generator run)
# --------------------------------------------------------------------------------------

def _is_identity(m, eps=1e-6):
    return all(abs(m[i][j] - (1.0 if i == j else 0.0)) <= eps for i in range(4) for j in range(4))


def import_exported_fbx(path):
    """
    Import one FBX written by cs2_asset.export_fbx and return it in meters, Z-up, identity
    transform, named after the file stem. Blender 5.x wm.fbx_import keeps the raw vertices
    (cm, Y-up: export baked (x, y, z) -> (x, z, -y) x 100, cs2_asset.py:1300-1302) and an
    identity object matrix (probed on 5.2.2: SkiLodgeHutCommercialLowW raw y -720..880 = the
    manifest's 16.0 m height, min_y -7.2); FBX_TO_BLENDER undoes it on the mesh data. Custom
    split normals are re-set rotated so shading survives. Uses the operator: headless only.
    """
    import numpy as np
    if not bpy.app.background:
        raise RuntimeError("import_exported_fbx runs headless only (ROUND-SPEC.md R10)")
    if not hasattr(bpy.ops.wm, "fbx_import"):
        raise RuntimeError("import_exported_fbx needs Blender's wm.fbx_import (5.x); the legacy "
                           "importer moves the axis fix onto the object and is not supported")
    before = set(bpy.data.objects)
    bpy.ops.wm.fbx_import(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == "MESH"]
    if len(meshes) != 1:
        raise RuntimeError(f"{path}: expected one mesh, the import made "
                           f"{[(o.name, o.type) for o in new]}")
    obj = meshes[0]
    if obj.parent is not None or not _is_identity(obj.matrix_world):
        raise RuntimeError(f"{path}: the importer applied a transform {list(map(tuple, obj.matrix_world))}"
                           f" (parent {obj.parent}); expected raw data with an identity matrix")
    me = obj.data
    custom = bool(getattr(me, "has_custom_normals", False))
    if custom:
        cn = np.empty(len(me.loops) * 3, dtype=np.float64)
        me.corner_normals.foreach_get("vector", cn)
        rot = np.array(FBX_TO_BLENDER.to_3x3().normalized(), dtype=np.float64)
        cn = (cn.reshape(-1, 3) @ rot.T)
    me.transform(FBX_TO_BLENDER)
    if custom:
        me.normals_split_custom_set([tuple(v) for v in cn])
    me.update()
    stem = os.path.splitext(os.path.basename(path))[0]
    obj.name = stem
    me.name = stem
    return obj


def _check_extent(obj, manifest, label):
    """The re-imported LOD0 against the manifest's extent_game_m (game axes: y up, z = -Blender
    y). An axis or unit slip is meters off; returns a note or raises."""
    ext = (manifest or {}).get("extent_game_m")
    if not isinstance(ext, dict):
        return "no extent_game_m in the manifest: orientation not cross-checked"
    import numpy as np
    co = np.empty(len(obj.data.vertices) * 3, dtype=np.float64)
    obj.data.vertices.foreach_get("co", co)      # identity matrix: data = world
    co = co.reshape(-1, 3)
    lo, hi = co.min(axis=0), co.max(axis=0)
    got = {"x": hi[0] - lo[0], "y": hi[2] - lo[2], "z": hi[1] - lo[1], "min_y": lo[2]}
    bad = {k: (round(got[k], 3), ext[k]) for k in ("x", "y", "z", "min_y")
           if k in ext and abs(got[k] - float(ext[k])) > EXTENT_TOL_M}
    if bad:
        raise RuntimeError(f"{label}: re-imported extent {bad} (got, manifest) is off by more than "
                           f"{EXTENT_TOL_M} m: the FBX axis / unit fix is wrong for this file")
    return None


def _sha1(path):
    import hashlib
    h = hashlib.sha1()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def _folder_vs_manifest(asset_dir, manifest):
    """Files the manifest lists whose bytes differ (or are missing) in the folder."""
    out = []
    for fn, rec in sorted(((manifest or {}).get("files") or {}).items()):
        p = os.path.join(asset_dir, fn)
        if not os.path.isfile(p):
            out.append(f"{fn}: missing")
        elif isinstance(rec, dict) and rec.get("sha1") and _sha1(p) != rec["sha1"]:
            out.append(f"{fn}: sha1 differs from the manifest")
    return out


def _signset_extra(manifest, letter):
    """(fbx path, texture name, texture dir) of the manifest's sign set, or (None, reason)."""
    ss = ((manifest or {}).get("fine_detail") or {}).get("sign_set")
    if not isinstance(ss, dict) or not ss.get("mesh"):
        return None, None, None
    mesh = ss["mesh"]
    cands = []
    if ss.get("asset_dir"):
        cands.append(ss["asset_dir"])
    cands.append(os.path.join(REPO_ROOT, "assets", "import", "signs", mesh[-1:] or letter, mesh))
    for d in cands:
        fbx = os.path.join(d, f"{mesh}.fbx")
        if not os.path.isfile(fbx):
            continue
        tgt = _shared_assets(d).get(f"{mesh}_BaseColor.png")
        if tgt:
            p = os.path.normpath(os.path.join(d, tgt))
            if os.path.isfile(p):
                return fbx, os.path.basename(p)[:-len("_BaseColor.png")], os.path.dirname(p)
        if os.path.isfile(os.path.join(d, f"{mesh}_BaseColor.png")):
            return fbx, mesh, d
    return None, mesh, None


def _recipe_meta(manifest):
    """tint / lot_cells / caps the generator would have passed (lodge.py:1652-1658)."""
    out = {}
    budget = (manifest or {}).get("budget") or {}
    tris = (manifest or {}).get("tris") or {}
    caps = {0: budget.get("lod0_cap"), 1: tris.get("1"), 2: budget.get("lod2_cap")}
    out["caps"] = {k: v for k, v in caps.items() if v is not None}
    key = (manifest or {}).get("recipe")
    if key and (manifest or {}).get("generator") == "lodge.py":
        try:
            import bldg_recipes
            rc = bldg_recipes.get_recipe(key)
            out["tint"] = {"RGB"[k - 1]: tuple(v) for k, v in rc.slots.items()}
            out["lot_cells"] = list(rc.lot)
        except Exception as exc:  # noqa: BLE001 - a recipe problem keeps the default tint
            out["tint_error"] = f"{type(exc).__name__}: {exc}"
    return out


def write_folder_pack(asset_dir, out_dir=None, name=None):
    """
    Write the review pack of an EXPORTED asset folder (module docstring): no generator run, so a
    pack is minutes, not a 20-minute bake, and shows exactly the files Richard imports. LOD0 =
    <name>.fbx, LOD1 / LOD2 = <name>_LOD<k>.fbx, subs = every other <name>_<suffix>.fbx whose
    suffix does not start with LOD (the generator hands write_pack the LOD0 subs only), the
    manifest's sign set as the SignSet extra (its texture via its own settings.json). The
    manifest (rows, tiled block, recipe) goes to write_pack whole. Headless only; the session is
    left as found (everything imported is removed again).
    """
    if not bpy.app.background:
        raise RuntimeError("write_folder_pack runs headless only (ROUND-SPEC.md R10)")
    asset_dir = os.path.abspath(asset_dir)
    name = cs2_asset.validate_asset_name(name or os.path.basename(asset_dir.rstrip("\\/")))
    lod0_fbx = os.path.join(asset_dir, f"{name}.fbx")
    if not os.path.isfile(lod0_fbx):
        raise FileNotFoundError(f"write_folder_pack: no {name}.fbx in {asset_dir}")
    man_path = os.path.join(asset_dir, f"{name}.manifest.json")
    manifest = _read_json(man_path) if os.path.isfile(man_path) else None
    letter = letter_of(name, asset_dir)
    before = _snapshot()
    vl = bpy.context.view_layer
    active = vl.objects.active
    selected = [o for o in vl.objects if o is not None and o.select_get()]
    try:
        lods = {0: import_exported_fbx(lod0_fbx)}
        notes = [n for n in [_check_extent(lods[0], manifest, name)] if n]
        for k in (1, 2):
            p = os.path.join(asset_dir, f"{name}_LOD{k}.fbx")
            if os.path.isfile(p):
                lods[k] = import_exported_fbx(p)
        for k, o in lods.items():          # the importer may drop degenerate triangles
            want = ((manifest or {}).get("tris") or {}).get(str(k))
            got = cs2_asset.tri_count(o)
            if want is not None and int(want) != got:
                notes.append(f"LOD{k}: {got} tris re-imported vs {want} in the manifest")
        subs = {}
        for f in sorted(os.listdir(asset_dir)):
            if not (f.lower().endswith(".fbx") and f.startswith(name + "_")):
                continue
            suffix = f[len(name) + 1:-4]
            if suffix.upper().startswith("LOD"):
                continue
            subs[suffix] = import_exported_fbx(os.path.join(asset_dir, f))
        extra = {}
        ss_fbx, ss_tex, ss_dir = _signset_extra(manifest, letter)
        if ss_fbx:
            extra["SignSet"] = (import_exported_fbx(ss_fbx), ss_tex, ss_dir)
        elif ss_tex:
            notes.append(f"sign set {ss_tex}: folder not found, not shown")
        mism = _folder_vs_manifest(asset_dir, manifest)
        for n in notes + mism:
            print(f"[review] WARN {name}: {n}")
        try:
            rel = os.path.relpath(asset_dir, REPO_ROOT).replace("\\", "/")
        except ValueError:
            rel = asset_dir
        meta = {"generator": (manifest or {}).get("generator") or "unknown",
                "kind": "building" if (manifest or {}).get("generator") == "lodge.py"
                else (manifest or {}).get("kind") or "asset",
                "letter": letter, "pack_from": "folder",
                "args": {"from_folder": rel, "manifest_written_utc": (manifest or {}).get("written_utc"),
                         "source_git_sha": (manifest or {}).get("git_sha"),
                         "recipe": (manifest or {}).get("recipe"),
                         "sign_set": f"{ss_tex} <- {ss_fbx}" if ss_fbx else None,
                         "notes": notes, "folder_vs_manifest": mism}}
        meta.update(_recipe_meta(manifest))
        vl.update()
        return write_pack(name, asset_dir, lods, subs, meta=meta, out_dir=out_dir,
                          manifest=manifest if manifest is not None else {},
                          extra_meshes=extra or None)
    finally:
        _purge_new(before)
        for o in list(vl.objects):
            if o is not None and o.select_get():
                o.select_set(False)
        for o in selected:
            try:
                o.select_set(True)
            except (ReferenceError, RuntimeError):
                pass
        try:
            vl.objects.active = active
        except (ReferenceError, RuntimeError):
            vl.objects.active = None


def _main(argv):
    import argparse
    import traceback
    ap = argparse.ArgumentParser(prog="cs2_review.py",
                                 description="headless review packs (ROUND-SPEC R10)")
    ap.add_argument("--stand-in", nargs="?", const=STANDIN_FBX, default=None, metavar="FBX",
                    help=f"write a shipped FBX as a stand-in pack (default {STANDIN_FBX})")
    ap.add_argument("--from-folder", nargs="+", default=None, metavar="DIR",
                    help="write the pack of each EXPORTED asset folder (no generator run)")
    ap.add_argument("--out", default=None, metavar="DIR",
                    help=f"pack folder (default {DEFAULT_OUT_DIR})")
    args = ap.parse_args(argv)
    if args.stand_in is None and not args.from_folder:
        ap.error("nothing to do: pass --stand-in [FBX] or --from-folder DIR [DIR ...]")
    failed = []
    if args.stand_in is not None:
        try:
            print(f"[review] stand-in pack: {write_standin_pack(args.stand_in, out_dir=args.out)}")
        except Exception:  # noqa: BLE001 - make the exit status unambiguous for the harness
            traceback.print_exc()
            failed.append(args.stand_in)
    for d in args.from_folder or []:
        try:
            print(f"[review] folder pack: {write_folder_pack(d, out_dir=args.out)}")
        except Exception:  # noqa: BLE001 - one bad folder must not hide the others' result
            traceback.print_exc()
            failed.append(d)
    if failed:
        print(f"[review] FAILED: {failed}")
        sys.exit(1)


if __name__ == "__main__":
    _main(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
