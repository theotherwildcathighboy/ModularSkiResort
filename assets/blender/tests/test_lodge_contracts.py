"""
test_lodge_contracts - recipe contracts (bldg-kit.md section 17 row test_lodge_contracts).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_lodge_contracts.py
        [-- --fast]          skip the cross-process blockouts (two child Blenders per asset)

For EVERY registered recipe (bldg_recipes.load_recipes: L1 + recipes/*.py) a --blockout
build must pass lot fit + facing (authoring frame, after the yaw, before orient - the
engine raises otherwise), the base mode on LOD0 (engine: ring OK, ground prefixes OK,
ground-band hygiene; plinth: no band vertex), LOD0 <= cap, complete part tags, and the
asset name must pass validate_asset_name + lint and carry its interior keyword.
L1 only: base_lodge.py parsed with `ast` (never imported: it mutates
cs2_asset.QUAD_RESERVED_TOP at import, base_lodge.py:107) - every AFRAME constant equal,
the derived MAIN_PITCH 1.32558 / 52.97 deg, S2_Z 6.758, PROW_PITCH 1.325, S2P_Z 6.625,
DECK_TOP 4.66 within 1e-3 of both base_lodge's expressions and aframe_derived, and the
main W slab's CENTRE plane passes through (S2_X0 - OVER, EAVE_Z) and (RIDGE_AT_X, RIDGE_Z)
pre-yaw (25c: wall plates derived from the roof).
Prefab names (buildings-review-pipeline 3; the prefab name is save identity): every
registered recipe's `prefab`, welcome_sign.PREFAB and the `prefab` of every exported building
manifest (assets/import/{lodges,buildings,signs}) is a BuildingPrefab name of the
SkiCatalog.cs NAMES block (parsed with preflight / LiftBatch CT-8's line regex).
Pane clearance (buildings-review-geometry F1-F3): on every registered recipe's parts no
`win_` pane looks into a roof / gutter / fascia / rake / pier part within 0.6 m
(recipes/l5_swanky_bottom.check_pane_clearance; L4 / L5 also assert it in their builders).
L1 (legacy A-frame massing) is skipped: its prow panes sit under the AFRAME eave by design.
Cross-process determinism (buildings-review-pipeline 4): every registered recipe and the
Welcome Sign, --blockout in two separate Blender processes -> identical manifest mesh hashes
and room cells (sign: band PNG sha1s).
"""

import ast
import glob
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
import traceback

import bpy
import numpy as np

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir))
sys.path.insert(0, HERE)
import bldg_recipes as R   # noqa: E402
import cs2_asset           # noqa: E402
import cs2_validate        # noqa: E402
import lodge               # noqa: E402

CATALOG = os.path.join(REPO, "mod", "SkiResortMod", "Systems", "SkiCatalog.cs")
# tools/preflight.ps1 section 7 / tests/LiftBatch CatalogTests CT-8 (same groups)
NAMES_LINE = re.compile(r'^\s*public const string \w+\s*=\s*"([^"]+)";\s*//\s*(\w+Prefab)'
                        r'(?:,\s*item\s+(\w+))?(?:,\s*(pending))?\s*$')
BUILDING_EXPORTS = ("lodges", "buildings", "signs")


def catalog_names():
    """{name: prefab type} from the lines between // NAMES-BEGIN and // NAMES-END."""
    lines = open(CATALOG, encoding="utf-8").read().splitlines()
    b = [i for i, t in enumerate(lines) if t.strip() == "// NAMES-BEGIN"]
    e = [i for i, t in enumerate(lines) if t.strip() == "// NAMES-END"]
    assert len(b) == 1 and len(e) == 1 and b[0] < e[0], f"NAMES markers {b} / {e}"
    names = {}
    for i in range(b[0] + 1, e[0]):
        m = NAMES_LINE.match(lines[i])
        assert m, f"SkiCatalog.cs line {i + 1} does not match the NAMES format: {lines[i].strip()}"
        names[m.group(1)] = m.group(2)
    assert names, "0 catalog names"
    return names


def check_prefab_names(recipes):
    names = catalog_names()

    def is_building(prefab, where):
        assert names.get(prefab) == "BuildingPrefab", \
            f"{where}: prefab {prefab!r} is not a BuildingPrefab name of SkiCatalog NAMES " \
            f"({names.get(prefab)})"
    for key in sorted(recipes):
        is_building(recipes[key].prefab, f"recipe {key}")
    src = open(os.path.join(HERE, "welcome_sign.py"), encoding="utf-8").read()
    m = re.search(r'^PREFAB\s*=\s*"([^"]+)"', src, re.M)
    assert m, "welcome_sign.py has no PREFAB"
    is_building(m.group(1), "welcome_sign.PREFAB")
    found, props = [], []
    for cat in BUILDING_EXPORTS:
        for path in sorted(glob.glob(os.path.join(REPO, "assets", "import", cat, "*", "*",
                                                  "*.manifest.json"))):
            data = json.load(open(path, encoding="utf-8"))
            prefab = data.get("prefab")
            if not prefab and data.get("object_prefab"):
                # fine-detail pass: a sign-set PROP (signs/B, cs2_signset) is a StaticObjectPrefab
                # hosted by a building's identity sub-object row, not a SkiCatalog building
                props.append(data["object_prefab"])
                continue
            assert prefab, f"{path}: no prefab field"
            is_building(prefab, os.path.basename(path))
            found.append(prefab)
    print(f"ok prefab names: {len(recipes)} recipes + welcome_sign.PREFAB + {len(found)} exported "
          f"building manifests are SkiCatalog BuildingPrefab names ({len(names)} catalog names); "
          f"{len(props)} sub-object prop manifest(s) skipped {props}")


def _child(script, argv, log):
    cmd = [bpy.app.binary_path, "--background", "--factory-startup", "--python-exit-code", "1",
           "--python", os.path.join(HERE, script), "--"] + argv
    fh = open(log, "w", encoding="utf-8")
    return subprocess.Popen(cmd, stdout=fh, stderr=subprocess.STDOUT), fh


def _only_manifest(d):
    found = sorted(glob.glob(os.path.join(d, "*", "*.manifest.json")))
    assert len(found) == 1, f"{d}: {len(found)} manifests"
    return json.load(open(found[0], encoding="utf-8"))


def check_cross_process(recipes, tmp):
    """buildings-review-pipeline 4: every registered recipe and the Welcome Sign, --blockout in
    TWO separate Blender processes (hash seed and BMesh set order change per process; one
    process cannot see it) -> identical manifest mesh hashes, plus room cells (recipes) or
    the band PNG sha1s (sign). The two processes of an asset run side by side."""
    jobs = [(key, "lodge.py", ["--recipe", key, "--blockout"], recipes[key].letter)
            for key in sorted(recipes)] + [("sign", "welcome_sign.py", ["--blockout"], "A")]
    for key, script, argv, letter in jobs:
        procs, mans = [], []
        for k in range(2):
            d = os.path.join(tmp, "xp", f"{key}{k}", letter)
            log = os.path.join(tmp, f"xp_{key}{k}.log")
            procs.append((_child(script, argv + ["--out", d], log), d, log))
        for (p, fh), d, log in procs:
            rc = p.wait(timeout=1800)
            fh.close()
            if rc != 0:
                tail = open(log, encoding="utf-8", errors="replace").read()[-2000:]
                raise RuntimeError(f"{key}: child Blender exit {rc} ({' '.join(argv)})\n{tail}")
            mans.append(_only_manifest(d))
        h = [m["mesh_hash"] for m in mans]
        assert h[0] == h[1], f"{key}: blockout mesh hashes differ across processes {h}"
        if key == "sign":
            b = [m["band"]["png_sha1"] for m in mans]
            assert b[0] == b[1], f"sign: band PNGs differ across processes {b}"
        else:
            assert mans[0]["rooms"] == mans[1]["rooms"], f"{key}: room cells differ across processes"
        print(f"ok {key}: blockout identical across 2 processes {h[0]}")


def check_pane_rule(recipe):
    """None for the legacy A-frame massing: L1's prow panes sit under the AFRAME roof by
    design (constants verbatim from base_lodge.py; buildings-review-geometry "known, not
    re-raised"): 18 top-row rays meet roof_prowN / roof_prowS at 0.60 m, the deep eave over
    the glass, not a part across it."""
    from recipes import l5_swanky_bottom as SW
    if recipe.massing == "aframe":
        return None
    lodge.clear_scene()
    parts = lodge.build_parts(recipe, {"tower": True, "roof_windows": "win"})
    n = SW.check_pane_clearance(parts, recipe.key)
    lodge.clear_scene()
    return n


def base_lodge_constants():
    """Module-level numeric assignments of base_lodge.py, evaluated in file order
    (literals and arithmetic on earlier names only)."""
    src = open(os.path.join(HERE, "base_lodge.py"), encoding="utf-8").read()
    ns = {}
    for node in ast.parse(src).body:
        if not isinstance(node, ast.Assign) or len(node.targets) != 1:
            continue
        tgt = node.targets[0]
        names = [tgt.id] if isinstance(tgt, ast.Name) else \
            [e.id for e in tgt.elts if isinstance(e, ast.Name)] if isinstance(tgt, ast.Tuple) else []
        if not names:
            continue
        for sub in ast.walk(node.value):
            if isinstance(sub, ast.Call):
                break
        else:
            try:
                val = eval(compile(ast.Expression(node.value), "base_lodge", "eval"),
                           {"__builtins__": {}}, dict(ns))
            except Exception:  # noqa: BLE001 - names we do not track
                continue
            vals = list(val) if isinstance(val, tuple) else [val]
            if len(vals) == len(names) and all(isinstance(v, (int, float)) for v in vals):
                ns.update(dict(zip(names, vals)))
    return ns


def check_l1_constants():
    bl = base_lodge_constants()
    for k, v in R.AFRAME.items():
        assert k in bl, f"base_lodge.py has no {k}"
        assert bl[k] == v, f"AFRAME[{k}] = {v} but base_lodge.py has {bl[k]}"
    d = R.aframe_derived(R.AFRAME)
    expect = {"MAIN_PITCH": 1.32558, "S2_Z": 6.758, "PROW_PITCH": 1.325, "S2P_Z": 6.625,
              "DECK_TOP": 4.66, "MAIN_PITCH_DEG": 52.97}
    for k, v in expect.items():
        assert abs(d[k] - v) < 1e-3, f"derived {k} = {d[k]}, expected {v}"
        if k in bl:
            assert abs(bl[k] - d[k]) < 1e-9, f"{k}: base_lodge {bl[k]} vs aframe_derived {d[k]}"
    print(f"ok L1: {len(R.AFRAME)} AFRAME constants == base_lodge.py (ast); derived "
          f"MAIN_PITCH {d['MAIN_PITCH']:.5f} ({d['MAIN_PITCH_DEG']:.2f} deg), S2_Z {d['S2_Z']:.3f}, "
          f"PROW_PITCH {d['PROW_PITCH']:.3f}, S2P_Z {d['S2P_Z']:.3f}, DECK_TOP {d['DECK_TOP']:.2f}")
    # the W slab centre plane through the eave tip and the ridge line (pre-yaw parts)
    lodge.clear_scene()
    parts = lodge.aframe_parts(R.AFRAME, {})
    slab = next(o for o, _ in parts if o.name == "roof_mainW")
    me = slab.data
    top = max(me.polygons, key=lambda p: p.normal.z * p.area)
    n = np.array(top.normal)
    p0 = np.array(me.vertices[top.vertices[0]].co)
    centre_off = 0.12                       # thick 0.24 / 2, perpendicular to the slab

    def plane_dist(x, z):
        return float(np.dot(np.array((x, 0.0, z)) - p0, n)) + centre_off
    for (x, z) in ((d["S2_X0"] - d["OVER"], d["EAVE_Z"]), (d["RIDGE_AT_X"], d["RIDGE_Z"]),
                   (d["S2_X0"], d["S2_Z"])):
        dist = plane_dist(x, z)
        assert abs(dist) < 1e-3, f"W slab centre plane misses ({x}, {z}) by {dist:.4f} m"
    print("ok L1: W slab centre plane passes through the eave tip (-13.1, 5.30), the ridge "
          "(-4.5, 16.7) and the wall plate (-12.0, S2_Z)")


def check_recipe(recipe, tmp):
    args = lodge.parse_args(["--recipe", recipe.key, "--blockout",
                             "--out", os.path.join(tmp, recipe.key, recipe.letter)])
    r = lodge.run(recipe, args)
    name = r["name"]
    rep = cs2_validate._Report(name)
    cs2_validate.lint_asset_name(name, rep)
    assert not [f for f in rep if f[0] == "ERROR"], f"{name}: lint {list(rep)}"
    assert recipe.interior in name, f"{name}: keyword {recipe.interior} missing"
    assert r["tris"][0] <= recipe.budget.lod0_cap, f"{name}: LOD0 {r['tris'][0]} > cap"
    obj = bpy.data.objects[name]
    table = __import__("json").loads(obj.data[cs2_asset.PART_TABLE_PROP])
    attr = obj.data.attributes.get("part")
    vals = set(int(v.value) for v in attr.data)
    assert vals <= set(range(1, len(table) + 1)), "untagged faces"
    b = r["base"][0]
    if recipe.base_mode == "engine":
        assert b["verdict"] == "OK", b
    else:
        assert b["ok_edges"] == 0, b
    print(f"ok {recipe.key} ({name}): lot margins x {r['lot']['margin_x']} / y "
          f"{r['lot']['margin_y']} m, arrival centroid x {r['facing']['arrival_centroid_x']}, "
          f"base {b['verdict']} ({b['ok_edges']} OK edges), LOD0 {r['tris'][0]} <= "
          f"{recipe.budget.lod0_cap}, rooms {len(r['cells'])}, predicted w=1 px/m @ "
          f"{recipe.tex}: {r['area']['predicted_ppm_w1'].get(recipe.tex)}")


def demo_volumes_recipe():
    """A small synthetic `volumes` recipe (not registered): every facade token, a gable
    roof extra, an extra builder, a recipe lamp - the engine path the L2-L5 / school /
    patrol recipes use."""
    from bldg_recipes import Recipe, Budget, Volume, FacadeRule, Extra
    from cs2_bldg import RoofSpec, WinSpec
    return Recipe(
        key="demo", asset="SkiDemoHutCommercialLow", prefab="SkiDemoHut", category="buildings",
        lot=(2, 2), base_mode="engine", plinth=None, yaw_deg=0.0, tex=1024, allow_4096=False,
        budget=Budget(1500, 6000, 650, 200, 500), massing="volumes", letter="A",
        volumes=(Volume("body", -4, 4, -5, 5, 0.0, ((3.0, "stone_rubble"), (2.6, "timber_lap")),
                        ground=True, roofed=True),),
        roofs=(Extra("gable_roof", dict(name="body", spec=RoofSpec(
            "gable", -4, 4, -5, 5, 5.6, 30.0, axis="Y", overhang=0.6), tails=1.2, snow_guard=1)),),
        facades=(FacadeRule("body", "+X", 0, "WDW", 2.4, WinSpec(1.2, 1.4, 0.9, lights=2), margin=0.6),
                 FacadeRule("body", "-X", 1, "WW", 2.6, WinSpec(1.4, 1.2, 0.9, lights=2), room="row"),
                 FacadeRule("body", "+Y", 0, "G", 4.0, WinSpec(1.0, 1.0, 0.0)),
                 FacadeRule("body", "-Y", 1, "SB", 3.0, WinSpec(1.0, 1.0, 0.0))),
        extras=(Extra("flag_mast", dict(name="f", x=3.6, y=-3.0, h=6.0)),),
        lamps=(dict(name="side", at=(-4.0, 3.0, 2.2), normal="-X"),),
        ground_prefixes=("wall_body",), arrival_prefixes=("entry_",))


def main():
    t0 = time.perf_counter()
    tmp0 = tempfile.mkdtemp(prefix="lodge_demo_")
    try:
        check_recipe(demo_volumes_recipe(), tmp0)
    finally:
        shutil.rmtree(tmp0, ignore_errors=True)
    check_l1_constants()
    recipes = R.load_recipes()
    check_prefab_names(recipes)
    tmp = tempfile.mkdtemp(prefix="lodge_contracts_")
    fast = "--fast" in (sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    try:
        for key in sorted(recipes):
            check_recipe(recipes[key], tmp)
            n = check_pane_rule(recipes[key])
            print(f"ok {key}: pane clearance 0.6 m on {n} panes" if n is not None else
                  f"skip {key}: pane clearance (legacy A-frame massing, eave over the prow panes)")
        if fast:
            print("skip cross-process blockouts (--fast)")
        else:
            check_cross_process(recipes, tmp)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    print(f"PASS test_lodge_contracts: {len(recipes)} recipe(s) {sorted(recipes)}, prefab names, "
          f"pane clearance{'' if fast else ', cross-process blockouts (+ sign)'} "
          f"({time.perf_counter() - t0:.0f} s)")


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        print("FAIL test_lodge_contracts")
        sys.exit(1)
