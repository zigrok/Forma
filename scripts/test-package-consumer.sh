#!/usr/bin/env bash
set -euo pipefail

# Purpose: Pack and inspect all peer artifacts, run isolated consumers from empty package caches,
# and prove mixed runtime variants fail before reference resolution.
# Usage: `bash scripts/test-package-consumer.sh` from any directory.

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package_root="$repository_root/Artifacts/packages"
repro_root="$repository_root/Artifacts/package-repro"
consumer_project="$repository_root/tests/Forma.PackageConsumer/Forma.PackageConsumer.csproj"
version="$(grep -oE '<FormaVersion>[^<]+</FormaVersion>' "$repository_root/Directory.Build.props" | sed -E 's/<\/?FormaVersion>//g')"
commit_sha="$(git -C "$repository_root" rev-parse HEAD)"

rm -rf "$package_root" "$repro_root" \
  "$repository_root/tests/Forma.PackageConsumer/bin" \
  "$repository_root/tests/Forma.PackageConsumer/obj"
mkdir -p "$repro_root"
dotnet tool restore
dotnet build "$repository_root/tools/Forma.AssemblyInspector/Forma.AssemblyInspector.csproj" --configuration Release --nologo
assembly_inspector="$repository_root/tools/Forma.AssemblyInspector/bin/MonoGame/Release/net10.0/Forma.AssemblyInspector.dll"

for runtime in MonoGame FNA; do
  if [[ "$runtime" == "MonoGame" ]]; then
    catalog_project="$repository_root/samples/Forma.Catalog.MonoGame/Forma.Catalog.MonoGame.csproj"
  else
    catalog_project="$repository_root/samples/Forma.Catalog.FNA/Forma.Catalog.FNA.csproj"
  fi
  dotnet build "$catalog_project" --configuration Release -p:FormaRuntime="$runtime" --nologo
  for font_name in Catalog CatalogCode; do
    cmp \
      "$repository_root/tests/Assets/Fonts/$font_name.xnb" \
      "$repository_root/samples/Forma.Catalog.$runtime/bin/$runtime/Release/net10.0/Content/Fonts/$font_name.xnb"
  done
done

for runtime in MonoGame FNA; do
  dotnet pack "$repository_root/src/Forma/Forma.csproj" \
    --configuration Release -p:FormaRuntime="$runtime" --nologo
  dotnet pack "$repository_root/src/Forma.DynamicText/Forma.DynamicText.csproj" \
    --configuration Release -p:FormaRuntime="$runtime" --nologo
  dotnet pack "$repository_root/src/Forma.Media/Forma.Media.csproj" \
    --configuration Release -p:FormaRuntime="$runtime" --nologo
  dotnet pack "$repository_root/src/Forma.Svg/Forma.Svg.csproj" \
    --configuration Release -p:FormaRuntime="$runtime" --nologo
  dotnet pack "$repository_root/src/Forma.Xaml.Build/Forma.Xaml.Build.csproj" \
    --configuration Release -p:FormaRuntime="$runtime" --nologo
done

inspect_package() {
  local runtime="$1"
  local package_id="$2"
  local assembly_name="$3"
  local guard_name="$4"
  local package_path="$package_root/$runtime/$package_id.$version.nupkg"
  local symbol_path="$package_root/$runtime/$package_id.$version.snupkg"
  local entries

  entries="$(unzip -Z1 "$package_path")"
  for required_entry in \
    "lib/net10.0/$assembly_name.dll" \
    "lib/net10.0/$assembly_name.xml" \
    "buildTransitive/$guard_name" \
    LICENSE \
    NOTICE.md \
    README.md \
    THIRD-PARTY-NOTICES.md; do
    grep -Fxq "$required_entry" <<<"$entries"
  done

  if [[ "$assembly_name" == "Forma" ]]; then
    # MonoGame embeds one compiled Alpha8Coverage effect per graphics backend
    # (OpenGL, DirectX11, DirectX12, Vulkan); FNA embeds a single fxb, so its
    # budget stays tighter than the multi-backend MonoGame assembly's.
    local core_budget_bytes=$((2 * 1024 * 1024))
    if [[ "$runtime" == "MonoGame" ]]; then
      core_budget_bytes=$((2 * 1024 * 1024 + 256 * 1024))
    fi
    assembly_bytes="$(unzip -p "$package_path" "lib/net10.0/$assembly_name.dll" | wc -c | tr -d ' ')"
    if (( assembly_bytes > core_budget_bytes )); then
      printf '%s exceeds the %s core managed assembly budget (%s bytes).\n' \
        "$package_id" "$([[ "$runtime" == "MonoGame" ]] && echo "2.25 MiB" || echo "2 MiB")" "$assembly_bytes" >&2
      exit 1
    fi
    grep -Fxq "licenses/theme-icons/LICENSE.Godot.txt" <<<"$entries"
    grep -Fxq "lib/net10.0/Clipper2Lib.dll" <<<"$entries"
    grep -Fxq "licenses/geometry/Clipper2.LICENSE.txt" <<<"$entries"
    strings_output="$(unzip -p "$package_path" "lib/net10.0/$assembly_name.dll" | strings)"
    for resource_name in theme-icons-1x.png theme-icons-2x.png theme-icons.json; do
      grep -Fq "Forma.ThemeIcons.$resource_name" <<<"$strings_output"
    done
  fi

  unzip -p "$package_path" "$package_id.nuspec" |
    grep -Fq "repository type=\"git\" url=\"https://github.com/zigrok/Forma\" commit=\"$commit_sha\""
  unzip -p "$symbol_path" "lib/net10.0/$assembly_name.pdb" |
    strings |
      LC_ALL=C grep -F "raw.githubusercontent.com/zigrok/Forma/$commit_sha" >/dev/null
}

inspect_package MonoGame Forma.MonoGame Forma Forma.MonoGame.targets
inspect_package MonoGame Forma.Media.MonoGame Forma.Media Forma.Media.MonoGame.targets
inspect_package FNA Forma.FNA Forma Forma.FNA.targets
inspect_package FNA Forma.Media.FNA Forma.Media Forma.Media.FNA.targets

inspect_dynamic_package() {
  local runtime="$1"
  local opposite_runtime="$2"
  local package_id="Forma.DynamicText.$runtime"
  local package_path="$package_root/$runtime/$package_id.$version.nupkg"
  local symbol_path="$package_root/$runtime/$package_id.$version.snupkg"
  local entries
  local manifest

  entries="$(unzip -Z1 "$package_path")"
  for required_entry in lib/net10.0/Forma.DynamicText.dll lib/net10.0/Forma.DynamicText.xml "buildTransitive/$package_id.targets" buildTransitive/Forma.DynamicText.PackageInitializer.cs.txt buildTransitive/assets/Inter_Regular.ttf licenses/fonts/LICENSE.Inter.txt docs/dynamic-text.md examples/DynamicTextQuickStart.cs LICENSE NOTICE.md README.md THIRD-PARTY-NOTICES.md; do
    grep -Fxq "$required_entry" <<<"$entries"
  done
  assembly_bytes="$(unzip -p "$package_path" lib/net10.0/Forma.DynamicText.dll | wc -c | tr -d ' ')"
  if (( assembly_bytes > 256 * 1024 )); then
    printf '%s exceeds the 256 KiB dynamic-text managed assembly budget (%s bytes).\n' "$package_id" "$assembly_bytes" >&2
    exit 1
  fi
  unzip -p "$symbol_path" lib/net10.0/Forma.DynamicText.pdb |
    strings |
      LC_ALL=C grep -F "raw.githubusercontent.com/zigrok/Forma/$commit_sha" >/dev/null
  manifest="$(unzip -p "$package_path" "$package_id.nuspec")"
  for dependency in "Forma.$runtime" FreeTypeSharp HarfBuzzSharp HarfBuzzSharp.NativeAssets.Linux; do
    grep -Fq "dependency id=\"$dependency\"" <<<"$manifest"
  done
  if grep -Fq "dependency id=\"Forma.$opposite_runtime\"" <<<"$manifest"; then
    printf '%s must not depend on Forma.%s.\n' "$package_id" "$opposite_runtime" >&2
    exit 1
  fi
}

inspect_dynamic_package MonoGame FNA
inspect_dynamic_package FNA MonoGame

inspect_svg_package() {
  local runtime="$1"
  local opposite_runtime="$2"
  local package_id="Forma.Svg.Skia.$runtime"
  local package_path="$package_root/$runtime/$package_id.$version.nupkg"
  local symbol_path="$package_root/$runtime/$package_id.$version.snupkg"
  local entries
  local manifest

  entries="$(unzip -Z1 "$package_path")"
  for required_entry in lib/net10.0/Forma.Svg.Skia.dll lib/net10.0/Forma.Svg.Skia.xml "buildTransitive/$package_id.targets" buildTransitive/Forma.Svg.PackageInitializer.cs.txt LICENSE NOTICE.md README.md THIRD-PARTY-NOTICES.md; do
    grep -Fxq "$required_entry" <<<"$entries"
  done
  assembly_bytes="$(unzip -p "$package_path" lib/net10.0/Forma.Svg.Skia.dll | wc -c | tr -d ' ')"
  if (( assembly_bytes > 256 * 1024 )); then
    printf '%s exceeds the 256 KiB SVG managed assembly budget (%s bytes).\n' "$package_id" "$assembly_bytes" >&2
    exit 1
  fi
  unzip -p "$symbol_path" lib/net10.0/Forma.Svg.Skia.pdb |
    strings |
      LC_ALL=C grep -F "raw.githubusercontent.com/zigrok/Forma/$commit_sha" >/dev/null
  manifest="$(unzip -p "$package_path" "$package_id.nuspec")"
  for dependency in "Forma.$runtime" Svg.Skia SkiaSharp.NativeAssets.Linux.NoDependencies; do
    grep -Fq "dependency id=\"$dependency\"" <<<"$manifest"
  done
  if grep -Fq "dependency id=\"Forma.$opposite_runtime\"" <<<"$manifest"; then
    printf '%s must not depend on Forma.%s.\n' "$package_id" "$opposite_runtime" >&2
    exit 1
  fi
}

inspect_svg_package MonoGame FNA
inspect_svg_package FNA MonoGame

inspect_xaml_build_package() {
  local runtime="$1"
  local opposite_runtime="$2"
  local package_id="Forma.Xaml.Build.$runtime"
  local package_path="$package_root/$runtime/$package_id.$version.nupkg"
  local entries
  local package_bytes

  entries="$(unzip -Z1 "$package_path")"
  for required_entry in \
    "buildTransitive/$package_id.targets" \
    tools/net10.0/Forma.dll \
    tools/net10.0/Forma.Xaml.Build.dll \
    tools/net10.0/Forma.Xaml.Compiler.dll \
    tools/net10.0/XamlX.dll \
    tools/net10.0/XamlX.IL.Cecil.dll \
    tools/net10.0/Mono.Cecil.dll \
    licenses/XamlX/LICENSE \
    LICENSE NOTICE.md README.md THIRD-PARTY-NOTICES.md; do
    grep -Fxq "$required_entry" <<<"$entries"
  done
  if grep -Eq '^(lib|ref)/|\.xaml$' <<<"$entries"; then
    printf 'Forma.Xaml.Build for %s contains runtime or source-XAML assets.\n' "$runtime" >&2
    exit 1
  fi
  package_bytes="$(wc -c < "$package_path" | tr -d ' ')"
  if (( package_bytes > 8 * 1024 * 1024 )); then
    printf '%s exceeds the 8 MiB package budget (%s bytes).\n' "$package_id" "$package_bytes" >&2
    exit 1
  fi
  temporary_assembly="$repro_root/$runtime-$package_id-Forma.dll"
  unzip -p "$package_path" tools/net10.0/Forma.dll > "$temporary_assembly"
  dotnet "$assembly_inspector" forbid-references "$temporary_assembly" "$opposite_runtime"
}

inspect_xaml_build_package MonoGame FNA.NET
inspect_xaml_build_package FNA MonoGame.Framework

for runtime in MonoGame FNA; do
  dotnet "$assembly_inspector" forbid-references \
    "$repository_root/src/Forma/bin/$runtime/Release/net10.0/Forma.dll" \
    FreeTypeSharp HarfBuzzSharp Svg.Skia SkiaSharp ShimSkiaSharp Forma.Svg
done

package_fingerprint() {
  local package_path="$1"
  while IFS= read -r entry; do
    case "$entry" in
      _rels/.rels|package/services/metadata/core-properties/*) continue ;;
    esac
    local unzip_entry="$entry"
    if [[ "$entry" == '[Content_Types].xml' ]]; then unzip_entry='\[Content_Types\].xml'; fi
    printf '%s\0' "$entry"
    unzip -p "$package_path" "$unzip_entry" | shasum -a 256
  done < <(unzip -Z1 "$package_path" | LC_ALL=C sort)
}

for runtime in MonoGame FNA; do
  dotnet pack "$repository_root/src/Forma/Forma.csproj" \
    --configuration Release \
    -p:FormaRuntime="$runtime" \
    -p:PackageOutputPath="$repro_root/$runtime" \
    --nologo
  dotnet pack "$repository_root/src/Forma.DynamicText/Forma.DynamicText.csproj" \
    --configuration Release \
    -p:FormaRuntime="$runtime" \
    -p:PackageOutputPath="$repro_root/$runtime" \
    --nologo
  dotnet pack "$repository_root/src/Forma.Media/Forma.Media.csproj" \
    --configuration Release \
    -p:FormaRuntime="$runtime" \
    -p:PackageOutputPath="$repro_root/$runtime" \
    --nologo
  dotnet pack "$repository_root/src/Forma.Svg/Forma.Svg.csproj" \
    --configuration Release \
    -p:FormaRuntime="$runtime" \
    -p:PackageOutputPath="$repro_root/$runtime" \
    --nologo
  dotnet pack "$repository_root/src/Forma.Xaml.Build/Forma.Xaml.Build.csproj" \
    --configuration Release \
    -p:FormaRuntime="$runtime" \
    -p:PackageOutputPath="$repro_root/$runtime" \
    --nologo
  for package_id in "Forma.$runtime" "Forma.DynamicText.$runtime" "Forma.Media.$runtime" "Forma.Svg.Skia.$runtime"; do
    for extension in nupkg snupkg; do
      printf 'Comparing deterministic contents: %s.%s\n' "$package_id" "$extension"
      diff \
        <(package_fingerprint "$package_root/$runtime/$package_id.$version.$extension") \
        <(package_fingerprint "$repro_root/$runtime/$package_id.$version.$extension")
    done
  done
  printf 'Comparing deterministic contents: Forma.Xaml.Build.%s.nupkg\n' "$runtime"
  diff \
    <(package_fingerprint "$package_root/$runtime/Forma.Xaml.Build.$runtime.$version.nupkg") \
    <(package_fingerprint "$repro_root/$runtime/Forma.Xaml.Build.$runtime.$version.nupkg")
done

if unzip -p "$package_root/MonoGame/Forma.MonoGame.$version.nupkg" Forma.MonoGame.nuspec |
  grep -Eq 'FNA.NET|MonoGame.Framework'; then
  printf 'Forma.MonoGame must leave framework backend selection to the application.\n' >&2
  exit 1
fi
if unzip -p "$package_root/FNA/Forma.FNA.$version.nupkg" Forma.FNA.nuspec |
  grep -Eq 'FNA.NET|MonoGame.Framework'; then
  printf 'Forma.FNA must leave framework selection to the application.\n' >&2
  exit 1
fi

run_consumer() {
  local runtime="$1"
  shift
  if [[ "$runtime" == "FNA" && "$(uname -s)" == "Linux" ]]; then
    env SDL_VIDEODRIVER=offscreen FNA3D_FORCE_DRIVER=OpenGL FNA3D_OPENGL_WINDOW_DEPTHSTENCILFORMAT=None "$@"
  else
    "$@"
  fi
}

assert_no_xaml_development_artifacts() {
  local output_dir="$1"
  if find "$output_dir" -type f \( \
    -iname '*.xaml' -o \
    -iname 'XamlX*' -o \
    -iname 'Mono.Cecil*' -o \
    -iname 'Forma.Xaml.Build*' -o \
    -iname 'Forma.Xaml.Compiler*' -o \
    -iname 'Forma.Xaml.HotReload*' \) -print -quit | grep -q .; then
    printf 'Release output contains a Forma XAML development artifact: %s\n' "$output_dir" >&2
    exit 1
  fi
}

assert_official_nuget_package() {
  local cache_dir="$1"
  local package_id="$2"
  local package_directory
  local metadata_path

  package_directory="$(find "$cache_dir/$package_id" -mindepth 1 -maxdepth 1 -type d -print -quit)"
  metadata_path="$package_directory/.nupkg.metadata"
  if [[ -z "$package_directory" || ! -f "$metadata_path" ]] ||
    ! grep -Fq '"source": "https://api.nuget.org/v3/index.json"' "$metadata_path" ||
    ! grep -Eq '"contentHash": "[^"]+"' "$metadata_path"; then
    printf 'SVG dependency %s was not restored with official NuGet provenance.\n' "$package_id" >&2
    exit 1
  fi
}

for runtime in MonoGame FNA; do
  consumer_cache="$package_root/.consumer-packages/$runtime"
  rm -rf "$consumer_cache" "$repository_root/tests/Forma.PackageConsumer/bin" "$repository_root/tests/Forma.PackageConsumer/obj"
  NUGET_PACKAGES="$consumer_cache" dotnet restore "$consumer_project" \
    -p:FormaRuntime="$runtime" \
    -p:FormaPackageSource="$package_root/$runtime" \
    -p:RestoreNoCache=true \
    --nologo
  NUGET_PACKAGES="$consumer_cache" run_consumer "$runtime" dotnet run --project "$consumer_project" \
    --configuration Release \
    -p:FormaRuntime="$runtime" \
    -p:FormaPackageSource="$package_root/$runtime" \
    --no-restore \
    --nologo
  assert_no_xaml_development_artifacts "$repository_root/tests/Forma.PackageConsumer/bin/$runtime/Release/net10.0"
  for forbidden_package in freetypesharp harfbuzzsharp harfbuzzsharp.nativeassets.linux svg.skia skiasharp skiasharp.nativeassets.linux.nodependencies shimskiasharp; do
    if [[ -d "$consumer_cache/$forbidden_package" ]]; then
      printf 'Core-only %s consumer unexpectedly resolved %s.\n' "$runtime" "$forbidden_package" >&2
      exit 1
    fi
  done
  if find "$repository_root/tests/Forma.PackageConsumer/bin" -type f \
    \( -iname '*freetype*' -o -iname '*harfbuzz*' -o -iname '*skia*' -o -iname 'Forma.Svg*' \) -print -quit | grep -q .; then
    printf 'Core-only %s consumer unexpectedly copied optional native assets.\n' "$runtime" >&2
    exit 1
  fi
done

for runtime in MonoGame FNA; do
  consumer_cache="$package_root/.svg-consumer-packages/$runtime"
  rm -rf "$consumer_cache" "$repository_root/tests/Forma.PackageConsumer/bin" "$repository_root/tests/Forma.PackageConsumer/obj"
  NUGET_PACKAGES="$consumer_cache" dotnet restore "$consumer_project" \
    -p:FormaRuntime="$runtime" \
    -p:IncludeFormaMedia=false \
    -p:IncludeFormaSvg=true \
    -p:ExerciseSpriteFont=true \
    -p:FormaPackageSource="$package_root/$runtime" \
    -p:RestoreNoCache=true \
    --nologo
  NUGET_PACKAGES="$consumer_cache" run_consumer "$runtime" dotnet run --project "$consumer_project" \
    --configuration Release \
    -p:FormaRuntime="$runtime" \
    -p:IncludeFormaMedia=false \
    -p:IncludeFormaSvg=true \
    -p:ExerciseSpriteFont=true \
    -p:FormaPackageSource="$package_root/$runtime" \
    --no-restore \
    --nologo
  for required_package in svg.skia skiasharp; do
    if [[ ! -d "$consumer_cache/$required_package" ]]; then
      printf 'SVG %s consumer did not resolve %s.\n' "$runtime" "$required_package" >&2
      exit 1
    fi
  done
  for official_package in svg.skia skiasharp skiasharp.nativeassets.linux.nodependencies shimskiasharp; do
    assert_official_nuget_package "$consumer_cache" "$official_package"
  done
done

for runtime in MonoGame FNA; do
  consumer_cache="$package_root/.dynamic-consumer-packages/$runtime"
  rm -rf "$consumer_cache" "$repository_root/tests/Forma.PackageConsumer/bin" "$repository_root/tests/Forma.PackageConsumer/obj"
  NUGET_PACKAGES="$consumer_cache" dotnet restore "$consumer_project" \
    -p:FormaRuntime="$runtime" \
    -p:IncludeFormaMedia=false \
    -p:IncludeDynamicText=true \
    -p:FormaPackageSource="$package_root/$runtime" \
    -p:RestoreNoCache=true \
    --nologo
  NUGET_PACKAGES="$consumer_cache" run_consumer "$runtime" dotnet run --project "$consumer_project" \
    --configuration Release \
    -p:FormaRuntime="$runtime" \
    -p:IncludeFormaMedia=false \
    -p:IncludeDynamicText=true \
    -p:FormaPackageSource="$package_root/$runtime" \
    --no-restore \
    --nologo
  for required_package in freetypesharp harfbuzzsharp; do
    if [[ ! -d "$consumer_cache/$required_package" ]]; then
      printf 'Dynamic-text %s consumer did not resolve %s.\n' "$runtime" "$required_package" >&2
      exit 1
    fi
  done
done

for runtime in MonoGame FNA; do
  consumer_cache="$package_root/.spritefont-consumer-packages/$runtime"
  rm -rf "$consumer_cache" "$repository_root/tests/Forma.PackageConsumer/bin" "$repository_root/tests/Forma.PackageConsumer/obj"
  NUGET_PACKAGES="$consumer_cache" dotnet restore "$consumer_project" \
    -p:FormaRuntime="$runtime" \
    -p:IncludeFormaMedia=false \
    -p:ExerciseSpriteFont=true \
    -p:FormaPackageSource="$package_root/$runtime" \
    -p:RestoreNoCache=true \
    --nologo
  NUGET_PACKAGES="$consumer_cache" run_consumer "$runtime" dotnet run --project "$consumer_project" \
    --configuration Release \
    -p:FormaRuntime="$runtime" \
    -p:IncludeFormaMedia=false \
    -p:ExerciseSpriteFont=true \
    -p:FormaPackageSource="$package_root/$runtime" \
    --no-restore \
    --nologo
done

for runtime in MonoGame FNA; do
  consumer_cache="$package_root/.integration-consumer-packages/$runtime"
  rm -rf "$consumer_cache" "$repository_root/tests/Forma.PackageConsumer/bin" "$repository_root/tests/Forma.PackageConsumer/obj"
  NUGET_PACKAGES="$consumer_cache" dotnet restore "$consumer_project" \
    -p:FormaRuntime="$runtime" \
    -p:IncludeFormaMedia=false \
    -p:IncludeDynamicText=true \
    -p:ExerciseSpriteFont=true \
    -p:FormaPackageSource="$package_root/$runtime" \
    -p:RestoreNoCache=true \
    --nologo
  NUGET_PACKAGES="$consumer_cache" run_consumer "$runtime" dotnet run --project "$consumer_project" \
    --configuration Release \
    -p:FormaRuntime="$runtime" \
    -p:IncludeFormaMedia=false \
    -p:IncludeDynamicText=true \
    -p:ExerciseSpriteFont=true \
    -p:FormaPackageSource="$package_root/$runtime" \
    --no-restore \
    --nologo
done

for runtime in MonoGame FNA; do
  consumer_cache="$package_root/.rollback-consumer-packages/$runtime"
  rm -rf "$consumer_cache" "$repository_root/tests/Forma.PackageConsumer/bin" "$repository_root/tests/Forma.PackageConsumer/obj"
  NUGET_PACKAGES="$consumer_cache" dotnet restore "$consumer_project" \
    -p:FormaRuntime="$runtime" \
    -p:IncludeFormaMedia=false \
    -p:IncludeDynamicText=true \
    -p:ExpectDynamicTextDefault=false \
    -p:FormaDynamicTextDefaultEnabled=false \
    -p:FormaPackageSource="$package_root/$runtime" \
    -p:RestoreNoCache=true \
    --nologo
  NUGET_PACKAGES="$consumer_cache" run_consumer "$runtime" dotnet run --project "$consumer_project" \
    --configuration Release \
    -p:FormaRuntime="$runtime" \
    -p:IncludeFormaMedia=false \
    -p:IncludeDynamicText=true \
    -p:ExpectDynamicTextDefault=false \
    -p:FormaDynamicTextDefaultEnabled=false \
    -p:FormaPackageSource="$package_root/$runtime" \
    --no-restore \
    --nologo
done

validate_core_rid() {
  local runtime="$1"
  local rid="$2"
  local consumer_cache="$package_root/.core-consumer-packages/$runtime/$rid"
  local publish_dir="$package_root/.core-consumer-publish/$runtime/$rid"

  rm -rf "$consumer_cache" "$publish_dir" \
    "$repository_root/tests/Forma.PackageConsumer/bin" \
    "$repository_root/tests/Forma.PackageConsumer/obj"
  NUGET_PACKAGES="$consumer_cache" dotnet restore "$consumer_project" \
    -r "$rid" \
    -p:FormaRuntime="$runtime" \
    -p:IncludeFormaMedia=false \
    -p:FormaPackageSource="$package_root/$runtime" \
    -p:RestoreNoCache=true \
    --nologo
  NUGET_PACKAGES="$consumer_cache" dotnet publish "$consumer_project" \
    --configuration Release \
    -r "$rid" \
    --self-contained false \
    -p:FormaRuntime="$runtime" \
    -p:IncludeFormaMedia=false \
    -p:FormaPackageSource="$package_root/$runtime" \
    -p:PublishDir="$publish_dir" \
    --no-restore \
    --nologo
  for forbidden_package in freetypesharp harfbuzzsharp harfbuzzsharp.nativeassets.linux svg.skia skiasharp skiasharp.nativeassets.linux.nodependencies shimskiasharp; do
    if [[ -d "$consumer_cache/$forbidden_package" ]]; then
      printf 'Core-only %s/%s consumer unexpectedly resolved %s.\n' "$runtime" "$rid" "$forbidden_package" >&2
      exit 1
    fi
  done
  if find "$publish_dir" -type f \( -iname '*freetype*' -o -iname '*harfbuzz*' -o -iname '*skia*' -o -iname 'Forma.Svg*' \) -print -quit | grep -q .; then
    printf 'Core-only %s/%s consumer unexpectedly copied optional native assets.\n' "$runtime" "$rid" >&2
    exit 1
  fi
  bash "$repository_root/scripts/inspect-native-imports.sh" "$publish_dir"
}

validate_dynamic_rid() {
  local runtime="$1"
  local rid="$2"
  local consumer_cache="$package_root/.dynamic-consumer-packages/$runtime/$rid"
  local publish_dir="$package_root/.dynamic-consumer-publish/$runtime/$rid"
  local freetype_asset
  local harfbuzz_asset

  case "$rid" in
    win-*) freetype_asset='freetype.dll'; harfbuzz_asset='libHarfBuzzSharp.dll' ;;
    linux-*) freetype_asset='libfreetype.so'; harfbuzz_asset='libHarfBuzzSharp.so' ;;
    osx-*) freetype_asset='libfreetype.dylib'; harfbuzz_asset='libHarfBuzzSharp.dylib' ;;
    *) printf 'No dynamic-text asset mapping for %s.\n' "$rid" >&2; exit 2 ;;
  esac

  rm -rf "$consumer_cache" "$publish_dir" \
    "$repository_root/tests/Forma.PackageConsumer/bin" \
    "$repository_root/tests/Forma.PackageConsumer/obj"
  NUGET_PACKAGES="$consumer_cache" dotnet restore "$consumer_project" \
    -r "$rid" \
    -p:FormaRuntime="$runtime" \
    -p:IncludeFormaMedia=false \
    -p:IncludeDynamicText=true \
    -p:FormaPackageSource="$package_root/$runtime" \
    -p:RestoreNoCache=true \
    --nologo
  NUGET_PACKAGES="$consumer_cache" dotnet publish "$consumer_project" \
    --configuration Release \
    -r "$rid" \
    --self-contained false \
    -p:FormaRuntime="$runtime" \
    -p:IncludeFormaMedia=false \
    -p:IncludeDynamicText=true \
    -p:FormaPackageSource="$package_root/$runtime" \
    -p:PublishDir="$publish_dir" \
    --no-restore \
    --nologo
  for native_asset in "$freetype_asset" "$harfbuzz_asset"; do
    if [[ ! -f "$publish_dir/$native_asset" ]]; then
      printf 'Dynamic-text %s/%s consumer did not publish %s.\n' "$runtime" "$rid" "$native_asset" >&2
      exit 1
    fi
  done
}

validate_svg_rid() {
  local runtime="$1"
  local rid="$2"
  local consumer_cache="$package_root/.svg-consumer-packages/$runtime/$rid"
  local publish_dir="$package_root/.svg-consumer-publish/$runtime/$rid"
  local skia_asset

  case "$rid" in
    win-*) skia_asset='libSkiaSharp.dll' ;;
    linux-*) skia_asset='libSkiaSharp.so' ;;
    osx-*) skia_asset='libSkiaSharp.dylib' ;;
    *) printf 'No SVG native asset mapping for %s.\n' "$rid" >&2; exit 2 ;;
  esac

  rm -rf "$consumer_cache" "$publish_dir" \
    "$repository_root/tests/Forma.PackageConsumer/bin" \
    "$repository_root/tests/Forma.PackageConsumer/obj"
  NUGET_PACKAGES="$consumer_cache" dotnet restore "$consumer_project" \
    -r "$rid" \
    -p:FormaRuntime="$runtime" \
    -p:IncludeFormaMedia=false \
    -p:IncludeFormaSvg=true \
    -p:FormaPackageSource="$package_root/$runtime" \
    -p:RestoreNoCache=true \
    --nologo
  NUGET_PACKAGES="$consumer_cache" dotnet publish "$consumer_project" \
    --configuration Release \
    -r "$rid" \
    --self-contained false \
    -p:FormaRuntime="$runtime" \
    -p:IncludeFormaMedia=false \
    -p:IncludeFormaSvg=true \
    -p:FormaPackageSource="$package_root/$runtime" \
    -p:PublishDir="$publish_dir" \
    --no-restore \
    --nologo
  if [[ ! -f "$publish_dir/$skia_asset" ]]; then
    printf 'SVG %s/%s consumer did not publish %s.\n' "$runtime" "$rid" "$skia_asset" >&2
    exit 1
  fi
}

for rid in win-x64 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64; do
  validate_core_rid MonoGame "$rid"
done
for rid in win-x64 linux-x64 linux-arm64 osx-x64 osx-arm64; do
  validate_core_rid FNA "$rid"
done
for rid in win-x64 win-arm64 linux-x64 osx-x64 osx-arm64; do
  validate_dynamic_rid MonoGame "$rid"
done
for rid in win-x64 linux-x64 osx-x64 osx-arm64; do
  validate_dynamic_rid FNA "$rid"
done
for rid in win-x64 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64; do
  validate_svg_rid MonoGame "$rid"
done
for rid in win-x64 linux-x64 linux-arm64 osx-x64 osx-arm64; do
  validate_svg_rid FNA "$rid"
done

mixed_output="$(dotnet build "$consumer_project" \
  --configuration Release \
  -p:FormaRuntime=MonoGame \
  -p:FormaMediaRuntime=FNA \
  -p:FormaPackageSource="$package_root/MonoGame" \
  -p:FormaAdditionalPackageSource="$package_root/FNA" \
  --nologo 2>&1)" && {
    printf 'Mixed Forma runtime variants unexpectedly built successfully.\n' >&2
    exit 1
}
grep -Eq 'cannot be referenced by the same project|cannot be combined with' <<<"$mixed_output"

mixed_svg_output="$(dotnet build "$consumer_project" \
  --configuration Release \
  -p:FormaRuntime=MonoGame \
  -p:IncludeFormaMedia=false \
  -p:IncludeFormaSvg=true \
  -p:FormaSvgRuntime=FNA \
  -p:FormaPackageSource="$package_root/MonoGame" \
  -p:FormaAdditionalPackageSource="$package_root/FNA" \
  --nologo 2>&1)" && {
    printf 'Mixed Forma SVG runtime variants unexpectedly built successfully.\n' >&2
    exit 1
}
grep -Eq 'cannot be referenced by the same project|cannot be combined with' <<<"$mixed_svg_output"

printf 'Validated ten peer packages, 11 native-free core publishes, nine dynamic publishes, 11 SVG publishes, compiled XAML consumers, and mixed-variant rejection.\n'