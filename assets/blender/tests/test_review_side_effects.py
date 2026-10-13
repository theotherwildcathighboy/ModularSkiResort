"""
Side-effect gate for the live-review packs (ROUND-SPEC R10, pipeline-gap.md section 6).

`--review` must be free: the asset folder a generator writes is byte-identical with and
without it, and cs2_review.write_pack leaves the build session exactly as it found it.

  1. Builds the default chair (--style tr, SkiLiftChair6M: v2 bake, authored LOD2 proxy)
     twice in ONE Blender process through lift_chair.main(): without and with
     `--review <tmp>`, --finish --lods --verify (plate "27" default; validation and the
     manifest run inside the generator). The FBX header clock is frozen (io_scene_fbx
     export_fbx_bin.py:3235 stamps datetime.now() into CreationTimeStamp - the only byte
     that differs between runs), then every file's sha1 must match except the manifest
     (write_manifest stamps written_utc, cs2_validate.py:1087, and records the args, which
     include --review; dq-chair-skeptic D1 / verdict 11): it is compared without those
     two keys. The shipped round-L chair stays reproducible as --style fg6l.
  1b. The same for the top station (lift_terminal.main(), --kind return --finish --lods,
     validation on): its hook passes `_Win` and runs after the manifest and the validator's
     renames (pipeline-gap.md section 6 gate). Every file must match byte for byte except
     the LOD1 / LOD2 main FBXs (p0a-integration O9: not reproducible even within one
     process) and the manifest, compared without written_utc, FBX sha1s and LOD FBX sizes.
  2. A spy around cs2_review.write_pack snapshots the session before / after the call:
     every ID collection's members, the build objects' geometry / UV / material / matrix
     hash, scene settings, selection and active object. All must be unchanged.
  3. The pack: CS2R_SkiLiftChair6M with LOD0 / LOD1 / LOD2 / SUB, the custom props
     (tris = lift_chair.STYLES["tr"] tallies, caps, LOD switch, density, generator, args,
     git sha, letter M), the slot nodes and RELATIVE image paths that resolve to the
     asset folder.
  4. review_live.py smoke run on a temporary COPY of review-2026-10-10.blend: exec only
     defines; ensure_scene / load / lod / tint / night / stand_ins / cams / reload / unload
     all return ok, the reference collections and stand-ins survive, unload leaves nothing.
     P0a review additions: load() re-includes an EXCLUDED Review collection, lod() reports
     a role hidden by an excluded _Live collection as ok False; ensure_scene(lighting=False)
     gives back "no world" and the Standard view transform; the FG8N stand-in is an
     instance of the headless pack cs2_review.write_standin_pack wrote (no live import).
     (Nothing is saved; the real review file is only copied.)
  5. Vanilla-prop stand-ins (DECISIONS 183): tests/test_review_subobjects.py runs in this process before part 4
     (its own file, also runnable alone in ~2 s): a pack whose manifest holds N sub-object rows gets N
     labelled boxes of the right bounds in SUB, every shipped manifest row maps through the generators' own
     box test, review_live.props() swaps SUB <-> SUB_NOPROPS. A failure there fails this test too.

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_review_side_effects.py

Exit 1 on any failure; one PASS line on success.
"""
import datetime
import hashlib
import json
import os
import shutil
import sys
import tempfile
import time
import types

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(HERE)
sys.path.insert(0, LIB)
import cs2_review     # noqa: E402
import lift_chair     # noqa: E402
import lift_terminal  # noqa: E402

CHAIR_STYLE = lift_chair.STYLES["tr"]
CHAIR_NAME = "SkiLiftChair6M"
CHAIR_ARGS = ["--style", "tr", "--name", CHAIR_NAME, "--finish", "--lods", "--verify"]
CHAIR_MANIFEST = f"{CHAIR_NAME}.manifest.json"
STATION_NAME = "SkiLiftTopStation"
STATION_ARGS = ["--kind", "return", "--finish", "--lods"]
STATION_NOBYTES = (f"{STATION_NAME}_LOD1.fbx", f"{STATION_NAME}_LOD2.fbx",
                   f"{STATION_NAME}.manifest.json")
FROZEN_FBX_TIME = datetime.datetime(2026, 1, 1, 0, 0, 0)
REVIEW_BLEND = os.path.join(cs2_review.REPO_ROOT, "assets", "preview", "review",
                            "review-2026-10-10.blend")
REVIEW_LIVE = os.path.join(LIB, "review_live.py")
EXPECTED_PROPS = ("cs2r_pack", "name", "letter", "kind", "generator", "args", "git_sha",
                  "tris_LOD0", "tris_LOD1", "tris_LOD2", "cap_LOD0", "lod_switch_m_LOD1",
                  "lod_switch_m_LOD2", "lod_cull_m", "density_px_per_m_median",
                  "density_px_per_m_min", "texture_px", "bbox_min", "bbox_max",
                  "slot1_default", "slot2_default", "slot3_default", "cs2r_meta_json")
KEPT_COLLECTIONS = ("Review", "Review_Scale", "Kagelok_Ref", "Gondola_Ref")
KEPT_OBJECTS = ("LotGrid8m", "ReviewSun", "ReviewCam", "ReviewTarget", "ScalePerson_body",
                "ScalePerson_head", "ScaleTower8m_mast", "ScaleTower8m_crossarm")

FAILS = []
SPY = []


def check(cond, msg):
    if not cond:
        FAILS.append(msg)
        print(f"[review-test] FAIL: {msg}")
    return cond


# --------------------------------------------------------------------------------------
# Session fingerprints
# --------------------------------------------------------------------------------------

def freeze_fbx_clock():
    import io_scene_fbx.export_fbx_bin as fbxbin

    class _Frozen(datetime.datetime):
        @classmethod
        def now(cls, tz=None):
            return FROZEN_FBX_TIME

    fbxbin.datetime = types.SimpleNamespace(datetime=_Frozen)


def file_hashes(folder):
    out = {}
    for fn in sorted(os.listdir(folder)):
        with open(os.path.join(folder, fn), "rb") as fh:
            out[fn] = hashlib.sha1(fh.read()).hexdigest()
    return out


def object_hash(obj):
    h = hashlib.sha1()
    h.update(f"{obj.name}|{obj.data.name}|{obj.users_collection[:]!r}|".encode())
    h.update(("%r" % [tuple(r) for r in obj.matrix_world]).encode())
    me = obj.data
    for v in me.vertices:
        h.update(("%.6f %.6f %.6f;" % tuple(v.co)).encode())
    for p in me.polygons:
        h.update((",".join(str(i) for i in p.vertices) + f"/{p.material_index};").encode())
    for layer in me.uv_layers:
        h.update(f"[{layer.name}{'*' if layer.active else ''}]".encode())
        for lp in layer.data:
            h.update(("%.6f %.6f;" % tuple(lp.uv)).encode())
    h.update(repr([m.name if m else None for m in me.materials]).encode())
    h.update(repr(sorted(me.keys())).encode())
    h.update(repr(sorted(a.name for a in me.attributes)).encode())
    return h.hexdigest()


def session_state(objs):
    sc = bpy.context.scene
    vl = bpy.context.view_layer
    ids = {attr: sorted(i.name for i in getattr(bpy.data, attr))
           for attr in cs2_review._ID_COLLECTIONS}
    return {
        "ids": ids,
        "objects": {o.name: object_hash(o) for o in objs},
        "scene": (sc.render.engine, sc.camera.name if sc.camera else None,
                  sc.world.name if sc.world else None, sc.frame_current,
                  sc.render.resolution_x, sc.render.resolution_y,
                  sc.view_settings.view_transform, sc.cycles.samples,
                  sc.render.bake.margin, sc.tool_settings.use_uv_select_sync),
        "selected": sorted(o.name for o in vl.objects if o.select_get()),
        "active": vl.objects.active.name if vl.objects.active else None,
        "scene_objects": sorted(o.name for o in sc.objects),
    }


def install_spy():
    orig = cs2_review.write_pack

    def spy(name, asset_dir, lod_objs, submeshes=None, **kw):
        objs = list(lod_objs.values()) + list((submeshes or {}).values())
        before = session_state(objs)
        path = orig(name, asset_dir, lod_objs, submeshes, **kw)
        after = session_state(objs)
        SPY.append({"before": before, "after": after, "path": path, "name": name,
                    "levels": sorted(lod_objs), "subs": sorted(submeshes or {})})
        return path

    cs2_review.write_pack = spy


def run_chair(out, review_dir=None):
    saved = sys.argv
    sys.argv = ["blender", "--", "--out", out] + CHAIR_ARGS + \
        (["--review", review_dir] if review_dir else [])
    try:
        lift_chair.main()
    finally:
        sys.argv = saved
    return file_hashes(os.path.join(out, CHAIR_NAME))


def run_station(out, review_dir=None):
    saved = sys.argv
    sys.argv = ["blender", "--", "--out", out] + STATION_ARGS + \
        (["--review", review_dir] if review_dir else [])
    try:
        lift_terminal.main()
    finally:
        sys.argv = saved
    return file_hashes(os.path.join(out, STATION_NAME))


def chair_manifest_core(folder):
    """The chair manifest minus written_utc and the args (they carry --review)."""
    with open(os.path.join(folder, CHAIR_NAME, CHAIR_MANIFEST), encoding="utf-8") as fh:
        m = json.load(fh)
    m.pop("written_utc", None)
    m.pop("args", None)
    return m


def manifest_core(folder):
    """The station manifest minus what legitimately differs between two builds."""
    with open(os.path.join(folder, STATION_NAME, f"{STATION_NAME}.manifest.json"),
              encoding="utf-8") as fh:
        m = json.load(fh)
    m.pop("written_utc", None)
    for fn, row in m.get("files", {}).items():
        if fn.lower().endswith(".fbx"):
            row.pop("sha1", None)
            if fn in STATION_NOBYTES:
                row.pop("bytes", None)
    return m


def compare_builds(label, plain, reviewed, skip=()):
    print(f"[review-test] {label}: files without --review: {len(plain)}, with: {len(reviewed)}")
    check(sorted(plain) == sorted(reviewed),
          f"{label}: file sets differ: {sorted(plain)} vs {sorted(reviewed)}")
    for fn in sorted(plain):
        if fn in skip:
            print(f"[review-test]   {fn:<38} (not byte-compared)")
            continue
        same = plain[fn] == reviewed.get(fn)
        print(f"[review-test]   {fn:<38} {plain[fn][:12]} {'==' if same else '!='} "
              f"{(reviewed.get(fn) or '-')[:12]}")
        check(same, f"{label}: {fn} differs with --review")
    check(any(f.endswith(".fbx") for f in plain) and any(f.endswith(".png") for f in plain),
          f"{label}: expected FBX + PNG outputs")
    check(not any(f.endswith(".blend") for f in reviewed), f"{label}: pack leaked into the "
                                                          f"asset folder")


# --------------------------------------------------------------------------------------
# Pack inspection
# --------------------------------------------------------------------------------------

def inspect_pack(path, asset_dir):
    coll_name = f"{cs2_review.PACK_PREFIX}{CHAIR_NAME}"
    with bpy.data.libraries.load(path, link=True, relative=False) as (src, dst):
        check(coll_name in src.collections, f"pack holds {list(src.collections)}, not {coll_name}")
        dst.collections = [coll_name]
    pack = dst.collections[0]
    lib = pack.library
    try:
        roles = {(c.get("cs2r_role") or c.name): c for c in pack.children}
        check(sorted(roles) == sorted(cs2_review.ROLES), f"children {sorted(roles)}")
        for role in ("LOD0", "LOD1", "LOD2"):
            objs = list(roles.get(role).objects) if role in roles else []
            check(len(objs) == 1 and objs[0].name == f"CS2R_{CHAIR_NAME}_{role}",
                  f"{role} holds {[o.name for o in objs]}")
        check("SUB" in roles and len(roles["SUB"].objects) == 0, "SUB should be empty for a chair")
        missing = [k for k in EXPECTED_PROPS if k not in pack]
        check(not missing, f"collection props missing: {missing}")
        p = {k: (pack[k].to_list() if hasattr(pack[k], "to_list") else pack[k]) for k in pack.keys()}
        t0, t1, t2 = (p.get(f"tris_LOD{k}") or 0 for k in range(3))
        st = CHAIR_STYLE
        check(t0 == st.lod0_tris, f"tris_LOD0 {t0} != {st.lod0_tris}")
        check(abs(t1 - st.lod1_target) <= 0.25 * st.lod1_target and t1 <= 0.5 * t0,
              f"tris_LOD1 {t1} not within 25 % of {st.lod1_target} and <= 0.5 x LOD0")
        check(t2 == st.lod2_tris, f"tris_LOD2 {t2} != proxy tally {st.lod2_tris}")
        check(p.get("cap_LOD0") == st.lod0_cap, f"cap_LOD0 {p.get('cap_LOD0')}")
        s1, s2 = p.get("lod_switch_m_LOD1", 0), p.get("lod_switch_m_LOD2", 0)
        check(0 < s1 < s2 < p.get("lod_cull_m", 0), f"switch {s1} / {s2} / cull {p.get('lod_cull_m')}")
        check(40.0 < s1 < 60.0, f"LOD1 switch {s1} m outside the skeptic-13 band ~50 m")
        check(0 < p.get("density_px_per_m_min", 0) <= p.get("density_px_per_m_median", 0),
              f"density min {p.get('density_px_per_m_min')} / median {p.get('density_px_per_m_median')}")
        check(p.get("texture_px") == lift_chair.FINISH_ATLAS, f"texture_px {p.get('texture_px')}")
        check(p.get("generator") == "lift_chair.py", f"generator {p.get('generator')}")
        args = json.loads(p.get("args", "{}"))
        check(args.get("finish") is True and args.get("lods") is True, f"args {args}")
        check(p.get("letter") == "M", f"letter {p.get('letter')!r}")
        check(bool(p.get("git_sha")) and p.get("git_sha") != "unknown", f"git_sha {p.get('git_sha')!r}")
        meta = json.loads(p.get("cs2r_meta_json", "{}"))
        check(meta.get("tris", {}).get("LOD0") == CHAIR_STYLE.lod0_tris, "cs2r_meta_json lacks tris")

        mat = next((m for m in bpy.data.materials
                    if m.library == lib and m.name == f"CS2R_{CHAIR_NAME}_PBR"), None)
        check(mat is not None, "pack material CS2R_<name>_PBR not linked")
        if mat is not None:
            nodes = mat.node_tree.nodes
            for slot, rgb in ((1, cs2_review.SLOT_DEFAULTS[1]), (2, cs2_review.SLOT_DEFAULTS[2]),
                              (3, lift_chair.BENCH_PREVIEW_RGB)):
                n = nodes.get(f"Slot{slot}")
                got = tuple(round(c, 4) for c in n.outputs[0].default_value[:3]) if n else None
                check(got == tuple(round(c, 4) for c in rgb), f"Slot{slot} default {got} != {rgb}")
                a = nodes.get(f"Slot{slot}Attr")
                check(a is not None and a.attribute_type == "INSTANCER"
                      and a.attribute_name == cs2_review.SLOT_ATTR.format(slot),
                      f"Slot{slot}Attr instancer node")
        imgs = [i for i in bpy.data.images if i.library == lib]
        check(len(imgs) == 4, f"{len(imgs)} images linked, expected 4 maps")
        for img in imgs:
            check(img.filepath.startswith("//"), f"{img.name} path not relative: {img.filepath}")
            real = os.path.normcase(os.path.abspath(bpy.path.abspath(img.filepath, library=lib)))
            check(os.path.isfile(real) and os.path.dirname(real) == os.path.normcase(asset_dir),
                  f"{img.name} -> {real} is not in the asset folder")
        print(f"[review-test] pack props: tris {p.get('tris_LOD0')}/{p.get('tris_LOD1')}/"
              f"{p.get('tris_LOD2')}, switch {s1}/{s2} m, density median "
              f"{p.get('density_px_per_m_median')} min {p.get('density_px_per_m_min')} "
              f"({p.get('density_min_part')}), letter {p.get('letter')}, git {p.get('git_sha')}")
    finally:
        bpy.data.libraries.remove(lib)


# --------------------------------------------------------------------------------------
# review_live smoke run (on a COPY of the live review file)
# --------------------------------------------------------------------------------------

def live_smoke(work, review_dir):
    if not os.path.isfile(REVIEW_BLEND):
        print(f"[review-test] SKIP live smoke: {REVIEW_BLEND} missing")
        return
    copy = os.path.join(work, "live_copy.blend")
    shutil.copy2(REVIEW_BLEND, copy)
    bpy.ops.wm.open_mainfile(filepath=copy, load_ui=False)
    n_obj, n_coll = len(bpy.data.objects), len(bpy.data.collections)
    ns = {"__name__": "review_live_smoke"}
    with open(REVIEW_LIVE, encoding="utf-8") as fh:
        src = fh.read()
    check(src.isascii(), "review_live.py is not ASCII")
    exec(compile(src, REVIEW_LIVE, "exec"), ns)
    check((len(bpy.data.objects), len(bpy.data.collections)) == (n_obj, n_coll),
          "executing review_live.py changed the scene")
    # separate globals / locals (some exec hosts do this): names must still resolve
    ns2_g, ns2_l = {"__name__": "review_live_smoke2"}, {}
    exec(compile(src, REVIEW_LIVE, "exec"), ns2_g, ns2_l)
    check(ns2_g.get("status") is not None and ns2_l["status"]()["ok"],
          "two-dict exec: functions do not resolve")

    def call(fn, *a, **kw):
        res = ns[fn](*a, **kw)
        check(isinstance(res, dict) and res.get("ok"), f"{fn}{a} -> {res}")
        print(f"[review-test] live {fn}{a}: { {k: res[k] for k in list(res)[:6]} }")
        return res

    sc = bpy.context.scene
    sc.world = None                             # ensure_scene must give "no world" back
    sc.view_settings.view_transform = "Standard"
    call("ensure_scene")
    check(sc.view_settings.view_transform == "AgX" and sc.world is not None,
          f"ensure_scene: AgX + sky ({sc.view_settings.view_transform}, {sc.world})")
    vl = bpy.context.view_layer
    # the copy of the live file already holds Richard's loaded chair (root + library): without this
    # unload the "first" load is a reload of HIS library and 8 checks fail (2026-10-11, one-line fix)
    ns["unload"](CHAIR_NAME)
    ns["_find_layer_coll"](vl.layer_collection, "Review").exclude = True
    r = call("load", CHAIR_NAME, review_dir=review_dir)
    check(r.get("cell") == ns["FIRST_CELL"] and abs(r.get("x", 0) - 60.0) < 1e-6,
          f"first pack not on cell 1 / x 60: {r.get('cell')} / {r.get('x')}")
    check(r.get("visible") == ["LOD0", "SUB"] and "Review" in (r.get("unhidden_layers") or [])
          and not ns["_find_layer_coll"](vl.layer_collection, "Review").exclude,
          f"load re-includes an excluded Review collection ({r.get('unhidden_layers')}, "
          f"visible {r.get('visible')})")
    live_lc = ns["_find_layer_coll"](vl.layer_collection, f"CS2R_{CHAIR_NAME}_Live")
    live_lc.exclude = True
    r = ns["lod"](CHAIR_NAME, 0)
    check(r.get("ok") is False and r.get("hidden_roles") == ["LOD0", "SUB"],
          f"lod() reports roles under an excluded _Live as hidden, ok False ({r})")
    ns["_find_layer_coll"](vl.layer_collection, f"CS2R_{CHAIR_NAME}_Live").exclude = False
    r = call("lod", CHAIR_NAME, "lineup")
    check(r.get("visible") == ["LOD0", "LOD1", "LOD2", "SUB"], f"lineup visible {r.get('visible')}")
    check(r["offsets_m"]["LOD1"] > 10.0, f"lineup offsets {r['offsets_m']}")
    r = call("lod", CHAIR_NAME, 1)
    check(r.get("visible") == ["LOD1", "SUB"], f"lod 1 visible {r.get('visible')}")
    call("tint", CHAIR_NAME, "steel", (0.1, 0.5, 0.9))
    e = bpy.data.objects.get(f"CS2R_{CHAIR_NAME}_LOD0")
    check(e is not None and list(e.get("cs2r_slot1", [])) == [0.1, 0.5, 0.9], "tint prop not set")
    r = call("night", True)
    sun = bpy.data.objects.get("ReviewSun")
    check(r.get("sun_energy") == 0.4 and abs(sun.data.energy - 0.4) < 1e-6, "night sun energy")
    check(e.get("cs2r_win_emit") == 1.0, "night did not set the window toggle")
    call("night", False)
    check(abs(sun.data.energy - 4.0) < 1e-6 and e.get("cs2r_win_emit") == 0.0, "noon restore")
    old = bpy.data.objects.get("ScaleTowerFG8N")
    if old is not None and old.instance_collection is None:
        # the review file carries a pre-pack MESH stand-in (older stand_ins imported it
        # live): it is kept and flagged, never replaced behind Richard's back
        r = call("stand_ins", review_dir=review_dir)
        check(r.get("tower_kind") == "mesh" and any("pre-pack" in n for n in r.get("notes", [])),
              f"a pre-pack mesh stand-in is kept and flagged ({r.get('notes')})")
        me = old.data
        bpy.data.objects.remove(old, do_unlink=True)
        if me.users == 0:
            bpy.data.meshes.remove(me)
    r = call("stand_ins", review_dir=review_dir)
    check(bpy.data.objects.get("ScaleRuler3m") is not None, "ruler missing")
    if os.path.isfile(cs2_review.STANDIN_FBX):
        tw = bpy.data.objects.get("ScaleTowerFG8N")
        inst = list(tw.instance_collection.all_objects) if tw is not None and \
            tw.instance_collection is not None else []
        check(tw is not None and tw.type == "EMPTY" and tw.instance_type == "COLLECTION"
              and r.get("tower_kind") == "pack" and len(inst) == 1
              and inst[0].library is not None
              and 12.5 < inst[0].dimensions.z < 13.5 and 12.5 < (r.get("tower_height_m") or 0)
              < 13.5, f"FG8N stand-in: a linked pack instance 13.01 m tall "
                      f"({inst and tuple(inst[0].dimensions)}, {r.get('tower_height_m')})")
        check(tw is not None and [c.name for c in tw.users_collection] == ["Review_Scale"],
              "FG8N not (only) in Review_Scale")
        check(not [o for o in bpy.data.objects if o.name.startswith("SkiLiftTowerFG8N")
                   and o.library is None], "stand_ins imported geometry into the live scene")
    r = call("cams", CHAIR_NAME)
    views = r.get("cameras", {})
    want = {"three_quarter", "front", "line_mouth", "back", "turntable", "field_30m",
            "field_140m", "field_280m", "field_600m", "field_LOD1_switch", "field_LOD2_switch"}
    check(want <= set(views), f"cams missing {sorted(want - set(views))}")
    tt = bpy.data.objects.get(f"CS2R_{CHAIR_NAME}_turntable")
    fcs = ns["_fcurves"](tt) if tt else []
    kps = [kp for fc in fcs for kp in fc.keyframe_points]
    check(len(kps) == 2 and all(kp.interpolation == "LINEAR" for kp in kps)
          and abs(kps[-1].co.x - kps[0].co.x - 240) < 1e-6, f"turntable keys {[tuple(k.co) for k in kps]}")
    call("reload", CHAIR_NAME)
    r = call("load", CHAIR_NAME, review_dir=review_dir)      # second load = reload
    check(r.get("reloaded") is True, "second load did not reload")
    call("unload", CHAIR_NAME)
    left = [o.name for o in bpy.data.objects if o.name.startswith(f"CS2R_{CHAIR_NAME}")]
    check(not left, f"unload left {left}")
    r = call("ensure_scene", lighting=False)
    check(sc.world is None and sc.view_settings.view_transform == "Standard"
          and "cs2r_prev_world" not in sc and "cs2r_prev_view_transform" not in sc,
          f"ensure_scene(lighting=False) restores no world + Standard ({r.get('restored')})")
    check(not [l for l in bpy.data.libraries if CHAIR_NAME in l.filepath], "library not removed")
    for c in KEPT_COLLECTIONS:
        check(bpy.data.collections.get(c) is not None, f"collection {c} lost")
    for o in KEPT_OBJECTS:
        check(bpy.data.objects.get(o) is not None, f"object {o} lost")


# --------------------------------------------------------------------------------------

def run_subobject_part():
    """Part 5: tests/test_review_subobjects.py in this process (it exits 1 on a failure)."""
    import runpy
    path = os.path.join(HERE, "test_review_subobjects.py")
    try:
        runpy.run_path(path, run_name="__main__")
    except SystemExit as exc:
        check(not exc.code, f"part 5: test_review_subobjects.py failed (exit {exc.code})")


# --------------------------------------------------------------------------------------

def main():
    t0 = time.perf_counter()
    freeze_fbx_clock()
    install_spy()
    work = tempfile.mkdtemp(prefix="cs2_review_test_")
    out_a = os.path.join(work, "a", "chairs", "M")
    out_b = os.path.join(work, "b", "chairs", "M")
    review_dir = os.path.join(work, "review")

    plain = run_chair(out_a)
    reviewed = run_chair(out_b, review_dir)
    compare_builds("chair M", plain, reviewed, skip=(CHAIR_MANIFEST,))
    check(CHAIR_MANIFEST in plain, f"chair: {CHAIR_MANIFEST} not written")
    if CHAIR_MANIFEST in plain and CHAIR_MANIFEST in reviewed:
        ca, cb = chair_manifest_core(out_a), chair_manifest_core(out_b)
        check(ca == cb, "chair manifest differs with --review (minus written_utc, args): "
                        + str(sorted(k for k in set(ca) | set(cb) if ca.get(k) != cb.get(k))))

    st_a = os.path.join(work, "a", "stations")
    st_b = os.path.join(work, "b", "stations")
    st_plain = run_station(st_a)
    st_reviewed = run_station(st_b, review_dir)
    compare_builds("top station", st_plain, st_reviewed, skip=STATION_NOBYTES)
    ma, mb = manifest_core(st_a), manifest_core(st_b)
    check(ma == mb, "top station manifest differs with --review (minus written_utc, FBX "
                    "sha1s, LOD FBX sizes): "
                    + str(sorted(k for k in set(ma) | set(mb) if ma.get(k) != mb.get(k))))

    standin = None
    if os.path.isfile(cs2_review.STANDIN_FBX):
        standin = cs2_review.write_standin_pack(out_dir=review_dir)

    want = [(CHAIR_NAME, [0, 1, 2], os.path.join(out_b, CHAIR_NAME)),
            (STATION_NAME, [0, 1, 2], os.path.join(st_b, STATION_NAME))]
    if standin:
        want.append(("SkiLiftTowerFG8N", [0], None))
    check([s["name"] for s in SPY] == [w[0] for w in want],
          f"write_pack calls {[s['name'] for s in SPY]} (expected {[w[0] for w in want]}: "
          f"the --review runs and the stand-in only)")
    for s, (nm, levels, asset) in zip(SPY, want):
        b, a = s["before"], s["after"]
        check(s["levels"] == levels, f"{nm}: levels passed {s['levels']}")
        for attr in sorted(b["ids"]):
            check(b["ids"][attr] == a["ids"][attr],
                  f"{nm}: bpy.data.{attr} changed: {len(b['ids'][attr])} -> "
                  f"{len(a['ids'][attr])}")
        check(b["objects"] == a["objects"],
              f"{nm}: build objects (geometry/UV/material/matrix) changed")
        for key in ("scene", "selected", "active", "scene_objects"):
            check(b[key] == a[key], f"{nm}: session {key} changed: {b[key]} -> {a[key]}")
        print(f"[review-test] write_pack {nm}: {len(b['ids']['objects'])} objects / "
              f"{len(b['ids']['meshes'])} meshes / {len(b['ids']['materials'])} materials / "
              f"{len(b['ids']['images'])} images before and after, subs {s['subs']}")
        pack = s["path"]
        check(os.path.isfile(pack) and os.path.dirname(pack) == os.path.abspath(review_dir),
              f"{nm}: pack not at {review_dir}: {pack}")
    if SPY and os.path.isfile(SPY[0]["path"]):
        inspect_pack(SPY[0]["path"], os.path.join(out_b, CHAIR_NAME))
    if len(SPY) > 1:
        check(SPY[1]["subs"] == ["Win"], f"station pack subs {SPY[1]['subs']} (expected Win)")

    run_subobject_part()
    live_smoke(work, review_dir)

    dt = time.perf_counter() - t0
    if FAILS:
        print(f"FAIL test_review_side_effects: {len(FAILS)} failures ({dt:.1f} s); work dir {work}")
        sys.exit(1)
    shutil.rmtree(work, ignore_errors=True)
    print(f"PASS test_review_side_effects: chair M tr ({len(plain) - 1} files byte-identical "
          f"+ manifest) and top station "
          f"({len(st_plain) - len(STATION_NOBYTES)} files byte-identical + manifest) unchanged "
          f"by --review, write_pack left the session unchanged, pack + stand-in + review_live "
          f"smoke OK ({dt:.1f} s)")


main()
