# Runtime SVG

Forma provides bounded runtime SVG rendering through one explicitly selected runtime-matched Skia
or ThorVG companion. Core packages expose immutable source, profile, and cache contracts but do not
reference either renderer or its native assets.

## Setup

Reference exactly one backend matching the selected Forma runtime:

```xml
<PackageReference Include="Forma.MonoGame" Version="0.1.0-alpha.2" />
<PackageReference Include="Forma.Svg.ThorVG.MonoGame" Version="0.1.0-alpha.2" />
```

ThorVG is the default used by Forma's Catalog and SVG smoke hosts on validated macOS arm64 and
Linux x64 systems. Replace `ThorVG` with `Skia` to select the reference renderer explicitly. Use
`.FNA` peers together for FNA. Package builds install the selected backend automatically through a source module
initializer. Source-project consumers call the matching explicit installer:

```csharp
var health = SvgThorvgBackendDefaults.Verify();
if (!health.IsAvailable)
    throw new InvalidOperationException(health.Diagnostic);
```

Use `SvgSkiaBackendDefaults.Verify()` for Skia. `SvgRuntime.Health` reports stable backend ID,
registration, native availability/source, link mode, backend/profile versions, tested features, and
an actionable bounded diagnostic. Drawing without a backend fails explicitly. Selection is
process-wide and immutable after first parse; there is no cross-backend fallback. See the
[selection and migration guide](svg-backend-migration.md).

## Sources

`SvgImageSource` validates source bytes eagerly and is immutable after construction:

```csharp
var file = SvgImageSource.FromFile("Assets/icon.svg");
using var stream = File.OpenRead("Assets/badge.svg");
var streamed = SvgImageSource.FromStream(stream);
var memory = SvgImageSource.FromMemory(svgBytes);
```

The source exposes intrinsic size, view box, aspect-ratio metadata, content identity, element count,
and local-reference count. It owns a private copy of the bytes; callers retain ownership of input
streams and buffers. Source lifetime is independent of graphics devices.

`Image`, `InlineImage`, `ImageDrawing`, and `ThemeIcon` accept `ScalableImageSource`. Existing source
precedence is preserved: bitmap content wins over `DrawingImage`, which wins over scalable content.
Accessibility labels belong to the consuming control; SVG metadata does not replace an explicit
application label.

## Compiled XAML

Project SVG files are discovered as `FormaSvg` items, validated during the build, assigned stable
logical resource names, and embedded without MGCB or XNB:

```xml
<Image ScalableSource="../Assets/status.svg"
       Stretch="Contain"
       CustomMinimumSize="32,32" />
```

Relative paths resolve against the XAML document. Static missing, invalid, forbidden, oversized,
outside-project, and duplicate assets fail the build with `FXAML3601`, `FXAML3602`, or `FXAML3603`.
Compiled loads pass their owning assembly explicitly, including under NativeAOT; they do not
infer ownership by walking the calling stack. C# resource loads use `FromManifestResource`
or the `ParseSvgAsset` overload with an explicit assembly. The legacy one-argument converter
retains its managed calling-assembly behavior and is not a NativeAOT entry point.
Debug hot reload copies and watches SVG assets; changed bytes create a new content identity while
unaffected cache variants remain reusable.

## Exact-Scale Cache

The renderer computes the physical output dimensions from the complete logical transform and
`UIContext.DisplayScale`. The cache key contains source identity, exact physical width and height,
and render options. Selected validation scales are 1x, 1.25x, 1.5x, 1.75x, 2x, and 2.5x.

Validated documents and premultiplied RGBA rasters are CPU-owned. Atlas textures are device-owned,
created and uploaded on the render thread, and rebuilt after reset. Shared contexts reuse one
bounded cache per graphics device. Warm exact-size rendering performs no parsing, rasterization,
texture creation, upload, or managed allocation.

Defaults are at most 128 documents, four 2048x2048 RGBA pages, one pixel of transparent padding,
and a 16 MiB upload budget per frame. `UIContext.SvgRasterDiagnostics` reports entries, documents,
pages, bytes, hits, misses, parses, rasterizations, uploads, evictions, failures, and the last
failure. `GetSvgRasterAtlasPages`, `ClearSvgRasterCache`, and `PrewarmSvg` support inspection,
recovery, and predictable first use.

## Security and Validation Limits

All preflight validation runs before the selected backend parses the document. Violations produce
`SvgLoadException` with an `SvgLoadErrorCode`; no rejected document is forwarded to the backend or
triggers network I/O.

| Limit | Default | `SvgLoadOptions` property |
| --- | --- | --- |
| Source bytes | 4 MiB | `MaximumSourceBytes` |
| XML depth | 128 | `MaximumDepth` |
| Elements | 16,384 | `MaximumElements` |
| Attributes | 65,536 | `MaximumAttributes` |
| Text bytes | 1 MiB | `MaximumTextBytes` |
| Local references | 16,384 | `MaximumLocalReferences` |
| Dimension (width or height) | 16,384 px | `MaximumDimension` |
| Pixel area | 64 Mpx | `MaximumPixelArea` |

Pass a `SvgLoadOptions` to any `SvgImageSource` factory to override individual limits. Cache limits
are configured through `SvgRasterCacheOptions`: at most 8 pages, at most 4096×4096 per page, and at
most 64 MiB total across all pages.

Preflight rejects scripts, event attributes, animation, foreign objects, embedded raster images, data
URLs, external URLs and files, external stylesheets and documents, text and external fonts, filters
and blur, blend modes, ICC `color-profile` declarations, unsupported compositing operators, duplicate
element IDs, and cyclic local references.

## Threading, Lifetime, and Disposal

`SvgImageSource` is sealed, immutable, and safe to share across threads after construction. CPU
rasters and parsed documents are owned by the cache and accessed only from the render thread through
`UIContext` surface APIs. GPU atlas textures are created and uploaded on the render thread and
disposed with the owning graphics device.

ThorVG initialization is process-wide and explicit. Each ThorVG document serializes raster access;
Forma does not schedule SVG parse, raster, cancellation, or prewarm work in the background. The
conditional background-race matrix is therefore not applicable in Profile v1. A future background
pipeline must first add isolated-document concurrency and shutdown stress without moving texture
creation or upload off the render thread.

Multiple `UIContext` instances sharing a `GraphicsDevice` share one bounded `SvgRasterCache`. A
zero-owner shared cache is retained until device disposal; immediate last-context disposal is
intentionally deferred because FNA's SDL_GPU Metal path applies samplers lazily and provides no
portable post-present fence.

`UIContext.Dispose()` releases that context's renderer lease, but the shared zero-owner SVG cache is
retained until `GraphicsDevice.Disposing` so deferred runtime draws cannot reference prematurely
disposed atlas textures. Call `UIContext.ClearSvgRasterCache()` between draws to clear shared GPU
pages and CPU rasters during long-lived sessions when SVG content changes significantly.
`UIContext.GetSvgRasterAtlasPages()` returns immutable CPU-pixel snapshots.
`UIContext.PrewarmSvg(source, logicalSize)` queues a variant before the next `Draw`; the draw performs
the bounded render-thread upload before normal content rendering.

## Default Theme Policy

`UIContext.ThemeIconRenderingPolicy` accepts:

- `BitmapAtlas`: always use the embedded 1x/2x PNG atlases.
- `RuntimeSvg`: use authoritative companion SVG sources when the backend is healthy, with the PNG
  atlas retained as a per-icon fallback.
- `Auto`: select runtime SVG when its provider and backend are available, otherwise use PNG.

The approved MVP default remains `BitmapAtlas`, even after the complete release matrix passes.
Applications opt in with `RuntimeSvg` or `Auto`; this preserves native-free startup, deterministic
bitmap output, and immediate rollback. A missing companion never removes a default control icon.
`ThemeIconDiagnostics` distinguishes runtime SVG icon sources, PNG fallback events, atlas memory,
generations, and missing names.

## Supported Envelope

Runtime SVG Profile v1 accepts SVG geometry, paths, groups, local `defs`/`use`, dimensions and view boxes,
presentation attributes, supported inline CSS, transforms, gradients, local clips and masks,
opacity, strokes, fill rules, and `currentColor` used by the approved fixture set.

It rejects scripts, event attributes, animation, foreign objects, embedded raster images, data URLs,
external URLs or files, external stylesheets and documents, text and external fonts, filters and
blur, blend modes, ICC `color-profile` declarations, browser layout semantics, unsupported
compositing, and references that escape the document. Validation also enforces
finite source-byte, XML-depth, element, attribute, text, local-reference, dimension, and pixel-area
limits before native parsing. The feature is deliberately documented as a bounded SVG
subset, not full browser SVG support.

The normative feature list, fixtures, and cross-backend tolerances are documented in
[Runtime SVG Profile v1](runtime-svg-profile-v1.md).

## Validation Gates

| Host/backend | Runtime peer | Gate |
| --- | --- | --- |
| macOS Metal | FNA | Focused lifecycle smoke and Catalog baseline |
| macOS OpenGL | MonoGame | Focused lifecycle smoke and Catalog baseline |
| Linux OpenGL | MonoGame and FNA | `scripts/check-runtime-svg-linux.sh` |
| Linux Vulkan | MonoGame Native | `scripts/check-runtime-svg-linux.sh` on native x64 |
| Windows Direct3D | MonoGame and FNA | Windows CI focused lifecycle smoke |

The full matrix passed in [CI run 31110056321](https://github.com/zigrok/Forma/actions/runs/31110056321)
at implementation snapshot `cd94582436e3ad8065262d8f5c9507ea03d98abe`. The same run validated
official NuGet provenance, package consumers, runtime parity, trim-only publishing, and NativeAOT.

Catalog comparisons cover 1x, 1.25x, 1.5x, 1.75x, 2x, 2.5x, RTL, and narrow layouts. Runtime SVG
versus PNG aggregate coverage/color/edge metrics may differ by at most 3%; MonoGame versus FNA may
differ by at most 1%. Exact focused-smoke hashes remain a stronger diagnostic when peers use the same
graphics path.

## Catalog Baselines

Local macOS arm64 measurements from `scripts/test-dynamic-render-smoke.sh` and the Catalog smoke
runs. These are transient development baselines, not release gates; the CI cells in Validation Gates
are the authoritative gates.

| Metric | MonoGame (DesktopGL) | FNA |
| --- | --- | --- |
| Svg.Skia version | 5.2.0 | 5.2.0 |
| Theme SVG icons loaded | 67 / 67 | 67 / 67 |
| Bitmap fallbacks at 1x | 0 | 0 |
| SVG raster entries at 1x | 6 | 6 |
| SVG raster entries at 1.25x | 8 | 8 |
| Atlas bytes (single 2048×2048 page) | 16 MiB | 16 MiB |
| Edge transitions delta vs PNG (1.25x whole-Catalog) | −0.03 % | — |
| Edge strength delta vs PNG (1.25x whole-Catalog) | −0.13 % | — |

Cold parse and rasterize time are available through `SvgRasterCacheDiagnostics.ParseTime` and
`RasterTime`; run `scripts/test-dynamic-render-smoke.sh` and inspect the printed diagnostics for
current values on the local machine.

## Migration Examples

See [docs/examples/RuntimeSvgMigration.cs](examples/RuntimeSvgMigration.cs) for annotated C#
examples covering migration from a `Texture2D` atlas icon, a `ThemeIcon` bitmap-atlas consumer, and
a `DrawingImage`/`ImageDrawing` surface.

## Deployment

Svg.Skia 5.2.0/SkiaSharp 4.148.0 and ThorVG 1.1.0/Forma ABI 1 are pinned. Explicit companions carry
only their selected implementation; authoritative default-theme SVG sources live in core. Existing
`Forma.Svg.*` packages are warning-producing Skia compatibility packages for one migration window.
ThorVG provenance and reproducible build inputs are in
[ThorVG Build, Provenance, and Host Integration](thorvg-build-and-provenance.md).

The Catalog starts with a verified backend and includes the `Runtime SVG` story. It compares compiled
and file sources, arbitrary sizes and tint, LTR/RTL placement, theme SVG/PNG policies, cache
statistics, and rejected external input. Use `--theme-icon-policy BitmapAtlas`, `RuntimeSvg`, or
`Auto` for bounded automated captures.
