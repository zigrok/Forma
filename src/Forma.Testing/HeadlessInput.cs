// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Forma.Testing
{
    /// <summary>Gamepad buttons <see cref="HeadlessInput"/> can press. UI navigation is keyboard-driven, so each maps to a key.</summary>
    public enum HeadlessGamepadButton { DPadUp, DPadDown, DPadLeft, DPadRight, Accept, Cancel }

    /// <summary>
    /// Builds and drives a <see cref="UIContext"/> with no window or graphics device, through the context's
    /// Inject* side channel. Positions are logical pixels when the context's DisplayScale is 1 (the default).
    /// </summary>
    public sealed class HeadlessInput
    {
        public HeadlessInput(UIContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            context.SuppressPolledInput = true;
        }

        public UIContext Context { get; }
        public Point Position { get; private set; }

        /// <summary>Creates a context with the given viewport. Dispose it when done.</summary>
        public static UIContext CreateContext(int width = 800, int height = 600) =>
            new UIContext { ViewportSize = new Vector2(width, height) };

        /// <summary>Creates a context and the driver for it.</summary>
        public static HeadlessInput Create(int width = 800, int height = 600) => new HeadlessInput(CreateContext(width, height));

        /// <summary>Centre of a control's laid-out bounds, for aiming input at it.</summary>
        public static Point CenterOf(Control control)
        {
            if (control == null) throw new ArgumentNullException(nameof(control));
            control.Context?.Layout();
            var b = control.Bounds;
            return new Point((int)(b.X + b.Width / 2), (int)(b.Y + b.Height / 2));
        }

        public HeadlessInput Move(Point position)
        {
            Position = position;
            Context.InjectPointerMove(position);
            return this;
        }

        public HeadlessInput Move(Control control) => Move(CenterOf(control));

        public HeadlessInput Press(PointerButton button = PointerButton.Left)
        {
            Context.InjectPointerPress(Position, button);
            return this;
        }

        public HeadlessInput Release(PointerButton button = PointerButton.Left)
        {
            Context.InjectPointerRelease(Position, button);
            return this;
        }

        /// <summary>Move, press and release at a position or the centre of a control.</summary>
        public HeadlessInput Click(Point position, PointerButton button = PointerButton.Left)
        {
            Move(position);
            Press(button);
            return Release(button);
        }

        public HeadlessInput Click(Control control, PointerButton button = PointerButton.Left) => Click(CenterOf(control), button);

        /// <summary>Two clicks in a row at the same spot with no time passing between them.</summary>
        public HeadlessInput DoubleClick(Control control) => Click(control).Click(control);

        public HeadlessInput Wheel(int delta, Point? position = null)
        {
            if (position.HasValue) Move(position.Value);
            Context.InjectPointerWheel(Position, delta);
            return this;
        }

        public HeadlessInput Key(Keys key, params Keys[] modifiers)
        {
            Context.InjectKeyPress(key, modifiers);
            Context.InjectKeyRelease(key);
            return this;
        }

        public HeadlessInput KeyDown(Keys key, params Keys[] modifiers)
        {
            Context.InjectKeyPress(key, modifiers);
            return this;
        }

        public HeadlessInput KeyUp(Keys key)
        {
            Context.InjectKeyRelease(key);
            return this;
        }

        public HeadlessInput Type(string text)
        {
            Context.InjectText(text);
            return this;
        }

        /// <summary>Presses and releases a gamepad button as the key Forma's UI navigation uses for it.</summary>
        public HeadlessInput Gamepad(HeadlessGamepadButton button)
        {
            var key = button switch
            {
                HeadlessGamepadButton.DPadUp => Keys.Up,
                HeadlessGamepadButton.DPadDown => Keys.Down,
                HeadlessGamepadButton.DPadLeft => Keys.Left,
                HeadlessGamepadButton.DPadRight => Keys.Right,
                HeadlessGamepadButton.Accept => Keys.Enter,
                HeadlessGamepadButton.Cancel => Keys.Escape,
                _ => throw new ArgumentOutOfRangeException(nameof(button)),
            };
            return Key(key);
        }

        /// <summary>Advances the frame by the given time so animations, timers and double-click windows move.</summary>
        public HeadlessInput Advance(TimeSpan elapsed)
        {
            var total = Context.CurrentTime + elapsed;
            Context.Update(new GameTime(total, elapsed), default, default);
            return this;
        }
    }
}
