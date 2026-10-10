# The HTML and CSS authoring dialect

Forma views can be written in a strict subset of HTML and CSS (`.fhtml` files and `.fcss` stylesheets) instead of XAML; an application can be written entirely in the dialect (Trace Arena has no XAML source). A build-time converter turns
each file into canonical XAML, and the existing XAML pipeline does everything else, so the runtime, generated code, trimming and
NativeAOT guarantees are exactly the XAML ones. XAML stays the reference format and a project can mix both.

This is a Forma dialect that borrows HTML and CSS notation. It is not a browser: no JavaScript, no DOM at runtime, no web view,
and **a construct outside the dialect fails the build with a diagnostic, it is never approximated.**

## Using it

Put `.fhtml` files in a project that references `Forma.Xaml.Build`. Every `**/*.fhtml` item converts into the intermediate
directory (`*.fhtml.xaml` plus a `*.fhtmlmap` source map) and joins the `FormaXaml` items. A view declares its code-behind class and
data type on the root element:

```html
<meta name="f-namespace" content="local=clr-namespace:MyApp.ViewModels">
<style>
  button.primary { min-width: 320px; min-height: 46px; }
  button.primary:hover { font-weight: bold; }
</style>
<div data-class="MyApp.MenuView" data-type="local:MenuViewModel">
  <div style="border-width: 1px; padding: 16px 18px; border-radius: 3px">
    <div style="display: flex; flex-direction: column; gap: 10px">
      <button id="PlayButton" class="primary" tabindex="-1" bind:text="PlayText" onclick="OnPlayPressed"></button>
    </div>
  </div>
</div>
```

## Elements

Every row below is a catalog entry (`FormaHtmlDialect.Catalog`) that a test converts and compiles, so the table matches the converter.

| HTML | Forma |
| --- | --- |
| `div`, `section`, `nav`, `main`, `header`, `footer` | `Container`; `HBoxContainer` / `VBoxContainer` with `display: flex`; `Border` when it has padding, border or background; `GridPanel` with `display: grid` |
| `span`, `p`, `label`, `h1`-`h6` | `Label` (text content becomes `Text`) |
| `button` | `Button` |
| `input type=text`, `textarea` | `LineEdit`, `TextEdit` |
| `input type=checkbox`, `input type=range` | `CheckBox`, `HSlider` |
| `select` | `OptionButton` |
| `ul`, `ol`, `li` | `VBoxContainer` of `Container` items |
| `table`, `tr`, `td`, `th` | `GridPanel` (columns from the widest row; `colspan` supported) |
| `f-border`, `f-scroll`, `f-group-box` | `Border`, `ScrollContainer`, `GroupBox` |
| `f-control type="prefix:Type"` | any control, with `<meta name="f-namespace" content="prefix=clr-namespace:...">` |

## Attributes

`id` → `x:Name`; `class` → `Classes`; `hidden`, `disabled` → `Visible`/`Enabled`; `title` → `TooltipText`; `aria-label` →
`AccessibilityLabel`; `data-automation-id` → `AutomationId`; `tabindex` (`-1` or `0`) → `FocusMode`; `data-class`, `data-type` →
`x:Class`, `x:DataType`; `onclick`, `onchange`, `onfocus`, `onblur` name a code-behind handler, never an expression;
`bind:text="Path; mode=TwoWay"` → `{Binding Path, Mode=TwoWay}` (any property, `bind:visible`, `bind:enabled`, `bind:title`).
`f:Property="value"` sets any Forma property verbatim; the XAML compiler validates it. `f-scroll` takes `data-follow-focus`,
`data-horizontal`, `data-vertical` and `data-step-buttons`.

## CSS

- Selectors: type (`button`, `span`, `p`, `label`, `h1`-`h3`, `input`, `div`, `f-border`, `f-group-box`, `f-scroll`), class, id,
  descendant and child (`>`) combinators, `:hover`, `:active` (pressed), `:focus`, `:disabled`, `:checked`, `:selected`, `:not()`.
  They lower to Forma `Style` entries. Specificity and order are Forma's, not the browser's.
- Properties: `display` (`flex`, `grid`, `block`, `none`), `flex-direction`, `gap`, `flex-grow`, `align-self`, `min-width`,
  `min-height`, `padding`, `border-width`, `border-radius`, `border-color`, `background-color`, `color`, `font-size`,
  `font-weight`, `opacity`, `pointer-events` (`none` → `MouseFilter="Ignore"`), `text-align`, `vertical-align`,
  `grid-template-columns`, `grid-template-rows`, `grid-column`, `grid-row`, `transition`.
- `@media (input-modality: pointer|keyboard|gamepad|touch)`, `(min-width: Npx)` and `(max-width: Npx)` → `AdaptiveCondition`.
- `transition: opacity 150ms` → `FloatTransition`/`ColorTransition` for `opacity`, `background-color`, `border-color`, `color`, `font-size`.
- `:root { --name: value }` and `var(--name)`.
- Units: `px`, unitless, and `rem` (16 px). Colors: `#RRGGBB` and `#RRGGBBAA`.
- Inline `style` lowers to attributes on that element; rules in a `<style>` block lower to styles.

## Shared stylesheets (`.fcss`)

A stylesheet shared by many views is a `.fcss` file, linked before the root element:

```html
<link rel="stylesheet" href="theme.fcss">
```

- `href` is relative to the linking file (a leading `/` is the project root). It must end in `.fcss` and stay inside the project;
  URLs, plain `.css` files and any `rel` other than `stylesheet` fail with `FHTML1004`, and a missing file with `FHTML1006`.
- A `.fcss` holds the same strict CSS a `<style>` block does (rules, `:root` tokens, `@media`, `transition`) and gets the same
  diagnostics, located at the `.fcss` line. Each sheet is read, parsed and converted once per build; the converted entries are placed
  in the root resources of every linking view, ahead of the view's own `<style>`, so a view overrides the theme like a page overrides a
  CSS file. A theme linked from the application root view reaches every screen through the style engine's default reach.
- Tokens: `:root { --accent: #47E4FF; --gap: 12px }` lowers to typed resources. A color is both a `SolidColorBrush` (`accent`)
  and a `Color` (`accent.color`); a number or single length is a `Single` (`gap`). `var(--accent)` lowers to a live
  `{DynamicResource}` for `color`, `background-color`, `border-color`, `font-size`, `opacity` and `gap`, so changing the resource at
  runtime updates styled controls. Elsewhere (for example `padding`, `min-width`) the value is substituted at build time.
  A token in the view's own `<style>` shadows the shared one for that view.
- Bridges to XAML: `resource(Trace.Color.Text)` references a resource defined elsewhere (live), `-f-Property: value` sets any Forma
  property verbatim (`-f-Template: {StaticResource MyTemplate}`), and `<f-resources>` embeds raw XAML (templates, storyboards,
  `>>` selector styles) in a view. The dialect has no template-child combinator and no selector types for every control yet, so a theme
  is typically a hybrid: class styles and tokens in `.fcss`, templates and template-part styles in `<f-resources>`.
- Tooling: `forma-xaml format` formats `.fcss`; Debug hot reload of a changed `.fcss` reconverts every view that links it;
  `StyleInspector` reports a winning value's source as `Button.primary @ theme.fcss:12` in Debug builds (release artifacts omit
  the location); a changed sheet regenerates only the views that link it.
- See [a design system in `.fcss`](design-system-fcss.md) for a small component set.

## Differences from browsers

- `padding` is `Padding` on a decorated element and `Margins` on a flex container. `margin` has no single Forma counterpart and
  is rejected; use `gap`, `padding` or `f:Margins`.
- Normal flow, margin collapsing and intrinsic sizing do not exist. Only the flex and grid forms with a defined mapping are accepted.
- A decorated `div` becomes a `Border`, which holds one child; wrap several children in a flex container.
- Custom properties lower to typed resources and live `{DynamicResource}` values for color, font size, opacity and gap (see shared
  stylesheets); in other properties they are substituted when the file is converted.
- Styles and attribute order follow Forma's rules: a rule applies to a control that matches at that moment, and local attributes win.
- Rejected with a stable code: `<script>`, inline event expressions, `position`, `float`, `calc()`, `em`/`%` units, gradients,
  `box-shadow`, `filter`, `backdrop-filter`, `!important`, unknown elements, attributes and properties.

## Diagnostics

Codes `FHTML1xxx` (HTML) and `FHTML2xxx` (CSS) carry a project-relative path, line and column in the original file. Errors raised
by later stages (names, bindings, resources) are remapped through the source map to the `.fhtml` line.

## Tooling

- Formatter: `forma-xaml format [--check] <dir|file.fhtml>`; deterministic and idempotent, comments preserved.
- Hot reload (Debug): `FormaXamlHotReloadService.RequestHtmlReloadAsync` converts the changed `.fhtml`, rewrites the generated XAML
  and reloads the view, preserving presenter and data context. Invalid input reports diagnostics at the HTML location and leaves
  the live tree intact. The Debug development output carries `.fhtml` sources and maps; release and NativeAOT artifacts never do.
- Release and NativeAOT: the converter lives in `Forma.Xaml.Compiler`, a build-host assembly. Published artifacts contain no
  parser, converter, `.fhtml` or `.fhtmlmap` file; the artifact verifier fails if they appear.
- Accessibility and testing: a converted view has the same tree, names and `AutomationId` values as its XAML original.

## The complete dialect

The generated pages are authoritative and always match the converter (a test compares them with the generator):

- [Support matrix](html-css-support-matrix.md): every element, property, selector, at-rule and unit, supported or rejected with the
  alternative. [Reference](html-css-reference.md): each construct with an example and the Forma type it becomes.
  [Cookbook](html-css-cookbook.md): tested recipes.
- Beyond the basics above, the dialect has: control templates (`<template for>`, `<slot>`, `part`, `::part()`), data templates for
  lists and grids (`<ul bind:items>`, `<table bind:items>`), ARIA tabs, `<dialog>`, `@keyframes`/`animation`, `prefers-reduced-motion`
  and `prefers-color-scheme`, CSS custom properties as the resource keys, theme overrides (`:root[data-theme]`), `data-*` state
  selectors, structural pseudo-classes, `data-i18n`, `dir`, margin/padding with web box semantics, and visual effects the renderer can
  draw (shadows, gradients, transforms).
- Tools: `forma-xaml validate|format|preview|docs|agent|mcp|new`; see [Forma for AI agents](agents.md) and `llms.txt`.

