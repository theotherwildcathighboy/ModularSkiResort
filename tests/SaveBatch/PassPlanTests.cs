// SL-P (T284, 1.0.4): the per-area prices across saves. See the Program.cs header.
//   SL-P1 the two new rows (PassPlan 36 bytes on the sign, PassArea 6 bytes on the household) sit at the end of the table, and no
//         released save holds either (every release line lacks them): an older build skips both as unknown types.
//   SL-P2 save + reload keeps every field of both types byte for byte (the never-locked sentinel, the masks, a Null sign included).
//   SL-P3 the old-save case: a 1.0.3 save holds SkiSeasonPass (v1 bytes) and neither new type. Its households' standings load whole,
//         count at every area, and every area resolves to the Options ticket and the 1.0.3 calendar (phase, price, window) at every
//         season position. A sign that got the follow-default component from the season lock reads the same.
//   SL-P4 a 1.0.4 save loaded in 1.0.3 (the new types skipped): its SkiSeasonPass bytes are the frozen v1 layout, unchanged.

using System;
using System.Collections.Generic;
using System.Linq;
using SkiResortMod.Systems;
using Unity.Entities;

namespace SaveBatch
{
    public static class PassPlanTests
    {
        private static Func<bool, string, string, bool> s_Check;
        private static bool C(bool ok, string where, string msg) => s_Check(ok, where, msg);

        public static void Run(Func<bool, string, string, bool> check, string root)
        {
            s_Check = check;
            Rows(root);
            RoundTrip();
            OldSave();
        }

        private static SaveLayouts.Layout RowOf(string shortName) => SaveLayouts.All.FirstOrDefault(r => r.Short == shortName);

        private static byte[] Bytes(ISerializableShim s)
        {
            var w = new ByteWriter();
            s.Write(w);
            return w.Bytes.ToArray();
        }

        private interface ISerializableShim { void Write(ByteWriter w); }
        private sealed class PlanBox : ISerializableShim { public SkiPassPlan V; public void Write(ByteWriter w) => V.Serialize(w); }
        private sealed class AreaBox : ISerializableShim { public SkiSeasonPassArea V; public void Write(ByteWriter w) => V.Serialize(w); }
        private sealed class PassBox : ISerializableShim { public SkiSeasonPass V; public void Write(ByteWriter w) => V.Serialize(w); }

        private static void Rows(string root)
        {
            SaveLayouts.Layout plan = RowOf("PassPlan"), area = RowOf("PassArea");
            if (!C(plan != null && area != null, "SL-P1", "no PassPlan / PassArea row in SaveLayouts.All"))
                return;
            C(plan.TypeName == "SkiPassPlan" && plan.Kind == SaveLayouts.KindComponent && plan.Versions.Length == 1 && plan.Current.Bytes == 36,
              "SL-P1", "PassPlan is one IComponentData layout of 36 bytes");
            C(area.TypeName == "SkiSeasonPassArea" && area.Kind == SaveLayouts.KindComponent && area.Versions.Length == 1 && area.Current.Bytes == 6,
              "SL-P1", "PassArea is one IComponentData layout of 6 bytes (u8 version, u8 m_Flags, entity m_Sign)");
            int n = SaveLayouts.All.Length;
            C(n >= 2 && SaveLayouts.All[n - 2] == plan && SaveLayouts.All[n - 1] == area, "SL-P1", "the two 1.0.4 rows are the last two rows of the table");
            string path = System.IO.Path.Combine(root, "release", "CHANGELOG.md");
            if (C(System.IO.File.Exists(path), "SL-P1", "release/CHANGELOG.md not found"))
            {
                // the newest entry's line names both types; every older entry's line names neither
                var lines = System.IO.File.ReadAllLines(path)
                                          .SkipWhile(l => !System.Text.RegularExpressions.Regex.IsMatch(l, @"^## \d+\.\d+\.\d+\b"))   // past the template block
                                          .Select(l => l.Trim().TrimStart('-', '*').Trim())
                                          .Where(t => t.StartsWith("Layout versions:")).ToList();
                C(lines.Count >= 2 && lines[0].Contains("PassPlan 1") && lines[0].Contains("PassArea 1"), "SL-P1",
                  "the newest changelog entry's layout line names PassPlan 1 and PassArea 1");
                for (int i = 1; i < lines.Count; i++)
                    C(!lines[i].Contains("PassPlan") && !lines[i].Contains("PassArea"), "SL-P1", "an older changelog entry names a 1.0.4 type: " + lines[i]);
            }
        }

        private static void RoundTrip()
        {
            var plans = new List<SkiPassPlan>
            {
                SkiPassPlan.FollowDefault(),
                new SkiPassPlan
                {
                    m_Flags = SkiPassPlanRules.FlagOwnTicket | SkiPassPlanRules.FlagOwnPlan | SkiPassPlanRules.FlagLockedDefault,
                    m_TicketPrice = 155, m_EarlyPrice = 2000, m_RisePct = 50, m_SaleMask = 0xFFF, m_RiseMask = 0x00E,
                    m_LockSeason = 2027, m_LockEarly = 400, m_LockRisePct = 15, m_LockSaleMask = 0x1E, m_LockRiseMask = 0x8,
                },
                new SkiPassPlan { m_LockSeason = int.MaxValue, m_TicketPrice = -1, m_EarlyPrice = int.MinValue, m_SaleMask = -1, m_RiseMask = int.MaxValue },
            };
            foreach (SkiPassPlan p in plans)
            {
                byte[] b = Bytes(new PlanBox { V = p });
                var r = new ByteReader(b.ToList());
                var back = new SkiPassPlan();
                back.Deserialize(r);
                bool same = back.m_Flags == p.m_Flags && back.m_TicketPrice == p.m_TicketPrice && back.m_EarlyPrice == p.m_EarlyPrice &&
                            back.m_RisePct == p.m_RisePct && back.m_SaleMask == p.m_SaleMask && back.m_RiseMask == p.m_RiseMask &&
                            back.m_LockSeason == p.m_LockSeason && back.m_LockEarly == p.m_LockEarly && back.m_LockRisePct == p.m_LockRisePct &&
                            back.m_LockSaleMask == p.m_LockSaleMask && back.m_LockRiseMask == p.m_LockRiseMask && back.m_Version == 1;
                C(b.Length == 36 && b[0] == 1 && r.Pos == 36 && same, "SL-P2", "SkiPassPlan round trip (" + b.Length + " bytes, read " + r.Pos + ")");
                C(Bytes(new PlanBox { V = back }).SequenceEqual(b), "SL-P2", "SkiPassPlan read-then-write is not byte-identical");
            }
            C(SkiPassPlan.FollowDefault().m_LockSeason == SkiPassPlanRules.NoLock && SkiPassPlanRules.NoLock == int.MinValue, "SL-P2",
              "a fresh follow-default plan is never locked (int.MinValue sentinel)");
            foreach (var a in new[] { new SkiSeasonPassArea(new Entity { Index = 77, Version = 3 }, SkiPassPlanRules.AreaFlagPool), new SkiSeasonPassArea(Entity.Null, 0) })
            {
                byte[] b = Bytes(new AreaBox { V = a });
                var r = new ByteReader(b.ToList());
                var back = new SkiSeasonPassArea();
                back.Deserialize(r);
                C(b.Length == 6 && b[0] == 1 && r.Pos == 6 && back.m_Flags == a.m_Flags && back.m_Sign.Index == a.m_Sign.Index && back.m_Version == 1,
                  "SL-P2", "SkiSeasonPassArea round trip (" + b.Length + " bytes)");
            }
        }

        private static void OldSave()
        {
            const int season = 2027;
            // A 1.0.3 household: SkiSeasonPass v1 bytes [1][season i32][paid i32][flags u8], no SkiSeasonPassArea.
            byte[] old = Bytes(new PassBox { V = new SkiSeasonPass(season, 400, 0) });
            var r = new ByteReader(old.ToList());
            var pass = new SkiSeasonPass();
            pass.Deserialize(r);
            C(old.Length == 10 && r.Pos == 10 && pass.m_SeasonKey == season && pass.m_PricePaid == 400 && pass.m_Flags == 0, "SL-P3",
              "a 1.0.3 standing loads whole (10 bytes)");
            C(SkiMoneyRules.PassStandingOf(true, pass.m_SeasonKey, pass.m_Flags, season) == SkiMoneyRules.PassStanding.Holder &&
              SkiPassPlanRules.PassValidAt(false, 0, false, false) && SkiPassPlanRules.PassValidAt(false, 0, false, true), "SL-P3",
              "a 1.0.3 holder (no SkiSeasonPassArea) is good at every area, own plan or not");
            // Every area of a 1.0.3 save: no SkiPassPlan. And after the season lock: the follow-default component.
            foreach (int optTicket in new[] { 0, 80, 100, 200 })
            {
                foreach (int optPass in new[] { 20, 400, 2000 })
                {
                    var none = SkiPassPlanRules.Resolve(false, 0, 0, default, SkiPassPlanRules.NoLock, default, season, optTicket, optPass);
                    SkiPassPlan fd = SkiPassPlan.FollowDefault();
                    var comp = SkiPassPlanRules.Resolve(true, fd.m_Flags, fd.m_TicketPrice, fd.Own, fd.m_LockSeason, fd.LockedPlan, season, optTicket, optPass);
                    bool ok = none.Ticket == SkiMoneyRules.ClampTicketPrice(optTicket) && comp.Ticket == none.Ticket && none.OnDefault == 1 && comp.OnDefault == 1;
                    for (int k = 0; k < 12 * 16 && ok; k++)
                    {
                        double pos = k / 16.0;
                        SkiPassRules.SalePhase ph = SkiPassRules.PhaseOf(pos);
                        ok &= SkiPassPlanRules.PhaseAt(none.Plan, pos) == ph && SkiPassPlanRules.PhaseAt(comp.Plan, pos) == ph &&
                              SkiPassPlanRules.SalePrice(none.Plan, none.Ticket, pos) == SkiPassRules.SalePrice(optPass, optTicket, ph) &&
                              SkiPassPlanRules.SalePrice(comp.Plan, comp.Ticket, pos) == SkiPassRules.SalePrice(optPass, optTicket, ph) &&
                              SkiPassPlanRules.WindowPosition(none.Plan, pos) == SkiPassRules.WindowPosition(pos);
                    }
                    // the season lock of an untouched area copies the default plan: still the 1.0.3 prices, and pool passes
                    SkiPassPlan locked = fd;
                    locked.SetLocked(season, none.Plan);
                    locked.m_Flags |= SkiPassPlanRules.FlagLockedDefault;
                    var lr = SkiPassPlanRules.Resolve(true, locked.m_Flags, locked.m_TicketPrice, locked.Own, locked.m_LockSeason, locked.LockedPlan, season, optTicket, optPass);
                    ok &= lr.Ticket == none.Ticket && lr.Plan.SameAs(none.Plan) && lr.OnDefault == 1 && lr.Locked == 1;
                    C(ok, "SL-P3", "an untouched area (ticket " + optTicket + ", pass " + optPass + ") resolves to the 1.0.3 prices and calendar, before and after the lock");
                }
            }
            // SL-P4: what a 1.0.4 build writes for SkiSeasonPass is still the frozen v1 layout (a 1.0.3 build reads it, skipping the new types).
            byte[] now = Bytes(new PassBox { V = new SkiSeasonPass(season, 520, SkiMoneyRules.PassFlagDeclined) });
            C(now.Length == 10 && now[0] == 1 && SkiSeasonPass.CurrentVersion == 1, "SL-P4", "SkiSeasonPass still writes its frozen 10-byte v1 layout");
        }
    }
}
