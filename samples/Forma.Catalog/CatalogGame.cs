// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Forma;
using Forma.Accessibility;
#if FORMA_XAML_HOT_RELOAD
using Forma.Xaml.HotReload;
#endif
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Forma.Catalog;

public sealed class CatalogGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly UIContext _ui;
    private readonly RuntimeCatalogTextInputAdapter _textInput;
    private readonly Stopwatch _startupTimer = Stopwatch.StartNew();
    private CatalogShell _catalog;
    private SpriteFont _font;
    private SpriteFont _codeFont;
    private UIFontFace _interFace;
    private UIFontFace _cjkFace;
    private UIFontFace _arabicFace;
    private UIFontFace _devanagariFace;
    private UIFontFace _hebrewFace;
    private UIFontFace _emojiFace;
    private Texture2D _catalogTexture;
    private readonly CatalogMetricsOptions _metricsOptions;
    private readonly VertexPositionColorTexture[] _hotReloadVertices =
    {
        new(new Vector3(0.82f, -0.92f, 0), Color.White, new Vector2(0, 1)),
        new(new Vector3(0.9f, -0.72f, 0), Color.White, new Vector2(0.5f, 0)),
        new(new Vector3(0.98f, -0.92f, 0), Color.White, new Vector2(1, 1)),
    };
    private CatalogEffectHotReloadService _hotReload;
    private AccessibilityBridge _accessibility;
    private ILiveResizeAdapter _liveResize;
#if FORMA_XAML_HOT_RELOAD
    private FormaXamlHotReloadService _xamlHotReload;
    private IDisposable _xamlHotReloadRegistration;
    private IDisposable _activeStoryHotReloadRegistration;
#endif
    private float _displayScale = 1;
    private float? _interactiveDisplayScale;
    private Vector2? _interactiveLogicalViewport;
    private int _storyCount;
    private bool _dynamicTextEnabled = true;
    private int _renderedFrames;
    private double _startupMilliseconds;
    private long _steadyStateAllocationStart;
    private long _steadyStateAllocatedBytes;
    private int _steadyStateMeasuredFrames;

    public CatalogGame(CatalogMetricsOptions metricsOptions = null)
    {
        _metricsOptions = metricsOptions;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = _metricsOptions?.ViewportWidth ?? 1440,
            PreferredBackBufferHeight = _metricsOptions?.ViewportHeight ?? 900,
        };
        EnableHighDpiIfSupported(_graphics);
        _ui = new UIContext
        {
            Theme = CreateTheme(),
            ThemeIconRenderingPolicy = _metricsOptions?.ThemeIconPolicy ?? (SvgRuntime.Health.IsAvailable
                ? ThemeIconRenderingPolicy.RuntimeSvg
                : ThemeIconRenderingPolicy.BitmapAtlas),
        };
        if (_metricsOptions != null) _ui.TooltipDelay = TimeSpan.MaxValue;
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        Window.Title = CatalogBackend.WindowTitle;
        _textInput = new RuntimeCatalogTextInputAdapter(this, _ui.TextInput);
    }

    private static void EnableHighDpiIfSupported(GraphicsDeviceManager graphics)
    {
        graphics.GetType().GetProperty("AllowHighDpi")?.SetValue(graphics, true);
    }

    protected override void LoadContent()
    {
        _font = Content.Load<SpriteFont>("Fonts/Catalog");
        _codeFont = Content.Load<SpriteFont>("Fonts/CatalogCode");
        _interFace = UIFontFace.FromProjectFile(AppContext.BaseDirectory, "Fonts/Inter_Regular.ttf");
        _cjkFace = UIFontFace.FromProjectFile(AppContext.BaseDirectory, "Fonts/NotoSansCJK_Subset.ttf");
        _arabicFace = UIFontFace.FromProjectFile(AppContext.BaseDirectory, "Fonts/NotoSansArabic_Variable.ttf");
        _devanagariFace = UIFontFace.FromProjectFile(AppContext.BaseDirectory, "Fonts/NotoSansDevanagari_Subset.ttf");
        _hebrewFace = UIFontFace.FromProjectFile(AppContext.BaseDirectory, "Fonts/NotoSansHebrew_Subset.ttf");
        _emojiFace = UIFontFace.FromProjectFile(AppContext.BaseDirectory, "Fonts/NotoEmoji_Subset.ttf");
        _catalogTexture = CreateCatalogTexture();
        var defaultFont = CreateDynamicFont("Inter", 16, null);
        _ui.Theme.FontFamily = new UIFontFamily(new[] { defaultFont });
        _ui.TooltipUIFont = defaultFont;
        var stories = StoryCatalog.Create(_catalogTexture, SetInteractiveDisplayScale, CreateDynamicFont);
        _storyCount = stories.Count;
        _catalog = new CatalogShell(
            stories,
            defaultFont,
            CreateDynamicFont("Inter", 15, null),
            _font,
            _codeFont,
            enabled => _dynamicTextEnabled = enabled);
        if (_metricsOptions?.LayoutDirection != null) _catalog.LayoutDirection = _metricsOptions.LayoutDirection.Value;
        _ui.Add(_catalog);
        _liveResize = LiveResizeAdapter.TryCreate(this);
        StartAccessibility();
    #if FORMA_XAML_HOT_RELOAD
        StartXamlHotReload();
    #endif
        if (_metricsOptions?.StoryName != null && !_catalog.SelectStory(_metricsOptions.StoryName))
            throw new InvalidOperationException($"Catalog story not found: {_metricsOptions.StoryName}");
        if (_metricsOptions?.WatchedEffectPath != null)
        {
            _hotReload = new CatalogEffectHotReloadService(
                GraphicsDevice,
                Path.GetFullPath(_metricsOptions.WatchedEffectPath));
        }
        base.LoadContent();
    }

    /// <summary>
    /// Publishes the Catalog's tree to the operating system when <c>FORMA_ACCESSIBILITY=1</c> is
    /// set, so a screen reader or an automation client sees the controls rather than one opaque
    /// window.
    /// </summary>
    /// <remarks>
    /// An environment variable rather than a flag: the Catalog's arguments are the metrics harness's,
    /// and this has nothing to do with metrics. It says which of the two outcomes happened, because
    /// "off" and "on but empty" are indistinguishable from outside.
    /// </remarks>
    private void StartAccessibility()
    {
        if (Environment.GetEnvironmentVariable("FORMA_ACCESSIBILITY") != "1") return;

    #if FORMA_CATALOG_PLATFORM_HANDLE
        var windowHandle = Window.PlatformHandle;
    #else
        var windowHandle = IntPtr.Zero;
    #endif

        _accessibility = new AccessibilityBridge(_ui) { WindowTitle = Window.Title };
        if (_accessibility.Attach(windowHandle))
        {
            Console.WriteLine("accessibility: attached");
            return;
        }

        Console.WriteLine(windowHandle == IntPtr.Zero
            ? "accessibility: this runtime exposes no native window handle, so nothing was attached"
            : "accessibility: the platform adapter could not be started");
        _accessibility.Dispose();
        _accessibility = null;
    }

    protected override void Update(GameTime gameTime)
    {
        if (_metricsOptions == null && Keyboard.GetState().IsKeyDown(Keys.Escape)) Exit();
        var viewport = GraphicsDevice.Viewport;
        var liveViewport = default(Vector2);
        var hasLiveViewport = _metricsOptions == null && _liveResize?.TryGetLogicalViewport(out liveViewport) == true;
        _displayScale = _interactiveDisplayScale ?? (hasLiveViewport ? viewport.Width / liveViewport.X : GetDisplayScale(viewport));
        _ui.DisplayScale = _displayScale;
        _ui.ViewportSize = _interactiveLogicalViewport ?? (hasLiveViewport ? liveViewport : new Vector2(viewport.Width / _displayScale, viewport.Height / _displayScale));
        if (_catalog != null) _catalog.Size = _ui.ViewportSize;
        if (_metricsOptions == null)
        {
            var mouse = Mouse.GetState();
            var keyboard = Keyboard.GetState();
            var target = _ui.HitTest(new Point(mouse.X, mouse.Y));
            try
            {
                _ui.Update(gameTime, mouse, keyboard);
            }
            catch (Exception exception)
            {
                throw CreateInteractionException(_catalog?.ActiveStory, target, exception);
            }
        }
        else _ui.Update(gameTime, default, default);
        base.Update(gameTime);
    }

    internal static InvalidOperationException CreateInteractionException(ComponentStory story, Control target, Exception innerException)
    {
        if (innerException == null) throw new ArgumentNullException(nameof(innerException));
        var storyName = story == null ? "<no active story>" : $"{story.Category} / {story.Name}";
        var path = new List<string>();
        for (var control = target; control != null && path.Count < 8; control = control.VisualParent)
            path.Add(string.IsNullOrWhiteSpace(control.Name) ? control.GetType().Name : $"{control.GetType().Name}#{control.Name}");
        var controlPath = path.Count == 0 ? "<no hit control>" : string.Join(" -> ", path);
        return new InvalidOperationException(
            $"Catalog interaction failed in '{storyName}' at {controlPath}: {innerException.Message}",
            innerException);
    }

    internal static InvalidOperationException CreateDrawException(ComponentStory story, Exception innerException)
    {
        if (innerException == null) throw new ArgumentNullException(nameof(innerException));
        var storyName = story == null ? "<no active story>" : $"{story.Category} / {story.Name}";
        return new InvalidOperationException(
            $"Catalog draw failed in '{storyName}': {innerException.Message}",
            innerException);
    }

    protected override void Draw(GameTime gameTime)
    {
        // After layout has settled for the frame: bounds are what an assistive technology
        // hit-tests with, so a tree captured mid-layout describes a UI that is about to move.
        _accessibility?.Update();
        GraphicsDevice.Clear(_ui.Theme.BackgroundColor);
        try
        {
            _ui.Draw(GraphicsDevice);
        }
        catch (Exception exception)
        {
            throw CreateDrawException(_catalog?.ActiveStory, exception);
        }
        if (_hotReload != null)
        {
            _hotReload.Update();
            var effect = _hotReload.Current;
            effect.Parameters["MatrixTransform"]?.SetValue(Matrix.Identity);
            effect.Parameters["Texture"]?.SetValue(_catalogTexture);
            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserPrimitives(
                    PrimitiveType.TriangleList,
                    _hotReloadVertices,
                    0,
                    1);
            }
        }
        base.Draw(gameTime);

        if (_metricsOptions != null)
        {
            _renderedFrames++;
            if (_renderedFrames == 1)
            {
                _startupMilliseconds = _startupTimer.Elapsed.TotalMilliseconds;
                _steadyStateAllocationStart = GC.GetAllocatedBytesForCurrentThread();
            }
            if (_renderedFrames >= _metricsOptions.FrameCount)
            {
                _steadyStateMeasuredFrames = Math.Max(0, _renderedFrames - 1);
                _steadyStateAllocatedBytes = _steadyStateMeasuredFrames == 0
                    ? 0
                    : Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - _steadyStateAllocationStart);
                if (_metricsOptions.OutputPath != null) WriteMetrics();
                if (_metricsOptions.RenderOutputPath != null) WriteRenderOutput();
                if (_metricsOptions.ScreenshotPath != null) WriteScreenshot();
                Exit();
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _liveResize?.Dispose();
            _textInput.Dispose();
#if FORMA_XAML_HOT_RELOAD
            if (_catalog != null) _catalog.ActiveStoryChanged -= RegisterActiveStory;
            _activeStoryHotReloadRegistration?.Dispose();
            _xamlHotReloadRegistration?.Dispose();
            _xamlHotReload?.Dispose();
#endif
            _hotReload?.Dispose();
            _ui.Dispose();
            _emojiFace?.Dispose();
            _devanagariFace?.Dispose();
            _hebrewFace?.Dispose();
            _arabicFace?.Dispose();
            _cjkFace?.Dispose();
            _interFace?.Dispose();
            _catalogTexture?.Dispose();
        }
        base.Dispose(disposing);
    }

#if FORMA_XAML_HOT_RELOAD
    private void StartXamlHotReload()
    {
        if (!RuntimeFeature.IsDynamicCodeSupported || !RuntimeFeature.IsDynamicCodeCompiled) return;
        string sourceRoot = null;
        foreach (var metadata in typeof(CatalogGame).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            if (metadata.Key == "FormaCatalogXamlRoot") sourceRoot = metadata.Value;
        if (sourceRoot == null) throw new InvalidOperationException("The Debug catalog build did not provide its XAML source root.");
        _xamlHotReload = new FormaXamlHotReloadService(_ui, sourceRoot);
        _xamlHotReload.DiagnosticsChanged += diagnostics =>
        {
            foreach (var diagnostic in diagnostics) Console.Error.WriteLine(diagnostic);
            _catalog.ReportHotReloadDiagnostics(
                diagnostics.Count,
                string.Join(Environment.NewLine + Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString())));
        };
        _xamlHotReloadRegistration = _xamlHotReload.Register<Control>("CatalogShell.xaml", () => _catalog, (_, replacement) =>
        {
            if (replacement is not BoxContainer shell) throw new InvalidOperationException("CatalogShell.xaml must have a BoxContainer root.");
            _catalog.ApplyHotReloadedTree(shell);
        });
        _catalog.ActiveStoryChanged += RegisterActiveStory;
        RegisterActiveStory(_catalog.ActiveStory, _catalog.ActiveStoryControl);
        _catalog.ReportHotReloadDiagnostics(0, null);
    }

    private void RegisterActiveStory(ComponentStory story, Control root)
    {
        _activeStoryHotReloadRegistration?.Dispose();
        _activeStoryHotReloadRegistration = null;
        if (story?.XamlPath == null || root == null) return;
        _activeStoryHotReloadRegistration = _xamlHotReload.Register<Control>(story.XamlPath, () => _catalog.ActiveStoryControl, (oldRoot, replacement) =>
        {
            _catalog.ReplaceActiveStory(oldRoot, replacement);
        });
    }
#endif

    private UIFont CreateDynamicFont(string familyName, float size, IReadOnlyList<UIFontVariationCoordinate> variations)
    {
        if (!_dynamicTextEnabled) return new SpriteFontAdapter(_font, size);
        variations ??= Array.Empty<UIFontVariationCoordinate>();
        if (familyName == "Noto Sans Arabic") return new DynamicUIFont(_arabicFace, size, UIFontHinting.Light, variations, _interFace, _hebrewFace, _devanagariFace, _cjkFace, _emojiFace);
        if (familyName == "Noto Sans SC") return new DynamicUIFont(_cjkFace, size, UIFontHinting.Light, variations, _interFace, _arabicFace, _hebrewFace, _devanagariFace, _emojiFace);
        return new DynamicUIFont(_interFace, size, UIFontHinting.Light, variations, _arabicFace, _hebrewFace, _devanagariFace, _cjkFace, _emojiFace);
    }

    private float GetDisplayScale(Viewport viewport)
    {
        if (_metricsOptions?.DisplayScale is float displayScale) return displayScale;
        var clientBounds = Window.ClientBounds;
        if (clientBounds.Width <= 0 || viewport.Width <= 0) return 1f;
        var scale = viewport.Width / (float)clientBounds.Width;
        return float.IsFinite(scale) && scale > 0 ? scale : 1f;
    }

    private void SetInteractiveDisplayScale(float displayScale)
    {
        _interactiveLogicalViewport ??= _ui.ViewportSize;
        _interactiveDisplayScale = displayScale;
        _ui.DisplayScale = displayScale;
        _ui.ViewportSize = _interactiveLogicalViewport.Value;
    }

    private void WriteMetrics()
    {
        var iconDiagnostics = _ui.ThemeIconDiagnostics;
        var glyphDiagnostics = _ui.DynamicGlyphDiagnostics;
        var spriteFontTextureBytes = GetSpriteFontTextureBytes(_font) + GetSpriteFontTextureBytes(_codeFont);
        var catalogTextureBytes = checked((long)_catalogTexture.Width * _catalogTexture.Height * 4);
        var report = new
        {
            schemaVersion = 2,
            backend = CatalogBackend.Name,
            renderedFrames = _renderedFrames,
            storyCount = _storyCount,
            selectedStory = _metricsOptions.StoryName,
            displayScale = _displayScale,
            physicalViewportWidth = GraphicsDevice.PresentationParameters.BackBufferWidth,
            physicalViewportHeight = GraphicsDevice.PresentationParameters.BackBufferHeight,
            logicalViewportWidth = _ui.ViewportSize.X,
            logicalViewportHeight = _ui.ViewportSize.Y,
            densityFontSelected = _displayScale > 1,
            startupMilliseconds = _startupMilliseconds,
            steadyStateMeasuredFrames = _steadyStateMeasuredFrames,
            steadyStateAllocatedBytes = _steadyStateAllocatedBytes,
            steadyStateAllocatedBytesPerFrame = _steadyStateMeasuredFrames == 0
                ? 0
                : _steadyStateAllocatedBytes / (double)_steadyStateMeasuredFrames,
            fontXnbBytes = GetFontXnbBytes(),
            spriteFontTextureBytes,
            steadyStateTextureBytes = iconDiagnostics.TextureBytes + spriteFontTextureBytes + catalogTextureBytes,
            themeIconDensity = iconDiagnostics.ActiveDensity,
            themeIconAtlasCount = iconDiagnostics.AtlasCount,
            themeIconTextureBytes = iconDiagnostics.TextureBytes,
            themeIconGeneration = iconDiagnostics.Generation,
            themeIconMissingCount = iconDiagnostics.MissingIconCount,
            themeIconPolicy = _ui.ThemeIconRenderingPolicy.ToString(),
            layoutDirection = _catalog.LayoutDirection.ToString(),
            themeIconRuntimeSvgCount = iconDiagnostics.RuntimeSvgIconCount,
            themeIconBitmapFallbackCount = iconDiagnostics.BitmapFallbackCount,
            svgBackendName = SvgRuntime.Health.Name,
            svgBackendId = SvgRuntime.Health.BackendId,
            svgBackendVersion = SvgRuntime.Health.Version,
            svgBackendProfile = SvgRuntime.Health.ProfileVersion,
            svgBackendNativeAvailability = SvgRuntime.Health.NativeAvailability.ToString(),
            svgBackendLinkMode = SvgRuntime.Health.LinkMode.ToString(),
            svgBackendAvailable = SvgRuntime.Health.IsAvailable,
            svgRasterEntries = _ui.SvgRasterDiagnostics.EntryCount,
            svgRasterBytes = _ui.SvgRasterDiagnostics.Bytes,
            dynamicGlyphPageCount = glyphDiagnostics.PageCount,
            dynamicGlyphCount = glyphDiagnostics.GlyphCount,
            dynamicGlyphBytes = glyphDiagnostics.Bytes,
            dynamicGlyphPendingUploads = glyphDiagnostics.PendingUploads,
            dynamicGlyphFailures = glyphDiagnostics.Failures,
            dynamicGlyphLastFailure = glyphDiagnostics.LastFailure,
            hotReloadEnabled = _hotReload != null,
            hotReloadSucceeded = _hotReload?.LastReloadSucceeded,
            hotReloadMilliseconds = _hotReload?.LastReloadMilliseconds,
            hotReloadMessage = _hotReload?.LastReloadMessage,
        };
        var outputPath = Path.GetFullPath(_metricsOptions.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
    }

    private static long GetSpriteFontTextureBytes(SpriteFont font)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var texture = typeof(SpriteFont).GetProperty("Texture", flags)?.GetValue(font) as Texture2D ??
            typeof(SpriteFont).GetField("textureValue", flags)?.GetValue(font) as Texture2D;
        if (texture == null) throw new InvalidOperationException("The selected runtime does not expose its SpriteFont atlas to catalog metrics.");
        return checked((long)texture.Width * texture.Height * 4);
    }

    private static long GetFontXnbBytes()
    {
        var fontDirectory = Path.Combine(AppContext.BaseDirectory, "Content", "Fonts");
        long bytes = 0;
        foreach (var asset in new[] { "Catalog", "CatalogCode" })
        {
            var path = Path.Combine(fontDirectory, asset + ".xnb");
            bytes = checked(bytes + new FileInfo(path).Length);
        }
        return bytes;
    }

    private void WriteRenderOutput()
    {
        var width = GraphicsDevice.PresentationParameters.BackBufferWidth;
        var height = GraphicsDevice.PresentationParameters.BackBufferHeight;
        var pixels = new Color[width * height];
        GraphicsDevice.GetBackBufferData(pixels);
        var hash = 14695981039346656037UL;
        var background = _ui.Theme.BackgroundColor.PackedValue;
        long nonBackgroundPixels = 0;
        ulong redTotal = 0;
        ulong greenTotal = 0;
        ulong blueTotal = 0;
        ulong alphaTotal = 0;
        long edgeTransitions = 0;
        ulong edgeStrength = 0;
        foreach (var pixel in pixels)
        {
            hash = (hash ^ pixel.PackedValue) * 1099511628211UL;
            if (pixel.PackedValue != background) nonBackgroundPixels++;
            redTotal += pixel.R;
            greenTotal += pixel.G;
            blueTotal += pixel.B;
            alphaTotal += pixel.A;
        }
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var pixel = pixels[y * width + x];
            if (x + 1 < width) AccumulateEdge(pixel, pixels[y * width + x + 1], ref edgeTransitions, ref edgeStrength);
            if (y + 1 < height) AccumulateEdge(pixel, pixels[(y + 1) * width + x], ref edgeTransitions, ref edgeStrength);
        }
        var report = new
        {
            schemaVersion = 1,
            width,
            height,
            pixelHash = hash.ToString("x16"),
            nonBackgroundPixels,
            redTotal,
            greenTotal,
            blueTotal,
            alphaTotal,
            edgeTransitions,
            edgeStrength,
        };
        var outputPath = Path.GetFullPath(_metricsOptions.RenderOutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
    }

    private static void AccumulateEdge(Color first, Color second, ref long transitions, ref ulong strength)
    {
        var difference = Math.Abs(first.R - second.R) + Math.Abs(first.G - second.G) +
            Math.Abs(first.B - second.B) + Math.Abs(first.A - second.A);
        strength += (uint)difference;
        if (difference >= 64) transitions++;
    }

    private void WriteScreenshot()
    {
        var width = GraphicsDevice.PresentationParameters.BackBufferWidth;
        var height = GraphicsDevice.PresentationParameters.BackBufferHeight;
        var pixels = new Color[width * height];
        GraphicsDevice.GetBackBufferData(pixels);
        using var texture = new Texture2D(GraphicsDevice, width, height);
        texture.SetData(pixels);
        var outputPath = Path.GetFullPath(_metricsOptions.ScreenshotPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        using var stream = File.Create(outputPath);
        texture.SaveAsPng(stream, width, height);
    }

    private Texture2D CreateCatalogTexture()
    {
        const int size = 48;
        var texture = new Texture2D(GraphicsDevice, size, size);
        var pixels = new Color[size * size];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var checker = (x / 8 + y / 8) % 2 == 0;
            pixels[y * size + x] = checker ? new Color(48, 185, 164) : new Color(246, 185, 73);
        }
        texture.SetData(pixels);
        return texture;
    }

    private static Theme CreateTheme() => new Theme
    {
        BackgroundColor = new Color(20, 24, 31),
        PanelColor = new Color(29, 35, 45),
        PanelBorderColor = new Color(56, 66, 82),
        TextColor = new Color(235, 239, 246),
        DisabledTextColor = new Color(143, 153, 170),
        AccentColor = new Color(48, 185, 164),
        HoverColor = new Color(43, 52, 66),
        PressedColor = new Color(23, 28, 36),
        FocusColor = new Color(246, 185, 73),
    };
}

public sealed class CatalogMetricsOptions
{
    public string OutputPath { get; }
    public string RenderOutputPath { get; }
    public string ScreenshotPath { get; }
    public int FrameCount { get; }
    public string WatchedEffectPath { get; }
    public float? DisplayScale { get; }
    public string StoryName { get; }
    public int? ViewportWidth { get; }
    public int? ViewportHeight { get; }
    public ThemeIconRenderingPolicy? ThemeIconPolicy { get; }
    public LayoutDirection? LayoutDirection { get; }

    private CatalogMetricsOptions(
        string outputPath,
        string renderOutputPath,
        string screenshotPath,
        int frameCount,
        string watchedEffectPath,
        float? displayScale,
        string storyName,
        int? viewportWidth,
        int? viewportHeight,
        ThemeIconRenderingPolicy? themeIconPolicy,
        LayoutDirection? layoutDirection)
    {
        OutputPath = outputPath;
        RenderOutputPath = renderOutputPath;
        ScreenshotPath = screenshotPath;
        FrameCount = frameCount;
        WatchedEffectPath = watchedEffectPath;
        DisplayScale = displayScale;
        StoryName = storyName;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        ThemeIconPolicy = themeIconPolicy;
        LayoutDirection = layoutDirection;
    }

    public static CatalogMetricsOptions Parse(string[] args)
    {
        string outputPath = null;
        string renderOutputPath = null;
        string screenshotPath = null;
        string watchedEffectPath = null;
        float? displayScale = null;
        string storyName = null;
        int? viewportWidth = null;
        int? viewportHeight = null;
        ThemeIconRenderingPolicy? themeIconPolicy = null;
        LayoutDirection? layoutDirection = null;
        var frameCount = 120;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--metrics" when index + 1 < args.Length:
                    outputPath = args[++index];
                    break;
                case "--render-output" when index + 1 < args.Length:
                    renderOutputPath = args[++index];
                    break;
                case "--screenshot" when index + 1 < args.Length:
                    screenshotPath = args[++index];
                    break;
                case "--frames" when index + 1 < args.Length && int.TryParse(args[++index], out frameCount) && frameCount > 0:
                    break;
                case "--watch-effect" when index + 1 < args.Length:
                    watchedEffectPath = args[++index];
                    break;
                case "--display-scale" when index + 1 < args.Length &&
                    float.TryParse(args[++index], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsedScale) &&
                    float.IsFinite(parsedScale) && parsedScale > 0:
                    displayScale = parsedScale;
                    break;
                case "--story" when index + 1 < args.Length:
                    storyName = args[++index];
                    break;
                case "--viewport-width" when index + 1 < args.Length && int.TryParse(args[++index], out var parsedWidth) && parsedWidth >= 320:
                    viewportWidth = parsedWidth;
                    break;
                case "--viewport-height" when index + 1 < args.Length && int.TryParse(args[++index], out var parsedHeight) && parsedHeight >= 240:
                    viewportHeight = parsedHeight;
                    break;
                case "--theme-icon-policy" when index + 1 < args.Length && Enum.TryParse<ThemeIconRenderingPolicy>(args[++index], true, out var parsedPolicy):
                    themeIconPolicy = parsedPolicy;
                    break;
                case "--layout-direction" when index + 1 < args.Length:
                    layoutDirection = args[++index].ToLowerInvariant() switch
                    {
                        "ltr" => Forma.LayoutDirection.LeftToRight,
                        "rtl" => Forma.LayoutDirection.RightToLeft,
                        _ => throw new ArgumentException("Catalog layout direction must be LTR or RTL."),
                    };
                    break;
                default:
                    throw new ArgumentException($"Unknown or invalid catalog argument: {args[index]}");
            }
        }

        if (watchedEffectPath != null && !File.Exists(watchedEffectPath))
            throw new ArgumentException($"The watched effect does not exist: {watchedEffectPath}");
        return outputPath == null && renderOutputPath == null && screenshotPath == null && watchedEffectPath == null && displayScale == null && storyName == null && viewportWidth == null && viewportHeight == null && themeIconPolicy == null && layoutDirection == null
            ? null
            : new CatalogMetricsOptions(outputPath, renderOutputPath, screenshotPath, frameCount, watchedEffectPath, displayScale, storyName, viewportWidth, viewportHeight, themeIconPolicy, layoutDirection);
    }
}