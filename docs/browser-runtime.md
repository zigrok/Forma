# Browser runtime and replaceable compiled modules

Forma uses the normal XamlX/Cecil compiler for browser game modules. The output
is executable managed IL, not a XAML-data interpreter. The browser platform
must run an interpreter-enabled .NET runtime with managed AOT and trimming
disabled so that future assemblies can call APIs absent from the first module.
Do not ship `Forma.Xaml.Build`, `Forma.Xaml.Compiler`, XamlX, Cecil, or
development hot reload in the platform or module.

Code updates restart the page. Forma's global XAML registrations and the
browser runtime do not promise collectible unloading.

## Separate compiler and runtime builds

Run commands from the Forma repository. Build desktop tooling independently:

```sh
dotnet build src/Forma.Xaml.Build -c Release \
  -p:FormaBuildTarget=Desktop -p:MonoGameProjectPath=
```

The default desktop package can be replaced with an independent desktop source
`MonoGameProjectPath`. Never build the compiler against the browser-native
framework. `FormaBuildTarget=Browser` rejects compiler and hot-reload projects.

For runtime libraries and the game module, pass:

```sh
dotnet build path/to/Game.Module.csproj -c Release \
  -p:FormaBuildTarget=Browser \
  -p:MonoGameProjectPath=/absolute/path/to/MonoGame.Framework.Browser.csproj \
  -p:FormaXamlBuildAssembly=/absolute/path/to/Forma/src/Forma.Xaml.Build/bin/MonoGame/Release/net10.0/Forma.Xaml.Build.dll
```

The included `Resources/Alpha8Coverage.Browser.mgfxo.b64` uses native BrowserGL
profile 81 selected for `GraphicsBackend.WebGL` (value 5);
`FormaBrowserAlpha8EffectPath` can override it. DesktopGL MGFX is
not a fallback. Regenerate after changing the shader/native profile with
`bash native/dynamictext/build-browser-effect.sh`, after building the sibling
MonoGame native browser tools.

Import `src/Forma.Xaml.Build/buildTransitive/Forma.Xaml.Build.targets` in the
module. Do **not** add a browser project reference to the compiler. Runtime
outputs use `bin/MonoGame/Browser` and `obj/MonoGame/Browser`; desktop outputs
retain their existing locations. Standalone External-backend test builds use
`bin/MonoGame/External` and `obj/MonoGame/External`.

Compile plugin-owned views with `x:Class`; their constructors call
`FormaXamlLoader.Load(this)`. `Load(Type)` is for registered factories, not
`x:Class` population. Module initialization registers emitted code when the
host loads the assembly bytes. Share one Forma and one MonoGame assembly
identity with the host.

The existing comprehensive compiler integration fixture can be built as a
browser module using the same properties. Its browser build excludes the
desktop compiler project reference. A separate desktop loader validates the
same executable loading boundary:

```sh
dotnet build tests/Forma.Xaml.Build.Integration
dotnet run --project tests/Forma.Xaml.ModuleConsumer -- \
  tests/Forma.Xaml.Build.Integration/bin/MonoGame/Debug/net10.0/Forma.Xaml.Build.Integration.dll
```

This loads raw DLL bytes with no compile-time reference to the plugin, invokes
its `x:Class` constructor, and runs all existing integration assertions:
custom controls/events, typed bindings, resources, styles, templates,
triggers and storyboards. It checks shared runtime identity and absence of
compiler assemblies. A desktop result does not establish browser rendering.

## Clipboard and rendering

`new UIContext(hostClipboard)` accepts an `IClipboard` without initializing the
default native clipboard. The browser default reports unavailable (`null` on
read, `false` on write) without probing SDL2 or dynamic native libraries.
Browser clipboard permissions and operations are asynchronous; the synchronous
interface does not pretend those operations succeeded.

The browser Alpha8 coverage effect uses its own resource and **never** falls
back to DesktopGL MGFX. `FormaBrowserAlpha8EffectPath` defaults to the included
base64 bytecode generated for MonoGame's native browser shader profile from
`src/Forma/Resources/Alpha8Coverage.fx`. Missing bytecode throws a diagnostic
when the renderer is created. The native renderer must implement that profile,
SpriteBatch, BasicEffect, scissor, blending, render targets and texture upload.
Merely compiling the library does not establish that these operations work.

## Real DynamicText native ABI

Browser builds of `Forma.DynamicText` automatically select the existing
`External` hook with `Browser/BrowserDynamicTextBackend.cs`. They retain the
normal managed layout, bidi, fallback, grapheme and post-shaping reveal logic.
SFNT validation, collection indices and variation-axis parsing are shared with
the desktop backend.

`native/dynamictext/forma_dynamictext.h` defines ABI version 1. It uses opaque
face handles, fixed-width structs, UTF-16 cluster indices, explicit lengths,
and status codes. The C bridge owns copies of font bytes, FreeType faces and
HarfBuzz faces. Native shaping allocations are released explicitly. Managed
face operations serialize access and use safe handles. There are no reverse
callbacks or native-structure layout guesses.

The host's native build must:

1. Pin licensed FreeType and HarfBuzz sources and build them with the **same
   Emscripten toolchain as the .NET browser workload**.
2. Add CMake targets `freetype` and `harfbuzz` before
   `add_subdirectory(path/to/Forma/native/dynamictext)`. Avoid a circular
   FreeType/HarfBuzz integration dependency: the bridge uses HarfBuzz OpenType
   functions for shaping, not HarfBuzz's FreeType callbacks.
3. Build static `forma_dynamictext`, `freetype`, and `harfbuzz` archives and
   link them into the .NET host. Retain every `fdt_*` symbol referenced by
   `BrowserTextNative`; browser P/Invokes resolve from `libforma_dynamictext`.
   Do not substitute `__Internal`: the tested interpreter runtime did not
   resolve that reserved module name even though the generated table contained it.
4. Include Forma.DynamicText in the immutable shared managed platform.

`bash native/dynamictext/build-wasm.sh` performs a reproducible standalone build
with SDK 10.0.103, workload 10.0.110, and its Emscripten 3.1.56/10.0.10 packs.
It downloads SHA-256-verified FreeType 2.14.3 and HarfBuzz 14.4.0 source archives.
Archives are written to `artifacts/browser/native-text/lib`; distribute the
corresponding notices in `artifacts/browser/native-text/licenses`.

Import `native/dynamictext/Forma.DynamicText.Browser.Native.targets` into the
executable browser project (not game libraries). It adds the three archive
references, `-sSUPPORT_LONGJMP=wasm`, interpreter/untrimmed/native-build settings,
and fails if the archives are missing. It composes with MonoGame's native
targets. Keep a host project reference to `Forma.DynamicText` so publishing
discovers its native imports. `FormaBrowserNativeTextRoot` optionally selects
a different prepared native-text output directory.

The host must add `-sSUPPORT_LONGJMP=wasm` to `EmccExtraLDFlags` if not using
the targets. Libraries are
compiled with Wasm exceptions to match .NET. The pinned workload combines LLVM
19 with an older Emscripten compiler-runtime cache, which lacks LLVM 19's
`__wasm_setjmp` and `__wasm_setjmp_test` functions. The archive therefore includes
the SHA-256-pinned upstream Emscripten 3.1.68 `emscripten_setjmp.c` implementation.
This is the real compiler-runtime implementation, not no-op symbols, and does
not replace the installed toolchain. The browser probe checks nested invocations,
multiple jump destinations, and `longjmp(..., 0)` returning one.

The shaping ABI uses request/result pointers rather than a long argument list:
the latter exceeds the interpreter's native-call argument limit and aborts.
All scalar fields are fixed-width; pointer fields follow the target address size.

The CMake project refuses to resolve desktop packages during an Emscripten
build. Font files are **content**, not native build inputs. Use
`UIFontAsset.FromMemory` / `UIFontFace.FromMemory` after fetching and verifying
the selected content revision, then dispose obsolete faces after dependent
layouts/atlases are no longer used. No native relink is required for font updates.

When replacing a font pack, tear down the old `UIContext` before disposing its
faces, then construct the new context and family. Context disposal clears its
own layouts and the detached-control/text-metrics shared caches. Hosts that
use only detached controls may call `TextLayoutEngine.ClearSharedCaches()` on
the UI thread. Layout cache entries also distinguish primary/fallback face
ownership, so identical content hashes never resurrect disposed native faces.
Equivalent fonts backed by the same live faces still share layouts.

## Verification and remaining browser gates

The real bridge can also be tested against installed desktop FreeType/HarfBuzz
without substituting a synthetic font backend:

```sh
cmake -S native/dynamictext -B native/dynamictext/build \
  -DBUILD_SHARED_LIBS=ON -DCMAKE_BUILD_TYPE=Debug
cmake --build native/dynamictext/build
DYLD_LIBRARY_PATH="$PWD/native/dynamictext/build" dotnet test tests/Forma.BrowserText.Tests \
  -p:FormaDynamicTextBackend=External \
  -p:FormaDynamicTextBackendSource="$PWD/src/Forma.DynamicText/Browser/BrowserDynamicTextBackend.cs" \
  --filter 'FullyQualifiedName!~DefaultBackendReports&FullyQualifiedName!~RepeatedCreateAndDispose'
```

Use `LD_LIBRARY_PATH` instead on Linux. The two excluded inherited assertions
describe the desktop wrapper package names and pinned managed bytes; dedicated
ABI tests check the real bridge's identity, copying ownership, disposal, and
zero managed font pins instead. All other inherited font tests run, including
reference Arabic glyphs/positions, variable weights, malformed font/text fuzz,
font collections, source reloading and raster bounds. Dedicated tests cover
Latin locales, bidi/fallback, fixed-width ABI sizes, and post-shaping reveal.

`tests/Forma.BrowserText.Module` is a separate executable-code probe. Its public
`BrowserTextProbe.Run(IReadOnlyDictionary<string, byte[]>)` requires real
font bytes for **en, ar, es-419, ja, ko, pt-BR, zh-Hans**. It verifies nonmissing
shaped glyphs, nonzero raster coverage, Arabic bidi, and stable glyph positions
during reveal. Missing locale fonts are errors, not skipped tests.

The bundled tiny CJK fixture is not a production font pack. Licensed full
Japanese, Korean and Simplified-Chinese fonts with regional Han forms, plus
Latin and Arabic fonts, must be delivered as versioned content. The seven-locale
probe runs in the actual browser host with those fonts and native archives:

```sh
bash tests/Forma.Browser.Probe/prepare.sh
node tests/Forma.Browser.Probe/serve.mjs \
  tests/Forma.Browser.Probe/bin/MonoGame/Browser/Release/net10.0/publish/wwwroot 5196
```

First build the sibling MonoGame native libraries and shader tools with
`bash ../MonoGame/native/browser/build.sh`. The probe executable imports
`MonoGame.Browser.Native.targets`, and its canvas is named `canvas`. The host
owns a single animation callback, calling `BrowserGameLoop.Tick(game)`.

Preparation builds and freezes the platform **before** building the two
independent probe modules, then verifies every platform file hash is unchanged.
It fetches full regional Noto CJK fonts at the source revision and hashes in
`tests/Forma.Browser.Probe/fetch-fonts.sh`, and copies the existing Latin/Arabic
fonts and licenses as external content. Open `http://127.0.0.1:5196/`.

This proof passes in Chromium, Firefox and WebKit with identical native shaped
glyph counts and raster-coverage sums for all seven locales. It runs the full
compiled-XAML integration assertions, checks browser clipboard initialization,
shared assembly identities, native `setjmp`, native text and post-shaping reveal.
It then instantiates seven separately compiled, typed-bound XAML labels and
renders them through real `UIContext`, the dynamic glyph atlas, native BrowserGL
Alpha8 effect and WebGL2. GPU readback checks ink in every locale row, reduced
coverage after revealing one grapheme, warm-atlas restoration, and deliberately
narrowed text clips. Final screenshots show the restored complete text.
The probe then disposes every context/face, reloads identical font bytes, and
requires the recreated compiled UI to produce the same GPU output.
GPU edge pixels differ slightly across browser backends; comparisons are
within each browser, not a false cross-browser bit-identity requirement.
Full story-text coverage, complete application composition, and actual
Safari/Edge release verification remain separate gates.

If Playwright and its browser binaries are already installed, the automated
proof can use them without adding dependencies:

```sh
node tests/Forma.Browser.Probe/verify.mjs \
  ../frontend/node_modules/@playwright/test/index.mjs http://127.0.0.1:5196/
```

It saves per-browser screenshots and a machine-readable result under
`artifacts/browser`. A missing dependency or browser executable is an error,
not an ignored/skipped pass.
