# Godot text compatibility

Forma follows Godot's retained-control concepts, but it is not API- or pixel-compatible with
Godot. This document records the text behavior backed by Forma's runtime shaping implementation.

## Backed behavior

- `Label` uses retained layouts for wrapping, trimming, ellipsis, visible ranges, paragraph
  direction, language, OpenType features, tabs, alignment, and character bounds.
- `LineEdit`, `TextEdit`, and `CodeEdit` use layout cluster maps for pointer hit testing, caret
  placement, selection geometry, grapheme movement, and word movement when a `UIFont` is active.
- Unlike Godot, a `LineEdit` whose text starts with a right-to-left strong character right-aligns
  it when it fits, like an HTML input with `dir="auto"`; an explicit `LeftToRight` direction opts out.
- Buttons, menus, tabs, item lists, trees, graph controls, dialogs, tooltips, and rich text resolve
  through `UIFont`; packed `SpriteFont` values remain supported through `SpriteFontAdapter`.
- `RichTextLabel` shapes styled text chunks and maps wrapping and interaction by grapheme rather
  than by UTF-16 code unit.
- Dynamic layouts support Latin shaping, Arabic joining, Hebrew/Latin bidirectional text,
  Devanagari and Thai clusters, CJK line breaking, fallback faces, and malformed UTF-16 handling.
- Logical layout size remains independent of display density. Dynamic glyph pages are rasterized
  for the active density and are device-scoped, bounded, resettable, and disposable.

## Remaining differences

- Forma accepts committed text from the host text-input callback, but it does not expose Godot's
  native IME preedit/composition range and candidate-window integration.
- Rich-text styles are shaped per style span. Joining or ligature substitution across a style
  boundary is not guaranteed.
- Synthetic bold is an offset draw pass; synthetic italic and Godot's complete font variation and
  emboldening policy are not reproduced.
- Color-font tables are treated as outline/fallback input; COLR, CPAL, CBDT, CBLC, and SVG color
  glyph rendering is not implemented.
- Accessibility text providers, platform-native selection services, and Godot's complete theme
  resource lookup hierarchy remain outside the current compatibility contract.
- Rendering is validated for MonoGame DesktopGL and FNA SDL GPU Metal on macOS. Other backend and
  platform combinations require their own package, startup, render, reset, and disposal evidence.

## Compatibility path

Assigning a control's `Font` property keeps existing XNB `SpriteFont` workflows native-free. New
runtime-shaped UI should assign `UIFont`, or set `Theme.FontFamily` and optional `Theme.FontSize` so
unset text controls inherit a dynamic family. The catalog's SpriteFont Compatibility story renders
both paths and labels expected metric and glyph-coverage differences.