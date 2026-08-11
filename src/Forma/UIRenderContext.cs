// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Forma
{
    /// <summary>SpriteBatch-backed drawing surface used by controls. Applications can subclass controls to draw custom content.</summary>
    public sealed class UIRenderContext : IDisposable
    {
        private readonly SpriteBatch _spriteBatch;
        private readonly Texture2D _pixel;
        private readonly BasicEffect _basicEffect;
        private readonly Effect _alpha8CoverageEffect;
        private readonly bool _dynamicGlyphsUseColorTexture;
        private readonly RasterizerState _scissorRasterizer;
        private readonly SamplerState[] _imageSamplers;
        private readonly DynamicGlyphCache _dynamicGlyphCache;
        private readonly SvgRasterCache _svgRasterCache;
        private readonly SvgRasterCacheLease _svgRasterCacheLease;
        private readonly Stack<Rectangle?> _clipStack = new Stack<Rectangle?>();
        private readonly Stack<ThemeScope> _themeStack = new Stack<ThemeScope>();
        private readonly List<Texture2D> _activeTransientTextures = new List<Texture2D>();
        private readonly List<Texture2D> _retiredTransientTextures = new List<Texture2D>();
        private readonly List<string> _compositorDiagnostics = new List<string>();
        private readonly HashSet<string> _reportedCompositorDiagnostics = new HashSet<string>(StringComparer.Ordinal);
        private TextLayoutEngine _textLayoutEngine = new TextLayoutEngine();
        private bool _begun;
        private int _compositorDepth;
        private Rectangle? _currentClip;
        private Vector2 _captureOrigin;
        internal UIRenderContext(GraphicsDevice graphicsDevice, Theme theme)
        {
            GraphicsDevice = graphicsDevice;
            Theme = theme;
            _spriteBatch = new SpriteBatch(graphicsDevice);
            _pixel = new Texture2D(graphicsDevice, 1, 1);
            _pixel.SetData(new[] { Color.White });
            _basicEffect = new BasicEffect(graphicsDevice) { TextureEnabled = true, VertexColorEnabled = true };
            _alpha8CoverageEffect = Alpha8CoverageEffect.Create(graphicsDevice);
            _dynamicGlyphsUseColorTexture = Alpha8CoverageEffect.RequiresColorGlyphAtlas;
            _scissorRasterizer = new RasterizerState { ScissorTestEnable = true };
            _imageSamplers = CreateImageSamplers();
            _dynamicGlyphCache = new DynamicGlyphCache(
                graphicsDevice,
                _dynamicGlyphsUseColorTexture ? new DynamicGlyphCacheOptions(1024, 1024, 8) : null,
                useColorTextures: _dynamicGlyphsUseColorTexture);
            _svgRasterCacheLease = SvgRasterCacheLease.Acquire(graphicsDevice);
            _svgRasterCache = _svgRasterCacheLease.Cache;
            Drawing = new DrawingContext(this);
            graphicsDevice.DeviceReset += OnDeviceReset;
            graphicsDevice.Disposing += OnGraphicsDeviceDisposing;
        }
        public GraphicsDevice GraphicsDevice { get; }
        /// <summary>Backend-neutral vector drawing operations for foundational visuals.</summary>
        public DrawingContext Drawing { get; }
        public Theme Theme { get; internal set; }
        internal float DisplayScale { get; set; } = 1f;
        internal Func<SpriteFont, float, SpriteFont> DisplayFontResolver { get; set; }
        internal TextLayoutEngine TextLayoutEngine { get => _textLayoutEngine; set => _textLayoutEngine = value ?? throw new ArgumentNullException(nameof(value)); }
        public SpriteBatch SpriteBatch => _spriteBatch;
        /// <summary>Deduplicated runtime composition-limit reports retained for this device context.</summary>
        public IReadOnlyList<string> CompositorDiagnostics => _compositorDiagnostics;
        internal Texture2D Pixel => _pixel;
        public void Begin()
        {
            if (_begun) return;
            RetireTransientTargets();
            _currentClip = null;
            _dynamicGlyphCache.FlushUploads();
            _svgRasterCache.FlushUploads();
            _dynamicGlyphCache.BeginFrame();
            _svgRasterCache.BeginFrame();
            BeginBatch();
        }
        public void End()
        {
            if (!_begun) return;
            _spriteBatch.End();
            _begun = false;
            _clipStack.Clear();
            _themeStack.Clear();
            _currentClip = null;
            _dynamicGlyphCache.EndFrame();
            _svgRasterCache.EndFrame();
        }
        /// <summary>Clips subsequent drawing to the intersection of this rectangle and all active parent clips.</summary>
        public void PushClip(Rectangle rectangle)
        {
            if (!_begun) throw new InvalidOperationException("Begin must be called before pushing a clip.");
            _clipStack.Push(_currentClip);
            var logicalViewport = GetLogicalTargetBounds();
            _currentClip = _currentClip.HasValue ? Rectangle.Intersect(_currentClip.Value, rectangle) : Rectangle.Intersect(logicalViewport, rectangle);
            RestartBatch();
        }
        /// <summary>Restores the parent clip previously installed with <see cref="PushClip"/>.</summary>
        public void PopClip()
        {
            if (_clipStack.Count == 0) throw new InvalidOperationException("No clip is active.");
            _currentClip = _clipStack.Pop();
            RestartBatch();
        }
        internal void PushTheme(Theme themeOverride)
        {
            var inheritedParent = false;
            if (themeOverride != null && !ReferenceEquals(themeOverride, Theme) && themeOverride.Parent == null)
            {
                themeOverride.SetInheritedParent(Theme);
                inheritedParent = true;
            }
            _themeStack.Push(new ThemeScope(Theme, themeOverride, inheritedParent));
            if (themeOverride != null) Theme = themeOverride;
        }
        internal void PopTheme()
        {
            if (_themeStack.Count == 0) throw new InvalidOperationException("No theme scope is active.");
            var scope = _themeStack.Pop();
            if (scope.InheritedParent) scope.Override.SetInheritedParent(null);
            Theme = scope.Previous;
        }
        public void Fill(Rectangle rectangle, Color color)
        {
            if (rectangle.Width <= 0 || rectangle.Height <= 0) return;
            var width = GetVisibleHairlineThickness(rectangle.Width, DisplayScale);
            var height = GetVisibleHairlineThickness(rectangle.Height, DisplayScale);
            if (width == rectangle.Width && height == rectangle.Height) _spriteBatch.Draw(_pixel, rectangle, color);
            else _spriteBatch.Draw(_pixel, new Vector2(rectangle.X, rectangle.Y), null, color, 0, Vector2.Zero, new Vector2(width, height), SpriteEffects.None, 0);
        }
        internal static float GetVisibleHairlineThickness(float logicalThickness, float displayScale)
        {
            if (logicalThickness <= 0) return 0;
            return MathF.Max(logicalThickness, 1f / displayScale);
        }
        /// <summary>Draws a theme-owned atlas region into a pixel-rounded logical rectangle.</summary>
        public void Icon(ThemeIcon icon, Rectangle destination, Color color)
        {
            if (destination.Width <= 0 || destination.Height <= 0) return;
            if (icon.ScalableSource != null)
            {
                try
                {
                    if (DrawScalableImage(icon.ScalableSource, destination, color, ImageSamplingMode.Linear)) return;
                }
                catch (Exception exception) when (icon.Texture != null && exception is InvalidOperationException or SvgLoadException or NotSupportedException)
                {
                    ThemeIconSvgFallback?.Invoke();
                }
            }
            if (icon.Texture != null) _spriteBatch.Draw(icon.Texture, destination, icon.SourceRectangle, color);
            else if (icon.VectorSource != null) icon.VectorSource.Render(this, destination, color);
        }
        internal Action ThemeIconSvgFallback { get; set; }
        /// <summary>Draws a theme-owned atlas region at its stable logical size.</summary>
        public void Icon(ThemeIcon icon, Vector2 position, Color color)
        {
            var destination = new Rectangle(
                (int)MathF.Round(position.X),
                (int)MathF.Round(position.Y),
                icon.LogicalSize.X,
                icon.LogicalSize.Y);
            Icon(icon, destination, color);
        }
        /// <summary>Draws a scalable image through the exact-size device cache.</summary>
        public bool DrawScalableImage(ScalableImageSource source, Rectangle destination, Color color, ImageSamplingMode samplingMode = ImageSamplingMode.Linear)
        {
            return DrawScalableImage(
                source,
                new Rectangle(0, 0, destination.Width, destination.Height),
                Matrix.CreateTranslation(destination.X, destination.Y, 0),
                color,
                samplingMode);
        }

        internal bool DrawScalableImage(ScalableImageSource source, Rectangle bounds, Matrix transform, Color color, ImageSamplingMode samplingMode = ImageSamplingMode.Linear)
        {
            if (!_begun) throw new InvalidOperationException("Begin must be called before drawing scalable images.");
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (bounds.Width <= 0 || bounds.Height <= 0) return false;
            if (source is not SvgImageSource svgSource) throw new NotSupportedException($"Scalable image source '{source.GetType().Name}' is not supported.");
            var horizontal = Vector2.TransformNormal(new Vector2(bounds.Width, 0), transform).Length();
            var vertical = Vector2.TransformNormal(new Vector2(0, bounds.Height), transform).Length();
            if (!float.IsFinite(horizontal) || !float.IsFinite(vertical)) throw new InvalidOperationException("The scalable image transform produced non-finite dimensions.");
            var physicalWidth = checked(Math.Max(1, (int)MathF.Ceiling(horizontal * DisplayScale)));
            var physicalHeight = checked(Math.Max(1, (int)MathF.Ceiling(vertical * DisplayScale)));
            var entry = _svgRasterCache.GetOrAdd(svgSource, physicalWidth, physicalHeight);
            var texture = _svgRasterCache.GetTexture(entry);
            if (texture == null) return false;
            Drawing.DrawImage(
                texture,
                entry.Bounds,
                bounds,
                transform,
                color,
                samplingMode);
            return true;
        }
        /// <summary>Fills a deterministic pixel-rounded rectangle without requiring an external texture asset.</summary>
        public void FillRounded(Rectangle rectangle, Color color, int radius)
        {
            if (rectangle.Width <= 0 || rectangle.Height <= 0) return;
            radius = Math.Max(0, Math.Min(radius, Math.Min(rectangle.Width, rectangle.Height) / 2));
            if (radius == 0) { Fill(rectangle, color); return; }
            for (var y = 0; y < rectangle.Height; y++)
            {
                var distance = y < radius ? radius - y - .5f : y >= rectangle.Height - radius ? y - (rectangle.Height - radius) + .5f : 0;
                var inset = distance == 0 ? 0 : (int)Math.Ceiling(radius - Math.Sqrt(radius * radius - distance * distance));
                Fill(new Rectangle(rectangle.X + inset, rectangle.Y + y, Math.Max(0, rectangle.Width - inset * 2), 1), color);
            }
        }
        public void Border(Rectangle rectangle, Color color, int width = 1)
        {
            if (width <= 0 || rectangle.Width <= 0 || rectangle.Height <= 0) return;
            Fill(new Rectangle(rectangle.Left, rectangle.Top, rectangle.Width, width), color);
            Fill(new Rectangle(rectangle.Left, rectangle.Bottom - width, rectangle.Width, width), color);
            Fill(new Rectangle(rectangle.Left, rectangle.Top, width, rectangle.Height), color);
            Fill(new Rectangle(rectangle.Right - width, rectangle.Top, width, rectangle.Height), color);
        }
        public void Text(SpriteFont font, string text, Vector2 position, Color color)
        {
            Text(font, text, position, color, 1f);
        }
        public void Text(UIFont font, string text, Vector2 position, Color color)
        {
            if (font == null || string.IsNullOrEmpty(text)) return;
            Text(TextLayoutEngine.Layout(font, text), position, color);
        }
        /// <summary>Draws SpriteFont text at a deterministic uniform scale for controls with a font-size override.</summary>
        public void Text(SpriteFont font, string text, Vector2 position, Color color, float scale)
        {
            if (font == null || string.IsNullOrEmpty(text) || scale <= 0) return;
            var adapter = new SpriteFontAdapter(font, font.LineSpacing * scale);
            Text(TextLayoutEngine.Layout(adapter, text), position, color);
        }
        /// <summary>Draws an immutable layout without repeating measurement or line breaking.</summary>
        public void Text(TextLayout layout, Vector2 position, Color color)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            layout.Font.Draw(this, layout, position, color);
        }
        internal void DrawSpriteFont(SpriteFont font, string text, Vector2 position, Color color, float scale)
        {
            if (font == null || string.IsNullOrEmpty(text) || scale <= 0) return;
            var displayFont = DisplayFontResolver?.Invoke(font, DisplayScale);
            if (displayFont != null && !ReferenceEquals(displayFont, font) && displayFont.LineSpacing > 0)
            {
                scale *= font.LineSpacing / (float)displayFont.LineSpacing;
                font = displayFont;
            }
            if (Math.Abs(scale - 1f) < .0001f) { _spriteBatch.DrawString(font, text, position, color); return; }
            _spriteBatch.DrawString(font, text, position, color, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
        }
        internal void DrawDynamicGlyph(UIFont font, uint glyphId, Vector2 baselinePosition, Color color)
        {
            var rasterScale = GetDynamicGlyphRasterScale(DisplayScale);
            var glyph = _dynamicGlyphCache.GetOrAdd(font, glyphId, rasterScale);
            if (glyph.PageIndex < 0 || !glyph.Uploaded) return;
            var texture = _dynamicGlyphCache.GetTexture(glyph);
            var topLeft = GetDynamicGlyphPosition(baselinePosition, glyph.BearingX, glyph.BearingY, DisplayScale, rasterScale);
            _spriteBatch.Draw(texture, topLeft, glyph.Bounds, color, 0, Vector2.Zero, 1f / rasterScale, SpriteEffects.None, 0);
        }
        internal static float GetDynamicGlyphRasterScale(float displayScale) => MathF.Max(2, displayScale);
        internal static Vector2 GetDynamicGlyphPosition(Vector2 baselinePosition, int bearingX, int bearingY, float displayScale, float rasterScale)
        {
            return new Vector2(
                baselinePosition.X + bearingX / rasterScale,
                MathF.Round(baselinePosition.Y * displayScale) / displayScale - bearingY / rasterScale);
        }
        internal void BeginDynamicGlyphs()
        {
            if (!_begun) throw new InvalidOperationException("Begin must be called before drawing dynamic glyphs.");
            if (_dynamicGlyphsUseColorTexture) return;
            _spriteBatch.End();
            var transform = Math.Abs(DisplayScale - 1f) < .0001f ? Matrix.Identity : Matrix.CreateScale(DisplayScale, DisplayScale, 1f);
            var viewport = GraphicsDevice.Viewport;
            var projection = Matrix.CreateOrthographicOffCenter(0, viewport.Width, viewport.Height, 0, 0, -1);
            _alpha8CoverageEffect.Parameters["MatrixTransform"].SetValue(transform * projection);
            _spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, _currentClip.HasValue ? _scissorRasterizer : RasterizerState.CullNone, null, transform);
            _alpha8CoverageEffect.CurrentTechnique.Passes[0].Apply();
        }
        internal void EndDynamicGlyphs()
        {
            if (_dynamicGlyphsUseColorTexture) return;
            _spriteBatch.End();
            BeginBatch();
        }
        internal DynamicGlyphCacheDiagnostics DynamicGlyphDiagnostics => _dynamicGlyphCache.Diagnostics;
        internal IReadOnlyList<DynamicGlyphAtlasPageSnapshot> DynamicGlyphPages => _dynamicGlyphCache.GetDebugPages();
        internal void ClearDynamicGlyphCache() => _dynamicGlyphCache.Clear();
        internal SvgRasterCacheDiagnostics SvgRasterDiagnostics => _svgRasterCache.Diagnostics;
        internal IReadOnlyList<SvgRasterAtlasPageSnapshot> SvgRasterPages => _svgRasterCache.GetDebugPages();
        internal void ClearSvgRasterCache() => _svgRasterCache.Clear();
        internal void PrewarmSvg(SvgImageSource source, Vector2 logicalSize)
        {
            var width = checked((int)MathF.Ceiling(logicalSize.X * DisplayScale));
            var height = checked((int)MathF.Ceiling(logicalSize.Y * DisplayScale));
            _svgRasterCache.BeginFrame();
            try { _svgRasterCache.GetOrAdd(source, width, height); }
            finally { _svgRasterCache.EndFrame(); }
        }
        internal void RenderToTarget(RenderTarget2D target, Color clearColor, Action draw)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            if (!_begun) throw new InvalidOperationException("Begin must be called before rendering a nested target.");
            _spriteBatch.End();
            _begun = false;
            var previousTargets = GraphicsDevice.GetRenderTargets();
            var previousViewport = GraphicsDevice.Viewport;
            try
            {
                GraphicsDevice.SetRenderTarget(target);
                GraphicsDevice.Clear(clearColor);
                draw();
            }
            finally
            {
                if (previousTargets.Length == 0) GraphicsDevice.SetRenderTarget(null);
                else GraphicsDevice.SetRenderTargets(previousTargets);
                GraphicsDevice.Viewport = previousViewport;
                BeginBatch();
            }
        }
        internal void DrawClipped(DrawingPath clipPath, Matrix clipTransform, Rectangle bounds, Action draw)
        {
            if (clipPath == null) throw new ArgumentNullException(nameof(clipPath));
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            if (!_begun) throw new InvalidOperationException("Begin must be called before compositing a clip.");
            if (_compositorDepth >= DrawingContextLimits.MaximumOffscreenNestingDepth)
                throw new InvalidOperationException($"Offscreen nesting cannot exceed {DrawingContextLimits.MaximumOffscreenNestingDepth}.");
            if (!TryCaptureToTarget(draw, bounds, out var target, out var capturedBounds)) return;
            Drawing.DrawImageUncomposited(target, capturedBounds, clipPath, clipTransform);
        }
        internal void DrawScaled(Rectangle sourceBounds, Rectangle destinationBounds, ImageSamplingMode samplingMode, Action draw)
            => DrawScaled(sourceBounds, new Vector4(destinationBounds.X, destinationBounds.Y, destinationBounds.Width, destinationBounds.Height), samplingMode, draw);

        internal void DrawScaled(Rectangle sourceBounds, Vector4 destinationBounds, ImageSamplingMode samplingMode, Action draw)
        {
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            if (sourceBounds.Width <= 0 || sourceBounds.Height <= 0 || destinationBounds.Z <= 0 || destinationBounds.W <= 0) return;
            if (!TryCaptureToTarget(draw, sourceBounds, out var target, out _)) return;
            Drawing.DrawImageUncomposited(target, new Rectangle(0, 0, target.Width, target.Height), destinationBounds, samplingMode);
        }
        internal void DrawOpacity(float opacity, Rectangle bounds, Action draw)
        {
            if (!float.IsFinite(opacity) || opacity < 0 || opacity > 1) throw new ArgumentOutOfRangeException(nameof(opacity));
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            if (opacity == 0) return;
            if (!TryCaptureToTarget(draw, bounds, out var target, out var capturedBounds)) return;
            Drawing.DrawImage(target, null, capturedBounds, Matrix.CreateTranslation(capturedBounds.X, capturedBounds.Y, 0), Color.White * opacity);
        }
        internal void DrawTransformed(Matrix transform, Rectangle bounds, Action draw)
        {
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            if (!TryCaptureToTarget(draw, bounds, out var target, out var capturedBounds)) return;
            Drawing.DrawImage(target, null, capturedBounds, Matrix.CreateTranslation(capturedBounds.X, capturedBounds.Y, 0) * transform, Color.White);
        }
        internal void DrawEffect(VisualEffect effect, Rectangle contentBounds, Action draw)
        {
            if (effect == null) throw new ArgumentNullException(nameof(effect));
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            IList<VisualEffect> effects = effect is EffectGroup group ? group.Children : new[] { effect };
            if (effects.Count == 0) { draw(); return; }
            var logicalViewport = GetLogicalTargetBounds();
            var processingBounds = GetEffectProcessingBounds(effect, contentBounds, logicalViewport);
            if (processingBounds.Width <= 0 || processingBounds.Height <= 0) { draw(); return; }
            var physicalBounds = ToPhysicalRectangle(processingBounds);
            if (!TryValidateEffectCapture(physicalBounds, out var diagnostic))
            {
                ReportCompositorDiagnostic(diagnostic);
                draw();
                return;
            }
            if (!TryCaptureToTarget(draw, processingBounds, out var target, out var capturedBounds)) return;
            var pixels = new Color[target.Width * target.Height];
            target.GetData(pixels);
            foreach (var child in effects)
            {
                if (child is ColorMatrixEffect matrix)
                {
                    var drawingEffect = matrix.CreateDrawingEffect();
                    for (var index = 0; index < pixels.Length; index++) pixels[index] = drawingEffect.Apply(pixels[index]);
                    continue;
                }
                var logicalRadius = child is BlurEffect blur ? blur.Radius : child is DropShadowEffect shadow ? shadow.BlurRadius : -1;
                if (logicalRadius < 0) throw new NotSupportedException($"{child.GetType().Name} is not a supported compositing effect.");
                var radius = Math.Min((int)MathF.Ceiling(logicalRadius * DisplayScale), (int)MathF.Ceiling(DrawingContextLimits.MaximumBlurRadius * DisplayScale));
                var filtered = BoxBlur(pixels, target.Width, target.Height, radius);
                if (child is DropShadowEffect dropShadow)
                {
                    var shadowPixels = CreateShadow(filtered, target.Width, target.Height, dropShadow.Color,
                        (int)MathF.Round(dropShadow.Offset.X * DisplayScale), (int)MathF.Round(dropShadow.Offset.Y * DisplayScale));
                    for (var index = 0; index < pixels.Length; index++) pixels[index] = CompositeOver(pixels[index], shadowPixels[index]);
                }
                else pixels = filtered;
            }
            EnsureTransientTargetBudget((long)target.Width * target.Height * 4);
            var processed = new Texture2D(GraphicsDevice, target.Width, target.Height, false, SurfaceFormat.Color);
            processed.SetData(pixels);
            Drawing.DrawImageUncomposited(processed, capturedBounds);
            _activeTransientTextures.Add(processed);
        }
        internal static Rectangle GetEffectProcessingBounds(VisualEffect effect, Rectangle contentBounds, Rectangle viewportBounds)
        {
            if (effect == null) throw new ArgumentNullException(nameof(effect));
            var left = contentBounds.Left;
            var top = contentBounds.Top;
            var right = contentBounds.Right;
            var bottom = contentBounds.Bottom;
            var effects = effect is EffectGroup group ? group.Children : new[] { effect };
            foreach (var child in effects)
            {
                var radius = child is BlurEffect blur
                    ? (int)MathF.Ceiling(blur.Radius)
                    : child is DropShadowEffect shadow
                        ? (int)MathF.Ceiling(shadow.BlurRadius)
                        : 0;
                left -= radius;
                top -= radius;
                right += radius;
                bottom += radius;
                if (child is not DropShadowEffect dropShadow) continue;
                left -= (int)MathF.Ceiling(Math.Max(0, -dropShadow.Offset.X));
                top -= (int)MathF.Ceiling(Math.Max(0, -dropShadow.Offset.Y));
                right += (int)MathF.Ceiling(Math.Max(0, dropShadow.Offset.X));
                bottom += (int)MathF.Ceiling(Math.Max(0, dropShadow.Offset.Y));
            }
            return Rectangle.Intersect(viewportBounds, new Rectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top)));
        }
        internal void DrawOpacityMask(Brush mask, Rectangle bounds, Action draw)
        {
            if (mask == null) throw new ArgumentNullException(nameof(mask));
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            if (!TryCaptureToTarget(draw, bounds, out var target, out var capturedBounds)) return;
            var pixels = new Color[target.Width * target.Height];
            target.GetData(pixels);
            for (var y = 0; y < target.Height; y++)
            for (var x = 0; x < target.Width; x++)
            {
                var maskAlpha = mask.Sample(new Vector2(capturedBounds.X + (x + .5f) / DisplayScale, capturedBounds.Y + (y + .5f) / DisplayScale), bounds).A / 255f;
                pixels[x + y * target.Width] *= maskAlpha;
            }
            EnsureTransientTargetBudget((long)target.Width * target.Height * 4);
            var processed = new Texture2D(GraphicsDevice, target.Width, target.Height, false, SurfaceFormat.Color);
            processed.SetData(pixels);
            Drawing.DrawImageUncomposited(processed, capturedBounds);
            _activeTransientTextures.Add(processed);
        }
        internal void DrawShadow(DropShadowEffect shadow, Rectangle contentBounds, Action drawMask, DrawingPath clipPath = null, Matrix clipTransform = default, bool inset = false)
        {
            if (shadow == null) throw new ArgumentNullException(nameof(shadow));
            if (drawMask == null) throw new ArgumentNullException(nameof(drawMask));
            var processingBounds = inset
                ? GetInsetShadowProcessingBounds(shadow, contentBounds, GetLogicalTargetBounds())
                : GetEffectProcessingBounds(shadow, contentBounds, GetLogicalTargetBounds());
            if (!TryCaptureToTarget(drawMask, processingBounds, out var target, out var capturedBounds)) return;
            var pixels = new Color[target.Width * target.Height];
            target.GetData(pixels);
            if (inset)
            {
                for (var index = 0; index < pixels.Length; index++)
                    pixels[index] = new Color(255, 255, 255, 255 - pixels[index].A);
            }
            var radius = Math.Min((int)MathF.Ceiling(shadow.BlurRadius * DisplayScale), (int)MathF.Ceiling(DrawingContextLimits.MaximumBlurRadius * DisplayScale));
            var filtered = BoxBlur(pixels, target.Width, target.Height, radius);
            EnsureTransientTargetBudget((long)target.Width * target.Height * 4);
            var processed = new Texture2D(GraphicsDevice, target.Width, target.Height, false, SurfaceFormat.Color);
            processed.SetData(CreateShadow(filtered, target.Width, target.Height, shadow.Color,
                (int)MathF.Round(shadow.Offset.X * DisplayScale), (int)MathF.Round(shadow.Offset.Y * DisplayScale)));
            if (clipPath != null)
            {
                Drawing.DrawImageUncomposited(processed, capturedBounds, clipPath, clipTransform);
            }
            else Drawing.DrawImageUncomposited(processed, capturedBounds);
            _activeTransientTextures.Add(processed);
        }
        private static Rectangle GetInsetShadowProcessingBounds(DropShadowEffect shadow, Rectangle contentBounds, Rectangle viewportBounds)
        {
            var horizontalPadding = (int)MathF.Ceiling(shadow.BlurRadius + MathF.Abs(shadow.Offset.X));
            var verticalPadding = (int)MathF.Ceiling(shadow.BlurRadius + MathF.Abs(shadow.Offset.Y));
            return Rectangle.Intersect(viewportBounds, new Rectangle(
                contentBounds.X - horizontalPadding,
                contentBounds.Y - verticalPadding,
                contentBounds.Width + horizontalPadding * 2,
                contentBounds.Height + verticalPadding * 2));
        }
        /// <summary>Draws a texture-clipped triangle fan while preserving the active UI clip and batch state.</summary>
        public void TexturedFan(Texture2D texture, Vector2 center, Vector2 centerUv, IReadOnlyList<Vector2> boundary, IReadOnlyList<Vector2> uvs, Color color)
        {
            if (texture == null || boundary == null || uvs == null || boundary.Count < 2 || boundary.Count != uvs.Count) return;
            _spriteBatch.End();
            var vertices = new VertexPositionColorTexture[(boundary.Count - 1) * 3];
            centerUv = Vector2.Clamp(centerUv, Vector2.Zero, Vector2.One);
            for (var i = 0; i < boundary.Count - 1; i++)
            {
                var index = i * 3;
                vertices[index] = new VertexPositionColorTexture(new Vector3(center, 0), color, centerUv);
                vertices[index + 1] = new VertexPositionColorTexture(new Vector3(boundary[i], 0), color, uvs[i]);
                vertices[index + 2] = new VertexPositionColorTexture(new Vector3(boundary[i + 1], 0), color, uvs[i + 1]);
            }
            _basicEffect.Texture = texture;
            _basicEffect.TextureEnabled = true;
            _basicEffect.World = Matrix.CreateTranslation(-_captureOrigin.X, -_captureOrigin.Y, 0);
            _basicEffect.View = Matrix.Identity;
            _basicEffect.Projection = Matrix.CreateOrthographicOffCenter(0, GraphicsDevice.Viewport.Width / DisplayScale, GraphicsDevice.Viewport.Height / DisplayScale, 0, 0, 1);
            GraphicsDevice.BlendState = BlendState.AlphaBlend;
            GraphicsDevice.DepthStencilState = DepthStencilState.None;
            GraphicsDevice.RasterizerState = _currentClip.HasValue ? _scissorRasterizer : RasterizerState.CullNone;
            GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;
            foreach (var pass in _basicEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, boundary.Count - 1);
            }
            BeginBatch();
        }
        internal void Draw(DrawingMesh mesh, Func<Vector2, Color> colorAt)
        {
            if (mesh.Vertices == null || mesh.Indices == null || mesh.Indices.Length == 0) return;
            if (colorAt == null) throw new ArgumentNullException(nameof(colorAt));
            _spriteBatch.End();
            var vertices = new VertexPositionColor[mesh.Indices.Length];
            for (var index = 0; index < mesh.Indices.Length; index++)
            {
                var point = mesh.Vertices[mesh.Indices[index]];
                vertices[index] = new VertexPositionColor(new Vector3(point, 0), colorAt(point));
            }
            _basicEffect.TextureEnabled = false;
            _basicEffect.World = Matrix.CreateTranslation(-_captureOrigin.X, -_captureOrigin.Y, 0);
            _basicEffect.View = Matrix.Identity;
            _basicEffect.Projection = Matrix.CreateOrthographicOffCenter(0, GraphicsDevice.Viewport.Width / DisplayScale, GraphicsDevice.Viewport.Height / DisplayScale, 0, 0, 1);
            GraphicsDevice.BlendState = BlendState.AlphaBlend;
            GraphicsDevice.DepthStencilState = DepthStencilState.None;
            GraphicsDevice.RasterizerState = _currentClip.HasValue ? _scissorRasterizer : RasterizerState.CullNone;
            foreach (var pass in _basicEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, vertices.Length / 3);
            }
            _basicEffect.Texture = null;
            _basicEffect.TextureEnabled = false;
            GraphicsDevice.Textures[0] = null;
            GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;
            BeginBatch();
        }
        internal void DrawTextured(DrawingMesh mesh, Texture2D texture, Func<Vector2, (Vector2 Coordinate, Color Color)> sampleAt, ImageSamplingMode samplingMode, ImageTileMode tileMode)
        {
            if (mesh.Vertices == null || mesh.Indices == null || mesh.Indices.Length == 0 || texture == null) return;
            if (sampleAt == null) throw new ArgumentNullException(nameof(sampleAt));
            _spriteBatch.End();
            var vertices = new VertexPositionColorTexture[mesh.Indices.Length];
            for (var index = 0; index < mesh.Indices.Length; index++)
            {
                var point = mesh.Vertices[mesh.Indices[index]];
                var sample = sampleAt(point);
                vertices[index] = new VertexPositionColorTexture(new Vector3(point, 0), sample.Color, sample.Coordinate);
            }
            _basicEffect.Texture = texture;
            _basicEffect.TextureEnabled = true;
            _basicEffect.World = Matrix.CreateTranslation(-_captureOrigin.X, -_captureOrigin.Y, 0);
            _basicEffect.View = Matrix.Identity;
            _basicEffect.Projection = Matrix.CreateOrthographicOffCenter(0, GraphicsDevice.Viewport.Width / DisplayScale, GraphicsDevice.Viewport.Height / DisplayScale, 0, 0, 1);
            GraphicsDevice.BlendState = BlendState.AlphaBlend;
            GraphicsDevice.DepthStencilState = DepthStencilState.None;
            GraphicsDevice.RasterizerState = _currentClip.HasValue ? _scissorRasterizer : RasterizerState.CullNone;
            GraphicsDevice.SamplerStates[0] = _imageSamplers[(int)samplingMode * 4 + (int)tileMode];
            foreach (var pass in _basicEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, vertices.Length / 3);
            }
            _basicEffect.Texture = null;
            _basicEffect.TextureEnabled = false;
            GraphicsDevice.Textures[0] = null;
            GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;
            BeginBatch();
        }
        internal void DrawSampled(DrawingMesh mesh, Rectangle bounds, Func<Vector2, Color> sampleAt)
        {
            if (mesh.Vertices == null || mesh.Indices == null || mesh.Indices.Length == 0 || bounds.Width <= 0 || bounds.Height <= 0) return;
            if (sampleAt == null) throw new ArgumentNullException(nameof(sampleAt));
            var width = Math.Max(1, (int)MathF.Ceiling(bounds.Width * DisplayScale));
            var height = Math.Max(1, (int)MathF.Ceiling(bounds.Height * DisplayScale));
            if (width > DrawingContextLimits.MaximumRenderTargetDimension || height > DrawingContextLimits.MaximumRenderTargetDimension ||
                (long)width * height > DrawingContextLimits.MaximumRenderTargetArea)
                throw new InvalidOperationException("The sampled brush bounds exceed the bounded compositor limits.");
            EnsureTransientTargetBudget((long)width * height * 4);
            var pixels = new Color[width * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                pixels[x + y * width] = sampleAt(new Vector2(bounds.X + (x + .5f) / DisplayScale, bounds.Y + (y + .5f) / DisplayScale));
            var texture = new Texture2D(GraphicsDevice, width, height, false, SurfaceFormat.Color);
            var completed = false;
            try
            {
                texture.SetData(pixels);
                DrawTextured(mesh, texture, point =>
                {
                    var coordinate = new Vector2((point.X - bounds.X) * DisplayScale / width, (point.Y - bounds.Y) * DisplayScale / height);
                    return (coordinate, Color.White);
                }, ImageSamplingMode.Linear, ImageTileMode.None);
                _activeTransientTextures.Add(texture);
                completed = true;
            }
            finally
            {
                if (!completed) texture.Dispose();
            }
        }

        private static SamplerState[] CreateImageSamplers()
        {
            var samplers = new SamplerState[8];
            foreach (var samplingMode in Enum.GetValues<ImageSamplingMode>())
            foreach (var tileMode in Enum.GetValues<ImageTileMode>())
            {
                var index = (int)samplingMode * 4 + (int)tileMode;
                if (tileMode == ImageTileMode.None)
                    samplers[index] = samplingMode == ImageSamplingMode.Nearest ? SamplerState.PointClamp : SamplerState.LinearClamp;
                else
                    samplers[index] = samplingMode == ImageSamplingMode.Nearest ? SamplerState.PointWrap : SamplerState.LinearWrap;
            }
            return samplers;
        }
        private void RestartBatch()
        {
            _spriteBatch.End();
            BeginBatch();
        }
        private void BeginBatch(Effect effect = null)
        {
            var transform = Matrix.CreateTranslation(-_captureOrigin.X, -_captureOrigin.Y, 0) * Matrix.CreateScale(DisplayScale, DisplayScale, 1f);
            if (effect != null)
            {
                var viewport = GraphicsDevice.Viewport;
                var projection = Matrix.CreateOrthographicOffCenter(0, viewport.Width, viewport.Height, 0, 0, -1);
                effect.Parameters["MatrixTransform"].SetValue(transform * projection);
            }
            if (_currentClip.HasValue)
            {
                var viewport = GraphicsDevice.Viewport;
                var clip = ToPhysicalRectangle(_currentClip.Value);
                if (clip.Width <= 0 || clip.Height <= 0) clip = new Rectangle(viewport.X, viewport.Y, 1, 1);
                GraphicsDevice.ScissorRectangle = clip;
                _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, _scissorRasterizer, effect, transform);
            }
            else _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, null, effect, transform);
            _begun = true;
        }
        private Rectangle ToPhysicalRectangle(Rectangle rectangle)
        {
            var viewport = GraphicsDevice.Viewport;
            var left = viewport.X + (int)MathF.Floor((rectangle.Left - _captureOrigin.X) * DisplayScale);
            var top = viewport.Y + (int)MathF.Floor((rectangle.Top - _captureOrigin.Y) * DisplayScale);
            var right = viewport.X + (int)MathF.Ceiling((rectangle.Right - _captureOrigin.X) * DisplayScale);
            var bottom = viewport.Y + (int)MathF.Ceiling((rectangle.Bottom - _captureOrigin.Y) * DisplayScale);
            return Rectangle.Intersect(viewport.Bounds, new Rectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top)));
        }
        private Rectangle GetLogicalTargetBounds()
        {
            var viewport = GraphicsDevice.Viewport;
            return new Rectangle(
                (int)MathF.Floor(_captureOrigin.X),
                (int)MathF.Floor(_captureOrigin.Y),
                Math.Max(1, (int)MathF.Ceiling(viewport.Width / DisplayScale)),
                Math.Max(1, (int)MathF.Ceiling(viewport.Height / DisplayScale)));
        }
        private void EnsureTransientTargetBudget(long additionalBytes)
        {
            var bytes = GetTransientTextureBytes() + additionalBytes;
            if (bytes > DrawingContextLimits.MaximumDeviceCacheBytes)
                throw new InvalidOperationException("The transient compositor target budget has been exceeded.");
        }
        private long GetTransientTextureBytes()
        {
            long bytes = 0;
            foreach (var texture in _activeTransientTextures) bytes += (long)texture.Width * texture.Height * 4;
            foreach (var texture in _retiredTransientTextures) bytes += (long)texture.Width * texture.Height * 4;
            return bytes;
        }
        private bool TryValidateEffectCapture(Rectangle physicalBounds, out string diagnostic)
        {
            if (!_begun) throw new InvalidOperationException("Begin must be called before offscreen composition.");
            if (_compositorDepth >= DrawingContextLimits.MaximumOffscreenNestingDepth)
            {
                diagnostic = $"Effect disabled: offscreen nesting exceeded {DrawingContextLimits.MaximumOffscreenNestingDepth}.";
                return false;
            }
            if (physicalBounds.Width > DrawingContextLimits.MaximumRenderTargetDimension || physicalBounds.Height > DrawingContextLimits.MaximumRenderTargetDimension ||
                (long)physicalBounds.Width * physicalBounds.Height > DrawingContextLimits.MaximumRenderTargetArea)
            {
                diagnostic = "Effect disabled: the active viewport exceeds bounded compositor dimensions.";
                return false;
            }
            var requiredBytes = (long)physicalBounds.Width * physicalBounds.Height * 8;
            if (GetTransientTextureBytes() + requiredBytes > DrawingContextLimits.MaximumDeviceCacheBytes)
            {
                diagnostic = "Effect disabled: the transient compositor byte budget would be exceeded.";
                return false;
            }
            diagnostic = null;
            return true;
        }
        private void ReportCompositorDiagnostic(string diagnostic)
        {
            if (string.IsNullOrEmpty(diagnostic) || !_reportedCompositorDiagnostics.Add(diagnostic)) return;
            if (_compositorDiagnostics.Count >= DrawingContextLimits.MaximumRuntimeDiagnostics) return;
            _compositorDiagnostics.Add(diagnostic);
        }
        private void RetireTransientTargets()
        {
            DisposeTextures(_retiredTransientTextures);
            _retiredTransientTextures.AddRange(_activeTransientTextures);
            _activeTransientTextures.Clear();
        }
        private void OnDeviceReset(object sender, EventArgs args)
        {
            DisposeTextures(_activeTransientTextures);
            DisposeTextures(_retiredTransientTextures);
        }
        private void OnGraphicsDeviceDisposing(object sender, EventArgs args) => Dispose();
        private bool TryCaptureToTarget(Action draw, Rectangle bounds, out RenderTarget2D target, out Rectangle capturedBounds)
        {
            if (!_begun) throw new InvalidOperationException("Begin must be called before offscreen composition.");
            if (_compositorDepth >= DrawingContextLimits.MaximumOffscreenNestingDepth)
                throw new InvalidOperationException($"Offscreen nesting cannot exceed {DrawingContextLimits.MaximumOffscreenNestingDepth}.");
            capturedBounds = Rectangle.Intersect(GetLogicalTargetBounds(), bounds);
            if (capturedBounds.Width <= 0 || capturedBounds.Height <= 0)
            {
                target = null;
                return false;
            }
            var viewport = GraphicsDevice.Viewport;
            var physicalBounds = ToPhysicalRectangle(capturedBounds);
            var width = physicalBounds.Width;
            var height = physicalBounds.Height;
            if (width > DrawingContextLimits.MaximumRenderTargetDimension || height > DrawingContextLimits.MaximumRenderTargetDimension ||
                (long)width * height > DrawingContextLimits.MaximumRenderTargetArea)
                throw new InvalidOperationException("The active viewport exceeds the bounded compositor limits.");
            EnsureTransientTargetBudget((long)width * height * 4);
            target = new RenderTarget2D(GraphicsDevice, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            var previousTargets = GraphicsDevice.GetRenderTargets();
            var previousBackBufferUsage = GraphicsDevice.PresentationParameters.RenderTargetUsage;
            var previousCaptureOrigin = _captureOrigin;
            var captureOrigin = previousCaptureOrigin + new Vector2(physicalBounds.X / DisplayScale, physicalBounds.Y / DisplayScale);
            var completed = false;
            _compositorDepth++;
            _spriteBatch.End();
            _begun = false;
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            try
            {
                if (previousTargets.Length == 0)
                    GraphicsDevice.PresentationParameters.RenderTargetUsage = RenderTargetUsage.PreserveContents;
                GraphicsDevice.SetRenderTarget(target);
                _captureOrigin = captureOrigin;
                GraphicsDevice.Clear(Color.Transparent);
                BeginBatch();
                draw();
                _spriteBatch.End();
                _begun = false;
                GraphicsDevice.RasterizerState = RasterizerState.CullNone;
                if (previousTargets.Length == 0) GraphicsDevice.SetRenderTarget(null);
                else GraphicsDevice.SetRenderTargets(previousTargets);
                GraphicsDevice.PresentationParameters.RenderTargetUsage = previousBackBufferUsage;
                GraphicsDevice.Viewport = viewport;
                _captureOrigin = previousCaptureOrigin;
                BeginBatch();
                _activeTransientTextures.Add(target);
                completed = true;
                return true;
            }
            finally
            {
                _compositorDepth--;
                if (!completed)
                {
                    if (_begun) { _spriteBatch.End(); _begun = false; }
                    GraphicsDevice.RasterizerState = RasterizerState.CullNone;
                    if (previousTargets.Length == 0) GraphicsDevice.SetRenderTarget(null);
                    else GraphicsDevice.SetRenderTargets(previousTargets);
                    GraphicsDevice.PresentationParameters.RenderTargetUsage = previousBackBufferUsage;
                    GraphicsDevice.Viewport = viewport;
                    _captureOrigin = previousCaptureOrigin;
                    BeginBatch();
                    target.Dispose();
                }
            }
        }
        private static Color[] BoxBlur(Color[] source, int width, int height, int radius)
        {
            if (radius <= 0) return (Color[])source.Clone();
            var horizontal = new Color[source.Length];
            var result = new Color[source.Length];
            var diameter = radius * 2 + 1;
            for (var y = 0; y < height; y++)
            {
                long red = 0, green = 0, blue = 0, alpha = 0;
                for (var x = 0; x <= radius && x < width; x++) Add(source[y * width + x], ref red, ref green, ref blue, ref alpha);
                for (var x = 0; x < width; x++)
                {
                    horizontal[y * width + x] = Average(red, green, blue, alpha, diameter);
                    var remove = x - radius;
                    var add = x + radius + 1;
                    if (remove >= 0) Subtract(source[y * width + remove], ref red, ref green, ref blue, ref alpha);
                    if (add < width) Add(source[y * width + add], ref red, ref green, ref blue, ref alpha);
                }
            }
            for (var x = 0; x < width; x++)
            {
                long red = 0, green = 0, blue = 0, alpha = 0;
                for (var y = 0; y <= radius && y < height; y++) Add(horizontal[y * width + x], ref red, ref green, ref blue, ref alpha);
                for (var y = 0; y < height; y++)
                {
                    result[y * width + x] = Average(red, green, blue, alpha, diameter);
                    var remove = y - radius;
                    var add = y + radius + 1;
                    if (remove >= 0) Subtract(horizontal[remove * width + x], ref red, ref green, ref blue, ref alpha);
                    if (add < height) Add(horizontal[add * width + x], ref red, ref green, ref blue, ref alpha);
                }
            }
            return result;
        }
        private static Color[] CreateShadow(Color[] source, int width, int height, Color color, int offsetX, int offsetY)
        {
            var result = new Color[source.Length];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var sourceX = x - offsetX;
                var sourceY = y - offsetY;
                if (sourceX < 0 || sourceX >= width || sourceY < 0 || sourceY >= height) continue;
                var alpha = source[sourceY * width + sourceX].A * color.A / 255;
                result[y * width + x] = new Color(color.R * alpha / 255, color.G * alpha / 255, color.B * alpha / 255, alpha);
            }
            return result;
        }
        private static Color CompositeOver(Color foreground, Color background)
        {
            var inverseAlpha = 255 - foreground.A;
            return new Color(
                Math.Min(255, foreground.R + background.R * inverseAlpha / 255),
                Math.Min(255, foreground.G + background.G * inverseAlpha / 255),
                Math.Min(255, foreground.B + background.B * inverseAlpha / 255),
                Math.Min(255, foreground.A + background.A * inverseAlpha / 255));
        }
        private static void Add(Color color, ref long red, ref long green, ref long blue, ref long alpha)
        {
            red += color.R; green += color.G; blue += color.B; alpha += color.A;
        }
        private static void Subtract(Color color, ref long red, ref long green, ref long blue, ref long alpha)
        {
            red -= color.R; green -= color.G; blue -= color.B; alpha -= color.A;
        }
        private static Color Average(long red, long green, long blue, long alpha, int count) => new Color((int)(red / count), (int)(green / count), (int)(blue / count), (int)(alpha / count));
        private static void DisposeTextures(List<Texture2D> textures)
        {
            foreach (var texture in textures) texture.Dispose();
            textures.Clear();
        }
        private readonly struct ThemeScope
        {
            public ThemeScope(Theme previous, Theme themeOverride, bool inheritedParent) { Previous = previous; Override = themeOverride; InheritedParent = inheritedParent; }
            public Theme Previous { get; }
            public Theme Override { get; }
            public bool InheritedParent { get; }
        }
        public void Dispose()
        {
            GraphicsDevice.DeviceReset -= OnDeviceReset;
            GraphicsDevice.Disposing -= OnGraphicsDeviceDisposing;
            DisposeTextures(_activeTransientTextures);
            DisposeTextures(_retiredTransientTextures);
            _dynamicGlyphCache.Dispose();
            _svgRasterCacheLease.Dispose();
            _spriteBatch.Dispose();
            _pixel.Dispose();
            _basicEffect.Dispose();
            _alpha8CoverageEffect.Dispose();
            _scissorRasterizer.Dispose();
        }
    }
}
