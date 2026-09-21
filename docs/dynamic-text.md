# Dynamic Text

Forma exposes the same text contracts in its MonoGame and FNA variants. Runtime-loaded text uses
`UIFontFace`, `DynamicUIFont`, `TextLayoutEngine`, and device-scoped glyph atlases. Existing
`SpriteFont` applications remain supported through `SpriteFontAdapter`.

Installing the runtime-matched dynamic package gives each new `UIContext` a packaged Inter
`DynamicUIFont` at 16 logical pixels. Set `FormaDynamicTextDefaultEnabled=false` in the application
project to disable this initializer. Core-only and opted-out applications continue to resolve only
explicitly assigned fonts. Applications can replace the default through `UIContext.Theme.FontFamily`
or call `DynamicTextDefaults.Install` before constructing contexts.

## Typed rich text and reveal

`TextBlock.Inlines` uses the existing typed `Span`, `Run`, `LineBreak` and
`InlineImage` tree; it does not require BBCode parsing. Author that tree in Forma
XAML. Spans inherit font, foreground, background, decoration, language, direction
and optional `Meta` values into their children. A font override supplies a real
font face/size, not a synthetic bold or italic approximation.

`VisibleCharacters` counts document graphemes, including combining sequences
across inline boundaries. A nonnegative count takes precedence over
`VisibleRatio`; the ratio is clamped to `[0,1]` and must be finite.
`CharactersBeforeShaping` truncates at the reveal boundary and can change layout.
The other existing reveal modes retain the complete shaped geometry and hide
unrevealed glyphs, backgrounds and decorations together. Hidden text has no
character bounds or active metadata region. Ellipsis is suppressed while reveal
is incomplete. Inline images participate through `AlternativeText`; an empty
alternative does not consume a character and remains visible under full reveal.

`GetMetaUnderPosition` takes a context-space point and queries the same visible
shaped rectangles used for drawing. `MetaClicked` follows the existing
pointer-press convention, with inherited opaque metadata as its argument. Hidden,
disabled, clipped-row and synthetic-ellipsis regions do not activate links.
Hosts decide what metadata means: Forma does not open URLs or dispatch external
actions automatically.

This path retains the current per-inline/chunk shaping model. It does not yet
provide paragraph-wide bidirectional reordering or joining across differently
styled runs, or distinct visual-glyph-order semantics for each glyph reveal
enum variant. Grapheme-atomic visibility across a style boundary is not a claim
of cross-style glyph joining. Locale-aware word navigation remains separate.
The runtime path uses no new reflection or dynamic compilation.

## Native IME input

On native macOS, editable controls use Option+Left/Right for word movement and
Command+Left/Right for line boundaries. In `TextEdit`/`CodeEdit`, Command+Up/Down
moves to document boundaries, while Option+Up/Down uses hard paragraph boundaries;
Command+Left/Right follows displayed rows when text wraps. Shift extends the
selection without moving its anchor. Word/line deletion uses the same modifier
policy. Windows/Linux retain Control-based word navigation. Plain character
movement respects grapheme boundaries even without an assigned font.

`UIComponent` connects native text input automatically; hosts should not forward a second
`GameWindow.TextInput` handler into its context. `SupportsTextComposition` reports whether the
runtime exposes real preedit, complete-string commits and native candidate-area placement.
`SupportsFocusedTextComposition` additionally checks the focused editor. The supported control is
editable `LineEdit`; `TextEdit` and other character consumers retain their ordinary text route,
but do **not** claim native composition or candidate-placement parity.

The source-built MonoGame SDL3 native backend carries `SDL_EVENT_TEXT_EDITING` separately from
`SDL_EVENT_TEXT_INPUT`. Preedit does not change `Text`, raise `TextChanged` or enter undo history.
A complete UTF-8 commit becomes one managed string and one LineEdit edit, including selected-text
replacement, surrogate pairs and combining sequences. SDL scalar selection offsets are converted
to UTF-16 at the MonoGame boundary. Existing per-character MonoGame subscribers still receive
UTF-16 characters after the complete-string event; Forma consumes only the latter on this route.
Native candidate navigation and Enter are not synthesized into LineEdit commits.

Candidate rectangles follow the shaped/scrolled caret, control transforms and `DisplayScale`.
They are sent in drawable pixels; MonoGame converts using the live drawable-to-window ratio
before calling SDL's point-space text-input-area API. `UIComponent` divides the drawable viewport
by `DisplayScale` when assigning the context's logical `ViewportSize`, so root layout and popup
limits remain in the same coordinates as the controls. Focus transfer and local cancellation clear
the native session rather than committing marked text. Window deactivation also resets retained
keyboard modifiers; reactivation restarts input for the still-focused editor.
Modifiers pressed during preedit remain tracked across commit, while candidate-navigation keys
remain owned by the IME. Deactivation cancels pointer selection, captured presses and active
drag/drop without synthesizing a release, click or drop; logical focus and the existing text
selection remain available when the window becomes active again.

Platform cancellation visits the complete retained visual tree, not only the focused/captured
control: scroll observers, nested split handles and hosted `SubViewportContainer` contexts also
own gestures. Cancellation-only cleanup covers Slider, ScrollBar (including drag-node inertia
and pending smooth motion), SpinBox drag/repeat, ScrollContainer touch inertia, SplitContainer
and its helper draggers, ColorPicker (without flushing a deferred change or adding a recent
preset), tab reordering, Tree column/range/edit-button gestures, rich-text selection/autoscroll,
CodeEdit minimap, graph element/group/resize/connection/box/pan/minimap gestures, and list/dialog
multi-click tracking. LineEdit/TextEdit and BaseButton retain their existing cancellation path.
Controls with only immediate press actions or stateless release actions need no gesture flag
cleanup; cancellation never dispatches their release handler. Committed values, layout offsets,
selection and focus are retained. VirtualJoystick clears `IsPressed` but retains `Value`, without
synthesizing `Released` or `ValueChanged`. A later real gesture starts normally.

The APIs are discovered using typed reflection delegates with trimming annotations, not dynamic
code generation. Existing MonoGame 3.8.5 packages and FNA continue to compile and use their
character-only adapters. Browser/SDL2 and old native binaries report unsupported, not simulated
IME coverage. A new managed assembly alone does not upgrade an old native runtime. Event enum
values are appended and the existing native event union is unchanged; text payloads are copied
from platform-owned storage before the next poll. Rich events are opt-in, preserving legacy
native callers.

Focused regression commands, from the enclosing Textus checkout:

```sh
dotnet test Forma/tests/Forma.Tests/Forma.Tests.csproj \
  --filter 'FullyQualifiedName~NativeTextCompositionTest|FullyQualifiedName~PlatformInputCancellationTest|FullyQualifiedName~ModalInputBoundaryTest|FullyQualifiedName~UITest.LineEdit'
dotnet test Forma/tests/Forma.Tests/Forma.Tests.csproj -p:FormaRuntime=FNA \
  --filter 'FullyQualifiedName~NativeTextCompositionTest|FullyQualifiedName~PlatformInputCancellationTest|FullyQualifiedName~ModalInputBoundaryTest|FullyQualifiedName~UITest.LineEdit'
dotnet test MonoGame/Tests/MonoGame.Tests.DesktopGL.csproj \
  '-p:DefaultItemExcludesInProjectFolder=Assets/Projects/obj/**' \
  --filter 'FullyQualifiedName~TextCompositionIndexTest'
```

The MonoGame exclusion prevents earlier generated project-fixture assembly metadata from entering
the framework test compilation; it does not exclude any test source.

These are synthetic routing/Unicode/lifecycle regressions, **not** macOS IME acceptance.
Release acceptance still requires a newly source-bound SDL3 native build and the authorized
real-window session: select a real OS IME, compose without committed changes, navigate and commit
a candidate, cancel, switch between two LineEdits, move/resize the window and caret on Retina,
and leave/re-enter focus with modifiers held. That user-controlled run must verify the visible
candidate popup and retained text/undo behavior. Do not reuse earlier immutable graphics build
evidence for changed native source, or change system input settings without authorization.

## Packages and Deployment

Use the package matching the application's framework:

| Runtime | Native-free compatibility | Dynamic text |
| --- | --- | --- |
| MonoGame | `Forma.MonoGame` | `Forma.DynamicText.MonoGame` |
| FNA | `Forma.FNA` | `Forma.DynamicText.FNA` |

The core package is the native-free compatibility profile for restricted platforms and authorized
console ports. It does not include FreeType, HarfBuzz, or their native libraries. Trim-only and
NativeAOT package consumers are validated for both peers on macOS arm64, including native-free
SpriteFont and optional dynamic-text graphical profiles. Run
`bash scripts/test-nativeaot-package-consumer.sh` to reproduce the packed-artifact gate. Other RIDs
remain unsupported until equivalent executable gates pass. Actual console support remains
conditional on the selected MonoGame/FNA platform port, platform-holder approval, and validation on
authorized hardware.

The optional dynamic package resolves FreeType and HarfBuzz native assets for its declared runtime
identifiers. Publish and test every target RID in a clean environment; do not rely on system-installed
libraries. Ship font licenses and the repository third-party notices with redistributed fonts.
Forma selects the permissive FreeType License and HarfBuzz's MIT license. Binary redistribution
requires retaining their acknowledgments and notices, which `make compliance` enforces; neither
selected license requires a source offer. Modified or source redistribution must be reviewed against
the corresponding upstream terms rather than inferred from this binary-package conclusion.

### Internal Backend Boundary

`UIFontFace`, `DynamicUIFont`, and `TextLayoutEngine` do not expose FreeTypeSharp, HarfBuzzSharp,
native handles, or platform font types. `UIFontFace` delegates face metadata, character/glyph lookup,
metrics, variations, shaping, rasterization, diagnostics, and disposal to an internal backend
contract. The normal `Forma.DynamicText.<Runtime>` build selects `FreeTypeHarfBuzz` and preserves
the existing desktop package behavior.

Authorized source builds may set `FormaDynamicTextBackend=External` and provide
`FormaDynamicTextBackendSource` pointing to a source file compiled into `Forma.DynamicText`. That
file defines the internal `ExternalDynamicTextBackend` implementation. Selection is compile-time:
there is no assembly scanning, reflection activation, runtime generic construction, or public
backend API. The source remains in authorized infrastructure when it contains platform SDK details.

Run `make static-font-backend` to publish and execute the NDA-neutral platform-adapter spike for both
runtime peers. The gate verifies the unchanged public face API and rejects FreeType/HarfBuzz managed
dependencies and sidecar libraries from the spike output. This proves the replacement boundary and
packaging shape; target-specific font quality, lifecycle, and policy remain platform validation work.

`DynamicTextNativeDiagnostics.Current` reports the target RID, logical native library names, and
NuGet packaging sources without scanning loaded modules or exposing handles. Forma owns one direct
entry point, `FT_Set_Var_Design_Coordinates`, against FreeTypeSharp's `freetype` library name. All
other FreeType calls use FreeTypeSharp; shaping uses HarfBuzzSharp and its `libHarfBuzzSharp` native
assets. Forma owns pinned-memory, FreeType-library, and FreeType-face safe handles. HarfBuzzSharp owns
its blob, face, font, and buffer handles. This path registers no unmanaged callbacks and uses no
runtime-generated marshalling.

Missing, incompatible, or rejected native font libraries fail face creation with
`FontLoadException` and `FontLoadErrorCode.NativeFailure`. The public message is bounded and stable;
loader details remain available through `InnerException` for host diagnostics. Run
`make native-font-failures` to exercise missing files, invalid binaries, and valid libraries with
missing FreeType exports in fresh processes for both peers. Packed dynamic consumers also require
exactly one loaded FreeType and HarfBuzz module from their publish directory.

MGCB/XNB is not required for dynamic text. MonoGame MGCB SpriteFonts and FNA-compatible XNB
SpriteFonts are optional offline compatibility routes.

## Release Budgets

The dual-runtime render smoke enforces these deliberately conservative ceilings on supported
graphical CI hosts. The August 2026 Apple M4 Max baseline measured MonoGame/FNA respectively at
1.9/9.7 ms cold face load, 0.19/0.24 ms first shape, 0.24/0.27 ms first raster plus upload,
1.0/1.1 ms per 1,000 warm layout lookups, 6.4/2.5 ms per 100 warm draws, 4.6/5.5 ms fallback-heavy
layout, and 2.5/3.3 ms atlas churn.

| Operation | Release ceiling |
| --- | ---: |
| Cold face load | 1,000 ms |
| First shape | 500 ms |
| First glyph raster and upload | 500 ms |
| 1,000 warm layout lookups | 500 ms |
| Warm layout cache hit rate | at least 99% |
| 100 unchanged warm draws | 1,000 ms and zero managed allocation |
| Fallback-heavy layout | 500 ms |
| One-page atlas churn | 2,000 ms |
| Core managed assembly | 2 MiB |
| Dynamic-text managed assembly | 256 KiB |

The retained layout cache is bounded at 512 entries. Device-scoped Alpha8 atlases are bounded at
eight 2048x2048 pages and 32 MiB. Budget failures block release; measurements are performance gates,
not cross-machine throughput promises.

## Loading and Ownership

```csharp
using var latinFace = UIFontFace.FromProjectFile(projectDirectory, "Fonts/Inter-Regular.ttf");
using var arabicFace = UIFontFace.FromStream(File.OpenRead("Fonts/NotoSansArabic.ttf"));
var font = new DynamicUIFont(latinFace, 18, UIFontHinting.Default, arabicFace);

var label = new Label
{
    Text = "Forma مرحبا",
    UIFont = font,
    Language = "ar",
};
```

Faces can also be loaded from `ReadOnlyMemory<byte>`. Forma copies and pins bounded source bytes for
the native face lifetime. The application owns faces and must keep them alive while fonts or layouts
can use them, then dispose them idempotently. Controls, `DynamicUIFont`, and immutable `TextLayout`
instances do not own faces.

Fallback order is deterministic and resolved per grapheme cluster. Put the normal UI face first,
then script and emoji faces. Unsupported input reaches glyph 0 (`.notdef`) after the chain is
exhausted; it is not replaced with `?` and does not throw.

Use `UIFontHinting.Light` for small grayscale UI text that needs vertical pixel alignment while
preserving fractional horizontal advances and inter-glyph spacing. Dynamic glyphs rasterize at a
minimum 2x density and downsample linearly on 1x displays, avoiding low-resolution spacing artifacts
without changing HarfBuzz layout metrics.

## Layout and Display Density

Font sizes and all `TextLayout` geometry use logical UI units. `UIContext.DisplayScale` controls the
physical glyph raster size. Moving from 1x to 2x rerasterizes or reuses density-specific cache entries
without changing line breaks, caret positions, or logical bounds.

```csharp
var options = new TextLayoutOptions(
    maxWidth: 320,
    wrapping: TextWrapping.Word,
    direction: TextDirection.Auto,
    locale: "he");
var layout = ui.TextLayoutEngine.Layout(font, text, options);
var caret = layout.GetCaretPosition(utf16Index);
var hit = layout.HitTest(pointerInLayout);
var selection = layout.GetSelectionRectangles(startUtf16, lengthUtf16);
```

UTF-16 offsets, grapheme clusters, visual clusters, and glyph IDs are distinct. Use layout movement,
hit-testing, word-boundary, and selection APIs instead of incrementing code units or measuring
substrings.

## OpenType and Variable Fonts

`TextLayoutOptions.OpenTypeFeatures` accepts immutable four-character OpenType tags. Label forwards
features with `SetOpenTypeFeatures`. Variable coordinates belong to `DynamicUIFont` identity:

```csharp
var variable = new DynamicUIFont(
    face,
    24,
    UIFontHinting.Default,
    new[] { new UIFontVariationCoordinate("wght", 650) });
label.UIFont = variable;
label.SetOpenTypeFeatures(new[]
{
    new UIFontOpenTypeFeature("liga", 1),
    new UIFontOpenTypeFeature("kern", 1),
});
```

## Cache Budget and Recovery

Each `UIContext` owns a glyph cache per `GraphicsDevice`. The default hard limits are eight
2048x2048 Alpha8 pages and 32 MB. `UIContext.DynamicGlyphDiagnostics` reports pages, glyphs,
occupancy, hits, misses, uploads, evictions, failures, and bytes. Immutable page snapshots are
available through `GetDynamicGlyphAtlasPages`; `ClearDynamicGlyphCache` clears pages between draws.
Device reset recreates textures from retained grayscale pages. Active-frame budget exhaustion skips
the unavailable glyph, records a diagnostic, and retries normally on later frames instead of
allocating beyond the budget or terminating the process.

## SpriteFont Compatibility

Keep `SpriteFont` when the application needs a fixed glyph set, pixel-art sampling, a deterministic
offline atlas, minimal native dependencies, or legacy XNA-compatible deployment. Assigning a
control's existing `Font` property installs a cached `SpriteFontAdapter`; no source rewrite is
required.

Migrate without changing layout intent:

```csharp
// Before: offline SpriteFont.
var column = new VBoxContainer { Separation = 8 };
column.AddChild(new Label { Font = content.Load<SpriteFont>("UI"), Text = "Settings" });
column.AddChild(new Button { Font = content.Load<SpriteFont>("UI"), Text = "Apply" });

// After: retain the same controls and layout properties; change only font selection.
using var face = UIFontFace.FromProjectFile(projectDirectory, "Fonts/Inter-Regular.ttf");
var uiFont = new DynamicUIFont(face, 16);
column.Children.OfType<Label>().Single().UIFont = uiFont;
column.Children.OfType<Button>().Single().UIFont = uiFont;
```

During rollout, keep the original SpriteFont loaded and expose an application switch:

```csharp
void SelectCompatibility(bool compatibility)
{
    label.UIFont = compatibility ? new SpriteFontAdapter(spriteFont, 16) : dynamicFont;
}
```

The catalog header demonstrates this rollback path in both runtime hosts.

## Compatibility Policy

Forma 0.x preserves the parallel `Font` and `UIFont` properties during migration. Additive text
contracts are minor-version changes. Existing `Font` members will not be silently reinterpreted or
removed; any future obsoletion forwards through `SpriteFontAdapter` for at least one minor release,
and removal requires a documented major-version decision. Assigning both properties remains
last-assignment-wins.

## Catalog Stories

The exact typography story names are **Dynamic Sizes**, **Letter Spacing**, **Display Density**,
**Fallback Chain**, **Shaping and Features**, **Bidirectional Text**, **Wrapping and Selection**,
**SpriteFont Compatibility**, **Atlas Inspector**, and **Failure States**. Their stable identifiers
are the kebab-case names prefixed with `catalog-`, such as `catalog-fallback-chain`.
