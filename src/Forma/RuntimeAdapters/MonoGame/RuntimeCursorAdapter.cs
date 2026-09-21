// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Forma;

internal sealed class RuntimeCursorAdapter : IRuntimeCursorAdapter
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetFocus();

    private readonly Game _game;
    private readonly GetFocus _mouseFocus;
    private readonly GetFocus _keyboardFocus;
    private readonly IntPtr _library;
    private bool _disposed;

    internal RuntimeCursorAdapter(Game game)
    {
        _game = game;
#if !FORMA_BROWSER
        if (OperatingSystem.IsBrowser()) return;
        var libraries = game.Window.GetType().FullName switch
        {
            "Microsoft.Xna.Framework.NativeGameWindow" => new[] { "mgruntime" },
            "Microsoft.Xna.Framework.SdlGameWindow" => Sdl2Libraries(),
            _ => Array.Empty<string>()
        };
        foreach (var name in libraries)
        {
            if (!NativeLibrary.TryLoad(name, typeof(Game).Assembly, null, out var library)) continue;
            if (NativeLibrary.TryGetExport(library, "SDL_GetMouseFocus", out var mouse) &&
                NativeLibrary.TryGetExport(library, "SDL_GetKeyboardFocus", out var keyboard))
            {
                _library = library;
                _mouseFocus = Marshal.GetDelegateForFunctionPointer<GetFocus>(mouse);
                _keyboardFocus = Marshal.GetDelegateForFunctionPointer<GetFocus>(keyboard);
                break;
            }
            NativeLibrary.Free(library);
        }
#endif
    }

    private static string[] Sdl2Libraries() => OperatingSystem.IsWindows()
        ? new[] { "SDL2.dll", "SDL2" }
        : OperatingSystem.IsMacOS()
            ? new[] { "libSDL2-2.0.0.dylib", "SDL2" }
            : new[] { "libSDL2-2.0.so.0", "SDL2" };

    public bool IsSupported => !_disposed && _mouseFocus != null;

    public bool OwnsPointer
    {
        get
        {
            if (!IsSupported || !_game.IsActive || !_game.IsMouseVisible) return false;
            var handle = _game.Window.Handle;
            if (handle == IntPtr.Zero || Mouse.WindowHandle != handle ||
                _mouseFocus() != handle || _keyboardFocus() != handle) return false;
            var device = _game.GraphicsDevice;
            if (device == null || device.IsDisposed) return false;
            var mouse = Mouse.GetState(_game.Window);
            var presentation = device.PresentationParameters;
            return mouse.X >= 0 && mouse.Y >= 0 &&
                mouse.X < presentation.BackBufferWidth && mouse.Y < presentation.BackBufferHeight;
        }
    }

    public void SetCursor(Cursor cursor) => Mouse.SetCursor(cursor switch
    {
        Cursor.IBeam => MouseCursor.IBeam,
        Cursor.Hand => MouseCursor.Hand,
        Cursor.Crosshair => MouseCursor.Crosshair,
        Cursor.Wait => MouseCursor.Wait,
        Cursor.SizeHorizontal => MouseCursor.SizeWE,
        Cursor.SizeVertical => MouseCursor.SizeNS,
        Cursor.SizeDiagonalNorthwestSoutheast => MouseCursor.SizeNWSE,
        Cursor.SizeDiagonalNortheastSouthwest => MouseCursor.SizeNESW,
        _ => MouseCursor.Arrow
    });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_library != IntPtr.Zero) NativeLibrary.Free(_library);
    }
}
