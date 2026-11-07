"""
test_dress_kit - gates for assets/blender/dress_kit.py (the dressing kit, props letter B, build/dress-kit.md).
Contracts DK1-DK6 of design/fine-detail-pass.md G.2 as corrected by its skeptic (FS3: one 1024^2 set, DK6 >= 250
px/m), each with a NEGATIVE that must fail.

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_dress_kit.py [-- --quick]

- DK1: LOD0 tris <= the design D.1 cap of the part (re-imported FBX). Negative: cap = tris - 1.
- DK2: one set: only the owner folder has PNGs, and every other folder's settings.json maps the four maps to the
  owner's existing files. Negatives: a key removed, a missing target.
- DK3: hi -> low normal is not flat: fine-detail rms >= 5 deg on each prop's islands (the benchmark metric,
  vanilla GardenBench01 10.7 deg). The metric equals the benchmark's nmstats.normal_stats_xy. Negative: a flat
  map.
- DK4: pivots: every prop stands on y 0, wall props have their back on z 0 (prefab frame), re-imported FBX.
  Negative: shifted 5 cm.
- DK5: no mirrored / zero-area UV face (winding vs the face normal). Negative: one face flipped.
- DK6: area-weighted median px/m of the full-weight texel-owning faces >= 250, kit and every prop. Negative: same
  UVs at 512^2.
- determinism: two child processes (--export --no-staging --no-subobj): the four PNGs and all eleven
  settings.json byte-identical, the FBX geometry + UVs equal (--quick skips it).
- parity: every A-frame gear item = cs2_bldg.ski_rack_frame's own (vertex sets), the wall-rack slot math =
  cs2_liftdress.ski_rack's, every rigid copy = its library item (<= 1e-5 m).
- DK8: no bindings: ski pair 32 tris, board 28, pole pair 44 (cs2_bldg.ski_gear default).
- validator: owner folder 0 errors. Every sharer 0 errors as the importer resolves it.
- FS12: cs2_asset.write_settings untouched: regenerated shipped settings.json byte-identical.
- staging: the five UNCOLORED object prefab templates parse as JSON. No colored base prefab is staged.
- DK9: kit color variants (2026-10-12, build/kit-colour-variants.md): seeded CIDs. kit_row refuses a colored prop
  without a set / an unknown set / an uncolored prop with a set. Every shipped host = ONE set = the palette.
  Staging = exactly the used variants, each complete (object prefab naming its mesh CID, .cid files, a FIXED
  ColorProperties: 1 set, no external channel, ranges 0). The six Parent base prefabs archived.
  kit-variants.json. The kit manifests say no Parent. The mesh-prefab clone (importer-shaped base -> types 0-12,
  every CID / bound / count kept) and variant_surgery on a fake ImportedData (idempotent). Negatives: a re-mint,
  three malformed bases, an overwrite (`-- --variants-only` runs DK9 alone).
"""

import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import traceback

import bpy
import numpy as np

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir))
sys.path.insert(0, HERE)
import dress_kit as D       # noqa: E402
import cs2_compose          # noqa: E402
import cs2_uv               # noqa: E402
import cs2_validate         # noqa: E402

QUICK = "--quick" in sys.argv
VARIANTS_ONLY = "--variants-only" in sys.argv
FAILS, SKIPS = [], []
NMSTATS = os.path.join(REPO, "docs", "research", "art", "vanilla-detail-benchmark.data", "scripts")


def ok(cond, msg):
    if not cond:
        FAILS.append(msg)
        print(f"[test_dress_kit] FAIL {msg}")
    else:
        print(f"[test_dress_kit] ok   {msg}")


def load_fbx(path):
    """Re-imported FBX -> (objs, mesh object) with the raw cm Y-up vertices as stored."""
    objs = cs2_validate.import_fbx_temp(path)
    return objs, [o for o in objs if o.type == "MESH"][0]


def raw_co(me):
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


# -- the checks (each returns True when the contract holds) --------------------------------

def dk1(tris, cap):
    return tris <= cap


def dk4(raw, kind):
    """raw = FBX vertices in cm, (x, y up, z = prefab z). Prefab min y == 0. Wall props: min z == 0 (the back on the
    wall plane, the prop toward +z)."""
    lo = raw.min(axis=0)
    good = abs(lo[1]) <= 0.01
    if kind == "wall":
        good = good and abs(lo[2]) <= 0.01
    return bool(good)


def dk5(me):
    uv = cs2_uv.loop_uvs(me.uv_layers[0])
    start, total = cs2_uv.face_loop_table(me)
    bad = 0
    for i in range(len(start)):
        s, t = int(start[i]), int(total[i])
        x, y = uv[s:s + t, 0], uv[s:s + t, 1]
        sa = 0.5 * float(np.dot(x, np.roll(y, -1)) - np.dot(y, np.roll(x, -1)))
        bad += sa <= 1e-12
    return bad


def dk6_median(me, size):
    """Area-weighted median px/m of a re-imported mesh (raw cm -> m)."""
    d = cs2_uv.face_px_per_m(me, me.uv_layers[0], size) * 100.0
    a = cs2_uv.face_world_areas(me) / 1e4
    okm = np.isfinite(d) & (a > 0)
    return D._wmedian(d[okm], a[okm])


def run_child(out_dir):
    cmd = [bpy.app.binary_path, "--background", "--factory-startup", "--python-exit-code", "1",
           "--python", os.path.join(HERE, "dress_kit.py"), "--", "--out", out_dir, "--export",
           "--no-staging", "--no-subobj"]
    r = subprocess.run(cmd, cwd=HERE, capture_output=True, text=True, timeout=1200)
    return r.returncode, (r.stdout[-1500:] + r.stderr[-1500:])


def fbx_signature(path):
    objs, o = load_fbx(path)
    try:
        me = o.data
        h = hashlib.sha1()
        h.update(np.round(raw_co(me), 3).tobytes())
        h.update(np.round(cs2_uv.loop_uvs(me.uv_layers[0]), 6).tobytes())
        return len(me.polygons), h.hexdigest()
    finally:
        cs2_validate.remove_objects(objs)




# -- DK9 kit color variants (2026-10-12, build/kit-colour-variants.md) ---------------------------------------

#: the palette (one set per building, the 18:50 ruling as built): shipped host -> color set
PALETTE = {"SkiSchoolCommercialLowA": "blue", "SkiLodgeAFrameCommercialLowB": "orange",
           "SkiLodgeHutCommercialLowB": "red", "SkiLodgeChaletCommercialLowB": "green",
           "SkiLodgeSwankyBottomCommercialLowB": "red", "SkiPatrolHutCommercialLowA": "red",
           "SkiPatrolHQCommercialLowA": "red", "SkiDQBaseStationA": "orange", "SkiGondolaBaseStationA": "orange",
           "SkiGondolaTopStationA": "orange", "SkiTramBaseStationA": "red", "SkiLiftBaseStationM": "yellow"}


def _family(host):
    """A host name without its generation (CommercialLow<letter> / <letter>): SkiPatrolHQCommercialLowX -> SkiPatrolHQ."""
    return re.sub(r"(CommercialLow)?[A-Z]$", "", host)


HYDRANT_MESH = os.path.join(REPO, "gamedata-mirror", "ImportedData", "c030ee3ff256df91", "SkiHydrantBoxA",
                            "SkiHydrantBoxA Mesh_3a28bf00d14e0b012b4cffacb9ecc91d.Prefab")


def _cp_json(txt):
    """A CS2 components fragment / prefab -> JSON (positional Unity Colors -> {"rgba": [...]}, $fstrref -> str)."""
    txt = txt.replace('$fstrref:"', '"$fstrref:')
    txt = re.sub(r'\{ "\$type": ("[^"]*"|\d+), ([-0-9.eE]+), ([-0-9.eE]+), ([-0-9.eE]+), ([-0-9.eE]+) \}',
                 r'{"$type": \1, "rgba": [\2, \3, \4, \5]}', txt)
    return json.loads(txt)


def synth_base_mesh(mesh_name):
    """An importer-shaped base mesh prefab (no components, tail types 2 / 3 / 4) from the deployed hydrant mesh:
    the probes-B 4e synthetic (the real importer output is the first unverified input)."""
    with open(HYDRANT_MESH, encoding="utf-8") as fh:
        hyd = fh.read()
    a, b = hyd.find('    "components": {'), hyd.find('    "m_GeometryAsset"')
    empty = ('    "components": {\n        "$id": 1,\n        "$type": "1|System.Collections.Generic.List`1[[Game.'
             'Prefabs.ComponentBase, Game]], mscorlib",\n        "$rlength": 0,\n        "$rcontent": [\n        ]\n'
             '    },\n')
    tail = re.sub(r'"(1[0-2])\|', lambda m: f'"{int(m.group(1)) - 8}|', hyd[b:])
    tail = re.sub(r'"\$type": 12\b', '"$type": 4', tail)
    return hyd[:a].replace("SkiHydrantBoxA Mesh", f"{mesh_name} Mesh") + empty + tail


def variants(tmp):
    import fine_detail as FD
    # V1 the registry: seeded CIDs, names, sets
    ok(FD.variant_cids("SkiDressSkiRackV2_Red") == (hashlib.md5(b"SkiResortMod.KitVariant.v1/SkiDressSkiRackV2_Red")
                                                      .hexdigest(),
                                                      hashlib.md5(b"SkiResortMod.KitVariantMesh.v1/SkiDressSkiRackV2_Red"
                                                                  b" Mesh").hexdigest()),
       "DK9 variant CIDs = md5(seed + name), object and mesh")
    ok(set(D.COLOURED) == {p for p, k in FD.KIT.items() if k.coloured} == {s["obj"] for s in D.PROPS
                                                                             if s["colour"] == "variants"},
       f"DK9 the colored props agree (dress_kit PROPS / fine_detail.KIT): {D.COLOURED}")
    ok(4 <= len(FD.COLOUR_SETS) <= 5 and all(len(c.rgb) == 3 and max(c.rgb) <= 1.0 for c in FD.COLOUR_SETS.values()),
       f"DK9 4-5 color sets, slot-2 values <= 1: {sorted(FD.COLOUR_SETS)}")
    for bad, why in ((("SkiDressSkiRack", None), "a colored prop without a set"),
                     (("SkiDressSkiRack", "pink"), "an unknown set"), (("SkiDressEStop", "red"), "an uncoloured prop with a set")):
        try:
            FD.kit_row(bad[0], (0.0, 0.0, 0.0), colour_set=bad[1])
            ok(False, f"DK9 negative: kit_row with {why} did not raise")
        except ValueError:
            ok(True, f"DK9 negative: kit_row with {why} raises")
    r = FD.kit_row("SkiDressSkiRackV3", (1.0, 0.0, 2.0), colour_set="green")
    ok(r["m_Object"] == FD.variant_cids("SkiDressSkiRackV3_Green")[0] and r["variant"] == "SkiDressSkiRackV3_Green"
       and FD.row_colour_error(r) is None, f"DK9 a colored row names its variant {r}")
    ok(FD.row_colour_error(dict(r, m_Object=FD.KIT["SkiDressSkiRackV3"].cid)) is not None
       and FD.row_colour_error(dict(r, colour_set="red")) is not None,
       "DK9 negative: the retired base CID / a mismatched set are row errors")
    # V2 shipped hosts: one set per building = the palette, CIDs seeded
    used = D.used_variants()
    per_host = {}
    for vn, u in used.items():
        for h in u["hosts"]:
            per_host.setdefault(h, set()).add(u["colour_set"])
    # every shipped generation of a building (A / B / M / N, X, Y folders) carries its family's one set
    fam = {_family(h): c for h, c in PALETTE.items()}
    want = {h: {fam[_family(h)]} for h in per_host if _family(h) in fam}
    ok(per_host == want and set(map(_family, PALETTE)) <= set(map(_family, per_host)),
       f"DK9 hosts' color sets = the palette ({len(per_host)} hosts, {len(fam)} families): {per_host}")
    # V3 staging: exactly the used variants, complete and fixed. The six base prefabs retired
    staged = D.staged_variant_names()
    ok(staged == sorted(used), f"DK9 staged variants = the used ones ({len(staged)} / {len(used)}): "
                              f"{sorted(set(staged) ^ set(used))}")
    ok(not [o for o in D.COLOURED if os.path.isdir(os.path.join(D.STAGING, o))]
       and all(os.path.isdir(os.path.join(D.RETIRED_DIR, o)) for o in D.COLOURED),
       "DK9 the six Parent base object prefabs are out of staging (archived under staging/archive)")
    with open(D.VARIANT_TABLE, encoding="utf-8") as fh:
        table = json.load(fh)
    ok([v["name"] for v in table["variants"]] == staged and not table["unstaged_but_used"],
       "DK9 kit-variants.json lists exactly the staged variants")
    for vn in staged:
        u = used[vn]
        rec = D.variant_record(u["prop"], u["colour_set"])
        d = os.path.join(D.STAGING, vn)
        mesh = rec["mesh_prefab"]
        files = sorted(os.listdir(d))
        ok(files == sorted([f"{vn}.Prefab", f"{vn}.Prefab.cid", f"{mesh}.Prefab.cid", f"{mesh}.components.json"]),
           f"DK9 {vn}: staged files {files}")
        oc, mc = open(os.path.join(d, f"{vn}.Prefab.cid"), "rb").read(), open(os.path.join(d, f"{mesh}.Prefab.cid"), "rb").read()
        ok(oc == rec["object_prefab_cid"].encode() and mc == rec["mesh_prefab_cid"].encode(),
           f"DK9 {vn}: .cid files = the seeded CIDs, 32 hex, no newline")
        raw = open(os.path.join(d, f"{vn}.Prefab"), "rb").read()
        js = _cp_json(raw.decode("utf-8"))
        ok(not raw.startswith(b"\xef\xbb\xbf") and b"\r\n" not in raw and js["name"] == vn
           and js["m_Meshes"]["$rcontent"][0]["m_Mesh"] == f"$fstrref:CID:{rec['mesh_prefab_cid']}"
           and not D.prefab_text_errors(raw.decode()),
           f"DK9 {vn}: object prefab (no BOM, LF) names the variant mesh CID, validate rules hold")
        cp = _cp_json("{" + open(os.path.join(d, f"{mesh}.components.json"), encoding="utf-8").read().rstrip()
                      .rstrip(",") + "}")["components"]["$rcontent"][0]
        sets = cp["m_ColorVariations"]["$rcontent"]
        binds = [b["m_CanBeModifiedByExternal"] for b in cp["m_ChannelsBinding"]["$rcontent"]]
        rng = [cp["m_VariationRanges"][k] for k in "xyz"] + [cp["m_AlphaRanges"][k] for k in "xyz"]
        col = sets[0]["m_Colors"]["$rcontent"][1]["rgba"][:3] if len(sets) == 1 else None
        ok(len(sets) == 1 and binds == [False, False, False] and rng == [0] * 6 and cp["m_ExternalColorSource"] == 0
           and col is not None and max(abs(a - b) for a, b in zip(col, FD.COLOUR_SETS[u["colour_set"]].rgb)) < 1e-9,
           f"DK9 {vn}: FIXED ColorProperties (1 set {col}, no external channel, ranges 0)")
    # V4 negative: a re-mint with another CID is refused
    alt = os.path.join(tmp, "staging_neg")
    D.stage_variant("SkiDressAkja", "red", staging=alt)
    with open(os.path.join(alt, "SkiDressAkja_Red", "SkiDressAkja_Red.Prefab.cid"), "wb") as fh:
        fh.write(b"0" * 32)
    try:
        D.stage_variant("SkiDressAkja", "red", staging=alt)
        ok(False, "DK9 negative: a staged .cid that differs from the seeded one was overwritten")
    except RuntimeError:
        ok(True, "DK9 negative: a staged .cid that differs from the seeded one raises (never re-mint)")
    # V5 the kit manifests no longer say Parent
    for spec in D.PROPS:
        with open(os.path.join(REPO, "assets", "import", "props", "B", spec["name"], f"{spec['name']}.manifest.json"),
                  encoding="utf-8") as fh:
            txt = fh.read()
        m = json.loads(txt)
        ok("Parent (1)" not in txt and m.get("colour") == D.colour_record(spec)
           and (spec["colour"] != "variants" or "color-set variant" in m["row_spec"]["object_prefab"]),
           f"DK9 {spec['name']} manifest: color / row_spec as the registry writes them, no Parent")
    # V6 the clone: an importer-shaped base -> the variant mesh prefab
    base = synth_base_mesh("SkiDressSkiRackV2B")
    comp = D.variant_components_json(FD.COLOUR_SETS["red"].rgb)
    out = D.clone_mesh_prefab(base, "SkiDressSkiRackV2_Red", comp)
    decl = [int(x) for x in re.findall(r'"\$type": "(\d+)\|', out)]
    keep = [ln for ln in base.split("\n") if "CID:" in ln or re.search(r'"[xyz]": |m_SurfaceArea|Count|m_IsImpostor', ln)]
    ok(not D.prefab_text_errors(out) and decl == list(range(13)) and '"name": "SkiDressSkiRackV2_Red Mesh"' in out
       and all(ln in out for ln in keep) and out.count("CID:") == base.count("CID:"),
       f"DK9 clone: types 0-12 in order, name, every CID / bound / count of the base kept ({len(keep)} lines)")
    for neg, why in ((base.replace('    "m_GeometryAsset"', '    "m_Geometry"'), "no m_GeometryAsset anchor"),
                     (base.replace('"$type": 4,', '"$type": 7,'), "a tail reference to an undeclared type"),
                     (base.replace('"$id": 4,', '"$id": 104,'), "an $id clash with the new block")):
        try:
            D.clone_mesh_prefab(neg, "SkiDressSkiRackV2_Red", comp)
            ok(False, f"DK9 negative: clone with {why} did not raise")
        except ValueError:
            ok(True, f"DK9 negative: clone with {why} raises")
    # V7 surgery on a fake ImportedData (one synthetic base mesh prefab per used base mesh), with a SHORT base
    # folder: Blender's Python has no long-path support and variant paths reach ~90 characters under ImportedData
    base_tmp = tempfile.gettempdir()
    if len(base_tmp) > 60 and os.environ.get("LOCALAPPDATA"):
        base_tmp = os.path.join(os.environ["LOCALAPPDATA"], "Temp")
    fake_root = tempfile.mkdtemp(prefix="dk9_", dir=base_tmp)
    try:
        _surgery_checks(os.path.join(fake_root, "ImportedData"), table)
    finally:
        shutil.rmtree(fake_root, ignore_errors=True)


def _surgery_checks(fake, table):
    for b in sorted({v["base_mesh"] for v in table["variants"]}):
        d = os.path.join(fake, D.EDITOR_SEED, b)
        os.makedirs(d)
        with open(os.path.join(d, f"{b} Mesh_0f.Prefab"), "w", encoding="utf-8",      # short: MAX_PATH under long TEMPs
                  newline="\n") as fh:
            fh.write(synth_base_mesh(b))
    w1 = D.variant_surgery(fake)
    w2 = D.variant_surgery(fake)
    errs = [p for p in w1 if p.endswith(".Prefab") and D.prefab_text_errors(open(p, encoding="utf-8").read())]
    ok(len(w1) == 4 * len(table["variants"]) and not w2 and not errs,
       f"DK9 variant_surgery: {len(w1)} files for {len(table['variants'])} variants, idempotent, validate rules hold")
    p0 = next(p for p in w1 if p.endswith(" Mesh.Prefab"))
    with open(p0, "a", encoding="utf-8") as fh:
        fh.write(" ")
    try:
        D.variant_surgery(fake)
        ok(False, "DK9 negative: surgery overwrote a different existing file")
    except RuntimeError:
        ok(True, "DK9 negative: surgery refuses a different existing file")


def main():
    D.clear_scene()
    tmp = tempfile.mkdtemp(prefix="test_dress_kit_")
    if VARIANTS_ONLY:
        try:
            variants(tmp)
        finally:
            shutil.rmtree(tmp, ignore_errors=True)
        return
    try:
        out = os.path.join(tmp, "B")
        kit = D.export(out, staging=False, subobj=False)
        owner_dir = os.path.join(out, D.OWNER)
        nx, ny, nb = D.read_normal(os.path.join(owner_dir, f"{D.OWNER}_Normal.png"))

        # -- per prop: DK1, DK3, DK4, DK5 on the re-imported FBX ----------------------------
        for spec in D.PROPS:
            name = spec["name"]
            objs, o = load_fbx(os.path.join(out, name, f"{name}.fbx"))
            try:
                me = o.data
                tris = sum(1 for p in me.polygons if len(p.vertices) == 3)
                ok(dk1(tris, spec["cap"]), f"DK1 {name}: {tris} tris <= cap {spec['cap']}")
                ok(not dk1(tris, tris - 1), f"DK1 negative {name}: cap {tris - 1} fails")
                mask = cs2_validate.uv_coverage(me, D.A) > 0
                nd = D.normal_detail(nx, ny, mask)
                ok(nd["hf_rms_deg"] >= D.DK3_MIN_HF_DEG,
                   f"DK3 {name}: fine-detail rms {nd['hf_rms_deg']} deg >= {D.DK3_MIN_HF_DEG} "
                   f"(GardenBench01 {D.BENCH_HF_DEG})")
                raw = raw_co(me)
                ok(dk4(raw, spec["kind"]), f"DK4 {name}: pivot (raw min {np.round(raw.min(0), 3)} cm)")
                ok(not dk4(raw + np.array([0.0, 5.0, 0.0]), spec["kind"]),
                   f"DK4 negative {name}: shifted 5 cm fails")
                bad = dk5(me)
                ok(bad == 0, f"DK5 {name}: {bad} mirrored / zero-area UV faces")
                if name == D.OWNER:
                    lay = me.uv_layers[0]
                    p0 = me.polygons[0]
                    li = list(p0.loop_indices)
                    uvs = [tuple(lay.data[i].uv) for i in li]
                    for i, uv in zip(li, reversed(uvs)):
                        lay.data[i].uv = uv
                    ok(dk5(me) >= 1, "DK5 negative: one reversed face is detected")
                    med = dk6_median(me, D.A)
                    ok(med >= D.DK6_MIN_PPM, f"DK6 owner FBX area-weighted median {med:.1f} px/m >= 250")
                    ok(dk6_median(me, 512) < D.DK6_MIN_PPM,
                       f"DK6 negative: the same UVs at 512^2 give {dk6_median(me, 512):.1f} < 250")
            finally:
                cs2_validate.remove_objects(objs)
        flat = np.zeros_like(nx)
        cov = np.ones(nx.shape, dtype=bool)
        ok(D.normal_detail(flat, flat, cov)["hf_rms_deg"] < D.DK3_MIN_HF_DEG,
           "DK3 negative: a flat normal map gives 0 deg")
        if os.path.isfile(os.path.join(NMSTATS, "nmstats.py")):
            sys.dont_write_bytecode = True          # read-only import from docs/
            sys.path.insert(0, NMSTATS)
            import nmstats
            m = np.zeros(nx.shape, dtype=bool)
            m[100:400, 100:400] = True
            ref = nmstats.normal_stats_xy(nx, ny, m, 255.0)["hf_rms_deg"]
            mine = D.normal_detail(nx, ny, m)["hf_rms_deg"]
            ok(abs(ref - mine) < 0.01, f"DK3 metric = benchmark nmstats ({mine} vs {ref:.2f})")
        else:
            SKIPS.append("nmstats.py not found: metric parity")

        # -- DK2 + negatives -----------------------------------------------------------------
        ok(D.check_sharers(out) == [], f"DK2 one set, every sharer complete: {D.check_sharers(out)}")
        neg = os.path.join(tmp, "neg", "B")
        shutil.copytree(out, neg)
        sp = os.path.join(neg, "SkiDressToolsB", "settings.json")
        with open(sp, encoding="utf-8") as fh:
            st = json.load(fh)
        st["sharedAssets"].pop("SkiDressToolsB_Normal.png")
        with open(sp, "w", encoding="utf-8") as fh:
            json.dump(st, fh, indent=2)
        ok(len(D.check_sharers(neg)) >= 1, "DK2 negative: a missing key is reported")
        D.write_sharer_settings(os.path.join(neg, "SkiDressToolsB"), "SkiDressToolsB")
        os.remove(os.path.join(neg, D.OWNER, f"{D.OWNER}_MaskMap.png"))
        ok(len(D.check_sharers(neg)) >= 10, "DK2 negative: a missing owner map is reported for every sharer")

        # -- DK6 kit + per prop (full-weight faces, from the in-process layout) --------------
        u = kit["bake"]["layout"]["unique_px_per_m"]
        ok(kit["DK6_kit_median_ge_250"] and u["median_w1"] >= D.DK6_MIN_PPM,
           f"DK6 kit: full-weight median {u['median_w1']} px/m >= 250 (all texel-owning faces {u['median']})")
        for name, p in kit["props"].items():
            ok((p["px_per_m"]["median_w1"] or 0) >= D.DK6_MIN_PPM,
               f"DK6 {name}: full-weight median {p['px_per_m']['median_w1']} px/m")

        # -- parity, DK8, validator, sharer resolution ---------------------------------------
        par = kit["parity"]
        ok(all(v["max_dev_m"] <= 1e-6 for v in par["aframe"].values()),
           f"parity A-frame items = ski_rack_frame's: {par['aframe']}")
        ok(par["wall"]["max_dev_m"] <= 1e-5, f"parity wall rack slots = ski_rack's: {par['wall']}")
        ok(kit["library_max_dev_m"] <= 1e-5, f"rigid copies = library items ({kit['library_max_dev_m']:.2e} m)")
        it = kit["gear_item_tris"]
        want = {"ski": 32, "board": 28, "poles": 44}
        ok(all(it[k] == want[D.GEAR[k][0]] for k in it), f"DK8 no bindings: item tris {it}")
        for name, p in kit["props"].items():
            ok(p["validation"]["errors"] == 0,
               f"validator {name}: 0 errors ({p['validation']['error_list'][:2]})")
            if name != D.OWNER:
                st_ = p["stock_validator_on_sharer"]
                ok(set(st_["errors"]) <= set(D.SHARER_STOCK_CODES),
                   f"stock validator on sharer {name}: only {D.SHARER_STOCK_CODES} codes ({st_['errors']})")

        # -- FS12 ------------------------------------------------------------------------
        proof = D.settings_proof()
        ok(proof and all(r.get("identical", True) for r in proof),
           f"FS12 write_settings byte-identical on shipped settings.json: {[r.get('asset') for r in proof]}")

        # -- determinism in two processes ------------------------------------------------
        if QUICK:
            SKIPS.append("two-process determinism (--quick)")
        else:
            d1, d2 = os.path.join(tmp, "p1"), os.path.join(tmp, "p2")
            r1, l1 = run_child(d1)
            r2, l2 = run_child(d2)
            ok(r1 == 0 and r2 == 0, f"child exports exit 0 ({r1}, {r2})" + ("" if r1 == r2 == 0 else l1 + l2))
            if r1 == 0 and r2 == 0:
                files = [os.path.join(D.OWNER, f"{D.OWNER}_{m}.png") for m in D.MAPS]
                files += [os.path.join(s_["name"], "settings.json") for s_ in D.PROPS]
                same = []
                for rel in files:
                    with open(os.path.join(d1, rel), "rb") as f1, open(os.path.join(d2, rel), "rb") as f2:
                        same.append(f1.read() == f2.read())
                ok(all(same), f"two processes: {sum(same)} / {len(files)} PNG + settings files byte-identical")
                sig = [fbx_signature(os.path.join(d, s_["name"], s_["name"] + ".fbx"))
                       for s_ in D.PROPS for d in (d1, d2)]
                ok(all(sig[i] == sig[i + 1] for i in range(0, len(sig), 2)),
                   "two processes: all 11 FBX geometry + UV signatures equal")

        # -- staging templates (the shipped ones, when present) ------------------------------
        if os.path.isdir(D.STAGING):
            for spec in D.PROPS:
                d = os.path.join(D.STAGING, spec["obj"])
                pf = os.path.join(d, f"{spec['obj']}.Prefab")
                if spec["colour"] == "variants":       # kit color variants: the Parent base prefab is retired
                    ok(not os.path.exists(d), f"staging {spec['obj']}: colored base prefab NOT staged (retired)")
                    continue
                if not os.path.isfile(pf):
                    SKIPS.append(f"staging {spec['obj']} absent")
                    continue
                txt = open(pf, encoding="utf-8").read().replace('$fstrref:"', '"$fstrref:')
                try:
                    js = json.loads(txt)
                    ok(js["name"] == spec["obj"] and js["m_Meshes"]["$rlength"] == 1,
                       f"staging {spec['obj']}: object prefab JSON parses")
                except ValueError as e:
                    ok(False, f"staging {spec['obj']}: JSON {e}")
                cid = open(pf + ".cid", "rb").read()
                ok(len(cid) == 32 and all(c in b"0123456789abcdef" for c in cid),
                   f"staging {spec['obj']}: .cid is 32 lowercase hex, no newline")
        else:
            SKIPS.append("no staging folder yet")
        variants(tmp)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        FAILS.append("exception")
    for s in SKIPS:
        print(f"[test_dress_kit] SKIP {s}")
    print(f"[test_dress_kit] {'PASS' if not FAILS else 'FAIL'}: {len(FAILS)} failure(s), "
          f"{len(SKIPS)} skip(s)")
    sys.exit(1 if FAILS else 0)
