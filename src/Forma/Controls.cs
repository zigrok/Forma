// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// This control API and behavior are adapted from Godot Engine's Panel implementation;
// see THIRD-PARTY-NOTICES.md.

using Microsoft.Xna.Framework;

namespace Forma
{
    /// <summary>Draws a themed panel surface with configurable fallback background and border colors.</summary>
    public class Panel : Control
    {
        public Color? BackgroundColor { get; set; }
        public Color? BorderColor { get; set; }
        public int BorderWidth { get; set; } = 1;
        internal override void Draw(UIRenderContext context)
        {
            var rect = Bounds;
            var style = GetThemeStyleBox("panel");
            if (style != null) style.Draw(context, rect);
            else { context.Fill(rect, BackgroundColor ?? context.Theme.PanelColor); context.Border(rect, BorderColor ?? context.Theme.PanelBorderColor, BorderWidth); }
            base.Draw(context);
        }
    }
}
