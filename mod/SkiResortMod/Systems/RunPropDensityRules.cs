// What a run may carry and how a settled change is timed, for the run prop placer (RunPropPlacerRules has the walk). SkiRunPropSystem calls
// these on the main thread. tests/ResortBatch DensityTests checks them with literal values. Snow and upkeep read a run's prop spacings only
// as its Snowmaking and Lights features (SkiRunPropSystem.FeaturesOf, DECISIONS 577).
// Pure file: `using System;` only (no Game.dll, no Unity types). It is linked into tests/ResortBatch. C# 9 / net48: no Math.Clamp.
// Engine notes: EngineNotes/RunPropDensityRules.md

using System;

namespace SkiResortMod.Systems
{
    public static class RunPropDensityRules
    {
        /// <summary>OnUpdate frames a run's slider must stay still before re-placement, so a drag rebuilds once.</summary>
        public const int SettleFrames = 30;

        /// <summary>Census classes of a run prop (the tag's m_Class, log lines).</summary>
        public const byte ClassOther = 0, ClassGun = 1, ClassHydrant = 2, ClassMast = 3, ClassEmpty = 4;

        /// <summary>The empty marker's prefab name (mesh-less SkiPropNone) and the hydrant prefix (SkiHydrantBox*).</summary>
        private const string EmptyMarkerName = "SkiPropNone";
        private const string HydrantPrefix = "SkiHydrant";

        public static int NextStable(int stable, bool changed)
        {
            if (changed)
                return 0;
            int s = stable < 0 ? 0 : stable;
            return s >= SettleFrames ? SettleFrames : s + 1;
        }

        /// <summary>The census class of a run prop by prefab name. Null or empty gives Other. A night-only member gives Mast. SkiPropNone
        /// gives Empty. SkiHydrant* gives Hydrant. Anything else gives Gun. Old saves' corner props only (the conversion's log
        /// line).</summary>
        public static byte ClassOf(string prefabName, bool nightOnly)
        {
            if (string.IsNullOrEmpty(prefabName))
                return ClassOther;
            if (nightOnly)
                return ClassMast;
            if (string.Equals(prefabName, EmptyMarkerName, StringComparison.Ordinal))
                return ClassEmpty;
            if (prefabName.StartsWith(HydrantPrefix, StringComparison.Ordinal))
                return ClassHydrant;
            return ClassGun;
        }

        /// <summary>Which classes the placer stands on a run kind. Snowmaking: guns and hydrants. Night: those and masts. Cat track: guns
        /// and hydrants, masts only when lit (<paramref name="lit"/> = its lights slider is above Off, and no other lit state exists). Groomed and
        /// natural runs carry nothing. The empty marker is never placed: the spacing is the density.</summary>
        public static bool PropClassAllowed(int runKind, byte propClass, bool lit) =>
            PropClassAllowed(RunFeatures.FromKind(runKind, true, lit), propClass);

        /// <summary>The feature form: guns and hydrants with snowmaking, masts with lights.</summary>
        public static bool PropClassAllowed(RunFeatures f, byte propClass)
        {
            if (propClass == ClassGun || propClass == ClassHydrant)
                return f.Snowmaking;
            return propClass == ClassMast && f.Lights;
        }
    }

    /// <summary>The yaw a floodlight mast must have so its lamps aim INTO the run and slightly downhill. Pure, plain floats (System only),
    /// x/z in the game's xz plane, y = height. The placer turns each mast to look along the vector returned here at placement. Nothing
    /// rewrites a free-standing mast's rotation afterward. Interior side from the ring's winding (CCW in x,z = interior on the LEFT of
    /// travel, the glade planner's convention). Deterministic. Engine notes: EngineNotes/RunPropDensityRules.md</summary>
    public static class MastAimRules
    {
        /// <summary>Largest sideways lean toward downhill (degrees) and the fall rate (m/m) that earns all of it.</summary>
        public const float BiasMaxDeg = 25f;
        public const float SlopeFull = 0.15f;

        /// <summary>The horizontal direction (unit, ax, az) a mast must face, from the border's unit tangent at its slot, the ring's
        /// winding and the fall rate along travel (positive: going along the border loses height). False for a zero tangent.</summary>
        public static bool AimFromTangent(float tx, float tz, bool ccw, float fall, out float ax, out float az)
        {
            ax = 0f;
            az = 0f;
            float tLen = (float)Math.Sqrt(tx * tx + tz * tz);
            if (!(tLen > 1e-6f))
                return false;
            float ux = tx / tLen, uz = tz / tLen;
            float nX = ccw ? -uz : uz, nZ = ccw ? ux : -ux;
            float k = fall / SlopeFull;
            k = float.IsNaN(k) ? 0f : k < -1f ? -1f : k > 1f ? 1f : k;
            float bias = k * (float)Math.Tan(BiasMaxDeg * Math.PI / 180.0);
            float rX = nX + bias * ux, rZ = nZ + bias * uz;
            float rLen = (float)Math.Sqrt(rX * rX + rZ * rZ);
            ax = rX / rLen;
            az = rZ / rLen;
            return true;
        }

        /// <summary>The angle in degrees between two horizontal directions (0..180).</summary>
        public static float AngleDeg(float ax, float az, float bx, float bz)
        {
            double dot = (double)ax * bx + (double)az * bz;
            double cross = (double)ax * bz - (double)az * bx;
            return (float)(Math.Abs(Math.Atan2(cross, dot)) * 180.0 / Math.PI);
        }
    }
}
