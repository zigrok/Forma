using System;
using System.Collections.Generic;

namespace Forma
{
    /// <summary>
    /// Chooses the whole-pixel size at which a font is rasterized for a display scale. Hinters only snap stems
    /// cleanly at some sizes, and which ones depends on the font, so each candidate size next to the ideal one is
    /// rasterized for a few probe glyphs and the one with the fewest half-covered edge pixels wins.
    /// </summary>
    internal static class GlyphSizeSnapper
    {
        private const float DeviationPenalty = 0.1f;
        private static readonly Dictionary<(UIFontIdentity, float, float), float> Cache = new Dictionary<(UIFontIdentity, float, float), float>();
        private static readonly object Sync = new object();

        internal static float ChooseRasterScale(UIFont font, float displayScale)
        {
            var key = (font.Identity, font.Size, displayScale);
            lock (Sync)
            {
                if (Cache.TryGetValue(key, out var cached)) return cached;
            }
            var ideal = font.Size * displayScale;
            var rounded = MathF.Max(1, MathF.Round(ideal));
            var best = rounded;
            if (rounded >= 8 && font.ProbeGlyphId('l') != 0)
            {
                var bestScore = float.MaxValue;
                for (var candidate = rounded - 1; candidate <= rounded + 1; candidate++)
                {
                    var score = Score(font, candidate / font.Size) + DeviationPenalty * MathF.Abs(candidate - ideal);
                    if (score < bestScore - 0.0001f || (MathF.Abs(score - bestScore) <= 0.0001f && MathF.Abs(candidate - ideal) < MathF.Abs(best - ideal)))
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }
            }
            var result = best / font.Size;
            lock (Sync) Cache[key] = result;
            return result;
        }

        private static float Score(UIFont font, float rasterScale)
        {
            var total = 0f;
            var samples = 0;
            foreach (var probe in Probes)
            {
                var glyph = font.ProbeGlyphId(probe.Scalar);
                if (glyph == 0) continue;
                var bitmap = font.RasterizeGlyph(glyph, rasterScale);
                if (bitmap.Width == 0 || bitmap.Height == 0) continue;
                var pixels = bitmap.Pixels.Span;
                var length = probe.Horizontal ? bitmap.Width : bitmap.Height;
                for (var line = 0; line < probe.Lines; line++)
                {
                    var fixedIndex = probe.Horizontal
                        ? (int)(bitmap.Height * probe.Position) + line
                        : (int)(bitmap.Width * probe.Position) + line;
                    var blur = 0f;
                    for (var step = 0; step < length; step++)
                    {
                        var x = probe.Horizontal ? step : fixedIndex;
                        var y = probe.Horizontal ? fixedIndex : step;
                        if (x >= bitmap.Width || y >= bitmap.Height) continue;
                        var value = pixels[y * bitmap.Width + x];
                        blur += MathF.Min(value, 255 - value) / 255f;
                    }
                    total += blur;
                    samples++;
                }
            }
            return samples == 0 ? 0 : total / samples;
        }

        private readonly struct Probe
        {
            internal Probe(int scalar, bool horizontal, float position, int lines) { Scalar = scalar; Horizontal = horizontal; Position = position; Lines = lines; }
            internal int Scalar { get; }
            internal bool Horizontal { get; }
            internal float Position { get; }
            internal int Lines { get; }
        }

        // Horizontal probes cross vertical stems along a row; vertical probes cross horizontal bars down a column.
        private static readonly Probe[] Probes =
        {
            new Probe('l', true, 0.5f, 3),
            new Probe('I', true, 0.5f, 3),
            new Probe('i', true, 0.75f, 2),
            new Probe('T', false, 0.15f, 1),
            new Probe('E', false, 0.2f, 1),
        };
    }
}
