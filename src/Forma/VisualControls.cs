// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// Control APIs and layout/render behavior are adapted from Godot Engine's TextureRect,
// NinePatchRect, AspectRatioContainer, SplitContainer, FlowContainer, and PanelContainer
// implementations under scene/gui; see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    /// <summary>Fills its bounds with a solid color.</summary>
    public sealed class ColorRect : Control
    {
        public Color Color { get; set; } = Color.White;
        internal override void Draw(UIRenderContext context) { context.Fill(Bounds, Color); base.Draw(context); }
    }

    public enum TextureStretchMode { Keep, KeepCentered, KeepAspect, KeepAspectCentered, Scale, Tile, KeepAspectCovered }
    /// <summary>Controls how a TextureRect contributes a minimum size, matching Godot's expand_mode.</summary>
    public enum TextureRectExpandMode { KeepSize, IgnoreSize, FitWidth, FitWidthProportional, FitHeight, FitHeightProportional }

    /// <summary>Local drawing regions calculated for a TextureRect.</summary>
    public readonly struct TextureRectLayout
    {
        public TextureRectLayout(Rectangle destination, Rectangle source, bool tile) { Destination = destination; Source = source; Tile = tile; }
        public Rectangle Destination { get; }
        public Rectangle Source { get; }
        public bool Tile { get; }
    }

    /// <summary>Draws a texture with configurable scaling, aspect cropping, tiling, flipping, and size contribution.</summary>
    public class TextureRect : Control
    {
        // Godot's TextureRect() constructor calls set_mouse_filter(MOUSE_FILTER_PASS) - a bare texture
        // display shouldn't claim hit-testing by default like this port's base Control (Stop) does.
        public TextureRect() { MouseFilter = MouseFilter.Pass; }
        private Texture2D _texture;
        // Godot's set_texture (the single setter bound to both the `texture` property and the
        // set_texture method) dedups against the current value, then queues a redraw/minimum-size
        // update and (for NinePatchRect specifically) fires texture_changed - matching that exactly.
        public Texture2D Texture
        {
            get => _texture;
            set
            {
                if (_texture == value) return;
                _texture = value;
                QueueLayout();
                OnTextureChanged();
            }
        }
        /// <summary>Hook for a subclass-specific texture_changed signal (only NinePatchRect has one in Godot).</summary>
        protected virtual void OnTextureChanged() { }
        public TextureStretchMode StretchMode { get; set; } = TextureStretchMode.Scale;
        public TextureRectExpandMode ExpandMode { get; set; } = TextureRectExpandMode.KeepSize;
        public bool FlipH { get; set; }
        public bool FlipV { get; set; }
        public Color Modulate { get; set; } = Color.White;
        public void SetTexture(Texture2D texture) => Texture = texture;
        public Texture2D GetTexture() => Texture;
        public void SetStretchMode(TextureStretchMode mode) { if (!Enum.IsDefined(typeof(TextureStretchMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode)); StretchMode = mode; QueueLayout(); }
        public TextureStretchMode GetStretchMode() => StretchMode;
        public void SetExpandMode(TextureRectExpandMode mode) { if (!Enum.IsDefined(typeof(TextureRectExpandMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode)); ExpandMode = mode; QueueLayout(); }
        public TextureRectExpandMode GetExpandMode() => ExpandMode;
        public void SetFlipH(bool enable) => FlipH = enable;
        public bool IsFlippedH() => FlipH;
        public void SetFlipV(bool enable) => FlipV = enable;
        public bool IsFlippedV() => FlipV;
        public void SetModulate(Color color) => Modulate = color;
        public Color GetModulate() => Modulate;
        public override Vector2 GetMinimumSize()
        {
            if (Texture == null) return CustomMinimumSize;
            return Vector2.Max(CustomMinimumSize, GetTextureMinimumSize(new Vector2(Texture.Width, Texture.Height)));
        }
        /// <summary>Calculates the texture-derived minimum size for the current expand mode.</summary>
        public Vector2 GetTextureMinimumSize(Vector2 textureSize)
        {
            var textureWidth = Math.Max(0, textureSize.X);
            var textureHeight = Math.Max(0, textureSize.Y);
            Vector2 result;
            switch (ExpandMode)
            {
                case TextureRectExpandMode.IgnoreSize: result = Vector2.Zero; break;
                case TextureRectExpandMode.FitWidth: result = new Vector2(Size.Y, 0); break;
                case TextureRectExpandMode.FitWidthProportional: result = new Vector2(Size.Y * textureWidth / Math.Max(1f, textureHeight), 0); break;
                case TextureRectExpandMode.FitHeight: result = new Vector2(0, Size.X); break;
                case TextureRectExpandMode.FitHeightProportional: result = new Vector2(0, Size.X * textureHeight / Math.Max(1f, textureWidth)); break;
                default: result = new Vector2(textureWidth, textureHeight); break;
            }
            return result;
        }
        /// <summary>Calculates local draw placement, including source cropping for aspect-covered images.</summary>
        public TextureRectLayout GetTextureLayout(Vector2 textureSize)
        {
            var textureWidth = Math.Max(0, (int)MathF.Round(textureSize.X));
            var textureHeight = Math.Max(0, (int)MathF.Round(textureSize.Y));
            var width = Math.Max(0, (int)MathF.Round(Size.X));
            var height = Math.Max(0, (int)MathF.Round(Size.Y));
            if (textureWidth == 0 || textureHeight == 0) return new TextureRectLayout(Rectangle.Empty, Rectangle.Empty, false);
            var source = new Rectangle(0, 0, textureWidth, textureHeight);
            var destination = new Rectangle(0, 0, width, height);
            switch (StretchMode)
            {
                case TextureStretchMode.Keep:
                    destination = new Rectangle(0, 0, textureWidth, textureHeight); break;
                case TextureStretchMode.KeepCentered:
                    destination = new Rectangle((width - textureWidth) / 2, (height - textureHeight) / 2, textureWidth, textureHeight); break;
                case TextureStretchMode.KeepAspect:
                case TextureStretchMode.KeepAspectCentered:
                    if (width > 0 && height > 0)
                    {
                        // Godot's exact two-pass algorithm (TextureRect::_notification): first assume the
                        // texture fills the full height and derive width proportionally, truncating like
                        // C++'s float-to-int assignment (not rounding); if that overflows the available
                        // width, clamp width and recompute height from it using pure integer division.
                        var destinationHeight = height;
                        var destinationWidth = (int)(textureWidth * (float)destinationHeight / textureHeight);
                        if (destinationWidth > width)
                        {
                            destinationWidth = width;
                            destinationHeight = textureHeight * destinationWidth / textureWidth;
                        }
                        var x = StretchMode == TextureStretchMode.KeepAspectCentered ? (width - destinationWidth) / 2 : 0;
                        var y = StretchMode == TextureStretchMode.KeepAspectCentered ? (height - destinationHeight) / 2 : 0;
                        destination = new Rectangle(x, y, destinationWidth, destinationHeight);
                    }
                    break;
                case TextureStretchMode.KeepAspectCovered:
                    if (width > 0 && height > 0)
                    {
                        var scale = Math.Max(width / (float)textureWidth, height / (float)textureHeight);
                        var sourceWidth = Math.Min(textureWidth, Math.Max(1, (int)MathF.Round(width / scale)));
                        var sourceHeight = Math.Min(textureHeight, Math.Max(1, (int)MathF.Round(height / scale)));
                        source = new Rectangle((textureWidth - sourceWidth) / 2, (textureHeight - sourceHeight) / 2, sourceWidth, sourceHeight);
                    }
                    break;
                case TextureStretchMode.Tile:
                    return new TextureRectLayout(destination, source, true);
            }
            return new TextureRectLayout(destination, source, false);
        }
        internal override void Draw(UIRenderContext context)
        {
            if (Texture != null)
            {
                var layout = GetTextureLayout(new Vector2(Texture.Width, Texture.Height));
                var effects = (FlipH ? SpriteEffects.FlipHorizontally : SpriteEffects.None) | (FlipV ? SpriteEffects.FlipVertically : SpriteEffects.None);
                if (layout.Tile)
                {
                    for (var y = 0; y < layout.Destination.Height; y += Texture.Height)
                    for (var x = 0; x < layout.Destination.Width; x += Texture.Width)
                    {
                        var tileWidth = Math.Min(Texture.Width, layout.Destination.Width - x);
                        var tileHeight = Math.Min(Texture.Height, layout.Destination.Height - y);
                        context.SpriteBatch.Draw(Texture, new Rectangle(Bounds.X + x, Bounds.Y + y, tileWidth, tileHeight), new Rectangle(0, 0, tileWidth, tileHeight), Modulate, 0, Vector2.Zero, effects, 0);
                    }
                }
                else if (layout.Destination.Width > 0 && layout.Destination.Height > 0)
                    context.SpriteBatch.Draw(Texture, new Rectangle(Bounds.X + layout.Destination.X, Bounds.Y + layout.Destination.Y, layout.Destination.Width, layout.Destination.Height), layout.Source, Modulate, 0, Vector2.Zero, effects, 0);
            }
            DrawChildControls(context);
        }
        protected void DrawChildControls(UIRenderContext context) => base.Draw(context);
    }

    /// <summary>Displays a non-owning theme icon at its stable logical size.</summary>
    public class ThemeIconRect : Control
    {
        private ThemeIcon? _icon;
        public ThemeIcon? Icon
        {
            get => _icon;
            set { _icon = value; QueueLayout(); }
        }
        public string ThemeItemName { get; set; } = string.Empty;
        public string ThemeTypeName { get; set; } = string.Empty;
        public Color Modulate { get; set; } = Color.White;
        public override Vector2 GetMinimumSize() => Vector2.Max(CustomMinimumSize, ResolveIcon() is ThemeIcon icon ? icon.LogicalSize.ToVector2() : Vector2.Zero);
        internal override void Draw(UIRenderContext context)
        {
            if (ResolveIcon() is ThemeIcon icon)
            {
                var x = Bounds.X + (Bounds.Width - icon.LogicalSize.X) / 2;
                var y = Bounds.Y + (Bounds.Height - icon.LogicalSize.Y) / 2;
                context.Icon(icon, new Vector2(x, y), Modulate);
            }
            base.Draw(context);
        }
        protected ThemeIcon? ResolveIcon()
        {
            if (Icon is ThemeIcon icon) return icon;
            return string.IsNullOrWhiteSpace(ThemeItemName) ? null : GetThemeIcon(ThemeItemName, ThemeTypeName);
        }
    }

    /// <summary>Controls whether a NinePatchRect axis scales, repeats, or repeats with fitted tiles.</summary>
    public enum NinePatchAxisStretchMode { Stretch, Tile, TileFit }

    /// <summary>Draws a texture as nine patches so configured borders remain stable while center and edge regions stretch or tile.</summary>
    public class NinePatchRect : TextureRect
    {
        // Godot's NinePatchRect() constructor calls set_mouse_filter(MOUSE_FILTER_IGNORE) - fully
        // click-through by default, unlike the Pass this inherits from TextureRect's own constructor.
        public NinePatchRect() { MouseFilter = MouseFilter.Ignore; }
        /// <summary>Matches Godot's NinePatchRect-only texture_changed signal.</summary>
        public event Action<NinePatchRect, Texture2D> TextureChanged;
        protected override void OnTextureChanged() => TextureChanged?.Invoke(this, Texture);
        public Thickness PatchMargin { get; set; }
        /// <summary>Optional source region. An empty region uses the entire texture.</summary>
        public Rectangle RegionRect { get; set; }
        public bool DrawCenter { get; set; } = true;
        public ImageSamplingMode SamplingMode { get; set; } = ImageSamplingMode.Linear;
        public NinePatchAxisStretchMode HorizontalAxisStretchMode { get; set; }
        public NinePatchAxisStretchMode VerticalAxisStretchMode { get; set; }
        public void SetPatchMargin(Side side, float value)
        {
            if (!Enum.IsDefined(typeof(Side), side)) throw new ArgumentOutOfRangeException(nameof(side));
            PatchMargin = side == Side.Left ? new Thickness(value, PatchMargin.Top, PatchMargin.Right, PatchMargin.Bottom)
                : side == Side.Top ? new Thickness(PatchMargin.Left, value, PatchMargin.Right, PatchMargin.Bottom)
                : side == Side.Right ? new Thickness(PatchMargin.Left, PatchMargin.Top, value, PatchMargin.Bottom)
                : new Thickness(PatchMargin.Left, PatchMargin.Top, PatchMargin.Right, value);
            QueueLayout();
        }
        public float GetPatchMargin(Side side)
        {
            if (!Enum.IsDefined(typeof(Side), side)) throw new ArgumentOutOfRangeException(nameof(side));
            return side == Side.Left ? PatchMargin.Left : side == Side.Top ? PatchMargin.Top : side == Side.Right ? PatchMargin.Right : PatchMargin.Bottom;
        }
        public void SetPatchMargins(Thickness margins) { PatchMargin = margins; QueueLayout(); }
        public Thickness GetPatchMargins() => PatchMargin;
        public void SetRegionRect(Rectangle rect) { RegionRect = rect; QueueLayout(); }
        public Rectangle GetRegionRect() => RegionRect;
        public void SetDrawCenter(bool enabled) => DrawCenter = enabled;
        public bool IsDrawCenterEnabled() => DrawCenter;
        public void SetHAxisStretchMode(NinePatchAxisStretchMode mode) { if (!Enum.IsDefined(typeof(NinePatchAxisStretchMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode)); HorizontalAxisStretchMode = mode; }
        public NinePatchAxisStretchMode GetHAxisStretchMode() => HorizontalAxisStretchMode;
        public void SetVAxisStretchMode(NinePatchAxisStretchMode mode) { if (!Enum.IsDefined(typeof(NinePatchAxisStretchMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode)); VerticalAxisStretchMode = mode; }
        public NinePatchAxisStretchMode GetVAxisStretchMode() => VerticalAxisStretchMode;
        public override Vector2 GetMinimumSize() => Vector2.Max(CustomMinimumSize, new Vector2(Math.Max(0, PatchMargin.Left + PatchMargin.Right), Math.Max(0, PatchMargin.Top + PatchMargin.Bottom)));
        /// <summary>Resolves the source region used by this nine-patch against a texture size.</summary>
        public Rectangle GetSourceRegion(Vector2 textureSize)
        {
            var bounds = new Rectangle(0, 0, Math.Max(0, (int)MathF.Round(textureSize.X)), Math.Max(0, (int)MathF.Round(textureSize.Y)));
            return RegionRect.Width > 0 && RegionRect.Height > 0 ? Rectangle.Intersect(bounds, RegionRect) : bounds;
        }
        internal override void Draw(UIRenderContext context)
        {
            if (Texture == null) { base.Draw(context); return; }
            var region = GetSourceRegion(new Vector2(Texture.Width, Texture.Height));
            var left = Math.Max(0, Math.Min(region.Width, (int)PatchMargin.Left));
            var top = Math.Max(0, Math.Min(region.Height, (int)PatchMargin.Top));
            var right = Math.Max(0, Math.Min(region.Width - left, (int)PatchMargin.Right));
            var bottom = Math.Max(0, Math.Min(region.Height - top, (int)PatchMargin.Bottom));
            var middleSourceWidth = Math.Max(0, region.Width - left - right);
            var middleSourceHeight = Math.Max(0, region.Height - top - bottom);
            var middleDestinationWidth = Math.Max(0, Bounds.Width - left - right);
            var middleDestinationHeight = Math.Max(0, Bounds.Height - top - bottom);

            DrawPatch(context, new Rectangle(region.X, region.Y, left, top), new Rectangle(Bounds.Left, Bounds.Top, left, top));
            DrawPatch(context, new Rectangle(region.X + left, region.Y, middleSourceWidth, top), new Rectangle(Bounds.Left + left, Bounds.Top, middleDestinationWidth, top), HorizontalAxisStretchMode, NinePatchAxisStretchMode.Stretch);
            DrawPatch(context, new Rectangle(region.Right - right, region.Y, right, top), new Rectangle(Bounds.Right - right, Bounds.Top, right, top));
            DrawPatch(context, new Rectangle(region.X, region.Y + top, left, middleSourceHeight), new Rectangle(Bounds.Left, Bounds.Top + top, left, middleDestinationHeight), NinePatchAxisStretchMode.Stretch, VerticalAxisStretchMode);
            if (DrawCenter) DrawPatch(context, new Rectangle(region.X + left, region.Y + top, middleSourceWidth, middleSourceHeight), new Rectangle(Bounds.Left + left, Bounds.Top + top, middleDestinationWidth, middleDestinationHeight), HorizontalAxisStretchMode, VerticalAxisStretchMode);
            DrawPatch(context, new Rectangle(region.Right - right, region.Y + top, right, middleSourceHeight), new Rectangle(Bounds.Right - right, Bounds.Top + top, right, middleDestinationHeight), NinePatchAxisStretchMode.Stretch, VerticalAxisStretchMode);
            DrawPatch(context, new Rectangle(region.X, region.Bottom - bottom, left, bottom), new Rectangle(Bounds.Left, Bounds.Bottom - bottom, left, bottom));
            DrawPatch(context, new Rectangle(region.X + left, region.Bottom - bottom, middleSourceWidth, bottom), new Rectangle(Bounds.Left + left, Bounds.Bottom - bottom, middleDestinationWidth, bottom), HorizontalAxisStretchMode, NinePatchAxisStretchMode.Stretch);
            DrawPatch(context, new Rectangle(region.Right - right, region.Bottom - bottom, right, bottom), new Rectangle(Bounds.Right - right, Bounds.Bottom - bottom, right, bottom));
            DrawChildControls(context);
        }
        private void DrawPatch(UIRenderContext context, Rectangle source, Rectangle destination, NinePatchAxisStretchMode horizontal = NinePatchAxisStretchMode.Stretch, NinePatchAxisStretchMode vertical = NinePatchAxisStretchMode.Stretch)
        {
            if (source.Width <= 0 || source.Height <= 0 || destination.Width <= 0 || destination.Height <= 0) return;
            var horizontalSegments = GetSegments(source.X, source.Width, destination.X, destination.Width, horizontal);
            var verticalSegments = GetSegments(source.Y, source.Height, destination.Y, destination.Height, vertical);
            foreach (var x in horizontalSegments)
                foreach (var y in verticalSegments)
                    context.Drawing.DrawImage(
                        Texture,
                        new Rectangle(x.SourceStart, y.SourceStart, x.SourceLength, y.SourceLength),
                        new Rectangle(0, 0, x.DestinationLength, y.DestinationLength),
                        Matrix.CreateTranslation(x.DestinationStart, y.DestinationStart, 0),
                        Modulate,
                        SamplingMode);
        }
        private static List<NinePatchSegment> GetSegments(int sourceStart, int sourceLength, int destinationStart, int destinationLength, NinePatchAxisStretchMode mode)
        {
            var result = new List<NinePatchSegment>();
            if (mode == NinePatchAxisStretchMode.Stretch) { result.Add(new NinePatchSegment(sourceStart, sourceLength, destinationStart, destinationLength)); return result; }
            var count = GetTileCount(sourceLength, destinationLength, mode);
            var fittedSize = mode == NinePatchAxisStretchMode.TileFit ? destinationLength / count : sourceLength;
            var consumed = 0;
            for (var i = 0; i < count && consumed < destinationLength; i++)
            {
                var destinationSize = Math.Min(fittedSize, destinationLength - consumed);
                var sourceSize = mode == NinePatchAxisStretchMode.TileFit ? sourceLength : Math.Min(sourceLength, destinationSize);
                result.Add(new NinePatchSegment(sourceStart, sourceSize, destinationStart + consumed, destinationSize));
                consumed += destinationSize;
            }
            return result;
        }
        /// <summary>Godot's real AXIS_STRETCH_MODE_TILE_FIT tile count (canvas.glsl) rounds to the
        /// NEAREST integer (floor(x+0.5)), not up - plain Tile mode keeps ceiling since it only needs
        /// enough untouched-size tiles to cover the destination, not a specific stretched quantity.</summary>
        internal static int GetTileCount(int sourceLength, int destinationLength, NinePatchAxisStretchMode mode)
        {
            return mode == NinePatchAxisStretchMode.TileFit
                ? Math.Max(1, (int)Math.Floor(destinationLength / (double)sourceLength + 0.5))
                : Math.Max(1, (int)Math.Ceiling(destinationLength / (double)sourceLength));
        }
        private readonly struct NinePatchSegment
        {
            public NinePatchSegment(int sourceStart, int sourceLength, int destinationStart, int destinationLength) { SourceStart = sourceStart; SourceLength = sourceLength; DestinationStart = destinationStart; DestinationLength = destinationLength; }
            public int SourceStart { get; }
            public int SourceLength { get; }
            public int DestinationStart { get; }
            public int DestinationLength { get; }
        }
    }

    public enum AspectRatioMode { Fit, Cover, WidthControlsHeight, HeightControlsWidth }
    /// <summary>Placement of the ratio-constrained child inside an AspectRatioContainer.</summary>
    public enum AspectRatioAlignment { Begin, Center, End }
    /// <summary>Constrains visible children to a target aspect ratio using fit, cover, or axis-driven sizing and alignment.</summary>
    public sealed class AspectRatioContainer : Container
    {
        private float _ratio = 1;
        private AspectRatioMode _stretchMode = AspectRatioMode.Fit;
        private AspectRatioAlignment _alignmentHorizontal = AspectRatioAlignment.Center;
        private AspectRatioAlignment _alignmentVertical = AspectRatioAlignment.Center;

        public float Ratio { get => _ratio; set { _ratio = value; QueueLayout(); } }
        public AspectRatioMode StretchMode { get => _stretchMode; set { _stretchMode = value; QueueLayout(); } }
        public AspectRatioAlignment AlignmentHorizontal { get => _alignmentHorizontal; set { _alignmentHorizontal = value; QueueLayout(); } }
        public AspectRatioAlignment AlignmentVertical { get => _alignmentVertical; set { _alignmentVertical = value; QueueLayout(); } }

        public override Vector2 GetMinimumSize()
        {
            var minimum = CustomMinimumSize;
            foreach (var child in Children)
                if (child.Visible) minimum = Vector2.Max(minimum, child.GetMinimumSize());
            return minimum;
        }

        /// <summary>Calculates the local child rectangle using Godot's ratio, mode, and alignment rules.</summary>
        public Rectangle GetChildRect(Vector2 childMinimumSize)
        {
            if (Ratio <= 0) return new Rectangle(0, 0, Math.Max(0, (int)MathF.Round(Size.X)), Math.Max(0, (int)MathF.Round(Size.Y)));

            var ratioSize = new Vector2(Ratio, 1);
            float scale;
            switch (StretchMode)
            {
                case AspectRatioMode.WidthControlsHeight: scale = Size.X / ratioSize.X; break;
                case AspectRatioMode.HeightControlsWidth: scale = Size.Y / ratioSize.Y; break;
                case AspectRatioMode.Cover: scale = Math.Max(Size.X / ratioSize.X, Size.Y / ratioSize.Y); break;
                default: scale = Math.Min(Size.X / ratioSize.X, Size.Y / ratioSize.Y); break;
            }
            var childSize = Vector2.Max(ratioSize * scale, childMinimumSize);
            var offset = new Vector2(
                Align(AlignmentHorizontal) * (Size.X - childSize.X),
                Align(AlignmentVertical) * (Size.Y - childSize.Y));
            return new Rectangle((int)MathF.Round(offset.X), (int)MathF.Round(offset.Y), Math.Max(0, (int)MathF.Round(childSize.X)), Math.Max(0, (int)MathF.Round(childSize.Y)));
        }

        protected override void ArrangeChildren()
        {
            var rtl = IsLayoutRtl();
            foreach (var child in Children)
            {
                if (!child.Visible) continue;
                var rect = GetChildRect(child.GetMinimumSize());
                // Godot mirrors the fitted rect horizontally under RTL rather than swapping alignment modes,
                // so Begin/End visually flip sides while Center stays put.
                var x = rtl ? Size.X - rect.X - rect.Width : rect.X;
                // Godot's AspectRatioContainer::_notification hands this aspect-fitted rect to
                // Container::fit_child_in_rect, which applies a second layer on top: a non-Fill child is
                // resized back down to its own minimum and aligned within the rect via its own size flags,
                // rather than being stretched to the aspect-computed size. Route through the same shared
                // helper used by MarginContainer so that layer isn't silently skipped here.
                FitChildInRect(child, new Vector2(x, rect.Y), new Vector2(rect.Width, rect.Height), rtl);
            }
        }

        private static float Align(AspectRatioAlignment alignment) => alignment == AspectRatioAlignment.Begin ? 0 : alignment == AspectRatioAlignment.End ? 1 : .5f;
    }

    /// <summary>Draws a configurable outline around its bounds without filling the interior.</summary>
    public sealed class ReferenceRect : Control
    {
        public Color BorderColor { get; set; } = Color.Red;
        public int BorderWidth { get; set; } = 1;
        internal override void Draw(UIRenderContext context) { context.Border(Bounds, BorderColor, BorderWidth); base.Draw(context); }
    }

    public abstract class Separator : Control
    {
        protected Separator(Orientation orientation) { Orientation = orientation; }
        public Orientation Orientation { get; }
    }
    /// <summary>Draws a horizontal themed divider with a fixed cross-axis minimum.</summary>
    public sealed class HSeparator : Separator
    {
        public HSeparator() : base(Orientation.Horizontal) { }
        public override Vector2 GetMinimumSize() => Vector2.Max(CustomMinimumSize, new Vector2(0, 2));
        internal override void Draw(UIRenderContext context) { context.Fill(new Rectangle(Bounds.X, Bounds.Center.Y, Bounds.Width, 1), context.Theme.PanelBorderColor); base.Draw(context); }
    }
    /// <summary>Draws a vertical themed divider with a fixed cross-axis minimum.</summary>
    public sealed class VSeparator : Separator
    {
        public VSeparator() : base(Orientation.Vertical) { }
        public override Vector2 GetMinimumSize() => Vector2.Max(CustomMinimumSize, new Vector2(2, 0));
        internal override void Draw(UIRenderContext context) { context.Fill(new Rectangle(Bounds.Center.X, Bounds.Y, 1, Bounds.Height), context.Theme.PanelBorderColor); base.Draw(context); }
    }

    public enum SplitContainerDraggerVisibility { Visible, Hidden, HiddenCollapsed }

    /// <summary>Partitions children along one axis with pointer- and keyboard-adjustable dividers that preserve child minimum sizes.</summary>
    public class SplitContainer : TemplatedControl
    {
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.Group;
        private readonly List<float> _splitOffsets = new List<float> { 0 };
        private readonly List<float> _defaultDraggerPositions = new List<float>();
        private readonly List<float> _resolvedDraggerPositions = new List<float>();
        private float _dragAreaSize = 6;
        private int _draggingIndex = -1;
        private int _selectedDraggerIndex;
        private float _dragStartPointerMain;
        private float _dragStartSplitOffset;
        private readonly List<SplitContainer> _nestedIntersectionDraggers = new List<SplitContainer>();
        private bool _collapsed;
        private bool _draggingEnabled = true;
        private bool _draggingNestedIntersections;
        private bool _touchDraggerEnabled;
        private float _touchDraggerSize = 24;
        private SplitContainerDraggerVisibility _draggerVisibility = SplitContainerDraggerVisibility.Visible;
        public SplitContainer(Orientation orientation)
        {
            Orientation = orientation;
            FocusMode = FocusMode.All;
            ChildAdded += OnLogicalChildrenChanged;
            ChildRemoved += OnLogicalChildrenChanged;
        }
        public Orientation Orientation { get; }
        /// <summary>Offset from the default half-way split, matching Godot's persisted split_offset.</summary>
        public float SplitOffset { get => GetSplitOffset(); set => SetSplitOffset(value); }
        public float ResolvedSplitOffset => GetResolvedSplitOffset();
        public IReadOnlyList<float> SplitOffsets => _splitOffsets;
        public void SetSplitOffset(float offset, int index = 0)
        {
            if (index < 0 || index >= _splitOffsets.Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (_splitOffsets[index] == offset) return;
            _splitOffsets[index] = offset;
            QueueSplitLayout();
        }
        public float GetSplitOffset(int index = 0)
        {
            if (index < 0 || index >= _splitOffsets.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return _splitOffsets[index];
        }
        public void SetSplitOffsets(params float[] offsets)
        {
            if (offsets == null) throw new ArgumentNullException(nameof(offsets));
            _splitOffsets.Clear();
            _splitOffsets.AddRange(offsets);
            if (_splitOffsets.Count == 0) _splitOffsets.Add(0);
            QueueSplitLayout();
        }
        public float[] GetSplitOffsets() => _splitOffsets.ToArray();
        public float GetResolvedSplitOffset(int index = 0)
        {
            if (index < 0 || index >= _resolvedDraggerPositions.Count || index >= _defaultDraggerPositions.Count) return 0;
            return _resolvedDraggerPositions[index] - _defaultDraggerPositions[index];
        }
        public float DragAreaSize { get => _dragAreaSize; set { _dragAreaSize = Math.Max(0, value); QueueSplitLayout(); } }
        public bool Collapsed { get => _collapsed; set { _collapsed = value; QueueSplitLayout(); } }
        public bool DraggingEnabled { get => _draggingEnabled; set { _draggingEnabled = value; QueueSplitLayout(); } }
        public bool DraggingNestedIntersections { get => _draggingNestedIntersections; set => _draggingNestedIntersections = value; }
        public bool TouchDraggerEnabled { get => _touchDraggerEnabled; set { _touchDraggerEnabled = value; QueueSplitLayout(); } }
        public float TouchDraggerSize { get => _touchDraggerSize; set { _touchDraggerSize = Math.Max(0, value); QueueSplitLayout(); } }
        public SplitContainerDraggerVisibility DraggerVisibility { get => _draggerVisibility; set { _draggerVisibility = value; QueueSplitLayout(); } }
        public void SetDraggingNestedIntersections(bool enabled) => DraggingNestedIntersections = enabled;
        public bool IsDraggingNestedIntersections() => DraggingNestedIntersections;
        public void SetTouchDraggerEnabled(bool enabled) => TouchDraggerEnabled = enabled;
        public bool IsTouchDraggerEnabled() => TouchDraggerEnabled;
        public override Vector2 GetMinimumSize()
        {
            if (Children.Count == 0) return CustomMinimumSize;
            var main = Math.Max(0, DragAreaSize) * Math.Max(0, Children.Count - 1);
            var cross = 0f;
            foreach (var child in Children)
            {
                var minimum = child.GetMinimumSize();
                main += Orientation == Orientation.Horizontal ? minimum.X : minimum.Y;
                cross = Math.Max(cross, Orientation == Orientation.Horizontal ? minimum.Y : minimum.X);
            }
            return Vector2.Max(CustomMinimumSize, Orientation == Orientation.Horizontal ? new Vector2(main, cross) : new Vector2(cross, main));
        }
        /// <summary>
        /// Marks both this container and the presenter that actually arranges the children.
        /// <para>
        /// Used wherever a split's geometry changes. It replaces an unconditional
        /// <c>TemplateRoot.QueueLayout()</c> at the end of every arrange, which re-dirtied every
        /// ancestor *after* they had cleared for the pass: a single split recovered on the next
        /// pass, but nested ones - which is what any real dock layout is - never settled at all.
        /// </para>
        /// </summary>
        private void QueueSplitLayout()
        {
            TemplateRoot?.QueueLayout();
            QueueLayout();
        }

        protected override void ArrangeChildren() => base.ArrangeChildren();
        /// <summary>Clamps the saved offset so both visible children retain their minimum main-axis size.</summary>
        public void ClampSplitOffset(int index = 0)
        {
            if (Children.Count < 2) return;
            EnsureDraggerState();
            if (index < 0 || index >= _splitOffsets.Count) throw new ArgumentOutOfRangeException(nameof(index));
            ResolveDraggerPositions();
            _splitOffsets[index] = _resolvedDraggerPositions[index] - _defaultDraggerPositions[index];
            QueueSplitLayout();
        }
        internal void ArrangePresentedChildren(IReadOnlyList<ContentPresenter> presenters, Vector2 availableSize)
        {
            if (presenters.Count == 0) return;
            if (Children.Count == 1)
            {
                presenters[0].Position = Vector2.Zero;
                presenters[0].Size = availableSize;
                _defaultDraggerPositions.Clear();
                _resolvedDraggerPositions.Clear();
                return;
            }
            EnsureDraggerState();
            ResolveDraggerPositions();
            var total = GetMainSize(availableSize);
            var rtl = Orientation == Orientation.Horizontal && IsLayoutRtl();
            for (var index = 0; index < Children.Count; index++)
            {
                var start = index == 0 ? 0 : _resolvedDraggerPositions[index - 1] + DragAreaSize;
                var end = index == Children.Count - 1 ? total : _resolvedDraggerPositions[index];
                var length = Math.Max(0, end - start);
                if (rtl) start = total - end;
                if (Orientation == Orientation.Horizontal)
                {
                    presenters[index].Position = new Vector2(start, 0);
                    presenters[index].Size = new Vector2(length, availableSize.Y);
                }
                else
                {
                    presenters[index].Position = new Vector2(0, start);
                    presenters[index].Size = new Vector2(availableSize.X, length);
                }
            }
        }
        protected internal override void PointerPressed(Point point)
        {
            base.PointerPressed(point);
            _nestedIntersectionDraggers.Clear();
            if (!BeginDividerDrag(point)) return;
            foreach (var nested in GetDescendantSplitContainers())
            {
                if (!nested.DraggingNestedIntersections || nested.Orientation == Orientation) continue;
                if (nested.BeginDividerDrag(point, DragAreaSize)) _nestedIntersectionDraggers.Add(nested);
            }
        }
        internal override bool HitTestBeforeChildren(Point point) => IsOverDragger(point);
        /// <summary>
        /// Shows the axis resize cursor over a dragger, which is the only affordance a thin divider has
        /// before it is pressed, and holds it for the whole gesture once one is grabbed, since a drag
        /// routinely runs the pointer past the bar it is moving. An explicit <see cref="Control.Cursor"/>
        /// on this container wins, so a host that wants its own cursor still gets it.
        /// </summary>
        public override Cursor GetCursorAt(Point position)
        {
            if (Cursor != Cursor.Inherited || (_draggingIndex < 0 && !IsOverDragger(position))) return base.GetCursorAt(position);
            return Orientation == Orientation.Horizontal ? Cursor.SizeHorizontal : Cursor.SizeVertical;
        }
        /// <summary>Whether a point lands on a grabbable dragger. Hit testing, the resize cursor and the
        /// start of a drag share this, so the region that advertises a resize is the one that performs it.</summary>
        private bool IsOverDragger(Point point)
        {
            if (!DraggingEnabled || Collapsed || DraggerVisibility != SplitContainerDraggerVisibility.Visible) return false;
            for (var index = 0; index < _resolvedDraggerPositions.Count; index++)
            {
                var bounds = TouchDraggerEnabled ? GetTouchDraggerBounds(index) : GetDividerBounds(index);
                if (bounds.Contains(point)) return true;
            }
            return false;
        }
        protected internal override void PointerMoved(Point point)
        {
            if (_draggingIndex < 0) return;
            // Godot's SplitContainerDragger::gui_input tracks a relative delta from the press point
            // rather than recomputing an absolute position every move event, so wherever inside the bar
            // the drag started is preserved for the whole gesture; RTL horizontal splits invert the sign.
            var main = Orientation == Orientation.Horizontal ? point.X : point.Y;
            var delta = main - _dragStartPointerMain;
            if (Orientation == Orientation.Horizontal && IsLayoutRtl()) delta = -delta;
            SetSplitOffset(_dragStartSplitOffset + delta, _draggingIndex);
            foreach (var nested in _nestedIntersectionDraggers) nested.MoveDividerDrag(point);
        }
        protected internal override void PointerReleased(Point point, bool isInside)
        {
            _draggingIndex = -1;
            foreach (var nested in _nestedIntersectionDraggers) nested._draggingIndex = -1;
            _nestedIntersectionDraggers.Clear();
        }
        internal override void CancelInput()
        {
            _draggingIndex = -1;
            foreach (var nested in _nestedIntersectionDraggers) nested._draggingIndex = -1;
            _nestedIntersectionDraggers.Clear();
            base.CancelInput();
        }
        /// <summary>Nudges the dragger by 10% of the container's extent per key press, matching Godot's SplitContainerDragger::gui_input keyboard handling.</summary>
        internal override void KeyPressed(Keys key)
        {
            if (Collapsed || Children.Count < 2 || !DraggingEnabled) { base.KeyPressed(key); return; }
            EnsureDraggerState();
            _selectedDraggerIndex = Math.Min(_selectedDraggerIndex, _splitOffsets.Count - 1);
            var extent = Orientation == Orientation.Horizontal ? Size.X : Size.Y;
            var decrementKey = Orientation == Orientation.Vertical ? Keys.Up : (IsLayoutRtl() ? Keys.Right : Keys.Left);
            var incrementKey = Orientation == Orientation.Vertical ? Keys.Down : (IsLayoutRtl() ? Keys.Left : Keys.Right);
            if (key == decrementKey) { SetSplitOffset(GetSplitOffset(_selectedDraggerIndex) - extent * 0.1f, _selectedDraggerIndex); ClampSplitOffset(_selectedDraggerIndex); return; }
            if (key == incrementKey) { SetSplitOffset(GetSplitOffset(_selectedDraggerIndex) + extent * 0.1f, _selectedDraggerIndex); ClampSplitOffset(_selectedDraggerIndex); return; }
            base.KeyPressed(key);
        }
        internal int ResolvedDraggerCount => _resolvedDraggerPositions.Count;
        private void EnsureDraggerState()
        {
            var required = Math.Max(1, Children.Count - 1);
            while (_splitOffsets.Count < required) _splitOffsets.Add(0);
            if (_splitOffsets.Count > required) _splitOffsets.RemoveRange(required, _splitOffsets.Count - required);
        }
        private void ResolveDraggerPositions()
        {
            var draggerCount = Math.Max(0, Children.Count - 1);
            _defaultDraggerPositions.Clear();
            _resolvedDraggerPositions.Clear();
            if (draggerCount == 0) return;
            var total = GetMainSize(Size);
            var available = Math.Max(0, total - DragAreaSize * draggerCount);
            var defaultChildSize = available / Children.Count;
            var position = 0f;
            for (var index = 0; index < draggerCount; index++)
            {
                position += defaultChildSize;
                _defaultDraggerPositions.Add(position);
                position += DragAreaSize;
            }
            for (var index = 0; index < draggerCount; index++)
            {
                var minimum = 0f;
                for (var childIndex = 0; childIndex <= index; childIndex++) minimum += GetMainSize(Children[childIndex].GetMinimumSize());
                minimum += DragAreaSize * index;
                var trailingMinimum = 0f;
                for (var childIndex = index + 1; childIndex < Children.Count; childIndex++) trailingMinimum += GetMainSize(Children[childIndex].GetMinimumSize());
                trailingMinimum += DragAreaSize * (draggerCount - index - 1);
                var maximum = Math.Max(minimum, total - DragAreaSize - trailingMinimum);
                var desired = _defaultDraggerPositions[index] + (Collapsed ? 0 : _splitOffsets[index]);
                _resolvedDraggerPositions.Add(MathHelper.Clamp(desired, minimum, maximum));
            }
            for (var index = 0; index < draggerCount - 1; index++)
            {
                var nextMinimum = _resolvedDraggerPositions[index] + DragAreaSize + GetMainSize(Children[index + 1].GetMinimumSize());
                if (_resolvedDraggerPositions[index + 1] < nextMinimum) _resolvedDraggerPositions[index + 1] = nextMinimum;
            }
        }
        private float GetMainSize(Vector2 size) => Orientation == Orientation.Horizontal ? size.X : size.Y;
        private bool BeginDividerDrag(Point point, float crossAxisExpansion = 0)
        {
            _draggingIndex = -1;
            if (!DraggingEnabled || Collapsed || DraggerVisibility != SplitContainerDraggerVisibility.Visible) return false;
            for (var index = 0; index < _resolvedDraggerPositions.Count; index++)
            {
                var bounds = TouchDraggerEnabled ? GetTouchDraggerBounds(index) : GetDividerBounds(index);
                if (Orientation == Orientation.Horizontal)
                    bounds.Inflate(0, (int)Math.Ceiling(crossAxisExpansion));
                else
                    bounds.Inflate((int)Math.Ceiling(crossAxisExpansion), 0);
                if (!bounds.Contains(point)) continue;
                _draggingIndex = index;
                _selectedDraggerIndex = index;
                _dragStartPointerMain = Orientation == Orientation.Horizontal ? point.X : point.Y;
                _dragStartSplitOffset = GetSplitOffset(index);
                return true;
            }
            return false;
        }
        private void MoveDividerDrag(Point point)
        {
            if (_draggingIndex < 0) return;
            var main = Orientation == Orientation.Horizontal ? point.X : point.Y;
            var delta = main - _dragStartPointerMain;
            if (Orientation == Orientation.Horizontal && IsLayoutRtl()) delta = -delta;
            SetSplitOffset(_dragStartSplitOffset + delta, _draggingIndex);
        }
        private IEnumerable<SplitContainer> GetDescendantSplitContainers()
        {
            var pending = new Stack<Control>();
            for (var index = Children.Count - 1; index >= 0; index--) pending.Push(Children[index]);
            while (pending.Count > 0)
            {
                var control = pending.Pop();
                if (control is SplitContainer split) yield return split;
                for (var index = control.Children.Count - 1; index >= 0; index--) pending.Push(control.Children[index]);
            }
        }
        internal Rectangle GetDividerBounds(int index)
        {
            var total = GetMainSize(Size);
            var offset = _resolvedDraggerPositions[index];
            if (Orientation == Orientation.Horizontal && IsLayoutRtl()) offset = total - offset - DragAreaSize;
            return Orientation == Orientation.Horizontal
                ? new Rectangle(Bounds.X + (int)offset, Bounds.Y, (int)DragAreaSize, Bounds.Height)
                : new Rectangle(Bounds.X, Bounds.Y + (int)offset, Bounds.Width, (int)DragAreaSize);
        }
        internal Rectangle GetTouchDraggerBounds(int index)
        {
            var divider = GetDividerBounds(index);
            var size = (int)Math.Ceiling(Math.Max(DragAreaSize, TouchDraggerSize));
            return new Rectangle(divider.Center.X - size / 2, divider.Center.Y - size / 2, size, size);
        }
        private void OnLogicalChildrenChanged(Control owner, Control child)
        {
            if (TemplateRoot is SplitContainerPresenter presenter) presenter.SyncChildren();
            else if (child.VisualParent == this) RemoveVisualChild(child);
            QueueLayout();
        }
    }
    /// <summary>Partitions children into horizontally adjacent panes with vertical draggable dividers.</summary>
    public sealed class HSplitContainer : SplitContainer { public HSplitContainer() : base(Orientation.Horizontal) { } }
    /// <summary>Partitions children into vertically adjacent panes with horizontal draggable dividers.</summary>
    public sealed class VSplitContainer : SplitContainer { public VSplitContainer() : base(Orientation.Vertical) { } }

    public enum FlowAlignment { Begin, Center, End }
    public enum FlowLastWrapAlignment { Inherit, Begin, Center, End }

    /// <summary>Flows children into wrapping lines, distributing expansion and applying configurable alignment to incomplete lines.</summary>
    public class FlowContainer : Container
    {
        private bool _fixedOrientation;
        private Orientation _orientation;
        private float _separation = 4;
        private FlowAlignment _alignment;
        private FlowLastWrapAlignment _lastWrapAlignment;
        private bool _reverseFill;
        /// <summary>Cross-axis extent (perpendicular to flow direction) needed to show every wrapped line, cached from the last layout pass, matching Godot's FlowContainer::cached_size.</summary>
        private float _cachedCrossAxisExtent;
        public FlowContainer(Orientation orientation) { _orientation = orientation; }
        public Orientation Orientation
        {
            get => _orientation;
            set
            {
                // Godot's HFlowContainer/VFlowContainer set is_fixed = true in their constructors so
                // set_vertical ERR_FAILs on them, while the base FlowContainer allows runtime reorientation.
                if (_fixedOrientation || _orientation == value) return;
                _orientation = value;
                QueueLayout();
            }
        }
        public float Separation { get => _separation; set { if (_separation == value) return; _separation = value; QueueLayout(); } }
        public FlowAlignment Alignment { get => _alignment; set { if (_alignment == value) return; _alignment = value; QueueLayout(); } }
        public FlowLastWrapAlignment LastWrapAlignment { get => _lastWrapAlignment; set { if (_lastWrapAlignment == value) return; _lastWrapAlignment = value; QueueLayout(); } }
        public bool ReverseFill { get => _reverseFill; set { if (_reverseFill == value) return; _reverseFill = value; QueueLayout(); } }
        public int LineCount { get; private set; }
        public int LineMaxChildCount { get; private set; }
        protected void SetFixedOrientation() => _fixedOrientation = true;
        /// <summary>Matches Godot's FlowContainer::get_minimum_size: the main axis only needs to fit the single
        /// largest child (a tighter fit just wraps into more lines), while the cross axis needs the full wrapped extent.</summary>
        public override Vector2 GetMinimumSize()
        {
            var mainMax = 0f;
            foreach (var child in Children)
            {
                if (!child.Visible) continue;
                var min = child.GetMinimumSize();
                mainMax = Math.Max(mainMax, Orientation == Orientation.Horizontal ? min.X : min.Y);
            }
            return Orientation == Orientation.Horizontal
                ? Vector2.Max(CustomMinimumSize, new Vector2(mainMax, _cachedCrossAxisExtent))
                : Vector2.Max(CustomMinimumSize, new Vector2(_cachedCrossAxisExtent, mainMax));
        }
        protected override void ArrangeChildren()
        {
            var visible = new List<Control>(); foreach (var child in Children) if (child.Visible) visible.Add(child);
            var lines = new List<List<Control>>(); var current = new List<Control>(); var main = 0f;
            var available = Orientation == Orientation.Horizontal ? Size.X : Size.Y;
            foreach (var child in visible)
            {
                var childSize = child.GetBoundDesiredSize();
                var childMain = Orientation == Orientation.Horizontal ? childSize.X : childSize.Y;
                if (current.Count > 0 && main + Separation + childMain > available) { lines.Add(current); current = new List<Control>(); main = 0; }
                if (current.Count > 0) main += Separation;
                current.Add(child); main += childMain;
            }
            if (current.Count > 0) lines.Add(current);
            LineCount = lines.Count; LineMaxChildCount = lines.Count > 0 ? lines[0].Count : 0;
            if (lines.Count == 0) { _cachedCrossAxisExtent = 0; return; }
            var rtl = IsLayoutRtl();

            var lineMains = new float[lines.Count]; var lineCrosses = new float[lines.Count]; var expandWeights = new float[lines.Count]; var remainings = new float[lines.Count];
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i]; float lineMainV = 0, lineCrossV = 0, expandW = 0;
                foreach (var child in line)
                {
                    var min = child.GetBoundDesiredSize();
                    lineMainV += Orientation == Orientation.Horizontal ? min.X : min.Y;
                    lineCrossV = Math.Max(lineCrossV, Orientation == Orientation.Horizontal ? min.Y : min.X);
                    var flags = Orientation == Orientation.Horizontal ? child.HorizontalSizeFlags : child.VerticalSizeFlags;
                    // Godot's Control::set_stretch_ratio performs no clamping, so a true zero ratio is
                    // valid and must contribute nothing to the pool (matching FlowContainer::_resort's
                    // line_stretch_ratio_total, which a zero ratio leaves untouched).
                    if ((flags & SizeFlags.Expand) != 0) expandW += child.SizeFlagsStretchRatio;
                }
                lineMainV += Separation * Math.Max(0, line.Count - 1);
                lineMains[i] = lineMainV; lineCrosses[i] = lineCrossV; expandWeights[i] = expandW;
                remainings[i] = Math.Max(0, available - lineMainV);
            }
            var crossExtent = 0f; for (var i = 0; i < lines.Count; i++) crossExtent += lineCrosses[i];
            crossExtent += Separation * Math.Max(0, lines.Count - 1);
            _cachedCrossAxisExtent = crossExtent;

            var stretchBonuses = new Dictionary<Control, float>[lines.Count];
            var stretchAvailAfterExpansion = new float[lines.Count];
            for (var i = 0; i < lines.Count; i++)
            {
                var bonuses = new Dictionary<Control, float>();
                stretchBonuses[i] = bonuses;
                var active = new List<Control>();
                var remaining = remainings[i];
                var ratioTotal = expandWeights[i];
                foreach (var child in lines[i])
                {
                    bonuses[child] = 0;
                    var flags = Orientation == Orientation.Horizontal ? child.HorizontalSizeFlags : child.VerticalSizeFlags;
                    if ((flags & SizeFlags.Expand) != 0)
                    {
                        bonuses[child] = -1;
                        active.Add(child);
                    }
                }
                while (ratioTotal > 0)
                {
                    var refitSuccessful = true;
                    foreach (var child in active)
                    {
                        if (bonuses[child] >= 0) continue;
                        var childStretch = remaining * child.SizeFlagsStretchRatio / ratioTotal;
                        var minimum = child.GetBoundDesiredSize();
                        var maximum = child.GetCombinedMaximumSize();
                        var childMinimum = Orientation == Orientation.Horizontal ? minimum.X : minimum.Y;
                        var childMaximum = Orientation == Orientation.Horizontal ? maximum.X : maximum.Y;
                        var maximumStretch = childMaximum >= 0 ? Math.Max(childMaximum - childMinimum, 0) : float.MaxValue;
                        if (childStretch > maximumStretch)
                        {
                            bonuses[child] = maximumStretch;
                            ratioTotal -= child.SizeFlagsStretchRatio;
                            remaining -= maximumStretch;
                            refitSuccessful = false;
                            break;
                        }
                    }
                    if (!refitSuccessful) continue;
                    foreach (var child in active)
                        if (bonuses[child] < 0) bonuses[child] = remaining * child.SizeFlagsStretchRatio / ratioTotal;
                    break;
                }
                var usedStretch = 0f;
                foreach (var bonus in bonuses.Values) usedStretch += Math.Max(bonus, 0);
                stretchAvailAfterExpansion[i] = Math.Max(remainings[i] - usedStretch, 0);
            }

            // Godot's real is_filled: every line is filled except possibly the true last one, which is
            // "filled" only if appending one more copy of its own last child's minimum size would overflow.
            var isFilled = new bool[lines.Count];
            for (var i = 0; i < lines.Count; i++) isFilled[i] = true;
            var lastLine = lines[lines.Count - 1]; var lastChild = lastLine[lastLine.Count - 1];
            var lastChildMain = Orientation == Orientation.Horizontal ? lastChild.GetMinimumSize().X : lastChild.GetMinimumSize().Y;
            isFilled[lines.Count - 1] = lineMains[lines.Count - 1] + lastChildMain > available;

            float AlignmentOffset(int lineIndex)
            {
                var stretchAvail = stretchAvailAfterExpansion[lineIndex];
                if (stretchAvail <= 0) return 0f;
                var isNotFirstAndNotFilled = lineIndex != 0 && !isFilled[lineIndex];
                var priorStretchAvail = isNotFirstAndNotFilled ? stretchAvailAfterExpansion[lineIndex - 1] : 0f;
                switch (Alignment)
                {
                    case FlowAlignment.Begin:
                        if (LastWrapAlignment != FlowLastWrapAlignment.Inherit && isNotFirstAndNotFilled)
                        {
                            if (LastWrapAlignment == FlowLastWrapAlignment.End) return stretchAvail - priorStretchAvail;
                            if (LastWrapAlignment == FlowLastWrapAlignment.Center) return (stretchAvail - priorStretchAvail) * 0.5f;
                        }
                        return 0f;
                    case FlowAlignment.Center:
                        if (LastWrapAlignment != FlowLastWrapAlignment.Inherit && LastWrapAlignment != FlowLastWrapAlignment.Center && isNotFirstAndNotFilled)
                            return LastWrapAlignment == FlowLastWrapAlignment.End ? stretchAvail - priorStretchAvail * 0.5f : priorStretchAvail * 0.5f;
                        return stretchAvail * 0.5f;
                    case FlowAlignment.End:
                        if (LastWrapAlignment != FlowLastWrapAlignment.Inherit && LastWrapAlignment != FlowLastWrapAlignment.End && isNotFirstAndNotFilled)
                            return LastWrapAlignment == FlowLastWrapAlignment.Begin ? priorStretchAvail : priorStretchAvail + (stretchAvail - priorStretchAvail) * 0.5f;
                        return stretchAvail;
                    default: return 0f;
                }
            }

            var crossCursor = 0f;
            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var line = lines[lineIndex]; var lineCross = lineCrosses[lineIndex];
                var mainCursor = AlignmentOffset(lineIndex);
                foreach (var child in line)
                {
                    var minimum = child.GetBoundDesiredSize(); var flags = Orientation == Orientation.Horizontal ? child.HorizontalSizeFlags : child.VerticalSizeFlags;
                    var childMain = Orientation == Orientation.Horizontal ? minimum.X : minimum.Y;
                    if ((flags & SizeFlags.Expand) != 0) childMain += Math.Max(stretchBonuses[lineIndex][child], 0);
                    var crossFlags = Orientation == Orientation.Horizontal ? child.VerticalSizeFlags : child.HorizontalSizeFlags;
                    var childCross = (crossFlags & SizeFlags.Fill) != 0 ? lineCross : Math.Min(lineCross, Orientation == Orientation.Horizontal ? minimum.Y : minimum.X);
                    var crossOffset = Align((int)lineCross, (int)childCross, crossFlags);
                    if (Orientation == Orientation.Horizontal) { child.Position = new Vector2(mainCursor, crossCursor + crossOffset); child.Size = new Vector2(childMain, childCross); }
                    else { child.Position = new Vector2(crossCursor + crossOffset, mainCursor); child.Size = new Vector2(childCross, childMain); }
                    mainCursor += childMain + Separation;
                }
                crossCursor += lineCross + Separation;
            }

            // Godot applies RTL/ReverseFill as a whole-container axis flip on each child's final rect,
            // computed AFTER normal forward layout above - matching FlowContainer::_resort exactly:
            // reverse_fill mirrors Y only when horizontal; rtl mirrors X only when horizontal; and for
            // vertical flow the two combine by XOR to mirror X (the main axis is never mirrored vertically).
            foreach (var child in visible)
            {
                var pos = child.Position; var size = child.Size; var changed = false;
                if (Orientation == Orientation.Horizontal)
                {
                    if (ReverseFill) { pos.Y = Size.Y - pos.Y - size.Y; changed = true; }
                    if (rtl) { pos.X = Size.X - pos.X - size.X; changed = true; }
                }
                else if (rtl != ReverseFill)
                {
                    pos.X = Size.X - pos.X - size.X; changed = true;
                }
                if (changed) child.Position = pos;
            }
        }
        private static int Align(int available, int size, SizeFlags flags) => (flags & SizeFlags.ShrinkEnd) != 0 ? available - size : (flags & SizeFlags.ShrinkCenter) != 0 ? (available - size) / 2 : 0;
    }
    /// <summary>Flows children horizontally and wraps overflowing items onto additional rows.</summary>
    public sealed class HFlowContainer : FlowContainer { public HFlowContainer() : base(Orientation.Horizontal) { SetFixedOrientation(); } }
    /// <summary>Flows children vertically and wraps overflowing items into additional columns.</summary>
    public sealed class VFlowContainer : FlowContainer { public VFlowContainer() : base(Orientation.Vertical) { SetFixedOrientation(); } }

    /// <summary>Draws a themed panel and arranges visible children inside its content margins.</summary>
    public sealed class PanelContainer : Container
    {
        public Thickness Padding { get; set; } = new Thickness(4);
        private Thickness ContentPadding => GetThemeStyleBox("panel")?.ContentMargin ?? Padding;
        public override Vector2 GetMinimumSize()
        {
            var padding = ContentPadding;
            var size = CustomMinimumSize;
            foreach (var child in Children) if (child.Visible) size = Vector2.Max(size, child.GetMinimumSize() + new Vector2(padding.Horizontal, padding.Vertical));
            return size;
        }
        protected override void ArrangeChildren()
        {
            var padding = ContentPadding;
            foreach (var child in Children) { child.Position = new Vector2(padding.Left, padding.Top); child.Size = Vector2.Max(Vector2.Zero, Size - new Vector2(padding.Horizontal, padding.Vertical)); }
        }
        internal override void Draw(UIRenderContext context)
        {
            var style = GetThemeStyleBox("panel");
            if (style != null) style.Draw(context, Bounds); else { context.Fill(Bounds, context.Theme.PanelColor); context.Border(Bounds, context.Theme.PanelBorderColor); }
            base.Draw(context);
        }
    }
}
