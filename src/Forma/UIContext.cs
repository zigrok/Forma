// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// Tooltip ancestor traversal is adapted from Godot Engine's scene/main/viewport.cpp;
// see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Globalization;
using Forma.Xaml;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    public enum ThemeVariant { Default, Light, Dark, HighContrast }
    public enum InputModality { Pointer, Touch, Keyboard, Gamepad }

    /// <summary>
    /// Owns a UI tree's input state, focus and rendering. Add it to a Game's update/draw loop, or use
    /// <see cref="UIComponent"/> for the usual game component integration.
    /// </summary>
    public sealed class UIContext : IDisposable
    {
        private readonly List<Control> _roots = new List<Control>();
        private readonly List<Control> _rootsInDrawOrder = new List<Control>();
        private readonly HashSet<XamlAttachmentScope> _xamlScopes = new HashSet<XamlAttachmentScope>();
        private readonly HashSet<Action<GameTime>> _frameBoundaryCallbacks = new HashSet<Action<GameTime>>();
        private readonly List<SvgPrewarmRequest> _svgPrewarmRequests = new List<SvgPrewarmRequest>();
        private readonly UIFontSelection _tooltipFontSelection = new UIFontSelection();
        private MouseState _previousMouse;
        private KeyboardState _previousKeyboard;
        private Control _hovered;
        private Control _captured;
        private Control _dragSource;
        private object _dragData;
        private Point _dragStartPosition;
        private Control _tooltipOwner;
        private string _tooltipText = string.Empty;
        private TimeSpan _tooltipElapsed;
        private Point _tooltipPointerPosition;
        private UIRenderContext _renderer;
        private DefaultThemeIconResources _iconResources;
        private long _nextRootOrder;
        private bool _rootOrderDirty = true;
        private CultureInfo _applicationCulture = CultureInfo.CurrentUICulture;
        private CultureInfo _systemCulture = CultureInfo.CurrentCulture;
        private LayoutDirection _rootLayoutDirection = LayoutDirection.ApplicationLocale;
        private float _displayScale = 1f;
        private Theme _theme;
        private Vector2 _viewportSize;
        private ThemeVariant _themeVariant;
        private ThemeIconRenderingPolicy _themeIconRenderingPolicy;
        private InputModality _inputModality;
        private long _themeGeneration;
        /// <summary>Most recently dispatched pointer position, available to retained controls that coordinate transient surfaces.</summary>
        public Point PointerPosition { get; private set; }
        /// <summary>Keyboard state for the input frame currently being dispatched, including modifier keys used by controls such as Tree.</summary>
        public KeyboardState CurrentKeyboardState { get; private set; }
        /// <summary>Game time for the input frame currently being dispatched, used by retained multi-click gestures.</summary>
        public TimeSpan CurrentTime { get; private set; }

        public UIContext() : this(RuntimeClipboard.Instance)
        {
        }

        /// <summary>Creates a context with a host-owned clipboard without probing native platform libraries.</summary>
        public UIContext(IClipboard clipboard)
        {
            Clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
            Theme = new Theme { FontFamily = UIFontDefaultRegistry.FontFamily };
        }
        public IReadOnlyList<Control> Roots => _roots;
        public ResourceDictionary Resources { get; } = new ResourceDictionary();
        /// <summary>Clipboard capability used by copy, cut, and paste commands in retained text controls.</summary>
        public IClipboard Clipboard { get; set; }
        public Theme Theme
        {
            get => _theme;
            set
            {
                value ??= new Theme();
                if (ReferenceEquals(_theme, value)) return;
                if (_theme != null) _theme.Changed -= ThemeChanged;
                _theme = value;
                _theme.Changed += ThemeChanged;
                ThemeChanged(_theme, EventArgs.Empty);
            }
        }
        internal long ThemeGeneration => _themeGeneration;
        public TextLayoutEngine TextLayoutEngine { get; } = new TextLayoutEngine();
        public Control FocusedControl { get; private set; }
        /// <summary>Whether retained touch-style interactions should be enabled for pointer input.</summary>
        public bool TouchscreenAvailable { get; set; }
        /// <summary>Whether a retained drag-and-drop operation is currently active.</summary>
        public bool IsDragging => _dragSource != null;
        /// <summary>Payload supplied by the active retained drag source, or null when not dragging.</summary>
        public object DragData => _dragData;
        public Vector2 ViewportSize
        {
            get => _viewportSize;
            set
            {
                if (_viewportSize == value) return;
                _viewportSize = value;
                AdaptiveEnvironmentChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public ThemeVariant ThemeVariant
        {
            get => _themeVariant;
            set
            {
                if (!Enum.IsDefined(typeof(ThemeVariant), value)) throw new ArgumentOutOfRangeException(nameof(value));
                if (_themeVariant == value) return;
                _themeVariant = value;
                AdaptiveEnvironmentChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public ThemeIconRenderingPolicy ThemeIconRenderingPolicy
        {
            get => _themeIconRenderingPolicy;
            set
            {
                if (!Enum.IsDefined(typeof(ThemeIconRenderingPolicy), value)) throw new ArgumentOutOfRangeException(nameof(value));
                if (_themeIconRenderingPolicy == value) return;
                _themeIconRenderingPolicy = value;
                foreach (var root in _roots) root.MarkThemeDirty();
            }
        }
        public InputModality InputModality
        {
            get => _inputModality;
            set
            {
                if (!Enum.IsDefined(typeof(InputModality), value)) throw new ArgumentOutOfRangeException(nameof(value));
                if (_inputModality == value) return;
                _inputModality = value;
                AdaptiveEnvironmentChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler AdaptiveEnvironmentChanged;
        internal event EventHandler ThemeGenerationChanged;
        /// <summary>Physical display pixels per logical UI coordinate. Input is mapped back to logical coordinates and drawing is scaled to physical pixels.</summary>
        public float DisplayScale
        {
            get => _displayScale;
            set
            {
                if (!float.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
                if (_displayScale == value) return;
                _displayScale = value;
                ClearDynamicGlyphCache();
                foreach (var root in _roots) root.MarkDisplayScaleLayoutDirty();
                AdaptiveEnvironmentChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        /// <summary>Optionally resolves a denser font atlas for the supplied logical font and display scale.</summary>
        public Func<SpriteFont, float, SpriteFont> DisplayFontResolver { get; set; }
        /// <summary>Read-only diagnostics for this context's device-scoped dynamic glyph cache.</summary>
        public DynamicGlyphCacheDiagnostics DynamicGlyphDiagnostics => _renderer?.DynamicGlyphDiagnostics ?? default;
        /// <summary>Returns immutable grayscale snapshots of currently allocated glyph-atlas pages.</summary>
        public IReadOnlyList<DynamicGlyphAtlasPageSnapshot> GetDynamicGlyphAtlasPages() => _renderer?.DynamicGlyphPages ?? Array.Empty<DynamicGlyphAtlasPageSnapshot>();
        /// <summary>Clears device-scoped dynamic glyph pages between draw calls. Cumulative counters remain available.</summary>
        public void ClearDynamicGlyphCache() => _renderer?.ClearDynamicGlyphCache();
        /// <summary>Read-only diagnostics for this context's device-scoped SVG raster cache.</summary>
        public SvgRasterCacheDiagnostics SvgRasterDiagnostics => _renderer?.SvgRasterDiagnostics ?? default;
        /// <summary>Returns immutable RGBA snapshots of currently allocated SVG-atlas pages.</summary>
        public IReadOnlyList<SvgRasterAtlasPageSnapshot> GetSvgRasterAtlasPages() => _renderer?.SvgRasterPages ?? Array.Empty<SvgRasterAtlasPageSnapshot>();
        /// <summary>Clears device-scoped SVG documents, rasters, and pages between draw calls.</summary>
        public void ClearSvgRasterCache() => _renderer?.ClearSvgRasterCache();
        /// <summary>Queues an SVG raster variant for creation before the next UI draw.</summary>
        public void PrewarmSvg(SvgImageSource source, Vector2 logicalSize)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!float.IsFinite(logicalSize.X) || !float.IsFinite(logicalSize.Y) || logicalSize.X <= 0 || logicalSize.Y <= 0) throw new ArgumentOutOfRangeException(nameof(logicalSize));
            _svgPrewarmRequests.Add(new SvgPrewarmRequest(source, logicalSize));
        }
        /// <summary>Application locale used by controls with <see cref="LayoutDirection.ApplicationLocale"/>.</summary>
        public CultureInfo ApplicationCulture
        {
            get => _applicationCulture;
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(value));
                if (Equals(_applicationCulture, value)) return;
                var previous = CaptureLayoutDirections();
                _applicationCulture = value;
                MarkLayoutDirectionsDirty(previous);
            }
        }
        /// <summary>System locale used by controls with <see cref="LayoutDirection.SystemLocale"/>.</summary>
        public CultureInfo SystemCulture
        {
            get => _systemCulture;
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(value));
                if (Equals(_systemCulture, value)) return;
                var previous = CaptureLayoutDirections();
                _systemCulture = value;
                MarkLayoutDirectionsDirty(previous);
            }
        }
        /// <summary>Fallback layout direction for root controls whose direction is inherited.</summary>
        public LayoutDirection RootLayoutDirection
        {
            get => _rootLayoutDirection;
            set
            {
                if (_rootLayoutDirection == value) return;
                var previous = CaptureLayoutDirections();
                _rootLayoutDirection = value;
                MarkLayoutDirectionsDirty(previous);
            }
        }
        /// <summary>Delay before hover help becomes visible. Defaults to Godot-like delayed presentation.</summary>
        public TimeSpan TooltipDelay { get; set; } = TimeSpan.FromMilliseconds(700);
        /// <summary>Font used when drawing tooltip text. Assign the same font used by the application UI.</summary>
        public SpriteFont TooltipFont { get => _tooltipFontSelection.SpriteFont; set => _tooltipFontSelection.SetSpriteFont(value); }
        public UIFont TooltipUIFont { get => _tooltipFontSelection.UIFont; set => _tooltipFontSelection.SetUIFont(value); }
        internal UIFont EffectiveTooltipUIFont => _tooltipFontSelection.Resolve(Theme);
        /// <summary>Current default icon-atlas resource and lookup counters.</summary>
        public ThemeIconDiagnostics ThemeIconDiagnostics => _iconResources?.Diagnostics ?? default;
        public Thickness TooltipPadding { get; set; } = new Thickness(7, 4, 7, 4);
        public Vector2 TooltipOffset { get; set; } = new Vector2(14, 18);
        public bool IsTooltipVisible { get; private set; }
        public Control TooltipOwner => _tooltipOwner;
        public string TooltipText => _tooltipText;
        internal event Action<Control, Point> RetainedPointerPressed;
        internal event Action<Control, Point, Vector2> RetainedPointerMoved;
        internal event Action<Control, Point> RetainedPointerReleased;
        private void ThemeChanged(object sender, EventArgs args)
        {
            _themeGeneration++;
            TextLayoutEngine.Clear();
            foreach (var root in _roots) root.MarkThemeDirty();
            ThemeGenerationChanged?.Invoke(this, EventArgs.Empty);
        }

        internal void ResetInteractionState(Control root)
        {
            if (root == null) return;
            if (IsInVisualSubtree(root, FocusedControl))
            {
                foreach (var key in CurrentKeyboardState.GetPressedKeys()) FocusedControl.KeyReleased(key);
                SetFocus(null);
            }
            if (IsInVisualSubtree(root, _captured))
            {
                var captured = _captured;
                _captured = null;
                for (var control = captured; control != null; control = control.VisualParent)
                {
                    control.PointerReleased(PointerPosition, false);
                    if (control.ConsumeEventAccepted() || control.MouseFilter != MouseFilter.Pass) break;
                }
            }
            if (IsInVisualSubtree(root, _dragSource))
            {
                _dragSource.NotifyDragEnded(false);
                _dragSource = null;
                _dragData = null;
            }
            if (IsInVisualSubtree(root, _hovered))
            {
                UpdateHoveredTarget(null);
            }
            if (IsInVisualSubtree(root, _tooltipOwner))
            {
                _tooltipOwner = null;
                _tooltipText = string.Empty;
                _tooltipElapsed = TimeSpan.Zero;
                IsTooltipVisible = false;
            }
        }

        internal bool HasPinnedInteraction(Control root) =>
            IsInVisualSubtree(root, _captured) || IsInVisualSubtree(root, _dragSource);

        private static bool IsInVisualSubtree(Control root, Control control)
        {
            for (var current = control; current != null; current = current.VisualParent)
                if (ReferenceEquals(current, root)) return true;
            return false;
        }

        public void Add(Control control)
        {
            if (control == null) throw new ArgumentNullException(nameof(control));
            control.RemoveFromParent();
            if (_roots.Contains(control)) return;
            _roots.Add(control);
            control.SetTreeOrder(++_nextRootOrder);
            _rootOrderDirty = true;
            control.SetContext(this);
        }

        public bool Remove(Control control)
        {
            if (control == null || !_roots.Remove(control)) return false;
            if (FocusedControl == control) SetFocus(null);
            _rootOrderDirty = true;
            control.SetContext(null);
            return true;
        }

        public void Update(GameTime gameTime) => Update(gameTime, Mouse.GetState(), Keyboard.GetState());

        /// <summary>Moves the pointer using physical back-buffer pixels without changing the polled mouse state.</summary>
        public void InjectPointerMove(Point physicalPosition)
        {
            Layout();
            var previous = PointerPosition;
            var point = ToLogicalPointerPosition(physicalPosition);
            var target = UpdateInjectedPointerTarget(point);
            if (point == previous) return;
            var motionTarget = _captured ?? target;
            if (motionTarget != null)
            {
                DispatchPointerMoved(motionTarget, point);
                RetainedPointerMoved?.Invoke(motionTarget, point, new Vector2(point.X - previous.X, point.Y - previous.Y));
            }
            if (_captured != null) UpdateDrag(point);
        }

        /// <summary>Presses a pointer button at a position expressed in physical back-buffer pixels.</summary>
        public void InjectPointerPress(Point physicalPosition, PointerButton button = PointerButton.Left)
        {
            Layout();
            var point = ToLogicalPointerPosition(physicalPosition);
            var modalPopup = GetActiveModalPopup();
            var target = UpdateInjectedPointerTarget(point, modalPopup);
            if (button == PointerButton.Left)
            {
                if (modalPopup != null && target == null)
                {
                    modalPopup.OutsidePointerPressed(point);
                    return;
                }
                if (target != null)
                {
                    _captured = target;
                    _dragStartPosition = point;
                    DispatchPointerPressed(target, point);
                    RetainedPointerPressed?.Invoke(target, point);
                }
            }
            DispatchInjectedPointerButton(target, point, button, pressed: true);
        }

        /// <summary>Releases a pointer button at a position expressed in physical back-buffer pixels.</summary>
        public void InjectPointerRelease(Point physicalPosition, PointerButton button = PointerButton.Left)
        {
            Layout();
            var point = ToLogicalPointerPosition(physicalPosition);
            var target = UpdateInjectedPointerTarget(point);
            DispatchInjectedPointerButton(target, point, button, pressed: false);
            if (button != PointerButton.Left || _captured == null) return;
            var capture = _captured;
            _captured = null;
            var dropped = false;
            if (_dragSource != null)
            {
                var dropTarget = GetDropTarget(point);
                if (dropTarget != null)
                {
                    dropTarget.DropData(point, _dragData);
                    dropped = true;
                }
                _dragSource.NotifyDragEnded(dropped);
                _dragSource = null;
                _dragData = null;
            }
            DispatchPointerReleased(capture, point);
            RetainedPointerReleased?.Invoke(capture, point);
        }

        /// <summary>Routes a wheel delta at a position expressed in physical back-buffer pixels.</summary>
        public bool InjectPointerWheel(Point physicalPosition, int delta, Control fallbackTarget = null)
        {
            if (delta == 0) return false;
            Layout();
            var point = ToLogicalPointerPosition(physicalPosition);
            var target = UpdateInjectedPointerTarget(point);
            if (DispatchPointerWheel(target, delta)) return true;
            return fallbackTarget != null && fallbackTarget.Context == this && fallbackTarget.IsRendered
                && DispatchPointerWheel(fallbackTarget, delta);
        }

        /// <summary>Updates the tree from supplied states; this overload makes UI input deterministic in tests.</summary>
        public void Update(GameTime gameTime, MouseState mouse, KeyboardState keyboard)
        {
            UpdateFrameBoundaryCallbacks(gameTime);
            UpdateXamlScopes(gameTime);
            if (Math.Abs(DisplayScale - 1f) > .0001f)
            {
                mouse = new MouseState(
                    (int)MathF.Round(mouse.X / DisplayScale),
                    (int)MathF.Round(mouse.Y / DisplayScale),
                    mouse.ScrollWheelValue,
                    mouse.LeftButton,
                    mouse.MiddleButton,
                    mouse.RightButton,
                    mouse.XButton1,
                    mouse.XButton2);
            }
            Layout();
            CurrentKeyboardState = keyboard;
            CurrentTime = gameTime?.TotalGameTime ?? TimeSpan.Zero;
            var point = mouse.GetPosition();
            PointerPosition = point;
            var modalPopup = GetActiveModalPopup();
            var target = modalPopup == null ? HitTest(point) : HitTest(modalPopup, point);
            UpdateHoveredTarget(target);
            UpdateTooltip(target, point, gameTime.ElapsedGameTime);

            var pressed = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
            var released = mouse.LeftButton == ButtonState.Released && _previousMouse.LeftButton == ButtonState.Pressed;
            if (pressed && modalPopup != null && target == null)
            {
                // Dismissal deliberately consumes the press, preventing click-through to an underlying control.
                modalPopup.OutsidePointerPressed(point);
            }
            else if (pressed && target != null)
            {
                _captured = target;
                _dragStartPosition = point;
                DispatchPointerPressed(target, point);
                RetainedPointerPressed?.Invoke(target, point);
            }
            DispatchPointerButtonTransitions(target, point, mouse, _previousMouse);
            if (mouse.GetPosition() != _previousMouse.GetPosition())
            {
                var motionTarget = _captured ?? target;
                if (motionTarget != null)
                {
                    DispatchPointerMoved(motionTarget, point);
                    RetainedPointerMoved?.Invoke(motionTarget, point, new Vector2(point.X - _previousMouse.X, point.Y - _previousMouse.Y));
                }
                if (_captured != null && mouse.LeftButton == ButtonState.Pressed) UpdateDrag(point);
            }
            if (released && _captured != null)
            {
                var capture = _captured;
                _captured = null;
                var dropped = false;
                if (_dragSource != null)
                {
                    var dropTarget = GetDropTarget(point);
                    if (dropTarget != null)
                    {
                        dropTarget.DropData(point, _dragData);
                        dropped = true;
                    }
                    _dragSource.NotifyDragEnded(dropped);
                    _dragSource = null;
                    _dragData = null;
                }
                DispatchPointerReleased(capture, point);
                RetainedPointerReleased?.Invoke(capture, point);
            }
            var wheelDelta = mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue;
            if (wheelDelta != 0)
                for (var control = target; control != null; control = control.VisualParent)
                    if (control.PointerWheel(wheelDelta)) break;

            foreach (var key in keyboard.GetPressedKeys())
                if (!_previousKeyboard.IsKeyDown(key))
                    DispatchKey(key, keyboard);
            foreach (var key in _previousKeyboard.GetPressedKeys())
                if (!keyboard.IsKeyDown(key))
                    FocusedControl?.KeyReleased(key);

            // Input handlers may add transient roots (for example a MenuButton opening its PopupMenu).
            // Process a stable snapshot so the current frame remains valid; the new root participates next frame.
            foreach (var root in new List<Control>(_roots)) if (root.IsRendered) root.Process(gameTime);
            _previousMouse = mouse;
            _previousKeyboard = keyboard;
        }

        private Point ToLogicalPointerPosition(Point physicalPosition) => Math.Abs(DisplayScale - 1f) <= .0001f
            ? physicalPosition
            : new Point(
                (int)MathF.Round(physicalPosition.X / DisplayScale),
                (int)MathF.Round(physicalPosition.Y / DisplayScale));

        private Control UpdateInjectedPointerTarget(Point point, Popup modalPopup = null)
        {
            PointerPosition = point;
            modalPopup ??= GetActiveModalPopup();
            var target = modalPopup == null ? HitTest(point) : HitTest(modalPopup, point);
            UpdateHoveredTarget(target);
            return target;
        }

        private void UpdateHoveredTarget(Control target)
        {
            if (ReferenceEquals(target, _hovered)) return;

            var previousChain = VisualAncestorChain(_hovered);
            var nextChain = VisualAncestorChain(target);
            var previousIndex = previousChain.Count - 1;
            var nextIndex = nextChain.Count - 1;
            while (previousIndex >= 0 && nextIndex >= 0 && ReferenceEquals(previousChain[previousIndex], nextChain[nextIndex]))
            {
                previousIndex--;
                nextIndex--;
            }

            for (var index = 0; index <= previousIndex; index++)
                previousChain[index].PointerExited();
            for (var index = nextIndex; index >= 0; index--)
                nextChain[index].PointerEntered();
            _hovered = target;
        }

        private static List<Control> VisualAncestorChain(Control control)
        {
            var chain = new List<Control>();
            for (var current = control; current != null; current = current.VisualParent)
                chain.Add(current);
            return chain;
        }

        private static void DispatchInjectedPointerButton(Control target, Point point, PointerButton button, bool pressed)
        {
            if (target == null) return;
            for (var control = target; control != null; control = control.VisualParent)
            {
                if (pressed) control.PointerButtonPressed(point, button);
                else control.PointerButtonReleased(point, button);
                if (control.ConsumeEventAccepted() || control.MouseFilter != MouseFilter.Pass) break;
            }
        }

        private static bool DispatchPointerWheel(Control target, int delta)
        {
            for (var control = target; control != null; control = control.VisualParent)
                if (control.PointerWheel(delta)) return true;
            return false;
        }

        public void Layout()
        {
            foreach (var root in _roots)
            {
                if (root.Size == Vector2.Zero && ViewportSize != Vector2.Zero) root.Size = ViewportSize;
                root.LayoutTree();
            }
        }

        public void Draw(GraphicsDevice graphicsDevice)
        {
            if (graphicsDevice == null) throw new ArgumentNullException(nameof(graphicsDevice));
            if (_iconResources == null || !ReferenceEquals(_iconResources.GraphicsDevice, graphicsDevice))
            {
                _iconResources?.Dispose();
                _iconResources = new DefaultThemeIconResources(graphicsDevice);
            }
            if (_iconResources.Ensure(DisplayScale, ThemeIconRenderingPolicy))
                foreach (var root in _roots) root.MarkThemeDirty();
            Layout();
            if (_renderer == null || _renderer.GraphicsDevice != graphicsDevice)
            {
                _renderer?.Dispose();
                _renderer = new UIRenderContext(graphicsDevice, Theme);
            }
            _renderer.Theme = Theme;
            _renderer.DisplayScale = DisplayScale;
            _renderer.DisplayFontResolver = DisplayFontResolver;
            _renderer.TextLayoutEngine = TextLayoutEngine;
            _renderer.ThemeIconSvgFallback = _iconResources.RecordSvgFallback;
            foreach (var request in _svgPrewarmRequests) _renderer.PrewarmSvg(request.Source, request.LogicalSize);
            _svgPrewarmRequests.Clear();
            _renderer.Begin();
            try
            {
                foreach (var root in GetRootsInDrawOrder()) if (root.IsRendered) root.DrawTree(_renderer);
                DrawTooltip(_renderer);
            }
            finally { _renderer.End(); }
        }

        private readonly struct SvgPrewarmRequest
        {
            internal SvgPrewarmRequest(SvgImageSource source, Vector2 logicalSize)
            {
                Source = source;
                LogicalSize = logicalSize;
            }

            internal SvgImageSource Source { get; }
            internal Vector2 LogicalSize { get; }
        }

        public void SetFocus(Control control)
        {
            var modalPopup = GetActiveModalPopup();
            if (control != null && modalPopup != null && !modalPopup.IsAncestorOf(control)) return;
            if (control != null && (control.Context != this || !control.IsRendered || !control.IsEffectivelyEnabled || control.FocusMode == FocusMode.None)) return;
            if (FocusedControl == control) return;
            var previous = FocusedControl;
            FocusedControl = control;
            previous?.FocusLost();
            FocusedControl?.FocusGained();
        }

        public Control HitTest(Point point)
        {
            var roots = GetRootsInDrawOrder();
            for (var i = roots.Count - 1; i >= 0; i--)
            {
                var hit = HitTest(roots[i], point);
                if (hit != null) return hit;
            }
            return null;
        }

        private static Control HitTest(Control control, Point point)
        {
            if (!control.IsRendered || !control.IsEffectivelyEnabled || !control.IsHitTestVisible || !control.TryTransformHitTestPoint(point, out point)) return null;
            if (!control.ContainsComposedClipPoint(point)) return null;
            if (control.ClipContents && !control.ContainsPoint(point)) return null;
            if (control.MouseFilter != MouseFilter.Ignore && control.ContainsPoint(point) && control.HitTestBeforeChildren(point)) return control;
            var children = control.GetChildrenInDrawOrder();
            for (var i = children.Count - 1; i >= 0; i--)
            {
                var hit = HitTest(children[i], point);
                if (hit != null) return hit;
            }
            return control.MouseFilter != MouseFilter.Ignore && control.ContainsPoint(point) ? control : null;
        }

        private static void DispatchPointerPressed(Control target, Point point)
        {
            for (var control = target; control != null; control = control.VisualParent)
            {
                control.PointerPressed(point);
                if (control.ConsumeEventAccepted() || control.MouseFilter != MouseFilter.Pass) break;
            }
        }

        private static void DispatchPointerMoved(Control target, Point point)
        {
            for (var control = target; control != null; control = control.VisualParent)
            {
                control.PointerMoved(point);
                if (control.ConsumeEventAccepted() || control.MouseFilter != MouseFilter.Pass) break;
            }
        }

        private static void DispatchPointerButtonTransitions(Control target, Point point, MouseState mouse, MouseState previousMouse)
        {
            DispatchPointerButtonTransition(target, point, PointerButton.Left, mouse.LeftButton, previousMouse.LeftButton);
            DispatchPointerButtonTransition(target, point, PointerButton.Right, mouse.RightButton, previousMouse.RightButton);
            DispatchPointerButtonTransition(target, point, PointerButton.Middle, mouse.MiddleButton, previousMouse.MiddleButton);
            DispatchPointerButtonTransition(target, point, PointerButton.XButton1, mouse.XButton1, previousMouse.XButton1);
            DispatchPointerButtonTransition(target, point, PointerButton.XButton2, mouse.XButton2, previousMouse.XButton2);
        }

        private static void DispatchPointerButtonTransition(Control target, Point point, PointerButton button, ButtonState current, ButtonState previous)
        {
            if (target == null || current == previous) return;
            for (var control = target; control != null; control = control.VisualParent)
            {
                if (current == ButtonState.Pressed) control.PointerButtonPressed(point, button);
                else control.PointerButtonReleased(point, button);
                if (control.ConsumeEventAccepted() || control.MouseFilter != MouseFilter.Pass) break;
            }
        }

        private static void DispatchPointerReleased(Control target, Point point)
        {
            for (var control = target; control != null; control = control.VisualParent)
            {
                control.PointerReleased(point, control.ContainsPoint(point));
                if (control.ConsumeEventAccepted() || control.MouseFilter != MouseFilter.Pass) break;
            }
        }

        private void DispatchKey(Keys key, KeyboardState keyboard)
        {
            var modalPopup = GetActiveModalPopup();
            if (modalPopup != null && (FocusedControl == null || !modalPopup.IsAncestorOf(FocusedControl))) SetFocus(modalPopup);
            if (DispatchShortcutInput(key, keyboard, modalPopup)) return;
            if (key == Keys.Tab)
            {
                var backwards = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
                var explicitTarget = backwards ? FocusedControl?.FocusPrevious : FocusedControl?.FocusNext;
                if (CanFocus(explicitTarget))
                {
                    SetFocus(explicitTarget);
                    return;
                }
                var focusable = new List<Control>();
                if (modalPopup != null) CollectFocusable(modalPopup, focusable);
                else foreach (var root in _roots) CollectFocusable(root, focusable);
                if (focusable.Count > 0)
                {
                    var current = focusable.IndexOf(FocusedControl);
                    SetFocus(focusable[(current + (backwards ? focusable.Count - 1 : 1)) % focusable.Count]);
                }
                return;
            }
            var neighbor = key == Keys.Left ? FocusedControl?.FocusNeighborLeft :
                key == Keys.Up ? FocusedControl?.FocusNeighborTop :
                key == Keys.Right ? FocusedControl?.FocusNeighborRight :
                key == Keys.Down ? FocusedControl?.FocusNeighborBottom : null;
            if (CanFocus(neighbor))
            {
                SetFocus(neighbor);
                return;
            }
            FocusedControl?.KeyPressed(key);
        }

        private bool DispatchShortcutInput(Keys key, KeyboardState keyboard, Control modalRoot)
        {
            if (modalRoot != null) return DispatchShortcutInput(modalRoot, key, keyboard);
            var roots = GetRootsInDrawOrder();
            for (var i = roots.Count - 1; i >= 0; i--)
                if (DispatchShortcutInput(roots[i], key, keyboard)) return true;
            return false;
        }

        private static bool DispatchShortcutInput(Control control, Keys key, KeyboardState keyboard)
        {
            if (control == null || !control.IsRendered || !control.IsEffectivelyEnabled) return false;
            var children = control.GetChildrenInDrawOrder();
            for (var i = children.Count - 1; i >= 0; i--)
                if (DispatchShortcutInput(children[i], key, keyboard)) return true;
            return control.ShortcutInput(key, keyboard);
        }

        private bool CanFocus(Control control) => control != null && control.Context == this && control.IsRendered && control.IsEffectivelyEnabled && control.FocusMode != FocusMode.None;

        /// <summary>Forwards one platform text-input character to the focused retained control.</summary>
        public void TextInput(char character)
        {
            if (!char.IsControl(character)) FocusedControl?.TextInput(character);
        }

        /// <summary>Forwards platform IME preedit text and its selected range to the focused control.</summary>
        public void TextComposition(string text, int selectionStart = 0, int selectionLength = 0) =>
            FocusedControl?.TextComposition(text ?? string.Empty, selectionStart, selectionLength);

        private void UpdateDrag(Point point)
        {
            if (_dragSource == null)
            {
                var deltaX = point.X - _dragStartPosition.X;
                var deltaY = point.Y - _dragStartPosition.Y;
                if (deltaX * deltaX + deltaY * deltaY < 16) return;
                var data = _captured.GetDragData(_dragStartPosition);
                if (data == null) return;
                _dragSource = _captured;
                _dragData = data;
                _dragSource.NotifyDragStarted(data);
            }
        }

        private Control GetDropTarget(Point point)
        {
            for (var control = HitTest(point); control != null; control = control.VisualParent)
                if (control.CanDropData(point, _dragData)) return control;
            return null;
        }

        private Popup GetActiveModalPopup()
        {
            var roots = GetRootsInDrawOrder();
            for (var index = roots.Count - 1; index >= 0; index--)
            {
                var popup = GetTopmostModalPopup(roots[index]);
                if (popup != null) return popup;
            }
            return null;
        }

        private static Popup GetTopmostModalPopup(Control control)
        {
            if (!control.IsRendered) return null;
            var children = control.GetChildrenInDrawOrder();
            for (var index = children.Count - 1; index >= 0; index--)
            {
                var popup = GetTopmostModalPopup(children[index]);
                if (popup != null) return popup;
            }
            return control is Popup popupControl && popupControl.Modal ? popupControl : null;
        }

        private void UpdateTooltip(Control target, Point position, TimeSpan elapsed)
        {
            var owner = GetTooltipOwner(target, position, out var text);
            _tooltipPointerPosition = position;
            if (owner != _tooltipOwner || !string.Equals(text, _tooltipText, StringComparison.Ordinal))
            {
                _tooltipOwner = owner;
                _tooltipText = text ?? string.Empty;
                _tooltipElapsed = TimeSpan.Zero;
                IsTooltipVisible = false;
            }
            if (_tooltipOwner == null || string.IsNullOrEmpty(_tooltipText)) return;
            _tooltipElapsed += elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
            if (_tooltipElapsed >= TooltipDelay) IsTooltipVisible = true;
        }

        /// <summary>
        /// Matches Godot's Viewport::_gui_get_tooltip: bubbles up to ancestors when a control's own tooltip is empty,
        /// but only while ancestors don't stop propagation — a control with MouseFilter.Stop is where the search ends.
        /// </summary>
        private static Control GetTooltipOwner(Control target, Point position, out string text)
        {
            for (var control = target; control != null; control = control.VisualParent)
            {
                text = control.GetTooltip(position);
                if (!string.IsNullOrEmpty(text)) return control;
                if (control.MouseFilter == MouseFilter.Stop) break;
            }
            text = string.Empty;
            return null;
        }

        private void DrawTooltip(UIRenderContext context)
        {
            var font = EffectiveTooltipUIFont;
            if (!IsTooltipVisible || string.IsNullOrEmpty(_tooltipText) || font == null) return;
            var textSize = TextMetrics.Measure(font, _tooltipText);
            var width = (int)MathF.Ceiling(textSize.X + TooltipPadding.Horizontal);
            var height = (int)MathF.Ceiling(Math.Max(TextMetrics.LineHeight(font), textSize.Y) + TooltipPadding.Vertical);
            var position = _tooltipPointerPosition.ToVector2() + TooltipOffset;
            var viewport = context.GraphicsDevice.Viewport.Bounds;
            position.X = MathHelper.Clamp(position.X, viewport.Left, Math.Max(viewport.Left, viewport.Right - width));
            position.Y = MathHelper.Clamp(position.Y, viewport.Top, Math.Max(viewport.Top, viewport.Bottom - height));
            var bounds = new Rectangle((int)position.X, (int)position.Y, width, height);
            var style = Theme.GetStyleBox("panel", "TooltipPanel");
            if (style != null) style.Draw(context, bounds);
            else { context.FillRounded(bounds, Theme.PanelColor, 3); context.Border(bounds, Theme.PanelBorderColor); }
            context.Text(font, _tooltipText, position + new Vector2(TooltipPadding.Left, TooltipPadding.Top), Theme.TextColor);
        }

        private static void CollectFocusable(Control control, List<Control> result)
        {
            if (!control.IsRendered || !control.IsEffectivelyEnabled) return;
            if (control.FocusMode == FocusMode.All) result.Add(control);
            foreach (var child in control.VisualChildren) CollectFocusable(child, result);
        }

        internal void MarkRootOrderDirty() => _rootOrderDirty = true;
        internal bool TryGetDefaultThemeIcon(string itemName, IEnumerable<string> typeNames, out ThemeIcon? icon)
        {
            if (_iconResources != null && _iconResources.Theme.TryGetIcon(itemName, typeNames, out icon)) return true;
            _iconResources?.RecordMissing(itemName);
            icon = null;
            return false;
        }
        internal bool ResolveLayoutDirection(LayoutDirection direction)
        {
            if (direction == LayoutDirection.RightToLeft) return true;
            if (direction == LayoutDirection.LeftToRight) return false;
            if (direction == LayoutDirection.SystemLocale) return SystemCulture.TextInfo.IsRightToLeft;
            if (direction == LayoutDirection.ApplicationLocale) return ApplicationCulture.TextInfo.IsRightToLeft;
            return RootLayoutDirection == LayoutDirection.Inherited
                ? ApplicationCulture.TextInfo.IsRightToLeft
                : ResolveLayoutDirection(RootLayoutDirection);
        }
        private List<KeyValuePair<Control, Dictionary<Control, LayoutDirection>>> CaptureLayoutDirections()
        {
            var values = new List<KeyValuePair<Control, Dictionary<Control, LayoutDirection>>>(_roots.Count);
            foreach (var root in _roots)
                values.Add(new KeyValuePair<Control, Dictionary<Control, LayoutDirection>>(root, root.CaptureEffectiveLayoutDirections()));
            return values;
        }

        private static void MarkLayoutDirectionsDirty(List<KeyValuePair<Control, Dictionary<Control, LayoutDirection>>> previous)
        {
            foreach (var pair in previous) pair.Key.MarkInheritedLayoutDirectionDirty(pair.Value);
        }

        internal void RegisterXamlScope(XamlAttachmentScope scope) => _xamlScopes.Add(scope);
        internal void UnregisterXamlScope(XamlAttachmentScope scope) => _xamlScopes.Remove(scope);

        private void UpdateXamlScopes(GameTime gameTime)
        {
            var snapshot = new List<XamlAttachmentScope>(_xamlScopes);
            foreach (var scope in snapshot) scope.Update(gameTime);
        }

        public IDisposable RegisterFrameBoundaryCallback(Action<GameTime> callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            lock (_frameBoundaryCallbacks) _frameBoundaryCallbacks.Add(callback);
            return new FrameBoundaryRegistration(this, callback);
        }

        private void UpdateFrameBoundaryCallbacks(GameTime gameTime)
        {
            Action<GameTime>[] callbacks;
            lock (_frameBoundaryCallbacks) callbacks = new List<Action<GameTime>>(_frameBoundaryCallbacks).ToArray();
            foreach (var callback in callbacks) callback(gameTime);
        }

        private sealed class FrameBoundaryRegistration : IDisposable
        {
            private UIContext _owner;
            private Action<GameTime> _callback;
            public FrameBoundaryRegistration(UIContext owner, Action<GameTime> callback) { _owner = owner; _callback = callback; }
            public void Dispose()
            {
                var owner = _owner;
                var callback = _callback;
                _owner = null;
                _callback = null;
                if (owner != null) lock (owner._frameBoundaryCallbacks) owner._frameBoundaryCallbacks.Remove(callback);
            }
        }

        private IReadOnlyList<Control> GetRootsInDrawOrder()
        {
            if (!_rootOrderDirty) return _rootsInDrawOrder;
            _rootsInDrawOrder.Clear();
            _rootsInDrawOrder.AddRange(_roots);
            _rootsInDrawOrder.Sort((left, right) =>
            {
                var zOrder = left.ZIndex.CompareTo(right.ZIndex);
                return zOrder != 0 ? zOrder : left.TreeOrder.CompareTo(right.TreeOrder);
            });
            _rootOrderDirty = false;
            return _rootsInDrawOrder;
        }

        public void Dispose()
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo failure = null;
            var roots = _roots.ToArray();
            foreach (var root in roots)
            {
                try { Remove(root); }
                catch (Exception exception) { failure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception); }
            }
            var xamlScopes = _xamlScopes.ToArray();
            foreach (var scope in xamlScopes)
            {
                try { scope.DisposeOwner(); }
                catch (Exception exception) { failure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception); }
            }
            _xamlScopes.Clear();
            lock (_frameBoundaryCallbacks) _frameBoundaryCallbacks.Clear();
            try { _renderer?.Dispose(); }
            catch (Exception exception) { failure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception); }
            _renderer = null;
            try { _iconResources?.Dispose(); }
            catch (Exception exception) { failure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception); }
            _iconResources = null;
            TextLayoutEngine.Clear();
            global::Forma.TextLayoutEngine.ClearSharedCaches();
            failure?.Throw();
        }
    }

    /// <summary>Connects a <see cref="UIContext"/> to an XNA-compatible <see cref="Game"/>.</summary>
    public sealed class UIComponent : DrawableGameComponent
    {
        private readonly RuntimeTextInputAdapter _textInput;

        public UIComponent(Game game, UIContext context = null) : base(game)
        {
            Context = context ?? new UIContext();
            _textInput = new RuntimeTextInputAdapter(game, Context.TextInput);
        }
        public UIContext Context { get; }
        public override void Update(GameTime gameTime)
        {
            Context.ViewportSize = new Vector2(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
            Context.Update(gameTime);
        }
        public override void Draw(GameTime gameTime) => Context.Draw(GraphicsDevice);
        protected override void Dispose(bool disposing)
        {
            if (disposing) { _textInput.Dispose(); Context.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
