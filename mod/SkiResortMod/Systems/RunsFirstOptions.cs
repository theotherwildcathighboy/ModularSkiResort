// RunsFirst options: every knob of the planner in one plain object, with today's defaults. The planner reads only this object
// (RunsFirstPlanner.O, set by Plan); it never reads the environment. The test host fills it from the RF_* environment switches
// (RunsFirst.HostOptions in tests/AutoGenBatch/RunsFirst.Host.cs), so experiments still work there; the mod will pass its own. The comment after each field names its switch.
// Density presets (port round R2-a, plan 7.4): ForDensity sets the pick's lines per km2 and the serve cap; Normal is the default. The
// tool plans with ForDensity(knob) and logs Fingerprint() per scan. Pure C# 9, net48-clean.

using System;
using System.Globalization;
using System.Reflection;

namespace SkiResortMod.Systems
{
    public sealed class RunsFirstOptions
    {
        // ---- lines and the pick
        public float PicksPerKm2 = 5f;            // RF_PPK: line cap per km2 (12-80), the Normal density
        public int MixMode;                       // RF_MIXW: 0 fixed band weights, 1 / 2 the region's ground mix
        public float MixGreenMin = 1.5f;          // RF_MIXGREEN
        // RF_RATER: which band decisions read the mod rater's band of a run (the band scorecard v7 reads and the game shows), one bit
        // each (RunsFirstPlanner.Rater.cs). Default 131: 1 the result's band, 2 the ski graph (way home, beginner, learning km), 128 a
        // learning chair's further green runs. Measured and left off (each lowered a set mean, port step R2-p1): 4 green ways down and pods,
        // 8 the serve value, 16 link runs, 32 chords, 64 the line pick, 256 a learning chair's own run. 0: the planner's own predictors
        public int RaterBands = 131;

        // ---- lifts
        public float TopLatticeM = 100f;          // RF_LAT
        public float ReturnM = 150f;              // RF_RET
        public float ReachBudget = 15e6f;         // RF_RBUDGET
        public float PadReachM = 70f;             // RF_PADR
        public int ScanThreads = 4;               // RF_THREADS: threads for the top lattice's reach scan (at least 1)
        public int TypeUse = 2;                   // RF_TYPEUSE
        public float ChairMaxRise = 700f;         // RF_CHAIRRISE
        public float VeryLongM = 2500f;           // RF_VERYLONG
        public float TramRise = 800f;             // RF_TRAMRISE
        public float GondolaCap = 0.6f;           // RF_GONDCAP
        public float LiftFallW;                   // RF_LIFTFALL
        public float LiftsPerKm2 = 1.1f;          // RF_LPK
        public float StopShare = 0.05f;           // RF_STOP
        public float EdgeLiftM = 45f;             // RF_EDGELIFT
        public int TransferBeside = 1;            // RF_TRANSFERBESIDE
        public int OwnBottomTries = 8;            // RF_OWNBOTTOMTRIES
        public float MaxRiseM;                    // RF_MAXRISE
        public bool CoverLifts = true;            // RF_NOCOVERLIFT=1 turns off
        public bool Below = true;                 // RF_NOBELOW=1 turns off (the lattice flow below a top)
        public bool Spread = true;                // RF_NOSPREAD=1 turns off
        public bool SpreadAxis = true;            // RF_NOSPREADAXIS=1 turns off
        public bool SpreadEach = true;            // RF_NOSPREADEACH=1 turns off
        public bool NoParallel = true;            // RF_PARALLELOK=1 turns off
        public bool OwnBottom = true;             // RF_OWNBOTTOM=0 turns off
        public bool Village = true;               // RF_NOVILLAGE=1 turns off

        // ---- coverage lifts
        public bool CoverNewBase;                 // RF_CNEWBASE=1 turns on
        public float CoverLiftShare = 0.015f;     // RF_CLS
        public int CoverLiftMinPts = 120;         // RF_CLM
        public float CoverParallelM = 170f;       // RF_CPAR
        public float CoverClearM = 100f;          // RF_CCLR
        public float CoverFallW = 0.8f;           // RF_COVERFALL
        public bool CoverOwnPads = true;          // RF_COVEROWNPAD=0 turns off
        public int PodHold = 2;                   // RF_PODHOLD

        // ---- the plan's passes
        public float ServeCapPerKm2 = 8f;         // RF_SCAP
        public float RunOutM = 1600f;             // RF_RUNOUT
        public float ChordDefer = 0.35f;          // RF_CDEF
        public float ChordKeep = 0.5f;            // RF_CKEEP
        public bool Spurs = true;                 // RF_NOSPUR=1 turns off
        public bool Pods = true;                  // RF_NOPOD=1 turns off
        public bool Bridges = true;               // RF_NOBRIDGE=1 turns off
        public bool LearningPods;                 // RF_LEARNPOD=1 turns on
        public bool WayHome = true;               // RF_NOWAYHOME=1 turns off
        public bool Downloads = true;             // RF_NODOWNLOAD=1 turns off
        public bool DropStranded = true;          // RF_NODROP=1 turns off
        public bool Regrade = true;               // RF_NOREGRADE=1 turns off
        public bool GradedWays = true;            // RF_NOGRADEDWAY=1 turns off
        public bool Fill = true;                  // RF_NOFILL=1 turns off
        public bool Chords = true;                // RF_NOCHORD=1 turns off (and with it nudge, chord tracks, chord drop)
        public bool Nudge = true;                 // RF_NONUDGE=1 turns off
        public bool ChordTracks = true;           // RF_NOCHORDTRACK=1 turns off
        public bool ChordDrop = true;             // RF_NOCHORDDROP=1 turns off
        public bool Merge = true;                 // RF_NOMERGE=1 turns off
        public bool Prune = true;                 // RF_NOPRUNE=1 turns off
        public bool Straight = true;              // RF_NOSTRAIGHT=1 turns off
        public bool Junctions;                    // RF_JUNCTION=1 turns on
        public bool Heads;                        // RF_HEAD=1 turns on
        public bool LearningAreas = true;         // RF_NOLEARN=1 turns off
        public float NudgeMaxM = 350f;            // RF_NUDGEM
        public float BridgeFallW = 2f;            // RF_BRIDGEFALL
        public bool GradedLinks = true;           // RF_NOGRADEDLINK=1 turns off
        public float HeadForwardM = 60f;          // RF_HFWD
        public float JunctionBackM = 60f;         // RF_JBACK

        // ---- easy ground, beginner and learning
        public float EasyUpTol = 0.10f;           // RF_UPTOL
        public float EasyBudget = 3.0e6f;         // RF_EBUDGET: cells the easy-ground searches may settle per region
        public float LinkBudget = 1.8e6f;         // RF_LBUDGET
        // cells the way-home pass may settle, a budget of its own (0: it spends the shared EasyBudget, the plans before R2-p2). With
        // the rater on the ski graph more lifts read without a way home, and on big-4 the way home spent all of the shared budget:
        // the regrade and the learning ways then found nothing (69 legs past the budget, -11.2)
        public float WayHomeBudget = 3.0e6f;      // RF_WBUDGET
        public float TrackTurnM = 40f;            // RF_TURNM
        public float LearnGround = 0.235f;        // RF_LGROUND
        public float LearnMinRise = 32f;          // RF_LMINRISE
        public float LearnMaxRise = 130f;         // RF_LMAXRISE
        public float LearnMinLen = 180f;          // RF_LMINLEN
        public float LearnRouteMax = 1500f;       // RF_LROUTE
        public float LearnCrowd = 0.45f;          // RF_LCROWD
        public float LearnFallDeg = 35f;          // RF_LFALL
        public bool LearnHigh = true;             // RF_NOLEARNHIGH=1 turns off
        public bool LearnAbove;                   // RF_LEARNABOVE=1 turns on
        public bool LearnConvert;                 // RF_LEARNCONVERT=1 turns on

        // ---- way home
        public bool V3Home;                       // RF_V3HOME=1 turns on
        public bool WayPeakOnly;                  // RF_WAYPEAK=1 turns on
        public bool PeakWayAgain;                 // RF_PEAKAGAIN=1 turns on
        public bool WayGreenFirst;                // RF_WAYGREEN1=1 turns on

        // ---- fill, graded, layout, regrade
        public float FillMinM = 130f;             // RF_FILLMIN
        public float FillCrowdTrade = 0.25f;      // RF_FTRADE
        public bool FaceLines = true;             // RF_NOFACE=1 turns off
        public bool Diagonals = true;             // RF_NODIAG=1 turns off
        public int NaturalMode = 2;               // RF_NATURAL
        public float NaturalHostile = 0.4f;       // RF_NATHOSTILE
        public float StraightDevM = 30f;          // RF_STRDEV
        public bool NoLoneBase;                   // RF_LONEBASE=1 turns on
        public float StackDegM = 33f;             // RF_STACKDEG
        public float StackTopMm = 330f;           // RF_STACKTOP
        public float StackBottomMm = 280f;        // RF_STACKBOT
        public bool NoStacked = true;             // RF_STACKED=0 turns off
        public float RegradeStretch = 1.8f;       // RF_RGSTRETCH
        public float RegradeMinGain = 0.005f;     // RF_RGMIN
        public float PruneMinGain = 0.01f;        // RF_PRUNEMIN

        // ---- debug output only (console lines and map dots; no effect on the plan's runs, lifts or connectors)
        public bool Debug;                        // RF_DEBUG
        public bool Timing;                       // RF_TIME
        public bool DebugTops;                    // RF_TOPDBG (with RF_DEBUG)
        public bool DebugTwins;                   // RF_TWINDBG (with RF_DEBUG)
        public bool DebugTopList;                 // RF_TOPS
        public bool DebugConvert;                 // RF_CONVDBG
        public bool DebugV3All;                   // RF_V3ALL
        public bool DebugWayHome;                 // RF_WHDBG
        public bool DebugChords;                  // RF_CHORDDBG
        public bool DebugModel;                   // RF_MODELDBG
        public bool DebugCover;                   // RF_COVDBG
        public bool DebugCoverMap;                // RF_COVER
        public bool DebugCrowd;                   // RF_CROWD
        public bool DebugV3;                      // RF_V3DBG
        public bool DebugTurns;                   // RF_TURNDBG

        public RunsFirstOptions Clone() => (RunsFirstOptions)MemberwiseClone();

        // ---- density presets (the hub's Density knob). Normal = the scored defaults above
        // Measured in the host (rf-scan, port round R2-a): the line and serve caps alone barely move a plan, because the fill pass
        // covers the open faces again. Sparse also turns the fill and the face lines off. Dense has no stronger lever in the options:
        // the spacing of the lines is a planner constant (RunsFirstPlanner.SpacingM, FillCrowdM). LiftsPerKm2 did not bind on the field test 5 regions.
        public const float SparsePicksPerKm2 = 3.5f, SparseServeCapPerKm2 = 5.5f;
        public const float DensePicksPerKm2 = 7f, DenseServeCapPerKm2 = 11f;

        /// <summary>The defaults with the density's line cap and serve cap (Sparse: no fill). Normal: exactly the defaults (the scored plan).</summary>
        public static RunsFirstOptions ForDensity(AutoDensity density)
        {
            var o = new RunsFirstOptions();
            switch (density)
            {
                case AutoDensity.Sparse:
                    o.PicksPerKm2 = SparsePicksPerKm2;
                    o.ServeCapPerKm2 = SparseServeCapPerKm2;
                    o.Fill = false;        // no backfill of the uncovered faces: the picked lines only
                    o.FaceLines = false;
                    break;
                case AutoDensity.Dense:
                    o.PicksPerKm2 = DensePicksPerKm2;
                    o.ServeCapPerKm2 = DenseServeCapPerKm2;
                    break;
            }
            return o;
        }

        /// <summary>FNV-1a 64 over every public field by name (ordinal order), so a new option changes the print without a list to
        /// keep. Floats by their bits. The tool logs it per scan and keys its plan cache with it.</summary>
        public ulong Fingerprint()
        {
            ulong h = 14695981039346656037UL;
            void U(uint v)
            {
                for (int b = 0; b < 4; b++)
                {
                    h ^= (v >> (8 * b)) & 0xFFu;
                    h *= 1099511628211UL;
                }
            }
            FieldInfo[] fields = typeof(RunsFirstOptions).GetFields(BindingFlags.Public | BindingFlags.Instance);
            Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));
            foreach (FieldInfo f in fields)
            {
                foreach (char c in f.Name)
                    U(c);
                object v = f.GetValue(this);
                if (v is float fl)
                    U(BitConverter.ToUInt32(BitConverter.GetBytes(fl), 0));
                else if (v is int i)
                    U((uint)i);
                else if (v is bool bo)
                    U(bo ? 1u : 0u);
                else
                    foreach (char c in Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty)
                        U(c);
            }
            return h;
        }
    }
}
