# A design system in `.fcss`

A web design system such as Bootstrap is one stylesheet with tokens, components and utility classes that every page links.
Forma's dialect has the same shape: a shared `.fcss` file linked from your views. It is not Bootstrap compatible (the dialect rejects
`em`, `%`, `calc()`, `box-shadow`, gradients, `position`, floats and pseudo-elements), so a Forma design system is written in the
dialect. This page is the starting point.

The complete sheet is [`docs/examples/design-system.fcss`](examples/design-system.fcss); a test converts it and styles a component
view with it, so it stays valid.

## Tokens

```css
:root {
  --surface: #14213D;
  --accent: #47E4FF;
  --text: #E0F8FF;
  --space-2: 8px;
}
```

Colors become a brush and a `Color`, numbers become `Single` resources. Change the token and every control styled with
`var(--accent)` updates live. Define them once in the sheet; views never repeat a color or spacing literal, and a lint rejects raw
literals in `.fhtml` views.

## Components

| Class | What it styles |
| --- | --- |
| `label.text`, `label.text-muted`, `label.title` | text colors and the title size and weight |
| `f-border.card`, `f-border.card-raised` | a bordered, padded surface |
| `button.btn`, `button.btn-primary` | minimum size, rest/hover weight, disabled opacity |
| `f-hbox.gap-1` ... `f-vbox.gap-4` | spacing utilities: `gap` on a row or column container |

```html
<link rel="stylesheet" href="design-system.fcss">
<div style="display: flex; flex-direction: column" class="gap-3">
  <f-border class="card"><span class="title">Settings</span></f-border>
  <button class="btn btn-primary" onclick="OnSave">Save</button>
</div>
```

Note the utility classes are written for the flex containers the converter produces (`f-hbox` and `f-vbox` select the generated
`HBoxContainer` and `VBoxContainer`).

## Extending it

- Add a class rule per component state (`:hover`, `:active`, `:disabled`, `:focus`, `:checked`).
- Put input-dependent rules in `@media (input-modality: pointer|keyboard|gamepad|touch)`.
- Anything the dialect cannot express (control templates, `>>` template-child selectors) stays XAML next to the sheet, reached with
  `-f-Template: {StaticResource Key}` and `resource(Key)`; Trace Arena's theme (`theme.fcss` plus the XAML kept in
  `TraceRootView.fhtml`) is a worked example.
- Check the result with the gallery, the style inspector and the design lints described in the
  [agent design guide](agent-design-guide.md).
