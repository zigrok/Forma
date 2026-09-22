// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// These control APIs and behaviors are adapted from Godot Engine's Slider implementation;
// see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    public enum SliderTickPosition { BottomRight, TopLeft, Both, Center }

    /// <summary>Adjusts a numeric range by dragging a thumb or using the keyboard or mouse wheel.</summary>
    public class Slider : Range
    {
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.Slider;
        private bool _dragging;
        private bool _hovering;
        private float _dragStartMain;
        private float _dragStartRatio;
        private float _ratioBeforeDragging;
        public Slider(Orientation orientation = Orientation.Horizontal) { Orientation = orientation; FocusMode = FocusMode.All; }
        public Orientation Orientation { get; }
        public bool Editable { get; set; } = true;
        /// <summary>Raised when a pointer drag begins, matching Godot's Slider.drag_started signal.</summary>
        public new event EventHandler DragStarted;
        /// <summary>Raised when a pointer drag ends; the argument reports whether the ratio actually changed, matching Godot's Slider.drag_ended(value_changed) signal.</summary>
        public new event Action<Slider, bool> DragEnded;
        /// <summary>Enables mouse-wheel adjustment, matching Godot's Slider.scrollable property.</summary>
        public bool Scrollable { get; set; } = true;
        /// <summary>Number of evenly-spaced visual ticks; values below two draw no ticks.</summary>
        public int TickCount { get; set; }
        public bool TicksOnBorders { get; set; }
        public SliderTickPosition TicksPosition { get; set; } = SliderTickPosition.BottomRight;
        /// <summary>Godot-style keyboard/gamepad increment override; negative values fall back to <see cref="Range.Step"/>.</summary>
        public float CustomStep { get; set; } = -1;
        public void SetCustomStep(float customStep) => CustomStep = customStep;
        public float GetCustomStep() => CustomStep;
        internal override void PointerEntered() { _hovering = true; base.PointerEntered(); }
        internal override void PointerExited() { _hovering = false; base.PointerExited(); }
        public override Vector2 GetMinimumSize() => Vector2.Max(CustomMinimumSize, Orientation == Orientation.Horizontal ? new Vector2(48, 20) : new Vector2(20, 48));
        /// <summary>Returns local tick rectangles for deterministic theme-independent validation.</summary>
        public IReadOnlyList<Rectangle> GetTickRectangles()
        {
            var result = new List<Rectangle>();
            if (TickCount <= 1) return result;
            var mainLength = Math.Max(0, (int)MathF.Round((Orientation == Orientation.Horizontal ? Size.X : Size.Y) - 12));
            var crossLength = Math.Max(0, (int)MathF.Round(Orientation == Orientation.Horizontal ? Size.Y : Size.X));
            for (var i = 0; i < TickCount; i++)
            {
                if (!TicksOnBorders && (i == 0 || i == TickCount - 1)) continue;
                var main = 6 + (int)MathF.Round(mainLength * i / (float)(TickCount - 1));
                AddTick(result, main, crossLength, TicksPosition);
            }
            return result;
        }
        protected internal override void PointerPressed(Point point)
        {
            base.PointerPressed(point);
            if (!Editable) return;
            _ratioBeforeDragging = Ratio;
            DragStarted?.Invoke(this, EventArgs.Empty);
            SetFromPoint(point);
            _dragging = true;
            _dragStartMain = Orientation == Orientation.Horizontal ? point.X : point.Y;
            _dragStartRatio = Ratio;
        }
        /// <summary>Tracks the pointer while held, matching Godot's Slider::gui_input relative-motion grab (grab.pos/grab.uvalue).</summary>
        protected internal override void PointerMoved(Point point)
        {
            if (!_dragging || !Editable) return;
            var main = Orientation == Orientation.Horizontal ? point.X : point.Y;
            var motion = main - _dragStartMain;
            if (Orientation == Orientation.Vertical) motion = -motion;
            else if (IsLayoutRtl()) motion = -motion;
            var areaSize = Orientation == Orientation.Horizontal ? Bounds.Width : Bounds.Height;
            if (areaSize <= 0) return;
            Ratio = _dragStartRatio + motion / areaSize;
        }
        protected internal override void PointerReleased(Point point, bool isInside)
        {
            if (!_dragging) return;
            _dragging = false;
            DragEnded?.Invoke(this, Math.Abs(_ratioBeforeDragging - Ratio) > 0.0001f);
        }
        internal override void CancelInput()
        {
            _dragging = false;
            base.CancelInput();
        }
        private void SetFromPoint(Point point)
        {
            if (!Editable) return;
            var ratio = Orientation == Orientation.Horizontal ? (point.X - Bounds.Left) / Math.Max(1f, Bounds.Width) : (point.Y - Bounds.Top) / Math.Max(1f, Bounds.Height);
            ratio = MathHelper.Clamp(ratio, 0, 1);
            // Godot's Slider::gui_input inverts the ratio for a vertical slider (the top of the track is
            // max, the standard "fader" convention) and mirrors it for a horizontal slider under RTL;
            // this click-jump path had been computing the un-inverted ratio directly, while the drag-
            // continuation path in PointerMoved already applied both inversions correctly.
            if (Orientation == Orientation.Vertical) ratio = 1 - ratio;
            else if (IsLayoutRtl()) ratio = 1 - ratio;
            Value = MinValue + (MaxValue - MinValue) * ratio;
        }
        internal override void KeyPressed(Keys key)
        {
            if (!Editable) return;
            var step = CustomStep >= 0 ? CustomStep : Step;
            if (key == Keys.Home) Value = MinValue;
            else if (key == Keys.End) Value = MaxValue;
            else if (Orientation == Orientation.Horizontal && key == Keys.Left) Value += IsLayoutRtl() ? step : -step;
            else if (Orientation == Orientation.Horizontal && key == Keys.Right) Value += IsLayoutRtl() ? -step : step;
            else if (Orientation == Orientation.Vertical && key == Keys.Up) Value += step;
            else if (Orientation == Orientation.Vertical && key == Keys.Down) Value -= step;
        }
        /// <summary>Mouse-wheel adjustment gated on <see cref="Editable"/> and <see cref="Scrollable"/>, matching Godot's Slider::gui_input WHEEL_UP/WHEEL_DOWN handling.</summary>
        internal override bool PointerWheel(int delta)
        {
            if (!Editable || !Scrollable || delta == 0) return false;
            if (FocusMode != FocusMode.None) GrabFocus();
            Value += delta > 0 ? Step : -Step;
            return true;
        }
        internal override bool HitTestBeforeChildren(Point point) => ContainsPoint(point);
        internal bool IsGrabberHighlighted => _hovering || _dragging;

        internal ThemeIcon? GetSliderThemeIcon(string itemName) => GetThemeIcon(itemName, Orientation == Orientation.Horizontal ? nameof(HSlider) : nameof(VSlider));

        private void AddTick(List<Rectangle> ticks, int main, int crossLength, SliderTickPosition position)
        {
            if (Orientation == Orientation.Horizontal)
            {
                if (position == SliderTickPosition.BottomRight || position == SliderTickPosition.Both) ticks.Add(new Rectangle(main - 1, Math.Max(0, crossLength - 4), 2, 4));
                if (position == SliderTickPosition.TopLeft || position == SliderTickPosition.Both) ticks.Add(new Rectangle(main - 1, 0, 2, 4));
                if (position == SliderTickPosition.Center) ticks.Add(new Rectangle(main - 1, Math.Max(0, crossLength / 2 - 2), 2, 4));
            }
            else
            {
                if (position == SliderTickPosition.BottomRight || position == SliderTickPosition.Both) ticks.Add(new Rectangle(Math.Max(0, crossLength - 4), main - 1, 4, 2));
                if (position == SliderTickPosition.TopLeft || position == SliderTickPosition.Both) ticks.Add(new Rectangle(0, main - 1, 4, 2));
                if (position == SliderTickPosition.Center) ticks.Add(new Rectangle(Math.Max(0, crossLength / 2 - 2), main - 1, 4, 2));
            }
        }
    }
    /// <summary>Adjusts a numeric range along a horizontal track, mirroring direction under RTL layout.</summary>
    public sealed class HSlider : Slider { public HSlider() : base(Orientation.Horizontal) { } }
    /// <summary>Adjusts a numeric range along a vertical track with larger values toward the top.</summary>
    public sealed class VSlider : Slider { public VSlider() : base(Orientation.Vertical) { } }

}
