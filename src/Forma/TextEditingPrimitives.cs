// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// Text-editing contracts and highlighting behavior are adapted from Godot Engine's
// LineEdit, TextEdit, SyntaxHighlighter, and CodeHighlighter implementations;
// see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    [Flags]
    public enum TextSearchFlags { None = 0, MatchCase = 1, WholeWords = 2, Backwards = 4 }
    public enum TextEditGutterType { String, Icon, Custom }
    /// <summary>Godot-compatible source-line wrapping policy for <see cref="TextEdit"/>.</summary>
    public enum TextEditLineWrappingMode { None, Boundary }
    /// <summary>Godot-compatible grouping categories for retained TextEdit history operations.</summary>
    public enum TextEditEditAction { None, Typing, Backspace, Delete }
    /// <summary>Core retained TextEdit context-menu commands, corresponding to Godot's TextEdit menu IDs.</summary>
    public enum TextEditMenuOption
    {
        Cut, Copy, Paste, Clear, SelectAll, Undo, Redo,
        SubmenuTextDirection, DirectionInherited, DirectionAuto, DirectionLeftToRight, DirectionRightToLeft,
        DisplayControlCharacters, SubmenuInsertControlCharacter,
        InsertLeftToRightMark, InsertRightToLeftMark, InsertLeftToRightEmbedding, InsertRightToLeftEmbedding,
        InsertLeftToRightOverride, InsertRightToLeftOverride, InsertPopDirectionFormatting, InsertArabicLetterMark,
        InsertLeftToRightIsolate, InsertRightToLeftIsolate, InsertFirstStrongIsolate, InsertPopDirectionIsolate,
        InsertZeroWidthJoiner, InsertZeroWidthNonJoiner, InsertWordJoiner, InsertSoftHyphen
    }
    /// <summary>Core retained LineEdit context-menu commands, corresponding to Godot's LineEdit menu IDs.</summary>
    public enum LineEditMenuOption
    {
        Cut, Copy, Paste, Clear, SelectAll, Undo, Redo,
        SubmenuTextDirection, DirectionInherited, DirectionAuto, DirectionLeftToRight, DirectionRightToLeft,
        DisplayControlCharacters, SubmenuInsertControlCharacter,
        InsertLeftToRightMark, InsertRightToLeftMark, InsertLeftToRightEmbedding, InsertRightToLeftEmbedding,
        InsertLeftToRightOverride, InsertRightToLeftOverride, InsertPopDirectionFormatting, InsertArabicLetterMark,
        InsertLeftToRightIsolate, InsertRightToLeftIsolate, InsertFirstStrongIsolate, InsertPopDirectionIsolate,
        InsertZeroWidthJoiner, InsertZeroWidthNonJoiner, InsertWordJoiner, InsertSoftHyphen
    }
    public enum StructuredTextParser { Default, Uri, File, Email, List, None, Custom }

    /// <summary>Snapshot of one TextEdit caret, using Godot's source-line and source-column coordinates.</summary>
    public readonly struct TextEditCaret
    {
        public TextEditCaret(int line, int column, int selectionOriginLine, int selectionOriginColumn)
        {
            Line = line; Column = column; SelectionOriginLine = selectionOriginLine; SelectionOriginColumn = selectionOriginColumn;
        }
        public int Line { get; }
        public int Column { get; }
        public int SelectionOriginLine { get; }
        public int SelectionOriginColumn { get; }
    }

    /// <summary>A colored source range returned by a <see cref="SyntaxHighlighter"/> for one document line.</summary>
    public readonly struct SyntaxHighlightSpan
    {
        public SyntaxHighlightSpan(int startColumn, int length, Color color)
        {
            StartColumn = Math.Max(0, startColumn);
            Length = Math.Max(0, length);
            Color = color;
        }
        public int StartColumn { get; }
        public int Length { get; }
        public Color Color { get; }
    }

    /// <summary>One configurable <see cref="CodeHighlighter"/> color region, analogous to Godot's color_regions dictionary entry.</summary>
    public readonly struct CodeHighlightColorRegion
    {
        public CodeHighlightColorRegion(string startKey, string endKey, Color color, bool lineOnly = false)
        {
            StartKey = startKey ?? string.Empty;
            EndKey = endKey ?? string.Empty;
            Color = color;
            LineOnly = lineOnly || string.IsNullOrEmpty(EndKey);
        }
        public string StartKey { get; }
        public string EndKey { get; }
        public Color Color { get; }
        public bool LineOnly { get; }
    }

    /// <summary>
    /// Document-bound syntax-color provider analogous to Godot's SyntaxHighlighter resource.
    /// Override <see cref="GetLineSyntaxHighlightingCore"/> to return colored source ranges for a line.
    /// </summary>
    public abstract class SyntaxHighlighter
    {
        private readonly Dictionary<int, IReadOnlyList<SyntaxHighlightSpan>> _highlightingCache = new Dictionary<int, IReadOnlyList<SyntaxHighlightSpan>>();
        private TextEdit _textEdit;
        /// <summary>Raised when cached highlighting was invalidated and the owning editor should redraw.</summary>
        public event EventHandler Changed;
        public TextEdit TextEdit => _textEdit;
        public IReadOnlyList<SyntaxHighlightSpan> GetLineSyntaxHighlighting(int line)
        {
            if (_textEdit == null || line < 0 || line >= _textEdit.LineCount) return Array.Empty<SyntaxHighlightSpan>();
            if (_highlightingCache.TryGetValue(line, out var cached)) return cached;
            var highlighting = GetLineSyntaxHighlightingCore(line) ?? Array.Empty<SyntaxHighlightSpan>();
            _highlightingCache[line] = highlighting;
            return highlighting;
        }
        public void ClearHighlightingCache()
        {
            _highlightingCache.Clear();
            OnClearHighlightingCache();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        public void UpdateCache()
        {
            _highlightingCache.Clear();
            OnClearHighlightingCache();
            OnUpdateCache();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        internal void InvalidateFromLine(int line)
        {
            foreach (var cachedLine in new List<int>(_highlightingCache.Keys))
                if (cachedLine >= line) _highlightingCache.Remove(cachedLine);
            OnInvalidateFromLine(line);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        protected abstract IReadOnlyList<SyntaxHighlightSpan> GetLineSyntaxHighlightingCore(int line);
        protected virtual void OnClearHighlightingCache() { }
        protected virtual void OnUpdateCache() { }
        protected virtual void OnInvalidateFromLine(int line) { }
        internal void SetTextEdit(TextEdit textEdit)
        {
            if (_textEdit == textEdit) return;
            _textEdit = textEdit;
            UpdateCache();
        }
    }

    /// <summary>
    /// Basic configurable code highlighter corresponding to Godot's CodeHighlighter.
    /// It supports word/member colors and one-line or paired multiline color regions.
    /// </summary>
    public sealed class CodeHighlighter : SyntaxHighlighter
    {
        private sealed class ColorRegion
        {
            public string StartKey;
            public string EndKey;
            public Color Color;
            public bool LineOnly;
        }
        private readonly List<ColorRegion> _colorRegions = new List<ColorRegion>();
        private readonly Dictionary<string, Color> _keywordColors = new Dictionary<string, Color>(StringComparer.Ordinal);
        private readonly Dictionary<string, Color> _memberKeywordColors = new Dictionary<string, Color>(StringComparer.Ordinal);
        private readonly Dictionary<int, int> _colorRegionCache = new Dictionary<int, int>();
        private Color _numberColor = Color.Transparent;
        private Color _symbolColor = Color.Transparent;
        private Color _functionColor = Color.Transparent;
        private Color _memberVariableColor = Color.Transparent;
        private bool _uintSuffixEnabled;
        public Color NumberColor { get => _numberColor; set { if (_numberColor == value) return; _numberColor = value; UpdateCache(); } }
        public Color SymbolColor { get => _symbolColor; set { if (_symbolColor == value) return; _symbolColor = value; UpdateCache(); } }
        public Color FunctionColor { get => _functionColor; set { if (_functionColor == value) return; _functionColor = value; UpdateCache(); } }
        public Color MemberVariableColor { get => _memberVariableColor; set { if (_memberVariableColor == value) return; _memberVariableColor = value; UpdateCache(); } }
        public bool UIntSuffixEnabled { get => _uintSuffixEnabled; set { if (_uintSuffixEnabled == value) return; _uintSuffixEnabled = value; UpdateCache(); } }
        public void AddKeywordColor(string keyword, Color color) { if (string.IsNullOrEmpty(keyword)) throw new ArgumentException("A keyword is required.", nameof(keyword)); _keywordColors[keyword] = color; UpdateCache(); }
        public void RemoveKeywordColor(string keyword) { if (_keywordColors.Remove(keyword ?? string.Empty)) UpdateCache(); }
        public bool HasKeywordColor(string keyword) => !string.IsNullOrEmpty(keyword) && _keywordColors.ContainsKey(keyword);
        public Color GetKeywordColor(string keyword) => _keywordColors.TryGetValue(keyword ?? string.Empty, out var color) ? color : Color.Transparent;
        public IReadOnlyDictionary<string, Color> GetKeywordColors() => new Dictionary<string, Color>(_keywordColors);
        public void SetKeywordColors(IDictionary<string, Color> colors) { _keywordColors.Clear(); if (colors != null) foreach (var pair in colors) if (!string.IsNullOrEmpty(pair.Key)) _keywordColors[pair.Key] = pair.Value; UpdateCache(); }
        public void ClearKeywordColors() { if (_keywordColors.Count == 0) return; _keywordColors.Clear(); UpdateCache(); }
        public void AddMemberKeywordColor(string keyword, Color color) { if (string.IsNullOrEmpty(keyword)) throw new ArgumentException("A member keyword is required.", nameof(keyword)); _memberKeywordColors[keyword] = color; UpdateCache(); }
        public void RemoveMemberKeywordColor(string keyword) { if (_memberKeywordColors.Remove(keyword ?? string.Empty)) UpdateCache(); }
        public bool HasMemberKeywordColor(string keyword) => !string.IsNullOrEmpty(keyword) && _memberKeywordColors.ContainsKey(keyword);
        public Color GetMemberKeywordColor(string keyword) => _memberKeywordColors.TryGetValue(keyword ?? string.Empty, out var color) ? color : Color.Transparent;
        public IReadOnlyDictionary<string, Color> GetMemberKeywordColors() => new Dictionary<string, Color>(_memberKeywordColors);
        public void SetMemberKeywordColors(IDictionary<string, Color> colors) { _memberKeywordColors.Clear(); if (colors != null) foreach (var pair in colors) if (!string.IsNullOrEmpty(pair.Key)) _memberKeywordColors[pair.Key] = pair.Value; UpdateCache(); }
        public void ClearMemberKeywordColors() { if (_memberKeywordColors.Count == 0) return; _memberKeywordColors.Clear(); UpdateCache(); }
        public void AddColorRegion(string startKey, string endKey, Color color, bool lineOnly = false)
        {
            AddColorRegionInternal(startKey, endKey, color, lineOnly);
            UpdateCache();
        }
        public void RemoveColorRegion(string startKey) { if (RemoveColorRegionInternal(startKey)) UpdateCache(); }
        public bool HasColorRegion(string startKey) { foreach (var region in _colorRegions) if (region.StartKey == startKey) return true; return false; }
        public IReadOnlyList<CodeHighlightColorRegion> GetColorRegions()
        {
            var regions = new List<CodeHighlightColorRegion>(_colorRegions.Count);
            foreach (var region in _colorRegions) regions.Add(new CodeHighlightColorRegion(region.StartKey, region.EndKey, region.Color, region.LineOnly));
            return regions;
        }
        public void SetColorRegions(IEnumerable<CodeHighlightColorRegion> regions)
        {
            _colorRegions.Clear();
            if (regions != null) foreach (var region in regions) AddColorRegionInternal(region.StartKey, region.EndKey, region.Color, region.LineOnly);
            UpdateCache();
        }
        public void ClearColorRegions() { if (_colorRegions.Count == 0) return; _colorRegions.Clear(); UpdateCache(); }
        public void SetNumberColor(Color color) => NumberColor = color;
        public Color GetNumberColor() => NumberColor;
        public void SetSymbolColor(Color color) => SymbolColor = color;
        public Color GetSymbolColor() => SymbolColor;
        public void SetFunctionColor(Color color) => FunctionColor = color;
        public Color GetFunctionColor() => FunctionColor;
        public void SetMemberVariableColor(Color color) => MemberVariableColor = color;
        public Color GetMemberVariableColor() => MemberVariableColor;
        public void SetUIntSuffixEnabled(bool enabled) => UIntSuffixEnabled = enabled;
        public bool IsUIntSuffixEnabled() => UIntSuffixEnabled;
        protected override void OnClearHighlightingCache() => _colorRegionCache.Clear();
        protected override void OnInvalidateFromLine(int line)
        {
            foreach (var cachedLine in new List<int>(_colorRegionCache.Keys))
                if (cachedLine >= line) _colorRegionCache.Remove(cachedLine);
        }
        protected override IReadOnlyList<SyntaxHighlightSpan> GetLineSyntaxHighlightingCore(int line)
        {
            var text = TextEdit.GetLine(line);
            var spans = new List<SyntaxHighlightSpan>();
            var activeRegion = GetActiveRegionAtLineStart(line);
            var column = 0;
            while (column < text.Length)
            {
                if (activeRegion >= 0)
                {
                    var region = _colorRegions[activeRegion];
                    var end = string.IsNullOrEmpty(region.EndKey) ? -1 : text.IndexOf(region.EndKey, column, StringComparison.Ordinal);
                    var last = end < 0 ? text.Length : end + region.EndKey.Length;
                    AddSpan(spans, column, last - column, region.Color);
                    if (end < 0) { _colorRegionCache[line] = region.LineOnly ? -1 : activeRegion; return spans; }
                    activeRegion = -1; column = last; continue;
                }
                var enteringRegion = FindRegionAt(text, column);
                if (enteringRegion >= 0)
                {
                    var region = _colorRegions[enteringRegion];
                    var afterStart = column + region.StartKey.Length;
                    var end = string.IsNullOrEmpty(region.EndKey) ? -1 : text.IndexOf(region.EndKey, afterStart, StringComparison.Ordinal);
                    var last = end < 0 ? text.Length : end + region.EndKey.Length;
                    AddSpan(spans, column, last - column, region.Color);
                    if (end < 0) { _colorRegionCache[line] = region.LineOnly ? -1 : enteringRegion; return spans; }
                    column = last; continue;
                }
                if (IsWordCharacter(text[column]))
                {
                    var start = column; while (column < text.Length && IsWordCharacter(text[column])) column++;
                    var word = text.Substring(start, column - start);
                    var memberAccess = start > 0 && text[start - 1] == '.';
                    if (_keywordColors.TryGetValue(word, out var keywordColor)) AddSpan(spans, start, word.Length, keywordColor);
                    else if (!memberAccess && _memberKeywordColors.TryGetValue(word, out var memberKeywordColor)) AddSpan(spans, start, word.Length, memberKeywordColor);
                    else if (memberAccess && MemberVariableColor != Color.Transparent) AddSpan(spans, start, word.Length, MemberVariableColor);
                    else if (IsNumericToken(word) && NumberColor != Color.Transparent) AddSpan(spans, start, word.Length, NumberColor);
                    else if (NextNonWhitespaceIsOpenParenthesis(text, column) && FunctionColor != Color.Transparent) AddSpan(spans, start, word.Length, FunctionColor);
                    continue;
                }
                if (!char.IsWhiteSpace(text[column]) && SymbolColor != Color.Transparent) AddSpan(spans, column, 1, SymbolColor);
                column++;
            }
            _colorRegionCache[line] = -1;
            return spans;
        }
        private int GetActiveRegionAtLineStart(int line)
        {
            if (line <= 0) return -1;
            GetLineSyntaxHighlighting(line - 1);
            return _colorRegionCache.TryGetValue(line - 1, out var region) ? region : -1;
        }
        private int FindRegionAt(string text, int column)
        {
            for (var index = 0; index < _colorRegions.Count; index++)
            {
                var start = _colorRegions[index].StartKey;
                if (column + start.Length <= text.Length && string.CompareOrdinal(text, column, start, 0, start.Length) == 0) return index;
            }
            return -1;
        }
        private bool RemoveColorRegionInternal(string startKey)
        {
            for (var index = _colorRegions.Count - 1; index >= 0; index--) if (_colorRegions[index].StartKey == startKey) { _colorRegions.RemoveAt(index); return true; }
            return false;
        }
        private void AddColorRegionInternal(string startKey, string endKey, Color color, bool lineOnly)
        {
            if (string.IsNullOrEmpty(startKey)) throw new ArgumentException("A region start key is required.", nameof(startKey));
            if (!IsSymbolSequence(startKey)) throw new ArgumentException("Color region keys must contain only symbol characters.", nameof(startKey));
            endKey ??= string.Empty;
            if (!string.IsNullOrEmpty(endKey) && !IsSymbolSequence(endKey)) throw new ArgumentException("Color region keys must contain only symbol characters.", nameof(endKey));
            if (HasColorRegion(startKey)) throw new ArgumentException("A color region with the same start key already exists.", nameof(startKey));
            var index = 0; while (index < _colorRegions.Count && _colorRegions[index].StartKey.Length >= startKey.Length) index++;
            _colorRegions.Insert(index, new ColorRegion { StartKey = startKey, EndKey = endKey, Color = color, LineOnly = lineOnly || string.IsNullOrEmpty(endKey) });
        }
        private static void AddSpan(List<SyntaxHighlightSpan> spans, int start, int length, Color color) { if (length > 0 && color != Color.Transparent) spans.Add(new SyntaxHighlightSpan(start, length, color)); }
        private static bool IsWordCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';
        private static bool IsSymbolSequence(string text) { foreach (var character in text) if (char.IsLetterOrDigit(character) || character == '_' || char.IsWhiteSpace(character)) return false; return true; }
        private static bool NextNonWhitespaceIsOpenParenthesis(string text, int column) { while (column < text.Length && char.IsWhiteSpace(text[column])) column++; return column < text.Length && text[column] == '('; }
        private bool IsNumericToken(string word)
        {
            if (string.IsNullOrEmpty(word) || !char.IsDigit(word[0])) return false;
            var end = word.Length;
            if (word[end - 1] == 'u') { if (!UIntSuffixEnabled) return false; end--; }
            for (var index = 0; index < end; index++)
            {
                var character = word[index];
                if (char.IsDigit(character) || character == '_' || character == '.' || character == 'x' || character == 'X' || character == 'e' || character == 'E' || character == 'f' || (index > 1 && (character >= 'a' && character <= 'f' || character >= 'A' && character <= 'F'))) continue;
                return false;
            }
            return end > 0;
        }
    }

    /// <summary>Configuration for one of a <see cref="TextEdit"/>'s left-side gutter columns.</summary>
    public sealed class TextEditGutter
    {
        internal TextEditGutter() { }
        public string Name { get; internal set; } = string.Empty;
        public TextEditGutterType Type { get; internal set; }
        public int Width { get; internal set; } = 24;
        public bool Draw { get; internal set; } = true;
        public bool Clickable { get; internal set; }
        public bool Overwritable { get; internal set; }
        public Action<UIRenderContext, TextEdit, int, Rectangle> CustomDraw { get; internal set; }
    }

}
