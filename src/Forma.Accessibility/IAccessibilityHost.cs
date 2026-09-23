// SPDX-License-Identifier: MIT
// Copyright (c) Igor Hipólito Vieira

using System;

namespace Forma.Accessibility
{
    /// <summary>
    /// The platform half of the bridge: everything that knows what operating system this is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The interface has to fit two shapes, not just two platforms, or it needs rewriting at the
    /// first Windows attempt. On macOS the adapter is <em>attached</em> to the window's view and
    /// the system then calls into it. On Windows the adapter must <em>answer</em> a
    /// <c>WM_GETOBJECT</c> message from the window procedure — and the windowing backend owns that
    /// procedure. <see cref="Attach"/> covers both: macOS installs a subclass there, Windows would
    /// install a window-procedure hook there.
    /// </para>
    /// <para>
    /// A host must never throw for "this platform cannot do it" — it returns false from
    /// <see cref="Attach"/>. Accessibility being unavailable is a normal state, not an error, and
    /// an application should not have to guard against it.
    /// </para>
    /// </remarks>
    public interface IAccessibilityHost : IDisposable
    {
        /// <summary>Whether this host can run here at all, checked before anything is loaded.</summary>
        bool IsSupported { get; }

        /// <summary>
        /// Attaches to a native window and takes ownership of answering the platform's accessibility
        /// queries.
        /// </summary>
        /// <param name="windowHandle">
        /// The operating system's window object — <c>NSWindow*</c>, <c>HWND</c> — not the windowing
        /// backend's. <see cref="IntPtr.Zero"/> means there is no window, which is the normal answer
        /// under a headless runtime.
        /// </param>
        /// <param name="requestTree">
        /// Called when the platform first activates accessibility, and thereafter whenever an update
        /// is pushed. Returns the current tree. It is called on the thread that owns the window.
        /// </param>
        /// <param name="performAction">
        /// Called when an assistive technology requests an action. Also on the window's thread.
        /// </param>
        /// <returns>False when accessibility could not be started, for any reason.</returns>
        bool Attach(IntPtr windowHandle, Func<AccessibilityTreeSnapshot> requestTree, Action<AccessibilityActionRequest> performAction);

        /// <summary>
        /// Offers the current tree to the platform. Cheap and safe to call every frame: nothing
        /// happens until an assistive technology has actually activated the tree.
        /// </summary>
        void Update(AccessibilityTreeSnapshot snapshot);

        /// <summary>Reports that the window gained or lost keyboard focus.</summary>
        void SetWindowFocused(bool focused);
    }

    /// <summary>What an assistive technology asked the application to do, in Forma's own terms.</summary>
    public readonly struct AccessibilityActionRequest
    {
        /// <summary>Creates a request.</summary>
        public AccessibilityActionRequest(int nodeId, AccessibilityActions action, object argument = null)
        {
            NodeId = nodeId;
            Action = action;
            Argument = argument;
        }

        /// <summary>The <see cref="AccessibilityNode.Id"/> the request targets.</summary>
        public int NodeId { get; }

        /// <summary>
        /// What was asked for. <see cref="AccessibilityActions.None"/> when the platform requested
        /// something Forma has no equivalent for, which a handler should ignore rather than guess at.
        /// </summary>
        public AccessibilityActions Action { get; }

        /// <summary>
        /// What the action was given, where it takes one: the new text or number for
        /// <see cref="AccessibilityActions.SetValue"/>. Null otherwise.
        /// </summary>
        public object Argument { get; }
    }
}
