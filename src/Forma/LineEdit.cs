// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// This control API and behavior are adapted from Godot Engine's LineEdit implementation;
// see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    /// <summary>Single-line editable text. Call <see cref="InsertText"/> from the host window's text-input callback for IME-safe text entry.</summary>
    [TemplatePart(EditorPresenterPartName, typeof(LineEditPresenter))]
    public class LineEdit : TemplatedControl
    {
        public const string EditorPresenterPartName = "PART_EditorPresenter";
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.TextBox;
        public override string AccessibilityName => string.IsNullOrEmpty(base.AccessibilityName) ? Text ?? string.Empty : base.AccessibilityName;
        public override string AccessibilityValue => Text ?? string.Empty;
        public override AccessibilityActions AccessibilityActions => base.AccessibilityActions |
            (Editable ? AccessibilityActions.SetValue : AccessibilityActions.None);
        public override AccessibilityStates AccessibilityStates => base.AccessibilityStates |
            (!Editable ? AccessibilityStates.ReadOnly : AccessibilityStates.None);
        private readonly UIFontSelection _fontSelection = new UIFontSelection();
        private string _text = string.Empty;
        private int _selectionAnchor = -1;
        private readonly List<string> _undoStack = new List<string>();
        private readonly List<string> _redoStack = new List<string>();
        private bool _restoringHistory;
        private readonly PopupMenu _contextMenu;
        private readonly PopupMenu _directionMenu;
        private readonly PopupMenu _controlCharacterMenu;
        private object[] _structuredTextBidiOverrideOptions = Array.Empty<object>();
        private bool _selectingText;
        private bool _selectingByWord;
        private int _pointerSelectionAnchor;
        private int _textClickCount;
        private TimeSpan _lastTextClickTime = TimeSpan.MinValue;
        private Point _lastTextClickPosition;
        private float _scrollOffset;
        private string _imeComposition = string.Empty;
        private int _imeSelectionStart;
        private int _imeSelectionLength;
        private int _imeReplaceStart;
        private int _imeReplaceLength;
        private VerticalAlignment _textVerticalAlignment = VerticalAlignment.Center;
        private static readonly TimeSpan MultiClickTimeout = TimeSpan.FromMilliseconds(600);
        private const int MultiClickTolerance = 5;
        public LineEdit()
        {
            Cursor = Cursor.IBeam;
            FocusMode = FocusMode.All;
            Padding = new Thickness(6, 4, 6, 4);
            _contextMenu = new PopupMenu { Visible = false };
            _contextMenu.IdPressed += (_, id) => MenuOption((LineEditMenuOption)id);
            _directionMenu = new PopupMenu { Visible = false };
            _directionMenu.IdPressed += (_, id) => MenuOption((LineEditMenuOption)id);
            _controlCharacterMenu = new PopupMenu { Visible = false };
            _controlCharacterMenu.IdPressed += (_, id) => MenuOption((LineEditMenuOption)id);
        }
        public string Text
        {
            get => _text;
            set
            {
                value ??= string.Empty;
                if (_text == value) return;
                if (HasImeComposition) CancelImeComposition();
                if (!_restoringHistory && UndoEnabled)
                {
                    _undoStack.Add(_text);
                    if (_undoStack.Count > UndoStackMaxSize) _undoStack.RemoveAt(0);
                    _redoStack.Clear();
                }
                var isInitialPopulation = MoveCaretToEndOnInitialTextAssignment && _text.Length == 0 && CaretColumn == 0 && _selectionAnchor < 0;
                _text = value;
                CaretColumn = isInitialPopulation ? _text.Length : Math.Min(CaretColumn, _text.Length);
                if (_selectionAnchor >= 0) _selectionAnchor = Math.Min(_selectionAnchor, _text.Length);
                TextChanged?.Invoke(this, _text);
            }
        }
        protected override void OnTemplateApplied()
        {
            if (GetTemplateChild(EditorPresenterPartName) is LineEditPresenter presenter) presenter.Owner = this;
            base.OnTemplateApplied();
        }
        public string PlaceholderText { get; set; } = string.Empty;
        /// <summary>Gets whether the first programmatic text assignment moves the caret to the end.</summary>
        protected virtual bool MoveCaretToEndOnInitialTextAssignment => true;
        public string SecretCharacter { get; set; } = string.Empty;
        public bool Editable { get; set; } = true;
        private int _maxLength;
        public int MaxLength
        {
            get => _maxLength;
            set
            {
                _maxLength = Math.Max(0, value);
                // Godot's set_max_length re-applies set_text(text), re-inserting the current text through
                // the same max_length-truncating insertion pipeline used for ordinary typed/pasted input -
                // so shrinking the limit below the current length clips it immediately, from the tail end.
                if (_maxLength > 0 && Text.Length > _maxLength)
                {
                    var rejected = Text.Substring(_maxLength);
                    Text = Text.Substring(0, _maxLength);
                    TextChangeRejected?.Invoke(this, rejected);
                }
            }
        }
        public bool ClearButtonEnabled { get; set; }
        public bool SubmitOnFocusExit { get; set; }
        public bool DeselectOnFocusLoss { get; set; } = true;
        public bool ContextMenuEnabled { get; set; } = true;
        public bool ShortcutKeysEnabled { get; set; } = true;
        public bool SelectAllOnFocus { get; set; }
        public bool SelectingEnabled
        {
            get => _selectingEnabled;
            set { _selectingEnabled = value; if (!value) Deselect(); }
        }
        private bool _selectingEnabled = true;
        public bool UndoEnabled { get; set; } = true;
        public int UndoStackMaxSize { get; set; } = 50;
        public TextDirection TextDirection { get; private set; } = TextDirection.Auto;
        public bool DrawControlCharacters { get; private set; }
        public StructuredTextParser StructuredTextBidiOverride { get; private set; } = StructuredTextParser.Default;
        /// <summary>Zero-based caret index in the underlying string.</summary>
        public int CaretColumn { get; protected set; }
        public bool HasImeComposition => _imeComposition.Length > 0;
        internal bool NativeImeActive { get; set; }
        internal event Action ImeCompositionCancelled;
        internal virtual bool SupportsNativeTextComposition => true;
        public string ImeCompositionText => _imeComposition;
        public Point ImeCompositionSelection => new Point(_imeSelectionStart, _imeSelectionLength);
        public float GetScrollOffset()
        {
            if (EffectiveUIFont != null) EnsureCaretVisible(GetEditingLayout(GetComposedDisplayText()), GetDisplayCaretColumn());
            return _scrollOffset;
        }
        public bool HasSelection => _selectionAnchor >= 0 && _selectionAnchor != CaretColumn;
        public int SelectionFrom => HasSelection ? Math.Min(_selectionAnchor, CaretColumn) : CaretColumn;
        public int SelectionTo => HasSelection ? Math.Max(_selectionAnchor, CaretColumn) : CaretColumn;
        public string SelectedText => HasSelection ? Text.Substring(SelectionFrom, SelectionTo - SelectionFrom) : string.Empty;
        public bool HasUndo => _undoStack.Count > 0;
        public bool HasRedo => _redoStack.Count > 0;
        public SpriteFont Font { get => _fontSelection.SpriteFont; set { _fontSelection.SetSpriteFont(value); QueueLayout(); } }
        public UIFont UIFont { get => _fontSelection.UIFont; set { _fontSelection.SetUIFont(value); QueueLayout(); } }
        internal UIFont EffectiveUIFont => ResolveFont(_fontSelection);
        public Thickness Padding { get; set; }
        public VerticalAlignment TextVerticalAlignment
        {
            get => _textVerticalAlignment;
            set
            {
                if (!Enum.IsDefined(typeof(VerticalAlignment), value)) throw new ArgumentOutOfRangeException(nameof(value));
                if (_textVerticalAlignment == value) return;
                _textVerticalAlignment = value;
                QueueLayout();
            }
        }
        /// <summary>Optional per-control override used by <see cref="Paste()"/> before <see cref="UIContext.Clipboard"/>.</summary>
        public Func<LineEdit, string> ClipboardTextProvider { get; set; }
        public event Action<LineEdit, string> TextChanged;
        public event Action<LineEdit, string> TextChangeRejected;
        public event EventHandler TextSubmitted;
        /// <summary>Raised after Copy or Cut submits text to <see cref="UIContext.Clipboard"/>.</summary>
        public event Action<LineEdit, string> CopyRequested;
        /// <summary>Reports unavailable/rejected clipboard access without exposing clipboard contents.</summary>
        public event Action<LineEdit, ClipboardOperation> ClipboardOperationFailed;
        /// <summary>Returns the retained context popup, equivalent to Godot's <c>get_menu()</c>.</summary>
        public virtual PopupMenu GetMenu() => _contextMenu;
        public PopupMenu GetTextDirectionMenu() => _directionMenu;
        public PopupMenu GetControlCharacterMenu() => _controlCharacterMenu;
        public virtual bool IsMenuVisible() => _contextMenu.Visible;
        public void SetContextMenuEnabled(bool enabled) => ContextMenuEnabled = enabled;
        public bool IsContextMenuEnabled() => ContextMenuEnabled;
        public void SetShortcutKeysEnabled(bool enabled) => ShortcutKeysEnabled = enabled;
        public bool IsShortcutKeysEnabled() => ShortcutKeysEnabled;
        public void SetSelectAllOnFocus(bool enabled) => SelectAllOnFocus = enabled;
        public bool IsSelectAllOnFocus() => SelectAllOnFocus;
        public void SetSelectingEnabled(bool enabled) => SelectingEnabled = enabled;
        public bool IsSelectingEnabled() => SelectingEnabled;
        public void SetTextDirection(TextDirection direction)
        {
            if (!Enum.IsDefined(typeof(TextDirection), direction)) throw new ArgumentOutOfRangeException(nameof(direction));
            if (TextDirection == direction) return;
            TextDirection = direction;
            QueueLayout();
        }
        public TextDirection GetTextDirection() => TextDirection;
        public void SetLanguage(string language)
        {
            language ??= string.Empty;
            if (Language == language) return;
            Language = language;
            QueueLayout();
        }
        public string GetLanguage() => Language;
        public void SetDrawControlChars(bool enabled) => DrawControlCharacters = enabled;
        public bool GetDrawControlChars() => DrawControlCharacters;
        public void SetStructuredTextBidiOverride(StructuredTextParser parser)
        {
            if (!Enum.IsDefined(typeof(StructuredTextParser), parser)) throw new ArgumentOutOfRangeException(nameof(parser));
            StructuredTextBidiOverride = parser;
        }
        public StructuredTextParser GetStructuredTextBidiOverride() => StructuredTextBidiOverride;
        public void SetStructuredTextBidiOverrideOptions(IEnumerable<object> options) => _structuredTextBidiOverrideOptions = options == null ? Array.Empty<object>() : new List<object>(options).ToArray();
        public IReadOnlyList<object> GetStructuredTextBidiOverrideOptions() => _structuredTextBidiOverrideOptions;
        /// <summary>Local clear-button hit region, or an empty rectangle when the affordance is unavailable.</summary>
        public Rectangle GetClearButtonRectangle()
        {
            if (!ClearButtonEnabled || !Editable || string.IsNullOrEmpty(Text)) return Rectangle.Empty;
            var side = Math.Max(12, Math.Min(16, (int)MathF.Round(Size.Y - Padding.Vertical)));
            return new Rectangle(Math.Max(0, (int)MathF.Round(Size.X - Padding.Right - side)), Math.Max(0, (int)MathF.Round((Size.Y - side) / 2)), side, side);
        }
        public virtual void InsertText(string text)
        {
            if (!Editable || string.IsNullOrEmpty(text)) return;
            if (HasSelection) DeleteSelection();
            if (MaxLength > 0)
            {
                var available = Math.Max(0, MaxLength - Text.Length);
                if (text.Length > available)
                {
                    var rejected = text.Substring(available);
                    text = text.Substring(0, available);
                    if (!string.IsNullOrEmpty(rejected)) TextChangeRejected?.Invoke(this, rejected);
                }
            }
            if (string.IsNullOrEmpty(text)) return;
            var insertionOffset = CaretColumn;
            Text = Text.Insert(insertionOffset, text);
            CaretColumn = Math.Min(insertionOffset + text.Length, Text.Length);
            Deselect();
        }
        public void SetImeComposition(string text, int selectionStart = 0, int selectionLength = 0)
        {
            if (!Editable) return;
            text ??= string.Empty;
            if (!HasImeComposition)
            {
                _imeReplaceStart = SelectionFrom;
                _imeReplaceLength = SelectionTo - SelectionFrom;
            }
            _imeComposition = text;
            _imeSelectionStart = MathHelper.Clamp(selectionStart, 0, text.Length);
            _imeSelectionLength = MathHelper.Clamp(selectionLength, 0, text.Length - _imeSelectionStart);
            if (text.Length == 0) CancelImeComposition();
            QueueLayout();
        }
        public void CommitImeComposition(string text = null)
        {
            if (!HasImeComposition && string.IsNullOrEmpty(text)) return;
            var committed = text ?? _imeComposition;
            var start = HasImeComposition ? _imeReplaceStart : SelectionFrom;
            var length = HasImeComposition ? _imeReplaceLength : SelectionTo - SelectionFrom;
            ClearImeComposition();
            if (!Editable || string.IsNullOrEmpty(committed)) return;
            if (MaxLength > 0)
            {
                var available = Math.Max(0, MaxLength - (Text.Length - length));
                if (committed.Length > available)
                {
                    var accepted = 0;
                    foreach (var boundary in StringInfo.ParseCombiningCharacters(committed))
                        if (boundary <= available) accepted = boundary;
                    var rejected = committed.Substring(accepted);
                    committed = committed.Substring(0, accepted);
                    TextChangeRejected?.Invoke(this, rejected);
                }
            }
            if (committed.Length == 0) return;
            Text = Text.Remove(start, length).Insert(start, committed);
            CaretColumn = Math.Min(start + committed.Length, Text.Length);
            Deselect();
        }
        public void CancelImeComposition()
        {
            var hadComposition = HasImeComposition;
            ClearImeComposition();
            if (hadComposition) ImeCompositionCancelled?.Invoke();
        }
        private void ClearImeComposition()
        {
            _imeComposition = string.Empty;
            _imeSelectionStart = 0;
            _imeSelectionLength = 0;
            _imeReplaceStart = CaretColumn;
            _imeReplaceLength = 0;
            QueueLayout();
        }
        public void Select(int from, int to)
        {
            if (HasImeComposition) CancelImeComposition();
            _selectionAnchor = MathHelper.Clamp(from, 0, Text.Length);
            CaretColumn = MathHelper.Clamp(to, 0, Text.Length);
        }
        public void SelectAll() => Select(0, Text.Length);
        public void Deselect()
        {
            if (HasImeComposition) CancelImeComposition();
            _selectionAnchor = -1;
        }
        public void DeleteSelection()
        {
            if (!HasSelection) return;
            var start = SelectionFrom;
            Text = Text.Remove(start, SelectionTo - start);
            CaretColumn = start;
            Deselect();
        }
        public void DeleteText(int fromColumn, int toColumn)
        {
            var start = Math.Max(0, Math.Min(fromColumn, toColumn)); var end = Math.Min(Text.Length, Math.Max(fromColumn, toColumn));
            if (end <= start) return;
            Text = Text.Remove(start, end - start); CaretColumn = Math.Min(start, Text.Length); Deselect();
        }
        public void Clear()
        {
            if (!Editable) return;
            Text = string.Empty;
            CaretColumn = 0;
            Deselect();
        }
        public void ClearUndoHistory()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }
        public void Undo()
        {
            if (!Editable || !UndoEnabled || _undoStack.Count == 0) return;
            var previous = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            _redoStack.Add(Text);
            RestoreHistoryText(previous);
        }
        public void Redo()
        {
            if (!Editable || !UndoEnabled || _redoStack.Count == 0) return;
            var next = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            _undoStack.Add(Text);
            RestoreHistoryText(next);
        }
        public void Copy()
        {
            if (!HasSelection || !string.IsNullOrEmpty(SecretCharacter)) return;
            var copied = SelectedText;
            if (!string.IsNullOrEmpty(copied)) WriteClipboard(copied);
        }
        public void Cut()
        {
            // Godot's ui_cut handler falls back to a plain copy when the field isn't editable, rather
            // than no-oping entirely: a read-only field with a selection still puts it on the clipboard.
            if (!Editable) { Copy(); return; }
            if (!HasSelection || !string.IsNullOrEmpty(SecretCharacter)) return;
            var copied = SelectedText;
            if (string.IsNullOrEmpty(copied)) return;
            if (WriteClipboard(copied, ClipboardOperation.Cut)) DeleteSelection();
        }
        public void Paste()
        {
            if (Editable) Paste(ClipboardTextProvider?.Invoke(this) ?? ReadClipboardText());
        }
        public void Paste(string clipboard)
        {
            if (!Editable || string.IsNullOrEmpty(clipboard)) return;
            var stripped = new System.Text.StringBuilder(clipboard.Length);
            foreach (var character in clipboard)
                if (!char.IsControl(character)) stripped.Append(character);
            InsertText(stripped.ToString());
        }
        public void MenuOption(LineEditMenuOption option)
        {
            switch (option)
            {
                case LineEditMenuOption.Cut: Cut(); break;
                case LineEditMenuOption.Copy: Copy(); break;
                case LineEditMenuOption.Paste: Paste(); break;
                case LineEditMenuOption.Clear: Clear(); break;
                case LineEditMenuOption.SelectAll: SelectAll(); break;
                case LineEditMenuOption.Undo: Undo(); break;
                case LineEditMenuOption.Redo: Redo(); break;
                case LineEditMenuOption.DirectionInherited: SetTextDirection(TextDirection.Inherited); break;
                case LineEditMenuOption.DirectionAuto: SetTextDirection(TextDirection.Auto); break;
                case LineEditMenuOption.DirectionLeftToRight: SetTextDirection(TextDirection.LeftToRight); break;
                case LineEditMenuOption.DirectionRightToLeft: SetTextDirection(TextDirection.RightToLeft); break;
                case LineEditMenuOption.DisplayControlCharacters: SetDrawControlChars(!GetDrawControlChars()); break;
                case LineEditMenuOption.InsertLeftToRightMark: InsertControlCharacter('\u200E'); break;
                case LineEditMenuOption.InsertRightToLeftMark: InsertControlCharacter('\u200F'); break;
                case LineEditMenuOption.InsertLeftToRightEmbedding: InsertControlCharacter('\u202A'); break;
                case LineEditMenuOption.InsertRightToLeftEmbedding: InsertControlCharacter('\u202B'); break;
                case LineEditMenuOption.InsertLeftToRightOverride: InsertControlCharacter('\u202D'); break;
                case LineEditMenuOption.InsertRightToLeftOverride: InsertControlCharacter('\u202E'); break;
                case LineEditMenuOption.InsertPopDirectionFormatting: InsertControlCharacter('\u202C'); break;
                case LineEditMenuOption.InsertArabicLetterMark: InsertControlCharacter('\u061C'); break;
                case LineEditMenuOption.InsertLeftToRightIsolate: InsertControlCharacter('\u2066'); break;
                case LineEditMenuOption.InsertRightToLeftIsolate: InsertControlCharacter('\u2067'); break;
                case LineEditMenuOption.InsertFirstStrongIsolate: InsertControlCharacter('\u2068'); break;
                case LineEditMenuOption.InsertPopDirectionIsolate: InsertControlCharacter('\u2069'); break;
                case LineEditMenuOption.InsertZeroWidthJoiner: InsertControlCharacter('\u200D'); break;
                case LineEditMenuOption.InsertZeroWidthNonJoiner: InsertControlCharacter('\u200C'); break;
                case LineEditMenuOption.InsertWordJoiner: InsertControlCharacter('\u2060'); break;
                case LineEditMenuOption.InsertSoftHyphen: InsertControlCharacter('\u00AD'); break;
            }
        }
        private bool WriteClipboard(string text, ClipboardOperation operation = ClipboardOperation.Copy)
        {
            var accepted = TryWriteClipboardText(text, operation);
            CopyRequested?.Invoke(this, text);
            return accepted;
        }
        protected bool TryWriteClipboardText(string text, ClipboardOperation operation)
        {
            if (Context?.Clipboard?.SetText(text) == true) return true;
            ReportClipboardFailure(operation);
            return false;
        }
        protected string ReadClipboardText()
        {
            var text = Context?.Clipboard?.GetText();
            if (text == null) ReportClipboardFailure(ClipboardOperation.Paste);
            return text;
        }
        private void ReportClipboardFailure(ClipboardOperation operation)
        {
            System.Diagnostics.Trace.TraceWarning($"Clipboard {operation} was unavailable or rejected; editor text was preserved.");
            ClipboardOperationFailed?.Invoke(this, operation);
        }
        public override Vector2 GetMinimumSize() => Vector2.Max(CustomMinimumSize, new Vector2(80, EffectiveUIFont == null ? 24 : TextMetrics.LineHeight(EffectiveUIFont) + Padding.Vertical));
        internal override void PointerPressed(Point position)
        {
            CancelImeComposition();
            var hadSelectionBeforeFocus = HasSelection;
            base.PointerPressed(position);
            var clear = GetClearButtonRectangle();
            if (!clear.IsEmpty && clear.Contains((int)(position.X - GlobalPosition.X), (int)(position.Y - GlobalPosition.Y)))
            {
                Text = string.Empty; CaretColumn = 0; Deselect(); return;
            }
            // Godot's SelectAllOnFocus defers select_all() to mouse release when focus was gained by a
            // mouse press, so the click's caret placement never overwrites the just-created selection;
            // this port has no PointerReleased hook for LineEdit, so the equivalent is simply: if this
            // exact press just caused a fresh focus-driven SelectAll (no selection before, one now),
            // don't let the click below immediately collapse it back down to a caret position.
            if (SelectAllOnFocus && !hadSelectionBeforeFocus && HasSelection) return;
            var clickedColumn = GetCaretColumnAtPosition(position);
            if (HasShiftModifier() && SelectingEnabled)
            {
                if (!HasSelection) _selectionAnchor = CaretColumn;
                CaretColumn = clickedColumn;
                _pointerSelectionAnchor = _selectionAnchor;
                _selectingText = true;
                _selectingByWord = false;
                return;
            }
            CaretColumn = clickedColumn;
            if (!SelectingEnabled || string.IsNullOrEmpty(Text)) { Deselect(); return; }
            var clickTime = Context?.CurrentTime ?? TimeSpan.Zero;
            var withinTimeout = _lastTextClickTime != TimeSpan.MinValue && clickTime - _lastTextClickTime <= MultiClickTimeout;
            var withinTolerance = Vector2.DistanceSquared(position.ToVector2(), _lastTextClickPosition.ToVector2()) <= MultiClickTolerance * MultiClickTolerance;
            _textClickCount = withinTimeout && withinTolerance ? Math.Min(3, _textClickCount + 1) : 1;
            _lastTextClickTime = clickTime;
            _lastTextClickPosition = position;
            _pointerSelectionAnchor = clickedColumn;
            _selectingText = true;
            _selectingByWord = _textClickCount == 2;
            if (_textClickCount == 3)
            {
                SelectAll();
                _selectingText = false;
                _selectingByWord = false;
                _textClickCount = 0;
                _lastTextClickTime = TimeSpan.MinValue;
            }
            else if (_selectingByWord) SelectPointerRange(clickedColumn);
            else Deselect();
        }
        internal override void PointerMoved(Point position)
        {
            if (_selectingText) SelectPointerRange(GetCaretColumnAtPosition(position));
        }
        internal override void PointerReleased(Point position, bool isInside)
        {
            if (_selectingText) SelectPointerRange(GetCaretColumnAtPosition(position));
            _selectingText = false;
            _selectingByWord = false;
            base.PointerReleased(position, isInside);
        }
        internal override void CancelInput()
        {
            _selectingText = false;
            _selectingByWord = false;
            _textClickCount = 0;
            _lastTextClickTime = TimeSpan.MinValue;
            base.CancelInput();
        }
        internal override void PointerRightPressed(Point position) => OpenContextMenu(position);
        internal virtual bool UsesMacTextNavigation => OperatingSystem.IsMacOS();
        internal override void KeyPressed(Keys key)
        {
            if (HasImeComposition)
            {
                if (NativeImeActive) return;
                if (key == Keys.Escape) { CancelImeComposition(); return; }
                if (key == Keys.Enter) { CommitImeComposition(); return; }
                CancelImeComposition();
            }
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
            if (!Editable) return;
            var shift = HasShiftModifier();
            var keyboard = Context?.CurrentKeyboardState ?? default;
            var movement = TextNavigation.Resolve(key, keyboard, UsesMacTextNavigation, false);
            if (movement != TextNavigationAction.None)
            {
                if (HasSelection && !shift && movement is TextNavigationAction.PreviousCharacter or TextNavigationAction.NextCharacter)
                {
                    CaretColumn = movement == TextNavigationAction.PreviousCharacter ? SelectionFrom : SelectionTo;
                    Deselect();
                }
                else
                {
                    ShiftSelectionCheckPre(shift);
                    CaretColumn = GetNavigationTarget(movement, CaretColumn);
                }
            }
            else if (key == Keys.Back)
            {
                if (HasSelection) DeleteSelection();
                else
                {
                    var action = TextNavigation.Resolve(Keys.Left, keyboard, UsesMacTextNavigation, false);
                    var start = GetNavigationTarget(action, CaretColumn);
                    if (start < CaretColumn) DeleteText(start, CaretColumn);
                }
            }
            else if (key == Keys.Delete)
            {
                if (HasSelection) DeleteSelection();
                else
                {
                    var action = TextNavigation.Resolve(Keys.Right, keyboard, UsesMacTextNavigation, false);
                    var end = GetNavigationTarget(action, CaretColumn);
                    if (end > CaretColumn) DeleteText(CaretColumn, end);
                }
            }
            else if (key == Keys.Enter) TextSubmitted?.Invoke(this, EventArgs.Empty);
        }
        internal virtual int GetNavigationTarget(TextNavigationAction action, int from) => action switch
        {
            TextNavigationAction.PreviousCharacter => FindGraphemeBoundaryLeft(from),
            TextNavigationAction.NextCharacter => FindGraphemeBoundaryRight(from),
            TextNavigationAction.PreviousWord => FindWordBoundaryLeft(from),
            TextNavigationAction.NextWord => FindWordBoundaryRight(from),
            TextNavigationAction.LineStart or TextNavigationAction.DocumentStart or TextNavigationAction.PreviousParagraph => 0,
            TextNavigationAction.LineEnd or TextNavigationAction.DocumentEnd or TextNavigationAction.NextParagraph => Text.Length,
            _ => from
        };
        private int FindGraphemeBoundaryLeft(int from) => EffectiveUIFont == null ? TextNavigation.GraphemeBoundary(Text, from, -1) : GetEditingLayout().GetPreviousGraphemeBoundary(from);
        private int FindGraphemeBoundaryRight(int from) => EffectiveUIFont == null ? TextNavigation.GraphemeBoundary(Text, from, 1) : GetEditingLayout().GetNextGraphemeBoundary(from);
        private int FindWordBoundaryLeft(int from)
        {
            if (EffectiveUIFont != null) return GetEditingLayout().GetPreviousWordBoundary(from);
            if (from > 0) from--;
            while (from > 0 && !IsWordChar(Text[from])) from--;
            while (from > 0 && IsWordChar(Text[from - 1])) from--;
            return from;
        }
        private int FindWordBoundaryRight(int from)
        {
            if (EffectiveUIFont != null) return GetEditingLayout().GetNextWordBoundary(from);
            while (from < Text.Length && !IsWordChar(Text[from])) from++;
            while (from < Text.Length && IsWordChar(Text[from])) from++;
            return from;
        }
        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' ||
            char.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
        protected virtual int GetCaretColumnAtPosition(Point position)
        {
            if (EffectiveUIFont == null) return Text.Length;
            var layout = GetEditingLayout();
            EnsureCaretVisible(layout);
            var localX = position.X - GlobalPosition.X - Padding.Left + _scrollOffset;
            return layout.HitTest(new Vector2(localX, 0));
        }
        private void SelectPointerRange(int column)
        {
            if (!_selectingByWord)
            {
                Select(_pointerSelectionAnchor, column);
                return;
            }
            var anchorRange = GetWordRange(_pointerSelectionAnchor);
            var currentRange = GetWordRange(column);
            if (anchorRange.X == anchorRange.Y || currentRange.X == currentRange.Y) return;
            if (column < _pointerSelectionAnchor) Select(anchorRange.Y, currentRange.X);
            else Select(anchorRange.X, currentRange.Y);
        }
        private Point GetWordRange(int column)
        {
            if (string.IsNullOrEmpty(Text)) return Point.Zero;
            var range = GetEditingLayout().GetWordBoundary(Math.Min(Math.Max(0, column), Text.Length));
            return new Point(range.Start, range.End);
        }
        /// <summary>Marks the selection anchor at the current caret before a caret-movement key, matching
        /// Godot's LineEdit::shift_selection_check_pre - the resulting selection range is then implicit
        /// in SelectionFrom/SelectionTo once the caret actually moves, matching shift_selection_check_post.</summary>
        private void ShiftSelectionCheckPre(bool shiftHeld)
        {
            if (!HasSelection && shiftHeld) _selectionAnchor = CaretColumn;
            if (!shiftHeld) Deselect();
        }
        internal override void TextInput(char character)
        {
            if (HasImeComposition) CommitImeComposition(character.ToString());
            else InsertText(character.ToString());
        }
        internal override void TextInput(string text) => CommitImeComposition(text);
        internal override void TextComposition(string text, int selectionStart, int selectionLength) => SetImeComposition(text, selectionStart, selectionLength);
        internal override void FocusGained()
        {
            base.FocusGained();
            if (SelectAllOnFocus) SelectAll();
        }
        internal override void FocusLost()
        {
            CancelImeComposition();
            if (DeselectOnFocusLoss && !IsMenuVisible()) Deselect();
            if (SubmitOnFocusExit) TextSubmitted?.Invoke(this, EventArgs.Empty);
            base.FocusLost();
        }
        private bool HasCommandModifier()
        {
            return TextInputShortcuts.HasCommandModifier(Context?.CurrentKeyboardState ?? default);
        }
        private bool HasShiftModifier()
        {
            var keyboard = Context?.CurrentKeyboardState ?? default;
            return keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        }
        private void RestoreHistoryText(string text)
        {
            _restoringHistory = true;
            try { Text = text; }
            finally { _restoringHistory = false; }
            CaretColumn = Text.Length;
            Deselect();
        }
        private void InsertControlCharacter(char character)
        {
            if (!Editable) return;
            InsertText(character.ToString());
        }
        private void OpenContextMenu(Point position)
        {
            if (!ContextMenuEnabled || Context == null) return;
            _contextMenu.Clear(); _contextMenu.Font = Font; _contextMenu.UIFont = UIFont;
            _directionMenu.Clear(); _directionMenu.Font = Font; _directionMenu.UIFont = UIFont;
            _directionMenu.AddRadioCheckItem("Same as Layout Direction", (int)LineEditMenuOption.DirectionInherited).Checked = TextDirection == TextDirection.Inherited;
            _directionMenu.AddRadioCheckItem("Auto-Detect Direction", (int)LineEditMenuOption.DirectionAuto).Checked = TextDirection == TextDirection.Auto;
            _directionMenu.AddRadioCheckItem("Left-to-Right", (int)LineEditMenuOption.DirectionLeftToRight).Checked = TextDirection == TextDirection.LeftToRight;
            _directionMenu.AddRadioCheckItem("Right-to-Left", (int)LineEditMenuOption.DirectionRightToLeft).Checked = TextDirection == TextDirection.RightToLeft;
            _controlCharacterMenu.Clear(); _controlCharacterMenu.Font = Font; _controlCharacterMenu.UIFont = UIFont;
            _controlCharacterMenu.AddItem("Left-to-Right Mark (LRM)", (int)LineEditMenuOption.InsertLeftToRightMark);
            _controlCharacterMenu.AddItem("Right-to-Left Mark (RLM)", (int)LineEditMenuOption.InsertRightToLeftMark);
            _controlCharacterMenu.AddItem("Start of Left-to-Right Embedding (LRE)", (int)LineEditMenuOption.InsertLeftToRightEmbedding);
            _controlCharacterMenu.AddItem("Start of Right-to-Left Embedding (RLE)", (int)LineEditMenuOption.InsertRightToLeftEmbedding);
            _controlCharacterMenu.AddItem("Start of Left-to-Right Override (LRO)", (int)LineEditMenuOption.InsertLeftToRightOverride);
            _controlCharacterMenu.AddItem("Start of Right-to-Left Override (RLO)", (int)LineEditMenuOption.InsertRightToLeftOverride);
            _controlCharacterMenu.AddItem("Pop Direction Formatting (PDF)", (int)LineEditMenuOption.InsertPopDirectionFormatting);
            _controlCharacterMenu.AddSeparator();
            _controlCharacterMenu.AddItem("Arabic Letter Mark (ALM)", (int)LineEditMenuOption.InsertArabicLetterMark);
            _controlCharacterMenu.AddItem("Left-to-Right Isolate (LRI)", (int)LineEditMenuOption.InsertLeftToRightIsolate);
            _controlCharacterMenu.AddItem("Right-to-Left Isolate (RLI)", (int)LineEditMenuOption.InsertRightToLeftIsolate);
            _controlCharacterMenu.AddItem("First Strong Isolate (FSI)", (int)LineEditMenuOption.InsertFirstStrongIsolate);
            _controlCharacterMenu.AddItem("Pop Direction Isolate (PDI)", (int)LineEditMenuOption.InsertPopDirectionIsolate);
            _controlCharacterMenu.AddSeparator();
            _controlCharacterMenu.AddItem("Zero-Width Joiner (ZWJ)", (int)LineEditMenuOption.InsertZeroWidthJoiner);
            _controlCharacterMenu.AddItem("Zero-Width Non-Joiner (ZWNJ)", (int)LineEditMenuOption.InsertZeroWidthNonJoiner);
            _controlCharacterMenu.AddItem("Word Joiner (WJ)", (int)LineEditMenuOption.InsertWordJoiner);
            _controlCharacterMenu.AddItem("Soft Hyphen (SHY)", (int)LineEditMenuOption.InsertSoftHyphen);
            _contextMenu.AddItem("Cut", (int)LineEditMenuOption.Cut).Disabled = !Editable;
            _contextMenu.AddItem("Copy", (int)LineEditMenuOption.Copy);
            _contextMenu.AddItem("Paste", (int)LineEditMenuOption.Paste).Disabled = !Editable;
            _contextMenu.AddSeparator();
            _contextMenu.AddItem("Select All", (int)LineEditMenuOption.SelectAll);
            _contextMenu.AddItem("Clear", (int)LineEditMenuOption.Clear).Disabled = !Editable;
            _contextMenu.AddSeparator();
            _contextMenu.AddItem("Undo", (int)LineEditMenuOption.Undo).Disabled = !Editable || !UndoEnabled || !HasUndo;
            _contextMenu.AddItem("Redo", (int)LineEditMenuOption.Redo).Disabled = !Editable || !UndoEnabled || !HasRedo;
            _contextMenu.AddSeparator();
            _contextMenu.AddSubmenuNodeItem("Text Writing Direction", _directionMenu, (int)LineEditMenuOption.SubmenuTextDirection);
            _contextMenu.AddSeparator();
            _contextMenu.AddCheckItem("Display Control Characters", (int)LineEditMenuOption.DisplayControlCharacters).Checked = DrawControlCharacters;
            _contextMenu.AddSubmenuNodeItem("Insert Control Character", _controlCharacterMenu, (int)LineEditMenuOption.SubmenuInsertControlCharacter).Disabled = !Editable;
            if (_contextMenu.Context != Context) Context.Add(_contextMenu);
            _contextMenu.PopupAt(new Vector2(position.X, position.Y), null);
        }
        internal virtual void DrawEditor(UIRenderContext context)
        {
            context.Fill(Bounds, context.Theme.BackgroundColor); context.Border(Bounds, Context?.FocusedControl == this ? context.Theme.FocusColor : context.Theme.PanelBorderColor);
            if (EffectiveUIFont != null)
            {
                var shown = string.IsNullOrEmpty(Text) && !HasImeComposition ? PlaceholderText : string.IsNullOrEmpty(SecretCharacter) ? GetComposedDisplayText() : new string(SecretCharacter[0], Text.Length);
                var layout = GetEditingLayout(shown);
                if (string.IsNullOrEmpty(Text) && !HasImeComposition) _scrollOffset = 0;
                else EnsureCaretVisible(layout, GetDisplayCaretColumn());
                var viewport = GetTextViewport();
                var origin = GlobalPosition + new Vector2(Padding.Left - _scrollOffset, Padding.Top + GetTextVerticalOffset(layout));
                context.PushClip(viewport);
                try
                {
                    if (HasSelection && !HasImeComposition && string.IsNullOrEmpty(SecretCharacter) && !string.IsNullOrEmpty(Text))
                        foreach (var rectangle in layout.GetSelectionRectangles(SelectionFrom, SelectionTo - SelectionFrom))
                            context.Fill(new Rectangle(
                                (int)MathF.Floor(origin.X + rectangle.X),
                                (int)MathF.Floor(origin.Y + rectangle.Y),
                                Math.Max(1, (int)MathF.Ceiling(rectangle.Width)),
                                Math.Max(1, (int)MathF.Ceiling(rectangle.Height))), context.Theme.AccentColor.WithAlpha(96));
                    if (HasImeComposition && string.IsNullOrEmpty(SecretCharacter))
                        foreach (var rectangle in layout.GetSelectionRectangles(_imeReplaceStart, _imeComposition.Length))
                            context.Fill(new Rectangle(
                                (int)MathF.Floor(origin.X + rectangle.X),
                                (int)MathF.Floor(origin.Y + rectangle.Bottom - 1),
                                Math.Max(1, (int)MathF.Ceiling(rectangle.Width)),
                                1), context.Theme.FocusColor);
                    context.Text(layout, origin, string.IsNullOrEmpty(Text) ? context.Theme.DisabledTextColor : context.Theme.TextColor);
                    if (Context?.FocusedControl == this)
                    {
                        var caret = layout.GetCaretPosition(Math.Min(GetDisplayCaretColumn(), layout.Text.Length));
                        context.Fill(new Rectangle((int)MathF.Round(origin.X + caret.X), (int)MathF.Round(origin.Y + caret.Y), 1, Math.Max(1, TextMetrics.LineHeight(EffectiveUIFont))), context.Theme.FocusColor);
                    }
                }
                finally { context.PopClip(); }
            }
            var clear = GetClearButtonRectangle();
            var clearIcon = GetThemeIcon("clear");
            if (!clear.IsEmpty && clearIcon.HasValue)
            {
                var x = Bounds.X + clear.Center.X - clearIcon.Value.LogicalSize.X / 2;
                var y = Bounds.Y + clear.Center.Y - clearIcon.Value.LogicalSize.Y / 2;
                context.Icon(clearIcon.Value, new Vector2(x, y), Enabled ? Color.White : context.Theme.DisabledTextColor);
            }
        }
        internal Vector2 GetEditorMinimumSize()
        {
            var lineHeight = EffectiveUIFont == null ? 16 : TextMetrics.LineHeight(EffectiveUIFont);
            return Vector2.Max(CustomMinimumSize, new Vector2(64, Math.Max(24, lineHeight + 8)));
        }
        internal TextLayout GetEditingLayout(string text = null)
        {
            text ??= Text;
            if (EffectiveUIFont == null) throw new InvalidOperationException("A font is required for text layout.");
            var direction = TextDirection == TextDirection.Inherited ? TextDirection.Auto : TextDirection;
            return TextMetrics.Layout(EffectiveUIFont, text, new TextLayoutOptions(direction: direction, locale: Language));
        }
        internal float GetTextVerticalOffset(TextLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            var spareHeight = MathF.Max(0, Size.Y - Padding.Vertical - layout.Size.Y);
            return TextVerticalAlignment == VerticalAlignment.Center ? spareHeight * .5f
                : TextVerticalAlignment == VerticalAlignment.Bottom ? spareHeight : 0;
        }
        private Rectangle GetTextViewport()
        {
            var clear = GetClearButtonRectangle();
            var right = clear.IsEmpty ? Size.X - Padding.Right : clear.X - 2;
            return new Rectangle((int)MathF.Round(GlobalPosition.X + Padding.Left), (int)MathF.Round(GlobalPosition.Y + Padding.Top), Math.Max(0, (int)MathF.Floor(right - Padding.Left)), Math.Max(0, (int)MathF.Floor(Size.Y - Padding.Vertical)));
        }
        internal Rectangle GetTextInputRectangle()
        {
            var origin = GlobalPosition + new Vector2(Padding.Left, Padding.Top);
            var height = Math.Max(1, (int)(Size.Y - Padding.Vertical));
            if (EffectiveUIFont != null)
            {
                var shown = string.IsNullOrEmpty(SecretCharacter) ? GetComposedDisplayText() : new string(SecretCharacter[0], Text.Length);
                var layout = GetEditingLayout(shown);
                var column = Math.Min(GetDisplayCaretColumn(), layout.Text.Length);
                EnsureCaretVisible(layout, column);
                origin += layout.GetCaretPosition(column) + new Vector2(-_scrollOffset, GetTextVerticalOffset(layout));
                height = Math.Max(1, TextMetrics.LineHeight(EffectiveUIFont));
            }
            var caret = new Rectangle((int)MathF.Floor(origin.X), (int)MathF.Floor(origin.Y), 1, height);
            var scale = Context?.DisplayScale ?? 1f;
            return TransformBounds(caret, GetWorldRenderTransformMatrix() * Matrix.CreateScale(scale, scale, 1));
        }
        private string GetComposedDisplayText() => HasImeComposition
            ? Text.Remove(_imeReplaceStart, _imeReplaceLength).Insert(_imeReplaceStart, _imeComposition)
            : Text;
        private int GetDisplayCaretColumn() => HasImeComposition
            ? _imeReplaceStart + _imeSelectionStart + _imeSelectionLength
            : CaretColumn;
        private void EnsureCaretVisible(TextLayout layout, int? displayCaretColumn = null)
        {
            if (string.IsNullOrEmpty(Text) && !HasImeComposition) { _scrollOffset = 0; return; }
            var viewportWidth = GetTextViewport().Width;
            if (viewportWidth <= 0) { _scrollOffset = 0; return; }
            var caretX = layout.GetCaretPosition(Math.Min(displayCaretColumn ?? CaretColumn, layout.Text.Length)).X;
            if (caretX < _scrollOffset) _scrollOffset = caretX;
            else if (caretX > _scrollOffset + viewportWidth - 1) _scrollOffset = caretX - viewportWidth + 1;
            _scrollOffset = MathHelper.Clamp(_scrollOffset, 0, Math.Max(0, layout.Size.X - viewportWidth + 1));
        }
    }

}
