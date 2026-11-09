// Pure rule behind the base stations' vanilla "low voltage not connected" icon (used by StationLvIconSystem). System only.
// The icon (ElectricityParameterData.m_LowVoltageNotConnectedPrefab) is a topology check, not a power check.
// ConnectionWarningSystem (UpdateConnectionWarning, ConnectionWarningSystem.cs:2376-2401) counts the stub node as connected only
// when an edge not owned by the station ends AT that node (CheckConnectedLayers / FindEdgeConnections, :1845-1877, :2286-2310).
// It skips mid-edge connections for layers the node requires (:1878-1881), and the LV stub node requires PowerlineLow.
// The power path counts more: a wire landing on the stub's middle becomes a ConnectedNode (NetInitializeSystem.cs:2412-2454,
// GenerateEdgesSystem.cs:1805-1856) and powers the station (CreateEdgeMiddleNodeConnections, :86-99) while the icon stays.
// Rule: an LV wire attached to the stub (at a node or on its middle) means the station is LV-connected, so the system removes the icon.
// With no wire, the icon stays.
// The LV stub marker (StationLvMarkerSystem) reads the same wire state: MarkStyle picks its look per mode, MarkScale its size.

namespace SkiResortMod.Systems
{
    /// <summary>How an LV wire reaches a station's LV stub.</summary>
    public enum LvStubWire : byte { None = 0, AtNode = 1, MidStub = 2 }

    /// <summary>Why the LV stub marker draws. PowerView = the LV power-line tool or the electricity infoview (every base station);
    /// Selected = one selected base station; Placing = the object tool's ghost of a base station.</summary>
    public enum LvMarkMode : byte { None = 0, PowerView = 1, Selected = 2, Placing = 3 }

    /// <summary>The look of one mark: the warning color for no wire, green when wired (faint where it is done), yellow for a ghost.</summary>
    public enum LvMarkStyle : byte { None = 0, Unwired = 1, WiredFaint = 2, Wired = 3, Ghost = 4 }

    public static class StationLvRule
    {
        /// <summary><paramref name="lvEdgesAtStubNodes"/> = LV edges not owned by the station ending at an LV stub node.
        /// <paramref name="lvNodesOnStub"/> = LV nodes not owned by the station in the LV stub edge's ConnectedNode buffer.</summary>
        public static LvStubWire Classify(int lvEdgesAtStubNodes, int lvNodesOnStub)
        {
            return lvEdgesAtStubNodes > 0 ? LvStubWire.AtNode : lvNodesOnStub > 0 ? LvStubWire.MidStub : LvStubWire.None;
        }

        /// <summary>The vanilla "low voltage not connected" icon on the station is removed when a wire reaches the stub.</summary>
        public static bool ClearsVanillaIcon(bool hasLvIcon, LvStubWire wire)
        {
            return hasLvIcon && wire != LvStubWire.None;
        }

        /// <summary>The marker's look for one stub. A ghost is never wired. No wire is always loud. A wire is faint in the power view
        /// (nothing to do there) and full when the player selected that station.</summary>
        public static LvMarkStyle MarkStyle(LvMarkMode mode, LvStubWire wire)
        {
            switch (mode)
            {
                case LvMarkMode.Placing: return LvMarkStyle.Ghost;
                case LvMarkMode.PowerView: return wire == LvStubWire.None ? LvMarkStyle.Unwired : LvMarkStyle.WiredFaint;
                case LvMarkMode.Selected: return wire == LvStubWire.None ? LvMarkStyle.Unwired : LvMarkStyle.Wired;
                default: return LvMarkStyle.None;
            }
        }

        /// <summary>The mode for this frame, by priority: placing a base station, then the power view, then a selected base station.
        /// Off when the lift building aids are off.</summary>
        public static LvMarkMode Decide(bool aidsOn, bool placingBase, bool lvToolOrPowerView, bool baseSelected)
        {
            if (!aidsOn)
                return LvMarkMode.None;
            if (placingBase)
                return LvMarkMode.Placing;
            if (lvToolOrPowerView)
                return LvMarkMode.PowerView;
            return baseSelected ? LvMarkMode.Selected : LvMarkMode.None;
        }

        /// <summary>Ring diameter at the stub midpoint (m). The stub is 3 m, so the ring encloses it with room for the wire end.</summary>
        public const float MarkDiameterM = 9f;
        /// <summary>Camera zoom (m) up to which the mark keeps its size; farther out it grows with the zoom, up to MarkMaxScale.</summary>
        public const float MarkRefZoomM = 400f;
        public const float MarkMaxScale = 6f;

        /// <summary>The size factor of the mark at camera <paramref name="zoom"/> (m). 1 near, linear past MarkRefZoomM, capped. A zoom of
        /// 0 or less (no camera controller) or NaN draws at 1.</summary>
        public static float MarkScale(float zoom)
        {
            if (!(zoom > MarkRefZoomM))
                return 1f;
            float k = zoom / MarkRefZoomM;
            return k < MarkMaxScale ? k : MarkMaxScale;
        }

        /// <summary>Log / DUMP words.</summary>
        public static string Describe(LvStubWire wire)
        {
            switch (wire)
            {
                case LvStubWire.AtNode: return "LV wire at the stub node";
                case LvStubWire.MidStub: return "LV wire on the stub's middle (local connection; the vanilla warning does not count it)";
                default: return "no LV wire on the stub";
            }
        }
    }
}
