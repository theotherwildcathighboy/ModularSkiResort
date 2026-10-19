// ChangelogTests — SL-CL: release/CHANGELOG.md's three fixed lines (constraints doc §4 item 9, §3 rule 9). A release entry
// is a "## X.Y.Z ..." heading outside ``` fences (the template block is fenced, the beta notes heading has no version); the
// entries are newest first.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using SkiResortMod.Systems;

namespace SaveBatch
{
    public static class ChangelogTests
    {
        private static readonly Regex Heading = new Regex(@"^## (\d+\.\d+\.\d+)\b");
        private static readonly Regex Compat = new Regex(@"^\s*[-*]?\s*Save compatible with previous:\s*(yes|no|n/a)\b");
        private static readonly Regex Layout = new Regex(@"^\s*[-*]?\s*(Layout versions:.*?)\s*$");
        private static readonly Regex Older = new Regex(@"^\s*[-*]?\s*Do not load in older versions:\s*(yes|no)\b");

        private sealed class Entry
        {
            public string Version;
            public int Line;
            public readonly List<string> Compat = new List<string>();
            public readonly List<string> Layout = new List<string>();
            public readonly List<string> Older = new List<string>();
        }

        public static void Run(Func<bool, string, string, bool> check, string root)
        {
            string path = Path.Combine(root, "release", "CHANGELOG.md");
            if (!check(File.Exists(path), "SL-CL", "release/CHANGELOG.md not found"))
                return;
            var entries = new List<Entry>();
            bool fence = false;
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i];
                if (l.TrimStart().StartsWith("```")) { fence = !fence; continue; }
                if (fence) continue;
                if (l.StartsWith("## "))
                {
                    Match h = Heading.Match(l);
                    entries.Add(h.Success ? new Entry { Version = h.Groups[1].Value, Line = i + 1 } : null);
                    continue;
                }
                Entry e = entries.Count > 0 ? entries[entries.Count - 1] : null;
                if (e == null) continue;
                Match m;
                if ((m = Compat.Match(l)).Success) e.Compat.Add(m.Groups[1].Value);
                else if ((m = Layout.Match(l)).Success) e.Layout.Add(m.Groups[1].Value);
                else if ((m = Older.Match(l)).Success) e.Older.Add(m.Groups[1].Value);
            }
            entries.RemoveAll(e => e == null);
            if (!check(entries.Count > 0, "SL-CL", "no release entry ('## X.Y.Z ...') in release/CHANGELOG.md"))
                return;
            foreach (Entry e in entries)
            {
                string w = "SL-CL " + e.Version + " (line " + e.Line + ")";
                check(e.Compat.Count == 1, w, e.Compat.Count + " 'Save compatible with previous: yes/no' line(s), want 1");
                check(e.Layout.Count == 1, w, e.Layout.Count + " 'Layout versions:' line(s), want 1");
                check(e.Older.Count == 1, w, e.Older.Count + " 'Do not load in older versions: yes/no' line(s), want 1");
            }
            Entry newest = entries[0];
            if (newest.Layout.Count == 1)
                check(newest.Layout[0] == SaveLayouts.ChangelogLine, "SL-CL " + newest.Version,
                      "newest entry says '" + newest.Layout[0] + "', the code says '" + SaveLayouts.ChangelogLine + "' (SaveLayouts.ChangelogLine)");
            for (int i = 0; i + 1 < entries.Count; i++)
            {
                Entry a = entries[i], b = entries[i + 1];
                if (a.Layout.Count == 1 && b.Layout.Count == 1 && a.Layout[0] != b.Layout[0] && a.Older.Count == 1)
                    check(a.Older[0] == "yes", "SL-CL " + a.Version,
                          "layouts changed since " + b.Version + " but 'Do not load in older versions' is not yes (rule 9: every bump is a one-way door)");
            }
            Program.Info("SL-CL " + entries.Count + " release entr" + (entries.Count == 1 ? "y" : "ies") + "; newest " + newest.Version);
        }
    }
}
