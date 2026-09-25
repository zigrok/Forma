// SPDX-License-Identifier: MIT
// Copyright (c) Igor Hipólito Vieira

using System;
using System.Runtime.InteropServices;

namespace Forma.Accessibility
{
    /// <summary>
    /// Raw bindings to accesskit-c 0.23.0. Nothing above this type deals in pointers.
    /// </summary>
    /// <remarks>
    /// Only the subset the bridge uses is declared. AccessKit's C surface is 454 symbols; binding
    /// all of them would be a lot of untested code whose only reader is a compiler.
    /// </remarks>
    internal static unsafe class AccessKit
    {
        internal const string Library = "libaccesskit";

        // --- nodes ---------------------------------------------------------------------------

        [DllImport(Library, ExactSpelling = true)]
        internal static extern IntPtr accesskit_node_new(AccessKitRole role);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern void accesskit_node_free(IntPtr node);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern void accesskit_node_set_label_with_length(IntPtr node, byte* value, nuint length);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern void accesskit_node_set_value_with_length(IntPtr node, byte* value, nuint length);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern void accesskit_node_set_bounds(IntPtr node, AccessKitRect bounds);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern void accesskit_node_add_action(IntPtr node, AccessKitAction action);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern void accesskit_node_set_children(IntPtr node, nuint length, ulong* values);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern void accesskit_node_set_disabled(IntPtr node);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern void accesskit_node_set_hidden(IntPtr node);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern void accesskit_node_set_read_only(IntPtr node);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern void accesskit_node_set_selected(IntPtr node, [MarshalAs(UnmanagedType.U1)] bool value);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern void accesskit_node_set_toggled(IntPtr node, AccessKitToggled value);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern void accesskit_node_set_numeric_value(IntPtr node, double value);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern void accesskit_node_set_min_numeric_value(IntPtr node, double value);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern void accesskit_node_set_max_numeric_value(IntPtr node, double value);

        // --- trees ---------------------------------------------------------------------------

        [DllImport(Library, ExactSpelling = true)]
        internal static extern IntPtr accesskit_tree_info_new(ulong root);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern void accesskit_tree_info_set_toolkit_name_with_length(IntPtr tree, byte* value, nuint length);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern void accesskit_tree_info_set_toolkit_version_with_length(IntPtr tree, byte* value, nuint length);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern IntPtr accesskit_tree_update_with_capacity_and_focus(nuint capacity, ulong focus);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern void accesskit_tree_update_free(IntPtr update);

        /// <remarks>Takes ownership of <paramref name="node"/>; do not free it afterwards.</remarks>
        [DllImport(Library, ExactSpelling = true)]
        internal static extern void accesskit_tree_update_push_node(IntPtr update, ulong id, IntPtr node);

        /// <remarks>Takes ownership of <paramref name="tree"/>.</remarks>
        [DllImport(Library, ExactSpelling = true)]
        internal static extern void accesskit_tree_update_set_tree_info(IntPtr update, IntPtr tree);

        // --- action requests -----------------------------------------------------------------

        [DllImport(Library, ExactSpelling = true)]
        internal static extern void accesskit_action_request_free(AccessKitActionRequest* request);

        // --- macOS ---------------------------------------------------------------------------

        [DllImport(Library, ExactSpelling = true)]
        internal static extern void accesskit_macos_add_focus_forwarder_to_window_class_with_length(byte* className, nuint length);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern IntPtr accesskit_macos_subclassing_adapter_for_window(
            IntPtr window,
            IntPtr activationHandler,
            IntPtr activationHandlerUserdata,
            IntPtr actionHandler,
            IntPtr actionHandlerUserdata);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern void accesskit_macos_subclassing_adapter_free(IntPtr adapter);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern IntPtr accesskit_macos_subclassing_adapter_update_if_active(
            IntPtr adapter,
            IntPtr updateFactory,
            IntPtr updateFactoryUserdata);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern IntPtr accesskit_macos_subclassing_adapter_update_view_focus_state(
            IntPtr adapter,
            [MarshalAs(UnmanagedType.U1)] bool isFocused);

        [DllImport(Library, ExactSpelling = true)]
        internal static extern void accesskit_macos_queued_events_raise(IntPtr events);
    }

    /// <summary>A rectangle in AccessKit's coordinate space: top-left origin, physical pixels.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct AccessKitRect
    {
        public double X0;
        public double Y0;
        public double X1;
        public double Y1;
    }

    /// <summary>
    /// A 128-bit tree identifier. Declared as bytes because that is what it is on the wire, and
    /// because its alignment of 1 is what puts it at offset 1 of an action request.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 16)]
    internal unsafe struct AccessKitTreeId
    {
        public fixed byte Bytes[16];
    }

    /// <summary>
    /// What an assistive technology asked for.
    /// </summary>
    /// <remarks>
    /// Explicit offsets, measured against the shipped binary rather than inferred. The layout is
    /// natural C alignment, but it does not look like it: <c>accesskit_tree_id</c> is a byte array,
    /// so it has alignment 1 and lands at offset 1 immediately after the one-byte action, and the
    /// 8-aligned node id that follows is then pushed from 17 to 24. Both "pack everything" and
    /// "align everything" get this wrong, and the failure mode is a plausible-looking wrong node id
    /// rather than a crash.
    /// </remarks>
    [StructLayout(LayoutKind.Explicit, Size = 80)]
    internal struct AccessKitActionRequest
    {
        [FieldOffset(0)] public AccessKitAction Action;
        [FieldOffset(1)] public AccessKitTreeId TargetTree;
        [FieldOffset(24)] public ulong TargetNode;

        /// <summary>Whether <c>accesskit_opt_action_data</c> carries a value.</summary>
        [FieldOffset(32)] public byte HasData;

        /// <summary>Which arm of the data union is live. Only meaningful when <see cref="HasData"/>.</summary>
        [FieldOffset(40)] public AccessKitActionDataTag DataTag;

        /// <summary>The union's payload when the tag says it is a number.</summary>
        [FieldOffset(48)] public double NumericValue;

        /// <summary>
        /// The union's payload when the tag says it is a string: a NUL-terminated UTF-8 buffer
        /// owned by the request, valid only until the request is freed.
        /// </summary>
        [FieldOffset(48)] public IntPtr Value;
    }

    /// <summary>Which arm of an action request's data union is live.</summary>
    internal enum AccessKitActionDataTag
    {
        /// <summary>A custom action index.</summary>
        CustomAction = 0,

        /// <summary>A string, for a text control.</summary>
        Value = 1,

        /// <summary>A number, for a slider or spin button.</summary>
        NumericValue = 2,

        /// <summary>A scroll unit.</summary>
        ScrollUnit = 3,

        /// <summary>A scroll hint.</summary>
        ScrollHint = 4,

        /// <summary>A point to scroll to.</summary>
        ScrollToPoint = 5,

        /// <summary>A scroll offset to apply.</summary>
        SetScrollOffset = 6,

        /// <summary>A text selection to apply.</summary>
        SetTextSelection = 7,
    }
}
