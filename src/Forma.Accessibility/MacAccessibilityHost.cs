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
                // Checked before anything is attached, because the adapter panics rather than
                // fails if the window has no content view -- and a Rust panic across the FFI
                // boundary aborts the process.
                if (!HasContentView(windowHandle))
                {
                    Release();
                    return false;
                }

                AddFocusForwarder();

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

        /// <summary>
        /// Teaches the windowing library's <c>NSWindow</c> subclass to forward the focused element
        /// to its content view. SDL puts keyboard focus on the window rather than the view, so
        /// without this nothing is ever reported as focused.
        /// </summary>
        /// <remarks>
        /// The class is looked up first rather than named and hoped for. AccessKit unwraps the
        /// lookup, so passing a class that does not exist is a Rust panic and therefore a process
        /// abort — and the name genuinely differs by backend: SDL2 (MonoGame's DesktopGL, FNA) uses
        /// <c>SDLWindow</c> while SDL3 uses <c>SDL3Window</c>. A window class that matches neither
        /// is not a failure: focus reporting is the only thing lost, and the tree still works.
        /// </remarks>
        private static void AddFocusForwarder()
        {
            foreach (var className in WindowClassNames)
            {
                if (objc_getClass(className) == IntPtr.Zero) continue;
                AddFocusForwarderTo(className);
                return;
            }
        }

        /// <summary>NSWindow subclasses known to place focus on the window rather than the view.</summary>
        private static readonly string[] WindowClassNames = { "SDL3Window", "SDLWindow" };

        private static unsafe void AddFocusForwarderTo(string className)
        {
            var bytes = Encoding.UTF8.GetBytes(className);
            fixed (byte* pointer = bytes)
            {
                AccessKit.accesskit_macos_add_focus_forwarder_to_window_class_with_length(pointer, (nuint)bytes.Length);
            }
        }

        /// <summary>
        /// Whether the window has a content view to attach to. The adapter subclasses that view, and
        /// documents that it panics when there is not one.
        /// </summary>
        private static bool HasContentView(IntPtr window)
        {
            try
            {
                var selector = sel_registerName("contentView");
                return selector != IntPtr.Zero && objc_msgSend_IntPtr(window, selector) != IntPtr.Zero;
            }
            catch (EntryPointNotFoundException)
            {
                // No Objective-C runtime to ask. Not macOS, so there is nothing to attach to.
                return false;
            }
        }

        [DllImport("/usr/lib/libobjc.dylib", CharSet = CharSet.Ansi)]
        private static extern IntPtr objc_getClass(string name);

        [DllImport("/usr/lib/libobjc.dylib", CharSet = CharSet.Ansi)]
        private static extern IntPtr sel_registerName(string name);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

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
