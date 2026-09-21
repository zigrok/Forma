// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// This control API and behavior are adapted from Godot Engine's ScrollContainer implementation;
// see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    /// <summary>Visual overflow hint placement, corresponding to Godot's <c>ScrollContainer.ScrollHintMode</c>.</summary>
    public enum ScrollContainerScrollHintMode { Disabled, All, TopAndLeft, BottomAndRight }

    /// <summary>Clips content to a viewport and provides horizontal and vertical scrolling when it overflows.</summary>
    [TemplatePart(ScrollPresenterPartName, typeof(ScrollPresenter))]
    public sealed class ScrollContainer : TemplatedControl, IScrollViewportOwner
    {
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.ScrollView;
        public override AccessibilityActions AccessibilityActions => base.AccessibilityActions | AccessibilityActions.Scroll;
        public const string ScrollPresenterPartName = "PART_ScrollPresenter";
        private readonly ScrollViewportController _viewportController = new ScrollViewportController();
        private Vector2 _viewportSize;
        private readonly HScrollBar _horizontalScrollBar;
        private readonly VScrollBar _verticalScrollBar;
        private Control _content;
        private ScrollPresenter _scrollPresenter;
        private ScrollBarVisibility _horizontalScrollMode = ScrollBarVisibility.Auto;
        private ScrollBarVisibility _verticalScrollMode = ScrollBarVisibility.Auto;
        private Control _followedFocus;
        private Vector2 _focusScrollDiff;

        public ScrollContainer()
        {
            ClipContents = true;
            _horizontalScrollBar = new HScrollBar { ZIndex = 1, Visible = false, IsXamlInfrastructure = true };
            _verticalScrollBar = new VScrollBar { ZIndex = 1, Visible = false, IsXamlInfrastructure = true };
            _horizontalScrollBar.ValueChanged += (_, value) => ScrollOffset = new Vector2(value, ScrollOffset.Y);
            _verticalScrollBar.ValueChanged += (_, value) => ScrollOffset = new Vector2(ScrollOffset.X, value);
            _viewportController.ScrollStarted += (_, _) => ScrollStarted?.Invoke(this, EventArgs.Empty);
            _viewportController.ScrollEnded += (_, _) => ScrollEnded?.Invoke(this, EventArgs.Empty);
            _viewportController.MetricsChanged += OnViewportMetricsChanged;
            base.AddChild(_horizontalScrollBar);
            base.AddChild(_verticalScrollBar);
            ApplyTemplate();
        }

        public Control Content
        {
            get => _content;
            set
            {
                if (ReferenceEquals(_content, value)) return;
                if (value == _horizontalScrollBar || value == _verticalScrollBar)
                    throw new InvalidOperationException("ScrollContainer scrollbar chrome cannot be assigned as content.");
                if (value != null && value.Parent != null && value.Parent != this)
                    throw new InvalidOperationException("Scroll content is already owned by another control.");
                var previous = _content;
                if (value != null && value.Parent == null) base.AddChild(value);
                try
                {
                    if (_scrollPresenter != null) _scrollPresenter.Content = value;
                    _content = value;
                    if (previous?.Parent == this) base.RemoveChild(previous);
                    OnPropertyChanged(nameof(Content));
                    QueueLayout();
                }
                catch
                {
                    if (value?.Parent == this) base.RemoveChild(value);
                    if (previous != null && previous.Parent == null) base.AddChild(previous);
                    if (_scrollPresenter != null) _scrollPresenter.Content = previous;
                    throw;
                }
            }
        }

        public override void AddChild(Control child)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            if (child == _horizontalScrollBar || child == _verticalScrollBar)
            {
                base.AddChild(child);
                return;
            }
            if (_content != null && !ReferenceEquals(_content, child))
                throw new InvalidOperationException("ScrollContainer accepts one content control.");
            Content = child;
        }
        public Vector2 ScrollOffset
        {
            get => _viewportController.Offset;
            set
            {
                UpdateControllerMetrics();
                _viewportController.Offset = value;
                _horizontalScrollBar.SetValueNoSignal(ScrollOffset.X);
                _verticalScrollBar.SetValueNoSignal(ScrollOffset.Y);
                _scrollPresenter?.QueueLayout();
                QueueLayout();
            }
        }
        /// <summary>Horizontal scroll position, corresponding to Godot's h_scroll property.</summary>
        public int HorizontalScroll { get => (int)ScrollOffset.X; set { ScrollOffset = new Vector2(value, ScrollOffset.Y); CancelTouchDragScroll(); } }
        /// <summary>Vertical scroll position, corresponding to Godot's v_scroll property.</summary>
        public int VerticalScroll { get => (int)ScrollOffset.Y; set { ScrollOffset = new Vector2(ScrollOffset.X, value); CancelTouchDragScroll(); } }
        /// <summary>Forwards to the underlying scrollbar's own CustomStep, matching Godot's
        /// set_horizontal_custom_step/set_vertical_custom_step, which themselves just forward to
        /// h_scroll/v_scroll's set_custom_step - this has no effect on mouse-wheel scroll amount,
        /// which Godot always computes from the scrollbar's page instead (see PointerWheel).</summary>
        public float HorizontalCustomStep { get => _horizontalScrollBar.CustomStep; set => _horizontalScrollBar.CustomStep = value; }
        public float VerticalCustomStep { get => _verticalScrollBar.CustomStep; set => _verticalScrollBar.CustomStep = value; }
        /// <summary>Gets or sets whether the horizontal scrollbar shows its decrement and increment buttons.</summary>
        public bool ShowHorizontalStepButtons { get => _horizontalScrollBar.ShowStepButtons; set => _horizontalScrollBar.ShowStepButtons = value; }
        /// <summary>Gets or sets whether the vertical scrollbar shows its decrement and increment buttons.</summary>
        public bool ShowVerticalStepButtons { get => _verticalScrollBar.ShowStepButtons; set => _verticalScrollBar.ShowStepButtons = value; }
        /// <summary>Scrolls the focused descendant into view as focus changes, matching Godot's follow_focus property.</summary>
        public bool FollowFocus { get; set; }
        /// <summary>Scrolls while a retained drag hovers near an edge, matching Godot's scroll_on_drag_hover property.</summary>
        public bool ScrollOnDragHover { get; set; }
        public int DragHoverScrollBorder { get; set; } = 20;
        public float DragHoverScrollSpeed { get; set; } = 12;
        /// <summary>Retained touch-drag deadzone state, corresponding to Godot's scroll_deadzone property.</summary>
        public int ScrollDeadzone { get => _viewportController.ScrollDeadzone; set => _viewportController.ScrollDeadzone = value; }
        /// <summary>Retained overflow hint mode.</summary>
        public ScrollContainerScrollHintMode ScrollHintMode { get; private set; }
        /// <summary>Retains Godot's texture tiling policy for themed scroll hints.</summary>
        public bool TileScrollHint { get; private set; }
        /// <summary>Retained draw-focus-border policy.</summary>
        public bool DrawFocusBorder { get; set; }
        /// <summary>Retained default wheel-axis policy. Touch/trackpad gesture parity remains platform-dependent.</summary>
        public bool ScrollHorizontalByDefault { get; set; }
        public ScrollBarVisibility HorizontalScrollMode { get => _horizontalScrollMode; set { _horizontalScrollMode = value; UpdateControllerAxes(); QueueLayout(); } }
        public ScrollBarVisibility VerticalScrollMode { get => _verticalScrollMode; set { _verticalScrollMode = value; UpdateControllerAxes(); QueueLayout(); } }
        public HScrollBar HorizontalScrollBar => _horizontalScrollBar;
        public VScrollBar VerticalScrollBar => _verticalScrollBar;
        public Vector2 MaxScrollOffset
        {
            get
            {
                UpdateControllerMetrics();
                return _viewportController.MaxOffset;
            }
        }
        public Vector2 Viewport => _viewportController.Viewport;
        public Vector2 Extent => _viewportController.Extent;
        internal Vector2 ScrollPresenterPosition => IsLayoutRtl() && _verticalScrollBar.Visible ? new Vector2(_verticalScrollBar.Size.X, 0) : Vector2.Zero;
        internal Vector2 ScrollPresenterSize => _viewportSize;
        public ScrollAnchor? ScrollAnchor => _viewportController.Anchor;
        // Matches Godot's set_h_scroll/set_v_scroll, which both call _cancel_drag() after applying the
        // value - unlike the shared internal ScrollOffset mutator, which the wheel/touch-drag machinery
        // itself routes through and must NOT self-cancel on every frame.
        public void ScrollTo(Vector2 offset) { ScrollOffset = offset; CancelTouchDragScroll(); }
        public void SetHScroll(int value) => HorizontalScroll = value;
        public int GetHScroll() => HorizontalScroll;
        public void SetVScroll(int value) => VerticalScroll = value;
        public int GetVScroll() => VerticalScroll;
        public void SetHorizontalCustomStep(float value) => HorizontalCustomStep = value;
        public float GetHorizontalCustomStep() => HorizontalCustomStep;
        public void SetVerticalCustomStep(float value) => VerticalCustomStep = value;
        public float GetVerticalCustomStep() => VerticalCustomStep;
        /// <summary>Shows or hides the horizontal scrollbar's decrement and increment buttons.</summary>
        public void SetShowHorizontalStepButtons(bool visible) => ShowHorizontalStepButtons = visible;
        /// <summary>Gets whether the horizontal scrollbar shows decrement and increment buttons.</summary>
        public bool IsShowingHorizontalStepButtons() => ShowHorizontalStepButtons;
        /// <summary>Shows or hides the vertical scrollbar's decrement and increment buttons.</summary>
        public void SetShowVerticalStepButtons(bool visible) => ShowVerticalStepButtons = visible;
        /// <summary>Gets whether the vertical scrollbar shows decrement and increment buttons.</summary>
        public bool IsShowingVerticalStepButtons() => ShowVerticalStepButtons;
        public void SetHorizontalScrollMode(ScrollBarVisibility mode) { HorizontalScrollMode = mode; }
        public ScrollBarVisibility GetHorizontalScrollMode() => HorizontalScrollMode;
        public void SetVerticalScrollMode(ScrollBarVisibility mode) { VerticalScrollMode = mode; }
        public ScrollBarVisibility GetVerticalScrollMode() => VerticalScrollMode;
        public void SetScrollHorizontalByDefault(bool enable) => ScrollHorizontalByDefault = enable;
        public bool IsScrollHorizontalByDefault() => ScrollHorizontalByDefault;
        public void SetDeadzone(int deadzone) => ScrollDeadzone = deadzone;
        public int GetDeadzone() => ScrollDeadzone;
        public void SetScrollHintMode(ScrollContainerScrollHintMode mode) { if (!Enum.IsDefined(typeof(ScrollContainerScrollHintMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode)); ScrollHintMode = mode; QueueLayout(); }
        public ScrollContainerScrollHintMode GetScrollHintMode() => ScrollHintMode;
        public void SetTileScrollHint(bool enable) { TileScrollHint = enable; QueueLayout(); }
        public bool IsScrollHintTiled() => TileScrollHint;
        public void SetFollowFocus(bool enable) => FollowFocus = enable;
        public bool IsFollowingFocus() => FollowFocus;
        public void SetScrollOnDragHover(bool enable) => ScrollOnDragHover = enable;
        public bool IsScrollOnDragHoverEnabled() => ScrollOnDragHover;
        public HScrollBar GetHScrollBar() => _horizontalScrollBar;
        public VScrollBar GetVScrollBar() => _verticalScrollBar;
        public void SetDrawFocusBorder(bool draw) => DrawFocusBorder = draw;
        public bool GetDrawFocusBorder() => DrawFocusBorder;
        /// <summary>Raised when a touch drag first crosses <see cref="ScrollDeadzone"/>, matching Godot's scroll_started signal.</summary>
        public event EventHandler ScrollStarted;
        /// <summary>Raised when a touch drag that crossed the deadzone ends, matching Godot's scroll_ended signal.</summary>
        public event EventHandler ScrollEnded;
        public event EventHandler ScrollOffsetChanged;
        public event EventHandler ViewportChanged;
        public event EventHandler ScrollExtentChanged;
        public event EventHandler<ScrollViewportMetricsChangedEventArgs> ScrollMetricsChanged;
        public bool IsTouchDragging => _viewportController.IsTouchDragging;
        public bool IsTouchDragDecelerating => _viewportController.IsTouchDragDecelerating;
        public bool IsBeyondScrollDeadzone => _viewportController.IsBeyondScrollDeadzone;
        public Vector2 TouchDragSpeed => _viewportController.TouchDragSpeed;
        /// <summary>Begins a retained touch drag, mirroring Godot's touchscreen mouse-press handling in ScrollContainer::gui_input.</summary>
        public void BeginTouchDragScroll()
        {
            UpdateControllerMetrics();
            _viewportController.BeginTouchDrag();
        }
        /// <summary>Applies relative touch motion, gating on <see cref="ScrollDeadzone"/> exactly like Godot's gui_input drag accumulation.</summary>
        public void TouchDragScrollBy(Vector2 relativeMotion)
        {
            UpdateControllerMetrics();
            _viewportController.TouchDragBy(relativeMotion);
            SynchronizeControllerOffset();
        }
        /// <summary>Ends a retained touch drag, entering inertial deceleration when speed is nonzero like Godot's release handling.</summary>
        public void EndTouchDragScroll()
        {
            _viewportController.EndTouchDrag();
        }
        /// <summary>Cancels an in-progress touch drag immediately, mirroring Godot's ScrollContainer::_cancel_drag.</summary>
        public void CancelTouchDragScroll()
        {
            _viewportController.CancelTouchDrag();
        }
        internal override void CancelInput()
        {
            CancelTouchDragScroll();
            base.CancelInput();
        }
        protected override void OnContextChanged(UIContext previous, UIContext current)
        {
            if (previous != null)
            {
                previous.RetainedPointerPressed -= OnTouchPointerPressed;
                previous.RetainedPointerMoved -= OnTouchPointerMoved;
                previous.RetainedPointerReleased -= OnTouchPointerReleased;
            }
            if (current != null)
            {
                current.RetainedPointerPressed += OnTouchPointerPressed;
                current.RetainedPointerMoved += OnTouchPointerMoved;
                current.RetainedPointerReleased += OnTouchPointerReleased;
            }
            base.OnContextChanged(previous, current);
        }
        private void OnTouchPointerPressed(Control target, Point point)
        {
            if (Context?.TouchscreenAvailable == true && IsTouchScrollTarget(target)) BeginTouchDragScroll();
        }
        private void OnTouchPointerMoved(Control target, Point point, Vector2 relativeMotion)
        {
            if (IsTouchScrollTarget(target)) TouchDragScrollBy(relativeMotion);
        }
        private void OnTouchPointerReleased(Control target, Point point)
        {
            if (IsTouchScrollTarget(target)) EndTouchDragScroll();
        }
        private bool IsTouchScrollTarget(Control target)
        {
            for (var control = target; control != null; control = control.VisualParent)
            {
                if (control == _horizontalScrollBar || control == _verticalScrollBar) return false;
                if (control == this) return true;
            }
            return false;
        }
        /// <summary>Scrolls as little as necessary to reveal a descendant control inside the viewport.</summary>
        public void EnsureControlVisible(Control control)
        {
            if (control == null) throw new ArgumentNullException(nameof(control));
            if (control != this && !ContainsDescendant(control)) throw new ArgumentException("The control must be a descendant of this ScrollContainer.", nameof(control));
            _focusScrollDiff = Vector2.Zero;
            if (control == this) return;
            var viewport = GetVisibleViewportRectangle();
            var target = control.Bounds;
            for (var ancestor = control.VisualParent; ancestor != null && ancestor != this; ancestor = ancestor.VisualParent)
            {
                if (!(ancestor is ScrollContainer inner)) continue;
                target.Offset(-(int)MathF.Round(inner._focusScrollDiff.X), -(int)MathF.Round(inner._focusScrollDiff.Y));
                target = Rectangle.Intersect(target, inner.GetVisibleViewportRectangle());
                if (target.Width <= 0 || target.Height <= 0) return;
            }
            var before = ScrollOffset;
            _viewportController.BringIntoView(viewport, target);
            SynchronizeControllerOffset();
            _focusScrollDiff = ScrollOffset - before;
        }

        public bool BringIndexIntoView(int index)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            if (_content is not IScrollIndexProvider provider || !provider.TryGetIndexBounds(index, out var bounds)) return false;
            var viewport = new Rectangle((int)MathF.Round(ScrollOffset.X), (int)MathF.Round(ScrollOffset.Y), (int)_viewportSize.X, (int)_viewportSize.Y);
            _viewportController.BringIntoView(viewport, bounds);
            SynchronizeControllerOffset();
            return true;
        }

        public ScrollAnchor CaptureScrollAnchor(object token, Vector2 contentPosition) =>
            _viewportController.CaptureAnchor(token, contentPosition);

        public bool RestoreScrollAnchor(object token, Vector2 contentPosition)
        {
            var restored = _viewportController.RestoreAnchor(token, contentPosition);
            if (restored) SynchronizeControllerOffset();
            return restored;
        }

        public void ClearScrollAnchor() => _viewportController.ClearAnchor();

        void IScrollViewportOwner.OnScrollMetricsChanged(ScrollPresenter presenter, ScrollMetrics metrics)
        {
            if (!ReferenceEquals(presenter, _scrollPresenter)) return;
            _viewportSize = metrics.Viewport;
            _viewportController.UpdateMetrics(metrics.Viewport, metrics.Extent);
            SynchronizeControllerOffset();
        }

        void IScrollViewportOwner.BringIntoView(ScrollPresenter presenter, Control target, Rectangle targetBounds)
        {
            if (ReferenceEquals(presenter, _scrollPresenter) && target != null) EnsureControlVisible(target);
        }
        private Rectangle GetVisibleViewportRectangle()
        {
            var x = Bounds.X + (IsLayoutRtl() && _verticalScrollBar.Visible ? _verticalScrollBar.Bounds.Width : 0);
            return new Rectangle(x, Bounds.Y, (int)_viewportSize.X, (int)_viewportSize.Y);
        }
        public override Vector2 GetMinimumSize()
        {
            var content = ContentSize;
            var minimum = CustomMinimumSize;
            // Godot's _get_minimum_size also budgets for the OTHER axis's scrollbar when that axis is
            // actually showing/reserved, so a size-aware parent doesn't under-allocate space for it.
            var verticalShows = ShouldShow(VerticalScrollMode, content.Y, Size.Y);
            var horizontalShows = ShouldShow(HorizontalScrollMode, content.X, Size.X);
            if (HorizontalScrollMode == ScrollBarVisibility.Disabled || HorizontalScrollMode == ScrollBarVisibility.MaximizeFirst)
            {
                minimum.X = Math.Max(minimum.X, content.X);
                if (verticalShows) minimum.X += _verticalScrollBar.GetMinimumSize().X;
            }
            if (VerticalScrollMode == ScrollBarVisibility.Disabled || VerticalScrollMode == ScrollBarVisibility.MaximizeFirst)
            {
                minimum.Y = Math.Max(minimum.Y, content.Y);
                if (horizontalShows) minimum.Y += _horizontalScrollBar.GetMinimumSize().Y;
            }
            return minimum;
        }
        internal IReadOnlyList<Rectangle> GetVisibleScrollHintRectangles()
        {
            var result = new List<Rectangle>();
            if (ScrollHintMode == ScrollContainerScrollHintMode.Disabled) return result;
            var max = MaxScrollOffset;
            var showTop = ScrollOffset.Y > 1;
            var showBottom = ScrollOffset.Y < max.Y - 1;
            var showLeft = ScrollOffset.X > 1;
            var showRight = ScrollOffset.X < max.X - 1;
            var showVerticalHints = showTop || showBottom;
            var showHorizontalHints = showLeft || showRight;
            const int hintThickness = 4;
            if (showVerticalHints)
            {
                if (showHorizontalHints) return result;
                if (showTop && (ScrollHintMode == ScrollContainerScrollHintMode.All || ScrollHintMode == ScrollContainerScrollHintMode.TopAndLeft))
                    result.Add(new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, Math.Min(hintThickness, Bounds.Height)));
                if (showBottom && (ScrollHintMode == ScrollContainerScrollHintMode.All || ScrollHintMode == ScrollContainerScrollHintMode.BottomAndRight))
                    result.Add(new Rectangle(Bounds.X, Math.Max(Bounds.Y, Bounds.Bottom - hintThickness), Bounds.Width, Math.Min(hintThickness, Bounds.Height)));
                return result;
            }
            if (!showHorizontalHints) return result;
            var startMode = IsLayoutRtl() ? ScrollContainerScrollHintMode.BottomAndRight : ScrollContainerScrollHintMode.TopAndLeft;
            var endMode = IsLayoutRtl() ? ScrollContainerScrollHintMode.TopAndLeft : ScrollContainerScrollHintMode.BottomAndRight;
            if (showLeft && (ScrollHintMode == ScrollContainerScrollHintMode.All || ScrollHintMode == startMode))
            {
                var x = IsLayoutRtl() ? Math.Max(Bounds.X, Bounds.Right - hintThickness) : Bounds.X;
                result.Add(new Rectangle(x, Bounds.Y, Math.Min(hintThickness, Bounds.Width), Bounds.Height));
            }
            if (showRight && (ScrollHintMode == ScrollContainerScrollHintMode.All || ScrollHintMode == endMode))
            {
                var x = IsLayoutRtl() ? Bounds.X : Math.Max(Bounds.X, Bounds.Right - hintThickness);
                result.Add(new Rectangle(x, Bounds.Y, Math.Min(hintThickness, Bounds.Width), Bounds.Height));
            }
            return result;
        }
        internal bool IsFocusBorderVisible => DrawFocusBorder && (Context?.FocusedControl == this || ContainsDescendant(Context?.FocusedControl));
        /// <summary>Matches Godot's ScrollContainer::gui_input WHEEL_UP/WHEEL_DOWN handling: the scroll
        /// amount is always the relevant scrollbar's page / ScrollBar::PAGE_DIVISOR (8), Shift held swaps
        /// to the horizontal axis (scroll_horizontal_by_default flips which axis is the "default" one),
        /// and an auto-hidden vertical scrollbar falls back to scrolling horizontally either way. This
        /// port has no separate horizontal-wheel input channel (the XNA-compatible MouseState only reports a
        /// single vertical wheel value), so Godot's WHEEL_LEFT/WHEEL_RIGHT hardware path is out of scope.</summary>
        internal override bool PointerWheel(int delta)
        {
            if (delta == 0) return false;
            var swapAxes = ScrollHorizontalByDefault != HasShiftModifier();
            var horizontalEnabled = HorizontalScrollMode != ScrollBarVisibility.Disabled;
            var verticalEnabled = VerticalScrollMode != ScrollBarVisibility.Disabled;
            var verticalHidden = !_verticalScrollBar.Visible && VerticalScrollMode != ScrollBarVisibility.Never;
            UpdateControllerMetrics();
            if ((horizontalEnabled && swapAxes) || verticalHidden)
            {
                if (horizontalEnabled) _viewportController.ScrollWheel(delta, true, _horizontalScrollBar.Page, _verticalScrollBar.Page);
            }
            else if (verticalEnabled)
            {
                _viewportController.ScrollWheel(delta, false, _horizontalScrollBar.Page, _verticalScrollBar.Page);
            }
            return SynchronizeControllerOffset();
        }
        private bool HasShiftModifier()
        {
            var keyboard = Context?.CurrentKeyboardState ?? default;
            return keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        }
        internal override void Process(GameTime gameTime)
        {
            if (FollowFocus)
            {
                var focused = Context?.FocusedControl;
                if (focused != _followedFocus)
                {
                    _followedFocus = focused;
                    if (focused != null && focused != this && ContainsDescendant(focused) && !IsScrollBar(focused))
                    {
                        EnsureNestedFocusFollowers(focused);
                        EnsureControlVisible(focused);
                    }
                }
            }
            ProcessDragHoverScroll(gameTime);
            ProcessTouchDrag(gameTime);
            base.Process(gameTime);
        }
        private void EnsureNestedFocusFollowers(Control focused)
        {
            for (var ancestor = focused.VisualParent; ancestor != null && ancestor != this; ancestor = ancestor.VisualParent)
            {
                if (!(ancestor is ScrollContainer nested) || !nested.FollowFocus) continue;
                nested._followedFocus = focused;
                nested.EnsureControlVisible(focused);
            }
        }
        private void ProcessDragHoverScroll(GameTime gameTime)
        {
            if (!ScrollOnDragHover || Context?.IsDragging != true || IsTabDrag(Context.DragData)) return;
            var delta = (float)gameTime.ElapsedGameTime.TotalSeconds;
            var border = Math.Max(0, DragHoverScrollBorder);
            if (delta <= 0 || border == 0) return;
            var pointer = new Vector2(Context.PointerPosition.X, Context.PointerPosition.Y) - GlobalPosition;
            if (pointer.X < -border || pointer.X > Size.X + border || pointer.Y < -border || pointer.Y > Size.Y + border) return;
            var offset = Vector2.Zero;
            if (Math.Abs(pointer.X) < Math.Abs(pointer.X - Size.X) && Math.Abs(pointer.X) < border) offset.X = pointer.X - border;
            else if (Math.Abs(pointer.X - Size.X) < border) offset.X = pointer.X - (Size.X - border);
            if (Math.Abs(pointer.Y) < Math.Abs(pointer.Y - Size.Y) && Math.Abs(pointer.Y) < border) offset.Y = pointer.Y - border;
            else if (Math.Abs(pointer.Y - Size.Y) < border) offset.Y = pointer.Y - (Size.Y - border);
            ScrollOffset += offset * DragHoverScrollSpeed * delta;
        }
        private static bool IsTabDrag(object data)
        {
            if (data is IDictionary<string, object> dictionary && dictionary.TryGetValue("type", out var type)) return string.Equals(type as string, "tab", StringComparison.Ordinal);
            return false;
        }
        private void ProcessTouchDrag(GameTime gameTime)
        {
            var delta = (float)gameTime.ElapsedGameTime.TotalSeconds;
            UpdateControllerMetrics();
            _viewportController.Process(delta);
            SynchronizeControllerOffset();
        }
        protected override void ArrangeChildren()
        {
            base.ArrangeChildren();
            var content = ContentSize;
            var barWidth = _verticalScrollBar.GetMinimumSize().X;
            var barHeight = _horizontalScrollBar.GetMinimumSize().Y;
            var reserveHorizontal = HorizontalScrollMode == ScrollBarVisibility.Reserve;
            var reserveVertical = VerticalScrollMode == ScrollBarVisibility.Reserve;
            var showHorizontal = false; var showVertical = false;
            for (var i = 0; i < 2; i++)
            {
                var availableWidth = Size.X - (reserveVertical ? barWidth : 0);
                var availableHeight = Size.Y - (reserveHorizontal ? barHeight : 0);
                showHorizontal = ShouldShow(HorizontalScrollMode, content.X, availableWidth);
                showVertical = ShouldShow(VerticalScrollMode, content.Y, availableHeight);
                reserveHorizontal = HorizontalScrollMode == ScrollBarVisibility.Reserve || showHorizontal;
                reserveVertical = VerticalScrollMode == ScrollBarVisibility.Reserve || showVertical;
            }
            _horizontalScrollBar.Visible = showHorizontal;
            _verticalScrollBar.Visible = showVertical;
            _viewportSize = Vector2.Max(Vector2.Zero, Size - new Vector2(reserveVertical ? barWidth : 0, reserveHorizontal ? barHeight : 0));
            UpdateControllerMetrics();
            var contentOrigin = IsLayoutRtl() && reserveVertical ? new Vector2(barWidth, 0) : Vector2.Zero;
            if (TemplateRoot != null)
            {
                TemplateRoot.Position = TemplateRoot == _scrollPresenter ? contentOrigin : Vector2.Zero;
                TemplateRoot.Size = TemplateRoot == _scrollPresenter ? _viewportSize : Size;
            }
            var max = MaxScrollOffset;
            _horizontalScrollBar.Position = new Vector2(0, _viewportSize.Y);
            _horizontalScrollBar.Size = new Vector2(_viewportSize.X, barHeight);
            // Range's maximum includes its visible page; retain MaxScrollOffset as the public
            // content-relative maximum while configuring the scrollbar with the full content span.
            _horizontalScrollBar.MinValue = 0; _horizontalScrollBar.MaxValue = Math.Max(0, max.X + _viewportSize.X); _horizontalScrollBar.Page = _viewportSize.X; _horizontalScrollBar.Value = ScrollOffset.X;
            _verticalScrollBar.Position = new Vector2(IsLayoutRtl() ? 0 : _viewportSize.X, 0);
            _verticalScrollBar.Size = new Vector2(barWidth, _viewportSize.Y);
            _verticalScrollBar.MinValue = 0; _verticalScrollBar.MaxValue = Math.Max(0, max.Y + _viewportSize.Y); _verticalScrollBar.Page = _viewportSize.Y; _verticalScrollBar.Value = ScrollOffset.Y;
            _focusScrollDiff = Vector2.Zero;
            TemplateRoot?.QueueLayout();
        }
        private Vector2 ContentSize
        {
            get
            {
                return _content == null ? Vector2.Zero : Vector2.Max(_content.GetMinimumSize(), _content.Size);
            }
        }
        private bool IsScrollBar(Control control) => control == _horizontalScrollBar || control == _verticalScrollBar;

        protected override void OnTemplateApplied()
        {
            var presenter = GetTemplateChild(ScrollPresenterPartName) as ScrollPresenter;
            if (presenter == null)
                throw new InvalidOperationException($"ScrollContainer templates must provide a {nameof(ScrollPresenter)} named '{ScrollPresenterPartName}'.");
            if (_scrollPresenter != null && !ReferenceEquals(_scrollPresenter, presenter))
            {
                _scrollPresenter.Content = null;
                _scrollPresenter.Owner = null;
            }
            _scrollPresenter = presenter;
            presenter.Owner = this;
            presenter.Content = _content;
            base.OnTemplateApplied();
        }
        private bool ContainsDescendant(Control control)
        {
            for (var current = control.VisualParent; current != null; current = current.VisualParent) if (current == this) return true;
            return false;
        }
        private void UpdateControllerAxes()
        {
            _viewportController.HorizontalEnabled = HorizontalScrollMode != ScrollBarVisibility.Disabled;
            _viewportController.VerticalEnabled = VerticalScrollMode != ScrollBarVisibility.Disabled;
        }
        private void UpdateControllerMetrics()
        {
            UpdateControllerAxes();
            _viewportController.UpdateMetrics(_viewportSize, ContentSize);
        }
        private bool SynchronizeControllerOffset()
        {
            var before = new Vector2(_horizontalScrollBar.Value, _verticalScrollBar.Value);
            _horizontalScrollBar.SetValueNoSignal(ScrollOffset.X);
            _verticalScrollBar.SetValueNoSignal(ScrollOffset.Y);
            _scrollPresenter?.QueueLayout();
            QueueLayout();
            return before != ScrollOffset;
        }

        private void OnViewportMetricsChanged(object sender, ScrollViewportMetricsChangedEventArgs args)
        {
            if (args.OffsetChanged)
            {
                OnPropertyChanged(nameof(ScrollOffset));
                OnPropertyChanged(nameof(HorizontalScroll));
                OnPropertyChanged(nameof(VerticalScroll));
                ScrollOffsetChanged?.Invoke(this, EventArgs.Empty);
            }
            if (args.ViewportChanged)
            {
                OnPropertyChanged(nameof(Viewport));
                ViewportChanged?.Invoke(this, EventArgs.Empty);
            }
            if (args.ExtentChanged)
            {
                OnPropertyChanged(nameof(Extent));
                ScrollExtentChanged?.Invoke(this, EventArgs.Empty);
            }
            ScrollMetricsChanged?.Invoke(this, args);
        }
        private static bool ShouldShow(ScrollBarVisibility mode, float content, float available)
        {
            if (mode == ScrollBarVisibility.Disabled || mode == ScrollBarVisibility.Never) return false;
            if (mode == ScrollBarVisibility.Always || mode == ScrollBarVisibility.Reserve) return true;
            return content > available;
        }
    }

}
