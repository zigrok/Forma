// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Xna.Framework;

namespace Forma
{
    /// <summary>Read-only downsampled visualization of one dynamic glyph-atlas page.</summary>
    public sealed class DynamicGlyphAtlasView : Control
    {
        public DynamicGlyphAtlasPageSnapshot Snapshot { get; set; }
        public int SampleColumns { get; set; } = 32;
        public DynamicGlyphAtlasView() => CustomMinimumSize = new Vector2(128, 128);
        internal override void Draw(UIRenderContext context)
        {
            context.Fill(Bounds, new Color(16, 20, 27));
            var snapshot = Snapshot;
            if (snapshot != null && SampleColumns > 0 && Size.X > 0 && Size.Y > 0)
            {
                var columns = Math.Min(SampleColumns, snapshot.Width);
                var rows = Math.Max(1, (int)MathF.Round(columns * snapshot.Height / (float)snapshot.Width));
                var pixels = snapshot.Pixels.Span;
                for (var row = 0; row < rows; row++)
                    for (var column = 0; column < columns; column++)
                    {
                        var sourceX = Math.Min(snapshot.Width - 1, column * snapshot.Width / columns);
                        var sourceY = Math.Min(snapshot.Height - 1, row * snapshot.Height / rows);
                        var coverage = pixels[sourceY * snapshot.Width + sourceX];
                        if (coverage == 0) continue;
                        var left = Bounds.Left + column * Bounds.Width / columns;
                        var top = Bounds.Top + row * Bounds.Height / rows;
                        var right = Bounds.Left + (column + 1) * Bounds.Width / columns;
                        var bottom = Bounds.Top + (row + 1) * Bounds.Height / rows;
                        context.Fill(new Rectangle(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top)), new Color(coverage, coverage, coverage));
                    }
            }
            context.Border(Bounds, new Color(56, 66, 82));
            base.Draw(context);
        }
    }

}
