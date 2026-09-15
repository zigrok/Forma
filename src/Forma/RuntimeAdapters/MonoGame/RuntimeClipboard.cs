// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using System.Text;

namespace Forma
{
    internal sealed class RuntimeClipboard : IClipboard
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr SdlGetClipboardText();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SdlSetClipboardText(IntPtr text);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SdlFree(IntPtr memory);

        private readonly SdlGetClipboardText _getText;
        private readonly SdlSetClipboardText _setText;
        private readonly SdlFree _free;

        public static RuntimeClipboard Instance { get; } = Create();

        private RuntimeClipboard(SdlGetClipboardText getText, SdlSetClipboardText setText, SdlFree free)
        {
            _getText = getText;
            _setText = setText;
            _free = free;
        }

        public string GetText()
        {
            if (_getText == null) return null;
            var pointer = _getText();
            if (pointer == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUTF8(pointer); }
            finally { _free(pointer); }
        }

        public bool SetText(string text)
        {
            if (_setText == null) return false;
            var bytes = Encoding.UTF8.GetBytes((text ?? string.Empty) + '\0');
            var pointer = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, pointer, bytes.Length);
                return _setText(pointer) == 0;
            }
            finally { Marshal.FreeHGlobal(pointer); }
        }

        private static RuntimeClipboard Create()
        {
            // Browser clipboard permissions are asynchronous; the synchronous contract reports unavailable.
            if (OperatingSystem.IsBrowser()) return new RuntimeClipboard(null, null, null);
            foreach (var libraryName in GetLibraryNames())
            {
                if (!NativeLibrary.TryLoad(libraryName, out var library)) continue;
                if (!NativeLibrary.TryGetExport(library, "SDL_GetClipboardText", out var getText) ||
                    !NativeLibrary.TryGetExport(library, "SDL_SetClipboardText", out var setText) ||
                    !NativeLibrary.TryGetExport(library, "SDL_free", out var free))
                {
                    NativeLibrary.Free(library);
                    continue;
                }
                return new RuntimeClipboard(
                    Marshal.GetDelegateForFunctionPointer<SdlGetClipboardText>(getText),
                    Marshal.GetDelegateForFunctionPointer<SdlSetClipboardText>(setText),
                    Marshal.GetDelegateForFunctionPointer<SdlFree>(free));
            }
            return new RuntimeClipboard(null, null, null);
        }

        private static IEnumerable<string> GetLibraryNames()
        {
            if (OperatingSystem.IsWindows()) yield return "SDL2.dll";
            else if (OperatingSystem.IsMacOS()) yield return "libSDL2-2.0.0.dylib";
            else yield return "libSDL2-2.0.so.0";
            yield return "SDL2";
        }
    }
}