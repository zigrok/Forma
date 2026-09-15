// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// This control API and behavior are adapted from Godot Engine's Label implementation;
// see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    public enum LabelAutowrapMode { Off, Arbitrary, Word, WordSmart }
    public enum LabelTextOverrunBehavior { NoTrimming, TrimCharacters, TrimWords, Ellipsis, WordEllipsis, EllipsisForce, WordEllipsisForce }
    public enum LabelVisibleCharactersBehavior { CharactersBeforeShaping, CharactersAfterShaping, GlyphsLayoutDirection, GlyphsLeftToRight, GlyphsRightToLeft }
    [Flags]
    public enum LabelJustificationFlags
    {
        None = 0,
        Kashida = 1,
        WordBound = 2,
        AfterLastTab = 8,
        SkipLastLine = 32,
        SkipLastLineWithVisibleCharacters = 64,
        DoNotSkipSingleLine = 128,
    }

    /// <summary>Displays shaped text with alignment, wrapping, trimming, casing, and visible-character controls.</summary>
    public class Label : Control
    {
        private static readonly TextLayoutEngine DynamicLayoutEngine = new TextLayoutEngine();
        internal static void ClearDetachedLayoutCache() => DynamicLayoutEngine.Clear();
        private readonly UIFontSelection _fontSelection = new UIFontSelection();
        private string _text = string.Empty;
        private LabelAutowrapMode _autowrapMode;
        private TextDirection _textDirection = TextDirection.Auto;
        private float[] _tabStops = Array.Empty<float>();
        private IReadOnlyList<UIFontOpenTypeFeature> _openTypeFeatures = Array.Empty<UIFontOpenTypeFeature>();
        private IReadOnlyList<object> _structuredTextBidiOverrideOptions = Array.Empty<object>();
        public string Text
        {
            get => _text;
            set
            {
                if (_text == value) return;
                _text = value ?? string.Empty;
                // Godot's Label::set_text resyncs visible_chars to keep an absolute visible-character
                // count proportionally consistent with the new text's length, whenever a ratio below 1
                // is active.
                if (VisibleRatio < 1) VisibleCharacters = (int)(GetTotalCharacterCount() * VisibleRatio);
                QueueLayout();
            }
        }
        public SpriteFont Font { get => _fontSelection.SpriteFont; set { _fontSelection.SetSpriteFont(value); QueueLayout(); } }
        public UIFont UIFont { get => _fontSelection.UIFont; set { _fontSelection.SetUIFont(value); QueueLayout(); } }
        internal UIFont EffectiveUIFont => ResolveFont(_fontSelection, FontFamily, FontSize, FontWeight, FontStyle, FontStretch);
        public Color? FontColor { get => Foreground; set => Foreground = value; }
        public new HorizontalAlignment HorizontalAlignment { get; set; }
        public new VerticalAlignment VerticalAlignment { get; set; }
        /// <summary>Legacy convenience switch; true maps to Godot's WordSmart mode.</summary>
        public bool Autowrap { get => AutowrapMode != LabelAutowrapMode.Off; set => AutowrapMode = value ? LabelAutowrapMode.WordSmart : LabelAutowrapMode.Off; }
        public LabelAutowrapMode AutowrapMode { get => _autowrapMode; set { _autowrapMode = value; QueueLayout(); } }
        public LabelTextOverrunBehavior TextOverrunBehavior { get; set; }
        public string EllipsisCharacter { get; set; } = "…";
        public bool Uppercase { get; set; }
        public bool ClipText { get; set; }
        public int VisibleCharacters { get; set; } = -1;
        public float VisibleRatio { get; set; } = 1;
        public LabelVisibleCharactersBehavior VisibleCharactersBehavior { get; set; }
        public int LinesSkipped { get; set; }
        public int MaxLinesVisible { get; set; } = -1;
        public string ParagraphSeparator { get; set; } = "\n";
        public float ParagraphSpacing { get; set; }
        public TextDirection TextDirection { get => _textDirection; set { if (_textDirection == value) return; _textDirection = value; QueueLayout(); } }
        public StructuredTextParser StructuredTextBidiOverride { get; set; } = StructuredTextParser.Default;
        public LabelJustificationFlags JustificationFlags { get; set; } = LabelJustificationFlags.Kashida | LabelJustificationFlags.WordBound | LabelJustificationFlags.SkipLastLine | LabelJustificationFlags.DoNotSkipSingleLine;
        public Thickness Padding { get; set; } = new Thickness(3);
        public void SetHorizontalAlignment(HorizontalAlignment alignment) { if (!Enum.IsDefined(typeof(HorizontalAlignment), alignment)) throw new ArgumentOutOfRangeException(nameof(alignment)); HorizontalAlignment = alignment; }
        public HorizontalAlignment GetHorizontalAlignment() => HorizontalAlignment;
        public void SetVerticalAlignment(VerticalAlignment alignment) { if (!Enum.IsDefined(typeof(VerticalAlignment), alignment)) throw new ArgumentOutOfRangeException(nameof(alignment)); VerticalAlignment = alignment; }
        public VerticalAlignment GetVerticalAlignment() => VerticalAlignment;
        public void SetText(string text) => Text = text;
        public string GetText() => Text;
        public void SetAutowrapMode(LabelAutowrapMode mode) { if (!Enum.IsDefined(typeof(LabelAutowrapMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode)); AutowrapMode = mode; }
        public LabelAutowrapMode GetAutowrapMode() => AutowrapMode;
        public void SetJustificationFlags(LabelJustificationFlags flags) => JustificationFlags = flags;
        public LabelJustificationFlags GetJustificationFlags() => JustificationFlags;
        public void SetClipText(bool enable) => ClipText = enable;
        public bool IsClippingText() => ClipText;
        public void SetTextOverrunBehavior(LabelTextOverrunBehavior behavior) { if (!Enum.IsDefined(typeof(LabelTextOverrunBehavior), behavior)) throw new ArgumentOutOfRangeException(nameof(behavior)); TextOverrunBehavior = behavior; }
        public LabelTextOverrunBehavior GetTextOverrunBehavior() => TextOverrunBehavior;
        public void SetEllipsisChar(string value) => EllipsisCharacter = string.IsNullOrEmpty(value) ? "…" : value;
        public string GetEllipsisChar() => EllipsisCharacter;
        public void SetUppercase(bool enable) => Uppercase = enable;
        public bool IsUppercase() => Uppercase;
        public void SetVisibleCharacters(int amount)
        {
            // Godot's set_visible_characters performs no clamping on the derived ratio at all - an
            // amount larger than the total character count legitimately produces a ratio above 1.
            VisibleCharacters = amount;
            var total = GetTotalCharacterCount();
            VisibleRatio = amount == -1 || total == 0 ? 1 : (float)amount / total;
        }
        public int GetVisibleCharacters() => VisibleCharacters;
        public void SetVisibleRatio(float ratio)
        {
            if (ratio >= 1) { VisibleCharacters = -1; VisibleRatio = 1; }
            else if (ratio < 0) { VisibleCharacters = 0; VisibleRatio = 0; }
            else { VisibleCharacters = (int)(GetTotalCharacterCount() * ratio); VisibleRatio = ratio; }
        }
        public float GetVisibleRatio() => VisibleRatio;
        public void SetVisibleCharactersBehavior(LabelVisibleCharactersBehavior behavior) { if (!Enum.IsDefined(typeof(LabelVisibleCharactersBehavior), behavior)) throw new ArgumentOutOfRangeException(nameof(behavior)); VisibleCharactersBehavior = behavior; }
        public LabelVisibleCharactersBehavior GetVisibleCharactersBehavior() => VisibleCharactersBehavior;
        public void SetLinesSkipped(int lines) { if (lines < 0) throw new ArgumentOutOfRangeException(nameof(lines)); LinesSkipped = lines; }
        public int GetLinesSkipped() => LinesSkipped;
        public void SetMaxLinesVisible(int lines) => MaxLinesVisible = lines;
        public int GetMaxLinesVisible() => MaxLinesVisible;
        public int GetLineCount() => GetDynamicLayout()?.Lines.Count ?? GetAllLineLayouts().Count;
        public int GetVisibleLineCount()
        {
            var layout = GetDynamicLayout();
            if (layout == null) return GetDisplayLines().Count;
            var start = Math.Min(LinesSkipped, layout.Lines.Count);
            var count = MaxLinesVisible < 0 ? layout.Lines.Count - start : Math.Min(MaxLinesVisible, layout.Lines.Count - start);
            var availableHeight = Math.Max(0, Size.Y - Padding.Vertical);
            var usedHeight = 0f;
            var visible = 0;
            while (visible < count && usedHeight + layout.Lines[start + visible].Size.Y <= availableHeight)
            {
                usedHeight += layout.Lines[start + visible].Size.Y;
                visible++;
            }
            return visible;
        }
        public int GetTotalCharacterCount() => GetTextForLayout().Length;
        public void SetTextDirection(TextDirection direction) { if (!Enum.IsDefined(typeof(TextDirection), direction)) throw new ArgumentOutOfRangeException(nameof(direction)); TextDirection = direction; }
        public TextDirection GetTextDirection() => TextDirection;
        public void SetLanguage(string language) => Language = language ?? string.Empty;
        public string GetLanguage() => Language;
        // Godot's set_paragraph_separator assigns the passed string directly with no empty-string
        // special-casing - an empty separator is a legitimate way to disable paragraph splitting
        // (the whole text becomes a single paragraph).
        public void SetParagraphSeparator(string paragraphSeparator) => ParagraphSeparator = paragraphSeparator ?? string.Empty;
        public string GetParagraphSeparator() => ParagraphSeparator;
        public void SetParagraphSpacing(float spacing) => ParagraphSpacing = Math.Max(0, spacing);
        public float GetParagraphSpacing() => ParagraphSpacing;
        public void SetStructuredTextBidiOverride(StructuredTextParser parser) { if (!Enum.IsDefined(typeof(StructuredTextParser), parser)) throw new ArgumentOutOfRangeException(nameof(parser)); StructuredTextBidiOverride = parser; }
        public StructuredTextParser GetStructuredTextBidiOverride() => StructuredTextBidiOverride;
        public void SetStructuredTextBidiOverrideOptions(IEnumerable<object> options) => _structuredTextBidiOverrideOptions = options == null ? Array.Empty<object>() : new List<object>(options).ToArray();
        public IReadOnlyList<object> GetStructuredTextBidiOverrideOptions() => _structuredTextBidiOverrideOptions;
        public void SetTabStops(IEnumerable<float> tabStops) => _tabStops = tabStops == null ? Array.Empty<float>() : new List<float>(tabStops).ToArray();
        public IReadOnlyList<float> GetTabStops() => _tabStops;
        public void SetOpenTypeFeatures(IEnumerable<UIFontOpenTypeFeature> features) { _openTypeFeatures = features == null ? Array.Empty<UIFontOpenTypeFeature>() : new List<UIFontOpenTypeFeature>(features).ToArray(); QueueLayout(); }
        public IReadOnlyList<UIFontOpenTypeFeature> GetOpenTypeFeatures() => _openTypeFeatures;
        public int GetLineHeight(int line = -1)
        {
            var dynamicLayout = GetDynamicLayout();
            if (dynamicLayout != null && dynamicLayout.Lines.Count > 0)
            {
                var index = line < 0 ? 0 : Math.Min(line, dynamicLayout.Lines.Count - 1);
                return Math.Max(1, (int)MathF.Ceiling(dynamicLayout.Lines[index].Size.Y));
            }
            return Font?.LineSpacing ?? 16;
        }
        public virtual Rectangle GetCharacterBounds(int position)
        {
            var dynamicLayout = GetDynamicLayout();
            if (dynamicLayout != null) return GetDynamicCharacterBounds(dynamicLayout, position);
            var text = GetVisibleText();
            if (position < 0 || position >= text.Length) return Rectangle.Empty;
            var layouts = GetDisplayLineLayouts();
            var line = -1;
            for (var index = 0; index < layouts.Count; index++)
            {
                if (position < layouts[index].SourceStart || position >= layouts[index].SourceStart + layouts[index].Text.Length) continue;
                line = index;
                break;
            }
            if (line < 0) return Rectangle.Empty;
            var layout = layouts[line];
            var lineText = layout.Text;
            var column = position - layout.SourceStart;
            var contentWidth = Math.Max(0, Size.X - Padding.Horizontal);
            var justificationStart = GetJustificationStart(lineText, layout.GlobalIndex);
            var extraSpace = GetJustificationExtra(lineText, contentWidth, justificationStart);
            var lineWidth = MeasureLineAdvance(lineText, lineText.Length, extraSpace, justificationStart);
            var x = (float)Padding.Left;
            if (HorizontalAlignment == HorizontalAlignment.Center) x += MathF.Max(0, (contentWidth - lineWidth) / 2);
            else if (HorizontalAlignment == HorizontalAlignment.Right) x += MathF.Max(0, contentWidth - lineWidth);
            x += MeasureLineAdvance(lineText, Math.Min(column, lineText.Length), extraSpace, justificationStart);
            var y = GetLineOffsets(layouts)[line];
            var nextX = MeasureLineAdvance(lineText, Math.Min(column + 1, lineText.Length), extraSpace, justificationStart);
            var currentX = MeasureLineAdvance(lineText, Math.Min(column, lineText.Length), extraSpace, justificationStart);
            var width = Math.Max(1, (int)MathF.Ceiling(nextX - currentX));
            return new Rectangle((int)MathF.Round(x), (int)MathF.Round(y), width, GetLineHeight(layout.GlobalIndex));
        }
        public override Vector2 GetMinimumSize()
        {
            var textSize = MeasureTextBlock();
            // Godot's get_minimum_size: clipping/trimming is what lets a label shrink below its natural
            // text size in a layout - autowrap always collapses width to a nominal minimum and, when
            // clipped/trimmed, collapses height to a nominal minimum too (unless MaxLinesVisible clamps
            // it to that many lines' worth of height instead); without autowrap, clipping/trimming
            // collapses width instead.
            var trimming = ClipText || TextOverrunBehavior != LabelTextOverrunBehavior.NoTrimming;
            if (AutowrapMode != LabelAutowrapMode.Off)
            {
                if (!ClipText && TextOverrunBehavior != LabelTextOverrunBehavior.NoTrimming && MaxLinesVisible > 0)
                    textSize.Y = Math.Min(textSize.Y, ((Font?.LineSpacing ?? 0) + ParagraphSpacing) * MaxLinesVisible);
                else if (trimming) textSize.Y = 1;
                textSize.X = 0;
            }
            else if (trimming) textSize.X = 1;
            return Vector2.Max(CustomMinimumSize, textSize + new Vector2(Padding.Horizontal, Padding.Vertical));
        }
        internal override void Draw(UIRenderContext context)
        {
            if (ClipText) context.PushClip(Bounds);
            try { DrawLabelText(context); }
            finally { if (ClipText) context.PopClip(); }
            DrawLabelChildren(context);
        }
        /// <summary>Draws this label's text. Rich-text derived controls can replace this while retaining child rendering.</summary>
        protected virtual void DrawLabelText(UIRenderContext context)
        {
            var dynamicLayout = GetDynamicLayout();
            if (dynamicLayout != null)
            {
                var color = Enabled ? FontColor ?? context.Theme.TextColor : context.Theme.DisabledTextColor;
                context.Text(dynamicLayout, GlobalPosition + new Vector2(Padding.Left, Padding.Top + GetDynamicVerticalOffset(dynamicLayout)), color);
                return;
            }
            if (Font != null && !string.IsNullOrEmpty(Text))
            {
                var layouts = GetDisplayLineLayouts();
                var lines = new List<string>(); foreach (var layout in layouts) lines.Add(layout.Text);
                var lineOffsets = GetLineOffsets(layouts);
                var content = Size - new Vector2(Padding.Horizontal, Padding.Vertical);
                var color = Enabled ? FontColor ?? context.Theme.TextColor : context.Theme.DisabledTextColor;
                for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
                {
                    var line = lines[lineIndex];
                    var linePosition = GlobalPosition + new Vector2(Padding.Left, lineOffsets[lineIndex]);
                    var justificationStart = GetJustificationStart(line, layouts[lineIndex].GlobalIndex);
                    var extraSpace = GetJustificationExtra(line, content.X, justificationStart);
                    var lineWidth = MeasureLineAdvance(line, line.Length, extraSpace, justificationStart);
                    if (HorizontalAlignment == HorizontalAlignment.Center) linePosition.X += MathF.Max(0, (content.X - lineWidth) / 2);
                    else if (HorizontalAlignment == HorizontalAlignment.Right) linePosition.X += MathF.Max(0, content.X - lineWidth);
                    DrawLine(context, line, linePosition, color, extraSpace, justificationStart);
                }
            }
        }
        /// <summary>Draws retained child controls after the label's text.</summary>
        protected void DrawLabelChildren(UIRenderContext context) => base.Draw(context);
        /// <summary>Returns the text as it will be displayed after casing, visible-character, wrapping, and line-limit rules.</summary>
        public IReadOnlyList<string> GetDisplayLines()
        {
            var result = new List<string>();
            foreach (var layout in GetDisplayLineLayouts()) result.Add(layout.Text);
            return result;
        }
        private List<string> GetAllDisplayLines()
        {
            var lines = new List<string>(); foreach (var layout in GetAllLineLayouts()) lines.Add(layout.Text); return lines;
        }
        private List<LabelLineLayout> GetAllLineLayouts()
        {
            var lines = new List<LabelLineLayout>();
            var visibleText = GetVisibleText();
            var globalIndex = 0;
            var paragraphStart = 0;
            var separator = GetNormalizedParagraphSeparator();
            foreach (var paragraph in SplitParagraphs(visibleText))
            {
                var paragraphLines = GetParagraphLines(paragraph);
                var searchStart = 0;
                for (var index = 0; index < paragraphLines.Count; index++)
                {
                    var line = paragraphLines[index];
                    var sourceStart = line.Length == 0 ? searchStart : paragraph.IndexOf(line, searchStart, StringComparison.Ordinal);
                    if (sourceStart < 0) sourceStart = searchStart;
                    lines.Add(new LabelLineLayout(line, index == paragraphLines.Count - 1, globalIndex++, paragraphStart + sourceStart));
                    searchStart = sourceStart + line.Length;
                }
                paragraphStart += paragraph.Length + separator.Length;
            }
            return lines;
        }
        private List<LabelLineLayout> GetDisplayLineLayouts()
        {
            var lines = GetAllLineLayouts();
            var start = Math.Max(0, Math.Min(LinesSkipped, lines.Count));
            var count = MaxLinesVisible < 0 ? lines.Count - start : Math.Min(MaxLinesVisible, lines.Count - start);
            var contentHeight = Size.Y > Padding.Vertical ? Size.Y - Padding.Vertical : float.MaxValue;
            var usedHeight = 0f; var heightCount = 0;
            while (heightCount < count)
            {
                var index = start + heightCount;
                var addition = (float)GetLineHeight(index);
                if (heightCount > 0 && lines[index - 1].ParagraphEnd) addition += ParagraphSpacing;
                if (usedHeight + addition > contentHeight) break;
                usedHeight += addition; heightCount++;
            }
            count = heightCount;
            var texts = new List<string>(); for (var index = 0; index < lines.Count; index++) texts.Add(lines[index].Text);
            var displayed = ApplyLineWindow(texts, start, count);
            var result = new List<LabelLineLayout>();
            for (var index = 0; index < count; index++)
            {
                var source = lines[start + index];
                result.Add(new LabelLineLayout(displayed[index], source.ParagraphEnd, source.GlobalIndex, source.SourceStart));
            }
            return result;
        }
        private float GetLayoutsHeight(IReadOnlyList<LabelLineLayout> layouts)
        {
            var height = (float)(layouts.Count * GetLineHeight());
            for (var index = 0; index + 1 < layouts.Count; index++) if (layouts[index].ParagraphEnd) height += ParagraphSpacing;
            return height;
        }
        private List<float> GetLineOffsets(IReadOnlyList<LabelLineLayout> layouts)
        {
            var offsets = new List<float>();
            var contentHeight = Math.Max(0, Size.Y - Padding.Vertical);
            var spareHeight = Math.Max(0, contentHeight - GetLayoutsHeight(layouts));
            var begin = 0f;
            var extraSpacing = 0f;
            if (VerticalAlignment == VerticalAlignment.Center) begin = MathF.Floor(spareHeight / 2);
            else if (VerticalAlignment == VerticalAlignment.Bottom) begin = MathF.Floor(spareHeight);
            else if (VerticalAlignment == VerticalAlignment.Fill && layouts.Count > 1) extraSpacing = MathF.Floor(spareHeight / (layouts.Count - 1));
            var y = Padding.Top + begin;
            for (var index = 0; index < layouts.Count; index++)
            {
                offsets.Add(y);
                y += GetLineHeight(layouts[index].GlobalIndex) + extraSpacing;
                if (layouts[index].ParagraphEnd && index + 1 < layouts.Count) y += ParagraphSpacing;
            }
            return offsets;
        }
        private readonly struct LabelLineLayout
        {
            public LabelLineLayout(string text, bool paragraphEnd, int globalIndex, int sourceStart) { Text = text; ParagraphEnd = paragraphEnd; GlobalIndex = globalIndex; SourceStart = sourceStart; }
            public string Text { get; }
            public bool ParagraphEnd { get; }
            public int GlobalIndex { get; }
            public int SourceStart { get; }
        }
        private List<string> GetParagraphLines(string paragraph)
        {
            var lines = new List<string>();
            if (AutowrapMode == LabelAutowrapMode.Off || Font == null || Size.X <= Padding.Horizontal)
            {
                lines.Add(paragraph);
                return lines;
            }
            var width = Math.Max(1, Size.X - Padding.Horizontal);
            if (AutowrapMode == LabelAutowrapMode.Arbitrary)
            {
                var currentCharacters = string.Empty;
                foreach (var character in paragraph)
                {
                    var candidate = currentCharacters + character;
                    if (currentCharacters.Length > 0 && MeasureTextWidth(candidate) > width) { lines.Add(currentCharacters); currentCharacters = character.ToString(); }
                    else currentCharacters = candidate;
                }
                lines.Add(currentCharacters);
                return lines;
            }
            var current = string.Empty;
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = string.IsNullOrEmpty(current) ? word : current + " " + word;
                if (!string.IsNullOrEmpty(current) && MeasureTextWidth(candidate) > width)
                {
                    lines.Add(current);
                    current = word;
                }
                else current = candidate;
            }
            lines.Add(current);
            return lines;
        }
        private IEnumerable<string> SplitParagraphs(string text)
        {
            text = text.Replace("\r", string.Empty);
            var separator = GetNormalizedParagraphSeparator();
            // An empty separator disables paragraph splitting entirely (the whole text is one
            // paragraph), matching Godot allowing an empty paragraph_separator - handled explicitly
            // rather than relying on String.Split's edge-case behavior for an empty separator.
            if (separator.Length == 0) return new[] { text };
            return text.Split(new[] { separator }, StringSplitOptions.None);
        }
        private string GetNormalizedParagraphSeparator() => (ParagraphSeparator ?? "\n").Replace("\\n", "\n");
        private string GetTextForLayout() => Uppercase ? Text.ToUpperInvariant() : Text;
        private int MeasureTextWidth(string text) => (int)MathF.Ceiling(MeasureLineAdvance(text ?? string.Empty, text?.Length ?? 0, 0, int.MaxValue));
        private Vector2 MeasureTextBlock()
        {
            var dynamicLayout = GetDynamicLayout(useAvailableWidth: false);
            if (dynamicLayout != null) return dynamicLayout.Size;
            if (Font == null || string.IsNullOrEmpty(Text)) return Vector2.Zero;
            var size = Vector2.Zero;
            var lineCount = 0;
            foreach (var line in SplitParagraphs(GetTextForLayout()))
            {
                size.X = Math.Max(size.X, MeasureTextWidth(line));
                lineCount++;
            }
            size.Y = lineCount * Font.LineSpacing;
            return size;
        }
        private TextLayout GetDynamicLayout(bool useAvailableWidth = true)
        {
            var font = EffectiveUIFont;
            if (font == null) return null;
            var text = GetTextForLayout();
            var boundaries = UnicodeGraphemeSegmenter.GetUtf16Boundaries(text);
            var graphemeCount = Math.Max(0, boundaries.Length - 1);
            var visibleCount = VisibleCharacters >= 0
                ? Math.Min(VisibleCharacters, graphemeCount)
                : (int)MathF.Floor(graphemeCount * MathHelper.Clamp(VisibleRatio, 0, 1));
            if (VisibleCharacters < 0 && VisibleRatio >= 1) visibleCount = graphemeCount;
            var maxVisibleCharacters = visibleCount;
            if (VisibleCharactersBehavior == LabelVisibleCharactersBehavior.CharactersBeforeShaping)
            {
                text = text.Substring(0, boundaries[visibleCount]);
                maxVisibleCharacters = int.MaxValue;
            }
            var contentWidth = useAvailableWidth && Size.X > Padding.Horizontal ? Size.X - Padding.Horizontal : float.PositiveInfinity;
            var wrapping = AutowrapMode switch
            {
                LabelAutowrapMode.Arbitrary => TextWrapping.Character,
                LabelAutowrapMode.Word => TextWrapping.Word,
                LabelAutowrapMode.WordSmart => TextWrapping.Word,
                _ => TextWrapping.NoWrap
            };
            var trimming = TextOverrunBehavior switch
            {
                LabelTextOverrunBehavior.TrimCharacters => TextTrimming.CharacterEllipsis,
                LabelTextOverrunBehavior.Ellipsis => TextTrimming.CharacterEllipsis,
                LabelTextOverrunBehavior.EllipsisForce => TextTrimming.CharacterEllipsis,
                LabelTextOverrunBehavior.TrimWords => TextTrimming.WordEllipsis,
                LabelTextOverrunBehavior.WordEllipsis => TextTrimming.WordEllipsis,
                LabelTextOverrunBehavior.WordEllipsisForce => TextTrimming.WordEllipsis,
                _ => TextTrimming.None
            };
            var direction = TextDirection == TextDirection.Inherited ? TextDirection.Auto : TextDirection;
            var options = new TextLayoutOptions(
                contentWidth,
                wrapping,
                HorizontalAlignment,
                direction,
                GetTextLineSpacing(font),
                4,
                trimming,
                maxVisibleCharacters,
                Language,
                GetNormalizedParagraphSeparator(),
                GetValidTabStops(),
                _openTypeFeatures,
                ellipsis: EllipsisCharacter,
                paragraphSpacing: ParagraphSpacing,
                justificationFlags: MapJustificationFlags());
            return AdjustTextLayout((Context?.TextLayoutEngine ?? DynamicLayoutEngine).Layout(font, text, options));
        }
        protected virtual float GetTextLineSpacing(UIFont font) => 1;
        protected virtual TextLayout AdjustTextLayout(TextLayout layout) => layout;
        private TextJustificationFlags MapJustificationFlags()
        {
            var flags = TextJustificationFlags.None;
            if ((JustificationFlags & LabelJustificationFlags.WordBound) != 0) flags |= TextJustificationFlags.WordBound;
            if ((JustificationFlags & LabelJustificationFlags.AfterLastTab) != 0) flags |= TextJustificationFlags.AfterLastTab;
            if ((JustificationFlags & LabelJustificationFlags.SkipLastLine) != 0) flags |= TextJustificationFlags.SkipLastLine;
            if ((JustificationFlags & LabelJustificationFlags.SkipLastLineWithVisibleCharacters) != 0) flags |= TextJustificationFlags.SkipLastLineWithVisibleCharacters;
            if ((JustificationFlags & LabelJustificationFlags.DoNotSkipSingleLine) != 0) flags |= TextJustificationFlags.DoNotSkipSingleLine;
            return flags;
        }
        private IReadOnlyList<float> GetValidTabStops()
        {
            foreach (var stop in _tabStops)
                if (!float.IsFinite(stop) || stop <= 0) return Array.Empty<float>();
            return _tabStops;
        }
        private Rectangle GetDynamicCharacterBounds(TextLayout layout, int position)
        {
            if (position < 0 || position >= layout.Text.Length) return Rectangle.Empty;
            if (!layout.IsUtf16IndexVisible(position)) return Rectangle.Empty;
            TextLayoutLine line = null;
            foreach (var candidate in layout.Lines)
            {
                if (position < candidate.Start || position >= candidate.Start + candidate.Length) continue;
                line = candidate;
                break;
            }
            if (line == null) return Rectangle.Empty;
            var current = layout.GetCaretPosition(position);
            var next = layout.GetCaretPosition(position + 1);
            var left = Math.Min(current.X, next.X) + Padding.Left;
            var top = line.Origin.Y + Padding.Top + GetDynamicVerticalOffset(layout);
            return new Rectangle(
                (int)MathF.Floor(left),
                (int)MathF.Floor(top),
                Math.Max(1, (int)MathF.Ceiling(MathF.Abs(next.X - current.X))),
                Math.Max(1, (int)MathF.Ceiling(line.Size.Y)));
        }
        private float GetDynamicVerticalOffset(TextLayout layout)
        {
            var contentHeight = Math.Max(0, Size.Y - Padding.Vertical);
            var spare = Math.Max(0, contentHeight - layout.Size.Y);
            return VerticalAlignment switch
            {
                VerticalAlignment.Center => MathF.Floor(spare / 2),
                VerticalAlignment.Bottom => MathF.Floor(spare),
                _ => 0
            };
        }
        private float MeasureLineAdvance(string line, int characterCount, float extraSpace, int justificationStart)
        {
            if (string.IsNullOrEmpty(line) || characterCount <= 0) return 0;
            characterCount = Math.Min(characterCount, line.Length);
            if (Font == null)
            {
                var fallbackAdvance = 0f;
                var fallbackSegmentAdvance = 0f;
                var fallbackTabIndex = 0;
                for (var index = 0; index < characterCount; index++)
                {
                    if (line[index] == '\t')
                    {
                        var tabAdvance = GetTabAdvance(fallbackSegmentAdvance, ref fallbackTabIndex);
                        fallbackAdvance += tabAdvance;
                        fallbackSegmentAdvance = 0;
                    }
                    else
                    {
                        var glyphAdvance = 8f + (line[index] == ' ' && index >= justificationStart ? extraSpace : 0);
                        fallbackAdvance += glyphAdvance;
                        fallbackSegmentAdvance += glyphAdvance;
                    }
                }
                return fallbackAdvance;
            }
            var advance = 0f;
            var segmentAdvance = 0f;
            var runStart = 0;
            var tabIndex = 0;
            for (var index = 0; index < characterCount; index++)
            {
                var character = line[index];
                if (character != '\t' && (character != ' ' || index < justificationStart)) continue;
                var runLength = index - runStart + (character == ' ' ? 1 : 0);
                var runAdvance = runLength > 0 ? TextMetrics.Measure(Font, line.Substring(runStart, runLength)).X : 0;
                advance += runAdvance;
                segmentAdvance += runAdvance;
                if (character == '\t')
                {
                    var tabAdvance = GetTabAdvance(segmentAdvance, ref tabIndex);
                    advance += tabAdvance;
                    segmentAdvance = 0;
                }
                else
                {
                    advance += extraSpace;
                    segmentAdvance += extraSpace;
                }
                runStart = index + 1;
            }
            if (runStart < characterCount)
            {
                advance += TextMetrics.Measure(Font, line.Substring(runStart, characterCount - runStart)).X;
            }
            return advance;
        }
        private float GetTabAdvance(float segmentAdvance, ref int tabIndex)
        {
            var validStops = _tabStops.Length > 0;
            for (var index = 0; index < _tabStops.Length; index++) validStops &= _tabStops[index] > 0;
            if (!validStops)
            {
                var defaultWidth = Font == null ? 32 : Math.Max(1, TextMetrics.Measure(Font, "    ").X);
                return defaultWidth - segmentAdvance % defaultWidth;
            }
            var tabOffset = 0f;
            while (tabOffset <= segmentAdvance)
            {
                tabOffset += _tabStops[tabIndex];
                tabIndex = (tabIndex + 1) % _tabStops.Length;
            }
            return tabOffset - segmentAdvance;
        }
        private int GetJustificationStart(string line, int globalLineIndex)
        {
            if (HorizontalAlignment != HorizontalAlignment.Fill || (JustificationFlags & LabelJustificationFlags.WordBound) == 0) return int.MaxValue;
            var lineOffset = 0;
            var paragraphLineIndex = 0;
            List<string> paragraphLines = null;
            foreach (var paragraph in SplitParagraphs(GetVisibleText()))
            {
                paragraphLines = GetParagraphLines(paragraph);
                if (globalLineIndex < lineOffset + paragraphLines.Count)
                {
                    paragraphLineIndex = Math.Max(0, globalLineIndex - lineOffset);
                    break;
                }
                lineOffset += paragraphLines.Count;
                paragraphLines = null;
            }
            if (paragraphLines == null) return int.MaxValue;
            var justifyToLine = paragraphLines.Count;
            if (paragraphLines.Count != 1 || (JustificationFlags & LabelJustificationFlags.DoNotSkipSingleLine) == 0)
            {
                if ((JustificationFlags & LabelJustificationFlags.SkipLastLine) != 0) justifyToLine = paragraphLines.Count - 1;
                if ((JustificationFlags & LabelJustificationFlags.SkipLastLineWithVisibleCharacters) != 0)
                {
                    for (var index = paragraphLines.Count - 1; index >= 0; index--)
                    {
                        if (paragraphLines[index].Trim().Length == 0) continue;
                        justifyToLine = index;
                        break;
                    }
                }
            }
            if (paragraphLineIndex >= justifyToLine) return int.MaxValue;
            var afterLastTab = _tabStops.Length > 0 || (JustificationFlags & LabelJustificationFlags.AfterLastTab) != 0;
            return afterLastTab ? line.LastIndexOf('\t') + 1 : 0;
        }
        private float GetJustificationExtra(string line, float width, int justificationStart)
        {
            if (justificationStart == int.MaxValue) return 0;
            var spaces = 0;
            for (var index = Math.Max(0, justificationStart); index < line.Length; index++) if (line[index] == ' ') spaces++;
            return spaces == 0 ? 0 : Math.Max(0, width - MeasureTextWidth(line)) / spaces;
        }
        private void DrawLine(UIRenderContext context, string line, Vector2 position, Color color, float extraSpace, int justificationStart)
        {
            if (line.IndexOf('\t') < 0 && extraSpace <= 0) { context.Text(Font, line, position, color); return; }
            var advance = 0f;
            var segmentAdvance = 0f;
            var runStart = 0;
            var tabIndex = 0;
            for (var index = 0; index < line.Length; index++)
            {
                var character = line[index];
                if (character != '\t' && (character != ' ' || index < justificationStart)) continue;
                var runLength = index - runStart;
                if (runLength > 0)
                {
                    var run = line.Substring(runStart, runLength);
                    context.Text(Font, run, position + new Vector2(advance, 0), color);
                    var runAdvance = TextMetrics.Measure(Font, run).X;
                    advance += runAdvance;
                    segmentAdvance += runAdvance;
                }
                if (character == '\t')
                {
                    var tabAdvance = GetTabAdvance(segmentAdvance, ref tabIndex);
                    advance += tabAdvance;
                    segmentAdvance = 0;
                }
                else
                {
                    var spaceAdvance = TextMetrics.Measure(Font, " ").X + extraSpace;
                    advance += spaceAdvance;
                    segmentAdvance += spaceAdvance;
                }
                runStart = index + 1;
            }
            if (runStart < line.Length) context.Text(Font, line.Substring(runStart), position + new Vector2(advance, 0), color);
        }
        private string GetVisibleText()
        {
            var text = GetTextForLayout();
            // Godot only substrs the text itself for VC_CHARS_BEFORE_SHAPING; every other behavior
            // shapes/wraps the FULL text and hides characters per-glyph at draw time instead (out of
            // scope for this port, which doesn't model glyph-level rendering) - so line count, wrapping,
            // and minimum size must still be computed from the full text for those behaviors.
            if (VisibleCharactersBehavior != LabelVisibleCharactersBehavior.CharactersBeforeShaping) return text;
            var count = VisibleCharacters >= 0 ? VisibleCharacters : (int)MathF.Floor(text.Length * MathHelper.Clamp(VisibleRatio, 0, 1));
            if (VisibleCharacters < 0 && VisibleRatio >= 1) count = text.Length;
            return text.Substring(0, Math.Max(0, Math.Min(text.Length, count)));
        }
        private IReadOnlyList<string> ApplyLineWindow(List<string> lines)
        {
            var start = Math.Max(0, Math.Min(LinesSkipped, lines.Count));
            var count = MaxLinesVisible < 0 ? lines.Count - start : Math.Min(MaxLinesVisible, lines.Count - start);
            return ApplyLineWindow(lines, start, count);
        }
        private IReadOnlyList<string> ApplyLineWindow(List<string> lines, int start, int count)
        {
            if (TextOverrunBehavior != LabelTextOverrunBehavior.NoTrimming && AutowrapMode == LabelAutowrapMode.Off && Font != null && Size.X > Padding.Horizontal)
            {
                var width = Size.X - Padding.Horizontal;
                for (var i = start; i < start + count; i++) lines[i] = TrimLine(lines[i], width);
            }
            // Godot forces an ellipsis on the last visible line when autowrap produced more lines than
            // MaxLinesVisible allows, EVEN when TextOverrunBehavior is NoTrimming (the default) -
            // signaling that text continues beyond what's shown.
            if (AutowrapMode != LabelAutowrapMode.Off && count > 0 && start + count < lines.Count && Font != null)
            {
                var lastIndex = start + count - 1;
                var width = Size.X > Padding.Horizontal ? Size.X - Padding.Horizontal : float.MaxValue;
                lines[lastIndex] = TrimLine(lines[lastIndex], width, forceEllipsis: true);
            }
            return lines.GetRange(start, count);
        }
        private string TrimLine(string source, float width, bool forceEllipsis = false)
        {
            if (!forceEllipsis && MeasureTextWidth(source) <= width) return source;
            var ellipsis = !forceEllipsis && (TextOverrunBehavior == LabelTextOverrunBehavior.TrimCharacters || TextOverrunBehavior == LabelTextOverrunBehavior.TrimWords) ? string.Empty : EllipsisCharacter;
            var candidate = source;
            while (candidate.Length > 0 && MeasureTextWidth(candidate + ellipsis) > width)
            {
                var cut = candidate.Length - 1;
                if (TextOverrunBehavior == LabelTextOverrunBehavior.TrimWords || TextOverrunBehavior == LabelTextOverrunBehavior.WordEllipsis || TextOverrunBehavior == LabelTextOverrunBehavior.WordEllipsisForce) cut = candidate.LastIndexOf(' ', Math.Max(0, cut - 1));
                if (cut < 0) break;
                candidate = candidate.Substring(0, cut).TrimEnd();
            }
            return candidate + ellipsis;
        }
    }

}
