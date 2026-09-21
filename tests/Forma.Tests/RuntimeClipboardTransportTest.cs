using System.Runtime.InteropServices;
using System.Text;

namespace Forma.Tests;

public sealed class RuntimeClipboardTransportTest
{
    [TestCase("WINDOWS", "SDL2.dll")]
    [TestCase("OSX", "libSDL2-2.0.0.dylib")]
    [TestCase("LINUX", "libSDL2-2.0.so.0")]
    public void DesktopLibrarySelectionUsesTheOwningRuntime(string platform, string sdl2)
    {
        var os = OSPlatform.Create(platform);
        Assert.That(SdlClipboardLibraries.ForWindow(null, os),
            Is.EqualTo(new[] { "mgruntime", sdl2, "SDL2" }));
        Assert.That(SdlClipboardLibraries.ForWindow("Microsoft.Xna.Framework.NativeGameWindow", os),
            Is.EqualTo(new[] { "mgruntime" }));
        Assert.That(SdlClipboardLibraries.ForWindow("Microsoft.Xna.Framework.SdlGameWindow", os),
            Is.EqualTo(new[] { sdl2, "SDL2" }));
        Assert.That(SdlClipboardLibraries.ForWindow("Microsoft.Xna.Framework.WinFormsGameWindow", os),
            Is.Empty);
    }

    private sealed class Exports
    {
        private readonly Dictionary<string, Delegate> _delegates = new();
        internal byte[] Bytes = Encoding.UTF8.GetBytes("日本語, ação, 😀");
        internal string Written;
        internal int Result2;
        internal bool Result3 = true;
        internal bool VideoInitialized = true;
        internal bool NullRead;
        internal int Reads;
        internal int Writes;
        internal int Frees;
        internal int Releases;
        internal int ErrorClears;
        internal IntPtr Error;
        internal IntPtr ReadError;
        internal bool Sdl2;
        internal bool Sdl3;
        internal string Missing;

        internal Exports(bool sdl3)
        {
            Sdl2 = !sdl3;
            Sdl3 = sdl3;
            _delegates["SDL_WasInit"] = new SdlClipboardTransport.WasInit(
                flags => VideoInitialized ? flags : 0);
            _delegates["SDL_GetClipboardText"] = new SdlClipboardTransport.GetClipboardText(() =>
            {
                Reads++;
                if (ReadError != IntPtr.Zero) Error = ReadError;
                if (NullRead) return IntPtr.Zero;
                var pointer = Marshal.AllocHGlobal(Bytes.Length + 1);
                Marshal.Copy(Bytes, 0, pointer, Bytes.Length);
                Marshal.WriteByte(pointer, Bytes.Length, 0);
                return pointer;
            });
            _delegates["SDL_SetClipboardText"] = sdl3
                ? new SdlClipboardTransport.SetClipboardText3(pointer => { Write(pointer); return Result3; })
                : new SdlClipboardTransport.SetClipboardText2(pointer => { Write(pointer); return Result2; });
            _delegates["SDL_free"] = new SdlClipboardTransport.Free(pointer =>
            {
                Frees++;
                Marshal.FreeHGlobal(pointer);
            });
            _delegates["SDL_ClearError"] = new SdlClipboardTransport.ClearError2(() =>
            {
                ErrorClears++;
                Error = IntPtr.Zero;
            });
            _delegates["SDL_GetError"] = new SdlClipboardTransport.GetClipboardText(() => Error);
        }

        private void Write(IntPtr pointer)
        {
            Writes++;
            Written = Marshal.PtrToStringUTF8(pointer);
        }

        internal IntPtr Find(string name)
        {
            if (name == Missing) return IntPtr.Zero;
            if (name == "SDL_GetWindowProperties") return Sdl3 ? (IntPtr)1 : IntPtr.Zero;
            if (name == "SDL_GetWindowWMInfo") return Sdl2 ? (IntPtr)1 : IntPtr.Zero;
            return _delegates.TryGetValue(name, out var function)
                ? Marshal.GetFunctionPointerForDelegate(function) : IntPtr.Zero;
        }

        internal SdlClipboardTransport Open() => SdlClipboardTransport.TryCreate(Find, () => Releases++);
    }

    [TestCase(0, true)]
    [TestCase(-1, false)]
    [TestCase(1, false)]
    [TestCase(256, false)]
    public void Sdl2OnlyZeroIsSuccess(int result, bool success)
    {
        var exports = new Exports(false) { Result2 = result };
        using var transport = exports.Open();
        Assert.That(transport.SetText("text"), Is.EqualTo(success));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Sdl3ReturnsItsBooleanResult(bool success)
    {
        var exports = new Exports(true) { Result3 = success };
        using var transport = exports.Open();
        Assert.That(transport.SetText("text"), Is.EqualTo(success));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Utf8RoundTripsAndNullWritesAnEmptyString(bool sdl3)
    {
        var exports = new Exports(sdl3);
        using var transport = exports.Open();
        const string text = "日本語, ação, 😀";
        Assert.That(transport.GetText(), Is.EqualTo(text));
        Assert.That(exports.Frees, Is.EqualTo(1));
        Assert.That(transport.SetText(text), Is.True);
        Assert.That(exports.Written, Is.EqualTo(text));
        Assert.That(transport.SetText(null), Is.True);
        Assert.That(exports.Written, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EmptyReadIsDistinctFromUnavailableAndMalformedReadStillFrees(bool sdl3)
    {
        var exports = new Exports(sdl3) { Bytes = Array.Empty<byte>() };
        using var transport = exports.Open();
        Assert.That(transport.GetText(), Is.Empty);
        exports.NullRead = true;
        Assert.That(transport.GetText(), Is.Null);
        Assert.That(exports.Frees, Is.EqualTo(1));
        exports.NullRead = false;
        exports.Bytes = new byte[] { 0xc3, 0x28 };
        Assert.That(transport.GetText(), Is.Null);
        Assert.That(exports.Frees, Is.EqualTo(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnrepresentableWritesAreNotSilentlyTruncated(bool sdl3)
    {
        var exports = new Exports(sdl3);
        using var transport = exports.Open();
        Assert.That(transport.SetText("prefix\0suffix"), Is.False);
        Assert.That(transport.SetText("\ud800"), Is.False);
        Assert.That(exports.Writes, Is.Zero);
    }

    [TestCase("SDL_GetClipboardText")]
    [TestCase("SDL_SetClipboardText")]
    [TestCase("SDL_free")]
    [TestCase("SDL_WasInit")]
    public void IncompleteLibraryReleasesItsReference(string missing)
    {
        var exports = new Exports(true) { Missing = missing };
        Assert.That(exports.Open(), Is.Null);
        Assert.That(exports.Releases, Is.EqualTo(1));
        Assert.That(exports.Reads + exports.Writes, Is.Zero);
    }

    [TestCase("SDL_ClearError")]
    [TestCase("SDL_GetError")]
    public void Sdl2RequiresItsErrorContract(string missing)
    {
        var exports = new Exports(false) { Missing = missing };
        Assert.That(exports.Open(), Is.Null);
        Assert.That(exports.Releases, Is.EqualTo(1));
    }

    [Test]
    public void Sdl2AllocatedEmptyErrorIsUnavailableAndStillFreed()
    {
        var error = Marshal.StringToCoTaskMemUTF8("clipboard unavailable");
        try
        {
            var exports = new Exports(false) { Bytes = Array.Empty<byte>(), Error = error };
            using var transport = exports.Open();
            Assert.That(transport.GetText(), Is.Empty, "A stale SDL error must not hide an empty clipboard.");
            Assert.That(exports.ErrorClears, Is.EqualTo(1));
            exports.ReadError = error;
            Assert.That(transport.GetText(), Is.Null);
            Assert.That(exports.Frees, Is.EqualTo(2));
            Assert.That(exports.ErrorClears, Is.EqualTo(2));
        }
        finally
        {
            Marshal.FreeCoTaskMem(error);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnknownOrAmbiguousAbiNeverInvokesClipboard(bool both)
    {
        var exports = new Exports(true) { Sdl2 = both, Sdl3 = both };
        Assert.That(exports.Open(), Is.Null);
        Assert.That(exports.Releases, Is.EqualTo(1));
        Assert.That(exports.Reads + exports.Writes, Is.Zero);
    }

    [Test]
    public void UninitializedRuntimeCanBeRetriedAndShutdownCannotAccessClipboard()
    {
        var exports = new Exports(true) { VideoInitialized = false };
        Assert.That(exports.Open(), Is.Null);
        Assert.That(exports.Releases, Is.EqualTo(1));
        exports.VideoInitialized = true;
        using var transport = exports.Open();
        Assert.That(transport, Is.Not.Null);
        Assert.That(transport.SetText("ready"), Is.True);
        exports.VideoInitialized = false;
        Assert.That(transport.GetText(), Is.Null);
        Assert.That(transport.SetText("shutdown"), Is.False);
        Assert.That(exports.Writes, Is.EqualTo(1));
        Assert.That(exports.Reads, Is.Zero);
    }

    [Test]
    public void DisposeReleasesModuleExactlyOnceAndPreventsFurtherAccess()
    {
        var exports = new Exports(true);
        var transport = exports.Open();
        Assert.That(exports.Releases, Is.Zero);
        transport.Dispose();
        transport.Dispose();
        Assert.That(exports.Releases, Is.EqualTo(1));
        Assert.That(transport.GetText(), Is.Null);
        Assert.That(transport.SetText("disposed"), Is.False);
        Assert.That(exports.Reads + exports.Writes, Is.Zero);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetResult(int result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FreeCount();

    [TestCase(false)]
    [TestCase(true)]
    public void IsolatedNativeFixtureExercisesActualAbiAndAllocator(bool sdl3)
    {
        var path = Environment.GetEnvironmentVariable("FORMA_CLIPBOARD_ABI_FIXTURE");
        if (string.IsNullOrEmpty(path))
            Assert.Ignore("Set FORMA_CLIPBOARD_ABI_FIXTURE to the compiled RuntimeClipboardAbiFixture.c library.");
        var library = NativeLibrary.Load(path);
        var releases = 0;
        try
        {
            var setResult = Marshal.GetDelegateForFunctionPointer<SetResult>(
                NativeLibrary.GetExport(library, "fixture_set_result"));
            var frees = Marshal.GetDelegateForFunctionPointer<FreeCount>(
                NativeLibrary.GetExport(library, "fixture_free_count"));
            IntPtr Find(string name) => name switch
            {
                "SDL_GetWindowProperties" => sdl3 ? (IntPtr)1 : IntPtr.Zero,
                "SDL_GetWindowWMInfo" => sdl3 ? IntPtr.Zero : (IntPtr)1,
                "SDL_SetClipboardText" => NativeLibrary.GetExport(library, sdl3 ? "fixture_set3" : "fixture_set2"),
                _ => NativeLibrary.TryGetExport(library, name, out var pointer) ? pointer : IntPtr.Zero
            };
            using (var transport = SdlClipboardTransport.TryCreate(Find, () => releases++))
            {
                foreach (var result in new[] { 0, 1, -1, 256 })
                {
                    setResult(result);
                    Assert.That(transport.SetText("日本語 ação 😀"), Is.EqualTo(sdl3 ? result != 0 : result == 0));
                }
                var before = frees();
                Assert.That(transport.GetText(), Is.EqualTo("日本語 ação 😀"));
                Assert.That(frees(), Is.EqualTo(before + 1));
                setResult(sdl3 ? 1 : 0);
                Assert.That(transport.SetText(null), Is.True);
                Assert.That(transport.GetText(), Is.Empty);
                Assert.That(frees(), Is.EqualTo(before + 2));
            }
            Assert.That(releases, Is.EqualTo(1));
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }
}
