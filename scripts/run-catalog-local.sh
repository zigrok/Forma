#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 4 ]]; then
  printf 'Usage: %s <MonoGame|FNA> <project> <local-framework-project> <configuration> [-- dotnet arguments...] [-- catalog arguments...]\n' "$0" >&2
  exit 2
fi

runtime="$1"
project="$2"
local_framework_project="$3"
configuration="$4"
shift 4
if [[ "${1:-}" == "--" ]]; then
  shift
fi

dotnet_arguments=()
catalog_arguments=()
catalog_argument_count=0
parsing_catalog_arguments=false
for argument in "$@"; do
  if [[ "$argument" == "--" ]]; then
    parsing_catalog_arguments=true
  elif [[ "$parsing_catalog_arguments" == "true" ]]; then
    catalog_arguments+=("$argument")
    catalog_argument_count=$((catalog_argument_count + 1))
  else
    dotnet_arguments+=("$argument")
  fi
done

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_command="${DOTNET:-dotnet}"
project="$(cd "$(dirname "$project")" && pwd)/$(basename "$project")"
local_framework_project="$(cd "$(dirname "$local_framework_project")" && pwd)/$(basename "$local_framework_project")"

case "$runtime" in
  MonoGame)
    assembly_name="Forma.Catalog.MonoGame"
    framework_property="MonoGameProjectPath"
    bundle_identifier="dev.forma.catalog.monogame.local"
    ;;
  FNA)
    assembly_name="Forma.Catalog.FNA"
    framework_property="FnaProjectPath"
    bundle_identifier="dev.forma.catalog.fna.local"
    ;;
  *)
    printf 'Runtime must be either MonoGame or FNA.\n' >&2
    exit 2
    ;;
esac

"$dotnet_command" build "$project" \
  --configuration "$configuration" \
  -p:FormaRuntime="$runtime" \
  -p:"$framework_property=$local_framework_project" \
  -p:UseAppHost=true \
  "${dotnet_arguments[@]+"${dotnet_arguments[@]}"}" \
  --nologo

if [[ "$(uname -s)" != "Darwin" ]]; then
  if (( catalog_argument_count == 0 )); then
    exec "$dotnet_command" run --project "$project" \
      --configuration "$configuration" \
      -p:FormaRuntime="$runtime" \
      -p:"$framework_property=$local_framework_project" \
      "${dotnet_arguments[@]+"${dotnet_arguments[@]}"}" \
      --no-build
  fi
  exec "$dotnet_command" run --project "$project" \
    --configuration "$configuration" \
    -p:FormaRuntime="$runtime" \
    -p:"$framework_property=$local_framework_project" \
    "${dotnet_arguments[@]+"${dotnet_arguments[@]}"}" \
    --no-build \
    -- "${catalog_arguments[@]+"${catalog_arguments[@]}"}"
fi

output_directory="$(dirname "$project")/bin/$runtime/$configuration/net10.0"
application="$repository_root/Artifacts/apps/$assembly_name.app"
launcher="$application/Contents/MacOS/$assembly_name"

if [[ ! -x "$output_directory/$assembly_name" ]]; then
  printf 'Catalog apphost does not exist: %s\n' "$output_directory/$assembly_name" >&2
  exit 2
fi

rm -rf "$application"
mkdir -p "$application/Contents/MacOS"
cp -R "$output_directory/". "$application/Contents/MacOS/"
cat > "$application/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key>
  <string>$assembly_name</string>
  <key>CFBundleIdentifier</key>
  <string>$bundle_identifier</string>
  <key>CFBundleName</key>
  <string>$assembly_name</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>1.0</string>
  <key>CFBundleVersion</key>
  <string>1</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
PLIST

if [[ "${FORMA_CATALOG_BUNDLE_ONLY:-0}" == "1" ]]; then
  printf '%s\n' "$application"
  exit 0
fi

cd "$application/Contents/MacOS"
if (( catalog_argument_count == 0 )); then
  exec "$launcher"
fi
exec "$launcher" "${catalog_arguments[@]}"
