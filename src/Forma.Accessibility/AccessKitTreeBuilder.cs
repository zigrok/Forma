// SPDX-License-Identifier: MIT
// Copyright (c) Igor Hipólito Vieira

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Forma.Accessibility
{
    /// <summary>
    /// The neutral half of the bridge: turns an <see cref="AccessibilityTreeSnapshot"/> into an
    /// AccessKit tree update. Knows nothing about macOS, Windows or any window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every update is a full tree. AccessKit supports sending only changed nodes, but doing so
    /// correctly means re-sending each changed node's parent as well — nodes are replaced wholesale,
    /// never patched, and a parent's child list is part of the parent. A Forma tree is hundreds of
    /// nodes, not hundreds of thousands, and <see cref="AccessibilityTree.Capture"/> already walks
    /// all of them; getting incremental updates subtly wrong costs far more than rebuilding does.
    /// </para>
    /// <para>
    /// Node ids pass through unchanged. Forma's are process-unique 32-bit ints and AccessKit's are
    /// 64-bit, so widening is lossless and an id means the same thing on both sides — which is what
    /// lets an action request come back and be dispatched without a lookup table.
    /// </para>
    /// </remarks>
    internal static class AccessKitTreeBuilder
    {
        /// <summary>
        /// Reserved for the tree's root. Forma reports a root node's parent as 0 rather than naming
        /// a window, and AccessKit requires exactly one root that owns everything, so the bridge
        /// supplies one.
        /// </summary>
        internal const ulong RootId = 1;

        /// <summary>
        /// Builds an update AccessKit takes ownership of. The caller hands it straight to an adapter
        /// and never frees it.
        /// </summary>
        internal static IntPtr Build(AccessibilityTreeSnapshot snapshot, string windowTitle)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            var focus = snapshot.FocusedId > 0 ? (ulong)snapshot.FocusedId : RootId;
            var update = AccessKit.accesskit_tree_update_with_capacity_and_focus(
                (nuint)(snapshot.Nodes.Count + 1), focus);

            AttachTreeInfo(update);

            var childrenByParent = GroupChildren(snapshot);

            AccessKit.accesskit_tree_update_push_node(update, RootId, BuildRoot(snapshot, windowTitle, childrenByParent));

            foreach (var node in snapshot.Nodes)
            {
                childrenByParent.TryGetValue(node.Id, out var children);
                AccessKit.accesskit_tree_update_push_node(update, (ulong)node.Id, BuildNode(node, children));
            }

            return update;
        }

        /// <summary>
        /// A tree with nothing in it but the window. Used when the real tree cannot be produced —
        /// AccessKit's update factory may not return null, so "no tree" has to be expressed as an
        /// empty one.
        /// </summary>
        internal static IntPtr BuildEmpty(string windowTitle)
        {
            var update = AccessKit.accesskit_tree_update_with_capacity_and_focus(1, RootId);
            AttachTreeInfo(update);

            var root = AccessKit.accesskit_node_new(AccessKitRole.Window);
            SetLabel(root, windowTitle ?? string.Empty);
            AccessKit.accesskit_tree_update_push_node(update, RootId, root);
            return update;
        }

        /// <summary>
        /// The tree description is required on the first update after an assistive technology
        /// activates, and harmless afterwards. Sending it every time costs one small allocation and
        /// removes a "have we already?" flag that would be wrong exactly once — at a reconnect.
        /// </summary>
        private static void AttachTreeInfo(IntPtr update)
        {
            var info = AccessKit.accesskit_tree_info_new(RootId);
            SetToolkit(info);
            AccessKit.accesskit_tree_update_set_tree_info(update, info);
        }

        private static Dictionary<int, List<ulong>> GroupChildren(AccessibilityTreeSnapshot snapshot)
        {
            // Snapshot order is depth-first in draw order, so appending in order preserves it — and
            // reading order is what a screen reader announces.
            var children = new Dictionary<int, List<ulong>>();
            foreach (var node in snapshot.Nodes)
            {
                if (node.IsRoot) continue;
                if (!children.TryGetValue(node.ParentId, out var list))
                {
                    list = new List<ulong>();
                    children[node.ParentId] = list;
                }

                list.Add((ulong)node.Id);
            }

            return children;
        }

        private static unsafe IntPtr BuildRoot(
            AccessibilityTreeSnapshot snapshot,
            string windowTitle,
            Dictionary<int, List<ulong>> childrenByParent)
        {
            var root = AccessKit.accesskit_node_new(AccessKitRole.Window);
            SetLabel(root, windowTitle ?? string.Empty);

            var roots = new List<ulong>();
            foreach (var node in snapshot.Nodes)
                if (node.IsRoot) roots.Add((ulong)node.Id);

            SetChildren(root, roots);
            return root;
        }

        private static IntPtr BuildNode(AccessibilityNode node, List<ulong> children)
        {
            var handle = AccessKit.accesskit_node_new(MapRole(node.Role));

            SetLabel(handle, node.Name);
            if (node.Value.Length > 0) SetValue(handle, node.Value);

            // Bounds are already global and in the same units pointer input uses, which is what
            // AccessKit wants: an assistive technology hit-tests with them.
            AccessKit.accesskit_node_set_bounds(handle, new AccessKitRect
            {
                X0 = node.Bounds.X,
                Y0 = node.Bounds.Y,
                X1 = node.Bounds.X + node.Bounds.Width,
                Y1 = node.Bounds.Y + node.Bounds.Height,
            });

            ApplyNumericValue(handle, node);
            ApplyToggled(handle, node);
            ApplyActions(handle, node.Actions);
            ApplyStates(handle, node.States);
            if (children != null) SetChildren(handle, children);

            return handle;
        }

        /// <summary>
        /// Reports a slider's position as a number as well as a string.
        /// </summary>
        /// <remarks>
        /// macOS reads a slider's <c>AXValue</c> as a number, and AccessKit derives that from the
        /// node's numeric value rather than from its text. Setting only the string leaves every
        /// slider announcing zero no matter where its thumb is — which looks like the bridge
        /// working, because the element is there and named.
        /// </remarks>
        private static void ApplyNumericValue(IntPtr handle, AccessibilityNode node)
        {
            switch (node.Role)
            {
                case AccessibilityRole.Slider:
                case AccessibilityRole.ProgressBar:
                case AccessibilityRole.ScrollBar:
                case AccessibilityRole.SpinButton:
                    break;
                default:
                    return;
            }

            if (double.TryParse(node.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                AccessKit.accesskit_node_set_numeric_value(handle, value);
        }

        private static void ApplyActions(IntPtr node, AccessibilityActions actions)
        {
            if ((actions & AccessibilityActions.Focus) != 0) AccessKit.accesskit_node_add_action(node, AccessKitAction.Focus);
            // Press and Toggle both become Click: macOS has one AXPress, and the distinction Forma
            // draws is about what the control does with it, not about what was asked.
            if ((actions & (AccessibilityActions.Press | AccessibilityActions.Toggle)) != 0)
                AccessKit.accesskit_node_add_action(node, AccessKitAction.Click);
            if ((actions & AccessibilityActions.Increment) != 0) AccessKit.accesskit_node_add_action(node, AccessKitAction.Increment);
            if ((actions & AccessibilityActions.Decrement) != 0) AccessKit.accesskit_node_add_action(node, AccessKitAction.Decrement);
            if ((actions & AccessibilityActions.SetValue) != 0) AccessKit.accesskit_node_add_action(node, AccessKitAction.SetValue);
            if ((actions & AccessibilityActions.Select) != 0) AccessKit.accesskit_node_add_action(node, AccessKitAction.Click);
            if ((actions & AccessibilityActions.Expand) != 0) AccessKit.accesskit_node_add_action(node, AccessKitAction.Expand);
            if ((actions & AccessibilityActions.Collapse) != 0) AccessKit.accesskit_node_add_action(node, AccessKitAction.Collapse);
        }

        /// <summary>
        /// Reports a checkable node's state, including when it is *not* checked.
        /// </summary>
        /// <remarks>
        /// Setting the toggled state only when checked is not the same as reporting it: a node that
        /// says nothing is announced as not checkable at all, so an unchecked box and a label read
        /// identically. Which nodes are checkable is decided by role, because that is the only
        /// thing that distinguishes "unchecked" from "has no such concept".
        /// </remarks>
        private static void ApplyToggled(IntPtr node, AccessibilityNode described)
        {
            switch (described.Role)
            {
                case AccessibilityRole.CheckBox:
                case AccessibilityRole.MenuItem:
                    break;
                default:
                    // Anything else only reports a toggled state when it actually has one.
                    if ((described.States & AccessibilityStates.Checked) != 0)
                        AccessKit.accesskit_node_set_toggled(node, AccessKitToggled.True);
                    return;
            }

            AccessKit.accesskit_node_set_toggled(
                node,
                (described.States & AccessibilityStates.Checked) != 0 ? AccessKitToggled.True : AccessKitToggled.False);
        }

        private static void ApplyStates(IntPtr node, AccessibilityStates states)
        {
            if ((states & AccessibilityStates.Disabled) != 0) AccessKit.accesskit_node_set_disabled(node);
            if ((states & AccessibilityStates.ReadOnly) != 0) AccessKit.accesskit_node_set_read_only(node);
            if ((states & AccessibilityStates.Offscreen) != 0) AccessKit.accesskit_node_set_hidden(node);
            if ((states & AccessibilityStates.Selected) != 0) AccessKit.accesskit_node_set_selected(node, true);
        }

        /// <summary>
        /// Forma's roles to AccessKit's. Every lossy mapping is called out: a role that is merely
        /// approximate still announces something sensible, whereas an unmapped one announces
        /// nothing.
        /// </summary>
        internal static AccessKitRole MapRole(AccessibilityRole role)
        {
            switch (role)
            {
                case AccessibilityRole.Button: return AccessKitRole.Button;
                case AccessibilityRole.CheckBox: return AccessKitRole.CheckBox;
                case AccessibilityRole.Link: return AccessKitRole.Link;
                case AccessibilityRole.Slider: return AccessKitRole.Slider;
                case AccessibilityRole.ProgressBar: return AccessKitRole.ProgressIndicator;
                case AccessibilityRole.ScrollBar: return AccessKitRole.ScrollBar;
                case AccessibilityRole.TextBox: return AccessKitRole.TextInput;
                case AccessibilityRole.SpinButton: return AccessKitRole.SpinButton;
                case AccessibilityRole.ComboBox: return AccessKitRole.ComboBox;
                case AccessibilityRole.TabList: return AccessKitRole.TabList;
                case AccessibilityRole.TabPanel: return AccessKitRole.TabPanel;
                case AccessibilityRole.ScrollView: return AccessKitRole.ScrollView;
                case AccessibilityRole.Window: return AccessKitRole.Window;
                case AccessibilityRole.Dialog: return AccessKitRole.Dialog;
                case AccessibilityRole.Menu: return AccessKitRole.Menu;
                case AccessibilityRole.MenuBar: return AccessKitRole.MenuBar;
                case AccessibilityRole.List: return AccessKitRole.List;
                case AccessibilityRole.ListItem: return AccessKitRole.ListItem;
                case AccessibilityRole.MenuItem: return AccessKitRole.MenuItem;
                case AccessibilityRole.Tree: return AccessKitRole.Tree;
                case AccessibilityRole.TreeItem: return AccessKitRole.TreeItem;
                case AccessibilityRole.Grid: return AccessKitRole.Grid;
                case AccessibilityRole.Row: return AccessKitRole.Row;
                case AccessibilityRole.Cell: return AccessKitRole.Cell;
                case AccessibilityRole.Document: return AccessKitRole.Document;
                case AccessibilityRole.Group: return AccessKitRole.Group;
                case AccessibilityRole.Canvas: return AccessKitRole.Canvas;

                // Lossy. AccessKit has no colour-picker role; ColorWell is what a native colour
                // control reports on macOS, so it announces correctly even though the composite
                // picker is richer than a well.
                case AccessibilityRole.ColorPicker: return AccessKitRole.ColorWell;

                // Lossy. A Forma viewport is an embedded scene, not a document or an image;
                // Group at least announces it as a container with children rather than as nothing.
                case AccessibilityRole.Viewport: return AccessKitRole.Group;

                // Lossy, and the worst of the three. A virtual joystick is a two-axis continuous
                // input with no AccessKit equivalent; Slider announces a value and accepts
                // increment/decrement, which is closer than Generic but still describes one axis
                // where there are two.
                case AccessibilityRole.Joystick: return AccessKitRole.Slider;

                default: return AccessKitRole.GenericContainer;
            }
        }

        /// <summary>AccessKit's actions back to Forma's, for dispatching a platform request.</summary>
        internal static AccessibilityActions MapAction(AccessKitAction action)
        {
            switch (action)
            {
                case AccessKitAction.Click: return AccessibilityActions.Press;
                case AccessKitAction.Focus: return AccessibilityActions.Focus;
                case AccessKitAction.Increment: return AccessibilityActions.Increment;
                case AccessKitAction.Decrement: return AccessibilityActions.Decrement;
                case AccessKitAction.SetValue: return AccessibilityActions.SetValue;
                case AccessKitAction.Expand: return AccessibilityActions.Expand;
                case AccessKitAction.Collapse: return AccessibilityActions.Collapse;

                // Deliberately not guessed at. A handler that receives None ignores it, which is
                // better than dispatching an action the platform did not ask for.
                default: return AccessibilityActions.None;
            }
        }

        private static unsafe void SetChildren(IntPtr node, List<ulong> children)
        {
            if (children.Count == 0) return;

            var array = children.ToArray();
            fixed (ulong* pointer = array)
            {
                AccessKit.accesskit_node_set_children(node, (nuint)array.Length, pointer);
            }
        }

        private static unsafe void SetLabel(IntPtr node, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            fixed (byte* pointer = bytes)
            {
                // The _with_length variants take the length rather than scanning for a NUL, so a
                // label containing one is truncated by the platform rather than by us.
                AccessKit.accesskit_node_set_label_with_length(node, pointer, (nuint)bytes.Length);
            }
        }

        private static unsafe void SetValue(IntPtr node, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            fixed (byte* pointer = bytes)
            {
                AccessKit.accesskit_node_set_value_with_length(node, pointer, (nuint)bytes.Length);
            }
        }

        private static unsafe void SetToolkit(IntPtr info)
        {
            var name = Encoding.UTF8.GetBytes("Forma");
            fixed (byte* pointer = name)
            {
                AccessKit.accesskit_tree_info_set_toolkit_name_with_length(info, pointer, (nuint)name.Length);
            }

            var version = Encoding.UTF8.GetBytes(typeof(AccessKitTreeBuilder).Assembly.GetName().Version?.ToString() ?? "0.0.0");
            fixed (byte* pointer = version)
            {
                AccessKit.accesskit_tree_info_set_toolkit_version_with_length(info, pointer, (nuint)version.Length);
            }
        }
    }
}
