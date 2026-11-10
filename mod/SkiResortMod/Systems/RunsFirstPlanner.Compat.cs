// RunsFirstPlanner compat: the few .NET Core APIs the planner used, as plain C# that net48 (the mod's Mono) has too. Each helper gives
// exactly the result of the API it replaces (Math.Clamp, Array.Fill, BitOperations.PopCount / TrailingZeroCount). The planner's
// hot methods carry [MethodImpl(Hot)]: AggressiveOptimization on .NET Core (the host: no slow tier-0 first region), nothing on
// net48, where the flag does not exist.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SkiResortMod.Systems
{
    public sealed partial class RunsFirstPlanner
    {
#if NETCOREAPP
        private const MethodImplOptions Hot = MethodImplOptions.AggressiveOptimization;
#else
        private const MethodImplOptions Hot = 0;
#endif

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        private static void FillAll<T>(T[] a, T v)
        {
            for (int i = 0; i < a.Length; i++)
                a[i] = v;
        }

        /// <summary>FirstOrDefault(predicate, default) and FirstOrDefault(default) (.NET 6): the first match, else the given value.</summary>
        private static T FirstOr<T>(IEnumerable<T> seq, Func<T, bool> pred, T dflt)
        {
            foreach (T x in seq)
                if (pred(x))
                    return x;
            return dflt;
        }

        private static T FirstOr<T>(IEnumerable<T> seq, T dflt)
        {
            foreach (T x in seq)
                return x;
            return dflt;
        }

        private static int PopCount(ulong x)
        {
            x -= (x >> 1) & 0x5555555555555555UL;
            x = (x & 0x3333333333333333UL) + ((x >> 2) & 0x3333333333333333UL);
            x = (x + (x >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((x * 0x0101010101010101UL) >> 56);
        }

        private static int TrailingZeroCount(ulong x)
        {
            if (x == 0)
                return 64;
            int n = 0;
            while ((x & 1UL) == 0)
            {
                x >>= 1;
                n++;
            }
            return n;
        }
    }
}
