# Continuous Integration Runners

The `CI` workflow runs on GitHub-hosted runners by default. Setting the repository variable
`TRACE_CI_RUNNERS` to `self-hosted` points the jobs at the local fleet documented in
`gh-runners-manager`; leaving it unset keeps the hosted setup, which is the default and the
supported configuration for forks.

## Where each job runs

| Job | Runner labels | Notes |
| --- | --- | --- |
| `lint` | `self-hosted, Linux, X64, yiazmat-linux-x64-emulated` | Pure managed build and analyzers. |
| `parity` | `self-hosted, Linux, X64, yiazmat-linux-x64-emulated` | Sequential runtime builds; the step is wrapped in `scripts/retry.sh` because a transient `GenerateDepsFile` file lock can survive one build boundary. |
| `runtime` (`ubuntu-latest` legs) | `self-hosted, Linux, X64, yiazmat-linux-x64-native` | Runs on the native x64 Linux server, where the Vulkan software renderer works. |
| `runtime` (`macos-latest` legs) | `self-hosted, macOS, ARM64` | Native arm64 macOS. |
| `runtime` (`windows-latest` legs) | placeholder job | No Windows machine is part of the fleet; the job reports the leg as not run so the required checks still resolve. |
| `thorvg-linux` | `self-hosted, Linux, X64, yiazmat-linux-x64-native` | Builds the ThorVG adapter and runs its tests inside the slot's own Docker daemon. |
| `thorvg-macos`, `nativeaot` | `self-hosted, macOS, ARM64` | Native arm64 macOS. |
| `package-consumers` | `self-hosted, Linux, X64, yiazmat-linux-x64-native` | Builds and inspects the shipped packages. |

## Known limits of the fleet

These are deliberate, documented limits — not silent skips:

- **MonoGame's Vulkan steps** (`MonoGamePlatform=DesktopVK`: the Vulkan catalog smoke and the
  swapchain smoke) are skipped on the fleet. MonoGame's desktop Vulkan backend aborts on llvmpipe
  over Xvfb with `Fatal : VkResult is c4653214 in vulkan/MGG_Vulkan.cpp:2226` and dies with
  `SIGSEGV` (exit 139); raising `/dev/shm` to 1 GB does not change it. Hosted CI keeps both steps.
  The Vulkan adapter itself is still built and tested on the native server by `thorvg-linux`.
- **Byte-exact font binaries**: the committed `tests/Assets/Fonts/*.xnb` files are compared only on
  hosted CI. The bytes follow the font build stack, not the architecture: a native x64 Linux build
  already differs from the committed file at byte 6 (the size field). The fleet sets
  `FORMA_SKIP_FONT_XNB_CMP` for that one comparison.
- **macOS Metal runtime**: the steps that need premake are skipped there; premake has no arm64
  build and the machine has no Rosetta. The jobs use the machine's Vulkan SDK for the Metal
  runtime build instead of installing one.

## Build hygiene

Every job exports `MSBUILDDISABLENODEREUSE=1`, `DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1` and
`DOTNET_PROCESSOR_COUNT=2`: reused MSBuild nodes have held `*.deps.json` files across builds and
broken the next `GenerateDepsFile`. Runs are serialized per working directory by the ephemeral
runner container, which serves exactly one job before it exits.
