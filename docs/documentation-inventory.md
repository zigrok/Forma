# Documentation Inventory

Inventory date: 2026-08-07.

This maintainer audit assigns ownership to every source-facing README and Markdown page under
`docs/`. Generated files under `Artifacts/` and `docs/api/` are excluded. Upstream submodule
READMEs are listed but remain externally owned.

Audience abbreviations: evaluator (E), game developer (G), XAML user (X), control author (A),
runtime/backend integrator (R), and contributor (C).

## Audience Tasks

| Audience | Representative first success | Representative failure recovery |
| --- | --- | --- |
| Evaluator | Run either first-UI route and see the documented editable form. | Use rendering troubleshooting to identify an unavailable graphics device or missing asset. |
| Game developer | Add one matching runtime family, forward host input and viewport size, and render a control tree. | Diagnose a mixed runtime package or missing native dependency without changing unrelated packages. |
| XAML user | Compile a typed view, resolve a named control, and observe a two-way binding update. | Use the stable `FXAML` diagnostic and project-relative source location to correct an invalid view. |
| Control author | Add a documented control or template with one Catalog story and focused behavioral coverage. | Resolve a control-story-reference mapping or XML documentation gate without lowering its baseline. |
| Runtime/backend integrator | Execute the declared host checks and record the exact platform/runtime/backend scope. | Convert an unavailable capability into the documented stable result instead of overstating support. |
| Contributor | Build one runtime, run the owning focused test, and prepare a scoped pull request. | Follow the architecture map to repair parity, generated-output, formatting, or documentation drift. |

## Page Ownership

| Page | Audience | Canonical topic | Status and destination |
| --- | --- | --- | --- |
| `README.md` | E, G, X | Product overview, runtime choice, validation entry points | Current repository front door |
| `plans/README.md` | C | Plan ownership and lifecycle | Current internal planning index; excluded from the site |
| `samples/Forma.Catalog/README.md` | E, G, A, C | Running and operating Catalog hosts | Current example guide |
| `samples/Forma.Xaml.Game/README.md` | G, X | Signal Run sample, XAML ownership, hot reload | Current end-to-end example guide |
| `src/Forma/README.md` | G | Minimal retained UI example | Stale MonoGame-only onboarding; merge into a future first-UI guide |
| `tests/Assets/Text/README.md` | R, C | Multilingual fixture provenance and regeneration | Current contributor reference |
| `tests/Assets/Video/README.md` | R, C | Video fixture provenance and deterministic generation | Current contributor reference |
| `external/ThorVG/README.md` | R, C | Upstream ThorVG documentation | Externally owned; link only from provenance documentation |
| `external/XamlX/README.md` | X, C | Upstream XamlX architecture | Externally owned; link only from compiler architecture documentation |
| `docs/index.md` | E, G, X, A, R, C | Documentation routes by task | Current site front door |
| `docs/layout-and-sizing.md` | G, X, A | Layout constraints, allocation, spacing, viewport scaling | Current conceptual guide |
| `docs/controls-and-containers.md` | G, X, A | Retained ownership, composition, projection, container choice | Current conceptual guide |
| `docs/input-and-focus.md` | G, A, R | Pointer, focus, keyboard, text, clipboard, host adapters | Current conceptual guide |
| `docs/styling-and-themes.md` | G, X, A | Theme inheritance, selectors, icons, templates | Current conceptual guide |
| `docs/data-binding.md` | G, X, A | Task-focused compiled binding workflow | Current conceptual guide; language contract owns syntax |
| `docs/resource-lifetime.md` | G, X, A, R | Context, font, SVG, device, and attachment ownership | Current conceptual guide; specialist contracts own details |
| `docs/troubleshooting/index.md` | G, X, A, R, C | Troubleshooting routes and report context | Current troubleshooting front door |
| `docs/troubleshooting/runtime-and-packages.md` | G, R, C | Peer, restore, native asset, and capability failures | Current troubleshooting guide; runtime contracts own support |
| `docs/troubleshooting/xaml-build-and-hot-reload.md` | X, A, C | XAML diagnostics, discovery, binding, and reload failures | Current troubleshooting guide; language contract owns semantics |
| `docs/troubleshooting/rendering-and-assets.md` | G, A, R, C | Blank output, assets, SVG, scale, and device failures | Current troubleshooting guide; specialist guides own limits |
| `docs/reference/controls/` | G, X, A | Nine curated control families, defaults, limits, accessibility, Catalog/API routes | Current curated reference; JSON manifest owns complete mapping |
| `docs/reference/testing.md` | G, A, C | Locator-based UI testing with Forma.Testing: finding, acting, actionability, waiting, and extending it per app | Current reference for the testing package |
| `docs/reference/accessibility.md` | G, A, R, C | The accessibility tree, the macOS platform bridge, role mapping, and checking it from outside the process | Current reference for platform accessibility; testing.md owns the in-process testing story |
| `docs/release-operations.md` | C | Tagged publication, correction, symbols, ownership, and credential recovery | Canonical release operations runbook |
| `docs/release-api-review.md` | C | Release public-API baseline, migration acknowledgements, and baseline rollover | Current maintainer/release procedure |
| `docs/authorized-host-checklist.md` | R, C | Approved runtime-host requirements | Current specialist host-integration checklist |
| `docs/dynamic-text.md` | G, A, R | Dynamic-font setup, shaping, caching, deployment | Current text and fonts guide |
| `docs/runtime-acquisition.md` | R, C | Runtime dependency selection and pins | Current architecture decision; package manifest is machine-owned |
| `docs/runtime-support.md` | E, G, R, C | Support terminology, runtime/platform/backend matrix, AOT | Canonical current support authority |
| `docs/runtime-svg.md` | G, A, R | Runtime SVG setup, security, caching, deployment | Current SVG guide; measured tables are dated evidence |
| `docs/runtime-svg-profile-v1.md` | A, R, C | Normative SVG feature and comparison profile | Current compatibility reference |
| `docs/svg-backend-migration.md` | G, R | Selecting explicit Skia or ThorVG packages | Current backend selection guide |
| `docs/svg-backend-rollout.md` | R, C | ThorVG rollout decision and qualification evidence | Dated release evidence |
| `docs/theme-icons.md` | G, A | Default icon policy, customization, regeneration | Current styling guide; manifest owns the icon count |
| `docs/thorvg-build-and-provenance.md` | R, C | ThorVG pin, provenance, ABI, build procedure | Current supply-chain reference |
| `docs/xaml-language.md` | X, A, C | Forma XAML language and tooling contract | Canonical current XAML reference |
| `docs/xaml-templates-migration.md` | X, A, C | Templates, items, and visual-tree migration | Current migration guide |
| `docs/control-template-migration-manifest.md` | A, C | Historical type-by-type template migration | Historical evidence; not the current API inventory |
| `docs/baselines/README.md` | C | Dynamic-text baseline regeneration | Current contributor validation guide |
| `docs/baselines/xaml-template-performance.md` | A, C | Deterministic performance gates and observations | Dated performance evidence |
| `docs/baselines/xaml-templates-items-and-virtualization.md` | A, C | Frozen pre-migration matrix | Historical baseline at its recorded commit |
| `docs/adr/0001-dynamic-text-api.md` | A, R, C | Dynamic-text API and ownership | Accepted architecture decision |
| `docs/adr/0002-dynamic-text-dependencies.md` | R, C | Native dependencies, Unicode baseline, RID matrix | Accepted desktop-spike decision; revalidate before expansion |
| `docs/adr/0003-dynamic-text-security-limits.md` | A, R, C | Dynamic-text work limits | Accepted normative decision |
| `docs/adr/0004-backend-neutral-drawing-and-compositing.md` | A, R, C | Drawing/compositing architecture | Accepted architecture decision |
| `docs/adr/0005-template-first-compatibility-and-lifetime.md` | A, C | Template ownership, compatibility, lifetime | Accepted architecture decision |
| `docs/adr/0006-runtime-svg-architecture.md` | A, R, C | Bounded SVG source/cache/upload architecture | Accepted; package ownership is superseded by ADR 0007 |
| `docs/adr/0007-explicit-svg-backends.md` | A, R, C | Explicit process-wide SVG backend selection | Accepted; measurements remain dated evidence |
| `docs/documentation-inventory.md` | C | Documentation ownership and drift audit | Canonical maintainer inventory |

## Volatile Claims

Do not duplicate the following values as undated prose. Link to or derive from the canonical source.

| Claim | Canonical source | Documentation rule |
| --- | --- | --- |
| Forma and runtime versions | `Directory.Build.props` | Versioned snippets describe a release; general guides link to the property source |
| Public package IDs | `scripts/release-packages.json` | Package matrices may render the manifest but must not own a separate list |
| Theme icon inventory | `assets/theme-icons/imports.json` | Generate counts; retain older counts only in clearly dated evidence |
| Public controls and members | Docfx metadata from release assemblies | Do not maintain prose totals |
| Catalog stories | `StoryCatalog.Create` plus `CatalogInventoryTest` | Do not maintain prose totals |
| Test totals | Test discovery and CI results | Front-door docs describe suites without fixed counts |
| Current platform/backend support | `docs/runtime-support.md` | Other guides link to this matrix instead of restating support claims |
| Runtime dependency contents | `docs/runtime-acquisition.md` and build properties | Support docs state only Forma's validated subset |
| Package sizes and benchmark values | Release artifacts and benchmark artifacts | Keep only with date, commit, environment, and artifact identity |

Normative limits, ABI versions, profile tolerances, and Unicode baselines are compatibility
contracts rather than volatile measurements. Change them through their owning ADR or profile.

## Public Control Coverage

`CatalogInventoryTest` reflects constructible public controls and requires one Catalog story for
each. Every strict documentation build then parses Docfx YAML, measures documented public types and
members, and verifies every reflected control story has both generated metadata and an HTML API
page. The generated `control-coverage.json` in the site artifact is the current count and mapping
authority.

The initial floors are 27.65% for public types and 11.57% for public members. They match the audited
baseline closely enough that one newly undocumented public item fails rather than lowering the
baseline silently. Raising the floors requires adding useful XML summaries first; lowering them
requires explicit compatibility/documentation review. Abstract and owner-only controls do not need
synthetic standalone stories, but they remain part of XML/API coverage.

`docs/reference/control-families.json` maps every generated `Control` descendant to exactly one of
the nine curated family pages. The documentation gate compares that manifest with Docfx inheritance
metadata and requires every page in the built site; fixed type totals are generated, not maintained
in prose.

`docs/reference/documentation-baseline.json` is the reviewed initial inventory of public Docfx UIDs.
It records legacy XML-documentation debt, not an allow-list to extend: any public UID absent from the
baseline must have a useful XML summary. The gate therefore rejects an undocumented API addition
even when another documented addition would keep aggregate coverage above its floor. Remove baseline
entries as existing APIs gain summaries.

## Audit Commands

```sh
# Source-facing Markdown pages
rg --files -g 'README.md' -g 'docs/**/*.md' -g '!Artifacts/**' -g '!docs/api/**'

# Drift-prone numeric and release claims
rg -n '\b[0-9]+\b.*\b(test|tests|icon|icons|package|packages|platform|RID|control|story)\b' \
  README.md RELEASE_NOTES.md docs

# Machine-owned icon inventory
jq '.Icons | length' assets/theme-icons/imports.json

# Generated XML coverage and control/story/API mapping (after `make docs`)
jq '{PublicTypes, DocumentedTypes, TypeCoveragePercent, PublicMembers, DocumentedMembers, MemberCoveragePercent, Controls: (.Controls | length)}' \
  Artifacts/docs/site/control-coverage.json

# Version and runtime pin authority
rg -n 'FormaVersion|MonoGameVersion|FnaVersion|FnaNativeAssetsVersion' Directory.Build.props
```
