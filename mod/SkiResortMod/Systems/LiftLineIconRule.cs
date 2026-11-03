// Pure rules behind the lift's notification icons (LiftLineIconSystem, LiftPowerCueSystem). System only; tests/LiftBatch LI-1..LI-3.
// Lifts are built on the power-line net class, so vanilla treats them as high-voltage lines and their stations as transformers. The rule:
// a lift shows no vanilla electricity cue except the base station's own power ones (its "low voltage not connected" icon and its
// consumer warnings: the base takes the lift's power from its low-voltage feed like any building). The top station shows none: its power
// comes through the lift line, so the line icon is its only cue. The vanilla "high voltage not connected" icon becomes our own "needs a
// lift line" icon on the same attach points:
//  - a station: its lift stub nodes (the SkiLiftMarker sub-net, the nodes ConnectionWarningSystem targets, ConnectionWarningSystem.cs:
//    2355-2401) with no lift edge from outside the station;
//  - a loose lift end: a lift node outside any station with exactly one lift edge (vanilla's own "connected once" test on the node,
//    UpdateNodeConnectionWarnings :1974-2010; a tower node has two).
// Which vanilla icon is stripped where: Strips (the icon prefabs are ElectricityParameterData's fields, ElectricityParameterData.cs:14-32).

namespace SkiResortMod.Systems
{
    /// <summary>The vanilla electricity notification icons, by their ElectricityParameterData field.</summary>
    public enum VanillaPowerIcon : byte
    {
        Other = 0,
        HighVoltageNotConnected = 1,   // ConnectionWarningSystem.cs:2367
        LowVoltageNotConnected = 2,    // ConnectionWarningSystem.cs:2366
        NoElectricity = 3,             // DispatchElectricitySystem.cs:141 (a consumer short of power)
        BuildingBottleneck = 4,        // DispatchElectricitySystem.cs:142 (a consumer behind a bottleneck)
        Transformer = 5,               // ElectricityStatusSystem.cs:132 (a building with ElectricityBuildingConnection)
        NotEnoughProduction = 6,       // ElectricityStatusSystem.cs:133
        Bottleneck = 7,                // ElectricityStatusSystem.cs:66 (a net) and :134 (a building)
    }

    /// <summary>What carries the icon.</summary>
    public enum LiftIconOwner : byte { BaseStation = 0, TopStation = 1, LiftNode = 2, LiftEdge = 3 }

    /// <summary>What the "needs a lift line" icon must do on one owner.</summary>
    public enum LineIconAction : byte { None = 0, Show = 1, Clear = 2 }

    public static class LiftLineIconRule
    {
        /// <summary>A station needs a lift line while none of its lift stub nodes has a lift edge from outside the station.
        /// <paramref name="stubNodes"/> = the station's lift stub nodes; <paramref name="liftEdgesAtStubs"/> = lift edges not owned by the
        /// station ending at them. A station without stub nodes (none found) never shows the icon.</summary>
        public static bool StationNeedsLine(int stubNodes, int liftEdgesAtStubs)
        {
            return stubNodes > 0 && liftEdgesAtStubs <= 0;
        }

        /// <summary>A lift node outside any station needs a line when exactly one lift edge reaches it (a cut section's end, a lift
        /// whose station was removed). Two or more: a tower between spans. Zero: a node about to go (no edge left).</summary>
        public static bool LooseEndNeedsLine(int liftEdgesAtNode)
        {
            return liftEdgesAtNode == 1;
        }

        /// <summary>The command for one owner from its rule state and the icon it carries.</summary>
        public static LineIconAction Decide(bool needsLine, bool hasIcon)
        {
            if (needsLine)
                return hasIcon ? LineIconAction.None : LineIconAction.Show;
            return hasIcon ? LineIconAction.Clear : LineIconAction.None;
        }

        /// <summary>True when the vanilla icon is removed from that owner. Kept: the base station's low-voltage icon and its consumer
        /// warnings (its own power). Removed: every high-voltage cue, the transformer-building cues a station carries only because it is
        /// built as a transformer (a plain consumer gets its bottleneck warning from DispatchElectricitySystem alone), and the top
        /// station's consumer warnings (its power comes through the line).</summary>
        public static bool Strips(LiftIconOwner owner, VanillaPowerIcon icon)
        {
            bool station = owner == LiftIconOwner.BaseStation || owner == LiftIconOwner.TopStation;
            switch (icon)
            {
                case VanillaPowerIcon.HighVoltageNotConnected:
                case VanillaPowerIcon.Bottleneck:
                    return true;
                case VanillaPowerIcon.Transformer:
                case VanillaPowerIcon.NotEnoughProduction:
                    return station;
                case VanillaPowerIcon.NoElectricity:
                case VanillaPowerIcon.BuildingBottleneck:
                    return owner == LiftIconOwner.TopStation;
                default:
                    return false;
            }
        }

        /// <summary>Log / DUMP words.</summary>
        public static string Describe(LineIconAction action)
        {
            switch (action)
            {
                case LineIconAction.Show: return "show";
                case LineIconAction.Clear: return "clear";
                default: return "none";
            }
        }
    }
}
