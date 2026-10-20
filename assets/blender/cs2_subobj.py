"""
cs2_subobj.py - vanilla-prop sub-object rows -> labeled review stand-ins (DECISIONS 183).

WHY
---
Standing rule (Richard, 2026-10-11 ~22:05): vanilla props are REFERENCED as ObjectSubObjects rows
(prefab GUID, position, rotation, m_ParentMesh) and never modeled. A review pack that shows only
our own mesh hides them, so Richard never sees where the benches, bins, lamps and tables go.
`cs2_review.write_pack` calls this module: every row becomes one semi-transparent BOX of the prop's
union bounds, with a text label, in the pack's SUB collection. Nothing is exported (the pack
only); the stand-ins never reach an FBX, a PNG or a manifest.

    CS2R_<Name>_prop<NN>            box (cs2r_prop "box"), identity transform, corners in the pack frame
    CS2R_<Name>_prop<NN>_label[k]   the label: LABEL_FACES (4) text objects 90 degrees apart, children of
                                    the box; each hides its own back (Backfacing -> Transparent), so any
                                    orbit direction shows one un-mirrored face (cs2r_prop "label",
                                    cs2r_label_face 0..3; face 0 looks at the three_quarter camera)
    custom props on both: cs2r_prefab / guid / family / parent_mesh / pos_game / rot_game /
                          bounds_lo / bounds_hi / bounds_src / row_source / mount / own_prop
    SUB_NOPROPS                     sibling collection with SUB's pre-existing sub-meshes only
Materials per family: CS2R_<Name>_Prop_<family> (alpha 0.35, BLENDED) and ..._PropText_<family>.

READER (one function for every block shape the generators write today)
----------------------------------------------------------------------
    rows_from_manifest(manifest) -> [row, ...]       game-local rows, one dict per sub-object
    beacon_rows(name)            -> [row, ...]       the SkiBeaconRedA hosting rows of a tower
    collect_rows(name, asset_dir, manifest=None)     both, de-duplicated -> (rows, info)

row = {prefab, guid (32 hex or None), pos (gx, gy, gz), rot (x, y, z, w), parent (m_ParentMesh),
       mount, source, index, own_bounds (own props only)}; pos / rot are PREFAB-LOCAL ("game local").
Block shapes read (tests/test_review_subobjects.py reads every shipped manifest):
  sub_objects.objects[]                    lodge.sub_objects_block: m_Object / prefab / position /
                                           rotation / m_ParentMesh (L1, L2, school, P1, P2)
  detail1.sub_objects.objects[]            cs2_liftdress.sub_object_rows: guid ("UnityGUID:...") /
                                           prefab / pos / rot / parent (DQ stations)
  detail1.sub_objects.{rows, block}        tram stations: block.objects carries the GUIDs, rows the
                                           mount / box (merged by index)
  rev1.sub_objects (same two shapes)       the gondola stations' revision 1
  night_subobjects[]                       lattice towers: prefab / guid / position_net_m /
                                           rotation_xyzw / parent_mesh (60A: WarningLightRandom01)
  props/A/SkiBeaconRedA hosting.rows[]     the beacon addendum (towers-transfer B1): ONE row per host
                                           tower, anchor = beacon origin, matched by `mesh` == name;
                                           it REPLACES a night_subobjects WarningLight at the same spot
  sub_objects.objects[] with "kind": "mod" OUR OWN props by object-prefab CID (fine-detail pass,
                                           fine_detail.py: the dressing kit props/B, the sign sets):
                                           m_Object = the CID, prefab = the object prefab, mesh =
                                           the mesh asset; family "mod" (FAMILY_RGB cyan), label =
                                           the full prefab name; bounds from the JSON cache keyed by
                                           prefab name (the kit's 11 entries) or the row's own
                                           "bounds"; role "signset" rows get NO box (the pack shows
                                           the real textured sign-set mesh, cs2_review extra_meshes).
                                           A kit row with "colour_set" (kit color variants, 2026-10-12)
                                           is TINTED with its set (colour_set_rgb: the set's slot-2 value
                                           x RECOLOR_ALBEDO 0.80, what the 0.80 accent texels show) and
                                           labeled with its variant name (SkiDressSkiRackV2_Red)

FRAMES (verified by ray-casting every shipped row onto its pack's LOD0, 2026-10-12)
-----------------------------------------------------------------------------------
Rows are game-local (x lateral, y up, z travel). The packs hold the generators' NET-ORIENTED
objects (cs2_asset.orient_for_net runs before write_pack in every generator), export_fbx
bakes (x, y, z) -> (x, z, -y) and the game negates FBX x on import (x-sign gate settled N,
2026-10-15 playtest; 2026-10-16 / T14: before that this module drew pack = (gx, -gz, gy), the
old +x frame), so game (gx, gy, gz) = pack (-bx, bz, -by) and
    pack = (-gx, -gz, gy)                    game_to_pack()
(tram_station._Q, lift_terminal_detach._post(blender_to_game(...)) are the same map). A yaw
quaternion turns the prop's local +Z towards +X (cs2_liftdress.facing_quat); the corner transform
below is cs2_liftdress.subobj_box's, so a stand-in's world AABB equals subobj_box(...) mapped to
the pack frame (the test asserts it). m_ParentMesh 0 = on the main mesh, -1 = on the lot (snapped
to terrain at runtime): both are placed at the row's position, -1 gets "(ground)" in its label.

BOUNDS
------
Union of the placeholder mesh and every variant mesh (RenderPrefab m_Bounds, game local, y up):
  1. cs2_liftdress.SUBOBJ (verified against the dump, K7)      source "SUBOBJ"
  2. assets/blender/subobj_bounds.json (this module's cache)   source "cache"
  3. the vanilla dump %TEMP%\\pltar\\dump.zip, in memory       source "dump"
  4. unknown: a 0.5 m cube and a "?" label                     source "unknown" (printed)
Rebuild / extend the cache (Blender's Python; reads the LZMA zip):
    blender --background --factory-startup --python cs2_subobj.py -- --build-bounds
    blender --background --factory-startup --python cs2_subobj.py -- --prop FenceResidentialPieceLowRandom01 ...

Side effects: data API only (no operator, no selection, nothing linked to a scene). Every datablock
created here lives in a pack the caller removes again (cs2_review's ID-snapshot purge, which now
includes `fonts`).
"""

import json
import math
import os
import re
import sys
import time
import zipfile

import bpy
import mathutils

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)

REPO_ROOT = os.path.normpath(os.path.join(_HERE, "..", ".."))
IMPORT_ROOT = os.path.join(REPO_ROOT, "assets", "import")
BOUNDS_JSON = os.path.join(_HERE, "subobj_bounds.json")
DUMP_ZIP = os.path.join(os.environ.get("TEMP", ""), "pltar", "dump.zip")
BEACON_NAME = "SkiBeaconRedA"
BEACON_MANIFEST = os.path.join(IMPORT_ROOT, "props", "A", BEACON_NAME, f"{BEACON_NAME}.manifest.json")

PACK_PREFIX = "CS2R_"
NOPROPS_ROLE = "SUB_NOPROPS"      # sibling of SUB holding the sub-meshes only (review_live.props)
FALLBACK_HALF = 0.25              # unknown bounds: a 0.5 m cube standing on its position

_GUID = re.compile(r"[0-9a-f]{32}")

# families: first match wins (so "WallLight" is a light, not a wall)
FAMILIES = ("lights", "bins", "seating", "trees", "fences", "other", "mod")
_FAMILY_RULES = (
    ("lights", re.compile(r"light|lamp|lantern|beacon|floodmast", re.I)),
    ("bins", re.compile(r"trash|dumpster|garbage|litter|recycl|wastebin|(?<![a-z])bin(?:random)?\d*$", re.I)),
    ("seating", re.compile(r"bench|chair|table|sofa|seat|stool|picnic|lounger|hammock", re.I)),
    ("trees", re.compile(r"tree|bush|shrub|hedge|plant|flower|conifer|foliage|spruce|birch|palm", re.I)),
    ("fences", re.compile(r"fence|railing|guardrail|barrier|gate|bollard|palisade", re.I)),
)
#: display colors (linear-ish sRGB triples), distinct per family
FAMILY_RGB = {
    "lights": (1.00, 0.80, 0.15),     # warm yellow
    "bins": (0.20, 0.85, 0.35),       # green
    "seating": (0.20, 0.55, 1.00),    # blue
    "trees": (0.05, 0.55, 0.10),      # dark green
    "fences": (0.80, 0.50, 0.20),     # brown / orange
    "other": (0.90, 0.25, 0.85),      # magenta
    "mod": (0.05, 0.95, 0.95),        # cyan: OUR props by CID (kit / sign set), fine-detail pass
}
STANDIN_ALPHA = 0.35
RECOLOR_ALBEDO = 0.80                # cs2_asset.RECOLOR_ALBEDO: the kit accent texels under the ControlMask


def colour_set_rgb(key):
    """The review tint of a kit color set (fine_detail.COLOUR_SETS): its slot-2 VariationSet value x the 0.80
    accent albedo = what the gear accent / akja shell show (linear; the in-game color space is unverified)."""
    import fine_detail as FD
    return tuple(round(float(v) * RECOLOR_ALBEDO, 4) for v in FD.COLOUR_SETS[key].rgb)
LABEL_SIZE = 0.30                    # m (small packs; larger packs scale it, see label_size)
LABEL_REL = 0.016                    # label size per meter of the pack's LOD0 bounding-box diagonal
LABEL_MAX = 0.60                     # m
LABEL_FACES = 4                      # one text object per 90 degrees of azimuth: whichever way Richard
#                                    # orbits, a face within 45 degrees of the camera reads un-mirrored
#                                    # (the material hides each label's back; face 0 = the three_quarter view)
LABEL_LIFT = 0.10                    # m above the box top
#: labels face the live review's default three_quarter camera (review_live.DEFAULT_VIEWS[0])
LABEL_FACING = (1.0, -1.2)


# --------------------------------------------------------------------------------------
# Frames
# --------------------------------------------------------------------------------------

def game_to_pack(p):
    """Prefab-local (gx, gy, gz) -> the net-oriented pack frame (bx, by, bz) = (-gx, -gz, gy) (gx NEGATED:
    the game negates FBX x on import; x-sign gate N)."""
    return (-float(p[0]) + 0.0, -float(p[2]), float(p[1]))


def pack_to_game(p):
    return (-float(p[0]) + 0.0, float(p[2]), -float(p[1]))


def quat_matrix(q):
    """3x3 rotation (row-major tuples) of a unit quaternion (x, y, z, w); for a pure yaw about +Y
    this is cs2_liftdress.subobj_box's x' = x cos + z sin, z' = -x sin + z cos."""
    x, y, z, w = (float(v) for v in q)
    n = math.sqrt(x * x + y * y + z * z + w * w) or 1.0
    x, y, z, w = x / n, y / n, z / n, w / n
    return ((1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)),
            (2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)),
            (2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)))


def box_corners_pack(bounds, pos, rot):
    """The 8 corners of a prop's local bounds placed at `pos` / `rot` (game local), in the pack
    frame. Index bits: x = 4, y = 2, z = 1 of the (lo, hi) choice."""
    lo, hi = bounds
    R = quat_matrix(rot)
    out = []
    for ix in (0, 1):
        for iy in (0, 1):
            for iz in (0, 1):
                c = (hi[0] if ix else lo[0], hi[1] if iy else lo[1], hi[2] if iz else lo[2])
                g = tuple(sum(R[r][k] * c[k] for k in range(3)) + float(pos[r]) for r in range(3))
                out.append(game_to_pack(g))
    return out


def aabb(points):
    lo = [min(p[i] for p in points) for i in range(3)]
    hi = [max(p[i] for p in points) for i in range(3)]
    return lo, hi


# --------------------------------------------------------------------------------------
# Families / labels
# --------------------------------------------------------------------------------------

def family_of(prefab):
    for fam, rx in _FAMILY_RULES:
        if rx.search(str(prefab or "")):
            return fam
    return "other"


def short_name(prefab, guid=None):
    s = re.sub(r"Random\d*$", "", str(prefab or ""))
    if not s:
        return (guid or "?")[:8]
    return s


# --------------------------------------------------------------------------------------
# Reader
# --------------------------------------------------------------------------------------

def _guid(v):
    if v is None:
        return None
    m = _GUID.search(str(v).lower())
    return m.group(0) if m else None


def _vec(v, n):
    if v is None or len(v) < n:
        return None
    return tuple(float(x) for x in v[:n])


def _norm_row(e, source, index):
    """One manifest object (any of the shipped spellings) -> the unified row, or None."""
    if not isinstance(e, dict):
        return None
    prefab = e.get("prefab") or e.get("name")
    guid = _guid(e.get("m_Object", e.get("guid")))
    pos = _vec(e.get("position", e.get("pos", e.get("position_net_m"))), 3)
    rot = _vec(e.get("rotation", e.get("rot", e.get("rotation_xyzw"))), 4) or (0.0, 0.0, 0.0, 1.0)
    parent = e.get("m_ParentMesh", e.get("parent", e.get("parent_mesh", 0)))
    if pos is None or (prefab is None and guid is None):
        return None
    if e.get("kind") == "mod":
        # our own prop by object-prefab CID (fine_detail.py): never a vanilla GUID lookup
        row = {"prefab": str(prefab) if prefab else None, "guid": None, "cid": guid, "kind": "mod",
               "role": e.get("role"), "mesh": e.get("mesh"), "pos": pos, "rot": rot,
               "parent": int(parent if parent is not None else 0), "mount": e.get("mount"),
               "source": source, "index": int(index)}
        if e.get("colour_set"):
            # kit color variants (2026-10-12): the stand-in is tinted with the set, labeled by the variant
            row["colour_set"] = str(e["colour_set"])
            row["variant"] = str(e.get("variant") or prefab)
        b = e.get("bounds")
        if isinstance(b, (list, tuple)) and len(b) == 2:
            row["own_bounds"] = (tuple(float(v) for v in b[0]), tuple(float(v) for v in b[1]))
        return row
    return {"prefab": str(prefab) if prefab else None, "guid": guid, "pos": pos, "rot": rot,
            "parent": int(parent if parent is not None else 0), "mount": e.get("mount") or e.get("host"),
            "source": source, "index": int(index)}


def _rows_of_block(block, source):
    """A `sub_objects` block: {objects: [...]} | {rows: [...], block: {objects: [...]}} | list."""
    if isinstance(block, list):
        objs, extra = block, []
    elif isinstance(block, dict):
        inner = block.get("block")
        if isinstance(inner, dict) and inner.get("objects"):
            objs, extra = inner["objects"], block.get("rows") or []
        elif block.get("objects"):
            objs, extra = block["objects"], []
        else:
            objs, extra = block.get("rows") or [], []
    else:
        return []
    out = []
    for i, e in enumerate(objs):
        r = _norm_row(e, source, i)
        if r is None:
            continue
        if i < len(extra) and isinstance(extra[i], dict) and extra[i].get("prefab") == e.get("prefab"):
            r["mount"] = r["mount"] or extra[i].get("mount")
        out.append(r)
    return out


def rows_from_manifest(manifest):
    """All sub-object rows of one manifest dict (see the module docstring for the shapes)."""
    if not isinstance(manifest, dict):
        return []
    rows = []
    for path in (("sub_objects",), ("detail1", "sub_objects"), ("rev1", "sub_objects"),
                 ("detail", "sub_objects")):
        cur = manifest
        for k in path:
            cur = cur.get(k) if isinstance(cur, dict) else None
        if cur:
            got = _rows_of_block(cur, ".".join(path))
            if got:
                rows.extend(got)
                break                      # the same block is never mirrored twice
    night = manifest.get("night_subobjects")
    if isinstance(night, list) and night:
        rows.extend(r for r in (_norm_row(e, "night_subobjects", i) for i, e in enumerate(night))
                    if r is not None)
    return rows


_BEACON_CACHE = {}


def _beacon_manifest():
    try:
        mt = os.path.getmtime(BEACON_MANIFEST)
    except OSError:
        return None
    hit = _BEACON_CACHE.get(BEACON_MANIFEST)
    if hit and hit[0] == mt:
        return hit[1]
    try:
        with open(BEACON_MANIFEST, "r", encoding="utf-8") as fh:
            data = json.load(fh)
    except (OSError, ValueError):
        return None
    _BEACON_CACHE[BEACON_MANIFEST] = (mt, data)
    return data


def beacon_rows(name):
    """The beacon addendum row(s) hosted on mesh asset `name` (props/A/SkiBeaconRedA hosting.rows)."""
    bm = _beacon_manifest()
    if not bm or not isinstance(bm.get("hosting"), dict):
        return []
    hosting = bm["hosting"]
    b = bm.get("bounds_m") or {}
    own = (tuple(b.get("min", (-0.13, 0.0, -0.13))), tuple(b.get("max", (0.13, 0.45, 0.13))))
    out = []
    for i, r in enumerate(hosting.get("rows") or []):
        if r.get("mesh") != name or r.get("anchor") is None:
            continue
        out.append({"prefab": bm.get("object_prefab") or "SkiBeaconRed", "guid": None,
                    "pos": _vec(r["anchor"], 3), "rot": _vec(hosting.get("rotation_xyzw"), 4) or (0, 0, 0, 1),
                    "parent": int(hosting.get("m_ParentMesh", 0)), "mount": r.get("host"),
                    "source": "beacon", "index": i, "own_bounds": own})
    return out


def _load_manifest(asset_dir, name):
    path = os.path.join(asset_dir, f"{name}.manifest.json")
    if not os.path.isfile(path):
        return None, None
    try:
        with open(path, "r", encoding="utf-8") as fh:
            return json.load(fh), path
    except (OSError, ValueError) as exc:
        print(f"[subobj] WARN {path}: unreadable ({exc})")
        return None, path


def collect_rows(name, asset_dir, manifest=None):
    """
    (rows, info) for pack `name`. manifest: None = read <asset_dir>/<name>.manifest.json (the
    generator's own, or the shipped one for write_standin_pack); a dict = that manifest (or just
    {"sub_objects": block}); {} = explicitly none; a str = a manifest path. The beacon addendum
    rows for `name` are always added; a night_subobjects WarningLight at the same spot is dropped.
    """
    src = "arg"
    if isinstance(manifest, str):
        src = manifest
        try:
            with open(manifest, "r", encoding="utf-8") as fh:
                manifest = json.load(fh)
        except (OSError, ValueError) as exc:
            print(f"[subobj] WARN {src}: unreadable ({exc})")
            manifest = {}
    elif manifest is None:
        manifest, path = _load_manifest(asset_dir, name)
        src = path or "none"
    rows = rows_from_manifest(manifest or {})
    replaced = []
    for b in beacon_rows(name):
        keep = []
        for r in rows:
            if (r["source"] == "night_subobjects" and str(r["prefab"]).startswith("WarningLight")
                    and max(abs(r["pos"][i] - b["pos"][i]) for i in range(3)) < 0.1):
                replaced.append(f"{r['prefab']} -> {b['prefab']} (towers-transfer addendum B1)")
                continue
            keep.append(r)
        rows = keep + [b]
    return rows, {"source": src, "replaced": replaced}


# --------------------------------------------------------------------------------------
# Bounds
# --------------------------------------------------------------------------------------

_CACHE = {"json": None, "mtime": None, "dump": None, "table": None}


def _liftdress_table():
    if _CACHE.get("table") is not None:
        return _CACHE["table"]
    try:
        import cs2_liftdress
        _CACHE["table"] = cs2_liftdress.SUBOBJ
    except Exception as exc:  # noqa: BLE001 - another fleet may be editing it; the cache covers it
        print(f"[subobj] note: cs2_liftdress.SUBOBJ unavailable ({type(exc).__name__}: {exc}); "
              f"using the JSON cache")
        _CACHE["table"] = {}
    return _CACHE["table"]


def _json_cache():
    try:
        mt = os.path.getmtime(BOUNDS_JSON)
    except OSError:
        return {}
    if _CACHE["json"] is not None and _CACHE["mtime"] == mt:
        return _CACHE["json"]
    try:
        with open(BOUNDS_JSON, "r", encoding="utf-8") as fh:
            data = json.load(fh).get("props", {})
    except (OSError, ValueError) as exc:
        print(f"[subobj] WARN {BOUNDS_JSON}: unreadable ({exc})")
        data = {}
    _CACHE["json"], _CACHE["mtime"] = data, mt
    return data


class DumpIndex:
    """The vanilla prefab dump (LZMA zip), indexed the way cs2_liftdress.verify_subobj reads it."""
    _PH = re.compile(r'"m_Placeholders"\s*:\s*\{.*?"\$rcontent"\s*:\s*\[(.*?)\]', re.S)
    _BOUNDS = re.compile(r'"m_Bounds".*?"min".*?"x":\s*([-0-9.Ee]+),\s*"y":\s*([-0-9.Ee]+),\s*'
                         r'"z":\s*([-0-9.Ee]+).*?"max".*?"x":\s*([-0-9.Ee]+),\s*"y":\s*'
                         r'([-0-9.Ee]+),\s*"z":\s*([-0-9.Ee]+)', re.S)
    _MESH_POS = re.compile(r'"m_Position"\s*:\s*\{[^}]*?"x":\s*([-0-9.Ee]+),\s*"y":\s*([-0-9.Ee]+),'
                           r'\s*"z":\s*([-0-9.Ee]+)')

    def __init__(self, path=DUMP_ZIP):
        self.z = zipfile.ZipFile(path)
        names = self.z.namelist()
        self.static = {n.split("/")[2]: n for n in names
                       if n.startswith("00_BaseGame/Game.Prefabs.StaticObjectPrefab/") and n.endswith(".Prefab")}
        self.render = {n.split("/")[2]: n for n in names
                       if n.startswith("00_BaseGame/Game.Prefabs.RenderPrefab/") and n.endswith(".Prefab")}
        self.gtov = {}
        for k in sorted(self.static):
            m = self._PH.search(self.text(self.static[k]))
            if m:
                for g in re.findall(r"UnityGUID:([0-9a-f]{32})", m.group(1)):
                    self.gtov.setdefault(g, []).append(k)

    def text(self, member):
        return self.z.read(member).decode("utf-8", "replace")

    def guid_of(self, placeholder):
        """The placeholder's GUID, inferred from its variants (the prefab file carries none):
        variants named <stem><NN> that list one common GUID."""
        stem = re.sub(r"Random\d*$", "", placeholder)
        cands = {}
        for g, vs in self.gtov.items():
            if all(re.fullmatch(re.escape(stem) + r"\d+", v) for v in vs) and vs:
                cands[g] = vs
        if len(cands) == 1:
            return next(iter(cands))
        return None

    def derive(self, prefab, guid):
        vs = tuple(sorted(self.gtov.get(guid, [])))
        lo = hi = None
        tris = 0
        meshes = []
        offsets = []
        for rn in ((prefab,) if prefab else ()) + vs:
            key = rn + " Mesh"
            if key not in self.render:
                continue
            t = self.text(self.render[key])
            bm = self._BOUNDS.search(t)
            if bm is None:
                continue
            b = [float(x) for x in bm.groups()]
            ic = re.search(r'"m_IndexCount":\s*(\d+)', t)
            lo = b[:3] if lo is None else [min(a, c) for a, c in zip(lo, b[:3])]
            hi = b[3:] if hi is None else [max(a, c) for a, c in zip(hi, b[3:])]
            tris = max(tris, int(ic.group(1)) // 3 if ic else 0)
            meshes.append(key)
        for v in vs:                                    # ObjectMeshInfo.m_Position offsets (flag only)
            st = self.text(self.static[v]) if v in self.static else ""
            seg = st[st.find('"m_Meshes"'):] if '"m_Meshes"' in st else ""
            for m in self._MESH_POS.finditer(seg):
                if any(abs(float(c)) > 1e-6 for c in m.groups()):
                    offsets.append(v)
                    break
        if lo is None:
            return None
        return {"prefab": prefab, "variants": list(vs),
                "bounds": [[round(v, 3) + 0.0 for v in lo], [round(v, 3) + 0.0 for v in hi]],
                "tris": tris, "meshes": meshes, "mesh_offset_variants": offsets, "source": "dump"}


def _dump():
    if _CACHE["dump"] is None:
        _CACHE["dump"] = DumpIndex() if os.path.isfile(DUMP_ZIP) else False
    return _CACHE["dump"] or None


def lookup_bounds(prefab, guid=None, own_bounds=None):
    """((lo, hi), source) of a prop's union bounds, game local (see the module docstring)."""
    if own_bounds is not None:
        return (tuple(own_bounds[0]), tuple(own_bounds[1])), "own"
    table = _liftdress_table()
    hit = next((v for v in table.values() if guid and v.get("guid") == guid), None)
    if hit is None and not guid:
        hit = table.get(prefab)
    if hit is not None:
        return (tuple(hit["bounds"][0]), tuple(hit["bounds"][1])), "SUBOBJ"
    cache = _json_cache()
    ent = cache.get(guid) if guid else None
    if ent is None and prefab:
        ent = next((e for e in cache.values() if prefab in (e.get("prefabs") or [e.get("prefab")])), None)
    if ent is not None:
        return (tuple(ent["bounds"][0]), tuple(ent["bounds"][1])), "cache"
    idx = _dump()
    if idx is not None:
        g = guid or (idx.guid_of(prefab) if prefab else None)
        got = idx.derive(prefab, g) if g else None
        if got is not None:
            return (tuple(got["bounds"][0]), tuple(got["bounds"][1])), "dump"
    h = FALLBACK_HALF
    return ((-h, 0.0, -h), (h, 2 * h, h)), "unknown"


# --------------------------------------------------------------------------------------
# Stand-in objects (data API only)
# --------------------------------------------------------------------------------------

#: outward-facing quads over the corner index bits (x = 4, y = 2, z = 1); flipped per box if needed
_QUADS = ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3))


def _box_mesh(name, corners):
    centre = [sum(c[i] for c in corners) / 8.0 for i in range(3)]
    faces = []
    for q in _QUADS:
        pts = [mathutils.Vector(corners[i]) for i in q]
        n = (pts[1] - pts[0]).cross(pts[2] - pts[0])
        mid = sum(pts, mathutils.Vector()) / 4.0 - mathutils.Vector(centre)
        faces.append(tuple(reversed(q)) if n.dot(mid) < 0 else q)
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(c) for c in corners], [], faces)
    me.update()
    me.validate()
    return me


def _fresh_tree(mat):
    if mat.node_tree is None:
        mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    out.location = (400, 0)
    return nt, out


def _box_material(name, rgb):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = tuple(rgb) + (STANDIN_ALPHA,)                 # solid-mode viewport color
    nt, out = _fresh_tree(mat)
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Base Color"].default_value = tuple(rgb) + (1.0,)
    bsdf.inputs["Alpha"].default_value = STANDIN_ALPHA
    bsdf.inputs["Roughness"].default_value = 0.6
    if "Emission Color" in bsdf.inputs:
        bsdf.inputs["Emission Color"].default_value = tuple(rgb) + (1.0,)
        bsdf.inputs["Emission Strength"].default_value = 0.6
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    for attr, val in (("surface_render_method", "BLENDED"), ("use_backface_culling", False),
                      ("show_transparent_back", False), ("blend_method", "BLEND")):
        if hasattr(mat, attr):
            try:
                setattr(mat, attr, val)
            except (TypeError, ValueError):
                pass
    return mat


def _label_material(name, rgb):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = tuple(rgb) + (1.0,)
    nt, out = _fresh_tree(mat)
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Color"].default_value = tuple(rgb) + (1.0,)
    em.inputs["Strength"].default_value = 3.0
    tr = nt.nodes.new("ShaderNodeBsdfTransparent")
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(geo.outputs["Backfacing"], mix.inputs[0])             # back side -> transparent
    nt.links.new(em.outputs["Emission"], mix.inputs[1])
    nt.links.new(tr.outputs["BSDF"], mix.inputs[2])
    nt.links.new(mix.outputs["Shader"], out.inputs["Surface"])
    if hasattr(mat, "use_backface_culling"):
        mat.use_backface_culling = True
    return mat


def label_size(ref_size=None):
    """Label height in m: LABEL_SIZE for small packs, LABEL_REL x the LOD0 bbox diagonal up to LABEL_MAX."""
    if not ref_size:
        return LABEL_SIZE
    return min(LABEL_MAX, max(LABEL_SIZE, LABEL_REL * float(ref_size)))


def _label_object(name, text, anchor, rgb_mat, size=LABEL_SIZE, face=0):
    cu = bpy.data.curves.new(name, "FONT")
    cu.body = text
    cu.size = size
    cu.align_x = "CENTER"
    cu.align_y = "BOTTOM"
    cu.extrude = 0.0
    ob = bpy.data.objects.new(name, cu)
    ob.location = anchor
    ob.rotation_euler = (math.pi / 2.0, 0.0,
                         math.atan2(LABEL_FACING[0], -LABEL_FACING[1]) + face * (2.0 * math.pi / LABEL_FACES))
    cu.materials.append(rgb_mat)
    ob.hide_render = False
    return ob


def add_standins(name, rows, root, sub_coll, info=None, ref_size=None):
    """
    Build one box + its label (LABEL_FACES text objects, parented to the box) per row into `sub_coll` (the pack's SUB collection) and, when there is
    at least one, the sibling collection SUB_NOPROPS (root's child) that holds only what SUB held
    before (review_live.props swaps the SUB instance between the two). Returns a summary dict:
    {n, families, unknown, sources, objects [names], rows [json-able], label_size, error?}. ref_size = the
    pack's LOD0 bounding-box diagonal in m (labels scale with it).
    """
    info = info or {}
    summary = {"n": 0, "families": {}, "unknown": 0, "sources": {}, "objects": [], "rows": [],
               "replaced": info.get("replaced") or [], "manifest": info.get("source")}
    if not rows:
        return summary
    # role "signset" rows: no box (the real sign-set mesh is in the pack, cs2_review extra_meshes);
    # counted and listed, so a pack without the mesh still reports the row
    sets = [r for r in rows if r.get("kind") == "mod" and r.get("role") == "signset"]
    if sets:
        summary["signset_rows"] = [{"prefab": r["prefab"], "cid": r.get("cid"), "mesh": r.get("mesh")}
                                   for r in sets]
        rows = [r for r in rows if r not in sets]
        if not rows:
            return summary
    before = list(sub_coll.objects)
    lsize = label_size(ref_size)
    mats = {}
    made = []
    for i, r in enumerate(rows):
        prefab, guid = r.get("prefab"), r.get("guid")
        mod = r.get("kind") == "mod"
        fam = "mod" if mod else family_of(prefab)
        cset = r.get("colour_set") if mod else None
        mkey = f"{fam}_{cset}" if cset else fam               # kit color variants: one material per set
        bounds, bsrc = lookup_bounds(prefab, guid, r.get("own_bounds"))
        if bsrc == "unknown":
            summary["unknown"] += 1
            print(f"[subobj] WARN {name}: no bounds for {prefab} {guid} - drawn as a 0.5 m cube "
                  f"(run cs2_subobj.py -- --build-bounds)")
        corners = box_corners_pack(bounds, r["pos"], r["rot"])
        tag = f"{PACK_PREFIX}{name}_prop{i:02d}"
        ob = bpy.data.objects.new(tag, _box_mesh(tag, corners))
        if mkey not in mats:
            rgb = colour_set_rgb(cset) if cset else FAMILY_RGB[fam]
            mats[mkey] = (_box_material(f"{PACK_PREFIX}{name}_Prop_{mkey}", rgb),
                          _label_material(f"{PACK_PREFIX}{name}_PropText_{mkey}", rgb))
        ob.data.materials.append(mats[mkey][0])
        ob.show_wire = True
        ob.show_in_front = False
        ob.hide_render = False
        text = (str(r.get("variant") or prefab) if mod else short_name(prefab, guid)) + \
            (" ?" if bsrc == "unknown" else "") + (" (ground)" if r["parent"] == -1 else "")
        lo, hi = aabb(corners)
        anchor = ((lo[0] + hi[0]) / 2.0, (lo[1] + hi[1]) / 2.0, hi[2] + LABEL_LIFT)
        labs = []
        for k in range(LABEL_FACES):
            lab = _label_object(tag + ("_label" if k == 0 else f"_label{k}"), text, anchor, mats[mkey][1],
                                lsize, k)
            lab.parent = ob                        # the box sits at the origin: local = world
            labs.append(lab)
        for o, kind in [(ob, "box")] + [(lb, "label") for lb in labs]:
            o["cs2r_role"] = "SUB"
            o["cs2r_prop"] = kind
            o["cs2r_prop_index"] = i
            o["cs2r_prefab"] = str(prefab or "")
            o["cs2r_guid"] = guid or ""
            o["cs2r_family"] = fam
            o["cs2r_parent_mesh"] = int(r["parent"])
            o["cs2r_pos_game"] = [float(v) for v in r["pos"]]
            o["cs2r_rot_game"] = [float(v) for v in r["rot"]]
            o["cs2r_bounds_lo"] = [float(v) for v in bounds[0]]
            o["cs2r_bounds_hi"] = [float(v) for v in bounds[1]]
            o["cs2r_bounds_src"] = bsrc
            o["cs2r_row_source"] = str(r["source"])
            o["cs2r_label"] = text
            o["cs2r_label_size_m"] = float(lsize)
            o["cs2r_own_prop"] = int(r.get("own_bounds") is not None or mod)
            if mod:
                o["cs2r_kind"] = "mod"
                o["cs2r_cid"] = str(r.get("cid") or "")
                o["cs2r_mesh"] = str(r.get("mesh") or "")
                if cset:
                    o["cs2r_colour_set"] = str(cset)
                    o["cs2r_variant"] = str(r.get("variant") or "")
            if r.get("mount"):
                o["cs2r_mount"] = str(r["mount"])
            sub_coll.objects.link(o)
        for k, lb in enumerate(labs):
            lb["cs2r_label_face"] = k
        made.append(ob)
        made.extend(labs)
        summary["families"][fam] = summary["families"].get(fam, 0) + 1
        summary["sources"][bsrc] = summary["sources"].get(bsrc, 0) + 1
        summary["rows"].append({"i": i, "prefab": prefab, "guid": guid, "family": fam,
                                "kind": "mod" if mod else "vanilla", "cid": r.get("cid"),
                                **({"colour_set": cset, "variant": r.get("variant")} if cset else {}),
                                "pos": [round(v, 4) for v in r["pos"]],
                                "rot": [round(v, 7) for v in r["rot"]], "parent": r["parent"],
                                "mount": r.get("mount"), "bounds": [list(bounds[0]), list(bounds[1])],
                                "bounds_src": bsrc, "row_source": r["source"]})
    summary["n"] = len(rows)
    summary["label_size"] = lsize
    summary["objects"] = [o.name for o in made]
    noprops = bpy.data.collections.new(NOPROPS_ROLE)
    noprops["cs2r_role"] = NOPROPS_ROLE
    for o in before:
        noprops.objects.link(o)
    root.children.link(noprops)
    return summary


# --------------------------------------------------------------------------------------
# Bounds cache builder (CLI: blender --background --python cs2_subobj.py -- --build-bounds)
# --------------------------------------------------------------------------------------

def _all_manifest_rows():
    """(name, row) over every shipped manifest, plus SUBOBJ, for the cache builder."""
    out = []
    for dp, _dn, fns in os.walk(IMPORT_ROOT):
        for fn in sorted(fns):
            if not fn.endswith(".manifest.json"):
                continue
            try:
                with open(os.path.join(dp, fn), "r", encoding="utf-8") as fh:
                    m = json.load(fh)
            except (OSError, ValueError):
                continue
            name = fn[:-len(".manifest.json")]
            for r in rows_from_manifest(m):
                out.append((name, r))
    return out


def build_bounds_cache(path=BOUNDS_JSON, extra_names=()):
    idx = DumpIndex()
    targets = {}                                  # guid -> {prefabs}
    for _name, r in _all_manifest_rows():
        if r["guid"]:
            targets.setdefault(r["guid"], set()).add(r["prefab"])
    table = _liftdress_table()
    for pf, s in table.items():
        targets.setdefault(s["guid"], set()).add(pf)
    for pf in extra_names:
        g = idx.guid_of(pf)
        if g is None:
            print(f"[subobj] WARN {pf}: no unique GUID inferred from its variants")
            continue
        targets.setdefault(g, set()).add(pf)
    old = _json_cache()
    props = dict(old)
    for g, prefabs in sorted(targets.items()):
        prefabs = sorted(p for p in prefabs if p)
        got = idx.derive(prefabs[0] if prefabs else None, g)
        if got is None:
            print(f"[subobj] WARN {g} {prefabs}: no mesh bounds in the dump")
            continue
        got["prefabs"] = prefabs
        got["guid"] = g
        props[g] = got
    data = {"_meta": {"what": "union of the placeholder mesh and every variant mesh (RenderPrefab "
                              "m_Bounds, game local, y up) per placeholder GUID, as "
                              "cs2_liftdress.verify_subobj reads them",
                      "dump": "%TEMP%/pltar/dump.zip (base game dump)",
                      "built": time.strftime("%Y-%m-%d"),
                      "rebuild": "blender --background --factory-startup --python cs2_subobj.py "
                                 "-- --build-bounds [--prop <PlaceholderName> ...]"},
            "props": {g: props[g] for g in sorted(props)}}
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(data, fh, indent=1, sort_keys=True)
        fh.write("\n")
    _CACHE["json"] = None
    print(f"[subobj] bounds cache: {len(props)} props -> {path}")
    return data


def _main(argv):
    import argparse
    ap = argparse.ArgumentParser(prog="cs2_subobj.py", description="vanilla sub-object bounds cache")
    ap.add_argument("--build-bounds", action="store_true",
                    help="(re)build subobj_bounds.json from every shipped manifest + SUBOBJ")
    ap.add_argument("--prop", nargs="*", default=[], metavar="NAME",
                    help="also add these placeholder names (GUID inferred from their variants)")
    ap.add_argument("--out", default=BOUNDS_JSON)
    args = ap.parse_args(argv)
    if not (args.build_bounds or args.prop):
        ap.error("nothing to do: pass --build-bounds and/or --prop NAME ...")
    build_bounds_cache(args.out, args.prop)


if __name__ == "__main__":
    try:
        _main(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001
        import traceback
        traceback.print_exc()
        sys.exit(1)
