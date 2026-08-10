// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// This control API and behavior are adapted from Godot Engine's OptionButton implementation;
// see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    /// <summary>Godot-style OptionButton item state.</summary>
    public sealed class OptionButtonItem
    {
        internal OptionButtonItem(string text, int id, bool separator) { Text = text ?? string.Empty; Id = id; Separator = separator; }
        public string Text { get; internal set; }
        public Texture2D Icon { get; internal set; }
        public int Id { get; internal set; }
        public object Metadata { get; internal set; }
        public string Tooltip { get; internal set; } = string.Empty;
        public AutoTranslateMode AutoTranslateMode { get; internal set; } = AutoTranslateMode.Inherit;
        public bool Disabled { get; internal set; }
        public bool Separator { get; }
    }

    /// <summary>Opens a popup menu of choices and reflects the selected item's text and icon.</summary>
    public sealed class OptionButton : BaseButton
    {
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.ComboBox;
        private readonly List<OptionButtonItem> _items = new List<OptionButtonItem>();
        public OptionButton()
        {
            // Godot's constructor sets toggle_mode(true), text_alignment(LEFT), and
            // action_mode(BUTTON_PRESS), and resets the pressed look when the popup closes.
            ToggleMode = true;
            TextAlignment = HorizontalAlignment.Left;
            ActionMode = ButtonActionMode.Press;
            Popup = new PopupMenu { Visible = false };
            // Godot's OptionButton::pressed() closes an already-open popup instead of reopening it.
            Pressed += (_, _) => { if (Popup.Visible) Popup.Hide(); else ShowPopup(); };
            Popup.IndexPressed += (_, index) => Select(index, true);
            Popup.IndexFocused += (_, index) => ItemFocused?.Invoke(this, index);
            Popup.PopupHidden += (_, _) => SetPressedNoSignal(false);
        }
        public PopupMenu Popup { get; }
        public IReadOnlyList<OptionButtonItem> ItemItems => _items;
        public IReadOnlyList<string> Items
        {
            get { var values = new List<string>(_items.Count); foreach (var item in _items) values.Add(item.Text); return values; }
        }
        public int Selected { get; private set; } = -1;
        // Godot's remove_item doesn't shift `current` down for later indices (see RemoveItem), so a
        // stale `current`/Selected can end up pointing past the shrunk item list; Godot's own
        // get_item_id/get_item_metadata gracefully fall back via the popup's own bounds check rather
        // than crash, so these guard the upper bound too, not just Selected < 0.
        public int SelectedId => Selected < 0 || Selected >= _items.Count ? -1 : _items[Selected].Id;
        public object SelectedMetadata => Selected < 0 || Selected >= _items.Count ? null : _items[Selected].Metadata;
        public bool FitToLongestItem { get; set; } = true;
        public bool AllowReselect { get; set; }
        /// <summary>Gates OptionButton's own accelerator/shortcut item activation, matching Godot's disable_shortcuts property.</summary>
        public bool DisableShortcuts { get; set; }
        public event Action<OptionButton, int> ItemSelected;
        /// <summary>Raised whenever the popup's focused item changes (keyboard navigation, incremental
        /// search, hover), matching Godot's item_focused signal wired from the popup's id_focused.</summary>
        public event Action<OptionButton, int> ItemFocused;
        /// <summary>Activates a matching item's accelerator/shortcut directly, without opening the popup, mirroring Godot's OptionButton::shortcut_input.</summary>
        internal override bool ShortcutInput(Keys key, KeyboardState keyboard)
        {
            if (!DisableShortcuts && Enabled && Visible && Popup.ActivateItemByShortcut(key, keyboard)) return true;
            return base.ShortcutInput(key, keyboard);
        }
        public int AddItem(string text, int id = -1) => AddItemCore(text, null, id, false);
        public int AddIconItem(Texture2D icon, string text, int id = -1) => AddItemCore(text, icon, id, false);
        public int AddSeparator(string text = "") => AddItemCore(text, null, -1, true);
        public void Clear() { _items.Clear(); Popup.Clear(); Selected = -1; Text = string.Empty; Icon = null; }
        public void RemoveItem(int index)
        {
            // Godot's OptionButton::remove_item only resets the selection when the removed index is
            // exactly the current one - it does NOT shift `current` down for later indices, so selecting
            // a later item then removing an earlier one leaves `current` pointing at the wrong item. A
            // real Godot quirk, matched here for behavioral parity rather than "fixed".
            ValidateIndex(index); _items.RemoveAt(index); Popup.Clear(); RebuildPopup();
            if (Selected == index) { Selected = -1; Text = string.Empty; }
        }
        public void SetItemCount(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            while (_items.Count < count) AddItem(string.Empty);
            while (_items.Count > count) RemoveItem(_items.Count - 1);
        }
        public void SetItemText(int index, string text) { ValidateIndex(index); _items[index].Text = text ?? string.Empty; Popup.SetItemText(index, _items[index].Text); if (Selected == index) Text = _items[index].Text; }
        public string GetItemText(int index) { ValidateIndex(index); return _items[index].Text; }
        public void SetItemIcon(int index, Texture2D icon) { ValidateIndex(index); _items[index].Icon = icon; Popup.SetItemIcon(index, icon); if (Selected == index) Icon = icon; }
        public Texture2D GetItemIcon(int index) { ValidateIndex(index); return _items[index].Icon; }
        public void SetItemId(int index, int id) { ValidateIndex(index); _items[index].Id = id; Popup.SetItemId(index, id); }
        public int GetItemId(int index) { ValidateIndex(index); return _items[index].Id; }
        public int GetItemIndex(int id) { for (var i = 0; i < _items.Count; i++) if (_items[i].Id == id) return i; return -1; }
        public void SetItemMetadata(int index, object metadata) { ValidateIndex(index); _items[index].Metadata = metadata; Popup.SetItemMetadata(index, metadata); }
        public object GetItemMetadata(int index) { ValidateIndex(index); return _items[index].Metadata; }
        public void SetItemTooltip(int index, string tooltip) { ValidateIndex(index); _items[index].Tooltip = tooltip ?? string.Empty; Popup.SetItemTooltip(index, _items[index].Tooltip); }
        public string GetItemTooltip(int index) { ValidateIndex(index); return _items[index].Tooltip; }
        public void SetItemAutoTranslateMode(int index, AutoTranslateMode mode)
        {
            ValidateIndex(index);
            if (!Enum.IsDefined(typeof(AutoTranslateMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            _items[index].AutoTranslateMode = mode;
            Popup.SetItemAutoTranslateMode(index, mode);
        }
        public AutoTranslateMode GetItemAutoTranslateMode(int index) { ValidateIndex(index); return _items[index].AutoTranslateMode; }
        public void SetItemDisabled(int index, bool disabled) { ValidateIndex(index); _items[index].Disabled = disabled; Popup.GetItem(index).Disabled = disabled; }
        public bool IsItemDisabled(int index) { ValidateIndex(index); return _items[index].Disabled; }
        public bool IsItemSeparator(int index) { ValidateIndex(index); return _items[index].Separator; }
        public int GetItemCount() => _items.Count;
        public void SetFitToLongestItem(bool fit) { FitToLongestItem = fit; QueueLayout(); }
        public bool IsFitToLongestItem() => FitToLongestItem;
        public void SetAllowReselect(bool allow) => AllowReselect = allow;
        public bool GetAllowReselect() => AllowReselect;
        public void SetSearchBarEnabled(bool enabled) => Popup.SetSearchBarEnabled(enabled);
        public bool IsSearchBarEnabled() => Popup.IsSearchBarEnabled();
        public void SetSearchBarMinItemCount(int count) => Popup.SetSearchBarMinItemCount(count);
        public int GetSearchBarMinItemCount() => Popup.GetSearchBarMinItemCount();
        public void SetSearchBarFuzzySearchEnabled(bool enabled) => Popup.SetSearchBarFuzzySearchEnabled(enabled);
        public bool IsSearchBarFuzzySearchEnabled() => Popup.IsSearchBarFuzzySearchEnabled();
        public void SetSearchBarFuzzySearchMaxMisses(int maxMisses) => Popup.SetSearchBarFuzzySearchMaxMisses(maxMisses);
        public int GetSearchBarFuzzySearchMaxMisses() => Popup.GetSearchBarFuzzySearchMaxMisses();
        public bool HasSelectableItems() => GetSelectableItem() >= 0;
        public int GetSelectableItem(bool fromLast = false)
        {
            if (fromLast)
            {
                for (var reverseIndex = _items.Count - 1; reverseIndex >= 0; reverseIndex--)
                    if (IsSelectable(reverseIndex)) return reverseIndex;
            }
            else
            {
                for (var forwardIndex = 0; forwardIndex < _items.Count; forwardIndex++)
                    if (IsSelectable(forwardIndex)) return forwardIndex;
            }
            return -1;
        }
        public void Select(int index) => Select(index, false);
        public void Select(int index, bool emitSignal)
        {
            if (Selected == index && !AllowReselect) return;
            if (index < 0)
            {
                for (var itemIndex = 0; itemIndex < Popup.GetItemCount(); itemIndex++) Popup.SetItemChecked(itemIndex, false);
                Selected = -1; Text = string.Empty; Icon = null; return;
            }
            // Godot's OptionButton::_select never checks selectability - select() can target a disabled
            // or separator item directly; only the popup's own UI prevents clicking one.
            ValidateIndex(index);
            for (var itemIndex = 0; itemIndex < Popup.GetItemCount(); itemIndex++) Popup.SetItemChecked(itemIndex, itemIndex == index);
            Selected = index; Text = _items[index].Text; Icon = _items[index].Icon;
            if (emitSignal) ItemSelected?.Invoke(this, index);
        }
        public void ShowPopup()
        {
            if (Context == null) return;
            if (Popup.Context != Context) Context.Add(Popup);
            Popup.LayoutDirection = LayoutDirection;
            Popup.PopupAt(new Vector2(Bounds.Left, Bounds.Bottom), new Vector2(Math.Max(Bounds.Width, Popup.CustomMinimumSize.X), 0), false);
            var focusIndex = Selected >= 0 && Selected < _items.Count && !_items[Selected].Disabled ? Selected : -1;
            if (focusIndex < 0)
            {
                for (var index = 0; index < _items.Count; index++)
                    if (!_items[index].Disabled) { focusIndex = index; break; }
            }
            if (focusIndex >= 0)
            {
                if (WasActivatedByPointer) Popup.ScrollToItem(focusIndex);
                else Popup.SetFocusedItem(focusIndex);
            }
            if (Context.ViewportSize.X > 0 || Context.ViewportSize.Y > 0)
            {
                var maxX = Context.ViewportSize.X > 0 ? Math.Max(0, Context.ViewportSize.X - Popup.Size.X) : Popup.Position.X;
                var maxY = Context.ViewportSize.Y > 0 ? Math.Max(0, Context.ViewportSize.Y - Popup.Size.Y) : Popup.Position.Y;
                Popup.Position = new Vector2(MathHelper.Clamp(Popup.Position.X, 0, maxX), MathHelper.Clamp(Popup.Position.Y, 0, maxY));
            }
        }
        public override Vector2 GetMinimumSize()
        {
            var result = base.GetMinimumSize();
            var arrow = GetThemeIcon("arrow");
            if (arrow.HasValue)
            {
                result.X += arrow.Value.LogicalSize.X + IconSeparation;
                result.Y = Math.Max(result.Y, arrow.Value.LogicalSize.Y + Padding.Vertical);
            }
            if (!FitToLongestItem || EffectiveUIFont == null) return result;
            foreach (var item in _items)
            {
                if (item.Separator) continue;
                // Matches Godot's _refresh_size_cache measuring get_minimum_size_for_text_and_icon per
                // item, folding in each item's own icon width, not just the currently-selected item's.
                var iconWidth = item.Icon != null ? item.Icon.Width + IconSeparation : 0;
                result.X = Math.Max(result.X, TextMetrics.Measure(EffectiveUIFont, item.Text).X + iconWidth + Padding.Horizontal);
            }
            return result;
        }
        private int AddItemCore(string text, Texture2D icon, int id, bool separator)
        {
            // Godot's add_item/add_icon_item check !has_selectable_items() BEFORE adding, not whether
            // Selected is currently -1 - these diverge once a previously-selected item has since become
            // disabled (Selected stays >= 0 pointing at it, but no item is actually selectable).
            var firstSelectable = !separator && !HasSelectableItems();
            var index = _items.Count;
            _items.Add(new OptionButtonItem(text, id < 0 ? index : id, separator) { Icon = icon });
            if (separator) Popup.AddSeparator(text, id < 0 ? index : id);
            else if (icon != null) Popup.AddIconRadioCheckItem(icon, text, id < 0 ? index : id);
            else Popup.AddRadioCheckItem(text, id < 0 ? index : id);
            if (firstSelectable) Select(index);
            return index;
        }
        private void RebuildPopup()
        {
            Popup.Clear();
            for (var index = 0; index < _items.Count; index++)
            {
                var item = _items[index];
                var popupItem = item.Separator ? Popup.AddSeparator(item.Text, item.Id) :
                    item.Icon != null ? Popup.AddIconRadioCheckItem(item.Icon, item.Text, item.Id) : Popup.AddRadioCheckItem(item.Text, item.Id);
                popupItem.Disabled = item.Disabled;
                popupItem.Metadata = item.Metadata;
                popupItem.Tooltip = item.Tooltip;
                popupItem.AutoTranslateMode = item.AutoTranslateMode;
                popupItem.Checked = index == Selected;
            }
        }
        private bool IsSelectable(int index) => !_items[index].Separator && !_items[index].Disabled;
        private void ValidateIndex(int index) { if (index < 0 || index >= _items.Count) throw new ArgumentOutOfRangeException(nameof(index)); }
    }

}
