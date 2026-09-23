# ADR 0008: Platform Accessibility Bridge

- Status: Accepted for implementation, macOS only
- Date: 2026-09-22
- Owners: Forma maintainers
- Related plan: `.github/prompts/plan-agent-drivable-forma-accessibility.md` (Paramia), Track B

## Context

Forma draws its own controls. To every operating system a Forma window is one opaque rectangle: a
screen reader announces nothing, and an assistive technology or computer-use agent sees a picture
rather than a tree. `AccessibilityTree.Capture` already produces a complete in-process description
of that tree — roles, names, values, states, actions, bounds — and `Forma.Testing` drives it. What
does not exist is a way for anything *outside* the process to see it.

The first question is not "which library" but whether it is reachable at all. SDL owns the
`NSWindow` and the `NSView`; Forma never sees either, and macOS accessibility works by the system
calling `accessibilityChildren`, `accessibilityFocusedUIElement` and `accessibilityHitTest:` on the
view. If answering those required owning an `NSView` subclass, this track would end here, because
subclassing SDL's view from C# is not something to attempt.

## Decision

### Use AccessKit through its C bindings

[AccessKit](https://accesskit.dev) is a cross-platform accessibility abstraction that implements
`NSAccessibility` on macOS, UIAutomation on Windows and AT-SPI on Linux from a single tree model.
Its model — a tree of nodes with roles, properties and supported actions, updated incrementally — is
close enough to `AccessibilityTreeSnapshot` that the translation is a mapping rather than a design.

- **Bindings:** `AccessKit/accesskit-c` (a separate repository from `AccessKit/accesskit`; the C
  bindings are no longer in the main repo). Version 0.23.0.
- **Distribution:** consume the release zip, which ships `include/accesskit.h` plus prebuilt
  `libaccesskit.dylib`/`.a` for macOS arm64 and x86_64, Windows, Linux, Android and iOS. Building
  from source needs Rust plus CMake, and regenerating the header additionally needs a pinned nightly
  and cbindgen — so vendoring the prebuilt binary is both simpler and more reproducible than
  `native/dynamictext`'s build-from-source story. Two caveats recorded in Track B: the macOS
  binaries are **thin, not universal** (arm64 and x86_64 must be `lipo`'d or selected at runtime),
  and they are **adhoc-signed**, so a notarized app has to re-sign them.
- **Licensing:** MIT OR Apache-2.0, matching Forma's MIT posture. Portions derive from Chromium
  under its BSD-style license (`LICENSE.chromium`, redistributed). The Meson build files are LGPL
  but are build tooling, not shipped code.

### Attach with the subclassing adapter, through the window

`accesskit_macos_subclassing_adapter_for_window(NSWindow*, ...)` installs a runtime Objective-C
subclass on the window's content view, adds the three accessibility methods, and restores the
original class when freed. That is what makes this viable: **Forma never touches Objective-C, never
subclasses anything, and never reaches into SDL.** It hands over a pointer and gets a working tree.

The pointer comes from `GameWindow.PlatformHandle`, added to the zigrok MonoGame fork for this —
`Handle` is the `SDL_Window*`, which is the wrong thing to give a Cocoa API. It reads
`SDL_PROP_WINDOW_COCOA_WINDOW_POINTER` from SDL3's window properties and is null under the headless
runtime, where there is no window to attach to.

SDL puts keyboard focus on the window rather than the content view, so
`accesskit_macos_add_focus_forwarder_to_window_class` must also be called. The class name is
**`SDL3Window`** — SDL2's was `SDLWindow`, and AccessKit's own SDL example still says the old name.
Passing the wrong one breaks focus reporting silently.

### Split the bridge at a platform seam

Two halves, per the plan's D6:

- **Neutral half** — snapshot-to-`TreeUpdate` translation, role mapping, action dispatch, update
  scheduling. No platform types.
- **Platform half** — `IAccessibilityHost`: obtain the window handle, attach and detach the adapter,
  push an update. `MacAccessibilityHost` is the only implementation in this pass.

The seam has to accommodate two *shapes*, not just two platforms. On macOS the adapter is attached
to a view and the platform calls into it. On Windows the adapter must answer `WM_GETOBJECT` from the
window procedure — a callback, and SDL owns the window procedure. An interface modelled only on
attachment would need rewriting at the first Windows attempt.

### Opt-in and failure-tolerant

Off unless asked for. A native library that is missing, incompatible, or fails to load degrades to
today's behavior and never takes the application down. This is not defensive habit: AccessKit
surfaces misuse — the wrong thread, a window with no content view, a view already subclassed — as
**Rust panics across the FFI boundary**, which from C# aborts the process rather than raising an
exception it could catch. The wrapper keeps those cases from arising instead of trying to recover
from them.

## Consequences

- Forma gains an optional native dependency on a Rust-built library. It is not in the default build
  and nothing in `Forma` references it.
- Every adapter call happens on the main thread, which is where the game loop already is. Action
  callbacks arrive on the main thread too, so there is no marshalling — unlike the Linux adapter,
  whose handlers are called from another thread.
- Delegates handed to the adapter live as long as it does and must be rooted on the managed side, or
  the first assistive-technology interaction collects them and crashes the process.
- Nodes are replaced wholesale by an update, never patched, and every changed node's parent must be
  re-sent with it. The neutral half owns that bookkeeping so callers never see it.
- Windows and Linux are deliberately out of scope here. Linux is additionally weaker than
  "unfinished": AccessKit documents that its AT-SPI adapter does not yet fully support text edit
  controls and is not yet usable with Orca, so Linux is automation support first and assistive
  technology later.
- The browser target is unaffected and cannot be served this way at all; it would need the tree
  mirrored into a hidden DOM overlay, which is a different project.

## Alternatives considered

- **Implement `NSAccessibility` directly from C#.** Rejected. It means owning an `NSView` subclass —
  the thing SDL prevents — and then reimplementing per-platform for Windows and Linux.
- **Build `accesskit-c` from source, mirroring `native/dynamictext`.** Rejected for now: it adds
  Rust and CMake to the build for a library that already publishes binaries for every platform
  Forma targets. Revisit if a fix is needed before an upstream release.
- **Use the plain `accesskit_macos_adapter`.** Rejected: it requires the host to implement the three
  `NSView` accessibility methods, which is exactly what is not available here.
