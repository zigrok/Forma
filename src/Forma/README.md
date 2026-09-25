# Forma

Forma is a retained-mode UI layer for MonoGame inspired by Godot's `Control` tree.

```csharp
using Forma;

var ui = new UIContext();
var root = new VBoxContainer { Size = new Vector2(800, 480) };
root.AddChild(new Button { Text = "Save", CustomMinimumSize = new Vector2(120, 32) });
ui.Add(root);

// Game.Update
ui.ViewportSize = new Vector2(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
ui.Update(gameTime);

// Game.Draw, after clearing the backbuffer
ui.Draw(GraphicsDevice);
```

For a normal `Game`, add `new UIComponent(this, ui)` to `Game.Components`. It forwards mouse/keyboard state, renders the control tree, and forwards `GameWindow.TextInput` into the focused `LineEdit` or `TextEdit`.

`LineEdit`, `TextEdit`, and `RichTextLabel` copy, cut, and paste commands use the runtime clipboard
through `UIContext.Clipboard`, including keyboard shortcuts and context-menu actions. Forma uses
FNA's SDL clipboard APIs or MonoGame DesktopGL's bundled SDL2 by default. Assign a custom
`IClipboard` implementation to the context for another platform or deterministic tests.

The subsystem uses logical pixels, a `Control` tree, anchors plus offsets, minimum sizes, box/grid/flow layout, focus traversal, pointer capture, delayed `TooltipText`, and a deterministic `Theme`. Rendering is SpriteBatch-based and does not require a content pipeline asset unless a control displays text or a texture. Set `UIContext.TooltipFont` to the font used by the rest of the interface when tooltips should be rendered.

Text can use an offline `SpriteFont` through existing `Font` properties or a runtime-loaded
`DynamicUIFont` through parallel `UIFont` properties. Dynamic layout, fallback, DPI behavior,
deployment, ownership, migration, and rollback are documented in
[Dynamic Text](../../docs/dynamic-text.md).

`Label.EffectiveUIFont` exposes the currently resolved explicit/family/theme font.
`UIFont.WithSize(size)` derives a font using the same face/resources and fails
explicitly for fonts that cannot resize; neither API uses reflection.
`TextBlock.AlignInlineBaselines = true` aligns mixed-size text and inline images
on a common baseline. It is opt-in so existing centered inline layouts stay
unchanged. Font variants still require the corresponding installed faces.

`LineEdit.MeasureTextWidth(text)` (also on `TextEdit`) measures the longest
unwrapped line in logical pixels using the editor's resolved font, direction
and language. It does not change the draft, selection or viewport, and fails
explicitly if no font is available. DPI scaling is not applied twice.
