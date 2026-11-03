# vanilla-guid-map.py - map every vanilla prefab UnityGUID to its prefab name (2026-10-13, DECISIONS 230).
# Reads Cities2_Data\resources.assets (Unity 2022.3.71f1, SerializedFile v22, no type trees): the MonoBehaviour
# field "PrefabsMap" holds N GUID strings + N PPtrs; each target MonoBehaviour's m_Name sits at data+12+4+12.
# Read-only. Run with Blender's python: "C:\Program Files (x86)\Steam\steamapps\common\Blender\5.2\python\bin\python.exe" -I tools\vanilla-guid-map.py
# Writes docs/research/field/2026-10-13/guid-harvest.json (the S1 targets) and vanilla-guid-map.json (all 47,432).
# Edit the "targets" list for a new harvest; validate against the known GUIDs printed first.
# Patch day: -I tools\vanilla-guid-map.py --out=<file> writes only the map, for node tools/game-surface-check.js --guid-map=<file>.
# The asserts below stop on a new SerializedFile version: a game patch on another Unity, not a missing GUID.

import mmap, struct, re, json, collections, sys
outArg = next((a[6:] for a in sys.argv[1:] if a.startswith("--out=")), None)
fp = r"C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II\Cities2_Data\resources.assets"
f = open(fp, "rb"); mm = mmap.mmap(f.fileno(), 0, access=mmap.ACCESS_READ)
# --- SerializedFile v22 header ---
version = struct.unpack_from(">I", mm, 8)[0]
endian = mm[16]
metaSize, fileSize, dataOffset = struct.unpack_from(">qqq", mm, 16)
print("version", version, "endian", endian, "metaSize", metaSize, "fileSize", fileSize, "dataOffset", dataOffset)
assert version == 22 and endian == 0
p = 48
def cstr(p):
    e = mm.find(b"\0", p); return mm[p:e].decode(), e + 1
unityVer, p = cstr(p); platform = struct.unpack_from("<i", mm, p)[0]; p += 4
enableTT = mm[p]; p += 1
print("unity", unityVer, "platform", platform, "typetree", enableTT)
ntypes = struct.unpack_from("<i", mm, p)[0]; p += 4
types = []
for i in range(ntypes):
    classID = struct.unpack_from("<i", mm, p)[0]; p += 4
    isStripped = mm[p]; p += 1
    scriptIdx = struct.unpack_from("<h", mm, p)[0]; p += 2
    if classID == 114: p += 16
    p += 16
    if enableTT:
        nNodes, sbuf = struct.unpack_from("<ii", mm, p); p += 8
        p += nNodes * 32 + sbuf
        ndep = struct.unpack_from("<i", mm, p)[0]; p += 4 + 4 * ndep
    types.append((classID, scriptIdx))
nobj = struct.unpack_from("<i", mm, p)[0]; p += 4
print("types", ntypes, "objects", nobj)
objs = {}
for i in range(nobj):
    p = (p + 3) & ~3
    pathID, byteStart = struct.unpack_from("<qq", mm, p); p += 16
    size, typeID = struct.unpack_from("<Ii", mm, p); p += 8
    objs[pathID] = (dataOffset + byteStart, size, types[typeID][0])
print("object table parsed; classIDs:", collections.Counter(v[2] for v in objs.values()).most_common(5))
def rd_str(pos):
    n = struct.unpack_from("<i", mm, pos)[0]
    return mm[pos+4:pos+4+n].decode("utf-8", "replace"), pos + 4 + ((n+3)&~3)
def mb_name(pathID):
    off, size, cid = objs[pathID]
    if cid != 114: return None
    return rd_str(off + 12 + 4 + 12)[0]   # m_GameObject PPtr, m_Enabled+pad, m_Script PPtr, m_Name
# --- PrefabsMap: [N][guid strings][N][PPtr] ---
anchor = mm.find(b"\x0a\x00\x00\x00PrefabsMap")
gpos = anchor + 4 + 12 + 4   # len+"PrefabsMap"+pad(2)+... recompute: 4 + 10 + pad to 12 = 16, then int32 N
gpos = anchor + 16
N = struct.unpack_from("<i", mm, gpos)[0]; gpos += 4
guids = []
for i in range(N):
    s, gpos = rd_str(gpos); guids.append(s)
N2 = struct.unpack_from("<i", mm, gpos)[0]; gpos += 4
assert N2 == N, (N, N2)
ptrs = [struct.unpack_from("<iq", mm, gpos + 12*i) for i in range(N)]
print("PrefabsMap N", N, "all fileID 0:", all(fid == 0 for fid, _ in ptrs))
g2n = {}
missing = 0
for g, (fid, pid) in zip(guids, ptrs):
    if fid != 0 or pid not in objs: missing += 1; continue
    g2n[g] = mb_name(pid)
print("mapped", len(g2n), "missing", missing)
known = {"ec48568022d46c1448c6620d95f993c1":"ParkSign placeholder","cfc12fde01d9da24b86ddedd657eaae1":"WallLightShortRangeRandom01","fb4247f7a6f75b1478d9dd01ac88ce86":"BarrelRandom01","4010d5851d69dbb418c62e6bd4d19759":"PowerLinePole01 Placeholder","95cbbe17c442dc940be6e7986155c05d":"ElectricalBox placeholder (rejected)","5d88ac45a40b30341a79535930e3b735":"HV cable lane"}
print("--- validation ---")
for g, l in known.items(): print("   %-36s -> %r" % (l, g2n.get(g)))
if outArg:
    json.dump(dict(sorted(g2n.items())), open(outArg, "w"), indent=0)
    print("written %s (%d entries)" % (outArg, len(g2n)))
    sys.exit(0)
n2g = collections.defaultdict(list)
for g, n in g2n.items(): n2g[n].append(g)
targets = ["FlagPole01","FlagPole02","SportParkFlag01","SportParkTrainingCone02","CampgroundBench02","ElectricalBox01","ElectricalBox02","CampgroundFireplace01","CampgroundFireplace02","CampgroundFireplace03"]
print("--- targets ---")
out = {}
for t in targets:
    gs = n2g.get(t, [])
    print("   %-26s -> %s" % (t, gs)); out[t] = gs
json.dump({"source": fp, "unity": unityVer, "object": "PrefabsMap", "count": N, "targets": out}, open("docs/research/field/2026-10-13/guid-harvest.json", "w"), indent=2)
json.dump(dict(sorted(g2n.items())), open("docs/research/field/2026-10-13/vanilla-guid-map.json", "w"), indent=0)
print("written guid-harvest.json + vanilla-guid-map.json (%d entries)" % len(g2n))
