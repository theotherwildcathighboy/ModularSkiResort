// CatalogTests — the PURE prefab catalog SkiCatalog.cs. Catalog Phase A (docs/research/art/assets-2026-10/catalog-a/
// phase-a-contract.md §6.1) created CT-1..CT-10; Catalog Phase B (docs/research/art/assets-2026-10/code-round-2/
// round-2-contract.md §11.1 with round-2-contract-skeptic.md MUST 2, 5, 7, 8) updates them and adds CT-11..CT-16.
// Core builder, 2026-10-11. Where-tags `catalog-…`.
//   CT-1  SkiCatalog.Problems is empty (the static initialiser never throws; it reports here).
//   CT-2  AllPrefabNames(): 30 entries (30 expected, 0 pending since the S1 lift flip, 2026-10-13; 25 / 5 before it, 11 / 19 before S1), unique,
//         "Ski…", one of the four PrefabID types,
//         kind ↔ type, 25 item ids unique; ExpectedNameCount / PendingNameCount.
//   CT-3  every lift type has exactly one Base and one Top station (StationOf answers them); station → known type and
//         index; a carrier in one type; Ids unique; Carriers mirror CarrierPrefabs.
//   CT-4  the FROZEN-NAME ORACLE: all 30 names as literals, in NAMES-block order, with kind / type / item / expected,
//         because names are save identity (PrefabSystem.Serialize writes type + name, PrefabSystem.cs:827-865; ext
//         F15). A rename fails here first. Plus every lookup's positives and negatives.
//   CT-5  every lift row == literal oracles (§1.3) incl. computed LinePerKm and ApproachPinCeilingM; row 0 == the
//         FixedGrip consts; lodge (§1.5), school, patrol and sign rows == literals; the spec arrays are copied.
//   CT-6  the 8 station rows: wheels, stub tips, costs, items, roles, types, flags; FG stub == StationAlign == −12.
//   CT-7  the 25 build items in strip order (id, prefab, kind, lift type, run kind); TryGetBuildItem / TryGetBuildItemDef.
//   CT-8  the NAMES block as TEXT with tools/preflight.ps1 section 7's regex (now with ", pending"): name, type, item and
//         pending, in order, == AllPrefabNames().
//   CT-9  REPLACED (contract §11.1, skeptic MUST 8): the BUILD-ITEMS block as TEXT (markers INSIDE the braces, every line
//         between them a row of §1.6's regex) == BuildItems row by row; every NAMES item token ↔ exactly one row.
//   CT-10 the resolver-lifetime decision table, SkiCatalog.NextAction (Phase A §2.2 with the skeptic's M1 / M2).
//   CT-11 catalog/carriers.json (§10.2, skeptic MUST 2: schema stays 1): every row's numbers == its catalog row's.
//   CT-12 gamedata-mirror drift, Expected rows only: line, piece, station, lodge, school, patrol and sign prefabs vs
//         the mirror numbers (MUST 7: the sign row mirrors the shipped cost, so no allow-list is needed; 12,000 since the
//         S1 sign surgery, 2026-10-13, was 5000).
//   CT-13 every name's ", pending" token ⇔ !Expected; each row's Expected is derived from its names (MUST 5).
//   CT-14 BUILD-ITEMS ↔ NAMES ↔ lift types: base / top / line per type, `baseStation{Suffix}` ids, LineItemOf /
//         StationItemOf / TypeNameOf.
//   CT-15 arithmetic: EvenSpacingCount oracles (SubObjectSystem.cs:1743), the "never MORE than floor(loop / s) − 1"
//         property over 1,000 seeded random straight courses per Loop type (§3.1), ApproachPinCeilingM (D9) and the
//         HeightRange.min clearance bound (§3.3).
//   CT-16 the static sanity rules (SkiCatalog.CheckRows) on a hand-made twin: clean → 0 findings; with a duplicate item
//         id, a Loop type with SpacingM 0 and a station of an unknown type → exactly those 3 findings.
// Catalog Phase C (docs/research/art/assets-2026-10/code-round-3/round-3-contract.md §8.1, loopcache group, 2026-10-11):
//   CT-5  + AccelMps2 / DwellS oracles (tram 0.8 / 60, Loop types 0 / 0) and the spec → def copy.
//   CT-6  + DockLocalA / DockLocalB / HostsCarriers for the 8 stations (tram docks (∓5.50, 16.00, −5.50) on both, the base
//         hosts), the 9-argument constructor's NaN / false defaults, and dock y − (HangM − grip) = the hosted pivot 6.00.
//   CT-11 + carriers.json dwell_s == DwellS when present, required on a Shuttle row (tram 60).
//   CT-16 the twin gains a Shuttle row (the tram's shape): clean → 0; the faulty twin's Shuttle has AccelMps2 0 → exactly 4
//         findings; each Phase C rule alone (shuttle dwell < 0, no hosting base, a hosting top, a NaN dock; a Loop station
//         with a dock or HostsCarriers, a Loop type with a dwell) → exactly 1.
//
// RESOLVER-LIFETIME NOTE (a comment, not a check): the per-load + per-frame lifetime itself
// (SkiPrefabCatalogSystem.EnsureFresh: invalidate at a Game / Editor OnGamePreload, resolve at the first query,
// revalidate Exists && !Deleted once per frame) needs Game types (PrefabSystem, EntityManager) and is proven only by
// the smoke sheet's SECOND LOAD in one session (Phase A contract §7 step 7: two "PrefabCatalog [arm] load 1 / load 2"
// lines). CT-10 pins the decision table it runs on, including M2.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    public static class CatalogTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            ProblemsEmpty();        // CT-1
            NameList();             // CT-2
            StationsPerType();      // CT-3
            FrozenNames();          // CT-4
            LiftRows();             // CT-5
            StationRows();          // CT-6
            BuildItems();           // CT-7
            NamesBlockText();       // CT-8
            BuildItemsBlockText();  // CT-9
            NextActionTable();      // CT-10
            CarriersJson();         // CT-11
            MirrorDrift();          // CT-12
            PendingTokens();        // CT-13
            ItemsPerType();         // CT-14
            Arithmetic();           // CT-15
            SanityTwin();           // CT-16
        }

        private static bool Check(bool cond, string where, string msg)
        {
            return s_Check(cond, where, msg);
        }

        private static void Eq<T>(T expected, T actual, string where, string what)
        {
            Check(EqualityComparer<T>.Default.Equals(expected, actual), where, what + ": expected " + expected + ", got " + actual);
        }

        private static void Near(double expected, double actual, double tol, string where, string what)
        {
            Check(Math.Abs(expected - actual) <= tol, where, what + ": expected " + expected.ToString("R", CultureInfo.InvariantCulture) +
                  ", got " + actual.ToString("R", CultureInfo.InvariantCulture) + " (+/-" + tol.ToString(CultureInfo.InvariantCulture) + ")");
        }

        // ================================================================== the frozen oracle (CT-2, CT-4, CT-8, CT-13)

        private sealed class NameOracle
        {
            public string Name, Type, Item;
            public CatalogKind Kind;
            public bool Expected;

            public NameOracle(string name, string type, CatalogKind kind, string item, bool expected)
            {
                Name = name;
                Type = type;
                Kind = kind;
                Item = item;
                Expected = expected;
            }
        }

        private const string L = "PowerLinePrefab", O = "StaticObjectPrefab", B = "BuildingPrefab", S = "SurfacePrefab";

        /// <summary>LITERALS (round-2-contract §1.1 / §10.1), NAMES-block order. Save identity: never edit a Name. S1 import
        /// flips (2026-10-13): the 14 stations / lodges / school / patrols / tram cabin (build/s1-fix-codefix.md), then the three
        /// lift lines and the DQ / gondola carriers (build/s1-fix-codeflip.md): all 30 names are Expected.</summary>
        private static readonly NameOracle[] Frozen =
        {
            new NameOracle("SkiChairliftHV", L, CatalogKind.Line, "lift", true),
            new NameOracle("SkiLiftChair4", O, CatalogKind.Carrier, null, true),
            new NameOracle("SkiDetachableHV", L, CatalogKind.Line, "liftDetachable", true),
            new NameOracle("SkiLiftChairDQ4", O, CatalogKind.Carrier, null, true),
            new NameOracle("SkiGondolaHV", L, CatalogKind.Line, "liftGondola", true),
            new NameOracle("SkiGondolaCabin8", O, CatalogKind.Carrier, null, true),
            new NameOracle("SkiTramHV", L, CatalogKind.Line, "liftTram", true),
            new NameOracle("SkiTramCabin", O, CatalogKind.Carrier, null, true),
            new NameOracle("SkiLiftMarker", L, CatalogKind.Marker, null, true),
            new NameOracle("SkiBaseStationBuilding", B, CatalogKind.Station, "baseStation", true),
            new NameOracle("SkiTopStationBuilding", B, CatalogKind.Station, "topStation", true),
            new NameOracle("SkiDQBaseStationBuilding", B, CatalogKind.Station, "baseStationDetachable", true),
            new NameOracle("SkiDQTopStationBuilding", B, CatalogKind.Station, "topStationDetachable", true),
            new NameOracle("SkiGondolaBaseStationBuilding", B, CatalogKind.Station, "baseStationGondola", true),
            new NameOracle("SkiGondolaTopStationBuilding", B, CatalogKind.Station, "topStationGondola", true),
            new NameOracle("SkiTramBaseStationBuilding", B, CatalogKind.Station, "baseStationTram", true),
            new NameOracle("SkiTramTopStationBuilding", B, CatalogKind.Station, "topStationTram", true),
            new NameOracle("SkiAFrameDayLodge", B, CatalogKind.Lodge, "lodge", true),
            new NameOracle("SkiLodgeHut", B, CatalogKind.Lodge, "lodgeHut", true),
            new NameOracle("SkiLodgeMid", B, CatalogKind.Lodge, "lodgeMid", true),
            new NameOracle("SkiLodgeSummit", B, CatalogKind.Lodge, "lodgeSummit", true),
            new NameOracle("SkiLodgeGrand", B, CatalogKind.Lodge, "lodgeGrand", true),
            new NameOracle("SkiSchool", B, CatalogKind.School, "school", true),
            new NameOracle("SkiPatrolHut", B, CatalogKind.Patrol, "patrolHut", true),
            new NameOracle("SkiPatrolHQ", B, CatalogKind.Patrol, "patrolHQ", true),
            new NameOracle("SkiResortGate", B, CatalogKind.Sign, "sign", true),
            new NameOracle("SkiRunNaturalA", S, CatalogKind.Run, "runNatural", true),
            new NameOracle("SkiRunGroomedA", S, CatalogKind.Run, "runGroomed", true),
            new NameOracle("SkiRunSnowmakingA", S, CatalogKind.Run, "runSnowmaking", true),
            new NameOracle("SkiRunNightA", S, CatalogKind.Run, "runNight", true),
        };

        // ================================================================== CT-1

        private static void ProblemsEmpty()
        {
            const string W = "catalog-problems";
            IReadOnlyList<string> p = SkiCatalog.Problems;
            Check(p != null, W, "Problems is null");
            if (p == null)
                return;
            Check(p.Count == 0, W, p.Count + " problem(s)");
            for (int i = 0; i < p.Count; i++)
                Check(false, W, p[i]);
        }

        // ================================================================== CT-2

        private static void NameList()
        {
            const string W = "catalog-names";
            IReadOnlyList<CatalogName> all = SkiCatalog.AllPrefabNames();
            Eq(30, all.Count, W, "AllPrefabNames().Count");
            Check(ReferenceEquals(all, SkiCatalog.AllPrefabNames()), W, "AllPrefabNames() is not the cached list (allocates per call)");
            var names = new HashSet<string>(StringComparer.Ordinal);
            var items = new HashSet<string>(StringComparer.Ordinal);
            int withItem = 0, expected = 0;
            for (int i = 0; i < all.Count; i++)
            {
                CatalogName n = all[i];
                string at = "entry " + i + " '" + n.Name + "'";
                Check(!string.IsNullOrEmpty(n.Name) && names.Add(n.Name), W, at + ": empty or duplicate name");
                Check(n.Name != null && n.Name.StartsWith("Ski", StringComparison.Ordinal), W, at + ": does not start with \"Ski\"");
                string want;
                switch (n.Kind)
                {
                    case CatalogKind.Line:
                    case CatalogKind.Marker:
                        want = "PowerLinePrefab";
                        break;
                    case CatalogKind.Carrier:
                        want = "StaticObjectPrefab";
                        break;
                    case CatalogKind.Station:
                    case CatalogKind.Lodge:
                    case CatalogKind.Sign:
                    case CatalogKind.School:
                    case CatalogKind.Patrol:
                        want = "BuildingPrefab";
                        break;
                    case CatalogKind.Run:
                        want = "SurfacePrefab";
                        break;
                    default:
                        want = "?";
                        break;
                }
                Check(n.TypeName == want, W, at + ": kind " + n.Kind + " has type " + n.TypeName + ", want " + want);
                Check(n.TypeName == SkiCatalog.TypeLine || n.TypeName == SkiCatalog.TypeCarrier ||
                      n.TypeName == SkiCatalog.TypeBuilding || n.TypeName == SkiCatalog.TypeSurface, W,
                      at + ": type " + n.TypeName + " is not one of the four");
                if (n.BuildItemId != null)
                {
                    withItem++;
                    Check(items.Add(n.BuildItemId), W, at + ": duplicate build item id " + n.BuildItemId);
                }
                if (n.Expected)
                    expected++;
            }
            Eq(25, withItem, W, "names with a build item id (the 25 strip items)");
            Eq(30, expected, W, "expected names");
            Eq(0, all.Count - expected, W, "pending names");
            Eq(30, SkiCatalog.ExpectedNameCount, W, "ExpectedNameCount");
            Eq(0, SkiCatalog.PendingNameCount, W, "PendingNameCount");
            // The four type constants are the Game type names (the system asserts nameof() at OnCreate; literal here).
            Eq("PowerLinePrefab", SkiCatalog.TypeLine, W, "TypeLine");
            Eq("StaticObjectPrefab", SkiCatalog.TypeCarrier, W, "TypeCarrier");
            Eq("BuildingPrefab", SkiCatalog.TypeBuilding, W, "TypeBuilding");
            Eq("SurfacePrefab", SkiCatalog.TypeSurface, W, "TypeSurface");
            // Append-only enums.
            Eq((byte)7, (byte)CatalogKind.School, W, "CatalogKind.School (append-only)");
            Eq((byte)8, (byte)CatalogKind.Patrol, W, "CatalogKind.Patrol (append-only)");
            Eq((byte)6, (byte)CatalogKind.Run, W, "CatalogKind.Run (append-only)");
            Eq((byte)1, (byte)MoverKind.Shuttle, W, "MoverKind.Shuttle");
            Eq((byte)1, (byte)ServeRuleKind.TerminalsOnly, W, "ServeRuleKind.TerminalsOnly");
            Eq((byte)2, (byte)LodgeRoadRule.None, W, "LodgeRoadRule.None");
            Eq((byte)6, (byte)BuildItemKind.Run, W, "BuildItemKind.Run (append-only)");
        }

        // ================================================================== CT-3

        private static void StationsPerType()
        {
            const string W = "catalog-types";
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var carrierOwners = new Dictionary<string, int>(StringComparer.Ordinal);
            Eq(4, SkiCatalog.Lifts.Count, W, "Lifts.Count");
            for (int l = 0; l < SkiCatalog.Lifts.Count; l++)
            {
                LiftTypeDef d = SkiCatalog.Lifts[l];
                Check(!string.IsNullOrEmpty(d.Id) && ids.Add(d.Id), W, "lift type " + l + ": empty or duplicate Id '" + d.Id + "'");
                Eq(l, d.Index, W, "lift type '" + d.Id + "' Index (append-only)");
                Check(ReferenceEquals(d, SkiCatalog.LiftByIndex(l)), W, "LiftByIndex(" + l + ") is not row " + l);
                int bases = 0, tops = 0;
                for (int s = 0; s < SkiCatalog.Stations.Count; s++)
                {
                    StationDef st = SkiCatalog.Stations[s];
                    if (st.LiftTypeId != d.Id)
                        continue;
                    if (st.Role == StationRole.Base)
                        bases++;
                    else if (st.Role == StationRole.Top)
                        tops++;
                }
                Check(bases == 1 && tops == 1, W, "lift type '" + d.Id + "': " + bases + " Base and " + tops + " Top, want 1 / 1");
                StationDef sb = SkiCatalog.StationOf(l, StationRole.Base), stt = SkiCatalog.StationOf(l, StationRole.Top);
                Check(sb != null && sb.Role == StationRole.Base && sb.LiftTypeId == d.Id, W, "StationOf(" + l + ", Base) wrong");
                Check(stt != null && stt.Role == StationRole.Top && stt.LiftTypeId == d.Id, W, "StationOf(" + l + ", Top) wrong");
                Check(d.Carriers.Count == d.CarrierPrefabs.Count, W, "lift type '" + d.Id + "': Carriers / CarrierPrefabs differ in count");
                for (int c = 0; c < d.CarrierPrefabs.Count; c++)
                {
                    carrierOwners.TryGetValue(d.CarrierPrefabs[c], out int owners);
                    carrierOwners[d.CarrierPrefabs[c]] = owners + 1;
                    if (c < d.Carriers.Count)
                    {
                        CarrierDef cd = d.Carriers[c];
                        Check(cd.Prefab == d.CarrierPrefabs[c] && cd.LiftTypeId == d.Id && cd.LiftTypeIndex == l &&
                              cd.Seats == d.SeatsPerCarrier && cd.HangM == d.HangM && cd.GripTopAboveRopeM == d.GripTopAboveRopeM, W,
                              "lift type '" + d.Id + "' carrier " + c + " does not mirror its row");
                    }
                }
                Check(d.CarrierPrefabs.Count > 0, W, "lift type '" + d.Id + "' has no carrier");
            }
            foreach (KeyValuePair<string, int> kv in carrierOwners)
                Check(kv.Value == 1, W, "carrier '" + kv.Key + "' belongs to " + kv.Value + " lift types");
            for (int s = 0; s < SkiCatalog.Stations.Count; s++)
            {
                StationDef st = SkiCatalog.Stations[s];
                LiftTypeDef t = SkiCatalog.LiftById(st.LiftTypeId);
                Check(t != null, W, "station '" + st.Prefab + "' names unknown lift type '" + st.LiftTypeId + "'");
                Check(t != null && st.LiftTypeIndex == t.Index, W, "station '" + st.Prefab + "' LiftTypeIndex " + st.LiftTypeIndex);
            }
            Check(SkiCatalog.StationOf(-1, StationRole.Base) == null && SkiCatalog.StationOf(4, StationRole.Top) == null &&
                  SkiCatalog.StationOf(255, StationRole.Base) == null, W, "StationOf out of range answered");
            Check(SkiCatalog.LiftByIndex(-1) == null && SkiCatalog.LiftByIndex(4) == null && SkiCatalog.LiftByIndex(255) == null, W,
                  "LiftByIndex out of range answered (LiftCensus.NoType 255 must read null)");
            Eq((byte)0, (byte)StationRole.Base, W, "StationRole.Base (append-only enum)");
            Eq((byte)1, (byte)StationRole.Top, W, "StationRole.Top (append-only enum)");
        }

        // ================================================================== CT-4

        private static void FrozenNames()
        {
            const string W = "catalog-frozen";
            IReadOnlyList<CatalogName> all = SkiCatalog.AllPrefabNames();
            Eq(Frozen.Length, all.Count, W, "names (frozen oracle length)");
            for (int i = 0; i < Frozen.Length && i < all.Count; i++)
            {
                NameOracle f = Frozen[i];
                CatalogName n = all[i];
                string at = "entry " + i + " '" + f.Name + "'";
                Eq(f.Name, n.Name, W, at + " name (NAMES-block order, save identity)");
                Eq(f.Type, n.TypeName, W, at + " type");
                Eq(f.Kind, n.Kind, W, at + " kind");
                Eq(f.Item, n.BuildItemId, W, at + " item");
                Eq(f.Expected, n.Expected, W, at + " expected");
                Check(SkiCatalog.TryGetName(f.Name, out CatalogName byName) && byName.Name == f.Name && byName.Expected == f.Expected, W,
                      at + ": TryGetName");
                Eq(f.Expected, SkiCatalog.IsExpected(f.Name), W, at + ": IsExpected");
            }

            // Lines and carriers → their type (the census tells the stub from a line by ByLinePrefab).
            LineIs("SkiChairliftHV", "fixedGrip", W);
            LineIs("SkiDetachableHV", "detachable", W);
            LineIs("SkiGondolaHV", "gondola", W);
            LineIs("SkiTramHV", "tram", W);
            CarrierIs("SkiLiftChair4", "fixedGrip", W);
            CarrierIs("SkiLiftChairDQ4", "detachable", W);
            CarrierIs("SkiGondolaCabin8", "gondola", W);
            CarrierIs("SkiTramCabin", "tram", W);
            LiftTypeDef fg = SkiCatalog.ByLinePrefab("SkiChairliftHV");
            Check(ReferenceEquals(fg, SkiCatalog.ByCarrierPrefab("SkiLiftChair4")) && ReferenceEquals(fg, SkiCatalog.LiftById("fixedGrip")) &&
                  ReferenceEquals(fg, SkiCatalog.Lifts[0]), W, "line, carrier, Id and index lookups do not answer the same FG row");
            StationIs("SkiBaseStationBuilding", StationRole.Base, "fixedGrip", W);
            StationIs("SkiTopStationBuilding", StationRole.Top, "fixedGrip", W);
            StationIs("SkiDQBaseStationBuilding", StationRole.Base, "detachable", W);
            StationIs("SkiDQTopStationBuilding", StationRole.Top, "detachable", W);
            StationIs("SkiGondolaBaseStationBuilding", StationRole.Base, "gondola", W);
            StationIs("SkiGondolaTopStationBuilding", StationRole.Top, "gondola", W);
            StationIs("SkiTramBaseStationBuilding", StationRole.Base, "tram", W);
            StationIs("SkiTramTopStationBuilding", StationRole.Top, "tram", W);
            LodgeIs("SkiAFrameDayLodge", "aFrame", 0, W);
            LodgeIs("SkiLodgeHut", "hut", 1, W);
            LodgeIs("SkiLodgeMid", "mid", 2, W);
            LodgeIs("SkiLodgeSummit", "summit", 3, W);
            LodgeIs("SkiLodgeGrand", "grand", 4, W);
            Check(SkiCatalog.SchoolByPrefab("SkiSchool") != null, W, "SkiSchool is not the school");
            Check(SkiCatalog.PatrolByPrefab("SkiPatrolHut") != null && SkiCatalog.PatrolByPrefab("SkiPatrolHQ") != null, W,
                  "SkiPatrolHut / SkiPatrolHQ are not patrol rows");
            Check(SkiCatalog.IsSign("SkiResortGate"), W, "SkiResortGate is not the sign");
            Eq("SkiLiftMarker", SkiCatalog.MarkerPrefab, W, "MarkerPrefab");
            Eq("SkiChairliftHV", SkiCatalog.Names.LineFixedGrip, W, "Names.LineFixedGrip");
            Eq("SkiTramHV", SkiCatalog.Names.LineTram, W, "Names.LineTram");
            RunIs("SkiRunNaturalA", RunKind.Natural, 3, W);
            RunIs("SkiRunGroomedA", RunKind.Groomed, 0, W);
            RunIs("SkiRunSnowmakingA", RunKind.Snowmaking, 1, W);
            RunIs("SkiRunNightA", RunKind.Night, 2, W);
            foreach (string up in new[] { "SkiAFrameDayLodge", "SkiLodgeHut", "SkiLodgeMid", "SkiLodgeSummit", "SkiLodgeGrand",
                                          "SkiSchool", "SkiPatrolHut", "SkiPatrolHQ" })
                Check(SkiCatalog.IsUpgradeBuilding(up), W, up + " is not an upgrade building");
            foreach (string notUp in new[] { "SkiResortGate", "SkiBaseStationBuilding", "SkiChairliftHV", "SkiRunGroomedA", null, "" })
                Check(!SkiCatalog.IsUpgradeBuilding(notUp), W, (notUp ?? "null") + " reads as an upgrade building");

            // Negatives (the marker is NOT a line; kinds never cross).
            Check(SkiCatalog.ByLinePrefab("SkiLiftMarker") == null, W, "the marker reads as a lift line");
            Check(SkiCatalog.ByLinePrefab(null) == null && SkiCatalog.ByLinePrefab("") == null, W, "null / empty is a line");
            Check(SkiCatalog.ByLinePrefab("SkiTramHVProto") == null, W, "the R16 prototype name reads as a catalog line (MUST 5)");
            Check(SkiCatalog.ByCarrierPrefab(null) == null && SkiCatalog.ByCarrierPrefab("SkiChairliftHV") == null, W,
                  "null or the line is a carrier");
            Check(SkiCatalog.StationByPrefab("SkiAFrameDayLodge") == null && SkiCatalog.StationByPrefab(null) == null, W,
                  "the lodge or null is a station");
            Check(!SkiCatalog.IsLodge("SkiResortGate") && !SkiCatalog.IsLodge("SkiSchool") && !SkiCatalog.IsLodge(null) &&
                  !SkiCatalog.IsLodge(""), W, "the sign, the school, null or empty is a lodge");
            Check(SkiCatalog.SchoolByPrefab("SkiPatrolHut") == null && SkiCatalog.PatrolByPrefab("SkiSchool") == null &&
                  SkiCatalog.SchoolByPrefab(null) == null && SkiCatalog.PatrolByPrefab("") == null, W, "school / patrol lookups cross");
            Check(!SkiCatalog.IsSign("SkiAFrameDayLodge") && !SkiCatalog.IsSign(null), W, "the lodge or null is the sign");
            Check(SkiCatalog.RunByPrefab("SkiChairliftHV") == null && SkiCatalog.RunByPrefab(null) == null, W,
                  "the line or null is a run");
            Check(SkiCatalog.LiftById(null) == null && SkiCatalog.LiftById("quad") == null, W, "null / unknown Id resolves");
            Check(SkiCatalog.ByLinePrefab("skichairlifthv") == null, W, "lookups are not Ordinal (case-folded hit)");
            Check(!SkiCatalog.TryGetName("SkiNope", out CatalogName none) && none.Name == null && !SkiCatalog.IsExpected("SkiNope") &&
                  !SkiCatalog.IsExpected(null), W, "an unknown name answered");
        }

        private static void LineIs(string name, string id, string w)
        {
            LiftTypeDef d = SkiCatalog.ByLinePrefab(name);
            Check(d != null && d.Id == id && d.LinePrefab == name, w, "ByLinePrefab(\"" + name + "\") is not " + id);
        }

        private static void CarrierIs(string name, string id, string w)
        {
            LiftTypeDef d = SkiCatalog.ByCarrierPrefab(name);
            Check(d != null && d.Id == id, w, "ByCarrierPrefab(\"" + name + "\") is not " + id);
        }

        private static void StationIs(string name, StationRole role, string id, string w)
        {
            StationDef s = SkiCatalog.StationByPrefab(name);
            Check(s != null && s.Role == role && s.LiftTypeId == id, w, name + " is not the " + role + " of " + id);
        }

        private static void LodgeIs(string name, string id, int index, string w)
        {
            LodgeDef d = SkiCatalog.LodgeByPrefab(name);
            Check(d != null && d.Id == id && d.Index == index && SkiCatalog.IsLodge(name) && ReferenceEquals(d, SkiCatalog.Lodges[index]), w,
                  name + " is not lodge row " + index + " '" + id + "'");
        }

        private static void RunIs(string name, RunKind kind, int value, string w)
        {
            RunDef r = SkiCatalog.RunByPrefab(name);
            Check(r != null && r.Kind == kind && (int)r.Kind == value, w, name + ": kind " + (r != null ? r.Kind.ToString() : "null") +
                  ", want " + kind + " (" + value + ")");
        }

        // ================================================================== CT-5

        private sealed class LiftOracle
        {
            public string Id, Line, Piece, Item, Carrier;
            public MoverKind Mover;
            public int Seats;
            public float Speed, Gauge, Hang, Grip, Axle, Aim, Spacing, Design, Radius, Margin, Fixed, PerKm;
            public ServeRuleKind Rule;
            public int Build8, Elev10, StBase, StTop, LinePerKm;
            public double Ceiling, Value, Summer;
            public float MaxPylon, Slope, NetH, LaneH, HMin, ElevMax;
            public bool Expected;
            public float Accel, Dwell;   // Catalog Phase C (round-3-contract §1.2): 0 / 0 on Loop types
        }

        /// <summary>round-2-contract §1.3, literal (the table's numbers, not the catalog's). Expected: every row true since the S1
        /// lift flip (2026-10-13, build/s1-fix-codeflip.md; the three new lines and their carriers exist game-side).</summary>
        private static readonly LiftOracle[] LiftOracles =
        {
            new LiftOracle { Id = "fixedGrip", Line = "SkiChairliftHV", Piece = "SkiChairliftPiece", Item = "lift", Carrier = "SkiLiftChair4",
                Mover = MoverKind.Loop, Seats = 3, Speed = 2.5f, Gauge = 1.8f, Hang = 2.4f, Grip = 0.08f, Axle = 7.04f, Aim = 8.0f,
                Spacing = 8f, Design = 15f, Rule = ServeRuleKind.AlongLine, Radius = 150f, Margin = 15f, Fixed = 2500f, PerKm = 1000f,
                Build8 = 60, Elev10 = 5, StBase = 60000, StTop = 30000, LinePerKm = 7500, Ceiling = 4.0, Value = 6, Summer = 0.25,
                MaxPylon = 60f, Slope = 0.8f, NetH = 0.03f, LaneH = 0.03f, HMin = 6f, ElevMax = 16f, Expected = true },
                // ElevMax 10 → 16 (S1 2026-10-13, DECISIONS 173 "Yes to both", CT-12 pattern: SkiCatalog FixedGrip Envelope and
                // SkiChairliftHV.Prefab:19 m_ElevationRange.max 16 landed together; the only departure from the §1.3 table).
            new LiftOracle { Id = "detachable", Line = "SkiDetachableHV", Piece = "SkiDetachablePiece", Item = "liftDetachable", Carrier = "SkiLiftChairDQ4",
                Mover = MoverKind.Loop, Seats = 4, Speed = 5.0f, Gauge = 2.6f, Hang = 3.3f, Grip = 0.22f, Axle = 8.0f, Aim = 9.0f,
                Spacing = 22.5f, Design = 30f, Rule = ServeRuleKind.AlongLine, Radius = 150f, Margin = 15f, Fixed = 5000f, PerKm = 2000f,
                Build8 = 120, Elev10 = 10, StBase = 125000, StTop = 65000, LinePerKm = 15000, Ceiling = 4.8, Value = 15, Summer = 0.25,
                MaxPylon = 90f, Slope = 0.9f, NetH = 0f, LaneH = 0.004f, HMin = 5.8f, ElevMax = 10f, Expected = true },
            new LiftOracle { Id = "gondola", Line = "SkiGondolaHV", Piece = "SkiGondolaPiece", Item = "liftGondola", Carrier = "SkiGondolaCabin8",
                Mover = MoverKind.Loop, Seats = 8, Speed = 5.5f, Gauge = 3.0f, Hang = 4.5f, Grip = 0.08f, Axle = 10.0f, Aim = 11.0f,
                Spacing = 45f, Design = 60f, Rule = ServeRuleKind.AlongLine, Radius = 150f, Margin = 15f, Fixed = 8000f, PerKm = 3000f,
                Build8 = 175, Elev10 = 15, StBase = 190000, StTop = 110000, LinePerKm = 21875, Ceiling = 6.0, Value = 25, Summer = 0.75,
                MaxPylon = 250f, Slope = 1.2f, NetH = 0f, LaneH = 0.004f, HMin = 6f, ElevMax = 28f, Expected = true },
            new LiftOracle { Id = "tram", Line = "SkiTramHV", Piece = "SkiTramPiece", Item = "liftTram", Carrier = "SkiTramCabin",
                Mover = MoverKind.Shuttle, Seats = 100, Speed = 10.0f, Gauge = 5.5f, Hang = 10.5f, Grip = 0.5f, Axle = 26.25f, Aim = 26.25f,
                Spacing = 0f, Design = 0f, Rule = ServeRuleKind.TerminalsOnly, Radius = 300f, Margin = 15f, Fixed = 11000f, PerKm = 4500f,
                Build8 = 270, Elev10 = 25, StBase = 300000, StTop = 190000, LinePerKm = 33750, Ceiling = 12.0, Value = 30, Summer = 1.0,
                MaxPylon = 1000f, Slope = 2.5f, NetH = 0f, LaneH = 0.004f, HMin = 13.75f, ElevMax = 30f, Expected = true,
                Accel = 0.8f, Dwell = 60f },   // Catalog Phase C (round-3-contract §1.2)
        };

        private static void LiftRows()
        {
            const string W = "catalog-lift-rows";
            Eq(LiftOracles.Length, SkiCatalog.Lifts.Count, W, "Lifts.Count");
            for (int i = 0; i < LiftOracles.Length && i < SkiCatalog.Lifts.Count; i++)
            {
                LiftOracle o = LiftOracles[i];
                LiftTypeDef r = SkiCatalog.Lifts[i];
                string at = "row " + i + " '" + o.Id + "' ";
                Eq(o.Id, r.Id, W, at + "Id");
                Eq(o.Line, r.LinePrefab, W, at + "LinePrefab");
                Eq(o.Piece, r.PiecePrefab, W, at + "PiecePrefab");
                Eq(o.Item, r.BuildItemId, W, at + "BuildItemId (the LINE item)");
                Check(r.CarrierPrefabs.Count == 1 && r.CarrierPrefabs[0] == o.Carrier, W, at + "CarrierPrefabs != { \"" + o.Carrier + "\" }");
                Check(!(r.CarrierPrefabs is string[]), W, at + "CarrierPrefabs is a writable array");
                Eq(o.Mover, r.Mover, W, at + "Mover");
                Eq(o.Seats, r.SeatsPerCarrier, W, at + "SeatsPerCarrier");
                Eq(o.Speed, r.SpeedMps, W, at + "SpeedMps");
                Eq(o.Gauge, r.GaugeHalfM, W, at + "GaugeHalfM");
                Eq(o.Hang, r.HangM, W, at + "HangM");
                Eq(o.Grip, r.GripTopAboveRopeM, W, at + "GripTopAboveRopeM");
                Eq(o.Axle, r.AxleM, W, at + "AxleM");
                Eq(o.Aim, r.AimHeightM, W, at + "AimHeightM");
                Eq(o.Spacing, r.SpacingM, W, at + "SpacingM");
                Eq(o.Design, r.DesignSpacingM, W, at + "DesignSpacingM");
                Eq(o.Rule, r.ServeRule, W, at + "ServeRule");
                Eq(o.Radius, r.ServeRadiusM, W, at + "ServeRadiusM");
                Eq(o.Margin, r.TopMarginM, W, at + "TopMarginM");
                Eq(o.Fixed, r.FixedPerMonth, W, at + "FixedPerMonth");
                Eq(o.PerKm, r.PerKmPerMonth, W, at + "PerKmPerMonth");
                Eq(o.Build8, r.LineBuildPer8m, W, at + "LineBuildPer8m");
                Eq(o.Elev10, r.ElevationCostPer10m, W, at + "ElevationCostPer10m");
                Eq(o.StBase, r.StationCostBase, W, at + "StationCostBase");
                Eq(o.StTop, r.StationCostTop, W, at + "StationCostTop");
                Eq(o.LinePerKm, r.LinePerKm, W, at + "LinePerKm (computed)");
                Eq(r.LineBuildPer8m * 125, r.LinePerKm, W, at + "LinePerKm == LineBuildPer8m × 125");
                Near(o.Ceiling, r.ApproachPinCeilingM, 1e-5, W, at + "ApproachPinCeilingM (computed)");
                Eq(o.Value, r.ScoreValue, W, at + "ScoreValue");
                Eq(o.Summer, r.SummerShare, W, at + "SummerShare");
                Eq(o.MaxPylon, r.Envelope.MaxPylonM, W, at + "Envelope.MaxPylonM");
                Eq(o.Slope, r.Envelope.SlopeMax, W, at + "Envelope.SlopeMax");
                Eq(o.NetH, r.Envelope.NetHanging, W, at + "Envelope.NetHanging");
                Eq(o.LaneH, r.Envelope.LaneHanging, W, at + "Envelope.LaneHanging");
                Eq(o.HMin, r.Envelope.HeightRangeMinM, W, at + "Envelope.HeightRangeMinM");
                Eq(o.ElevMax, r.Envelope.ElevationMaxM, W, at + "Envelope.ElevationMaxM");
                Eq(true, r.CloseElectricityView, W, at + "CloseElectricityView");
                Eq(o.Expected, r.Expected, W, at + "Expected (derived)");
                Eq(o.Expected, r.LineExpected, W, at + "LineExpected");
                Eq(o.Accel, r.AccelMps2, W, at + "AccelMps2 (Catalog Phase C)");
                Eq(o.Dwell, r.DwellS, W, at + "DwellS (Catalog Phase C)");
            }

            // Row 0 == the FixedGrip consts (the aliases' source, ResortBatch CG-1 checks the aliases).
            LiftTypeDef f = SkiCatalog.Lifts[0];
            Eq(SkiCatalog.FixedGrip.Id, f.Id, W, "FG Id == const");
            Eq(SkiCatalog.FixedGrip.SeatsPerCarrier, f.SeatsPerCarrier, W, "FG seats == const");
            Eq(SkiCatalog.FixedGrip.SpeedMps, f.SpeedMps, W, "FG speed == const");
            Eq(SkiCatalog.FixedGrip.AimHeightM, f.AimHeightM, W, "FG aim == const");
            Eq(SkiCatalog.FixedGrip.ServeRadiusM, f.ServeRadiusM, W, "FG serve radius == const");
            Eq(SkiCatalog.FixedGrip.TopMarginM, f.TopMarginM, W, "FG top margin == const");
            Eq(SkiCatalog.FixedGrip.FixedPerMonth, f.FixedPerMonth, W, "FG fixed == const");
            Eq(SkiCatalog.FixedGrip.PerKmPerMonth, f.PerKmPerMonth, W, "FG per km == const");
            Eq(SkiCatalog.FixedGrip.ScoreValue, f.ScoreValue, W, "FG value == const");
            Eq(SkiCatalog.FixedGrip.BuildItemId, f.BuildItemId, W, "FG item == const");
            Eq(SkiCatalog.FixedGrip.CloseElectricityView, f.CloseElectricityView, W, "FG Electricity view == const");
            Eq(SkiCatalog.FixedGrip.LinePerKm, f.LinePerKm, W, "FG LinePerKm == const (the D24 hand copy)");
            Eq(SkiCatalog.FixedGrip.LineBuildPer8m * SkiCatalog.PiecesPerKm, SkiCatalog.FixedGrip.LinePerKm, W,
               "FixedGrip.LinePerKm == LineBuildPer8m × PiecesPerKm");
            Eq(SkiCatalog.FixedGrip.ApproachPinCeilingM, f.ApproachPinCeilingM, W, "FG ceiling == const (max(4, 3.9) = 4 exactly)");
            Eq(SkiCatalog.FixedGrip.SpacingM, f.SpacingM, W, "FG spacing == const");
            Eq(SkiCatalog.FixedGrip.DesignSpacingM, f.DesignSpacingM, W, "FG design spacing == const");
            Eq(SkiCatalog.FixedGrip.HangM, f.HangM, W, "FG hang == const");
            Eq(SkiCatalog.FixedGrip.GripTopAboveRopeM, f.GripTopAboveRopeM, W, "FG grip == const");
            Eq(SkiCatalog.FixedGrip.GaugeHalfM, f.GaugeHalfM, W, "FG gauge == const");
            Eq(SkiCatalog.FixedGrip.AxleM, f.AxleM, W, "FG axle == const");
            Eq(SkiCatalog.FixedGrip.SummerShare, f.SummerShare, W, "FG λ == const");
            Eq(SkiCatalog.FixedGrip.LineBuildPer8m, f.LineBuildPer8m, W, "FG line per 8 m == const");
            Eq(360.0, SkiCatalog.AFrameLodge.SkiersPerDay, W, "AFrameLodge.SkiersPerDay");
            Eq(24.0, SkiCatalog.Ladder.ExtraCap, W, "Ladder.ExtraCap (R11)");
            // Ladder.LuxuryPerArea (D3, one luxury lodge per area) was removed 2026-10-11: no limit on lodges (Richard); ExtraCap above is the only limiter.
            Eq(300f, SkiCatalog.Ladder.SummitServedRadiusM, W, "Ladder.SummitServedRadiusM (R11 / D11)");
            Eq(12.0, SkiCatalog.School.ScoreValue, W, "School.ScoreValue (R13)");
            Eq(1.2, SkiCatalog.School.DemandFactor, W, "School.DemandFactor (R13)");
            Eq(1000f, SkiCatalog.Patrol.RadiusM, W, "Patrol.RadiusM (R13)");
            Eq(1.15, SkiCatalog.Patrol.ExpertFactor, W, "Patrol.ExpertFactor (R13)");
            Eq(0.70, SkiCatalog.Patrol.BackcountryServedMin, W, "Patrol.BackcountryServedMin (R13)");
            Eq(15, SkiCatalog.LodgeSpend.Default, W, "LodgeSpend.Default (econ-2 round 2: 15; was R11 off)");
            Eq(50, SkiCatalog.LodgeSpend.Max, W, "LodgeSpend.Max");
            Eq(5, SkiCatalog.LodgeSpend.Step, W, "LodgeSpend.Step");
            Eq(4.0f, SkiCatalog.ApproachPinCeilingFloorM, W, "ApproachPinCeilingFloorM (D9)");

            // The constructor copies the spec's arrays: a later write to them cannot change the row.
            string[] src = { "A", "B" };
            bool[] flags = { true, false };
            var spec = new LiftTypeSpec { Id = "t", LinePrefab = "L", CarrierPrefabs = src, CarrierExpected = flags, LineExpected = true,
                                          HangM = 3.3f };
            var copy = new LiftTypeDef(0, spec);
            src[0] = "Z";
            flags[1] = true;
            Check(copy.CarrierPrefabs.Count == 2 && copy.CarrierPrefabs[0] == "A" && copy.Carriers[0].Prefab == "A", W,
                  "LiftTypeDef shares the spec's carrier array");
            Check(copy.Carriers[0].Expected && !copy.Carriers[1].Expected && copy.LineExpected && !copy.Expected, W,
                  "per-name flags: line expected, carrier B pending → row Expected false (MUST 5)");
            Near(4.8, copy.ApproachPinCeilingM, 1e-5, W, "spec HangM 3.3 → ceiling 4.8");
            var profiled = new LiftTypeDef(0, new LiftTypeSpec { Id = "s", LinePrefab = "L", AccelMps2 = 0.5f, DwellS = 30f });
            Check(profiled.AccelMps2 == 0.5f && profiled.DwellS == 30f, W, "LiftTypeDef does not copy AccelMps2 / DwellS (Catalog Phase C)");
            var none = new LiftTypeDef(0, new LiftTypeSpec { Id = "t", LinePrefab = "L", LineExpected = true });
            Check(none.CarrierPrefabs != null && none.CarrierPrefabs.Count == 0 && none.Carriers.Count == 0 && none.Expected, W,
                  "a null carrier array is not empty (or the row is not expected)");

            LodgeRows();
            BuildingRows();
        }

        private static void LodgeRows()
        {
            const string W = "catalog-lodge-rows";
            // round-2-contract §1.5 (ROUND-SPEC §5, R11, R21): Idx, Id, Prefab, item, B, E, μ, μs, capacity, tolerance, jobs,
            // build, upkeep, ForbidMultiple, luxury, spend ×, road, expected. ForbidMultiple is false on EVERY lodge tier and
            // the "exclusive group" column became the plain `Luxury` label (a tip / log tag): Richard, 2026-10-11, no limit
            // on how many lodges a player builds (summit and grand were ForbidMultiple true, exclusive group 1: D3).
            // S1 import flip (2026-10-13, §10.1): every tier is imported, so every row is Expected.
            object[][] rows =
            {
                new object[] { "aFrame", "SkiAFrameDayLodge", "lodge", 20.0, 4.0, 0.5, 0.5, 360, 0, 6, 40000, 3000, false, false, 1.0f, LodgeRoadRule.Base, true },
                new object[] { "hut", "SkiLodgeHut", "lodgeHut", 16.0, 4.0, 0.5, 0.75, 200, 0, 4, 50000, 3000, false, false, 1.0f, LodgeRoadRule.Mountain, true },
                new object[] { "mid", "SkiLodgeMid", "lodgeMid", 30.0, 8.0, 0.5, 0.5, 720, 20, 12, 200000, 8000, false, false, 1.5f, LodgeRoadRule.Base, true },
                new object[] { "summit", "SkiLodgeSummit", "lodgeSummit", 40.0, 12.0, 0.5, 1.0, 480, 40, 8, 1500000, 18000, false, true, 3.0f, LodgeRoadRule.Mountain, true },
                new object[] { "grand", "SkiLodgeGrand", "lodgeGrand", 40.0, 12.0, 0.6, 0.6, 1080, 40, 18, 1200000, 20000, false, true, 3.0f, LodgeRoadRule.Base, true },
            };
            Eq(rows.Length, SkiCatalog.Lodges.Count, W, "Lodges.Count");
            for (int i = 0; i < rows.Length && i < SkiCatalog.Lodges.Count; i++)
            {
                object[] o = rows[i];
                LodgeDef d = SkiCatalog.Lodges[i];
                string at = "lodge " + i + " '" + o[0] + "' ";
                Eq(i, d.Index, W, at + "Index");
                Eq((string)o[0], d.Id, W, at + "Id");
                Eq((string)o[1], d.Prefab, W, at + "Prefab");
                Eq((string)o[2], d.BuildItemId, W, at + "BuildItemId");
                Eq((double)o[3], d.B, W, at + "B");
                Eq((double)o[4], d.E, W, at + "E");
                Eq((double)o[5], d.Mu, W, at + "Mu");
                Eq((double)o[6], d.MuSummit, W, at + "MuSummit");
                Eq((int)o[7], d.Capacity, W, at + "Capacity");
                Eq((double)(int)o[7], d.SkiersPerDay, W, at + "SkiersPerDay (Phase A name) == Capacity");
                Eq((int)o[8], d.PriceTolerance, W, at + "PriceTolerance");
                Eq((int)o[9], d.Jobs, W, at + "Jobs (complexity 1, R21)");
                Eq((int)o[10], d.BuildCost, W, at + "BuildCost");
                Eq((int)o[11], d.Upkeep, W, at + "Upkeep");
                Eq((bool)o[12], d.ForbidMultiple, W, at + "ForbidMultiple");
                Eq((bool)o[13], d.Luxury, W, at + "Luxury (label only)");
                Check(!d.ForbidMultiple, W, at + "no lodge tier forbids a second one on the sign (no count limit, 2026-10-11)");
                Eq((float)o[14], d.SpendMultiplier, W, at + "SpendMultiplier");
                Eq((LodgeRoadRule)o[15], d.RoadRule, W, at + "RoadRule");
                Eq((bool)o[16], d.Expected, W, at + "Expected");
            }
        }

        private static void BuildingRows()
        {
            const string W = "catalog-building-rows";
            Eq(1, SkiCatalog.Schools.Count, W, "Schools.Count");
            if (SkiCatalog.Schools.Count > 0)
            {
                SchoolDef s = SkiCatalog.Schools[0];
                Eq("SkiSchool", s.Prefab, W, "school Prefab");
                Eq("school", s.BuildItemId, W, "school item");
                Eq(12.0, s.ScoreValue, W, "school ScoreValue");
                Eq(1.2, s.DemandFactor, W, "school DemandFactor");
                Eq(6, s.Jobs, W, "school Jobs (instructors; econ-2 round 2, was 10)");
                Eq(80000, s.BuildCost, W, "school BuildCost");
                Eq(4000, s.Upkeep, W, "school Upkeep (econ-2 round 2, was 6000)");
                Eq(true, s.ForbidMultiple, W, "school ForbidMultiple");
                Eq(LodgeRoadRule.Base, s.RoadRule, W, "school RoadRule (R9)");
                Eq(true, s.Expected, W, "school Expected (S1 import flip, 2026-10-13)");
            }
            Eq(2, SkiCatalog.Patrols.Count, W, "Patrols.Count");
            string[] patrolNames = { "SkiPatrolHut", "SkiPatrolHQ" }, patrolItems = { "patrolHut", "patrolHQ" };
            for (int i = 0; i < 2 && i < SkiCatalog.Patrols.Count; i++)
            {
                PatrolDef p = SkiCatalog.Patrols[i];
                string at = "patrol " + i + " ";
                Eq(patrolNames[i], p.Prefab, W, at + "Prefab");
                Eq(patrolItems[i], p.BuildItemId, W, at + "item");
                Eq(1000f, p.RadiusM, W, at + "RadiusM");
                Eq(1.15, p.ExpertFactor, W, at + "ExpertFactor");
                Eq(0.70, p.BackcountryServedMin, W, at + "BackcountryServedMin");
                Eq(2, p.Jobs, W, at + "Jobs");
                Eq(15000, p.BuildCost, W, at + "BuildCost");
                Eq(1500, p.Upkeep, W, at + "Upkeep");
                Eq(false, p.ForbidMultiple, W, at + "ForbidMultiple");
                Eq(LodgeRoadRule.None, p.RoadRule, W, at + "RoadRule (roadless, R9)");
                Eq(true, p.Expected, W, at + "Expected (S1 import flip, 2026-10-13)");
            }
            Eq(1, SkiCatalog.Signs.Count, W, "Signs.Count");
            if (SkiCatalog.Signs.Count > 0)
            {
                SignDef g = SkiCatalog.Signs[0];
                Eq("SkiResortGate", g.Prefab, W, "sign Prefab");
                Eq("sign", g.BuildItemId, W, "sign item");
                // MUST 7: the shipped cost (SkiResortGate.Prefab:25). The S1 sign surgery (2026-10-13, STATE step 22) flipped
                // the prefab and SkiCatalog's SignDef from 5000 to R14's 12,000 together (CT-12 pattern).
                Eq(12000, g.BuildCost, W, "sign BuildCost (R14's 12,000 since the S1 sign surgery, MUST 7)");
                Eq(true, g.Expected, W, "sign Expected");
            }
        }

        // ================================================================== CT-6

        private static void StationRows()
        {
            const string W = "catalog-stations";
            // prefab, role, type, wheel (x, y, z) or NaN, stub z, cost, item, expected (round-2-contract §1.4; every station
            // Expected since the S1 import flip, 2026-10-13, §10.1).
            object[][] rows =
            {
                new object[] { "SkiBaseStationBuilding", StationRole.Base, "fixedGrip", 0f, 3.55f, 0.2f, -12f, 60000, "baseStation", true },
                new object[] { "SkiTopStationBuilding", StationRole.Top, "fixedGrip", 0f, 3.75f, 0f, -12f, 30000, "topStation", true },
                new object[] { "SkiDQBaseStationBuilding", StationRole.Base, "detachable", 0f, 3.70f, 9.60f, -16f, 125000, "baseStationDetachable", true },
                new object[] { "SkiDQTopStationBuilding", StationRole.Top, "detachable", 0f, 3.70f, 5.60f, -16f, 65000, "topStationDetachable", true },
                new object[] { "SkiGondolaBaseStationBuilding", StationRole.Base, "gondola", 0f, 4.85f, 9.40f, -16f, 190000, "baseStationGondola", true },
                new object[] { "SkiGondolaTopStationBuilding", StationRole.Top, "gondola", 0f, 4.85f, 5.40f, -16f, 110000, "topStationGondola", true },
                new object[] { "SkiTramBaseStationBuilding", StationRole.Base, "tram", float.NaN, float.NaN, float.NaN, -12f, 300000, "baseStationTram", true },
                new object[] { "SkiTramTopStationBuilding", StationRole.Top, "tram", float.NaN, float.NaN, float.NaN, -12f, 190000, "topStationTram", true },
            };
            Eq(rows.Length, SkiCatalog.Stations.Count, W, "Stations.Count");
            for (int i = 0; i < rows.Length && i < SkiCatalog.Stations.Count; i++)
            {
                object[] o = rows[i];
                StationDef s = SkiCatalog.Stations[i];
                string at = "station " + i + " '" + o[0] + "' ";
                Eq((string)o[0], s.Prefab, W, at + "Prefab");
                Eq((StationRole)o[1], s.Role, W, at + "Role");
                Eq((string)o[2], s.LiftTypeId, W, at + "LiftTypeId");
                float x = (float)o[3], y = (float)o[4], z = (float)o[5];
                if (float.IsNaN(x))
                    Check(float.IsNaN(s.WheelLocal.x) && float.IsNaN(s.WheelLocal.y) && float.IsNaN(s.WheelLocal.z), W,
                          at + "wheel " + s.WheelLocal + ", want NaN (Shuttle: no bullwheel arc)");
                else
                    Check(s.WheelLocal.x == x && s.WheelLocal.y == y && s.WheelLocal.z == z && math.all(math.isfinite(s.WheelLocal)), W,
                          at + "wheel " + s.WheelLocal + ", want (" + x + ", " + y + ", " + z + ")");
                Eq((float)o[6], s.StubTipLocalZ, W, at + "StubTipLocalZ");
                Eq((int)o[7], s.BuildCost, W, at + "BuildCost");
                LiftTypeDef t = SkiCatalog.LiftById(s.LiftTypeId);
                if (t != null)
                    Eq(s.Role == StationRole.Base ? t.StationCostBase : t.StationCostTop, s.BuildCost, W, at + "BuildCost == its type's StationCost");
                Eq((string)o[8], s.BuildItemId, W, at + "BuildItemId");
                Eq((bool)o[9], s.Expected, W, at + "Expected");
            }
            Eq(-12f, SkiCatalog.StubTipLocalZ, W, "SkiCatalog.StubTipLocalZ (the fixed-grip value, kept)");
            Eq(SkiCatalog.StubTipLocalZ, StationAlign.StubTipLocalZ, W, "StationAlign.StubTipLocalZ == SkiCatalog.StubTipLocalZ");
            Eq(SkiCatalog.StubTipLocalZ, SkiCatalog.StationOf(0, StationRole.Base).StubTipLocalZ, W, "FG base stub == SkiCatalog.StubTipLocalZ");
            Eq(SkiCatalog.StubTipLocalZ, SkiCatalog.StationOf(0, StationRole.Top).StubTipLocalZ, W, "FG top stub == SkiCatalog.StubTipLocalZ");
            StationDocks();
        }

        private static void StationDocks()
        {
            const string W = "catalog-stations";
            // Catalog Phase C (round-3-contract §1.2, C16): the docks at the track rope (SkiTram{Base,Top}StationA
            // .manifest.json prefab_contract.docks[0..1].rope) and the carrier host (R29: the tram base only). Literal oracle.
            const float N = float.NaN;
            object[][] docks =
            {
                // prefab, DockLocalA (x, y, z), DockLocalB (x, y, z), HostsCarriers
                new object[] { "SkiBaseStationBuilding", N, N, N, N, N, N, false },
                new object[] { "SkiTopStationBuilding", N, N, N, N, N, N, false },
                new object[] { "SkiDQBaseStationBuilding", N, N, N, N, N, N, false },
                new object[] { "SkiDQTopStationBuilding", N, N, N, N, N, N, false },
                new object[] { "SkiGondolaBaseStationBuilding", N, N, N, N, N, N, false },
                new object[] { "SkiGondolaTopStationBuilding", N, N, N, N, N, N, false },
                // nitpick B2 (2026-10-13): the base's 6 x 3 lot moved its building 4.00 m toward -X (axis x -4.00)
                // T14 x-sign N (2026-10-16): the game negates FBX x, the prefab rows were mirrored: axis +4.00, A = +X bay, B = -X bay
                new object[] { "SkiTramBaseStationBuilding", 9.50f, 16.00f, -5.50f, -1.50f, 16.00f, -5.50f, true },
                new object[] { "SkiTramTopStationBuilding", -5.50f, 16.00f, -5.50f, 5.50f, 16.00f, -5.50f, false },
            };
            Eq(docks.Length, SkiCatalog.Stations.Count, W, "Stations.Count (dock oracle)");
            for (int i = 0; i < docks.Length && i < SkiCatalog.Stations.Count; i++)
            {
                object[] o = docks[i];
                StationDef s = SkiCatalog.Stations[i];
                string at = "station " + i + " '" + o[0] + "' ";
                Eq((string)o[0], s.Prefab, W, at + "Prefab (dock oracle order)");
                Check(SameOrNaN(new float3((float)o[1], (float)o[2], (float)o[3]), s.DockLocalA), W,
                      at + "DockLocalA " + s.DockLocalA + ", want (" + o[1] + ", " + o[2] + ", " + o[3] + ")");
                Check(SameOrNaN(new float3((float)o[4], (float)o[5], (float)o[6]), s.DockLocalB), W,
                      at + "DockLocalB " + s.DockLocalB + ", want (" + o[4] + ", " + o[5] + ", " + o[6] + ")");
                Eq((bool)o[7], s.HostsCarriers, W, at + "HostsCarriers");
            }
            // The hand-made-row constructors keep "no docks, no host" (tests and CT-16's twin build rows with them).
            var legacy = new StationDef("X", StationRole.Base, "fixedGrip", new float3(0f), -12f, 0, "x", true, 0);
            Check(float.IsNaN(legacy.DockLocalA.x) && float.IsNaN(legacy.DockLocalB.z) && !legacy.HostsCarriers, W,
                  "the 9-argument StationDef constructor does not default to NaN docks / no host");
            // The base's two docks are the bays' rope points: the hosted cabin pivots (∓5.50, 6.00, −5.50) sit 10.00 below
            // (R27: grip top 10.50 = 0.50 above the rope; the mover's drop), on the same x / z.
            StationDef tramBase = SkiCatalog.StationOf(3, StationRole.Base);
            LiftTypeDef tram = SkiCatalog.LiftById("tram");
            if (tramBase != null && tram != null)
                Near(6.00, tramBase.DockLocalA.y - (tram.HangM - tram.GripTopAboveRopeM), 1e-5, W,
                     "tram base DockLocalA.y − (HangM − GripTopAboveRopeM) = the hosted pivot y 6.00");
            // Nitpick B2 (2026-10-13, build/nitpick-b2-tram-ramps.md): the line axis per row (StubTipLocalX), literal oracle:
            // -4.00 on the tram base (its 6 x 3 lot), 0 elsewhere; a Shuttle station's docks sit +-lane about it (the
            // relative geometry the 1e manifests froze: 2 x 5.50 apart, centred on the axis); the legacy constructors 0.
            for (int i = 0; i < SkiCatalog.Stations.Count; i++)
            {
                StationDef s = SkiCatalog.Stations[i];
                // T14 x-sign N (2026-10-16): +4.00 on the tram base in the game frame (was -4.00, authoring frame)
                float want = s.Prefab == "SkiTramBaseStationBuilding" ? 4.00f : 0f;
                Eq(want, s.StubTipLocalX, W, "station '" + s.Prefab + "' StubTipLocalX (the line axis)");
                if (math.all(math.isfinite(s.DockLocalA)) && math.all(math.isfinite(s.DockLocalB)))
                {
                    Near(s.StubTipLocalX, (s.DockLocalA.x + s.DockLocalB.x) * 0.5, 1e-5, W,
                         "station '" + s.Prefab + "' docks centred on its line axis");
                    // T14 x-sign N (2026-10-16): the tram base's A is the +X bay now, so B.x - A.x = -11 there (+11 on the top)
                    Near(s.Prefab == "SkiTramBaseStationBuilding" ? -11.0 : 11.0, s.DockLocalB.x - s.DockLocalA.x, 1e-5, W,
                         "station '" + s.Prefab + "' docks 2 x 5.50 apart (the lane gauge, unchanged by the shift)");
                }
            }
            Check(legacy.StubTipLocalX == 0f && new StationDef("Y", StationRole.Base, "tram", new float3(float.NaN), -12f, 0, "y",
                      true, 3, new float3(-5.5f, 16f, -5.5f), new float3(5.5f, 16f, -5.5f), true).StubTipLocalX == 0f, W,
                  "the 9- and 12-argument StationDef constructors keep the line axis at x 0");
        }

        /// <summary>Equal component-wise, NaN matching NaN.</summary>
        private static bool SameOrNaN(float3 want, float3 got)
        {
            for (int k = 0; k < 3; k++)
            {
                float w = want[k], g = got[k];
                if (float.IsNaN(w) ? !float.IsNaN(g) : w != g)
                    return false;
            }
            return true;
        }

        // ================================================================== CT-7

        private static readonly object[][] ItemOracle =
        {
            // id, prefab, kind, lift type, run kind — round-2-contract §1.6, strip order.
            new object[] { "sign", "SkiResortGate", BuildItemKind.Sign, "", -1 },
            new object[] { "lodge", "SkiAFrameDayLodge", BuildItemKind.Lodge, "", -1 },
            new object[] { "lodgeHut", "SkiLodgeHut", BuildItemKind.Lodge, "", -1 },
            new object[] { "lodgeMid", "SkiLodgeMid", BuildItemKind.Lodge, "", -1 },
            new object[] { "lodgeSummit", "SkiLodgeSummit", BuildItemKind.Lodge, "", -1 },
            new object[] { "lodgeGrand", "SkiLodgeGrand", BuildItemKind.Lodge, "", -1 },
            new object[] { "school", "SkiSchool", BuildItemKind.School, "", -1 },
            new object[] { "patrolHut", "SkiPatrolHut", BuildItemKind.Patrol, "", -1 },
            new object[] { "patrolHQ", "SkiPatrolHQ", BuildItemKind.Patrol, "", -1 },
            new object[] { "baseStation", "SkiBaseStationBuilding", BuildItemKind.Station, "fixedGrip", -1 },
            new object[] { "topStation", "SkiTopStationBuilding", BuildItemKind.Station, "fixedGrip", -1 },
            new object[] { "lift", "SkiChairliftHV", BuildItemKind.Line, "fixedGrip", -1 },
            new object[] { "baseStationDetachable", "SkiDQBaseStationBuilding", BuildItemKind.Station, "detachable", -1 },
            new object[] { "topStationDetachable", "SkiDQTopStationBuilding", BuildItemKind.Station, "detachable", -1 },
            new object[] { "liftDetachable", "SkiDetachableHV", BuildItemKind.Line, "detachable", -1 },
            new object[] { "baseStationGondola", "SkiGondolaBaseStationBuilding", BuildItemKind.Station, "gondola", -1 },
            new object[] { "topStationGondola", "SkiGondolaTopStationBuilding", BuildItemKind.Station, "gondola", -1 },
            new object[] { "liftGondola", "SkiGondolaHV", BuildItemKind.Line, "gondola", -1 },
            new object[] { "baseStationTram", "SkiTramBaseStationBuilding", BuildItemKind.Station, "tram", -1 },
            new object[] { "topStationTram", "SkiTramTopStationBuilding", BuildItemKind.Station, "tram", -1 },
            new object[] { "liftTram", "SkiTramHV", BuildItemKind.Line, "tram", -1 },
            new object[] { "runNatural", "SkiRunNaturalA", BuildItemKind.Run, "", 3 },
            new object[] { "runGroomed", "SkiRunGroomedA", BuildItemKind.Run, "", 0 },
            new object[] { "runSnowmaking", "SkiRunSnowmakingA", BuildItemKind.Run, "", 1 },
            new object[] { "runNight", "SkiRunNightA", BuildItemKind.Run, "", 2 },
        };

        private static void BuildItems()
        {
            const string W = "catalog-items";
            Eq(ItemOracle.Length, SkiCatalog.BuildItems.Count, W, "BuildItems.Count");
            for (int i = 0; i < ItemOracle.Length && i < SkiCatalog.BuildItems.Count; i++)
            {
                object[] o = ItemOracle[i];
                BuildItemDef it = SkiCatalog.BuildItems[i];
                string id = (string)o[0];
                string at = "item " + i + " '" + id + "' ";
                Eq(id, it.Id, W, at + "id (strip order)");
                Eq((string)o[1], it.Prefab, W, at + "prefab");
                Eq((BuildItemKind)o[2], it.Kind, W, at + "kind");
                Eq((string)o[3], it.LiftTypeId, W, at + "lift type");
                Eq((int)o[4], (int)it.RunKind, W, at + "run kind");
                Eq(it.Kind == BuildItemKind.Lodge || it.Kind == BuildItemKind.School || it.Kind == BuildItemKind.Patrol, it.IsUpgrade, W, at + "IsUpgrade");
                if (Check(SkiCatalog.TryGetBuildItemDef(id, out BuildItemDef byId), W, at + "TryGetBuildItemDef missed"))
                    Check(byId.Prefab == it.Prefab && byId.Kind == it.Kind, W, at + "TryGetBuildItemDef answered another row");
                if (Check(SkiCatalog.TryGetBuildItem(id, out CatalogName n, out int kind), W, at + "TryGetBuildItem missed"))
                {
                    Eq((string)o[1], n.Name, W, at + "TryGetBuildItem name");
                    Eq(SkiCatalog.TypeNameOf(it.Kind), n.TypeName, W, at + "TryGetBuildItem type");
                    Eq((int)o[4], kind, W, at + "TryGetBuildItem run kind");
                    Eq(id, n.BuildItemId, W, at + "id round trip");
                }
            }
            Check(!SkiCatalog.TryGetBuildItem("nope", out CatalogName none, out int noneKind) && noneKind == -1 && none.Name == null, W,
                  "TryGetBuildItem(\"nope\") answered");
            Check(!SkiCatalog.TryGetBuildItem(null, out _, out _) && !SkiCatalog.TryGetBuildItem("", out _, out _), W,
                  "TryGetBuildItem(null / \"\") answered");
            Check(!SkiCatalog.TryGetBuildItemDef("nope", out BuildItemDef noDef) && noDef.Id == null, W, "TryGetBuildItemDef(\"nope\") answered");
            Eq(4, SkiCatalog.Runs.Count, W, "Runs.Count");
            for (int i = 0; i < SkiCatalog.Runs.Count; i++)
            {
                RunDef r = SkiCatalog.Runs[i];
                Check(SkiCatalog.TryGetBuildItem(r.BuildItemId, out CatalogName rn, out int rk) && rk == (int)r.Kind && rn.Name == r.Prefab, W,
                      "run row " + r.Prefab + ": item / kind round trip");
            }
            Eq((byte)0, (byte)RunKind.Groomed, W, "RunKind.Groomed");
            Eq((byte)1, (byte)RunKind.Snowmaking, W, "RunKind.Snowmaking");
            Eq((byte)2, (byte)RunKind.Night, W, "RunKind.Night");
            Eq((byte)3, (byte)RunKind.Natural, W, "RunKind.Natural");
        }

        // ================================================================== CT-8

        // tools/preflight.ps1 section 7 NAMES, copied verbatim (round-2-contract §1.1, the ", pending" group 4). PowerShell
        // runs it through [regex]::Match (case-sensitive), like this copy.
        private const string NamesLinePattern =
            @"^\s*public const string \w+\s*=\s*""([^""]+)"";\s*//\s*(\w+Prefab)(?:,\s*item\s+(\w+))?(?:,\s*(pending))?\s*$";
        // The same line with the identifier captured (CT-9 maps `Names.X` to its value).
        private const string NamesIdentPattern =
            @"^\s*public const string (\w+)\s*=\s*""([^""]+)"";\s*//\s*(\w+Prefab)(?:,\s*item\s+(\w+))?(?:,\s*(pending))?\s*$";

        private sealed class NamesLine
        {
            public int LineNo;
            public string Ident, Name, Type, Item;
            public bool Pending;
        }

        /// <summary>The NAMES block's lines (null + one FAIL when the file or the markers are wrong).</summary>
        private static List<NamesLine> ReadNamesBlock(string w)
        {
            string[] lines = CatalogSource(w);
            if (lines == null)
                return null;
            if (!Markers(lines, "// NAMES-BEGIN", "// NAMES-END", w, out int begin, out int end))
                return null;
            var rx = new Regex(NamesIdentPattern);
            var outList = new List<NamesLine>();
            for (int i = begin + 1; i < end; i++)
            {
                Match m = rx.Match(lines[i]);
                if (!Check(m.Success, w, "line " + (i + 1) + " does not match the NAMES format: " + lines[i].Trim()))
                    continue;
                outList.Add(new NamesLine
                {
                    LineNo = i + 1,
                    Ident = m.Groups[1].Value,
                    Name = m.Groups[2].Value,
                    Type = m.Groups[3].Value,
                    Item = m.Groups[4].Success ? m.Groups[4].Value : null,
                    Pending = m.Groups[5].Success,
                });
            }
            return outList;
        }

        private static void NamesBlockText()
        {
            const string W = "catalog-names-block";
            string[] lines = CatalogSource(W);
            if (lines == null || !Markers(lines, "// NAMES-BEGIN", "// NAMES-END", W, out int begin, out int end))
                return;
            var rx = new Regex(NamesLinePattern);
            var parsed = new List<string>();
            for (int i = begin + 1; i < end; i++)
            {
                Match m = rx.Match(lines[i]);
                if (!Check(m.Success, W, "line " + (i + 1) + " does not match the NAMES format: " + lines[i].Trim()))
                    continue;
                parsed.Add(m.Groups[1].Value + "|" + m.Groups[2].Value + "|" + (m.Groups[3].Success ? m.Groups[3].Value : "") + "|" +
                           (m.Groups[4].Success ? "pending" : ""));
            }
            Eq(30, parsed.Count, W, "names between the markers");
            IReadOnlyList<CatalogName> all = SkiCatalog.AllPrefabNames();
            var expected = new List<string>();
            for (int i = 0; i < all.Count; i++)
                expected.Add(all[i].Name + "|" + all[i].TypeName + "|" + (all[i].BuildItemId ?? "") + "|" + (all[i].Expected ? "" : "pending"));
            Check(string.Join(", ", parsed) == string.Join(", ", expected), W,
                  "NAMES block [" + string.Join(", ", parsed) + "] != AllPrefabNames() in order [" + string.Join(", ", expected) + "]");
        }

        // ================================================================== CT-9

        // round-2-contract §1.6 (check-bundle.js R16 uses the same): one row per line between the markers.
        private const string ItemLinePattern =
            @"^\s*new BuildItemDef\(""(\w+)"",\s*Names\.(\w+),\s*BuildItemKind\.(\w+),\s*""(\w*)"",\s*(-?\d+)\),\s*$";

        private static void BuildItemsBlockText()
        {
            const string W = "catalog-items-block";
            string[] lines = CatalogSource(W);
            if (lines == null || !Markers(lines, "// BUILD-ITEMS-BEGIN", "// BUILD-ITEMS-END", W, out int begin, out int end))
                return;
            // MUST 8: the markers sit INSIDE the braces — the line before BEGIN ends the `= {` opener, the line after END closes.
            Check(lines[begin - 1].Trim() == "{" && lines[end + 1].Trim() == "};", W,
                  "the BUILD-ITEMS markers are not directly inside `{` … `};` (skeptic MUST 8: every line between them is a row)");
            List<NamesLine> names = ReadNamesBlock(W);
            if (names == null)
                return;
            var valueOf = new Dictionary<string, NamesLine>(StringComparer.Ordinal);
            foreach (NamesLine n in names)
                valueOf[n.Ident] = n;
            var rx = new Regex(ItemLinePattern);
            var rows = new List<string[]>();
            for (int i = begin + 1; i < end; i++)
            {
                Match m = rx.Match(lines[i]);
                if (!Check(m.Success, W, "line " + (i + 1) + " inside the BUILD-ITEMS block is not a row: " + lines[i].Trim()))
                    continue;
                rows.Add(new[] { m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value, m.Groups[5].Value });
            }
            Check(rows.Count > 0, W, "an empty BUILD-ITEMS table");
            Eq(SkiCatalog.BuildItems.Count, rows.Count, W, "text rows vs BuildItems.Count");
            var ids = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count && i < SkiCatalog.BuildItems.Count; i++)
            {
                string[] r = rows[i];
                BuildItemDef it = SkiCatalog.BuildItems[i];
                string at = "text row " + i + " '" + r[0] + "' ";
                Check(!ids.ContainsKey(r[0]), W, at + "duplicate id");
                ids[r[0]] = i;
                Eq(it.Id, r[0], W, at + "id vs BuildItems[" + i + "]");
                if (Check(valueOf.TryGetValue(r[1], out NamesLine nl), W, at + "Names." + r[1] + " is not a NAMES entry"))
                {
                    Eq(it.Prefab, nl.Name, W, at + "Names." + r[1] + " value vs BuildItems[" + i + "].Prefab");
                    Eq(r[0], nl.Item, W, at + "the NAMES line of Names." + r[1] + " carries item token");
                }
                Eq(it.Kind.ToString(), r[2], W, at + "kind");
                Eq(it.LiftTypeId, r[3], W, at + "lift type");
                Eq(((int)it.RunKind).ToString(CultureInfo.InvariantCulture), r[4], W, at + "run kind");
            }
            foreach (NamesLine n in names)
            {
                if (n.Item == null)
                    continue;
                int count = 0;
                foreach (string[] r in rows)
                {
                    if (r[0] == n.Item)
                    {
                        count++;
                        Check(valueOf.TryGetValue(r[1], out NamesLine target) && target.Name == n.Name, W,
                              "item '" + n.Item + "': row names Names." + r[1] + ", its NAMES token is on '" + n.Name + "'");
                    }
                }
                Eq(1, count, W, "NAMES item token '" + n.Item + "' (" + n.Name + "): table rows with that id");
            }
        }

        // ================================================================== CT-10

        private static void NextActionTable()
        {
            const string W = "catalog-lifetime";
            Act(true, false, 5, 5, CatalogAction.Resolve, W, "dirty resolves, even in a validated frame");
            Act(true, true, 5, 6, CatalogAction.Resolve, W, "a new load retries after a failure");
            Act(false, true, 5, 6, CatalogAction.Serve, W, "no retry storm in a failed load");
            Act(false, false, 5, 5, CatalogAction.Serve, W, "validated this frame");
            Act(false, false, 5, 6, CatalogAction.Validate, W, "first query of a new frame validates");
            Act(false, false, -1, 0, CatalogAction.Validate, W, "initial state (M1): validates zero entities, writes nothing");
            // M2: the system clears dirty and sets validatedFrame BEFORE resolving, so after a failed resolution the
            // state is (dirty F, failed T, v, f): Serve for ANY frame until the next Game / Editor preload.
            Act(false, true, 7, 7, CatalogAction.Serve, W, "M2: failed, same frame");
            Act(false, true, 7, 8, CatalogAction.Serve, W, "M2: failed, next frame");
            Act(false, true, 7, 100000, CatalogAction.Serve, W, "M2: failed, much later");
            Act(false, true, -1, 0, CatalogAction.Serve, W, "M2: failed before any validation");
            Eq((byte)0, (byte)CatalogAction.Serve, W, "CatalogAction.Serve");
            Eq((byte)1, (byte)CatalogAction.Validate, W, "CatalogAction.Validate");
            Eq((byte)2, (byte)CatalogAction.Resolve, W, "CatalogAction.Resolve");
        }

        private static void Act(bool dirty, bool failed, int validated, int frame, CatalogAction want, string w, string what)
        {
            CatalogAction got = SkiCatalog.NextAction(dirty, failed, validated, frame);
            Check(got == want, w, "NextAction(" + dirty + ", " + failed + ", " + validated + ", " + frame + ") = " + got +
                  ", want " + want + " (" + what + ")");
        }

        // ================================================================== CT-11

        /// <summary>catalog/carriers.json (round-2-contract §10.2; skeptic MUST 2: schema 1, the new fields additive). Rows
        /// map to lift types by object_prefab (ByCarrierPrefab); lift_type and line_prefab are compared when present
        /// (rows gd8 / tram are the cabin lens's: an exact-equality asset gate, test_gondola_tram_seams S11, keeps them as
        /// lift_cabin.CARRIERS writes them, so their line_prefab is the "&lt;line lens&gt;" placeholder or absent).</summary>
        private static void CarriersJson()
        {
            const string W = "catalog-carriers-json";
            string path = CatalogTests.RepoFile("catalog/carriers.json");
            if (!Check(path != null, W, "catalog/carriers.json not found from " + Directory.GetCurrentDirectory() + " or above " +
                                        AppContext.BaseDirectory))
                return;
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception e)
            {
                Check(false, W, "carriers.json does not parse: " + e.GetType().Name + ": " + e.Message);
                return;
            }
            using (doc)
            {
                JsonElement root = doc.RootElement;
                Check(root.TryGetProperty("schema", out JsonElement schema) && schema.ValueKind == JsonValueKind.Number &&
                      schema.GetInt32() == 1, W, "schema is not 1 (MUST 2: lift_chair.py:101-102 rejects any other at import)");
                if (!Check(root.TryGetProperty("carriers", out JsonElement carriers) && carriers.ValueKind == JsonValueKind.Object, W,
                           "no \"carriers\" object"))
                    return;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var notes = new List<string>();
                foreach (JsonProperty row in carriers.EnumerateObject())
                {
                    string at = "row '" + row.Name + "' ";
                    JsonElement r = row.Value;
                    string prefab = Str(r, "object_prefab");
                    LiftTypeDef d = SkiCatalog.ByCarrierPrefab(prefab);
                    if (!Check(d != null, W, at + "object_prefab '" + prefab + "' is no catalog carrier"))
                        continue;
                    Check(seen.Add(d.Id), W, at + "is a second json row for lift type '" + d.Id + "'");
                    string liftType = Str(r, "lift_type");
                    if (liftType != null)
                        Eq(d.Id, liftType, W, at + "lift_type");
                    else
                        notes.Add(row.Name + ": no lift_type (mapped by object_prefab to " + d.Id + ")");
                    string line = Str(r, "line_prefab");
                    if (line != null && !line.StartsWith("<", StringComparison.Ordinal))
                        Eq(d.LinePrefab, line, W, at + "line_prefab");
                    else
                        notes.Add(row.Name + ": line_prefab " + (line ?? "absent") + " (catalog: " + d.LinePrefab + ")");
                    NumEq(r, "seats", d.SeatsPerCarrier, true, W, at);
                    NumEq(r, "speed_mps", d.SpeedMps, false, W, at);
                    NumEq(r, "hang_m", d.HangM, true, W, at);
                    NumEq(r, "grip_top_above_rope_m", d.GripTopAboveRopeM, true, W, at);
                    NumEq(r, "lane_x_m", d.GaugeHalfM, true, W, at);
                    NumEq(r, "axle_m", d.AxleM, false, W, at);
                    NumEq(r, "spacing_m", d.DesignSpacingM, false, W, at);
                    Check(Num(r, "speed_mps", out _), W, at + "has no speed_mps (round-2-contract §10.2)");
                    // Catalog Phase C (round-3-contract §8.1 CT-11): dwell_s == DwellS when present (tram 60); a Shuttle row
                    // must carry it (the mover's station stop has one source of truth).
                    NumEq(r, "dwell_s", d.DwellS, d.Mover == MoverKind.Shuttle, W, at);
                }
                for (int i = 0; i < SkiCatalog.Lifts.Count; i++)
                    Check(seen.Contains(SkiCatalog.Lifts[i].Id), W, "lift type '" + SkiCatalog.Lifts[i].Id + "' has no carriers.json row");
                if (notes.Count > 0)
                    Console.WriteLine("INFO  " + W + ": " + string.Join("; ", notes));
            }
        }

        private static string Str(JsonElement r, string name)
        {
            return r.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }

        private static bool Num(JsonElement r, string name, out double value)
        {
            if (r.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number)
            {
                value = v.GetDouble();
                return true;
            }
            value = double.NaN;
            return false;
        }

        private static void NumEq(JsonElement r, string name, double want, bool required, string w, string at)
        {
            if (!Num(r, name, out double got))
            {
                Check(!required, w, at + "has no numeric " + name);
                return;
            }
            Near(want, got, 1e-6, w, at + name + " (json) vs SkiCatalog");
        }

        // ================================================================== CT-12

        /// <summary>gamedata-mirror drift, Expected names only (round-2-contract §11.1; MUST 7 keeps the sign at its
        /// shipped cost, so there is no allow-list). The mirror's .Prefab text is positional JSON (unquoted $fstrref
        /// values), so fields are read with a regex on the text (UTF-8, no BOM). A mirror file not found = one FAIL.</summary>
        private static void MirrorDrift()
        {
            const string W = "catalog-mirror";
            int compared = 0, skipped = 0;
            foreach (LiftTypeDef d in SkiCatalog.Lifts)
            {
                if (!d.LineExpected)
                {
                    skipped++;
                    continue;
                }
                string line = Mirror(d.LinePrefab, W);
                if (line != null)
                {
                    compared++;
                    MirrorNum(line, RootField("m_MaxPylonDistance"), d.Envelope.MaxPylonM, W, d.LinePrefab + " m_MaxPylonDistance");
                    MirrorNum(line, RootField("m_MaxSlopeSteepness"), d.Envelope.SlopeMax, W, d.LinePrefab + " m_MaxSlopeSteepness");
                    MirrorNum(line, RootField("m_Hanging"), d.Envelope.NetHanging, W, d.LinePrefab + " m_Hanging");
                    MirrorNum(line, @"""m_ElevationRange"":\s*\{[^{}]*?""max"":\s*(-?[0-9.]+)", d.Envelope.ElevationMaxM, W,
                              d.LinePrefab + " PlaceableNet m_ElevationRange.max");
                    int spaced = 0;
                    foreach (Match m in Regex.Matches(line, @"""m_Spacing"":\s*(-?[0-9.]+)"))
                    {
                        double v = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                        if (v <= 0)
                            continue;
                        spaced++;
                        Near(d.SpacingM, v, 1e-6, W, d.LinePrefab + " NetSubObjects m_Spacing (D1: the catalog's SpacingM)");
                    }
                    Check(d.Mover != MoverKind.Loop || spaced > 0, W, d.LinePrefab + ": no NetSubObjects row with m_Spacing > 0 (a Loop line spawns its carriers)");
                }
                string piece = Mirror(d.PiecePrefab, W);
                if (piece != null)
                {
                    compared++;
                    MirrorNum(piece, @"""m_ConstructionCost"":\s*(-?[0-9.]+)", d.LineBuildPer8m, W, d.PiecePrefab + " m_ConstructionCost");
                    MirrorNum(piece, @"""m_ElevationCost"":\s*(-?[0-9.]+)", d.ElevationCostPer10m, W, d.PiecePrefab + " m_ElevationCost");
                    MirrorNum(piece, @"""m_HeightRange"":\s*\{[^{}]*?""min"":\s*(-?[0-9.]+)", d.Envelope.HeightRangeMinM, W,
                              d.PiecePrefab + " m_HeightRange.min");
                }
            }
            foreach (StationDef s in SkiCatalog.Stations)
            {
                if (!s.Expected)
                {
                    skipped++;
                    continue;
                }
                string text = Mirror(s.Prefab, W);
                if (text == null)
                    continue;
                compared++;
                MirrorNum(text, PlaceableCost, s.BuildCost, W, s.Prefab + " PlaceableObject m_ConstructionCost");
            }
            foreach (LodgeDef d in SkiCatalog.Lodges)
            {
                if (!d.Expected)
                {
                    skipped++;
                    continue;
                }
                string text = Mirror(d.Prefab, W);
                if (text == null)
                    continue;
                compared++;
                UpgradeFields(text, d.Prefab, d.BuildCost, d.ForbidMultiple, d.Jobs, d.Upkeep, W);
            }
            foreach (SchoolDef d in SkiCatalog.Schools)
            {
                if (!d.Expected)
                {
                    skipped++;
                    continue;
                }
                string text = Mirror(d.Prefab, W);
                if (text == null)
                    continue;
                compared++;
                UpgradeFields(text, d.Prefab, d.BuildCost, d.ForbidMultiple, d.Jobs, d.Upkeep, W);
            }
            foreach (PatrolDef d in SkiCatalog.Patrols)
            {
                if (!d.Expected)
                {
                    skipped++;
                    continue;
                }
                string text = Mirror(d.Prefab, W);
                if (text == null)
                    continue;
                compared++;
                UpgradeFields(text, d.Prefab, d.BuildCost, d.ForbidMultiple, d.Jobs, d.Upkeep, W);
            }
            foreach (SignDef g in SkiCatalog.Signs)
            {
                if (!g.Expected)
                {
                    skipped++;
                    continue;
                }
                string text = Mirror(g.Prefab, W);
                if (text == null)
                    continue;
                compared++;
                MirrorNum(text, PlaceableCost, g.BuildCost, W, g.Prefab + " PlaceableObject m_ConstructionCost");
            }
            Check(compared >= 6, W, "only " + compared + " mirror file(s) compared (want the FG line, piece, 2 stations, the A-frame, the sign)");
            Console.WriteLine("INFO  " + W + ": " + compared + " mirror prefab(s) compared, " + skipped + " pending row(s) skipped");
        }

        private const string PlaceableCost = @"\|Game\.Prefabs\.PlaceableObject, Game""[^{}]*?""m_ConstructionCost"":\s*(-?[0-9.]+)";

        /// <summary>A root-level field of a prefab (4-space indent: the PrefabBase's own fields).</summary>
        private static string RootField(string name)
        {
            return @"(?m)^ {4}""" + name + @""":\s*(-?[0-9.]+)";
        }

        private static void UpgradeFields(string text, string prefab, int cost, bool forbid, int jobs, int upkeep, string w)
        {
            MirrorNum(text, @"""m_UpgradeCost"":\s*(-?[0-9.]+)", cost, w, prefab + " ServiceUpgrade m_UpgradeCost");
            Match fm = Regex.Match(text, @"""m_ForbidMultiple"":\s*(true|false)");
            if (Check(fm.Success, w, prefab + ": no m_ForbidMultiple in the mirror"))
                Eq(forbid ? "true" : "false", fm.Groups[1].Value, w, prefab + " m_ForbidMultiple");
            MirrorNum(text, @"""m_Workplaces"":\s*(-?[0-9.]+)", jobs, w, prefab + " Workplace m_Workplaces");
            MirrorNum(text, @"""m_Upkeep"":\s*(-?[0-9.]+)", upkeep, w, prefab + " ServiceConsumption m_Upkeep");
        }

        private static void MirrorNum(string text, string pattern, double want, string w, string what)
        {
            Match m = Regex.Match(text, pattern);
            if (!Check(m.Success, w, what + ": field not found in the mirror"))
                return;
            double got = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            Near(want, got, 1e-6, w, what + " (mirror) vs SkiCatalog");
        }

        /// <summary>gamedata-mirror/ImportedData/&lt;Name&gt;/&lt;Name&gt;.Prefab as text; null + one FAIL when missing.</summary>
        private static string Mirror(string name, string w)
        {
            string path = RepoFile("gamedata-mirror/ImportedData/" + name + "/" + name + ".Prefab");
            if (!Check(path != null, w, "gamedata-mirror/ImportedData/" + name + "/" + name + ".Prefab not found (an Expected row needs its mirror)"))
                return null;
            byte[] bytes = File.ReadAllBytes(path);
            Check(!(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF), w, name + ".Prefab has a UTF-8 BOM");
            return new UTF8Encoding(false).GetString(bytes);
        }

        // ================================================================== CT-13

        private static void PendingTokens()
        {
            const string W = "catalog-pending";
            List<NamesLine> names = ReadNamesBlock(W);
            if (names == null)
                return;
            int pending = 0;
            foreach (NamesLine n in names)
            {
                if (n.Pending)
                    pending++;
                if (Check(SkiCatalog.TryGetName(n.Name, out CatalogName entry), W, "NAMES line " + n.LineNo + " '" + n.Name + "' is no catalog name"))
                    Check(n.Pending == !entry.Expected, W, "'" + n.Name + "': NAMES token " + (n.Pending ? "pending" : "(none)") +
                          " but its row flag says Expected = " + entry.Expected + " (flip both in one edit)");
            }
            Eq(0, pending, W, "pending tokens in the NAMES block (0 since the S1 lift flip, 2026-10-13; 5 after the first S1 flip, 19 before)");
            // Each row's Expected is derived from its names (MUST 5).
            foreach (LiftTypeDef d in SkiCatalog.Lifts)
            {
                bool all = d.LineExpected;
                foreach (CarrierDef c in d.Carriers)
                {
                    Eq(SkiCatalog.IsExpected(c.Prefab), c.Expected, W, "carrier '" + c.Prefab + "' flag vs its name");
                    all &= c.Expected;
                }
                Eq(SkiCatalog.IsExpected(d.LinePrefab), d.LineExpected, W, "lift type '" + d.Id + "' LineExpected vs its line name");
                Eq(all, d.Expected, W, "lift type '" + d.Id + "' Expected == line and every carrier expected");
            }
            foreach (StationDef s in SkiCatalog.Stations)
                Eq(SkiCatalog.IsExpected(s.Prefab), s.Expected, W, "station '" + s.Prefab + "' flag vs its name");
            foreach (LodgeDef d in SkiCatalog.Lodges)
                Eq(SkiCatalog.IsExpected(d.Prefab), d.Expected, W, "lodge '" + d.Prefab + "' flag vs its name");
            foreach (SchoolDef d in SkiCatalog.Schools)
                Eq(SkiCatalog.IsExpected(d.Prefab), d.Expected, W, "school '" + d.Prefab + "' flag vs its name");
            foreach (PatrolDef d in SkiCatalog.Patrols)
                Eq(SkiCatalog.IsExpected(d.Prefab), d.Expected, W, "patrol '" + d.Prefab + "' flag vs its name");
            foreach (SignDef d in SkiCatalog.Signs)
                Eq(SkiCatalog.IsExpected(d.Prefab), d.Expected, W, "sign '" + d.Prefab + "' flag vs its name");
            foreach (RunDef r in SkiCatalog.Runs)
                Check(SkiCatalog.IsExpected(r.Prefab), W, "run '" + r.Prefab + "' is pending (every run is shipped)");
            Check(SkiCatalog.IsExpected(SkiCatalog.MarkerPrefab), W, "the marker is pending");
        }

        // ================================================================== CT-14

        private static void ItemsPerType()
        {
            const string W = "catalog-items-per-type";
            for (int i = 0; i < SkiCatalog.Lifts.Count; i++)
            {
                LiftTypeDef d = SkiCatalog.Lifts[i];
                string suffix = d.Id == "fixedGrip" ? "" : char.ToUpperInvariant(d.Id[0]) + d.Id.Substring(1);
                Eq("lift" + suffix, SkiCatalog.LineItemOf(i), W, d.Id + " LineItemOf");
                Eq("baseStation" + suffix, SkiCatalog.StationItemOf(i, StationRole.Base), W, d.Id + " StationItemOf(Base)");
                Eq("topStation" + suffix, SkiCatalog.StationItemOf(i, StationRole.Top), W, d.Id + " StationItemOf(Top)");
                int bases = 0, tops = 0, lines = 0;
                foreach (BuildItemDef it in SkiCatalog.BuildItems)
                {
                    if (it.LiftTypeId != d.Id)
                        continue;
                    if (it.Kind == BuildItemKind.Line)
                    {
                        lines++;
                        Eq(d.LinePrefab, it.Prefab, W, d.Id + " line item prefab");
                        Eq(d.BuildItemId, it.Id, W, d.Id + " line item id");
                    }
                    else if (it.Kind == BuildItemKind.Station)
                    {
                        StationDef st = SkiCatalog.StationByPrefab(it.Prefab);
                        if (!Check(st != null && st.LiftTypeId == d.Id, W, d.Id + " station item '" + it.Id + "' is not its station"))
                            continue;
                        if (st.Role == StationRole.Base)
                            bases++;
                        else
                            tops++;
                        Eq(st.BuildItemId, it.Id, W, d.Id + " station item id vs the StationDef");
                    }
                    else
                    {
                        Check(false, W, "item '" + it.Id + "' of kind " + it.Kind + " carries lift type '" + it.LiftTypeId + "'");
                    }
                }
                Check(bases == 1 && tops == 1 && lines == 1, W, d.Id + ": " + bases + " base, " + tops + " top, " + lines + " line item(s), want 1 / 1 / 1");
            }
            Check(SkiCatalog.LineItemOf(-1) == null && SkiCatalog.LineItemOf(4) == null && SkiCatalog.LineItemOf(255) == null, W,
                  "LineItemOf out of range answered");
            Check(SkiCatalog.StationItemOf(-1, StationRole.Base) == null && SkiCatalog.StationItemOf(4, StationRole.Top) == null, W,
                  "StationItemOf out of range answered");
            Eq("PowerLinePrefab", SkiCatalog.TypeNameOf(BuildItemKind.Line), W, "TypeNameOf(Line)");
            Eq("SurfacePrefab", SkiCatalog.TypeNameOf(BuildItemKind.Run), W, "TypeNameOf(Run)");
            foreach (BuildItemKind k in new[] { BuildItemKind.Sign, BuildItemKind.Lodge, BuildItemKind.School, BuildItemKind.Patrol, BuildItemKind.Station })
                Eq("BuildingPrefab", SkiCatalog.TypeNameOf(k), W, "TypeNameOf(" + k + ")");
            foreach (BuildItemDef it in SkiCatalog.BuildItems)
            {
                Check((it.Kind == BuildItemKind.Run) == it.Id.StartsWith("run", StringComparison.Ordinal), W,
                      "item '" + it.Id + "': Run items and only they start with \"run\"");
                Check((it.Kind == BuildItemKind.Station || it.Kind == BuildItemKind.Line) == (it.LiftTypeId.Length > 0), W,
                      "item '" + it.Id + "': a lift type exactly on Station / Line items");
            }
        }

        // ================================================================== CT-15

        private static void Arithmetic()
        {
            const string W = "catalog-arithmetic";
            // SubObjectSystem.cs:1743 (1.6.0f1): n = (int)(L / max(1, s) − 0.5f), in float.
            Eq(4, SkiCatalog.EvenSpacingCount(60f, 12f), W, "EvenSpacingCount(60, 12)");
            Eq(3, SkiCatalog.EvenSpacingCount(60f, 15f), W, "EvenSpacingCount(60, 15)");
            Eq(6, SkiCatalog.EvenSpacingCount(57.142857f, 8f), W, "EvenSpacingCount(57.142857, 8)");
            Eq(3, SkiCatalog.EvenSpacingCount(90f, 22.5f), W, "EvenSpacingCount(90, 22.5)");
            Eq(2, SkiCatalog.EvenSpacingCount(90f, 30f), W, "EvenSpacingCount(90, 30)");
            Eq(5, SkiCatalog.EvenSpacingCount(250f, 45f), W, "EvenSpacingCount(250, 45)");
            Eq(3, SkiCatalog.EvenSpacingCount(250f, 60f), W, "EvenSpacingCount(250, 60)");
            Eq(0, SkiCatalog.EvenSpacingCount(29.9f, 30f), W, "EvenSpacingCount(29.9, 30)");
            Eq(0, SkiCatalog.EvenSpacingCount(10f, 30f), W, "EvenSpacingCount(10, 30) (int truncates −0.17 to 0)");
            Eq(7, SkiCatalog.EvenSpacingCount(60f, 8f), W, "EvenSpacingCount(60, 8) (the FG full edge today)");
            Eq(9, SkiCatalog.EvenSpacingCount(10f, 0.5f), W, "EvenSpacingCount(10, 0.5) (max(1, s): spacing below 1 m reads as 1)");
            foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -5f })
            {
                Eq(0, SkiCatalog.EvenSpacingCount(bad, 10f), W, "EvenSpacingCount(" + bad + ", 10)");
                Eq(0, SkiCatalog.EvenSpacingCount(100f, bad), W, "EvenSpacingCount(100, " + bad + ")");
            }

            // The bound (§3.1, skeptic verdict 2): with the prefab at SpacingM a straight course never spawns MORE than
            // floor(loop / SpacingM) − 1 carriers. Course split k = ceil((L − 0.16) / MaxPylon) equal edges
            // (CourseSplitSystem.cs:3493), two rows (both lanes) per edge, loop = 2 × line + 2π × gauge + 2 × (base run +
            // top run). Terminal runs anchor → wheel: FG 7.2 + 5.0, DQ 20.6 + 17.6 (SkiDQ*StationA manifests), gondola
            // 20.4 + 17.4 (dq-terminal §12). Course lengths: the §3.1 table's ranges.
            var rng = new System.Random(20261011);
            foreach (LiftTypeDef d in SkiCatalog.Lifts)
            {
                if (d.Mover != MoverKind.Loop)
                    continue;
                double runs, lo, hi;
                switch (d.Id)
                {
                    case "fixedGrip": runs = 7.2 + 5.0; lo = 120; hi = 1500; break;
                    case "detachable": runs = 20.6 + 17.6; lo = 180; hi = 2500; break;
                    case "gondola": runs = 20.4 + 17.4; lo = 500; hi = 3500; break;
                    default: Check(false, W, "no course table for Loop type '" + d.Id + "'"); continue;
                }
                int worstSlack = int.MaxValue;
                for (int t = 0; t < 1000; t++)
                {
                    double lengthM = lo + rng.NextDouble() * (hi - lo);
                    int k = (int)Math.Ceiling((lengthM - 0.16) / d.Envelope.MaxPylonM);
                    float edge = (float)(lengthM / k);
                    int carriers = 2 * k * SkiCatalog.EvenSpacingCount(edge, d.SpacingM);
                    double loop = 2.0 * lengthM + 2.0 * Math.PI * d.GaugeHalfM + 2.0 * runs;
                    int bound = (int)Math.Floor(loop / d.SpacingM) - 1;
                    worstSlack = Math.Min(worstSlack, bound - carriers);
                    if (carriers > bound)
                    {
                        Check(false, W, d.Id + " course " + lengthM.ToString("0.###", CultureInfo.InvariantCulture) + " m: " + carriers +
                              " carriers > floor(loop / " + d.SpacingM + ") − 1 = " + bound);
                        break;
                    }
                }
                Check(worstSlack >= 0, W, d.Id + ": the bound property failed (worst slack " + worstSlack + ")");
            }

            // D9: ApproachPinCeilingM = max(4, HangM + 1.5); §3.3: HeightRange.min under the clearance bound
            // AxleM − drop − MaxPylon × (laneHanging − netHanging) + 2 − 0.5, drop = HangM (Loop) / HangM − GripTop (Shuttle).
            foreach (LiftTypeDef d in SkiCatalog.Lifts)
            {
                Near(Math.Max(4.0, d.HangM + 1.5), d.ApproachPinCeilingM, 1e-5, W, d.Id + " ApproachPinCeilingM == max(4, HangM + 1.5)");
                double drop = d.Mover == MoverKind.Loop ? d.HangM : d.HangM - d.GripTopAboveRopeM;
                double bound = d.AxleM - drop - d.Envelope.MaxPylonM * (d.Envelope.LaneHanging - d.Envelope.NetHanging) + 2.0 - 0.5;
                Check(d.Envelope.HeightRangeMinM <= bound + 1e-3, W, d.Id + " HeightRangeMinM " + d.Envelope.HeightRangeMinM +
                      " above the §3.3 clearance bound " + bound.ToString("0.###", CultureInfo.InvariantCulture));
            }
        }

        // ================================================================== CT-16

        /// <summary>SkiCatalog.CheckRows (the static constructor's generic rules) on a hand-made twin.</summary>
        private static void SanityTwin()
        {
            const string W = "catalog-sanity-twin";
            List<string> clean = Twin(TwinFault.None);
            Check(clean.Count == 0, W, "the clean twin has " + clean.Count + " finding(s): " + string.Join(" | ", clean));
            // Catalog Phase C (round-3-contract §8.1 CT-16): the faulty twin also carries a Shuttle row with AccelMps2 0 → 4.
            List<string> bad = Twin(TwinFault.PhaseB);
            Eq(4, bad.Count, W, "findings on the twin with four faults (" + string.Join(" | ", bad) + ")");
            Check(bad.Exists(p => p.Contains("duplicate build item id 'line'")), W, "no duplicate-item-id finding");
            Check(bad.Exists(p => p.Contains("Loop lift type 'a' has SpacingM 0")), W, "no Loop-SpacingM-0 finding");
            Check(bad.Exists(p => p.Contains("station 'TX' names an unknown lift type 'zzz'")), W, "no unknown-type station finding");
            Check(bad.Exists(p => p.Contains("Shuttle lift type 'b' has AccelMps2 0")), W, "no Shuttle-AccelMps2-0 finding");

            // Catalog Phase C (round-3-contract §1.3): each new rule alone gives exactly one finding.
            object[][] single =
            {
                new object[] { TwinFault.ShuttleDwellNegative, "Shuttle lift type 'b' has DwellS -1" },
                new object[] { TwinFault.ShuttleNoHost, "Shuttle lift type 'b' has 0 Base station(s) that HostsCarriers" },
                new object[] { TwinFault.ShuttleTopHosts, "station 'ST' of Shuttle type 'b' is a Top station that HostsCarriers" },
                new object[] { TwinFault.ShuttleNaNDock, "station 'ST' of Shuttle type 'b' has DockLocalA" },
                new object[] { TwinFault.LoopDock, "station 'TB' of Loop type 'a' has a dock" },
                new object[] { TwinFault.LoopHosts, "station 'TT' of Loop type 'a' HostsCarriers" },
                new object[] { TwinFault.LoopDwell, "Loop lift type 'a' has AccelMps2 0 / DwellS 5" },
            };
            foreach (object[] s in single)
            {
                List<string> found = Twin((TwinFault)s[0]);
                string want = (string)s[1];
                Check(found.Count == 1 && found[0].Contains(want), W, s[0] + ": want exactly one finding containing \"" + want +
                      "\", got " + found.Count + " (" + string.Join(" | ", found) + ")");
            }
        }

        private enum TwinFault { None, PhaseB, ShuttleDwellNegative, ShuttleNoHost, ShuttleTopHosts, ShuttleNaNDock, LoopDock, LoopHosts, LoopDwell }

        private static List<string> Twin(TwinFault fault)
        {
            bool faults = fault == TwinFault.PhaseB;
            var spec = new LiftTypeSpec
            {
                Id = "a", LinePrefab = "TL", PiecePrefab = "TP", LineItemId = "line", CarrierPrefabs = new[] { "TC" },
                Mover = MoverKind.Loop, SeatsPerCarrier = 1, SpeedMps = 1f, GaugeHalfM = 1f, HangM = 1f, SpacingM = faults ? 0f : 8f,
                StationCostBase = 10, StationCostTop = 5, ScoreValue = 1.0, SummerShare = 0.5, LineExpected = true,
                DwellS = fault == TwinFault.LoopDwell ? 5f : 0f,
            };
            // Catalog Phase C: a Shuttle row (the tram's shape) — clean unless a fault names it.
            var shuttle = new LiftTypeSpec
            {
                Id = "b", LinePrefab = "SL", PiecePrefab = "SP", LineItemId = "lineB", CarrierPrefabs = new[] { "SC" },
                Mover = MoverKind.Shuttle, SeatsPerCarrier = 100, SpeedMps = 10f, GaugeHalfM = 5.5f, HangM = 10.5f, SpacingM = 0f,
                StationCostBase = 20, StationCostTop = 15, ScoreValue = 1.0, SummerShare = 1.0, LineExpected = true,
                AccelMps2 = faults ? 0f : 0.8f, DwellS = fault == TwinFault.ShuttleDwellNegative ? -1f : 60f,
            };
            var lifts = new List<LiftTypeDef> { new LiftTypeDef(0, spec), new LiftTypeDef(1, shuttle) };
            float3 nan = new float3(float.NaN), dockA = new float3(-5.5f, 16f, -5.5f), dockB = new float3(5.5f, 16f, -5.5f);
            var stations = new List<StationDef>
            {
                fault == TwinFault.LoopDock
                    ? new StationDef("TB", StationRole.Base, "a", new float3(0f, 1f, 0f), -12f, 10, "base", true, 0, dockA, dockB, false)
                    : new StationDef("TB", StationRole.Base, "a", new float3(0f, 1f, 0f), -12f, 10, "base", true, 0),
                new StationDef("TT", StationRole.Top, "a", new float3(0f, 1f, 0f), -12f, 5, "top", true, 0, nan, nan,
                               fault == TwinFault.LoopHosts),
                new StationDef("SB", StationRole.Base, "b", nan, -12f, 20, "baseB", true, 1, dockA, dockB,
                               fault != TwinFault.ShuttleNoHost),
                new StationDef("ST", StationRole.Top, "b", nan, -12f, 15, "topB", true, 1,
                               fault == TwinFault.ShuttleNaNDock ? nan : dockA, dockB, fault == TwinFault.ShuttleTopHosts),
            };
            if (faults)
                stations.Add(new StationDef("TX", StationRole.Base, "zzz", new float3(0f, 1f, 0f), -12f, 0, null, true));
            var items = new List<BuildItemDef>
            {
                new BuildItemDef("base", "TB", BuildItemKind.Station, "a", -1),
                new BuildItemDef("top", "TT", BuildItemKind.Station, "a", -1),
                new BuildItemDef("line", "TL", BuildItemKind.Line, "a", -1),
                new BuildItemDef("baseB", "SB", BuildItemKind.Station, "b", -1),
                new BuildItemDef("topB", "ST", BuildItemKind.Station, "b", -1),
                new BuildItemDef("lineB", "SL", BuildItemKind.Line, "b", -1),
            };
            if (faults)
                items.Add(new BuildItemDef("line", "TL", BuildItemKind.Line, "a", -1));
            var lodges = new List<LodgeDef>();
            var schools = new List<SchoolDef>();
            var patrols = new List<PatrolDef>();
            var signs = new List<SignDef>();
            var runs = new List<RunDef>();
            List<CatalogName> names = SkiCatalog.NameList(lifts, null, stations, lodges, schools, patrols, signs, runs);
            var problems = new List<string>();
            SkiCatalog.CheckRows(problems, lifts, stations, lodges, schools, patrols, signs, runs, items, names);
            return problems;
        }

        // ================================================================== helpers

        /// <summary>mod/SkiResortMod/Systems/SkiCatalog.cs as lines; null + one FAIL when not found.</summary>
        private static string[] CatalogSource(string w)
        {
            string path = RepoFile("mod/SkiResortMod/Systems/SkiCatalog.cs");
            if (!Check(path != null, w, "mod/SkiResortMod/Systems/SkiCatalog.cs not found from " + Directory.GetCurrentDirectory() +
                                        " or above " + AppContext.BaseDirectory))
                return null;
            return File.ReadAllLines(path);
        }

        /// <summary>Exactly one begin and one end marker (trimmed line equality), begin before end.</summary>
        private static bool Markers(string[] lines, string beginMarker, string endMarker, string w, out int begin, out int end)
        {
            begin = -1;
            end = -1;
            int begins = 0, ends = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string tl = lines[i].Trim();
                if (tl == beginMarker)
                {
                    begins++;
                    begin = i;
                }
                else if (tl == endMarker)
                {
                    ends++;
                    end = i;
                }
            }
            return Check(begins == 1 && ends == 1 && begin < end && begin > 0 && end < lines.Length - 1, w,
                         "markers: " + begins + " " + beginMarker + ", " + ends + " " + endMarker);
        }

        /// <summary>A repo file: the current directory first (the documented run is from the ModularSkiResort root), then
        /// walking up from AppContext.BaseDirectory (tests/LiftBatch/bin/&lt;cfg&gt;/net8.0/; the SlopeBatch
        /// Program.cs:4889-4898 precedent). null when not found (the caller writes ONE FAIL).</summary>
        internal static string RepoFile(string rel)
        {
            string native = rel.Replace('/', Path.DirectorySeparatorChar);
            string p = Path.Combine(Directory.GetCurrentDirectory(), native);
            if (File.Exists(p))
                return p;
            for (DirectoryInfo d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                p = Path.Combine(d.FullName, native);
                if (File.Exists(p))
                    return p;
            }
            return null;
        }

        /// <summary>C# source with comments removed (strings, verbatim strings and interpolation holes respected): a port
        /// of mod/SkiResortUI/tools/check-bundle.js stripCsComments (:796-845). Kept for the hub group's R16 twin checks.</summary>
        internal static string StripCsComments(string src)
        {
            int n = src.Length;
            var sb = new StringBuilder(n);
            int i = 0;
            while (i < n)
            {
                char c = src[i];
                if (c == '/' && At(src, i + 1) == '/')
                {
                    while (i < n && src[i] != '\n')
                        i++;
                    continue;
                }
                if (c == '/' && At(src, i + 1) == '*')
                {
                    int e = src.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    int stop = e < 0 ? n : e + 2;
                    for (int k = i; k < stop; k++)
                        sb.Append(src[k] == '\n' ? '\n' : ' ');   // keep line numbers
                    i = stop;
                    continue;
                }
                if (IsStrStart(src, i))
                {
                    int e = ReadString(src, i);
                    sb.Append(src, i, e - i);
                    i = e;
                    continue;
                }
                if (c == '\'')
                {
                    int e = ReadChar(src, i);
                    sb.Append(src, i, e - i);
                    i = e;
                    continue;
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static char At(string s, int j)
        {
            return j >= 0 && j < s.Length ? s[j] : '\0';
        }

        private static bool IsStrStart(string s, int j)
        {
            char a = At(s, j), b = At(s, j + 1);
            return a == '"' || ((a == '$' || a == '@') && (b == '"' || ((b == '$' || b == '@') && At(s, j + 2) == '"')));
        }

        private static int ReadChar(string s, int j)   // 'x' / '\n' / '\''
        {
            int k = j + 1;
            while (k < s.Length && s[k] != '\'' && s[k] != '\n')
                k += s[k] == '\\' ? 2 : 1;
            return Math.Min(k + 1, s.Length);
        }

        private static int ReadString(string s, int j)   // the index just past the string starting at j
        {
            bool verbatim = false, interp = false;
            while (At(s, j) == '$' || At(s, j) == '@')
            {
                if (s[j] == '$')
                    interp = true;
                else
                    verbatim = true;
                j++;
            }
            j++;   // opening quote
            int depth = 0;
            while (j < s.Length)
            {
                char c = s[j];
                if (depth == 0)
                {
                    if (!verbatim && c == '\\')
                    {
                        j += 2;
                        continue;
                    }
                    if (c == '"')
                    {
                        if (verbatim && At(s, j + 1) == '"')
                        {
                            j += 2;
                            continue;
                        }
                        return j + 1;
                    }
                    if (interp && c == '{')
                    {
                        if (At(s, j + 1) == '{')
                        {
                            j += 2;
                            continue;
                        }
                        depth = 1;
                        j++;
                        continue;
                    }
                    j++;
                }
                else
                {
                    if (IsStrStart(s, j))
                    {
                        j = ReadString(s, j);
                        continue;
                    }
                    if (c == '\'')
                    {
                        j = ReadChar(s, j);
                        continue;
                    }
                    if (c == '{')
                        depth++;
                    else if (c == '}')
                        depth--;
                    j++;
                }
            }
            return Math.Min(j, s.Length);
        }
    }
}
