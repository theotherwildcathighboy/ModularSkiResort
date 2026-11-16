// The Sync Resort step catalog and its pure rules (T229, DECISIONS 670; design docs/research/round2-2026-11-16/sync-resort/DESIGN.md
// sections 4.2 and 4.3). A patch that changes how built objects should look adds a numbered step here. The engine (SkiSyncSystem) brings
// objects built under older rules up to date: each step has its own "needs it?" check, and the stamp (SkiRulesStamp.m_Rules, the level) only
// says which steps were already checked. A level is a hint, the check is the truth.
// Rules:
//   * Append only. A step number is never reused or renumbered; a retired step stays as a no-op. Current = the last step's number, and it
//     equals SkiRulesLevel.Current (the level our tools stamp on what they build). ResortBatch SY-1 checks both.
//   * Auto steps fix what the player cannot see as a loss and run by themselves after a load. Offer steps change what the player sees and
//     wait for the hub's Sync button (owner rule, 670): a preview with a per-item skip, "Keep as is" per item, no undo, free.
//   * A step that re-plans lift towers sets SkipsHandEdited: a line edited by a tool that is not ours is listed and skipped (670).
//   * A stamp above Current (a save from a newer build) is left alone.
// 1.0.2 has one step: 1 = a Patrol Hut or the Patrol HQ lacks a part of its prefab (the firewood pile added in 1.0.1). An asset change does not
// reach a placed building on its own: a load rebuilds the building's part list from the saved parts only (Game.Serialization.SubObjectSystem),
// so a row added to the prefab later has no part until the building's parts are rebuilt.
// Pure file (System only): tests/ResortBatch links it.

using System;

namespace SkiResortMod.Systems
{
    /// <summary>Auto: runs by itself after a load (invisible fixes). Offer: waits for the hub's Sync (visible changes).</summary>
    public enum SyncClass : byte
    {
        Auto = 0,
        Offer = 1,
    }

    /// <summary>What the intake decides for one object.</summary>
    public enum SyncVerdict : byte
    {
        /// <summary>No step of the catalog applies to this kind above the object's level: nothing to check, the stamp stays.</summary>
        NothingPending = 0,
        /// <summary>Steps apply, and no check says it is needed: the object is stamped Current without a word.</summary>
        Silent = 1,
        /// <summary>The first needed step is an Auto step: it runs by itself.</summary>
        Auto = 2,
        /// <summary>The first needed step is an Offer step: the object is listed under the hub's Sync button.</summary>
        Offer = 3,
        /// <summary>The first needed step skips lines edited by hand, and this one was: listed as skipped, never synced.</summary>
        HandEdited = 4,
        /// <summary>The stamp is newer than this build (a save from a later version): left alone.</summary>
        Newer = 5,
    }

    public struct SyncStep
    {
        public int Number;
        /// <summary>Bit (1 << SkiRulesKind) per kind the step acts on.</summary>
        public int KindMask;
        public SyncClass Class;
        /// <summary>Stable id for logs and the hub's sentence (hubText SYNC_STEP).</summary>
        public string Id;
        /// <summary>A retired step stays in the list as a no-op, so numbers never shift.</summary>
        public bool Retired;
        /// <summary>Lift tower re-plans: a line marked HandEdited is listed and skipped.</summary>
        public bool SkipsHandEdited;
    }

    public static class SkiSyncCatalog
    {
        /// <summary>Step 1: a Patrol Hut or the Patrol HQ lacks a part its prefab has (the 1.0.1 firewood pile). Offer: a part appears.</summary>
        public const int StepPatrolParts = 1;

        public static readonly SyncStep[] Steps =
        {
            new SyncStep { Number = StepPatrolParts, KindMask = Bit(SkiRulesKind.Patrol), Class = SyncClass.Offer, Id = "patrolParts" },
        };

        /// <summary>Objects the intake checks per frame after a load.</summary>
        public const int IntakePerFrame = 64;

        /// <summary>Objects a Sync starts per frame (vanilla rebuilds their parts at Modification2B; a small batch keeps one frame flat).</summary>
        public const int ApplyPerFrame = 8;

        /// <summary>Frames between a step's action and its check. The part rebuild lands through a command buffer, so the check waits.</summary>
        public const int VerifyFrames = 10;

        /// <summary>Frames the intake waits for the area links after a load before it lists objects without one.</summary>
        public const int LinkWaitFrames = 1200;

        /// <summary>The last step's number. Equals SkiRulesLevel.Current (ResortBatch SY-1).</summary>
        public static int Current => Steps.Length == 0 ? 0 : Steps[Steps.Length - 1].Number;

        public static int Bit(SkiRulesKind kind) => 1 << (int)kind;

        /// <summary>The kinds any live step acts on (the intake reads only these).</summary>
        public static int KindsTargeted()
        {
            int mask = 0;
            foreach (SyncStep s in Steps)
                if (!s.Retired)
                    mask |= s.KindMask;
            return mask;
        }

        /// <summary>Whether step <paramref name="s"/> still has to be checked for an object of <paramref name="kind"/> at
        /// <paramref name="level"/>.</summary>
        public static bool Pending(SyncStep s, int level, SkiRulesKind kind) =>
            !s.Retired && s.Number > level && (s.KindMask & Bit(kind)) != 0;

        public static bool TryGetStep(int number, out SyncStep step)
        {
            foreach (SyncStep s in Steps)
            {
                if (s.Number == number)
                {
                    step = s;
                    return true;
                }
            }
            step = default;
            return false;
        }

        /// <summary>The intake's verdict. <paramref name="needs"/> is the step's own check (called only for pending steps, in order, until one
        /// says yes). <paramref name="step"/> = the first needed step, 0 when none.</summary>
        public static SyncVerdict Decide(int level, byte flags, SkiRulesKind kind, Func<int, bool> needs, out int step)
        {
            step = 0;
            if (SkiRulesLevel.IsNewer(level))
                return SyncVerdict.Newer;
            bool any = false;
            foreach (SyncStep s in Steps)
            {
                if (!Pending(s, level, kind))
                    continue;
                any = true;
                if (!needs(s.Number))
                    continue;
                step = s.Number;
                if (s.SkipsHandEdited && (flags & SkiRulesLevel.FlagHandEdited) != 0)
                    return SyncVerdict.HandEdited;
                return s.Class == SyncClass.Auto ? SyncVerdict.Auto : SyncVerdict.Offer;
            }
            return any ? SyncVerdict.Silent : SyncVerdict.NothingPending;
        }

        /// <summary>"Keep as is": the object is stamped Current with the Kept bit, so it stops asking until a newer step (670).</summary>
        public static byte KeptFlags(byte flags) => (byte)(flags | SkiRulesLevel.FlagKept);

        /// <summary>How many queued objects a frame starts.</summary>
        public static int Take(int queued, int perFrame) => Math.Max(0, Math.Min(queued, perFrame));

        /// <summary>A part row counts for the "lacks a part" check only when vanilla always places it: probability 100, no placement
        /// requirement, and the part prefab is neither a placeholder (it spawns another prefab) nor gated by an object requirement (weather,
        /// theme). <paramref name="requireFlags"/> = the row's flags masked with the Require* bits.</summary>
        public static bool RowCounts(int probability, int requireFlags, bool placeholder, bool requirement) =>
            probability >= 100 && requireFlags == 0 && !placeholder && !requirement;

        /// <summary>Parts a building lacks for one part prefab: rows minus placed parts of that prefab, never negative.</summary>
        public static int Missing(int rows, int placed) => Math.Max(0, rows - placed);

        /// <summary>Parts beyond the rows for one part prefab (the duplicate check after a rebuild): placed minus rows, never negative.</summary>
        public static int Extra(int rows, int placed) => Math.Max(0, placed - rows);
    }
}
