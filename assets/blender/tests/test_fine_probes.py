"""
test_fine_probes - gates for assets/blender/fine_probes.py (probes B; build/probes-b.md).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_fine_probes.py

Checks (exit 1 on any failure):
  FP1 sharedAssets  each sharer's settings.json holds exactly the four `<sharer>_<map>.png` keys,
                    each pointing at `../SkiProbeSheetB/SkiProbeSheetB_<map>.png`, and the target
                    exists; no sharer ships a PNG; the owner ships exactly four 2048^2 maps.
  FP2 sign cards    measure_all() on the CURRENT DQ base FBX: both spots OK (flat <= 6 mm, normal
                    <= 2 deg, 2 m clear); the exported card front planes sit CARD_T (0.020) off the
                    measured wall +-1 mm, card centres at their (y, z) +-1 mm, every front face
                    normal along the wall's outward normal (-x); the shipped manifest was measured on
                    the same FBX sha1 (stale-host guard).
  FP3 lodge rows    every lodge row is OK (no lodge geometry above 0.05 m in its footprint, inside
                    the 6 x 5 lot) after the in-game -> raw x negation.
  FP4 maps          sign set: lit _Emissive texels only inside card A's inner field, BaseColor alpha
                    255; rack: ControlMask G only in the accent rect, R = B = A = 0 everywhere.
  FP5 determinism   two in-process builds of every mesh hash identically; paint_* twice equal.
  FP6 shipped       an in-process export to a temp root writes PNGs + settings.json byte-identical
                    to assets/import/probes/B (the FBX carries the clock: compared by mesh hash in
                    the manifest instead).
  FP7 validator     owner / sign set / rack 0 errors; sharers 0 errors when staged with the owner
                    maps under their own names (the importer's view after sharedAssets).
"""

import hashlib
import json
import os
import shutil
import sys
import tempfile
import traceback

import bpy
import numpy as np

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir))
sys.path.insert(0, HERE)
import fine_probes as F     # noqa: E402
import cs2_validate         # noqa: E402

SHIPPED = os.path.join(REPO, "assets", "import", "probes", "B")
FAILS = []


def ok(cond, msg):
    if not cond:
        FAILS.append(msg)
        print(f"[test_fine_probes] FAIL {msg}")
    else:
        print(f"[test_fine_probes] ok   {msg}")


def sha1(path):
    h = hashlib.sha1()
    with open(path, "rb") as fh:
        h.update(fh.read())
    return h.hexdigest()


def arr_hash(maps):
    h = hashlib.sha1()
    for k in sorted(maps):
        a = maps[k][0] if isinstance(maps[k], tuple) else maps[k]
        h.update(k.encode())
        h.update(np.ascontiguousarray(a).tobytes())
    return h.hexdigest()


def remove(obj):
    me = obj.data
    bpy.data.objects.remove(obj, do_unlink=True)
    if me.users == 0:
        bpy.data.meshes.remove(me)


def main():
    F.clear_scene()
    meas = F.measure_all()

    # FP2 / FP3 measurement
    for c in meas["cards"]:
        ok(c["ok"], f"FP2 {c['key']} wall spot OK ({c.get('reasons')}) flat {c.get('flat_mm')} mm, "
                    f"normal {c.get('normal_err_deg')} deg, parts {c.get('parts_hit')}")
    for r in meas["lodge_rows"]:
        ok(r["ok"], f"FP3 lodge row {r['asset']} at in-game {r['m_Position_in_game']} OK ({r['reasons']})")

    # FP5 determinism
    builders = {F.OWNER: F.build_owner, F.USER: F.build_user, F.LATE: F.build_late,
                F.RACK: F.build_rack, F.SIGNSET: lambda name=F.SIGNSET: F.build_signset(meas, name)}
    hashes = {}
    for name, b in builders.items():
        o1 = b(name=name + "T1")
        h1 = F.mesh_hash(o1)
        o2 = b(name=name + "T2")
        h2 = F.mesh_hash(o2)
        ok(h1 == h2, f"FP5 {name} two builds hash identically ({h1[:12]})")
        hashes[name] = (h1, o1)
        remove(o2)
    m1, _ = F.paint_sheet()
    m2, _ = F.paint_sheet()
    ok(arr_hash(m1) == arr_hash(m2), "FP5 paint_sheet deterministic")
    s1, _ = F.paint_signset()
    s2, _ = F.paint_signset()
    ok(arr_hash(s1) == arr_hash(s2), "FP5 paint_signset deterministic")
    ok(arr_hash(F.paint_rack()) == arr_hash(F.paint_rack()), "FP5 paint_rack deterministic")

    # FP2 geometry of the sign set against the measured walls
    so = hashes[F.SIGNSET][1]
    me = so.data
    rows = {r["key"]: r for r in meas["cards"]}
    for card in F.CARDS:
        wall = rows[card["key"]]["wall_x_measured"]
        out = card["outward"]
        front_x = wall + out * F.CARD_T
        fronts = []
        for p in me.polygons:
            c = p.center                                    # Blender frame: x = raw x
            n = p.normal
            if abs(c.x - front_x) < 0.001 and n.x * out > 0.999 and abs(-c.y - card["z"]) < card["w"]:
                fronts.append(p)
        ok(len(fronts) == 1, f"FP2 {card['key']}: one front face on x {front_x:.4f} facing {out:+.0f}x "
                             f"(found {len(fronts)})")
        if fronts:
            c = fronts[0].center
            ok(abs(c.z - card["y"]) < 0.001 and abs(-c.y - card["z"]) < 0.001,
               f"FP2 {card['key']}: centre y {c.z:.4f} z {-c.y:.4f} vs ({card['y']}, {card['z']})")
            ok(abs(fronts[0].area - card["w"] * card["h"]) < 1e-4,
               f"FP2 {card['key']}: face {fronts[0].area:.4f} m2 = {card['w']} x {card['h']}")
    mpath = os.path.join(SHIPPED, F.SIGNSET, F.SIGNSET + ".manifest.json")
    if os.path.isfile(mpath):
        with open(mpath, encoding="utf-8") as fh:
            man = json.load(fh)
        ok(man["host"]["fbx_sha1_measured"] == meas["dq_fbx_sha1"],
           f"FP2 shipped sign set measured on the current DQ base FBX ({meas['dq_fbx_sha1'][:12]}); "
           f"a mismatch means: re-run --export before the import")
        ok(man["mesh_hash"] == hashes[F.SIGNSET][0], "FP2 shipped sign-set mesh hash = this build")

    # FP4 maps
    emi = s1["Emissive"][0]
    lit = emi[..., 0:3].max(axis=-1) > 1e-6
    x0, y0, x1, y1 = F.R_CARD_A
    b = int(round(0.015 * F.CARD_PPM))
    inner = np.zeros(lit.shape, bool)
    inner[y0 + b:y1 - b, x0 + b:x1 - b] = True
    ok(lit.any() and not (lit & ~inner).any(),
       f"FP4 sign set: {int(lit.sum())} lit texels, all inside card A's inner field")
    ok(float(s1["BaseColor"][0][..., 3].min()) == 1.0, "FP4 sign set BaseColor alpha 1")
    ctl = F.paint_rack()["ControlMask"][0]
    acc = np.zeros(ctl.shape[:2], bool)
    ax0, ay0, ax1, ay1 = F.R_ACCENT
    acc[ay0:ay1, ax0:ax1] = True
    ok(bool((ctl[..., 1][acc] == 1.0).all()) and not (ctl[..., 1][~acc] > 0).any()
       and not ctl[..., 0].any() and not ctl[..., 2].any() and not ctl[..., 3].any(),
       "FP4 rack ControlMask: G = 1 on the accent rect only; R, B, A = 0")

    for _n, (_h, o) in hashes.items():
        remove(o)

    # FP6 / FP1 / FP7: export to temp, compare with the shipped folder
    tmp = tempfile.mkdtemp(prefix="test_fine_probes_")
    try:
        data = F.export(tmp, None, args=["test"], meas=meas)
        for name in F.ALL:
            d = os.path.join(tmp, name)
            sd = os.path.join(SHIPPED, name)
            files = sorted(f for f in os.listdir(d) if f.endswith((".png", ".json")) and "manifest" not in f)
            if os.path.isdir(sd):
                same = all(os.path.isfile(os.path.join(sd, f)) and sha1(os.path.join(sd, f)) == sha1(os.path.join(d, f))
                           for f in files)
                ok(same, f"FP6 {name}: {len(files)} PNG / settings files byte-identical to the shipped folder")
                with open(os.path.join(sd, name + ".manifest.json"), encoding="utf-8") as fh:
                    man = json.load(fh)
                ok(man["mesh_hash"] == data[name]["mesh_hash"], f"FP6 {name}: shipped manifest mesh hash = this build")
            else:
                ok(False, f"FP6 {name}: shipped folder missing ({sd})")
        # FP1
        own = [f for f in os.listdir(os.path.join(tmp, F.OWNER)) if f.endswith(".png")]
        ok(sorted(own) == sorted(f"{F.OWNER}_{m}.png" for m in F.MAPS4), f"FP1 owner ships the four maps {sorted(own)}")
        for m in F.MAPS4:
            w = cs2_validate.read_png_header(os.path.join(tmp, F.OWNER, f"{F.OWNER}_{m}.png"))[0]
            ok(w == 2048, f"FP1 owner {m} is {w} px")
        for name in F.SHARERS:
            d = os.path.join(tmp, name)
            with open(os.path.join(d, "settings.json"), encoding="utf-8") as fh:
                st = json.load(fh)
            sa = st.get("sharedAssets", {})
            want = {f"{name}_{m}.png": f"../{F.OWNER}/{F.OWNER}_{m}.png" for m in F.MAPS4}
            ok(sa == want and set(st) == {"sharedAssets"}, f"FP1 {name} sharedAssets = the four owner maps")
            ok(all(os.path.isfile(os.path.normpath(os.path.join(d, v))) for v in sa.values()),
               f"FP1 {name} every target resolves from the sharer folder")
            ok(not [f for f in os.listdir(d) if f.endswith(".png")], f"FP1 {name} ships no PNG")
        # FP7
        for name in (F.OWNER, F.SIGNSET, F.RACK):
            v = data[name]["validation"]
            ok(v["errors"] == 0 and not data[name]["mesh_validation"]["errors"],
               f"FP7 {name} validator 0 errors ({v['errors']} / mesh {data[name]['mesh_validation']['errors']}), "
               f"{v['warnings']} warnings")
        for name in F.SHARERS:
            v = data[name]["validation_staged_with_owner_maps"]
            ok(v["errors"] == 0, f"FP7 {name} staged validator 0 errors, {v['warnings']} warnings")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        FAILS.append("exception")
    print(f"[test_fine_probes] {'PASS' if not FAILS else 'FAIL'}: {len(FAILS)} failure(s)")
    sys.exit(1 if FAILS else 0)
