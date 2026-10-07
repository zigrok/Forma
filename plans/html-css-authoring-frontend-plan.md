# HTML and CSS Authoring Front End Plan

## Objective

Let UI authors, and AI coding agents in particular, write Forma views in a strict subset of HTML and CSS
as an alternative to XAML. The HTML front end is a build-time source converter that produces the same
compiled result as XAML: the same lowered representation, the same generated IL, the same runtime
controls, and the same trimming and ahead-of-time guarantees. XAML remains the reference format.

The motivation is authoring accuracy. Authors and models know HTML and CSS far better than Forma XAML,
and a familiar notation lowers the rate of mistakes in selectors, resources, and layout. It does not
change how a view behaves at runtime.

This plan owns the HTML and CSS dialect, the converter, its diagnostics, tooling parity, and the
migration proof. The [agent-friendly XAML authoring plan](agent-friendly-xaml-authoring-plan.md) owns
the gallery, tokens, and style-engine work that this plan uses as its test bed. The
[NativeAOT and console readiness plan](nativeaot-console-readiness-plan.md) continues to own the
production-artifact contract that this plan must not weaken.

## Non-Goals

- No JavaScript. Behavior stays in C# code-behind and view models. Scripting would require a runtime that
  conflicts with trimming, ahead-of-time compilation, and console targets.
- No browser engine, DOM at runtime, or web view. Output is compiled Forma controls.
- No promise of browser fidelity. This is a Forma dialect that borrows HTML and CSS notation. A page
  that is valid HTML but outside the dialect is rejected, not approximated.
- No replacement of XAML and no requirement that a project choose one format.

## Why It Is Feasible

The XAML compiler is already staged: parse a source document, lower it to `FormaLoweredDocument`, then
emit through Cecil or System.Reflection.Emit. The runtime and controls know nothing about XML. A new
front end only has to produce an equivalent lowered document. The lowest-risk first version converts HTML
and CSS to canonical XAML text and feeds the existing pipeline, so emission, diagnostics codes,
hot reload, and runtime behavior are reused unchanged.

Forma selectors are already class- and pseudo-state-based and close to CSS (`Button.primary:hover`),
which makes the style mapping direct for the common cases.

Parsing runs on the build host only, as XamlX and Mono.Cecil do today. A build-time HTML or CSS parser
dependency never ships in the target artifact. The artifact check that excludes compiler and source
documents from release builds must also cover the new front end.

## Dialect

### Elements

Standard elements map to Forma containers and controls:

| HTML | Forma |
|---|---|
| `div`, `section`, `nav`, `main` | container, laid out by its CSS `display` |
| `span`, `p`, `h1`-`h6`, `label` | `Label` / text with a style class |
| `button` | `Button` |
| `input type=text`, `textarea` | `LineEdit` / multiline editor |
| `input type=checkbox`, `input type=range` | `CheckBox`, `Slider` |
| `select` | `OptionButton` |
| `img`, inline `svg` | `Image`, SVG image |
| `ul`/`ol`/`li` | items control |
| `table` | `DataGrid` for data, grid panel for layout |

Controls with no HTML counterpart are custom elements with a `f-` prefix, for example `f-tab-container`,
`f-spin-box`, `f-scroll`, `f-group-box`. The mapping table is data, versioned with the dialect, and
extensible for application-defined controls.

### Attributes

- `class`, `id` map to `Classes` and `x:Name`.
- `hidden`, `disabled` map to visibility and enabled state.
- `aria-label`, `role`, and `data-automation-id` map to `AccessibilityLabel`, the accessibility role, and
  `AutomationId`.
- `onclick` and similar name a code-behind handler, never an expression: `onclick="OnPlayPressed"`.
- `bind:text="PlayText"`, `bind:visible="IsOpen"`, with an optional mode, map to typed bindings and
  require a declared data type, as `x:DataType` does.
- `data-class` names the code-behind partial class, as `x:Class` does.

### CSS

A restricted subset, parsed at build time:

- Selectors: type, class, id, descendant and child combinators, the supported pseudo-classes
  (`:hover`, `:pressed`, `:focus`, `:disabled`, `:checked`, `:selected`), and `:not()`. They lower to
  Forma selectors and `Style` entries. Specificity and ordering follow Forma's documented rules, which
  the dialect states explicitly where they differ from browsers.
- Box and visual properties that Forma can render: `padding`, `margin`, `border`, `border-radius`,
  `background-color`, `color`, `opacity`, `font-size`, `font-weight`, `font-family`, `text-align`,
  `width`/`height` and min/max forms.
- Layout: `display: flex` (row and column, `gap`, `justify-content`, `align-items`, `flex-grow`) lowering
  to box containers and size flags; `display: grid` lowering to the grid panel; `overflow: auto|scroll`
  lowering to a scroll container.
- Custom properties `--name: value` lower to static resources, and `var(--name)` lowers to
  `StaticResource`. They are static, with the same limit as XAML tokens until the style-engine work
  allows dynamic resources in setters.
- `transition` lowers to style transitions for the properties Forma can animate.
- `@media` is limited to Forma's adaptive conditions, such as input modality.

### Units

`px` and unitless numbers map to logical pixels at Forma's design resolution. `rem` maps to the theme
body size. Percentages and `fr` are supported where a layout panel has a matching concept. Other units
are rejected.

### Rejected Constructs

Anything outside the dialect fails the build with a stable diagnostic and a source location in the HTML or
CSS file. The converter never ignores an unknown property. This includes `position: absolute` and
`fixed` unless and until a defined overlay mapping exists, floats, `calc()`, `em`, `filter`, `backdrop`
effects, gradients and shadows until the renderer supports them, `<script>`, inline event expressions,
and unknown elements or attributes. A "looks like HTML but behaves differently" outcome is worse than no
support, so strictness is the main safety property of the design.

## Architecture

1. Parse: an HTML parser and a CSS parser produce syntax trees with source positions.
2. Validate: check every element, attribute, property, selector, and unit against the dialect tables and
   report diagnostics.
3. Convert: build canonical XAML text, preserving a source map from each generated node back to its HTML
   or CSS origin.
4. Compile: hand the canonical XAML to the existing compiler. Diagnostics from later stages are mapped
   back through the source map so they point at the HTML, not the generated XAML.
5. Emit: unchanged.

A later, optional step can lower directly to the intermediate representation, removing the text
intermediate, once the dialect is stable.

Source files use `.fhtml` and `.fcss` or a single `.fhtml` with a `<style>` block, selected by the
build item type, so the front end is opt-in per file and never guesses.

## Tooling Parity

The front end is not done until these match XAML:

- Diagnostics: stable codes in a new range, project-relative paths, line and column in the original file.
- Hot reload: Debug replacement of subtrees from changed `.fhtml`, preserving presenter and data
  context state, and leaving the live tree intact on invalid input.
- Formatting: a deterministic formatter and a check target, as `format-xaml` provides.
- Accessibility and testing: identical trees and `AutomationId` behavior, so `Forma.Testing` and
  agent tooling work unchanged.
- Documentation and a catalog story demonstrating each mapped element and property.
- Public API review: the converter is build tooling; any runtime API it needs is reviewed against the API
  baseline, and the goal is none.

## Staging

1. Specification spike: write the dialect tables for a minimal subset (box containers, buttons, labels,
   class and pseudo-state styles, flex row and column, bindings) and golden tests of HTML and CSS in,
   canonical XAML out. Prove the pipeline compiles and renders.
2. Proof screen: rewrite one real, small screen in the dialect and compare its rendered output with the
   existing XAML version through the state gallery from the agent-friendly authoring plan. Record what
   was awkward, what was missing, and how long each took.
3. Decision point: continue only if the proof is faithful, diagnostics are usable, and authoring is
   measurably easier or less error-prone. Otherwise keep the work as a documented experiment.
4. Widen the subset (grid, scroll, forms, lists, tables, transitions), add source maps, hot reload, the
   formatter, and the checks above.
5. Document the dialect and its differences from browsers, and add the front end to the contributor
   architecture map.

## Verification

- Golden conversion tests: each dialect feature has an input and an expected canonical XAML, and the
  expected XAML is itself a valid, tested view.
- Equivalence: for the proof screen, the compiled tree and a rendered frame match the XAML original within
  the gallery's tolerance, and the accessibility trees are identical.
- Rejection: every rejected construct has a test asserting the diagnostic code, message, and location.
- Source maps: an error in a later stage points at the original HTML or CSS line.
- Artifact contract: a release and NativeAOT publish of an application using the dialect contains no
  parser, converter, or source documents, and the existing artifact verifier passes.
- Hot reload: changing a style in `.fhtml` updates the live tree, and invalid input leaves it intact.

## Risks

- Expectation gap: users assume browser behavior. Mitigate with strict rejection, a documented list of
  differences, and diagnostics that explain the nearest supported alternative.
- Layout semantics: normal flow, margin collapsing, and intrinsic sizing differ from box containers.
  Support only the flex and grid forms with defined mappings and reject the rest.
- Two formats double the surface to document, test, and keep in parity. Keep conversion to canonical XAML
  so there is one runtime path and one set of control tests, and gate widening on the decision point.
- Parser dependencies and their licenses. Choose permissively licensed build-time libraries and record
  them in third-party notices, since they are build-host only.
- Maintenance of the dialect tables as controls are added. Generate the control mapping from control
  metadata where possible and fail the build on an unmapped public control that the dialect claims to
  support.
- Effort is uncertain until the spike. A first slice is likely days; a faithful subset with tooling
  parity is likely weeks. The decision point exists to avoid committing before the proof.
