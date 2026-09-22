# Forma

Forma is an independent retained-mode UI toolkit for XNA-compatible runtimes. MonoGame and FNA use
the same `Forma` namespace, controls, layout behavior, styling model, and catalog stories. Each
artifact is compiled against exactly one runtime because the framework assemblies are source
compatible in many places but are not binary substitutes.

The first NuGet preview is being prepared. CI produces auditable package artifacts, and `v*` tagged
releases are configured to publish the exact validated artifact automatically through the protected
trusted-publishing environment. Until that first release is indexed, use the source build route
below.

Run `make help` for the common build, test, catalog, validation, packaging, and plan-tracking
commands.

## Try Forma

Run the same small C# interface with either runtime from a source checkout:

```sh
make quick-start-monogame
make quick-start-fna
```

![Forma C# quick start with a label, editable field, button, and status text.](docs/images/quick-start-monogame.png)

The [C# first-UI guide](https://zigrok.github.io/Forma/latest/getting-started/csharp-first-ui.html) explains the shared control tree,
font setup, viewport resizing, input forwarding, disposal, clean-cache smoke check, and expected
result. Choose the peer already used by the host game; the public APIs match, but binaries cannot be
mixed. Use the [documentation home](https://zigrok.github.io/Forma/latest/) to choose XAML, concepts, troubleshooting,
control reference, optional features, or contributor guidance.

## Choose a Runtime

Use one matching package pair and one framework implementation. Never mix runtime variants.

Use `Forma.MonoGame` with MonoGame or `Forma.FNA` with FNA. Every optional Forma package must carry
the same runtime suffix and version as core. The detailed peer, backend, and platform matrices live
in [runtime support](https://zigrok.github.io/Forma/latest/runtime-support.html).

The core and media packages contain assemblies named `Forma` and `Forma.Media` with public types in
the `Forma` namespace. Add the matching `Forma.DynamicText` companion only when using runtime font
loading, shaping, or rasterization; `SpriteFontAdapter` consumers remain native-text-free.
Package-owned build guards reject mixed variants with an actionable error.

Add exactly one matching explicit `Forma.Svg.Skia` or `Forma.Svg.ThorVG` companion for bounded
runtime SVG rendering. The unused `Forma.Svg.MonoGame` and `Forma.Svg.FNA` compatibility identities
are excluded from the first public release. Core packages remain free of both backends. See the
[runtime SVG guide](https://zigrok.github.io/Forma/latest/runtime-svg.html) for
source loading, compiled XAML assets, scaling, cache diagnostics, security limits, theme policy,
deployment, and rollback.

## Forma XAML

Forma XAML is an optional, Forma-native declarative UI language. After the first packages are
published, a MonoGame project will pair the following references. Release builds inject generated
IL and typed bindings into the application assembly; shipped applications do not contain source
XAML, XamlX, Cecil, a reflection binding engine, or a runtime XAML reader. Pair the private build
package with the selected runtime:

```xml
<PackageReference Include="Forma.MonoGame" Version="0.1.0-alpha.2" />
<PackageReference Include="Forma.Xaml.Build.MonoGame" Version="0.1.0-alpha.2" PrivateAssets="All" />
```

Use the `.FNA` peers for an FNA application. Project `.xaml` files are discovered automatically.
Views use `xmlns="https://forma.dev/xaml"`, an `x:Class` root that calls
`FormaXamlLoader.Load(this)`, and `x:DataType` for release-safe typed bindings. Named controls are
resolved with `NameScope.GetNameScope(view).Find<T>("Name")`; names do not generate fields.

The language includes direct-rendered primitives, brushes/effects, flex and explicit grid layout,
typed control/data/items-panel templates, presenters, visual selectors with explicit template
boundary traversal, adaptive conditions, `ItemsControl`, `ListBox`, flat/hierarchical `DataGrid`,
and bounded stack/grid virtualization. Item templates and data-grid columns are always explicit;
Forma performs no reflected model discovery or implicit closest-type template lookup.

The shared Signal Run sample demonstrates three compiled views, resources, selectors, one/two-way
bindings, deterministic storyboards, and Debug hot reload on both runtimes:

```sh
make xaml-game-monogame
make xaml-game-fna
make test-xaml
```

See the [XAML language guide](https://zigrok.github.io/Forma/latest/xaml-language.html) for setup, syntax, MSBuild/CLI/LSP usage,
diagnostics, hot-reload limits, AOT behavior, and the compatibility matrix. See
[samples/Forma.Xaml.Game/README.md](samples/Forma.Xaml.Game/README.md) for the playable sample.
Breaking custom chrome, row factory, visual-tree, and virtualization changes are covered by the
[template and items migration guide](https://zigrok.github.io/Forma/latest/xaml-templates-migration.html).

See the [dynamic text guide](https://zigrok.github.io/Forma/latest/dynamic-text.html) for runtime loading, fallback, logical DPI,
OpenType features, variable fonts, atlas budgets, deployment, disposal, migration, rollback, and
native-free platform guidance. MGCB/XNB SpriteFonts remain an optional compatibility route rather
than a prerequisite for dynamic text.

## Build

Build either runtime explicitly:

```sh
dotnet build src/Forma/Forma.csproj -p:FormaRuntime=MonoGame
dotnet build src/Forma/Forma.csproj -p:FormaRuntime=FNA
```

Add the corresponding `src/Forma.Media/Forma.Media.csproj` build when `VideoStreamPlayer` is
required. Validate both complete graphs, framework references, and public API parity with:

```sh
bash scripts/check-runtime-parity.sh
bash scripts/test-dynamic-render-smoke.sh
```

Package references are the default. For coordinated source development, replace the selected
package with an absolute path to a local runtime project:

```sh
MONOGAME_PROJECT="$(pwd)/../MonoGame/MonoGame.Framework/MonoGame.Framework.DesktopGL.csproj"
dotnet build src/Forma/Forma.csproj -p:FormaRuntime=MonoGame \
  -p:MonoGameProjectPath="$MONOGAME_PROJECT"

FNA_PROJECT="$(pwd)/../FNA/src/FNA.csproj"
dotnet build src/Forma/Forma.csproj -p:FormaRuntime=FNA \
  -p:FnaProjectPath="$FNA_PROJECT"
```

## Catalog

Launch either thin host over the same runtime-neutral catalog:

```sh
dotnet run --project samples/Forma.Catalog.MonoGame/Forma.Catalog.MonoGame.csproj \
  -p:FormaRuntime=MonoGame

dotnet run --project samples/Forma.Catalog.FNA/Forma.Catalog.FNA.csproj \
  -p:FormaRuntime=FNA
```

To run the catalog against the opt-in Retina support in the MonoGame fork instead of the NuGet
package:

```sh
git clone --branch develop https://github.com/zigrok/MonoGame.git ../MonoGame
MONOGAME_PROJECT="$(pwd)/../MonoGame/MonoGame.Framework/MonoGame.Framework.DesktopGL.csproj"
dotnet run --project samples/Forma.Catalog.MonoGame/Forma.Catalog.MonoGame.csproj \
  -p:FormaRuntime=MonoGame -p:MonoGameProjectPath="$MONOGAME_PROJECT"
```

The catalog enables `GraphicsDeviceManager.AllowHighDpi` when the selected MonoGame build exposes
it. Stock MonoGame 3.8.5 does not expose that property and keeps its existing behavior.

For the default sibling clone at `../MonoGame`, the equivalent shorthand is:

```sh
make catalog-monogame-local
```

Override `MONOGAME_PROJECT` when the fork lives elsewhere.

![MonoGame catalog](docs/images/catalog-monogame.png)

*The MonoGame host showing the shared control-story catalog and inspector.*

![FNA catalog](docs/images/catalog-fna.png)

*The FNA host rendering the same stories and runtime-neutral UI.*

The catalog stories are runtime-neutral, while the window title identifies the active runtime as
`Forma Catalog [MonoGame]` or `Forma Catalog [FNA]`. See
[samples/Forma.Catalog/README.md](samples/Forma.Catalog/README.md) for bounded metrics, screenshot,
render-parity, and native-backend commands.

Default control icons are embedded, density-aware, and independent of application content
pipelines. The Catalog activates the optional runtime SVG provider and exposes SVG/PNG policy
controls in its `Runtime SVG` story. See the [theme icons guide](https://zigrok.github.io/Forma/latest/theme-icons.html) for icon names,
ownership, density selection, overrides, suppression, diagnostics, and deterministic regeneration.

## Validation

```sh
# Unit and catalog inventory tests
dotnet test tests/Forma.Tests/Forma.Tests.csproj -p:FormaRuntime=MonoGame
dotnet test tests/Forma.Tests/Forma.Tests.csproj -p:FormaRuntime=FNA

# SVG subset only
dotnet test tests/Forma.Tests/Forma.Tests.csproj -c Release -p:FormaRuntime=MonoGame \
  --filter 'FullyQualifiedName~SvgBackendTest|FullyQualifiedName~SvgImageSourceTest|FullyQualifiedName~SvgRasterCacheTest'
dotnet test tests/Forma.Tests/Forma.Tests.csproj -c Release -p:FormaRuntime=FNA \
  --filter 'FullyQualifiedName~SvgBackendTest|FullyQualifiedName~SvgImageSourceTest|FullyQualifiedName~SvgRasterCacheTest'

# Peer catalog presentation
bash scripts/check-catalog-render-parity.sh

# FNA Theora decoding
bash scripts/check-fna-video-smoke.sh

# Core package consumers, compiled-XAML empty-cache consumers, determinism, and conflict guards
bash scripts/test-package-consumer.sh

# Complete fourteen-package release manifest, package inspection, and hot-reload consumers
bash scripts/pack-release-packages.sh

# macOS arm64 trim and NativeAOT compiled-XAML consumers (includes SVG companion cells)
bash scripts/test-nativeaot-package-consumer.sh
```

Graphics render tests execute on supported Windows/Linux CI cells and compile on macOS, where NUnit
excludes fixture setup because SDL graphics-device creation must run on the process main thread.

### Before pushing

`make check-before-push` runs the subset of CI's gates that reproduce on a single machine: license
and SPDX compliance, documentation spelling/style, both-runtime build/test parity, the release
public API review, peer package budgets and isolated consumers, and (on macOS) trim/NativeAOT
consumers. It intentionally does not cover the `runtime` job's per-OS graphics smoke/render/video
matrix (needs Windows and Linux GPU drivers), the ThorVG native builds (`thorvg-linux`,
`thorvg-macos`), the full Docfx site build, or the external link check — those require CI's
OS matrix, native toolchains, or network access to run meaningfully. Running it before a push
catches the same compliance, parity, and packaging failures CI would, without waiting on
GitHub Actions.

See the [runtime support guide](https://zigrok.github.io/Forma/latest/runtime-support.html) for the graphics, content, effects, media,
native dependency, trimming, AOT, CI, and manual-gate matrix. See
[runtime acquisition](https://zigrok.github.io/Forma/latest/runtime-acquisition.html) for pinned distribution ownership.

## Contributing and Support

Start with [CONTRIBUTING.md](CONTRIBUTING.md) for setup, repository ownership boundaries, focused
validation, generated assets, and pull-request expectations. Use [SUPPORT.md](SUPPORT.md) to choose
between bug, documentation, feature, platform, security, and conduct routes.

Security reports use GitHub's private vulnerability-reporting flow described in
[SECURITY.md](SECURITY.md). Community participation is governed by
[CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md). Current compatibility and release changes are recorded in
[RELEASE_NOTES.md](RELEASE_NOTES.md); Forma-authored code is available under the
[MIT License](LICENSE).

## Release and Migration

The `Release` workflow validates the fourteen-package manifest and NativeAOT evidence before its
protected publish job can obtain a short-lived NuGet credential through GitHub OIDC. It downloads
and revalidates the auditable artifact instead of rebuilding, publishes without accepting duplicate
versions, verifies NuGet.org indexing and clean restores, and only then creates the GitHub release.
The `nuget-production` environment accepts only `v*` tags; creating a release tag authorizes
automatic publication after every required job passes. See the
[release operations runbook](https://zigrok.github.io/Forma/latest/release-operations.html) for preflight, correction, symbols,
ownership, and credential recovery.

Before the first public peer release, replace unqualified `Forma` and `Forma.Media` package
references with one matching peer pair. The unqualified IDs are not aliases and must not select a
canonical runtime. Detailed steps are in the [runtime support guide](https://zigrok.github.io/Forma/latest/runtime-support.html).

Existing `Font` properties remain source-compatible through `SpriteFontAdapter`. Dynamic migration
uses the parallel `UIFont` property and does not require changing control-tree layout intent. Fixed
glyph sets, pixel art, deterministic offline atlases, minimal native dependencies, and legacy XNA
projects may continue to prefer SpriteFont.

The template-first release separates semantic owners from replaceable visuals. Application code
that traversed widget internals, custom-drew outer chrome, or supplied C# item-row factories must
migrate to named parts/presenters, XAML `ControlTemplate`, and explicit `DataTemplate` contracts.

## Licensing

Forma-authored portions are available under the MIT License. Adapted and third-party portions keep
their original terms and attribution; see [NOTICE.md](NOTICE.md) and
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). These records do not constitute legal clearance.
