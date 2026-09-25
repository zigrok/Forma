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
        Joystick,

        // Appended rather than inserted: these are ordinals, and a snapshot or a golden tree
        // recorded before a renumbering would silently mean something else afterwards.

        /// <summary>One entry of a menu. Distinct from ListItem: an assistive technology announces
        /// a menu entry differently, and a menu is not a list.</summary>
        MenuItem
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

        /// <summary>
        /// Whether this peer describes something that is not a control — a virtualized list row, a
        /// menu entry — and therefore has to be published by its owner.
        /// <para>
        /// The tree walk reaches real controls on its own, so publishing those twice would list
        /// every control once as a child and once as a peer. Virtual peers are the ones with no
        /// control to be reached through.
        /// </para>
        /// </summary>
        public virtual bool IsVirtual => false;
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
        /// <inheritdoc />
        public override bool IsVirtual => true;
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

    /// <summary>
    /// One entry of a <see cref="PopupMenu"/>.
    /// </summary>
    /// <remarks>
    /// A menu draws its entries rather than building a control for each, so without this a menu is
    /// an empty node: a screen reader announces "menu" and stops, and an automation client sees a
    /// container with nothing in it. That is the one place in a Forma application where the whole
    /// command surface can go missing at once.
    /// </remarks>
    public sealed class MenuItemAccessibilityPeer : AccessibilityPeer
    {
        private readonly PopupMenu _menu;
        private readonly int _id = Control.AllocateAccessibilityId();

        internal MenuItemAccessibilityPeer(PopupMenu menu, int index) : base(menu)
        {
            _menu = menu;
            Index = index;
        }

        /// <summary>Position in the menu's item list, including hidden entries and separators.</summary>
        public int Index { get; }

        /// <summary>This entry's own identity, distinct from the menu's.</summary>
        public override int Id => _id;

        /// <summary>Always true: an entry is drawn by the menu, not built as a control.</summary>
        public override bool IsVirtual => true;

        /// <summary>Always empty: an entry is data rather than a control, so it has no automation id.</summary>
        public override string AutomationId => string.Empty;

        /// <summary>Always <see cref="AccessibilityRole.MenuItem"/>.</summary>
        public override AccessibilityRole Role => AccessibilityRole.MenuItem;

        /// <summary>The entry's text, which is what an assistive technology announces.</summary>
        public override string Name => _menu.GetAccessibilityItemText(Index);

        /// <summary>Always empty: a menu entry is a command, not a value.</summary>
        public override string Value => string.Empty;

        /// <summary>Press and Focus, plus Toggle for a checkable entry.</summary>
        public override AccessibilityActions Actions =>
            AccessibilityActions.Press | AccessibilityActions.Focus |
            (_menu.IsAccessibilityItemCheckable(Index) ? AccessibilityActions.Toggle : AccessibilityActions.None);

        /// <summary>
        /// Disabled, Checked, Current for the highlighted entry, and Expanded or Collapsed for one
        /// that owns a submenu.
        /// </summary>
        public override AccessibilityStates States => _menu.GetAccessibilityItemStates(Index);

        /// <summary>The entry's rectangle in global coordinates, matching what pointer input uses.</summary>
        public override Rectangle Bounds => _menu.GetAccessibilityItemBounds(Index);

        /// <summary>Always empty. A submenu is a separate menu, reached when it opens.</summary>
        public override IReadOnlyList<AccessibilityPeer> Children => Array.Empty<AccessibilityPeer>();

        /// <summary>
        /// Activates this entry rather than the menu. The base implementation forwards to the
        /// owning control, which for a menu would mean "press the menu" and do nothing useful.
        /// </summary>
        public override bool Invoke(AccessibilityActions action, object argument = null)
        {
            if ((Actions & action) == 0) return false;
            return _menu.PerformAccessibilityItemAction(Index, action);
        }
    }

    /// <summary>
    /// One visible row of a <see cref="Tree"/>.
    /// </summary>
    /// <remarks>
    /// A <see cref="TreeItem"/> is data the tree draws, not a control, so without this a tree is an
    /// empty node with a scrollbar. That is worse than it sounds for a navigation tree, which is
    /// usually the only way into the rest of an application: everything behind it becomes
    /// unreachable rather than merely unannounced.
    /// </remarks>
    public sealed class TreeItemAccessibilityPeer : AccessibilityPeer
    {
        private readonly Tree _tree;
        private readonly TreeItem _item;
        private readonly int _id = Control.AllocateAccessibilityId();

        internal TreeItemAccessibilityPeer(Tree tree, TreeItem item) : base(tree)
        {
            _tree = tree;
            _item = item;
        }

        /// <summary>The row this peer describes.</summary>
        public TreeItem Item => _item;

        /// <summary>This row's own identity, distinct from the tree's.</summary>
        public override int Id => _id;

        /// <summary>Always true: a row is drawn by the tree rather than built as a control.</summary>
        public override bool IsVirtual => true;

        /// <summary>Always empty: a row is data rather than a control.</summary>
        public override string AutomationId => string.Empty;

        /// <summary>Always <see cref="AccessibilityRole.TreeItem"/>.</summary>
        public override AccessibilityRole Role => AccessibilityRole.TreeItem;

        /// <summary>The row's text in the first column, which is what is announced.</summary>
        public override string Name => _tree.GetAccessibilityItemText(_item);

        /// <summary>Always empty: a row's text is its name, not a value.</summary>
        public override string Value => string.Empty;

        /// <summary>Select and Focus, plus Expand or Collapse for a row that has children.</summary>
        public override AccessibilityActions Actions => _tree.GetAccessibilityItemActions(_item);

        /// <summary>Selected, Current, Expanded or Collapsed, and Disabled for an unselectable row.</summary>
        public override AccessibilityStates States => _tree.GetAccessibilityItemStates(_item);

        /// <summary>The row's rectangle in global coordinates.</summary>
        public override Rectangle Bounds => _tree.GetItemAreaRectangle(_item);

        /// <summary>
        /// Always empty. Child rows are published as siblings, because the tree already flattens
        /// them into the order they are drawn and a snapshot is a flat list.
        /// </summary>
        public override IReadOnlyList<AccessibilityPeer> Children => Array.Empty<AccessibilityPeer>();

        /// <summary>Acts on this row rather than on the tree.</summary>
        public override bool Invoke(AccessibilityActions action, object argument = null)
        {
            if ((Actions & action) == 0) return false;
            return _tree.PerformAccessibilityItemAction(_item, action);
        }
    }
}