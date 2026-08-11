// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Reflection;
using Forma;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NUnit.Framework;

namespace Forma.RenderTests
{
    [NonParallelizable]
    [Platform(Exclude = "MacOsX", Reason = "SDL graphics-device creation must run on the macOS main thread; compilation remains validated here.")]
    internal sealed class UIRenderTest : GraphicsDeviceTestFixtureBase
    {
        [Test]
        public void SubViewportContainer_RendersHostedUiIntoAnIsolatedTarget()
        {
            using var parentTarget = new RenderTarget2D(gd, 64, 64, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            using var ui = new UIContext();
            using var viewport = new SubViewportContainer
            {
                Position = new Vector2(8, 8),
                Size = new Vector2(32, 32),
                Stretch = true,
                ViewportClearColor = Color.Transparent
            };
            viewport.ViewportContext.Add(new ColorRect { Size = new Vector2(32, 32), Color = Color.Red });
            ui.Add(viewport);

            gd.SetRenderTarget(parentTarget);
            try
            {
                gd.Clear(Color.Blue);
                ui.Draw(gd);
            }
            finally
            {
                gd.SetRenderTarget(null);
            }
            var pixels = new Color[64 * 64];
            parentTarget.GetData(pixels);

            Assert.That(pixels[16 + 16 * 64], Is.EqualTo(Color.Red));
            Assert.That(pixels[48 + 48 * 64], Is.EqualTo(Color.Blue));
        }

        [Test]
        public void UIContext_DisplayScaleRendersLogicalCoordinatesInPhysicalPixels()
        {
            using var renderTarget = new RenderTarget2D(gd, 64, 64, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            using var ui = new UIContext { DisplayScale = 2f, ViewportSize = new Vector2(32, 32) };
            var root = new ColorRect { Size = new Vector2(20, 20), Color = Color.Red, ClipContents = true };
            root.AddChild(new ColorRect { Position = new Vector2(16, 16), Size = new Vector2(16, 16), Color = Color.Lime });
            ui.Add(root);

            gd.SetRenderTarget(renderTarget);
            gd.Clear(Color.Blue);
            ui.Draw(gd);
            gd.SetRenderTarget(null);
            var pixels = new Color[64 * 64];
            renderTarget.GetData(pixels);

            Assert.That(pixels[30 + 30 * 64], Is.EqualTo(Color.Red));
            Assert.That(pixels[36 + 36 * 64], Is.EqualTo(Color.Lime));
            Assert.That(pixels[50 + 36 * 64], Is.EqualTo(Color.Blue));
        }

        [Test]
        public void UIContext_FractionalDisplayScaleKeepsHairlineBordersVisible()
        {
            using var renderTarget = new RenderTarget2D(gd, 85, 85, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            using var ui = new UIContext { DisplayScale = .85f, ViewportSize = new Vector2(100, 100) };
            ui.Add(new Panel
            {
                Position = new Vector2(3, 3),
                Size = new Vector2(30, 30),
                BackgroundColor = Color.Blue,
                BorderColor = Color.Red,
                BorderWidth = 1
            });
            ui.Add(new Border
            {
                Position = new Vector2(43, 43),
                Size = new Vector2(30, 30),
                BorderBrush = new SolidColorBrush(Color.Lime),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3)
            });

            gd.SetRenderTarget(renderTarget);
            gd.Clear(Color.Blue);
            ui.Draw(gd);
            gd.SetRenderTarget(null);
            var pixels = new Color[85 * 85];
            renderTarget.GetData(pixels);

            Assert.That(pixels[15 + 3 * 85], Is.EqualTo(Color.Red), "The SpriteBatch hairline disappeared at 85% scale.");
            Assert.That(pixels[49 + 37 * 85], Is.EqualTo(Color.Lime), "The rounded vector hairline disappeared at 85% scale.");
        }

        [Test]
        public void UIContext_DisplayFontResolverUsesDensityAtlasAtLogicalSize()
        {
            using var logicalTexture = new Texture2D(gd, 1, 1);
            logicalTexture.SetData(new[] { Color.Red });
            using var densityTexture = new Texture2D(gd, 2, 2);
            densityTexture.SetData(new[] { Color.Lime, Color.Lime, Color.Lime, Color.Lime });
            var logicalFont = CreateSingleGlyphFont(logicalTexture, 1);
            var densityFont = CreateSingleGlyphFont(densityTexture, 2);
            using var renderTarget = new RenderTarget2D(gd, 8, 8, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            using var ui = new UIContext
            {
                DisplayScale = 2f,
                ViewportSize = new Vector2(4, 4),
                DisplayFontResolver = (font, scale) => font == logicalFont && scale == 2f ? densityFont : null
            };
            ui.Add(new Label
            {
                Position = Vector2.One,
                Size = new Vector2(2, 2),
                Padding = new Thickness(0),
                Font = logicalFont,
                FontColor = Color.White,
                Text = "A"
            });

            gd.SetRenderTarget(renderTarget);
            gd.Clear(Color.Blue);
            ui.Draw(gd);
            gd.SetRenderTarget(null);
            var pixels = new Color[8 * 8];
            renderTarget.GetData(pixels);

            Assert.That(pixels[2 + 2 * 8], Is.EqualTo(Color.Lime));
            Assert.That(pixels[3 + 3 * 8], Is.EqualTo(Color.Lime));
            Assert.That(pixels[4 + 2 * 8], Is.EqualTo(Color.Blue));
        }

        [Test]
        public void CatalogDensityFontsProvideDoubleResolutionAtlases()
        {
            var logicalFont = content.Load<SpriteFont>("Fonts/Catalog");
            var densityFont = content.Load<SpriteFont>("Fonts/Catalog@2x");
            var logicalCodeFont = content.Load<SpriteFont>("Fonts/CatalogCode");
            var densityCodeFont = content.Load<SpriteFont>("Fonts/CatalogCode@2x");

            Assert.That(densityFont.LineSpacing, Is.InRange(logicalFont.LineSpacing * 1.9f, logicalFont.LineSpacing * 2.1f));
            Assert.That(densityCodeFont.LineSpacing, Is.InRange(logicalCodeFont.LineSpacing * 1.9f, logicalCodeFont.LineSpacing * 2.1f));
            Assert.That(GetFontTexture(densityFont).Width, Is.GreaterThan(GetFontTexture(logicalFont).Width));
            Assert.That(GetFontTexture(densityFont).Height, Is.GreaterThan(GetFontTexture(logicalFont).Height));
            Assert.That(GetFontTexture(densityCodeFont).Width, Is.GreaterThan(GetFontTexture(logicalCodeFont).Width));
            Assert.That(GetFontTexture(densityCodeFont).Height, Is.GreaterThan(GetFontTexture(logicalCodeFont).Height));
            Assert.That(logicalFont.DefaultCharacter, Is.EqualTo('?'));
            Assert.That(densityFont.DefaultCharacter, Is.EqualTo('?'));
            Assert.That(logicalCodeFont.DefaultCharacter, Is.EqualTo('?'));
            Assert.That(densityCodeFont.DefaultCharacter, Is.EqualTo('?'));
            Assert.DoesNotThrow(() => logicalFont.MeasureString("café 🔎"));
            Assert.DoesNotThrow(() => densityFont.MeasureString("café 🔎"));
            Assert.DoesNotThrow(() => logicalCodeFont.MeasureString("using Forma;"));
            Assert.DoesNotThrow(() => densityCodeFont.MeasureString("using Forma;"));

            using var renderTarget = new RenderTarget2D(gd, 256, 64);
            using var ui = new UIContext
            {
                DisplayScale = 2f,
                ViewportSize = new Vector2(128, 32),
                DisplayFontResolver = (font, scale) => font == logicalFont && scale > 1f ? densityFont : null
            };
            ui.Add(new LineEdit { Font = logicalFont, Text = "café 🔎", Size = new Vector2(128, 32) });

            gd.SetRenderTarget(renderTarget);
            Assert.DoesNotThrow(() => ui.Draw(gd));
            gd.SetRenderTarget(null);
        }

        [Test]
        public void DefaultThemeIcons_LoadLazilySharePerDeviceAndRecreateAfterLastOwnerDisposes()
        {
            var first = new DefaultThemeIconResources(gd);
            var second = new DefaultThemeIconResources(gd);
            Assert.That(first.Diagnostics.AtlasCount, Is.Zero);
            Assert.That(DefaultThemeIconResources.ManifestIconCount, Is.EqualTo(79));

            first.Ensure(1f, ThemeIconRenderingPolicy.BitmapAtlas);
            var firstIcon = first.Theme.GetIcon("arrow", nameof(OptionButton)) ?? throw new AssertionException("Default OptionButton arrow is missing.");
            Assert.That(first.Theme.GetIcon("decrement", nameof(HScrollBar)), Is.Not.Null);
            Assert.That(first.Theme.GetIcon("increment", nameof(HScrollBar)), Is.Not.Null);
            Assert.That(first.Theme.GetIcon("decrement", nameof(VScrollBar)), Is.Not.Null);
            Assert.That(first.Theme.GetIcon("increment", nameof(VScrollBar)), Is.Not.Null);
            Assert.That(first.Diagnostics.ActiveDensity, Is.EqualTo(1));
            Assert.That(first.Diagnostics.AtlasCount, Is.EqualTo(1));
            Assert.That(first.Diagnostics.TextureBytes, Is.GreaterThan(0));

            second.Ensure(1.25f, ThemeIconRenderingPolicy.BitmapAtlas);
            var sharedIcon = second.Theme.GetIcon("arrow", nameof(OptionButton)) ?? throw new AssertionException("Shared OptionButton arrow is missing.");
            Assert.That(sharedIcon.Texture, Is.SameAs(firstIcon.Texture));
            Assert.That(second.Diagnostics.Generation, Is.EqualTo(1));
            second.Ensure(1.25f, ThemeIconRenderingPolicy.BitmapAtlas);
            Assert.That(second.Diagnostics.Generation, Is.EqualTo(1), "Warm cache access must not decode or create another texture.");

            second.Ensure(1.5f, ThemeIconRenderingPolicy.BitmapAtlas);
            var densityIcon = second.Theme.GetIcon("arrow", nameof(OptionButton)) ?? throw new AssertionException("2x OptionButton arrow is missing.");
            Assert.That(densityIcon.Density, Is.EqualTo(2));
            Assert.That(densityIcon.LogicalSize, Is.EqualTo(firstIcon.LogicalSize));
            Assert.That(densityIcon.Texture, Is.Not.SameAs(firstIcon.Texture));
            Assert.That(second.Diagnostics.AtlasCount, Is.EqualTo(2));
            Assert.That(second.Diagnostics.Generation, Is.EqualTo(2));
            densityIcon.Texture.Dispose();
            second.Ensure(2f, ThemeIconRenderingPolicy.BitmapAtlas);
            var recreatedDensityIcon = second.Theme.GetIcon("arrow", nameof(OptionButton)) ?? throw new AssertionException("Recreated 2x OptionButton arrow is missing.");
            Assert.That(recreatedDensityIcon.Texture, Is.Not.SameAs(densityIcon.Texture));
            Assert.That(second.Diagnostics.Generation, Is.EqualTo(3));

            var originalTexture = firstIcon.Texture;
            first.Dispose();
            Assert.That(originalTexture.IsDisposed, Is.False);
            second.Dispose();
            Assert.That(originalTexture.IsDisposed, Is.True);

            using var replacement = new DefaultThemeIconResources(gd);
            replacement.Ensure(1f, ThemeIconRenderingPolicy.BitmapAtlas);
            var replacementIcon = replacement.Theme.GetIcon("arrow", nameof(OptionButton)) ?? throw new AssertionException("Recreated OptionButton arrow is missing.");
            Assert.That(replacementIcon.Texture, Is.Not.SameAs(originalTexture));
            Assert.That(replacementIcon.Texture.IsDisposed, Is.False);
        }

        [Test]
        public void DefaultThemeIcons_IsolateCachesForSeparateGraphicsDevices()
        {
            using var secondGame = new Game();
            _ = new GraphicsDeviceManager(secondGame) { GraphicsProfile = GraphicsProfile.HiDef };
            ((IGraphicsDeviceManager)secondGame.Services.GetService(typeof(IGraphicsDeviceManager))).CreateDevice();
            using var first = new DefaultThemeIconResources(gd);
            using var second = new DefaultThemeIconResources(secondGame.GraphicsDevice);

            first.Ensure(1f, ThemeIconRenderingPolicy.BitmapAtlas);
            second.Ensure(1f, ThemeIconRenderingPolicy.BitmapAtlas);
            var firstIcon = first.Theme.GetIcon("arrow", nameof(OptionButton)) ?? throw new AssertionException("First-device arrow is missing.");
            var secondIcon = second.Theme.GetIcon("arrow", nameof(OptionButton)) ?? throw new AssertionException("Second-device arrow is missing.");

            Assert.That(firstIcon.Texture, Is.Not.SameAs(secondIcon.Texture));
            Assert.That(firstIcon.Texture.GraphicsDevice, Is.SameAs(gd));
            Assert.That(secondIcon.Texture.GraphicsDevice, Is.SameAs(secondGame.GraphicsDevice));
        }

        [Test]
        public void ThemeIconDrawing_DoesNotLeakTextureOrColorStateIntoFollowingPrimitives()
        {
            using var iconTexture = new Texture2D(gd, 1, 1);
            iconTexture.SetData(new[] { Color.Red });
            using var renderTarget = new RenderTarget2D(gd, 8, 4, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            using var renderer = new UIRenderContext(gd, new Theme());
            var icon = new ThemeIcon(iconTexture, new Rectangle(0, 0, 1, 1), new Point(2, 2));

            gd.SetRenderTarget(renderTarget);
            gd.Clear(Color.Blue);
            renderer.Begin();
            renderer.Icon(icon, new Rectangle(0, 0, 2, 2), Color.White);
            renderer.Fill(new Rectangle(4, 0, 2, 2), Color.Lime);
            renderer.End();
            gd.SetRenderTarget(null);
            var pixels = new Color[8 * 4];
            renderTarget.GetData(pixels);

            Assert.That(pixels[0], Is.EqualTo(Color.Red));
            Assert.That(pixels[4], Is.EqualTo(Color.Lime));
            Assert.That(pixels[3], Is.EqualTo(Color.Blue));
        }

        private static SpriteFont CreateSingleGlyphFont(Texture2D texture, int glyphSize)
        {
            return (SpriteFont)Activator.CreateInstance(
                typeof(SpriteFont),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                [
                    texture,
                    new List<Rectangle> { new Rectangle(0, 0, glyphSize, glyphSize) },
                    new List<Rectangle> { new Rectangle(0, 0, glyphSize, glyphSize) },
                    new List<char> { 'A' },
                    glyphSize,
                    0f,
                    new List<Vector3> { new Vector3(0, glyphSize, 0) },
                    null,
                ],
                null);
        }

        private static Texture2D GetFontTexture(SpriteFont font) =>
            (Texture2D)typeof(SpriteFont)
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(field => typeof(Texture2D).IsAssignableFrom(field.FieldType))
                .GetValue(font);

        [Test]
        [Ignore("Requires a drawable desktop graphics context; run from the interactive visual test runner.")]
        public void ClipContents_PreventsChildRenderingOutsideItsParent()
        {
            using var renderTarget = new RenderTarget2D(gd, 32, 32);
            using var ui = new UIContext();
            var root = new Panel
            {
                Size = new Vector2(20, 20),
                BackgroundColor = Color.Red,
                BorderWidth = 0,
                ClipContents = true
            };
            root.AddChild(new ColorRect
            {
                Position = new Vector2(16, 16),
                Size = new Vector2(16, 16),
                Color = Color.Lime
            });
            ui.Add(root);

            gd.SetRenderTarget(renderTarget);
            gd.Clear(Color.Blue);
            ui.Draw(gd);
            gd.SetRenderTarget(null);
            var pixels = new Color[32 * 32];
            renderTarget.GetData(pixels);

            Assert.That(pixels[18 + 18 * 32], Is.EqualTo(Color.Lime));
            Assert.That(pixels[25 + 18 * 32], Is.EqualTo(Color.Blue));
        }
    }
}
