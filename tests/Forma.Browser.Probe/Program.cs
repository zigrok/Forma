using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Runtime.Loader;
using Forma;
using Forma.Xaml;

return;

[SupportedOSPlatform("browser")]
public static partial class BrowserProbe
{
    private static bool _started;
    private static BrowserGraphicsGame? _game;

    [JSExport]
    public static async Task<string> Run(string baseUrl)
    {
        if (_started) throw new InvalidOperationException("Restart the page before loading another executable revision.");
        _started = true;
        if (!OperatingSystem.IsBrowser()) throw new InvalidOperationException("This proof requires the real browser runtime.");
        if (ProbeLongjmp() != 1) throw new InvalidOperationException("The native setjmp ABI failed nested/multiple-jump tests.");
        Console.WriteLine("Native nested/multiple setjmp ABI: PASS");
        using var context = new UIContext();
        if (context.Clipboard.GetText() != null || context.Clipboard.SetText("must not report success"))
            throw new InvalidOperationException("Browser synchronous clipboard did not report unavailable.");
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var xaml = await LoadModule(http, "modules/Forma.Xaml.Build.Integration.dll");
        var viewType = xaml.GetType("Forma.Xaml.Build.Integration.HudView", throwOnError: true)!;
        var view = (Control)Activator.CreateInstance(viewType)!;
        if (NameScope.GetNameScope(view)?.Find<Label>("Child") == null)
            throw new InvalidOperationException("The plugin constructor did not populate compiled XAML.");
        if ((int)xaml.EntryPoint!.Invoke(null, null)! != 0)
            throw new InvalidOperationException("Full compiled XAML integration assertions failed.");
        Console.WriteLine("Separately compiled XAML module: PASS");

        var fonts = new Dictionary<string, byte[]>();
        var paths = new Dictionary<string, string>
        {
            ["en"] = "Inter_Regular.ttf", ["es-419"] = "Inter_Regular.ttf", ["pt-BR"] = "Inter_Regular.ttf",
            ["ar"] = "NotoSansArabic_Variable.ttf", ["ja"] = "NotoSansCJKjp-Regular.otf",
            ["ko"] = "NotoSansCJKkr-Regular.otf", ["zh-Hans"] = "NotoSansCJKsc-Regular.otf",
        };
        var byPath = new Dictionary<string, byte[]>();
        foreach (var (locale, path) in paths)
        {
            if (!byPath.TryGetValue(path, out var bytes))
                byPath[path] = bytes = await http.GetByteArrayAsync("fonts/" + path);
            fonts[locale] = bytes;
        }
        var text = await LoadModule(http, "modules/Forma.BrowserText.Module.dll");
        var result = (string)text.GetType("Forma.BrowserText.Module.BrowserTextProbe", throwOnError: true)!
            .GetMethod("Run")!.Invoke(null, new object[] { fonts })!;
        foreach (var name in new[] { "Forma", "Forma.DynamicText", "MonoGame.Framework" })
            if (AppDomain.CurrentDomain.GetAssemblies().Count(a => a.GetName().Name == name) != 1)
                throw new InvalidOperationException($"Duplicate shared runtime assembly: {name}.");
        if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "Forma.Xaml.Compiler" or "XamlX" or "Mono.Cecil"))
            throw new InvalidOperationException("Desktop tooling loaded in the browser runtime.");
        var glyphViewType = xaml.GetType("Forma.Xaml.Build.Integration.BrowserGlyphView", throwOnError: true)!;
        _game = new BrowserGraphicsGame(fonts, sample => (Label)Activator.CreateInstance(glyphViewType, sample)!);
        _game.Run();
        return $"PASS compiled-XAML + linked FT/HB + reloadable fonts: {result}";
    }

    [JSExport]
    public static bool Tick() => MonoGame.Framework.BrowserGameLoop.Tick(_game ?? throw new InvalidOperationException("Run the proof first."));

    [JSExport]
    public static string GraphicsResult() => _game?.Result ?? "";

    private static async Task<Assembly> LoadModule(HttpClient http, string path)
    {
        var bytes = await http.GetByteArrayAsync(path);
        using var stream = new MemoryStream(bytes, writable: false);
        var assembly = AssemblyLoadContext.Default.LoadFromStream(stream);
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        return assembly;
    }

    [DllImport("libforma_dynamictext", EntryPoint = "forma_probe_longjmp")]
    private static extern int ProbeLongjmp();
}
