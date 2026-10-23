// Difficulty view for the drawing tooltip, in its own partial so the shipped file stays untouched.
// Reads m_Result under TryGetReadout's gates, so it is valid only when the readout is.
// Hysteresis: when the rated band equals the crux band, the readout's own hysteresis band is shown;
// otherwise the rated band is shown directly (the steep-share cap boundary is not a single grade threshold).

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunDrawFeedbackSystem
    {
        /// <summary>Band is the band to show; the rest comes from the classifier (SlopeResult).</summary>
        public struct Difficulty
        {
            public bool Valid;
            public SlopeBand Band;
            public SlopeBand CruxBand;
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
