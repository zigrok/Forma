// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace Forma;

internal static class SdlClipboardLibraries
{
    internal static IEnumerable<string> ForWindow(string windowType, OSPlatform platform)
    {
        // Native packages embed SDL; a known Native window must never fall back to another SDL context.
        if (windowType == null || windowType == "Microsoft.Xna.Framework.NativeGameWindow")
            yield return "mgruntime";
        if (windowType != null && windowType != "Microsoft.Xna.Framework.SdlGameWindow")
            yield break;
        if (platform == OSPlatform.Windows) yield return "SDL2.dll";
        else if (platform == OSPlatform.OSX) yield return "libSDL2-2.0.0.dylib";
        else if (platform == OSPlatform.Linux) yield return "libSDL2-2.0.so.0";
        else yield break;
        yield return "SDL2";
    }
}
