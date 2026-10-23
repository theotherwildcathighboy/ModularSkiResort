// Pure rule behind the base stations' vanilla "low voltage not connected" icon (used by StationLvIconSystem). System only.
// The icon (ElectricityParameterData.m_LowVoltageNotConnectedPrefab) is a topology check, not a power check: ConnectionWarningSystem
// (UpdateConnectionWarning, ConnectionWarningSystem.cs:2376-2401) counts the stub node as connected only when an edge not owned
// by the station ends AT that node (CheckConnectedLayers / FindEdgeConnections, :1845-1877, :2286-2310); mid-edge connections
// are skipped for layers the node requires (:1878-1881), and the LV stub node requires PowerlineLow.
// The power path counts more: a wire landing on the stub's middle becomes a ConnectedNode (NetInitializeSystem.cs:2412-2454,
// GenerateEdgesSystem.cs:1805-1856) and powers the station (CreateEdgeMiddleNodeConnections, :86-99) while the icon stays.
// Rule: an LV wire attached to the stub (at a node or on its middle) means the station is LV-connected, so the icon is removed.
// No wire: the icon stays.

namespace SkiResortMod.Systems
{
    /// <summary>How an LV wire reaches a station's LV stub.</summary>
    public enum LvStubWire : byte { None = 0, AtNode = 1, MidStub = 2 }

    public static class StationLvRule
    {
        /// <summary><paramref name="lvEdgesAtStubNodes"/> = LV edges not owned by the station ending at an LV stub node;
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
