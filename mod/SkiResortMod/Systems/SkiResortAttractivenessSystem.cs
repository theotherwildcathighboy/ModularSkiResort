// SkiResortAttractivenessSystem — attraction model v2 (Round E1, decisions D1-D12; docs/research/econ-round/
// spec/ch1-model.md §6-§14, as bound by docs/research/econ-round/e1-spec.md): ONE attraction score per ski
// area (one Welcome Sign), computed by the pure SkiResortScore from the area's own runs, lifts, active lodges
// and the sign's efficiency, and written onto its active lodges so the game's tourism sum sees that score.
// Rewritten 2026-10-04 by stream S1 (Round E1). The F6 version (one score per lodge from a 1 km circle) is
// kept byte for byte at docs/research/econ-round/build/pre-e1/SkiResortAttractivenessSystem.cs.txt (D1's
// reverse path).
//
// ─────────────────────────────────────────────────────────────────────────────────────────
// WRITE PATH (the critical engineering — re-verified against fullsrc this round)
//
// Vanilla Game.Simulation.AttractionSystem (interval 16, GameSimulation) unconditionally
// rewrites AttractivenessProvider for every building in its per-tick UpdateFrame slice:
//
//     query All { AttractivenessProvider RW, PrefabRef RO, UpdateFrame } None { Destroyed, Deleted, Temp }
//     ...
//     AttractionData data = default;                         // prefab-side input
//     if (m_AttractionDatas.HasComponent(prefab)) data = m_AttractionDatas[prefab];
//     ...
//     nativeArray2[i] = new AttractivenessProvider { m_Attractiveness = Mathf.RoundToInt(num) };
//
// Two viable anchors were on the table (code-mod.md §1.3):
//   (a) write the PREFAB's AttractionData (input side) — rejected: prefab data is shared
//       by every placed instance, so two lodges could never score differently;
//   (b) anchored overwrite — CHOSEN: this system is registered
//       UpdateAfter<..., AttractionSystem>(GameSimulation) with the same interval 16
//       (anchored systems inherit the anchor's offset — verified in Game.UpdateSystem),
//       so each of our writes lands in the same tick as vanilla's, immediately after,
//       and simply wins. We write ALL cached lodges every tick rather than mirroring the
//       slice bookkeeping: vanilla only touches the current slice, our post-write is
//       idempotent, and lodge counts are tiny — the code-mod.md fallback, promoted to
//       the design because it removes the shared-component bookkeeping entirely.
//
// WHERE THE SCORE LIVES (D2, D3 as built: ch1 §1.1, e1-spec DV1). The lodge is a ServiceUpgrade:
// Attraction.GetArchetypeComponents skips upgrade prefabs (Game.Prefabs/Attraction.cs:22-28, the test at
// :24) and GetUpgradeComponents puts AttractivenessProvider on the OWNER sign when m_Attractiveness > 0
// (:30-36; ServiceUpgradeSystem.cs:138-157). Since Round E1 the sign prefab has no Attraction and the lodge
// prefab's Attraction is 0, so no sign gets a provider and vanilla writes nothing that could stack (the old
// (20·N)² exploit). The model's lodge base (20, +4 per extra lodge, at most +12; since Catalog Phase B the ladder by
// tier, extras at most +24) lives in SkiResortScore.
// Each ACTIVE member lodge carries PerLodge = round(A / √N) (added on first contact, then written every
// tick after vanilla), so the game's Σ a² sees N·PerLodge² ≈ A² (exact up to rounding, ch1 M2). A lodge
// that is switched off or listed by no live sign carries NO provider (removed once, ch1 §7.3): an upgrade
// has none in vanilla (Attraction.cs:24), and a provider at 0 would still make it a tourist target
// candidate (CitizenPathfindSetup.cs:836-853). A sign that still carries a provider (a save from before E1,
// or another mod) is held at 0 after the game's write each tick (ch1 §7.4; not removed).
//
// ─────────────────────────────────────────────────────────────────────────────────────────
// SCORE MODEL (v2, ch1 §2.1 — every constant and the formula in SkiResortScore; this system only gathers
// the inputs, D10: the hub reads this system's Result and never recomputes)
//
//   per member run v = band × size × type × served × open; counted ⇔ v > 0 (I1)
//   S = (Σ counted v, ascending + λ × 6 × complete-and-powered lifts) × variety × vertical
//   A = N ≥ 1 ? clamp(round((lodgeBase(N) × μ + K × √S) × sign efficiency), 0, 200) : 0
//   summer (no counted run, I2): μ 0.5, λ 0.25
//   Catalog Phase B: the lift term is per type (value and λ from the row), the lodge term is the ladder's applied value,
//   S also gains the school and a patrolled run's factors (header CATALOG PHASE B; SkiResortScore's header).
//
// MEMBERSHIP (ch1 §6; computed here, independent of the hub — the score works with the hub never opened):
//   * Ski area = a live Welcome Sign from one chunk walk of the PARK query {Building, Game.Buildings.Park,
//     PrefabRef} None {Temp, Deleted} (Park is on the sign and on every lodge, Park.cs:26-41, so the walk sees
//     parks, signs and lodges only; no Transform since the E1 fixer pass 2, header COST). A Destroyed sign
//     stays; its efficiency reads 0.
//   * Lodge = an InstalledUpgrade entry of the sign whose PrefabRef is the lodge prefab and whose building is
//     in the walk; ACTIVE ⇔ !BuildingUtils.CheckOption(entry, BuildingOption.Inactive) (vanilla mirrors the
//     lodge's option into the entry, BuildingPoliciesSystem.cs:100-113, and uses the same test,
//     UpgradeUtils.cs:24). A lodge building in the walk listed by no sign is an ORPHAN.
//   * Run = an area of the BOUND-RUN query {Area, Geometry, PrefabRef, SkiRunBinding} None {Temp, Deleted}
//     whose binding's m_Gate is one of this recompute's signs and whose prefab has an ownership kind
//     (SkiRunOwnershipSystem.KindOf ≥ 0: groomed, snowmaking, night, natural). Inputs: the catalog's
//     TryGetRating (a run rated earlier this load and being re-rated keeps its last band, I4; never rated →
//     Pending), Geometry.m_SurfaceArea (plan m²), the snow chapter's GetRunSnow(run, kind, node bottom, node
//     top) — Geometry.m_Bounds' y range is the node height range (GeometrySystem.cs:499-576; e1-spec R13) —
//     with Multiplier as the open factor (R3; Source None → 0, counted as "open unknown"), and the served
//     rule (R2): CopyServeLifts + LiftServedRule.Assign over every run's RunFacts.TopNode, once per
//     recompute; census not armed → every run served (fail open, one line per load).
//   * Lift = a census row (CopyRows; the same row order as CopyServeLifts) whose base AND top stations exist
//     and are not Deleted (State Complete, or Open = a section missing). Its area = SkiResortIndex.TryResolve(
//     base station xz, TetherMeters) over this recompute's signs (the stats system's lift rule). Complete =
//     census Complete; Powered = LiftServedRule.IsPowered(the row's ReadLiftPower via CopyServeLifts) —
//     the one power predicate (R1). Unfinished lines and lone stations carry no value and are skipped.
//   * Efficiency = SkiResortScore.SignEfficiency over the sign's Efficiency buffer (BuildingUtils.cs:121-133
//     re-done; BuildingUtils.GetEfficiency(DynamicBuffer) does not compile on net48, CS7069); the factors
//     below 1, worst first, are kept for the hub (CopyEfficiencyFactors).
//
// FIRST WRITE AFTER A LOAD (D12, ch1 §9, e1-spec R4). Nothing is serialized. At load every lodge's saved
// provider value is read (m_HeldValues). An area's first write waits until (no member run pending, or the
// catalog's rating service Failed) AND SkiSnowInjectionSystem.SnowSettled; while it waits, each active lodge
// that carried a saved value keeps it (written every tick, vanilla would rewrite it from the prefab, now 0),
// and the snapshot says WriteHeld. The wait is capped at RatingHoldMaxTicks = 1024 ticks (M1: one WARN,
// then unrated runs count 0). A new area (sign placed later) follows the same rule.
//
// CADENCE (ch1 §10; F6 D1's watcher, widened). This system never sees a Created/Updated/Deleted tag
// (Cleanup strips them before GameSimulation runs — the chain in SkiResortAttractivenessWatchSystem's
// header). One pending flag, two kinds of request:
//   * STRUCTURAL (next tick): the watcher (ModificationEnd, every frame — a lodge, lift station, Welcome Sign
//     or run of ours Created / Updated / Deleted), a game load, and on this system's tick: SkiRunOwnershipSystem
//     .BindGeneration (bindings raise no tag), LiftCensusSystem.Revision, the settings (K, backcountry,
//     gating), the rating hold expiring;
//   * SOFT (at most every MinSoftGapTicks = 16 ticks = 256 frames): SkiRunCatalogSystem.RatingRevision,
//     SkiSnowInjectionSystem.RunSnowGeneration, the SnowSettled false → true edge while an area is held, the
//     STATE POLL every 16 ticks (one FNV-1a hash of each sign's efficiency percent and its lodges' on/off
//     bits, plus LiftCensusSystem.ComputePowerSignature — power changes raise no event), and the backstop
//     every PeriodicRefreshTicks = 256 ticks (4,096 frames).
// A structural request upgrades a pending soft one. Counters are compared on the tick (they persist), never
// per frame. The recompute runs on the 16-frame tick in GameSimulation, so never while paused.
//
// GENERATION (e1-spec I-14; read by the hub's stats and the guest counter): ++ per load, per recompute that
// changed any area view, run view, lift view, efficiency factor list or written value, and when
// WriteProviders drops a deleted lodge. Results are bit-exact in any chunk order (SkiResortScore sums the
// run values ascending), so a chunk reorder does not move it.
//
// E1 SNAPSHOT (ch1 §12; e1-spec I-14, R5) — main-thread plain reads of the last recompute (frozen while
// paused): TryGetAreaScore (sign), TryGetRunScore (member run), TryGetLiftScore (census row Id, Top),
// CopyEfficiencyFactors (sign), Generation, IsLodgePrefab. Double-buffered (m_Cur / m_Next, swapped at the
// end of a recompute), allocation-free once the pools have grown. R8: the F6 accessors (LodgeBreakdown,
// TryGetBreakdown, CopyBreakdowns, RunCoverNow) are deleted, no shims.
//
// LOGS (ch1 §13, exact texts; numbers in invariant culture): the [arm] line per load; one area line when A,
// a count, a flag or WriteHeld changed, folded per area through a LogWindow(10 s) (slots A / runs / efficiency;
// trailing summaries flushed on the tick, in OnGamePreload and in OnDestroy); [first-write] once per area per
// load (a WARN when the 1,024-tick cap released it); provider added (once per lodge per load) / removed (per
// removal); a sign carrying a provider (once per sign per load); [settings] per change; the first trigger of
// each reason per load ([watch]; the rest counted for the DUMP); [cost] after 32 recomputes and one WARN per game
// day (re-armed at midday, Round M2 D8) for a recompute over 2 ms, with the game clock (the load's first recompute
// is kept apart: printed on its own, E1 fixer); Tourism Overhaul at load and per damped lodge (once per lodge per
// load); the one-shot "!!!". Not in the chapter's table (S1 deviation, docs/research/econ-round/build/notes-S1.md):
// the one fail-open line per load when the lift census is not armed.
// Round M2 (D8, 2026-10-10, builder B3; docs/research/round-m2/understand/measure-core.md 2.5): the [cost] WARN
// re-arms at the first recompute at or after 12:00 of a day other than the one it fired on
// (SkiDayRules.MiddayRearmDue / DoneDayOf, the M1 [cost] rule); the WARN and the DUMP's cost max print "YYYY-MM
// HH:MM" from TimeSystem (field test 10 run 2b: the WARN fired at 2.83 ms, the max later reached 3.159 ms with no
// WARN and no clock). Month and clock come from SkiMoneyRules.MonthKey / YearOfKey / MonthOfKey and
// SkiDayTiming.MinuteOfDay through the private MonthClockText.
//
// TOURISM OVERHAUL (ch1 §7.6): presence re-checked at game load (Mod.RefreshTourismOverhaulDetection). D27
// brings visitors on site, so TO's crowding / park gate can damp our lodges: when TO is present, a lodge whose
// provider reads below written − max(1, written / 10) on 16 consecutive ticks is logged once and counted in
// the DUMP. No behaviour change; nothing to fold in.
//
// COST (ch1 §14): nothing per frame (the watcher keeps its two IsEmptyIgnoreFilter checks). Per 16-frame tick:
// five int compares, one bool, three settings reads, the anchored writes (+ one provider read per lodge with TO).
// Per poll (256 frames): one Efficiency and one InstalledUpgrade buffer read per sign, one consumer read per
// complete lift. Per recompute: the park walk, the bound-run walk, three lookups per run, CopyServeLifts +
// Assign, Compute per area. Measured with a Stopwatch around Recompute and PollState: [cost] line, DUMP.
// SYNC POINTS (E1 fixer pass 2, e1-spec §8.10 G6; the fixer F7 / F17 class). The park walk syncs only
// PrefabRef's writers (EntityManager.CompleteDependencyBeforeRO, ParkedVehiclesSystem.cs:1732); the query
// has no Transform. Reading a Transform through EntityManager completes every job that writes Transform —
// CarMoveSystem, HumanMoveSystem and the other move systems, which run earlier in the same GameSimulation
// pass (SystemOrder.cs 1.6.2 :338-356, AttractionSystem :552). So the sign positions (SkiResortIndex, the
// lift rule) and the base-station positions (m_BaseXz) are read only when DUE: on a structural recompute
// (load, binding, census, settings, hold, the watcher's placements and moves — a sign or station moved by
// Move It is tagged Updated, and a moved station also moves the census Revision) and on the 4,096-frame
// backstop. Soft recomputes (ratings, snow, the state poll; up to every 256 frames in a snow season) reuse
// them and pay no Transform sync. A walk whose signs no longer match the cached index reads anyway (a missed
// tag; counted, DUMP `fallback read(s)`). Before E1 the walk read Transform chunks with no sync (a race); the
// stage-2 build synced on every recompute.
// ROUND S1 "Ski days" (stream C, 2026-10-05; ski-days spec ch3 §2.1, §5 F4; s1-spec §2.6). Additive accessors on
// the snapshot: CopySigns, CopyActiveLodges, CopySiteMembers and LodgeSetRevision (++ at a load, when a
// recompute publishes other (sign, active lodges) lists — LodgeSetChanged, per sign — and when WriteProviders
// drops a deleted lodge); consumers SkiDaySystem, SkiLodgeParkingSystem, SkiLiftTicketSystem's gate watch.
// F4: an efficiency-only area line is written only when A moves by 2 or the area was quiet for 60 s (LogArea).
// CATALOG PHASE B (2026-10-11, economy group; docs/research/art/assets-2026-10/code-round-2/round-2-contract.md §7.4,
// skeptic MUST 3 / SHOULD "no per-recompute allocation"). The sign's ServiceUpgrades are read by catalog row:
//   * LODGES (every SkiCatalog.Lodges row): slot.Active / Inactive as before, plus slot.ActiveInfo (seam S5 LodgeInfo:
//     the lodge's SkiCatalog.Lodges index, SummitServed) → AreaInput.Lodges, the ladder (SkiResortScore R11).
//     SummitServed (D11) = LiftServedRule.SummitServed(lodge xz, the area's complete powered lifts, Ladder.Summit-
//     ServedRadiusM 300) — evaluated only for rows whose summer share rises when served (μ summit > μ: hut, summit);
//     false for the others, which it cannot change. Every active lodge counts: LodgeInfo.Counted and the "second luxury
//     lodge not counted" Info line (the one-luxury-lodge cap, D3) were removed on 2026-10-11 (Richard: no count limit).
//   * THE SKI SCHOOL (SkiCatalog.Schools): an active entry → slot.SchoolActive → AreaInput.SchoolActive (R13).
//   * PATROL HUTS (SkiCatalog.Patrols): active entries → slot.PatrolEntities; each member run's RunInput.Patrolled =
//     PatrolRule.Patrolled(its top node xz, the area's patrol xz) (R13, D12).
//   The poll hash mixes the school and patrol entries' (entity, active) bits too (an FG-only city hashes as before).
//   Lodge and patrol POSITIONS are read only when due (structural or backstop), cached like m_BaseXz (header SYNC
//   POINTS): no new Transform sync on a soft recompute (a miss there is a counted fallback read).
//   * LIFTS: LiftInput.TypeIndex = the census row's Type (seam S1; Mixed rows are Unfinished and never reach here).
//   * IsWatchedBuildingPrefab: lodge ∪ school ∪ patrol hut (IsUpgradeBuilding) ∪ lift station ∪ Welcome Sign.
//   * Generation also moves when a published LodgeInfo list changes (SummitServed flips without a score
//     change, e.g. in winter), so the hub's "summit lodge not lift-served" row follows.
// No serialized state: a lodge's tier IS its PrefabRef; everything here is recomputed from live components.
// ─────────────────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Buildings;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using SkiResortMod.Settings;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static System.FormattableString;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiResortAttractivenessSystem : GameSystemBase
    {
        // ── Tuning — cadence and logging only (the model's constants live in SkiResortScore, ch1 §2.2) ──
        private static class Tuning
        {
            public const int PeriodicRefreshTicks = 256;   // backstop: 256 × 16 = 4,096 frames
            public const int StatePollTicks = 16;          // 256 frames (§10.3)
            public const int MinSoftGapTicks = 16;         // a soft request runs at most every 256 frames
            public const int RatingHoldMaxTicks = 1024;    // M1: 16,384 frames ≈ 4.6 min at 1×
            public const float AreaLogWindowS = 10f;       // per-area LogWindow (§13)
            public const int CostReportRecomputes = 32;    // the [cost] line, once per load
            public const double CostWarnMs = 2.0;          // one WARN per load above this
            public const int DampedTicksToLog = 16;        // TO damping: consecutive ticks before the line
            // Round S1 F4 (ski-days spec ch3 §5): an efficiency-only change writes its area line only when A moves
            // by this much, or when the area wrote no line for EfficiencyLineQuietS of wall clock.
            public const int EfficiencyLineMinDeltaA = 2;
            public const float EfficiencyLineQuietS = 60f;
        }

        // Recompute reasons (§10.1 table, exact texts). The watcher's three live in the watch system.
        internal const string ReasonLoad = "game load";
        internal const string ReasonTimer = "periodic refresh";
        internal const string ReasonBinding = "run binding changed";
        internal const string ReasonCensus = "lift census changed";
        internal const string ReasonSettings = "settings changed";
        internal const string ReasonHold = "rating hold expired";
        internal const string ReasonRating = "run rating changed";
        internal const string ReasonSnow = "run open state changed";
        internal const string ReasonSnowSettled = "snow settled";
        internal const string ReasonState = "sign efficiency, lodge on/off or lift power changed";

        // Prefab set — tolerant resolution (not yet imported ⇒ system stays inert). The run prefab sets are
        // the ownership system's (one registry, natural included; ch2 §13 item 1).
        // Catalog Phase A (F5 / F10; docs/research/art/assets-2026-10/catalog-a/phase-a-contract.md R1; was :213-215
        // LodgePrefabNames { "SkiAFrameDayLodge" }, :218 + :677 m_PowerBridgeSystem (read only at :480 for the station
        // set), :226 m_LodgePrefabs, :230 + :718 m_PrefabScanQuery, :232 m_PrefabsResolved and the :2037-2091
        // ResolvePrefabs / ResolveSet copy): the lodges are SkiCatalog.Lodges, the stations SkiCatalog.Stations, both
        // resolved per load by SkiPrefabCatalogSystem (was once per APP RUN here: contract §8 N1 — a lodge imported
        // mid-session or a prefab replaced is now followed at the next load / frame).
        private SkiPrefabCatalogSystem m_PrefabCatalog;
        private SkiRunOwnershipSystem m_Ownership;       // sign set, run kinds, BindGeneration
        private SkiRunCatalogSystem m_Catalog;           // ratings, run tops, rating service
        private LiftCensusSystem m_Census;               // rows, serve lifts, power
        private SkiSnowInjectionSystem m_Injection;      // run open state, SnowSettled
        private SnowLineSystem m_SnowLine;               // the one snow gating read
        private Game.Simulation.TimeSystem m_Time;       // Round M2 (D8): the [cost] WARN's midday re-arm and clocks

        private EntityQuery m_ParkQuery;       // signs, lodges (and other parks)
        private EntityQuery m_RunQuery;        // our bound runs

        private bool m_Disabled;
        private bool m_ErrorLogged;

        // ── the snapshot (double-buffered; header E1 SNAPSHOT) ─────────────────────────────────────

        /// <summary>One ski area as one recompute built it.</summary>
        private sealed class AreaSlot
        {
            public Entity Sign;
            public readonly List<SkiResortScore.RunInput> Runs = new List<SkiResortScore.RunInput>(16);
            public readonly List<Entity> RunEntities = new List<Entity>(16);
            public readonly List<SkiResortScore.RunPart> Parts = new List<SkiResortScore.RunPart>(16);
            public readonly List<SkiResortScore.LiftInput> Lifts = new List<SkiResortScore.LiftInput>(8);
            public readonly List<Entity> Active = new List<Entity>(4);
            public readonly List<Entity> Inactive = new List<Entity>(2);
            public readonly List<Efficiency> Factors = new List<Efficiency>(8);   // < 1, worst first
            public float EfficiencyValue;
            public int EfficiencyPct;
            public int OpenUnknown;
            public SkiResortScore.Result Result;
            public bool Held;
            // Catalog Phase B (header CATALOG PHASE B). ActiveInfo is parallel to Active (same order); Lodges is the
            // score's AreaInput.Lodges built from it; ServeLifts = indices into m_ServeLifts of this area's lifts.
            public readonly List<LodgeInfo> ActiveInfo = new List<LodgeInfo>(4);
            public readonly List<SkiResortScore.LodgeInput> Lodges = new List<SkiResortScore.LodgeInput>(4);
            public readonly List<Entity> PatrolEntities = new List<Entity>(2);
            public readonly List<float2> PatrolXz = new List<float2>(2);
            public readonly List<int> ServeLifts = new List<int>(8);
            public bool SchoolActive;
            public int Schools;   // school entries listed (active or not; DUMP)

            public void Reset(Entity sign)
            {
                Sign = sign;
                Runs.Clear();
                RunEntities.Clear();
                Parts.Clear();
                Lifts.Clear();
                Active.Clear();
                Inactive.Clear();
                Factors.Clear();
                EfficiencyValue = 1f;
                EfficiencyPct = 100;
                OpenUnknown = 0;
                Result = default;
                Held = false;
                ActiveInfo.Clear();     // Catalog Phase B
                Lodges.Clear();
                PatrolEntities.Clear();
                PatrolXz.Clear();
                ServeLifts.Clear();
                SchoolActive = false;
                Schools = 0;
            }
        }

        private struct LiftKey : IEquatable<LiftKey>
        {
            public Entity Id, Top;

            public LiftKey(Entity id, Entity top)
            {
                Id = id;
                Top = top;
            }

            public bool Equals(LiftKey other)
            {
                return Id == other.Id && Top == other.Top;
            }

            public override bool Equals(object obj)
            {
                return obj is LiftKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return unchecked(Id.GetHashCode() * 397 ^ Top.GetHashCode());
            }
        }

        /// <summary>Everything one recompute publishes. m_Cur is read; m_Next is filled, then swapped.</summary>
        private sealed class Snapshot
        {
            public readonly List<AreaSlot> Slots = new List<AreaSlot>(4);
            public int SlotCount;
            public readonly Dictionary<Entity, int> SlotOfSign = new Dictionary<Entity, int>();
            public readonly Dictionary<Entity, RunScoreView> Runs = new Dictionary<Entity, RunScoreView>();
            public readonly Dictionary<LiftKey, LiftScoreView> Lifts = new Dictionary<LiftKey, LiftScoreView>();
            public readonly Dictionary<Entity, int> WriteValues = new Dictionary<Entity, int>();   // lodge → value written every tick
            public readonly Dictionary<Entity, Entity> LodgeArea = new Dictionary<Entity, Entity>(); // written lodge → its sign
            public int LiveLodges, Chords, LiftCount;

            public void Clear()
            {
                SlotCount = 0;
                SlotOfSign.Clear();
                Runs.Clear();
                Lifts.Clear();
                WriteValues.Clear();
                LodgeArea.Clear();
                LiveLodges = Chords = LiftCount = 0;
            }

            public AreaSlot AddSlot(Entity sign)
            {
                if (SlotCount == Slots.Count)
                    Slots.Add(new AreaSlot());
                AreaSlot slot = Slots[SlotCount];
                slot.Reset(sign);
                SlotOfSign[sign] = SlotCount;
                SlotCount++;
                return slot;
            }

            public bool TryGetSlot(Entity sign, out AreaSlot slot)
            {
                if (SlotOfSign.TryGetValue(sign, out int index))
                {
                    slot = Slots[index];
                    return true;
                }
                slot = null;
                return false;
            }
        }

        private Snapshot m_Cur = new Snapshot();
        private Snapshot m_Next = new Snapshot();
        private int m_Generation;

        // ── per-load state ─────────────────────────────────────────────────────────────────────────

        /// <summary>First-write bookkeeping per area (ch1 §9), kept across recomputes for one load.</summary>
        private sealed class AreaState
        {
            public bool FirstWriteDone;
            public int HoldTicks;
            public bool HoldWarned;
        }

        private struct LastRating
        {
            public byte State;
            public sbyte Band;
            public float TopM, BottomM;
        }

        private struct DropEntry
        {
            public Entity Lodge;
            public bool SwitchedOff;   // else: on no live Welcome Sign
        }

        private struct RunRef
        {
            public int Slot, Index;
        }

        /// <summary>Per area: the last logged key (A, counts, flags, held) and the fold window (§13). Round S1 F4:
        /// the logged key without A and PerLodge (StructKey), the logged A / runs value / efficiency percent, and
        /// the last skipped key (a skip is counted once per change, not per recompute: refutation R12).</summary>
        private sealed class AreaLog
        {
            public readonly LogWindow Window = new LogWindow(Tuning.AreaLogWindowS);
            public bool HasKey;
            public ulong Key;
            public ulong StructKey;
            public ulong SkippedKey;
            public int LoggedA;
            public double LoggedRunsValue;
            public int LoggedEffPct;
        }

        private readonly Dictionary<Entity, AreaState> m_AreaStates = new Dictionary<Entity, AreaState>();
        private readonly Dictionary<Entity, int> m_HeldValues = new Dictionary<Entity, int>();        // lodge → provider value at load (§9)
        private readonly Dictionary<Entity, LastRating> m_LastRatings = new Dictionary<Entity, LastRating>(); // run → rating seen this load (I4)
        private readonly List<DropEntry> m_DropProvider = new List<DropEntry>();                     // consumed once by WriteProviders
        private readonly List<Entity> m_ZeroSigns = new List<Entity>();                              // signs carrying a provider: held at 0
        private readonly Dictionary<Entity, AreaLog> m_AreaLogs = new Dictionary<Entity, AreaLog>();
        private readonly HashSet<Entity> m_ProviderAddedLogged = new HashSet<Entity>();
        private readonly HashSet<Entity> m_ZeroSignLogged = new HashSet<Entity>();
        private readonly Dictionary<Entity, int> m_DampedTicks = new Dictionary<Entity, int>();
        private readonly HashSet<Entity> m_DampedLogged = new HashSet<Entity>();
        private bool m_CensusOpenLogged;
        private int m_HeldCount;   // areas held at the last recompute
        private long m_EfficiencyLinesSkipped;   // Round S1 F4: efficiency-only area lines not written this load (DUMP)

        // ── recompute scratch (reused; no allocation once grown) ───────────────────────────────────
        // m_SignIndex and m_BaseXz are CACHES since the E1 fixer pass 2 (header COST): filled when positions
        // are due (a structural recompute, the backstop), reused by soft recomputes.
        private readonly SkiResortIndex m_SignIndex = new SkiResortIndex();
        private readonly Dictionary<Entity, float2> m_BaseXz = new Dictionary<Entity, float2>();
        // Catalog Phase B (header CATALOG PHASE B): the same cache rule for the summit-served lodges and the patrol huts.
        private readonly Dictionary<Entity, float2> m_LodgeXz = new Dictionary<Entity, float2>();
        private readonly Dictionary<Entity, float2> m_PatrolXz = new Dictionary<Entity, float2>();
        private readonly List<LiftServedRule.Lift> m_AreaServeLifts = new List<LiftServedRule.Lift>(8);   // one area's lifts (SummitServed)
        private bool m_PositionsReadThisRecompute;   // CollectAreas' verdict, for the lodge / patrol reads after it
        private bool m_PositionsDue = true;      // the backstop asks for a position read (header COST)
        private int m_PositionReads, m_PositionFallbacks;   // this load (DUMP)
        private readonly HashSet<Entity> m_LiveLodges = new HashSet<Entity>();
        private readonly HashSet<Entity> m_LodgeClaimed = new HashSet<Entity>();
        private readonly List<float3> m_RunTops = new List<float3>(32);
        private readonly List<RunRef> m_RunRefs = new List<RunRef>(32);
        private readonly List<LiftServedRule.Lift> m_ServeLifts = new List<LiftServedRule.Lift>(8);
        private readonly List<LiftServedRule.Chord> m_Chords = new List<LiftServedRule.Chord>(64);
        private readonly List<int> m_ServingLift = new List<int>(32);
        private readonly List<float> m_ServingDistance = new List<float>(32);
        private readonly List<int> m_ServedCount = new List<int>(8);
        private readonly List<LiftCensus.Row> m_CensusRows = new List<LiftCensus.Row>(8);
        private readonly List<Entity> m_ScratchEntities = new List<Entity>(8);
        private float[] m_FactorValues = new float[32];

        // ── triggers (header CADENCE) ──────────────────────────────────────────────────────────────
        private bool m_StructuralPending;
        private bool m_SoftPending;
        private string m_PendingReason;
        private int m_Tick;
        private int m_LastRecomputeTick;
        private int m_TicksUntilRefresh = Tuning.PeriodicRefreshTicks;
        private bool m_CountersPrimed;
        private int m_LastBindGeneration, m_LastCensusRevision, m_LastRatingRevision, m_LastSnowGeneration;
        private int m_LastK, m_LastBackcountry;
        private bool m_LastGating;
        private bool m_LastSnowSettled;
        private ulong m_StateHash;

        // ── counts and cost (DUMP, [cost]) ─────────────────────────────────────────────────────────
        private string m_LastReason = "-";
        private int m_Recomputes, m_Requests;
        private readonly Dictionary<string, int> m_RecomputesByReason = new Dictionary<string, int>();
        private readonly Dictionary<string, int> m_RequestsByReason = new Dictionary<string, int>();
        private int m_CostCount, m_PollCount;
        private double m_CostSumMs, m_CostMaxMs, m_PollSumMs;
        private double m_FirstRecomputeMs = -1.0;   // the load's first recompute, kept out of mean / max / WARN (−1 = none yet)
        private bool m_CostWarned;
        // Round M2 (D8): the WARN's day (SkiDayRules.DoneDayOf: today when it fired at or after 12:00, else none, so it
        // re-arms today at noon) and the clock of the cost max (month key SkiDayRules.NoMonth / minute −1 before any).
        private int m_CostWarnDay = int.MinValue;
        private int m_CostMaxMonth = SkiDayRules.NoMonth, m_CostMaxMinute = -1;

        // ── public / internal surface ──────────────────────────────────────────────────────────────

        /// <summary>++ per load, per recompute that changed any view, factor list or written value, and when
        /// WriteProviders drops a deleted lodge (header GENERATION). Plain field read; never reset (readers
        /// compare for equality).</summary>
        public int Generation => m_Generation;

        /// <summary>True for a lodge BuildingPrefab entity (every SkiCatalog.Lodges row; SkiAFrameDayLodge today).
        /// Catalog Phase A (D6: signature kept for SkiResortStatsSystem, SkiGuestCounterSystem and the hub; was
        /// m_LodgePrefabs.Contains, :462-465): the prefab catalog's answer.</summary>
        internal bool IsLodgePrefab(Entity prefab)
        {
            return m_PrefabCatalog.IsLodge(prefab);
        }

        /// <summary>Catalog Phase B (Phase A review finding 1): the prefab catalog's live lodge-prefab view, for a caller
        /// that tests many buildings in one scan (SkiGuestCounterSystem.RebuildMap fetches it once instead of calling
        /// IsLodgePrefab per building). Main thread; do not keep it across frames (the catalog revalidates per frame).</summary>
        internal HashSet<Entity> LodgePrefabSet => m_PrefabCatalog.LodgePrefabSet;

        /// <summary>Scoring runs this load: not error-disabled, a lodge prefab resolved (catalog) and the ownership
        /// system's prefab sets ready (sign set and run sets, Game mode).</summary>
        internal bool IsActive => !m_Disabled && m_PrefabCatalog.LodgeCount > 0 && m_Ownership.PrefabSetsReady;   // Catalog Phase A (was :469 m_LodgePrefabs.Count > 0)

        /// <summary>A STRUCTURAL recompute is already waiting for the next 16-frame tick (a pending soft one
        /// does not count, so a placement is never delayed behind it).</summary>
        internal bool RecomputePending => m_StructuralPending;

        /// <summary>A building prefab whose placement, move or removal changes a ski area: a lodge, the ski school, a
        /// patrol hut (Catalog Phase B: every ServiceUpgrade the catalog knows), a lift station (the prefab catalog's)
        /// or a Welcome Sign (§10.2; the sign stays the ownership registry's: run family, contract D5).</summary>
        internal bool IsWatchedBuildingPrefab(Entity prefab)
        {
            // Catalog Phase A (ext row 6 / F10; was :479-481 m_LodgePrefabs.Contains || PowerBridge's station set).
            // Catalog Phase B (round-2-contract §7.4; was m_PrefabCatalog.IsLodge(prefab)): lodge ∪ school ∪ patrol hut.
            return m_PrefabCatalog.IsUpgradeBuilding(prefab) ||
                   m_PrefabCatalog.IsStation(prefab) ||
                   m_Ownership.IsGatePrefab(prefab);
        }

        /// <summary>One of our run surface prefabs (all four kinds) — the ownership registry (§10.2).</summary>
        internal bool IsWatchedAreaPrefab(Entity prefab)
        {
            return m_Ownership.IsRunPrefab(prefab);
        }

        /// <summary>STRUCTURAL request: the recompute runs on the next tick (it upgrades a pending soft one;
        /// the first structural reason is kept). Counted per reason; the first of each reason per load is
        /// logged. Main thread (the watcher at ModificationEnd, and this system's tick).</summary>
        internal void RequestRecompute(string reason)
        {
            CountRequest(reason);
            if (m_StructuralPending)
                return;
            m_StructuralPending = true;
            m_SoftPending = false;
            m_PendingReason = reason;
        }

        /// <summary>SOFT request: runs once MinSoftGapTicks passed since the last recompute.</summary>
        private void RequestSoftRecompute(string reason)
        {
            CountRequest(reason);
            if (m_StructuralPending || m_SoftPending)
                return;
            m_SoftPending = true;
            m_PendingReason = reason;
        }

        private void CountRequest(string reason)
        {
            m_Requests++;
            m_RequestsByReason.TryGetValue(reason, out int n);
            m_RequestsByReason[reason] = n + 1;
            if (n == 0)
                Mod.Log.Info($"Attractiveness [watch]: trigger ({reason}) — first this load; later triggers are counted (hub DUMP)");
        }

        // ── Round E1 snapshot (econ-round spec ch1 §12; e1-spec I-14, R5). Plain reads of the last
        // recompute (frozen while paused). Main thread only.

        /// <summary>One ski area's last result (D10: the hub reads this, never recomputes).</summary>
        internal struct AreaScoreView
        {
            public Entity Sign;
            public SkiResortScore.Result Result;
            public bool WriteHeld;          // the first write after a load still waits for run ratings (§9)
            public int HoldTicks;           // ticks held so far (0 once written)
            public int EfficiencyPct;       // round(100 × Result.Efficiency)
            public int OpenUnknown;         // member runs the snow chapter had no open state for (RunSnow.Source == None)
        }

        /// <summary>One member run's inputs and part of the last recompute.</summary>
        internal struct RunScoreView { public Entity Run, Sign; public SkiResortScore.RunInput Input; public SkiResortScore.RunPart Part; }

        /// <summary>One census lift as the last recompute counted it. Powered = lift.Power != LiftPower.NoPower
        /// (what the game shows); Counted = Complete && LiftServedRule.IsPowered(lift.Power).</summary>
        internal struct LiftScoreView { public Entity Id, Base, Top, Sign; public bool Complete, Powered, Counted; }

        /// <summary>The last recompute's result for <paramref name="sign"/> (false: not a live sign at the
        /// last recompute). HoldTicks is the live count while held.</summary>
        internal bool TryGetAreaScore(Entity sign, out AreaScoreView view)
        {
            if (!m_Cur.TryGetSlot(sign, out AreaSlot slot))
            {
                view = default;
                return false;
            }
            view = new AreaScoreView
            {
                Sign = slot.Sign,
                Result = slot.Result,
                WriteHeld = slot.Held,
                HoldTicks = slot.Held && m_AreaStates.TryGetValue(slot.Sign, out AreaState state) ? state.HoldTicks : 0,
                EfficiencyPct = slot.EfficiencyPct,
                OpenUnknown = slot.OpenUnknown,
            };
            return true;
        }

        /// <summary>The last recompute's view of member run <paramref name="run"/> (false: not a member —
        /// unbound, a paid orphan, or bound to a sign that was not live).</summary>
        internal bool TryGetRunScore(Entity run, out RunScoreView view)
        {
            return m_Cur.Runs.TryGetValue(run, out view);
        }

        /// <summary>The last recompute's view of the census row (<paramref name="id"/>, <paramref name="top"/>)
        /// (e1-spec R5: two lifts can share one base station). Only lifts that belong to a ski area (both
        /// stations live, base within the tether of a live sign) have a view.</summary>
        internal bool TryGetLiftScore(Entity id, Entity top, out LiftScoreView view)
        {
            return m_Cur.Lifts.TryGetValue(new LiftKey(id, top), out view);
        }

        /// <summary>The sign's efficiency factors below 100 % from the last recompute, worst first,
        /// appended to <paramref name="into"/> after clearing it; returns the count.</summary>
        internal int CopyEfficiencyFactors(Entity sign, List<Efficiency> into)
        {
            into.Clear();
            if (m_Cur.TryGetSlot(sign, out AreaSlot slot))
            {
                for (int i = 0; i < slot.Factors.Count; i++)
                    into.Add(slot.Factors[i]);
            }
            return into.Count;
        }

        // ── Round S1 ski days (ski-days spec ch3 §2.1; s1-spec §2.6). The lodge list and the D10 inputs of the
        // last recompute, for SkiDaySystem (B), SkiLodgeParkingSystem and SkiLiftTicketSystem's gate watch (C).
        // Main thread only; plain copies (no AddRange), allocation-free once the caller's lists have grown. Same
        // recompute as TryGetAreaScore, so the lodge list and the gate inputs agree (code-map V.1 #11, V.2 #1);
        // the guest counter's target map uses the same lodge rule, so a lodge from this list is a counted target.

        private int m_LodgeSetRevision;

        /// <summary>++ when the published (sign, active lodges) lists change; at every load; when WriteProviders
        /// drops a deleted lodge (consumers re-copy).</summary>
        internal int LodgeSetRevision => m_LodgeSetRevision;

        /// <summary>False only between a load and the first recompute of an active model: the snapshot is empty
        /// then, not "no ski area". While inactive (no lodge prefab, prefab sets not ready, error-disabled) it is
        /// true, and CopySigns' 0 is the truth (fixer, review CP-4: the parking census and SkiDaySystem wait on it).</summary>
        internal bool HasSnapshot => !IsActive || m_Recomputes > 0;

        /// <summary>Live Welcome Signs of the last recompute, slot order; clears <paramref name="into"/>; returns
        /// the count (0 while inactive).</summary>
        internal int CopySigns(List<Entity> into)
        {
            into.Clear();
            if (!IsActive)
                return 0;
            for (int s = 0; s < m_Cur.SlotCount; s++)
                into.Add(m_Cur.Slots[s].Sign);
            return into.Count;
        }

        /// <summary>AreaSlot.Active of <paramref name="sign"/> (lodge prefab, in its InstalledUpgrade, in the park
        /// walk, not Inactive; first sign wins); clears <paramref name="into"/>; returns the count. NOT filtered
        /// for Destroyed (code-map V.3 #6, e1-spec §8.6 item 2: the consumers' lookups judge it).</summary>
        internal int CopyActiveLodges(Entity sign, List<Entity> into)
        {
            into.Clear();
            if (m_Cur.TryGetSlot(sign, out AreaSlot slot))
            {
                for (int i = 0; i < slot.Active.Count; i++)
                    into.Add(slot.Active[i]);
            }
            return into.Count;
        }

        /// <summary>Catalog Phase B, seam S5 (round-2-contract §7.4): one ACTIVE lodge of the last recompute with its tier.
        /// LodgeIndex = SkiCatalog.Lodges index (the lodge's PrefabRef is its tier); SummitServed = within
        /// Ladder.SummitServedRadiusM of a complete, powered lift's top stub tip of its ski area (evaluated for rows with
        /// μ summit &gt; μ; false for the others). Every active lodge counts (no luxury cap since 2026-10-11).</summary>
        internal struct LodgeInfo
        {
            public Entity Lodge;
            public short LodgeIndex;
            public bool SummitServed;
        }

        /// <summary>Seam S5: the LodgeInfo of every lodge CopyActiveLodges returns for <paramref name="sign"/>, in the
        /// same order; clears <paramref name="into"/>; returns the count (0 for an unknown sign). Main thread, plain copy
        /// (allocation-free once the caller's list has grown). CopyActiveLodges is unchanged (base and mountain lodges).</summary>
        internal int CopyActiveLodgeInfo(Entity sign, List<LodgeInfo> into)
        {
            into.Clear();
            if (m_Cur.TryGetSlot(sign, out AreaSlot slot))
            {
                for (int i = 0; i < slot.ActiveInfo.Count; i++)
                    into.Add(slot.ActiveInfo[i]);
            }
            return into.Count;
        }

        /// <summary>D10 inputs for SkiDaySystem's SiteJob (ch3 refutation R5): clears both; runs = slot.RunEntities
        /// of <paramref name="sign"/>; stations = the non-Null Base / Top of every m_Cur.Lifts value with Sign ==
        /// sign, deduplicated; returns runs.Count + liftStations.Count; 0 for an unknown sign.</summary>
        internal int CopySiteMembers(Entity sign, List<Entity> runs, List<Entity> liftStations)
        {
            runs.Clear();
            liftStations.Clear();
            if (!m_Cur.TryGetSlot(sign, out AreaSlot slot))
                return 0;
            for (int i = 0; i < slot.RunEntities.Count; i++)
                runs.Add(slot.RunEntities[i]);
            foreach (var pair in m_Cur.Lifts)
            {
                LiftScoreView lift = pair.Value;
                if (lift.Sign != sign)
                    continue;
                AddStation(liftStations, lift.Base);
                AddStation(liftStations, lift.Top);
            }
            return runs.Count + liftStations.Count;
        }

        /// <summary>Appends a non-Null station once (a handful per area: a linear Contains is cheaper than a set).</summary>
        private static void AddStation(List<Entity> into, Entity station)
        {
            if (station != Entity.Null && !into.Contains(station))
                into.Add(station);
        }

        // ── lifecycle ──────────────────────────────────────────────────────────────────────────────

        /// <summary>Mirror vanilla AttractionSystem's cadence so the anchored write lands
        /// right after vanilla's each tick (see the write-path header).</summary>
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 16;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();   // Catalog Phase A
            // Catalog Phase A: m_PowerBridgeSystem is gone (its station set was read only at :480), but the call stays
            // so PowerBridgeSystem is still CREATED here: this system is registered first (Mod.cs, before PowerBridge),
            // and creation order is load-hook subscription order (GameSystemBase.cs:27-29) — kept identical.
            World.GetOrCreateSystemManaged<PowerBridgeSystem>();
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();
            m_Catalog = World.GetOrCreateSystemManaged<SkiRunCatalogSystem>();
            m_Census = World.GetOrCreateSystemManaged<LiftCensusSystem>();
            m_Injection = World.GetOrCreateSystemManaged<SkiSnowInjectionSystem>();
            m_SnowLine = World.GetOrCreateSystemManaged<SnowLineSystem>();
            m_Time = World.GetOrCreateSystemManaged<Game.Simulation.TimeSystem>();   // Round M2 (D8)

            // §6: the park-building query (signs and lodges carry Game.Buildings.Park, Park.cs:26-41). No
            // Transform (E1 fixer pass 2, header COST): the walk reads Entity and PrefabRef only; sign and
            // base-station positions are read through EntityManager when they are due (ReadPositions).
            m_ParkQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<Game.Buildings.Park>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });
            // §6: the bound-run query (only our runs carry SkiRunBinding).
            m_RunQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<Geometry>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<SkiRunBinding>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });

            Mod.Log.Info($"{nameof(SkiResortAttractivenessSystem)} created");
        }

        /// <summary>A load (or leaving to the menu) writes the folded area lines of the city being left.
        /// Never throws: GameSystemBase.GamePreload DISABLES a system whose OnGamePreload throws
        /// (Game/GameSystemBase.cs:85-96; same in 1.6.2).</summary>
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            try
            {
                FlushAllAreaLogs();
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Attractiveness: folded area lines could not be written before the load " +
                             $"({e.GetType().Name}: {e.Message}) — non-fatal");
            }
        }

        /// <summary>Quit to desktop raises no onGamePreload, but the world's systems are destroyed while the
        /// logger still lives (LogWindow.cs header): the folded area lines are written here. Never throws.</summary>
        protected override void OnDestroy()
        {
            try
            {
                FlushAllAreaLogs();
            }
            catch (Exception)
            {
                // nothing left to report to
            }
            base.OnDestroy();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            // Every per-load map starts empty, whatever the mode (nothing of the previous city survives).
            m_Disabled = false;
            m_ErrorLogged = false;
            m_Cur.Clear();
            m_Next.Clear();
            m_Generation++;
            m_LodgeSetRevision++;          // Round S1: the lists were emptied (consumers re-copy)
            m_EfficiencyLinesSkipped = 0;  // Round S1 F4
            m_AreaStates.Clear();
            m_HeldValues.Clear();
            m_LastRatings.Clear();
            m_DropProvider.Clear();
            m_ZeroSigns.Clear();
            m_AreaLogs.Clear();   // flushed in OnGamePreload
            m_ProviderAddedLogged.Clear();
            m_ZeroSignLogged.Clear();
            m_DampedTicks.Clear();
            m_DampedLogged.Clear();
            m_CensusOpenLogged = false;
            m_HeldCount = 0;
            m_StructuralPending = m_SoftPending = false;
            m_PendingReason = null;
            m_Tick = 0;
            m_LastRecomputeTick = -Tuning.MinSoftGapTicks;
            m_TicksUntilRefresh = Tuning.PeriodicRefreshTicks;
            m_CountersPrimed = false;
            m_LastSnowSettled = false;   // §10.4: the SnowSettled edge starts from false each load
            m_StateHash = 0UL;
            m_LastReason = "-";
            m_Recomputes = m_Requests = 0;
            m_RecomputesByReason.Clear();
            m_RequestsByReason.Clear();
            m_CostCount = m_PollCount = 0;
            m_CostSumMs = m_CostMaxMs = m_PollSumMs = 0.0;
            m_FirstRecomputeMs = -1.0;
            m_CostWarned = false;
            m_CostWarnDay = int.MinValue;                                     // Round M2 (D8)
            m_CostMaxMonth = SkiDayRules.NoMonth;
            m_CostMaxMinute = -1;
            m_SignIndex.Clear();
            m_BaseXz.Clear();
            m_LodgeXz.Clear();       // Catalog Phase B
            m_PatrolXz.Clear();
            m_PositionsDue = true;   // the load's recompute is structural anyway
            m_PositionReads = m_PositionFallbacks = 0;

            if (mode != GameMode.Game && mode != GameMode.Editor)
                return;
            LogLodgePrefabs();   // Catalog Phase A (was :805 ResolvePrefabs(), once per app run): per load now
            if (mode != GameMode.Game)
                return;

            Mod.RefreshTourismOverhaulDetection(); // fixes the OnLoad-time load-order race
            ReadHeldValues();
            ReadSettings(out int k, out int backcountry, out bool gating);
            // Integers and fixed text only, or invariant culture (ArmModelText).
            // Catalog Phase B (round-2-contract §7.4; was "lodge base × mu", "lambda × lifts", "lodge base 20 +4 per extra
            // lodge (max +12)", "lift 6 (complete and powered)", "summer … mu 0.5 lambda 0.25"): the ladder, the per-type
            // lift values, the school and the patrol, every number from SkiCatalog.
            Mod.Log.Info(
                "Attractiveness [arm]: model v2 (E1) + Catalog Phase B — one score per ski area: A = clamp(round((lodge applied + K × sqrt(S)) × sign efficiency), 0, 200), " +
                "S = (runs + lifts (summer: × each type's share) + school) × variety × vertical; " + ArmModelText() + "; band 0/6/12/20/28, unrateable 3; " +
                "size 1 + 0.3 ln(area / 20000 m²) in [0.25, 2] from 10000 m², proportional below (0.792 per ha); night ×1.25; natural ×0.5/0.75/1/1; " +
                "variety ×1.15; vertical / 600 m (max ×2); summer = no run worth more than 0; " +
                $"K {k}, backcountry {backcountry} %, snow gating {(gating ? "on" : "off")}; each active lodge carries round(A / sqrt(N)); " +
                $"held saved values {m_HeldValues.Count} lodge(s); first write waits for ratings and snow (max {Tuning.RatingHoldMaxTicks} ticks); " +
                $"poll every {Tuning.StatePollTicks} ticks, backstop every {Tuning.PeriodicRefreshTicks} ticks");
            if (Mod.TourismOverhaulDetected)
                Mod.Log.Info("Attractiveness: Tourism Overhaul present — its crowding and park gate can damp the values we write " +
                             "(each damped lodge is logged once, §7.6); nothing to fold in");
            RequestRecompute(ReasonLoad);
        }

        /// <summary>§9 step 1: every lodge building's AttractivenessProvider as the save left it (the
        /// component is ISerializable, so it is deserialized before any tick).</summary>
        private void ReadHeldValues()
        {
            // Catalog Phase A (F10; was :831 / :845 m_LodgePrefabs): the catalog's live lodge view, fetched once
            // before the chunk loop (skeptic M4).
            HashSet<Entity> lodgePrefabs = m_PrefabCatalog.LodgePrefabSet;
            if (lodgePrefabs.Count == 0 || m_ParkQuery.IsEmptyIgnoreFilter)
                return;
            m_ParkQuery.CompleteDependency();
            var entityHandle = GetEntityTypeHandle();
            var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
            m_ScratchEntities.Clear();
            using (var chunks = m_ParkQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (lodgePrefabs.Contains(prefabRefs[i].m_Prefab))
                            m_ScratchEntities.Add(entities[i]);
                    }
                }
            }
            for (int i = 0; i < m_ScratchEntities.Count; i++)
            {
                if (EntityManager.TryGetComponent(m_ScratchEntities[i], out AttractivenessProvider provider))
                    m_HeldValues[m_ScratchEntities[i]] = provider.m_Attractiveness;
            }
            m_ScratchEntities.Clear();
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !IsActive)
                    return; // lodge prefab not imported, or the ownership sets not ready — inert by design

                m_Tick++;
                CompareCounters();
                if (m_Tick % Tuning.StatePollTicks == 0)
                    PollState();
                if (--m_TicksUntilRefresh <= 0)
                {
                    m_TicksUntilRefresh = Tuning.PeriodicRefreshTicks;
                    m_PositionsDue = true;   // E1 fixer pass 2: the backstop re-reads the positions too (header COST)
                    RequestSoftRecompute(ReasonTimer);
                }
                AdvanceHolds();

                if (m_StructuralPending || (m_SoftPending && m_Tick - m_LastRecomputeTick >= Tuning.MinSoftGapTicks))
                {
                    string reason = m_PendingReason ?? ReasonTimer;
                    bool structural = m_StructuralPending;
                    m_StructuralPending = m_SoftPending = false;
                    m_PendingReason = null;
                    Recompute(reason, structural);
                }

                WriteProviders();
                FlushAreaLogs();
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiResortAttractivenessSystem)} EXCEPTION — attractiveness " +
                                  $"disabled until the next game load. Please report this stack:\n{e}");
                }
            }
        }

        // ── triggers (header CADENCE; §10) ─────────────────────────────────────────────────────────

        /// <summary>K, backcountry (Mod.Settings with the Default* fallback, clamped as the model clamps) and
        /// gating through SnowLineSystem (e1-spec R10: one read per setting).</summary>
        private void ReadSettings(out int k, out int backcountry, out bool gating)
        {
            k = Mod.Settings?.AttractivenessScaleK ?? SkiResortSettings.DefaultAttractivenessScaleK;
            k = k < SkiResortScore.MinK ? SkiResortScore.MinK : (k > SkiResortScore.MaxK ? SkiResortScore.MaxK : k);
            backcountry = Mod.Settings?.BackcountryValuePercent ?? SkiResortSettings.DefaultBackcountryValuePercent;
            backcountry = backcountry < 0 ? 0 : (backcountry > 100 ? 100 : backcountry);
            gating = m_SnowLine.IsSnowGatingEnabled();
        }

        /// <summary>§10.4 CompareCounters: four int counters, the settings, the SnowSettled edge. The first
        /// tick of a load only records them (the load's own structural recompute covers that tick).</summary>
        private void CompareCounters()
        {
            int bind = m_Ownership.BindGeneration;
            int census = m_Census.Revision;
            int rating = m_Catalog.RatingRevision;
            int snow = m_Injection.RunSnowGeneration;
            ReadSettings(out int k, out int backcountry, out bool gating);
            bool settled = m_Injection.SnowSettled;

            if (!m_CountersPrimed)
            {
                m_CountersPrimed = true;
                m_LastBindGeneration = bind;
                m_LastCensusRevision = census;
                m_LastRatingRevision = rating;
                m_LastSnowGeneration = snow;
                m_LastK = k;
                m_LastBackcountry = backcountry;
                m_LastGating = gating;
            }
            else
            {
                if (bind != m_LastBindGeneration)
                {
                    m_LastBindGeneration = bind;
                    RequestRecompute(ReasonBinding);
                }
                if (census != m_LastCensusRevision)
                {
                    m_LastCensusRevision = census;
                    RequestRecompute(ReasonCensus);
                }
                if (k != m_LastK || backcountry != m_LastBackcountry || gating != m_LastGating)
                {
                    Mod.Log.Info($"Attractiveness [settings]: K {m_LastK} → {k}, backcountry {m_LastBackcountry} % → {backcountry} %, " +
                                 $"snow gating {(m_LastGating ? "on" : "off")} → {(gating ? "on" : "off")} — recompute");
                    m_LastK = k;
                    m_LastBackcountry = backcountry;
                    m_LastGating = gating;
                    RequestRecompute(ReasonSettings);
                }
                if (rating != m_LastRatingRevision)
                {
                    m_LastRatingRevision = rating;
                    RequestSoftRecompute(ReasonRating);
                }
                if (snow != m_LastSnowGeneration)
                {
                    m_LastSnowGeneration = snow;
                    RequestSoftRecompute(ReasonSnow);
                }
            }
            // R4 (second pass): the SnowSettled false → true edge while an area is held.
            if (settled && !m_LastSnowSettled && m_HeldCount > 0)
                RequestSoftRecompute(ReasonSnowSettled);
            m_LastSnowSettled = settled;
        }

        /// <summary>§10.3: every StatePollTicks, one hash of each area's efficiency percent and lodge on/off
        /// bits (ReadSign, the same reads as the recompute) and the census power signature; a change asks for
        /// a soft recompute.</summary>
        private void PollState()
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            ulong hash = SkiRunRatingRules.FnvOffset;
            for (int s = 0; s < m_Cur.SlotCount; s++)
                ReadSign(m_Cur.Slots[s].Sign, null, ref hash);
            hash = SkiRunRatingRules.Mix(hash, unchecked((uint)m_Census.ComputePowerSignature()));
            m_PollCount++;
            m_PollSumMs += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (hash != m_StateHash)
            {
                m_StateHash = hash;
                RequestSoftRecompute(ReasonState);
            }
        }

        /// <summary>§9 step 4: HoldTicks + 1 per tick for every held area; reaching the cap asks for a
        /// structural recompute (which then writes anyway, M1).</summary>
        private void AdvanceHolds()
        {
            if (m_HeldCount == 0)
                return;
            for (int s = 0; s < m_Cur.SlotCount; s++)
            {
                AreaSlot slot = m_Cur.Slots[s];
                if (!slot.Held || !m_AreaStates.TryGetValue(slot.Sign, out AreaState state) || state.FirstWriteDone)
                    continue;
                state.HoldTicks++;
                if (state.HoldTicks == Tuning.RatingHoldMaxTicks)
                    RequestRecompute(ReasonHold);
            }
        }

        // ── the recompute (§6-§9) ──────────────────────────────────────────────────────────────────

        private void Recompute(string reason, bool structural)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            m_Recomputes++;
            m_LastReason = reason;
            m_RecomputesByReason.TryGetValue(reason, out int byReason);
            m_RecomputesByReason[reason] = byReason + 1;
            m_LastRecomputeTick = m_Tick;

            ReadSettings(out int k, out int backcountry, out bool gating);
            Snapshot next = m_Next;
            next.Clear();
            m_DropProvider.Clear();
            m_ZeroSigns.Clear();

            // 1. Signs and lodge buildings (one park walk), then each sign's efficiency and lodges. Positions
            // (the Transform sync) only when due: a structural recompute or the backstop (header COST).
            bool readPositions = structural || m_PositionsDue;
            m_PositionsDue = false;
            readPositions = CollectAreas(next, readPositions);
            m_PositionsReadThisRecompute = readPositions;
            if (readPositions)
            {
                m_LodgeXz.Clear();    // Catalog Phase B: re-read when due, like m_BaseXz (header CATALOG PHASE B)
                m_PatrolXz.Clear();
            }
            ulong stateHash = SkiRunRatingRules.FnvOffset;
            m_LodgeClaimed.Clear();
            for (int s = 0; s < next.SlotCount; s++)
                ReadSign(next.Slots[s].Sign, next.Slots[s], ref stateHash);
            ReadPatrolPositions(next);   // Catalog Phase B (R13): each area's active patrol huts' xz

            // 2. Member runs (bound-run walk): rating, snow, top node.
            CollectRuns(next);

            // 3. The served rule (R2), once for every member run against every lift of the city (I5).
            bool censusArmed = m_Census.IsArmed;
            m_ServeLifts.Clear();
            m_Chords.Clear();
            if (censusArmed)
            {
                m_Census.CopyServeLifts(m_ServeLifts, m_Chords);
                LiftServedRule.Assign(m_RunTops, m_ServeLifts, m_Chords, m_ServingLift, m_ServingDistance, m_ServedCount);
            }
            else if (!m_CensusOpenLogged && m_RunRefs.Count > 0)
            {
                m_CensusOpenLogged = true;
                Mod.Log.Info("Attractiveness: lift census not armed — every member run counts as lift-served until it is " +
                             "(fail open, e1-spec R2); once per load");
            }
            for (int i = 0; i < m_RunRefs.Count; i++)
            {
                RunRef rr = m_RunRefs[i];
                AreaSlot slot = next.Slots[rr.Slot];
                SkiResortScore.RunInput input = slot.Runs[rr.Index];
                input.Served = !censusArmed || m_ServingLift[i] >= 0;
                // Catalog Phase B (R13, D12): patrolled = the run's top node within PatrolRule.RadiusM of an active patrol
                // hut of its own area (the served rule's test point; NaN without facts → not patrolled).
                if (slot.PatrolXz.Count > 0)
                {
                    float3 top = m_RunTops[i];
                    input.Patrolled = PatrolRule.Patrolled(new float2(top.x, top.z), slot.PatrolXz);
                }
                slot.Runs[rr.Index] = input;
            }
            next.Chords = m_Chords.Count;

            // 4. Lifts (census rows with both stations live), resolved to an area.
            CollectLifts(next, readPositions);
            stateHash = SkiRunRatingRules.Mix(stateHash, unchecked((uint)m_Census.ComputePowerSignature()));

            // 5. Score each area; holds; write lists; views; area logs.
            ScoreAreas(next, k, backcountry, gating, reason);

            // 6. Orphan lodges (listed by no live sign) carry no provider.
            foreach (Entity lodge in m_LiveLodges)
            {
                if (!m_LodgeClaimed.Contains(lodge) && EntityManager.HasComponent<AttractivenessProvider>(lodge))
                    m_DropProvider.Add(new DropEntry { Lodge = lodge, SwitchedOff = false });
            }

            // 7. Publish: compare, swap, prune.
            bool changed = SnapshotChanged(m_Cur, next);
            if (LodgeSetChanged(m_Cur, next))
                m_LodgeSetRevision++;   // Round S1 (ch3 §2.1)
            m_Next = m_Cur;
            m_Cur = next;
            if (changed)
                m_Generation++;
            Prune();
            m_StateHash = stateHash;

            // 8. Cost (§14). The first recompute of a load pays one-off costs (Mono JIT of this call tree on the
            // session's first load, the first sync on the park query after the load, the first-call log lines
            // written inside the timed span), so it is kept apart: not in the mean or max, not tested for the
            // WARN; the [cost] line and the DUMP print it on its own (E1 fixer, e1-spec §8.9 F1).
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (m_Recomputes == 1)
            {
                m_FirstRecomputeMs = ms;
                return;
            }
            // Round F7 E3 (integrator; request in docs/research/field-test-7-notes-impl5.md, field test 7 measured an
            // empty city here): only recomputes with at least one area count toward the [cost] line and its WARN.
            if (m_Cur.SlotCount == 0)
                return;
            m_CostCount++;
            m_CostSumMs += ms;
            // Round M2 (D8): the game clock of this recompute (TimeSystem: normalizedTime, year, normalizedDate,
            // daysPerYear, as SkiLiftTicketSystem's midday re-arm reads them).
            float t = m_Time.normalizedTime;
            int day = SkiDayRules.DayKey(m_Time.year, m_Time.normalizedDate, m_Time.daysPerYear);
            int monthKey = SkiMoneyRules.MonthKey(m_Time.year, m_Time.normalizedDate);
            int minute = SkiDayTiming.MinuteOfDay(t);
            if (ms > m_CostMaxMs)
            {
                m_CostMaxMs = ms;
                m_CostMaxMonth = monthKey;
                m_CostMaxMinute = minute;
            }
            int runs = m_Cur.Runs.Count;
            if (SkiDayRules.MiddayRearmDue(m_CostWarned, t, day, m_CostWarnDay))
                m_CostWarned = false;   // Round M2 (D8): once per game day, re-armed at midday
            if (ms > Tuning.CostWarnMs && !m_CostWarned)
            {
                m_CostWarned = true;
                m_CostWarnDay = SkiDayRules.DoneDayOf(t, day);
                Mod.Log.Warn(Invariant($"Attractiveness [cost]: one recompute took {ms:F2} ms (> 2 ms) — ") +
                             $"{m_Cur.SlotCount} area(s), {runs} run(s), {m_Cur.LiftCount} lift(s); reason '{reason}' at " +
                             $"{MonthClockText(monthKey, minute)} (once per game day, re-armed at midday; the load's first " +
                             "recompute is not tested)");
            }
            if (m_CostCount == Tuning.CostReportRecomputes)
            {
                double pollMean = m_PollCount > 0 ? m_PollSumMs / m_PollCount : 0.0;
                Mod.Log.Info(Invariant($"Attractiveness [cost]: {Tuning.CostReportRecomputes} recomputes with an area, mean {m_CostSumMs / m_CostCount:F3} ms, ") +
                             Invariant($"max {m_CostMaxMs:F3} ms (last: {m_Cur.SlotCount} area(s), {runs} run(s), {m_Cur.LiftCount} lift(s), ") +
                             Invariant($"{m_Cur.LiveLodges} lodge(s), {m_Cur.Chords} chord(s)); state poll mean {pollMean:F3} ms over {m_PollCount} poll(s); ") +
                             Invariant($"first recompute after the load {m_FirstRecomputeMs:F3} ms (one-off costs, not in mean or max)"));
            }
        }

        /// <summary>The park walk (§6): one slot per live Welcome Sign, every lodge building into m_LiveLodges.
        /// Syncs only PrefabRef's writers (chunk reads do not wait on running jobs — the ownership header's
        /// rule; the E1 fixer F7 pattern, ParkedVehiclesSystem.cs:1732). Then the sign index for the lift
        /// rule (header COST, E1 fixer pass 2): re-read from the signs' Transforms when
        /// <paramref name="readPositions"/>, else kept from the last read — unless it no longer holds exactly
        /// this walk's signs (a missed tag: read anyway, counted). Returns whether positions were read.</summary>
        private bool CollectAreas(Snapshot next, bool readPositions)
        {
            m_LiveLodges.Clear();
            if (!m_ParkQuery.IsEmptyIgnoreFilter)
            {
                // Catalog Phase A (F10; was :1165 m_LodgePrefabs): the live lodge view, once before the chunk loop (M4).
                HashSet<Entity> lodgePrefabs = m_PrefabCatalog.LodgePrefabSet;
                EntityManager.CompleteDependencyBeforeRO<PrefabRef>();
                var entityHandle = GetEntityTypeHandle();
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                using (var chunks = m_ParkQuery.ToArchetypeChunkArray(Allocator.Temp))
                {
                    for (int c = 0; c < chunks.Length; c++)
                    {
                        var entities = chunks[c].GetNativeArray(entityHandle);
                        var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                        for (int i = 0; i < entities.Length; i++)
                        {
                            Entity prefab = prefabRefs[i].m_Prefab;
                            if (m_Ownership.IsGatePrefab(prefab))
                                next.AddSlot(entities[i]);
                            else if (lodgePrefabs.Contains(prefab))
                                m_LiveLodges.Add(entities[i]);
                        }
                    }
                }
            }
            next.LiveLodges = m_LiveLodges.Count;

            if (!readPositions && !SignIndexMatches(next))
            {
                readPositions = true;
                m_PositionFallbacks++;
            }
            if (readPositions)
            {
                // The one Transform sync of this recompute: EntityManager.GetComponentData completes the
                // Transform writers (the move systems' jobs) on its first call; the rest are plain reads.
                m_SignIndex.Clear();
                for (int s = 0; s < next.SlotCount; s++)
                {
                    Entity sign = next.Slots[s].Sign;
                    float2 xz = EntityManager.TryGetComponent(sign, out Game.Objects.Transform t)
                        ? t.m_Position.xz
                        : new float2(float.NaN, float.NaN);   // never resolves (SkiResortIndex: non-finite)
                    m_SignIndex.Add(LiftCensusSystem.Ref(sign), xz);
                }
                m_PositionReads++;
            }
            return readPositions;
        }

        /// <summary>The cached sign index holds exactly the signs of this walk (count and membership).</summary>
        private bool SignIndexMatches(Snapshot next)
        {
            if (m_SignIndex.Count != next.SlotCount)
                return false;
            for (int s = 0; s < next.SlotCount; s++)
            {
                if (m_SignIndex.IndexOf(LiftCensusSystem.Ref(next.Slots[s].Sign)) < 0)
                    return false;
            }
            return true;
        }

        /// <summary>One sign's efficiency (§8) and lodges (§6), mixed into <paramref name="hash"/> — the
        /// state poll's input. With a slot (the recompute) it also fills the slot: efficiency, percent, the
        /// factors below 1 worst first, and the active / inactive member lodges (a lodge building in the walk,
        /// first sign wins). Without one (the poll) it only hashes.</summary>
        private void ReadSign(Entity sign, AreaSlot slot, ref ulong hash)
        {
            hash = SkiRunRatingRules.Mix(SkiRunRatingRules.Mix(hash, unchecked((uint)sign.Index)), unchecked((uint)sign.Version));
            if (!EntityManager.Exists(sign))
            {
                hash = SkiRunRatingRules.Mix(hash, 0xFFFFFFFFu);
                return;
            }

            int count = 0;
            if (EntityManager.TryGetBuffer(sign, isReadOnly: true, out DynamicBuffer<Efficiency> factors))
            {
                count = factors.Length;
                if (m_FactorValues.Length < count)
                    m_FactorValues = new float[count];
                for (int i = 0; i < count; i++)
                {
                    Efficiency factor = factors[i];
                    m_FactorValues[i] = factor.m_Efficiency;
                    if (slot != null && factor.m_Efficiency < 1f)
                        slot.Factors.Add(factor);
                }
            }
            float efficiency = SkiResortScore.SignEfficiency(m_FactorValues, count);
            int pct = (int)Math.Round(100.0 * efficiency);
            hash = SkiRunRatingRules.Mix(hash, unchecked((uint)pct));
            if (slot != null)
            {
                slot.EfficiencyValue = efficiency;
                slot.EfficiencyPct = pct;
                // Efficiency.CompareTo orders the highest value first (Efficiency.cs:36-43): reversed = worst first.
                slot.Factors.Sort();
                slot.Factors.Reverse();
            }

            if (!EntityManager.TryGetBuffer(sign, isReadOnly: true, out DynamicBuffer<InstalledUpgrade> upgrades))
                return;
            // Catalog Phase A (F10; was :1254 m_LodgePrefabs), once per sign. Catalog Phase B (round-2-contract §7.4): the
            // lodge rows by tier (LodgeDefs), the school and the patrol huts, each view fetched once per sign.
            HashSet<Entity> lodgePrefabs = m_PrefabCatalog.LodgePrefabSet;
            IReadOnlyDictionary<Entity, LodgeDef> lodgeDefs = m_PrefabCatalog.LodgeDefs;
            HashSet<Entity> schoolPrefabs = m_PrefabCatalog.SchoolPrefabSet;
            HashSet<Entity> patrolPrefabs = m_PrefabCatalog.PatrolPrefabSet;
            for (int u = 0; u < upgrades.Length; u++)
            {
                InstalledUpgrade entry = upgrades[u];
                Entity upgrade = entry.m_Upgrade;
                if (!EntityManager.TryGetComponent(upgrade, out PrefabRef prefabRef))
                    continue;
                Entity prefab = prefabRef.m_Prefab;
                bool isLodge = lodgePrefabs.Contains(prefab);
                bool isSchool = !isLodge && schoolPrefabs.Contains(prefab);
                bool isPatrol = !isLodge && !isSchool && patrolPrefabs.Contains(prefab);
                if (!isLodge && !isSchool && !isPatrol)
                    continue;
                bool active = !BuildingUtils.CheckOption(entry, BuildingOption.Inactive);
                hash = SkiRunRatingRules.Mix(SkiRunRatingRules.Mix(SkiRunRatingRules.Mix(hash,
                    unchecked((uint)upgrade.Index)), unchecked((uint)upgrade.Version)), active ? 1u : 0u);
                if (slot == null)
                    continue;
                if (isLodge)
                {
                    Entity lodge = upgrade;
                    if (!m_LiveLodges.Contains(lodge) || !m_LodgeClaimed.Add(lodge))
                        continue;
                    if (!active)
                    {
                        slot.Inactive.Add(lodge);
                        continue;
                    }
                    slot.Active.Add(lodge);
                    // Seam S5: the tier (the lodge's PrefabRef IS its tier; no serialized state). SummitServed is filled in
                    // ScoreAreas, once the area's lifts are known.
                    int index = lodgeDefs.TryGetValue(prefab, out LodgeDef def) ? def.Index : 0;
                    slot.ActiveInfo.Add(new LodgeInfo { Lodge = lodge, LodgeIndex = (short)index, SummitServed = false });
                }
                else if (isSchool)
                {
                    slot.Schools++;
                    if (active && IsLiveUpgrade(upgrade))
                        slot.SchoolActive = true;
                }
                else if (active && IsLiveUpgrade(upgrade))
                {
                    slot.PatrolEntities.Add(upgrade);
                }
            }
        }

        /// <summary>Catalog Phase B: a school or patrol hut entry counts while its building exists and is neither Deleted
        /// nor Temp (they are not in the park walk: m_LiveLodges holds lodges only).</summary>
        private bool IsLiveUpgrade(Entity building)
        {
            return building != Entity.Null && EntityManager.Exists(building) && !EntityManager.HasComponent<Deleted>(building) &&
                   !EntityManager.HasComponent<Temp>(building);
        }

        /// <summary>Catalog Phase B (R13): each area's active patrol huts' xz into slot.PatrolXz, from m_PatrolXz (read
        /// when due: a structural recompute or the backstop; a miss on a soft recompute reads it and is counted as a
        /// fallback, header SYNC POINTS). A hut without a Transform is skipped (it patrols nothing).</summary>
        private void ReadPatrolPositions(Snapshot next)
        {
            for (int s = 0; s < next.SlotCount; s++)
            {
                AreaSlot slot = next.Slots[s];
                for (int i = 0; i < slot.PatrolEntities.Count; i++)
                {
                    if (TryCachedXz(m_PatrolXz, slot.PatrolEntities[i], out float2 xz))
                        slot.PatrolXz.Add(xz);
                }
            }
        }

        /// <summary>Catalog Phase B: one building's xz through a per-load cache (cleared when positions are due, header
        /// SYNC POINTS). A miss reads the Transform (counted as a fallback on a soft recompute); false without one.</summary>
        private bool TryCachedXz(Dictionary<Entity, float2> cache, Entity building, out float2 xz)
        {
            if (cache.TryGetValue(building, out xz))
                return true;
            if (!EntityManager.TryGetComponent(building, out Game.Objects.Transform transform))
                return false;
            xz = transform.m_Position.xz;
            cache[building] = xz;
            if (!m_PositionsReadThisRecompute)
                m_PositionFallbacks++;
            return true;
        }

        /// <summary>The bound-run walk (§6): each member run's RunInput (Served filled later), its entity, its
        /// top node for the served rule (NaN without facts: served by nothing).</summary>
        private void CollectRuns(Snapshot next)
        {
            m_RunTops.Clear();
            m_RunRefs.Clear();
            if (next.SlotCount == 0 || m_RunQuery.IsEmptyIgnoreFilter)
                return;
            m_RunQuery.CompleteDependency();
            var entityHandle = GetEntityTypeHandle();
            var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
            var geometryHandle = GetComponentTypeHandle<Geometry>(isReadOnly: true);
            var bindingHandle = GetComponentTypeHandle<SkiRunBinding>(isReadOnly: true);
            using (var chunks = m_RunQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    var geometries = chunks[c].GetNativeArray(ref geometryHandle);
                    var bindings = chunks[c].GetNativeArray(ref bindingHandle);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        Entity sign = bindings[i].m_Gate;
                        if (sign == Entity.Null || !next.SlotOfSign.TryGetValue(sign, out int slotIndex))
                            continue;   // unbound, paid orphan, or a sign that is not live: not a member (D1)
                        int kind = m_Ownership.KindOf(prefabRefs[i].m_Prefab);
                        if (kind < 0)
                            continue;
                        AddRun(next.Slots[slotIndex], slotIndex, entities[i], kind, geometries[i]);
                    }
                }
            }
        }

        private void AddRun(AreaSlot slot, int slotIndex, Entity run, int kind, Geometry geometry)
        {
            float bottomY = geometry.m_Bounds.min.y;   // node heights (GeometrySystem.cs:499-576; R13)
            float topY = geometry.m_Bounds.max.y;
            var input = new SkiResortScore.RunInput
            {
                Kind = (byte)kind,
                AreaM2 = geometry.m_SurfaceArea,
                TopM = topY,
                BottomM = bottomY,
            };

            // Rating (§6, I4): a current rating; else the last one seen this load; else pending.
            if (m_Catalog.TryGetRating(run, out byte state, out sbyte band, out float topM, out float bottomM))
            {
                m_LastRatings[run] = new LastRating { State = state, Band = band, TopM = topM, BottomM = bottomM };
                ApplyRating(ref input, state, band, topM, bottomM);
            }
            else if (m_LastRatings.TryGetValue(run, out LastRating last))
            {
                ApplyRating(ref input, last.State, last.Band, last.TopM, last.BottomM);
            }
            else
            {
                input.State = SkiResortScore.BandState.Pending;
            }

            // Open state (R3): the snow chapter's multiplier; no climate yet → 0, estimated, "open unknown".
            RunSnow snow = m_Injection.GetRunSnow(run, (RunKind)kind, bottomY, topY);
            if (snow.Source == RunSnowSource.None)
            {
                input.OpenFactor = 0.0;
                input.OpenEstimated = true;
                slot.OpenUnknown++;
            }
            else
            {
                input.OpenFactor = snow.Multiplier;
                input.OpenEstimated = snow.Source == RunSnowSource.Estimated;
            }

            // The served-rule test point (R2): the highest outline node.
            float3 top = new float3(float.NaN);
            if (m_Catalog.TryGetRunFacts(run, out SkiRunCatalogSystem.RunFacts facts))
                top = facts.TopNode;

            slot.Runs.Add(input);
            slot.RunEntities.Add(run);
            m_RunTops.Add(top);
            m_RunRefs.Add(new RunRef { Slot = slotIndex, Index = slot.Runs.Count - 1 });
        }

        /// <summary>The catalog's (state, band, heights) → BandState / Band / heights (§6 "Per member run").
        /// Rated with a SlopeBand 0..4 → Rated (rating heights when finite, else the node heights already set);
        /// states 2-4 → Unrateable (node heights); anything else → Pending.</summary>
        private static void ApplyRating(ref SkiResortScore.RunInput input, byte state, sbyte band, float topM, float bottomM)
        {
            if (state == SkiRunRatingRules.RateRated && band >= 0 && band <= SkiResortScore.BandDoubleBlack)
            {
                input.State = SkiResortScore.BandState.Rated;
                input.Band = (byte)band;
                if (IsFinite(topM) && IsFinite(bottomM))
                {
                    input.TopM = topM;
                    input.BottomM = bottomM;
                }
            }
            else if (state == SkiRunRatingRules.RateNoTerrain || state == SkiRunRatingRules.RateTooLarge ||
                     state == SkiRunRatingRules.RateFailed)
            {
                input.State = SkiResortScore.BandState.Unrateable;
            }
            else
            {
                input.State = SkiResortScore.BandState.Pending;
            }
        }

        private static bool IsFinite(float x)
        {
            return !float.IsNaN(x) && !float.IsInfinity(x);
        }

        /// <summary>The lift rule (§6): census rows whose two stations are live, Complete or Open; area by the
        /// base station's xz; power from the CopyServeLifts row of the same index (R1). The base xz comes from
        /// m_BaseXz (header COST, E1 fixer pass 2): cleared and re-read when <paramref name="readPositions"/>,
        /// else reused; a station missing from it is read (a sync; counted when it happens on a soft
        /// recompute — a new row moves the census Revision, so it should not).</summary>
        private void CollectLifts(Snapshot next, bool readPositions)
        {
            if (readPositions)
                m_BaseXz.Clear();
            m_CensusRows.Clear();
            m_Census.CopyRows(m_CensusRows);
            bool aligned = m_ServeLifts.Count == m_CensusRows.Count;
            for (int k = 0; k < m_CensusRows.Count; k++)
            {
                LiftCensus.Row row = m_CensusRows[k];
                if (row.State != LiftCensus.State.Complete && row.State != LiftCensus.State.Open)
                    continue;
                if (row.Base.IsNull || row.Top.IsNull)
                    continue;
                Entity baseStation = LiftCensusSystem.Ent(row.Base);
                Entity topStation = LiftCensusSystem.Ent(row.Top);
                if (!IsLiveBuilding(baseStation) || !IsLiveBuilding(topStation))
                    continue;
                if (!m_BaseXz.TryGetValue(baseStation, out float2 baseXz))
                {
                    if (!EntityManager.TryGetComponent(baseStation, out Game.Objects.Transform transform))
                        continue;
                    baseXz = transform.m_Position.xz;
                    m_BaseXz[baseStation] = baseXz;
                    if (!readPositions)
                        m_PositionFallbacks++;
                }
                if (!m_SignIndex.TryResolve(baseXz, SkiRunOwnershipSystem.TetherMeters, out EntityRef signRef, out float _))
                    continue;   // Not linked: no area, no value
                Entity sign = LiftCensusSystem.Ent(signRef);
                if (!next.TryGetSlot(sign, out AreaSlot slot))
                    continue;

                LiftPower power = LiftPower.Unknown;
                if (aligned && m_ServeLifts[k].Id == row.Id && m_ServeLifts[k].Top == row.Top)
                {
                    power = m_ServeLifts[k].Power;
                    slot.ServeLifts.Add(k);   // Catalog Phase B: this area's lifts for SummitServed (D11)
                }
                bool complete = row.State == LiftCensus.State.Complete;
                bool powered = LiftServedRule.IsPowered(power);
                // Catalog Phase B (round-2-contract §7.4, seam S1): the row's lift type (SkiCatalog.Lifts index; Mixed rows
                // are Unfinished and skipped above, a NoType 255 reads the fixed-grip row in the score).
                slot.Lifts.Add(new SkiResortScore.LiftInput { Complete = complete, Powered = powered, TypeIndex = row.Type });
                Entity id = LiftCensusSystem.Ent(row.Id);
                next.Lifts[new LiftKey(id, topStation)] = new LiftScoreView
                {
                    Id = id,
                    Base = baseStation,
                    Top = topStation,
                    Sign = sign,
                    Complete = complete,
                    Powered = power != LiftPower.NoPower,
                    Counted = complete && powered,
                };
                next.LiftCount++;
            }
        }

        private bool IsLiveBuilding(Entity entity)
        {
            return entity != Entity.Null && EntityManager.Exists(entity) && !EntityManager.HasComponent<Deleted>(entity);
        }

        /// <summary>§5 Compute per area, §9 holds, §7 write lists, §12 views, §13 area lines.</summary>
        private void ScoreAreas(Snapshot next, int k, int backcountry, bool gating, string reason)
        {
            var settings = new SkiResortScore.ScoreSettings { K = k, BackcountryPercent = backcountry, SnowGating = gating };
            bool ratingFailed = m_Catalog.ServiceState == SkiRunCatalogSystem.RatingServiceState.Failed;
            bool snowSettled = m_Injection.SnowSettled;
            m_HeldCount = 0;
            for (int s = 0; s < next.SlotCount; s++)
            {
                AreaSlot slot = next.Slots[s];
                BuildLodgeInputs(slot);   // Catalog Phase B: tiers + SummitServed (D11)
                var input = new SkiResortScore.AreaInput
                {
                    ActiveLodges = slot.Active.Count,
                    InactiveLodges = slot.Inactive.Count,
                    Efficiency = slot.EfficiencyValue,
                    Settings = settings,
                    Lodges = slot.Lodges,               // Catalog Phase B (R11): the ladder (Count == ActiveLodges)
                    SchoolActive = slot.SchoolActive,   // Catalog Phase B (R13)
                };
                slot.Result = SkiResortScore.Compute(input, slot.Runs, slot.Lifts, slot.Parts);

                // §9: the first write of this load waits for ratings and snow (R4), at most 1,024 ticks (M1).
                if (!m_AreaStates.TryGetValue(slot.Sign, out AreaState state))
                {
                    state = new AreaState();
                    m_AreaStates[slot.Sign] = state;
                }
                bool firstWrite = false, expired = false;
                if (!state.FirstWriteDone)
                {
                    if ((slot.Result.RunsPending == 0 || ratingFailed) && snowSettled)
                        firstWrite = true;
                    else if (state.HoldTicks < Tuning.RatingHoldMaxTicks)
                        slot.Held = true;
                    else
                        firstWrite = expired = true;
                    if (firstWrite)
                        state.FirstWriteDone = true;
                }
                if (slot.Held)
                    m_HeldCount++;

                // §7.2 / §7.5: active lodges get PerLodge — or, while held, the value saved in the city.
                for (int i = 0; i < slot.Active.Count; i++)
                {
                    Entity lodge = slot.Active[i];
                    if (!slot.Held)
                    {
                        next.WriteValues[lodge] = slot.Result.PerLodge;
                        next.LodgeArea[lodge] = slot.Sign;
                    }
                    else if (m_HeldValues.TryGetValue(lodge, out int held))
                    {
                        next.WriteValues[lodge] = held;
                        next.LodgeArea[lodge] = slot.Sign;
                    }
                }
                // §7.3: a switched-off lodge carries no provider.
                for (int i = 0; i < slot.Inactive.Count; i++)
                {
                    if (EntityManager.HasComponent<AttractivenessProvider>(slot.Inactive[i]))
                        m_DropProvider.Add(new DropEntry { Lodge = slot.Inactive[i], SwitchedOff = true });
                }
                // §7.4: a sign carrying a provider is held at 0 (not removed).
                if (EntityManager.HasComponent<AttractivenessProvider>(slot.Sign))
                {
                    m_ZeroSigns.Add(slot.Sign);
                    if (m_ZeroSignLogged.Add(slot.Sign))
                        Mod.Log.Info($"Attractiveness: Welcome Sign {IdOf(slot.Sign)} carries a game AttractivenessProvider (a city saved " +
                                     "before E1, or another mod) — held at 0 after the game's write each tick; the ski area's score " +
                                     "lives on its lodges");
                }

                // §12: run views.
                for (int i = 0; i < slot.RunEntities.Count; i++)
                {
                    next.Runs[slot.RunEntities[i]] = new RunScoreView
                    {
                        Run = slot.RunEntities[i],
                        Sign = slot.Sign,
                        Input = slot.Runs[i],
                        Part = slot.Parts[i],
                    };
                }

                // §13: logs.
                if (firstWrite)
                    LogFirstWrite(slot, state, expired);
                LogArea(slot, reason, ratingFailed);
            }
        }

        /// <summary>Catalog Phase B (R11, D11): slot.Lodges (the score's AreaInput.Lodges) from slot.ActiveInfo, with
        /// SummitServed evaluated for the rows whose summer share rises when served (μ summit &gt; μ): the lodge's xz
        /// (m_LodgeXz, read when due) within Ladder.SummitServedRadiusM of the top stub tip of a complete, powered lift
        /// of this area (LiftServedRule.SummitServed over the area's m_ServeLifts rows). Allocation-free once grown.</summary>
        private void BuildLodgeInputs(AreaSlot slot)
        {
            slot.Lodges.Clear();
            bool liftsCopied = false;
            for (int i = 0; i < slot.ActiveInfo.Count; i++)
            {
                LodgeInfo info = slot.ActiveInfo[i];
                bool served = false;
                if (info.LodgeIndex >= 0 && info.LodgeIndex < SkiCatalog.Lodges.Count)
                {
                    LodgeDef def = SkiCatalog.Lodges[info.LodgeIndex];
                    if (def.MuSummit > def.Mu && TryCachedXz(m_LodgeXz, info.Lodge, out float2 xz))
                    {
                        if (!liftsCopied)
                        {
                            m_AreaServeLifts.Clear();
                            for (int k = 0; k < slot.ServeLifts.Count; k++)
                            {
                                int idx = slot.ServeLifts[k];
                                if (idx >= 0 && idx < m_ServeLifts.Count)
                                    m_AreaServeLifts.Add(m_ServeLifts[idx]);
                            }
                            liftsCopied = true;
                        }
                        served = LiftServedRule.SummitServed(xz, m_AreaServeLifts, SkiCatalog.Ladder.SummitServedRadiusM);
                    }
                }
                info.SummitServed = served;
                slot.ActiveInfo[i] = info;
                slot.Lodges.Add(new SkiResortScore.LodgeInput { LodgeIndex = info.LodgeIndex, SummitServed = served });
            }
        }

        private static string LodgeName(int lodgeIndex)
        {
            return lodgeIndex >= 0 && lodgeIndex < SkiCatalog.Lodges.Count ? SkiCatalog.Lodges[lodgeIndex].Prefab : "?";
        }

        private void LogFirstWrite(AreaSlot slot, AreaState state, bool expired)
        {
            if (expired)
            {
                if (state.HoldWarned)
                    return;
                state.HoldWarned = true;
                Mod.Log.Warn($"Attractiveness [first-write] area {IdOf(slot.Sign)}: after {Tuning.RatingHoldMaxTicks} ticks " +
                             $"({Tuning.RatingHoldMaxTicks * 16} frames): {slot.Result.RunsPending} run(s) unrated, snow settled " +
                             $"{m_Injection.SnowSettled}, rating service {m_Catalog.ServiceState} — writing now; unrated runs count 0 " +
                             "until rated (look for 'Catalog [rate]' and 'Snow [' lines)");
                return;
            }
            var held = new StringBuilder();
            for (int i = 0; i < slot.Active.Count; i++)
            {
                if (!m_HeldValues.TryGetValue(slot.Active[i], out int value))
                    continue;
                held.Append(held.Length > 0 ? ", " : "").Append(IdOf(slot.Active[i])).Append('=').Append(value.ToString(CultureInfo.InvariantCulture));
            }
            // E1 fixer (e1-spec §8.9 F2): name both release conditions of R4, not only the ratings.
            string ratings = slot.Result.RunsPending == 0 ? "runs rated" : "rating service failed";
            Mod.Log.Info($"Attractiveness [first-write] area {IdOf(slot.Sign)}: released after {state.HoldTicks} tick(s) of 16 frames " +
                         $"({ratings}, snow settled) — A {slot.Result.Attraction}, each of {slot.Result.ActiveLodges} active lodge(s) " +
                         $"{slot.Result.PerLodge} (held until now: {(held.Length > 0 ? held.ToString() : "nothing")})");
        }

        /// <summary>§13 area line: written when A, a count, a flag or WriteHeld changed since the area's last
        /// line, through the area's LogWindow (the first after a quiet window plain, the rest folded). A held
        /// area names its blocker (E1 fixer, e1-spec §8.9 F2): "ratings pending" while a member run is unrated
        /// and the rating service has not failed, else "snow settling" (R4's SnowSettled clock).
        /// Round S1 F4 (ski-days spec ch3 §5; field test 8: 89 of 104 score lines were efficiency-triggered): a
        /// change that is efficiency-only — same counts, flags and held state (StructKey), same runs value, another
        /// efficiency percent, A within EfficiencyLineMinDeltaA of the logged A — is skipped (counted once per
        /// change, DUMP) unless the area wrote no line for EfficiencyLineQuietS. Log only: score, Generation and
        /// the hub are unaffected.</summary>
        private void LogArea(AreaSlot slot, string reason, bool ratingFailed)
        {
            SkiResortScore.Result r = slot.Result;
            ulong structKey = SkiRunRatingRules.FnvOffset;
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.ActiveLodges));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.InactiveLodges));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.Runs));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.RunsOpen));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.RunsCounted));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.RunsPending));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.RunsUnrateable));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.RunsFlat));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.RunsUnserved));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.RunsEstimated));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)slot.OpenUnknown));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.Lifts));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.LiftsComplete));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.LiftsOpenSection));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.LiftsUnpowered));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.LiftsCounted));
            structKey = SkiRunRatingRules.Mix(structKey, (uint)r.Flags);
            // Catalog Phase B: the ladder's best tier, the school and the patrolled runs.
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.LodgeBestIndex));
            structKey = SkiRunRatingRules.Mix(structKey, (r.SchoolActive ? 1u : 0u) | (r.GreenCounted ? 2u : 0u));
            structKey = SkiRunRatingRules.Mix(structKey, unchecked((uint)r.RunsPatrolled));
            bool heldForRatings = slot.Held && r.RunsPending > 0 && !ratingFailed;
            structKey = SkiRunRatingRules.Mix(structKey, !slot.Held ? 0u : heldForRatings ? 1u : 2u);
            ulong key = SkiRunRatingRules.Mix(SkiRunRatingRules.Mix(structKey, unchecked((uint)r.Attraction)),
                                              unchecked((uint)r.PerLodge));

            if (!m_AreaLogs.TryGetValue(slot.Sign, out AreaLog log))
            {
                log = new AreaLog();
                m_AreaLogs[slot.Sign] = log;
            }
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (log.HasKey && log.Key == key)
                return;
            // Round S1 F4: an efficiency-only change (summary above).
            if (log.HasKey && structKey == log.StructKey && r.RunsValue == log.LoggedRunsValue &&
                slot.EfficiencyPct != log.LoggedEffPct && Math.Abs(r.Attraction - log.LoggedA) < Tuning.EfficiencyLineMinDeltaA &&
                !log.Window.QuietFor(now, Tuning.EfficiencyLineQuietS))
            {
                if (key != log.SkippedKey)
                {
                    m_EfficiencyLinesSkipped++;
                    log.SkippedKey = key;
                }
                return;
            }
            log.HasKey = true;
            log.Key = key;
            log.StructKey = structKey;
            log.LoggedA = r.Attraction;
            log.LoggedRunsValue = r.RunsValue;
            log.LoggedEffPct = slot.EfficiencyPct;

            // Each Invariant(...) holds ONE interpolated string (C# 9: a `+` of two of them is a plain string).
            // Catalog Phase B: LodgeBase × LodgeShare = the ladder's applied value (display); the best tier is named when it
            // is not the A-frame; "lifts n × 6 × λ" stays for fixed-grip-only areas, other types print their summed value;
            // the school and patrolled runs are appended only when on / non-zero.
            string lodgeTier = r.LodgeBestIndex > 0 ? ", best " + LodgeName(r.LodgeBestIndex) : "";
            string liftsText = r.LiftsValue == SkiResortScore.LiftValue * r.LiftsCounted
                ? Invariant($"lifts {r.LiftsCounted} × 6 × {r.LiftShare:0.##}")
                : Invariant($"lifts {r.LiftsCounted} = {r.LiftsValue:0.##} pts × {r.LiftShare:0.##}");
            string extras = (r.SchoolActive ? (r.GreenCounted ? "; school on (+" + r.SchoolValue.ToString("0.##", CultureInfo.InvariantCulture) + ")"
                                                              : "; school on (no counted green run: +0)") : "") +
                            (r.RunsPatrolled > 0 ? Invariant($"; patrolled {r.RunsPatrolled}") : "");
            string text =
                Invariant($"Attractiveness: area {slot.Sign.Index}:{slot.Sign.Version} A {r.Attraction} = round(({r.LodgeBase:0.##} × {r.LodgeShare:0.##} + ") +
                Invariant($"{r.K} × sqrt({r.S:F1})) × {r.Efficiency:F2}) — lodges {r.ActiveLodges} active (+{r.InactiveLodges} off) × {r.PerLodge}") +
                lodgeTier + "; " +
                Invariant($"runs {r.RunsCounted}/{r.Runs} counted = {r.RunsValue:F1} pts ({r.RunsOpen} open, {r.RunsUnserved} without a lift, ") +
                Invariant($"{r.RunsPending} unrated, {slot.OpenUnknown} open unknown") +
                ((r.Flags & SkiResortScore.ScoreFlags.OpenEstimated) != 0 ? ", estimated" : "") + "); " +
                liftsText + Invariant($" ({r.LiftsOpenSection} with a section open, {r.LiftsUnpowered} unpowered); ") +
                Invariant($"variety ×{r.VarietyMultiplier:0.##}; vertical {r.VerticalM:F0} m ×{r.VerticalMultiplier:F2}") + extras +
                ((r.Flags & SkiResortScore.ScoreFlags.SummerMode) != 0 ? "; SUMMER" : "") +
                ((r.Flags & SkiResortScore.ScoreFlags.NoLodge) != 0 ? "; NO LODGE" : "") +
                (!slot.Held ? "" : heldForRatings ? "; HELD (ratings pending)" : "; HELD (snow settling)") +
                ((r.Flags & SkiResortScore.ScoreFlags.Clamped) != 0 ? "; CLAMPED" : "") + " — " + reason;
            switch (log.Window.Offer(now, false, text, r.Attraction, r.RunsValue, slot.EfficiencyPct))
            {
                case LogWindow.Verdict.Plain:
                    Mod.Log.Info(text);
                    break;
                case LogWindow.Verdict.Summary:
                    WriteAreaSummary(slot.Sign, log.Window, now);
                    break;
            }
        }

        private static void WriteAreaSummary(Entity sign, LogWindow window, float now)
        {
            Mod.Log.Info(Invariant($"Attractiveness: area {sign.Index}:{sign.Version} — {window.Count} change(s) folded over {window.SpanS(now):F0} s ") +
                         $"(A {window.Range(0, "F0")}, runs {window.Range(1, "F1")} pts, efficiency {window.Range(2, "F0")} %) — last: " +
                         window.LastText);
            window.Wrote(now);
        }

        /// <summary>Trailing fold summaries, once the window has passed (on the tick: silence impossible).</summary>
        private void FlushAreaLogs()
        {
            if (m_AreaLogs.Count == 0)
                return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            foreach (var pair in m_AreaLogs)
            {
                if (pair.Value.Window.TrailingDue(now))
                    WriteAreaSummary(pair.Key, pair.Value.Window, now);
            }
        }

        /// <summary>Every waiting fold, now (OnGamePreload, OnDestroy, an area that left).</summary>
        private void FlushAllAreaLogs()
        {
            if (m_AreaLogs.Count == 0)
                return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            foreach (var pair in m_AreaLogs)
            {
                if (pair.Value.Window.Count > 0)
                    WriteAreaSummary(pair.Key, pair.Value.Window, now);
            }
        }

        // ── publish: change detection and pruning ──────────────────────────────────────────────────

        /// <summary>True when any area view, run view, lift view, efficiency factor list or written value
        /// differs (header GENERATION). HoldTicks is not compared (the live count is read through the view).</summary>
        private static bool SnapshotChanged(Snapshot cur, Snapshot next)
        {
            if (cur.SlotCount != next.SlotCount || cur.Runs.Count != next.Runs.Count || cur.Lifts.Count != next.Lifts.Count ||
                cur.WriteValues.Count != next.WriteValues.Count)
                return true;
            for (int s = 0; s < next.SlotCount; s++)
            {
                AreaSlot a = next.Slots[s];
                if (!cur.TryGetSlot(a.Sign, out AreaSlot b))
                    return true;
                if (!a.Result.Equals(b.Result) || a.Held != b.Held || a.EfficiencyPct != b.EfficiencyPct ||
                    a.OpenUnknown != b.OpenUnknown || a.Factors.Count != b.Factors.Count)
                    return true;
                for (int i = 0; i < a.Factors.Count; i++)
                {
                    if (a.Factors[i].m_Factor != b.Factors[i].m_Factor || a.Factors[i].m_Efficiency != b.Factors[i].m_Efficiency)
                        return true;
                }
                // Catalog Phase B (seam S5): a published LodgeInfo that changed (tier, SummitServed) moves
                // Generation even when the score did not (e.g. SummitServed flipping in winter).
                if (a.ActiveInfo.Count != b.ActiveInfo.Count)
                    return true;
                for (int i = 0; i < a.ActiveInfo.Count; i++)
                {
                    LodgeInfo x = a.ActiveInfo[i], y = b.ActiveInfo[i];
                    if (x.Lodge != y.Lodge || x.LodgeIndex != y.LodgeIndex || x.SummitServed != y.SummitServed)
                        return true;
                }
            }
            foreach (var pair in next.Runs)
            {
                if (!cur.Runs.TryGetValue(pair.Key, out RunScoreView old) || !SameRun(old, pair.Value))
                    return true;
            }
            foreach (var pair in next.Lifts)
            {
                if (!cur.Lifts.TryGetValue(pair.Key, out LiftScoreView old))
                    return true;
                LiftScoreView v = pair.Value;
                if (old.Base != v.Base || old.Sign != v.Sign || old.Complete != v.Complete || old.Powered != v.Powered ||
                    old.Counted != v.Counted)
                    return true;
            }
            foreach (var pair in next.WriteValues)
            {
                if (!cur.WriteValues.TryGetValue(pair.Key, out int old) || old != pair.Value)
                    return true;
            }
            return false;
        }

        /// <summary>Round S1 (ch3 §2.1, refutation R15): the published (sign, active lodges) lists differ — the slot
        /// count, a sign of <paramref name="next"/> with no slot in <paramref name="cur"/>, or a sign's Active list
        /// in count or element order. Compared per sign through SlotOfSign, so a slot reorder (chunk order) is not
        /// a change.</summary>
        private static bool LodgeSetChanged(Snapshot cur, Snapshot next)
        {
            if (cur.SlotCount != next.SlotCount)
                return true;
            for (int s = 0; s < next.SlotCount; s++)
            {
                AreaSlot a = next.Slots[s];
                if (!cur.TryGetSlot(a.Sign, out AreaSlot b) || a.Active.Count != b.Active.Count)
                    return true;
                for (int i = 0; i < a.Active.Count; i++)
                {
                    if (a.Active[i] != b.Active[i])
                        return true;
                }
            }
            return false;
        }

        private static bool SameRun(RunScoreView a, RunScoreView b)
        {
            SkiResortScore.RunInput x = a.Input, y = b.Input;
            return a.Run == b.Run && a.Sign == b.Sign && a.Part.Equals(b.Part) &&
                   x.Kind == y.Kind && x.State == y.State && x.Band == y.Band && x.Served == y.Served &&
                   x.Patrolled == y.Patrolled &&   // Catalog Phase B (R13)
                   x.OpenEstimated == y.OpenEstimated && Bits(x.AreaM2) == Bits(y.AreaM2) &&
                   Bits(x.OpenFactor) == Bits(y.OpenFactor) && Bits(x.TopM) == Bits(y.TopM) && Bits(x.BottomM) == Bits(y.BottomM);
        }

        private static long Bits(double x)
        {
            return BitConverter.DoubleToInt64Bits(x);
        }

        /// <summary>Per-load maps keep only what the last recompute still knows: area states and log windows
        /// of signs that left (their folds written first), last ratings of runs that left, damping counters
        /// of lodges no longer written.</summary>
        private void Prune()
        {
            if (m_AreaStates.Count > m_Cur.SlotCount)
            {
                m_ScratchEntities.Clear();
                foreach (var pair in m_AreaStates)
                {
                    if (!m_Cur.SlotOfSign.ContainsKey(pair.Key))
                        m_ScratchEntities.Add(pair.Key);
                }
                for (int i = 0; i < m_ScratchEntities.Count; i++)
                    m_AreaStates.Remove(m_ScratchEntities[i]);
            }
            if (m_AreaLogs.Count > m_Cur.SlotCount)
            {
                m_ScratchEntities.Clear();
                float now = UnityEngine.Time.realtimeSinceStartup;
                foreach (var pair in m_AreaLogs)
                {
                    if (m_Cur.SlotOfSign.ContainsKey(pair.Key))
                        continue;
                    if (pair.Value.Window.Count > 0)
                        WriteAreaSummary(pair.Key, pair.Value.Window, now);
                    m_ScratchEntities.Add(pair.Key);
                }
                for (int i = 0; i < m_ScratchEntities.Count; i++)
                    m_AreaLogs.Remove(m_ScratchEntities[i]);
            }
            if (m_LastRatings.Count > m_Cur.Runs.Count)
            {
                m_ScratchEntities.Clear();
                foreach (var pair in m_LastRatings)
                {
                    if (!m_Cur.Runs.ContainsKey(pair.Key))
                        m_ScratchEntities.Add(pair.Key);
                }
                for (int i = 0; i < m_ScratchEntities.Count; i++)
                    m_LastRatings.Remove(m_ScratchEntities[i]);
            }
            if (m_DampedTicks.Count > m_Cur.WriteValues.Count)
            {
                m_ScratchEntities.Clear();
                foreach (var pair in m_DampedTicks)
                {
                    if (!m_Cur.WriteValues.ContainsKey(pair.Key))
                        m_ScratchEntities.Add(pair.Key);
                }
                for (int i = 0; i < m_ScratchEntities.Count; i++)
                    m_DampedTicks.Remove(m_ScratchEntities[i]);
            }
            m_ScratchEntities.Clear();
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // The anchored write (§7): every tick (interval 16, right after vanilla AttractionSystem).
        // ─────────────────────────────────────────────────────────────────────────────────
        private void WriteProviders()
        {
            // §7.3: switched-off and orphan lodges lose their provider once (a structural change; once per
            // lodge toggle, never per tick).
            if (m_DropProvider.Count > 0)
            {
                for (int i = 0; i < m_DropProvider.Count; i++)
                {
                    DropEntry drop = m_DropProvider[i];
                    if (!EntityManager.Exists(drop.Lodge) || !EntityManager.HasComponent<AttractivenessProvider>(drop.Lodge))
                        continue;
                    EntityManager.RemoveComponent<AttractivenessProvider>(drop.Lodge);
                    Mod.Log.Info($"Attractiveness: provider removed from lodge {IdOf(drop.Lodge)} — " +
                                 $"{(drop.SwitchedOff ? "switched off" : "on no live Welcome Sign")} (no attraction while it is)");
                }
                m_DropProvider.Clear();
            }

            // §7.2 / §7.5: the written lodges.
            Dictionary<Entity, int> values = m_Cur.WriteValues;
            if (values.Count > 0)
            {
                bool toDetected = Mod.TourismOverhaulDetected;
                m_ScratchEntities.Clear();
                foreach (var pair in values)
                {
                    Entity lodge = pair.Key;
                    if (!EntityManager.Exists(lodge) || EntityManager.HasComponent<Deleted>(lodge))
                    {
                        m_ScratchEntities.Add(lodge);
                        continue;
                    }
                    if (!EntityManager.HasComponent<AttractivenessProvider>(lodge))
                    {
                        // The lodge now matches vanilla AttractionSystem's query; vanilla rewrites it from the
                        // lodge prefab (Attraction 0) per slice pass and our anchored write restores it.
                        EntityManager.AddComponentData(lodge, new AttractivenessProvider { m_Attractiveness = pair.Value });
                        if (m_ProviderAddedLogged.Add(lodge))
                            Mod.Log.Info($"Attractiveness: provider added to lodge {IdOf(lodge)} of area {IdOf(AreaOf(lodge))} (value {pair.Value})");
                        continue;
                    }
                    if (toDetected)
                        CheckDamping(lodge, pair.Value);
                    EntityManager.SetComponentData(lodge, new AttractivenessProvider { m_Attractiveness = pair.Value });
                }
                if (m_ScratchEntities.Count > 0)
                {
                    for (int i = 0; i < m_ScratchEntities.Count; i++)
                    {
                        values.Remove(m_ScratchEntities[i]);
                        m_Cur.LodgeArea.Remove(m_ScratchEntities[i]);
                    }
                    m_ScratchEntities.Clear();
                    m_Generation++;   // never hand a deleted lodge to the hub
                    // Round S1 (ch3 §2.1, R15): consumers re-copy; the deleted lodge stays listed until the next
                    // recompute (slot.Active keeps it; their lookups reject it).
                    m_LodgeSetRevision++;
                }
            }

            // §7.4: a sign carrying a provider is held at 0 after vanilla's write.
            for (int i = 0; i < m_ZeroSigns.Count; i++)
            {
                Entity sign = m_ZeroSigns[i];
                if (EntityManager.Exists(sign) && EntityManager.TryGetComponent(sign, out AttractivenessProvider provider) &&
                    provider.m_Attractiveness != 0)
                    EntityManager.SetComponentData(sign, new AttractivenessProvider { m_Attractiveness = 0 });
            }
        }

        /// <summary>§7.6: Tourism Overhaul holding a lodge below what we wrote, on 16 consecutive ticks →
        /// one line per lodge per load (vanilla's own slice write of 0 hits one tick in 16 and resets the run).</summary>
        private void CheckDamping(Entity lodge, int written)
        {
            int current = EntityManager.GetComponentData<AttractivenessProvider>(lodge).m_Attractiveness;
            if (current < written - Math.Max(1, written / 10))
            {
                m_DampedTicks.TryGetValue(lodge, out int ticks);
                ticks++;
                m_DampedTicks[lodge] = ticks;
                if (ticks >= Tuning.DampedTicksToLog && m_DampedLogged.Add(lodge))
                    Mod.Log.Info($"Attractiveness: lodge {IdOf(lodge)} of area {IdOf(AreaOf(lodge))} — Tourism Overhaul holds its provider at " +
                                 $"{current} of {written} (crowding or its park gate); the game sums the lower value");
            }
            else if (m_DampedTicks.ContainsKey(lodge))
            {
                m_DampedTicks.Remove(lodge);
            }
        }

        private Entity AreaOf(Entity lodge)
        {
            return m_Cur.LodgeArea.TryGetValue(lodge, out Entity sign) ? sign : Entity.Null;
        }

        private static string IdOf(Entity e)
        {
            return e.Index.ToString(CultureInfo.InvariantCulture) + ":" + e.Version.ToString(CultureInfo.InvariantCulture);
        }

        // ── DUMP (§13; hub dev tab through SkiResortStatsSystem.AppendDump) ────────────────────────

        /// <summary>The hub DUMP's attractiveness block: the system line, one line per area, and (S1
        /// addition) one line per member run with its five factors. Event time only, main thread (the
        /// provider reads complete their writers). Appends only; never throws (I-29).</summary>
        internal void AppendDump(StringBuilder sb)
        {
            try
            {
                double mean = m_CostCount > 0 ? m_CostSumMs / m_CostCount : 0.0;
                double pollMean = m_PollCount > 0 ? m_PollSumMs / m_PollCount : 0.0;
                sb.Append("  attractiveness: active ").Append(IsActive).Append(", disabled ").Append(m_Disabled)
                  .Append(", recomputes ").Append(m_Recomputes).Append(" (by reason: ");
                AppendCounts(sb, m_RecomputesByReason);
                sb.Append("), requests ").Append(m_Requests).Append(" (by reason: ");
                AppendCounts(sb, m_RequestsByReason);
                sb.Append("), last '").Append(m_LastReason).Append("', pending ")
                  .Append(m_StructuralPending ? "structural" : m_SoftPending ? "soft" : "none");
                if (m_StructuralPending || m_SoftPending)
                    sb.Append(" ('").Append(m_PendingReason).Append("')");
                sb.Append(", next backstop in ").Append(m_TicksUntilRefresh).Append(" tick(s), generation ").Append(m_Generation)
                  .Append(", lodge set revision ").Append(m_LodgeSetRevision)   // Round S1 (ch3 §2.1)
                  .Append(", cost mean ").Append(mean.ToString("F3", CultureInfo.InvariantCulture))
                  .Append(" ms max ").Append(m_CostMaxMs.ToString("F3", CultureInfo.InvariantCulture))
                  .Append(" ms at ").Append(MonthClockText(m_CostMaxMonth, m_CostMaxMinute))   // Round M2 (D8); "-" before any
                  .Append(" (first after load ").Append(m_FirstRecomputeMs < 0.0 ? "-" : m_FirstRecomputeMs.ToString("F3", CultureInfo.InvariantCulture))
                  .Append(" ms, apart), poll mean ").Append(pollMean.ToString("F3", CultureInfo.InvariantCulture))
                  .Append(" ms, positions read ").Append(m_PositionReads).Append(" time(s) (structural or backstop; ")
                  .Append(m_PositionFallbacks).Append(" fallback read(s) on a soft recompute)")
                  .Append(", efficiency-only area lines skipped ").Append(m_EfficiencyLinesSkipped).Append('\n');   // Round S1 F4

                for (int s = 0; s < m_Cur.SlotCount; s++)
                {
                    AreaSlot slot = m_Cur.Slots[s];
                    SkiResortScore.Result r = slot.Result;
                    int holdTicks = slot.Held && m_AreaStates.TryGetValue(slot.Sign, out AreaState state) ? state.HoldTicks : 0;
                    sb.Append("    area ").Append(IdOf(slot.Sign)).Append(": A ").Append(r.Attraction)
                      .Append(" (full ").Append(r.AttractionFullEfficiency).Append("), held ").Append(slot.Held)
                      .Append(" (").Append(holdTicks).Append(" ticks), efficiency ").Append(slot.EfficiencyPct)
                      .Append(" %, lodges ").Append(r.ActiveLodges).Append('+').Append(r.InactiveLodges).Append(" × ").Append(r.PerLodge)
                      .Append(", runs ").Append(r.RunsCounted).Append('/').Append(r.Runs).Append(" (").Append(r.RunsOpen)
                      .Append(" open, ").Append(r.RunsPending).Append(" unrated), lifts ").Append(r.LiftsCounted).Append('/')
                      .Append(r.Lifts).Append(", flags ").Append(r.Flags).Append(", provider now ");
                    int damped = 0;
                    for (int i = 0; i < slot.Active.Count; i++)
                    {
                        Entity lodge = slot.Active[i];
                        sb.Append(i > 0 ? ", " : "").Append(IdOf(lodge)).Append('=');
                        if (EntityManager.Exists(lodge) && EntityManager.TryGetComponent(lodge, out AttractivenessProvider provider))
                            sb.Append(provider.m_Attractiveness);
                        else
                            sb.Append("none");
                        if (m_DampedLogged.Contains(lodge))
                            damped++;
                    }
                    if (slot.Active.Count == 0)
                        sb.Append('-');
                    sb.Append(", TO-damped ").Append(damped).Append(" lodge(s)\n");
                    AppendLadder(sb, slot);   // Catalog Phase B

                    for (int i = 0; i < slot.RunEntities.Count && i < slot.Parts.Count; i++)
                        AppendRun(sb, slot.RunEntities[i], slot.Runs[i], slot.Parts[i]);
                }
            }
            catch (Exception e)
            {
                sb.Append("  attractiveness: DUMP failed (").Append(e.GetType().Name).Append(": ").Append(e.Message).Append(")\n");
            }
        }

        /// <summary>Catalog Phase B (round-2-contract §7.4): one DUMP line per area — the ladder (best tier, winter base,
        /// the season's applied value and extras, price tolerance), each active lodge's tier with its SummitServed flag, the
        /// school, the patrol and the per-type lift sums.</summary>
        private static void AppendLadder(StringBuilder sb, AreaSlot slot)
        {
            CultureInfo ic = CultureInfo.InvariantCulture;
            SkiResortScore.Result r = slot.Result;
            sb.Append("      ladder: best ").Append(r.LodgeBestIndex >= 0 ? LodgeName(r.LodgeBestIndex) : "-")
              .Append(", base ").Append(r.LodgeBase.ToString("0.##", ic)).Append(" (winter), applied ").Append(r.LodgeApplied.ToString("0.##", ic))
              .Append(" (extras ").Append(r.LodgeExtraApplied.ToString("0.##", ic)).Append(')')
              .Append(", price tolerance ").Append(r.PriceTolerance).Append("; lodges:");
            if (slot.ActiveInfo.Count == 0)
                sb.Append(" -");
            for (int i = 0; i < slot.ActiveInfo.Count; i++)
            {
                LodgeInfo info = slot.ActiveInfo[i];
                sb.Append(i > 0 ? "," : "").Append(' ').Append(IdOf(info.Lodge)).Append(' ').Append(LodgeName(info.LodgeIndex));
                if (info.SummitServed)
                    sb.Append(" summit-served");
            }
            sb.Append("; school ").Append(r.SchoolActive ? "on" : "off").Append(" (").Append(slot.Schools).Append(" listed, green counted ")
              .Append(r.GreenCounted ? "y" : "n").Append(", +").Append(r.SchoolValue.ToString("0.##", ic)).Append("); patrols ")
              .Append(slot.PatrolEntities.Count).Append(" (").Append(slot.PatrolXz.Count).Append(" with a position), runs patrolled ")
              .Append(r.RunsPatrolled).Append("; lifts value ").Append(r.LiftsValue.ToString("0.##", ic)).Append(", applied ")
              .Append(r.LiftsApplied.ToString("0.##", ic)).Append('\n');
        }

        private static void AppendRun(StringBuilder sb, Entity run, SkiResortScore.RunInput input, SkiResortScore.RunPart part)
        {
            string kind = input.Kind == SkiResortScore.KindSnowmaking ? "snowmaking"
                        : input.Kind == SkiResortScore.KindNight ? "night"
                        : input.Kind == SkiResortScore.KindNatural ? "natural" : "groomed";
            string band = input.State == SkiResortScore.BandState.Pending ? "unrated"
                        : input.State == SkiResortScore.BandState.Unrateable ? "unrateable"
                        : input.Band == SkiResortScore.BandGreen ? "green"
                        : input.Band == SkiResortScore.BandBlue ? "blue"
                        : input.Band == SkiResortScore.BandBlack ? "black"
                        : input.Band == SkiResortScore.BandDoubleBlack ? "double black" : "flat";
            sb.Append(Invariant($"      run {run.Index}:{run.Version} {kind} {band}, {input.AreaM2:F0} m²: band {part.BandValue:0.##} × size {part.SizeFactor:F3} × ") +
                      Invariant($"type {part.TypeFactor:0.##} × served {part.ServedFactor:0.##} × open {part.OpenFactor:F2}") +
                      (input.OpenEstimated ? " (estimated)" : "") +
                      Invariant($" = {part.Value:F3}") + (part.Counted ? "" : " (not counted)") +
                      (input.Patrolled ? " (patrolled)" : "") + "\n");   // Catalog Phase B (R13)
        }

        /// <summary>Round M2 (D8): "YYYY-MM HH:MM" from a SkiMoneyRules month key and a SkiDayTiming.MinuteOfDay
        /// minute (invariant culture); "-" before any (NoMonth or a negative minute).</summary>
        private static string MonthClockText(int monthKey, int minute)
        {
            if (monthKey == SkiDayRules.NoMonth || minute < 0)
                return "-";
            return SkiMoneyRules.YearOfKey(monthKey).ToString(CultureInfo.InvariantCulture) + "-" +
                   SkiMoneyRules.MonthOfKey(monthKey).ToString("D2", CultureInfo.InvariantCulture) + " " +
                   (minute / 60).ToString("D2", CultureInfo.InvariantCulture) + ":" +
                   (minute % 60).ToString("D2", CultureInfo.InvariantCulture);
        }

        private static void AppendCounts(StringBuilder sb, Dictionary<string, int> counts)
        {
            bool any = false;
            foreach (var pair in counts)
            {
                sb.Append(any ? ", " : "").Append(pair.Key).Append(' ').Append(pair.Value);
                any = true;
            }
            if (!any)
                sb.Append('-');
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // Prefab state — Catalog Phase A (F5; was :2037-2091 ResolvePrefabs / ResolveSet, the PrefabID + name-scan
        // copy, once per APP RUN): SkiPrefabCatalogSystem resolves per load and its "PrefabCatalog [arm]" line names
        // every lodge FOUND / MISSING; this system still says, per load, whether it is inert (tolerated pre-import).
        // ─────────────────────────────────────────────────────────────────────────────────
        private void LogLodgePrefabs()
        {
            // Catalog Phase B (round-2-contract D5): a pending name (not imported yet) is listed apart from an expected
            // one that is missing; the school and the patrol huts are listed too (they change the score once imported).
            var found = new List<string>(SkiCatalog.Lodges.Count);
            var missing = new List<string>(SkiCatalog.Lodges.Count);
            var pending = new List<string>(SkiCatalog.Lodges.Count + 3);
            var upgrades = new List<string>(3);
            for (int i = 0; i < SkiCatalog.Lodges.Count; i++)
            {
                string name = SkiCatalog.Lodges[i].Prefab;
                if (m_PrefabCatalog.EntityOf(name) != Entity.Null)
                    found.Add(name);
                else if (SkiCatalog.IsExpected(name))
                    missing.Add(name);
                else
                    pending.Add(name);
            }
            for (int i = 0; i < SkiCatalog.Schools.Count + SkiCatalog.Patrols.Count; i++)
            {
                string name = i < SkiCatalog.Schools.Count ? SkiCatalog.Schools[i].Prefab : SkiCatalog.Patrols[i - SkiCatalog.Schools.Count].Prefab;
                if (m_PrefabCatalog.EntityOf(name) != Entity.Null)
                    upgrades.Add(name);
                else if (SkiCatalog.IsExpected(name))
                    missing.Add(name);
                else
                    pending.Add(name);
            }
            string pendingText = pending.Count > 0 ? $"; pending (not imported yet): '{string.Join("', '", pending.ToArray())}'" : "";

            if (m_PrefabCatalog.LodgeCount == 0)
            {
                var all = new List<string>(missing);
                all.AddRange(pending);
                Mod.Log.Info("Attractiveness: lodge prefab not present — system inert until " +
                             $"'{string.Join("', '", all.ToArray())}' is imported (expected pre-import)");
                return;
            }
            Mod.Log.Info($"Attractiveness: lodge prefab(s) '{string.Join("', '", found.ToArray())}' from the prefab catalog" +
                         (upgrades.Count > 0 ? $"; school / patrol prefab(s) '{string.Join("', '", upgrades.ToArray())}'" : "") +
                         (missing.Count > 0 ? $"; not found (tolerated): '{string.Join("', '", missing.ToArray())}'" : "") + pendingText);
        }

        /// <summary>Catalog Phase B: the [arm] line's model numbers, every one from SkiCatalog (invariant culture): the
        /// ladder rows, its area rules, the per-type lift values and summer shares, the school and the patrol.</summary>
        private static string ArmModelText()
        {
            CultureInfo ic = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(640);
            sb.Append("lodges by tier: lodge base = B(best) + min(sum E(others), ").Append(SkiCatalog.Ladder.ExtraCap.ToString("0", ic)).Append(") (");
            for (int i = 0; i < SkiCatalog.Lodges.Count; i++)
            {
                LodgeDef d = SkiCatalog.Lodges[i];
                sb.Append(i > 0 ? ", " : "").Append(d.Id).Append(' ').Append(d.B.ToString("0.##", ic)).Append('/').Append(d.E.ToString("0.##", ic))
                  .Append(" mu ").Append(d.Mu.ToString("0.##", ic));
                if (d.MuSummit > d.Mu)
                    sb.Append(" (").Append(d.MuSummit.ToString("0.##", ic)).Append(" summit-served)");
                if (d.Luxury)
                    sb.Append(" luxury");
            }
            sb.Append("); lodge applied = the base in winter, in summer best B × mu + the others' E × mu (scaled when sum E > ")
              .Append(SkiCatalog.Ladder.ExtraCap.ToString("0", ic)).Append("); no limit on the number of lodges of any tier; summit-served = within ").Append(SkiCatalog.Ladder.SummitServedRadiusM.ToString("0", ic))
              .Append(" m of a complete, powered lift's top; lifts (complete and powered) value / summer share: ");
            for (int i = 0; i < SkiCatalog.Lifts.Count; i++)
            {
                LiftTypeDef l = SkiCatalog.Lifts[i];
                sb.Append(i > 0 ? ", " : "").Append(l.Id).Append(' ').Append(l.ScoreValue.ToString("0.##", ic)).Append(" / ")
                  .Append(l.SummerShare.ToString("0.##", ic));
            }
            sb.Append("; school +").Append(SchoolRule.ScoreValue.ToString("0.##", ic)).Append(" to S while a green run counts; patrol: expert runs within ")
              .Append(PatrolRule.RadiusM.ToString("0", ic)).Append(" m ×").Append(PatrolRule.ExpertFactor.ToString("0.##", ic))
              .Append(", an unserved patrolled run served at least ").Append((PatrolRule.BackcountryServedMin * 100.0).ToString("0", ic)).Append(" %");
            return sb.ToString();
        }
    }
}
