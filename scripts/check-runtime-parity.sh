#!/usr/bin/env bash
set -euo pipefail

# Purpose: Build and test both peer runtimes, then verify framework isolation and public API parity.
# Usage: `bash scripts/check-runtime-parity.sh` from any directory.
#
# The builds run with one MSBuild node (-m:1). The test project references the same projects with an explicit
# AdditionalProperties runtime, so a parallel node can build one of them as a second instance while the first is still
# writing its *.deps.json; on a slow (emulated) runner that shows up as GenerateDepsFile's "being used by another process".
# A single node serializes the two instances and the race disappears, at the cost of a longer build.

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
inspector_project="$repository_root/tools/Forma.AssemblyInspector/Forma.AssemblyInspector.csproj"
inspector="$repository_root/tools/Forma.AssemblyInspector/bin/MonoGame/Release/net10.0/Forma.AssemblyInspector.dll"

dotnet test "$repository_root/tests/Forma.Tests/Forma.Tests.csproj" --configuration Release -p:FormaRuntime=FNA -m:1 --nologo
dotnet build "$repository_root/samples/Forma.Catalog.FNA/Forma.Catalog.FNA.csproj" --configuration Release -p:FormaRuntime=FNA -m:1 --nologo
dotnet test "$repository_root/tests/Forma.Tests/Forma.Tests.csproj" --configuration Release -p:FormaRuntime=MonoGame -m:1 --nologo
dotnet build "$repository_root/samples/Forma.Catalog.MonoGame/Forma.Catalog.MonoGame.csproj" --configuration Release -p:FormaRuntime=MonoGame -m:1 --nologo
dotnet build "$inspector_project" --configuration Release -m:1 --nologo

dotnet "$inspector" references "$repository_root/src/Forma/bin/FNA/Release/net10.0/Forma.dll" Forma FNA FNA.NET MonoGame.Framework
dotnet "$inspector" references "$repository_root/src/Forma/bin/MonoGame/Release/net10.0/Forma.dll" Forma MonoGame MonoGame.Framework FNA.NET
dotnet "$inspector" references "$repository_root/src/Forma.DynamicText/bin/FNA/Release/net10.0/Forma.DynamicText.dll" Forma.DynamicText FNA FNA.NET MonoGame.Framework
dotnet "$inspector" references "$repository_root/src/Forma.DynamicText/bin/MonoGame/Release/net10.0/Forma.DynamicText.dll" Forma.DynamicText MonoGame MonoGame.Framework FNA.NET
dotnet "$inspector" references "$repository_root/src/Forma.Media/bin/FNA/Release/net10.0/Forma.Media.dll" Forma.Media FNA FNA.NET MonoGame.Framework
dotnet "$inspector" references "$repository_root/src/Forma.Media/bin/MonoGame/Release/net10.0/Forma.Media.dll" Forma.Media MonoGame MonoGame.Framework FNA.NET
dotnet "$inspector" compare-api "$repository_root/src/Forma/bin/FNA/Release/net10.0/Forma.dll" "$repository_root/src/Forma/bin/MonoGame/Release/net10.0/Forma.dll"
dotnet "$inspector" compare-api "$repository_root/src/Forma.DynamicText/bin/FNA/Release/net10.0/Forma.DynamicText.dll" "$repository_root/src/Forma.DynamicText/bin/MonoGame/Release/net10.0/Forma.DynamicText.dll"
dotnet "$inspector" compare-api "$repository_root/src/Forma.Media/bin/FNA/Release/net10.0/Forma.Media.dll" "$repository_root/src/Forma.Media/bin/MonoGame/Release/net10.0/Forma.Media.dll"