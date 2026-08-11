---
title: Text input controls
description: Choose and integrate Forma text and numeric editors.
---

# Text input controls

Use [LineEdit](xref:Forma.LineEdit) for one line and IME-aware entry,
[TextEdit](xref:Forma.TextEdit) for multiline editing, and [SpinBox](xref:Forma.SpinBox) for numeric
text with stepping. [LineEditPresenter](xref:Forma.LineEditPresenter) and
[SpinBoxLineEdit](xref:Forma.SpinBoxLineEdit) are template/owner infrastructure; application views
normally use their owning controls.

| Type | Selection note |
| --- | --- |
| [LineEdit](xref:Forma.LineEdit) | Single line, selection, clipboard, placeholder, submission, and text binding |
| [TextEdit](xref:Forma.TextEdit) | Multiline editing, carets, gutters, wrapping, and scrolling |
| [SpinBox](xref:Forma.SpinBox) | Numeric range editing with increment/decrement controls |
| [LineEditPresenter](xref:Forma.LineEditPresenter) | Template presenter for line-edit rendering and interaction |
| [SpinBoxLineEdit](xref:Forma.SpinBoxLineEdit) | Owner-created editor used by `SpinBox` |

`LineEdit` starts empty, is keyboard-focusable, and has `6,4,6,4` padding. Host text-input forwarding
is required for composed/IME text; do not convert key codes to characters. `SpinBox` normally commits
edited text on submission because `UpdateOnTextChanged` defaults to `false`. Its `ArrowLayout`
defaults to `Vertical`, with stacked arrows on the right. Set `ArrowLayout="Horizontal"` in XAML
for a full-height decrement button on the left and increment button on the right around the editor.

Text editors expose a text-box accessibility role, current value, selection/edit actions, and a
read-only state when editing is disabled. `SpinBox` exposes the spin-button role and range actions.
Give an explicit `AccessibilityLabel` when the nearby visual label is not represented by the host's
accessibility bridge.

Catalog: [LineEdit](https://github.com/zigrok/Forma/blob/main/samples/Forma.Catalog/Stories/Controls/LineEdit.xaml),
[TextEdit](https://github.com/zigrok/Forma/blob/main/samples/Forma.Catalog/Stories/Controls/TextEdit.xaml),
[SpinBox](https://github.com/zigrok/Forma/blob/main/samples/Forma.Catalog/Stories/Controls/SpinBox.xaml).
See [Input and focus](../../input-and-focus.md) and [Data binding](../../data-binding.md).

Every public type in the table has a Catalog story with the exact unqualified type name. Its stable
identifier is `catalog-` plus the kebab-case type name, such as `catalog-line-edit`; the Catalog's
**Open reference** link returns to this page.
