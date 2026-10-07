# Agent-Friendly XAML Authoring Plan

## Objective

Make it practical for an AI coding agent to design polished, consistent Forma UI with good UX, and to
verify that it did so, with the same confidence a web developer gets from HTML, CSS, a browser, and
DevTools. Today an agent can compile and test Forma XAML, but it designs largely blind: the feedback
loop is slow and ad hoc, design values are scattered, and several styling gaps force code workarounds
that an agent has to discover the hard way.

This plan owns the authoring feedback loop, design tokens, style-engine reach, an inspection surface, design
lint, and agent-facing design guidance. It does not own a new authoring syntax; the
[HTML and CSS authoring front end plan](html-css-authoring-frontend-plan.md) owns that and depends on
the gallery and token work here as its test bed.

## Evidence

These gaps were observed while redesigning the Trace Arena client UI with Forma, driving it through an
MCP automation bridge, `cua-driver`, screenshots, and `Forma.Testing`.

- Verifying hover and selection meant writing a throwaway script that launched the game, sent keys,
  and captured frames. Pointer hover could not be forced reliably, so it was proven with unit tests
  instead of being seen.
- A change that clipped the last rows of a list was caught only by reading a screenshot.
- Style rules did not reach rows realized by `ListBox`, `DataGrid`, or `ItemsControl`, nor controls
  built in code. Hover, selection, and the shared button template each needed a C# workaround.
- `DynamicResource` is rejected in a style setter, so a shared size token needed `StaticResource`
  and a rebuild to change.
- Colors, spacing, radii, and border widths were hard-coded as hex and literals across many views and
  again in C#, so a button restyle meant editing several places that could drift apart.
- Diagnosing why a rule did not apply was guesswork. There is no way to ask which rules matched a
  control and why a candidate rule did not.
- Accessible names were derived from `x:Name` before visible text, so a button was announced as
  `PlayButton`. This was fixed, but it was found only by dumping the accessibility tree.

## Principles

- Everything an agent needs to judge a screen must be producible by one deterministic command, with no
  window management, timing, or coordinate guessing.
- A design value has one definition. Views reference tokens; they do not repeat literals.
- If a rule can apply to a control, it applies to every control of that kind however it was created.
  Workarounds that depend on how a control was built are defects in the engine, not in the app.
- Checks that a human does by eye should become tests where the data already exists.
- Tooling is build-host or debug-only and never enters a trimmed or ahead-of-time target artifact.

## Workstreams

### 1. State gallery and visual baselines

A deterministic way to render any screen in any state, headless or offscreen.

- A host command such as `--gallery <screen> --state hover=<automation id> --lang ja --scale 130
  --size 1280x720` that builds the screen from fixed fake data, forces pseudo-states (`hover`,
  `pressed`, `focus`, `disabled`, `selected`, keyboard- and gamepad-selected), and writes a PNG.
- A manifest that renders every screen across states, languages, UI scales, and aspect ratios in one
  run.
- Baseline images checked into the repository with a perceptual diff that reports changed regions and
  fails on unreviewed differences. Regenerating baselines is an explicit reviewed step.
- Forced pseudo-state must go through the same style engine as real input, not a parallel path, so a
  gallery frame cannot show a state the game never produces.

Forma already has a Catalog and render smoke tests; this workstream extends them from controls to
whole application screens and makes them drivable by an agent.

### 2. Design tokens

One source of truth for design values, readable from XAML and from code.

- Token categories: color (including state variants), spacing scale, corner radius, border width,
  elevation, typography (size, weight, line height), and motion (duration, easing).
- A single resource dictionary per application defines them; a typed C# reader exposes the same values
  so code never repeats a literal.
- A lint test that fails when a view or code file contains a raw color literal, a spacing literal
  outside the scale, or a font size, other than inside the token definition.
- Resolve the `DynamicResource` limitation in style setters so a token change does not require a
  rebuild during hot reload (see workstream 3).

### 3. Style-engine reach

Make rules apply uniformly, then delete the workarounds.

- Selectors match items realized by items controls, including recycled containers, and apply
  pseudo-state styles to them.
- Controls created in code participate in style matching exactly like XAML-created controls, including
  template application.
- Allow `DynamicResource` in style setters, observing the resource as for any other property.
- Provide an explicit, documented way to scope a theme to a subtree, replacing ad hoc per-control
  overrides.

Each fix needs a focused test that reproduces the original gap, for example a hover rule matching a
`ListBox` row and a code-built button receiving a class style.

### 4. Style inspector

The equivalent of a DevTools Computed pane, exposed to tooling.

- Given a control, report the matched rules in cascade order, the winning value per property and its
  source, the active pseudo-states, applied classes, and the template in use.
- Given a rule or selector, report which controls it matched and, for a chosen control, why it did not
  match (type, class, pseudo-state, combinator, or condition).
- Expose it through the debug automation bridge so an agent can ask "why is this not highlighted".

### 5. Design lint

Automated checks that read the accessibility tree and layout bounds.

- Clipping and overflow: text truncated, content outside its container, rows partly outside a scroll
  viewport without a scroll cue.
- Contrast: foreground against effective background, using WCAG ratios, per state.
- Target size: minimum interactive size for pointer and gamepad.
- Focus: tab order matches reading order, every interactive control reachable, visible focus indication
  distinct from hover.
- Naming: interactive controls have non-empty, human-readable names that are not code identifiers.
- Consistency: controls that share a role share size, padding, and weight; a control that looks like a
  label but is interactive, or the reverse.
- Internationalization: run the layout checks with the longest supported translation and with CJK text.

These run as ordinary tests over compiled views with no window, using the gallery's fake data.

### 6. Agent-facing design guidance

Written for an agent to read before it designs a screen.

- A short design language: spacing scale, hierarchy of button emphasis, panel versus dialog, selection
  and hover conventions, motion rules including reduced motion, gamepad focus rules, and examples of
  good and bad screens.
- A pattern library of annotated reference screens that exist as compiled views, so an agent can copy a
  proven structure rather than invent one.
- A catalog of known engine limits and their supported workarounds, kept current as workstream 3
  removes them.
- A troubleshooting guide mapping symptoms to the inspector and lint queries that diagnose them.

### 7. Live feedback

- Keep Debug hot reload for XAML and tokens, and report invalid markup without disturbing the live
  tree, as today.
- Provide a command that renders the affected screens through the gallery after a change and returns
  the baseline diff, so an agent gets a verdict in one step.

## Sequencing

1. State gallery with forced states and baseline diff. It changes what an agent can verify more than
   anything else and is the test bed for every later item.
2. Design tokens and the lint that enforces them.
3. Style-engine reach fixes, removing application workarounds as each lands.
4. Style inspector in the debug bridge.
5. Design lint over the tree and bounds, starting with clipping and contrast.
6. Agent-facing design guidance and pattern library, written as the above stabilize.

## Verification

- Gallery: the same screen and state produces a byte-stable image across runs on one machine, and the
  diff tool reports no change for an unmodified screen.
- Tokens: the lint fails on an injected literal and passes on the clean tree.
- Reach: each reproduced gap has a test that fails before the fix and passes after, and the
  corresponding application workaround is removed with tests still green.
- Inspector: for a known hover rule on a list row, it names the rule, the control, and the winning value;
  for a non-matching control it names the reason.
- Lint: seeded defects (clipped row, low-contrast text, unnamed button, undersized target) are each
  detected.
- All tooling is excluded from release, NativeAOT, and trimmed artifacts, and a build check proves it.

## Risks and Boundaries

- Image baselines are sensitive to fonts, rasterization, and GPU differences. Pin fonts, render
  offscreen on a defined backend, compare perceptually with tolerances, and treat baselines as
  per-platform where needed.
- Forcing states must not become a second implementation of hover or selection. It should drive the
  real pseudo-state mechanism.
- Style-engine changes affect every consumer. Land them behind focused tests, review the public API
  baseline, and keep behavior that callers rely on unless a change is documented.
- Lint rules that are too strict will be ignored. Start with the checks that catch real defects and
  make each one individually suppressible with a recorded reason.
- This plan improves verification and consistency. It does not replace human judgment about whether a
  design feels good; reference screens and reviewed screenshots remain the way to set taste.
