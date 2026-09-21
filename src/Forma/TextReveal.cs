// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;

namespace Forma
{
    internal static class TextReveal
    {
        internal static int VisibleCount(int graphemeCount, int characters, float ratio)
        {
            if (!float.IsFinite(ratio)) throw new ArgumentOutOfRangeException(nameof(ratio));
            return characters >= 0 ? Math.Min(characters, graphemeCount)
                : (int)MathF.Floor(graphemeCount * Math.Clamp(ratio, 0, 1));
        }

        internal static int VisibleEnd(string text, int characters, float ratio)
        {
            var boundaries = UnicodeGraphemeSegmenter.GetUtf16Boundaries(text);
            return boundaries[VisibleCount(boundaries.Length - 1, characters, ratio)];
        }
    }
}
