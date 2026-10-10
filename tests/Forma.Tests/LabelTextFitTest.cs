// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Microsoft.Xna.Framework;

namespace Forma.Tests
{
    [TestFixture]
    public sealed class LabelTextFitTest
    {
        private static Label CreateLabel(UIFontFace face, string text, Vector2 size, Action<Label> configure = null)
        {
            var label = new Label { Text = text, UIFont = new DynamicUIFont(face, 16), Padding = new Thickness(0), Size = size };
            configure?.Invoke(label);
            return label;
        }

        private static UIFontFace Face() => UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");

        [Test]
        public void ShortText_InARoomyBox_Fits()
        {
            using var face = Face();
            var info = CreateLabel(face, "Save", new Vector2(200, 40)).TextFit;

            Assert.Multiple(() =>
            {
                Assert.That(info.Fit, Is.EqualTo(LabelTextFit.Fits));
                Assert.That(info.Measured.X, Is.GreaterThan(0).And.LessThan(info.Available.X));
                Assert.That(info.Overflow, Is.EqualTo(Vector2.Zero));
            });
        }

        [Test]
        public void LongText_WithoutWrapOrTrimming_IsClipped_AndReportsTheOverflow()
        {
            using var face = Face();
            var info = CreateLabel(face, "Auto-place Spawn Points and everything else", new Vector2(80, 40)).TextFit;

            Assert.Multiple(() =>
            {
                Assert.That(info.Fit, Is.EqualTo(LabelTextFit.Clipped));
                Assert.That(info.Overflow.X, Is.GreaterThan(0));
            });
        }

        [Test]
        public void LongText_WithAnEllipsisBehavior_IsEllipsized()
        {
            using var face = Face();
            var info = CreateLabel(face, "Auto-place Spawn Points and everything else", new Vector2(80, 40), l => l.TextOverrunBehavior = LabelTextOverrunBehavior.Ellipsis).TextFit;

            Assert.That(info.Fit, Is.EqualTo(LabelTextFit.Ellipsized));
        }

        [Test]
        public void LongText_WithAutowrap_IsWrapped_WhenTheLinesFit_AndClipped_WhenTheyDoNot()
        {
            using var face = Face();
            const string text = "Select an object in the map or the list to edit it.";

            var roomy = CreateLabel(face, text, new Vector2(120, 200), l => l.Autowrap = true).TextFit;
            var short_ = CreateLabel(face, text, new Vector2(120, 20), l => l.Autowrap = true).TextFit;

            Assert.Multiple(() =>
            {
                Assert.That(roomy.Fit, Is.EqualTo(LabelTextFit.Wrapped));
                Assert.That(short_.Fit, Is.EqualTo(LabelTextFit.Clipped));
                Assert.That(short_.Overflow.Y, Is.GreaterThan(0));
            });
        }

        [Test]
        public void Padding_ReducesTheAvailableArea()
        {
            using var face = Face();
            var info = CreateLabel(face, "Save", new Vector2(100, 40), l => l.Padding = new Thickness(10, 4, 10, 4)).TextFit;

            Assert.That(info.Available, Is.EqualTo(new Vector2(80, 32)));
        }

        [Test]
        public void EmptyText_Fits()
        {
            using var face = Face();

            Assert.That(CreateLabel(face, string.Empty, new Vector2(10, 10)).TextFit.Fit, Is.EqualTo(LabelTextFit.Fits));
        }
    }
}
