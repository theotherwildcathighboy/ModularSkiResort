// The pure rules of the Options button "Delete all ski resort content" (SkiDeleteAllSystem): which prefabs are ours to delete, the
// node verdict that never cuts a vanilla line, the button gate and the one summary line. Pure file (System + SkiCatalog only);
// tests/ResortBatch DeleteAllTests (DA-*) links it. Plan and decompile citations: docs/research/delete-all-2026-10-29.md.

using System.Collections.Generic;
using System.Text;

namespace SkiResortMod.Systems
{
    /// <summary>What a node of a deleted net becomes.</summary>
    public enum DeleteNodeVerdict : byte
    {
        /// <summary>Not ours or owned (a station stub: vanilla's SubElementDeleteSystem decides): untouched.</summary>
        Skip = 0,
        /// <summary>Ours, unowned, every connected edge is one we delete (or none): Deleted.</summary>
        Delete = 1,
        /// <summary>Ours, unowned, but a foreign edge still uses it: kept so the other line stays whole (counted).</summary>
        KeepShared = 2,
    }

    /// <summary>The kinds the summary line counts, in log order.</summary>
    public enum DeleteKind : byte
    {
        LiftEdges = 0, LiftNodes = 1, Stations = 2, Lodges = 3, Schools = 4, Patrols = 5, Signs = 6, Runs = 7,
        FenceEdges = 8, FenceNodes = 9, Trees = 10, SeasonPasses = 11, KeptSharedNodes = 12, Depots = 13, RunProps = 14,
    }

    public static class DeleteAllRules
    {
        /// <summary>The base station's low-voltage feed stub (a PowerLinePrefab). Not a NAMES row: it only ever exists as a station
        /// sub-net, but a stub node detached by vanilla (a player's wire on it) keeps this prefab.</summary>
        public const string ExtraMarkerLV = "SkiLiftMarkerLV";

        public const int KindCount = 15;

        private static readonly string[] s_Labels =
        {
            "lift edges", "lift nodes", "stations", "lodges", "schools", "patrol huts", "signs", "runs",
            "fence edges", "fence nodes", "trees", "season passes removed", "shared nodes kept", "bus depots", "run props",
        };

        /// <summary>One prefab the button deletes: its PrefabID (type, name) and catalog kind.</summary>
        public readonly struct Target
        {
            public readonly string TypeName;
            public readonly string Name;
            public readonly CatalogKind Kind;

            public Target(string typeName, string name, CatalogKind kind)
            {
                TypeName = typeName;
                Name = name;
                Kind = kind;
            }
        }

        /// <summary>Every NAMES row except the carriers (always sub-objects of a line or a station: vanilla deletes them with their
        /// owner), plus the LV marker extra. Selection is by these prefabs only: never by a name prefix, never a vanilla prefab.</summary>
        public static List<Target> Targets()
        {
            var list = new List<Target>(40);
            foreach (CatalogName n in SkiCatalog.AllPrefabNames())
            {
                if (n.Kind == CatalogKind.Carrier || n.Kind == CatalogKind.Grip)
                    continue;
                list.Add(new Target(n.TypeName, n.Name, n.Kind));
            }
            list.Add(new Target(SkiCatalog.TypeLine, ExtraMarkerLV, CatalogKind.Marker));
            return list;
        }

        /// <summary>The counter a top-level building of this kind goes to; false for a non-building kind.</summary>
        public static bool BuildingKindOf(CatalogKind kind, out DeleteKind counter)
        {
            switch (kind)
            {
                case CatalogKind.Station: counter = DeleteKind.Stations; return true;
                case CatalogKind.Lodge: counter = DeleteKind.Lodges; return true;
                case CatalogKind.School: counter = DeleteKind.Schools; return true;
                case CatalogKind.Depot: counter = DeleteKind.Depots; return true;
                case CatalogKind.Patrol: counter = DeleteKind.Patrols; return true;
                case CatalogKind.Sign: counter = DeleteKind.Signs; return true;
                default: counter = DeleteKind.Signs; return false;
            }
        }

        /// <summary>True for the net kinds whose edges and nodes the button deletes (lift lines, markers, the fence).</summary>
        public static bool IsNetKind(CatalogKind kind)
        {
            return kind == CatalogKind.Line || kind == CatalogKind.Marker || kind == CatalogKind.Fence;
        }

        /// <summary>A net node's verdict. ourPrefab: its PrefabRef is a target net; owned: it has an Owner (a station's stub, left to
        /// vanilla); edges: its ConnectedEdge count; deletedEdges: how many of those the button deletes.</summary>
        public static DeleteNodeVerdict NodeVerdict(bool ourPrefab, bool owned, int edges, int deletedEdges)
        {
            if (!ourPrefab || owned)
                return DeleteNodeVerdict.Skip;
            return deletedEdges >= edges ? DeleteNodeVerdict.Delete : DeleteNodeVerdict.KeepShared;
        }

        /// <summary>SkiStubCleanupSystem: an unowned marker node becomes a plain node of the line it carries when it has edges and all of
        /// them are foreign. One of our edges on it (a lift line's loose end at a removed station) keeps it ours.</summary>
        public static bool StubBecomesPlain(int edges, int foreignEdges) => edges > 0 && foreignEdges >= edges;

        /// <summary>The button is live only in a loaded Game-mode city: not in the main menu, not in the Editor, not while loading.</summary>
        public static bool ButtonEnabled(bool gameMode, bool loading)
        {
            return gameMode && !loading;
        }

        /// <summary>The one log line: "DeleteAll: done — 12 lift edges, 14 lift nodes, ... (total 57)". Zero kinds are written too, so
        /// the line always has the same shape.</summary>
        public static string Summary(int[] counts)
        {
            var sb = new StringBuilder(256);
            sb.Append("DeleteAll: done — ");
            int total = 0;
            for (int i = 0; i < KindCount; i++)
            {
                int c = counts != null && i < counts.Length ? counts[i] : 0;
                if (i > 0)
                    sb.Append(", ");
                sb.Append(c).Append(' ').Append(s_Labels[i]);
                if (i != (int)DeleteKind.SeasonPasses && i != (int)DeleteKind.KeptSharedNodes)
                    total += c;
            }
            sb.Append(" (deleted ").Append(total).Append(')');
            return sb.ToString();
        }
    }
}
