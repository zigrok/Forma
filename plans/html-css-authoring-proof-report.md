# HTML and CSS Authoring Proof Report

Companion to [html-css-authoring-frontend-plan.md](html-css-authoring-frontend-plan.md). It records stages 1 to 3: the
spike, the proof screens, and the decision point.

## What was built

- `Forma.Xaml.Compiler/Html`: a strict HTML parser, a strict CSS parser, the dialect tables, and `FormaHtmlConverter`, which
  produces canonical XAML plus a source map. It runs on the build host only (the converter lives in the compiler assembly).
- `Forma.Xaml.Build`: a `ConvertFormaHtml` task and a `FormaHtmlConvert` target. `**/*.fhtml` items convert into the
  intermediate directory and join the normal `FormaXaml` pipeline, so emission, bindings, hot reload and the runtime are the
  XAML ones. Diagnostics from later stages are remapped through `*.fhtmlmap` sidecars to the HTML line.

## Proof screens

Two real Trace Arena screens were rewritten, and the XAML originals were moved to `tests/Trace.Client.MonoGame.Tests/Fhtml`:

| Screen | XAML | `.fhtml` | Equivalence |
| --- | --- | --- | --- |
| Main menu | 51 lines, 1931 bytes | 28 lines, 1643 bytes | state gallery 0 changed frames of 33; structural test; automation-id and naming tests |
| Settings > Controls page | 111 lines, 4975 bytes | 50 lines, 3854 bytes | state gallery 0 changed frames of 33; structural test |

The gallery covers rest, hover, pressed and keyboard-selected frames in en, ja and pt-BR at 100% and 130%, so the rendered
output is pixel-identical within the gallery's tolerance (in practice byte-identical here).

## What was awkward or missing

- Margins on a container, size flags and `Visible/Enabled` stay Forma concepts. `f:Property="value"` passes any Forma property
  through verbatim; the proof screens use it for `Margins` on a group box and `HorizontalSizeFlags="Fill, Expand"`.
- CSS `margin` has no single Forma counterpart, so it is rejected rather than approximated.
- Padding means `Padding` on a decorated element and `Margins` on a flex container. That is the dialect's rule, and a surprise
  for CSS authors.
- Every scroll option is a `data-*` attribute because HTML has no scroll-mode vocabulary.
- Custom properties are substituted at build time. They are not runtime resources, so they cannot change while the game runs.

## Decision (stage 3)

Continue. The proof is faithful (no visible or structural difference), diagnostics carry stable `FHTML` codes with HTML and CSS
locations and are covered by rejection tests, and the sources are 40 to 55 percent shorter by line count with the repeated size
and spacing declarations in one rule. "Measurably easier" was measured as size and as the number of Forma-only concepts an author
must know (property names such as `CustomMinimumSize`, `Separation`, `Margins`, `FocusMode`); authoring time was not measured.
The decision is recorded as a judgment on those two measures and should be revisited if authors report more escape-hatch use than
the proof screens needed.
