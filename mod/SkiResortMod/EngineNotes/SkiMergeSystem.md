# SkiMergeSystem: merging an existing building into a ski area as a lodge

A player can merge a building the mod did not make (a park, a landmark, a residential, commercial, mixed or office signature, a decor
building) into a ski area as one of our lodge tiers. The building keeps its prefab, its renters, its own jobs and its own tourism; the
tier adds its score, skier room, staff and attraction share. Pure rules: `MergeRules` (tests/BuildingBatch MI/MK/MS/MF/MJ/MA/MT/MD/MO).
Research and the P0 probe: docs/research/merge-asset-into-resort-2026-11-02.md, merge-asset-p0-2026-11-02.md (local docs).

## Add alongside, never strip
Vanilla cannot give back what we remove: an `AttractivenessProvider` that came with the prefab is added only at creation
(Attraction.cs:22-34), a `WorkProvider` only by its initialize system, renters only by the property market. So the merge adds, and
unmerge removes only what the merge added (`SkiMergedLodge.FlagAddedProvider`). The attractiveness system never drops a merged
building's provider: not when it is switched off, not when it is in no area (`m_MergedTier` checks in ScoreAreas and the orphan step).

## Identity
`LodgeIdentity.Resolve(catalogRow, tagged, tier, lodges)`: our prefab's catalog row first, whatever a tag says; else the tier the tag
names. MI1-MI4 prove our own lodges resolve exactly as `SkiCatalog.LodgeByPrefab`. The call sites that need the tier of any lodge
(attraction walk, stats ladder, mountain jobs) go through it or through `SkiResortAttractivenessSystem.MergedView.Tier`. The sites that
stay prefab-only on purpose: the guest counter, tickets and lodge spend (a merged building is not a guest destination in v1), lodge
numbering (it keeps its own name), parking (it takes no arrivals), off-season staffing of its own WorkProvider (never touched).
Gate LODGEID (`tools/check-lodge-identity.js`, CI and preflight): every prefab-keyed lodge lookup (`IsLodge`, `LodgeDefs`,
`LodgePrefabSet`) is counted per file with its reason; a new one fails until it is routed or reviewed.

## Counting (SkiResortAttractivenessSystem.Merged.cs)
- `CollectMerged` after the park walk: every top-level tagged building that is not ours and still qualifies (`Classify`, re-read each
  recompute) joins the top-level list as a lodge. One that no longer qualifies is listed `AssetMissing` and counted for nothing.
- `LinkTopLevel` links it to the nearest sign within the tether like our lodges, but it roots no supply chain, is never the carrier and
  needs no road (no `AddSupplyInputs`).
- `PlaceMerged`, after the runs and lifts of the same recompute: counted only within `SkiDayRules.SlopesideMaxM` of a run outline or a
  lift station (base, and top when known) of its own area; re-checked every recompute, never sticky. Its tier weight folds into the
  area efficiency on the carrier's staffing like a mountain lodge (`FoldLodgeEfficiency`, moved after `PlaceMerged`).
- Recompute triggers: the merge actions (`RequestMergedRecompute`, also while paused) and the watcher's `m_MergedEvents` (a merged
  building Updated or Deleted: moved, relocated, bulldozed).

## Attraction write
Tourism sums a² (TourismSystem.cs:95-107), so the write is round(sqrt(V² + P²)): V = the building's own vanilla value, P = PerLodge.
Vanilla AttractionSystem writes every provider on its update slice (frame slice 16 of 16: once per 256 frames), 0 without AttractionData
(AttractionSystem.cs:79-121). On the tick of the building's own slice the job reads V from the provider (vanilla wrote it this frame) into
the runtime `SkiMergedVanillaAttraction` (never saved), then writes the combine; on other ticks it writes the combine of the cached V.
Nothing is written until V is known, so the saved value stands after a load. Only the simulation tick reads V (`m_SimTick`): a paused
recompute's write would read back our own last value and grow it. P0 measured: 185 read, 210 written, no flicker over 1,215 UI frames,
vanilla back to 185 within 256 frames after the writes stopped.

## Jobs, ski days, parking, stats
- MountainJobs `SignJobsOf`: a merged building in its area carries `MergeRules.CarriedJobs` (tier jobs less its own WorkplaceData) to the
  area's carrier; halved off season like our lodges; 0 while switched off.
- SkiDay: `LodgeInfo.Merged` -> `NoRoadNeeded` 1 (holds skiers, never a claim target) and `LeisureTagged` true (no guest destination,
  so it needs no LeisureProvider).
- Parking: `IsRoadLodge` false (no arrivals, no car-space row).
- Stats: a merged building in an area joins its sign's lodge list (counts, the ladder through `LodgeIdentity`); its own staff, upkeep
  and tourism stay the city's (skipped in the jobs and building cost sums). The area's building cost adds its monthly merge fee.
- Tourism share (P2.5): the area's share adds only the part the area wrote, `MergeRules.ShareTerm(P)` = P^2 / 10^4 (P from
  `SkiResortAttractivenessSystem.TryGetMergedShare`, this recompute's write), while the provider exists; its own V^2 stays the city's.
  Since a^2 = V^2 + P^2 within rounding (MA4), the share is P^2 / sum(a^2) (MA6, MA7). The "provider exists" test reads the
  AttractivenessProvider component, not `m_ProviderOf`: WalkProviders fills that map before CollectLodges adds the merged buildings,
  so the first build never counted them (hub showed "-"; field test 2026-11-06, fixed in d7e91e9).

## Money
- Merge: `MergeRules.MergeFee` once, at once, `PlayerMoney.Subtract` (refused when the city cannot pay; unlimited money reads 2e9).
  A tier change charges `TierChangeCharge` (the difference to what was paid, never a refund). Unmerge refunds nothing.
- Monthly: `MergeRules.MonthlyFee` per billed building (`MergeRules.Billed`: counted and switched on), accrued per elapsed frame every
  1,024 frames like SkiRunUpkeepSystem, with a fractional carry. No ledger line: like our lodges' upkeep it is a city cost.
  `Merge [fee]` logs the rate on each change and every 64th debit, and "billing stops" when no building is billed any more
  (field test 2026-11-06: rate 4000, debited 15 for 1024 frames of a 262,144-frame month).

## Copies and asset changes
- Serial guard (every pass): two buildings with one serial (a copy that kept the component) - the later entity is untagged. Move It
  0.5.16 has no clone (P0); the guard stays for any other copier.
- A lot that no longer fits the tier (a prefab swap) lowers the tier to the best one below that fits (`BestAllowed`, `FlagTierLowered`).
- Without the mod the game drops the tag at load ("Not serializable type", P0) and the building is plain vanilla.
- Delete all (Options): every merged building is unmerged; the buildings stay.

## Overlay (P2.6, contract 36; SkiMergeOverlaySystem)
- Armed from the Overview's Merged buildings fold (`armMergeOverlay`); the fold is drawn in every area view so the chip is reachable with
  no merged building yet. `mergeOverlay` {armed, allowed, tooSmall} drives the chip and its tooltip counts.
- Tint: `MergeRules.TintOf` over the card's own classification (`ReadFacts`, `Classify`, `AllowedMask`): green = a tier fits, gray = the
  kind qualifies but no tier fits (AllowedMask 0), nothing for a wrong kind, a transient state, or an already merged building (the fold
  lists those). Near = within `SkiDayRules.SlopesideMaxM` of any area's lift station or run outline: the attractiveness system publishes
  that geometry at the end of `PlaceMerged` (`PublishSlopeside`, revision only on a real change) and `NearAnySlopeside` is `Slopeside` over
  every area.
- Drawing: four projected `OverlayRenderSystem.Buffer.DrawLine` segments per lot (`BuildingUtils.CalculateCorners`, BuildingUtils.cs:72-86;
  GetBuffer + deps.Complete on the main thread, OverlayRenderSystem.cs:603-634), anchored before OverlayRenderSystem at Rendering. No game
  component is written. Rejected: vanilla `Highlighted` (Game.Tools/Highlighted.cs, read by BatchInstanceSystem.cs:425): one color and a
  structural change per building, add and remove.
- The list is built when the mode arms, when the slopeside revision moves, when the building count changes (polled every 60 frames) or
  after an unmerge (`MergeRules.OverlayRebuild`); a frame only draws the cached corners. One `Merge [cost] overlay (reason)` line per build
  (ms, scanned, near, green, gray; WARN over 20 ms). A building moved without a count change keeps its old outline until the next build.
- Disarms (`MergeRules.NextArmed`): the chip again, Escape (read from the keyboard: the default tool's cancel action is the right mouse
  button, DefaultToolSystem.cs:741; a gamepad's B button disarms the same way), a merge or tier change (`SkiMergeSystem.Merge`), a load, and a
  city with no Welcome Sign. Each logs `Merge [overlay] armed` / `disarmed (reason)`.
- A click is vanilla selection; the merge card follows the selection as before. Tests: BuildingBatch MO1-MO15.

## Hub (contract 35)
`mergeCard` (always observed, like `busCard`) and the triggers `mergeBuilding (entity, tier)` / `unmergeBuilding (entity)`;
`mergedLodges` for the Overview's "Merged buildings" fold. The card shows for a merged building, or one that could be merged (a lot too
small included, to say why), while the city has a sign.
