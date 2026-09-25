#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
# Copyright (c) Igor Hipólito Vieira
"""Photograph Xcode's Accessibility Inspector pointed at a running application.

This is the "zero code" half of the accessibility demo: the artifact a reviewer can look at and
immediately see the difference between an application the operating system treats as one opaque
rectangle and one whose controls it can name. `a11y_probe.py` makes the same claim in text and is
the one to script against; this is the one to put in a pull request.

  python3 capture_inspector.py --pid 1234 --out before.png
  python3 capture_inspector.py --pid 1234 --out after.png --element "Search"

With `--element`, the Inspector's point-to-inspect mode is aimed at the named control and the
screenshot shows that control's attributes. Without it, the screenshot shows the application node
and its hierarchy — which is the telling one when there is nothing inside the window.

Needs Accessibility **and** Screen Recording permission for whatever runs it, and Xcode installed.
"""

from __future__ import annotations

import argparse
import subprocess
import sys
import time

sys.path.insert(0, __file__.rsplit("/", 1)[0])

from a11y_probe import POSITION, PRESS, SIZE, application, attribute, describe, walk  # noqa: E402

try:
    from ApplicationServices import (
        AXUIElementPerformAction,
        AXValueGetValue,
        kAXErrorSuccess,
        kAXValueCGPointType,
        kAXValueCGSizeType,
    )
    from Quartz import (
        CGEventCreateMouseEvent,
        CGEventPost,
        CGWindowListCopyWindowInfo,
        kCGEventLeftMouseDown,
        kCGEventLeftMouseUp,
        kCGEventMouseMoved,
        kCGHIDEventTap,
        kCGMouseButtonLeft,
        kCGNullWindowID,
        kCGWindowBounds,
        kCGWindowListOptionOnScreenOnly,
        kCGWindowNumber,
        kCGWindowOwnerPID,
    )
except ImportError:  # pragma: no cover - environment problem, not a code path
    sys.exit("pyobjc is required: pip install pyobjc-framework-ApplicationServices pyobjc-framework-Quartz")

INSPECTOR_PROCESS = "Accessibility Inspector.app/Contents/MacOS"


def relaunch_inspector() -> int:
    """Start a fresh Inspector and return its pid.

    Relaunched rather than reused: the Inspector holds an AXUIElement from the moment you target an
    application, so one opened before the application published its tree keeps showing the
    application as it was — which is indistinguishable from a bridge that does not work.
    """
    subprocess.run(["pkill", "-f", INSPECTOR_PROCESS], check=False)
    time.sleep(2)
    subprocess.run(["open", "-a", "Accessibility Inspector"], check=True)

    for _ in range(20):
        found = subprocess.run(["pgrep", "-f", INSPECTOR_PROCESS], capture_output=True, text=True)
        if found.stdout.strip():
            time.sleep(3)
            return int(found.stdout.split()[0])
        time.sleep(1)

    sys.exit("Accessibility Inspector did not start. Is Xcode installed?")


def find(pid: int, predicate):
    for _, element in walk(application(pid)):
        if predicate(describe(element)):
            return element
    return None


def wait_for(pid: int, predicate, seconds: int = 15):
    """The Inspector repopulates asynchronously; a lookup that runs too early finds nothing."""
    for _ in range(seconds):
        found = find(pid, predicate)
        if found is not None:
            return found
        time.sleep(1)
    return None


def press(element) -> bool:
    return AXUIElementPerformAction(element, PRESS) == kAXErrorSuccess


def centre(element):
    def unwrap(attribute_name, kind):
        raw = attribute(element, attribute_name)
        if raw is None:
            return None
        ok, value = AXValueGetValue(raw, kind, None)
        return value if ok else None

    position, size = unwrap(POSITION, kAXValueCGPointType), unwrap(SIZE, kAXValueCGSizeType)
    if position is None or size is None:
        return None
    return position.x + size.width / 2, position.y + size.height / 2


def click(point) -> None:
    for kind in (kCGEventMouseMoved, kCGEventLeftMouseDown, kCGEventLeftMouseUp):
        CGEventPost(kCGHIDEventTap, CGEventCreateMouseEvent(None, kind, point, kCGMouseButtonLeft))
        time.sleep(0.1)


def has_element(inspector: int) -> bool:
    """Whether the Inspector is showing an element rather than its empty prompt."""
    return find(inspector, lambda d: d["name"] == "Element") is not None


def target(inspector: int, pid: int, attempts: int = 3) -> None:
    """Point the Inspector at a process, retrying the pick until it shows an element.

    Picking once is not reliable: a freshly launched Inspector often takes the target but leaves
    "Select an element to inspect" showing, and a screenshot taken then is an empty panel that
    looks like a failed bridge. Picking again resolves it.
    """
    for attempt in range(attempts):
        picker = wait_for(inspector, lambda d: d["role"] == "AXList" and d["name"] == "Device Target")
        if picker is None:
            sys.exit("the Inspector has no Device Target control")

        click(centre(picker))
        time.sleep(2)

        entry = wait_for(inspector, lambda d: d["role"] == "AXMenuItem" and f"({pid})" in (d["name"] or ""))
        if entry is None:
            sys.exit(f"process {pid} was not offered by the Inspector's target picker")

        press(entry)
        time.sleep(3)

        if has_element(inspector):
            return

    print("warning: the Inspector never showed an element for this target", file=sys.stderr)


def inspect_window(inspector: int) -> None:
    """Drill into the target's window.

    This is the comparison that matters. At the application level a Forma app looks the same either
    way — one window and a menu bar — because the difference is *inside* the window: with no bridge
    its Children are None, and with one they are the control tree.
    """
    # The Hierarchy rows are static text with a separate disclosure arrow each, so the row cannot
    # be pressed by name. The arrows are in row order, and row 0 is the application itself.
    arrows = [
        element
        for _, element in walk(application(inspector))
        if describe(element)["name"] == "arrow.forward.circle.fill" and describe(element)["role"] == "AXButton"
    ]
    if len(arrows) < 2:
        sys.exit("the Inspector is not showing a hierarchy to drill into")

    press(arrows[1])
    time.sleep(2)


def inspect_element(inspector: int, pid: int, name: str) -> None:
    """Freeze the Inspector on one named control of the target application."""
    scope = find(inspector, lambda d: d["role"] == "AXCheckBox" and d["name"] == "scope")
    if scope is None:
        sys.exit("the Inspector has no point-to-inspect control")

    # The target has to be in front before it can be pointed at, or the click lands on whatever is
    # covering it -- usually the Inspector itself after a previous capture. Raised by clicking its
    # own window, which works regardless of how the process is registered with the window server.
    window = find(pid, lambda d: d["role"] == "AXWindow")
    if window is not None:
        click(centre(window))
        time.sleep(1.5)

    # Armed through the accessibility API rather than by clicking it: point-to-inspect freezes on
    # whatever is under the pointer when the click lands, so clicking the button itself inspects
    # the button.
    press(scope)
    time.sleep(1.5)

    control = find(pid, lambda d: d["name"] == name)
    if control is None:
        sys.exit(f"no element named {name!r} in process {pid}")

    point = centre(control)
    if point is None:
        sys.exit(f"{name!r} has no frame to point at")

    click(point)
    time.sleep(2)


def capture(pid: int, out: str) -> int:
    subprocess.run(["osascript", "-e", 'tell application "Accessibility Inspector" to activate'], check=False)
    time.sleep(2)

    best = None
    for window in CGWindowListCopyWindowInfo(kCGWindowListOptionOnScreenOnly, kCGNullWindowID):
        if window.get(kCGWindowOwnerPID) != pid:
            continue
        bounds = window.get(kCGWindowBounds, {})
        width, height = bounds.get("Width", 0), bounds.get("Height", 0)
        # Skip shadows, tooltips and helper windows. Layer is not a useful filter here: the
        # Inspector's own window is a floating panel rather than a document window.
        if width < 200 or height < 200:
            continue
        if best is None or width * height > best[1]:
            best = (window[kCGWindowNumber], width * height)

    if best is None:
        sys.exit(f"no on-screen window for pid {pid}")

    subprocess.run(["screencapture", "-x", "-o", "-l", str(best[0]), out], check=True)
    return best[0]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--pid", type=int, required=True, help="process to inspect")
    parser.add_argument("--out", required=True, help="where to write the PNG")
    parser.add_argument("--element", help="name of a control to point at; omit to show the app node")
    parser.add_argument("--window", action="store_true", help="drill into the target's window")
    args = parser.parse_args()

    inspector = relaunch_inspector()
    target(inspector, args.pid)
    if args.window:
        inspect_window(inspector)
    if args.element:
        inspect_element(inspector, args.pid, args.element)

    print(f"captured window {capture(inspector, args.out)} -> {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
