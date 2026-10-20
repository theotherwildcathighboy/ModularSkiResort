// StationLvRule — the PURE rule of the base stations' "low voltage not connected" icon (T117 follow-up, Richard 2026-10-20:
// "I connected the fixed-grip bottom station via LV and the icon demanding an LV connection never went away, but it was
// powered"; DECISIONS 347 (d) "LV wire only"). Build note docs/research/field/2026-10-20/sim-round-build.md §3. Created
// 2026-10-20. Used by StationLvIconSystem; linked into tests/LiftBatch (LV-1..LV-4).
//
// THE ICON is vanilla: ElectricityParameterData.m_LowVoltageNotConnectedPrefab, written by Game.Net.ConnectionWarningSystem
// on the station with the stub node as target (UpdateConnectionWarning, ConnectionWarningSystem.cs:2376-2401). It is a topology
// check, not a power check: a station's LV sub-net node counts as connected only when an edge NOT owned by the station ends AT
// that node (CheckConnectedLayers / FindEdgeConnections, :1845-1877, :2286-2310); FindSecondaryConnections (the nodes lying on
// an edge) runs only for layers the node does not require (:1878-1881), and the LV stub node requires PowerlineLow.
// THE POWER PATH counts more: the LV stub (SkiLiftMarkerLV, ElectricityConnection Low) local-connects any LV node within its
// snap radius onto its middle (NetData.m_LocalConnectLayers |= PowerlineLow, snap 4 m, NetInitializeSystem.cs:2412-2454;
// GenerateEdgesSystem.cs:1805-1856 adds a ConnectedNode), and ElectricityEdgeGraphSystem feeds the stub's middle flow node from
// every ConnectedNode (CreateEdgeMiddleNodeConnections, :86-99). So a wire whose end lands on the stub's middle powers the
// station (DUMP "Connected 1224 / 1224") while the warning still counts the stub as unconnected. The FG base's LV stub lies fully
// outside its mesh (z -7.5..-10.5, face -6.79; T117 table), so a wire can land mid-stub; the tram base's stub sits inside the
// mesh, so its wire can only reach the tip NODE, which the warning counts (the tram's icon cleared).
//
// THE RULE: an LV wire attached to the LV stub (at a node or on its middle) means the station is LV-connected; a vanilla LV icon
// on it is then wrong and is removed. No wire: the icon stays (DQ / gondola of 10-19: "needs an LV connection" is the truth).
//
// PURE FILE: System only.

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
