#!/usr/bin/env bash
set -euo pipefail
FORMA_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DOTNET_ROOT="${DOTNET_ROOT:-/usr/local/share/dotnet}"
case "$(uname -s)-$(uname -m)" in
  Darwin-arm64) host=osx-arm64 ;;
  Linux-x86_64) host=linux-x64 ;;
  *) echo "Unsupported .NET Emscripten host: $(uname -s)-$(uname -m)" >&2; exit 1 ;;
esac
pack_version=10.0.10
emscripten_version=3.1.56
packs="$DOTNET_ROOT/packs"
sdk="$packs/Microsoft.NET.Runtime.Emscripten.$emscripten_version.Sdk.$host/$pack_version/tools"
test -x "$sdk/emscripten/emcc"
export DOTNET_EMSCRIPTEN_LLVM_ROOT="$sdk/bin"
export DOTNET_EMSCRIPTEN_BINARYEN_ROOT="$sdk"
export DOTNET_EMSCRIPTEN_NODE_JS="$packs/Microsoft.NET.Runtime.Emscripten.$emscripten_version.Node.$host/$pack_version/tools/bin/node"
export EM_CACHE="$packs/Microsoft.NET.Runtime.Emscripten.$emscripten_version.Cache.$host/$pack_version/tools/emscripten/cache"
export PATH="$packs/Microsoft.NET.Runtime.Emscripten.$emscripten_version.Python.$host/$pack_version/tools/bin:$sdk/emscripten:$sdk/bin:$PATH"
mkdir -p "$FORMA_ROOT/artifacts/browser/scratch" "$FORMA_ROOT/artifacts/browser/downloads"
export TMPDIR="$FORMA_ROOT/artifacts/browser/scratch"
cmake -S "$FORMA_ROOT/native/dynamictext/wasm" -B "$FORMA_ROOT/artifacts/browser/native-text" \
  -DCMAKE_TOOLCHAIN_FILE="$sdk/emscripten/cmake/Modules/Platform/Emscripten.cmake" \
  -DCMAKE_BUILD_TYPE=Release \
  -DFORMA_DOWNLOAD_DIR="$FORMA_ROOT/artifacts/browser/downloads"
cmake --build "$FORMA_ROOT/artifacts/browser/native-text" --parallel "${FORMA_BUILD_JOBS:-4}"
printf 'Native archives: %s\n' "$FORMA_ROOT/artifacts/browser/native-text/lib"
