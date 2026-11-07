"""
refreeze_m_manifests.py - re-freezes the prefab_contract block of the FG generation-M station
manifests (assets/import/stations/fixedgrip/M/*/*.manifest.json) from the DEPLOYED K building
prefabs (gamedata-mirror).

Run it in the SAME round as any surgery on SkiBaseStationBuilding / SkiTopStationBuilding
(DECISIONS 388, 592 q). Otherwise test_fg_family_seams M1 and tools/check-lift-align.js (MANIFESTSHA) fail on a
stale baseline.

The script re-runs lift_terminal.m1_frozen. That function re-checks SkiCatalog, carriers.json, the
HV sub-lane ends and the hosted tower pose against the prefab. The script writes the new frozen
block and shas. It adds a `refrozen` note that nests the previous one.

The script first reproduces the JSON byte format (CRLF, indent 2, ensure_ascii). It stops if it
cannot. It touches no FBX, texture or mesh.

    blender --background --factory-startup --python-exit-code 1 --python assets\blender\refreeze_m_manifests.py -- --why "<what changed in the prefab>" [--write]

Without --write it only reports (dry run).
"""
import datetime
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import lift_terminal as LT  # noqa: E402

NL, CR = chr(10), chr(13)


def dump(m, ea, tail):
    return (json.dumps(m, indent=2, ensure_ascii=ea) + tail).replace(NL, CR + NL).encode("utf-8")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    write = "--write" in argv
    why = argv[argv.index("--why") + 1] if "--why" in argv else ""
    if write and not why:
        raise SystemExit("--write needs --why \"<what changed in the prefab>\"")
    for kind, st in (("drive", "SkiLiftBaseStationM"), ("return", "SkiLiftTopStationM")):
        p = os.path.join(LT.IMPORT_M, "stations", "fixedgrip", "M", st, st + ".manifest.json")
        raw = open(p, "rb").read()
        m = json.loads(raw.decode("utf-8"))
        fmt = None
        for ea in (False, True):
            for tail in ("", NL):
                if dump(m, ea, tail) == raw:
                    fmt = (ea, tail)
        if fmt is None:
            raise SystemExit(f"{st}: the manifest byte format is not reproduced; refusing to write")
        old = m["prefab_contract"]
        new = LT.m1_frozen(kind)       # raises on SkiCatalog / carriers / deployed-prefab drift
        for k in new:
            if k not in ("frozen", "frozen_sha256", "source_sha256") and new[k] != old.get(k):
                print(f"  NOTE {st}: {k} changed besides the frozen block")
        print(f"{st}: source_sha256 {old['source_sha256'][:12]} -> {new['source_sha256'][:12]}"
              f"{' (unchanged)' if old['source_sha256'] == new['source_sha256'] else ''}")
        new["refrozen"] = {"utc": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
                           "why": why or "(dry run)", "previous_source_sha256": old["source_sha256"]}
        if old.get("refrozen"):
            new["refrozen"]["earlier"] = old["refrozen"]
        m["prefab_contract"] = new
        if write:
            with open(p, "wb") as fh:
                fh.write(dump(m, fmt[0], fmt[1]))
            print(f"  written {p}")


main()
