// Resort Planner (v2) world labels: one label per proposed / pinned lift ("P2: detachable, 1.3 km, 540 m up, 7 runs") at the chord
// midpoint and one per pod ("Pod B: Intermediate") at its top, built once per published plan (at most 64 distinct strings) with the
// same TextMeshPro mesh pattern as SkiRunLabelSystem. Locale: SkiResort.Label.PLAN_LIFT / PLAN_POD.
// A0 spine STUB: registered so Mod.cs never changes again, draws nothing. A4 (lifts + overlay) fills it.
// Phase: Rendering, NOT anchored to OverlayRenderSystem (the labels are Graphics.DrawMesh calls; nothing is written to the overlay buffers).
// Engine notes: EngineNotes/ResortPlanner.md (index) and EngineNotes/Mod.md ("Near ResortPlanLabelSystem")

using Colossal.Serialization.Entities;
using Game;

namespace SkiResortMod.Systems
{
    public sealed partial class ResortPlanLabelSystem : GameSystemBase
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            Mod.Log.Info($"{nameof(ResortPlanLabelSystem)} created (stub: draws nothing until stage A4)");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
        }

        protected override void OnUpdate()
        {
        }
    }
}
