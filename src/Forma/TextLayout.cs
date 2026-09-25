// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Forma
{
    public enum UIFontWeight { Thin = 100, ExtraLight = 200, Light = 300, Normal = 400, Medium = 500, SemiBold = 600, Bold = 700, ExtraBold = 800, Black = 900 }
    public enum UIFontStyle { Normal, Italic, Oblique }
    /// <summary>Glyph outline synthesis for faces without a real bold or italic variant.</summary>
    [Flags] public enum UIFontSynthesis { None = 0, Bold = 1, Oblique = 2 }
    public enum UIFontStretch { UltraCondensed = 1, ExtraCondensed, Condensed, SemiCondensed, Normal, SemiExpanded, Expanded, ExtraExpanded, UltraExpanded }

    public enum TextWrapping { NoWrap, Character, Word }
    public enum TextTrimming { None, CharacterEllipsis, WordEllipsis }

    public readonly struct UIFontOpenTypeFeature : IEquatable<UIFontOpenTypeFeature>
    {
        public UIFontOpenTypeFeature(string tag, uint value = 1)
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));
            if (tag.Length != 4) throw new ArgumentException("An OpenType feature tag must contain exactly four characters.", nameof(tag));
            for (var index = 0; index < tag.Length; index++)
                if (tag[index] < 0x20 || tag[index] > 0x7E)
                    throw new ArgumentException("An OpenType feature tag must contain printable ASCII characters.", nameof(tag));
            Tag = tag;
            Value = value;
        }

        public string Tag { get; }
        public uint Value { get; }
        public bool Equals(UIFontOpenTypeFeature other) => string.Equals(Tag, other.Tag, StringComparison.Ordinal) && Value == other.Value;
        public override bool Equals(object obj) => obj is UIFontOpenTypeFeature other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(Tag ?? string.Empty), Value);
        public static bool operator ==(UIFontOpenTypeFeature left, UIFontOpenTypeFeature right) => left.Equals(right);
        public static bool operator !=(UIFontOpenTypeFeature left, UIFontOpenTypeFeature right) => !left.Equals(right);
    }

    public readonly struct UIFontIdentity : IEquatable<UIFontIdentity>
    {
        public UIFontIdentity(string kind, string value)
        {
            Kind = string.IsNullOrWhiteSpace(kind) ? throw new ArgumentException("A font identity kind is required.", nameof(kind)) : kind;
            Value = string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A font identity value is required.", nameof(value)) : value;
        }

        public string Kind { get; }
        public string Value { get; }
        public bool Equals(UIFontIdentity other) => string.Equals(Kind, other.Kind, StringComparison.Ordinal) && string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is UIFontIdentity other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Kind, Value);
        public static bool operator ==(UIFontIdentity left, UIFontIdentity right) => left.Equals(right);
        public static bool operator !=(UIFontIdentity left, UIFontIdentity right) => !left.Equals(right);
        public override string ToString() => $"{Kind}:{Value}";
    }

    public abstract class UIFont : IEquatable<UIFont>
    {
        protected UIFont(UIFontIdentity identity, float size, IReadOnlyList<UIFontOpenTypeFeature> defaultOpenTypeFeatures = null,
            UIFontWeight weight = UIFontWeight.Normal, UIFontStyle style = UIFontStyle.Normal, UIFontStretch stretch = UIFontStretch.Normal)
        {
            if (!float.IsFinite(size) || size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
            if (!Enum.IsDefined(typeof(UIFontWeight), weight)) throw new ArgumentOutOfRangeException(nameof(weight));
            if (!Enum.IsDefined(typeof(UIFontStyle), style)) throw new ArgumentOutOfRangeException(nameof(style));
            if (!Enum.IsDefined(typeof(UIFontStretch), stretch)) throw new ArgumentOutOfRangeException(nameof(stretch));
            Identity = identity;
            Size = size;
            DefaultOpenTypeFeatures = defaultOpenTypeFeatures == null ? Array.Empty<UIFontOpenTypeFeature>() : new List<UIFontOpenTypeFeature>(defaultOpenTypeFeatures).AsReadOnly();
            Weight = weight;
            Style = style;
            Stretch = stretch;
        }

        public UIFontIdentity Identity { get; }
        public float Size { get; }
        public UIFontWeight Weight { get; }
        public UIFontStyle Style { get; }
        public UIFontStretch Stretch { get; }
        public IReadOnlyList<UIFontOpenTypeFeature> DefaultOpenTypeFeatures { get; }
        public bool Equals(UIFont other) => other != null && Identity == other.Identity && Size.Equals(other.Size);
        public override bool Equals(object obj) => obj is UIFont other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Identity, Size);
        public UIFont WithSize(float size)
        {
            if (!float.IsFinite(size) || size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
            if (size == Size) return this;
            var resized = Resize(size);
            if (resized == null || resized.Size != size)
                throw new NotSupportedException($"{GetType().Name} does not support font resizing.");
            return resized;
        }
        internal virtual UIFont Resize(float size) => this;
        internal virtual bool HasThemeDefaults(float size, UIFontHinting hinting, IReadOnlyList<UIFontOpenTypeFeature> features) => Math.Abs(size - Size) < .0001f;
        internal virtual UIFont ApplyThemeDefaults(float size, UIFontHinting hinting, IReadOnlyList<UIFontOpenTypeFeature> features) => Resize(size);
        internal virtual UIFontHinting RasterHinting => UIFontHinting.Default;
        internal virtual long ShapeTicks => 0;
        internal virtual bool SharesLayoutResources(UIFont other) => true;
        internal virtual UIFontGlyphBitmap RasterizeGlyph(uint glyphId, float displayScale) => throw new NotSupportedException($"{GetType().Name} does not support dynamic glyph rasterization.");
        internal abstract TextLayout CreateLayout(string text, TextLayoutOptions options);
        internal abstract void Draw(UIRenderContext context, TextLayout layout, Vector2 position, Color color);
    }

    public sealed class UIFontFamily : IEquatable<UIFontFamily>
    {
        private readonly ReadOnlyCollection<UIFont> _fonts;

        public UIFontFamily(IEnumerable<UIFont> fonts)
        {
            if (fonts == null) throw new ArgumentNullException(nameof(fonts));
            var values = new List<UIFont>();
            foreach (var font in fonts) values.Add(font ?? throw new ArgumentException("Font families cannot contain null entries.", nameof(fonts)));
            if (values.Count == 0) throw new ArgumentException("A font family requires at least one font.", nameof(fonts));
            _fonts = values.AsReadOnly();
        }

        public IReadOnlyList<UIFont> Fonts => _fonts;
        public UIFont Primary => _fonts[0];

        public UIFont Match(UIFontWeight weight, UIFontStyle style, UIFontStretch stretch)
        {
            if (!Enum.IsDefined(typeof(UIFontWeight), weight)) throw new ArgumentOutOfRangeException(nameof(weight));
            if (!Enum.IsDefined(typeof(UIFontStyle), style)) throw new ArgumentOutOfRangeException(nameof(style));
            if (!Enum.IsDefined(typeof(UIFontStretch), stretch)) throw new ArgumentOutOfRangeException(nameof(stretch));
            UIFont best = null;
            var bestScore = int.MaxValue;
            foreach (var font in _fonts)
            {
                var score = Math.Abs((int)font.Weight - (int)weight) + Math.Abs((int)font.Stretch - (int)stretch) * 1000;
                if (font.Style != style) score += font.Style == UIFontStyle.Normal || style == UIFontStyle.Normal ? 100_000 : 10_000;
                if (score >= bestScore) continue;
                best = font;
                bestScore = score;
            }
            return best;
        }

        public bool Equals(UIFontFamily other)
        {
            if (other == null || _fonts.Count != other._fonts.Count) return false;
            for (var index = 0; index < _fonts.Count; index++)
                if (!_fonts[index].Equals(other._fonts[index])) return false;
            return true;
        }

        public override bool Equals(object obj) => obj is UIFontFamily other && Equals(other);
        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var font in _fonts) hash.Add(font);
            return hash.ToHashCode();
        }
    }

    [Flags]
    public enum TextJustificationFlags
    {
        None = 0,
        WordBound = 1,
        AfterLastTab = 2,
        SkipLastLine = 4,
        SkipLastLineWithVisibleCharacters = 8,
        DoNotSkipSingleLine = 16,
    }

    public readonly struct TextLayoutOptions : IEquatable<TextLayoutOptions>
    {
        private readonly ReadOnlyCollection<float> _tabStops;
        private readonly ReadOnlyCollection<UIFontOpenTypeFeature> _openTypeFeatures;

        public TextLayoutOptions()
            : this(float.PositiveInfinity, TextWrapping.NoWrap, HorizontalAlignment.Left, TextDirection.Auto, 1f, 4, TextTrimming.None, int.MaxValue, null, null, null, null, null)
        {
        }

        public TextLayoutOptions(
            float maxWidth = float.PositiveInfinity,
            TextWrapping wrapping = TextWrapping.NoWrap,
            HorizontalAlignment alignment = HorizontalAlignment.Left,
            TextDirection direction = TextDirection.Auto,
            float lineSpacing = 1f,
            int tabSize = 4,
            TextTrimming trimming = TextTrimming.None,
            int maxVisibleCharacters = int.MaxValue,
            string locale = null,
            string paragraphSeparator = null,
            IEnumerable<float> tabStops = null,
            IEnumerable<UIFontOpenTypeFeature> openTypeFeatures = null,
            string ellipsis = null,
            float paragraphSpacing = 0,
            TextJustificationFlags justificationFlags = TextJustificationFlags.None)
        {
            if (float.IsNaN(maxWidth) || maxWidth <= 0) throw new ArgumentOutOfRangeException(nameof(maxWidth));
            if (!Enum.IsDefined(typeof(TextWrapping), wrapping)) throw new ArgumentOutOfRangeException(nameof(wrapping));
            if (!Enum.IsDefined(typeof(HorizontalAlignment), alignment)) throw new ArgumentOutOfRangeException(nameof(alignment));
            if (!Enum.IsDefined(typeof(TextDirection), direction)) throw new ArgumentOutOfRangeException(nameof(direction));
            if (!float.IsFinite(lineSpacing) || lineSpacing <= 0) throw new ArgumentOutOfRangeException(nameof(lineSpacing));
            if (tabSize <= 0) throw new ArgumentOutOfRangeException(nameof(tabSize));
            if (!Enum.IsDefined(typeof(TextTrimming), trimming)) throw new ArgumentOutOfRangeException(nameof(trimming));
            if (maxVisibleCharacters < 0) throw new ArgumentOutOfRangeException(nameof(maxVisibleCharacters));
            if (!float.IsFinite(paragraphSpacing) || paragraphSpacing < 0) throw new ArgumentOutOfRangeException(nameof(paragraphSpacing));
            MaxWidth = maxWidth;
            Wrapping = wrapping;
            Alignment = alignment;
            Direction = direction;
            LineSpacing = lineSpacing;
            TabSize = tabSize;
            Trimming = trimming;
            MaxVisibleCharacters = maxVisibleCharacters;
            Locale = locale ?? string.Empty;
            ParagraphSeparator = paragraphSeparator ?? string.Empty;
            Ellipsis = ellipsis ?? "…";
            ParagraphSpacing = paragraphSpacing;
            JustificationFlags = justificationFlags;
            if (Ellipsis.Length == 0) throw new ArgumentException("Ellipsis text cannot be empty.", nameof(ellipsis));
            var stops = new List<float>();
            if (tabStops != null)
                foreach (var stop in tabStops)
                {
                    if (!float.IsFinite(stop) || stop <= 0) throw new ArgumentOutOfRangeException(nameof(tabStops));
                    stops.Add(stop);
                }
            _tabStops = stops.AsReadOnly();
            var features = new List<UIFontOpenTypeFeature>();
            if (openTypeFeatures != null)
                foreach (var feature in openTypeFeatures)
                {
                    if (feature.Tag == null) throw new ArgumentException("OpenType features must be initialized.", nameof(openTypeFeatures));
                    features.Add(feature);
                }
            _openTypeFeatures = features.AsReadOnly();
        }

        public static TextLayoutOptions Default => new TextLayoutOptions();
        public float MaxWidth { get; }
        public TextWrapping Wrapping { get; }
        public HorizontalAlignment Alignment { get; }
        public TextDirection Direction { get; }
        public float LineSpacing { get; }
        public int TabSize { get; }
        public TextTrimming Trimming { get; }
        public int MaxVisibleCharacters { get; }
        public string Locale { get; }
        public string ParagraphSeparator { get; }
        public string Ellipsis { get; }
        public float ParagraphSpacing { get; }
        public TextJustificationFlags JustificationFlags { get; }
        public IReadOnlyList<float> TabStops => _tabStops != null ? _tabStops : Array.Empty<float>();
        public IReadOnlyList<UIFontOpenTypeFeature> OpenTypeFeatures => _openTypeFeatures != null ? _openTypeFeatures : Array.Empty<UIFontOpenTypeFeature>();
        public bool Equals(TextLayoutOptions other) => MaxWidth.Equals(other.MaxWidth) && Wrapping == other.Wrapping &&
            Alignment == other.Alignment && Direction == other.Direction && LineSpacing.Equals(other.LineSpacing) &&
            TabSize == other.TabSize && Trimming == other.Trimming && MaxVisibleCharacters == other.MaxVisibleCharacters &&
            string.Equals(Locale, other.Locale, StringComparison.Ordinal) &&
            string.Equals(ParagraphSeparator, other.ParagraphSeparator, StringComparison.Ordinal) &&
            string.Equals(Ellipsis, other.Ellipsis, StringComparison.Ordinal) &&
            ParagraphSpacing.Equals(other.ParagraphSpacing) && JustificationFlags == other.JustificationFlags &&
            TabStopsEqual(TabStops, other.TabStops) && FeaturesEqual(OpenTypeFeatures, other.OpenTypeFeatures);
        public override bool Equals(object obj) => obj is TextLayoutOptions other && Equals(other);
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(MaxWidth);
            hash.Add(Wrapping);
            hash.Add(Alignment);
            hash.Add(Direction);
            hash.Add(LineSpacing);
            hash.Add(TabSize);
            hash.Add(Trimming);
            hash.Add(MaxVisibleCharacters);
            hash.Add(Locale, StringComparer.Ordinal);
            hash.Add(ParagraphSeparator, StringComparer.Ordinal);
            hash.Add(Ellipsis, StringComparer.Ordinal);
            hash.Add(ParagraphSpacing); hash.Add(JustificationFlags);
            foreach (var stop in TabStops) hash.Add(stop);
            foreach (var feature in OpenTypeFeatures) hash.Add(feature);
            return hash.ToHashCode();
        }
        private static bool TabStopsEqual(IReadOnlyList<float> left, IReadOnlyList<float> right)
        {
            if (left.Count != right.Count) return false;
            for (var index = 0; index < left.Count; index++)
                if (!left[index].Equals(right[index])) return false;
            return true;
        }
        private static bool FeaturesEqual(IReadOnlyList<UIFontOpenTypeFeature> left, IReadOnlyList<UIFontOpenTypeFeature> right)
        {
            if (left.Count != right.Count) return false;
            for (var index = 0; index < left.Count; index++)
                if (left[index] != right[index]) return false;
            return true;
        }
        public static bool operator ==(TextLayoutOptions left, TextLayoutOptions right) => left.Equals(right);
        public static bool operator !=(TextLayoutOptions left, TextLayoutOptions right) => !left.Equals(right);
    }

    public sealed class TextLayoutLine
    {
        internal TextLayoutLine(int start, int length, Vector2 origin, Vector2 size, float baseline, int visibleLength = -1, string ellipsis = null, float ellipsisOffset = 0)
        {
            Start = start;
            Length = length;
            Origin = origin;
            Size = size;
            Baseline = baseline;
            VisibleRange = new TextLayoutRange(start, visibleLength < 0 ? length : visibleLength);
            Ellipsis = ellipsis ?? string.Empty;
            EllipsisOffset = ellipsisOffset;
        }

        public int Start { get; }
        public int Length { get; }
        public Vector2 Origin { get; }
        public Vector2 Size { get; }
        public float Baseline { get; }
        public TextLayoutRange VisibleRange { get; }
        public string Ellipsis { get; }
        internal float EllipsisOffset { get; }
        public bool IsTrimmed => VisibleRange.Length < Length;
    }

    public sealed class TextLayoutGlyph
    {
        internal TextLayoutGlyph(uint glyphId, int utf16Cluster, Vector2 position, Vector2 advance, Vector2 offset, RectangleF bounds, bool isSynthetic = false)
        {
            GlyphId = glyphId;
            Utf16Cluster = utf16Cluster;
            Position = position;
            Advance = advance;
            Offset = offset;
            Bounds = bounds;
            IsSynthetic = isSynthetic;
        }

        public uint GlyphId { get; }
        public int Utf16Cluster { get; }
        public Vector2 Position { get; }
        public Vector2 Advance { get; }
        public Vector2 Offset { get; }
        public RectangleF Bounds { get; }
        public bool IsSynthetic { get; }
    }

    public sealed class TextLayoutCluster
    {
        internal TextLayoutCluster(int start, int length, int logicalIndex, int visualIndex, int lineIndex, RectangleF bounds)
        {
            Start = start;
            Length = length;
            LogicalIndex = logicalIndex;
            VisualIndex = visualIndex;
            LineIndex = lineIndex;
            Bounds = bounds;
        }

        public int Start { get; }
        public int Length { get; }
        public int LogicalIndex { get; }
        public int VisualIndex { get; }
        public int LineIndex { get; }
        public RectangleF Bounds { get; }
    }

    public readonly struct TextLayoutRange : IEquatable<TextLayoutRange>
    {
        public TextLayoutRange(int start, int length)
        {
            if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            Start = start;
            Length = length;
        }

        public int Start { get; }
        public int Length { get; }
        public int End => Start + Length;
        public bool Equals(TextLayoutRange other) => Start == other.Start && Length == other.Length;
        public override bool Equals(object obj) => obj is TextLayoutRange other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Start, Length);
        public static bool operator ==(TextLayoutRange left, TextLayoutRange right) => left.Equals(right);
        public static bool operator !=(TextLayoutRange left, TextLayoutRange right) => !left.Equals(right);
    }

    public sealed class TextLayoutRun
    {
        private readonly ReadOnlyCollection<TextLayoutGlyph> _glyphs;
        private readonly Vector2[] _carets;

        internal TextLayoutRun(int start, int length, UIFont font, TextDirection direction, byte? bidiLevel, IEnumerable<TextLayoutGlyph> glyphs, Vector2[] carets, RectangleF bounds)
        {
            Start = start;
            Length = length;
            Font = font ?? throw new ArgumentNullException(nameof(font));
            Direction = direction;
            BidiLevel = bidiLevel;
            _glyphs = new List<TextLayoutGlyph>(glyphs ?? throw new ArgumentNullException(nameof(glyphs))).AsReadOnly();
            if (carets == null || carets.Length != length + 1) throw new ArgumentException("Run carets must cover every UTF-16 boundary.", nameof(carets));
            _carets = (Vector2[])carets.Clone();
            Bounds = bounds;
        }

        public int Start { get; }
        public int Length { get; }
        public UIFont Font { get; }
        public TextDirection Direction { get; }
        public byte? BidiLevel { get; }
        public IReadOnlyList<TextLayoutGlyph> Glyphs => _glyphs;
        public RectangleF Bounds { get; }

        internal Vector2 GetCaretPosition(int utf16Offset)
        {
            if (utf16Offset < 0 || utf16Offset > Length) throw new ArgumentOutOfRangeException(nameof(utf16Offset));
            return _carets[utf16Offset];
        }
    }

    public sealed class TextLayout
    {
        private readonly ReadOnlyCollection<TextLayoutLine> _lines;
        private readonly ReadOnlyCollection<TextLayoutRun> _runs;
        private readonly ReadOnlyCollection<TextLayoutCluster> _clusters;
        private readonly ReadOnlyCollection<TextLayoutCluster> _visualClusters;
        private readonly ReadOnlyCollection<TextLayoutGlyph> _visibleGlyphs;
        private readonly ReadOnlyCollection<TextLayoutRange> _visibleRanges;
        private readonly Vector2[] _carets;
        private readonly int[] _wordBoundaries;

        internal TextLayout(string text, UIFont font, TextLayoutOptions options, List<TextLayoutLine> lines, List<TextLayoutRun> runs, Vector2[] carets, Vector2 size, List<TextLayoutRange> visibleRanges = null, float letterSpacing = 0)
        {
            Text = text;
            Font = font;
            Options = options;
            _lines = lines.AsReadOnly();
            _runs = runs.AsReadOnly();
            _carets = carets;
            _wordBoundaries = UnicodeWordBreaker.GetUtf16Boundaries(text);
            BuildClusters(text, lines, runs, carets, out _clusters, out _visualClusters);
            var visibleClusterCount = Math.Min(options.MaxVisibleCharacters, _clusters.Count);
            var visibleEnd = visibleClusterCount == 0 ? 0 : _clusters[visibleClusterCount - 1].Start + _clusters[visibleClusterCount - 1].Length;
            VisibleRange = new TextLayoutRange(0, visibleEnd);
            var ranges = visibleRanges ?? BuildDefaultVisibleRanges(lines);
            var clippedRanges = new List<TextLayoutRange>(ranges.Count);
            foreach (var range in ranges)
            {
                var end = Math.Min(range.End, visibleEnd);
                if (end > range.Start) clippedRanges.Add(new TextLayoutRange(range.Start, end - range.Start));
            }
            _visibleRanges = clippedRanges.AsReadOnly();
            var visibleGlyphs = new List<TextLayoutGlyph>();
            foreach (var run in runs)
                foreach (var glyph in run.Glyphs)
                    if (glyph.Utf16Cluster <= visibleEnd && (glyph.IsSynthetic && visibleEnd > 0 || IsSourceVisible(glyph.Utf16Cluster, clippedRanges))) visibleGlyphs.Add(glyph);
            _visibleGlyphs = visibleGlyphs.AsReadOnly();
            Size = size;
            LetterSpacing = letterSpacing;
        }

        public string Text { get; }
        public UIFont Font { get; }
        public TextLayoutOptions Options { get; }
        public IReadOnlyList<TextLayoutLine> Lines => _lines;
        public IReadOnlyList<TextLayoutRun> Runs => _runs;
        public IReadOnlyList<TextLayoutCluster> Clusters => _clusters;
        public IReadOnlyList<TextLayoutCluster> VisualClusters => _visualClusters;
        public IReadOnlyList<TextLayoutGlyph> VisibleGlyphs => _visibleGlyphs;
        public IReadOnlyList<TextLayoutRange> VisibleRanges => _visibleRanges;
        public TextLayoutRange VisibleRange { get; }
        public Vector2 Size { get; }
        internal float LetterSpacing { get; }
        public RectangleF Bounds => new RectangleF(0, 0, Size.X, Size.Y);

        public bool IsUtf16IndexVisible(int utf16Index)
        {
            if (utf16Index < 0 || utf16Index >= Text.Length) throw new ArgumentOutOfRangeException(nameof(utf16Index));
            return utf16Index < VisibleRange.End && IsSourceVisible(utf16Index, _visibleRanges);
        }

        internal bool IsVisible(TextLayoutGlyph glyph) => glyph.Utf16Cluster <= VisibleRange.End && (glyph.IsSynthetic && VisibleRange.End > 0 || IsSourceVisible(glyph.Utf16Cluster, _visibleRanges));

        private static List<TextLayoutRange> BuildDefaultVisibleRanges(List<TextLayoutLine> lines)
        {
            var ranges = new List<TextLayoutRange>(lines.Count);
            foreach (var line in lines)
                if (line.VisibleRange.Length > 0) ranges.Add(line.VisibleRange);
            return ranges;
        }

        private static bool IsSourceVisible(int utf16Index, IReadOnlyList<TextLayoutRange> ranges)
        {
            for (var index = 0; index < ranges.Count; index++)
            {
                var range = ranges[index];
                if (utf16Index >= range.Start && utf16Index < range.End) return true;
            }
            return false;
        }

        public Vector2 GetCaretPosition(int utf16Index)
        {
            if (utf16Index < 0 || utf16Index > Text.Length) throw new ArgumentOutOfRangeException(nameof(utf16Index));
            return _carets[utf16Index];
        }

        public int HitTest(Vector2 position)
        {
            if (_lines.Count == 0) return 0;
            var lineIndex = Math.Clamp((int)MathF.Floor(position.Y / Math.Max(1, _lines[0].Size.Y)), 0, _lines.Count - 1);
            var line = _lines[lineIndex];
            var best = line.Start;
            var bestDistance = float.PositiveInfinity;
            for (var index = line.Start; index <= line.Start + line.Length; index++)
            {
                var distance = MathF.Abs(_carets[index].X - position.X);
                if (distance >= bestDistance) continue;
                best = index;
                bestDistance = distance;
            }
            return best;
        }

        public int GetClusterIndex(int utf16Index)
        {
            if (utf16Index < 0 || utf16Index > Text.Length) throw new ArgumentOutOfRangeException(nameof(utf16Index));
            if (utf16Index == Text.Length) return _clusters.Count;
            var low = 0;
            var high = _clusters.Count - 1;
            while (low <= high)
            {
                var middle = low + (high - low) / 2;
                var cluster = _clusters[middle];
                if (utf16Index < cluster.Start) high = middle - 1;
                else if (utf16Index >= cluster.Start + cluster.Length) low = middle + 1;
                else return middle;
            }
            return Math.Clamp(low, 0, _clusters.Count);
        }

        public int GetPreviousGraphemeBoundary(int utf16Index)
        {
            if (utf16Index < 0 || utf16Index > Text.Length) throw new ArgumentOutOfRangeException(nameof(utf16Index));
            if (utf16Index == 0 || _clusters.Count == 0) return 0;
            var clusterIndex = GetClusterIndex(utf16Index);
            if (clusterIndex == _clusters.Count || _clusters[clusterIndex].Start >= utf16Index) clusterIndex--;
            return clusterIndex < 0 ? 0 : _clusters[clusterIndex].Start;
        }

        public int GetNextGraphemeBoundary(int utf16Index)
        {
            if (utf16Index < 0 || utf16Index > Text.Length) throw new ArgumentOutOfRangeException(nameof(utf16Index));
            if (utf16Index == Text.Length || _clusters.Count == 0) return Text.Length;
            var clusterIndex = GetClusterIndex(utf16Index);
            if (clusterIndex >= _clusters.Count) return Text.Length;
            var cluster = _clusters[clusterIndex];
            return cluster.Start + cluster.Length;
        }

        public TextLayoutRange GetWordBoundary(int utf16Index)
        {
            if (utf16Index < 0 || utf16Index > Text.Length) throw new ArgumentOutOfRangeException(nameof(utf16Index));
            if (Text.Length == 0) return new TextLayoutRange(0, 0);
            var next = Array.BinarySearch(_wordBoundaries, utf16Index);
            if (next >= 0)
            {
                if (next == _wordBoundaries.Length - 1) next--;
            }
            else
            {
                next = ~next - 1;
            }
            return new TextLayoutRange(_wordBoundaries[next], _wordBoundaries[next + 1] - _wordBoundaries[next]);
        }

        public int GetPreviousWordBoundary(int utf16Index)
        {
            if (utf16Index < 0 || utf16Index > Text.Length) throw new ArgumentOutOfRangeException(nameof(utf16Index));
            var index = Array.BinarySearch(_wordBoundaries, utf16Index);
            if (index >= 0) return index == 0 ? 0 : _wordBoundaries[index - 1];
            return _wordBoundaries[Math.Max(0, ~index - 1)];
        }

        public int GetNextWordBoundary(int utf16Index)
        {
            if (utf16Index < 0 || utf16Index > Text.Length) throw new ArgumentOutOfRangeException(nameof(utf16Index));
            var index = Array.BinarySearch(_wordBoundaries, utf16Index);
            if (index >= 0) return index + 1 < _wordBoundaries.Length ? _wordBoundaries[index + 1] : Text.Length;
            return _wordBoundaries[Math.Min(~index, _wordBoundaries.Length - 1)];
        }

        public RectangleF GetRangeBounds(int start, int length)
        {
            var rectangles = GetSelectionRectangles(start, length);
            if (rectangles.Count == 0)
            {
                var caret = GetCaretPosition(start);
                return new RectangleF(caret.X, caret.Y, 0, 0);
            }
            var left = float.PositiveInfinity;
            var top = float.PositiveInfinity;
            var right = float.NegativeInfinity;
            var bottom = float.NegativeInfinity;
            foreach (var rectangle in rectangles)
            {
                left = Math.Min(left, rectangle.Left);
                top = Math.Min(top, rectangle.Top);
                right = Math.Max(right, rectangle.Right);
                bottom = Math.Max(bottom, rectangle.Bottom);
            }
            return new RectangleF(left, top, right - left, bottom - top);
        }

        public IReadOnlyList<RectangleF> GetSelectionRectangles(int start, int length)
        {
            if (start < 0 || length < 0 || start > Text.Length - length) throw new ArgumentOutOfRangeException();
            var end = start + length;
            var rectangles = new List<RectangleF>();
            foreach (var line in _lines)
            {
                foreach (var run in _runs)
                {
                    var selectionStart = Math.Max(start, Math.Max(line.Start, run.Start));
                    var selectionEnd = Math.Min(end, Math.Min(line.Start + line.Length, run.Start + run.Length));
                    if (selectionEnd <= selectionStart) continue;
                    var left = run.GetCaretPosition(selectionStart - run.Start).X;
                    var right = run.GetCaretPosition(selectionEnd - run.Start).X;
                    var width = MathF.Abs(right - left);
                    if (width > 0) rectangles.Add(new RectangleF(Math.Min(left, right), line.Origin.Y, width, line.Size.Y));
                }
            }
            return rectangles;
        }

        private static void BuildClusters(
            string text,
            List<TextLayoutLine> lines,
            List<TextLayoutRun> runs,
            Vector2[] carets,
            out ReadOnlyCollection<TextLayoutCluster> logicalClusters,
            out ReadOnlyCollection<TextLayoutCluster> visualClusters)
        {
            var boundaries = UnicodeGraphemeSegmenter.GetUtf16Boundaries(text);
            var builders = new List<ClusterBuilder>(Math.Max(0, boundaries.Length - 1));
            for (var logicalIndex = 0; logicalIndex + 1 < boundaries.Length; logicalIndex++)
            {
                var start = boundaries[logicalIndex];
                var end = boundaries[logicalIndex + 1];
                var lineIndex = FindLine(lines, start);
                var bounds = new RectangleF(carets[start].X, lines.Count == 0 ? 0 : lines[lineIndex].Origin.Y, 0, lines.Count == 0 ? 0 : lines[lineIndex].Size.Y);
                foreach (var run in runs)
                {
                    if (start < run.Start || start >= run.Start + run.Length) continue;
                    var left = run.GetCaretPosition(start - run.Start).X;
                    var right = run.GetCaretPosition(Math.Min(end, run.Start + run.Length) - run.Start).X;
                    bounds = new RectangleF(Math.Min(left, right), run.Bounds.Y, MathF.Abs(right - left), run.Bounds.Height);
                    break;
                }
                builders.Add(new ClusterBuilder(start, end - start, logicalIndex, lineIndex, bounds));
            }

            var visualBuilders = new List<ClusterBuilder>(builders);
            visualBuilders.Sort((left, right) =>
            {
                var lineComparison = left.LineIndex.CompareTo(right.LineIndex);
                if (lineComparison != 0) return lineComparison;
                var positionComparison = left.Bounds.X.CompareTo(right.Bounds.X);
                return positionComparison != 0 ? positionComparison : left.LogicalIndex.CompareTo(right.LogicalIndex);
            });
            var visualIndices = new int[builders.Count];
            for (var visualIndex = 0; visualIndex < visualBuilders.Count; visualIndex++)
                visualIndices[visualBuilders[visualIndex].LogicalIndex] = visualIndex;
            var logical = new List<TextLayoutCluster>(builders.Count);
            foreach (var builder in builders)
                logical.Add(new TextLayoutCluster(builder.Start, builder.Length, builder.LogicalIndex, visualIndices[builder.LogicalIndex], builder.LineIndex, builder.Bounds));
            var visual = new List<TextLayoutCluster>(logical.Count);
            foreach (var builder in visualBuilders) visual.Add(logical[builder.LogicalIndex]);
            logicalClusters = logical.AsReadOnly();
            visualClusters = visual.AsReadOnly();
        }

        private static int FindLine(List<TextLayoutLine> lines, int utf16Index)
        {
            if (lines.Count == 0) return 0;
            for (var index = 0; index < lines.Count; index++)
            {
                var line = lines[index];
                if (utf16Index >= line.Start && utf16Index < line.Start + line.Length) return index;
            }
            for (var index = lines.Count - 1; index >= 0; index--)
                if (utf16Index >= lines[index].Start) return index;
            return 0;
        }

        private readonly record struct ClusterBuilder(int Start, int Length, int LogicalIndex, int LineIndex, RectangleF Bounds);
    }

    public readonly struct TextLayoutEngineDiagnostics
    {
        internal TextLayoutEngineDiagnostics(int cacheEntries, long cacheHits, long cacheMisses, long layoutTicks, long shapeTicks)
        {
            CacheEntries = cacheEntries;
            CacheHits = cacheHits;
            CacheMisses = cacheMisses;
            LayoutTime = TimeSpan.FromSeconds((double)layoutTicks / Stopwatch.Frequency);
            ShapeTime = TimeSpan.FromSeconds((double)shapeTicks / Stopwatch.Frequency);
        }

        public int CacheEntries { get; }
        public long CacheHits { get; }
        public long CacheMisses { get; }
        public double CacheHitRate => CacheHits + CacheMisses == 0 ? 0 : CacheHits / (double)(CacheHits + CacheMisses);
        public TimeSpan LayoutTime { get; }
        public TimeSpan ShapeTime { get; }
    }

    public sealed class TextLayoutEngine
    {
        private const int CacheCapacity = 512;
        private readonly Dictionary<LayoutCacheKey, TextLayout> _cache = new Dictionary<LayoutCacheKey, TextLayout>();
        private readonly Queue<LayoutCacheKey> _insertionOrder = new Queue<LayoutCacheKey>();
        private long _cacheHits;
        private long _cacheMisses;
        private long _layoutTicks;
        private long _shapeTicks;

        public TextLayoutEngineDiagnostics Diagnostics => new TextLayoutEngineDiagnostics(_cache.Count, _cacheHits, _cacheMisses, _layoutTicks, _shapeTicks);

        public TextLayout Layout(UIFont font, string text, TextLayoutOptions options = default)
        {
            if (font == null) throw new ArgumentNullException(nameof(font));
            if (text == null) throw new ArgumentNullException(nameof(text));
            options = Normalize(options);
            if (options.OpenTypeFeatures.Count == 0 && font.DefaultOpenTypeFeatures.Count > 0)
                options = new TextLayoutOptions(options.MaxWidth, options.Wrapping, options.Alignment, options.Direction, options.LineSpacing, options.TabSize, options.Trimming, options.MaxVisibleCharacters, options.Locale, options.ParagraphSeparator, options.TabStops, font.DefaultOpenTypeFeatures, options.Ellipsis, options.ParagraphSpacing, options.JustificationFlags);
            var key = new LayoutCacheKey(font, font.Size, text, options);
            if (_cache.TryGetValue(key, out var cached))
            {
                _cacheHits++;
                return cached;
            }
            _cacheMisses++;
            var layoutStarted = Stopwatch.GetTimestamp();
            var shapeStarted = font.ShapeTicks;
            var layout = font.CreateLayout(text, options);
            _layoutTicks = checked(_layoutTicks + Stopwatch.GetTimestamp() - layoutStarted);
            _shapeTicks = checked(_shapeTicks + font.ShapeTicks - shapeStarted);
            if (options.ParagraphSpacing > 0 || options.Alignment == HorizontalAlignment.Fill && options.JustificationFlags != TextJustificationFlags.None)
                layout = TextLayoutAdjuster.Apply(layout);
            if (_cache.Count >= CacheCapacity)
            {
                var oldest = _insertionOrder.Dequeue();
                _cache.Remove(oldest);
            }
            _cache.Add(key, layout);
            _insertionOrder.Enqueue(key);
            return layout;
        }

        public void Clear()
        {
            _cache.Clear();
            _insertionOrder.Clear();
        }

        /// <summary>Clears detached-control and text-metrics layouts on the UI thread before releasing a font pack.</summary>
        public static void ClearSharedCaches()
        {
            Label.ClearDetachedLayoutCache();
            TextBlock.ClearDetachedInlineLayoutCache();
            TextMetrics.ClearLayoutCache();
        }

        private static TextLayoutOptions Normalize(TextLayoutOptions options) => options.LineSpacing == 0 ? TextLayoutOptions.Default : options;

        private readonly struct LayoutCacheKey : IEquatable<LayoutCacheKey>
        {
            public LayoutCacheKey(UIFont font, float size, string text, TextLayoutOptions options)
            {
                Font = font;
                Size = size;
                Text = text;
                Options = options;
            }
            private UIFont Font { get; }
            private float Size { get; }
            private string Text { get; }
            private TextLayoutOptions Options { get; }
            public bool Equals(LayoutCacheKey other) => Font.Identity == other.Font.Identity && Font.SharesLayoutResources(other.Font) && other.Font.SharesLayoutResources(Font) &&
                Size.Equals(other.Size) && string.Equals(Text, other.Text, StringComparison.Ordinal) && Options == other.Options;
            public override bool Equals(object obj) => obj is LayoutCacheKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Font.Identity, Size, StringComparer.Ordinal.GetHashCode(Text), Options);
        }
    }

    internal static class TextLayoutAdjuster
    {
        public static TextLayout Apply(TextLayout layout, float letterSpacing = 0)
        {
            if (!float.IsFinite(letterSpacing)) throw new ArgumentOutOfRangeException(nameof(letterSpacing));
            var options = layout.Options;
            var paragraphs = TextParagraphSplitter.Split(layout.Text, options.ParagraphSeparator);
            var lineParagraphs = new int[layout.Lines.Count];
            var paragraphLines = new List<List<int>>(paragraphs.Count);
            for (var index = 0; index < paragraphs.Count; index++) paragraphLines.Add(new List<int>());
            for (var lineIndex = 0; lineIndex < layout.Lines.Count; lineIndex++)
            {
                var line = layout.Lines[lineIndex];
                var paragraphIndex = 0;
                while (paragraphIndex + 1 < paragraphs.Count && line.Start >= paragraphs[paragraphIndex].Start + paragraphs[paragraphIndex].Length + options.ParagraphSeparator.Length) paragraphIndex++;
                lineParagraphs[lineIndex] = paragraphIndex;
                paragraphLines[paragraphIndex].Add(lineIndex);
            }

            var xOffsets = new float[layout.Lines.Count][];
            var yOffsets = new float[layout.Lines.Count];
            var adjustedWidths = new float[layout.Lines.Count];
            for (var lineIndex = 0; lineIndex < layout.Lines.Count; lineIndex++)
            {
                xOffsets[lineIndex] = new float[layout.Lines[lineIndex].Length + 1];
                yOffsets[lineIndex] = lineParagraphs[lineIndex] * options.ParagraphSpacing;
                adjustedWidths[lineIndex] = layout.Lines[lineIndex].Size.X;
            }
            if (options.Alignment == HorizontalAlignment.Fill && float.IsFinite(options.MaxWidth) && options.JustificationFlags.HasFlag(TextJustificationFlags.WordBound))
            {
                for (var paragraphIndex = 0; paragraphIndex < paragraphLines.Count; paragraphIndex++)
                {
                    var lineIndexes = paragraphLines[paragraphIndex];
                    var justifyCount = lineIndexes.Count;
                    if (lineIndexes.Count != 1 || !options.JustificationFlags.HasFlag(TextJustificationFlags.DoNotSkipSingleLine))
                    {
                        if (options.JustificationFlags.HasFlag(TextJustificationFlags.SkipLastLine)) justifyCount--;
                        if (options.JustificationFlags.HasFlag(TextJustificationFlags.SkipLastLineWithVisibleCharacters) && options.MaxVisibleCharacters != int.MaxValue) justifyCount--;
                    }
                    for (var localLine = 0; localLine < Math.Max(0, justifyCount); localLine++)
                    {
                        var lineIndex = lineIndexes[localLine];
                        var line = layout.Lines[lineIndex];
                        var justificationStart = line.Start;
                        if (options.TabStops.Count > 0 || options.JustificationFlags.HasFlag(TextJustificationFlags.AfterLastTab))
                        {
                            var tab = layout.Text.LastIndexOf('\t', Math.Max(line.Start, line.Start + line.Length - 1), line.Length);
                            if (tab >= line.Start) justificationStart = tab + 1;
                        }
                        var spaces = 0;
                        for (var index = justificationStart; index < line.Start + line.VisibleRange.Length; index++) if (layout.Text[index] == ' ') spaces++;
                        if (spaces == 0 || line.Size.X >= options.MaxWidth) continue;
                        var extra = (options.MaxWidth - line.Size.X) / spaces;
                        var offset = 0f;
                        var lineOffsets = xOffsets[lineIndex];
                        for (var index = line.Start; index <= line.Start + line.Length; index++)
                        {
                            lineOffsets[index - line.Start] = offset;
                            if (index < line.Start + line.Length && index >= justificationStart && layout.Text[index] == ' ') offset += extra;
                        }
                        adjustedWidths[lineIndex] = options.MaxWidth;
                    }
                }
            }
            if (letterSpacing != 0)
            {
                var boundaries = UnicodeGraphemeSegmenter.GetUtf16Boundaries(layout.Text);
                for (var lineIndex = 0; lineIndex < layout.Lines.Count; lineIndex++)
                {
                    var line = layout.Lines[lineIndex];
                    var lineEnd = line.Start + line.Length;
                    var lineOffsets = xOffsets[lineIndex];
                    var clusterCount = 0;
                    for (var boundaryIndex = 0; boundaryIndex + 1 < boundaries.Length; boundaryIndex++)
                    {
                        var start = boundaries[boundaryIndex];
                        var end = boundaries[boundaryIndex + 1];
                        if (start < line.Start || start >= lineEnd) continue;
                        var offset = clusterCount * letterSpacing;
                        for (var index = start; index < Math.Min(end, lineEnd); index++) lineOffsets[index - line.Start] += offset;
                        clusterCount++;
                    }
                    if (clusterCount > 0)
                    {
                        lineOffsets[line.Length] += (clusterCount - 1) * letterSpacing;
                        adjustedWidths[lineIndex] = MathF.Max(0, adjustedWidths[lineIndex] + (clusterCount - 1) * letterSpacing);
                    }
                }
            }

            var carets = new Vector2[layout.Text.Length + 1];
            for (var lineIndex = 0; lineIndex < layout.Lines.Count; lineIndex++)
            {
                var line = layout.Lines[lineIndex];
                var lineOffsets = xOffsets[lineIndex];
                for (var index = line.Start; index <= line.Start + line.Length; index++) carets[index] = layout.GetCaretPosition(index) + new Vector2(lineOffsets[index - line.Start], yOffsets[lineIndex]);
            }
            var lines = new List<TextLayoutLine>(layout.Lines.Count);
            for (var lineIndex = 0; lineIndex < layout.Lines.Count; lineIndex++)
            {
                var line = layout.Lines[lineIndex];
                lines.Add(new TextLayoutLine(line.Start, line.Length, line.Origin + new Vector2(0, yOffsets[lineIndex]), new Vector2(adjustedWidths[lineIndex], line.Size.Y), line.Baseline, line.VisibleRange.Length, line.Ellipsis, line.EllipsisOffset));
            }
            var runs = new List<TextLayoutRun>(layout.Runs.Count);
            foreach (var run in layout.Runs)
            {
                var lineIndex = FindLine(layout.Lines, run.Start);
                var line = layout.Lines[lineIndex];
                var lineOffsets = xOffsets[lineIndex];
                var runCarets = new Vector2[run.Length + 1];
                for (var index = 0; index < runCarets.Length; index++)
                {
                    var offsetIndex = Math.Clamp(run.Start + index - line.Start, 0, lineOffsets.Length - 1);
                    runCarets[index] = run.GetCaretPosition(index) + new Vector2(lineOffsets[offsetIndex], yOffsets[lineIndex]);
                }
                var glyphs = new List<TextLayoutGlyph>(run.Glyphs.Count);
                foreach (var glyph in run.Glyphs)
                {
                    var offsetIndex = Math.Clamp(glyph.Utf16Cluster - line.Start, 0, lineOffsets.Length - 1);
                    var shift = new Vector2(lineOffsets[offsetIndex], yOffsets[lineIndex]);
                    glyphs.Add(new TextLayoutGlyph(glyph.GlyphId, glyph.Utf16Cluster, glyph.Position + shift, glyph.Advance, glyph.Offset, new RectangleF(glyph.Bounds.X + shift.X, glyph.Bounds.Y + shift.Y, glyph.Bounds.Width, glyph.Bounds.Height), glyph.IsSynthetic));
                }
                var runWidth = runCarets.Length == 0 ? run.Bounds.Width : MathF.Abs(runCarets[^1].X - runCarets[0].X);
                var runOffsetIndex = Math.Clamp(run.Start - line.Start, 0, lineOffsets.Length - 1);
                runs.Add(new TextLayoutRun(run.Start, run.Length, run.Font, run.Direction, run.BidiLevel, glyphs, runCarets, new RectangleF(run.Bounds.X + lineOffsets[runOffsetIndex], run.Bounds.Y + yOffsets[lineIndex], runWidth, run.Bounds.Height)));
            }
            var height = layout.Size.Y + Math.Max(0, paragraphs.Count - 1) * options.ParagraphSpacing;
            var width = adjustedWidths.Length == 0 ? 0 : adjustedWidths.Max();
            return new TextLayout(layout.Text, layout.Font, options, lines, runs, carets, new Vector2(width, height), new List<TextLayoutRange>(layout.VisibleRanges), letterSpacing);
        }

        private static int FindLine(IReadOnlyList<TextLayoutLine> lines, int index)
        {
            for (var line = 0; line < lines.Count; line++)
            {
                var candidate = lines[line];
                if ((candidate.Length == 0 && index == candidate.Start) ||
                    (index >= candidate.Start && index < candidate.Start + candidate.Length)) return line;
            }
            return Math.Max(0, lines.Count - 1);
        }
    }

    internal readonly struct TextParagraphRange
    {
        public TextParagraphRange(int start, int length)
        {
            Start = start;
            Length = length;
        }

        public int Start { get; }
        public int Length { get; }
    }

    internal static class TextParagraphSplitter
    {
        internal static List<TextParagraphRange> Split(string text, string applicationSeparator)
        {
            var ranges = new List<TextParagraphRange>();
            var paragraphStart = 0;
            var index = 0;
            while (index < text.Length)
            {
                var separatorLength = GetSeparatorLength(text, index, applicationSeparator);
                if (separatorLength == 0)
                {
                    index++;
                    continue;
                }
                ranges.Add(new TextParagraphRange(paragraphStart, index - paragraphStart));
                index += separatorLength;
                paragraphStart = index;
            }
            ranges.Add(new TextParagraphRange(paragraphStart, text.Length - paragraphStart));
            return ranges;
        }

        private static int GetSeparatorLength(string text, int index, string applicationSeparator)
        {
            if (text[index] == '\r') return index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
            if (text[index] is '\n' or '\u000B' or '\u000C' or '\u0085' or '\u2028' or '\u2029') return 1;
            return applicationSeparator.Length > 0 && text.AsSpan(index).StartsWith(applicationSeparator.AsSpan(), StringComparison.Ordinal)
                ? applicationSeparator.Length
                : 0;
        }
    }

    internal static class TextTabStops
    {
        internal static float GetAdvance(float position, IReadOnlyList<float> intervals, float defaultInterval)
        {
            if (!float.IsFinite(position) || position < 0) throw new ArgumentOutOfRangeException(nameof(position));
            if (intervals.Count == 0)
            {
                if (!float.IsFinite(defaultInterval) || defaultInterval <= 0) return 0;
                return (MathF.Floor(position / defaultInterval) + 1) * defaultInterval - position;
            }
            var cycleWidth = 0f;
            foreach (var interval in intervals) cycleWidth += interval;
            var stop = MathF.Floor(position / cycleWidth) * cycleWidth;
            foreach (var interval in intervals)
            {
                stop += interval;
                if (stop > position + .001f) return stop - position;
            }
            return cycleWidth + intervals[0] - (position - MathF.Floor(position / cycleWidth) * cycleWidth);
        }
    }

    public sealed class SpriteFontAdapter : UIFont
    {
        private static readonly ConditionalWeakTable<SpriteFont, SpriteFontIdentity> Identities = new ConditionalWeakTable<SpriteFont, SpriteFontIdentity>();
        private static long _nextIdentity;

        public SpriteFontAdapter(SpriteFont spriteFont, float? size = null, UIFontWeight weight = UIFontWeight.Normal,
            UIFontStyle style = UIFontStyle.Normal, UIFontStretch stretch = UIFontStretch.Normal)
            : base(GetIdentity(spriteFont), size ?? spriteFont?.LineSpacing ?? throw new ArgumentNullException(nameof(spriteFont)), null, weight, style, stretch)
        {
            SpriteFont = spriteFont;
        }

        public SpriteFont SpriteFont { get; }
        public float Scale => Size / SpriteFont.LineSpacing;
        internal override UIFont Resize(float size) => new SpriteFontAdapter(SpriteFont, size, Weight, Style, Stretch);

        internal override TextLayout CreateLayout(string text, TextLayoutOptions options)
        {
            var lines = BuildLineRanges(text, options);
            var lineHeight = Size * options.LineSpacing;
            var carets = new Vector2[text.Length + 1];
            var output = new List<TextLayoutLine>(lines.Count);
            var maxWidth = 0f;
            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var range = lines[lineIndex];
                var display = GetDisplayRange(text, range, options);
                var visibleWidth = Measure(text, range.Start, display.VisibleLength, options).X;
                var ellipsisWidth = display.Ellipsis.Length == 0 ? 0 : Measure(display.Ellipsis, 0, display.Ellipsis.Length, options).X;
                var width = visibleWidth + ellipsisWidth;
                var originX = GetAlignmentOffset(width, options);
                var origin = new Vector2(originX, lineIndex * lineHeight);
                for (var index = 0; index <= range.Length; index++)
                    carets[range.Start + index] = origin + new Vector2(index <= display.VisibleLength ? Measure(text, range.Start, index, options).X : visibleWidth, 0);
                output.Add(new TextLayoutLine(range.Start, range.Length, origin, new Vector2(width, lineHeight), Size * .8f, display.VisibleLength, display.Ellipsis, visibleWidth));
                maxWidth = Math.Max(maxWidth, width);
            }
            var runs = new List<TextLayoutRun>(output.Count);
            foreach (var line in output)
            {
                var runCarets = new Vector2[line.Length + 1];
                Array.Copy(carets, line.Start, runCarets, 0, runCarets.Length);
                runs.Add(new TextLayoutRun(line.Start, line.Length, this, TextDirection.LeftToRight, 0, Array.Empty<TextLayoutGlyph>(), runCarets, new RectangleF(line.Origin.X, line.Origin.Y, line.Size.X, line.Size.Y)));
            }
            return new TextLayout(text, this, options, output, runs, carets, new Vector2(maxWidth, text.Length == 0 ? 0 : lines.Count * lineHeight));
        }

        internal override void Draw(UIRenderContext context, TextLayout layout, Vector2 position, Color color)
        {
            if (layout.LetterSpacing != 0)
            {
                foreach (var cluster in layout.VisualClusters)
                {
                    if (cluster.Start >= layout.VisibleRange.End || cluster.Length == 0) continue;
                    context.DrawSpriteFont(SpriteFont, layout.Text.Substring(cluster.Start, cluster.Length), position + layout.GetCaretPosition(cluster.Start), color, Scale);
                }
                return;
            }
            foreach (var line in layout.Lines)
            {
                if (line.Length == 0) continue;
                DrawLine(context, layout, line, position, color);
            }
        }

        private List<TextRange> BuildLineRanges(string text, TextLayoutOptions options)
        {
            var lines = new List<TextRange>();
            foreach (var paragraph in TextParagraphSplitter.Split(text, options.ParagraphSeparator))
                AddWrappedRanges(lines, text, paragraph.Start, paragraph.Length, options);
            if (lines.Count == 0) lines.Add(new TextRange(0, 0));
            return lines;
        }

        private void AddWrappedRanges(List<TextRange> lines, string text, int start, int length, TextLayoutOptions options)
        {
            if (length == 0 || options.Wrapping == TextWrapping.NoWrap || float.IsPositiveInfinity(options.MaxWidth))
            {
                lines.Add(new TextRange(start, length));
                return;
            }
            var end = start + length;
            while (start < end)
            {
                var fit = start + 1;
                while (fit < end && Measure(text, start, fit - start + 1, options).X <= options.MaxWidth) fit++;
                if (fit < end && options.Wrapping == TextWrapping.Word)
                {
                    var wordBreak = fit;
                    while (wordBreak > start + 1 && !char.IsWhiteSpace(text[wordBreak - 1])) wordBreak--;
                    if (wordBreak > start + 1) fit = wordBreak;
                }
                lines.Add(new TextRange(start, fit - start));
                start = fit;
            }
        }

        private Vector2 Measure(string text, int start, int length, TextLayoutOptions options)
        {
            if (length == 0) return Vector2.Zero;
            var width = 0f;
            var runStart = start;
            var end = start + length;
            for (var index = start; index < end; index++)
            {
                if (text[index] != '\t') continue;
                if (index > runStart) width += SpriteFont.MeasureString(text.Substring(runStart, index - runStart)).X * Scale;
                width += TextTabStops.GetAdvance(width, options.TabStops, SpriteFont.MeasureString(" ").X * Scale * options.TabSize);
                runStart = index + 1;
            }
            if (runStart < end) width += SpriteFont.MeasureString(text.Substring(runStart, end - runStart)).X * Scale;
            return new Vector2(width, Size);
        }

        private void DrawLine(UIRenderContext context, TextLayout layout, TextLayoutLine line, Vector2 position, Color color)
        {
            var runStart = line.Start;
            var end = Math.Min(line.VisibleRange.End, layout.VisibleRange.End);
            for (var index = line.Start; index <= end; index++)
            {
                if (index < end && !char.IsWhiteSpace(layout.Text[index])) continue;
                if (index > runStart)
                {
                    var caret = layout.GetCaretPosition(runStart);
                    context.DrawSpriteFont(SpriteFont, layout.Text.Substring(runStart, index - runStart), position + caret, color, Scale);
                }
                runStart = index + 1;
            }
            if (line.Ellipsis.Length > 0 && layout.VisibleRange.End >= line.VisibleRange.End)
                context.DrawSpriteFont(SpriteFont, line.Ellipsis, position + line.Origin + new Vector2(line.EllipsisOffset, 0), color, Scale);
        }

        private DisplayRange GetDisplayRange(string text, TextRange range, TextLayoutOptions options)
        {
            if (options.Trimming == TextTrimming.None || float.IsPositiveInfinity(options.MaxWidth) || Measure(text, range.Start, range.Length, options).X <= options.MaxWidth)
                return new DisplayRange(range.Length, string.Empty);
            var ellipsisWidth = Measure(options.Ellipsis, 0, options.Ellipsis.Length, options).X;
            if (ellipsisWidth > options.MaxWidth) return new DisplayRange(0, string.Empty);
            var line = text.Substring(range.Start, range.Length);
            var boundaries = UnicodeGraphemeSegmenter.GetUtf16Boundaries(line);
            var visibleLength = 0;
            for (var index = 1; index < boundaries.Length; index++)
            {
                var candidate = boundaries[index];
                if (Measure(text, range.Start, candidate, options).X + ellipsisWidth > options.MaxWidth) break;
                visibleLength = candidate;
            }
            if (options.Trimming == TextTrimming.WordEllipsis) visibleLength = GetWordTrimLength(line, visibleLength);
            return new DisplayRange(visibleLength, options.Ellipsis);
        }

        private static int GetWordTrimLength(string text, int maximumLength)
        {
            var boundaries = UnicodeWordBreaker.GetUtf16Boundaries(text);
            var length = 0;
            foreach (var boundary in boundaries)
            {
                if (boundary > maximumLength) break;
                length = boundary;
            }
            while (length > 0 && char.IsWhiteSpace(text[length - 1])) length--;
            return length;
        }

        private static float GetAlignmentOffset(float width, TextLayoutOptions options)
        {
            if (float.IsPositiveInfinity(options.MaxWidth)) return 0;
            return options.Alignment switch
            {
                HorizontalAlignment.Center => Math.Max(0, (options.MaxWidth - width) / 2),
                HorizontalAlignment.Right => Math.Max(0, options.MaxWidth - width),
                _ => 0,
            };
        }

        private static UIFontIdentity GetIdentity(SpriteFont spriteFont)
        {
            if (spriteFont == null) throw new ArgumentNullException(nameof(spriteFont));
            return new UIFontIdentity("spritefont", Identities.GetValue(spriteFont, _ => new SpriteFontIdentity(Interlocked.Increment(ref _nextIdentity))).Value.ToString());
        }

        private sealed class SpriteFontIdentity
        {
            public SpriteFontIdentity(long value) { Value = value; }
            public long Value { get; }
        }

        private readonly struct TextRange
        {
            public TextRange(int start, int length) { Start = start; Length = length; }
            public int Start { get; }
            public int Length { get; }
        }

        private readonly struct DisplayRange
        {
            public DisplayRange(int visibleLength, string ellipsis) { VisibleLength = visibleLength; Ellipsis = ellipsis; }
            public int VisibleLength { get; }
            public string Ellipsis { get; }
        }
    }

    /// <summary>Measures and lays out text through Forma's unified font abstraction.</summary>
    public static class TextMetrics
    {
        private static readonly TextLayoutEngine LayoutEngine = new TextLayoutEngine();
        internal static void ClearLayoutCache() => LayoutEngine.Clear();

        /// <summary>Creates a text layout for the specified font, text, and shaping options.</summary>
        public static TextLayout Layout(UIFont font, string text, TextLayoutOptions options = default)
        {
            if (font == null) throw new ArgumentNullException(nameof(font));
            if (text == null) throw new ArgumentNullException(nameof(text));
            return LayoutEngine.Layout(font, text, options);
        }

        /// <summary>Measures text using the specified UI font.</summary>
        public static Vector2 Measure(UIFont font, string text)
        {
            if (font == null) throw new ArgumentNullException(nameof(font));
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (text.Length == 0) return Vector2.Zero;
            return LayoutEngine.Layout(font, text).Size;
        }

        /// <summary>Gets the rounded-up line height for the specified UI font.</summary>
        public static int LineHeight(UIFont font)
        {
            if (font == null) throw new ArgumentNullException(nameof(font));
            return Math.Max(1, (int)MathF.Ceiling(LayoutEngine.Layout(font, "Mg").Size.Y));
        }

        /// <summary>Returns the font resized to a positive size, or the original font when no resize is needed.</summary>
        public static UIFont Resize(UIFont font, float size)
        {
            if (font == null) throw new ArgumentNullException(nameof(font));
            if (size <= 0 || Math.Abs(size - font.Size) < .0001f) return font;
            return font.Resize(size);
        }

        /// <summary>Measures text using a MonoGame sprite font at the specified positive scale.</summary>
        public static Vector2 Measure(SpriteFont font, string text, float scale = 1f)
        {
            if (font == null) throw new ArgumentNullException(nameof(font));
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (!float.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
            if (text.Length == 0) return Vector2.Zero;
            return LayoutEngine.Layout(new SpriteFontAdapter(font, font.LineSpacing * scale), text).Size;
        }
    }

    internal sealed class UIFontSelection
    {
        private SpriteFontAdapter _adapter;
        private Theme _resolvedTheme;
        private UIFont _resolvedThemeFont;
        private float _resolvedThemeSize;
        private UIFontHinting _resolvedThemeHinting;
        private IReadOnlyList<UIFontOpenTypeFeature> _resolvedThemeFeatures;
        private UIFont _resolved;

        public SpriteFont SpriteFont { get; private set; }
        public UIFont UIFont { get; private set; }
        public UIFont Effective { get; private set; }
        public UIFont Resolve(Theme theme, UIFontFamily fontFamily = null, float fontSize = 0,
            UIFontWeight weight = UIFontWeight.Normal, UIFontStyle style = UIFontStyle.Normal, UIFontStretch stretch = UIFontStretch.Normal)
        {
            var family = fontFamily ?? theme?.FontFamily;
            var localFont = Effective;
            var font = localFont ?? family?.Match(weight, style, stretch);
            if (font == null) return null;
            var size = fontSize > 0 ? fontSize : localFont != null ? font.Size : theme?.FontSize > 0 ? theme.FontSize : font.Size;
            var features = localFont?.DefaultOpenTypeFeatures ?? theme?.FontOpenTypeFeatures ?? Array.Empty<UIFontOpenTypeFeature>();
            var hinting = localFont?.RasterHinting ?? theme?.FontHinting ?? UIFontHinting.Default;
            if (ReferenceEquals(theme, _resolvedTheme) && ReferenceEquals(font, _resolvedThemeFont) && size == _resolvedThemeSize && hinting == _resolvedThemeHinting && ReferenceEquals(features, _resolvedThemeFeatures)) return _resolved;
            _resolvedTheme = theme;
            _resolvedThemeFont = font;
            _resolvedThemeSize = size;
            _resolvedThemeHinting = hinting;
            _resolvedThemeFeatures = features;
            _resolved = font.HasThemeDefaults(size, hinting, features) ? font : font.ApplyThemeDefaults(size, hinting, features);
            return _resolved;
        }

        /// <summary>Returns whether the selection actually changed, so a caller can skip a layout pass.</summary>
        public bool SetSpriteFont(SpriteFont font)
        {
            var changed = !ReferenceEquals(SpriteFont, font);
            SpriteFont = font;
            if (font == null)
            {
                changed |= _adapter != null || Effective != null;
                _adapter = null;
                Effective = null;
                return changed;
            }
            if (_adapter == null || !ReferenceEquals(_adapter.SpriteFont, font)) { _adapter = new SpriteFontAdapter(font); changed = true; }
            changed |= !ReferenceEquals(Effective, _adapter);
            Effective = _adapter;
            return changed;
        }

        /// <summary>Returns whether the selection actually changed, so a caller can skip a layout pass.</summary>
        public bool SetUIFont(UIFont font)
        {
            var changed = !ReferenceEquals(UIFont, font) || !ReferenceEquals(Effective, font);
            UIFont = font;
            Effective = font;
            return changed;
        }
    }
}