# Contributor Architecture

Forma keeps public UI behavior runtime-neutral while producing separate MonoGame and FNA binaries.
The runtime packages share the `Forma` namespace and API surface but are not binary substitutes.

## Ownership Boundaries

| Area | Primary paths | Focused validation |
| --- | --- | --- |
| Core controls, layout, input, themes | `src/Forma`, `tests/Forma.Tests` | `make test-unit`, `make parity` |
| Dynamic text and Unicode | `src/Forma.DynamicText`, `tools/Forma.UnicodeGenerator` | `make test-unit`, `make unicode-verify` |
| Media | `src/Forma.Media`, video fixtures | `make video-smoke`, `make parity` |
| XAML compiler/build/hot reload | `src/Forma.Xaml.*`, `tests/Forma.Xaml.*` | `make test-xaml`, `make format-xaml-check` |
| HTML and CSS authoring front end (`.fhtml` to canonical XAML) | `src/Forma.Xaml.Compiler/Html`, `docs/html-css-dialect.md`, `plans/html-css-authoring-frontend-plan.md`, `FormaHtml*Test` in `tests/Forma.Xaml.Compiler.Tests` | `make test-xaml`; `forma-xaml format --check` |
| SVG API and Skia backend | `src/Forma.Svg`, SVG consumer tests | `make svg-selection`, `make svg-packages` |
| ThorVG backend/native ABI | `src/Forma.Svg.ThorVG`, `native`, `external/ThorVG` | `make thorvg-render`, `make thorvg-nativeaot` |
| Catalog and visual stories | `samples/Forma.Catalog*`, render tests | `make smoke`, `make render-parity` |
| End-to-end XAML sample | `samples/Forma.Xaml.Game*` | `make test-xaml` |
| Packages and release | project pack metadata, `scripts/release-packages.json` | `bash scripts/pack-release-packages.sh` |
| Documentation and API reference | `docs`, XML comments, Docfx config | `make docs-check` |
| Licenses and provenance | legal files, package payloads, asset manifests | `make compliance` |

## Runtime Peer Rule

Shared projects receive `FormaRuntime=MonoGame` or `FormaRuntime=FNA` and select exactly one framework
reference. Runtime adapters live behind narrow compile-time boundaries. A change to public shared API
must produce the same documented signatures in both peers; `scripts/check-runtime-parity.sh` is the
authority.

## Data and Resource Ownership

`UIContext` owns UI traversal and device-bound caches; controls own their retained child structure;
applications own resources they pass unless an API explicitly transfers ownership. Build-time XAML
injects production loaders into application assemblies. Debug hot reload is a separate package and
must not leak into release output. Native backends must fail explicitly when ABI or RID requirements
are unmet and may not silently select another renderer.

## Generated Surfaces

Theme icons, Unicode data, packages, API YAML, screenshots, metrics, and baselines each have a named
generator or source manifest. Change the owner first and run its verification command. Files under
`Artifacts/`, `docs/api/`, and `docs/_generated/` are outputs and are not reviewed as source.

## Review Expectations

Cross-boundary changes need the focused checks for every affected owner. Public API changes also
need parity and documentation. Rendering changes need visual evidence from both peers or an explicit
reason a peer cannot execute on the current host. Native/dependency changes need provenance,
redistribution review, clean consumers, and supported-RID evidence.
