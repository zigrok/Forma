// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// ColorPicker APIs, interaction, presets, and popup behavior are adapted from Godot
// Engine's scene/gui/color_picker.cpp and color_picker.h; see THIRD-PARTY-NOTICES.md.
// OkColor retains its separate Björn Ottosson MIT notice in OkColor.cs.

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    /// <summary>Godot ColorPicker channel representations.</summary>
    public enum ColorPickerMode { Rgb, Hsv, Linear, OkHsl }
    /// <summary>Godot ColorPicker's selectable surface layouts.</summary>
    public enum ColorPickerShape { HsvRectangle, HsvWheel, VhsCircle, OkHslCircle, None, OkHsRectangle, OkHlRectangle }

    /// <summary>HSV color editor with deterministic pointer and keyboard adjustment.</summary>
    [TemplatePart(ColorFieldPartName, typeof(Control))]
    public sealed class ColorPicker : TemplatedControl
    {
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.ColorPicker;
        public const string ColorFieldPartName = "PART_ColorField";
        private Color _color = Color.White;
        private Color _oldColor = Color.White;
        private static readonly Dictionary<string, Color> NamedColors = CreateNamedColors();
        private readonly List<Color> _presets = new List<Color>();
        private readonly List<Color> _recentPresets = new List<Color>();
        private bool _picking;
        private bool _deferredSliderDragging;
        public ColorPicker() { FocusMode = FocusMode.All; CustomMinimumSize = new Vector2(180, 140); }
        // Godot's ColorPicker::set_pick_color (the property this setter mirrors) updates the color and
        // redraws, but never fires color_changed itself - the signal only fires from the ~14 explicit
        // interactive call sites (slider drag, HTML submit, preset/recent-preset pick, shape commit).
        // CommitColor is the shared path those call sites route through.
        public Color Color { get => _color; set => _color = value; }
        private void CommitColor(Color value)
        {
            if (_color == value) return;
            _color = value;
            if (!DeferredMode || !_deferredSliderDragging) ColorChanged?.Invoke(this, value);
        }
        public bool EditAlpha { get; set; } = true;
        // Godot's ColorPicker (and ColorPickerButton) both default edit_intensity to true.
        public bool EditIntensity { get; set; } = true;
        public bool CanAddSwatches { get; set; } = true;
        public bool DeferredMode { get; set; }
        public bool ColorizeSliders { get; set; } = true;
        public bool PresetsVisible { get; set; } = true;
        public bool ModesVisible { get; set; } = true;
        public bool SamplerVisible { get; set; } = true;
        public bool SlidersVisible { get; set; } = true;
        public bool HexVisible { get; set; } = true;
        public float KeyboardAdjustmentStep { get; set; } = .01f;
        public ColorPickerMode ColorMode { get; set; } = ColorPickerMode.Rgb;
        /// <summary>Interactive color surface layout, equivalent to Godot's picker_shape.</summary>
        public ColorPickerShape PickerShape { get; set; } = ColorPickerShape.HsvRectangle;
        public IReadOnlyList<Color> Presets => _presets;
        /// <summary>Recently committed colors, maintained independently from persistent presets.</summary>
        public IReadOnlyList<Color> RecentPresets => _recentPresets;
        /// <summary>Previous color shown by a ColorPickerButton popup, equivalent to Godot's old_color.</summary>
        public Color OldColor { get => _oldColor; set => _oldColor = value; }
        public bool DisplayOldColor { get; set; }
        public string ColorHtml
        {
            get => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}{Color.A:X2}";
            set
            {
                var parsed = ParseHtml(value);
                // Godot's _html_submitted discards a typed alpha channel entirely when alpha editing
                // is disabled, restoring the picker's own current alpha instead.
                if (!EditAlpha) parsed = new Color(parsed.R, parsed.G, parsed.B, Color.A);
                CommitColor(parsed);
                // Godot's _set_pick_color calls add_recent_preset whenever a hex submission commits.
                AddRecentPreset(parsed);
            }
        }
        public event Action<ColorPicker, Color> ColorChanged;
        public event Action<ColorPicker, Color> PresetSelected;
        public event Action<ColorPicker, Color> RecentPresetSelected;
        public void SetPickColor(Color color) => Color = color;
        public Color GetPickColor() => Color;
        public void SetEditAlpha(bool show) => EditAlpha = show;
        public bool IsEditingAlpha() => EditAlpha;
        public void SetEditIntensity(bool show) => EditIntensity = show;
        public bool IsEditingIntensity() => EditIntensity;
        public void SetCanAddSwatches(bool enabled) => CanAddSwatches = enabled;
        public bool AreSwatchesEnabled() => CanAddSwatches;
        public void SetDeferredMode(bool enabled) => DeferredMode = enabled;
        public bool IsDeferredMode() => DeferredMode;
        public void SetColorizeSliders(bool enabled) => ColorizeSliders = enabled;
        public bool IsColorizingSliders() => ColorizeSliders;
        public void SetColorMode(ColorPickerMode mode) { if (!Enum.IsDefined(typeof(ColorPickerMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode)); ColorMode = mode; }
        public ColorPickerMode GetColorMode() => ColorMode;
        public void SetPickerShape(ColorPickerShape shape) { if (!Enum.IsDefined(typeof(ColorPickerShape), shape)) throw new ArgumentOutOfRangeException(nameof(shape)); PickerShape = shape; }
        public ColorPickerShape GetPickerShape() => PickerShape;
        public void SetOldColor(Color color) => OldColor = color;
        public Color GetOldColor() => OldColor;
        public void SetDisplayOldColor(bool enabled) => DisplayOldColor = enabled;
        public bool IsDisplayingOldColor() => DisplayOldColor;
        public void SetPresetsVisible(bool visible) => PresetsVisible = visible;
        public bool ArePresetsVisible() => PresetsVisible;
        public void SetModesVisible(bool visible) => ModesVisible = visible;
        public bool AreModesVisible() => ModesVisible;
        public void SetSamplerVisible(bool visible) => SamplerVisible = visible;
        public bool IsSamplerVisible() => SamplerVisible;
        public void SetSlidersVisible(bool visible) => SlidersVisible = visible;
        public bool AreSlidersVisible() => SlidersVisible;
        public void SetHexVisible(bool visible) => HexVisible = visible;
        public bool IsHexVisible() => HexVisible;
        public Vector2 GetPickerCursorNormalized()
        {
            var hsv = GetHsv();
            var hsl = GetOkHsl();
            if (PickerShape == ColorPickerShape.HsvRectangle) return new Vector2(hsv.Y, 1 - hsv.Z);
            if (PickerShape == ColorPickerShape.OkHsRectangle) return new Vector2(hsl.X, 1 - hsl.Y);
            if (PickerShape == ColorPickerShape.OkHlRectangle) return new Vector2(hsl.X, 1 - hsl.Z);
            if (PickerShape == ColorPickerShape.OkHslCircle) return new Vector2(hsl.X, hsl.Y);
            if (PickerShape == ColorPickerShape.None) return Vector2.Zero;
            return new Vector2(hsv.X, hsv.Y);
        }
        public void SetPickerCursorNormalized(Vector2 cursor)
        {
            cursor = new Vector2(MathHelper.Clamp(cursor.X, 0, 1), MathHelper.Clamp(cursor.Y, 0, 1));
            // Godot's set_edit_alpha only toggles slider/label visibility - it never forces alpha to
            // 1.0. Every interactive commit preserves whatever alpha is already set, hidden or not.
            var alpha = Color.A / 255f;
            if (PickerShape == ColorPickerShape.None) return;
            if (PickerShape == ColorPickerShape.HsvRectangle)
            {
                var hsv = GetHsv();
                SetHsv(hsv.X, cursor.X, 1 - cursor.Y, alpha);
            }
            else if (PickerShape == ColorPickerShape.OkHsRectangle)
            {
                var hsl = GetOkHsl();
                SetOkHsl(cursor.X, 1 - cursor.Y, hsl.Z, alpha);
            }
            else if (PickerShape == ColorPickerShape.OkHlRectangle)
            {
                var hsl = GetOkHsl();
                SetOkHsl(cursor.X, hsl.Y, 1 - cursor.Y, alpha);
            }
            else if (PickerShape == ColorPickerShape.OkHslCircle)
            {
                var hsl = GetOkHsl();
                SetOkHsl(cursor.X, cursor.Y, hsl.Z, alpha);
            }
            else
            {
                var hsv = GetHsv();
                SetHsv(cursor.X, cursor.Y, hsv.Z, alpha);
            }
        }
        public void Commit() { ColorChanged?.Invoke(this, Color); }
        /// <summary>Adds a preset, moving it to the back (most-recent position) if it already exists, matching Godot's ColorPicker::add_preset.</summary>
        public void AddPreset(Color color) { if (!CanAddSwatches) return; _presets.Remove(color); _presets.Add(color); }
        public bool ErasePreset(Color color) => _presets.Remove(color);
        public void ClearPresets() => _presets.Clear();
        public void SetPresets(IEnumerable<Color> colors)
        {
            _presets.Clear(); if (colors == null) return;
            foreach (var color in colors) if (!_presets.Contains(color)) _presets.Add(color);
        }
        /// <summary>Adds a unique recent swatch, retaining Godot's nine-color recent history limit.</summary>
        public void AddRecentPreset(Color color)
        {
            if (_recentPresets.Contains(color)) return;
            if (_recentPresets.Count == 9) _recentPresets.RemoveAt(0);
            _recentPresets.Add(color);
        }
        public bool EraseRecentPreset(Color color) => _recentPresets.Remove(color);
        public void ClearRecentPresets() => _recentPresets.Clear();
        public void SelectPreset(int index)
        {
            if (index < 0 || index >= _presets.Count) throw new ArgumentOutOfRangeException(nameof(index));
            var color = _presets[index];
            CommitColor(color);
            // Godot's _preset_input also calls add_recent_preset(color) before emitting color_changed.
            AddRecentPreset(color);
            PresetSelected?.Invoke(this, color);
        }
        public void SelectRecentPreset(int index)
        {
            if (index < 0 || index >= _recentPresets.Count) throw new ArgumentOutOfRangeException(nameof(index));
            var color = _recentPresets[index];
            // Godot's _recent_preset_pressed moves the reselected color to the back (most-recent
            // position) so it isn't the next one evicted, matching AddPreset's own move-to-back behavior.
            _recentPresets.RemoveAt(index); _recentPresets.Add(color);
            CommitColor(color);
            RecentPresetSelected?.Invoke(this, color);
        }
        /// <summary>Gets hue, saturation and value in normalized 0..1 units.</summary>
        public Vector3 GetHsv()
        {
            var red = Color.R / 255f; var green = Color.G / 255f; var blue = Color.B / 255f;
            var maximum = Math.Max(red, Math.Max(green, blue)); var minimum = Math.Min(red, Math.Min(green, blue)); var delta = maximum - minimum; var hue = 0f;
            if (delta > 0) { if (maximum == red) hue = ((green - blue) / delta + 6) % 6; else if (maximum == green) hue = (blue - red) / delta + 2; else hue = (red - green) / delta + 4; hue /= 6; }
            return new Vector3(hue, maximum == 0 ? 0 : delta / maximum, maximum);
        }
        public void SetHsv(float hue, float saturation, float value, float? alpha = null) => CommitColor(FromHsv(hue * 360, saturation, value, alpha ?? Color.A / 255f));
        /// <summary>Gets the Godot ColorPicker OKHSL channel values in normalized 0..1 units.</summary>
        public Vector3 GetOkHsl() => OkColor.ToOkHsl(Color);
        /// <summary>Sets the color from Godot's perceptually uniform OKHSL representation.</summary>
        public void SetOkHsl(float hue, float saturation, float lightness, float? alpha = null) => CommitColor(OkColor.FromOkHsl(hue, saturation, lightness, alpha ?? Color.A / 255f));
        /// <summary>Returns the mode-specific label for a visible color channel.</summary>
        public string GetChannelLabel(int channel)
        {
            if (channel < 0 || channel > 2) throw new ArgumentOutOfRangeException(nameof(channel));
            return ColorMode == ColorPickerMode.Hsv || ColorMode == ColorPickerMode.OkHsl ? channel == 0 ? "H" : channel == 1 ? "S" : ColorMode == ColorPickerMode.OkHsl ? "L" : "V" : channel == 0 ? "R" : channel == 1 ? "G" : "B";
        }
        /// <summary>Returns the upper bound used by Godot's selected ColorPicker channel mode.</summary>
        public float GetChannelMaximum(int channel)
        {
            if (channel < 0 || channel > 2) throw new ArgumentOutOfRangeException(nameof(channel));
            if (ColorMode == ColorPickerMode.Rgb) return 255;
            if (ColorMode == ColorPickerMode.Hsv || ColorMode == ColorPickerMode.OkHsl) return channel == 0 ? 359 : 100;
            return 1;
        }
        /// <summary>Reads a selected ColorPicker mode channel using Godot's displayed units.</summary>
        public float GetChannelValue(int channel)
        {
            if (channel < 0 || channel > 2) throw new ArgumentOutOfRangeException(nameof(channel));
            if (ColorMode == ColorPickerMode.Rgb) return channel == 0 ? Color.R : channel == 1 ? Color.G : Color.B;
            if (ColorMode == ColorPickerMode.Hsv) { var hsv = GetHsv(); return channel == 0 ? hsv.X * 360 : channel == 1 ? hsv.Y * 100 : hsv.Z * 100; }
            if (ColorMode == ColorPickerMode.OkHsl) { var hsl = GetOkHsl(); return channel == 0 ? hsl.X * 360 : channel == 1 ? hsl.Y * 100 : hsl.Z * 100; }
            var linear = OkColor.ToLinearSrgb(Color); return channel == 0 ? linear.X : channel == 1 ? linear.Y : linear.Z;
        }
        /// <summary>Updates one selected ColorPicker mode channel using Godot's displayed units.</summary>
        public void SetChannelValue(int channel, float value)
        {
            if (channel < 0 || channel > 2) throw new ArgumentOutOfRangeException(nameof(channel));
            if (ColorMode == ColorPickerMode.Rgb)
            {
                var r = Color.R; var g = Color.G; var b = Color.B; var component = (byte)MathHelper.Clamp(value, 0, 255);
                if (channel == 0) r = component; else if (channel == 1) g = component; else b = component;
                CommitColor(new Color(r, g, b, Color.A)); return;
            }
            if (ColorMode == ColorPickerMode.Hsv)
            {
                var hsv = GetHsv(); if (channel == 0) hsv.X = value / 360; else if (channel == 1) hsv.Y = value / 100; else hsv.Z = value / 100;
                SetHsv(hsv.X, hsv.Y, hsv.Z); return;
            }
            if (ColorMode == ColorPickerMode.OkHsl)
            {
                var hsl = GetOkHsl(); if (channel == 0) hsl.X = value / 360; else if (channel == 1) hsl.Y = value / 100; else hsl.Z = value / 100;
                SetOkHsl(hsl.X, hsl.Y, hsl.Z); return;
            }
            var linear = OkColor.ToLinearSrgb(Color); if (channel == 0) linear.X = value; else if (channel == 1) linear.Y = value; else linear.Z = value;
            CommitColor(OkColor.FromLinearSrgb(linear, Color.A / 255f));
        }
        internal override void PointerPressed(Point point)
        {
            base.PointerPressed(point);
            // Godot's _sample_input reverts to old_color on a left-click of the old-color half of the
            // sample swatch, firing color_changed like any other interactive commit.
            if (DisplayOldColor && GetOldColorSwatchRectangle().Contains(point)) { CommitColor(OldColor); return; }
            _picking = true;
            _deferredSliderDragging = DeferredMode && point.X >= GetMainSurface().Right;
            SetFromPoint(point);
        }
        private Rectangle GetOldColorSwatchRectangle()
        {
            var sampleWidth = Math.Min(20, Math.Max(0, Bounds.Width / 4));
            var sampleHeight = Math.Min(10, Math.Max(0, Bounds.Height / 8));
            return new Rectangle(Bounds.X + 2, Bounds.Y + 2, sampleWidth, sampleHeight);
        }
        internal override void PointerMoved(Point point) { if (_picking) SetFromPoint(point); }
        internal override void PointerReleased(Point point, bool isInside)
        {
            var wasPicking = _picking;
            if (wasPicking && isInside) SetFromPoint(point);
            _picking = false;
            var flushDeferred = _deferredSliderDragging;
            _deferredSliderDragging = false;
            if (flushDeferred) ColorChanged?.Invoke(this, Color);
            if (wasPicking) AddRecentPreset(Color);
        }
        internal override void CancelInput()
        {
            _picking = false;
            _deferredSliderDragging = false;
            base.CancelInput();
        }
        internal override void KeyPressed(Keys key)
        {
            if (DisplayOldColor && (key == Keys.Enter || key == Keys.Space))
            {
                CommitColor(OldColor);
                return;
            }
            if (PickerShape == ColorPickerShape.None) return;
            var cursor = GetPickerCursorNormalized();
            var step = Math.Max(.001f, KeyboardAdjustmentStep);
            if (key == Keys.Left) cursor.X -= step;
            else if (key == Keys.Right) cursor.X += step;
            else if (key == Keys.Up) cursor.Y -= step;
            else if (key == Keys.Down) cursor.Y += step;
            else return;
            SetPickerCursorNormalized(cursor);
        }
        private void SetFromPoint(Point point)
        {
            if (PickerShape == ColorPickerShape.None) return;
            var main = GetMainSurface();
            var x = MathHelper.Clamp((point.X - main.Left) / Math.Max(1f, main.Width), 0, 1);
            var y = MathHelper.Clamp((point.Y - main.Top) / Math.Max(1f, main.Height), 0, 1);
            // Godot never forces alpha to 1.0 for interactive picker-surface gestures - EditAlpha only
            // hides the alpha slider/label, it never mutates the value while it's off.
            var alpha = Color.A / 255f;
            if (point.X >= main.Right)
            {
                var slider = MathHelper.Clamp((point.Y - Bounds.Top) / Math.Max(1f, Bounds.Height), 0, 1);
                if (PickerShape == ColorPickerShape.HsvRectangle) { var hsv = GetHsv(); SetHsv(slider, hsv.Y, hsv.Z, alpha); }
                else if (PickerShape == ColorPickerShape.OkHsRectangle || PickerShape == ColorPickerShape.OkHslCircle) { var hsl = GetOkHsl(); SetOkHsl(hsl.X, hsl.Y, 1 - slider, alpha); }
                else if (PickerShape == ColorPickerShape.OkHlRectangle) { var hsl = GetOkHsl(); SetOkHsl(hsl.X, 1 - slider, hsl.Z, alpha); }
                else { var hsv = GetHsv(); SetHsv(hsv.X, hsv.Y, 1 - slider, alpha); }
                return;
            }
            if (PickerShape == ColorPickerShape.HsvRectangle)
            {
                var hsv = GetHsv(); SetHsv(hsv.X, x, 1 - y, alpha); return;
            }
            if (PickerShape == ColorPickerShape.OkHsRectangle)
            {
                var hsl = GetOkHsl(); SetOkHsl(x, 1 - y, hsl.Z, alpha); return;
            }
            if (PickerShape == ColorPickerShape.OkHlRectangle)
            {
                var hsl = GetOkHsl(); SetOkHsl(x, hsl.Y, 1 - y, alpha); return;
            }
            var center = new Vector2(main.Center.X, main.Center.Y);
            var delta = new Vector2(point.X, point.Y) - center;
            var radius = Math.Max(1, Math.Min(main.Width, main.Height) / 2f);
            var distance = delta.Length();
            if (distance > radius) return;
            var hue = MathF.Atan2(delta.Y, delta.X) / MathHelper.TwoPi;
            if (hue < 0) hue += 1;
            if (PickerShape == ColorPickerShape.HsvWheel)
            {
                var inner = radius * .66f;
                if (distance >= inner) { var hsv = GetHsv(); SetHsv(hue, hsv.Y, hsv.Z, alpha); }
                else { var hsv = GetHsv(); SetHsv(hsv.X, MathHelper.Clamp((delta.X + inner) / (inner * 2), 0, 1), MathHelper.Clamp(1 - (delta.Y + inner) / (inner * 2), 0, 1), alpha); }
            }
            else if (PickerShape == ColorPickerShape.VhsCircle)
                SetHsv(hue, MathHelper.Clamp(distance / radius, 0, 1), GetHsv().Z, alpha);
            else if (PickerShape == ColorPickerShape.OkHslCircle)
                SetOkHsl(hue, MathHelper.Clamp(distance / radius, 0, 1), GetOkHsl().Z, alpha);
        }
        internal void DrawColorField(UIRenderContext context)
        {
            if (PickerShape != ColorPickerShape.None) DrawPickerShape(context);
            if (DisplayOldColor)
            {
                var oldSwatch = GetOldColorSwatchRectangle();
                context.Fill(oldSwatch, OldColor);
                context.Border(oldSwatch, context.Theme.PanelBorderColor);
                var newSwatch = new Rectangle(oldSwatch.Right + 2, oldSwatch.Y, oldSwatch.Width, oldSwatch.Height);
                context.Fill(newSwatch, Color);
                context.Border(newSwatch, context.Theme.PanelBorderColor);
            }
            context.Border(Bounds, context.Theme.PanelBorderColor);
        }
        private Rectangle GetMainSurface() => PickerShape == ColorPickerShape.HsvWheel ? Bounds : new Rectangle(Bounds.X, Bounds.Y, Math.Max(0, Bounds.Width - 14), Bounds.Height);
        private void DrawPickerShape(UIRenderContext context)
        {
            var main = GetMainSurface();
            if (main.Width <= 0 || main.Height <= 0) return;
            const int bands = 32;
            var hsv = GetHsv(); var hsl = GetOkHsl();
            for (var row = 0; row < bands; row++)
            {
                var y = (row + .5f) / bands;
                for (var column = 0; column < bands; column++)
                {
                    var x = (column + .5f) / bands;
                    var cell = new Rectangle(main.X + column * main.Width / bands, main.Y + row * main.Height / bands, Math.Max(1, (column + 1) * main.Width / bands - column * main.Width / bands + 1), Math.Max(1, (row + 1) * main.Height / bands - row * main.Height / bands + 1));
                    var color = GetSurfaceColor(x, y, main, hsv, hsl);
                    if (color.HasValue) context.Fill(cell, color.Value);
                }
            }
            DrawSlider(context, main, hsv, hsl);
            DrawCursor(context, main, hsv, hsl);
        }
        private Color? GetSurfaceColor(float x, float y, Rectangle main, Vector3 hsv, Vector3 hsl)
        {
            if (PickerShape == ColorPickerShape.HsvRectangle) return FromHsv(hsv.X * 360, x, 1 - y, 1);
            if (PickerShape == ColorPickerShape.OkHsRectangle) return OkColor.FromOkHsl(x, 1 - y, hsl.Z);
            if (PickerShape == ColorPickerShape.OkHlRectangle) return OkColor.FromOkHsl(x, hsl.Y, 1 - y);
            var center = new Vector2(.5f, .5f); var point = new Vector2(x, y); var delta = point - center; var distance = delta.Length() * 2;
            if (distance > 1) return null;
            var hue = MathF.Atan2(delta.Y, delta.X) / MathHelper.TwoPi; if (hue < 0) hue += 1;
            if (PickerShape == ColorPickerShape.HsvWheel)
            {
                if (distance >= .66f) return FromHsv(hue * 360, 1, 1, 1);
                var sx = MathHelper.Clamp((delta.X + .33f) / .66f, 0, 1); var value = MathHelper.Clamp(1 - (delta.Y + .33f) / .66f, 0, 1);
                return FromHsv(hsv.X * 360, sx, value, 1);
            }
            return PickerShape == ColorPickerShape.VhsCircle ? FromHsv(hue * 360, distance, hsv.Z, 1) : OkColor.FromOkHsl(hue, distance, hsl.Z);
        }
        private void DrawSlider(UIRenderContext context, Rectangle main, Vector3 hsv, Vector3 hsl)
        {
            if (PickerShape == ColorPickerShape.HsvWheel) return;
            var slider = new Rectangle(main.Right, Bounds.Y, Math.Max(0, Bounds.Right - main.Right), Bounds.Height);
            if (slider.Width <= 0) return;
            const int bands = 32;
            for (var row = 0; row < bands; row++)
            {
                var value = 1 - (row + .5f) / bands;
                Color color;
                if (PickerShape == ColorPickerShape.HsvRectangle) color = FromHsv((1 - value) * 360, 1, 1, 1);
                else if (PickerShape == ColorPickerShape.OkHlRectangle) color = OkColor.FromOkHsl(hsl.X, value, hsl.Z);
                else if (PickerShape == ColorPickerShape.OkHsRectangle || PickerShape == ColorPickerShape.OkHslCircle) color = OkColor.FromOkHsl(hsl.X, hsl.Y, value);
                else color = FromHsv(hsv.X * 360, hsv.Y, value, 1);
                context.Fill(new Rectangle(slider.X, slider.Y + row * slider.Height / bands, slider.Width, Math.Max(1, (row + 1) * slider.Height / bands - row * slider.Height / bands + 1)), color);
            }
        }
        private void DrawCursor(UIRenderContext context, Rectangle main, Vector3 hsv, Vector3 hsl)
        {
            Vector2 position;
            if (PickerShape == ColorPickerShape.HsvRectangle) position = new Vector2(main.X + hsv.Y * main.Width, main.Y + (1 - hsv.Z) * main.Height);
            else if (PickerShape == ColorPickerShape.OkHsRectangle) position = new Vector2(main.X + hsl.X * main.Width, main.Y + (1 - hsl.Y) * main.Height);
            else if (PickerShape == ColorPickerShape.OkHlRectangle) position = new Vector2(main.X + hsl.X * main.Width, main.Y + (1 - hsl.Z) * main.Height);
            else
            {
                var hue = PickerShape == ColorPickerShape.OkHslCircle ? hsl.X : hsv.X; var saturation = PickerShape == ColorPickerShape.OkHslCircle ? hsl.Y : hsv.Y;
                var radius = Math.Min(main.Width, main.Height) * .5f * saturation; position = new Vector2(main.Center.X + MathF.Cos(hue * MathHelper.TwoPi) * radius, main.Center.Y + MathF.Sin(hue * MathHelper.TwoPi) * radius);
            }
            var background = GetThemeIcon("picker_cursor_bg");
            var cursor = GetThemeIcon("picker_cursor");
            if (background.HasValue) context.Icon(background.Value, new Vector2(position.X - background.Value.LogicalSize.X / 2, position.Y - background.Value.LogicalSize.Y / 2), Color.White);
            if (cursor.HasValue) context.Icon(cursor.Value, new Vector2(position.X - cursor.Value.LogicalSize.X / 2, position.Y - cursor.Value.LogicalSize.Y / 2), Color.White);
            if (!background.HasValue && !cursor.HasValue) context.Border(new Rectangle((int)position.X - 3, (int)position.Y - 3, 7, 7), Color.White, 1);
        }
        internal static Color FromHsv(float hue, float saturation, float value, float alpha)
        {
            hue = ((hue % 360) + 360) % 360;
            var chroma = value * saturation;
            var secondary = chroma * (1 - Math.Abs((hue / 60) % 2 - 1));
            var match = value - chroma;
            float red = 0, green = 0, blue = 0;
            if (hue < 60) { red = chroma; green = secondary; }
            else if (hue < 120) { red = secondary; green = chroma; }
            else if (hue < 180) { green = chroma; blue = secondary; }
            else if (hue < 240) { green = secondary; blue = chroma; }
            else if (hue < 300) { red = secondary; blue = chroma; }
            else { red = chroma; blue = secondary; }
            return new Color(red + match, green + match, blue + match, alpha);
        }
        private static Color ParseHtml(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new FormatException("A color is required.");
            value = value.Trim();
            if (value[0] == '#') value = value.Substring(1);
            else if (!IsHexDigits(value))
            {
                if (NamedColors.TryGetValue(NormalizeColorName(value), out var named)) return named;
                throw new FormatException("The color name is not recognized.");
            }
            if (!IsHexDigits(value)) throw new FormatException("Colors must use a recognized name or hexadecimal notation.");
            // Godot's _html_submitted auto-corrects malformed lengths the way tools like Figma do,
            // before parsing - #1 -> #111111, #12 -> #121212, #12345 -> truncate to #1234, #1234567 ->
            // truncate to #123456.
            if (value.Length == 1) value = new string(value[0], 6);
            else if (value.Length == 2) value = string.Concat(value, value, value);
            else if (value.Length == 5) value = value.Substring(0, 4);
            else if (value.Length == 7) value = value.Substring(0, 6);
            // Standard CSS 3/4-digit shorthand: each hex digit is doubled (#RGB -> RRGGBB, #RGBA -> RRGGBBAA).
            if (value.Length == 3 || value.Length == 4)
            {
                var expanded = new System.Text.StringBuilder();
                foreach (var digit in value) { expanded.Append(digit); expanded.Append(digit); }
                value = expanded.ToString();
            }
            if (value.Length != 6 && value.Length != 8) throw new FormatException("Colors must use RRGGBB or RRGGBBAA hexadecimal notation.");
            byte Parse(int offset) => Convert.ToByte(value.Substring(offset, 2), 16);
            return new Color(Parse(0), Parse(2), Parse(4), value.Length == 8 ? Parse(6) : (byte)255);
        }
        private static Dictionary<string, Color> CreateNamedColors()
        {
            return new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(Color.Transparent)] = Color.Transparent,
                [nameof(Color.AliceBlue)] = Color.AliceBlue,
                [nameof(Color.AntiqueWhite)] = Color.AntiqueWhite,
                [nameof(Color.Aqua)] = Color.Aqua,
                [nameof(Color.Aquamarine)] = Color.Aquamarine,
                [nameof(Color.Azure)] = Color.Azure,
                [nameof(Color.Beige)] = Color.Beige,
                [nameof(Color.Bisque)] = Color.Bisque,
                [nameof(Color.Black)] = Color.Black,
                [nameof(Color.BlanchedAlmond)] = Color.BlanchedAlmond,
                [nameof(Color.Blue)] = Color.Blue,
                [nameof(Color.BlueViolet)] = Color.BlueViolet,
                [nameof(Color.Brown)] = Color.Brown,
                [nameof(Color.BurlyWood)] = Color.BurlyWood,
                [nameof(Color.CadetBlue)] = Color.CadetBlue,
                [nameof(Color.Chartreuse)] = Color.Chartreuse,
                [nameof(Color.Chocolate)] = Color.Chocolate,
                [nameof(Color.Coral)] = Color.Coral,
                [nameof(Color.CornflowerBlue)] = Color.CornflowerBlue,
                [nameof(Color.Cornsilk)] = Color.Cornsilk,
                [nameof(Color.Crimson)] = Color.Crimson,
                [nameof(Color.Cyan)] = Color.Cyan,
                [nameof(Color.DarkBlue)] = Color.DarkBlue,
                [nameof(Color.DarkCyan)] = Color.DarkCyan,
                [nameof(Color.DarkGoldenrod)] = Color.DarkGoldenrod,
                [nameof(Color.DarkGray)] = Color.DarkGray,
                [nameof(Color.DarkGreen)] = Color.DarkGreen,
                [nameof(Color.DarkKhaki)] = Color.DarkKhaki,
                [nameof(Color.DarkMagenta)] = Color.DarkMagenta,
                [nameof(Color.DarkOliveGreen)] = Color.DarkOliveGreen,
                [nameof(Color.DarkOrange)] = Color.DarkOrange,
                [nameof(Color.DarkOrchid)] = Color.DarkOrchid,
                [nameof(Color.DarkRed)] = Color.DarkRed,
                [nameof(Color.DarkSalmon)] = Color.DarkSalmon,
                [nameof(Color.DarkSeaGreen)] = Color.DarkSeaGreen,
                [nameof(Color.DarkSlateBlue)] = Color.DarkSlateBlue,
                [nameof(Color.DarkSlateGray)] = Color.DarkSlateGray,
                [nameof(Color.DarkTurquoise)] = Color.DarkTurquoise,
                [nameof(Color.DarkViolet)] = Color.DarkViolet,
                [nameof(Color.DeepPink)] = Color.DeepPink,
                [nameof(Color.DeepSkyBlue)] = Color.DeepSkyBlue,
                [nameof(Color.DimGray)] = Color.DimGray,
                [nameof(Color.DodgerBlue)] = Color.DodgerBlue,
                [nameof(Color.Firebrick)] = Color.Firebrick,
                [nameof(Color.FloralWhite)] = Color.FloralWhite,
                [nameof(Color.ForestGreen)] = Color.ForestGreen,
                [nameof(Color.Fuchsia)] = Color.Fuchsia,
                [nameof(Color.Gainsboro)] = Color.Gainsboro,
                [nameof(Color.GhostWhite)] = Color.GhostWhite,
                [nameof(Color.Gold)] = Color.Gold,
                [nameof(Color.Goldenrod)] = Color.Goldenrod,
                [nameof(Color.Gray)] = Color.Gray,
                [nameof(Color.Green)] = Color.Green,
                [nameof(Color.GreenYellow)] = Color.GreenYellow,
                [nameof(Color.Honeydew)] = Color.Honeydew,
                [nameof(Color.HotPink)] = Color.HotPink,
                [nameof(Color.IndianRed)] = Color.IndianRed,
                [nameof(Color.Indigo)] = Color.Indigo,
                [nameof(Color.Ivory)] = Color.Ivory,
                [nameof(Color.Khaki)] = Color.Khaki,
                [nameof(Color.Lavender)] = Color.Lavender,
                [nameof(Color.LavenderBlush)] = Color.LavenderBlush,
                [nameof(Color.LawnGreen)] = Color.LawnGreen,
                [nameof(Color.LemonChiffon)] = Color.LemonChiffon,
                [nameof(Color.LightBlue)] = Color.LightBlue,
                [nameof(Color.LightCoral)] = Color.LightCoral,
                [nameof(Color.LightCyan)] = Color.LightCyan,
                [nameof(Color.LightGoldenrodYellow)] = Color.LightGoldenrodYellow,
                [nameof(Color.LightGray)] = Color.LightGray,
                [nameof(Color.LightGreen)] = Color.LightGreen,
                [nameof(Color.LightPink)] = Color.LightPink,
                [nameof(Color.LightSalmon)] = Color.LightSalmon,
                [nameof(Color.LightSeaGreen)] = Color.LightSeaGreen,
                [nameof(Color.LightSkyBlue)] = Color.LightSkyBlue,
                [nameof(Color.LightSlateGray)] = Color.LightSlateGray,
                [nameof(Color.LightSteelBlue)] = Color.LightSteelBlue,
                [nameof(Color.LightYellow)] = Color.LightYellow,
                [nameof(Color.Lime)] = Color.Lime,
                [nameof(Color.LimeGreen)] = Color.LimeGreen,
                [nameof(Color.Linen)] = Color.Linen,
                [nameof(Color.Magenta)] = Color.Magenta,
                [nameof(Color.Maroon)] = Color.Maroon,
                [nameof(Color.MediumAquamarine)] = Color.MediumAquamarine,
                [nameof(Color.MediumBlue)] = Color.MediumBlue,
                [nameof(Color.MediumOrchid)] = Color.MediumOrchid,
                [nameof(Color.MediumPurple)] = Color.MediumPurple,
                [nameof(Color.MediumSeaGreen)] = Color.MediumSeaGreen,
                [nameof(Color.MediumSlateBlue)] = Color.MediumSlateBlue,
                [nameof(Color.MediumSpringGreen)] = Color.MediumSpringGreen,
                [nameof(Color.MediumTurquoise)] = Color.MediumTurquoise,
                [nameof(Color.MediumVioletRed)] = Color.MediumVioletRed,
                [nameof(Color.MidnightBlue)] = Color.MidnightBlue,
                [nameof(Color.MintCream)] = Color.MintCream,
                [nameof(Color.MistyRose)] = Color.MistyRose,
                [nameof(Color.Moccasin)] = Color.Moccasin,
                [nameof(Color.NavajoWhite)] = Color.NavajoWhite,
                [nameof(Color.Navy)] = Color.Navy,
                [nameof(Color.OldLace)] = Color.OldLace,
                [nameof(Color.Olive)] = Color.Olive,
                [nameof(Color.OliveDrab)] = Color.OliveDrab,
                [nameof(Color.Orange)] = Color.Orange,
                [nameof(Color.OrangeRed)] = Color.OrangeRed,
                [nameof(Color.Orchid)] = Color.Orchid,
                [nameof(Color.PaleGoldenrod)] = Color.PaleGoldenrod,
                [nameof(Color.PaleGreen)] = Color.PaleGreen,
                [nameof(Color.PaleTurquoise)] = Color.PaleTurquoise,
                [nameof(Color.PaleVioletRed)] = Color.PaleVioletRed,
                [nameof(Color.PapayaWhip)] = Color.PapayaWhip,
                [nameof(Color.PeachPuff)] = Color.PeachPuff,
                [nameof(Color.Peru)] = Color.Peru,
                [nameof(Color.Pink)] = Color.Pink,
                [nameof(Color.Plum)] = Color.Plum,
                [nameof(Color.PowderBlue)] = Color.PowderBlue,
                [nameof(Color.Purple)] = Color.Purple,
                [nameof(Color.Red)] = Color.Red,
                [nameof(Color.RosyBrown)] = Color.RosyBrown,
                [nameof(Color.RoyalBlue)] = Color.RoyalBlue,
                [nameof(Color.SaddleBrown)] = Color.SaddleBrown,
                [nameof(Color.Salmon)] = Color.Salmon,
                [nameof(Color.SandyBrown)] = Color.SandyBrown,
                [nameof(Color.SeaGreen)] = Color.SeaGreen,
                [nameof(Color.SeaShell)] = Color.SeaShell,
                [nameof(Color.Sienna)] = Color.Sienna,
                [nameof(Color.Silver)] = Color.Silver,
                [nameof(Color.SkyBlue)] = Color.SkyBlue,
                [nameof(Color.SlateBlue)] = Color.SlateBlue,
                [nameof(Color.SlateGray)] = Color.SlateGray,
                [nameof(Color.Snow)] = Color.Snow,
                [nameof(Color.SpringGreen)] = Color.SpringGreen,
                [nameof(Color.SteelBlue)] = Color.SteelBlue,
                [nameof(Color.Tan)] = Color.Tan,
                [nameof(Color.Teal)] = Color.Teal,
                [nameof(Color.Thistle)] = Color.Thistle,
                [nameof(Color.Tomato)] = Color.Tomato,
                [nameof(Color.Turquoise)] = Color.Turquoise,
                [nameof(Color.Violet)] = Color.Violet,
                [nameof(Color.Wheat)] = Color.Wheat,
                [nameof(Color.White)] = Color.White,
                [nameof(Color.WhiteSmoke)] = Color.WhiteSmoke,
                [nameof(Color.Yellow)] = Color.Yellow,
                [nameof(Color.YellowGreen)] = Color.YellowGreen,
            };
        }
        private static string NormalizeColorName(string value) => value.Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty);
        private static bool IsHexDigits(string value)
        {
            if (value.Length == 0) return false;
            foreach (var c in value) if (!Uri.IsHexDigit(c)) return false;
            return true;
        }
    }

    /// <summary>Reusable popup panel that hosts and synchronizes a <see cref="ColorPicker"/>.</summary>
    public sealed class ColorPickerPopupPanel : PopupPanel
    {
        public ColorPickerPopupPanel()
        {
            Picker = new ColorPicker();
            Picker.ColorChanged += (_, color) => ColorChanged?.Invoke(this, color);
            AddChild(Picker);
        }
        public ColorPicker Picker { get; }
        public Color Color { get => Picker.Color; set => Picker.Color = value; }
        public event Action<ColorPickerPopupPanel, Color> ColorChanged;
        public override Vector2 GetMinimumSize() => Vector2.Max(CustomMinimumSize, Picker.GetMinimumSize());
        protected override void ArrangeChildren()
        {
            base.ArrangeChildren();
            Picker.Position = Vector2.Zero;
            Picker.Size = Size;
        }
    }

    /// <summary>Button that displays a color and exposes a child <see cref="ColorPicker"/> popup.</summary>
    public sealed class ColorPickerButton : BaseButton
    {
        private Color _color = Color.White;
        private readonly ColorPickerPopupPanel _popup;
        public ColorPickerButton()
        {
            _popup = new ColorPickerPopupPanel { Size = new Vector2(180, 140), Visible = false };
            Picker = _popup.Picker;
            // Godot only re-emits color_changed on the BUTTON via the picker's own interactive
            // color_changed signal (_color_changed) - never from a bare property assignment, matching
            // the picker-side fix that made Color's own setter quiet.
            Picker.ColorChanged += (_, color) => { if (_color != color) { _color = color; ColorChanged?.Invoke(this, color); } };
            _popup.PopupShown += (_, _) => { Picker.OldColor = Color; Picker.DisplayOldColor = true; };
            // Godot's ColorPickerButton::_modal_closed reverts to old_color and re-emits color_changed
            // only when the popup was dismissed via Escape (ui_cancel), and unconditionally emits
            // popup_closed regardless of the dismissal reason.
            _popup.PopupHidden += (_, reason) =>
            {
                if (reason == PopupHideReason.Cancelled && _color != Picker.OldColor)
                {
                    _color = Picker.OldColor; Picker.Color = _color;
                    ColorChanged?.Invoke(this, _color);
                }
                PopupClosed?.Invoke(this, EventArgs.Empty);
            };
        }
        public ColorPicker Picker { get; }
        public ColorPickerPopupPanel Popup => _popup;
        public bool EditAlpha { get => Picker.EditAlpha; set => Picker.EditAlpha = value; }
        // Matches Godot's ColorPickerButton::set_pick_color: updates the color and syncs the picker
        // quietly, without firing color_changed itself.
        public Color Color
        {
            get => _color;
            set
            {
                if (_color == value) return;
                _color = value;
                if (Picker.Color != value) Picker.Color = value;
            }
        }
        public event Action<ColorPickerButton, Color> ColorChanged;
        /// <summary>Raised whenever the picker popup closes, matching Godot's ColorPickerButton.popup_closed.</summary>
        public event EventHandler PopupClosed;
        internal override void PointerReleased(Point position, bool isInside)
        {
            base.PointerReleased(position, isInside);
            if (!isInside || Context == null) return;
            _popup.Size = Picker.GetMinimumSize();
            if (_popup.Context != Context) Context.Add(_popup);
            _popup.PopupAt(new Vector2(Bounds.Left, Bounds.Bottom));
        }
    }

    /// <summary>Popup color chooser with an accept/cancel lifecycle.</summary>
    public sealed class ColorPickerDialog : ConfirmationDialog
    {
        public ColorPickerDialog()
        {
            Picker = new ColorPicker();
            AddChild(Picker);
        }
        public ColorPicker Picker { get; }
        public Color Color { get => Picker.Color; set => Picker.Color = value; }
        public event Action<ColorPickerDialog, Color> ColorSelected;
        protected override void ArrangeChildren()
        {
            base.ArrangeChildren();
            Picker.Position = new Vector2(6, 24);
            Picker.Size = Vector2.Max(Vector2.Zero, Size - new Vector2(12, 30));
        }
        public override void Confirm()
        {
            ColorSelected?.Invoke(this, Color);
            base.Confirm();
        }
    }
}
