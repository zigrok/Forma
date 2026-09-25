// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// Control APIs and behavior are adapted from Godot Engine's subviewport_container.cpp,
// virtual_joystick.cpp, video_stream_player.cpp, and rich_text_label.cpp;
// see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    /// <summary>Texture-backed presentation surface for a render target or externally rendered subviewport.</summary>
    public sealed class SubViewportContainer : TemplatedControl
    {
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.Viewport;
        private int _stretchShrink = 1;
        private Texture2D _viewportTexture;
        private RenderTarget2D _hostRenderTarget;
        private bool _stretch;
        private bool _mouseTarget;
        private bool _hostPointerPressed;
        private ButtonState _hostRightButton;
        private ButtonState _hostMiddleButton;
        private ButtonState _hostXButton1;
        private ButtonState _hostXButton2;
        private int _hostScrollWheel;
        private KeyboardState _hostKeyboard;

        /// <summary>Creates a viewport presentation surface with Godot's click focus mode.</summary>
        public SubViewportContainer()
        {
            FocusMode = FocusMode.Click;
            ViewportContext = new UIContext();
        }
        /// <summary>Independent retained tree rendered into and receiving input through this viewport.</summary>
        public UIContext ViewportContext { get; }
        /// <summary>Color used to clear the hosted render target before drawing its independent UI tree.</summary>
        public Color ViewportClearColor { get; set; } = Color.Transparent;
        public Texture2D ViewportTexture { get => _viewportTexture; set { if (_viewportTexture == value) return; _viewportTexture = value; QueueLayout(); } }
        public bool Stretch { get => _stretch; set { if (_stretch == value) return; _stretch = value; QueueLayout(); } }
        /// <summary>Godot's positive integer render-resolution divisor used when stretching a viewport texture.</summary>
        public int StretchShrink { get => _stretchShrink; set { if (value < 1) throw new ArgumentOutOfRangeException(nameof(value)); _stretchShrink = value; } }
        /// <summary>Indicates that pointer input should be considered targeted at the embedded viewport.</summary>
        public bool MouseTarget { get => _mouseTarget; set => _mouseTarget = value; }
        public override Vector2 GetMinimumSize()
        {
            if (Stretch) return CustomMinimumSize;
            var textureSize = ViewportTexture == null ? Vector2.Zero : new Vector2(ViewportTexture.Width, ViewportTexture.Height);
            return Vector2.Max(CustomMinimumSize, textureSize);
        }
        /// <summary>Sets whether the viewport texture stretches to the container bounds.</summary>
        public void SetStretch(bool enable) => Stretch = enable;
        /// <summary>Returns whether viewport stretching is enabled.</summary>
        public bool IsStretchEnabled() => Stretch;
        /// <summary>Sets the positive integer render-resolution divisor.</summary>
        public void SetStretchShrink(int amount) => StretchShrink = amount;
        /// <summary>Returns the active render-resolution divisor.</summary>
        public int GetStretchShrink() => StretchShrink;
        /// <summary>Sets whether this container participates in embedded viewport mouse targeting.</summary>
        public void SetMouseTarget(bool enable) => MouseTarget = enable;
        /// <summary>Returns whether embedded viewport mouse targeting is enabled.</summary>
        public bool IsMouseTargetEnabled() => MouseTarget;
        /// <summary>Returns the render-target size required by the active stretch/shrink settings.</summary>
        public Vector2 GetViewportSize()
        {
            if (Stretch) return Vector2.Max(Vector2.One, Size / StretchShrink);
            if (ViewportContext.ViewportSize != Vector2.Zero) return ViewportContext.ViewportSize;
            return ViewportTexture == null ? Vector2.Zero : new Vector2(ViewportTexture.Width, ViewportTexture.Height);
        }
        /// <summary>Maps a global pointer position into the embedded viewport's coordinate space.</summary>
        public Vector2 MapPointerToViewport(Point position)
        {
            var local = new Vector2(position.X, position.Y) - GlobalPosition;
            return Stretch && StretchShrink > 1 ? local / StretchShrink : local;
        }
        protected internal override void PointerPressed(Point position)
        {
            base.PointerPressed(position);
            _hostPointerPressed = true;
        }
        protected internal override void PointerReleased(Point position, bool isInside) => _hostPointerPressed = false;
        internal override void CancelInput()
        {
            _hostPointerPressed = false;
            _hostRightButton = _hostMiddleButton = _hostXButton1 = _hostXButton2 = ButtonState.Released;
            _hostKeyboard = default;
            _hostScrollWheel = 0;
            ViewportContext.ResetPlatformInput();
        }
        internal override void PointerButtonPressed(Point position, PointerButton button)
        {
            base.PointerButtonPressed(position, button);
            SetHostPointerButton(button, ButtonState.Pressed);
        }
        internal override void PointerButtonReleased(Point position, PointerButton button)
        {
            base.PointerButtonReleased(position, button);
            SetHostPointerButton(button, ButtonState.Released);
        }
        internal override bool PointerWheel(int delta)
        {
            _hostScrollWheel += delta;
            return ViewportContext.Roots.Count > 0;
        }
        internal override void KeyPressed(Keys key)
        {
            _hostKeyboard = Context?.CurrentKeyboardState ?? new KeyboardState(key);
        }
        internal override void KeyReleased(Keys key)
        {
            _hostKeyboard = Context?.CurrentKeyboardState ?? new KeyboardState();
        }
        internal override void TextInput(char character) => ViewportContext.TextInput(character);
        internal override void FocusLost()
        {
            _hostKeyboard = new KeyboardState();
            base.FocusLost();
        }
        internal override bool HitTestBeforeChildren(Point point) => ContainsPoint(point);
        internal override void Process(GameTime gameTime)
        {
            var viewportSize = GetViewportSize();
            ViewportContext.ViewportSize = viewportSize;
            var mapped = MapPointerToViewport(Context?.PointerPosition ?? Point.Zero);
            var mouse = new MouseState(
                (int)MathF.Round(mapped.X),
                (int)MathF.Round(mapped.Y),
                _hostScrollWheel,
                _hostPointerPressed ? ButtonState.Pressed : ButtonState.Released,
                _hostMiddleButton,
                _hostRightButton,
                _hostXButton1,
                _hostXButton2);
            ViewportContext.Update(gameTime, mouse, _hostKeyboard);
            base.Process(gameTime);
        }
        internal void RenderViewport(UIRenderContext context)
        {
            var texture = ViewportTexture;
            if (ViewportContext.Roots.Count > 0)
            {
                var viewportSize = GetViewportSize();
                var width = Math.Max(1, (int)MathF.Round(viewportSize.X));
                var height = Math.Max(1, (int)MathF.Round(viewportSize.Y));
                EnsureHostRenderTarget(context.GraphicsDevice, width, height);
                ViewportContext.ViewportSize = new Vector2(width, height);
                context.RenderToTarget(_hostRenderTarget, ViewportClearColor, () => ViewportContext.Draw(context.GraphicsDevice));
                texture = _hostRenderTarget;
            }
            if (texture != null)
            {
                var destination = Bounds;
                if (!Stretch) { destination.Width = texture.Width; destination.Height = texture.Height; }
                context.SpriteBatch.Draw(texture, destination, Color.White);
            }
        }
        public override void Dispose()
        {
            _hostRenderTarget?.Dispose();
            _hostRenderTarget = null;
            ViewportContext.Dispose();
            base.Dispose();
        }
        private void EnsureHostRenderTarget(GraphicsDevice graphicsDevice, int width, int height)
        {
            if (_hostRenderTarget != null && !_hostRenderTarget.IsDisposed && _hostRenderTarget.GraphicsDevice == graphicsDevice && _hostRenderTarget.Width == width && _hostRenderTarget.Height == height) return;
            _hostRenderTarget?.Dispose();
            _hostRenderTarget = new RenderTarget2D(graphicsDevice, width, height, false, SurfaceFormat.Color, DepthFormat.None);
        }
        private void SetHostPointerButton(PointerButton button, ButtonState state)
        {
            switch (button)
            {
                case PointerButton.Left: _hostPointerPressed = state == ButtonState.Pressed; break;
                case PointerButton.Right: _hostRightButton = state; break;
                case PointerButton.Middle: _hostMiddleButton = state; break;
                case PointerButton.XButton1: _hostXButton1 = state; break;
                case PointerButton.XButton2: _hostXButton2 = state; break;
            }
        }
    }

    /// <summary>On-screen analog joystick with normalized vector output and pointer capture.</summary>
    public sealed class VirtualJoystick : TemplatedControl
    {
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.Joystick;
        public override string AccessibilityValue => $"{Value.X.ToString(System.Globalization.CultureInfo.InvariantCulture)},{Value.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        public override AccessibilityActions AccessibilityActions => base.AccessibilityActions | AccessibilityActions.SetValue;
        private bool _active;
        private Vector2 _value;
        public VirtualJoystick() { FocusMode = FocusMode.All; CustomMinimumSize = new Vector2(80, 80); }
        public float DeadZone { get; set; } = .1f;
        public bool FixedCenter { get; set; } = true;
        public Color? BackgroundColor { get; set; }
        public Color? BorderColor { get; set; }
        public Color? KnobColor { get; set; }
        public Vector2 Value { get => _value; private set { if (_value == value) return; _value = value; ValueChanged?.Invoke(this, value); } }
        public bool IsPressed => _active;

        /// <summary>
        /// Accepts a <see cref="Vector2"/> or the same <c>"x,y"</c> string this control reports as
        /// its accessible value, so what it reports can be fed straight back in. The result is
        /// normalized and dead-zoned by the same rule pointer input uses, rather than assigned raw —
        /// otherwise an invoked value could sit outside the unit circle, which dragging can never
        /// produce.
        /// </summary>
        public override bool PerformAccessibilityAction(AccessibilityActions action, object argument = null)
        {
            if (action != AccessibilityActions.SetValue)
                return base.PerformAccessibilityAction(action, argument);

            if (!IsEffectivelyEnabled || !TryParseValue(argument, out var requested)) return false;

            if (requested.LengthSquared() > 1) requested.Normalize();
            Value = requested.Length() < DeadZone ? Vector2.Zero : requested;
            return true;
        }

        private static bool TryParseValue(object argument, out Vector2 value)
        {
            switch (argument)
            {
                case Vector2 vector:
                    value = vector;
                    return true;

                case string text:
                    var parts = text.Split(',');
                    if (parts.Length == 2
                        && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
                        && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y))
                    {
                        value = new Vector2(x, y);
                        return true;
                    }

                    break;
            }

            value = default;
            return false;
        }
        public event Action<VirtualJoystick, Vector2> ValueChanged;
        public event EventHandler Pressed;
        public event EventHandler Released;
        protected internal override void PointerPressed(Point position)
        {
            base.PointerPressed(position); _active = true; SetFromPoint(position); Pressed?.Invoke(this, EventArgs.Empty);
        }
        protected internal override void PointerMoved(Point position) { if (_active) SetFromPoint(position); }
        protected internal override void PointerReleased(Point position, bool isInside)
        {
            if (!_active) return;
            _active = false; Value = Vector2.Zero; Released?.Invoke(this, EventArgs.Empty);
        }
        internal override void CancelInput() { _active = false; Value = Vector2.Zero; base.CancelInput(); }
        internal override bool HitTestBeforeChildren(Point point) => ContainsPoint(point);
        private void SetFromPoint(Point point)
        {
            var center = GlobalPosition + Size / 2;
            var radius = Math.Max(1, Math.Min(Size.X, Size.Y) / 2);
            var result = (new Vector2(point.X, point.Y) - center) / radius;
            if (result.LengthSquared() > 1) result.Normalize();
            Value = result.Length() < DeadZone ? Vector2.Zero : result;
        }
    }

    /// <summary>Transforms a rich-text fragment before it is appended to a <see cref="RichTextLabel"/>.</summary>
    public abstract class RichTextEffect
    {
        public abstract string Process(string source);
    }

    /// <summary>BBCode-oriented text model with an extensible effect pipeline.</summary>
    public sealed class RichTextDocument : RichTextLabel
    {
        private readonly List<RichTextEffect> _effects = new List<RichTextEffect>();
        public IReadOnlyList<RichTextEffect> Effects => _effects;
        public void InstallEffect(RichTextEffect effect)
        {
            if (effect == null) throw new ArgumentNullException(nameof(effect));
            _effects.Add(effect);
        }
        public override void AppendBbcode(string bbcode)
        {
            var text = bbcode ?? string.Empty;
            foreach (var effect in _effects) text = effect.Process(text) ?? string.Empty;
            base.AppendBbcode(text);
        }
        public void ClearEffects() => _effects.Clear();
    }
}
