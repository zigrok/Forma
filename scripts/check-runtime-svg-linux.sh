#!/usr/bin/env bash
# Purpose: Run dedicated runtime SVG validation on Linux OpenGL/FNA and MonoGame Vulkan cells.
# Usage: `bash scripts/check-runtime-svg-linux.sh` on Linux, including inside the documented SDK container.

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"

if [[ "${FORMA_SKIP_APT:-0}" != "1" ]] && command -v apt-get >/dev/null 2>&1 && [[ "$(id -u)" == "0" ]]; then
  apt-get update
  apt-get install -y --no-install-recommends libegl1 libfreetype6-dev libgl1 libgl1-mesa-dri libglx0 libglx-mesa0 libharfbuzz-dev mesa-vulkan-drivers vulkan-tools xvfb x11-utils xauth
fi

software_icd="$(find /usr/share/vulkan/icd.d -name 'lvp_icd*.json' -print -quit)"
test -n "$software_icd"
export LIBGL_ALWAYS_SOFTWARE=1
export FNA3D_OPENGL_WINDOW_DEPTHSTENCILFORMAT=None

for runtime_name in MonoGame FNA; do
  dotnet test tests/Forma.Tests/Forma.Tests.csproj --configuration Release \
    -p:FormaRuntime="$runtime_name" --filter 'FullyQualifiedName~Svg' --nologo
done

SDL_VIDEODRIVER=x11 bash scripts/xvfb-run.sh -s '-screen 0 1440x900x24 +extension GLX' \
  dotnet run --project tests/Forma.SvgRenderSmoke/Forma.SvgRenderSmoke.csproj \
  --configuration Release -p:FormaRuntime=MonoGame

env -u VK_ICD_FILENAMES -u VK_DRIVER_FILES SDL_VIDEODRIVER=offscreen FNA3D_FORCE_DRIVER=OpenGL \
  bash scripts/xvfb-run.sh -s '-screen 0 1440x900x24 +extension GLX' \
  dotnet run --project tests/Forma.SvgRenderSmoke/Forma.SvgRenderSmoke.csproj \
  --configuration Release -p:FormaRuntime=FNA

VK_ICD_FILENAMES="$software_icd" VK_DRIVER_FILES="$software_icd" vulkaninfo --summary
VK_ICD_FILENAMES="$software_icd" VK_DRIVER_FILES="$software_icd" SDL_VIDEODRIVER=x11 \
  MonoGamePlatform=DesktopVK FormaNativeRuntime=true \
  bash scripts/xvfb-run.sh -s '-screen 0 1440x900x24 +extension GLX' \
  dotnet run --project tests/Forma.SvgRenderSmoke/Forma.SvgRenderSmoke.csproj \
  --configuration Release -p:FormaRuntime=MonoGame -p:MonoGamePlatform=DesktopVK -p:FormaNativeRuntime=true

printf 'Runtime SVG Linux OpenGL, FNA, and Vulkan cells passed.\n'
