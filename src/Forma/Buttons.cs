// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// These control APIs and behaviors are adapted from Godot Engine's BaseButton, Button,
// CheckBox, and CheckButton implementations; see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    /// <summary>Chooses whether a button activates on pointer press or pointer release.</summary>
    public enum ButtonActionMode
    {
        Press,
        Release,
    }

    /// <summary>Provides pointer, keyboard, shortcut, toggle, and button-group activation behavior for button controls.</summary>
    public class BaseButton : ContentControl
    {
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.Button;
        public override string AccessibilityName => string.IsNullOrEmpty(base.AccessibilityName) ? Text ?? string.Empty : base.AccessibilityName;
        public override AccessibilityActions AccessibilityActions => base.AccessibilityActions | AccessibilityActions.Press |
            (ToggleMode ? AccessibilityActions.Toggle : AccessibilityActions.None);
        public override AccessibilityStates AccessibilityStates => base.AccessibilityStates |
            (ButtonPressed ? AccessibilityStates.Checked : AccessibilityStates.None);
        private readonly UIFontSelection _fontSelection = new UIFontSelection();
        private bool _pressed;
        private bool _activationHandled;
        private bool _buttonPressed;
        private float _shortcutFeedbackRemaining;
        private PointerButton _activePointerButton;
        private Keys? _activeKey;
        private ButtonGroup _buttonGroup;
        private string _text = string.Empty;
        public BaseButton()
        {
            FocusMode = FocusMode.All;
            Padding = new Thickness(8, 4, 8, 4);
        }
        public string Text
        {
            get => _text;
            set
            {
                value ??= string.Empty;
                if (_text == value) return;
                _text = value;
                OnPropertyChanged(nameof(Text));
                QueueLayout();
            }
        }
        public SpriteFont Font { get => _fontSelection.SpriteFont; set { _fontSelection.SetSpriteFont(value); QueueLayout(); } }
        public UIFont UIFont { get => _fontSelection.UIFont; set { _fontSelection.SetUIFont(value); QueueLayout(); } }
        internal UIFont EffectiveUIFont => ResolveFont(_fontSelection);
        public Thickness Padding { get; set; }
        /// <summary>Horizontal text placement for text-bearing buttons.</summary>
        public HorizontalAlignment TextAlignment { get; set; } = HorizontalAlignment.Center;
        /// <summary>Optional Godot-style icon displayed alongside the button text.</summary>
        public Texture2D Icon { get; set; }
        public bool ExpandIcon { get; set; }
        public HorizontalAlignment IconAlignment { get; set; } = HorizontalAlignment.Left;
        public VerticalAlignment VerticalIconAlignment { get; set; } = VerticalAlignment.Center;
        public float IconSeparation { get; set; } = 4;
        public Color IconModulate { get; set; } = Color.White;
        internal Func<ThemeIcon?> DecorativeIconProvider { get; set; }
        internal bool HideTextWhenDecorativeIconAvailable { get; set; }
        /// <summary>Suppresses the default button panel while retaining focus feedback.</summary>
        public bool Flat { get; set; }
        public bool ToggleMode { get; set; }
        public ButtonActionMode ActionMode { get; set; } = ButtonActionMode.Release;
        /// <summary>Whether a release outside the button still completes an active pointer press.</summary>
        public bool KeepPressedOutside { get; set; }
        /// <summary>Physical pointer buttons that can activate this button, matching Godot's button_mask flags.</summary>
        public ButtonMouseMask ButtonMask { get; set; } = ButtonMouseMask.Left;
        /// <summary>Optional retained shortcut that activates this button without requiring focus.</summary>
        public PopupMenuShortcut Shortcut { get; private set; }
        /// <summary>Whether shortcut text should be appended to this button's tooltip, matching Godot's shortcut_in_tooltip.</summary>
        public bool ShortcutInTooltip { get; set; } = true;
        /// <summary>Whether shortcut activation should temporarily use pressed drawing feedback.</summary>
        public bool ShortcutFeedback { get; set; } = true;
        /// <summary>Seconds of retained visual feedback after shortcut activation.</summary>
        public float ShortcutFeedbackDuration { get; set; } = .2f;
        /// <summary>Gets or sets the toggled state, emitting <see cref="Toggled"/> when it changes.</summary>
        public bool ButtonPressed { get => _buttonPressed; set => SetPressed(value, true); }
        /// <summary>Gets or sets the mutual-exclusion group used by this button.</summary>
        public ButtonGroup ButtonGroup
        {
            get => _buttonGroup;
            set
            {
                if (ReferenceEquals(_buttonGroup, value)) return;
                _buttonGroup?.Unregister(this);
                _buttonGroup = value;
                _buttonGroup?.Register(this);
            }
        }
        public bool IsHovering { get; private set; }
        public bool IsPressing => _pressed;
        protected bool WasActivatedByPointer { get; private set; }
        public bool IsShortcutFeedbackActive => _shortcutFeedbackRemaining > 0;
        /// <summary>Whether the button should currently render as pressed, matching Godot's BaseButton::get_draw_mode() DRAW_PRESSED case. Unlike <see cref="IsPressing"/>/release activation, this combines with <see cref="KeepPressedOutside"/> while dragging outside the button's bounds. A keyboard-driven press is always visually pressed, matching Godot's status.pressing_inside being forced true for the accept-action path.</summary>
        public bool IsVisuallyPressed => (_pressed && (IsHovering || KeepPressedOutside || _activeKey != null)) || ButtonPressed || IsShortcutFeedbackActive;
        public override bool IsPseudoStateActive(string state) => state switch
        {
            "pressed" => IsVisuallyPressed,
            "checked" => ButtonPressed,
            _ => base.IsPseudoStateActive(state),
        };
        public event EventHandler Pressed;
        public event EventHandler ButtonDown;
        public event EventHandler ButtonUp;
        public event Action<BaseButton, bool> Toggled;
        public void SetShortcut(PopupMenuShortcut shortcut) => Shortcut = shortcut;
        public PopupMenuShortcut GetShortcut() => Shortcut;
        public void SetShortcutInTooltip(bool enabled) => ShortcutInTooltip = enabled;
        public bool IsShortcutInTooltipEnabled() => ShortcutInTooltip;
        public void SetShortcutFeedback(bool enabled) { ShortcutFeedback = enabled; if (!enabled) _shortcutFeedbackRemaining = 0; }
        public bool IsShortcutFeedback() => ShortcutFeedback;
        public void SetToggleMode(bool enabled) { ToggleMode = enabled; if (!enabled) SetPressedDirect(false, false); }
        public bool IsToggleMode() => ToggleMode;
        public void SetActionMode(ButtonActionMode mode) { if (!Enum.IsDefined(typeof(ButtonActionMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode)); ActionMode = mode; }
        public ButtonActionMode GetActionMode() => ActionMode;
        public void SetButtonMask(ButtonMouseMask mask) { if ((mask & ~(ButtonMouseMask.Left | ButtonMouseMask.Right | ButtonMouseMask.Middle)) != 0) throw new ArgumentOutOfRangeException(nameof(mask)); ButtonMask = mask; }
        public ButtonMouseMask GetButtonMask() => ButtonMask;
        public void SetKeepPressedOutside(bool enabled) => KeepPressedOutside = enabled;
        public bool IsKeepPressedOutside() => KeepPressedOutside;
        public void SetDisabled(bool disabled) => Enabled = !disabled;
        public bool IsDisabled() => !Enabled;
        /// <summary>Sets the pressed state without emitting <see cref="Toggled"/>.</summary>
        public void SetPressedNoSignal(bool pressed) => SetPressed(pressed, false);
        public void SetPressed(bool pressed) => SetPressed(pressed, true);
        /// <summary>Matches Godot's BaseButton::is_pressed: the toggled state for a toggle button, or whether the pointer/key is currently held down otherwise.</summary>
        public bool IsPressed() => ToggleMode ? ButtonPressed : IsPressing;
        public bool IsHovered() => IsHovering;
        /// <summary>Calculates the button's content and template minimum size before applying <see cref="Control.CustomMinimumSize"/>.</summary>
        protected Vector2 GetButtonMinimumSizeWithoutCustomMinimum()
        {
            var text = EffectiveUIFont == null ? Vector2.Zero : TextMetrics.Measure(EffectiveUIFont, Text ?? string.Empty);
            var decorativeIcon = Icon == null ? DecorativeIconProvider?.Invoke() : null;
            if (decorativeIcon.HasValue && HideTextWhenDecorativeIconAvailable) text = Vector2.Zero;
            var icon = ExpandIcon ? Vector2.Zero : Icon != null ? new Vector2(Icon.Width, Icon.Height) : decorativeIcon.HasValue ? decorativeIcon.Value.LogicalSize.ToVector2() : Vector2.Zero;
            if (icon != Vector2.Zero)
            {
                text.Y = VerticalIconAlignment == VerticalAlignment.Center ? Math.Max(text.Y, icon.Y) : text.Y + icon.Y;
                text.X = IconAlignment == HorizontalAlignment.Center ? Math.Max(text.X, icon.X) : text.X + icon.X + (text.X > 0 ? IconSeparation : 0);
            }
            return Vector2.Max(TemplateRoot?.GetMinimumSize() ?? Vector2.Zero, text + new Vector2(Padding.Horizontal, Padding.Vertical));
        }
        public override Vector2 GetMinimumSize() => Vector2.Max(CustomMinimumSize, GetButtonMinimumSizeWithoutCustomMinimum());
        /// <summary>Calculates local text placement independent of a font renderer.</summary>
        public Vector2 GetTextPosition(Vector2 textSize)
        {
            var x = TextAlignment == HorizontalAlignment.Left ? Padding.Left
                : TextAlignment == HorizontalAlignment.Right ? Size.X - Padding.Right - textSize.X
                : (Size.X - textSize.X) / 2;
            return new Vector2(MathF.Max(Padding.Left, x), MathF.Max(Padding.Top, (Size.Y - textSize.Y) / 2));
        }
        internal Vector2 GetTextPosition(Vector2 textSize, Vector2 iconSize)
        {
            var position = GetTextPosition(textSize);
            if (iconSize == Vector2.Zero || IconAlignment == HorizontalAlignment.Center) return position;
            var icon = GetIconRectangle(iconSize);
            position.X = icon.Center.X < Size.X / 2f
                ? MathF.Max(position.X, icon.Right + IconSeparation)
                : MathF.Min(position.X, icon.Left - IconSeparation - textSize.X);
            return new Vector2(MathF.Max(Padding.Left, position.X), position.Y);
        }
        /// <summary>Calculates the local icon rectangle for the configured icon alignment and expansion mode.</summary>
        public Rectangle GetIconRectangle(Vector2 iconSize)
        {
            if (iconSize.X <= 0 || iconSize.Y <= 0) return Rectangle.Empty;
            var size = iconSize;
            if (ExpandIcon)
            {
                var available = Vector2.Max(Vector2.Zero, Size - new Vector2(Padding.Horizontal, Padding.Vertical));
                var scale = Math.Min(available.X / iconSize.X, available.Y / iconSize.Y);
                size *= Math.Max(0, scale);
            }
            var alignment = IconAlignment;
            if (IsLayoutRtl()) alignment = alignment == HorizontalAlignment.Left ? HorizontalAlignment.Right : alignment == HorizontalAlignment.Right ? HorizontalAlignment.Left : alignment;
            var x = alignment == HorizontalAlignment.Right ? Size.X - Padding.Right - size.X : alignment == HorizontalAlignment.Center ? (Size.X - size.X) / 2 : Padding.Left;
            var y = VerticalIconAlignment == VerticalAlignment.Bottom ? Size.Y - Padding.Bottom - size.Y : VerticalIconAlignment == VerticalAlignment.Center ? (Size.Y - size.Y) / 2 : Padding.Top;
            return new Rectangle((int)MathF.Round(MathF.Max(Padding.Left, x)), (int)MathF.Round(MathF.Max(Padding.Top, y)), Math.Max(0, (int)MathF.Round(size.X)), Math.Max(0, (int)MathF.Round(size.Y)));
        }
        internal override bool HitTestBeforeChildren(Point point) => ContainsPoint(point);
        internal override void PointerEntered() { IsHovering = true; base.PointerEntered(); NotifyPseudoStateChanged("pressed"); }
        internal override void PointerExited() { IsHovering = false; base.PointerExited(); NotifyPseudoStateChanged("pressed"); }
        internal override void PointerPressed(Point position)
        {
            if (IsPointerButtonMasked(PointerButton.Left)) BeginPointerActivation(position, PointerButton.Left);
        }
        internal override void PointerButtonPressed(Point position, PointerButton button)
        {
            if (button == PointerButton.Left) return;
            if (!IsPointerButtonMasked(button)) { base.PointerButtonPressed(position, button); return; }
            BeginPointerActivation(position, button);
        }
        internal override void PointerReleased(Point position, bool isInside)
        {
            if (_activePointerButton == PointerButton.Left) EndPointerActivation(isInside);
        }
        internal override void PointerButtonReleased(Point position, PointerButton button)
        {
            if (button == PointerButton.Left) return;
            if (_activePointerButton == button) EndPointerActivation(ContainsPoint(position));
        }
        internal override void KeyPressed(Keys key)
        {
            // Godot's BaseButton::gui_input returns immediately for every input kind, including
            // the ui_accept action, whenever the button is disabled.
            if (!Enabled) return;
            if (key != Keys.Enter && key != Keys.Space) return;
            if (_pressed) return;
            _activeKey = key;
            _pressed = true;
            ButtonDown?.Invoke(this, EventArgs.Empty);
            NotifyPseudoStateChanged("pressed");
            // Godot's on_action_event treats the accept action like every other input kind: it honors
            // action_mode (activating immediately on press, or waiting for the matching release below).
            _activationHandled = ActionMode == ButtonActionMode.Press;
            if (_activationHandled) Activate();
        }
        /// <summary>Completes a keyboard-driven press, matching Godot's on_action_event release branch: button_up always fires, and release-mode activation happens here.</summary>
        internal override void KeyReleased(Keys key)
        {
            if (_activeKey != key) return;
            _activeKey = null;
            var activate = _pressed && !_activationHandled;
            _pressed = false;
            _activationHandled = false;
            ButtonUp?.Invoke(this, EventArgs.Empty);
            NotifyPseudoStateChanged("pressed");
            if (activate) Activate();
        }
        internal override bool ShortcutInput(Keys key, KeyboardState keyboard)
        {
            if (!Enabled || !Visible || Shortcut == null || !Shortcut.Matches(key, keyboard)) return false;
            Activate();
            if (ShortcutFeedback)
            {
                _shortcutFeedbackRemaining = Math.Max(0, ShortcutFeedbackDuration);
                NotifyPseudoStateChanged("pressed");
            }
            return true;
        }
        internal override void Process(GameTime gameTime)
        {
            var wasActive = IsShortcutFeedbackActive;
            if (_shortcutFeedbackRemaining > 0)
                _shortcutFeedbackRemaining = Math.Max(0, _shortcutFeedbackRemaining - (float)gameTime.ElapsedGameTime.TotalSeconds);
            if (wasActive != IsShortcutFeedbackActive) NotifyPseudoStateChanged("pressed");
            base.Process(gameTime);
        }
        public override string GetTooltip(Point position)
        {
            var tooltip = base.GetTooltip(position) ?? string.Empty;
            if (!ShortcutInTooltip || Shortcut == null) return tooltip;
            var shortcutText = Shortcut.DisplayText;
            if (string.IsNullOrEmpty(shortcutText)) return tooltip;
            var shortcutName = Shortcut.Name ?? string.Empty;
            if (string.IsNullOrEmpty(tooltip))
                return string.IsNullOrEmpty(shortcutName) ? shortcutText : $"{shortcutName} ({shortcutText})";
            if (!string.IsNullOrEmpty(shortcutName) && string.Equals(tooltip, shortcutName, StringComparison.OrdinalIgnoreCase))
                return $"{tooltip} ({shortcutText})";
            return $"{tooltip}\n{(string.IsNullOrEmpty(shortcutName) ? shortcutText : $"{shortcutName} ({shortcutText})")}";
        }
        private bool IsPointerButtonMasked(PointerButton button)
        {
            switch (button)
            {
                case PointerButton.Left: return (ButtonMask & ButtonMouseMask.Left) != 0;
                case PointerButton.Right: return (ButtonMask & ButtonMouseMask.Right) != 0;
                case PointerButton.Middle: return (ButtonMask & ButtonMouseMask.Middle) != 0;
                default: return false;
            }
        }
        private void BeginPointerActivation(Point position, PointerButton button)
        {
            base.PointerPressed(position);
            if (_pressed) return;
            _activePointerButton = button;
            _pressed = true;
            ButtonDown?.Invoke(this, EventArgs.Empty);
            NotifyPseudoStateChanged("pressed");
            _activationHandled = ActionMode == ButtonActionMode.Press;
            if (_activationHandled) Activate(true);
        }
        private void EndPointerActivation(bool isInside)
        {
            // Godot's on_action_event gates real activation on status.pressing_inside alone;
            // keep_pressed_outside only widens get_draw_mode's visual "pressed" state (see Draw()).
            var wasPressing = _pressed;
            var activate = wasPressing && isInside && !_activationHandled;
            _pressed = false;
            _activePointerButton = PointerButton.None;
            _activationHandled = false;
            if (wasPressing) ButtonUp?.Invoke(this, EventArgs.Empty);
            if (wasPressing) NotifyPseudoStateChanged("pressed");
            if (activate) Activate(true);
        }
        private void Activate(bool fromPointer = false)
        {
            WasActivatedByPointer = fromPointer;
            try
            {
                if (ToggleMode)
                {
                    if (ButtonPressed && _buttonGroup != null && !_buttonGroup.AllowUnpress)
                    {
                        // Godot's on_action_event unconditionally re-fires the group's pressed signal here even
                        // though _unpress_group() reasserts pressed=true and nothing actually changes.
                        _buttonGroup.NotifyPressed(this);
                        Toggled?.Invoke(this, true);
                    }
                    else
                        SetPressed(!ButtonPressed, true);
                }
                Pressed?.Invoke(this, EventArgs.Empty);
            }
            finally { WasActivatedByPointer = false; }
        }
        private bool SetPressed(bool pressed, bool emitSignal)
        {
            if (!ToggleMode) return false;
            if (_buttonGroup != null) return _buttonGroup.SetPressed(this, pressed, emitSignal);
            return SetPressedDirect(pressed, emitSignal);
        }
        internal bool SetPressedDirect(bool pressed, bool emitSignal)
        {
            if (_buttonPressed == pressed) return false;
            _buttonPressed = pressed;
            if (emitSignal) Toggled?.Invoke(this, pressed);
            NotifyPseudoStateChanged("checked");
            NotifyPseudoStateChanged("pressed");
            return true;
        }
    }

    /// <summary>Maintains Godot-style mutually exclusive toggle buttons.</summary>
    public sealed class ButtonGroup
    {
        private readonly List<BaseButton> _buttons = new List<BaseButton>();
        /// <summary>Whether the selected button may be unpressed, leaving no selection.</summary>
        public bool AllowUnpress { get; set; }
        public IReadOnlyList<BaseButton> Buttons => _buttons;
        public BaseButton PressedButton
        {
            get
            {
                foreach (var button in _buttons) if (button.ButtonPressed) return button;
                return null;
            }
        }
        internal void Register(BaseButton button)
        {
            if (!_buttons.Contains(button)) _buttons.Add(button);
            if (button.ButtonPressed) SetPressed(button, true, false);
        }
        internal void Unregister(BaseButton button) => _buttons.Remove(button);
        internal bool SetPressed(BaseButton button, bool pressed, bool emitSignal)
        {
            if (!_buttons.Contains(button)) return button.SetPressedDirect(pressed, emitSignal);
            if (pressed)
            {
                foreach (var other in _buttons)
                    if (!ReferenceEquals(other, button)) other.SetPressedDirect(false, emitSignal);
                var changed = button.SetPressedDirect(true, emitSignal);
                if (changed) Pressed?.Invoke(this, button);
                return changed;
            }
            return button.SetPressedDirect(false, emitSignal);
        }
        /// <summary>Fires <see cref="Pressed"/> without changing any button's state, matching Godot re-firing button_group's pressed signal when re-activating the already-pressed member of a non-allow-unpress group.</summary>
        internal void NotifyPressed(BaseButton button) => Pressed?.Invoke(this, button);
        /// <summary>Raised when a member becomes the active button.</summary>
        public event Action<ButtonGroup, BaseButton> Pressed;
    }

    /// <summary>Presents clickable text, icon, or custom content using the standard button appearance.</summary>
    public sealed class Button : BaseButton { }

    /// <summary>Toggles a persistent checked state and displays a check-box or radio-group state icon.</summary>
    public class CheckBox : BaseButton
    {
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.CheckBox;
        public bool Checked { get => ButtonPressed; set => ButtonPressed = value; }
        public CheckBox() { ToggleMode = true; Padding = new Thickness(8, 4, 8, 4); }
        public override Vector2 GetMinimumSize()
        {
            var icon = GetStateIcon();
            var result = base.GetMinimumSize();
            if (icon.HasValue)
            {
                result.X += icon.Value.LogicalSize.X + IconSeparation;
                result.Y = Math.Max(result.Y, icon.Value.LogicalSize.Y + Padding.Vertical);
            }
            return Vector2.Max(CustomMinimumSize, result);
        }
        internal ThemeIcon? GetStateIcon() => GetThemeIcon(GetStateIconName());
        private string GetStateIconName()
        {
            var name = ButtonGroup != null ? (Checked ? "radio_checked" : "radio_unchecked") : Checked ? "checked" : "unchecked";
            if (!Enabled) name += "_disabled";
            if (this is CheckButton && IsLayoutRtl()) name += "_mirrored";
            return name;
        }
    }

    /// <summary>Toggle button variant that shares check-box semantics without a box glyph.</summary>
    public sealed class CheckButton : CheckBox { }

}
