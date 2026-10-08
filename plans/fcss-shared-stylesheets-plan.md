# Shared Stylesheets (`.fcss`) for the HTML and CSS Dialect

## Objective

Let many `.fhtml` views share one stylesheet and one set of design tokens, the way a web design system (Bootstrap is the
reference experience) shares a CSS file across pages. Today a `.fhtml` view can only use inline `style` attributes and its own
`<style>` blocks, so a shared theme has to be written in XAML (`TraceRootView.xaml` in Trace Arena) and the views only use its
classes. This plan adds shared stylesheets written in the dialect itself and proves them by moving Trace Arena's theme into one.

This plan extends the [HTML and CSS authoring front end plan](html-css-authoring-frontend-plan.md), which already names `.fcss`
files as a source type. It reuses the tokens, gallery, style inspector and lints from the
[agent-friendly XAML authoring plan](agent-friendly-xaml-authoring-plan.md).

## Non-Goals

- Not Bootstrap compatibility. Bootstrap's CSS uses constructs the dialect rejects on purpose (`em`, `%`, `calc()`, `box-shadow`,
  gradients, `position`, floats, `margin`, pseudo-elements). A Forma design system is written in the dialect, not imported.
- No CSS preprocessor (Sass, Less), `@import` of arbitrary URLs, or runtime stylesheet loading.
- No change to the runtime. Output is the same Forma `Style` and `ResourceDictionary` objects XAML produces.
- No requirement that a project pick one format.

## Today

- A view gets inline `style` (lowered to attributes) and `<style>` rules (lowered to `Style` entries in that view's resources).
- `:root { --name: value }` and `var(--name)` work inside one file and are substituted when the file is converted.
- Shared styling is a XAML resource dictionary attached at a root view. Rules attached there reach rows and presented content by
  default, and `{DynamicResource}` in a style setter is live.

## Design

### 1. Linking

```html
<link rel="stylesheet" href="theme.fcss">
```

`<link>` is allowed in the head area before the root element, like `<meta>`. `href` is project-relative or relative to the
file; anything else (URLs, absolute paths, missing files) is a diagnostic. Several links are allowed and apply in order.
`rel` other than `stylesheet` is rejected.

### 2. The `.fcss` file

A stylesheet contains the same strict CSS the dialect already accepts in a `<style>` block (rules, `:root` custom properties,
`@media` on input modality or viewport width, `transition`) and nothing else. It gets the same parser, the same validation and the
same `FHTML2xxx` diagnostics, with the `.fcss` path and line in each location.

### 3. Lowering: one shared dictionary, not copies

Inlining a stylesheet into every view would duplicate styles and leave live tokens impossible to share. Instead:

1. Each distinct `.fcss` converts once to a XAML `ResourceDictionary` file in the intermediate directory (`theme.fcss.xaml`) with a
   source map sidecar.
2. A view that links it gets that dictionary merged into its root resources, so its `StaticResource` and style lookups see it.
3. An application that wants the theme on every screen links it from its root view (as `TraceRootView` does today), and the
   style engine's default reach applies it to descendants, rows and presented content without per-view calls.

Rules in the view's own `<style>` come after linked sheets, so a view can override the theme the way a page overrides a CSS file.

### 4. Tokens

- `:root { --accent: #47E4FF }` in a `.fcss` lowers to named resources. A `Color` or `Brush` is chosen from the property that
  uses it, as the token section of the agent-friendly plan already does by hand.
- `var(--accent)` lowers to `{DynamicResource}` in style setters (now live) and to `{StaticResource}` where the target cannot
  observe changes. Changing a token at runtime then updates styled controls without a re-apply.
- Custom properties are not substituted at conversion time any more; the documented difference from browsers (build-time
  substitution) goes away for linked sheets, and a diagnostic names any use the engine cannot make live.
- A token defined in a view's own `<style>` shadows the linked one for that view.

### 5. Utility and component classes

A shared sheet is where the reusable vocabulary lives: spacing and gap utilities (`.gap-2`, `.p-3`), color and surface classes,
component classes (`.btn`, `.btn-primary`, `.card`). These are ordinary class rules. The dialect needs no new syntax for them,
but two things are needed to keep them honest:

- A lint that flags a view using a class no linked sheet or local rule defines (the CSS equivalent of an unknown resource).
- A lint, extending the existing token lint, that flags raw color and spacing literals in `.fhtml` views outside the token
  definition, so views use the shared tokens and classes.

## Tooling parity

- Formatter: `forma-xaml format` formats `.fcss` with the same deterministic rules as a `<style>` block.
- Hot reload (Debug): changing a `.fcss` reconverts its dictionary and reloads every view that links it. Invalid input reports
  diagnostics at the `.fcss` line and leaves the live tree intact.
- Source maps and diagnostics: later-stage errors in a generated theme dictionary map back to the `.fcss` line.
- Build incrementality: a changed sheet rebuilds only the views that link it.
- Artifact contract: release and NativeAOT artifacts contain no `.fcss`, no `.fcss.xaml` source and no converter, and the existing
  artifact verifier and `tools/verify-no-tooling.sh` cover the new extensions.
- Inspector: `StyleInspector` reports the source of a winning value as the `.fcss` rule and line when a source map exists.

## Staging

1. **Spec and spike.** Define `<link>` and the `.fcss` file, add the parser entry point and golden tests (a sheet plus two views in,
   dictionary plus views out). Prove the pipeline compiles and a style from the sheet applies to a control in both views.
2. **Tokens.** Lower `:root` custom properties to typed resources and `var()` to dynamic or static resources. Test that changing a
   token at runtime updates already-styled controls, and that a view-local token shadows the shared one.
3. **Proof: move Trace Arena's theme.** Port the shared styles and tokens from `UI/Resources/TraceRootView.xaml` into
   `theme.fcss`, link it from the root view, keep the two `.fhtml` screens, and keep the gallery at 0 changed frames of 33. Record
   what the dialect could not express (for example `ColorRect.Color`, template parts and `>>` selectors) and what stays in XAML.
4. **Decision point.** Continue only if the port is faithful, the diagnostics are usable and the theme is measurably easier to change
   than the XAML dictionary. Otherwise keep `.fcss` as a documented experiment.
5. **Parity.** Formatter, hot reload, incremental builds, source maps, inspector source, the undefined-class and raw-literal lints,
   artifact checks and a catalog story.
6. **Document.** Add a section to the dialect guide, a "design system in `.fcss`" page with a small component set (buttons, card,
   spacing utilities) as the Bootstrap-like starting point, and update the agent design guide.

## Verification

- Golden tests: each linked-sheet feature (link order, view override, token shadowing, media rule, transition) has input and
  expected output, and the expected XAML is itself a valid, tested view.
- Equivalence: with the Trace theme in `theme.fcss`, `make gallery` shows 0 changed frames of 33, the accessibility trees match,
  and the existing token and design lints pass.
- Rejection: every rejected construct in a `.fcss` has a test asserting code, message and location, including a missing link target
  and an unsupported `@import`.
- Live tokens: a test changes a token resource at runtime and asserts a styled control updates.
- Hot reload: editing the sheet updates every linking view; invalid input leaves the tree intact.
- Artifact contract: a release publish contains no `.fcss` or generated theme source and the artifact verifier passes.
- Mutation proofs: a raw color in a view, an undefined class, and a broken `.fcss` each fail their check, then revert.

## Risks

- Expectation gap: users expect Bootstrap or full CSS cascade behavior. Mitigate with strict rejection and a published list of
  differences, as for the base dialect.
- Cascade semantics: Forma orders by specificity and declaration order within a style set, and merged dictionaries add a layer.
  Define and test the order of linked sheets, the view's own rules and root-attached styles before widening.
- Live tokens across property types: a token used as both a `Color` and a `Brush` needs two resources or a typed conversion. Decide
  in stage 2 and cover it with tests.
- Theme parity: parts of Trace's theme use template parts, `>>` selectors and `ColorRect.Color`, which have no CSS counterpart.
  Stage 3 records these; the answer may be a hybrid where templates stay in XAML and tokens and class rules live in `.fcss`.
- Build graph: a shared sheet makes every linking view depend on it. Incremental builds and hot reload must track the edge.
- Effort is uncertain until stage 1. The decision point exists so the work stops if the port is not faithful.
