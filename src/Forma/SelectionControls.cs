// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// Control APIs and behavior are adapted from Godot Engine's LinkButton, TextureButton,
// ScrollBar, TextureProgressBar, TabBar, ItemList, and RichTextLabel implementations
// under scene/gui; see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    public enum LinkButtonUnderlineMode { Always, OnHover, Never }

    /// <summary>Activates an absolute URI through a host launcher and applies configurable link underlining.</summary>
    public sealed class LinkButton : BaseButton
    {
        public LinkButton()
        {
            Pressed += (_, _) => OpenUri();
        }
        /// <summary>Optional absolute URI opened when this link is activated, like Godot's uri property.</summary>
        public string Uri { get; set; } = string.Empty;
        /// <summary>Optional host capability used to launch absolute URIs.</summary>
        public Func<LinkButton, System.Uri, bool> UriLauncher { get; set; }
        public bool IsUriLaunchingAvailable => UriLauncher != null;
        public LinkButtonUnderlineMode UnderlineMode { get; set; } = LinkButtonUnderlineMode.Always;
        /// <summary>Raised when a valid URI is activated, whether or not a launcher is available.</summary>
        public event Action<LinkButton, string> UriRequested;
        private void OpenUri()
        {
            if (string.IsNullOrWhiteSpace(Uri) || !System.Uri.TryCreate(Uri, UriKind.Absolute, out var uri)) return;
            UriRequested?.Invoke(this, uri.AbsoluteUri);
            try { UriLauncher?.Invoke(this, uri); }
            catch (PlatformNotSupportedException) { }
            catch (NotImplementedException) { }
        }
    }

    /// <summary>Matches Godot TextureButton's texture placement policies.</summary>
    public enum TextureButtonStretchMode
    {
        Scale,
        Tile,
        Keep,
        KeepCentered,
        KeepAspect,
        KeepAspectCentered,
        KeepAspectCovered,
    }

    /// <summary>A compact, device-independent alpha-style hit mask for <see cref="TextureButton"/>.</summary>
    public sealed class TextureButtonClickMask
    {
        private readonly bool[] _bits;
        public TextureButtonClickMask(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            Width = width; Height = height; _bits = new bool[width * height];
        }
        public int Width { get; }
        public int Height { get; }
        public bool this[int x, int y]
        {
            get => x >= 0 && x < Width && y >= 0 && y < Height && _bits[y * Width + x];
            set
            {
                if (x < 0 || x >= Width) throw new ArgumentOutOfRangeException(nameof(x));
                if (y < 0 || y >= Height) throw new ArgumentOutOfRangeException(nameof(y));
                _bits[y * Width + x] = value;
            }
        }
    }

    /// <summary>Local texture destination and source rectangles calculated for a TextureButton.</summary>
    public readonly struct TextureButtonLayout
    {
        public TextureButtonLayout(Rectangle destination, Rectangle source, bool tile) { Destination = destination; Source = source; Tile = tile; }
        public Rectangle Destination { get; }
        public Rectangle Source { get; }
        public bool Tile { get; }
    }

    /// <summary>Uses state-specific textures for button feedback with configurable scaling, flipping, and alpha-mask hit testing.</summary>
    public sealed class TextureButton : BaseButton
    {
        public Texture2D TextureNormal { get; set; }
        public Texture2D TextureHover { get; set; }
        public Texture2D TexturePressed { get; set; }
        public Texture2D TextureDisabled { get; set; }
        public Texture2D TextureFocused { get; set; }
        public TextureButtonClickMask ClickMask { get; set; }
        public bool IgnoreTextureSize { get; set; }
        public TextureButtonStretchMode StretchMode { get; set; } = TextureButtonStretchMode.Keep;
        public bool FlipH { get; set; }
        public bool FlipV { get; set; }
        public void SetTextureNormal(Texture2D texture) { TextureNormal = texture; QueueLayout(); }
        public Texture2D GetTextureNormal() => TextureNormal;
        public void SetTexturePressed(Texture2D texture) { TexturePressed = texture; QueueLayout(); }
        public Texture2D GetTexturePressed() => TexturePressed;
        public void SetTextureHover(Texture2D texture) { TextureHover = texture; QueueLayout(); }
        public Texture2D GetTextureHover() => TextureHover;
        public void SetTextureDisabled(Texture2D texture) { TextureDisabled = texture; QueueLayout(); }
        public Texture2D GetTextureDisabled() => TextureDisabled;
        public void SetTextureFocused(Texture2D texture) { TextureFocused = texture; QueueLayout(); }
        public Texture2D GetTextureFocused() => TextureFocused;
        public void SetClickMask(TextureButtonClickMask mask) { ClickMask = mask; QueueLayout(); }
        public TextureButtonClickMask GetClickMask() => ClickMask;
        public void SetIgnoreTextureSize(bool ignore) { IgnoreTextureSize = ignore; QueueLayout(); }
        public bool GetIgnoreTextureSize() => IgnoreTextureSize;
        public void SetStretchMode(TextureButtonStretchMode mode) { if (!Enum.IsDefined(typeof(TextureButtonStretchMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode)); StretchMode = mode; QueueLayout(); }
        public TextureButtonStretchMode GetStretchMode() => StretchMode;
        public void SetFlipH(bool enable) => FlipH = enable;
        public bool IsFlippedH() => FlipH;
        public void SetFlipV(bool enable) => FlipV = enable;
        public bool IsFlippedV() => FlipV;
        public override Vector2 GetMinimumSize()
        {
            if (IgnoreTextureSize) return CustomMinimumSize;
            var size = GetPreferredTextureSize();
            return Vector2.Max(CustomMinimumSize, size);
        }
        /// <summary>Calculates the local destination and source regions using the selected stretch mode.</summary>
        public TextureButtonLayout GetTextureLayout(Vector2 textureSize)
        {
            var width = Math.Max(0, (int)MathF.Round(textureSize.X));
            var height = Math.Max(0, (int)MathF.Round(textureSize.Y));
            var controlWidth = Math.Max(0, (int)MathF.Round(Size.X));
            var controlHeight = Math.Max(0, (int)MathF.Round(Size.Y));
            if (width == 0 || height == 0) return new TextureButtonLayout(Rectangle.Empty, Rectangle.Empty, false);
            var source = new Rectangle(0, 0, width, height);
            var destination = new Rectangle(0, 0, width, height);
            switch (StretchMode)
            {
                case TextureButtonStretchMode.Scale:
                    destination = new Rectangle(0, 0, controlWidth, controlHeight); break;
                case TextureButtonStretchMode.Tile:
                    destination = new Rectangle(0, 0, controlWidth, controlHeight); return new TextureButtonLayout(destination, source, true);
                case TextureButtonStretchMode.KeepCentered:
                    destination = new Rectangle((controlWidth - width) / 2, (controlHeight - height) / 2, width, height); break;
                case TextureButtonStretchMode.KeepAspect:
                case TextureButtonStretchMode.KeepAspectCentered:
                    if (controlWidth > 0 && controlHeight > 0)
                    {
                        // Godot's exact two-pass algorithm (TextureButton::_notification, matching
                        // TextureRect::_notification's STRETCH_KEEP_ASPECT): first assume the texture
                        // fills the full height and derive width proportionally, truncating like C++'s
                        // float-to-int assignment (not rounding); if that overflows the available width,
                        // clamp width and recompute height from it using pure integer division.
                        var destinationHeight = controlHeight;
                        var destinationWidth = (int)(width * (float)destinationHeight / height);
                        if (destinationWidth > controlWidth)
                        {
                            destinationWidth = controlWidth;
                            destinationHeight = height * destinationWidth / width;
                        }
                        var x = StretchMode == TextureButtonStretchMode.KeepAspectCentered ? (controlWidth - destinationWidth) / 2 : 0;
                        var y = StretchMode == TextureButtonStretchMode.KeepAspectCentered ? (controlHeight - destinationHeight) / 2 : 0;
                        destination = new Rectangle(x, y, destinationWidth, destinationHeight);
                    }
                    break;
                case TextureButtonStretchMode.KeepAspectCovered:
                    if (controlWidth > 0 && controlHeight > 0)
                    {
                        var scale = Math.Max(controlWidth / (float)width, controlHeight / (float)height);
                        var sourceWidth = Math.Min(width, Math.Max(1, (int)MathF.Round(controlWidth / scale)));
                        var sourceHeight = Math.Min(height, Math.Max(1, (int)MathF.Round(controlHeight / scale)));
                        source = new Rectangle((width - sourceWidth) / 2, (height - sourceHeight) / 2, sourceWidth, sourceHeight);
                        destination = new Rectangle(0, 0, controlWidth, controlHeight);
                    }
                    break;
            }
            return new TextureButtonLayout(destination, source, false);
        }
        public override bool ContainsPoint(Point point)
        {
            if (!base.ContainsPoint(point) || ClickMask == null) return base.ContainsPoint(point);
            // Godot's has_point derives its layout from texdraw_size - the size of whichever texture is
            // actually drawn for the current state, falling back to the click mask's own size only when
            // no texture is set at all - not unconditionally the mask's size.
            var texture = GetCurrentTexture();
            var textureSize = texture != null ? new Vector2(texture.Width, texture.Height) : new Vector2(ClickMask.Width, ClickMask.Height);
            var layout = GetTextureLayout(textureSize);
            var localX = point.X - Bounds.X;
            var localY = point.Y - Bounds.Y;
            if (layout.Tile)
            {
                if (ClickMask.Width == 0 || ClickMask.Height == 0) return false;
                localX = ((localX % ClickMask.Width) + ClickMask.Width) % ClickMask.Width;
                localY = ((localY % ClickMask.Height) + ClickMask.Height) % ClickMask.Height;
                return ClickMask[localX, localY];
            }
            if (layout.Destination.Width <= 0 || layout.Destination.Height <= 0 ||
                localX < layout.Destination.X || localY < layout.Destination.Y ||
                localX >= layout.Destination.Right || localY >= layout.Destination.Bottom) return false;
            // Godot's has_point scales into the click mask's OWN size (mask_size / _position_rect.size),
            // not the drawn texture's size - the two only coincide when the mask matches the texture.
            var maskX = layout.Source.X + (int)((localX - layout.Destination.X) * ClickMask.Width / (float)layout.Destination.Width);
            var maskY = layout.Source.Y + (int)((localY - layout.Destination.Y) * ClickMask.Height / (float)layout.Destination.Height);
            return ClickMask[maskX, maskY];
        }
        /// <summary>Computes the focus-ring overlay's draw layout, matching Godot's draw_focus_only:
        /// the destination rect (and tile flag) are reused from the PRIMARY drawn texture's own layout,
        /// only falling back to geometry computed from the focus texture's own size when there is no
        /// primary texture at all - but the source region is never shared either way, since Godot's
        /// draw_texture_rect always samples the full focus texture image, unlike the primary texture's
        /// draw_texture_rect_region call.</summary>
        internal TextureButtonLayout? GetFocusOverlayLayout()
        {
            if (TextureFocused == null) return null;
            var texture = GetCurrentTexture();
            var reference = texture != null
                ? GetTextureLayout(new Vector2(texture.Width, texture.Height))
                : GetTextureLayout(new Vector2(TextureFocused.Width, TextureFocused.Height));
            var focusSource = new Rectangle(0, 0, TextureFocused.Width, TextureFocused.Height);
            return new TextureButtonLayout(reference.Destination, focusSource, reference.Tile);
        }
        private Vector2 GetPreferredTextureSize()
        {
            var texture = TextureNormal ?? TexturePressed ?? TextureHover;
            return texture != null ? new Vector2(texture.Width, texture.Height) : ClickMask == null ? Vector2.Zero : new Vector2(ClickMask.Width, ClickMask.Height);
        }
        internal Texture2D GetCurrentTexture()
        {
            if (!Enabled) return TextureDisabled ?? TextureNormal;
            if (IsPressing || ButtonPressed) return TexturePressed ?? TextureHover ?? TextureNormal;
            // Godot's DRAW_HOVER case falls through to Normal whenever Pressed is invalid, even if the
            // button is logically pressed - it never leaves the hover state texture-less.
            if (IsHovering) return TextureHover ?? (ButtonPressed && TexturePressed != null ? TexturePressed : TextureNormal);
            return TextureNormal;
        }
        internal void DrawTemplateTexture(UIRenderContext context, Texture2D texture, TextureButtonLayout layout)
        {
            var effects = (FlipH ? SpriteEffects.FlipHorizontally : SpriteEffects.None) | (FlipV ? SpriteEffects.FlipVertically : SpriteEffects.None);
            if (layout.Tile)
            {
                for (var y = 0; y < layout.Destination.Height; y += texture.Height)
                    for (var x = 0; x < layout.Destination.Width; x += texture.Width)
                    {
                        var width = Math.Min(texture.Width, layout.Destination.Width - x);
                        var height = Math.Min(texture.Height, layout.Destination.Height - y);
                        context.SpriteBatch.Draw(texture, new Rectangle(Bounds.X + x, Bounds.Y + y, width, height), new Rectangle(0, 0, width, height), Color.White, 0, Vector2.Zero, effects, 0);
                    }
                return;
            }
            var destination = new Rectangle(Bounds.X + layout.Destination.X, Bounds.Y + layout.Destination.Y, layout.Destination.Width, layout.Destination.Height);
            if (destination.Width > 0 && destination.Height > 0) context.SpriteBatch.Draw(texture, destination, layout.Source, Color.White, 0, Vector2.Zero, effects, 0);
        }
    }

    /// <summary>Scrolls a numeric range through step buttons, page regions, thumb dragging, or inertial drag-node input.</summary>
    public class ScrollBar : Range
    {
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.ScrollBar;
        public override AccessibilityActions AccessibilityActions => base.AccessibilityActions | AccessibilityActions.Scroll;
        private enum HighlightRegion { None, Decrement, Increment, Range }
        private bool _dragging;
        private bool _decrementActive;
        private bool _incrementActive;
        private HighlightRegion _highlight;
        private float _dragPositionAtClick;
        private float _dragRatioAtClick;
        private float _targetScroll;
        private bool _smoothScrolling;
        private bool _showStepButtons = true;
        private string _dragNodePath = string.Empty;
        private Control _dragNode;
        private bool _dragNodeTouching;
        private bool _dragNodeDecelerating;
        private Vector2 _dragNodeSpeed;
        private Vector2 _dragNodeAccum;
        private Vector2 _dragNodeFrom;
        private Vector2 _lastDragNodeAccum;
        private float _timeSinceDragNodeMotion;
        private const int PageDivisor = 8;
        private const int FallbackPageDivisor = 16;
        private const int MinimumGrabberSize = 4;
        private const float SmoothScrollSpeed = 500;
        private const float DragNodeDeceleration = 1000;
        // Godot's ScrollBar constructor calls set_step(0) - continuous/step-free by default, unlike
        // Range's own default of 1 - so an unconfigured scrollbar doesn't silently quantize every value.
        public ScrollBar(Orientation orientation) { Orientation = orientation; FocusMode = FocusMode.All; Step = 0; }
        public Orientation Orientation { get; }
        public float CustomStep { get; set; } = -1;
        public bool SmoothScrollEnabled { get; set; }
        public bool DragNodeEnabled { get; set; } = true;
        /// <summary>Shows the decrement and increment buttons. Hiding them reclaims their track space.</summary>
        public bool ShowStepButtons
        {
            get => _showStepButtons;
            set
            {
                if (_showStepButtons == value) return;
                _showStepButtons = value;
                _decrementActive = false;
                _incrementActive = false;
                _highlight = HighlightRegion.None;
                QueueLayout();
            }
        }
        public bool IsDraggingGrabber => _dragging;
        internal bool IsDecrementActive => _decrementActive;
        internal bool IsIncrementActive => _incrementActive;
        public bool IsDecrementHighlighted => _highlight == HighlightRegion.Decrement;
        public bool IsIncrementHighlighted => _highlight == HighlightRegion.Increment;
        public bool IsRangeHighlighted => _highlight == HighlightRegion.Range;
        public bool IsSmoothScrolling => _smoothScrolling;
        public float TargetScroll => _targetScroll;
        public bool IsDragNodeTouching => _dragNodeTouching;
        public bool IsDragNodeDecelerating => _dragNodeDecelerating;
        public Vector2 DragNodeSpeed => _dragNodeSpeed;
        /// <summary>Raised when user-driven scrolling changes the effective value, matching Godot's scrolling signal.</summary>
        public event EventHandler Scrolling;
        public void SetCustomStep(float customStep) => CustomStep = customStep;
        public float GetCustomStep() => CustomStep;
        public void SetSmoothScrollEnabled(bool enabled) => SmoothScrollEnabled = enabled;
        public bool IsSmoothScrollEnabled() => SmoothScrollEnabled;
        /// <summary>Shows or hides the scrollbar's decrement and increment buttons.</summary>
        public void SetShowStepButtons(bool visible) => ShowStepButtons = visible;
        /// <summary>Gets whether the scrollbar shows decrement and increment buttons.</summary>
        public bool IsShowingStepButtons() => ShowStepButtons;
        public void SetDragNode(string path)
        {
            _dragNodePath = path ?? string.Empty;
            ResolveDragNode();
        }
        public string GetDragNode() => _dragNodePath;
        public void SetDragNodeEnabled(bool enabled) => DragNodeEnabled = enabled;
        public bool IsDragNodeEnabled() => DragNodeEnabled;
        public void BeginDragNodeScroll(bool touchscreenAvailable = true)
        {
            if (!DragNodeEnabled) return;
            _dragNodeSpeed = Vector2.Zero;
            _dragNodeAccum = Vector2.Zero;
            _lastDragNodeAccum = Vector2.Zero;
            _dragNodeFrom = new Vector2(Orientation == Orientation.Horizontal ? Value : 0, Orientation == Orientation.Vertical ? Value : 0);
            _dragNodeTouching = touchscreenAvailable;
            _dragNodeDecelerating = false;
            _timeSinceDragNodeMotion = 0;
        }
        public void DragNodeScrollBy(Vector2 relativeMotion)
        {
            if (!_dragNodeTouching || _dragNodeDecelerating) return;
            _dragNodeAccum -= relativeMotion;
            var position = _dragNodeFrom + _dragNodeAccum;
            ScrollTo(Orientation == Orientation.Horizontal ? position.X : position.Y);
            _timeSinceDragNodeMotion = 0;
        }
        public void EndDragNodeScroll()
        {
            if (!_dragNodeTouching) return;
            if (_dragNodeSpeed == Vector2.Zero)
            {
                _dragNodeDecelerating = false;
                _dragNodeTouching = false;
            }
            else
            {
                _dragNodeDecelerating = true;
            }
        }
        protected override void OnContextChanged(UIContext previous, UIContext current)
        {
            if (previous != null)
            {
                previous.RetainedPointerPressed -= OnDragNodePointerPressed;
                previous.RetainedPointerMoved -= OnDragNodePointerMoved;
                previous.RetainedPointerReleased -= OnDragNodePointerReleased;
            }
            if (current != null)
            {
                current.RetainedPointerPressed += OnDragNodePointerPressed;
                current.RetainedPointerMoved += OnDragNodePointerMoved;
                current.RetainedPointerReleased += OnDragNodePointerReleased;
            }
            ResolveDragNode();
            base.OnContextChanged(previous, current);
        }
        private void OnDragNodePointerPressed(Control target, Point point)
        {
            if (IsDragNodeTarget(target)) BeginDragNodeScroll(Context?.TouchscreenAvailable == true);
        }
        private void OnDragNodePointerMoved(Control target, Point point, Vector2 relativeMotion)
        {
            if (IsDragNodeTarget(target)) DragNodeScrollBy(relativeMotion);
        }
        private void OnDragNodePointerReleased(Control target, Point point)
        {
            if (IsDragNodeTarget(target)) EndDragNodeScroll();
        }
        private bool IsDragNodeTarget(Control target)
        {
            if (_dragNode == null) ResolveDragNode();
            for (var control = target; control != null; control = control.VisualParent)
                if (control == _dragNode) return true;
            return false;
        }
        private void ResolveDragNode()
        {
            _dragNode = null;
            if (string.IsNullOrWhiteSpace(_dragNodePath)) return;
            Control control = this;
            foreach (var part in _dragNodePath.Split('/'))
            {
                if (part.Length == 0 || part == ".") continue;
                if (part == "..") { control = control?.Parent; continue; }
                Control next = null;
                if (control != null)
                    foreach (var child in control.Children)
                        if (child.Name == part) { next = child; break; }
                control = next;
                if (control == null) return;
            }
            _dragNode = control;
        }
        public override Vector2 GetMinimumSize()
        {
            var mainAxis = ShowStepButtons ? 32 : MinimumGrabberSize;
            return Vector2.Max(CustomMinimumSize, Orientation == Orientation.Horizontal ? new Vector2(mainAxis, 14) : new Vector2(14, mainAxis));
        }
        public Rectangle GetDecrementButtonRectangle()
        {
            var button = GetButtonSize();
            return Orientation == Orientation.Horizontal
                ? new Rectangle(0, 0, button, Math.Max(0, Bounds.Height))
                : new Rectangle(0, 0, Math.Max(0, Bounds.Width), button);
        }
        public Rectangle GetIncrementButtonRectangle()
        {
            var button = GetButtonSize();
            return Orientation == Orientation.Horizontal
                ? new Rectangle(Math.Max(0, Bounds.Width - button), 0, button, Math.Max(0, Bounds.Height))
                : new Rectangle(0, Math.Max(0, Bounds.Height - button), Math.Max(0, Bounds.Width), button);
        }
        public Rectangle GetGrabberRectangle()
        {
            var button = GetButtonSize();
            var start = button + (int)MathF.Round(GetGrabberOffset());
            var size = Math.Max(0, (int)MathF.Round(GetGrabberSize()));
            return Orientation == Orientation.Horizontal
                ? new Rectangle(start, 0, size, Math.Max(0, Bounds.Height))
                : new Rectangle(0, start, Math.Max(0, Bounds.Width), size);
        }
        internal override void PointerPressed(Point point)
        {
            base.PointerPressed(point);
            UpdateHighlight(point);
            var local = ToLocalMainAxis(point);
            var button = GetButtonSize();
            var length = GetMainLength();
            if (local < button)
            {
                _decrementActive = true;
                ScrollBy(-GetEffectiveStep());
                return;
            }
            if (local > length - button)
            {
                _incrementActive = true;
                ScrollBy(GetEffectiveStep());
                return;
            }

            var areaPosition = local - button;
            var grabberOffset = GetGrabberOffset();
            var grabberSize = GetGrabberSize();
            if (areaPosition < grabberOffset)
            {
                PageBy(-1);
                return;
            }
            if (areaPosition < grabberOffset + grabberSize)
            {
                _dragging = true;
                _dragPositionAtClick = areaPosition;
                _dragRatioAtClick = Ratio;
                return;
            }
            PageBy(1);
        }
        internal override void PointerMoved(Point point)
        {
            if (_dragging)
            {
                var areaPosition = ToLocalMainAxis(point) - GetButtonSize();
                var diff = (areaPosition - _dragPositionAtClick) / Math.Max(1f, GetAreaSize());
                ScrollToRatio(_dragRatioAtClick + diff);
            }
            else UpdateHighlight(point);
        }
        internal override void PointerReleased(Point point, bool isInside)
        {
            _dragging = false;
            _decrementActive = false;
            _incrementActive = false;
            if (isInside) UpdateHighlight(point);
        }
        internal override void CancelInput()
        {
            _dragging = false;
            _decrementActive = false;
            _incrementActive = false;
            _dragNodeTouching = false;
            _dragNodeDecelerating = false;
            _dragNodeSpeed = _dragNodeAccum = _lastDragNodeAccum = Vector2.Zero;
            _smoothScrolling = false;
            _targetScroll = Value;
            base.CancelInput();
        }
        internal override void PointerEntered() { UpdateHighlight(Context?.PointerPosition ?? Point.Zero); base.PointerEntered(); }
        internal override void PointerExited() { _highlight = HighlightRegion.None; base.PointerExited(); }
        internal override void Process(GameTime gameTime)
        {
            ProcessSmoothScroll(gameTime);
            ProcessDragNode(gameTime);
            base.Process(gameTime);
        }
        internal override bool PointerWheel(int delta)
        {
            if (delta == 0) return false;
            var factor = Math.Abs(delta) >= 120 ? delta / 120f : Math.Sign(delta);
            // Godot's ScrollBar::gui_input multiplies by the wheel factor BEFORE flooring at Step, not
            // after: change = base * factor; scroll(MAX(change, step)). Flooring first (the previous
            // shape here) over-scrolls whenever Step exceeds the per-notch page fraction and the event
            // reports more than one notch (factor > 1).
            var change = Math.Max(GetWheelScrollBase() * Math.Abs(factor), Step);
            ScrollBy(delta > 0 ? -change : change);
            return true;
        }
        internal bool PanGesture(Vector2 delta)
        {
            if (delta == Vector2.Zero) return false;
            if (Orientation == Orientation.Horizontal)
            {
                var amount = delta.X != 0 ? delta.X : delta.Y;
                if (amount == 0) return false;
                ScrollBy(MathF.Sign(amount) * Math.Max(MathF.Abs(amount), Step));
                return true;
            }
            if (delta.Y == 0) return false;
            ScrollBy(MathF.Sign(delta.Y) * Math.Max(MathF.Abs(delta.Y), Step));
            return true;
        }
        internal override void KeyPressed(Keys key)
        {
            var step = GetEffectiveStep();
            if (key == Keys.Home) ScrollTo(MinValue);
            else if (key == Keys.End) ScrollTo(MaxValue);
            else if (Orientation == Orientation.Horizontal && key == Keys.Left) ScrollTo(Value - step);
            else if (Orientation == Orientation.Horizontal && key == Keys.Right) ScrollTo(Value + step);
            else if (Orientation == Orientation.Vertical && key == Keys.Up) ScrollTo(Value - step);
            else if (Orientation == Orientation.Vertical && key == Keys.Down) ScrollTo(Value + step);
            else base.KeyPressed(key);
        }
        internal override bool HitTestBeforeChildren(Point point) => ContainsPoint(point);
        private void UpdateHighlight(Point point)
        {
            var local = ToLocalMainAxis(point);
            var button = GetButtonSize();
            var length = GetMainLength();
            _highlight = local < button ? HighlightRegion.Decrement : local > length - button ? HighlightRegion.Increment : HighlightRegion.Range;
        }
        private void ScrollBy(float amount) => ScrollTo(Value + amount);
        private void ScrollTo(float position)
        {
            var previous = Value;
            Value = position;
            EmitScrolling(previous);
        }
        private void ScrollToRatio(float ratio)
        {
            var previous = Value;
            Ratio = ratio;
            EmitScrolling(previous);
        }
        private void EmitScrolling(float previous)
        {
            if (previous != Value) Scrolling?.Invoke(this, EventArgs.Empty);
        }
        private void PageBy(int direction)
        {
            var change = GetPageScrollAmount();
            var source = _smoothScrolling ? _targetScroll : Value;
            var target = MathHelper.Clamp(source + direction * change, MinValue, MaxValue - Page);
            if (SmoothScrollEnabled)
            {
                _targetScroll = target;
                _smoothScrolling = true;
            }
            else
            {
                ScrollTo(target);
            }
        }
        private void ProcessSmoothScroll(GameTime gameTime)
        {
            if (!_smoothScrolling) return;
            if (Value == _targetScroll)
            {
                _smoothScrolling = false;
                return;
            }
            var target = _targetScroll - Value;
            var distance = MathF.Abs(target);
            var velocity = MathF.Sign(target) * SmoothScrollSpeed * (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (MathF.Abs(velocity) >= distance)
            {
                ScrollTo(_targetScroll);
                _smoothScrolling = false;
            }
            else
            {
                ScrollBy(velocity);
            }
        }
        private void ProcessDragNode(GameTime gameTime)
        {
            if (!_dragNodeTouching) return;
            var delta = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (delta <= 0) return;
            if (_dragNodeDecelerating)
            {
                var position = new Vector2(Orientation == Orientation.Horizontal ? Value : 0, Orientation == Orientation.Vertical ? Value : 0);
                position += _dragNodeSpeed * delta;
                var turnOff = false;
                if (Orientation == Orientation.Horizontal)
                {
                    if (position.X < MinValue) { position.X = MinValue; turnOff = true; }
                    if (position.X > MaxValue - Page) { position.X = MaxValue - Page; turnOff = true; }
                    ScrollTo(position.X);
                    var speed = Decelerate(_dragNodeSpeed.X, delta, out var stopped);
                    _dragNodeSpeed = new Vector2(speed, _dragNodeSpeed.Y);
                    turnOff |= stopped;
                }
                else
                {
                    if (position.Y < MinValue) { position.Y = MinValue; turnOff = true; }
                    if (position.Y > MaxValue - Page) { position.Y = MaxValue - Page; turnOff = true; }
                    ScrollTo(position.Y);
                    var speed = Decelerate(_dragNodeSpeed.Y, delta, out var stopped);
                    _dragNodeSpeed = new Vector2(_dragNodeSpeed.X, speed);
                    turnOff |= stopped;
                }
                if (turnOff)
                {
                    _dragNodeTouching = false;
                    _dragNodeDecelerating = false;
                }
                return;
            }

            if (_timeSinceDragNodeMotion == 0 || _timeSinceDragNodeMotion > 0.1f)
            {
                var diff = _dragNodeAccum - _lastDragNodeAccum;
                _lastDragNodeAccum = _dragNodeAccum;
                _dragNodeSpeed = diff / delta;
            }
            _timeSinceDragNodeMotion += delta;
        }
        private static float Decelerate(float speed, float delta, out bool stopped)
        {
            var sign = speed < 0 ? -1 : 1;
            var value = MathF.Abs(speed) - DragNodeDeceleration * delta;
            stopped = value < 0;
            return sign * value;
        }
        private float GetEffectiveStep()
        {
            if (CustomStep >= 0) return CustomStep;
            return Step > 0 ? Step : GetWheelScrollBase();
        }
        private float GetPageScrollAmount() => Page != 0 ? Page : (MaxValue - MinValue) / FallbackPageDivisor;
        private float GetWheelScrollBase() => Page != 0 ? Page / PageDivisor : (MaxValue - MinValue) / FallbackPageDivisor;
        private int GetButtonSize()
        {
            if (!ShowStepButtons) return 0;
            var cross = Orientation == Orientation.Horizontal ? Bounds.Height : Bounds.Width;
            return Math.Max(0, Math.Min(14, Math.Max(0, cross)));
        }
        private float GetMainLength() => Math.Max(0, Orientation == Orientation.Horizontal ? Bounds.Width : Bounds.Height);
        private float GetAreaSize()
        {
            var area = GetMainLength() - GetButtonSize() * 2 - MinimumGrabberSize;
            return Math.Max(0, area);
        }
        private float GetGrabberSize()
        {
            var range = MaxValue - MinValue;
            if (range <= 0) return 0;
            var page = Page > 0 ? Page : 0;
            return page / range * GetAreaSize() + MinimumGrabberSize;
        }
        private float GetGrabberOffset() => GetAreaSize() * Ratio;
        private float ToLocalMainAxis(Point point) => Orientation == Orientation.Horizontal ? point.X - Bounds.Left : point.Y - Bounds.Top;
        private Rectangle ToGlobal(Rectangle local) => new Rectangle(Bounds.X + local.X, Bounds.Y + local.Y, local.Width, local.Height);
    }
    /// <summary>Scrolls a range along a horizontal track.</summary>
    public sealed class HScrollBar : ScrollBar { public HScrollBar() : base(Orientation.Horizontal) { } }
    /// <summary>Scrolls a range along a vertical track.</summary>
    public sealed class VScrollBar : ScrollBar { public VScrollBar() : base(Orientation.Vertical) { } }

    public enum TextureProgressFillMode
    {
        LeftToRight, RightToLeft, TopToBottom, BottomToTop,
        Clockwise, CounterClockwise, BilinearLeftAndRight, BilinearTopAndBottom, ClockwiseAndCounterClockwise
    }

    /// <summary>Local source/destination region used by a non-radial TextureProgressBar fill mode.</summary>
    public readonly struct TextureProgressRegion
    {
        public TextureProgressRegion(Rectangle destination, Rectangle source) { Destination = destination; Source = source; }
        public Rectangle Destination { get; }
        public Rectangle Source { get; }
    }

    /// <summary>Adjusted rectangles and effective margins for a stretched TextureProgressBar layer.</summary>
    public readonly struct TextureProgressNinePatchRegion
    {
        public TextureProgressNinePatchRegion(Rectangle destination, Rectangle source, Thickness margins) { Destination = destination; Source = source; Margins = margins; }
        public Rectangle Destination { get; }
        public Rectangle Source { get; }
        public Thickness Margins { get; }
    }

    /// <summary>Visualizes progress with layered textures using linear, radial, bilinear, or nine-patch fill behavior.</summary>
    public sealed class TextureProgressBar : Range
    {
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.ProgressBar;
        public override AccessibilityActions AccessibilityActions => base.AccessibilityActions &
            ~(AccessibilityActions.Increment | AccessibilityActions.Decrement | AccessibilityActions.SetValue);
        // Godot's TextureProgressBar::TextureProgressBar() calls set_mouse_filter(MOUSE_FILTER_PASS),
        // the same as TextureRect/NinePatchRect - a decorative progress bar shouldn't swallow pointer
        // events meant for whatever is behind or around it.
        public TextureProgressBar() { MouseFilter = MouseFilter.Pass; }
        public Texture2D Under { get; set; }
        public Texture2D Progress { get; set; }
        public Texture2D Over { get; set; }
        public TextureProgressFillMode FillMode { get; set; }
        public Vector2 ProgressOffset { get; set; }
        public float RadialInitialAngle { get; set; }
        public float RadialFillDegrees { get; set; } = 360;
        public Vector2 RadialCenterOffset { get; set; }
        public bool NinePatchStretch { get; set; }
        public Thickness StretchMargins { get; set; }
        public Color TintUnder { get; set; } = Color.White;
        public Color TintProgress { get; set; } = Color.White;
        public Color TintOver { get; set; } = Color.White;
        public void SetUnderTexture(Texture2D texture) { Under = texture; QueueLayout(); }
        public Texture2D GetUnderTexture() => Under;
        public void SetProgressTexture(Texture2D texture) { Progress = texture; QueueLayout(); }
        public Texture2D GetProgressTexture() => Progress;
        public void SetOverTexture(Texture2D texture) { Over = texture; QueueLayout(); }
        public Texture2D GetOverTexture() => Over;
        public void SetFillMode(TextureProgressFillMode mode) { if (!Enum.IsDefined(typeof(TextureProgressFillMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode)); FillMode = mode; }
        public TextureProgressFillMode GetFillMode() => FillMode;
        public void SetTintUnder(Color color) => TintUnder = color;
        public Color GetTintUnder() => TintUnder;
        public void SetTintProgress(Color color) => TintProgress = color;
        public Color GetTintProgress() => TintProgress;
        public void SetTintOver(Color color) => TintOver = color;
        public Color GetTintOver() => TintOver;
        public void SetTextureProgressOffset(Vector2 offset) => ProgressOffset = offset;
        public Vector2 GetTextureProgressOffset() => ProgressOffset;
        // Godot's set_radial_initial_angle wraps an out-of-[0,360] angle via fposmodp (always a
        // non-negative result), so the stored/returned value is always normalized, not just its
        // transient use inside fill-polygon math.
        public void SetRadialInitialAngle(float angle) => RadialInitialAngle = angle is < 0 or > 360 ? PositiveModulo(angle, 360) : angle;
        public float GetRadialInitialAngle() => RadialInitialAngle;
        // Godot's set_fill_degrees clamps into [0,360], so the stored/returned value is always normalized.
        public void SetRadialFillDegrees(float degrees) => RadialFillDegrees = MathHelper.Clamp(degrees, 0, 360);
        public float GetRadialFillDegrees() => RadialFillDegrees;
        public void SetRadialCenterOffset(Vector2 offset) => RadialCenterOffset = offset;
        public Vector2 GetRadialCenterOffset() => RadialCenterOffset;
        public void SetNinePatchStretch(bool stretch) { NinePatchStretch = stretch; QueueLayout(); }
        public bool IsNinePatchStretchEnabled() => NinePatchStretch;
        public void SetStretchMargin(Side side, float value)
        {
            if (!Enum.IsDefined(typeof(Side), side)) throw new ArgumentOutOfRangeException(nameof(side));
            StretchMargins = side == Side.Left ? new Thickness(value, StretchMargins.Top, StretchMargins.Right, StretchMargins.Bottom)
                : side == Side.Top ? new Thickness(StretchMargins.Left, value, StretchMargins.Right, StretchMargins.Bottom)
                : side == Side.Right ? new Thickness(StretchMargins.Left, StretchMargins.Top, value, StretchMargins.Bottom)
                : new Thickness(StretchMargins.Left, StretchMargins.Top, StretchMargins.Right, value);
            QueueLayout();
        }
        public float GetStretchMargin(Side side)
        {
            if (!Enum.IsDefined(typeof(Side), side)) throw new ArgumentOutOfRangeException(nameof(side));
            return side == Side.Left ? StretchMargins.Left : side == Side.Top ? StretchMargins.Top : side == Side.Right ? StretchMargins.Right : StretchMargins.Bottom;
        }
        public override Vector2 GetMinimumSize()
        {
            // Matches Godot's TextureProgressBar::get_minimum_size exactly: nine-patch-stretch mode
            // reports only the stretch margins (the texture's own size never matters once it stretches),
            // and the non-stretched fallback is the largest of ALL THREE textures maxed against (1,1),
            // not just Under with an arbitrary (48,20) placeholder.
            if (NinePatchStretch) return Vector2.Max(CustomMinimumSize, new Vector2(StretchMargins.Horizontal, StretchMargins.Vertical));
            var size = Vector2.One;
            if (Under != null) size = Vector2.Max(size, new Vector2(Under.Width, Under.Height));
            if (Progress != null) size = Vector2.Max(size, new Vector2(Progress.Width, Progress.Height));
            if (Over != null) size = Vector2.Max(size, new Vector2(Over.Width, Over.Height));
            return Vector2.Max(CustomMinimumSize, size);
        }
        /// <summary>Returns the local rectangular progress region for linear and bilinear modes.</summary>
        public TextureProgressRegion GetProgressRegion(Vector2 textureSize)
        {
            var sourceWidth = Math.Max(0, (int)MathF.Round(textureSize.X));
            var sourceHeight = Math.Max(0, (int)MathF.Round(textureSize.Y));
            var displaySize = NinePatchStretch ? Size : textureSize;
            var displayWidth = Math.Max(0, (int)MathF.Round(displaySize.X));
            var displayHeight = Math.Max(0, (int)MathF.Round(displaySize.Y));
            var ratio = MathHelper.Clamp(Ratio, 0, 1);
            var source = new Rectangle(0, 0, sourceWidth, sourceHeight);
            var destination = new Rectangle((int)MathF.Round(ProgressOffset.X), (int)MathF.Round(ProgressOffset.Y), displayWidth, displayHeight);
            var sourceFillWidth = (int)MathF.Round(sourceWidth * ratio); var sourceFillHeight = (int)MathF.Round(sourceHeight * ratio);
            var displayFillWidth = (int)MathF.Round(displayWidth * ratio); var displayFillHeight = (int)MathF.Round(displayHeight * ratio);
            switch (FillMode)
            {
                case TextureProgressFillMode.RightToLeft:
                    source.X = sourceWidth - sourceFillWidth; source.Width = sourceFillWidth;
                    destination.X += displayWidth - displayFillWidth; destination.Width = displayFillWidth; break;
                case TextureProgressFillMode.TopToBottom:
                    source.Height = sourceFillHeight; destination.Height = displayFillHeight; break;
                case TextureProgressFillMode.BottomToTop:
                    source.Y = sourceHeight - sourceFillHeight; source.Height = sourceFillHeight;
                    destination.Y += displayHeight - displayFillHeight; destination.Height = displayFillHeight; break;
                case TextureProgressFillMode.BilinearLeftAndRight:
                    source.X = (sourceWidth - sourceFillWidth) / 2; source.Width = sourceFillWidth;
                    destination.X += (displayWidth - displayFillWidth) / 2; destination.Width = displayFillWidth; break;
                case TextureProgressFillMode.BilinearTopAndBottom:
                    source.Y = (sourceHeight - sourceFillHeight) / 2; source.Height = sourceFillHeight;
                    destination.Y += (displayHeight - displayFillHeight) / 2; destination.Height = displayFillHeight; break;
                default:
                    source.Width = sourceFillWidth; destination.Width = displayFillWidth; break;
            }
            return new TextureProgressRegion(destination, source);
        }
        /// <summary>Returns Godot's adjusted partial-fill nine-patch geometry for the progress texture.</summary>
        public TextureProgressNinePatchRegion GetNinePatchProgressRegion(Vector2 textureSize)
        {
            return GetNinePatchRegion(textureSize, FillMode, MathHelper.Clamp(Ratio, 0, 1), ProgressOffset);
        }
        private TextureProgressNinePatchRegion GetNinePatchRegion(Vector2 textureSize, TextureProgressFillMode mode, float ratio, Vector2 offset)
        {
            var source = new Rectangle(0, 0, Math.Max(0, (int)MathF.Round(textureSize.X)), Math.Max(0, (int)MathF.Round(textureSize.Y)));
            var destination = new Rectangle((int)MathF.Round(offset.X), (int)MathF.Round(offset.Y), Math.Max(0, (int)MathF.Round(Size.X)), Math.Max(0, (int)MathF.Round(Size.Y)));
            var left = StretchMargins.Left; var top = StretchMargins.Top; var right = StretchMargins.Right; var bottom = StretchMargins.Bottom;
            if (ratio >= 1 || IsRadial) return new TextureProgressNinePatchRegion(destination, source, new Thickness(left, top, right, bottom));

            var horizontal = mode == TextureProgressFillMode.LeftToRight || mode == TextureProgressFillMode.RightToLeft || mode == TextureProgressFillMode.BilinearLeftAndRight;
            var total = horizontal ? destination.Width : destination.Height;
            var textureLength = horizontal ? source.Width : source.Height;
            var first = mode == TextureProgressFillMode.RightToLeft ? right : mode == TextureProgressFillMode.BottomToTop ? bottom : horizontal ? left : top;
            var last = mode == TextureProgressFillMode.RightToLeft ? left : mode == TextureProgressFillMode.BottomToTop ? top : horizontal ? right : bottom;
            var filled = total * ratio;
            var middle = Math.Max(0, textureLength - first - last);
            var maxMiddleTexture = middle;
            var maxMiddleReal = Math.Max(0, total - first - last);
            var bilinear = mode == TextureProgressFillMode.BilinearLeftAndRight || mode == TextureProgressFillMode.BilinearTopAndBottom;
            if (bilinear)
            {
                last = Math.Max(0, last - (total - filled) * .5f);
                first = Math.Max(0, first - (total - filled) * .5f);
                var realMiddle = filled - first - last;
                middle = maxMiddleReal > 0 ? middle * Math.Min(maxMiddleReal, realMiddle) / maxMiddleReal : 0;
                textureLength = (int)MathF.Round(Math.Min(textureLength, first + middle + last));
            }
            else
            {
                middle *= Math.Min(1, Math.Max(0, filled - first) / Math.Max(1, total - first - last));
                last = Math.Max(0, last - (total - filled));
                first = Math.Min(first, filled);
                textureLength = (int)MathF.Round(Math.Min(textureLength, first + middle + last));
            }

            var fillLength = Math.Max(0, (int)MathF.Round(filled));
            var firstMargin = Math.Max(0, (int)MathF.Round(first));
            var lastMargin = Math.Max(0, (int)MathF.Round(last));
            if (mode == TextureProgressFillMode.RightToLeft)
            {
                source.X = source.Right - textureLength; source.Width = textureLength;
                destination.X += destination.Width - fillLength; destination.Width = fillLength;
                left = lastMargin; right = firstMargin;
            }
            else if (mode == TextureProgressFillMode.BottomToTop)
            {
                source.Y = source.Bottom - textureLength; source.Height = textureLength;
                destination.Y += destination.Height - fillLength; destination.Height = fillLength;
                top = lastMargin; bottom = firstMargin;
            }
            else if (mode == TextureProgressFillMode.BilinearLeftAndRight)
            {
                var center = maxMiddleReal > 0 ? (total * .5f - StretchMargins.Left) / maxMiddleReal * maxMiddleTexture + StretchMargins.Left : source.Width * .5f;
                var drift = StretchMargins.Right != StretchMargins.Left ? (source.Width * .5f - center) * (last - first) / (StretchMargins.Right - StretchMargins.Left) : 0;
                source.X += (int)MathF.Round(center + drift - textureLength * .5f); source.Width = textureLength;
                destination.X += (destination.Width - fillLength) / 2; destination.Width = fillLength;
                left = firstMargin; right = lastMargin;
            }
            else if (mode == TextureProgressFillMode.BilinearTopAndBottom)
            {
                var center = maxMiddleReal > 0 ? (total * .5f - StretchMargins.Top) / maxMiddleReal * maxMiddleTexture + StretchMargins.Top : source.Height * .5f;
                var drift = StretchMargins.Bottom != StretchMargins.Top ? (source.Height * .5f - center) * (last - first) / (StretchMargins.Bottom - StretchMargins.Top) : 0;
                source.Y += (int)MathF.Round(center + drift - textureLength * .5f); source.Height = textureLength;
                destination.Y += (destination.Height - fillLength) / 2; destination.Height = fillLength;
                top = firstMargin; bottom = lastMargin;
            }
            else if (mode == TextureProgressFillMode.TopToBottom)
            {
                source.Height = textureLength; destination.Height = fillLength; top = firstMargin; bottom = lastMargin;
            }
            else
            {
                source.Width = textureLength; destination.Width = fillLength; left = firstMargin; right = lastMargin;
            }
            return new TextureProgressNinePatchRegion(destination, source, new Thickness(left, top, right, bottom));
        }
        /// <summary>Returns local boundary points for Godot's radial fill modes, ordered around the clipped sector.</summary>
        public IReadOnlyList<Vector2> GetRadialFillPolygon(Vector2 textureSize)
        {
            var result = new List<Vector2>();
            if (!IsRadial || textureSize.X <= 0 || textureSize.Y <= 0) return result;
            var displayed = NinePatchStretch ? Size : textureSize;
            var amount = MathHelper.Clamp(Ratio, 0, 1) * MathHelper.Clamp(RadialFillDegrees, 0, 360) / 360f;
            if (amount <= 0) return result;
            var start = NormalizeTurns(RadialInitialAngle / 360f);
            var direction = FillMode == TextureProgressFillMode.CounterClockwise ? -1f : 1f;
            if (FillMode == TextureProgressFillMode.ClockwiseAndCounterClockwise) start -= amount / 2;
            var end = start + direction * amount;
            var from = Math.Min(start, end);
            var to = Math.Max(start, end);
            // Godot's exact corner lattice: the true 45/135/225/315-degree turns
            // (floor(from*4+0.5)*0.25+0.125), not the edge midpoints this port previously stepped
            // through - matches TextureProgressBar's radial fan-triangulation corner insertion exactly.
            var turns = new List<float> { from };
            for (var corner = MathF.Floor(from * 4 + 0.5f) * 0.25f + 0.125f; corner < to; corner += .25f) turns.Add(corner);
            turns.Add(to);
            // Godot's unit_val_to_uv clips against a literal UNIT SQUARE using a center normalized into
            // the TEXTURE's own [0,1] space, then only afterward rescales the resulting UV per-axis by
            // the (possibly differently-shaped) display size - collapsing this into a single clip
            // directly against the display rect (as this port previously did) only agrees with Godot
            // when the display and texture share the same aspect ratio.
            var seenUvs = new List<Vector2>();
            foreach (var turn in turns)
            {
                var uv = UnitValToUv(turn, textureSize);
                if (seenUvs.Contains(uv)) continue;
                seenUvs.Add(uv);
                result.Add(new Vector2(uv.X * displayed.X, uv.Y * displayed.Y));
            }
            return result;
        }
        internal void DrawTemplate(UIRenderContext context)
        {
            // Godot's NOTIFICATION_DRAW only ever draws a layer when its texture is actually valid -
            // there is no fallback fill anywhere; an untextured TextureProgressBar renders fully
            // transparent, letting whatever is behind it show through.
            DrawLayer(context, Under, TintUnder, Vector2.Zero);
            if (Progress != null)
            {
                if (IsRadial) DrawRadialProgress(context);
                else if (NinePatchStretch) DrawNinePatch(context, Progress, TintProgress, GetNinePatchProgressRegion(new Vector2(Progress.Width, Progress.Height)));
                else
                {
                    var region = GetProgressRegion(new Vector2(Progress.Width, Progress.Height));
                    if (region.Destination.Width > 0 && region.Destination.Height > 0 && region.Source.Width > 0 && region.Source.Height > 0)
                        context.SpriteBatch.Draw(Progress, Offset(region.Destination), region.Source, TintProgress);
                }
            }
            DrawLayer(context, Over, TintOver, Vector2.Zero);
        }
        private bool IsRadial => FillMode == TextureProgressFillMode.Clockwise || FillMode == TextureProgressFillMode.CounterClockwise || FillMode == TextureProgressFillMode.ClockwiseAndCounterClockwise;
        private void DrawLayer(UIRenderContext context, Texture2D texture, Color tint, Vector2 offset)
        {
            if (texture == null) return;
            if (NinePatchStretch)
            {
                DrawNinePatch(context, texture, tint, GetNinePatchRegion(new Vector2(texture.Width, texture.Height), FillMode, 1, offset));
                return;
            }
            var size = new Vector2(texture.Width, texture.Height);
            context.SpriteBatch.Draw(texture, new Rectangle(Bounds.X + (int)offset.X, Bounds.Y + (int)offset.Y, Math.Max(0, (int)size.X), Math.Max(0, (int)size.Y)), tint);
        }
        private void DrawNinePatch(UIRenderContext context, Texture2D texture, Color tint, TextureProgressNinePatchRegion plan)
        {
            var source = plan.Source; var destination = Offset(plan.Destination); var margins = plan.Margins;
            var sourceLeft = Math.Min(source.Width, Math.Max(0, (int)margins.Left));
            var sourceTop = Math.Min(source.Height, Math.Max(0, (int)margins.Top));
            var sourceRight = Math.Min(source.Width - sourceLeft, Math.Max(0, (int)margins.Right));
            var sourceBottom = Math.Min(source.Height - sourceTop, Math.Max(0, (int)margins.Bottom));
            var destinationLeft = Math.Min(destination.Width, sourceLeft); var destinationTop = Math.Min(destination.Height, sourceTop);
            var destinationRight = Math.Min(destination.Width - destinationLeft, sourceRight); var destinationBottom = Math.Min(destination.Height - destinationTop, sourceBottom);
            var sourceXs = new[] { source.X, source.X + sourceLeft, source.Right - sourceRight, source.Right };
            var sourceYs = new[] { source.Y, source.Y + sourceTop, source.Bottom - sourceBottom, source.Bottom };
            var destinationXs = new[] { destination.X, destination.X + destinationLeft, destination.Right - destinationRight, destination.Right };
            var destinationYs = new[] { destination.Y, destination.Y + destinationTop, destination.Bottom - destinationBottom, destination.Bottom };
            for (var x = 0; x < 3; x++) for (var y = 0; y < 3; y++)
            {
                var src = new Rectangle(sourceXs[x], sourceYs[y], sourceXs[x + 1] - sourceXs[x], sourceYs[y + 1] - sourceYs[y]);
                var dst = new Rectangle(destinationXs[x], destinationYs[y], destinationXs[x + 1] - destinationXs[x], destinationYs[y + 1] - destinationYs[y]);
                if (src.Width > 0 && src.Height > 0 && dst.Width > 0 && dst.Height > 0) context.SpriteBatch.Draw(texture, dst, src, tint);
            }
        }
        private void DrawRadialProgress(UIRenderContext context)
        {
            var textureSize = new Vector2(Progress.Width, Progress.Height);
            var displaySize = NinePatchStretch ? Size : textureSize;
            var polygon = GetRadialFillPolygon(textureSize);
            if (polygon.Count < 2) return;
            var center = GetRadialCenter(textureSize, displaySize) + ProgressOffset;
            var worldBoundary = new List<Vector2>(polygon.Count);
            var uvs = new List<Vector2>(polygon.Count);
            foreach (var point in polygon)
            {
                worldBoundary.Add(GlobalPosition + point + ProgressOffset);
                uvs.Add(new Vector2(MathHelper.Clamp(point.X / Math.Max(1, displaySize.X), 0, 1), MathHelper.Clamp(point.Y / Math.Max(1, displaySize.Y), 0, 1)));
            }
            context.TexturedFan(Progress, GlobalPosition + center, new Vector2(center.X / Math.Max(1, displaySize.X), center.Y / Math.Max(1, displaySize.Y)), worldBoundary, uvs, TintProgress);
        }
        /// <summary>The radial center normalized into the TEXTURE's own [0,1] UV space, matching Godot's
        /// get_relative_center exactly (the offset and clamp both happen in texture-native units, before
        /// any rescale to a possibly differently-shaped display size).</summary>
        private Vector2 GetRelativeCenterUv(Vector2 textureSize)
        {
            if (textureSize.X <= 0 || textureSize.Y <= 0) return Vector2.Zero;
            var center = textureSize / 2 + RadialCenterOffset;
            center.X /= textureSize.X; center.Y /= textureSize.Y;
            return Vector2.Clamp(center, Vector2.Zero, Vector2.One);
        }
        private Vector2 GetRadialCenter(Vector2 textureSize, Vector2 displaySize)
        {
            var uv = GetRelativeCenterUv(textureSize);
            return new Vector2(uv.X * displaySize.X, uv.Y * displaySize.Y);
        }
        /// <summary>Matches Godot's TextureProgressBar::unit_val_to_uv exactly: a minimal Liang-Barsky
        /// clip of a ray from the texture-native center against the literal unit square [0,1]x[0,1],
        /// returning a UV - NOT a display-space point. The per-edge dir mutation is intentional and
        /// carries over between iterations, exactly like Godot's own "minimal version" of the algorithm.</summary>
        private Vector2 UnitValToUv(float val, Vector2 textureSize)
        {
            if (textureSize.X <= 0 || textureSize.Y <= 0) return Vector2.Zero;
            if (val < 0) val += 1;
            if (val > 1) val -= 1;
            var p = GetRelativeCenterUv(textureSize);
            var angle = val * MathHelper.TwoPi - MathHelper.PiOver2;
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var t1 = 1f;
            for (var edge = 0; edge < 4; edge++)
            {
                float cq, cp;
                if (edge == 0)
                {
                    if (dir.X > 0) continue;
                    cq = -(0f - p.X);
                    dir.X *= 2f * cq;
                    cp = -dir.X;
                }
                else if (edge == 1)
                {
                    if (dir.X < 0) continue;
                    cq = 1f - p.X;
                    dir.X *= 2f * cq;
                    cp = dir.X;
                }
                else if (edge == 2)
                {
                    if (dir.Y > 0) continue;
                    cq = -(0f - p.Y);
                    dir.Y *= 2f * cq;
                    cp = -dir.Y;
                }
                else
                {
                    if (dir.Y < 0) continue;
                    cq = 1f - p.Y;
                    dir.Y *= 2f * cq;
                    cp = dir.Y;
                }
                var cr = cq / cp;
                if (cr >= 0 && cr < t1) t1 = cr;
            }
            return p + t1 * dir;
        }
        private static float NormalizeTurns(float turns) => turns - MathF.Floor(turns);
        private static float PositiveModulo(float value, float modulus) { var remainder = value % modulus; return remainder < 0 ? remainder + modulus : remainder; }
        private Rectangle Offset(Rectangle rectangle) => new Rectangle(Bounds.X + rectangle.X, Bounds.Y + rectangle.Y, rectangle.Width, rectangle.Height);
    }

    public enum TabCloseDisplayPolicy { Never, ActiveOnly, Always }
    public enum TabBarAlignment { Left, Center, Right }
    public enum TabBarSizingMode { FitContent, Uniform, Justify, Expand }
    public sealed class TabBarItem
    {
        internal TabBarItem(string title, Texture2D icon) { Title = title ?? string.Empty; Icon = icon; }
        public string Title { get; internal set; }
        public Texture2D Icon { get; internal set; }
        public Texture2D ButtonIcon { get; internal set; }
        public string Tooltip { get; internal set; } = string.Empty;
        public TextDirection TextDirection { get; internal set; } = TextDirection.Inherited;
        public string Language { get; internal set; } = string.Empty;
        public bool Disabled { get; internal set; }
        public bool Hidden { get; internal set; }
        public object Metadata { get; internal set; }
    }

    /// <summary>Godot-style tab strip with per-tab state, tooltips, metadata and optional close buttons.</summary>
    public sealed class TabBar : TemplatedControl
    {
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.TabList;
        private readonly UIFontSelection _fontSelection = new UIFontSelection();
        private const int OffsetButtonWidth = 16;
        private readonly List<TabBarItem> _tabs = new List<TabBarItem>();
        private int _currentTab = -1;
        private int _previousTab = -1;
        private int _draggedTab = -1;
        private int _tabOffset;
        private int _hoveredTab = -1;
        private bool _deselectEnabled;
        public IReadOnlyList<TabBarItem> TabItems => _tabs;
        public IReadOnlyList<string> Tabs { get { var result = new List<string>(_tabs.Count); foreach (var tab in _tabs) result.Add(tab.Title); return result; } }
        public SpriteFont Font { get => _fontSelection.SpriteFont; set { _fontSelection.SetSpriteFont(value); QueueLayout(); } }
        public UIFont UIFont { get => _fontSelection.UIFont; set { _fontSelection.SetUIFont(value); QueueLayout(); } }
        internal UIFont EffectiveUIFont => ResolveFont(_fontSelection);
        public TabCloseDisplayPolicy CloseDisplayPolicy { get; set; }
        public TabBarAlignment TabAlignment { get; set; }
        public TabBarSizingMode TabSizing { get; set; } = TabBarSizingMode.FitContent;
        /// <summary>Clips overflowed fit-content tabs to the strip bounds, matching Godot's clip_tabs behavior.</summary>
        public bool ClipTabs { get; set; } = true;
        /// <summary>When overflow is clipped, keeps the selected tab in the visible portion of the strip.</summary>
        public bool ScrollToSelected { get; set; } = true;
        /// <summary>Enables overflow navigation controls, matching Godot's scrolling_enabled property.</summary>
        public bool ScrollingEnabled { get; set; } = true;
        /// <summary>The first tab index displayed when the strip overflows, matching Godot's tab_offset property.</summary>
        public int TabOffset
        {
            get => _tabOffset;
            set
            {
                GetTab(value);
                _tabOffset = value;
                QueueLayout();
            }
        }
        /// <summary>Whether the tab content currently exceeds the available strip width.</summary>
        public bool OffsetButtonsVisible => ClipTabs && ScrollingEnabled && GetUnclippedTabWidth() > Bounds.Width;
        /// <summary>Optional maximum width for an individual tab; zero leaves titles unconstrained.</summary>
        public int MaxTabWidth { get; set; }
        /// <summary>Enables pointer drag reordering, matching Godot's drag_to_rearrange_enabled property.</summary>
        public bool DragToRearrangeEnabled { get; set; }
        /// <summary>Optional group identifier reserved for compatibility with Godot tab-strip rearrangement groups.</summary>
        public int TabsRearrangeGroup { get; set; } = -1;
        /// <summary>Retained drag-hover switching policy, matching Godot's switch_on_drag_hover property.</summary>
        public bool SwitchOnDragHover { get; set; } = true;
        /// <summary>Allows right-clicking a tab to select it, matching Godot's select_with_rmb property.</summary>
        public bool SelectWithRightMouseButton { get; set; }
        /// <summary>Allows the current tab to become -1, matching Godot's deselect_enabled property.</summary>
        public bool DeselectEnabled
        {
            get => _deselectEnabled;
            set
            {
                if (_deselectEnabled == value) return;
                _deselectEnabled = value;
                if (!_deselectEnabled && _currentTab == -1) EnsureValidCurrent();
            }
        }
        /// <summary>Emits the close signal when a tab is middle-clicked, matching Godot's close_with_middle_mouse property.</summary>
        public bool CloseWithMiddleMouse { get; set; } = true;
        public int TabCount => _tabs.Count;
        public int CurrentTab
        {
            get => _currentTab;
            set
            {
                if (_tabs.Count == 0) { _currentTab = -1; return; }
                if (value == -1)
                {
                    if (!DeselectEnabled) throw new InvalidOperationException("Cannot deselect tabs when deselection is disabled.");
                    _previousTab = _currentTab;
                    if (_currentTab == -1) { TabSelected?.Invoke(this, -1); return; }
                    _currentTab = -1;
                    TabSelected?.Invoke(this, -1); TabChanged?.Invoke(this, -1);
                    return;
                }
                value = MathHelper.Clamp(value, 0, _tabs.Count - 1);
                // Godot's set_current_tab has no disabled/hidden guard at all - only the mouse-click
                // path blocks selecting a disabled tab; programmatic selection can pick any valid index.
                _previousTab = _currentTab;
                if (_currentTab == value) { TabSelected?.Invoke(this, value); return; }
                _currentTab = value;
                if (ScrollToSelected) EnsureTabVisible(value);
                TabSelected?.Invoke(this, value); TabChanged?.Invoke(this, value);
            }
        }
        public event Action<TabBar, int> TabSelected;
        public event Action<TabBar, int> TabChanged;
        public event Action<TabBar, int> TabClicked;
        public event Action<TabBar, int> TabRightClicked;
        public event Action<TabBar, int> TabClosePressed;
        public event Action<TabBar, int> TabButtonPressed;
        public event Action<TabBar, int> TabHovered;
        /// <summary>Raised while a dragged active tab is moved to a new index.</summary>
        public event Action<TabBar, int> ActiveTabRearranged;
        public void SetTabCount(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            while (_tabs.Count < count) AddTab();
            while (_tabs.Count > count) RemoveTab(_tabs.Count - 1);
        }
        public int GetTabCount() => TabCount;
        public int AddTab(string title = "", Texture2D icon = null)
        {
            _tabs.Add(new TabBarItem(title, icon));
            if (ScrollToSelected && CurrentTab >= 0) EnsureTabVisible(CurrentTab);
            QueueLayout();
            // Godot's add_tab only auto-selects the very first tab ever added, and only when
            // deselection is disabled - never on subsequent tabs, unlike a bare "if nothing selected" check.
            if (!DeselectEnabled && _tabs.Count == 1)
            {
                if (Context != null) CurrentTab = 0;
                else { _currentTab = 0; _previousTab = -1; }
            }
            return _tabs.Count - 1;
        }
        public void ClearTabs() { _tabs.Clear(); _currentTab = -1; _tabOffset = 0; QueueLayout(); }
        public void SetCurrentTab(int tab) => CurrentTab = tab;
        public int GetCurrentTab() => CurrentTab;
        /// <summary>The tab selected immediately before the current one, matching Godot's previous property/get_previous_tab.</summary>
        public int PreviousTab => _previousTab;
        public int GetPreviousTab() => PreviousTab;
        public string GetTabTitle(int tab) => GetTab(tab).Title;
        public void SetTabTitle(int tab, string title) { GetTab(tab).Title = title ?? string.Empty; if (ScrollToSelected && CurrentTab >= 0) EnsureTabVisible(CurrentTab); QueueLayout(); }
        public Texture2D GetTabIcon(int tab) => GetTab(tab).Icon;
        public void SetTabIcon(int tab, Texture2D icon) { GetTab(tab).Icon = icon; if (ScrollToSelected && CurrentTab >= 0) EnsureTabVisible(CurrentTab); QueueLayout(); }
        public Texture2D GetTabButtonIcon(int tab) => GetTab(tab).ButtonIcon;
        public void SetTabButtonIcon(int tab, Texture2D icon) { GetTab(tab).ButtonIcon = icon; if (ScrollToSelected && CurrentTab >= 0) EnsureTabVisible(CurrentTab); QueueLayout(); }
        public void SetTabTooltip(int tab, string tooltip) => GetTab(tab).Tooltip = tooltip ?? string.Empty;
        public string GetTabTooltip(int tab) => GetTab(tab).Tooltip;
        public void SetTabTextDirection(int tab, TextDirection direction) { if (!Enum.IsDefined(typeof(TextDirection), direction)) throw new ArgumentOutOfRangeException(nameof(direction)); GetTab(tab).TextDirection = direction; QueueLayout(); }
        public TextDirection GetTabTextDirection(int tab) => GetTab(tab).TextDirection;
        public void SetTabLanguage(int tab, string language) { GetTab(tab).Language = language ?? string.Empty; QueueLayout(); }
        public string GetTabLanguage(int tab) => GetTab(tab).Language;
        // Godot's set_tab_disabled/set_tab_hidden never touch `current` at all - a disabled or hidden
        // active tab stays selected until something else (e.g. remove_tab) reconciles it.
        public void SetTabDisabled(int tab, bool disabled) { GetTab(tab).Disabled = disabled; QueueLayout(); }
        public bool IsTabDisabled(int tab) => GetTab(tab).Disabled;
        public void SetTabHidden(int tab, bool hidden) { GetTab(tab).Hidden = hidden; if (ScrollToSelected && CurrentTab >= 0) EnsureTabVisible(CurrentTab); QueueLayout(); }
        public bool IsTabHidden(int tab) => GetTab(tab).Hidden;
        public void SetTabMetadata(int tab, object metadata) => GetTab(tab).Metadata = metadata;
        public object GetTabMetadata(int tab) => GetTab(tab).Metadata;
        public void RemoveTab(int tab)
        {
            GetTab(tab);
            _tabs.RemoveAt(tab);
            // Godot's remove_tab shifts current/previous down by one whenever the removed index was
            // at-or-before them, keeping the same logical tab selected rather than an off-by-one one.
            if (_currentTab >= tab && _currentTab > 0) _currentTab--;
            if (_previousTab >= tab && _previousTab > 0) _previousTab--;
            if (_tabs.Count == 0)
            {
                _tabOffset = 0;
                _currentTab = -1;
                _previousTab = -1;
            }
            else
            {
                if (_currentTab != -1)
                {
                    // Try a valid tab forward from the (already shifted) current index first, then
                    // backward, then deselect entirely - without firing TabSelected, matching Godot exactly.
                    var found = -1;
                    for (var i = _currentTab; i < _tabs.Count; i++) if (!_tabs[i].Disabled && !_tabs[i].Hidden) { found = i; break; }
                    if (found < 0) for (var i = _currentTab - 1; i >= 0; i--) if (!_tabs[i].Disabled && !_tabs[i].Hidden) { found = i; break; }
                    _currentTab = found;
                }
                _tabOffset = Math.Min(_tabOffset, _tabs.Count - 1);
            }
            if (ScrollToSelected && CurrentTab >= 0) EnsureTabVisible(CurrentTab);
            QueueLayout();
        }
        public void SetSelectWithRmb(bool enabled) => SelectWithRightMouseButton = enabled;
        public bool GetSelectWithRmb() => SelectWithRightMouseButton;
        public void SetDeselectEnabled(bool enabled) => DeselectEnabled = enabled;
        public bool GetDeselectEnabled() => DeselectEnabled;
        public void SetCloseWithMiddleMouse(bool enabled) => CloseWithMiddleMouse = enabled;
        public bool GetCloseWithMiddleMouse() => CloseWithMiddleMouse;
        public void SetTabAlignment(TabBarAlignment alignment) { if (!Enum.IsDefined(typeof(TabBarAlignment), alignment)) throw new ArgumentOutOfRangeException(nameof(alignment)); TabAlignment = alignment; QueueLayout(); }
        public TabBarAlignment GetTabAlignment() => TabAlignment;
        public void SetTabSizing(TabBarSizingMode sizing) { if (!Enum.IsDefined(typeof(TabBarSizingMode), sizing)) throw new ArgumentOutOfRangeException(nameof(sizing)); TabSizing = sizing; QueueLayout(); }
        public TabBarSizingMode GetTabSizing() => TabSizing;
        public void SetClipTabs(bool clip) { ClipTabs = clip; QueueLayout(); }
        public bool GetClipTabs() => ClipTabs;
        public int GetTabOffset() => TabOffset;
        public bool GetOffsetButtonsVisible() => OffsetButtonsVisible;
        public void SetTabCloseDisplayPolicy(TabCloseDisplayPolicy policy) { if (!Enum.IsDefined(typeof(TabCloseDisplayPolicy), policy)) throw new ArgumentOutOfRangeException(nameof(policy)); CloseDisplayPolicy = policy; QueueLayout(); }
        public TabCloseDisplayPolicy GetTabCloseDisplayPolicy() => CloseDisplayPolicy;
        public void SetMaxTabWidth(int width) { if (width < 0) throw new ArgumentOutOfRangeException(nameof(width)); MaxTabWidth = width; QueueLayout(); }
        public int GetMaxTabWidth() => MaxTabWidth;
        public void SetScrollingEnabled(bool enabled) { ScrollingEnabled = enabled; QueueLayout(); }
        public bool GetScrollingEnabled() => ScrollingEnabled;
        public void SetDragToRearrangeEnabled(bool enabled) => DragToRearrangeEnabled = enabled;
        public bool GetDragToRearrangeEnabled() => DragToRearrangeEnabled;
        public void SetTabsRearrangeGroup(int groupId) => TabsRearrangeGroup = groupId;
        public int GetTabsRearrangeGroup() => TabsRearrangeGroup;
        public void SetSwitchOnDragHover(bool enabled) => SwitchOnDragHover = enabled;
        public bool GetSwitchOnDragHover() => SwitchOnDragHover;
        public void SetScrollToSelected(bool enabled) => ScrollToSelected = enabled;
        public bool GetScrollToSelected() => ScrollToSelected;
        /// <summary>Moves a tab while preserving the tab that is currently selected.</summary>
        public void MoveTab(int from, int to)
        {
            GetTab(from); GetTab(to);
            if (from == to) return;
            var moved = _tabs[from];
            _tabs.RemoveAt(from);
            _tabs.Insert(to, moved);
            if (_currentTab == from) _currentTab = to;
            else if (_currentTab > from && _currentTab <= to) _currentTab--;
            else if (_currentTab < from && _currentTab >= to) _currentTab++;
            if (_tabOffset == from) _tabOffset = to;
            else if (_tabOffset > from && _tabOffset <= to) _tabOffset--;
            else if (_tabOffset < from && _tabOffset >= to) _tabOffset++;
            if (ScrollToSelected && CurrentTab >= 0) EnsureTabVisible(CurrentTab);
            QueueLayout();
        }
        /// <summary>Scrolls an overflowed strip so that <paramref name="tab"/> intersects its visible bounds.</summary>
        public void EnsureTabVisible(int tab)
        {
            GetTab(tab);
            if (_tabs[tab].Hidden || !OffsetButtonsVisible) return;
            var visible = GetVisibleTabs();
            var target = visible.IndexOf(tab);
            if (target < 0) return;
            var offset = visible.IndexOf(_tabOffset);
            if (offset < 0) offset = 0;
            var widths = GetLayoutWidths(visible, out _);
            if (target < offset) { _tabOffset = tab; QueueLayout(); return; }
            var viewport = GetTabViewport();
            var used = 0;
            for (var index = offset; index <= target; index++) used += widths[index];
            while (used > viewport.Width && offset < target) used -= widths[offset++];
            var desiredOffset = visible[offset];
            if (_tabOffset != desiredOffset) { _tabOffset = desiredOffset; QueueLayout(); }
        }
        /// <summary>Returns the previous visible, enabled tab before <paramref name="tab"/>, or -1 when none exists.</summary>
        public int GetPreviousAvailable(int tab = -1)
        {
            var start = tab == -1 ? CurrentTab : tab;
            if (start < 0 || start >= _tabs.Count) return -1;
            for (var index = start - 1; index >= 0; index--) if (!_tabs[index].Hidden && !_tabs[index].Disabled) return index;
            return -1;
        }
        /// <summary>Returns the next visible, enabled tab after <paramref name="tab"/>, or -1 when none exists.</summary>
        public int GetNextAvailable(int tab = -1)
        {
            var start = tab == -1 ? CurrentTab : tab;
            if (start < 0 || start >= _tabs.Count) return -1;
            for (var index = start + 1; index < _tabs.Count; index++) if (!_tabs[index].Hidden && !_tabs[index].Disabled) return index;
            return -1;
        }
        /// <summary>Selects the preceding visible, enabled tab and reports whether selection changed.</summary>
        public bool SelectPreviousAvailable()
        {
            var previous = GetPreviousAvailable();
            if (previous < 0) return false;
            CurrentTab = previous;
            return true;
        }
        /// <summary>Selects the following visible, enabled tab and reports whether selection changed.</summary>
        public bool SelectNextAvailable()
        {
            var next = GetNextAvailable();
            if (next < 0) return false;
            CurrentTab = next;
            return true;
        }
        public Rectangle GetTabRect(int tab)
        {
            GetTab(tab);
            foreach (var layout in GetTabLayouts()) if (layout.Key == tab) return layout.Value;
            return Rectangle.Empty;
        }
        public override string GetTooltip(Point position) { var index = GetTabAt(position, out _); return index >= 0 && !string.IsNullOrEmpty(_tabs[index].Tooltip) ? _tabs[index].Tooltip : base.GetTooltip(position); }
        internal override void PointerPressed(Point point)
        {
            base.PointerPressed(point);
            UpdateHoveredTab(point);
            if (OffsetButtonsVisible && GetDecrementButtonRect().Contains(point)) { MoveTabOffset(-1); return; }
            if (OffsetButtonsVisible && GetIncrementButtonRect().Contains(point)) { MoveTabOffset(1); return; }
            PressTabAt(point, PointerButton.Left);
        }
        internal override void PointerButtonPressed(Point position, PointerButton button)
        {
            base.PointerButtonPressed(position, button);
            UpdateHoveredTab(position);
            if (button == PointerButton.Right && OffsetButtonsVisible && SelectWithRightMouseButton)
            {
                if (GetDecrementButtonRect().Contains(position)) { MoveTabOffset(-1); return; }
                if (GetIncrementButtonRect().Contains(position)) { MoveTabOffset(1); return; }
            }
            PressTabAt(position, button);
        }
        internal override void PointerMoved(Point point)
        {
            UpdateHoveredTab(point);
            if (_draggedTab < 0) return;
            var target = GetTabAt(point, out _);
            if (target < 0 || target == _draggedTab) return;
            MoveTab(_draggedTab, target);
            _draggedTab = target;
            ActiveTabRearranged?.Invoke(this, target);
        }
        internal override void PointerReleased(Point point, bool isInside) { _draggedTab = -1; }
        internal override void CancelInput() { _draggedTab = -1; base.CancelInput(); }
        /// <summary>Scrolls the tab offset by one tab per wheel tick, matching Godot's TabBar::gui_input WHEEL_UP/WHEEL_DOWN handling.</summary>
        internal override bool PointerWheel(int delta)
        {
            if (delta == 0 || !ScrollingEnabled || !OffsetButtonsVisible) return false;
            var before = _tabOffset;
            if (delta > 0) { if (HasPreviousVisibleTab()) MoveTabOffset(-1); }
            else { if (HasNextVisibleTab()) MoveTabOffset(1); }
            return _tabOffset != before;
        }
        internal override void KeyPressed(Keys key)
        {
            if (key == Keys.Right)
            {
                if (IsLayoutRtl()) SelectPreviousAvailable(); else SelectNextAvailable();
            }
            else if (key == Keys.Left)
            {
                if (IsLayoutRtl()) SelectNextAvailable(); else SelectPreviousAvailable();
            }
            else base.KeyPressed(key);
        }
        internal void DrawTabStrip(UIRenderContext context)
        {
            context.Fill(Bounds, context.Theme.BackgroundColor);
            var viewport = GetTabViewport();
            if (ClipTabs) context.PushClip(viewport);
            try { foreach (var layout in GetTabLayouts())
            {
                var index = layout.Key; var tab = _tabs[index]; var rect = layout.Value;
                context.Fill(rect, index == CurrentTab ? context.Theme.PanelColor : context.Theme.BackgroundColor); context.Border(rect, context.Theme.PanelBorderColor);
                var textX = rect.X + 6;
                if (tab.Icon != null) { var icon = new Rectangle(textX, rect.Y + Math.Max(2, (rect.Height - 16) / 2), 16, 16); context.SpriteBatch.Draw(tab.Icon, icon, Color.White); textX = icon.Right + 4; }
                if (EffectiveUIFont != null) context.Text(EffectiveUIFont, tab.Title, new Vector2(textX, rect.Y + Math.Max(2, (rect.Height - TextMetrics.LineHeight(EffectiveUIFont)) / 2)), tab.Disabled ? context.Theme.DisabledTextColor : context.Theme.TextColor);
                if (tab.ButtonIcon != null) context.SpriteBatch.Draw(tab.ButtonIcon, GetTabButtonRect(index, rect), Color.White);
                if (ShowsClose(index))
                {
                    var close = GetTabCloseRect(index, rect); var closeIcon = GetThemeIcon("close");
                    if (closeIcon.HasValue) context.Icon(closeIcon.Value, close, tab.Disabled ? context.Theme.DisabledTextColor : Color.White);
                    else { context.Fill(new Rectangle(close.X, close.Y + 4, close.Width, 2), context.Theme.TextColor); context.Fill(new Rectangle(close.X + 4, close.Y, 2, close.Height), context.Theme.TextColor); }
                }
            } }
            finally { if (ClipTabs) context.PopClip(); }
            if (OffsetButtonsVisible)
            {
                DrawOffsetButton(context, GetDecrementButtonRect(), false, HasPreviousVisibleTab());
                DrawOffsetButton(context, GetIncrementButtonRect(), true, HasNextVisibleTab());
            }
        }
        private TabBarItem GetTab(int tab) { if (tab < 0 || tab >= _tabs.Count) throw new ArgumentOutOfRangeException(nameof(tab)); return _tabs[tab]; }
        private List<int> GetVisibleTabs() { var visible = new List<int>(); for (var index = 0; index < _tabs.Count; index++) if (!_tabs[index].Hidden) visible.Add(index); return visible; }
        private int GetTabAt(Point point, out Rectangle rect)
        {
            if (!GetTabViewport().Contains(point)) { rect = Rectangle.Empty; return -1; }
            foreach (var layout in GetTabLayouts()) if (layout.Value.Contains(point)) { rect = layout.Value; return layout.Key; }
            rect = Rectangle.Empty; return -1;
        }
        private List<KeyValuePair<int, Rectangle>> GetTabLayouts()
        {
            var visible = GetVisibleTabs(); var layouts = new List<KeyValuePair<int, Rectangle>>(); if (visible.Count == 0) return layouts;
            var widths = GetLayoutWidths(visible, out var total);
            var viewport = GetTabViewport();
            var x = viewport.X;
            if (total < Bounds.Width && TabSizing != TabBarSizingMode.Justify) x += TabAlignment == TabBarAlignment.Center ? (Bounds.Width - total) / 2 : TabAlignment == TabBarAlignment.Right ? Bounds.Width - total : 0;
            else if (ClipTabs && total > Bounds.Width)
            {
                var offset = visible.IndexOf(_tabOffset);
                if (offset < 0) offset = 0;
                for (var i = 0; i < offset; i++) x -= widths[i];
            }
            for (var i = 0; i < visible.Count; i++) { var width = widths[i]; layouts.Add(new KeyValuePair<int, Rectangle>(visible[i], new Rectangle(x, Bounds.Y, width, Bounds.Height))); x += width; }
            return layouts;
        }
        private List<int> GetLayoutWidths(List<int> visible, out int total)
        {
            var widths = new List<int>(); var uniform = 0; total = 0;
            foreach (var tab in visible) { var width = GetDesiredTabWidth(tab); widths.Add(width); total += width; uniform = Math.Max(uniform, width); }
            if (TabSizing == TabBarSizingMode.Uniform) { total = uniform * widths.Count; for (var i = 0; i < widths.Count; i++) widths[i] = uniform; }
            if (TabSizing == TabBarSizingMode.Justify) { var width = Math.Max(1, Bounds.Width / widths.Count); total = width * widths.Count; for (var i = 0; i < widths.Count; i++) widths[i] = width; }
            else if (TabSizing == TabBarSizingMode.Expand && total < Bounds.Width) { var extra = (Bounds.Width - total) / widths.Count; total = 0; for (var i = 0; i < widths.Count; i++) { widths[i] += extra; total += widths[i]; } }
            return widths;
        }
        private int GetUnclippedTabWidth()
        {
            var visible = GetVisibleTabs();
            GetLayoutWidths(visible, out var total);
            return total;
        }
        private Rectangle GetTabViewport()
        {
            if (!OffsetButtonsVisible) return Bounds;
            return new Rectangle(Bounds.X + OffsetButtonWidth, Bounds.Y, Math.Max(0, Bounds.Width - OffsetButtonWidth * 2), Bounds.Height);
        }
        private Rectangle GetDecrementButtonRect() => new Rectangle(Bounds.X, Bounds.Y, OffsetButtonWidth, Bounds.Height);
        private Rectangle GetIncrementButtonRect() => new Rectangle(Bounds.Right - OffsetButtonWidth, Bounds.Y, OffsetButtonWidth, Bounds.Height);
        private bool HasPreviousVisibleTab()
        {
            var visible = GetVisibleTabs();
            return visible.IndexOf(_tabOffset) > 0;
        }
        private bool HasNextVisibleTab()
        {
            var visible = GetVisibleTabs(); var offset = visible.IndexOf(_tabOffset);
            if (offset < 0) offset = 0;
            var widths = GetLayoutWidths(visible, out _); var used = 0;
            for (var index = offset; index < visible.Count; index++) { used += widths[index]; if (used > GetTabViewport().Width) return true; }
            return false;
        }
        private void MoveTabOffset(int direction)
        {
            var visible = GetVisibleTabs(); var offset = visible.IndexOf(_tabOffset);
            if (offset < 0) offset = 0;
            offset = MathHelper.Clamp(offset + direction, 0, Math.Max(0, visible.Count - 1));
            if (visible.Count > 0 && _tabOffset != visible[offset]) { _tabOffset = visible[offset]; QueueLayout(); }
        }
        private void DrawOffsetButton(UIRenderContext context, Rectangle rectangle, bool increment, bool enabled)
        {
            context.Fill(rectangle, enabled ? context.Theme.PanelColor : context.Theme.BackgroundColor);
            context.Border(rectangle, context.Theme.PanelBorderColor);
            var color = enabled ? context.Theme.TextColor : context.Theme.DisabledTextColor;
            var hovered = enabled && rectangle.Contains(Context?.PointerPosition ?? Point.Zero);
            var iconName = increment ? "increment" : "decrement";
            if (IsLayoutRtl()) iconName = increment ? "decrement" : "increment";
            var icon = GetThemeIcon(iconName + (hovered ? "_highlight" : string.Empty));
            if (icon.HasValue)
            {
                context.Icon(icon.Value, new Vector2(rectangle.Center.X - icon.Value.LogicalSize.X / 2, rectangle.Center.Y - icon.Value.LogicalSize.Y / 2), color);
                return;
            }
            var centerX = rectangle.X + rectangle.Width / 2; var centerY = rectangle.Y + rectangle.Height / 2;
            if (increment)
            {
                context.Fill(new Rectangle(centerX - 2, centerY - 3, 2, 2), color);
                context.Fill(new Rectangle(centerX, centerY - 1, 2, 2), color);
                context.Fill(new Rectangle(centerX - 2, centerY + 1, 2, 2), color);
            }
            else
            {
                context.Fill(new Rectangle(centerX, centerY - 3, 2, 2), color);
                context.Fill(new Rectangle(centerX - 2, centerY - 1, 2, 2), color);
                context.Fill(new Rectangle(centerX, centerY + 1, 2, 2), color);
            }
        }
        private int GetDesiredTabWidth(int tab)
        {
            var item = _tabs[tab]; var text = EffectiveUIFont == null ? item.Title.Length * 8 : (int)MathF.Ceiling(TextMetrics.Measure(EffectiveUIFont, item.Title).X);
            var width = Math.Max(32, text + 12 + (item.Icon == null ? 0 : 20) + (item.ButtonIcon == null ? 0 : 18) + (ShowsClose(tab) ? 18 : 0));
            return MaxTabWidth > 0 ? Math.Min(width, MaxTabWidth) : width;
        }
        private bool ShowsClose(int tab) => CloseDisplayPolicy == TabCloseDisplayPolicy.Always || CloseDisplayPolicy == TabCloseDisplayPolicy.ActiveOnly && tab == CurrentTab;
        private Rectangle GetTabCloseRect(int index, Rectangle rect) => new Rectangle(rect.Right - 14, rect.Y + Math.Max(3, (rect.Height - 10) / 2), 10, 10);
        private Rectangle GetTabButtonRect(int index, Rectangle rect)
        {
            var right = ShowsClose(index) ? GetTabCloseRect(index, rect).Left - 4 : rect.Right - 4;
            return new Rectangle(right - 10, rect.Y + Math.Max(3, (rect.Height - 10) / 2), 10, 10);
        }
        private void PressTabAt(Point point, PointerButton button)
        {
            var index = GetTabAt(point, out var rect); if (index < 0) return;
            if (button == PointerButton.Middle && CloseWithMiddleMouse) { TabClosePressed?.Invoke(this, index); return; }
            var selecting = button == PointerButton.Left || button == PointerButton.Right && SelectWithRightMouseButton;
            if (button == PointerButton.Right) TabRightClicked?.Invoke(this, index);
            if (!selecting) return;
            _draggedTab = -1;
            if (_tabs[index].ButtonIcon != null && GetTabButtonRect(index, rect).Contains(point)) TabButtonPressed?.Invoke(this, index);
            else if (ShowsClose(index) && GetTabCloseRect(index, rect).Contains(point)) TabClosePressed?.Invoke(this, index);
            else if (!_tabs[index].Disabled)
            {
                CurrentTab = DeselectEnabled && CurrentTab == index ? -1 : index;
                TabClicked?.Invoke(this, index);
                if (button == PointerButton.Left && DragToRearrangeEnabled) _draggedTab = index;
            }
        }
        private void UpdateHoveredTab(Point point)
        {
            var hovered = GetTabAt(point, out _);
            if (hovered == _hoveredTab) return;
            _hoveredTab = hovered;
            if (hovered >= 0) TabHovered?.Invoke(this, hovered);
        }
        private void EnsureValidCurrent()
        {
            if (_currentTab >= 0 && _currentTab < _tabs.Count && !_tabs[_currentTab].Hidden && !_tabs[_currentTab].Disabled) return;
            _currentTab = -1;
            if (DeselectEnabled) return;
            for (var index = 0; index < _tabs.Count; index++) if (!_tabs[index].Hidden && !_tabs[index].Disabled) { _currentTab = index; break; }
        }
    }

    /// <summary>Selection behavior corresponding to Godot ItemList's single, multi and toggle modes.</summary>
    public enum ItemListSelectionMode { Single, Multi, Toggle }
    /// <summary>Whether an ItemList places an optional icon beside or above its text.</summary>
    public enum ItemListIconMode { Left, Top }
    public enum ItemListScrollHintMode { Disabled, Both, Top, Bottom }

    /// <summary>Mutable per-item state exposed by <see cref="ItemList"/>.</summary>
    public sealed class ItemListEntry
    {
        internal ItemListEntry(string text, Texture2D icon, bool selectable) { Text = text ?? string.Empty; Icon = icon; Selectable = selectable; }
        public string Text { get; internal set; }
        public Texture2D Icon { get; internal set; }
        public bool IconTransposed { get; internal set; }
        public Rectangle? IconRegion { get; internal set; }
        public Color IconModulate { get; internal set; } = Color.White;
        public Texture2D TagIcon { get; internal set; }
        public bool Selectable { get; internal set; }
        public bool Disabled { get; internal set; }
        public bool Selected { get; internal set; }
        public object Metadata { get; internal set; }
        public string Tooltip { get; internal set; } = string.Empty;
        public bool TooltipEnabled { get; internal set; } = true;
        public TextDirection TextDirection { get; internal set; } = TextDirection.Inherited;
        public string Language { get; internal set; } = string.Empty;
        public AutoTranslateMode AutoTranslateMode { get; internal set; } = AutoTranslateMode.Inherit;
        public Color? CustomForegroundColor { get; internal set; }
        public Color? CustomBackgroundColor { get; internal set; }
    }

    /// <summary>Godot-style selectable list supporting item state, columns, keyboard navigation and tile layouts.</summary>
    public sealed class ItemList : TemplatedControl
    {
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.List;
        private readonly UIFontSelection _fontSelection = new UIFontSelection();
        private readonly List<ItemListEntry> _entries = new List<ItemListEntry>();
        private ItemListSelectionMode _selectionMode;
        private int _current = -1;
        private float _scrollOffsetY;
        private string _searchString = string.Empty;
        private TimeSpan _lastSearchTime = TimeSpan.MinValue;
        private int _shiftAnchor = -1;
        private TimeSpan _lastClickTime = TimeSpan.MinValue;
        private Point _lastClickPosition;
        private int _lastClickIndex = -1;
        private bool _isDoubleClick;
        private static readonly TimeSpan DoubleClickTimeout = TimeSpan.FromMilliseconds(600);
        private const int DoubleClickTolerance = 5;
        public IReadOnlyList<ItemListEntry> Entries => _entries;
        /// <summary>Compatibility projection of the entries' text values.</summary>
        public IReadOnlyList<string> Items { get { var items = new List<string>(_entries.Count); foreach (var entry in _entries) items.Add(entry.Text); return items; } }
        public int ItemCount => _entries.Count;
        public ItemListSelectionMode SelectionMode { get => _selectionMode; set => _selectionMode = value; }
        public bool SelectModeMulti { get => SelectionMode == ItemListSelectionMode.Multi; set => SelectionMode = value ? ItemListSelectionMode.Multi : ItemListSelectionMode.Single; }
        public bool AllowReselect { get; set; }
        public bool AllowRightMouseSelect { get; set; }
        public bool AllowSearch { get; set; } = true;
        public TimeSpan IncrementalSearchTimeout { get; set; } = TimeSpan.FromMilliseconds(1000);
        public bool AutoWidth { get; set; }
        public bool AutoHeight { get; set; }
        // Godot's ItemList declares `bool wraparound_items = true;` (item_list.h) with no constructor
        // override - true is the real default, not false.
        public bool WraparoundItems { get; set; } = true;
        public ItemListScrollHintMode ScrollHintMode { get; private set; }
        public bool TileScrollHint { get; private set; }
        public bool SameColumnWidth { get; set; }
        public int FixedColumnWidth { get; set; }
        /// <summary>Maximum visual columns; zero lets the list use all fitting columns.</summary>
        public int MaxColumns { get; set; } = 1;
        public int MaxTextLines { get; set; } = 1;
        public ItemListIconMode IconMode { get; set; } = ItemListIconMode.Left;
        public Vector2 FixedIconSize { get; set; }
        public float IconScale { get; set; } = 1f;
        public float ItemHeight { get; set; } = 24;
        public SpriteFont Font { get => _fontSelection.SpriteFont; set { _fontSelection.SetSpriteFont(value); QueueLayout(); } }
        public UIFont UIFont { get => _fontSelection.UIFont; set { _fontSelection.SetUIFont(value); QueueLayout(); } }
        internal UIFont EffectiveUIFont => ResolveFont(_fontSelection);
        public int Current { get => _current; set => SetCurrent(value); }
        public float ScrollOffsetY { get => _scrollOffsetY; set => _scrollOffsetY = MathHelper.Clamp(value, 0, GetMaxScrollOffsetY()); }
        public event Action<ItemList, int> ItemSelected;
        public event Action<ItemList, int, bool> MultiSelected;
        public event Action<ItemList, int> ItemActivated;

        public ItemList()
        {
            FocusMode = FocusMode.All;
        }

        public int AddItem(string text, Texture2D icon = null, bool selectable = true)
        {
            _entries.Add(new ItemListEntry(text, icon, selectable)); QueueLayout(); return _entries.Count - 1;
        }
        public int AddIconItem(Texture2D icon, bool selectable = true) => AddItem(string.Empty, icon, selectable);
        public void Clear() { _entries.Clear(); _current = -1; QueueLayout(); }
        public void SetItemCount(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            while (_entries.Count < count) _entries.Add(new ItemListEntry(string.Empty, null, true));
            if (_entries.Count > count) _entries.RemoveRange(count, _entries.Count - count);
            if (_current >= count) _current = -1;
            QueueLayout();
        }
        public void RemoveItem(int index)
        {
            // Godot's remove_item only resets the selection when the removed index is exactly the
            // current one - it does NOT shift `current` down for later indices, a real (if arguably
            // buggy) Godot quirk matched here for behavioral parity, same family as OptionButton's
            // remove_item.
            index = NormalizeIndex(index); _entries.RemoveAt(index);
            if (_current == index) _current = -1;
            QueueLayout();
        }
        public void MoveItem(int fromIndex, int toIndex)
        {
            fromIndex = NormalizeIndex(fromIndex); toIndex = NormalizeIndex(toIndex);
            var entry = _entries[fromIndex]; _entries.RemoveAt(fromIndex); _entries.Insert(toIndex, entry);
            if (_current == fromIndex) _current = toIndex; QueueLayout();
        }
        public void SortItemsByText()
        {
            // Godot's sort_items_by_text re-anchors `current` to the selected item's new position
            // afterward (Single mode only), so keyboard nav/EnsureCurrentIsVisible don't desync onto a
            // stale index; select() itself fires no signal here, matching Godot exactly.
            var selected = SelectionMode == ItemListSelectionMode.Single ? _entries.Find(entry => entry.Selected) : null;
            _entries.Sort((left, right) => string.Compare(left.Text, right.Text, StringComparison.Ordinal));
            QueueLayout();
            if (selected != null) Select(_entries.IndexOf(selected));
        }
        public string GetItemText(int index) => _entries[NormalizeIndex(index)].Text;
        public void SetItemText(int index, string text) { _entries[NormalizeIndex(index)].Text = text ?? string.Empty; QueueLayout(); }
        public void SetItemTextDirection(int index, TextDirection direction)
        {
            if (!Enum.IsDefined(typeof(TextDirection), direction)) throw new ArgumentOutOfRangeException(nameof(direction));
            _entries[NormalizeIndex(index)].TextDirection = direction;
        }
        public TextDirection GetItemTextDirection(int index) => _entries[NormalizeIndex(index)].TextDirection;
        public void SetItemLanguage(int index, string language) => _entries[NormalizeIndex(index)].Language = language ?? string.Empty;
        public string GetItemLanguage(int index) => _entries[NormalizeIndex(index)].Language;
        public void SetItemAutoTranslateMode(int index, AutoTranslateMode mode)
        {
            if (!Enum.IsDefined(typeof(AutoTranslateMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            _entries[NormalizeIndex(index)].AutoTranslateMode = mode;
        }
        public AutoTranslateMode GetItemAutoTranslateMode(int index) => _entries[NormalizeIndex(index)].AutoTranslateMode;
        public Texture2D GetItemIcon(int index) => _entries[NormalizeIndex(index)].Icon;
        public void SetItemIcon(int index, Texture2D icon) { _entries[NormalizeIndex(index)].Icon = icon; QueueLayout(); }
        public void SetItemIconTransposed(int index, bool transposed) { _entries[NormalizeIndex(index)].IconTransposed = transposed; QueueLayout(); }
        public bool IsItemIconTransposed(int index) => _entries[NormalizeIndex(index)].IconTransposed;
        public void SetItemIconRegion(int index, Rectangle? region)
        {
            var entry = _entries[NormalizeIndex(index)];
            if (region.HasValue && (region.Value.Width <= 0 || region.Value.Height <= 0)) throw new ArgumentOutOfRangeException(nameof(region));
            entry.IconRegion = region; QueueLayout();
        }
        public Rectangle? GetItemIconRegion(int index) => _entries[NormalizeIndex(index)].IconRegion;
        public void SetItemIconModulate(int index, Color modulate) => _entries[NormalizeIndex(index)].IconModulate = modulate;
        public Color GetItemIconModulate(int index) => _entries[NormalizeIndex(index)].IconModulate;
        public void SetItemTagIcon(int index, Texture2D icon) { _entries[NormalizeIndex(index)].TagIcon = icon; QueueLayout(); }
        public Texture2D GetItemTagIcon(int index) => _entries[NormalizeIndex(index)].TagIcon;
        public void SetItemSelectable(int index, bool selectable) { _entries[NormalizeIndex(index)].Selectable = selectable; }
        public bool IsItemSelectable(int index) => _entries[NormalizeIndex(index)].Selectable;
        public void SetItemDisabled(int index, bool disabled) { _entries[NormalizeIndex(index)].Disabled = disabled; }
        public bool IsItemDisabled(int index) => _entries[NormalizeIndex(index)].Disabled;
        public void SetItemMetadata(int index, object metadata) => _entries[NormalizeIndex(index)].Metadata = metadata;
        public object GetItemMetadata(int index) => _entries[NormalizeIndex(index)].Metadata;
        public int FindMetadata(object metadata) { for (var index = 0; index < _entries.Count; index++) if (Equals(_entries[index].Metadata, metadata)) return index; return -1; }
        public void SetItemTooltip(int index, string tooltip) => _entries[NormalizeIndex(index)].Tooltip = tooltip ?? string.Empty;
        public string GetItemTooltip(int index) => _entries[NormalizeIndex(index)].Tooltip;
        public void SetItemTooltipEnabled(int index, bool enabled) => _entries[NormalizeIndex(index)].TooltipEnabled = enabled;
        public bool IsItemTooltipEnabled(int index) => _entries[NormalizeIndex(index)].TooltipEnabled;
        public void SetItemCustomForegroundColor(int index, Color? color) => _entries[NormalizeIndex(index)].CustomForegroundColor = color;
        public Color? GetItemCustomForegroundColor(int index) => _entries[NormalizeIndex(index)].CustomForegroundColor;
        public void SetItemCustomBackgroundColor(int index, Color? color) => _entries[NormalizeIndex(index)].CustomBackgroundColor = color;
        public Color? GetItemCustomBackgroundColor(int index) => _entries[NormalizeIndex(index)].CustomBackgroundColor;
        public bool IsSelected(int index) => _entries[NormalizeIndex(index)].Selected;
        public bool IsAnythingSelected() { foreach (var entry in _entries) if (entry.Selected) return true; return false; }
        public IReadOnlyList<int> GetSelectedItems() { var selected = new List<int>(); for (var index = 0; index < _entries.Count; index++) if (_entries[index].Selected) selected.Add(index); return selected; }
        public void SetAllowRmbSelect(bool allow) => AllowRightMouseSelect = allow;
        public bool GetAllowRmbSelect() => AllowRightMouseSelect;
        public void SetAllowReselect(bool allow) => AllowReselect = allow;
        public bool GetAllowReselect() => AllowReselect;
        public void SetAllowSearch(bool allow) { AllowSearch = allow; if (!allow) ClearIncrementalSearch(); }
        public bool GetAllowSearch() => AllowSearch;
        public void SetAutoWidth(bool enable) { AutoWidth = enable; QueueLayout(); }
        public bool HasAutoWidth() => AutoWidth;
        public void SetAutoHeight(bool enable) { AutoHeight = enable; QueueLayout(); }
        public bool HasAutoHeight() => AutoHeight;
        public void SetWraparoundItems(bool enable) => WraparoundItems = enable;
        public bool HasWraparoundItems() => WraparoundItems;
        public void SetScrollHintMode(ItemListScrollHintMode mode) { if (!Enum.IsDefined(typeof(ItemListScrollHintMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode)); ScrollHintMode = mode; QueueLayout(); }
        public ItemListScrollHintMode GetScrollHintMode() => ScrollHintMode;
        public void SetTileScrollHint(bool enable) { TileScrollHint = enable; QueueLayout(); }
        public bool IsScrollHintTiled() => TileScrollHint;
        public string GetIncrementalSearch() => _searchString;
        public void ClearIncrementalSearch() => _searchString = string.Empty;
        public void Select(int index, bool single = true)
        {
            index = NormalizeIndex(index); var entry = _entries[index];
            if (!entry.Selectable || entry.Disabled) return;
            var wasSelected = entry.Selected;
            // Godot's select() only touches `current` in the single-select branch; a Multi-mode
            // Ctrl-click add (single=false) deliberately leaves the keyboard cursor where it was.
            if (single || SelectionMode == ItemListSelectionMode.Single)
            {
                foreach (var item in _entries) item.Selected = false;
                _current = index;
            }
            entry.Selected = true;
            // Godot only ever fires item_selected from call sites gated on select_mode == SELECT_SINGLE
            // (select() itself never emits it) - Multi/Toggle mode selection fires multi_selected only.
            if ((!wasSelected || AllowReselect) && SelectionMode == ItemListSelectionMode.Single) ItemSelected?.Invoke(this, index);
            if ((!wasSelected || AllowReselect) && SelectionMode != ItemListSelectionMode.Single) MultiSelected?.Invoke(this, index, true);
        }
        public void Deselect(int index)
        {
            index = NormalizeIndex(index); var entry = _entries[index]; if (!entry.Selected) return;
            // Godot's deselect() only resets `current` in Single mode; Multi/Toggle mode leaves the
            // keyboard cursor where it was.
            entry.Selected = false; if (_current == index && SelectionMode == ItemListSelectionMode.Single) _current = -1;
            if (SelectionMode != ItemListSelectionMode.Single) MultiSelected?.Invoke(this, index, false);
        }
        public void DeselectAll() { foreach (var entry in _entries) entry.Selected = false; _current = -1; }
        public void SetCurrent(int index)
        {
            // Godot's ItemList::set_current only actually selects (and thus can fire item_selected) in
            // Single mode; in Multi/Toggle mode it's pure keyboard-cursor bookkeeping that never touches
            // any item's selected state or fires any signal.
            if (index < 0) { _current = -1; return; }
            if (_current == index) return;
            if (SelectionMode == ItemListSelectionMode.Single) Select(index, true);
            else _current = index;
        }
        public void EnsureCurrentIsVisible()
        {
            if (_current < 0 || _current >= _entries.Count) return;
            var rect = GetItemRect(_current);
            if (rect.Top < Bounds.Top) ScrollOffsetY += rect.Top - Bounds.Top;
            else if (rect.Bottom > Bounds.Bottom) ScrollOffsetY += rect.Bottom - Bounds.Bottom;
        }
        public void CenterOnCurrent(bool centerVertically = true, bool centerHorizontally = false)
        {
            if (_current < 0 || _current >= _entries.Count || !centerVertically) return;
            var unscrolled = GetUnscrolledItemRect(_current);
            ScrollOffsetY = unscrolled.Center.Y - Bounds.Height / 2f;
        }
        public Rectangle GetItemRect(int index, bool expand = true)
        {
            var rect = GetUnscrolledItemRect(index, expand);
            rect.Y -= (int)MathF.Round(_scrollOffsetY);
            return rect;
        }
        private Rectangle GetUnscrolledItemRect(int index, bool expand = true)
        {
            index = NormalizeIndex(index); var columns = GetColumnCount(); var cellWidth = GetCellWidth(columns); var column = index % columns; var row = index / columns;
            var width = expand && column == columns - 1 ? Math.Max(0, Bounds.Right - (Bounds.X + column * cellWidth) - 1) : cellWidth;
            return new Rectangle(Bounds.X + column * cellWidth, Bounds.Y + row * (int)ItemHeight, width, (int)ItemHeight);
        }
        public int GetItemAtPosition(Point position, bool exact = false)
        {
            if (!Bounds.Contains(position)) return -1;
            var columns = GetColumnCount(); var cellWidth = GetCellWidth(columns); var column = Math.Max(0, (position.X - Bounds.X) / Math.Max(1, cellWidth)); var row = Math.Max(0, (int)((position.Y - Bounds.Y + _scrollOffsetY) / ItemHeight));
            var index = row * columns + column;
            return index >= 0 && index < _entries.Count && (!exact || GetItemRect(index).Contains(position)) ? index : -1;
        }
        public bool IsPosAtEndOfItems(Point position)
        {
            if (_entries.Count == 0) return true;
            return position.Y + _scrollOffsetY > GetUnscrolledItemRect(_entries.Count - 1).Bottom;
        }
        public AutoTranslateMode GetTooltipAutoTranslateModeAt(Point position)
        {
            var index = GetItemAtPosition(position, true);
            return index >= 0 ? _entries[index].AutoTranslateMode : AutoTranslateMode.Inherit;
        }
        public float GetMaxScrollOffsetY()
        {
            var rows = (int)Math.Ceiling(_entries.Count / (float)Math.Max(1, GetColumnCount()));
            return Math.Max(0, rows * ItemHeight - Bounds.Height);
        }
        public override Vector2 GetMinimumSize()
        {
            var minimum = base.GetMinimumSize(); if (AutoHeight) minimum.Y = Math.Max(minimum.Y, ((int)Math.Ceiling(_entries.Count / (float)GetColumnCount())) * ItemHeight); if (AutoWidth) minimum.X = Math.Max(minimum.X, FixedColumnWidth > 0 ? FixedColumnWidth * GetColumnCount() : 120); return minimum;
        }
        public override string GetTooltip(Point position)
        {
            var index = GetItemAtPosition(position, true); if (index >= 0) { var entry = _entries[index]; if (!entry.TooltipEnabled) return string.Empty; if (!string.IsNullOrEmpty(entry.Tooltip)) return entry.Tooltip; if (!string.IsNullOrEmpty(entry.Text)) return entry.Text; }
            return base.GetTooltip(position);
        }
        internal override void PointerPressed(Point point)
        {
            base.PointerPressed(point); var index = GetItemAtPosition(point); if (index < 0) return;
            // Godot's item_activated only fires on an actual LEFT double-click, not every plain click -
            // track click timing/position/index the same way RichTextLabel already tracks multi-click state.
            var clickTime = Context?.CurrentTime ?? TimeSpan.Zero;
            var withinTimeout = _lastClickTime != TimeSpan.MinValue && clickTime - _lastClickTime <= DoubleClickTimeout;
            var withinTolerance = Vector2.DistanceSquared(point.ToVector2(), _lastClickPosition.ToVector2()) <= DoubleClickTolerance * DoubleClickTolerance;
            _isDoubleClick = withinTimeout && withinTolerance && index == _lastClickIndex;
            _lastClickTime = clickTime; _lastClickPosition = point; _lastClickIndex = index;
            SelectFromPointer(index);
        }
        internal override void PointerButtonPressed(Point position, PointerButton button)
        {
            base.PointerButtonPressed(position, button);
            if (button != PointerButton.Right || !AllowRightMouseSelect) return;
            var index = GetItemAtPosition(position); if (index >= 0) SelectFromPointer(index);
        }
        internal override void PointerReleased(Point point, bool isInside)
        {
            var index = GetItemAtPosition(point); if (isInside && index >= 0 && index == _current && _isDoubleClick) ItemActivated?.Invoke(this, index);
        }
        internal override void CancelInput()
        {
            _isDoubleClick = false;
            _lastClickTime = TimeSpan.MinValue;
            _lastClickIndex = -1;
            base.CancelInput();
        }
        internal override void KeyPressed(Keys key)
        {
            if (_entries.Count == 0) return; var columns = GetColumnCount(); var current = _current < 0 ? 0 : _current;
            var keyboard = Context?.CurrentKeyboardState ?? default;
            var shiftHeld = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
            if (SelectionMode == ItemListSelectionMode.Multi && shiftHeld && (key == Keys.Up || key == Keys.Down || key == Keys.Left || key == Keys.Right))
            {
                int shiftTarget;
                // Godot's Shift+Up/Down range-select steps by the raw max_columns PROPERTY, not the
                // fitted current_columns layout count plain navigation uses below - when MaxColumns is
                // set to the auto-fit sentinel (0), this collapses the shift-step to the current item.
                if (key == Keys.Up) shiftTarget = Math.Max(current - MaxColumns, 0);
                else if (key == Keys.Down) shiftTarget = Math.Min(current + MaxColumns, _entries.Count - 1);
                else if (key == Keys.Left) shiftTarget = Math.Max(current - 1, 0);
                else shiftTarget = Math.Min(current + 1, _entries.Count - 1);
                ShiftRangeSelect(current, shiftTarget);
                return;
            }
            // Godot's ui_select (Space) toggles the current item's selection in Multi/Toggle mode
            // without moving the cursor at all.
            if (key == Keys.Space && (SelectionMode == ItemListSelectionMode.Multi || SelectionMode == ItemListSelectionMode.Toggle))
            {
                if (_current >= 0 && _current < _entries.Count)
                {
                    var entry = _entries[_current];
                    if (CanSelect(_current) && !entry.Selected) Select(_current, false);
                    else if (entry.Selected) Deselect(_current);
                }
                return;
            }
            // Godot's ui_page_up/ui_page_down walk up to 4 grid rows looking for the first selectable target.
            if (key == Keys.PageUp)
            {
                for (var i = 4; i > 0; i--)
                {
                    var index = current - columns * i;
                    if (index >= 0 && index < _entries.Count && CanSelect(index)) { SetCurrent(index); EnsureCurrentIsVisible(); return; }
                }
                return;
            }
            if (key == Keys.PageDown)
            {
                for (var i = 4; i > 0; i--)
                {
                    var index = current + columns * i;
                    if (index >= 0 && index < _entries.Count && CanSelect(index)) { SetCurrent(index); EnsureCurrentIsVisible(); return; }
                }
                return;
            }
            var next = current;
            if (key == Keys.Left) next = Navigate(current, -1); else if (key == Keys.Right) next = Navigate(current, 1); else if (key == Keys.Up) next = Navigate(current, -columns); else if (key == Keys.Down) next = Navigate(current, columns); else if (key == Keys.Enter && _current >= 0 && !_entries[_current].Disabled) { ItemActivated?.Invoke(this, _current); return; } else return;
            SetCurrent(next);
            EnsureCurrentIsVisible();
        }
        private bool CanSelect(int index) { var entry = _entries[index]; return entry.Selectable && !entry.Disabled; }
        internal override void Process(GameTime gameTime)
        {
            var keyboard = Context?.CurrentKeyboardState ?? default;
            if (!keyboard.IsKeyDown(Keys.LeftShift) && !keyboard.IsKeyDown(Keys.RightShift)) _shiftAnchor = -1;
            base.Process(gameTime);
        }
        /// <summary>Selects the inclusive range between the shift-select anchor and <paramref name="to"/>, matching Godot's ItemList::_shift_range_select.</summary>
        private void ShiftRangeSelect(int from, int to)
        {
            if (_shiftAnchor < 0) _shiftAnchor = from;
            var low = Math.Min(_shiftAnchor, to); var high = Math.Max(_shiftAnchor, to);
            for (var i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (i >= low && i <= high)
                {
                    if (!entry.Selected)
                    {
                        // Godot's select() silently no-ops for a disabled/non-selectable item, but
                        // _shift_range_select still unconditionally emits multi_selected regardless -
                        // matching that exact quirk rather than only firing when the state actually flips.
                        if (entry.Selectable && !entry.Disabled) entry.Selected = true;
                        MultiSelected?.Invoke(this, i, true);
                    }
                }
                else if (entry.Selected) { entry.Selected = false; MultiSelected?.Invoke(this, i, false); }
            }
            _current = to;
            EnsureCurrentIsVisible();
        }
        internal override void TextInput(char character)
        {
            if (!AllowSearch || _entries.Count == 0 || char.IsControl(character)) return;
            var now = Context?.CurrentTime ?? TimeSpan.Zero;
            if (_lastSearchTime == TimeSpan.MinValue || now - _lastSearchTime > IncrementalSearchTimeout) _searchString = string.Empty;
            _lastSearchTime = now;
            var text = character.ToString();
            if (_searchString.Length == 1 && string.Equals(_searchString, text, StringComparison.OrdinalIgnoreCase))
            {
                SearchNext(_searchString, true);
                return;
            }
            _searchString += text;
            if (!SearchNext(_searchString, false) && _searchString.Length > 1)
            {
                _searchString = text;
                SearchNext(_searchString, true);
            }
        }
        internal void DrawItemList(UIRenderContext context)
        {
            context.Fill(Bounds, context.Theme.BackgroundColor); context.Border(Bounds, context.Theme.PanelBorderColor);
            for (var index = 0; index < _entries.Count; index++)
            {
                var entry = _entries[index]; var rect = GetItemRect(index);
                if (rect.Bottom < Bounds.Top || rect.Top > Bounds.Bottom) continue;
                if (entry.CustomBackgroundColor.HasValue) context.Fill(rect, entry.CustomBackgroundColor.Value); else if (entry.Selected) context.Fill(rect, context.Theme.AccentColor);
                var textX = rect.X + 5;
                if (entry.Icon != null)
                {
                    var source = entry.IconRegion ?? new Rectangle(0, 0, entry.Icon.Width, entry.Icon.Height);
                    var iconSize = FixedIconSize == Vector2.Zero ? new Vector2(source.Width, source.Height) : FixedIconSize;
                    iconSize *= IconScale;
                    var displayWidth = Math.Max(1, (int)iconSize.X); var displayHeight = Math.Max(1, (int)iconSize.Y);
                    var iconRect = new Rectangle(rect.X + 3, IconMode == ItemListIconMode.Left ? rect.Y + Math.Max(1, (rect.Height - (entry.IconTransposed ? displayWidth : displayHeight)) / 2) : rect.Y + 3, entry.IconTransposed ? displayHeight : displayWidth, entry.IconTransposed ? displayWidth : displayHeight);
                    if (entry.IconTransposed)
                    {
                        var position = new Vector2(iconRect.Center.X, iconRect.Center.Y);
                        var origin = new Vector2(source.Width / 2f, source.Height / 2f);
                        var scale = new Vector2(displayWidth / (float)source.Width, displayHeight / (float)source.Height);
                        context.SpriteBatch.Draw(entry.Icon, position, source, entry.IconModulate, MathHelper.PiOver2, origin, scale, SpriteEffects.None, 0);
                    }
                    else context.SpriteBatch.Draw(entry.Icon, iconRect, source, entry.IconModulate);
                    if (IconMode == ItemListIconMode.Left) textX = iconRect.Right + 4;
                }
                if (entry.TagIcon != null)
                {
                    var tagSize = Math.Max(8, Math.Min(14, Math.Min(rect.Width, rect.Height) / 2));
                    context.SpriteBatch.Draw(entry.TagIcon, new Rectangle(rect.Right - tagSize - 3, rect.Y + 3, tagSize, tagSize), Color.White);
                }
                if (EffectiveUIFont != null && !string.IsNullOrEmpty(entry.Text)) context.Text(EffectiveUIFont, entry.Text, new Vector2(textX, rect.Y + Math.Max(2, (rect.Height - TextMetrics.LineHeight(EffectiveUIFont)) / 2)), entry.Disabled ? context.Theme.DisabledTextColor : entry.CustomForegroundColor ?? context.Theme.TextColor);
            }
        }
        private void SelectFromPointer(int index)
        {
            // Matches Godot's ItemList::gui_input click branching exactly: a disabled item is a hard
            // no-op; Ctrl/Cmd-click on an already-selected Multi-mode item deselects just that item;
            // Shift-click in Multi mode range-selects from the current item; Toggle mode flips the
            // clicked item's own state; otherwise a plain click selects exclusively (clearing every
            // other selection) UNLESS Ctrl/Cmd is held, in which case it adds to the existing selection
            // - Godot's `select(i, select_mode == SELECT_SINGLE || !ctrl_pressed)`.
            var entry = _entries[index];
            if (entry.Disabled) { ClearIncrementalSearch(); return; }
            var keyboard = Context?.CurrentKeyboardState ?? default;
            var ctrl = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
            var shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
            if (SelectionMode == ItemListSelectionMode.Multi && entry.Selected && ctrl) { Deselect(index); ClearIncrementalSearch(); return; }
            if (SelectionMode == ItemListSelectionMode.Multi && shift && _current >= 0 && _current != index) { ShiftRangeSelect(_current, index); ClearIncrementalSearch(); return; }
            if (SelectionMode == ItemListSelectionMode.Toggle)
            {
                if (entry.Selectable) { if (entry.Selected) Deselect(index); else Select(index, false); _current = index; }
            }
            else if (entry.Selectable && (!entry.Selected || AllowReselect))
            {
                Select(index, SelectionMode == ItemListSelectionMode.Single || !ctrl);
            }
            ClearIncrementalSearch();
        }
        private bool SearchNext(string query, bool wrapFromNext)
        {
            if (string.IsNullOrEmpty(query)) return false;
            var start = _current < 0 ? 0 : (_current + (wrapFromNext ? 1 : 0)) % _entries.Count;
            for (var offset = 0; offset < _entries.Count; offset++)
            {
                var index = (start + offset) % _entries.Count;
                var entry = _entries[index];
                if (!entry.Selectable || entry.Disabled) continue;
                if (!entry.Text.StartsWith(query, StringComparison.OrdinalIgnoreCase)) continue;
                SetCurrent(index);
                EnsureCurrentIsVisible();
                return true;
            }
            return false;
        }
        private int Navigate(int current, int delta)
        {
            var next = current + delta;
            if (WraparoundItems && _entries.Count > 0)
            {
                next %= _entries.Count;
                if (next < 0) next += _entries.Count;
                return next;
            }
            return Math.Max(0, Math.Min(_entries.Count - 1, next));
        }
        private int GetColumnCount()
        {
            if (_entries.Count == 0) return 1;
            if (MaxColumns > 0) return Math.Max(1, Math.Min(MaxColumns, _entries.Count));
            var width = FixedColumnWidth > 0 ? FixedColumnWidth : Math.Max(80, Bounds.Width / Math.Max(1, _entries.Count));
            return Math.Max(1, Math.Min(_entries.Count, Bounds.Width / Math.Max(1, width)));
        }
        private int GetCellWidth(int columns) => FixedColumnWidth > 0 ? FixedColumnWidth : Math.Max(1, Bounds.Width / Math.Max(1, columns));
        private int NormalizeIndex(int index) { if (index < 0) index += _entries.Count; if (index < 0 || index >= _entries.Count) throw new ArgumentOutOfRangeException(nameof(index)); return index; }
    }

    /// <summary>A parsed run in a <see cref="RichTextLabel"/> document.</summary>
    public readonly struct RichTextSpan
    {
        public RichTextSpan(string text, Color? color, bool bold, bool italic, bool underline, bool strikethrough)
            : this(text, color, null, bold, italic, underline, strikethrough, null) { }
        public RichTextSpan(string text, Color? color, Color? backgroundColor, bool bold, bool italic, bool underline, bool strikethrough, object meta)
        {
            Text = text ?? string.Empty; Color = color; BackgroundColor = backgroundColor; Bold = bold; Italic = italic; Underline = underline; Strikethrough = strikethrough; Meta = meta; Image = null; ImageSize = Vector2.Zero; ImageModulate = Microsoft.Xna.Framework.Color.White; IsHorizontalRule = false; RuleWidth = 0; RuleHeight = 0; RuleColor = null; RuleAlignment = HorizontalAlignment.Center; RuleWidthInPercent = false; RuleHeightInPercent = false;
        }
        internal RichTextSpan(Texture2D image, Vector2 size, Color modulate, object meta, string altText)
        {
            Text = altText ?? string.Empty; Color = null; BackgroundColor = null; Bold = Italic = Underline = Strikethrough = false; Meta = meta; Image = image; ImageSize = size; ImageModulate = modulate; IsHorizontalRule = false; RuleWidth = 0; RuleHeight = 0; RuleColor = null; RuleAlignment = HorizontalAlignment.Center; RuleWidthInPercent = false; RuleHeightInPercent = false;
        }
        internal RichTextSpan(int width, int height, Color? color, HorizontalAlignment alignment, bool widthInPercent, bool heightInPercent)
        {
            Text = string.Empty; Color = null; BackgroundColor = null; Bold = Italic = Underline = Strikethrough = false; Meta = null; Image = null; ImageSize = Vector2.Zero; ImageModulate = Microsoft.Xna.Framework.Color.White; IsHorizontalRule = true; RuleWidth = Math.Max(0, width); RuleHeight = Math.Max(0, height); RuleColor = color; RuleAlignment = alignment; RuleWidthInPercent = widthInPercent; RuleHeightInPercent = heightInPercent;
        }
        public string Text { get; }
        public Color? Color { get; }
        public Color? BackgroundColor { get; }
        public bool Bold { get; }
        public bool Italic { get; }
        public bool Underline { get; }
        public bool Strikethrough { get; }
        /// <summary>Godot-style metadata attached to this span (for example from <c>[url]</c> or <c>[meta]</c>).</summary>
        public object Meta { get; }
        /// <summary>Optional inline texture added through <see cref="RichTextLabel.AddImage"/>.</summary>
        public Texture2D Image { get; }
        public Vector2 ImageSize { get; }
        public Color ImageModulate { get; }
        /// <summary>Whether this span is a paragraph-level horizontal rule created by <see cref="RichTextLabel.AddHorizontalRule"/>.</summary>
        public bool IsHorizontalRule { get; }
        public int RuleWidth { get; }
        public int RuleHeight { get; }
        public Color? RuleColor { get; }
        public HorizontalAlignment RuleAlignment { get; }
        public bool RuleWidthInPercent { get; }
        public bool RuleHeightInPercent { get; }
    }

    /// <summary>A screen-space metadata hit region generated by <see cref="RichTextLabel"/>.</summary>
    public readonly struct RichTextMetaRegion
    {
        public RichTextMetaRegion(Rectangle bounds, object meta) { Bounds = bounds; Meta = meta; }
        public Rectangle Bounds { get; }
        public object Meta { get; }
    }

    /// <summary>Selection expansion modes used by retained RichText multi-click gestures.</summary>
    public enum RichTextSelectionMode { SingleClick, DoubleClick, TripleClick }

    /// <summary>Marker styles accepted by <see cref="RichTextLabel.PushList"/>, corresponding to Godot's RichTextLabel list types.</summary>
    public enum RichTextListType { Dots, Numbers, Letters, Roman }

    /// <summary>
    /// Lightweight BBCode label with styled text runs. It intentionally concentrates on the common
    /// authoring tags (`b`, `i`, `u`, `s`/`strike`, `color`, and `br`) while preserving the retained
    /// Control model and plain-text <see cref="Label.Text"/> projection.
    /// </summary>
    [TemplatePart(RichTextPresenterPartName, typeof(Container))]
    public class RichTextLabel : TemplatedControl
    {
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.Document;
        public override string AccessibilityName => string.IsNullOrEmpty(base.AccessibilityName) ? Text ?? string.Empty : base.AccessibilityName;
        public const string RichTextPresenterPartName = "PART_RichTextPresenter";
        private const int ContextMenuCopyId = 1;
        private const int ContextMenuSelectAllId = 2;
        private readonly UIFontSelection _fontSelection = new UIFontSelection();
        private string _text = string.Empty;
        private LabelAutowrapMode _autowrapMode;
        private TextDirection _textDirection = TextDirection.Auto;
        private float _letterSpacing;
        private readonly List<RichTextSpan> _spans = new List<RichTextSpan>();
        private readonly List<RichTextMetaRegion> _metaRegions = new List<RichTextMetaRegion>();
        private readonly List<TextStyle> _styleStack = new List<TextStyle> { default };
        private readonly List<TableFrame> _tableStack = new List<TableFrame>();
        private string _spanText = string.Empty;
        private int _selectionFrom = -1;
        private int _selectionTo = -1;
        private int _selectionAnchor = -1;
        private int _scrollOffset;
        private TimeSpan _lastTextClickTime = TimeSpan.MinValue;
        private Point _lastTextClickPosition;
        private int _textClickCount;
        private RichTextSelectionMode _selectionMode;
        private bool _selectingText;
        private Point _lastSelectionPointerPosition;
        private float _selectionAutoScrollRemainder;
        private bool _selectionDragAttempt;
        private bool _deselectOnFocusLossEnabled = true;
        private bool _scrollActive = true;
        private readonly VScrollBar _verticalScrollBar;
        private bool _syncingVerticalScrollBar;
        private readonly PopupMenu _contextMenu;
        public RichTextLabel()
        {
            _verticalScrollBar = new VScrollBar { ZIndex = 1, Visible = false, TooltipText = "RichText document scroll" };
            _verticalScrollBar.ValueChanged += (_, value) => { if (!_syncingVerticalScrollBar) SetScrollOffsetCore((int)MathF.Round(value)); };
            AddChild(_verticalScrollBar);
            _contextMenu = new PopupMenu { Visible = false };
            _contextMenu.IdPressed += (_, id) =>
            {
                if (id == ContextMenuCopyId)
                {
                    var text = GetSelectedText();
                    if (string.IsNullOrEmpty(text)) text = Text;
                    WriteClipboard(text);
                }
                else if (id == ContextMenuSelectAllId) SelectAll();
            };
        }
        public string Text { get => _text; set { value ??= string.Empty; if (_text == value) return; _text = value; QueueLayout(); } }
        public SpriteFont Font { get => _fontSelection.SpriteFont; set { _fontSelection.SetSpriteFont(value); QueueLayout(); } }
        public UIFont UIFont { get => _fontSelection.UIFont; set { _fontSelection.SetUIFont(value); QueueLayout(); } }
        internal UIFont EffectiveUIFont => ResolveFont(_fontSelection, FontFamily, FontSize, FontWeight, FontStyle, FontStretch);
        public Color? FontColor { get => Foreground; set => Foreground = value; }
        public new HorizontalAlignment HorizontalAlignment { get; set; }
        public new VerticalAlignment VerticalAlignment { get; set; }
        public bool Autowrap { get => AutowrapMode != LabelAutowrapMode.Off; set => AutowrapMode = value ? LabelAutowrapMode.WordSmart : LabelAutowrapMode.Off; }
        public LabelAutowrapMode AutowrapMode { get => _autowrapMode; set { _autowrapMode = value; QueueLayout(); } }
        public TextDirection TextDirection { get => _textDirection; set { if (_textDirection == value) return; _textDirection = value; QueueLayout(); } }
        public float LetterSpacing
        {
            get => _letterSpacing;
            set
            {
                if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
                if (_letterSpacing == value) return;
                _letterSpacing = value;
                QueueLayout();
            }
        }
        public Thickness Padding { get; set; } = new Thickness(3);
        public void SetText(string text) => Text = text;
        public string GetText() => Text;
        public void SetAutowrapMode(LabelAutowrapMode mode) { if (!Enum.IsDefined(typeof(LabelAutowrapMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode)); AutowrapMode = mode; }
        public LabelAutowrapMode GetAutowrapMode() => AutowrapMode;
        public IReadOnlyList<RichTextSpan> Spans => _spans;
        /// <summary>Current deterministic hit regions for spans carrying metadata.</summary>
        public IReadOnlyList<RichTextMetaRegion> MetaRegions { get { BuildMetaRegions(); return _metaRegions; } }
        public bool BbcodeEnabled { get; set; } = true;
        public bool FitContent { get; set; }
        /// <summary>Enables wheel scrolling and the retained scroll viewport, matching Godot's <c>scroll_active</c>.</summary>
        public bool ScrollActive
        {
            get => _scrollActive;
            set
            {
                if (_scrollActive == value) return;
                _scrollActive = value;
                SynchronizeVerticalScrollBar();
                QueueLayout();
            }
        }
        /// <summary>Automatically follows newly appended content to its end, matching Godot's <c>scroll_following</c>.</summary>
        public bool ScrollFollowing { get; set; }
        /// <summary>Whether the retained vertical scrollbar is currently presented for overflowing, active document content.</summary>
        public bool IsVerticalScrollBarVisible => _verticalScrollBar.Visible;
        /// <summary>Pixels moved for one wheel notch. The default uses the document line height.</summary>
        public int ScrollStep { get; set; }
        /// <summary>Current vertical document offset in pixels, clamped to the retained content range.</summary>
        public int ScrollOffset { get => ClampScrollOffset(_scrollOffset); set => SetScrollOffsetCore(value); }
        /// <summary>Enables Godot-style text selection for this read-only document.</summary>
        public bool SelectionEnabled
        {
            get => _selectionEnabled;
            set
            {
                if (_selectionEnabled == value) return;
                _selectionEnabled = value;
                if (value && FocusMode == FocusMode.None) FocusMode = FocusMode.Click;
                if (!value) Deselect();
            }
        }
        private bool _selectionEnabled;
        /// <summary>Theme-like background color drawn behind selected document characters.</summary>
        public Color SelectionColor { get; set; } = new Color(66, 133, 197, 160);
        /// <summary>Maximum interval between text clicks that belong to the same double/triple-click gesture.</summary>
        public TimeSpan MultiClickTimeout { get; set; } = TimeSpan.FromMilliseconds(600);
        /// <summary>Maximum pointer displacement between text clicks that belong to the same double/triple-click gesture.</summary>
        public int MultiClickTolerance { get; set; } = 5;
        /// <summary>Enables scrolling a document while a captured text-selection gesture is held beyond its vertical viewport.</summary>
        public bool SelectionAutoScrollEnabled { get; set; } = true;
        /// <summary>Base selection auto-scroll velocity in pixels per second. Godot's 2-pixel 50 ms timer step corresponds to 40.</summary>
        public float SelectionAutoScrollSpeed { get; set; } = 40f;
        /// <summary>Whether selection is cleared when keyboard focus moves to another control, matching Godot's <c>deselect_on_focus_loss_enabled</c>.</summary>
        public bool DeselectOnFocusLossEnabled
        {
            get => _deselectOnFocusLossEnabled;
            set
            {
                if (_deselectOnFocusLossEnabled == value) return;
                _deselectOnFocusLossEnabled = value;
                if (value && Context != null && Context.FocusedControl != this) Deselect();
            }
        }
        /// <summary>Whether pressing inside an existing selection can provide its text through the Control drag/drop lifecycle.</summary>
        public bool DragAndDropSelectionEnabled { get; set; } = true;
        /// <summary>Enables focused select-all and copy shortcuts, matching Godot's <c>shortcut_keys_enabled</c>.</summary>
        public bool ShortcutKeysEnabled { get; set; } = true;
        /// <summary>Enables the retained Copy/Select All context menu, matching Godot's <c>context_menu_enabled</c>.</summary>
        public bool ContextMenuEnabled { get; set; }
        /// <summary>Whether this label currently owns a non-empty selection.</summary>
        public bool HasSelection => SelectionEnabled && _selectionFrom >= 0 && _selectionTo > _selectionFrom;
        /// <summary>Zero-based inclusive selection start, or <c>-1</c> when no selection is active.</summary>
        public int GetSelectionFrom() => HasSelection ? _selectionFrom : -1;
        /// <summary>Zero-based exclusive selection end, or <c>-1</c> when no selection is active.</summary>
        public int GetSelectionTo() => HasSelection ? _selectionTo : -1;
        /// <summary>Returns the retained visual-line offset of the selection start, or <c>-1</c> when there is no selection.</summary>
        public float GetSelectionLineOffset() => HasSelection ? GetTextLineOffset(_selectionFrom) : -1f;
        /// <summary>Returns the selected plain-text projection, matching Godot's <c>get_selected_text()</c>.</summary>
        public string GetSelectedText() => HasSelection ? Text.Substring(_selectionFrom, _selectionTo - _selectionFrom) : string.Empty;
        /// <summary>Selects the complete plain-text document when <see cref="SelectionEnabled"/> is enabled.</summary>
        public void SelectAll()
        {
            if (!SelectionEnabled || string.IsNullOrEmpty(Text)) return;
            _selectionFrom = 0;
            _selectionTo = Text.Length;
        }
        /// <summary>Clears the current selection, matching Godot's <c>deselect()</c>.</summary>
        public void Deselect() { _selectionFrom = -1; _selectionTo = -1; }
        /// <summary>Sets a normalized plain-text selection. This retained-layer convenience is intended for selection gestures.</summary>
        public void Select(int from, int to)
        {
            if (!SelectionEnabled) return;
            from = MathHelper.Clamp(from, 0, Text.Length);
            to = MathHelper.Clamp(to, 0, Text.Length);
            _selectionFrom = Math.Min(from, to);
            _selectionTo = Math.Max(from, to);
            if (_selectionFrom == _selectionTo) Deselect();
        }
        /// <summary>Fallback width for one rendered RichText table cell when no full shaping/layout engine is installed.</summary>
        public int TableCellWidth { get; set; } = 96;
        /// <summary>Color used for metadata links when the span has no explicit foreground color.</summary>
        public Color MetaColor { get; set; } = Color.CornflowerBlue;
        /// <summary>Whether metadata spans use the conventional Godot link underline.</summary>
        public bool MetaUnderline { get; set; } = true;
        public event Action<RichTextLabel, object> MetaClicked;
        /// <summary>Raised after a copy command submits text to <see cref="UIContext.Clipboard"/>.</summary>
        public event Action<RichTextLabel, string> CopyRequested;
        /// <summary>Returns the retained context popup, equivalent to Godot's <c>get_menu()</c>.</summary>
        public PopupMenu GetMenu() => _contextMenu;
        public void AppendText(string text) => AppendSpan(text, CurrentStyle);
        public void PushBold() { var style = CurrentStyle; style.Bold = true; PushStyle(style); }
        public void PushItalics() { var style = CurrentStyle; style.Italic = true; PushStyle(style); }
        public void PushColor(Color color) { var style = CurrentStyle; style.Color = color; PushStyle(style); }
        public void PushBgColor(Color color) { var style = CurrentStyle; style.BackgroundColor = color; PushStyle(style); }
        public void PushUnderline() { var style = CurrentStyle; style.Underline = true; PushStyle(style); }
        public void PushStrikethrough() { var style = CurrentStyle; style.Strikethrough = true; PushStyle(style); }
        /// <summary>Adds a Godot-style horizontal rule paragraph. Percentage dimensions use the content width.</summary>
        public void AddHorizontalRule(int width = 90, int height = 2, Color? color = null, HorizontalAlignment alignment = HorizontalAlignment.Center, bool widthInPercent = true, bool heightInPercent = false)
        {
            if (width < 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 0) throw new ArgumentOutOfRangeException(nameof(height));
            SynchronizeSpansToText();
            _spans.Add(new RichTextSpan(width, height, color ?? Color.White, alignment, widthInPercent, heightInPercent));
            _spanText = Text; FollowToEnd();
            QueueLayout();
        }
        /// <summary>Starts an indented rich-text block. Call <see cref="Pop"/> after appending its content.</summary>
        public void PushIndent(int level = 1)
        {
            var style = CurrentStyle; PushStyle(style);
            AppendSpan(new string(' ', Math.Max(0, level) * 4), style);
        }
        /// <summary>Starts a list item with a Godot-style marker. Call <see cref="Pop"/> after appending its content.</summary>
        public void PushList(int level = 1, RichTextListType type = RichTextListType.Dots, bool capitalize = false, string bullet = "•")
        {
            var style = CurrentStyle; PushStyle(style);
            AppendSpan(new string(' ', Math.Max(0, level - 1) * 4) + GetListMarker(1, type, capitalize, bullet) + " ", style);
        }
        /// <summary>Starts a Godot-style table. Call <see cref="PushCell"/> for each cell and <see cref="Pop"/> to close the table.</summary>
        public void PushTable(int columns, string name = "")
        {
            if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns));
            _tableStack.Add(new TableFrame(columns, name));
        }
        /// <summary>Starts the next cell in the active table, matching Godot's <c>push_cell()</c>.</summary>
        public void PushCell()
        {
            if (_tableStack.Count == 0) throw new InvalidOperationException("PushCell requires an active RichText table.");
            var table = _tableStack[_tableStack.Count - 1];
            if (table.CellCount > 0) AppendSpan(table.CellCount % table.Columns == 0 ? "\n" : "\t", CurrentStyle);
            table.CellCount++;
        }
        public int GetCurrentTableColumn() => _tableStack.Count == 0 ? -1 : _tableStack[_tableStack.Count - 1].CellCount % _tableStack[_tableStack.Count - 1].Columns;
        /// <summary>Starts a Godot-style clickable metadata span. Call <see cref="Pop"/> after appending its text.</summary>
        public void PushMeta(object meta) { var style = CurrentStyle; style.Meta = meta; PushStyle(style); }
        /// <summary>Adds an inline image, corresponding to Godot's <c>add_image()</c> API.</summary>
        public void AddImage(Texture2D image, float width = 0, float height = 0, Color? modulate = null, object metadata = null, string altText = "")
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            SynchronizeSpansToText();
            var size = new Vector2(width > 0 ? width : image.Width, height > 0 ? height : image.Height);
            _spans.Add(new RichTextSpan(image, size, modulate ?? Color.White, metadata ?? CurrentStyle.Meta, altText));
            Text += altText ?? string.Empty; _spanText = Text; FollowToEnd(); QueueLayout();
        }
        public void Pop()
        {
            if (_styleStack.Count > 1) _styleStack.RemoveAt(_styleStack.Count - 1);
            else if (_tableStack.Count > 0) _tableStack.RemoveAt(_tableStack.Count - 1);
        }
        public virtual void AppendBbcode(string bbcode)
        {
            if (!BbcodeEnabled) { AppendText(bbcode); return; }
            ParseAndAppend(bbcode ?? string.Empty);
        }
        public void ParseBbcode(string bbcode) { Clear(); AppendBbcode(bbcode); }
        public void Clear() { _spans.Clear(); _metaRegions.Clear(); _styleStack.Clear(); _styleStack.Add(default); _tableStack.Clear(); _spanText = string.Empty; Deselect(); _selectionAnchor = -1; _selectionMode = RichTextSelectionMode.SingleClick; _selectingText = false; _selectionAutoScrollRemainder = 0; _scrollOffset = 0; Text = string.Empty; QueueLayout(); }
        public override Vector2 GetMinimumSize()
        {
            // Text may carry internal '\t'/table-column markers; SpriteFont only special-cases '\r'/'\n'
            // during measurement, so those markers are substituted with a space before measuring here.
            // The real render/hit-test paths already interpret '\t' via their own tab-stop math and are unaffected.
            var measuredText = Text.IndexOf('\t') >= 0 ? Text.Replace('\t', ' ') : Text;
            if (!FitContent)
            {
                var textSize = EffectiveUIFont == null || string.IsNullOrEmpty(measuredText) ? Vector2.Zero : CreateTextLayout(measuredText, new TextLayoutOptions()).Size;
                if (AutowrapMode != LabelAutowrapMode.Off && EffectiveUIFont != null && !string.IsNullOrEmpty(measuredText)) textSize.X = 0;
                return Vector2.Max(CustomMinimumSize, textSize + new Vector2(Padding.Horizontal, Padding.Vertical));
            }
            var text = EffectiveUIFont == null ? Vector2.Zero : CreateTextLayout(measuredText, new TextLayoutOptions()).Size;
            return Vector2.Max(CustomMinimumSize, new Vector2(text.X + Padding.Horizontal, GetContentHeight() + Padding.Vertical));
        }
        /// <summary>Returns the document's retained visual line count. Explicit and automatic wrapped lines are included.</summary>
        public int GetLineCount() => GetVisualLines().Count;
        /// <summary>Returns the number of explicit newline-delimited document paragraphs.</summary>
        public int GetParagraphCount() => GetExplicitParagraphCount();
        /// <summary>Returns the number of explicit paragraphs intersecting the clipped document viewport.</summary>
        public int GetVisibleParagraphCount()
        {
            var visible = new HashSet<int>();
            var top = ScrollOffset; var bottom = top + GetViewportHeight();
            foreach (var line in GetVisualLines()) if (line.Bottom > top && line.Y < bottom) visible.Add(line.Paragraph);
            return visible.Count;
        }
        /// <summary>Returns the content-space offset of a visual line, or zero for an invalid line.</summary>
        public float GetLineOffset(int line)
        {
            var lines = GetVisualLines();
            return line >= 0 && line < lines.Count ? lines[line].Y : 0;
        }
        /// <summary>Returns the content-space offset of an explicit paragraph, or zero for an invalid paragraph.</summary>
        public float GetParagraphOffset(int paragraph)
        {
            foreach (var line in GetVisualLines()) if (line.Paragraph == paragraph) return line.Y;
            return 0;
        }
        /// <summary>Returns the retained plain-text range covered by a visual line, using <see cref="Point.X"/> for the inclusive start and <see cref="Point.Y"/> for the exclusive end.</summary>
        public Point GetLineRange(int line)
        {
            var lines = GetVisualLines();
            return line >= 0 && line < lines.Count ? new Point(lines[line].Start, lines[line].End) : Point.Zero;
        }
        /// <summary>Returns the number of visual lines intersecting the clipped document viewport.</summary>
        public int GetVisibleLineCount()
        {
            var top = ScrollOffset; var bottom = top + GetViewportHeight(); var visible = 0;
            foreach (var line in GetVisualLines()) if (line.Bottom > top && line.Y < bottom) visible++;
            return visible;
        }
        /// <summary>Returns the document's retained content height in pixels.</summary>
        public int GetContentHeight() => GetDocumentMetrics().ContentHeight;
        /// <summary>Returns the widest retained visual-line width in pixels.</summary>
        public int GetContentWidth()
        {
            var width = 0;
            foreach (var line in GetVisualLines()) width = Math.Max(width, line.Width);
            return width;
        }
        /// <summary>Returns the retained height of a visual line, or zero for an invalid line.</summary>
        public int GetLineHeight(int line)
        {
            var lines = GetVisualLines();
            return line >= 0 && line < lines.Count ? lines[line].Height : 0;
        }
        /// <summary>Returns the retained width of a visual line, or zero for an invalid line.</summary>
        public int GetLineWidth(int line)
        {
            var lines = GetVisualLines();
            return line >= 0 && line < lines.Count ? lines[line].Width : 0;
        }
        /// <summary>Returns the maximum available vertical offset for the current bounds.</summary>
        public int GetScrollMaximum() => Math.Max(0, GetContentHeight() - GetViewportHeight());
        /// <summary>Returns the screen-space clipped content viewport, corresponding to Godot's <c>get_visible_content_rect()</c>.</summary>
        public Rectangle GetVisibleContentRect() => GetContentBounds();
        public void SetScrollActive(bool active) => ScrollActive = active;
        public bool IsScrollActive() => ScrollActive;
        public void SetScrollFollow(bool following) { ScrollFollowing = following; if (following) FollowToEnd(); }
        public bool IsScrollFollowing() => ScrollFollowing;
        public int GetScrollOffset() => ScrollOffset;
        public void SetScrollOffset(int offset) => SetScrollOffsetCore(offset);
        /// <summary>Returns the retained vertical scrollbar, corresponding to Godot's <c>get_v_scroll_bar()</c>.</summary>
        public VScrollBar GetVScrollBar() => _verticalScrollBar;
        public void SetDeselectOnFocusLossEnabled(bool enabled) => DeselectOnFocusLossEnabled = enabled;
        public bool IsDeselectOnFocusLossEnabled() => DeselectOnFocusLossEnabled;
        public void SetDragAndDropSelectionEnabled(bool enabled) => DragAndDropSelectionEnabled = enabled;
        public bool IsDragAndDropSelectionEnabled() => DragAndDropSelectionEnabled;
        /// <summary>Scrolls the requested zero-based visual line to the top of the retained viewport.</summary>
        public void ScrollToLine(int line)
        {
            if (line <= 0) { SetScrollOffsetCore(0); return; }
            SetScrollOffsetCore((int)MathF.Floor(GetLineOffset(line)));
        }
        /// <summary>Scrolls the requested zero-based explicit paragraph to the top of the retained viewport.</summary>
        public void ScrollToParagraph(int paragraph) => SetScrollOffsetCore((int)MathF.Floor(GetParagraphOffset(Math.Max(0, paragraph))));
        /// <summary>Scrolls the visual line containing the selection start to the top of the document viewport.</summary>
        public void ScrollToSelection()
        {
            var lineOffset = GetSelectionLineOffset();
            if (lineOffset >= 0) SetScrollOffsetCore((int)MathF.Floor(lineOffset));
        }
        /// <summary>Finds case-insensitive plain-text content, selects the match, and reveals its visual line; optionally continues from the active selection or searches backward.</summary>
        public bool Search(string text, bool fromSelection = false, bool searchPrevious = false)
        {
            if (!SelectionEnabled) return false;
            if (string.IsNullOrEmpty(text)) { Deselect(); return false; }
            var found = -1;
            if (fromSelection && HasSelection)
            {
                found = searchPrevious
                    ? FindPrevious(text, _selectionFrom - 1)
                    : Text.IndexOf(text, _selectionTo, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                    found = searchPrevious ? FindPrevious(text, Text.Length - text.Length) : Text.IndexOf(text, StringComparison.OrdinalIgnoreCase);
            }
            else found = searchPrevious ? FindPrevious(text, Text.Length - text.Length) : Text.IndexOf(text, StringComparison.OrdinalIgnoreCase);
            if (found < 0) return false;
            Select(found, found + text.Length);
            ScrollToSelection();
            return true;
        }
        internal void DrawRichText(UIRenderContext context)
        {
            EnsurePlainTextSpan();
            BuildMetaRegions();
            if (_spans.Count > 0)
            {
                if (ScrollActive) context.PushClip(GetContentBounds());
                try
                {
                    var origin = GlobalPosition + new Vector2(Padding.Left, Padding.Top - ScrollOffset);
                    var cursor = origin;
                    var right = GetContentBounds().Right;
                    var lineHeight = EffectiveUIFont == null ? 16 : Math.Max(1, TextMetrics.LineHeight(EffectiveUIFont));
                    var textIndex = 0;
                    foreach (var span in _spans)
                    {
                        if (span.IsHorizontalRule)
                        {
                            DrawHorizontalRule(context, span, origin, right, lineHeight, ref cursor);
                            continue;
                        }
                        if (EffectiveUIFont == null) continue;
                        var color = Enabled ? span.Color ?? (span.Meta != null ? MetaColor : FontColor ?? context.Theme.TextColor) : context.Theme.DisabledTextColor;
                        if (span.Image != null)
                        {
                            var size = new Vector2(Math.Max(1, span.ImageSize.X), Math.Max(1, span.ImageSize.Y));
                            if (Autowrap && cursor.X > origin.X && cursor.X + size.X > right) { cursor.X = origin.X; cursor.Y += lineHeight; }
                            var imageRect = new Rectangle((int)cursor.X, (int)(cursor.Y + Math.Max(0, (lineHeight - size.Y) / 2)), (int)size.X, (int)size.Y);
                            context.SpriteBatch.Draw(span.Image, imageRect, span.ImageModulate);
                            cursor.X += size.X;
                            textIndex += span.Text.Length;
                            continue;
                        }
                        for (var offset = 0; offset < span.Text.Length;)
                        {
                            var character = span.Text[offset];
                            if (character == '\r') { offset++; textIndex++; continue; }
                            if (character == '\n') { cursor.X = origin.X; cursor.Y += lineHeight; offset++; textIndex++; continue; }
                            if (character == '\t') { cursor.X = origin.X + Math.Max(1, (int)MathF.Floor((cursor.X - origin.X) / Math.Max(1, TableCellWidth)) + 1) * Math.Max(1, TableCellWidth); offset++; textIndex++; continue; }
                            var end = offset + 1;
                            while (end < span.Text.Length && span.Text[end] != '\r' && span.Text[end] != '\n' && span.Text[end] != '\t') end++;
                            var chunk = span.Text.Substring(offset, end - offset);
                            var direction = TextDirection == TextDirection.Inherited ? TextDirection.Auto : TextDirection;
                            var layout = CreateTextLayout(EffectiveUIFont, chunk, new TextLayoutOptions(direction: direction, locale: Language));
                            if (Autowrap && cursor.X > origin.X && cursor.X + layout.Size.X > right)
                            {
                                var prefixLength = GetFittingWrapPrefixLength(chunk, Math.Max(1, right - cursor.X), direction);
                                if (prefixLength > 0)
                                {
                                    var prefixLayout = CreateTextLayout(EffectiveUIFont, chunk.Substring(0, prefixLength), new TextLayoutOptions(direction: direction, locale: Language));
                                    DrawRichTextLayout(context, prefixLayout, cursor, span, color, textIndex);
                                    offset += prefixLength;
                                    textIndex += prefixLength;
                                }
                                cursor.X = origin.X;
                                cursor.Y += lineHeight;
                                if (prefixLength > 0) continue;
                            }
                            if (Autowrap && layout.Size.X > right - origin.X)
                            {
                                var wrapping = AutowrapMode == LabelAutowrapMode.Arbitrary ? TextWrapping.Character : TextWrapping.Word;
                                layout = CreateTextLayout(EffectiveUIFont, chunk, new TextLayoutOptions(Math.Max(1, right - origin.X), wrapping, direction: direction, locale: Language));
                            }
                            DrawRichTextLayout(context, layout, cursor, span, color, textIndex);
                            var lastLine = layout.Lines[layout.Lines.Count - 1];
                            cursor.X += lastLine.Origin.X + lastLine.Size.X;
                            cursor.Y += lastLine.Origin.Y;
                            offset = end;
                            textIndex += chunk.Length;
                        }
                    }
                }
                finally { if (ScrollActive) context.PopClip(); }
            }
        }
        internal int GetFittingWrapPrefixLength(string text, float availableWidth, TextDirection direction = TextDirection.Auto)
        {
            if (EffectiveUIFont == null || string.IsNullOrEmpty(text) || !float.IsFinite(availableWidth) || availableWidth <= 0) return 0;
            if (AutowrapMode == LabelAutowrapMode.Arbitrary)
            {
                var layout = CreateTextLayout(EffectiveUIFont, text, new TextLayoutOptions(availableWidth, TextWrapping.Character, direction: direction, locale: Language));
                return layout.Lines.Count > 1 ? layout.Lines[0].Length : 0;
            }
            var fittingLength = 0;
            foreach (var opportunity in UnicodeLineBreaker.GetUtf16BreakOpportunities(text))
            {
                if (opportunity >= text.Length) break;
                var prefix = text.Substring(0, opportunity);
                var width = CreateTextLayout(EffectiveUIFont, prefix, new TextLayoutOptions(direction: direction, locale: Language)).Size.X;
                if (width > availableWidth) break;
                fittingLength = opportunity;
            }
            return fittingLength;
        }
        private void DrawRichTextLayout(UIRenderContext context, TextLayout layout, Vector2 position, RichTextSpan span, Color color, int textIndex)
        {
            var fullRange = layout.GetSelectionRectangles(0, layout.Text.Length);
            if (span.BackgroundColor.HasValue)
                foreach (var rectangle in fullRange) FillLayoutRectangle(context, position, rectangle, span.BackgroundColor.Value);
            if (HasSelection)
            {
                var start = Math.Max(0, _selectionFrom - textIndex);
                var end = Math.Min(layout.Text.Length, _selectionTo - textIndex);
                if (end > start)
                    foreach (var rectangle in layout.GetSelectionRectangles(start, end - start)) FillLayoutRectangle(context, position, rectangle, SelectionColor);
            }
            context.Text(layout, position, color);
            if (span.Bold) context.Text(layout, position + Vector2.UnitX, color);
            foreach (var rectangle in fullRange)
            {
                if (span.Underline || span.Meta != null && MetaUnderline)
                    context.Fill(new Rectangle((int)MathF.Floor(position.X + rectangle.X), (int)MathF.Ceiling(position.Y + rectangle.Bottom) - 2, Math.Max(1, (int)MathF.Ceiling(rectangle.Width)), 1), color);
                if (span.Strikethrough)
                    context.Fill(new Rectangle((int)MathF.Floor(position.X + rectangle.X), (int)MathF.Round(position.Y + rectangle.Y + rectangle.Height / 2), Math.Max(1, (int)MathF.Ceiling(rectangle.Width)), 1), color);
            }
        }
        private static void FillLayoutRectangle(UIRenderContext context, Vector2 position, RectangleF rectangle, Color color)
        {
            context.Fill(new Rectangle((int)MathF.Floor(position.X + rectangle.X), (int)MathF.Floor(position.Y + rectangle.Y), Math.Max(1, (int)MathF.Ceiling(rectangle.Width)), Math.Max(1, (int)MathF.Ceiling(rectangle.Height))), color);
        }
        internal override void PointerPressed(Point position)
        {
            var meta = GetMetaUnderPosition(position);
            if (meta != null) { MetaClicked?.Invoke(this, meta); return; }
            base.PointerPressed(position);
            if (!SelectionEnabled) return;
            _selectionAnchor = GetTextIndexAtPosition(position);
            _selectionDragAttempt = false;
            _selectingText = _selectionAnchor >= 0;
            _lastSelectionPointerPosition = position;
            _selectionAutoScrollRemainder = 0;
            if (!_selectingText) return;
            var clickTime = Context?.CurrentTime ?? TimeSpan.Zero;
            var withinTimeout = _lastTextClickTime != TimeSpan.MinValue && clickTime - _lastTextClickTime <= MultiClickTimeout;
            var withinTolerance = Vector2.DistanceSquared(position.ToVector2(), _lastTextClickPosition.ToVector2()) <= MultiClickTolerance * MultiClickTolerance;
            _textClickCount = withinTimeout && withinTolerance ? Math.Min(3, _textClickCount + 1) : 1;
            _lastTextClickTime = clickTime;
            _lastTextClickPosition = position;
            _selectionMode = _textClickCount == 2 ? RichTextSelectionMode.DoubleClick : _textClickCount == 3 ? RichTextSelectionMode.TripleClick : RichTextSelectionMode.SingleClick;
            if (_selectionMode == RichTextSelectionMode.SingleClick && DragAndDropSelectionEnabled && HasSelection && _selectionAnchor >= _selectionFrom && _selectionAnchor < _selectionTo)
            {
                _selectionDragAttempt = true;
                _selectingText = false;
                return;
            }
            if (_selectionMode == RichTextSelectionMode.SingleClick) Deselect();
            else SelectGestureRange(_selectionAnchor, _selectionAnchor);
            if (_selectionMode == RichTextSelectionMode.TripleClick) _textClickCount = 0;
        }
        internal override void PointerMoved(Point position)
        {
            if (!_selectingText) return;
            _lastSelectionPointerPosition = position;
            var index = GetTextIndexAtPosition(position);
            if (index >= 0) SelectGestureRange(_selectionAnchor, index);
        }
        internal override void PointerReleased(Point position, bool isInside)
        {
            if (_selectionDragAttempt)
            {
                var index = GetTextIndexAtPosition(position);
                if (index >= _selectionFrom && index < _selectionTo) Deselect();
                _selectionDragAttempt = false;
                _selectionAnchor = -1;
                _selectionMode = RichTextSelectionMode.SingleClick;
                _textClickCount = 0;
                _lastTextClickTime = TimeSpan.MinValue;
                _selectionAutoScrollRemainder = 0;
                base.PointerReleased(position, isInside);
                return;
            }
            if (_selectingText)
            {
                var index = GetTextIndexAtPosition(position);
                if (index >= 0) SelectGestureRange(_selectionAnchor, index);
            }
            _selectionAnchor = -1;
            _selectionMode = RichTextSelectionMode.SingleClick;
            _selectingText = false;
            _selectionDragAttempt = false;
            _selectionAutoScrollRemainder = 0;
            base.PointerReleased(position, isInside);
        }
        internal override void CancelInput()
        {
            _selectingText = false;
            _selectionDragAttempt = false;
            _selectionAnchor = -1;
            _selectionMode = RichTextSelectionMode.SingleClick;
            _selectionAutoScrollRemainder = 0;
            _textClickCount = 0;
            _lastTextClickTime = TimeSpan.MinValue;
            base.CancelInput();
        }
        internal override void FocusLost()
        {
            if (DeselectOnFocusLossEnabled && !_contextMenu.Visible) Deselect();
            base.FocusLost();
        }
        /// <summary>Returns the selected text when a retained drag begins from an existing selection.</summary>
        public override object GetDragData(Point position)
        {
            if (!_selectionDragAttempt || !SelectionEnabled) return null;
            _selectionDragAttempt = false;
            _textClickCount = 0;
            _lastTextClickTime = TimeSpan.MinValue;
            return GetSelectedText();
        }
        internal override void Process(GameTime gameTime)
        {
            if (_selectingText && SelectionEnabled && SelectionAutoScrollEnabled && ScrollActive && GetScrollMaximum() > 0)
                UpdateSelectionAutoScroll(gameTime?.ElapsedGameTime ?? TimeSpan.Zero);
            base.Process(gameTime);
        }
        protected override void ArrangeChildren()
        {
            base.ArrangeChildren();
            SynchronizeVerticalScrollBar();
        }
        internal override bool PointerWheel(int delta)
        {
            if (!ScrollActive || delta == 0 || GetScrollMaximum() <= 0) return false;
            var step = ScrollStep > 0 ? ScrollStep : GetDocumentMetrics().LineHeight;
            var previous = ScrollOffset;
            SetScrollOffsetCore(previous - Math.Sign(delta) * Math.Max(1, step));
            return previous != ScrollOffset;
        }
        internal override void PointerRightPressed(Point position)
        {
            OpenContextMenu(position);
        }
        private void OpenContextMenu(Point position)
        {
            if (!ContextMenuEnabled || Context == null) return;
            _contextMenu.Clear();
            _contextMenu.Font = Font;
            _contextMenu.AddItem("Copy", ContextMenuCopyId).Disabled = !SelectionEnabled;
            _contextMenu.AddItem("Select All", ContextMenuSelectAllId).Disabled = !SelectionEnabled;
            if (_contextMenu.Context != Context) Context.Add(_contextMenu);
            _contextMenu.PopupAt(position.ToVector2(), null);
        }
        internal override void KeyPressed(Keys key)
        {
            if (_verticalScrollBar.Visible)
            {
                var lineStep = Math.Max(1, GetDocumentMetrics().LineHeight);
                if (key == Keys.PageUp) { SetScrollOffsetCore(ScrollOffset - GetViewportHeight()); return; }
                if (key == Keys.PageDown) { SetScrollOffsetCore(ScrollOffset + GetViewportHeight()); return; }
                if (key == Keys.Up) { SetScrollOffsetCore(ScrollOffset - lineStep); return; }
                if (key == Keys.Down) { SetScrollOffsetCore(ScrollOffset + lineStep); return; }
                if (key == Keys.Home) { SetScrollOffsetCore(0); return; }
                if (key == Keys.End) { SetScrollOffsetCore(GetScrollMaximum()); return; }
            }

            if (ShortcutKeysEnabled && HasCommandModifier())
            {
                if (key == Keys.A)
                {
                    SelectAll();
                    return;
                }
                if (key == Keys.C)
                {
                    var selectedText = GetSelectedText();
                    WriteClipboard(selectedText);
                }
            }
            if (key == Keys.Apps || (key == Keys.F10 && HasShiftModifier()))
                OpenContextMenu(new Point((int)MathF.Floor(GlobalPosition.X), (int)MathF.Floor(GlobalPosition.Y)));
        }
        private void WriteClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Context?.Clipboard?.SetText(text);
            CopyRequested?.Invoke(this, text);
        }
        /// <summary>Returns metadata at the given screen-space position, equivalent to Godot's <c>get_meta_under_mouse()</c>.</summary>
        public object GetMetaUnderPosition(Point position)
        {
            BuildMetaRegions();
            for (var index = _metaRegions.Count - 1; index >= 0; index--) if (_metaRegions[index].Bounds.Contains(position)) return _metaRegions[index].Meta;
            return null;
        }
        private void SelectGestureRange(int anchor, int target)
        {
            if (_selectionMode == RichTextSelectionMode.SingleClick) { Select(anchor, target); return; }
            var first = Math.Min(anchor, target); var last = Math.Max(anchor, target);
            if (_selectionMode == RichTextSelectionMode.DoubleClick) Select(GetWordStart(first), GetWordEnd(last));
            else Select(GetParagraphStart(first), GetParagraphEnd(last));
        }
        private void UpdateSelectionAutoScroll(TimeSpan elapsed)
        {
            var pointer = _lastSelectionPointerPosition;
            var delta = 0f;
            if (pointer.Y < Bounds.Top) delta = -1f * (1f + (Bounds.Top - pointer.Y) / 15f);
            else if (pointer.Y >= Bounds.Bottom) delta = 1f * (1f + (pointer.Y - Bounds.Bottom + 1) / 15f);
            if (delta == 0f) { _selectionAutoScrollRemainder = 0; return; }

            _selectionAutoScrollRemainder += delta * Math.Max(0f, SelectionAutoScrollSpeed) * (float)elapsed.TotalSeconds;
            var pixels = (int)MathF.Truncate(_selectionAutoScrollRemainder);
            if (pixels == 0) return;
            _selectionAutoScrollRemainder -= pixels;
            var previous = ScrollOffset;
            SetScrollOffsetCore(previous + pixels);
            if (ScrollOffset == previous) return;

            var clampedPosition = new Point(
                MathHelper.Clamp(pointer.X, Bounds.Left, Math.Max(Bounds.Left, Bounds.Right - 1)),
                MathHelper.Clamp(pointer.Y, Bounds.Top, Math.Max(Bounds.Top, Bounds.Bottom - 1)));
            var index = GetTextIndexAtPosition(clampedPosition);
            if (index >= 0) SelectGestureRange(_selectionAnchor, index);
        }
        private int GetWordStart(int index)
        {
            index = MathHelper.Clamp(index, 0, Text.Length);
            if (index == Text.Length || !IsWordCharacter(Text[index])) return index;
            while (index > 0 && IsWordCharacter(Text[index - 1])) index--;
            return index;
        }
        private int GetWordEnd(int index)
        {
            index = MathHelper.Clamp(index, 0, Text.Length);
            if (index > 0 && (index == Text.Length || !IsWordCharacter(Text[index]))) index--;
            if (index < 0 || index >= Text.Length || !IsWordCharacter(Text[index])) return MathHelper.Clamp(index + 1, 0, Text.Length);
            while (index < Text.Length && IsWordCharacter(Text[index])) index++;
            return index;
        }
        private int GetParagraphStart(int index)
        {
            index = MathHelper.Clamp(index, 0, Text.Length);
            var lineBreak = Text.LastIndexOf('\n', Math.Max(0, index - 1));
            return lineBreak < 0 ? 0 : lineBreak + 1;
        }
        private int GetParagraphEnd(int index)
        {
            index = MathHelper.Clamp(index, 0, Text.Length);
            var lineBreak = Text.IndexOf('\n', index);
            return lineBreak < 0 ? Text.Length : lineBreak;
        }
        private static bool IsWordCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';
        private int GetExplicitParagraphCount()
        {
            if (_spans.Count == 0) return 0;
            var paragraphs = 1;
            foreach (var character in Text) if (character == '\n') paragraphs++;
            return paragraphs;
        }
        private int FindPrevious(string text, int startIndex)
        {
            if (startIndex < 0 || Text.Length == 0) return -1;
            startIndex = Math.Min(startIndex, Text.Length - 1);
            return Text.LastIndexOf(text, startIndex, StringComparison.OrdinalIgnoreCase);
        }
        private int GetTextLineOffset(int targetIndex)
        {
            EnsurePlainTextSpan();
            targetIndex = MathHelper.Clamp(targetIndex, 0, Text.Length);
            var lineHeight = EffectiveUIFont == null ? 16 : Math.Max(1, TextMetrics.LineHeight(EffectiveUIFont));
            var availableWidth = Math.Max(1, GetContentBounds().Width);
            var x = 0f;
            var y = 0f;
            var textIndex = 0;
            foreach (var span in _spans)
            {
                if (span.IsHorizontalRule)
                {
                    if (x > 0) { x = 0; y += lineHeight; }
                    y += Math.Max(lineHeight, GetHorizontalRuleHeight(span, availableWidth));
                    continue;
                }
                if (span.Image != null)
                {
                    var width = Math.Max(1, span.ImageSize.X);
                    if (Autowrap && x > 0 && x + width > availableWidth) { x = 0; y += lineHeight; }
                    if (targetIndex <= textIndex) return (int)y;
                    x += width;
                    textIndex += span.Text.Length;
                    continue;
                }
                foreach (var unit in GetTextUnits(span.Text))
                {
                    if (targetIndex <= textIndex) return (int)y;
                    var character = span.Text[unit.Start];
                    if (character == '\r') { textIndex++; continue; }
                    if (character == '\n') { x = 0; y += lineHeight; textIndex++; continue; }
                    var width = character == '\t'
                        ? Math.Max(1, (int)(Math.Max(1, (int)MathF.Floor(x / Math.Max(1, TableCellWidth)) + 1) * Math.Max(1, TableCellWidth) - x))
                        : unit.Width;
                    if (Autowrap && x > 0 && x + width > availableWidth) { x = 0; y += lineHeight; }
                    x += width;
                    textIndex += unit.Length;
                }
            }
            return (int)y;
        }
        private int GetTextIndexAtPosition(Point position)
        {
            EnsurePlainTextSpan();
            if (!Bounds.Contains(position) || _spans.Count == 0) return -1;
            var origin = GlobalPosition + new Vector2(Padding.Left, Padding.Top - ScrollOffset);
            var cursor = origin;
            var right = GetContentBounds().Right;
            var lineHeight = EffectiveUIFont == null ? 16 : Math.Max(1, TextMetrics.LineHeight(EffectiveUIFont));
            var textIndex = 0;
            foreach (var span in _spans)
            {
                if (span.IsHorizontalRule)
                {
                    var ruleTop = cursor.Y + (cursor.X > origin.X ? lineHeight : 0);
                    var ruleLineHeight = Math.Max(lineHeight, GetHorizontalRuleHeight(span, right - origin.X));
                    if (position.Y >= ruleTop && position.Y < ruleTop + ruleLineHeight) return textIndex;
                    cursor.X = origin.X; cursor.Y = ruleTop + ruleLineHeight;
                    continue;
                }
                if (span.Image != null)
                {
                    var size = new Vector2(Math.Max(1, span.ImageSize.X), Math.Max(1, span.ImageSize.Y));
                    if (Autowrap && cursor.X > origin.X && cursor.X + size.X > right) { cursor.X = origin.X; cursor.Y += lineHeight; }
                    if (position.Y >= cursor.Y && position.Y < cursor.Y + lineHeight && position.X >= cursor.X && position.X < cursor.X + size.X) return position.X < cursor.X + size.X / 2 ? textIndex : textIndex + span.Text.Length;
                    cursor.X += size.X;
                    textIndex += span.Text.Length;
                    continue;
                }
                foreach (var unit in GetTextUnits(span.Text))
                {
                    var character = span.Text[unit.Start];
                    if (character == '\r') { textIndex++; continue; }
                    if (character == '\n')
                    {
                        if (position.Y >= cursor.Y && position.Y < cursor.Y + lineHeight) return position.X < cursor.X ? textIndex : textIndex + 1;
                        cursor.X = origin.X; cursor.Y += lineHeight; textIndex++; continue;
                    }
                    var width = character == '\t'
                        ? Math.Max(1, (int)(origin.X + Math.Max(1, (int)MathF.Floor((cursor.X - origin.X) / Math.Max(1, TableCellWidth)) + 1) * Math.Max(1, TableCellWidth) - cursor.X))
                        : unit.Width;
                    if (Autowrap && cursor.X > origin.X && cursor.X + width > right) { cursor.X = origin.X; cursor.Y += lineHeight; }
                    if (position.Y >= cursor.Y && position.Y < cursor.Y + lineHeight && position.X >= cursor.X && position.X < cursor.X + width) return position.X < cursor.X + width / 2f ? textIndex : textIndex + unit.Length;
                    cursor.X += width;
                    textIndex += unit.Length;
                }
            }
            return position.Y >= cursor.Y && position.Y < cursor.Y + lineHeight ? textIndex : -1;
        }
        private static void DrawHorizontalRule(UIRenderContext context, RichTextSpan span, Vector2 origin, float right, float lineHeight, ref Vector2 cursor)
        {
            if (cursor.X > origin.X) { cursor.X = origin.X; cursor.Y += lineHeight; }
            var availableWidth = Math.Max(0, (int)MathF.Floor(right - origin.X));
            var width = span.RuleWidthInPercent ? (int)MathF.Round(availableWidth * MathHelper.Clamp(span.RuleWidth, 0, 100) / 100f) : span.RuleWidth;
            width = Math.Max(0, Math.Min(availableWidth, width));
            var height = GetHorizontalRuleHeight(span, availableWidth);
            var x = (int)origin.X;
            if (span.RuleAlignment == HorizontalAlignment.Center) x += Math.Max(0, (availableWidth - width) / 2);
            else if (span.RuleAlignment == HorizontalAlignment.Right) x += Math.Max(0, availableWidth - width);
            if (width > 0 && height > 0) context.Fill(new Rectangle(x, (int)(cursor.Y + Math.Max(0, (lineHeight - height) / 2)), width, height), span.RuleColor ?? context.Theme.TextColor);
            cursor.X = origin.X;
            cursor.Y += Math.Max(lineHeight, height);
        }
        private static int GetHorizontalRuleHeight(RichTextSpan span, float contentWidth)
        {
            return span.RuleHeightInPercent ? Math.Max(0, (int)MathF.Round(Math.Max(0, contentWidth) * span.RuleHeight / 100f)) : span.RuleHeight;
        }
        private List<VisualLine> GetVisualLines()
        {
            EnsurePlainTextSpan();
            var lines = new List<VisualLine>();
            var lineHeight = EffectiveUIFont == null ? 16 : Math.Max(1, TextMetrics.LineHeight(EffectiveUIFont));
            var availableWidth = Math.Max(1, GetContentBounds().Width);
            var x = 0f; var y = 0; var start = 0; var textIndex = 0; var paragraph = 0; var active = false;
            void BeginLine() { if (!active) { start = textIndex; active = true; } }
            void FinishLine(int end, int height = 0)
            {
                if (!active) return;
                height = Math.Max(lineHeight, height);
                lines.Add(new VisualLine(start, end, paragraph, y, height, Math.Max(0, (int)MathF.Ceiling(x))));
                y += height; x = 0; active = false;
            }
            foreach (var span in _spans)
            {
                if (span.IsHorizontalRule)
                {
                    FinishLine(textIndex);
                    var width = span.RuleWidthInPercent ? (int)MathF.Round(availableWidth * MathHelper.Clamp(span.RuleWidth, 0, 100) / 100f) : span.RuleWidth;
                    var height = Math.Max(lineHeight, GetHorizontalRuleHeight(span, availableWidth));
                    lines.Add(new VisualLine(textIndex, textIndex, paragraph, y, height, Math.Max(0, Math.Min((int)availableWidth, width))));
                    y += height;
                    continue;
                }
                if (span.Image != null)
                {
                    var width = Math.Max(1, span.ImageSize.X);
                    if (Autowrap && x > 0 && x + width > availableWidth) FinishLine(textIndex);
                    BeginLine(); x += width; textIndex += span.Text.Length;
                    continue;
                }
                foreach (var unit in GetTextUnits(span.Text))
                {
                    var character = span.Text[unit.Start];
                    if (character == '\r') { textIndex++; continue; }
                    if (character == '\n') { BeginLine(); FinishLine(textIndex); textIndex++; paragraph++; continue; }
                    var width = character == '\t'
                        ? Math.Max(1, (int)(Math.Max(1, (int)MathF.Floor(x / Math.Max(1, TableCellWidth)) + 1) * Math.Max(1, TableCellWidth) - x))
                        : unit.Width;
                    if (Autowrap && x > 0 && x + width > availableWidth) FinishLine(textIndex);
                    BeginLine(); x += width; textIndex += unit.Length;
                }
            }
            FinishLine(textIndex);
            return lines;
        }
        private DocumentMetrics GetDocumentMetrics()
        {
            EnsurePlainTextSpan();
            var lineHeight = EffectiveUIFont == null ? 16 : Math.Max(1, TextMetrics.LineHeight(EffectiveUIFont));
            var availableWidth = Math.Max(1, GetContentBounds().Width);
            var x = 0f; var y = 0f; var lines = 0; var lineActive = false;
            void BeginLine()
            {
                if (lineActive) return;
                lineActive = true;
                lines++;
            }
            void AdvanceLine()
            {
                BeginLine();
                y += lineHeight;
                x = 0;
                lineActive = false;
            }
            foreach (var span in _spans)
            {
                if (span.IsHorizontalRule)
                {
                    if (lineActive) AdvanceLine();
                    lines++;
                    y += Math.Max(lineHeight, GetHorizontalRuleHeight(span, availableWidth));
                    x = 0;
                    continue;
                }
                if (span.Image != null)
                {
                    var width = Math.Max(1, span.ImageSize.X);
                    if (Autowrap && x > 0 && x + width > availableWidth) AdvanceLine();
                    BeginLine();
                    x += width;
                    continue;
                }
                foreach (var unit in GetTextUnits(span.Text))
                {
                    var character = span.Text[unit.Start];
                    if (character == '\r') continue;
                    if (character == '\n') { AdvanceLine(); continue; }
                    var width = character == '\t'
                        ? Math.Max(1, (int)(Math.Max(1, (int)MathF.Floor(x / Math.Max(1, TableCellWidth)) + 1) * Math.Max(1, TableCellWidth) - x))
                        : unit.Width;
                    if (Autowrap && x > 0 && x + width > availableWidth) AdvanceLine();
                    BeginLine();
                    x += width;
                }
            }
            return new DocumentMetrics(Math.Max(0, lines), Math.Max(0, (int)MathF.Ceiling(y + (lineActive ? lineHeight : 0))), lineHeight);
        }
        private int GetViewportHeight() => Math.Max(0, (int)MathF.Floor(Bounds.Height - Padding.Vertical));
        private Rectangle GetContentBounds()
        {
            var scrollbarWidth = _verticalScrollBar.Visible ? (int)MathF.Ceiling(_verticalScrollBar.GetMinimumSize().X) : 0;
            var x = (int)(GlobalPosition.X + Padding.Left + (IsLayoutRtl() ? scrollbarWidth : 0));
            return new Rectangle(x, (int)(GlobalPosition.Y + Padding.Top), Math.Max(0, (int)MathF.Floor(Bounds.Width - Padding.Horizontal - scrollbarWidth)), GetViewportHeight());
        }
        private void SynchronizeVerticalScrollBar()
        {
            var thickness = Math.Max(1, (int)MathF.Ceiling(_verticalScrollBar.GetMinimumSize().X));
            var shouldShow = ScrollActive && Bounds.Width > thickness && GetScrollMaximum() > 0;
            var visibilityChanged = _verticalScrollBar.Visible != shouldShow;
            _verticalScrollBar.Visible = shouldShow;
            _verticalScrollBar.Position = new Vector2(IsLayoutRtl() ? 0 : Math.Max(0, Size.X - thickness), 0);
            _verticalScrollBar.Size = new Vector2(thickness, Math.Max(0, Size.Y));
            _syncingVerticalScrollBar = true;
            _verticalScrollBar.MinValue = 0;
            _verticalScrollBar.MaxValue = Math.Max(0, GetContentHeight());
            _verticalScrollBar.Page = GetViewportHeight();
            _verticalScrollBar.SetValueNoSignal(ScrollOffset);
            _syncingVerticalScrollBar = false;
            if (visibilityChanged) QueueLayout();
        }
        private int ClampScrollOffset(int offset) => MathHelper.Clamp(offset, 0, GetScrollMaximum());
        private void SetScrollOffsetCore(int offset)
        {
            var clamped = ClampScrollOffset(offset);
            if (_scrollOffset == clamped) return;
            _scrollOffset = clamped;
            SynchronizeVerticalScrollBar();
            QueueLayout();
        }
        private void FollowToEnd()
        {
            if (ScrollFollowing) _scrollOffset = GetScrollMaximum();
        }
        private readonly struct DocumentMetrics
        {
            public DocumentMetrics(int lineCount, int contentHeight, int lineHeight) { LineCount = lineCount; ContentHeight = contentHeight; LineHeight = lineHeight; }
            public int LineCount { get; }
            public int ContentHeight { get; }
            public int LineHeight { get; }
        }
        private readonly struct VisualLine
        {
            public VisualLine(int start, int end, int paragraph, int y, int height, int width) { Start = start; End = end; Paragraph = paragraph; Y = y; Height = height; Width = width; }
            public int Start { get; }
            public int End { get; }
            public int Paragraph { get; }
            public int Y { get; }
            public int Height { get; }
            public int Width { get; }
            public int Bottom => Y + Height;
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
        private void ParseAndAppend(string source)
        {
            var styles = new List<StyleFrame> { new StyleFrame(string.Empty, CurrentStyle) };
            var listCounts = new List<int> { 0 };
            var parseTables = new List<TableFrame>();
            var cursor = 0;
            while (cursor < source.Length)
            {
                var open = source.IndexOf('[', cursor);
                if (open < 0) { AppendSpan(source.Substring(cursor), styles[styles.Count - 1].Style); break; }
                if (open > cursor) AppendSpan(source.Substring(cursor, open - cursor), styles[styles.Count - 1].Style);
                var close = source.IndexOf(']', open + 1);
                if (close < 0) { AppendSpan(source.Substring(open), styles[styles.Count - 1].Style); break; }
                var tag = source.Substring(open + 1, close - open - 1).Trim();
                if (tag.StartsWith("/", StringComparison.Ordinal))
                {
                    var name = tag.Substring(1).Trim().ToLowerInvariant();
                    if (name == "table") { if (parseTables.Count > 0) parseTables.RemoveAt(parseTables.Count - 1); cursor = close + 1; continue; }
                    if (name == "cell") { cursor = close + 1; continue; }
                    for (var index = styles.Count - 1; index > 0; index--)
                        if (styles[index].Tag == name)
                        {
                            var frame = styles[index];
                            // Godot accepts [url]https://example.invalid[/url] in addition to [url=value]text[/url].
                            // Resolve the former only after its enclosed text has been appended.
                            if (name == "url" && frame.Style.Meta is string url && url.Length == 0)
                            {
                                var resolved = string.Empty;
                                for (var spanIndex = frame.SpanStart; spanIndex < _spans.Count; spanIndex++) resolved += _spans[spanIndex].Text;
                                for (var spanIndex = frame.SpanStart; spanIndex < _spans.Count; spanIndex++)
                                {
                                    var span = _spans[spanIndex];
                                    _spans[spanIndex] = new RichTextSpan(span.Text, span.Color, span.BackgroundColor, span.Bold, span.Italic, span.Underline, span.Strikethrough, resolved);
                                }
                            }
                            styles.RemoveRange(index, styles.Count - index); listCounts.RemoveRange(index, listCounts.Count - index); break;
                        }
                }
                else if (string.Equals(tag, "br", StringComparison.OrdinalIgnoreCase)) AppendSpan("\n", styles[styles.Count - 1].Style);
                else if (tag.StartsWith("hr", StringComparison.OrdinalIgnoreCase))
                {
                    var widthSource = GetBbcodeOption(tag, "width");
                    var heightSource = GetBbcodeOption(tag, "height");
                    var colorSource = GetBbcodeOption(tag, "color");
                    var alignmentSource = GetBbcodeOption(tag, "align");
                    var width = 90; var widthInPercent = true;
                    if (!string.IsNullOrEmpty(widthSource)) { widthInPercent = widthSource.EndsWith("%", StringComparison.Ordinal); int.TryParse(widthSource.TrimEnd('%'), out width); }
                    var height = 2;
                    var heightInPercent = heightSource.EndsWith("%", StringComparison.Ordinal);
                    if (!string.IsNullOrEmpty(heightSource)) int.TryParse(heightSource.TrimEnd('%'), out height);
                    Color? color = FontColor ?? Color.White;
                    if (!string.IsNullOrEmpty(colorSource) && TryParseColor(colorSource, out var parsedColor)) color = parsedColor;
                    var alignment = HorizontalAlignment.Center;
                    if (string.Equals(alignmentSource, "left", StringComparison.OrdinalIgnoreCase) || string.Equals(alignmentSource, "l", StringComparison.OrdinalIgnoreCase)) alignment = HorizontalAlignment.Left;
                    else if (string.Equals(alignmentSource, "right", StringComparison.OrdinalIgnoreCase) || string.Equals(alignmentSource, "r", StringComparison.OrdinalIgnoreCase)) alignment = HorizontalAlignment.Right;
                    AddHorizontalRule(Math.Max(0, width), Math.Max(0, height), color, alignment, widthInPercent, heightInPercent);
                }
                else
                {
                    var rawTag = tag;
                    if (rawTag.StartsWith("table", StringComparison.OrdinalIgnoreCase))
                    {
                        var equalsIndex = rawTag.IndexOf('=');
                        var columns = 1;
                        if (equalsIndex >= 0) int.TryParse(rawTag.Substring(equalsIndex + 1).Trim().Trim('"', '\''), out columns);
                        parseTables.Add(new TableFrame(Math.Max(1, columns), string.Empty)); cursor = close + 1; continue;
                    }
                    if (rawTag.Equals("cell", StringComparison.OrdinalIgnoreCase))
                    {
                        if (parseTables.Count == 0) { AppendSpan("[cell]", styles[styles.Count - 1].Style); cursor = close + 1; continue; }
                        AppendParsedTableCell(parseTables, styles[styles.Count - 1].Style); cursor = close + 1; continue;
                    }
                    var equals = tag.IndexOf('=');
                    var name = (equals < 0 ? tag : tag.Substring(0, equals)).Trim().ToLowerInvariant();
                    var value = equals < 0 ? string.Empty : tag.Substring(equals + 1).Trim().Trim('"', '\'');
                    RichTextListType? listType = null; var listCapitalize = false; var listBullet = "•";
                    if (rawTag.Equals("ul", StringComparison.OrdinalIgnoreCase) || rawTag.StartsWith("ul ", StringComparison.OrdinalIgnoreCase))
                    {
                        name = "ul"; listType = RichTextListType.Dots;
                        var bulletIndex = rawTag.IndexOf("bullet=", StringComparison.OrdinalIgnoreCase);
                        if (bulletIndex >= 0) listBullet = rawTag.Substring(bulletIndex + 7).Trim().Trim('"', '\'');
                    }
                    else if (rawTag.Equals("ol", StringComparison.OrdinalIgnoreCase) || rawTag.StartsWith("ol ", StringComparison.OrdinalIgnoreCase))
                    {
                        name = "ol"; listType = RichTextListType.Numbers;
                        var typeIndex = rawTag.IndexOf("type=", StringComparison.OrdinalIgnoreCase);
                        var listValue = typeIndex < 0 ? "1" : rawTag.Substring(typeIndex + 5).Trim().Trim('"', '\'');
                        if (listValue == "a" || listValue == "A") { listType = RichTextListType.Letters; listCapitalize = listValue == "A"; }
                        else if (listValue == "i" || listValue == "I") { listType = RichTextListType.Roman; listCapitalize = listValue == "I"; }
                    }
                    var style = styles[styles.Count - 1].Style;
                    var recognized = true;
                    var pushStyle = true;
                    switch (name)
                    {
                        case "b": style.Bold = true; break;
                        case "i": style.Italic = true; break;
                        case "u": style.Underline = true; break;
                        case "s": case "strike": style.Strikethrough = true; break;
                        case "color": if (TryParseColor(value, out var color)) style.Color = color; else recognized = false; break;
                        case "bgcolor": if (TryParseColor(value, out var backgroundColor)) style.BackgroundColor = backgroundColor; else recognized = false; break;
                        case "meta": if (!string.IsNullOrEmpty(value)) style.Meta = value; else recognized = false; break;
                        case "url": style.Meta = value; break;
                        case "indent": AppendSpan(new string(' ', 4), style); break;
                        case "ul": case "ol": break;
                        case "*":
                            pushStyle = false;
                            var listIndex = -1;
                            for (var index = styles.Count - 1; index > 0; index--)
                                if (styles[index].Tag == "ul" || styles[index].Tag == "ol") { listIndex = index; break; }
                            if (listIndex < 0) recognized = false;
                            else
                            {
                                if (!string.IsNullOrEmpty(Text) && !Text.EndsWith("\n", StringComparison.Ordinal)) AppendSpan("\n", style);
                                listCounts[listIndex]++;
                                var frame = styles[listIndex]; var depth = 0;
                                for (var index = 1; index <= listIndex; index++) if (styles[index].ListType.HasValue) depth++;
                                AppendSpan(new string(' ', Math.Max(0, depth - 1) * 4) + GetListMarker(listCounts[listIndex], frame.ListType ?? RichTextListType.Dots, frame.ListCapitalize, frame.ListBullet) + " ", style);
                            }
                            break;
                        default: recognized = false; break;
                    }
                    if (recognized && pushStyle) { styles.Add(new StyleFrame(name, style, _spans.Count, listType, listCapitalize, listBullet)); listCounts.Add(0); }
                }
                cursor = close + 1;
            }
        }
        private void AppendSpan(string text, TextStyle style)
        {
            if (string.IsNullOrEmpty(text)) return;
            SynchronizeSpansToText();
            var span = new RichTextSpan(text, style.Color, style.BackgroundColor, style.Bold, style.Italic, style.Underline, style.Strikethrough, style.Meta);
            if (_spans.Count > 0 && SameStyle(_spans[_spans.Count - 1], span))
            {
                var previous = _spans[_spans.Count - 1];
                _spans[_spans.Count - 1] = new RichTextSpan(previous.Text + text, previous.Color, previous.BackgroundColor, previous.Bold, previous.Italic, previous.Underline, previous.Strikethrough, previous.Meta);
            }
            else _spans.Add(span);
            Text += text;
            _spanText = Text;
            FollowToEnd();
            QueueLayout();
        }
        private void EnsurePlainTextSpan()
        {
            SynchronizeSpansToText();
        }
        private void SynchronizeSpansToText()
        {
            if (string.Equals(_spanText, Text, StringComparison.Ordinal)) return;
            _spans.Clear();
            _spanText = Text;
            if (!string.IsNullOrEmpty(Text)) _spans.Add(new RichTextSpan(Text, null, null, false, false, false, false, null));
        }
        private void BuildMetaRegions()
        {
            _metaRegions.Clear();
            if (EffectiveUIFont == null) return;
            EnsurePlainTextSpan();
            var origin = GlobalPosition + new Vector2(Padding.Left, Padding.Top - ScrollOffset);
            var cursor = origin; var right = Bounds.Right - Padding.Right; var lineHeight = TextMetrics.LineHeight(EffectiveUIFont);
            foreach (var span in _spans)
            {
                if (span.IsHorizontalRule)
                {
                    if (cursor.X > origin.X) { cursor.X = origin.X; cursor.Y += lineHeight; }
                    cursor.Y += Math.Max(lineHeight, GetHorizontalRuleHeight(span, right - origin.X));
                    continue;
                }
                if (span.Image != null)
                {
                    var size = new Vector2(Math.Max(1, span.ImageSize.X), Math.Max(1, span.ImageSize.Y));
                    if (Autowrap && cursor.X > origin.X && cursor.X + size.X > right) { cursor.X = origin.X; cursor.Y += lineHeight; }
                    if (span.Meta != null) _metaRegions.Add(new RichTextMetaRegion(new Rectangle((int)cursor.X, (int)cursor.Y, (int)size.X, (int)size.Y), span.Meta));
                    cursor.X += size.X;
                    continue;
                }
                foreach (var unit in GetTextUnits(span.Text))
                {
                    var character = span.Text[unit.Start];
                    if (character == '\r') continue;
                    if (character == '\n') { cursor.X = origin.X; cursor.Y += lineHeight; continue; }
                    if (character == '\t') { cursor.X = origin.X + Math.Max(1, (int)MathF.Floor((cursor.X - origin.X) / Math.Max(1, TableCellWidth)) + 1) * Math.Max(1, TableCellWidth); continue; }
                    var glyphWidth = unit.Width;
                    if (Autowrap && cursor.X > origin.X && cursor.X + glyphWidth > right) { cursor.X = origin.X; cursor.Y += lineHeight; }
                    if (span.Meta != null) _metaRegions.Add(new RichTextMetaRegion(new Rectangle((int)cursor.X, (int)cursor.Y, Math.Max(1, (int)MathF.Ceiling(glyphWidth)), lineHeight), span.Meta));
                    cursor.X += glyphWidth;
                }
            }
        }
        private IReadOnlyList<TextUnit> GetTextUnits(string text)
        {
            var units = new List<TextUnit>();
            for (var offset = 0; offset < text.Length;)
            {
                var character = text[offset];
                if (character == '\r' || character == '\n' || character == '\t' || EffectiveUIFont == null)
                {
                    units.Add(new TextUnit(offset, 1, character == '\r' || character == '\n' || character == '\t' ? 0 : 8));
                    offset++;
                    continue;
                }
                var end = offset + 1;
                while (end < text.Length && text[end] != '\r' && text[end] != '\n' && text[end] != '\t') end++;
                var chunk = text.Substring(offset, end - offset);
                var layout = CreateTextLayout(EffectiveUIFont, chunk, new TextLayoutOptions());
                for (var chunkOffset = 0; chunkOffset < chunk.Length;)
                {
                    var next = layout.GetNextGraphemeBoundary(chunkOffset);
                    var width = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(layout.GetCaretPosition(next).X - layout.GetCaretPosition(chunkOffset).X)));
                    units.Add(new TextUnit(offset + chunkOffset, next - chunkOffset, width));
                    chunkOffset = next;
                }
                offset = end;
            }
            return units;
        }
        private TextLayout CreateTextLayout(string text, TextLayoutOptions options) => CreateTextLayout(EffectiveUIFont, text, options);
        private TextLayout CreateTextLayout(UIFont font, string text, TextLayoutOptions options)
        {
            var layout = TextMetrics.Layout(font, text, options);
            return LetterSpacing == 0 ? layout : TextLayoutAdjuster.Apply(layout, LetterSpacing);
        }
        private readonly struct TextUnit
        {
            public TextUnit(int start, int length, int width) { Start = start; Length = length; Width = width; }
            public int Start { get; }
            public int Length { get; }
            public int Width { get; }
        }
        private TextStyle CurrentStyle => _styleStack[_styleStack.Count - 1];
        private void PushStyle(TextStyle style) => _styleStack.Add(style);
        private static bool SameStyle(RichTextSpan left, RichTextSpan right) => !left.IsHorizontalRule && !right.IsHorizontalRule && left.Image == null && right.Image == null && left.Color == right.Color && left.BackgroundColor == right.BackgroundColor && left.Bold == right.Bold && left.Italic == right.Italic && left.Underline == right.Underline && left.Strikethrough == right.Strikethrough && Equals(left.Meta, right.Meta);
        private static string GetListMarker(int index, RichTextListType type, bool capitalize, string bullet)
        {
            switch (type)
            {
                case RichTextListType.Numbers: return index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";
                case RichTextListType.Letters:
                    var letter = (char)('a' + Math.Max(0, (index - 1) % 26)); return (capitalize ? char.ToUpperInvariant(letter) : letter).ToString() + ".";
                case RichTextListType.Roman:
                    var roman = ToRoman(Math.Max(1, index)); return (capitalize ? roman : roman.ToLowerInvariant()) + ".";
                default: return string.IsNullOrEmpty(bullet) ? "•" : bullet;
            }
        }
        private static string ToRoman(int value)
        {
            var symbols = new[] { (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") };
            var result = string.Empty;
            foreach (var symbol in symbols) while (value >= symbol.Item1) { result += symbol.Item2; value -= symbol.Item1; }
            return result;
        }
        private void AppendParsedTableCell(List<TableFrame> tables, TextStyle style)
        {
            var table = tables[tables.Count - 1];
            if (table.CellCount > 0) AppendSpan(table.CellCount % table.Columns == 0 ? "\n" : "\t", style);
            table.CellCount++;
        }
        private static string GetBbcodeOption(string tag, string name)
        {
            var marker = name + "=";
            var start = tag.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return string.Empty;
            start += marker.Length;
            if (start >= tag.Length) return string.Empty;
            var quote = tag[start];
            if (quote == '\'' || quote == '"')
            {
                var endQuote = tag.IndexOf(quote, start + 1);
                return endQuote < 0 ? tag.Substring(start + 1) : tag.Substring(start + 1, endQuote - start - 1);
            }
            var end = start;
            while (end < tag.Length && !char.IsWhiteSpace(tag[end])) end++;
            return tag.Substring(start, end - start);
        }
        private static bool TryParseColor(string source, out Color color)
        {
            source = source?.Trim() ?? string.Empty;
            if (source.StartsWith("#", StringComparison.Ordinal))
            {
                var hex = source.Substring(1);
                if ((hex.Length == 6 || hex.Length == 8) && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value))
                {
                    color = hex.Length == 6 ? new Color((byte)(value >> 16), (byte)(value >> 8), (byte)value) : new Color((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
                    return true;
                }
            }
            switch (source.ToLowerInvariant())
            {
                case "red": color = Color.Red; return true;
                case "green": color = Color.Green; return true;
                case "blue": color = Color.Blue; return true;
                case "yellow": color = Color.Yellow; return true;
                case "black": color = Color.Black; return true;
                case "white": color = Color.White; return true;
                case "gray": case "grey": color = Color.Gray; return true;
                default: color = default; return false;
            }
        }
        private struct TextStyle { public Color? Color, BackgroundColor; public object Meta; public bool Bold, Italic, Underline, Strikethrough; }
        private readonly struct StyleFrame
        {
            public StyleFrame(string tag, TextStyle style, int spanStart = 0, RichTextListType? listType = null, bool listCapitalize = false, string listBullet = "•") { Tag = tag; Style = style; SpanStart = spanStart; ListType = listType; ListCapitalize = listCapitalize; ListBullet = listBullet ?? "•"; }
            public string Tag { get; } public TextStyle Style { get; } public int SpanStart { get; } public RichTextListType? ListType { get; } public bool ListCapitalize { get; } public string ListBullet { get; }
        }
        private sealed class TableFrame
        {
            public TableFrame(int columns, string name) { Columns = columns; Name = name ?? string.Empty; }
            public int Columns { get; }
            public string Name { get; }
            public int CellCount { get; set; }
        }
    }
}
