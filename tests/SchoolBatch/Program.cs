// SchoolBatch — T100 (the ski school as a TOP-LEVEL building; T10 D1 option A, DECISIONS 308;
// docs/research/lodges-top-level-spec.md (b); report docs/research/field/2026-10-19/school-top-level-t100.md).
// Run from the ModularSkiResort root:
//   dotnet run --project tests/SchoolBatch
// Exit 0 = every check passed. Created 2026-10-19.
//
//   L  attribution (SchoolLinkRules.Resolve = SkiResortIndex.TryResolve, TetherMeters 5000): L1 nearest sign, L2 5000.00 m in,
//      L3 5000.01 m out, L4 NaN never links, L5 an exact tie goes to the lower (Index, Version), L6 the sign deleted -> not
//      linked, L7 a nearer new sign takes the school (not sticky), L8 no sign at all.
//   C  the one count rule (SchoolLinkRules.Count): C1 active + live, C2 Inactive, C3 not live, C4 a child and a top-level
//      school in one area (2 listed, active), C5 an inactive child + an active top-level school.
//   I  THE INVARIANT: a linked top-level school yields the same slot facts as the child did, hence the same
//      SkiResortScore.Result (all fields, Equals), SchoolValue, ski-day SchoolFactor, lessons and HubProblems rows; over
//      four fixtures (green counted / no counted green / no runs / school Inactive). I5: an unlinked school = no school.
//   D  the link-log diff (SchoolLinkRules.Diff): Linked, Relinked, Unlinked (new and lost), Removed, distance-only = no
//      line, stable order, empty before -> one line per school (the per-load re-log).
//   P  the SkiSchool.Prefab mirror gate (gamedata-mirror): no ServiceUpgrade; ServiceObject (Parks and Recreation),
//      PlaceableObject cost / XP = the catalog row's (80,000 / 150), CityServiceBuilding, Park kept (D4), Workplace 6 =
//      the row's jobs, the mesh CID unchanged, ObjectSubObjects 27 rows, every declared $type id unique.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using SkiResortMod.Systems;
using Unity.Mathematics;
using Score = SkiResortMod.Systems.SkiResortScore;

namespace SchoolBatch
{
    public static class Program
    {
        private const float Tether = 5000f;   // SkiRunOwnershipSystem.TetherMeters (not linked here: GameSystemBase file)
        private static int s_Pass, s_Fail;

        private static bool Check(bool ok, string id, string what)
        {
            if (ok)
                s_Pass++;
            else
                s_Fail++;
            Console.WriteLine((ok ? "PASS  " : "FAIL  ") + id + "  " + what);
            return ok;
        }

        public static int Main()
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            Attribution();
            CountRule();
            Invariant();
            DiffLog();
            PrefabGate();
            Console.WriteLine($"SchoolBatch: {s_Pass} passed, {s_Fail} failed");
            return s_Fail == 0 ? 0 : 1;
        }

        private static readonly EntityRef SignA = new EntityRef(10, 1), SignB = new EntityRef(20, 1), SignC = new EntityRef(5, 3);
        private static readonly EntityRef School1 = new EntityRef(100, 1), School2 = new EntityRef(101, 1), School3 = new EntityRef(99, 2);

        private static SkiResortIndex Index(params (EntityRef sign, float x, float z)[] signs)
        {
            var index = new SkiResortIndex();
            foreach (var s in signs)
                index.Add(s.sign, new float2(s.x, s.z));
            return index;
        }

        // ── L ────────────────────────────────────────────────────────────────────────────────
        private static void Attribution()
        {
            SkiResortIndex two = Index((SignA, 0f, 0f), (SignB, 8000f, 0f));
            SchoolLinkRules.Link l1 = SchoolLinkRules.Resolve(two, new float2(812f, 300f), Tether);
            Check(l1.Linked && l1.Sign == SignA && Math.Abs(l1.Distance - 865.64f) < 0.01f, "L1", $"nearest sign A at {l1.Distance:F2} m");

            SkiResortIndex one = Index((SignA, 0f, 0f));
            SchoolLinkRules.Link l2 = SchoolLinkRules.Resolve(one, new float2(5000f, 0f), Tether);
            Check(l2.Linked && l2.Sign == SignA, "L2", "5000.00 m is in");
            SchoolLinkRules.Link l3 = SchoolLinkRules.Resolve(one, new float2(5000.01f, 0f), Tether);
            Check(!l3.Linked && l3.Sign.IsNull && l3.Distance > 5000f && !float.IsInfinity(l3.Distance), "L3",
                  $"5000.01 m is out (distance kept for the log: {l3.Distance:F2})");
            SchoolLinkRules.Link l4 = SchoolLinkRules.Resolve(one, new float2(float.NaN, 0f), Tether);
            Check(!l4.Linked && float.IsPositiveInfinity(l4.Distance), "L4", "NaN position never links, distance +inf");

            SkiResortIndex tie = Index((SignB, 100f, 0f), (SignA, -100f, 0f));
            SchoolLinkRules.Link l5 = SchoolLinkRules.Resolve(tie, new float2(0f, 0f), Tether);
            Check(l5.Sign == SignA, "L5", "exact tie -> the lower (Index, Version) sign A");

            SkiResortIndex gone = Index((SignB, 8000f, 0f));   // A bulldozed: the next index has only B
            SchoolLinkRules.Link l6 = SchoolLinkRules.Resolve(gone, new float2(812f, 300f), Tether);
            Check(!l6.Linked, "L6", $"sign deleted -> not linked (B at {l6.Distance:F0} m is beyond the tether)");

            SkiResortIndex nearer = Index((SignA, 0f, 0f), (SignC, 700f, 300f));
            SchoolLinkRules.Link l7 = SchoolLinkRules.Resolve(nearer, new float2(812f, 300f), Tether);
            Check(l7.Sign == SignC, "L7", "a nearer new sign takes the school (not sticky)");

            SchoolLinkRules.Link l8 = SchoolLinkRules.Resolve(new SkiResortIndex(), new float2(0f, 0f), Tether);
            SchoolLinkRules.Link l8b = SchoolLinkRules.Resolve(null, new float2(0f, 0f), Tether);
            Check(!l8.Linked && !l8b.Linked, "L8", "no sign (or no index) -> not linked");
        }

        // ── C ────────────────────────────────────────────────────────────────────────────────
        private static void CountRule()
        {
            int n = 0; bool on = false;
            SchoolLinkRules.Count(ref n, ref on, active: true, live: true);
            Check(n == 1 && on, "C1", "active + live: 1 listed, on");
            n = 0; on = false;
            SchoolLinkRules.Count(ref n, ref on, active: false, live: true);
            Check(n == 1 && !on, "C2", "Inactive: 1 listed, off");
            n = 0; on = false;
            SchoolLinkRules.Count(ref n, ref on, active: true, live: false);
            Check(n == 1 && !on, "C3", "not live (Deleted / Temp): 1 listed, off");
            n = 0; on = false;
            SchoolLinkRules.Count(ref n, ref on, active: true, live: true);   // child entry
            SchoolLinkRules.Count(ref n, ref on, active: true, live: true);   // top-level linked
            Check(n == 2 && on, "C4", "a child and a top-level school: 2 listed, on (SchoolActive is a flag, no double value)");
            n = 0; on = false;
            SchoolLinkRules.Count(ref n, ref on, active: false, live: true);
            SchoolLinkRules.Count(ref n, ref on, active: true, live: true);
            Check(n == 2 && on, "C5", "inactive child + active top-level: on");
        }

        // ── I ────────────────────────────────────────────────────────────────────────────────

        /// <summary>The slot facts of area A, child path: the sign's InstalledUpgrade entry (Inactive bit = !active).</summary>
        private static (int schools, bool on) ChildPath(bool active)
        {
            int n = 0; bool on = false;
            SchoolLinkRules.Count(ref n, ref on, active, live: true);
            return (n, on);
        }

        /// <summary>The slot facts of area A, top-level path: resolve the school, count it into the slot it links to.</summary>
        private static (int schools, bool on, bool linked) TopLevelPath(SkiResortIndex signs, float2 xz, bool active)
        {
            int n = 0; bool on = false;
            SchoolLinkRules.Link link = SchoolLinkRules.Resolve(signs, xz, Tether);
            if (link.Linked && link.Sign == SignA)
                SchoolLinkRules.Count(ref n, ref on, active, live: true);
            return (n, on, link.Linked);
        }

        private static Score.RunInput Run(byte band, double areaM2, double top, double bottom)
        {
            return new Score.RunInput
            {
                Kind = Score.KindGroomed, State = Score.BandState.Rated, Band = band, AreaM2 = areaM2, Served = true,
                OpenFactor = 1.0, TopM = top, BottomM = bottom,
            };
        }

        private static Score.AreaInput Area(bool schoolActive)
        {
            return new Score.AreaInput
            {
                ActiveLodges = 1, InactiveLodges = 0, Efficiency = 1.0,
                Settings = new Score.ScoreSettings { K = 4, BackcountryPercent = 40, SnowGating = true },
                Lodges = new List<Score.LodgeInput> { new Score.LodgeInput { LodgeIndex = 0 } },
                SchoolActive = schoolActive,
            };
        }

        /// <summary>The stats system's problem input for one area (SkiResortStatsSystem.cs: SchoolNoGreenRun = hasResult &&
        /// r.SchoolActive && r.RunsCounted > 0 && !r.GreenCounted), the rest fixed.</summary>
        private static List<HubProblem> Problems(in Score.Result r)
        {
            var input = new HubProblemInput
            {
                Lodges = 1, LodgesActive = 1, EfficiencyPct = 100, Runs = r.Runs, AttractionFull = r.AttractionFullEfficiency,
                IfOneLodge = -1, IfLodgesOn = -1, BackcountryPct = 40, LiftRows = 0,
                SchoolNoGreenRun = r.SchoolActive && r.RunsCounted > 0 && !r.GreenCounted,
            };
            var rows = new List<HubProblem>();
            HubProblems.Build(input, rows);
            return rows;
        }

        private static string RowsText(List<HubProblem> rows)
        {
            var parts = new List<string>();
            foreach (HubProblem p in rows)
                parts.Add($"{(int)p.Code}/{p.Severity}/{p.Count}");
            return string.Join(",", parts);
        }

        /// <summary>SkiLiftTicketSystem's lesson gate (slot SchoolActive = known && Result.SchoolActive; lessons only when the
        /// school is on, the share and the price are &gt; 0) over 40 arrivals of 1-6 people on one day.</summary>
        private static int Lessons(bool school)
        {
            int share = SkiMoneyRules.LessonShareDefault;
            int price = SkiMoneyRules.LessonPrice(SkiMoneyRules.LessonMultiplierDefault, 40);
            int total = 0;
            for (int hh = 1; hh <= 40; hh++)
            {
                if (school && share > 0 && price > 0)
                    total += SkiMoneyRules.LessonsTaken(1 + hh % 6, 5000 + hh, 731, share);
            }
            return total;
        }

        private static void Invariant()
        {
            SkiResortIndex signs = Index((SignA, 0f, 0f), (SignB, 8000f, 0f));
            float2 near = new float2(812f, 300f);
            var fixtures = new (string id, List<Score.RunInput> runs, bool active, double schoolValue, bool row25)[]
            {
                ("I1 green counted", new List<Score.RunInput> { Run(Score.BandGreen, 40000, 1800, 1650), Run(Score.BandBlue, 60000, 1900, 1600) },
                    true, SchoolRule.ScoreValue, false),
                ("I2 no counted green", new List<Score.RunInput> { Run(Score.BandBlue, 60000, 1900, 1600), Run(Score.BandBlack, 50000, 2000, 1600) },
                    true, 0.0, true),
                ("I3 no runs", new List<Score.RunInput>(), true, 0.0, false),
                ("I4 school Inactive", new List<Score.RunInput> { Run(Score.BandGreen, 40000, 1800, 1650) }, false, 0.0, false),
            };
            foreach (var f in fixtures)
            {
                var child = ChildPath(f.active);
                var top = TopLevelPath(signs, near, f.active);
                Check(top.linked && child.schools == top.schools && child.on == top.on, f.id + " slot",
                      $"child ({child.schools}, {child.on}) == top-level ({top.schools}, {top.on})");
                Score.Result rc = Score.Compute(Area(child.on), f.runs, new List<Score.LiftInput>(), new List<Score.RunPart>());
                Score.Result rt = Score.Compute(Area(top.on), f.runs, new List<Score.LiftInput>(), new List<Score.RunPart>());
                Check(rc.Equals(rt), f.id + " result", $"Result equal in every field (A {rc.Attraction} / {rt.Attraction}, S {rc.S:F3} / {rt.S:F3})");
                Check(rt.SchoolValue == f.schoolValue && rc.SchoolValue == rt.SchoolValue, f.id + " schoolValue",
                      $"schoolValue {rt.SchoolValue} (expected {f.schoolValue})");
                double fc = SchoolRule.DemandFactorOf(rc.SchoolActive, rc.GreenCounted);
                double ft = SchoolRule.DemandFactorOf(rt.SchoolActive, rt.GreenCounted);
                Check(fc == ft, f.id + " ski days", $"SchoolFactor {fc} == {ft}");
                int lc = Lessons(rc.SchoolActive), lt = Lessons(rt.SchoolActive);
                Check(lc == lt && (f.active ? lt > 0 : lt == 0), f.id + " lessons", $"lessons {lc} == {lt}");
                string pc = RowsText(Problems(rc)), pt = RowsText(Problems(rt));
                bool has25 = pt.Contains(((int)HubProblemCode.SchoolNoGreenRun).ToString() + "/");
                Check(pc == pt && has25 == f.row25, f.id + " problems", $"rows [{pc}] == [{pt}], row 25 {(has25 ? "on" : "off")}");
            }

            // I5: beyond the tether the school is no school for A (the child could not be built there: the 5 km tether).
            var far = TopLevelPath(signs, new float2(-5200f, 0f), true);
            Score.Result rNone = Score.Compute(Area(false), fixtures[0].runs, new List<Score.LiftInput>(), new List<Score.RunPart>());
            Score.Result rFar = Score.Compute(Area(far.on), fixtures[0].runs, new List<Score.LiftInput>(), new List<Score.RunPart>());
            Check(!far.linked && far.schools == 0 && rFar.Equals(rNone) && rFar.SchoolValue == 0.0, "I5 not linked",
                  "an unlinked school counts for no area (= no school)");
        }

        // ── D ────────────────────────────────────────────────────────────────────────────────
        private static SchoolLinkRules.Link L(EntityRef sign, float d) => new SchoolLinkRules.Link { Sign = sign, Distance = d };

        private static void DiffLog()
        {
            var before = new Dictionary<EntityRef, SchoolLinkRules.Link>();
            var after = new Dictionary<EntityRef, SchoolLinkRules.Link>();
            var changes = new List<SchoolLinkRules.Change>();

            after[School1] = L(SignA, 865f);
            after[School2] = L(EntityRef.Null, 6100f);
            SchoolLinkRules.Diff(before, after, changes);
            Check(changes.Count == 2 && changes[0].School == School1 && changes[0].Kind == SchoolLinkRules.ChangeKind.Linked &&
                  changes[1].Kind == SchoolLinkRules.ChangeKind.Unlinked, "D1",
                  "first recompute (or a load): one line per school, Linked / NOT LINKED");

            before = new Dictionary<EntityRef, SchoolLinkRules.Link>(after);
            after[School1] = L(SignA, 400f);   // moved closer to the same sign
            SchoolLinkRules.Diff(before, after, changes);
            Check(changes.Count == 0, "D2", "same sign, other distance (Move It within the area): no line");

            after[School1] = L(SignC, 120f);
            SchoolLinkRules.Diff(before, after, changes);
            Check(changes.Count == 1 && changes[0].Kind == SchoolLinkRules.ChangeKind.Relinked && changes[0].Before.Sign == SignA &&
                  changes[0].After.Sign == SignC, "D3", "a nearer sign: Relinked A -> C");

            after[School1] = L(EntityRef.Null, float.PositiveInfinity);   // its sign bulldozed, nothing else near
            SchoolLinkRules.Diff(before, after, changes);
            Check(changes.Count == 1 && changes[0].Kind == SchoolLinkRules.ChangeKind.Unlinked && changes[0].Before.Sign == SignA,
                  "D4", "sign bulldozed: NOT LINKED, was A");

            after.Remove(School1);
            after[School2] = L(SignB, 300f);
            after[School3] = L(SignB, 900f);
            SchoolLinkRules.Diff(before, after, changes);
            Check(changes.Count == 3 && changes[0].School == School3 && changes[0].Kind == SchoolLinkRules.ChangeKind.Linked &&
                  changes[1].School == School1 && changes[1].Kind == SchoolLinkRules.ChangeKind.Removed && changes[1].Before.Sign == SignA &&
                  changes[2].School == School2 && changes[2].Kind == SchoolLinkRules.ChangeKind.Linked,
                  "D5", "new linked school, removed school, not-linked -> linked; sorted by (Index, Version)");
        }

        // ── P ────────────────────────────────────────────────────────────────────────────────
        private static void PrefabGate()
        {
            string path = Path.Combine("gamedata-mirror", "ImportedData", "SkiSchool", "SkiSchool.Prefab");
            if (!Check(File.Exists(path), "P0", "mirror file " + path + " (run from the ModularSkiResort root)"))
                return;
            string text = File.ReadAllText(path);
            SchoolDef row = SkiCatalog.SchoolByPrefab(SkiCatalog.Names.School);
            Check(row != null, "P0b", "catalog row SkiSchool");
            if (row == null)
                return;
            Check(!text.Contains("Game.Prefabs.ServiceUpgrade, Game"), "P1", "no ServiceUpgrade component (top-level)");
            Check(Regex.IsMatch(text, "Game\\.Prefabs\\.ServiceObject, Game\"[\\s\\S]{0,200}?UnityGUID:5c1f656a5ea20284197a71e4997b4187"),
                  "P2", "ServiceObject = Parks and Recreation (5c1f656a...)");
            Match placeable = Regex.Match(text, "Game\\.Prefabs\\.PlaceableObject, Game\"[\\s\\S]{0,200}?\"m_ConstructionCost\": (\\d+),\\s*\"m_XPReward\": (\\d+)");
            Check(placeable.Success && placeable.Groups[1].Value == row.BuildCost.ToString(CultureInfo.InvariantCulture) &&
                  placeable.Groups[2].Value == "150", "P3",
                  $"PlaceableObject cost {(placeable.Success ? placeable.Groups[1].Value : "?")} = row {row.BuildCost}, XP {(placeable.Success ? placeable.Groups[2].Value : "?")} = 150");
            Check(text.Contains("Game.Prefabs.CityServiceBuilding, Game"), "P4", "CityServiceBuilding (D2: upkeep billed)");
            Check(text.Contains("Game.Prefabs.Park, Game"), "P5", "Park kept (D4)");
            Match work = Regex.Match(text, "\"m_Workplaces\": (\\d+)");
            Check(work.Success && work.Groups[1].Value == row.Jobs.ToString(CultureInfo.InvariantCulture), "P6",
                  $"Workplace {(work.Success ? work.Groups[1].Value : "?")} = row jobs {row.Jobs}");
            Match upkeep = Regex.Match(text, "\"m_Upkeep\": (\\d+)");
            Check(upkeep.Success && upkeep.Groups[1].Value == row.Upkeep.ToString(CultureInfo.InvariantCulture), "P7",
                  $"ServiceConsumption upkeep {(upkeep.Success ? upkeep.Groups[1].Value : "?")} = row {row.Upkeep}");
            Check(text.Contains("\"m_Mesh\": $fstrref:\"CID:b6da11948419194976357fa66602ffb9\""), "P8", "mesh CID unchanged (b6da1194...)");
            Match subs = Regex.Match(text, "ObjectSubObjectInfo\\[\\], Game\",\\s*\"\\$rlength\": (\\d+)");
            Check(subs.Success && subs.Groups[1].Value == "27", "P9", $"ObjectSubObjects rows {(subs.Success ? subs.Groups[1].Value : "?")} = 27");
            var ids = new HashSet<string>();
            bool unique = true;
            foreach (Match m in Regex.Matches(text, "\"\\$type\": \"(\\d+)\\|"))
                unique &= ids.Add(m.Groups[1].Value);
            Check(unique, "P10", $"{ids.Count} declared $type ids, each declared once");
        }
    }
}
