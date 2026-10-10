// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;

namespace Forma
{
    /// <summary>
    /// Global adjustments applied when dynamic glyphs are rasterized, for tuning how a font looks at a given display
    /// scale. Layout is unaffected: only the glyph bitmaps change. Both values are zero unless a host sets them.
    /// </summary>
    public static class GlyphRasterTuning
    {
        /// <summary>Whole pixels added to the physical pixel size glyphs are rasterized at.</summary>
        public static int PixelSizeOffset { get; set; }

        /// <summary>Added to the font's wght axis value when it has one (quantized to 25 by the font).</summary>
        public static float WeightOffset { get; set; }

        internal static UIFont Apply(UIFont font)
        {
            if (WeightOffset == 0f) return font;
            var weight = font.VariationWeight;
            return weight.HasValue ? font.ApplyVariationWeight(weight.Value + WeightOffset) : font;
        }
    }
}
