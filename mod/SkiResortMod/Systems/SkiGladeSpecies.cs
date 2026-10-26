// Tree species groups for the Quick runs tree filler (conifer / deciduous / palm / bush), and the species pool a group or a single
// species hands to the glade planner. Pure (System only) so tests/GladeBatch links it.
// No prefab field encodes a botanical group (TreeData holds only m_WoodAmount, Game.Prefabs/TreeObject.cs:12-30; placeholders and
// UIObject groups mix families), so the group comes from the prefab name: the vanilla table first, then whole-word keywords of the
// name's camel-case / underscore tokens, else Ungrouped. A tree prefab (TreeData) with wood 0 is a bush (vanilla GreenBushWild01/02,
// FlowerBushWild01/02). Engine notes: EngineNotes/SkiGladeSpecies.md

using System;
using System.Collections.Generic;

namespace SkiResortMod.Systems
{
    public enum TreeGroup : byte
    {
        Conifer,
        Deciduous,
        Palm,
        Bush,
        /// <summary>An asset whose name names no known family: in the species list and "all trees", in no group.</summary>
        Ungrouped,
    }

    public static class SkiGladeSpecies
    {
        /// <summary>The 15 vanilla trees and 4 vanilla bushes (1.6.0f1 = 1.6.2f1 prefab dump), by prefab name.</summary>
        private static readonly KeyValuePair<string, TreeGroup>[] s_Vanilla =
        {
            new KeyValuePair<string, TreeGroup>("PineTree01", TreeGroup.Conifer),
            new KeyValuePair<string, TreeGroup>("SpruceTree01", TreeGroup.Conifer),
            new KeyValuePair<string, TreeGroup>("BirchTree01", TreeGroup.Deciduous),
            new KeyValuePair<string, TreeGroup>("OakTree01", TreeGroup.Deciduous),
            new KeyValuePair<string, TreeGroup>("AppleTree01", TreeGroup.Deciduous),
            new KeyValuePair<string, TreeGroup>("EU_AlderTree01", TreeGroup.Deciduous),
            new KeyValuePair<string, TreeGroup>("EU_ChestnutTree01", TreeGroup.Deciduous),
            new KeyValuePair<string, TreeGroup>("EU_PoplarTree01", TreeGroup.Deciduous),
            new KeyValuePair<string, TreeGroup>("NA_HickoryTree01", TreeGroup.Deciduous),
            new KeyValuePair<string, TreeGroup>("NA_LindenTree01", TreeGroup.Deciduous),
            new KeyValuePair<string, TreeGroup>("NA_LondonPlaneTree01", TreeGroup.Deciduous),
            new KeyValuePair<string, TreeGroup>("CoconutPalmTree01", TreeGroup.Palm),
            new KeyValuePair<string, TreeGroup>("FloridaPalmTree01", TreeGroup.Palm),
            new KeyValuePair<string, TreeGroup>("RoyalPalmTree01", TreeGroup.Palm),
            new KeyValuePair<string, TreeGroup>("SylvesterPalmTree01", TreeGroup.Palm),
            new KeyValuePair<string, TreeGroup>("GreenBushWild01", TreeGroup.Bush),
            new KeyValuePair<string, TreeGroup>("GreenBushWild02", TreeGroup.Bush),
            new KeyValuePair<string, TreeGroup>("FlowerBushWild01", TreeGroup.Bush),
            new KeyValuePair<string, TreeGroup>("FlowerBushWild02", TreeGroup.Bush),
        };

        // Checked in this order: palm and conifer before deciduous ("palm" never collides; "plane" stays deciduous)
        private static readonly string[] s_Palm = { "palm" };
        private static readonly string[] s_Conifer =
        {
            "pine", "spruce", "fir", "cedar", "larch", "hemlock", "juniper", "cypress", "redwood", "sequoia", "evergreen", "conifer",
        };
        private static readonly string[] s_Deciduous =
        {
            "oak", "birch", "maple", "beech", "elm", "ash", "alder", "poplar", "linden", "aspen", "willow", "chestnut", "hickory",
            "plane", "apple", "cherry",
        };
        private static readonly string[] s_Bush = { "bush", "shrub", "hedge" };

        /// <summary>Group of a tree prefab (one with TreeData). <paramref name="woodAmount"/> = TreeData.m_WoodAmount; 0 = a bush.</summary>
        public static TreeGroup GroupOf(string prefabName, float woodAmount)
        {
            string name = prefabName ?? "";
            for (int i = 0; i < s_Vanilla.Length; i++)
            {
                if (string.Equals(s_Vanilla[i].Key, name, StringComparison.Ordinal))
                    return s_Vanilla[i].Value;
            }
            List<string> words = Words(name);
            if (!(woodAmount > 0f) || AnyWord(words, s_Bush))
                return TreeGroup.Bush;
            if (AnyWord(words, s_Palm))
                return TreeGroup.Palm;
            if (AnyWord(words, s_Conifer))
                return TreeGroup.Conifer;
            if (AnyWord(words, s_Deciduous))
                return TreeGroup.Deciduous;
            return TreeGroup.Ungrouped;
        }

        /// <summary>Species knobs for a pool of <paramref name="count"/> prefabs (index 0 first): a group plants every member equally
        /// (DominantShare = 1 / count makes the planner's "index 0 with p, else uniform over the rest" uniform); one species gets
        /// SpeciesCount 1. Species picks never move a tree (the planner draws them after the position), so changing the pool keeps the
        /// layout.</summary>
        public static void ApplyPool(ref GladeParams p, int count)
        {
            int n = count < 1 ? 1 : count;
            p.SpeciesCount = n;
            p.DominantShare = 1f / n;
        }

        /// <summary>Lower-case words of a prefab name: split at '_', '-', ' ', '.', digit runs and lower-to-upper case changes
        /// ("NA_LondonPlaneTree01" -> na, london, plane, tree).</summary>
        internal static List<string> Words(string name)
        {
            var words = new List<string>();
            var cur = new System.Text.StringBuilder();
            char prev = '\0';
            foreach (char ch in name)
            {
                bool letter = char.IsLetter(ch);
                bool split = !letter || (char.IsUpper(ch) && char.IsLower(prev));
                if (split && cur.Length > 0)
                {
                    words.Add(cur.ToString());
                    cur.Clear();
                }
                if (letter)
                    cur.Append(char.ToLowerInvariant(ch));
                prev = ch;
            }
            if (cur.Length > 0)
                words.Add(cur.ToString());
            return words;
        }

        private static bool AnyWord(List<string> words, string[] keys)
        {
            for (int i = 0; i < words.Count; i++)
            {
                for (int k = 0; k < keys.Length; k++)
                {
                    if (string.Equals(words[i], keys[k], StringComparison.Ordinal))
                        return true;
                }
            }
            return false;
        }
    }
}
