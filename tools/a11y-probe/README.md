# a11y-probe

Reads a running application's accessibility tree from **outside** the process, through the real
macOS accessibility API.

Forma's own tests capture the tree in-process. That proves the tree is built; it does not prove
anyone else can see it — and "anyone else" is the entire point of the platform bridge. This is the
check that closes that gap.

```sh
make a11y-probe PID=1234                       # dump the tree
make a11y-probe PID=1234 ARGS='press --name Save'
make a11y-probe PID=1234 ARGS='set --name Volume --value 12'
```

Or directly:

```sh
python3 -m venv .venv && .venv/bin/pip install \
  pyobjc-framework-ApplicationServices pyobjc-framework-Quartz
.venv/bin/python a11y_probe.py --pid 1234 dump
```

## Prerequisite

The **driving** process needs Accessibility permission — System Settings → Privacy & Security →
Accessibility — for the terminal or interpreter running the script, not for the application being
inspected. Without it every query fails with an API-disabled error; the script reports that rather
than printing an empty tree, because an empty tree and a missing permission look identical
otherwise.

## What a good result looks like

An application with no accessibility support still reports a handful of window-level elements — the
title bar and its buttons. A working bridge shows the application's own controls with their names:

```text
AXApplication "dotnet"
  AXWindow "Paramia Editor"
    AXGroup
      AXMenuBar
        AXButton "File"
        AXButton "Edit"
      AXGroup
        AXCheckBox "Parts" = True
        AXButton "Bones"
        AXUnknown
          AXButton "Body"
          AXButton "Head"
```
