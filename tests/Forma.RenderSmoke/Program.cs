// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Forma;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using var game = new Game();
_ = new GraphicsDeviceManager(game) { GraphicsProfile = GraphicsProfile.HiDef };
var manager = (IGraphicsDeviceManager)game.Services.GetService(typeof(IGraphicsDeviceManager));
manager.CreateDevice();
var graphicsDevice = game.GraphicsDevice;
var deviceLifetimeResources = new List<object>();
graphicsDevice.Disposing += (_, _) => GC.KeepAlive(deviceLifetimeResources);
using var face = UIFontFace.FromProjectFile(AppContext.BaseDirectory, "Fonts/Inter_Regular.ttf");

if (args.Contains("--composition-only", StringComparer.Ordinal))
{
    ValidateFullyClippedComposition(graphicsDevice);
    ValidateBorder(graphicsDevice);
    ValidateControlComposition(graphicsDevice);
    Console.WriteLine($"Composition render smoke passed on {graphicsDevice.Adapter.Description} ({graphicsDevice.GraphicsProfile}).");
    return;
}
ValidateDrawingPath(graphicsDevice);
ValidateFoundationalShape(graphicsDevice);
ValidateFullyClippedComposition(graphicsDevice);
ValidateBorder(graphicsDevice);
ValidateVectorImage(graphicsDevice);
ValidateViewbox(graphicsDevice);
ValidateControlComposition(graphicsDevice);
using var inlineContext = ValidateTypedInlines(graphicsDevice, face);
ValidateRetainedDrawing(graphicsDevice, face);
ValidateGeometryComposition(graphicsDevice);
ValidateBoundedEffects(graphicsDevice);
using var imageBrushSource = ValidateImageBrush(graphicsDevice);
ValidateFoundationalImages(graphicsDevice, imageBrushSource);
ValidateAlpha8AndReset(graphicsDevice, face);
ValidateDynamicTextTracking(graphicsDevice, face, deviceLifetimeResources);
ValidateSvgRasterCache(graphicsDevice);
ValidateSvgImageStretchModes(graphicsDevice, deviceLifetimeResources);
Console.WriteLine(RunSvgPerformanceBenchmarks());
Console.WriteLine(RunSvgGpuBenchmarks(graphicsDevice, deviceLifetimeResources));
ValidateSvgConsumerSurfaces(graphicsDevice, face);
ValidateDefaultThemeSvgPolicy(graphicsDevice, deviceLifetimeResources);
var warmDraw = ValidateWarmDrawing(graphicsDevice, face);
Console.WriteLine(RunPerformanceBenchmarks(graphicsDevice, warmDraw));
ValidateIndependentDeviceOwnership(graphicsDevice, face);
Console.WriteLine($"Dynamic render smoke passed on {graphicsDevice.Adapter.Description} ({graphicsDevice.GraphicsProfile}).");

static void ValidateDynamicTextTracking(GraphicsDevice graphicsDevice, UIFontFace face, List<object> deviceLifetimeResources)
{
    var font = new DynamicUIFont(face, 16, UIFontHinting.Light);
    var natural1x = RenderTrackedText(graphicsDevice, font, 1, 0, deviceLifetimeResources);
    var tracked1x = RenderTrackedText(graphicsDevice, font, 1, .25f, deviceLifetimeResources);
    var natural2x = RenderTrackedText(graphicsDevice, font, 2, 0, deviceLifetimeResources);
    var tracked2x = RenderTrackedText(graphicsDevice, font, 2, .25f, deviceLifetimeResources);
    var naturalBounds1x = GetInkBounds(natural1x.Pixels, natural1x.Width, natural1x.Height);
    var trackedBounds1x = GetInkBounds(tracked1x.Pixels, tracked1x.Width, tracked1x.Height);
    var naturalBounds2x = GetInkBounds(natural2x.Pixels, natural2x.Width, natural2x.Height);
    var trackedBounds2x = GetInkBounds(tracked2x.Pixels, tracked2x.Width, tracked2x.Height);

    Require(trackedBounds1x.Width > naturalBounds1x.Width, "RichTextLabel tracking must expand final 1x ink output.");
    Require(trackedBounds2x.Width > naturalBounds2x.Width, "RichTextLabel tracking must expand final 2x ink output.");
    Require(MathF.Abs(trackedBounds1x.Left - trackedBounds2x.Left / 2f) <= .5f, "Tracked text must retain its logical left edge across display scales.");
    Require(MathF.Abs(trackedBounds1x.Right - trackedBounds2x.Right / 2f) <= 1f, "Tracked text must retain its logical right edge across display scales.");
}

static (Color[] Pixels, int Width, int Height) RenderTrackedText(
    GraphicsDevice graphicsDevice,
    DynamicUIFont font,
    float displayScale,
    float letterSpacing,
    List<object> deviceLifetimeResources)
{
    var logicalSize = new Vector2(96, 32);
    var physicalWidth = (int)(logicalSize.X * displayScale);
    var physicalHeight = (int)(logicalSize.Y * displayScale);
    var target = new RenderTarget2D(graphicsDevice, physicalWidth, physicalHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    var context = new UIContext { DisplayScale = displayScale, ViewportSize = logicalSize };
    deviceLifetimeResources.Add(target);
    deviceLifetimeResources.Add(context);
    context.Add(new RichTextLabel
    {
        UIFont = font,
        Text = "runtime.",
        LetterSpacing = letterSpacing,
        Padding = Thickness.Zero,
        FontColor = Color.White,
        Size = logicalSize,
    });
    DrawContextFrame(graphicsDevice, context, target);
    DrawContextFrame(graphicsDevice, context, target);
    return (ReadPixels(target), physicalWidth, physicalHeight);
}

static Rectangle GetInkBounds(Color[] pixels, int width, int height)
{
    var left = width;
    var top = height;
    var right = -1;
    var bottom = -1;
    for (var y = 0; y < height; y++)
    {
        for (var x = 0; x < width; x++)
        {
            if (pixels[x + y * width].A == 0) continue;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }
    }
    Require(right >= left && bottom >= top, "Dynamic text render must contain visible ink.");
    return new Rectangle(left, top, right - left + 1, bottom - top + 1);
}

static void ValidateSvgRasterCache(GraphicsDevice graphicsDevice)
{
    SvgBackendDefaults.Install();
    var source = SvgImageSource.FromMemory(Encoding.UTF8.GetBytes(
        "<svg xmlns='http://www.w3.org/2000/svg' width='12' height='8' color='#ffffff'><rect width='12' height='8' fill='currentColor'/></svg>"));
    using var target = new RenderTarget2D(graphicsDevice, 64, 48, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    using var sharedCacheLease = SvgRasterCacheLease.Acquire(graphicsDevice);
    var image = new Image
    {
        Position = new Vector2(4, 4),
        Size = new Vector2(24, 16),
        ExpandMode = TextureRectExpandMode.IgnoreSize,
        Stretch = ImageStretch.Fill,
        ScalableSource = source,
        Tint = new Color(20, 180, 100),
    };

    using (var context = new UIRenderContext(graphicsDevice, new Theme()) { DisplayScale = 1.5f })
    {
        DrawSvgFrame(graphicsDevice, context, target, image);
        var coldPixels = ReadPixels(target);
        DrawSvgFrame(graphicsDevice, context, target, image);
        var populatedPixels = ReadPixels(target);
        var populated = context.SvgRasterDiagnostics;
        DrawSvgFrame(graphicsDevice, context, target, image);
        var warmPixels = ReadPixels(target);
        var warm = context.SvgRasterDiagnostics;

        Require(coldPixels.All(pixel => pixel == Color.Transparent), "Cold SVG rasters must wait for their bounded upload.");
        Require(populatedPixels.Any(pixel => pixel.G > pixel.R * 4 && pixel.G > pixel.B), "Atlas-backed SVG drawing must preserve tint modulation.");
        Require(populatedPixels[2 + 2 * target.Width] == Color.Transparent, "SVG drawing must remain clipped to its destination.");
        Require(warmPixels.SequenceEqual(populatedPixels), "Warm SVG drawing must preserve exact pixel output.");
        Require(populated.Parses == 1 && populated.Rasterizations == 1 && populated.Uploads == 1, "One SVG size must parse, rasterize, and upload exactly once.");
        Require(warm.Misses == populated.Misses && warm.Rasterizations == populated.Rasterizations && warm.Uploads == populated.Uploads, "Warm SVG drawing must perform no cold cache work.");
        Require(context.SvgRasterPages[0].Pixels.Length == 2048 * 2048 * 4, "SVG atlas snapshots must expose retained RGBA pages.");
    }

    using var cache = new SvgRasterCache(graphicsDevice, new SvgRasterCacheOptions(8, 8, 2, 1, 8 * 8 * 4));
    var secondSource = SvgImageSource.FromMemory(Encoding.UTF8.GetBytes(
        "<svg xmlns='http://www.w3.org/2000/svg' width='6' height='6'><circle cx='3' cy='3' r='3' fill='#ff0000'/></svg>"));
    cache.BeginFrame();
    var first = cache.GetOrAdd(source, 6, 6);
    var second = cache.GetOrAdd(secondSource, 6, 6);
    cache.EndFrame();
    cache.FlushUploads();
    Require(cache.Diagnostics.Uploads == 1 && cache.Diagnostics.PendingUploads == 1, "The SVG upload byte budget must defer the second dirty page.");
    var originalTexture = cache.GetTexture(first);
    Require(originalTexture?.Format == SurfaceFormat.Color, "SVG atlas pages must use premultiplied RGBA Color textures.");
    Require(cache.GetTexture(second) == null, "An SVG entry must not expose stale texture pixels before upload.");
    cache.FlushUploads();
    Require(cache.GetTexture(second) != null && cache.Diagnostics.PendingUploads == 0, "A deferred SVG page must upload on the next flush.");

    graphicsDevice.Reset();
    Require(cache.Diagnostics.PendingUploads == 2 && cache.GetTexture(first) == null, "Device reset must retain SVG CPU pages and hide invalid textures.");
    cache.FlushUploads();
    cache.FlushUploads();
    Require(cache.GetTexture(first) != null && !ReferenceEquals(cache.GetTexture(first), originalTexture), "Device reset must recreate SVG textures from retained RGBA pages.");
    Require(cache.Diagnostics.PendingUploads == 0, "All retained SVG pages must finish reuploading within the configured frame budget.");
    cache.Dispose();

    using (var firstSharedContext = new UIRenderContext(graphicsDevice, new Theme()))
    {
        firstSharedContext.Begin();
        firstSharedContext.DrawScalableImage(source, new Rectangle(0, 0, 12, 8), Color.White);
        firstSharedContext.End();
    }
    var missesBeforeSharedLookup = sharedCacheLease.Cache.Diagnostics.Misses;
    using (var secondSharedContext = new UIRenderContext(graphicsDevice, new Theme()))
    {
        secondSharedContext.Begin();
        secondSharedContext.DrawScalableImage(source, new Rectangle(0, 0, 12, 8), Color.White);
        secondSharedContext.End();
        Require(secondSharedContext.SvgRasterDiagnostics.Hits > 0 && secondSharedContext.SvgRasterDiagnostics.Misses == missesBeforeSharedLookup, "Sequential UI contexts on one graphics device must share SVG raster variants.");
    }
    Require(!sharedCacheLease.Cache.IsDisposed, "The explicit device lease must retain the SVG cache after renderer release.");
    sharedCacheLease.Dispose();

    var scaleDiagnostics = new StringBuilder("SVG scale hashes:");
    var scaleContext = new UIRenderContext(graphicsDevice, new Theme());
    var scaleTargets = new List<RenderTarget2D>();
    foreach (var scale in new[] { 1f, 1.25f, 1.5f, 1.75f, 2f, 2.5f })
    {
        var physicalWidth = (int)MathF.Ceiling(20 * scale);
        var physicalHeight = (int)MathF.Ceiling(14 * scale);
        var scaleTarget = new RenderTarget2D(graphicsDevice, physicalWidth, physicalHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        scaleTargets.Add(scaleTarget);
        scaleContext.DisplayScale = scale;
        var scaleImage = new Image { Size = new Vector2(20, 14), ExpandMode = TextureRectExpandMode.IgnoreSize, Stretch = ImageStretch.Fill, ScalableSource = source };
        DrawSvgFrame(graphicsDevice, scaleContext, scaleTarget, scaleImage);
        DrawSvgFrame(graphicsDevice, scaleContext, scaleTarget, scaleImage);
        var scalePixels = ReadPixels(scaleTarget);
        Require(scalePixels.Any(pixel => pixel.A > 0), $"SVG output at {scale}x must contain visible pixels.");
        var bytes = new byte[scalePixels.Length * sizeof(uint)];
        for (var index = 0; index < scalePixels.Length; index++) BitConverter.TryWriteBytes(bytes.AsSpan(index * sizeof(uint), sizeof(uint)), scalePixels[index].PackedValue);
        scaleDiagnostics.Append(' ').Append(scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append('=').Append(Convert.ToHexString(SHA256.HashData(bytes)));
    }
    Console.WriteLine(scaleDiagnostics);
}

static string RunSvgPerformanceBenchmarks()
{
    var bytes = Encoding.UTF8.GetBytes(
        "<svg xmlns='http://www.w3.org/2000/svg' width='96' height='64' viewBox='0 0 96 64'><defs><linearGradient id='g'><stop stop-color='#30b9a4'/><stop offset='1' stop-color='#f6b949'/></linearGradient></defs><rect x='2' y='2' width='92' height='60' rx='12' fill='url(#g)'/><path d='M28 43L48 20L68 43Z' fill='#fff'/></svg>");
    var timer = Stopwatch.StartNew();
    var source = SvgImageSource.FromMemory(bytes);
    timer.Stop();
    var validationMilliseconds = timer.Elapsed.TotalMilliseconds;

    timer.Restart();
    using var document = SvgBackendRegistry.Backend.Parse(source.CopySource());
    timer.Stop();
    var parseMilliseconds = timer.Elapsed.TotalMilliseconds;

    timer.Restart();
    var raster = SvgBackendRegistry.Backend.Rasterize(document, 240, 160);
    timer.Stop();
    var rasterMilliseconds = timer.Elapsed.TotalMilliseconds;

    using var store = new SvgRasterAtlasStore(new SvgRasterCacheOptions(512, 512, 2, 1));
    store.BeginFrame();
    timer.Restart();
    _ = store.GetOrAdd(source, 240, 160);
    timer.Stop();
    var coldCacheMilliseconds = timer.Elapsed.TotalMilliseconds;
    store.EndFrame();
    store.BeginFrame();
    timer.Restart();
    for (var index = 0; index < 1000; index++) _ = store.GetOrAdd(source, 240, 160);
    timer.Stop();
    store.EndFrame();

    var warmLookupMilliseconds = timer.Elapsed.TotalMilliseconds;
    Require(validationMilliseconds < 100, "SVG source validation exceeded the 100 ms smoke budget.");
    Require(parseMilliseconds < 500, "SVG cold parse exceeded the 500 ms smoke budget.");
    Require(rasterMilliseconds < 500, "SVG 240x160 rasterization exceeded the 500 ms smoke budget.");
    Require(coldCacheMilliseconds < 500, "SVG cold cache insertion exceeded the 500 ms smoke budget.");
    Require(warmLookupMilliseconds < 25, "One thousand warm SVG lookups exceeded the 25 ms smoke budget.");
    return FormattableString.Invariant($"SVG benchmark: validate={validationMilliseconds:0.000}ms, parse={parseMilliseconds:0.000}ms, raster240x160={rasterMilliseconds:0.000}ms, coldCache={coldCacheMilliseconds:0.000}ms, warmLookup1000={warmLookupMilliseconds:0.000}ms, rgba={raster.Pixels.Length}B");
}

static void ValidateDefaultThemeSvgPolicy(GraphicsDevice graphicsDevice, List<object> deviceLifetimeResources)
{
    var target = new RenderTarget2D(graphicsDevice, 32, 24, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    var context = new UIContext
    {
        ViewportSize = new Vector2(target.Width, target.Height),
        ThemeIconRenderingPolicy = ThemeIconRenderingPolicy.RuntimeSvg,
    };
    deviceLifetimeResources.Add(target);
    deviceLifetimeResources.Add(context);
    context.Add(new ThemeIconRect
    {
        Size = new Vector2(target.Width, target.Height),
        ThemeItemName = "arrow",
        ThemeTypeName = nameof(OptionButton),
    });

    DrawContextFrame(graphicsDevice, context, target);
    DrawContextFrame(graphicsDevice, context, target);
    var svgPixels = ReadPixels(target);
    var runtimeIcons = ReadDefaultThemeIcons(context, density: 1);
    Require(context.ThemeIconDiagnostics.RuntimeSvgIconCount > 0, "RuntimeSvg policy must resolve default icons from companion SVG sources.");
    Require(runtimeIcons.Count == DefaultThemeIconResources.ManifestIconCount, "RuntimeSvg policy must resolve every default theme icon.");
    Require(runtimeIcons.All(pair => pair.Value.ScalableSource != null), "RuntimeSvg policy must retain a scalable source for every default theme icon.");
    Require(svgPixels.Any(pixel => pixel.A != 0), "RuntimeSvg policy must render a default theme icon.");

    context.ThemeIconRenderingPolicy = ThemeIconRenderingPolicy.BitmapAtlas;
    DrawContextFrame(graphicsDevice, context, target);
    var bitmapPixels = ReadPixels(target);
    var bitmapIcons = ReadDefaultThemeIcons(context, density: 1);
    Require(context.ThemeIconDiagnostics.RuntimeSvgIconCount == 0, "BitmapAtlas policy must explicitly bypass runtime SVG sources.");
    Require(bitmapIcons.Count == runtimeIcons.Count, "BitmapAtlas policy must preserve the complete default icon inventory.");
    foreach (var pair in runtimeIcons)
    {
        var bitmap = bitmapIcons[pair.Key];
        Require(bitmap.ScalableSource == null && bitmap.Texture != null, $"BitmapAtlas policy must use PNG for {pair.Key}.");
        Require(bitmap.LogicalSize == pair.Value.LogicalSize && bitmap.SourceRectangle == pair.Value.SourceRectangle && bitmap.Density == pair.Value.Density,
            $"Default theme icon metadata must match between policies for {pair.Key}.");
    }
    Require(bitmapPixels.Any(pixel => pixel.A != 0), "BitmapAtlas policy must preserve the default PNG fallback.");

    // Resolve all 67 icons through policy metadata; backend-only tests rasterize the complete source inventory.
    context.ThemeIconRenderingPolicy = ThemeIconRenderingPolicy.RuntimeSvg;
    DrawContextFrame(graphicsDevice, context, target);
    var allSvgIcons = ReadDefaultThemeIcons(context, density: 1);
    Require(allSvgIcons.Count == DefaultThemeIconResources.ManifestIconCount && allSvgIcons.All(pair => pair.Value.ScalableSource != null),
        "Every default icon must resolve to a scalable source under RuntimeSvg policy.");

    // Tree hierarchy arrow: verify it renders visible pixels and registers a cache hit on a warm draw.
    Require(context.TryGetDefaultThemeIcon("arrow", new[] { "Tree" }, out var treeArrowIcon) && treeArrowIcon.HasValue,
        "Tree:arrow must resolve from the default theme under RuntimeSvg policy.");
    Require(treeArrowIcon.Value.ScalableSource != null,
        "Tree:arrow must retain an SVG scalable source under RuntimeSvg policy.");
    var iconTarget = new RenderTarget2D(graphicsDevice, 24, 24, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    var iconContext = new UIRenderContext(graphicsDevice, new Theme());
    deviceLifetimeResources.Add(iconTarget);
    deviceLifetimeResources.Add(iconContext);
    for (var frame = 0; frame < 2; frame++)
    {
        graphicsDevice.SetRenderTarget(iconTarget);
        graphicsDevice.Clear(Color.Transparent);
        iconContext.Begin();
        iconContext.Icon(treeArrowIcon.Value, new Rectangle(0, 0, 24, 24), Color.White);
        iconContext.Fill(new Rectangle(-1, -1, 1, 1), Color.White);
        iconContext.End();
        graphicsDevice.SetRenderTarget(null);
    }
    Require(ReadPixels(iconTarget).Any(p => p.A > 0), "Tree hierarchy arrow must render visible pixels under RuntimeSvg policy.");
    var hitsBeforeWarm = iconContext.SvgRasterDiagnostics.Hits;
    graphicsDevice.SetRenderTarget(iconTarget);
    graphicsDevice.Clear(Color.Transparent);
    iconContext.Begin();
    iconContext.Icon(treeArrowIcon.Value, new Rectangle(0, 0, 24, 24), Color.White);
    iconContext.Fill(new Rectangle(-1, -1, 1, 1), Color.White);
    iconContext.End();
    graphicsDevice.SetRenderTarget(null);
    Require(iconContext.SvgRasterDiagnostics.Hits > hitsBeforeWarm,
        "Tree hierarchy arrow warm draw must register a cache hit in the SVG raster cache.");

    static Dictionary<string, ThemeIcon> ReadDefaultThemeIcons(UIContext context, int density)
    {
        var result = new Dictionary<string, ThemeIcon>(StringComparer.Ordinal);
        foreach (var entry in DefaultThemeIconResources.ManifestEntries.Where(entry => entry.Density == density))
        {
            var binding = entry.Bindings[0];
            var separator = binding.IndexOf(':');
            var typeName = binding.Substring(0, separator);
            var itemName = binding.Substring(separator + 1);
            Require(context.TryGetDefaultThemeIcon(itemName, new[] { typeName }, out var icon) && icon.HasValue, $"Default theme binding {binding} must resolve.");
            result.Add(entry.Name, icon.Value);
        }
        return result;
    }
}

static void DrawContextFrame(GraphicsDevice graphicsDevice, UIContext context, RenderTarget2D target)
{
    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Draw(graphicsDevice);
    graphicsDevice.SetRenderTarget(null);
}

static void DrawSvgFrame(GraphicsDevice graphicsDevice, UIRenderContext context, RenderTarget2D target, Image image)
{
    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    image.DrawTree(context);
    context.Fill(new Rectangle(-1, -1, 1, 1), Color.White);
    context.End();
    graphicsDevice.SetRenderTarget(null);
}

static void ValidateSvgConsumerSurfaces(GraphicsDevice graphicsDevice, UIFontFace face)
{
    var source = SvgImageSource.FromMemory(Encoding.UTF8.GetBytes(
        "<svg xmlns='http://www.w3.org/2000/svg' width='10' height='6'><rect width='10' height='6' fill='#ffffff'/></svg>"));
    var precedenceTexture = new Texture2D(graphicsDevice, 1, 1);
    precedenceTexture.SetData(new[] { Color.Blue });
    var target = new RenderTarget2D(graphicsDevice, 128, 48, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    var context = new UIRenderContext(graphicsDevice, new Theme());
    var drawing = new DrawingImage
    {
        IntrinsicSize = source.IntrinsicSize,
        Drawing = new ImageDrawing { ScalableSource = source, Tint = Color.Lime },
    };
    var icon = new ThemeIconView
    {
        Position = new Vector2(32, 4),
        Size = new Vector2(20, 14),
        Icon = new ThemeIcon(source, new Point(10, 6)),
        Modulate = Color.Cyan,
    };
    var inline = new TextBlock
    {
        Position = new Vector2(58, 4),
        Size = new Vector2(28, 20),
        Padding = new Thickness(0),
        UIFont = new DynamicUIFont(face, 16),
    };
    inline.Inlines.Add(new InlineImage { Size = new Vector2(10, 6), ScalableSource = source, AlternativeText = "icon" });
    var precedence = new Image
    {
        Position = new Vector2(96, 4),
        Size = new Vector2(16, 12),
        ExpandMode = TextureRectExpandMode.IgnoreSize,
        Stretch = ImageStretch.Fill,
        Texture = precedenceTexture,
        VectorSource = new DrawingImage
        {
            IntrinsicSize = new Vector2(1, 1),
            Drawing = new GeometryDrawing { Geometry = new RectangleGeometry(), Fill = new SolidColorBrush(Color.Red) },
        },
        ScalableSource = source,
    };

    SvgRasterCacheDiagnostics populated = default;
    for (var frame = 0; frame < 3; frame++)
    {
        graphicsDevice.SetRenderTarget(target);
        graphicsDevice.Clear(Color.Transparent);
        context.Begin();
        drawing.Render(context, new Rectangle(4, 4, 20, 12));
        context.DrawScalableImage(
            source,
            new Rectangle(0, 0, 10, 6),
            Matrix.CreateRotationZ(Microsoft.Xna.Framework.MathHelper.PiOver2) * Matrix.CreateTranslation(24, 28, 0),
            Color.Magenta);
        icon.DrawTree(context);
        inline.DrawTree(context);
        precedence.DrawTree(context);
        context.Fill(new Rectangle(-1, -1, 1, 1), Color.White);
        context.End();
        graphicsDevice.SetRenderTarget(null);
        if (frame == 1) populated = context.SvgRasterDiagnostics;
    }

    var pixels = ReadPixels(target);
    var warm = context.SvgRasterDiagnostics;
    Require(pixels[10 + 8 * target.Width].G > 200, "ImageDrawing must render and tint a scalable source.");
    Require(pixels[39 + 8 * target.Width].G > 200 && pixels[39 + 8 * target.Width].B > 200, "ThemeIconView must render and tint a scalable ThemeIcon.");
    Require(pixels.Skip(58 + 4 * target.Width).Take(28 * 20).Any(pixel => pixel.A > 0), "InlineImage must render a scalable source in text flow.");
    Require(pixels[102 + 8 * target.Width].B > 200 && pixels[102 + 8 * target.Width].R < 40, "Image bitmap sources must retain precedence over vector and scalable sources.");
    Require(pixels.Skip(18 + 28 * target.Width).Take(12 * 10).Any(pixel => pixel.R > 200 && pixel.B > 200), "ImageDrawing transforms must be preserved for scalable sources.");
    Require(warm.Rasterizations == populated.Rasterizations && warm.Uploads == populated.Uploads, "Warm scalable consumer frames must share cached raster variants.");
}

static void ValidateDrawingPath(GraphicsDevice graphicsDevice)
{
    var path = new DrawingPath()
        .MoveTo(new Vector2(8, 30))
        .CubicTo(new Vector2(8, 8), new Vector2(40, 8), new Vector2(40, 30))
        .LineTo(new Vector2(40, 42))
        .LineTo(new Vector2(8, 42))
        .Close();
    using var target = new RenderTarget2D(graphicsDevice, 64, 64, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    using var context = new UIRenderContext(graphicsDevice, new Theme());

    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    context.Drawing.Save();
    context.Drawing.Clip(
        new DrawingPath()
            .MoveTo(new Vector2(18, 16))
            .LineTo(new Vector2(46, 16))
            .LineTo(new Vector2(40, 46))
            .LineTo(new Vector2(14, 40))
            .Close(),
        Matrix.Identity);
    context.Drawing.SetOpacityMask(new DrawingLinearGradient(new Vector2(14, 0), new Vector2(46, 0), Color.Transparent, Color.White));
    context.Drawing.SetEffect(new DrawingColorMatrixEffect(new float[]
    {
        0, 0, 1, 0, 0,
        0, 1, 0, 0, 0,
        1, 0, 0, 0, 0,
        0, 0, 0, 1, 0,
    }));
    context.Drawing.FillPath(path, new Color(40, 190, 120, 255), Matrix.CreateTranslation(4, 4, 0));
    context.Drawing.Restore();
    context.Drawing.StrokePath(
        path,
        new DrawingLinearGradient(new Vector2(12, 34), new Vector2(44, 34), Color.Red, Color.Blue),
        4,
        Matrix.CreateTranslation(4, 4, 0));
    context.End();
    graphicsDevice.SetRenderTarget(null);

    var pixels = ReadPixels(target);
    Require(pixels[28 + 22 * target.Width].A > 0, "The clipped and masked curved path must cover its center.");
    Require(pixels[16 + 30 * target.Width].A < pixels[38 + 30 * target.Width].A, "The opacity mask must increase coverage along its gradient.");
    Require(pixels[46 + 30 * target.Width] == Color.Transparent, "The arbitrary geometry clip must remove path pixels outside its edge.");
    Require(pixels[28 + 22 * target.Width].R > pixels[28 + 22 * target.Width].B, "The bounded color effect must swap red and blue channels.");
    Require(pixels[12 + 12 * target.Width] == Color.Transparent, "The curved path must not fill outside its upper arc.");
    Require(pixels[60 + 60 * target.Width] == Color.Transparent, "The transformed path must not fill outside its bounds.");
    Require(pixels[12 + 38 * target.Width].R > pixels[12 + 38 * target.Width].B, "The gradient stroke must retain its red start.");
    Require(pixels[44 + 38 * target.Width].B > pixels[44 + 38 * target.Width].R, "The gradient stroke must retain its blue end.");
}

static void ValidateFoundationalShape(GraphicsDevice graphicsDevice)
{
    using var target = new RenderTarget2D(graphicsDevice, 64, 48, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    using var context = new UIRenderContext(graphicsDevice, new Theme());
    var shape = new RectangleShape
    {
        Position = new Vector2(8, 8),
        Size = new Vector2(48, 32),
        RadiusX = 6,
        Fill = new LinearGradientBrush
        {
            GradientStops = new[] { new GradientStop(0, Color.Red), new GradientStop(1, Color.Blue) },
        },
        Stroke = new SolidColorBrush(Color.White),
        StrokeThickness = 2,
    };

    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    shape.DrawTree(context);
    context.End();
    graphicsDevice.SetRenderTarget(null);

    var pixels = ReadPixels(target);
    Require(pixels[14 + 24 * target.Width].R > pixels[14 + 24 * target.Width].B, "The shape gradient must retain its red start.");
    Require(pixels[50 + 24 * target.Width].B > pixels[50 + 24 * target.Width].R, "The shape gradient must retain its blue end.");
    Require(pixels[32 + 24 * target.Width].A > 0, "The shape must fill its center through the normal control draw tree.");
    Require(pixels[2 + 2 * target.Width] == Color.Transparent, "The shape must not draw outside its bounds.");

    var radial = new RectangleShape
    {
        Position = new Vector2(12, 8),
        Size = new Vector2(40, 32),
        Fill = new RadialGradientBrush
        {
            GradientStops = new[] { new GradientStop(0, Color.Red), new GradientStop(1, Color.Blue) },
        },
    };
    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    radial.DrawTree(context);
    context.End();
    graphicsDevice.SetRenderTarget(null);

    pixels = ReadPixels(target);
    Require(pixels[32 + 24 * target.Width].R > pixels[32 + 24 * target.Width].B, "A radial shape fill must sample its center stop per pixel.");
    Require(pixels[13 + 9 * target.Width].B > pixels[13 + 9 * target.Width].R, "A radial shape fill must retain its edge stop.");

    var conic = new RectangleShape
    {
        Position = new Vector2(12, 8),
        Size = new Vector2(40, 32),
        Fill = new ConicGradientBrush
        {
            GradientStops = new[] { new GradientStop(0, Color.Red), new GradientStop(.5f, Color.Blue), new GradientStop(1, Color.Red) },
        },
    };
    var polygon = new PolygonShape
    {
        Points = new[] { new Vector2(4, 4), new Vector2(20, 4), new Vector2(12, 18) },
        Fill = new SolidColorBrush(Color.Lime),
    };
    var polyline = new PolylineShape
    {
        Position = new Vector2(32, 0),
        Points = new[] { new Vector2(4, 18), new Vector2(12, 4), new Vector2(20, 18) },
        Stroke = new SolidColorBrush(Color.White),
        StrokeThickness = 2,
    };
    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    conic.DrawTree(context);
    context.End();
    graphicsDevice.SetRenderTarget(null);
    pixels = ReadPixels(target);
    Require(pixels[48 + 24 * target.Width] != pixels[16 + 24 * target.Width], "A conic fill must sample distinct colors at opposing angles.");

    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    polygon.DrawTree(context);
    polyline.DrawTree(context);
    context.End();
    graphicsDevice.SetRenderTarget(null);
    pixels = ReadPixels(target);
    Require(pixels[12 + 10 * target.Width].G > 200, "PolygonShape must close and fill its point sequence.");
    Require(pixels[44 + 12 * target.Width] == Color.Transparent, "PolylineShape must keep its point sequence open and unfilled.");
}

static void ValidateBorder(GraphicsDevice graphicsDevice)
{
    using var target = new RenderTarget2D(graphicsDevice, 100, 32, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    using var context = new UIRenderContext(graphicsDevice, new Theme());
    var border = new Border
    {
        Position = new Vector2(4, 4),
        Size = new Vector2(30, 24),
        BorderBrush = new SolidColorBrush(Color.White),
        BorderThickness = new Thickness(2, 4, 6, 8),
        CornerRadius = new CornerRadius(3, 5, 7, 9),
    };
    var clipped = new Border
    {
        Position = new Vector2(44, 4),
        Size = new Vector2(24, 24),
        CornerRadius = new CornerRadius(8),
    };
    clipped.Shadows.Add(new BoxShadow(Color.Blue, new Vector2(4, 0)));
    clipped.AddChild(new ColorRect { Size = clipped.Size, Color = Color.Red });
    var inset = new Border
    {
        Position = new Vector2(76, 4),
        Size = new Vector2(16, 24),
        Background = new SolidColorBrush(Color.White),
        CornerRadius = new CornerRadius(4),
    };
    inset.Shadows.Add(new BoxShadow(Color.Blue, new Vector2(3, 0), inset: true));
    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    border.DrawTree(context);
    clipped.DrawTree(context);
    inset.DrawTree(context);
    context.End();
    graphicsDevice.SetRenderTarget(null);

    var pixels = ReadPixels(target);
    Require(pixels[5 + 16 * target.Width].A > 0, "Border must render its two-pixel left side.");
    Require(pixels[7 + 16 * target.Width] == Color.Transparent, "Border must not exceed its left-side thickness.");
    Require(pixels[30 + 16 * target.Width].A > 0, "Border must render its six-pixel right side.");
    Require(pixels[18 + 6 * target.Width].A > 0, "Border must render its four-pixel top side.");
    Require(pixels[18 + 22 * target.Width].A > 0, "Border must render its eight-pixel bottom side.");
    Require(pixels[18 + 16 * target.Width] == Color.Transparent, "A border without a background must leave its interior transparent.");
    Require(pixels[44 + 4 * target.Width] == Color.Transparent, "A rounded Border must clip child pixels outside its corner geometry.");
    Require(pixels[56 + 16 * target.Width].R > 200, "A rounded Border must retain child pixels inside its clip geometry.");
    Require(pixels[70 + 16 * target.Width].B > 200 && pixels[70 + 16 * target.Width].R < 40, "Border outer shadows must render behind and beyond box content.");
    Require(pixels[77 + 16 * target.Width].B > 200 && pixels[77 + 16 * target.Width].R < 40, "Border inset shadows must render inside the rounded box clip.");
    Require(pixels[86 + 16 * target.Width].R > 200 && pixels[86 + 16 * target.Width].G > 200, "Border inset shadows must preserve unaffected background pixels.");
}

static void ValidateFullyClippedComposition(GraphicsDevice graphicsDevice)
{
    using var target = new RenderTarget2D(graphicsDevice, 32, 24, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    using var context = new UIRenderContext(graphicsDevice, new Theme());
    var outer = new Border { Position = new Vector2(4, 2), Size = new Vector2(20, 20), CornerRadius = new CornerRadius(4) };
    var content = new Container { Size = outer.Size };
    content.AddChild(new ColorRect { Size = outer.Size, Color = Color.Lime });
    var fullyClippedChild = new Border { Position = new Vector2(40, 0), Size = new Vector2(12, 12), CornerRadius = new CornerRadius(3) };
    fullyClippedChild.AddChild(new ColorRect { Size = fullyClippedChild.Size, Color = Color.Red });
    content.AddChild(fullyClippedChild);
    outer.AddChild(content);

    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    outer.DrawTree(context);
    context.End();
    graphicsDevice.SetRenderTarget(null);

    var pixels = ReadPixels(target);
    Require(pixels[14 + 12 * target.Width].G > 200, "A visible sibling must survive nested rounded composition.");
    Require(pixels[28 + 8 * target.Width] == Color.Transparent, "A fully clipped rounded child must contribute no pixels.");
}

static void ValidateVectorImage(GraphicsDevice graphicsDevice)
{
    using var target = new RenderTarget2D(graphicsDevice, 48, 32, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    using var context = new UIRenderContext(graphicsDevice, new Theme());
    var image = new Image
    {
        Position = new Vector2(4, 4),
        Size = new Vector2(40, 24),
        ExpandMode = TextureRectExpandMode.IgnoreSize,
        Stretch = ImageStretch.Contain,
        VectorSource = new DrawingImage
        {
            IntrinsicSize = new Vector2(10, 20),
            Drawing = new GeometryDrawing
            {
                Geometry = new RectangleGeometry { RadiusX = 2 },
                Fill = new SolidColorBrush(Color.Lime),
            },
        },
    };

    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    image.DrawTree(context);
    context.End();
    graphicsDevice.SetRenderTarget(null);

    var pixels = ReadPixels(target);
    Require(pixels[8 + 16 * target.Width] == Color.Transparent, "Contained vector images must preserve side margins.");
    Require(pixels[24 + 16 * target.Width].G > 200, "Compiled-vector Image sources must render centered content.");
    Require(pixels[40 + 16 * target.Width] == Color.Transparent, "Contained vector images must preserve both side margins.");
}

static void ValidateViewbox(GraphicsDevice graphicsDevice)
{
    using var target = new RenderTarget2D(graphicsDevice, 48, 28, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    using var context = new UIRenderContext(graphicsDevice, new Theme());
    var viewbox = new Viewbox { Position = new Vector2(4, 4), Size = new Vector2(40, 20), Stretch = ImageStretch.Contain, SamplingMode = ImageSamplingMode.Nearest };
    viewbox.AddChild(new RectangleShape { Size = new Vector2(10, 10), CustomMinimumSize = new Vector2(10, 10), Fill = new SolidColorBrush(Color.Red) });

    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    viewbox.DrawTree(context);
    context.End();
    graphicsDevice.SetRenderTarget(null);
    var pixels = ReadPixels(target);
    Require(pixels[8 + 14 * target.Width] == Color.Transparent, "Contained Viewbox content must preserve its side margins.");
    Require(pixels[24 + 14 * target.Width].R > 200, "Viewbox must scale its rendered child subtree into the fitted destination.");
    Require(pixels[40 + 14 * target.Width] == Color.Transparent, "Contained Viewbox content must preserve both side margins.");

    viewbox.Stretch = ImageStretch.Cover;
    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    viewbox.DrawTree(context);
    context.End();
    graphicsDevice.SetRenderTarget(null);
    pixels = ReadPixels(target);
    Require(pixels[5 + 5 * target.Width].R > 200 && pixels[42 + 22 * target.Width].R > 200, "Covered Viewbox content must fill and clip to its viewport.");
    Require(pixels[2 + 14 * target.Width] == Color.Transparent, "Viewbox cover clipping must not paint outside its bounds.");

    using var highScaleTarget = new RenderTarget2D(graphicsDevice, 42, 40, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    using var highScaleContext = new UIRenderContext(graphicsDevice, new Theme()) { DisplayScale = 2 };
    var highScaleViewbox = new Viewbox { Size = new Vector2(21, 20), Stretch = ImageStretch.Contain, SamplingMode = ImageSamplingMode.Nearest };
    highScaleViewbox.AddChild(new RectangleShape { Size = new Vector2(10, 10), CustomMinimumSize = new Vector2(10, 10), Fill = new SolidColorBrush(Color.Red) });
    graphicsDevice.SetRenderTarget(highScaleTarget);
    graphicsDevice.Clear(Color.Transparent);
    highScaleContext.Begin();
    highScaleViewbox.DrawTree(highScaleContext);
    highScaleContext.End();
    graphicsDevice.SetRenderTarget(null);
    var highScalePixels = ReadPixels(highScaleTarget);
    Require(highScalePixels[0 + 20 * highScaleTarget.Width] == Color.Transparent, "A centered high-scale Viewbox must preserve its left physical margin.");
    Require(highScalePixels[1 + 20 * highScaleTarget.Width].R > 200 && highScalePixels[40 + 20 * highScaleTarget.Width].R > 200, "A high-scale Viewbox must fill between its symmetric margins.");
    Require(highScalePixels[41 + 20 * highScaleTarget.Width] == Color.Transparent, "A centered high-scale Viewbox must preserve its right physical margin.");
}

static void ValidateControlComposition(GraphicsDevice graphicsDevice)
{
    using var target = new RenderTarget2D(graphicsDevice, 56, 32, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    using var context = new UIRenderContext(graphicsDevice, new Theme());
    var root = new Control
    {
        Position = new Vector2(4, 4),
        Size = new Vector2(20, 20),
        RenderTransform = new TranslateTransform { X = 24 },
        TransformOrigin = Vector2.Zero,
        Clip = new EllipseGeometry(),
        Opacity = .5f,
    };
    root.AddChild(new ColorRect { Size = root.Size, Color = Color.Red });

    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    root.DrawTree(context);
    context.End();
    graphicsDevice.SetRenderTarget(null);

    var pixels = ReadPixels(target);
    Require(pixels[14 + 14 * target.Width] == Color.Transparent, "A render transform must remove composed subtree pixels from the original footprint.");
    var center = pixels[38 + 14 * target.Width];
    Require(center.R == center.A && center.A > 110 && center.A < 145, "Control opacity must apply exactly once to the transformed subtree using premultiplied alpha.");
    Require(pixels[29 + 5 * target.Width] == Color.Transparent, "Control geometry clips must compose before the transformed subtree is replayed.");
}

static UIRenderContext ValidateTypedInlines(GraphicsDevice graphicsDevice, UIFontFace face)
{
    using var target = new RenderTarget2D(graphicsDevice, 96, 48, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    var context = new UIRenderContext(graphicsDevice, new Theme());
    var first = new Span { Background = Color.Red, Foreground = Color.White, Decoration = TextDecoration.Underline };
    first.Inlines.Add(new Run("MM") { LetterSpacing = 3 });
    var second = new Span { Background = Color.Blue, Foreground = Color.White, Decoration = TextDecoration.Strikethrough };
    second.Inlines.Add(new Run("II"));
    var text = new TextBlock
    {
        Position = new Vector2(2, 2),
        Size = new Vector2(92, 44),
        Padding = new Thickness(0),
        UIFont = new DynamicUIFont(face, 16),
    };
    text.Inlines.Add(first);
    text.Inlines.Add(new InlineImage
    {
        Size = new Vector2(10, 10),
        VectorSource = new DrawingImage
        {
            IntrinsicSize = new Vector2(10, 10),
            Drawing = new GeometryDrawing
            {
                Geometry = new EllipseGeometry(),
                Fill = new SolidColorBrush(Color.Lime),
            },
        },
    });
    text.Inlines.Add(new LineBreak());
    text.Inlines.Add(second);

    DrawInlineFrame(graphicsDevice, context, target, text);
    DrawInlineFrame(graphicsDevice, context, target, text);
    DrawInlineFrame(graphicsDevice, context, target, text);
    var pixels = ReadPixels(target);
    Require(pixels.Any(pixel => pixel.R > 200 && pixel.G < 40 && pixel.B < 40), "Typed spans must render inherited red backgrounds.");
    Require(pixels.Any(pixel => pixel.B > 200 && pixel.R < 40 && pixel.G < 40), "Typed spans must render inherited blue backgrounds after a line break.");
    Require(pixels.Any(pixel => pixel.G > 200 && pixel.R < 40 && pixel.B < 40), "InlineImage must render a vector DrawingImage in the text flow.");
    Require(pixels.Any(pixel => pixel.R > 200 && pixel.G > 200 && pixel.B > 200), "Typed spans must render their inherited foreground glyph color.");
    Require(HasHorizontalWhiteRun(pixels, target.Width, 2, 28, 13, 20, 8), "Typed spans must render inherited underline decoration from retained baseline metrics.");
    Require(HasHorizontalWhiteRun(pixels, target.Width, 2, 18, 21, 31, 5), "Typed spans must render inherited strikethrough decoration from retained baseline metrics.");

    text.Inlines.Clear();
    text.Text = "MM";
    text.Decoration = TextDecoration.Underline;
    DrawInlineFrame(graphicsDevice, context, target, text);
    DrawInlineFrame(graphicsDevice, context, target, text);
    DrawInlineFrame(graphicsDevice, context, target, text);
    pixels = ReadPixels(target);
    Require(HasHorizontalWhiteRun(pixels, target.Width, 2, 32, 13, 24, 8), "TextBlock block decoration must underline the plain-text fast path.");

    text.Text = string.Empty;
    text.Decoration = TextDecoration.None;
    text.Size = new Vector2(36, 24);
    text.AutowrapMode = LabelAutowrapMode.WordSmart;
    text.MaxLinesVisible = 1;
    text.EllipsisCharacter = "...";
    text.Inlines.Add(new Run("MM "));
    text.Inlines.Add(new Run("hidden"));
    DrawInlineFrame(graphicsDevice, context, target, text);
    var ellipsizedPixels = ReadPixels(target);
    text.EllipsisCharacter = string.Empty;
    DrawInlineFrame(graphicsDevice, context, target, text);
    var clippedPixels = ReadPixels(target);
    Require(ellipsizedPixels.Where((pixel, index) => pixel != clippedPixels[index]).Any(), "Inline text hidden by MaxLinesVisible must render a trailing ellipsis.");
    Require(text.GetCharacterBounds(3) == Rectangle.Empty, "Inline source hidden by forced ellipsis must not remain hittable.");
    return context;
}

static bool HasHorizontalWhiteRun(Color[] pixels, int width, int left, int right, int top, int bottom, int requiredLength)
{
    for (var y = top; y < bottom; y++)
    {
        var run = 0;
        for (var x = left; x < right; x++)
        {
            var pixel = pixels[x + y * width];
            run = pixel.R > 200 && pixel.G > 200 && pixel.B > 200 ? run + 1 : 0;
            if (run >= requiredLength) return true;
        }
    }
    return false;
}

static void DrawInlineFrame(GraphicsDevice graphicsDevice, UIRenderContext context, RenderTarget2D target, TextBlock text)
{
    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    text.DrawTree(context);
    context.End();
    graphicsDevice.SetRenderTarget(null);
}

static void ValidateRetainedDrawing(GraphicsDevice graphicsDevice, UIFontFace face)
{
    using var target = new RenderTarget2D(graphicsDevice, 48, 32, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    using var context = new UIRenderContext(graphicsDevice, new Theme());
    var matrix = new ColorMatrixEffect
    {
        Values = new float[]
        {
            0, 0, 1, 0, 0,
            0, 1, 0, 0, 0,
            1, 0, 0, 0, 0,
            0, 0, 0, 1, 0,
        },
    };
    var drawing = new DrawingImage
    {
        Drawing = new DrawingGroup
        {
            Opacity = .5f,
            Effect = matrix,
            Children =
            {
                new GeometryDrawing
                {
                    Geometry = new RectangleGeometry { RadiusX = 4 },
                    Fill = new SolidColorBrush(new Color(40, 180, 120)),
                },
            },
        },
    };

    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    drawing.Render(context, new Rectangle(8, 6, 32, 20));
    context.End();
    graphicsDevice.SetRenderTarget(null);

    var pixel = ReadPixels(target)[24 + 16 * target.Width];
    Require(pixel.R > pixel.B, "The retained drawing color matrix must swap red and blue.");
    Require(pixel.A > 100 && pixel.A < 155, "The retained drawing group must apply bounded opacity once.");

    var textDrawing = new DrawingImage
    {
        Drawing = new TextDrawing
        {
            Font = new DynamicUIFont(face, 16),
            Text = "M",
            Position = new Vector2(4, 4),
            Color = Color.White,
        },
    };
    for (var frame = 0; frame < 3; frame++)
    {
        graphicsDevice.SetRenderTarget(target);
        graphicsDevice.Clear(Color.Transparent);
        context.Begin();
        textDrawing.Render(context, new Rectangle(0, 0, 24, 24));
        context.End();
        graphicsDevice.SetRenderTarget(null);
    }
    Require(ReadPixels(target).Any(value => value.R > 200 && value.G > 200 && value.B > 200 && value.A > 0), "TextDrawing must render retained UIFont glyphs.");

    var tinted = new DrawingImage
    {
        Drawing = new GeometryDrawing
        {
            Geometry = new RectangleGeometry(),
            Fill = new SolidColorBrush(Color.Red),
            Effect = matrix,
        },
    };
    var maskThenEffect = new DrawingImage
    {
        Drawing = new GeometryDrawing
        {
            Geometry = new RectangleGeometry(),
            Fill = new SolidColorBrush(Color.White),
            OpacityMask = new SolidColorBrush(new Color(255, 255, 255, 128)),
            Effect = new ColorMatrixEffect
            {
                Values = new float[]
                {
                    1, 0, 0, 0, 0,
                    0, 1, 0, 0, 0,
                    0, 0, 1, 0, 0,
                    0, 0, 0, 0, 1,
                },
            },
        },
    };

    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    tinted.Render(context, new Rectangle(4, 4, 8, 8), Color.Yellow);
    maskThenEffect.Render(context, new Rectangle(20, 4, 8, 8));
    context.End();
    graphicsDevice.SetRenderTarget(null);

    var composedPixels = ReadPixels(target);
    var tintedPixel = composedPixels[8 + 8 * target.Width];
    Require(tintedPixel.B < 30 && tintedPixel.A > 240, "DrawingImage tint must compose after a child color matrix.");
    Require(composedPixels[24 + 8 * target.Width].A > 240, "A retained color matrix must run after its opacity mask.");

    var matrixThenShadow = new EffectGroup();
    matrixThenShadow.Add(matrix);
    matrixThenShadow.Add(new DropShadowEffect { Color = Color.Red, Offset = new Vector2(4, 0) });
    var shadowThenMatrix = new EffectGroup();
    shadowThenMatrix.Add(new DropShadowEffect { Color = Color.Red, Offset = new Vector2(4, 0) });
    shadowThenMatrix.Add(matrix);
    var firstOrder = new DrawingImage
    {
        Drawing = new GeometryDrawing
        {
            Geometry = new RectangleGeometry(),
            Fill = new SolidColorBrush(Color.Red),
            Effect = matrixThenShadow,
        },
    };
    var secondOrder = new DrawingImage
    {
        Drawing = new GeometryDrawing
        {
            Geometry = new RectangleGeometry(),
            Fill = new SolidColorBrush(Color.Red),
            Effect = shadowThenMatrix,
        },
    };

    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    firstOrder.Render(context, new Rectangle(4, 4, 8, 8));
    secondOrder.Render(context, new Rectangle(24, 4, 8, 8));
    context.End();
    graphicsDevice.SetRenderTarget(null);

    var orderedPixels = ReadPixels(target);
    var firstShadow = orderedPixels[14 + 8 * target.Width];
    var secondShadow = orderedPixels[34 + 8 * target.Width];
    Require(firstShadow.R > 200 && firstShadow.B < 40, "A drop shadow after a color matrix must retain its declared color.");
    Require(secondShadow.B > 200 && secondShadow.R < 40, "A color matrix after a drop shadow must transform the shadow composite.");

    var clipped = new DrawingImage
    {
        Drawing = new GeometryDrawing
        {
            Geometry = new RectangleGeometry(),
            Fill = new SolidColorBrush(Color.White),
            Clip = new PathGeometry(DrawingPath.Parse("M0 0 H20 V20 H0 Z M6 6 H14 V14 H6 Z")) { FillRule = FillRule.EvenOdd },
        },
    };
    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    clipped.Render(context, new Rectangle(4, 4, 20, 20));
    context.End();
    graphicsDevice.SetRenderTarget(null);

    var clippedPixels = ReadPixels(target);
    Require(clippedPixels[6 + 6 * target.Width].A > 240, "A multi-contour drawing clip must preserve its outer contour.");
    Require(clippedPixels[14 + 14 * target.Width] == Color.Transparent, "An even-odd drawing clip must preserve its hole.");

    var masked = new DrawingImage
    {
        Drawing = new GeometryDrawing
        {
            Geometry = new RectangleGeometry(),
            Fill = new SolidColorBrush(Color.White),
            OpacityMask = new RadialGradientBrush
            {
                Center = new Vector2(16, 12),
                Radius = 8,
                RelativeCoordinates = false,
                Transform = new TranslateTransform { X = 4 },
                GradientStops = new[] { new GradientStop(0, Color.White), new GradientStop(1, Color.Transparent) },
            },
        },
    };
    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    masked.Render(context, new Rectangle(8, 4, 24, 16));
    context.End();
    graphicsDevice.SetRenderTarget(null);

    var maskedPixels = ReadPixels(target);
    Require(maskedPixels[20 + 12 * target.Width].A > 220, "A transformed radial brush mask must retain opacity at its shifted center.");
    Require(maskedPixels[9 + 5 * target.Width].A < 20, "A radial brush mask must fade drawing coverage outside its radius.");
}

static void ValidateGeometryComposition(GraphicsDevice graphicsDevice)
{
    using var target = new RenderTarget2D(graphicsDevice, 64, 32, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    using var context = new UIRenderContext(graphicsDevice, new Theme());
    var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
    group.Children.Add(new RectangleGeometry());
    group.Children.Add(new RectangleGeometry { Transform = new TranslateTransform { X = 6, Y = 6 } });
    var overlap = new CombinedGeometry
    {
        Mode = GeometryCombineMode.Intersect,
        Geometry1 = new RectangleGeometry(),
        Geometry2 = new RectangleGeometry { Transform = new TranslateTransform { X = 8 } },
    };
    var groupedDrawing = new DrawingImage
    {
        Drawing = new GeometryDrawing { Geometry = group, Fill = new SolidColorBrush(Color.White) },
    };
    var booleanDrawing = new DrawingImage
    {
        Drawing = new GeometryDrawing { Geometry = overlap, Fill = new SolidColorBrush(Color.Lime) },
    };

    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    groupedDrawing.Render(context, new Rectangle(4, 4, 24, 24));
    booleanDrawing.Render(context, new Rectangle(36, 4, 24, 24));
    context.End();
    graphicsDevice.SetRenderTarget(null);

    var pixels = ReadPixels(target);
    Require(pixels[6 + 16 * target.Width].A > 0, "An even-odd geometry group must retain its outer-only region.");
    Require(pixels[16 + 16 * target.Width] == Color.Transparent, "An even-odd geometry group must remove its overlapping center.");
    Require(pixels[40 + 16 * target.Width] == Color.Transparent, "Boolean intersection must remove the non-overlapping region.");
    Require(pixels[48 + 16 * target.Width].G > 200, "Boolean intersection must render its overlapping region.");
}

static void ValidateBoundedEffects(GraphicsDevice graphicsDevice)
{
    using var target = new RenderTarget2D(graphicsDevice, 64, 32, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    using var context = new UIRenderContext(graphicsDevice, new Theme());
    var blur = new DrawingImage
    {
        Drawing = new GeometryDrawing
        {
            Geometry = new RectangleGeometry(),
            Fill = new SolidColorBrush(Color.White),
            Effect = new BlurEffect { Radius = 2 },
        },
    };
    var shadow = new DrawingImage
    {
        Drawing = new GeometryDrawing
        {
            Geometry = new RectangleGeometry(),
            Fill = new SolidColorBrush(Color.Red),
            Effect = new DropShadowEffect { Color = Color.Blue, Offset = new Vector2(5, 4) },
        },
    };
    Drawing nested = new GeometryDrawing
    {
        Geometry = new RectangleGeometry(),
        Fill = new SolidColorBrush(Color.White),
    };
    for (var depth = 0; depth <= DrawingContextLimits.MaximumOffscreenNestingDepth; depth++)
    {
        var group = new DrawingGroup { Effect = new BlurEffect() };
        group.Children.Add(nested);
        nested = group;
    }
    var overNested = new DrawingImage { Drawing = nested };

    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    blur.Render(context, new Rectangle(8, 8, 8, 8));
    shadow.Render(context, new Rectangle(34, 8, 8, 8));
    overNested.Render(context, new Rectangle(52, 8, 6, 6));
    overNested.Render(context, new Rectangle(52, 8, 6, 6));
    context.End();
    graphicsDevice.SetRenderTarget(null);

    var pixels = ReadPixels(target);
    Require(pixels[7 + 11 * target.Width].A > 0, "BlurEffect must expand alpha beyond the source geometry.");
    Require(pixels[11 + 11 * target.Width].A > pixels[7 + 11 * target.Width].A, "BlurEffect must retain stronger coverage near the source center.");
    Require(pixels[36 + 10 * target.Width].R > 200, "DropShadowEffect must retain the original drawing above its shadow.");
    Require(pixels[44 + 18 * target.Width].B > 200 && pixels[44 + 18 * target.Width].R < 40, "DropShadowEffect must color and offset captured alpha.");
    Require(pixels[54 + 10 * target.Width].A > 240, "An over-nested effect must fall back to its unfiltered content.");
    Require(context.CompositorDiagnostics.Count == 1 && context.CompositorDiagnostics[0].Contains("nesting", StringComparison.Ordinal),
        "Repeated runtime effect-limit failures must report one bounded diagnostic.");
}

static Texture2D ValidateImageBrush(GraphicsDevice graphicsDevice)
{
    var texture = new Texture2D(graphicsDevice, 2, 2);
    texture.SetData(new[] { Color.Red, Color.Blue, Color.Lime, Color.White });
    using var target = new RenderTarget2D(graphicsDevice, 64, 24, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    using var context = new UIRenderContext(graphicsDevice, new Theme());
    var path = new DrawingPath().MoveTo(Vector2.Zero).LineTo(new Vector2(16, 0)).LineTo(new Vector2(16, 16)).LineTo(new Vector2(0, 16)).Close();
    var contain = new ImageBrush
    {
        Source = texture,
        Stretch = ImageStretch.Contain,
        SamplingMode = ImageSamplingMode.Nearest,
    };
    var tiled = new ImageBrush
    {
        Source = texture,
        Stretch = ImageStretch.None,
        TileMode = ImageTileMode.Tile,
        SamplingMode = ImageSamplingMode.Nearest,
    };

    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    context.Drawing.FillPath(path, contain, new Rectangle(4, 2, 16, 20), Matrix.CreateTranslation(4, 2, 0));
    context.Drawing.FillPath(path, tiled, new Rectangle(28, 4, 16, 16), Matrix.CreateTranslation(28, 4, 0));
    new DrawingImage
    {
        Drawing = new ImageDrawing
        {
            Source = texture,
            SourceRectangle = new Rectangle(0, 0, 1, 1),
            SamplingMode = ImageSamplingMode.Nearest,
            Transform = new MatrixTransform
            {
                Matrix = Matrix.CreateRotationZ(Microsoft.Xna.Framework.MathHelper.PiOver2) * Matrix.CreateTranslation(8, 0, 0),
            },
        },
    }.Render(context, new Rectangle(50, 4, 8, 4));
    context.End();
    graphicsDevice.SetRenderTarget(null);

    var pixels = ReadPixels(target);
    Require(pixels[12 + 3 * target.Width] == Color.Transparent, "Contain image brushes must leave their letterbox margin transparent.");
    Require(pixels[12 + 8 * target.Width].A > 0, "Contain image brushes must render inside their aligned placement.");
    Require(pixels[29 + 5 * target.Width] != pixels[30 + 5 * target.Width], "Nearest image brushes must preserve adjacent texels.");
    Require(pixels[29 + 5 * target.Width] == pixels[31 + 5 * target.Width], "Tiled image brushes must repeat their horizontal texel period.");
    Require(pixels[29 + 5 * target.Width] == pixels[29 + 7 * target.Width], "Tiled image brushes must repeat their vertical texel period.");
    Require(pixels[56 + 8 * target.Width].R > 200, "ImageDrawing must retain its cropped source texel through rotation.");
    Require(pixels[51 + 6 * target.Width] == Color.Transparent, "ImageDrawing rotation must move pixels out of the untransformed footprint.");
    return texture;
}

static void ValidateFoundationalImages(GraphicsDevice graphicsDevice, Texture2D texture)
{
    using var target = new RenderTarget2D(graphicsDevice, 56, 16, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    using var context = new UIRenderContext(graphicsDevice, new Theme());
    var nineSlice = new NineSliceImage
    {
        Texture = texture,
        Position = new Vector2(2, 2),
        Size = new Vector2(8, 8),
        PatchMargin = new Thickness(1),
        DrawCenter = false,
        SamplingMode = ImageSamplingMode.Nearest,
    };
    var vectorIcon = new ThemeIconView
    {
        Icon = new ThemeIcon(new DrawingImage
        {
            IntrinsicSize = new Vector2(6, 4),
            Drawing = new GeometryDrawing
            {
                Geometry = new RectangleGeometry(),
                Fill = new SolidColorBrush(Color.White),
            },
        }, new Point(6, 4)),
        Position = new Vector2(16, 2),
        Size = new Vector2(10, 8),
        Modulate = Color.Cyan,
    };
    var cropped = new Image
    {
        Texture = texture,
        SourceRectangle = new Rectangle(1, 0, 1, 1),
        Position = new Vector2(30, 2),
        Size = new Vector2(8, 8),
        Stretch = ImageStretch.Fill,
        SamplingMode = ImageSamplingMode.Nearest,
    };
    var tiled = new Image
    {
        Texture = texture,
        Position = new Vector2(42, 2),
        Size = new Vector2(10, 8),
        Stretch = ImageStretch.None,
        TileMode = ImageTileMode.TileX,
        ImageHorizontalAlignment = HorizontalAlignment.Left,
        ImageVerticalAlignment = VerticalAlignment.Top,
        SamplingMode = ImageSamplingMode.Nearest,
    };

    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    nineSlice.DrawTree(context);
    vectorIcon.DrawTree(context);
    cropped.DrawTree(context);
    tiled.DrawTree(context);
    context.End();
    graphicsDevice.SetRenderTarget(null);

    var pixels = ReadPixels(target);
    Require(pixels[2 + 2 * target.Width] == Color.Red, "NineSliceImage must retain its top-left source corner.");
    Require(pixels[9 + 2 * target.Width] == Color.Blue, "NineSliceImage must retain its top-right source corner.");
    Require(pixels[2 + 9 * target.Width] == Color.Lime, "NineSliceImage must retain its bottom-left source corner.");
    Require(pixels[9 + 9 * target.Width] == Color.White, "NineSliceImage must retain its bottom-right source corner.");
    Require(pixels[18 + 4 * target.Width].G > 200 && pixels[18 + 4 * target.Width].B > 200 && pixels[18 + 4 * target.Width].R < 40, "ThemeIconView must tint and render a compiled vector icon at logical size.");
    Require(pixels[16 + 3 * target.Width] == Color.Transparent, "ThemeIconView must center its logical icon without filling its control bounds.");
    Require(pixels[34 + 6 * target.Width] == Color.Blue, "Image must crop and stretch a bitmap source rectangle.");
    Require(pixels[42 + 2 * target.Width] == pixels[44 + 2 * target.Width] && pixels[43 + 2 * target.Width] == pixels[45 + 2 * target.Width], "Image tile-X mode must repeat its source columns horizontally.");
    Require(pixels[44 + 5 * target.Width] == Color.Transparent, "Image tile-X mode must not repeat vertically.");
}

static string RunPerformanceBenchmarks(GraphicsDevice graphicsDevice, TimeSpan warmDraw)
{
    var timer = Stopwatch.StartNew();
    using (var coldFace = UIFontFace.FromProjectFile(AppContext.BaseDirectory, "Fonts/Inter_Regular.ttf")) { }
    var coldLoad = timer.Elapsed;

    using var inter = UIFontFace.FromProjectFile(AppContext.BaseDirectory, "Fonts/Inter_Regular.ttf");
    timer.Restart();
    inter.Shape("Forma office", 20, TextDirection.LeftToRight, "en", "Latn");
    var firstShape = timer.Elapsed;

    using var cache = new DynamicGlyphCache(graphicsDevice, new DynamicGlyphCacheOptions(64, 64, 1, 1));
    var font = new DynamicUIFont(inter, 20);
    timer.Restart();
    cache.BeginFrame();
    cache.GetOrAdd(font, inter.GetGlyphId('A'), 1);
    cache.EndFrame();
    cache.FlushUploads();
    var firstRasterUpload = timer.Elapsed;

    var engine = new TextLayoutEngine();
    engine.Layout(font, "Warm layout lookup");
    timer.Restart();
    for (var iteration = 0; iteration < 1000; iteration++) engine.Layout(font, "Warm layout lookup");
    var warmLayout = timer.Elapsed;

    using var arabic = UIFontFace.FromProjectFile(AppContext.BaseDirectory, "Fonts/NotoSansArabic_Variable.ttf");
    var fallbackFont = new DynamicUIFont(inter, 20, UIFontHinting.Default, arabic);
    timer.Restart();
    new TextLayoutEngine().Layout(fallbackFont, "Forma مرحبا office العالم Forma مرحبا", new TextLayoutOptions(maxWidth: 180, wrapping: TextWrapping.Word, locale: "ar"));
    var fallbackLayout = timer.Elapsed;

    using var churnCache = new DynamicGlyphCache(graphicsDevice, new DynamicGlyphCacheOptions(64, 64, 1, 1));
    timer.Restart();
    for (var character = 33; character < 127; character++)
    {
        churnCache.BeginFrame();
        churnCache.GetOrAdd(font, inter.GetGlyphId(character), 1);
        churnCache.EndFrame();
        churnCache.FlushUploads();
    }
    var atlasChurn = timer.Elapsed;

    RequireBudget("cold face load", coldLoad, 1000);
    RequireBudget("first shape", firstShape, 500);
    RequireBudget("first raster/upload", firstRasterUpload, 500);
    RequireBudget("1,000 warm layout lookups", warmLayout, 500);
    RequireBudget("100 warm draws", warmDraw, 1000);
    RequireBudget("fallback-heavy layout", fallbackLayout, 500);
    RequireBudget("atlas churn", atlasChurn, 2000);
    Require(engine.Diagnostics.CacheHitRate >= .99, $"Warm layout cache hit rate was {engine.Diagnostics.CacheHitRate:P2}; budget is at least 99%.");

    return FormattableString.Invariant($"Dynamic text benchmark: load={coldLoad.TotalMilliseconds:F3}ms, shape={firstShape.TotalMilliseconds:F3}ms, rasterUpload={firstRasterUpload.TotalMilliseconds:F3}ms, warmLayout1000={warmLayout.TotalMilliseconds:F3}ms, warmDraw100={warmDraw.TotalMilliseconds:F3}ms, fallback={fallbackLayout.TotalMilliseconds:F3}ms, churn={atlasChurn.TotalMilliseconds:F3}ms");
}

static void RequireBudget(string operation, TimeSpan elapsed, double milliseconds) =>
    Require(elapsed.TotalMilliseconds <= milliseconds, $"Dynamic text {operation} took {elapsed.TotalMilliseconds:F3} ms; budget is {milliseconds:F0} ms.");

static void ValidateAlpha8AndReset(GraphicsDevice graphicsDevice, UIFontFace face)
{
    var font = new DynamicUIFont(face, 18);
    using var cache = new DynamicGlyphCache(graphicsDevice, new DynamicGlyphCacheOptions(64, 64, 2, 1));
    cache.BeginFrame();
    var first = cache.GetOrAdd(font, face.GetGlyphId('A'), 1);
    var second = cache.GetOrAdd(font, face.GetGlyphId('B'), 1);
    cache.EndFrame();
    Require(cache.Diagnostics.PendingUploads == 1, "Two cold glyphs must batch into one pending page upload.");
    cache.FlushUploads();
    var originalTexture = cache.GetTexture(first);
    var pixels = new byte[originalTexture.Width * originalTexture.Height];
    originalTexture.GetData(pixels);
    Require(originalTexture.Format == SurfaceFormat.Alpha8, "Dynamic glyph pages must use Alpha8.");
    Require(ReferenceEquals(cache.GetTexture(second), originalTexture), "Glyphs on one page must share one texture.");
    Require(cache.Diagnostics.Uploads == 1 && cache.Diagnostics.PendingUploads == 0, "The cold page must upload exactly once.");
    Require(pixels.Any(value => value > 0), "The Alpha8 page must preserve nonzero glyph coverage.");

    graphicsDevice.Reset();
    Require(cache.Diagnostics.PendingUploads == 1, "Device reset must retain CPU pages and queue them for reupload.");
    cache.FlushUploads();
    var recoveredTexture = cache.GetTexture(first);
    Require(!ReferenceEquals(recoveredTexture, originalTexture), "Device reset must replace invalid texture references.");
    Require(cache.Diagnostics.PendingUploads == 0, "Recovered pages must finish uploading.");
}

static TimeSpan ValidateWarmDrawing(GraphicsDevice graphicsDevice, UIFontFace face)
{
    var engine = new TextLayoutEngine();
    var font = new DynamicUIFont(face, 20);
    var layout = engine.Layout(font, "Atlas");
    var emptyDynamicLayout = engine.Layout(font, string.Empty);
    var glyphTint = new Color(20, 180, 140, 255);
    using var target = new RenderTarget2D(graphicsDevice, 120, 60, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    using var context = new UIRenderContext(graphicsDevice, new Theme());

    DrawFrame(graphicsDevice, context, target, layout, glyphTint);
    var firstFrame = ReadPixels(target);
    var cold = context.DynamicGlyphDiagnostics;
    DrawFrame(graphicsDevice, context, target, layout, glyphTint);
    var populatedFrame = ReadPixels(target);
    var populated = context.DynamicGlyphDiagnostics;
    DrawFrame(graphicsDevice, context, target, layout, glyphTint);
    var warmFrame = ReadPixels(target);
    var warm = context.DynamicGlyphDiagnostics;

    Require(firstFrame.Any(pixel => pixel != Color.Transparent), "Cold glyphs must batch-upload before their first draw.");
    Require(firstFrame.SequenceEqual(populatedFrame), "Cold drawing must match the next warm frame.");
    Require(cold.Uploads == 1 && cold.PendingUploads == 0, "A cold layout must complete one batched page upload before drawing.");
    Require(populatedFrame.Any(pixel => pixel != Color.Transparent), "Uploaded glyphs must render visible pixels.");
    Require(populatedFrame.Any(pixel => pixel.G > pixel.R * 4 && pixel.B > pixel.R * 3), "Dynamic glyphs must preserve the requested RGB tint.");
    Require(warmFrame.SequenceEqual(populatedFrame), "Warm drawing must preserve pixel output.");
    Require(warm.Misses == populated.Misses, "Warm drawing must not rasterize another glyph.");
    Require(warm.Uploads == populated.Uploads && warm.PendingUploads == 0, "Warm drawing must not upload another page.");
    ValidateWarmTraversalAllocations(layout);
    ValidateWarmCacheAllocations(graphicsDevice, font, face);


static void ValidateWarmTraversalAllocations(TextLayout layout)
{
    var visible = 0;
    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    for (var iteration = 0; iteration < 100; iteration++)
        for (var runIndex = 0; runIndex < layout.Runs.Count; runIndex++)
            for (var glyphIndex = 0; glyphIndex < layout.Runs[runIndex].Glyphs.Count; glyphIndex++)
                if (layout.IsVisible(layout.Runs[runIndex].Glyphs[glyphIndex])) visible++;
    var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    Require(visible > 0 && allocated == 0, $"Warm retained-layout traversal allocated {allocated} managed bytes.");
}

static void ValidateWarmCacheAllocations(GraphicsDevice graphicsDevice, DynamicUIFont font, UIFontFace face)
{
    using var cache = new DynamicGlyphCache(graphicsDevice, new DynamicGlyphCacheOptions(64, 64, 1, 1));
    var glyphIds = "Atlas".Select(character => face.GetGlyphId(character)).ToArray();
    cache.BeginFrame();
    foreach (var glyphId in glyphIds) cache.GetOrAdd(font, glyphId, 1);
    cache.EndFrame();
    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    for (var iteration = 0; iteration < 100; iteration++)
    {
        cache.BeginFrame();
        for (var glyphIndex = 0; glyphIndex < glyphIds.Length; glyphIndex++) cache.GetOrAdd(font, glyphIds[glyphIndex], 1);
        cache.EndFrame();
    }
    var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    Require(allocated == 0, $"Warm atlas lookup allocated {allocated} managed bytes.");
}
    for (var frame = 0; frame < 100; frame++) DrawEmptyFrame(graphicsDevice, context, target);
    var emptyAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    for (var frame = 0; frame < 100; frame++) DrawEmptyFrame(graphicsDevice, context, target);
    var emptyAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - emptyAllocatedBefore;
    for (var frame = 0; frame < 100; frame++) DrawFilledFrame(graphicsDevice, context, target);
    var filledAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    for (var frame = 0; frame < 100; frame++) DrawFilledFrame(graphicsDevice, context, target);
    var filledAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - filledAllocatedBefore;
    for (var frame = 0; frame < 100; frame++) DrawFrame(graphicsDevice, context, target, emptyDynamicLayout, glyphTint);
    var effectAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    for (var frame = 0; frame < 100; frame++) DrawFrame(graphicsDevice, context, target, emptyDynamicLayout, glyphTint);
    var effectAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - effectAllocatedBefore;
    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    var warmDrawStarted = Stopwatch.GetTimestamp();
    for (var frame = 0; frame < 100; frame++) DrawFrame(graphicsDevice, context, target, layout, glyphTint);
    var warmDraw = Stopwatch.GetElapsedTime(warmDrawStarted);
    var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    var allocationDiagnostics = context.DynamicGlyphDiagnostics;
    Require(allocatedBytes == effectAllocatedBytes, $"One hundred unchanged warm text draws allocated {allocatedBytes} managed bytes versus {effectAllocatedBytes} for empty Alpha8 effect frames, {filledAllocatedBytes} for rendered non-text frames, and {emptyAllocatedBytes} for empty frames.");
    Require(allocationDiagnostics.Misses == warm.Misses && allocationDiagnostics.Uploads == warm.Uploads, "Warm allocation sampling must remain free of rasterization and uploads.");

    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    context.PushClip(new Rectangle(0, 0, 12, target.Height));
    context.Text(layout, new Vector2(4, 4), glyphTint);
    context.PopClip();
    context.End();
    graphicsDevice.SetRenderTarget(null);
    var clipped = ReadPixels(target);
    Require(clipped.Any(pixel => pixel != Color.Transparent), "A partial clip must retain covered glyph pixels.");
    for (var y = 0; y < target.Height; y++)
        for (var x = 12; x < target.Width; x++)
            Require(clipped[y * target.Width + x] == Color.Transparent, "Glyph pixels must not escape the active clip.");

    context.DisplayScale = 1.5f;
    var fractionalLayout = new TextLayoutEngine().Layout(new DynamicUIFont(face, 17.5f), "Fractional");
    DrawClippedFrame(graphicsDevice, context, target, fractionalLayout, new Vector2(3.25f, 2.5f), new Rectangle(0, 0, 10, 40), glyphTint);
    DrawClippedFrame(graphicsDevice, context, target, fractionalLayout, new Vector2(3.25f, 2.5f), new Rectangle(0, 0, 10, 40), glyphTint);
    var fractional = ReadPixels(target);
    DrawClippedFrame(graphicsDevice, context, target, fractionalLayout, new Vector2(3.25f, 2.5f), new Rectangle(0, 0, 10, 40), glyphTint);
    var fractionalWarm = ReadPixels(target);
    Require(fractional.Any(pixel => pixel != Color.Transparent), "Fractional 1.5x drawing must preserve covered glyph samples.");
    Require(fractionalWarm.SequenceEqual(fractional), "Fractional 1.5x sampling must remain stable on a warm frame.");
    for (var y = 0; y < target.Height; y++)
        for (var x = 15; x < target.Width; x++)
            Require(fractional[y * target.Width + x] == Color.Transparent, "The 1.5x physical scissor must contain fractional glyph samples.");
    ValidateLifecycleStress(graphicsDevice, context, target, face);
    return warmDraw;
}

static void ValidateLifecycleStress(GraphicsDevice graphicsDevice, UIRenderContext context, RenderTarget2D target, UIFontFace inter)
{
    using var arabic = UIFontFace.FromProjectFile(AppContext.BaseDirectory, "Fonts/NotoSansArabic_Variable.ttf");
    var engine = new TextLayoutEngine();
    for (var iteration = 0; iteration < 48; iteration++)
    {
        using var recreated = iteration % 8 == 0 ? UIFontFace.FromProjectFile(AppContext.BaseDirectory, "Fonts/Inter_Regular.ttf") : null;
        var primary = recreated ?? inter;
        var font = iteration % 2 == 0
            ? new DynamicUIFont(primary, 12 + iteration % 17, UIFontHinting.Default, arabic)
            : new DynamicUIFont(arabic, 12 + iteration % 17, UIFontHinting.Default, primary);
        var arabicIteration = iteration % 3 == 0;
        var text = arabicIteration ? $"تحرير سريع {iteration} Forma" : $"Rapid edit {iteration} مرحبا";
        var options = new TextLayoutOptions(
            maxWidth: 80 + iteration % 5 * 12,
            wrapping: TextWrapping.Word,
            direction: arabicIteration ? TextDirection.RightToLeft : TextDirection.LeftToRight,
            locale: arabicIteration ? "ar" : "en");
        context.DisplayScale = iteration % 3 == 0 ? 1f : iteration % 3 == 1 ? 1.5f : 2f;
        var layout = engine.Layout(font, text, options);
        DrawFrame(graphicsDevice, context, target, layout, Color.White);
        DrawFrame(graphicsDevice, context, target, layout, Color.White);
        if (iteration % 12 == 11) engine.Clear();
    }
    var diagnostics = context.DynamicGlyphDiagnostics;
    Require(diagnostics.Bytes <= DynamicGlyphCacheOptions.MaximumBytes, "Lifecycle stress exceeded the glyph atlas memory budget.");
    Require(diagnostics.Failures == 0, $"Lifecycle stress produced an atlas failure: {diagnostics.LastFailure}");
}

static void ValidateIndependentDeviceOwnership(GraphicsDevice primaryDevice, UIFontFace face)
{
    using var primaryCache = new DynamicGlyphCache(primaryDevice, new DynamicGlyphCacheOptions(64, 64, 1, 1));
    var primaryEntry = Prepare(primaryCache, face);
    var primaryTexture = primaryCache.GetTexture(primaryEntry);

    var secondGame = new Game();
    _ = new GraphicsDeviceManager(secondGame) { GraphicsProfile = GraphicsProfile.HiDef };
    var secondManager = (IGraphicsDeviceManager)secondGame.Services.GetService(typeof(IGraphicsDeviceManager));
    secondManager.CreateDevice();
    var secondCache = new DynamicGlyphCache(secondGame.GraphicsDevice, new DynamicGlyphCacheOptions(64, 64, 1, 1));
    var secondEntry = Prepare(secondCache, face);
    var secondTexture = secondCache.GetTexture(secondEntry);
    Require(!ReferenceEquals(primaryTexture, secondTexture), "Each graphics device must own independent atlas textures.");

    var svgSource = SvgImageSource.FromMemory(Encoding.UTF8.GetBytes(
        "<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'><rect width='4' height='4' fill='#ffffff'/></svg>"));
    using var primarySvgCache = new SvgRasterCache(primaryDevice, new SvgRasterCacheOptions(8, 8, 1, 1));
    using var secondSvgCache = new SvgRasterCache(secondGame.GraphicsDevice, new SvgRasterCacheOptions(8, 8, 1, 1));
    primarySvgCache.BeginFrame();
    var primarySvgEntry = primarySvgCache.GetOrAdd(svgSource, 4, 4);
    primarySvgCache.EndFrame();
    primarySvgCache.FlushUploads();
    secondSvgCache.BeginFrame();
    var secondSvgEntry = secondSvgCache.GetOrAdd(svgSource, 4, 4);
    secondSvgCache.EndFrame();
    secondSvgCache.FlushUploads();
    Require(!ReferenceEquals(primarySvgCache.GetTexture(primarySvgEntry), secondSvgCache.GetTexture(secondSvgEntry)), "Each graphics device must own independent SVG atlas textures.");

    secondCache.Dispose();
    secondGame.Dispose();
}

static DynamicGlyphAtlasEntry Prepare(DynamicGlyphCache cache, UIFontFace face)
{
    var font = new DynamicUIFont(face, 18);
    cache.BeginFrame();
    var entry = cache.GetOrAdd(font, face.GetGlyphId('A'), 1);
    cache.EndFrame();
    cache.FlushUploads();
    return entry;
}

static void DrawFrame(GraphicsDevice graphicsDevice, UIRenderContext context, RenderTarget2D target, TextLayout layout, Color color)
{
    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    context.PushClip(new Rectangle(0, 0, target.Width, target.Height));
    context.Text(layout, new Vector2(4, 4), color);
    context.PopClip();
    context.End();
    graphicsDevice.SetRenderTarget(null);
}

static void DrawEmptyFrame(GraphicsDevice graphicsDevice, UIRenderContext context, RenderTarget2D target)
{
    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    context.PushClip(new Rectangle(0, 0, target.Width, target.Height));
    context.PopClip();
    context.End();
    graphicsDevice.SetRenderTarget(null);
}

static void DrawFilledFrame(GraphicsDevice graphicsDevice, UIRenderContext context, RenderTarget2D target)
{
    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    context.PushClip(new Rectangle(0, 0, target.Width, target.Height));
    context.Fill(new Rectangle(4, 4, 20, 20), Color.White);
    context.PopClip();
    context.End();
    graphicsDevice.SetRenderTarget(null);
}

static void DrawClippedFrame(GraphicsDevice graphicsDevice, UIRenderContext context, RenderTarget2D target, TextLayout layout, Vector2 position, Rectangle clip, Color color)
{
    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    context.PushClip(clip);
    context.Text(layout, position, color);
    context.PopClip();
    context.End();
    graphicsDevice.SetRenderTarget(null);
}

static Color[] ReadPixels(RenderTarget2D target)
{
    var pixels = new Color[target.Width * target.Height];
    target.GetData(pixels);
    return pixels;
}

static void ValidateSvgImageStretchModes(GraphicsDevice graphicsDevice, List<object> deviceLifetimeResources)
{
    SvgBackendDefaults.Install();
    // A wide (20x8) SVG rendered into a square (16x16) container:
    //   Contain → letterbox at top/bottom rows; image centered in rows 5..10
    //   Cover   → image fills all rows; no transparent letterbox
    var source = SvgImageSource.FromMemory(Encoding.UTF8.GetBytes(
        "<svg xmlns='http://www.w3.org/2000/svg' width='20' height='8'><rect width='20' height='8' fill='#ff0000'/></svg>"));
    var target = new RenderTarget2D(graphicsDevice, 16, 16, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    var context = new UIRenderContext(graphicsDevice, new Theme());
    deviceLifetimeResources.Add(target);
    deviceLifetimeResources.Add(context);

    var containImage = new Image { Size = new Vector2(16, 16), ExpandMode = TextureRectExpandMode.IgnoreSize, Stretch = ImageStretch.Contain, ScalableSource = source };
    DrawSvgFrame(graphicsDevice, context, target, containImage);
    DrawSvgFrame(graphicsDevice, context, target, containImage);
    var containPixels = ReadPixels(target);

    var coverImage = new Image { Size = new Vector2(16, 16), ExpandMode = TextureRectExpandMode.IgnoreSize, Stretch = ImageStretch.Cover, ScalableSource = source };
    DrawSvgFrame(graphicsDevice, context, target, coverImage);
    DrawSvgFrame(graphicsDevice, context, target, coverImage);
    var coverPixels = ReadPixels(target);

    // Contain: top letterbox row (y=0) transparent; centered image row (y=8) visible.
    Require(containPixels[8 + 0 * 16] == Color.Transparent, "Contain must leave the top letterbox row transparent.");
    Require(containPixels[8 + 8 * 16].A > 0, "Contain must render the SVG in the centered rows.");
    // Cover: top row (y=0) filled with image; no letterbox.
    Require(coverPixels[8 + 0 * 16].A > 0, "Cover must fill the top of the container without letterboxing.");
    Require(coverPixels[8 + 8 * 16].A > 0, "Cover must render the SVG in the center.");

    // Opacity=0.5: pixels must be partially transparent (premultiplied).
    var opacityImage = new Image { Size = new Vector2(16, 16), ExpandMode = TextureRectExpandMode.IgnoreSize, Stretch = ImageStretch.Fill, ScalableSource = source, ImageOpacity = 0.5f };
    DrawSvgFrame(graphicsDevice, context, target, opacityImage);
    DrawSvgFrame(graphicsDevice, context, target, opacityImage);
    var opacityPixels = ReadPixels(target);
    Require(opacityPixels[8 + 8 * 16].A > 0 && opacityPixels[8 + 8 * 16].A < 240, "ImageOpacity=0.5 must produce partially transparent SVG pixels.");

    // Disabled modulation: Tint=Transparent must suppress all SVG output.
    var disabledImage = new Image { Size = new Vector2(16, 16), ExpandMode = TextureRectExpandMode.IgnoreSize, Stretch = ImageStretch.Fill, ScalableSource = source, Tint = Color.Transparent };
    DrawSvgFrame(graphicsDevice, context, target, disabledImage);
    DrawSvgFrame(graphicsDevice, context, target, disabledImage);
    var disabledPixels = ReadPixels(target);
    Require(disabledPixels.All(p => p.A == 0), "Tint=Transparent must produce no visible SVG pixels (disabled modulation).");

    // RTL horizontal-flip transform must produce visible mirrored SVG output.
    var rtlTransform = Matrix.CreateScale(-1, 1, 1) * Matrix.CreateTranslation(16, 4, 0);
    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    context.DrawScalableImage(source, new Rectangle(0, 0, 16, 8), rtlTransform, Color.White);
    context.Fill(new Rectangle(-1, -1, 1, 1), Color.White);
    context.End();
    graphicsDevice.SetRenderTarget(null);
    graphicsDevice.SetRenderTarget(target);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    context.DrawScalableImage(source, new Rectangle(0, 0, 16, 8), rtlTransform, Color.White);
    context.Fill(new Rectangle(-1, -1, 1, 1), Color.White);
    context.End();
    graphicsDevice.SetRenderTarget(null);
    var rtlPixels = ReadPixels(target);
    Require(rtlPixels.Any(p => p.A > 0), "RTL horizontal-flip transform must produce visible SVG output.");

    // Per-icon SVG fallback: an unsupported ScalableImageSource type invokes ThemeIconSvgFallback and renders the PNG.
    var fallbackTexture = new Texture2D(graphicsDevice, 8, 8);
    deviceLifetimeResources.Add(fallbackTexture);
    fallbackTexture.SetData(Enumerable.Repeat(Color.Red, 64).ToArray());
    var fakeSource = new FakeScalableSource();
    var icon = new ThemeIcon(fakeSource, fallbackTexture, new Rectangle(0, 0, 8, 8), new Point(8, 8));
    var fallbackCount = 0;
    context.ThemeIconSvgFallback = () => fallbackCount++;
    var iconTarget = new RenderTarget2D(graphicsDevice, 8, 8, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    deviceLifetimeResources.Add(iconTarget);
    graphicsDevice.SetRenderTarget(iconTarget);
    graphicsDevice.Clear(Color.Transparent);
    context.Begin();
    context.Icon(icon, new Rectangle(0, 0, 8, 8), Color.White);
    context.Fill(new Rectangle(-1, -1, 1, 1), Color.White);
    context.End();
    graphicsDevice.SetRenderTarget(null);
    var fallbackPixels = ReadPixels(iconTarget);
    Require(fallbackCount == 1, "Unsupported scalable source type must invoke ThemeIconSvgFallback exactly once.");
    Require(fallbackPixels.Any(p => p.R > 200 && p.A > 0), "ThemeIcon SVG fallback must render the PNG atlas texture.");
    context.ThemeIconSvgFallback = null;
}

static string RunSvgGpuBenchmarks(GraphicsDevice graphicsDevice, List<object> deviceLifetimeResources)
{
    SvgBackendDefaults.Install();
    var sources = new[]
    {
        SvgImageSource.FromMemory(Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><circle cx='8' cy='8' r='6' fill='#40a0c0'/></svg>")),
        SvgImageSource.FromMemory(Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><rect width='32' height='32' rx='4' fill='#f06030'/></svg>")),
        SvgImageSource.FromMemory(Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24'><polygon points='12,2 22,22 2,22' fill='#80c040'/></svg>")),
    };
    var widths  = new[] { 16, 20, 24, 28, 32 };
    var heights = new[] { 16, 20, 24, 28, 32 };
    var cache = new SvgRasterCache(graphicsDevice, new SvgRasterCacheOptions(512, 512, 4, 1));
    deviceLifetimeResources.Add(cache);

    // Prime the CPU raster for every source/size combination.
    cache.BeginFrame();
    foreach (var src in sources)
        for (var i = 0; i < widths.Length; i++)
            cache.GetOrAdd(src, widths[i], heights[i]);
    cache.EndFrame();

    // Measure first GPU upload (all CPU pages ready; time the transfer).
    var timer = Stopwatch.StartNew();
    cache.FlushUploads();
    timer.Stop();
    var firstUploadMs = timer.Elapsed.TotalMilliseconds;
    var cardinality = cache.Diagnostics.EntryCount;

    // Measure sustained mixed-size rendering over 60 frames and capture managed allocations.
    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    var sustainedStart = Stopwatch.GetTimestamp();
    for (var frame = 0; frame < 60; frame++)
    {
        cache.BeginFrame();
        foreach (var src in sources)
            for (var i = 0; i < widths.Length; i++)
                cache.GetOrAdd(src, widths[i], heights[i]);
        cache.EndFrame();
    }
    var sustainedMs = Stopwatch.GetElapsedTime(sustainedStart).TotalMilliseconds;
    var sustainedAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

    Require(firstUploadMs < 2000, $"First SVG GPU upload took {firstUploadMs:0.000}ms; budget is 2000ms.");
    Require(sustainedMs < 500, $"60 frames of sustained mixed-size SVG lookup took {sustainedMs:0.000}ms; budget is 500ms.");
    Require(sustainedAllocated == 0, $"Sustained mixed-size SVG rendering allocated {sustainedAllocated} managed bytes.");
    Require(cardinality == sources.Length * widths.Length,
        $"Cache cardinality was {cardinality}; expected {sources.Length * widths.Length} distinct source/size combinations.");

    return FormattableString.Invariant($"SVG GPU benchmark: firstUpload={firstUploadMs:0.000}ms, sustained60Frames={sustainedMs:0.000}ms, entries={cardinality}, sustainedAlloc={sustainedAllocated}B");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

/// <summary>A non-SVG scalable source used to exercise the ThemeIconSvgFallback path in UIRenderContext.Icon.</summary>
sealed class FakeScalableSource : ScalableImageSource
{
    public override Vector2 IntrinsicSize => new Vector2(8, 8);
    public override string ContentIdentity => "fake-scalable-source-for-fallback-test";
}