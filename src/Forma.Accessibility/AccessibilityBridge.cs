// SPDX-License-Identifier: MIT
// Copyright (c) Igor Hipólito Vieira

using System;

namespace Forma.Accessibility
{
    /// <summary>
    /// Publishes a <see cref="UIContext"/>'s accessibility tree to the operating system, so a screen
    /// reader or an automation client sees a tree of controls instead of one opaque rectangle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Opt-in. An application creates one, attaches it to its window, and calls
    /// <see cref="Update"/> each frame. Everything is a no-op until an assistive technology
    /// actually activates the tree, so a running bridge costs one native call per frame.
    /// </para>
    /// <para>
    /// <see cref="Attach"/> answering false is a normal outcome, not a failure to handle: it means
    /// this platform has no host, there is no window (a headless runtime), or the native library is
    /// missing. The application carries on exactly as it did before.
    /// </para>
    /// <example>
    /// <code>
    /// _accessibility = new AccessibilityBridge(_ui) { WindowTitle = Window.Title };
    /// _accessibility.Attach(Window.PlatformHandle);
    /// // ... each frame, after layout:
    /// _accessibility.Update();
    /// </code>
    /// </example>
    /// </remarks>
    public sealed class AccessibilityBridge : IDisposable
    {
        private readonly UIContext _context;
        private readonly IAccessibilityHost _host;
        private bool _attached;
        private bool _disposed;

        /// <summary>Creates a bridge over a context, using the host for this platform.</summary>
        public AccessibilityBridge(UIContext context)
            : this(context, CreateDefaultHost())
        {
        }

        /// <summary>Creates a bridge over a context with a specific host. Mainly for tests.</summary>
        public AccessibilityBridge(UIContext context, IAccessibilityHost host)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        /// <summary>
        /// What the window is called. Announced as the root element's name, which is how an
        /// assistive technology tells one window from another.
        /// </summary>
        public string WindowTitle
        {
            get => _host is MacAccessibilityHost mac ? mac.WindowTitle : "Forma";
            set { if (_host is MacAccessibilityHost mac) mac.WindowTitle = value; }
        }

        /// <summary>Whether the tree is currently published.</summary>
        public bool IsAttached => _attached;

        /// <summary>
        /// Attaches to a native window.
        /// </summary>
        /// <param name="windowHandle">
        /// The operating system's window object, not the windowing backend's — <c>NSWindow*</c> on
        /// macOS. MonoGame exposes it as <c>GameWindow.PlatformHandle</c>;
        /// <c>GameWindow.Handle</c> is the wrong one, being the <c>SDL_Window*</c>.
        /// </param>
        /// <returns>Whether accessibility was started. False is a normal answer, not an error.</returns>
        public bool Attach(IntPtr windowHandle)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AccessibilityBridge));
            if (_attached) return true;

            _attached = _host.Attach(windowHandle, CaptureTree, PerformAction);
            return _attached;
        }

        /// <summary>
        /// Offers the current tree to the platform. Call after layout has settled for the frame —
        /// a tree captured mid-layout reports bounds that are about to change.
        /// </summary>
        public void Update()
        {
            if (!_attached || _disposed) return;
            _host.Update(CaptureTree());
        }

        /// <summary>Reports that the window gained or lost keyboard focus.</summary>
        public void SetWindowFocused(bool focused)
        {
            if (!_attached || _disposed) return;
            _host.SetWindowFocused(focused);
        }

        /// <summary>Detaches and releases the platform adapter.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _attached = false;
            _host.Dispose();
        }

        private AccessibilityTreeSnapshot CaptureTree() => AccessibilityTree.Capture(_context);

        /// <summary>
        /// Runs what the platform asked for through the control's own peer, which is the same path
        /// a locator takes. A bridge that reached past the peer could drive behaviour a person
        /// cannot — the opposite of what an accessibility tree is for.
        /// </summary>
        private void PerformAction(AccessibilityActionRequest request) =>
            AccessibilityTree.TryPerformAction(_context, request.NodeId, request.Action, request.Argument);

        private static IAccessibilityHost CreateDefaultHost()
        {
            if (OperatingSystem.IsMacOS()) return new MacAccessibilityHost();

            // Windows and Linux adapters exist in AccessKit but are not wired up here; see
            // docs/adr/0008-platform-accessibility-bridge.md. A null host keeps the shape of the
            // calling code identical everywhere rather than making applications branch on platform.
            return new NullAccessibilityHost();
        }
    }

    /// <summary>
    /// The host for platforms with no bridge. Answers "no" to everything and does nothing else.
    /// </summary>
    /// <remarks>
    /// Exists so applications never branch on platform. An application that has to write
    /// <c>if (OperatingSystem.IsMacOS())</c> around its accessibility calls will eventually get that
    /// condition wrong somewhere, and the failure will be silent.
    /// </remarks>
    public sealed class NullAccessibilityHost : IAccessibilityHost
    {
        /// <summary>Creates a host that does nothing.</summary>
        public NullAccessibilityHost()
        {
        }

        /// <inheritdoc />
        public bool IsSupported => false;

        /// <inheritdoc />
        public bool Attach(IntPtr windowHandle, Func<AccessibilityTreeSnapshot> requestTree, Action<AccessibilityActionRequest> performAction) => false;

        /// <inheritdoc />
        public void Update(AccessibilityTreeSnapshot snapshot) { }

        /// <inheritdoc />
        public void SetWindowFocused(bool focused) { }

        /// <summary>Nothing to release.</summary>
        public void Dispose() { }
    }
}
