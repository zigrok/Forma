// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Clipper2Lib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ClipperFillRule = Clipper2Lib.FillRule;

namespace Forma
{
    public enum GradientSpreadMethod { Pad, Repeat, Reflect }
    public enum GradientInterpolationSpace { Srgb, LinearSrgb }
    public enum StrokeLineCap { Butt, Square, Round }
    public enum StrokeLineJoin { Miter, Bevel, Round }
    public enum StrokeAlignment { Center, Inside, Outside }
    public enum FillRule { NonZero, EvenOdd }
    public enum GeometryCombineMode { Union, Intersect, Exclude, Xor }
    public enum ImageStretch { None, Fill, Contain, Cover, ScaleDown }
    public enum ImageTileMode { None, TileX, TileY, Tile }
    public enum ImageSamplingMode { Nearest, Linear }
    public enum ShapeStretch { None, Fill, Uniform, UniformToFill }
    [Flags]
    public enum TextDecoration { None = 0, Underline = 1, Strikethrough = 2 }

    public readonly struct CornerRadius : IEquatable<CornerRadius>
    {
        public CornerRadius(float uniformRadius) : this(uniformRadius, uniformRadius, uniformRadius, uniformRadius) { }
        public CornerRadius(float topLeft, float topRight, float bottomRight, float bottomLeft)
        {
            if (!float.IsFinite(topLeft) || topLeft < 0) throw new ArgumentOutOfRangeException(nameof(topLeft));
            if (!float.IsFinite(topRight) || topRight < 0) throw new ArgumentOutOfRangeException(nameof(topRight));
            if (!float.IsFinite(bottomRight) || bottomRight < 0) throw new ArgumentOutOfRangeException(nameof(bottomRight));
            if (!float.IsFinite(bottomLeft) || bottomLeft < 0) throw new ArgumentOutOfRangeException(nameof(bottomLeft));
            TopLeft = topLeft;
            TopRight = topRight;
            BottomRight = bottomRight;
            BottomLeft = bottomLeft;
        }
        public float TopLeft { get; }
        public float TopRight { get; }
        public float BottomRight { get; }
        public float BottomLeft { get; }
        public bool Equals(CornerRadius other) => TopLeft == other.TopLeft && TopRight == other.TopRight && BottomRight == other.BottomRight && BottomLeft == other.BottomLeft;
        public override bool Equals(object obj) => obj is CornerRadius other && Equals(other);
        public override int GetHashCode() => System.HashCode.Combine(TopLeft, TopRight, BottomRight, BottomLeft);
        public static bool operator ==(CornerRadius left, CornerRadius right) => left.Equals(right);
        public static bool operator !=(CornerRadius left, CornerRadius right) => !left.Equals(right);
    }

    public sealed class StrokeStyle
    {
        private IReadOnlyList<float> _dashArray = Array.Empty<float>();
        public static StrokeStyle Default { get; } = new StrokeStyle();
        public StrokeLineCap StartLineCap { get; set; }
        public StrokeLineCap EndLineCap { get; set; }
        public StrokeLineJoin LineJoin { get; set; } = StrokeLineJoin.Miter;
        public float MiterLimit { get; set; } = 4;
        public IReadOnlyList<float> DashArray
        {
            get => _dashArray;
            set => _dashArray = value == null ? Array.Empty<float>() : new List<float>(value).AsReadOnly();
        }
        public float DashOffset { get; set; }
        public StrokeAlignment Alignment { get; set; }
        internal void Validate()
        {
            if (!Enum.IsDefined(typeof(StrokeLineCap), StartLineCap) || !Enum.IsDefined(typeof(StrokeLineCap), EndLineCap)) throw new InvalidOperationException("Stroke caps are invalid.");
            if (!Enum.IsDefined(typeof(StrokeLineJoin), LineJoin)) throw new InvalidOperationException("Stroke join is invalid.");
            if (!Enum.IsDefined(typeof(StrokeAlignment), Alignment)) throw new InvalidOperationException("Stroke alignment is invalid.");
            if (!float.IsFinite(MiterLimit) || MiterLimit < 1) throw new InvalidOperationException("Miter limit must be finite and at least one.");
            if (!float.IsFinite(DashOffset)) throw new InvalidOperationException("Dash offset must be finite.");
            foreach (var dash in DashArray)
                if (!float.IsFinite(dash) || dash <= 0) throw new InvalidOperationException("Dash lengths must be finite and positive.");
        }
    }

    /// <summary>A one-child brush-backed box with padding and an optional border.</summary>
    public sealed class Border : Container
    {
        private readonly BoxShadowCollection _shadows = new BoxShadowCollection();
        public Brush Background { get; set; }
        public Brush BorderBrush { get; set; }
        public Thickness BorderThickness { get; set; }
        public Thickness Padding { get; set; }
        public CornerRadius CornerRadius { get; set; }
        public IList<BoxShadow> Shadows => _shadows;

        public override void AddChild(Control child)
        {
            if (VisualChildren.Count != 0) throw new InvalidOperationException("Border accepts one child.");
            base.AddChild(child);
        }

        public override Vector2 GetMinimumSize()
        {
            var inset = new Vector2(
                BorderThickness.Horizontal + Padding.Horizontal,
                BorderThickness.Vertical + Padding.Vertical);
            var child = VisualChildren.Count == 0 ? Vector2.Zero : VisualChildren[0].GetMinimumSize();
            return Vector2.Max(CustomMinimumSize, child + inset);
        }

        protected override void ArrangeChildren()
        {
            if (Children.Count == 0) return;
            var left = BorderThickness.Left + Padding.Left;
            var top = BorderThickness.Top + Padding.Top;
            var available = new Vector2(
                MathF.Max(0, Size.X - left - BorderThickness.Right - Padding.Right),
                MathF.Max(0, Size.Y - top - BorderThickness.Bottom - Padding.Bottom));
            FitChildInRect(VisualChildren[0], new Vector2(left, top), available, IsLayoutRtl());
        }

        internal override void Draw(UIRenderContext context)
        {
            var transform = Matrix.CreateTranslation(GlobalPosition.X, GlobalPosition.Y, 0);
            var left = UIRenderContext.GetVisibleHairlineThickness(BorderThickness.Left, context.DisplayScale);
            var top = UIRenderContext.GetVisibleHairlineThickness(BorderThickness.Top, context.DisplayScale);
            var right = UIRenderContext.GetVisibleHairlineThickness(BorderThickness.Right, context.DisplayScale);
            var bottom = UIRenderContext.GetVisibleHairlineThickness(BorderThickness.Bottom, context.DisplayScale);
            var width = MathF.Max(0, Size.X - left - right);
            var height = MathF.Max(0, Size.Y - top - bottom);
            var innerRadius = new CornerRadius(
                MathF.Max(0, CornerRadius.TopLeft - MathF.Max(left, top)),
                MathF.Max(0, CornerRadius.TopRight - MathF.Max(right, top)),
                MathF.Max(0, CornerRadius.BottomRight - MathF.Max(right, bottom)),
                MathF.Max(0, CornerRadius.BottomLeft - MathF.Max(left, bottom)));
            var content = GeometryPaths.Rectangle(new Vector2(width, height), innerRadius);
            var outer = GeometryPaths.Rectangle(Size, CornerRadius);
            foreach (var shadow in Shadows)
            {
                if (shadow.Inset) continue;
                var spread = shadow.SpreadRadius;
                var shadowSize = new Vector2(MathF.Max(0, Size.X + spread * 2), MathF.Max(0, Size.Y + spread * 2));
                var shadowRadius = new CornerRadius(
                    MathF.Max(0, CornerRadius.TopLeft + spread),
                    MathF.Max(0, CornerRadius.TopRight + spread),
                    MathF.Max(0, CornerRadius.BottomRight + spread),
                    MathF.Max(0, CornerRadius.BottomLeft + spread));
                var shadowPath = GeometryPaths.Rectangle(shadowSize, shadowRadius);
                var shadowTransform = Matrix.CreateTranslation(GlobalPosition.X - spread, GlobalPosition.Y - spread, 0);
                context.DrawShadow(
                    new DropShadowEffect { Color = shadow.Color, Offset = shadow.Offset, BlurRadius = shadow.BlurRadius },
                    new Rectangle((int)MathF.Floor(GlobalPosition.X - spread), (int)MathF.Floor(GlobalPosition.Y - spread), (int)MathF.Ceiling(shadowSize.X), (int)MathF.Ceiling(shadowSize.Y)),
                    () => context.Drawing.FillPath(shadowPath, Color.White, shadowTransform));
            }
            if (BorderBrush != null && (left > 0 || top > 0 || right > 0 || bottom > 0))
            {
                var contours = new List<IReadOnlyList<Vector2>>();
                contours.AddRange(DrawingPathFlattener.Flatten(outer, Matrix.Identity, .25f));
                contours.AddRange(DrawingPathFlattener.Flatten(content, Matrix.CreateTranslation(left, top, 0), .25f));
                context.Drawing.FillPath(GeometryPaths.FromContours(contours), BorderBrush, Bounds, transform, FillRule.EvenOdd);
            }
            if (Background != null && width > 0 && height > 0)
            {
                context.Drawing.FillPath(content, Background, Bounds, Matrix.CreateTranslation(GlobalPosition.X + left, GlobalPosition.Y + top, 0));
            }
            foreach (var shadow in Shadows)
            {
                if (!shadow.Inset) continue;
                var spread = shadow.SpreadRadius;
                var insetSize = new Vector2(MathF.Max(0, Size.X - spread * 2), MathF.Max(0, Size.Y - spread * 2));
                var insetPath = GeometryPaths.Rectangle(insetSize, CornerRadius);
                var insetTransform = Matrix.CreateTranslation(GlobalPosition.X + spread, GlobalPosition.Y + spread, 0);
                context.DrawShadow(
                    new DropShadowEffect { Color = shadow.Color, Offset = shadow.Offset, BlurRadius = shadow.BlurRadius },
                    Bounds,
                    () => context.Drawing.FillPath(insetPath, Color.White, insetTransform),
                    outer,
                    transform,
                    true);
            }
            if (Children.Count > 0 && CornerRadius != default)
                context.DrawClipped(GeometryPaths.Rectangle(Size, CornerRadius), transform, Bounds, () => base.Draw(context));
            else base.Draw(context);
        }
    }

    public abstract class Inline
    {
        private UIFont _font;
        private Color? _foreground;
        private Color? _background;
        private string _language = string.Empty;
        private TextDirection _direction = TextDirection.Inherited;
        private TextDecoration _decoration;
        private float? _letterSpacing;
        public UIFont Font { get => _font; set { if (ReferenceEquals(_font, value)) return; _font = value; Invalidate(); } }
        public Color? Foreground { get => _foreground; set { if (_foreground == value) return; _foreground = value; Invalidate(); } }
        public Color? Background { get => _background; set { if (_background == value) return; _background = value; Invalidate(); } }
        public string Language { get => _language; set { value ??= string.Empty; if (_language == value) return; _language = value; Invalidate(); } }
        public TextDirection Direction { get => _direction; set { if (_direction == value) return; _direction = value; Invalidate(); } }
        public TextDecoration Decoration { get => _decoration; set { if (_decoration == value) return; _decoration = value; Invalidate(); } }
        public float? LetterSpacing
        {
            get => _letterSpacing;
            set
            {
                if (value.HasValue && !float.IsFinite(value.Value)) throw new ArgumentOutOfRangeException(nameof(value));
                if (_letterSpacing == value) return;
                _letterSpacing = value;
                Invalidate();
            }
        }
        internal event Action Changed;
        protected void Invalidate() => Changed?.Invoke();
        internal abstract void AppendText(StringBuilder builder);
    }

    public sealed class Run : Inline
    {
        private string _text = string.Empty;
        public Run() { }
        public Run(string text) => _text = text ?? string.Empty;
        public string Text
        {
            get => _text;
            set
            {
                value ??= string.Empty;
                if (_text == value) return;
                _text = value;
                Invalidate();
            }
        }
        internal override void AppendText(StringBuilder builder) => builder.Append(Text);
    }

    public sealed class LineBreak : Inline
    {
        internal override void AppendText(StringBuilder builder) => builder.Append('\n');
    }

    public sealed class InlineImage : Inline
    {
        private string _alternativeText = "\uFFFC";
        private Texture2D _source;
        private DrawingImage _vectorSource;
        private ScalableImageSource _scalableSource;
        private Vector2 _size;
        public Texture2D Source { get => _source; set { if (ReferenceEquals(_source, value)) return; _source = value; Invalidate(); } }
        public DrawingImage VectorSource { get => _vectorSource; set { if (ReferenceEquals(_vectorSource, value)) return; _vectorSource = value; Invalidate(); } }
        public ScalableImageSource ScalableSource { get => _scalableSource; set { if (ReferenceEquals(_scalableSource, value)) return; _scalableSource = value; Invalidate(); } }
        public Vector2 Size { get => _size; set { if (_size == value) return; _size = value; Invalidate(); } }
        public string AlternativeText
        {
            get => _alternativeText;
            set
            {
                value ??= string.Empty;
                if (_alternativeText == value) return;
                _alternativeText = value;
                Invalidate();
            }
        }
        internal override void AppendText(StringBuilder builder) => builder.Append(AlternativeText);
    }

    public sealed class Span : Inline
    {
        private readonly InlineCollection _inlines;
        public Span() => _inlines = new InlineCollection(Invalidate);
        public IList<Inline> Inlines => _inlines;
        internal override void AppendText(StringBuilder builder)
        {
            foreach (var inline in _inlines) inline.AppendText(builder);
        }
    }

    internal sealed class InlineCollection : Collection<Inline>
    {
        private readonly Action _changed;
        public InlineCollection(Action changed) => _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        protected override void InsertItem(int index, Inline item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            base.InsertItem(index, item);
            item.Changed += OnItemChanged;
            _changed();
        }
        protected override void SetItem(int index, Inline item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            this[index].Changed -= OnItemChanged;
            base.SetItem(index, item);
            item.Changed += OnItemChanged;
            _changed();
        }
        protected override void RemoveItem(int index)
        {
            this[index].Changed -= OnItemChanged;
            base.RemoveItem(index);
            _changed();
        }
        protected override void ClearItems()
        {
            foreach (var item in this) item.Changed -= OnItemChanged;
            base.ClearItems();
            _changed();
        }
        private void OnItemChanged() => _changed();
    }

    /// <summary>Template-free read-only text foundation backed by the retained text layout path.</summary>
    public class TextBlock : Label
    {
        private static readonly TextLayoutEngine InlineLayoutEngine = new TextLayoutEngine();
        internal static void ClearDetachedInlineLayoutCache() => InlineLayoutEngine.Clear();
        private readonly InlineCollection _inlines;
        private string _plainText = string.Empty;
        private float _letterSpacing;
        private float _lineHeight;
        private TextDecoration _decoration;
        public TextBlock() => _inlines = new InlineCollection(RebuildText);
        public new string Text
        {
            get => _plainText;
            set
            {
                value ??= string.Empty;
                if (_plainText == value) return;
                _plainText = value;
                RebuildText();
            }
        }
        public IList<Inline> Inlines => _inlines;
        public bool UsesInlineContent => _inlines.Count != 0;
        public float LetterSpacing
        {
            get => _letterSpacing;
            set
            {
                if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
                if (_letterSpacing == value) return;
                _letterSpacing = value;
                QueueLayout();
            }
        }
        public float LineHeight
        {
            get => _lineHeight;
            set
            {
                if (!float.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                if (_lineHeight == value) return;
                _lineHeight = value;
                QueueLayout();
            }
        }
        public TextDecoration Decoration
        {
            get => _decoration;
            set
            {
                if (_decoration == value) return;
                _decoration = value;
                QueueLayout();
            }
        }

        protected override float GetTextLineSpacing(UIFont font) => LineHeight > 0 && font != null ? LineHeight / font.Size : 1;
        protected override TextLayout AdjustTextLayout(TextLayout layout) => LetterSpacing == 0 ? layout : TextLayoutAdjuster.Apply(layout, LetterSpacing);

        public override Rectangle GetCharacterBounds(int position)
        {
            if (_inlines.Count == 0) return base.GetCharacterBounds(position);
            if (position < 0 || position >= base.Text.Length) return Rectangle.Empty;
            var layout = BuildInlineLayout(useAvailableWidth: true);
            var contentHeight = MathF.Max(0, Size.Y - Padding.Vertical);
            var lineCount = PrepareInlineLayoutForDisplay(layout, contentHeight);
            var offsetY = VerticalAlignment == VerticalAlignment.Center ? MathF.Max(0, (contentHeight - layout.Size.Y) * .5f)
                : VerticalAlignment == VerticalAlignment.Bottom ? MathF.Max(0, contentHeight - layout.Size.Y) : 0;
            for (var lineIndex = 0; lineIndex < lineCount; lineIndex++)
            {
                var line = layout.Lines[lineIndex];
                var offsetX = HorizontalAlignment == HorizontalAlignment.Center ? MathF.Max(0, (layout.AvailableWidth - line.Width) * .5f)
                    : HorizontalAlignment == HorizontalAlignment.Right ? MathF.Max(0, layout.AvailableWidth - line.Width) : 0;
                foreach (var box in line.Boxes)
                {
                    if (position < box.SourceStart || position >= box.SourceStart + box.SourceLength) continue;
                    var boxY = Padding.Top + offsetY + line.Y + MathF.Max(0, (line.Height - box.Size.Y) * .5f);
                    if (box.Image != null)
                        return new Rectangle((int)MathF.Round(Padding.Left + offsetX + box.X), (int)MathF.Round(boxY),
                            Math.Max(1, (int)MathF.Ceiling(box.Size.X)), Math.Max(1, (int)MathF.Ceiling(box.Size.Y)));
                    var localIndex = position - box.SourceStart;
                    foreach (var cluster in box.TextLayout.Clusters)
                    {
                        if (localIndex < cluster.Start || localIndex >= cluster.Start + cluster.Length) continue;
                        return new Rectangle(
                            (int)MathF.Floor(Padding.Left + offsetX + box.X + cluster.Bounds.X),
                            (int)MathF.Floor(boxY + cluster.Bounds.Y),
                            Math.Max(1, (int)MathF.Ceiling(cluster.Bounds.Width)),
                            Math.Max(1, (int)MathF.Ceiling(cluster.Bounds.Height)));
                    }
                    return Rectangle.Empty;
                }
            }
            return Rectangle.Empty;
        }

        public override Vector2 GetMinimumSize()
        {
            if (_inlines.Count == 0) return base.GetMinimumSize();
            var layout = BuildInlineLayout(useAvailableWidth: false);
            var width = AutowrapMode == LabelAutowrapMode.Off ? layout.Size.X : 0;
            return Vector2.Max(CustomMinimumSize, new Vector2(width + Padding.Horizontal, layout.Size.Y + Padding.Vertical));
        }

        protected override void DrawLabelText(UIRenderContext context)
        {
            if (_inlines.Count == 0)
            {
                base.DrawLabelText(context);
                DrawPlainTextDecorations(context);
                return;
            }
            var layout = BuildInlineLayout(useAvailableWidth: true);
            var contentHeight = MathF.Max(0, Size.Y - Padding.Vertical);
            var lineCount = PrepareInlineLayoutForDisplay(layout, contentHeight);
            var offsetY = VerticalAlignment == VerticalAlignment.Center ? MathF.Max(0, (contentHeight - layout.Size.Y) * .5f)
                : VerticalAlignment == VerticalAlignment.Bottom ? MathF.Max(0, contentHeight - layout.Size.Y) : 0;
            var origin = GlobalPosition + new Vector2(Padding.Left, Padding.Top + offsetY);
            for (var lineIndex = 0; lineIndex < lineCount; lineIndex++)
            {
                var line = layout.Lines[lineIndex];
                var offsetX = HorizontalAlignment == HorizontalAlignment.Center ? MathF.Max(0, (layout.AvailableWidth - line.Width) * .5f)
                    : HorizontalAlignment == HorizontalAlignment.Right ? MathF.Max(0, layout.AvailableWidth - line.Width) : 0;
                foreach (var box in line.Boxes)
                {
                    var position = origin + new Vector2(offsetX + box.X, line.Y + MathF.Max(0, (line.Height - box.Size.Y) * .5f));
                    var rectangle = new Rectangle((int)MathF.Round(position.X), (int)MathF.Round(position.Y), Math.Max(1, (int)MathF.Ceiling(box.Size.X)), Math.Max(1, (int)MathF.Ceiling(box.Size.Y)));
                    if (box.Style.Background.HasValue) context.Fill(rectangle, box.Style.Background.Value);
                    if (box.Image != null)
                    {
                        if (box.Image.Source != null) context.SpriteBatch.Draw(box.Image.Source, rectangle, Color.White);
                        else if (box.Image.VectorSource != null) box.Image.VectorSource.Render(context, rectangle);
                        else if (box.Image.ScalableSource != null) context.DrawScalableImage(box.Image.ScalableSource, rectangle, Color.White);
                    }
                    else
                    {
                        var color = Enabled ? box.Style.Foreground ?? FontColor ?? context.Theme.TextColor : context.Theme.DisabledTextColor;
                        context.Text(box.TextLayout, position, color);
                        DrawDecorations(context, box, position, color);
                    }
                }
            }
        }

        private int PrepareInlineLayoutForDisplay(InlineLayout layout, float contentHeight)
        {
            var maximum = MaxLinesVisible < 0 ? layout.Lines.Count : Math.Min(MaxLinesVisible, layout.Lines.Count);
            var visible = 0;
            while (visible < maximum && layout.Lines[visible].Y + layout.Lines[visible].Height <= contentHeight) visible++;
            if (visible == 0 || visible >= layout.Lines.Count || string.IsNullOrEmpty(EllipsisCharacter)) return visible;
            var line = layout.Lines[visible - 1];
            var style = line.Boxes.Count > 0
                ? line.Boxes[line.Boxes.Count - 1].Style
                : new InlineStyle(EffectiveUIFont, FontColor, null, Language, TextDirection, Decoration, LetterSpacing);
            if (style.Font == null) return visible;
            var options = new TextLayoutOptions(
                direction: style.Direction == TextDirection.Inherited ? TextDirection.Auto : style.Direction,
                lineSpacing: LineHeight > 0 ? LineHeight / style.Font.Size : 1,
                locale: style.Language);
            var ellipsisLayout = (Context?.TextLayoutEngine ?? InlineLayoutEngine).Layout(style.Font, EllipsisCharacter, options);
            if (style.LetterSpacing != 0) ellipsisLayout = TextLayoutAdjuster.Apply(ellipsisLayout, style.LetterSpacing);
            while (line.Boxes.Count > 0 && line.Width + ellipsisLayout.Size.X > layout.AvailableWidth)
            {
                var removed = line.Boxes[line.Boxes.Count - 1];
                if (removed.TextLayout != null && !string.IsNullOrEmpty(removed.Text))
                {
                    var boundaries = UnicodeGraphemeSegmenter.GetUtf16Boundaries(removed.Text);
                    var available = layout.AvailableWidth - removed.X - ellipsisLayout.Size.X;
                    InlineBox replacement = null;
                    for (var boundaryIndex = boundaries.Length - 2; boundaryIndex > 0; boundaryIndex--)
                    {
                        var candidate = removed.Text.Substring(0, boundaries[boundaryIndex]);
                        var candidateLayout = (Context?.TextLayoutEngine ?? InlineLayoutEngine).Layout(removed.Style.Font, candidate, options);
                        if (removed.Style.LetterSpacing != 0) candidateLayout = TextLayoutAdjuster.Apply(candidateLayout, removed.Style.LetterSpacing);
                        if (candidateLayout.Size.X > available) continue;
                        replacement = new InlineBox(candidate, candidateLayout, removed.Style, removed.SourceStart);
                        replacement.X = removed.X;
                        break;
                    }
                    if (replacement != null)
                    {
                        line.Boxes[line.Boxes.Count - 1] = replacement;
                        line.Width = replacement.X + replacement.Size.X;
                        break;
                    }
                }
                line.Boxes.RemoveAt(line.Boxes.Count - 1);
                line.Width = removed.X;
            }
            var ellipsis = new InlineBox(EllipsisCharacter, ellipsisLayout, style, base.Text.Length);
            ellipsis.X = line.Width;
            line.Boxes.Add(ellipsis);
            line.Width += ellipsis.Size.X;
            line.Height = MathF.Max(line.Height, ellipsis.Size.Y);
            return visible;
        }

        private static void DrawDecorations(UIRenderContext context, InlineBox box, Vector2 position, Color color)
        {
            if (box.Style.Decoration == TextDecoration.None || box.TextLayout.Lines.Count == 0) return;
            var line = box.TextLayout.Lines[0];
            var thickness = Math.Max(1, (int)MathF.Round(box.Style.Font.Size / 14f));
            var left = (int)MathF.Round(position.X + line.Origin.X);
            var width = Math.Max(1, (int)MathF.Ceiling(line.Size.X));
            if ((box.Style.Decoration & TextDecoration.Underline) != 0)
            {
                var y = (int)MathF.Round(position.Y + line.Origin.Y + line.Baseline + thickness);
                context.Fill(new Rectangle(left, y, width, thickness), color);
            }
            if ((box.Style.Decoration & TextDecoration.Strikethrough) != 0)
            {
                var y = (int)MathF.Round(position.Y + line.Origin.Y + line.Baseline * .55f);
                context.Fill(new Rectangle(left, y, width, thickness), color);
            }
        }

        private void DrawPlainTextDecorations(UIRenderContext context)
        {
            if (Decoration == TextDecoration.None || string.IsNullOrEmpty(base.Text)) return;
            var color = Enabled ? FontColor ?? context.Theme.TextColor : context.Theme.DisabledTextColor;
            var rows = new SortedDictionary<int, Rectangle>();
            for (var index = 0; index < base.Text.Length; index++)
            {
                var bounds = GetCharacterBounds(index);
                if (bounds == Rectangle.Empty) continue;
                if (rows.TryGetValue(bounds.Y, out var row)) rows[bounds.Y] = Rectangle.Union(row, bounds);
                else rows.Add(bounds.Y, bounds);
            }
            var thickness = Math.Max(1, (int)MathF.Round((EffectiveUIFont?.Size ?? 16) / 14f));
            foreach (var row in rows.Values)
            {
                var left = Bounds.X + row.Left;
                var width = Math.Max(1, row.Width);
                if ((Decoration & TextDecoration.Underline) != 0)
                    context.Fill(new Rectangle(left, Bounds.Y + row.Bottom - thickness, width, thickness), color);
                if ((Decoration & TextDecoration.Strikethrough) != 0)
                    context.Fill(new Rectangle(left, Bounds.Y + row.Y + row.Height / 2, width, thickness), color);
            }
        }

        private void RebuildText()
        {
            if (_inlines.Count == 0)
            {
                base.Text = _plainText;
                return;
            }
            var builder = new StringBuilder();
            foreach (var inline in _inlines) inline.AppendText(builder);
            base.Text = builder.ToString();
        }

        private InlineLayout BuildInlineLayout(bool useAvailableWidth)
        {
            var defaultFont = EffectiveUIFont;
            var availableWidth = useAvailableWidth && Size.X > Padding.Horizontal ? Size.X - Padding.Horizontal : float.PositiveInfinity;
            var layout = new InlineLayout(availableWidth);
            var style = new InlineStyle(defaultFont, FontColor, null, Language, TextDirection, Decoration, LetterSpacing);
            var sourceOffset = 0;
            foreach (var inline in _inlines) AppendInline(layout, inline, style, ref sourceOffset);
            layout.FinishLine(GetInlineLineHeight(defaultFont));
            return layout;
        }

        private void AppendInline(InlineLayout target, Inline inline, InlineStyle inherited, ref int sourceOffset)
        {
            var style = inherited.With(inline);
            if (inline is Span span)
            {
                foreach (var child in span.Inlines) AppendInline(target, child, style, ref sourceOffset);
                return;
            }
            if (inline is LineBreak)
            {
                target.FinishLine(GetInlineLineHeight(style.Font));
                sourceOffset++;
                return;
            }
            if (inline is InlineImage image)
            {
                var size = image.Size;
                if (size.X <= 0 || size.Y <= 0)
                    size = image.Source != null ? new Vector2(image.Source.Width, image.Source.Height) : image.VectorSource?.IntrinsicSize ?? image.ScalableSource?.IntrinsicSize ?? Vector2.Zero;
                if (size.X > 0 && size.Y > 0) target.Add(new InlineBox(image, size, style, sourceOffset, image.AlternativeText.Length), AutowrapMode != LabelAutowrapMode.Off);
                sourceOffset += image.AlternativeText.Length;
                return;
            }
            if (inline is not Run run || string.IsNullOrEmpty(run.Text)) return;
            foreach (var text in SplitInlineText(run.Text))
            {
                if (text == "\n") { target.FinishLine(GetInlineLineHeight(style.Font)); sourceOffset++; continue; }
                if (style.Font == null) { sourceOffset += text.Length; continue; }
                var options = new TextLayoutOptions(
                    direction: style.Direction == TextDirection.Inherited ? TextDirection.Auto : style.Direction,
                    lineSpacing: LineHeight > 0 ? LineHeight / style.Font.Size : 1,
                    locale: style.Language);
                var textLayout = (Context?.TextLayoutEngine ?? InlineLayoutEngine).Layout(style.Font, text, options);
                if (style.LetterSpacing != 0) textLayout = TextLayoutAdjuster.Apply(textLayout, style.LetterSpacing);
                target.Add(new InlineBox(text, textLayout, style, sourceOffset), AutowrapMode != LabelAutowrapMode.Off);
                sourceOffset += text.Length;
            }
        }

        private float GetInlineLineHeight(UIFont font) => LineHeight > 0 ? LineHeight : font?.Size ?? 16;

        private IEnumerable<string> SplitInlineText(string text)
        {
            if (AutowrapMode == LabelAutowrapMode.Off) return text.Split(new[] { "\n" }, StringSplitOptions.None).SelectMany((part, index) => index == 0 ? new[] { part } : new[] { "\n", part });
            var parts = new List<string>();
            if (AutowrapMode == LabelAutowrapMode.Arbitrary)
            {
                var boundaries = UnicodeGraphemeSegmenter.GetUtf16Boundaries(text);
                for (var index = 0; index + 1 < boundaries.Length; index++) parts.Add(text.Substring(boundaries[index], boundaries[index + 1] - boundaries[index]));
                return parts;
            }
            var start = 0;
            for (var index = 0; index < text.Length; index++)
            {
                if (text[index] != ' ' && text[index] != '\n') continue;
                if (index > start) parts.Add(text.Substring(start, index - start));
                parts.Add(text[index].ToString());
                start = index + 1;
            }
            if (start < text.Length) parts.Add(text.Substring(start));
            return parts;
        }

        private readonly struct InlineStyle
        {
            public InlineStyle(UIFont font, Color? foreground, Color? background, string language, TextDirection direction, TextDecoration decoration, float letterSpacing)
            {
                Font = font; Foreground = foreground; Background = background; Language = language ?? string.Empty; Direction = direction; Decoration = decoration; LetterSpacing = letterSpacing;
            }
            public UIFont Font { get; }
            public Color? Foreground { get; }
            public Color? Background { get; }
            public string Language { get; }
            public TextDirection Direction { get; }
            public TextDecoration Decoration { get; }
            public float LetterSpacing { get; }
            public InlineStyle With(Inline inline) => new InlineStyle(
                inline.Font ?? Font,
                inline.Foreground ?? Foreground,
                inline.Background ?? Background,
                string.IsNullOrEmpty(inline.Language) ? Language : inline.Language,
                inline.Direction == TextDirection.Inherited ? Direction : inline.Direction,
                Decoration | inline.Decoration,
                inline.LetterSpacing ?? LetterSpacing);
        }

        private sealed class InlineBox
        {
            public InlineBox(string text, TextLayout textLayout, InlineStyle style, int sourceStart) { Text = text; TextLayout = textLayout; Size = textLayout.Size; Style = style; SourceStart = sourceStart; SourceLength = text.Length; }
            public InlineBox(InlineImage image, Vector2 size, InlineStyle style, int sourceStart, int sourceLength) { Image = image; Size = size; Style = style; SourceStart = sourceStart; SourceLength = sourceLength; }
            public string Text { get; }
            public TextLayout TextLayout { get; }
            public InlineImage Image { get; }
            public Vector2 Size { get; }
            public InlineStyle Style { get; }
            public int SourceStart { get; }
            public int SourceLength { get; }
            public float X { get; set; }
        }

        private sealed class InlineLine
        {
            public List<InlineBox> Boxes { get; } = new List<InlineBox>();
            public float Y { get; set; }
            public float Width { get; set; }
            public float Height { get; set; }
        }

        private sealed class InlineLayout
        {
            private InlineLine _line = new InlineLine();
            public InlineLayout(float availableWidth) => AvailableWidth = availableWidth;
            public List<InlineLine> Lines { get; } = new List<InlineLine>();
            public float AvailableWidth { get; }
            public Vector2 Size { get; private set; }
            public void Add(InlineBox box, bool wrap)
            {
                if (wrap && _line.Boxes.Count > 0 && _line.Width + box.Size.X > AvailableWidth) FinishLine(box.Size.Y);
                box.X = _line.Width;
                _line.Boxes.Add(box);
                _line.Width += box.Size.X;
                _line.Height = MathF.Max(_line.Height, box.Size.Y);
            }
            public void FinishLine(float fallbackHeight)
            {
                if (_line.Boxes.Count == 0 && Lines.Count > 0) _line.Height = fallbackHeight;
                if (_line.Boxes.Count == 0 && Lines.Count == 0 && fallbackHeight <= 0) return;
                _line.Height = MathF.Max(_line.Height, fallbackHeight);
                _line.Y = Size.Y;
                Lines.Add(_line);
                Size = new Vector2(MathF.Max(Size.X, _line.Width), Size.Y + _line.Height);
                _line = new InlineLine();
            }
        }
    }

    /// <summary>Template-free bitmap image foundation.</summary>
    public class Image : TextureRect
    {
        private DrawingImage _vectorSource;
        private ScalableImageSource _scalableSource;
        private float _imageOpacity = 1;
        public DrawingImage VectorSource
        {
            get => _vectorSource;
            set
            {
                if (ReferenceEquals(_vectorSource, value)) return;
                _vectorSource = value;
                QueueLayout();
            }
        }
        public ScalableImageSource ScalableSource
        {
            get => _scalableSource;
            set
            {
                if (ReferenceEquals(_scalableSource, value)) return;
                _scalableSource = value;
                QueueLayout();
            }
        }
        public Rectangle? SourceRectangle { get; set; }
        public ImageStretch Stretch { get; set; } = ImageStretch.Contain;
        public ImageTileMode TileMode { get; set; }
        public ImageSamplingMode SamplingMode { get; set; } = ImageSamplingMode.Linear;
        public Color Tint { get; set; } = Color.White;
        public float ImageOpacity
        {
            get => _imageOpacity;
            set
            {
                if (!float.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value));
                _imageOpacity = value;
            }
        }
        public HorizontalAlignment ImageHorizontalAlignment { get; set; } = HorizontalAlignment.Center;
        public VerticalAlignment ImageVerticalAlignment { get; set; } = VerticalAlignment.Center;

        public override Vector2 GetMinimumSize()
        {
            var intrinsic = GetIntrinsicSize();
            return intrinsic == Vector2.Zero ? CustomMinimumSize : Vector2.Max(CustomMinimumSize, GetTextureMinimumSize(intrinsic));
        }

        internal override void Draw(UIRenderContext context)
        {
            var intrinsic = GetIntrinsicSize();
            if (intrinsic.X > 0 && intrinsic.Y > 0)
            {
                var layout = GetImageLayout(intrinsic);
                context.Drawing.Save();
                try
                {
                    context.Drawing.Clip(GeometryPaths.Rectangle(Size, default), Matrix.CreateTranslation(GlobalPosition.X, GlobalPosition.Y, 0));
                    foreach (var destination in GetTileDestinations(layout.Destination)) DrawSource(context, destination);
                }
                finally { context.Drawing.Restore(); }
            }
            DrawChildControls(context);
        }

        public ImageLayout GetImageLayout(Vector2 intrinsic)
        {
            if (intrinsic.X <= 0 || intrinsic.Y <= 0 || Size.X <= 0 || Size.Y <= 0) return default;
            var scaleX = 1f;
            var scaleY = 1f;
            if (Stretch == ImageStretch.Fill)
            {
                scaleX = Size.X / intrinsic.X;
                scaleY = Size.Y / intrinsic.Y;
            }
            else if (Stretch is ImageStretch.Contain or ImageStretch.Cover or ImageStretch.ScaleDown)
            {
                var scale = Stretch == ImageStretch.Cover
                    ? MathF.Max(Size.X / intrinsic.X, Size.Y / intrinsic.Y)
                    : MathF.Min(Size.X / intrinsic.X, Size.Y / intrinsic.Y);
                if (Stretch == ImageStretch.ScaleDown) scale = MathF.Min(1, scale);
                scaleX = scaleY = scale;
            }
            var width = Math.Max(1, (int)MathF.Round(intrinsic.X * scaleX));
            var height = Math.Max(1, (int)MathF.Round(intrinsic.Y * scaleY));
            var x = ImageHorizontalAlignment == HorizontalAlignment.Left ? 0
                : ImageHorizontalAlignment == HorizontalAlignment.Right ? (int)MathF.Round(Size.X) - width
                : ((int)MathF.Round(Size.X) - width) / 2;
            var y = ImageVerticalAlignment == VerticalAlignment.Top ? 0
                : ImageVerticalAlignment == VerticalAlignment.Bottom ? (int)MathF.Round(Size.Y) - height
                : ((int)MathF.Round(Size.Y) - height) / 2;
            return new ImageLayout(new Rectangle((int)MathF.Round(GlobalPosition.X) + x, (int)MathF.Round(GlobalPosition.Y) + y, width, height));
        }

        private Vector2 GetIntrinsicSize()
        {
            if (Texture != null)
            {
                var source = GetBitmapSourceRectangle();
                return new Vector2(source.Width, source.Height);
            }
            return VectorSource?.IntrinsicSize ?? ScalableSource?.IntrinsicSize ?? Vector2.Zero;
        }

        private Rectangle GetBitmapSourceRectangle()
        {
            if (Texture == null) return Rectangle.Empty;
            var bounds = new Rectangle(0, 0, Texture.Width, Texture.Height);
            return SourceRectangle.HasValue ? Rectangle.Intersect(bounds, SourceRectangle.Value) : bounds;
        }

        private IEnumerable<Rectangle> GetTileDestinations(Rectangle placement)
        {
            var tileX = TileMode is ImageTileMode.TileX or ImageTileMode.Tile;
            var tileY = TileMode is ImageTileMode.TileY or ImageTileMode.Tile;
            var startX = tileX ? Bounds.Left : placement.X;
            var startY = tileY ? Bounds.Top : placement.Y;
            var endX = tileX ? Bounds.Right : placement.Right;
            var endY = tileY ? Bounds.Bottom : placement.Bottom;
            for (var y = startY; y < endY; y += placement.Height)
            for (var x = startX; x < endX; x += placement.Width)
                yield return new Rectangle(x, y, placement.Width, placement.Height);
        }

        private void DrawSource(UIRenderContext context, Rectangle destination)
        {
            if (Texture != null)
            {
                context.Drawing.DrawImage(
                    Texture,
                    GetBitmapSourceRectangle(),
                    new Rectangle(0, 0, destination.Width, destination.Height),
                    Matrix.CreateTranslation(destination.X, destination.Y, 0),
                    Tint * ImageOpacity,
                    SamplingMode);
            }
            else if (VectorSource != null) VectorSource.Render(context, destination, Tint * ImageOpacity);
            else if (ScalableSource != null) context.DrawScalableImage(ScalableSource, destination, Tint * ImageOpacity, SamplingMode);
        }
    }

    public readonly struct ImageLayout
    {
        public ImageLayout(Rectangle destination) => Destination = destination;
        public Rectangle Destination { get; }
    }

    /// <summary>Template-free nine-slice bitmap foundation.</summary>
    public class NineSliceImage : NinePatchRect { }

    /// <summary>Template-free theme icon foundation.</summary>
    public class ThemeIconView : ThemeIconRect { }

    public readonly struct GradientStop
    {
        public GradientStop(float offset, Color color)
        {
            if (!float.IsFinite(offset) || offset < 0 || offset > 1) throw new ArgumentOutOfRangeException(nameof(offset));
            Offset = offset;
            Color = color;
        }

        public float Offset { get; }
        public Color Color { get; }
    }

    public abstract class FreezableResource
    {
        public bool IsFrozen { get; private set; }

        public void Freeze()
        {
            if (IsFrozen) return;
            FreezeCore();
            IsFrozen = true;
        }

        protected virtual void FreezeCore() { }

        protected void ThrowIfFrozen()
        {
            if (IsFrozen) throw new InvalidOperationException($"A frozen {GetType().Name} cannot be changed.");
        }
    }

    internal sealed class FreezableCollection<T> : Collection<T> where T : FreezableResource
    {
        private readonly Action _throwIfFrozen;
        public FreezableCollection(Action throwIfFrozen) => _throwIfFrozen = throwIfFrozen;
        protected override void InsertItem(int index, T item)
        {
            _throwIfFrozen();
            if (item == null) throw new ArgumentNullException(nameof(item));
            base.InsertItem(index, item);
        }
        protected override void SetItem(int index, T item)
        {
            _throwIfFrozen();
            if (item == null) throw new ArgumentNullException(nameof(item));
            base.SetItem(index, item);
        }
        protected override void RemoveItem(int index) { _throwIfFrozen(); base.RemoveItem(index); }
        protected override void ClearItems() { _throwIfFrozen(); base.ClearItems(); }
    }

    public abstract class Transform : FreezableResource
    {
        public abstract Matrix Value { get; }
    }

    public sealed class TranslateTransform : Transform
    {
        private float _x;
        private float _y;
        public float X { get => _x; set { ThrowIfFrozen(); _x = value; } }
        public float Y { get => _y; set { ThrowIfFrozen(); _y = value; } }
        public override Matrix Value => Matrix.CreateTranslation(X, Y, 0);
    }

    public sealed class ScaleTransform : Transform
    {
        private float _scaleX = 1;
        private float _scaleY = 1;
        public float ScaleX { get => _scaleX; set { ThrowIfFrozen(); _scaleX = value; } }
        public float ScaleY { get => _scaleY; set { ThrowIfFrozen(); _scaleY = value; } }
        public override Matrix Value => Matrix.CreateScale(ScaleX, ScaleY, 1);
    }

    public sealed class RotateTransform : Transform
    {
        private float _angle;
        public float Angle { get => _angle; set { ThrowIfFrozen(); _angle = value; } }
        public override Matrix Value => Matrix.CreateRotationZ(Angle * MathF.PI / 180f);
    }

    public sealed class SkewTransform : Transform
    {
        private float _angleX;
        private float _angleY;
        public float AngleX { get => _angleX; set { ThrowIfFrozen(); _angleX = value; } }
        public float AngleY { get => _angleY; set { ThrowIfFrozen(); _angleY = value; } }
        public override Matrix Value => new Matrix(
            1, MathF.Tan(AngleY * MathF.PI / 180f), 0, 0,
            MathF.Tan(AngleX * MathF.PI / 180f), 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1);
    }

    public sealed class MatrixTransform : Transform
    {
        private Matrix _matrix = Matrix.Identity;
        public Matrix Matrix { get => _matrix; set { ThrowIfFrozen(); _matrix = value; } }
        public override Matrix Value => Matrix;
    }

    public sealed class TransformGroup : Transform
    {
        private readonly FreezableCollection<Transform> _children;
        public TransformGroup() => _children = new FreezableCollection<Transform>(ThrowIfFrozen);
        public IList<Transform> Children => _children;
        public override Matrix Value
        {
            get
            {
                var result = Matrix.Identity;
                foreach (var child in _children) result *= child?.Value ?? Matrix.Identity;
                return result;
            }
        }
        protected override void FreezeCore()
        {
            foreach (var child in _children) child.Freeze();
        }
    }

    public abstract class Brush
    {
        private float _opacity = 1;
        public float Opacity
        {
            get => _opacity;
            set
            {
                if (!float.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value));
                _opacity = value;
            }
        }
        public Transform Transform { get; set; }
        internal Color Sample(Vector2 point, Rectangle bounds)
        {
            if (Transform != null) point = Vector2.Transform(point, Matrix.Invert(Transform.Value));
            return SampleCore(point, bounds) * Opacity;
        }
        protected abstract Color SampleCore(Vector2 point, Rectangle bounds);
    }

    public sealed class SolidColorBrush : Brush
    {
        public SolidColorBrush() { }
        public SolidColorBrush(Color color) => Color = color;
        public Color Color { get; set; } = Color.White;
        protected override Color SampleCore(Vector2 point, Rectangle bounds) => Color;
    }

    public abstract class GradientBrush : Brush
    {
        private IReadOnlyList<GradientStop> _gradientStops = new[] { new GradientStop(0, Color.Transparent), new GradientStop(1, Color.White) };
        public IReadOnlyList<GradientStop> GradientStops
        {
            get => _gradientStops;
            set
            {
                if (value == null || value.Count == 0) throw new ArgumentException("A gradient requires at least one stop.", nameof(value));
                var stops = new List<GradientStop>(value);
                stops.Sort((left, right) => left.Offset.CompareTo(right.Offset));
                _gradientStops = stops.AsReadOnly();
            }
        }
        public GradientSpreadMethod SpreadMethod { get; set; }
        public GradientInterpolationSpace InterpolationSpace { get; set; }

        protected Color SampleStops(float amount)
        {
            amount = Spread(amount);
            if (amount <= GradientStops[0].Offset) return GradientStops[0].Color;
            for (var index = 1; index < GradientStops.Count; index++)
            {
                var next = GradientStops[index];
                if (amount > next.Offset) continue;
                var previous = GradientStops[index - 1];
                var range = next.Offset - previous.Offset;
                var interpolation = range <= float.Epsilon ? 1 : (amount - previous.Offset) / range;
                return InterpolationSpace == GradientInterpolationSpace.LinearSrgb
                    ? InterpolateLinear(previous.Color, next.Color, interpolation)
                    : Color.Lerp(previous.Color, next.Color, interpolation);
            }
            return GradientStops[GradientStops.Count - 1].Color;
        }

        private float Spread(float amount)
        {
            if (SpreadMethod == GradientSpreadMethod.Pad) return MathHelper.Clamp(amount, 0, 1);
            var interval = MathF.Floor(amount);
            amount -= interval;
            return SpreadMethod == GradientSpreadMethod.Reflect && ((int)interval & 1) != 0 ? 1 - amount : amount;
        }

        private static Color InterpolateLinear(Color start, Color end, float amount)
        {
            static float ToLinear(byte value)
            {
                var channel = value / 255f;
                return channel <= .04045f ? channel / 12.92f : MathF.Pow((channel + .055f) / 1.055f, 2.4f);
            }
            static float ToSrgb(float value) => value <= .0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1f / 2.4f) - .055f;
            static float Interpolate(float start, float end, float amount) => start + (end - start) * amount;
            return new Color(
                MathHelper.Clamp(ToSrgb(Interpolate(ToLinear(start.R), ToLinear(end.R), amount)), 0, 1),
                MathHelper.Clamp(ToSrgb(Interpolate(ToLinear(start.G), ToLinear(end.G), amount)), 0, 1),
                MathHelper.Clamp(ToSrgb(Interpolate(ToLinear(start.B), ToLinear(end.B), amount)), 0, 1),
                Interpolate(start.A / 255f, end.A / 255f, amount));
        }
    }

    public sealed class LinearGradientBrush : GradientBrush
    {
        public Vector2 StartPoint { get; set; }
        public Vector2 EndPoint { get; set; } = Vector2.UnitX;
        public bool RelativeCoordinates { get; set; } = true;
        protected override Color SampleCore(Vector2 point, Rectangle bounds)
        {
            var start = RelativeCoordinates ? new Vector2(bounds.X + StartPoint.X * bounds.Width, bounds.Y + StartPoint.Y * bounds.Height) : StartPoint;
            var end = RelativeCoordinates ? new Vector2(bounds.X + EndPoint.X * bounds.Width, bounds.Y + EndPoint.Y * bounds.Height) : EndPoint;
            var axis = end - start;
            return SampleStops(axis.LengthSquared() <= float.Epsilon ? 0 : Vector2.Dot(point - start, axis) / axis.LengthSquared());
        }
    }

    public sealed class RadialGradientBrush : GradientBrush
    {
        public Vector2 Center { get; set; } = new Vector2(.5f);
        public float Radius { get; set; } = .5f;
        public bool RelativeCoordinates { get; set; } = true;
        protected override Color SampleCore(Vector2 point, Rectangle bounds)
        {
            if (!float.IsFinite(Radius) || Radius <= 0) return SampleStops(1);
            var center = RelativeCoordinates ? new Vector2(bounds.X + Center.X * bounds.Width, bounds.Y + Center.Y * bounds.Height) : Center;
            var radius = RelativeCoordinates ? Radius * MathF.Max(bounds.Width, bounds.Height) : Radius;
            return SampleStops(Vector2.Distance(point, center) / MathF.Max(float.Epsilon, radius));
        }
    }

    public sealed class ConicGradientBrush : GradientBrush
    {
        public Vector2 Center { get; set; } = new Vector2(.5f);
        public float StartAngle { get; set; }
        public bool RelativeCoordinates { get; set; } = true;
        protected override Color SampleCore(Vector2 point, Rectangle bounds)
        {
            var center = RelativeCoordinates ? new Vector2(bounds.X + Center.X * bounds.Width, bounds.Y + Center.Y * bounds.Height) : Center;
            var amount = (MathF.Atan2(point.Y - center.Y, point.X - center.X) - StartAngle * MathF.PI / 180f) / MathHelper.TwoPi;
            return SampleStops(amount - MathF.Floor(amount));
        }
    }

    public sealed class ImageBrush : Brush
    {
        public Texture2D Source { get; set; }
        public ImageStretch Stretch { get; set; } = ImageStretch.Fill;
        public ImageTileMode TileMode { get; set; }
        public ImageSamplingMode SamplingMode { get; set; } = ImageSamplingMode.Linear;
        public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Center;
        public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Center;
        public Color Tint { get; set; } = Color.White;

        internal Rectangle GetPlacement(Rectangle bounds)
        {
            if (Source == null) return Rectangle.Empty;
            var sourceWidth = Source.Width;
            var sourceHeight = Source.Height;
            var width = bounds.Width;
            var height = bounds.Height;
            switch (Stretch)
            {
                case ImageStretch.None:
                    width = sourceWidth;
                    height = sourceHeight;
                    break;
                case ImageStretch.Contain:
                    ScaleUniform(bounds, sourceWidth, sourceHeight, false, out width, out height);
                    break;
                case ImageStretch.Cover:
                    ScaleUniform(bounds, sourceWidth, sourceHeight, true, out width, out height);
                    break;
                case ImageStretch.ScaleDown:
                    if (sourceWidth <= bounds.Width && sourceHeight <= bounds.Height)
                    {
                        width = sourceWidth;
                        height = sourceHeight;
                    }
                    else ScaleUniform(bounds, sourceWidth, sourceHeight, false, out width, out height);
                    break;
            }
            var x = HorizontalAlignment == HorizontalAlignment.Left ? bounds.X
                : HorizontalAlignment == HorizontalAlignment.Right ? bounds.Right - width
                : bounds.X + (bounds.Width - width) / 2;
            var y = VerticalAlignment == VerticalAlignment.Top ? bounds.Y
                : VerticalAlignment == VerticalAlignment.Bottom ? bounds.Bottom - height
                : bounds.Y + (bounds.Height - height) / 2;
            return new Rectangle(x, y, Math.Max(0, width), Math.Max(0, height));
        }

        internal IReadOnlyList<Vector2> GetPaintBounds(Rectangle bounds, Rectangle placement)
        {
            var tileX = TileMode is ImageTileMode.TileX or ImageTileMode.Tile;
            var tileY = TileMode is ImageTileMode.TileY or ImageTileMode.Tile;
            var paint = new Rectangle(
                tileX ? bounds.X : placement.X,
                tileY ? bounds.Y : placement.Y,
                tileX ? bounds.Width : placement.Width,
                tileY ? bounds.Height : placement.Height);
            var transform = Transform?.Value ?? Matrix.Identity;
            return new[]
            {
                Vector2.Transform(new Vector2(paint.Left, paint.Top), transform),
                Vector2.Transform(new Vector2(paint.Right, paint.Top), transform),
                Vector2.Transform(new Vector2(paint.Right, paint.Bottom), transform),
                Vector2.Transform(new Vector2(paint.Left, paint.Bottom), transform),
            };
        }

        internal Vector2 ToBrushSpace(Vector2 point) => Transform == null ? point : Vector2.Transform(point, Matrix.Invert(Transform.Value));

        internal Vector2 GetTextureCoordinate(Vector2 point, Rectangle placement) => new Vector2(
            (point.X - placement.X) / Math.Max(1f, placement.Width),
            (point.Y - placement.Y) / Math.Max(1f, placement.Height));

        protected override Color SampleCore(Vector2 point, Rectangle bounds) => Tint;

        private static void ScaleUniform(Rectangle bounds, int sourceWidth, int sourceHeight, bool cover, out int width, out int height)
        {
            var scale = cover
                ? MathF.Max(bounds.Width / (float)sourceWidth, bounds.Height / (float)sourceHeight)
                : MathF.Min(bounds.Width / (float)sourceWidth, bounds.Height / (float)sourceHeight);
            width = Math.Max(1, (int)MathF.Round(sourceWidth * scale));
            height = Math.Max(1, (int)MathF.Round(sourceHeight * scale));
        }
    }

    public abstract class Geometry : FreezableResource
    {
        private Transform _transform;
        private FillRule _fillRule;
        public Transform Transform { get => _transform; set { ThrowIfFrozen(); _transform = value; } }
        public FillRule FillRule { get => _fillRule; set { ThrowIfFrozen(); _fillRule = value; } }
        internal DrawingPath CreatePath(Vector2 size)
        {
            var path = CreatePathCore(size);
            return Transform == null ? path : GeometryPaths.FromContours(DrawingPathFlattener.Flatten(path, Transform.Value, .25f));
        }
        internal abstract DrawingPath CreatePathCore(Vector2 size);
        protected override void FreezeCore() => _transform?.Freeze();
    }

    public sealed class RectangleGeometry : Geometry
    {
        private float _radiusX;
        private float _radiusY;
        private CornerRadius _cornerRadius;
        public float RadiusX { get => _radiusX; set { ThrowIfFrozen(); _radiusX = value; } }
        public float RadiusY { get => _radiusY; set { ThrowIfFrozen(); _radiusY = value; } }
        public CornerRadius CornerRadius { get => _cornerRadius; set { ThrowIfFrozen(); _cornerRadius = value; } }
        internal override DrawingPath CreatePathCore(Vector2 size) => GeometryPaths.Rectangle(size, CornerRadius == default ? new CornerRadius(MathF.Max(RadiusX, RadiusY)) : CornerRadius);
    }

    public sealed class EllipseGeometry : Geometry
    {
        internal override DrawingPath CreatePathCore(Vector2 size) => GeometryPaths.Ellipse(size);
    }

    public sealed class LineGeometry : Geometry
    {
        private Vector2 _startPoint;
        private Vector2 _endPoint;
        public Vector2 StartPoint { get => _startPoint; set { ThrowIfFrozen(); _startPoint = value; } }
        public Vector2 EndPoint { get => _endPoint; set { ThrowIfFrozen(); _endPoint = value; } }
        internal override DrawingPath CreatePathCore(Vector2 size) => new DrawingPath().MoveTo(StartPoint).LineTo(EndPoint);
    }

    public sealed class PathGeometry : Geometry
    {
        private string _data;
        private DrawingPath _path;
        public PathGeometry() { }
        public PathGeometry(DrawingPath path) => _path = path;
        public DrawingPath Path { get => _path; set { ThrowIfFrozen(); _path = value; } }
        public string Data
        {
            get => _data;
            set
            {
                ThrowIfFrozen();
                Path = string.IsNullOrWhiteSpace(value) ? new DrawingPath() : DrawingPath.Parse(value);
                _data = value;
            }
        }
        internal override DrawingPath CreatePathCore(Vector2 size) => Path ?? new DrawingPath();
        protected override void FreezeCore()
        {
            base.FreezeCore();
            _path?.Freeze();
        }
    }

    public sealed class GeometryGroup : Geometry
    {
        private readonly FreezableCollection<Geometry> _children;
        public GeometryGroup() => _children = new FreezableCollection<Geometry>(ThrowIfFrozen);
        public IList<Geometry> Children => _children;
        internal override DrawingPath CreatePathCore(Vector2 size)
        {
            var contours = new List<IReadOnlyList<Vector2>>();
            foreach (var child in _children)
                if (child != null) contours.AddRange(DrawingPathFlattener.Flatten(child.CreatePath(size), Matrix.Identity, .25f));
            return GeometryPaths.FromContours(contours);
        }
        protected override void FreezeCore()
        {
            base.FreezeCore();
            foreach (var child in _children) child.Freeze();
        }
    }

    public sealed class CombinedGeometry : Geometry
    {
        private Geometry _geometry1;
        private Geometry _geometry2;
        private GeometryCombineMode _mode;
        public Geometry Geometry1 { get => _geometry1; set { ThrowIfFrozen(); _geometry1 = value; } }
        public Geometry Geometry2 { get => _geometry2; set { ThrowIfFrozen(); _geometry2 = value; } }
        public GeometryCombineMode Mode { get => _mode; set { ThrowIfFrozen(); _mode = value; } }
        internal override DrawingPath CreatePathCore(Vector2 size)
        {
            var first = GeometryClipper.ToPaths(Geometry1 == null ? Array.Empty<IReadOnlyList<Vector2>>() : DrawingPathFlattener.Flatten(Geometry1.CreatePath(size), Matrix.Identity, .25f));
            var second = GeometryClipper.ToPaths(Geometry2 == null ? Array.Empty<IReadOnlyList<Vector2>>() : DrawingPathFlattener.Flatten(Geometry2.CreatePath(size), Matrix.Identity, .25f));
            var rule = FillRule == FillRule.EvenOdd ? ClipperFillRule.EvenOdd : ClipperFillRule.NonZero;
            var result = Mode switch
            {
                GeometryCombineMode.Intersect => Clipper.Intersect(first, second, rule, GeometryClipper.DecimalPrecision),
                GeometryCombineMode.Exclude => Clipper.Difference(first, second, rule, GeometryClipper.DecimalPrecision),
                GeometryCombineMode.Xor => Clipper.Xor(first, second, rule, GeometryClipper.DecimalPrecision),
                _ => Clipper.Union(first, second, rule, GeometryClipper.DecimalPrecision),
            };
            return GeometryClipper.ToDrawingPath(result);
        }
        protected override void FreezeCore()
        {
            base.FreezeCore();
            _geometry1?.Freeze();
            _geometry2?.Freeze();
        }
    }

    public abstract class VisualEffect { }

    public sealed class ColorMatrixEffect : VisualEffect
    {
        private IReadOnlyList<float> _values = new float[]
        {
            1, 0, 0, 0, 0,
            0, 1, 0, 0, 0,
            0, 0, 1, 0, 0,
            0, 0, 0, 1, 0,
        };
        public IReadOnlyList<float> Values
        {
            get => _values;
            set
            {
                _ = new DrawingColorMatrixEffect(value);
                _values = new List<float>(value).AsReadOnly();
            }
        }
        internal DrawingColorMatrixEffect CreateDrawingEffect() => new DrawingColorMatrixEffect(Values);
    }

    public sealed class BlurEffect : VisualEffect
    {
        private float _radius;
        public float Radius
        {
            get => _radius;
            set
            {
                if (!float.IsFinite(value) || value < 0 || value > DrawingContextLimits.MaximumBlurRadius)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _radius = value;
            }
        }
    }

    public sealed class DropShadowEffect : VisualEffect
    {
        private float _blurRadius;
        public Color Color { get; set; } = Color.Black;
        public Vector2 Offset { get; set; }
        public float BlurRadius
        {
            get => _blurRadius;
            set
            {
                if (!float.IsFinite(value) || value < 0 || value > DrawingContextLimits.MaximumBlurRadius)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _blurRadius = value;
            }
        }
    }

    public readonly struct BoxShadow
    {
        public BoxShadow(Color color, Vector2 offset, float blurRadius = 0, float spreadRadius = 0, bool inset = false)
        {
            if (!float.IsFinite(blurRadius) || blurRadius < 0 || blurRadius > DrawingContextLimits.MaximumBlurRadius)
                throw new ArgumentOutOfRangeException(nameof(blurRadius));
            if (!float.IsFinite(spreadRadius) || MathF.Abs(spreadRadius) > DrawingContextLimits.MaximumOffscreenExpansion)
                throw new ArgumentOutOfRangeException(nameof(spreadRadius));
            Color = color;
            Offset = offset;
            BlurRadius = blurRadius;
            SpreadRadius = spreadRadius;
            Inset = inset;
        }
        public Color Color { get; }
        public Vector2 Offset { get; }
        public float BlurRadius { get; }
        public float SpreadRadius { get; }
        public bool Inset { get; }
    }

    public sealed class BoxShadowCollection : Collection<BoxShadow>
    {
        protected override void InsertItem(int index, BoxShadow item)
        {
            if (Count >= DrawingContextLimits.MaximumShadowCount)
                throw new InvalidOperationException($"A Border cannot exceed {DrawingContextLimits.MaximumShadowCount} shadows.");
            base.InsertItem(index, item);
        }
    }

    public sealed class EffectGroup : VisualEffect
    {
        private sealed class EffectCollection : Collection<VisualEffect>
        {
            protected override void InsertItem(int index, VisualEffect item)
            {
                if (item == null) throw new ArgumentNullException(nameof(item));
                if (item is EffectGroup) throw new InvalidOperationException("Effect groups cannot be nested.");
                if (Count >= DrawingContextLimits.MaximumEffectGroupLength)
                    throw new InvalidOperationException($"An effect group cannot exceed {DrawingContextLimits.MaximumEffectGroupLength} effects.");
                base.InsertItem(index, item);
            }

            protected override void SetItem(int index, VisualEffect item)
            {
                if (item == null) throw new ArgumentNullException(nameof(item));
                if (item is EffectGroup) throw new InvalidOperationException("Effect groups cannot be nested.");
                base.SetItem(index, item);
            }
        }

        private readonly EffectCollection _children = new EffectCollection();
        public IList<VisualEffect> Children => _children;
        public void Add(VisualEffect effect) => _children.Add(effect);
    }

    public abstract class Drawing
    {
        public Transform Transform { get; set; }
        public Geometry Clip { get; set; }
        public Brush OpacityMask { get; set; }
        public float Opacity { get; set; } = 1;
        public VisualEffect Effect { get; set; }

        internal void Render(UIRenderContext context, Rectangle bounds, Matrix parentTransform, float parentOpacity)
        {
            if (!float.IsFinite(Opacity) || Opacity < 0 || Opacity > 1) throw new InvalidOperationException("Drawing opacity must be between zero and one.");
            var transform = (Transform?.Value ?? Matrix.Identity) * parentTransform;
            context.Drawing.Save();
            try
            {
                context.Drawing.MultiplyOpacity(Opacity);
                if (Clip != null) context.Drawing.Clip(Clip.CreatePath(new Vector2(bounds.Width, bounds.Height)), transform, Clip.FillRule);
                Action draw = () => RenderCore(context, bounds, transform, parentOpacity * Opacity);
                if (OpacityMask != null)
                {
                    var content = draw;
                    draw = () => context.DrawOpacityMask(OpacityMask, bounds, content);
                }
                if (Effect != null) context.DrawEffect(Effect, bounds, draw);
                else draw();
            }
            finally { context.Drawing.Restore(); }
        }

        internal abstract void RenderCore(UIRenderContext context, Rectangle bounds, Matrix transform, float opacity);
    }

    public sealed class GeometryDrawing : Drawing
    {
        public Geometry Geometry { get; set; }
        public Brush Fill { get; set; }
        public Brush Stroke { get; set; }
        public float StrokeThickness { get; set; } = 1;
        public StrokeStyle StrokeStyle { get; set; }
        internal override void RenderCore(UIRenderContext context, Rectangle bounds, Matrix transform, float opacity)
        {
            if (Geometry == null) return;
            var path = Geometry.CreatePath(new Vector2(bounds.Width, bounds.Height));
            if (Fill != null) context.Drawing.FillPath(path, Fill, bounds, transform, Geometry.FillRule);
            if (Stroke != null && StrokeThickness > 0) context.Drawing.StrokePath(path, Stroke, bounds, StrokeThickness, transform, StrokeStyle);
        }
    }

    public sealed class ImageDrawing : Drawing
    {
        public Texture2D Source { get; set; }
        public ScalableImageSource ScalableSource { get; set; }
        public Rectangle? SourceRectangle { get; set; }
        public Color Tint { get; set; } = Color.White;
        public ImageSamplingMode SamplingMode { get; set; } = ImageSamplingMode.Linear;
        internal override void RenderCore(UIRenderContext context, Rectangle bounds, Matrix transform, float opacity)
        {
            if (Source != null) context.Drawing.DrawImage(Source, SourceRectangle, bounds, transform, Tint, SamplingMode);
            else if (ScalableSource != null) context.DrawScalableImage(ScalableSource, bounds, transform, Tint, SamplingMode);
        }
    }

    public sealed class TextDrawing : Drawing
    {
        public UIFont Font { get; set; }
        public string Text { get; set; } = string.Empty;
        public Vector2 Position { get; set; }
        public Color Color { get; set; } = Color.White;
        internal override void RenderCore(UIRenderContext context, Rectangle bounds, Matrix transform, float opacity)
        {
            if (Font == null || string.IsNullOrEmpty(Text)) return;
            context.Text(Font, Text, Vector2.Transform(Position, transform), Color * opacity);
        }
    }

    public sealed class DrawingGroup : Drawing
    {
        private readonly List<Drawing> _children = new List<Drawing>();
        public IList<Drawing> Children => _children;
        internal override void RenderCore(UIRenderContext context, Rectangle bounds, Matrix transform, float opacity)
        {
            foreach (var child in _children) child?.Render(context, bounds, transform, opacity);
        }
    }

    public sealed class DrawingImage
    {
        public Drawing Drawing { get; set; }
        public Vector2 IntrinsicSize { get; set; }
        public void Render(UIRenderContext context, Rectangle bounds) => Render(context, bounds, Color.White);
        public void Render(UIRenderContext context, Rectangle bounds, Color tint)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (Drawing == null) return;
            Action draw = () => Drawing.Render(context, bounds, Matrix.CreateTranslation(bounds.X, bounds.Y, 0), 1);
            if (tint == Color.White) draw();
            else
            {
                context.DrawEffect(new ColorMatrixEffect
                {
                    Values = new[]
                    {
                        tint.R / 255f, 0, 0, 0, 0,
                        0, tint.G / 255f, 0, 0, 0,
                        0, 0, tint.B / 255f, 0, 0,
                        0, 0, 0, tint.A / 255f, 0,
                    },
                }, bounds, draw);
            }
        }
    }

    public abstract class Shape : Control
    {
        private float _strokeThickness = 1;
        public Brush Fill { get; set; }
        public Brush Stroke { get; set; }
        public float StrokeThickness
        {
            get => _strokeThickness;
            set
            {
                if (!float.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                _strokeThickness = value;
                QueueLayout();
            }
        }
        public Transform GeometryTransform { get; set; }
        public StrokeStyle StrokeStyle { get; set; }
        public StrokeLineCap StrokeStartLineCap
        {
            get => StrokeStyle?.StartLineCap ?? StrokeLineCap.Butt;
            set => GetStrokeStyle().StartLineCap = value;
        }
        public StrokeLineCap StrokeEndLineCap
        {
            get => StrokeStyle?.EndLineCap ?? StrokeLineCap.Butt;
            set => GetStrokeStyle().EndLineCap = value;
        }
        public StrokeLineJoin StrokeLineJoin
        {
            get => StrokeStyle?.LineJoin ?? StrokeLineJoin.Miter;
            set => GetStrokeStyle().LineJoin = value;
        }
        public float StrokeMiterLimit
        {
            get => StrokeStyle?.MiterLimit ?? 4;
            set => GetStrokeStyle().MiterLimit = value;
        }
        public IReadOnlyList<float> StrokeDashArray
        {
            get => StrokeStyle?.DashArray ?? Array.Empty<float>();
            set => GetStrokeStyle().DashArray = value;
        }
        public float StrokeDashOffset
        {
            get => StrokeStyle?.DashOffset ?? 0;
            set => GetStrokeStyle().DashOffset = value;
        }
        public StrokeAlignment StrokeAlignment
        {
            get => StrokeStyle?.Alignment ?? StrokeAlignment.Center;
            set => GetStrokeStyle().Alignment = value;
        }
        public FillRule FillRule { get; set; }
        public ShapeStretch Stretch { get; set; } = ShapeStretch.None;
        public HorizontalAlignment GeometryHorizontalAlignment { get; set; } = HorizontalAlignment.Center;
        public VerticalAlignment GeometryVerticalAlignment { get; set; } = VerticalAlignment.Center;

        public sealed override void AddChild(Control child) => throw new InvalidOperationException("Shape elements cannot contain controls.");
        protected abstract DrawingPath CreatePath();
        protected virtual bool HasIntrinsicGeometryBounds => false;

        public override Vector2 GetMinimumSize()
        {
            var minimum = base.GetMinimumSize();
            if (!HasIntrinsicGeometryBounds) return minimum;
            var path = CreatePath();
            var bounds = GeometryPaths.GetBounds(GeometryTransform == null
                ? path
                : GeometryPaths.FromContours(DrawingPathFlattener.Flatten(path, GeometryTransform.Value, .25f)));
            var padding = Stroke != null ? StrokeThickness * .5f : 0;
            var intrinsic = new Vector2(
                MathF.Max(0, bounds.Right + padding) - MathF.Min(0, bounds.Left - padding),
                MathF.Max(0, bounds.Bottom + padding) - MathF.Min(0, bounds.Top - padding));
            return Vector2.Max(minimum, intrinsic);
        }

        public override bool ContainsPoint(Point point)
        {
            var path = CreatePath();
            var transform = GetStretchTransform(path) * (GeometryTransform?.Value ?? Matrix.Identity) * Matrix.CreateTranslation(GlobalPosition.X, GlobalPosition.Y, 0);
            var position = new Vector2(point.X, point.Y);
            if (Fill != null && path.ContainsPoint(position, transform, FillRule)) return true;
            if (Stroke == null || StrokeThickness <= 0) return false;
            return MeshContainsPoint(DrawingPathTessellator.TessellateStroke(path, transform, StrokeThickness, .25f, StrokeStyle), position);
        }

        internal override void Draw(UIRenderContext context)
        {
            var path = CreatePath();
            var transform = GetStretchTransform(path) * (GeometryTransform?.Value ?? Matrix.Identity) * Matrix.CreateTranslation(GlobalPosition.X, GlobalPosition.Y, 0);
            if (Fill != null) context.Drawing.FillPath(path, Fill, Bounds, transform, FillRule);
            if (Stroke != null && StrokeThickness > 0) context.Drawing.StrokePath(path, Stroke, Bounds, StrokeThickness, transform, StrokeStyle);
            base.Draw(context);
        }

        internal Matrix GetStretchTransform(DrawingPath path)
        {
            if (Stretch == ShapeStretch.None || path == null) return Matrix.Identity;
            var source = GeometryPaths.GetBounds(path);
            if (source.Width <= 0 || source.Height <= 0 || Size.X <= 0 || Size.Y <= 0) return Matrix.Identity;
            var scaleX = Size.X / source.Width;
            var scaleY = Size.Y / source.Height;
            if (Stretch == ShapeStretch.Uniform) scaleX = scaleY = MathF.Min(scaleX, scaleY);
            else if (Stretch == ShapeStretch.UniformToFill) scaleX = scaleY = MathF.Max(scaleX, scaleY);
            var width = source.Width * scaleX;
            var height = source.Height * scaleY;
            var x = GeometryHorizontalAlignment == HorizontalAlignment.Left ? 0
                : GeometryHorizontalAlignment == HorizontalAlignment.Right ? Size.X - width
                : (Size.X - width) * .5f;
            var y = GeometryVerticalAlignment == VerticalAlignment.Top ? 0
                : GeometryVerticalAlignment == VerticalAlignment.Bottom ? Size.Y - height
                : (Size.Y - height) * .5f;
            return Matrix.CreateTranslation(-source.X, -source.Y, 0) * Matrix.CreateScale(scaleX, scaleY, 1) * Matrix.CreateTranslation(x, y, 0);
        }

        private static bool MeshContainsPoint(DrawingMesh mesh, Vector2 point)
        {
            for (var index = 0; index < mesh.Indices.Length; index += 3)
            {
                var first = mesh.Vertices[mesh.Indices[index]];
                var second = mesh.Vertices[mesh.Indices[index + 1]];
                var third = mesh.Vertices[mesh.Indices[index + 2]];
                var firstSign = Cross(second - first, point - first);
                var secondSign = Cross(third - second, point - second);
                var thirdSign = Cross(first - third, point - third);
                if ((firstSign >= 0 && secondSign >= 0 && thirdSign >= 0) || (firstSign <= 0 && secondSign <= 0 && thirdSign <= 0)) return true;
            }
            return false;
        }

        private static float Cross(Vector2 left, Vector2 right) => left.X * right.Y - left.Y * right.X;

        private StrokeStyle GetStrokeStyle() => StrokeStyle ??= new StrokeStyle();
    }

    /// <summary>Draws a fillable and strokeable rectangle with optional uniform or per-corner rounding.</summary>
    public sealed class RectangleShape : Shape
    {
        public float RadiusX { get; set; }
        public float RadiusY { get; set; }
        public CornerRadius CornerRadius { get; set; }
        protected override DrawingPath CreatePath() => GeometryPaths.Rectangle(Size, CornerRadius == default ? new CornerRadius(MathF.Max(RadiusX, RadiusY)) : CornerRadius);
    }

    /// <summary>Draws a fillable and strokeable ellipse fitted to its bounds.</summary>
    public sealed class EllipseShape : Shape
    {
        protected override DrawingPath CreatePath() => GeometryPaths.Ellipse(Size);
    }

    /// <summary>Draws a stroked line segment between configurable local endpoints.</summary>
    public sealed class LineShape : Shape
    {
        public Vector2 StartPoint { get; set; }
        public Vector2 EndPoint { get; set; }
        protected override bool HasIntrinsicGeometryBounds => true;
        protected override DrawingPath CreatePath() => new DrawingPath().MoveTo(StartPoint).LineTo(EndPoint);
    }

    /// <summary>Draws an open path that connects a sequence of local points.</summary>
    public class PolylineShape : Shape
    {
        private IReadOnlyList<Vector2> _points = Array.Empty<Vector2>();
        public IReadOnlyList<Vector2> Points { get => _points; set => _points = value ?? Array.Empty<Vector2>(); }
        protected override bool HasIntrinsicGeometryBounds => true;
        protected override DrawingPath CreatePath()
        {
            var path = new DrawingPath();
            if (Points.Count == 0) return path;
            path.MoveTo(Points[0]);
            for (var index = 1; index < Points.Count; index++) path.LineTo(Points[index]);
            return path;
        }
    }

    /// <summary>Draws a closed polygonal path through a sequence of local points.</summary>
    public sealed class PolygonShape : PolylineShape
    {
        protected override DrawingPath CreatePath()
        {
            var path = base.CreatePath();
            if (Points.Count > 0) path.Close();
            return path;
        }
    }

    /// <summary>Draws fill and stroke geometry supplied by a reusable <see cref="Geometry"/> object.</summary>
    public sealed class PathShape : Shape
    {
        public Geometry Data { get; set; }
        protected override bool HasIntrinsicGeometryBounds => true;
        protected override DrawingPath CreatePath() => Data?.CreatePath(Size) ?? new DrawingPath();
    }

    internal static class GeometryPaths
    {
        private const float Kappa = .55228475f;

        public static DrawingPath Rectangle(Vector2 size, float radiusX, float radiusY) => Rectangle(size, new CornerRadius(MathF.Max(radiusX, radiusY)));

        public static DrawingPath Rectangle(Vector2 size, CornerRadius cornerRadius)
        {
            var width = MathF.Max(0, size.X);
            var height = MathF.Max(0, size.Y);
            var maximum = MathF.Min(width, height) * .5f;
            var topLeft = MathHelper.Clamp(cornerRadius.TopLeft, 0, maximum);
            var topRight = MathHelper.Clamp(cornerRadius.TopRight, 0, maximum);
            var bottomRight = MathHelper.Clamp(cornerRadius.BottomRight, 0, maximum);
            var bottomLeft = MathHelper.Clamp(cornerRadius.BottomLeft, 0, maximum);
            var scale = MathF.Min(1, MathF.Min(
                width / MathF.Max(float.Epsilon, MathF.Max(topLeft + topRight, bottomLeft + bottomRight)),
                height / MathF.Max(float.Epsilon, MathF.Max(topLeft + bottomLeft, topRight + bottomRight))));
            topLeft *= scale;
            topRight *= scale;
            bottomRight *= scale;
            bottomLeft *= scale;
            if (topLeft <= 0 && topRight <= 0 && bottomRight <= 0 && bottomLeft <= 0)
                return new DrawingPath().MoveTo(Vector2.Zero).LineTo(new Vector2(width, 0)).LineTo(new Vector2(width, height)).LineTo(new Vector2(0, height)).Close();
            return new DrawingPath().MoveTo(new Vector2(topLeft, 0)).LineTo(new Vector2(width - topRight, 0))
                .CubicTo(new Vector2(width - topRight + topRight * Kappa, 0), new Vector2(width, topRight - topRight * Kappa), new Vector2(width, topRight))
                .LineTo(new Vector2(width, height - bottomRight)).CubicTo(new Vector2(width, height - bottomRight + bottomRight * Kappa), new Vector2(width - bottomRight + bottomRight * Kappa, height), new Vector2(width - bottomRight, height))
                .LineTo(new Vector2(bottomLeft, height)).CubicTo(new Vector2(bottomLeft - bottomLeft * Kappa, height), new Vector2(0, height - bottomLeft + bottomLeft * Kappa), new Vector2(0, height - bottomLeft))
                .LineTo(new Vector2(0, topLeft)).CubicTo(new Vector2(0, topLeft - topLeft * Kappa), new Vector2(topLeft - topLeft * Kappa, 0), new Vector2(topLeft, 0)).Close();
        }

        public static DrawingPath Ellipse(Vector2 size)
        {
            var radiusX = MathF.Max(0, size.X * .5f);
            var radiusY = MathF.Max(0, size.Y * .5f);
            var center = new Vector2(radiusX, radiusY);
            return new DrawingPath().MoveTo(new Vector2(center.X, 0))
                .CubicTo(new Vector2(center.X + radiusX * Kappa, 0), new Vector2(size.X, center.Y - radiusY * Kappa), new Vector2(size.X, center.Y))
                .CubicTo(new Vector2(size.X, center.Y + radiusY * Kappa), new Vector2(center.X + radiusX * Kappa, size.Y), new Vector2(center.X, size.Y))
                .CubicTo(new Vector2(center.X - radiusX * Kappa, size.Y), new Vector2(0, center.Y + radiusY * Kappa), new Vector2(0, center.Y))
                .CubicTo(new Vector2(0, center.Y - radiusY * Kappa), new Vector2(center.X - radiusX * Kappa, 0), new Vector2(center.X, 0)).Close();
        }

        public static DrawingPath FromContours(IEnumerable<IReadOnlyList<Vector2>> contours)
        {
            var result = new DrawingPath();
            foreach (var contour in contours)
            {
                var count = contour.Count > 1 && contour[0] == contour[contour.Count - 1] ? contour.Count - 1 : contour.Count;
                if (count < 2) continue;
                result.MoveTo(contour[0]);
                for (var index = 1; index < count; index++) result.LineTo(contour[index]);
                if (count >= 3) result.Close();
            }
            return result;
        }

        public static RectangleF GetBounds(DrawingPath path)
        {
            var contours = DrawingPathFlattener.Flatten(path, Matrix.Identity, .25f);
            if (contours.Count == 0) return new RectangleF();
            var minimum = new Vector2(float.PositiveInfinity);
            var maximum = new Vector2(float.NegativeInfinity);
            foreach (var contour in contours)
            foreach (var point in contour)
            {
                minimum = Vector2.Min(minimum, point);
                maximum = Vector2.Max(maximum, point);
            }
            return float.IsFinite(minimum.X) ? new RectangleF(minimum.X, minimum.Y, maximum.X - minimum.X, maximum.Y - minimum.Y) : new RectangleF();
        }
    }

    internal static class GeometryClipper
    {
        internal const int DecimalPrecision = 4;

        public static PathsD ToPaths(IEnumerable<IReadOnlyList<Vector2>> contours)
        {
            var result = new PathsD();
            foreach (var contour in contours)
            {
                var count = contour.Count > 1 && contour[0] == contour[contour.Count - 1] ? contour.Count - 1 : contour.Count;
                if (count < 3) continue;
                var path = new PathD(count);
                for (var index = 0; index < count; index++) path.Add(new PointD(contour[index].X, contour[index].Y));
                result.Add(path);
            }
            return result;
        }

        public static DrawingPath ToDrawingPath(PathsD paths)
        {
            var contours = new List<IReadOnlyList<Vector2>>(paths.Count);
            foreach (var path in paths)
            {
                var contour = new List<Vector2>(path.Count);
                foreach (var point in path) contour.Add(new Vector2((float)point.x, (float)point.y));
                contours.Add(contour);
            }
            return GeometryPaths.FromContours(contours);
        }
    }
}