SHELL := /bin/bash
.DEFAULT_GOAL := help
.NOTPARALLEL:

DOTNET ?= dotnet
CONFIGURATION ?= Debug
CATALOG_ARGS ?=
MONOGAME_PROJECT ?= $(abspath ../MonoGame/MonoGame.Framework/MonoGame.Framework.DesktopGL.csproj)
FNA_PROJECT ?= $(abspath ../FNA/FNA.Core.csproj)
PLAN ?= plans/dynamic-text-rendering-plan.md
TRACK_ARGS ?= --summary
NATIVEAOT_RUNTIME ?=
NATIVEAOT_PROFILE ?=
NATIVEAOT_MODE ?=

MONOGAME_CATALOG := samples/Forma.Catalog.MonoGame/Forma.Catalog.MonoGame.csproj
FNA_CATALOG := samples/Forma.Catalog.FNA/Forma.Catalog.FNA.csproj
MONOGAME_QUICK_START := samples/Forma.QuickStart.MonoGame/Forma.QuickStart.MonoGame.csproj
FNA_QUICK_START := samples/Forma.QuickStart.FNA/Forma.QuickStart.FNA.csproj
MONOGAME_XAML_GAME := samples/Forma.Xaml.Game.MonoGame/Forma.Xaml.Game.MonoGame.csproj
FNA_XAML_GAME := samples/Forma.Xaml.Game.FNA/Forma.Xaml.Game.FNA.csproj
UNIT_TESTS := tests/Forma.Tests/Forma.Tests.csproj
RENDER_TESTS := tests/Forma.RenderTests/Forma.RenderTests.csproj
DOTNET_ARGS := --configuration "$(CONFIGURATION)" --nologo

.PHONY: help setup tools restore restore-monogame restore-fna \
	build build-monogame build-fna \
	test test-unit test-unit-monogame test-unit-fna \
	test-xaml test-xaml-monogame test-xaml-fna xaml-build-fixtures \
	test-render test-render-monogame test-render-fna \
	performance performance-graphics \
	catalog-monogame catalog-monogame-local catalog-fna catalog-fna-local \
	quick-start quick-start-monogame quick-start-fna quick-start-check \
	xaml-game-monogame xaml-game-fna smoke smoke-monogame smoke-fna render-parity video-smoke \
	text-spike text-spike-local text-baseline xaml-spike \
	format-xaml format-xaml-check \
	docs docs-quality docs-check docs-serve a11y-probe \
	compliance backend-references parity api-review packages aot-analyzers native-font-failures static-font-backend nativeaot check check-all check-before-push \
	svg-selection svg-benchmark svg-compare svg-packages thorvg-catalog thorvg-render thorvg-spike thorvg-linux thorvg-nativeaot thorvg-static-host icons icons-import icons-verify unicode unicode-verify track clean

help: ## Show available targets and configuration variables.
	@awk 'BEGIN { FS = ":.*## "; printf "Forma development targets\n\n" } /^[a-zA-Z0-9_.-]+:.*## / { printf "  %-24s %s\n", $$1, $$2 } END { printf "\nVariables: CONFIGURATION, DOTNET, CATALOG_ARGS, MONOGAME_PROJECT, FNA_PROJECT, PLAN, TRACK_ARGS, NATIVEAOT_RUNTIME, NATIVEAOT_PROFILE, NATIVEAOT_MODE\n" }' $(MAKEFILE_LIST)

setup: tools restore ## Restore local tools and both runtime dependency graphs.

tools: ## Restore repository-local .NET tools.
	$(DOTNET) tool restore

restore: restore-monogame restore-fna ## Restore both runtime dependency graphs.

restore-monogame: ## Restore the MonoGame catalog graph.
	$(DOTNET) restore $(MONOGAME_CATALOG) -p:FormaRuntime=MonoGame --nologo

restore-fna: ## Restore the FNA catalog graph.
	$(DOTNET) restore $(FNA_CATALOG) -p:FormaRuntime=FNA --nologo

build: build-monogame build-fna ## Build both complete runtime graphs.

build-monogame: ## Build the complete MonoGame graph.
	$(DOTNET) build $(MONOGAME_CATALOG) $(DOTNET_ARGS) -p:FormaRuntime=MonoGame
	$(DOTNET) build $(MONOGAME_XAML_GAME) $(DOTNET_ARGS) -p:FormaRuntime=MonoGame

build-fna: ## Build the complete FNA graph.
	$(DOTNET) build $(FNA_CATALOG) $(DOTNET_ARGS) -p:FormaRuntime=FNA
	$(DOTNET) build $(FNA_XAML_GAME) $(DOTNET_ARGS) -p:FormaRuntime=FNA

test: test-unit test-render ## Run unit and render tests for both runtimes.

test-unit: test-unit-monogame test-unit-fna ## Run unit tests for both runtimes.

test-unit-monogame: ## Run MonoGame unit and catalog inventory tests.
	$(DOTNET) test $(UNIT_TESTS) $(DOTNET_ARGS) -p:FormaRuntime=MonoGame

test-unit-fna: ## Run FNA unit and catalog inventory tests.
	$(DOTNET) test $(UNIT_TESTS) $(DOTNET_ARGS) -p:FormaRuntime=FNA

test-xaml: test-xaml-monogame test-xaml-fna xaml-build-fixtures ## Run Forma XAML runtime, compiler, tooling, sample, and build tests.

test-xaml-monogame: ## Run focused Forma XAML tests against MonoGame.
	$(DOTNET) test tests/Forma.Xaml.Tests/Forma.Xaml.Tests.csproj $(DOTNET_ARGS) -p:FormaRuntime=MonoGame
	$(DOTNET) test tests/Forma.Xaml.Tool.Tests/Forma.Xaml.Tool.Tests.csproj $(DOTNET_ARGS) -p:FormaRuntime=MonoGame
	$(DOTNET) test tests/Forma.Xaml.Game.Tests/Forma.Xaml.Game.Tests.csproj $(DOTNET_ARGS) -p:FormaRuntime=MonoGame

test-xaml-fna: ## Run focused Forma XAML tests against FNA.
	$(DOTNET) test tests/Forma.Xaml.Tests/Forma.Xaml.Tests.csproj $(DOTNET_ARGS) -p:FormaRuntime=FNA
	$(DOTNET) test tests/Forma.Xaml.Tool.Tests/Forma.Xaml.Tool.Tests.csproj $(DOTNET_ARGS) -p:FormaRuntime=FNA
	$(DOTNET) test tests/Forma.Xaml.Game.Tests/Forma.Xaml.Game.Tests.csproj $(DOTNET_ARGS) -p:FormaRuntime=FNA

xaml-build-fixtures: ## Validate compiled, invalid, incremental, PDB, and deterministic XAML builds.
	bash scripts/test-xaml-build-fixtures.sh

format-xaml: tools ## Format all repository XAML files.
	DOTNET="$(DOTNET)" bash scripts/format-xaml.sh --write

format-xaml-check: tools ## Check all repository XAML formatting without changing files.
	DOTNET="$(DOTNET)" bash scripts/format-xaml.sh --check

a11y-probe: ## Read a running app's accessibility tree from outside it (PID=... ARGS='press --name Save').
	@if [ -z "$(PID)" ]; then echo "PID is required: make a11y-probe PID=1234"; exit 1; fi
	@if [ ! -x tools/a11y-probe/.venv/bin/python ]; then \
		python3 -m venv tools/a11y-probe/.venv && \
		tools/a11y-probe/.venv/bin/pip install --quiet \
			pyobjc-framework-ApplicationServices pyobjc-framework-Quartz; \
	fi
	tools/a11y-probe/.venv/bin/python tools/a11y-probe/a11y_probe.py --pid $(PID) $(if $(ARGS),$(ARGS),dump)

docs: ## Build the Docfx guide and API site.
	bash scripts/build-docs.sh build

docs-quality: ## Check authored documentation spelling and Markdown structure.
	npm ci --ignore-scripts
	npm run docs:spell
	npm run docs:style

docs-check: docs-quality ## Validate prose, runtime parity, and Docfx with warnings as errors.
	bash scripts/build-docs.sh check

docs-serve: ## Build and serve the Docfx site locally (DOCS_PORT defaults to 8080).
	bash scripts/build-docs.sh serve

test-render: test-render-monogame test-render-fna ## Run render tests for both runtimes.

test-render-monogame: ## Run MonoGame render tests.
	$(DOTNET) test $(RENDER_TESTS) $(DOTNET_ARGS) -p:FormaRuntime=MonoGame

test-render-fna: ## Run FNA render tests.
	$(DOTNET) test $(RENDER_TESTS) $(DOTNET_ARGS) -p:FormaRuntime=FNA

performance: ## Run deterministic template, collection, selector, and virtualization invariants.
	bash scripts/check-xaml-performance-invariants.sh

performance-graphics: ## Run bounded compositor/effect and warm graphics-cache invariants.
	bash scripts/test-dynamic-render-smoke.sh

catalog-monogame: ## Launch the interactive MonoGame catalog.
	$(DOTNET) run --project $(MONOGAME_CATALOG) --configuration "$(CONFIGURATION)" -p:FormaRuntime=MonoGame $(CATALOG_ARGS)

catalog-monogame-local: ## Launch the catalog against a local MonoGame fork.
	@test -f "$(MONOGAME_PROJECT)" || { echo "MONOGAME_PROJECT does not exist: $(MONOGAME_PROJECT)" >&2; exit 2; }
	DOTNET="$(DOTNET)" bash scripts/run-catalog-local.sh MonoGame "$(MONOGAME_CATALOG)" "$(MONOGAME_PROJECT)" "$(CONFIGURATION)" -- $(CATALOG_ARGS)

catalog-fna: ## Launch the interactive FNA catalog.
	$(DOTNET) run --project $(FNA_CATALOG) --configuration "$(CONFIGURATION)" -p:FormaRuntime=FNA $(CATALOG_ARGS)

catalog-fna-local: ## Launch the catalog against a local FNA fork.
	@test -f "$(FNA_PROJECT)" || { echo "FNA_PROJECT does not exist: $(FNA_PROJECT)" >&2; exit 2; }
	DOTNET="$(DOTNET)" bash scripts/run-catalog-local.sh FNA "$(FNA_CATALOG)" "$(FNA_PROJECT)" "$(CONFIGURATION)" -- $(CATALOG_ARGS)

quick-start: quick-start-monogame ## Launch the C# quick start with the default MonoGame peer.

quick-start-monogame: ## Launch the C# quick start with MonoGame.
	$(DOTNET) run --project $(MONOGAME_QUICK_START) --configuration "$(CONFIGURATION)" -p:FormaRuntime=MonoGame

quick-start-fna: ## Launch the C# quick start with FNA.
	$(DOTNET) run --project $(FNA_QUICK_START) --configuration "$(CONFIGURATION)" -p:FormaRuntime=FNA

quick-start-check: ## Restore, build, and render both quick starts from empty package caches.
	FORMA_RUNTIME=MonoGame bash scripts/check-quick-start.sh
	FORMA_RUNTIME=FNA bash scripts/check-quick-start.sh

xaml-game-monogame: ## Launch the shared XAML sample with MonoGame.
	$(DOTNET) run --project $(MONOGAME_XAML_GAME) --configuration "$(CONFIGURATION)" -p:FormaRuntime=MonoGame

xaml-game-fna: ## Launch the shared XAML sample with FNA.
	$(DOTNET) run --project $(FNA_XAML_GAME) --configuration "$(CONFIGURATION)" -p:FormaRuntime=FNA

smoke: smoke-monogame smoke-fna ## Run both bounded catalog smoke checks.

smoke-monogame: ## Run the bounded MonoGame catalog smoke check.
	bash scripts/check-catalog-smoke.sh
	$(DOTNET) test tests/Forma.Xaml.Game.Tests/Forma.Xaml.Game.Tests.csproj $(DOTNET_ARGS) -p:FormaRuntime=MonoGame

smoke-fna: ## Run the bounded FNA catalog smoke check.
	FormaRuntime=FNA bash scripts/check-catalog-smoke.sh
	$(DOTNET) test tests/Forma.Xaml.Game.Tests/Forma.Xaml.Game.Tests.csproj $(DOTNET_ARGS) -p:FormaRuntime=FNA

render-parity: ## Compare deterministic catalog rendering across runtimes.
	bash scripts/check-catalog-render-parity.sh

video-smoke: ## Validate FNA Theora playback through Forma.Media.
	bash scripts/check-fna-video-smoke.sh

text-spike: ## Validate dynamic text shaping, rasterization, and atlas upload against both packages.
	bash scripts/check-dynamic-text-spike.sh

text-spike-local: ## Validate the dynamic text spike against packaged peers and a local FNA fork.
	@test -f "$(FNA_PROJECT)" || { echo "FNA_PROJECT does not exist: $(FNA_PROJECT)" >&2; exit 2; }
	FNA_PROJECT="$(FNA_PROJECT)" bash scripts/check-dynamic-text-spike.sh

text-baseline: ## Capture the pre-dynamic-text catalog screenshots and metrics at 1x and 2x.
	bash scripts/capture-dynamic-text-baseline.sh

compliance: ## Validate licenses, notices, and SPDX identifiers.
	bash scripts/check-compliance.sh

backend-references: ## Compile Forma.Media against supported MonoGame backends.
	bash scripts/check-backend-references.sh

parity: ## Build/test both runtimes and compare references and public APIs.
	bash scripts/check-runtime-parity.sh

api-review: ## Review the release public API surface against the acknowledged baseline.
	bash scripts/check-release-api.sh

packages: ## Build and validate all peer packages and isolated consumers.
	bash scripts/test-package-consumer.sh

aot-analyzers: ## Build fast source-linked trim/AOT analyzer consumers for both runtimes.
	bash scripts/check-aot-analyzers.sh

native-font-failures: ## Verify bounded missing, incompatible, and rejected native font failures.
	bash scripts/check-native-font-failures.sh

static-font-backend: ## Validate source-injected platform font backends without FreeType/HarfBuzz sidecars.
	FORMA_RUNTIME="$(NATIVEAOT_RUNTIME)" bash scripts/test-static-font-backend-spike.sh

nativeaot: static-font-backend ## Validate trim-only and NativeAOT package consumers on macOS arm64.
	NATIVEAOT_RUNTIME="$(NATIVEAOT_RUNTIME)" NATIVEAOT_PROFILE="$(NATIVEAOT_PROFILE)" NATIVEAOT_MODE="$(NATIVEAOT_MODE)" bash scripts/test-nativeaot-package-consumer.sh

xaml-spike: ## Validate XAML compiler feasibility and a compiler-free NativeAOT view.
	bash scripts/test-xaml-spike.sh

thorvg-spike: ## Build the pinned minimal ThorVG adapter and run its native smoke test.
	bash scripts/build-thorvg-spike.sh

thorvg-linux: ## Build and smoke-test the ThorVG adapter on clean Linux x64.
	bash scripts/check-thorvg-linux.sh

svg-selection: ## Validate process-isolated SVG backend selection failures.
	bash scripts/check-svg-backend-selection.sh

svg-benchmark: ## Measure both SVG backends over the 67-icon corpus.
	bash scripts/benchmark-svg-backends.sh

svg-compare: ## Compare isolated Skia and ThorVG profile/theme rasters and generate contact sheets.
	bash scripts/compare-svg-backends.sh

svg-packages: ## Pack and execute isolated SVG backend consumers.
	bash scripts/test-svg-package-consumers.sh

thorvg-render: ## Run ThorVG SVG GPU/cache/reset lifecycle smoke on MonoGame and FNA.
	bash scripts/test-thorvg-render-smoke.sh

thorvg-catalog: ## Run the bounded Runtime SVG Catalog story with ThorVG on both peers.
	bash scripts/test-thorvg-catalog.sh

thorvg-nativeaot: ## Publish and execute the ThorVG source-generated interop consumer with NativeAOT.
	bash scripts/test-thorvg-nativeaot.sh

thorvg-static-host: ## Publish and execute the NativeAOT direct-P/Invoke ThorVG static reference host.
	bash scripts/test-thorvg-static-host.sh

icons: ## Regenerate canonical 1x/2x default theme icon atlases.
	$(DOTNET) run --project tools/Forma.IconPipeline/Forma.IconPipeline.csproj -- generate

icons-import: ## Import mapped icons from GODOT_ROOT at the pinned revision.
	@test -n "$(GODOT_ROOT)" || { echo "GODOT_ROOT is required." >&2; exit 2; }
	$(DOTNET) run --project tools/Forma.IconPipeline/Forma.IconPipeline.csproj -- import "$(GODOT_ROOT)"

icons-verify: ## Regenerate and byte-compare committed icon outputs.
	$(DOTNET) run --project tools/Forma.IconPipeline/Forma.IconPipeline.csproj -- verify

unicode: ## Regenerate canonical Unicode 17 managed tables and conformance cases.
	$(DOTNET) run --project tools/Forma.UnicodePipeline/Forma.UnicodePipeline.csproj -- generate

unicode-verify: ## Download pinned Unicode sources and byte-compare generated outputs.
	$(DOTNET) run --project tools/Forma.UnicodePipeline/Forma.UnicodePipeline.csproj -- verify

check: compliance icons-verify unicode-verify parity test-xaml performance aot-analyzers native-font-failures ## Run the portable CI validation gates.

check-all: check backend-references smoke performance-graphics render-parity video-smoke packages nativeaot ## Run every validation, including graphical, package, and NativeAOT checks.

check-before-push: compliance docs-quality parity api-review packages nativeaot ## Run the CI checks practical to reproduce on a single machine before pushing.

track: ## Show plan progress; override PLAN and TRACK_ARGS as needed.
	bash scripts/track-plan.sh $(TRACK_ARGS) "$(PLAN)"

clean: ## Clean both runtime variants across the solution.
	$(DOTNET) clean Forma.slnx $(DOTNET_ARGS) -p:FormaRuntime=MonoGame
	$(DOTNET) clean Forma.slnx $(DOTNET_ARGS) -p:FormaRuntime=FNA