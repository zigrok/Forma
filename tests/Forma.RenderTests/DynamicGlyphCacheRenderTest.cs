// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.IO;
using System.Linq;
using Forma;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NUnit.Framework;

namespace Forma.RenderTests
{
    [NonParallelizable]
    [Platform(Exclude = "MacOsX", Reason = "SDL graphics-device creation must run on the macOS main thread; compilation remains validated here.")]
    internal sealed class DynamicGlyphCacheRenderTest : GraphicsDeviceTestFixtureBase
    {
        [Test]
        public void CacheBatchesAlpha8UploadsAndPreservesCoverage()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var font = new DynamicUIFont(face, 18);
            using var cache = new DynamicGlyphCache(gd, new DynamicGlyphCacheOptions(64, 64, 2, 1));
            cache.BeginFrame();
            var first = cache.GetOrAdd(font, face.GetGlyphId('A'), 1);
            var second = cache.GetOrAdd(font, face.GetGlyphId('B'), 1);
            cache.EndFrame();

            Assert.That(cache.Diagnostics.PendingUploads, Is.EqualTo(1));
            cache.FlushUploads();
            var texture = cache.GetTexture(first);
            var pixels = new byte[texture.Width * texture.Height];
            texture.GetData(pixels);

            Assert.Multiple(() =>
            {
                Assert.That(texture.Format, Is.EqualTo(SurfaceFormat.Alpha8));
                Assert.That(cache.GetTexture(second), Is.SameAs(texture));
                Assert.That(cache.Diagnostics.Uploads, Is.EqualTo(1));
                Assert.That(cache.Diagnostics.PendingUploads, Is.Zero);
                Assert.That(pixels, Has.Some.GreaterThan((byte)0));
            });
        }

        [Test]
        public void IndependentGraphicsDevicesOwnIndependentTextures()
        {
            using var otherGame = new Microsoft.Xna.Framework.Game();
            _ = new Microsoft.Xna.Framework.GraphicsDeviceManager(otherGame) { GraphicsProfile = GraphicsProfile.HiDef };
            ((Microsoft.Xna.Framework.IGraphicsDeviceManager)otherGame.Services.GetService(typeof(Microsoft.Xna.Framework.IGraphicsDeviceManager))).CreateDevice();
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            using var firstCache = new DynamicGlyphCache(gd, new DynamicGlyphCacheOptions(64, 64, 1, 1));
            using var secondCache = new DynamicGlyphCache(otherGame.GraphicsDevice, new DynamicGlyphCacheOptions(64, 64, 1, 1));
            var first = Prepare(firstCache, face);
            var second = Prepare(secondCache, face);

            Assert.That(firstCache.GetTexture(first), Is.Not.SameAs(secondCache.GetTexture(second)));
        }

        [Test]
        public void DynamicLayoutRendersOnTheFirstFrameAfterABatchedUpload()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var layout = new TextLayoutEngine().Layout(new DynamicUIFont(face, 20), "Atlas");
            using var target = new RenderTarget2D(gd, 96, 40, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            using var context = new UIRenderContext(gd, new Theme());

            DrawFrame(context, target, layout);
            var firstFrame = ReadPixels(target);
            DrawFrame(context, target, layout);
            var secondFrame = ReadPixels(target);
            var populated = context.DynamicGlyphDiagnostics;
            DrawFrame(context, target, layout);
            var warmFrame = ReadPixels(target);
            var warm = context.DynamicGlyphDiagnostics;

            Assert.Multiple(() =>
            {
                // Uploads are flushed before the text batch begins, so the very first frame already shows the glyphs.
                Assert.That(firstFrame.Any(pixel => pixel != Color.Transparent), Is.True);
                Assert.That(secondFrame.Any(pixel => pixel != Color.Transparent), Is.True);
                Assert.That(warmFrame, Is.EqualTo(secondFrame));
                Assert.That(warm.Misses, Is.EqualTo(populated.Misses));
                Assert.That(warm.Uploads, Is.EqualTo(populated.Uploads));
                Assert.That(warm.PendingUploads, Is.Zero);
            });
        }

        [Test]
        public void DynamicLabelUsesFallbackLayoutAndAtlasRenderer()
        {
            using var latinFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            using var arabicFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansArabic_Variable.ttf");
            using var target = new RenderTarget2D(gd, 160, 64, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            using var ui = new UIContext { ViewportSize = new Vector2(target.Width, target.Height) };
            ui.Add(new Label
            {
                UIFont = new DynamicUIFont(latinFace, 20, UIFontHinting.Default, arabicFace),
                Text = "Forma مرحبا",
                Language = "ar",
                Padding = new Thickness(4),
                Size = new Vector2(target.Width, target.Height),
                FontColor = Color.White
            });

            DrawUiFrame(ui, target);
            DrawUiFrame(ui, target);
            var pixels = ReadPixels(target);
            Assert.That(pixels.Any(pixel => pixel != Color.Transparent), Is.True);
        }

        private void DrawFrame(UIRenderContext context, RenderTarget2D target, TextLayout layout)
        {
            gd.SetRenderTarget(target);
            gd.Clear(Color.Transparent);
            context.Begin();
            context.PushClip(new Rectangle(0, 0, target.Width, target.Height));
            context.Text(layout, new Vector2(4, 4), Color.White);
            context.PopClip();
            context.End();
            gd.SetRenderTarget(null);
        }

        private static Color[] ReadPixels(RenderTarget2D target)
        {
            var pixels = new Color[target.Width * target.Height];
            target.GetData(pixels);
            return pixels;
        }

        private void DrawUiFrame(UIContext ui, RenderTarget2D target)
        {
            gd.SetRenderTarget(target);
            gd.Clear(Color.Transparent);
            ui.Draw(gd);
            gd.SetRenderTarget(null);
        }

        private static DynamicGlyphAtlasEntry Prepare(DynamicGlyphCache cache, UIFontFace face)
        {
            var font = new DynamicUIFont(face, 18);
            cache.BeginFrame();
            var entry = cache.GetOrAdd(font, face.GetGlyphId('A'), 1);
            cache.EndFrame();
            cache.FlushUploads();
            return entry;
        }
    }
}