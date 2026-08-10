// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// This control API and behavior are adapted from Godot Engine's TabContainer implementation;
// see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    /// <summary>Displays one child page at a time and provides tabs for switching the selected page.</summary>
    public sealed class TabContainer : TemplatedControl
    {
        private const float DefaultTabHeight = 28;
        private const float DefaultTabVerticalPadding = 10;
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.TabPanel;
        private readonly UIFontSelection _fontSelection = new UIFontSelection();
        private sealed class TabPageState
        {
            public string Title;
            public Texture2D Icon;
            public Texture2D ButtonIcon;
            public string Tooltip = string.Empty;
            public object Metadata;
            public int IconMaxWidth;
            public bool Disabled;
            public bool Hidden;
        }

        private readonly Dictionary<Control, TabPageState> _tabStates = new Dictionary<Control, TabPageState>();
        private int _currentTab;
        private int _previousTab = -1;
        private int _draggedTab = -1;
        private int _hoveredTab = -1;
        private Popup _popup;
        private bool _deselectEnabled;
        private const int PopupButtonWidth = 20;
        public int CurrentTab
        {
            get => _currentTab;
            set
            {
                if (Children.Count == 0) { _currentTab = 0; return; }
                if (value == -1)
                {
                    if (!DeselectEnabled) throw new InvalidOperationException("Cannot deselect tabs when deselection is disabled.");
                    _previousTab = _currentTab;
                    TabSelected?.Invoke(this, -1);
                    if (_currentTab == -1) return;
                    _currentTab = -1; UpdateVisibility();
                    TabChanged?.Invoke(this, -1);
                    return;
                }
                var next = Math.Max(0, Math.Min(value, Children.Count - 1));
                // Godot's TabContainer::set_current_tab forwards straight to the internal TabBar's
                // set_current_tab, which has no disabled/hidden guard - only clicking a tab blocks that.
                _previousTab = _currentTab;
                TabSelected?.Invoke(this, next);
                if (_currentTab == next) return;
                _currentTab = next; UpdateVisibility(); TabChanged?.Invoke(this, next);
            }
        }
        /// <summary>The tab selected immediately before the current one, matching Godot's get_previous_tab.</summary>
        public int GetPreviousTab() => _previousTab;
        /// <summary>Allows CurrentTab to become -1, matching Godot's deselect_enabled property.</summary>
        public bool DeselectEnabled { get => _deselectEnabled; set => _deselectEnabled = value; }
        public void SetDeselectEnabled(bool enabled) => DeselectEnabled = enabled;
        public bool GetDeselectEnabled() => DeselectEnabled;
        private float _tabHeight = DefaultTabHeight;
        /// <summary>Minimum tab-header height. The effective height grows to fit the active font with balanced vertical padding.</summary>
        public float TabHeight
        {
            get => _tabHeight;
            set
            {
                var validated = Math.Max(0, value);
                if (Math.Abs(_tabHeight - validated) <= float.Epsilon) return;
                _tabHeight = validated;
                QueueLayout();
            }
        }
        public SpriteFont Font { get => _fontSelection.SpriteFont; set { _fontSelection.SetSpriteFont(value); QueueLayout(); } }
        public UIFont UIFont { get => _fontSelection.UIFont; set { _fontSelection.SetUIFont(value); QueueLayout(); } }
        internal UIFont EffectiveUIFont => ResolveFont(_fontSelection);
        internal float EffectiveTabHeight => MathF.Ceiling(Math.Max(
            TabHeight,
            EffectiveUIFont == null ? DefaultTabHeight : TextMetrics.LineHeight(EffectiveUIFont) + DefaultTabVerticalPadding));
        /// <summary>Enables pointer drag reordering of tab pages, like Godot's TabContainer.</summary>
        public bool DragToRearrangeEnabled { get; set; }
        /// <summary>Optional group identifier reserved for compatibility with Godot tab-container rearrangement groups.</summary>
        public int TabsRearrangeGroup { get; set; } = -1;
        public event Action<TabContainer, int> TabChanged;
        /// <summary>Raised on every tab selection attempt, even reselecting the current tab, matching Godot's tab_selected signal (TabChanged only fires when the selection actually changes).</summary>
        public event Action<TabContainer, int> TabSelected;
        /// <summary>Raised when a (non-disabled) tab is clicked, matching Godot's tab_clicked signal.</summary>
        public event Action<TabContainer, int> TabClicked;
        /// <summary>Raised when the pointer enters a tab header, matching Godot's tab_hovered signal.</summary>
        public event Action<TabContainer, int> TabHovered;
        /// <summary>Raised when a tab's configured button icon is pressed.</summary>
        public event Action<TabContainer, int> TabButtonPressed;
        /// <summary>Raised while the selected tab page is moved through the header strip.</summary>
        public event Action<TabContainer, int> ActiveTabRearranged;
        /// <summary>Raised immediately before the attached popup is shown, matching Godot's pre_popup_pressed signal.</summary>
        public event EventHandler PrePopupPressed;
        /// <summary>Returns the page Control at the given tab index, matching Godot's get_tab_control.</summary>
        public Control GetTabControl(int tab) { if (tab < 0 || tab >= Children.Count) throw new ArgumentOutOfRangeException(nameof(tab)); return Children[tab]; }
        /// <summary>Returns the currently selected page Control, or null when deselected, matching Godot's get_current_tab_control.</summary>
        public Control GetCurrentTabControl() => CurrentTab >= 0 && CurrentTab < Children.Count ? Children[CurrentTab] : null;
        /// <summary>Selects the nearest available tab before the current one, wrapping around; returns whether one was found, matching Godot's select_previous_available.</summary>
        public bool SelectPreviousAvailable() => SelectAvailableTab(-1);
        /// <summary>Selects the nearest available tab after the current one, wrapping around; returns whether one was found, matching Godot's select_next_available.</summary>
        public bool SelectNextAvailable() => SelectAvailableTab(1);
        public void NextTab() => SelectAvailableTab(1);
        public void PreviousTab() => SelectAvailableTab(-1);
        /// <summary>Attaches an arbitrary popup opened from a header button, matching Godot's TabContainer::set_popup.</summary>
        public void SetPopup(Popup popup)
        {
            if (_popup == popup) return;
            var hadPopup = _popup != null;
            _popup = popup;
            if (hadPopup != (popup != null)) QueueLayout();
        }
        /// <summary>Returns the popup attached with <see cref="SetPopup"/>, matching Godot's TabContainer::get_popup.</summary>
        public Popup GetPopup() => _popup;
        /// <summary>Returns the header rectangle of the popup button, empty when no popup is attached.</summary>
        public Rectangle GetPopupButtonRectangle()
        {
            if (_popup == null) return Rectangle.Empty;
            var headerHeight = EffectiveTabHeight;
            return IsLayoutRtl()
                ? new Rectangle(Bounds.X, Bounds.Y, PopupButtonWidth, (int)headerHeight)
                : new Rectangle(Bounds.Right - PopupButtonWidth, Bounds.Y, PopupButtonWidth, (int)headerHeight);
        }
        private void ShowPopupAtButton()
        {
            if (_popup == null) return;
            PrePopupPressed?.Invoke(this, EventArgs.Empty);
            if (Context != null && _popup.Context != Context) Context.Add(_popup);
            var button = GetPopupButtonRectangle();
            var x = IsLayoutRtl() ? button.X : button.X + button.Width - (int)_popup.Size.X;
            var y = button.Bottom;
            _popup.PopupAt(new Vector2(x, y));
        }
        public string GetTabTitle(int tab) => GetState(tab).Title ?? Children[tab].Name ?? string.Empty;
        public void SetTabTitle(int tab, string title) { GetState(tab).Title = title ?? string.Empty; QueueLayout(); }
        public Texture2D GetTabIcon(int tab) => GetState(tab).Icon;
        public void SetTabIcon(int tab, Texture2D icon) { GetState(tab).Icon = icon; QueueLayout(); }
        public Texture2D GetTabButtonIcon(int tab) => GetState(tab).ButtonIcon;
        public void SetTabButtonIcon(int tab, Texture2D icon) => GetState(tab).ButtonIcon = icon;
        public string GetTabTooltip(int tab) => GetState(tab).Tooltip;
        public void SetTabTooltip(int tab, string tooltip) => GetState(tab).Tooltip = tooltip ?? string.Empty;
        public object GetTabMetadata(int tab) => GetState(tab).Metadata;
        public void SetTabMetadata(int tab, object metadata) => GetState(tab).Metadata = metadata;
        public int GetTabIconMaxWidth(int tab) => GetState(tab).IconMaxWidth;
        public void SetTabIconMaxWidth(int tab, int width) { GetState(tab).IconMaxWidth = Math.Max(0, width); QueueLayout(); }
        public bool IsTabDisabled(int tab) => GetState(tab).Disabled;
        public void SetTabDisabled(int tab, bool disabled) { GetState(tab).Disabled = disabled; EnsureCurrentTab(); QueueLayout(); }
        public bool IsTabHidden(int tab) => GetState(tab).Hidden;
        public void SetTabHidden(int tab, bool hidden) { GetState(tab).Hidden = hidden; EnsureCurrentTab(); QueueLayout(); }
        /// <summary>Moves a tab page while preserving the selected page.</summary>
        public void MoveTab(int from, int to)
        {
            if (from < 0 || from >= Children.Count) throw new ArgumentOutOfRangeException(nameof(from));
            if (to < 0 || to >= Children.Count) throw new ArgumentOutOfRangeException(nameof(to));
            if (from == to) return;
            MoveChild(Children[from], to);
            if (_currentTab == from) _currentTab = to;
            else if (_currentTab > from && _currentTab <= to) _currentTab--;
            else if (_currentTab < from && _currentTab >= to) _currentTab++;
            UpdateVisibility();
        }
        public override string GetTooltip(Point position)
        {
            var tab = GetTabAt(position);
            return tab >= 0 && !string.IsNullOrEmpty(GetState(tab).Tooltip) ? GetState(tab).Tooltip : base.GetTooltip(position);
        }
        /// <summary>Matches Godot's TabContainer::_get_minimum_size: the tab-bar height plus (by
        /// default) only the CURRENT page's minimum size, not the max over every page - Godot only
        /// folds in every page when use_hidden_tabs_for_min_size is enabled, which this port doesn't
        /// model. The popup button, when attached, adds its width like Godot's popup_button branch.</summary>
        public override Vector2 GetMinimumSize()
        {
            var current = CurrentTab >= 0 && CurrentTab < Children.Count ? Children[CurrentTab] : null;
            var contentMin = current?.GetMinimumSize() ?? Vector2.Zero;
            var width = contentMin.X + (_popup != null ? PopupButtonWidth : 0);
            return Vector2.Max(CustomMinimumSize, new Vector2(width, EffectiveTabHeight + contentMin.Y));
        }
        protected override void ArrangeChildren()
        {
            base.ArrangeChildren();
            UpdateVisibility();
            var headerHeight = EffectiveTabHeight;
            foreach (var child in Children) { child.Position = new Vector2(0, headerHeight); child.Size = new Vector2(Size.X, Math.Max(0, Size.Y - headerHeight)); }
        }
        private void UpdateVisibility() { for (var i = 0; i < Children.Count; i++) Children[i].Visible = i == CurrentTab && !GetState(i).Hidden; }
        internal override void PointerPressed(Point point)
        {
            if (_popup != null && GetPopupButtonRectangle().Contains(point)) { ShowPopupAtButton(); return; }
            if (point.Y >= Bounds.Top && point.Y < Bounds.Top + EffectiveTabHeight && Children.Count > 0)
            {
                var index = GetTabAt(point);
                UpdateHoveredTab(point);
                if (index >= 0 && GetState(index).ButtonIcon != null && GetTabButtonRectangle(index).Contains(point))
                {
                    TabButtonPressed?.Invoke(this, index);
                    GrabFocus();
                    return;
                }
                // Godot's gui_input only selects/emits tab_clicked for a hit, non-disabled tab.
                if (index >= 0 && !GetState(index).Disabled)
                {
                    CurrentTab = index;
                    TabClicked?.Invoke(this, index);
                }
                _draggedTab = DragToRearrangeEnabled ? index : -1;
                GrabFocus();
            }
            else base.PointerPressed(point);
        }
        internal override void PointerMoved(Point point)
        {
            UpdateHoveredTab(point);
            if (_draggedTab < 0) return;
            var target = GetTabAt(point);
            if (target == _draggedTab) return;
            MoveTab(_draggedTab, target);
            _draggedTab = target;
            ActiveTabRearranged?.Invoke(this, target);
        }
        internal override void PointerEntered() { UpdateHoveredTab(Context?.PointerPosition ?? Point.Zero); base.PointerEntered(); }
        internal override void PointerExited() { _hoveredTab = -1; base.PointerExited(); }
        internal override void PointerReleased(Point point, bool isInside) { _draggedTab = -1; }
        internal void DrawTabContainerChrome(UIRenderContext context)
        {
            var headerHeight = EffectiveTabHeight;
            context.Fill(new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, (int)headerHeight), context.Theme.BackgroundColor);
            var strip = GetTabStripRectangle();
            var visible = GetVisibleTabs();
            var width = visible.Count == 0 ? strip.Width : Math.Max(1, strip.Width / visible.Count);
            for (var order = 0; order < visible.Count; order++)
            {
                var i = visible[order]; var state = GetState(i);
                var rect = new Rectangle(strip.X + width * order, Bounds.Y, order == visible.Count - 1 ? strip.Right - (strip.X + width * order) : width, (int)headerHeight);
                context.Fill(rect, i == CurrentTab ? context.Theme.PanelColor : context.Theme.BackgroundColor); context.Border(rect, context.Theme.PanelBorderColor);
                var textX = rect.X + 6;
                if (state.Icon != null)
                {
                    var iconHeight = Math.Max(1, Math.Min(16, rect.Height - 4));
                    var iconWidth = Math.Max(1, (int)MathF.Round(iconHeight * state.Icon.Width / (float)Math.Max(1, state.Icon.Height)));
                    if (state.IconMaxWidth > 0) iconWidth = Math.Min(iconWidth, state.IconMaxWidth);
                    var icon = new Rectangle(textX, rect.Y + (rect.Height - iconHeight) / 2, iconWidth, iconHeight);
                    context.SpriteBatch.Draw(state.Icon, icon, Color.White); textX = icon.Right + 4;
                }
                if (EffectiveUIFont != null)
                {
                    var title = GetTabTitle(i);
                    var layout = TextMetrics.Layout(EffectiveUIFont, title);
                    context.Text(layout, new Vector2(textX, GetTabTitleY(layout, rect)), state.Disabled ? context.Theme.DisabledTextColor : context.Theme.TextColor);
                }
                if (state.ButtonIcon != null) context.SpriteBatch.Draw(state.ButtonIcon, GetTabButtonRectangle(i), Color.White);
            }
            if (_popup != null)
            {
                var button = GetPopupButtonRectangle();
                var hovered = button.Contains(Context?.PointerPosition ?? Point.Zero);
                var menu = GetThemeIcon(hovered ? "menu_highlight" : "menu");
                if (menu.HasValue) context.Icon(menu.Value, new Vector2(button.Center.X - menu.Value.LogicalSize.X / 2, button.Center.Y - menu.Value.LogicalSize.Y / 2), Color.White);
            }
        }
        internal static float GetTabTitleY(TextLayout layout, Rectangle header)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (layout.VisibleGlyphs.Count == 0)
                return header.Y + Math.Max(2, (header.Height - layout.Size.Y) / 2);
            var top = float.MaxValue;
            var bottom = float.MinValue;
            foreach (var glyph in layout.VisibleGlyphs)
            {
                top = Math.Min(top, glyph.Bounds.Top);
                bottom = Math.Max(bottom, glyph.Bounds.Bottom);
            }
            return header.Y + header.Height / 2f - (top + bottom) / 2f;
        }
        private TabPageState GetState(int tab)
        {
            if (tab < 0 || tab >= Children.Count) throw new ArgumentOutOfRangeException(nameof(tab));
            var child = Children[tab];
            if (!_tabStates.TryGetValue(child, out var state)) { state = new TabPageState(); _tabStates.Add(child, state); }
            return state;
        }
        private bool IsTabAvailable(int tab) { var state = GetState(tab); return !state.Disabled && !state.Hidden; }
        private void EnsureCurrentTab()
        {
            if (Children.Count == 0 || IsTabAvailable(_currentTab)) { UpdateVisibility(); return; }
            for (var i = 0; i < Children.Count; i++)
                if (IsTabAvailable(i)) { _currentTab = i; UpdateVisibility(); return; }
            UpdateVisibility();
        }
        private bool SelectAvailableTab(int direction)
        {
            if (Children.Count == 0) return false;
            // Godot's get_next_available/get_previous_available only ever visit the OTHER tabs once
            // each (loop bound Children.Count - 1 steps) - they never wrap back around to re-consider
            // the current tab itself as a valid "next available" result.
            for (var i = 1; i < Children.Count; i++)
            {
                var tab = (_currentTab + direction * i + Children.Count) % Children.Count;
                if (IsTabAvailable(tab)) { CurrentTab = tab; return true; }
            }
            return false;
        }
        private List<int> GetVisibleTabs()
        {
            var tabs = new List<int>();
            for (var i = 0; i < Children.Count; i++) if (!GetState(i).Hidden) tabs.Add(i);
            return tabs;
        }
        private int GetTabAt(Point point)
        {
            var visible = GetVisibleTabs();
            if (visible.Count == 0 || point.Y < Bounds.Top || point.Y >= Bounds.Top + EffectiveTabHeight) return -1;
            var strip = GetTabStripRectangle();
            var width = Math.Max(1, strip.Width / visible.Count);
            var order = MathHelper.Clamp((point.X - strip.Left) / width, 0, visible.Count - 1);
            return point.X < strip.Left || point.X >= strip.Right ? -1 : visible[order];
        }
        private Rectangle GetTabRectangle(int tab)
        {
            var visible = GetVisibleTabs();
            var order = visible.IndexOf(tab);
            if (order < 0) return Rectangle.Empty;
            var strip = GetTabStripRectangle();
            var width = Math.Max(1, strip.Width / visible.Count);
            var x = strip.X + width * order;
            return new Rectangle(x, Bounds.Y, order == visible.Count - 1 ? strip.Right - x : width, (int)EffectiveTabHeight);
        }
        private Rectangle GetTabButtonRectangle(int tab)
        {
            var rect = GetTabRectangle(tab);
            return rect == Rectangle.Empty ? Rectangle.Empty : new Rectangle(rect.Right - 14, rect.Y + Math.Max(3, (rect.Height - 10) / 2), 10, 10);
        }
        private void UpdateHoveredTab(Point point)
        {
            var hovered = GetTabAt(point);
            if (hovered == _hoveredTab) return;
            _hoveredTab = hovered;
            if (hovered >= 0) TabHovered?.Invoke(this, hovered);
        }
        /// <summary>Returns the header rectangle available for tabs, excluding the popup button's reserved space.</summary>
        private Rectangle GetTabStripRectangle()
        {
            var headerHeight = (int)EffectiveTabHeight;
            if (_popup == null) return new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, headerHeight);
            var width = Math.Max(0, Bounds.Width - PopupButtonWidth);
            var x = IsLayoutRtl() ? Bounds.X + PopupButtonWidth : Bounds.X;
            return new Rectangle(x, Bounds.Y, width, headerHeight);
        }
    }

}
