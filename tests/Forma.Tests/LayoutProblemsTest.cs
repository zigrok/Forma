// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Linq;
using Microsoft.Xna.Framework;

namespace Forma.Tests
{
    [TestFixture]
    public sealed class LayoutProblemsTest
    {
        private static UIFontFace Face() => UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");

        private static (UIContext Context, Control Root) Host(Vector2 viewport, params Control[] children)
        {
            var context = new UIContext { ViewportSize = viewport };
            var root = new Control { Size = viewport };
            foreach (var child in children) root.AddChild(child);
            context.Add(root);
            return (context, root);
        }

        private static Label TextLabel(UIFontFace face, string text, Vector2 position, Vector2 size, Action<Label> configure = null)
        {
            var label = new Label { Text = text, UIFont = new DynamicUIFont(face, 16), Padding = new Thickness(0), Position = position, Size = size };
            configure?.Invoke(label);
            return label;
        }

        [Test]
        public void AViewThatFits_HasNoProblems()
        {
            using var face = Face();
            var (context, root) = Host(new Vector2(400, 300), TextLabel(face, "Save", new Vector2(10, 10), new Vector2(200, 40)));
            using var _ = context;

            Assert.That(context.FindLayoutProblems(root), Is.Empty);
        }

        [Test]
        public void ClippedText_IsReported_WithItsNameClassesAndOverflow()
        {
            using var face = Face();
            var label = TextLabel(face, "Auto-place Spawn Points and everything else", new Vector2(10, 10), new Vector2(80, 40));
            label.Name = "Menu";
            label.Classes.Add("trace-text");
            var (context, root) = Host(new Vector2(400, 300), label);
            using var _ = context;

            var problem = context.FindLayoutProblems(root).Single(p => p.Kind == LayoutProblemKind.TextClipped);

            Assert.Multiple(() =>
            {
                Assert.That(problem.Control, Is.SameAs(label));
                Assert.That(problem.Name, Is.EqualTo("Menu"));
                Assert.That(problem.Classes, Is.EqualTo("trace-text"));
                Assert.That(problem.Overflow.X, Is.GreaterThan(0));
                Assert.That(problem.Message, Does.Contain("Menu").And.Contain("Auto-place"));
            });
        }

        [Test]
        public void WrappedLinesTallerThanTheBox_AreTextWrappedPastBox()
        {
            using var face = Face();
            var label = TextLabel(face, "Select an object in the map or the list to edit it.", new Vector2(10, 10), new Vector2(120, 20), l => l.Autowrap = true);
            var (context, root) = Host(new Vector2(400, 300), label);
            using var _ = context;

            var problems = context.FindLayoutProblems(root);

            Assert.That(problems.Select(p => p.Kind), Does.Contain(LayoutProblemKind.TextWrappedPastBox));
        }

        [Test]
        public void AControlPastItsParent_IsChildOutsideParent_AndPastAClippingAncestor_IsClippedByAncestor()
        {
            var inner = new ColorRect { Position = new Vector2(80, 80), Size = new Vector2(50, 50) };
            var plain = new Control { Position = new Vector2(10, 10), Size = new Vector2(100, 100) };
            plain.AddChild(inner);
            var clipped = new ColorRect { Position = new Vector2(80, 0), Size = new Vector2(50, 20) };
            var clipper = new Control { Position = new Vector2(200, 10), Size = new Vector2(100, 100), ClipContents = true };
            clipper.AddChild(clipped);
            var (context, root) = Host(new Vector2(400, 300), plain, clipper);
            using var _ = context;

            var problems = context.FindLayoutProblems(root);

            Assert.Multiple(() =>
            {
                Assert.That(problems.Any(p => p.Kind == LayoutProblemKind.ChildOutsideParent && ReferenceEquals(p.Control, inner)), Is.True);
                Assert.That(problems.Any(p => p.Kind == LayoutProblemKind.ClippedByAncestor && ReferenceEquals(p.Control, clipped)), Is.True);
            });
        }

        [Test]
        public void AControlPastTheViewport_IsOutsideViewport()
        {
            var wide = new ColorRect { Position = new Vector2(300, 10), Size = new Vector2(200, 20) };
            var (context, root) = Host(new Vector2(400, 300), wide);
            using var _ = context;

            var problem = context.FindLayoutProblems(root).Single(p => p.Kind == LayoutProblemKind.OutsideViewport);

            Assert.That(problem.Overflow.X, Is.EqualTo(100).Within(1));
        }

        [Test]
        public void ContentOfAScrollContainer_IsNotReported_ByDefault()
        {
            var scroll = new ScrollContainer { Position = new Vector2(10, 10), Size = new Vector2(100, 100) };
            scroll.AddChild(new ColorRect { Size = new Vector2(400, 400) });
            var (context, root) = Host(new Vector2(400, 300), scroll);
            using var _ = context;

            Assert.That(context.FindLayoutProblems(root), Is.Empty);
        }

        [Test]
        public void TheKindsOption_FiltersWhatIsReported_AndResultsAreStable()
        {
            var wide = new ColorRect { Position = new Vector2(300, 10), Size = new Vector2(200, 20) };
            var (context, root) = Host(new Vector2(400, 300), wide);
            using var _ = context;
            var only = new LayoutProblemOptions { Kinds = LayoutProblemKind.TextClipped };

            Assert.Multiple(() =>
            {
                Assert.That(context.FindLayoutProblems(root, only), Is.Empty);
                Assert.That(context.FindLayoutProblems(root).Select(p => p.ToString()), Is.EqualTo(context.FindLayoutProblems(root).Select(p => p.ToString())));
            });
        }
    }
}
