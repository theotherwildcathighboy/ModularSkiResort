"""
test_door_kit - door kit v2 contracts (docs/research/art/door-kit-2026-10-17.md), the HARDWARE GATE first
(Richard 2026-10-17: "It's good to render levers or doorknobs. Vanilla buildings tend to have them").

    blender --background --factory-startup --python-exit-code 1 --python tests\\test_door_kit.py

Asserts:
  1. one door per family and leaf count (glazing double + 2 sidelights + transom, glazing solid double,
     chalet single + double, utility flush + with a vision panel, utility without a casing): every leaf
     carries a hardware part (door_<tag>_pull / _lever / _knob), DOOR2_HARDWARE lists them, and the main-
     mesh tri count equals door_tris() for the same arguments;
  2. hardware="none:<reason>" builds without hardware and records the reason; "none:", "none", False,
     None and any other value RAISE; a family whose handle kind is not a hardware kind raises;
  3. door_unit outside door_kit() raises; entry() inside door_kit() returns the v2 door (gate applies);
  4. every door kit part lands in door2_flat() / door2_protect(); `_Win` panes are not in them; the
     liner `reveal_d<name>` has 3 faces and no upward (stool) face.
Exit 1 on the first failure; one PASS line otherwise.
"""

import os
import sys
import time
import traceback

import bpy

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cs2_bldg as K                     # noqa: E402
from cs2_bldg import WallRef             # noqa: E402

CHECKS = []


def ok(msg):
    CHECKS.append(msg)
    print("  ok", msg)


def clear():
    for o in list(bpy.data.objects):
        me = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if isinstance(me, bpy.types.Mesh) and me.users == 0:
            bpy.data.meshes.remove(me)


def tris(parts, skip_win=True):
    t = 0
    for o, _k in parts:
        if skip_win and o.name.startswith("win_"):
            continue
        t += sum(len(p.vertices) - 2 for p in o.data.polygons)
    return t


CASES = (
    # family, name, kwargs to door_unit, door_tris kwargs
    ("glazing", "gd", dict(w=2.4, h=2.7, sidelights=2, sidelight_w=0.7, room="r"),
     dict(leaves=2, sidelights=2, transom=True)),
    ("glazing", "gs", dict(w=1.6, h=2.4, glass=False, room="r"),
     dict(leaves=2, glass=False)),
    ("chalet", "cs", dict(w=1.0, h=2.1, room="r"), dict(leaves=1)),
    ("chalet", "cd", dict(w=1.8, h=2.4, room="r"), dict(leaves=2)),
    ("utility", "uf", dict(w=1.0, h=2.2, room="r", vision=False), dict(leaves=1, vision=False)),
    ("utility", "uv", dict(w=0.9, h=2.1, room="r"), dict(leaves=1)),
    ("utility", "un", dict(w=0.85, h=1.97, room="r", casing="none"), dict(leaves=1, casing="none")),
)


def build(family, name, kw, hardware="auto"):
    wall = WallRef("+X", 5.0)
    with K.door_kit(family=family, holes="all", mode="full", arrival=(1.0, 0.0)):
        parts = K.door_unit(name, wall, 0.0, kw.pop("w"), kw.pop("h"), 0.15, hardware=hardware, **kw)
        return parts, dict(K.DOOR2_HARDWARE), K.door2_flat(), K.door2_protect()


def check_hardware_present():
    for family, name, kw, tk in CASES:
        clear()
        parts, hw, flat, prot = build(family, name, dict(kw))
        names = {o.name for o, _k in parts}
        L = tk.get("leaves", 1)
        tags = [f"{name}{k}" for k in range(L)] if L > 1 else [name]
        for tag in tags:
            got = [n for n in (f"door_{tag}_pull", f"door_{tag}_lever", f"door_{tag}_knob") if n in names]
            assert got, f"{family} {name}: leaf {tag} has no hardware part: {sorted(names)}"
        assert hw.get(name) and len(hw[name]) == L, f"{family} {name}: DOOR2_HARDWARE {hw}"
        want = K.door_tris(family, **tk)
        got_t = tris(parts)
        assert got_t == want, f"{family} {name}: {got_t} tris != door_tris {want}"
        kit = {o.name for o, _k in parts if not o.name.startswith("win_")}
        assert kit == set(flat) == set(prot), f"{family} {name}: registry {set(flat) ^ kit}"
        assert not any(n.startswith("win_") for n in flat), f"{name}: a pane in the flat registry"
        liner = next(o for o, _k in parts if o.name == f"reveal_d{name}")
        assert len(liner.data.polygons) == 3, f"{name}: liner faces {len(liner.data.polygons)}"
        liner.data.update()
        assert all(p.normal.z < 0.5 for p in liner.data.polygons), f"{name}: liner has a stool"
        ok(f"{family} {name}: {L} leaf/leaves, hardware {hw[name]}, {got_t} tris = door_tris")


def check_opt_out():
    family, name, kw, tk = CASES[2]
    clear()
    parts, hw, _f, _p = build(family, "co", dict(kw), hardware="none:behind the fence, 20 m+")
    names = {o.name for o, _k in parts}
    assert not any(n.endswith(("_pull", "_lever", "_knob")) for n in names), "opt-out built hardware"
    assert hw["co"] == "none:behind the fence, 20 m+", hw
    assert tris(parts) == K.door_tris(family, hardware="none:x", **tk)
    for bad in ("none:", "none:   ", "none", False, None, True, "off", ""):
        clear()
        try:
            build(family, "cb", dict(kw), hardware=bad)
        except ValueError:
            continue
        raise AssertionError(f"hardware={bad!r} did not raise")
    ok("opt-out needs 'none:<reason>'; 'none:', 'none', False, None, True, 'off', '' raise")
    # a family without a hardware kind: the gate refuses it
    saved = K.DOOR_FAMILIES["utility"]["handle"]
    K.DOOR_FAMILIES["utility"]["handle"] = None
    try:
        clear()
        try:
            build("utility", "ux", dict(CASES[4][2]))
        except RuntimeError as e:
            assert "hardware" in str(e), e
        else:
            raise AssertionError("a family with handle None built a door")
    finally:
        K.DOOR_FAMILIES["utility"]["handle"] = saved
    ok("a family with no hardware kind raises")


def check_context():
    clear()
    try:
        K.door_unit("x", WallRef("+X", 0.0), 0.0, 1.0, 2.1, 0.15, room="r")
    except RuntimeError:
        pass
    else:
        raise AssertionError("door_unit outside door_kit() did not raise")
    with K.door_kit(family="chalet", holes="all", mode="full", arrival=(1.0, 0.0)):
        parts = K.entry("e", WallRef("+X", 5.0), 0.0, w=1.2, h=2.2, lamps=0, room="r")
    names = {o.name for o, _k in parts}
    assert "entry_e" in names and "door_e_lever" in names and "reveal_de" in names, sorted(names)
    assert K.DOOR_KIT["version"] == 1, "door_kit() did not restore the v1 state"
    ok("door_unit needs door_kit(); entry() inside it is the v2 door with its lever; state restored")


def main():
    t0 = time.perf_counter()
    check_hardware_present()
    check_opt_out()
    check_context()
    print(f"PASS test_door_kit: {len(CHECKS)} checks ({time.perf_counter() - t0:.1f} s)")


if __name__ == "__main__":
    try:
        main()
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        print("FAIL test_door_kit")
        sys.exit(1)
