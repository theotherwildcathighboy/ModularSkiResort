// Pure part of the run outline overlay; SkiRunOutlineSystem's Burst job calls these. Mirrors the game's area outline
// (Game.Rendering/AreaBorderRenderSystem.cs 1.6.0f1, non-infoview pass :129-136); each rule names its lines.
// The flag constants are the literal values of Game.Tools.TempFlags and Game.Areas.AreaFlags. This file cannot reference Game.dll
// (the test host links it alone), so SkiRunOutlineSystem.OnCreate compares each with the game's enum and refuses to arm (one WARN)
// if a patch changed one. The glade outline color + dash and the run node dot colors / rim live in the "run visuals" block below.
// Pure file: `using Unity.Mathematics;` only. The walks are generic over two small interfaces so the Burst job passes its
// DynamicBuffer and native hash sets and the tests pass managed ones; no allocation here.
// Engine notes: EngineNotes/RunOutlineRules.md

using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>Which of the game's outline colors an area takes (RenderingSettingsData fields).</summary>
    public enum OutlineColour : byte { Hovered = 0, Owner = 1, Warning = 2, Error = 3 }

    /// <summary>The nodes of one area, in buffer order (Game.Areas.Node.m_Position).</summary>
    public interface IOutlineNodes
    {
        int Count { get; }
        float3 Get(int index);
    }

    /// <summary>What a walk writes: the frame's border set (borderMap), its dot set (nodeMap), and the draw calls.</summary>
    public interface IOutlineSink
    {
        void AddBorder(float3 start, float3 end);
        bool HasBorder(float3 start, float3 end);
        /// <summary>True when the position was not yet in the frame's dot set (and adds it).</summary>
        bool AddDot(float3 position);
        void Line(float3 a, float3 b);
        void Dot(float3 position);
    }

    /// <summary>The overlay's pure rules (see engine notes).</summary>
    public static class RunOutlineRules
    {
        public const int MaxSize = 16;              // SkiResortSettings.RunOutlineSize slider max
        public const float WidthPerSize = 0.3f;     // AreaBorderRenderSystem.cs:517, :531

        // Game.Tools.TempFlags (uint), checked against the enum by SkiRunOutlineSystem.OnCreate
        public const uint TempCreate = 0x1u;
        public const uint TempDelete = 0x2u;
        public const uint TempSelect = 0x20u;
        public const uint TempModify = 0x40u;
        public const uint TempReplace = 0x100u;
        public const uint TempHidden = 0x400u;
        public const uint TempParent = 0x800u;
        /// <summary>The flags that make a Temp-only area outlined (:178, :418).</summary>
        public const uint TempShown = TempCreate | TempDelete | TempSelect | TempModify | TempReplace | TempParent;
        /// <summary>The flags that give an area its corner dots (:427).</summary>
        public const uint TempDots = TempCreate | TempModify;

        // Game.Areas.AreaFlags (byte) — checked likewise.
        public const byte AreaComplete = 1;
        public const byte AreaCounterClockwise = 2;
        public const byte AreaSlave = 8;

        // ── Run visuals: the colors and sizes live here and nowhere else ──
        // Tree (glade) tool outlines: a violet-magenta that no band color, game outline color (hovered blue, owner green, warning
        // yellow, error red) or glade ring color (red / orange) uses; opaque enough to read on snow, grass and forest floor.
        // Candidate runs (every bound run, while nothing is hovered) are dashed, the hovered / pending run is solid.
        public static readonly float4 GladeOutlineColour = new float4(0.86f, 0.30f, 1.00f, 0.95f);   // sRGB, as Overlay takes it
        public const float GladeCandidateAlphaFactor = 0.8f;   // dashed candidates: GladeOutlineColour.w × this
        public const float GladeLineWidthM = 2.2f;
        public const float GladeDashM = 9f, GladeGapM = 5f;    // cut on the CPU (PisteMapRules.DashRing)

        // Corner dots of a run being drawn or edited: white with a thin dark rim, so the node to connect to is found on any ground
        // (the game's own dots are 50 % white and stay underneath).
        public static readonly float4 NodeDotFill = new float4(1f, 1f, 1f, 1f);
        public static readonly float4 NodeDotRim = new float4(0.04f, 0.04f, 0.06f, 0.95f);
        public const float NodeRimFraction = 0.10f;            // rim width = this × the dot diameter ...
        public const float NodeRimMinM = 0.12f;                // ... but at least this (meters)

        // While a run is drawn / edited (Temp Create or Modify), the nodes of existing run areas of ours within NeighbourMarginM (xz)
        // of the drawn area's bounds get the same dots, so the corner to connect to is found.
        public const float NeighbourMarginM = 150f;

        public static float4 EmptyBounds() => new float4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);

        public static float4 Extend(float4 b, float3 p)
        {
            return new float4(math.min(b.x, p.x), math.min(b.y, p.z), math.max(b.z, p.x), math.max(b.w, p.z));
        }

        public static bool HasBounds(float4 b) => b.x <= b.z && b.y <= b.w;

        public static bool InWindow(float4 b, float3 p, float marginM)
        {
            return HasBounds(b) && p.x >= b.x - marginM && p.x <= b.z + marginM && p.z >= b.y - marginM && p.z <= b.w + marginM;
        }

        public static float NodeRimWidth(int size)
        {
            float w = NodeRimFraction * DotDiameter(size);
            return w < NodeRimMinM ? NodeRimMinM : w;
        }

        public static int ClampSize(int size)
        {
            return size < 0 ? 0 : size > MaxSize ? MaxSize : size;
        }

        public static bool IsOn(int size) => ClampSize(size) > 0;

        public static float LineWidth(int size) => WidthPerSize * ClampSize(size);

        public static float DotDiameter(int size) => WidthPerSize * ClampSize(size);

        /// <summary>The size-change log rule: "RunOutline: size a -> b" is due when the clamped setting differs from the last logged
        /// size AND this frame would draw. One line per settled change, written at the first frame that would draw (a slider drag
        /// logs once), a switch to 0 included.</summary>
        public static bool SizeChangeDue(int loggedSize, int size, bool wouldDraw)
        {
            return wouldDraw && ClampSize(size) != loggedSize;
        }

        /// <summary>Whether the area is outlined at all. <paramref name="tempFlags"/> is ignored when <paramref name="hasTemp"/> is false.</summary>
        public static bool Outlined(bool chunkError, bool chunkWarning, bool hasTemp, uint tempFlags, byte areaFlags)
        {
            if ((areaFlags & AreaSlave) != 0)
                return false;
            if (chunkError || chunkWarning)
                return true;
            if (!hasTemp)
                return false;
            return (tempFlags & TempShown) != 0 && (tempFlags & TempHidden) == 0;
        }

        public static bool Dots(bool hasTemp, uint tempFlags)
        {
            return hasTemp && (tempFlags & TempDots) != 0;
        }

        /// <summary>Error &gt; Warning &gt; (Temp-only chunk) Parent: Owner &gt; Hovered.</summary>
        public static OutlineColour ColourOf(bool chunkError, bool chunkWarning, uint tempFlags)
        {
            if (chunkError)
                return OutlineColour.Error;
            if (chunkWarning)
                return OutlineColour.Warning;
            return (tempFlags & TempParent) != 0 ? OutlineColour.Owner : OutlineColour.Hovered;
        }

        public static bool ClosingSide(byte areaFlags) => (areaFlags & AreaComplete) != 0;

        /// <summary>The key AddBorders files for the side prev to cur (:227-239, :248-260).</summary>
        public static void AddKey(float3 prev, float3 cur, byte areaFlags, out float3 start, out float3 end)
        {
            if ((areaFlags & AreaCounterClockwise) != 0)
            {
                start = cur;
                end = prev;
            }
            else
            {
                start = prev;
                end = cur;
            }
        }

        /// <summary>The key DrawBorders looks up (:457-465, :490-498): the reverse of AddKey. Found = another outlined area shares
        /// the side, and the game does not draw it.</summary>
        public static void DrawKey(float3 prev, float3 cur, byte areaFlags, out float3 start, out float3 end)
        {
            AddKey(prev, cur, areaFlags, out end, out start);
        }

        /// <summary>Files every side of one outlined area (the closing side when Complete). An area with no node files nothing.</summary>
        public static void AddEdges<TNodes, TSink>(ref TNodes nodes, ref TSink sink, byte areaFlags)
            where TNodes : struct, IOutlineNodes
            where TSink : struct, IOutlineSink
        {
            int n = nodes.Count;
            if (n < 1)
                return;
            float3 prev = nodes.Get(0);
            float3 s, e;
            for (int j = 1; j < n; j++)
            {
                float3 cur = nodes.Get(j);
                AddKey(prev, cur, areaFlags, out s, out e);
                sink.AddBorder(s, e);
                prev = cur;
            }
            if (ClosingSide(areaFlags))
            {
                AddKey(prev, nodes.Get(0), areaFlags, out s, out e);
                sink.AddBorder(s, e);
            }
        }

        /// <summary>Draws one of our outlined areas in the game's order.</summary>
        public static void DrawArea<TNodes, TSink>(ref TNodes nodes, ref TSink sink, byte areaFlags, bool dots)
            where TNodes : struct, IOutlineNodes
            where TSink : struct, IOutlineSink
        {
            int n = nodes.Count;
            if (n < 1)
                return;
            float3 prev = nodes.Get(0);
            if (n == 1)
            {
                if (dots && sink.AddDot(prev))
                    sink.Dot(prev);
                return;
            }
            for (int j = 1; j < n; j++)
            {
                float3 cur = nodes.Get(j);
                Side(ref sink, prev, cur, areaFlags, dots);
                prev = cur;
            }
            if (ClosingSide(areaFlags))
                Side(ref sink, prev, nodes.Get(0), areaFlags, dots);
        }

        private static void Side<TSink>(ref TSink sink, float3 prev, float3 cur, byte areaFlags, bool dots)
            where TSink : struct, IOutlineSink
        {
            if (dots && sink.AddDot(prev))
                sink.Dot(prev);
            DrawKey(prev, cur, areaFlags, out float3 s, out float3 e);
            if (!sink.HasBorder(s, e))
                sink.Line(prev, cur);
            if (dots && sink.AddDot(cur))
                sink.Dot(cur);
        }
    }
}
