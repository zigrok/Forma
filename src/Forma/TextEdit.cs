// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// This control API and behavior are adapted from Godot Engine's TextEdit implementation;
// see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    /// <summary>Edits multiline text with wrapping, multiple carets, gutters, syntax highlighting, and undo history.</summary>
    public class TextEdit : LineEdit
    {
        internal override bool SupportsNativeTextComposition => false;
        internal override void TextInput(string text)
        {
            foreach (var character in text) TextInput(character);
        }
        /// <summary>Gets whether the first programmatic text assignment moves the caret to the end.</summary>
        protected override bool MoveCaretToEndOnInitialTextAssignment => false;
        private readonly List<string> _undoStack = new List<string>();
        private readonly List<string> _redoStack = new List<string>();
        private readonly List<uint> _undoVersions = new List<uint>();
        private readonly List<uint> _redoVersions = new List<uint>();
        private readonly Dictionary<int, Color> _lineBackgroundColors = new Dictionary<int, Color>();
        private readonly List<TextEditGutter> _gutters = new List<TextEditGutter>();
        private readonly Dictionary<(int Line, int Gutter), TextEditGutterItem> _lineGutterItems = new Dictionary<(int Line, int Gutter), TextEditGutterItem>();
        private string _historyText = string.Empty;
        private bool _restoringHistory;
        private SyntaxHighlighter _syntaxHighlighter;
        private TextEditLineWrappingMode _lineWrappingMode;
        private int _wrapAtColumn;
        private string _wrapLayoutText = null;
        private float _wrapLayoutWidth = -1;
        private UIFontIdentity _wrapLayoutFontIdentity;
        private float _wrapLayoutFontSize;
        private string _wrapLayoutLanguage = string.Empty;
        private TextDirection _wrapLayoutDirection;
        private readonly Dictionary<int, List<TextEditWrapSegment>> _wrapLayoutCache = new Dictionary<int, List<TextEditWrapSegment>>();
        internal int WrapLayoutBuildCount { get; private set; }
        private readonly List<SecondaryCaret> _secondaryCarets = new List<SecondaryCaret>();
        private readonly PopupMenu _contextMenu;
        private bool _multipleCaretsEnabled = true;
        private bool _updatingMultipleCarets;
        private int _caretMergeSuspension;
        private string _cutCopyLine = string.Empty;
        private TextEditEditAction _currentAction;
        private string _actionStartText = string.Empty;
        private uint _actionStartVersion;
        private bool _actionChanged;
        private uint _version;
        private uint _savedVersion;
        /// <summary>Extra horizontal space reserved by specialized text editors before their editable content.</summary>
        protected virtual float TextContentLeftInset => GetTotalGutterWidth();
        public TextEdit()
        {
            TextChanged += TrackTextChange;
            TextChanged += (_, _) => { if (!_updatingMultipleCarets) _secondaryCarets.Clear(); };
            _contextMenu = new PopupMenu { Visible = false };
            _contextMenu.IdPressed += (_, id) => MenuOption((TextEditMenuOption)id);
            _historyText = Text;
        }
        /// <summary>Compatibility alias for enabling or disabling <see cref="LineWrappingMode"/>.</summary>
        public bool WrapMode { get => LineWrappingMode != TextEditLineWrappingMode.None; set => SetLineWrappingMode(value ? TextEditLineWrappingMode.Boundary : TextEditLineWrappingMode.None); }
        public TextEditLineWrappingMode LineWrappingMode { get => _lineWrappingMode; set => SetLineWrappingMode(value); }
        /// <summary>Optional deterministic wrap width in source columns; zero derives a width from the editor viewport.</summary>
        public int WrapAtColumn { get => _wrapAtColumn; set { var column = Math.Max(0, value); if (_wrapAtColumn == column) return; _wrapAtColumn = column; InvalidateWrapLayout(); QueueLayout(); } }
        public new bool UndoEnabled { get; set; } = true;
        public new int UndoStackMaxSize { get; set; } = 50;
        /// <summary>Enables Control/Command copy, cut, paste, and select-all dispatch, matching Godot's <c>shortcut_keys_enabled</c>.</summary>
        public new bool ShortcutKeysEnabled { get => base.ShortcutKeysEnabled; set => base.ShortcutKeysEnabled = value; }
        /// <summary>Allows copying/cutting whole caret line ranges when no caret has a selection, matching Godot's <c>empty_selection_clipboard_enabled</c>.</summary>
        public bool EmptySelectionClipboardEnabled { get; set; } = true;
        /// <summary>Optional per-control override used by <see cref="Paste(int)"/> before <see cref="UIContext.Clipboard"/>.</summary>
        public new Func<TextEdit, string> ClipboardTextProvider { get; set; }
        /// <summary>First visible source line, corresponding to Godot's get_first_visible_line().</summary>
        public int FirstVisibleLine { get; private set; }
        /// <summary>Visual wrap-row offset within <see cref="FirstVisibleLine"/>.</summary>
        public int FirstVisibleLineWrapIndex { get; private set; }
        public string SearchText { get; private set; } = string.Empty;
        public TextSearchFlags SearchFlags { get; private set; }
        public new bool HasUndo => _undoStack.Count > 0;
        public new bool HasRedo => _redoStack.Count > 0;
        public TextEditEditAction CurrentAction => _currentAction;
        public int GutterCount => _gutters.Count;
        /// <summary>Enables Godot-style secondary caret creation and multi-caret text input.</summary>
        public bool MultipleCaretsEnabled { get => _multipleCaretsEnabled; set { _multipleCaretsEnabled = value; if (!value) RemoveSecondaryCarets(); } }
        public int CaretCount => 1 + _secondaryCarets.Count;
        public event Action<TextEdit, int, int> GutterClicked;
        /// <summary>Raised after Copy or Cut submits text to <see cref="UIContext.Clipboard"/>.</summary>
        public new event Action<TextEdit, string> CopyRequested;
        /// <summary>Returns the retained context popup, equivalent to Godot's <c>get_menu()</c>.</summary>
        public override PopupMenu GetMenu() => _contextMenu;
        public override bool IsMenuVisible() => _contextMenu.Visible;
        /// <summary>Sets the document-bound syntax provider, analogous to Godot's set_syntax_highlighter().</summary>
        public void SetSyntaxHighlighter(SyntaxHighlighter highlighter)
        {
            if (_syntaxHighlighter == highlighter) return;
            if (_syntaxHighlighter != null)
            {
                _syntaxHighlighter.Changed -= SyntaxHighlighterChanged;
                _syntaxHighlighter.SetTextEdit(null);
            }
            _syntaxHighlighter = highlighter;
            if (_syntaxHighlighter != null)
            {
                _syntaxHighlighter.SetTextEdit(this);
                _syntaxHighlighter.Changed += SyntaxHighlighterChanged;
            }
            QueueLayout();
        }
        public SyntaxHighlighter GetSyntaxHighlighter() => _syntaxHighlighter;
        /// <summary>Gets the retained highlighter output for a line. Empty when no provider is assigned.</summary>
        public IReadOnlyList<SyntaxHighlightSpan> GetLineSyntaxHighlighting(int line)
        {
            ValidateLine(line);
            return _syntaxHighlighter?.GetLineSyntaxHighlighting(line) ?? Array.Empty<SyntaxHighlightSpan>();
        }
        public int CaretLine
        {
            get => GetLineForIndex(CaretColumn);
            set => SetCaret(value, CaretColumnInLine);
        }
        public int CaretColumnInLine => CaretColumn - GetLineStart(CaretLine);
        public int LineCount => string.IsNullOrEmpty(Text) ? 1 : Text.Split('\n').Length;
        public string GetLine(int line)
        {
            if (line < 0 || line >= LineCount) throw new ArgumentOutOfRangeException(nameof(line));
            var start = GetLineStart(line);
            var end = Text.IndexOf('\n', start);
            return end < 0 ? Text.Substring(start) : Text.Substring(start, end - start);
        }
        public void SetCaret(int line, int column) => SetCaret(line, column, 0);
        public void SetCaret(int line, int column, int caret)
        {
            line = MathHelper.Clamp(line, 0, LineCount - 1);
            var index = GetLineStart(line) + MathHelper.Clamp(column, 0, GetLine(line).Length);
            if (caret == 0) { CaretColumn = index; Deselect(); RequestCaretMerge(); return; }
            var secondary = GetSecondaryCaret(caret); secondary.Index = index; secondary.Anchor = -1; RequestCaretMerge();
        }
        public void SetMultipleCaretsEnabled(bool enabled) => MultipleCaretsEnabled = enabled;
        public bool IsMultipleCaretsEnabled() => MultipleCaretsEnabled;
        /// <summary>Adds a non-overlapping secondary caret and returns its Godot-compatible index, or -1 when unavailable.</summary>
        public int AddCaret(int line, int column)
        {
            if (!MultipleCaretsEnabled) return -1;
            line = MathHelper.Clamp(line, 0, LineCount - 1); var index = GetLineStart(line) + MathHelper.Clamp(column, 0, GetLine(line).Length);
            if (CaretContainsIndex(0, index) || _secondaryCarets.Exists(caret => CaretContainsIndex(caret, index))) return -1;
            _secondaryCarets.Add(new SecondaryCaret { Index = index, Anchor = -1 }); return CaretCount - 1;
        }
        public void RemoveCaret(int caret)
        {
            if (caret <= 0 || caret >= CaretCount) throw new ArgumentOutOfRangeException(nameof(caret), "The primary caret cannot be removed.");
            _secondaryCarets.RemoveAt(caret - 1);
        }
        public void RemoveSecondaryCarets() => _secondaryCarets.Clear();
        /// <summary>Merges caret points or selections that overlap, following Godot's inclusive overlap rule.</summary>
        public void MergeOverlappingCarets()
        {
            if (_secondaryCarets.Count == 0) return;
            var entries = new List<CaretMerge>(CaretCount);
            for (var caret = 0; caret < CaretCount; caret++) entries.Add(new CaretMerge { Caret = caret, From = GetSelectionFromIndex(caret), To = GetSelectionToIndex(caret) });
            entries.Sort((left, right) => { var comparison = left.From.CompareTo(right.From); return comparison != 0 ? comparison : left.To.CompareTo(right.To); });
            var mergedGroups = new List<CaretMerge>(); var index = 0;
            while (index < entries.Count)
            {
                var group = entries[index]; var containsPrimary = group.Caret == 0; index++;
                while (index < entries.Count && entries[index].From <= group.To)
                {
                    group.To = Math.Max(group.To, entries[index].To); containsPrimary |= entries[index].Caret == 0; index++;
                }
                group.ContainsPrimary = containsPrimary; mergedGroups.Add(group);
            }
            CaretMerge primary = null;
            foreach (var group in mergedGroups) if (group.ContainsPrimary) { primary = group; break; }
            if (primary == null) return;
            if (primary.From == primary.To) { CaretColumn = primary.From; Deselect(); }
            else base.Select(primary.From, primary.To);
            _secondaryCarets.Clear();
            foreach (var group in mergedGroups)
            {
                if (group.ContainsPrimary) continue;
                _secondaryCarets.Add(new SecondaryCaret { Index = group.To, Anchor = group.From == group.To ? -1 : group.From });
            }
        }
        public int GetCaretCount() => CaretCount;
        public int GetCaretLine(int caret = 0) => GetLineForIndex(GetCaretIndex(caret));
        public int GetCaretColumn(int caret = 0) => GetCaretIndex(caret) - GetLineStart(GetCaretLine(caret));
        public void SetCaretLine(int line, int caret = 0) => SetCaret(line, GetCaretColumn(caret), caret);
        public void SetCaretColumn(int column, int caret = 0) => SetCaret(GetCaretLine(caret), column, caret);
        /// <summary>Returns selection state for a specific caret without shadowing LineEdit's primary <see cref="LineEdit.HasSelection"/> property.</summary>
        public bool HasCaretSelection(int caret) => caret == 0 ? base.HasSelection : GetSecondaryCaret(caret).Anchor >= 0 && GetSecondaryCaret(caret).Anchor != GetSecondaryCaret(caret).Index;
        public int GetSelectionOriginLine(int caret = 0) => GetLineForIndex(GetSelectionOriginIndex(caret));
        public int GetSelectionOriginColumn(int caret = 0) { var origin = GetSelectionOriginIndex(caret); return origin - GetLineStart(GetLineForIndex(origin)); }
        public int GetSelectionFrom(int caret) => GetSelectionFromIndex(caret);
        public int GetSelectionTo(int caret) => GetSelectionToIndex(caret);
        public void Select(int fromLine, int fromColumn, int toLine, int toColumn, int caret = 0)
        {
            fromLine = MathHelper.Clamp(fromLine, 0, LineCount - 1); toLine = MathHelper.Clamp(toLine, 0, LineCount - 1);
            var from = GetLineStart(fromLine) + MathHelper.Clamp(fromColumn, 0, GetLine(fromLine).Length);
            var to = GetLineStart(toLine) + MathHelper.Clamp(toColumn, 0, GetLine(toLine).Length);
            if (caret == 0) base.Select(from, to);
            else { var secondary = GetSecondaryCaret(caret); secondary.Anchor = from; secondary.Index = to; }
            RequestCaretMerge();
        }
        /// <summary>Selects the word at one caret, or toggles the existing selection off, matching Godot's <c>select_word_under_caret</c>.</summary>
        public void SelectWordUnderCaret(int caret = -1)
        {
            if (caret >= CaretCount || caret < -1) throw new ArgumentOutOfRangeException(nameof(caret));
            var first = caret < 0 ? 0 : caret; var last = caret < 0 ? CaretCount - 1 : caret;
            _caretMergeSuspension++;
            try
            {
                for (var index = first; index <= last; index++)
                {
                    if (HasCaretSelection(index)) { Deselect(index); continue; }
                    if (!TryGetWordRange(index, out var line, out var from, out var to)) continue;
                    Select(line, from, line, to, index);
                }
            }
            finally { _caretMergeSuspension--; }
            MergeOverlappingCarets();
        }
        /// <summary>Adds a selected secondary caret at the next case-sensitive occurrence of the final caret's selected word.</summary>
        public void AddSelectionForNextOccurrence()
        {
            if (!MultipleCaretsEnabled || string.IsNullOrEmpty(Text)) return;
            var caret = CaretCount - 1;
            if (!HasCaretSelection(caret)) { SelectWordUnderCaret(caret); return; }
            var selected = GetSelectedText(caret); if (string.IsNullOrEmpty(selected)) return;
            var from = GetSelectionFromIndex(caret); var line = GetLineForIndex(from); var column = from - GetLineStart(line) + 1;
            var occurrence = Search(selected, TextSearchFlags.MatchCase, line, column);
            if (occurrence.X < 0 || occurrence.Y < 0) return;
            var added = AddCaret(occurrence.Y, occurrence.X + selected.Length);
            if (added >= 0) Select(occurrence.Y, occurrence.X, occurrence.Y, occurrence.X + selected.Length, added);
        }
        /// <summary>Moves the final occurrence selection to its next match, matching Godot's <c>skip_selection_for_next_occurrence</c>.</summary>
        public void SkipSelectionForNextOccurrence()
        {
            if (string.IsNullOrEmpty(Text)) return;
            var caret = CaretCount - 1; var selected = HasCaretSelection(caret) ? GetSelectedText(caret) : GetWordAtCaret(caret);
            if (string.IsNullOrEmpty(selected)) return;
            var from = HasCaretSelection(caret) ? GetSelectionFromIndex(caret) : GetCaretIndex(caret); var line = GetLineForIndex(from); var column = from - GetLineStart(line) + 1;
            var occurrence = Search(selected, TextSearchFlags.MatchCase, line, column);
            if (occurrence.X < 0 || occurrence.Y < 0) return;
            if (caret == 0)
            {
                Deselect(0); Select(occurrence.Y, occurrence.X, occurrence.Y, occurrence.X + selected.Length); return;
            }
            var added = AddCaret(occurrence.Y, occurrence.X + selected.Length);
            if (added < 0) return;
            Select(occurrence.Y, occurrence.X, occurrence.Y, occurrence.X + selected.Length, added); RemoveCaret(caret);
        }
        public IReadOnlyList<TextEditCaret> GetCarets()
        {
            var carets = new List<TextEditCaret>(CaretCount); for (var caret = 0; caret < CaretCount; caret++) carets.Add(new TextEditCaret(GetCaretLine(caret), GetCaretColumn(caret), GetSelectionOriginLine(caret), GetSelectionOriginColumn(caret))); return carets;
        }
        /// <summary>Returns screen-space visual-row rectangles for a caret selection, including wrapped source rows.</summary>
        public IReadOnlyList<Rectangle> GetSelectionRectangles(int caret = 0)
        {
            if (caret < 0 || caret >= CaretCount) throw new ArgumentOutOfRangeException(nameof(caret));
            var rectangles = new List<Rectangle>(); if (!HasCaretSelection(caret)) return rectangles;
            var from = GetSelectionFromIndex(caret); var to = GetSelectionToIndex(caret); var fromLine = GetLineForIndex(from); var toLine = GetLineForIndex(to); var lineHeight = EffectiveUIFont == null ? 16 : Math.Max(1, TextMetrics.LineHeight(EffectiveUIFont));
            for (var line = fromLine; line <= toLine; line++)
            {
                if (IsLineHiddenForDisplay(line)) continue;
                var sourceStart = GetLineStart(line); var source = GetLine(line);
                for (var wrap = 0; wrap <= GetLineWrapCount(line); wrap++)
                {
                    var localSegmentStart = GetLineWrapStartColumn(line, wrap); var segmentStart = sourceStart + localSegmentStart; var segmentLength = GetLineWrapLength(line, wrap); var segmentEnd = segmentStart + segmentLength;
                    var start = Math.Max(from, segmentStart); var end = Math.Min(to, segmentEnd); if (end <= start) continue;
                    var x = GlobalPosition.X + Padding.Left + TextContentLeftInset; var y = GlobalPosition.Y + Padding.Top + GetVisibleRow(line, wrap) * lineHeight;
                    if (EffectiveUIFont == null)
                    {
                        var prefix = source.Substring(localSegmentStart, start - segmentStart);
                        var selected = source.Substring(start - sourceStart, end - start);
                        rectangles.Add(new Rectangle((int)(x + MeasureTextWidth(prefix)), (int)y, Math.Max(1, (int)MathF.Ceiling(MeasureTextWidth(selected))), lineHeight));
                        continue;
                    }
                    var layout = GetSegmentLayout(source, localSegmentStart, segmentLength);
                    foreach (var rectangle in layout.GetSelectionRectangles(start - segmentStart, end - start))
                        rectangles.Add(new Rectangle((int)MathF.Floor(x + rectangle.X), (int)MathF.Floor(y + rectangle.Y), Math.Max(1, (int)MathF.Ceiling(rectangle.Width)), Math.Max(1, (int)MathF.Ceiling(rectangle.Height))));
                }
            }
            return rectangles;
        }
        /// <summary>Adds one caret above or below every existing caret where the adjacent visible source line exists.</summary>
        public void AddCaretAtCarets(bool below)
        {
            if (!MultipleCaretsEnabled) return;
            var existing = GetCarets();
            foreach (var caret in existing)
            {
                var line = caret.Line + (below ? 1 : -1); if (line < 0 || line >= LineCount || IsLineHiddenForDisplay(line)) continue;
                AddCaret(line, caret.Column);
            }
            MergeOverlappingCarets();
        }
        public override void InsertText(string text)
        {
            if (!Editable || string.IsNullOrEmpty(text) || _secondaryCarets.Count == 0) { base.InsertText(text); return; }
            var edits = new List<CaretEdit>();
            for (var caret = 0; caret < CaretCount; caret++) edits.Add(new CaretEdit { Caret = caret, Start = GetSelectionFromIndex(caret), End = GetSelectionToIndex(caret) });
            ApplyMultiCaretEdits(edits, text);
        }
        public void InsertNewline() => InsertText("\n");
        /// <summary>Inserts text at one caret, or all carets when <paramref name="caret"/> is -1, matching Godot's <c>insert_text_at_caret</c>.</summary>
        public void InsertTextAtCaret(string text, int caret = -1)
        {
            if (!Editable || string.IsNullOrEmpty(text)) return;
            if (caret >= CaretCount || caret < -1) throw new ArgumentOutOfRangeException(nameof(caret));
            if (caret < 0) { InsertText(text); return; }
            var edits = new List<CaretEdit>();
            for (var index = 0; index < CaretCount; index++)
            {
                var target = index == caret; var start = target ? GetSelectionFromIndex(index) : GetCaretIndex(index); var end = target ? GetSelectionToIndex(index) : start;
                edits.Add(new CaretEdit { Caret = index, Start = start, End = end });
            }
            ApplyMultiCaretEdits(edits, edit => edit.Caret == caret ? text : string.Empty);
        }
        /// <summary>Inserts text at a source line and column, retaining all caret and selection positions relative to the inserted range.</summary>
        public void InsertText(string text, int line, int column, bool beforeSelectionBegin = true, bool beforeSelectionEnd = false)
        {
            if (!Editable || string.IsNullOrEmpty(text)) return;
            line = MathHelper.Clamp(line, 0, LineCount - 1); var index = GetLineStart(line) + MathHelper.Clamp(column, 0, GetLine(line).Length);
            var states = CaptureCaretStates();
            foreach (var state in states)
            {
                if (state.Index > index || state.Index == index && beforeSelectionEnd) state.Index += text.Length;
                if (state.Anchor >= 0 && (state.Anchor > index || state.Anchor == index && beforeSelectionBegin)) state.Anchor += text.Length;
            }
            _updatingMultipleCarets = true;
            try { Text = Text.Insert(index, text); RestoreCaretStates(states); }
            finally { _updatingMultipleCarets = false; }
        }
        /// <summary>Removes a source range, retaining all carets and selections at the collapsed range start where necessary.</summary>
        public void RemoveText(int fromLine, int fromColumn, int toLine, int toColumn)
        {
            fromLine = MathHelper.Clamp(fromLine, 0, LineCount - 1); toLine = MathHelper.Clamp(toLine, 0, LineCount - 1);
            var from = GetLineStart(fromLine) + MathHelper.Clamp(fromColumn, 0, GetLine(fromLine).Length); var to = GetLineStart(toLine) + MathHelper.Clamp(toColumn, 0, GetLine(toLine).Length);
            if (to < from) { var swap = from; from = to; to = swap; }
            if (to == from) return;
            var states = CaptureCaretStates();
            foreach (var state in states) { state.Index = CollapseTextIndex(state.Index, from, to); if (state.Anchor >= 0) state.Anchor = CollapseTextIndex(state.Anchor, from, to); }
            _updatingMultipleCarets = true;
            try { Text = Text.Remove(from, to - from); RestoreCaretStates(states); }
            finally { _updatingMultipleCarets = false; }
        }
        /// <summary>Deletes selections at one caret, or every selected caret when <paramref name="caret"/> is -1.</summary>
        public void DeleteSelection(int caret)
        {
            if (caret >= CaretCount || caret < -1) throw new ArgumentOutOfRangeException(nameof(caret));
            if (caret >= 0 ? HasCaretSelection(caret) : HasAnyCaretSelection()) DeleteCaretSelections(caret);
        }
        public void DeleteAllSelections() => DeleteSelection(-1);
        /// <summary>Clears selection state at one caret, or all carets when <paramref name="caret"/> is -1.</summary>
        public void Deselect(int caret)
        {
            if (caret >= CaretCount || caret < -1) throw new ArgumentOutOfRangeException(nameof(caret));
            if (caret <= 0) base.Deselect();
            if (caret < 0) for (var index = 1; index < CaretCount; index++) GetSecondaryCaret(index).Anchor = -1;
            else if (caret > 0) GetSecondaryCaret(caret).Anchor = -1;
        }
        public void DeselectAll() => Deselect(-1);
        /// <summary>Returns leading indentation measured with Godot-compatible tab width of four columns.</summary>
        public int GetIndentLevel(int line)
        {
            var indent = 0; foreach (var character in GetLine(line)) { if (character == ' ') indent++; else if (character == '\t') indent += 4; else break; } return indent;
        }
        /// <summary>Returns the first non-whitespace source column, or the line length when a line is blank.</summary>
        public int GetFirstNonWhitespaceColumn(int line)
        {
            var text = GetLine(line); var column = 0; while (column < text.Length && char.IsWhiteSpace(text[column])) column++; return column;
        }
        /// <summary>Swaps two source lines while retaining carets, selections, line backgrounds, and gutter items with their original line content.</summary>
        public void SwapLines(int fromLine, int toLine)
        {
            ValidateLine(fromLine); ValidateLine(toLine); if (fromLine == toLine) return;
            var caretStates = new List<CaretLineState>();
            for (var caret = 0; caret < CaretCount; caret++) caretStates.Add(new CaretLineState { Line = GetCaretLine(caret), Column = GetCaretColumn(caret), OriginLine = GetSelectionOriginLine(caret), OriginColumn = GetSelectionOriginColumn(caret), Selected = HasCaretSelection(caret) });
            var lines = GetLines(); var swap = lines[fromLine]; lines[fromLine] = lines[toLine]; lines[toLine] = swap;
            SwapLineMetadata(fromLine, toLine);
            _updatingMultipleCarets = true;
            try { Text = string.Join("\n", lines); RestoreCaretLineStates(caretStates, fromLine, toLine); }
            finally { _updatingMultipleCarets = false; }
        }
        public new void SetShortcutKeysEnabled(bool enabled) => ShortcutKeysEnabled = enabled;
        public new bool IsShortcutKeysEnabled() => ShortcutKeysEnabled;
        public void SetEmptySelectionClipboardEnabled(bool enabled) => EmptySelectionClipboardEnabled = enabled;
        public bool IsEmptySelectionClipboardEnabled() => EmptySelectionClipboardEnabled;
        /// <summary>Returns Godot-style selected text, or newline-joined selected fragments from all carets when <paramref name="caret"/> is -1.</summary>
        public string GetSelectedText(int caret = -1)
        {
            if (caret >= CaretCount || caret < -1) throw new ArgumentOutOfRangeException(nameof(caret));
            if (caret >= 0) return HasCaretSelection(caret) ? Text.Substring(GetSelectionFromIndex(caret), GetSelectionToIndex(caret) - GetSelectionFromIndex(caret)) : string.Empty;
            var caretIndexes = GetSortedCaretIndexes(); var fragments = new List<string>();
            foreach (var index in caretIndexes) if (HasCaretSelection(index)) fragments.Add(Text.Substring(GetSelectionFromIndex(index), GetSelectionToIndex(index) - GetSelectionFromIndex(index)));
            return string.Join("\n", fragments);
        }
        /// <summary>Requests a host clipboard write using Godot's selection-or-whole-line copy policy.</summary>
        public void Copy(int caret = -1) => TryCopy(caret, ClipboardOperation.Copy);
        private bool TryCopy(int caret, ClipboardOperation operation)
        {
            if (caret >= CaretCount || caret < -1) throw new ArgumentOutOfRangeException(nameof(caret));
            var copied = GetSelectedText(caret);
            if (!string.IsNullOrEmpty(copied))
            {
                if (!WriteClipboard(copied, operation)) return false;
                _cutCopyLine = string.Empty;
                return true;
            }
            if (!EmptySelectionClipboardEnabled) return false;
            var ranges = GetCaretLineRanges(caret); var lines = new System.Text.StringBuilder();
            foreach (var range in ranges) for (var line = range.First; line <= range.Last; line++) { lines.Append(GetLine(line)); lines.Append('\n'); }
            copied = lines.ToString();
            if (string.IsNullOrEmpty(copied) || !WriteClipboard(copied, operation)) return false;
            _cutCopyLine = CaretCount == 1 ? copied : string.Empty;
            return true;
        }
        /// <summary>Copies text using <see cref="Copy"/> then removes selections or whole caret line ranges when editable.</summary>
        public void Cut(int caret = -1)
        {
            if (!TryCopy(caret, ClipboardOperation.Cut) || !Editable) return;
            if (caret >= CaretCount || caret < -1) throw new ArgumentOutOfRangeException(nameof(caret));
            if (caret >= 0 ? HasCaretSelection(caret) : HasAnyCaretSelection()) { DeleteCaretSelections(caret); return; }
            if (!EmptySelectionClipboardEnabled) return;
            DeleteCaretLines(caret);
        }
        /// <summary>Obtains host clipboard content and applies Godot's multi-caret paste distribution policy.</summary>
        public void Paste(int caret = -1)
        {
            if (Editable) Paste(ClipboardTextProvider?.Invoke(this) ?? ReadClipboardText(), caret);
        }
        /// <summary>Pastes supplied clipboard content at one caret or all carets. A line per caret is used when the counts match.</summary>
        public void Paste(string clipboard, int caret = -1)
        {
            if (!Editable || string.IsNullOrEmpty(clipboard)) return;
            if (caret >= CaretCount || caret < -1) throw new ArgumentOutOfRangeException(nameof(caret));
            clipboard = clipboard.Replace("\r", string.Empty);
            if (CaretCount == 1 && (caret < 0 || caret == 0) && !HasCaretSelection(0) && !string.IsNullOrEmpty(_cutCopyLine) && string.Equals(_cutCopyLine, clipboard, StringComparison.Ordinal))
            {
                var originalLine = CaretLine; var originalColumn = CaretColumnInLine; Text = Text.Insert(GetLineStart(originalLine), clipboard); SetCaret(originalLine + clipboard.Split('\n').Length - 1, originalColumn); return;
            }
            var carets = caret < 0 ? GetSortedCaretIndexes() : new List<int> { caret };
            var clipboardLines = clipboard.Split('\n'); var distributeLines = caret < 0 && CaretCount > 1 && clipboardLines.Length == CaretCount;
            var replacements = new Dictionary<int, string>();
            for (var index = 0; index < CaretCount; index++) replacements[index] = string.Empty;
            for (var index = 0; index < carets.Count; index++) replacements[carets[index]] = distributeLines ? clipboardLines[index] : clipboard;
            var edits = new List<CaretEdit>();
            for (var index = 0; index < CaretCount; index++)
            {
                var target = carets.Contains(index); var start = target ? GetSelectionFromIndex(index) : GetCaretIndex(index); var end = target ? GetSelectionToIndex(index) : start;
                edits.Add(new CaretEdit { Caret = index, Start = start, End = end });
            }
            foreach (var index in carets) replacements[index] = distributeLines ? replacements[index] : clipboard;
            ApplyMultiCaretEdits(edits, edit => replacements[edit.Caret]);
        }
        private bool WriteClipboard(string text, ClipboardOperation operation)
        {
            var accepted = TryWriteClipboardText(text, operation);
            CopyRequested?.Invoke(this, text);
            return accepted;
        }
        /// <summary>Clears the retained document and secondary carets, matching Godot's <c>clear</c>.</summary>
        public new void Clear()
        {
            if (!Editable) return;
            Text = string.Empty; RemoveSecondaryCarets(); SetCaret(0, 0);
        }
        /// <summary>Selects the entire document with the primary caret, matching Godot's <c>select_all</c>.</summary>
        public new void SelectAll()
        {
            RemoveSecondaryCarets(); base.SelectAll();
        }
        /// <summary>Executes one retained context-menu command.</summary>
        public void MenuOption(TextEditMenuOption option)
        {
            switch (option)
            {
                case TextEditMenuOption.Cut: Cut(); break;
                case TextEditMenuOption.Copy: Copy(); break;
                case TextEditMenuOption.Paste: Paste(); break;
                case TextEditMenuOption.Clear: Clear(); break;
                case TextEditMenuOption.SelectAll: SelectAll(); break;
                case TextEditMenuOption.Undo: Undo(); break;
                case TextEditMenuOption.Redo: Redo(); break;
                case TextEditMenuOption.DirectionInherited: SetTextDirection(TextDirection.Inherited); break;
                case TextEditMenuOption.DirectionAuto: SetTextDirection(TextDirection.Auto); break;
                case TextEditMenuOption.DirectionLeftToRight: SetTextDirection(TextDirection.LeftToRight); break;
                case TextEditMenuOption.DirectionRightToLeft: SetTextDirection(TextDirection.RightToLeft); break;
                case TextEditMenuOption.DisplayControlCharacters: SetDrawControlChars(!GetDrawControlChars()); break;
                case TextEditMenuOption.InsertLeftToRightMark: InsertText("\u200E"); break;
                case TextEditMenuOption.InsertRightToLeftMark: InsertText("\u200F"); break;
                case TextEditMenuOption.InsertLeftToRightEmbedding: InsertText("\u202A"); break;
                case TextEditMenuOption.InsertRightToLeftEmbedding: InsertText("\u202B"); break;
                case TextEditMenuOption.InsertLeftToRightOverride: InsertText("\u202D"); break;
                case TextEditMenuOption.InsertRightToLeftOverride: InsertText("\u202E"); break;
                case TextEditMenuOption.InsertPopDirectionFormatting: InsertText("\u202C"); break;
                case TextEditMenuOption.InsertArabicLetterMark: InsertText("\u061C"); break;
                case TextEditMenuOption.InsertLeftToRightIsolate: InsertText("\u2066"); break;
                case TextEditMenuOption.InsertRightToLeftIsolate: InsertText("\u2067"); break;
                case TextEditMenuOption.InsertFirstStrongIsolate: InsertText("\u2068"); break;
                case TextEditMenuOption.InsertPopDirectionIsolate: InsertText("\u2069"); break;
                case TextEditMenuOption.InsertZeroWidthJoiner: InsertText("\u200D"); break;
                case TextEditMenuOption.InsertZeroWidthNonJoiner: InsertText("\u200C"); break;
                case TextEditMenuOption.InsertWordJoiner: InsertText("\u2060"); break;
                case TextEditMenuOption.InsertSoftHyphen: InsertText("\u00AD"); break;
            }
        }
        public void SetLine(int line, string text)
        {
            var lines = GetLines(); ValidateLine(line); lines[line] = text ?? string.Empty; Text = string.Join("\n", lines); SetCaret(line, Math.Min(CaretColumnInLine, lines[line].Length));
        }
        public void InsertLineAt(int line, string text)
        {
            var lines = GetLines(); if (line < 0 || line > lines.Count) throw new ArgumentOutOfRangeException(nameof(line)); ShiftLineBackgrounds(line, 1); lines.Insert(line, text ?? string.Empty); Text = string.Join("\n", lines); SetCaret(line, 0);
        }
        public void RemoveLineAt(int line)
        {
            var lines = GetLines(); ValidateLine(line); if (lines.Count == 1) lines[0] = string.Empty; else { lines.RemoveAt(line); _lineBackgroundColors.Remove(line); RemoveLineGutterItems(line); ShiftLineBackgrounds(line + 1, -1); } Text = string.Join("\n", lines); SetCaret(Math.Min(line, lines.Count - 1), 0);
        }
        public void SetLineBackgroundColor(int line, Color color) { ValidateLine(line); _lineBackgroundColors[line] = color; }
        public void ClearLineBackgroundColor(int line) { ValidateLine(line); _lineBackgroundColors.Remove(line); }
        public Color? GetLineBackgroundColor(int line) { ValidateLine(line); return _lineBackgroundColors.TryGetValue(line, out var color) ? color : (Color?)null; }
        public void SetSearchText(string text) => SearchText = text ?? string.Empty;
        public void SetSearchFlags(TextSearchFlags flags) => SearchFlags = flags;
        public void SetLineWrappingMode(TextEditLineWrappingMode mode)
        {
            if (mode != TextEditLineWrappingMode.None && mode != TextEditLineWrappingMode.Boundary) throw new ArgumentOutOfRangeException(nameof(mode));
            if (_lineWrappingMode == mode) return;
            _lineWrappingMode = mode; InvalidateWrapLayout(); QueueLayout();
        }
        public TextEditLineWrappingMode GetLineWrappingMode() => _lineWrappingMode;
        public bool IsLineWrapped(int line) { ValidateLine(line); return GetWrapSegments(line).Count > 1; }
        /// <summary>Returns the number of continuation rows after the first visual row for a source line.</summary>
        public int GetLineWrapCount(int line) { ValidateLine(line); return Math.Max(0, GetWrapSegments(line).Count - 1); }
        public int GetLineWrapIndexAtColumn(int line, int column)
        {
            ValidateLine(line); column = MathHelper.Clamp(column, 0, GetLine(line).Length);
            var segments = GetWrapSegments(line);
            for (var index = 0; index < segments.Count; index++) if (column < segments[index].Start + segments[index].Length || index == segments.Count - 1) return index;
            return 0;
        }
        public IReadOnlyList<string> GetLineWrappedText(int line)
        {
            ValidateLine(line); var source = GetLine(line); var result = new List<string>();
            foreach (var segment in GetWrapSegments(line)) result.Add(source.Substring(segment.Start, segment.Length));
            return result;
        }
        /// <summary>Returns the width of a source line or one of its visual wrap rows.</summary>
        public float GetLineWidth(int line, int wrapIndex = -1)
        {
            ValidateLine(line); var source = GetLine(line);
            if (wrapIndex < 0) return EffectiveUIFont == null ? source.Length * 8 : TextMetrics.Measure(EffectiveUIFont, source).X;
            var segments = GetWrapSegments(line); if (wrapIndex >= segments.Count) throw new ArgumentOutOfRangeException(nameof(wrapIndex));
            var segment = segments[wrapIndex]; var text = source.Substring(segment.Start, segment.Length);
            return EffectiveUIFont == null ? text.Length * 8 : TextMetrics.Measure(EffectiveUIFont, text).X;
        }
        public void AddGutter(int at = -1)
        {
            if (at < 0) at = _gutters.Count;
            if (at < 0 || at > _gutters.Count) throw new ArgumentOutOfRangeException(nameof(at));
            _gutters.Insert(at, new TextEditGutter()); ShiftGutterItems(at, 1); QueueLayout();
        }
        public void RemoveGutter(int gutter)
        {
            ValidateGutter(gutter); _gutters.RemoveAt(gutter); ShiftGutterItems(gutter + 1, -1, gutter); QueueLayout();
        }
        public string GetGutterName(int gutter) => GetGutter(gutter).Name;
        public void SetGutterName(int gutter, string name) => GetGutter(gutter).Name = name ?? string.Empty;
        public TextEditGutterType GetGutterType(int gutter) => GetGutter(gutter).Type;
        public void SetGutterType(int gutter, TextEditGutterType type) => GetGutter(gutter).Type = type;
        public int GetGutterWidth(int gutter) => GetGutter(gutter).Width;
        public void SetGutterWidth(int gutter, int width) { GetGutter(gutter).Width = Math.Max(0, width); QueueLayout(); }
        public bool IsGutterDrawn(int gutter) => GetGutter(gutter).Draw;
        public void SetGutterDraw(int gutter, bool draw) { GetGutter(gutter).Draw = draw; QueueLayout(); }
        public bool IsGutterClickable(int gutter) => GetGutter(gutter).Clickable;
        public void SetGutterClickable(int gutter, bool clickable) => GetGutter(gutter).Clickable = clickable;
        public bool IsGutterOverwritable(int gutter) => GetGutter(gutter).Overwritable;
        public void SetGutterOverwritable(int gutter, bool overwritable) => GetGutter(gutter).Overwritable = overwritable;
        public void SetGutterCustomDraw(int gutter, Action<UIRenderContext, TextEdit, int, Rectangle> draw) { var state = GetGutter(gutter); state.Type = TextEditGutterType.Custom; state.CustomDraw = draw; }
        public int GetTotalGutterWidth() { var width = 0; foreach (var gutter in _gutters) if (gutter.Draw) width += gutter.Width; return width; }
        public void SetLineGutterMetadata(int line, int gutter, object metadata) => GetLineGutterItem(line, gutter, true).Metadata = metadata;
        public object GetLineGutterMetadata(int line, int gutter) => GetLineGutterItem(line, gutter, false)?.Metadata;
        public void SetLineGutterText(int line, int gutter, string text) => GetLineGutterItem(line, gutter, true).Text = text ?? string.Empty;
        public string GetLineGutterText(int line, int gutter) => GetLineGutterItem(line, gutter, false)?.Text ?? string.Empty;
        public void SetLineGutterIcon(int line, int gutter, Texture2D icon) => GetLineGutterItem(line, gutter, true).Icon = icon;
        public Texture2D GetLineGutterIcon(int line, int gutter) => GetLineGutterItem(line, gutter, false)?.Icon;
        public void SetLineGutterItemColor(int line, int gutter, Color color) => GetLineGutterItem(line, gutter, true).Color = color;
        public Color GetLineGutterItemColor(int line, int gutter) => GetLineGutterItem(line, gutter, false)?.Color ?? Color.White;
        public void SetLineGutterClickable(int line, int gutter, bool clickable) => GetLineGutterItem(line, gutter, true).Clickable = clickable;
        public bool IsLineGutterClickable(int line, int gutter) => GetLineGutterItem(line, gutter, false)?.Clickable ?? false;
        /// <summary>Returns a Godot-compatible (column, line) point, or (-1, -1) when not found.</summary>
        public Point Search(string key, TextSearchFlags flags, int fromLine = 0, int fromColumn = 0)
        {
            if (string.IsNullOrEmpty(key)) return new Point(-1, -1);
            var comparison = flags.HasFlag(TextSearchFlags.MatchCase) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var lines = GetLines(); fromLine = MathHelper.Clamp(fromLine, 0, lines.Count - 1);
            if (flags.HasFlag(TextSearchFlags.Backwards))
            {
                for (var line = fromLine; line >= 0; line--)
                {
                    var start = (line == fromLine ? Math.Min(fromColumn, lines[line].Length) : lines[line].Length) - 1;
                    while (start >= 0)
                    {
                        var column = lines[line].LastIndexOf(key, start, comparison);
                        if (column < 0) break;
                        if (!flags.HasFlag(TextSearchFlags.WholeWords) || IsWholeWord(lines[line], column, key.Length)) return new Point(column, line);
                        start = column - 1;
                    }
                }
            }
            else
            {
                for (var line = fromLine; line < lines.Count; line++)
                {
                    var start = line == fromLine ? MathHelper.Clamp(fromColumn, 0, lines[line].Length) : 0;
                    for (var column = lines[line].IndexOf(key, start, comparison); column >= 0; column = lines[line].IndexOf(key, column + 1, comparison)) if (!flags.HasFlag(TextSearchFlags.WholeWords) || IsWholeWord(lines[line], column, key.Length)) return new Point(column, line);
                }
            }
            return new Point(-1, -1);
        }
        public new void Undo()
        {
            if (!UndoEnabled || _undoStack.Count == 0) return;
            _redoStack.Add(Text); _redoVersions.Add(_version); var last = _undoStack.Count - 1; RestoreHistory(_undoStack[last], _undoVersions[last]); _undoStack.RemoveAt(last); _undoVersions.RemoveAt(last);
        }
        public new void Redo()
        {
            if (!UndoEnabled || _redoStack.Count == 0) return;
            _undoStack.Add(Text); _undoVersions.Add(_version); var last = _redoStack.Count - 1; RestoreHistory(_redoStack[last], _redoVersions[last]); _redoStack.RemoveAt(last); _redoVersions.RemoveAt(last);
        }
        public new void ClearUndoHistory() { _undoStack.Clear(); _redoStack.Clear(); _undoVersions.Clear(); _redoVersions.Clear(); _historyText = Text; }
        /// <summary>Begins a Godot-style edit action; all edits until <see cref="EndAction"/> become one undo step.</summary>
        public void StartAction(TextEditEditAction action)
        {
            if (action == TextEditEditAction.None) { EndAction(); return; }
            if (_currentAction == action) return;
            EndAction(); _currentAction = action; _actionStartText = Text; _actionStartVersion = _version; _actionChanged = false;
        }
        /// <summary>Finishes the active action and commits its grouped undo/version entry.</summary>
        public void EndAction()
        {
            if (_currentAction == TextEditEditAction.None) return;
            if (_actionChanged)
            {
                if (UndoEnabled) PushUndoState(_actionStartText, _actionStartVersion);
                _version = _actionStartVersion + 1;
            }
            _currentAction = TextEditEditAction.None; _actionChanged = false;
        }
        public TextEditEditAction GetCurrentAction() => _currentAction;
        public void TagSavedVersion() => _savedVersion = _version;
        public uint GetVersion() => _version;
        public uint GetSavedVersion() => _savedVersion;
        /// <summary>Returns the visual-row scroll position for a source line and wrap row.</summary>
        public int GetScrollPosForLine(int line, int wrapIndex = 0) => GetVisualRowOffset(line, wrapIndex);
        public int GetFirstVisibleLine() => FirstVisibleLine;
        public void SetLineAsFirstVisible(int line, int wrapIndex = 0) => SetFirstVisibleRow(line, wrapIndex);
        public void SetLineAsCenterVisible(int line, int wrapIndex = 0) => SetFirstVisibleVisualRow(GetVisualRowOffset(line, wrapIndex) - GetVisibleLineCount() / 2);
        public void SetLineAsLastVisible(int line, int wrapIndex = 0) => SetFirstVisibleVisualRow(GetVisualRowOffset(line, wrapIndex) - GetVisibleLineCount() + 1);
        public int GetLastFullVisibleLine() { GetLineAndWrapAtVisibleRow(Math.Max(0, GetVisibleLineCount() - 1), out var line, out _); return line; }
        public int GetLastFullVisibleLineWrapIndex() { GetLineAndWrapAtVisibleRow(Math.Max(0, GetVisibleLineCount() - 1), out _, out var wrapIndex); return wrapIndex; }
        /// <summary>Returns the visible line capacity of the current text viewport.</summary>
        public int GetVisibleLineCount() => EffectiveUIFont == null ? Math.Max(1, (int)(Size.Y / 16)) : Math.Max(1, (int)((Size.Y - Padding.Vertical) / TextMetrics.LineHeight(EffectiveUIFont)));
        public int GetVisibleLineCountInRange(int fromLine, int toLine)
        {
            ValidateLine(fromLine); ValidateLine(toLine);
            var from = Math.Min(fromLine, toLine); var to = Math.Max(fromLine, toLine); var visible = 0;
            for (var line = from; line <= to; line++) if (!IsLineHiddenForDisplay(line)) visible += GetWrapSegments(line).Count;
            return visible;
        }
        public int GetTotalVisibleLineCount()
        {
            var visible = 0;
            for (var line = 0; line < LineCount; line++) if (!IsLineHiddenForDisplay(line)) visible += GetWrapSegments(line).Count;
            return visible;
        }
        public bool IsLineInViewport(int line)
        {
            ValidateLine(line);
            if (IsLineHiddenForDisplay(line)) return false;
            var first = GetVisualRowOffset(FirstVisibleLine, FirstVisibleLineWrapIndex);
            var lineFirst = GetVisualRowOffset(line, 0); var lineLast = lineFirst + GetWrapSegments(line).Count - 1;
            var last = first + GetVisibleLineCount() - 1;
            return lineLast >= first && lineFirst <= last;
        }
        /// <summary>Moves the text viewport only when the caret would otherwise be outside it.</summary>
        public void AdjustViewportToCaret()
        {
            if (IsLineHiddenForDisplay(CaretLine)) return;
            var caretWrap = GetLineWrapIndexAtColumn(CaretLine, CaretColumnInLine);
            var first = GetVisualRowOffset(FirstVisibleLine, FirstVisibleLineWrapIndex);
            var caretRow = GetVisualRowOffset(CaretLine, caretWrap); var last = first + GetVisibleLineCount() - 1;
            if (caretRow < first) SetLineAsFirstVisible(CaretLine, caretWrap);
            else if (caretRow > last) SetLineAsLastVisible(CaretLine, caretWrap);
        }
        /// <summary>Centers the active caret line in the text viewport where possible.</summary>
        public void CenterViewportToCaret() => SetLineAsCenterVisible(CaretLine, GetLineWrapIndexAtColumn(CaretLine, CaretColumnInLine));
        protected internal override void PointerPressed(Point position)
        {
            base.PointerPressed(position);
            if (EffectiveUIFont == null) return;
            if (TryGetGutterAt(position, out var gutter))
            {
                var gutterLine = GetLineAtVisibleRow((int)((position.Y - GlobalPosition.Y - Padding.Top) / Math.Max(1, TextMetrics.LineHeight(EffectiveUIFont))));
                if (_gutters[gutter].Clickable && IsLineGutterClickable(gutterLine, gutter)) GutterClicked?.Invoke(this, gutterLine, gutter);
            }
        }
        protected override int GetCaretColumnAtPosition(Point position)
        {
            if (EffectiveUIFont == null) return Text.Length;
            GetLineAndWrapAtVisibleRow((int)((position.Y - GlobalPosition.Y - Padding.Top) / Math.Max(1, TextMetrics.LineHeight(EffectiveUIFont))), out var line, out var wrapIndex);
            var localX = position.X - GlobalPosition.X - Padding.Left - TextContentLeftInset;
            var source = GetLine(line); var segment = GetWrapSegments(line)[wrapIndex];
            var column = segment.Start + GetSegmentLayout(source, segment.Start, segment.Length).HitTest(new Vector2(localX, 0));
            return GetLineStart(line) + column;
        }
        internal override void PointerRightPressed(Point position) => OpenContextMenu(position);
        internal override void KeyPressed(Keys key)
        {
            if (key == Keys.Apps || key == Keys.F10 && HasShiftModifier()) { OpenContextMenu(new Point((int)GlobalPosition.X, (int)GlobalPosition.Y)); return; }
            if (ShortcutKeysEnabled && HasCommandModifier())
            {
                if (key == Keys.A) { SelectAll(); return; }
                if (key == Keys.C) { Copy(); return; }
                if (key == Keys.X) { Cut(); return; }
                if (key == Keys.V) { Paste(); return; }
                if (key == Keys.Z) { if (HasShiftModifier()) Redo(); else Undo(); return; }
                if (key == Keys.Y) { Redo(); return; }
            }
            var movement = TextNavigation.Resolve(key, Context?.CurrentKeyboardState ?? default, UsesMacTextNavigation, true);
            if (movement != TextNavigationAction.None)
            {
                MoveCarets(movement, HasShiftModifier());
                return;
            }
            if (key is Keys.Left or Keys.Right or Keys.Up or Keys.Down) return;
            if (_secondaryCarets.Count > 0)
            {
                if (key == Keys.Back) { DeleteAtCarets(true); return; }
                if (key == Keys.Delete) { DeleteAtCarets(false); return; }
            }
            if (key == Keys.Enter) InsertNewline();
            else base.KeyPressed(key);
        }
        internal override void TextInput(char character) => InsertText(character.ToString());
        internal override void DrawEditor(UIRenderContext context)
        {
            context.Fill(Bounds, context.Theme.BackgroundColor);
            context.Border(Bounds, Context?.FocusedControl == this ? context.Theme.FocusColor : context.Theme.PanelBorderColor);
            if (EffectiveUIFont != null)
            {
                var position = GlobalPosition + new Vector2(Padding.Left + TextContentLeftInset, Padding.Top);
                DrawGutters(context);
                for (var line = FirstVisibleLine; line >= 0 && line < LineCount && position.Y + TextMetrics.LineHeight(EffectiveUIFont) <= Bounds.Bottom; line++)
                {
                    if (IsLineHiddenForDisplay(line)) continue;
                    var source = GetLine(line);
                    var segments = GetWrapSegments(line);
                    var firstWrap = line == FirstVisibleLine ? FirstVisibleLineWrapIndex : 0;
                    for (var wrapIndex = firstWrap; wrapIndex < segments.Count; wrapIndex++)
                    {
                        if (position.Y + TextMetrics.LineHeight(EffectiveUIFont) > Bounds.Bottom) break;
                        var segment = segments[wrapIndex];
                        if (_lineBackgroundColors.TryGetValue(line, out var background)) context.Fill(new Rectangle(Bounds.X, (int)position.Y, Bounds.Width, TextMetrics.LineHeight(EffectiveUIFont)), background);
                        var text = source.Substring(segment.Start, segment.Length);
                        DrawSelectionHighlights(context, line, segment.Start, segment.Length, position);
                        context.Text(EffectiveUIFont, text, position, context.Theme.TextColor);
                        DrawSyntaxHighlighting(context, line, source, segment.Start, segment.Length, position);
                        DrawControlCharacterIcons(context, source, segment.Start, segment.Length, position);
                        position.Y += TextMetrics.LineHeight(EffectiveUIFont);
                    }
                }
                if (Context?.FocusedControl == this)
                {
                    var wrapIndex = GetLineWrapIndexAtColumn(CaretLine, CaretColumnInLine); var segment = GetWrapSegments(CaretLine)[wrapIndex];
                    var segmentLayout = GetSegmentLayout(GetLine(CaretLine), segment.Start, segment.Length);
                    var x = GlobalPosition.X + Padding.Left + TextContentLeftInset + segmentLayout.GetCaretPosition(CaretColumnInLine - segment.Start).X;
                    var y = GlobalPosition.Y + Padding.Top + GetVisibleRow(CaretLine, wrapIndex) * TextMetrics.LineHeight(EffectiveUIFont);
                    if (y >= Bounds.Top && y < Bounds.Bottom) context.Fill(new Rectangle((int)x, (int)y, 1, Math.Max(1, TextMetrics.LineHeight(EffectiveUIFont))), context.Theme.FocusColor);
                    for (var caret = 1; caret < CaretCount; caret++)
                    {
                        var line = GetCaretLine(caret); var column = GetCaretColumn(caret); var secondaryWrap = GetLineWrapIndexAtColumn(line, column); var secondarySegment = GetWrapSegments(line)[secondaryWrap];
                        var secondaryLayout = GetSegmentLayout(GetLine(line), secondarySegment.Start, secondarySegment.Length);
                        var secondaryX = GlobalPosition.X + Padding.Left + TextContentLeftInset + secondaryLayout.GetCaretPosition(column - secondarySegment.Start).X;
                        var secondaryY = GlobalPosition.Y + Padding.Top + GetVisibleRow(line, secondaryWrap) * TextMetrics.LineHeight(EffectiveUIFont);
                        if (secondaryY >= Bounds.Top && secondaryY < Bounds.Bottom) context.Fill(new Rectangle((int)secondaryX, (int)secondaryY, 1, Math.Max(1, TextMetrics.LineHeight(EffectiveUIFont))), context.Theme.FocusColor);
                    }
                }
            }
        }
        private void DrawControlCharacterIcons(UIRenderContext context, string source, int start, int length, Vector2 position)
        {
            if (!DrawControlCharacters) return;
            var layout = GetSegmentLayout(source, start, length);
            for (var offset = 0; offset < length; offset++)
            {
                var name = source[start + offset] == '\t' ? "tab" : source[start + offset] == ' ' ? "space" : null;
                if (name == null) continue;
                var icon = GetThemeIcon(name);
                if (!icon.HasValue) continue;
                var x = position.X + layout.GetCaretPosition(offset).X;
                var advance = layout.GetCaretPosition(offset + 1).X - layout.GetCaretPosition(offset).X;
                context.Icon(icon.Value, new Vector2(x + (advance - icon.Value.LogicalSize.X) / 2, position.Y + (TextMetrics.LineHeight(EffectiveUIFont) - icon.Value.LogicalSize.Y) / 2), context.Theme.DisabledTextColor);
            }
        }
        private int GetLineForIndex(int index)
        {
            var line = 0;
            for (var i = 0; i < Math.Min(index, Text.Length); i++) if (Text[i] == '\n') line++;
            return line;
        }
        private int GetLineStart(int line)
        {
            var start = 0;
            while (line-- > 0)
            {
                var next = Text.IndexOf('\n', start);
                if (next < 0) return Text.Length;
                start = next + 1;
            }
            return start;
        }
        private List<string> GetLines() => new List<string>(Text.Split('\n'));
        private void ValidateLine(int line) { if (line < 0 || line >= LineCount) throw new ArgumentOutOfRangeException(nameof(line)); }
        private void ValidateGutter(int gutter) { if (gutter < 0 || gutter >= _gutters.Count) throw new ArgumentOutOfRangeException(nameof(gutter)); }
        private TextEditGutter GetGutter(int gutter) { ValidateGutter(gutter); return _gutters[gutter]; }
        private TextEditGutterItem GetLineGutterItem(int line, int gutter, bool create)
        {
            ValidateLine(line); ValidateGutter(gutter); var key = (line, gutter);
            if (!_lineGutterItems.TryGetValue(key, out var item) && create) { item = new TextEditGutterItem(); _lineGutterItems[key] = item; }
            return item;
        }
        private void DrawGutters(UIRenderContext context)
        {
            if (_gutters.Count == 0 || EffectiveUIFont == null) return;
            var x = Bounds.X;
            for (var index = 0; index < _gutters.Count; index++)
            {
                var gutter = _gutters[index]; if (!gutter.Draw) continue;
                var rect = new Rectangle(x, Bounds.Y, gutter.Width, Bounds.Height); context.Fill(rect, context.Theme.PanelColor); x += gutter.Width;
            }
            var y = Bounds.Y + Padding.Top;
            for (var line = FirstVisibleLine; line >= 0 && line < LineCount && y + TextMetrics.LineHeight(EffectiveUIFont) <= Bounds.Bottom; line++)
            {
                if (IsLineHiddenForDisplay(line)) continue;
                var firstWrap = line == FirstVisibleLine ? FirstVisibleLineWrapIndex : 0;
                if (firstWrap == 0)
                {
                x = Bounds.X;
                for (var index = 0; index < _gutters.Count; index++)
                {
                    var gutter = _gutters[index]; if (!gutter.Draw) continue;
                    var rect = new Rectangle(x, (int)y, gutter.Width, TextMetrics.LineHeight(EffectiveUIFont)); var item = GetLineGutterItem(line, index, false);
                    if (gutter.Type == TextEditGutterType.Custom) gutter.CustomDraw?.Invoke(context, this, line, rect);
                    else if (gutter.Type == TextEditGutterType.Icon && item?.Icon != null) context.SpriteBatch.Draw(item.Icon, new Rectangle(rect.X + 2, rect.Y + 2, Math.Max(1, Math.Min(rect.Width - 4, TextMetrics.LineHeight(EffectiveUIFont) - 4)), Math.Max(1, Math.Min(rect.Height - 4, TextMetrics.LineHeight(EffectiveUIFont) - 4))), item.Color);
                    else if (gutter.Type == TextEditGutterType.String && item != null) context.Text(EffectiveUIFont, item.Text, new Vector2(rect.X + 2, rect.Y), item.Color);
                    x += gutter.Width;
                }
                }
                y += TextMetrics.LineHeight(EffectiveUIFont) * (GetWrapSegments(line).Count - firstWrap);
            }
        }
        private bool TryGetGutterAt(Point point, out int gutter)
        {
            var x = Bounds.X;
            for (var index = 0; index < _gutters.Count; index++)
            {
                var state = _gutters[index]; if (!state.Draw) continue;
                if (new Rectangle(x, Bounds.Y, state.Width, Bounds.Height).Contains(point)) { gutter = index; return true; }
                x += state.Width;
            }
            gutter = -1; return false;
        }
        private void TrackTextChange(LineEdit _, string text)
        {
            var previousText = _historyText;
            if (!_restoringHistory && _historyText != text)
            {
                if (_currentAction != TextEditEditAction.None) _actionChanged = true;
                else
                {
                    if (UndoEnabled) PushUndoState(_historyText, _version);
                    _version++;
                }
            }
            _historyText = text;
            var firstChangedLine = GetFirstChangedLine(previousText, text);
            _syntaxHighlighter?.InvalidateFromLine(firstChangedLine);
            InvalidateWrapLayoutAfterEdit(previousText, text, firstChangedLine);
            if (FirstVisibleLine >= LineCount || IsLineHiddenForDisplay(FirstVisibleLine)) SetFirstVisibleRow(Math.Min(FirstVisibleLine, LineCount - 1), 0);
            else FirstVisibleLineWrapIndex = Math.Min(FirstVisibleLineWrapIndex, Math.Max(0, GetWrapSegments(FirstVisibleLine).Count - 1));
        }
        private void SyntaxHighlighterChanged(object _, EventArgs __) => QueueLayout();
        private void DrawSyntaxHighlighting(UIRenderContext context, int line, string source, int segmentStart, int segmentLength, Vector2 position)
        {
            if (_syntaxHighlighter == null || string.IsNullOrEmpty(source)) return;
            var layout = GetSegmentLayout(source, segmentStart, segmentLength);
            foreach (var span in _syntaxHighlighter.GetLineSyntaxHighlighting(line))
            {
                var start = Math.Max(segmentStart, span.StartColumn); var end = Math.Min(segmentStart + segmentLength, span.StartColumn + span.Length);
                var length = end - start;
                if (length <= 0) continue;
                foreach (var rectangle in layout.GetSelectionRectangles(start - segmentStart, length))
                {
                    context.PushClip(new Rectangle((int)MathF.Floor(position.X + rectangle.X), (int)MathF.Floor(position.Y + rectangle.Y), Math.Max(1, (int)MathF.Ceiling(rectangle.Width)), Math.Max(1, (int)MathF.Ceiling(rectangle.Height))));
                    try { context.Text(layout, position, span.Color); }
                    finally { context.PopClip(); }
                }
            }
        }
        private void DrawSelectionHighlights(UIRenderContext context, int line, int segmentStart, int segmentLength, Vector2 position)
        {
            var sourceStart = GetLineStart(line); var absoluteStart = sourceStart + segmentStart; var absoluteEnd = absoluteStart + segmentLength; var source = GetLine(line);
            for (var caret = 0; caret < CaretCount; caret++)
            {
                if (!HasCaretSelection(caret)) continue;
                var start = Math.Max(GetSelectionFromIndex(caret), absoluteStart); var end = Math.Min(GetSelectionToIndex(caret), absoluteEnd);
                if (end <= start) continue;
                var layout = GetSegmentLayout(source, segmentStart, segmentLength);
                foreach (var rectangle in layout.GetSelectionRectangles(start - absoluteStart, end - start))
                    context.Fill(new Rectangle((int)(position.X + rectangle.X), (int)(position.Y + rectangle.Y), Math.Max(1, (int)MathF.Ceiling(rectangle.Width)), Math.Max(1, (int)MathF.Ceiling(rectangle.Height))), context.Theme.HoverColor);
            }
        }
        private TextLayout GetSegmentLayout(string source, int start, int length) => GetEditingLayout(source.Substring(start, length));
        private new float MeasureTextWidth(string text) => EffectiveUIFont == null ? text.Length * 8 : TextMetrics.Measure(EffectiveUIFont, text).X;
        private void MoveCarets(TextNavigationAction movement, bool shift)
        {
            _caretMergeSuspension++;
            try
            {
                for (var caret = 0; caret < CaretCount; caret++)
                {
                    var anchor = GetSelectionOriginIndex(caret);
                    var index = GetCaretIndex(caret);
                    if (!shift && HasCaretSelection(caret) && movement is TextNavigationAction.PreviousCharacter or TextNavigationAction.NextCharacter)
                        index = movement == TextNavigationAction.PreviousCharacter ? GetSelectionFromIndex(caret) : GetSelectionToIndex(caret);
                    else index = GetNavigationTarget(movement, index);
                    SetCaretAtTextIndex(index, caret);
                    if (shift)
                    {
                        if (caret == 0) base.Select(anchor, index);
                        else GetSecondaryCaret(caret).Anchor = anchor;
                    }
                }
            }
            finally { _caretMergeSuspension--; }
            var sorted = GetSortedCaretIndexes();
            for (var index = 1; index < sorted.Count; index++)
                if (GetSelectionFromIndex(sorted[index]) <= GetSelectionToIndex(sorted[index - 1]))
                {
                    var carets = Enumerable.Range(0, CaretCount)
                        .Select(caret => new TextCaretRange(GetSelectionOriginIndex(caret), GetCaretIndex(caret))).ToArray();
                    var merged = TextEditRanges.Merge(carets);
                    base.Select(merged[0].Anchor, merged[0].Index);
                    _secondaryCarets.Clear();
                    foreach (var caret in merged.Skip(1))
                        _secondaryCarets.Add(new SecondaryCaret { Anchor = caret.Anchor, Index = caret.Index });
                    break;
                }
            AdjustViewportToCaret();
        }
        private void DeleteAtCarets(bool backward)
        {
            var edits = new List<CaretEdit>();
            for (var caret = 0; caret < CaretCount; caret++)
            {
                var start = GetSelectionFromIndex(caret); var end = GetSelectionToIndex(caret);
                if (start == end)
                {
                    var action = TextNavigation.Resolve(backward ? Keys.Left : Keys.Right,
                        Context?.CurrentKeyboardState ?? default, UsesMacTextNavigation, true);
                    if (backward) start = GetNavigationTarget(action, start);
                    else end = GetNavigationTarget(action, end);
                }
                edits.Add(new CaretEdit { Caret = caret, Start = start, End = end });
            }
            var result = TextEditRanges.Delete(Text, edits.Select(edit => (edit.Start, edit.End)).ToArray());
            ApplyTextWithCarets(result.Text, result.Carets);
        }
        private bool HasAnyCaretSelection()
        {
            for (var caret = 0; caret < CaretCount; caret++) if (HasCaretSelection(caret)) return true;
            return false;
        }
        private string GetWordAtCaret(int caret)
        {
            return TryGetWordRange(caret, out var line, out var from, out var to) ? GetLine(line).Substring(from, to - from) : string.Empty;
        }
        private bool TryGetWordRange(int caret, out int line, out int from, out int to)
        {
            line = GetCaretLine(caret); var text = GetLine(line); var column = GetCaretColumn(caret); from = to = 0;
            if (string.IsNullOrEmpty(text)) return false;
            var index = Math.Min(column, text.Length - 1);
            if (!IsWordCharacter(text[index]) && index > 0 && IsWordCharacter(text[index - 1])) index--;
            if (!IsWordCharacter(text[index])) return false;
            from = index; to = index + 1;
            while (from > 0 && IsWordCharacter(text[from - 1])) from--;
            while (to < text.Length && IsWordCharacter(text[to])) to++;
            return true;
        }
        private List<CaretState> CaptureCaretStates()
        {
            var states = new List<CaretState>();
            for (var caret = 0; caret < CaretCount; caret++) states.Add(new CaretState { Index = GetCaretIndex(caret), Anchor = HasCaretSelection(caret) ? GetSelectionOriginIndex(caret) : -1 });
            return states;
        }
        private void RestoreCaretStates(List<CaretState> states)
        {
            _caretMergeSuspension++;
            try
            {
                var primary = states[0];
                if (primary.Anchor >= 0) base.Select(MathHelper.Clamp(primary.Anchor, 0, Text.Length), MathHelper.Clamp(primary.Index, 0, Text.Length));
                else { CaretColumn = MathHelper.Clamp(primary.Index, 0, Text.Length); base.Deselect(); }
                for (var caret = 1; caret < states.Count; caret++)
                {
                    var state = states[caret]; var secondary = GetSecondaryCaret(caret); secondary.Index = MathHelper.Clamp(state.Index, 0, Text.Length); secondary.Anchor = state.Anchor < 0 ? -1 : MathHelper.Clamp(state.Anchor, 0, Text.Length);
                }
            }
            finally { _caretMergeSuspension--; }
            MergeOverlappingCarets();
        }
        private void RestoreCaretLineStates(List<CaretLineState> states, int fromLine, int toLine)
        {
            _caretMergeSuspension++;
            try
            {
                for (var caret = 0; caret < states.Count; caret++)
                {
                    var state = states[caret]; var line = SwapLineNumber(state.Line, fromLine, toLine); var originLine = SwapLineNumber(state.OriginLine, fromLine, toLine);
                    var index = GetLineStart(line) + MathHelper.Clamp(state.Column, 0, GetLine(line).Length);
                    var origin = GetLineStart(originLine) + MathHelper.Clamp(state.OriginColumn, 0, GetLine(originLine).Length);
                    if (caret == 0)
                    {
                        if (state.Selected) base.Select(origin, index); else { CaretColumn = index; base.Deselect(); }
                    }
                    else { var secondary = GetSecondaryCaret(caret); secondary.Index = index; secondary.Anchor = state.Selected ? origin : -1; }
                }
            }
            finally { _caretMergeSuspension--; }
            MergeOverlappingCarets();
        }
        private void SwapLineMetadata(int fromLine, int toLine)
        {
            var hasFromBackground = _lineBackgroundColors.TryGetValue(fromLine, out var fromBackground); var hasToBackground = _lineBackgroundColors.TryGetValue(toLine, out var toBackground);
            _lineBackgroundColors.Remove(fromLine); _lineBackgroundColors.Remove(toLine);
            if (hasFromBackground) _lineBackgroundColors[toLine] = fromBackground;
            if (hasToBackground) _lineBackgroundColors[fromLine] = toBackground;
            var moved = new List<KeyValuePair<(int Line, int Gutter), TextEditGutterItem>>();
            foreach (var item in _lineGutterItems) if (item.Key.Line == fromLine || item.Key.Line == toLine) moved.Add(item);
            foreach (var item in moved) _lineGutterItems.Remove(item.Key);
            foreach (var item in moved) _lineGutterItems[(SwapLineNumber(item.Key.Line, fromLine, toLine), item.Key.Gutter)] = item.Value;
        }
        private static int SwapLineNumber(int line, int fromLine, int toLine) => line == fromLine ? toLine : line == toLine ? fromLine : line;
        private static int CollapseTextIndex(int index, int from, int to) => index <= from ? index : index >= to ? index - (to - from) : from;
        private bool HasCommandModifier()
        {
            return TextInputShortcuts.HasCommandModifier(Context?.CurrentKeyboardState ?? default);
        }
        private bool HasShiftModifier()
        {
            var keyboard = Context?.CurrentKeyboardState ?? default;
            return keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        }
        private void OpenContextMenu(Point position)
        {
            if (!ContextMenuEnabled || Context == null) return;
            _contextMenu.Clear(); _contextMenu.Font = Font;
            var directionMenu = GetTextDirectionMenu();
            directionMenu.Clear(); directionMenu.Font = Font;
            directionMenu.AddRadioCheckItem("Same as Layout Direction", (int)TextEditMenuOption.DirectionInherited).Checked = TextDirection == TextDirection.Inherited;
            directionMenu.AddRadioCheckItem("Auto-Detect Direction", (int)TextEditMenuOption.DirectionAuto).Checked = TextDirection == TextDirection.Auto;
            directionMenu.AddRadioCheckItem("Left-to-Right", (int)TextEditMenuOption.DirectionLeftToRight).Checked = TextDirection == TextDirection.LeftToRight;
            directionMenu.AddRadioCheckItem("Right-to-Left", (int)TextEditMenuOption.DirectionRightToLeft).Checked = TextDirection == TextDirection.RightToLeft;
            var controlCharacterMenu = GetControlCharacterMenu();
            controlCharacterMenu.Clear(); controlCharacterMenu.Font = Font;
            controlCharacterMenu.AddItem("Left-to-Right Mark (LRM)", (int)TextEditMenuOption.InsertLeftToRightMark);
            controlCharacterMenu.AddItem("Right-to-Left Mark (RLM)", (int)TextEditMenuOption.InsertRightToLeftMark);
            controlCharacterMenu.AddItem("Start of Left-to-Right Embedding (LRE)", (int)TextEditMenuOption.InsertLeftToRightEmbedding);
            controlCharacterMenu.AddItem("Start of Right-to-Left Embedding (RLE)", (int)TextEditMenuOption.InsertRightToLeftEmbedding);
            controlCharacterMenu.AddItem("Start of Left-to-Right Override (LRO)", (int)TextEditMenuOption.InsertLeftToRightOverride);
            controlCharacterMenu.AddItem("Start of Right-to-Left Override (RLO)", (int)TextEditMenuOption.InsertRightToLeftOverride);
            controlCharacterMenu.AddItem("Pop Direction Formatting (PDF)", (int)TextEditMenuOption.InsertPopDirectionFormatting);
            controlCharacterMenu.AddSeparator();
            controlCharacterMenu.AddItem("Arabic Letter Mark (ALM)", (int)TextEditMenuOption.InsertArabicLetterMark);
            controlCharacterMenu.AddItem("Left-to-Right Isolate (LRI)", (int)TextEditMenuOption.InsertLeftToRightIsolate);
            controlCharacterMenu.AddItem("Right-to-Left Isolate (RLI)", (int)TextEditMenuOption.InsertRightToLeftIsolate);
            controlCharacterMenu.AddItem("First Strong Isolate (FSI)", (int)TextEditMenuOption.InsertFirstStrongIsolate);
            controlCharacterMenu.AddItem("Pop Direction Isolate (PDI)", (int)TextEditMenuOption.InsertPopDirectionIsolate);
            controlCharacterMenu.AddSeparator();
            controlCharacterMenu.AddItem("Zero-Width Joiner (ZWJ)", (int)TextEditMenuOption.InsertZeroWidthJoiner);
            controlCharacterMenu.AddItem("Zero-Width Non-Joiner (ZWNJ)", (int)TextEditMenuOption.InsertZeroWidthNonJoiner);
            controlCharacterMenu.AddItem("Word Joiner (WJ)", (int)TextEditMenuOption.InsertWordJoiner);
            controlCharacterMenu.AddItem("Soft Hyphen (SHY)", (int)TextEditMenuOption.InsertSoftHyphen);
            _contextMenu.AddItem("Cut", (int)TextEditMenuOption.Cut).Disabled = !Editable;
            _contextMenu.AddItem("Copy", (int)TextEditMenuOption.Copy);
            _contextMenu.AddItem("Paste", (int)TextEditMenuOption.Paste).Disabled = !Editable;
            _contextMenu.AddSeparator();
            _contextMenu.AddItem("Select All", (int)TextEditMenuOption.SelectAll);
            _contextMenu.AddItem("Clear", (int)TextEditMenuOption.Clear).Disabled = !Editable;
            _contextMenu.AddSeparator();
            _contextMenu.AddItem("Undo", (int)TextEditMenuOption.Undo).Disabled = !Editable || !HasUndo;
            _contextMenu.AddItem("Redo", (int)TextEditMenuOption.Redo).Disabled = !Editable || !HasRedo;
            _contextMenu.AddSeparator();
            _contextMenu.AddSubmenuNodeItem("Text Writing Direction", directionMenu, (int)TextEditMenuOption.SubmenuTextDirection);
            _contextMenu.AddSeparator();
            _contextMenu.AddCheckItem("Display Control Characters", (int)TextEditMenuOption.DisplayControlCharacters).Checked = DrawControlCharacters;
            _contextMenu.AddSubmenuNodeItem("Insert Control Character", controlCharacterMenu, (int)TextEditMenuOption.SubmenuInsertControlCharacter).Disabled = !Editable;
            if (_contextMenu.Context != Context) Context.Add(_contextMenu);
            _contextMenu.PopupAt(new Vector2(position.X, position.Y), null);
        }
        private List<int> GetSortedCaretIndexes()
        {
            var indexes = new List<int>(); for (var caret = 0; caret < CaretCount; caret++) indexes.Add(caret);
            indexes.Sort((left, right) => GetSelectionFromIndex(left).CompareTo(GetSelectionFromIndex(right))); return indexes;
        }
        private List<CaretLineRange> GetCaretLineRanges(int caret)
        {
            var indexes = caret < 0 ? GetSortedCaretIndexes() : new List<int> { caret }; var ranges = new List<CaretLineRange>(); var last = int.MinValue;
            foreach (var index in indexes)
            {
                var first = GetCaretLine(index); var line = first;
                if (HasCaretSelection(index))
                {
                    line = GetLineForIndex(GetSelectionToIndex(index));
                    if (GetSelectionToIndex(index) == GetLineStart(line) && line > first) line--;
                }
                if (ranges.Count > 0 && first <= last + 1) ranges[ranges.Count - 1].Last = Math.Max(ranges[ranges.Count - 1].Last, line);
                else ranges.Add(new CaretLineRange(first, line));
                last = Math.Max(last, line);
            }
            return ranges;
        }
        private void DeleteCaretSelections(int caret)
        {
            var edits = new List<CaretEdit>();
            for (var index = 0; index < CaretCount; index++)
            {
                var target = caret < 0 || index == caret; var start = GetSelectionFromIndex(index); var end = target && HasCaretSelection(index) ? GetSelectionToIndex(index) : start;
                edits.Add(new CaretEdit { Caret = index, Start = start, End = end });
            }
            ApplyMultiCaretEdits(edits, string.Empty);
        }
        private void DeleteCaretLines(int caret)
        {
            var lines = GetLines(); var ranges = GetCaretLineRanges(caret);
            for (var index = ranges.Count - 1; index >= 0; index--) lines.RemoveRange(ranges[index].First, ranges[index].Last - ranges[index].First + 1);
            if (lines.Count == 0) lines.Add(string.Empty);
            Text = string.Join("\n", lines); RemoveSecondaryCarets();
            for (var index = 0; index < ranges.Count; index++)
            {
                var line = Math.Min(ranges[index].First, lines.Count - 1); if (index == 0) SetCaret(line, 0); else AddCaret(line, 0);
            }
        }
        private void ApplyMultiCaretEdits(List<CaretEdit> edits, string replacement)
            => ApplyMultiCaretEdits(edits, _ => replacement);
        private void ApplyMultiCaretEdits(List<CaretEdit> edits, Func<CaretEdit, string> replacement)
        {
            edits.Sort((left, right) => left.Start.CompareTo(right.Start));
            for (var index = 1; index < edits.Count; index++) if (edits[index].Start < edits[index - 1].End || edits[index].Start == edits[index - 1].Start) return;
            var builder = new System.Text.StringBuilder(Text.Length); var cursor = 0; var finalIndexes = new int[CaretCount];
            foreach (var edit in edits)
            {
                var inserted = replacement(edit) ?? string.Empty;
                builder.Append(Text, cursor, edit.Start - cursor); builder.Append(inserted); finalIndexes[edit.Caret] = builder.Length; cursor = edit.End;
            }
            builder.Append(Text, cursor, Text.Length - cursor);
            ApplyTextWithCarets(builder.ToString(), finalIndexes);
        }
        private void ApplyTextWithCarets(string text, int[] finalIndexes)
        {
            _updatingMultipleCarets = true;
            try { Text = text; CaretColumn = finalIndexes[0]; Deselect(); for (var caret = 1; caret < CaretCount; caret++) { var secondary = _secondaryCarets[caret - 1]; secondary.Index = finalIndexes[caret]; secondary.Anchor = -1; } }
            finally { _updatingMultipleCarets = false; }
            MergeOverlappingCarets();
        }
        private void SetCaretAtTextIndex(int index, int caret)
        {
            index = MathHelper.Clamp(index, 0, Text.Length); var line = GetLineForIndex(index); SetCaret(line, index - GetLineStart(line), caret);
        }
        internal override int GetNavigationTarget(TextNavigationAction action, int from)
        {
            var line = GetLineForIndex(from);
            var start = GetLineStart(line);
            var column = from - start;
            if (action is TextNavigationAction.LineStart or TextNavigationAction.LineEnd)
            {
                var segment = GetWrapSegments(line)[GetLineWrapIndexAtColumn(line, column)];
                return start + segment.Start + (action == TextNavigationAction.LineEnd ? segment.Length : 0);
            }
            if (action == TextNavigationAction.PreviousParagraph)
                return column == 0 && line > 0 ? GetLineStart(line - 1) : start;
            if (action == TextNavigationAction.NextParagraph)
            {
                if (column == GetLine(line).Length && line < LineCount - 1) line++;
                return GetLineStart(line) + GetLine(line).Length;
            }
            if (action == TextNavigationAction.PreviousRow) return GetVisualRowTarget(from, -1);
            if (action == TextNavigationAction.NextRow) return GetVisualRowTarget(from, 1);
            if (action == TextNavigationAction.PreviousCharacter) return GetAdjacentGraphemeIndex(from, -1);
            if (action == TextNavigationAction.NextCharacter) return GetAdjacentGraphemeIndex(from, 1);
            return base.GetNavigationTarget(action, from);
        }
        private int GetVisualRowTarget(int from, int direction)
        {
            var line = GetLineForIndex(from); var sourceColumn = from - GetLineStart(line); var segments = GetWrapSegments(line); var wrapIndex = GetLineWrapIndexAtColumn(line, sourceColumn);
            var targetLine = line; var targetWrap = wrapIndex + direction;
            if (targetWrap < 0)
            {
                targetLine = line - 1; while (targetLine >= 0 && IsLineHiddenForDisplay(targetLine)) targetLine--;
                if (targetLine < 0) return from;
                targetWrap = Math.Max(0, GetWrapSegments(targetLine).Count - 1);
            }
            else if (targetWrap >= segments.Count)
            {
                targetLine = line + 1; while (targetLine < LineCount && IsLineHiddenForDisplay(targetLine)) targetLine++;
                if (targetLine >= LineCount) return from;
                targetWrap = 0;
            }
            var current = segments[wrapIndex]; var targetSource = GetLine(targetLine); var target = GetWrapSegments(targetLine)[targetWrap];
            if (EffectiveUIFont == null)
            {
                return GetLineStart(targetLine) + target.Start + Math.Min(Math.Max(0, sourceColumn - current.Start), target.Length);
            }
            var currentLayout = GetSegmentLayout(GetLine(line), current.Start, current.Length);
            var targetLayout = GetSegmentLayout(targetSource, target.Start, target.Length);
            var preferredX = currentLayout.GetCaretPosition(MathHelper.Clamp(sourceColumn - current.Start, 0, current.Length)).X;
            var targetColumn = target.Start + targetLayout.HitTest(new Vector2(preferredX, 0));
            return GetLineStart(targetLine) + targetColumn;
        }
        private int GetAdjacentGraphemeIndex(int index, int direction)
        {
            index = MathHelper.Clamp(index, 0, Text.Length);
            if (EffectiveUIFont == null) return TextNavigation.GraphemeBoundary(Text, index, direction);
            var line = GetLineForIndex(index);
            var lineStart = GetLineStart(line);
            var column = index - lineStart;
            var source = GetLine(line);
            if (direction < 0)
            {
                if (column == 0) return Math.Max(0, index - 1);
                return lineStart + GetEditingLayout(source).GetPreviousGraphemeBoundary(column);
            }
            if (column == source.Length) return Math.Min(Text.Length, index + 1);
            return lineStart + GetEditingLayout(source).GetNextGraphemeBoundary(column);
        }
        private List<TextEditWrapSegment> GetWrapSegments(int line)
        {
            EnsureWrapLayout();
            if (!_wrapLayoutCache.TryGetValue(line, out var segments))
            {
                segments = BuildWrapSegments(GetLine(line), GetEffectiveWrapWidth());
                _wrapLayoutCache[line] = segments;
                WrapLayoutBuildCount++;
            }
            return segments;
        }
        private void EnsureWrapLayout()
        {
            var width = GetEffectiveWrapWidth();
            var font = EffectiveUIFont;
            var identity = font?.Identity ?? default;
            var size = font?.Size ?? 0;
            if (_wrapLayoutText == Text && _wrapLayoutWidth == width && _wrapLayoutFontIdentity == identity && _wrapLayoutFontSize == size && _wrapLayoutLanguage == Language && _wrapLayoutDirection == TextDirection) return;
            _wrapLayoutText = Text; _wrapLayoutWidth = width; _wrapLayoutFontIdentity = identity; _wrapLayoutFontSize = size; _wrapLayoutLanguage = Language; _wrapLayoutDirection = TextDirection; _wrapLayoutCache.Clear();
        }
        private void InvalidateWrapLayout() { _wrapLayoutText = null; _wrapLayoutWidth = -1; _wrapLayoutCache.Clear(); }
        private void InvalidateWrapLayoutAfterEdit(string previousText, string text, int firstChangedLine)
        {
            var previousLineCount = CountLines(previousText);
            var currentLineCount = CountLines(text);
            if (previousLineCount != currentLineCount)
            {
                foreach (var line in new List<int>(_wrapLayoutCache.Keys)) if (line >= firstChangedLine) _wrapLayoutCache.Remove(line);
            }
            else
            {
                var lastChangedLine = GetLastChangedLine(previousText, text);
                foreach (var line in new List<int>(_wrapLayoutCache.Keys)) if (line >= firstChangedLine && line <= lastChangedLine) _wrapLayoutCache.Remove(line);
            }
            _wrapLayoutText = text;
        }
        private float GetEffectiveWrapWidth()
        {
            if (LineWrappingMode == TextEditLineWrappingMode.None) return float.PositiveInfinity;
            var glyphWidth = EffectiveUIFont == null ? 8 : TextMetrics.Measure(EffectiveUIFont, "0").X;
            if (WrapAtColumn > 0) return Math.Max(1, glyphWidth * WrapAtColumn);
            return Math.Max(1, Size.X - Padding.Horizontal - TextContentLeftInset);
        }
        private List<TextEditWrapSegment> BuildWrapSegments(string text, float wrapWidth)
        {
            var segments = new List<TextEditWrapSegment>();
            if (LineWrappingMode != TextEditLineWrappingMode.None && EffectiveUIFont == null && WrapAtColumn > 0) return BuildColumnWrapSegments(text, WrapAtColumn);
            if (!float.IsFinite(wrapWidth) || EffectiveUIFont == null || string.IsNullOrEmpty(text)) { segments.Add(new TextEditWrapSegment(0, text.Length)); return segments; }
            var layout = GetEditingLayout(text);
            var start = 0;
            while (start < text.Length)
            {
                var end = start;
                var lastBreak = -1;
                var width = 0f;
                foreach (var cluster in layout.Clusters)
                {
                    if (cluster.Start < start) continue;
                    var clusterWidth = Math.Max(cluster.Bounds.Width, MathF.Abs(layout.GetCaretPosition(cluster.Start + cluster.Length).X - layout.GetCaretPosition(cluster.Start).X));
                    if (end > start && width + clusterWidth > wrapWidth) break;
                    width += clusterWidth;
                    end = cluster.Start + cluster.Length;
                    if (char.IsWhiteSpace(text[cluster.Start])) lastBreak = end;
                }
                if (end <= start) end = layout.GetNextGraphemeBoundary(start);
                if (end < text.Length && lastBreak > start) end = lastBreak;
                segments.Add(new TextEditWrapSegment(start, end - start));
                start = end;
            }
            return segments;
        }
        private static List<TextEditWrapSegment> BuildColumnWrapSegments(string text, int wrapColumn)
        {
            var segments = new List<TextEditWrapSegment>();
            if (wrapColumn <= 0 || text.Length <= wrapColumn) { segments.Add(new TextEditWrapSegment(0, text.Length)); return segments; }
            var start = 0;
            while (start < text.Length)
            {
                var end = Math.Min(text.Length, start + wrapColumn);
                if (end < text.Length)
                    for (var candidate = end - 1; candidate > start; candidate--)
                        if (char.IsWhiteSpace(text[candidate])) { end = candidate + 1; break; }
                if (end <= start) end = Math.Min(text.Length, start + wrapColumn);
                segments.Add(new TextEditWrapSegment(start, end - start));
                start = end;
            }
            return segments;
        }
        private static int GetFirstChangedLine(string previousText, string text)
        {
            var common = 0;
            while (common < previousText.Length && common < text.Length && previousText[common] == text[common]) common++;
            var line = 0;
            for (var index = 0; index < common; index++) if (text[index] == '\n') line++;
            return line;
        }
        private static int GetLastChangedLine(string previousText, string text)
        {
            var suffix = 0;
            while (suffix < previousText.Length && suffix < text.Length && previousText[previousText.Length - suffix - 1] == text[text.Length - suffix - 1]) suffix++;
            var changedEnd = text.Length - suffix;
            var line = 0;
            for (var index = 0; index < changedEnd; index++) if (text[index] == '\n') line++;
            return line;
        }
        private static int CountLines(string text)
        {
            var count = 1;
            foreach (var character in text) if (character == '\n') count++;
            return count;
        }
        private readonly struct TextEditWrapSegment
        {
            public TextEditWrapSegment(int start, int length) { Start = start; Length = length; }
            public int Start { get; }
            public int Length { get; }
        }
        /// <summary>Allows specialized editors to hide document lines without mutating the underlying text.</summary>
        protected virtual bool IsLineHiddenForDisplay(int line) => false;
        protected int GetLineAtVisibleRow(int row)
        {
            GetLineAndWrapAtVisibleRow(row, out var line, out _); return line;
        }
        /// <summary>Returns the source-line wrap row represented by a viewport-relative visual row.</summary>
        protected int GetLineWrapIndexAtVisibleRow(int row)
        {
            GetLineAndWrapAtVisibleRow(row, out _, out var wrapIndex); return wrapIndex;
        }
        protected int GetLineWrapStartColumn(int line, int wrapIndex)
        {
            ValidateLine(line); var segments = GetWrapSegments(line); if (wrapIndex < 0 || wrapIndex >= segments.Count) throw new ArgumentOutOfRangeException(nameof(wrapIndex));
            return segments[wrapIndex].Start;
        }
        protected int GetLineWrapLength(int line, int wrapIndex)
        {
            ValidateLine(line); var segments = GetWrapSegments(line); if (wrapIndex < 0 || wrapIndex >= segments.Count) throw new ArgumentOutOfRangeException(nameof(wrapIndex));
            return segments[wrapIndex].Length;
        }
        private void GetLineAndWrapAtVisibleRow(int row, out int line, out int wrapIndex)
        {
            row = Math.Max(0, row);
            for (line = FirstVisibleLine; line >= 0 && line < LineCount; line++)
            {
                if (IsLineHiddenForDisplay(line)) continue;
                var wrapCount = GetWrapSegments(line).Count;
                var firstWrap = line == FirstVisibleLine ? FirstVisibleLineWrapIndex : 0;
                wrapCount -= firstWrap;
                if (row < wrapCount) { wrapIndex = firstWrap + row; return; }
                row -= wrapCount;
            }
            line = Math.Max(0, LineCount - 1); wrapIndex = Math.Max(0, GetWrapSegments(line).Count - 1);
        }
        protected int GetVisibleRow(int line, int wrapIndex = 0)
        {
            if (line < FirstVisibleLine || IsLineHiddenForDisplay(line)) return -1;
            var row = -FirstVisibleLineWrapIndex;
            for (var current = FirstVisibleLine; current < line; current++) if (!IsLineHiddenForDisplay(current)) row += GetWrapSegments(current).Count;
            return row + MathHelper.Clamp(wrapIndex, 0, Math.Max(0, GetWrapSegments(line).Count - 1));
        }
        private int GetVisualRowOffset(int line, int wrapIndex)
        {
            ValidateLine(line);
            var row = 0;
            for (var current = 0; current < line; current++) if (!IsLineHiddenForDisplay(current)) row += GetWrapSegments(current).Count;
            if (IsLineHiddenForDisplay(line)) return row;
            return row + MathHelper.Clamp(wrapIndex, 0, Math.Max(0, GetWrapSegments(line).Count - 1));
        }
        private void SetFirstVisibleVisualRow(int row)
        {
            var total = Math.Max(1, GetTotalVisibleLineCount()); row = MathHelper.Clamp(row, 0, total - 1);
            var line = 0;
            for (; line < LineCount; line++)
            {
                if (IsLineHiddenForDisplay(line)) continue;
                var wraps = GetWrapSegments(line).Count;
                if (row < wraps) { SetFirstVisibleRow(line, row); return; }
                row -= wraps;
            }
            SetFirstVisibleRow(Math.Max(0, LineCount - 1), Math.Max(0, GetWrapSegments(Math.Max(0, LineCount - 1)).Count - 1));
        }
        private void SetFirstVisibleRow(int line, int wrapIndex)
        {
            line = FindVisibleLineAtOrAfter(MathHelper.Clamp(line, 0, LineCount - 1));
            FirstVisibleLine = line;
            FirstVisibleLineWrapIndex = MathHelper.Clamp(wrapIndex, 0, Math.Max(0, GetWrapSegments(line).Count - 1));
        }
        protected int FindVisibleLineAtOrAfter(int line)
        {
            for (var current = Math.Max(0, line); current < LineCount; current++) if (!IsLineHiddenForDisplay(current)) return current;
            for (var current = Math.Min(LineCount - 1, line - 1); current >= 0; current--) if (!IsLineHiddenForDisplay(current)) return current;
            return 0;
        }
        protected int FindVisibleLineAtOrBefore(int line, int fallback)
        {
            for (var current = Math.Min(LineCount - 1, line); current >= 0; current--) if (!IsLineHiddenForDisplay(current)) return current;
            return FindVisibleLineAtOrAfter(fallback);
        }
        private void PushUndoState(string text, uint version)
        {
            _undoStack.Add(text); _undoVersions.Add(version);
            var maximum = Math.Max(1, UndoStackMaxSize);
            while (_undoStack.Count > maximum) { _undoStack.RemoveAt(0); _undoVersions.RemoveAt(0); }
            _redoStack.Clear(); _redoVersions.Clear();
        }
        private void RestoreHistory(string text, uint version) { _restoringHistory = true; Text = text; _restoringHistory = false; _historyText = Text; _version = version; CaretColumn = Math.Min(CaretColumn, Text.Length); Deselect(); }
        private void ShiftLineBackgrounds(int firstLine, int delta)
        {
            if (delta == 0) return;
            var moved = new List<KeyValuePair<int, Color>>(); foreach (var pair in _lineBackgroundColors) if (pair.Key >= firstLine) moved.Add(pair);
            foreach (var pair in moved) _lineBackgroundColors.Remove(pair.Key);
            foreach (var pair in moved) _lineBackgroundColors[pair.Key + delta] = pair.Value;
            ShiftLineGutterItems(firstLine, delta);
        }
        private void ShiftLineGutterItems(int firstLine, int delta)
        {
            var moved = new List<KeyValuePair<(int Line, int Gutter), TextEditGutterItem>>(); foreach (var pair in _lineGutterItems) if (pair.Key.Line >= firstLine) moved.Add(pair);
            foreach (var pair in moved) _lineGutterItems.Remove(pair.Key);
            foreach (var pair in moved) _lineGutterItems[(pair.Key.Line + delta, pair.Key.Gutter)] = pair.Value;
        }
        private void RemoveLineGutterItems(int line)
        {
            var removed = new List<(int Line, int Gutter)>(); foreach (var pair in _lineGutterItems) if (pair.Key.Line == line) removed.Add(pair.Key);
            foreach (var key in removed) _lineGutterItems.Remove(key);
        }
        private void ShiftGutterItems(int firstGutter, int delta, int removedGutter = -1)
        {
            var moved = new List<KeyValuePair<(int Line, int Gutter), TextEditGutterItem>>(_lineGutterItems); _lineGutterItems.Clear();
            foreach (var pair in moved)
            {
                if (pair.Key.Gutter == removedGutter) continue;
                var gutter = pair.Key.Gutter >= firstGutter ? pair.Key.Gutter + delta : pair.Key.Gutter;
                _lineGutterItems[(pair.Key.Line, gutter)] = pair.Value;
            }
        }
        private SecondaryCaret GetSecondaryCaret(int caret)
        {
            if (caret <= 0 || caret >= CaretCount) throw new ArgumentOutOfRangeException(nameof(caret));
            return _secondaryCarets[caret - 1];
        }
        private void RequestCaretMerge() { if (_caretMergeSuspension == 0) MergeOverlappingCarets(); }
        private int GetCaretIndex(int caret) => caret == 0 ? CaretColumn : GetSecondaryCaret(caret).Index;
        private int GetSelectionOriginIndex(int caret)
        {
            if (caret == 0)
            {
                if (!base.HasSelection) return CaretColumn;
                return SelectionFrom == CaretColumn ? SelectionTo : SelectionFrom;
            }
            var secondary = GetSecondaryCaret(caret); return secondary.Anchor < 0 ? secondary.Index : secondary.Anchor;
        }
        private int GetSelectionFromIndex(int caret)
        {
            if (caret == 0) return base.HasSelection ? SelectionFrom : CaretColumn;
            var secondary = GetSecondaryCaret(caret); return secondary.Anchor < 0 ? secondary.Index : Math.Min(secondary.Anchor, secondary.Index);
        }
        private int GetSelectionToIndex(int caret)
        {
            if (caret == 0) return base.HasSelection ? SelectionTo : CaretColumn;
            var secondary = GetSecondaryCaret(caret); return secondary.Anchor < 0 ? secondary.Index : Math.Max(secondary.Anchor, secondary.Index);
        }
        private bool CaretContainsIndex(int caret, int index) => index >= GetSelectionFromIndex(caret) && index <= GetSelectionToIndex(caret);
        private static bool CaretContainsIndex(SecondaryCaret caret, int index)
        {
            var from = caret.Anchor < 0 ? caret.Index : Math.Min(caret.Anchor, caret.Index); var to = caret.Anchor < 0 ? caret.Index : Math.Max(caret.Anchor, caret.Index);
            return index >= from && index <= to;
        }
        private sealed class SecondaryCaret { public int Index; public int Anchor; }
        private sealed class CaretEdit { public int Caret; public int Start; public int End; }
        private sealed class CaretLineRange { public CaretLineRange(int first, int last) { First = first; Last = last; } public int First; public int Last; }
        private sealed class CaretState { public int Index; public int Anchor; }
        private sealed class CaretLineState { public int Line; public int Column; public int OriginLine; public int OriginColumn; public bool Selected; }
        private sealed class CaretMerge { public int Caret; public int From; public int To; public bool ContainsPrimary; }
        private sealed class TextEditGutterItem
        {
            public object Metadata;
            public string Text = string.Empty;
            public Texture2D Icon;
            public Color Color = Color.White;
            public bool Clickable;
        }
        private static bool IsWholeWord(string text, int start, int length)
        {
            return (start == 0 || !IsWordCharacter(text[start - 1])) && (start + length >= text.Length || !IsWordCharacter(text[start + length]));
        }
        private static bool IsWordCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';
    }

}
