// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Forma
{
    public sealed class AccessibilityChangedEventArgs : EventArgs
    {
        public AccessibilityChangedEventArgs(string propertyName) => PropertyName = propertyName;
        public string PropertyName { get; }
    }

    public enum AccessibilityRole
    {
        Generic,
        Button,
        CheckBox,
        Link,
        Slider,
        ProgressBar,
        ScrollBar,
        TextBox,
        SpinButton,
        ComboBox,
        TabList,
        TabPanel,
        ScrollView,
        Window,
        Dialog,
        Menu,
        MenuBar,
        List,
        ListItem,
        Tree,
        TreeItem,
        Grid,
        Row,
        Cell,
        Document,
        Group,
        Canvas,
        ColorPicker,
        Viewport,
        Joystick
    }

    [Flags]
    public enum AccessibilityActions
    {
        None = 0,
        Focus = 1 << 0,
        Press = 1 << 1,
        Toggle = 1 << 2,
        Increment = 1 << 3,
        Decrement = 1 << 4,
        SetValue = 1 << 5,
        Select = 1 << 6,
        Expand = 1 << 7,
        Collapse = 1 << 8,
        Scroll = 1 << 9
    }

    [Flags]
    public enum AccessibilityStates
    {
        None = 0,
        Disabled = 1 << 0,
        Focused = 1 << 1,
        Checked = 1 << 2,
        Selected = 1 << 3,
        Current = 1 << 4,
        Expanded = 1 << 5,
        Collapsed = 1 << 6,
        Offscreen = 1 << 7,
        ReadOnly = 1 << 8,
        Modal = 1 << 9
    }

    public class AccessibilityPeer
    {
        internal AccessibilityPeer(Control owner) => Owner = owner ?? throw new ArgumentNullException(nameof(owner));

        public Control Owner { get; }
        public virtual AccessibilityRole Role => Owner.AccessibilityRole;
        /// <summary>Process-unique identity of the owning control, stable for its lifetime.</summary>
        public virtual int Id => Owner.AccessibilityId;
        /// <summary>The owner's author-assigned <see cref="Control.AutomationId"/>, empty when unset.</summary>
        public virtual string AutomationId => Owner.AutomationId ?? string.Empty;
        public virtual string Name => Owner.AccessibilityName;
        public virtual string Value => Owner.AccessibilityValue;
        public virtual AccessibilityActions Actions => Owner.AccessibilityActions;
        public virtual AccessibilityStates States => Owner.AccessibilityStates;
        public virtual Rectangle Bounds => Owner.AccessibilityBounds;
        public virtual bool IsOffscreen => (States & AccessibilityStates.Offscreen) != 0;
        public virtual IReadOnlyList<AccessibilityPeer> Children => Owner.GetAccessibilityChildren();

        /// <summary>
        /// Performs one of the actions this peer advertises in <see cref="Actions"/>, returning
        /// whether it was handled. Asking for an action that is not advertised returns false rather
        /// than throwing, so a caller can probe without guarding every call.
        /// </summary>
        public virtual bool Invoke(AccessibilityActions action, object argument = null)
        {
            if ((Actions & action) == 0) return false;
            return Owner.PerformAccessibilityAction(action, argument);
        }
    }

    public sealed class ItemAccessibilityPeer : AccessibilityPeer
    {
        private readonly ItemsControl _itemsOwner;
        private readonly object _token;

        internal ItemAccessibilityPeer(ItemsControl owner, object token) : base(owner)
        {
            _itemsOwner = owner;
            _token = token;
        }

        private readonly int _id = Control.AllocateAccessibilityId();
        /// <summary>
        /// This item's own identity, not the owning items control's: every item is a distinct node,
        /// and a snapshot keyed by id would otherwise collapse them all onto the list itself.
        /// Allocated once per slot, and slots outlive scrolling, so it is stable while realized.
        /// </summary>
        public override int Id => _id;
        /// <summary>Always empty: an automation id is author-assigned to a control, and an item is
        /// data rather than a control. Items are addressed by role, name or index.</summary>
        public override string AutomationId => string.Empty;
        public int Index => _itemsOwner.GetAccessibilityItemIndex(_token);
        public object Item => _itemsOwner.GetAccessibilityItem(_token);
        public override AccessibilityRole Role => _itemsOwner.GetAccessibilityItemRole(Index);
        public override string Name => Item?.ToString() ?? string.Empty;
        public override AccessibilityActions Actions => AccessibilityActions.Select | AccessibilityActions.Focus;
        public override AccessibilityStates States
        {
            get
            {
                var index = Index;
                var states = index < 0 || !_itemsOwner.TryGetAccessibilityItemContainer(index, out _)
                    ? AccessibilityStates.Offscreen
                    : AccessibilityStates.None;
                if (index >= 0 && _itemsOwner.IsAccessibilityItemSelected(index)) states |= AccessibilityStates.Selected;
                if (index >= 0 && _itemsOwner.IsAccessibilityItemCurrent(index)) states |= AccessibilityStates.Current;
                return states;
            }
        }
        public override Rectangle Bounds =>
            Index >= 0 && _itemsOwner.TryGetAccessibilityItemContainer(Index, out var container)
                ? container.AccessibilityBounds
                : Rectangle.Empty;
        public override IReadOnlyList<AccessibilityPeer> Children => Array.Empty<AccessibilityPeer>();
    }
}