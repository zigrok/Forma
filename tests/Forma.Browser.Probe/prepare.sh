#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
framework="${MonoGameProjectPath:-$root/../MonoGame/MonoGame.Framework/MonoGame.Framework.Browser.csproj}"
test -f "$framework"
bash "$root/native/dynamictext/build-wasm.sh"
bash "$root/native/dynamictext/build-browser-effect.sh"
bash "$root/tests/Forma.Browser.Probe/fetch-fonts.sh"
export TMPDIR="$root/artifacts/browser/scratch"
browser=(-p:FormaBuildTarget=Browser "-p:MonoGameProjectPath=$framework")
dotnet publish "$root/tests/Forma.Browser.Probe" -c Release "${browser[@]}" --nologo
output="$root/tests/Forma.Browser.Probe/bin/MonoGame/Browser/Release/net10.0/publish/wwwroot"
hashes="$root/artifacts/browser/frozen-platform-sha256.json"
node "$root/tests/Forma.Browser.Probe/platform-hashes.mjs" "$output" "$hashes"
dotnet build "$root/src/Forma.Xaml.Build" -c Release -p:FormaBuildTarget=Desktop -p:MonoGameProjectPath= --nologo
dotnet build "$root/tests/Forma.Xaml.Build.Integration" -c Release "${browser[@]}" --nologo
dotnet build "$root/tests/Forma.BrowserText.Module" -c Release "${browser[@]}" --nologo
mkdir -p "$output/modules" "$output/fonts" "$output/licenses"
cp "$root/tests/Forma.Xaml.Build.Integration/bin/MonoGame/Browser/Release/net10.0/Forma.Xaml.Build.Integration.dll" "$output/modules/"
cp "$root/tests/Forma.BrowserText.Module/bin/MonoGame/Browser/Release/net10.0/Forma.BrowserText.Module.dll" "$output/modules/"
cp "$root/artifacts/browser/fonts/"* "$output/fonts/"
cp "$root/artifacts/browser/native-text/licenses/"* "$output/licenses/"
node "$root/tests/Forma.Browser.Probe/platform-hashes.mjs" "$output" "$hashes" verify
printf 'Serve proof: node %q %q 5196\n' "$root/tests/Forma.Browser.Probe/serve.mjs" "$output"
