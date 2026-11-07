// Pure rule behind PowerJoinRefusalSystem: may a vanilla power line Temp edge be built, or does it join one of our lift lines?
// Our four lift lines are PowerLinePrefab nets with ElectricityConnection Voltage High. Therefore a vanilla HV line gets the same net
// layers (PowerlineHigh required + connect, NetInitializeSystem.cs:2420-2425). NetUtils.CanConnect (NetUtils.cs:499-506) lets it snap onto
// a lift node or split a lift edge. A vanilla LV line cannot: its required layer is PowerlineLow (:2416-2419), which our PowerlineHigh
// connect layers do not contain. So CanConnect fails both ways. LV only reaches a station's LV stub (station power).
// "Lift side" = a catalog lift line prefab or the station HV stub marker (SkiLiftMarker): city HV on either bridges into a lift.
// The system gathers the facts per Temp edge. This file decides. The system only reads the verdict.

namespace SkiResortMod.Systems
{
    /// <summary>How one end node of a vanilla HV Temp edge meets a lift (strongest first).</summary>
    public enum PowerJoinTouch : byte
    {
        None = 0,
        /// <summary>The node (or the existing node its Temp copies) already has a lift-side edge: a join at a tower or stub.</summary>
        LiftNode = 1,
        /// <summary>The node ends a lift-side Temp edge: the vanilla line splits a lift section here.</summary>
        LiftSplit = 2,
        /// <summary>The node sits in a lift-side Temp edge's ConnectedNode buffer: a local connection on its middle.</summary>
        LiftMidEdge = 3,
    }

    public enum PowerJoinVerdict : byte
    {
        /// <summary>Not ours to judge: not a vanilla HV line, a lift-side or station-owned edge, a delete, or an unchanged copy.</summary>
        Skip = 0,
        /// <summary>A new vanilla HV edge that does not touch a lift.</summary>
        Allow = 1,
        /// <summary>Refuse: an end node joins a lift (PowerJoinTouch.LiftNode / LiftSplit / LiftMidEdge).</summary>
        RefuseJoin = 2,
        /// <summary>Refuse: the edge replaces a lift-side edge (the net tool's Replace mode with a vanilla line).</summary>
        RefuseReplace = 3,
    }

    /// <summary>What the system reads for one Temp edge (Temp flags, prefab class, owner, original, end nodes).</summary>
    public struct PowerJoinFacts
    {
        /// <summary>The prefab has ElectricityConnectionData with Voltage High.</summary>
        public bool HighVoltage;
        /// <summary>The prefab is a catalog lift line or the HV stub marker.</summary>
        public bool LiftSide;
        /// <summary>The edge's top owner is one of our catalog stations (its stub sub-nets).</summary>
        public bool OwnedByOurStation;
        /// <summary>TempFlags.Delete: a bulldoze or a removed piece. Never refused (it is how a wrong join is undone).</summary>
        public bool Deleting;
        /// <summary>TempFlags.Create: a new edge (an untouched Temp copy of an existing edge has no Create).</summary>
        public bool Creates;
        /// <summary>Temp.m_Original is a lift-side edge.</summary>
        public bool OriginalLiftSide;
        public PowerJoinTouch Start, End;
    }

    public static class PowerJoinRule
    {
        /// <summary>The strongest touch of one end node. <paramref name="nodeHasLiftEdge"/>: the node, or the existing node its
        /// Temp copies (Temp.m_Original), has a live lift-side edge in its ConnectedEdge buffer.</summary>
        public static PowerJoinTouch ClassifyNode(bool nodeHasLiftEdge, bool endsLiftTempEdge, bool onLiftTempEdgeMiddle)
        {
            if (nodeHasLiftEdge)
                return PowerJoinTouch.LiftNode;
            if (endsLiftTempEdge)
                return PowerJoinTouch.LiftSplit;
            return onLiftTempEdgeMiddle ? PowerJoinTouch.LiftMidEdge : PowerJoinTouch.None;
        }

        public static PowerJoinVerdict Decide(in PowerJoinFacts f)
        {
            if (!f.HighVoltage || f.LiftSide || f.OwnedByOurStation || f.Deleting)
                return PowerJoinVerdict.Skip;
            if (f.OriginalLiftSide)
                return PowerJoinVerdict.RefuseReplace;
            if (!f.Creates)
                return PowerJoinVerdict.Skip;
            return f.Start != PowerJoinTouch.None || f.End != PowerJoinTouch.None ? PowerJoinVerdict.RefuseJoin : PowerJoinVerdict.Allow;
        }

        public static bool Refuses(PowerJoinVerdict v) => v == PowerJoinVerdict.RefuseJoin || v == PowerJoinVerdict.RefuseReplace;

        /// <summary>Log words for one end.</summary>
        public static string Describe(PowerJoinTouch t)
        {
            switch (t)
            {
                case PowerJoinTouch.LiftNode: return "joins a lift tower or stub node";
                case PowerJoinTouch.LiftSplit: return "splits a lift section";
                case PowerJoinTouch.LiftMidEdge: return "connects to the middle of a lift section";
                default: return "free";
            }
        }
    }

    /// <summary>One INFO line per refusal episode: Started on the first refusing evaluation, Ended on the first clear one after it.</summary>
    public struct PowerJoinEpisode
    {
        public enum Event : byte { None = 0, Started = 1, Ended = 2 }

        public bool Active;
        public int Evaluations;   // refusing evaluations in the current episode

        public Event Step(bool refusing)
        {
            if (refusing)
            {
                if (Active)
                {
                    Evaluations++;
                    return Event.None;
                }
                Active = true;
                Evaluations = 1;
                return Event.Started;
            }
            if (!Active)
                return Event.None;
            Active = false;
            return Event.Ended;
        }

        public void Reset()
        {
            Active = false;
            Evaluations = 0;
        }
    }
}
