# Control Template Migration Manifest

- Status: Phase 0 contract
- Date: 2026-08-04
- Scope: every concrete public `Control` type declared under `src/Forma`
- Related plan: `plans/xaml-templates-items-and-virtualization-plan.md`, task 3

## Rules

Each type has exactly one migration classification. `Control` remains the universal styleable
visual/layout root. A **foundation** draws, lays out, or projects directly and never resolves a
`ControlTemplate`. A **semantic widget** owns behavior and receives all replaceable chrome from a
default template. A **specialized part** retains only indivisible rendering/input behavior behind a
semantic owner and is not a general XAML root.

Every row requires MonoGame and FNA unit/build/render coverage, Release compiled-XAML coverage,
trim/NativeAOT coverage, and the listed application continuity checks. `Catalog auto` means the
reflected public-type story in `StoryCatalog`; named Catalog stories add targeted behavior/render
coverage. `Signal Run all` means HUD, settings, and result XAML plus `GameScreen` and
`GameViewPresentation` hot-reload wiring.

## Gate Contracts

| Gate | Required preservation |
| --- | --- |
| `F` | Measure/arrange, direct render, hit test, resources/styles, clipping, device loss, screenshot parity, and no template lookup. |
| `B` | Pointer/keyboard/focus, action timing, toggle/group state, logical content projection, pseudo-states, template replacement. |
| `R` | Range clamp/step/page/ratio, orientation, drag/key interaction, template geometry, render parity. |
| `E` | Editing, selection, caret, IME, clipboard, undo, wrapping, focus, and presenter geometry. |
| `C` | Color conversion, cursor geometry, preset/deferred behavior, popup lifecycle, render parity. |
| `G` | Graph coordinates, slots/ports/connections, selection, drag/resize, zoom, canvas parity. |
| `M` | Popup/menu focus, shortcuts, submenu timing, modal dismissal, dialog lifecycle. |
| `L` | Source-occurrence identity, selection/navigation/search/scrolling, generated visuals, rich/tree rendering. |
| `V` | Viewport rendering/input forwarding, target lifetime, joystick geometry and capture. |

## Default Template Contracts

| Code | Default composition, required parts, projected content, and states |
| --- | --- |
| `BTN` | `Border.chrome > ContentPresenter`; `PART_ContentPresenter`. Projects text/icon/content. States `:disabled`, `:hover`, `:pressed`, `:focus`, `:checked`. Texture/swatch/menu variants add typed image, swatch, or `PART_Popup` parts. |
| `CHECK` | Indicator plus content; `PART_Indicator`, `PART_ContentPresenter`. Adds checked/unchecked presentation while preserving button behavior. |
| `RANGE` | `PART_Track`, `PART_Fill`, `PART_Thumb`; scrollbars add decrement/increment buttons; progress variants use an indicator/text presenter. |
| `SPLIT` | Two content presenters and `PART_Dragger : ISplitDragger`; projects the first two logical children. Orientation, dragging, collapsed, focus, disabled states. |
| `EDITOR` | Border and `LineEditPresenter`/`TextEditPresenter`; code editor adds gutter, minimap, and completion parts. `SpinBox` adds `PART_Editor` and increment/decrement buttons. Owner retains edit state. |
| `CHOICE` | Content plus `PART_Popup`; tab container uses `PART_TabBar` and `PART_SelectedContentPresenter`; tab bar uses `TabStripPresenter`. |
| `SCROLL` | `PART_ScrollPresenter`, `PART_HorizontalScrollBar`, `PART_VerticalScrollBar`; projects one logical child. Owner retains scrolling policy/input. |
| `POPUP` | Border/content presenter; menus add `PART_Items` and optional search editor; menu bar uses a horizontal items presenter. |
| `DIALOG` | `PART_TitlePresenter`, `PART_ContentPresenter`, `PART_AcceptButton`, optional cancel/custom actions. File dialog adds navigation, path, entries, filename, filter, options, and overwrite parts. |
| `COLOR` | `ColorFieldPresenter` plus composable channel/mode/swatch/hex controls; popup/button/dialog wrappers use normal popup/button/dialog contracts. |
| `FOLD` | `PART_Header`, `PART_ContentPresenter`; projects header and logical child. |
| `GRAPH` | Element/node/frame chrome uses resize/title/content/port presenters; `GraphEdit` requires `GraphCanvasPresenter`, toolbar, and minimap parts. |
| `COLLECTION` | `ItemList` uses `ItemsPresenter`; rich text uses `RichTextPresenter`; tree uses `TreePresenter` plus scrollbar/editor-popup parts. |
| `VIEW` | `SubViewportPresenter` or `JoystickPresenter`; owner retains lifetime and interaction state. |

## Foundation And Layout Types

| Type | Source | Classification | Direct responsibility and styleable surface | Migration and gate | Application ownership |
| --- | --- | --- | --- | --- | --- |
| `Control` | `Control.cs` | Foundation root | Geometry, layout participation, input, resources, classes, inherited values, children | Remains template-free universal root; add visual/inheritance trees and compositing surface. `F` | Catalog auto; base for Signal Run all |
| `Panel` | `Controls.cs` | Drawing primitive | Bounds fill/border; background and border colors | Compatibility primitive; migrate composition to `Border`. `F` | Catalog auto/custom |
| `Label` | `Label.cs` | Drawing primitive | Text/font/color, wrapping, clipping, alignment, bidi/language, padding | Migrate/alias to `TextBlock`; no template. `F` | Catalog shell, animation, binding, dynamic-size, icon, style; Signal Run all |
| `ColorRect` | `VisualControls.cs` | Drawing primitive | Solid color fill | Compatibility alias over `RectangleShape`/`Border`. `F` | Catalog shell, animation, style |
| `TextureRect` | `VisualControls.cs` | Drawing primitive | Bitmap source, stretch/tile/flip, tint | Migrate to `Image`; template-free. `F` | Catalog auto/custom |
| `NinePatchRect` | `VisualControls.cs` | Drawing primitive | Nine-slice margins, edge/center policy, tint | Migrate to `NineSliceImage`; template-free. `F` | Catalog auto/custom |
| `ThemeIconRect` | `VisualControls.cs` | Drawing primitive | Theme icon lookup, intrinsic size, tint | Rename compatibility path to `ThemeIconView`. `F` | Catalog auto/custom |
| `ReferenceRect` | `VisualControls.cs` | Drawing primitive | Debug/reference outline | Compose with border/rectangle; retain compatibility alias. `F` | Catalog auto/custom |
| `HSeparator` | `VisualControls.cs` | Drawing primitive | Horizontal line and minimum thickness | Direct line-shape compatibility primitive. `F` | Catalog shell/auto |
| `VSeparator` | `VisualControls.cs` | Drawing primitive | Vertical line and minimum thickness | Direct line-shape compatibility primitive. `F` | Catalog auto |
| `Container` | `Containers.cs` | Layout panel | Base child fitting and layout participation | Template-free panel base. `F` | Catalog preview/auto |
| `BoxContainer` | `Containers.cs` | Layout panel | Axis layout, orientation, separation, stretch ratios | Migrate to `StackPanel`/`FlexPanel`; compatibility type. `F` | Catalog broad; Signal Run all |
| `HBoxContainer` | `Containers.cs` | Layout panel | Horizontal `BoxContainer` policy | Compatibility alias over horizontal stack/flex. `F` | Catalog broad; Signal Run settings/result |
| `VBoxContainer` | `Containers.cs` | Layout panel | Vertical `BoxContainer` policy | Compatibility alias over vertical stack/flex. `F` | Catalog broad; Signal Run all views |
| `MarginContainer` | `Containers.cs` | Layout panel | Inset child by margins | Migrate to `Border.Padding`; compatibility panel. `F` | Catalog shell/auto |
| `CenterContainer` | `Containers.cs` | Layout panel | Center one child | Migrate to `OverlayPanel` alignment. `F` | Catalog auto |
| `GridContainer` | `Containers.cs` | Layout panel | Fixed-column grid arrangement | Migrate to `GridPanel`. `F` | Catalog auto/custom |
| `AspectRatioContainer` | `VisualControls.cs` | Layout panel | One-child aspect fit/cover/alignment | Migrate to `Viewbox`. `F` | Catalog auto/custom |
| `FlowContainer` | `VisualControls.cs` | Layout panel | Intrinsic wrapping flow and gaps | Migrate to `WrapPanel`; compatibility base. `F` | Catalog auto/custom |
| `HFlowContainer` | `VisualControls.cs` | Layout panel | Horizontal wrapping flow | Compatibility alias over `WrapPanel`. `F` | Catalog icon inventory/auto |
| `VFlowContainer` | `VisualControls.cs` | Layout panel | Vertical wrapping flow | Compatibility alias over `WrapPanel`. `F` | Catalog auto |
| `PanelContainer` | `VisualControls.cs` | Layout panel | Panel surface, padding, child fitting | Compatibility composition of `Border` and panel; no template. `F` | Catalog shell/auto/custom |

No existing concrete public type is a general presenter. `ContentPresenter`, `ItemsPresenter`, and
`ScrollPresenter` are new foundational types.

## Semantic Widgets

| Type | Source | Template | Behavior/style surface to preserve | Gate | Application ownership |
| --- | --- | --- | --- | --- | --- |
| `BaseButton` | `Buttons.cs` | `BTN` | Action mode, toggle/group, shortcut, text/icon alignment | `B` | Catalog auto/custom |
| `Button` | `Buttons.cs` | `BTN` | Base button behavior and arbitrary content compatibility | `B` | Catalog shell/stories; Signal Run all |
| `CheckBox` | `Buttons.cs` | `CHECK` | Toggle state and indicator semantics | `B` | Catalog shell, animation, binding; Signal Run settings |
| `CheckButton` | `Buttons.cs` | `CHECK` variant | CheckBox behavior with alternate packaged chrome | `B` | Catalog auto |
| `LinkButton` | `SelectionControls.cs` | `BTN` variant | URI activation and text interaction | `B` | Catalog auto |
| `TextureButton` | `SelectionControls.cs` | `BTN` variant | State textures and alpha hit testing | `B` | Catalog auto/custom |
| `ColorPresetButton` | `FoldableControls.cs` | `BTN` swatch | Preset color and swatch activation | `C/B` | Catalog auto/custom |
| `ColorPickerButton` | `ColorControls.cs` | `BTN` swatch/popup | Color value, popup lifecycle, edit/accept behavior | `C/B` | Catalog shell/auto/custom |
| `Slider` | `Slider.cs` | `RANGE` | Value, orientation, pointer/key changes, grabber state | `R` | Catalog auto/custom |
| `HSlider` | `Slider.cs` | `RANGE` horizontal | Slider semantics with fixed orientation | `R` | Catalog binding/dynamic-size; Signal Run settings |
| `VSlider` | `Slider.cs` | `RANGE` vertical | Slider semantics with fixed orientation | `R` | Catalog auto |
| `ProgressBar` | `ProgressBar.cs` | `RANGE` progress | Fill ratio, percentage text, range semantics | `R` | Catalog binding/auto/custom |
| `TextureProgressBar` | `SelectionControls.cs` | `RANGE` texture | Texture layers, radial/nine-slice geometry | `R` | Catalog auto/custom |
| `ScrollBar` | `SelectionControls.cs` | `RANGE` scrollbar | Paging, thumb drag, wheel, smooth/drag-node scrolling | `R` | Catalog auto/custom |
| `HScrollBar` | `SelectionControls.cs` | `RANGE` horizontal scrollbar | ScrollBar semantics with fixed orientation | `R` | Catalog auto |
| `VScrollBar` | `SelectionControls.cs` | `RANGE` vertical scrollbar | ScrollBar semantics with fixed orientation | `R` | Catalog auto |
| `SplitContainer` | `VisualControls.cs` | `SPLIT` | Two-child layout, offset, collapse, drag, RTL | `B/F` | Catalog auto/custom |
| `HSplitContainer` | `VisualControls.cs` | `SPLIT` horizontal | Split semantics with fixed orientation | `B/F` | Catalog auto |
| `VSplitContainer` | `VisualControls.cs` | `SPLIT` vertical | Split semantics with fixed orientation | `B/F` | Catalog auto |
| `LineEdit` | `LineEdit.cs` | `EDITOR` line | Editing, selection, IME, clipboard, history, secret text | `E` | Catalog shell/binding/dynamic-size; Signal Run settings |
| `TextEdit` | `TextEdit.cs` | `EDITOR` text | Multiline editing, wrapping, gutters and scrolling | `E` | Catalog auto/custom |
| `CodeEdit` | `GraphAndCodeControls.cs` | `EDITOR` code | Folding, completion, gutters, minimap, code navigation | `E` | Catalog auto/custom |
| `SpinBox` | `SpinBox.cs` | `EDITOR` numeric | Numeric parsing, prefix/suffix, repeat and drag adjustment | `E/R` | Catalog auto/custom |
| `OptionButton` | `OptionButton.cs` | `CHOICE` popup | Item identity, popup selection, shortcuts, longest-fit | `B/L` | Catalog shell, dynamic-size, icon; custom |
| `TabBar` | `SelectionControls.cs` | `CHOICE` tabs | Selection, disabled/hidden tabs, overflow, reorder | `B/L` | Catalog auto/custom |
| `TabContainer` | `TabContainer.cs` | `CHOICE` pages | Selected logical page projection and tab policy | `B/L` | Catalog auto/custom |
| `ScrollContainer` | `ScrollContainer.cs` | `SCROLL` | Viewport policy, wheel/touch/focus-follow, hints, RTL | `R/V` | Catalog shell, icon inventory, custom |
| `Popup` | `Popup.cs` | `POPUP` | Modal focus, outside click, Escape, focus restoration | `M` | Catalog auto/custom |
| `PopupPanel` | `Popup.cs` | `POPUP` panel | Popup behavior with panel chrome | `M` | Catalog auto |
| `PopupMenu` | `MenusAndDialogs.cs` | `POPUP` menu | Item state, search, shortcuts, submenus, tooltips | `M/L` | Catalog auto/custom |
| `MenuButton` | `MenusAndDialogs.cs` | `BTN` menu | Button behavior plus popup menu ownership | `M/B` | Catalog auto/custom |
| `MenuBar` | `MenusAndDialogs.cs` | `POPUP` menu bar | Sibling switching, shortcuts, horizontal menu layout | `M` | Catalog auto/custom |
| `AcceptDialog` | `MenusAndDialogs.cs` | `DIALOG` | Accept/cancel ordering, actions, modal lifecycle | `M` | Catalog auto/custom |
| `ConfirmationDialog` | `MenusAndDialogs.cs` | `DIALOG` confirm | Confirmation/cancel semantics and button ordering | `M` | Catalog auto/custom |
| `FileDialog` | `MenusAndDialogs.cs` | `DIALOG` file | Filesystem modes, navigation, filter, save/overwrite validation | `M/L` | Catalog auto/custom |
| `ColorPicker` | `ColorControls.cs` | `COLOR` | Color spaces, channels, presets, cursor, deferred changes | `C` | Catalog auto/custom |
| `ColorPickerPopupPanel` | `ColorControls.cs` | `COLOR` popup | Picker popup ownership, old-color rollback, dismissal | `C/M` | Catalog auto/custom |
| `ColorPickerDialog` | `ColorControls.cs` | `COLOR` dialog | Picker confirmation/cancel and modal lifecycle | `C/M` | Catalog auto/custom |
| `FoldableContainer` | `FoldableControls.cs` | `FOLD` | Header interaction, content visibility, accordion group | `B` | Catalog auto/custom |
| `GraphElement` | `GraphAndCodeControls.cs` | `GRAPH` element | Graph position/size, selection, drag/resize | `G` | Catalog auto |
| `GraphNode` | `GraphAndCodeControls.cs` | `GRAPH` node | Title, slots, ports, connection compatibility | `G` | Catalog auto/custom |
| `GraphFrame` | `GraphAndCodeControls.cs` | `GRAPH` frame | Frame title/content, autoshrink and contained nodes | `G` | Catalog auto |
| `GraphEdit` | `GraphAndCodeControls.cs` | `GRAPH` canvas | Connections, selection, dragging, framing, zoom, shortcuts | `G` | Catalog auto/custom |
| `ItemList` | `SelectionControls.cs` | `COLLECTION` items | Current/selected slots, multi/toggle, search, grid, scroll | `L` | Catalog shell/auto/custom |
| `RichTextLabel` | `SelectionControls.cs` | `COLLECTION` rich text | BBCode, metadata, selection, scrolling, context menu/effects | `L/E` | Catalog shell/auto/custom |
| `RichTextDocument` | `SpecializedControls.cs` | `COLLECTION` document | Rich text behavior plus document/effect collection | `L/E` | Catalog auto |
| `Tree` | `Tree.cs` | `COLLECTION` tree | Items/cells, folding, editing, selection, search, drag, scroll | `L/E` | Catalog auto/custom |
| `SubViewportContainer` | `SpecializedControls.cs` | `VIEW` viewport | Independent UI context, target lifetime, input forwarding | `V` | Catalog auto/custom |
| `VirtualJoystick` | `SpecializedControls.cs` | `VIEW` joystick | Capture, dead zone, normalized output and events | `V` | Catalog auto/custom |

## Specialized Parts And Compatibility

These types are currently public but are coupled to one semantic owner. The compiler must reject
them as standalone XAML roots once their owner templates land. Internalization is source-, binary-,
and XAML-breaking, so exposed members first move to a public base type/interface in this breaking
release; implementation types can then become internal.

| Type | Source | Specialized responsibility | Owner/disposition | Gate | Application ownership |
| --- | --- | --- | --- | --- | --- |
| `DynamicGlyphAtlasView` | `DynamicGlyphAtlasView.cs` | Diagnostic atlas-page rendering | Move behind diagnostics model/package; not general vocabulary | `F` | Catalog auto/custom dynamic-text diagnostics |
| `SpinBoxLineEdit` | `SpinBox.cs` | Route Up/Down while retaining line editing | `SpinBox.PART_Editor`; change `SpinBox.LineEdit` return type to `LineEdit` | `E` | No standalone story; owner custom story |
| `GraphEditMinimap` | `GraphOverlays.cs` | Graph/minimap transforms, draw, pan/resize | `GraphEdit.PART_Minimap`; replace public `GraphEdit.Minimap` type | `G` | Catalog auto through owner/custom |
| `GraphEditFilter` | `GraphOverlays.cs` | Consume graph-canvas press/release gestures | Internal input part/interface for `GraphEdit` | `G` | Catalog auto through owner/custom |
| `SplitContainerDragger` | `GraphOverlays.cs` | Forward split-offset drag | Internal `ISplitDragger` for `SplitContainer` | `B` | Catalog auto through owner/custom |
| `SplitContainerMultiDragger` | `GraphOverlays.cs` | Multi-split drag forwarding | Internal `ISplitDragger` implementation | `B` | Catalog auto through owner/custom |
| `PopupMenuItems` | `MenusAndDialogs.cs` | Menu hit test, wheel and tooltip routing | `PopupMenu.PART_Items`; change public `ItemsControl` member to presenter/base contract | `M/L` | No standalone story; owner custom story |

`ColorPickerPopupPanel` remains a semantic compatibility widget because
`ColorPickerButton.Popup` exposes it publicly. It may become a specialized popup part only after
that API returns a stable public popup contract.

## First-Party Continuity Matrix

| Application | Affected types | Required migration checks |
| --- | --- | --- |
| Forma Catalog | Every public constructible type through automatic stories; named shell, animation, binding, dynamic-size, icon, style, text, graph, menu/dialog, collection, color, viewport, and custom stories above | Preserve story inventory/search/selection/property editors, DynamicText toggle, 1x/2x metrics and screenshots, exact peer render parity, Debug XAML/effect hot reload, and no temporary legacy chrome. |
| Signal Run | `BoxContainer`, `HBoxContainer`, `VBoxContainer`, `Label`, `Button`, `CheckBox`, `HSlider`, `LineEdit` | Preserve score/timer/status, pause/resume/restart, player/difficulty/sound/volume two-way bindings, resources/styles, view-model identity and game state across hot reload on both peers. Remain core-only with no DynamicText/Media/compiler-runtime dependency in Release. |

## Completeness Check

The manifest contains 78 concrete public types: the `Control` root, 9 drawing primitives, 12
layout panels, 49 semantic templated widgets, and 7 specialized parts. A type may move between
public compatibility and internal implementation status only by updating its row, API migration,
Catalog/Signal Run ownership, and focused parity gate together.
