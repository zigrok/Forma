# FHTML/FCSS-Only UI for Trace Arena

## Objective

Make Trace Arena's entire user interface, every screen and the whole theme, authored in the Forma HTML and CSS dialect
(`.fhtml` and `.fcss`) with **no XAML source file in the Trace repository**. This is the proof that the dialect can carry a real,
shipping game UI end to end.

Forma itself keeps full XAML support. The dialect is an alternative front end that converts to the same canonical XAML in the
intermediate directory and then uses the same compiler, runtime and trimming/NativeAOT guarantees. A project can use XAML, the
dialect, or both. Trace Arena is simply the project that uses only the dialect. Nothing in this plan may remove, deprecate or
weaken XAML support, and every Forma change must keep the existing XAML tests, golden files and public API baseline intact.

This plan builds on [html-css-authoring-frontend-plan.md](html-css-authoring-frontend-plan.md),
[fcss-shared-stylesheets-plan.md](fcss-shared-stylesheets-plan.md) and their proof reports. It assumes the work in the goal that
closes the stylesheet caveats (gallery stability, CI protection, selector types, the template-child combinator, tokens in `.fcss`).

## End state (acceptance)

1. `git ls-files 'src/**/*.xaml'` and `git ls-files 'tests/**/*.xaml*'` in the Trace repository list nothing. This includes
   `TraceRootView`, every view and row template, the control templates, the storyboards and the tokens. The `.xaml.txt` originals kept
   for equivalence tests are deleted at the end, replaced by the gallery and by accessibility-tree snapshots.
2. No `<f-resources>` blocks and no raw XAML embedded in any `.fhtml` or `.fcss`. If a construct cannot be written in the dialect,
   the dialect gains it (this plan's phases) rather than Trace keeping XAML.
3. `make gallery` shows 0 changed frames of 33 against the baselines that existed before the conversion began. Any visible
   difference is a defect to fix, not a baseline to update, unless an explicit, explained exception is recorded in the decision log.
4. The same behavior as today: Regular to Bold on hover and selection, the shared bordered button, keyboard and gamepad selection,
   focus adorner and controller animation, application entrance animation, localization in en, es-419, pt-BR and ja, UI scale, all
   existing tests, lints and mutation proofs, hot reload, and the release/NativeAOT artifact checks.
5. Authoring and accessibility parity: automation ids, accessible names and the accessibility tree of every converted screen are
   identical to the XAML version (verified screen by screen before the original is deleted).
6. Forma's XAML path is untouched: the Forma test suites, the API baseline (additions only) and the XAML samples still pass.
7. An AI agent that is asked to build or change UI in a Forma project finds out, without being told, that it should author `.fhtml` and
   `.fcss` and use the preview, query, inspect, validate and lint tooling to iterate. This is delivered by an installable agent skill,
   a generated agent quickstart and reference, an MCP server and a setup command (Phase F), and it is proven with agent evaluations
   rather than assumed.

## Design criterion: easier for AI agents, with HTML and CSS tooling parity

The purpose of the dialect is to let AI agents design and validate Forma UIs with the same confidence and capability they have with
HTML and CSS, because they are already well trained on both. Every decision in this plan is made against that criterion:

- **Prefer the standard spelling.** Use the real HTML element, attribute, ARIA role, CSS property, selector and at-rule when one
  exists and has a defined Forma mapping. Introduce an `f-` name, a Forma-only attribute or a vendor-style escape only when no
  standard construct fits, and say why in the decision log.
- **Prefer the standard behavior.** Where Forma differs from a browser, the difference is documented, diagnosed and tested; the
  dialect never silently does something a browser would not.
- **Provide the equivalent of the tools agents rely on** for web UI: a queryable element tree, computed styles, "why did this rule
  not apply", element screenshots, device and preference emulation, validators and linters with machine-readable output, editor data
  files and a reference manual (see Phase D).
- **Make the feedback loop one step**: change a file, get a rendered verdict and a diff (already true through `make gallery-changed`).

## Principles

- **Dialect first, escape hatches last.** The `f:` attribute, `-f-` property and `resource()` bridges exist for the long tail, but an
  `<f-resources>` block holding XAML is not allowed in Trace by the end state.
- **One screen at a time, always green.** Each view converts, passes the gallery and its equivalence checks, and only then is the
  XAML original deleted. Trace builds and ships between every step.
- **Strict, not approximate.** Anything outside the dialect fails the build with a stable diagnostic and a source location.
- **Additive Forma changes.** New elements, attributes, properties and diagnostics are additive. Each converter change gets a test
  that fails before and passes after, plus a catalog story.
- **Tooling parity grows with the dialect.** Formatter, hot reload, source maps, the inspector origin, the lints and the release
  artifact checks cover every new construct.

## Inventory (Phase 0 audit, 2026-10-08)

Trace has 20 view XAML files left (about 1,200 lines) plus three `.fhtml` sources (main menu, settings controls page, root view with
`theme.fcss`). Constructs found by scanning every file:

| File | Lines | Constructs beyond containers, labels, buttons, borders and bindings |
| --- | --- | --- |
| BrowserView | 232 | `DataGrid` with 6 `DataGridTemplateColumn`s and `DataTemplate` cells, `LineEdit`, dialog, `PromptHintBar` |
| MapEditorView | 226 | two `ListBox`es with `DataTemplate` rows, `LineEdit`, `ScrollContainer`, dialog, `PromptHintBar` |
| SettingsGeneralPageView | 120 | `ColorRect`, `LineEdit`, `ScrollContainer`, `TraceSpinBox` |
| MapBrowserView | 105 | `ListBox` with `DataTemplate` row view, `LineEdit`, dialog |
| LocalGameView | 93 | `OptionButton`, `TraceSpinBox`, `PromptHintBar` |
| ScoreboardView, StandingsView | 67, 63 | `ItemsControl` with `ItemsPanelTemplate` and `DataTemplate`, `ColorRect`, `ScrollContainer` |
| SettingsSoundPageView | 64 | `HSlider`, `ScrollContainer` |
| PlayMenuView, PauseView, MatchOverView | 55, 45, 46 | containers, buttons, labels, `PromptHintBar`, embedded `StandingsView` |
| RoundSettingsView, RoundSettingRowView | 46, 51 | `ItemsControl` with `ItemsPanelTemplate` and `DataTemplate` row view, `CheckBox`, `OptionButton`, `ColorRect`, `TraceSpinBox` |
| ManagedQuickMatchView, SettingsView | 37, 28 | `PromptHintBar`; `TabContainer` |
| MapBrowserRowView, MapEditorRowView, SettingsBindingRowView | 42, 19, 33 | row views: containers, labels, buttons |
| DebugConsoleView, FormaProbeView | 8, 8 | an `OverlaySurfaceView`; a `BoxContainer` probe |
| TraceRootView (already `.fhtml`) | | `<f-resources>`: tokens, three `ControlTemplate`s, three `Storyboard`s, about 20 `>>`/typed styles |

Distinct element types still to express (counts are files using them): `DataTemplate` 5, `ScrollContainer` 5, `LineEdit` 4,
`ColorRect` 3, `TraceSpinBox` 3, `ListBox` 2, `ItemsControl` 2, `ItemsPanelTemplate` 2, `OptionButton` 2, `CheckBox`, `HSlider`,
`DataGrid` (+ columns), `TabContainer`, `BoxContainer` (probe), and the application controls `PromptHintBar`, `OverlaySurfaceView`,
`StandingsView` and the row views. Everything else is already expressible. Refinements to the decisions from this audit are in the
decision log.

## Decisions

Made by the plan's owner on the user's instruction, using the design criterion above: the goal for `.fhtml` and `.fcss` is to make it
easier for AI agents to design and validate Forma UIs with the same confidence and capability they have with HTML and CSS. Each
decision cites the HTML or CSS convention it follows. They can be revisited in the decision log, not silently.

### D1. Template parts and the part selector (decided)
Use the CSS Shadow Parts model. A template element marks a part with the `part` attribute and a stylesheet selects it with
`::part(name)`; `:host` selects the templated control itself.

```css
button.trace-button:hover::part(chrome) { background-color: var(--trace-color-surface-highlight); }
```

- Rationale: `::part()` and `:host` are real CSS that agents already know; `>>` is not CSS (`>>>` and `/deep/` were deprecated).
- Lowering: a `part="chrome"` element gets the class `part-chrome` and the name `PART_Chrome`; `A::part(x)` lowers to the Forma
  template-child selector `A >> *.part-chrome`. Forma XAML keeps `>>` unchanged.
- The dialect accepts only `::part()`; `>>` in a `.fcss` is a diagnostic that names the replacement.

### D2. Data templates, lists, grids and tabs (decided)
Use the HTML elements agents know.
- `<template>` is the HTML template element. Inside a list, grid or items control it is the item template; its content is the row.
- Lists: `<ul bind:items="Rows">` with a `<template>` child is an `ItemsControl`; adding `selectable` makes it a `ListBox`
  (`bind:selected-index` two-way, `onitemactivated`). `<ol>` is the same, numbered.
- Tables: a `<table>` with `bind:items` and a `<template>` row inside `<tbody>` is a `DataGrid`; `<th>` cells give headers,
  `width="2*"` and `data-sortable` give column options. A table without `bind:items` stays a static layout grid, as today.
- A row, cell or page may also be its own file referenced by `<template src="MapRow.fhtml">`, so the existing row views convert as
  they are.
- Tabs follow the ARIA tab pattern: `<div role="tablist">` with `<button role="tab">` children and `<div role="tabpanel">` panels
  lowers to a `TabContainer`. Agents already know the pattern and it keeps the accessibility tree honest.
- Dialogs use `<dialog>` (`open` attribute, modal behavior, Escape handling through the existing dialog support).
- Rationale: no `f-list`, `f-data-grid` or `f-tab-container`; each of these is the HTML or ARIA construct for the same job.

### D3. Control templates (decided)
Custom control appearance is a Web Components style template.

```html
<template for="button" id="trace-button-template">
  <div part="chrome" style="border-width: 1px; border-radius: 3px; padding: 2px 14px">
    <slot></slot>
  </div>
</template>
```

- `for` names the Forma control type, `<slot>` is the content presenter (the control's text or content), `part` marks named parts.
- A templated-parent binding is `bind:text="host.Text"`, where `host.` follows `:host` (the templated control).
- A rule applies a template with `-f-template: template(trace-button-template)`, and an element with `template="trace-button-template"`.
- Rationale: `<template>`, `<slot>`, `part` and `:host` are the standard Shadow DOM vocabulary.

### D4. Animations (decided, with a spike)
Use CSS `@keyframes` and `animation`, plus `transition` (already supported).
- `@keyframes` lowers to a `Storyboard` with timelines and key frames; `animation-duration`, `-timing-function`, `-iteration-count`,
  `-direction` and `-fill-mode` map to Forma's repeat, easing, auto-reverse and fill behavior.
- Starting an animation is declarative where the engine can do it (a rule or class that sets `animation`), and by name from
  code-behind (`Begin("entrance")`) otherwise. Phase A spikes class-triggered starts first; if the style engine cannot start a
  storyboard from a class, code-behind starts remain and the limit is documented.
- Only properties Forma can animate are accepted; anything else is a diagnostic naming the animatable set.
- `@media (prefers-reduced-motion: reduce)` maps to Trace's existing motion setting so agents can express reduced motion the way
  they would on the web.

### D5. Tokens (decided)
CSS custom properties are the tokens, with their real names. The resource key is the custom property name without the leading
dashes (`--trace-color-surface` has key `trace-color-surface`), and `var(--trace-color-surface)` is the reference, exactly as in CSS.
- Typed output as today: a color becomes a brush (key) and a `Color` (key plus `.color`), a number or length becomes a `Single`.
- Trace's code, lints and `TraceStyle` change to the kebab keys. The explicit-key-comment idea is dropped because it is not CSS and
  adds a second spelling an agent could get wrong.
- Rationale: the rule an agent already knows is the rule used.

### D6. Small controls (decided)
Use HTML for each control; a custom element only where Forma has no HTML counterpart.
- `input` types `text`, `checkbox`, `range`, `number` (a spin box); `textarea`; `select` with `option`; `progress`; `details` with
  `summary` (a foldable container); `dialog`; `hr` and an empty `div` with only `background-color` (a `ColorRect`); `img` and inline
  `svg` (the existing image controls, with a defined source form).
- Application controls are custom elements: a hyphenated tag registered with
  `<meta name="f-element" content="trace-spin-box=local:TraceSpinBox">`, used as `<trace-spin-box id="PlayerSpin">`. `f-control type=`
  remains as the explicit fallback.
- Rationale: custom elements are how HTML names application components.

### D7. The probe view and triggers (decided)
`FormaProbeView.xaml` exists to exercise Forma XAML features. Retire it; if a pseudo-class style covers its purpose, keep a dialect
version, otherwise delete it and its tests with a note in the report. No XAML trigger construct is added to the dialect.

### D8. Code-behind and wiring (decided)
Unchanged: `data-class`, `id`, `onclick`, `onchange`, `bind:`, `data-automation-id`, `aria-*`, `role`. Standard `aria-label` and
`role` stay the way accessibility is authored, and the accessibility tree is the equivalence check.

### D9. Layout vocabulary: web semantics wherever Forma has a primitive (decided)
Agents write the full CSS box and flex/grid vocabulary first, so the dialect implements it instead of rejecting it, using Forma
containers that already exist (`Margins`, size flags, `CustomMinimumSize`/`MaxWidth`, `ZIndex`, `GridPanel`, `WrapPanel`,
`FlowContainer`, `OverlayPanel`, `CanvasPanel`, `ClipContents`, `ScrollContainer`).
- `margin` and the longhands lower to the control's own margin (`Margins`). **This corrects a current inaccuracy:** the dialect
  today lowers `padding` on a flex container to `Margins`, which is outer-margin behavior. In Phase A, `padding`, `border` and
  `background` on a `display: flex` or `grid` element lower to a `Border` wrapping the container (CSS box semantics), and `margin`
  lowers to `Margins`. The Trace views that relied on the old mapping are re-expressed and the gallery proves no pixel changes.
- `width`, `height`, `min-*`, `max-*` lower to the matching custom size properties. `box-sizing` is documented as `border-box`
  (Forma sizes include padding and border) and `content-box` is a diagnostic.
- `align-items`, `align-self`, `justify-content`, `justify-items`, `place-*` lower to alignment and size flags per the container
  direction; `flex-grow`, `flex-shrink`, `flex-basis` and `flex` lower to size flags plus minimum sizes; `flex-wrap` lowers to a
  `WrapPanel`/`HFlowContainer`; `order` is a diagnostic with the alternative of reordering markup.
- Percentages and `fr` become proportional tracks in a grid or flex parent; `vw`/`vh` and `%` against an unconstrained parent are a
  diagnostic whose help text names the proportional alternative.
- `position: relative|absolute|fixed` with `top/right/bottom/left` lowers to `OverlayPanel` or `CanvasPanel` children (the parent with
  positioned children becomes the overlay), and `z-index` lowers to `ZIndex`. `overflow: hidden` lowers to `ClipContents`;
  `overflow: auto|scroll` to a `ScrollContainer`.
- Every construct that still cannot be mapped fails with a stable code and a help line naming the nearest supported alternative.

### D10. Selectors and state without JavaScript (decided)
- Pseudo-classes: add `:focus-visible` (focus reached by keyboard or gamepad modality), `:focus-within`, `:first-child`,
  `:last-child`, `:only-child`, `:nth-child(an+b)`, `:empty`, `:is()`, `:where()` and `:not()` with selector lists.
- State by attribute: `data-*` attributes are real, queryable state. A control carries a data set (additive Forma API), code-behind
  sets values (`view.SetData("state", "open")`), and `[data-state="open"]`, `[data-state]` and `[disabled]` selectors match and
  re-evaluate on change. This is how agents express variants and state in plain CSS.
- Combinators: descendant, child and, as part of this decision, adjacent sibling `+` and general sibling `~` where Forma has sibling
  order (documented otherwise).

### D11. Localization, direction and theme conventions (decided)
- Text localized through the application's localizer uses `data-i18n="menu.play"` (the convention i18next-style tools use) and
  `data-i18n-attr="aria-label:menu.play"` for attributes. It lowers to a binding on the localizer indexer, with the key checked at
  build time against the application's key list when one is declared (`<meta name="f-i18n-keys" content="Locales/en.json">`).
- `lang` on the root names the source language; `dir="rtl"` sets flow direction. CSS logical properties (`margin-inline-start`,
  `padding-inline`, `text-align: start`) lower through Forma's right-to-left layout support; where a logical property cannot flip at
  runtime the build reports a diagnostic with the physical alternative.
- Themes use `prefers-color-scheme` and `:root[data-theme="…"]` token overrides. A theme switch sets the root's `data-theme`
  (D10) and the live tokens (D5) update running UI. Phase A spikes how token overrides keyed by an attribute lower to swapped
  resources before committing to the exact mechanism.

### D12. Visual effects: implement what the renderer can draw (decided)
- Implement: `box-shadow` (the border shadow collection), `linear-gradient()` and `radial-gradient()` backgrounds (gradient brushes),
  per-corner `border-radius` where the corner type allows, `outline` and `outline-offset` for focus rings, `opacity`, and `transform`
  with `translate`, `scale` and `rotate` (render transforms), with `transform-origin`.
- Reject with a help line: `filter`, `backdrop-filter`, `mix-blend-mode`, `clip-path`, `mask`, `text-shadow` unless the renderer
  gains them, each naming the closest available effect (for example `opacity`, `box-shadow`).
- Phase A spikes each implemented effect against the renderer in the gallery before it is documented as supported; an effect the
  renderer cannot draw faithfully moves to the rejected list and the support matrix says why.

### D13. Typography, images and icons (decided)
- Typography: `font-family` and `@font-face` over Forma's registered faces, `font-size`, `font-weight`, `font-style`,
  `line-height`, `letter-spacing`, `text-align`, `text-transform`, `text-decoration`, `white-space` (`nowrap`, `normal`) and
  `text-overflow: ellipsis` (label overrun behaviors). Properties the text engine lacks are rejected with help.
- Images: `<img src="…">` and inline `<svg>` map to the existing image and SVG controls with project-relative assets processed by
  the existing asset pipeline; `alt` becomes the accessible name. `background-image` and icon fonts are rejected with the `img`/`svg`
  alternative.

### D14. Preview, support matrix and cookbook (decided)
- A standalone preview command (Phase D) renders one view headlessly with fake data. The data comes from a sidecar
  `View.preview.json` deserialized into the view's declared `data-type`, or a `[FormaPreview]` static factory in C# for types that JSON
  cannot build. The preview host is build-host and Debug tooling only and never ships.
- A support matrix (caniuse style) and a tested cookbook are generated and kept current from the same catalog the tests use; every
  rejection diagnostic carries a help line naming the nearest supported alternative.

### D15. How agents learn Forma and its tooling (decided)
Instructions an agent can load on demand, tools it can call, and discoverability at the moment it needs them. Not one big document.
- **An installable agent skill.** Forma ships a skill in the common `SKILL.md` format (a short trigger description, then instructions
  with links to reference files loaded only when needed). `forma-xaml agent install [--target opencode|claude|agents-md|all]
  [--scope project|user]` writes it into the project (or the user's skill directory) and adds a marked, versioned block to `AGENTS.md`
  or `CLAUDE.md` so agents that only read those files still get the rules. `agent update` refreshes it, `agent doctor` checks that it
  is installed, current and that the tools it names exist.
- **An MCP server.** `forma-xaml mcp` (stdio) exposes the tooling as tools any MCP-capable agent can call: `preview`, `ui_query`,
  `ui_inspect`, `validate`, `lint`, `format`, `gallery_diff`, plus the documentation as resources. The skill names the MCP tools and
  falls back to the equivalent CLI commands, so it works with or without MCP. Trace's own game MCP bridge keeps its game-specific tools.
- **Docs written for agents.** A one-page agent quickstart (the loop, the five rules, the command table), a generated reference from
  the dialect catalog, the support matrix, the cookbook, and a symptom-to-command troubleshooting table, all in the repo, and
  `llms.txt` and `llms-full.txt` at the docs root for agents that browse documentation. Everything generated from the catalog is
  regenerated by the build so it cannot go stale.
- **Discoverability at the point of need.** `forma-xaml --help` and `forma-xaml agent` print the quickstart; MSBuild prints a one-time
  informational message in a project that references Forma.Xaml.Build and has no installed skill (opt out with a property); every
  diagnostic's help line names the command or doc that fixes it; the `.fhtml` project template and `forma-xaml new screen` scaffold a
  view, preview data and a gallery entry so the first screen already uses the loop.
- **The skill's content** (kept under about 150 lines, with references loaded on demand):
  1. Trigger: any request to design, change, style or debug UI in a project that references Forma.
  2. The rules: write `.fhtml` and `.fcss` (never XAML unless the project already uses it), use tokens and shared classes, never guess
     a property (consult the support matrix), keep behavior in C# code-behind and view models.
  3. The loop: edit, `preview` (with the states and languages that matter), `ui_query` and `ui_inspect` when something looks wrong,
     `validate` and `lint`, then the gallery diff before finishing.
  4. A command and tool reference with one example each, and a table of common mistakes with their diagnostics.
  5. Links to the cookbook, the support matrix and the troubleshooting table.
- **Proven, not assumed.** Phase F measures it with agent evaluations (below), the way the skill-creator workflow does for skills.

## Phases

### Phase 0: audit and decisions
- Script and hand-audit every remaining XAML file; replace the inventory table with the exact constructs per file.
- Re-read D1 to D15 against what the audit finds and record any amendment in the decision log. The decisions are already made; the
  audit may only refine them, using the design criterion.
- Acceptance: the inventory is exact and the decision log is current. No dialect code yet.

### Phase A: widen the dialect (Forma)
In this order, each with converter tests (golden input and expected XAML, plus rejection tests with code, message and location), a
catalog story and tooling parity (formatter, hot reload, source map, inspector origin):
1. Selector types for every control Trace styles, `:host`, and `::part()` with the `part` attribute (D1).
2. Tokens with CSS names (D5).
3. `<template for>` control templates with `<slot>`, `part` and `host.` bindings (D3).
4. `<template>` item templates, `ul`/`ol` items and selectable lists, `table` data grids with columns, ARIA tabs, `<dialog>` (D2).
5. `@keyframes` and `animation`, `prefers-reduced-motion` (D4).
6. The small controls and registered custom elements (D6), and the probe-view resolution (D7).
7. Layout vocabulary, including the padding and margin correction (D9). First proof: re-express the Trace views that relied on the old
   mapping and keep the gallery at 33 of 33.
8. Selectors and `data-*` state (D10), a small additive Forma runtime change with tests that fail before and pass after.
9. Localization, direction and theme conventions (D11), starting with a spike for attribute-keyed token overrides.
10. Visual effects (D12) and typography, images and icons (D13), each spiked against the renderer first.
- Proof for each step: a Trace view or the theme uses it and the gallery stays at 33 of 33. The first views converted for this
  purpose are the hardest ones: the server browser (data grid), the map editor (list, dialogs), settings (tabs) and the theme.
- Acceptance: the dialect can express every construct in the Phase 0 inventory. Forma tests, the XAML tests and the API baseline pass.

### Phase B: convert Trace
- Convert the root view first (templates, storyboards, tokens, all styles, no `<f-resources>`), then the theme `.fcss`, then views
  in batches ordered by risk: simple menus, dialogs and overlays, settings, lists and editors, the server browser, the debug console.
- Per-view recipe (every view, no exceptions): convert; build; gallery 33 of 33; accessibility-tree and `AutomationId` snapshot equal
  to the XAML version; Trace tests, design lint and `.fhtml` lints; formatter check; then delete the XAML original in the same commit.
- Keep a record of anything awkward in the proof report, and fix it in the dialect if it recurs.
- Acceptance: End state items 1 to 6.

### Phase C: finish and prove
- Remove the `.xaml.txt` originals and the structural XAML comparisons; replace them with accessibility-tree snapshots and golden
  conversions of the shipped views.
- Update docs: the dialect guide, the design-system page, the agent design guide, Trace's `docs/ui-architecture.md` and `AGENTS.md`
  (no XAML in Trace; how to author a screen), and the plan reports.
- Run the full gate, including a real Release publish and the artifact checks, and a clean-clone build to prove nothing depends on a
  leftover XAML file.
- Acceptance: End state items 1 to 6 hold on a clean clone and CI is green on the self-hosted runner.

### Phase D: agent tooling parity with HTML and CSS

Runs alongside Phases A to C and finishes before the end-state proof. The target is that an agent can do for a Forma UI what it does
with browser DevTools and web linters. Each item has a test and a short reference entry:

| Web tool or habit | Forma equivalent to provide |
| --- | --- |
| `document.querySelector(All)` and the Elements panel | `ui-query selector=".trace-button:hover"` through the MCP bridge, returning matches with id, classes, automation id, role, name, bounds and state; the existing tree dump gains HTML-style tag names and attributes |
| Computed styles and "why is this not applied" | `ui-inspect` already reports matched rules, winning values with `rule @ file:line` and unmatched reasons; extend it to accept a CSS selector and to show the inherited and token-resolved value |
| Element and region screenshots | `take_screenshot` with a selector or a bounds clip, so an agent looks at one component |
| Device, viewport, locale and preference emulation | gallery profiles already cover size, scale and language; add `prefers-reduced-motion`, input modality and a forced focus-visible state |
| Live reload and visual regression | hot reload of `.fhtml` and `.fcss`, `make gallery-changed` with the baseline diff (already) |
| Linters (HTML validators, stylelint, axe, Lighthouse) | `forma-xaml validate` for `.fhtml` and `.fcss` with `--format json\|sarif`, the existing design and token lints exposed with stable rule ids and fix hints, undefined-class and raw-literal lints |
| Editor intelligence (HTML and CSS custom data) | generate `html.customData` and `css.customData` JSON from the dialect catalog so editors and agents get completion and hover for `f:`, `bind:`, `part`, `::part()`, `resource()` and `-f-` |
| MDN-style reference | a generated dialect reference (every element, attribute, property, selector, at-rule, unit and diagnostic with an example), produced from the same catalog the tests use |
| Opening one HTML file in a browser | `forma-xaml preview View.fhtml --state hover:#PlayButton --lang ja --size 1280x720 --scale 130 -o out.png`, headless, fake data from `View.preview.json` or a `[FormaPreview]` factory (D14); a component catalog page that renders states side by side |
| DevTools box model overlay | a Debug overlay drawing margin, padding and content boxes and text-overflow markers, toggled through the MCP bridge and by `preview --overlay` |
| caniuse and MDN compatibility tables | a generated support matrix per element, attribute, property, selector, at-rule and unit (supported, partly, rejected with reason), plus a tested cookbook of menus, forms, lists, dialogs, tabs and a settings page |
| Error messages that suggest fixes | every `FHTML` diagnostic carries a help line naming the nearest supported alternative, checked by a test that no rejection lacks one |
| Snapshot and golden tests | accessibility-tree snapshots per screen and golden conversions of the shipped views (Phase C) |

Acceptance: each row has a working command or file, a test, and an entry in the agent design guide; the guide's troubleshooting table
maps a symptom to the query that diagnoses it.

### Phase F: agent onboarding and documentation

Starts when the Phase D tools exist (preview, query, inspect, validate, lint) and finishes before the end-state proof. Delivered in
Forma, installed into Trace as the first real consumer.

1. **Documentation architecture.** Reorganize the docs task-first for agents: quickstart, cookbook, generated reference and support
   matrix, troubleshooting, and the dialect guide; remove duplication; every page states when to use it. Generate `llms.txt` and
   `llms-full.txt`. A docs build check fails if a generated page is stale or a catalog entry has no reference entry.
2. **The skill.** Author `forma-ui` (SKILL.md plus references) per D15. Its description is tuned so the skill triggers for UI requests in
   Forma projects and not for unrelated work.
3. **The installer.** `forma-xaml agent install|update|doctor` with targets for opencode, Claude and `AGENTS.md`/`CLAUDE.md`, project and
   user scope, idempotent marked blocks with a version stamp, and a dry run that prints what would change. Tests cover fresh install,
   re-install, update, a pre-existing `AGENTS.md`, and removal.
4. **The MCP server.** `forma-xaml mcp` exposing the tools and docs resources, with a conformance test that lists the tools and calls
   each against a fixture project, and a build-host/Debug-only guarantee (never in release artifacts).
5. **Discoverability.** The `--help` output, the one-time MSBuild message and its opt-out, help lines on diagnostics, `forma-xaml new
   screen` and the project template.
6. **Install it in Trace.** Run the installer in the Trace repository, and replace the hand-written UI guidance in `AGENTS.md` with the
   generated block plus Trace-specific facts (`make gallery-changed`, the game MCP bridge, the lints). The skill and `AGENTS.md` block
   are regenerated by a Make target and checked in CI for drift.
7. **Agent evaluations.** A fixed set of scenarios run against an agent with and without the skill and MCP server, scored on the
   outcome and the process:
   - build a settings screen from a description, with a list and a dialog;
   - fix text clipping in Japanese at 130% scale;
   - restyle the hover and selected states of a list;
   - add a data grid to a screen with fake preview data;
   - migrate a small XAML view to `.fhtml`.
   Scores: the result converts, lints and matches the gallery or an agreed reference; whether the agent used `preview`, `ui_query` and
   `ui_inspect` before declaring done; the number of iterations; the number of rejected-construct diagnostics it hit; whether it
   touched XAML. Results are stored in `plans/agent-eval-report.md` and the skill is revised until the with-skill runs reliably use the
   tooling. The eval harness is tooling only and does not ship.

Acceptance: the installer and MCP tests pass; the docs build check passes; the evaluation report shows the with-skill runs use the
preview and query tools in every scenario and converge in fewer iterations than the without-skill runs; a fresh Trace clone plus
`forma-xaml agent doctor` reports a healthy setup.

## Verification (every phase, from runs after the last change)

- `dotnet build Trace.slnx`: 0 errors and 0 warnings; `dotnet test Trace.slnx`: all passed with new tests for the phase.
- Forma: `tests/Forma.Tests`, `tests/Forma.Xaml.Compiler.Tests`, `tests/Forma.Xaml.HotReload.Tests`, and
  `bash scripts/check-release-api.sh` reporting additions only. XAML golden tests are unchanged.
- `make format-xaml-check` (covers `.fhtml` and `.fcss`), `make verify-source-dependencies`, `make gallery` (33 of 33).
- `tools/lint-mutation-proof.sh`, `tools/verify-no-tooling.sh`, `tools/fhtml-diagnostic-proof.sh`, and a real Release publish passing
  `tools/verify-client-artifact.sh`.
- Preview command: a test renders a view with preview data and compares it with a baseline image; the matrix and cookbook generation
  fail the build if a catalog entry or example stops converting and compiling.
- Agent onboarding: installer, MCP and docs-build tests pass, `forma-xaml agent doctor` is green in Trace, the generated `AGENTS.md`
  block and skill have no drift, and the evaluation report is committed with the with-skill runs using the tooling.
- A check that fails if any `.xaml` file appears in the Trace repository after the end-state commit.
- CI green on the self-hosted runner for the final push; tracking issue and issues #1 to #3 fully ticked with commits.

## Risks

- **Scope.** The largest piece is data templates plus lists and grids. Mitigation: prove them on the server browser and map editor
  before converting easy views, so the design is validated early.
- **Fidelity.** A pixel or behavior difference appears when a template or animation is re-expressed. Mitigation: gallery plus tree
  snapshots per screen, and the rule that a difference is a bug, not a new baseline.
- **Two ways to write things.** Elements like `input type=checkbox` and `f-check-box` could drift. Mitigation: one canonical spelling
  per control in the catalog; the formatter and a lint can prefer it.
- **Dialect creep.** Pressure to add CSS the engine cannot honor. Mitigation: only accept what has a defined Forma mapping and a
  test; reject everything else with a diagnostic, as today.
- **Build-time cost and diagnostics.** More generated XAML means more places for later-stage errors. Mitigation: the source map and
  remapping already cover generated entries; extend tests for each new construct.
- **Chasing browser parity.** Full CSS is endless. Mitigation: the support matrix is the contract, additions need a Forma mapping and a
  test, and the help line on every rejection keeps agents productive at the edges.
- **Layout semantics drift.** Flex and grid lower to Forma containers that are not identical to browser layout. Mitigation: a
  layout-equivalence test suite with side-by-side expectations (documented differences, not silent ones), plus the box overlay.
- **Preview fidelity.** A headless preview could differ from the real game. Mitigation: it runs the same compiled view and style
  engine, and a test compares a preview render with the gallery frame of the same screen.
- **Stale or ignored guidance.** Instructions drift from the tools, or agents never load them. Mitigation: generate the reference from
  the catalog, version-stamp the installed block, run `agent doctor` and a drift check in CI, put the rules in `AGENTS.md` as well as the
  skill, and measure behavior with evaluations instead of trusting the text.
- **Agent environments differ.** Skill formats and MCP support vary. Mitigation: the same content ships as a skill, an `AGENTS.md`
  block and CLI help; MCP is optional because every tool also exists as a command.
- **Over-long instructions.** A big prompt costs attention. Mitigation: a short skill body with on-demand references, and an
  evaluation that compares variants.
- **Keeping XAML first-class.** Every converter change risks touching shared emission code. Mitigation: the converter only produces
  canonical XAML text, the Forma XAML suites and API baseline run in every gate, and no XAML golden changes without review.

## Decision log

- **Authority and criterion.** The user delegated decisions D1 to D8 and set the parameter: the goal of the dialect is to make it
  easier for AI agents to design and validate Forma UIs with the same confidence and capability they have with HTML and CSS, so the
  tooling should be the same as for HTML and CSS. Decisions above follow that parameter; the HTML or CSS convention behind each is
  named in its rationale.
- **D1 supersedes the `>>` recommendation** in the earlier proposal: `::part()` and `:host` are used because they are real CSS. The
  stylesheet-caveats goal that asks the user to choose a combinator syntax is therefore already decided: implement `::part()`,
  lowering to Forma's `>>`, and do not pause for the choice.
- **D5 supersedes the explicit key comment** proposed for tokens in that goal: tokens use their CSS names as keys.
- Later decisions (small mapping details, spellings found during a spike) are added here with the date, the reason and the HTML or
  CSS convention they follow.
- **Web-fidelity pass.** After a review of what an agent with web frontend expertise reaches for, the user asked to include every gap
  found and, where a decision was needed, to take the path that makes it easier for such agents. D9 to D14 and the matching Phase A
  and Phase D items were added on that basis: implement web layout and selector semantics where Forma has a primitive, add
  `data-*` state, an i18n and theme convention, visual effects the renderer can draw, a standalone preview command with fake data, a
  box-model overlay, a support matrix with a tested cookbook, and help text on every rejection.
- **Known inaccuracy recorded.** The current dialect lowers `padding` on a flex container to `Margins` (outer-margin semantics). D9
  fixes it in Phase A; until then views that rely on it are documented in the proof report.
- **Agent onboarding (D15, Phase F).** The user asked that an agent asked to build UI in a Forma project know, without being told, to use
  `.fhtml`, `.fcss` and the tooling (the preview command in particular) to iterate quickly, and suggested installable skills. Decided:
  ship a skill, an `AGENTS.md`/`CLAUDE.md` block, an MCP server and CLI help from one generated source, install them with
  `forma-xaml agent install`, surface them at the point of need, and prove the behavior with agent evaluations.
- **Phase 0 audit refinements.** `ItemsPanelTemplate` (2 files) is the items panel of an `ItemsControl`; D2 covers it as `<ul>`
  with a `style="display: flex"` (the panel follows the list's own display type), so no extra element is added. `OptionButton`,
  `CheckBox` and `HSlider` map to `select`, `input type=checkbox` and `input type=range` (D6). `PromptHintBar`, `OverlaySurfaceView`,
  `StandingsView` and the row views are application controls, registered with `<meta name="f-element">` (D6) or converted into their
  own `.fhtml` views. The probe view (`BoxContainer`) is retired per D7 unless a test depends on it.

### Decision log additions (Phases A and B)
- `::part(name)` lowers to the part's element type taken from the declaring `<template>` (for example `Border.part-chrome`), so
  type-specific properties are valid; with no declaring template it is `Control.part-name`.
- `ul li` and `table tr` selectors lower to template children (`ListBox >> ListBoxItem`, `DataGrid >> DataGridRow`).
- `animation:` accepts a comma list (one storyboard per entry); the formatter keeps `@keyframes` blocks.
- Trace tokens are CSS custom properties named `trace-*`; hex in `.fcss` is `#RRGGBBAA`, so the old `#AARRGGBB` token values were
  rewritten, and the gallery proved the result unchanged.
- `<dialog>` was not used for Trace's two dialogs: they are a backdrop and a panel that are siblings of the screen content in the
  original, and wrapping them would change the tree. They are authored as `f-border` plus a panel. `<dialog>` stays in the dialect.
- A `ColorRect` colored by a class style (`f-color-rect.trace-rule`) shifted a sibling text by 2 px in the gallery, while the same
  color set as an element value did not. Views therefore use `style="-f-Color: resource(token.color)"`; the style-engine difference
  is recorded as a known issue (no baseline changed).
- The unused marker class `settings-row` was dropped from the binding row; `FormaProbeView` was retired (D7).
- New gallery scene `settings-sound` (Settings > Sound) so `forma-xaml preview` can show that page; its three baselines are additions, not
  changes to existing frames. The preview host picks the scene with the longest matching `sources` entry.
- Agent-evaluation findings fixed: the style inspector hid every `var()` setter; `<details>` was expanded by default and its children stayed
  visible when folded in markup; `<progress>` height under 20px was silently 20px; `ui-query` dropped `*`; `trace-ui.py` did not build
  the Desktop worker, so edits looked stale; the preview host hid build errors.
- Phase F evaluation criterion: after four scenarios (report in `plans/agent-eval-report.md`) the skill saved one iteration in one scenario
  and tied in three; the repo's own tools directory and `--help` make the live-app tools discoverable without the skill. The owner
  accepted the result as reported instead of the "fewer iterations in every scenario" criterion.

