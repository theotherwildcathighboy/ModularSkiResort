// The C# side of the Ski Resort hub: every binding and trigger of the "skiResort" group. The TS side, mod/SkiResortUI/src/bindings.ts,
// is the single source of truth for names and shapes; tools/check-bundle.js matches this file's literals and TypeBegin blocks (R1, R2, R16).
// Per-area numbers come from SkiResortStatsSystem; this system only writes its rows to the UI. Bindings use AddBinding, never
// AddUpdateBinding (it re-sends a raw binding every frame; UISystemBase.cs:48-58). Every trigger body has its own try/catch (the
// binding logs a trigger exception to the vanilla UI log, not ours) and QUEUES the request for OnUpdate (main thread).
// Phase (Mod.cs): UpdateAt UIUpdate after SkiRunCatalogSystem and SkiResortStatsSystem (add order, UpdateSystem.cs:158-161), paused
// included. A one-shot "!!!" catch pushes contract = ContractAbsent (0) and refuses every trigger until the next load.
// Engine notes: EngineNotes/SkiResortHubUISystem.md

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Colossal.Entities;            // EntityManager.TryGetComponent / HasEnabledComponent extensions
using Colossal.Mathematics;        // MathUtils.Extents, Bounds3.xz
using Colossal.Serialization.Entities;
using Colossal.UI.Binding;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Game.SceneFlow;
using Game.Tools;
using Game.UI;
using Game.UI.InGame;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiResortHubUISystem : UISystemBase
    {
        /// <summary>Binding group (bindings.ts GROUP).</summary>
        public const string Group = "skiResort";

        /// <summary>The Ski Bus card refreshes twice a second at 60 fps.</summary>
        private const int BusCardRefreshFrames = 30;
        // bindings.ts BusCardStateName.
        private const string BusCardRunning = "running", BusCardNoRoad = "noRoad", BusCardNoHillLodge = "noHillLodge", BusCardOff = "off";

        /// <summary>The contract number (bindings.ts HUB_CONTRACT). Bump both together whenever a binding, trigger, field or meaning
        /// changes (Engine notes: Near HubContract lists what each number appended).</summary>
        public const int HubContract = 32;

        /// <summary>The `contract` value the UI reads as "C# absent" (bindings.ts CONTRACT_ABSENT). Pushed after the one-shot "!!!"
        /// until the next load: the UI then shows its "not connected" notice instead of launchers that do nothing.</summary>
        public const int ContractAbsent = 0;

        /// <summary>Prefix of a UI-only build's stamp (bindings.ts STANDALONE_STAMP_PREFIX).</summary>
        public const string StandaloneStampPrefix = "ui-";

        // Build[].reason locale keys (root LocaleEN.cs).
        public const string ReasonKeyLocked = "SkiResort.Hub.REASON[Locked]";
        public const string ReasonKeyNoSign = "SkiResort.Hub.REASON[NoSign]";
        public const string ReasonKeyMissing = "SkiResort.Hub.REASON[Missing]";
        // Reason byte 4 is AlreadyBuilt (Engine notes: Launching, Locks); byte 5 (OneLuxury) is retired.
        public const string ReasonKeyAlreadyBuilt = "SkiResort.Hub.REASON[AlreadyBuilt]";

        // Build[].hint locale key (root LocaleEN.cs).
        public const string HintKeyLiftNoPair = "SkiResort.Hub.HINT[LiftNoPair]";

        /// <summary>ResortRow.kind of the "Not linked" row (always last, always sent).</summary>
        public const int ResortKindNotLinked = 1;

        private static class Tuning
        {
            /// <summary>Seconds after the session's first ARM with no uiHello before the one WARN (the UI module loads at game start, so a
            /// working UI has said hello long before this).</summary>
            public const float NoHelloWarnS = 30f;
            /// <summary>Wall-clock period (s) of the Locked re-check while the build section is open
            /// (an unlock raises no event we listen to).</summary>
            public const float LockRefreshS = 2f;
            /// <summary>"Hub [items]" lines (the build items re-resolved after a prefab catalog re-resolution) written per load; the DUMP's
            /// PrefabCatalog line counts the rest.</summary>
            public const int CatalogItemLinesMax = 8;
                /// <summary>Requests queued between two OnUpdates before further ones are dropped (logged).</summary>
            public const int QueueCapacity = 32;
            /// <summary>Distinct uiError messages logged per load.</summary>
            public const int UiErrorsMax = 32;
            /// <summary>uiError / handshake / layout text cut to this length in the log.</summary>
            public const int TextMax = 400;
            /// <summary>Focus recipe (DebugSystemSearch.cs:634-635, 1.6.2 :655-656: pitch 45°, zoom × 2.5).</summary>
            public const float FocusZoomFactor = 2.5f;
            public const float FocusPitchDeg = 45f;
            /// <summary>Minimum zoom (m) when flying to a sign, lodge or station *(tunable, unmeasured)*.</summary>
            public const float FocusBuildingMinZoomM = 150f;
            /// <summary>Minimum zoom (m) when flying to a whole lift *(tunable, unmeasured)*.</summary>
            public const float FocusLiftMinZoomM = 200f;
            /// <summary>Zoom (m) when flying to a lift's gap *(tunable, unmeasured)*.</summary>
            public const float FocusGapZoomM = 250f;
            /// <summary>Longest name rename accepts (UTF-16 units): the vanilla name field's maxLength
            /// (installed index.js@1009013, Engine notes: Rename).</summary>
            public const int RenameMaxChars = 64;
            /// <summary>Hub [layout] lines per load (one per distinct measurement; the UI sends on a text-scale or row-bucket change and
            /// re-sends on every view change, five views × text scales): a cap so a UI bug cannot flood.</summary>
            public const int LayoutLinesMax = 64;
            /// <summary>Hub [bind-notice] lines per stats rows change (the rest are summed in one line).</summary>
            public const int BindNoticesPerPass = 4;
            /// <summary>An array push slower than this (ms) is logged once per load per array.</summary>
            public const float SlowPushMs = 5f;
            /// <summary>Each array's `Hub [push] … rows=` line goes through its own LogWindow of this many wall-clock seconds: `problems`
            /// follows simulation state (efficiency factors crossing 100 % in a brownout), so its row count can move every poll.</summary>
            public const float PushRowsLogWindowS = 10f;
            /// <summary>Wall-clock seconds between two `history` pushes caused only by the month in progress moving (a stored month or an
            /// area change pushes at once).</summary>
            public const float HistoryProgressPushS = 5f;
        }

        // Build items: the one table SkiCatalog.BuildItems; bindings.ts BUILD_ITEM_IDS / BUILD_ITEM_PREFAB and buildItems.ts
        // BUILD_ITEMS mirror it row by row (gate R16 parses SkiCatalog.cs); the UI groups the tiles itself

        // Every item fact comes from its BuildItemDef row (kind, prefab name, lift type, run kind); the PrefabID type is
        // SkiCatalog.TypeNameOf(kind). Static readonly: SkiCatalog's static ctor never throws (a fault leaves Problems, and the ARMED
        // line names them).
        private static readonly IReadOnlyList<BuildItemDef> Items = SkiCatalog.BuildItems;
        private static readonly int ItemCount = SkiCatalog.BuildItems.Count;
        /// <summary>The table's ids (BuildItemIds, the dev tab's launch buttons), built once.</summary>
        private static readonly string[] s_ItemIds = CopyItemIds();

        private static string[] CopyItemIds()
        {
            var ids = new string[SkiCatalog.BuildItems.Count];
            for (int i = 0; i < ids.Length; i++)
                ids[i] = SkiCatalog.BuildItems[i].Id;
            return ids;
        }

        /// <summary>Each item's lift type (SkiCatalog.Lifts index; −1 = not a station / line item), built once.</summary>
        private static readonly int[] s_ItemTypeIndex = CopyItemTypeIndex();

        private static int[] CopyItemTypeIndex()
        {
            var types = new int[SkiCatalog.BuildItems.Count];
            for (int i = 0; i < types.Length; i++)
            {
                LiftTypeDef def = SkiCatalog.LiftById(SkiCatalog.BuildItems[i].LiftTypeId);
                types[i] = def != null ? def.Index : -1;
            }
            return types;
        }

        private static bool IsRunItem(int i) => Items[i].Kind == BuildItemKind.Run;
        private static bool IsLineItem(int i) => Items[i].Kind == BuildItemKind.Line;
        private static bool IsCatTrackItem(int i) => Items[i].Prefab == SkiCatalog.Names.RunCatTrack;

        private const byte ReasonNone = 0, ReasonLocked = 1, ReasonNoSign = 2, ReasonMissing = 3,
                           ReasonAlreadyBuilt = 4;   // 4; 5 (OneLuxury) is retired, never reused

        private readonly PrefabBase[] m_ItemPrefab = new PrefabBase[ItemCount];
        private readonly Entity[] m_ItemPrefabEntity = new Entity[ItemCount];
        // CopyTopLevelLinks scratch (event time: locks, launches, Not linked check).
        private readonly List<Entity> m_SchoolScratch = new List<Entity>(2);
        private readonly List<Entity> m_SchoolSignScratch = new List<Entity>(2);
        private readonly bool[] m_ItemLocked = new bool[ItemCount];
        private readonly byte[] m_ItemReason = new byte[ItemCount];
        private readonly int[] m_ItemPrice = new int[ItemCount];
        // Per-item values read once at arm time (ReadItemValues). −1 = not applicable / unknown.
        private readonly int[] m_ItemPricePerHa = new int[ItemCount];
        private readonly int[] m_ItemCost = new int[ItemCount];
        private readonly int[] m_ItemCostPerKm = new int[ItemCount];
        private readonly int[] m_ItemMonthly = new int[ItemCount];
        private readonly int[] m_ItemMonthlyPerHa = new int[ItemCount];
        private readonly int[] m_ItemMonthlyPerKm = new int[ItemCount];
        // Offered this load; the row's ForbidMultiple for the AlreadyBuilt lock; the
        // row's luxury label (the DUMP only: no lock hangs on it); the per-load "drift logged" marks (one WARN per item per load).
        private readonly bool[] m_ItemSent = new bool[ItemCount];
        private readonly bool[] m_ItemForbidMultiple = new bool[ItemCount];
        private readonly bool[] m_ItemLuxury = new bool[ItemCount];
        private readonly bool[] m_ItemDriftLogged = new bool[ItemCount];
        private int m_SentCount;
        private bool m_QueueDropLogged, m_UnknownLaunchLogged;   // per load
        private readonly HashSet<string> m_WriterFailed = new HashSet<string>();   // per load: raw binding writers that threw
        private bool m_SentMismatchLogged;      // per load: WriteBuild's never-silent guard
        private Entity m_LockSign = Entity.Null;   // the shown sign the locks were evaluated for (a switch re-checks)
        private int m_ActiveItem = -1;

        /// <summary>bindings.ts BusCardState: the card beside the game's panel for a selected coach or depot. GuestsMonth -1 = unknown,
        /// DistanceM = straight line to the hill lodge, what the bill counts (0 = unknown), State = bindings.ts BusCardStateName.</summary>
        public struct HubBusCardState : IJsonWritable, IEquatable<HubBusCardState>
        {
            public bool Shown;
            public string Area;
            public int GuestsMonth;
            public int BillMonth;
            public int DistanceM;
            public string State;

            public void Write(IJsonWriter writer)
            {
                writer.TypeBegin("SkiResortMod.BusCardState");
                writer.PropertyName("shown");
                writer.Write(Shown);
                writer.PropertyName("area");
                writer.Write(Area ?? "");
                writer.PropertyName("guestsMonth");
                writer.Write(GuestsMonth);
                writer.PropertyName("billMonth");
                writer.Write(BillMonth);
                writer.PropertyName("distanceM");
                writer.Write(DistanceM);
                writer.PropertyName("state");
                writer.Write(State ?? "");
                writer.TypeEnd();
            }

            public bool Equals(HubBusCardState other)
            {
                return Shown == other.Shown && GuestsMonth == other.GuestsMonth && BillMonth == other.BillMonth && DistanceM == other.DistanceM &&
                       string.Equals(Area ?? "", other.Area ?? "", StringComparison.Ordinal) &&
                       string.Equals(State ?? "", other.State ?? "", StringComparison.Ordinal);
            }

            public override bool Equals(object obj) => obj is HubBusCardState other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = Shown ? 1 : 0;
                    h = h * 31 + GuestsMonth;
                    h = h * 31 + BillMonth;
                    h = h * 31 + DistanceM;
                    h = h * 31 + (Area ?? "").GetHashCode();
                    return h * 31 + (State ?? "").GetHashCode();
                }
            }
        }

        /// <summary>bindings.ts GladeState. Reason is a locale key constant (reference-stable).</summary>
        public struct HubGladeState : IJsonWritable, IEquatable<HubGladeState>
        {
            public bool Available;
            public bool Active;
            public int Level;
            public bool TreeLine;
            public string Reason;

            public HubGladeState(bool available, bool active, int level, bool treeLine, string reason)
            {
                Available = available;
                Active = active;
                Level = level;
                TreeLine = treeLine;
                Reason = reason ?? "";
            }

            public void Write(IJsonWriter writer)
            {
                writer.TypeBegin("SkiResortMod.GladeState");
                writer.PropertyName("available");
                writer.Write(Available);
                writer.PropertyName("active");
                writer.Write(Active);
                writer.PropertyName("level");
                writer.Write(Level);
                writer.PropertyName("treeLine");
                writer.Write(TreeLine);
                writer.PropertyName("reason");
                writer.Write(Reason ?? "");
                writer.TypeEnd();
            }

            public bool Equals(HubGladeState other)
            {
                return Available == other.Available && Active == other.Active && Level == other.Level &&
                       TreeLine == other.TreeLine && string.Equals(Reason, other.Reason, StringComparison.Ordinal);
            }

            public override bool Equals(object obj) => obj is HubGladeState other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = Available ? 1 : 0;
                    h = h * 31 + (Active ? 1 : 0);
                    h = h * 31 + Level;
                    h = h * 31 + (TreeLine ? 1 : 0);
                    return h * 31 + (Reason == null ? 0 : Reason.GetHashCode());
                }
            }
        }

        /// <summary>bindings.ts LiftLineState: the lift-line generator's state for the Lifts tab toggle. State is the
        /// LiftLineToolSystem.ToolState name (a cached enum string, reference-stable).</summary>
        public struct HubLiftLineState : IJsonWritable, IEquatable<HubLiftLineState>
        {
            public bool Available;
            public bool Active;
            public string State;

            public HubLiftLineState(bool available, bool active, string state)
            {
                Available = available;
                Active = active;
                State = state ?? "";
            }

            public void Write(IJsonWriter writer)
            {
                writer.TypeBegin("SkiResortMod.LiftLineState");
                writer.PropertyName("available");
                writer.Write(Available);
                writer.PropertyName("active");
                writer.Write(Active);
                writer.PropertyName("state");
                writer.Write(State ?? "");
                writer.TypeEnd();
            }

            public bool Equals(HubLiftLineState other) =>
                Available == other.Available && Active == other.Active && string.Equals(State, other.State, StringComparison.Ordinal);

            public override bool Equals(object obj) => obj is HubLiftLineState other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = Available ? 1 : 0;
                    h = h * 31 + (Active ? 1 : 0);
                    return h * 31 + (State == null ? 0 : State.GetHashCode());
                }
            }
        }

        /// <summary>`city` writer (bindings.ts CityStats). Ours, so the TypeBegin name and the shape the
        /// gate checks live in this file (Engine notes: Bindings).</summary>
        private sealed class CityStatsWriter : IWriter<CityStats>
        {
            public void Write(IJsonWriter writer, CityStats value)
            {
                writer.TypeBegin("SkiResortMod.CityStats");
                writer.PropertyName("known");
                writer.Write(value.Known);
                writer.PropertyName("daysPerYear");
                writer.Write(Fin(value.DaysPerYear));
                writer.PropertyName("scaleK");
                writer.Write(Fin(value.ScaleK));
                writer.PropertyName("backcountryPct");
                writer.Write(Fin(value.BackcountryPct));
                writer.PropertyName("ticketPrice");
                writer.Write(Fin(value.TicketPrice));
                writer.PropertyName("snowGating");
                writer.Write(value.SnowGating);
                writer.PropertyName("snowmakingMarginC");
                writer.Write(Fin(value.SnowmakingMarginC));
                writer.PropertyName("snowVisualsOff");
                writer.Write(value.SnowVisualsOff);
                writer.PropertyName("tourismOverhaul");
                writer.Write(value.TourismOverhaul);
                writer.PropertyName("realisticWorkplaces");
                writer.Write(value.RealisticWorkplaces);
                // "" = the game's own trip systems; "?" = SkiTripSystems.UnknownLabel (bindings.ts TRIP_PLANNER_UNKNOWN)
                writer.PropertyName("tripPlanner");
                writer.Write(value.TripPlanner ?? "");
                writer.PropertyName("tripPlannerBlocksSkiDays");
                writer.Write(value.TripPlannerBlocksSkiDays);
                // The booking setting and the pass share (0 = passes off).
                writer.PropertyName("bookTicketsAsFees");
                writer.Write(value.BookTicketsAsFees);
                writer.PropertyName("passShare");
                writer.Write(Fin(value.PassShare));
                // The calendar month 0..11 (the UI names it with vanilla Common.MONTH:<n>).
                writer.PropertyName("month");
                writer.Write(Fin(value.Month));
                writer.TypeEnd();
            }
        }

        /// <summary>`city` dedup: all fifteen fields (typed Equals per field — no boxing; floats with .Equals; the
        /// string with ==).</summary>
        private sealed class CityStatsComparer : EqualityComparer<CityStats>
        {
            public override bool Equals(CityStats a, CityStats b)
            {
                return a.Known == b.Known && a.DaysPerYear == b.DaysPerYear && a.ScaleK == b.ScaleK &&
                       a.BackcountryPct == b.BackcountryPct && a.TicketPrice == b.TicketPrice && a.SnowGating == b.SnowGating &&
                       a.SnowmakingMarginC.Equals(b.SnowmakingMarginC) && a.SnowVisualsOff == b.SnowVisualsOff &&
                       a.TourismOverhaul == b.TourismOverhaul && a.RealisticWorkplaces == b.RealisticWorkplaces &&
                       a.TripPlanner == b.TripPlanner && a.TripPlannerBlocksSkiDays == b.TripPlannerBlocksSkiDays &&
                       a.BookTicketsAsFees == b.BookTicketsAsFees && a.PassShare == b.PassShare &&
                       a.Month == b.Month;
            }

            public override int GetHashCode(CityStats c)
            {
                unchecked
                {
                    int h = c.Known ? 1 : 0;
                    h = h * 31 + c.DaysPerYear;
                    h = h * 31 + c.ScaleK;
                    h = h * 31 + c.BackcountryPct;
                    h = h * 31 + c.TicketPrice;
                    h = h * 31 + (c.SnowGating ? 1 : 0);
                    h = h * 31 + c.SnowmakingMarginC.GetHashCode();
                    h = h * 31 + (c.SnowVisualsOff ? 1 : 0);
                    h = h * 31 + (c.TourismOverhaul ? 1 : 0);
                    h = h * 31 + (c.RealisticWorkplaces ? 1 : 0);
                    h = h * 31 + (c.TripPlanner ?? "").GetHashCode();
                    h = h * 31 + (c.TripPlannerBlocksSkiDays ? 1 : 0);
                    h = h * 31 + (c.BookTicketsAsFees ? 1 : 0);
                    h = h * 31 + c.PassShare;
                    return h * 31 + c.Month;
                }
            }
        }

        private static int s_NonFinite;

        private static int Fin(int v) => v;

        private static float Fin(float v)
        {
            if (!float.IsNaN(v) && !float.IsInfinity(v))
                return v;
            s_NonFinite++;
            return 0f;
        }

        private static double Fin(double v)
        {
            if (!double.IsNaN(v) && !double.IsInfinity(v))
                return v;
            s_NonFinite++;
            return 0d;
        }

        private sealed class ArrayPush
        {
            public readonly string Name;
            public RawValueBinding Binding;
            public bool WasActive;
            public int PushedRevision = int.MinValue;
            public int LoggedRows = -1;
            public long Pushes;
            public float LastMs, MaxMs;
            public bool SlowLogged;
            /// <summary>Folds the row-count lines (slot 0 rows, slot 1 push ms).</summary>
            public readonly LogWindow RowsLog = new LogWindow(Tuning.PushRowsLogWindowS);

            public ArrayPush(string name)
            {
                Name = name;
            }

            /// <summary>A load: the caller flushed RowsLog first (FlushPushRows).</summary>
            public void Reset()
            {
                WasActive = false;
                PushedRevision = int.MinValue;
                LoggedRows = -1;
                SlowLogged = false;
                RowsLog.Reset();
            }
        }

        private readonly ArrayPush m_ResortsPush = new ArrayPush("resorts");
        private readonly ArrayPush m_ResortLivePush = new ArrayPush("resortLive");
        private readonly ArrayPush m_RunsPush = new ArrayPush("runs");
        private readonly ArrayPush m_RunLivePush = new ArrayPush("runLive");
        private readonly ArrayPush m_LiftsPush = new ArrayPush("lifts");
        private readonly ArrayPush m_ProblemsPush = new ArrayPush("problems");
        private readonly ArrayPush m_HistoryPush = new ArrayPush("history");
        private readonly ArrayPush m_LodgeParkingPush = new ArrayPush("lodgeParking");   // contract 29
        private readonly ArrayPush m_MergedPush = new ArrayPush("mergedLodges");   // contract 32

        private enum RequestKind : byte
        {
            ToggleHub, SetHubOpen, Hello, UiError, Launch, Focus, GladeToggle, GladeLevel, GladeTreeLine, Dump,
            SelectResort, Rename, FocusLift, FocusBuilding, Layout,
            SetPisteMap, TogglePisteMap,   // TogglePisteMap is the dev tab's only
            LiftLineToggle,
            QuickRunsToggle, QuickRunsSet,   // SkiResortHubUISystem.QuickRuns.cs
            DeleteCatTrack,                  // contract 18: the cat track row's trash button
            SetCatTrackDeleteMode,           // contract 19: the Runs tab's Delete cat tracks chip
            SetRunPropSpacing,               // contract 28: the Runs view's prop rows (Bool = lights, Int = meters)
            MergeBuilding, UnmergeBuilding,  // contract 32: the merge card (Int = tier); SkiResortHubUISystem.Merge.cs
        }

        private struct Request
        {
            public RequestKind Kind;
            public bool Bool;
            public int Int;
            public string Text;
            public Entity Entity;
            public Entity Entity2;   // focusLift: the row's top station (may be Null)
            public string Source;    // "ui" or "dev tab"
        }

        private readonly Request[] m_Queue = new Request[Tuning.QueueCapacity];
        private int m_QueueCount;

        private SkiRunPropSystem m_RunProps;
        private ToolSystem m_ToolSystem;
        private DefaultToolSystem m_DefaultTool;
        private ObjectToolSystem m_ObjectTool;
        private SelectedInfoUISystem m_SelectedInfo;
        private CameraUpdateSystem m_CameraUpdateSystem;
        private PrefabSystem m_PrefabSystem;
        private Game.Simulation.ClimateSystem m_ClimateSystem;   // the ski season's first month in History (SkiClimateCalendar)
        private NameSystem m_NameSystem;
        private SkiRunOwnershipSystem m_Ownership;
        private SkiRunBindTriggerSystem m_BindTrigger;   // PickSignName (empty sign rename)
        private SkiRunCatalogSystem m_Catalog;
        private SkiResortStatsSystem m_Stats;
        // The stored months, the ledger's month in progress, its ski days and the calendar.
        private SkiResortHistorySystem m_HistorySystem;
        private SkiGuestCounterSystem m_GuestCounter;
        private SkiDaySystem m_SkiDays;
        private Game.Simulation.TimeSystem m_Time;
        private LiftCensusSystem m_Census;
        private SkiResortAttractivenessSystem m_Attractiveness;
        // The build items, the stations and the DUMP line come from the prefab catalog.
        private SkiPrefabCatalogSystem m_PrefabCatalog;
        private SkiPlacementInfoviewSystem m_Infoview;
        private SkiGladeToolSystem m_GladeTool;
        private LiftLineToolSystem m_LiftLineTool;   // the Lifts tab's lift generator (public API only; the lift session owns it)
        private CatTrackGradeToolSystem m_GradeTool; // the cat track tool the Cat track tile opens (public API only)
        private SkiCatTrackDeleteSystem m_CatTrackDelete;
        private SkiPisteMapSystem m_PisteMap;        // DUMP only
        private LodgeRoadSystem m_LodgeRoad;                  // DUMP only
        private EntityQuery m_CitizenQuery;          // DUMP only (citizens at a lodge / sign)
        private EntityQuery m_ConsumerQuery;         // DUMP only (station ElectricityConsumer)

        private ValueBinding<bool> m_HubOpen;
        private ValueBinding<int> m_Contract;
        private ValueBinding<string> m_BuildStamp;
        private ValueBinding<bool> m_ShowHubButton;
        private RawValueBinding m_Build;
        private ValueBinding<HubGladeState> m_Glade;
        private ValueBinding<HubLiftLineState> m_LiftLine;
        private RawValueBinding m_Resorts;
        private RawValueBinding m_ResortLive;
        private ValueBinding<Entity> m_SelectedBinding;
        private RawValueBinding m_Runs;
        private RawValueBinding m_RunLive;
        private RawValueBinding m_Lifts;
        private RawValueBinding m_Problems;
        private ValueBinding<CityStats> m_City;
        private ValueBinding<bool> m_PisteMapBinding;
        private ValueBinding<bool> m_CatTrackDeleteBinding;   // contract 19: the cat track tool is open AND in Delete mode
        private ValueBinding<int> m_FollowBinding;
        private RawValueBinding m_History;
        // contract 29: the car spaces of every active road lodge (the Guests view). Rebuilt when the attraction snapshot, the parking
        // census or the lodge numbers move; pushed only while the Guests view observes it. Contract 30: each row's shown number. Contract 31:
        // the lodge's custom name (NameSystem), polled each frame while observed (PollLodgeNames: one dictionary read per row).
        private RawValueBinding m_LodgeParking;
        private SkiLodgeParkingSystem m_Parking;
        private LodgeNumberSystem m_Numbers;
        private readonly List<LodgeParkingRowData> m_LodgeParkingRows = new List<LodgeParkingRowData>(4);
        private readonly List<Entity> m_LodgeParkingSigns = new List<Entity>(4);
        private readonly List<SkiResortAttractivenessSystem.LodgeInfo> m_LodgeParkingInfo = new List<SkiResortAttractivenessSystem.LodgeInfo>(4);
        private readonly HashSet<Entity> m_LodgeParkingSeen = new HashSet<Entity>();
        private int m_LodgeParkingSigAttract = int.MinValue, m_LodgeParkingSigCensus = int.MinValue, m_LodgeParkingSigNumbers = int.MinValue,
                    m_LodgeParkingRevision;

        /// <summary>One bindings.ts LodgeParkingRow.</summary>
        private struct LodgeParkingRowData
        {
            public Entity Sign, Lodge;
            public string Item;
            public int CarSpaces;   // -1: no census yet
            public int Number;      // LodgeNumberSystem.ShownNumberOf: 0 = the only lodge of its tier in the area (no number shown)
            public string RawName;  // NameSystem's string for the lodge (the poll compares references); null = no custom name
            public string Name;     // LodgeNumberRule.DisplayName(RawName): null = none, the tier word and number show
        }
        private ValueBinding<HubBusCardState> m_BusCard;   // contract 26: always observed; costs a compare per frame until a coach or depot is selected
        private readonly List<SkiDaySystem.DepotView> m_BusCardViews = new List<SkiDaySystem.DepotView>(2);
        private Entity m_BusCardSelected, m_BusCardDepot;
        private int m_BusCardTicks;

        /// <summary>The DLL's stamp as a runtime value (BuildStamp.Value is a const: comparing it with
        /// another constant would be a compile-time expression).</summary>
        private static readonly string s_DllStamp = BuildStamp.Value;

        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;
        private Entity m_LastSelected;
        private bool m_BuildWasActive;
        private int m_BuildPushedSignature = int.MinValue;
        private float m_NextLockRefresh;
        private int m_LockResortsRevision = int.MinValue;
        private bool m_LocksDirty = true;
        private readonly HashSet<string> m_UiErrors = new HashSet<string>();
        private bool m_UiErrorCapLogged;
        private readonly Stopwatch m_PushTimer = new Stopwatch();
        private bool m_NonFiniteLogged;
        // Station tests ask m_PrefabCatalog.IsStation, which runs the catalog's per-frame validation. The build items' catalog
        // generation: RefreshLocks re-resolves the items when it moves.
        private int m_ItemsGeneration = int.MinValue;
        private int m_ItemsReresolveLines;

        // Selected resort.
        private Entity m_SelectedResort;
        private int m_ReconciledResortsRevision;
        private bool m_SignBaselineSet;
        private HashSet<Entity> m_SignsKnown = new HashSet<Entity>();
        private HashSet<Entity> m_SignsScratch = new HashSet<Entity>();

        // Bind notices.
        private int m_NoticedRunsRevision;
        private bool m_RunBaselineSet;
        private Dictionary<Entity, Entity> m_RunSigns = new Dictionary<Entity, Entity>();
        private Dictionary<Entity, Entity> m_RunSignsScratch = new Dictionary<Entity, Entity>();

        // Lift close notices: stats LiftStats.State per (entity, top).
        private int m_NoticedLiftsRevision;
        private bool m_LiftBaselineSet;
        private Dictionary<(Entity, Entity), int> m_LiftStates = new Dictionary<(Entity, Entity), int>();
        private Dictionary<(Entity, Entity), int> m_LiftStatesScratch = new Dictionary<(Entity, Entity), int>();

        // Lift hint (build[line].hint, per shown area): one flag per lift type (SkiCatalog.Lifts index), HubRules.LiftNoPair over that
        // type's rows of the shown area; m_HintRows is the one scratch list, refilled per type (no per-recompute allocation).
        private int m_HintLiftsRevision = int.MinValue;
        private Entity m_HintSign = Entity.Null;
        private readonly bool[] m_LiftNoPairByType = new bool[SkiCatalog.Lifts.Count];
        private readonly List<HubLiftHintRow> m_HintRows = new List<HubLiftHintRow>(8);

        // Piste map and the run hover.
        private bool m_UiContractOk;            // set by ApplyHello; NOT reset by a load (the UI module outlives a load)
        private bool m_PisteMapOn = true;       // the piste-map toggle; OnGamePreload → true
        private Entity m_HoveredRun;            // OnGamePreload → Null
        private Entity m_HoverRequest;
        private bool m_HoverPending;            // trigger → OnUpdate
        private bool m_HoverLogged, m_HoverRefusedLogged, m_HoverDisabledLogged;   // per load
        public long HoverCalls, HoverAccepted, HoverRefused;                       // per load

        // Selection rule 7.
        private struct FollowCandidate { public Entity Sign; public string Reason; }   // Sign Null = none
        private int m_FollowCount;              // OnGamePreload → 0 (the binding's value)
        public int FollowsSuppressedClosed, FollowsSuppressedRule, FollowsSuppressedUser, FollowsSuppressedSoon;
        private float m_LastFollowAt = float.NegativeInfinity, m_LastUserSelectAt = float.NegativeInfinity;
        private bool m_RuleSwitchedThisPass;
        private FollowCandidate m_FollowCandidate;
        private bool m_StationBaselineSet;
        private HashSet<Entity> m_StationsKnown = new HashSet<Entity>(), m_StationsScratch = new HashSet<Entity>();

        // uiLayout.
        private string m_LastLayout;
        private int m_LayoutLines;
        private bool m_LayoutCapLogged;

        // Dev tab / DUMP (plain fields written on events).
        public string HandshakeText { get; private set; } = "no uiHello yet";
        // Session flags, never reset on load. The UI module loads once at game start and its HubRoot says uiHello once; none
        // NoHelloWarnS after the session's first arm = the hub never mounted (a moved "Game" hook, another mod's registrar throwing,
        // the root boundary tripped): one WARN, so silence is impossible.
        private static bool s_HelloSeen;
        private static bool s_NoHelloWarned;
        private static float s_FirstArmAt = -1f;
        public string LastTriggerText { get; private set; } = "-";
        public string LastSelectText { get; private set; } = "-";
        public long TriggerCount;
        public int UiErrorRepeats;

        /// <summary>`runs` pushes this session.</summary>
        public long RunsPushes => m_RunsPush.Pushes;

        public bool IsArmed => m_Armed;
        public bool IsDisabled => m_Disabled;
        public bool IsOpen => m_HubOpen != null && m_HubOpen.value;

        /// <summary>Draw the piste map now. Armed, not disabled, the UI answered with this DLL's contract
        /// (HubContract), the hub is open and the Piste map toggle is on. A few field reads; called every frame by
        /// SkiPisteMapSystem.</summary>
        internal bool PisteMapVisible => m_Armed && !m_Disabled && m_UiContractOk && m_HubOpen != null && m_HubOpen.value && m_PisteMapOn;

        /// <summary>The hub's selected area: a live sign, or Entity.Null = the Not linked entry.</summary>
        internal Entity ShownSign => m_SelectedResort;

        /// <summary>The run the mouse is over in the hub's run list (Null = none). Validated.</summary>
        internal Entity HoveredRun => m_HoveredRun;

        /// <summary>Log text only (event time): the area's name, or "Not linked" for Null.</summary>
        internal string AreaLabel(Entity sign) => AreaName(sign);

        /// <summary>Dev tab / DUMP.</summary>
        public bool PisteMapOn => m_PisteMapOn;
        public int FollowCount => m_FollowCount;

        /// <summary>The UI answered uiHello with this DLL's contract (DUMP of SkiPisteMapSystem).</summary>
        internal bool UiContractOk => m_UiContractOk;

        public override GameMode gameMode => GameMode.Game;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_DefaultTool = World.GetOrCreateSystemManaged<DefaultToolSystem>();
            m_ObjectTool = World.GetOrCreateSystemManaged<ObjectToolSystem>();
            m_SelectedInfo = World.GetOrCreateSystemManaged<SelectedInfoUISystem>();
            m_CameraUpdateSystem = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();
            m_BindTrigger = World.GetOrCreateSystemManaged<SkiRunBindTriggerSystem>();
            m_Catalog = World.GetOrCreateSystemManaged<SkiRunCatalogSystem>();
            m_Stats = World.GetOrCreateSystemManaged<SkiResortStatsSystem>();
            m_RunProps = World.GetOrCreateSystemManaged<SkiRunPropSystem>();
            m_HistorySystem = World.GetOrCreateSystemManaged<SkiResortHistorySystem>();
            m_GuestCounter = World.GetOrCreateSystemManaged<SkiGuestCounterSystem>();
            m_SkiDays = World.GetOrCreateSystemManaged<SkiDaySystem>();
            m_Time = World.GetOrCreateSystemManaged<Game.Simulation.TimeSystem>();
            m_ClimateSystem = World.GetOrCreateSystemManaged<Game.Simulation.ClimateSystem>();
            m_Census = World.GetOrCreateSystemManaged<LiftCensusSystem>();
            m_Attractiveness = World.GetOrCreateSystemManaged<SkiResortAttractivenessSystem>();
            m_Parking = World.GetOrCreateSystemManaged<SkiLodgeParkingSystem>();
            m_Numbers = World.GetOrCreateSystemManaged<LodgeNumberSystem>();
            m_PrefabCatalog = World.GetOrCreateSystemManaged<SkiPrefabCatalogSystem>();
            m_Infoview = World.GetOrCreateSystemManaged<SkiPlacementInfoviewSystem>();
            m_GladeTool = World.GetOrCreateSystemManaged<SkiGladeToolSystem>();
            // The lift generator. Its OnCreate resolves ToolOutputBarrier, TerrainSystem, OverlayRenderSystem, LiftCensusSystem and
            // SkiPrefabCatalogSystem, never this system, so a GetOrCreate here cannot recurse (Mod.cs registers its update phase).
            m_LiftLineTool = World.GetOrCreateSystemManaged<LiftLineToolSystem>();
            // The grading tool. Its OnCreate resolves Overlay / Terrain / Water / ToolOutputBarrier / SkiPrefabCatalogSystem /
            // Objects.SearchSystem / CameraUpdateSystem, never this system (it reads SkiRunCatalogSystem with GetExisting at load), so a
            // GetOrCreate here cannot recurse.
            m_GradeTool = World.GetOrCreateSystemManaged<CatTrackGradeToolSystem>();
            m_CatTrackDelete = World.GetOrCreateSystemManaged<SkiCatTrackDeleteSystem>();
            // The Quick runs tool. Its OnCreate resolves Overlay / Terrain / Water / SkiPrefabCatalogSystem / SkiRunOwnershipSystem /
            // Objects.SearchSystem / CameraUpdateSystem / CitySystem / NameSystem / ToolOutputBarrier, never this system, so a
            // GetOrCreate here cannot recurse.
            m_QuickRunsTool = World.GetOrCreateSystemManaged<QuickRunsToolSystem>();
            // m_PisteMap (DUMP only) is resolved at DUMP time with GetExistingSystemManaged: SkiPisteMapSystem's
            // OnCreate resolves THIS system, and a GetOrCreate both ways would create one inside the other's OnCreate.
            m_CitizenQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Citizens.Citizen>(), ComponentType.ReadOnly<Game.Citizens.CurrentBuilding>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            m_ConsumerQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Buildings.Building>(), ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Game.Buildings.ElectricityConsumer>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });

            bool showButton = Mod.Settings == null || Mod.Settings.ShowHubButton;
            m_ResortsPush.Binding = m_Resorts = new RawValueBinding(Group, "resorts", WriteResorts);
            m_ResortLivePush.Binding = m_ResortLive = new RawValueBinding(Group, "resortLive", WriteResortLive);
            m_RunsPush.Binding = m_Runs = new RawValueBinding(Group, "runs", WriteRuns);
            m_RunLivePush.Binding = m_RunLive = new RawValueBinding(Group, "runLive", WriteRunLive);
            m_LiftsPush.Binding = m_Lifts = new RawValueBinding(Group, "lifts", WriteLifts);
            m_ProblemsPush.Binding = m_Problems = new RawValueBinding(Group, "problems", WriteProblems);
            m_HistoryPush.Binding = m_History = new RawValueBinding(Group, "history", WriteHistory);
            m_LodgeParkingPush.Binding = m_LodgeParking = new RawValueBinding(Group, "lodgeParking", WriteLodgeParking);
            m_MergedPush.Binding = m_Merged = new RawValueBinding(Group, "mergedLodges", WriteMergedLodges);   // contract 32

            // Values (C# → UI), in bindings.ts order (gate R1 needs the literals).
            AddBinding(m_HubOpen = new ValueBinding<bool>(Group, "hubOpen", false));
            AddBinding(m_Contract = new ValueBinding<int>(Group, "contract", HubContract));
            AddBinding(m_BuildStamp = new ValueBinding<string>(Group, "buildStamp", s_DllStamp ?? ""));
            AddBinding(m_ShowHubButton = new ValueBinding<bool>(Group, "showHubButton", showButton));
            AddBinding(m_Build = new RawValueBinding(Group, "build", WriteBuild));
            AddBinding(m_Glade = new ValueBinding<HubGladeState>(Group, "glade",
                new HubGladeState(false, false, SkiGladeCompose.DefaultLevel, false, "")));
            AddBinding(m_Resorts);
            AddBinding(m_ResortLive);
            AddBinding(m_SelectedBinding = new ValueBinding<Entity>(Group, "selectedResort", Entity.Null));
            AddBinding(m_Runs);
            AddBinding(m_RunLive);
            AddBinding(m_Lifts);
            AddBinding(m_Problems);
            AddBinding(m_City = new ValueBinding<CityStats>(Group, "city", default(CityStats), new CityStatsWriter(),
                new CityStatsComparer()));
            AddBinding(m_PisteMapBinding = new ValueBinding<bool>(Group, "pisteMap", true));
            AddBinding(m_FollowBinding = new ValueBinding<int>(Group, "follow", 0));
            AddBinding(m_History);   // panel binding, no Demand
            AddBinding(m_LiftLine = new ValueBinding<HubLiftLineState>(Group, "liftLine",
                new HubLiftLineState(false, false, "")));   // panel binding, no Demand
            AddBinding(m_QuickRuns = new RawValueBinding(Group, "quickRuns", WriteQuickRuns));   // panel binding, no Demand
            AddBinding(m_CatTrackDeleteBinding = new ValueBinding<bool>(Group, "catTrackDeleteMode", false));   // panel binding, no Demand
            AddBinding(m_BusCard = new ValueBinding<HubBusCardState>(Group, "busCard", default));   // not a panel binding
            AddBinding(m_LodgeParking);   // contract 29: panel binding (the Guests view), no Demand
            AddBinding(m_Merged);         // contract 32: panel binding (the Overview fold), no Demand
            AddBinding(m_MergeCard = new ValueBinding<HubMergeCardState>(Group, "mergeCard", default));   // contract 32: always observed, like busCard

            // Triggers (UI → C#): bindings.ts TRIGGER.
            AddBinding(new TriggerBinding(Group, "toggleHub", OnToggleHub));
            AddBinding(new TriggerBinding<bool>(Group, "setHubOpen", OnSetHubOpen));
            AddBinding(new TriggerBinding<int, string>(Group, "uiHello", OnUiHello));
            AddBinding(new TriggerBinding<string>(Group, "uiError", OnUiError));
            AddBinding(new TriggerBinding<string>(Group, "launch", OnLaunch));
            AddBinding(new TriggerBinding<Entity>(Group, "focusRun", OnFocusRun));
            AddBinding(new TriggerBinding<Entity>(Group, "deleteCatTrack", OnDeleteCatTrack));
            AddBinding(new TriggerBinding<Entity, int>(Group, "mergeBuilding", OnMergeBuilding));   // contract 32
            AddBinding(new TriggerBinding<Entity>(Group, "unmergeBuilding", OnUnmergeBuilding));
            AddBinding(new TriggerBinding(Group, "gladeToggle", OnGladeToggle));
            AddBinding(new TriggerBinding<int>(Group, "gladeSetLevel", OnGladeSetLevel));
            AddBinding(new TriggerBinding<bool>(Group, "gladeSetTreeLine", OnGladeSetTreeLine));
            AddBinding(new TriggerBinding(Group, "dumpHub", OnDumpHub));
            AddBinding(new TriggerBinding<Entity>(Group, "selectResort", OnSelectResort));
            AddBinding(new TriggerBinding<Entity, string>(Group, "rename", OnRename));
            AddBinding(new TriggerBinding<Entity, Entity>(Group, "focusLift", OnFocusLift));
            AddBinding(new TriggerBinding<Entity>(Group, "focusBuilding", OnFocusBuilding));
            AddBinding(new TriggerBinding<string>(Group, "uiLayout", OnUiLayout));
            AddBinding(new TriggerBinding<bool>(Group, "setPisteMap", OnSetPisteMap));
            AddBinding(new TriggerBinding<Entity>(Group, "hoverRun", OnHoverRun));
            AddBinding(new TriggerBinding(Group, "liftLineToggle", OnLiftLineToggle));
            AddBinding(new TriggerBinding(Group, "quickRunsToggle", OnQuickRunsToggle));
            AddBinding(new TriggerBinding<string, int>(Group, "quickRunsSet", OnQuickRunsSet));
            AddBinding(new TriggerBinding<bool>(Group, "setCatTrackDeleteMode", OnSetCatTrackDeleteMode));
            AddBinding(new TriggerBinding<Entity, int>(Group, "setRunGunSpacing", OnSetRunGunSpacing));
            AddBinding(new TriggerBinding<Entity, int>(Group, "setRunLightSpacing", OnSetRunLightSpacing));

            Mod.Log.Info($"{nameof(SkiResortHubUISystem)} created — group '{Group}', contract {HubContract}, " +
                         $"DLL stamp {s_DllStamp} (UI bundled at build: {BuildStamp.UiBundled}); 20 value bindings, " +
                         "24 triggers");
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);   // Enabled = (gameMode & mode) != 0 (UISystemBase.cs:60-64)
            try
            {
                if (m_QueueCount > 0)
                {
                    Mod.Log.Info($"Hub: {m_QueueCount} queued request(s) dropped by the load ({mode})");
                    ClearQueue();
                }
                m_Armed = false;
                m_Disabled = false;
                m_ErrorLogged = false;
                m_Contract.Update(HubContract);   // undo HandleException's ContractAbsent (pushes only on a change)
                m_LastSelected = Entity.Null;
                m_BusCardSelected = m_BusCardDepot = Entity.Null;
                m_BuildWasActive = false;
                m_BuildPushedSignature = int.MinValue;
                FlushPushRows();   // folded row-count lines of the last city first
                m_ResortsPush.Reset();
                m_ResortLivePush.Reset();
                m_RunsPush.Reset();
                m_RunLivePush.Reset();
                m_LiftsPush.Reset();
                m_ProblemsPush.Reset();
                m_HistoryPush.Reset();
                ResetHistoryPush();   // drop the last city's rows
                m_LodgeParkingPush.Reset();
                m_MergedPush.Reset();
                ResetMergeCard();
                m_LodgeParkingRows.Clear();
                m_LodgeParkingSigAttract = m_LodgeParkingSigCensus = m_LodgeParkingSigNumbers = int.MinValue;
                m_NextLockRefresh = 0f;
                m_LockResortsRevision = int.MinValue;
                m_LocksDirty = true;
                m_UiErrors.Clear();
                m_UiErrorCapLogged = false;
                UiErrorRepeats = 0;
                m_NonFiniteLogged = false;
                s_NonFinite = 0;
                Array.Clear(m_ItemDriftLogged, 0, m_ItemDriftLogged.Length);   // one drift WARN per item per load
                m_SentMismatchLogged = false;
                m_QueueDropLogged = m_UnknownLaunchLogged = false;
                m_WriterFailed.Clear();
                m_LockSign = Entity.Null;
                LastTriggerText = "-";
                LastSelectText = "-";
                HandshakeText = "no uiHello yet this load";
                m_ActiveItem = -1;
                m_SelectedResort = Entity.Null;   // rule 1: Null at load
                m_SelectedBinding.Update(Entity.Null);
                m_SignBaselineSet = false;
                m_SignsKnown.Clear();
                m_RunBaselineSet = false;
                m_RunSigns.Clear();
                m_LiftBaselineSet = false;
                m_LiftStates.Clear();
                m_HintLiftsRevision = int.MinValue;
                m_HintSign = Entity.Null;
                Array.Clear(m_LiftNoPairByType, 0, m_LiftNoPairByType.Length);
                // Piste map on at every load, hover and follow state per load.
                m_PisteMapOn = true;
                m_PisteMapBinding.Update(true);
                m_HoveredRun = Entity.Null;
                m_HoverRequest = Entity.Null;
                m_HoverPending = false;
                m_HoverLogged = m_HoverRefusedLogged = m_HoverDisabledLogged = false;
                HoverCalls = HoverAccepted = HoverRefused = 0;
                m_FollowCount = 0;
                m_FollowBinding.Update(0);
                FollowsSuppressedClosed = FollowsSuppressedRule = FollowsSuppressedUser = FollowsSuppressedSoon = 0;
                m_LastFollowAt = m_LastUserSelectAt = float.NegativeInfinity;
                m_RuleSwitchedThisPass = false;
                m_FollowCandidate = default;
                m_StationBaselineSet = false;
                m_StationsKnown.Clear();
                m_LastLayout = null;
                m_LayoutLines = 0;
                m_LayoutCapLogged = false;
                m_Catalog.Demand = false;
                m_Stats.Demand = false;
                m_HubOpen.Update(false);   // a new city starts with the hub closed
            }
            catch (Exception e)
            {
                HandleException(e, "OnGamePreload");
            }
        }

        /// <summary>Quit to desktop raises no OnGamePreload, so the folded `Hub [push]` row-count lines are written here (the LogWindow
        /// engine notes: OnDestroy runs while the logger is still alive).</summary>
        protected override void OnDestroy()
        {
            try
            {
                FlushPushRows();
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
            if (mode != GameMode.Game)
                return;
            try
            {
                string itemText = ResolveItems();
                ReadItemValues();
                m_ItemsGeneration = m_PrefabCatalog.Generation;   // the generation ResolveItems used
                m_ItemsReresolveLines = 0;
                // The items are SkiCatalog's table, so nothing is compared at arm time; a catalog sanity problem is named in the catalog's own
                // log (SkiPrefabCatalogSystem OnCreate / [arm]).
                string catalogItems = SkiCatalog.Problems.Count == 0
                    ? $"items = SkiCatalog.BuildItems ({ItemCount} rows, sanity clean)"
                    : $"items = SkiCatalog.BuildItems ({ItemCount} rows) — SkiCatalog has " +
                      $"{SkiCatalog.Problems.Count} sanity problem(s): {string.Join("; ", SkiCatalog.Problems)}";
                int stationPrefabs = m_PrefabCatalog.StationPrefabSet.Count;
                // The stats rows may still hold the previous city's until its first rebuild: start from the
                // current revisions so rules 1/4/5/6 and the bind notices run on fresh rows only.
                m_ReconciledResortsRevision = m_Stats.ResortsRevision;
                m_NoticedRunsRevision = m_Stats.RunsRevision;
                m_NoticedLiftsRevision = m_Stats.LiftsRevision;
                m_Armed = true;
                if (s_FirstArmAt < 0f)
                    s_FirstArmAt = UnityEngine.Time.realtimeSinceStartup;   // the session's first arm
                Mod.Log.Info($"Hub: ARMED — group '{Group}', contract {HubContract}, DLL stamp {s_DllStamp} (UI " +
                             $"bundled at build: {BuildStamp.UiBundled}); bindings hubOpen, contract, buildStamp, " +
                             "showHubButton (always observed), build, glade, resorts, resortLive, selectedResort, runs, " +
                             "runLive, lifts, problems, city (panel bindings: catalog and stats Demand follow them, nothing " +
                             "is computed while closed), pisteMap and follow (panel values, no Demand), liftLine (contract 12: the Lifts " +
                             "tab toggle, no Demand), catTrackDeleteMode (contract 19: the Runs tab chip, no Demand), history (contract 10: " +
                             "the History view only, no Demand; pushed when a stored month or the area set changed, the month " +
                             $"in progress at most every {Tuning.HistoryProgressPushS:0.#} s, HistoryProgressPushS); arrays pushed per " +
                             "stats revision (slow rows and live rows apart), logged only when a row count changes; triggers " +
                             "toggleHub, setHubOpen, uiHello, uiError, launch, focusRun, deleteCatTrack (contract 18), gladeToggle, gladeSetLevel, " +
                             "gladeSetTreeLine, dumpHub, selectResort, rename, focusLift, focusBuilding, uiLayout, setPisteMap " +
                             "liftLineToggle (contract 12), setCatTrackDeleteMode (contract 19) (queued, applied in OnUpdate, one line each; uiLayout only when changed) and hoverRun (a field " +
                             "write, validated next update, counted; first per load logged); settings " +
                             $"ShowHubButton={Mod.Settings == null || Mod.Settings.ShowHubButton} OpenHubOnSignSelect=" +
                             $"{Mod.Settings == null || Mod.Settings.OpenHubOnSignSelect}; {itemText}; {catalogItems}; item " +
                             $"costs: {ItemCostText()}; station prefabs {stationPrefabs}; " +
                             $"locks (Locked, Missing, NoSign; AlreadyBuilt on the shown sign, re-checked on the " +
                             $"picked sign at an upgrade launch) re-checked every {Tuning.LockRefreshS:0.#} s while the build " +
                             "section is open (LockRefreshS); the line hints follow the shown ski area, per lift type; D36 " +
                             "follow (rule 7): a run bound, a " +
                             "station placed or a lift closed in another live ski area switches the hub to it while it is " +
                             "open — not in a pass where a new or removed sign already switched, not within " +
                             $"FollowQuietAfterUserS={HubRules.FollowQuietAfterUserS:0.#} s of the player's own switch, at " +
                             $"most one per FollowMinIntervalS={HubRules.FollowMinIntervalS:0.#} s, never moving the camera; " +
                             "piste map on at load (D19, drawn by SkiPisteMapSystem); ‹ › (UI requests only) fly to the " +
                             "sign unless the camera " +
                             $"is already on it (pivot within {HubCameraRules.KeepZoomFraction} × zoom of the sign and zoom " +
                             $"<= {HubCameraRules.KeepMaxZoomM} m: HubCameraRules.KeepZoomFraction, KeepMaxZoomM), Not " +
                             "linked never moves it; " +
                             "a lift that closes in another resort than the one shown is logged (Hub [bind-notice]); " +
                             $"focus zoom = radius × {Tuning.FocusZoomFactor} at {Tuning.FocusPitchDeg}° " +
                             $"(FocusZoomFactor, FocusPitchDeg; no selection, OD6), buildings >= {Tuning.FocusBuildingMinZoomM} m " +
                             $"(FocusBuildingMinZoomM), lifts >= {Tuning.FocusLiftMinZoomM} m (FocusLiftMinZoomM), a lift's gap " +
                             $"{Tuning.FocusGapZoomM} m (FocusGapZoomM); rename max {Tuning.RenameMaxChars} chars " +
                             $"(RenameMaxChars); LayoutLinesMax={Tuning.LayoutLinesMax} BindNoticesPerPass=" +
                             $"{Tuning.BindNoticesPerPass} SlowPushMs={Tuning.SlowPushMs} QueueCapacity={Tuning.QueueCapacity} " +
                             $"UiErrorsMax={Tuning.UiErrorsMax}; selected resort Null until the first stats rebuild; hub " +
                             "closed at load");
            }
            catch (Exception e)
            {
                HandleException(e, "OnGameLoadingComplete");
            }
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();   // UISystemBase.cs:39-46 (no update binding registered; kept for the contract)
            if (m_Disabled)
                return;
            try
            {
                GameManager gm = GameManager.instance;
                if (gm == null || gm.isGameLoading || !m_Armed)
                {
                    m_Catalog.Demand = false;
                    m_Stats.Demand = false;
                    return;
                }

                // One WARN per session when the UI never said hello (a float compare per frame until then).
                if (!s_HelloSeen && !s_NoHelloWarned && s_FirstArmAt >= 0f &&
                    UnityEngine.Time.realtimeSinceStartup - s_FirstArmAt > Tuning.NoHelloWarnS)
                {
                    s_NoHelloWarned = true;
                    Mod.Log.Warn($"Hub: no uiHello from the UI module {Tuning.NoHelloWarnS:0} s after the first ARM this session — " +
                                 "the hub panel (HubRoot) never mounted: the Ski Resort button and the sign selection open nothing. " +
                                 "Check UI.log for an error in SkiResortMod.mjs or another mod's module (the game's registrar " +
                                 "loop has no try/catch), and that SkiResortMod.mjs / .css sit beside the DLL");
                }

                // Setting → binding (a bool read; the binding pushes only on a change).
                m_ShowHubButton.Update(Mod.Settings == null || Mod.Settings.ShowHubButton);

                // World selection: selected-resort mapping (rule 2), then auto-open (one compare per frame).
                Entity selected = m_ToolSystem.selected;
                if (selected != m_LastSelected)
                {
                    m_LastSelected = selected;
                    OnSelectionChanged(selected);
                }

                if (m_QueueCount > 0)
                    DrainQueue();

                // hoverRun: the trigger wrote a field; validate and apply it here (main thread).
                if (m_HoverPending)
                {
                    m_HoverPending = false;
                    ApplyHover(m_HoverRequest);
                }

                ReconcileSelection();
                PushPanelData();
            }
            catch (Exception e)
            {
                HandleException(e, "OnUpdate");
            }
        }

        private void OnToggleHub() => Trigger("toggleHub", new Request { Kind = RequestKind.ToggleHub });

        private void OnSetHubOpen(bool open) =>
            Trigger("setHubOpen", new Request { Kind = RequestKind.SetHubOpen, Bool = open });

        private void OnUiHello(int contract, string bundleStamp)
        {
            s_HelloSeen = true;   // a field write at dispatch time (the queue may wait for the arm)
            Trigger("uiHello", new Request { Kind = RequestKind.Hello, Int = contract, Text = bundleStamp });
        }

        private void OnUiError(string message) =>
            Trigger("uiError", new Request { Kind = RequestKind.UiError, Text = message });

        private void OnLaunch(string id) => Trigger("launch", new Request { Kind = RequestKind.Launch, Text = id });

        private void OnFocusRun(Entity run) =>
            Trigger("focusRun", new Request { Kind = RequestKind.Focus, Entity = run });
        private void OnDeleteCatTrack(Entity run) =>
            Trigger("deleteCatTrack", new Request { Kind = RequestKind.DeleteCatTrack, Entity = run });

        private void OnSetCatTrackDeleteMode(bool on) =>
            Trigger("setCatTrackDeleteMode", new Request { Kind = RequestKind.SetCatTrackDeleteMode, Bool = on });

        private void OnGladeToggle() => Trigger("gladeToggle", new Request { Kind = RequestKind.GladeToggle });

        private void OnGladeSetLevel(int level) =>
            Trigger("gladeSetLevel", new Request { Kind = RequestKind.GladeLevel, Int = level });

        private void OnGladeSetTreeLine(bool treeLine) =>
            Trigger("gladeSetTreeLine", new Request { Kind = RequestKind.GladeTreeLine, Bool = treeLine });

        private void OnDumpHub() => Trigger("dumpHub", new Request { Kind = RequestKind.Dump });

        private void OnSelectResort(Entity sign) =>
            Trigger("selectResort", new Request { Kind = RequestKind.SelectResort, Entity = sign });

        private void OnRename(Entity entity, string name) =>
            Trigger("rename", new Request { Kind = RequestKind.Rename, Entity = entity, Text = name });

        private void OnSetRunGunSpacing(Entity run, int meters) =>
            Trigger("setRunGunSpacing", new Request { Kind = RequestKind.SetRunPropSpacing, Entity = run, Bool = false, Int = meters });

        private void OnSetRunLightSpacing(Entity run, int meters) =>
            Trigger("setRunLightSpacing", new Request { Kind = RequestKind.SetRunPropSpacing, Entity = run, Bool = true, Int = meters });

        private void OnFocusLift(Entity entity, Entity top) =>
            Trigger("focusLift", new Request { Kind = RequestKind.FocusLift, Entity = entity, Entity2 = top });

        private void OnFocusBuilding(Entity building) =>
            Trigger("focusBuilding", new Request { Kind = RequestKind.FocusBuilding, Entity = building });

        private void OnUiLayout(string layout) =>
            Trigger("uiLayout", new Request { Kind = RequestKind.Layout, Text = layout });

        private void OnLiftLineToggle() => Trigger("liftLineToggle", new Request { Kind = RequestKind.LiftLineToggle });

        private void OnSetPisteMap(bool on) =>
            Trigger("setPisteMap", new Request { Kind = RequestKind.SetPisteMap, Bool = on });

        /// <summary>Piste-map emphasis: a field write only (no queue: mouse traffic must not fill QueueCapacity), no EntityManager, no
        /// log. OnUpdate validates and applies it.</summary>
        private void OnHoverRun(Entity run)
        {
            try
            {
                HoverCalls++;
                if (m_Disabled)   // OnUpdate returns early after a "!!!": say it once here, count the rest
                {
                    if (!m_HoverDisabledLogged)
                    {
                        m_HoverDisabledLogged = true;
                        Mod.Log.Info("Hub [hover] ignored — the hub is disabled after an exception until the next game load (later calls are counted, DUMP)");
                    }
                    return;
                }
                m_HoverRequest = run;
                m_HoverPending = true;
            }
            catch (Exception e)
            {
                HandleException(e, "trigger hoverRun");
            }
        }

        private void Trigger(string name, Request request)
        {
            try
            {
                TriggerCount++;
                request.Source = "ui";
                if (!Enabled)
                {
                    Mod.Log.Info($"Hub: trigger {name} ignored — the hub runs in a loaded city only " +
                                 $"(game mode {GameManager.instance?.gameMode})");
                    return;
                }
                if (m_Disabled)
                {
                    RefuseDisabled(name, request);
                    return;
                }
                if (NeedsEntity(request.Kind, request.Entity) && !EntityManager.Exists(request.Entity))
                {
                    Mod.Log.Info($"Hub [{name}] refused — entity {Ent(request.Entity)} does not exist (stale row?)");
                    LastTriggerText = $"{name} {Ent(request.Entity)}: refused, no such entity";
                    return;
                }
                Enqueue(name, request);
            }
            catch (Exception e)
            {
                HandleException(e, "trigger " + name);
            }
        }

        /// <summary>Kinds whose entity argument must exist at trigger time (selectResort accepts Null =
        /// the Not linked view; focusLift's second argument may be Null and is checked in Apply).</summary>
        private static bool NeedsEntity(RequestKind kind, Entity entity)
        {
            switch (kind)
            {
                case RequestKind.Focus:
                case RequestKind.Rename:
                case RequestKind.FocusLift:
                case RequestKind.FocusBuilding:
                case RequestKind.MergeBuilding:
                case RequestKind.UnmergeBuilding:
                case RequestKind.SetRunPropSpacing:
                    return true;
                case RequestKind.SelectResort:
                    return entity != Entity.Null;
                default:
                    return false;
            }
        }

        /// <summary>After a "!!!": hello and uiError are pure log lines and are still written; every
        /// other trigger is refused with one line.</summary>
        private void RefuseDisabled(string name, Request request)
        {
            if (request.Kind == RequestKind.Hello)
                ApplyHello(request);
            else if (request.Kind == RequestKind.UiError)
                ApplyUiError(request);
            else
                Mod.Log.Info($"Hub: trigger {name} refused — the hub is disabled after an exception until the next " +
                             "game load (see the !!! line)");
        }

        private void Enqueue(string name, Request request)
        {
            if (m_QueueCount >= m_Queue.Length)
            {
                if (!m_QueueDropLogged)
                {
                    m_QueueDropLogged = true;
                    Mod.Log.Warn($"Hub: trigger {name} DROPPED — {m_QueueCount} requests already queued since the last " +
                                 "update (QueueCapacity); further drops this load are not logged");
                }
                return;
            }
            m_Queue[m_QueueCount++] = request;
        }

        private void ClearQueue()
        {
            for (int i = 0; i < m_QueueCount; i++)
                m_Queue[i] = default;
            m_QueueCount = 0;
        }

        private void DrainQueue()
        {
            int count = m_QueueCount;
            for (int i = 0; i < count && i < m_Queue.Length; i++)
            {
                Request r = m_Queue[i];
                m_Queue[i] = default;
                Apply(r);
            }
            // Requests queued while applying (dev-tab buttons cannot, triggers do not run here) stay.
            int rest = m_QueueCount - count;
            for (int i = 0; i < rest; i++)
            {
                m_Queue[i] = m_Queue[count + i];
                m_Queue[count + i] = default;
            }
            m_QueueCount = rest > 0 ? rest : 0;
        }

        private void Apply(Request r)
        {
            switch (r.Kind)
            {
                case RequestKind.ToggleHub:
                    SetOpen(!m_HubOpen.value, $"toggle ({r.Source})");
                    break;
                case RequestKind.SetHubOpen:
                    SetOpen(r.Bool, r.Bool ? $"setHubOpen(true) ({r.Source})" : $"setHubOpen(false): X ({r.Source})");
                    break;
                case RequestKind.Hello:
                    ApplyHello(r);
                    break;
                case RequestKind.UiError:
                    ApplyUiError(r);
                    break;
                case RequestKind.Launch:
                    ApplyLaunch(r.Text, r.Source);
                    break;
                case RequestKind.Focus:
                    ApplyFocusRun(r.Entity, r.Source);
                    break;
                case RequestKind.GladeToggle:
                    ApplyGladeToggle(r.Source);
                    break;
                case RequestKind.GladeLevel:
                    m_GladeTool.SetLevel(r.Int, "hub" + SourceSuffix(r.Source));   // the tool writes the line
                    LastTriggerText = $"gladeSetLevel({r.Int}) → L{m_GladeTool.Level}";
                    break;
                case RequestKind.GladeTreeLine:
                    m_GladeTool.SetTreeLine(r.Bool, "hub" + SourceSuffix(r.Source));   // the tool writes the line
                    LastTriggerText = $"gladeSetTreeLine({r.Bool})";
                    break;
                case RequestKind.Dump:
                    DumpToLog(r.Source);
                    break;
                case RequestKind.SelectResort:
                    ApplySelectResort(r.Entity, r.Source);
                    break;
                case RequestKind.Rename:
                    ApplyRename(r.Entity, r.Text, r.Source);
                    break;
                case RequestKind.FocusLift:
                    ApplyFocusLift(r.Entity, r.Entity2, r.Source);
                    break;
                case RequestKind.FocusBuilding:
                    ApplyFocusBuilding(r.Entity, r.Source);
                    break;
                case RequestKind.Layout:
                    ApplyLayout(r.Text);
                    break;
                case RequestKind.SetPisteMap:
                    ApplyPisteMap(r.Bool, r.Source);
                    break;
                case RequestKind.TogglePisteMap:
                    ApplyPisteMap(!m_PisteMapOn, r.Source);
                    break;
                case RequestKind.LiftLineToggle:
                    ApplyLiftLineToggle(r.Source);
                    break;
                case RequestKind.QuickRunsToggle:
                    ApplyQuickRunsToggle(r.Source);
                    break;
                case RequestKind.QuickRunsSet:
                    ApplyQuickRunsSet(r.Text, r.Int, r.Source);
                    break;
                case RequestKind.DeleteCatTrack:
                    // Queued for PostTool: a Deleted tag added here, in UIUpdate, would never reach vanilla's area search tree
                    m_CatTrackDelete.Request(r.Entity, "hub row" + SourceSuffix(r.Source));
                    LastTriggerText = $"deleteCatTrack {Ent(r.Entity)}: queued";
                    break;
                case RequestKind.SetCatTrackDeleteMode:
                    ApplyCatTrackDeleteMode(r.Bool, r.Source);
                    break;
                case RequestKind.SetRunPropSpacing:
                    ApplyRunPropSpacing(r.Entity, r.Bool, r.Int, r.Source);
                    break;
                case RequestKind.MergeBuilding:
                    ApplyMerge(r.Entity, r.Int, r.Source);
                    break;
                case RequestKind.UnmergeBuilding:
                    ApplyUnmerge(r.Entity, r.Source);
                    break;
            }
        }

        /// <summary>The Runs tab's Delete cat tracks chip (contract 19): on opens the cat track tool if needed, then its Delete mode; off
        /// turns the mode off (the tool stays open). The value follows the tool (m_CatTrackDeleteBinding, pushed in OnUpdate), so closing
        /// the tool or picking another one shows it off.</summary>
        private void ApplyCatTrackDeleteMode(bool on, string source)
        {
            string why = "hub chip" + SourceSuffix(source);
            bool ok = m_GradeTool != null && (!on || m_GradeTool.TryActivate(why)) && m_GradeTool.SetDeleteMode(on, why);
            LastTriggerText = $"setCatTrackDeleteMode({on}): {(ok ? "done" : "refused")}";
        }

        /// <summary>A step of the Runs view's prop rows (contract 28): a component write only, no Deleted in this phase (tools/check-run-props.js).
        /// The placer re-places after its settle; snow and upkeep never read it.</summary>
        private void ApplyRunPropSpacing(Entity run, bool lights, int meters, string source)
        {
            string what = lights ? "setRunLightSpacing" : "setRunGunSpacing";
            bool ok = m_RunProps.SetSpacing(run, lights, meters);
            if (ok)
                m_Stats.RequestRebuild("hub run props");
            Mod.Log.Info($"Hub [props] {what} {Ent(run)} {meters} m: {(ok ? "stored" : "refused")} ({source})");
            LastTriggerText = $"{what} {Ent(run)} {meters}: {(ok ? "stored" : "refused")}";
        }

        private void ApplyPisteMap(bool on, string source)
        {
            bool changed = on != m_PisteMapOn;
            m_PisteMapOn = on;
            m_PisteMapBinding.Update(on);
            string area = AreaName(m_SelectedResort);
            Mod.Log.Info($"Hub [piste] map {(on ? "on" : "off")} ({source}; showing '{area}'{(changed ? "" : $"; already {(on ? "on" : "off")}")})");
            LastTriggerText = $"setPisteMap({on})" + (changed ? "" : ": no change");
        }

        /// <summary>Validate the hovered run against the catalog (one of our runs) and apply it. Null clears.
        /// The first valid and the first refused hover of a load are logged; the rest are counted (DUMP).</summary>
        private void ApplyHover(Entity run)
        {
            if (run == Entity.Null)
            {
                m_HoveredRun = Entity.Null;
                return;
            }
            if (!EntityManager.Exists(run) || !m_Catalog.TryGetRow(run, out SkiRunCatalogSystem.RunRow row))
            {
                HoverRefused++;
                if (!m_HoverRefusedLogged)
                {
                    m_HoverRefusedLogged = true;
                    Mod.Log.Warn($"Hub [hover] refused {Ent(run)} (not one of our runs); later refusals are counted (DUMP)");
                }
                return;
            }
            HoverAccepted++;
            if (!m_HoverLogged)
            {
                m_HoverLogged = true;
                Mod.Log.Info($"Hub [hover] first run hover this load: run {Ent(run)} '{row.Name}'; later hovers are counted (DUMP)");
            }
            m_HoveredRun = run;
        }

        /// <summary>"Copper Bowl" for a sign row, "Not linked" for Null (log text only): the resort row's Name,
        /// else the sign's custom name, else "sign i:v". Event time only.</summary>
        private string AreaName(Entity sign)
        {
            if (sign == Entity.Null)
                return "Not linked";
            int count = m_Stats.ResortCount;
            for (int i = 0; i < count; i++)
            {
                ResortStats r = m_Stats.GetResort(i);
                if (r.Sign == sign)
                    return r.Name ?? "";
            }
            if (m_NameSystem.TryGetCustomName(sign, out string custom) && !string.IsNullOrEmpty(custom))
                return custom;
            return "sign " + Ent(sign);
        }

        private static string SourceSuffix(string source) => source == "ui" ? "" : " (" + source + ")";

        private void SetOpen(bool open, string why)
        {
            if (m_HubOpen.value == open)
            {
                Mod.Log.Info($"Hub: {why} — already {(open ? "open" : "closed")}");
                LastTriggerText = why + ": no change";
                return;
            }
            m_HubOpen.Update(open);
            Mod.Log.Info($"Hub [{(open ? "open" : "close")}] {why}");
            LastTriggerText = why + (open ? ": opened" : ": closed");
        }

        private void OnSelectionChanged(Entity selected)
        {
            if (selected == Entity.Null)
                return;
            // Rule 2 BEFORE the auto-open early return: the hub may be open already.
            MapWorldSelection(selected);

            if (m_HubOpen.value)
                return;   // never forced closed; nothing to do when already open
            if (Mod.Settings != null && !Mod.Settings.OpenHubOnSignSelect)
                return;
            if (!m_Ownership.IsLiveGate(selected))
                return;   // auto-open stays sign-only
            m_HubOpen.Update(true);
            Mod.Log.Info($"Hub [open] Ski Area Welcome Sign {Ent(selected)} selected (setting OpenHubOnSignSelect)");
        }

        /// <summary>Rule 2 (Engine notes: Selected resort). Event time only (a selection change). A user action: it
        /// stamps m_LastUserSelectAt (rule 7's FollowQuietAfterUserS guard) whenever it selects.</summary>
        private void MapWorldSelection(Entity selected)
        {
            if (m_Ownership.IsLiveGate(selected))
            {
                Select(selected, $"world selection of the Welcome Sign {Ent(selected)}", true);
                m_LastUserSelectAt = UnityEngine.Time.realtimeSinceStartup;
                return;
            }
            // A top-level lodge, ski school or patrol hut has no Owner; its sign is the published link (nearest live sign within the
            // tether). Not linked: the Not linked view, with the reason in the line. Move It selections land here too.
            if (m_Attractiveness.TryGetTopLevelLink(selected, out Entity schoolSign))
            {
                if (schoolSign != Entity.Null && m_Ownership.IsLiveGate(schoolSign))
                    Select(schoolSign, $"world selection of {PrefabNameOf(selected)} {Ent(selected)}, linked to the sign " +
                                       $"{Ent(schoolSign)}", true);
                else
                    Select(Entity.Null, $"world selection of {PrefabNameOf(selected)} {Ent(selected)}: not linked " +
                                        $"(no Welcome Sign within {SkiRunOwnershipSystem.TetherMeters:F0} m)", true);
                m_LastUserSelectAt = UnityEngine.Time.realtimeSinceStartup;
                return;
            }
            if (EntityManager.TryGetComponent(selected, out Owner owner) && m_Ownership.IsLiveGate(owner.m_Owner))
            {
                Select(owner.m_Owner, $"world selection of {PrefabNameOf(selected)} {Ent(selected)}, owned by the sign " +
                                      $"{Ent(owner.m_Owner)}", true);
                m_LastUserSelectAt = UnityEngine.Time.realtimeSinceStartup;
                return;
            }
            if (IsStation(selected))
            {
                string how = ResolveStationResort(selected, out Entity sign);
                Select(sign, $"world selection of station {PrefabNameOf(selected)} {Ent(selected)}: {how}", true);
                m_LastUserSelectAt = UnityEngine.Time.realtimeSinceStartup;
            }
            // anything else: unchanged, no line (a click anywhere in the city must not log)
        }

        /// <summary>Station rule: the first census row touching the station decides, through its base
        /// station, else its top station; a station on no row resolves from its own position. Returns the
        /// "how" text for the log. Event time only.</summary>
        private string ResolveStationResort(Entity station, out Entity sign)
        {
            sign = Entity.Null;
            Entity anchor = station;
            string how;
            if (m_Census.TryGetRowOfStation(station, out LiftCensus.Row row))
            {
                Entity b = ToEntity(row.Base), t = ToEntity(row.Top);
                // A row may keep a removed station: skip a dead one.
                if (b != Entity.Null && !EntityManager.Exists(b)) b = Entity.Null;
                if (t != Entity.Null && !EntityManager.Exists(t)) t = Entity.Null;
                anchor = b != Entity.Null ? b : (t != Entity.Null ? t : station);
                how = $"its lift (base {Ent(b)}, top {Ent(t)}) belongs to the nearest sign within " +
                      $"{SkiRunOwnershipSystem.TetherMeters:F0} m of its {(b != Entity.Null ? "base" : "top")} station";
            }
            else
            {
                how = $"on no lift: the nearest sign within {SkiRunOwnershipSystem.TetherMeters:F0} m of the station";
            }
            if (!EntityManager.TryGetComponent(anchor, out Game.Objects.Transform transform))
                return how + " — no Transform, Not linked";
            if (!m_Stats.TryResolveResort(transform.m_Position.xz, out sign))
            {
                sign = Entity.Null;
                return how + " — none, Not linked";
            }
            return how;
        }

        /// <summary>Sets the selected resort and writes the one "Hub [select]" line. <paramref name="always"/>
        /// = log even when unchanged (world selection, selectResort); the event-driven rules log only a
        /// change. <paramref name="writeLine"/> false: no "Hub [select]" line (rule 7 writes its own
        /// "Hub [follow]" line). Returns whether the selection changed.</summary>
        private bool Select(Entity sign, string why, bool always, bool writeLine = true)
        {
            bool changed = sign != m_SelectedResort;
            if (!changed && !always)
                return false;
            m_SelectedResort = sign;
            m_SelectedBinding.Update(sign);   // dedups; pushes only while observed
            LastSelectText = $"{ResortLabel(sign)} ({why})";
            if (writeLine)
                Mod.Log.Info($"Hub [select] {ResortLabel(sign)}{(changed ? "" : " (unchanged)")} ({why})");
            return changed;
        }

        private void ApplySelectResort(Entity sign, string source)
        {
            if (sign == Entity.Null)
            {
                if (!NotLinkedHasItems())
                {
                    Mod.Log.Info($"Hub [select] refused — Not linked was asked for, but nothing is unlinked ({source})");
                    LastTriggerText = "selectResort(Not linked): refused, empty";
                    return;
                }
                Select(Entity.Null, $"selectResort ({source})", true);
                m_LastUserSelectAt = UnityEngine.Time.realtimeSinceStartup;   // rule 3 is a user action (rule 7 guard)
                string stays = source == "ui" ? ": camera " + FocusAfterSwitch(Entity.Null) : "";
                LastTriggerText = "selectResort(Not linked)" + stays;
                return;
            }
            if (!m_Ownership.IsLiveGate(sign))
            {
                Mod.Log.Info($"Hub [select] refused — {Ent(sign)} is not a live Ski Area Welcome Sign ({source})");
                LastTriggerText = $"selectResort {Ent(sign)}: refused";
                return;
            }
            Select(sign, $"selectResort ({source})", true);
            m_LastUserSelectAt = UnityEngine.Time.realtimeSinceStartup;   // rule 3 is a user action (rule 7 guard)
            // FocusAfterSwitch may write LastTriggerText (ApplyFocusBuilding): the switch's text is set after it.
            string camera = source == "ui" ? ": camera " + FocusAfterSwitch(sign) : "";
            LastTriggerText = $"selectResort {Ent(sign)}{camera}";
        }

        /// <summary>After an accepted ‹ › switch only. Writes the one
        /// "Hub [focus]" line of the switch (a fly that fails: ApplyFocusBuilding's WARN). Returns the verdict
        /// for the dev tab ("fly", "already on", "not moved", "fly failed"). Event time only.</summary>
        private string FocusAfterSwitch(Entity sign)
        {
            if (sign == Entity.Null)
            {
                Mod.Log.Info("Hub [focus] switcher: Not linked: camera not moved (the view has no sign; each of its rows " +
                             "flies on click)");
                return "not moved";
            }
            if (CameraAlreadyOn(sign, out string view))
            {
                Mod.Log.Info($"Hub [focus] switcher: already on {ResortLabel(sign)} — {view}; camera not moved");
                return "already on";
            }
            return ApplyFocusBuilding(sign, "switcher: fly — " + view) ? "fly" : "fly failed";
        }

        /// <summary>HubCameraRules.AlreadyOn for <paramref name="sign"/>: the
        /// active controller's pivot and zoom against the point the fly follows (SelectedInfoUISystem
        /// .TryGetPosition, as ApplyFocusBuilding reads it). <paramref name="view"/> = the numbers for the
        /// log. An unknown camera or sign position → false (the caller flies; ApplyFocusBuilding refuses a
        /// sign with no position with its own line). Event time only.</summary>
        private bool CameraAlreadyOn(Entity sign, out string view)
        {
            if (!TryCameraView(out float3 pivot, out float zoom))
            {
                view = "camera state unknown (no active camera controller)";
                return false;
            }
            int element = -1;
            if (!SelectedInfoUISystem.TryGetPosition(sign, EntityManager, ref element, out Entity _, out float3 at,
                    out Bounds3 _, out quaternion _))
            {
                view = $"the sign has no position (camera pivot at {pivot.x:F0}, {pivot.z:F0}, zoom {zoom:F0})";
                return false;
            }
            bool on = HubCameraRules.AlreadyOn(pivot.xz, zoom, at.xz, out float distance);
            view = $"pivot {distance:F0} m away at zoom {zoom:F0}; keep <= {HubCameraRules.KeepRadiusM(zoom):F0} m " +
                   $"at zoom <= {HubCameraRules.KeepMaxZoomM:F0}";
            return on;
        }

        /// <summary>The active controller's pivot and zoom (IGameCameraController.pivot / .zoom,
        /// Game.Rendering/IGameCameraController.cs:8-10; EngineNotes/HubCameraRules.md). False without a
        /// controller. Event time only.</summary>
        private bool TryCameraView(out float3 pivot, out float zoom)
        {
            pivot = default;
            zoom = 0f;
            try
            {
                IGameCameraController controller = m_CameraUpdateSystem.activeCameraController;
                if (controller == null)
                    return false;
                UnityEngine.Vector3 p = controller.pivot;
                pivot = new float3(p.x, p.y, p.z);
                zoom = controller.zoom;
                return true;
            }
            catch (Exception)
            {
                return false;   // a controller being torn down (the CameraPoint precedent): treated as unknown
            }
        }

        /// <summary>Rules 1, 4, 5, 6, the bind notices and rule 7 — only when the stats rows changed
        /// (event-driven: three int compares and rule 7's two field writes per frame otherwise). Rule 7's
        /// candidates are collected in this order (a lift closed, a station placed, a run bound;
        /// the first found wins) and applied last, so a follow can never undo a rule-1/4/5/6 switch of the
        /// same pass.</summary>
        private void ReconcileSelection()
        {
            int resortsRevision = m_Stats.ResortsRevision;
            if (resortsRevision != m_ReconciledResortsRevision)
            {
                m_ReconciledResortsRevision = resortsRevision;
                ReconcileResorts();
            }
            int liftsRevision = m_Stats.LiftsRevision;
            if (liftsRevision != m_NoticedLiftsRevision)
            {
                m_NoticedLiftsRevision = liftsRevision;
                NoticeLiftCloses();
                NoticeStations();
            }
            int runsRevision = m_Stats.RunsRevision;
            if (runsRevision != m_NoticedRunsRevision)
            {
                m_NoticedRunsRevision = runsRevision;
                NoticeBinds();
            }
            ApplyFollow();
        }

        /// <summary>Rule 7's candidate: the first one of a pass wins.</summary>
        private void OfferFollow(Entity sign, string reason)
        {
            if (m_FollowCandidate.Sign == Entity.Null && sign != Entity.Null && sign != m_SelectedResort)
                m_FollowCandidate = new FollowCandidate { Sign = sign, Reason = reason };
        }

        /// <summary>Rule 7: the last statement of ReconcileSelection, every frame;
        /// with no candidate it only clears the pass flag. Guards: HubRules.FollowCheck and a live target
        /// sign. A suppressed follow writes no line and is counted per reason (DUMP).</summary>
        private void ApplyFollow()
        {
            FollowCandidate c = m_FollowCandidate;
            m_FollowCandidate = default;
            bool ruleSwitched = m_RuleSwitchedThisPass;
            m_RuleSwitchedThisPass = false;
            if (c.Sign == Entity.Null || c.Sign == m_SelectedResort || !m_Ownership.IsLiveGate(c.Sign))
                return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            switch (HubRules.FollowCheck(m_HubOpen.value, ruleSwitched, now, m_LastFollowAt, m_LastUserSelectAt))
            {
                case FollowVerdict.HubClosed: FollowsSuppressedClosed++; return;
                case FollowVerdict.RuleSwitched: FollowsSuppressedRule++; return;
                case FollowVerdict.UserJustSelected: FollowsSuppressedUser++; return;
                case FollowVerdict.TooSoon: FollowsSuppressedSoon++; return;
            }
            Entity old = m_SelectedResort;
            Select(c.Sign, "D36 follow: " + c.Reason, always: false, writeLine: false);
            m_LastFollowAt = now;
            m_FollowBinding.Update(++m_FollowCount);
            Mod.Log.Info($"Hub [follow] now showing '{AreaName(c.Sign)}' ({Ent(c.Sign)}) — {c.Reason}; was '{AreaName(old)}'");
        }

        private void ReconcileResorts()
        {
            int count = m_Stats.ResortCount;
            if (count <= 0)
                return;   // no rebuild yet this load (the Not linked row is always sent once rebuilt)
            m_SignsScratch.Clear();
            Entity first = Entity.Null, firstNew = Entity.Null;
            int newSigns = 0;
            bool notLinkedEmpty = true;
            for (int i = 0; i < count; i++)
            {
                ResortStats r = m_Stats.GetResort(i);
                if ((int)r.Kind == ResortKindNotLinked)
                {
                    notLinkedEmpty = !(r.Runs > 0 || r.LiftRows > 0 || r.Lodges > 0);
                    continue;
                }
                Entity s = r.Sign;
                if (s == Entity.Null)
                    continue;
                m_SignsScratch.Add(s);
                if (first == Entity.Null)
                    first = s;
                if (m_SignBaselineSet && !m_SignsKnown.Contains(s))
                {
                    newSigns++;
                    if (firstNew == Entity.Null)
                        firstNew = s;
                }
            }
            bool baseline = !m_SignBaselineSet;
            HashSet<Entity> swap = m_SignsKnown;
            m_SignsKnown = m_SignsScratch;
            m_SignsScratch = swap;
            m_SignBaselineSet = true;

            // Rules 1/4/5/6 mark the pass so rule 7 never undoes them.
            if (baseline)
            {
                if (m_SelectedResort == Entity.Null && first != Entity.Null)
                    m_RuleSwitchedThisPass |= Select(first, "first stats rebuild of this load: the first sign in stable order", false);   // rule 1
            }
            else if (firstNew != Entity.Null)
            {
                m_RuleSwitchedThisPass |= Select(firstNew, newSigns == 1 ? "new sign" : $"new sign, the first of {newSigns} new", false);   // rule 4
            }
            if (m_SelectedResort != Entity.Null && !m_Ownership.IsLiveGate(m_SelectedResort))
            {
                m_RuleSwitchedThisPass |= Select(first, $"the selected sign {Ent(m_SelectedResort)} is gone", false);   // rule 5 (first may be Null)
            }
            if (m_SelectedResort == Entity.Null && notLinkedEmpty && first != Entity.Null)
            {
                m_RuleSwitchedThisPass |= Select(first, "the Not linked view is empty", false);   // rule 6
            }
        }

        /// <summary>A run that newly bound to a sign other than the selected resort.</summary>
        private void NoticeBinds()
        {
            int count = m_Stats.RunCount;
            if (!m_RunBaselineSet)
            {
                if (m_Stats.ResortCount <= 0)
                    return;   // no rebuild yet this load
                m_RunBaselineSet = true;
                m_RunSigns.Clear();
                for (int i = 0; i < count; i++)
                {
                    RunStats r = m_Stats.GetRun(i);
                    m_RunSigns[r.Entity] = r.Sign;
                }
                return;
            }
            Entity selected = m_SelectedResort;
            bool selectedLive = selected != Entity.Null && m_Ownership.IsLiveGate(selected);
            int noticed = 0;
            m_RunSignsScratch.Clear();
            for (int i = 0; i < count; i++)
            {
                RunStats r = m_Stats.GetRun(i);
                m_RunSignsScratch[r.Entity] = r.Sign;
                if (r.Sign == Entity.Null || r.Sign == selected)
                    continue;
                bool hadSign = m_RunSigns.TryGetValue(r.Entity, out Entity before) && before != Entity.Null;
                if (hadSign)
                    continue;
                // Rule 7: a run newly bound in another ski area than the shown one, the Not linked view included (it is not a ski area).
                OfferFollow(r.Sign, $"run '{r.Name}' bound to it");
                if (!selectedLive)
                    continue;
                noticed++;
                if (noticed <= Tuning.BindNoticesPerPass)
                {
                    Mod.Log.Info($"Hub [bind-notice] run '{r.Name}' {Ent(r.Entity)} bound to {ResortLabel(r.Sign)} while " +
                                 $"{ResortLabel(selected)} was selected (nearest sign wins)");
                }
            }
            if (noticed > Tuning.BindNoticesPerPass)
            {
                Mod.Log.Info($"Hub [bind-notice] … and {noticed - Tuning.BindNoticesPerPass} more run(s) bound to another " +
                             $"sign than {ResortLabel(selected)} (BindNoticesPerPass)");
            }
            Dictionary<Entity, Entity> swap = m_RunSigns;
            m_RunSigns = m_RunSignsScratch;
            m_RunSignsScratch = swap;
        }

        /// <summary>A lift row that became complete (LiftStats.State 0
        /// Running or 1 Static) and was not complete under the same (entity, top) key at the previous rows —
        /// "closed again" when the key was there (a gap redrawn), "closed" when it is new (a first line, or a
        /// station re-placed: a new key, the census's own rule) — in a resort other than the one shown. Running
        /// ↔ Static is not a close. Log only; the BindNoticesPerPass cap and summary line of the runs. Runs on
        /// LiftsRevision (a few times a minute while the panel is open: chairs riding move it); O(lifts),
        /// reused dictionaries.</summary>
        private void NoticeLiftCloses()
        {
            int count = m_Stats.LiftCount;
            if (!m_LiftBaselineSet)
            {
                if (m_Stats.ResortCount <= 0)
                    return;   // no rebuild yet this load
                m_LiftBaselineSet = true;
                m_LiftStates.Clear();
                for (int i = 0; i < count; i++)
                {
                    LiftStats r = m_Stats.GetLift(i);
                    m_LiftStates[(r.Entity, r.Top)] = r.State;
                }
                return;
            }
            Entity selected = m_SelectedResort;
            int noticed = 0;
            m_LiftStatesScratch.Clear();
            for (int i = 0; i < count; i++)
            {
                LiftStats r = m_Stats.GetLift(i);
                m_LiftStatesScratch[(r.Entity, r.Top)] = r.State;
                if (!LiftComplete(r.State) || r.Sign == selected)
                    continue;
                bool had = m_LiftStates.TryGetValue((r.Entity, r.Top), out int before);
                if (had && LiftComplete(before))
                    continue;
                OfferFollow(r.Sign, $"lift '{r.Name}' closed in it");   // rule 7; Null sign: no candidate
                noticed++;
                if (noticed <= Tuning.BindNoticesPerPass)
                {
                    Mod.Log.Info($"Hub [bind-notice] lift '{r.Name}' (base {Ent(r.Entity)}, top {Ent(r.Top)}) " +
                                 $"{(had ? "closed again" : "closed")} in {ResortLabel(r.Sign)} while " +
                                 $"{ResortLabel(selected)} was selected (a lift belongs to the nearest sign within " +
                                 $"{SkiRunOwnershipSystem.TetherMeters:F0} m of its base station)");
                }
            }
            if (noticed > Tuning.BindNoticesPerPass)
            {
                Mod.Log.Info($"Hub [bind-notice] … and {noticed - Tuning.BindNoticesPerPass} more lift(s) closed in another " +
                             $"resort than {ResortLabel(selected)} (BindNoticesPerPass)");
            }
            Dictionary<(Entity, Entity), int> swap = m_LiftStates;
            m_LiftStates = m_LiftStatesScratch;
            m_LiftStatesScratch = swap;
        }

        /// <summary>Rule 7: the live stations of the lift rows ({row id | HasBase} ∪
        /// {top | HasTop}); the first call of a load with stats rows sets the baseline (no candidate, the
        /// NoticeBinds pattern); later a station not seen before, whose row belongs to a live sign other than
        /// the shown one, offers that sign. The two sets are swapped (no allocation). Runs on LiftsRevision.</summary>
        private void NoticeStations()
        {
            int count = m_Stats.LiftCount;
            if (!m_StationBaselineSet && m_Stats.ResortCount <= 0)
                return;   // no rebuild yet this load
            bool baseline = !m_StationBaselineSet;
            m_StationBaselineSet = true;
            m_StationsScratch.Clear();
            for (int i = 0; i < count; i++)
            {
                LiftStats r = m_Stats.GetLift(i);
                if (r.HasBase)
                    NoticeStation(r.Entity, r.Sign, baseline);
                if (r.HasTop)
                    NoticeStation(r.Top, r.Sign, baseline);
            }
            HashSet<Entity> swap = m_StationsKnown;
            m_StationsKnown = m_StationsScratch;
            m_StationsScratch = swap;
        }

        private void NoticeStation(Entity station, Entity sign, bool baseline)
        {
            if (station == Entity.Null || !m_StationsScratch.Add(station) || baseline || m_StationsKnown.Contains(station))
                return;
            if (sign != Entity.Null && sign != m_SelectedResort && m_Ownership.IsLiveGate(sign))
                OfferFollow(sign, $"station {Ent(station)} placed in it");
        }

        /// <summary>LiftStats.State 0 Running or 1 Static: the census row is Complete (SkiResortStatsSystem
        /// CollectLifts).</summary>
        private static bool LiftComplete(int state) => state == 0 || state == 1;

        private bool NotLinkedHasItems()
        {
            int count = m_Stats.ResortCount;
            for (int i = 0; i < count; i++)
            {
                ResortStats r = m_Stats.GetResort(i);
                if ((int)r.Kind == ResortKindNotLinked && (r.Runs > 0 || r.LiftRows > 0 || r.Lodges > 0))
                    return true;
            }
            return UnlinkedSchools() > 0;   // a top-level building beyond every sign's tether belongs to Not linked
        }

        /// <summary>Live top-level resort buildings (lodges, the school, patrol huts) linked to no sign (CopyTopLevelLinks,
        /// the last recompute). Event time only.</summary>
        private int UnlinkedSchools()
        {
            m_Attractiveness.CopyTopLevelLinks(m_SchoolScratch, m_SchoolSignScratch);
            int n = 0;
            for (int k = 0; k < m_SchoolScratch.Count; k++)
            {
                Entity school = m_SchoolScratch[k];
                if (m_SchoolSignScratch[k] == Entity.Null && school != Entity.Null && EntityManager.Exists(school) &&
                    !EntityManager.HasComponent<Deleted>(school))
                    n++;
            }
            return n;
        }

        /// <summary>"'Ski area 1' 124274:35", or "Not linked". Event time only (a linear scan of ≤ a
        /// handful of resort rows).</summary>
        private string ResortLabel(Entity sign)
        {
            if (sign == Entity.Null)
                return "Not linked";
            int count = m_Stats.ResortCount;
            for (int i = 0; i < count; i++)
            {
                ResortStats r = m_Stats.GetResort(i);
                if (r.Sign == sign)
                    return $"'{r.Name}' {Ent(sign)}";
            }
            if (m_NameSystem.TryGetCustomName(sign, out string custom) && !string.IsNullOrEmpty(custom))
                return $"'{custom}' {Ent(sign)}";
            return $"sign {Ent(sign)}";
        }

        private void ApplyRename(Entity entity, string text, string source)
        {
            string what = NameableKind(entity);
            if (what == null)
            {
                Mod.Log.Info($"Hub [rename] refused — {Ent(entity)} ({PrefabNameOf(entity)}) is not a live sign, run or " +
                             $"lift station of ours (a lift row without a station cannot be renamed) ({source})");
                LastTriggerText = $"rename {Ent(entity)}: refused";
                return;
            }
            string name = CleanName(text);
            string before = CurrentName(entity, what);
            string note = "";
            if (what == "sign" && name.Length == 0)
            {
                // A sign's "default" is a fresh auto-name, written (Engine notes: Rename).
                string auto = m_BindTrigger.PickSignName(entity);
                if (auto != null)
                {
                    name = auto;
                    note = " (empty name: a fresh default name, written so the sign's panel shows it too)";
                }
            }
            else if (what == "station" && name.Length > 0)
            {
                string stripped = StripSharedBaseSuffix(entity, name);
                if (!ReferenceEquals(stripped, name))
                {
                    note = $" (the shared-station label's '{name.Substring(stripped.Length)}' was not stored)";
                    name = stripped;
                }
            }
            m_NameSystem.SetCustomName(entity, name);   // "" / whitespace removes it (NameSystem.cs:224-233)
            if (what == "run")
                m_Catalog.RequestRebuild("hub rename");
            m_Stats.RequestRebuild("hub rename");   // rebuilt next frame, logged with this reason
            string after = name.Length == 0 ? "(default name)" : "'" + name + "'";
            Mod.Log.Info($"Hub [rename] {what} {Ent(entity)} '{before}' → {after}{note} ({source})");
            LastTriggerText = $"rename {what} {Ent(entity)} → {after}";
        }

        /// <summary>A base station shared by two or more lift rows is labeled "&lt;name&gt; → &lt;top label&gt;"
        /// (SkiResortStatsSystem.LiftNameSeparator), and the rename field starts from that label. When <paramref name="name"/> ends
        /// with the separator + top label of one of that station's rows, the suffix is dropped, so an in-place edit stores the name,
        /// not the label (it would compound on every edit). Returns <paramref name="name"/> itself (same reference) when nothing
        /// matched. Event time only.</summary>
        private string StripSharedBaseSuffix(Entity station, string name)
        {
            int rows = 0;
            for (int i = 0; i < m_Stats.LiftCount; i++)
            {
                if (m_Stats.GetLift(i).Entity == station)
                    rows++;
            }
            if (rows < 2 || !m_NameSystem.TryGetCustomName(station, out string current) || string.IsNullOrEmpty(current))
                return name;
            string prefix = current + SkiResortStatsSystem.LiftNameSeparator;
            for (int i = 0; i < m_Stats.LiftCount; i++)
            {
                LiftStats r = m_Stats.GetLift(i);
                if (r.Entity != station || r.Name == null || r.Name.Length <= prefix.Length ||
                    !r.Name.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                string suffix = r.Name.Substring(current.Length);   // " → <top label>"
                if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
                    return name.Substring(0, name.Length - suffix.Length).TrimEnd();
            }
            return name;
        }

        /// <summary>"sign" / "run" / "station", or null when the entity is none of ours (or dying).</summary>
        private string NameableKind(Entity entity)
        {
            if (m_Ownership.IsLiveGate(entity))
                return "sign";
            if (entity == Entity.Null || !EntityManager.Exists(entity) || EntityManager.HasComponent<Deleted>(entity) ||
                !EntityManager.TryGetComponent(entity, out PrefabRef prefabRef))
                return null;
            if (m_Ownership.IsRunPrefab(prefabRef.m_Prefab))
                return "run";
            if (m_PrefabCatalog.IsStation(prefabRef.m_Prefab))
                return "station";
            return null;
        }

        /// <summary>Trim, cut to RenameMaxChars UTF-16 units without splitting a surrogate pair.</summary>
        private static string CleanName(string text)
        {
            string name = (text ?? "").Trim();
            if (name.Length > Tuning.RenameMaxChars)
            {
                int cut = Tuning.RenameMaxChars;
                if (char.IsHighSurrogate(name[cut - 1]))
                    cut--;
                name = name.Substring(0, cut).TrimEnd();
            }
            return name;
        }

        /// <summary>The name the hub shows now (stats rows), else the custom name, else "(default)".
        /// Event time only.</summary>
        private string CurrentName(Entity entity, string what)
        {
            if (what == "sign")
            {
                for (int i = 0; i < m_Stats.ResortCount; i++)
                {
                    ResortStats r = m_Stats.GetResort(i);
                    if (r.Sign == entity)
                        return r.Name ?? "";
                }
            }
            else if (what == "run")
            {
                for (int i = 0; i < m_Stats.RunCount; i++)
                {
                    RunStats r = m_Stats.GetRun(i);
                    if (r.Entity == entity)
                        return r.Name ?? "";
                }
            }
            else
            {
                for (int i = 0; i < m_Stats.LiftCount; i++)
                {
                    LiftStats r = m_Stats.GetLift(i);
                    if (r.Entity == entity || r.Top == entity)
                        return r.Name ?? "";
                }
            }
            return m_NameSystem.TryGetCustomName(entity, out string custom) ? custom : "(default)";
        }

        private void ApplyHello(Request r)
        {
            int contract = r.Int;
            string bundle = Cut(r.Text ?? "(null)");
            string dll = s_DllStamp ?? "(null)";
            bool contractOk = contract == HubContract;
            bool stampOk = string.Equals(bundle, dll, StringComparison.Ordinal);
            bool uiOnly = bundle.StartsWith(StandaloneStampPrefix, StringComparison.Ordinal);
            m_UiContractOk = contractOk;   // the piste map draws only for a UI of this contract (kept across loads)
            if (!contractOk)
            {
                HandshakeText = $"CONTRACT MISMATCH: bundle {contract}, DLL {HubContract}; stamps bundle {bundle} / DLL {dll}";
                Mod.Log.Error($"!!! UI hub contract MISMATCH: bundle contract {contract}, DLL contract {HubContract} — the " +
                              "panel shows its mismatch line: building and the tree controls still work, the resort " +
                              $"statistics are hidden (hub-u2-spec §4.5). The UI bundle (stamp {bundle}) and the DLL " +
                              $"(stamp {dll}) come from different versions: rebuild with dotnet build, or reinstall");
            }
            else if (stampOk)
            {
                HandshakeText = $"connected: contract {contract}, stamp {bundle} (same build as the DLL)";
                Mod.Log.Info($"UI hub connected: contract {contract} (DLL contract {HubContract}), stamp {bundle} " +
                             "(same build as the DLL)");
            }
            else if (uiOnly)
            {
                HandshakeText = $"connected: contract {contract}, UI-only stamp {bundle} (DLL {dll})";
                Mod.Log.Info($"UI hub connected: contract {contract} (DLL contract {HubContract}), stamp {bundle} — a " +
                             $"UI-only build (npm run build / dev) beside DLL stamp {dll}; expected while iterating on the UI");
            }
            else
            {
                HandshakeText = $"connected, STAMP DIFFERS: bundle {bundle}, DLL {dll} (contract {contract} matches)";
                Mod.Log.Error($"!!! UI hub connected with a bundle from a DIFFERENT build: contract {contract} matches, " +
                              $"but bundle stamp {bundle} != DLL stamp {dll} — SkiResortMod.mjs beside the DLL is " +
                              "not the one this DLL was built with (a stale deploy?)");
            }
            LastTriggerText = "uiHello: " + HandshakeText;
        }

        private void ApplyUiError(Request r)
        {
            string message = Cut(r.Text ?? "(null)");
            if (m_UiErrors.Contains(message))
            {
                UiErrorRepeats++;   // logged once per distinct message
                return;
            }
            if (m_UiErrors.Count >= Tuning.UiErrorsMax)
            {
                UiErrorRepeats++;
                if (!m_UiErrorCapLogged)
                {
                    m_UiErrorCapLogged = true;
                    Mod.Log.Error($"!!! UI error: more than {Tuning.UiErrorsMax} distinct messages this load — further " +
                                  "ones are only counted (browser console / UI.log have them)");
                }
                return;
            }
            m_UiErrors.Add(message);
            Mod.Log.Error($"!!! UI error reported by the Ski Resort UI module: {message}");
            LastTriggerText = "uiError: " + message;
        }

        /// <summary>Logged only when the measurement string changed; capped per load.</summary>
        private void ApplyLayout(string text)
        {
            string layout = Cut(text ?? "(null)");
            if (string.Equals(layout, m_LastLayout, StringComparison.Ordinal))
                return;
            m_LastLayout = layout;
            LastTriggerText = "uiLayout: " + layout;
            if (m_LayoutLines < Tuning.LayoutLinesMax)
            {
                m_LayoutLines++;
                Mod.Log.Info($"Hub [layout] {layout}");
            }
            else if (!m_LayoutCapLogged)
            {
                m_LayoutCapLogged = true;
                Mod.Log.Info($"Hub [layout] {Tuning.LayoutLinesMax} layout lines this load (LayoutLinesMax) — further " +
                             "changes are kept for the DUMP only");
            }
        }

        /// <summary>A raw binding writer threw. It runs on subscribe, outside OnUpdate's try, so it must not escape into the UI's binding
        /// dispatch: the panel gets an empty array and the first failure per binding is logged. Tokens the writer had already emitted
        /// cannot be taken back.</summary>
        private void WriterFailed(IJsonWriter writer, string binding, Exception e)
        {
            if (m_WriterFailed.Add(binding))
            {
                Mod.Log.Error($"!!! Hub binding writer '{binding}' EXCEPTION — sent an empty array instead; this binding logs once per game " +
                              $"load. Please report this stack:\n{e}");
            }
            writer.ArrayBegin(0u);
            writer.ArrayEnd();
        }

        private static string Cut(string text)
        {
            return text.Length <= Tuning.TextMax ? text : text.Substring(0, Tuning.TextMax) + "…";
        }

        private static int IndexOfItem(string id)
        {
            if (id == null)
                return -1;
            for (int i = 0; i < ItemCount; i++)
            {
                if (string.Equals(Items[i].Id, id, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }

        private void ApplyLaunch(string id, string source)
        {
            m_LocksDirty = true;
            int item = IndexOfItem(id);
            if (item < 0)
            {
                // The id comes from the UI, so it is cut, and one line per load is enough.
                string shown = Cut(id ?? "(null)");
                if (!m_UnknownLaunchLogged)
                {
                    m_UnknownLaunchLogged = true;
                    Mod.Log.Warn($"Hub [launch] '{shown}' — not a build id of this DLL (contract {HubContract}); " +
                                 $"nothing started ({source}); further unknown ids this load are not logged");
                }
                LastTriggerText = $"launch '{shown}': unknown id";
                return;
            }
            PrefabBase prefab = m_ItemPrefab[item];
            if (prefab == null)
            {
                // A pending item that is MISSING is never sent, so only the dev tab (every id) or an
                // older bundle can ask for it; the line says which case it is.
                Mod.Log.Warn($"Hub [launch] {id} refused — prefab '{Items[item].Prefab}' was not found this load " +
                             (SkiCatalog.IsExpected(Items[item].Prefab)
                                 ? "(asset pack missing?)"
                                 : "(pending in SkiCatalog: not imported yet, so the hub does not offer it)") +
                             $" ({source})");
                LastTriggerText = $"launch {id}: prefab missing";
                return;
            }
            if (!m_ItemSent[item])
            {
                // Found but not offered = an item of a Shuttle lift type: that happens only while TramShuttleSystem is not registered.
                Mod.Log.Info($"Hub [launch] {id} refused — not offered this load (lift type '{Items[item].LiftTypeId}' " +
                             $"moves as a Shuttle and TramShuttleSystem is not registered) ({source})");
                LastTriggerText = $"launch {id}: not offered";
                return;
            }
            if (item == ActiveItemIndex())
            {
                m_ToolSystem.activeTool = m_DefaultTool;   // ToolSystem.cs:84-98
                Mod.Log.Info($"Hub [launch] {id} was the active tool -> default tool ({source})");
                LastTriggerText = $"launch {id}: was active, stopped";
                return;
            }
            if (EntityManager.HasEnabledComponent<Locked>(m_ItemPrefabEntity[item]))
            {
                Mod.Log.Info($"Hub [launch] {id} refused — locked (progression; the vanilla toolbar refuses it too) " +
                             $"({source})");
                LastTriggerText = $"launch {id}: locked";
                return;
            }
            // A lift-line tile opens THE lift tool (LiftLineToolSystem) for its type; the vanilla net tool
            // (quick-place) is only the fallback when the lift tool is unusable (disabled after an exception, not armed).
            if (IsLineItem(item) && m_LiftLineTool != null && m_LiftLineTool.Usable)
            {
                LiftTypeDef lineType = SkiCatalog.LiftById(Items[item].LiftTypeId);
                bool opened = m_LiftLineTool.TryActivate("hub tile " + id + SourceSuffix(source), lineType);
                Mod.Log.Info($"Hub [launch] {id} -> lift tool (type {lineType?.Id ?? "any"}) ok={opened} ({source})");
                LastTriggerText = $"launch {id}: {(opened ? "lift tool" : "lift tool refused")}";
                if (opened)
                    return;
            }
            // The Cat track tile opens THE cat track tool (CatTrackGradeToolSystem: grades the corridor and builds the run in one action,
            // 2026-10-24) and nothing else: every cat track is tool-built, which is what makes it green by construction
            // (SkiRunRatingRules.CatTrackBand). No area-tool fallback; the tool logs its own refusal (disabled after an exception, not armed).
            if (IsCatTrackItem(item))
            {
                bool opened = m_GradeTool != null && m_GradeTool.TryActivate("hub tile " + id + SourceSuffix(source));
                Mod.Log.Info($"Hub [launch] {id} -> cat track tool ok={opened} ({source})");
                LastTriggerText = $"launch {id}: {(opened ? "cat track tool" : "cat track tool refused")}";
                return;
            }
            // An upgrade-kind row whose prefab is top-level (the ski school) is placed like a station; the hub keeps its
            // soft locks (NoSign, one school per sign) and names the sign it will link to.
            string link = "";
            if (Items[item].IsUpgrade)
            {
                if (!TopLevelUpgradeLaunchAllowed(item, id, source, out link))
                    return;
            }

            bool ok = m_ToolSystem.ActivatePrefabTool(prefab);   // ToolSystem.cs:263-277
            ToolBaseSystem tool = m_ToolSystem.activeTool;
            string line = $"Hub [launch] {id} -> {ToolId(tool)} mode {ModeText(tool)} locked=False ok={ok}{link} ({source})";
            if (ok)
                Mod.Log.Info(line);
            else
                Mod.Log.Warn(line + " — no tool accepted the prefab; ActivatePrefabTool switched to the default tool");
            LastTriggerText = $"launch {id}: {(ok ? "ok" : "no tool took it")} -> {ToolId(tool)}";
        }

        /// <summary>ReasonAlreadyBuilt when an upgrade-kind <paramref name="item"/> already has a building linked to
        /// <paramref name="sign"/> (TopLevelConflict); ReasonNone otherwise or for a non-upgrade item. <paramref name="conflict"/> =
        /// the building that decides. Event time (RefreshLocks cadence).</summary>
        private byte UpgradeConflict(Entity sign, int item, out Entity conflict)
        {
            conflict = Entity.Null;
            if (sign == Entity.Null || item < 0 || item >= ItemCount || !Items[item].IsUpgrade)
                return ReasonNone;
            return TopLevelConflict(sign, item, out conflict);
        }

        /// <summary>The hub's SOFT one-per-sign lock for an upgrade-kind row (top-level, so the engine has no AlreadyUpgraded rule
        /// for it); a second school adds nothing (SchoolActive is a flag). ReasonAlreadyBuilt when the row is
        /// ForbidMultiple (m_ItemForbidMultiple, ReadItemValues) and a live school of
        /// the same prefab is linked to <paramref name="sign"/> (SkiResortAttractivenessSystem.CopyTopLevelLinks, the last
        /// recompute). Event time only (RefreshLocks cadence, launches).</summary>
        private byte TopLevelConflict(Entity sign, int item, out Entity conflict)
        {
            conflict = Entity.Null;
            Entity prefab = m_ItemPrefabEntity[item];
            if (!m_ItemForbidMultiple[item] || prefab == Entity.Null)
                return ReasonNone;
            m_Attractiveness.CopyTopLevelLinks(m_SchoolScratch, m_SchoolSignScratch);
            for (int k = 0; k < m_SchoolScratch.Count; k++)
            {
                Entity school = m_SchoolScratch[k];
                if (m_SchoolSignScratch[k] != sign || school == Entity.Null || !EntityManager.Exists(school) ||
                    EntityManager.HasComponent<Deleted>(school) || EntityManager.HasComponent<Temp>(school) ||
                    !EntityManager.TryGetComponent(school, out PrefabRef prefabRef) || prefabRef.m_Prefab != prefab)
                    continue;
                conflict = school;
                return ReasonAlreadyBuilt;
            }
            return ReasonNone;
        }

        /// <summary>The hub's soft checks before a top-level upgrade-kind item (the ski school) goes on the cursor like a
        /// station. The sign = the shown ski area's, else the one nearest the camera. Refused (one line, false): no Welcome Sign in
        /// the city (REASON[NoSign]: it would count for no ski area), or that sign already has its school (TopLevelConflict). Else
        /// <paramref name="link"/> = the launch line's tail naming the sign nearest the camera and its distance: the school links to
        /// the nearest sign within the tether where it is PLACED (SchoolLinkRules.Resolve, SkiRunOwnershipSystem.TetherMeters).</summary>
        private bool TopLevelUpgradeLaunchAllowed(int item, string id, string source, out string link)
        {
            link = "";
            float3 at = CameraPoint();
            // TryNearestGateEntity returns false beyond the tether with the nearest sign still set: only no sign at all refuses
            m_Ownership.TryNearestGateEntity(at.xz, out Entity nearest, out float distance);
            if (nearest == Entity.Null)
            {
                Mod.Log.Info($"Hub [launch] {id} refused — no Ski Area Welcome Sign in this city (place one first: it links " +
                             $"to the nearest sign within {SkiRunOwnershipSystem.TetherMeters:F0} m) ({source})");
                LastTriggerText = $"launch {id}: no sign";
                return false;
            }
            bool shownLive = m_SelectedResort != Entity.Null && m_Ownership.IsLiveGate(m_SelectedResort);
            Entity sign = shownLive ? m_SelectedResort : nearest;
            if (TopLevelConflict(sign, item, out Entity other) != ReasonNone)
            {
                Mod.Log.Info($"Hub [launch] {id} refused — {(shownLive ? "the shown ski area's" : "the nearest")} sign " +
                             $"{Ent(sign)} already has {Ent(other)} {PrefabNameOf(other)} linked (one per sign, the hub's lock; a " +
                             $"second adds nothing) ({source})");
                LastTriggerText = $"launch {id} on {Ent(sign)}: refused, already built";
                return false;
            }
            string fate = Items[item].Kind == BuildItemKind.Depot
                ? "links to the nearest sign at any distance where placed"
                : distance <= SkiRunOwnershipSystem.TetherMeters
                ? "links to the nearest sign within " + SkiRunOwnershipSystem.TetherMeters.ToString("F0", CultureInfo.InvariantCulture) + " m where placed"
                : "beyond the tether here: place it within " + SkiRunOwnershipSystem.TetherMeters.ToString("F0", CultureInfo.InvariantCulture) +
                  " m of a sign or it counts for no ski area";
            link = $" top-level (T100: ordinary placement, no Upgrade mode); nearest sign {ResortLabel(nearest)} " +
                   $"{distance.ToString("F0", CultureInfo.InvariantCulture)} m from the camera, {fate}" +
                   (shownLive && sign != nearest ? $"; the hub shows {ResortLabel(sign)}" : "");
            return true;
        }

        /// <summary>Where the camera looks (the active controller's pivot), else the camera position.
        /// Event time only (upgrade-kind launch).</summary>
        private float3 CameraPoint()
        {
            try
            {
                IGameCameraController controller = m_CameraUpdateSystem.activeCameraController;
                if (controller != null)
                {
                    UnityEngine.Vector3 p = controller.pivot;
                    return new float3(p.x, p.y, p.z);
                }
            }
            catch (Exception)
            {
                // a controller being torn down: fall back to the camera position
            }
            return m_CameraUpdateSystem.position;
        }

        /// <summary>The build item whose tool is active, or -1 (Engine notes: Launching, "Active"). Per frame while the build section is open:
        /// two field reads and up to ItemCount reference compares.</summary>
        private int ActiveItemIndex()
        {
            ToolBaseSystem tool = m_ToolSystem.activeTool;
            if (tool == null || tool == m_DefaultTool)
                return -1;
            if (m_LiftLineTool != null && tool == m_LiftLineTool)   // The lift tool lights its type's line tile
            {
                LiftTypeDef t = m_LiftLineTool.PresetType;
                for (int i = 0; t != null && i < ItemCount; i++)
                    if (IsLineItem(i) && Items[i].LiftTypeId == t.Id)
                        return i;
                return -1;
            }
            if (m_GradeTool != null && tool == m_GradeTool)   // The cat track tool lights the Cat track tile
            {
                for (int i = 0; i < ItemCount; i++)
                    if (IsCatTrackItem(i))
                        return i;
                return -1;
            }
            PrefabBase active = tool.GetPrefab();
            if (ReferenceEquals(active, null))
                return -1;
            for (int i = 0; i < ItemCount; i++)
            {
                if (ReferenceEquals(active, m_ItemPrefab[i]))
                    return i;
            }
            return -1;
        }

        private static string ToolId(ToolBaseSystem tool)
        {
            return tool == null ? "none" : tool.toolID;   // ToolBaseSystem.cs:142
        }

        private static string ModeText(ToolBaseSystem tool)
        {
            switch (tool)
            {
                case ObjectToolSystem o:
                    return o.actualMode.ToString();
                case AreaToolSystem a:
                    return a.actualMode.ToString();
                case NetToolSystem n:
                    return n.actualMode.ToString();
                case null:
                    return "-";
                default:
                    return tool.uiModeIndex.ToString(CultureInfo.InvariantCulture);   // ToolBaseSystem.cs:144
            }
        }

        private void ApplyFocusRun(Entity run, string source)
        {
            if (!EntityManager.Exists(run) || EntityManager.HasComponent<Deleted>(run) ||
                !EntityManager.TryGetComponent(run, out PrefabRef prefabRef) || m_Ownership.KindOf(prefabRef.m_Prefab) < 0 ||
                !EntityManager.TryGetComponent(run, out Game.Areas.Geometry geometry))
            {
                Mod.Log.Info($"Hub [focus] refused — {Ent(run)} is not a live ski run of ours ({source})");
                LastTriggerText = $"focusRun {Ent(run)}: refused";
                return;
            }
            float2 half = MathUtils.Extents(geometry.m_Bounds.xz);
            if (!Fly(run, default, math.length(half) * Tuning.FocusZoomFactor, out float zoom, out string failure))
            {
                Mod.Log.Warn($"Hub [focus] run {Ent(run)} refused — {failure} ({source})");
                LastTriggerText = $"focusRun {Ent(run)}: {failure}";
                return;
            }
            string name = RunName(run);
            Mod.Log.Info($"Hub [focus] run {Ent(run)} \"{name}\" zoom={zoom:F0} (outline {half.x * 2f:F0} × " +
                         $"{half.y * 2f:F0} m, center {geometry.m_CenterPosition.x:F0}, {geometry.m_CenterPosition.z:F0}) ({source})");
            LastTriggerText = $"focusRun {Ent(run)} \"{name}\": zoom {zoom:F0}";
        }

        /// <summary>focusBuilding (the engine notes pin and the list rows) and the switcher's fly, with the reason in
        /// <paramref name="source"/>. True when the camera flew. One line either way.</summary>
        private bool ApplyFocusBuilding(Entity building, string source)
        {
            string what = null;
            if (m_Ownership.IsLiveGate(building))
                what = "sign";
            else if (EntityManager.Exists(building) && !EntityManager.HasComponent<Deleted>(building) &&
                     EntityManager.TryGetComponent(building, out PrefabRef prefabRef))
            {
                if (m_Attractiveness.IsLodgePrefab(prefabRef.m_Prefab))
                    what = "lodge";
                else if (m_PrefabCatalog.IsStation(prefabRef.m_Prefab))
                    what = "station";
                else if (m_PrefabCatalog.IsUpgradeBuilding(prefabRef.m_Prefab))   // The school, a patrol hut
                    what = "upgrade building";
                else if (EntityManager.HasComponent<SkiMergedLodge>(building))   // contract 32: the Merged buildings fold
                    what = "merged building";
            }
            if (what == null)
            {
                Mod.Log.Info($"Hub [focus] refused — {Ent(building)} ({PrefabNameOf(building)}) is not a live sign, lodge, " +
                             $"school, patrol hut, lift station or merged building of ours ({source})");
                LastTriggerText = $"focusBuilding {Ent(building)}: refused";
                return false;
            }
            int element = -1;
            if (!SelectedInfoUISystem.TryGetPosition(building, EntityManager, ref element, out Entity _, out float3 position,
                    out Bounds3 bounds, out quaternion _))
            {
                Mod.Log.Warn($"Hub [focus] {what} {Ent(building)} refused — no position (SelectedInfoUISystem.TryGetPosition) " +
                             $"({source})");
                LastTriggerText = $"focusBuilding {Ent(building)}: no position";
                return false;
            }
            float radius = math.length(MathUtils.Extents(bounds.xz));
            float wanted = math.max(radius * Tuning.FocusZoomFactor, Tuning.FocusBuildingMinZoomM);
            if (!Fly(building, default, wanted, out float zoom, out string failure))
            {
                Mod.Log.Warn($"Hub [focus] {what} {Ent(building)} refused — {failure} ({source})");
                LastTriggerText = $"focusBuilding {Ent(building)}: {failure}";
                return false;
            }
            Mod.Log.Info($"Hub [focus] {what} {Ent(building)} {PrefabNameOf(building)} zoom={zoom:F0} (at {position.x:F0}, " +
                         $"{position.z:F0}) ({source})");
            LastTriggerText = $"focusBuilding {what} {Ent(building)}: zoom {zoom:F0}";
            return true;
        }

        private void ApplyFocusLift(Entity id, Entity top, string source)
        {
            // The exact (id, top) pair first: a row keeps a removed top station in its key, so the UI's dead top still names it; then a
            // stale top → the id alone.
            bool found = m_Census.TryGetRow(id, top, out LiftCensus.Row row);
            if (!found && top != Entity.Null && !EntityManager.Exists(top))
            {
                top = Entity.Null;
                found = m_Census.TryGetRow(id, top, out row);
            }
            // The LiftBend problem row sends the bent tower's node with no top (SkiResortStatsSystem): fly to that tower.
            if (!found && top == Entity.Null && m_Census.TryGetRowOfBendNode(id, out row))
            {
                float3 at = row.BendAt;
                if (!Fly(Entity.Null, at, Tuning.FocusGapZoomM, out float bendZoom, out string bendFailure))
                {
                    Mod.Log.Warn($"Hub [focus] lift bend {Ent(id)} refused — {bendFailure} ({source})");
                    LastTriggerText = $"focusLift {Ent(id)}: {bendFailure}";
                    return;
                }
                Mod.Log.Info($"Hub [focus] lift bend: tower {row.BendTower} node {Ent(id)} turns {row.BendDeg:F1}° → ({at.x:F0}, {at.z:F0}) " +
                             $"zoom={bendZoom:F0} ({source})");
                LastTriggerText = $"focusLift {Ent(id)}: the bent tower {row.BendTower}";
                return;
            }
            if (!found)
            {
                Mod.Log.Info($"Hub [focus] lift refused — no lift row ({Ent(id)}, top {Ent(top)}) in the census (stale row?) " +
                             $"({source})");
                LastTriggerText = $"focusLift {Ent(id)}: refused";
                return;
            }
            float3 point;
            float wanted;
            string where;
            if (row.HasGap)
            {
                point = row.GapAt;
                wanted = Tuning.FocusGapZoomM;
                where = $"the gap at ({point.x:F0}, {point.z:F0})";
            }
            else
            {
                point = row.FocusCenter;
                wanted = math.max(row.FocusRadius * Tuning.FocusZoomFactor, Tuning.FocusLiftMinZoomM);
                where = $"the lift's center ({point.x:F0}, {point.z:F0}), radius {row.FocusRadius:F0} m";
            }
            if (!Fly(Entity.Null, point, wanted, out float zoom, out string failure))
            {
                Mod.Log.Warn($"Hub [focus] lift {Ent(id)} refused — {failure} ({source})");
                LastTriggerText = $"focusLift {Ent(id)}: {failure}";
                return;
            }
            Mod.Log.Info($"Hub [focus] lift '{LiftName(id, top)}' (base {Ent(ToEntity(row.Base))}, top {Ent(ToEntity(row.Top))}, " +
                         $"{row.State}) → {where} zoom={zoom:F0} ({source})");
            LastTriggerText = $"focusLift {Ent(id)}: {where}";
        }

        /// <summary>The shared camera move (Engine notes: Focus). follow != Null: the orbit camera follows it;
        /// else it flies to <paramref name="point"/> and follows nothing.</summary>
        private bool Fly(Entity follow, float3 point, float wantedZoom, out float zoom, out string failure)
        {
            zoom = 0f;
            failure = null;
            OrbitCameraController orbit = m_CameraUpdateSystem.orbitCameraController;
            if (orbit == null)
            {
                failure = "no orbit camera controller";
                return false;
            }
            float2 range = orbit.m_ZoomRange;
            zoom = math.clamp(wantedZoom, range.x, range.y);
            orbit.followedEntity = follow;   // Null for a point: no follow (OrbitCameraController.cs:47-71)
            IGameCameraController active = m_CameraUpdateSystem.activeCameraController;
            // Keep the player's heading (the orbit's own getter is stale here).
            float yaw = active != null ? active.rotation.y : orbit.rotation.y;
            if (active != null)
                orbit.TryMatchPosition(active);
            if (follow == Entity.Null)
                orbit.pivot = new UnityEngine.Vector3(point.x, point.y, point.z);   // re-clamped to terrain + 10 m (:234-244)
            orbit.rotation = new UnityEngine.Vector3(Tuning.FocusPitchDeg, yaw, 0f);
            orbit.zoom = zoom;
            m_CameraUpdateSystem.activeCameraController = orbit;
            return true;
        }

        private string RunName(Entity run)
        {
            int count = m_Stats.RunCount;
            for (int i = 0; i < count; i++)
            {
                RunStats r = m_Stats.GetRun(i);
                if (r.Entity == run)
                    return r.Name ?? "";
            }
            return m_NameSystem.TryGetCustomName(run, out string custom) ? custom : "?";
        }

        private string LiftName(Entity id, Entity top)
        {
            int count = m_Stats.LiftCount;
            for (int i = 0; i < count; i++)
            {
                LiftStats r = m_Stats.GetLift(i);
                if (r.Entity == id && r.Top == top)
                    return r.Name ?? "";
            }
            return "?";
        }

        private void ApplyGladeToggle(string source)
        {
            if (m_GladeTool.IsActive)
            {
                Mod.Log.Info($"Hub [glade] toggle — deactivating the glade tool ({source})");
                m_GladeTool.Deactivate();
                LastTriggerText = "gladeToggle: off";
                return;
            }
            // TryActivate owns the gating (armed, not disabled, Game mode) and logs its own refusal reason.
            bool ok = m_GladeTool.TryActivate("hub" + SourceSuffix(source));
            Mod.Log.Info($"Hub [glade] toggle — activate {(ok ? "ok" : "refused")} ({source})");
            LastTriggerText = $"gladeToggle: {(ok ? "on" : "refused: " + m_GladeTool.UnavailableReasonKey)}";
        }

        /// <summary>The Lifts tab's toggle, like ApplyGladeToggle: Deactivate() when the lift tool is active, else
        /// TryActivate("hub" + source), which owns the gating (armed in a game, not disabled) and logs its own refusal.</summary>
        private void ApplyLiftLineToggle(string source)
        {
            if (m_LiftLineTool == null)
            {
                Mod.Log.Warn($"Hub [liftLine] toggle — no LiftLineToolSystem in this world ({source})");
                LastTriggerText = "liftLineToggle: no tool";
                return;
            }
            if (m_LiftLineTool.IsActive)
            {
                Mod.Log.Info($"Hub [liftLine] toggle — deactivating the lift generator ({source})");
                m_LiftLineTool.Deactivate();
                LastTriggerText = "liftLineToggle: off";
                return;
            }
            bool ok = m_LiftLineTool.TryActivate("hub" + SourceSuffix(source));
            Mod.Log.Info($"Hub [liftLine] toggle — activate {(ok ? "ok" : "refused")} ({source})");
            LastTriggerText = $"liftLineToggle: {(ok ? "on" : "refused")}";
        }

        private static readonly string[] s_LiftLineStateNames = Enum.GetNames(typeof(LiftLineToolSystem.ToolState));

        /// <summary>available = a city is loaded (this system only updates in Game mode) and the tool exists.</summary>
        private HubLiftLineState LiftLineNow()
        {
            LiftLineToolSystem t = m_LiftLineTool;
            if (t == null)
                return new HubLiftLineState(false, false, "");
            int i = (int)t.State;
            string state = i >= 0 && i < s_LiftLineStateNames.Length ? s_LiftLineStateNames[i] : "";
            bool inGame = GameManager.instance != null && GameManager.instance.gameMode == GameMode.Game;
            return new HubLiftLineState(inGame, t.IsActive, state);
        }

        private HubGladeState GladeNow()
        {
            SkiGladeToolSystem t = m_GladeTool;
            return new HubGladeState(t.IsArmed && !t.IsDisabled, t.IsActive, t.Level, t.TreeLineTarget,
                t.UnavailableReasonKey);
        }

        /// <summary>The Ski Bus card: follows the game's selection. Hidden (a struct compare) unless a coach or a depot is selected; refreshed every
        /// BusCardRefreshFrames while one is, so it has no per-frame work of its own.</summary>
        private void PushBusCard()
        {
            Entity selected = m_BusCard.active ? m_SelectedInfo.selectedEntity : Entity.Null;
            if (selected != m_BusCardSelected)
            {
                m_BusCardSelected = selected;
                m_BusCardDepot = BusCardDepotOf(selected);
                m_BusCardTicks = 0;
            }
            if (m_BusCardDepot == Entity.Null)
            {
                m_BusCard.Update(default);   // ValueBinding dedups (struct, IEquatable)
                return;
            }
            if (--m_BusCardTicks > 0)
                return;
            m_BusCardTicks = BusCardRefreshFrames;
            m_BusCard.Update(BusCardNow());
        }

        /// <summary>The depot a selected entity shows on the card: itself, or a coach's owner. Null for anything else, a vanilla bus included.</summary>
        private Entity BusCardDepotOf(Entity selected)
        {
            string name = PrefabNameOf(selected);
            if (name == SkiCatalog.Names.BusDepot)
                return selected;
            if (name == SkiCoachSystem.BusPrefabName && EntityManager.TryGetComponent(selected, out Owner owner) &&
                PrefabNameOf(owner.m_Owner) == SkiCatalog.Names.BusDepot)
                return owner.m_Owner;
            return Entity.Null;
        }

        private HubBusCardState BusCardNow()
        {
            Entity depot = m_BusCardDepot;
            if (!EntityManager.Exists(depot) || EntityManager.HasComponent<Deleted>(depot))
                return default;
            m_SkiDays.CopyDepots(m_BusCardViews);
            int at = m_BusCardViews.FindIndex(v => v.Depot == depot);
            var card = new HubBusCardState { Shown = true, GuestsMonth = -1, State = BusCardOff };
            if (at >= 0)
            {
                SkiDaySystem.DepotView view = m_BusCardViews[at];
                card.DistanceM = view.DistanceM > 0f && !float.IsInfinity(view.DistanceM) ? (int)math.round(view.DistanceM) : 0;
                if (SkiBusRules.IsBilled(view.Verdict))
                {
                    card.State = BusCardRunning;
                    card.BillMonth = (int)math.round(SkiBusRules.MonthlyBill(view.DistanceM));
                }
                else if (view.Verdict == SkiDayRules.LodgeReject.NoRoad)
                    card.State = BusCardNoRoad;
                else if (view.Verdict == SkiDayRules.LodgeReject.NoHillLodge)
                    card.State = BusCardNoHillLodge;
                if (m_SkiDays.TryGetArea(view.Sign, out SkiDayAreaView area))
                    card.GuestsMonth = area.DepotArrivedThisMonth;
                for (int i = 0; i < m_Stats.ResortCount; i++)
                {
                    ResortStats row = m_Stats.GetResort(i);
                    if (row.Sign == view.Sign)
                    {
                        card.Area = row.Name;
                        break;
                    }
                }
            }
            return card;
        }

        /// <summary>Only for bindings JS observes (the panel is open); nothing while closed. Per frame:
        /// nine `active` reads, the Demand writes, revision compares, struct ValueBinding updates.</summary>
        private void PushPanelData()
        {
            bool buildActive = m_Build.active;
            bool resortsActive = m_Resorts.active, resortLiveActive = m_ResortLive.active;
            bool runsActive = m_Runs.active, runLiveActive = m_RunLive.active;
            bool liftsActive = m_Lifts.active, problemsActive = m_Problems.active;
            bool cityActive = m_City.active;
            PushBusCard();
            PushMergeCard();
            bool demand = buildActive || resortsActive || resortLiveActive || runsActive || runLiveActive ||
                          liftsActive || problemsActive || cityActive ||     // pisteMap and follow do NOT demand
                          m_BusCardDepot != Entity.Null ||                    // the card's ski area name comes from the stats rows
                          m_MergeCardBuilding != Entity.Null;                // the merge card too
            m_Catalog.Demand = demand;   // catalog and stats follow every panel binding
            m_Stats.Demand = demand;

            PushArray(m_ResortsPush, resortsActive, m_Stats.ResortsRevision, m_Stats.ResortCount);
            PushArray(m_ResortLivePush, resortLiveActive, m_Stats.ResortLiveRevision, m_Stats.ResortLiveCount);
            PushArray(m_RunsPush, runsActive, m_Stats.RunsRevision, m_Stats.RunCount);
            PushArray(m_RunLivePush, runLiveActive, m_Stats.RunLiveRevision, m_Stats.RunLiveCount);
            PushArray(m_LiftsPush, liftsActive, m_Stats.LiftsRevision, m_Stats.LiftCount);
            PushArray(m_ProblemsPush, problemsActive, m_Stats.ProblemsRevision, m_Stats.ProblemCount);
            PushHistory();   // no Demand of its own
            bool lodgeParkingActive = m_LodgeParking.active;
            if (lodgeParkingActive)
            {
                RefreshLodgeParking();
                PollLodgeNames();
            }
            PushArray(m_LodgeParkingPush, lodgeParkingActive, m_LodgeParkingRevision, m_LodgeParkingRows.Count);
            PushMerged();

            if (cityActive)
                m_City.Update(m_Stats.City);   // our comparer dedups (struct, field by field)

            if (buildActive)
                PushBuild(!m_BuildWasActive);
            m_BuildWasActive = buildActive;

            if (m_Glade.active)
                m_Glade.Update(GladeNow());   // ValueBinding dedups (struct, IEquatable)
            if (m_LiftLine.active)
                m_LiftLine.Update(LiftLineNow());   // dedups like glade
            if (m_CatTrackDeleteBinding.active)
                m_CatTrackDeleteBinding.Update(m_GradeTool != null && m_GradeTool.IsActive && m_GradeTool.DeleteMode);   // dedups
            bool quickActive = m_QuickRuns.active;
            if (quickActive)
                PushQuickRuns(!m_QuickRunsWasActive);   // on the tool's HubRevision / availability / activity
            m_QuickRunsWasActive = quickActive;
            // pisteMap / follow: ValueBinding.Update at the event (ApplyPisteMap, ApplyFollow); a subscribe
            // pushes the current value (ValueBinding.cs:24-35). Nothing per frame.

            if (s_NonFinite > 0 && !m_NonFiniteLogged)
            {
                m_NonFiniteLogged = true;
                Mod.Log.Warn($"Hub: {s_NonFinite} non-finite number(s) in the stats rows were sent as 0 (NaN/Infinity would " +
                             "break the UI's JSON) — a SkiResortStatsSystem value is not clamped; logged once per load");
            }
        }

        /// <summary>Push when the revision moved or the binding was (re)subscribed; log only
        /// a row-count change (through the array's RowsLog window) and, once per load, a slow
        /// push. Per frame beyond the push: one int compare for a waiting fold (also while closed, so the
        /// trailing summary is never lost).</summary>
        private void PushArray(ArrayPush push, bool active, int revision, int rows)
        {
            if (push.RowsLog.Count > 0)
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (push.RowsLog.TrailingDue(now))
                    WritePushRowsSummary(push, now);
            }
            if (!active)
            {
                push.WasActive = false;
                return;
            }
            if (!push.WasActive)
                push.PushedRevision = int.MinValue;   // re-send once on (re)subscribe
            push.WasActive = true;
            if (revision == push.PushedRevision)
                return;
            bool first = push.PushedRevision == int.MinValue;
            push.PushedRevision = revision;
            m_PushTimer.Restart();
            push.Binding.Update();
            m_PushTimer.Stop();
            push.Pushes++;
            float ms = (float)m_PushTimer.Elapsed.TotalMilliseconds;
            push.LastMs = ms;
            if (ms > push.MaxMs)
                push.MaxMs = ms;
            if (!first && rows != push.LoggedRows)
            {
                string text = $"Hub [push] {push.Name} rows={rows} (was {push.LoggedRows}) revision {revision} ms={ms:F2}";
                float now = UnityEngine.Time.realtimeSinceStartup;
                switch (push.RowsLog.Offer(now, false, text, rows, ms))
                {
                    case LogWindow.Verdict.Plain:
                        Mod.Log.Info(text);
                        break;
                    case LogWindow.Verdict.Summary:
                        WritePushRowsSummary(push, now);
                        break;
                }
            }
            push.LoggedRows = rows;
            if (ms > Tuning.SlowPushMs && !push.SlowLogged)
            {
                push.SlowLogged = true;
                Mod.Log.Warn($"Hub [push] {push.Name}: {ms:F2} ms for {rows} row(s) (> SlowPushMs {Tuning.SlowPushMs}) — " +
                             "logged once per load");
            }
        }

        /// <summary>One line for the row-count changes folded in <paramref name="push"/>'s window.</summary>
        private static void WritePushRowsSummary(ArrayPush push, float now)
        {
            LogWindow w = push.RowsLog;
            Mod.Log.Info($"Hub [push] {push.Name}: {w.Count} row-count change(s) folded over {w.SpanS(now):F0} s " +
                         $"(rows {w.Range(0, "F0")}, push {w.Range(1, "F2")} ms) — last: {w.LastText}");
            w.Wrote(now);
        }

        /// <summary>Writes every array's waiting fold (a load, quit).</summary>
        private void FlushPushRows()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            ArrayPush[] all = { m_ResortsPush, m_ResortLivePush, m_RunsPush, m_RunLivePush, m_LiftsPush, m_ProblemsPush, m_HistoryPush,
                                m_LodgeParkingPush, m_MergedPush };
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].RowsLog.Count > 0)
                    WritePushRowsSummary(all[i], now);
            }
        }

        private void PushBuild(bool rising)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            int resortsRevision = m_Stats.ResortsRevision;
            // The AlreadyBuilt lock reads the SHOWN sign, so a switch re-checks at once.
            if (rising || m_LocksDirty || now >= m_NextLockRefresh || resortsRevision != m_LockResortsRevision ||
                m_SelectedResort != m_LockSign)
            {
                RefreshLocks();
                m_LocksDirty = false;
                m_NextLockRefresh = now + Tuning.LockRefreshS;
                m_LockResortsRevision = resortsRevision;
                m_LockSign = m_SelectedResort;
            }
            RefreshLiftHint();
            int active = ActiveItemIndex();
            int signature = HubRules.BuildSignature(active, m_ItemReason, m_ItemPrice, m_ItemSent, m_LiftNoPairByType);
            if (rising || signature != m_BuildPushedSignature || active != m_ActiveItem)
            {
                m_ActiveItem = active;
                m_BuildPushedSignature = signature;
                m_Build.Update();
            }
        }

        private void RefreshLocks()
        {
            // A catalog re-resolution (a prefab entity replaced or deleted,
            // PrefabSystem.cs:766-824) moves Generation; the items follow it here, at the LockRefreshS cadence, instead
            // of keeping a Deleted prefab entity until the next load. At most Tuning.CatalogItemLinesMax lines per load.
            int generation = m_PrefabCatalog.Generation;
            if (generation != m_ItemsGeneration)
            {
                m_ItemsGeneration = generation;
                string itemText = ResolveItems();
                ReadItemValues();
                if (m_ItemsReresolveLines < Tuning.CatalogItemLinesMax)
                {
                    m_ItemsReresolveLines++;
                    Mod.Log.Warn($"Hub [items] prefab catalog generation {generation} (re-resolved after the load): " +
                                 $"{itemText}; item costs: {ItemCostText()}" +
                                 (m_ItemsReresolveLines == Tuning.CatalogItemLinesMax
                                     ? " — further [items] lines this load are not written (PrefabCatalog lines in the DUMP)"
                                     : ""));
                }
            }
            int signs = SignCount();
            // The shown ski area's sign for AlreadyBuilt; the Not linked view has none
            // (Missing / Locked / NoSign only: the launch picks a sign and re-checks there).
            Entity shown = m_SelectedResort != Entity.Null && m_Ownership.IsLiveGate(m_SelectedResort) ? m_SelectedResort : Entity.Null;
            for (int i = 0; i < ItemCount; i++)
            {
                BuildItemDef item = Items[i];
                byte reason = ReasonNone;
                if (m_ItemPrefab[i] == null || m_ItemPrefabEntity[i] == Entity.Null ||
                    !EntityManager.Exists(m_ItemPrefabEntity[i]))
                    reason = ReasonMissing;
                else if (EntityManager.HasEnabledComponent<Locked>(m_ItemPrefabEntity[i]))
                    reason = ReasonLocked;
                else if (item.IsUpgrade && signs <= 0)
                    reason = ReasonNoSign;
                else if (item.IsUpgrade && shown != Entity.Null && m_ItemSent[i])
                    reason = UpgradeConflict(shown, i, out Entity _);
                m_ItemReason[i] = reason;
                m_ItemLocked[i] = reason != ReasonNone;
                // `price` = the run's BASE fee by its item's kind (no prefab read).
                m_ItemPrice[i] = IsRunItem(i) && reason != ReasonMissing ? SkiRunFees.BaseFee(item.RunKind) : -1;
            }
        }

        /// <summary>The six optional item numbers, read once at arm time (prefab data does not change in a session). Run fees and rates
        /// need no prefab; a missing building prefab sends cost −1. A uint above int.MaxValue is sent as int.MaxValue. By the row's
        /// kind: Line = its lift type's row (LinePerKm, FixedPerMonth, PerKmPerMonth: the fixed-grip row is what those consts alias,
        /// CG-1); Station / Sign / Lodge / School / Patrol = PlaceableObjectData.m_ConstructionCost; Run: base fee and rates. Also
        /// the upgrade items' lock facts (ForbidMultiple from the row; the luxury label from the lodge row, for the DUMP) and one
        /// drift WARN per item per load when a FOUND prefab's cost differs from its SkiCatalog row (informational mirror: "the prefab
        /// wins").</summary>
        private void ReadItemValues()
        {
            for (int i = 0; i < ItemCount; i++)
            {
                BuildItemDef item = Items[i];
                m_ItemPricePerHa[i] = m_ItemCost[i] = m_ItemCostPerKm[i] = -1;
                m_ItemMonthly[i] = m_ItemMonthlyPerHa[i] = m_ItemMonthlyPerKm[i] = -1;
                m_ItemForbidMultiple[i] = false;
                m_ItemLuxury[i] = false;
                Entity prefab = m_ItemPrefabEntity[i];
                bool live = prefab != Entity.Null && EntityManager.Exists(prefab);
                int rowCost = -1;
                bool rowForbid = false;
                switch (item.Kind)
                {
                    case BuildItemKind.Run:
                        m_ItemPricePerHa[i] = SkiRunFees.PerHaFee(item.RunKind);
                        m_ItemMonthlyPerHa[i] = SkiRunUpkeepSystem.CostPerHaMonth((RunKind)item.RunKind);
                        break;
                    case BuildItemKind.Line:
                    {
                        LiftTypeDef def = SkiCatalog.LiftById(item.LiftTypeId);
                        if (def != null)
                        {
                            m_ItemCostPerKm[i] = def.LinePerKm;
                            m_ItemMonthly[i] = (int)def.FixedPerMonth;
                            m_ItemMonthlyPerKm[i] = (int)def.PerKmPerMonth;
                        }
                        break;
                    }
                    case BuildItemKind.Depot:
                    {
                        if (live && EntityManager.TryGetComponent(prefab, out PlaceableObjectData placeable))
                            m_ItemCost[i] = ClampCost(placeable.m_ConstructionCost);
                        DepotDef depot = SkiCatalog.DepotByPrefab(item.Prefab);
                        rowCost = depot != null ? depot.BuildCost : -1;
                        break;
                    }
                    case BuildItemKind.Lodge:
                    case BuildItemKind.School:
                    case BuildItemKind.Patrol:
                    {
                        if (live && EntityManager.TryGetComponent(prefab, out PlaceableObjectData placeable))
                            m_ItemCost[i] = ClampCost(placeable.m_ConstructionCost);
                        LodgeDef lodge = SkiCatalog.LodgeByPrefab(item.Prefab);
                        SchoolDef school = lodge == null ? SkiCatalog.SchoolByPrefab(item.Prefab) : null;
                        PatrolDef patrol = lodge == null && school == null ? SkiCatalog.PatrolByPrefab(item.Prefab) : null;
                        if (lodge != null)
                        {
                            rowCost = lodge.BuildCost;
                            rowForbid = lodge.ForbidMultiple;
                            m_ItemLuxury[i] = lodge.Luxury;
                        }
                        else if (school != null)
                        {
                            rowCost = school.BuildCost;
                            rowForbid = school.ForbidMultiple;
                        }
                        else if (patrol != null)
                        {
                            rowCost = patrol.BuildCost;
                            rowForbid = patrol.ForbidMultiple;
                        }
                        break;
                    }
                    default:   // Sign, Station
                    {
                        if (live && EntityManager.TryGetComponent(prefab, out PlaceableObjectData placeable))
                            m_ItemCost[i] = ClampCost(placeable.m_ConstructionCost);
                        if (item.Kind == BuildItemKind.Station)
                        {
                            StationDef station = SkiCatalog.StationByPrefab(item.Prefab);
                            rowCost = station != null ? station.BuildCost : -1;
                        }
                        else
                        {
                            for (int s = 0; s < SkiCatalog.Signs.Count; s++)
                            {
                                if (string.Equals(SkiCatalog.Signs[s].Prefab, item.Prefab, StringComparison.Ordinal))
                                    rowCost = SkiCatalog.Signs[s].BuildCost;
                            }
                        }
                        break;
                    }
                }
                // A top-level building has no engine one-per-sign rule; the row decides the hub's soft lock.
                m_ItemForbidMultiple[i] = rowForbid;
                if (!live || m_ItemDriftLogged[i])
                    continue;
                string drift = null;
                if (rowCost >= 0 && m_ItemCost[i] >= 0 && m_ItemCost[i] != rowCost)
                    drift = $"costs {m_ItemCost[i]}, SkiCatalog says {rowCost}";
                if (drift != null)
                {
                    m_ItemDriftLogged[i] = true;
                    Mod.Log.Warn($"Hub [items] drift: '{item.Prefab}' ({item.Id}) {drift} — the prefab wins; update the row in " +
                                 "Systems/SkiCatalog.cs with the prefab surgery (round-2-contract §10.1)");
                }
            }
        }

        private static int ClampCost(uint cost) => cost > int.MaxValue ? int.MaxValue : (int)cost;

        /// <summary>The ARMED line's item costs (every SENT item by its kind - run: base + per ha;
        /// line: per km + a month + per km a month; else the one-time cost).</summary>
        private string ItemCostText()
        {
            var sb = new StringBuilder(512);
            for (int i = 0; i < ItemCount; i++)
            {
                if (!m_ItemSent[i])
                    continue;
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(Items[i].Id).Append(' ');
                switch (Items[i].Kind)
                {
                    case BuildItemKind.Run:
                        sb.Append(SkiRunFees.BaseFee(Items[i].RunKind).ToString(CultureInfo.InvariantCulture)).Append('+')
                          .Append(SkiRunFees.PerHaFee(Items[i].RunKind).ToString(CultureInfo.InvariantCulture)).Append("/ha");
                        break;
                    case BuildItemKind.Line:
                        sb.Append(m_ItemCostPerKm[i].ToString(CultureInfo.InvariantCulture)).Append("/km + ")
                          .Append(m_ItemMonthly[i].ToString(CultureInfo.InvariantCulture)).Append("/month + ")
                          .Append(m_ItemMonthlyPerKm[i].ToString(CultureInfo.InvariantCulture)).Append("/km/month");
                        break;
                    default:
                        sb.Append(m_ItemCost[i].ToString(CultureInfo.InvariantCulture));
                        break;
                }
            }
            return sb.Length > 0 ? sb.ToString() : "none (no item sent)";
        }

        /// <summary>Live signs in the stats rows (the rows are fresh while the build section is observed:
        /// it is part of the stats Demand). With no rows at all (no stats rebuild yet this load, or the
        /// stats system disabled by its "!!!"), the ownership system's sign scan decides — at the
        /// RefreshLocks cadence only (LockRefreshS), never per frame.</summary>
        private int SignCount()
        {
            int count = m_Stats.ResortCount;
            if (count <= 0)
            {
                m_Ownership.TryNearestGateEntity(float2.zero, out Entity any, out float _);   // any distance
                return any != Entity.Null ? 1 : 0;
            }
            int signs = 0;
            for (int i = 0; i < count; i++)
            {
                ResortStats r = m_Stats.GetResort(i);
                if ((int)r.Kind != ResortKindNotLinked && r.Sign != Entity.Null)
                    signs++;
            }
            return signs;
        }

        /// <summary>build[line].hint: HINT[LiftNoPair] when HubRules.LiftNoPair holds for the
        /// SHOWN area's lift rows (stats rows whose Sign == m_SelectedResort; Null = the Not linked rows), per lift type, over the rows
        /// of that type (LiftStats.TypeIndex; a NoType row joins no type), one flag per
        /// SkiCatalog.Lifts index, the one scratch list refilled per type. Recomputed only when the stats LiftsRevision
        /// moved or the shown area changed. Text only: the Build-strip dot comes from ProblemRow.buildItem (UI).</summary>
        private void RefreshLiftHint()
        {
            int revision = m_Stats.LiftsRevision;
            if (revision == m_HintLiftsRevision && m_SelectedResort == m_HintSign)
                return;
            m_HintLiftsRevision = revision;
            m_HintSign = m_SelectedResort;
            int count = m_Stats.LiftCount;
            for (int t = 0; t < m_LiftNoPairByType.Length; t++)
            {
                m_HintRows.Clear();
                for (int i = 0; i < count; i++)
                {
                    LiftStats l = m_Stats.GetLift(i);
                    if (l.Sign == m_SelectedResort && l.TypeIndex == t)
                        m_HintRows.Add(new HubLiftHintRow { State = l.State, HasBase = l.HasBase, HasTop = l.HasTop, Type = l.TypeIndex });
                }
                m_LiftNoPairByType[t] = HubRules.LiftNoPair(m_HintRows);
            }
        }

        /// <summary>The line item's HINT[LiftNoPair] flag: its lift type's (false for every other item).</summary>
        private bool LiftNoPairOf(int item)
        {
            int t = s_ItemTypeIndex[item];
            return IsLineItem(item) && t >= 0 && t < m_LiftNoPairByType.Length && m_LiftNoPairByType[t];
        }

        private static string ReasonKey(byte reason)
        {
            switch (reason)
            {
                case ReasonLocked: return ReasonKeyLocked;
                case ReasonNoSign: return ReasonKeyNoSign;
                case ReasonMissing: return ReasonKeyMissing;
                case ReasonAlreadyBuilt: return ReasonKeyAlreadyBuilt;
                default: return "";
            }
        }

        /// <summary>`build` (RawValueBinding writer): one typed object per SENT item (m_ItemSent;
        /// m_SentCount of them), table order (the UI groups the tiles by id). Runs on Update() and on subscribe (main
        /// thread). Fixed-size arrays only.</summary>
        private void WriteBuild(IJsonWriter writer)
        {
            try
            {
                WriteBuildRows(writer);
            }
            catch (Exception e)
            {
                WriterFailed(writer, "build", e);
            }
        }

        private void WriteBuildRows(IJsonWriter writer)
        {
            writer.ArrayBegin((uint)m_SentCount);
            int written = 0;
            for (int i = 0; i < ItemCount; i++)
            {
                if (!m_ItemSent[i])
                    continue;
                written++;
                writer.TypeBegin("SkiResortMod.BuildItem");
                writer.PropertyName("id");
                writer.Write(Items[i].Id);
                writer.PropertyName("locked");
                writer.Write(m_ItemLocked[i]);
                writer.PropertyName("active");
                writer.Write(i == m_ActiveItem);
                writer.PropertyName("reason");
                writer.Write(ReasonKey(m_ItemReason[i]));
                writer.PropertyName("price");
                writer.Write(m_ItemPrice[i]);
                writer.PropertyName("hint");
                writer.Write(LiftNoPairOf(i) ? HintKeyLiftNoPair : "");
                // Six optional numbers, −1 = not applicable.
                writer.PropertyName("pricePerHa");
                writer.Write(m_ItemPricePerHa[i]);
                writer.PropertyName("cost");
                writer.Write(m_ItemCost[i]);
                writer.PropertyName("costPerKm");
                writer.Write(m_ItemCostPerKm[i]);
                writer.PropertyName("monthly");
                writer.Write(m_ItemMonthly[i]);
                writer.PropertyName("monthlyPerHa");
                writer.Write(m_ItemMonthlyPerHa[i]);
                writer.PropertyName("monthlyPerKm");
                writer.Write(m_ItemMonthlyPerKm[i]);
                writer.TypeEnd();
            }
            writer.ArrayEnd();
            if (written != m_SentCount && !m_SentMismatchLogged)
            {
                m_SentMismatchLogged = true;   // cannot happen (both from m_ItemSent); never silent if it does
                Mod.Log.Error($"!!! Hub [build] wrote {written} item(s) into an array announced as {m_SentCount} — the UI may " +
                              "drop the build strip; logged once per load");
            }
        }

        /// <summary>`resorts` (bindings.ts ResortRow: identity and slow facts): SkiResortStatsSystem
        /// rows — signs in stable order, then the Not linked row. Main thread.</summary>
        private void WriteResorts(IJsonWriter writer)
        {
            try
            {
                WriteResortsRows(writer);
            }
            catch (Exception e)
            {
                WriterFailed(writer, "resorts", e);
            }
        }

        private void WriteResortsRows(IJsonWriter writer)
        {
            int count = m_Stats.ResortCount;
            writer.ArrayBegin((uint)count);
            for (int i = 0; i < count; i++)
            {
                ResortStats r = m_Stats.GetResort(i);
                writer.TypeBegin("SkiResortMod.ResortRow");
                writer.PropertyName("sign");
                UnityWriters.Write(writer, r.Sign);   // {index, version} (UnityWriters.cs:17-25)
                writer.PropertyName("kind");
                writer.Write((int)r.Kind);
                writer.PropertyName("name");
                writer.Write(r.Name ?? "");
                writer.PropertyName("customName");
                writer.Write(r.CustomName);
                writer.PropertyName("runs");
                writer.Write(Fin(r.Runs));
                writer.PropertyName("rated");
                writer.Write(Fin(r.Rated));
                writer.PropertyName("flat");
                writer.Write(Fin(r.Flat));
                writer.PropertyName("green");
                writer.Write(Fin(r.Green));
                writer.PropertyName("blue");
                writer.Write(Fin(r.Blue));
                writer.PropertyName("black");
                writer.Write(Fin(r.Black));
                writer.PropertyName("doubleBlack");
                writer.Write(Fin(r.DoubleBlack));
                writer.PropertyName("areaM2");
                writer.Write(Fin(r.AreaM2));
                writer.PropertyName("verticalM");
                writer.Write(Fin(r.VerticalM));
                writer.PropertyName("verticalApprox");
                writer.Write(r.VerticalApprox);
                writer.PropertyName("lodges");
                writer.Write(Fin(r.Lodges));
                writer.PropertyName("freeLodges");
                writer.Write(Fin(r.FreeLodges));
                writer.PropertyName("liftRows");
                writer.Write(Fin(r.LiftRows));
                writer.PropertyName("liftLengthM");
                writer.Write(Fin(r.LiftLengthM));
                writer.PropertyName("capacityPerHour");
                writer.Write(Fin(r.CapacityPerHour));
                writer.TypeEnd();
            }
            writer.ArrayEnd();
        }

        /// <summary>`resortLive` (bindings.ts ResortLive): the values that move, same order and count as `resorts`, joined by sign. Main
        /// thread.</summary>
        private void WriteResortLive(IJsonWriter writer)
        {
            try
            {
                WriteResortLiveRows(writer);
            }
            catch (Exception e)
            {
                WriterFailed(writer, "resortLive", e);
            }
        }

        private void WriteResortLiveRows(IJsonWriter writer)
        {
            int count = m_Stats.ResortLiveCount;
            writer.ArrayBegin((uint)count);
            for (int i = 0; i < count; i++)
            {
                ResortLiveStats r = m_Stats.GetResortLive(i);
                writer.TypeBegin("SkiResortMod.ResortLive");
                writer.PropertyName("sign");
                UnityWriters.Write(writer, r.Sign);
                writer.PropertyName("status");
                writer.Write((int)r.Status);
                writer.PropertyName("runsOpen");
                writer.Write(Fin(r.RunsOpen));
                writer.PropertyName("runsPartly");
                writer.Write(Fin(r.RunsPartly));
                writer.PropertyName("snowEstimated");
                writer.Write(r.SnowEstimated);
                writer.PropertyName("snowPct");
                writer.Write(Fin(r.SnowPct));
                writer.PropertyName("snowDepthCm");
                writer.Write(Fin(r.SnowDepthCm));
                writer.PropertyName("seasonHint");
                writer.Write((int)r.SeasonHint);
                writer.PropertyName("topTempKnown");
                writer.Write(r.TopTempKnown);
                writer.PropertyName("topTempC");
                writer.Write(Fin(r.TopTempC));
                writer.PropertyName("attraction");
                writer.Write(Fin(r.Attraction));
                writer.PropertyName("attractionFull");
                writer.Write(Fin(r.AttractionFull));
                writer.PropertyName("attractionHeld");
                writer.Write(r.AttractionHeld);
                writer.PropertyName("lodgesActive");
                writer.Write(Fin(r.LodgesActive));
                writer.PropertyName("summer");
                writer.Write(r.Summer);
                writer.PropertyName("lodgePart");
                writer.Write(Fin(r.LodgePart));
                writer.PropertyName("terrain");
                writer.Write(Fin(r.Terrain));
                writer.PropertyName("runsValue");
                writer.Write(Fin(r.RunsValue));
                writer.PropertyName("runsValueFull");
                writer.Write(Fin(r.RunsValueFull));
                writer.PropertyName("openSharePct");
                writer.Write(Fin(r.OpenSharePct));
                writer.PropertyName("liftsValue");
                writer.Write(Fin(r.LiftsValue));
                writer.PropertyName("liftsComplete");
                writer.Write(Fin(r.LiftsComplete));
                writer.PropertyName("liftsCounted");
                writer.Write(Fin(r.LiftsCounted));
                writer.PropertyName("varietyMultiplier");
                writer.Write(Fin(r.VarietyMultiplier));
                writer.PropertyName("verticalMultiplier");
                writer.Write(Fin(r.VerticalMultiplier));
                writer.PropertyName("scoreVerticalM");
                writer.Write(Fin(r.ScoreVerticalM));
                writer.PropertyName("efficiencyPct");
                writer.Write(Fin(r.EfficiencyPct));
                writer.PropertyName("sharePct");
                writer.Write(Fin(r.SharePct));
                writer.PropertyName("estKnown");
                writer.Write(r.EstKnown);
                writer.PropertyName("estTourists");
                writer.Write(Fin(r.EstTourists));
                writer.PropertyName("nextStep");
                writer.Write((int)r.NextStep);
                writer.PropertyName("thisMonthSinceLoad");
                writer.Write(r.ThisMonthSinceLoad);
                writer.PropertyName("lastMonthFull");
                writer.Write(r.LastMonthFull);
                writer.PropertyName("guestsThisMonth");
                writer.Write(Fin(r.GuestsThisMonth));
                writer.PropertyName("touristsThisMonth");
                writer.Write(Fin(r.TouristsThisMonth));
                writer.PropertyName("touristGroupsThisMonth");
                writer.Write(Fin(r.TouristGroupsThisMonth));
                writer.PropertyName("residentsThisMonth");
                writer.Write(Fin(r.ResidentsThisMonth));
                writer.PropertyName("paidThisMonth");
                writer.Write(Fin(r.PaidThisMonth));
                writer.PropertyName("unpaidThisMonth");
                writer.Write(Fin(r.UnpaidThisMonth));
                writer.PropertyName("freeThisMonth");
                writer.Write(Fin(r.FreeThisMonth));
                writer.PropertyName("ticketsThisMonth");
                writer.Write(Fin(r.TicketsThisMonth));
                writer.PropertyName("guestsLastMonth");
                writer.Write(Fin(r.GuestsLastMonth));
                writer.PropertyName("touristsLastMonth");
                writer.Write(Fin(r.TouristsLastMonth));
                writer.PropertyName("touristGroupsLastMonth");
                writer.Write(Fin(r.TouristGroupsLastMonth));
                writer.PropertyName("residentsLastMonth");
                writer.Write(Fin(r.ResidentsLastMonth));
                writer.PropertyName("paidLastMonth");
                writer.Write(Fin(r.PaidLastMonth));
                writer.PropertyName("ticketsLastMonth");
                writer.Write(Fin(r.TicketsLastMonth));
                writer.PropertyName("ticketGate");
                writer.Write((int)r.TicketGate);
                writer.PropertyName("ticketIncomeMonth");
                writer.Write(Fin(r.TicketIncomeMonth));
                writer.PropertyName("ticketPaidMonth");
                writer.Write(Fin(r.TicketPaidMonth));
                writer.PropertyName("ticketIncomeEstimated");
                writer.Write(r.TicketIncomeEstimated);
                writer.PropertyName("costGroomedMonth");
                writer.Write(Fin(r.CostGroomedMonth));
                writer.PropertyName("costSnowmakingMonth");
                writer.Write(Fin(r.CostSnowmakingMonth));
                writer.PropertyName("costNightMonth");
                writer.Write(Fin(r.CostNightMonth));
                writer.PropertyName("haNatural");
                writer.Write(Fin(r.HaNatural));
                writer.PropertyName("haGroomed");
                writer.Write(Fin(r.HaGroomed));
                writer.PropertyName("haSnowmaking");
                writer.Write(Fin(r.HaSnowmaking));
                writer.PropertyName("haNight");
                writer.Write(Fin(r.HaNight));
                writer.PropertyName("liftCostMonth");
                writer.Write(Fin(r.LiftCostMonth));
                writer.PropertyName("liftsBilled");
                writer.Write(Fin(r.LiftsBilled));
                writer.PropertyName("liftsBilledReduced");
                writer.Write(Fin(r.LiftsBilledReduced));
                writer.PropertyName("buildingsMonth");
                writer.Write(Fin(r.BuildingsMonth));
                writer.PropertyName("wagesMonth");
                writer.Write(Fin(r.WagesMonth));
                writer.PropertyName("jobs");
                writer.Write(Fin(r.Jobs));
                writer.PropertyName("jobsFilled");
                writer.Write(Fin(r.JobsFilled));
                writer.PropertyName("netMonth");
                writer.Write(Fin(r.NetMonth));
                writer.PropertyName("netEstimated");
                writer.Write(r.NetEstimated);
                // Appended in bindings.ts order; −1 unknown, State / Limit 0.
                writer.PropertyName("skiDaysState");
                writer.Write(Fin(r.SkiDaysState));
                writer.PropertyName("skiDaysThisMonth");
                writer.Write(Fin(r.SkiDaysThisMonth));
                writer.PropertyName("skiDaysLastMonth");
                writer.Write(Fin(r.SkiDaysLastMonth));
                writer.PropertyName("skiDaysSent");
                writer.Write(Fin(r.SkiDaysSent));
                writer.PropertyName("skiDaysSentCar");
                writer.Write(Fin(r.SkiDaysSentCar));
                writer.PropertyName("skiDaysSentNoCar");
                writer.Write(Fin(r.SkiDaysSentNoCar));
                writer.PropertyName("skiDaysNow");
                writer.Write(Fin(r.SkiDaysNow));
                writer.PropertyName("skiDaysCap");
                writer.Write(Fin(r.SkiDaysCap));
                writer.PropertyName("skiDaysLimit");
                writer.Write(Fin(r.SkiDaysLimit));
                writer.PropertyName("skiDaysPowderPct");
                writer.Write(Fin(r.SkiDaysPowderPct));
                // Appended in bindings.ts order; the model's Result (SkiResortStatsSystem fills them); "" / −1 unknown.
                writer.PropertyName("lodgeBest");
                writer.Write(r.LodgeBest ?? "");
                writer.PropertyName("lodgeExtra");
                writer.Write(Fin(r.LodgeExtra));
                writer.PropertyName("luxuryIgnored");
                writer.Write(Fin(r.LuxuryIgnored));
                writer.PropertyName("schoolValue");
                writer.Write(Fin(r.SchoolValue));
                writer.PropertyName("patrolledRuns");
                writer.Write(Fin(r.PatrolledRuns));
                writer.PropertyName("priceTolerance");
                writer.Write(Fin(r.PriceTolerance));
                writer.PropertyName("lodgeSpendMonth");
                writer.Write(Fin(r.LodgeSpendMonth));
                // This ski season's pass sales (since the game was loaded) and the pass-holder guests by month.
                writer.PropertyName("passesSoldSeason");
                writer.Write(Fin(r.PassesSoldSeason));
                writer.PropertyName("passIncomeSeason");
                writer.Write(Fin(r.PassIncomeSeason));
                writer.PropertyName("passHoldersThisMonth");
                writer.Write(Fin(r.PassHoldersThisMonth));
                writer.PropertyName("passHoldersLastMonth");
                writer.Write(Fin(r.PassHoldersLastMonth));
                // Lessons by month, the lesson income so far this month, the pre-season part of the season's
                // passes, and the city-income estimate (-1 = unknown or Tourism Overhaul).
                writer.PropertyName("lessonsThisMonth");
                writer.Write(Fin(r.LessonsThisMonth));
                writer.PropertyName("lessonsLastMonth");
                writer.Write(Fin(r.LessonsLastMonth));
                writer.PropertyName("lessonIncomeMonth");
                writer.Write(Fin(r.LessonIncomeMonth));
                writer.PropertyName("passesPreSeason");
                writer.Write(Fin(r.PassesPreSeason));
                writer.PropertyName("passIncomePreSeason");
                writer.Write(Fin(r.PassIncomePreSeason));
                writer.PropertyName("cityIncomeEst");
                writer.Write(Fin(r.CityIncomeEst));
                // EngineNotes/HubRules.md ONE NET: the Net's rows of the month in progress not sent above
                // (ticketsThisMonth and lessonIncomeMonth are the other two); Σ max(0, income rows) − Σ max(0, cost rows) = netMonth.
                writer.PropertyName("passIncomeMonth");
                writer.Write(Fin(r.PassIncomeMonth));
                writer.PropertyName("passIncomePreMonth");
                writer.Write(Fin(r.PassIncomePreMonth));
                writer.PropertyName("lodgeIncomeMonth");
                writer.Write(Fin(r.LodgeIncomeMonth));
                writer.PropertyName("liftChargedMonth");
                writer.Write(Fin(r.LiftChargedMonth));
                writer.PropertyName("runCostMonth");
                writer.Write(Fin(r.RunCostMonth));
                writer.PropertyName("buildingsSoFar");
                writer.Write(Fin(r.BuildingsSoFar));
                writer.PropertyName("wagesSoFar");
                writer.Write(Fin(r.WagesSoFar));
                writer.PropertyName("monthShare");
                writer.Write(Fin(r.MonthShare));
                writer.PropertyName("busChargedMonth");
                writer.Write(Fin(r.BusChargedMonth));
                writer.PropertyName("depots");
                writer.Write(Fin(r.Depots));
                writer.PropertyName("busGuestsMonth");
                writer.Write(Fin(r.BusGuestsMonth));
                writer.PropertyName("busBillMonth");
                writer.Write(Fin(r.BusBillMonth));
                writer.TypeEnd();
            }
            writer.ArrayEnd();
        }

        /// <summary>`runs` (bindings.ts RunRow: identity, rating, service, value parts).</summary>
        private void WriteRuns(IJsonWriter writer)
        {
            try
            {
                WriteRunsRows(writer);
            }
            catch (Exception e)
            {
                WriterFailed(writer, "runs", e);
            }
        }

        private void WriteRunsRows(IJsonWriter writer)
        {
            int count = m_Stats.RunCount;
            writer.ArrayBegin((uint)count);
            for (int i = 0; i < count; i++)
            {
                RunStats r = m_Stats.GetRun(i);
                writer.TypeBegin("SkiResortMod.RunRow");
                writer.PropertyName("entity");
                UnityWriters.Write(writer, r.Entity);
                writer.PropertyName("name");
                writer.Write(r.Name ?? "");
                writer.PropertyName("kind");
                writer.Write((int)r.Kind);
                writer.PropertyName("bound");
                writer.Write(r.Bound);
                writer.PropertyName("lengthM");
                writer.Write(Fin(r.LengthM));
                writer.PropertyName("dropM");
                writer.Write(Fin(r.DropM));
                writer.PropertyName("areaM2");
                writer.Write(Fin(r.AreaM2));
                writer.PropertyName("sign");
                UnityWriters.Write(writer, r.Sign);
                writer.PropertyName("ordinal");
                writer.Write(Fin(r.Ordinal));
                writer.PropertyName("customName");
                writer.Write(r.CustomName);
                writer.PropertyName("band");
                writer.Write((int)r.Band);
                writer.PropertyName("gradePct");
                writer.Write(Fin(r.GradePct));
                writer.PropertyName("steepestPct");
                writer.Write(Fin(r.SteepestPct));
                writer.PropertyName("approx");
                writer.Write(r.Approx);
                writer.PropertyName("member");
                writer.Write(r.Member);
                writer.PropertyName("served");
                writer.Write(r.Served);
                writer.PropertyName("servedBy");
                UnityWriters.Write(writer, r.ServedBy);
                writer.PropertyName("valueFull");
                writer.Write(Fin(r.ValueFull));
                writer.PropertyName("bandValue");
                writer.Write(Fin(r.BandValue));
                writer.PropertyName("sizeFactor");
                writer.Write(Fin(r.SizeFactor));
                writer.PropertyName("typeFactor");
                writer.Write(Fin(r.TypeFactor));
                writer.PropertyName("servedFactor");
                writer.Write(Fin(r.ServedFactor));
                writer.PropertyName("gunSpacingM");
                writer.Write(r.GunSpacingM);
                writer.PropertyName("lightSpacingM");
                writer.Write(r.LightSpacingM);
                writer.TypeEnd();
            }
            writer.ArrayEnd();
        }

        /// <summary>`runLive` (bindings.ts RunLive): same order and count as `runs`, joined by entity.</summary>
        private void WriteRunLive(IJsonWriter writer)
        {
            try
            {
                WriteRunLiveRows(writer);
            }
            catch (Exception e)
            {
                WriterFailed(writer, "runLive", e);
            }
        }

        private void WriteRunLiveRows(IJsonWriter writer)
        {
            int count = m_Stats.RunLiveCount;
            writer.ArrayBegin((uint)count);
            for (int i = 0; i < count; i++)
            {
                RunLiveStats r = m_Stats.GetRunLive(i);
                writer.TypeBegin("SkiResortMod.RunLive");
                writer.PropertyName("entity");
                UnityWriters.Write(writer, r.Entity);
                writer.PropertyName("openState");
                writer.Write((int)r.OpenState);
                writer.PropertyName("openFactor");
                writer.Write(Fin(r.OpenFactor));
                writer.PropertyName("snowPct");
                writer.Write(Fin(r.SnowPct));
                writer.PropertyName("snowEstimated");
                writer.Write(r.SnowEstimated);
                writer.PropertyName("valueNow");
                writer.Write(Fin(r.ValueNow));
                writer.PropertyName("costMonth");
                writer.Write(Fin(r.CostMonth));
                writer.PropertyName("snowDepthCm");
                writer.Write(r.SnowDepthCm);
                writer.PropertyName("gunWhy");        // HubRules RunGunWhy
                writer.Write(r.GunWhy);
                writer.TypeEnd();
            }
            writer.ArrayEnd();
        }

        /// <summary>`lifts` (bindings.ts LiftRow).</summary>
        private void WriteLifts(IJsonWriter writer)
        {
            try
            {
                WriteLiftsRows(writer);
            }
            catch (Exception e)
            {
                WriterFailed(writer, "lifts", e);
            }
        }

        private void WriteLiftsRows(IJsonWriter writer)
        {
            int count = m_Stats.LiftCount;
            writer.ArrayBegin((uint)count);
            for (int i = 0; i < count; i++)
            {
                LiftStats r = m_Stats.GetLift(i);
                writer.TypeBegin("SkiResortMod.LiftRow");
                writer.PropertyName("entity");
                UnityWriters.Write(writer, r.Entity);
                writer.PropertyName("top");
                UnityWriters.Write(writer, r.Top);
                writer.PropertyName("sign");
                UnityWriters.Write(writer, r.Sign);
                writer.PropertyName("name");
                writer.Write(r.Name ?? "");
                writer.PropertyName("customName");
                writer.Write(r.CustomName);
                writer.PropertyName("state");
                writer.Write((int)r.State);
                writer.PropertyName("sections");
                writer.Write(Fin(r.Sections));
                writer.PropertyName("towers");
                writer.Write(Fin(r.Towers));
                writer.PropertyName("chairs");
                writer.Write(Fin(r.Chairs));
                writer.PropertyName("chairsRiding");
                writer.Write(Fin(r.ChairsRiding));
                writer.PropertyName("gaps");
                writer.Write(Fin(r.Gaps));
                writer.PropertyName("towersRemoved");
                writer.Write(Fin(r.TowersRemoved));
                writer.PropertyName("lengthM");
                writer.Write(Fin(r.LengthM));
                writer.PropertyName("riseM");
                writer.Write(Fin(r.RiseM));
                writer.PropertyName("horizontalM");
                writer.Write(Fin(r.HorizontalM));
                writer.PropertyName("alignKnown");
                writer.Write(r.AlignKnown);
                writer.PropertyName("alignOffsetM");
                writer.Write(Fin(r.AlignOffsetM));
                writer.PropertyName("alignYawDeg");
                writer.Write(Fin(r.AlignYawDeg));
                writer.PropertyName("capacityPerHour");
                writer.Write(Fin(r.CapacityPerHour));
                writer.PropertyName("hasBase");
                writer.Write(r.HasBase);
                writer.PropertyName("hasTop");
                writer.Write(r.HasTop);
                writer.PropertyName("powered");
                writer.Write(r.Powered);
                writer.PropertyName("counted");
                writer.Write(r.Counted);
                writer.PropertyName("status");
                writer.Write((int)r.Status);
                writer.PropertyName("rideTimeS");
                writer.Write(Fin(r.RideTimeS));
                writer.PropertyName("runsServed");
                writer.Write(Fin(r.RunsServed));
                writer.PropertyName("costMonth");
                writer.Write(Fin(r.CostMonth));
                writer.PropertyName("billedReduced");
                writer.Write(r.BilledReduced);
                // The row's lift type (LiftTypeDef.Id, "" unknown) and that type's seats / speed (0 unknown) for the capacity and ride-time
                // tooltips.
                writer.PropertyName("type");
                writer.Write(r.Type ?? "");
                writer.PropertyName("seats");
                writer.Write(Fin(r.Seats));
                writer.PropertyName("speedMps");
                writer.Write(Fin(r.SpeedMps));
                writer.TypeEnd();
            }
            writer.ArrayEnd();
        }

        /// <summary>The lodgeParking rows: per live sign (snapshot order), its active road lodges (SkiLodgeParkingSystem.IsRoadLodge, the
        /// census's own test) by entity index, each with its last census (-1 before the first). A lodge listed by two signs shows once, under
        /// the first, as the census pairs it. Rebuilt only when the snapshot generation, the census revision or the lodge numbers moved.</summary>
        private void RefreshLodgeParking()
        {
            int attract = m_Attractiveness.Generation, census = m_Parking.Revision, numbers = m_Numbers.Revision;
            if (attract == m_LodgeParkingSigAttract && census == m_LodgeParkingSigCensus && numbers == m_LodgeParkingSigNumbers)
                return;
            m_LodgeParkingSigAttract = attract;
            m_LodgeParkingSigCensus = census;
            m_LodgeParkingSigNumbers = numbers;
            m_LodgeParkingRows.Clear();
            m_LodgeParkingSeen.Clear();
            m_Attractiveness.CopySigns(m_LodgeParkingSigns);
            for (int s = 0; s < m_LodgeParkingSigns.Count; s++)
            {
                Entity sign = m_LodgeParkingSigns[s];
                int first = m_LodgeParkingRows.Count;
                m_Attractiveness.CopyActiveLodgeInfo(sign, m_LodgeParkingInfo);
                for (int l = 0; l < m_LodgeParkingInfo.Count; l++)
                {
                    SkiResortAttractivenessSystem.LodgeInfo info = m_LodgeParkingInfo[l];
                    if (!SkiLodgeParkingSystem.IsRoadLodge(info) || !m_LodgeParkingSeen.Add(info.Lodge))
                        continue;
                    int i = info.LodgeIndex;
                    string item = i >= 0 && i < SkiCatalog.Lodges.Count ? SkiCatalog.Lodges[i].BuildItemId : "";
                    string raw = m_NameSystem.TryGetCustomName(info.Lodge, out string custom) ? custom : null;
                    m_LodgeParkingRows.Add(new LodgeParkingRowData
                    {
                        Sign = sign,
                        Lodge = info.Lodge,
                        Item = item,
                        CarSpaces = m_Parking.TryGetParking(info.Lodge, out LodgeParking p) && p.Done ? p.CarSpaces : -1,
                        Number = m_Numbers.ShownNumberOf(info.Lodge),
                        RawName = raw,
                        Name = LodgeNumberRule.DisplayName(raw),
                    });
                }
                // Stable order inside a sign: the snapshot's lodge order follows chunk order.
                m_LodgeParkingRows.Sort(first, m_LodgeParkingRows.Count - first,
                    Comparer<LodgeParkingRowData>.Create((a, b) => a.Lodge.Index != b.Lodge.Index ? a.Lodge.Index.CompareTo(b.Lodge.Index)
                                                                                                  : a.Lodge.Version.CompareTo(b.Lodge.Version)));
            }
            m_LodgeParkingRevision++;
        }

        /// <summary>A rename shows without a reload: the game's rename (TitleSection.OnRename, TitleSection.cs:48-52) writes NameSystem's
        /// dictionary and raises no event this system can see, and renaming a named lodge adds no new component (NameSystem.cs:214-241). So
        /// each frame the Guests view observes lodgeParking, every row reads its lodge's entry (NameSystem.TryGetCustomName, :209-212: one
        /// dictionary read; an unchanged name is the same string reference) and a change pushes the rows. A handful of rows: well under a
        /// microsecond a frame.</summary>
        private void PollLodgeNames()
        {
            bool changed = false;
            for (int i = 0; i < m_LodgeParkingRows.Count; i++)
            {
                LodgeParkingRowData r = m_LodgeParkingRows[i];
                string raw = m_NameSystem.TryGetCustomName(r.Lodge, out string custom) ? custom : null;
                if (ReferenceEquals(raw, r.RawName) || string.Equals(raw, r.RawName, StringComparison.Ordinal))
                    continue;
                string shown = LodgeNumberRule.DisplayName(raw);
                Mod.Log.Info($"Hub [lodge-name] lodge {Ent(r.Lodge)}: {(r.Name == null ? "(no custom name)" : "'" + r.Name + "'")} -> " +
                             $"{(shown == null ? "(no custom name: the tier word and number show)" : "'" + shown + "'")}");
                r.RawName = raw;
                r.Name = shown;
                m_LodgeParkingRows[i] = r;
                changed = true;
            }
            if (changed)
                m_LodgeParkingRevision++;
        }

        /// <summary>`lodgeParking` (bindings.ts LodgeParkingRow).</summary>
        private void WriteLodgeParking(IJsonWriter writer)
        {
            try
            {
                writer.ArrayBegin((uint)m_LodgeParkingRows.Count);
                for (int i = 0; i < m_LodgeParkingRows.Count; i++)
                {
                    LodgeParkingRowData r = m_LodgeParkingRows[i];
                    writer.TypeBegin("SkiResortMod.LodgeParkingRow");
                    writer.PropertyName("sign");
                    UnityWriters.Write(writer, r.Sign);
                    writer.PropertyName("lodge");
                    UnityWriters.Write(writer, r.Lodge);
                    writer.PropertyName("item");
                    writer.Write(r.Item ?? "");
                    writer.PropertyName("carSpaces");
                    writer.Write(r.CarSpaces);
                    writer.PropertyName("number");
                    writer.Write(r.Number);
                    writer.PropertyName("name");
                    writer.Write(r.Name ?? "");
                    writer.TypeEnd();
                }
                writer.ArrayEnd();
            }
            catch (Exception e)
            {
                WriterFailed(writer, "lodgeParking", e);
            }
        }

        /// <summary>`problems` (bindings.ts ProblemRow): grouped by area in `resorts` order.</summary>
        private void WriteProblems(IJsonWriter writer)
        {
            try
            {
                WriteProblemsRows(writer);
            }
            catch (Exception e)
            {
                WriterFailed(writer, "problems", e);
            }
        }

        private void WriteProblemsRows(IJsonWriter writer)
        {
            int count = m_Stats.ProblemCount;
            writer.ArrayBegin((uint)count);
            for (int i = 0; i < count; i++)
            {
                ProblemStats r = m_Stats.GetProblem(i);
                writer.TypeBegin("SkiResortMod.ProblemRow");
                writer.PropertyName("sign");
                UnityWriters.Write(writer, r.Sign);
                writer.PropertyName("code");
                writer.Write((int)r.Code);
                writer.PropertyName("severity");
                writer.Write((int)r.Severity);
                writer.PropertyName("subject");
                UnityWriters.Write(writer, r.Subject);
                writer.PropertyName("subjectTop");
                UnityWriters.Write(writer, r.SubjectTop);
                writer.PropertyName("name");
                writer.Write(r.Name ?? "");
                writer.PropertyName("count");
                writer.Write(Fin(r.Count));
                writer.PropertyName("pct");
                writer.Write(Fin(r.Pct));
                writer.PropertyName("factor");
                writer.Write(r.Factor ?? "");
                writer.PropertyName("more");
                writer.Write(Fin(r.More));
                writer.PropertyName("detail");
                writer.Write(Fin(r.Detail));
                writer.PropertyName("ifFixed");
                writer.Write(Fin(r.IfFixed));
                writer.PropertyName("buildItem");
                writer.Write(r.BuildItem ?? "");
                writer.TypeEnd();
            }
            writer.ArrayEnd();
        }

        /// <summary>HistoryArea.flags bit of the month in progress: binding only, never stored (bindings.ts HISTORY_FLAG.Current;
        /// the stored bits are HistoryFlags, SkiResortHistory.cs, 1..64).</summary>
        public const int HistoryFlagCurrent = 128;

        // Series indices of a HistoryRow, in the HistoryArea field order after sign / firstMonthKey.
        private const int HsFlags = 0, HsTourists = 1, HsResidents = 2, HsSkiDays = 3, HsTicketsPaid = 4, HsTicketIncome = 5,
                          HsLodgeIncome = 6, HsPassIncome = 7, HsLessonIncome = 8, HsLiftCost = 9, HsRunCost = 10, HsBuildingsCost = 11,
                          HsWagesCost = 12, HsAttraction = 13, HsSnowPct = 14, HsSnowDepthCm = 15, HsCount = 16;

        /// <summary>One HistoryArea row as it will be sent (rows are reused across pushes; no per-push allocation once warm).</summary>
        private sealed class HistoryRow
        {
            public Entity Sign;
            public int FirstKey;
            public readonly List<int>[] Series = new List<int>[HsCount];
            public int Count => Series[HsFlags].Count;

            public HistoryRow()
            {
                for (int s = 0; s < HsCount; s++)
                    Series[s] = new List<int>(64);
            }

            public void Clear(Entity sign)
            {
                Sign = sign;
                FirstKey = SkiResortLedger.NoMonth;
                for (int s = 0; s < HsCount; s++)
                    Series[s].Clear();
            }
        }

        private readonly List<HistoryRow> m_HistoryRows = new List<HistoryRow>(8);
        private int m_HistoryRowCount;
        private int m_HistoryPushKey;
        private int m_HistorySeenRevision = int.MinValue, m_HistorySeenResorts = int.MinValue, m_HistorySeenProgress = int.MinValue;
        private float m_HistoryNextProgressPush;

        /// <summary>A load: the last city's rows are dropped (the subscribe-time write then sends []).</summary>
        private void ResetHistoryPush()
        {
            m_HistoryRowCount = 0;
            m_HistorySeenRevision = m_HistorySeenResorts = m_HistorySeenProgress = int.MinValue;
            m_HistoryNextProgressPush = 0f;
            m_HistoryPushKey++;
        }

        /// <summary>Rebuild the rows only when the push will happen (a stored month, the area set, the throttled
        /// month in progress, or a (re)subscribe), then the shared PushArray bookkeeping (row-count lines, slow-push WARN).</summary>
        private void PushHistory()
        {
            bool active = m_History.active;
            if (active)
            {
                int revision = m_HistorySystem.Revision, resorts = m_Stats.ResortsRevision, progress = m_HistorySystem.ProgressRevision;
                float now = UnityEngine.Time.realtimeSinceStartup;
                bool due = !m_HistoryPush.WasActive || revision != m_HistorySeenRevision || resorts != m_HistorySeenResorts ||
                           (progress != m_HistorySeenProgress && now >= m_HistoryNextProgressPush);
                if (due)
                {
                    m_HistorySeenRevision = revision;
                    m_HistorySeenResorts = resorts;
                    m_HistorySeenProgress = progress;
                    m_HistoryNextProgressPush = now + Tuning.HistoryProgressPushS;
                    CollectHistory();
                    m_HistoryPushKey++;
                }
            }
            PushArray(m_HistoryPush, active, m_HistoryPushKey, m_HistoryRowCount);
        }

        /// <summary>One row per live sign of the stats `resorts` rows (their order) that has a stored month or a month
        /// in progress; the buffer densified (Gap markers expanded), the month in progress appended last.</summary>
        private void CollectHistory()
        {
            m_HistoryRowCount = 0;
            SkiResortLedger ledger = m_GuestCounter.Ledger;
            int progressKey = ledger != null && m_GuestCounter.IsArmed ? ledger.MonthKey : SkiResortLedger.NoMonth;
            EntityManager em = EntityManager;
            for (int i = 0; i < m_Stats.ResortCount; i++)
            {
                ResortStats rs = m_Stats.GetResort(i);
                Entity sign = rs.Sign;
                if (rs.Kind != 0 || sign == Entity.Null || !em.Exists(sign) || em.HasComponent<Deleted>(sign))
                    continue;
                if (m_HistoryRowCount == m_HistoryRows.Count)
                    m_HistoryRows.Add(new HistoryRow());
                HistoryRow row = m_HistoryRows[m_HistoryRowCount];
                row.Clear(sign);
                int next = SkiResortLedger.NoMonth;   // the key the next month must have
                if (em.HasBuffer<SkiResortHistoryMonth>(sign))
                {
                    DynamicBuffer<SkiResortHistoryMonth> b = em.GetBuffer<SkiResortHistoryMonth>(sign, isReadOnly: true);
                    for (int k = 0; k < b.Length; k++)
                    {
                        SkiResortHistoryMonth m = b[k];
                        if (row.Count == 0)
                        {
                            if (m.IsGap)
                                continue;   // a marker never leads a row (SkiResortHistory.Trim keeps it so; belt and braces)
                            row.FirstKey = m.m_MonthKey;
                        }
                        else if (m.m_MonthKey < next)
                            continue;       // keys are strictly increasing (Append); a stray older key is skipped
                        else
                            HistoryFillGap(row, next, m.m_MonthKey);   // nothing to fill unless a marker is missing
                        if (m.IsGap)
                        {
                            // A marker covers its key .. the next element's key − 1 (or up to the month in progress).
                            int until = k + 1 < b.Length ? b[k + 1].m_MonthKey : progressKey;
                            until = math.max(until, m.m_MonthKey + 1);
                            HistoryFillGap(row, m.m_MonthKey, until);
                            next = until;
                        }
                        else
                        {
                            HistoryAdd(row, m, m.m_SnowPct == SkiResortHistoryMonth.SnowUnknown ? -1 : m.m_SnowPct);
                            next = m.m_MonthKey + 1;
                        }
                    }
                }
                if (progressKey != SkiResortLedger.NoMonth && (row.Count == 0 || progressKey >= next))
                {
                    if (row.Count == 0)
                        row.FirstKey = progressKey;
                    else
                        HistoryFillGap(row, next, progressKey);
                    HistoryAddProgress(row, ledger, i, progressKey);
                }
                if (row.Count > 0)
                    m_HistoryRowCount++;
            }
        }

        /// <summary>GAP months for keys [from, to). A span longer than the 1200-month safety cap (a dev-tool calendar jump)
        /// restarts the row at `to`: the older months are not sent (they stay stored).</summary>
        private static void HistoryFillGap(HistoryRow row, int from, int to)
        {
            if (to <= from)
                return;
            if (to - from > SkiResortHistory.CapMonths)
            {
                row.Clear(row.Sign);
                row.FirstKey = to;
                return;
            }
            SkiResortHistoryMonth gap = SkiResortHistoryMonth.Gap(from);
            for (int key = from; key < to; key++)
                HistoryAdd(row, gap, -1);
        }

        private static void HistoryAdd(HistoryRow row, in SkiResortHistoryMonth m, int snowPct)
        {
            List<int>[] s = row.Series;
            s[HsFlags].Add(m.m_Flags);
            s[HsTourists].Add(m.m_Tourists);
            s[HsResidents].Add(m.m_Residents);
            s[HsSkiDays].Add(m.m_SkiDays);
            s[HsTicketsPaid].Add(m.m_TicketsPaid);
            s[HsTicketIncome].Add(m.m_TicketIncome);
            s[HsLodgeIncome].Add(m.m_LodgeIncome);
            s[HsPassIncome].Add(m.m_PassIncome);
            s[HsLessonIncome].Add(m.m_LessonIncome);
            s[HsLiftCost].Add(m.m_LiftCost);
            s[HsRunCost].Add(m.m_RunCost);
            s[HsBuildingsCost].Add(m.m_BuildingsCost);
            s[HsWagesCost].Add(m.m_WagesCost);
            s[HsAttraction].Add(m.m_Attraction);
            s[HsSnowPct].Add(snowPct);
            s[HsSnowDepthCm].Add(m.m_SnowDepthCm);   // -1 unknown (gap months, v1 elements)
        }

        /// <summary>The month in progress: the ledger's This booked so far (the stored months' own mapping,
        /// SkiResortHistoryMonth.FromLedger), ski days so far; B1: the eight money terms from the ResortLive row (its NetTerms, the
        /// Finances tile's rows: History's last bar nets to netMonth exactly); attraction and snow now. Flags CURRENT | PARTIAL when the
        /// ledger counts it since load.</summary>
        private void HistoryAddProgress(HistoryRow row, SkiResortLedger ledger, int resortIndex, int key)
        {
            Entity sign = row.Sign;
            SkiResortLedger.Month month = ledger.TryGet(SkiResortStatsSystem.ToRef(sign), out SkiResortLedger.Entry e) ? e.This : default;
            int flags = HistoryFlagCurrent | (ledger.ThisMonthSinceLoad ? HistoryFlags.Partial : 0);
            SkiResortHistoryMonth m = SkiResortHistoryMonth.FromLedger(month, key, 0);
            if (m_SkiDays.IsArmed && m_SkiDays.TryGetArea(sign, out SkiDayAreaView view) && view.ArrivedThisMonth >= 0)
                m.m_SkiDays = view.ArrivedThisMonth;
            int snow = -1;
            if (TryLiveOf(sign, resortIndex, out ResortLiveStats live))
            {
                HubMonthMoney t = live.NetTerms;
                if (t.TicketIncome >= 0)
                {
                    // the six booked terms as the tile has them (one stats rebuild), not a fresher ledger read: one Net on screen
                    m.m_TicketIncome = t.TicketIncome;
                    m.m_LodgeIncome = t.LodgeIncome;
                    m.m_LessonIncome = t.LessonIncome;
                    m.m_PassIncome = t.PassIncome;
                    m.m_LiftCost = t.LiftCost;
                    m.m_RunCost = t.RunCost;
                }
                m.m_BuildingsCost = t.BuildingsCost >= 0 ? t.BuildingsCost : SkiResortHistoryMonth.Unknown;
                m.m_WagesCost = t.WagesCost >= 0 ? t.WagesCost : SkiResortHistoryMonth.Unknown;
                m.m_Attraction = live.Attraction >= 0 ? live.Attraction : SkiResortHistoryMonth.Unknown;
                byte b = SkiResortHistoryMonth.SnowByte(live.SnowPct);
                snow = b == SkiResortHistoryMonth.SnowUnknown ? -1 : b;
                m.m_SnowDepthCm = SkiResortHistoryMonth.DepthCm(live.SnowDepthCm);
            }
            HistoryAdd(row, m, snow);
            row.Series[HsFlags][row.Count - 1] = flags;
        }

        /// <summary>The sign's ResortLive row: same index as its resorts row when the arrays agree, else searched.</summary>
        private bool TryLiveOf(Entity sign, int resortIndex, out ResortLiveStats live)
        {
            if (resortIndex < m_Stats.ResortLiveCount)
            {
                live = m_Stats.GetResortLive(resortIndex);
                if (live.Sign == sign)
                    return true;
            }
            for (int j = 0; j < m_Stats.ResortLiveCount; j++)
            {
                live = m_Stats.GetResortLive(j);
                if (live.Sign == sign)
                    return true;
            }
            live = default;
            return false;
        }

        /// <summary>`history` (bindings.ts HistoryArea): the rows CollectHistory built.</summary>
        private void WriteHistory(IJsonWriter writer)
        {
            try
            {
                WriteHistoryRows(writer);
            }
            catch (Exception e)
            {
                WriterFailed(writer, "history", e);
            }
        }

        private void WriteHistoryRows(IJsonWriter writer)
        {
            writer.ArrayBegin((uint)m_HistoryRowCount);
            for (int i = 0; i < m_HistoryRowCount; i++)
            {
                HistoryRow r = m_HistoryRows[i];
                writer.TypeBegin("SkiResortMod.HistoryArea");
                writer.PropertyName("sign");
                UnityWriters.Write(writer, r.Sign);
                writer.PropertyName("firstMonthKey");
                writer.Write(r.FirstKey);
                writer.PropertyName("seasonFirstMonth0");
                writer.Write(SkiClimateCalendar.SeasonFirstMonth(m_ClimateSystem, m_PrefabSystem) - 1);
                writer.PropertyName("flags");
                WriteHistorySeries(writer, r.Series[HsFlags]);
                writer.PropertyName("tourists");
                WriteHistorySeries(writer, r.Series[HsTourists]);
                writer.PropertyName("residents");
                WriteHistorySeries(writer, r.Series[HsResidents]);
                writer.PropertyName("skiDays");
                WriteHistorySeries(writer, r.Series[HsSkiDays]);
                writer.PropertyName("ticketsPaid");
                WriteHistorySeries(writer, r.Series[HsTicketsPaid]);
                writer.PropertyName("ticketIncome");
                WriteHistorySeries(writer, r.Series[HsTicketIncome]);
                writer.PropertyName("lodgeIncome");
                WriteHistorySeries(writer, r.Series[HsLodgeIncome]);
                writer.PropertyName("passIncome");
                WriteHistorySeries(writer, r.Series[HsPassIncome]);
                writer.PropertyName("lessonIncome");
                WriteHistorySeries(writer, r.Series[HsLessonIncome]);
                writer.PropertyName("liftCost");
                WriteHistorySeries(writer, r.Series[HsLiftCost]);
                writer.PropertyName("runCost");
                WriteHistorySeries(writer, r.Series[HsRunCost]);
                writer.PropertyName("buildingsCost");
                WriteHistorySeries(writer, r.Series[HsBuildingsCost]);
                writer.PropertyName("wagesCost");
                WriteHistorySeries(writer, r.Series[HsWagesCost]);
                writer.PropertyName("attraction");
                WriteHistorySeries(writer, r.Series[HsAttraction]);
                writer.PropertyName("snowPct");
                WriteHistorySeries(writer, r.Series[HsSnowPct]);
                writer.PropertyName("snowDepthCm");
                WriteHistorySeries(writer, r.Series[HsSnowDepthCm]);
                writer.TypeEnd();
            }
            writer.ArrayEnd();
        }

        private static void WriteHistorySeries(IJsonWriter writer, List<int> values)
        {
            writer.ArrayBegin((uint)values.Count);
            for (int i = 0; i < values.Count; i++)
                writer.Write(values[i]);
            writer.ArrayEnd();
        }

        // Prefab resolution: each item is resolved by SkiPrefabCatalogSystem.TryGetPrefab (same per-load cache as every other
        // consumer); the (type, name) pair is the table row's (SkiCatalog.TypeNameOf(kind), Prefab); every item is classed expected /
        // pending found / pending missing (not sent) and Shuttle (not sent).

        /// <summary>Resolves every item, fills m_ItemPrefab / m_ItemPrefabEntity / m_ItemSent / m_SentCount, and returns the ARMED
        /// line's fragment: "build prefabs 9/9 expected [sign=SkiResortGate, …], 0 pending found and offered, 16 pending not offered
        /// (not imported yet: lodgeHut, …), tram moved by TramShuttleSystem" ("tram NOT offered: TramShuttleSystem not registered (…)"
        /// when the mover is absent). An Expected item that is MISSING keeps " MISSING" in the list (sent, locked by
        /// REASON[Missing]).</summary>
        private string ResolveItems()
        {
            var expected = new StringBuilder(256);
            var pendingFound = new StringBuilder(64);
            var pendingMissing = new StringBuilder(256);
            int expectedCount = 0, expectedFound = 0, pendingFoundCount = 0, pendingMissingCount = 0, sent = 0;
            // A Shuttle type's items are offered when TramShuttleSystem is
            // registered (Mod.cs, mover group) — it moves the tram's cabins; resolved here, at event time (arm and catalog
            // re-resolution), never per frame.
            bool tramMoverRegistered = World.GetExistingSystemManaged<TramShuttleSystem>() != null;
            for (int i = 0; i < ItemCount; i++)
            {
                BuildItemDef item = Items[i];
                m_ItemPrefab[i] = null;
                m_ItemPrefabEntity[i] = Entity.Null;
                m_ItemPrice[i] = -1;
                m_PrefabCatalog.TryGetPrefab(SkiCatalog.TypeNameOf(item.Kind), item.Prefab, out Entity entity, out PrefabBase prefab);
                bool found = prefab != null && entity != Entity.Null;
                if (found)
                {
                    m_ItemPrefab[i] = prefab;
                    m_ItemPrefabEntity[i] = entity;
                }
                bool expectedName = SkiCatalog.IsExpected(item.Prefab);
                LiftTypeDef type = s_ItemTypeIndex[i] >= 0 ? SkiCatalog.LiftByIndex(s_ItemTypeIndex[i]) : null;
                bool shuttle = type != null && type.Mover == MoverKind.Shuttle;
                // The rule for every item: sent when (expected name or found), plus "a Shuttle item needs the tram mover registered".
                m_ItemSent[i] = (expectedName || found) && (!shuttle || tramMoverRegistered);
                if (m_ItemSent[i])
                    sent++;
                if (expectedName)
                {
                    expectedCount++;
                    if (found)
                        expectedFound++;
                    if (expected.Length > 0)
                        expected.Append(", ");
                    expected.Append(item.Id).Append('=').Append(item.Prefab).Append(found ? "" : " MISSING");
                }
                else if (found)
                {
                    pendingFoundCount++;
                    if (pendingFound.Length > 0)
                        pendingFound.Append(", ");
                    // A found pending Shuttle item is offered once the mover is registered, so only the unregistered case is marked.
                    pendingFound.Append(item.Id).Append('=').Append(item.Prefab)
                                .Append(shuttle && !tramMoverRegistered ? " (Shuttle: NOT offered, TramShuttleSystem not registered)" : "");
                }
                else
                {
                    pendingMissingCount++;
                    if (pendingMissing.Length > 0)
                        pendingMissing.Append(", ");
                    pendingMissing.Append(item.Id);
                }
            }
            m_SentCount = sent;
            var shuttles = new StringBuilder(64);
            for (int t = 0; t < SkiCatalog.Lifts.Count; t++)
            {
                LiftTypeDef def = SkiCatalog.Lifts[t];
                if (def.Mover != MoverKind.Shuttle)
                    continue;
                if (shuttles.Length > 0)
                    shuttles.Append(", ");
                shuttles.Append(def.Id).Append(tramMoverRegistered
                    ? " moved by TramShuttleSystem"
                    : " NOT offered: TramShuttleSystem not registered (its station and line items)");
            }
            return $"build prefabs {expectedFound}/{expectedCount} expected [{expected}], {pendingFoundCount} pending found " +
                   $"and offered{(pendingFoundCount > 0 ? " [" + pendingFound + "]" : "")}, {pendingMissingCount} pending not " +
                   $"offered (not imported yet{(pendingMissingCount > 0 ? ": " + pendingMissing : "")})" +
                   (shuttles.Length > 0 ? ", " + shuttles : "") + $"; {sent} of {ItemCount} sent";
        }

        private bool IsStation(Entity entity)
        {
            // The prefab catalog's station test.
            return entity != Entity.Null && EntityManager.Exists(entity) &&
                   !EntityManager.HasComponent<Deleted>(entity) &&
                   EntityManager.TryGetComponent(entity, out PrefabRef prefabRef) &&
                   m_PrefabCatalog.IsStation(prefabRef.m_Prefab);
        }

        private string PrefabNameOf(Entity entity)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity) || !EntityManager.TryGetComponent(entity, out PrefabRef prefabRef))
                return "(no prefab)";
            return m_PrefabSystem.GetPrefabName(prefabRef.m_Prefab);
        }

        private static Entity ToEntity(EntityRef r)
        {
            return new Entity { Index = r.Index, Version = r.Version };
        }

        private static string Ent(Entity e)
        {
            return e == Entity.Null ? "null" : $"{e.Index}:{e.Version}";
        }

        /// <summary>Observer counts of every value binding (dev tab, DUMP).</summary>
        public string ObserverText()
        {
            if (m_HubOpen == null)
                return "-";
            return $"hubOpen {m_HubOpen.observerCount}, contract {m_Contract.observerCount}, buildStamp " +
                   $"{m_BuildStamp.observerCount}, showHubButton {m_ShowHubButton.observerCount}, build " +
                   $"{m_Build.observerCount}, glade {m_Glade.observerCount}, resorts {m_Resorts.observerCount}, " +
                   $"resortLive {m_ResortLive.observerCount}, selectedResort {m_SelectedBinding.observerCount}, runs " +
                   $"{m_Runs.observerCount}, runLive {m_RunLive.observerCount}, lifts {m_Lifts.observerCount}, problems " +
                   $"{m_Problems.observerCount}, city {m_City.observerCount}, pisteMap {m_PisteMapBinding.observerCount}, " +
                   $"follow {m_FollowBinding.observerCount}, history {m_History.observerCount}";
        }

        /// <summary>Pushes per array (dev tab, DUMP).</summary>
        public string PushText()
        {
            return $"{PushPart(m_ResortsPush, true)}, {PushPart(m_ResortLivePush, false)}, {PushPart(m_RunsPush, false)}, " +
                   $"{PushPart(m_RunLivePush, false)}, {PushPart(m_LiftsPush, false)}, {PushPart(m_ProblemsPush, false)}; " +
                   $"non-finite numbers sent as 0: {s_NonFinite}";
        }

        private static string PushPart(ArrayPush p, bool first)
        {
            return first
                ? $"{p.Name} {p.Pushes} (last {p.LastMs:F2} / max {p.MaxMs:F2} ms)"
                : $"{p.Name} {p.Pushes} ({p.LastMs:F2} / {p.MaxMs:F2})";
        }

        /// <summary>The DUMP's `hub v3:` line (hover, piste map, rule 7, pushes).</summary>
        private string HubV3Text()
        {
            int suppressed = FollowsSuppressedClosed + FollowsSuppressedRule + FollowsSuppressedUser + FollowsSuppressedSoon;
            float pushMax = Math.Max(Math.Max(Math.Max(m_ResortsPush.MaxMs, m_ResortLivePush.MaxMs), Math.Max(m_RunsPush.MaxMs, m_RunLivePush.MaxMs)),
                Math.Max(m_LiftsPush.MaxMs, m_ProblemsPush.MaxMs));
            return $"hovers {HoverAccepted} (refused {HoverRefused}, calls {HoverCalls}), piste map {(m_PisteMapOn ? "on" : "off")} " +
                   $"(visible {PisteMapVisible}, UI contract ok {m_UiContractOk}), follows {m_FollowCount} (suppressed {suppressed}: " +
                   $"hub closed {FollowsSuppressedClosed}, same-pass rule {FollowsSuppressedRule}, user switch " +
                   $"{FollowsSuppressedUser}, too soon {FollowsSuppressedSoon}), pushes resorts {m_ResortsPush.Pushes} resortLive " +
                   $"{m_ResortLivePush.Pushes} runs {m_RunsPush.Pushes} runLive {m_RunLivePush.Pushes} lifts {m_LiftsPush.Pushes} " +
                   $"problems {m_ProblemsPush.Pushes}, push max {pushMax:F2} ms";
        }

        /// <summary>Active tool, its mode and the selection (dev tab, DUMP).</summary>
        public string ToolText()
        {
            if (m_ToolSystem == null)
                return "-";
            ToolBaseSystem tool = m_ToolSystem.activeTool;
            int item = m_Armed ? ActiveItemIndex() : -1;
            return $"tool '{ToolId(tool)}' mode {ModeText(tool)}, hub item {(item >= 0 ? Items[item].Id : "-")}, selected " +
                   $"{Ent(m_ToolSystem.selected)}";
        }

        private void DumpToLog(string source)
        {
            var sb = new StringBuilder(4096);
            sb.Append("Hub: DUMP (").Append(source).Append(") — armed ").Append(m_Armed)
              .Append(", disabled ").Append(m_Disabled)
              .Append(", open ").Append(IsOpen)
              .Append(", contract ").Append(HubContract)
              .Append(", DLL stamp ").Append(s_DllStamp).Append(" (UI bundled at build ").Append(BuildStamp.UiBundled).Append(')')
              .Append("; handshake: ").Append(HandshakeText)
              .Append("; triggers ").Append(TriggerCount)
              .Append(", UI errors ").Append(m_UiErrors.Count).Append(" distinct / ").Append(UiErrorRepeats).Append(" repeats")
              .Append("; last trigger: ").Append(LastTriggerText).Append('\n');
            sb.Append("  observers: ").Append(ObserverText()).Append('\n');
            sb.Append("  pushes: ").Append(PushText()).Append('\n');
            sb.Append("  settings: ShowHubButton ").Append(Mod.Settings == null || Mod.Settings.ShowHubButton)
              .Append(", OpenHubOnSignSelect ").Append(Mod.Settings == null || Mod.Settings.OpenHubOnSignSelect)
              .Append(", LiftBuildAids ").Append(Mod.Settings == null || Mod.Settings.LiftBuildAids).Append('\n');
            // "upgrade launch": every upgrade item takes the lodge path.
            sb.Append("  ").Append(ToolText()).Append('\n');
            sb.Append("  selected resort: ").Append(ResortLabel(m_SelectedResort)).Append("; last select: ").Append(LastSelectText)
              .Append("; last layout: ").Append(m_LastLayout ?? "-").Append('\n');
            sb.Append("  hub v3: ").Append(HubV3Text()).Append('\n');
            // Every table row; not-sent items are marked (pending not imported, or a Shuttle type).
            // A found item is unsent only for want of the tram mover.
            sb.Append("  build (").Append(m_SentCount).Append(" of ").Append(ItemCount).Append(" sent):");
            for (int i = 0; i < ItemCount; i++)
            {
                sb.Append(' ').Append(Items[i].Id).Append('[')
                  .Append(m_ItemPrefab[i] != null ? "ok" : "MISSING")
                  .Append(m_ItemSent[i] ? "" : ", NOT SENT (" + (m_ItemPrefab[i] == null ? "pending" : "no tram mover") + ")")
                  .Append(m_ItemLocked[i] ? ", locked " + ReasonKey(m_ItemReason[i]) : "")
                  .Append(Items[i].IsUpgrade && m_ItemForbidMultiple[i] ? ", one per sign" : "")
                  .Append(m_ItemLuxury[i] ? ", luxury" : "");
                AppendItemValue(sb, "price", m_ItemPrice[i]);
                AppendItemValue(sb, "perHa", m_ItemPricePerHa[i]);
                AppendItemValue(sb, "cost", m_ItemCost[i]);
                AppendItemValue(sb, "perKm", m_ItemCostPerKm[i]);
                AppendItemValue(sb, "month", m_ItemMonthly[i]);
                AppendItemValue(sb, "monthPerHa", m_ItemMonthlyPerHa[i]);
                AppendItemValue(sb, "monthPerKm", m_ItemMonthlyPerKm[i]);
                sb.Append(LiftNoPairOf(i) ? ", hint LiftNoPair" : "")
                  .Append(i == m_ActiveItem ? ", ACTIVE" : "").Append(']');
            }
            sb.Append('\n');
            // The prefab catalog's state (load, generation, validations,
            // re-resolves, the last ARM / re-resolve text) and the generation the build items were resolved at.
            sb.Append("  ").Append(m_PrefabCatalog.Describe()).Append(" (hub items at generation ")
              .Append(m_ItemsGeneration == int.MinValue ? "-" : m_ItemsGeneration.ToString(CultureInfo.InvariantCulture))
              .Append(")\n");
            HubGladeState g = GladeNow();
            sb.Append("  glade: available ").Append(g.Available).Append(", active ").Append(g.Active)
              .Append(", level ").Append(g.Level).Append(", tree line ").Append(g.TreeLine)
              .Append(", reason '").Append(g.Reason).Append("'\n");
            HubLiftLineState ll = LiftLineNow();
            sb.Append("  liftLine: available ").Append(ll.Available).Append(", active ").Append(ll.Active)
              .Append(", state ").Append(ll.State).Append('\n');
            AppendQuickRunsDump(sb);
            // The stats system's own dump: every area row with its live numbers and problems, every lift row,
            // city; then the piste map's one line (written once, here).
            sb.Append("  stats rows: ").Append(m_Stats.ResortCount).Append(" resort(s), ").Append(m_Stats.ResortLiveCount)
              .Append(" live, ").Append(m_Stats.RunCount).Append(" run(s), ").Append(m_Stats.RunLiveCount).Append(" live, ")
              .Append(m_Stats.LiftCount).Append(" lift(s), ").Append(m_Stats.ProblemCount).Append(" problem row(s)\n");
            m_Stats.AppendDump(sb);
            m_PisteMap ??= World.GetExistingSystemManaged<SkiPisteMapSystem>();
            if (m_PisteMap != null)
                m_PisteMap.AppendDump(sb);
            else
                sb.Append("  piste map: system not registered\n");
            sb.Append("  census: revision ").Append(m_Census.Revision).Append(", builds ").Append(m_Census.Builds)
              .Append(", last event '").Append(m_Census.LastEvent).Append("', last build ")
              .Append(m_Census.LastBuildMs.ToString("F2", CultureInfo.InvariantCulture)).Append(" ms (max ")
              .Append(m_Census.MaxBuildMs.ToString("F2", CultureInfo.InvariantCulture)).Append(")\n");
            sb.Append("  infoview: ").Append(m_Infoview.StateText()).Append('\n');
            AppendCitizenPresence(sb);
            AppendStationPower(sb);
            AppendLodgeRoads(sb);
            m_Catalog.AppendDump(sb);
            Mod.Log.Info(sb.ToString().TrimEnd('\n'));
            // Tower diagnostic: the "Towers [nodes]" blocks, one per lift
            // line, read-only, DUMP only. Resolved at DUMP time like m_PisteMap; DumpAll never throws.
            World.GetExistingSystemManaged<LiftTowerDiagSystem>()?.DumpAll("hub dump " + source);
            // Lodge spawn spots: the "Lodge [spots]" block, read-only, DUMP
            // only: every lodge's registered spawn spots (SpawnLocationElement buffer). Resolved at DUMP time; DumpAll never throws.
            World.GetExistingSystemManaged<SkiLodgeSpotDiag>()?.DumpAll("hub dump " + source);
            LastTriggerText = $"dumpHub ({source})";
        }

        /// <summary>Dev only, O(citizens), DUMP only: citizens whose CurrentBuilding
        /// (Game.Citizens/CurrentBuilding.cs) is one of our signs or a lodge — settles whether a visitor's
        /// CurrentBuilding is the lodge or the sign.</summary>
        private void AppendCitizenPresence(StringBuilder sb)
        {
            var atSign = new Dictionary<Entity, int>();
            var atLodge = new Dictionary<Entity, int>();
            var kindOf = new Dictionary<Entity, byte>();   // 0 other, 1 sign, 2 lodge
            int citizens = 0;
            using (NativeArray<Game.Citizens.CurrentBuilding> current =
                       m_CitizenQuery.ToComponentDataArray<Game.Citizens.CurrentBuilding>(Allocator.Temp))
            {
                citizens = current.Length;
                for (int i = 0; i < current.Length; i++)
                {
                    Entity b = current[i].m_CurrentBuilding;
                    if (b == Entity.Null)
                        continue;
                    if (!kindOf.TryGetValue(b, out byte kind))
                    {
                        kind = 0;
                        if (m_Ownership.IsLiveGate(b))
                            kind = 1;
                        else if (EntityManager.TryGetComponent(b, out PrefabRef prefabRef) &&
                                 m_Attractiveness.IsLodgePrefab(prefabRef.m_Prefab))
                            kind = 2;
                        kindOf[b] = kind;
                    }
                    if (kind == 1)
                        atSign[b] = atSign.TryGetValue(b, out int n) ? n + 1 : 1;
                    else if (kind == 2)
                        atLodge[b] = atLodge.TryGetValue(b, out int m) ? m + 1 : 1;
                }
            }
            sb.Append("  presence (").Append(citizens).Append(" citizens with a CurrentBuilding): at signs");
            if (atSign.Count == 0)
                sb.Append(" none");
            foreach (KeyValuePair<Entity, int> kv in atSign)
                sb.Append(' ').Append(Ent(kv.Key)).Append('=').Append(kv.Value);
            sb.Append("; at lodges");
            if (atLodge.Count == 0)
                sb.Append(" none");
            foreach (KeyValuePair<Entity, int> kv in atLodge)
                sb.Append(' ').Append(Ent(kv.Key)).Append('=').Append(kv.Value);
            sb.Append('\n');
        }

        /// <summary>DUMP only: every station's ElectricityConsumer
        /// (Game.Buildings/ElectricityConsumer.cs: wanted, fulfilled, cooldown, flags).</summary>
        private void AppendStationPower(StringBuilder sb)
        {
            sb.Append("  station power:");
            int stations = 0;
            using (NativeArray<Entity> entities = m_ConsumerQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity e = entities[i];
                    if (!IsStation(e) || !EntityManager.TryGetComponent(e, out Game.Buildings.ElectricityConsumer c))
                        continue;
                    stations++;
                    sb.Append(' ').Append(PrefabNameOf(e)).Append(' ').Append(Ent(e)).Append(" wanted ")
                      .Append(c.m_WantedConsumption).Append(" fulfilled ").Append(c.m_FulfilledConsumption)
                      .Append(" cooldown ").Append(c.m_CooldownCounter).Append(" flags ").Append(c.m_Flags).Append(';');
                }
            }
            if (stations == 0)
                sb.Append(" no station carries an ElectricityConsumer");
            sb.Append('\n');
            // The station LV icon state and the mountain jobs on the signs.
            World.GetExistingSystemManaged<StationLvIconSystem>()?.AppendDump(sb);
            World.GetExistingSystemManaged<MountainJobsSystem>()?.AppendDump(sb);
            World.GetExistingSystemManaged<OffSeasonStaffSystem>()?.AppendDump(sb);
            World.GetExistingSystemManaged<LodgeSupplySystem>()?.AppendDump(sb);
            World.GetExistingSystemManaged<LodgeNumberSystem>()?.AppendDump(sb);
        }

        /// <summary>The lodge road system's lines (per lodge and sign: road edge; "Lodge roads: n lodge(s), c with a road"), resolved
        /// at DUMP time with GetExistingSystemManaged (the m_PisteMap rule: never create a system from here). A throw is written into
        /// the DUMP and does not close the hub.</summary>
        private void AppendLodgeRoads(StringBuilder sb)
        {
            m_LodgeRoad ??= World.GetExistingSystemManaged<LodgeRoadSystem>();
            if (m_LodgeRoad == null)
                sb.Append("  lodge roads: system not registered\n");
            else
            {
                try
                {
                    m_LodgeRoad.AppendDump(sb);
                }
                catch (Exception e)
                {
                    sb.Append("\n  lodge roads: DUMP failed (").Append(e.GetType().Name).Append(": ").Append(e.Message).Append(")\n");
                }
            }
        }

        internal void DevRequest(string name, int kind, string text, Entity entity)
        {
            try
            {
                TriggerCount++;
                var request = new Request { Kind = (RequestKind)kind, Text = text, Entity = entity, Source = "dev tab" };
                if (!Enabled || !m_Armed)
                {
                    Mod.Log.Info($"Hub: [dev tab] {name} ignored — not in a loaded city");
                    return;
                }
                if (m_Disabled)
                {
                    RefuseDisabled(name, request);
                    return;
                }
                Enqueue(name, request);
            }
            catch (Exception e)
            {
                HandleException(e, "dev tab " + name);
            }
        }

        internal const int DevKindToggleHub = (int)RequestKind.ToggleHub;
        internal const int DevKindLaunch = (int)RequestKind.Launch;
        internal const int DevKindFocus = (int)RequestKind.Focus;
        internal const int DevKindGladeToggle = (int)RequestKind.GladeToggle;
        internal const int DevKindDump = (int)RequestKind.Dump;
        internal const int DevKindTogglePisteMap = (int)RequestKind.TogglePisteMap;

        /// <summary>The build ids of the SkiCatalog table (the dev tab's launch buttons follow them; a pending item that is not imported
        /// is refused at launch with its own line).</summary>
        internal static IReadOnlyList<string> BuildItemIds => s_ItemIds;

        /// <summary>DUMP build line: ", name value", omitted for −1.</summary>
        private static void AppendItemValue(StringBuilder sb, string name, int value)
        {
            if (value != -1)
                sb.Append(", ").Append(name).Append(' ').Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private void HandleException(Exception e, string where)
        {
            m_Disabled = true;
            if (!m_ErrorLogged)
            {
                m_ErrorLogged = true;
                Mod.Log.Error($"!!! {nameof(SkiResortHubUISystem)} EXCEPTION in {where} — the Ski Resort hub is closed " +
                              "and refuses every request until the next game load (each refusal is logged); the UI " +
                              $"is told contract {ContractAbsent} and shows its \"not connected\" notice:\n" + e);
            }
            try
            {
                if (m_Catalog != null)
                    m_Catalog.Demand = false;
                if (m_Stats != null)
                    m_Stats.Demand = false;
                if (m_HubOpen != null)
                    m_HubOpen.Update(false);
                // ContractAbsent: the UI's existing absent path (bindings.ts CONTRACT_ABSENT, hubState.ts):
                // launchers toggle a local flag and the panel shows LOC.notConnected; OnGamePreload restores it.
                if (m_Contract != null)
                    m_Contract.Update(ContractAbsent);
            }
            catch (Exception)
            {
                // already in the failure path; the "!!!" line carries the cause
            }
        }
    }
}
