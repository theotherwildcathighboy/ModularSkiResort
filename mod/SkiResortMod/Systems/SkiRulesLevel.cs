// The per-object rules stamp's pure rules (T229, DECISIONS 670; design docs/research/round2-2026-11-16/sync-resort/DESIGN.md section 4.1).
// An object our tools build carries SkiRulesStamp with the rules level it was built under (Current). An object loaded from a save without
// the stamp reads as level 0 ("unknown, built before 1.0.2"). An object loaded WITH a stamp keeps its level: loading never re-stamps.
// The level is a hint, never the truth: a later sync step acts only when its own check says so (670). The Sync engine
// (SkiSyncSystem, step catalog SkiSyncCatalog) reads the level to skip steps an object has already passed.
// One global level, not one per type (design 4.2: one append-only step catalog with a kind mask per step). Level 1 = the 1.0.2 baseline:
// everything 1.0.1 changed for built objects (the Patrol Hut firewood row, the filler kept out of lift corridors) is in it, so the
// engine's first catalog numbers those steps 1 and an object at level 1 needs none of them. Raise Current by one per new catalog step;
// never reuse or renumber a level (a retired step stays as a no-op number).
// Pure file (System only): tests/ResortBatch and tests/SaveBatch link it.

namespace SkiResortMod.Systems
{
    /// <summary>What a stamp sits on. Only for counts and log lines (the entity says what it is); never persisted.</summary>
    public enum SkiRulesKind : byte
    {
        LiftEdge = 0,
        Station = 1,
        Lodge = 2,
        School = 3,
        Patrol = 4,
        Depot = 5,
        Sign = 6,
        Run = 7,
        CatTrack = 8,
        Merged = 9,
    }

    public static class SkiRulesLevel
    {
        /// <summary>The rules level this build stamps on what it creates. 1 = the 1.0.2 baseline (file header).</summary>
        public const int Current = 1;

        /// <summary>The level of an object without a stamp: a save from before 1.0.2, or an object our writers never stamped.</summary>
        public const int Unknown = 0;

        /// <summary>SkiRulesStamp.m_Flags bit: the player chose "Keep as is" in the Sync card (SkiSyncSystem); m_Rules was set to Current.</summary>
        public const byte FlagKept = 1;

        /// <summary>SkiRulesStamp.m_Flags bit: a lift section built or changed by a tool that is not ours (SkiRulesStampSystem.MarkHandEdited,
        /// design 4.6). A step that re-plans towers lists and skips it. Never cleared. Other bits are reserved and never reused.</summary>
        public const byte FlagHandEdited = 2;

        /// <summary>Kinds in census order.</summary>
        public const int KindCount = 10;

        /// <summary>The level an object reads as: its stamp's, or Unknown without one.</summary>
        public static int LevelOf(bool hasStamp, int rules) => hasStamp ? rules : Unknown;

        /// <summary>A stamp from a newer build than this one (a save from a later version loaded in an older one): a sync step leaves that
        /// object alone (design 4.1 rule 3).</summary>
        public static bool IsNewer(int level) => level > Current;

        /// <summary>The stamp-new pass stamps an entity only when it was created this frame by a tool (Created, not Temp, not Deleted),
        /// carries no stamp yet, and the load guard is over. A loaded entity has no Created tag (the save keeps only serialized components,
        /// RequiredComponentSystem adds the rest without it), and the guard keeps out anything our own systems make in a load's first
        /// frames.</summary>
        public static bool StampsNew(bool created, bool temp, bool deleted, bool hasStamp, bool guardOver) =>
            created && !temp && !deleted && !hasStamp && guardOver;

        /// <summary>A re-created object (the run re-emit) keeps its source's level, never Current: an unstamped source gives an explicit
        /// Unknown stamp, so the stamp-new pass leaves the copy alone.</summary>
        public static int CarriedLevel(bool sourceHasStamp, int sourceRules) => sourceHasStamp ? sourceRules : Unknown;

        /// <summary>A lift line's edges are stamped when the tower fit takes a line that was built or edited this session (the census
        /// queued it outside the load's baseline build). A line the load's baseline queued keeps whatever it had, fitted or kept.</summary>
        public static bool FitStampsLine(bool loadBaseline) => !loadBaseline;

        /// <summary>Short census label of a kind (log lines, the bridge read).</summary>
        public static string Label(SkiRulesKind kind)
        {
            switch (kind)
            {
                case SkiRulesKind.LiftEdge: return "lift sections";
                case SkiRulesKind.Station: return "stations";
                case SkiRulesKind.Lodge: return "lodges";
                case SkiRulesKind.School: return "schools";
                case SkiRulesKind.Patrol: return "patrol huts";
                case SkiRulesKind.Depot: return "bus depots";
                case SkiRulesKind.Sign: return "welcome signs";
                case SkiRulesKind.Run: return "runs";
                case SkiRulesKind.CatTrack: return "cat tracks";
                case SkiRulesKind.Merged: return "merged buildings";
                default: return "?";
            }
        }

        /// <summary>One kind's counts: objects of the kind, and how many read at level 0, at Current, at another level (older, newer).</summary>
        public struct Count
        {
            public int Total, Level0, Current, Other, HandEdited, Kept;

            public void Add(int level) => Add(level, 0);

            public void Add(int level, byte flags)
            {
                Total++;
                if (level == Unknown) Level0++;
                else if (level == SkiRulesLevel.Current) Current++;
                else Other++;
                if ((flags & FlagHandEdited) != 0) HandEdited++;
                if ((flags & FlagKept) != 0) Kept++;
            }

            public override string ToString() =>
                Total + " (" + Level0 + " at 0, " + Current + " at " + SkiRulesLevel.Current + (Other > 0 ? ", " + Other + " other" : "") +
                (HandEdited > 0 ? ", " + HandEdited + " edited by hand" : "") + (Kept > 0 ? ", " + Kept + " kept as is" : "") + ")";
        }

        /// <summary>The census line body: "lift sections 12 (12 at 0, 0 at 1), runs 3 (...)". Kinds with no object are left out.</summary>
        public static string Describe(Count[] counts)
        {
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < counts.Length; k++)
            {
                if (counts[k].Total == 0)
                    continue;
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(Label((SkiRulesKind)k)).Append(' ').Append(counts[k].ToString());
            }
            return sb.Length > 0 ? sb.ToString() : "no ski objects";
        }
    }
}
