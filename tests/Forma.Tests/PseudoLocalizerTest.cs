// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Forma.Xaml;
using Microsoft.Xna.Framework;

namespace Forma.Tests
{
    [TestFixture]
    public sealed class PseudoLocalizerTest
    {
        [Test]
        public void Transform_IsDeterministic_AndExpandsByTheFactor()
        {
            var first = PseudoLocalizer.Transform("Save before leaving");
            var second = PseudoLocalizer.Transform("Save before leaving");

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.EqualTo(second));
                Assert.That(first.Length, Is.GreaterThanOrEqualTo((int)("Save before leaving".Length * 1.4)));
                Assert.That(first, Does.StartWith("[").And.EndWith("]"));
                Assert.That(first, Does.Contain("Sávé"));
            });
        }

        [TestCase("Players: {0}")]
        [TestCase("{name} has {count} points")]
        [TestCase("No placeholders")]
        public void Transform_KeepsPlaceholdersVerbatim(string text)
        {
            var result = PseudoLocalizer.Transform(text);

            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, @"\{[^}]*\}"))
                Assert.That(result, Does.Contain(match.Value));
        }

        [Test]
        public void Options_TurnPartsOff_AndMirror()
        {
            var plain = PseudoLocalizer.Transform("Save", new PseudoLocaleOptions { Accents = false, Markers = false, ExpansionFactor = 0 });
            var mirrored = PseudoLocalizer.Transform("one two", new PseudoLocaleOptions { Accents = false, Markers = false, ExpansionFactor = 0, RightToLeft = true });

            Assert.Multiple(() =>
            {
                Assert.That(plain, Is.EqualTo("Save"));
                Assert.That(mirrored, Is.EqualTo("\u202Btwo one\u202C"));
            });
        }

        [Test]
        public void Wrap_TransformsWhatTheTranslatorReturns_ForLocalizationApply()
        {
            var label = new Label();
            label.SetData("i18n", "menu.play");

            Localization.Apply(label, PseudoLocalizer.Wrap(key => key == "menu.play" ? "Play" : key));

            Assert.That(label.Text, Does.StartWith("[Plá").And.EndWith("]"));
        }

        [Test]
        public void ATightLabel_FitsInTheSourceLanguage_AndOverflowsUnderThePseudoLocale()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var font = new DynamicUIFont(face, 16);
            var source = new Label { Text = "Save and exit", UIFont = font, Padding = new Thickness(0) };
            var pseudo = new Label { Text = PseudoLocalizer.Transform("Save and exit"), UIFont = font, Padding = new Thickness(0) };
            var box = source.GetMinimumSize().X + 4;

            Assert.Multiple(() =>
            {
                Assert.That(source.GetMinimumSize().X, Is.LessThanOrEqualTo(box));
                Assert.That(pseudo.GetMinimumSize().X, Is.GreaterThan(box));
            });
        }
    }
}
