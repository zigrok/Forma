# Shared Stylesheet Proof Report

Companion to [fcss-shared-stylesheets-plan.md](fcss-shared-stylesheets-plan.md): stages 1 to 4.

## What was built

- `<link rel="stylesheet" href="theme.fcss">` in `.fhtml`, resolved relative to the linking file, with `FHTML1006` for a missing
  or out-of-project target and `FHTML1004` for URLs, plain `.css` and other `rel` values.
- `.fcss` files use the same strict parser and diagnostics as a `<style>` block, located at the `.fcss` line.
- `FormaHtmlProject` reads and parses each sheet once. Each sheet also converts once to a ResourceDictionary document
  (`*.fcss.dict`) with a source map. A view's generated XAML carries the sheets' tokens and rules first, then its own, so the
  view's own `<style>` wins ties.
- Tokens: `:root { --x: … }` lowers to typed resources. A color becomes a `SolidColorBrush` (`Fcss.x`) and a `Color`
  (`Fcss.x.color`), a single length or number becomes a `Single`. `var(--x)` and `resource(Key)` lower to `{DynamicResource}` for
  `color`, `background-color`, `border-color`, `font-size`, `opacity` and `gap`, and substitute statically elsewhere (shorthand
  such as `padding`, `min-width` pairs). A view-local token shadows the shared one. A token of the wrong kind is `FHTML2003`.
- Bridges to XAML: `resource(Key)` references a XAML-defined resource, `-f-Property: value` sets any Forma property, and
  `<f-resources>` embeds raw XAML resources (templates, storyboards) in a view.

## Decisions

- **Color versus Brush:** two resources per color token, chosen by property (`color` takes the `Color`, `background-color` and
  `border-color` take the brush). One token, no conversion at use, both typed correctly.
- **Merging:** the plan asked for a dictionary merged into each linking view. Forma XAML has no external dictionary reference, so
  the converted entries are placed in each linking view's own root resources, ahead of the view's entries. A sheet is converted once,
  but each linking view instantiates its own copy; a theme linked from the application root reaches every screen through the style
  engine's default reach, so the live-token case needs one link.

## Proof: Trace Arena's theme

`TraceRootView` is now `TraceRootView.fhtml`, linked to `theme.fcss`. The state gallery shows 0 changed frames of 33.

Moved into `theme.fcss` (44 styles): surfaces, borders, text colors, titles, the button, spin arrow, settings row and field
classes, hover/selected weights, the group box, with `resource(Trace.*)` bridging to the tokens.

Stays in XAML (inside `<f-resources>` of `TraceRootView.fhtml`), because the dialect has no way to express it:

| Kept in XAML | Why |
| --- | --- |
| `Trace.*` tokens | `TraceStyle` reads them by key and the token lints scan them; a key rule that preserves `Trace.Color.Surface` casing was not worth inventing |
| Control templates, storyboards | no CSS counterpart |
| `ColorRect.Color` styles | `ColorRect` has no selector type and the style does not apply anyway (known limit) |
| `>>` template-child selectors (button chrome, field chrome, list items, grid rows) | CSS has no template-child combinator |
| `CheckBox`, `ListBox`, `DataGrid`, `TabContainer` styles | no selector type in the dialect yet |

20 styles remain in XAML.

## Decision (stage 4)

**Continue.** Faithful: 33 of 33 frames unchanged, 478 Trace tests pass, the existing lints and mutation proofs pass. Diagnostics are
usable: a broken sheet or missing link fails the build with a code and the `.fcss` or `.fhtml` line. Ease of change: a class style is
now one short rule in `theme.fcss` instead of an XML element with a key, and a token override is one `:root` line.

Observed during the proof: the gallery frame for the pt-BR profile once showed a hovered button when the physical mouse was over the
window; a rerun matched the baselines. That is an existing gallery sensitivity to the real pointer, not an effect of the theme.

The hybrid is the answer for the dialect's limits: templates, template-child selectors and tokens stay XAML for now. Adding
selector types and a template-child combinator is the next widening if the `.fcss` theme is to cover more.
