// SkiRunDrawFeedbackSystem.Difficulty — the T44 difficulty layer's view for the drawing tooltip
// (docs/research/field/2026-10-15/build/T44-difficulty.md). A NEW partial file so the shipped
// SkiRunDrawFeedbackSystem.cs stays untouched: the Readout struct and DisplayBand(RatingGrade) there
// know only the raw crux band, while the rated band (SlopeResult.Band: steep-share cap, green-mean
// rule, wide ease) and the width evidence live in m_Result. This reads m_Result (same class, private
// access) under exactly TryGetReadout's gates, so it is valid only when the readout is.
//
// Hysteresis: while the layer did not move the band (Band == CruxBand, always so in polyline mode) the
// shown band is the readout's own hysteresis band, unchanged from before. When the layer moved it the
// rated band is shown directly (no hysteresis: the cap boundary depends on the share, not on a single
// grade the player drags across a threshold).

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunDrawFeedbackSystem
    {
        /// <summary>T44: what the tooltip needs beyond Readout. Band = the band to SHOW; Note / WidthM /
        /// SteepShare / CruxBand come from the classifier (SlopeResult).</summary>
        public struct Difficulty
        {
            public bool Valid;
            public SlopeBand Band;       // the rated band to show
            public SlopeBand CruxBand;   // BandOf(RatingGrade)
            public SlopeNote Note;
            public float WidthM;         // mean extent over the crux stretch, m (-1 = unknown)
            public float SteepShare;     // fraction of cells at/above the crux band's floor (Capped only)
        }

        /// <summary>True only when TryGetReadout is true (same gates, same one-frame lag).</summary>
        public bool TryGetDifficulty(out Difficulty d)
        {
            d = default;
            if (!TryGetReadout(out Readout r) || !r.Valid)
                return false;
            SlopeResult res = m_Result;
            d.Valid = true;
            d.CruxBand = res.CruxBand;
            d.Note = res.Note;
            d.WidthM = res.CruxWidthM;
            d.SteepShare = res.SteepShare;
            d.Band = res.Band != res.CruxBand ? res.Band : r.Band;
            return true;
        }
    }
}
