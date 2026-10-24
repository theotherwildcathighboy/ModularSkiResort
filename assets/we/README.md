# Write Everywhere layout (draft) — `K45_WE/layouts/SkiResortGate.wedefault.xml`

The Welcome Sign's resort name, drawn by the optional Write Everywhere (WE) mod on the
plain name plank of `SkiWelcomeSignA` (assets/import/signs/A). Without WE the plank stays
plain wood and the baked "SKI AREA" header + emblem carry the vanilla read (ROUND-SPEC R14).
Data only: no reference to WE, no Harmony, nothing copied from WE (it has no license).

Contract: `docs/research/art/assets-2026-10/design/civic-sign.md` §5 (with the skeptic's
notes). Geometry constants come from `assets/blender/welcome_sign.py` and are written to the
sign's manifest `"we"` block:

| Constant | Value (game frame: lot-center origin at grade, +Z = road, y up) |
|---|---|
| `PLANK_CENTRE_GAME` | (0.000, 2.550, −0.460), plank normal (0, 0, +1) |
| `PLANK_W` × `PLANK_H` | 7.50 × 1.50 m (ONE quad, contract S1) |
| `WE_MARGIN` / `WE_MAX_WIDTH` | 0.45 / 6.60 m |
| `WE_STANDOFF` / `WE_OFFSET` | 0.004 m / (0.000, 2.550, −0.456) |

## Files (updated 2026-10-13, twenty-fifth look; full write-up `docs/research/art/assets-2026-10/build/writable-signs-we.md`)

| Path | What |
|---|---|
| `K45_WE/layouts/SkiResortGate.wedefault.xml` | the plank name above (hand-kept, asserted by `tests/test_civic_sign.py`; one textMesh, do not add nodes here) |
| `K45_WE/layouts/{SkiLodgeGrand,SkiSchool}.wedefault.xml` | painted boards of the Swanky Grand Lodge and the Ski School (GRAND LODGE, SKI SCHOOL): a board-colored cover patch (node `<board>: patch`) + text (`<board>: text`), OFF until the player sets the patch's Show when to Always |
| `K45_WE/layouts/{SkiLodgeMid,SkiLodgeSummit,SkiLodgeHut}.wedefault.xml` | the blank boards (Chalet Lodge x 3, Swanky Summit Lodge x 1, Mountain Hut x 1; in-game names from `mod/SkiResortMod/LocaleEN.cs`): one empty text node each |
| `xpos/K45_WE/layouts/*` | the retired P set (x as in the pre-2026-10-16 manifests): a record only. `K45_WE/layouts` is now the N set (x negated, yaw mirrored: the game negates FBX x; x-sign gate settled N by the 2026-10-15 playtest, swapped in 2026-10-16 / T14); `xneg/` is its pre-swap duplicate (byte-equal, no longer written) |
| `optional/SkiResortGate.header-overlay.xml` | opt-in patch + text over the baked "SKI AREA" header (not shipped) |
| `gen_layouts.ps1` | writes all of the above except SkiResortGate (`-PaintedOn` starts the painted-board patches drawn) |

## Deploy

- **File name** = prefab name + `.wedefault.xml` (WETemplateManager.cs:1344-1347).
- **Dev deploy (by hand, first time)**: copy `K45_WE\` into
  `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\ImportedData\SkiResortGate\`
  (beside `SkiResortGate.Prefab`). `sync-gamedata -Restore` is all-or-nothing, so copy by hand;
  afterwards the game → repo sync picks the file up once `tools/sync-gamedata.ps1:19` includes
  `*.xml` (contract §4.6 step 9, not done in this build).
- **Release**: `K45_WE\layouts\*.wedefault.xml`, six files (SkiResortGate, SkiLodgeGrand,
  SkiLodgeHut, SkiLodgeMid, SkiLodgeSummit, SkiSchool), at the listing root beside the `.cok`
  (WE changelog 1.0.0.6). Branch N is settled (2026-10-15 playtest) and `K45_WE\layouts\` is the N set since
  2026-10-16. `xpos\`, `xneg\` and `optional\` are not shipped. The `.cok`
  packaging route is itself an open check.
- **Proof it loaded**: `Loading data for asset: …` (WEAssetsSettingsLoaderUtility.cs:40) in
  `Logs\Mods_K45_WE.log`.

## Authoring loop (R14)

1. Deploy this draft. 2. The owner places a sign with WE on and fixes the text in WE's editor.
3. The owner saves it as the prefab default → `ModsData\.Klyte45Mods\WriteEverywhere\layouts\SkiResortGate.wedefault.xml`
   (WELayoutController.cs:193-199). 4. We copy that file over this draft.
5. The owner MOVES the ModsData copy out: it applies to every SkiResortGate and concatenates with
   ours, which doubles the text.

Rules: never `<hideMesh>` (re-creates the prefab entity, our session caches go stale); no atlas
from the owner's WE folder. Fonts: the gate plank and the blank boards use the default
(`fontName=""` = Source Sans Pro); the two painted boards name `BOOKOSB` (GRAND LODGE) and
`FRADMCN` (SKI SCHOOL) and the opt-in header overlay names `BOOKOSB` (5 nodes in all: 2 per set
in the P and N sets + 1 in the overlay; Windows' Bookman Old Style Bold / Franklin Gothic Demi
Cond; WE falls back to Source Sans Pro when they are not installed). The asset route loads no
fonts and nothing is shipped.

## Verified vs guessed (WE 2.0.0.15 decompile, re-read 2026-10-11 for this draft)

| Item | Status | Evidence |
|---|---|---|
| Root `WELayout`; children `self`, `children`, `variable`, `metadata`, `hideMesh` | verified | WETextDataXmlTree.cs:15-33 (`[XmlRoot("WELayout")]`, `[XmlElement("self")]`, `[XmlElement("children")]`) |
| Root without `self` holding top-level `children` | verified (format) | installed 140670_4 `K45_WE/layouts/BR422*` (asset route), 127990_8 NA_BusStop02 |
| `self@itemName`; elements `transform`, `textMesh`, `defaultStyle` | verified | WETextDataXml.cs:1292-1332 |
| `transform`: `offsetPosition` / `offsetRotation` / `scale` (x/y/z attributes), `@alignment`, `@arrayAxisOrder`, `mustDraw`, `instanceCount` (default −1), `arrayInstances` / `arraySpacing` setters | verified | WETextDataXml.cs:16-100 (TransformXml; the two array properties are write-only setters into `arrayInstancesData` / `arraySpacingData`) |
| `textMesh@fontName`, `@rescaleHeightOnTextOverflow`, `text` (XmlText + `@formulae`), `MaxWidthMeters` element | verified | WETextDataXml.cs:361-382 (legacy attribute `maxWidthMeters` also read) |
| `MaxWidthMeters` in the node's meters; with rescale on, scale.y is SET to the clamped scale.x | verified | WEPreCullingSystem.cs:616-621 (skeptic claim 7: equal only because scale x = y = 0.5 here) |
| `defaultStyle` children (`color`, `emissiveColor`, `metallic`, `smoothness`, `emissiveIntensity`, `emissiveExposureWeight`, `coatStrength`, `colorMask1..3`), RGBA hex text | verified | WETextDataXml.cs:593-652 (`FormulaeColorRgbaXml.defaultValueRGBA` via `ColorExtensions.FromRGBA`) |
| `&BelzontWE.Builtin.WEUtitlitiesFn;GetEntityName` = the sign's custom name ("Ski area N" / hub rename) | verified (code) | WEUtitlitiesFn.cs:15-19; NameSystem.cs:289-303 |
| `defaultStyle@renderBackface` default false: a wrong facing makes the text INVISIBLE, not mirrored | verified | WETextDataXml.cs:601-603 |
| WE draws in the target entity's local space (lot center at grade, +Z road) | inferred | sign-we §2.8; our mesh origin is the lot center |
| `offsetRotation 0` faces +Z (readable from the road) | verified by reading (2026-10-13), not seen in game | FontSystem.cs:195-198 negates the glyph x, so the quad reads left to right from the +Z side; 140670 "Front" node (z +8.3, rotation 0) and "Right" (x +1.51, rotation y 90) agree; yaw +90 faces +X |
| `scale 0.5` → cap height | **0.476 m** (computed; the old "~0.55 m" guess was wrong) | cap height = 0.952 m x scale.y for Source Sans Pro Regular (Font.cs:36-59, FontSystem.cs:285, FontServer.cs:81; `build/writable-signs-we.md` section 2.5); "Ski area 1" is 5.7 m wide at scale 1 |
| standoff 0.004 m free of z-fighting | GUESS | WE adds 1 mm itself to children of a node (WEPreCullingSystem.cs:504, 661) |
| `alignment="Center"` vs "Left" on a single line | no effect (verified) | `alignment` only spaces the instances of an array / placeholder (WETemplateManager.cs:667-690); the exports use Left |
| `emissiveIntensity 0` (unlit name at night) | GUESS / taste | NA_BusStop02 uses 0.78 |
| Text LOD cut ~150 m camera distance | verified (changelog) | beyond it the plain plank shows |

Optional color variant (not the default): `<color formulae="&amp;BelzontWE.Builtin.WEUtitlitiesFn;GetMainMeshColor1">F2E8CFFF</color>`
reads MeshColor[0] channel 0 = slot 1 (WEUtitlitiesFn.cs:21-29) and returns magenta when the mesh
has no ColorProperties (the sign surgery adds them, contract §4.6 step 7). With the default green
on the walnut plank the contrast is poor (contract Q4), so the static cream stays.
