# Forma XAML Language Contract

## Status and Scope

This document defines the Forma XAML source language for the template-first breaking release. It is
the contract used by the runtime, compiler, MSBuild integration, command-line validator, language
server, tests, and samples. It supersedes the earlier v1 exclusions for templates, relative
sources, selector combinators, items controls, and virtualization.

Contracted syntax is not evidence that a rollout phase is complete. Implementation status and
canonical fixture delivery are tracked in `plans/xaml-templates-items-and-virtualization-plan.md`;
the runtime, compiler, tooling, tests, Catalog, and Signal Run must land a capability together
before release. Unsupported contracted syntax is a diagnostic during development, never a runtime
reflection fallback.

Forma XAML is a Forma-native declarative language built on XML and selected XAML 2006 concepts. It
does not promise source compatibility with WPF, Avalonia, MAUI, UWP, WinUI, or any other XAML
framework. A construct is supported only when this document defines it and the Forma compiler
accepts it.

Release builds compile XAML and typed bindings to IL. Generated views and the Forma runtime do not
depend on XamlX, Mono.Cecil, a runtime XAML reader, reflection-based binding, or source XAML.
Development hot reload is an opt-in, non-trimmed, non-NativeAOT feature with separate compiler
dependencies.

## Namespaces and Project Items

The Forma namespace URI is fixed as:

```xml
xmlns="https://forma.dev/xaml"
```

The XAML language namespace is:

```xml
xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
```

Application types use `clr-namespace:` declarations. An optional assembly component follows a
semicolon:

```xml
xmlns:views="clr-namespace:Game.Views"
xmlns:vm="clr-namespace:Game.ViewModels;assembly=Game.Core"
```

MSBuild treats project `.xaml` files as `@(FormaXaml)` by default, subject to the SDK's normal
default-item exclusions for output and intermediate directories. Projects may explicitly include
or remove items. Logical source identity is the normalized, project-relative path using `/`; this
identity is also used by diagnostics, incremental compilation, and hot-reload registration.

Each compiled view has one root element. Multiple implicit XAML files may not populate the same
root CLR type. Resource dictionaries without `x:Class` are allowed and compile as resources rather
than views.

## Project Setup and Build Properties

Use matching runtime and build peers. The build package is private because it contributes only
MSBuild targets and compiler tools:

```xml
<ItemGroup>
  <PackageReference Include="Forma.MonoGame" Version="0.1.0-alpha.2" />
  <PackageReference Include="Forma.Xaml.Build.MonoGame"
                    Version="0.1.0-alpha.2"
                    PrivateAssets="All" />
</ItemGroup>
```

Replace both `.MonoGame` suffixes with `.FNA` for FNA. Do not mix peers. The package imports the
compiler automatically and includes every project `.xaml` file as `@(FormaXaml)`.

Debug hosts that use live XAML replacement also reference the matching
`Forma.Xaml.HotReload.MonoGame` or `Forma.Xaml.HotReload.FNA` package and set
`FormaXamlHotReload=true`. Do not add that package to core-only Release, trimmed, or NativeAOT
profiles. `Forma.DynamicText` is independently optional; templates, selectors, items, `DataGrid`,
virtualization, and `SpriteFontAdapter` text require only the core peer.

MSBuild properties:

- `FormaXamlRequireCompiledBindings` requires inherited `x:DataType`; it defaults to `true` in
  Release and `false` in Debug.
- `FormaXamlValidateOnly=true` validates without injecting IL.
- `FormaXamlHotReload=true` copies Debug source XAML for the development host. It has no effect on
  Release output.
- `FormaXamlIntermediateDirectory` and `FormaXamlDevelopmentOutputDirectory` override generated
  intermediate and Debug source-copy locations.

Compilation is incremental over XAML, references, the target assembly, and compiler task. Release
outputs are deterministic and portable-PDB diagnostics retain source file, line, and column.
XAML edits and additions/removals participate in C# compilation inputs, as do the XAML build
options. Referenced implementation assemblies also participate, even when a dependency's
public reference assembly is unchanged. Changed inputs regenerate a pristine assembly
before Cecil injection; an unchanged rebuild does not append another set of generated
types or registrations.

## Object Construction and Content

An element names a public CLR type in its XML namespace. Attribute syntax sets public properties
or subscribes public events. Property-element syntax uses `Owner.Property`. Child elements are
added through the configured `IAddChild<T>`/`IAddChild` content contract. Forma controls implement
that contract by forwarding to `Control.AddChild`.

The compiler validates constructors, members, event-handler signatures, content types, conversion,
and accessibility. It does not silently store unknown XML or use runtime reflection as a fallback.

Text content is supported only for types that explicitly define text content. Controls do not
implicitly map text to a `Text` property.

## `x:Class` and Populate Semantics

`x:Class` names the public or internal code-behind root type:

```xml
<PanelContainer
    xmlns="https://forma.dev/xaml"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    x:Class="Game.Views.HudView">
</PanelContainer>
```

The code-behind type derives from, or is the same type as, the XAML root element and calls
`FormaXamlLoader.Load(this)` from its constructor. Build integration injects a hidden populate
method that applies the XAML to that existing constructor-created instance. Populate does not
replace the root, rerun its constructor, or discard fields initialized by the constructor.

A root without `x:Class` compiles to a generated factory and may be loaded through
`FormaXamlLoader.Load<T>()`. Generated build and populate methods are implementation details and
are not a public API.

## Namescopes and `x:Name`

`x:Name` assigns `Control.Name` when applicable and registers the object in the nearest XAML
namescope. Names must be unique within that scope and use identifier syntax. A compiled view root
creates a namescope. Every `ControlTemplate` and `DataTemplate` instance creates a separate local
namescope. `ItemsPanelTemplate` creates a fresh panel in its owning items-presenter scope. Names in
one template instance are not visible from another instance or from the containing view.

Names do not generate fields. Code-behind resolves short-lived references through the namescope
API. Storyboard targets and trigger source names resolve in the same local namescope. A name from
another compiled view is not visible unless an explicit host API passes that object.

Hot reload replaces a namescope with the detached replacement tree. Code must not retain
long-lived references to named controls across replacement. Code-behind uses namescope lookup
rather than generated fields:

```csharp
public sealed class HudView : Control
{
  public HudView() => FormaXamlLoader.Load(this);

  public Label ScoreText => NameScope.GetNameScope(this)!.Find<Label>("ScoreText");
}
```

## Data Context and Typed Binding

`DataContext` is inherited through the control tree. A local value replaces the inherited value
for that subtree. `x:DataType` declares the expected data-context CLR type and is inherited by
descendants unless overridden:

```xml
xmlns:vm="clr-namespace:Game.ViewModels"
x:DataType="vm:GameHudViewModel"
```

Release and NativeAOT builds require `x:DataType` for every data-context binding. The compiler
resolves every path segment and emits typed accessors; it does not use reflection or string-based
member lookup at runtime.

The v1 binding form is:

```xml
Text="{Binding Player.Profile.DisplayName,
               Mode=OneWay,
               FallbackValue='Player',
               TargetNullValue='Guest',
               StringFormat='Name: {0}',
               Converter={StaticResource NameConverter},
               ConverterParameter=Short,
               UpdateSourceTrigger=PropertyChanged}"
```

Supported binding behavior:

- The path is empty for the whole data context or is a dotted public property path.
- Null intermediate values short-circuit the path and use `TargetNullValue` or the target default.
- Modes are `OneTime`, `OneWay`, and `TwoWay`. `OneWay` is the default.
- `FallbackValue` applies when evaluation or conversion fails. `TargetNullValue` applies to a
  successfully evaluated null result.
- `StringFormat` uses invariant composite-format syntax unless a converter explicitly applies
  another culture.
- A converter implements Forma's `IValueConverter` contract and may be supplied from resources.
- Source notifications use `INotifyPropertyChanged`; bindings never poll.
- `TwoWay` requires a writable source property and a compiler-known target adapter with a matching
  change event. Unsupported target properties are diagnostics.
- `UpdateSourceTrigger` is `Default`, `PropertyChanged`, or `LostFocus`. The target adapter defines
  `Default`; an unsupported trigger is a diagnostic.

Bindings use inherited `DataContext` by default and may select one statically typed relative
source:

```xml
Text="{Binding Content,
               RelativeSource={RelativeSource TemplatedParent}}"
Width="{Binding Height, RelativeSource={RelativeSource Self}}"
IsEnabled="{Binding CanSelect,
                    RelativeSource={RelativeSource FindAncestor,
                                                   AncestorType=ListBox,
                                                   AncestorLevel=1}}"
```

`Self` has the target element type. `TemplatedParent` is legal only inside a `ControlTemplate` and
has that template's `TargetType`. `FindAncestor` requires `AncestorType`; `AncestorLevel` is
optional, one-based, and defaults to `1`. It walks `VisualParent`, rebinds after visual reparenting,
and cannot cross a data-template or compiled-view boundary. Paths, modes, conversion, and two-way
writes are validated against the selected source type at compile time.

Arbitrary object sources, element-name binding, multibinding, priority binding, commands, indexers,
methods, dynamic objects, and untyped reflection fallback are outside this release.

## Literals, Conversion, and Markup Extensions

The compiler converts attribute text to the target CLR type. V1 includes invariant conversion for
strings, booleans, numeric types, enums and flag enums, nullable values, named and hexadecimal
`Color`, `Vector2`, `Thickness`, and `TimeSpan`. Invalid or ambiguous values are diagnostics.

V1 markup extensions are:

- `{Binding ...}` for typed data binding.
- `{RelativeSource Self}`, `{RelativeSource TemplatedParent}`, and typed
  `{RelativeSource FindAncestor, ...}` nested in a binding.
- `{StaticResource Key}` for one-time lexical resource resolution.
- `{DynamicResource Key}` for observable resource resolution.

A leading `{}` escapes a literal value that otherwise begins with `{`. Markup extensions may be
nested only in arguments explicitly documented to accept them, such as a binding converter.

## Resources

`ResourceDictionary` stores values by unique `x:Key`. A key is required unless a resource type has
a documented implicit key. Duplicate keys in one dictionary are diagnostics.

Controls expose a `Resources` property. Lookup starts at the requesting control's dictionary, then
walks parent controls, then uses `UIContext` application resources. Local entries override merged
dictionaries. Merged dictionaries are searched in reverse declaration order so the last merge has
the highest priority. Cycles and unresolved sources are diagnostics.

`StaticResource` resolves once when the tree is built. `DynamicResource` observes the winning
resource entry and reapplies the XAML value layer when that entry or lookup winner changes. Missing
static resources are compile errors; missing dynamic resources produce a diagnostic and use the
target's underlying value until a matching resource appears.

Resources can contain Forma controls only where a consuming property explicitly accepts them.
Shared mutable UI controls are otherwise rejected.

## Classes and Selector Styles

`Classes` is a whitespace-separated set of case-sensitive class names. Duplicate names collapse to
one entry. Runtime class changes notify the style engine.

Selectors are Forma-owned and Avalonia-inspired; they do not promise CSS or Avalonia source
compatibility. A selector list contains comma-separated complex selectors. A complex selector
contains compound selectors joined by descendant (` `), direct visual-child (`>`), or explicit
control-template-boundary (`>>`) combinators. A compound may contain one type or `*`, one `#name`,
and any number of `.class`, pseudo-state, and single-compound `:not(...)` terms. Examples are:

```text
Button.primary
#PauseButton
Dialog .command
ToolBar > Button:not(.overflow)
Button.primary, MenuButton.primary
ListBoxItem:selected >> Border.selection
```

Selectors match right to left and setters target the rightmost compound. Child and descendant
matching follows `VisualParent` only within the current style boundary. `>>` crosses exactly one
`ControlTemplate` boundary from the templated owner on its left; it never crosses a
`DataTemplate`, an arbitrary compiled-view boundary, or more than one nested template. A second
template crossing requires another `>>`. Style candidates remain restricted to the attachment
scope of the style resource.

Standard pseudo-states are `:hover`, `:focus`, `:focus-within`, `:disabled`, `:pressed`, `:checked`,
`:selected`, and `:current`. Referenced assemblies may declare additional typed pseudo-states in
compiler-readable metadata. Matching is event-driven; state, class, name, visual parent, scope, or
template-instance changes invalidate only indexed candidates whose compiled dependencies mention
the change. Warm frames do not poll states or scan the full visual tree.

Styles declare typed setters with compiler-validated property names and values:

```xml
<Style x:Key="PrimaryAction" Selector="Button.primary">
  <Setter Property="Margins" Value="4,2" />
</Style>
```

Specificity is compared lexicographically as name count, class plus pseudo-state count, then type
count. `*` and combinators add zero; `:not(...)` contributes its argument specificity. Each list
arm is ranked independently. Declaration order breaks equal specificity, with the later style
winning. The compiler infers every rightmost subject type and validates each setter against all
arms. When a selector stops matching, the previous winning style or underlying value is restored.

Sibling combinators, attribute/property selectors, `:is`, `:where`, `:has`, structural-position
selectors, arbitrary data predicates, cascade layers, CSS namespaces, and CSS text stylesheets are
unsupported diagnostics.

## XAML Value Precedence

Only properties touched by XAML participate in the coordinated value layer. Precedence from low to
high is:

1. Theme or control default.
2. Winning selector style.
3. Inherited value, binding value, or local XAML value.
4. Active animation value.

Later values do not destroy lower layers. Removing a class, ending a trigger, stopping an
animation, or detaching a binding reveals the next applicable value. A plain C# setter remains
valid, but code that needs immediate reconciliation while a property is styled or animated uses
the documented XAML value API.

## Visual Architecture and Templates

`Control` is the universal styleable visual and layout node. Foundational elements render, lay out,
or project content directly and never resolve a `ControlTemplate`: visual primitives (`Border`,
text, image, and shape elements), layout panels (`CanvasPanel`, `OverlayPanel`, `StackPanel`,
`WrapPanel`, `FlexPanel`, `GridPanel`, and `Viewbox`), and presenters (`ContentPresenter`,
`ItemsPresenter`, and `ScrollPresenter`). Value resources such as brushes, geometry, transforms,
drawings, effects, and text inlines are not visual-tree nodes. A specialized presenter is allowed
only for indivisible rendering/input behavior such as text editing; its semantic owner remains
templated.

`TemplatedControl : Control` is the boundary for semantic widgets. Buttons, editors, selectors,
menus, dialogs, sliders, `ItemsControl`, and `ListBox` own behavior but obtain all replaceable
chrome from a `ControlTemplate`. A semantic widget may use a documented specialized part but may
not retain an unconditional outer-chrome draw path. Foundational elements cannot have control
templates.

`ControlTemplate` requires one control root and a `TargetType`. Applying it creates a fresh
`TemplateInstance`, local namescope, binding/style/resource/trigger lifetime, and typed
`TemplatedParent`; reapplication disposes the old instance. Explicit `TemplatedControl.Template`
wins over the nearest type-hierarchical theme template and then the packaged typed default.
Template factories are closed generated delegates and never use reflection, assembly scanning, or
a runtime XAML reader.

### Foundational Visual and Compositing Model

`Border` supplies background, per-edge border thickness, padding, and bounded corner radii.
`TextBlock` and typed inlines provide package-independent text projection; image, nine-slice,
theme-icon, shape, geometry, path, drawing, transform, clip, mask, shadow, and bounded effect values
compose without introducing semantic widgets. Brushes include solid, linear/radial gradient,
image, and drawing forms. Dynamic font loading and shaping remain an optional companion concern,
not a template or compiler dependency.

Margin participates outside a control's arranged box; border and padding consume space inside it;
content alignment then positions the child in the remaining content box. Layout, drawing, hit
testing, focus/accessibility bounds, clipping, masking, opacity, and transforms consume the same
composed state. Effects and shadows expand render bounds only within documented finite limits and
use bounded, device-loss-safe caches. Percentage layout cycles and over-budget compositing fall
back deterministically rather than allocating unbounded intermediate surfaces.

`CanvasPanel` performs anchored absolute placement, `OverlayPanel` layers children, stack and wrap
panels provide intrinsic flow, `FlexPanel` provides declared flex/wrap behavior, and `GridPanel`
uses explicit typed tracks and attached row/column placement. `Viewbox` scales one child under a
declared constraint. These panels remain direct layout foundations and never resolve templates.

`DataTemplate` requires one control root and requires `x:DataType` when it contains bindings. It is
selected explicitly through `ItemTemplate`, an inline template property, or a keyed resource;
there is no implicit closest-item-type lookup or runtime `DataTemplateSelector`.
`ItemsPanelTemplate` creates one fresh compatible panel per `ItemsPresenter`; mutable panel or
template instances cannot be shared. Template roots may not contain nested `x:Class`. Event
attributes are forbidden directly inside a `DataTemplate`; an eventful row is an ordinary separate
compiled `x:Class` view referenced by the template, and its handlers belong only to that row.

## Items, Selection, and Virtualization

`ItemsControl` consumes an observable `ItemsSource`, requires an explicit `ItemTemplate` for data
items, and generates one logical slot per source occurrence. Duplicate object references remain
distinct slots. Add/remove preserve unaffected slots, move preserves slot identity, replace creates
a new slot, and reset rebuilds. Attached collection notifications must occur on the UI thread.
`ItemsPresenter` connects the owner generator to its `ItemsPanelTemplate`; ordinary panels realize
all slots, while virtualizing panels request bounded ranges.

`ListBox : ItemsControl` supports `Single`, `Multi`, and `Toggle` selection. `SelectedIndex` is the
canonical writable identity; `SelectedItem` is a lossy convenience when duplicate references
exist. `SelectedIndices` and `SelectedItems` are read-only source-occurrence projections in multi
selection, and ambiguous two-way collection binding is rejected. Current navigation and selection
are independent and expose `:current` and `:selected` on generated containers. Collection moves
preserve slot selection; replacement does not inherit the replaced slot's identity.

`VirtualizingStackPanel` supports vertical/horizontal variable-size realization and
`VirtualizingGridPanel` supports uniform two-dimensional realization. Realized controls are
bounded by viewport, overscan, and explicitly pinned focus/capture/edit interactions, independent
of source count. Unreliable extents use a positive estimate and are corrected incrementally without
enumerating the full source. Recycling is keyed by compatible container/template/theme versions;
stale or non-poolable instances are disposed under bounded pools. Warm recycled scrolling compiles
no templates, performs no reflection or full-source enumeration, and allocates no new realized
controls. Accessibility exposes logical offscreen item peers without retaining visual containers.

`ListBox` intentionally differs from legacy `ItemList` mutation quirks. Its current and selected
occurrences follow stable slots when items are inserted, removed before them, or moved across
them. Replacement creates a new unselected slot; removal, reset, and source replacement discard
only identities that no longer exist. Each user gesture or destructive collection delta publishes
property updates followed by one `SelectionChanged` event containing atomic old/new index and item
snapshots. Index-only reprojection after insert or move does not report a semantic selection change.

Each `ItemsControl` owns a pool for its current `UIContext`; `RecyclePoolCapacity` defaults to 64,
and eviction disposes the oldest retained container. Compatibility includes concrete container
type, control-template factory version, data-template factory version, item-container style
generation, owner, and context theme generation. Replacing any template or style, changing context,
or advancing the theme generation drains obsolete entries before they can be reused.

Pooling deactivates template bindings, resources, triggers, transitions, and clocks before detach,
then rebinds the item and activates the same template instance on reuse. The context clears focus,
pointer capture, drag, hover, pressed, and tooltip state for the recycled subtree. Built-in
foundational roots participate automatically; semantic widget roots require explicit reset logic.
An application-defined row control is non-poolable unless it implements
`IDataTemplateRecyclingState` to reset row-local validation, local values, and
code-behind state. A custom generated container likewise opts in through
`IRecyclableItemContainer`. `RealizedCount`, `RecycledCount`, and `PinnedCount` expose the current
bounds for diagnostics; pinning policy is defined with interaction anchoring below.

The first visible source-occurrence token and its intra-item pixel offset form the scroll anchor.
Indexed add, remove, and move notifications transform that anchor without scanning the source;
replacement or removal of the anchor itself falls back to the current raw offset. For reset or
source replacement, applications may set `ItemsControl.ItemKeySelector`; the first new occurrence
with the same key becomes the anchor. Without a configured key, or when that key no longer exists,
the raw offset is clamped to the new finite extent.

Pointer-captured, actively dragged, and explicitly edited containers remain realized outside the
overscan range. Editable descendants expose `IVirtualizationPinState`; multiple pins are independent
and are recycled on the first layout after their interaction ends. When focus alone scrolls out,
the owning items control becomes a temporary focus proxy and records the slot token, template-local
visual path, realization generation, data-template version, and theme generation. Focus returns only
when that same slot is realized while focus is still on the proxy. User focus movement, item removal,
disabled or missing descendants, item-template replacement, and theme/pool generation changes cancel
restoration.

## DataGrid

`DataGrid : ListBox` is the typed template-first table and tree-table control. `Mode="Flat"` maps
source occurrences directly; `Mode="Hierarchical"` uses one `DataGridExpanderColumn` with compiled
`Children` and optional `HasChildren`/two-way `IsExpanded` bindings. Hierarchical identities are
immutable occurrence-based `IndexPath` values, cycles are rejected, and collection notifications
are observed independently for expanded levels.

Columns are explicit: `DataGridTextColumn`, `DataGridCheckBoxColumn`,
`DataGridTemplateColumn`, and `DataGridExpanderColumn`. Header/cell templates, pixel/auto/star
widths, bounds, display order, visibility, alignment, and resize/sort policy are typed. Forma does
not auto-generate columns or reflect item properties. Rows are virtualized; columns are not, so no
more than `DataGrid.MaximumSupportedVisibleColumns` (256) columns may be visible. The deterministic
cell bound is realized rows multiplied by visible columns.

Header sorting cycles none/ascending/descending and requires a compiled `SortBinding` or typed
comparer. Stable multi-column descriptions are supported programmatically. Filtering uses a typed
`DataGridSource<T>` predicate or an application-owned filtered source and changes only when
`RefreshFilter` runs. `IncludeAncestorsOfMatches` preserves hierarchical paths to matching rows and
may inspect the full tree during that explicit refresh; warm frames do no sorting or filtering.

`SelectionUnit="Row"` reuses source-slot single/multi selection and exposes selected paths.
`SelectionUnit="Cell"` uses immutable `CellIndex` values, current cell, rectangular ranges, and
atomic old/new selection snapshots. Sorting, filtering, expansion/collapse, source deltas, and
template replacement preserve surviving paths, selection/current state, expansion state, and
scroll anchors. Row and cell containers expose selected/current and expansion/sort pseudo-states
to the ordinary visual selector engine.

## Adaptive Styles

`Style.Condition` accepts a typed `AdaptiveCondition` with minimum/maximum viewport width or
height, display scale, theme variant, and input modality. Values on one condition are ANDed;
separate conditional styles compose through normal specificity and declaration order. Condition
changes invalidate only their attached style scope and use the ordinary value layer, so losing a
condition restores the next applicable value. Arbitrary expression conditions and per-frame
predicate polling are not supported.

## Events, Triggers, and Storyboards

An event attribute names a compatible method on the `x:Class` root. The compiler validates the
event and handler signature. Forma v1 adds public `Control.Attached` and `Control.Detached` events;
they fire when a control enters or leaves a `UIContext` and may be used like other CLR events.

`EventTrigger` resolves `SourceName` in the local namescope and validates `Event` on the source
type. `PropertyTrigger` uses a typed `Binding` and converts `Value` to the binding result type.
Trigger actions are `BeginStoryboard` and `StopStoryboard` in v1.

Storyboards are resources. V1 timelines target a local `x:Name` and a validated property path.
Timeline types are `FloatTimeline`, `ColorTimeline`, `Vector2Timeline`, and `ThicknessTimeline`.
Their keyframe values must match the target property type. Durations and keyframe times use
`TimeSpan`; easing names are validated against Forma's easing catalog.

Supported clock options are finite repeat counts or `Forever`, `AutoReverse`, and `FillBehavior`
values `Stop` and `HoldEnd`. `UIContext.Update(GameTime)` advances clocks deterministically.
Animation values overlay but do not write through a two-way binding source. Stopping a clock or a
`Stop` fill restores the underlying value.

## Supported XAML Directives

Forma v1 supports these directives in the XAML language namespace:

- `x:Class` on a compiled view root.
- `x:Name` on objects participating in a namescope.
- `x:Key` on resource dictionary entries.
- `x:DataType` on a binding scope.

The following are not supported in v1: `x:Arguments`, `x:FactoryMethod`, `x:TypeArguments`,
`x:Shared`, `x:Uid`, `x:Reference`, `x:Null`, `x:Type`, `x:Static`, `x:Code`, `x:Subclass`,
`x:FieldModifier`, `x:ClassModifier`, `x:Members`, and `x:Property`. Unknown directives and use of a
supported directive in an invalid location are diagnostics.

## Diagnostics

Every parser, schema, semantic, binding, style, trigger, and emission diagnostic has a stable
`FXAML` code, severity, project-relative file path, one-based line and column, and concise message.
Where practical it also includes an end position and related location. MSBuild, CLI text, JSON,
SARIF, and LSP output use the same diagnostic catalog.

Errors prevent emission or replacement. Warnings do not change semantics and may be promoted to
errors by project policy. The compiler does not downgrade unknown types, members, events,
resources, names, binding paths, selectors, directives, or incompatible values to runtime errors.

During hot reload, diagnostics leave the currently attached tree untouched. A later valid edit is
compiled independently and may replace it.

Diagnostic families:

| Code | Category |
| --- | --- |
| `FXAML1001`-`FXAML1004` | XML, root namespace, and directive errors |
| `FXAML2001`-`FXAML2002` | Duplicate or invalid names |
| `FXAML3001`-`FXAML3002` | Binding syntax and compiled-binding errors |
| `FXAML3501` | Resource key and lookup errors |
| `FXAML4001` | Selector errors |
| `FXAML5001` | Trigger errors |
| `FXAML6001` | Storyboard/timeline errors |
| `FXAML7001`-`FXAML7002` | IL emission and duplicate root-class errors |

## CLI and Language Server

The repository tool accepts files, directories, or projects and emits the same diagnostics used by
MSBuild:

```sh
dotnet run --project tools/Forma.Xaml.Tool/Forma.Xaml.Tool.csproj -- \
  validate --require-compiled-bindings --format human samples/Forma.Xaml.Game
dotnet run --project tools/Forma.Xaml.Tool/Forma.Xaml.Tool.csproj -- \
  validate --format json MyView.xaml
dotnet run --project tools/Forma.Xaml.Tool/Forma.Xaml.Tool.csproj -- \
  validate --format sarif MyProject.csproj
dotnet run --project tools/Forma.Xaml.Tool/Forma.Xaml.Tool.csproj -- watch MyProject.csproj
dotnet run --project tools/Forma.Xaml.Tool/Forma.Xaml.Tool.csproj -- schema --json
```

Start the language server with `forma-xaml lsp --stdio` (or the equivalent `dotnet run` command).
It discovers project references through Roslyn and supports diagnostics, completion, hover,
definition, references, rename, and formatting. V1 supplies the server protocol, not a bundled
editor extension.

## Hot Reload and AOT

Debug hot reload is opt-in and watches source files through `Forma.Xaml.HotReload`. Compilation
runs off-thread; a valid latest result is applied only during `UIContext.Update`. Replacement
retains the host slot and `DataContext`, then disposes old bindings, resource subscriptions,
styles, triggers, and clocks. Invalid edits report diagnostics without changing the live tree.
Burst saves are latest-wins.

Hot reload does not preserve references to old named controls, arbitrary code-behind control
state, focus/capture within the replaced subtree, or animation clock position. It is not supported
in trimmed or NativeAOT builds. Release, trimmed, and NativeAOT builds use only injected IL and may
not contain source XAML, watchers, SRE, XamlX, Cecil, or Forma compiler/hot-reload assemblies.

## Catalog Stories

The Catalog stories **Selector Styles**, **Template Systems**, **Composition Systems**,
**Storyboards and Triggers**, and **Compiled Data Binding** link to this guide. Their stable
identifiers are the kebab-case names prefixed with `catalog-`, such as `catalog-selector-styles`.

## Compatibility Matrix

| Concept | Forma XAML v1 | XAML 2006 / WPF / Avalonia comparison |
| --- | --- | --- |
| XML namespaces and `clr-namespace:` | Supported | Familiar syntax; Forma types and URI are distinct |
| `x:Class`, `x:Name`, `x:Key`, `x:DataType` | Supported | `x:Name` uses namescope lookup, not generated fields |
| Properties, property elements, content, events | Supported | Only public CLR members and Forma content contracts |
| Resources and merged dictionaries | Supported | Forma lookup and precedence rules apply |
| Visual selectors and lists | Supported | Forma grammar with ` `, `>`, `>>`, `:not`, and typed pseudo-states |
| Typed `OneTime`/`OneWay`/`TwoWay` binding | Supported | Requires `x:DataType` in Release; no reflection fallback |
| `RelativeSource` | Supported | Typed `Self`, `TemplatedParent`, and `FindAncestor` only |
| Static/dynamic resources | Supported | Forma value layers restore underlying values |
| Property/event triggers and storyboards | Supported | Deterministic `UIContext.Update` clocks |
| Control/data/items-panel templates | Supported | Explicit typed factories and local namescopes |
| `ItemsControl`, `ListBox`, virtualization | Supported | Explicit item templates and source-occurrence identity |
| `DataGrid` flat/hierarchical modes | Supported | Explicit typed columns; row virtualization; bounded non-virtualized columns |
| Adaptive style conditions | Supported | Typed viewport, scale, theme, and input conditions |
| Core-only text | Supported | Explicit `SpriteFontAdapter`; no DynamicText dependency |
| Optional DynamicText | Companion package | Adds shaping/rasterization without changing XAML factories |
| Commands, element sources, multibinding | Not supported | Use typed view models and row-owned code-behind events |
| WPF/Avalonia namespace/source compatibility | Not promised | Forma XAML is its own dialect |

## Canonical Syntax Fixtures

The examples under **Proposed Capability Showcase** in
`plans/xaml-templates-items-and-virtualization-plan.md` are canonical fixtures for this contract.
Each must be copied into compiler golden tests, Catalog, and packed-consumer fixtures as its phase
lands and then kept compiling. Syntax may change only when this contract, the plan fixture, and its
typed behavior change together. The earlier declarative-XAML composition, resource, binding,
trigger, and storyboard examples remain canonical where they do not conflict with this contract.
