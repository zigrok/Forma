// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;

namespace Forma
{
    public sealed class DynamicUIFont : UIFont
    {
        private readonly ReadOnlyCollection<UIFontFace> _fallbackFaces;
        private readonly ReadOnlyCollection<UIFontVariationCoordinate> _variationCoordinates;
        private readonly Dictionary<UIFontIdentity, DynamicUIFont> _resolvedFonts = new Dictionary<UIFontIdentity, DynamicUIFont>();
        private long _shapeTicks;

        public DynamicUIFont(UIFontFace face, float size, UIFontHinting hinting = UIFontHinting.Default, params UIFontFace[] fallbackFaces)
            : this(face, size, hinting, Array.Empty<UIFontVariationCoordinate>(), fallbackFaces)
        {
        }

        public DynamicUIFont(UIFontFace face, float size, UIFontHinting hinting, IReadOnlyList<UIFontVariationCoordinate> variationCoordinates, params UIFontFace[] fallbackFaces)
            : this(face, size, hinting, variationCoordinates, Array.Empty<UIFontOpenTypeFeature>(), fallbackFaces)
        {
        }

        private DynamicUIFont(UIFontFace face, float size, UIFontHinting hinting, IReadOnlyList<UIFontVariationCoordinate> variationCoordinates, IReadOnlyList<UIFontOpenTypeFeature> defaultOpenTypeFeatures, params UIFontFace[] fallbackFaces)
            : base(CreateIdentity(face, hinting, variationCoordinates, fallbackFaces), size, defaultOpenTypeFeatures)
        {
            Face = face;
            Hinting = hinting;
            _variationCoordinates = ValidateVariationCoordinates(face, variationCoordinates).AsReadOnly();
            var values = new List<UIFontFace>();
            var identities = new HashSet<UIFontIdentity> { face.Identity };
            if (fallbackFaces != null)
                foreach (var fallback in fallbackFaces)
                {
                    if (fallback == null) throw new ArgumentException("Fallback faces cannot contain null entries.", nameof(fallbackFaces));
                    if (!identities.Add(fallback.Identity)) continue;
                    if (values.Count >= 16) throw new ArgumentException("A font fallback family supports at most 16 fallback faces.", nameof(fallbackFaces));
                    values.Add(fallback);
                }
            _fallbackFaces = values.AsReadOnly();
            _resolvedFonts.Add(face.Identity, this);
        }

        public UIFontFace Face { get; }
        public UIFontHinting Hinting { get; }
        public IReadOnlyList<UIFontFace> FallbackFaces => _fallbackFaces;
        public IReadOnlyList<UIFontVariationCoordinate> VariationCoordinates => _variationCoordinates;
        internal override UIFont Resize(float size) => new DynamicUIFont(Face, size, Hinting, VariationCoordinates, DefaultOpenTypeFeatures, new List<UIFontFace>(FallbackFaces).ToArray());
        internal override bool HasThemeDefaults(float size, UIFontHinting hinting, IReadOnlyList<UIFontOpenTypeFeature> features) =>
            Math.Abs(size - Size) < .0001f && hinting == Hinting && DefaultOpenTypeFeatures.SequenceEqual(features);
        internal override UIFont ApplyThemeDefaults(float size, UIFontHinting hinting, IReadOnlyList<UIFontOpenTypeFeature> features) =>
            new DynamicUIFont(Face, size, hinting, VariationCoordinates, features, new List<UIFontFace>(FallbackFaces).ToArray());
        internal override UIFontHinting RasterHinting => Hinting;
        internal override long ShapeTicks => Interlocked.Read(ref _shapeTicks);
        internal override bool SharesLayoutResources(UIFont other)
        {
            if (other is not DynamicUIFont font || !ReferenceEquals(Face, font.Face) || FallbackFaces.Count != font.FallbackFaces.Count)
                return false;
            for (var index = 0; index < FallbackFaces.Count; index++)
                if (!ReferenceEquals(FallbackFaces[index], font.FallbackFaces[index])) return false;
            return true;
        }
        internal override UIFontGlyphBitmap RasterizeGlyph(uint glyphId, float displayScale) => Face.RasterizeGlyph(glyphId, Size, displayScale, Hinting, VariationCoordinates);

        internal override TextLayout CreateLayout(string text, TextLayoutOptions options)
        {
            var metrics = Face.GetMetrics(Size);
            var lineHeight = metrics.LineHeight * options.LineSpacing;
            var baseline = metrics.Ascender;
            var lines = new List<TextLayoutLine>();
            var runs = new List<TextLayoutRun>();
            var carets = new Vector2[text.Length + 1];
            var maxWidth = 0f;
            var lineIndex = 0;
            foreach (var paragraphRange in TextParagraphSplitter.Split(text, options.ParagraphSeparator))
            {
                var paragraphStart = paragraphRange.Start;
                var paragraph = text.Substring(paragraphStart, paragraphRange.Length);
                var bidi = UnicodeBidiResolver.Resolve(paragraph, GetParagraphDirection(options.Direction));
                foreach (var range in GetLineRanges(paragraph, options, bidi))
                {
                    var lineText = paragraph.Substring(range.Start, range.Length);
                    var scalarStart = CountScalars(paragraph.AsSpan(0, range.Start));
                    var display = GetDisplayLine(lineText, options, bidi, scalarStart);
                    var segments = display.SourceSegments;
                    var width = display.SourceWidth + display.EllipsisWidth;
                    var originX = GetAlignmentOffset(width, options);
                    var originY = lineIndex * lineHeight;
                    var rightToLeft = (bidi.ParagraphLevel & 1) != 0;
                    var penX = originX + (display.IsTrimmed && rightToLeft ? display.EllipsisWidth : 0);
                    foreach (var segment in segments)
                    {
                        var glyphs = new List<TextLayoutGlyph>(segment.Run.Glyphs.Count);
                        var runOriginX = penX;
                        var runAdvance = GetSegmentAdvance(segment, penX - originX, options);
                        if (segment.IsTab)
                        {
                            penX += runAdvance;
                        }
                        else foreach (var glyph in segment.Run.Glyphs)
                        {
                            var glyphPosition = new Vector2(penX + glyph.OffsetX, originY + baseline - glyph.OffsetY);
                            var glyphMetrics = segment.Font.Face.GetGlyphMetrics(glyph.GlyphId, segment.Font.Size, segment.Font.VariationCoordinates);
                            glyphs.Add(new TextLayoutGlyph(
                                glyph.GlyphId,
                                paragraphStart + range.Start + segment.Start + glyph.Utf16Cluster,
                                glyphPosition,
                                new Vector2(glyph.AdvanceX, glyph.AdvanceY),
                                new Vector2(glyph.OffsetX, glyph.OffsetY),
                                new RectangleF(
                                    glyphPosition.X + glyphMetrics.BearingX,
                                    glyphPosition.Y - glyphMetrics.BearingY,
                                    glyphMetrics.Width,
                                    glyphMetrics.Height)));
                            penX += glyph.AdvanceX;
                        }
                        var runCarets = segment.IsTab
                            ? BuildEmptyCarets(carets, paragraphStart + range.Start + segment.Start, segment.Length, runOriginX, runAdvance, originY, segment.Run.Direction)
                            : BuildCarets(carets, paragraphStart + range.Start + segment.Start, segment.Length, segment.Run, runOriginX, originY);
                        runs.Add(new TextLayoutRun(
                            paragraphStart + range.Start + segment.Start,
                            segment.Length,
                            segment.Font,
                            segment.Run.Direction,
                            segment.Removed ? (byte?)null : segment.Level,
                            glyphs,
                            runCarets,
                            new RectangleF(runOriginX, originY, runAdvance, lineHeight)));
                    }
                    var trimIndex = paragraphStart + range.Start + display.VisibleLength;
                    var trimPosition = new Vector2(rightToLeft ? originX + display.EllipsisWidth : originX + display.SourceWidth, originY);
                    if (display.IsTrimmed)
                    {
                        var hiddenLength = range.Length - display.VisibleLength;
                        var hiddenCarets = new Vector2[hiddenLength + 1];
                        for (var index = 0; index < hiddenCarets.Length; index++) hiddenCarets[index] = carets[trimIndex + index] = trimPosition;
                        if (hiddenLength > 0)
                            runs.Add(new TextLayoutRun(trimIndex, hiddenLength, this, GetTextDirection(bidi.ParagraphLevel), bidi.ParagraphLevel, Array.Empty<TextLayoutGlyph>(), hiddenCarets, new RectangleF(trimPosition.X, originY, 0, lineHeight)));
                        AddSyntheticEllipsisRuns(runs, display.EllipsisSegments, trimIndex, rightToLeft ? originX : originX + display.SourceWidth, originY, baseline, lineHeight);
                    }
                    lines.Add(new TextLayoutLine(paragraphStart + range.Start, range.Length, new Vector2(originX, originY), new Vector2(width, lineHeight), baseline, display.VisibleLength, display.IsTrimmed ? options.Ellipsis : string.Empty, display.SourceWidth));
                    maxWidth = Math.Max(maxWidth, width);
                    lineIndex++;
                    if (lineIndex > 100_000) throw new ArgumentOutOfRangeException(nameof(text), "Layout exceeds the 100,000-line limit.");
                }
            }
            return new TextLayout(text, this, options, lines, runs, carets, new Vector2(maxWidth, text.Length == 0 ? 0 : lineIndex * lineHeight));
        }

        internal override void Draw(UIRenderContext context, TextLayout layout, Vector2 position, Color color)
        {
            context.BeginDynamicGlyphs();
            try
            {
                for (var runIndex = 0; runIndex < layout.Runs.Count; runIndex++)
                {
                    var run = layout.Runs[runIndex];
                    var font = (DynamicUIFont)run.Font;
                    for (var glyphIndex = 0; glyphIndex < run.Glyphs.Count; glyphIndex++)
                    {
                        var glyph = run.Glyphs[glyphIndex];
                        if (!layout.IsVisible(glyph)) continue;
                        context.DrawDynamicGlyph(font, glyph.GlyphId, position + glyph.Position, color);
                    }
                }
            }
            finally { context.EndDynamicGlyphs(); }
        }

        private DynamicDisplayLine GetDisplayLine(string text, TextLayoutOptions options, UnicodeBidiResult bidi, int scalarStart)
        {
            var sourceSegments = ShapeSegments(text, options, bidi, scalarStart);
            var sourceWidth = GetAdvance(sourceSegments, options);
            if (options.Trimming == TextTrimming.None || float.IsPositiveInfinity(options.MaxWidth) || sourceWidth <= options.MaxWidth)
                return new DynamicDisplayLine(text.Length, sourceSegments, sourceWidth, new List<ShapedSegment>(), 0, false);
            var ellipsisBidi = UnicodeBidiResolver.Resolve(options.Ellipsis, (bidi.ParagraphLevel & 1) == 0 ? BidiParagraphDirection.LeftToRight : BidiParagraphDirection.RightToLeft);
            var ellipsisSegments = ShapeSegments(options.Ellipsis, options, ellipsisBidi, 0);
            var ellipsisWidth = GetAdvance(ellipsisSegments, options);
            if (ellipsisWidth > options.MaxWidth)
                return new DynamicDisplayLine(0, new List<ShapedSegment>(), 0, new List<ShapedSegment>(), 0, true);
            var boundaries = UnicodeGraphemeSegmenter.GetUtf16Boundaries(text);
            var low = 0;
            var high = boundaries.Length - 1;
            var fittingBoundary = 0;
            var fittingSegments = new List<ShapedSegment>();
            var fittingWidth = 0f;
            while (low <= high)
            {
                var middle = low + (high - low) / 2;
                var candidateLength = boundaries[middle];
                var candidateSegments = ShapeSegments(text.Substring(0, candidateLength), options, bidi, scalarStart);
                var candidateWidth = GetAdvance(candidateSegments, options);
                if (candidateWidth + ellipsisWidth <= options.MaxWidth)
                {
                    fittingBoundary = candidateLength;
                    fittingSegments = candidateSegments;
                    fittingWidth = candidateWidth;
                    low = middle + 1;
                }
                else high = middle - 1;
            }
            if (options.Trimming == TextTrimming.WordEllipsis)
            {
                fittingBoundary = GetDynamicWordTrimLength(text, fittingBoundary);
                fittingSegments = ShapeSegments(text.Substring(0, fittingBoundary), options, bidi, scalarStart);
                fittingWidth = GetAdvance(fittingSegments, options);
            }
            return new DynamicDisplayLine(fittingBoundary, fittingSegments, fittingWidth, ellipsisSegments, ellipsisWidth, true);
        }

        private void AddSyntheticEllipsisRuns(List<TextLayoutRun> runs, List<ShapedSegment> segments, int sourceIndex, float originX, float originY, float baseline, float lineHeight)
        {
            var penX = originX;
            foreach (var segment in segments)
            {
                var glyphs = new List<TextLayoutGlyph>(segment.Run.Glyphs.Count);
                var runOriginX = penX;
                foreach (var glyph in segment.Run.Glyphs)
                {
                    var glyphPosition = new Vector2(penX + glyph.OffsetX, originY + baseline - glyph.OffsetY);
                    var glyphMetrics = segment.Font.Face.GetGlyphMetrics(glyph.GlyphId, segment.Font.Size, segment.Font.VariationCoordinates);
                    glyphs.Add(new TextLayoutGlyph(glyph.GlyphId, sourceIndex, glyphPosition, new Vector2(glyph.AdvanceX, glyph.AdvanceY), new Vector2(glyph.OffsetX, glyph.OffsetY), new RectangleF(glyphPosition.X + glyphMetrics.BearingX, glyphPosition.Y - glyphMetrics.BearingY, glyphMetrics.Width, glyphMetrics.Height), true));
                    penX += glyph.AdvanceX;
                }
                runs.Add(new TextLayoutRun(sourceIndex, 0, segment.Font, segment.Run.Direction, segment.Level, glyphs, new[] { new Vector2(runOriginX, originY) }, new RectangleF(runOriginX, originY, penX - runOriginX, lineHeight)));
            }
        }

        private static int GetDynamicWordTrimLength(string text, int maximumLength)
        {
            var length = 0;
            foreach (var boundary in UnicodeWordBreaker.GetUtf16Boundaries(text))
            {
                if (boundary > maximumLength) break;
                length = boundary;
            }
            while (length > 0 && char.IsWhiteSpace(text[length - 1])) length--;
            return length;
        }

        private static UIFontIdentity CreateIdentity(UIFontFace face, UIFontHinting hinting, IReadOnlyList<UIFontVariationCoordinate> variationCoordinates, UIFontFace[] fallbackFaces)
        {
            if (face == null) throw new ArgumentNullException(nameof(face));
            if (!Enum.IsDefined(typeof(UIFontHinting), hinting)) throw new ArgumentOutOfRangeException(nameof(hinting));
            var value = $"{face.Identity.Value}:{hinting}";
            foreach (var variation in ValidateVariationCoordinates(face, variationCoordinates))
                value += $":{variation.Tag}={BitConverter.SingleToInt32Bits(variation.Value):X8}";
            var identities = new HashSet<UIFontIdentity> { face.Identity };
            var count = 0;
            if (fallbackFaces != null)
                foreach (var fallback in fallbackFaces)
                {
                    if (fallback == null || !identities.Add(fallback.Identity)) continue;
                    if (count++ >= 16) throw new ArgumentException("A font fallback family supports at most 16 fallback faces.", nameof(fallbackFaces));
                    value += $":{fallback.Identity.Value}";
                }
            return new UIFontIdentity("dynamic-font", value);
        }

        private static List<UIFontVariationCoordinate> ValidateVariationCoordinates(UIFontFace face, IReadOnlyList<UIFontVariationCoordinate> variationCoordinates)
        {
            if (variationCoordinates == null) throw new ArgumentNullException(nameof(variationCoordinates));
            var values = new List<UIFontVariationCoordinate>(variationCoordinates.Count);
            var tags = new HashSet<string>(StringComparer.Ordinal);
            foreach (var variation in variationCoordinates)
            {
                if (variation.Tag == null) throw new ArgumentException("Variation coordinates must be initialized.", nameof(variationCoordinates));
                if (!tags.Add(variation.Tag)) throw new ArgumentException($"Variation axis '{variation.Tag}' is configured more than once.", nameof(variationCoordinates));
                UIFontVariationAxis? matchingAxis = null;
                foreach (var axis in face.VariationAxes)
                    if (string.Equals(axis.Tag, variation.Tag, StringComparison.Ordinal))
                    {
                        matchingAxis = axis;
                        break;
                    }
                if (matchingAxis == null) throw new ArgumentException($"Font face does not define variation axis '{variation.Tag}'.", nameof(variationCoordinates));
                if (variation.Value < matchingAxis.Value.Minimum || variation.Value > matchingAxis.Value.Maximum)
                    throw new ArgumentOutOfRangeException(nameof(variationCoordinates), $"Variation '{variation.Tag}' must be between {matchingAxis.Value.Minimum} and {matchingAxis.Value.Maximum}.");
                values.Add(variation);
            }
            values.Sort((left, right) => StringComparer.Ordinal.Compare(left.Tag, right.Tag));
            return values;
        }

        private IReadOnlyList<UIFontVariationCoordinate> GetApplicableVariations(UIFontFace face)
        {
            if (face.Identity == Face.Identity) return VariationCoordinates;
            var values = new List<UIFontVariationCoordinate>();
            foreach (var variation in VariationCoordinates)
                foreach (var axis in face.VariationAxes)
                    if (string.Equals(axis.Tag, variation.Tag, StringComparison.Ordinal) && variation.Value >= axis.Minimum && variation.Value <= axis.Maximum)
                    {
                        values.Add(variation);
                        break;
                    }
            return values;
        }

        private static float GetAlignmentOffset(float width, TextLayoutOptions options)
        {
            if (float.IsPositiveInfinity(options.MaxWidth)) return 0;
            return options.Alignment switch
            {
                HorizontalAlignment.Center => Math.Max(0, (options.MaxWidth - width) / 2),
                HorizontalAlignment.Right => Math.Max(0, options.MaxWidth - width),
                _ => 0
            };
        }

        private List<DynamicTextRange> GetLineRanges(string paragraph, TextLayoutOptions options, UnicodeBidiResult bidi)
        {
            if (paragraph.Length == 0) return new List<DynamicTextRange> { new DynamicTextRange(0, 0) };
            if (options.Wrapping == TextWrapping.NoWrap || float.IsPositiveInfinity(options.MaxWidth))
                return new List<DynamicTextRange> { new DynamicTextRange(0, paragraph.Length) };

            var segments = ShapeSegments(paragraph, options, bidi, 0);
            var widths = new Dictionary<int, float>();
            var tabs = new Dictionary<int, ShapedSegment>();
            foreach (var segment in segments)
            {
                if (segment.IsTab) tabs[segment.Start] = segment;
                foreach (var glyph in segment.Run.Glyphs)
                {
                    var cluster = Math.Clamp(segment.Start + glyph.Utf16Cluster, 0, paragraph.Length);
                    widths.TryGetValue(cluster, out var width);
                    widths[cluster] = width + glyph.AdvanceX;
                }
            }
            var boundaries = new List<int>(UnicodeGraphemeSegmenter.GetUtf16Boundaries(paragraph));
            var lineBreaks = options.Wrapping == TextWrapping.Word
                ? new HashSet<int>(UnicodeLineBreaker.GetUtf16BreakOpportunities(paragraph))
                : null;

            var ranges = new List<DynamicTextRange>();
            var lineStart = 0;
            while (lineStart < boundaries.Count - 1)
            {
                var cursor = lineStart;
                var lineWidth = 0f;
                var lastWordBreak = -1;
                while (cursor < boundaries.Count - 1)
                {
                    var clusterStart = boundaries[cursor];
                    var clusterWidth = tabs.TryGetValue(clusterStart, out var tab)
                        ? GetSegmentAdvance(tab, lineWidth, options)
                        : widths.GetValueOrDefault(clusterStart);
                    if (cursor > lineStart && lineWidth + clusterWidth > options.MaxWidth) break;
                    lineWidth += clusterWidth;
                    cursor++;
                    var clusterEnd = boundaries[cursor];
                    if (lineBreaks != null && lineBreaks.Contains(clusterEnd)) lastWordBreak = cursor;
                }
                var lineEnd = cursor;
                if (options.Wrapping == TextWrapping.Word && lastWordBreak > lineStart && cursor < boundaries.Count - 1) lineEnd = lastWordBreak;
                if (lineEnd <= lineStart) lineEnd = lineStart + 1;
                ranges.Add(new DynamicTextRange(boundaries[lineStart], boundaries[lineEnd] - boundaries[lineStart]));
                lineStart = lineEnd;
            }
            return ranges;
        }

        private static float GetAdvance(UIFontShapedRun shaped)
        {
            var width = 0f;
            foreach (var glyph in shaped.Glyphs) width += glyph.AdvanceX;
            return width;
        }

        private float GetAdvance(IEnumerable<ShapedSegment> segments, TextLayoutOptions options)
        {
            var width = 0f;
            foreach (var segment in segments) width += GetSegmentAdvance(segment, width, options);
            return width;
        }

        private float GetSegmentAdvance(ShapedSegment segment, float position, TextLayoutOptions options)
        {
            if (!segment.IsTab) return GetAdvance(segment.Run);
            var space = MeasureShape(() => segment.Font.Face.Shape(" ", segment.Font.Size, TextDirection.LeftToRight, options.Locale, null, options.OpenTypeFeatures, segment.Font.VariationCoordinates));
            return TextTabStops.GetAdvance(position, options.TabStops, GetAdvance(space) * options.TabSize);
        }

        private List<ShapedSegment> ShapeSegments(string text, TextLayoutOptions options, UnicodeBidiResult bidi, int scalarStart)
        {
            if (text.Length == 0)
            {
                var level = bidi.ParagraphLevel;
                var direction = GetTextDirection(level);
                return new List<ShapedSegment> { new ShapedSegment(0, 0, level, false, false, this, MeasureShape(() => Face.Shape(text, Size, direction, options.Locale, null, options.OpenTypeFeatures, VariationCoordinates))) };
            }
            var boundaries = UnicodeGraphemeSegmenter.GetUtf16Boundaries(text);
            var scripts = UnicodeScriptResolver.ResolveGraphemeScripts(text, boundaries);
            var scalarOffsets = GetScalarOffsets(text);
            var levels = bidi.GetLineLevels(scalarStart, scalarOffsets.Count - 1);
            var segments = new List<ShapedSegment>();
            var segmentStart = boundaries[0];
            var selectedFace = SelectFace(text, segmentStart, boundaries[1] - segmentStart);
            var selectedScript = scripts[0];
            var selectedLevel = GetLevel(levels, scalarOffsets, segmentStart, bidi.ParagraphLevel);
            var selectedRemoved = IsRemoved(levels, scalarOffsets, segmentStart);
            var selectedInvisible = IsBidiControl(text.Substring(segmentStart, boundaries[1] - segmentStart));
            var selectedTab = text.AsSpan(segmentStart, boundaries[1] - segmentStart).SequenceEqual("\t");
            for (var index = 1; index < boundaries.Length - 1; index++)
            {
                var start = boundaries[index];
                var end = boundaries[index + 1];
                var face = SelectFace(text, start, end - start);
                var script = scripts[index];
                var level = GetLevel(levels, scalarOffsets, start, bidi.ParagraphLevel);
                var removed = IsRemoved(levels, scalarOffsets, start);
                var invisible = IsBidiControl(text.Substring(start, end - start));
                var tab = text.AsSpan(start, end - start).SequenceEqual("\t");
                if (face.Identity == selectedFace.Identity && script == selectedScript && level == selectedLevel && removed == selectedRemoved && invisible == selectedInvisible && tab == selectedTab) continue;
                AddSegment(segments, text, segmentStart, start - segmentStart, selectedFace, selectedScript, selectedLevel, selectedRemoved, selectedInvisible, selectedTab, options);
                segmentStart = start;
                selectedFace = face;
                selectedScript = script;
                selectedLevel = level;
                selectedRemoved = removed;
                selectedInvisible = invisible;
                selectedTab = tab;
            }
            AddSegment(segments, text, segmentStart, text.Length - segmentStart, selectedFace, selectedScript, selectedLevel, selectedRemoved, selectedInvisible, selectedTab, options);
            ReorderSegments(segments);
            return segments;
        }

        private void AddSegment(List<ShapedSegment> segments, string text, int start, int length, UIFontFace face, string script, byte level, bool removed, bool invisible, bool tab, TextLayoutOptions options)
        {
            if (!_resolvedFonts.TryGetValue(face.Identity, out var font))
            {
                font = new DynamicUIFont(face, Size, Hinting, GetApplicableVariations(face));
                _resolvedFonts.Add(face.Identity, font);
            }
            var direction = GetTextDirection(level);
            var segmentText = text.Substring(start, length);
            var run = removed || invisible || tab
                ? new UIFontShapedRun(segmentText, direction, new List<UIFontShapedGlyph>())
                : MeasureShape(() => face.Shape(segmentText, Size, direction, options.Locale, script, options.OpenTypeFeatures, font.VariationCoordinates));
            segments.Add(new ShapedSegment(start, length, level, removed, tab, font, run));
        }

        private UIFontShapedRun MeasureShape(Func<UIFontShapedRun> shape)
        {
            var started = Stopwatch.GetTimestamp();
            try { return shape(); }
            finally { Interlocked.Add(ref _shapeTicks, Stopwatch.GetTimestamp() - started); }
        }

        private static void ReorderSegments(List<ShapedSegment> segments)
        {
            var removed = segments.FindAll(segment => segment.Removed);
            segments.RemoveAll(segment => segment.Removed);
            var highest = 0;
            var lowestOdd = int.MaxValue;
            foreach (var segment in segments)
            {
                highest = Math.Max(highest, segment.Level);
                if ((segment.Level & 1) != 0) lowestOdd = Math.Min(lowestOdd, segment.Level);
            }
            for (var level = highest; level >= lowestOdd; level--)
            {
                for (var position = 0; position < segments.Count;)
                {
                    if (segments[position].Level < level)
                    {
                        position++;
                        continue;
                    }
                    var start = position;
                    while (position < segments.Count && segments[position].Level >= level) position++;
                    segments.Reverse(start, position - start);
                }
            }
            segments.AddRange(removed);
        }

        private static bool IsBidiControl(string text)
        {
            var remaining = text.AsSpan();
            while (!remaining.IsEmpty)
            {
                var status = Rune.DecodeFromUtf16(remaining, out var rune, out var consumed);
                if (status != OperationStatus.Done || rune.Value is not (0x061C or 0x200E or 0x200F or >= 0x202A and <= 0x202E or >= 0x2066 and <= 0x2069))
                    return false;
                remaining = remaining.Slice(consumed);
            }
            return true;
        }

        private static byte GetLevel(byte?[] levels, List<int> scalarOffsets, int utf16Offset, byte paragraphLevel)
        {
            var scalar = scalarOffsets.BinarySearch(utf16Offset);
            if (scalar < 0) scalar = ~scalar - 1;
            return levels[Math.Clamp(scalar, 0, levels.Length - 1)] ?? paragraphLevel;
        }

        private static bool IsRemoved(byte?[] levels, List<int> scalarOffsets, int utf16Offset)
        {
            var scalar = scalarOffsets.BinarySearch(utf16Offset);
            if (scalar < 0) scalar = ~scalar - 1;
            return levels[Math.Clamp(scalar, 0, levels.Length - 1)] == null;
        }

        private static List<int> GetScalarOffsets(string text)
        {
            var offsets = new List<int> { 0 };
            var offset = 0;
            while (offset < text.Length)
            {
                var status = Rune.DecodeFromUtf16(text.AsSpan(offset), out _, out var consumed);
                offset += status == OperationStatus.Done ? consumed : 1;
                offsets.Add(offset);
            }
            return offsets;
        }

        private static int CountScalars(ReadOnlySpan<char> text)
        {
            var count = 0;
            while (!text.IsEmpty)
            {
                var status = Rune.DecodeFromUtf16(text, out _, out var consumed);
                text = text.Slice(status == OperationStatus.Done ? consumed : 1);
                count++;
            }
            return count;
        }

        private static BidiParagraphDirection GetParagraphDirection(TextDirection direction) => direction switch
        {
            TextDirection.LeftToRight => BidiParagraphDirection.LeftToRight,
            TextDirection.RightToLeft => BidiParagraphDirection.RightToLeft,
            _ => BidiParagraphDirection.AutoLeftToRight
        };

        private static TextDirection GetTextDirection(byte level) => (level & 1) == 0 ? TextDirection.LeftToRight : TextDirection.RightToLeft;

        private UIFontFace SelectFace(string text, int start, int length)
        {
            if (CoversCluster(Face, text, start, length)) return Face;
            foreach (var fallback in _fallbackFaces)
                if (CoversCluster(fallback, text, start, length)) return fallback;
            return Face;
        }

        private static bool CoversCluster(UIFontFace face, string text, int start, int length)
        {
            var remaining = text.AsSpan(start, length);
            while (!remaining.IsEmpty)
            {
                var status = Rune.DecodeFromUtf16(remaining, out var rune, out var consumed);
                if (status != OperationStatus.Done) return false;
                var value = rune.Value;
                if (value != 0x200D && !IsVariationSelector(value) && !face.SupportsCharacter(value)) return false;
                remaining = remaining.Slice(consumed);
            }
            return true;
        }

        private static bool IsVariationSelector(int value) => value is >= 0xFE00 and <= 0xFE0F or >= 0xE0100 and <= 0xE01EF;

        private static Vector2[] BuildCarets(Vector2[] carets, int start, int length, UIFontShapedRun shaped, float originX, float originY)
        {
            var result = new Vector2[length + 1];
            if (length == 0)
            {
                result[0] = new Vector2(originX, originY);
                carets[start] = result[0];
                return result;
            }
            if (shaped.Glyphs.Count == 0)
            {
                for (var index = 0; index <= length; index++) result[index] = carets[start + index] = new Vector2(originX, originY);
                return result;
            }
            var clusterBounds = new Dictionary<int, Vector2>();
            var penX = originX;
            foreach (var glyph in shaped.Glyphs)
            {
                var cluster = Math.Clamp(glyph.Utf16Cluster, 0, length);
                var nextPenX = penX + glyph.AdvanceX;
                var minimum = Math.Min(penX, nextPenX);
                var maximum = Math.Max(penX, nextPenX);
                if (clusterBounds.TryGetValue(cluster, out var bounds))
                    clusterBounds[cluster] = new Vector2(Math.Min(bounds.X, minimum), Math.Max(bounds.Y, maximum));
                else
                    clusterBounds.Add(cluster, new Vector2(minimum, maximum));
                penX = nextPenX;
            }
            var ordered = new List<int>(clusterBounds.Keys);
            ordered.Sort();
            for (var clusterIndex = 0; clusterIndex < ordered.Count; clusterIndex++)
            {
                var clusterStart = ordered[clusterIndex];
                var clusterEnd = clusterIndex + 1 < ordered.Count ? ordered[clusterIndex + 1] : length;
                var bounds = clusterBounds[clusterStart];
                var from = shaped.Direction == TextDirection.RightToLeft ? bounds.Y : bounds.X;
                var to = shaped.Direction == TextDirection.RightToLeft ? bounds.X : bounds.Y;
                for (var index = clusterStart; index <= clusterEnd; index++)
                {
                    var amount = (index - clusterStart) / (float)Math.Max(1, clusterEnd - clusterStart);
                    result[index] = new Vector2(from + (to - from) * amount, originY);
                }
            }
            for (var index = 0; index <= length; index++) carets[start + index] = result[index];
            return result;
        }

        private static Vector2[] BuildEmptyCarets(Vector2[] carets, int start, int length, float originX, float advance, float originY, TextDirection direction)
        {
            var result = new Vector2[length + 1];
            var from = direction == TextDirection.RightToLeft ? originX + advance : originX;
            var to = direction == TextDirection.RightToLeft ? originX : originX + advance;
            for (var index = 0; index <= length; index++)
            {
                var amount = index / (float)Math.Max(1, length);
                result[index] = carets[start + index] = new Vector2(from + (to - from) * amount, originY);
            }
            return result;
        }

        private readonly struct DynamicTextRange
        {
            public DynamicTextRange(int start, int length) { Start = start; Length = length; }
            public int Start { get; }
            public int Length { get; }
        }

        private readonly struct DynamicDisplayLine
        {
            public DynamicDisplayLine(int visibleLength, List<ShapedSegment> sourceSegments, float sourceWidth, List<ShapedSegment> ellipsisSegments, float ellipsisWidth, bool isTrimmed)
            {
                VisibleLength = visibleLength;
                SourceSegments = sourceSegments;
                SourceWidth = sourceWidth;
                EllipsisSegments = ellipsisSegments;
                EllipsisWidth = ellipsisWidth;
                IsTrimmed = isTrimmed;
            }
            public int VisibleLength { get; }
            public List<ShapedSegment> SourceSegments { get; }
            public float SourceWidth { get; }
            public List<ShapedSegment> EllipsisSegments { get; }
            public float EllipsisWidth { get; }
            public bool IsTrimmed { get; }
        }

        private readonly struct ShapedSegment
        {
            public ShapedSegment(int start, int length, byte level, bool removed, bool isTab, DynamicUIFont font, UIFontShapedRun run)
            {
                Start = start;
                Length = length;
                Level = level;
                Removed = removed;
                IsTab = isTab;
                Font = font;
                Run = run;
            }
            public int Start { get; }
            public int Length { get; }
            public byte Level { get; }
            public bool Removed { get; }
            public bool IsTab { get; }
            public DynamicUIFont Font { get; }
            public UIFontShapedRun Run { get; }
        }
    }
}