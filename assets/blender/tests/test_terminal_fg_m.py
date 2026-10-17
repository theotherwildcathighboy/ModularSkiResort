"""
test_terminal_fg_m.py - generation M of the fixed-grip stations (lift_terminal.py --gen M, the
default): every contract of design/fg-stations-m.md section 5 (as amended by its skeptic) on both
stations, each with the negative the design names, plus a 512 / AO 4 bake smoke through main().

  M1  frozen block: the generator's datums = SkiCatalog.cs + carriers.json + the deployed K
      prefab (gamedata-mirror); the manifest's prefab_contract.frozen = the re-parsed deployed
      prefab, byte for byte (json, sorted keys).                  negative: WheelLocal y 3.50
  M2  rope datum (torus = WheelLocal, r 1.80; rim <= 1.745; chord). negative: torus 0.05 low
  M3  real 6M chair sweep (LOD0 + LOD1, both lane ends + the line-first top ends).
                                    negatives: an L-style beam at the front portal; a stake 0.15 in
  M4  cushion 0.45..0.55 over the carpet / crest; plank >= 0.30.  negatives: crest 1.42, deck 0.30
  M5  hosted ET68O / ET62O seam (shipped FBXs).                    negative: top toe at -4.60
  M6  engine base on the oriented mesh.                            negative: a closed cwfoot
  M7  reachability (ramps <= 20 deg, ladders, crossings, standables).
                                    negatives: main ladder removed; an undeclared 1.2 m box at 2 m
  M8  part clashes (declared kinds only).                          negative: a box in a bay post
  M9  lot.                                                         negative: a part at (-10, -6)
  M10 windows (4 house sides, hut 3 sides + lite, no pane on a wall). negative: no -x pane
  M11 sign / labels / tension scale (in the bake hook; the scale is VECTOR).
                                    negative: the K strip (3 x 5 bitmap) shows < 8 AA levels
  M12 no *binding* part; vanilla rows box-tested in M's own frame.  negative: a ski_binding part
  M13 weathering: edge band 0.06 refused; the bake smoke ships a stepped intensity.
  M18 top station bay: every walk route (crest -> booth door, crest -> ledge corners, the W / back /
      E strips, the front ledge) >= 0.70 m wide at hip height (0.30-1.80 m over the surface); the
      report names the narrowest point and the two parts that pinch it. Positive on the generator's
      top AND on the SHIPPED FBX (an independent BVH route scan below, shares no code with the
      contract). negatives: the first build's layout put back (bay, booth, boards, e-stop, tools,
      propane, stop arm, extinguisher: the 0.39 m door route, 0.17 m W / back strips), each pinching
      item alone (UNLOAD board, e-stop post, tools, booth), and a box in the E strip. (The STAY board
      that also pinched is gone from the top since 2026-10-12: M19.)
  M19 (2026-10-12 lift-board redraw, build/lift-boards-redraw.md) the top carries NO STAY SEATED board
      (post, panel, label, sheet rect) - the DQ top's D11 `forbid`: positives on the generator, the
      tables and the SHIPPED top manifest; negatives: STAY SEATED back on its old post 2.55 m past the
      unload line, on the bay boundary, as a wall board on the booth - each raises M19 naming it.
  Fine-detail step 3c (build/fine-detail-step3c-tram-pali.md; lift_terminal.FINE_DETAIL_M, CLI --no-fine): contracts_m
      runs F1 on every dressing-kit row (base e-stop, 2 extinguishers, tools, wall rack W2; top e-stop, extinguisher,
      tools) and M3 / M7 / M18 count the kit meshes; the M18 negatives and the shipped-FBX scan include them.
  F0  --no-fine = the approved part set; fine: the kit-replaced families gone + the cabinet lid, every other shared
      part vertex-identical (labels, the 1-16 scale, badge, sign, weather mast) but the fine families.
  F1  check_kit_rows_m passes on both stations; negatives: an extinguisher 5 cm off its wall, the base tools on the
      cabinet's -y face (feet off the pad), the rack 0.10 m off the pad edge, e-stops in the swept chair (load line,
      unload lane), the top tools without the tube offset, the booth extinguisher past its corner, a kit CID drift.
  F2  H1: e-stop posts and extinguisher bodies on the removed ones, the wall rack frame vertex for vertex, the moved
      tools declared.  F3  fine geometry: maze / gate / post-board families grow, the finish cabinet 10 + a 12-tri lid.
  F4  the shipped manifests' fine block (4 + 5 / 2 + 3 rows, M's -y frame string, F1 rows = the mod rows).
  M15 budgets / M16 determinism (tests/test_terminal_determinism.py) / M17 the library API the
      detach builder reuses (names, positional signatures, L table values unchanged).

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_terminal_fg_m.py
"""
import contextlib
import inspect
import json
import os
import shutil
import sys
import tempfile
import time

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import cs2_asset  # noqa: E402
import cs2_validate  # noqa: E402
import lift_terminal as LT  # noqa: E402

FAILS = []
N_OK = [0]

# the top station's M_TOP keys of the FIRST generation-M build (before M18): the layout whose bay routes
# measured 0.39 m to the booth door and 0.17 m in the W / back strips (build/fg-stations-m-check.md 5)
M18_FIRST_BUILD = dict(bay=(-1.70, 1.90, -6.00, -3.20), hut=(-0.80, 1.00, -5.45, -3.85),
                       unload=(-0.60, -3.30), board_z0=1.00, estop=(1.00, -3.35),
                       tools=(1.48, -5.25, "-X"), propane=(0.40, 0.45), stoparm=(0.30, -0.15), ext=-1.45)


def check(cond, label, detail=""):
    if cond:
        N_OK[0] += 1
        print(f"  ok   {label} {detail}".rstrip())
    else:
        print(f"  FAIL {label} {detail}".rstrip())
        FAILS.append(label)


def expect(code, fn, label):
    """The negative: fn() must raise an M contract error whose message starts with `code`."""
    try:
        fn()
    except (LT.MContractError, ValueError, RuntimeError) as e:
        msg = str(e)
        check(code in msg,
              f"{label} -> {code}", f"({msg[:140]})")
        return
    check(False, f"{label} -> {code}", "(no error raised)")


@contextlib.contextmanager
def patched(d, **kw):
    saved = {k: d[k] for k in kw}
    d.update(kw)
    try:
        yield
    finally:
        d.update(saved)


def build(kind, mutate=None, finish=True):
    LT.clear_scene()
    parts = LT.build_base_m() if kind == "drive" else LT.build_top_m()
    if mutate:
        parts = mutate(parts)
    t, table, _src = LT.assemble_m(parts, finish, kind)
    co, tris, names, _ = LT.m_arrays(t)
    return t, table, co, tris, names


def drop(prefixes):
    def f(parts):
        keep = []
        for o, m in parts:
            if o.name.startswith(prefixes):
                bpy.data.objects.remove(o, do_unlink=True)
            else:
                keep.append((o, m))
        return keep
    return f


def add_box(name, x0, x1, y0, y1, z0, z1, mat="paint1", open_bottom=False):
    def f(parts):
        return parts + [(LT._mbox(name, x0, x1, y0, y1, z0, z1, open_bottom=open_bottom), mat)]
    return f


def aug(co, tris, names, kind):
    """fine-detail step 3c: the station arrays + the dressing-kit rows' REAL meshes (cs2_kitrows.augment)."""
    import cs2_kitrows as KR
    kit = LT.fd_kit_rows_m(kind)
    return KR.augment(co, tris, names, kit, LT.m_host) if kit else (co, tris, names)


def quick_sweep(co, tris, names, kind):
    spec = LT.M_SPECS[kind]
    rep = LT.chair_sweep_m(co, tris, names, spec, levels=(0,), step=0.20, dang=6.0)
    LT.assert_sweep_m(co, tris, names, spec, rep)


# --------------------------------------------------------------------------------------
# positives: every geometry contract on both stations (full sweep, LOD0 + LOD1)
# --------------------------------------------------------------------------------------

def positives():
    print("[fg-m] 1. contracts on both stations")
    out = {}
    for kind in ("drive", "return"):
        t, table, co, tris, names = build(kind)
        n0 = cs2_asset.tri_count(t)
        try:
            rep = LT.contracts_m(t, table, kind, sweep=True)
            rep["M10"] = LT.windows_m(co, tris, names, kind)
            out[kind] = rep
            check(True, f"{kind}: M1 M2 M3 M4 M5 M7 M8 M9 M10 M12",
                  f"(M3 body->struct {rep['M3']['min'].get('struct')} / dress {rep['M3']['min'].get('dress')} / "
                  f"machine {rep['M3']['min'].get('machine')}; M4 cushion {rep['M4']['cushion_over']}, "
                  f"plank min {rep['M4']['plank_min_sweep']}; M5 {min(v['min_m'] for v in rep['M5'].values())})")
        except LT.MContractError as e:
            check(False, f"{kind}: contracts", f"({e})")
            continue
        check(n0 <= LT.LOD0_CAP_M[kind], f"{kind}: M15 LOD0 {n0} <= {LT.LOD0_CAP_M[kind]} (bevels on)")
        # fine-detail step 3c: F1 ran inside contracts_m on every kit row (M's own frame)
        f1 = rep.get("F1") or {}
        nk = {"drive": 5, "return": 3}[kind]
        check(len(f1.get("rows") or []) == nk and all(r_.get("cloud_gap_m") for r_ in f1["rows"]),
              f"{kind}: F1 {nk} dressing-kit rows (mount, contact, access, real mesh, hosted tower, chair cloud, rows, lot)",
              f"(nearest chair {min(min(r_['cloud_gap_m'].values()) for r_ in f1.get('rows') or [{'cloud_gap_m': {'x': 9}}])} m)")
        check(rep["M3"]["poses"] > 1000, f"{kind}: M3 swept {rep['M3']['poses']} poses (both facings, LOD0 + LOD1)")
        if kind == "return":
            check(len(rep["M3"]["anchors"]) == 4, "return: M3 includes the line-first ends (-9, 7.04) / (-12, 7.04) (X7)")
        r7 = rep["M7"]
        if kind == "drive":
            check(set(r7["ladders"]) == {"main", "roof"} and r7["ramps"]["loadramp"]["slope_deg"] <= 20.0,
                  "drive: M7 main + roof ladders, load ramp <= 20 deg",
                  f"({r7['ramps']['loadramp']['slope_deg']} deg; roof ladder rails over the roof "
                  f"{r7['ladders']['roof']['rails_over_arrival']} m, X8)")
        else:
            j = r7["joins"].get("crest | liftiebay", {})
            check(j.get("ok") and j.get("opening", 0) >= 0.61, "return: M7 crest <-> bay crossing >= 0.61 (X9)",
                  f"({j.get('opening')} m)")
            check(r7["ramps"]["rampup"]["slope_deg"] <= 20.0, "return: M7 up-ramp <= 20 deg",
                  f"({r7['ramps']['rampup']['slope_deg']})")
            r18 = rep["M18"]
            check(r18["min_width"] >= LT.M_ROUTE_MIN and sorted(r18["routes"]) == sorted(
                ["crest>door", "crest>NW", "crest>NE", "W strip", "back strip", "E strip", "front ledge"]),
                "return: M18 every bay route >= 0.70 m wide at hip height",
                f"(min {r18['min_width']} m on '{r18['narrowest_route']}' at {r18['narrowest_at']}, pinched by "
                f"{r18['narrowest_parts']}; crest>door {r18['routes']['crest>door']['width']} m)")
        if kind == "drive":
            check("M18" not in rep, "drive: no M18 (the drive station has no bay)")
    return out


# --------------------------------------------------------------------------------------
# negatives
# --------------------------------------------------------------------------------------

def negatives():
    print("[fg-m] 2. negatives")
    # M1: WheelLocal y 3.50 in the generator
    spec = LT.M_SPECS["drive"]
    LT.M_SPECS["drive"] = LT._dc.replace(spec, wheel=(0.20, 0.0, 3.50))
    try:
        expect("M1", lambda: LT.m1_frozen("drive"), "M1 generator WheelLocal y 3.50")
    finally:
        LT.M_SPECS["drive"] = spec
    # M2: the rope torus 0.05 low (L's 3.50 under a 3.55 datum)
    t, table, co, tris, names = build("return", finish=False)
    co2 = co.copy()
    ids = np.unique(tris[[n == "wheelrope" for n in names]])
    co2[ids, 2] -= 0.05
    expect("M2", lambda: LT.m2_rope(co2, tris, names, LT.M_SPECS["return"]), "M2 torus 0.05 below the datum")
    # M3a: an L-style front beam at x -4.30, z 4.35..4.75 (the chairs hang through it)
    t, table, co, tris, names = build("drive", add_box("beam_lstyle", -4.55, -4.05, -3.70, 3.70, 4.35, 4.75),
                                      finish=False)
    expect("M3", lambda: quick_sweep(co, tris, names, "drive"), "M3 L-style beam at the front portal")
    # M3b: a knoll stake 0.15 m inward
    st = list(LT.M_TOP["stakes"])
    st[2] = (st[2][0], st[2][1] - 0.15)
    with patched(LT.M_TOP, stakes=tuple(st)):
        t, table, co, tris, names = build("return", finish=False)
    expect("M3", lambda: quick_sweep(co, tris, names, "return"), "M3 stake moved 0.15 inward")
    # M4a: crest back at L's 1.42
    with patched(LT.M_TOP, crest_z=1.42):
        t, table, co, tris, names = build("return", finish=False)
    expect("M4", lambda: LT.load_window_m(co, tris, names, LT.M_SPECS["return"], "return"), "M4 crest 1.42")
    # M4b: load deck at the pad (0.30)
    with patched(LT.M_BASE, deck=(-1.00, 1.40, 0.60, 3.00, 0.35)):
        t, table, co, tris, names = build("drive", finish=False)
    expect("M4", lambda: LT.load_window_m(co, tris, names, LT.M_SPECS["drive"], "drive"), "M4 deck at the pad")
    # M5: the up-ramp toe back at L's -4.60 (inside the hosted ET62O pad)
    with patched(LT.M_TOP, toe_x=-4.60):
        t, table, co, tris, names = build("return", finish=False)
    expect("M5", lambda: LT.seam_m(co, tris, names, "return", levels=((0, 0),)), "M5 top toe at -4.60")

    # M6: a closed counterweight footing
    def closed_cwfoot(parts):
        out = []
        for o, m in parts:
            if o.name == "cwfoot":
                bpy.data.objects.remove(o, do_unlink=True)
                o = LT._mbox("cwfoot", 6.40, 9.60, -1.20, 1.20, 0.0, 0.25, open_bottom=False)
            out.append((o, m))
        return out
    t, table, co, tris, names = build("drive", closed_cwfoot, finish=False)
    cs2_asset.orient_for_net(t)
    expect("M6", lambda: LT.engine_ring_m(t, "drive"), "M6 closed cwfoot")
    # M7a: the main ladder removed (the catwalk has no way down)
    t, table, co, tris, names = build("drive", drop(("ladder_main", "lrung_main")), finish=False)
    expect("M7", lambda: LT.reach_m(t, "drive"), "M7 main ladder removed")
    # M7b: an undeclared 1.2 x 1.2 platform at 2 m
    t, table, co, tris, names = build("drive", add_box("platx", 6.50, 7.70, -3.60, -2.40, 1.80, 2.00),
                                      finish=False)
    expect("M7", lambda: LT.reach_m(t, "drive"), "M7 undeclared 1.2 m box at 2 m")
    # M8: a box pushed into the bay railing's corner post
    px, py = LT.M_TOP["bay"][0] + 0.10, LT.M_TOP["bay"][2] + 0.10          # the bay railing's SW corner post
    t, table, co, tris, names = build("return", add_box("boothx", px - 0.02, px + 0.40, py - 0.02, py + 0.52,
                                                        1.00, 2.40), finish=False)
    expect("M8", lambda: LT.clash_m(t), "M8 box into a bay rail post")
    # M9: a part at (-10, -6) (the LV stub)
    t, table, co, tris, names = build("drive", add_box("strayx", -10.2, -9.8, -6.2, -5.8, 0.0, 0.5),
                                      finish=False)
    expect("M9", lambda: LT.lot_m(co, "drive"), "M9 a part at (-10, -6)")
    # M10: the house's -x pane deleted
    t, table, co, tris, names = build("drive", drop(("cabwin_mx", "winframe_mx")), finish=False)
    expect("M10", lambda: LT.windows_m(co, tris, names, "drive"), "M10 no -x house pane")
    # M12: a binding part
    t, table, co, tris, names = build("drive", add_box("ski_binding_0", 0.0, 0.1, -3.9, -3.85, 0.3, 0.35),
                                      finish=False)
    expect("M12", lambda: LT.subobj_check_m(co, tris, names, "drive"), "M12 a ski_binding part")
    # M13: a weathering edge band below the as-built 0.08
    import cs2_liftdress as LD
    expect("edge_band", lambda: LD.station_weather_spec("SkiLiftBaseStation", ground="grade",
                                                       edge_band_m=0.06),
           "M13 edge band 0.06 refused")

    # M18: the first build's bay (0.39 m to the booth door, 0.17 m in the W / back strips) put back,
    # each pinching item alone, the booth at its old y, and a box in the E strip
    def m18_raises(label, mutate=None, **top):
        with patched(LT.M_TOP, **top):
            t, table, co, tris, names = build("return", mutate, finish=False)
            # fine-detail step 3c: the kit rows (e-stop, extinguisher, tools) follow M_TOP and stand on the bay
            aco, atris, anames = aug(co, tris, names, "return")
        expect("M18", lambda: LT.route_width_m(aco, atris, anames, "return"), f"M18 {label}")

    m18_raises("first-build layout put back (bay, booth, boards, e-stop, tools, propane, stop arm, ext)",
               **M18_FIRST_BUILD)
    # (2026-10-12: the STAY board is gone from the top - M19 forbids it; its old pinch negative is m19_forbid's now)
    m18_raises("UNLOAD board back on the bay boundary (-0.60, -3.30)", unload=(-0.60, -3.30), board_z0=1.00)
    m18_raises("e-stop post back at (1.00, -3.35)", estop=(1.00, -3.35))
    m18_raises("scraper + shovel back on the E rail beside the extinguisher", tools=(1.48, -5.25, "-X"),
               ext=-1.45)
    m18_raises("booth back at its first y (the stake <-> booth corner gateway 0.72 m)",
               hut=(-0.80, 1.00, -5.45, -3.85))
    m18_raises("a 0.40 x 0.40 box in the E strip beside the door", add_box("blockx", 1.40, 1.80, -5.00, -4.60,
                                                                           1.00, 2.00))


# --------------------------------------------------------------------------------------
# M18 on the SHIPPED top FBX: an independent route scan (no code shared with
# lift_terminal.route_width_m: its own FBX read, standing-surface rays, hip-band probes on a
# BVH of the band triangles, 3-D nearest distances, a different grid phase, a bottleneck
# Dijkstra). It reads only the nodes and strip boxes the generator wrote into the manifest.
# --------------------------------------------------------------------------------------

def m18_shipped():
    import heapq
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    print("[fg-m] 3a. M18 on the shipped top FBX (independent scan)")
    d = os.path.join(LT.IMPORT_M, "stations", "fixedgrip", "M", "SkiLiftTopStationM")
    mf, fbx = os.path.join(d, "SkiLiftTopStationM.manifest.json"), os.path.join(d, "SkiLiftTopStationM.fbx")
    if not (os.path.isfile(mf) and os.path.isfile(fbx)):
        check(False, "M18 shipped: SkiLiftTopStationM is not under assets/import/stations/fixedgrip/M")
        return
    with open(mf, encoding="utf-8") as fh:
        m18 = json.load(fh)["contracts"].get("M18")
    check(m18 is not None and m18["min_width"] >= 0.70, "M18 shipped manifest carries M18, min width >= 0.70",
          f"({m18 and m18['min_width']})")
    if m18 is None:
        return
    objs = cs2_validate.import_fbx_temp(fbx)
    try:
        o = [x for x in objs if x.type == "MESH"][0]
        me = o.data
        raw = np.array([v.co[:] for v in me.vertices], dtype=np.float64) / 100.0
        tris = []
        for p in me.polygons:
            v = list(p.vertices)
            tris += [(v[0], v[k], v[k + 1]) for k in range(1, len(v) - 1)]
        tris = np.array(tris, dtype=np.int64)
    finally:
        cs2_validate.remove_objects(objs)
    co = np.stack([raw[:, 2], raw[:, 0], raw[:, 1]], axis=1)                  # authoring (x, y, z)
    # fine-detail step 3c: the e-stop, extinguisher and tools are dressing-kit rows (the manifest's mod rows): their
    # REAL kit meshes, placed by the shipped rows (game frame, M's -y convention -> authoring), stand on the bay too
    import cs2_kitrows as KR
    with open(mf, encoding="utf-8") as fh:
        blk = json.load(fh).get("sub_objects") or {}
    kit = [{"mesh": o["mesh"], "position": o["pos"], "rotation": o["rot"], "prefab": o["prefab"]}
           for o in blk.get("objects", []) if o.get("kind") == "mod"]
    for r_ in kit:
        A, kt, _km = KR.host_mesh(r_, lambda G: np.stack([G[:, 2], -G[:, 0], G[:, 1]], axis=1))
        tris = np.concatenate([tris, np.asarray(kt, dtype=np.int64) + len(co)])
        co = np.concatenate([co, A])
    check(len(kit) == 3, "M18 shipped: the 3 kit rows' real meshes added to the scan", f"({[r_['prefab'] for r_ in kit]})")
    zs = m18["rule"]["surface_z"]
    z_lo, z_hi = zs + 0.30, zs + 1.80
    full = BVHTree.FromPolygons(co.tolist(), tris.tolist(), all_triangles=True)
    tz = co[tris][:, :, 2]
    band = (tz.max(axis=1) > z_lo + 1e-6) & (tz.min(axis=1) < z_hi - 1e-6)
    obs = BVHTree.FromPolygons(co.tolist(), tris[band].tolist(), all_triangles=True)
    probes = [z_lo + 0.05 * k for k in range(int(round((z_hi - z_lo) / 0.05)) + 1)]
    nodes = m18["nodes"]
    xs = np.arange(-2.6, 2.9, 0.05) + 0.0231
    ys = np.arange(-7.2, -1.2, 0.05) + 0.0089
    clr = np.full((len(xs), len(ys)), np.nan)
    for i, x in enumerate(xs):
        for j, y in enumerate(ys):
            z, top = z_lo - 1e-3, None
            for _ in range(8):
                h = full.ray_cast(Vector((x, y, z)), Vector((0, 0, -1)), 10.0)
                if h[0] is None:
                    break
                if h[1].z >= 0.5:
                    top = h[0].z
                    break
                z = h[0].z - 1e-4
            if top is None or abs(top - zs) > 0.05:
                continue
            c = 0.80
            for pz in probes:
                f = obs.find_nearest(Vector((x, y, pz)), c)
                if f[0] is not None and f[3] < c:
                    c = f[3]
            clr[i, j] = c
    X, Y = np.meshgrid(xs, ys, indexing="ij")

    def cell(p):
        near = np.where(np.isnan(clr), np.inf, (X - p[0]) ** 2 + (Y - p[1]) ** 2)
        return np.unravel_index(np.argmin(near), clr.shape)

    def widest(src, dst, box):
        ok = ~np.isnan(clr)
        if box is not None:
            ok &= (X >= box[0] - 1e-9) & (X <= box[1] + 1e-9) & (Y >= box[2] - 1e-9) & (Y <= box[3] + 1e-9)
        best = np.full(clr.shape, -1.0)
        best[src] = clr[src]
        pq = [(-clr[src], src)]
        while pq:
            b, (i, j) = heapq.heappop(pq)
            b = -b
            if b < best[i, j] - 1e-12:
                continue
            for di in (-1, 0, 1):
                for dj in (-1, 0, 1):
                    a, c = i + di, j + dj
                    if (di or dj) and 0 <= a < clr.shape[0] and 0 <= c < clr.shape[1] and ok[a, c]:
                        nb = min(b, clr[a, c])
                        if nb > best[a, c] + 1e-12:
                            best[a, c] = nb
                            heapq.heappush(pq, (-nb, (a, c)))
        return float(best[dst])

    got = {}
    for rname, r in m18["routes"].items():
        got[rname] = 2.0 * min(widest(cell(nodes[r["from"]]), cell(nodes[r["to"]]), r["box"]), 0.80)
    low = min(got.values())
    check(low >= 0.70, "M18 shipped FBX: every bay route >= 0.70 m wide (independent scan)",
          "(" + ", ".join(f"{k} {v:.3f}" for k, v in got.items()) + ")")
    worst = max(abs(got[k] - m18["routes"][k]["width"]) for k in got)
    check(worst <= 0.08, "M18 shipped FBX: the independent widths agree with the manifest's within 0.08 m",
          f"(largest difference {worst:.3f} m)")




# --------------------------------------------------------------------------------------
# M11 (the vector scale) on PNGs, M17 API, the bake smoke through main()
# --------------------------------------------------------------------------------------

# --------------------------------------------------------------------------------------
# Fine-detail step 3c (build/fine-detail-step3c-tram-pali.md): F0 the switch, F1 the kit rows (+ negatives),
# F2 H1 placement, F3 fine geometry, F4 the shipped fine block
# --------------------------------------------------------------------------------------
FD_GONE_M = {"drive": ("estop_ld_", "ext_house", "ext_hut", "tool_cab_", "skirack_b_", "ski_b_"),
             "return": ("estop_ul_", "ext_booth", "tool_bay_")}
FD_ROWS_M = {"drive": (4, 5), "return": (2, 3)}
FINE_FAMS = ("maze_post_", "maze_rope_", "gate_", "board_tipsup_post", "board_slow_post", "board_unload_post",
             "cabinet",
             # nitpick A (2026-10-13): WAIT HERE on its own post (the base's corral is gone): the fine 12-seg post
             "board_waithere_post")
H1_TOL = 0.001


def build_mode(kind, fine, finish=False):
    saved = LT.FINE_DETAIL_M
    LT.FINE_DETAIL_M = fine
    try:
        return build(kind, finish=finish)
    finally:
        LT.FINE_DETAIL_M = saved


def _by_part(co, tris, names):
    out = {}
    for tr, n in zip(tris, names):
        out.setdefault(n, []).append(co[tr])
    return out


def _vset(T):
    X = np.unique(np.round(np.array(T).reshape(-1, 3), 5), axis=0)
    return X[np.lexsort(X.T[::-1])]


def f0_f3():
    """F0: --no-fine = the approved part set; fine: the kit-replaced families gone, + the cabinet lid, every other
    shared part vertex-identical except the fine-geometry families. F3: the fine families' tris per part."""
    print("[fg-m] 5. F0 / F3 the switch and the fine geometry")
    for kind in ("drive", "return"):
        t0, tb0, co0, tr0, n0 = build_mode(kind, False)
        a = _by_part(co0, tr0, n0)
        t1, tb1, co1, tr1, n1 = build_mode(kind, True)
        b = _by_part(co1, tr1, n1)
        gone = sorted(set(a) - set(b))
        new = sorted(set(b) - set(a))
        want_gone = sorted(n for n in a if n.startswith(FD_GONE_M[kind]))
        check(gone == want_gone and new == (["cabinet_lid"] if kind == "drive" else []),
              f"{kind}: F0 the kit-replaced parts gone ({len(gone)}: {sorted({g.split('_')[0] for g in gone})}), new {new}")
        shared = [n for n in sorted(set(a) & set(b)) if not n.startswith(FINE_FAMS)]
        diff = [n for n in shared if _vset(a[n]).shape != _vset(b[n]).shape or
                not np.allclose(_vset(a[n]), _vset(b[n]), atol=1e-5)]
        lab = [n for n in shared if n.startswith(("label_", "scale_", "badge", "signband", "signboard", "wx", "guy_"))]
        check(not diff, f"{kind}: F0 {len(shared)} shared parts vertex-identical fine vs --no-fine ({len(lab)} labels / "
                        f"scale / badge / sign / weather-mast parts among them: FS5 / FS1)", f"(changed {diff[:6]})")
        fam = {}
        for n in sorted(set(a) & set(b)):
            if n.startswith(FINE_FAMS):
                key = next(f for f in FINE_FAMS if n.startswith(f))
                fam.setdefault(key, [0, 0, 0])
                fam[key][0] += 1
                fam[key][1] += len(a[n])
                fam[key][2] += len(b[n])
        grew = all(v[2] > v[1] for k_, v in fam.items() if k_ != "cabinet")
        check(grew and fam, f"{kind}: F3 fine geometry (parts, tris legacy -> fine) {fam}")
    # the cabinet on the finish build: no bevel strips + a lid lip (the detach cabinets' treatment)
    _t, _tb, co_l, tr_l, n_l = build_mode("drive", False, finish=True)
    _t, _tb, co_f, tr_f, n_f = build_mode("drive", True, finish=True)
    cl = sum(1 for n in n_l if n == "cabinet")
    cf, lid = sum(1 for n in n_f if n == "cabinet"), sum(1 for n in n_f if n == "cabinet_lid")
    check(cf == 10 and lid == 12 and cl > 12, "drive: F3 finish cabinet = 10 tris (no hidden top, no bevel strips) + "
                                              "a 12-tri lid lip", f"(legacy {cl} tris with the finish bevel)")


def _patched_specs_m(patch):
    saved = LT.fd_kit_specs_m
    LT.fd_kit_specs_m = lambda kind, _s=saved: patch(kind, [dict(x) for x in _s(kind)])
    return saved


def _edit_m(prop, **kw):
    def patch(kind, specs):
        out = []
        for s in specs:
            if s["prop"] == prop or (prop == "rack" and s["prop"].startswith("SkiDressSkiRack")):
                s = dict(s)
                for k_, v in kw.items():
                    if k_ == "dpos":
                        s["pos"] = tuple(p_ + d_ for p_, d_ in zip(s["pos"], v))
                    elif v is None:
                        s.pop(k_, None)
                    else:
                        s[k_] = v
            out.append(s)
        return out
    return patch


def f1_m():
    """F1 on both stations (finish geometry, with the M3 chair cloud) + negatives, each must raise F1."""
    import dataclasses
    import fine_detail as FD
    print("[fg-m] 6. F1 kit rows + negatives")
    cases = {
        "drive": (
            (_edit_m("SkiDressExtinguisher", dpos=(0.0, 0.05, 0.0)), "the house extinguisher 5 cm off its wall"),
            (lambda kind, specs: [dict(s, pos=(-1.95, -3.50, 0.30), facing="-Y") if s["prop"] == "SkiDressTools" else s
                                  for s in specs], "the tools on the cabinet's -y face (the foot line leaves the pad)"),
            (_edit_m("rack", dpos=(0.0, -0.10, 0.0)), "the wall rack 0.10 m toward -y (its feet over the pad edge)"),
            (_edit_m("SkiDressEStop", pos=(0.20, 1.80, 0.83)), "an e-stop on the carpet at the load line (the swept chair)"),
        ),
        "return": (
            (_edit_m("SkiDressTools", tube=None), "the top tools on the rail face without the tube offset"),
            (_edit_m("SkiDressEStop", pos=(0.0, -2.20, 1.00)), "the top e-stop on the crest in the unload lane"),
            (_edit_m("SkiDressExtinguisher", dpos=(0.0, 0.25, 0.0)), "the booth extinguisher slid past the booth's +y corner"),
        ),
    }
    for kind in ("drive", "return"):
        t, table, co, tris, names = build_mode(kind, True, finish=True)
        rep = LT.chair_sweep_m(*aug(co, tris, names, kind), LT.M_SPECS[kind], collect=True)
        cloud = rep.pop("_cloud")
        try:
            msg, nums = LT.check_kit_rows_m(co, tris, names, table, kind, cloud=cloud)
            check(len(nums["rows"]) == FD_ROWS_M[kind][1], f"{kind}: F1 passes on {len(nums['rows'])} kit rows",
                  f"({msg['F1'][:90]}...)")
        except LT.MContractError as e:
            check(False, f"{kind}: F1 raised on the shipping layout", f"({e})")
        for patch, why in cases[kind]:
            saved = _patched_specs_m(patch)
            try:
                LT.check_kit_rows_m(co, tris, names, table, kind, cloud=cloud)
                check(False, f"{kind}: F1 negative: {why}", "(no error raised)")
            except LT.MContractError as e:
                check(str(e).startswith("F1"), f"{kind}: F1 negative: {why}", f"({str(e)[:150]})")
            finally:
                LT.fd_kit_specs_m = saved
        if kind == "return":
            k = FD.KIT["SkiDressEStop"]
            FD.KIT["SkiDressEStop"] = dataclasses.replace(k, cid="0" * 32)
            try:
                LT.check_kit_rows_m(co, tris, names, table, kind, cloud=cloud)
                check(False, "return: F1 negative: a kit CID drift", "(no error raised)")
            except (LT.MContractError, ValueError) as e:
                check("F1" in str(e) or "CID" in str(e), "return: F1 negative: a kit CID drift", f"({str(e)[:110]})")
            finally:
                FD.KIT["SkiDressEStop"] = k


def f2_m():
    """F2 (H1): every kit row (REAL kit mesh, placed by its game-frame row, mapped back to authoring) against the
    parts it replaces on a --no-fine build."""
    import cs2_kitrows as KR
    print("[fg-m] 7. F2 H1 placement against the removed parts")
    out = {}
    for kind in ("drive", "return"):
        _t, _tb, co, tris, names = build_mode(kind, False)
        parts = _by_part(co, tris, names)
        saved = LT.FINE_DETAIL_M
        LT.FINE_DETAIL_M = True
        try:
            specs, rows = LT.fd_kit_specs_m(kind), LT.fd_kit_rows_m(kind)
        finally:
            LT.FINE_DETAIL_M = saved
        for s, r in zip(specs, rows):
            A = KR.host_mesh(r, LT.m_host)[0]
            nx, ny = LT._M_NORMAL[s["facing"]]
            nv = np.array([nx, ny, 0.0])
            old = np.concatenate([np.array(v).reshape(-1, 3) for n, v in parts.items() if n.startswith(s["removed"])])
            tag = f"{kind} {s['prop']}"
            if s["prop"] == "SkiDressEStop":
                post = np.array(parts[s["removed"][0] + "post"]).reshape(-1, 3)
                ring = A[np.abs(A[:, 2] - (A[:, 2].min() + 0.01)) < 1e-3]
                da = float(np.linalg.norm(ring[:, :2].mean(axis=0) - post[:, :2].mean(axis=0)))
                df = float(A[:, 2].min() - post[:, 2].min())
                out[tag] = {"axis_dev_mm": round(da * 1000, 3), "foot_dz_m": round(df, 4)}
                check(da <= H1_TOL and abs(df) <= H1_TOL, f"{tag}: F2 the kit post axis = the removed post's",
                      f"({da * 1000:.3f} mm, foot {df:+.4f} m)")
            elif s["prop"] == "SkiDressExtinguisher":
                body = np.array(parts[s["removed"][0]]).reshape(-1, 3)
                ob = body[np.abs(body[:, 2] - body[:, 2].min()) < 1e-4]
                kb = A[np.abs(A[:, 2] - A[:, 2].min()) < 1e-4]
                da = float(np.linalg.norm(kb[:, :2].mean(axis=0) - ob[:, :2].mean(axis=0)))
                df = float(A[:, 2].min() - body[:, 2].min())
                out[tag + " " + s["removed"][0]] = {"axis_dev_mm": round(da * 1000, 3), "bottom_dz_m": round(df, 4)}
                check(da <= H1_TOL and abs(df) <= H1_TOL, f"{tag} {s['removed'][0]}: F2 the kit body axis = the removed "
                                                          f"body's", f"({da * 1000:.3f} mm, bottom {df:+.4f} m)")
            elif s["prop"] == "SkiDressTools":
                of = old[old[:, 2] <= old[:, 2].min() + 0.002]
                kf = A[A[:, 2] <= A[:, 2].min() + 0.002]
                mv = kf.mean(axis=0) - of.mean(axis=0)
                out[tag] = {"foot_line_move_m": [round(float(v), 4) for v in mv[:2]], "out_from_wall_m":
                            round(float(mv @ nv), 4)}
                mv2 = np.array([mv[0], mv[1], 0.0])
                lat = float(np.linalg.norm(mv2 - nv * float(mv2 @ nv)))
                out[tag]["lateral_m"] = round(lat, 4)
                moved = "MOVED" in s["was"]
                ok_t = moved or (lat < 1e-3 and (s.get("tube") or s.get("ledge")
                                                 or abs(float(mv @ nv) - (0.3925 - 0.32)) < 1e-3))
                check(ok_t, f"{tag}: F2 the kit tools " + ("MOVED (declared)" if moved else
                                                           "= the builder pair's centre, moved out from the wall only"),
                      f"(foot line moved {out[tag]['foot_line_move_m']} m, {float(mv @ nv):+.4f} out, lateral {lat:.4f}; "
                      f"{s['was'][:80]})")
            else:                                   # the wall rack: frame vertex for vertex after the foot drop
                frame = np.unique(np.round(np.concatenate([np.array(v).reshape(-1, 3) for n, v in parts.items()
                                                           if n.startswith("skirack_b_")]), 6), axis=0)
                dz = float(A[:, 2].min() - frame[:, 2].min())
                dev = max(float(np.min(np.linalg.norm(A - (v + np.array([0.0, 0.0, dz])), axis=1))) for v in frame)
                out[tag] = {"frame_dev_mm": round(dev * 1000, 3), "foot_dz_m": round(dz, 4)}
                check(dev <= H1_TOL, f"{tag}: F2 the kit wall rack's frame = the removed frame vertex for vertex",
                      f"(max {dev * 1000:.3f} mm after a foot drop of {dz:+.4f} m; {len(frame)} frame vertices)")
    print("[fg-m] F2 H1 numbers: " + json.dumps(out))


def f4_m():
    """F4: the shipped manifests' fine block (rows by kind, M's frame string, F1 rows = the block's mod rows)."""
    print("[fg-m] 8. F4 the shipped fine block")
    for kind, name in (("drive", "SkiLiftBaseStationM"), ("return", "SkiLiftTopStationM")):
        with open(os.path.join(LT.IMPORT_M, "stations", "fixedgrip", "M", name, f"{name}.manifest.json"),
                  encoding="utf-8") as fh:
            m = json.load(fh)
        fd = m.get("fine_detail") or {}
        blk = m.get("sub_objects") or {}
        mods = [o for o in blk.get("objects", []) if o.get("kind") == "mod"]
        nv, nk = FD_ROWS_M[kind]
        kr = fd.get("kit_rows") or []
        # T14 x-sign N (2026-10-16), DECISIONS 245 / T53: the shipped M manifests still carry the pre-T14 frame string
        # ("probe X2 decides") until the next export regenerates them: only that conjunct is SKIPPED (loud); once the
        # export lands the string must equal LT.M_X_CONVENTION again, and any other text still fails.
        xc = blk.get("x_convention")
        xc_stale = xc != LT.M_X_CONVENTION and "probe X2 decides" in (xc or "")
        if xc_stale:
            print(f"SKIP {kind}: F4 shipped x_convention is the pre-T14 string (stale until the next export, DECISIONS 245 / T53)")
        ok = (fd.get("rows") == {"vanilla": nv, "mod": nk} == blk.get("kinds") and len(kr) == nk
              and np.allclose([r_["row_pos"] for r_ in kr], [o["pos"] for o in mods], atol=1e-4)
              and (xc == LT.M_X_CONVENTION or xc_stale) and "F1" in (m.get("contracts") or {}))
        check(ok, f"{kind}: F4 shipped fine block {fd.get('rows')}, {len(kr)} F1 rows = the mod rows, M's frame string",
              f"({blk.get('x_convention', '')[:50]}...)")


def m19_forbid():
    """M19 (2026-10-12 lift-board redraw, build/lift-boards-redraw.md): the top carries NO STAY SEATED board
    (post, panel, label, atlas rect) - the DQ top's D11 `forbid` semantics: positives on the generator, its tables
    and the SHIPPED top; negatives put STAY SEATED back in three mounts (each must raise M19 and name it)."""
    import cs2_liftdress as LD
    print("[fg-m] 9. M19 no STAY SEATED on the top (the DQ top's D11 forbid)")
    for kind in ("drive", "return"):
        _t, _tb, co, tris, names = build(kind, finish=False)
        stay = sorted(n for n in set(names) if n.startswith(("board_stay", "label_stay")))
        rep = LT.board_forbid_m(names, kind)
        want = [{"item": "stay", "words": "STAY SEATED"}] if kind == "return" else []
        check(not stay and rep["forbidden"] == want, f"{kind}: M19 no board_stay* / label_stay* part, forbidden {want}",
              f"({stay})")
    check("label_stay" not in LT.M_LABELS["return"] and "label_stay" not in LT.label_sheet_m("return")
          and len(LT.label_sheet_m("return")) == 3 and "stay" not in LT.M_TOP,
          "return: M19 tables - no label_stay, 3 sheet rects (unload / ops / slow), no M_TOP stay key",
          f"({sorted(LT.label_sheet_m('return'))})")

    def put_back(c, facing, mount, **kw):
        def f(parts):
            return parts + LD.board("stay", c, facing, 0.70, 0.55, mount=mount, **kw)
        return f
    hx0, hx1, hy0, hy1 = LT.M_TOP["hut"]
    for label, mut in (("STAY SEATED back on its old post at (2.55, -3.50) - 2.55 m PAST the unload line",
                        put_back((2.55, -3.50, 2.30), "-X", "post", post_z0=0.0)),
                       ("STAY SEATED on the bay boundary (1.45, -3.35) before the line",
                        put_back((1.45, -3.35, 2.30), "-X", "post", post_z0=1.00)),
                       ("STAY SEATED as a wall board on the booth (any mount)",
                        put_back(((hx0 + hx1) / 2.0, hy1 + 0.023, 2.60), "+Y", "wall"))):
        _t, _tb, co, tris, names = build("return", mut, finish=False)
        try:
            LT.board_forbid_m(names, "return")
            check(False, f"return: M19 negative: {label}", "(no error raised)")
        except LT.MContractError as e:
            check(str(e).startswith("M19") and "STAY SEATED" in str(e), f"return: M19 negative: {label}",
                  f"({str(e)[:120]})")
    # the shipped top: M19 recorded, no label_stay in its label block
    name = "SkiLiftTopStationM"
    mf = os.path.join(LT.IMPORT_M, "stations", "fixedgrip", "M", name, f"{name}.manifest.json")
    with open(mf, encoding="utf-8") as fh:
        m = json.load(fh)
    c19 = (m.get("contracts") or {}).get("M19") or {}
    check(c19.get("ok") and c19.get("forbidden") == [{"item": "stay", "words": "STAY SEATED"}]
          and sorted(m.get("labels") or {}) == ["label_ops", "label_slow", "label_unload"],
          "return: M19 on the SHIPPED top (contract recorded, labels unload / ops / slow only)",
          f"({c19}, {sorted(m.get('labels') or {})})")



# --------------------------------------------------------------------------------------
# Nitpick A (2026-10-13; Richard's twenty-fifth look S-1 / S-2 / S-3; build/nitpick-a-stations.md)
# --------------------------------------------------------------------------------------
LADDER_CLEAR = 0.30     # S-3: no dressing part within this (plan) of the base's main ladder rails / rungs


def _ladder_clear_m(co, tris, names):
    """Plan distance from every dressing part (maze_ / gate_ / board_ / label_) to the main ladder's rails + rungs."""
    lad = [i for i, n in enumerate(names) if n.startswith(("ladder_main", "lrung_main"))]
    L = co[np.unique(tris[lad].ravel())][:, :2]
    best = (np.inf, None)
    for pref in ("maze_", "gate_", "board_", "label_"):
        idx = [i for i, n in enumerate(names) if n.startswith(pref)]
        if not idx:
            continue
        for i in idx:
            V = co[tris[i]][:, :2]
            d = float(np.min(np.linalg.norm(V[:, None, :] - L[None, :, :], axis=2)))
            if d < best[0]:
                best = (d, names[i])
    return best


def nitpick_a_m():
    """M20 the layout round the up lane (both stations) + negatives (UP_LANE_Y flipped); M21 the base's hold-down
    trains = the towers' Yan rocker tree, rope tangent, in front of the portal beam + negatives (the train 2 cm off the
    rope, pushed into the portal beam), the top reports no rope-guide group; S-3 the base has no corral (no maze_ /
    gate_ part in either FINE_DETAIL_M mode), WAIT HERE on its own post on the pad, every dressing part >= 0.30 m (plan)
    from the main ladder - the corral put back (M_BASE_CORRAL) fails that clearance; the shipped manifests record
    M20 / M21 and the base's sheave block."""
    print("[fg-m] 10. nitpick A: M20 orientation, M21 the towers' sheave trains, S-3 no corral")
    for kind in ("drive", "return"):
        _t, _tb, co, tris, names = build(kind, finish=False)
        rep = LT.orientation_m(co, tris, names, kind)
        check(rep["up_lane_y_authoring"] == LT.UP_LANE_Y[kind], f"{kind}: M20 {rep['measured_centre_y']}")
        with patched(LT.UP_LANE_Y, **{kind: -LT.UP_LANE_Y[kind]}):
            expect("M20", lambda co=co, tris=tris, names=names, kind=kind: LT.orientation_m(co, tris, names, kind),
                   f"{kind}: M20 the up lane flipped (the station laid out for the other spin)")
        rep = LT.sheave_trains_m(co, tris, names, kind)
        if kind == "drive":
            check(rep["rims"] == 2 * LT.M_HOLD["n"] and rep["tangent_err_m"] <= 0.002,
                  f"drive: M21 {rep['rims']} rims, rope tangent err {rep['tangent_err_m']}, {rep['tris_lod0']} tris")
        else:
            check("note" in rep, f"return: M21 {rep.get('note', '')[:80]}")
    saved = LT.hold_train_layout_m
    LT.hold_train_layout_m = lambda spec, _s=saved: dict(_s(spec), axle_z=_s(spec)["axle_z"] + 0.02)
    try:
        _t, _tb, co, tris, names = build("drive", finish=False)
    finally:
        LT.hold_train_layout_m = saved
    expect("M21", lambda: LT.sheave_trains_m(co, tris, names, "drive"),
           "drive: M21 the hold-down train 2 cm off the rope")
    with patched(LT.M_HOLD, xc=-3.60):
        _t, _tb, co, tris, names = build("drive", finish=False)
    expect("M21", lambda: LT.sheave_trains_m(co, tris, names, "drive"),
           "drive: M21 the hold-down train pushed 1 m toward the wheel (into the portal beam)")
    # S-3: no corral, WAIT HERE on its post, the main ladder clear
    for fine in (True, False):
        _t, _tb, co, tris, names = build_mode("drive", fine)
        n_maze = sum(1 for n in set(names) if n.startswith(("maze_", "gate_")))
        d, who = _ladder_clear_m(co, tris, names)
        post = [i for i, n in enumerate(names) if n.startswith("board_waithere_post")]
        pz = float(co[np.unique(tris[post].ravel())][:, 2].min()) if post else None
        check(n_maze == 0 and post and abs(pz - LT.M_BASE["pad_h"]) < 1e-3 and d >= LADDER_CLEAR,
              f"drive ({'fine' if fine else '--no-fine'}): S-3 no maze / gate part, WAIT HERE on its own post (foot z "
              f"{pz}), dressing >= {LADDER_CLEAR} m from the main ladder (nearest {d:.3f} m, {who})")
    # the corral's maze sits where it stood before T17 (x 3.2-5.2 behind the old ramp), beside the pre-T17 main ladder
    # (x 5.48); T17 (M_BASE_LOADROOM) moved the ladder to 8.28, so the negative runs on the pre-T17 rear end
    saved, saved_lr = LT.M_BASE_CORRAL, LT.M_BASE_LOADROOM
    LT.M_BASE_CORRAL, LT.M_BASE_LOADROOM = True, False
    try:
        _t, _tb, co, tris, names = build("drive", finish=False)
    finally:
        LT.M_BASE_CORRAL, LT.M_BASE_LOADROOM = saved, saved_lr
    d, who = _ladder_clear_m(co, tris, names)
    check(d < LADDER_CLEAR, f"drive: S-3 negative - the corral put back stands {d:.3f} m from the main ladder ({who})")
    # T17 (M_BASE_LOADROOM, build/T17-fg-loadroom.md): the loading bay between the front and rear leg faces, measured on
    # the built legs, is >= 1.5 m longer than the pre-T17 6.60 m; off = the pre-T17 bay
    for lr, want in ((True, lambda b: b >= 6.60 + 1.50 - 1e-6), (False, lambda b: abs(b - 6.60) < 1e-3)):
        saved_lr = LT.M_BASE_LOADROOM
        LT.M_BASE_LOADROOM = lr
        try:
            _t, _tb, co, tris, names = build("drive", finish=False)
        finally:
            LT.M_BASE_LOADROOM = saved_lr
        xf = co[np.unique(tris[[n == "leg_f_p" for n in names]].ravel())][:, 0].max()
        xb = co[np.unique(tris[[n == "leg_b_p" for n in names]].ravel())][:, 0].min()
        check(want(float(xb - xf)), f"drive: T17 loading bay between the leg faces {xb - xf:.2f} m (M_BASE_LOADROOM {lr})")
    for kind, name in (("drive", "SkiLiftBaseStationM"), ("return", "SkiLiftTopStationM")):
        with open(os.path.join(LT.IMPORT_M, "stations", "fixedgrip", "M", name, f"{name}.manifest.json"),
                  encoding="utf-8") as fh:
            m = json.load(fh)
        c = m.get("contracts") or {}
        ok = "M20" in c and "M21" in c and (m.get("orientation") or {}).get("up_lane_y_authoring") == LT.UP_LANE_Y[kind]
        if kind == "drive":
            ok = ok and (m.get("sheave_trains") or {}).get("per_lane") == LT.M_HOLD["n"]
        check(ok, f"{kind}: shipped manifest records M20 / M21 (orientation, sheave_trains)")

def m17_api():
    print("[fg-m] 3. M17 library API (the detach builder's reuse)")
    for fn, need in ((LT.clear_scene, []), (LT.apply_finish_bevels, ["parts"]),
                     (LT.split_windows, ["t", "name", "table"]), (LT.make_post_compose, ["obj", "reserved"]),
                     (LT.check_engine_base, ["t", "kind"])):
        sig = inspect.signature(fn)
        req = [p.name for p in sig.parameters.values() if p.default is inspect._empty]
        check(req == need, f"M17 {fn.__name__}{tuple(need)} positional signature unchanged", f"({sig})")
    check(LT.BEVEL_PREFIXES == ("pad", "beam_", "saddle_", "endmember_", "carr_", "machfloor", "machinebox",
                                "boxroof", "cabfloor", "cabin", "cabroof", "hutbase", "hut", "hutroof",
                                "cabinet", "bankmount_", "plinth", "crest", "rampup", "rampdown")
          and LT.BEVEL_EXCLUDE == ("hutwin", "cabwin"), "M17 BEVEL_PREFIXES / BEVEL_EXCLUDE defaults = L")
    check(LT.WIN_CELLS == {"hutwin": (2, 1), "cabwin": (3, 2)}, "M17 WIN_CELLS default = L")
    check(LT.ENGINE_BASE_PARTS == {"drive": ("pad", "hutbase"), "return": ("rampup", "crest", "rampdown",
                                                                             "liftiebay")},
          "M17 ENGINE_BASE_PARTS default = L")
    check(sorted(LT.detail_specs()) == sorted(["machinebox", "cabin", "cabinet", "cabmull_", "hut", "hutbase",
                                               "hutroof", "leg_", "beam_", "rail_", "rail_catwalk", "rail_knoll",
                                               "mast", "mastcollar", "divrope", "wheelrope", "tcable_",
                                               "grate_catwalk", "grate_catwalk_lip", "door_hut_leaf"]),
          "M17 detail_specs() default = L")
    saved = sys.argv
    sys.argv = ["blender", "--", "--out", "x", "--kind", "return", "--finish", "--lods"]
    try:
        a = LT.parse_args()
    finally:
        sys.argv = saved
    check(a.gen == "M" and a.kind == "return" and a.finish and a.lods,
          "M17 main() CLI: test_review_side_effects 1b's args parse, generation M by default")


def bake_smoke():
    print("[fg-m] 4. 512 / AO 4 bake smoke through main() (both stations)")
    tmp = tempfile.mkdtemp(prefix="fgm_smoke_")
    try:
        for kind in ("drive", "return"):
            name = "SkiLiftBaseStationM" if kind == "drive" else "SkiLiftTopStationM"
            saved = sys.argv
            sys.argv = ["blender", "--", "--out", tmp, "--kind", kind, "--name", name, "--finish", "--lods",
                        "--atlas", "512", "--ao-samples", "4", "--skip-sweep"]
            try:
                LT.main()
            finally:
                sys.argv = saved
            d = os.path.join(tmp, name)
            files = sorted(os.listdir(d))
            want = [f"{name}{s}.fbx" for s in ("", "_LOD1", "_LOD2", "_Win", "_LOD1_Win", "_LOD2_Win")] + \
                   [f"{name}_{m}.png" for m in ("BaseColor", "Normal", "MaskMap", "ControlMask")] + \
                   ["settings.json", f"{name}.manifest.json"]
            check(sorted(want) == files, f"{kind}: smoke folder holds exactly the shipping files", f"({files})")
            with open(os.path.join(d, f"{name}.manifest.json"), encoding="utf-8") as fh:
                m = json.load(fh)
            fz, _old, _sha, _p = LT.frozen_block_m(kind)
            check(json.dumps(m["prefab_contract"]["frozen"], sort_keys=True) == json.dumps(fz, sort_keys=True),
                  f"{kind}: M1 manifest frozen block == the deployed K prefab (json, sorted keys)")
            check((m.get("fine_detail") or {}).get("rows") == {"vanilla": FD_ROWS_M[kind][0], "mod": FD_ROWS_M[kind][1]},
                  f"{kind}: the smoke manifest carries the fine-detail block (step 3c on by default)",
                  f"({(m.get('fine_detail') or {}).get('rows')})")
            check(m["validation"]["errors"] == 0, f"{kind}: validate_asset_folder 0 errors",
                  f"({m['validation']})")
            check(m["tris"]["2"] <= LT.LOD2_TARGET_M[kind] * 1.1 and m["tris"]["1"] <= 0.5 * m["tris"]["0"],
                  f"{kind}: M15 LOD chain {m['tris']}")
            check(m["weather"] and m["weather"]["intensity"] >= 0.40, f"{kind}: M13 stepped weathering shipped",
                  f"({m['weather']['intensity'] if m['weather'] else None})")
            if kind == "drive":
                lv = m["tension_scale"]["aa_levels"]
                check(lv >= 8, "drive: M11 tension scale digits are anti-aliased (vector, the AA rule)", f"({lv} levels)")
        # M11 negative: the K / L stamp (lift_terminal.stamp_tension_scale, the 3 x 5 bitmap) through
        # the same rule: its marks carry no partial coverage
        kb, kn, km, kc = (np.zeros((2048, 2048, 4), np.float32) for _ in range(4))
        LT.stamp_tension_scale(kb, kn, km, kc, 2048)
        r0, c0, c1 = int(LT.SCALE_V0 * 2048), int(LT.SCALE_U0 * 2048), int(LT.SCALE_U1 * 2048)
        lvk = LT.scale_aa_levels_m(np.clip(kb[r0:, c0:c1, 0] / 0.85, 0.0, 1.0))
        check(lvk < 8, "M11 negative: the K 3 x 5 bitmap scale stamp fails the vector rule", f"({lvk} levels)")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def main():
    t0 = time.perf_counter()
    positives()
    negatives()
    m18_shipped()
    f0_f3()
    f1_m()
    f2_m()
    f4_m()
    m19_forbid()
    nitpick_a_m()
    m17_api()
    bake_smoke()
    dt = time.perf_counter() - t0
    if FAILS:
        print(f"FAIL test_terminal_fg_m: {len(FAILS)} failed: {FAILS}")
        sys.exit(1)
    print(f"PASS test_terminal_fg_m: M1-M15 + M17 on both stations, M18 on the top (generator + shipped FBX + kit rows), F0-F4 (fine-detail step 3c), M19 no STAY SEATED on the top (+ 3 negatives, shipped), nitpick A M20 / M21 / S-3 (+ negatives, shipped), every negative raises, 512 bake smoke "
          f"through main() ({N_OK[0]} ok, {dt:.0f} s)")


main()
