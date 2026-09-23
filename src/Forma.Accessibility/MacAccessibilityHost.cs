// SPDX-License-Identifier: MIT
// Copyright (c) Igor Hipólito Vieira

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Forma.Accessibility
{
    /// <summary>
    /// Exposes a Forma window's tree through <c>NSAccessibility</c>, using AccessKit's subclassing
    /// adapter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Forma never touches Objective-C here. macOS accessibility works by calling
    /// <c>accessibilityChildren</c>, <c>accessibilityFocusedUIElement</c> and
    /// <c>accessibilityHitTest:</c> on the window's view — and the windowing backend owns that view,
    /// so implementing them directly is not an option. The subclassing adapter installs a runtime
    /// Objective-C subclass carrying those methods and restores the original class when freed.
    /// </para>
    /// <para>
    /// Everything here runs on the thread that owns the window. AccessKit's macOS adapter asserts
    /// the main thread internally and a violation is a Rust panic across the FFI boundary, which
    /// from managed code is a process abort rather than an exception — so the rule is enforced by
    /// not breaking it, not by catching it.
    /// </para>
    /// </remarks>
    public sealed class MacAccessibilityHost : IAccessibilityHost
    {
        // Rooted for the adapter's whole lifetime. These are passed to native code as function
        // pointers; if the collector takes them, the first interaction from an assistive technology
        // calls into freed memory and the process dies.
        private readonly ActivationHandler _activationHandler;
        private readonly ActionHandler _actionHandler;

        private IntPtr _adapter;
        private GCHandle _self;
        private Func<AccessibilityTreeSnapshot> _requestTree;
        private Action<AccessibilityActionRequest> _performAction;
        private AccessibilityTreeSnapshot _pendingSnapshot;
        private bool _disposed;

        /// <summary>The window title announced as the root element's name.</summary>
        public string WindowTitle { get; set; } = "Forma";

        /// <summary>Creates a host. Nothing is loaded or attached until <see cref="Attach"/>.</summary>
        public MacAccessibilityHost()
        {
            _activationHandler = OnActivation;
            _actionHandler = OnAction;
        }

        /// <inheritdoc />
        public bool IsSupported => OperatingSystem.IsMacOS();

        /// <inheritdoc />
        public bool Attach(
            IntPtr windowHandle,
            Func<AccessibilityTreeSnapshot> requestTree,
            Action<AccessibilityActionRequest> performAction)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MacAccessibilityHost));
            if (requestTree == null) throw new ArgumentNullException(nameof(requestTree));

            // Not an error. A headless runtime has no window and a non-macOS host has no adapter;
            // an application should be able to ask for accessibility unconditionally and carry on.
            if (!IsSupported || windowHandle == IntPtr.Zero) return false;

            _requestTree = requestTree;
            _performAction = performAction;

            try
            {
                // SDL puts keyboard focus on the window rather than on the content view, so without
                // this the tree reports nothing as focused. The class name is SDL3's: SDL2's was
                // "SDLWindow", and AccessKit's own SDL example still says the old one.
                AddFocusForwarder("SDL3Window");

                _self = GCHandle.Alloc(this);
                _adapter = AccessKit.accesskit_macos_subclassing_adapter_for_window(
                    windowHandle,
                    Marshal.GetFunctionPointerForDelegate(_activationHandler),
                    GCHandle.ToIntPtr(_self),
                    Marshal.GetFunctionPointerForDelegate(_actionHandler),
                    GCHandle.ToIntPtr(_self));
            }
            catch (DllNotFoundException)
            {
                // The native library is optional by design. Missing it means no accessibility, not
                // a broken application.
                Release();
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                // A libaccesskit from a different release. Same treatment: degrade, do not crash.
                Release();
                return false;
            }

            if (_adapter == IntPtr.Zero)
            {
                Release();
                return false;
            }

            return true;
        }

        /// <inheritdoc />
        public void Update(AccessibilityTreeSnapshot snapshot)
        {
            if (_adapter == IntPtr.Zero || snapshot == null) return;

            // Held only for the duration of the call below, which reads it back through the factory
            // callback. AccessKit does nothing until an assistive technology has activated the
            // tree, so on almost every frame this costs one native call and no tree building.
            _pendingSnapshot = snapshot;
            try
            {
                var events = AccessKit.accesskit_macos_subclassing_adapter_update_if_active(
                    _adapter,
                    Marshal.GetFunctionPointerForDelegate(_activationHandler),
                    GCHandle.ToIntPtr(_self));

                Raise(events);
            }
            finally
            {
                _pendingSnapshot = null;
            }
        }

        /// <inheritdoc />
        public void SetWindowFocused(bool focused)
        {
            if (_adapter == IntPtr.Zero) return;
            Raise(AccessKit.accesskit_macos_subclassing_adapter_update_view_focus_state(_adapter, focused));
        }

        /// <summary>Detaches the adapter, restoring the view's original class.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Release();
        }

        private void Release()
        {
            if (_adapter != IntPtr.Zero)
            {
                AccessKit.accesskit_macos_subclassing_adapter_free(_adapter);
                _adapter = IntPtr.Zero;
            }

            if (_self.IsAllocated) _self.Free();
            _requestTree = null;
            _performAction = null;
        }

        private static void Raise(IntPtr events)
        {
            if (events == IntPtr.Zero) return;

            // Raising may re-enter the view's accessibility methods, so nothing that could be
            // needed by them may be locked or borrowed across this call. Nothing here holds
            // anything; the warning is recorded because the next person to add a lock needs it.
            AccessKit.accesskit_macos_queued_events_raise(events);
        }

        private static unsafe void AddFocusForwarder(string className)
        {
            var bytes = Encoding.UTF8.GetBytes(className);
            fixed (byte* pointer = bytes)
            {
                AccessKit.accesskit_macos_add_focus_forwarder_to_window_class_with_length(pointer, (nuint)bytes.Length);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr ActivationHandler(IntPtr userdata);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ActionHandler(IntPtr request, IntPtr userdata);

        /// <summary>
        /// Builds the tree AccessKit asked for. Serves both the initial activation and every
        /// subsequent update, which is why the same delegate is passed to both.
        /// </summary>
        /// <remarks>
        /// Never returns null and never lets an exception escape. This is called from Rust, where a
        /// managed exception is undefined behaviour and a null tree update violates the documented
        /// contract of the update factory — so a failure becomes an empty tree, which announces an
        /// empty window rather than taking the process down.
        /// </remarks>
        private static IntPtr OnActivation(IntPtr userdata)
        {
            MacAccessibilityHost host = null;
            try
            {
                host = Resolve(userdata);
                var snapshot = host?._pendingSnapshot ?? host?._requestTree?.Invoke();
                if (snapshot != null) return AccessKitTreeBuilder.Build(snapshot, host.WindowTitle);
            }
            catch
            {
                // Swallowed deliberately: there is nowhere to report it that would not itself cross
                // back into native code.
            }

            return AccessKitTreeBuilder.BuildEmpty(host?.WindowTitle ?? "Forma");
        }

        private static unsafe void OnAction(IntPtr request, IntPtr userdata)
        {
            var typed = (AccessKitActionRequest*)request;

            try
            {
                var host = Resolve(userdata);
                if (host?._performAction == null || typed == null) return;

                var action = AccessKitTreeBuilder.MapAction(typed->Action);
                if (action == AccessibilityActions.None) return;

                host._performAction(new AccessibilityActionRequest(
                    (int)typed->TargetNode, action, ReadArgument(typed)));
            }
            catch
            {
                // Same reason as above. A handler that throws must not become a process abort.
            }
            finally
            {
                // Ownership of the request transfers to this callback whatever happens to it.
                if (typed != null) AccessKit.accesskit_action_request_free(typed);
            }
        }

        /// <summary>
        /// The value an action was given, or null. A set-value on a slider arrives as a number and
        /// on a field as a string, and passing the wrong one through means the control silently
        /// refuses an action the platform believes succeeded.
        /// </summary>
        private static unsafe object ReadArgument(AccessKitActionRequest* request)
        {
            if (request->HasData == 0) return null;

            switch (request->DataTag)
            {
                case AccessKitActionDataTag.NumericValue:
                    return request->NumericValue;
                case AccessKitActionDataTag.Value:
                    // Owned by the request and freed with it, so it is copied here rather than
                    // handed onwards as a pointer.
                    return request->Value == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(request->Value);
                default:
                    return null;
            }
        }

        private static MacAccessibilityHost Resolve(IntPtr userdata)
        {
            if (userdata == IntPtr.Zero) return null;
            var handle = GCHandle.FromIntPtr(userdata);
            return handle.IsAllocated ? handle.Target as MacAccessibilityHost : null;
        }
    }
}
