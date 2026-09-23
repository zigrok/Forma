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
        private static readonly Thickness DefaultContentPadding = new Thickness(8);
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
        private TabBarSizingMode _tabSizing = TabBarSizingMode.FitContent;
        private TabBarAlignment _tabAlignment = TabBarAlignment.Left;
        private int _maxTabWidth;
        private int _currentTab;
        private int _previousTab = -1;
        private int _draggedTab = -1;
        private int _hoveredTab = -1;
        private Popup _popup;
        private bool _deselectEnabled;
        private int _tabScroll;
        private bool _scrollCurrentIntoView;
        private const int PopupButtonWidth = 20;

        /// How far a wheel notch moves the strip. Roughly a narrow tab, so a notch is a visible
        /// step without skipping past a tab you were looking for.
        private const int TabScrollWheelStep = 48;
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
                _currentTab = next; UpdateVisibility(); ScrollTabIntoView(next); TabChanged?.Invoke(this, next);
            }
        }
        /// <summary>The tab selected immediately before the current one, matching Godot's get_previous_tab.</summary>
        public int GetPreviousTab() => _previousTab;

        /// <summary>
        /// How far the tab strip is scrolled sideways, in pixels. Zero unless the tabs overflow.
        /// </summary>
        /// <remarks>
        /// Tabs keep their natural width and the strip scrolls, rather than the tabs shrinking to
        /// fit: a strip of twenty documents squeezed into the window is a strip of twenty titles
        /// nobody can read. The tab clipped at the edge is what says there is more, which is what
        /// VS Code does and costs none of an already short strip's height.
        /// </remarks>
        public int TabScrollOffset
        {
            get => _tabScroll;
            set
            {
                var clamped = Math.Max(0, value);
                if (_tabScroll == clamped) return;
                _tabScroll = clamped;
                QueueLayout();
            }
        }

        /// <summary>The largest useful <see cref="TabScrollOffset"/>: zero when every tab fits.</summary>
        public int MaxTabScroll
        {
            get
            {
                var visible = GetVisibleTabs();
                if (visible.Count == 0) return 0;

                var strip = GetTabStripRectangle();
                GetTabWidths(visible, strip.Width, out var total);
                return Math.Max(0, total - strip.Width);
            }
        }

        /// <summary>Scrolls until a tab is fully visible, if it is not already.</summary>
        /// <remarks>
        /// Selecting a tab you cannot see -- with Ctrl+Tab, by opening a document, or by closing
        /// the one in front -- has to bring it into view, or the strip shows one tab as active
        /// while a different one is on screen.
        /// </remarks>
        public void ScrollTabIntoView(int tab)
        {
            if (tab < 0) return;
            _scrollCurrentIntoView = true;
            QueueLayout();
        }
        /// <summary>Allows CurrentTab to become -1, matching Godot's deselect_enabled property.</summary>
        public bool DeselectEnabled { get => _deselectEnabled; set => _deselectEnabled = value; }
        public void SetDeselectEnabled(bool enabled) => DeselectEnabled = enabled;
        public bool GetDeselectEnabled() => DeselectEnabled;
        private float _tabHeight = DefaultTabHeight;
        private Thickness _contentPadding = DefaultContentPadding;
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
        /// <summary>Insets the selected page from the tab body chrome.</summary>
        public Thickness ContentPadding
        {
            get => _contentPadding;
            set
            {
                if (_contentPadding.Equals(value)) return;
                _contentPadding = value;
                QueueLayout();
            }
        }
        public SpriteFont Font { get => _fontSelection.SpriteFont; set { if (_fontSelection.SetSpriteFont(value)) QueueLayout(); } }
        public UIFont UIFont { get => _fontSelection.UIFont; set { if (_fontSelection.SetUIFont(value)) QueueLayout(); } }
        internal UIFont EffectiveUIFont => ResolveFont(_fontSelection);
        internal float EffectiveTabHeight => MathF.Ceiling(Math.Max(
            TabHeight,
            EffectiveUIFont == null ? DefaultTabHeight : TextMetrics.LineHeight(EffectiveUIFont) + DefaultTabVerticalPadding));
        /// <summary>Enables pointer drag reordering of tab pages, like Godot's TabContainer.</summary>
        /// <summary>
        /// How wide each tab is drawn. Defaults to <see cref="TabBarSizingMode.FitContent"/>, so a
        /// tab is as wide as its title needs.
        /// </summary>
        /// <remarks>
        /// This used to be <see cref="TabBarSizingMode.Justify"/> with no way out: every tab took
        /// an equal share of the full width, so two tabs each took half the window however short
        /// their titles. <see cref="TabBar"/> has offered the choice since it was written, and a
        /// container that wraps the same idea should not answer differently.
        ///
        /// The modes match <see cref="TabBar.TabSizing"/>: fit to content, uniform at the widest
        /// tab's size, justify to fill regardless of content, or expand -- fit to content, then
        /// share out any leftover.
        /// </remarks>
        public TabBarSizingMode TabSizing
        {
            get => _tabSizing;
            set { if (_tabSizing == value) return; _tabSizing = value; QueueLayout(); }
        }

        /// <summary>
        /// Where the tabs sit when they do not fill the strip. Ignored when they do.
        /// </summary>
        public TabBarAlignment TabAlignment
        {
            get => _tabAlignment;
            set { if (_tabAlignment == value) return; _tabAlignment = value; QueueLayout(); }
        }

        /// <summary>
        /// A ceiling on any one tab's width, or zero for none. A long file name is truncated
        /// rather than pushing every other tab off the strip.
        /// </summary>
        public int MaxTabWidth
        {
            get => _maxTabWidth;
            set { var clamped = Math.Max(0, value); if (_maxTabWidth == clamped) return; _maxTabWidth = clamped; QueueLayout(); }
        }

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
            var width = contentMin.X + ContentPadding.Horizontal + (_popup != null ? PopupButtonWidth : 0);
            return Vector2.Max(CustomMinimumSize, new Vector2(width, EffectiveTabHeight + ContentPadding.Vertical + contentMin.Y));
        }
        protected override void ArrangeChildren()
        {
            base.ArrangeChildren();
            UpdateVisibility();
            var headerHeight = EffectiveTabHeight;
            foreach (var child in Children)
            {
                child.Position = new Vector2(ContentPadding.Left, headerHeight + ContentPadding.Top);
                child.Size = new Vector2(
                    Math.Max(0, Size.X - ContentPadding.Horizontal),
                    Math.Max(0, Size.Y - headerHeight - ContentPadding.Vertical));
            }
        }
        private void UpdateVisibility() { for (var i = 0; i < Children.Count; i++) Children[i].Visible = i == CurrentTab && !GetState(i).Hidden; }
        protected internal override void PointerPressed(Point point)
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
        protected internal override void PointerMoved(Point point)
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
        protected internal override void PointerReleased(Point point, bool isInside) { _draggedTab = -1; }
        internal override void CancelInput() { _draggedTab = -1; base.CancelInput(); }

        internal override bool PointerWheel(int delta)
        {
            // A vertical wheel scrolls the strip sideways: a tab strip has no vertical extent to
            // scroll, and a mouse without a horizontal wheel is the common case.
            var maxScroll = MaxTabScroll;
            if (delta == 0 || maxScroll <= 0) return base.PointerWheel(delta);

            // Only over the strip. The wheel bubbles up from whatever is inside the active tab, so
            // without this, scrolling a document's contents would drag the tab strip along with it.
            if (!GetTabStripRectangle().Contains(Context?.PointerPosition ?? Point.Zero)) return base.PointerWheel(delta);

            var next = Math.Max(0, Math.Min(_tabScroll - Math.Sign(delta) * TabScrollWheelStep, maxScroll));
            if (next == _tabScroll) return false;

            _tabScroll = next;
            QueueLayout();
            return true;
        }
        internal void DrawTabContainerChrome(UIRenderContext context)
        {
            var headerHeight = EffectiveTabHeight;
            var body = new Rectangle(Bounds.X, Bounds.Y + (int)headerHeight - 1, Bounds.Width, Math.Max(0, Bounds.Height - (int)headerHeight + 1));
            context.Border(body, context.Theme.PanelBorderColor);
            context.Fill(new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, (int)headerHeight), context.Theme.BackgroundColor);
            // The same rectangles hit testing and the close button use. This loop used to compute
            // its own equal split, which is how the strip ended up painting half-width tabs with
            // their close buttons somewhere else entirely.
            // Scrolled tabs run past both ends of the strip, and the popup button sits just past
            // the right one. Without a clip the first casualty is the button, painted over by
            // whichever tab happens to be scrolled under it.
            context.PushClip(GetTabStripRectangle());
            try
            {
                foreach (var (i, rect) in GetTabLayouts())
                {
                    var state = GetState(i);
                    var drawRect = i == CurrentTab ? GetSelectedTabRectangle(rect, context.Theme) : rect;
                    var fill = i == CurrentTab
                        ? context.Theme.TabSelectedColor
                        : i == _hoveredTab && !state.Disabled
                            ? context.Theme.HoverColor
                            : context.Theme.BackgroundColor;
                    context.Fill(drawRect, fill); context.Border(drawRect, context.Theme.PanelBorderColor);
                    if (i == CurrentTab)
                        context.Fill(GetSelectedTabIndicatorRectangle(rect, context.Theme), context.Theme.TabSelectedIndicatorColor);
                    var textX = drawRect.X + 6;
                    if (state.Icon != null)
                    {
                        var iconHeight = Math.Max(1, Math.Min(16, drawRect.Height - 4));
                        var iconWidth = Math.Max(1, (int)MathF.Round(iconHeight * state.Icon.Width / (float)Math.Max(1, state.Icon.Height)));
                        if (state.IconMaxWidth > 0) iconWidth = Math.Min(iconWidth, state.IconMaxWidth);
                        var icon = new Rectangle(textX, drawRect.Y + (drawRect.Height - iconHeight) / 2, iconWidth, iconHeight);
                        context.SpriteBatch.Draw(state.Icon, icon, Color.White); textX = icon.Right + 4;
                    }
                    if (EffectiveUIFont != null)
                    {
                        var title = GetTabTitle(i);
                        var layout = TextMetrics.Layout(EffectiveUIFont, title);
                        context.Text(layout, new Vector2(textX, GetTabTitleY(layout, drawRect)), state.Disabled ? context.Theme.DisabledTextColor : context.Theme.TextColor);
                    }
                    if (state.ButtonIcon != null) context.SpriteBatch.Draw(state.ButtonIcon, GetTabButtonRectangle(i), Color.White);
                }
            }
            finally
            {
                context.PopClip();
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
                if (IsTabAvailable(i)) { _currentTab = i; UpdateVisibility(); ScrollTabIntoView(i); return; }
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
            if (point.Y < Bounds.Top || point.Y >= Bounds.Top + EffectiveTabHeight) return -1;

            // Walks the same rectangles that get drawn rather than recomputing a width. Deriving
            // the index arithmetically only works while every tab is the same width, which is
            // exactly what FitContent stops being true.
            foreach (var (tab, rect) in GetTabLayouts())
            {
                if (rect.Contains(point)) return tab;
            }

            return -1;
        }
        /// <summary>
        /// Where a tab is drawn, in global coordinates, or <see cref="Rectangle.Empty"/> when it is
        /// hidden or out of range. Public to match <see cref="TabBar.GetTabRect"/>.
        /// </summary>
        public Rectangle GetTabRect(int tab) => GetTabRectangle(tab);

        private Rectangle GetTabRectangle(int tab)
        {
            foreach (var (candidate, rect) in GetTabLayouts())
            {
                if (candidate == tab) return rect;
            }

            return Rectangle.Empty;
        }

        /// <summary>
        /// Every visible tab's rectangle, in strip order.
        /// </summary>
        /// <remarks>
        /// One place, because drawing, hit testing and the close button's position all have to
        /// agree. They were three separate width calculations, and they agreed only because every
        /// tab was the same size.
        /// </remarks>
        private List<(int Tab, Rectangle Rect)> GetTabLayouts()
        {
            var layouts = new List<(int, Rectangle)>();
            var visible = GetVisibleTabs();
            if (visible.Count == 0) return layouts;

            var strip = GetTabStripRectangle();
            var widths = GetTabWidths(visible, strip.Width, out var total);

            // Scrolling and alignment are alternatives, not a pair: alignment distributes slack,
            // and a strip that overflows has none. Justify and Expand fill the strip by
            // construction, so they can never overflow and never scroll.
            var maxScroll = Math.Max(0, total - strip.Width);
            ApplyPendingTabScroll(visible, widths, strip.Width, maxScroll);
            _tabScroll = Math.Max(0, Math.Min(_tabScroll, maxScroll));

            var x = strip.X - _tabScroll;
            if (maxScroll == 0 && TabSizing != TabBarSizingMode.Justify)
            {
                x += TabAlignment switch
                {
                    TabBarAlignment.Center => (strip.Width - total) / 2,
                    TabBarAlignment.Right => strip.Width - total,
                    _ => 0,
                };
            }

            for (var index = 0; index < visible.Count; index++)
            {
                layouts.Add((visible[index], new Rectangle(x, Bounds.Y, widths[index], (int)EffectiveTabHeight)));
                x += widths[index];
            }

            return layouts;
        }

        /// <summary>
        /// Honours a pending <see cref="ScrollTabIntoView"/> now that the widths are known.
        /// </summary>
        /// <remarks>
        /// Deferred rather than done in the setter because selecting a tab does not need layout to
        /// have run -- a document can be opened and made current before the strip has ever been
        /// measured, and at that point every width is zero and the answer would be nonsense.
        /// </remarks>
        private void ApplyPendingTabScroll(List<int> visible, List<int> widths, int stripWidth, int maxScroll)
        {
            if (!_scrollCurrentIntoView) return;
            _scrollCurrentIntoView = false;
            if (maxScroll <= 0) { _tabScroll = 0; return; }

            var target = visible.IndexOf(CurrentTab);
            if (target < 0) return;

            var start = 0;
            for (var index = 0; index < target; index++) start += widths[index];
            var end = start + widths[target];

            // Nearest edge: a tab off the left scrolls to sit against the left, one off the right
            // scrolls just far enough to show its trailing edge. Centring it instead would move
            // the strip under the pointer on every ordinary click near an edge.
            if (start < _tabScroll) _tabScroll = start;
            else if (end > _tabScroll + stripWidth) _tabScroll = end - stripWidth;
        }

        /// The width of each visible tab under the current sizing mode. Mirrors TabBar's.
        private List<int> GetTabWidths(List<int> visible, int stripWidth, out int total)
        {
            var widths = new List<int>(visible.Count);
            var widest = 0;
            total = 0;

            foreach (var tab in visible)
            {
                var width = GetDesiredTabWidth(tab);
                widths.Add(width);
                total += width;
                widest = Math.Max(widest, width);
            }

            switch (TabSizing)
            {
                case TabBarSizingMode.Uniform:
                    total = widest * widths.Count;
                    for (var index = 0; index < widths.Count; index++) widths[index] = widest;
                    break;

                case TabBarSizingMode.Justify:
                {
                    var width = Math.Max(1, stripWidth / widths.Count);
                    total = width * widths.Count;
                    for (var index = 0; index < widths.Count; index++) widths[index] = width;

                    // The last tab absorbs the rounding, so the strip is filled exactly rather
                    // than leaving a sliver of background at the right edge.
                    widths[^1] += stripWidth - total;
                    total = stripWidth;
                    break;
                }

                case TabBarSizingMode.Expand when total < stripWidth:
                {
                    var extra = (stripWidth - total) / widths.Count;
                    total = 0;
                    for (var index = 0; index < widths.Count; index++)
                    {
                        widths[index] += extra;
                        total += widths[index];
                    }

                    // The last tab takes the remainder, as in Justify. Dividing the slack by the
                    // tab count loses up to count-1 pixels, and a mode whose whole purpose is to
                    // use the space should not leave a sliver of background at the right edge.
                    widths[^1] += stripWidth - total;
                    total = stripWidth;
                    break;
                }
            }

            return widths;
        }

        /// <summary>
        /// How much room a tab's contents need: its title, plus whatever is drawn beside it.
        /// </summary>
        private int GetDesiredTabWidth(int tab)
        {
            var state = GetState(tab);
            var title = state.Title ?? Children[tab].Name ?? string.Empty;

            // The fallback matters on the frame before a font resolves: a zero-width tab cannot be
            // clicked, and eight pixels a character is closer than nothing.
            var text = EffectiveUIFont == null
                ? title.Length * 8
                : (int)MathF.Ceiling(TextMetrics.Measure(EffectiveUIFont, title).X);

            var width = Math.Max(32, text + 12 + (state.Icon == null ? 0 : 20) + (state.ButtonIcon == null ? 0 : 18));
            return MaxTabWidth > 0 ? Math.Min(width, MaxTabWidth) : width;
        }
        internal Rectangle GetSelectedTabRectangle()
        {
            if (CurrentTab < 0) return Rectangle.Empty;
            var rect = GetTabRectangle(CurrentTab);
            return rect == Rectangle.Empty ? Rectangle.Empty : GetSelectedTabRectangle(rect, Context?.Theme ?? new Theme());
        }
        internal Rectangle GetSelectedTabIndicatorRectangle()
        {
            if (CurrentTab < 0) return Rectangle.Empty;
            var rect = GetTabRectangle(CurrentTab);
            return rect == Rectangle.Empty ? Rectangle.Empty : GetSelectedTabIndicatorRectangle(rect, Context?.Theme ?? new Theme());
        }
        private static Rectangle GetSelectedTabRectangle(Rectangle rect, Theme theme)
        {
            var lift = Math.Max(0, (int)MathF.Ceiling(theme.TabSelectedLift));
            return new Rectangle(rect.X, rect.Y - lift, rect.Width, rect.Height + lift);
        }
        private static Rectangle GetSelectedTabIndicatorRectangle(Rectangle rect, Theme theme)
        {
            var selected = GetSelectedTabRectangle(rect, theme);
            var height = Math.Min(selected.Height, Math.Max(0, (int)MathF.Ceiling(theme.TabSelectedIndicatorHeight)));
            return new Rectangle(selected.X + 1, selected.Y, Math.Max(0, selected.Width - 2), height);
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
