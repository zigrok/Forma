// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// Tree and TreeItem APIs and behavior are adapted from Godot Engine's
// scene/gui/tree.cpp and scene/gui/tree.h; see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    public enum TreeCellMode { String, Check, Range, Icon, Custom }
    /// <summary>Selection behavior for a <see cref="Tree"/>, corresponding to Godot's <c>Tree.SelectMode</c>.</summary>
    public enum TreeSelectMode { Single, Row, Multi }
    /// <summary>Visual overflow hint placement, corresponding to Godot's <c>Tree.ScrollHintMode</c>.</summary>
    public enum TreeScrollHintMode { Disabled, Both, Top, Bottom }
    /// <summary>Allowed Tree drop target regions, corresponding to Godot's <c>Tree.DropModeFlags</c>.</summary>
    [Flags] public enum TreeDropModeFlags { Disabled = 0, OnItem = 1, InBetween = 2 }

    /// <summary>Draws the content of a <see cref="TreeCellMode.Custom"/> tree cell.</summary>
    public delegate void TreeItemCustomDrawCallback(UIRenderContext context, TreeItem item, Rectangle cell);

    /// <summary>One action button displayed at the trailing edge of a <see cref="TreeItem"/> cell.</summary>
    public sealed class TreeItemButton
    {
        internal TreeItemButton(Texture2D texture, int id, bool disabled, string tooltip, string description)
        {
            Texture = texture; Id = id; Disabled = disabled; Tooltip = tooltip ?? string.Empty; Description = description ?? string.Empty;
        }
        public Texture2D Texture { get; internal set; }
        public int Id { get; }
        public bool Disabled { get; internal set; }
        public string Tooltip { get; internal set; }
        public string Description { get; internal set; }
        public Color Color { get; internal set; } = Microsoft.Xna.Framework.Color.White;
    }

    /// <summary>One hierarchical row owned by a <see cref="Tree"/>.</summary>
    public sealed class TreeItem
    {
        private readonly List<TreeItem> _children = new List<TreeItem>();
        private readonly List<string> _texts = new List<string>();
        private readonly List<bool> _selectable = new List<bool>();
        private readonly List<TreeCellMode> _modes = new List<TreeCellMode>();
        private readonly List<bool> _checked = new List<bool>();
        private readonly List<bool> _indeterminate = new List<bool>();
        private readonly List<bool> _editable = new List<bool>();
        private readonly List<bool> _editMultiline = new List<bool>();
        private readonly List<string> _descriptions = new List<string>();
        private readonly List<TextDirection> _textDirections = new List<TextDirection>();
        private readonly List<string> _languages = new List<string>();
        private readonly List<string> _suffixes = new List<string>();
        private readonly List<string> _tooltips = new List<string>();
        private readonly List<float> _rangeMinimum = new List<float>();
        private readonly List<float> _rangeMaximum = new List<float>();
        private readonly List<float> _rangeStep = new List<float>();
        private readonly List<bool> _rangeExponential = new List<bool>();
        private readonly List<float> _rangeValue = new List<float>();
        private readonly List<Color?> _customColors = new List<Color?>();
        private readonly List<SpriteFont> _customFonts = new List<SpriteFont>();
        private readonly List<UIFontSelection> _customFontSelections = new List<UIFontSelection>();
        private readonly List<int> _customFontSizes = new List<int>();
        private readonly List<StyleBox> _customStyleBoxes = new List<StyleBox>();
        private readonly List<Color?> _customBackgroundColors = new List<Color?>();
        private readonly List<bool> _customBackgroundOutlines = new List<bool>();
        private readonly List<HorizontalAlignment> _textAlignments = new List<HorizontalAlignment>();
        private readonly List<bool> _expandRight = new List<bool>();
        private readonly List<Texture2D> _icons = new List<Texture2D>();
        private readonly List<Texture2D> _iconOverlays = new List<Texture2D>();
        private readonly List<Rectangle?> _iconRegions = new List<Rectangle?>();
        private readonly List<Color> _iconModulates = new List<Color>();
        private readonly List<int> _iconMaxWidths = new List<int>();
        private readonly List<List<TreeItemButton>> _buttons = new List<List<TreeItemButton>>();
        private readonly List<TreeItemCustomDrawCallback> _customDrawCallbacks = new List<TreeItemCustomDrawCallback>();
        private readonly List<bool> _customAsButtons = new List<bool>();
        private readonly List<object> _metadata = new List<object>();
        private readonly List<bool> _selectedCells = new List<bool>();
        private int _customMinimumHeight;
        private bool _acceptsChildren = true;
        private bool _foldingDisabled;
        internal TreeItem(Tree owner, TreeItem parent)
        {
            Owner = owner; Parent = parent; parent?._children.Add(this); SetColumnCount(owner.Columns);
        }
        internal Tree Owner { get; }
        public TreeItem Parent { get; private set; }
        public IReadOnlyList<TreeItem> Children => _children;
        /// <summary>Convenience alias for the first column's text.</summary>
        public string Text { get => GetText(0); set => SetText(0, value); }
        /// <summary>Convenience alias for first-column metadata. Prefer <see cref="SetMetadata"/> for Godot-compatible per-cell state.</summary>
        public object Metadata { get => GetMetadata(0); set => SetMetadata(0, value); }
        public bool Collapsed { get; private set; }
        /// <summary>Whether this item participates in the rendered and navigable tree, matching Godot's <c>visible</c> property.</summary>
        public bool Visible { get; private set; } = true;
        /// <summary>Convenience alias for whether the first column can be selected.</summary>
        public bool Selectable { get => IsSelectable(0); set => SetSelectable(0, value); }
        /// <summary>Legacy first-column editing projection. Prefer <see cref="SetEditable"/>.</summary>
        public bool Editable { get => IsEditable(0); set => SetEditable(0, value); }
        /// <summary>Convenience projection indicating whether any selectable cell in this item is selected.</summary>
        public bool IsSelected { get => IsAnyColumnSelected(); internal set { for (var column = 0; column < _selectedCells.Count; column++) _selectedCells[column] = value && _selectable[column]; } }
        public int Depth { get { var depth = 0; for (var item = Parent; item != null; item = item.Parent) depth++; return depth; } }
        /// <summary>Sets this row's minimum height, matching Godot's <c>set_custom_minimum_height()</c>.</summary>
        public void SetCustomMinimumHeight(int height)
        {
            height = Math.Max(0, height);
            if (_customMinimumHeight == height) return;
            _customMinimumHeight = height; Owner.NotifyItemMinimumHeightChanged(this);
        }
        /// <summary>Returns this row's custom minimum height, or zero when it uses the Tree default.</summary>
        public int GetCustomMinimumHeight() => _customMinimumHeight;
        public void SetCollapsed(bool collapsed)
        {
            if (Collapsed == collapsed) return;
            Collapsed = collapsed; Owner.NotifyItemCollapsed(this);
        }
        /// <summary>Applies collapse state to this item and all descendants, matching Godot's <c>set_collapsed_recursive()</c>.</summary>
        public void SetCollapsedRecursive(bool collapsed)
        {
            SetCollapsed(collapsed);
            foreach (var child in _children) child.SetCollapsedRecursive(collapsed);
        }
        /// <summary>Returns whether this subtree contains a collapsed item with children.</summary>
        public bool IsAnyCollapsed(bool onlyVisible = false)
        {
            if (Collapsed && _children.Count > 0 && (!onlyVisible || Visible && HasVisibleChildren())) return true;
            foreach (var child in _children) if ((!onlyVisible || child.Visible) && child.IsAnyCollapsed(onlyVisible)) return true;
            return false;
        }
        public void SetVisible(bool visible)
        {
            if (Visible == visible) return;
            Visible = visible; Owner.NotifyItemVisibilityChanged(this);
        }
        /// <summary>Returns whether this item and every ancestor are visible in the tree.</summary>
        public bool IsVisibleInTree()
        {
            for (var item = this; item != null; item = item.Parent) if (!item.Visible) return false;
            return true;
        }
        /// <summary>Expands this item and its ancestor chain, equivalent to Godot's <c>uncollapse_tree()</c>.</summary>
        public void UncollapseTree()
        {
            for (var item = this; item != null; item = item.Parent) item.SetCollapsed(false);
        }
        /// <summary>Returns the next rendered item, optionally wrapping to the first visible item.</summary>
        public TreeItem GetNextVisible(bool wrap = false) => Owner.GetNextVisible(this, wrap);
        /// <summary>Returns the previous rendered item, optionally wrapping to the last visible item.</summary>
        public TreeItem GetPreviousVisible(bool wrap = false) => Owner.GetPreviousVisible(this, wrap);
        /// <summary>Returns the next item in complete preorder hierarchy, including collapsed and invisible descendants.</summary>
        public TreeItem GetNextInTree(bool wrap = false) => Owner.GetTreeOrderRelative(this, 1, wrap);
        /// <summary>Returns the previous item in complete preorder hierarchy, including collapsed and invisible descendants.</summary>
        public TreeItem GetPreviousInTree(bool wrap = false) => Owner.GetTreeOrderRelative(this, -1, wrap);
        /// <summary>Returns this item's next sibling, matching Godot's <c>get_next()</c>.</summary>
        public TreeItem GetNext() => Owner.GetSibling(this, 1);
        /// <summary>Returns this item's previous sibling, matching Godot's <c>get_prev()</c>.</summary>
        public TreeItem GetPrevious() => Owner.GetSibling(this, -1);
        public TreeItem GetParent() => Parent;
        public Tree GetTree() => Owner;
        public TreeItem GetFirstChild() => _children.Count == 0 ? null : _children[0];
        public TreeItem GetLastChild() => _children.Count == 0 ? null : _children[_children.Count - 1];
        public TreeItem GetChild(int index)
        {
            if (index < 0) index += _children.Count;
            if (index < 0 || index >= _children.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return _children[index];
        }
        public int GetChildCount() => _children.Count;
        /// <summary>Returns the number of directly visible children, matching Godot's <c>get_visible_child_count()</c>.</summary>
        public int GetVisibleChildCount()
        {
            var count = 0;
            foreach (var child in _children) if (child.Visible) count++;
            return count;
        }
        /// <summary>Removes every direct child and its retained subtree from this item's visible hierarchy, matching Godot's <c>clear_children()</c>.</summary>
        public void ClearChildren() => Owner.ClearItemChildren(this);
        public int GetIndex() => Owner.GetSiblingIndex(this);
        /// <summary>Moves this item before <paramref name="item"/>, matching Godot's <c>move_before()</c>.</summary>
        public void MoveBefore(TreeItem item) => Owner.MoveItemRelative(this, item, after: false);
        /// <summary>Moves this item after <paramref name="item"/>, matching Godot's <c>move_after()</c>.</summary>
        public void MoveAfter(TreeItem item) => Owner.MoveItemRelative(this, item, after: true);
        public TreeItem CreateChild(int index = -1) => Owner.CreateItem(this, index);
        public void Remove() => Owner.RemoveItem(this);
        public void SetText(int column, string text) { EnsureColumn(column); _texts[column] = text ?? string.Empty; }
        public string GetText(int column) { EnsureColumn(column); return _texts[column]; }
        public void SetSelectable(int column, bool selectable) { EnsureColumn(column); _selectable[column] = selectable; }
        public bool IsSelectable(int column) { EnsureColumn(column); return _selectable[column]; }
        /// <summary>Returns whether a selectable cell is selected, corresponding to Godot's per-cell <c>is_selected()</c>.</summary>
        /// <remarks>The name distinguishes it from the retained layer's existing item-level <see cref="IsSelected"/> convenience property.</remarks>
        public bool IsCellSelected(int column) { EnsureColumn(column); return _selectable[column] && _selectedCells[column]; }
        /// <summary>Returns whether any selectable cell in this item is selected.</summary>
        public bool IsAnyColumnSelected()
        {
            for (var column = 0; column < _selectedCells.Count; column++) if (_selectable[column] && _selectedCells[column]) return true;
            return false;
        }
        /// <summary>Controls whether this item can be a child drop target, matching Godot's <c>set_accept_children()</c>.</summary>
        public void SetAcceptChildren(bool allowed) => _acceptsChildren = allowed;
        public bool IsAcceptingChildren() => _acceptsChildren;
        /// <summary>Selects this cell. In multi-select mode this preserves other selections, matching Godot's <c>TreeItem.select()</c>.</summary>
        public void Select(int column = 0, bool setAsCursor = true) { EnsureColumn(column); Owner.SelectItemCell(this, column, setAsCursor); }
        /// <summary>Deselects this cell, matching Godot's <c>TreeItem.deselect()</c>.</summary>
        public void Deselect(int column = 0) { EnsureColumn(column); Owner.DeselectItemCell(this, column); }
        /// <summary>Makes this cell the navigation cursor in multi-select mode without changing its selection state.</summary>
        public void SetAsCursor(int column = 0) { EnsureColumn(column); Owner.SetItemCursor(this, column); }
        public void SetCellMode(int column, TreeCellMode mode) { EnsureColumn(column); _modes[column] = mode; }
        public TreeCellMode GetCellMode(int column) { EnsureColumn(column); return _modes[column]; }
        /// <summary>Sets the primary texture for a Tree cell, corresponding to Godot's <c>set_icon()</c>.</summary>
        public void SetIcon(int column, Texture2D icon) { EnsureColumn(column); _icons[column] = icon; }
        public Texture2D GetIcon(int column) { EnsureColumn(column); return _icons[column]; }
        public void SetIconOverlay(int column, Texture2D icon) { EnsureColumn(column); _iconOverlays[column] = icon; }
        public Texture2D GetIconOverlay(int column) { EnsureColumn(column); return _iconOverlays[column]; }
        public void SetIconRegion(int column, Rectangle? region) { EnsureColumn(column); _iconRegions[column] = region; }
        public Rectangle? GetIconRegion(int column) { EnsureColumn(column); return _iconRegions[column]; }
        public void SetIconModulate(int column, Color color) { EnsureColumn(column); _iconModulates[column] = color; }
        public Color GetIconModulate(int column) { EnsureColumn(column); return _iconModulates[column]; }
        public void SetIconMaxWidth(int column, int maximumWidth) { EnsureColumn(column); _iconMaxWidths[column] = Math.Max(0, maximumWidth); }
        public int GetIconMaxWidth(int column) { EnsureColumn(column); return _iconMaxWidths[column]; }
        public void SetChecked(int column, bool value) { EnsureColumn(column); _checked[column] = value; _indeterminate[column] = false; }
        public bool IsChecked(int column) { EnsureColumn(column); return _checked[column]; }
        public void SetIndeterminate(int column, bool value)
        {
            EnsureColumn(column);
            if (_indeterminate[column] == value) return;
            _indeterminate[column] = value; _checked[column] = false;
        }
        public bool IsIndeterminate(int column) { EnsureColumn(column); return _indeterminate[column]; }
        /// <summary>Propagates this check state through descendants and updates ancestors, matching Godot's <c>propagate_check()</c>.</summary>
        public void PropagateCheck(int column, bool emitSignal = true)
        {
            EnsureColumn(column);
            if (emitSignal) Owner.NotifyCheckPropagated(this, column);
            PropagateCheckThroughChildren(column, _checked[column], emitSignal);
            PropagateCheckThroughParents(column, emitSignal);
        }
        public void SetRange(int column, float value)
        {
            EnsureColumn(column); _rangeValue[column] = SnapRange(column, MathHelper.Clamp(value, _rangeMinimum[column], _rangeMaximum[column]));
        }
        /// <summary>Convenience overload that configures and assigns a range cell in one call.</summary>
        public void SetRange(int column, float minimum, float maximum, float value) { SetRangeConfig(column, minimum, maximum, 1); SetRange(column, value); }
        public float GetRange(int column) { EnsureColumn(column); return _rangeValue[column]; }
        public float GetRangeMinimum(int column) { EnsureColumn(column); return _rangeMinimum[column]; }
        public float GetRangeMaximum(int column) { EnsureColumn(column); return _rangeMaximum[column]; }
        public void SetRangeConfig(int column, float minimum, float maximum, float step = 1, bool exponential = false)
        {
            EnsureColumn(column); _rangeMinimum[column] = Math.Min(minimum, maximum); _rangeMaximum[column] = Math.Max(minimum, maximum); _rangeStep[column] = Math.Max(0, step); _rangeExponential[column] = exponential; _rangeValue[column] = SnapRange(column, MathHelper.Clamp(_rangeValue[column], _rangeMinimum[column], _rangeMaximum[column])); _modes[column] = TreeCellMode.Range;
        }
        public void GetRangeConfig(int column, out float minimum, out float maximum, out float step) { EnsureColumn(column); minimum = _rangeMinimum[column]; maximum = _rangeMaximum[column]; step = _rangeStep[column]; }
        public bool IsRangeExponential(int column) { EnsureColumn(column); return _rangeExponential[column]; }
        public void SetEditable(int column, bool editable) { EnsureColumn(column); _editable[column] = editable; }
        public bool IsEditable(int column) { EnsureColumn(column); return _editable[column]; }
        /// <summary>Sets whether a string cell should use a multiline editor, matching Godot's <c>set_edit_multiline()</c>.</summary>
        public void SetEditMultiline(int column, bool multiline) { EnsureColumn(column); _editMultiline[column] = multiline; }
        public bool IsEditMultiline(int column) { EnsureColumn(column); return _editMultiline[column]; }
        /// <summary>Sets the accessibility description for a cell, matching Godot's <c>set_description()</c>.</summary>
        public void SetDescription(int column, string description) { EnsureColumn(column); _descriptions[column] = description ?? string.Empty; }
        public string GetDescription(int column) { EnsureColumn(column); return _descriptions[column]; }
        /// <summary>Sets the cell text direction used by a shaping renderer, matching Godot's <c>set_text_direction()</c>.</summary>
        public void SetTextDirection(int column, TextDirection direction)
        {
            EnsureColumn(column);
            if (_textDirections[column] == direction) return;
            _textDirections[column] = direction; Owner.NotifyItemPresentationChanged(this);
        }
        public TextDirection GetTextDirection(int column) { EnsureColumn(column); return _textDirections[column]; }
        /// <summary>Sets the BCP-47 shaping language for a cell, matching Godot's <c>set_language()</c>.</summary>
        public void SetLanguage(int column, string language)
        {
            EnsureColumn(column); language ??= string.Empty;
            if (_languages[column] == language) return;
            _languages[column] = language; Owner.NotifyItemPresentationChanged(this);
        }
        public string GetLanguage(int column) { EnsureColumn(column); return _languages[column]; }
        /// <summary>Sets text displayed after a cell value, matching Godot's <c>set_suffix()</c>.</summary>
        public void SetSuffix(int column, string suffix)
        {
            EnsureColumn(column); suffix ??= string.Empty;
            if (_suffixes[column] == suffix) return;
            _suffixes[column] = suffix; Owner.NotifyItemPresentationChanged(this);
        }
        public string GetSuffix(int column) { EnsureColumn(column); return _suffixes[column]; }
        /// <summary>Gets the textual projection drawn for this cell, including a range value and suffix.</summary>
        /// <remarks>Godot computes this value internally while updating its shaped text buffer; this retained inspection helper makes the renderer contract deterministic.</remarks>
        public string GetDisplayText(int column)
        {
            EnsureColumn(column);
            var text = _modes[column] == TreeCellMode.Range ? GetRangeDisplayText(column) : _texts[column];
            if (text == null) return string.Empty;
            var suffix = _suffixes[column];
            return string.IsNullOrEmpty(suffix) ? text : string.IsNullOrEmpty(text) ? suffix : text + " " + suffix;
        }
        /// <summary>Sets a Godot-style per-cell text color override.</summary>
        public void SetCustomColor(int column, Color color) { EnsureColumn(column); _customColors[column] = color; }
        public Color? GetCustomColor(int column) { EnsureColumn(column); return _customColors[column]; }
        public void ClearCustomColor(int column) { EnsureColumn(column); _customColors[column] = null; }
        /// <summary>Sets a per-cell SpriteFont override, corresponding to Godot's <c>set_custom_font()</c>. Pass <c>null</c> to inherit <see cref="Tree.Font"/>.</summary>
        public void SetCustomFont(int column, SpriteFont font) { EnsureColumn(column); _customFonts[column] = font; _customFontSelections[column].SetSpriteFont(font); Owner.NotifyItemFontChanged(this); }
        public SpriteFont GetCustomFont(int column) { EnsureColumn(column); return _customFonts[column]; }
        /// <summary>Sets a per-cell logical font override while retaining the legacy SpriteFont value.</summary>
        public void SetCustomUIFont(int column, UIFont font) { EnsureColumn(column); _customFontSelections[column].SetUIFont(font); Owner.NotifyItemFontChanged(this); }
        public UIFont GetCustomUIFont(int column) { EnsureColumn(column); return _customFontSelections[column].UIFont; }
        internal UIFont GetEffectiveCustomUIFont(int column) { EnsureColumn(column); return _customFontSelections[column].Effective; }
        /// <summary>Sets a per-cell font size override in pixels, corresponding to Godot's <c>set_custom_font_size()</c>. Use <c>-1</c> to inherit the SpriteFont's native size.</summary>
        public void SetCustomFontSize(int column, int fontSize) { EnsureColumn(column); fontSize = Math.Max(-1, fontSize); if (_customFontSizes[column] == fontSize) return; _customFontSizes[column] = fontSize; Owner.NotifyItemFontChanged(this); }
        public int GetCustomFontSize(int column) { EnsureColumn(column); return _customFontSizes[column]; }
        /// <summary>Sets a per-cell visual surface, corresponding to Godot's <c>set_custom_stylebox()</c>. Pass <c>null</c> to clear it.</summary>
        public void SetCustomStyleBox(int column, StyleBox styleBox) { EnsureColumn(column); _customStyleBoxes[column] = styleBox; }
        public StyleBox GetCustomStyleBox(int column) { EnsureColumn(column); return _customStyleBoxes[column]; }
        /// <summary>Sets a Godot-style per-cell background override. When <paramref name="outlineOnly"/> is true only a border is drawn.</summary>
        public void SetCustomBackgroundColor(int column, Color color, bool outlineOnly = false) { EnsureColumn(column); _customBackgroundColors[column] = color; _customBackgroundOutlines[column] = outlineOnly; }
        public Color? GetCustomBackgroundColor(int column) { EnsureColumn(column); return _customBackgroundColors[column]; }
        public bool IsCustomBackgroundOutline(int column) { EnsureColumn(column); return _customBackgroundOutlines[column]; }
        public void ClearCustomBackgroundColor(int column) { EnsureColumn(column); _customBackgroundColors[column] = null; _customBackgroundOutlines[column] = false; }
        public void SetTextAlignment(int column, HorizontalAlignment alignment) { EnsureColumn(column); _textAlignments[column] = alignment; }
        public HorizontalAlignment GetTextAlignment(int column) { EnsureColumn(column); return _textAlignments[column]; }
        public void SetExpandRight(int column, bool enable) { EnsureColumn(column); _expandRight[column] = enable; }
        public bool GetExpandRight(int column) { EnsureColumn(column); return _expandRight[column]; }
        /// <summary>Hides and disables this item's folding affordance, matching Godot's <c>set_disable_folding()</c>.</summary>
        public void SetDisableFolding(bool disabled)
        {
            if (_foldingDisabled == disabled) return;
            _foldingDisabled = disabled; Owner.NotifyItemFoldingPolicyChanged(this);
        }
        public bool IsFoldingDisabled() => _foldingDisabled;
        public void SetTooltipText(int column, string tooltip) { EnsureColumn(column); _tooltips[column] = tooltip ?? string.Empty; }
        public string GetTooltipText(int column) { EnsureColumn(column); return _tooltips[column]; }
        /// <summary>Sets metadata for a cell, matching Godot's <c>set_metadata()</c>.</summary>
        public void SetMetadata(int column, object metadata) { EnsureColumn(column); _metadata[column] = metadata; }
        public object GetMetadata(int column) { EnsureColumn(column); return _metadata[column]; }
        /// <summary>Removes every action button from this item, matching Godot's <c>clear_buttons()</c>.</summary>
        public void ClearButtons() { foreach (var buttons in _buttons) buttons.Clear(); }
        public void AddButton(int column, Texture2D button, int id = -1, bool disabled = false, string tooltip = "", string description = "") { EnsureColumn(column); _buttons[column].Add(new TreeItemButton(button, id, disabled, tooltip, description)); }
        public int GetButtonCount(int column) { EnsureColumn(column); return _buttons[column].Count; }
        public string GetButtonTooltipText(int column, int index) => GetButtonModel(column, index).Tooltip;
        public Texture2D GetButtonTexture(int column, int index) => GetButton(column, index);
        /// <summary>Compatibility alias for Godot's <c>get_button()</c>.</summary>
        public Texture2D GetButton(int column, int index) => GetButtonModel(column, index).Texture;
        public int GetButtonId(int column, int index) => GetButtonModel(column, index).Id;
        public int GetButtonById(int column, int id)
        {
            EnsureColumn(column);
            for (var index = 0; index < _buttons[column].Count; index++) if (_buttons[column][index].Id == id) return index;
            return -1;
        }
        public Color GetButtonColor(int column, int index) => GetButtonModel(column, index).Color;
        public void SetButtonTooltipText(int column, int index, string tooltip) { GetButtonModel(column, index).Tooltip = tooltip ?? string.Empty; }
        public void SetButton(int column, int index, Texture2D button) { GetButtonModel(column, index).Texture = button; }
        public void SetButtonDescription(int column, int index, string description) { GetButtonModel(column, index).Description = description ?? string.Empty; }
        public string GetButtonDescription(int column, int index) => GetButtonModel(column, index).Description;
        public void SetButtonColor(int column, int index, Color color) { GetButtonModel(column, index).Color = color; }
        public void SetButtonDisabled(int column, int index, bool disabled) { GetButtonModel(column, index).Disabled = disabled; }
        public bool IsButtonDisabled(int column, int index) => GetButtonModel(column, index).Disabled;
        /// <summary>Removes one action button, matching Godot's <c>erase_button()</c>.</summary>
        public void EraseButton(int column, int index)
        {
            EnsureColumn(column);
            if (index < 0 || index >= _buttons[column].Count) throw new ArgumentOutOfRangeException(nameof(index));
            _buttons[column].RemoveAt(index); Owner.NotifyItemPresentationChanged(this);
        }
        /// <summary>Sets the drawing callback for a custom cell, corresponding to Godot's <c>set_custom_draw_callback()</c>.</summary>
        public void SetCustomDrawCallback(int column, TreeItemCustomDrawCallback callback) { EnsureColumn(column); _customDrawCallbacks[column] = callback; }
        public TreeItemCustomDrawCallback GetCustomDrawCallback(int column) { EnsureColumn(column); return _customDrawCallbacks[column]; }
        /// <summary>Gives an editable custom cell button presentation, corresponding to Godot's <c>set_custom_as_button()</c>.</summary>
        public void SetCustomAsButton(int column, bool enable) { EnsureColumn(column); _customAsButtons[column] = enable; }
        public bool IsCustomSetAsButton(int column) { EnsureColumn(column); return _customAsButtons[column]; }
        internal void SetColumnCount(int count)
        {
            while (_texts.Count < count) _texts.Add(string.Empty);
            while (_selectable.Count < count) _selectable.Add(true);
            while (_modes.Count < count) _modes.Add(TreeCellMode.String);
            while (_checked.Count < count) _checked.Add(false);
            while (_indeterminate.Count < count) _indeterminate.Add(false);
            while (_editable.Count < count) _editable.Add(false);
            while (_editMultiline.Count < count) _editMultiline.Add(false);
            while (_descriptions.Count < count) _descriptions.Add(string.Empty);
            while (_textDirections.Count < count) _textDirections.Add(TextDirection.Inherited);
            while (_languages.Count < count) _languages.Add(string.Empty);
            while (_suffixes.Count < count) _suffixes.Add(string.Empty);
            while (_tooltips.Count < count) _tooltips.Add(string.Empty);
            while (_rangeMinimum.Count < count) _rangeMinimum.Add(0);
            while (_rangeMaximum.Count < count) _rangeMaximum.Add(100);
            while (_rangeStep.Count < count) _rangeStep.Add(1);
            while (_rangeExponential.Count < count) _rangeExponential.Add(false);
            while (_rangeValue.Count < count) _rangeValue.Add(0);
            while (_customColors.Count < count) _customColors.Add(null);
            while (_customFonts.Count < count) _customFonts.Add(null);
            while (_customFontSelections.Count < count) _customFontSelections.Add(new UIFontSelection());
            while (_customFontSizes.Count < count) _customFontSizes.Add(-1);
            while (_customStyleBoxes.Count < count) _customStyleBoxes.Add(null);
            while (_customBackgroundColors.Count < count) _customBackgroundColors.Add(null);
            while (_customBackgroundOutlines.Count < count) _customBackgroundOutlines.Add(false);
            while (_textAlignments.Count < count) _textAlignments.Add(HorizontalAlignment.Left);
            while (_expandRight.Count < count) _expandRight.Add(false);
            while (_icons.Count < count) _icons.Add(null);
            while (_iconOverlays.Count < count) _iconOverlays.Add(null);
            while (_iconRegions.Count < count) _iconRegions.Add(null);
            while (_iconModulates.Count < count) _iconModulates.Add(Color.White);
            while (_iconMaxWidths.Count < count) _iconMaxWidths.Add(0);
            while (_buttons.Count < count) _buttons.Add(new List<TreeItemButton>());
            while (_customDrawCallbacks.Count < count) _customDrawCallbacks.Add(null);
            while (_customAsButtons.Count < count) _customAsButtons.Add(false);
            while (_metadata.Count < count) _metadata.Add(null);
            while (_selectedCells.Count < count) _selectedCells.Add(false);
            if (_texts.Count > count) _texts.RemoveRange(count, _texts.Count - count);
            if (_selectable.Count > count) _selectable.RemoveRange(count, _selectable.Count - count);
            if (_modes.Count > count) _modes.RemoveRange(count, _modes.Count - count);
            if (_checked.Count > count) _checked.RemoveRange(count, _checked.Count - count);
            if (_indeterminate.Count > count) _indeterminate.RemoveRange(count, _indeterminate.Count - count);
            if (_editable.Count > count) _editable.RemoveRange(count, _editable.Count - count);
            if (_editMultiline.Count > count) _editMultiline.RemoveRange(count, _editMultiline.Count - count);
            if (_descriptions.Count > count) _descriptions.RemoveRange(count, _descriptions.Count - count);
            if (_textDirections.Count > count) _textDirections.RemoveRange(count, _textDirections.Count - count);
            if (_languages.Count > count) _languages.RemoveRange(count, _languages.Count - count);
            if (_suffixes.Count > count) _suffixes.RemoveRange(count, _suffixes.Count - count);
            if (_tooltips.Count > count) _tooltips.RemoveRange(count, _tooltips.Count - count);
            if (_rangeMinimum.Count > count) _rangeMinimum.RemoveRange(count, _rangeMinimum.Count - count);
            if (_rangeMaximum.Count > count) _rangeMaximum.RemoveRange(count, _rangeMaximum.Count - count);
            if (_rangeStep.Count > count) _rangeStep.RemoveRange(count, _rangeStep.Count - count);
            if (_rangeExponential.Count > count) _rangeExponential.RemoveRange(count, _rangeExponential.Count - count);
            if (_rangeValue.Count > count) _rangeValue.RemoveRange(count, _rangeValue.Count - count);
            if (_customColors.Count > count) _customColors.RemoveRange(count, _customColors.Count - count);
            if (_customFonts.Count > count) _customFonts.RemoveRange(count, _customFonts.Count - count);
            if (_customFontSelections.Count > count) _customFontSelections.RemoveRange(count, _customFontSelections.Count - count);
            if (_customFontSizes.Count > count) _customFontSizes.RemoveRange(count, _customFontSizes.Count - count);
            if (_customStyleBoxes.Count > count) _customStyleBoxes.RemoveRange(count, _customStyleBoxes.Count - count);
            if (_customBackgroundColors.Count > count) _customBackgroundColors.RemoveRange(count, _customBackgroundColors.Count - count);
            if (_customBackgroundOutlines.Count > count) _customBackgroundOutlines.RemoveRange(count, _customBackgroundOutlines.Count - count);
            if (_textAlignments.Count > count) _textAlignments.RemoveRange(count, _textAlignments.Count - count);
            if (_expandRight.Count > count) _expandRight.RemoveRange(count, _expandRight.Count - count);
            if (_icons.Count > count) _icons.RemoveRange(count, _icons.Count - count);
            if (_iconOverlays.Count > count) _iconOverlays.RemoveRange(count, _iconOverlays.Count - count);
            if (_iconRegions.Count > count) _iconRegions.RemoveRange(count, _iconRegions.Count - count);
            if (_iconModulates.Count > count) _iconModulates.RemoveRange(count, _iconModulates.Count - count);
            if (_iconMaxWidths.Count > count) _iconMaxWidths.RemoveRange(count, _iconMaxWidths.Count - count);
            if (_buttons.Count > count) _buttons.RemoveRange(count, _buttons.Count - count);
            if (_customDrawCallbacks.Count > count) _customDrawCallbacks.RemoveRange(count, _customDrawCallbacks.Count - count);
            if (_customAsButtons.Count > count) _customAsButtons.RemoveRange(count, _customAsButtons.Count - count);
            if (_metadata.Count > count) _metadata.RemoveRange(count, _metadata.Count - count);
            if (_selectedCells.Count > count) _selectedCells.RemoveRange(count, _selectedCells.Count - count);
        }
        private float SnapRange(int column, float value)
        {
            var step = _rangeStep[column]; if (step <= 0) return value;
            return MathHelper.Clamp(_rangeMinimum[column] + MathF.Round((value - _rangeMinimum[column]) / step) * step, _rangeMinimum[column], _rangeMaximum[column]);
        }
        private string GetRangeDisplayText(int column)
        {
            if (!string.IsNullOrEmpty(_texts[column]))
            {
                if (!_editable[column]) return null;
                var option = (int)_rangeValue[column];
                var label = "(Other)";
                var options = _texts[column].Split(',');
                for (var index = 0; index < options.Length; index++)
                {
                    var entry = options[index];
                    var separator = entry.IndexOf(':');
                    var value = index;
                    if (separator >= 0 && separator < entry.Length - 1)
                        int.TryParse(entry.Substring(separator + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
                    if (option != value) continue;
                    label = separator < 0 ? entry : entry.Substring(0, separator);
                    break;
                }
                return label;
            }
            var decimals = GetRangeStepDecimals(_rangeStep[column]);
            return _rangeValue[column].ToString(decimals == 0 ? "0" : "0." + new string('#', decimals), CultureInfo.InvariantCulture);
        }
        private static int GetRangeStepDecimals(float step)
        {
            step = MathF.Abs(step);
            var decimals = 0;
            while (decimals < 6 && MathF.Abs(step - MathF.Round(step)) > .00001f) { step *= 10; decimals++; }
            return decimals;
        }
        private void PropagateCheckThroughChildren(int column, bool value, bool emitSignal)
        {
            foreach (var child in _children)
            {
                child.SetChecked(column, value);
                if (emitSignal) Owner.NotifyCheckPropagated(child, column);
                child.PropagateCheckThroughChildren(column, value, emitSignal);
            }
        }
        private void PropagateCheckThroughParents(int column, bool emitSignal)
        {
            var parent = Parent; if (parent == null) return;
            var anyChecked = false; var anyUnchecked = false; var anyIndeterminate = false;
            foreach (var child in parent._children)
            {
                if (child.IsChecked(column)) anyChecked = true;
                else
                {
                    anyUnchecked = true;
                    if (child.IsIndeterminate(column)) { anyIndeterminate = true; break; }
                }
            }
            if (anyIndeterminate || anyChecked && anyUnchecked) parent.SetIndeterminate(column, true);
            else if (parent.IsIndeterminate(column) && !anyChecked) parent.SetIndeterminate(column, false);
            else parent.SetChecked(column, anyChecked);
            if (emitSignal) Owner.NotifyCheckPropagated(parent, column);
            parent.PropagateCheckThroughParents(column, emitSignal);
        }
        private bool HasVisibleChildren() { foreach (var child in _children) if (child.Visible) return true; return false; }
        private TreeItemButton GetButtonModel(int column, int index)
        {
            EnsureColumn(column);
            if (index < 0 || index >= _buttons[column].Count) throw new ArgumentOutOfRangeException(nameof(index));
            return _buttons[column][index];
        }
        internal TreeItemButton GetButtonModelForTree(int column, int index) => GetButtonModel(column, index);
        internal TreeItemCustomDrawCallback GetCustomDrawCallbackForTree(int column) => GetCustomDrawCallback(column);
        internal void SetSelectedForTree(int column, bool selected) { EnsureColumn(column); _selectedCells[column] = selected && _selectable[column]; }
        private void EnsureColumn(int column)
        {
            if (column < 0 || column >= Owner.Columns) throw new ArgumentOutOfRangeException(nameof(column));
        }
        internal void RemoveChild(TreeItem child) => _children.Remove(child);
        internal int GetChildIndex(TreeItem child) => _children.IndexOf(child);
        internal void InsertChild(int index, TreeItem child) => _children.Insert(index, child);
        internal void SetParentForTree(TreeItem parent) => Parent = parent;
        internal void ClearChildrenForTree()
        {
            foreach (var child in _children) child.SetParentForTree(null);
            _children.Clear();
        }
    }

    /// <summary>Hierarchical, column-oriented selection control modeled after Godot's Tree.</summary>
    [TemplatePart(TreePresenterPartName, typeof(Container))]
    public sealed class Tree : TemplatedControl
    {
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.Tree;
        public const string TreePresenterPartName = "PART_TreePresenter";
        private TreePresenter _treePresenter;
        private readonly UIFontSelection _fontSelection = new UIFontSelection();
        private sealed class TreeRangeEditorLineEdit : LineEdit
        {
            public Action CancelRequested { get; set; }
            internal override void KeyPressed(Keys key)
            {
                if (key == Keys.Escape) { CancelRequested?.Invoke(); return; }
                base.KeyPressed(key);
            }
        }

        protected override void OnTemplateApplied()
        {
            var presenter = GetTemplateChild(TreePresenterPartName) as TreePresenter;
            if (!ReferenceEquals(_treePresenter, presenter)) _treePresenter?.Deactivate();
            _treePresenter = presenter;
            if (presenter != null)
            {
                presenter.Owner = this;
                presenter.Activate();
            }
            base.OnTemplateApplied();
        }
        private sealed class TreeStringEditorTextEdit : TextEdit
        {
            public Action CancelRequested { get; set; }
            public Action CommitRequested { get; set; }
            internal override void KeyPressed(Keys key)
            {
                if (key == Keys.Escape) { CancelRequested?.Invoke(); return; }
                var keyboard = Context?.CurrentKeyboardState ?? default;
                if (key == Keys.Enter && (keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl) || keyboard.IsKeyDown(Keys.LeftWindows) || keyboard.IsKeyDown(Keys.RightWindows)))
                {
                    CommitRequested?.Invoke();
                    return;
                }
                base.KeyPressed(key);
            }
        }
        private sealed class TreeColumn
        {
            public string Title = string.Empty;
            public string TitleTooltip = string.Empty;
            public HorizontalAlignment TitleAlignment = HorizontalAlignment.Center;
            public TextDirection TitleDirection = TextDirection.Inherited;
            public string TitleLanguage = string.Empty;
            public int CustomMinimumWidth;
            public bool Expand = true;
            public int ExpandRatio = 1;
            public bool ClipContent;
        }

        private readonly List<TreeItem> _roots = new List<TreeItem>();
        private readonly List<TreeColumn> _columns = new List<TreeColumn> { new TreeColumn() };
        private TreeItem _selected;
        private int _selectedColumn;
        private TreeItem _pressedButtonItem;
        private int _pressedButtonColumn = -1;
        private int _pressedButtonIndex = -1;
        private int _resizingColumn = -1;
        private int _resizeStartX;
        private int _resizeStartWidth;
        private TreeItem _shiftSelectionAnchor;
        private string _incrementalSearch = string.Empty;
        private TimeSpan _lastTextInputTime = TimeSpan.MinValue;
        private TimeSpan _processTime;
        private TreeSelectMode _selectMode;
        private int _horizontalScrollOffset;
        private int _verticalScrollOffset;
        private bool _dragAutoScrolling;
        private Point _dragPointer;
        private TreeItem _editedItem;
        private int _editedColumn = -1;
        private Rectangle _customPopupRect;
        private TreeItem _dragUnfoldTarget;
        private TimeSpan _dragUnfoldElapsed;
        private readonly HScrollBar _horizontalScrollBar;
        private readonly VScrollBar _verticalScrollBar;
        private readonly PopupMenu _rangePopup;
        private readonly List<float> _rangePopupValues = new List<float>();
        private TreeItem _rangePopupItem;
        private int _rangePopupColumn = -1;
        private readonly PopupPanel _rangeEditorPopup;
        private readonly TreeRangeEditorLineEdit _rangeEditorText;
        private readonly HSlider _rangeEditorSlider;
        private TreeItem _rangeEditorItem;
        private int _rangeEditorColumn = -1;
        private bool _syncingRangeEditor;
        private readonly PopupPanel _stringEditorPopup;
        private readonly LineEdit _stringEditorLine;
        private readonly TreeStringEditorTextEdit _stringEditorText;
        private TreeItem _stringEditorItem;
        private int _stringEditorColumn = -1;
        private bool _stringEditorMultiline;
        private bool _stringEditorCommitted;
        private TreeItem _stringEditPressItem;
        private int _stringEditPressColumn = -1;
        private TreeItem _rangeDragItem;
        private int _rangeDragColumn = -1;
        private Point _rangeDragStart;
        private Point _rangeDragPrevious;
        private bool _rangeDragging;
        private TreeItem _rangeStepRepeatItem;
        private int _rangeStepRepeatColumn = -1;
        private bool _rangeStepRepeatUp;
        private TimeSpan _rangeStepRepeatElapsed;
        private TimeSpan _rangeStepRepeatIntervalElapsed;
        private bool _syncingScrollBars;
        private bool _dragTouching;
        private bool _dragTouchingDeaccel;
        private float _dragSpeed;
        private float _dragAccum;
        private float _dragFrom;
        private const float DragDeceleration = 1000;

        public Tree()
        {
            FocusMode = FocusMode.All; ClipContents = true; CustomMinimumSize = new Vector2(120, 80);
            _horizontalScrollBar = new HScrollBar { ZIndex = 1, Visible = false, TooltipText = "Tree scroll" };
            _verticalScrollBar = new VScrollBar { ZIndex = 1, Visible = false, TooltipText = "Tree scroll" };
            _rangePopup = new PopupMenu { Visible = false };
            _rangePopup.IndexPressed += (_, index) => SelectRangePopupOption(index);
            _rangeEditorPopup = new PopupPanel { Visible = false, CustomMinimumSize = new Vector2(80, 56) };
            _rangeEditorText = new TreeRangeEditorLineEdit { SubmitOnFocusExit = false };
            _rangeEditorSlider = new HSlider();
            _rangeEditorText.TextSubmitted += (_, _) => CommitRangeEditor();
            _rangeEditorText.CancelRequested = CancelRangeEditor;
            _rangeEditorSlider.ValueChanged += (_, value) => { if (!_syncingRangeEditor) _rangeEditorText.Text = value.ToString(CultureInfo.InvariantCulture); };
            _rangeEditorPopup.PopupHidden += (_, _) => ClearRangeEditorState();
            _rangeEditorPopup.AddChild(_rangeEditorText);
            _rangeEditorPopup.AddChild(_rangeEditorSlider);
            _stringEditorPopup = new PopupPanel { Visible = false, CustomMinimumSize = new Vector2(80, 24) };
            _stringEditorLine = new LineEdit { SubmitOnFocusExit = false };
            _stringEditorText = new TreeStringEditorTextEdit();
            _stringEditorLine.TextSubmitted += (_, _) => CommitStringEditor();
            _stringEditorText.CancelRequested = CancelStringEditor;
            _stringEditorText.CommitRequested = CommitStringEditor;
            _stringEditorPopup.PopupHidden += (_, reason) => CloseStringEditor(reason);
            _stringEditorPopup.AddChild(_stringEditorLine);
            _stringEditorPopup.AddChild(_stringEditorText);
            _horizontalScrollBar.ValueChanged += (_, value) => { if (!_syncingScrollBars) SetHorizontalScrollOffset((int)MathF.Round(value)); };
            _verticalScrollBar.ValueChanged += (_, value) => { if (!_syncingScrollBars) SetVerticalScrollOffset((int)MathF.Round(value)); };
            AddChild(_horizontalScrollBar);
            AddChild(_verticalScrollBar);
            DragStarted += (_, _) => { _dragAutoScrolling = DragAutoScrollEnabled; UpdateDragUnfoldTarget(_dragPointer); };
            DragEnded += (_, _) => { _dragAutoScrolling = false; ResetDragUnfoldTarget(); };
        }
        public IReadOnlyList<TreeItem> RootItems => _roots;
        public bool HideRoot { get; set; }
        public bool AllowReselect { get; set; }
        /// <summary>Allows a right pointer press to select an item, matching Godot's <c>allow_rmb_select</c>. Disabled by default.</summary>
        public bool AllowRightMouseSelect { get; set; }
        /// <summary>Legacy convenience alias for <see cref="SelectMode"/> set to <see cref="TreeSelectMode.Multi"/>.</summary>
        public bool AllowMultiSelect { get => SelectMode == TreeSelectMode.Multi; set => SelectMode = value ? TreeSelectMode.Multi : TreeSelectMode.Single; }
        /// <summary>Configures single-cell, full-row, or multi-cell selection, matching Godot's <c>select_mode</c>.</summary>
        public TreeSelectMode SelectMode
        {
            get => _selectMode;
            set
            {
                if (_selectMode == value) return;
                _selectMode = value;
                if (_selected != null) Select(_selected, Math.Max(0, _selectedColumn), emit: false);
            }
        }
        /// <summary>Enables Godot-style case-insensitive incremental text search for focused tree items.</summary>
        public bool AllowSearch { get; set; } = true;
        /// <summary>Uses a cell's text as its tooltip when no explicit cell tooltip is set, matching Godot's <c>set_auto_tooltip()</c>.</summary>
        public bool AutoTooltipEnabled { get; private set; } = true;
        /// <summary>Allows checkbox cell edits only when the checkbox glyph itself is pressed, matching Godot's checkbox edit policy.</summary>
        public bool EditCheckboxCellOnlyWhenCheckboxPressed { get; private set; }
        /// <summary>Hides folding arrows and disables their pointer toggle region, matching Godot's <c>set_hide_folding()</c>.</summary>
        public bool FoldingHidden { get; private set; }
        /// <summary>Allows Shift-clicking a folding arrow to collapse or expand every descendant, matching Godot's recursive folding policy.</summary>
        public bool RecursiveFoldingEnabled { get; private set; } = true;
        /// <summary>Enables delayed expansion when a same-tree drag hovers a collapsed item, matching Godot's drag-unfold policy.</summary>
        public bool DragUnfoldingEnabled { get; private set; } = true;
        /// <summary>Delay before a collapsed drag target unfolds. Godot's themed default is 500 ms.</summary>
        public TimeSpan DragUnfoldDelay { get; set; } = TimeSpan.FromMilliseconds(500);
        /// <summary>Initial hold duration before an editable numeric range stepper repeats, matching Godot's 600 ms default.</summary>
        public TimeSpan RangeStepRepeatDelay { get; set; } = TimeSpan.FromMilliseconds(600);
        /// <summary>Repeat cadence for a held numeric range stepper, matching Godot's 50 ms default.</summary>
        public TimeSpan RangeStepRepeatInterval { get; set; } = TimeSpan.FromMilliseconds(50);
        /// <summary>Maximum interval between typed characters that belong to the same incremental search.</summary>
        public TimeSpan IncrementalSearchTimeout { get; set; } = TimeSpan.FromSeconds(1);
        public string IncrementalSearchText => _incrementalSearch;
        public float ItemHeight { get; set; } = 24;
        public float Indent { get; set; } = 16;
        public SpriteFont Font { get => _fontSelection.SpriteFont; set { if (_fontSelection.SetSpriteFont(value)) QueueLayout(); } }
        public UIFont UIFont { get => _fontSelection.UIFont; set { if (_fontSelection.SetUIFont(value)) QueueLayout(); } }
        internal UIFont EffectiveUIFont => ResolveFont(_fontSelection);
        public TreeItem SelectedItem => _selected;
        public int SelectedColumn => _selected == null ? -1 : _selectedColumn;
        /// <summary>Returns the cell currently opened for editing, matching Godot's <c>get_edited()</c>.</summary>
        public TreeItem GetEdited() => _editedItem;
        public int GetEditedColumn() => _editedColumn;
        /// <summary>Returns the edited custom cell's rectangle for application popup placement, matching Godot's <c>get_custom_popup_rect()</c>.</summary>
        public Rectangle GetCustomPopupRect() => _customPopupRect;
        public bool IsEditing() => _editedItem != null;
        public int Columns
        {
            get => _columns.Count;
            set
            {
                value = Math.Max(1, value);
                if (value == _columns.Count) return;
                while (_columns.Count < value) _columns.Add(new TreeColumn());
                if (_columns.Count > value) _columns.RemoveRange(value, _columns.Count - value);
                foreach (var item in AllItems()) item.SetColumnCount(value);
                if (_selectedColumn >= value) _selectedColumn = value - 1;
                QueueLayout();
            }
        }
        public bool ColumnTitlesVisible { get; set; }
        /// <summary>Enables Tree horizontal scrolling, matching Godot's <c>set_h_scroll_enabled()</c>.</summary>
        public bool HorizontalScrollEnabled { get; private set; } = true;
        /// <summary>Enables Tree vertical scrolling, matching Godot's <c>set_v_scroll_enabled()</c>.</summary>
        public bool VerticalScrollEnabled { get; private set; } = true;
        /// <summary>Whether the retained vertical scrollbar is currently visible for overflowing Tree rows.</summary>
        public bool IsVerticalScrollBarVisible => _verticalScrollBar.Visible;
        /// <summary>Whether the retained horizontal scrollbar is currently visible for overflowing Tree columns.</summary>
        public bool IsHorizontalScrollBarVisible => _horizontalScrollBar.Visible;
        /// <summary>Configures Godot-style top/bottom overflow indicators.</summary>
        public TreeScrollHintMode ScrollHintMode { get; private set; }
        /// <summary>Retains Godot's texture tiling policy for themed scroll hints. The default retained solid hint does not tile.</summary>
        public bool TileScrollHint { get; private set; }
        /// <summary>Height in pixels of each retained top or bottom scroll hint.</summary>
        public int ScrollHintHeight { get; set; } = 3;
        /// <summary>Enables vertical edge scrolling while a retained drag is active, corresponding to Godot Tree's themed drag-scroll behavior.</summary>
        public bool DragAutoScrollEnabled { get; set; } = true;
        /// <summary>Width in pixels of the top and bottom drag-edge zones.</summary>
        public int DragAutoScrollBorder { get; set; } = 24;
        /// <summary>Maximum vertical auto-scroll speed in pixels per second at an edge.</summary>
        public float DragAutoScrollSpeed { get; set; } = 240;
        /// <summary>Enables pointer resizing of visible column-header dividers.</summary>
        public bool ColumnResizeEnabled { get; set; } = true;
        /// <summary>Screen-space tolerance around a column divider that starts resize capture.</summary>
        public int ColumnResizeHandleWidth { get; set; } = 4;
        /// <summary>Enables item and/or in-between regions returned by <see cref="GetDropSectionAtPosition"/>.</summary>
        public TreeDropModeFlags DropModeFlags { get; set; }
        /// <summary>Enables retained same-tree drag reparenting. Godot applications commonly provide their own payload policy through the Control drag/drop virtuals, so this is opt-in.</summary>
        public bool SelfDragDropEnabled { get; set; }
        /// <summary>Application drag-payload hook corresponding to Godot's overridable <c>_get_drag_data()</c>. It receives the hit item and column, which can be <c>null</c> and <c>-1</c>.</summary>
        public Func<Tree, TreeItem, int, Point, object> DragDataProvider { get; set; }
        /// <summary>Application drop-acceptance hook corresponding to Godot's overridable <c>_can_drop_data()</c>.</summary>
        public Func<Tree, TreeItem, int, Point, object, bool> CanDropDataProvider { get; set; }
        /// <summary>Application drop-delivery hook corresponding to Godot's overridable <c>_drop_data()</c>.</summary>
        public Action<Tree, TreeItem, int, Point, object> DropDataHandler { get; set; }
        /// <summary>Godot's sentinel returned when no drop target exists.</summary>
        public const int DropSectionNotFound = -100;
        public event Action<Tree, TreeItem> ItemSelected;
        public event Action<Tree, TreeItem, int> CellSelected;
        public event Action<Tree, TreeItem> ItemActivated;
        public event Action<Tree, TreeItem> ItemCollapsed;
        public event Action<Tree, TreeItem, int> ItemEdited;
        /// <summary>Raised when a custom cell requests an application popup, matching Godot's <c>custom_popup_edited</c> signal. Query the edited-cell methods for context.</summary>
        public event Action<Tree, bool> CustomPopupEdited;
        /// <summary>Raised for each item affected by <see cref="TreeItem.PropagateCheck"/>, matching Godot's <c>check_propagated_to_item</c> signal.</summary>
        public event Action<Tree, TreeItem, int> CheckPropagatedToItem;
        /// <summary>Raised when a cell is selected or deselected in multi-select mode. The final argument is the new selected state.</summary>
        public event Action<Tree, TreeItem, int, bool> MultiSelected;
        /// <summary>Raised on left or right release over a visible column title, corresponding to Godot's <c>column_title_clicked(column, mouse_button_index)</c> signal.</summary>
        public event Action<Tree, int, PointerButton> ColumnTitleClicked;
        /// <summary>Raised when item selection is initiated with a pointer button, corresponding to Godot's <c>item_mouse_selected(mouse_position, mouse_button_index)</c> signal.</summary>
        public event Action<Tree, Point, PointerButton> ItemMouseSelected;
        /// <summary>Raised after an interactive column-header resize changes the retained minimum width.</summary>
        public event Action<Tree, int, int> ColumnResized;
        /// <summary>Raised when an enabled TreeItem action button is clicked. Arguments are item, column, and button id.</summary>
        public event Action<Tree, TreeItem, int, int> ButtonClicked;
        /// <summary>Raised after an opt-in same-tree drag successfully changes hierarchy. Arguments are dragged item, target item, and drop section.</summary>
        public event Action<Tree, TreeItem, TreeItem, int> ItemDropped;

        public TreeItem CreateItem(TreeItem parent = null, int index = -1)
        {
            if (parent != null && parent.Owner != this) throw new InvalidOperationException("The parent item belongs to another tree.");
            if (parent == null && _roots.Count > 0) parent = _roots[0];
            var item = new TreeItem(this, parent);
            if (parent == null)
            {
                _roots.Add(item);
                if (index >= 0)
                {
                    _roots.RemoveAt(_roots.Count - 1);
                    _roots.Insert(Math.Min(index, _roots.Count), item);
                }
            }
            else if (index >= 0)
            {
                parent.RemoveChild(item);
                parent.InsertChild(Math.Min(index, parent.GetChildCount()), item);
            }
            QueueLayout(); return item;
        }
        public void Clear()
        {
            if (_stringEditorPopup.Visible) _stringEditorPopup.Hide(PopupHideReason.Cancelled);
            _roots.Clear(); _selected = null; _selectedColumn = 0; _editedItem = null; _editedColumn = -1; _customPopupRect = Rectangle.Empty; QueueLayout();
        }
        /// <summary>Returns the first root item, matching Godot's <c>get_root()</c>.</summary>
        public TreeItem GetRoot() => _roots.Count == 0 ? null : _roots[0];
        /// <summary>Returns the deepest final child of the root while it remains expanded, matching Godot's <c>get_last_item()</c>.</summary>
        public TreeItem GetLastItem()
        {
            var item = GetRoot();
            while (item != null && !item.Collapsed && item.Children.Count > 0) item = item.Children[item.Children.Count - 1];
            return item;
        }
        /// <summary>Returns the next selected item in complete tree order after <paramref name="from"/>, matching Godot's <c>get_next_selected()</c>.</summary>
        public TreeItem GetNextSelected(TreeItem from = null)
        {
            if (from != null && from.Owner != this) return null;
            var items = new List<TreeItem>(AllItems()); var start = from == null ? 0 : items.IndexOf(from) + 1;
            for (var index = start; index >= 0 && index < items.Count; index++) if (items[index].IsAnyColumnSelected()) return items[index];
            return null;
        }
        public TreeItem GetSelected() => _selected;
        public int GetSelectedColumn() => SelectedColumn;
        /// <summary>Returns the index of the action button held by the current pointer gesture, or -1.</summary>
        public int GetPressedButton() => _pressedButtonIndex;
        /// <summary>Compatibility method for Godot's <c>set_selected()</c>; it replaces the existing selection.</summary>
        public void SetSelected(TreeItem item, int column = 0) => Select(item, column, emit: false);
        /// <summary>Opens the selected editable cell for application-provided editing, matching Godot's <c>edit_selected()</c>.</summary>
        public bool EditSelected(bool forceEdit = false)
        {
            if (_selected == null || _selectedColumn < 0 || (!forceEdit && !_selected.IsEditable(_selectedColumn))) return false;
            if (_selected.GetCellMode(_selectedColumn) == TreeCellMode.Range)
            {
                var cell = GetItemAreaRectangle(_selected, _selectedColumn);
                if (!string.IsNullOrEmpty(_selected.GetText(_selectedColumn))) ShowRangePopup(_selected, _selectedColumn, cell);
                else ShowRangeEditor(_selected, _selectedColumn, cell);
                return true;
            }
            if (_selected.GetCellMode(_selectedColumn) == TreeCellMode.String)
            {
                ShowStringEditor(_selected, _selectedColumn, GetItemAreaRectangle(_selected, _selectedColumn));
                return true;
            }
            BeginCellEdit(_selected, _selectedColumn, _selected.GetCellMode(_selectedColumn) == TreeCellMode.Custom);
            return true;
        }
        public void SetSelectMode(TreeSelectMode mode) => SelectMode = mode;
        public TreeSelectMode GetSelectMode() => SelectMode;
        /// <summary>Clears every selected cell and navigation cursor, matching Godot's <c>deselect_all()</c>.</summary>
        public void DeselectAll()
        {
            foreach (var item in AllItems()) item.IsSelected = false;
            _selected = null; _selectedColumn = 0;
        }
        /// <summary>Returns whether the Tree has a selection cursor, matching Godot's <c>is_anything_selected()</c>.</summary>
        public bool IsAnythingSelected() => _selected != null;
        /// <summary>Returns the visible item at a screen position, matching Godot's <c>get_item_at_position()</c>.</summary>
        public TreeItem GetItemAtPosition(Point position)
        {
            if (!Bounds.Contains(position) || position.X < ContentLeft || position.X >= ContentLeft + ContentWidth || ColumnTitlesVisible && position.Y < RowOriginY) return null;
            var rows = Flatten(); var index = GetRowIndexAtPosition(rows, position.Y);
            return index >= 0 ? rows[index] : null;
        }
        /// <summary>Returns the column of the visible item at a screen position, or -1 when none is present.</summary>
        public int GetColumnAtPosition(Point position) => GetItemAtPosition(position) == null ? -1 : GetColumnAtX(position.X);
        /// <summary>Returns the action-button ID at a screen position, or -1 when the position does not hit a visible action button.</summary>
        public int GetButtonIdAtPosition(Point position)
        {
            var item = GetItemAtPosition(position);
            if (item == null) return -1;
            var rows = Flatten(); var rowIndex = rows.IndexOf(item); var column = GetColumnAtX(position.X);
            if (column < 0) return -1;
            var button = GetButtonAtPosition(item, column, rowIndex, position, rows);
            return button < 0 ? -1 : item.GetButtonId(column, button);
        }
        /// <summary>Returns Godot's drop section (-1 before, 0 on, 1 after, 2 as first child), or <see cref="DropSectionNotFound"/>.</summary>
        public int GetDropSectionAtPosition(Point position)
        {
            if (DropModeFlags == TreeDropModeFlags.Disabled) return DropSectionNotFound;
            var item = GetItemAtPosition(position); if (item == null) return DropSectionNotFound;
            var row = GetItemAreaRectangle(item);
            var parentAllows = item.Parent != null && item.Parent.IsAcceptingChildren();
            var itemAllows = item.IsAcceptingChildren();
            if (DropModeFlags == TreeDropModeFlags.OnItem) return itemAllows ? 0 : DropSectionNotFound;
            if (DropModeFlags == TreeDropModeFlags.InBetween) return parentAllows ? position.Y < row.Center.Y ? -1 : 1 : DropSectionNotFound;
            if (position.Y < row.Y + row.Height / 4) return parentAllows ? -1 : itemAllows ? 0 : DropSectionNotFound;
            if (position.Y >= row.Bottom - row.Height / 4) return item.Children.Count > 0 && itemAllows ? 2 : parentAllows ? 1 : DropSectionNotFound;
            return itemAllows ? 0 : parentAllows ? position.Y < row.Center.Y ? -1 : 1 : DropSectionNotFound;
        }
        /// <summary>Finds the first item in tree order with matching cell metadata, matching Godot's <c>get_item_with_metadata()</c>.</summary>
        public TreeItem GetItemWithMetadata(object metadata, int column = -1)
        {
            if (column >= Columns) throw new ArgumentOutOfRangeException(nameof(column));
            foreach (var item in AllItems())
            {
                if (column < 0)
                {
                    for (var currentColumn = 0; currentColumn < Columns; currentColumn++)
                        if (object.Equals(item.GetMetadata(currentColumn), metadata)) return item;
                }
                else if (object.Equals(item.GetMetadata(column), metadata)) return item;
            }
            return null;
        }
        /// <summary>Finds the first visible item with exactly matching text in any column, matching Godot's <c>get_item_with_text()</c>.</summary>
        public TreeItem GetItemWithText(string text)
        {
            text = text ?? string.Empty;
            foreach (var item in Flatten())
                for (var column = 0; column < Columns; column++)
                    if (string.Equals(item.GetText(column), text, StringComparison.Ordinal)) return item;
            return null;
        }
        /// <summary>Finds the next visible item whose cell starts with <paramref name="text"/>, wrapping from the active selection as Godot's <c>search_item_text()</c> does.</summary>
        public TreeItem SearchItemText(string text, out int column, bool selectable = false)
        {
            column = -1; text = text ?? string.Empty;
            var rows = Flatten(); if (rows.Count == 0) return null;
            var selectedIndex = rows.IndexOf(_selected); var start = selectedIndex >= 0 ? (selectedIndex + 1) % rows.Count : rows.Count > 1 ? 1 : 0;
            for (var offset = 0; offset < rows.Count; offset++)
            {
                var item = rows[(start + offset) % rows.Count];
                for (var currentColumn = 0; currentColumn < Columns; currentColumn++)
                {
                    if (item.GetText(currentColumn).StartsWith(text, StringComparison.OrdinalIgnoreCase) && (!selectable || item.IsSelectable(currentColumn)))
                    {
                        column = currentColumn; return item;
                    }
                }
            }
            return null;
        }
        /// <summary>Clears the current incremental search prefix.</summary>
        public void ClearIncrementalSearch() => _incrementalSearch = string.Empty;
        /// <summary>Returns the next rendered tree item after <paramref name="item"/>.</summary>
        public TreeItem GetNextVisible(TreeItem item, bool wrap = false)
        {
            if (item == null || item.Owner != this) return null;
            var rows = Flatten(); var index = rows.IndexOf(item);
            if (index < 0) return null;
            if (index + 1 < rows.Count) return rows[index + 1];
            return wrap && rows.Count > 0 ? rows[0] : null;
        }
        internal TreeItem GetPreviousVisible(TreeItem item, bool wrap = false)
        {
            if (item == null || item.Owner != this) return null;
            var rows = Flatten(); var index = rows.IndexOf(item);
            if (index < 0) return null;
            if (index > 0) return rows[index - 1];
            return wrap && rows.Count > 0 ? rows[rows.Count - 1] : null;
        }
        internal TreeItem GetTreeOrderRelative(TreeItem item, int direction, bool wrap = false)
        {
            if (item == null || item.Owner != this || direction == 0) return null;
            var items = new List<TreeItem>(AllItems()); var index = items.IndexOf(item);
            if (index < 0) return null;
            var target = index + Math.Sign(direction);
            if (target >= 0 && target < items.Count) return items[target];
            return wrap && items.Count > 0 ? items[direction > 0 ? 0 : items.Count - 1] : null;
        }
        public void Select(TreeItem item, int column = 0, bool emit = true)
        {
            ValidateColumn(column);
            if (item != null && (item.Owner != this || !item.IsSelectable(column))) return;
            if (_selected == item && _selectedColumn == column && !AllowReselect) return;
            foreach (var row in AllItems()) row.IsSelected = false;
            _selected = item; _selectedColumn = item == null ? 0 : column;
            if (item != null)
            {
                if (SelectMode == TreeSelectMode.Row) item.IsSelected = true;
                else item.SetSelectedForTree(column, true);
            }
            if (emit && item != null) { ItemSelected?.Invoke(this, item); CellSelected?.Invoke(this, item, column); }
        }
        internal void SelectItemCell(TreeItem item, int column, bool setAsCursor)
        {
            if (item.Owner != this || !item.IsSelectable(column)) return;
            if (SelectMode != TreeSelectMode.Multi) { Select(item, column, emit: false); return; }
            item.SetSelectedForTree(column, true);
            if (setAsCursor) SetItemCursor(item, column);
        }
        internal void DeselectItemCell(TreeItem item, int column)
        {
            if (item.Owner != this) return;
            if (SelectMode == TreeSelectMode.Row) item.IsSelected = false;
            else item.SetSelectedForTree(column, false);
            if (_selected == item && _selectedColumn == column && SelectMode != TreeSelectMode.Multi) { _selected = null; _selectedColumn = 0; }
        }
        internal void SetItemCursor(TreeItem item, int column)
        {
            if (item.Owner != this || SelectMode != TreeSelectMode.Multi) return;
            _selected = item; _selectedColumn = column;
        }
        public void SetColumnTitle(int column, string title) { ValidateColumn(column); _columns[column].Title = title ?? string.Empty; }
        public string GetColumnTitle(int column) { ValidateColumn(column); return _columns[column].Title; }
        public void SetColumnTitleTooltipText(int column, string tooltip) { ValidateColumn(column); _columns[column].TitleTooltip = tooltip ?? string.Empty; }
        public string GetColumnTitleTooltipText(int column) { ValidateColumn(column); return _columns[column].TitleTooltip; }
        public void SetColumnTitleAlignment(int column, HorizontalAlignment alignment) { ValidateColumn(column); _columns[column].TitleAlignment = alignment; }
        public HorizontalAlignment GetColumnTitleAlignment(int column) { ValidateColumn(column); return _columns[column].TitleAlignment; }
        /// <summary>Sets the shaping direction for a column title, matching Godot's <c>set_column_title_direction()</c>.</summary>
        /// <remarks>Direction is retained for the text renderer. The default SpriteFont renderer does not yet provide Godot's Unicode/bidi shaping behavior.</remarks>
        public void SetColumnTitleDirection(int column, TextDirection direction) { ValidateColumn(column); _columns[column].TitleDirection = direction; }
        public TextDirection GetColumnTitleDirection(int column) { ValidateColumn(column); return _columns[column].TitleDirection; }
        /// <summary>Sets the BCP-47 language hint for a column title, matching Godot's <c>set_column_title_language()</c>.</summary>
        /// <remarks>The hint is retained for shaped-text capable renderers; the default SpriteFont renderer does not yet use language-specific shaping.</remarks>
        public void SetColumnTitleLanguage(int column, string language) { ValidateColumn(column); _columns[column].TitleLanguage = language ?? string.Empty; }
        public string GetColumnTitleLanguage(int column) { ValidateColumn(column); return _columns[column].TitleLanguage; }
        public void SetColumnTitlesVisible(bool visible) { if (ColumnTitlesVisible == visible) return; ColumnTitlesVisible = visible; QueueLayout(); }
        public bool AreColumnTitlesVisible() => ColumnTitlesVisible;
        public void SetHideRoot(bool hidden) { if (HideRoot == hidden) return; HideRoot = hidden; QueueLayout(); }
        public bool IsRootHidden() => HideRoot;
        public void SetAutoTooltip(bool enabled) => AutoTooltipEnabled = enabled;
        public bool IsAutoTooltipEnabled() => AutoTooltipEnabled;
        public void SetEditCheckboxCellOnlyWhenCheckboxPressed(bool enabled) => EditCheckboxCellOnlyWhenCheckboxPressed = enabled;
        public bool IsEditCheckboxCellOnlyWhenCheckboxPressed() => EditCheckboxCellOnlyWhenCheckboxPressed;
        public void SetHideFolding(bool hidden) => FoldingHidden = hidden;
        public bool IsFoldingHidden() => FoldingHidden;
        public void SetEnableRecursiveFolding(bool enabled) => RecursiveFoldingEnabled = enabled;
        public bool IsRecursiveFoldingEnabled() => RecursiveFoldingEnabled;
        public void SetEnableDragUnfolding(bool enabled) { DragUnfoldingEnabled = enabled; if (!enabled) ResetDragUnfoldTarget(); }
        public bool IsDragUnfoldingEnabled() => DragUnfoldingEnabled;
        /// <summary>Returns the retained scroll position, matching Godot's <c>get_scroll()</c>.</summary>
        public Vector2 GetScroll() => new Vector2(GetHorizontalScrollOffset(), GetVerticalScrollOffset());
        /// <summary>Scrolls as little as necessary to reveal an item, matching Godot's <c>scroll_to_item()</c>.</summary>
        public void ScrollToItem(TreeItem item, bool centerOnItem = false)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (item.Owner != this) throw new ArgumentException("The item belongs to another Tree.", nameof(item));
            if (!VerticalScrollEnabled) return;
            var rows = Flatten(); var rowIndex = rows.IndexOf(item); if (rowIndex < 0) return;
            var itemTop = GetUnscrolledRowTop(rows, rowIndex) - RowOriginY;
            var itemHeight = GetRowHeight(item); var viewportHeight = GetViewportHeight();
            if (viewportHeight <= 0) return;
            var current = GetVerticalScrollOffset();
            if (centerOnItem) SetVerticalScrollOffset(itemTop - Math.Max(0, (viewportHeight - itemHeight) / 2));
            else if (itemHeight > viewportHeight) SetVerticalScrollOffset(itemTop);
            else if (itemTop + itemHeight > current + viewportHeight) SetVerticalScrollOffset(itemTop - viewportHeight + itemHeight);
            else if (itemTop < current) SetVerticalScrollOffset(itemTop);
            EnsureColumnVisible(item, _selected == item ? _selectedColumn : 0);
        }
        /// <summary>Reveals the active selection cursor, matching Godot's <c>ensure_cursor_is_visible()</c>.</summary>
        public void EnsureCursorIsVisible() { if (_selected != null) ScrollToItem(_selected); }
        private void EnsureColumnVisible(TreeItem item, int column)
        {
            if (!HorizontalScrollEnabled || item == null || item.Owner != this || column < 0 || column >= Columns) return;
            var widths = GetColumnWidths(); var left = 0;
            for (var index = 0; index < column; index++) left += widths[index];
            var width = widths[column]; var viewport = ContentWidth; var current = GetHorizontalScrollOffset();
            if (width > viewport) SetHorizontalScrollOffset(left);
            else if (left + width > current + viewport) SetHorizontalScrollOffset(left - viewport + width);
            else if (left < current) SetHorizontalScrollOffset(left);
        }
        /// <summary>Returns an item's unscrolled top offset inside the retained Tree, or zero when it is not reachable in the expanded hierarchy.</summary>
        public int GetItemOffset(TreeItem item)
        {
            if (item == null || item.Owner != this || GetRoot() == null) return 0;
            var offset = RowOriginY - Bounds.Y;
            foreach (var current in EnumerateExpandedItems())
            {
                if (current == item) return offset;
                if ((current.Parent != null || !HideRoot) && current.IsVisibleInTree()) offset += GetRowHeight(current);
            }
            return 0;
        }
        public void SetHorizontalScrollEnabled(bool enabled)
        {
            HorizontalScrollEnabled = enabled;
            if (!enabled) _horizontalScrollOffset = 0;
            else _horizontalScrollOffset = ClampHorizontalScrollOffset(_horizontalScrollOffset);
            SynchronizeScrollBars();
            QueueLayout();
        }
        public bool IsHorizontalScrollEnabled() => HorizontalScrollEnabled;
        public void SetVerticalScrollEnabled(bool enabled)
        {
            VerticalScrollEnabled = enabled;
            if (!enabled) _verticalScrollOffset = 0;
            else _verticalScrollOffset = ClampVerticalScrollOffset(_verticalScrollOffset);
            SynchronizeScrollBars();
            QueueLayout();
        }
        public bool IsVerticalScrollEnabled() => VerticalScrollEnabled;
        /// <summary>Returns the retained horizontal scrollbar, matching Godot's <c>get_h_scroll_bar()</c>.</summary>
        public HScrollBar GetHScrollBar() => _horizontalScrollBar;
        /// <summary>Returns the retained vertical scrollbar, matching Godot's <c>get_v_scroll_bar()</c>.</summary>
        public VScrollBar GetVScrollBar() => _verticalScrollBar;
        public bool IsTouchDragging => _dragTouching;
        public bool IsTouchDragDecelerating => _dragTouchingDeaccel;
        public float TouchDragSpeed => _dragSpeed;
        /// <summary>Begins a retained vertical touch drag, mirroring Godot's touchscreen mouse-press handling in Tree::gui_input.</summary>
        public void BeginTouchDragScroll()
        {
            _dragSpeed = 0;
            _dragAccum = 0;
            _dragFrom = GetVerticalScrollOffset();
            _dragTouching = true;
            _dragTouchingDeaccel = false;
        }
        /// <summary>Applies relative vertical touch motion and its instantaneous velocity, mirroring Godot's gui_input drag accumulation.</summary>
        public void TouchDragScrollBy(float relativeMotion, float velocity)
        {
            if (!_dragTouching || _dragTouchingDeaccel) return;
            _dragAccum -= relativeMotion;
            SetVerticalScrollOffset((int)MathF.Round(_dragFrom + _dragAccum));
            _dragSpeed = -velocity;
        }
        /// <summary>Ends a retained touch drag, entering inertial deceleration when speed is nonzero like Godot's release handling.</summary>
        public void EndTouchDragScroll()
        {
            if (!_dragTouching) return;
            if (_dragSpeed == 0)
            {
                _dragTouchingDeaccel = false;
                _dragTouching = false;
            }
            else
            {
                _dragTouchingDeaccel = true;
            }
        }
        /// <summary>Cancels an in-progress touch drag immediately, mirroring Godot's Tree release/cleanup paths.</summary>
        public void CancelTouchDragScroll()
        {
            _dragTouching = false;
            _dragTouchingDeaccel = false;
            _dragSpeed = 0;
            _dragFrom = 0;
        }
        private void ProcessTouchDrag(GameTime gameTime)
        {
            if (!_dragTouching || !_dragTouchingDeaccel) return;
            var delta = (float)(gameTime?.ElapsedGameTime.TotalSeconds ?? 0);
            if (delta <= 0) return;
            var pos = GetVerticalScrollOffset() + _dragSpeed * delta;
            var turnoff = false;
            if (pos < 0) { pos = 0; turnoff = true; _dragTouching = false; _dragTouchingDeaccel = false; }
            var max = GetVerticalScrollMaximum();
            if (pos > max) { pos = max; turnoff = true; }
            SetVerticalScrollOffset((int)MathF.Round(pos));
            var sign = _dragSpeed < 0 ? -1 : 1;
            var value = MathF.Abs(_dragSpeed) - DragDeceleration * delta;
            if (value < 0) turnoff = true;
            _dragSpeed = sign * value;
            if (turnoff) { _dragTouching = false; _dragTouchingDeaccel = false; }
        }
        /// <summary>Returns the retained option menu used by editable labeled range cells.</summary>
        /// <remarks>Godot owns this popup internally; exposing it here permits host styling and deterministic inspection.</remarks>
        public PopupMenu GetRangePopup() => _rangePopup;
        /// <summary>Returns the retained inline editor used by editable numeric range cells.</summary>
        public PopupPanel GetRangeEditorPopup() => _rangeEditorPopup;
        /// <summary>Returns the numeric text entry owned by <see cref="GetRangeEditorPopup"/>.</summary>
        public LineEdit GetRangeEditorLineEdit() => _rangeEditorText;
        /// <summary>Returns the slider synchronized with <see cref="GetRangeEditorLineEdit"/>.</summary>
        public HSlider GetRangeEditorSlider() => _rangeEditorSlider;
        /// <summary>Returns the popup that hosts editable string Tree cells.</summary>
        public PopupPanel GetStringEditorPopup() => _stringEditorPopup;
        /// <summary>Returns the single-line editor used for a Tree string cell without <c>edit_multiline</c>.</summary>
        public LineEdit GetStringEditorLineEdit() => _stringEditorLine;
        /// <summary>Returns the multiline editor used for a Tree string cell with <c>edit_multiline</c>.</summary>
        public TextEdit GetStringEditorTextEdit() => _stringEditorText;
        public void SetScrollHintMode(TreeScrollHintMode mode) { ScrollHintMode = mode; QueueLayout(); }
        public TreeScrollHintMode GetScrollHintMode() => ScrollHintMode;
        public void SetTileScrollHint(bool enabled) { TileScrollHint = enabled; QueueLayout(); }
        public bool IsScrollHintTiled() => TileScrollHint;
        public void SetColumnCustomMinimumWidth(int column, int minimumWidth) { ValidateColumn(column); _columns[column].CustomMinimumWidth = Math.Max(0, minimumWidth); QueueLayout(); }
        public int GetColumnMinimumWidth(int column) { ValidateColumn(column); return _columns[column].CustomMinimumWidth; }
        public void SetColumnExpand(int column, bool expand) { ValidateColumn(column); _columns[column].Expand = expand; QueueLayout(); }
        public bool IsColumnExpanding(int column) { ValidateColumn(column); return _columns[column].Expand; }
        public void SetColumnExpandRatio(int column, int ratio) { ValidateColumn(column); _columns[column].ExpandRatio = Math.Max(1, ratio); QueueLayout(); }
        public int GetColumnExpandRatio(int column) { ValidateColumn(column); return _columns[column].ExpandRatio; }
        /// <summary>Clips icons, text, buttons, and custom drawing to a column's cell bounds, matching Godot's <c>set_column_clip_content()</c>.</summary>
        public void SetColumnClipContent(int column, bool clip) { ValidateColumn(column); _columns[column].ClipContent = clip; }
        public bool IsColumnClippingContent(int column) { ValidateColumn(column); return _columns[column].ClipContent; }
        /// <summary>Returns the current rendered width of a column, matching Godot's <c>get_column_width()</c>.</summary>
        public int GetColumnWidth(int column) { ValidateColumn(column); return GetColumnWidths()[column]; }
        internal void RemoveItem(TreeItem item)
        {
            if (item == null || item.Owner != this) return;
            if (_stringEditorItem != null && IsInSubtree(_stringEditorItem, item)) _stringEditorPopup.Hide(PopupHideReason.Cancelled);
            if (item.Parent != null) item.Parent.RemoveChild(item); else _roots.Remove(item);
            if (_selected == item) { _selected = null; _selectedColumn = 0; } QueueLayout();
        }
        internal void ClearItemChildren(TreeItem item)
        {
            if (item == null || item.Owner != this || item.Children.Count == 0) return;
            if (_selected != null && _selected != item && IsInSubtree(_selected, item)) { _selected = null; _selectedColumn = 0; }
            if (_editedItem != null && _editedItem != item && IsInSubtree(_editedItem, item)) { _editedItem = null; _editedColumn = -1; _customPopupRect = Rectangle.Empty; }
            if (_stringEditorItem != null && IsInSubtree(_stringEditorItem, item)) _stringEditorPopup.Hide(PopupHideReason.Cancelled);
            item.ClearChildrenForTree();
            QueueLayout();
        }
        internal TreeItem GetSibling(TreeItem item, int offset)
        {
            if (item == null || item.Owner != this || offset == 0) return null;
            var index = GetSiblingIndex(item) + offset;
            if (item.Parent == null) return index >= 0 && index < _roots.Count ? _roots[index] : null;
            return index >= 0 && index < item.Parent.Children.Count ? item.Parent.Children[index] : null;
        }
        internal int GetSiblingIndex(TreeItem item)
        {
            if (item == null || item.Owner != this) return -1;
            return item.Parent == null ? _roots.IndexOf(item) : item.Parent.GetChildIndex(item);
        }
        internal void MoveItemRelative(TreeItem item, TreeItem target, bool after)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (item.Owner != this || target.Owner != this) throw new InvalidOperationException("Tree items must belong to the same Tree.");
            if (item == target) return;
            for (var ancestor = target; ancestor != null; ancestor = ancestor.Parent)
                if (ancestor == item) throw new InvalidOperationException("A TreeItem cannot be moved relative to one of its descendants.");

            if (item.Parent == null) _roots.Remove(item); else item.Parent.RemoveChild(item);
            var destinationParent = target.Parent;
            var targetIndex = destinationParent == null ? _roots.IndexOf(target) : destinationParent.GetChildIndex(target);
            if (destinationParent == null) _roots.Insert(targetIndex + (after ? 1 : 0), item);
            else destinationParent.InsertChild(targetIndex + (after ? 1 : 0), item);
            item.SetParentForTree(destinationParent);
            QueueLayout();
        }
        public override object GetDragData(Point position)
        {
            var itemAtPosition = GetItemAtPosition(position); var columnAtPosition = GetColumnAtPosition(position);
            if (DragDataProvider != null) return DragDataProvider(this, itemAtPosition, columnAtPosition, position);
            if (!SelfDragDropEnabled || _selected == null || GetItemAtPosition(position) != _selected) return null;
            return _selected;
        }
        public override bool CanDropData(Point position, object data)
        {
            var itemAtPosition = GetItemAtPosition(position); var columnAtPosition = GetColumnAtPosition(position);
            if (CanDropDataProvider != null) return CanDropDataProvider(this, itemAtPosition, columnAtPosition, position, data);
            if (!SelfDragDropEnabled || data is not TreeItem item || item.Owner != this) return false;
            var target = itemAtPosition;
            var section = GetDropSectionAtPosition(position);
            return CanReparentItemAtSection(item, target, section);
        }
        public override void DropData(Point position, object data)
        {
            var itemAtPosition = GetItemAtPosition(position); var columnAtPosition = GetColumnAtPosition(position);
            if (DropDataHandler != null) { DropDataHandler(this, itemAtPosition, columnAtPosition, position, data); return; }
            if (data is not TreeItem item || item.Owner != this) return;
            var target = itemAtPosition;
            var section = GetDropSectionAtPosition(position);
            if (!CanReparentItemAtSection(item, target, section)) return;
            ReparentItemAtSection(item, target, section);
            ItemDropped?.Invoke(this, item, target, section);
        }
        private bool CanReparentItemAtSection(TreeItem item, TreeItem target, int section)
        {
            if (item == null || target == null || item == target || section == DropSectionNotFound) return false;
            TreeItem destinationParent;
            switch (section)
            {
                case 0: case 2: destinationParent = target; break;
                case -1: case 1: destinationParent = target.Parent; break;
                default: return false;
            }
            if (destinationParent != null && !destinationParent.IsAcceptingChildren()) return false;
            for (var ancestor = destinationParent; ancestor != null; ancestor = ancestor.Parent)
                if (ancestor == item) return false;
            return true;
        }
        private void ReparentItemAtSection(TreeItem item, TreeItem target, int section)
        {
            TreeItem destinationParent;
            var destinationIndex = 0;
            if (section == 0 || section == 2)
            {
                destinationParent = target;
                destinationIndex = target.GetChildCount();
            }
            else
            {
                destinationParent = target.Parent;
                destinationIndex = destinationParent == null ? _roots.IndexOf(target) : destinationParent.GetChildIndex(target);
                if (section == 1) destinationIndex++;
            }
            if (item.Parent == destinationParent)
            {
                var sourceIndex = destinationParent == null ? _roots.IndexOf(item) : destinationParent.GetChildIndex(item);
                if (sourceIndex >= 0 && sourceIndex < destinationIndex) destinationIndex--;
            }
            if (item.Parent == null) _roots.Remove(item); else item.Parent.RemoveChild(item);
            if (destinationParent == null) _roots.Insert(destinationIndex, item);
            else destinationParent.InsertChild(destinationIndex, item);
            item.SetParentForTree(destinationParent);
            QueueLayout();
        }
        internal void NotifyItemCollapsed(TreeItem item) { QueueLayout(); ItemCollapsed?.Invoke(this, item); }
        internal void NotifyItemFoldingPolicyChanged(TreeItem item) { QueueLayout(); }
        internal void NotifyItemMinimumHeightChanged(TreeItem item) { QueueLayout(); }
        internal void NotifyItemFontChanged(TreeItem item) { QueueLayout(); }
        internal void NotifyItemPresentationChanged(TreeItem item) { QueueLayout(); }
        internal void NotifyCheckPropagated(TreeItem item, int column) => CheckPropagatedToItem?.Invoke(this, item, column);
        internal void NotifyItemVisibilityChanged(TreeItem item)
        {
            if (_selected != null && !_selected.IsVisibleInTree()) { _selected.IsSelected = false; _selected = null; _selectedColumn = 0; }
            QueueLayout();
        }
        protected internal override void PointerPressed(Point point)
        {
            base.PointerPressed(point);
            if (ColumnTitlesVisible && point.Y < RowOriginY)
            {
                if (TryGetColumnResizeHandle(point, out var resizeColumn))
                {
                    _resizingColumn = resizeColumn;
                    _resizeStartX = point.X;
                    _resizeStartWidth = GetColumnWidth(resizeColumn);
                    return;
                }
                return;
            }
            var rows = Flatten(); var index = GetRowIndexAtPosition(rows, point.Y);
            if (index < 0 || index >= rows.Count) return;
            var item = rows[index]; var indentX = ContentLeft - GetHorizontalScrollOffset() + item.Depth * Indent;
            if (!FoldingHidden && !item.IsFoldingDisabled() && item.GetVisibleChildCount() > 0 && point.X < indentX + Indent)
            {
                var keyboard = Context?.CurrentKeyboardState ?? default;
                var shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
                if (RecursiveFoldingEnabled && shift) item.SetCollapsedRecursive(!item.Collapsed);
                else item.SetCollapsed(!item.Collapsed);
            }
            else
            {
                var column = GetColumnAtX(point.X);
                if (column < 0) return;
                var buttonIndex = GetButtonAtPosition(item, column, index, point, rows);
                if (buttonIndex >= 0)
                {
                    var button = item.GetButtonModelForTree(column, buttonIndex);
                    if (!button.Disabled) { _pressedButtonItem = item; _pressedButtonColumn = column; _pressedButtonIndex = buttonIndex; }
                    return;
                }
                if (item.GetCellMode(column) == TreeCellMode.Custom && item.IsEditable(column))
                {
                    var cell = GetCellRectangle(item, column, index, rows);
                    // Godot reports whether the pointer lies in the right-side arrow affordance even
                    // for a custom cell that is not configured as a button. A custom button only uses
                    // that affordance to decide whether to request the application popup.
                    var popupArrowPressed = point.X >= cell.Right - Math.Min(GetRowHeight(item), cell.Width) / 2;
                    var customButton = item.IsCustomSetAsButton(column);
                    BeginCellEdit(item, column, !customButton || popupArrowPressed, !customButton || !popupArrowPressed, popupArrowPressed);
                    if (popupArrowPressed) return;
                }
                if (item.GetCellMode(column) == TreeCellMode.Check && item.IsEditable(column))
                {
                    if (!EditCheckboxCellOnlyWhenCheckboxPressed || GetCheckBoxRectangle(item, column, index, rows).Contains(point))
                    {
                        item.SetChecked(column, !item.IsChecked(column)); ItemEdited?.Invoke(this, item, column);
                    }
                }
                else if (item.GetCellMode(column) == TreeCellMode.Range && item.IsEditable(column))
                {
                    var cell = GetCellRectangle(item, column, index, rows);
                    if (!string.IsNullOrEmpty(item.GetText(column))) ShowRangePopup(item, column, cell);
                    else if (GetRangeSpinnerRectangle(cell).Contains(point)) { AdjustRangeSpinner(item, column, point, cell, setToEndpoint: false); BeginRangeStepRepeat(item, column, point, cell); }
                    else BeginRangeDrag(item, column, point);
                }
                else if (item.GetCellMode(column) == TreeCellMode.String && item.IsEditable(column))
                {
                    _stringEditPressItem = item;
                    _stringEditPressColumn = column;
                }
                SelectFromPointer(item, column, rows);
            }
        }
        internal override void PointerButtonReleased(Point point, PointerButton button)
        {
            if ((button != PointerButton.Left && button != PointerButton.Right) || !ColumnTitlesVisible || point.Y < Bounds.Y + 1 || point.Y >= RowOriginY) return;
            var column = GetColumnAtX(point.X);
            if (column >= 0) ColumnTitleClicked?.Invoke(this, column, button);
        }
        internal override void PointerButtonPressed(Point point, PointerButton button)
        {
            base.PointerButtonPressed(point, button);
            if (button == PointerButton.Right && TryGetRangeCellAtPosition(point, out var rangeItem, out var rangeColumn, out var rangeCell) && rangeItem.IsEditable(rangeColumn) && string.IsNullOrEmpty(rangeItem.GetText(rangeColumn)) && GetRangeSpinnerRectangle(rangeCell).Contains(point))
            {
                AdjustRangeSpinner(rangeItem, rangeColumn, point, rangeCell, setToEndpoint: true);
                return;
            }
            if (button != PointerButton.Right || !AllowRightMouseSelect || ColumnTitlesVisible && point.Y < RowOriginY) return;
            base.PointerPressed(point);
            var rows = Flatten(); var index = GetRowIndexAtPosition(rows, point.Y);
            if (index < 0 || index >= rows.Count) return;
            var item = rows[index]; var column = GetColumnAtX(point.X);
            if (column < 0) return;
            if (!item.IsSelectable(column)) return;

            var keyboard = Context?.CurrentKeyboardState ?? default;
            var command = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl) || keyboard.IsKeyDown(Keys.LeftWindows) || keyboard.IsKeyDown(Keys.RightWindows);
            var shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
            if (SelectMode == TreeSelectMode.Multi && command)
            {
                if (!item.IsCellSelected(column)) { item.SetSelectedForTree(column, true); SetItemCursor(item, column); MultiSelected?.Invoke(this, item, column, true); }
            }
            else if (SelectMode == TreeSelectMode.Multi && shift && _selected != null && _selected != item)
            {
                _shiftSelectionAnchor ??= _selected;
                SelectVisibleRange(_shiftSelectionAnchor, item, column);
            }
            else if (SelectMode != TreeSelectMode.Multi || !item.IsCellSelected(column))
            {
                Select(item, column);
            }
            ItemMouseSelected?.Invoke(this, point, button);
        }
        protected internal override void PointerMoved(Point point)
        {
            _dragPointer = point;
            if (_dragAutoScrolling) UpdateDragUnfoldTarget(point);
            if (_rangeDragItem != null)
            {
                if (!_rangeDragging)
                {
                    var dx = point.X - _rangeDragStart.X; var dy = point.Y - _rangeDragStart.Y;
                    if (dx * dx + dy * dy > 4) { _rangeDragging = true; _rangeDragPrevious = point; }
                    return;
                }
                var deltaY = _rangeDragPrevious.Y - point.Y;
                _rangeDragPrevious = point;
                if (deltaY != 0)
                {
                    var scaled = MathF.Pow(MathF.Abs(deltaY), 1.8f) * MathF.Sign(deltaY) * .1f;
                    _rangeDragItem.GetRangeConfig(_rangeDragColumn, out _, out _, out var step);
                    _rangeDragItem.SetRange(_rangeDragColumn, _rangeDragItem.GetRange(_rangeDragColumn) + step * scaled);
                    ItemEdited?.Invoke(this, _rangeDragItem, _rangeDragColumn);
                }
                return;
            }
            if (_resizingColumn < 0) return;
            var width = Math.Max(0, _resizeStartWidth + point.X - _resizeStartX);
            var column = _columns[_resizingColumn];
            column.Expand = false;
            if (column.CustomMinimumWidth == width) return;
            column.CustomMinimumWidth = width;
            QueueLayout();
            ColumnResized?.Invoke(this, _resizingColumn, width);
        }
        protected internal override void PointerReleased(Point point, bool isInside)
        {
            ResetRangeStepRepeat();
            if (_stringEditPressItem != null)
            {
                var item = _stringEditPressItem; var column = _stringEditPressColumn;
                _stringEditPressItem = null; _stringEditPressColumn = -1;
                if (isInside && GetItemAtPosition(point) == item && GetColumnAtPosition(point) == column)
                {
                    ShowStringEditor(item, column, GetItemAreaRectangle(item, column));
                    return;
                }
            }
            if (_rangeDragItem != null)
            {
                var item = _rangeDragItem; var column = _rangeDragColumn;
                var dragged = _rangeDragging;
                ResetRangeDrag();
                if (dragged) return;
                if (isInside && GetItemAtPosition(point) == item && GetColumnAtPosition(point) == column)
                {
                    ShowRangeEditor(item, column, GetItemAreaRectangle(item, column));
                    return;
                }
            }
            if (_resizingColumn >= 0)
            {
                _resizingColumn = -1;
                return;
            }
            if (_pressedButtonItem != null)
            {
                var item = _pressedButtonItem; var column = _pressedButtonColumn; var buttonIndex = _pressedButtonIndex;
                _pressedButtonItem = null; _pressedButtonColumn = -1; _pressedButtonIndex = -1;
                var pressedRows = Flatten();
                if (isInside && GetButtonAtPosition(item, column, pressedRows.IndexOf(item), point, pressedRows) == buttonIndex)
                    ButtonClicked?.Invoke(this, item, column, item.GetButtonId(column, buttonIndex));
                return;
            }
            if (!isInside || _selected == null) return;
            var rows = Flatten(); var index = GetRowIndexAtPosition(rows, point.Y);
            if (index >= 0 && index < rows.Count && rows[index] == _selected) ItemActivated?.Invoke(this, _selected);
        }
        internal override void CancelInput()
        {
            _resizingColumn = -1;
            _pressedButtonItem = null;
            _pressedButtonColumn = _pressedButtonIndex = -1;
            _stringEditPressItem = null;
            _stringEditPressColumn = -1;
            ResetRangeDrag();
            ResetRangeStepRepeat();
            CancelTouchDragScroll();
            _dragAutoScrolling = false;
            ResetDragUnfoldTarget();
            base.CancelInput();
        }
        public override string GetTooltip(Point position)
        {
            if (!Bounds.Contains(position) || position.X < ContentLeft || position.X >= ContentLeft + ContentWidth) return base.GetTooltip(position);
            if (ColumnTitlesVisible && position.Y < RowOriginY)
            {
                var titleColumn = GetColumnAtX(position.X);
                if (titleColumn < 0) return base.GetTooltip(position);
                var titleTooltip = GetColumnTitleTooltipText(titleColumn);
                return string.IsNullOrEmpty(titleTooltip) ? base.GetTooltip(position) : titleTooltip;
            }
            var rows = Flatten(); var index = GetRowIndexAtPosition(rows, position.Y);
            if (index < 0 || index >= rows.Count) return base.GetTooltip(position);
            var column = GetColumnAtX(position.X); if (column < 0) return base.GetTooltip(position);
            var item = rows[index]; var buttonIndex = GetButtonAtPosition(item, column, index, position, rows);
            var tooltip = buttonIndex >= 0 ? item.GetButtonTooltipText(column, buttonIndex) : item.GetTooltipText(column);
            if (buttonIndex < 0 && AutoTooltipEnabled && string.IsNullOrEmpty(tooltip)) tooltip = item.GetText(column);
            return string.IsNullOrEmpty(tooltip) ? base.GetTooltip(position) : tooltip;
        }
        internal override void KeyPressed(Keys key)
        {
            var rows = Flatten(); if (rows.Count == 0) return; var index = Math.Max(0, rows.IndexOf(_selected));
            var keyboard = Context?.CurrentKeyboardState ?? default;
            var shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
            if (SelectMode == TreeSelectMode.Multi && shift && _selected != null && (key == Keys.Down || key == Keys.Up))
            {
                var target = rows[key == Keys.Down ? Math.Min(rows.Count - 1, index + 1) : Math.Max(0, index - 1)];
                if (target != _selected)
                {
                    _shiftSelectionAnchor ??= _selected;
                    SelectVisibleRange(_shiftSelectionAnchor, target, _selectedColumn);
                }
            }
            else if (key == Keys.Down) { _shiftSelectionAnchor = null; Select(rows[Math.Min(rows.Count - 1, index + 1)], _selectedColumn); }
            else if (key == Keys.Up) { _shiftSelectionAnchor = null; Select(rows[Math.Max(0, index - 1)], _selectedColumn); }
            else if (key == Keys.Right && _selected != null && _selected.Children.Count > 0) _selected.SetCollapsed(false);
            else if (key == Keys.Left && _selected != null) { if (!_selected.Collapsed && _selected.Children.Count > 0) _selected.SetCollapsed(true); else if (_selected.Parent != null) Select(_selected.Parent, _selectedColumn); }
            else if (key == Keys.Enter && _selected != null) { if (!EditSelected()) ItemActivated?.Invoke(this, _selected); }
            if (_selected != null && (key == Keys.Down || key == Keys.Up || key == Keys.Left || key == Keys.Right)) EnsureCursorIsVisible();
        }
        private void SelectVisibleRange(TreeItem anchor, TreeItem target, int column)
        {
            var rows = Flatten(); var start = rows.IndexOf(anchor); var end = rows.IndexOf(target);
            if (start < 0 || end < 0) return;
            var first = Math.Min(start, end); var last = Math.Max(start, end);
            foreach (var row in AllItems())
            {
                var selected = row.IsCellSelected(column);
                var visibleIndex = rows.IndexOf(row);
                var inRange = visibleIndex >= first && visibleIndex <= last && row.IsSelectable(column);
                if (selected == inRange) continue;
                row.SetSelectedForTree(column, inRange);
                MultiSelected?.Invoke(this, row, column, inRange);
            }
            _selected = target; _selectedColumn = column;
            CellSelected?.Invoke(this, target, column);
        }
        private void SelectFromPointer(TreeItem item, int column, List<TreeItem> rows)
        {
            var keyboard = Context?.CurrentKeyboardState ?? default;
            var command = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl) || keyboard.IsKeyDown(Keys.LeftWindows) || keyboard.IsKeyDown(Keys.RightWindows);
            var shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
            if (SelectMode != TreeSelectMode.Multi) { Select(item, column); return; }
            if (command)
            {
                if (item.IsCellSelected(column)) { item.SetSelectedForTree(column, false); _selected = item; _selectedColumn = column; MultiSelected?.Invoke(this, item, column, false); }
                else { item.SetSelectedForTree(column, true); SetItemCursor(item, column); MultiSelected?.Invoke(this, item, column, true); }
                return;
            }
            if (shift && _selected != null && _selected != item)
            {
                _shiftSelectionAnchor ??= _selected;
                SelectVisibleRange(_shiftSelectionAnchor, item, column);
                return;
            }
            _shiftSelectionAnchor = null;
            Select(item, column);
        }
        internal override void TextInput(char character)
        {
            if (!AllowSearch || char.IsControl(character)) return;
            var input = character.ToString();
            if (_lastTextInputTime == TimeSpan.MinValue || _processTime - _lastTextInputTime > IncrementalSearchTimeout) _incrementalSearch = input;
            else if (!string.Equals(_incrementalSearch, input, StringComparison.OrdinalIgnoreCase)) _incrementalSearch += input;
            _lastTextInputTime = _processTime;
            var item = SearchItemText(_incrementalSearch, out var column, selectable: true);
            if (item != null) { Select(item, column); EnsureCursorIsVisible(); }
        }
        internal override void Process(GameTime gameTime)
        {
            _processTime = gameTime.TotalGameTime;
            var keyboard = Context?.CurrentKeyboardState ?? default;
            if (!keyboard.IsKeyDown(Keys.LeftShift) && !keyboard.IsKeyDown(Keys.RightShift)) _shiftSelectionAnchor = null;
            UpdateRangeStepRepeat(gameTime?.ElapsedGameTime ?? TimeSpan.Zero);
            UpdateDragAutoScroll(gameTime?.ElapsedGameTime ?? TimeSpan.Zero);
            UpdateDragUnfold(gameTime?.ElapsedGameTime ?? TimeSpan.Zero);
            ProcessTouchDrag(gameTime);
            base.Process(gameTime);
        }
        protected override void ArrangeChildren()
        {
            base.ArrangeChildren();
            SynchronizeScrollBars();
        }
        internal void DrawTreeContent(UIRenderContext context)
        {
            SynchronizeScrollBars();
            context.Fill(Bounds, context.Theme.BackgroundColor); context.Border(Bounds, context.Theme.PanelBorderColor);
            var widths = GetColumnWidths();
            if (ColumnTitlesVisible)
            {
                var columnX = ContentLeft - GetHorizontalScrollOffset();
                context.PushClip(new Rectangle(ContentLeft, Bounds.Y + 1, ContentWidth, Math.Max(0, (int)ItemHeight)));
                for (var column = 0; column < Columns; column++)
                {
                    var header = new Rectangle(columnX, Bounds.Y + 1, widths[column], (int)ItemHeight);
                    context.Fill(header, context.Theme.PanelBorderColor);
                    if (EffectiveUIFont != null)
                    {
                        var title = _columns[column].Title; var width = (int)MathF.Ceiling(TextMetrics.Measure(EffectiveUIFont, title).X); var titleX = header.X + 4;
                        if (_columns[column].TitleAlignment == HorizontalAlignment.Center) titleX = header.X + (header.Width - width) / 2;
                        else if (_columns[column].TitleAlignment == HorizontalAlignment.Right) titleX = header.Right - 4 - width;
                        context.Text(EffectiveUIFont, title, new Vector2(titleX, header.Y + Math.Max(2, (ItemHeight - TextMetrics.LineHeight(EffectiveUIFont)) / 2)), context.Theme.TextColor);
                    }
                    columnX += widths[column];
                }
                context.PopClip();
            }
            var rows = Flatten();
            var rowY = RowOriginY - GetVerticalScrollOffset();
            context.PushClip(new Rectangle(ContentLeft, RowOriginY, ContentWidth, GetViewportHeight()));
            try
            {
                for (var index = 0; index < rows.Count; index++)
                {
                    var item = rows[index]; var rect = new Rectangle(ContentLeft, rowY, ContentWidth, GetRowHeight(item));
                    if (SelectMode == TreeSelectMode.Row && item.IsSelected) context.Fill(rect, context.Theme.AccentColor);
                    var indentX = rect.X - GetHorizontalScrollOffset() + (int)(item.Depth * Indent);
                    if (!FoldingHidden && !item.IsFoldingDisabled() && item.GetVisibleChildCount() > 0)
                    {
                        var arrowName = item.Collapsed ? IsLayoutRtl() ? "arrow_collapsed_mirrored" : "arrow_collapsed" : "arrow";
                        var arrow = GetThemeIcon(arrowName);
                        if (arrow.HasValue) context.Icon(arrow.Value, new Vector2(indentX + 4, rect.Center.Y - arrow.Value.LogicalSize.Y / 2), Color.White);
                        else context.Fill(new Rectangle(indentX + 4, rect.Y + rect.Height / 2 - 2, item.Collapsed ? 7 : 3, item.Collapsed ? 3 : 7), context.Theme.PanelBorderColor);
                    }
                    var columnX = rect.X - GetHorizontalScrollOffset();
                    for (var column = 0; column < Columns; column++)
                    {
                        var cell = new Rectangle(columnX, rect.Y, widths[column], rect.Height);
                        if (SelectMode != TreeSelectMode.Row && item.IsCellSelected(column)) context.Fill(cell, context.Theme.AccentColor);
                        var clipContent = _columns[column].ClipContent;
                        if (clipContent) context.PushClip(cell);
                        try
                        {
                            if (item.GetCustomBackgroundColor(column).HasValue)
                            {
                                var background = item.GetCustomBackgroundColor(column).Value;
                                if (item.IsCustomBackgroundOutline(column)) context.Border(cell, background);
                                else context.Fill(cell, background);
                            }
                            item.GetCustomStyleBox(column)?.Draw(context, cell);
                            if (item.GetCellMode(column) == TreeCellMode.Custom && item.IsEditable(column) && item.IsCustomSetAsButton(column))
                            {
                                context.Fill(cell, item.IsSelected ? context.Theme.PressedColor : context.Theme.HoverColor);
                                context.Border(cell, context.Theme.PanelBorderColor);
                            }
                            var customDraw = item.GetCustomDrawCallbackForTree(column);
                            if (item.GetCellMode(column) == TreeCellMode.Custom && customDraw != null) customDraw(context, item, cell);
                            var textX = column == 0 ? indentX + (int)Indent : columnX + 4;
                            var icon = item.GetIcon(column);
                            if (icon != null)
                            {
                                var source = item.GetIconRegion(column); var sourceWidth = source?.Width ?? icon.Width; var sourceHeight = source?.Height ?? icon.Height;
                                var maximumWidth = item.GetIconMaxWidth(column); var iconWidth = sourceWidth; var iconHeight = sourceHeight;
                                var scale = Math.Min(1f, Math.Max(1, rect.Height - 4) / (float)Math.Max(1, iconHeight));
                                if (maximumWidth > 0) scale = Math.Min(scale, maximumWidth / (float)Math.Max(1, iconWidth));
                                iconWidth = Math.Max(1, (int)MathF.Round(iconWidth * scale)); iconHeight = Math.Max(1, (int)MathF.Round(iconHeight * scale));
                                var iconRect = new Rectangle(textX, rect.Y + (rect.Height - iconHeight) / 2, iconWidth, iconHeight);
                                context.SpriteBatch.Draw(icon, iconRect, source, item.GetIconModulate(column));
                                var overlay = item.GetIconOverlay(column); if (overlay != null) context.SpriteBatch.Draw(overlay, iconRect, Color.White);
                                textX += iconWidth + 4;
                            }
                            if (item.GetCellMode(column) == TreeCellMode.Check)
                            {
                                var box = GetCheckBoxRectangle(item, column, index, rows);
                                var checkColor = item.IsEditable(column) ? context.Theme.AccentColor : context.Theme.DisabledTextColor;
                                var state = item.IsIndeterminate(column) ? "indeterminate" : item.IsChecked(column) ? "checked" : "unchecked";
                                if (!item.IsEditable(column)) state += "_disabled";
                                var check = GetThemeIcon(state);
                                if (check.HasValue) context.Icon(check.Value, box, Color.White);
                                else
                                {
                                    context.Fill(box, item.IsChecked(column) ? checkColor : context.Theme.BackgroundColor); context.Border(box, context.Theme.PanelBorderColor);
                                    if (item.IsIndeterminate(column)) context.Fill(new Rectangle(box.X + 3, box.Y + 6, 8, 2), context.Theme.TextColor);
                                    else if (item.IsChecked(column)) context.Fill(new Rectangle(box.X + 4, box.Y + 4, 6, 6), Color.White);
                                }
                                textX += 18;
                            }
                            else if (item.GetCellMode(column) == TreeCellMode.Range)
                            {
                                var spinner = GetRangeSpinnerRectangle(cell);
                                context.Fill(spinner, item.IsEditable(column) ? context.Theme.BackgroundColor : context.Theme.DisabledTextColor);
                                context.Border(spinner, context.Theme.PanelBorderColor);
                                var updown = GetThemeIcon("updown");
                                if (updown.HasValue) context.Icon(updown.Value, new Vector2(spinner.Center.X - updown.Value.LogicalSize.X / 2, spinner.Center.Y - updown.Value.LogicalSize.Y / 2), item.IsEditable(column) ? Color.White : context.Theme.DisabledTextColor);
                                else if (string.IsNullOrEmpty(item.GetText(column)))
                                {
                                    var centerX = spinner.X + spinner.Width / 2;
                                    context.Fill(new Rectangle(centerX - 2, spinner.Y + 4, 5, 3), context.Theme.TextColor);
                                    context.Fill(new Rectangle(centerX - 2, spinner.Bottom - 7, 5, 3), context.Theme.TextColor);
                                }
                                else
                                    context.Fill(new Rectangle(spinner.X + Math.Max(1, spinner.Width / 2 - 2), spinner.Y + spinner.Height / 2 - 1, 5, 3), context.Theme.TextColor);
                            }
                            if (item.GetCellMode(column) == TreeCellMode.Custom && item.IsEditable(column))
                            {
                                var selectArrow = GetThemeIcon("select_arrow");
                                if (selectArrow.HasValue) context.Icon(selectArrow.Value, new Vector2(cell.Right - 4 - selectArrow.Value.LogicalSize.X, cell.Center.Y - selectArrow.Value.LogicalSize.Y / 2), Color.White);
                            }
                            var cellFont = item.GetEffectiveCustomUIFont(column) ?? EffectiveUIFont;
                            if (cellFont != null)
                            {
                                cellFont = TextMetrics.Resize(cellFont, item.GetCustomFontSize(column));
                                var lineHeight = TextMetrics.LineHeight(cellFont);
                                var text = item.GetDisplayText(column); var textWidth = TextMetrics.Measure(cellFont, text).X;
                                if (item.GetExpandRight(column) || item.GetTextAlignment(column) == HorizontalAlignment.Right) textX = cell.Right - 4 - (int)MathF.Ceiling(textWidth);
                                else if (item.GetTextAlignment(column) == HorizontalAlignment.Center) textX = cell.X + (cell.Width - (int)MathF.Ceiling(textWidth)) / 2;
                                var defaultColor = item.IsSelectable(column) && (item.GetCellMode(column) != TreeCellMode.Check || item.IsEditable(column)) ? context.Theme.TextColor : context.Theme.DisabledTextColor;
                                context.Text(cellFont, text, new Vector2(textX, rect.Y + Math.Max(2, (rect.Height - lineHeight) / 2)), item.GetCustomColor(column) ?? defaultColor);
                            }
                            for (var buttonIndex = 0; buttonIndex < item.GetButtonCount(column); buttonIndex++)
                            {
                                var button = item.GetButtonModelForTree(column, buttonIndex); var buttonRect = GetButtonRectangle(item, column, index, buttonIndex, rows);
                                var buttonColor = button.Disabled ? context.Theme.DisabledTextColor : button.Color;
                                context.Fill(buttonRect, context.Theme.BackgroundColor); context.Border(buttonRect, context.Theme.PanelBorderColor);
                                if (button.Texture != null)
                                {
                                    var inset = new Rectangle(buttonRect.X + 2, buttonRect.Y + 2, Math.Max(1, buttonRect.Width - 4), Math.Max(1, buttonRect.Height - 4));
                                    context.SpriteBatch.Draw(button.Texture, inset, buttonColor);
                                }
                                else
                                    context.Fill(new Rectangle(buttonRect.X + 4, buttonRect.Y + 4, Math.Max(1, buttonRect.Width - 8), Math.Max(1, buttonRect.Height - 8)), buttonColor);
                            }
                        }
                        finally { if (clipContent) context.PopClip(); }
                        columnX += widths[column];
                    }
                    rowY += rect.Height;
                }
            }
            finally { context.PopClip(); }
            DrawScrollHints(context);
        }
        private int RowOriginY => Bounds.Y + 1 + (ColumnTitlesVisible ? (int)ItemHeight : 0);
        private int VerticalScrollBarWidth => _verticalScrollBar.Visible ? Math.Max(1, (int)MathF.Ceiling(_verticalScrollBar.GetMinimumSize().X)) : 0;
        private int HorizontalScrollBarHeight => _horizontalScrollBar.Visible ? Math.Max(1, (int)MathF.Ceiling(_horizontalScrollBar.GetMinimumSize().Y)) : 0;
        private int ContentLeft => Bounds.X + 1 + (IsLayoutRtl() ? VerticalScrollBarWidth : 0);
        private int ContentWidth => Math.Max(0, Bounds.Width - 2 - VerticalScrollBarWidth);
        private int GetColumnAtX(int x)
        {
            if (x < ContentLeft || x >= ContentLeft + ContentWidth) return -1;
            var right = ContentLeft - GetHorizontalScrollOffset(); var widths = GetColumnWidths();
            for (var column = 0; column < widths.Count; column++) { right += widths[column]; if (x < right) return column; }
            return Columns - 1;
        }
        private bool TryGetColumnResizeHandle(Point point, out int column)
        {
            column = -1;
            if (!ColumnResizeEnabled || !ColumnTitlesVisible || point.Y < Bounds.Y + 1 || point.Y >= RowOriginY) return false;
            var right = ContentLeft - GetHorizontalScrollOffset();
            var widths = GetColumnWidths();
            var tolerance = Math.Max(1, ColumnResizeHandleWidth);
            for (var index = 0; index < widths.Count - 1; index++)
            {
                right += widths[index];
                if (Math.Abs(point.X - right) <= tolerance) { column = index; return true; }
            }
            return false;
        }
        private Rectangle GetCellRectangle(TreeItem item, int column, int rowIndex)
        {
            return GetCellRectangle(item, column, rowIndex, Flatten());
        }
        private Rectangle GetCellRectangle(TreeItem item, int column, int rowIndex, List<TreeItem> rows)
        {
            var widths = GetColumnWidths(); var x = ContentLeft - GetHorizontalScrollOffset();
            for (var i = 0; i < column; i++) x += widths[i];
            return new Rectangle(x, GetRowTop(rows, rowIndex), widths[column], GetRowHeight(item));
        }
        /// <summary>Returns the on-screen rectangle for an item's cell or a cell action button.</summary>
        public Rectangle GetItemAreaRectangle(TreeItem item, int column = -1, int button = -1)
        {
            if (item == null || item.Owner != this) return Rectangle.Empty;
            var rows = Flatten(); var rowIndex = rows.IndexOf(item); if (rowIndex < 0) return Rectangle.Empty;
            if (column < 0) return new Rectangle(ContentLeft, GetRowTop(rows, rowIndex), ContentWidth, GetRowHeight(item));
            ValidateColumn(column);
            if (button >= 0) return button < item.GetButtonCount(column) ? GetButtonRectangle(item, column, rowIndex, button, rows) : Rectangle.Empty;
            return GetCellRectangle(item, column, rowIndex, rows);
        }
        private int GetButtonsWidth(TreeItem item, int column) => item.GetButtonCount(column) * 16;
        private Rectangle GetCheckBoxRectangle(TreeItem item, int column, int rowIndex, List<TreeItem> rows)
        {
            var cell = GetCellRectangle(item, column, rowIndex, rows);
            var textX = column == 0 ? cell.X + (int)(item.Depth * Indent) + (int)Indent : cell.X + 4;
            var icon = item.GetIcon(column);
            if (icon != null)
            {
                var source = item.GetIconRegion(column); var sourceWidth = source?.Width ?? icon.Width; var sourceHeight = source?.Height ?? icon.Height;
                var maximumWidth = item.GetIconMaxWidth(column); var scale = Math.Min(1f, Math.Max(1, cell.Height - 4) / (float)Math.Max(1, sourceHeight));
                if (maximumWidth > 0) scale = Math.Min(scale, maximumWidth / (float)Math.Max(1, sourceWidth));
                textX += Math.Max(1, (int)MathF.Round(sourceWidth * scale)) + 4;
            }
            return new Rectangle(textX, cell.Y + Math.Max(2, (cell.Height - 14) / 2), 14, 14);
        }
        private static Rectangle GetRangeSpinnerRectangle(Rectangle cell)
        {
            var width = Math.Min(cell.Width, Math.Max(1, cell.Height / 2));
            return new Rectangle(cell.Right - width, cell.Y, width, cell.Height);
        }
        private bool TryGetRangeCellAtPosition(Point point, out TreeItem item, out int column, out Rectangle cell)
        {
            item = GetItemAtPosition(point);
            column = item == null ? -1 : GetColumnAtPosition(point);
            cell = Rectangle.Empty;
            if (item == null || column < 0 || item.GetCellMode(column) != TreeCellMode.Range) return false;
            cell = GetItemAreaRectangle(item, column);
            return cell.Width > 0 && cell.Height > 0;
        }
        private void AdjustRangeSpinner(TreeItem item, int column, Point point, Rectangle cell, bool setToEndpoint)
        {
            var up = point.Y < cell.Y + cell.Height / 2;
            item.GetRangeConfig(column, out _, out _, out var step);
            item.SetRange(column, setToEndpoint ? (up ? item.GetRangeMaximum(column) : item.GetRangeMinimum(column)) : item.GetRange(column) + (up ? step : -step));
            ItemEdited?.Invoke(this, item, column);
        }
        private void BeginRangeStepRepeat(TreeItem item, int column, Point point, Rectangle cell)
        {
            _rangeStepRepeatItem = item; _rangeStepRepeatColumn = column; _rangeStepRepeatUp = point.Y < cell.Y + cell.Height / 2;
            _rangeStepRepeatElapsed = TimeSpan.Zero; _rangeStepRepeatIntervalElapsed = TimeSpan.Zero;
        }
        private void UpdateRangeStepRepeat(TimeSpan elapsed)
        {
            if (_rangeStepRepeatItem == null || elapsed <= TimeSpan.Zero) return;
            var item = _rangeStepRepeatItem; var column = _rangeStepRepeatColumn;
            if (item.Owner != this || !item.IsEditable(column) || !string.IsNullOrEmpty(item.GetText(column))) { ResetRangeStepRepeat(); return; }
            var cell = GetItemAreaRectangle(item, column); var pointer = Context?.PointerPosition ?? default;
            if (!GetRangeSpinnerRectangle(cell).Contains(pointer) || (pointer.Y < cell.Y + cell.Height / 2) != _rangeStepRepeatUp) { ResetRangeStepRepeat(); return; }
            var previousElapsed = _rangeStepRepeatElapsed;
            _rangeStepRepeatElapsed += elapsed;
            if (_rangeStepRepeatElapsed < RangeStepRepeatDelay) return;
            var interval = RangeStepRepeatInterval <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : RangeStepRepeatInterval;
            if (previousElapsed < RangeStepRepeatDelay)
            {
                item.GetRangeConfig(column, out _, out _, out var initialStep);
                item.SetRange(column, item.GetRange(column) + (_rangeStepRepeatUp ? initialStep : -initialStep));
                ItemEdited?.Invoke(this, item, column);
                _rangeStepRepeatIntervalElapsed = _rangeStepRepeatElapsed - RangeStepRepeatDelay;
            }
            else _rangeStepRepeatIntervalElapsed += elapsed;
            while (_rangeStepRepeatIntervalElapsed >= interval)
            {
                _rangeStepRepeatIntervalElapsed -= interval;
                item.GetRangeConfig(column, out _, out _, out var step);
                item.SetRange(column, item.GetRange(column) + (_rangeStepRepeatUp ? step : -step));
                ItemEdited?.Invoke(this, item, column);
            }
        }
        private void ResetRangeStepRepeat()
        {
            _rangeStepRepeatItem = null; _rangeStepRepeatColumn = -1; _rangeStepRepeatElapsed = TimeSpan.Zero; _rangeStepRepeatIntervalElapsed = TimeSpan.Zero;
        }
        private void BeginRangeDrag(TreeItem item, int column, Point point)
        {
            _rangeDragItem = item; _rangeDragColumn = column; _rangeDragStart = point; _rangeDragPrevious = point; _rangeDragging = false;
        }
        private void ResetRangeDrag()
        {
            _rangeDragItem = null; _rangeDragColumn = -1; _rangeDragging = false;
        }
        private void ShowRangePopup(TreeItem item, int column, Rectangle cell)
        {
            if (Context == null) return;
            _rangePopup.Clear(); _rangePopupValues.Clear();
            var options = item.GetText(column).Split(',');
            for (var index = 0; index < options.Length; index++)
            {
                var entry = options[index]; var separator = entry.IndexOf(':'); var value = index;
                if (separator >= 0 && separator < entry.Length - 1) int.TryParse(entry.Substring(separator + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
                _rangePopup.AddItem(separator < 0 ? entry : entry.Substring(0, separator));
                _rangePopupValues.Add(value);
            }
            _rangePopupItem = item; _rangePopupColumn = column; _rangePopup.Font = Font;
            if (_rangePopup.Context != Context) Context.Add(_rangePopup);
            _rangePopup.PopupAt(new Vector2(cell.X, cell.Bottom), new Vector2(cell.Width, 0));
        }
        private void SelectRangePopupOption(int index)
        {
            if (_rangePopupItem == null || _rangePopupColumn < 0 || index < 0 || index >= _rangePopupValues.Count) return;
            var item = _rangePopupItem; var column = _rangePopupColumn;
            _rangePopupItem = null; _rangePopupColumn = -1;
            item.SetRange(column, _rangePopupValues[index]); ItemEdited?.Invoke(this, item, column);
        }
        private void ShowRangeEditor(TreeItem item, int column, Rectangle cell)
        {
            if (Context == null || cell.Width <= 0 || cell.Height <= 0) return;
            item.GetRangeConfig(column, out var minimum, out var maximum, out var step);
            _rangeEditorItem = item; _rangeEditorColumn = column;
            _syncingRangeEditor = true;
            _rangeEditorSlider.MinValue = minimum; _rangeEditorSlider.MaxValue = maximum; _rangeEditorSlider.Step = step; _rangeEditorSlider.ExpRatio = item.IsRangeExponential(column); _rangeEditorSlider.SetValueNoSignal(item.GetRange(column));
            _rangeEditorText.Text = item.GetRange(column).ToString(CultureInfo.InvariantCulture); _rangeEditorText.SelectAll(); _syncingRangeEditor = false;
            var width = Math.Max(80, cell.Width); var textHeight = Math.Max(24, (int)MathF.Ceiling(_rangeEditorText.GetMinimumSize().Y)); var sliderHeight = 20;
            _rangeEditorPopup.Size = new Vector2(width, textHeight + sliderHeight + 10);
            _rangeEditorText.Position = new Vector2(4, 4); _rangeEditorText.Size = new Vector2(width - 8, textHeight);
            _rangeEditorSlider.Position = new Vector2(4, textHeight + 6); _rangeEditorSlider.Size = new Vector2(width - 8, sliderHeight);
            if (_rangeEditorPopup.Context != Context) Context.Add(_rangeEditorPopup);
            _rangeEditorPopup.PopupAt(new Vector2(cell.X, cell.Bottom));
            _rangeEditorText.GrabFocus();
        }
        private void CommitRangeEditor()
        {
            if (_rangeEditorItem == null || _rangeEditorColumn < 0) return;
            var item = _rangeEditorItem; var column = _rangeEditorColumn;
            if (!float.TryParse(_rangeEditorText.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) value = 0;
            item.SetRange(column, value); ItemEdited?.Invoke(this, item, column);
            _rangeEditorPopup.Hide();
        }
        private void CancelRangeEditor() => _rangeEditorPopup.Hide(PopupHideReason.Cancelled);
        private void ClearRangeEditorState()
        {
            _rangeEditorItem = null; _rangeEditorColumn = -1; _syncingRangeEditor = false;
        }
        private void ShowStringEditor(TreeItem item, int column, Rectangle cell)
        {
            if (Context == null || cell.Width <= 0 || cell.Height <= 0) return;
            _stringEditorItem = item; _stringEditorColumn = column; _stringEditorMultiline = item.IsEditMultiline(column); _stringEditorCommitted = false;
            var width = Math.Max(80, cell.Width);
            var height = Math.Max(cell.Height, (int)MathF.Ceiling(_stringEditorMultiline ? _stringEditorText.GetMinimumSize().Y : _stringEditorLine.GetMinimumSize().Y));
            _stringEditorPopup.Size = new Vector2(width, height);
            _stringEditorLine.Visible = !_stringEditorMultiline;
            _stringEditorText.Visible = _stringEditorMultiline;
            if (_stringEditorMultiline)
            {
                _stringEditorText.Font = Font;
                _stringEditorText.Text = item.GetText(column); _stringEditorText.SelectAll();
                _stringEditorText.Position = Vector2.Zero; _stringEditorText.Size = new Vector2(width, height);
            }
            else
            {
                _stringEditorLine.Font = Font;
                _stringEditorLine.Text = item.GetText(column); _stringEditorLine.SelectAll();
                _stringEditorLine.Position = Vector2.Zero; _stringEditorLine.Size = new Vector2(width, height);
            }
            if (_stringEditorPopup.Context != Context) Context.Add(_stringEditorPopup);
            _stringEditorPopup.PopupAt(new Vector2(cell.X, cell.Y));
            if (_stringEditorMultiline) _stringEditorText.GrabFocus(); else _stringEditorLine.GrabFocus();
        }
        private void CommitStringEditor()
        {
            if (_stringEditorItem == null || _stringEditorColumn < 0) return;
            var item = _stringEditorItem; var column = _stringEditorColumn;
            item.SetText(column, _stringEditorMultiline ? _stringEditorText.Text : _stringEditorLine.Text);
            _stringEditorCommitted = true;
            ItemEdited?.Invoke(this, item, column);
            _stringEditorPopup.Hide();
        }
        private void CancelStringEditor() => _stringEditorPopup.Hide(PopupHideReason.Cancelled);
        private void CloseStringEditor(PopupHideReason reason)
        {
            if (!_stringEditorCommitted && reason != PopupHideReason.Cancelled && _stringEditorItem != null) CommitStringEditor();
            _stringEditorItem = null; _stringEditorColumn = -1; _stringEditorMultiline = false; _stringEditorCommitted = false;
        }
        private Rectangle GetButtonRectangle(TreeItem item, int column, int rowIndex, int buttonIndex, List<TreeItem> rows = null)
        {
            var cell = GetCellRectangle(item, column, rowIndex, rows ?? Flatten());
            return new Rectangle(cell.Right - 4 - GetButtonsWidth(item, column) + buttonIndex * 16, cell.Y + Math.Max(2, (cell.Height - 14) / 2), 14, 14);
        }
        private int GetButtonAtPosition(TreeItem item, int column, int rowIndex, Point point, List<TreeItem> rows = null)
        {
            if (rowIndex < 0) return -1;
            for (var index = 0; index < item.GetButtonCount(column); index++) if (GetButtonRectangle(item, column, rowIndex, index, rows).Contains(point)) return index;
            return -1;
        }
        private int GetRowHeight(TreeItem item)
        {
            var height = Math.Max(Math.Max(1, (int)ItemHeight), item.GetCustomMinimumHeight());
            for (var column = 0; column < Columns; column++)
            {
                var font = item.GetEffectiveCustomUIFont(column) ?? EffectiveUIFont;
                if (font != null) height = Math.Max(height, TextMetrics.LineHeight(TextMetrics.Resize(font, item.GetCustomFontSize(column))));
            }
            return height;
        }
        private int GetRowTop(List<TreeItem> rows, int rowIndex)
        {
            var y = RowOriginY - GetVerticalScrollOffset();
            for (var index = 0; index < rowIndex; index++) y += GetRowHeight(rows[index]);
            return y;
        }
        private int GetUnscrolledRowTop(List<TreeItem> rows, int rowIndex)
        {
            var y = RowOriginY;
            for (var index = 0; index < rowIndex; index++) y += GetRowHeight(rows[index]);
            return y;
        }
        private int GetRowIndexAtPosition(List<TreeItem> rows, int y)
        {
            var rowY = RowOriginY - GetVerticalScrollOffset();
            for (var index = 0; index < rows.Count; index++)
            {
                var height = GetRowHeight(rows[index]);
                if (y >= rowY && y < rowY + height) return index;
                rowY += height;
            }
            return -1;
        }
        internal override bool PointerWheel(int delta)
        {
            var pointer = Context?.PointerPosition ?? default;
            if (delta != 0 && TryGetRangeCellAtPosition(pointer, out var rangeItem, out var rangeColumn, out var rangeCell) && rangeItem.IsEditable(rangeColumn) && string.IsNullOrEmpty(rangeItem.GetText(rangeColumn)) && GetRangeSpinnerRectangle(rangeCell).Contains(pointer))
            {
                var directionPoint = new Point(pointer.X, delta > 0 ? rangeCell.Y : rangeCell.Bottom - 1);
                AdjustRangeSpinner(rangeItem, rangeColumn, directionPoint, rangeCell, setToEndpoint: false);
                return true;
            }
            if (!VerticalScrollEnabled || delta == 0 || GetVerticalScrollMaximum() <= 0) return false;
            var previous = GetVerticalScrollOffset();
            SetVerticalScrollOffset(previous - Math.Sign(delta) * Math.Max(1, (int)ItemHeight));
            return previous != GetVerticalScrollOffset();
        }
        private int GetViewportHeight() => GetViewportHeight(_horizontalScrollBar.Visible);
        private int GetViewportHeight(bool horizontalVisible) => Math.Max(0, Bounds.Bottom - RowOriginY - 1 - (horizontalVisible ? Math.Max(1, (int)MathF.Ceiling(_horizontalScrollBar.GetMinimumSize().Y)) : 0));
        private int GetContentWidth(bool verticalVisible) => Math.Max(0, Bounds.Width - 2 - (verticalVisible ? Math.Max(1, (int)MathF.Ceiling(_verticalScrollBar.GetMinimumSize().X)) : 0));
        private int GetInternalColumnWidth(int contentWidth)
        {
            var minimums = 0;
            foreach (var column in _columns) minimums += column.CustomMinimumWidth;
            return Math.Max(contentWidth, minimums);
        }
        private int GetHorizontalScrollMaximum(bool verticalVisible) => HorizontalScrollEnabled ? Math.Max(0, GetInternalColumnWidth(GetContentWidth(verticalVisible)) - GetContentWidth(verticalVisible)) : 0;
        private int GetVerticalScrollMaximum(bool horizontalVisible)
        {
            var contentHeight = 0;
            foreach (var item in Flatten()) contentHeight += GetRowHeight(item);
            return VerticalScrollEnabled ? Math.Max(0, contentHeight - GetViewportHeight(horizontalVisible)) : 0;
        }
        private void SynchronizeScrollBars()
        {
            var verticalWidth = Math.Max(1, (int)MathF.Ceiling(_verticalScrollBar.GetMinimumSize().X));
            var horizontalHeight = Math.Max(1, (int)MathF.Ceiling(_horizontalScrollBar.GetMinimumSize().Y));
            var showHorizontal = _horizontalScrollBar.Visible; var showVertical = _verticalScrollBar.Visible;
            for (var iteration = 0; iteration < 3; iteration++)
            {
                var nextHorizontal = HorizontalScrollEnabled && Size.X > verticalWidth + 2 && GetHorizontalScrollMaximum(showVertical) > 0;
                var nextVertical = VerticalScrollEnabled && Size.Y > horizontalHeight + 2 && GetVerticalScrollMaximum(nextHorizontal) > 0;
                if (nextHorizontal == showHorizontal && nextVertical == showVertical) break;
                showHorizontal = nextHorizontal; showVertical = nextVertical;
            }
            var visibilityChanged = _horizontalScrollBar.Visible != showHorizontal || _verticalScrollBar.Visible != showVertical;
            _horizontalScrollBar.Visible = showHorizontal; _verticalScrollBar.Visible = showVertical;
            var viewportWidth = Math.Max(0, Size.X - (showVertical ? verticalWidth : 0));
            var viewportHeight = Math.Max(0, Size.Y - (showHorizontal ? horizontalHeight : 0));
            _horizontalScrollBar.Position = new Vector2(IsLayoutRtl() && showVertical ? verticalWidth : 0, Math.Max(0, Size.Y - horizontalHeight));
            _horizontalScrollBar.Size = new Vector2(viewportWidth, horizontalHeight);
            _verticalScrollBar.Position = new Vector2(IsLayoutRtl() ? 0 : Math.Max(0, Size.X - verticalWidth), 0);
            _verticalScrollBar.Size = new Vector2(verticalWidth, viewportHeight);
            _syncingScrollBars = true;
            var horizontalPage = GetContentWidth(showVertical);
            _horizontalScrollBar.MinValue = 0;
            _horizontalScrollBar.MaxValue = Math.Max(0, GetHorizontalScrollMaximum(showVertical) + horizontalPage);
            _horizontalScrollBar.Page = horizontalPage;
            _horizontalScrollBar.SetValueNoSignal(GetHorizontalScrollOffset());
            var verticalPage = GetViewportHeight(showHorizontal);
            _verticalScrollBar.MinValue = 0;
            _verticalScrollBar.MaxValue = Math.Max(0, GetVerticalScrollMaximum(showHorizontal) + verticalPage);
            _verticalScrollBar.Page = verticalPage;
            _verticalScrollBar.SetValueNoSignal(GetVerticalScrollOffset());
            _syncingScrollBars = false;
            if (visibilityChanged) QueueLayout();
        }
        private void DrawScrollHints(UIRenderContext context)
        {
            if (ScrollHintMode == TreeScrollHintMode.Disabled || GetVerticalScrollMaximum() <= 0) return;
            var offset = GetVerticalScrollOffset(); var maximum = GetVerticalScrollMaximum(); var height = Math.Max(1, ScrollHintHeight);
            if ((ScrollHintMode == TreeScrollHintMode.Both || ScrollHintMode == TreeScrollHintMode.Top) && offset > 1)
                context.Fill(new Rectangle(ContentLeft, RowOriginY, ContentWidth, height), context.Theme.PanelBorderColor);
            if ((ScrollHintMode == TreeScrollHintMode.Both || ScrollHintMode == TreeScrollHintMode.Bottom) && offset < maximum)
                context.Fill(new Rectangle(ContentLeft, Math.Max(RowOriginY, Bounds.Bottom - 1 - HorizontalScrollBarHeight - height), ContentWidth, height), context.Theme.PanelBorderColor);
        }
        private void BeginCellEdit(TreeItem item, int column, bool emitCustomPopup, bool emitItemEdited = true, bool popupArrowPressed = false)
        {
            _editedItem = item; _editedColumn = column;
            var cell = GetItemAreaRectangle(item, column);
            _customPopupRect = cell;
            if (emitCustomPopup && item.GetCellMode(column) == TreeCellMode.Custom) CustomPopupEdited?.Invoke(this, popupArrowPressed);
            if (emitItemEdited) ItemEdited?.Invoke(this, item, column);
        }
        private void UpdateDragAutoScroll(TimeSpan elapsed)
        {
            if (!_dragAutoScrolling || !VerticalScrollEnabled || DragAutoScrollSpeed <= 0 || GetVerticalScrollMaximum() <= 0) return;
            var border = Math.Max(1, DragAutoScrollBorder);
            var viewportTop = RowOriginY; var viewportBottom = Bounds.Bottom - 1 - HorizontalScrollBarHeight;
            var edgeDistance = 0f;
            if (_dragPointer.Y < viewportTop + border) edgeDistance = (_dragPointer.Y - (viewportTop + border)) / (float)border;
            else if (_dragPointer.Y > viewportBottom - border) edgeDistance = (_dragPointer.Y - (viewportBottom - border)) / (float)border;
            if (edgeDistance == 0) return;
            var delta = (int)MathF.Round(edgeDistance * DragAutoScrollSpeed * Math.Max(0, (float)elapsed.TotalSeconds));
            if (delta != 0) SetVerticalScrollOffset(GetVerticalScrollOffset() + delta);
        }
        private void UpdateDragUnfoldTarget(Point point)
        {
            if (!DragUnfoldingEnabled) { ResetDragUnfoldTarget(); return; }
            var target = GetItemAtPosition(point);
            if (target == _selected || target == null || !target.Collapsed || target.Children.Count == 0) target = null;
            if (_dragUnfoldTarget == target) return;
            _dragUnfoldTarget = target; _dragUnfoldElapsed = TimeSpan.Zero;
        }
        private void UpdateDragUnfold(TimeSpan elapsed)
        {
            if (!_dragAutoScrolling || !DragUnfoldingEnabled || _dragUnfoldTarget == null) return;
            _dragUnfoldElapsed += elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
            if (_dragUnfoldElapsed < DragUnfoldDelay) return;
            var target = _dragUnfoldTarget; ResetDragUnfoldTarget(); target.SetCollapsed(false);
        }
        private void ResetDragUnfoldTarget() { _dragUnfoldTarget = null; _dragUnfoldElapsed = TimeSpan.Zero; }
        private int GetHorizontalScrollMaximum() => GetHorizontalScrollMaximum(_verticalScrollBar.Visible);
        private int GetVerticalScrollMaximum() => GetVerticalScrollMaximum(_horizontalScrollBar.Visible);
        private int ClampHorizontalScrollOffset(int value) => MathHelper.Clamp(value, 0, GetHorizontalScrollMaximum());
        private int GetHorizontalScrollOffset() => ClampHorizontalScrollOffset(_horizontalScrollOffset);
        private void SetHorizontalScrollOffset(int value)
        {
            var clamped = ClampHorizontalScrollOffset(value);
            if (_horizontalScrollOffset == clamped) return;
            _horizontalScrollOffset = clamped;
            SynchronizeScrollBars();
            QueueLayout();
        }
        private int ClampVerticalScrollOffset(int value) => MathHelper.Clamp(value, 0, GetVerticalScrollMaximum());
        private int GetVerticalScrollOffset() => ClampVerticalScrollOffset(_verticalScrollOffset);
        private void SetVerticalScrollOffset(int value)
        {
            var clamped = ClampVerticalScrollOffset(value);
            if (_verticalScrollOffset == clamped) return;
            _verticalScrollOffset = clamped;
            SynchronizeScrollBars();
            QueueLayout();
        }
        private List<int> GetColumnWidths()
        {
            var widths = new List<int>(Columns); var available = ContentWidth; var minimums = 0; var ratios = 0;
            foreach (var column in _columns) { minimums += column.CustomMinimumWidth; if (column.Expand) ratios += column.ExpandRatio; }
            var remaining = Math.Max(0, available - minimums);
            foreach (var column in _columns) widths.Add(column.CustomMinimumWidth + (column.Expand && ratios > 0 ? remaining * column.ExpandRatio / ratios : 0));
            if (widths.Count > 0 && Sum(widths) < available) widths[widths.Count - 1] += available - Sum(widths);
            return widths;
        }
        private static int Sum(List<int> values) { var total = 0; foreach (var value in values) total += value; return total; }
        /// <summary>
        /// One peer per visible row. A tree draws its rows rather than building a control for each,
        /// so this is the only way they reach the accessibility tree — and a navigation tree that
        /// announces nothing makes everything behind it unreachable, not merely unannounced.
        /// </summary>
        public override IReadOnlyList<AccessibilityPeer> GetAccessibilityChildren()
        {
            var rows = Flatten();
            if (rows.Count == 0) return Array.Empty<AccessibilityPeer>();

            var peers = new List<AccessibilityPeer>(rows.Count);
            foreach (var item in rows)
            {
                if (!_accessibilityPeers.TryGetValue(item, out var peer))
                {
                    // Cached per item so a row keeps its identity across snapshots: a diff that saw
                    // a new id for every row on every frame would report the whole tree as replaced.
                    peer = new TreeItemAccessibilityPeer(this, item);
                    _accessibilityPeers[item] = peer;
                }

                peers.Add(peer);
            }

            return peers;
        }

        private readonly Dictionary<TreeItem, TreeItemAccessibilityPeer> _accessibilityPeers =
            new Dictionary<TreeItem, TreeItemAccessibilityPeer>();

        internal string GetAccessibilityItemText(TreeItem item) =>
            item == null || item.Owner != this ? string.Empty : item.GetText(0) ?? string.Empty;

        internal AccessibilityActions GetAccessibilityItemActions(TreeItem item)
        {
            if (item == null || item.Owner != this) return AccessibilityActions.None;

            var actions = AccessibilityActions.Focus;
            if (item.Selectable) actions |= AccessibilityActions.Select;
            if (item.Children.Count > 0)
                actions |= item.Collapsed ? AccessibilityActions.Expand : AccessibilityActions.Collapse;
            return actions;
        }

        internal AccessibilityStates GetAccessibilityItemStates(TreeItem item)
        {
            if (item == null || item.Owner != this) return AccessibilityStates.Offscreen;

            var states = AccessibilityStates.None;
            if (!item.Selectable) states |= AccessibilityStates.Disabled;
            if (ReferenceEquals(SelectedItem, item)) states |= AccessibilityStates.Selected;
            if (item.Children.Count > 0)
                states |= item.Collapsed ? AccessibilityStates.Collapsed : AccessibilityStates.Expanded;
            return states;
        }

        /// <summary>
        /// Runs an action on one row, through the same selection and collapse paths a click takes,
        /// so an assistive technology cannot reach a row a person cannot.
        /// </summary>
        internal bool PerformAccessibilityItemAction(TreeItem item, AccessibilityActions action)
        {
            if (item == null || item.Owner != this) return false;

            switch (action)
            {
                case AccessibilityActions.Select:
                case AccessibilityActions.Focus:
                    if (!item.Selectable) return false;
                    Select(item);
                    return true;
                case AccessibilityActions.Expand:
                    if (item.Children.Count == 0) return false;
                    item.SetCollapsed(false);
                    return true;
                case AccessibilityActions.Collapse:
                    if (item.Children.Count == 0) return false;
                    item.SetCollapsed(true);
                    return true;
                default:
                    return false;
            }
        }

        private List<TreeItem> Flatten()
        {
            var result = new List<TreeItem>(); foreach (var root in _roots) Collect(root, result, !(HideRoot && root.Parent == null)); return result;
        }
        private static void Collect(TreeItem item, List<TreeItem> result, bool include)
        {
            if (!item.Visible) return;
            if (include) result.Add(item); if (item.Collapsed) return; foreach (var child in item.Children) Collect(child, result, true);
        }
        private IEnumerable<TreeItem> AllItems() { foreach (var root in _roots) foreach (var item in Descendants(root)) yield return item; }
        private static bool IsInSubtree(TreeItem item, TreeItem root)
        {
            for (var current = item; current != null; current = current.Parent) if (current == root) return true;
            return false;
        }
        private IEnumerable<TreeItem> EnumerateExpandedItems() { foreach (var root in _roots) foreach (var item in ExpandedDescendants(root)) yield return item; }
        private static IEnumerable<TreeItem> Descendants(TreeItem item) { yield return item; foreach (var child in item.Children) foreach (var descendant in Descendants(child)) yield return descendant; }
        private static IEnumerable<TreeItem> ExpandedDescendants(TreeItem item) { yield return item; if (item.Collapsed) yield break; foreach (var child in item.Children) foreach (var descendant in ExpandedDescendants(child)) yield return descendant; }
        private void ValidateColumn(int column) { if (column < 0 || column >= Columns) throw new ArgumentOutOfRangeException(nameof(column)); }
    }
}
