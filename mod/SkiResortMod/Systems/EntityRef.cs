// EntityRef and RunKind: pure stand-ins so pure files and the net8 test hosts (tests/ResortBatch, tests/LiftBatch),
// which cannot see Unity.Entities, can carry entities as {Index, Version}. Convert back at the system boundary
// with `new Entity { Index = r.Index, Version = r.Version }`.
//   * IsNull is Index == 0 && Version == 0, exactly Entity.Null; a merely nonexistent Entity is not null here either.
//   * Equality is Index and Version (a recycled index with a new version differs); CompareTo orders by Index, then Version.
//   * RunKind values equal SkiRunOwnershipSystem.KindOf and mod/SkiResortUI/src/bindings.ts RunKind. The snow
//     injection system's private kind order (0 night / 1 snowmaking / 2 groomed) never leaves it; never cast between them.
// Pure file: `using System;` only (the test-host rule), no allocation, no state.

using System;

namespace SkiResortMod.Systems
{
    /// <summary>Pure stand-in for Unity.Entities.Entity.</summary>
    public struct EntityRef : IEquatable<EntityRef>, IComparable<EntityRef>
    {
        public int Index, Version;

        public EntityRef(int index, int version)
        {
            Index = index;
            Version = version;
        }

        /// <summary>Index == 0 &amp;&amp; Version == 0 (= Entity.Null).</summary>
        public bool IsNull => Index == 0 && Version == 0;

        public static EntityRef Null => default(EntityRef);

        public bool Equals(EntityRef other)
        {
            return Index == other.Index && Version == other.Version;
        }

        public override bool Equals(object obj)
        {
            return obj is EntityRef other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Index * 397 ^ Version;
        }

        /// <summary>Index, then Version.</summary>
        public int CompareTo(EntityRef other)
        {
            if (Index != other.Index)
                return Index < other.Index ? -1 : 1;
            if (Version != other.Version)
                return Version < other.Version ? -1 : 1;
            return 0;
        }

        public static bool operator ==(EntityRef a, EntityRef b)
        {
            return a.Index == b.Index && a.Version == b.Version;
        }

        public static bool operator !=(EntityRef a, EntityRef b)
        {
            return a.Index != b.Index || a.Version != b.Version;
        }

        /// <summary>"Index:Version", the log form of Entity. Allocates: logs and dumps only.</summary>
        public override string ToString()
        {
            return Index + ":" + Version;
        }
    }

    /// <summary>Run kind; values match SkiRunOwnershipSystem.KindOf and bindings.ts RunKind.</summary>
    public enum RunKind : byte
    {
        Groomed = 0,
        Snowmaking = 1,
        Night = 2,
        Natural = 3,   // Appended: never renumber (the test hosts assert 0-3)
        CatTrack = 4,  // Appended: a drawable green track that may cross other runs
    }

    /// <summary>What a run is drawn as (run types, DECISIONS 576): a piste or a cat track. Not saved: derived (RunFeatures.FromKind).</summary>
    public enum RunBase : byte
    {
        Piste = 0,
        CatTrack = 1,
    }

    /// <summary>A run as a base plus three features (run types, DECISIONS 576): Groomed, Snowmaking, Lights. Snowmaking and Lights
    /// need Groomed, and a cat track is always groomed (<see cref="Of"/> enforces both). Not saved: the kind and the saved prop settings
    /// (SkiRunPropSettings) hold everything it is built from. The per-kind rules are wrappers over the feature rules, so a kind and its
    /// features can never disagree; tests/ResortBatch/RunFeaturesTests.cs holds the golden table of the kind answers.
    /// default(RunFeatures) is <see cref="Unknown"/>: a kind outside the enum keeps the legacy unknown answers (no fee, no upkeep, type
    /// factor 1, freezing threshold, not counted).</summary>
    public readonly struct RunFeatures : IEquatable<RunFeatures>
    {
        public readonly RunBase Base;
        /// <summary>False only for <see cref="Unknown"/>; every other field is then false.</summary>
        public readonly bool Known;
        public readonly bool Groomed, Snowmaking, Lights;

        private RunFeatures(RunBase runBase, bool groomed, bool snowmaking, bool lights)
        {
            Base = runBase;
            Known = true;
            Groomed = groomed || runBase == RunBase.CatTrack;
            Snowmaking = snowmaking && Groomed;
            Lights = lights && Groomed;
        }

        public static RunFeatures Unknown => default(RunFeatures);

        /// <summary>A known run of this base; Snowmaking and Lights without Groomed are dropped, a cat track is groomed.</summary>
        public static RunFeatures Of(RunBase runBase, bool groomed, bool snowmaking, bool lights)
        {
            return new RunFeatures(runBase, groomed, snowmaking, lights);
        }

        /// <summary>The features of a run kind (the parity shim). Groomed: groomed. Snowmaking: groomed + snowmaking. Night: groomed +
        /// snowmaking + lights. Natural: a piste and nothing else. Cat track: the cat track base, groomed, snowmaking while its guns are on,
        /// lights while lit. <paramref name="gunsOn"/>: a cat track's gun switch (SkiRunPropSystem.CatTrackGunsOn), or false for a gun run
        /// whose guns may not run (a run passes true: its kind already says whether its guns are on, SnowKind, DECISIONS 577). <paramref name="lit"/>: a cat track's
        /// light spacing is above Off; runs ignore it (a night run is lit by its kind). Anything else: <see cref="Unknown"/>.</summary>
        public static RunFeatures FromKind(int kind, bool gunsOn = true, bool lit = false)
        {
            switch (kind)
            {
                case (int)RunKind.Groomed: return Of(RunBase.Piste, true, false, false);
                case (int)RunKind.Snowmaking: return Of(RunBase.Piste, true, gunsOn, false);
                case (int)RunKind.Night: return Of(RunBase.Piste, true, gunsOn, true);
                case (int)RunKind.Natural: return Of(RunBase.Piste, false, false, false);
                case (int)RunKind.CatTrack: return Of(RunBase.CatTrack, true, gunsOn, lit);
                default: return Unknown;
            }
        }

        /// <summary>The features of a built run (run types R2, DECISIONS 577): its prefab kind gives the base and Groomed (every piste prefab but
        /// Natural is groomed), its spacings give the rest: Snowmaking while the gun spacing is above Off, Lights while the light spacing is
        /// above Off (RunPropPlacerRules.SpacingOff = 0). A natural run has neither (Of drops them: both need Groomed). So the Snowmaking and
        /// Night prefabs are presets: their default spacings (RunPropPlacerRules.DefaultGunSpacing / DefaultLightSpacing) give FromKind's
        /// features, and a slider moves them.</summary>
        public static RunFeatures FromSpacings(int kind, int gunSpacingM, int lightSpacingM)
        {
            RunFeatures k = FromKind(kind);   // the base and Groomed; Unknown stays Unknown
            return k.Known ? Of(k.Base, k.Groomed, gunSpacingM > 0, lightSpacingM > 0) : Unknown;
        }

        /// <summary>The kind whose snow rules equal these features' (the snow rules read the base, Groomed and Snowmaking, never Lights):
        /// cat track, natural, Night (snowmaking + lights: the injection bills it on its own counter), Snowmaking, Groomed (a lit groomed run
        /// too); 255 for Unknown. A cat track's gun switch still travels on its own (gunsOn).</summary>
        public RunKind SnowKind
        {
            get
            {
                if (!Known)
                    return (RunKind)255;
                if (Base == RunBase.CatTrack)
                    return RunKind.CatTrack;
                if (!Groomed)
                    return RunKind.Natural;
                if (Snowmaking)
                    return Lights ? RunKind.Night : RunKind.Snowmaking;
                return RunKind.Groomed;
            }
        }

        /// <summary>The kind whose score answers equal these features' (SkiResortScore reads the base, Groomed and Lights, never Snowmaking):
        /// cat track, natural, Night for any lit piste, else Groomed; 255 for Unknown. SkiResortScore.RunInput.Kind carries it. Snowmaking
        /// is set to Lights so the snow kind lands on Night (lit) or Groomed (dark).</summary>
        public byte ScoreKind => Known ? (byte)Of(Base, Groomed, Lights, Lights).SnowKind : (byte)255;

        public bool IsPiste => Known && Base == RunBase.Piste;
        public bool IsCatTrack => Known && Base == RunBase.CatTrack;
        /// <summary>An ungroomed piste (the Natural kind).</summary>
        public bool IsNatural => IsPiste && !Groomed;

        public bool Equals(RunFeatures other)
        {
            return Base == other.Base && Known == other.Known && Groomed == other.Groomed && Snowmaking == other.Snowmaking && Lights == other.Lights;
        }

        public override bool Equals(object obj)
        {
            return obj is RunFeatures other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (int)Base | (Known ? 2 : 0) | (Groomed ? 4 : 0) | (Snowmaking ? 8 : 0) | (Lights ? 16 : 0);
        }

        /// <summary>"piste+groomed+snowmaking", "cat track+groomed", "unknown". Allocates: logs and tests only.</summary>
        public override string ToString()
        {
            if (!Known)
                return "unknown";
            return (Base == RunBase.CatTrack ? "cat track" : "piste") + (Groomed ? "+groomed" : "") + (Snowmaking ? "+snowmaking" : "") +
                   (Lights ? "+lights" : "");
        }
    }

    /// <summary>Per-kind rules of the cat track in one pure place. Since 2026-10-24 a cat track counts minimally: a member run of its
    /// resort (Runs, RunsOpen, the difficulty bar, the "no open run" lift rule) rated green by construction (the cat track tool grades it under
    /// 25 %, SkiRunRatingRules.CatTrackBand), worth a quarter of a run its size in the score and the capacity
    /// (SkiResortScore.CatTrackFactor / CatTrackSkiersFactor), and nothing else: no variety or school green, no vertical, it does
    /// not end the summer mode (SkiResortScore.Compute). The int overloads wrap the RunFeatures ones (RunFeatures.FromKind).</summary>
    public static class RunKindRules
    {
        /// <summary>Whether a run of this kind is a member of its resort's numbers (RunInput: RunsCapacity, the attraction score, Runs /
        /// RunsOpen / RunsPartly, so HubRules.Status, and the "no open run" lift-closed rule). A whitelist: an unknown kind counts false.
        /// The cat track's reduced weight lives in SkiResortScore.</summary>
        public static bool CountsTowardResort(RunFeatures f) => f.Known;

        public static bool CountsTowardResort(int kind) => CountsTowardResort(RunFeatures.FromKind(kind));

        /// <summary>LiftServedRule's cat track mask (RunLinks.Build / AssignLinked, rule 3, DECISIONS 405): a cat track carries service
        /// level or downhill (ClimbToleranceM) to the runs it meets lower down; it needs no drop below a junction of its own.</summary>
        public static bool IsCatTrackLink(RunFeatures f) => f.IsCatTrack;

        public static bool IsCatTrackLink(int kind) => IsCatTrackLink(RunFeatures.FromKind(kind));

        /// <summary>True when the snow system injects, measures or bills a run: a groomed piste. The cat track is none of these: it
        /// opens by the day-mean estimate alone (RunSnowRules.ThresholdC) and is never rasterized; a natural run is never injected.</summary>
        public static bool HasSnowInjection(RunFeatures f) => f.IsPiste && f.Groomed;

        public static bool HasSnowInjection(int kind) => HasSnowInjection(RunFeatures.FromKind(kind));
    }
}
