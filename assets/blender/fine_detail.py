"""
fine_detail.py - the fine-detail engine's RECIPE-SIDE API (no bpy: recipe modules import it).

Design: docs/research/art/assets-2026-10/design/fine-detail-pass.md A-D as cut by
design/fine-detail-pass-skeptic.md FS1-FS16 (the skeptic wins) and Richard's seventeenth look
(H1-H6 defaults: lanterns -> WallLightShortRangeRandom01, lit name signs at 0.2, +34 MB).
Build report: docs/research/art/assets-2026-10/build/fine-detail-step2-engine.md.

EVERYTHING IS OPT-IN PER HOST. A recipe that is a plain bldg_recipes.Recipe never reaches any
code path of this module, so every host that does not opt in regenerates byte-identical
(skeptic FS12; proven by the A/B in the build report).

    FineRecipe(Recipe)          a Recipe with three more fields (bldg_recipes.py is NOT edited:
                                its sha1 guards the L1 row of test_detail_kit K4):
        fine_detail  bool       the host's builders take fine=True (design B.1 / FS6) and the
                                recipe's vanilla / kit swaps replace the modelled parts
        sign_set     SignSetSpec | None   the host's label faces leave the host atlas for ONE
                                sign-set prop drawing from the shared building sign sheet
                                (cs2_signset.py, design C as cut by FS5 / FS14 / FS15 / FS16)
        legacy       ((field, value), ...)   the pre-pass values of the fields the pass changed
                                (sub_objects, lod1_extra, ...): effective(fine=False) restores
                                them, so `lodge.py --no-fine` rebuilds the approved pre-pass host
    effective(recipe, fine=True, signset=True) -> recipe     what lodge.py builds (CLI
                                --no-fine / --no-signset); plain Recipes pass through unchanged
    wrap(recipe, **changes) -> FineRecipe     (step 3a) a plain Recipe's fields + the fine ones, for a
                                recipe that cannot subclass here (L1 in bldg_recipes.py; registered by
                                recipes/lodge_aframe_fine.py through bldg_recipes OVERRIDES)

ROW KINDS in a host manifest's `sub_objects.objects` block (lodge.sub_objects_block):
    vanilla  (no "kind" key: the block shape every shipped manifest has) m_Object = the 32-hex
             placeholder GUID -> surgery `$fstrref:"UnityGUID:<guid>"`
    mod      {"kind": "mod", "ref": "CID", "m_Object": <32-hex object-prefab CID>, "prefab":
             <object prefab>, "mesh": <mesh asset>, position, rotation, m_ParentMesh [, "role"]}
             -> surgery `$fstrref:"CID:<cid>"`, the hosted-tower / beacon pattern
             (surgery/towers-transfer.md B1: our own CID sub-object rows; ObjectSubObjectInfo is
             the same component either way). role "signset" = the host's sign-set identity row.
    kit_row(prop, pos, ...) / kit_lean_row(...) build mod rows of the dressing kit (props/B,
    build/dress-kit.md section 3: pivots, the lean props' rest planes); sign_set_row(...) the
    sign-set identity row. A COLOURED kit prop (racks, akja) takes colour_set=<COLOUR_SETS key>:
    its row names the colour VARIANT's object-prefab CID and adds "colour_set" / "variant"
    (build/kit-colour-variants.md; the Parent-coloured base prefabs do not ship).

FRAMES. Rows are prefab-local "game" metres: (gx, gy, gz) = (-y, z, x) of authoring; FBX-raw is
(x, z, -y) of the net-oriented export (= authoring (y, z, x)) and the game NEGATES FBX x on import
(build/probes-b.md section 2), so game x = -authoring y: gx NEGATED. Settled N by the 2026-10-15
playtest (DECISIONS 201 S1b-N, applied 2026-10-16 / T14; analysis-assets.md section 3).
The recipes still DESIGN their rows in the old frame (gx = +authoring y, the frame the box / mesh
contracts test in); lodge.sub_objects_block / _mod_row_block apply rule T at the manifest boundary
(x -> -x, quaternion (qx, qy, qz, qw) -> (qx, -qy, -qz, qw), lean wall_point / wall_normal x -> -x):
design_row_to_game() below. An identity row (the sign set) is frame-free: host and set flip together.
"""

import math
import os
import re
import sys
from dataclasses import dataclass, field, fields, replace

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)
from bldg_recipes import Recipe    # noqa: E402

REPO = os.path.normpath(os.path.join(_HERE, os.pardir, os.pardir))
STAGING_ROOT = os.path.join(REPO, "assets", "staging", "prefabs")
_HEX32 = re.compile(r"^[0-9a-f]{32}$")

#: the convention string every opted-in manifest carries (and the build report quotes)
X_CONVENTION = ("game (gx, gy, gz) = (-y, z, x) of authoring; gx NEGATED (x-sign gate settled N, 2026-10-15 "
                "playtest; FBX-raw = authoring (y, z, x) and the game negates FBX x on import). Rows are designed "
                "in the old +y frame and written through rule T (x -> -x, q -> (qx, -qy, -qz, qw)). "
                "Identity rows (role signset) are frame-free.")


def rule_t_pos(p):
    """Rule T on a position: a design-frame row (gx = +authoring y) -> game, x -> -x (-0.0 normalised)."""
    return (-float(p[0]) + 0.0, float(p[1]), float(p[2]))


def rule_t_rot(q):
    """Rule T on a quaternion (qx, qy, qz, qw) -> (qx, -qy, -qz, qw); a yaw about +Y mirrors."""
    return (float(q[0]) + 0.0, -float(q[1]) + 0.0, -float(q[2]) + 0.0, float(q[3]) + 0.0)


def design_row_to_game(row):
    """A "kind": "mod" row dict (kit_row / kit_lean_row / sign_set_row, design frame) -> its game-frame copy:
    position and rotation through rule T, and a lean row's wall_point / wall_normal (both [x, z]) x -> -x.
    Every other key is carried over unchanged."""
    r = dict(row)
    r["position"] = rule_t_pos(row["position"])
    r["rotation"] = rule_t_rot(row["rotation"])
    if row.get("lean"):
        lean = dict(row["lean"])
        lean["wall_point"] = [-float(lean["wall_point"][0]) + 0.0, float(lean["wall_point"][1])]
        lean["wall_normal"] = [-float(lean["wall_normal"][0]) + 0.0, float(lean["wall_normal"][1])]
        r["lean"] = lean
    return r


# ======================================================================================
# The opt-in recipe
# ======================================================================================

@dataclass(frozen=True)
class SignSetSpec:
    """One host's sign set (design C.3 / C.6, FS15 letters). `faces` = ((host label face part
    name, sheet rect key), ...): those faces leave the host and are drawn from the shared
    building sheet (cs2_signset.SHEET) at their exact as-built position. `prop` is the
    letterless object prefab (survives mesh re-imports by CID repoint, beacon pattern);
    the mesh asset is prop + letter (one new letter per import round)."""
    prop: str                   # "SkiSignSetSchool"
    letter: str                 # "B" -> mesh asset "SkiSignSetSchoolB"
    host: str                   # sheet host key ("school")
    faces: tuple                # ((face part name, sheet key), ...)
    category: str = "signs"     # assets/import/<category>/<letter>/<mesh>/
    owner: bool = False         # this set's folder holds the sheet PNGs (the first sign set)

    @property
    def mesh(self):
        return self.prop + self.letter

    def face_names(self):
        return tuple(f for f, _k in self.faces)


@dataclass(frozen=True)
class FineRecipe(Recipe):
    fine_detail: bool = False
    sign_set: object = None
    legacy: tuple = ()
    #: kit colour variants (2026-10-12, build/kit-colour-variants.md): the COLOUR_SETS key every coloured
    #: kit row of this host names (check_kit_colours asserts it); None = the host has no coloured kit row
    kit_colour_set: object = None


def wrap(recipe, **changes):
    """A FineRecipe carrying every field of the PLAIN `recipe`, with `changes` applied (fine-detail step
    3a). For a recipe defined where FineRecipe cannot be imported: L1 lives in bldg_recipes.py, which
    this module imports, so it cannot import us back (circular); recipes/lodge_aframe_fine.py wraps
    bldg_recipes.L1 and registers it through bldg_recipes.load_recipes()'s OVERRIDES hook."""
    if isinstance(recipe, FineRecipe) or not isinstance(recipe, Recipe):
        raise TypeError(f"wrap: {type(recipe).__name__} is not a plain bldg_recipes.Recipe")
    base = {f.name: getattr(recipe, f.name) for f in fields(Recipe)}
    bad = sorted(set(changes) - {f.name for f in fields(FineRecipe)})
    if bad:
        raise KeyError(f"wrap {recipe.key}: {bad} are not FineRecipe fields")
    base.update(changes)
    return FineRecipe(**base)


def is_fine(recipe):
    return bool(getattr(recipe, "fine_detail", False))


def sign_set_of(recipe):
    return getattr(recipe, "sign_set", None)


def effective(recipe, fine=True, signset=True):
    """The recipe lodge.py builds. Plain Recipes are returned unchanged (identity). fine=False:
    the pre-pass host (legacy fields restored, no sign set). signset=False: the fine host with
    its labels kept in the atlas."""
    if not isinstance(recipe, FineRecipe):
        return recipe
    if not fine:
        names = {f.name for f in fields(recipe)}
        legacy = dict(recipe.legacy)
        bad = sorted(set(legacy) - names)
        if bad:
            raise KeyError(f"{recipe.key}: legacy fields {bad} are not Recipe fields")
        return replace(recipe, fine_detail=False, sign_set=None, **legacy)
    if not signset:
        return replace(recipe, sign_set=None)
    return recipe


# ======================================================================================
# The dressing kit (props letter B, build/dress-kit.md section 3)
# ======================================================================================

@dataclass(frozen=True)
class KitProp:
    prop: str                   # object prefab (letterless)
    mesh: str                   # mesh asset (import folder)
    cid: str                    # object-prefab CID, hand-minted once (assets/staging/prefabs/props/B); for a
    #                             COLOURED prop the base CID is RETIRED (never shipped): rows name a variant
    mount: str                  # "ground" | "wall" | "lean"
    rest_z: float = 0.0         # lean props: prefab z of the plane that rests on the wall (< 0)
    tris: int = 0
    coloured: bool = False      # gear accent / akja shell texels on ControlMask G (slot 2, channel 1)


#: object prefab -> KitProp. CIDs = the staged .cid files of the UNCOLOURED props (tests/test_signset.py KIT
#: asserts it, so a re-mint cannot drift silently); the six coloured props' base CIDs are retired with their
#: staged base prefabs (kit colour variants, 2026-10-12). rest_z = the shipped FBX min z of the three leaning
#: props (dress-kit.md 3: tools -0.393 "the foot-line rule", akja -0.711, spine board -0.161).
KIT = {
    "SkiDressSkiRack": KitProp("SkiDressSkiRack", "SkiDressSkiRackB", "56fd40887e144eb3b01f270044f47271", "ground", tris=692, coloured=True),
    "SkiDressSkiRackV2": KitProp("SkiDressSkiRackV2", "SkiDressSkiRackV2B", "b1a58b870920400ea06c1a2590d2e081", "ground", tris=640, coloured=True),
    "SkiDressSkiRackV3": KitProp("SkiDressSkiRackV3", "SkiDressSkiRackV3B", "1bef6d6e9b884b06828092a9ad5c9bc1", "ground", tris=600, coloured=True),
    "SkiDressSkiRackWall": KitProp("SkiDressSkiRackWall", "SkiDressSkiRackWallB", "7d045169cdbf4756b260975e6cd68982", "ground", tris=336, coloured=True),
    "SkiDressSkiRackWallV2": KitProp("SkiDressSkiRackWallV2", "SkiDressSkiRackWallV2B", "df644f5447f34ecf87722c623fe18bcb", "ground", tris=320, coloured=True),
    "SkiDressHoseReel": KitProp("SkiDressHoseReel", "SkiDressHoseReelB", "e8c76b0b9f9340a08f728e6d992d4692", "wall", tris=256),
    "SkiDressExtinguisher": KitProp("SkiDressExtinguisher", "SkiDressExtinguisherB", "04211f82829f421b9899ffaff582e530", "wall", tris=178),
    "SkiDressTools": KitProp("SkiDressTools", "SkiDressToolsB", "5f92c70cec824289aa2673d59018e8a5", "lean", rest_z=-0.3925, tris=92),
    "SkiDressEStop": KitProp("SkiDressEStop", "SkiDressEStopB", "9ad8824185f7473ea1799df015acc940", "ground", tris=102),
    "SkiDressAkja": KitProp("SkiDressAkja", "SkiDressAkjaB", "c447edf1e8ff4063929996fdf990a042", "lean", rest_z=-0.7108, tris=252, coloured=True),
    "SkiDressSpineBoard": KitProp("SkiDressSpineBoard", "SkiDressSpineBoardB", "bfd4df9f277f4148b2615650e3470444", "lean", rest_z=-0.1612, tris=28),
}


# --------------------------------------------------------------------------------------
# Kit colour variants (Richard 2026-10-12 ~18:50: "I don't think we want the accents to follow the parent
# colour. I think they should stay a colour that makes sense for that building." -> FS7 Parent DROPPED;
# build/kit-colour-variants.md). ENGINE FACT (decompile 1.6.0f1): ColorProperties is a RenderPrefab
# component (ColorProperties.cs:12-16 ComponentMenu RenderPrefab / CharacterOverlay; GetPrefabComponents
# adds the ColorVariation buffer to the MESH prefab entity, :137-140) and MeshColorSystem picks an
# instance colour from the ColorVariation buffer of each SubMesh's MESH prefab (MeshColorSystem.cs:470-471,
# 541), never from the object prefab. So a colour variant = its own object prefab + its own mesh prefab
# (a hand-made RenderPrefab clone of the imported base mesh prefab: the SAME geometry and surface assets,
# a new name / CID and a FIXED ColorProperties with ONE VariationSet, no external channel, ranges 0), both
# minted by dress_kit.py's registry. One VariationSet at probability 100 is always the pick (the loop at
# MeshColorSystem.cs:560-702 with one entry), ranges 0 skip RandomizeColor (:842-857), no external channel
# skips FindExternalSource (:768-774; ColorVariation.hasExternalChannels): deterministic.
# --------------------------------------------------------------------------------------

@dataclass(frozen=True)
class ColourSet:
    key: str                    # registry key ("red")
    label: str                  # variant-name suffix ("Red" -> SkiDressSkiRackV2_Red)
    rgb: tuple                  # the slot-2 VariationSet value (channel 1), the hosts' own picker space: the
    #                             kit accent texels are RECOLOR_ALBEDO 0.80 grey under the mask, like every
    #                             host slot texel, so a set equal to a host slot value renders as that slot
    source: str                 # where the value comes from


COLOUR_SETS = {
    "red": ColourSet("red", "Red", (0.775, 0.125, 0.100),
                     "tram base slot-3 red (tram_station.TINT['base']['B']) = cs2_bldg.HUT_RED_SRGB (0.62, 0.10, "
                     "0.08) / 0.80: the revision-2 tram rack's red, the hut red, the patrol rescue red"),
    "orange": ColourSet("orange", "Orange", (0.85, 0.25, 0.10),
                        "cs2_asset.PAD_RGB: the slot-2 default of L1, the DQ / gondola stations (pad2, GD_TINT G), "
                        "every tower crash pad and the K / M stations (M_TINT G)"),
    "yellow": ColourSet("yellow", "Yellow", (0.90, 0.70, 0.08),
                        "tram top slot-2 bullwheel yellow (tram_station.TINT['top']['G']): Yan black / yellow"),
    "green": ColourSet("green", "Green", (0.0625, 0.3125, 0.25),
                       "the resort trim green (slot 1 of L1 / L2 / L3, lodges-basic Q6) = (0.05, 0.25, 0.20) / 0.80"),
    "blue": ColourSet("blue", "Blue", (0.10, 0.35, 0.90),
                      "school.TRAIL_BLUE (0.08, 0.28, 0.72) / 0.80: the school's lesson / trail-board blue"),
}
#: seeds of the variant CIDs (md5 of seed + name, the SkiResortIcons pattern): stable across runs and machines
VARIANT_CID_SEED = "SkiResortMod.KitVariant.v1/"
VARIANT_MESH_CID_SEED = "SkiResortMod.KitVariantMesh.v1/"


def is_coloured(prop):
    return bool(KIT[prop].coloured)


def variant_name(prop, colour_set):
    """The variant object prefab of a coloured kit prop: SkiDressSkiRackV2 + red -> SkiDressSkiRackV2_Red."""
    if prop not in KIT or not KIT[prop].coloured:
        raise ValueError(f"variant_name: {prop!r} is not a coloured kit prop")
    if colour_set not in COLOUR_SETS:
        raise ValueError(f"variant_name: unknown colour set {colour_set!r} (COLOUR_SETS {sorted(COLOUR_SETS)})")
    return f"{prop}_{COLOUR_SETS[colour_set].label}"


def variant_mesh_name(name):
    """The variant's own mesh prefab (RenderPrefab clone of the imported base mesh prefab)."""
    return f"{name} Mesh"


def _seeded(seed, name):
    import hashlib
    return hashlib.md5((seed + name).encode("utf-8")).hexdigest()


def variant_cids(name):
    """(object-prefab CID, mesh-prefab CID) of a variant: seeded from its name, so every run mints the same."""
    return _seeded(VARIANT_CID_SEED, name), _seeded(VARIANT_MESH_CID_SEED, variant_mesh_name(name))


def kit_cid(prop, colour_set=None):
    """The object-prefab CID a row of `prop` names: the colour-set VARIANT's for a coloured prop (colour_set
    required: no Parent colour ships), KIT[prop].cid for an uncoloured one (colour_set must be None)."""
    k = KIT[prop]
    if k.coloured:
        if colour_set is None:
            raise ValueError(f"{prop} is a coloured kit prop: its row needs a colour set ({sorted(COLOUR_SETS)}); "
                             f"the base prefab (Parent colour) does not ship")
        return variant_cids(variant_name(prop, colour_set))[0]
    if colour_set is not None:
        raise ValueError(f"{prop} has no ControlMask texel: no colour set ({colour_set!r})")
    return k.cid
#: A-frame rack loadouts to alternate along a row (FS6; dress-kit.md 1.2)
AFRAME_RACKS = ("SkiDressSkiRack", "SkiDressSkiRackV2", "SkiDressSkiRackV3")
WALL_RACKS = ("SkiDressSkiRackWall", "SkiDressSkiRackWallV2")
#: the lean props' foot-line rule (Richard / dress-kit.md 3, the "0.393 m" note): the prop's
#: rest plane (prefab z = rest_z) touches the wall face; FOOT_TOL = the contract tolerance
FOOT_TOL = 0.005


def staged_cid(prop, letter="B", kind="props"):
    """The hand-minted object-prefab CID staged at assets/staging/prefabs/<kind>/<letter>/<prop>/
    <prop>.Prefab.cid (32 lowercase hex, no newline), or None."""
    p = os.path.join(STAGING_ROOT, kind, letter, prop, f"{prop}.Prefab.cid")
    if not os.path.isfile(p):
        return None
    with open(p, "r", encoding="utf-8") as fh:
        s = fh.read().strip()
    return s if _HEX32.match(s) else None


def staged_kit_cid(prop, colour_set=None):
    """The staged object-prefab CID of what a row of `prop` names: the variant folder
    (assets/staging/prefabs/props/B/<Variant>/<Variant>.Prefab.cid, dress_kit.stage_variant) of a coloured
    prop, the base folder of an uncoloured one; None if not staged."""
    if KIT[prop].coloured:
        return staged_cid(variant_name(prop, colour_set)) if colour_set in COLOUR_SETS else None
    return staged_cid(prop)


def row_colour_error(row):
    """None if a kit "mod" row names the right object prefab, else the reason: a coloured prop needs a known
    colour set, CID = its variant's seeded CID and "variant" = its name, and a staged variant .cid (when one
    exists) equal to it; an uncoloured prop has no colour set and CID = KIT's = the staged base .cid."""
    prop, cs = row.get("prefab"), row.get("colour_set")
    if prop not in KIT:
        return f"{prop!r} is not a kit prop"
    try:
        want = kit_cid(prop, cs)
    except ValueError as e:
        return str(e)
    if row.get("m_Object") != want:
        return f"{prop} ({cs}): CID {row.get('m_Object')} != {want}"
    if KIT[prop].coloured:
        if row.get("variant") != variant_name(prop, cs):
            return f"{prop} ({cs}): variant {row.get('variant')!r} != {variant_name(prop, cs)!r}"
        st = staged_kit_cid(prop, cs)
        if st is not None and st != want:
            return f"{prop} ({cs}): staged variant .cid {st} != the seeded {want}"
    elif staged_cid(prop) != KIT[prop].cid:
        return f"{prop}: CID {KIT[prop].cid} != the staged {staged_cid(prop)}"
    return None


def check_kit_colours(recipe):
    """Every coloured kit row of a FineRecipe names the recipe's kit_colour_set (one set per building, Richard
    2026-10-12); raises ValueError. Uncoloured rows and sign-set rows pass."""
    want = getattr(recipe, "kit_colour_set", None)
    bad = []
    for e in getattr(recipe, "sub_objects", ()) or ():
        if not (isinstance(e, dict) and e.get("kind") == "mod" and e.get("prefab") in KIT):
            continue
        err = row_colour_error(e)
        if err:
            bad.append(err)
        elif KIT[e["prefab"]].coloured and e.get("colour_set") != want:
            bad.append(f"{e['prefab']}: colour set {e.get('colour_set')!r} != the recipe's {want!r}")
    if bad:
        raise ValueError(f"{getattr(recipe, 'key', '?')} kit colours: {bad[:4]}")
    return True


def yaw_quat(direction):
    """(x, y, z, w) yaw about game +Y turning the prop's local +Z to the horizontal game
    direction (dx, dz) (cs2_liftdress.subobj_box rotation: +Z -> (sin a, 0, cos a))."""
    dx, dz = float(direction[0]), float(direction[1])
    n = math.hypot(dx, dz)
    if n < 1e-9:
        raise ValueError(f"yaw_quat: zero direction {direction}")
    a = math.atan2(dx / n, dz / n)
    return (0.0, round(math.sin(a / 2.0), 7) + 0.0, 0.0, round(math.cos(a / 2.0), 7) + 0.0)


def _row(kind_prop, cid, mesh, pos, rot, parent, role=None, extra=None):
    if not _HEX32.match(str(cid)):
        raise ValueError(f"mod row {kind_prop}: CID {cid!r} is not 32 lowercase hex")
    if len(pos) != 3 or len(rot) != 4:
        raise ValueError(f"mod row {kind_prop}: position / rotation shape")
    nq = math.sqrt(sum(float(c) ** 2 for c in rot))
    if abs(nq - 1.0) > 1e-3:
        raise ValueError(f"mod row {kind_prop}: |q| = {nq:.4f}")
    if int(parent) not in (-1, 0):
        raise ValueError(f"mod row {kind_prop}: m_ParentMesh {parent} (-1 lot | 0 main mesh)")
    r = {"kind": "mod", "ref": "CID", "m_Object": str(cid), "prefab": str(kind_prop),
         "mesh": str(mesh), "position": tuple(float(v) for v in pos),
         "rotation": tuple(float(v) for v in rot), "m_ParentMesh": int(parent)}
    if role:
        r["role"] = str(role)
    if extra:
        r.update(extra)
    return r


def _colour_extra(prop, colour_set):
    """The row keys a coloured kit prop adds (after the existing ones): its colour set and variant name."""
    kit_cid(prop, colour_set)               # raises the explicit message (missing / forbidden colour set)
    if not KIT[prop].coloured:
        return {}
    return {"colour_set": str(colour_set), "variant": variant_name(prop, colour_set)}


def kit_row(prop, pos, facing=(0.0, 1.0), parent=-1, rot=None, note=None, colour_set=None):
    """A dressing-kit row: `pos` game-local (the prop's pivot: foot centre for ground props,
    plate bottom on the wall plane for wall props), `facing` = the game direction (dx, dz) the
    prop's +Z (its front / out of the wall) turns to (default (0, 1) = game +Z = identity), or an
    explicit quaternion `rot`. parent -1 on the lot (snaps to terrain), 0 on the main mesh.
    colour_set: a COLOUR_SETS key, REQUIRED for a coloured prop (racks, akja): the row then names that
    variant's CID and carries "colour_set" / "variant"; forbidden for an uncoloured one."""
    k = KIT[prop]
    q = tuple(rot) if rot is not None else yaw_quat(facing)
    extra = dict({"note": note} if note else {}, **_colour_extra(prop, colour_set))
    return _row(prop, kit_cid(prop, colour_set), k.mesh, pos, q, parent, extra=extra or None)


def kit_lean_row(prop, wall_point, wall_normal, floor_y, parent=-1, note=None, colour_set=None):
    """A LEANING kit prop (tools, akja, spine board) against a wall: the prop's rest plane
    (prefab z = KitProp.rest_z) lies ON the wall face. wall_point = any game point on the wall
    face (its gx, gz used), wall_normal = the wall's outward horizontal normal (dx, dz); the prop
    faces away from the wall (+Z = normal, its top leans back toward -Z = the wall), its foot
    line on floor_y, -rest_z out from the wall face (the 0.393 m tools rule). colour_set as kit_row.
    Returns the row."""
    k = KIT[prop]
    if k.mount != "lean":
        raise ValueError(f"kit_lean_row: {prop} is a {k.mount} prop")
    nx, nz = float(wall_normal[0]), float(wall_normal[1])
    n = math.hypot(nx, nz)
    nx, nz = nx / n, nz / n
    d = -k.rest_z
    pos = (float(wall_point[0]) + nx * d, float(floor_y), float(wall_point[2]) + nz * d)
    return _row(prop, kit_cid(prop, colour_set), k.mesh, pos, yaw_quat((nx, nz)), parent,
                extra={"lean": {"wall_point": [float(wall_point[0]), float(wall_point[2])],
                                "wall_normal": [nx, nz], "rest_z": k.rest_z},
                       **({"note": note} if note else {}), **_colour_extra(prop, colour_set)})


def lean_rest_gap(row):
    """Distance (m, signed: + = the rest plane stands off the wall) between a lean row's rest
    plane and its wall face (0 within FOOT_TOL = the foot-line contract)."""
    lean = row["lean"]
    k = KIT[row["prefab"]]
    nx, nz = lean["wall_normal"]
    px, pz = row["position"][0], row["position"][2]
    wx, wz = lean["wall_point"]
    # the rest plane in game: pivot + R(-|rest_z| along +Z) = pivot - n * |rest_z|
    rx, rz = px + nx * k.rest_z, pz + nz * k.rest_z
    return (rx - wx) * nx + (rz - wz) * nz


def sign_set_row(spec, cid, bounds=None):
    """The host's sign-set identity row (design C.6): position 0, rotation identity, m_ParentMesh 0
    (on the main mesh: -1 would snap it to terrain), role signset; `bounds` = the set's game-local
    ((lo), (hi)) for the review stand-in readers (cs2_subobj own_bounds)."""
    extra = None
    if bounds is not None:
        extra = {"bounds": [[round(float(v), 4) for v in bounds[0]],
                            [round(float(v), 4) for v in bounds[1]]]}
    return _row(spec.prop, cid, spec.mesh, (0.0, 0.0, 0.0), (0.0, 0.0, 0.0, 1.0), 0, role="signset",
                extra=extra)


# ======================================================================================
# Nitpick C (2026-10-13, build/nitpick-c-lodges.md): BLANK BOARDS. Richard, twenty-fifth look, L3: "not
# loving the SIGN of the FORK AND KNIFE, that looks quite bad. I wonder if we just leave it blank or keep
# it simple, and people can paste their own labels on it?"; L4: "Same issue with the sign outside as the
# chalet." A pictogram board becomes a plain panel with a thin rim (a draw callable for cs2_bldg's
# "display" painter and the sheet's "blank" kind); lodge.py records every blank board's face (centre /
# normal / size, game frame) in the host manifest's `we_boards` block for an optional Write Everywhere
# layout (another fleet's job): the face stays flat and unobstructed.
# ======================================================================================

def blank_board(bg=(0.20, 0.14, 0.10), rim=None, rim_m=0.02, inset_m=0.05):
    """A draw callable(cv) for a DisplayCanvas (cs2_bldg.DisplayPainter, cs2_signset._canvas): fill `bg`
    (encoded sRGB), then, when `rim` is given, a rectangular line `rim_m` wide `inset_m` in from the face
    edge in `rim` (encoded sRGB). No text, no pictogram. The callable carries `we_blank` (its parameters),
    which lodge.py reads to list the face in the manifest's `we_boards` block."""
    bg = tuple(float(v) for v in ((bg,) * 3 if isinstance(bg, (int, float)) else bg))
    rim_t = None if rim is None else tuple(float(v) for v in ((rim,) * 3 if isinstance(rim, (int, float)) else rim))

    def draw(cv):
        import numpy as np
        cv.fill(bg)
        rec = {"bg": list(bg), "rim": None if rim_t is None else list(rim_t)}
        if rim_t is not None:
            a, b = float(inset_m), float(inset_m) + float(rim_m)
            X, Y = cv.x, cv.y
            outer = (X >= a) & (X <= cv.W - a) & (Y >= a) & (Y <= cv.H - a)
            inner = (X > b) & (X < cv.W - b) & (Y > b) & (Y < cv.H - b)
            cv.paint(cv.cover(np.asarray(outer & ~inner)), rim_t)
            rec.update({"rim_m": round(float(rim_m), 4), "inset_m": round(float(inset_m), 4),
                        "rim_px": round(float(rim_m) * cv.ppm, 2)})
        cv.record("blank", rec)
    draw.we_blank = {"bg": list(bg), "rim": None if rim_t is None else list(rim_t), "rim_m": float(rim_m),
                     "inset_m": float(inset_m)}
    draw.__name__ = "blank_board"
    return draw


def is_blank_label(entry):
    """True for a recipe label entry (face, painter, kwargs) painted by a blank_board draw (the "display"
    painter with a draw carrying `we_blank`)."""
    _face, painter, kw = entry
    if painter != "display":
        return False
    return any(callable(d) and getattr(d, "we_blank", None) is not None for d in (kw or {}).get("draws", ()))


__all__ = ["FineRecipe", "SignSetSpec", "effective", "wrap", "is_fine", "sign_set_of", "KIT", "KitProp",
           "blank_board", "is_blank_label",
           "AFRAME_RACKS", "WALL_RACKS", "kit_row", "kit_lean_row", "lean_rest_gap", "sign_set_row",
           "staged_cid", "yaw_quat", "X_CONVENTION", "FOOT_TOL", "ColourSet", "COLOUR_SETS", "is_coloured",
           "variant_name", "variant_mesh_name", "variant_cids", "kit_cid", "staged_kit_cid", "row_colour_error",
           "check_kit_colours", "VARIANT_CID_SEED", "VARIANT_MESH_CID_SEED"]
