// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Linq;
using Microsoft.Xna.Framework;

namespace Forma.Tests
{
    [TestFixture]
    public sealed class UiMatrixTest
    {
        private static readonly Vector2[] Viewports = { new Vector2(1280, 720), new Vector2(1920, 1080) };

        // A label a quarter of the logical width wide: it fits the short text everywhere and the long text only while the logical
        // viewport is wide, so it breaks in exactly one language at one scale.
        private static IReadOnlyList<UiMatrixFailure> Run(UIFontFace face, out int cases)
        {
            var count = 0;
            var views = new[]
            {
                new UiMatrixView("tight label", matrixCase =>
                {
                    var text = matrixCase.Locale == "long" ? "Auto-place Spawn Points now" : "Save";
                    var label = new Label { Text = text, UIFont = new DynamicUIFont(face, 16), Padding = new Thickness(0), Position = new Vector2(4, 4), Size = new Vector2(matrixCase.LogicalViewport.X / 4, 30) };
                    var host = new Control();
                    host.AddChild(label);
                    return host;
                }),
            };

            var failures = UiMatrix.Run(views, new[] { "short", "long" }, new[] { 100, 200 }, Viewports,
                (context, root, matrixCase) =>
                {
                    count++;
                    return UiMatrix.LayoutProblems().Invoke(context, root, matrixCase);
                });
            cases = count;
            return failures;
        }

        [Test]
        public void ABrokenView_FailsInExactlyTheExpectedCombinations_AndNoOther()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");

            var failures = Run(face, out var cases);

            Assert.Multiple(() =>
            {
                Assert.That(cases, Is.EqualTo(1 * 2 * 2 * 2));
                Assert.That(failures.Select(f => f.Case.Locale).Distinct(), Is.EqualTo(new[] { "long" }));
                Assert.That(failures.Select(f => f.Case.ScalePercent).Distinct(), Is.EqualTo(new[] { 200 }));
                Assert.That(failures.Select(f => f.Case.Viewport).OrderBy(v => v.X), Is.EqualTo(Viewports));
                Assert.That(failures[0].ToString(), Does.Contain("tight label").And.Contain("long").And.Contain("200%").And.Contain("TextClipped"));
            });
        }

        [Test]
        public void TheCase_CarriesTheDisplayScaleAndTheLogicalViewport()
        {
            UiMatrixCase seen = null;

            UiMatrix.Run(new[] { new UiMatrixView("v", c => new Control()) }, new[] { "en" }, new[] { 130 }, new[] { new Vector2(1920, 1080) },
                (context, root, matrixCase) => { seen = matrixCase; return System.Array.Empty<string>(); });

            Assert.Multiple(() =>
            {
                Assert.That(seen.DisplayScale, Is.EqualTo(1.5f * 1.3f).Within(0.001f));
                Assert.That(seen.LogicalViewport.X, Is.EqualTo(1920 / seen.DisplayScale).Within(0.01f));
            });
        }

        [Test]
        public void AViewThatFitsEverywhere_ProducesNoFailures()
        {
            var failures = UiMatrix.Run(new[] { new UiMatrixView("empty", c => new Control()) }, new[] { "en", "ja" }, new[] { 70, 100, 130 }, Viewports, UiMatrix.LayoutProblems());

            Assert.That(failures, Is.Empty);
        }
    }
}
