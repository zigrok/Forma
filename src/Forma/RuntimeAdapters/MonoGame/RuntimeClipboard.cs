// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;

namespace Forma;

internal sealed class RuntimeClipboard : IClipboard
{
    private readonly Func<SdlClipboardTransport> _open;

    public static RuntimeClipboard Instance { get; } = new RuntimeClipboard(Open);

    internal RuntimeClipboard(Func<SdlClipboardTransport> open) => _open = open;

    internal static IClipboard ForGame(Game game)
    {
        var windowType = game.Window.GetType().FullName;
        return new RuntimeClipboard(() => Open(GetLibraryNames(windowType)));
    }

    public string GetText()
    {
        using var transport = _open();
        return transport?.GetText();
    }

    public bool SetText(string text)
    {
        using var transport = _open();
        return transport?.SetText(text) ?? false;
    }

    private static SdlClipboardTransport Open() => Open(GetLibraryNames(null));

    private static SdlClipboardTransport Open(IEnumerable<string> libraries)
    {
        // Do not cache unavailability: UIContext can exist before Game initializes SDL.
#if !FORMA_BROWSER
        if (!OperatingSystem.IsBrowser())
        {
            foreach (var name in libraries)
            {
                if (!NativeLibrary.TryLoad(name, typeof(Game).Assembly, null, out var library)) continue;
                var transport = SdlClipboardTransport.TryCreate(
                    symbol => NativeLibrary.TryGetExport(library, symbol, out var address) ? address : IntPtr.Zero,
                    () => NativeLibrary.Free(library));
                if (transport != null) return transport;
            }
        }
#endif
        return null;
    }

    private static IEnumerable<string> GetLibraryNames(string windowType) =>
        SdlClipboardLibraries.ForWindow(windowType, OperatingSystem.IsWindows() ? OSPlatform.Windows :
            OperatingSystem.IsMacOS() ? OSPlatform.OSX :
            OperatingSystem.IsLinux() ? OSPlatform.Linux : default);
}
