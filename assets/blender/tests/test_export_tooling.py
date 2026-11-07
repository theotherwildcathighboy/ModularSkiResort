"""
test_export_tooling.py: T83 export-round tooling gaps (DECISIONS 290, 298).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_export_tooling.py

A. cs2_validate.guess_kind reads the kind from the asset's manifest (school / patrol / sign have no
building word in their names). The name guess is only the fallback.

B. The one-batch layout gate: a sharedAssets path must be `../<Owner>/<file>`. A folder whose sharer
reaches out of its family folder gets a `family-folder` ERROR from validate_asset_folder_ex.

C. welcome_sign takes the letter: default A = the old signs/A + SkiWelcomeSignA, X -> signs/X.

D. lodge.py: every tiled building (all five lodges, school, both patrol huts) exports under ONE
parent (assets/import/buildings/<letter>) with the owner at `../<Owner>`. An untiled build keeps its
category.

E. lodge.py --finalize recomposes the family sheet and installs the owner maps (no building built).
The argument guards always run. The sheet build itself (2048 px, ~7.5 min) runs only with `--
--full`.

Prints one PASS line and exits 0. Any failure raises (exit 1 with --python-exit-code 1).
"""

import json
import os
import shutil
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import cs2_validate   # noqa: E402
import lodge          # noqa: E402
import welcome_sign   # noqa: E402

CHECKS = 0


def check(cond, msg):
    global CHECKS
    CHECKS += 1
    if not cond:
        raise AssertionError(msg)


def write_json(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(data, fh)


def test_guess_kind(tmp):
    def manifest(name, data):
        write_json(os.path.join(tmp, name, f"{name}.manifest.json"), data)
        return os.path.join(tmp, name)

    # the three export-round-X cases: the name says "prop", the manifest says building
    school = manifest("SkiSchoolCommercialLowX", {"generator": "lodge.py", "recipe": "school", "lot": {}})
    check(cs2_validate.guess_kind("SkiSchoolCommercialLowX") == "prop", "name-only guess changed")
    check(cs2_validate.guess_kind("SkiSchoolCommercialLowX", school) == "building", "school manifest ignored")
    patrol = manifest("SkiPatrolHQY", {"generator": "lodge.py", "recipe": "P2", "lot": {"x": 1}})
    check(cs2_validate.guess_kind("SkiPatrolHQY", patrol) == "building", "patrol manifest ignored")
    sign = manifest("SkiWelcomeSignX", {"generator": "welcome_sign.py", "kind": "building"})
    check(cs2_validate.guess_kind("SkiWelcomeSignX", sign) == "building", "sign manifest ignored")
    # free-text kinds lead with the validator kind
    prop = manifest("SkiDressToolsB", {"kind": "prop (hosted sub-object, lean; no engine base rule)"})
    check(cs2_validate.guess_kind("SkiDressToolsB", prop) == "prop", "'prop (...)' not read as prop")
    hang = manifest("SkiSafetyNetA", {"kind": "hanging (lane mesh tile; cs2_validate has no lane kind)"})
    check(cs2_validate.guess_kind("SkiSafetyNetA", hang) == "hanging", "'hanging (...)' not read")
    # a kind the validator does not know falls back to the name guess
    other = manifest("SkiLiftTowerDQ10O", {"kind": "support"})
    check(cs2_validate.guess_kind("SkiLiftTowerDQ10O", other) == "prop", "unknown kind must fall back")
    # no manifest / broken manifest / nothing useful: the name guess, unchanged
    check(cs2_validate.guess_kind("SkiLodgeAFrameX", os.path.join(tmp, "nowhere")) == "building", "no-manifest fallback")
    bad = os.path.join(tmp, "SkiChairBrokenA")
    os.makedirs(bad)
    with open(os.path.join(bad, "SkiChairBrokenA.manifest.json"), "w") as fh:
        fh.write("{not json")
    check(cs2_validate.guess_kind("SkiChairBrokenA", bad) == "hanging", "broken-manifest fallback")
    # a lodge.py manifest WITHOUT a lot is not trusted as a building
    nolot = manifest("SkiThingA", {"generator": "lodge.py"})
    check(cs2_validate.guess_kind("SkiThingA", nolot) == "prop", "lot-less lodge manifest trusted")
    print("[test] A guess_kind: manifest first, name fallback")


def test_family_folder(tmp):
    err = cs2_validate.family_folder_error
    check(err("../SkiSchoolY/SkiSchoolY_BaseColor.png") is None, "sibling owner flagged")
    for bad in ("../../buildings/X/SkiSchoolX/SkiSchoolX_BaseColor.png", "../../../buildings/X/O/O_BaseColor.png",
                "SkiSchoolY/SkiSchoolY_BaseColor.png", "../O/sub/O_BaseColor.png", "../../O_BaseColor.png",
                "../O/../O_BaseColor.png", "..\\..\\X\\O\\O_BaseColor.png"):
        check(err(bad) is not None, f"{bad!r} leaves the family folder but passed")

    maps = ("BaseColor", "Normal", "MaskMap", "ControlMask")

    def folder(family, target_dir, sub):
        d = os.path.join(tmp, family, sub)
        write_json(os.path.join(d, "settings.json"),
                   {"sharedAssets": {f"{sub}_{m}.png": f"{target_dir}/SkiOwnerY/SkiOwnerY_{m}.png" for m in maps}})
        return d

    own = os.path.join(tmp, "famgood", "SkiOwnerY")
    os.makedirs(own)
    good = folder("famgood", "..", "SkiSharerY")
    bad = folder("fambad", "../../famgood", "SkiSharerY")
    c_good = [c for _l, c, _m in cs2_validate.validate_asset_folder_ex(good, "SkiSharerY", {"kind": "building"})[0]
              if _l == "ERROR"]
    c_bad = [c for _l, c, _m in cs2_validate.validate_asset_folder_ex(bad, "SkiSharerY", {"kind": "building"})[0]
             if _l == "ERROR"]
    check("family-folder" not in c_good, f"sibling sharer flagged: {c_good}")
    check("family-folder" in c_bad, f"escaping sharer not flagged: {c_bad}")
    print("[test] B family-folder gate: sibling ok, escaping sharedAssets ERROR")


def test_sign_letter():
    d, n = welcome_sign.letter_names("A")
    check(d == os.path.join(welcome_sign.IMPORT_ROOT, "A") and n == "SkiWelcomeSignA", f"default changed {d} {n}")
    d, n = welcome_sign.letter_names("X")
    check(os.path.basename(d) == "X" and os.path.basename(os.path.dirname(d)) == "signs", f"X folder {d}")
    check(n == "SkiWelcomeSignX", f"X name {n}")
    for bad in ("", "a", "AB", "1", None, "../A"):
        try:
            welcome_sign.letter_names(bad)
        except ValueError:
            continue
        raise AssertionError(f"letter {bad!r} accepted")
    print("[test] C welcome_sign --letter: A default, X -> signs/X, junk rejected")


def test_one_batch_layout():
    keys = [("--lodge", k) for k in ("L1", "L2", "L3", "L4", "L5")] + [("--school", None), ("--patrol", "p1"),
                                                                       ("--patrol", "p2")]
    parents, owners = set(), set()
    args_x = None
    for flag, val in keys:
        argv = [flag] + ([val] if val else []) + ["--tiled", "--export", "--letter", "X"]
        args = lodge.parse_args(argv)
        recipe = lodge.select_recipe(args)
        from dataclasses import replace
        recipe = replace(recipe, tiled=True)
        out = lodge._out_dir(recipe, args, "X")
        name = lodge.asset_name(recipe, "X", None, args.interior, args.glass)
        asset_dir = os.path.join(out, name)
        odir, oname = lodge.sheet_owner(args, "X")
        parents.add(os.path.normcase(os.path.dirname(asset_dir)))
        owners.add(os.path.normcase(odir))
        rel = os.path.relpath(odir, asset_dir).replace("\\", "/")
        if recipe.key == lodge.SHEET_OWNER_KEY:          # the owner carries the maps itself
            check(rel == ".", f"owner is not its own folder: {rel!r}")
        else:
            check(rel == f"../{oname}", f"{recipe.key}: owner reached by {rel!r}, not '../{oname}'")
            check(cs2_validate.family_folder_error(f"{rel}/{oname}_BaseColor.png") is None,
                  f"{recipe.key}: the shared_prefix path fails the family-folder gate")
        args_x = args
    check(len(parents) == 1, f"eight tiled buildings in {len(parents)} parent folders: {sorted(parents)}")
    (parent,) = parents
    check(parent == os.path.normcase(os.path.join(lodge.IMPORT_ROOT, "buildings", "X")), f"family folder {parent}")
    check(len(owners) == 1, "one sheet owner")
    sdir, _ = lodge.sheet_paths(args_x, "X")
    check(os.path.normcase(os.path.dirname(sdir)) == parent, "the sheet workspace is not in the family folder")
    # untiled builds are unchanged: their own category
    args = lodge.parse_args(["--lodge", "L1", "--export", "--letter", "X"])
    rc = lodge.select_recipe(args)
    check(not rc.tiled, "L1 is tiled by default?")
    check(lodge._out_dir(rc, args, "X") == os.path.join(lodge.IMPORT_ROOT, rc.category, "X"), "untiled category moved")
    # a --out build is the caller's folder, with or without tiling
    check(lodge._out_dir(rc, lodge.parse_args(["--lodge", "L1", "--out", "somewhere"]), "X")
          == os.path.abspath("somewhere"), "--out ignored")
    print("[test] D one-batch layout: 8 tiled buildings -> one family folder, owner at ../<Owner>")


def test_finalize(tmp):
    out = os.path.join(tmp, "Z")
    argv = ["--school", "--tiled", "--out", out, "--letter", "Z", "--sheet-tex", "2048", "--finalize"]
    args = lodge.parse_args(argv)
    check(args.finalize, "--finalize not parsed")
    for bad in (["--school", "--tiled", "--letter", "Z", "--finalize"],               # no --out / --export
                ["--school", "--tiled", "--export", "--finalize"]):                   # no letter
        try:
            lodge.finalize_family_sheet(lodge.parse_args(bad))
        except RuntimeError:
            continue
        raise AssertionError(f"--finalize accepted {bad}")
    if "--full" not in sys.argv:
        print("[test] E --finalize: parse + guards ok (sheet build skipped, pass '-- --full' to run it)")
        return
    res = lodge.finalize_family_sheet(args)
    odir, oname = lodge.sheet_owner(args, "Z")
    check(os.path.normcase(res["owner_dir"]) == os.path.normcase(odir), "installed outside the owner folder")
    check(os.path.dirname(odir) == os.path.abspath(out), "owner not in the family folder")
    for m in ("BaseColor", "Normal", "MaskMap", "ControlMask", "Emissive"):
        p = os.path.join(odir, f"{oname}_{m}.png")
        check(os.path.isfile(p) and os.path.getsize(p) > 1000, f"{p} missing")
    sdir, sname = lodge.sheet_paths(args, "Z")
    check(os.path.isfile(os.path.join(sdir + ".aux", f"{sname}.layout.json")), "layout json missing")
    check(len(res["hash"]) == 16, f"layout hash {res['hash']!r}")
    print(f"[test] E --finalize: sheet {sname} hash {res['hash']} -> {oname}")


def run():
    tmp = tempfile.mkdtemp(prefix="t83_")
    try:
        test_guess_kind(os.path.join(tmp, "a"))
        test_family_folder(os.path.join(tmp, "b"))
        test_sign_letter()
        test_one_batch_layout()
        test_finalize(os.path.join(tmp, "e"))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    run()
    print(f"PASS: test_export_tooling ({CHECKS} checks)")
