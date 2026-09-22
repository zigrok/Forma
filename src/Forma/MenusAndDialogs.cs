// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// Menu and dialog APIs and behavior are adapted from Godot Engine's popup_menu.cpp,
// menu_button.cpp, menu_bar.cpp, dialogs.cpp, and file_dialog.cpp;
// see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    public enum PopupMenuItemKind { Item, Check, RadioCheck, Separator, Submenu, MultiState }
    public enum PopupMenuCheckableType { None, Check, Radio }
    public enum AutoTranslateMode { Inherit, Always, Disabled }
    public enum PopupSystemMenu { Invalid = 0, Main = 1, Application = 2, Window = 3, Help = 4, Dock = 5 }

    /// <summary>Retained keyboard shortcut/accelerator descriptor used by <see cref="PopupMenu"/>.</summary>
    public sealed class PopupMenuShortcut
    {
        public PopupMenuShortcut(string name, Keys key, bool control = false, bool alt = false, bool shift = false, bool meta = false)
        {
            Name = name ?? string.Empty;
            Key = key;
            Control = control;
            Alt = alt;
            Shift = shift;
            Meta = meta;
        }

        public string Name { get; }
        public Keys Key { get; }
        public bool Control { get; }
        public bool Alt { get; }
        public bool Shift { get; }
        public bool Meta { get; }
        public bool IsValid => Key != Keys.None;
        public string DisplayText
        {
            get
            {
                if (!IsValid) return string.Empty;
                var parts = new List<string>();
                if (Control) parts.Add("Ctrl");
                if (Alt) parts.Add("Alt");
                if (Shift) parts.Add("Shift");
                if (Meta) parts.Add("Meta");
                parts.Add(Key.ToString());
                return string.Join("+", parts);
            }
        }
        public bool Matches(Keys key, KeyboardState keyboard)
        {
            if (!IsValid || key != Key) return false;
            var control = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
            var alt = keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt);
            var shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
            var meta = keyboard.IsKeyDown(Keys.LeftWindows) || keyboard.IsKeyDown(Keys.RightWindows);
            return control == Control && alt == Alt && shift == Shift && meta == Meta;
        }
    }

    /// <summary>Data for one command presented by <see cref="PopupMenu"/>.</summary>
    public sealed class PopupMenuItem
    {
        internal PopupMenuItem(string text, int id, PopupMenuItemKind kind)
        {
            Text = text ?? string.Empty;
            Id = id;
            Kind = kind;
        }
        public string Text { get; set; }
        public int Id { get; internal set; }
        public PopupMenuItemKind Kind { get; internal set; }
        public bool Separator { get; internal set; }
        public PopupMenuCheckableType CheckableType { get; internal set; }
        public bool Disabled { get; set; }
        public bool Checked { get; set; }
        public bool Indeterminate { get; internal set; }
        public bool Visible { get; internal set; } = true;
        public object Metadata { get; set; }
        public string Tooltip { get; internal set; } = string.Empty;
        public int Indent { get; internal set; }
        public TextDirection TextDirection { get; internal set; } = TextDirection.Inherited;
        public string Language { get; internal set; } = string.Empty;
        public AutoTranslateMode AutoTranslateMode { get; internal set; } = AutoTranslateMode.Inherit;
        public Texture2D Icon { get; internal set; }
        public int IconMaxWidth { get; internal set; }
        public Color IconModulate { get; internal set; } = Color.White;
        public string SubmenuPath { get; internal set; } = string.Empty;
        public PopupMenu Submenu { get; set; }
        public PopupMenuShortcut Accelerator { get; internal set; }
        public PopupMenuShortcut Shortcut { get; internal set; }
        public bool ShortcutIsGlobal { get; internal set; }
        public bool ShortcutDisabled { get; set; }
        public int MaxStates { get; internal set; }
        public int State { get; internal set; }
    }

    /// <summary>
    /// Input surface owned by a <see cref="PopupMenu"/>. It mirrors Godot's internal PopupMenuItems
    /// control so the command list can participate in normal hit testing without making menu data a
    /// collection of transient button controls.
    /// </summary>
    public sealed class PopupMenuItems : Control
    {
        internal PopupMenuItems(PopupMenu popup)
        {
            Popup = popup ?? throw new ArgumentNullException(nameof(popup));
            MouseFilter = MouseFilter.Stop;
        }
        public PopupMenu Popup { get; }
        protected internal override void PointerPressed(Point point) => Popup.HandleItemsPressed(point);
        protected internal override void PointerReleased(Point point, bool isInside) => Popup.HandleItemsReleased(point, isInside);
        internal override bool PointerWheel(int delta) => Popup.HandleItemsWheel(delta);
        public override string GetTooltip(Point position)
        {
            var index = Popup.ItemAt(position);
            return index >= 0 ? Popup.GetItem(index).Tooltip : base.GetTooltip(position);
        }
    }

    /// <summary>A keyboard-focusable command popup with normal, check, radio and separator entries.</summary>
    [TemplatePart(ItemsPartName, typeof(PopupMenuItems))]
    public sealed class PopupMenu : Popup
    {
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.Menu;
        public const string ItemsPartName = "PART_Items";
        private const float DefaultItemVerticalPadding = 8;
        private readonly UIFontSelection _fontSelection = new UIFontSelection();
        private readonly List<PopupMenuItem> _items = new List<PopupMenuItem>();
        private int _highlighted = -1;
        private int _activeSubmenuIndex = -1;
        private int _pendingSubmenuIndex = -1;
        private TimeSpan _pendingSubmenuStarted;
        private string _searchString = string.Empty;
        private string _searchBarText = string.Empty;
        private int _searchBarCaretColumn;
        private bool _searchBarFocused;
        private int _scrollOffset;
        private TimeSpan _lastSearchTime = TimeSpan.MinValue;
        internal MenuButton OwnerMenuButton { get; set; }
        public PopupMenu()
        {
            ClipContents = false;
            CustomMinimumSize = new Vector2(140, 0);
            ItemsControl = new PopupMenuItems(this);
            AddChild(ItemsControl);
            PopupHidden += (_, reason) =>
            {
                _searchBarFocused = false;
                CloseActiveSubmenu(reason);
            };
        }
        public IReadOnlyList<PopupMenuItem> Items => _items;
        public PopupMenuItems ItemsControl { get; }
        public SpriteFont Font { get => _fontSelection.SpriteFont; set { if (_fontSelection.SetSpriteFont(value)) QueueLayout(); } }
        public UIFont UIFont { get => _fontSelection.UIFont; set { if (_fontSelection.SetUIFont(value)) QueueLayout(); } }
        internal UIFont EffectiveUIFont => ResolveFont(_fontSelection);
        public float ItemHeight { get; set; } = 24;
        internal float EffectiveItemHeight => MathF.Ceiling(Math.Max(
            ItemHeight,
            EffectiveUIFont == null
                ? ItemHeight
                : TextMetrics.LineHeight(EffectiveUIFont) + DefaultItemVerticalPadding));
        public TimeSpan SubmenuPopupDelay { get; set; } = TimeSpan.FromMilliseconds(200);
        public bool HideOnItemSelection { get; set; } = true;
        public bool HideOnCheckableItemSelection { get; set; } = true;
        public bool HideOnMultistateItemSelection { get; set; }
        public bool ShrinkHeight { get; set; } = true;
        public bool ShrinkWidth { get; set; } = true;
        public bool AllowSearch { get; set; } = true;
        public bool SearchBarEnabled { get; set; }
        public int SearchBarMinItemCount { get; private set; }
        public bool SearchBarFuzzySearchEnabled { get; set; } = true;
        public int SearchBarFuzzySearchMaxMisses { get; private set; } = 2;
        public float SearchBarHeight { get; set; } = 24;
        public float SearchBarSeparation { get; set; } = 4;
        public TimeSpan IncrementalSearchTimeout { get; set; } = TimeSpan.FromMilliseconds(1000);
        public bool IsSearchBarVisible => SearchBarEnabled && SearchableItemCount >= SearchBarMinItemCount;
        public bool PreferNativeMenu { get; private set; }
        public PopupSystemMenu SystemMenu { get; private set; } = PopupSystemMenu.Invalid;
        public int HighlightedIndex => _highlighted;
        public int GetFocusedItem() => _highlighted;
        public bool IsSearchBarFocused => _searchBarFocused;
        public int GetSearchBarCaretColumn() => _searchBarCaretColumn;
        public int ActiveSubmenuIndex => _activeSubmenuIndex;
        public event Action<PopupMenu, int> IdPressed;
        public event Action<PopupMenu, int> IndexPressed;
        public event Action<PopupMenu, int> IndexFocused;
        public PopupMenuItem AddItem(string text, int id = -1, PopupMenuShortcut accelerator = null)
        {
            var item = Add(text, id, PopupMenuItemKind.Item);
            item.Accelerator = accelerator;
            return item;
        }
        public PopupMenuItem AddIconItem(Texture2D icon, string text, int id = -1, PopupMenuShortcut accelerator = null)
        {
            var item = AddItem(text, id, accelerator);
            item.Icon = icon;
            return item;
        }
        public PopupMenuItem AddCheckItem(string text, int id = -1, PopupMenuShortcut accelerator = null)
        {
            var item = Add(text, id, PopupMenuItemKind.Check);
            item.Accelerator = accelerator;
            return item;
        }
        public PopupMenuItem AddIconCheckItem(Texture2D icon, string text, int id = -1, PopupMenuShortcut accelerator = null)
        {
            var item = AddCheckItem(text, id, accelerator);
            item.Icon = icon;
            return item;
        }
        public PopupMenuItem AddRadioCheckItem(string text, int id = -1, PopupMenuShortcut accelerator = null)
        {
            var item = Add(text, id, PopupMenuItemKind.RadioCheck);
            item.Accelerator = accelerator;
            return item;
        }
        public PopupMenuItem AddIconRadioCheckItem(Texture2D icon, string text, int id = -1, PopupMenuShortcut accelerator = null)
        {
            var item = AddRadioCheckItem(text, id, accelerator);
            item.Icon = icon;
            return item;
        }
        public PopupMenuItem AddMultistateItem(string text, int maxStates, int defaultState = 0, int id = -1, PopupMenuShortcut accelerator = null)
        {
            var item = Add(text, id, PopupMenuItemKind.MultiState);
            item.MaxStates = maxStates;
            item.State = defaultState;
            item.Accelerator = accelerator;
            return item;
        }
        public PopupMenuItem AddShortcut(PopupMenuShortcut shortcut, int id = -1, bool global = false, bool allowEcho = false) => AddShortcutItem(shortcut, id, PopupMenuItemKind.Item, global);
        public PopupMenuItem AddIconShortcut(Texture2D icon, PopupMenuShortcut shortcut, int id = -1, bool global = false, bool allowEcho = false)
        {
            var item = AddShortcut(shortcut, id, global, allowEcho);
            item.Icon = icon;
            return item;
        }
        public PopupMenuItem AddCheckShortcut(PopupMenuShortcut shortcut, int id = -1, bool global = false) => AddShortcutItem(shortcut, id, PopupMenuItemKind.Check, global);
        public PopupMenuItem AddIconCheckShortcut(Texture2D icon, PopupMenuShortcut shortcut, int id = -1, bool global = false)
        {
            var item = AddCheckShortcut(shortcut, id, global);
            item.Icon = icon;
            return item;
        }
        public PopupMenuItem AddRadioCheckShortcut(PopupMenuShortcut shortcut, int id = -1, bool global = false) => AddShortcutItem(shortcut, id, PopupMenuItemKind.RadioCheck, global);
        public PopupMenuItem AddIconRadioCheckShortcut(Texture2D icon, PopupMenuShortcut shortcut, int id = -1, bool global = false)
        {
            var item = AddRadioCheckShortcut(shortcut, id, global);
            item.Icon = icon;
            return item;
        }
        public PopupMenuItem AddSubmenuItem(string text, PopupMenu submenu, int id = -1)
        {
            var item = Add(text, id, PopupMenuItemKind.Submenu);
            SetItemSubmenuNode(_items.Count - 1, submenu);
            return item;
        }
        public PopupMenuItem AddSubmenuItem(string text, string submenu, int id = -1)
        {
            var item = Add(text, id, PopupMenuItemKind.Submenu);
            SetItemSubmenuPath(_items.Count - 1, submenu);
            return item;
        }
        public PopupMenuItem AddSubmenuNodeItem(string text, PopupMenu submenu, int id = -1) => AddSubmenuItem(text, submenu, id);
        public PopupMenuItem AddSeparator(string text = "", int id = -1) => Add(text, id, PopupMenuItemKind.Separator);
        public void Clear() { CloseActiveSubmenu(PopupHideReason.Programmatic); _items.Clear(); _highlighted = -1; _pendingSubmenuIndex = -1; _searchBarText = string.Empty; QueueLayout(); }
        /// <summary>Resizes the item collection, matching Godot's PopupMenu item_count property.</summary>
        public void SetItemCount(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            while (_items.Count < count) AddItem(string.Empty);
            if (_items.Count > count) _items.RemoveRange(count, _items.Count - count);
            if (_highlighted >= _items.Count) _highlighted = -1;
            ApplySearchFilter();
        }
        public PopupMenuItem GetItem(int index) => _items[NormalizeIndex(index)];
        public int GetItemCount() => _items.Count;
        public void SetItemText(int index, string text)
        {
            GetItem(index).Text = text ?? string.Empty;
            ApplySearchFilter();
        }
        public string GetItemText(int index) => GetItem(index).Text;
        public int GetItemIdxFromText(string text)
        {
            for (var index = 0; index < _items.Count; index++)
                if (string.Equals(_items[index].Text, text ?? string.Empty, StringComparison.Ordinal)) return index;
            return -1;
        }
        public void SetItemId(int index, int id) => GetItem(index).Id = id;
        public int GetItemId(int index) => GetItem(index).Id;
        public int GetItemIndex(int id)
        {
            for (var index = 0; index < _items.Count; index++)
                if (_items[index].Id == id) return index;
            return -1;
        }
        public void SetItemMetadata(int index, object metadata) => GetItem(index).Metadata = metadata;
        public object GetItemMetadata(int index) => GetItem(index).Metadata;
        public void SetItemDisabled(int index, bool disabled) => GetItem(index).Disabled = disabled;
        public bool IsItemDisabled(int index) => GetItem(index).Disabled;
        public void SetItemTooltip(int index, string tooltip) => GetItem(index).Tooltip = tooltip ?? string.Empty;
        public string GetItemTooltip(int index) => GetItem(index).Tooltip;
        public void SetItemIndent(int index, int indent) => GetItem(index).Indent = indent;
        public int GetItemIndent(int index) => GetItem(index).Indent;
        public void SetItemTextDirection(int index, TextDirection direction) => GetItem(index).TextDirection = direction;
        public TextDirection GetItemTextDirection(int index) => GetItem(index).TextDirection;
        public void SetItemLanguage(int index, string language) => GetItem(index).Language = language ?? string.Empty;
        public string GetItemLanguage(int index) => GetItem(index).Language;
        public void SetItemAutoTranslateMode(int index, AutoTranslateMode mode) => GetItem(index).AutoTranslateMode = mode;
        public AutoTranslateMode GetItemAutoTranslateMode(int index) => GetItem(index).AutoTranslateMode;
        public void SetItemIcon(int index, Texture2D icon) => GetItem(index).Icon = icon;
        public Texture2D GetItemIcon(int index) => GetItem(index).Icon;
        public void SetItemIconMaxWidth(int index, int width) => GetItem(index).IconMaxWidth = width;
        public int GetItemIconMaxWidth(int index) => GetItem(index).IconMaxWidth;
        public void SetItemIconModulate(int index, Color modulate) => GetItem(index).IconModulate = modulate;
        public Color GetItemIconModulate(int index) => GetItem(index).IconModulate;
        public void SetItemSubmenu(int index, PopupMenu submenu) => SetItemSubmenuNode(index, submenu);
        public void SetItemSubmenu(int index, string submenu) => SetItemSubmenuPath(index, submenu);
        public PopupMenu GetItemSubmenu(int index) => GetItemSubmenuNode(index);
        public void SetItemSubmenuPath(int index, string submenu)
        {
            var item = GetItem(index);
            item.SubmenuPath = submenu ?? string.Empty;
            if (!item.Separator)
            {
                if (!string.IsNullOrEmpty(item.SubmenuPath)) item.Kind = PopupMenuItemKind.Submenu;
                else if (item.Kind == PopupMenuItemKind.Submenu && item.Submenu == null) item.Kind = PopupMenuItemKind.Item;
            }
            ApplySearchFilter();
        }
        public string GetItemSubmenuPath(int index) => GetItem(index).SubmenuPath;
        public void SetItemSubmenuNode(int index, PopupMenu submenu)
        {
            if (submenu == null) throw new ArgumentNullException(nameof(submenu));
            var item = GetItem(index);
            PrepareSubmenu(submenu);
            item.Submenu = submenu;
            if (!item.Separator) item.Kind = PopupMenuItemKind.Submenu;
            ApplySearchFilter();
        }
        public PopupMenu GetItemSubmenuNode(int index) => GetItem(index).Submenu;
        public void SetItemAsSeparator(int index, bool separator)
        {
            var item = GetItem(index);
            item.Separator = separator;
            if (!separator && item.Kind == PopupMenuItemKind.Separator) item.Kind = item.CheckableType == PopupMenuCheckableType.Check ? PopupMenuItemKind.Check :
                item.CheckableType == PopupMenuCheckableType.Radio ? PopupMenuItemKind.RadioCheck :
                item.Submenu != null || !string.IsNullOrEmpty(item.SubmenuPath) ? PopupMenuItemKind.Submenu : PopupMenuItemKind.Item;
            ApplySearchFilter();
        }
        public bool IsItemSeparator(int index) => GetItem(index).Separator;
        public void SetItemIndex(int index, int targetIndex)
        {
            var from = NormalizeIndex(index);
            var to = NormalizeIndex(targetIndex);
            if (from == to) return;
            var item = _items[from];
            _items.RemoveAt(from);
            _items.Insert(to, item);
            if (_highlighted == from) _highlighted = to;
            else if (from < _highlighted && _highlighted <= to) _highlighted--;
            else if (to <= _highlighted && _highlighted < from) _highlighted++;
            if (_activeSubmenuIndex == from) _activeSubmenuIndex = to;
            else if (from < _activeSubmenuIndex && _activeSubmenuIndex <= to) _activeSubmenuIndex--;
            else if (to <= _activeSubmenuIndex && _activeSubmenuIndex < from) _activeSubmenuIndex++;
            ApplySearchFilter();
        }
        public void RemoveItem(int index)
        {
            var normalized = NormalizeIndex(index);
            if (_activeSubmenuIndex == normalized) CloseActiveSubmenu(PopupHideReason.Programmatic);
            _items.RemoveAt(normalized);
            if (_highlighted == normalized) _highlighted = -1;
            else if (_highlighted > normalized) _highlighted--;
            if (_activeSubmenuIndex > normalized) _activeSubmenuIndex--;
            if (_pendingSubmenuIndex == normalized) _pendingSubmenuIndex = -1;
            else if (_pendingSubmenuIndex > normalized) _pendingSubmenuIndex--;
            ApplySearchFilter();
        }
        public void SetItemAccelerator(int index, PopupMenuShortcut accelerator) { GetItem(index).Accelerator = accelerator; }
        public PopupMenuShortcut GetItemAccelerator(int index) => GetItem(index).Accelerator;
        public void SetItemShortcut(int index, PopupMenuShortcut shortcut, bool global = false)
        {
            var item = GetItem(index);
            item.Shortcut = shortcut;
            item.ShortcutIsGlobal = global;
        }
        public PopupMenuShortcut GetItemShortcut(int index) => GetItem(index).Shortcut;
        public void SetItemShortcutDisabled(int index, bool disabled) => GetItem(index).ShortcutDisabled = disabled;
        public bool IsItemShortcutDisabled(int index) => GetItem(index).ShortcutDisabled;
        public bool IsItemShortcutGlobal(int index) => GetItem(index).ShortcutIsGlobal;
        public void SetItemChecked(int index, bool checkedValue)
        {
            var item = GetItem(index);
            if (item.Checked == checkedValue) return;
            item.Checked = checkedValue;
            item.Indeterminate = false;
        }
        public bool IsItemChecked(int index) => GetItem(index).Checked;
        public void ToggleItemChecked(int index)
        {
            var item = GetItem(index);
            item.Checked = !item.Checked;
        }
        public void SetItemIndeterminate(int index, bool indeterminate)
        {
            var item = GetItem(index);
            if (item.Indeterminate == indeterminate) return;
            item.Indeterminate = indeterminate;
            item.Checked = false;
        }
        public bool IsItemIndeterminate(int index) => GetItem(index).Indeterminate;
        public void SetItemAsCheckable(int index, bool checkable)
        {
            var item = GetItem(index);
            item.CheckableType = checkable ? PopupMenuCheckableType.Check : PopupMenuCheckableType.None;
            if (item.Kind == PopupMenuItemKind.Item || item.Kind == PopupMenuItemKind.Check || item.Kind == PopupMenuItemKind.RadioCheck)
                item.Kind = checkable ? PopupMenuItemKind.Check : PopupMenuItemKind.Item;
        }
        public void SetItemAsRadioCheckable(int index, bool radioCheckable)
        {
            var item = GetItem(index);
            item.CheckableType = radioCheckable ? PopupMenuCheckableType.Radio : PopupMenuCheckableType.None;
            if (item.Kind == PopupMenuItemKind.Item || item.Kind == PopupMenuItemKind.Check || item.Kind == PopupMenuItemKind.RadioCheck)
                item.Kind = radioCheckable ? PopupMenuItemKind.RadioCheck : PopupMenuItemKind.Item;
        }
        public bool IsItemCheckable(int index) => GetItem(index).CheckableType != PopupMenuCheckableType.None;
        public bool IsItemRadioCheckable(int index) => GetItem(index).CheckableType == PopupMenuCheckableType.Radio;
        public void SetItemMaxStates(int index, int maxStates) { GetItem(index).MaxStates = maxStates; }
        public int GetItemMaxStates(int index) => GetItem(index).MaxStates;
        public void SetItemMultistate(int index, int state) { GetItem(index).State = state; }
        public void SetItemMultistateMax(int index, int maxStates) => SetItemMaxStates(index, maxStates);
        public int GetItemState(int index) => GetItem(index).State;
        public int GetItemMultistate(int index) => GetItem(index).State;
        public int GetItemMultistateMax(int index) => GetItem(index).MaxStates;
        public void ToggleItemMultistate(int index)
        {
            var item = GetItem(index);
            if (item.MaxStates <= 0) return;
            item.State++;
            if (item.State >= item.MaxStates) item.State = 0;
        }
        public void SetHideOnStateItemSelection(bool enabled) => HideOnMultistateItemSelection = enabled;
        public bool IsHideOnStateItemSelection() => HideOnMultistateItemSelection;
        public bool IsHideOnMultistateItemSelection() => HideOnMultistateItemSelection;
        public void SetHideOnItemSelection(bool enabled) => HideOnItemSelection = enabled;
        public bool IsHideOnItemSelection() => HideOnItemSelection;
        public void SetHideOnCheckableItemSelection(bool enabled) => HideOnCheckableItemSelection = enabled;
        public bool IsHideOnCheckableItemSelection() => HideOnCheckableItemSelection;
        public void SetSubmenuPopupDelay(float seconds)
        {
            if (seconds <= 0) seconds = 0.01f;
            SubmenuPopupDelay = TimeSpan.FromSeconds(seconds);
        }
        public float GetSubmenuPopupDelay() => (float)SubmenuPopupDelay.TotalSeconds;
        public void SetShrinkHeight(bool shrink) => ShrinkHeight = shrink;
        public bool GetShrinkHeight() => ShrinkHeight;
        public void SetShrinkWidth(bool shrink) => ShrinkWidth = shrink;
        public bool GetShrinkWidth() => ShrinkWidth;
        public void SetAllowSearch(bool allow) => AllowSearch = allow;
        public bool GetAllowSearch() => AllowSearch;
        public void SetSearchBarEnabled(bool enabled)
        {
            SearchBarEnabled = enabled;
            ApplySearchFilter();
        }
        public bool IsSearchBarEnabled() => SearchBarEnabled;
        public void SetSearchBarMinItemCount(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            SearchBarMinItemCount = count;
            ApplySearchFilter();
        }
        public int GetSearchBarMinItemCount() => SearchBarMinItemCount;
        public void SetSearchBarFuzzySearchEnabled(bool enabled)
        {
            SearchBarFuzzySearchEnabled = enabled;
            ApplySearchFilter();
        }
        public bool IsSearchBarFuzzySearchEnabled() => SearchBarFuzzySearchEnabled;
        public void SetSearchBarFuzzySearchMaxMisses(int maxMisses)
        {
            if (maxMisses < 0) throw new ArgumentOutOfRangeException(nameof(maxMisses));
            SearchBarFuzzySearchMaxMisses = maxMisses;
            ApplySearchFilter();
        }
        public int GetSearchBarFuzzySearchMaxMisses() => SearchBarFuzzySearchMaxMisses;
        public void SetSearchBarText(string text)
        {
            _searchBarText = text ?? string.Empty;
            _searchBarCaretColumn = _searchBarText.Length;
            ApplySearchFilter();
        }
        public string GetSearchBarText() => _searchBarText;
        public void SetSearchBarCaretColumn(int column)
        {
            if (column < 0 || column > _searchBarText.Length) throw new ArgumentOutOfRangeException(nameof(column));
            _searchBarCaretColumn = column;
        }
        public Rectangle GetSearchBarBounds()
        {
            if (!IsSearchBarVisible) return Rectangle.Empty;
            return new Rectangle(Bounds.X + 4, Bounds.Y + 4, Math.Max(0, Bounds.Width - 8), Math.Max(0, (int)SearchBarHeight - 4));
        }
        public Rectangle GetSearchBarClearButtonBounds()
        {
            if (!IsSearchBarVisible || string.IsNullOrEmpty(_searchBarText)) return Rectangle.Empty;
            var bounds = GetSearchBarBounds();
            var size = Math.Max(12, Math.Min(bounds.Height, 20));
            return new Rectangle(bounds.Right - size - 4, bounds.Y + Math.Max(0, (bounds.Height - size) / 2), size, size);
        }
        public void SetPreferNativeMenu(bool enabled) => PreferNativeMenu = enabled;
        public bool IsPreferNativeMenu() => PreferNativeMenu;
        public bool IsNativeMenu() => false;
        public void SetSystemMenu(PopupSystemMenu systemMenu) => SystemMenu = systemMenu;
        public PopupSystemMenu GetSystemMenu() => SystemMenu;
        public bool IsSystemMenu() => false;
        public void SetFocusedItem(int index)
        {
            if (index != -1 && (index < 0 || index >= _items.Count)) throw new ArgumentOutOfRangeException(nameof(index));
            FocusIndex(index);
            if (index != -1) ScrollToItem(index);
        }
        public void ScrollToItem(int index)
        {
            if (index < 0 || index >= _items.Count) throw new ArgumentOutOfRangeException(nameof(index));
            var itemTop = ItemContentTop(index);
            var itemHeight = (int)(_items[index].Separator ? 7 : EffectiveItemHeight);
            var relativeTop = itemTop - _scrollOffset;
            if (relativeTop < 0) SetScrollOffset(itemTop);
            else if (relativeTop + itemHeight > VisibleItemsHeight) SetScrollOffset(itemTop + itemHeight - VisibleItemsHeight);
        }
        public void PopupAt(Vector2 position, Vector2? minimumSize) => PopupAt(position, minimumSize, true);
        internal void PopupAt(Vector2 position, Vector2? minimumSize, bool focusFirst)
        {
            var requested = minimumSize ?? Vector2.Zero;
            var shrinkSize = new Vector2(
                Math.Max(Math.Max(CustomMinimumSize.X, requested.X), RequiredWidth),
                Math.Max(requested.Y, RequiredHeight));
            var popupSize = new Vector2(ShrinkWidth || Size.X <= 0 ? shrinkSize.X : Size.X, ShrinkHeight || Size.Y <= 0 ? shrinkSize.Y : Size.Y);
            if (Context != null)
            {
                if (Context.ViewportSize.X > 0) popupSize.X = Math.Min(popupSize.X, Context.ViewportSize.X);
                if (Context.ViewportSize.Y > 0) popupSize.Y = Math.Min(popupSize.Y, Context.ViewportSize.Y);
            }
            Size = popupSize;
            _scrollOffset = 0;
            base.PopupAt(position);
            if (focusFirst) SetFocusedItem(FirstEnabled(0, 1));
        }
        public override Vector2 GetMinimumSize() => Vector2.Max(CustomMinimumSize, new Vector2(RequiredWidth, RequiredHeight));
        /// <summary>
        /// Width needed for the widest item to render without its label and its shortcut colliding.
        /// The popup is otherwise only as wide as whatever the opener asked for, which leaves any
        /// item carrying an accelerator overlapping, since the shortcut is drawn right-aligned.
        /// </summary>
        private float RequiredWidth
        {
            get
            {
                var font = EffectiveUIFont;
                if (font == null) return 0;
                var width = 0f;
                foreach (var item in _items)
                {
                    if (!item.Visible || item.Separator) continue;
                    // Mirrors the draw pass: content starts past the check/icon gutter, and the
                    // shortcut is right-aligned inside a trailing inset.
                    var itemWidth = (float)ItemContentInset + (item.Indent * 16);
                    if (!string.IsNullOrEmpty(item.Text)) itemWidth += TextMetrics.Measure(font, item.Text).X;
                    var shortcutText = ItemShortcutText(item);
                    if (!string.IsNullOrEmpty(shortcutText))
                        itemWidth += ItemShortcutGap + TextMetrics.Measure(font, shortcutText).X;
                    else if (item.Kind == PopupMenuItemKind.Submenu) itemWidth += ItemShortcutGap;
                    width = Math.Max(width, itemWidth + ItemTrailingInset);
                }
                return width <= 0 ? 0 : width + 2;
            }
        }
        private const int ItemContentInset = 22;
        private const int ItemTrailingInset = 18;
        private const int ItemShortcutGap = 12;
        private static string ItemShortcutText(PopupMenuItem item)
            => item.Accelerator?.DisplayText
                ?? item.Shortcut?.DisplayText
                ?? (item.MaxStates > 0 ? $"{item.State}/{item.MaxStates - 1}" : string.Empty);
        private float RequiredHeight
        {
            get
            {
                var height = 0f;
                if (IsSearchBarVisible) height += SearchBarContentHeight;
                foreach (var item in _items)
                {
                    if (!item.Visible) continue;
                    height += item.Separator ? 7 : EffectiveItemHeight;
                }
                return height + 2;
            }
        }
        private PopupMenuItem Add(string text, int id, PopupMenuItemKind kind)
        {
            var item = new PopupMenuItem(text, id < 0 ? _items.Count : id, kind);
            if (kind == PopupMenuItemKind.Check) item.CheckableType = PopupMenuCheckableType.Check;
            else if (kind == PopupMenuItemKind.RadioCheck) item.CheckableType = PopupMenuCheckableType.Radio;
            item.Separator = kind == PopupMenuItemKind.Separator;
            _items.Add(item); ApplySearchFilter(); return item;
        }
        private int NormalizeIndex(int index)
        {
            if (index < 0) index += _items.Count;
            if (index < 0 || index >= _items.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return index;
        }
        private PopupMenuItem AddShortcutItem(PopupMenuShortcut shortcut, int id, PopupMenuItemKind kind, bool global)
        {
            if (shortcut == null) throw new ArgumentNullException(nameof(shortcut));
            var item = Add(shortcut.Name, id, kind);
            item.Shortcut = shortcut;
            item.ShortcutIsGlobal = global;
            return item;
        }
        private void PrepareSubmenu(PopupMenu submenu)
        {
            if (submenu == null) throw new ArgumentNullException(nameof(submenu));
            if (submenu.Parent != null && submenu.Parent != this) throw new InvalidOperationException("A submenu already attached to another parent cannot be assigned to this popup.");
            submenu.Visible = false;
            submenu.Modal = false;
            submenu.HideOnOutsideClick = false;
            submenu.ZIndex = Math.Max(submenu.ZIndex, 1);
            if (submenu.Parent != this) AddChild(submenu);
        }
        internal void HandleItemsPressed(Point point)
        {
            base.PointerPressed(point);
            if (IsSearchBarVisible && GetSearchBarBounds().Contains(point))
            {
                _searchBarFocused = true;
                FocusIndex(-1);
                if (GetSearchBarClearButtonBounds().Contains(point)) SetSearchBarText(string.Empty);
                else _searchBarCaretColumn = _searchBarText.Length;
                return;
            }
            _searchBarFocused = false;
            FocusIndex(ItemAt(point));
        }
        internal void HandleItemsReleased(Point point, bool isInside)
        {
            if (IsSearchBarVisible && GetSearchBarBounds().Contains(point)) return;
            var index = isInside ? ItemAt(point) : -1;
            if (index < 0) return;
            Activate(index, point);
        }
        internal bool HandleItemsWheel(int delta)
        {
            if (delta == 0 || MaxScrollOffset <= 0) return false;
            SetScrollOffset(_scrollOffset - Math.Sign(delta) * Math.Max(1, (int)EffectiveItemHeight * 3));
            return true;
        }
        protected override void ArrangeChildren()
        {
            base.ArrangeChildren();
            ItemsControl.Position = Vector2.Zero;
            ItemsControl.Size = Size;
        }
        internal override void KeyPressed(Microsoft.Xna.Framework.Input.Keys key)
        {
            if (HandleSearchBarKey(key)) return;
            if (key == Microsoft.Xna.Framework.Input.Keys.Down) SetFocusedItem(FirstEnabled(_highlighted + 1, 1));
            else if (key == Microsoft.Xna.Framework.Input.Keys.Up) SetFocusedItem(FirstEnabled(_highlighted - 1, -1));
            else if (key == Microsoft.Xna.Framework.Input.Keys.Right)
            {
                if (!OpenSubmenu(_highlighted, true) && OwnerMenuButton?.Parent is MenuBar menuBar)
                    menuBar.OpenAdjacentMenu(OwnerMenuButton, 1, true);
            }
            else if (key == Microsoft.Xna.Framework.Input.Keys.Left && Parent is PopupMenu parentMenu)
            {
                Hide(PopupHideReason.Programmatic);
                parentMenu._activeSubmenuIndex = -1;
                parentMenu.GrabFocus();
            }
            else if (key == Microsoft.Xna.Framework.Input.Keys.Left && OwnerMenuButton?.Parent is MenuBar menuBar)
                menuBar.OpenAdjacentMenu(OwnerMenuButton, -1, true);
            else if (key == Microsoft.Xna.Framework.Input.Keys.Enter || key == Microsoft.Xna.Framework.Input.Keys.Space) Activate(_highlighted, new Point(Bounds.X, Bounds.Y));
            else if (key == Microsoft.Xna.Framework.Input.Keys.Escape && IsSearchBarVisible && _searchBarText.Length > 0) SetSearchBarText(string.Empty);
            else if (key == Microsoft.Xna.Framework.Input.Keys.Escape) HideMenuTree(PopupHideReason.Cancelled);
            else if (Context != null) ActivateItemByShortcut(key, Context.CurrentKeyboardState);
        }
        internal override void TextInput(char character)
        {
            if (IsSearchBarVisible)
            {
                _searchBarFocused = true;
                if (!char.IsControl(character))
                {
                    _searchBarText = _searchBarText.Insert(_searchBarCaretColumn, character.ToString());
                    _searchBarCaretColumn++;
                    ApplySearchFilter();
                }
                return;
            }
            if (!AllowSearch) return;
            var now = Context?.CurrentTime ?? TimeSpan.Zero;
            if (_lastSearchTime == TimeSpan.MinValue || now - _lastSearchTime > IncrementalSearchTimeout)
                _searchString = string.Empty;
            _lastSearchTime = now;
            var next = character.ToString();
            if (!string.Equals(next, _searchString, StringComparison.OrdinalIgnoreCase))
                _searchString += next;
            FocusSearchMatch(_searchString);
        }
        private bool HandleSearchBarKey(Keys key)
        {
            if (!IsSearchBarVisible) return false;
            if (key == Keys.Back)
            {
                if (_searchBarCaretColumn <= 0) return true;
                _searchBarText = _searchBarText.Remove(_searchBarCaretColumn - 1, 1);
                _searchBarCaretColumn--;
                ApplySearchFilter();
                return true;
            }
            if (key == Keys.Delete)
            {
                if (_searchBarCaretColumn >= _searchBarText.Length)
                {
                    if (_searchBarText.Length == 0) return true;
                    SetSearchBarText(string.Empty);
                    return true;
                }
                _searchBarText = _searchBarText.Remove(_searchBarCaretColumn, 1);
                ApplySearchFilter();
                return true;
            }
            if (!_searchBarFocused) return false;
            if (key == Keys.Left)
            {
                _searchBarCaretColumn = Math.Max(0, _searchBarCaretColumn - 1);
                return true;
            }
            if (key == Keys.Right)
            {
                _searchBarCaretColumn = Math.Min(_searchBarText.Length, _searchBarCaretColumn + 1);
                return true;
            }
            if (key == Keys.Home)
            {
                _searchBarCaretColumn = 0;
                return true;
            }
            if (key == Keys.End)
            {
                _searchBarCaretColumn = _searchBarText.Length;
                return true;
            }
            return false;
        }
        internal int ItemAt(Point point)
        {
            if (!Bounds.Contains(point)) return -1;
            var y = point.Y - Bounds.Y - 1 + _scrollOffset;
            if (IsSearchBarVisible)
            {
                if (y - _scrollOffset < SearchBarContentHeight) return -1;
                y -= (int)SearchBarContentHeight;
            }
            for (var index = 0; index < _items.Count; index++)
            {
                if (!_items[index].Visible) continue;
                var height = _items[index].Separator ? 7 : EffectiveItemHeight;
                if (y >= 0 && y < height) return _items[index].Separator ? -1 : index;
                y -= (int)height;
            }
            return -1;
        }
        private int FirstEnabled(int index, int delta)
        {
            if (_items.Count == 0) return -1;
            for (var i = 0; i < _items.Count; i++)
            {
                var candidate = (index + _items.Count) % _items.Count;
                var item = _items[candidate];
                if (item.Visible && !item.Disabled && !item.Separator) return candidate;
                index = candidate + delta;
            }
            return -1;
        }
        private void FocusIndex(int index)
        {
            if (_highlighted == index) return;
            if (_activeSubmenuIndex >= 0 && _activeSubmenuIndex != index) CloseActiveSubmenu(PopupHideReason.Programmatic);
            if (_pendingSubmenuIndex != index) _pendingSubmenuIndex = -1;
            _highlighted = index;
            if (index >= 0) IndexFocused?.Invoke(this, index);
        }
        private void FocusSearchMatch(string query)
        {
            if (string.IsNullOrEmpty(query) || _items.Count == 0) return;
            for (var step = 1; step <= _items.Count; step++)
            {
                var index = (_highlighted + step + _items.Count) % _items.Count;
                if (_items[index].Visible && _items[index].Text.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                {
                    SetFocusedItem(index);
                    return;
                }
            }
        }
        internal override void Process(GameTime gameTime)
        {
            base.Process(gameTime);
            if (!Visible || Context == null) return;
            if (_activeSubmenuIndex >= 0 && (_activeSubmenuIndex >= _items.Count || _items[_activeSubmenuIndex].Submenu?.Visible != true))
                _activeSubmenuIndex = -1;
            var pointer = Context.PointerPosition;
            if (!Bounds.Contains(pointer)) return;
            var index = ItemAt(pointer);
            FocusIndex(index);
            if (index < 0 || index >= _items.Count || _items[index].Kind != PopupMenuItemKind.Submenu || _items[index].Submenu == null || _items[index].Disabled)
            {
                _pendingSubmenuIndex = -1;
                return;
            }
            if (_activeSubmenuIndex == index && _items[index].Submenu.Visible) return;
            if (_pendingSubmenuIndex != index)
            {
                _pendingSubmenuIndex = index;
                _pendingSubmenuStarted = Context.CurrentTime;
                return;
            }
            var delay = SubmenuPopupDelay < TimeSpan.Zero ? TimeSpan.Zero : SubmenuPopupDelay;
            if (Context.CurrentTime - _pendingSubmenuStarted >= delay) OpenSubmenu(index, false);
        }
        private void Activate(int index, Point point)
        {
            if (index < 0 || index >= _items.Count) return;
            var item = _items[index];
            if (!item.Visible || item.Disabled || item.Separator) return;
            if (item.Kind == PopupMenuItemKind.Submenu)
            {
                OpenSubmenu(index, false);
                return;
            }
            IndexPressed?.Invoke(this, index);
            IdPressed?.Invoke(this, item.Id);
            var checkable = item.CheckableType != PopupMenuCheckableType.None;
            var multistate = item.MaxStates > 0;
            if (checkable ? HideOnCheckableItemSelection : multistate ? HideOnMultistateItemSelection : HideOnItemSelection)
                HideMenuTree(PopupHideReason.Programmatic);
        }
        public bool ActivateItemByShortcut(Keys key, KeyboardState keyboard, bool globalOnly = false)
        {
            for (var index = 0; index < _items.Count; index++)
            {
                var item = _items[index];
                if (!item.Visible || item.Disabled || item.ShortcutDisabled || item.Separator) continue;
                if (item.Accelerator != null && item.Accelerator.Matches(key, keyboard))
                {
                    Activate(index, new Point(Bounds.X, Bounds.Y));
                    return true;
                }
                if (item.Shortcut != null && item.Shortcut.Matches(key, keyboard) && (item.ShortcutIsGlobal || !globalOnly))
                {
                    Activate(index, new Point(Bounds.X, Bounds.Y));
                    return true;
                }
                if (item.Submenu != null && item.Submenu.ActivateItemByShortcut(key, keyboard, globalOnly)) return true;
            }
            return false;
        }
        private bool OpenSubmenu(int index, bool byKeyboard)
        {
            if (index < 0 || index >= _items.Count) return false;
            var item = _items[index];
            if (!item.Visible || item.Disabled || item.Kind != PopupMenuItemKind.Submenu || item.Submenu == null) return false;
            if (_activeSubmenuIndex >= 0 && _activeSubmenuIndex != index) CloseActiveSubmenu(PopupHideReason.Programmatic);
            PrepareSubmenu(item.Submenu);
            var submenuWidth = Math.Max(Size.X, item.Submenu.CustomMinimumSize.X);
            var globalX = IsLayoutRtl() ? Bounds.Left - submenuWidth + 1 : Bounds.Right - 1;
            if (Context?.ViewportSize.X > 0)
            {
                if (!IsLayoutRtl() && globalX + submenuWidth > Context.ViewportSize.X) globalX = Bounds.Left - submenuWidth + 1;
                else if (IsLayoutRtl() && globalX < 0) globalX = Bounds.Right - 1;
            }
            var localX = globalX - Bounds.X;
            item.Submenu.PopupAt(new Vector2(localX, ItemTop(index)), new Vector2(submenuWidth, 0), byKeyboard);
            _activeSubmenuIndex = index;
            _pendingSubmenuIndex = -1;
            if (byKeyboard) item.Submenu.GrabFocus();
            return true;
        }
        private void CloseActiveSubmenu(PopupHideReason reason)
        {
            if (_activeSubmenuIndex >= 0 && _activeSubmenuIndex < _items.Count)
            {
                var submenu = _items[_activeSubmenuIndex].Submenu;
                submenu?.CloseActiveSubmenu(reason);
                submenu?.Hide(reason);
            }
            _activeSubmenuIndex = -1;
            _pendingSubmenuIndex = -1;
        }
        private void HideMenuTree(PopupHideReason reason)
        {
            var root = this;
            while (root.Parent is PopupMenu parentMenu) root = parentMenu;
            root.Hide(reason);
        }
        private int ItemTop(int index)
        {
            return 1 + (IsSearchBarVisible ? (int)SearchBarContentHeight : 0) + ItemContentTop(index) - _scrollOffset;
        }
        private int ItemContentTop(int index)
        {
            var y = 0;
            for (var i = 0; i < index && i < _items.Count; i++)
            {
                if (!_items[i].Visible) continue;
                y += (int)(_items[i].Separator ? 7 : EffectiveItemHeight);
            }
            return y;
        }
        private int ItemContentHeight
        {
            get
            {
                var height = 0;
                foreach (var item in _items)
                    if (item.Visible) height += (int)(item.Separator ? 7 : EffectiveItemHeight);
                return height;
            }
        }
        private int VisibleItemsHeight => Math.Max(0, Bounds.Height - 2 - (IsSearchBarVisible ? (int)SearchBarContentHeight : 0));
        private int MaxScrollOffset => Math.Max(0, ItemContentHeight - VisibleItemsHeight);
        private void SetScrollOffset(int offset) => _scrollOffset = Math.Max(0, Math.Min(offset, MaxScrollOffset));
        private int SearchableItemCount
        {
            get
            {
                var count = 0;
                foreach (var item in _items)
                    if (!item.Separator) count++;
                return count;
            }
        }
        private float SearchBarContentHeight => SearchBarHeight + SearchBarSeparation;
        internal static float GetItemStateIconY(TextLayout textLayout, float textY, Rectangle row, float iconHeight)
        {
            if (textLayout == null) throw new ArgumentNullException(nameof(textLayout));
            if (textLayout.VisibleGlyphs.Count == 0) return row.Center.Y - iconHeight / 2;
            var top = float.MaxValue;
            var bottom = float.MinValue;
            foreach (var glyph in textLayout.VisibleGlyphs)
            {
                top = Math.Min(top, glyph.Bounds.Top);
                bottom = Math.Max(bottom, glyph.Bounds.Bottom);
            }
            return textY + (top + bottom - iconHeight) / 2;
        }
        private void ApplySearchFilter()
        {
            ApplySearchFilterForQuery(IsSearchBarVisible ? _searchBarText : string.Empty);
        }
        private void ApplySearchFilterForQuery(string query)
        {
            query ??= string.Empty;

            foreach (var item in _items)
                item.Submenu?.ApplySearchFilterForQuery(query);

            if (string.IsNullOrEmpty(query))
            {
                foreach (var item in _items)
                    item.Visible = true;
                QueueLayout();
                return;
            }

            foreach (var item in _items)
            {
                item.Visible = false;
                if (item.Submenu == null) continue;
                foreach (var submenuItem in item.Submenu._items)
                {
                    if (!submenuItem.Visible) continue;
                    item.Visible = true;
                    break;
                }
            }

            foreach (var item in _items)
            {
                if (item.Separator || !MatchesSearchFilter(item.Text, query)) continue;
                item.Visible = true;
                if (item.Submenu == null) continue;
                foreach (var submenuItem in item.Submenu._items)
                    submenuItem.Visible = true;
            }

            if (_highlighted >= 0 && (_highlighted >= _items.Count || !_items[_highlighted].Visible))
                FocusIndex(FirstEnabled(0, 1));
            if (_activeSubmenuIndex >= 0 && (_activeSubmenuIndex >= _items.Count || !_items[_activeSubmenuIndex].Visible))
                CloseActiveSubmenu(PopupHideReason.Programmatic);
            if (_pendingSubmenuIndex >= 0 && (_pendingSubmenuIndex >= _items.Count || !_items[_pendingSubmenuIndex].Visible))
                _pendingSubmenuIndex = -1;
            QueueLayout();
        }
        private bool MatchesSearchFilter(string text, string query)
        {
            text ??= string.Empty;
            query ??= string.Empty;
            if (query.Length == 0) return true;
            if (!SearchBarFuzzySearchEnabled)
            {
                var start = 0;
                while (start < query.Length)
                {
                    while (start < query.Length && char.IsWhiteSpace(query[start])) start++;
                    if (start >= query.Length) break;
                    var end = start;
                    while (end < query.Length && !char.IsWhiteSpace(query[end])) end++;
                    var token = query.Substring(start, end - start);
                    if (text.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0) return false;
                    start = end;
                }
                return true;
            }

            var textIndex = 0;
            var misses = 0;
            for (var queryIndex = 0; queryIndex < query.Length; queryIndex++)
            {
                var queryChar = query[queryIndex];
                if (char.IsWhiteSpace(queryChar)) continue;
                var matched = false;
                while (textIndex < text.Length)
                {
                    if (char.ToUpperInvariant(text[textIndex++]) != char.ToUpperInvariant(queryChar)) continue;
                    matched = true;
                    break;
                }
                if (!matched && ++misses > SearchBarFuzzySearchMaxMisses) return false;
            }
            return true;
        }
        internal void DrawMenuContent(UIRenderContext context)
        {
            var y = Bounds.Y + 1;
            if (IsSearchBarVisible)
            {
                var searchRect = GetSearchBarBounds();
                context.Fill(searchRect, context.Theme.BackgroundColor);
                context.Border(searchRect, _searchBarFocused ? context.Theme.FocusColor : context.Theme.PanelBorderColor);
                if (EffectiveUIFont != null)
                {
                    var text = string.IsNullOrEmpty(_searchBarText) ? "Search" : _searchBarText;
                    var color = string.IsNullOrEmpty(_searchBarText) ? context.Theme.DisabledTextColor : context.Theme.TextColor;
                    var textPosition = new Vector2(searchRect.X + 8, searchRect.Y + Math.Max(2, (searchRect.Height - TextMetrics.LineHeight(EffectiveUIFont)) / 2));
                    context.Text(EffectiveUIFont, text, textPosition, color);
                    if (_searchBarFocused)
                    {
                        var layout = TextMetrics.Layout(EffectiveUIFont, _searchBarText);
                        var caretX = textPosition.X + layout.GetCaretPosition(Math.Min(_searchBarCaretColumn, _searchBarText.Length)).X;
                        context.Fill(new Rectangle((int)MathF.Round(caretX), searchRect.Y + 4, 1, Math.Max(4, searchRect.Height - 8)), context.Theme.FocusColor);
                    }
                    if (!string.IsNullOrEmpty(_searchBarText))
                    {
                        var clearBounds = GetSearchBarClearButtonBounds();
                        var clearIcon = GetThemeIcon("clear");
                        if (clearIcon.HasValue)
                            context.Icon(clearIcon.Value, new Vector2(
                                clearBounds.Center.X - clearIcon.Value.LogicalSize.X / 2,
                                clearBounds.Center.Y - clearIcon.Value.LogicalSize.Y / 2), context.Theme.DisabledTextColor);
                    }
                }
                y += (int)SearchBarContentHeight;
            }
            y -= _scrollOffset;
            var itemHeight = EffectiveItemHeight;
            var itemsBounds = new Rectangle(Bounds.X + 1, Bounds.Y + 1 + (IsSearchBarVisible ? (int)SearchBarContentHeight : 0), Math.Max(0, Bounds.Width - 2), VisibleItemsHeight);
            context.PushClip(itemsBounds);
            try
            {
                for (var index = 0; index < _items.Count; index++)
                {
                    var item = _items[index];
                    if (!item.Visible) continue;
                    if (item.Separator)
                    {
                        context.Fill(new Rectangle(Bounds.X + 5, y + 3, Math.Max(0, Bounds.Width - 10), 1), context.Theme.PanelBorderColor);
                        y += 7;
                        continue;
                    }
                    var rect = new Rectangle(Bounds.X + 1, y, Math.Max(0, Bounds.Width - 2), (int)itemHeight);
                    var itemTextLayout = EffectiveUIFont == null || string.IsNullOrEmpty(item.Text)
                        ? null
                        : TextMetrics.Layout(EffectiveUIFont, item.Text);
                    var itemTextY = EffectiveUIFont == null ? rect.Y : rect.Y + Math.Max(2, (itemHeight - TextMetrics.LineHeight(EffectiveUIFont)) / 2);
                    if (index == _highlighted) context.Fill(rect, context.Theme.HoverColor);
                    if (item.CheckableType != PopupMenuCheckableType.None)
                    {
                        var state = item.Indeterminate ? "indeterminate" : item.CheckableType == PopupMenuCheckableType.Radio ? (item.Checked ? "radio_checked" : "radio_unchecked") : item.Checked ? "checked" : "unchecked";
                        if (item.Disabled) state += "_disabled";
                        var stateIcon = GetThemeIcon(state);
                        if (stateIcon.HasValue)
                        {
                            var iconY = itemTextLayout == null
                                ? rect.Center.Y - stateIcon.Value.LogicalSize.Y / 2
                                : GetItemStateIconY(itemTextLayout, itemTextY, rect, stateIcon.Value.LogicalSize.Y);
                            context.Icon(stateIcon.Value, new Vector2(rect.X + 4, iconY), Color.White);
                        }
                    }
                    var contentX = rect.X + 22 + item.Indent * 16;
                    if (item.Icon != null)
                    {
                        var iconWidth = item.IconMaxWidth > 0 ? Math.Min(item.Icon.Width, item.IconMaxWidth) : item.Icon.Width;
                        var iconHeight = item.Icon.Height;
                        var maxHeight = Math.Max(1, (int)itemHeight - 4);
                        var scale = Math.Min(1f, Math.Min(iconWidth / (float)Math.Max(1, item.Icon.Width), maxHeight / (float)Math.Max(1, item.Icon.Height)));
                        iconWidth = Math.Max(1, (int)MathF.Round(item.Icon.Width * scale));
                        iconHeight = Math.Max(1, (int)MathF.Round(item.Icon.Height * scale));
                        var iconRect = new Rectangle(contentX, rect.Y + Math.Max(0, ((int)itemHeight - iconHeight) / 2), iconWidth, iconHeight);
                        context.SpriteBatch.Draw(item.Icon, iconRect, item.Disabled ? context.Theme.DisabledTextColor : item.IconModulate);
                        contentX = iconRect.Right + 4;
                    }
                    if (itemTextLayout != null) context.Text(itemTextLayout, new Vector2(contentX, itemTextY), item.Disabled ? context.Theme.DisabledTextColor : context.Theme.TextColor);
                    var shortcutText = ItemShortcutText(item);
                    if (EffectiveUIFont != null && !string.IsNullOrEmpty(shortcutText))
                    {
                        var textSize = TextMetrics.Measure(EffectiveUIFont, shortcutText);
                        context.Text(EffectiveUIFont, shortcutText, new Vector2(rect.Right - 18 - textSize.X, rect.Y + Math.Max(2, (itemHeight - TextMetrics.LineHeight(EffectiveUIFont)) / 2)), item.Disabled ? context.Theme.DisabledTextColor : context.Theme.TextColor);
                    }
                    if (item.Kind == PopupMenuItemKind.Submenu)
                    {
                        var submenu = GetThemeIcon(IsLayoutRtl() ? "submenu_mirrored" : "submenu");
                        if (submenu.HasValue) context.Icon(submenu.Value, new Vector2(rect.Right - 4 - submenu.Value.LogicalSize.X, rect.Center.Y - submenu.Value.LogicalSize.Y / 2), item.Disabled ? context.Theme.DisabledTextColor : Color.White);
                    }
                    y += (int)itemHeight;
                }
            }
            finally
            {
                context.PopClip();
            }
        }
    }

    /// <summary>A button that opens its owned <see cref="PopupMenu"/> underneath itself.</summary>
    public sealed class MenuButton : BaseButton
    {
        public MenuButton()
        {
            Menu = new PopupMenu { Visible = false };
            Menu.OwnerMenuButton = this;
            ToggleMode = true;
            Flat = true;
            ActionMode = ButtonActionMode.Press;
            Menu.PopupShown += (_, _) => { SetPressedNoSignal(true); PopupShown?.Invoke(this, EventArgs.Empty); };
            Menu.PopupHidden += (_, reason) => { SetPressedNoSignal(false); PopupHidden?.Invoke(this, reason); };
            Pressed += (_, _) => { if (Menu.Visible) Menu.Hide(PopupHideReason.Programmatic); else ShowPopup(); };
        }
        public PopupMenu Menu { get; }
        /// <summary>Whether an already-open sibling menu switches to this button when it is hovered.</summary>
        public bool SwitchOnHover { get; set; }
        /// <summary>Retained Godot shortcut-routing state. It is exposed for applications that provide a shortcut dispatcher.</summary>
        public bool DisableShortcuts { get; set; }
        public int ItemCount { get => Menu.Items.Count; set => Menu.SetItemCount(value); }
        public event EventHandler AboutToPopup;
        public event EventHandler PopupShown;
        public event Action<MenuButton, PopupHideReason> PopupHidden;
        public void ShowPopup() => ShowPopup(true);
        internal void ShowPopup(bool focusFirstItem)
        {
            if (Context == null) return;
            if (Menu.Context != Context) Context.Add(Menu);
            AboutToPopup?.Invoke(this, EventArgs.Empty);
            var width = Math.Max(Bounds.Width, Menu.CustomMinimumSize.X);
            var x = IsLayoutRtl() ? Bounds.Right - width : Bounds.Left;
            Menu.PopupAt(new Vector2(x, Bounds.Bottom), new Vector2(width, 0), focusFirstItem);
        }
        internal override void PointerEntered()
        {
            base.PointerEntered();
            if (!SwitchOnHover || !Enabled || Menu.Visible || Parent == null) return;
            foreach (var sibling in Parent.Children)
            {
                if (sibling is not MenuButton button || button == this || !button.Menu.Visible) continue;
                button.Menu.Hide(PopupHideReason.Programmatic);
                ShowPopup();
                break;
            }
        }
    }

    /// <summary>Simple horizontal menu strip that owns menu buttons and opens one popup at a time.</summary>
    public sealed class MenuBar : TemplatedControl
    {
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.MenuBar;
        private BoxAlignment _alignment;
        private bool _reverseSort;
        public MenuBar() { Separation = 0; }
        public Orientation Orientation => Orientation.Horizontal;
        public float Separation { get; set; }
        public BoxAlignment Alignment { get => _alignment; set { _alignment = value; QueueLayout(); } }
        public bool ReverseSort { get => _reverseSort; set { _reverseSort = value; QueueLayout(); } }
        public Control AddSpacer(bool begin = false)
        {
            var spacer = new Control { MouseFilter = MouseFilter.Pass, HorizontalSizeFlags = SizeFlags.Expand | SizeFlags.Fill };
            AddChild(spacer);
            if (begin) MoveChild(spacer, 0);
            return spacer;
        }
        /// <summary>Gates the bar's own accelerator/shortcut activation across every child menu, matching Godot's MenuBar.disable_shortcuts.</summary>
        public bool DisableShortcuts { get; set; }
        /// <summary>Activates a matching item's accelerator/shortcut in any visible, enabled child menu without opening it, mirroring Godot's MenuBar::shortcut_input. Unlike PopupMenu's own routing, this fires even while every menu is closed.</summary>
        internal override bool ShortcutInput(Keys key, KeyboardState keyboard)
        {
            if (!DisableShortcuts)
                foreach (var child in Children)
                    if (child is MenuButton button && button.Visible && button.Enabled && button.Menu.ActivateItemByShortcut(key, keyboard))
                        return true;
            return base.ShortcutInput(key, keyboard);
        }
        public MenuButton AddMenu(string text)
        {
            var button = new MenuButton { Text = text ?? string.Empty, CustomMinimumSize = new Vector2(50, 26), HorizontalSizeFlags = SizeFlags.ShrinkBegin };
            button.Pressed += (_, _) => CloseOtherMenus(button);
            AddChild(button);
            return button;
        }
        private void CloseOtherMenus(MenuButton selected)
        {
            foreach (var child in Children)
                if (child is MenuButton button && button != selected) button.Menu.Hide();
        }
        internal bool OpenAdjacentMenu(MenuButton current, int direction, bool focusFirstItem)
        {
            var buttons = new List<MenuButton>();
            foreach (var child in Children)
                if (child is MenuButton button && button.Visible && button.Enabled) buttons.Add(button);
            if (buttons.Count <= 1) return false;
            var currentIndex = buttons.IndexOf(current);
            if (currentIndex < 0) return false;
            var nextIndex = (currentIndex + direction + buttons.Count) % buttons.Count;
            var next = buttons[nextIndex];
            if (next == current) return false;
            current.Menu.Hide(PopupHideReason.Programmatic);
            next.ShowPopup(focusFirstItem);
            return true;
        }
        internal override void Process(GameTime gameTime)
        {
            base.Process(gameTime);
            if (Context == null) return;
            MenuButton open = null;
            foreach (var child in Children) if (child is MenuButton button && button.Menu.Visible) { open = button; break; }
            if (open == null) return;
            foreach (var child in Children)
            {
                if (child is not MenuButton button || button == open || !button.SwitchOnHover || !button.Enabled || !button.ContainsPoint(Context.PointerPosition)) continue;
                open.Menu.Hide(PopupHideReason.Programmatic);
                button.ShowPopup();
                break;
            }
        }
    }

    /// <summary>Popup dialog with explicit accepted/cancelled lifecycle events.</summary>
    [TemplatePart(TitlePresenterPartName, typeof(Label))]
    [TemplatePart(ContentControl.ContentPresenterPartName, typeof(Label))]
    [TemplatePart(AcceptButtonPartName, typeof(BaseButton))]
    [TemplatePart(CancelButtonPartName, typeof(BaseButton), false)]
    public class AcceptDialog : PopupPanel
    {
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.Dialog;
        public const string TitlePresenterPartName = "PART_TitlePresenter";
        public const string AcceptButtonPartName = "PART_AcceptButton";
        public const string CancelButtonPartName = "PART_CancelButton";
        private readonly UIFontSelection _fontSelection = new UIFontSelection();
        private sealed class DialogButtonEntry
        {
            public Button Button;
            public bool Right;
            public bool IsCancel;
            public EventHandler PressedHandler;
        }

        private readonly List<DialogButtonEntry> _dialogButtons = new List<DialogButtonEntry>();
        private Label _templateTitlePresenter;
        private Label _templateContentPresenter;
        private BaseButton _templateAcceptButton;
        private BaseButton _templateCancelButton;
        public string Title { get; set; } = string.Empty;
        public string DialogText { get; set; } = string.Empty;
        private string _customOkText = string.Empty;
        private string _defaultOkText = "OK";
        /// <summary>Matches Godot's ok_text/default_ok_text layering: a custom override always wins over
        /// the current default (which FileDialog changes per file-mode via <see cref="DefaultOkText"/>);
        /// clearing back to empty/null reverts to showing the default again.</summary>
        public string OkText
        {
            get => string.IsNullOrEmpty(_customOkText) ? _defaultOkText : _customOkText;
            set => _customOkText = value ?? string.Empty;
        }
        /// <summary>The fallback OK button text shown while no custom <see cref="OkText"/> override is set.</summary>
        protected string DefaultOkText { get => _defaultOkText; set => _defaultOkText = value ?? string.Empty; }
        public SpriteFont Font { get => _fontSelection.SpriteFont; set { if (_fontSelection.SetSpriteFont(value)) QueueLayout(); } }
        public UIFont UIFont { get => _fontSelection.UIFont; set { if (_fontSelection.SetUIFont(value)) QueueLayout(); } }
        internal UIFont EffectiveUIFont => ResolveFont(_fontSelection);
        public float ButtonHeight { get; set; } = 24;
        /// <summary>Matches Godot's dialog_hide_on_ok option. Confirmation still emits when disabled.</summary>
        public bool HideOnOk { get; set; } = true;
        public bool CloseOnEscape { get; set; } = true;
        /// <summary>Matches Godot's get_ok_button()->is_disabled(): blocks confirming via click, Enter,
        /// or a registered LineEdit's text_submitted, without a real button-widget model.</summary>
        public bool OkButtonDisabled { get; set; }
        public event EventHandler Confirmed;
        public event EventHandler Canceled;
        public event Action<AcceptDialog, string> CustomAction;
        public override Vector2 GetMinimumSize()
        {
            var buttonWidth = 76f;
            foreach (var entry in _dialogButtons) buttonWidth += GetDialogButtonWidth(entry.Button) + 8;
            if (HasCancelButton) buttonWidth += 72;
            return Vector2.Max(CustomMinimumSize, new Vector2(Math.Max(180, buttonWidth), 82));
        }
        public virtual void Confirm()
        {
            // Godot's AcceptDialog::_ok_pressed hides first (when hide_on_ok), then emits confirmed.
            if (HideOnOk) Hide();
            Confirmed?.Invoke(this, EventArgs.Empty);
        }
        public virtual void Cancel() { Canceled?.Invoke(this, EventArgs.Empty); Hide(); }
        /// <summary>Wires a LineEdit's Enter/TextSubmitted to confirm this dialog, matching Godot's
        /// register_text_enter - guarded by OkButtonDisabled exactly like Godot's _text_submitted.</summary>
        public void RegisterTextEnter(LineEdit lineEdit)
        {
            if (lineEdit == null) throw new ArgumentNullException(nameof(lineEdit));
            lineEdit.TextSubmitted += (_, _) => { if (!OkButtonDisabled) Confirm(); };
        }
        public Button AddButton(string text, bool right = false, string action = "")
        {
            var button = new Button { Text = text ?? string.Empty, Font = Font };
            EventHandler handler = null;
            if (!string.IsNullOrEmpty(action))
            {
                handler = (_, _) => CustomAction?.Invoke(this, action);
                button.Pressed += handler;
            }
            _dialogButtons.Add(new DialogButtonEntry { Button = button, Right = right, PressedHandler = handler });
            AddChild(button);
            return button;
        }
        public Button AddCancelButton(string text = "")
        {
            var button = AddButton(string.IsNullOrEmpty(text) ? "Cancel" : text);
            var entry = _dialogButtons[_dialogButtons.Count - 1];
            entry.IsCancel = true;
            EventHandler cancelHandler = (_, _) => Cancel();
            button.Pressed += cancelHandler;
            entry.PressedHandler += cancelHandler;
            return button;
        }
        public void RemoveButton(Button button)
        {
            if (button == null) throw new ArgumentNullException(nameof(button));
            var index = _dialogButtons.FindIndex(entry => ReferenceEquals(entry.Button, button));
            if (index < 0 || !ReferenceEquals(button.Parent, this)) throw new ArgumentException("The button does not belong to this dialog.", nameof(button));
            var entry = _dialogButtons[index];
            if (entry.PressedHandler != null) button.Pressed -= entry.PressedHandler;
            _dialogButtons.RemoveAt(index);
            RemoveChild(button);
        }
        protected override void ArrangeChildren()
        {
            base.ArrangeChildren();
            SynchronizeTemplateParts();
            var y = Math.Max(0, Size.Y - ButtonHeight - 8);
            var left = 8f;
            foreach (var entry in _dialogButtons)
            {
                if (entry.Right || !entry.Button.Visible) continue;
                var width = GetDialogButtonWidth(entry.Button);
                entry.Button.Font = Font;
                entry.Button.Position = new Vector2(left, y);
                entry.Button.Size = new Vector2(width, ButtonHeight);
                left += width + 8;
            }
            var right = (HasCancelButton ? CancelButtonBounds.Left : OkButtonBounds.Left) - Bounds.Left - 8f;
            for (var index = _dialogButtons.Count - 1; index >= 0; index--)
            {
                var entry = _dialogButtons[index];
                if (!entry.Right && !entry.IsCancel || !entry.Button.Visible) continue;
                var width = GetDialogButtonWidth(entry.Button);
                right -= width;
                entry.Button.Font = Font;
                entry.Button.Position = new Vector2(right, y);
                entry.Button.Size = new Vector2(width, ButtonHeight);
                right -= 8;
            }
        }
        protected override void OnTemplateApplied()
        {
            if (_templateAcceptButton != null) _templateAcceptButton.Pressed -= HandleTemplateAcceptPressed;
            if (_templateCancelButton != null) _templateCancelButton.Pressed -= HandleTemplateCancelPressed;
            _templateTitlePresenter = GetTemplateChild(TitlePresenterPartName) as Label;
            _templateContentPresenter = GetTemplateChild(ContentControl.ContentPresenterPartName) as Label;
            _templateAcceptButton = GetTemplateChild(AcceptButtonPartName) as BaseButton;
            _templateCancelButton = GetTemplateChild(CancelButtonPartName) as BaseButton;
            if (_templateAcceptButton != null) _templateAcceptButton.Pressed += HandleTemplateAcceptPressed;
            if (_templateCancelButton != null) _templateCancelButton.Pressed += HandleTemplateCancelPressed;
            SynchronizeTemplateParts();
            base.OnTemplateApplied();
        }
        protected internal override void PointerReleased(Point point, bool isInside)
        {
            if (!isInside) return;
            if (OkButtonBounds.Contains(point)) { if (!OkButtonDisabled) Confirm(); }
            else if (HasCancelButton && CancelButtonBounds.Contains(point)) Cancel();
        }
        internal override void KeyPressed(Microsoft.Xna.Framework.Input.Keys key)
        {
            if (key == Microsoft.Xna.Framework.Input.Keys.Enter) { if (!OkButtonDisabled) Confirm(); }
            else if (key == Microsoft.Xna.Framework.Input.Keys.Escape)
            {
                if (CloseOnEscape) Cancel();
            }
            else base.KeyPressed(key);
        }
        internal virtual void DrawDialogBody(UIRenderContext context) { }
        internal bool DialogHasCancelButton => HasCancelButton;
        internal string DialogCancelLabelText => CancelLabelText;
        internal Rectangle DialogOkButtonBounds => OkButtonBounds;
        internal Rectangle DialogCancelButtonBounds => CancelButtonBounds;
        protected virtual bool HasCancelButton => false;
        protected virtual string CancelLabelText => string.Empty;
        protected virtual Rectangle OkButtonBounds => new Rectangle(Bounds.Right - 70, Bounds.Bottom - (int)ButtonHeight - 8, 60, (int)ButtonHeight);
        protected virtual Rectangle CancelButtonBounds => Rectangle.Empty;
        private void HandleTemplateAcceptPressed(object sender, EventArgs args)
        {
            if (!OkButtonDisabled) Confirm();
        }
        private void HandleTemplateCancelPressed(object sender, EventArgs args) => Cancel();
        private void SynchronizeTemplateParts()
        {
            if (_templateTitlePresenter != null)
            {
                _templateTitlePresenter.Text = Title;
                _templateTitlePresenter.Font = Font;
                _templateTitlePresenter.UIFont = UIFont;
            }
            if (_templateContentPresenter != null)
            {
                _templateContentPresenter.Text = DialogText;
                _templateContentPresenter.Font = Font;
                _templateContentPresenter.UIFont = UIFont;
            }
            if (_templateAcceptButton != null)
            {
                _templateAcceptButton.Text = OkText;
                _templateAcceptButton.Enabled = !OkButtonDisabled;
            }
            if (_templateCancelButton != null)
            {
                _templateCancelButton.Text = DialogCancelLabelText;
                _templateCancelButton.Visible = DialogHasCancelButton;
            }
        }
        private int GetDialogButtonWidth(Button button)
        {
            var textWidth = EffectiveUIFont == null ? (button.Text?.Length ?? 0) * 8 : (int)MathF.Ceiling(TextMetrics.Measure(EffectiveUIFont, button.Text ?? string.Empty).X);
            return Math.Max(60, textWidth + 16);
        }
    }

    /// <summary>Adds a configurable cancel action alongside the accept action of an <see cref="AcceptDialog"/>.</summary>
    public class ConfirmationDialog : AcceptDialog
    {
        public string CancelText { get; set; } = "Cancel";
        protected override bool HasCancelButton => true;
        protected override string CancelLabelText => CancelText;
        protected override Rectangle OkButtonBounds => new Rectangle(Bounds.Right - 70, Bounds.Bottom - (int)ButtonHeight - 8, 60, (int)ButtonHeight);
        protected override Rectangle CancelButtonBounds => new Rectangle(Bounds.Right - 142, Bounds.Bottom - (int)ButtonHeight - 8, 64, (int)ButtonHeight);
    }

    public enum FileDialogMode { OpenFile, OpenFiles, OpenDirectory, OpenAny, SaveFile }
    public enum FileDialogAccess { Resources, UserData, FileSystem }
    public enum FileDialogDisplayMode { Thumbnails, List }
    public enum FileDialogSortOption { Name, NameReverse, Type, TypeReverse, ModifiedTime, ModifiedTimeReverse }
    public enum FileDialogCustomization { HiddenFiles, CreateFolder, FileFilter, FileSort, Favorites, Recent, Layout, OverwriteWarning, Delete }

    public interface IFileDialogFileSystem
    {
        bool IsAvailable { get; }
        string GetCurrentDirectory();
        bool FileExists(string path);
        bool DirectoryExists(string path);
        IEnumerable<string> EnumerateEntries(string path);
        string GetParentDirectory(string path);
        void CreateDirectory(string path);
        DateTime GetLastWriteTimeUtc(string path);
        bool CanDelete => false;
        void DeleteFile(string path) => throw new NotSupportedException("Deleting files is unavailable.");
        void DeleteDirectory(string path) => throw new NotSupportedException("Deleting folders is unavailable.");
        bool CanShowInFileManager => false;
        void ShowInFileManager(string path) => throw new NotSupportedException("Showing files in the platform file manager is unavailable.");
        IReadOnlyList<string> GetDrives() => Array.Empty<string>();
    }

    public sealed class DesktopFileDialogFileSystem : IFileDialogFileSystem
    {
        public static DesktopFileDialogFileSystem Instance { get; } = new DesktopFileDialogFileSystem();
        private DesktopFileDialogFileSystem() { }
        public bool IsAvailable => true;
        public string GetCurrentDirectory() => Directory.GetCurrentDirectory();
        public bool FileExists(string path) => File.Exists(path);
        public bool DirectoryExists(string path) => Directory.Exists(path);
        public IEnumerable<string> EnumerateEntries(string path) => Directory.EnumerateFileSystemEntries(path);
        public string GetParentDirectory(string path) => Directory.GetParent(path)?.FullName;
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);
        public bool CanDelete => true;
        public void DeleteFile(string path) => File.Delete(path);
        public void DeleteDirectory(string path) => Directory.Delete(path, true);
        public bool CanShowInFileManager => true;
        public void ShowInFileManager(string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (OperatingSystem.IsMacOS()) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("open", $"-R \"{fullPath}\"") { UseShellExecute = false });
            else if (OperatingSystem.IsWindows()) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{fullPath}\"") { UseShellExecute = false });
            else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("xdg-open", $"\"{Path.GetDirectoryName(fullPath) ?? fullPath}\"") { UseShellExecute = false });
        }
        public IReadOnlyList<string> GetDrives() => DriveInfo.GetDrives().Select(drive => drive.RootDirectory.FullName).ToArray();
    }

    public sealed class FileDialogOption
    {
        internal FileDialogOption(string name, IEnumerable<string> values, int defaultIndex)
        {
            Name = name ?? string.Empty;
            Values = values == null ? new List<string>() : new List<string>(values);
            DefaultIndex = ClampDefaultIndex(defaultIndex, Values.Count);
        }
        public string Name { get; internal set; }
        public List<string> Values { get; internal set; }
        public int DefaultIndex { get; internal set; }
        internal object SelectedValue => Values.Count == 0 ? (object)(DefaultIndex != 0) : DefaultIndex;
        internal static int ClampDefaultIndex(int index, int valueCount) => valueCount == 0 ? MathHelper.Clamp(index, 0, 1) : MathHelper.Clamp(index, 0, valueCount - 1);
    }

    /// <summary>Filesystem-selection dialog with navigation, filtering, sorting and list presentation.</summary>
    public sealed class FileDialog : ConfirmationDialog
    {
        private const int ContextCopyPath = 1;
        private const int ContextDelete = 2;
        private const int ContextRefresh = 3;
        private const int ContextNewFolder = 4;
        private const int ContextShowInFileManager = 5;
        private const int MaxRecentDirectories = 20;
        private const int DialogInset = 10;
        private const int NavigationButtonSize = 30;
        private const int NavigationButtonGap = 6;
        private const int ControlHeight = 30;
        private const int ToolbarButtonWidth = 32;
        private const int SidebarWidth = 160;
        private const int FooterButtonWidth = 80;
        private const int FooterButtonGap = 8;
        private const int FooterInset = 10;
        private const int ThumbnailWidth = 112;
        private const int ThumbnailHeight = 84;
        private const int ThumbnailIconSize = 48;
        private static readonly TimeSpan DoubleClickTimeout = TimeSpan.FromMilliseconds(600);
        private const int DoubleClickTolerance = 5;
        private static readonly List<string> _favoriteList = new List<string>();
        private static readonly List<string> _recentList = new List<string>();
        private static bool _defaultShowHiddenFiles;
        private static FileDialogDisplayMode _defaultDisplayMode = FileDialogDisplayMode.Thumbnails;
        public static Func<FileDialog, bool> NativeDialogHandler { get; set; }
        public static Func<string, ThemeIcon?> GetIconCallback { get; set; }
        public static Func<string, int, ThemeIcon?> GetThumbnailCallback { get; set; }
        private readonly List<string> _filters = new List<string>();
        private readonly List<FileDialogOption> _options = new List<FileDialogOption>();
        private readonly List<string> _entries = new List<string>();
        private readonly List<string> _selectedFiles = new List<string>();
        private readonly bool[] _customizationFlags = new bool[(int)FileDialogCustomization.Delete + 1];
        private readonly Button _backButton;
        private readonly Button _forwardButton;
        private readonly Button _upButton;
        private readonly Button _refreshButton;
        private readonly Button _favoriteButton;
        private readonly Button _createFolderButton;
        private readonly Button _showHiddenButton;
        private readonly Button _thumbnailModeButton;
        private readonly Button _listModeButton;
        private readonly Button _filenameFilterButton;
        private readonly LineEdit _directoryEdit;
        private readonly OptionButton _driveSelector;
        private readonly LineEdit _filenameEdit;
        private readonly LineEdit _filenameFilterEdit;
        private readonly OptionButton _filterSelector;
        private readonly OptionButton _sortSelector;
        private readonly Label _favoritesLabel;
        private readonly Label _recentsLabel;
        private readonly ItemList _favoritesList;
        private readonly ItemList _recentsList;
        private readonly Button _favoriteUpButton;
        private readonly Button _favoriteDownButton;
        private readonly ConfirmationDialog _makeDirectoryDialog;
        private readonly LineEdit _newDirectoryEdit;
        private readonly ConfirmationDialog _deleteConfirmation;
        private readonly PopupMenu _itemMenu;
        private readonly List<Control> _optionControls = new List<Control>();
        private readonly Dictionary<string, object> _selectedOptions = new Dictionary<string, object>();
        private readonly ConfirmationDialog _overwriteConfirmation;
        private string _pendingOverwritePath = string.Empty;
        private readonly List<string> _history = new List<string>();
        private int _historyPosition = -1;
        private TimeSpan _lastClickTime = TimeSpan.MinValue;
        private Point _lastClickPosition;
        private int _lastClickIndex = -1;
        private bool _doubleClickPending;
        private int _filterIndex;
        private FileDialogMode _fileMode = FileDialogMode.OpenFile;
        private FileDialogAccess _access = FileDialogAccess.FileSystem;
        private FileDialogDisplayMode _displayMode;
        private FileDialogSortOption _sortOption = FileDialogSortOption.Name;
        private bool _showHiddenFiles;
        private bool _showFilenameFilter;
        private bool _canCreateFolders = true;
        private string _filenameFilter = string.Empty;
        private string _rootSubfolder = string.Empty;
        private bool _synchronizingControls;
        private int _contextEntryIndex = -1;
        private bool _modeOverridesTitle = true;
        public FileDialog()
        {
            // Godot's FileDialog constructor calls set_hide_on_ok(false): hiding is managed entirely by
            // the mode-specific validity checks in Confirm(), not by the base AcceptDialog's hide-on-ok.
            HideOnOk = false;
            ButtonHeight = 32;
            _showHiddenFiles = _defaultShowHiddenFiles;
            _displayMode = _defaultDisplayMode;
            for (var index = 0; index < _customizationFlags.Length; index++) _customizationFlags[index] = true;
            _backButton = CreateNavigationButton("FileDialogBack", "<", "Back");
            _forwardButton = CreateNavigationButton("FileDialogForward", ">", "Forward");
            _upButton = CreateNavigationButton("FileDialogUp", "^", "Up one folder");
            ConfigureNavigationIcon(_backButton, "back");
            ConfigureNavigationIcon(_forwardButton, "forward");
            ConfigureNavigationIcon(_upButton, "up");
            _backButton.Pressed += (_, _) => { GoBack(); ClearSelectionAfterDirectoryNavigation(); };
            _forwardButton.Pressed += (_, _) => { GoForward(); ClearSelectionAfterDirectoryNavigation(); };
            _upButton.Pressed += (_, _) => { GoUp(); ClearSelectionAfterDirectoryNavigation(); };
            _refreshButton = CreateNavigationButton("FileDialogRefresh", "R", "Refresh files");
            ConfigureNavigationIcon(_refreshButton, "reload");
            _refreshButton.Pressed += (_, _) => { if (!string.IsNullOrEmpty(CurrentPath)) Refresh(CurrentPath); };
            _favoriteButton = CreateNavigationButton("FileDialogFavorite", "*", "Favorite current folder");
            ConfigureNavigationIcon(_favoriteButton, "favorite");
            _favoriteButton.ToggleMode = true;
            _favoriteButton.Pressed += (_, _) => { ToggleCurrentDirectoryFavorite(); UpdateDirectoryLists(); };
            _createFolderButton = CreateNavigationButton("FileDialogCreateFolder", "+", "Create a new folder");
            ConfigureNavigationIcon(_createFolderButton, "create_folder");
            _createFolderButton.Pressed += (_, _) => ShowCreateFolderDialog();
            _directoryEdit = new LineEdit { Name = "FileDialogPath", PlaceholderText = "Path", ClearButtonEnabled = true };
            _directoryEdit.TextSubmitted += (_, _) => SubmitDirectoryPath();
            _driveSelector = new OptionButton { Name = "FileDialogDrives", FitToLongestItem = false, Visible = false };
            _driveSelector.ItemSelected += (_, index) => SelectDrive(index);
            _showHiddenButton = CreateToolbarButton("FileDialogShowHidden", "Hidden", "Toggle hidden files", true);
            ConfigureNavigationIcon(_showHiddenButton, "toggle_hidden");
            _showHiddenButton.Toggled += (_, enabled) => { if (!_synchronizingControls) ShowHiddenFiles = enabled; };
            _thumbnailModeButton = CreateToolbarButton("FileDialogThumbnails", "Grid", "View items as thumbnails", true);
            ConfigureNavigationIcon(_thumbnailModeButton, "thumbnail_mode");
            _thumbnailModeButton.Pressed += (_, _) => DisplayMode = FileDialogDisplayMode.Thumbnails;
            _listModeButton = CreateToolbarButton("FileDialogList", "List", "View items as a list", true);
            ConfigureNavigationIcon(_listModeButton, "list_mode");
            _listModeButton.Pressed += (_, _) => DisplayMode = FileDialogDisplayMode.List;
            _filenameFilterButton = CreateToolbarButton("FileDialogFilenameFilterToggle", "Find", "Filter by filename", true);
            ConfigureNavigationIcon(_filenameFilterButton, "toggle_filename_filter");
            _filenameFilterButton.Toggled += (_, enabled) => { if (!_synchronizingControls) ShowFilenameFilter = enabled; };
            _sortSelector = new OptionButton { Name = "FileDialogSort", FitToLongestItem = false };
            _sortSelector.DecorativeIconProvider = () => GetThemeIcon("sort");
            foreach (var name in new[] { "Name (A-Z)", "Name (Z-A)", "Type (A-Z)", "Type (Z-A)", "Modified (newest)", "Modified (oldest)" }) _sortSelector.AddItem(name);
            _sortSelector.ItemSelected += (_, index) => { if (!_synchronizingControls) SortOption = (FileDialogSortOption)index; };
            _filenameFilterEdit = new LineEdit { Name = "FileDialogFilenameFilter", PlaceholderText = "Filter filenames", ClearButtonEnabled = true };
            _filenameFilterEdit.TextChanged += (_, text) => { if (!_synchronizingControls) FilenameFilter = text; };
            _filenameEdit = new LineEdit { Name = "FileDialogFilename", PlaceholderText = "File name" };
            _filenameEdit.TextChanged += (_, text) => { if (!_synchronizingControls) SetFilenameFromEditor(text); };
            _filenameEdit.TextSubmitted += (_, _) => Confirm();
            _filterSelector = new OptionButton { Name = "FileDialogFilter", FitToLongestItem = false };
            _filterSelector.ItemSelected += (_, index) => { if (!_synchronizingControls) FilterIndex = index; };
            _favoritesLabel = new Label { Name = "FileDialogFavoritesLabel", Text = "Favorites" };
            _recentsLabel = new Label { Name = "FileDialogRecentsLabel", Text = "Recent" };
            _favoritesList = new ItemList { Name = "FileDialogFavorites", ItemHeight = 26 };
            _favoritesList.ItemSelected += (_, index) => NavigateFromDirectoryList(_favoritesList, index);
            _recentsList = new ItemList { Name = "FileDialogRecents", ItemHeight = 26 };
            _recentsList.ItemSelected += (_, index) => NavigateFromDirectoryList(_recentsList, index);
            _favoriteUpButton = CreateNavigationButton("FileDialogFavoriteUp", "^", "Move favorite up");
            ConfigureNavigationIcon(_favoriteUpButton, "favorite_up");
            _favoriteUpButton.Pressed += (_, _) => MoveSelectedFavorite(-1);
            _favoriteDownButton = CreateNavigationButton("FileDialogFavoriteDown", "v", "Move favorite down");
            ConfigureNavigationIcon(_favoriteDownButton, "favorite_down");
            _favoriteDownButton.Pressed += (_, _) => MoveSelectedFavorite(1);
            _makeDirectoryDialog = new ConfirmationDialog { Name = "FileDialogCreateFolderDialog", Title = "Create Folder", OkText = "Create", Visible = false, Size = new Vector2(300, 120) };
            _newDirectoryEdit = new LineEdit { Name = "FileDialogNewFolderName", PlaceholderText = "Folder name", Position = new Vector2(12, 38), Size = new Vector2(276, 28) };
            _makeDirectoryDialog.AddChild(_newDirectoryEdit);
            _makeDirectoryDialog.RegisterTextEnter(_newDirectoryEdit);
            _makeDirectoryDialog.Confirmed += (_, _) => ConfirmCreateFolder();
            _deleteConfirmation = new ConfirmationDialog { Name = "FileDialogDeleteConfirmation", Title = "Delete", DialogText = "Delete the selected item?", OkText = "Delete", Visible = false, Size = new Vector2(320, 120) };
            _deleteConfirmation.Confirmed += (_, _) => DeleteContextEntry();
            _itemMenu = new PopupMenu { Name = "FileDialogItemMenu", Visible = false };
            _itemMenu.IdPressed += (_, id) => HandleItemMenu(id);
            AddChild(_backButton);
            AddChild(_forwardButton);
            AddChild(_upButton);
            AddChild(_refreshButton);
            AddChild(_favoriteButton);
            AddChild(_createFolderButton);
            AddChild(_directoryEdit);
            AddChild(_driveSelector);
            AddChild(_showHiddenButton);
            AddChild(_thumbnailModeButton);
            AddChild(_listModeButton);
            AddChild(_filenameFilterButton);
            AddChild(_sortSelector);
            AddChild(_filenameFilterEdit);
            AddChild(_filenameEdit);
            AddChild(_filterSelector);
            AddChild(_favoritesLabel);
            AddChild(_recentsLabel);
            AddChild(_favoritesList);
            AddChild(_recentsList);
            AddChild(_favoriteUpButton);
            AddChild(_favoriteDownButton);
            AddChild(_makeDirectoryDialog);
            AddChild(_deleteConfirmation);
            AddChild(_itemMenu);
            _overwriteConfirmation = new ConfirmationDialog { Title = "Confirm overwrite", DialogText = string.Empty, OkText = "Save", Visible = false, Size = new Vector2(250, 80) };
            _overwriteConfirmation.Confirmed += (_, _) => ConfirmPendingOverwrite();
            _overwriteConfirmation.Canceled += (_, _) => _pendingOverwritePath = string.Empty;
            AddChild(_overwriteConfirmation);
            ApplyFileModePresentation();
            UpdateFilterSelector();
            SynchronizeControls();
            SynchronizeNavigationButtons();
        }
        public override Vector2 GetMinimumSize() => Vector2.Max(base.GetMinimumSize(), new Vector2(640, 420));
        public FileDialogMode FileMode
        {
            get => _fileMode;
            set
            {
                if (_fileMode == value) return;
                _fileMode = value;
                ApplyFileModePresentation();
                SynchronizeControls();
                QueueLayout();
            }
        }
        public bool ModeOverridesTitle
        {
            get => _modeOverridesTitle;
            set => _modeOverridesTitle = value;
        }
        public FileDialogAccess Access
        {
            get => _access;
            set
            {
                if (_access == value) return;
                _access = value;
                if (ReferenceEquals(FileSystem, DesktopFileDialogFileSystem.Instance) && value != FileDialogAccess.FileSystem)
                {
                    var root = value == FileDialogAccess.Resources
                        ? AppContext.BaseDirectory
                        : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    if (!string.IsNullOrEmpty(root) && FileSystem.DirectoryExists(root)) SetRootSubfolder(root);
                }
                else if (value == FileDialogAccess.FileSystem) _rootSubfolder = string.Empty;
                UpdateDriveSelector();
                QueueLayout();
            }
        }
        public FileDialogDisplayMode DisplayMode
        {
            get => _displayMode;
            set { if (_displayMode == value) return; _displayMode = value; SynchronizeControls(); QueueLayout(); }
        }
        public FileDialogSortOption SortOption
        {
            get => _sortOption;
            set { if (_sortOption == value) return; _sortOption = value; SynchronizeControls(); if (!string.IsNullOrEmpty(CurrentPath)) Refresh(CurrentPath); }
        }
        public bool ShowHiddenFiles
        {
            get => _showHiddenFiles;
            set { if (_showHiddenFiles == value) return; _showHiddenFiles = value; SynchronizeControls(); if (!string.IsNullOrEmpty(CurrentPath)) Refresh(CurrentPath); }
        }
        public bool ShowFilenameFilter
        {
            get => _showFilenameFilter;
            set { if (_showFilenameFilter == value) return; _showFilenameFilter = value; SynchronizeControls(); QueueLayout(); }
        }
        public string RootSubfolder
        {
            get => _rootSubfolder;
            set => SetRootSubfolder(value);
        }
        public bool UseNativeDialog { get; set; }
        public bool CanCreateFolders
        {
            get => _canCreateFolders;
            set
            {
                if (_canCreateFolders == value) return;
                _canCreateFolders = value;
                SynchronizeControls();
                QueueLayout();
            }
        }
        public IFileDialogFileSystem FileSystem { get; set; } = DesktopFileDialogFileSystem.Instance;
        public bool IsFileSystemAvailable => FileSystem?.IsAvailable == true;
        public bool OverwriteWarningEnabled
        {
            get => IsCustomizationFlagEnabled(FileDialogCustomization.OverwriteWarning);
            set => SetCustomizationFlagEnabled(FileDialogCustomization.OverwriteWarning, value);
        }
        public float EntryHeight { get; set; } = 28;
        public string FilenameFilter
        {
            get => _filenameFilter;
            set
            {
                value ??= string.Empty;
                if (_filenameFilter == value) return;
                _filenameFilter = value;
                SynchronizeControls();
                if (!string.IsNullOrEmpty(CurrentPath)) Refresh(CurrentPath);
                FilenameFilterChanged?.Invoke(this, value);
            }
        }
        public string CurrentPath { get; private set; } = string.Empty;
        public string CurrentFile { get; private set; } = string.Empty;
        public string PendingOverwritePath => _pendingOverwritePath;
        public bool IsOverwriteConfirmationVisible => _overwriteConfirmation.Visible;
        public ConfirmationDialog OverwriteConfirmationDialog => _overwriteConfirmation;
        public IReadOnlyList<string> Filters => _filters;
        public IReadOnlyList<FileDialogOption> Options => _options;
        public IReadOnlyList<string> Entries => _entries;
        public IReadOnlyList<string> SelectedFiles => _selectedFiles;
        public event Action<FileDialog, string> FileSelected;
        public event Action<FileDialog, IReadOnlyList<string>> FilesSelected;
        public event Action<FileDialog, string> DirectorySelected;
        public event Action<FileDialog, string> FolderCreated;
        public event Action<FileDialog, string> FilenameFilterChanged;
        public static void SetDefaultShowHiddenFiles(bool show) => _defaultShowHiddenFiles = show;
        public static bool GetDefaultShowHiddenFiles() => _defaultShowHiddenFiles;
        public static void SetDefaultDisplayMode(FileDialogDisplayMode mode) => _defaultDisplayMode = mode;
        public static FileDialogDisplayMode GetDefaultDisplayMode() => _defaultDisplayMode;
        public void ClearFilenameFilter() => FilenameFilter = string.Empty;
        public void DeselectAll() => ClearSelection();
        public void Invalidate() { if (!string.IsNullOrEmpty(CurrentPath)) Refresh(CurrentPath); }
        public bool PopupFileDialog()
        {
            if (UseNativeDialog && NativeDialogHandler?.Invoke(this) == true) return true;
            Visible = true;
            return false;
        }
        public void AddFilter(string filter) { if (!string.IsNullOrWhiteSpace(filter)) _filters.Add(filter); ClampFilterIndex(); UpdateFilterSelector(); }
        public void ClearFilters() { _filters.Clear(); ClampFilterIndex(); UpdateFilterSelector(); }
        public void SetFilters(IEnumerable<string> filters)
        {
            _filters.Clear(); if (filters != null) foreach (var filter in filters) if (!string.IsNullOrWhiteSpace(filter)) _filters.Add(filter);
            ClampFilterIndex();
            UpdateFilterSelector();
        }
        private void ClampFilterIndex() { if (_filterIndex > LastFilterIndex) _filterIndex = LastFilterIndex; }
        public void AddOption(string name, IEnumerable<string> values, int defaultValueIndex = 0)
        {
            _options.Add(new FileDialogOption(name, values, defaultValueIndex));
            RebuildOptionControls();
        }
        public void SetOptionCount(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            while (_options.Count < count) _options.Add(new FileDialogOption(string.Empty, null, 0));
            while (_options.Count > count) _options.RemoveAt(_options.Count - 1);
            RebuildOptionControls();
        }
        public int GetOptionCount() => _options.Count;
        public string GetOptionName(int option) => GetOption(option).Name;
        public void SetOptionName(int option, string name) { GetOption(option, true).Name = name ?? string.Empty; RebuildOptionControls(); }
        public IReadOnlyList<string> GetOptionValues(int option) => GetOption(option).Values.ToArray();
        public void SetOptionValues(int option, IEnumerable<string> values)
        {
            var entry = GetOption(option, true);
            entry.Values = values == null ? new List<string>() : new List<string>(values);
            entry.DefaultIndex = FileDialogOption.ClampDefaultIndex(entry.DefaultIndex, entry.Values.Count);
            RebuildOptionControls();
        }
        public int GetOptionDefault(int option) => GetOption(option).DefaultIndex;
        public void SetOptionDefault(int option, int defaultValueIndex)
        {
            var entry = GetOption(option, true);
            entry.DefaultIndex = FileDialogOption.ClampDefaultIndex(defaultValueIndex, entry.Values.Count);
            RebuildOptionControls();
        }
        public IReadOnlyDictionary<string, object> GetSelectedOptions()
        {
            return new Dictionary<string, object>(_selectedOptions);
        }
        public void SetFileMode(FileDialogMode mode) => FileMode = mode;
        public FileDialogMode GetFileMode() => FileMode;
        public void SetModeOverridesTitle(bool enabled) => ModeOverridesTitle = enabled;
        public bool IsModeOverridingTitle() => ModeOverridesTitle;
        public void SetCustomizationFlagEnabled(FileDialogCustomization flag, bool enabled)
        {
            _customizationFlags[(int)flag] = enabled;
            SynchronizeControls();
            QueueLayout();
        }
        public bool IsCustomizationFlagEnabled(FileDialogCustomization flag) => _customizationFlags[(int)flag];
        public static void SetFavoriteList(IEnumerable<string> favorites)
        {
            _favoriteList.Clear();
            if (favorites == null) return;
            foreach (var favorite in favorites) _favoriteList.Add(NormalizeDirectoryListPath(favorite));
        }
        public static IReadOnlyList<string> GetFavoriteList() => _favoriteList.ToArray();
        public static void SetRecentList(IEnumerable<string> recents)
        {
            _recentList.Clear();
            if (recents == null) return;
            foreach (var recent in recents) _recentList.Add(NormalizeDirectoryListPath(recent));
        }
        public static IReadOnlyList<string> GetRecentList() => _recentList.ToArray();
        public void ToggleCurrentDirectoryFavorite()
        {
            if (string.IsNullOrEmpty(CurrentPath)) return;
            var directory = NormalizeDirectoryListPath(CurrentPath);
            if (_favoriteList.Contains(directory)) _favoriteList.Remove(directory);
            else _favoriteList.Add(directory);
        }
        public string GetCurrentDir() => CurrentPath;
        public void SetCurrentDir(string path) => NavigateTo(path);
        public string GetCurrentFile() => CurrentFile;
        public void SetCurrentFile(string path) => SelectFile(path);
        public string GetCurrentPath()
        {
            if (string.IsNullOrEmpty(CurrentFile)) return CurrentPath ?? string.Empty;
            return Path.IsPathRooted(CurrentFile) ? CurrentFile : Path.GetFullPath(Path.Combine(string.IsNullOrEmpty(CurrentPath) ? FileSystem.GetCurrentDirectory() : CurrentPath, CurrentFile));
        }
        public void SetCurrentPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            var fileName = Path.GetFileName(fullPath);
            if (!string.IsNullOrEmpty(directory) && FileSystem.DirectoryExists(directory)) NavigateTo(directory);
            if (!string.IsNullOrEmpty(fileName)) SelectFile(fullPath);
        }
        public IReadOnlyList<string> GetSelectedFiles() => _selectedFiles;
        /// <summary>Non-empty when the last Refresh hit a permission error, matching Godot's own
        /// "You don't have permission..." message label shown in place of the file list.</summary>
        public string Message { get; private set; } = string.Empty;
        public void Refresh(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            var fullPath = Path.GetFullPath(path);
            EnsureWithinRoot(fullPath);
            CurrentPath = fullPath;
            SynchronizeControls();
            QueueLayout();
            SynchronizeNavigationButtons();
            _entries.Clear();
            Message = string.Empty;
            if (!IsFileSystemAvailable)
            {
                Message = "Filesystem access is unavailable.";
                return;
            }
            if (!FileSystem.DirectoryExists(CurrentPath)) return;
            try
            {
                foreach (var entry in FileSystem.EnumerateEntries(CurrentPath))
                {
                    var name = Path.GetFileName(entry);
                    if (!ShowHiddenFiles && name.StartsWith(".", StringComparison.Ordinal)) continue;
                    if ((FileSystem.DirectoryExists(entry) || MatchesFilter(name)) && MatchesFilenameFilter(name)) _entries.Add(entry);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Godot's update_file_list checks is_readable() before listing and shows a graceful
                // message instead of the list; this port only discovers the same fact by attempting the
                // enumeration, so any partially-added entries are discarded on failure.
                _entries.Clear();
                Message = "You don't have permission to access contents of this folder.";
                return;
            }
            _entries.Sort(CompareEntries);
        }
        public void NavigateTo(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A directory path is required.", nameof(path));
            if (!IsFileSystemAvailable) throw new PlatformNotSupportedException("Filesystem access is unavailable.");
            var fullPath = Path.GetFullPath(path);
            EnsureWithinRoot(fullPath);
            if (!FileSystem.DirectoryExists(fullPath)) throw new DirectoryNotFoundException(fullPath);
            Refresh(fullPath);
            PushHistory();
        }
        public void GoUp()
        {
            if (string.IsNullOrEmpty(CurrentPath)) return;
            var parent = FileSystem.GetParentDirectory(CurrentPath);
            if (parent == null || !IsWithinRoot(parent)) return;
            Refresh(parent);
            PushHistory();
        }
        /// <summary>Whether GoBack() has an earlier directory to return to, matching Godot's dir_prev enablement.</summary>
        public bool CanGoBack => _historyPosition > 0;
        /// <summary>Whether GoForward() has a later directory to redo, matching Godot's dir_next enablement.</summary>
        public bool CanGoForward => _historyPosition >= 0 && _historyPosition < _history.Count - 1;
        /// <summary>Navigates to the previously-visited directory without disturbing forward history,
        /// matching Godot's _go_back (which calls _change_dir directly, bypassing _push_history).</summary>
        public void GoBack() { if (!CanGoBack) return; _historyPosition--; Refresh(_history[_historyPosition]); }
        /// <summary>Redoes a GoBack(), matching Godot's _go_forward.</summary>
        public void GoForward() { if (!CanGoForward) return; _historyPosition++; Refresh(_history[_historyPosition]); }
        private void PushHistory()
        {
            if (string.IsNullOrEmpty(CurrentPath)) return;
            if (_historyPosition >= 0 && _historyPosition < _history.Count - 1) _history.RemoveRange(_historyPosition + 1, _history.Count - _historyPosition - 1);
            if (_history.Count == 0 || _history[_historyPosition] != CurrentPath) { _history.Add(CurrentPath); _historyPosition = _history.Count - 1; }
            SynchronizeNavigationButtons();
        }
        /// <summary>Matches Godot's _update_make_dir_visible: folder creation is force-disabled while
        /// merely picking a file/files to open, regardless of the CanCreateFolders customization flag.</summary>
        public bool EffectiveCanCreateFolders => CanCreateFolders && IsCustomizationFlagEnabled(FileDialogCustomization.CreateFolder) && FileMode != FileDialogMode.OpenFile && FileMode != FileDialogMode.OpenFiles;
        public string CreateFolder(string name)
        {
            if (!EffectiveCanCreateFolders) throw new InvalidOperationException("Folder creation is disabled.");
            if (string.IsNullOrWhiteSpace(CurrentPath)) throw new InvalidOperationException("A current directory is required.");
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("A valid folder name is required.", nameof(name));
            if (!IsFileSystemAvailable) throw new PlatformNotSupportedException("Filesystem access is unavailable.");
            var path = Path.Combine(CurrentPath, name); FileSystem.CreateDirectory(path); Refresh(CurrentPath); FolderCreated?.Invoke(this, path); return path;
        }
        public void SelectEntry(int index, bool append = false)
        {
            if (index < 0 || index >= _entries.Count) throw new ArgumentOutOfRangeException(nameof(index));
            SelectFile(_entries[index], append && FileMode == FileDialogMode.OpenFiles);
        }
        public void SelectFile(string path, bool append = false)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", nameof(path));
            CurrentFile = FileMode == FileDialogMode.SaveFile && !Path.IsPathRooted(path) && !string.IsNullOrEmpty(CurrentPath)
                ? Path.GetFullPath(Path.Combine(CurrentPath, path))
                : Path.GetFullPath(path);
            if (!append) _selectedFiles.Clear();
            if (!_selectedFiles.Contains(CurrentFile)) _selectedFiles.Add(CurrentFile);
            SynchronizeControls();
        }
        public void ActivateEntry(int index)
        {
            if (index < 0 || index >= _entries.Count) throw new ArgumentOutOfRangeException(nameof(index));
            var entry = _entries[index];
            if (FileSystem.DirectoryExists(entry))
            {
                NavigateTo(entry);
                // Godot's _file_list_item_activated clears the filename field on directory navigation
                // for every mode except Save, so a stale selection from a directory the user has since
                // navigated away from can't be silently confirmed.
                if (FileMode != FileDialogMode.SaveFile) ClearSelection();
            }
            else SelectEntry(index, FileMode == FileDialogMode.OpenFiles);
        }
        public void ClearSelection()
        {
            CurrentFile = string.Empty;
            _selectedFiles.Clear();
            SynchronizeControls();
        }
        protected override void ArrangeChildren()
        {
            base.ArrangeChildren();
            var x = (float)DialogInset;
            foreach (var button in new[] { _backButton, _forwardButton, _upButton })
            {
                button.Position = new Vector2(x, 32);
                button.Size = new Vector2(NavigationButtonSize, ControlHeight);
                x += NavigationButtonSize + NavigationButtonGap;
            }
            var trailingX = Size.X - DialogInset - NavigationButtonSize;
            _refreshButton.Position = new Vector2(Math.Max(x, trailingX), 32);
            _refreshButton.Size = new Vector2(NavigationButtonSize, ControlHeight);
            trailingX -= NavigationButtonSize + NavigationButtonGap;
            _favoriteButton.Position = new Vector2(Math.Max(x, trailingX), 32);
            _favoriteButton.Size = new Vector2(NavigationButtonSize, ControlHeight);
            trailingX -= NavigationButtonSize + NavigationButtonGap;
            _createFolderButton.Position = new Vector2(Math.Max(x, trailingX), 32);
            _createFolderButton.Size = new Vector2(NavigationButtonSize, ControlHeight);
            UpdateDriveSelector();
            _driveSelector.Position = new Vector2(x, 32);
            _driveSelector.Size = new Vector2(_driveSelector.Visible ? 112 : 0, ControlHeight);
            x += _driveSelector.Visible ? 112 + NavigationButtonGap : 0;
            _directoryEdit.Position = new Vector2(x, 32);
            var firstTrailingButton = _createFolderButton.Visible ? _createFolderButton : _favoriteButton.Visible ? _favoriteButton : _refreshButton;
            _directoryEdit.Size = new Vector2(Math.Max(0, firstTrailingButton.Position.X - x - NavigationButtonGap), ControlHeight);

            var toolbarY = 68f;
            var toolbarX = (float)DialogInset;
            foreach (var button in new[] { _showHiddenButton, _thumbnailModeButton, _listModeButton, _filenameFilterButton })
            {
                button.Position = new Vector2(toolbarX, toolbarY);
                button.Size = new Vector2(ToolbarButtonWidth, ControlHeight);
                if (button.Visible) toolbarX += ToolbarButtonWidth + NavigationButtonGap;
            }
            _sortSelector.Position = new Vector2(Math.Max(toolbarX, Size.X - 210), toolbarY);
            _sortSelector.Size = new Vector2(Math.Min(200, Math.Max(0, Size.X - _sortSelector.Position.X - DialogInset)), ControlHeight);

            var fileRowY = Math.Max(104, Size.Y - ButtonHeight - FooterInset - ControlHeight - 8);
            var filterWidth = Math.Min(230, Math.Max(150, Size.X * .36f));
            _filterSelector.Position = new Vector2(Size.X - DialogInset - filterWidth, fileRowY);
            _filterSelector.Size = new Vector2(filterWidth, ControlHeight);
            _filenameEdit.Position = new Vector2(DialogInset, fileRowY);
            _filenameEdit.Size = new Vector2(Math.Max(0, _filterSelector.Position.X - DialogInset - NavigationButtonGap), ControlHeight);
            _filenameFilterEdit.Position = new Vector2(DialogInset, fileRowY - ControlHeight - NavigationButtonGap);
            _filenameFilterEdit.Size = new Vector2(Math.Max(0, Size.X - DialogInset * 2), ControlHeight);
            ArrangeOptionControls(fileRowY - (_filenameFilterEdit.Visible ? ControlHeight + NavigationButtonGap : 0));
            ArrangeDirectoryLists();
            SynchronizeNavigationButtons();
        }
        protected override Rectangle OkButtonBounds => new Rectangle(
            Bounds.Right - FooterInset - FooterButtonWidth,
            Bounds.Bottom - (int)ButtonHeight - FooterInset,
            FooterButtonWidth,
            (int)ButtonHeight);
        protected override Rectangle CancelButtonBounds => new Rectangle(
            OkButtonBounds.Left - FooterButtonGap - FooterButtonWidth,
            OkButtonBounds.Y,
            FooterButtonWidth,
            OkButtonBounds.Height);
        public override void Confirm()
        {
            // Godot's FileDialog constructor calls set_hide_on_ok(false) and connects its OWN
            // `confirmed` signal to _action_pressed - so `confirmed` fires unconditionally the instant
            // OK is pressed (from the base AcceptDialog::_ok_pressed), decoupled from whether the
            // specific file/dir selection is actually valid; the mode-specific signal (file_selected
            // etc.) and the actual hide only happen once _action_pressed's own validity checks pass.
            base.Confirm();
            switch (FileMode)
            {
                case FileDialogMode.OpenFiles:
                    if (_selectedFiles.Count == 0) return;
                    SaveCurrentDirectoryToRecent();
                    FilesSelected?.Invoke(this, _selectedFiles.ToArray());
                    Hide();
                    return;
                case FileDialogMode.OpenFile:
                    if (string.IsNullOrEmpty(CurrentFile) || !FileSystem.FileExists(CurrentFile)) return;
                    SaveCurrentDirectoryToRecent();
                    FileSelected?.Invoke(this, CurrentFile);
                    Hide();
                    return;
                case FileDialogMode.OpenAny:
                    SaveCurrentDirectoryToRecent();
                    if (!string.IsNullOrEmpty(CurrentFile) && FileSystem.FileExists(CurrentFile)) FileSelected?.Invoke(this, CurrentFile);
                    else DirectorySelected?.Invoke(this, FileSystem.DirectoryExists(CurrentFile) ? CurrentFile : CurrentPath);
                    Hide();
                    return;
                case FileDialogMode.OpenDirectory:
                    SaveCurrentDirectoryToRecent();
                    DirectorySelected?.Invoke(this, FileSystem.DirectoryExists(CurrentFile) ? CurrentFile : CurrentPath);
                    Hide();
                    return;
                case FileDialogMode.SaveFile:
                    ConfirmSaveFile();
                    return;
            }
        }
        protected internal override void PointerPressed(Point point)
        {
            base.PointerPressed(point);
            var index = GetEntryIndexAt(point);
            if (index < 0) { _doubleClickPending = false; return; }
            var clickTime = Context?.CurrentTime ?? TimeSpan.Zero;
            var withinTimeout = _lastClickTime != TimeSpan.MinValue && clickTime - _lastClickTime <= DoubleClickTimeout;
            var withinTolerance = Vector2.DistanceSquared(point.ToVector2(), _lastClickPosition.ToVector2()) <= DoubleClickTolerance * DoubleClickTolerance;
            _doubleClickPending = withinTimeout && withinTolerance && index == _lastClickIndex;
            _lastClickTime = clickTime;
            _lastClickPosition = point;
            _lastClickIndex = index;
            SelectEntry(index, FileMode == FileDialogMode.OpenFiles);
        }
        protected internal override void PointerReleased(Point point, bool isInside)
        {
            base.PointerReleased(point, isInside);
            var index = GetEntryIndexAt(point);
            if (isInside && _doubleClickPending && index >= 0 && index == _lastClickIndex) ActivateEntry(index);
            _doubleClickPending = false;
        }
        internal override void CancelInput()
        {
            _doubleClickPending = false;
            _lastClickTime = TimeSpan.MinValue;
            _lastClickIndex = -1;
            base.CancelInput();
        }
        internal override void PointerButtonPressed(Point point, PointerButton button)
        {
            base.PointerButtonPressed(point, button);
            if (button != PointerButton.Right) return;
            var index = GetEntryIndexAt(point);
            if (index >= 0) SelectEntry(index, FileMode == FileDialogMode.OpenFiles);
            _contextEntryIndex = index;
            BuildItemMenu(index >= 0);
            _itemMenu.PopupAt(point.ToVector2());
        }
        internal override void KeyPressed(Keys key)
        {
            if (key == Keys.Back)
            {
                GoUp();
                ClearSelectionAfterDirectoryNavigation();
                return;
            }
            if (key == Keys.Delete && IsCustomizationFlagEnabled(FileDialogCustomization.Delete) && !string.IsNullOrEmpty(CurrentFile))
            {
                _contextEntryIndex = _entries.IndexOf(CurrentFile);
                ShowDeleteConfirmation();
                return;
            }
            if (key == Keys.F5)
            {
                if (!string.IsNullOrEmpty(CurrentPath)) Refresh(CurrentPath);
                return;
            }
            if (key == Keys.Enter && !string.IsNullOrEmpty(CurrentFile) && FileSystem.DirectoryExists(CurrentFile))
            {
                NavigateTo(CurrentFile);
                ClearSelectionAfterDirectoryNavigation();
                return;
            }
            base.KeyPressed(key);
        }
        internal override void DrawDialogBody(UIRenderContext context)
        {
            var entriesBounds = EntriesBounds;
            context.Fill(entriesBounds, context.Theme.BackgroundColor);
            context.Border(entriesBounds, context.Theme.PanelBorderColor);
            if (!string.IsNullOrEmpty(Message))
            {
                if (EffectiveUIFont != null) context.Text(EffectiveUIFont, Message, new Vector2(entriesBounds.X + 8, entriesBounds.Y + 8), context.Theme.DisabledTextColor);
                return;
            }
            for (var index = 0; index < _entries.Count; index++)
            {
                var entry = _entries[index];
                var row = GetEntryRectangle(index);
                if (row == Rectangle.Empty || row.Bottom > entriesBounds.Bottom) break;
                if (_selectedFiles.Contains(entry)) context.Fill(row, context.Theme.AccentColor);
                var isDirectory = FileSystem.DirectoryExists(entry);
                var icon = DisplayMode == FileDialogDisplayMode.Thumbnails ? GetThumbnailCallback?.Invoke(entry, ThumbnailIconSize) : null;
                icon ??= GetIconCallback?.Invoke(entry);
                icon ??= GetThemeIcon(isDirectory
                    ? DisplayMode == FileDialogDisplayMode.Thumbnails ? "folder_thumbnail" : "folder"
                    : DisplayMode == FileDialogDisplayMode.Thumbnails ? "file_thumbnail" : "file");
                if (DisplayMode == FileDialogDisplayMode.Thumbnails)
                {
                    if (icon.HasValue)
                    {
                        var iconSize = Math.Min(ThumbnailIconSize, Math.Min(row.Width - 12, row.Height - 30));
                        context.Icon(icon.Value, new Rectangle(row.Center.X - iconSize / 2, row.Y + 6, iconSize, iconSize), Color.White);
                    }
                    if (EffectiveUIFont != null)
                    {
                        var name = Path.GetFileName(entry);
                        var textSize = TextMetrics.Measure(EffectiveUIFont, name);
                        context.Text(EffectiveUIFont, name, new Vector2(Math.Max(row.X + 3, row.Center.X - textSize.X / 2), row.Bottom - TextMetrics.LineHeight(EffectiveUIFont) - 4), context.Theme.TextColor);
                    }
                    continue;
                }
                var textX = row.X + 4;
                if (icon.HasValue)
                {
                    context.Icon(icon.Value, new Vector2(textX, row.Center.Y - icon.Value.LogicalSize.Y / 2), Color.White);
                    textX += icon.Value.LogicalSize.X + 4;
                }
                if (EffectiveUIFont != null)
                {
                    var label = icon.HasValue ? Path.GetFileName(entry) : (isDirectory ? "> " : "  ") + Path.GetFileName(entry);
                    context.Text(EffectiveUIFont, label, new Vector2(textX, row.Y + Math.Max(1, (row.Height - TextMetrics.LineHeight(EffectiveUIFont)) / 2)), context.Theme.TextColor);
                }
            }
        }
        private Rectangle EntriesBounds
        {
            get
            {
                var sidebarVisible = IsCustomizationFlagEnabled(FileDialogCustomization.Favorites) || IsCustomizationFlagEnabled(FileDialogCustomization.Recent);
                var left = Bounds.X + (sidebarVisible ? DialogInset + SidebarWidth + 12 : DialogInset);
                var bottom = GetEntriesBottom();
                return new Rectangle(left, Bounds.Y + 104, Math.Max(0, Bounds.Right - DialogInset - left), Math.Max(0, bottom - Bounds.Y - 104));
            }
        }
        private int GetEntryIndexAt(Point point)
        {
            if (!EntriesBounds.Contains(point)) return -1;
            if (DisplayMode == FileDialogDisplayMode.Thumbnails)
            {
                var columns = Math.Max(1, (EntriesBounds.Width - 2) / ThumbnailWidth);
                var column = (point.X - EntriesBounds.X - 1) / ThumbnailWidth;
                var row = (point.Y - EntriesBounds.Y - 1) / ThumbnailHeight;
                var index = row * columns + column;
                return index >= 0 && index < _entries.Count && GetEntryRectangle(index).Contains(point) ? index : -1;
            }
            var listIndex = (int)((point.Y - EntriesBounds.Top - 1) / EntryHeight);
            return listIndex >= 0 && listIndex < _entries.Count ? listIndex : -1;
        }
        private Rectangle GetEntryRectangle(int index)
        {
            var bounds = EntriesBounds;
            if (DisplayMode == FileDialogDisplayMode.List)
                return new Rectangle(bounds.X + 1, bounds.Y + 1 + (int)(index * EntryHeight), Math.Max(0, bounds.Width - 2), (int)EntryHeight);
            var columns = Math.Max(1, (bounds.Width - 2) / ThumbnailWidth);
            return new Rectangle(bounds.X + 1 + index % columns * ThumbnailWidth, bounds.Y + 1 + index / columns * ThumbnailHeight, ThumbnailWidth - 6, ThumbnailHeight - 6);
        }
        private void ClearSelectionAfterDirectoryNavigation()
        {
            if (FileMode != FileDialogMode.SaveFile) ClearSelection();
        }
        private void SynchronizeNavigationButtons()
        {
            _backButton.Enabled = CanGoBack;
            _forwardButton.Enabled = CanGoForward;
            _upButton.Enabled = IsFileSystemAvailable && !string.IsNullOrEmpty(CurrentPath) && FileSystem.GetParentDirectory(CurrentPath) != null;
        }
        private void SynchronizeControls()
        {
            if (_directoryEdit == null) return;
            _synchronizingControls = true;
            try
            {
                _directoryEdit.Text = CurrentPath ?? string.Empty;
                _filenameEdit.Text = string.IsNullOrEmpty(CurrentFile) ? string.Empty : Path.GetFileName(CurrentFile);
                _filenameEdit.Visible = FileMode != FileDialogMode.OpenDirectory;
                _filterSelector.Visible = FileMode != FileDialogMode.OpenDirectory;
                _showHiddenButton.Visible = IsCustomizationFlagEnabled(FileDialogCustomization.HiddenFiles);
                _filenameFilterButton.Visible = IsCustomizationFlagEnabled(FileDialogCustomization.FileFilter);
                _sortSelector.Visible = IsCustomizationFlagEnabled(FileDialogCustomization.FileSort);
                _thumbnailModeButton.Visible = IsCustomizationFlagEnabled(FileDialogCustomization.Layout);
                _listModeButton.Visible = IsCustomizationFlagEnabled(FileDialogCustomization.Layout);
                _favoriteButton.Visible = IsCustomizationFlagEnabled(FileDialogCustomization.Favorites);
                _createFolderButton.Visible = EffectiveCanCreateFolders;
                _showHiddenButton.SetPressedNoSignal(ShowHiddenFiles);
                _thumbnailModeButton.SetPressedNoSignal(DisplayMode == FileDialogDisplayMode.Thumbnails);
                _listModeButton.SetPressedNoSignal(DisplayMode == FileDialogDisplayMode.List);
                _filenameFilterButton.SetPressedNoSignal(ShowFilenameFilter);
                _filenameFilterEdit.Visible = ShowFilenameFilter && IsCustomizationFlagEnabled(FileDialogCustomization.FileFilter);
                _filenameFilterEdit.Text = FilenameFilter;
                _sortSelector.Select((int)SortOption);
                if (_filterSelector.GetItemCount() > 0) _filterSelector.Select(FilterIndex);
                UpdateDirectoryLists();
            }
            finally
            {
                _synchronizingControls = false;
            }
        }
        private void SubmitDirectoryPath()
        {
            if (!IsFileSystemAvailable || string.IsNullOrWhiteSpace(_directoryEdit.Text)) return;
            var path = Path.GetFullPath(_directoryEdit.Text);
            if (FileSystem.DirectoryExists(path))
            {
                NavigateTo(path);
                ClearSelectionAfterDirectoryNavigation();
            }
            else
            {
                Message = "Folder does not exist.";
                _directoryEdit.Text = CurrentPath;
            }
        }
        public void SetRootSubfolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                _rootSubfolder = string.Empty;
                return;
            }
            var root = Path.GetFullPath(path);
            if (!FileSystem.DirectoryExists(root)) throw new DirectoryNotFoundException(root);
            _rootSubfolder = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(CurrentPath) || !IsWithinRoot(CurrentPath)) NavigateTo(_rootSubfolder);
            SynchronizeControls();
        }
        public string GetRootSubfolder() => RootSubfolder;
        private bool IsWithinRoot(string path)
        {
            if (string.IsNullOrEmpty(_rootSubfolder)) return true;
            var relative = Path.GetRelativePath(_rootSubfolder, Path.GetFullPath(path));
            return relative == "." || !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && relative != ".." && !Path.IsPathRooted(relative);
        }
        private void EnsureWithinRoot(string path)
        {
            if (!IsWithinRoot(path)) throw new UnauthorizedAccessException($"Path '{path}' is outside the FileDialog root '{_rootSubfolder}'.");
        }
        private void UpdateDriveSelector()
        {
            if (_driveSelector == null) return;
            var drives = Access == FileDialogAccess.FileSystem && IsFileSystemAvailable ? FileSystem.GetDrives() : Array.Empty<string>();
            _synchronizingControls = true;
            try
            {
                _driveSelector.Clear();
                foreach (var drive in drives) _driveSelector.AddItem(drive);
                _driveSelector.Visible = drives.Count > 1;
                if (_driveSelector.Visible)
                {
                    var current = drives.ToList().FindIndex(drive => CurrentPath.StartsWith(drive, StringComparison.OrdinalIgnoreCase));
                    _driveSelector.Select(Math.Max(0, current));
                }
            }
            finally
            {
                _synchronizingControls = false;
            }
        }
        private void SelectDrive(int index)
        {
            if (_synchronizingControls) return;
            var drives = FileSystem.GetDrives();
            if (index < 0 || index >= drives.Count) return;
            NavigateTo(drives[index]);
            ClearSelectionAfterDirectoryNavigation();
        }
        private void ShowCreateFolderDialog()
        {
            if (!EffectiveCanCreateFolders) return;
            _newDirectoryEdit.Text = string.Empty;
            _makeDirectoryDialog.PopupAt(new Vector2(
                Bounds.X + Math.Max(0, (Bounds.Width - _makeDirectoryDialog.Size.X) / 2),
                Bounds.Y + Math.Max(0, (Bounds.Height - _makeDirectoryDialog.Size.Y) / 2)));
        }
        private void ConfirmCreateFolder()
        {
            var name = _newDirectoryEdit.Text?.Trim();
            if (string.IsNullOrEmpty(name)) return;
            var path = CreateFolder(name);
            _makeDirectoryDialog.Hide();
            NavigateTo(path);
            ClearSelectionAfterDirectoryNavigation();
        }
        private void UpdateDirectoryLists()
        {
            if (_favoritesList == null) return;
            var wasSynchronizing = _synchronizingControls;
            _synchronizingControls = true;
            try
            {
                PopulateDirectoryList(_favoritesList, _favoriteList);
                PopulateDirectoryList(_recentsList, _recentList);
                var current = NormalizeDirectoryListPath(CurrentPath);
                var favoriteIndex = _favoriteList.IndexOf(current);
                _favoriteButton.SetPressedNoSignal(favoriteIndex >= 0);
                if (favoriteIndex >= 0 && favoriteIndex < _favoritesList.ItemCount) _favoritesList.SetCurrent(favoriteIndex);
                _favoriteUpButton.Enabled = favoriteIndex > 0;
                _favoriteDownButton.Enabled = favoriteIndex >= 0 && favoriteIndex < _favoriteList.Count - 1;
            }
            finally
            {
                _synchronizingControls = wasSynchronizing;
            }
        }
        private void PopulateDirectoryList(ItemList list, List<string> paths)
        {
            list.Clear();
            foreach (var normalized in paths.ToArray())
            {
                var path = normalized.TrimEnd('/');
                if (!IsFileSystemAvailable || !FileSystem.DirectoryExists(path)) continue;
                var name = Path.GetFileName(path);
                var index = list.AddItem(string.IsNullOrEmpty(name) ? path : name);
                list.SetItemMetadata(index, path);
                list.SetItemTooltip(index, normalized);
            }
        }
        private void NavigateFromDirectoryList(ItemList list, int index)
        {
            if (_synchronizingControls) return;
            if (list.GetItemMetadata(index) is not string path || !FileSystem.DirectoryExists(path)) return;
            NavigateTo(path);
            ClearSelectionAfterDirectoryNavigation();
            UpdateDirectoryLists();
        }
        private void MoveSelectedFavorite(int direction)
        {
            var index = _favoritesList.Current;
            if (index < 0 || _favoritesList.GetItemMetadata(index) is not string path) return;
            var source = _favoriteList.FindIndex(candidate => string.Equals(candidate.TrimEnd('/'), path, StringComparison.OrdinalIgnoreCase));
            var target = source + direction;
            if (source < 0 || target < 0 || target >= _favoriteList.Count) return;
            var value = _favoriteList[source];
            _favoriteList.RemoveAt(source);
            _favoriteList.Insert(target, value);
            UpdateDirectoryLists();
            _favoritesList.SetCurrent(target);
        }
        private void BuildItemMenu(bool hasEntry)
        {
            _itemMenu.Clear();
            if (hasEntry) _itemMenu.AddItem("Copy Path", ContextCopyPath);
            if (hasEntry && IsCustomizationFlagEnabled(FileDialogCustomization.Delete) && FileSystem.CanDelete) _itemMenu.AddItem("Delete", ContextDelete);
            _itemMenu.AddItem("Refresh", ContextRefresh);
            if (EffectiveCanCreateFolders) _itemMenu.AddItem("New Folder", ContextNewFolder);
            if (hasEntry && FileSystem.CanShowInFileManager) _itemMenu.AddItem("Show in File Manager", ContextShowInFileManager);
        }
        private void HandleItemMenu(int id)
        {
            var path = _contextEntryIndex >= 0 && _contextEntryIndex < _entries.Count ? _entries[_contextEntryIndex] : null;
            switch (id)
            {
                case ContextCopyPath:
                    if (path != null) Context?.Clipboard?.SetText(Path.GetFullPath(path));
                    break;
                case ContextDelete:
                    ShowDeleteConfirmation();
                    break;
                case ContextRefresh:
                    if (!string.IsNullOrEmpty(CurrentPath)) Refresh(CurrentPath);
                    break;
                case ContextNewFolder:
                    ShowCreateFolderDialog();
                    break;
                case ContextShowInFileManager:
                    if (path != null) FileSystem.ShowInFileManager(path);
                    break;
            }
        }
        private void ShowDeleteConfirmation()
        {
            if (_contextEntryIndex < 0 || _contextEntryIndex >= _entries.Count || !FileSystem.CanDelete) return;
            _deleteConfirmation.DialogText = $"Delete \"{Path.GetFileName(_entries[_contextEntryIndex])}\"?";
            _deleteConfirmation.PopupAt(new Vector2(
                Bounds.X + Math.Max(0, (Bounds.Width - _deleteConfirmation.Size.X) / 2),
                Bounds.Y + Math.Max(0, (Bounds.Height - _deleteConfirmation.Size.Y) / 2)));
        }
        private void DeleteContextEntry()
        {
            if (_contextEntryIndex < 0 || _contextEntryIndex >= _entries.Count || !FileSystem.CanDelete) return;
            var path = _entries[_contextEntryIndex];
            if (FileSystem.DirectoryExists(path)) FileSystem.DeleteDirectory(path);
            else FileSystem.DeleteFile(path);
            _deleteConfirmation.Hide();
            _contextEntryIndex = -1;
            ClearSelection();
            Refresh(CurrentPath);
        }
        private void ArrangeDirectoryLists()
        {
            var showFavorites = IsCustomizationFlagEnabled(FileDialogCustomization.Favorites);
            var showRecents = IsCustomizationFlagEnabled(FileDialogCustomization.Recent);
            _favoritesLabel.Visible = showFavorites;
            _favoritesList.Visible = showFavorites;
            _favoriteUpButton.Visible = showFavorites;
            _favoriteDownButton.Visible = showFavorites;
            _recentsLabel.Visible = showRecents;
            _recentsList.Visible = showRecents;
            if (!showFavorites && !showRecents) return;
            const float width = SidebarWidth;
            var top = 104f;
            var bottom = Math.Max(top, GetEntriesBottom() - Bounds.Y);
            var height = bottom - top;
            var favoritesHeight = showFavorites && showRecents ? height * .55f : showFavorites ? height : 0;
            if (showFavorites)
            {
                _favoritesLabel.Position = new Vector2(DialogInset, top);
                _favoritesLabel.Size = new Vector2(width - NavigationButtonSize * 2 - NavigationButtonGap, ControlHeight);
                _favoriteUpButton.Position = new Vector2(DialogInset + width - NavigationButtonSize * 2 - NavigationButtonGap, top);
                _favoriteUpButton.Size = new Vector2(NavigationButtonSize, ControlHeight);
                _favoriteDownButton.Position = new Vector2(DialogInset + width - NavigationButtonSize, top);
                _favoriteDownButton.Size = new Vector2(NavigationButtonSize, ControlHeight);
                _favoritesList.Position = new Vector2(DialogInset, top + ControlHeight + NavigationButtonGap);
                _favoritesList.Size = new Vector2(width, Math.Max(0, favoritesHeight - ControlHeight - NavigationButtonGap));
            }
            if (showRecents)
            {
                var recentTop = top + favoritesHeight;
                _recentsLabel.Position = new Vector2(DialogInset, recentTop);
                _recentsLabel.Size = new Vector2(width, ControlHeight);
                _recentsList.Position = new Vector2(DialogInset, recentTop + ControlHeight + NavigationButtonGap);
                _recentsList.Size = new Vector2(width, Math.Max(0, height - favoritesHeight - ControlHeight - NavigationButtonGap));
            }
        }
        private void RebuildOptionControls()
        {
            foreach (var control in _optionControls) RemoveChild(control);
            _optionControls.Clear();
            _selectedOptions.Clear();
            for (var optionIndex = 0; optionIndex < _options.Count; optionIndex++)
            {
                var option = _options[optionIndex];
                if (option.Values.Count == 0)
                {
                    var checkBox = new CheckBox { Name = $"FileDialogOption{optionIndex}", Text = option.Name, ButtonPressed = option.DefaultIndex != 0 };
                    checkBox.Toggled += (_, enabled) => _selectedOptions[option.Name] = enabled;
                    _selectedOptions[option.Name] = option.DefaultIndex != 0;
                    _optionControls.Add(checkBox);
                    AddChild(checkBox);
                }
                else
                {
                    var label = new Label { Name = $"FileDialogOptionLabel{optionIndex}", Text = option.Name };
                    var selector = new OptionButton { Name = $"FileDialogOption{optionIndex}", FitToLongestItem = false };
                    foreach (var value in option.Values) selector.AddItem(value);
                    selector.Select(option.DefaultIndex);
                    selector.ItemSelected += (_, index) => _selectedOptions[option.Name] = index;
                    _selectedOptions[option.Name] = option.DefaultIndex;
                    _optionControls.Add(label);
                    _optionControls.Add(selector);
                    AddChild(label);
                    AddChild(selector);
                }
            }
            QueueLayout();
        }
        private int OptionRowCount
        {
            get
            {
                var count = 0;
                for (var index = 0; index < _optionControls.Count; index++) { count++; if (_optionControls[index] is Label) index++; }
                return count;
            }
        }
        private void ArrangeOptionControls(float bottom)
        {
            var y = bottom - OptionRowCount * (ControlHeight + NavigationButtonGap);
            for (var index = 0; index < _optionControls.Count; index++)
            {
                var control = _optionControls[index];
                if (control is CheckBox)
                {
                    control.Position = new Vector2(DialogInset, y);
                    control.Size = new Vector2(Math.Max(0, Size.X - DialogInset * 2), ControlHeight);
                }
                else
                {
                    control.Position = new Vector2(DialogInset, y);
                    control.Size = new Vector2(160, ControlHeight);
                    var selector = _optionControls[++index];
                    selector.Position = new Vector2(DialogInset + 166, y);
                    selector.Size = new Vector2(Math.Max(0, Size.X - DialogInset * 2 - 166), ControlHeight);
                }
                y += ControlHeight + NavigationButtonGap;
            }
        }
        private int GetEntriesBottom()
        {
            var bottom = Bounds.Y + (int)_filenameEdit.Position.Y - 6;
            if (ShowFilenameFilter && _filenameFilterEdit.Visible) bottom = Bounds.Y + (int)_filenameFilterEdit.Position.Y - 6;
            return bottom - OptionRowCount * (ControlHeight + NavigationButtonGap);
        }
        private void SetFilenameFromEditor(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                CurrentFile = string.Empty;
                _selectedFiles.Clear();
                return;
            }
            CurrentFile = Path.GetFullPath(Path.Combine(string.IsNullOrEmpty(CurrentPath) ? FileSystem.GetCurrentDirectory() : CurrentPath, text));
            _selectedFiles.Clear();
            _selectedFiles.Add(CurrentFile);
        }
        private void UpdateFilterSelector()
        {
            if (_filterSelector == null) return;
            _synchronizingControls = true;
            try
            {
                _filterSelector.Clear();
                if (_filters.Count > 1) _filterSelector.AddItem("All Recognized");
                foreach (var filter in _filters)
                {
                    var parts = filter.Split(';');
                    _filterSelector.AddItem(parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1].Trim() : parts[0].Trim());
                }
                _filterSelector.AddItem("All Files");
                _filterSelector.Select(FilterIndex);
            }
            finally
            {
                _synchronizingControls = false;
            }
        }
        private static Button CreateNavigationButton(string name, string text, string tooltip) => new Button
        {
            Name = name,
            Text = text,
            TooltipText = tooltip,
            Padding = new Thickness(2),
            IconAlignment = HorizontalAlignment.Center,
            VerticalIconAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(NavigationButtonSize, ControlHeight),
        };
        private static Button CreateToolbarButton(string name, string text, string tooltip, bool toggle = false) => new Button
        {
            Name = name,
            Text = text,
            TooltipText = tooltip,
            ToggleMode = toggle,
            Padding = new Thickness(6, 2, 6, 2),
        };
        private void ConfigureNavigationIcon(BaseButton button, string iconName)
        {
            button.DecorativeIconProvider = () => GetThemeIcon(iconName);
            button.HideTextWhenDecorativeIconAvailable = true;
        }
        private FileDialogOption GetOption(int option, bool allowNegative = false)
        {
            if (allowNegative && option < 0) option += _options.Count;
            if (option < 0 || option >= _options.Count) throw new ArgumentOutOfRangeException(nameof(option));
            return _options[option];
        }
        private bool MatchesFilter(string name)
        {
            var patterns = GetActiveFilterPatterns();
            if (patterns == null) return true;
            foreach (var trimmed in patterns)
            {
                if (trimmed == "*" || trimmed == "*.*") return true;
                if (trimmed.StartsWith("*.", StringComparison.Ordinal) && name.EndsWith(trimmed.Substring(1), StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(trimmed, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
        /// <summary>Godot's real filter dropdown: item 0 is "All Recognized" (only when there's more than
        /// one filter), the following items are the individual filters, and the last item is always
        /// "All Files" (match everything) - matching FileDialog::update_file_list's filter->get_selected() branching.</summary>
        private int LastFilterIndex => _filters.Count == 0 ? 0 : (_filters.Count > 1 ? _filters.Count + 1 : 1);
        public int FilterIndex
        {
            get => _filterIndex;
            set
            {
                var clamped = Math.Max(0, Math.Min(value, LastFilterIndex));
                if (_filterIndex == clamped) return;
                _filterIndex = clamped;
                if (!string.IsNullOrEmpty(CurrentPath)) Refresh(CurrentPath);
            }
        }
        public void SetFilterIndex(int index) => FilterIndex = index;
        public int GetFilterIndex() => FilterIndex;
        /// <summary>Returns null to mean "match everything" (no filters registered, or "All Files" selected).</summary>
        private List<string> GetActiveFilterPatterns()
        {
            if (_filters.Count == 0 || FilterIndex == LastFilterIndex) return null;
            if (_filters.Count > 1 && FilterIndex == 0)
            {
                var all = new List<string>();
                foreach (var filter in _filters) all.AddRange(SplitFilterPatterns(filter));
                return all;
            }
            var index = FilterIndex; if (_filters.Count > 1) index--;
            return index >= 0 && index < _filters.Count ? new List<string>(SplitFilterPatterns(_filters[index])) : null;
        }
        /// <summary>Godot's filter string format is "pattern,pattern;Description" - patterns are comma
        /// separated, and everything after the first semicolon is a human-readable label, not a pattern.</summary>
        private static IEnumerable<string> SplitFilterPatterns(string filter)
        {
            if (string.IsNullOrEmpty(filter)) yield break;
            foreach (var pattern in filter.Split(';')[0].Split(','))
            {
                var trimmed = pattern.Trim();
                if (trimmed.Length > 0) yield return trimmed;
            }
        }
        private void ConfirmSaveFile()
        {
            var path = ResolveSavePath();
            if (string.IsNullOrEmpty(path)) return;
            CurrentFile = path;
            if (OverwriteWarningEnabled && FileSystem.FileExists(path))
            {
                _pendingOverwritePath = path;
                _overwriteConfirmation.DialogText = "File \"" + path + "\" already exists.\nDo you want to overwrite it?";
                _overwriteConfirmation.PopupAt(new Vector2(Bounds.X + Math.Max(0, (Bounds.Width - _overwriteConfirmation.Size.X) / 2), Bounds.Y + Math.Max(0, (Bounds.Height - _overwriteConfirmation.Size.Y) / 2)));
                return;
            }
            EmitSavedFile(path);
        }
        public void ConfirmPendingOverwrite()
        {
            if (string.IsNullOrEmpty(_pendingOverwritePath)) return;
            var path = _pendingOverwritePath;
            _pendingOverwritePath = string.Empty;
            _overwriteConfirmation.Hide();
            EmitSavedFile(path);
        }
        public void CancelPendingOverwrite()
        {
            _pendingOverwritePath = string.Empty;
            _overwriteConfirmation.Cancel();
        }
        private void EmitSavedFile(string path)
        {
            // Confirmed already fired unconditionally when Confirm() was first called (possibly turns
            // ago, if an overwrite confirmation was pending) - only the hide and the mode-specific
            // signal happen here now.
            SaveCurrentDirectoryToRecent();
            FileSelected?.Invoke(this, path);
            Hide();
        }
        private string ResolveSavePath()
        {
            var candidate = CurrentFile;
            if (string.IsNullOrWhiteSpace(candidate)) return string.Empty;
            candidate = Path.IsPathRooted(candidate)
                ? Path.GetFullPath(candidate)
                : Path.GetFullPath(Path.Combine(string.IsNullOrEmpty(CurrentPath) ? FileSystem.GetCurrentDirectory() : CurrentPath, candidate));
            return EnsureSaveExtension(candidate);
        }
        private void ApplyFileModePresentation()
        {
            // Godot's file-mode presentation calls set_default_ok_text, not set_ok_button_text, so a
            // caller's custom OkText override survives a FileMode change instead of being clobbered.
            switch (_fileMode)
            {
                case FileDialogMode.OpenFile:
                    DefaultOkText = "Open";
                    if (ModeOverridesTitle) Title = "Open a File";
                    break;
                case FileDialogMode.OpenFiles:
                    DefaultOkText = "Open";
                    if (ModeOverridesTitle) Title = "Open File(s)";
                    break;
                case FileDialogMode.OpenDirectory:
                    DefaultOkText = "Select Current Folder";
                    if (ModeOverridesTitle) Title = "Open a Directory";
                    break;
                case FileDialogMode.OpenAny:
                    DefaultOkText = "Open";
                    if (ModeOverridesTitle) Title = "Open a File or Directory";
                    break;
                case FileDialogMode.SaveFile:
                    DefaultOkText = "Save";
                    if (ModeOverridesTitle) Title = "Save a File";
                    break;
            }
        }
        private void SaveCurrentDirectoryToRecent()
        {
            if (string.IsNullOrEmpty(CurrentPath)) return;
            var directory = NormalizeDirectoryListPath(CurrentPath);
            for (var index = _recentList.Count - 1; index >= 0; index--)
                if (_recentList[index] == directory || index >= MaxRecentDirectories) _recentList.RemoveAt(index);
            _recentList.Insert(0, directory);
        }
        private static string NormalizeDirectoryListPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "/";
            var normalized = path.Replace('\\', '/');
            return normalized.EndsWith("/", StringComparison.Ordinal) ? normalized : normalized + "/";
        }
        private string EnsureSaveExtension(string path)
        {
            var patterns = GetActiveFilterPatterns();
            if (patterns == null || MatchesFilter(Path.GetFileName(path))) return path;
            foreach (var trimmed in patterns)
                if (trimmed.StartsWith("*.", StringComparison.Ordinal) && trimmed.Length > 2) return path + trimmed.Substring(1);
            return path;
        }
        private bool MatchesFilenameFilter(string name)
        {
            if (string.IsNullOrWhiteSpace(FilenameFilter)) return true;
            foreach (var pattern in FilenameFilter.Split(';')) if (WildcardMatches(name, pattern.Trim())) return true;
            return false;
        }
        private static bool WildcardMatches(string value, string pattern)
        {
            if (string.IsNullOrEmpty(pattern) || pattern == "*") return true;
            var valueIndex = 0; var patternIndex = 0; var star = -1; var match = 0;
            while (valueIndex < value.Length)
            {
                if (patternIndex < pattern.Length && (pattern[patternIndex] == '?' || char.ToUpperInvariant(pattern[patternIndex]) == char.ToUpperInvariant(value[valueIndex]))) { patternIndex++; valueIndex++; }
                else if (patternIndex < pattern.Length && pattern[patternIndex] == '*') { star = patternIndex++; match = valueIndex; }
                else if (star >= 0) { patternIndex = star + 1; valueIndex = ++match; }
                else return false;
            }
            while (patternIndex < pattern.Length && pattern[patternIndex] == '*') patternIndex++;
            return patternIndex == pattern.Length;
        }
        private int CompareEntries(string left, string right)
        {
            var directoryOrder = FileSystem.DirectoryExists(left).CompareTo(FileSystem.DirectoryExists(right));
            if (directoryOrder != 0) return -directoryOrder;
            var leftName = Path.GetFileName(left);
            var rightName = Path.GetFileName(right);
            var compareByType = SortOption == FileDialogSortOption.Type || SortOption == FileDialogSortOption.TypeReverse;
            var compareByTime = SortOption == FileDialogSortOption.ModifiedTime || SortOption == FileDialogSortOption.ModifiedTimeReverse;
            var comparison = compareByTime
                ? DateTime.Compare(FileSystem.GetLastWriteTimeUtc(left), FileSystem.GetLastWriteTimeUtc(right))
                : compareByType
                ? string.Compare(Path.GetExtension(leftName), Path.GetExtension(rightName), StringComparison.OrdinalIgnoreCase)
                : string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase);
            // Keep listings deterministic when equally typed files differ only by their filename.
            if (comparison == 0 && compareByType) comparison = string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase);
            if (SortOption == FileDialogSortOption.NameReverse || SortOption == FileDialogSortOption.TypeReverse || SortOption == FileDialogSortOption.ModifiedTimeReverse) comparison = -comparison;
            return comparison;
        }
    }
}
