// Pure numbering of same-tier lodges in one ski area: the hub names a lodge "Chalet 2" while its area has more than one Chalet. The number
// is held in SkiLodgeNumber (persisted, with the sign it was given for), so it survives frames, saves and loads: a lodge keeps it while it
// exists and stays in that area. A lodge without one, or one that joined another area, takes the lowest free number of its tier there;
// lodges seen together take numbers in entity order (in play they arrive one at a time, so this is build order). A clash (two lodges
// holding one number in one area) keeps it on the lower entity. Unlinked lodges get none and keep a held one untouched.
// A lodge the player renamed (the game's NameSystem) shows that name instead (DisplayName); the number stays the fallback.
// Pure file: System, System.Text and System.Collections.Generic only (+ EntityRef). Linked into tests/BuildingBatch.

using System.Collections.Generic;

namespace SkiResortMod.Systems
{
    public static class LodgeNumberRule
    {
        /// <summary>One live top-level lodge of the last recompute.</summary>
        public struct Entry
        {
            public EntityRef Lodge;
            public EntityRef Sign;       // its published link; Null = not linked (no number given, a held one is kept)
            public int Tier;             // SkiCatalog.Lodges index
            public bool Has;             // holds a SkiLodgeNumber
            public EntityRef HeldSign;   // the sign that number was given for
            public int Number;           // the held number (1 and up)
        }

        /// <summary>A SkiLodgeNumber to write (add or replace).</summary>
        public struct Write
        {
            public EntityRef Lodge;
            public EntityRef Sign;
            public int Number;
        }

        /// <summary>Numbers every linked lodge per (sign, tier). <paramref name="writes"/>: the lodges whose SkiLodgeNumber must be added or
        /// replaced; <paramref name="shown"/>, aligned with <paramref name="entries"/>: the number the hub shows (Shown), 0 for an unlinked
        /// lodge. Both lists are cleared first.</summary>
        public static void Assign(IReadOnlyList<Entry> entries, List<Write> writes, List<int> shown)
        {
            writes.Clear();
            shown.Clear();
            var order = new List<int>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                shown.Add(0);
                if (!entries[i].Sign.IsNull)
                    order.Add(i);
            }
            order.Sort((a, b) =>
            {
                int c = entries[a].Sign.CompareTo(entries[b].Sign);
                if (c != 0)
                    return c;
                c = entries[a].Tier.CompareTo(entries[b].Tier);
                return c != 0 ? c : entries[a].Lodge.CompareTo(entries[b].Lodge);
            });

            var taken = new HashSet<int>();
            var pending = new List<int>(4);
            var final = new int[entries.Count];
            int start = 0;
            while (start < order.Count)
            {
                Entry head = entries[order[start]];
                int end = start;
                while (end < order.Count && entries[order[end]].Sign == head.Sign && entries[order[end]].Tier == head.Tier)
                    end++;
                taken.Clear();
                pending.Clear();
                // Held numbers first, in entity order: the lower entity keeps a clashing number.
                for (int k = start; k < end; k++)
                {
                    Entry e = entries[order[k]];
                    if (e.Has && e.Number >= 1 && e.HeldSign == e.Sign && taken.Add(e.Number))
                        final[order[k]] = e.Number;
                    else
                        pending.Add(order[k]);
                }
                int next = 1;
                for (int p = 0; p < pending.Count; p++)
                {
                    while (taken.Contains(next))
                        next++;
                    taken.Add(next);
                    int i = pending[p];
                    final[i] = next;
                    writes.Add(new Write { Lodge = entries[i].Lodge, Sign = entries[i].Sign, Number = next });
                }
                int count = end - start;
                for (int k = start; k < end; k++)
                    shown[order[k]] = Shown(count, final[order[k]]);
                start = end;
            }
        }

        /// <summary>The number the hub shows after the tier word: 0 (none) while the lodge is the only one of its tier in its area.</summary>
        public static int Shown(int sameTierInArea, int number)
        {
            return sameTierInArea > 1 && number >= 1 ? number : 0;
        }

        /// <summary>Longest custom name the hub shows (UTF-16 units): the game's own rename field in the info panel title takes 85
        /// (installed index.js, selected-info title `maxLength:85`); a longer name can only come from code. A cut name ends in an ellipsis.</summary>
        public const int NameMaxChars = 85;

        /// <summary>A lodge's custom name (NameSystem) as the hub shows it, or null for none: control characters and line or paragraph
        /// separators become spaces, runs of white space become one, the ends are trimmed, and a name longer than NameMaxChars is cut
        /// (never inside a surrogate pair) and ends in an ellipsis. No other escaping: the game inserts a placeholder argument as text in one
        /// pass, so braces in a name stay literal (index.js `substitute`). Returns <paramref name="raw"/> itself when nothing changed.</summary>
        public static string DisplayName(string raw)
        {
            if (raw == null)
                return null;
            bool clean = raw.Length > 0 && raw.Length <= NameMaxChars && !char.IsWhiteSpace(raw[0]) && !char.IsWhiteSpace(raw[raw.Length - 1]);
            for (int i = 0; clean && i < raw.Length; i++)
            {
                char c = raw[i];
                if (char.IsControl(c) || c == '\u2028' || c == '\u2029' || (char.IsWhiteSpace(c) && (c != ' ' || raw[i - 1] == ' ')))
                    clean = false;
            }
            if (clean)
                return raw;
            var sb = new System.Text.StringBuilder(raw.Length);
            bool space = false;
            foreach (char c in raw)
            {
                if (char.IsControl(c) || char.IsWhiteSpace(c) || c == '\u2028' || c == '\u2029')
                {
                    space = sb.Length > 0;
                    continue;
                }
                if (space)
                    sb.Append(' ');
                space = false;
                sb.Append(c);
            }
            if (sb.Length == 0)
                return null;
            if (sb.Length > NameMaxChars)
            {
                int cut = NameMaxChars - 1;   // room for the ellipsis
                if (char.IsHighSurrogate(sb[cut - 1]))
                    cut--;
                while (cut > 0 && sb[cut - 1] == ' ')
                    cut--;
                sb.Length = cut;
                sb.Append('\u2026');
            }
            return sb.ToString();
        }
    }
}
