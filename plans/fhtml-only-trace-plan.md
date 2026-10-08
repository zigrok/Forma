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

## Inventory (to be audited in Phase 0)

Today Trace has 20 view XAML files plus the root view that is already `.fhtml`, and three `.fhtml` sources (main menu, settings
controls page, root). The XAML that remains, with the constructs that are not yet expressible in the dialect:

| Area | Files (approx.) | Constructs still needed |
| --- | --- | --- |
| Menus, pause, play menu, match over, scoreboard, standings, quick match, local game | about 8 | containers, labels, buttons, bindings: expressible today; some `ScrollContainer`, `LineEdit`, `ColorRect` |
| Settings: view, general page, sound page, binding row | 4 | `TabContainer`, `CheckBox`, `LineEdit`, spin box custom control, `ScrollContainer`, `GroupBox`, row view as a data template |
| Map editor and map browser | 4 | `ListBox` with data-template rows, dialogs, `ColorRect` |
| Server browser | 1 | `DataGrid` with templated columns, dialogs |
| Round settings, debug console, probe | 3 | rows as data templates, probe view with a trigger |
| Root view | 1 (fhtml) | control templates with template parts, storyboards, tokens, styles for `ColorRect`, `CheckBox`, `ListBox`, `DataGrid`, `TabContainer`, field chrome |

Phase 0 produces the exact per-file list of constructs (a script over the XAML plus a hand check), and this table is replaced by it.

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

## Phases

### Phase 0: audit and decisions
- Script and hand-audit every remaining XAML file; replace the inventory table with the exact constructs per file.
- Re-read D1 to D8 against what the audit finds and record any amendment in the decision log. The decisions are already made; the
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
| Snapshot and golden tests | accessibility-tree snapshots per screen and golden conversions of the shipped views (Phase C) |

Acceptance: each row has a working command or file, a test, and an entry in the agent design guide; the guide's troubleshooting table
maps a symptom to the query that diagnoses it.

## Verification (every phase, from runs after the last change)

- `dotnet build Trace.slnx`: 0 errors and 0 warnings; `dotnet test Trace.slnx`: all passed with new tests for the phase.
- Forma: `tests/Forma.Tests`, `tests/Forma.Xaml.Compiler.Tests`, `tests/Forma.Xaml.HotReload.Tests`, and
  `bash scripts/check-release-api.sh` reporting additions only. XAML golden tests are unchanged.
- `make format-xaml-check` (covers `.fhtml` and `.fcss`), `make verify-source-dependencies`, `make gallery` (33 of 33).
- `tools/lint-mutation-proof.sh`, `tools/verify-no-tooling.sh`, `tools/fhtml-diagnostic-proof.sh`, and a real Release publish passing
  `tools/verify-client-artifact.sh`.
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
