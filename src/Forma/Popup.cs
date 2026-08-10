// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// These control APIs and behaviors are adapted from Godot Engine's Popup and PopupPanel implementations;
// see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    public enum PopupHideReason { Programmatic, OutsideClick, Cancelled }

    /// <summary>
    /// A top-level transient panel. Modal popups gate pointer/focus input to their subtree and can
    /// optionally dismiss themselves when the user presses outside their bounds.
    /// </summary>
    public class Popup : TemplatedControl
    {
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.Window;
        public override AccessibilityStates AccessibilityStates => base.AccessibilityStates | AccessibilityStates.Modal;
        private Control _focusBeforePopup;
        public Popup() { FocusMode = FocusMode.All; Modal = true; HideOnOutsideClick = true; }
        public Color? BackgroundColor { get; set; }
        public Color? BorderColor { get; set; }
        public int BorderWidth { get; set; } = 1;
        public bool Modal { get; set; }
        /// <summary>Prevents an outside pointer press from dismissing this modal popup.</summary>
        public bool Exclusive { get; set; }
        public bool HideOnOutsideClick { get; set; }
        public event EventHandler PopupShown;
        public event Action<Popup, PopupHideReason> PopupHidden;
        public void PopupAt(Vector2 position)
        {
            _focusBeforePopup = Context?.FocusedControl;
            Position = position;
            var wasVisible = Visible;
            Visible = true;
            GrabFocus();
            if (!wasVisible) PopupShown?.Invoke(this, EventArgs.Empty);
        }
        public void Hide(PopupHideReason reason = PopupHideReason.Programmatic)
        {
            if (!Visible) return;
            Visible = false;
            var priorFocus = _focusBeforePopup;
            _focusBeforePopup = null;
            if (Context?.FocusedControl != null && IsAncestorOf(Context.FocusedControl)) Context.SetFocus(null);
            if (priorFocus != null && priorFocus.Context == Context && priorFocus.Visible && priorFocus.Enabled && priorFocus.FocusMode != FocusMode.None)
                priorFocus.GrabFocus();
            PopupHidden?.Invoke(this, reason);
        }
        internal void OutsidePointerPressed(Point point)
        {
            if (HideOnOutsideClick && !Exclusive) Hide(PopupHideReason.OutsideClick);
        }
        internal bool IsAncestorOf(Control descendant)
        {
            for (var control = descendant; control != null; control = control.VisualParent)
                if (ReferenceEquals(control, this)) return true;
            return false;
        }
        internal override void KeyPressed(Keys key)
        {
            if (key == Keys.Escape) Hide(PopupHideReason.Cancelled);
            else base.KeyPressed(key);
        }
    }
    /// <summary>Presents popup content on a panel-styled surface with standard popup dismissal behavior.</summary>
    public class PopupPanel : Popup { }
}
