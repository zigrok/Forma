---
title: Layout and sizing
description: Choose direct sizes, constraints, containers, and display scaling in Forma.
---

# Layout and sizing

Forma measures retained controls, applies constraints, and then lets the parent allocate the final
rectangle. Most application UI should express intent through minimums and container rules instead
of rewriting `Size` every frame.

```mermaid
flowchart LR
    A[Desired content size] --> B[CustomMinimumSize and MinWidth/MinHeight]
    B --> C[Width/Height and AspectRatio]
    C --> D[MaxWidth/MaxHeight]
    D --> E[Parent container allocation]
    E --> F[Position and Size]
```

## Choose the sizing control

| Need | Use | Notes |
| --- | --- | --- |
| Place a root or canvas child at an exact runtime rectangle | `Position` and `Size` | The host owns a root's available rectangle. Containers can replace a child's allocated size. |
| Keep a control usable while allowing growth | `CustomMinimumSize` | Combined with the control's content minimum; this is the normal application-level floor. |
| Set scalar layout constraints | `MinWidth`, `MinHeight`, `MaxWidth`, `MaxHeight` | Defaults are `0` and positive infinity. A minimum wins if it exceeds a maximum. |
| Request a direct dimension | `Width` or `Height` | Defaults are auto (`NaN`). One explicit axis can combine with `AspectRatio`. |
| Share surplus space in a container | `HorizontalSizeFlags`, `VerticalSizeFlags` | Defaults are `Fill`; add `Expand` to participate in surplus allocation. |
| Align without filling the allocated slot | `HorizontalAlignment`, `VerticalAlignment` | Both default to `Fill`. |

The QuickStart uses the recommended split: its host assigns the root `Size` from the viewport, while
the reusable controls declare `CustomMinimumSize` and size flags.

[!code-csharp[](examples/csharp-first-ui.cs)]

## Containers and spacing

Use `StackPanel` for a simple axis, `WrapPanel` for wrapping, `FlexPanel` for flex-style distribution,
`GridPanel` for explicit tracks, `OverlayPanel` for layers, and `CanvasPanel` for coordinate-based
placement. `StackPanel` defaults to vertical orientation, zero gap, and stretched cross-axis items.
Its `Gap` and the classic `BoxContainer.Separation` are sibling spacing, not outer margin.

`Control.Margin` reserves space outside a child. Padding is supplied by controls that own an inner
content box, such as `Border`; it is not a universal `Control` property. Content controls separately
default `HorizontalContentAlignment` and `VerticalContentAlignment` to `Fill`.

Explore the executable Catalog stories for
[StackPanel](https://github.com/zigrok/Forma/blob/main/samples/Forma.Catalog/Stories/Controls/StackPanel.xaml),
[GridPanel](https://github.com/zigrok/Forma/blob/main/samples/Forma.Catalog/Stories/Controls/GridPanel.xaml),
and [FlexPanel](https://github.com/zigrok/Forma/blob/main/samples/Forma.Catalog/Stories/Controls/FlexPanel.xaml).

## Viewport and display scale

`UIComponent` updates `UIContext.ViewportSize` from the graphics-device viewport. A root whose size
is still zero adopts that logical viewport. Set `UIContext.DisplayScale` to physical pixels per
logical UI coordinate; the default is `1`. Forma maps pointer input back to logical coordinates,
scales drawing, invalidates scale-sensitive layout, and refreshes device-scoped glyph resources.

## Finding clipped and overflowing text

Text that is clipped or pushed outside its box depends on the language, the UI scale and the window size together, so it is
caught by a test, not by eye.

- `Label.TextFit` says whether a label's text fits the box layout gave it: `Fits`, `Wrapped`, trimmed with an ellipsis, or `Clipped`, with the
  measured and available sizes and the overflow. Read it after layout.
- `UIContext.FindLayoutProblems(root)` walks a laid-out view and returns each `LayoutProblem`: text clipped or wrapped past its
  box, a control past its parent, past an ancestor that clips it, or past the viewport. Each item carries the control, its name and
  classes, the overflow in pixels and a message. `LayoutProblemOptions` selects the kinds, the tolerance and whether scrolling
  containers are ignored.
- `PseudoLocalizer` makes every string longer, accented and wrapped in markers (and can mirror it for right to left) without a real
  translation, so a layout that only fits the source language fails in a test first. Placeholders such as `{0}` survive.
- `UiMatrix.Run` (in `Forma.Testing`) lays every view out for every locale, UI scale and viewport and runs a check, with
  `UiMatrix.LayoutProblems()` as the ready-made check. A failure names the view, locale, scale and viewport.
- `DynamicTextCoverage.FindMissingGlyphs` lists the characters of a set of strings that no face of a font stack covers.
- `forma-xaml validate --layout View.fhtml [--lang l] [--scale n] [--size WxH]` validates the markup and then runs the project's
  `layoutHost` (named in `forma-preview.json`), which lays the view out with the application's types and prints the findings. A
  game can expose the same findings from its MCP server as `ui_layout_problems`.

The loop for a layout change: edit, `forma-xaml validate`, run the layout check for the languages and scales you ship (and the
pseudo-locale), fix the layout, not the text.

## Common mistakes

- Do not use `Size` as a minimum inside a container; use `CustomMinimumSize` or scalar minimums.
- Do not expect `MaxWidth` to violate a larger `MinWidth`; the minimum remains authoritative.
- Do not combine margins and padded content as if they occupy the same side of the border.
- After changing anchors, verify `keepOffset`: `false` preserves the resolved position, while `true`
  preserves the raw offset and can visibly move the control.

For XAML value syntax and attached grid placement, see the [XAML language contract](xaml-language.md).
