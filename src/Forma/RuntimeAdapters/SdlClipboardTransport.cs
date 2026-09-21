// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using System.Text;

namespace Forma;

internal sealed class SdlClipboardTransport : IClipboard, IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr GetClipboardText();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int SetClipboardText2(IntPtr text);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal delegate bool SetClipboardText3(IntPtr text);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void Free(IntPtr memory);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate uint WasInit(uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ClearError2();

    private const uint Video = 0x20;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private readonly GetClipboardText _getText;
    private readonly SetClipboardText2 _setText2;
    private readonly SetClipboardText3 _setText3;
    private readonly Free _free;
    private readonly WasInit _wasInit;
    private readonly ClearError2 _clearError;
    private readonly GetClipboardText _getError;
    private Action _release;

    private SdlClipboardTransport(GetClipboardText getText, SetClipboardText2 setText2,
        SetClipboardText3 setText3, Free free, WasInit wasInit, ClearError2 clearError,
        GetClipboardText getError, Action release)
    {
        _getText = getText;
        _setText2 = setText2;
        _setText3 = setText3;
        _free = free;
        _wasInit = wasInit;
        _clearError = clearError;
        _getError = getError;
        _release = release;
    }

    // Takes ownership of this module reference, including rejected or incomplete bindings.
    internal static SdlClipboardTransport TryCreate(Func<string, IntPtr> export, Action release)
    {
        try
        {
            var sdl3 = export("SDL_GetWindowProperties") != IntPtr.Zero;
            var sdl2 = export("SDL_GetWindowWMInfo") != IntPtr.Zero;
            if (sdl3 == sdl2) return null;
            var get = export("SDL_GetClipboardText");
            var set = export("SDL_SetClipboardText");
            var free = export("SDL_free");
            var init = export("SDL_WasInit");
            var clearError = sdl2 ? export("SDL_ClearError") : IntPtr.Zero;
            var getError = sdl2 ? export("SDL_GetError") : IntPtr.Zero;
            if (get == IntPtr.Zero || set == IntPtr.Zero || free == IntPtr.Zero || init == IntPtr.Zero)
                return null;
            if (sdl2 && (clearError == IntPtr.Zero || getError == IntPtr.Zero)) return null;
            var wasInit = Marshal.GetDelegateForFunctionPointer<WasInit>(init);
            if ((wasInit(Video) & Video) == 0) return null;
            var transport = new SdlClipboardTransport(
                Marshal.GetDelegateForFunctionPointer<GetClipboardText>(get),
                sdl2 ? Marshal.GetDelegateForFunctionPointer<SetClipboardText2>(set) : null,
                sdl3 ? Marshal.GetDelegateForFunctionPointer<SetClipboardText3>(set) : null,
                Marshal.GetDelegateForFunctionPointer<Free>(free), wasInit,
                sdl2 ? Marshal.GetDelegateForFunctionPointer<ClearError2>(clearError) : null,
                sdl2 ? Marshal.GetDelegateForFunctionPointer<GetClipboardText>(getError) : null, release);
            release = null;
            return transport;
        }
        finally
        {
            release?.Invoke();
        }
    }

    public string GetText()
    {
        if (_release == null || (_wasInit(Video) & Video) == 0) return null;
        // SDL2 returns allocated empty text on failure; only a freshly cleared error distinguishes it.
        _clearError?.Invoke();
        var pointer = _getText();
        if (pointer == IntPtr.Zero) return null;
        try
        {
            var length = 0;
            while (Marshal.ReadByte(pointer, length) != 0) length++;
            if (length == 0 && _getError != null)
            {
                var error = _getError();
                if (error != IntPtr.Zero && Marshal.ReadByte(error) != 0) return null;
            }
            var bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return Utf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
        finally
        {
            _free(pointer);
        }
    }

    public bool SetText(string text)
    {
        if (_release == null || (_wasInit(Video) & Video) == 0) return false;
        text ??= string.Empty;
        // SDL accepts C strings; truncating an embedded NUL would falsely report a complete write.
        if (text.Contains('\0')) return false;
        byte[] bytes;
        try { bytes = Utf8.GetBytes(text + '\0'); }
        catch (EncoderFallbackException) { return false; }
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            return _setText3 != null ? _setText3(pointer) : _setText2(pointer) == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
