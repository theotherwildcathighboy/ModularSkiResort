// SL-R (the released layout lines against the table) and SL-S (the 1.0.2 rules stamp across saves). See the Program.cs header.
//   SL-R1 every row a release names still exists, at the version that release wrote (after 1.0 a NEW type, never a longer layout).
//   SL-R2 every version a release wrote is in LayoutTests.Shipped (the guard copy cannot be forgotten at a release again). A heading
//         that says "unreleased" is not in saves yet: SL-R2 binds once the heading gets its date.
//   SL-R3 the newest release's rows are the table's first rows, in order (new rows append at the end).
//   SL-S1 an old save (the 1.0.0 / 1.0.1 lines) holds no stamp: every object reads level 0.
//   SL-S2 a new object gets the current level, and its bytes are the v1 layout.
//   SL-S3 save + reload keeps any level (older, current, newer) and the flags; a loaded object is never stamped again.
//   SL-S4 an uninstall-style load (an older build, no such type) is clean: the type is new to every release, no released row changed size,
//         and an object re-saved there reads level 0 again.
//   SL-S5 a re-emitted run keeps its source's level; a newer-than-me level is flagged.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SkiResortMod.Systems;

namespace SaveBatch
{
    public static class RulesStampTests
    {
        private static readonly Regex Heading = new Regex(@"^## (\d+\.\d+\.\d+)\b");
        private static readonly Regex Layout = new Regex(@"^\s*[-*]?\s*Layout versions:\s*(.*?)\s*$");

        /// <summary>Suffix on a release whose heading says "unreleased": its rows are not in players' saves yet (SL-R2 skips it).</summary>
        private const string Unreleased = " (unreleased)";

        private static Func<bool, string, string, bool> s_Check;
        private static bool C(bool ok, string where, string msg) => s_Check(ok, where, msg);

        public static void Run(Func<bool, string, string, bool> check, string root)
        {
            s_Check = check;
            List<(string Version, List<(string Short, int N)> Rows)> releases = Releases(root);
            Released(releases);
            Stamp(releases);
        }

        /// <summary>Every "## X.Y.Z" entry's layout line, newest first (fenced template skipped, as ChangelogTests reads them).</summary>
        private static List<(string, List<(string, int)>)> Releases(string root)
        {
            var list = new List<(string, List<(string, int)>)>();
            string path = Path.Combine(root, "release", "CHANGELOG.md");
            if (!File.Exists(path))
                return list;
            bool fence = false;
            string version = null;
            foreach (string l in File.ReadAllLines(path))
            {
                if (l.TrimStart().StartsWith("```")) { fence = !fence; continue; }
                if (fence) continue;
                if (l.StartsWith("## ")) { Match h = Heading.Match(l); version = h.Success ? h.Groups[1].Value + (l.Contains("unreleased") ? Unreleased : "") : null; continue; }
                Match m;
                if (version == null || !(m = Layout.Match(l)).Success)
                    continue;
                var rows = new List<(string, int)>();
                foreach (string part in m.Groups[1].Value.Split(','))
                {
                    string[] t = part.Trim().Split(' ');
                    if (t.Length == 2 && int.TryParse(t[1], out int n))
                        rows.Add((t[0], n));
                    else
                        C(false, "SL-R " + version, "layout token '" + part.Trim() + "' is not 'Short N'");
                }
                list.Add((version, rows));
                version = null;
            }
            return list;
        }

        private static SaveLayouts.Layout RowOf(string shortName) => SaveLayouts.All.FirstOrDefault(r => r.Short == shortName);

        private static string Render(SaveLayouts.Layout row, SaveLayouts.LayoutVersion v) =>
            row.TypeName + " v" + v.Number + " " + v.Bytes + " " + string.Join(" ", v.Appended.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

        // SL-R

        private static void Released(List<(string Version, List<(string Short, int N)> Rows)> releases)
        {
            C(releases.Count > 0, "SL-R", "no release layout line in release/CHANGELOG.md");
            foreach ((string version, List<(string Short, int N)> rows) in releases)
            {
                string w = "SL-R " + version;
                foreach ((string shortName, int n) in rows)
                {
                    SaveLayouts.Layout row = RowOf(shortName);
                    if (!C(row != null, w, shortName + " is in that release's saves but no longer in SaveLayouts.All (a row is never removed)"))
                        continue;
                    // SL-R1: after 1.0 a shipped row never gains a version (production mode, CLAUDE.md "Released")
                    C(row.Current.Number == n, w, row.TypeName + " is v" + row.Current.Number + ", the release wrote v" + n +
                      " (after 1.0 a new field is a NEW type with a new row, never a longer layout)");
                    // SL-R2: the guard copy holds every version that reached a player
                    foreach (SaveLayouts.LayoutVersion v in row.Versions.Where(v => v.Number <= n))
                        if (!version.EndsWith(Unreleased))
                            C(LayoutTests.Shipped.Contains(Render(row, v)), w + " shipped",
                          "add to LayoutTests.Shipped (it reached players in " + version + "): " + Render(row, v));
                }
            }
            if (releases.Count == 0)
                return;
            // SL-R3: the newest release's rows, in order, are the table's first rows
            List<(string Short, int N)> newest = releases[0].Rows;
            for (int i = 0; i < newest.Count; i++)
                C(i < SaveLayouts.All.Length && SaveLayouts.All[i].Short == newest[i].Short, "SL-R3 " + releases[0].Version,
                  "row " + i + " is " + (i < SaveLayouts.All.Length ? SaveLayouts.All[i].Short : "missing") + ", the release line has " +
                  newest[i].Short + " (rows append at the end)");
        }

        // SL-S

        private static byte[] Bytes(SkiRulesStamp s)
        {
            var w = new ByteWriter();
            s.Serialize(w);
            return w.Bytes.ToArray();
        }

        private static SkiRulesStamp Load(byte[] bytes, out int consumed)
        {
            var r = new ByteReader(bytes.ToList());
            var s = new SkiRulesStamp();
            s.Deserialize(r);
            consumed = r.Pos;
            return s;
        }

        private static void Stamp(List<(string Version, List<(string Short, int N)> Rows)> releases)
        {
            SaveLayouts.Layout row = RowOf("RulesStamp");
            if (!C(row != null, "SL-S", "no RulesStamp row in SaveLayouts.All"))
                return;
            C(row.TypeName == "SkiRulesStamp" && row.Kind == SaveLayouts.KindComponent && row.Versions.Length == 1 && row.Current.Bytes == 6,
              "SL-S", "RulesStamp is one IComponentData layout of 6 bytes (u8 version, i32 m_Rules, u8 m_Flags)");
            C(SaveLayouts.All[SaveLayouts.All.Length - 1] == row || releases.Any(r => r.Rows.Any(x => x.Short == "RulesStamp")),
              "SL-S", "an unreleased row sits at the end of the table");

            // SL-S1 / SL-S4: no released save holds the type; an older build has no row for it (skipped as unknown, ComponentSerializer.cs:51-73)
            foreach ((string version, List<(string Short, int N)> rows) in releases.Where(r => r.Version == "1.0.0" || r.Version == "1.0.1"))
                C(!rows.Any(x => x.Short == "RulesStamp"), "SL-S1 " + version, "a " + version + " save cannot hold RulesStamp");
            C(releases.Any(r => r.Version == "1.0.0") && releases.Any(r => r.Version == "1.0.1"), "SL-S1", "the 1.0.0 and 1.0.1 release lines are in the changelog");
            C(SkiRulesLevel.LevelOf(false, 0) == 0 && SkiRulesLevel.LevelOf(false, 5) == 0 && SkiRulesLevel.Unknown == 0, "SL-S1",
              "an object without the stamp (every object of a 1.0.0 / 1.0.1 save) reads level 0");

            // SL-S2: a new object
            C(SkiRulesLevel.Current >= 1, "SL-S2", "the current level is at least 1 (0 is 'unknown')");
            C(SkiRulesLevel.StampsNew(created: true, temp: false, deleted: false, hasStamp: false, guardOver: true), "SL-S2",
              "a tool-created object (Created, not Temp) is stamped");
            var fresh = new SkiRulesStamp(SkiRulesLevel.Current);
            byte[] b = Bytes(fresh);
            int cur = SkiRulesLevel.Current;
            C(b.Length == 6 && b[0] == 1 && b[1] == (byte)cur && b[2] == (byte)(cur >> 8) && b[3] == (byte)(cur >> 16) && b[4] == (byte)(cur >> 24) && b[5] == 0,
              "SL-S2", "a new stamp writes [1][Current, i32 LE][0], got " + BitConverter.ToString(b));

            // SL-S3: save + reload keeps the level and flags; loading never stamps
            foreach (int level in new[] { 0, 1, cur, cur + 3, int.MaxValue })
            {
                var s = new SkiRulesStamp(level) { m_Flags = (byte)(SkiRulesLevel.FlagKept | SkiRulesLevel.FlagHandEdited) };
                SkiRulesStamp back = Load(Bytes(s), out int used);
                C(used == 6 && back.m_Rules == level && back.m_Flags == s.m_Flags && back.m_Version == 1, "SL-S3",
                  "level " + level + " came back as " + back.m_Rules + " (flags " + back.m_Flags + ", " + used + " bytes)");
                C(Bytes(back).SequenceEqual(Bytes(s)), "SL-S3", "level " + level + ": read-then-write is not byte-identical");
            }
            C(!SkiRulesLevel.StampsNew(created: false, temp: false, deleted: false, hasStamp: false, guardOver: true), "SL-S3",
              "a loaded object (no Created tag: a save holds none) is never stamped");
            C(!SkiRulesLevel.StampsNew(created: true, temp: false, deleted: false, hasStamp: true, guardOver: true), "SL-S3",
              "an object that has a stamp keeps it");
            C(!SkiRulesLevel.StampsNew(created: true, temp: false, deleted: false, hasStamp: false, guardOver: false), "SL-S3",
              "nothing is stamped in a load's guard frames");
            C(!SkiRulesLevel.StampsNew(created: true, temp: true, deleted: false, hasStamp: false, guardOver: true) &&
              !SkiRulesLevel.StampsNew(created: true, temp: false, deleted: true, hasStamp: false, guardOver: true), "SL-S3",
              "a tool preview (Temp) or a deleted object is never stamped");

            // SL-S4: the uninstall-style load. No released row changed size (an older build reading a longer block throws "Data size
            // mismatch"); the new type is the only change, so that build skips it as unknown and loses nothing else.
            if (releases.Count > 0)
            {
                foreach ((string shortName, int n) in releases[0].Rows)
                {
                    SaveLayouts.Layout r = RowOf(shortName);
                    if (r != null && n >= 1 && n <= r.Versions.Length)
                        C(r.Current.Bytes == r.Versions[n - 1].Bytes, "SL-S4 " + releases[0].Version,
                          r.TypeName + " writes " + r.Current.Bytes + " bytes, " + releases[0].Version + " reads " + r.Versions[n - 1].Bytes);
                }
            }
            C(SkiRulesLevel.LevelOf(hasStamp: false, rules: cur) == 0, "SL-S4",
              "an object re-saved by an older build (the stamp dropped) reads level 0 again");

            // SL-S5
            C(SkiRulesLevel.CarriedLevel(false, 9) == 0 && SkiRulesLevel.CarriedLevel(true, 0) == 0 && SkiRulesLevel.CarriedLevel(true, cur) == cur &&
              SkiRulesLevel.CarriedLevel(true, cur + 2) == cur + 2, "SL-S5", "a re-emitted run keeps its source's level (none = an explicit 0), never Current");
            C(SkiRulesLevel.IsNewer(cur + 1) && !SkiRulesLevel.IsNewer(cur) && !SkiRulesLevel.IsNewer(0), "SL-S5", "only a level above Current is newer than this build");

            Program.Info("SL-S RulesStamp v" + row.Current.Number + " = " + row.Current.Bytes + " B, rules level " + cur +
                         "; " + releases.Count + " release line(s) checked (SL-R)");
        }
    }
}
