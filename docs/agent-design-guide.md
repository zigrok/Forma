# Designing screens with an agent

Read this before designing or restyling a screen. It is written for a coding agent working in an
application built on Forma, and it uses Trace Arena as the worked example because every claim below is
backed by a compiled view, a test or a gallery frame there.

The loop is: **change → render → read the verdict**. Do not judge a UI from XAML alone.

## Design language

Define every design value once, as a resource, and refer to it by name.

| Concern | Rule | Trace Arena source |
| --- | --- | --- |
| Color | Surfaces, borders, text and accents are named tokens (`Trace.Color.*`). Views use classes (`trace-surface`, `trace-text-muted`), never hex. | `UI/Resources/TraceRootView.xaml` |
| Spacing | One scale: 0, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 14, 16, 18, 20. A new value needs a new token. | `Trace.Space.*`, `TokenLintTests` |
| Radius, border, elevation | `Trace.Radius`, `Trace.Border`, `Trace.Elevation` tokens. | `TraceRootView.xaml` |
| Typography | One body size; emphasis is weight (Regular → Bold on hover or selection), not size. Titles use the title tokens. | `TraceStyle.cs` |
| Motion | Durations and easing are tokens; honor reduced motion. | `Trace.Motion.*` |
| Buttons | One shared template with a border. Hover, keyboard-selected and gamepad-selected share one highlight so a pointer user and a controller user see the same thing. | `TraceButtonTemplate` |
| Panel vs dialog | A panel sits on `trace-surface-menu` or `-card`; a dialog is `trace-surface-dialog` over `trace-surface-scrim` and owns focus until dismissed. | `OverlaySurfaceView`, the map editor unsaved-changes dialog |
| Selection and hover | Selected rows use the highlight surface plus bold weight. Hover and keyboard selection must be distinguishable from rest at a glance and from each other by more than color. | `ButtonHighlight`, gallery `hover` / `keyboard-selected` frames |
| Gamepad | Everything reachable by keyboard is reachable by gamepad; the selected control is always visibly marked. | `gamepad-selected` class |

Good: a menu of same-height buttons, centered on a card, titles in the title style, one accent color.
Bad: a one-off `Padding="13,16"`, an inline `#FF123456`, a button that is 18px tall, a label that is
secretly clickable. Each of these is caught by a test (see below).

## Pattern library

Copy a proven structure instead of inventing one. Each is a compiled view you can open, plus a gallery
scene that renders it.

| Pattern | View | Gallery scene |
| --- | --- | --- |
| Centered menu card with stacked buttons | `MainMenuView`, `PauseView`, `PlayMenuView` | `main-menu` |
| Tabbed settings page with label / value / action rows | `SettingsView`, `SettingsControlsPageView`, `SettingsBindingRowView` | `settings-general`, `settings-controls` |
| List with realized rows and per-row actions | `MapEditorView`, `MapEditorRowView`, `MapBrowserRowView` | `map-editor-list` |
| Modal dialog with up to three actions | `MapEditorView` dialog, `BrowserView` dialog | `unsaved-dialog` |
| Round setup form | `RoundSettingsView`, `RoundSettingRowView` | `local-game` |

Annotated anatomy of the menu card (`PauseView.xaml`):

1. `Container` root, so the hint bar can overlay the card.
2. `Border` with `trace-surface-card trace-border`, fixed minimum width (the card never collapses).
3. `VBoxContainer` with a spacing token between children.
4. Title `Label` with `trace-title`.
5. `Button` children with `trace-button`, a minimum size, a localized `Text` binding and an
   `x:Name` only for code that needs the instance. The accessible name comes from the text, never
   from the XAML name.

A row view that is created by a data template is its own style boundary. Call
`Resources.TraceStyle.InheritStyles(this)` (which uses `StyleEngine.AttachFrom`) in its constructor so
the shared classes reach it.

## Known limits and workarounds

| Limit | Workaround |
| --- | --- |
| A data template, item row or compiled view is a style boundary; root styles do not reach it. | Call `StyleEngine.AttachFrom(view, source)` from the view. |
| A style setter for `ColorRect.Color` does not apply. | Set `Color` on the control directly; keep it to the documented exceptions in `TokenLintTests`. |
| `DynamicResource` in a style setter is resolved each time the style applies, not live. | Re-apply (class or state change) or use a control-local `DynamicResource`. |
| `StyleSelector` descendant rules do not cross a style boundary. | Attach the style set inside the boundary. |
| Layout never shrinks a control below its minimum size, so an over-long translation runs off the screen instead of clipping inside its parent. | The design lint's offscreen check catches it; shorten the text or lower the minimum. |
| Headless tests cannot measure glyphs. | Rely on gallery frames for pt-BR and ja to catch glyph-level clipping. |
| Code-built controls are not reached by tree styles until attached to a styled tree. | Give them a class and attach them under a styled root. |

## Troubleshooting

| Symptom | Ask | Tool |
| --- | --- | --- |
| "Why is this not highlighted?" | Which rules matched, which did not and why? | `StyleInspector.Inspect(control)`; in the live game `game_ui` action `ui-inspect button=<automation id or name>` returns `styleInspection`. A "no style set attached" reason means a style boundary: use `AttachFrom`. |
| A rule exists but never applies | Is the class spelled right, is the pseudo-state active, is an adaptive condition (modality) unmet? | The inspector's reason text names the exact missing class, inactive pseudo-state or unmet condition. |
| Text is invisible or faint | Is contrast below 4.5:1 in some state? | `DesignLint` contrast checks and `StateColors_KeepTheirTextReadable`. |
| A button cannot be found by an agent or screen reader | Does it have a human name? | `ui-tree` via `game_ui`; the naming lint. |
| The layout looks different in another language or scale | Does it still fit? | `make gallery` renders en, ja and pt-BR at 100% and 130%; the lint runs en, es, pt-BR and ja. |
| "Did my change break another screen?" | Which screens render differently? | `make gallery-changed` re-renders only the affected scenes and reports the diff; `make gallery` renders all. Accept intended change with `make gallery-update` after reviewing the overlays. |
| A color or spacing value is "wrong" | Is it a literal? | `TokenLintTests` fails on raw hex, off-scale spacing and `FontSize` in views. |

## Verification checklist

Before declaring a UI change done: `dotnet build` and `dotnet test` are clean, `make gallery-changed`
shows only intended change, and `tools/lint-mutation-proof.sh` still shows every lint failing on its
seeded defect.
