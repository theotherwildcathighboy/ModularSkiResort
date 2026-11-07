// Tests for the top-level resort buildings (the ski school, the five lodges and both patrol huts). Run from the ModularSkiResort
// root: dotnet run --project tests/BuildingBatch. Exit 0 = every check passed. Groups:
// - L: sign attribution (nearest sign within TetherMeters 5000, a tie goes to the lower (Index, Version))
// - C: the one count rule
// - I: a linked school's slot facts and what follows
// - D: the link-log diff
// - E: AreaEfficiency
// - J: mountain jobs on the sign (the sign prefab must keep its Workplace block:
//   WorkProviderSystem.cs:133 needs WorkplaceData or it liquidates the staff)
// - P: the gamedata-mirror gate over all eight buildings
// Oracles are literal values. The mirror gate reads the shipped prefab JSON, not the code under test.
// Engine notes: EngineNotes/BuildingBatch.Program.md

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using SkiResortMod.Systems;
using Unity.Mathematics;
using Score = SkiResortMod.Systems.SkiResortScore;

namespace BuildingBatch
{
    public static class Program
    {
        private const float Tether = 5000f; // SkiRunOwnershipSystem.TetherMeters (not linked here: GameSystemBase file)
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
            ToRepoRoot();
            Attribution();
            CountRule();
            Invariant();
            DiffLog();
            EfficiencyRule();
            MountainJobs();
            OffSeasonTests.Run(Check);
            LodgeSupplyTests.Run(Check);
            MergeTests.Run(Check);
            foreach (Gate g in Gates)
                PrefabGate(g);
            Console.WriteLine($"BuildingBatch: {s_Pass} passed, {s_Fail} failed");
            return s_Fail == 0 ? 0 : 1;
        }

        /// <summary>The mirror paths are repo-relative (gamedata-mirror/ImportedData): when the current directory has none, move to the
        /// first parent of AppContext.BaseDirectory (tests/BuildingBatch/bin/&lt;cfg&gt;/net8.0/) that has one, so a run started from
        /// tests/ or mod/SkiResortUI checks the same files as one from the root.</summary>
        private static void ToRepoRoot()
        {
            if (Directory.Exists("gamedata-mirror"))
                return;
            for (DirectoryInfo d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "gamedata-mirror")))
                {
                    Directory.SetCurrentDirectory(d.FullName);
                    return;
                }
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
            // The bus depot alone has no tether: the nearest live sign at any distance. No sign and a NaN position still mean not linked.
            SchoolLinkRules.Link d1 = SchoolLinkRules.Resolve(two, new float2(58000f, 0f), SchoolLinkRules.DepotMaxDistance);
            Check(d1.Linked && d1.Sign == SignB && Math.Abs(d1.Distance - 50000f) < 0.5f, "L3b", $"a depot 50 km out links to the nearest sign, {d1.Distance:F0} m");
            Check(!SchoolLinkRules.Resolve(two, new float2(58000f, 0f), Tether).Linked, "L3c", "the same spot is out for the tethered buildings");
            Check(!SchoolLinkRules.Resolve(new SkiResortIndex(), new float2(0f, 0f), SchoolLinkRules.DepotMaxDistance).Linked, "L3d", "a depot with no sign is not linked");
            Check(!SchoolLinkRules.Resolve(one, new float2(float.NaN, 0f), SchoolLinkRules.DepotMaxDistance).Linked, "L3e", "a depot at NaN is not linked");
            SchoolLinkRules.Link l4 = SchoolLinkRules.Resolve(one, new float2(float.NaN, 0f), Tether);
            Check(!l4.Linked && float.IsPositiveInfinity(l4.Distance), "L4", "NaN position never links, distance +inf");

            SkiResortIndex tie = Index((SignB, 100f, 0f), (SignA, -100f, 0f));
            SchoolLinkRules.Link l5 = SchoolLinkRules.Resolve(tie, new float2(0f, 0f), Tether);
            Check(l5.Sign == SignA, "L5", "exact tie -> the lower (Index, Version) sign A");

            SkiResortIndex gone = Index((SignB, 8000f, 0f)); // A bulldozed: the next index has only B
            SchoolLinkRules.Link l6 = SchoolLinkRules.Resolve(gone, new float2(812f, 300f), Tether);
            Check(!l6.Linked, "L6", $"sign deleted -> not linked (B at {l6.Distance:F0} m is beyond the tether)");

            SkiResortIndex nearer = Index((SignA, 0f, 0f), (SignC, 700f, 300f));
            SchoolLinkRules.Link l7 = SchoolLinkRules.Resolve(nearer, new float2(812f, 300f), Tether);
            Check(l7.Sign == SignC, "L7", "a nearer new sign takes the school (not sticky)");

            SchoolLinkRules.Link l8 = SchoolLinkRules.Resolve(new SkiResortIndex(), new float2(0f, 0f), Tether);
            SchoolLinkRules.Link l8b = SchoolLinkRules.Resolve(null, new float2(0f, 0f), Tether);
            Check(!l8.Linked && !l8b.Linked, "L8", "no sign (or no index) -> not linked");
        }

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
            SchoolLinkRules.Count(ref n, ref on, active: true, live: true); // child entry
            SchoolLinkRules.Count(ref n, ref on, active: true, live: true); // top-level linked
            Check(n == 2 && on, "C4", "a child and a top-level school: 2 listed, on (SchoolActive is a flag, no double value)");
            n = 0; on = false;
            SchoolLinkRules.Count(ref n, ref on, active: false, live: true);
            SchoolLinkRules.Count(ref n, ref on, active: true, live: true);
            Check(n == 2 && on, "C5", "inactive child + active top-level: on");
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

        /// <summary>SkiLiftTicketSystem's lesson gate (slot SchoolActive = known && Result.SchoolActive. Lessons only when the school
        /// is on, the share and the price are &gt; 0) over 40 arrivals of 1-6 people on one day.</summary>
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
                var top = TopLevelPath(signs, near, f.active);
                Check(top.linked && top.schools == 1 && top.on == f.active, f.id + " slot",
                      $"top-level ({top.schools}, {top.on}), linked {top.linked}");
                Score.Result rt = Score.Compute(Area(top.on), f.runs, new List<Score.LiftInput>(), new List<Score.RunPart>());
                Check(rt.SchoolValue == f.schoolValue, f.id + " schoolValue", $"schoolValue {rt.SchoolValue} (expected {f.schoolValue})");
                int lt = Lessons(rt.SchoolActive);
                Check(f.active ? lt > 0 : lt == 0, f.id + " lessons", $"lessons {lt}");
                string pt = RowsText(Problems(rt));
                bool has25 = pt.Contains(((int)HubProblemCode.SchoolNoGreenRun).ToString() + "/");
                Check(has25 == f.row25, f.id + " problems", $"rows [{pt}], row 25 {(has25 ? "on" : "off")}");
            }

            // I5: beyond the tether the school is no school for A.
            var far = TopLevelPath(signs, new float2(-5200f, 0f), true);
            Score.Result rNone = Score.Compute(Area(false), fixtures[0].runs, new List<Score.LiftInput>(), new List<Score.RunPart>());
            Score.Result rFar = Score.Compute(Area(far.on), fixtures[0].runs, new List<Score.LiftInput>(), new List<Score.RunPart>());
            Check(!far.linked && far.schools == 0 && rFar.Equals(rNone) && rFar.SchoolValue == 0.0, "I5 not linked",
                  "an unlinked school counts for no area (= no school)");
        }

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
            after[School1] = L(SignA, 400f); // moved closer to the same sign
            SchoolLinkRules.Diff(before, after, changes);
            Check(changes.Count == 0, "D2", "same sign, other distance (Move It within the area): no line");

            after[School1] = L(SignC, 120f);
            SchoolLinkRules.Diff(before, after, changes);
            Check(changes.Count == 1 && changes[0].Kind == SchoolLinkRules.ChangeKind.Relinked && changes[0].Before.Sign == SignA &&
                  changes[0].After.Sign == SignC, "D3", "a nearer sign: Relinked A -> C");

            after[School1] = L(EntityRef.Null, float.PositiveInfinity); // its sign bulldozed, nothing else near
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

        private static bool Near(float a, float b) => Math.Abs(a - b) < 1e-5f;

        private static void EfficiencyRule()
        {
            Check(Near(SchoolLinkRules.AreaEfficiency(0.87f, 0.0, 0.0), 0.87f), "E1", "no active top-level lodge: the sign's own efficiency");
            // A-frame (B 20) at 0.60 + Grand (B 40) at 0.90: mean (12 + 36) / 60 = 0.80, x sign 0.95 = 0.76
            float e2 = SchoolLinkRules.AreaEfficiency(0.95f, 20 * 0.60 + 40 * 0.90, 60.0);
            Check(Near(e2, 0.76f), "E2", $"tier-weighted mean 0.80 x sign 0.95 = {e2} (0.76)");
            Check(SchoolLinkRules.AreaEfficiency(1f, 20 * 0.0, 20.0) == 0f, "E3", "the only lodge at 0: area 0");
            Check(SchoolLinkRules.AreaEfficiency(0f, 20 * 1.0, 20.0) == 0f, "E4", "sign at 0 (Destroyed, D9): area 0");
            float e5a = SchoolLinkRules.AreaEfficiency(1f, 16 * 0.333, 16.0);
            float e5b = SchoolLinkRules.AreaEfficiency(0.02f, 16 * 0.2, 16.0);
            Check(Near(e5a, 0.33f) && Near(e5b, 0.01f), "E5", $"rounded to 0.01 ({e5a}), floored at 0.01 ({e5b})");
            // E6: the school and patrol huts have no tier base (weight 0): with only them the area is the sign's.
            Check(Near(SchoolLinkRules.AreaEfficiency(0.9f, 0.0 * 0.4, 0.0), 0.9f), "E6", "school / patrol weigh 0: the sign's efficiency");
        }

        private static Dictionary<EntityRef, int> Sum(params (EntityRef sign, int jobs, bool live)[] rows)
        {
            var signs = new List<EntityRef>(); var jobs = new List<int>(); var live = new List<bool>();
            foreach (var r in rows) { signs.Add(r.sign); jobs.Add(r.jobs); live.Add(r.live); }
            var output = new Dictionary<EntityRef, int>();
            MountainJobsRules.SumBySign(signs, jobs, live, output);
            return output;
        }

        private static int At(Dictionary<EntityRef, int> d, EntityRef sign) => d.TryGetValue(sign, out int n) ? n : 0;

        private static void MountainJobs()
        {
            LodgeDef hut = null, summit = null, mid = null;
            foreach (LodgeDef l in SkiCatalog.Lodges) { if (l.Id == "hut") hut = l; if (l.Id == "summit") summit = l; if (l.Id == "mid") mid = l; }
            PatrolDef ph = SkiCatalog.PatrolByPrefab("SkiPatrolHut"), pq = SkiCatalog.PatrolByPrefab("SkiPatrolHQ");

            // J1: one sign carries every live linked mountain building. A base lodge (own Workplace) adds 0.
            var j1 = Sum((SignA, summit.SignJobs, true), (SignA, hut.SignJobs, true), (SignA, ph.SignJobs, true), (SignA, mid.SignJobs, true));
            Check(At(j1, SignA) == 14 && j1.Count == 1, "J1", $"summit 8 + hut 4 + patrol hut 2 + mid 0 on sign A = {At(j1, SignA)} (14)");

            // J2: the summit relinks to B (Move It, or a nearer new sign): A drops 8, B gains 8.
            var j2 = Sum((SignB, summit.SignJobs, true), (SignA, hut.SignJobs, true), (SignA, ph.SignJobs, true));
            Check(At(j2, SignA) == 6 && At(j2, SignB) == 8, "J2", $"relink: A {At(j2, SignA)} (6), B {At(j2, SignB)} (8)");
            Check(MountainJobsRules.NeedsWrite(true, 14, 6) && MountainJobsRules.NeedsWrite(false, 0, 8) &&
                  !MountainJobsRules.StartsGrace(true, 14, 6) && MountainJobsRules.StartsGrace(false, 0, 8), "J2b",
                  "relink writes both signs; only B (new workplaces) gets the vanilla grace");

            // J3: not linked (no sign within 5 km) and removed (not live): nothing summed. A carrier sign whose target fell to 0 is
            // written to 0 (the component stays, the game lays the staff off).
            var j3 = Sum((EntityRef.Null, summit.SignJobs, true), (SignA, hut.SignJobs, false));
            Check(j3.Count == 0, "J3", "not linked + removed building: no sign has a target");
            Check(MountainJobsRules.NeedsWrite(true, 6, 0) && !MountainJobsRules.NeedsWrite(false, 0, 0), "J3b",
                  "a carrier falls to 0 (written); a sign that never carried any stays untouched");

            // J4: the write rule table.
            Check(!MountainJobsRules.NeedsWrite(true, 8, 8) && MountainJobsRules.NeedsWrite(true, 0, 8) && MountainJobsRules.StartsGrace(true, 0, 8) &&
                  !MountainJobsRules.StartsGrace(true, 8, 0), "J4", "equal = no write; 0 -> 8 writes with grace; 8 -> 0 without grace");

            // J5: the catalog rows: the old Workplace numbers, carried only by roadless rows with no own jobs.
            Check(hut.SignJobs == 4 && summit.SignJobs == 8 && ph.SignJobs == 2 && pq.SignJobs == 2, "J5",
                  $"SignJobs hut {hut.SignJobs} summit {summit.SignJobs} patrol hut {ph.SignJobs} patrol HQ {pq.SignJobs} (4 / 8 / 2 / 2)");
            bool rowsOk = true;
            foreach (LodgeDef l in SkiCatalog.Lodges)
                rowsOk &= l.SignJobs == 0 || (l.Jobs == 0 && l.RoadRule != LodgeRoadRule.Base);
            rowsOk &= ph.Jobs == 0 && pq.Jobs == 0 && SkiCatalog.SchoolByPrefab("SkiSchool").Jobs > 0;
            Check(rowsOk, "J5b", "a SignJobs row has Jobs 0 and is not a Base road row (else it would double-count)");

            // J6: the efficiency slice. Sign 100 % except staffing 0.5. Area = summit (B 40) + A-frame (B 20, own efficiency 1). Staffing
            // on the summit slice only, the sign term without it: (40 x 0.5 + 20 x 1) / 60 x 1.0 = 0.67.
            float signTerm = MountainJobsRules.SignTerm(0.5f, 1f, true);
            float summitEff = MountainJobsRules.MountainEfficiency(1f, 0.5f);
            float a6 = SchoolLinkRules.AreaEfficiency(signTerm, 40 * summitEff + 20 * 1.0, 60.0);
            Check(Near(a6, 0.67f), "J6", $"staffing 0.5 on the summit slice: area {a6} (0.67; counting it twice would give 0.33)");
            Check(Near(MountainJobsRules.SignTerm(0.5f, 1f, false), 0.5f), "J6b",
                  "no mountain lodge in the mean (patrol huts only, weight 0): staffing stays on the sign term (as before T113)");
            Check(MountainJobsRules.MountainEfficiency(0.9f, float.NaN) == 0f && MountainJobsRules.MountainEfficiency(0.9f, -1f) == 0f,
                  "J6c", "non-finite / negative staffing reads 0");

            // J7: the four mountain buildings by one sign = 16, the jobs they had as ServiceUpgrade children.
            var j7 = Sum((SignA, summit.SignJobs, true), (SignA, hut.SignJobs, true), (SignA, pq.SignJobs, true), (SignA, ph.SignJobs, true));
            Check(At(j7, SignA) == 16, "J7", $"the four mountain buildings on one sign: {At(j7, SignA)} jobs (16)");
            Check(MountainJobsRules.Breakdown(new[] { "summit 1:1", "hut 2:1" }, new[] { 8, 4 }) == "summit 1:1 8 + hut 2:1 4" &&
                  MountainJobsRules.Breakdown(new string[0], new int[0]) == "none linked", "J7b", "log breakdown words");

            // J8: the sign prefab keeps WorkplaceData (Workplace block, m_Workplaces 0).
            string gate = Path.Combine("gamedata-mirror", "ImportedData", "SkiResortGate", "SkiResortGate.Prefab");
            string text = File.Exists(gate) ? File.ReadAllText(gate) : string.Empty;
            Check(text.Contains("Game.Prefabs.Workplace, Game") && Num(text, "m_Workplaces") == "0", "J8",
                  $"SkiResortGate keeps its Workplace block (m_Workplaces {Num(text, "m_Workplaces")}): WorkProviderSystem.cs:133 reads its WorkplaceData");
        }

        private sealed class Gate
        {
            public string Prefab;
            public int Xp, Doors;
            public bool Roadless;
            public Gate(string prefab, int xp, int doors, bool roadless) { Prefab = prefab; Xp = xp; Doors = doors; Roadless = roadless; }
        }

        // XP = the old ServiceUpgrade m_XPReward (unchanged by the conversion). Doors = Pedestrian Spawn rows.
        private static readonly Gate[] Gates =
        {
            new Gate("SkiAFrameDayLodge", 100, 1, false),
            new Gate("SkiLodgeHut", 100, 0, true),
            new Gate("SkiLodgeMid", 200, 5, false),
            new Gate("SkiLodgeSummit", 400, 0, true),
            new Gate("SkiLodgeGrand", 400, 5, false),
            new Gate("SkiBusDepot", 60, 3, false),
            new Gate("SkiSchool", 150, 4, false),
            new Gate("SkiPatrolHut", 50, 0, true),
            new Gate("SkiPatrolHQ", 50, 0, true),
        };

        private const string SpawnGuid = "c30226ded1fc8d64abee5d0bd2554bc4"; // Pedestrian Spawn Location
        private static HashSet<string> s_MeshCids;

        /// <summary>Every .Prefab.cid content under the mirror (the mesh prefabs' CIDs), read once.</summary>
        private static HashSet<string> MeshCids()
        {
            if (s_MeshCids != null)
                return s_MeshCids;
            s_MeshCids = new HashSet<string>();
            foreach (string f in Directory.EnumerateFiles(Path.Combine("gamedata-mirror", "ImportedData"), "*.Prefab.cid", SearchOption.AllDirectories))
                s_MeshCids.Add(File.ReadAllText(f).Trim());
            return s_MeshCids;
        }

        private static bool Row(string prefab, out int cost, out int jobs, out int upkeep)
        {
            cost = jobs = upkeep = -1;
            for (int i = 0; i < SkiCatalog.Lodges.Count; i++)
            {
                if (SkiCatalog.Lodges[i].Prefab == prefab)
                {
                    cost = SkiCatalog.Lodges[i].BuildCost; jobs = SkiCatalog.Lodges[i].Jobs; upkeep = SkiCatalog.Lodges[i].Upkeep;
                    return true;
                }
            }
            SchoolDef s = SkiCatalog.SchoolByPrefab(prefab);
            if (s != null) { cost = s.BuildCost; jobs = s.Jobs; upkeep = s.Upkeep; return true; }
            PatrolDef p = SkiCatalog.PatrolByPrefab(prefab);
            if (p != null) { cost = p.BuildCost; jobs = p.Jobs; upkeep = p.Upkeep; return true; }
            DepotDef d = SkiCatalog.DepotByPrefab(prefab);
            if (d != null) { cost = d.BuildCost; jobs = 0; upkeep = d.Upkeep; return true; }
            return false;
        }

        private static string Num(string text, string field)
        {
            Match m = Regex.Match(text, "\"" + field + "\": (-?[0-9.]+)");
            return m.Success ? m.Groups[1].Value : "?";
        }

        private static void PrefabGate(Gate g)
        {
            string id = g.Prefab + " ";
            string path = Path.Combine("gamedata-mirror", "ImportedData", g.Prefab, g.Prefab + ".Prefab");
            if (!Check(File.Exists(path), id + "P0", "mirror file " + path + " (run from the ModularSkiResort root)"))
                return;
            string text = File.ReadAllText(path);
            if (!Check(Row(g.Prefab, out int cost, out int jobs, out int upkeep), id + "P0b", "catalog row " + g.Prefab))
                return;
            string I(int v) => v.ToString(CultureInfo.InvariantCulture);
            Check(!text.Contains("Game.Prefabs.ServiceUpgrade, Game"), id + "P1", "no ServiceUpgrade component (top-level)");
            Check(Regex.IsMatch(text, "Game\\.Prefabs\\.ServiceObject, Game\"[\\s\\S]{0,200}?UnityGUID:5c1f656a5ea20284197a71e4997b4187"),
                  id + "P2", "ServiceObject = Parks and Recreation (5c1f656a...)");
            Match placeable = Regex.Match(text, "Game\\.Prefabs\\.PlaceableObject, Game\"[\\s\\S]{0,200}?\"m_ConstructionCost\": (\\d+),\\s*\"m_XPReward\": (\\d+)");
            Check(placeable.Success && placeable.Groups[1].Value == I(cost) && placeable.Groups[2].Value == I(g.Xp), id + "P3",
                  $"PlaceableObject cost {(placeable.Success ? placeable.Groups[1].Value : "?")} = row {cost}, XP {(placeable.Success ? placeable.Groups[2].Value : "?")} = {g.Xp}");
            Check(text.Contains("Game.Prefabs.CityServiceBuilding, Game"), id + "P4", "CityServiceBuilding (D2: upkeep billed)");
            Check(text.Contains("Game.Prefabs.Park, Game"), id + "P5", "Park kept (D4)");
            if (jobs == 0)
                Check(!text.Contains("Game.Prefabs.Workplace, Game"), id + "P6", "row jobs 0: no Workplace block (demands no workers)");
            else
                Check(Num(text, "m_Workplaces") == I(jobs), id + "P6", $"Workplace {Num(text, "m_Workplaces")} = row jobs {jobs}");
            bool upkeepOk = Num(text, "m_Upkeep") == I(upkeep);
            string demand = Num(text, "m_ElectricityConsumption") + " / " + Num(text, "m_WaterConsumption") + " / " + Num(text, "m_GarbageAccumulation");
            bool demandOk = !g.Roadless || demand == "0 / 0 / 0";
            Check(upkeepOk && demandOk, id + "P7", $"upkeep {Num(text, "m_Upkeep")} = row {upkeep}; power / water / garbage {demand}" +
                  (g.Roadless ? " (roadless: must be 0 / 0 / 0)" : ""));
            Match mesh = Regex.Match(text, "\"m_Mesh\": \\$fstrref:\"CID:([0-9a-f]{32})\"");
            Check(mesh.Success && MeshCids().Contains(mesh.Groups[1].Value), id + "P8",
                  $"mesh CID {(mesh.Success ? mesh.Groups[1].Value.Substring(0, 8) : "?")}... resolves to a mesh prefab .cid in the mirror");
            int doors = Regex.Matches(text, "UnityGUID:" + SpawnGuid).Count;
            Match subs = Regex.Match(text, "ObjectSubObjectInfo\\[\\], Game\",\\s*\"\\$rlength\": (\\d+)");
            int rows = Regex.Matches(text, "\"m_Object\": \\$fstrref:").Count;
            Check(doors == g.Doors && subs.Success && subs.Groups[1].Value == I(rows), id + "P9",
                  $"door spawn rows {doors} = {g.Doors}; sub-object $rlength {(subs.Success ? subs.Groups[1].Value : "?")} = rows {rows}");
            var ids = new HashSet<string>();
            bool unique = true;
            foreach (Match m in Regex.Matches(text, "\"\\$type\": \"(\\d+)\\|"))
                unique &= ids.Add(m.Groups[1].Value);
            Check(unique, id + "P10", $"{ids.Count} declared $type ids, each declared once");
        }
    }
}
