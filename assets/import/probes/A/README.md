# Rig probe AlignRigProbeA (lift alignment, 2026-11-02)

The first SKINNED mesh of this project. A dev probe, not a shipped asset (no `Ski` prefix). It proves that an
Editor import of our FBX yields a RenderPrefab with `ProceduralAnimationProperties` whose bones the game
animates. That is the gate for the spinning bullwheel and the pitching grip / sheave train (research
`liftalign-anim-research.md` sections 3 and 5).

Built by `assets/blender/rig_probe.py` (`cs2_asset.export_fbx_skinned`). The other folders under `A/` are
older probes and are not part of this import.

## What is in the folder

| File | Content |
|---|---|
| `AlignRigProbeA.fbx` | LOD0, 524 tris: one mesh skinned to the armature `AlignRigProbeARig` |
| `AlignRigProbeA_LOD1.fbx` | LOD1, 208 tris, skinned to the same three bones |
| `_BaseColor/_Normal/_MaskMap/_ControlMask.png` | 512 atlas, alpha 255, no recolor slots |
| `settings.json` | LOD1 `Skip` + shared LOD1 maps + `"useProceduralAnimation": true` |

Bones (every vertex weighted 1.0 to exactly one bone, checked before export and after a Blender round trip):

| Bone | Parent | Parts | Expected after import (game frame, m) |
|---|---|---|---|
| `Root` | none | base beam, both posts | position (0, 0, 0), rotation (0, 0, 0, 1) |
| `Wheel` | Root | 1.2 m wheel: rim, hub, 4 spokes. The ORANGE spoke + rim block is the marker | position (0.65, 1.00, 0), rotation (0, 0, 0, 1) |
| `Train` | Root | 1.5 m blue bar along the object Z axis, orange arrow on the +Z end, pivot pin | position (-0.55, 1.10, 0), rotation (0, 0, 0, 1) |

The expected values apply PIPELINE rule T (the game negates FBX x) to the exported rest pose. They are
UNVERIFIED until the import. Reading them back is step 1 of the acceptance check.

## What the importer does with it (decompile citations)

- `useProceduralAnimation` is a public field of `Colossal.AssetPipeline.Settings` (1.6.2f1
  Colossal.AssetPipeline decompile `Settings.cs:387`), filled from settings.json by name (`JSONImporter.cs:58`,
  `JSON.WriteInto`). `Settings.GetDefault` already sets it true (`Settings.cs:585`) and the Editor import starts
  from that default (1.6.0f1 `AssetImportPipeline.cs:355`). So the key is explicit, not required. With it true,
  the folder name goes into `createProceduralAnimationForPrefab` (`:403-406`).
- The importer creates the component on LOD0 only, and only when mesh 0 carries BlendIndices + BlendWeight, a root bone and
  bind poses (`:2232-2242`, `GetSkinningInfo :2331-2358`, `:2377-2392`). Bones are the skin's cluster bones with
  their local position / rotation / scale, parent and bind pose (`FBXImporter.cs:393-403` ->
  `AssetImportPipeline.cs:2393-2406`). `m_Type / m_Speed / m_Acceleration / m_ConnectionID / m_SourceID` are not
  in the FBX. A re-import keeps them by bone name only when the prefab already has the component (`:2407-2418`).
- The importer reads only mesh 0 of the FBX (`FBXImporter.cs:137`). LOD1 receives a copy of LOD0's component (`SetupLODs
  :2210-2217`), and `SkinPostProcessor` remaps LOD1's bone indices onto LOD0's bones by name
  (`SkinPostProcessor.cs:168-187`).
- An animation take in the FBX would add an AnimationClip asset and make the bones PlaybackLayer0
  (`AssetImportPipeline.cs:1977-2010`, `FBXImporter.cs:494-563`). The export writes none (`bake_anim=False`).

## Editor import

1. Check that the FBX / PNG files exist (they are git-ignored). If they do not exist, rebuild from `assets/blender`:
   `& "C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe" --background --factory-startup --python-exit-code 1 --python rig_probe.py -- --out ..\import\probes\A`
2. Import in the Editor with **No Preset** (as for every static prop, PIPELINE section 5):
   Project root = `assets/import/probes/A`, Assets folder = `assets/import/probes/A/AlignRigProbeA`.
   You need no other import option. Leave the Import Seed alone.
3. Expected result: `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\ImportedData\c030ee3ff256df91\AlignRigProbeA\`
   with `AlignRigProbeA Mesh_<hash>.Prefab` and `AlignRigProbeA_LOD1 Mesh_<hash>.Prefab`. The import log should
   carry the warning "Mesh prefab AlignRigProbeA Mesh was missing ProceduralAnimationProperties. Please set up
   correctly..." (`AssetImportPipeline.cs:2389`). That line means the component was created.

## Acceptance check (read the JSON before any edit)

In `AlignRigProbeA Mesh_<hash>.Prefab`:
- a `Game.Prefabs.ProceduralAnimationProperties` component with `m_Bones` `$rlength` 3, names `Root`, `Wheel`,
  `Train` (a fourth bone named `AlignRigProbeARig` would mean the armature node came in as the root bone.
  This is UNVERIFIED and harmless: the surgery goes by name).
- `Wheel` and `Train` `rotation` = (0, 0, 0, 1) like the vanilla fan below, positions as in the table above,
  `bindPose` translation = minus the position.
- `AlignRigProbeA_LOD1 Mesh_<hash>.Prefab` carries the same bone block.

If the rotation is not identity, stop. The bone axis mapping differs from the derivation in
`cs2_asset.export_fbx_skinned`, and the B2 writers need that number.

## Prefab surgery: make the wheel spin

Vanilla template: `IndustrialACMachineRoof01 Mesh` (dump `%TEMP%\pltar\dump.zip`, LZMA zip, read it with
Blender's bundled Python), the rooftop AC fan. Its root bone is type 0. Each fan is a child bone with an
identity rest rotation and `PoweredRotation`:

```json
{
    "$id": 7,
    "$type": 4,
    "name": "VentilationFan01",
    "position": { "$type": 5, 0.778437138, 1.85129344, 0.669445753 },
    "rotation": { "$type": 6, 0, 0, 0, 1 },
    "scale": { "$type": 5, 1, 1, 1 },
    "bindPose": {
        "$type": 7,
        "m00": 1, "m10": 0, "m20": 0, "m30": 0,
        "m01": 0, "m11": 1, "m21": 1.192093E-07, "m31": 0,
        "m02": 0, "m12": -1.192093E-07, "m22": 1, "m32": 0,
        "m03": -0.778437138, "m13": -1.85129344, "m23": -0.669445753, "m33": 1
    },
    "parentId": 0,
    "m_Type": 6,
    "m_Speed": 2.3,
    "m_Acceleration": 0.35,
    "m_ConnectionID": 0,
    "m_SourceID": 0
}
```

(Reformatted from the dump, values unchanged. The vanilla LOD1 prefab carries the identical bone block.)

Make the edit in BOTH `AlignRigProbeA Mesh_<hash>.Prefab` and `AlignRigProbeA_LOD1 Mesh_<hash>.Prefab`, on the
`Wheel` element of `m_Bones` only:

| Field | Imported | Set to | Why |
|---|---|---|---|
| `m_Type` | 0 | **6** (`PoweredRotation`, `Game.Prefabs/BoneType.cs:11`) | spin about the bone's local Y (`ObjectInterpolateSystem.cs:841-846`, `AnimateRotatingBoneY :3826-3845`) |
| `m_Speed` | 0 | **0.5** | rev/s. MeshSystem multiplies by 2 pi (`Game.Prefabs/MeshSystem.cs:521-536`). One turn every 2 s. The sign is the direction |
| `m_Acceleration` | 0 | **0.2** | rev/s2. It must be nonzero, or momentum never leaves its reset value (`:3829-3838`) |

Leave `Root` and `Train` at `m_Type 0` (None). Vanilla never writes a None bone (`ObjectInterpolateSystem.cs:807-1030`
has no None case), so the bar staying level is itself the check that our future writer owns that bone. Do not
touch position, rotation, bindPose or parentId.

Rules (PIPELINE section 6, prefab-surgery skill): Use the Edit tool only. Use UTF-8 without BOM. Keep `$type`
references as they are. Then run `tools\validate_prefab.ps1 -Path <file>` for each file and `tools\sync-gamedata.ps1`.
A re-import keeps the three values by bone name (`AssetImportPipeline.cs:2407-2418`). Re-check them after any re-import.

Power gate: `PoweredRotation` reads the object's own ElectricityConsumer, else its efficiency, else the owner
chain, else 1 (`ObjectInterpolateSystem.cs:1275-1316`). A free-standing probe therefore spins without power.
Bones exist only while the object is near the camera, and the first frame starts at a random angle
(`AnimateRotatingBoneY :3829-3833`).

## What a pass looks like

1. The JSON shows the component, three bones, identity rotations (the import works for a Blender rig).
2. Place the probe in game (wrapped in an object prefab the usual way, PIPELINE section 6). Then the orange spoke
   turns, the posts and the bar stand still, and LOD1 at distance turns too.
3. Then the same bone with `m_Type 0` driven by our code (research section 2.3), and the `Train` bone pitched by
   our writer about its local X.

UNVERIFIED until the import:

- the exact bone list (armature node as an extra root?)
- the bone axis mapping (identity predicted)
- that the native FBX reader takes no empty animation take from the file
- the spin direction for a positive `m_Speed`
- what an LOD without skin would do (both LODs are skinned here)

## Result (2026-11-02, imported through the bridge)

- The JSON had the component, three bones `Root` / `Wheel` / `Train`, the predicted positions and identity child rotations. The
  ROOT came in as rotation (-0.7071, 0, 0, 0.7071) scale 100 (the FBX scene conversion) with identity bind poses. The game composes
  the root into every skin matrix, so the surgery also set Root to (0, 0, 0, 1) / (1, 1, 1). Do this after every re-import (gate
  BONEROOT in `tools/check-lift-align.js`).
- We wrapped the probe in a game-local object prefab `AlignRigProbe` (top level of ImportedData, not synced, not shipped) to place it.
- In game: true size and orientation. The wheel turns (positive `m_Speed` = clockwise seen from above, about local Y). The bridge
  `bone` op pitched `Train` 20 and 45 deg about X. It held and LOD1 followed. PASS on every point of "What a pass looks like".
