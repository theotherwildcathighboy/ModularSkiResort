// What BullwheelSpinSystem needs from the loop cache (T256): per loop and station, the wheel the rope rounds (its radius and turn
// sense, from the loop's own arc), and the rope's speed now (0 while the loop is held). Read on the main thread, right after this
// system in Modification4B. Nothing here writes: the wheels follow the carriers, never the other way round.
// Engine notes: EngineNotes/BullwheelSpinSystem.md

using System.Collections.Generic;
using Colossal.Entities;
using Game.Objects;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace SkiResortMod.Systems
{
    public sealed partial class ChairPinSystem
    {
        /// <summary>One station wheel of one loop.</summary>
        internal struct WheelDrive
        {
            public Entity Station;
            public int Loop;        // index into the loop cache of this build (WheelBuilds)
            public float RadiusM;   // the rope's radius on the wheel (the loop's turn arc)
            public int Sign;        // BullwheelMath.TurnSign: +1 = clockwise seen from above
        }

        /// <summary>Counts the loop cache builds of this load. A consumer that kept loop indices re-reads them when it changes.</summary>
        internal int WheelBuilds { get; private set; }

        /// <summary>The mover's clock, seconds (TimeS): stands while the game is paused.</summary>
        internal double WheelClockS => TimeS();

        /// <summary>The wheels of the current loop cache (main thread, at a rebuild only: it reads station Transforms).
        /// <paramref name="unmatched"/> counts loop stations that have a catalog wheel but no turn arc on it: the loop builder refused the
        /// wheel (LiftLoopCache.AppendTerminalTurn's plain arc), so the carriers do not round it and it stands.</summary>
        internal void CollectWheelDrives(List<WheelDrive> into, out int unmatched)
        {
            into.Clear();
            unmatched = 0;
            for (int l = 0; l < m_LoopCache.m_Loops.Count; l++)
            {
                LiftLoopCache.Loop loop = m_LoopCache.m_Loops[l];
                for (int end = 0; end < 2; end++)
                {
                    Entity station = end == 0 ? loop.m_BaseStation : loop.m_TopStation;
                    if (!WheelWorld(station, out float3 wheel))
                        continue;
                    bool matched = false;
                    for (int s = 0; s < loop.m_Segments.Count && !matched; s++)
                    {
                        LiftLoopCache.Segment seg = loop.m_Segments[s];
                        if (!seg.m_IsArc ||
                            !BullwheelMath.ArcDrive(seg.m_ArcCenter, seg.m_ArcU, seg.m_ArcB, seg.m_ArcRadius, wheel, out float radius, out int sign))
                            continue;
                        matched = true;
                        into.Add(new WheelDrive { Station = station, Loop = l, RadiusM = radius, Sign = sign });
                    }
                    if (!matched)
                        unmatched++;
                }
            }
        }

        /// <summary>The rope speed of loop <paramref name="loop"/> now, m/s: 0 while it is held (no power, closed) or out of range.</summary>
        internal double WheelSpeedMps(int loop)
        {
            if (m_Disabled || !MovingChairs || loop < 0 || loop >= m_LoopCache.m_Loops.Count)
                return 0.0;   // the mover is off (tripped, or the dev tab's static mode): the chairs stand, so do the wheels
            if (m_HeldLoopCount > 0 && loop < m_HoldByLoop.Count && m_HoldByLoop[loop].Held)
                return 0.0;
            return m_LoopCache.m_Loops[loop].m_SpeedMps;
        }

        /// <summary>The station's bullwheel center at rope height in the world (its catalog row's WheelLocal). False for a station with no
        /// wheel row, an unknown prefab or a dead entity.</summary>
        private bool WheelWorld(Entity station, out float3 wheel)
        {
            wheel = default;
            if (station == Entity.Null || !EntityManager.Exists(station) ||
                !EntityManager.TryGetComponent(station, out PrefabRef prefab) ||
                !EntityManager.TryGetComponent(station, out Transform transform) ||
                !m_PrefabCatalog.TryGetStation(prefab.m_Prefab, out StationDef def) || def == null ||
                !math.all(math.isfinite(def.WheelLocal)))
                return false;
            wheel = ObjectUtils.LocalToWorld(transform, def.WheelLocal);
            return true;
        }
    }
}
