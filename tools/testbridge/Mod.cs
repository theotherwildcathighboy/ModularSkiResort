// Dev-only companion mod (README.md). Inert unless the game was launched with --uiDeveloperMode, the flag that opens the
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
            Log.Info("Active: commands on engine.call(\"skitest.cmd\", json).");
        }

        public void OnDispose()
        {
            NativeInput.ReleaseAll();
        }
    }
}
