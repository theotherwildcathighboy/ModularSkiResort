"""
T121 (2026-10-20): list the modeled rope parts of each shipped station, by part name, in the EXPORT
frame.

The script builds every station's tagged mesh with its own generator (no finish, no bake, no
export). The rope parts take no bevel, so their vertices equal the finish build's. Per station, it
writes the vertices of every face whose part name starts with one of that generator's rope prefixes,
in the generator's object space (meters). station_cable_strip.py matches these against the shipped
FBX.

    blender --background --factory-startup --python-exit-code 1 --python assets\\blender\\station_rope_parts.py -- <out.json>

Rope prefixes (the lift ropes only. The comm cable, queue-maze ropes and anything else stay):
    lift_terminal_detach.py (DQ, gondola): haulrope_ (anchor -> bullwheel), wheelrope (the rope round the bullwheel)
    lift_terminal.py M (fixed grip):        ropechord_ (anchor -> wheel), wheelrope (half torus)
    tram_station.py:                        rope_haul, rope_track_
"""
import json
import os
import sys

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import cs2_asset  # noqa: E402

ROPES = {"detach": ("haulrope_", "wheelrope"), "fg": ("ropechord_", "wheelrope"), "tram": ("rope_haul", "rope_track_")}


def export_frame(co):
    """Raw object-space meters as the generator leaves them. station_cable_strip.py finds the axis map
    to the FBX (one of the 48 signed axis permutations, x 100) by matching, so a generator that
    orients early or late works either way."""
    return co


def rope_faces(t, table, prefixes):
    me = t.data
    attr = me.attributes["part"].data
    mw = np.array(t.matrix_world)
    co = np.array([(mw @ np.array([*v.co, 1.0]))[:3] for v in me.vertices])
    out = {}
    for poly in me.polygons:
        name = table[attr[poly.index].value - 1]
        if name.startswith(prefixes):
            out.setdefault(name, []).append([list(map(float, export_frame(co[list(poly.vertices)])[k]))
                                             for k in range(len(poly.vertices))])
    allco = export_frame(co)
    return out, allco.min(0).tolist(), allco.max(0).tolist()


def detach(kind, gondola):
    import lift_terminal
    import lift_terminal_detach as D
    import cs2_sheaves as S
    D.FINE_DETAIL = True
    D.ROPE_ON_CROWN = False
    S.set_wheels("v2")
    p = D.params_for(kind, gondola, D.load_carrier("gd8" if gondola else "dq"))
    lift_terminal.clear_scene()
    t, table, _src = D.assemble(D.build_station(p), False, p)
    return t, table, "detach"


def fg(kind):
    import lift_terminal
    lift_terminal.clear_scene()
    t, table, _src = lift_terminal.assemble_m(lift_terminal.build_station_m(kind), False, kind)
    return t, table, "fg"


def tram(role):
    import tram_station as TS
    args = TS.parse_args(["--role", role])
    TS.apply_fine(args)
    TS.clear_scene()
    t, table, _src, _meta = TS.assemble(role, args, False)
    return t, table, "tram"


def main():
    out_path = sys.argv[sys.argv.index("--") + 1]
    jobs = {"SkiDQBaseStationB": lambda: detach("base", False), "SkiDQTopStationB": lambda: detach("top", False),
            "SkiGondolaBaseStationB": lambda: detach("base", True), "SkiGondolaTopStationB": lambda: detach("top", True),
            "SkiLiftBaseStationN": lambda: fg("drive"), "SkiLiftTopStationN": lambda: fg("return"),
            "SkiTramBaseStationB": lambda: tram("base"), "SkiTramTopStationB": lambda: tram("top")}
    only = sys.argv[sys.argv.index("--") + 2:]
    res = {}
    for name, job in jobs.items():
        if only and name not in only:
            continue
        t, table, fam = job()
        bpy.context.view_layer.update()
        faces, lo, hi = rope_faces(t, table, ROPES[fam])
        res[name] = {"family": fam, "prefixes": list(ROPES[fam]), "bbox": [lo, hi],
                     "parts": {k: {"faces": len(v), "verts": v} for k, v in sorted(faces.items())}}
        print(f"[ropes] {name}: " + ", ".join(f"{k} {len(v)} faces" for k, v in sorted(faces.items())))
    json.dump(res, open(out_path, "w"))


main()
