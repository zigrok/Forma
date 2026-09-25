#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
# Copyright (c) Igor Hipólito Vieira
"""Reads a running application's accessibility tree from outside the process.

This is the external half of Forma's accessibility story. Forma's own tests capture the tree
in-process, which proves the tree is built but not that anyone else can see it — and "anyone else"
is the entire point: a screen reader, an automation client, a computer-use agent. This walks the
real macOS accessibility API against a live process, so a pass means the operating system
genuinely has the tree.

  python3 a11y_probe.py dump  --pid 1234
  python3 a11y_probe.py find  --pid 1234 --name "Save"
  python3 a11y_probe.py press --pid 1234 --name "Save"
  python3 a11y_probe.py value --pid 1234 --name "head_angle_y"
  python3 a11y_probe.py set   --pid 1234 --name "head_angle_y" --value 12

Requires macOS, pyobjc, and Accessibility permission for whatever runs this
(System Settings -> Privacy & Security -> Accessibility). Without the permission every query
returns an API-disabled error rather than an empty tree, which the script reports as such.
"""

from __future__ import annotations

import argparse
import json
import sys

try:
    from ApplicationServices import (
        AXIsProcessTrusted,
        AXUIElementCopyAttributeValue,
        AXUIElementCreateApplication,
        AXUIElementPerformAction,
        AXUIElementSetAttributeValue,
        kAXErrorSuccess,
    )
except ImportError:  # pragma: no cover - environment problem, not a code path
    sys.exit(
        "pyobjc is required: pip install pyobjc-framework-ApplicationServices pyobjc-framework-Quartz"
    )

# Attribute and action names are plain strings in the AX API; pyobjc does not re-export all of them.
CHILDREN = "AXChildren"
ROLE = "AXRole"
TITLE = "AXTitle"
DESCRIPTION = "AXDescription"
VALUE = "AXValue"
ENABLED = "AXEnabled"
POSITION = "AXPosition"
SIZE = "AXSize"
PRESS = "AXPress"
INCREMENT = "AXIncrement"
DECREMENT = "AXDecrement"


def attribute(element, name):
    """One attribute, or None when the element does not have it."""
    error, value = AXUIElementCopyAttributeValue(element, name, None)
    return value if error == kAXErrorSuccess else None


def describe(element) -> dict:
    """An element as plain data: role, name, value, and whether it is enabled."""
    return {
        "role": attribute(element, ROLE),
        # AXTitle is what most controls put their label in; AXDescription is the fallback several
        # roles use instead, so a probe that reads only one of them misses half the tree.
        "name": attribute(element, TITLE) or attribute(element, DESCRIPTION) or "",
        "value": _plain(attribute(element, VALUE)),
        "enabled": bool(attribute(element, ENABLED)),
    }


def _plain(value):
    """AX values are Objective-C objects; reduce to something JSON can hold."""
    if value is None:
        return None
    if isinstance(value, (str, int, float, bool)):
        return value
    return str(value)


def walk(element, depth=0, limit=40):
    """Depth-first over the tree, yielding (depth, element)."""
    if depth > limit:
        return
    yield depth, element
    for child in attribute(element, CHILDREN) or []:
        yield from walk(child, depth + 1, limit)


def application(pid: int):
    if not AXIsProcessTrusted():
        sys.exit(
            "This process does not have Accessibility permission. Grant it under\n"
            "System Settings -> Privacy & Security -> Accessibility for the terminal or\n"
            "interpreter running this script, then try again."
        )
    return AXUIElementCreateApplication(pid)


def find(root, name: str, role: str | None = None):
    """The first element whose name matches, optionally constrained by role."""
    for _, element in walk(root):
        described = describe(element)
        if described["name"] != name:
            continue
        if role and described["role"] != role:
            continue
        return element, described
    return None, None


def command_dump(args) -> int:
    root = application(args.pid)
    entries = []
    for depth, element in walk(root):
        described = describe(element)
        described["depth"] = depth
        entries.append(described)

    if args.json:
        print(json.dumps(entries, indent=2))
    else:
        for entry in entries:
            label = f' "{entry["name"]}"' if entry["name"] else ""
            value = f" = {entry['value']!r}" if entry["value"] not in (None, "") else ""
            print(f"{'  ' * entry['depth']}{entry['role']}{label}{value}")

    print(f"\n{len(entries)} elements", file=sys.stderr)
    # An application with no accessibility support still reports a handful of window-level
    # elements, so "did we get a tree" is a question about depth, not about emptiness.
    return 0 if len(entries) > 1 else 1


def command_find(args) -> int:
    root = application(args.pid)
    element, described = find(root, args.name, args.role)
    if element is None:
        print(f"not found: {args.name!r}", file=sys.stderr)
        return 1
    print(json.dumps(described, indent=2))
    return 0


def command_press(args) -> int:
    root = application(args.pid)
    element, described = find(root, args.name, args.role)
    if element is None:
        print(f"not found: {args.name!r}", file=sys.stderr)
        return 1

    error = AXUIElementPerformAction(element, PRESS)
    if error != kAXErrorSuccess:
        print(f"AXPress failed with {error} on {described}", file=sys.stderr)
        return 1
    print(f"pressed {described['role']} {described['name']!r}")
    return 0


def command_value(args) -> int:
    root = application(args.pid)
    element, described = find(root, args.name, args.role)
    if element is None:
        print(f"not found: {args.name!r}", file=sys.stderr)
        return 1
    print(json.dumps(described["value"]))
    return 0


def command_set(args) -> int:
    root = application(args.pid)
    element, described = find(root, args.name, args.role)
    if element is None:
        print(f"not found: {args.name!r}", file=sys.stderr)
        return 1

    error = AXUIElementSetAttributeValue(element, VALUE, args.value)
    if error != kAXErrorSuccess:
        print(f"AXSetValue failed with {error} on {described}", file=sys.stderr)
        return 1
    print(json.dumps(describe(element), indent=2))
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--pid", type=int, required=True, help="process id of the application")
    parser.add_argument("--role", help="constrain matches to this AX role")
    sub = parser.add_subparsers(dest="command", required=True)

    dump = sub.add_parser("dump", help="print the whole tree")
    dump.add_argument("--json", action="store_true")
    dump.set_defaults(func=command_dump)

    found = sub.add_parser("find", help="find one element by name")
    found.add_argument("--name", required=True)
    found.set_defaults(func=command_find)

    press = sub.add_parser("press", help="AXPress the element with this name")
    press.add_argument("--name", required=True)
    press.set_defaults(func=command_press)

    value = sub.add_parser("value", help="read the value of the element with this name")
    value.add_argument("--name", required=True)
    value.set_defaults(func=command_value)

    setter = sub.add_parser("set", help="set the value of the element with this name")
    setter.add_argument("--name", required=True)
    setter.add_argument("--value", required=True)
    setter.set_defaults(func=command_set)

    args = parser.parse_args()
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
