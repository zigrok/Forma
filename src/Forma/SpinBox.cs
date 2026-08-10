// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// This control API and behavior are adapted from Godot Engine's SpinBox implementation;
// see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    /// <summary>Editable numeric field owned by a <see cref="SpinBox"/>.</summary>
    public sealed class SpinBoxLineEdit : LineEdit
    {
        internal SpinBoxLineEdit(SpinBox owner) { Owner = owner; }
        public SpinBox Owner { get; }
        internal override void KeyPressed(Keys key)
        {
            if (key == Keys.Up) Owner.StepArrow(true);
            else if (key == Keys.Down) Owner.StepArrow(false);
            else base.KeyPressed(key);
        }
    }

    /// <summary>Edits a numeric range through text entry, arrow stepping, keyboard input, or vertical dragging.</summary>
    [TemplatePart(EditorPartName, typeof(ContentPresenter))]
    public sealed class SpinBox : Range
    {
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.SpinButton;
        public const string EditorPartName = "PART_Editor";
        private readonly UIFontSelection _fontSelection = new UIFontSelection();
        private HorizontalAlignment _horizontalAlignment;
        private bool _updateOnTextChanged;
        private bool _syncingText;
        private bool _dragAllowed;
        private bool _dragging;
        private Point _dragStart;
        private int _lastDragY;
        private float _dragBaseValue;
        private float _dragDiffY;
        private Point _heldArrowPoint;
        private double _heldArrowElapsed;
        private bool _heldArrowRepeating;
        private bool _heldArrowActive;
        private const double ArrowRepeatDelaySeconds = 0.6;
        private const double ArrowRepeatIntervalSeconds = 0.075;
        public SpinBox()
        {
            FocusMode = FocusMode.All;
            LineEdit = new SpinBoxLineEdit(this) { MouseFilter = MouseFilter.Stop };
            LineEdit.TextSubmitted += (_, _) => CommitText();
            LineEdit.TextChanged += (_, _) => { if (UpdateOnTextChanged && !_syncingText) CommitText(onlyIfValid: true); };
            ValueChanged += (_, _) => SyncText();
            AddChild(LineEdit);
            SyncText();
        }
        public string Prefix { get; set; } = string.Empty;
        public string Suffix { get; set; } = string.Empty;
        public bool UpdateOnTextChanged { get => _updateOnTextChanged; set => _updateOnTextChanged = value; }
        public float CustomArrowStep { get; set; }
        public bool CustomArrowRound { get; set; }
        public bool IsDraggingValue => _dragging;
        public SpriteFont Font { get => _fontSelection.SpriteFont; set { _fontSelection.SetSpriteFont(value); LineEdit.Font = value; QueueLayout(); } }
        public UIFont UIFont { get => _fontSelection.UIFont; set { _fontSelection.SetUIFont(value); LineEdit.UIFont = value; QueueLayout(); } }
        internal UIFont EffectiveUIFont => ResolveFont(_fontSelection);
        public SpinBoxLineEdit LineEdit { get; }
        public void SetHorizontalAlignment(HorizontalAlignment alignment) { if (!Enum.IsDefined(typeof(HorizontalAlignment), alignment)) throw new ArgumentOutOfRangeException(nameof(alignment)); _horizontalAlignment = alignment; }
        public HorizontalAlignment GetHorizontalAlignment() => _horizontalAlignment;
        public void SetPrefix(string prefix) { Prefix = prefix ?? string.Empty; SyncText(); }
        public string GetPrefix() => Prefix;
        public void SetSuffix(string suffix) { Suffix = suffix ?? string.Empty; SyncText(); }
        public string GetSuffix() => Suffix;
        public void SetUpdateOnTextChanged(bool enabled) => UpdateOnTextChanged = enabled;
        public bool GetUpdateOnTextChanged() => UpdateOnTextChanged;
        public void SetSelectAllOnFocus(bool enabled) => LineEdit.SetSelectAllOnFocus(enabled);
        public bool IsSelectAllOnFocus() => LineEdit.IsSelectAllOnFocus();
        public void SetEditable(bool enabled) => LineEdit.Editable = enabled;
        public bool IsEditable() => LineEdit.Editable;
        public void SetCustomArrowStep(float customArrowStep) => CustomArrowStep = Math.Max(0, customArrowStep);
        public float GetCustomArrowStep() => CustomArrowStep;
        public void SetCustomArrowRound(bool round) => CustomArrowRound = round;
        public bool IsCustomArrowRounding() => CustomArrowRound;
        public void Apply() => CommitText();
        public LineEdit GetLineEdit() => LineEdit;
        public override Vector2 GetMinimumSize() => Vector2.Max(CustomMinimumSize, new Vector2(72, 24));
        internal override void PointerPressed(Point point)
        {
            base.PointerPressed(point);
            _dragAllowed = IsEditable();
            _dragging = false;
            _dragStart = point;
            _lastDragY = point.Y;
            _dragBaseValue = Value;
            _dragDiffY = 0;
            if (!IsEditable()) return;
            StepArrow(point.Y < Bounds.Center.Y);
            _heldArrowPoint = point;
            _heldArrowElapsed = 0;
            _heldArrowRepeating = false;
            _heldArrowActive = IsPointOnArrowButton(point);
        }
        internal override void PointerMoved(Point point)
        {
            _heldArrowPoint = point;
            if (!_dragAllowed) return;
            if (!_dragging)
            {
                var dx = point.X - _dragStart.X;
                var dy = point.Y - _dragStart.Y;
                if (dx * dx + dy * dy <= 4) return;
                _dragging = true;
                _heldArrowActive = false;
                _dragBaseValue = Value;
                _dragDiffY = 0;
                _lastDragY = point.Y;
                return;
            }
            _dragDiffY += point.Y - _lastDragY;
            _lastDragY = point.Y;
            var step = Step == 0 ? 1 : Step;
            var diff = -0.01f * MathF.Pow(MathF.Abs(_dragDiffY), 1.8f) * MathF.Sign(_dragDiffY);
            Value = _dragBaseValue + step * diff;
        }
        internal override void PointerReleased(Point point, bool isInside)
        {
            _dragAllowed = false;
            _dragging = false;
            _heldArrowActive = false;
            _heldArrowRepeating = false;
            _heldArrowElapsed = 0;
        }
        internal override void KeyPressed(Keys key) { if (key == Keys.Up) StepArrow(true); else if (key == Keys.Down) StepArrow(false); }
        internal override void Process(GameTime gameTime)
        {
            ProcessHeldArrowRepeat(gameTime);
            base.Process(gameTime);
        }
        internal void DrawSpinBoxChrome(UIRenderContext context)
        {
            context.Fill(Bounds, context.Theme.BackgroundColor); context.Border(Bounds, context.Theme.PanelBorderColor);
            context.Fill(new Rectangle(Bounds.Right - 16, Bounds.Top, 16, Math.Max(1, Bounds.Height / 2)), context.Theme.HoverColor);
            context.Fill(new Rectangle(Bounds.Right - 16, Bounds.Center.Y, 16, Math.Max(1, Bounds.Height - Bounds.Height / 2)), context.Theme.HoverColor);
            DrawArrow(context, true);
            DrawArrow(context, false);
        }
        private void DrawArrow(UIRenderContext context, bool up)
        {
            var suffix = !Enabled || !IsEditable() ? "_disabled" : _heldArrowActive && TryGetArrowButton(_heldArrowPoint, out var heldUp) && heldUp == up ? "_pressed" : string.Empty;
            var icon = GetThemeIcon((up ? "up" : "down") + suffix);
            if (!icon.HasValue) return;
            var half = up ? new Rectangle(Bounds.Right - 16, Bounds.Top, 16, Math.Max(1, Bounds.Height / 2)) : new Rectangle(Bounds.Right - 16, Bounds.Center.Y, 16, Math.Max(1, Bounds.Height - Bounds.Height / 2));
            context.Icon(icon.Value, new Vector2(half.Center.X - icon.Value.LogicalSize.X / 2, half.Center.Y - icon.Value.LogicalSize.Y / 2), Color.White);
        }
        internal void StepArrow(bool up)
        {
            if (!IsEditable()) return;
            var arrowStep = CustomArrowStep != 0 ? CustomArrowStep : Step;
            if (CustomArrowRound)
            {
                // Godot's SpinBox::_arrow_clicked: pre-snap arrow_step itself to a multiple of Step, snap
                // the CURRENT value to the nearest multiple of that arrow_step, and only step-and-resnap
                // if that snap didn't actually move in the requested direction (e.g. already exactly on
                // a multiple, or the nearest multiple undershoots).
                arrowStep = SnapToMultiple(arrowStep, Step);
                var newValue = SnapToStep(Value, arrowStep);
                if ((up && newValue <= Value) || (!up && newValue >= Value)) newValue = SnapToStep(Value + (up ? arrowStep : -arrowStep), arrowStep);
                Value = newValue;
            }
            else Value += up ? arrowStep : -arrowStep;
        }
        private void ProcessHeldArrowRepeat(GameTime gameTime)
        {
            if (!_heldArrowActive || _dragging || !IsEditable()) return;
            _heldArrowElapsed += gameTime.ElapsedGameTime.TotalSeconds;
            var delay = _heldArrowRepeating ? ArrowRepeatIntervalSeconds : ArrowRepeatDelaySeconds;
            while (_heldArrowElapsed >= delay)
            {
                _heldArrowElapsed -= delay;
                _heldArrowRepeating = true;
                delay = ArrowRepeatIntervalSeconds;
                if (TryGetArrowButton(_heldArrowPoint, out var up))
                    StepArrow(up);
            }
        }
        private bool IsPointOnArrowButton(Point point) => TryGetArrowButton(point, out _);
        private bool TryGetArrowButton(Point point, out bool up)
        {
            up = point.Y < Bounds.Center.Y;
            if (Bounds.Width <= 0 || Bounds.Height <= 0) return false;
            if (point.X < Bounds.Right - 16 || point.X >= Bounds.Right) return false;
            if (point.Y < Bounds.Top || point.Y >= Bounds.Bottom) return false;
            return true;
        }
        private void CommitText(bool onlyIfValid = false)
        {
            var text = StripAffixes(LineEdit.Text);
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) Value = value;
            else if (!onlyIfValid) SyncText();
        }
        private string StripAffixes(string text)
        {
            text = text ?? string.Empty;
            if (!string.IsNullOrEmpty(Prefix) && text.StartsWith(Prefix + " ", StringComparison.Ordinal)) text = text.Substring(Prefix.Length + 1);
            if (!string.IsNullOrEmpty(Suffix) && text.EndsWith(" " + Suffix, StringComparison.Ordinal)) text = text.Substring(0, text.Length - Suffix.Length - 1);
            return text;
        }
        private void SyncText()
        {
            _syncingText = true;
            var value = Value.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(Prefix)) value = Prefix + " " + value;
            if (!string.IsNullOrEmpty(Suffix)) value += " " + Suffix;
            LineEdit.Text = value;
            _syncingText = false;
        }
    }

}
