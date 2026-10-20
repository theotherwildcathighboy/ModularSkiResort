// RunOutlineRules — the PURE part of the run outline overlay (Round F7 item C1, docs/research/field-test-7-fixes.md;
// research docs/research/field/2026-10-04/findings/run-collisions.md §3.3 / §5 step 3 with its Verification,
// fact 16). SkiRunOutlineSystem's Burst job calls these; tests/ResortBatch RunOutlineTests checks them with literal
// values. Written 2026-10-04 (Round F7, implementer 2).
//
// WHAT IT MIRRORS. The game's area outline, Game.Rendering/AreaBorderRenderSystem.cs (1.6.0f1; identical in 1.6.2f1,
// not in diff-1.6.0-to-1.6.2/changed-files.txt), its non-infoview pass (:129-136). Every rule below names its lines.
//   Outlined     AddBorders :157-187 = DrawBorders :388-437: a chunk with Error or Warning outlines every area in
//                it; a chunk with only Temp outlines an area whose Temp flags hold Create, Delete, Select, Modify,
//                Replace or Parent and not Hidden; a chunk with neither draws nothing; a Slave area never.
//   ColourOf     :388-426: Error before Warning before the Temp-only hovered color; Parent (Temp-only chunks
//                only) takes the owner color.
//   Dots         :427-432: corner dots only for a Temp area flagged Create or Modify (so not for the
//                bulldozer's Delete copy, nor for a permanent area that only carries Error or Warning).
//   ClosingSide  :243, :487: the side from the last node back to the first only when the area is Complete.
//   AddKey / DrawKey  the shared-edge rule (Verification fact 16): AddBorders (:221-262) files every outlined
//                area's edges in one rotational sense (a CounterClockwise area's edges reversed); DrawBorders
//                (:457-465, :490-498) skips an edge when the REVERSED key is filed. Two neighboring outlined
//                areas walk their common edge in opposite senses, so the game draws it for neither, and neither
//                do we.
//   AddEdges / DrawArea  the two walks of one area in the game's order: AddEdges = AddBorders' loop; DrawArea =
//                DrawBorders' loop :445-511 — a 1-node area draws one dot (:446-452); each side draws the dot at
//                its start, the side unless its reverse is filed, the dot at its end, every dot only the FIRST
//                time its position is seen in this frame (the game's nodeMap, :448, :466, :481, :499, :507).
//   Width        :517 (DrawCircle diameter) and :531 (DrawLine width) are m_SnapDistance × 0.3; ours is
//                RunOutlineSize × 0.3 for both (8 → 2.4 m, the look of field test 7), the size clamped 0..16
//                (the Options slider's range); 0 = off.
//   SizeChangeDue  not a game rule: when SkiRunOutlineSystem writes its "size a -> b" line (F7 fixer).
// Deliberately NOT mirrored (none applies to a ski run): the Lot first-side half color (:472-474), the MapTile
// dashes and projection (:367-380), the infoview pass (:117-128). The game draws surfaces with style flags 0
// (straight 3D lines, :363); ours are Projected (draped on the terrain) — a choice of the overlay system, not a rule.
//
// FLAG MIRRORS. The constants below are the literal values of Game.Tools.TempFlags (Game.Tools/TempFlags.cs) and
// Game.Areas.AreaFlags (Game.Areas/AreaFlags.cs), identical in 1.6.2f1. This file cannot reference Game.dll (the
// test host links it alone), so SkiRunOutlineSystem.OnCreate compares each constant with the game's enum and
// refuses to arm (one WARN) if a patch changed one.
//
// T30 / T36 (2026-10-16): the glade outline color + dash and the run node dot colors / rim live at the "run visuals"
// constants block below (one place); SkiGladeToolSystem and SkiRunOutlineSystem read them.
//
// PURE FILE: `using Unity.Mathematics;` only (the ResortBatch host links it with UnityMathematics.NoDeps). The walks
// are generic over two small interfaces so the Burst job passes its DynamicBuffer and native hash sets, and the
// tests pass managed ones; no allocation here.

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

    /// <summary>The overlay's pure rules (header).</summary>
    public static class RunOutlineRules
    {
        public const int MaxSize = 16;              // SkiResortSettings.RunOutlineSize slider max
        public const float WidthPerSize = 0.3f;     // AreaBorderRenderSystem.cs:517, :531

        // Game.Tools.TempFlags (uint) — checked against the enum by SkiRunOutlineSystem.OnCreate.
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

        // ── T22-T36 run visuals: the colors and sizes live HERE and nowhere else ───────────────────────────────────
        // T30 (field test 2026-10-15, item 46): the tree (glade) tool's outlines. A violet-magenta no band color, no game
        // outline color (hovered blue, owner green, warning yellow, error red) and no glade ring color (red / orange)
        // uses; opaque enough to read on snow, grass and forest floor. SkiGladeToolSystem.DrawOverlay reads these.
        // Candidate runs (every bound run, while nothing is hovered) are DASHED, the hovered / pending run is SOLID.
        public static readonly float4 GladeOutlineColour = new float4(0.86f, 0.30f, 1.00f, 0.95f);   // sRGB, as Overlay takes it
        public const float GladeCandidateAlphaFactor = 0.8f;   // dashed candidates: GladeOutlineColour.w × this
        public const float GladeLineWidthM = 2.2f;             // was the game-blue 1.5 m
        public const float GladeDashM = 9f, GladeGapM = 5f;    // cut on the CPU (PisteMapRules.DashRing)

        // T36 (item 41): the corner dots of a run being drawn or edited — white with a thin dark rim, so the node to
        // connect to is found on any ground. SkiRunOutlineSystem draws them (the game's own dots are 50 % white and stay
        // underneath).
        public static readonly float4 NodeDotFill = new float4(1f, 1f, 1f, 1f);
        public static readonly float4 NodeDotRim = new float4(0.04f, 0.04f, 0.06f, 0.95f);
        public const float NodeRimFraction = 0.10f;            // rim width = this × the dot diameter ...
        public const float NodeRimMinM = 0.12f;                // ... but at least this (meters)

        // T36 follow-up (item 41): while a run is drawn / edited (Temp Create or Modify), the nodes of EXISTING run areas of ours
        // within NeighbourMarginM (xz) of the drawn area's bounds get the same dots, so the corner to connect to is found.
        public const float NeighbourMarginM = 150f;

        /// <summary>Empty xz bounds (minX, minZ, maxX, maxZ) that HasBounds rejects until a point is added.</summary>
        public static float4 EmptyBounds() => new float4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);

        /// <summary>The bounds grown to hold p (xz).</summary>
        public static float4 Extend(float4 b, float3 p)
        {
            return new float4(math.min(b.x, p.x), math.min(b.y, p.z), math.max(b.z, p.x), math.max(b.w, p.z));
        }

        public static bool HasBounds(float4 b) => b.x <= b.z && b.y <= b.w;

        /// <summary>p (xz) lies within the bounds grown by marginM on every side (edges inclusive); false for empty bounds.</summary>
        public static bool InWindow(float4 b, float3 p, float marginM)
        {
            return HasBounds(b) && p.x >= b.x - marginM && p.x <= b.z + marginM && p.z >= b.y - marginM && p.z <= b.w + marginM;
        }

        /// <summary>Rim width (m) of a corner dot at the given Options size: max(NodeRimMinM, NodeRimFraction × diameter).</summary>
        public static float NodeRimWidth(int size)
        {
            float w = NodeRimFraction * DotDiameter(size);
            return w < NodeRimMinM ? NodeRimMinM : w;
        }

        /// <summary>The setting clamped to 0..MaxSize.</summary>
        public static int ClampSize(int size)
        {
            return size < 0 ? 0 : size > MaxSize ? MaxSize : size;
        }

        /// <summary>0 draws nothing: only the game's own thin outline remains.</summary>
        public static bool IsOn(int size) => ClampSize(size) > 0;

        /// <summary>Line width in meters: 0.3 × the clamped size.</summary>
        public static float LineWidth(int size) => WidthPerSize * ClampSize(size);

        /// <summary>Corner dot diameter in meters: 0.3 × the clamped size (the game: the same factor, :517).</summary>
        public static float DotDiameter(int size) => WidthPerSize * ClampSize(size);

        /// <summary>The size-change log rule (F7 fixer): the line "RunOutline: size a -> b" is due when the clamped
        /// setting differs from the last logged size AND this frame would draw (a Temp or flagged surface exists).
        /// One line per settled change, written at the first frame that would draw (a slider drag logs once), and a
        /// switch to 0 included: it used to be silent, so 8 → 0 → 16 → 8 logged "8 -> 16" and "16 -> 8".</summary>
        public static bool SizeChangeDue(int loggedSize, int size, bool wouldDraw)
        {
            return wouldDraw && ClampSize(size) != loggedSize;
        }

        /// <summary>Is the area outlined at all (header Outlined). <paramref name="tempFlags"/> is ignored when
        /// <paramref name="hasTemp"/> is false.</summary>
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

        /// <summary>Corner dots (header Dots).</summary>
        public static bool Dots(bool hasTemp, uint tempFlags)
        {
            return hasTemp && (tempFlags & TempDots) != 0;
        }

        /// <summary>Error &gt; Warning &gt; (Temp-only chunk) Parent → Owner &gt; Hovered (header ColourOf).</summary>
        public static OutlineColour ColourOf(bool chunkError, bool chunkWarning, uint tempFlags)
        {
            if (chunkError)
                return OutlineColour.Error;
            if (chunkWarning)
                return OutlineColour.Warning;
            return (tempFlags & TempParent) != 0 ? OutlineColour.Owner : OutlineColour.Hovered;
        }

        /// <summary>The side last node → first node is drawn only when the area is Complete.</summary>
        public static bool ClosingSide(byte areaFlags) => (areaFlags & AreaComplete) != 0;

        /// <summary>The key AddBorders files for the side prev → cur (:227-239, :248-260).</summary>
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

        /// <summary>The key DrawBorders looks up for the side prev → cur (:457-465, :490-498): the reverse of
        /// AddKey. Found = another outlined area shares the side, and the game does not draw it.</summary>
        public static void DrawKey(float3 prev, float3 cur, byte areaFlags, out float3 start, out float3 end)
        {
            AddKey(prev, cur, areaFlags, out end, out start);
        }

        /// <summary>Files every side of one outlined area (any prefab; the closing side when Complete).
        /// An area with no node files nothing (the game would index node 0).</summary>
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

        /// <summary>Draws one of OUR outlined areas in the game's order (header AddEdges / DrawArea).</summary>
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
