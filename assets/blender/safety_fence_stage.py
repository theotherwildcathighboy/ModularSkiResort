"""
safety_fence_stage.py - prefab JSON staging + S1 surgery for the orange safety-net fence
(design: docs/research/art/assets-2026-10/design/safety-fence.md; build report:
docs/research/art/assets-2026-10/build/safety-fence.md). Pure Python (no bpy): runs under
Blender's bundled interpreter (`python.exe -I safety_fence_stage.py ...`) or is imported by
safety_fence.py / tests/test_safety_fence.py.

WHAT IT STAGES (assets/staging/prefabs/fences/A/, idempotent; a .cid is minted ONCE and never
re-minted, 32 lowercase hex, no newline, no BOM; .Prefab files UTF-8 without BOM, CRLF like the
game-side files)

    SkiSafetyFence/            NetFencePrefab  (clone of 1.6.2 FenceIndustrialHigh01, design section 2)
    SkiSafetyFenceSection/     NetSectionPrefab
    SkiSafetyFencePiece/       NetPiecePrefab  (geometry-less Surface-layer piece, lane at 0,0,0)
    SkiSafetyFenceLane/        NetLaneGeometryPrefab (UtilityLane type 16 = Fence) -> SkiSafetyNetA Mesh
                               + the mesh-prefab surgery fragments of SkiSafetyNetA / _LOD1
    SkiNetPost/                StaticObjectPrefab + UtilityObject 16 (the optional dead-end post)
                               + the SkiNetPostA Mesh ColorProperties fragment
    _probe/SkiSafetyFenceProbe{,Section,Piece}/   step 0 of the design: our net on the VANILLA
                               chain-link lane (UnityGUID 9a89f6ce...), no import needed
    SkiSafetyFence/SkiSafetyFence_en-US.loc (+ .cid)   OPTIONAL display name (format proven by
                               re-writing SkiRunGroomedA_en-US.loc byte for byte, see prove_loc)
    safety-fence-staging.json  the record (names, cids, placeholders, GUIDs and their sources)

Every $type table is written by one emitter (`emit`): ids are declared at first use in document
order (monotonic, no forward reference), $ids are pre-order. Placeholders for not-yet-imported
meshes follow the sign-set convention: `$fstrref:"CID:REPLACE_<Mesh>_MESH_CID"`.

SURGERY (`surgery(imported_data, apply=False, ...)`, game CLOSED; dry run unless apply=True)
    probe=True   install the three probe prefabs only (no import needed).
    default      after the import of assets/import/fences/A:
      1 find "SkiSafetyNetA Mesh_*", "SkiSafetyNetA_LOD1 Mesh_*", "SkiNetPostA Mesh_*" under
        ImportedData/c030ee3ff256df91 (exactly one each, else STOP);
      2 TEMPLATE GATE: the .Surface each net mesh prefab references must carry template hash 34
        (CurvedHashedCutout) in bytes 2-5 (SurfaceAsset.cs:421-425 1.6.2f1-colossal); 0 / 1 =
        the settings.json Surface block did not apply -> STOP;
      3 LOD GATE: the LOD0 mesh prefab's LodProperties lists the LOD1 mesh CID;
      4 append CurveProperties + ColorProperties (LOD0), CurveProperties (LOD1), ColorProperties
        (SkiNetPostA Mesh), renumbering the whole $type table to first-use order;
      5 write the five staged prefabs into ImportedData/<Name>/ with the mesh CIDs filled in
        (end_posts=True also sets UtilityLane.m_NodeObject = SkiNetPost and installs it);
      6 re-check every written file (the validate_prefab.ps1 rules, ported: check_prefab_text).
    Backups of every touched game file go to %TEMP%/safety-fence-surgery-<stamp>/ first.

    simulate_import(scratch) fabricates the importer's output (mesh prefabs, .cid files and
    .Surface headers) in a SCRATCH copy of ImportedData only (it refuses any folder without the
    SCRATCH-COPY.txt marker), so the surgery, validate_prefab.ps1 and preflight.ps1 can be run
    end to end before the real import.

CLI
    python -I safety_fence_stage.py stage
    python -I safety_fence_stage.py surgery --imported-data <dir> [--apply] [--end-posts] [--loc] [--probe]
    python -I safety_fence_stage.py simulate-import --imported-data <scratch dir>
    python -I safety_fence_stage.py prove-loc
"""

import argparse
import glob
import json
import os
import re
import shutil
import struct
import sys
import time
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
STAGING = os.path.join(REPO, "assets", "staging", "prefabs", "fences", "A")
IMPORT_ROOT = os.path.join(REPO, "assets", "import", "fences", "A")
MIRROR = os.path.join(REPO, "gamedata-mirror", "ImportedData")
GAME_IMPORTED_DATA = os.path.join(os.environ.get("USERPROFILE", ""), "AppData", "LocalLow",
                                  "Colossal Order", "Cities Skylines II", "ImportedData")
LIBRARY = "c030ee3ff256df91"          # the Editor's import seed folder (PIPELINE section 5)
SCRATCH_MARKER = "SCRATCH-COPY.txt"

# --------------------------------------------------------------------------------------
# Names
# --------------------------------------------------------------------------------------

NET = "SkiSafetyFence"
SECTION = "SkiSafetyFenceSection"
PIECE = "SkiSafetyFencePiece"
LANE = "SkiSafetyFenceLane"
POST = "SkiNetPost"
PROBE_NET = "SkiSafetyFenceProbe"
PROBE_SECTION = "SkiSafetyFenceProbeSection"
PROBE_PIECE = "SkiSafetyFenceProbePiece"
MESH = "SkiSafetyNetA"                # imported -> "SkiSafetyNetA Mesh", "SkiSafetyNetA_LOD1 Mesh"
POST_MESH = "SkiNetPostA"             # imported -> "SkiNetPostA Mesh"
MAIN_PREFABS = (NET, SECTION, PIECE, LANE, POST)
PROBE_PREFABS = (PROBE_NET, PROBE_SECTION, PROBE_PIECE)
PLACEHOLDER = "REPLACE_{}_MESH_CID"

# --------------------------------------------------------------------------------------
# Vanilla references (UnityGUID) and where each value comes from
# --------------------------------------------------------------------------------------

GUID = {
    "pathways_group": ("05564431524791c48846dbd183fd232d",
                       "Landscaping > Pathways UI category: vanilla Pavement Path UIObject.m_Group "
                       "(dump 1.6.0, PathwayPrefab/Pavement Path)"),
    "fences_group": ("71df37e0fc45ade46a64fba0eedd7c54",
                     "1.6.2 'Fences' UIAssetCategoryPrefab (m_Menu null): FenceIndustrialHigh01 "
                     "UIObject.m_Group, read from resources.assets via PrefabsMap "
                     "(design section 1.2, safety-fence-vanilla-read.py)"),
    "landscaping_service": ("5930db6811522d34495a9e16c4fd4c18",
                            "Landscaping service: vanilla Pavement Path ServiceObject (dump) = "
                            "FenceIndustrialHigh01 (1.6.2); preflight whitelist"),
    "surfaces_unlock": ("3e47e765f44f210409edf4e6229fd0df",
                        "the unlock our runs ship (SkiRunGroomedA ...); preflight whitelist"),
    "chainlink_lane": ("9a89f6cea93f7e74995aaac4f9408cba",
                       "vanilla lane FenceIndustrialHigh01 (NetLaneGeometryPrefab), PROBE ONLY "
                       "(design section 6 step 0)"),
}
ICON_PLACEHOLDER = "Media/Game/Icons/Fences/TallIndustrialChainLinkFence.svg"   # PLACEHOLDER icon
FENCE_STATE = 95                      # NetPieceRequirements.Fence (enum line 100 - 5, 1.6.0 = 1.6.2)

# --------------------------------------------------------------------------------------
# Values (design section 2 / 5; the knobs the probe may change are marked KNOB)
# --------------------------------------------------------------------------------------

MAX_SLOPE = 2.0                       # vanilla fences 0.2 (20 %): useless on a ski run
UI_PRIORITY = 1000
PROBE_UI_PRIORITY = 1001
PIECE_WIDTH = 1.0
PIECE_LENGTH = 4.0
PIECE_HEIGHT = (0.0, 1.6)             # vanilla chain link {0, 2.6}; our net + caps reach 1.58
PIECE_LAYER = 0                       # KNOB: 0 Surface (grades a 1 m strip) vs 2 Top (cut only)
NODE_OFFSET = 0.5                     # KNOB
ALLOW_PARALLEL = False                # KNOB (vanilla false; open question 6)
LANE_WIDTH = 0.1
LANE_CAPACITY = 2.0
UTILITY_FENCE = 16                    # UtilityTypes.Fence
TILING_COUNT = 4                      # KNOB: 2 / 4 / 8 all clip on the tile's 0.5 m vertex columns
OVERRIDE_LENGTH = 4.0
PAD_RGB = (1.0, 0.15, 0.02)           # orchestrator 2026-10-13: the checker's redder "tintB" (the photo's net is
                                      # crimson-red; cs2_asset.PAD_RGB 0.85/0.25/0.10 read terracotta); slot 2 (net)
                                      # and slot 3 (posts). One constant: change here, re-run surgery, no re-import.
TEMPLATE_HASH = {0: "Default", 1: "DefaultCutout", 13: "DefaultDoubleSided",
                 14: "DefaultCutoutDoubleSided", 16: "Curved", 30: "CurvedTransparent",
                 31: "CurvedCutout", 34: "CurvedHashedCutout", 35: "DefaultHashedCutout",
                 36: "DefaultHashedCutoutDoubleSided"}
WANT_TEMPLATE = 34

# --------------------------------------------------------------------------------------
# Type names
# --------------------------------------------------------------------------------------

T_LIST = "System.Collections.Generic.List`1[[Game.Prefabs.ComponentBase, Game]], mscorlib"
T_F3 = "Unity.Mathematics.float3, Unity.Mathematics"
T_F4 = "Unity.Mathematics.float4, Unity.Mathematics"
T_B1 = "Colossal.Mathematics.Bounds1, Colossal.Mathematics"
T_B3 = "Colossal.Mathematics.Bounds3, Colossal.Mathematics"
T_REQ = "Game.Prefabs.NetPieceRequirements[], Game"
T_ASSETREF = ("Colossal.IO.AssetDatabase.AssetReference`1[[Colossal.IO.AssetDatabase.SurfaceAsset, "
              "Colossal.IO.AssetDatabase, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null]][], "
              "Colossal.IO.AssetDatabase")


def gp(name):
    return f"Game.Prefabs.{name}, Game"


# --------------------------------------------------------------------------------------
# Emitter: Python value tree -> prefab JSON text with a positional $type table
# --------------------------------------------------------------------------------------

class Obj:
    """A reference-type value: emitted with "$id" and "$type"."""

    def __init__(self, type_name, fields):
        self.type_name, self.fields = type_name, list(fields)


class Struct:
    """A value type (float3, Bounds1, quaternion ...): "$type" only."""

    def __init__(self, type_name, fields):
        self.type_name, self.fields = type_name, list(fields)


class Arr:
    """A serialized array / List: "$id", "$type", "$rlength", "$rcontent"."""

    def __init__(self, type_name, items):
        self.type_name, self.items = type_name, list(items)


class Raw(str):
    """Emitted verbatim (e.g. $fstrref:"CID:...")."""


def fref(kind, value):
    return Raw(f'$fstrref:"{kind}:{value}"')


def f3(x=0, y=0, z=0):
    return Struct(T_F3, [("x", x), ("y", y), ("z", z)])


def _num(v):
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, int):
        return str(v)
    f = float(v)
    if f.is_integer():
        return str(int(f))
    return repr(f)


def emit(root):
    """Prefab text (CRLF, 4-space indent, no trailing newline after the last brace + CRLF)."""
    types = {}
    next_id = [0]
    lines = []

    def tdecl(t):
        if t in types:
            return str(types[t])
        types[t] = len(types)
        return json.dumps(f"{types[t]}|{t}")

    def scalar(v):
        if v is None:
            return "null"
        if isinstance(v, Raw):
            return str(v)
        if isinstance(v, str):
            return json.dumps(v)
        return _num(v)

    def block(v, ind, prefix):
        """Write `prefix` + v; `prefix` is the '"key": ' part (or '' for array items)."""
        pad = " " * ind
        if isinstance(v, (Obj, Struct)):
            lines.append(f"{pad}{prefix}{{")
            inner = []
            if isinstance(v, Obj):
                inner.append(("$id", next_id[0]))
                next_id[0] += 1
            inner.append(("$type", Raw(tdecl(v.type_name))))
            inner.extend(v.fields)
            for i, (k, val) in enumerate(inner):
                block(val, ind + 4, json.dumps(k) + ": ")
                if i < len(inner) - 1:
                    lines[-1] += ","
            lines.append(f"{pad}}}")
        elif isinstance(v, Arr):
            lines.append(f"{pad}{prefix}{{")
            lines.append(f'{pad}    "$id": {next_id[0]},')
            next_id[0] += 1
            lines.append(f'{pad}    "$type": {tdecl(v.type_name)},')
            lines.append(f'{pad}    "$rlength": {len(v.items)},')
            lines.append(f'{pad}    "$rcontent": [')
            for i, item in enumerate(v.items):
                block(item, ind + 8, "")
                if i < len(v.items) - 1:
                    lines[-1] += ","
            lines.append(f"{pad}    ]")
            lines.append(f"{pad}}}")
        else:
            lines.append(f"{pad}{prefix}{scalar(v)}")

    block(root, 0, "")
    return "\r\n".join(lines) + "\r\n"


# --------------------------------------------------------------------------------------
# Prefab builders
# --------------------------------------------------------------------------------------

def _req():
    return Arr(T_REQ, [])


def _component(type_short, name, fields):
    return Obj(gp(type_short), [("name", name), ("active", True)] + list(fields))


def net_prefab(name, section_cid, ui_priority):
    """NetFencePrefab: UIObject (Pathways) + UIObjectOverrideGroup (Fences) + PlaceableNet +
    ServiceObject (Landscaping) + Unlockable (Surfaces); one section; Fence edge / node states."""
    comps = [
        _component("UIObject", "UIObject", [
            ("m_Group", fref("UnityGUID", GUID["pathways_group"][0])),
            ("m_Priority", ui_priority),
            ("m_Icon", ICON_PLACEHOLDER),
            ("m_IsDebugObject", False)]),
        _component("UIObjectOverrideGroup", "UIObjectOverrideGroup", [
            ("m_Group", fref("UnityGUID", GUID["fences_group"][0]))]),
        _component("PlaceableNet", "PlaceableNet", [
            ("m_ElevationRange", Struct(T_B1, [("min", 0), ("max", 0)])),
            ("m_AllowParallelMode", ALLOW_PARALLEL),
            ("m_DisableZoneOverlay", True),
            ("m_UndergroundPrefab", None),
            ("m_XPReward", 0)]),
        _component("ServiceObject", "ServiceObject", [
            ("m_Service", fref("UnityGUID", GUID["landscaping_service"][0]))]),
        _component("Unlockable", "Unlockable", [
            ("m_RequireAll", Arr(gp("PrefabBase[]"), [fref("UnityGUID", GUID["surfaces_unlock"][0])])),
            ("m_RequireAny", Arr(gp("PrefabBase[]"), [])),
            ("m_IgnoreDependencies", False)]),
    ]
    section = Obj(gp("NetSectionInfo"), [
        ("m_Section", fref("CID", section_cid)),
        ("m_RequireAll", _req()), ("m_RequireAny", _req()), ("m_RequireNone", _req()),
        ("m_HiddenLayers", 0), ("m_Invert", False), ("m_Flip", False), ("m_Median", False),
        ("m_HalfLength", False), ("m_Offset", f3())])
    edge_state = Obj(gp("NetEdgeStateInfo"), [
        ("m_RequireAll", _req()), ("m_RequireAny", _req()), ("m_RequireNone", _req()),
        ("m_SetState", Arr(T_REQ, [FENCE_STATE]))])
    node_state = Obj(gp("NetNodeStateInfo"), [
        ("m_RequireAll", _req()), ("m_RequireAny", _req()), ("m_RequireNone", _req()),
        ("m_SetState", Arr(T_REQ, [FENCE_STATE])), ("m_MatchType", 0)])
    return emit(Obj(gp("NetFencePrefab"), [
        ("name", name), ("active", True),
        ("components", Arr(T_LIST, comps)),
        ("m_Sections", Arr(gp("NetSectionInfo[]"), [section])),
        ("m_EdgeStates", Arr(gp("NetEdgeStateInfo[]"), [edge_state])),
        ("m_NodeStates", Arr(gp("NetNodeStateInfo[]"), [node_state])),
        ("m_MaxSlopeSteepness", MAX_SLOPE),
        ("m_AggregateType", None), ("m_StyleType", None), ("m_InvertMode", 0)]))


def section_prefab(name, piece_cid):
    piece = Obj(gp("NetPieceInfo"), [
        ("m_Piece", fref("CID", piece_cid)),
        ("m_RequireAll", _req()), ("m_RequireAny", _req()), ("m_RequireNone", _req()),
        ("m_Offset", f3())])
    return emit(Obj(gp("NetSectionPrefab"), [
        ("name", name), ("active", True),
        ("components", Arr(T_LIST, [])),
        ("m_SubSections", Arr(gp("NetSubSectionInfo[]"), [])),
        ("m_Pieces", Arr(gp("NetPieceInfo[]"), [piece]))]))


def piece_prefab(name, lane_ref):
    """Geometry-less Surface piece carrying one lane at (0, 0, 0) (FenceIndustrialHighPiece01)."""
    lanes = _component("NetPieceLanes", "NetPieceLanes", [
        ("m_Lanes", Arr(gp("NetLaneInfo[]"), [Obj(gp("NetLaneInfo"), [
            ("m_Lane", lane_ref), ("m_Position", f3()), ("m_FindAnchor", False)])]))])
    cost = _component("PlaceableNetPiece", "PlaceableNetPiece", [
        ("m_ConstructionCost", 0), ("m_ElevationCost", 0), ("m_UpkeepCost", 0)])
    return emit(Obj(gp("NetPiecePrefab"), [
        ("name", name), ("active", True),
        ("components", Arr(T_LIST, [lanes, cost])),
        ("m_GeometryAsset", fref("CID", "0" * 32)),
        ("m_SurfaceAssets", Arr(T_ASSETREF, [])),
        ("m_Bounds", Struct(T_B3, [("min", f3()), ("max", f3())])),
        ("m_SurfaceArea", 0), ("m_IndexCount", 0), ("m_VertexCount", 0), ("m_MeshCount", 0),
        ("m_IsImpostor", False), ("m_ManualVTRequired", False),
        ("m_Layer", PIECE_LAYER), ("m_Width", PIECE_WIDTH), ("m_Length", PIECE_LENGTH),
        ("m_HeightRange", Struct(T_B1, [("min", PIECE_HEIGHT[0]), ("max", PIECE_HEIGHT[1])])),
        ("m_WidthOffset", 0), ("m_NodeOffset", NODE_OFFSET), ("m_SideConnectionOffset", 0),
        ("m_SurfaceHeights", Struct(T_F4, [("x", 0), ("y", 0), ("z", 0), ("w", 0)]))]))


def lane_prefab(name, mesh_ref, node_object_ref=None):
    """NetLaneGeometryPrefab: UtilityLane type 16 (no SpawnableLane), one mesh."""
    util = _component("UtilityLane", "UtilityLane", [
        ("m_UtilityType", UTILITY_FENCE), ("m_LocalConnectionLane", None),
        ("m_LocalConnectionLane2", None), ("m_NodeObject", node_object_ref),
        ("m_Width", LANE_WIDTH), ("m_VisualCapacity", LANE_CAPACITY), ("m_Hanging", 0),
        ("m_Underground", False)])
    mesh = Obj(gp("NetLaneMeshInfo"), [
        ("m_Mesh", mesh_ref), ("m_RequireSafe", False), ("m_RequireLevelCrossing", False),
        ("m_RequireEditor", False), ("m_RequireTrackCrossing", False), ("m_RequireClear", False),
        ("m_RequireLeftHandTraffic", False), ("m_RequireRightHandTraffic", False)])
    return emit(Obj(gp("NetLaneGeometryPrefab"), [
        ("name", name), ("active", True),
        ("components", Arr(T_LIST, [util])),
        ("m_PathfindPrefab", None),
        ("m_Meshes", Arr(gp("NetLaneMeshInfo[]"), [mesh]))]))


def post_prefab(name, mesh_ref):
    """The dead-end post (vanilla 'Water Pipe Node 1' shape): UtilityObject type 16."""
    util = _component("UtilityObject", "UtilityObject", [
        ("m_UtilityType", UTILITY_FENCE), ("m_UtilityPosition", f3())])
    info = Obj(gp("ObjectMeshInfo"), [
        ("m_Mesh", mesh_ref), ("m_Position", f3()),
        ("m_Rotation", Struct("Unity.Mathematics.quaternion, Unity.Mathematics", [
            ("value", Struct(T_F4, [("x", 0), ("y", 0), ("z", 0), ("w", 1)]))])),
        ("m_RequireState", 0)])
    return emit(Obj(gp("StaticObjectPrefab"), [
        ("name", name), ("active", True),
        ("components", Arr(T_LIST, [util])),
        ("m_Meshes", Arr(gp("ObjectMeshInfo[]"), [info])),
        ("m_Circular", True)]))


# --------------------------------------------------------------------------------------
# Mesh-prefab surgery fragments (appended to the IMPORTED mesh prefabs; @Tk@ = type ids,
# assigned and renumbered at surgery time)
# --------------------------------------------------------------------------------------

def curve_fragment(id0=900):
    return "\r\n".join([
        "            {",
        f'                "$id": {id0},',
        '                "$type": "@T1@|Game.Prefabs.CurveProperties, Game",',
        '                "name": "CurveProperties",',
        '                "active": true,',
        f'                "m_TilingCount": {TILING_COUNT},',
        f'                "m_OverrideLength": {_num(OVERRIDE_LENGTH)},',
        '                "m_SmoothingDistance": 0,',
        '                "m_GeometryTiling": true,',
        '                "m_StraightTiling": true,',
        '                "m_InvertCurve": false,',
        '                "m_SubFlow": false,',
        '                "m_HangingSwaying": false',
        "            }"])


def colour_fragment(rgb_slot2=PAD_RGB, rgb_slot3=PAD_RGB, id0=910):
    """ColorProperties with ONE VariationSet (the deployed SkiHydrantBoxA Mesh / kit-variant
    shape): ch0 white (no R texel), ch1 = net (ControlMask G), ch2 = posts (ControlMask B)."""
    g = ", ".join(_num(v) for v in rgb_slot2)
    b = ", ".join(_num(v) for v in rgb_slot3)
    return "\r\n".join([
        "            {",
        f'                "$id": {id0},',
        '                "$type": "@T1@|Game.Prefabs.ColorProperties, Game",',
        '                "name": "ColorProperties",',
        '                "active": true,',
        '                "m_ColorVariations": {',
        f'                    "$id": {id0 + 1},',
        '                    "$type": "@T2@|System.Collections.Generic.List`1[[Game.Prefabs.ColorProperties+VariationSet, Game]], mscorlib",',
        '                    "$rlength": 1,',
        '                    "$rcontent": [',
        '                        {',
        f'                            "$id": {id0 + 2},',
        '                            "$type": "@T3@|Game.Prefabs.ColorProperties+VariationSet, Game",',
        '                            "m_Colors": {',
        f'                                "$id": {id0 + 3},',
        '                                "$type": "@T4@|UnityEngine.Color[], UnityEngine.CoreModule",',
        '                                "$rlength": 3,',
        '                                "$rcontent": [',
        '                                    { "$type": "@T5@|UnityEngine.Color, UnityEngine.CoreModule", 1, 1, 1, 1 },',
        f'                                    {{ "$type": @T5@, {g}, 1 }},',
        f'                                    {{ "$type": @T5@, {b}, 1 }}',
        '                                ]',
        '                            },',
        '                            "m_VariationGroup": ""',
        '                        }',
        '                    ]',
        '                },',
        '                "m_ChannelsBinding": {',
        f'                    "$id": {id0 + 4},',
        '                    "$type": "@T6@|System.Collections.Generic.List`1[[Game.Prefabs.ColorProperties+ColorChannelBinding, Game]], mscorlib",',
        '                    "$rlength": 3,',
        '                    "$rcontent": [',
        f'                        {{ "$id": {id0 + 5}, "$type": "@T7@|Game.Prefabs.ColorProperties+ColorChannelBinding, Game", "m_ChannelId": 0, "m_CanBeModifiedByExternal": false }},',
        f'                        {{ "$id": {id0 + 6}, "$type": @T7@, "m_ChannelId": 1, "m_CanBeModifiedByExternal": false }},',
        f'                        {{ "$id": {id0 + 7}, "$type": @T7@, "m_ChannelId": 2, "m_CanBeModifiedByExternal": false }}',
        '                    ]',
        '                },',
        '                "m_VariationRanges": { "$type": "@T8@|Unity.Mathematics.int3, Unity.Mathematics", "x": 0, "y": 0, "z": 0 },',
        '                "m_AlphaRanges": { "$type": @T8@, "x": 0, "y": 0, "z": 0 },',
        '                "m_ExternalColorSource": 0',
        "            }"])


#: imported mesh prefab -> fragments appended at surgery (design section 5, vanilla
#: FenceIndustrialPieceHigh01 Mesh / _LOD1 Mesh shapes: LOD1 carries CurveProperties only)
MESH_FRAGMENTS = {
    f"{MESH} Mesh": (("CurveProperties", curve_fragment), ("ColorProperties", colour_fragment)),
    f"{MESH}_LOD1 Mesh": (("CurveProperties", curve_fragment),),
    f"{POST_MESH} Mesh": (("ColorProperties", colour_fragment),),
}


# --------------------------------------------------------------------------------------
# Text tools: type renumbering, the validate_prefab.ps1 rules, component insertion
# --------------------------------------------------------------------------------------

_TYPE_RE = re.compile(r'"\$type":\s*(?:"(\d+)\|([^"]+)"|(\d+))')


def renumber_types(text):
    """Re-number every $type id to first-declaration order (0, 1, 2 ...). Raises on a reference
    before its declaration or on one id declared with two type names."""
    mapping, names = {}, {}

    def sub(m):
        if m.group(1) is not None:
            old, tname = int(m.group(1)), m.group(2)
            if old in names and names[old] != tname:
                raise ValueError(f"type id {old} declared as {names[old]!r} and {tname!r}")
            names[old] = tname
            if old not in mapping:
                mapping[old] = len(mapping)
            return f'"$type": "{mapping[old]}|{tname}"'
        old = int(m.group(3))
        if old not in mapping:
            raise ValueError(f"$type {old} referenced before its declaration")
        return f'"$type": {mapping[old]}'

    return _TYPE_RE.sub(sub, text)


def check_prefab_text(text):
    """The rules of tools/validate_prefab.ps1 (forward refs, redeclarations, shell-mangle line
    shapes, brace balance) plus: no BOM, no unreplaced placeholder, monotonic declarations.
    Returns a list of error strings (empty = OK)."""
    errs = []
    if text.startswith("\ufeff"):
        errs.append("UTF-8 BOM")
    declared = {}
    last = -1
    braces = 0
    for i, line in enumerate(text.splitlines(), start=1):
        braces += line.count("{") - line.count("}")
        if len(line) > 400 or "&n " in line or '\\n"' in line:
            errs.append(f"line {i}: suspicious line (len {len(line)})")
        for m in _TYPE_RE.finditer(line):
            if m.group(1) is not None:
                idx, name = int(m.group(1)), m.group(2)
                if idx in declared and declared[idx] != name:
                    errs.append(f"line {i}: type id {idx} redeclared as {name!r}")
                if idx not in declared and idx < last:
                    errs.append(f"line {i}: declaration {idx} after {last} (non-monotonic)")
                declared[idx] = name
                last = max(last, idx)
            elif int(m.group(3)) not in declared:
                errs.append(f"line {i}: forward/undeclared type reference {m.group(3)}")
    if braces:
        errs.append(f"unbalanced braces (delta {braces})")
    if "REPLACE_" in text or "@T" in text:
        errs.append("unreplaced placeholder (REPLACE_ / @T)")
    return errs


def append_components(text, fragments):
    """Insert `fragments` (component blocks at 12-space indent, CRLF or LF) at the end of the
    root "components" list of an Editor-written prefab, bump $rlength, give the @Tk@ tokens
    fresh ids and renumber the whole table. Fragment $ids must not collide with the file's."""
    nl = "\r\n" if "\r\n" in text else "\n"
    lines = text.split(nl)
    try:
        i_c = lines.index('    "components": {')
    except ValueError:
        raise ValueError("no root components block") from None
    i_len = next(i for i in range(i_c, len(lines)) if re.fullmatch(r'        "\$rlength": \d+,', lines[i]))
    if lines[i_len + 1] != '        "$rcontent": [':
        raise ValueError("components block shape not recognized")
    i_close = next(i for i in range(i_len + 2, len(lines)) if lines[i] == "        ]")
    n = int(re.search(r"\d+", lines[i_len]).group(0))
    existing_ids = {int(x) for x in re.findall(r'"\$id":\s*(\d+)', text)}
    used = {int(x) for x in re.findall(r'"\$type":\s*"?(\d+)', text)}
    nxt = max(used | {0}) + 1000
    blocks = []
    for frag in fragments:
        for fid in re.findall(r'"\$id":\s*(\d+)', frag):
            if int(fid) in existing_ids:
                raise ValueError(f"fragment $id {fid} collides with the file")
        tokens = sorted(set(re.findall(r"@T(\d+)@", frag)), key=int)
        for t in tokens:
            frag = frag.replace(f"@T{t}@", str(nxt))
            nxt += 1
        blocks.append(frag.replace("\r\n", nl))
    if n > 0:
        if lines[i_close - 1] != "            }":
            raise ValueError("last component does not close at 12 spaces")
        lines[i_close - 1] += ","
    joined = ("," + nl).join(blocks).split(nl)
    lines[i_close:i_close] = joined
    lines[i_len] = f'        "$rlength": {n + len(fragments)},'
    return renumber_types(nl.join(lines))


# --------------------------------------------------------------------------------------
# .cid / .loc
# --------------------------------------------------------------------------------------

def read_cid(path):
    with open(path, "rb") as fh:
        raw = fh.read()
    cid = raw.decode("ascii").strip()
    if len(cid) != 32 or any(c not in "0123456789abcdef" for c in cid) or raw != cid.encode():
        raise RuntimeError(f"{path}: not a clean 32-hex cid ({raw!r})")
    return cid


def mint_cid(path):
    """Hand-minted .cid (beacon / dress_kit rule): an existing file is kept, never re-minted."""
    if os.path.isfile(path):
        return read_cid(path), False
    cid = uuid.uuid4().hex
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as fh:
        fh.write(cid.encode("ascii"))
    return cid, True


def _bstr(s):
    """System.IO.BinaryWriter.Write(string): 7-bit encoded byte length + UTF-8."""
    data = s.encode("utf-8")
    n, out = len(data), bytearray()
    while True:
        b = n & 0x7F
        n >>= 7
        out.append(b | (0x80 if n else 0))
        if not n:
            break
    return bytes(out) + data


def loc_bytes(entries, locale_id="en-US", language="English", localized_name="English"):
    """LocaleAsset.Write (1.6.2f1-colossal LocaleAsset.cs:142-160): ushort 1, SystemLanguage,
    localeId, localizedName, int32 count + (key, value)*, int32 count + (key, int32)* (0 here)."""
    out = struct.pack("<H", 1) + _bstr(language) + _bstr(locale_id) + _bstr(localized_name)
    out += struct.pack("<i", len(entries))
    for k, v in entries:
        out += _bstr(k) + _bstr(v)
    return out + struct.pack("<i", 0)


LOC_ENTRIES = (
    (f"Assets.NAME[{NET}]", "Safety Net Fence"),
    (f"Assets.DESCRIPTION[{NET}]", "Orange plastic safety netting on thin poles. Purely decorative."),
)


def prove_loc(mirror=MIRROR):
    """Re-write an Editor-made .loc byte for byte with loc_bytes. Returns (ok, detail)."""
    path = os.path.join(mirror, "SkiRunGroomedA", "SkiRunGroomedA_en-US.loc")
    with open(path, "rb") as fh:
        want = fh.read()
    got = loc_bytes([("Assets.NAME[SkiRunGroomedA]", "SkiRunGroomedA")])
    return got == want, f"{os.path.relpath(path, REPO)}: {len(want)} bytes, rewrite " \
                        f"{'identical' if got == want else 'DIFFERS'}"


# --------------------------------------------------------------------------------------
# Staging
# --------------------------------------------------------------------------------------

def _write_if_changed(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    if os.path.isfile(path):
        with open(path, "rb") as fh:
            if fh.read() == data:
                return False
    with open(path, "wb") as fh:
        fh.write(data)
    return True


def staged_dir(name, staging=STAGING):
    return os.path.join(staging, "_probe", name) if name in PROBE_PREFABS else os.path.join(staging, name)


def stage(staging=STAGING, with_loc=True):
    """Write every staged file (idempotent). Returns the record (also written as JSON)."""
    cids, minted = {}, []
    for name in MAIN_PREFABS + PROBE_PREFABS:
        cid, new = mint_cid(os.path.join(staged_dir(name, staging), f"{name}.Prefab.cid"))
        cids[name] = cid
        if new:
            minted.append(name)
    texts = {
        NET: net_prefab(NET, cids[SECTION], UI_PRIORITY),
        SECTION: section_prefab(SECTION, cids[PIECE]),
        PIECE: piece_prefab(PIECE, fref("CID", cids[LANE])),
        LANE: lane_prefab(LANE, fref("CID", PLACEHOLDER.format(MESH))),
        POST: post_prefab(POST, fref("CID", PLACEHOLDER.format(POST_MESH))),
        PROBE_NET: net_prefab(PROBE_NET, cids[PROBE_SECTION], PROBE_UI_PRIORITY),
        PROBE_SECTION: section_prefab(PROBE_SECTION, cids[PROBE_PIECE]),
        PROBE_PIECE: piece_prefab(PROBE_PIECE, fref("UnityGUID", GUID["chainlink_lane"][0])),
    }
    written = []
    for name, text in texts.items():
        if _write_if_changed(os.path.join(staged_dir(name, staging), f"{name}.Prefab"),
                             text.encode("utf-8")):
            written.append(f"{name}.Prefab")
    frag_dirs = {f"{MESH} Mesh": LANE, f"{MESH}_LOD1 Mesh": LANE, f"{POST_MESH} Mesh": POST}
    for mesh, frags in MESH_FRAGMENTS.items():
        for comp, fn in frags:
            p = os.path.join(staged_dir(frag_dirs[mesh], staging), f"{mesh}.{comp}.fragment")
            if _write_if_changed(p, (fn() + "\r\n").encode("utf-8")):
                written.append(os.path.basename(p))
    loc = None
    if with_loc:
        ok, detail = prove_loc()
        if not ok:
            raise RuntimeError(f"prove_loc failed: {detail}")
        lp = os.path.join(staged_dir(NET, staging), f"{NET}_en-US.loc")
        if _write_if_changed(lp, loc_bytes(LOC_ENTRIES)):
            written.append(os.path.basename(lp))
        lcid, new = mint_cid(lp + ".cid")
        loc = {"file": f"{NET}/{NET}_en-US.loc", "cid": lcid, "entries": dict(LOC_ENTRIES),
               "format_proof": detail, "optional": True}
        if new:
            minted.append(f"{NET}_en-US.loc")
    record = {
        "generator": "assets/blender/safety_fence_stage.py",
        "design": "docs/research/art/assets-2026-10/design/safety-fence.md",
        "report": "docs/research/art/assets-2026-10/build/safety-fence.md",
        "prefabs": {n: {"cid": cids[n], "dir": os.path.relpath(staged_dir(n, staging), staging).replace("\\", "/")}
                    for n in MAIN_PREFABS + PROBE_PREFABS},
        "placeholders": {LANE: PLACEHOLDER.format(MESH), POST: PLACEHOLDER.format(POST_MESH)},
        "mesh_fragments": {m: [c for c, _f in fr] for m, fr in MESH_FRAGMENTS.items()},
        "vanilla_guids": {k: {"guid": v[0], "source": v[1]} for k, v in GUID.items()},
        "icon_placeholder": ICON_PLACEHOLDER,
        "values": {"m_MaxSlopeSteepness": MAX_SLOPE, "piece_layer": PIECE_LAYER,
                   "piece_width": PIECE_WIDTH, "piece_length": PIECE_LENGTH,
                   "piece_height_range": list(PIECE_HEIGHT), "node_offset": NODE_OFFSET,
                   "allow_parallel": ALLOW_PARALLEL, "tiling_count": TILING_COUNT,
                   "override_length": OVERRIDE_LENGTH, "colour_slot2_slot3": list(PAD_RGB),
                   "lane_node_object_default": None},
        "loc": loc,
    }
    rec_text = json.dumps(record, indent=2) + "\n"
    if _write_if_changed(os.path.join(staging, "safety-fence-staging.json"), rec_text.encode("utf-8")):
        written.append("safety-fence-staging.json")
    record["minted"] = minted
    record["written"] = written
    return record


# --------------------------------------------------------------------------------------
# Surgery
# --------------------------------------------------------------------------------------

class SurgeryStop(RuntimeError):
    pass


def _one(pattern, what):
    hits = sorted(glob.glob(pattern))
    hits = [h for h in hits if h.endswith(".Prefab")]
    if len(hits) != 1:
        raise SurgeryStop(f"{what}: {len(hits)} match(es) for {pattern}")
    return hits[0]


def surface_template(path):
    with open(path, "rb") as fh:
        head = fh.read(6)
    if len(head) < 6:
        raise SurgeryStop(f"{path}: too short for a .Surface header")
    return struct.unpack_from("<i", head, 2)[0]


def _surface_of(mesh_prefab_text, lib):
    i0 = mesh_prefab_text.find('"m_SurfaceAssets"')
    i1 = mesh_prefab_text.find('"m_Bounds"', i0)
    m = re.search(r'\$fstrref:"CID:([0-9a-f]{32})"', mesh_prefab_text[i0:i1]) if 0 <= i0 < i1 else None
    if not m:
        raise SurgeryStop("mesh prefab has no m_SurfaceAssets CID")
    cid = m.group(1)
    for c in glob.glob(os.path.join(lib, "*.Surface.cid")):
        with open(c, "rb") as fh:
            if fh.read().decode("ascii", "replace").strip() == cid:
                return c[:-len(".cid")], cid
    raise SurgeryStop(f"no .Surface.cid holds {cid}")


def find_imported(imported_data):
    lib = os.path.join(imported_data, LIBRARY)
    out = {}
    for key, folder, stem in ((f"{MESH} Mesh", MESH, f"{MESH} Mesh_"),
                              (f"{MESH}_LOD1 Mesh", MESH, f"{MESH}_LOD1 Mesh_"),
                              (f"{POST_MESH} Mesh", POST_MESH, f"{POST_MESH} Mesh_")):
        p = _one(os.path.join(lib, folder, f"{stem}*.Prefab"), key)
        out[key] = {"prefab": p, "cid": read_cid(p + ".cid")}
    return out


def surgery(imported_data, apply=False, end_posts=False, probe=False, staging=STAGING, log=print,
            backup_root=None, loc=False):
    """See the module docstring. Returns a dict of what was (or would be) done."""
    imported_data = os.path.abspath(imported_data)
    stamp = time.strftime("%Y%m%d-%H%M%S")
    backup = os.path.join(backup_root or os.environ.get("TEMP", staging), f"safety-fence-surgery-{stamp}")
    done = {"imported_data": imported_data, "apply": apply, "steps": [], "written": [], "backup": None}

    def step(msg):
        done["steps"].append(msg)
        log(f"[surgery] {msg}")

    def write(path, data):
        if not apply:
            step(f"DRY RUN would write {os.path.relpath(path, imported_data)}")
            return
        if os.path.isfile(path):
            dst = os.path.join(backup, os.path.relpath(path, imported_data))
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.copy2(path, dst)
            done["backup"] = backup
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "wb") as fh:
            fh.write(data)
        done["written"].append(path)
        step(f"wrote {os.path.relpath(path, imported_data)}")

    def install(name, text):
        errs = check_prefab_text(text)
        if errs:
            raise SurgeryStop(f"{name}: {errs[:4]}")
        d = os.path.join(imported_data, name)
        write(os.path.join(d, f"{name}.Prefab"), text.encode("utf-8"))
        src = os.path.join(staged_dir(name, staging), f"{name}.Prefab.cid")
        write(os.path.join(d, f"{name}.Prefab.cid"), open(src, "rb").read())

    def staged_text(name):
        with open(os.path.join(staged_dir(name, staging), f"{name}.Prefab"), "rb") as fh:
            return fh.read().decode("utf-8")

    if probe:
        for name in PROBE_PREFABS:
            install(name, staged_text(name))
        step("probe installed: restart the game, draw it from Landscaping > Pathways "
             "(design section 6 step 0); delete the three folders after the probe")
        return done

    found = find_imported(imported_data)
    lib = os.path.join(imported_data, LIBRARY)
    texts = {}
    for key, rec in found.items():
        with open(rec["prefab"], "rb") as fh:
            texts[key] = fh.read().decode("utf-8")
        step(f"found {key}: {os.path.relpath(rec['prefab'], imported_data)} cid {rec['cid']}")
    # 2. template gate
    for key in (f"{MESH} Mesh", f"{MESH}_LOD1 Mesh"):
        spath, scid = _surface_of(texts[key], lib)
        t = surface_template(spath)
        step(f"{key}: surface {os.path.basename(spath)} template {t} "
             f"({TEMPLATE_HASH.get(t, '?')})")
        if t != WANT_TEMPLATE:
            raise SurgeryStop(f"{key}: template {t} ({TEMPLATE_HASH.get(t, '?')}) != {WANT_TEMPLATE} "
                              f"(CurvedHashedCutout): the settings.json Surface block did not apply. "
                              f"STOP: do not install the fence (a Default-template lane mesh does not "
                              f"bend, PHASE3 section 3.3); re-import or try CurvedCutout (31)")
    # 3. LOD gate
    lod0 = texts[f"{MESH} Mesh"]
    if "Game.Prefabs.LodProperties, Game" not in lod0 or found[f"{MESH}_LOD1 Mesh"]["cid"] not in lod0:
        raise SurgeryStop(f"{MESH} Mesh: LodProperties does not list the LOD1 mesh "
                          f"{found[f'{MESH}_LOD1 Mesh']['cid']}")
    step(f"{MESH} Mesh: LodProperties lists LOD1")
    # 4. mesh components
    for key, frags in MESH_FRAGMENTS.items():
        text = texts[key]
        have = [c for c, _f in frags if f"Game.Prefabs.{c}, Game" in text]
        if have:
            if len(have) != len(frags):
                raise SurgeryStop(f"{key}: partially edited already ({have}); restore from backup")
            step(f"{key}: already carries {have} (skipped)")
            continue
        new = append_components(text, [f() for _c, f in frags])
        errs = check_prefab_text(new)
        if errs:
            raise SurgeryStop(f"{key}: edited text fails checks {errs[:4]}")
        write(found[key]["prefab"], new.encode("utf-8"))
        step(f"{key}: + {[c for c, _f in frags]}")
    # 5. the hand-made prefabs
    post_cid = read_cid(os.path.join(staged_dir(POST, staging), f"{POST}.Prefab.cid"))
    lane = staged_text(LANE).replace(f"CID:{PLACEHOLDER.format(MESH)}",
                                     f"CID:{found[f'{MESH} Mesh']['cid']}")
    if end_posts:
        old = '"m_NodeObject": null,'
        if lane.count(old) != 1:
            raise SurgeryStop("lane: m_NodeObject line not found once")
        lane = lane.replace(old, f'"m_NodeObject": $fstrref:"CID:{post_cid}",')
        post = staged_text(POST).replace(f"CID:{PLACEHOLDER.format(POST_MESH)}",
                                         f"CID:{found[f'{POST_MESH} Mesh']['cid']}")
        install(POST, post)
    for name in (NET, SECTION, PIECE):
        install(name, staged_text(name))
    install(LANE, lane)
    if loc:                                   # OPTIONAL display name (build report)
        for fn in (f"{NET}_en-US.loc", f"{NET}_en-US.loc.cid"):
            with open(os.path.join(staged_dir(NET, staging), fn), "rb") as fh:
                write(os.path.join(imported_data, NET, fn), fh.read())
    step("installed " + ", ".join((NET, SECTION, PIECE, LANE) + ((POST,) if end_posts else ())
                                  + ((f"{NET}_en-US.loc",) if loc else ())))
    step("next: tools/validate_prefab.ps1 on every written .Prefab, tools/preflight.ps1 "
         "(0 FAIL / 0 WARN after the whitelist lines of the build report), tools/sync-gamedata.ps1")
    return done


# --------------------------------------------------------------------------------------
# Scratch-only: fabricate the importer's output so the surgery can be rehearsed
# --------------------------------------------------------------------------------------

def _fake_hex(seed):
    import hashlib
    return hashlib.md5(f"SkiSafetyFence.scratch.v1/{seed}".encode()).hexdigest()


def _mesh_prefab_text(name, geo_cid, surf_cid, bounds, counts, lod_cids=()):
    comps = []
    if lod_cids:
        comps.append(_component("LodProperties", "LodProperties", [
            ("m_Bias", 0), ("m_ShadowBias", 0),
            ("m_LodMeshes", Arr(gp("RenderPrefab[]"), [fref("CID", c) for c in lod_cids]))]))
    (x0, y0, z0), (x1, y1, z1) = bounds
    return emit(Obj(gp("RenderPrefab"), [
        ("name", name), ("active", True), ("version", 1), ("m_prefabFormat", 0),
        ("components", Arr(T_LIST, comps)),
        ("m_GeometryAsset", fref("CID", geo_cid)),
        ("m_SurfaceAssets", Arr(T_ASSETREF, [fref("CID", surf_cid)])),
        ("m_Bounds", Struct(T_B3, [("min", f3(x0, y0, z0)), ("max", f3(x1, y1, z1))])),
        ("m_SurfaceArea", counts["area"]), ("m_IndexCount", counts["index"]),
        ("m_VertexCount", counts["vertex"]), ("m_MeshCount", 1), ("m_IsImpostor", False),
        ("m_ManualVTRequired", False)]))


def simulate_import(scratch, template=WANT_TEMPLATE, import_root=IMPORT_ROOT):
    """Fabricate the Editor's output for SkiSafetyNetA (+ _LOD1) and SkiNetPostA in a SCRATCH
    ImportedData (marker file required). Bounds / counts come from the exported manifests."""
    scratch = os.path.abspath(scratch)
    if not os.path.isfile(os.path.join(scratch, SCRATCH_MARKER)):
        raise SystemExit(f"refusing: {scratch} has no {SCRATCH_MARKER} (scratch copies only)")
    if os.path.normcase(scratch) == os.path.normcase(os.path.abspath(GAME_IMPORTED_DATA)):
        raise SystemExit("refusing: that is the game's ImportedData")
    lib = os.path.join(scratch, LIBRARY)
    made = []

    def put(path, data):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "wb") as fh:
            fh.write(data)
        made.append(os.path.relpath(path, scratch))

    def manifest(name):
        with open(os.path.join(import_root, name, f"{name}.manifest.json"), encoding="utf-8") as fh:
            return json.load(fh)

    for asset, levels in ((MESH, (0, 1)), (POST_MESH, (0,))):
        man = manifest(asset)
        prefab_cids = {}
        for lvl in sorted(levels, reverse=True):          # LOD1 first: LOD0 lists it
            stem = asset if lvl == 0 else f"{asset}_LOD{lvl}"
            geo, surf = _fake_hex(f"{stem}.geo"), _fake_hex(f"{stem}.surf")
            put(os.path.join(lib, f"{stem}_{_fake_hex(stem + '.gname')}.Geometry.cid"), geo.encode())
            spath = os.path.join(lib, f"{stem}_{_fake_hex(stem + '.sname')}.Surface")
            put(spath, struct.pack("<Hi", 1, template if asset == MESH else 0) + b"\x00" * 10)
            put(spath + ".cid", surf.encode())
            info = man["prefab_bounds"][f"LOD{lvl}"]
            cnt = {"area": info["area_m2"], "index": 3 * info["tris"], "vertex": info["verts"]}
            lod_cids = [prefab_cids[1]] if (lvl == 0 and 1 in prefab_cids) else []
            text = _mesh_prefab_text(f"{stem} Mesh", geo, surf, (info["min"], info["max"]), cnt, lod_cids)
            pc = _fake_hex(f"{stem}.prefab")
            base = os.path.join(lib, asset, f"{stem} Mesh_{_fake_hex(stem + '.pname')}.Prefab")
            put(base, text.encode("utf-8"))
            put(base + ".cid", pc.encode())
            prefab_cids[lvl] = pc
        wpath = os.path.join(lib, asset, f"{asset}_{_fake_hex(asset + '.wname')}.Prefab")
        put(wpath, emit(Obj(gp("StaticObjectPrefab"), [
            ("name", asset), ("active", True), ("components", Arr(T_LIST, [])),
            ("m_Meshes", Arr(gp("ObjectMeshInfo[]"), [Obj(gp("ObjectMeshInfo"), [
                ("m_Mesh", fref("CID", prefab_cids[0])), ("m_Position", f3()),
                ("m_Rotation", Struct("Unity.Mathematics.quaternion, Unity.Mathematics", [
                    ("value", Struct(T_F4, [("x", 0), ("y", 0), ("z", 0), ("w", 1)]))])),
                ("m_RequireState", 0)])])),
            ("m_Circular", False)])).encode("utf-8"))
        put(wpath + ".cid", _fake_hex(asset + ".wrapper").encode())
    return made


# --------------------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------------------

def main(argv=None):
    ap = argparse.ArgumentParser(prog="safety_fence_stage.py")
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("stage")
    sub.add_parser("prove-loc")
    s = sub.add_parser("surgery")
    s.add_argument("--imported-data", default=GAME_IMPORTED_DATA)
    s.add_argument("--apply", action="store_true")
    s.add_argument("--end-posts", action="store_true")
    s.add_argument("--probe", action="store_true")
    s.add_argument("--loc", action="store_true", help="also install the optional en-US .loc")
    s2 = sub.add_parser("simulate-import")
    s2.add_argument("--imported-data", required=True)
    s2.add_argument("--template", type=int, default=WANT_TEMPLATE)
    args = ap.parse_args(argv)
    if args.cmd == "stage":
        rec = stage()
        print(f"[stage] {STAGING}: minted {rec['minted'] or 'none'}; written {rec['written'] or 'none'}")
        for n, r in rec["prefabs"].items():
            print(f"[stage]   {n:28s} {r['cid']}  {r['dir']}")
    elif args.cmd == "prove-loc":
        ok, detail = prove_loc()
        print(f"[stage] prove-loc {'OK' if ok else 'FAIL'}: {detail}")
        sys.exit(0 if ok else 1)
    elif args.cmd == "surgery":
        try:
            surgery(args.imported_data, apply=args.apply, end_posts=args.end_posts, probe=args.probe,
                    loc=args.loc)
        except SurgeryStop as e:
            print(f"[surgery] STOP: {e}")
            sys.exit(2)
    elif args.cmd == "simulate-import":
        for p in simulate_import(args.imported_data, template=args.template):
            print(f"[simulate] {p}")


if __name__ == "__main__":
    main()
