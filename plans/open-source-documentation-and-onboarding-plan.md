# Open-Source Documentation and Onboarding Plan

## Objective

Make Forma straightforward to evaluate, install, learn, troubleshoot, and contribute to as an
open-source UI toolkit. A developer who is new to the repository should be able to choose the
correct MonoGame or FNA package family, run a minimal UI, understand the core layout model, find the
relevant control and XAML reference, and know where to ask for help without first learning Forma's
release engineering or validation architecture.

This plan reorganizes and extends the project's existing technical material. Forma already has
deep subsystem contracts for compiled XAML, dynamic text, runtime SVG, theme icons, runtime support,
and migration. The primary gap is not raw documentation volume; it is a clear path from first
contact to first success, followed by predictable navigation into the existing depth.

Package availability is part of the onboarding contract. NuGet.org is the canonical public registry
for Forma preview and stable packages because it works with the default .NET package source and
requires no consumer authentication. GitHub Actions artifacts remain the review surface before
publication, and GitHub Releases may archive the approved packages and release notes. GitHub
Packages is not required for ordinary consumers; it may be introduced later only for distinctly
versioned nightly or private builds.

## Guiding Principles

- **First success before architecture:** the front page should get a supported application running
  before explaining package internals, backend matrices, or release machinery.
- **Task-oriented navigation:** organize entry points around what readers want to accomplish rather
  than around repository projects or implementation subsystems.
- **Two runtimes, one learning model:** MonoGame and FNA instructions should differ only where setup,
  dependencies, or host integration genuinely differ.
- **Copyable and executable examples:** important snippets must come from projects or fixtures that
  CI compiles. Untested pseudocode does not satisfy a quick-start or guide requirement.
- **Defaults must be documented:** sizing, alignment, input, focus, theme, resource ownership, and
  disposal guidance should state default behavior and common surprises explicitly.
- **Progressive disclosure:** quick starts stay narrow; conceptual guides explain behavior; API
  reference provides exhaustive member detail; specialist contracts retain engineering depth.
- **One canonical answer:** overlapping documents link to one authoritative explanation rather than
  copying version-sensitive package lists, platform matrices, or numeric inventories.
- **Documentation is release surface:** broken links, stale snippets, missing package versions, and
  undocumented public controls are validation failures, not editorial cleanup deferred indefinitely.
- **Honest support claims:** clearly separate implemented, tested, platform-validated, preview,
  experimental, and unavailable capabilities.
- **Contribution should be reproducible:** contributor instructions use repository commands and
  describe the minimum focused checks expected for each kind of change.
- **Public packages should require no registry setup:** supported preview and stable releases belong
  on NuGet.org; authenticated feeds must not be part of the normal onboarding path.

## Decision Summary

- Add a documentation landing page at `docs/index.md` with routes for first-time users,
  control/XAML reference, optional features, troubleshooting, migration, and contribution.
- Rework the repository README into a concise product front door: purpose, status, supported
  runtimes, one minimal result, installation choices, screenshots, and links to deeper material.
- Create separate MonoGame and FNA quick starts backed by buildable sample projects or documentation
  fixtures.
- Add a shared first-UI guide and a first-XAML-view guide; neither should require the Catalog or the
  Signal Run sample to understand the basic path.
- Treat public package availability as a prerequisite for a canonical package quick start. Until
  the first tagged publication succeeds, document and test a clearly marked source-reference preview
  path.
- Publish tagged public packages to NuGet.org through GitHub Actions trusted publishing with OIDC
  and a protected GitHub `nuget-production` environment restricted to `v*` tags. Creating and
  pushing a release tag authorizes publication after validation; no manual environment reviewer is
  required. Do not store a long-lived NuGet API key when trusted publishing is available.
- Publish the complete validated runtime-peer manifest as one release operation, including
  `Forma.Svg.ThorVG.MonoGame`, `Forma.Svg.ThorVG.FNA`, `Forma.Xaml.HotReload.MonoGame`, and
  `Forma.Xaml.HotReload.FNA`; do not leave one runtime family or optional backend at a different
  Forma version.
- Prioritize layout and sizing, input and focus, controls and containers, styling and themes, data
  binding, and resource lifetime as the first conceptual guides.
- Build the documentation with Docfx's modern template and publish its static output to GitHub
  Pages. Pin Docfx in the repository's local .NET tool manifest so local and CI builds use the same
  version.
- Generate one searchable, runtime-neutral API reference from validated MonoGame release assemblies,
  XML documentation, and portable PDBs, and augment it with curated control overview pages. Runtime
  parity must pass before this representative API surface is published; generated member lists alone
  are not sufficient user documentation.
- Link Catalog stories and control reference pages in both directions where the hosting technology
  permits stable URLs.
- Add small focused samples between the quick start and the feature-dense Signal Run application.
- Add project-owned contribution, conduct, security, support, issue, and pull-request guidance.
- Remove or generate volatile numeric claims such as test totals, package totals, and icon counts
  when the exact number is not itself a compatibility contract.
- Publish the documentation as a versioned static site only after local preview, link validation,
  and snippet/sample gates are reproducible in CI.

## NuGet Publication Contract

### Registry and Ownership

- NuGet.org is the source of record for public preview and stable packages.
- The Zigrok NuGet.org organization owns every package. While Forma is a single-maintainer project,
  the project owner is intentionally its sole administrator.
- Keep Microsoft/NuGet.org account recovery methods and MFA recovery material outside the repository.
  Recover lost access through the account provider and NuGet.org support. If project maintenance is
  transferred, add the successor to the organization and transfer package ownership before removing
  the original administrator.
- The canonical source repository is `zigrok/Forma`; package metadata, Source Link, trusted
  publishing, release links, and documentation must use that owner and repository identity.
- Exact package IDs must be checked immediately before the first push. Publishing claims an ID, but
  availability checks do not reserve it.
- Request a `Forma.*` ID-prefix reservation after the initial packages and project identity provide
  sufficient evidence for NuGet.org review. Prefix reservation is desirable but does not block the
  first tagged preview.
- GitHub Packages, if introduced, must use versions such as `0.1.0-nightly.YYYYMMDD.N` that cannot
  collide with NuGet.org preview or stable versions.

### Initial Public Package Manifest

The first public preview publishes these runtime peers at one shared Forma version:

| Capability | MonoGame package | FNA package |
| --- | --- | --- |
| Core UI | `Forma.MonoGame` | `Forma.FNA` |
| Dynamic text | `Forma.DynamicText.MonoGame` | `Forma.DynamicText.FNA` |
| Media | `Forma.Media.MonoGame` | `Forma.Media.FNA` |
| Skia SVG | `Forma.Svg.Skia.MonoGame` | `Forma.Svg.Skia.FNA` |
| ThorVG SVG | `Forma.Svg.ThorVG.MonoGame` | `Forma.Svg.ThorVG.FNA` |
| Compiled XAML | `Forma.Xaml.Build.MonoGame` | `Forma.Xaml.Build.FNA` |
| XAML hot reload | `Forma.Xaml.HotReload.MonoGame` | `Forma.Xaml.HotReload.FNA` |

`Forma.Svg.ThorVG.*` remains experimental and initially carries native assets only for the RIDs
declared in the runtime support contract, currently `osx-arm64` and `linux-x64`. Publication must not
imply Windows, console, or other RID support. Each ThorVG package must contain only its matching
Forma ThorVG native assets and must not acquire a Skia dependency.

The packable `Forma.Svg.MonoGame` and `Forma.Svg.FNA` compatibility packages are excluded from the
first public preview. No previous public Forma release requires migration from those legacy IDs, so
publishing them would create permanent package identities without serving an existing consumer.
They may be reconsidered only if a future migration requirement is documented and approved.

### Trusted Publishing Workflow

The release workflow must preserve the existing validation-first behavior and add a separate publish
job with these controls:

1. Start from an explicitly selected release commit or tag and verify that the package version is
   identical across every manifest entry.
2. Run compliance, runtime parity, XAML, package-consumer, SVG-package-consumer, ThorVG native-asset,
   and applicable NativeAOT release gates.
3. Upload the exact `.nupkg` and `.snupkg` set as an auditable GitHub Actions artifact.
4. Restrict the protected `nuget-production` GitHub environment to `v*` tags. Creating and pushing
  the tag authorizes automatic publication after all required jobs pass.
5. Request `id-token: write` only in the publish job and exchange GitHub's OIDC token through
   `NuGet/login` for a short-lived NuGet.org API key.
6. Download and publish the previously validated artifact; never rebuild packages after validation.
7. Push an explicit manifest rather than a broad wildcard, fail on missing or extra packages, and
   treat an existing version as an error rather than silently using `--skip-duplicate`.
8. Verify NuGet.org validation/indexing and restore every published package from an empty cache.
9. Create the matching GitHub release and attach the reviewed package artifacts only after the
   NuGet.org publication result is known.

NuGet.org package versions are immutable and are normally unlisted rather than deleted. A failed or
incorrect release is corrected with a new version and, when necessary, unlisting plus an advisory;
the workflow must never overwrite an existing version.

## Documentation Toolchain Contract

Docfx is the documentation generator because it combines the project's Markdown guides with native
.NET API metadata generation, cross-reference resolution, search, and Source Link-aware source links
in one static site. The built-in modern template is the starting point; customization should remain
limited to Forma/Zigrok identity, navigation, version information, and accessibility-preserving
styles until user feedback demonstrates a need for a custom frontend.

The repository pins Docfx in `.config/dotnet-tools.json` and restores it with `dotnet tool restore`.
The canonical configuration lives at `docs/docfx.json`, consumes the task-oriented Markdown hierarchy
under `docs/`, writes generated API metadata beneath ignored `docs/api/`, stages references beneath
ignored `docs/_generated/`, and writes deployable site output beneath ignored `Artifacts/docs/`.
Docfx content and reference globs cannot traverse above the configuration directory, so the
in-docset intermediates are required.
The Makefile exposes production build, local preview, and validation commands without requiring a
global Docfx installation. Node.js or a separate JavaScript documentation application is not part of
the baseline toolchain.

Generated API metadata comes from the prebuilt MonoGame release-family `.dll`, XML documentation,
and portable PDB files after runtime API parity has passed. This makes the reference describe shipped
artifacts, enables Source Link-backed "View Source" links to `zigrok/Forma`, and avoids duplicate or
conflicting API pages for the binary-incompatible but public-API-equivalent MonoGame and FNA peers.
Curated package and runtime pages explain which package family consumers must select. If parity ever
permits a documented public API difference, generation must fail until that difference has an
explicit reference strategy; the site must not silently present one runtime as universal.

GitHub Pages hosts the public static output. Pull requests receive a downloadable or reviewable site
artifact before public deployment is enabled. The default documentation URL represents the current
supported release, while immutable release snapshots use versioned paths such as `/0.1/` and `/1.0/`;
default-branch previews remain visibly marked and must not replace stable content. Version selection
can be driven by a small generated manifest rather than introducing a second site framework.

Docfx warnings cover internal links, cross-references, and malformed content. A maintained external
link checker such as Linkspector or Lychee covers outbound URLs with an explicit retry and allowlist
policy for transient failures. The deprecated `gaurav-nelson/github-action-markdown-link-check`
action must not be introduced. MkDocs Material, Docusaurus, and VitePress were considered: each has
strong guide-authoring or versioning capabilities, but each would require a separate .NET API
generation pipeline and an additional Python or JavaScript toolchain without improving Forma's core
documentation requirements.

## Progress Dashboard

- [ ] Phase 0: Audience, NuGet Publication Contract, and Documentation Inventory
- [x] Phase 1: README and Documentation Front Door
- [x] Phase 2: Tested MonoGame and FNA Quick Starts
- [x] Phase 3: Core Conceptual Guides
- [x] Phase 4: Control and API Reference
- [x] Phase 5: Focused Example Gallery and Catalog Cross-Links
- [x] Phase 6: Contributor and Community Health
- [x] Phase 7: Documentation Site, Versioning, and CI Quality Gates

Check a phase only after every task and exit criterion in that phase is complete.

### Progress Tracking Workflow

Run the shared tracker at the start and end of implementation sessions:

```sh
bash scripts/track-plan.sh plans/open-source-documentation-and-onboarding-plan.md
```

Add newly discovered required work to this document. Do not mark a task complete because prose was
drafted; its links, commands, snippets, screenshots, and support claims must also pass the phase's
validation criteria.

## Success Criteria

- [x] A developer starting from a clean machine can follow either runtime quick start and display a
  working Forma UI without consulting repository source or internal build scripts.
- [x] The quick starts state prerequisites, supported package/source route, exact commands, expected
  result, and common failure recovery.
- [x] A reader can find installation, first UI, XAML, layout, input, styling, data binding, dynamic
  text, SVG, platform support, migration, troubleshooting, and contribution material from one
  documentation landing page.
- [x] The README explains what Forma is, its current release maturity, which runtime variants exist,
  and the shortest supported path to trying it.
- [x] Every supported installation snippet references packages or source projects that are actually
  available and validated from an empty package cache or clean checkout.
- [x] Every package in the initial public manifest, including both ThorVG peers, is owned by the
  Zigrok NuGet.org organization, published at the same version, indexed, and restorable without an
  authenticated package source.
- [x] Public publication uses a protected GitHub environment and NuGet.org trusted publishing; no
  long-lived registry credential is stored in the repository or GitHub Actions secrets.
- [x] The layout guide explains `Size`, `Width`, `Height`, `CustomMinimumSize`,
  `CustomMaximumSize`, size flags, parent-container ownership, margin, padding, content alignment,
  viewport size, and display scale with runnable examples and diagrams.
- [x] The input guide explains `UIContext.Update`, text input, focus, mouse filtering, keyboard
  interaction, clipboard capabilities, and host-specific adapters.
- [x] Every public control has a searchable API entry and a curated summary containing purpose,
  defaults, common properties/events, sizing behavior, related controls, and at least one C# or XAML
  example where applicable.
- [x] Every Catalog story links to a stable control or feature reference, and reference pages identify
  their corresponding story names.
- [x] At least six focused examples build for both runtimes or share one runtime-neutral core with
  thin peer hosts.
- [x] `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, and `SUPPORT.md` define project-owned
  policies and link from the README.
- [x] Issue and pull-request templates collect runtime, backend, operating system, reproduction,
  logs, visual evidence, test coverage, and compatibility impact where relevant.
- [x] CI rejects broken internal links, uncompilable documented code, failed quick-start builds,
  missing required reference metadata, and unclassified documentation drift.
- [x] Documentation is versioned with releases so users can distinguish the default branch from the
  latest stable or preview package contract.

## Non-Goals

- Rewrite specialist contracts that are already authoritative and current.
- Promise compatibility with WPF, Avalonia, MAUI, WinUI, Godot, or another XAML/UI framework.
- Publish NuGet packages without an explicit `v*` release tag.
- Use GitHub Packages as a required public source or publish the same package version to competing
  feeds with ambiguous restore behavior.
- Claim support for runtimes, backends, operating systems, AOT modes, or consoles that have not
  passed their owning validation gates.
- Generate reference pages for private implementation details or expose internal APIs merely to make
  documentation generation easier.
- Maintain exact test, package, control, story, or icon totals manually when they can be generated or
  omitted.
- Build a custom documentation engine when an established static documentation tool meets the
  versioning, search, API generation, and CI requirements.
- Replace the Catalog with prose. The Catalog remains the interactive visual reference; written
  documentation explains contracts, defaults, and integration.
- Treat community policy templates as legal advice. Security, conduct, and contribution policies
  require maintainer review before publication.

## Current State

### Existing Strengths

- `README.md` is a user-facing front door for source quick starts, runtime selection, documentation,
  Catalog, support, contribution, release status, migration, and licensing.
- `docs/index.md` routes readers through C#/XAML onboarding, concepts, troubleshooting, optional
  features, curated control reference, generated API reference, and contribution material.
- `docs/xaml-language.md` is a detailed language and tooling contract.
- The core conceptual guides document layout, controls, input, styling, binding, resource lifetime,
  and recurring troubleshooting with validated examples and explicit defaults.
- `docs/dynamic-text.md`, `docs/runtime-svg.md`, `docs/theme-icons.md`, and `docs/runtime-support.md`
  own detailed deployment, optional-feature, and support contracts.
- The Catalog provides a searchable interactive inventory shared by MonoGame and FNA.
- Signal Run demonstrates a realistic compiled-XAML application with peer hosts and Debug hot
  reload.
- Docfx publishes a searchable runtime-neutral API reference with immutable Source Link URLs and
  nine curated control-family pages.
- The Makefile exposes discoverable build, test, Catalog, packaging, formatting, compliance, and
  validation commands through `make help`.
- The tag-triggered release workflow validates peer versions, compliance, runtime parity, XAML,
  package consumers, NativeAOT, package checksums, and symbol-package selection before OIDC
  publication.
- `scripts/test-svg-package-consumers.sh` already packs and validates the MonoGame and FNA ThorVG
  companions in isolated consumers, including native failure modes and mixed-backend rejection.
- Project-owned contribution, conduct, security, support, issue, and pull-request guidance is live
  and linked from the repository front door.

### Gaps

- No tagged preview has been published, so package artifacts are auditable but not yet available
  through the default public .NET package source.
- Post-publication indexing and restore verification have not been exercised yet.
- The focused example gallery and its bounded sample CI do not exist yet.
- Existing public XML documentation debt remains below full type/member coverage even though new
  undocumented public APIs are rejected.
- Documentation deployment does not yet preserve immutable release snapshots or a version manifest.
- Package-based quick starts cannot become canonical until the first NuGet.org preview is indexed.

## Target Information Architecture

```text
README.md
  -> Try Forma
  -> Documentation
  -> Catalog
  -> Contributing and support

docs/index.md
  getting-started/
    monogame.md
    fna.md
    first-ui.md
    first-xaml-view.md
  guides/
    layout-and-sizing.md
    controls-and-containers.md
    input-and-focus.md
    styling-and-themes.md
    data-binding.md
    templates.md
    text-and-fonts.md
    images-and-svg.md
    hot-reload.md
    resource-lifetime.md
  reference/
    controls.md
    xaml.md
    package-matrix.md
    platform-support.md
  troubleshooting/
    common-errors.md
    runtime-mismatch.md
    native-dependencies.md
    rendering-and-input.md
  existing specialist and migration documents
```

Docfx navigation and configuration may introduce supporting files around this hierarchy, but the
reader-facing categories and canonical ownership should remain stable.

## Documentation Content Contracts

### Quick Starts

Each runtime quick start must include:

- supported .NET SDK and host runtime prerequisites;
- package installation or provisional source-reference setup;
- a complete project file;
- minimal `Game` initialization and `UIContext` ownership;
- viewport-size updates and resize behavior;
- input forwarding, including text input where applicable;
- a first control tree in C#;
- build and run commands;
- expected visible result;
- cleanup/disposal responsibility;
- the three most likely setup failures with corrective actions;
- a link to the equivalent XAML path.

### Conceptual Guides

Every guide should answer:

1. What problem does this system solve?
2. What are its defaults?
3. Which object owns the behavior?
4. Which properties or APIs are commonly used?
5. What changes inside a container, template, or runtime peer?
6. What is a minimal working C# and XAML example?
7. What are the common mistakes and diagnostics?
8. Which Catalog story demonstrates it?
9. Where is the exhaustive API or specialist contract?

### Control Reference

Each curated control entry should include:

- purpose and typical use;
- inheritance and closely related controls;
- default focus, sizing, alignment, and input behavior;
- high-value properties, methods, and events;
- content/template model and named parts when relevant;
- C# and XAML examples;
- accessibility behavior;
- runtime/platform limitations;
- Catalog category and story name;
- links to generated member reference.

## Phase 0: Audience, NuGet Publication Contract, and Documentation Inventory

### Tasks

- [x] Define primary audiences: evaluator, game developer, XAML user, control author, runtime/backend
  integrator, and contributor.
- [x] Write one representative first-success task and one failure-recovery task for each audience.
- [ ] Use the source-reference route only as a clearly labeled pre-publication fallback; make
  NuGet.org package references canonical after the first tagged preview is indexed.
- [x] Create the Zigrok NuGet.org organization with the project owner as its sole administrator for the current
  single-maintainer project, and record account recovery and future ownership-transfer procedures
  without committing credentials.
- [x] Recheck every initial package ID immediately before publication and record the result in the
  release evidence.
- [x] Finalize the fourteen-package initial public manifest, including both ThorVG and XAML
  hot-reload peers, while rejecting the two unused SVG compatibility package IDs.
- [x] Configure a NuGet.org trusted-publishing policy for GitHub owner `zigrok`, repository `Forma`,
  and workflow `.github/workflows/release.yml`, owned by the Zigrok NuGet.org organization and
  restricted to the protected `nuget-production` environment.
- [x] Restrict the GitHub `nuget-production` environment to `v*` tags without a manual reviewer so a
  release tag proceeds automatically only after validation succeeds.
- [x] Extend the release package job to pack, inspect, and upload both ThorVG peers, both XAML
  hot-reload peers, and their symbol packages where produced.
- [x] Run `scripts/test-svg-package-consumers.sh` or an equivalent release gate before publication,
  including native RID selection, absent/mismatched ABI failures, no-Skia checks, single-file
  behavior, and mixed-backend rejection.
- [x] Add a publish job that downloads the validated artifact, verifies it exactly matches the
  release manifest/version, obtains a short-lived credential through `NuGet/login`, and pushes to
  NuGet.org without rebuilding or skipping duplicate versions.
- [x] Add post-publication indexing and clean-cache restore checks for every manifest package.
- [x] Document correction, unlisting, ownership, symbol-package, and credential-compromise procedures.
- [x] Inventory every current README and `docs/` page with audience, canonical topic, status, and
  intended destination.
- [x] Inventory volatile values and duplicated package/platform claims across documentation.
- [x] Inventory public controls and map each to XML summary coverage, Catalog story, curated reference
  status, and example status.
- [x] Validate the Docfx decision with a minimal local spike that generates one API page from a
  release assembly/XML/PDB set, resolves its Source Link URL, builds the modern template, serves the
  site locally, and produces a deployable static artifact.
- [x] Establish terminology for stable, preview, experimental, platform-validated, and unsupported.

### Exit Criteria

- [x] The audience and content inventory is committed and has no unowned documentation page.
- [x] The sole-administrator recovery procedure, NuGet.org organization, trusted-publishing policy,
  and tag-restricted protected environment are active and tested without a long-lived API key.
- [x] One tagged workflow run publishes the exact manifest at one version, including both ThorVG
  and both XAML hot-reload peers, and attaches the same validated artifacts to its GitHub release.
- [x] Every published package restores from NuGet.org in a clean consumer without authentication;
  ThorVG consumers receive only supported RID assets and no Skia dependency.
- [x] The installation route used by quick starts is executable from a clean environment.
- [x] The documentation toolchain decision records alternatives, tradeoffs, and maintenance cost.
- [x] Every duplicated volatile claim has one proposed canonical source or a removal decision.

## Phase 1: README and Documentation Front Door

### Tasks

- [x] Rewrite the README opening around product purpose, maturity, supported runtimes, and a minimal
  visible result.
- [x] Add a short “Try Forma” route for MonoGame and FNA without embedding the entire quick start.
- [x] Keep runtime pairing and optional package warnings, but move detailed matrices behind links.
- [x] Retain Catalog screenshots and add concise captions describing what users can inspect.
- [x] Create `docs/index.md` with task-oriented navigation and clear experience levels.
- [x] Add “choose C# or XAML” and “choose MonoGame or FNA” decision points.
- [x] Link support, security, contribution, release notes, license, and compatibility status from the
  repository front door.
- [x] Remove stale manually maintained totals unless generated and contractually useful.
- [x] Add a local documentation preview command to `make help` after the site tool is selected.

### Exit Criteria

- [x] A reader can reach any primary documentation journey from the README in two link selections or
  fewer.
- [x] The README contains no unavailable package command presented as generally usable.
- [x] Internal links pass automated validation.

## Phase 2: Tested MonoGame and FNA Quick Starts

### Tasks

- [x] Add the smallest supported MonoGame host fixture used by the documentation.
- [x] Add the smallest supported FNA host fixture used by the documentation.
- [x] Share runtime-neutral Forma setup code where practical without hiding host responsibilities.
- [x] Document a C# first UI with one layout container, label, editable field, and button event.
- [x] Document viewport resize and `UIContext` disposal explicitly.
- [x] Add a first XAML view with matching build package, `x:Class`, namescope lookup, and typed binding.
- [x] Verify Debug hot reload separately from the minimum production XAML path.
- [x] Add clean-cache build and bounded startup checks for both quick-start fixtures.
- [x] Capture small, current screenshots of the expected result.
- [x] Add troubleshooting callouts for mixed runtime packages, missing native assets, absent content,
  unavailable graphics devices, and XAML diagnostics.

### Exit Criteria

- [x] Both C# quick starts build and execute from the documented commands on supported CI hosts.
- [x] Both XAML quick starts compile in Debug and Release, and Release output excludes development
  compiler/hot-reload artifacts according to existing XAML gates.
- [x] Package-based instructions restore from an empty cache when public packages become canonical.

## Phase 3: Core Conceptual Guides

### Tasks

- [x] Write `layout-and-sizing.md` with diagrams and examples for direct sizing, minimum/maximum
  constraints, container allocation, size flags, margin, padding, content alignment, viewport size,
  and display scale.
- [x] Write `controls-and-containers.md` explaining retained trees, parent ownership, common
  containers, scrolling, overlays, and selection criteria.
- [x] Write `input-and-focus.md` covering pointer hit testing, mouse filters, focus modes, keyboard,
  text input, clipboard, and host adapters.
- [x] Write `styling-and-themes.md` covering defaults, overrides, selectors, icons, templates, and
  inheritance.
- [x] Write `data-binding.md` as a task-focused companion to the XAML language contract.
- [x] Write `resource-lifetime.md` covering `UIContext`, graphics resources, fonts, SVG providers,
  device reset, and disposal ownership.
- [x] Create focused troubleshooting pages from recurring diagnostics and support questions.
- [x] Add reviewed diagrams only where they clarify ownership or layout behavior better than prose.
- [x] Cross-link each guide to relevant existing specialist contracts and Catalog stories.

### Exit Criteria

- [x] Every guide contains tested C# or XAML examples and documents defaults and common mistakes.
- [x] The layout guide directly answers when to use `Size`, `CustomMinimumSize`, maximum constraints,
  and size flags.
- [x] Input setup is validated for both peer hosts rather than inferred from one runtime.
- [x] Specialist documents remain canonical for detailed contracts and do not conflict with guides.

## Phase 4: Control and API Reference

### Tasks

- [x] Enable XML documentation output for all public runtime packages intended for reference.
- [x] Measure public type/member XML coverage and define an initial enforced threshold.
- [x] Document every public control type and high-value public member lacking a useful summary.
- [x] Build the canonical MonoGame release-family assemblies, XML documentation, and portable PDBs
  before Docfx metadata generation, and require runtime API parity to pass first.
- [x] Configure Docfx metadata from those prebuilt artifacts with stable `zigrok/Forma` Source Link
  URLs and runtime-neutral namespaces.
- [x] Generate one conceptual API reference rather than duplicate MonoGame/FNA pages, while clearly
  documenting package selection and failing if parity detects an unexplained public API difference.
- [x] Create curated control-family overview pages for text input, buttons, selection, containers,
  collections, dialogs, data display, graph/code controls, and media.
- [x] Generate or validate the mapping among public controls, Catalog stories, and reference pages.
- [x] Add default-value and support-limitation notes where generated signatures are insufficient.
- [x] Add accessibility contracts to applicable control pages.
- [x] Introduce API diff review for releases so removed or changed public members require migration
  notes and reference updates.

### Exit Criteria

- [x] Every public control appears in searchable generated reference and one curated family page.
- [x] Missing XML documentation fails the agreed coverage gate for newly added public APIs.
- [x] Runtime peer APIs resolve to one conceptual reference without obscuring package requirements.
- [x] Control-reference links and source links pass CI.

## Phase 5: Focused Example Gallery and Catalog Cross-Links

### Tasks

- [x] Add a settings or login form demonstrating layout, validation, focus, and two-way input.
- [x] Add a responsive HUD demonstrating anchors/containers, resize, and display scale.
- [x] Add a scrollable inventory demonstrating item templates and selection.
- [x] Add a dialog workflow demonstrating modal ownership and result handling.
- [x] Add a list/DataGrid example demonstrating typed data binding and observable updates.
- [x] Add a custom theme/control example demonstrating styles, templates, and default icons.
- [x] Add focused dynamic-text and runtime-SVG examples or convert existing snippets into executable
  fixtures.
- [x] Give every sample a short README with purpose, run commands, concepts, expected result, and
  links to guides/reference.
- [x] Add stable documentation identifiers to Catalog stories where needed.
- [x] Link Catalog stories to reference URLs and reference pages back to exact story names.
- [x] Add screenshot refresh and review ownership for user-facing examples.

### Exit Criteria

- [x] At least six focused examples build for both runtimes or use verified shared core code with thin
  peer hosts.
- [x] Examples are individually understandable and do not require reading Signal Run first.
- [x] Every example has bounded CI validation appropriate to its graphics/input needs.
- [x] Catalog/reference cross-links are complete for the supported public-control inventory.

## Phase 6: Contributor and Community Health

### Tasks

- [x] Add `CONTRIBUTING.md` covering setup, repository structure, runtime parity, focused/full tests,
  XAML formatting, generated assets, documentation, commit expectations, and pull-request scope.
- [x] Add a maintainer-reviewed `CODE_OF_CONDUCT.md` and enforcement contact/process.
- [x] Add `SECURITY.md` with supported versions, private reporting channel, response expectations, and
  disclosure policy.
- [x] Add `SUPPORT.md` distinguishing usage questions, bugs, security reports, runtime upstream
  issues, and unsupported platform requests.
- [x] Add bug, feature, documentation, and platform/backend issue forms.
- [x] Add a pull-request template with runtime/backend impact, tests, visual evidence, API changes,
  documentation, licensing/provenance, and generated-output checks.
- [x] Document good-first-issue and help-wanted labeling criteria.
- [x] Document release-note expectations and public API compatibility review.
- [x] Add an architecture/contributor map linking code ownership boundaries to focused validation
  commands.

### Exit Criteria

- [x] A clean-checkout contributor can build one runtime, run a focused test, format XAML, and prepare
  a compliant pull request solely from project-owned guidance.
- [x] Security and conduct contact routes have confirmed maintainers and are not placeholder text.
- [x] Issue forms collect enough environment and reproduction information to triage runtime-specific
  defects without an immediate clarification round.
- [x] Community files are linked from the README and recognized by the repository host.

## Phase 7: Documentation Site, Versioning, and CI Quality Gates

### Tasks

- [x] Pin Docfx in `.config/dotnet-tools.json` and add `docs/docfx.json`, top-level navigation, the
  modern template, ignored metadata beneath `docs/api/`, staged references beneath
  `docs/_generated/`, and site output beneath `Artifacts/docs/`.
- [x] Add `make docs`, `make docs-serve`, and `make docs-check` commands that restore and invoke the
  repository-local Docfx tool for production build, local preview, and validation.
- [x] Publish preview artifacts in pull requests or workflow artifacts before enabling public hosting.
- [x] Treat Docfx internal-link, cross-reference, and content warnings as CI failures; run Linkspector,
  Lychee, or another maintained external-link checker with a documented transient-failure policy.
- [x] Compile or import every important snippet from executable fixtures.
- [x] Build all quick-start and focused sample projects from clean caches in CI.
- [x] Add spell/style checks with a narrow project dictionary and no blanket suppression.
- [x] Validate package IDs and versions against generated build/package metadata.
- [x] Validate the NuGet.org publication manifest against packable project IDs so newly approved
  packages, including runtime/backend peers, cannot be omitted silently.
- [x] Generate inventory values from authoritative manifests where exact counts are intentionally
  displayed.
- [x] Add a control-story-reference completeness check.
- [x] Publish stable documentation through GitHub Pages, keep default-branch previews visibly
  separate, and generate immutable release paths plus a version manifest for supported package lines.
- [x] Add redirects and link-stability rules before publishing public URLs.
- [x] Include documentation changes in release checklists and release-note review.

### Exit Criteria

- [x] The documentation site builds deterministically from a clean checkout.
- [x] CI fails on broken internal links, missing pages, failed snippets, failed quick starts, or
  incomplete control mappings.
- [x] Published pages visibly identify their Forma version and support maturity.
- [x] API source links resolve to the release commit in `zigrok/Forma`, and GitHub Pages versioned
  navigation resolves correctly for a release tag and the default development preview.

## Validation Matrix

| Surface | Local validation | CI validation | Release evidence |
| --- | --- | --- | --- |
| Documentation site | Preview build and link check | Clean deterministic site build | Versioned artifact/site |
| MonoGame quick start | Clean restore, build, bounded run | Supported OS/backend cells | Screenshot and command log |
| FNA quick start | Clean restore, build, bounded run | Supported OS/backend cells | Screenshot and command log |
| C# snippets | Compile from fixture source | All runtime peers where relevant | Source-linked example |
| XAML snippets | Compiler/build fixture | Debug and Release peer builds | No development assets in Release |
| API reference | XML coverage report | Missing/new-public-doc gate | Versioned API pages |
| Control inventory | Catalog/reference mapping | Completeness and broken-link gate | Published control index |
| Package instructions | Empty-cache restore | Package-consumer workflow | Published package/version match |
| NuGet publication | Manifest and artifact inspection | Protected OIDC publish job | Indexed clean-cache restore |
| ThorVG packages | Isolated peer consumers and native inspection | Supported-RID, ABI, no-Skia, and mixed-backend gates | Both runtime peers at the shared version |
| Community files | Maintainer review | Required-file/link check | Repository host discovery |

## Risks and Mitigations

### Package Availability Blocks the Canonical Quick Start

Mitigation: publish tagged previews to NuGet.org through the protected trusted-publishing workflow.
Until that succeeds, test and clearly label a source-reference path without presenting it as the
final installation experience. Treat creation of an explicit `v*` tag as publication authorization
rather than coupling publication to ordinary branch or documentation work.

### An Incorrect or Partial NuGet Release Becomes Permanent History

Mitigation: validate one explicit package manifest and produce an auditable artifact before publishing,
publish without rebuilding, reject duplicate versions, and verify every peer afterward. Correct a
bad release with a new version and unlist the affected version when necessary; never assume a NuGet
package can be overwritten or routinely deleted.

### ThorVG Packages Overstate Platform Support or Carry the Wrong Backend

Mitigation: publish both runtime peers together, inspect their RID-native assets and dependencies,
run isolated package consumers, and retain the experimental support label. NuGet availability does
not expand the platform matrix beyond the runtime-support contract.

### Documentation Duplicates Fast-Moving Contracts

Mitigation: assign one canonical source for package matrices, platform support, XAML syntax, and
generated inventories. Guides summarize and link rather than reproduce exhaustive tables.

### Generated API Pages Are Technically Complete but Hard to Learn From

Mitigation: pair generated member reference with curated control-family pages, defaults, examples,
and Catalog links. Measure task completion, not only XML-comment percentage.

### Examples Become Additional Applications to Maintain

Mitigation: keep examples narrowly scoped, share runtime-neutral code, use thin peer hosts, and give
each example a bounded test. Reject examples that duplicate an existing fixture without teaching a
distinct task.

### Graphics Tests Cannot Execute on Every Documentation Host

Mitigation: separate compile, bounded startup, and pixel evidence. Run graphics-backed checks only
on declared supported CI cells and label compile-only platforms honestly.

### Community Policies Ship as Unowned Boilerplate

Mitigation: require named maintainer review, working contact routes, and project-specific response
expectations before checking the phase complete.

## Initial Priority Order

1. Establish Forma NuGet.org ownership and the protected OIDC trusted-publishing path.
2. Publish and clean-cache validate the fourteen-package peer manifest, including both ThorVG and
  both XAML hot-reload packages.
3. Deliver tested MonoGame and FNA quick starts against NuGet.org.
4. Replace the README's maintainer-first opening with a user-first front door.
5. Add `docs/index.md` and stable task-oriented navigation.
6. Publish layout/sizing and input/focus guides.
7. Generate API reference and curate control-family pages.
8. Add focused examples and Catalog/reference cross-links.
9. Add contributor/community-health documentation.
10. Enforce documentation correctness and versioning in CI.
