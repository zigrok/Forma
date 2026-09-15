#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
monogame="${MonoGameRoot:-$root/../MonoGame}"
export MONOGAME_BROWSER_SHADER_TRANSLATOR="$monogame/Artifacts/browser/shader-tool/mggl-shader"
test -x "$MONOGAME_BROWSER_SHADER_TRANSLATOR"
mkdir -p "$root/artifacts/browser"
dotnet "$monogame/Artifacts/MonoGame.Effect.Compiler/Release/mgfxc.dll" \
  "$root/src/Forma/Resources/Alpha8Coverage.fx" "$root/artifacts/browser/Alpha8Coverage.Browser.mgfxo" /Profile:BrowserGL
node --input-type=module - "$root" <<'JS'
import {readFileSync, writeFileSync} from 'node:fs';
import assert from 'node:assert/strict';
const root = process.argv[2];
const bytes = readFileSync(`${root}/artifacts/browser/Alpha8Coverage.Browser.mgfxo`);
assert.equal(bytes.toString('ascii', 0, 4), 'MGFX');
assert.equal(bytes[5], 81, 'Expected the native BrowserGL profile, not legacy OpenGL.');
writeFileSync(`${root}/src/Forma/Resources/Alpha8Coverage.Browser.mgfxo.b64`, bytes.toString('base64') + '\n');
JS
