// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Forma;

internal sealed class RuntimeCursorAdapter : IRuntimeCursorAdapter
{
    private readonly Game _game;
    private readonly bool _sdl2;
    private bool _disposed;
    private bool _available;

    internal RuntimeCursorAdapter(Game game)
    {
        _game = game;
        var backend = Environment.GetEnvironmentVariable("FNA_PLATFORM_BACKEND");
        _sdl2 = backend == "SDL2";
        _available = (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()) &&
            backend is null or "SDL2" or "SDL3";
    }

    public bool IsSupported => !_disposed && _available;

    public bool OwnsPointer
    {
        get
        {
            if (!IsSupported || !_game.IsActive || !_game.IsMouseVisible) return false;
            var handle = _game.Window.Handle;
            if (handle == IntPtr.Zero || Mouse.WindowHandle != handle) return false;
            try
            {
                var mouseFocus = _sdl2 ? SDL2.SDL.SDL_GetMouseFocus() : SDL3.SDL.SDL_GetMouseFocus();
                var keyboardFocus = _sdl2 ? SDL2.SDL.SDL_GetKeyboardFocus() : SDL3.SDL.SDL_GetKeyboardFocus();
                if (mouseFocus != handle || keyboardFocus != handle) return false;
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                _available = false;
                return false;
            }
            var device = _game.GraphicsDevice;
            if (device == null || device.IsDisposed) return false;
            var mouse = Mouse.GetState();
            var presentation = device.PresentationParameters;
            return mouse.X >= 0 && mouse.Y >= 0 &&
                mouse.X < presentation.BackBufferWidth && mouse.Y < presentation.BackBufferHeight;
        }
    }

    public void SetCursor(Cursor cursor) => MouseCursorEXT.SetCursor(cursor switch
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

    public void Dispose() => _disposed = true;
}
