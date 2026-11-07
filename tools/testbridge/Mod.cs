// Dev-only companion mod (README.md). Inert unless the game starts with --uiDeveloperMode. This flag opens the
// UI debug port the commands arrive through.

using System;
using System.Linq;
using Colossal.Logging;
using Game;
using Game.Modding;

namespace SkiTestBridge
{
    public sealed class Mod : IMod
    {
        internal static readonly ILog Log = LogManager.GetLogger(nameof(SkiTestBridge)).SetShowsErrorsInUI(false);

        public void OnLoad(UpdateSystem updateSystem)
        {
            bool devMode = Environment.GetCommandLineArgs()
                .Any(a => a.IndexOf("uiDeveloperMode", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!devMode)
            {
                Log.Info("Inert: the game was launched without --uiDeveloperMode.");
                return;
            }
            NativeInput.CheckLayout();
            updateSystem.UpdateAt<TestBridgeSystem>(SystemUpdatePhase.UIUpdate);
            // Before InitializeSystem (Modification2): an Updated tag added in UIUpdate is cleaned up that same frame, unseen.
            updateSystem.UpdateAt<TestBridgeOffGridSystem>(SystemUpdatePhase.Modification1);
            // U1 probe (TestBridgeSystem.U1.cs): idle until the u1probe op arms it.
            updateSystem.UpdateAfter<U1ProbeM3System, Game.Objects.SubObjectReferencesSystem>(SystemUpdatePhase.Modification3);
            updateSystem.UpdateAfter<U1ProbeM4System, Game.Objects.AlignSystem>(SystemUpdatePhase.Modification4);
            updateSystem.UpdateBefore<U1ProbeM4System, Game.Net.LaneSystem>(SystemUpdatePhase.Modification4);
            updateSystem.UpdateAt<U1ProbeM4BSystem>(SystemUpdatePhase.Modification4B);
            // Right after vanilla AttractionSystem on the same tick, as SkiResortAttractivenessSystem is (mprobe trace).
            updateSystem.UpdateAfter<TestBridgeMergeProbeSystem, Game.Simulation.AttractionSystem>(SystemUpdatePhase.GameSimulation);
            Log.Info("Active: commands on engine.call(\"skitest.cmd\", json).");
        }

        public void OnDispose()
        {
            NativeInput.ReleaseAll();
        }
    }
}
