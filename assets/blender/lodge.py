"""
lodge.py - the building recipe engine (lodges L1-L5, ski school, patrol P1/P2).

Contract: docs/research/art/assets-2026-10/design/bldg-kit.md sections 1, 8, 9, 11-17 with
design/bldg-kit-skeptic.md applied (MUST-CHANGE 1 src_uv="UVTile" + stripped part UV
layers; 2 lot-fit / facing between apply_yaw and orient_for_net; 3 plinth stairs below
grade; 4 ground-band hygiene; 5 height-aware cull; 6 CC0 prefilter; 7 colour spaces;
8 kept-area texel prediction + full-level LOD dry run). Recipes are data in
bldg_recipes.py and recipes/*.py; the kit is cs2_bldg.py.

CLI (headless only - refuses to run unless bpy.app.background, ROUND-SPEC R10):
    blender --background --factory-startup --python-exit-code 1 --python lodge.py --
        (--lodge {L1..L5 | basic-base | basic-top | mid | lux-top | lux-base}
         | --school | --patrol {p1,p2} | --recipe KEY)
        [--out DIR | --export]   letter folder; --export = assets/import/<category>/<letter>;
                                 neither = a scratch dir under the system temp
        [--name BASE] [--letter X]
        [--blockout]             geometry + contracts + rooms + cull + area / density
                                 prediction + manifest; no bake, no FBX
        [--finish | --no-finish] bake (default on); --no-finish = look-only FBX, NOT shippable
        [--lods | --no-lods] [--no-validate]
        [--tex {auto,1024,2048,4096}] [--interior {name,settings}]
        [--tower | --no-tower] [--roof-windows {win,paint}]
        [--preview [DIR]]        default assets/preview/<category>/
        [--ab DIR] [--review [DIR]]   review pack default assets/preview/review/<Name>.blend
        [--weather-sweep [I,I,...]]   art pass: vanilla budget per weathering intensity on
                                 the same bake (recipe.weather set; default steps
                                 0.35..0.70), manifest weather_sweep + highest clean step
        [--signset | --no-signset] [--no-fine] [--signset-out DIR]   fine-detail pass (opt-in
                                 per recipe: fine_detail.FineRecipe; a plain Recipe ignores
                                 all four): --signset asserts the recipe declares a sign set,
                                 --no-signset keeps its labels in the atlas, --no-fine builds the
                                 pre-pass host (legacy fields, no sign set); the sign set goes to
                                 assets/import/signs/<letter> with --export, else beside --out
Fine-detail pass (design/fine-detail-pass.md A-D + skeptic FS1-FS16, build/fine-detail-step2-
engine.md): a FineRecipe's builder takes fine=True kit geometry, its sub_objects carry vanilla
AND "kind": "mod" rows (our own props by object-prefab CID: the dressing kit, the sign set), and
with a sign_set its label faces are routed out of the host before the join into ONE sign-set
prop on the shared building sheet (cs2_signset), exported beside the host with its identity row
appended to the host's sub_objects; the review pack shows the sign set as its real mesh.
Art pass, detail pass 1 (design/detail-pass-1.md section 4, skeptic M3; DECISIONS 171):
recipe.weather (None = the old path, byte-identical) wraps the compose hook with
cs2_weather (wrap_weather: roles by material index, label rects protected); recipe.weights
override the kit's density weights (part_weights); recipe.sub_objects and the
DisplayPainter numbers go into the manifest (sub_objects, labels, weather blocks).
Nitpick C (2026-10-13, build/nitpick-c-lodges.md): a FineRecipe build lists its Write Everywhere targets (every
host label painted by fine_detail.blank_board, every lit or blank sign-set face: centre / normal / right / up in
the game frame + size, cs2_signset.face_record) in the manifest `we_boards` block (absent when there are none).
Exit code 1 on any exception; last line
    PASS lodge: <Name> LOD0 n, chain [n1, n2], rooms k, engine base <verdict>, tex A, px/m med

Stage order (run): clear -> build_parts -> bevel -> density_w -> tag_parts -> strip UV
layers + cs2_bake.material_sources (UVTile) + retile -> join + weld 0.0005 ->
shade 66 -> apply_yaw -> check_lot_fit + check_facing (authoring frame) -> orient_for_net
-> check_base_mode -> split_rooms -> cull_hidden -> assert_budget -> area_report
(blockout stops) -> prefilter + bake_unique(src_uv="UVTile") -> (4096 rule) -> export
LOD0 + `_Win` -> plan_lod_targets + lod_chain -> per-LOD base / pane checks -> settings
-> previews / review pack -> manifest -> validate.
"""

import argparse
import json
import math
import os
import subprocess
import sys
import tempfile
import time
import traceback
from dataclasses import replace

import bpy
import numpy as np

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)
import bldg_recipes as R      # noqa: E402
import cs2_asset              # noqa: E402
import cs2_bake               # noqa: E402
import cs2_bldg as K          # noqa: E402
import cs2_compose            # noqa: E402
import cs2_submesh            # noqa: E402
import cs2_uv                 # noqa: E402
import cs2_validate           # noqa: E402
import fine_detail as FD      # noqa: E402  (bpy-free; plain Recipes pass through untouched)
from cs2_bldg import WallRef, RoofSpec, WinSpec   # noqa: E402

REPO = os.path.normpath(os.path.join(_HERE, os.pardir, os.pardir))
IMPORT_ROOT = os.path.join(REPO, "assets", "import")
PREVIEW_ROOT = os.path.join(REPO, "assets", "preview")
SPEC_TAG = "bldg-kit-2026-10"
WELD_M = 0.0005                 # base_lodge.py:914
SHADE_ANGLE = 66.0
AO_SAMPLES = 48                 # PIPELINE section 3
AO_DISTANCE_M = 2.0             # N6 via the world AO distance (cs2_bake has no parameter)
R18_MIN_PPM = 25.0
KEYWORDS = ("CommercialLow", "Office")
ALIASES_CLI = {"basic-base": "L1", "basic-top": "L2", "mid": "L3", "lux-top": "L4",
               "lux-base": "L5"}
AB_DEFAULT = {"L1": os.path.join("lodges", "A", "SkiAFrameDayLodge")}   # section 15 "ab"
VIEWS = (("arrival_3q", (0.8, -1.0, 0.45)), ("back_3q", (-0.8, 1.0, 0.45)),
         ("arrival_front", (0.05, -1.0, 0.20)), ("camera_top", (0.3, -0.4, 1.0)))


# ======================================================================================
# CLI
# ======================================================================================

def parse_args(argv=None):
    if argv is None:
        argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser(prog="lodge.py", description="building recipe engine")
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--lodge", help="L1..L5 or basic-base|basic-top|mid|lux-top|lux-base")
    g.add_argument("--school", action="store_true")
    g.add_argument("--patrol", choices=("p1", "p2"))
    g.add_argument("--recipe", help="any registered recipe key or alias")
    ap.add_argument("--out", default=None)
    ap.add_argument("--export", action="store_true",
                    help="write to assets/import/<category>/<letter> (the contract tree)")
    ap.add_argument("--name", default=None)
    ap.add_argument("--letter", default=None)
    ap.add_argument("--blockout", action="store_true")
    ap.add_argument("--finish", dest="finish", action="store_true", default=True)
    ap.add_argument("--no-finish", dest="finish", action="store_false")
    ap.add_argument("--lods", dest="lods", action="store_true", default=True)
    ap.add_argument("--no-lods", dest="lods", action="store_false")
    ap.add_argument("--no-validate", action="store_true")
    ap.add_argument("--tex", default="auto", choices=("auto", "1024", "2048", "4096"))
    ap.add_argument("--interior", default="name", choices=("name", "settings"))
    ap.add_argument("--tower", dest="tower", action="store_true", default=True)
    ap.add_argument("--no-tower", dest="tower", action="store_false")
    ap.add_argument("--roof-windows", default="win", choices=("win", "paint"))
    ap.add_argument("--preview", nargs="?", const="", default=None, metavar="DIR")
    ap.add_argument("--ab", default=None)
    ap.add_argument("--review", nargs="?", const="", default=None, metavar="DIR")
    ap.add_argument("--review-out", default=None)
    ap.add_argument("--ao-samples", type=int, default=AO_SAMPLES)
    ap.add_argument("--packer", default="calibrated", choices=("calibrated", "fraction"),
                    help="calibrated = cs2_bldg.pack_layout_calibrated around bake_unique "
                         "(default); fraction = the library cs2_uv packer unchanged")
    ap.add_argument("--weather-sweep", nargs="?", const=",".join(f"{s:.2f}" for s in WEATHER_SWEEP),
                    default=None, metavar="I,I,...",
                    type=lambda s: tuple(float(v) for v in s.split(",") if v.strip()),
                    help="art pass: also run the weathering at these intensities on the same "
                         "bake (recipe.weather required) and report the vanilla budget per "
                         "step + the highest budget-clean one (manifest weather_sweep); the "
                         "shipped maps keep the recipe's own intensity")
    ap.add_argument("--signset", action="store_true",
                    help="fine-detail pass: assert the recipe declares a sign set (it is built "
                         "whenever the recipe declares one)")
    ap.add_argument("--no-signset", action="store_true",
                    help="fine-detail pass: keep the recipe's labels in the host atlas")
    ap.add_argument("--no-fine", action="store_true",
                    help="fine-detail pass: build the recipe's pre-pass host (legacy fields)")
    ap.add_argument("--signset-out", default=None,
                    help="letter folder for the sign set (default: assets/import/signs/<letter> "
                         "with --export, else <out>/../signs/<letter>)")
    # T16 texture-parity probe (2026-10-16, docs/research/art/texture-parity-2026-10-16.md):
    # recipe.tiled (or this flag) = LOD0 on the shared tile sheet (cs2_tilesheet), LOD1/LOD2 on
    # an own "far" unique bake. Off = every build byte-identical to before.
    ap.add_argument("--tiled", action="store_true",
                    help="T16 probe: LOD0 tiled on the family sheet (sets recipe.tiled)")
    ap.add_argument("--sheet-tex", type=int, default=4096, choices=(2048, 4096),
                    help="tiled: family sheet size")
    ap.add_argument("--far-tex", type=int, default=1024, choices=(512, 1024, 2048),
                    help="tiled: LOD1/LOD2 own unique set (the --tex auto size in tiled mode)")
    # tiled ROLLOUT (2026-10-16, docs/research/art/texture-parity-rollout-2026-10-16.md): the eight
    # buildings share ONE family sheet (an owner folder beside the buildings); "probe" rebuilds T16
    ap.add_argument("--sheet-layout", default="rollout", choices=("rollout", "probe"),
                    help="tiled: rollout = shared family sheet + weathering; probe = the T16 probe")
    ap.add_argument("--sheet-dir", default=None,
                    help="tiled rollout: the family sheet owner folder (default <out>/SkiTileSheet<letter>)")
    ap.add_argument("--weather-level", default="auto",
                    help="tiled rollout: LOD0 weathering ladder level 0..5 or auto (highest detail "
                         "under the LOD0 cap); recipe.tiled_weather overrides")
    # window kit v2 (2026-10-17, docs/research/art/window-kit-2026-10-17.md): recessed glass in a
    # wall hole (LOD0 only), thin casing, sloped sill, sash + bead step, thin bars. Tiled builds only.
    ap.add_argument("--window-kit", default="v1", choices=("v1", "v2"),
                    help="v2 = window kit v2 (tiled builds only; v1 = every build byte-identical)")
    ap.add_argument("--window-mode", default="auto", choices=("auto", "full", "lean"),
                    help="kit v2: full = sash ring with the bead step; lean = no sash ring; auto = "
                         "WINDOW_MODE per recipe (the no-headroom buildings go lean)")
    ap.add_argument("--window-family", default="auto",
                    choices=("auto", "base", "glazing", "chalet", "utility"),
                    help="kit v2 family preset (auto = WINDOW_FAMILY per recipe)")
    ap.add_argument("--ppm-report", action="store_true",
                    help="manifest ppm_by_material + smooth_by_material (the T16 measure; "
                         "no effect on the maps)")
    return ap.parse_args(argv)


def select_recipe(args):
    recipes = R.load_recipes()
    if args.recipe:
        return R.get_recipe(args.recipe, recipes)
    if args.lodge:
        key = ALIASES_CLI.get(args.lodge, args.lodge)
        try:
            return R.get_recipe(key, recipes)
        except KeyError:
            return R.get_recipe(args.lodge, recipes)
    if args.school:
        return R.get_recipe("school", recipes)
    return R.get_recipe(args.patrol.upper() if args.patrol.upper() in recipes else args.patrol,
                        recipes)


def asset_name(recipe, letter, override=None, interior="name"):
    """`<asset><letter>`; validate_asset_name + lint; the _Win interior keyword must be in
    the FILE name on the name route (EC-sk M1)."""
    if interior == "settings":
        raise NotImplementedError(
            "--interior settings (the R19 probe route) needs a literal sharedAssets key in "
            "settings.json: cs2_asset.write_settings(extra_shared=...) is not built (N10); "
            "read Settings.cs:678-689 first. Use the default name route.")
    base = override or recipe.asset
    name = cs2_asset.validate_asset_name(base + letter)
    rep = cs2_validate._Report(name)
    cs2_validate.lint_asset_name(name, rep)
    if any(f[0] == "ERROR" for f in rep):
        raise RuntimeError(f"asset name {name!r} fails lint: {list(rep)}")
    if interior == "name" and recipe.interior not in name:
        raise RuntimeError(f"{name}: the interior keyword {recipe.interior!r} must be in the "
                           f"asset name (route 'name'; SettingsImporter matches the FILE name)")
    return name


def clear_scene():
    """lift_terminal.py:103-110 plus images (prefilter / bake leftovers)."""
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights,
                  bpy.data.images):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


# ======================================================================================
# Part building
# ======================================================================================

_SIDE_TAG = {"+X": "Xp", "-X": "Xn", "+Y": "Yp", "-Y": "Yn"}


def build_volumes(recipe):
    parts = []
    for v in recipe.volumes:
        z = v.z0
        n = len(v.storeys)
        for i, (h, mat) in enumerate(v.storeys):
            drop = []
            ground = v.ground and i == 0
            if i > 0 or (not ground and z > 0):
                drop.append("-Z")
            if i < n - 1 or v.roofed:
                drop.append("+Z")
            nm = v.name if i == 0 else f"{v.name}s{i}"
            parts += K.storey(nm, v.x0, v.x1, v.y0, v.y1, z, z + h, mat, ground=ground,
                              drop=tuple(drop), cut_z=tuple(c for c in v.cut_z if z < c < z + h))
            z += h
    return parts


def _call(fn, kwargs):
    f = getattr(K, fn, None)
    if f is None or not callable(f):
        raise KeyError(f"cs2_bldg has no builder {fn!r}")
    res = f(**kwargs)
    if isinstance(res, tuple) and len(res) == 2 and isinstance(res[0], list):
        return res[0]           # lantern -> (parts, point)
    return res


def build_roofs(recipe):
    parts = []
    for e in recipe.roofs:
        parts += _call(e.fn, dict(e.kwargs))
    return parts


def _volume(recipe, name):
    for v in recipe.volumes:
        if v.name == name:
            return v
    raise KeyError(f"facade rule names unknown volume {name!r}")


def _wall_of(v, side):
    """WallRef of one volume side with u0..u1 = the wall's span in u terms."""
    if side == "+X":
        return WallRef(side, v.x1, v.y0, v.y1)
    if side == "-X":
        return WallRef(side, v.x0, -v.y1, -v.y0)
    if side == "+Y":
        return WallRef(side, v.y1, -v.x1, -v.x0)
    if side == "-Y":
        return WallRef(side, v.y0, v.x0, v.x1)
    raise ValueError(side)


def layout_facade(rule, volume):
    """[(token, u0, u1)]: n = len(pattern) bays of rule.bay_w centred in the wall span
    minus `margin` each side; asserts bay >= win.w + 0.6 (W tokens) and fit."""
    wall = _wall_of(volume, rule.side)
    span0, span1 = wall.u0 + rule.margin, wall.u1 - rule.margin
    n = len(rule.pattern)
    total = n * rule.bay_w
    if total > (span1 - span0) + 1e-6:
        raise ValueError(f"facade {rule.volume}{rule.side}{rule.storey}: {n} bays x "
                         f"{rule.bay_w} = {total:.2f} m > span {span1 - span0:.2f}")
    if "W" in rule.pattern and rule.bay_w < rule.win.w + 0.6 - 1e-6:
        raise ValueError(f"facade {rule.volume}{rule.side}: bay {rule.bay_w} < window "
                         f"{rule.win.w} + 0.6")
    c = 0.5 * (span0 + span1)
    a = c - total / 2
    return [(tok, a + i * rule.bay_w, a + (i + 1) * rule.bay_w)
            for i, tok in enumerate(rule.pattern)]


def build_facades(recipe):
    parts = []
    for rule in recipe.facades:
        v = _volume(recipe, rule.volume)
        floor = v.z0 + sum(h for h, _ in v.storeys[:rule.storey])
        sh = v.storeys[rule.storey][0]
        wall = _wall_of(v, rule.side)
        base = f"{v.name}{_SIDE_TAG[rule.side]}{rule.storey}"
        for i, (tok, u0, u1) in enumerate(layout_facade(rule, v)):
            nm = f"{base}b{i}"
            room = (nm if rule.room == "bay" else base if rule.room == "row" else rule.room)
            if tok == "W":
                parts += K.window_bay(nm, wall, u0, u1, replace(rule.win, sill=floor + rule.win.sill),
                                      room=room)
            elif tok == "D":
                kw = dict(w=1.8, h=2.4, sidelights=0, lamps=2)
                kw.update(rule.door)
                parts += K.entry(nm, wall, 0.5 * (u0 + u1), z=floor, room=room, **kw)
            elif tok == "G":
                parts += K.curtain_wall(nm, wall, u0, u1, floor, floor + sh - 0.3, room=room)
            elif tok == "S":
                kw = dict(w=min(3.0, u1 - u0 - 0.2), h=0.8, z=floor + sh - 1.0)
                kw.update(rule.sign)
                parts += K.sign_board(nm, wall, 0.5 * (u0 + u1), **kw)
            elif tok == "B":
                continue
            else:
                raise ValueError(f"facade token {tok!r}")
    return parts


def build_extras(recipe, opts):
    parts = []
    for e in recipe.extras:
        if e.kwargs.get("_option") and not opts.get(e.kwargs["_option"], True):
            continue
        parts += _call(e.fn, {k: v for k, v in e.kwargs.items() if not k.startswith("_")})
    for i, kw in enumerate(recipe.lamps):
        kw = dict(kw)
        kw.setdefault("name", f"r{i}")
        lp, _pt = K.lantern(**kw)
        parts += lp
    return parts


def aframe_parts(c, opts):
    """L1 massing: base_lodge.build_lodge (:579-873) rebuilt on the kit, pre-yaw (entry at
    -X), every derived value from aframe_derived (25c). Section 13.1 port map + deltas,
    skeptic MUST-CHANGE 4 (porch slab a ground part, entry surround on the porch).
    Revision 1 (Richard's first live review 2026-10-11, verdicts item 12; L1 is the only
    `aframe` recipe): `KNEE_H` (bldg_recipes.aframe_knee; absent = 0 = the base_lodge port)
    is the vertical wall added under the eaves - every literal height above the old eave
    moves with it (roof windows, loft bands, clerestory, chimney top = RIDGE_Z + 1.16); the
    prow storey starts at the main +X wall (its -X face contact-culls against the main
    wall, bisected at the prow's y, instead of standing 15 m inside the hall); the porch
    roof runs back to the main wall over the cap ledge; DECK_X1 past the cap ring builds a
    `deck_ext` strip on `col_deckext` posts (closed, from GROUND_LIFT) + `beam_deckext` +
    `deckfascia_ext`; the deck slab runs 0.2 past DECK_Y (rail F rule); the side rails start
    at the wall once the eave clears them; `UMBRELLAS` / `RIBBON_TRANSOM` from the params.
    Ski racks are recipe extras (cs2_bldg.ski_rack(..., slots=))."""
    c = R.aframe_derived(c)
    KH = float(c.get("KNEE_H", 0.0))
    S1_Z, EAVE_Z = c["S1_Z"], c["EAVE_Z"]
    BX0, BX1, BY0, BY1 = c["BASE_X0"], c["BASE_X1"], c["BASE_Y0"], c["BASE_Y1"]
    S2X0, S2X1, RX, OVER = c["S2_X0"], c["S2_X1"], c["RIDGE_AT_X"], c["OVER"]
    RZ, MP, S2_Z = c["RIDGE_Z"], c["MAIN_PITCH"], c["S2_Z"]
    PX1, PY0, PY1 = c["PROW_X1"], c["PROW_Y0"], c["PROW_Y1"]
    PRY, PRZ, PP, S2P_Z = c["PROW_RIDGE_Y"], c["PROW_RIDGE"], c["PROW_PITCH"], c["S2P_Z"]
    DX1, DY, DTOP = c["DECK_X1"], c["DECK_Y"], c["DECK_TOP"]
    SILL, HEAD = c["SILL_Z"], c["HEAD_Z"]
    P = []

    # ---- stone base (ground part; the skirt is gone: the engine draws the base) --------
    P += K.storey("base", BX0, BX1, BY0, BY1, 0.0, S1_Z, "stone_rubble", ground=True,
                  drop=("+Z",))
    cap = K.box_part("plinthcap_base", (BX0 - 0.2, BY0 - 0.2, S1_Z - 0.13),
                     (BX1 + 0.2, BY1 + 0.2, S1_Z + 0.17), "concrete")
    K.bisect_part(cap[0][0], [((BX0, 0, 0), (1, 0, 0)), ((BX1, 0, 0), (1, 0, 0)),
                              ((0, BY0, 0), (0, 1, 0)), ((0, BY1, 0), (0, 1, 0))])
    P += cap
    # ---- timber storey (+ KNEE_H knee walls) + gable walls --------------------------------
    main = K.storey("main", S2X0, S2X1, BY0, BY1, S1_Z, S2_Z, "timber_lap", drop=("-Z", "+Z"),
                    cut_z=(S1_Z + 0.17, DTOP))
    K.bisect_part(main[0][0], [((0, PY0, 0), (0, 1, 0)), ((0, PY1, 0), (0, 1, 0))])
    P += main
    for gy, tag in ((-20.85, "S"), (20.85, "N")):
        P += K.prism_gable(f"main{tag}", "Y", gy - 0.15, gy + 0.15, S2X0, S2X1, S2_Z,
                           RZ + 0.05, ridge_at=RX)
    P += K.storey("prow", S2X1, PX1 - 0.15, PY0, PY1, S1_Z, S2P_Z, "timber_lap",
                  drop=("-Z", "+Z"), cut_z=(S1_Z + 0.17, DTOP))
    P += K.prism_gable("prow", "X", PX1 - 0.3, PX1, PY0, PY1, S2P_Z, PRZ - 0.02, ridge_at=PRY)
    # ---- roofs (standing seam, slot 3), ridge caps, rakes, fascia, tails ----------------
    P += K.roof_slab("mainW", "Y", -22.0, 22.0, S2X0 - OVER, EAVE_Z, RX + 0.35, RZ + 0.35 * MP,
                     wall_line=S2X0, mat="metal_roof")
    P += K.roof_slab("mainE", "Y", -22.0, 22.0, S2X1 + OVER, EAVE_Z, RX - 0.35, RZ + 0.35 * MP,
                     wall_line=S2X1, mat="metal_roof")
    P += K.box_part("ridge_main", (RX - 0.225, -0.75 - 21.35, RZ + 0.01),
                    (RX + 0.225, -0.75 + 21.35, RZ + 0.15), "galv")
    P += K.roof_slab("prowS", "X", -4.1, PX1 + 1.1, PY0 - 1.0, EAVE_Z, PRY + 0.35,
                     PRZ + 0.35 * PP, wall_line=PY0, mat="metal_roof")
    P += K.roof_slab("prowN", "X", -4.1, PX1 + 1.1, PY1 + 1.0, EAVE_Z, PRY - 0.35,
                     PRZ + 0.35 * PP, wall_line=PY1, mat="metal_roof")
    P += K.box_part("ridge_prow", (3.63 - 7.55, PRY - 0.225, PRZ + 0.01),
                    (3.63 + 7.55, PRY + 0.225, PRZ + 0.15), "galv")
    for gy, tag in ((-22.06, "S"), (22.06, "N")):
        P += K.roof_slab(f"W{tag}", "Y", gy - 0.06, gy + 0.06, S2X0 - OVER, EAVE_Z - 0.05, RX,
                         RZ - 0.02, thick=0.40, mat="trim", prefix="rake_")
        P += K.roof_slab(f"E{tag}", "Y", gy - 0.06, gy + 0.06, S2X1 + OVER, EAVE_Z - 0.05, RX,
                         RZ - 0.02, thick=0.40, mat="trim", prefix="rake_")
    px1 = PX1 + 1.16
    P += K.roof_slab("prowS", "X", px1 - 0.06, px1 + 0.06, PY0 - 1.0, EAVE_Z - 0.05, PRY,
                     PRZ - 0.02, thick=0.40, mat="trim", prefix="rake_")
    P += K.roof_slab("prowN", "X", px1 - 0.06, px1 + 0.06, PY1 + 1.0, EAVE_Z - 0.05, PRY,
                     PRZ - 0.02, thick=0.40, mat="trim", prefix="rake_")

    def cbox(cx, cy, cz, sx, sy, sz):
        return ((cx - sx / 2, cy - sy / 2, cz - sz / 2), (cx + sx / 2, cy + sy / 2, cz + sz / 2))
    for nm, (cx, cy, sx, sy) in (("slope", (S2X1 + OVER + 0.07, 0.0, 0.12, 44.1)),
                                 ("park", (S2X0 - OVER - 0.07, 0.0, 0.12, 44.1)),
                                 ("prowS", (7.65, PY0 - 1.07, 6.9, 0.12)),
                                 ("prowN", (7.65, PY1 + 1.07, 6.9, 0.12))):
        lo, hi = cbox(cx, cy, EAVE_Z - 0.06, sx, sy, 0.42)
        P += K.box_part(f"fascia_{nm}", lo, hi, "trim")
    ny = 17
    yy = [(-20.8 + i * 41.6 / (ny - 1)) for i in range(ny)]
    for nm, x in (("E", S2X1 + 0.55), ("W", S2X0 - 0.55)):
        P += K.boxes_part(f"tail_{nm}", [cbox(x, y, EAVE_Z + 0.50, 0.62, 0.12, 0.16) for y in yy],
                          "timber_heavy")
    xx = [(5.8 + i * 1.5) for i in range(4)]
    for nm, y in (("PS", PY0 - 0.55), ("PN", PY1 + 0.55)):
        P += K.boxes_part(f"tail_{nm}", [cbox(x, y, EAVE_Z + 0.36, 0.12, 0.62, 0.16) for x in xx],
                          "timber_heavy")
    # ---- sundeck + rails ------------------------------------------------------------------
    cap_x1 = BX1 + 0.2                      # cap ring edge
    dslab_y = DY + 0.2                      # slab 0.2 past the rail line (as rail F)
    deck_x1 = DX1 + 0.2
    P += K.deck("main", S2X1 - 0.2, min(deck_x1, cap_x1), -dslab_y, dslab_y, DTOP,
                support="solid", fascia=0.0, board_dir="X")
    if deck_x1 > cap_x1 + 0.05:             # revision 1: deeper deck on posts past the cap
        zu = DTOP - 0.16                    # slab underside
        P += K.deck("ext", cap_x1, deck_x1, -dslab_y, dslab_y, DTOP, support="none", fascia=0.0,
                    board_dir="X")
        fz = (zu - 0.14, DTOP - 0.02)       # deck() fascia band (0.30, 0.04 proud)
        P += K.boxes_part("deckfascia_ext", [
            ((deck_x1, -dslab_y - 0.04, fz[0]), (deck_x1 + 0.04, dslab_y + 0.04, fz[1])),
            ((cap_x1, -dslab_y - 0.04, fz[0]), (deck_x1, -dslab_y, fz[1])),
            ((cap_x1, dslab_y, fz[0]), (deck_x1, dslab_y + 0.04, fz[1]))], "trim")
        px = deck_x1 - 0.25
        P += K.beam("deckext", (px, -dslab_y + 0.12, zu - 0.13), (px, dslab_y - 0.12, zu - 0.13),
                    0.22, 0.26, mat="trim")
        ny = max(2, int(math.ceil(2.0 * (dslab_y - 0.25) / 4.3)))
        for k in range(ny + 1):
            py = -(dslab_y - 0.25) + 2.0 * (dslab_y - 0.25) * k / ny
            P += K.col(f"deckext{k}", px, py, 0.0, zu - 0.26, size=0.20, ground=False, mat="trim")
    gap = (DY + 1.5, DY + 3.0)              # rail F opening onto the stair landing (y 1.5..3.0)
    P += K.rail("F", [(DX1, -DY, DTOP), (DX1, DY, DTOP)], kind="timber", gaps=(gap,))
    # side rails: clear of the swept eave tip (25c), or from the wall once the eave clears them
    rx0 = S2X1 + 0.12 if EAVE_Z - 0.35 > DTOP + 1.10 + 0.5 else 4.6
    P += K.rail("S", [(rx0, -DY, DTOP), (DX1, -DY, DTOP)], kind="timber")
    P += K.rail("N", [(rx0, DY, DTOP), (DX1, DY, DTOP)], kind="timber")
    # ---- deck stair (A-Basin green steel): outside the cap / deck, landing over it -------
    sx0 = max(BX1 + 0.3, deck_x1 + 0.3)     # stair width 1.4 (base_lodge: 14.3, cap <= 14.2)
    sx1 = sx0 + 1.4
    scx = 0.5 * (sx0 + sx1)
    P += K.deck("landing", deck_x1, sx1 + 0.06, 1.5, 3.0, DTOP, support="none", fascia=0.0,
                board_dir="Y")
    for k, ly in enumerate((1.7, 2.8)):
        P += K.col(f"decklanding{k}", sx1 - 0.1, ly, 0.0, DTOP - 0.16, size=0.16, ground=True,
                   mat="galv")
    P += K.stair("deck", (scx, -5.8, 0.0), (scx, 1.5, DTOP), w=1.4, rise_max=0.18, rail="tube",
                 ground=True, rail_sides=(1,), tread_mat="deck", stringer_mat="trim")
    P += K.rail("landing", [(sx1 + 0.03, 1.5, DTOP), (sx1 + 0.03, 3.0, DTOP), (DX1, 3.0, DTOP)],
                kind="timber")
    P += K.rail("landingb", [(DX1, 1.5, DTOP), (sx0 - 0.05, 1.5, DTOP)], kind="timber")
    # ---- umbrellas (slot 2) -------------------------------------------------------------------
    # PARASOLS_VANILLA (L1, fifteenth look 2026-10-11, Richard: no house-made umbrellas): the
    # deck parasols are vanilla ParasolRandom01 sub-objects (bldg_recipes.L1_PARASOLS), none
    # modelled; params without the key keep the base_lodge port's modelled umbrellas
    umbrellas = () if c.get("PARASOLS_VANILLA") else (
        c.get("UMBRELLAS") or ((8.6, -17.0), (11.9, -12.7), (8.8, -9.9), (9.0, 13.2), (11.9, 17.2)))
    for k, (ux, uy) in enumerate(umbrellas):
        P += K.umbrella(f"deck{k}", ux, uy, DTOP)
    # ---- prow glass wall (A-Basin signature) -> `_Win` -----------------------------------
    bay = [-4.3, -1.78, 0.74, 3.26, 5.78, 8.3]
    prow = WallRef("+X", PX1)
    P += K.window_bay("prow", prow, bay[0], bay[-1],
                      WinSpec(bay[-1] - bay[0], 2.20, 4.85, lights=5, transom=5.95), room="prow")
    for i, (cy0, cy1, top) in enumerate(((bay[1], bay[2], 10.4 + KH), (bay[2], bay[3], 13.7 + KH),
                                         (bay[3], bay[4], 10.4 + KH))):
        P += K.pane_quad(f"win_cle{i}_prowcl{i}", prow, cy0, cy1, 7.65, top)
    P += K.wall_boxes("mull_prowcl", prow,
                      [(my - 0.07, my + 0.07, 7.35, top, -0.05, 0.15)
                       for my, top in ((bay[1], 10.5 + KH), (bay[2], 13.8 + KH),
                                       (bay[3], 13.8 + KH), (bay[4], 10.5 + KH))], "trim")
    if KH > 0.0:                            # the old side-pane head as a transom bar
        P += K.wall_boxes("mull_prowtr", prow, [(bay[1], bay[4], 10.34, 10.46, -0.05, 0.15)],
                          "trim")
    # ---- ribbon window bands (11, the base_lodge tuples) ----------------------------------
    tr = c.get("RIBBON_TRANSOM")
    bands_x = (("SlopeS", S2X1, +1, -18.0, -8.0, 5), ("SlopeN", S2X1, +1, 11.0, 19.0, 4),
               ("ParkS", S2X0, -1, -18.0, -10.0, 4), ("ParkC", S2X0, -1, -4.0, 4.0, 4),
               ("ParkN", S2X0, -1, 10.0, 18.0, 4))
    for tag, wx, sgn, y0, y1, lights in bands_x:
        wall = WallRef("+X" if sgn > 0 else "-X", wx)
        u0, u1 = (y0, y1) if sgn > 0 else (-y1, -y0)
        P += K.window_bay(tag, wall, u0, u1, WinSpec(u1 - u0, HEAD - SILL, SILL, lights=lights,
                                                     transom=tr), room=tag)
    lz0, lz1 = 8.0 + KH, 9.4 + KH           # loft windows in the gable triangles
    bands_y = (("EndS", BY0, -1, -11.5, -4.0, SILL, HEAD, 4), ("LoftS", BY0, -1, -6.5, -2.5, lz0, lz1, 2),
               ("EndN", BY1, +1, -11.5, -6.2, SILL, HEAD, 3), ("LoftN", BY1, +1, -9.2, -6.2, lz0, lz1, 2),
               ("ProwS", PY0, -1, 5.4, 9.6, SILL, HEAD, 3), ("ProwN", PY1, +1, 5.4, 9.6, SILL, HEAD, 3))
    for tag, wy, sgn, x0, x1, sill, head, lights in bands_y:
        wall = WallRef("+Y" if sgn > 0 else "-Y", wy)
        u0, u1 = (-x1, -x0) if sgn > 0 else (x0, x1)
        P += K.window_bay(tag, wall, u0, u1, WinSpec(u1 - u0, head - sill, sill, lights=lights,
                                                     transom=tr if sill == SILL else None),
                          room=tag)
    # ---- entry porch (arrival after the yaw) ---------------------------------------------
    P += K.box_part("step_porch1", (-18.4, -3.2, 0.0), (-17.8, 3.2, 0.18), "concrete", ground=True)
    P += K.box_part("step_porch2", (-17.8, -3.0, 0.0), (-17.4, 3.0, 0.36), "concrete", ground=True)
    P += K.box_part("step_porch3", (-17.4, -2.8, 0.0), (BX0, 2.8, 0.52), "concrete", ground=True)
    for py, tag in ((-1.9, "S"), (1.9, "N")):
        P += K.col(f"porch{tag}", -16.5, py, 0.52, 0.52 + 2.70, size=0.22)
    lo, hi = cbox(-16.5, 0.0, 3.16, 0.24, 4.0, 0.26)
    P += K.box_part("beam_porch", lo, hi, "timber_heavy")
    pb = S2X0                               # porch roof runs back to the main wall (rev. 1:
    #                                         the eave no longer covers the cap ledge)
    P += K.roof_slab("porchS", "X", -17.4, pb, -2.6, 2.85, 0.05, 5.295, thick=0.20,
                     mat="metal_roof")
    P += K.roof_slab("porchN", "X", -17.4, pb, 2.6, 2.85, -0.05, 5.295, thick=0.20,
                     mat="metal_roof")
    lo, hi = cbox(0.5 * (-17.405 + pb - 0.005), 0.0, 5.35, pb - 0.005 + 17.405, 0.40, 0.12)
    P += K.box_part("ridge_porch", lo, hi, "galv")
    P += K.prism_gable("porch", "X", -17.15, -16.85, -2.2, 2.2, 3.20, 5.22)
    for sgn, tag in ((-1, "S"), (1, "N")):
        P += K.roof_slab(f"porch{tag}", "X", -17.52, -17.40, sgn * 2.6, 2.78, 0.0, 5.18,
                         thick=0.30, mat="trim", prefix="rake_")
    P += K.entry("main", WallRef("-X", BX0), 0.0, w=2.6, h=2.5, sidelights=2, lamps=0,
                 room="entry", z=0.52)
    # fine-detail step 3a (PORCH_LAMPS_VANILLA in the params, L1 only): the two porch lanterns are
    # WallLightShortRangeRandom01 rows (recipes/lodge_aframe_fine.py); params without the key: as built
    for py, tag in (() if c.get("PORCH_LAMPS_VANILLA") else ((-1.9, "S"), (1.9, "N"))):
        lp, _pt = K.lantern(f"porch{tag}", (-16.61, py, 0.52 + 2.2), "-X", kind="wall")
        P += lp
    # ---- corner boards ---------------------------------------------------------------------------
    corners = ((S2X0, -21.0, S2_Z), (S2X0, 21.0, S2_Z), (S2X1, -21.0, S2_Z), (S2X1, 21.0, S2_Z),
               (PX1 - 0.08, PY0, S2P_Z), (PX1 - 0.08, PY1, S2P_Z))
    P += K.boxes_part("trim_corners", [cbox(cx, cy, 0.5 * (S1_Z + cz), 0.18, 0.18, cz - S1_Z)
                                       for cx, cy, cz in corners], "trim")
    # ---- chimney (shoulder = ground part 0..4.4, was -1.0) -----------------------------------
    P += K.chimney("north", RX, 21.55, 2.6, 1.8, RZ + 1.16, shoulder=(3.4, 2.4, S1_Z), ground=True)
    # ---- deltas: roof windows, ridge vents, snow guards, gutters, downspouts, lanterns -----
    spec = RoofSpec("gable", S2X0, S2X1, BY0, BY1, S2_Z, c["MAIN_PITCH_DEG"], axis="Y",
                    overhang=OVER, rake_over=1.0, ridge_at=RX, thick=0.24, mat="metal_roof")
    panes = opts.get("roof_windows", "win") == "win"
    skip = lambda y: abs(y - PRY) < 8.5      # noqa: E731  (E slab under the prow roof)
    for side, sl in (("-X", "W"), ("+X", "E")):
        for row, z, n in (("up", 12.5 + KH, 7), ("lo", 9.5 + KH, 6)):
            for i in range(n):
                y = -18.0 + 36.0 * i / (n - 1)
                if side == "+X" and skip(y):
                    continue
                P += K.roof_window(f"{sl}{row}{i}", spec, side, y, z, room=f"rw{sl}{row}",
                                   panes=panes)
    for i, y in enumerate((-16.0, -8.0, 0.0, 8.0, 16.0)):
        P += K.vent_stack(f"ridge{i}", spec, RX, y)
    P += K.snow_guard("W", spec, "-X")
    P += K.snow_guard("E", spec, "+X", spans=((-22.0, PRY - 8.5), (PRY + 8.5, 22.0)))
    gz = EAVE_Z - 0.10
    P += K.gutter("W", (S2X0 - OVER - 0.25, -22.0), (S2X0 - OVER - 0.25, 22.0), gz)
    P += K.gutter("E0", (S2X1 + OVER + 0.25, -22.0), (S2X1 + OVER + 0.25, PY0 - 1.2), gz)
    P += K.gutter("E1", (S2X1 + OVER + 0.25, PY1 + 1.2), (S2X1 + OVER + 0.25, 22.0), gz)
    P += K.gutter("prowS", (S2X1 + OVER + 0.3, PY0 - 1.25), (PX1 + 1.1, PY0 - 1.25), gz)
    P += K.gutter("prowN", (S2X1 + OVER + 0.3, PY1 + 1.25), (PX1 + 1.1, PY1 + 1.25), gz)
    wsx = BX0 - 0.30 if KH > 0.0 else BX0 - 0.06    # rev. 1: outside the cap ring (-13.2)
    for nm, x, y, nrm, zb in (("WS", wsx, -20.8, "-X", 0.05), ("WN", wsx, 20.8, "-X", 0.05),
                              ("ES", S2X1 + OVER + 0.25, -20.9, "+X", S1_Z + 0.22),
                              ("EN", S2X1 + OVER + 0.25, 20.9, "+X", S1_Z + 0.22)):
        gap_w = 0.0 if nm.startswith("E") else (S2X0 - OVER - 0.25) - x
        P += K.downspout(nm, x, y, gz, nrm, wall_gap=abs(gap_w) if gap_w else 0.02, z_bottom=zb)
    # detail pass 1 (Richard's second verdict 2026-10-11, DETAIL1 in the params; L1 only):
    # snow guards on the two prow slopes (the main slopes have them), and the prow gutters'
    # downspouts: outlet at the gutter end, an offset leader to the prow corner, down to the deck
    if c.get("DETAIL1"):
        sp = RoofSpec("gable", S2X1, PX1, PY0, PY1, S2P_Z, c["PROW_PITCH_DEG"], axis="X",
                      overhang=1.0, rake_over=0.0, ridge_at=PRY, thick=0.24, mat="metal_roof")
        span = ((S2X1 + OVER + 0.3, PX1 + 0.95),)
        P += K.snow_guard("prowS", sp, "-Y", spans=span)
        P += K.snow_guard("prowN", sp, "+Y", spans=span)
        for nm, gy, ly in (("PS", PY0 - 1.25, PY0 - 0.14), ("PN", PY1 + 1.25, PY1 + 0.14)):
            P += K.downspout(nm, PX1 + 0.95, gy, gz, "+X", z_bottom=DTOP + 0.05,
                             leader=(PX1 + 0.10, ly))
    # rail F lanterns on actual post positions (piece 1: y -20.5.., piece 2: y 3.0..)
    piece1, piece2 = gap[0], 2 * DY - gap[1]
    n1, n2 = max(1, int(round(piece1 / 2.05))), max(1, int(round(piece2 / 2.05)))
    lamp_y = [-DY + piece1 * 2 / n1, -DY + piece1 * 7 / n1, gap[1] - DY + piece2 * 2 / n2,
              gap[1] - DY + piece2 * 7 / n2]
    for k, ly in enumerate(lamp_y):
        lp, _pt = K.lantern(f"railF{k}", (DX1, ly, DTOP + 1.10), "+X", kind="post")
        P += lp
    return P


# window kit v2 mode per recipe key (window-kit doc section 5): "lean" drops the sash ring where
# the LOD0 cap leaves no headroom; anything not listed is "full"
WINDOW_MODE = {"L2": "lean", "L5": "lean"}
# head-board rule override per recipe ("family" = the family's own): L2 has 208 tris of LOD0
# headroom, so only its arrival facade gets the head board (the rest the U casing)
WINDOW_LINTEL = {"L2": "none"}
# extra flat-detail prefixes (cs2_tilesheet flat_prefixes: the material's mean colour, no tile
# cuts) that pay for kit v2 where the cap is tight: L2's timber guard rails (604 tris tiled)
WINDOW_FLAT_EXTRA = {}
# wall holes per recipe: "all" | "arrival" (the other facades get SURFACE openings: glass just
# proud of the wall inside a built-out casing). L5's 245 holes cost ~8.6 tris each tiled: over cap
WINDOW_HOLES = {"L5": "arrival"}
# window family per recipe (cs2_bldg.FAMILIES; window-kit doc section 3): big-lodge glazing,
# chalet / hut timber casings, utilitarian frames
WINDOW_FAMILY = {"L1": "glazing", "L4": "glazing", "L5": "glazing", "L2": "chalet", "L3": "chalet",
                 "school": "utility", "P1": "utility", "P2": "utility"}


def window_kit_cfg(recipe, args):
    """kwargs for cs2_bldg.window_kit, or None for kit v1. The arrival direction in the
    authoring frame BEFORE yaw: check_facing wants arrival = +X after apply_yaw(yaw)."""
    if getattr(args, "window_kit", "v1") != "v2":
        return None
    mode = getattr(args, "window_mode", "auto")
    if mode == "auto":
        mode = WINDOW_MODE.get(recipe.key, "full")
    a = math.radians(-recipe.yaw_deg)
    fam = getattr(args, "window_family", "auto")
    if fam == "auto":
        fam = WINDOW_FAMILY.get(recipe.key, "base")
    return {"version": 2, "mode": mode, "arrival": (math.cos(a), math.sin(a)), "family": fam,
            "lintel": WINDOW_LINTEL.get(recipe.key, "family"), "holes": WINDOW_HOLES.get(recipe.key, "all")}


def build_parts(recipe, opts):
    """Kit calls in recipe order; asserts unique part names (no Blender .001 renames).
    opts["window_kit"] (window_kit_cfg) builds the windows with kit v2."""
    wk = opts.get("window_kit")
    if wk:
        with K.window_kit(**wk):
            return _build_parts(recipe, opts)
    return _build_parts(recipe, opts)


def _build_parts(recipe, opts):
    if recipe.massing == "aframe":
        parts = aframe_parts(recipe.params or R.AFRAME, opts)
    elif recipe.massing == "custom":
        parts = list(recipe.builder(recipe, opts))
    elif recipe.massing == "volumes":
        parts = build_volumes(recipe) + build_roofs(recipe) + build_facades(recipe)
    else:
        raise ValueError(f"massing {recipe.massing!r}")
    if recipe.massing != "volumes":
        parts += build_roofs(recipe) + build_facades(recipe)
    parts += build_extras(recipe, opts)
    names = [o.name for o, _ in parts]
    dup = sorted({n for n in names if names.count(n) > 1})
    if dup or any("." in n for n in names):
        raise RuntimeError(f"part names not unique: {dup or [n for n in names if '.' in n]}")
    bad = [(o.name, m) for o, m in parts if m not in K.BLDG_MATERIALS]
    if bad:
        raise RuntimeError(f"unknown material keys: {bad[:6]}")
    return parts


def apply_yaw(obj, deg):
    """Rotation about Z + transform_apply (a rotation, never a mirror)."""
    if abs(deg) < 1e-9:
        return
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    obj.rotation_euler = (0.0, 0.0, math.radians(deg))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)


def part_weights(parts, recipe=None):
    """Texel-density weight per part: the kit rules (K.part_weights: WEIGHT_RULES, the
    LOD1-dressing cap, material density_w, a part's obj["w_override"]), then the recipe's
    `weights` ((prefix, w), ...; longest prefix wins) over the kit rules (detail pass 1,
    design 2.0 label weights / 5.2 P2 frames). Precedence: obj["w_override"] (an explicit
    per-part value, painted_board) > recipe.weights > kit rules. No recipe weights = the
    kit's dict unchanged."""
    w = K.part_weights(parts)
    rw = tuple(getattr(recipe, "weights", ()) or ())
    if not rw:
        return w
    table = {}
    for p, v in rw:
        if not isinstance(p, str) or not p or not (float(v) > 0.0):
            raise ValueError(f"recipe weights entry {(p, v)!r}: (non-empty prefix, w > 0)")
        table[p] = float(v)
    fixed = {o.name for o, _k in parts if "w_override" in o}
    for name in w:
        if name in fixed:
            continue
        hit = max((p for p in table if name.startswith(p)), key=len, default=None)
        if hit is not None:
            w[name] = table[hit]
    return w


def delete_lists(recipe, table):
    """(LOD1, LOD2) delete prefix tuples; lod2_keep re-protects stamped faces (their delete
    prefix is replaced by the exact names of the other parts it matched); ground parts are
    never deleted (asserted)."""
    d1 = tuple(K.LOD1_DELETE) + tuple(recipe.lod1_extra)
    d2 = list(K.LOD2_DELETE) + list(recipe.lod2_extra)
    for keep in recipe.lod2_keep:
        for p in [p for p in d2 if keep.startswith(p)]:
            d2.remove(p)
            d2 += [n for n in table if n.startswith(p) and n != keep and n not in d2]
    d2 = tuple(d2)
    for n in table:
        if n.startswith(tuple(recipe.ground_prefixes)) and (n.startswith(d1) or n.startswith(d2)):
            raise RuntimeError(f"ground part {n} is in an LOD delete list")
    return d1, d2


def plan_lod_targets(obj, table, recipe):
    d1, d2 = delete_lists(recipe, table)
    return K.plan_lod_targets(obj, table, recipe, delete_lists=(d1, d2),
                              protect_prefixes=recipe.ground_prefixes)


def area_report(obj, recipe):
    return K.area_report(obj)


# ======================================================================================
# Bake hook
# ======================================================================================

def make_post_compose(obj, recipe, dspecs):
    """cs2_bake post_compose hook (signature of lift_tower.py:859, used as in
    lift_terminal.py:401-431): detail relief, compose_maps (part noise, contact band in
    engine mode), recipe labels, then a flat normal in the uncovered gutter and
    renormalised covered texels."""
    def hook(base, nrm, msk, ctl, ao, mesh, A):
        t0 = time.perf_counter()
        hook.density = K.shipped_density(obj, A)
        table = json.loads(mesh[cs2_asset.PART_TABLE_PROP])
        part_map, ppm_map = cs2_compose.rasterise_face_fields(mesh, A)
        h = cs2_compose.detail_height(mesh, A, dspecs) if dspecs else None
        contact = K.contact_map(mesh, A) if recipe.base_mode == "engine" else None
        uncovered = nrm[..., 0:3].max(axis=-1) < 1e-3
        opts = {"part_map": part_map, "seed": 0}
        if contact is not None:
            opts["contact"] = contact
        base, nrm, msk, ctl = cs2_compose.compose_maps(base, nrm, msk, ctl, None, h, ppm_map, opts)
        hook.stamp_rects = []
        hook.label_info = {}
        for prefix, painter, kw in recipe.labels:
            p = K.make_painter(painter, kw, face=prefix)
            cs2_compose.stamp_on_face(base, ctl, mesh, table, prefix, p)
            # detail pass 1: the rect each label painted (= stamp_on_face's own rect) for the
            # weathering layer's protect_px (labels restored 1:1, G6), and the painter's
            # numbers (DisplayPainter.last: cap / stroke / tier) for the manifest
            r = cs2_compose.uv_bbox_px(mesh, table, prefix, A)
            if r is not None:
                hook.stamp_rects.append((prefix, tuple(int(v) for v in r)))
            if isinstance(getattr(p, "last", None), dict):
                hook.label_info[prefix] = p.last
        n = nrm[..., 0:3] * 2.0 - 1.0
        n[..., 2] = np.maximum(n[..., 2], 0.05)
        n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-6)
        nrm[..., 0:3] = n * 0.5 + 0.5
        nrm[uncovered, 0:3] = (0.5, 0.5, 1.0)
        nrm[..., 3] = 1.0
        hook.height_range = (float(h.min()), float(h.max())) if h is not None else None
        hook.seconds = time.perf_counter() - t0
        print(f"[lodge] compose: relief {hook.height_range}, labels {len(recipe.labels)}, "
              f"w=1 median {hook.density['median_w1']} px/m @ {A}")
        return base, nrm, msk, ctl
    hook.density = None
    hook.height_range = None
    hook.seconds = 0.0
    hook.stamp_rects = []
    hook.label_info = {}
    return hook


# ======================================================================================
# Weathering wiring (art pass, detail pass 1: design/detail-pass-1.md section 4, skeptic
# M3; build/weathering-layer.md section 8 + "Fix pass 1"; DECISIONS 171)
# ======================================================================================

WEATHER_SWEEP = (0.35, 0.40, 0.45, 0.50, 0.55, 0.60, 0.70)


def weather_spec(recipe, name, intensity=None):
    """cs2_weather.WeatherSpec of recipe.weather (plain data): any WeatherSpec field
    (intensity, ground, snow, slot_wear, edge_band_m ...) + "overrides" ((prefix,
    {PartWeather kwargs}), ...). ground defaults to "plinth" for a plinth-mode recipe (L2:
    its below-grade base keeps its weathered stone, DECISIONS 171), else "grade". Fields
    left out take the cs2_weather defaults (DECISIONS 171: intensity 0.40, slot_wear
    "free", snow 0.0)."""
    import cs2_weather as CW
    import dataclasses
    cfg = dict(recipe.weather)
    ov = tuple((str(p), CW.PartWeather(**dict(kw))) for p, kw in cfg.pop("overrides", ()))
    allowed = {f.name for f in dataclasses.fields(CW.WeatherSpec)} - {
        "name", "kind", "overrides", "mat_roles_extra"}
    bad = sorted(set(cfg) - allowed)
    if bad:
        raise KeyError(f"{recipe.key}: recipe.weather keys {bad} are not WeatherSpec fields")
    cfg.setdefault("ground", "plinth" if recipe.base_mode == "plinth" else "grade")
    if intensity is not None:
        cfg["intensity"] = float(intensity)
    return CW.WeatherSpec.preset("building", name, overrides=ov, **cfg)


def weather_mat_roles(keys):
    """{material index: weathering role} by INDEX (= position in sorted(keys), the order
    cs2_bake.material_sources assigned and bake_unique keeps on every polygon; the
    placeholder NAMES are gone by the time the hook runs): cs2_weather.MAT_ROLES +
    cs2_bldg.WEATHER_ROLES_EXTRA; a key with no role raises (no silent default)."""
    import cs2_weather as CW
    out = {}
    for i, k in enumerate(sorted(keys)):
        r = K.WEATHER_ROLES_EXTRA.get(k, CW.MAT_ROLES.get(k))
        if r is None:
            raise KeyError(f"material key {k!r} has no weathering role (cs2_weather.MAT_ROLES "
                           f"or cs2_bldg.WEATHER_ROLES_EXTRA)")
        out[i] = r
    return out


def wrap_weather(hook, recipe, name, keys, dspecs, sweep=None):
    """The art-pass weathering around the compose hook (W12): recipe.weather None returns
    `hook` itself (the old path, byte-identical). Otherwise a cs2_weather.WeatherHook with
    mat_roles by material index, the label rects the inner hook stamped as protect_px
    (restored 1:1), compose_contact in engine mode (compose_maps already darkened the
    contact band). hook.density / height_range / seconds / stamp_rects / label_info read
    through (WeatherHook.__getattr__). `sweep` = intensities: the hook also runs the layer
    at each on the same inner maps and keeps the vanilla-budget rows (hook.sweep)."""
    if recipe.weather is None:
        return hook
    import cs2_weather as CW
    spec = weather_spec(recipe, name)
    roles = weather_mat_roles(keys)

    def protect(_mesh, _A):
        return [r for _p, r in (hook.stamp_rects or ())]
    kw = dict(mat_roles=roles, detail_specs=dspecs, protect_px=protect,
              compose_contact=recipe.base_mode == "engine")
    if sweep:
        return _SweepWeatherHook(hook, spec, tuple(float(s) for s in sweep), **kw)
    return CW.make_weather_hook(hook, spec, **kw)


def _sweep_class():
    import cs2_weather as CW

    class SweepWeatherHook(CW.WeatherHook):
        """WeatherHook that, after its own pass (whose maps the bake keeps), runs the layer
        again on the SAME inner maps at every sweep intensity (fresh fields + masks each, as
        tools_weather_demo's ladder) and records the vanilla budget (visible texels, D23)
        and the perceptual delta per step: hook.sweep = {"rows": [...], "highest_clean":
        the highest step with every step up to it budget-clean}."""

        def __init__(self, inner, spec, steps, **kw):
            super().__init__(inner, spec, **kw)
            self.steps = steps
            self.sweep = None

        def __call__(self, base, nrm, msk, ctl, ao, mesh, A):
            inner = self.inner
            box = {}

            def capture(b, n, m, c, a, me, AA):
                b, n, m, c = inner(b, n, m, c, a, me, AA)
                box["maps"] = (np.array(b, np.float32), np.array(n, np.float32),
                               np.array(m, np.float32))
                return b, n, m, c
            self.inner = capture
            try:
                out = super().__call__(base, nrm, msk, ctl, ao, mesh, A)
            finally:
                self.inner = inner
            b0, n0, m0 = box["maps"]
            maps = CW.WeatherMaps(b0, n0, m0, ctl, ao)
            rows = []
            for s in self.steps:
                sp = replace(self.spec, intensity=float(s)).validate()
                fl = CW.build_fields(mesh, A, sp, maps, mat_keys=self.mat_keys,
                                     mat_roles=self.mat_roles, detail_specs=self.detail_specs,
                                     stamped=self.stamped, reserved=self.reserved,
                                     protect_px=self.protect_px)
                mk = CW.compute_masks(maps, fl, sp, compose_contact=self.compose_contact)
                o = CW.apply_weathering(maps, fl, sp, compose_contact=self.compose_contact,
                                        gutter_px=self.gutter_px, masks=mk)
                rep = CW.weather_report(maps, o, fl, sp, mk)
                v, pd = rep["vanilla"], rep["perceptual"]
                rows.append({"intensity": float(s), "ok": bool(v["ok"]), "failed": v["failed"],
                             "VL1": v["VL1_mean_rel_dL"], "VL2": v["VL2_share_rel_dL_gt_0.15"],
                             "VL3": v["VL3_mean_dsmooth"], "VL4": v["VL4_max"],
                             "VL1_all_covered": v["VL1_all_covered"],
                             "dLstar_mean": pd["mean_abs_dLstar"],
                             "dLstar_gt2": pd["share_dLstar_gt_2"]})
                print(f"[weather-sweep] {self.spec.name} @ {s:.2f}: ok {v['ok']} VL1 "
                      f"{v['VL1_mean_rel_dL']:.3f} VL2 {v['VL2_share_rel_dL_gt_0.15']:.3f} VL4 "
                      f"{v['VL4_max']:.2f} dL* {pd['mean_abs_dLstar']:.2f} / "
                      f"{pd['share_dLstar_gt_2']:.3f}")
                del fl, mk, o
            best = None
            for r in sorted(rows, key=lambda r: r["intensity"]):
                if not r["ok"]:
                    break
                best = r["intensity"]
            self.sweep = {"slot_wear": self.spec.slot_wear, "snow": self.spec.snow,
                          "ground": self.spec.ground, "rows": rows, "highest_clean": best}
            print(f"[weather-sweep] {self.spec.name}: highest budget-clean step {best} "
                  f"({self.spec.slot_wear} slots)")
            return out
    return SweepWeatherHook


def _SweepWeatherHook(inner, spec, steps, **kw):
    return _sweep_class()(inner, spec, steps, **kw)


SUB_OBJECT_NOTE = ("ObjectSubObjects on the hand-made prefab: m_GroupIndex 0, m_Probability "
                   "100; m_ParentMesh per object (-1 = on the lot, 0 = attached to the main "
                   "mesh: wall lights, deck props); GUIDs = the variant family's placeholder "
                   "(design/detail-pass-1.md 1.6), whitelisted in tools/preflight.ps1 at the "
                   "surgery round")


_VERIFIED_GUIDS = None


def verified_guids():
    """(allowed GUIDs, rejected {guid: why}) for building sub-object rows (fine-detail step 2
    check item 16 / step 3a): allowed = the dump-verified placeholders of cs2_liftdress.SUBOBJ
    plus every GUID of subobj_bounds.json (cs2_subobj's cache, derived from the dump); rejected =
    cs2_liftdress.REJECTED_GUIDS (mixed-spawn placeholders), never allowed. Read once per process."""
    global _VERIFIED_GUIDS
    if _VERIFIED_GUIDS is None:
        import cs2_liftdress as LD
        with open(os.path.join(_HERE, "subobj_bounds.json"), encoding="utf-8") as fh:
            cache = json.load(fh).get("props", {})
        allowed = {str(s["guid"]) for s in LD.SUBOBJ.values()}
        allowed |= {str(v["guid"]) for v in cache.values() if v.get("guid")}
        _VERIFIED_GUIDS = (frozenset(allowed - set(LD.REJECTED_GUIDS)), dict(LD.REJECTED_GUIDS))
    return _VERIFIED_GUIDS


def sub_objects_block(recipe):
    """The manifest's sub_objects block from recipe.sub_objects (replaces school.py
    --merge-manifest: a re-export no longer drops it). Entries (prefab, guid, (gx, gy, gz),
    (x, y, z, w), parent) or dicts with m_Object / prefab / position / rotation
    [/ m_ParentMesh, default -1]; per-object keys m_Object, prefab, position, rotation,
    m_ParentMesh (skeptic S4). Validated: 32-hex GUID, unit quaternion, parent >= -1; the GUID
    must be dump-verified (cs2_liftdress.SUBOBJ or subobj_bounds.json) and never a rejected
    mixed-spawn placeholder (cs2_liftdress.REJECTED_GUIDS): both raise (verified_guids)."""
    objs = []
    for e in recipe.sub_objects:
        if isinstance(e, dict) and e.get("kind") == "mod":
            objs.append(_mod_row_block(recipe, e))
            continue
        if isinstance(e, dict):
            prefab, guid = e["prefab"], e["m_Object"]
            pos, q, parent = e["position"], e["rotation"], e.get("m_ParentMesh", -1)
        else:
            prefab, guid, pos, q, parent = e
        guid = str(guid)
        if len(guid) != 32 or any(c not in "0123456789abcdef" for c in guid):
            raise ValueError(f"{recipe.key}: sub-object {prefab!r} GUID {guid!r} (32 hex)")
        allowed, rejected = verified_guids()
        if guid in rejected:
            raise ValueError(f"{recipe.key}: sub-object {prefab!r} uses the REJECTED mixed-spawn "
                             f"placeholder GUID {guid} ({rejected[guid]})")
        if guid not in allowed:
            raise ValueError(f"{recipe.key}: sub-object {prefab!r} GUID {guid} is not dump-verified "
                             f"(neither cs2_liftdress.SUBOBJ nor subobj_bounds.json holds it)")
        if len(pos) != 3 or len(q) != 4:
            raise ValueError(f"{recipe.key}: sub-object {prefab!r} position / rotation shape")
        nq = math.sqrt(sum(float(c) ** 2 for c in q))
        if abs(nq - 1.0) > 1e-3:
            raise ValueError(f"{recipe.key}: sub-object {prefab!r} rotation |q| = {nq:.4f}")
        if int(parent) < -1:
            raise ValueError(f"{recipe.key}: sub-object {prefab!r} m_ParentMesh {parent}")
        # x-sign gate N (2026-10-15 playtest; S1b-N 2026-10-16): the recipes DESIGN their rows in the old frame
        # (gx = +authoring y, the frame their box / mesh contracts test in); the manifest gets rule T (x -> -x,
        # q -> (qx, -qy, -qz, qw)), the game frame
        gpos, gq = FD.rule_t_pos(pos), FD.rule_t_rot(q)
        objs.append({"m_Object": guid, "prefab": str(prefab),
                     "position": [round(float(v), 4) + 0.0 for v in gpos],
                     "rotation": [round(float(v), 7) + 0.0 for v in gq],
                     "m_ParentMesh": int(parent)})
    if isinstance(recipe, FD.FineRecipe):
        FD.check_kit_colours(recipe)        # one colour set per building (kit colour variants), after the row checks
    block = {"target_prefab": recipe.prefab,
             "frame": "game local (gx, gy, gz) = (-y, z, x) of authoring, gx NEGATED (x-sign gate N, 2026-10-15); "
                      "rotation (x, y, z, w); recipes design rows in the old +y frame, written through rule T "
                      "(fine_detail.design_row_to_game)",
             "notes": SUB_OBJECT_NOTE, "objects": objs}
    if isinstance(recipe, FD.FineRecipe):
        block["x_convention"] = FD.X_CONVENTION
        block["kinds"] = {"vanilla": sum(1 for o in objs if o.get("kind") != "mod"),
                          "mod": sum(1 for o in objs if o.get("kind") == "mod")}
    return block


def _mod_row_block(recipe, e):
    """One "kind": "mod" row (fine_detail.kit_row / kit_lean_row / sign_set_row): our own prop by
    object-prefab CID -> surgery $fstrref:"CID:<m_Object>" (the hosted-tower / beacon pattern)."""
    cid = str(e["m_Object"])
    if len(cid) != 32 or any(c not in "0123456789abcdef" for c in cid):
        raise ValueError(f"{recipe.key}: mod row {e.get('prefab')!r} CID {cid!r} (32 hex)")
    kit = FD.KIT.get(str(e.get("prefab")))
    # kit colour variants (2026-10-12): a coloured kit prop names its colour-set VARIANT's CID
    # (fine_detail.row_colour_error), an uncoloured one the staged KIT CID
    if e.get("role") != "signset" and (kit is None or FD.row_colour_error(e)):
        raise ValueError(f"{recipe.key}: mod row {e.get('prefab')!r} CID {cid} is neither a dressing-kit "
                         f"object prefab (fine_detail.KIT / its colour variant: "
                         f"{FD.row_colour_error(e) if kit else 'not in KIT'}) nor a sign-set identity row")
    pos, q, parent = e["position"], e["rotation"], int(e.get("m_ParentMesh", -1))
    if len(pos) != 3 or len(q) != 4:
        raise ValueError(f"{recipe.key}: mod row {e.get('prefab')!r} position / rotation shape")
    nq = math.sqrt(sum(float(c) ** 2 for c in q))
    if abs(nq - 1.0) > 1e-3 or parent not in (-1, 0):
        raise ValueError(f"{recipe.key}: mod row {e.get('prefab')!r} |q| {nq:.4f} parent {parent}")
    g = FD.design_row_to_game(e)          # rule T (x-sign gate N): position, rotation and a lean row's wall x / normal x
    out = {"kind": "mod", "ref": "CID", "m_Object": cid, "prefab": str(e["prefab"]),
           "mesh": str(e.get("mesh")), "position": [round(float(v), 4) + 0.0 for v in g["position"]],
           "rotation": [round(float(v), 7) + 0.0 for v in g["rotation"]], "m_ParentMesh": parent}
    for k in ("role", "note", "lean", "bounds", "colour_set", "variant"):
        if e.get(k) is not None:
            out[k] = g[k] if k == "lean" else e[k]
    return out


def pack_margin(A, n_islands, packer="calibrated"):
    """Pack / bake margin in px: the contract's max(16, A // 64) (cs2_uv.py:68-70, mip-4
    safe) up to 300 islands; 8 px (mip-3 safe) for building kits beyond that. Measured on
    L1 (2048, ~1,000-1,300 islands, Blender 5.2.2, 2026-10-11): with the library FRACTION
    packer the w = 1 median is 15.9 px/m at 4 px, 14.9 at 8, 8.5 at 16, and at 32 px it
    stops scaling (UVUnique up to 124, the bake raises); with the calibrated ADD packer
    (cs2_bldg.pack_layout_calibrated) 28.1 at 4, 23.7 at 8, 16.0 at 16."""
    m = max(16, int(A) // 64)
    if n_islands > 300:
        return min(m, 8)
    return m


def dry_pack_density(obj, A, margin, packer):
    """Pack a COPY of `obj` from UVTile exactly as bake_unique will and return
    cs2_bldg.shipped_density (the prefilter target: k 0.48 was calibrated on BaseStationL's
    few smart-project islands and does not transfer to building kits)."""
    tmp = obj.copy()
    tmp.data = obj.data.copy()
    tmp.name = obj.name + "DryPack"
    bpy.context.scene.collection.objects.link(tmp)
    try:
        tmp.data.uv_layers.active = tmp.data.uv_layers[cs2_bake.TILE_LAYER]
        with (K.calibrated_packer() if packer == "calibrated" else _NullCtx()):
            cs2_uv.pack_unique(tmp, A, [], margin_px=margin)
        return K.shipped_density(tmp, A)
    finally:
        me = tmp.data
        bpy.data.objects.remove(tmp, do_unlink=True)
        bpy.data.meshes.remove(me)


class _NullCtx:
    def __enter__(self):
        return self

    def __exit__(self, *exc):
        return False


def _final_sources(spec_table, keys):
    """{mat_index: SourceSet} for the bake from the prefiltered specs (index = position in
    sorted keys, as cs2_bake.material_sources assigned it)."""
    out = {}
    for i, k in enumerate(sorted(keys)):
        ss = cs2_bake._resolve_spec(cs2_bake._as_material_spec(spec_table[k], k), k)
        out[i] = replace(ss, uv=cs2_bake.TILE_LAYER)
    return out


def mesh_hash(obj, layers=None):
    """sha1 (12 hex) of vertex coords (%.5f), face vertex lists, the given UV layers
    (%.6f, default all), material indices and the part / density_w face attributes - the
    test_terminal_determinism pattern extended to every order-dependent channel."""
    import hashlib
    me = obj.data
    h = hashlib.sha1()
    co = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get("co", co)
    h.update(" ".join("%.5f" % v for v in co).encode())
    for p in me.polygons:
        h.update((",".join(str(i) for i in p.vertices) + ";").encode())
    for lay in sorted(me.uv_layers, key=lambda l: l.name):
        if layers is not None and lay.name not in layers:
            continue
        uv = np.empty(len(lay.data) * 2, np.float32)
        lay.data.foreach_get("uv", uv)
        h.update(lay.name.encode() + " ".join("%.6f" % v for v in uv).encode())
    mi = np.empty(len(me.polygons), np.int32)
    me.polygons.foreach_get("material_index", mi)
    h.update(mi.tobytes())
    for an in ("part", "density_w"):
        a = me.attributes.get(an)
        if a is not None:
            buf = np.empty(len(a.data), np.int32 if a.data_type == "INT" else np.float32)
            a.data.foreach_get("value", buf)
            h.update(an.encode() + buf.tobytes())
    return h.hexdigest()[:12]


def _git_sha():
    try:
        return subprocess.run(["git", "-C", REPO, "rev-parse", "--short", "HEAD"],
                              capture_output=True, text=True, timeout=10).stdout.strip() or None
    except (OSError, subprocess.SubprocessError):
        return None


def _game_extent(obj):
    W = K._world_coords(obj)
    lo, hi = W.min(0), W.max(0)
    # post-orient Blender (x, y, z) -> game (x, z, -y)
    return {"x": round(float(hi[0] - lo[0]), 2), "y": round(float(hi[2] - lo[2]), 2),
            "z": round(float(hi[1] - lo[1]), 2), "min_y": round(float(lo[2]), 3)}


# ======================================================================================
# Run
# ======================================================================================

def remove_stale_maps(asset_dir, name, written):
    """Fine-detail step 3d (the engine gap of build/fine-detail-step3a-buildings.md section 1): delete
    every texture MAP of asset `name` in `asset_dir` that this build did not write. A map is a PNG that
    cs2_validate.classify_file reads as the asset's own "map" (`<name>[_LOD<k>]_<BaseColor|Normal|
    MaskMap|ControlMask>.png`) or "emissive" (`<name>_Emissive*.png`); `written` = the bake's file
    names (cs2_bake.bake_unique report "files"). Anything else in the folder (FBXs, settings.json, the
    manifest, another asset's files, stray PNGs) is never touched. settings.json is rewritten from the
    build's own arguments afterwards (it never names an Emissive map) and the manifest's "files" block
    hashes the folder as it is then, so both drop the removed maps. Returns the sorted removed names."""
    keep = {os.path.basename(str(f)) for f in written}
    removed = []
    if not os.path.isdir(asset_dir):
        return removed
    for f in sorted(os.listdir(asset_dir)):
        p = os.path.join(asset_dir, f)
        if f in keep or not os.path.isfile(p) or not f.lower().endswith(".png"):
            continue
        kind = cs2_validate.classify_file(f, name)[0]
        if kind in ("map", "emissive"):
            os.remove(p)
            removed.append(f)
    for f in removed:
        print(f"[lodge] {name}: removed the stale {f} (this build does not write it)")
    return removed


def _we_boards(parts, recipe, spec):
    """Nitpick C (2026-10-13): the Write Everywhere targets of a build - every host label painted by a
    fine_detail.blank_board (role "blank", drawn in the host atlas) and every sign-set face whose sheet kind is
    lit (role "name") or painted blank (role "blank") - each as cs2_signset.face_record (centre / normal / right
    / up in the game frame, size). Read from the parts BEFORE the sign set routes its faces out. [] when none
    (plain Recipes, hosts without such boards: the manifest then has no `we_boards` key)."""
    if not isinstance(recipe, FD.FineRecipe):
        return []
    import cs2_signset
    by = {o.name: o for o, _k in parts}
    out = []
    for e in recipe.labels:
        if FD.is_blank_label(e) and e[0] in by:
            blank = next(d.we_blank for d in e[2]["draws"] if getattr(d, "we_blank", None) is not None)
            out.append({"face": e[0], "role": "blank", "drawn_by": "host atlas", "blank": blank,
                        **cs2_signset.face_record(by[e[0]], recipe.yaw_deg)})
    if spec is not None:
        for face, key in spec.faces:
            k = cs2_signset.kind(key)
            blank = any(d == "blank" for d, _kw in k.draws)
            if (k.lit or blank) and face in by:
                out.append({"face": face, "role": "blank" if blank else "name", "sheet_kind": key,
                            "drawn_by": f"sign set {spec.mesh}",
                            **cs2_signset.face_record(by[face], recipe.yaw_deg)})
    return out


def _out_dir(recipe, args, letter):
    if args.out:
        return os.path.abspath(args.out)
    if args.export:
        return os.path.join(IMPORT_ROOT, recipe.category, letter)
    return os.path.join(tempfile.gettempdir(), "lodge", recipe.category, letter)


def run(recipe, args):
    """The section-1 stage table. Returns the result dict."""
    if not bpy.app.background:
        raise RuntimeError("lodge.py runs headless only (ROUND-SPEC R10)")
    # fine-detail pass (opt-in): a plain Recipe passes through unchanged
    recipe = FD.effective(recipe, fine=not getattr(args, "no_fine", False),
                          signset=not getattr(args, "no_signset", False))
    if getattr(args, "tiled", False):
        recipe = replace(recipe, tiled=True)
    tiled = bool(getattr(recipe, "tiled", False)) and args.finish and not args.blockout
    if getattr(args, "signset", False) and FD.sign_set_of(recipe) is None:
        raise RuntimeError(f"{recipe.key}: --signset but the recipe declares no sign set "
                           f"(or --no-signset / --no-fine dropped it)")
    T = {}
    t0 = time.perf_counter()
    letter = args.letter or (os.path.basename(os.path.normpath(args.out)) if args.out else recipe.letter)
    name = asset_name(recipe, letter, args.name, args.interior)
    out = _out_dir(recipe, args, letter)
    asset_dir = os.path.join(out, name)
    opts = {"tower": args.tower, "roof_windows": args.roof_windows}
    wk = window_kit_cfg(recipe, args)
    if wk:
        if not tiled and not args.blockout:
            raise RuntimeError("--window-kit v2 needs a tiled build (the wall holes are cut on the "
                               "tiled LOD0 copy only; the far set keeps whole walls)")
        opts["window_kit"] = wk
    W_, D_ = recipe.lot
    # 1-2 clean + parts
    clear_scene()
    parts = build_parts(recipe, opts)
    spec = FD.sign_set_of(recipe)
    # nitpick C (2026-10-13): the Write Everywhere targets (blank boards, lit name boards), read off the faces
    # before a sign set takes them; the manifest's `we_boards` block (absent when there are none)
    we_boards = _we_boards(parts, recipe, spec)
    signset = None
    if spec is not None:
        import cs2_signset
        parts, routed = cs2_signset.route(parts, spec)
        cid = cs2_signset.mint_cid(spec) if args.export else (cs2_signset.staged_cid(spec)
                                                              or cs2_signset.PLACEHOLDER_CID)
        sobj = cs2_signset.build_sign_set(routed, spec, yaw_deg=recipe.yaw_deg)
        row = FD.sign_set_row(spec, cid, bounds=cs2_signset.bounds_game(sobj))
        recipe = replace(cs2_signset.host_recipe(recipe, spec),
                         sub_objects=tuple(recipe.sub_objects) + (row,))
        signset = {"spec": spec, "cid": cid, "obj": sobj, "faces": [o.name for o, _k in routed]}
        for o, _k in routed:                    # the quads now live in the sign set only
            me = o.data
            bpy.data.objects.remove(o, do_unlink=True)
            bpy.data.meshes.remove(me)
    keys = sorted({k for _o, k in parts})
    T["build"] = time.perf_counter() - t0
    # 3 bevel, 4 density, 5 tags
    n_bev = K.bevel_parts(parts)
    weights = part_weights(parts, recipe)
    cs2_uv.set_density(parts, weights)
    table = cs2_asset.tag_parts(parts)
    dspecs = K.detail_specs(parts)
    # 6 materials: strip UV layers (MUST-CHANGE 1), UVTile + placeholder slots, retile
    K.strip_uv_layers(parts)
    proj_specs = K.projection_specs(keys)              # projection only (UVTile, tile_sq)
    cs2_bake.material_sources(proj_specs, parts)
    retiled = K.retile(parts, proj_specs)
    # 7 join + weld + table
    bpy.ops.object.select_all(action="DESELECT")
    for o, _m in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]
    bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    obj.data.name = name
    if obj.name != name:
        raise RuntimeError(f"joined object got {obj.name!r}")
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=WELD_M)
    bpy.ops.object.mode_set(mode="OBJECT")
    cs2_asset.store_part_table(obj, table)
    # 8 shade
    cs2_asset.shade_by_angle(obj, SHADE_ANGLE, weighted=True)
    T["parts"] = time.perf_counter() - t0
    # 9-10 yaw, contracts in the authoring frame (MUST-CHANGE 2), orient
    apply_yaw(obj, recipe.yaw_deg)
    lot = K.check_lot_fit(obj, W_, D_)
    facing = K.check_facing(obj, table, recipe.arrival_prefixes, D_)
    cs2_asset.orient_for_net(obj)
    base0 = K.check_base_mode(obj, table, recipe.base_mode, recipe.ground_prefixes, recipe.plinth)
    # 11 rooms (before the bake: no pane texels)
    win, cells = K.split_rooms(obj, table, recipe.rooms, name=name)
    # 12 cull (after the rooms so panes are not occluders)
    cull = K.cull_hidden(obj, protect=tuple(recipe.ground_prefixes) + K.LABEL_PREFIXES
                         + (K.KIT2_RECESSED if wk else ()),
                         table=table, cull_ok=recipe.cull_ok, wall_rule=recipe.wall_rule,
                         arrival=(0.0, -1.0), ground_prefixes=recipe.ground_prefixes)
    T["cull"] = time.perf_counter() - t0
    # 13 budget, 14 area report
    n0 = K.assert_budget(obj, recipe.budget, 0)
    area = area_report(obj, recipe)
    A = recipe.tex if args.tex == "auto" else int(args.tex)
    if tiled and args.tex == "auto":
        A = int(getattr(args, "far_tex", 1024))
    base_ppm = area["predicted_ppm_w1"].get(A) or (0.48 * A / math.sqrt(max(area["sum_w2a"], 1)))
    print(f"[lodge] {name}: LOD0 {n0} tris, kept {area['kept_m2']} m2, sum w2a "
          f"{area['sum_w2a']}, predicted w=1 px/m {area['predicted_ppm_w1']}")
    result = dict(name=name, asset_dir=asset_dir, recipe=recipe.key, tris={0: n0}, cells=cells,
                  hashes={"LOD0_prebake": mesh_hash(obj), "Win": mesh_hash(win) if win else None},
                  lot=lot, facing=facing, base={0: base0}, cull=cull, area=area, bevelled=n_bev,
                  retiled=len(retiled), keys=keys, parts=len(table))
    if signset is not None:
        result["sign_set"] = {"obj": signset["obj"], "cid": signset["cid"], "faces": signset["faces"],
                              "mesh": signset["spec"].mesh}
    if we_boards:
        result["we_boards"] = we_boards
    os.makedirs(asset_dir, exist_ok=True)
    if args.blockout:
        result["finished"] = False
        _write_manifest(asset_dir, name, recipe, args, result, None, None, T, t0)
        print(f"PASS lodge: {name} BLOCKOUT LOD0 {n0}, rooms {len(cells)}, engine base "
              f"{base0['verdict']}, predicted px/m {base_ppm:.1f} @ {A}")
        return result
    # 15 bake (prefiltered CC0 sources at the predicted density, MUST-CHANGE 6)
    finished = False
    hook = None
    mat_info = None
    lod0_tiled = None
    mat_idx0 = None
    if args.finish and (tiled or getattr(args, "ppm_report", False)):
        mat_idx0 = np.zeros(len(obj.data.polygons), np.int32)
        obj.data.polygons.foreach_get("material_index", mat_idx0)
    if tiled:
        # T16: the unsliced, un-aligned copy (world-anchored UVTile, material slots) becomes
        # LOD0; `obj` goes on through the unchanged bake as the LOD1/LOD2 "far" set
        lod0_tiled = obj.copy()
        lod0_tiled.data = obj.data.copy()
        lod0_tiled.name = name + "Tiled"
        bpy.context.scene.collection.objects.link(lod0_tiled)

        if recipe.allow_4096:
            print("[lodge] tiled: R18 4096 escalation off (the far set is a LOD1/LOD2 set)")
    if args.finish:
        # pack-friendly UVTile islands (cs2_uv packs axis-aligned boxes, no rotation)
        result["aligned"] = K.align_islands(obj)
        result["split_islands"] = K.split_poor_islands(obj)
        prebake = None
        if recipe.allow_4096 and args.tex == "auto" and not tiled:
            prebake = obj.copy()
            prebake.data = obj.data.copy()
            prebake.name = name + "Prebake"
        while True:
            n_isl = len(cs2_uv.uv_islands(obj.data, obj.data.uv_layers[cs2_bake.TILE_LAYER]))
            margin = pack_margin(A, n_isl, args.packer)
            dry = dry_pack_density(obj, A, margin, args.packer)
            if dry.get("median_w1"):
                base_ppm = dry["median_w1"]
            result["pack_margin"] = {"islands": n_isl, "margin_px": margin,
                                     "contract_px": max(16, A // 64), "packer": args.packer,
                                     "dry_pack_w1_ppm": dry.get("median_w1")}
            print(f"[lodge] pack: {n_isl} UV islands -> margin {margin} px at {A} (contract "
                  f"{max(16, A // 64)}), dry pack w=1 median {dry.get('median_w1')} px/m -> "
                  f"prefilter at {K.PREFILTER_FACTOR} x")
            specs, mat_info = K.material_specs(keys, base_ppm)
            if tiled and getattr(args, "sheet_layout", "probe") == "rollout":
                # the far set (LOD1 / LOD2) carries the same measured-vanilla smoothness and metal
                # as the sheet, so the LOD switch does not change the shine (rollout doc section 4)
                import cs2_tilesheet as TS
                specs = {k: replace(s, rough=1.0 - TS.smooth_of(k), metal=TS.metal_of(k))
                         for k, s in specs.items()}
            sources = _final_sources(specs, keys)
            world = bpy.context.scene.world or bpy.data.worlds.new("World")
            bpy.context.scene.world = world
            world.light_settings.distance = AO_DISTANCE_M
            hook = make_post_compose(obj, recipe, dspecs)
            if recipe.weather is not None:
                # art pass (W12): the layer reads roles by material INDEX = sorted(keys)
                mats = [m.name if m else None for m in obj.data.materials]
                want = [cs2_bake.SRC_MAT_PREFIX + k for k in sorted(keys)]
                if mats != want:
                    raise RuntimeError(f"{name}: material slots {mats} do not map 1:1 onto "
                                       f"the sorted keys {want} (weathering roles by index)")
                hook = wrap_weather(hook, recipe, name, keys, dspecs,
                                    sweep=getattr(args, "weather_sweep", None))
            elif getattr(args, "weather_sweep", None):
                raise RuntimeError(f"{recipe.key}: --weather-sweep needs recipe.weather")
            packer = K.calibrated_packer() if args.packer == "calibrated" else _NullCtx()
            with packer:
                rep = cs2_bake.bake_unique(obj, asset_dir, name, A, sources, reserved=(),
                                           margin_px=margin, ao_samples=args.ao_samples,
                                           post_compose=hook,
                                           emissive=any(n.startswith("lamp_") for n in table),
                                           src_uv=cs2_bake.TILE_LAYER)
            med = hook.density["median_w1"] if hook.density else None
            # 16 R18: L4 / L5 only, auto -> 4096 when the shipped w=1 median < 25 px/m
            if prebake is not None and A < 4096 and med is not None and med < R18_MIN_PPM:
                print(f"[lodge] R18: median {med} < {R18_MIN_PPM} px/m at {A} -> rebake at 4096")
                me = obj.data
                bpy.data.objects.remove(obj, do_unlink=True)
                bpy.data.meshes.remove(me)
                obj = prebake
                obj.name, obj.data.name = name, name
                bpy.context.scene.collection.objects.link(obj)
                prebake = None
                A = 4096
                base_ppm = area["predicted_ppm_w1"].get(4096, base_ppm * 2)
                continue
            break
        if prebake is not None:
            pm = prebake.data
            bpy.data.objects.remove(prebake)
            bpy.data.meshes.remove(pm)
        finished = True
        result["bake"] = {k: rep[k] for k in ("A", "margin_px", "uncovered_texels", "seconds",
                                              "files", "ao")}
        result["bake"]["pack_px_per_m"] = rep["pack"]["px_per_m"]
        # fine-detail step 3d (the step-3a engine gap): a texture map an earlier export wrote and this
        # build no longer does (an _Emissive once the last lamp_ part is a vanilla row, any map) is
        # deleted, so the folder, its settings.json and the manifest's files list carry only this
        # build's maps. The key exists only when something was removed (other manifests unchanged).
        stale = remove_stale_maps(asset_dir, name, rep["files"])
        if stale:
            result["bake"]["stale_maps_removed"] = stale
        result["density"] = hook.density
        result["materials"] = mat_info
        if mat_idx0 is not None:
            import cs2_tilesheet as TS
            result["ppm_by_material"] = TS.face_ppm(obj, A, keys, mat_index=mat_idx0)
            result["smooth_by_material"] = TS.mask_stats(
                obj, os.path.join(asset_dir, f"{name}_MaskMap.png"), keys, mat_index=mat_idx0)
        if tiled:
            obj, far = _tiled_lod0(obj, lod0_tiled, recipe, args, name, asset_dir, keys, rep,
                                   result, A)
            if wk:
                # window kit v2: the holes were cut on the tiled LOD0 (_tiled_lod0_rollout);
                # every recessed pane must now see out of its hole
                blocked = K.check_panes_clear(obj, win) if win is not None else []
                if blocked:
                    raise RuntimeError(f"{name}: {len(blocked)} recessed panes look at a wall: "
                                       f"{blocked[:8]}")
                result["window_kit"].update(panes_blocked=0, mode=wk["mode"], family=wk.get("family"),
                                            arrival_authoring=[round(v, 3) for v in wk["arrival"]])
    else:
        obj.data.materials.clear()
    T["bake"] = time.perf_counter() - t0
    result["finished"] = finished
    result["tex"] = A
    result["hashes"]["LOD0"] = mesh_hash(obj)
    # 17 export LOD0 + rooms
    cs2_asset.export_fbx(obj, os.path.join(asset_dir, f"{name}.fbx"))
    win_far = None
    if win is not None and wk and tiled:
        # kit v2: LOD1 / LOD2 have no wall holes, so their `_Win` panes go back to the v1 depth
        win_far, moved = K.unrecess_panes(win, far, depth=K.FAMILIES[wk["family"]]["reveal"])
        result["window_kit"]["panes_unrecessed_far"] = moved
        result["window_kit"]["panes"] = len(win.data.polygons)
        result["hashes"]["WinFar"] = mesh_hash(win_far)
        if args.lods:                     # the export takes each file stem as the object name
            keep = win.name
            win.name = keep + "Near"
            try:
                cs2_submesh.export_submeshes(win_far, name, "Win", asset_dir, lods=(1, 2))
            finally:
                win.name = keep
        cs2_submesh.export_submeshes(win, name, "Win", asset_dir, lods=())
    elif win is not None:
        cs2_submesh.export_submeshes(win, name, "Win", asset_dir, lods=(1, 2) if args.lods else ())
    # 18-19 LOD chain + checks
    lods = []
    if args.lods:
        src = far if tiled else obj
        (t1, t2), plan = plan_lod_targets(src, table, recipe)
        d1, d2 = delete_lists(recipe, table)
        lods, infos = K.lod_chain(src, name, (t1, t2), (d1, d2), recipe.ground_prefixes)
        result["lod_plan"] = plan
        result["lod_info"] = infos
        for lvl, lod in enumerate(lods, start=1):
            result["base"][lvl] = K.check_base_mode(lod, table, recipe.base_mode,
                                                    recipe.ground_prefixes, recipe.plinth)
            result["tris"][lvl] = cs2_asset.tri_count(lod)
            if lvl == 2:
                K.assert_budget(lod, recipe.budget, 2)
            if win is not None:
                result.setdefault("panes_off", {})[lvl] = K.panes_on_surface(
                    lod, win_far if win_far is not None else win, 0.30)
            result["hashes"][f"LOD{lvl}"] = mesh_hash(lod)
            cs2_asset.export_fbx(lod, os.path.join(asset_dir, f"{name}_LOD{lvl}.fbx"))
    T["lods"] = time.perf_counter() - t0
    if signset is not None:
        result["sign_set"].update(_export_sign_set(signset, recipe, args, name, obj, lods, result,
                                                   area))
    # 20 settings
    if tiled:
        _tiled_settings(asset_dir, name, len(lods), args, result)
    else:
        cs2_asset.write_settings(asset_dir, name, supplied_lods=tuple(range(1, len(lods) + 1)))
    # 21 previews / review
    tint = {"RGB"[k - 1]: tuple(v) for k, v in recipe.slots.items()}
    if args.preview is not None and finished:
        import cs2_preview
        prev = args.preview or os.path.join(PREVIEW_ROOT, recipe.category)
        if args.ab is None and recipe.key in AB_DEFAULT:
            cand = os.path.join(IMPORT_ROOT, AB_DEFAULT[recipe.key])
            args.ab = cand if os.path.isdir(cand) else None
        modes = ["pbr", "dusk", "normals", "density"] + (["lod"] if lods and not tiled else []) + \
            (["ab"] if args.ab else [])
        files = cs2_preview.preview(obj, asset_dir, name, prev, modes=tuple(modes),
                                    views=list(VIEWS) + list(recipe.views), lods=lods or None,
                                    prev_generation=args.ab, tint=tint)
        result["previews"] = [os.path.relpath(f, REPO) for f in files]
        obj.data.materials.clear()
    if args.review is not None:
        import cs2_review
        result["review_pack"] = cs2_review.write_pack(
            name, asset_dir, dict(enumerate([obj] + lods)), {"Win": win} if win is not None else {},
            meta={"generator": "lodge.py", "args": vars(args), "kind": "building",
                  "caps": {0: recipe.budget.lod0_cap, 1: result["tris"].get(1),
                           2: recipe.budget.lod2_cap},
                  "letter": letter, "tint": tint, "lot_cells": list(recipe.lot)},
            out_dir=args.review or args.review_out or None,
            # vanilla props (DECISIONS 183): the manifest is written after this call, so hand the
            # rows over; the pack shows one labelled stand-in box per row (pack only, not exported)
            manifest={"sub_objects": sub_objects_block(recipe)} if recipe.sub_objects else {},
            **({"extra_meshes": {"SignSet": (signset["obj"], *result["sign_set"]["texture"])}}
               if signset is not None and result["sign_set"].get("texture") else {}))
    # switch distances (with the LOD0 area, as the review pack)
    if lods:
        import cs2_preview
        W = K._world_coords(obj)
        size = tuple(float(v) for v in (W.max(0) - W.min(0)))
        result["lod_switch_m"] = cs2_preview.lod_switch_table(
            size, [result["tris"][i] for i in sorted(result["tris"])], area=area["kept_m2"])
    result["extent_game_m"] = _game_extent(obj)
    for lod in lods:
        me = lod.data
        bpy.data.objects.remove(lod, do_unlink=True)
        bpy.data.meshes.remove(me)
    # 22 manifest + validate
    _write_manifest(asset_dir, name, recipe, args, result, hook, win, T, t0)
    if not args.no_validate:
        result["validate"] = validate(asset_dir, name, recipe, obj, win, A,
                                      max_texture=4096 if tiled else None)
    print(f"[lodge] asset folder: {asset_dir}")
    med = (hook.density or {}).get("median_w1") if hook else None
    chain = [result["tris"][i] for i in sorted(result["tris"]) if i]
    print(f"PASS lodge: {name} LOD0 {result['tris'][0]}, chain {chain}, rooms {len(cells)}, "
          f"engine base {base0['verdict']}, tex {A}, px/m med {med}")
    return result


def _tiled_lod0(far, lod0, recipe, args, name, asset_dir, keys, rep, result, A_far):
    """Tiled LOD0 (T16 probe / rollout). Returns (lod0 renamed to `name`, far)."""
    if getattr(args, "sheet_layout", "probe") == "probe":
        return _tiled_lod0_probe(far, lod0, recipe, args, name, asset_dir, keys, rep, result, A_far)
    return _tiled_lod0_rollout(far, lod0, recipe, args, name, asset_dir, keys, rep, result, A_far)


def _tiled_settings(asset_dir, name, n_lods, args=None, result=None):
    if args is None or getattr(args, "sheet_layout", "probe") == "probe":
        return _tiled_settings_probe(asset_dir, name, n_lods)
    return _tiled_settings_rollout(asset_dir, name, n_lods, result["tiled"])


def _sheet_repeats():
    """{key: r} of the shared sheet: max over every registered recipe's tile_scale."""
    reps = {}
    for rc in R.load_recipes().values():
        for k, r in getattr(rc, "tile_scale", ()) or ():
            reps[k] = max(int(r), reps.get(k, 1))
    return reps


SHEET_OWNER_KEY = "school"      # the building whose folder carries the family sheet as its LOD0 maps


def sheet_paths(args, letter):
    """(sheet WORKSPACE dir, sheet name) of the rollout family sheet: where it is composed (not an
    asset folder; the owner building's folder receives the composed maps, install_owner_maps)."""
    sname = cs2_asset.validate_asset_name("SkiTileSheet" + letter)
    if getattr(args, "sheet_dir", None):
        return os.path.abspath(args.sheet_dir), sname
    root = os.path.abspath(args.out) if args.out else os.path.join(IMPORT_ROOT, "buildings", letter)
    return os.path.join(root, "_" + sname), sname


def sheet_owner(args, letter):
    """(owner asset dir, owner name): the ski school of the same letter (rollout doc section 7:
    the sheet travels as the school's own LOD0 maps; the seven others share them cross-asset)."""
    rc = R.get_recipe(SHEET_OWNER_KEY)
    oname = rc.asset + letter
    root = os.path.abspath(args.out) if args.out else os.path.join(IMPORT_ROOT, rc.category, letter)
    return os.path.join(root, oname), oname


def install_owner_maps(sdir, sname, odir, oname, emissive=True):
    """Copy the composed sheet into the owner's folder under the owner's name."""
    import shutil
    os.makedirs(odir, exist_ok=True)
    out = []
    for m in ("BaseColor", "Normal", "MaskMap", "ControlMask") + (("Emissive",) if emissive else ()):
        src = os.path.join(sdir, f"{sname}_{m}.png")
        dst = os.path.join(odir, f"{oname}_{m}.png")
        shutil.copyfile(src, dst)
        out.append(os.path.basename(dst))
    return out


def ensure_family_sheet(sdir, sname, A, repeats, emissive=True):
    """The composed family sheet in `sdir` (layout JSON + PNGs). Rebuilt when the layout hash
    (rects, repeats, material definitions, smoothness table) changed; label rects already in
    `sdir/labels` are pasted. Returns the layout dict (with per-swatch means, no pixels)."""
    import cs2_tilesheet as TS
    lay = TS.sheet_layout(A, "rollout", repeats)
    aux = sdir + ".aux"                       # layout + label rects: never inside the asset folder
    os.makedirs(aux, exist_ok=True)
    jp = os.path.join(aux, f"{sname}.layout.json")
    if os.path.isfile(jp):
        old = TS.load_layout(jp)
        if old.get("hash") == lay["hash"] and all(
                os.path.isfile(os.path.join(sdir, f"{sname}_{m}.png"))
                for m in ("BaseColor", "Normal", "MaskMap", "ControlMask")):
            return old
    print(f"[lodge] tiled: building the family sheet {sname} ({A}, hash {lay['hash']})")
    full = TS.build_sheet(sdir, sname, A=A, layout="rollout", repeats=repeats)
    ldir = os.path.join(sdir + ".aux", "labels")
    labels = sorted(os.path.join(ldir, f) for f in os.listdir(ldir) if f.endswith(".npz")) \
        if os.path.isdir(ldir) else []
    TS.write_sheet(full, emissive=emissive, labels=labels)
    TS.save_layout(full, jp)
    return TS.load_layout(jp)


def recompose_family_sheet(sdir, sname, A=4096):
    """Rebuild the family sheet PNGs with every building's label rect pasted (run once after the
    last building of a round; the hash check alone would keep a sheet without new labels)."""
    jp = os.path.join(sdir + ".aux", f"{sname}.layout.json")
    if os.path.isfile(jp):
        os.remove(jp)
    return ensure_family_sheet(sdir, sname, A, _sheet_repeats())


def _tiled_lod0_rollout(far, lod0, recipe, args, name, asset_dir, keys, rep, result, A_far):
    """Rollout (docs/research/art/texture-parity-rollout-2026-10-16.md): rename the far bake's
    maps to <name>_LOD1_* (emissive dropped: LOD0-only engine-side, DECISIONS 77), re-mesh `lod0`
    onto the SHARED family sheet (cs2_tilesheet.tile_mesh with the weathering rules, highest
    ladder level under the LOD0 cap), paint this building's labels into its own label rect
    (<sheet_dir>.aux/labels/<name>.npz; recompose_family_sheet pastes every building's rect),
    measure. No sheet map is written into the building folder: settings.json shares them."""
    import cs2_tilesheet as TS
    t0 = time.perf_counter()
    renamed = []
    for f in rep["files"]:
        if f.endswith("_Emissive.png"):
            os.remove(os.path.join(asset_dir, f))
            continue
        dst = f.replace(f"{name}_", f"{name}_LOD1_", 1)
        os.replace(os.path.join(asset_dir, f), os.path.join(asset_dir, dst))
        renamed.append(dst)
    far.name = name + "Far"
    far.data.name = name + "Far"
    SA = int(getattr(args, "sheet_tex", 4096))
    letter = name[len(recipe.asset):] if name.startswith(recipe.asset) else (args.letter or recipe.letter)
    sdir, sname = sheet_paths(args, letter)
    repeats = _sheet_repeats()
    layout = ensure_family_sheet(sdir, sname, SA, repeats)
    layout["label_rect"] = TS.label_rect_for(layout, recipe.key)
    uvt = cs2_uv.loop_uvs(lod0.data.uv_layers[cs2_bake.TILE_LAYER])
    tw = getattr(recipe, "tiled_weather", None) or {}
    wl = getattr(args, "weather_level", "auto")
    if tw.get("off"):
        ladder = [None]
    elif "level" in tw:
        ladder = [TS.weather_level(int(tw["level"]))]
    elif wl != "auto":
        ladder = [TS.weather_level(int(wl))]
    else:
        ladder = [TS.weather_level(i) for i in range(6)]
    cap = recipe.budget.lod0_cap
    pristine = lod0.data.copy()
    tm = None
    tried = []
    for W in ladder:
        if tm is not None:                          # retry from the untouched copy
            old = lod0.data
            lod0.data = pristine.copy()
            bpy.data.meshes.remove(old)
        tm = TS.tile_mesh(lod0, uvt, sorted(keys), layout,
                          label_prefixes=tuple(p for p, _pa, _kw in recipe.labels),
                          ground_prefixes=tuple(recipe.ground_prefixes) if recipe.base_mode == "engine" else (),
                          label_ppm=TS.LABEL_PPM * SA / 4096.0, weather=W,
                          flat_prefixes=TS.FLAT_DETAIL_PREFIXES + tuple(getattr(recipe, "tiled_flat", ()) or ())
                          # kit v2: the window parts draw their material's mean colour (no tile cuts)
                          + ((K.KIT2_PARTS + WINDOW_FLAT_EXTRA.get(recipe.key, ()))
                             if getattr(args, "window_kit", "v1") == "v2" else ()),
                          grid_offset=0.5 if recipe.base_mode == "plinth" else 0.0)
        if getattr(args, "window_kit", "v1") == "v2":
            # window kit v2: the wall holes, cut AFTER the tile slicing so only the tile pieces
            # under an opening are split (cut before, a hole's lines crossed every tile row)
            kit2 = K.punch_openings(lod0, json.loads(lod0.data[cs2_asset.PART_TABLE_PROP]))
            # "missing" = no wall face in the opening's plane (a glass wall such as L1's prow):
            # nothing to cut, the check_panes_clear gate in run() still proves the glass is seen
            if kit2["partial"]:
                raise RuntimeError(f"{name}: window kit v2 openings with a partial wall hole: "
                                   f"{kit2['partial'][:8]}")
            tm["tris_after"] = cs2_asset.tri_count(lod0)
            tm["window_kit_holes"] = kit2["holes"]
            result["window_kit"] = kit2
        tried.append({"level": None if W is None else W.level, "tris": tm["tris_after"]})
        print(f"[lodge] tiled: weather level {None if W is None else W.level} -> LOD0 {tm['tris_after']} "
              f"(cap {cap})")
        if tm["tris_after"] <= cap:
            break
    bpy.data.meshes.remove(pristine)
    lod0.name = name
    lod0.data.name = name
    table = json.loads(lod0.data[cs2_asset.PART_TABLE_PROP])
    # labels: this building's rect only (the oriented painters want ONE mesh with a UVUnique
    # layer: hide the far mesh's layer name while they run)
    lab_npz = None
    if recipe.labels and tm["label_rects"]:
        far_uv = far.data.uv_layers.get(cs2_uv.UNIQUE_LAYER)
        if far_uv is not None:
            far_uv.name = "UVUniqueFarSet"
        try:
            os.makedirs(os.path.join(sdir + ".aux", "labels"), exist_ok=True)
            lab_npz = TS.paint_labels(layout, tm["label_rects"], recipe.labels, lod0.data, table,
                                      os.path.join(sdir + ".aux", "labels", f"{name}.npz"))
        finally:
            if far_uv is not None:
                far.data.uv_layers["UVUniqueFarSet"].name = cs2_uv.UNIQUE_LAYER
    ppm = TS.face_ppm(lod0, SA, sorted(keys), split_weather=True)
    tm["uv_outside_region"] = TS.check_uv_cells(lod0, layout, sorted(keys))
    print(f"[lodge] tiled: faces sampling outside their sheet region {tm['uv_outside_region']}")
    if tm["uv_outside_region"]:
        raise RuntimeError(f"{name}: faces sample outside their sheet region {tm['uv_outside_region']}")
    smooth = TS.mask_stats(lod0, os.path.join(sdir, f"{sname}_MaskMap.png"), sorted(keys))
    wstats = TS.weather_stats(lod0, sorted(keys)) if tm.get("weather") else {}
    for a in ("cs2ts_mat", "cs2ts_w"):
        attr = lod0.data.attributes.get(a)
        if attr is not None:
            lod0.data.attributes.remove(attr)
    lod0.data.materials.clear()
    for lay in [l.name for l in lod0.data.uv_layers if l.name != cs2_uv.UNIQUE_LAYER]:
        lod0.data.uv_layers.remove(lod0.data.uv_layers[lay])
    tris0 = cs2_asset.tri_count(lod0)
    result["tris"][0] = tris0
    result["base"][0] = K.check_base_mode(lod0, table, recipe.base_mode, recipe.ground_prefixes,
                                          recipe.plinth)
    emis = any(n.startswith("lamp_") for n in table) or any(
        K.BLDG_MATERIALS[k].emissive_srgb is not None for k in keys)
    lrect = list(layout["label_rect"])
    odir, oname = sheet_owner(args, letter)
    is_owner = recipe.key == SHEET_OWNER_KEY
    if is_owner:
        if lab_npz:                                 # the owner ships the sheet WITH its own labels
            layout = recompose_family_sheet(sdir, sname, SA)
        install_owner_maps(sdir, sname, asset_dir, name)
    rel = os.path.relpath(odir, asset_dir).replace("\\", "/")
    result["tiled"] = {
        "layout": "rollout",
        "sheet": {"name": sname, "workspace": os.path.relpath(sdir, asset_dir).replace("\\", "/"),
                  "A": SA, "hash": layout["hash"], "owner": oname, "is_owner": is_owner,
                  "maps": 5, "mb_raw_bc7": TS.bc7_mb(SA, 5), "shared_prefix": f"{rel}/{oname}",
                  "emissive_bound": bool(emis), "repeats": layout["repeats"],
                  "label_rect": lrect, "label_npz": os.path.basename(lab_npz) if lab_npz else None},
        "far": {"A": A_far, "files": renamed, "mb_raw_bc7": TS.bc7_mb(A_far, len(renamed))},
        "tile_mesh": tm, "weather_ladder": tried, "ppm_by_material": ppm, "smooth_by_material": smooth,
        "weather_by_material": wstats, "lod0_tris": tris0, "seconds": round(time.perf_counter() - t0, 1),
    }
    print(f"[lodge] tiled: LOD0 {tris0} tris on {sname} ({tm['sliced_tris']} tris sliced into "
          f"{tm['pieces']} pieces, {tm['tjunction_inserts']} T-junction inserts, weather "
          f"{tm.get('weather')}); far set {A_far}")
    for k, v in ppm.items():
        print(f"[lodge] tiled px/m {k:22s} p10 {v['p10']:7.1f} p50 {v['p50']:7.1f} "
              f"p90 {v['p90']:7.1f} ({v['area_m2']} m2)")
    K.assert_budget(lod0, recipe.budget, 0)
    return lod0, far


def _tiled_settings_rollout(asset_dir, name, n_lods, tiled):
    """Rollout settings.json: LOD0 maps shared from the family sheet owner (cross-asset
    sharedAssets, AssetImportPipeline.cs:502-509 1.6.2f1 resolves the relative target); LOD1
    ships its own maps (dedicated, identity entries, the ExampleBakedAlphaFence pattern); LOD2
    shares LOD1's. Whether a LOD0 cross-asset key and dedicated LOD keys combine in one
    import is the ONE thing the import probe must show (rollout doc section 7)."""
    pre = tiled["sheet"]["shared_prefix"]
    maps = ["BaseColor", "Normal", "MaskMap", "ControlMask"] + (["Emissive"] if tiled["sheet"]["emissive_bound"] else [])
    # the owner (the school) carries the sheet as its own LOD0 maps; the others share them
    shared = {} if tiled["sheet"]["is_owner"] else {f"{name}_{m}.png": f"{pre}_{m}.png" for m in maps}
    if n_lods >= 1:
        for m in ("BaseColor", "Normal", "MaskMap", "ControlMask"):
            shared[f"{name}_LOD1_{m}.png"] = f"{name}_LOD1_{m}.png"
    if n_lods >= 2:
        for m in ("BaseColor", "Normal", "MaskMap", "ControlMask"):
            shared[f"{name}_LOD2_{m}.png"] = f"{name}_LOD1_{m}.png"
    settings = {"sharedAssets": shared}
    for lvl in range(1, n_lods + 1):
        settings[f"LOD{lvl}"] = {"mode": "Skip"}
    with open(os.path.join(asset_dir, "settings.json"), "w", encoding="utf-8", newline="\n") as fh:
        json.dump(settings, fh, indent=2)
        fh.write("\n")


def _tiled_lod0_probe(far, lod0, recipe, args, name, asset_dir, keys, rep, result, A_far):
    """T16 probe (docs/research/art/texture-parity-2026-10-16.md): rename the far bake's maps
    to <name>_LOD1_* (emissive dropped: LOD0-only engine-side, DECISIONS 77), build the family
    sheet into the asset folder as the LOD0 maps, re-mesh `lod0` onto it
    (cs2_tilesheet.tile_mesh), paint the recipe labels into the sheet's label swatch, measure.
    Returns (lod0 renamed to `name`, far)."""
    import cs2_tilesheet as TS
    t0 = time.perf_counter()
    renamed = []
    for f in rep["files"]:
        if f.endswith("_Emissive.png"):
            os.remove(os.path.join(asset_dir, f))
            continue
        dst = f.replace(f"{name}_", f"{name}_LOD1_", 1)
        os.replace(os.path.join(asset_dir, f), os.path.join(asset_dir, dst))
        renamed.append(dst)
    far.name = name + "Far"
    far.data.name = name + "Far"
    lod0.name = name
    lod0.data.name = name
    SA = int(getattr(args, "sheet_tex", 4096))
    layout = TS.build_sheet(asset_dir, name, A=SA, swatch=SA // 4, pad=max(16, SA // 128), layout="probe")
    uvt = cs2_uv.loop_uvs(lod0.data.uv_layers[cs2_bake.TILE_LAYER])
    tm = TS.tile_mesh(lod0, uvt, sorted(keys), layout,
                      label_prefixes=tuple(p for p, _pa, _kw in recipe.labels),
                      ground_prefixes=tuple(recipe.ground_prefixes) if recipe.base_mode == "engine" else (),
                      label_ppm=TS.LABEL_PPM * (SA // 4) / 1024.0)
    table = json.loads(lod0.data[cs2_asset.PART_TABLE_PROP])
    TS.fill_label_backgrounds(layout, tm["label_rects"])
    base, ctl = layout["maps"]["base"], layout["maps"]["ctl"]
    # oriented painters (recipes/_civic.find_baking_mesh) want ONE mesh with a UVUnique layer:
    # hide the far mesh's layer name while the sheet's labels are painted
    far_uv = far.data.uv_layers.get(cs2_uv.UNIQUE_LAYER)
    if far_uv is not None:
        far_uv.name = "UVUniqueFarSet"
    try:
        for prefix, painter, kw in recipe.labels:
            p = K.make_painter(painter, kw, face=prefix)
            cs2_compose.stamp_on_face(base, ctl, lod0.data, table, prefix, p)
    finally:
        if far_uv is not None:
            far.data.uv_layers["UVUniqueFarSet"].name = cs2_uv.UNIQUE_LAYER
    emis = any(n.startswith("lamp_") for n in table)
    files = TS.write_sheet(layout, emissive=emis)
    ppm = TS.face_ppm(lod0, SA, sorted(keys))
    tm["uv_outside_region"] = TS.check_uv_cells(lod0, layout, sorted(keys))
    print(f"[lodge] tiled: faces sampling outside their sheet region {tm['uv_outside_region']}")
    smooth = TS.mask_stats(lod0, os.path.join(asset_dir, f"{name}_MaskMap.png"), sorted(keys))
    attr = lod0.data.attributes.get("cs2ts_mat")
    if attr is not None:
        lod0.data.attributes.remove(attr)
    lod0.data.materials.clear()
    for lay in [l.name for l in lod0.data.uv_layers if l.name != cs2_uv.UNIQUE_LAYER]:
        lod0.data.uv_layers.remove(lod0.data.uv_layers[lay])
    tris0 = cs2_asset.tri_count(lod0)
    result["tris"][0] = tris0
    result["base"][0] = K.check_base_mode(lod0, table, recipe.base_mode, recipe.ground_prefixes,
                                          recipe.plinth)
    result["tiled"] = {
        "sheet": {"A": SA, "swatch": SA // 4, "pad": layout["pad"], "content": layout["C"],
                  "files": files, "mb_raw_bc7": TS.bc7_mb(SA, len(files)),
                  "tiles": layout["tiles"], "palette": layout["palette"],
                  "label_rect": layout["label_rect"]},
        "far": {"A": A_far, "files": renamed, "mb_raw_bc7": TS.bc7_mb(A_far, len(renamed))},
        "tile_mesh": tm, "ppm_by_material": ppm, "smooth_by_material": smooth,
        "lod0_tris": tris0, "seconds": round(time.perf_counter() - t0, 1),
    }
    print(f"[lodge] tiled: LOD0 {tris0} tris on the {SA} sheet ({tm['sliced_tris']} tris sliced into "
          f"{tm['pieces']} pieces, {tm['tjunction_inserts']} T-junction inserts); far set {A_far}")
    for k, v in ppm.items():
        print(f"[lodge] tiled px/m {k:14s} p10 {v['p10']:7.1f} p50 {v['p50']:7.1f} "
              f"p90 {v['p90']:7.1f} ({v['area_m2']} m2)")
    K.assert_budget(lod0, recipe.budget, 0)
    return lod0, far


def _tiled_settings_probe(asset_dir, name, n_lods):
    """T16 probe settings.json: LOD0 = the sheet maps in this folder (a production build points
    them at the family owner with sharedAssets, cs2_asset.write_settings(shared_from=...)); LOD1
    ships its own maps (they bind by the LOD1 name); LOD2 shares LOD1's."""
    shared = {}
    if n_lods >= 2:
        for m in ("BaseColor", "Normal", "MaskMap", "ControlMask"):
            shared[f"{name}_LOD2_{m}.png"] = f"{name}_LOD1_{m}.png"
    settings = {"sharedAssets": shared} if shared else {}
    for lvl in range(1, n_lods + 1):
        settings[f"LOD{lvl}"] = {"mode": "Skip"}
    with open(os.path.join(asset_dir, "settings.json"), "w", encoding="utf-8", newline="\n") as fh:
        json.dump(settings, fh, indent=2)
        fh.write("\n")


def _write_manifest(asset_dir, name, recipe, args, result, hook, win, T, t0):
    batches = (1 + (1 if win is not None else 0) + (1 if recipe.base_mode == "engine" else 1)) \
        * (1 + (2 if args.lods and not args.blockout else 0))
    vram = {1024: 5.6, 2048: 22.4, 4096: 89.5}.get(result.get("tex"), None)
    emis = any(n.startswith("lamp_") for n in json.loads(bpy.data.objects[name].data
                                                         [cs2_asset.PART_TABLE_PROP])) \
        if name in bpy.data.objects else None
    data = {
        "generator": "lodge.py", "git_sha": _git_sha(), "spec": SPEC_TAG,
        "recipe": recipe.key, "args": vars(args), "prefab": recipe.prefab,
        "tris": result.get("tris"), "budget": vars(recipe.budget),
        "parts": result.get("parts"),
        "materials_used": result.get("keys"), "materials": result.get("materials"),
        "cull": result.get("cull"), "area_report": result.get("area"),
        "shipped_density": result.get("density"), "tex": result.get("tex"),
        "bake": result.get("bake"), "pack_margin": result.get("pack_margin"),
        "rooms": result.get("cells"), "rooms_n": len(result.get("cells") or {}),
        "engine_base": result.get("base"), "lot": result.get("lot"), "mesh_hash": result.get("hashes"),
        "facing": result.get("facing"), "extent_game_m": result.get("extent_game_m"),
        "lod_plan": result.get("lod_plan"), "lod_info": result.get("lod_info"),
        "lod_switch_m": result.get("lod_switch_m"), "panes_off_surface": result.get("panes_off"),
        "batches_per_mesh_group": batches,
        "vram_mb": (round(vram * (1.25 if emis else 1.0), 1) if vram else None),
        "interior": {"route": args.interior, "keyword": recipe.interior},
        "emissive": emis, "review_pack": result.get("review_pack"),
        "stage_seconds": {k: round(v, 1) for k, v in T.items()},
        "total_seconds": round(time.perf_counter() - t0, 1),
    }
    if result.get("window_kit"):           # kit v2 builds only (older manifests keep their keys)
        data["window_kit"] = result["window_kit"]
    # art pass, detail pass 1: keys only for recipes that use the features (older manifests
    # keep their key set)
    rep = getattr(hook, "report", None) if hook is not None else None
    if rep is not None:
        import cs2_weather as CW
        data["weather"] = json.loads(CW.report_json(rep))
        data["weather"]["recipe_spec"] = json.loads(json.dumps(recipe.weather, default=str))
    sweep = getattr(hook, "sweep", None) if hook is not None else None
    if sweep:
        data["weather_sweep"] = sweep
    info = getattr(hook, "label_info", None) if hook is not None else None
    if info:
        data["labels"] = info
    if result.get("we_boards"):
        data["we_boards"] = {
            "by": "lodge.py nitpick C (build/nitpick-c-lodges.md)",
            "use": "optional Write Everywhere layouts (another fleet): each face is the board's flat printable quad, "
                   "unobstructed (its frame ring sits outside the face, 0.03 m proud); role blank = an empty panel "
                   "left for the player's own label, name = a lit name board (sheet kind) a WE text may cover",
            "frame": "centre / normal / right / up in prefab-local game metres; " + FD.X_CONVENTION,
            "boards": result["we_boards"]}
    if recipe.sub_objects:
        data["sub_objects"] = sub_objects_block(recipe)
    # T16 probe keys (only when asked: --ppm-report / --tiled; other manifests keep their key set)
    for k in ("ppm_by_material", "smooth_by_material", "tiled"):
        if result.get(k) is not None:
            data[k] = result[k]
    if isinstance(recipe, FD.FineRecipe):
        ss = {k: v for k, v in (result.get("sign_set") or {}).items() if k not in ("obj", "texture")}
        data["fine_detail"] = {"fine_detail": bool(recipe.fine_detail), "x_convention": FD.X_CONVENTION,
                               "sign_set": ss or None,
                               "rows": (data.get("sub_objects") or {}).get("kinds")}
    cs2_validate.write_manifest(asset_dir, name, data)
    # kit colour variants (2026-10-12): a shipping export stages the variants its kit rows name
    import dress_kit
    dress_kit.stage_from_manifest(asset_dir, name)


def _export_sign_set(signset, recipe, args, name, obj, lods, result, area):
    """Export the recipe's sign set (cs2_signset) once the host's LOD chain exists: its cull bias
    targets the host's LOD2 switch (design C.4: building signs are kept to LOD1). Returns the
    numbers merged into result["sign_set"] (and "texture" for the review pack)."""
    import cs2_preview
    import cs2_signset
    spec = signset["spec"]
    W = K._world_coords(obj)
    size = tuple(float(v) for v in (W.max(0) - W.min(0)))
    tris = [result["tris"][i] for i in sorted(result["tris"])]
    sw = cs2_preview.lod_switch_table(size, tris, area=area["kept_m2"])
    target = sw["switch"].get(2) or sw["switch"].get(max(sw["switch"]) if sw["switch"] else 0) or sw["cull"]
    letter_dir = os.path.abspath(args.signset_out) if getattr(args, "signset_out", None) else \
        cs2_signset.letter_dir_for(spec, args.export, args.out)
    W_, D_ = recipe.lot
    host = {"name": name, "recipe": recipe.key, "prefab": recipe.prefab,
            "lot_half_game": [4.0 * W_, 4.0 * D_], "switch_lod2_m": round(float(target), 2),
            "host_tris": result["tris"]}
    rep = cs2_signset.export_sign_set(signset["obj"], spec, letter_dir, host=host, cid=signset["cid"],
                                      mint=bool(args.export))
    owner_dir = os.path.join(os.path.dirname(letter_dir), cs2_signset.SHEET_LETTER, cs2_signset.SHEET_OWNER)
    if not rep["owner"] and not os.path.isdir(owner_dir):
        # a SHARER built beside a scratch --out (no owner there): validate / preview against the
        # shipped owner's sheet (fine-detail step 3a; the owner is the school's sign set)
        owner_dir = os.path.join(IMPORT_ROOT, "signs", cs2_signset.SHEET_LETTER, cs2_signset.SHEET_OWNER)
    val = cs2_signset.validate_sign_set(rep["asset_dir"], rep["name"],
                                        None if rep["owner"] else owner_dir)
    print(f"[lodge] sign set {rep['name']}: {rep['tris']} tris, cull m_Bias {rep['cull']['m_Bias']} "
          f"(target {rep['cull']['target_m']} m), bounds {rep['bounds_game']}, validate "
          f"{len(val['errors'])} errors / {len(val['warnings'])} warnings -> {rep['asset_dir']}")
    return {"asset_dir": os.path.relpath(rep["asset_dir"], REPO).replace("\\", "/")
            if rep["asset_dir"].startswith(REPO) else rep["asset_dir"],
            "tris": rep["tris"], "cull": rep["cull"], "bounds_game": rep["bounds_game"],
            "row": {k: (list(v) if isinstance(v, tuple) else v) for k, v in rep["row"].items()},
            "owner": rep["owner"], "validate": val,
            "staged": [os.path.relpath(p, REPO).replace("\\", "/") for p in rep["staged"]],
            "texture": (cs2_signset.SHEET_OWNER, rep["asset_dir"] if rep["owner"] else owner_dir)}


def validate(asset_dir, name, recipe, obj, win, A, max_texture=None):
    """cs2_validate.validate_asset_folder with the section-14 spec (the N11 keys are passed
    for the validator lens and checked here: keyword, emissive file, max texture)."""
    lods = (1, 2)
    spec = {"kind": "building", "base_mode": recipe.base_mode,
            "lot_m": (8 * recipe.lot[0], 8 * recipe.lot[1]), "lod0_cap": recipe.budget.lod0_cap,
            "lod1_ratio": 0.5, "lod2_cap": recipe.budget.lod2_cap, "hero": True,
            "expected_lods": lods, "expected_submeshes": ("Win",) if win is not None else (),
            "interior_keyword": recipe.interior,
            "max_texture": max_texture or (4096 if recipe.allow_4096 else recipe.tex)}
    obj.name, obj.data.name = name + "Src", name + "Src"
    if win is not None:
        win.name, win.data.name = name + "WinSrc", name + "WinSrc"
    findings = cs2_validate.validate_asset_folder(asset_dir, name, spec)
    errs = [f for f in findings if f[0] == "ERROR"]
    warns = [f for f in findings if f[0] == "WARN"]
    emis = os.path.join(asset_dir, f"{name}_Emissive.png")
    has_lamps = any(n.startswith("lamp_") for n in json.loads(obj.data[cs2_asset.PART_TABLE_PROP]))
    if has_lamps:
        if not os.path.isfile(emis):
            # tiled rollout: the emissive map is the family sheet's, shared like the other LOD0 maps
            with open(os.path.join(asset_dir, "settings.json"), encoding="utf-8") as fh:
                tgt = (json.load(fh).get("sharedAssets") or {}).get(f"{name}_Emissive.png")
            if not tgt or not os.path.isfile(os.path.normpath(os.path.join(asset_dir, tgt))):
                raise RuntimeError(f"{name}: lanterns but no _Emissive.png")
            emis = os.path.normpath(os.path.join(asset_dir, tgt))
            A = cs2_validate.read_png_header(emis.replace("_Emissive.png", "_BaseColor.png"))[0]
        bc = emis.replace("_Emissive.png", "_BaseColor.png")
        if os.path.isfile(bc):                      # the set the emissive belongs to (tiled: the sheet)
            A = cs2_validate.read_png_header(bc)[0]
        w, h, _d, _c = cs2_validate.read_png_header(emis)
        if (w, h) != (A, A):
            raise RuntimeError(f"{name}: _Emissive is {w}x{h}, set is {A}")
    print(f"[lodge] validate_asset_folder: {len(errs)} errors, {len(warns)} warnings")
    return {"errors": len(errs), "warnings": len(warns), "warn": [f[1:] for f in warns][:12]}


def main():
    args = parse_args()
    recipe = select_recipe(args)
    run(recipe, args)


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001 - unambiguous exit status for the harness
        traceback.print_exc()
        sys.exit(1)
