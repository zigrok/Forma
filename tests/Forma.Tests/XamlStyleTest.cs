// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using Forma.Xaml;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Forma.Tests
{
    public class XamlStyleTest
    {
        private static readonly XamlProperty<string> TooltipProperty = new XamlProperty<string>(
            nameof(Control.TooltipText), target => ((Control)target).TooltipText, (target, value) => ((Control)target).TooltipText = value);

        [Test]
        public void Resources_UseLexicalLookupAndDynamicRestoration()
        {
            using var context = new UIContext();
            context.Resources["accent"] = "context";
            var root = new Control();
            root.Resources["accent"] = "root";
            var child = new Control { TooltipText = "base" };
            root.AddChild(child);
            DynamicResource.Attach(root, child, TooltipProperty, "accent");
            context.Add(root);

            Assert.That(StaticResource.Resolve<string>(child, "accent"), Is.EqualTo("root"));
            Assert.That(child.TooltipText, Is.EqualTo("root"));
            root.Resources["accent"] = "changed";
            Assert.That(child.TooltipText, Is.EqualTo("changed"));
            root.Resources.Remove("accent");
            Assert.That(child.TooltipText, Is.EqualTo("context"));
            context.Remove(root);
            Assert.That(child.TooltipText, Is.EqualTo("base"));
        }

        [Test]
        public void Resources_FollowLogicalOwnerThroughVisualProjection()
        {
            using var context = new UIContext();
            var root = new Control();
            var owner = new Control();
            var host = new Control();
            var child = new Control { TooltipText = "base" };
            owner.Resources["accent"] = "owner";
            owner.AddChild(child);
            root.AddChild(owner);
            root.AddChild(host);
            DynamicResource.Attach(root, child, TooltipProperty, "accent");
            context.Add(root);

            host.ProjectVisualChild(child);
            Assert.That(child.TooltipText, Is.EqualTo("owner"));
            owner.Resources["accent"] = "changed";
            Assert.That(child.TooltipText, Is.EqualTo("changed"));
        }

        [Test]
        public void StyleEngine_AttachFrom_GivesATemplateScopedViewTheStylesOfASource()
        {
            var source = new Control();
            var merged = new ResourceDictionary();
            merged.Add("Row", CreateStyle("Control.row-label", "styled"));
            source.Resources.MergedDictionaries.Add(merged);
            source.Resources.Add("NotAStyle", 42);

            var root = new Control { Size = new Vector2(300, 200) };
            var items = new ItemsControl
            {
                Size = new Vector2(300, 200),
                ItemTemplate = DataTemplate.Create<string>((context, item) =>
                {
                    var row = new Control();
                    var label = new Control();
                    label.Classes.Add("row-label");
                    row.AddChild(label);
                    StyleEngine.AttachFrom(row, source);
                    return row;
                }),
                ItemsSource = new[] { "a" },
            };
            root.AddChild(items);
            using var context = new UIContext { ViewportSize = new Vector2(300, 200) };
            context.Add(root);
            StyleEngine.Attach(root, new[] { CreateStyle("Control.row-label", "root") });
            context.Layout();

            var label2 = FindClass(items.GetRealizedContainer(0), "row-label");

            Assert.That(label2.TooltipText, Is.EqualTo("styled"));
            Assert.That(FindClass(items.GetRealizedContainer(0), "row-label").TryFindResource("NotAStyle", out var shared), Is.True);
            Assert.That(shared, Is.EqualTo(42));
        }

        private static Control FindClass(Control control, string className)
        {
            if (control.Classes.Contains(className)) return control;
            foreach (var child in control.VisualChildren)
                if (FindClass(child, className) is { } found) return found;
            return null;
        }

        [Test]
        public void Styles_ResolveSpecificityOrderAndLocalPrecedence()
        {
            var button = new Button { Name = "Action", TooltipText = "base" };
            button.Classes.Add("primary");
            var typeStyle = CreateStyle("Button", "type");
            var classStyle = CreateStyle("Button.primary", "class");
            var nameStyle = CreateStyle("#Action", "name");
            StyleEngine.Attach(button, new[] { typeStyle, classStyle, nameStyle });
            Assert.That(button.TooltipText, Is.EqualTo("name"));

            using var local = XamlValues.Set(button, TooltipProperty, XamlValueLayer.Local, "local");
            button.Name = "Other";
            Assert.That(button.TooltipText, Is.EqualTo("local"));
            local.Dispose();
            Assert.That(button.TooltipText, Is.EqualTo("class"));
            button.Classes.Remove("primary");
            Assert.That(button.TooltipText, Is.EqualTo("type"));
        }

        [Test]
        public void Styles_TrackPseudoStatesAndNewChildren()
        {
            using var context = new UIContext { ViewportSize = new Vector2(200, 100) };
            var root = new Control { Size = new Vector2(200, 100) };
            var hoverStyle = CreateStyle("Button:hover", "hover");
            var disabledStyle = CreateStyle("Button:disabled", "disabled");
            StyleEngine.Attach(root, new[] { hoverStyle, disabledStyle });
            context.Add(root);
            var button = new Button { Position = new Vector2(10, 10), Size = new Vector2(80, 30), TooltipText = "base" };
            root.AddChild(button);

            context.Update(new GameTime(), new MouseState(20, 20, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());
            Assert.That(button.TooltipText, Is.EqualTo("hover"));
            button.Enabled = false;
            Assert.That(button.TooltipText, Is.EqualTo("disabled"));
            root.RemoveChild(button);
            Assert.That(button.TooltipText, Is.EqualTo("base"));
        }

        [Test]
        public void Styles_TrackTemplateVisualChildren()
        {
            var control = new TemplateStyleProbe();
            StyleEngine.Attach(control, new[] { CreateStyle("TemplateStyleProbe >> Button", "template") });
            var part = new Button { TooltipText = "base" };

            control.Template = new ControlTemplate(typeof(TemplateStyleProbe), _ => part);
            Assert.That(part.TooltipText, Is.EqualTo("template"));

            control.Template = null;
            Assert.That(part.TooltipText, Is.EqualTo("base"));
        }

        [Test]
        public void Selectors_DoNotCrossTemplateBoundariesWithoutTemplateCombinator()
        {
            var control = new TemplateStyleProbe();
            using var attachment = StyleEngine.Attach(control, new[] { CreateStyle("Button", "unexpected") });
            var part = new Button { TooltipText = "base" };

            control.Template = new ControlTemplate(typeof(TemplateStyleProbe), _ => part);

            Assert.That(part.TooltipText, Is.EqualTo("base"));
        }

        [Test]
        public void Selectors_RejectMalformedCombinators()
        {
            Assert.Throws<FormatException>(() => StyleSelector.Parse("Panel > > Button"));
        }

        [Test]
        public void Selectors_InvalidateDescendantsWhenAncestorTermsChange()
        {
            var root = new Control();
            var wrapper = new Control();
            var button = new Button { TooltipText = "base" };
            root.AddChild(wrapper);
            wrapper.AddChild(button);
            using var attachment = StyleEngine.Attach(root, new[] { CreateStyle("Control.scope Button", "matched") });

            Assert.That(button.TooltipText, Is.EqualTo("base"));
            root.Classes.Add("scope");
            Assert.That(button.TooltipText, Is.EqualTo("matched"));
            root.Classes.Remove("scope");
            Assert.That(button.TooltipText, Is.EqualTo("base"));
        }

        [Test]
        public void Selectors_FilterUnrelatedAncestorDependencies()
        {
            var root = new Control();
            var button = new Button { TooltipText = "base" };
            var probe = new CountingSelectorProbe();
            root.AddChild(button);
            root.AddChild(probe);
            using var attachment = StyleEngine.Attach(root, new[]
            {
                CreateStyle("Control.scope Button", "matched"),
                CreateStyle("Control.other CountingSelectorProbe:watched", "unrelated"),
            });
            probe.PseudoStateQueryCount = 0;

            root.Classes.Add("scope");

            Assert.Multiple(() =>
            {
                Assert.That(button.TooltipText, Is.EqualTo("matched"));
                Assert.That(probe.PseudoStateQueryCount, Is.Zero);
            });
        }

        [Test]
        public void Selectors_CrossOneControlTemplateBoundaryPerTemplateCombinator()
        {
            var outer = new OuterTemplateProbe();
            var inner = new InnerTemplateProbe();
            var part = new Border { TooltipText = "base" };
            using var attachment = StyleEngine.Attach(outer, new[]
            {
                CreateStyle("OuterTemplateProbe >> InnerTemplateProbe >> Border", "nested"),
            });

            outer.Template = new ControlTemplate(typeof(OuterTemplateProbe), _ => inner);
            inner.Template = new ControlTemplate(typeof(InnerTemplateProbe), _ => part);

            Assert.That(part.TooltipText, Is.EqualTo("nested"));
        }

        [Test]
        public void SelectorListsUseTheMostSpecificMatchingArm()
        {
            var button = new Button { Name = "Action", TooltipText = "base" };
            button.Classes.Add("primary");
            var listStyle = CreateStyle("Button, #Action", "list");
            var classStyle = CreateStyle("Button.primary", "class");
            using var attachment = StyleEngine.Attach(button, new[] { listStyle, classStyle });

            Assert.That(button.TooltipText, Is.EqualTo("list"));
            button.Name = "Other";
            Assert.That(button.TooltipText, Is.EqualTo("class"));
        }

        [Test]
        public void Selectors_UseExtensiblePseudoStateProvider()
        {
            var control = new SelectorStateProbe { TooltipText = "base" };
            using var attachment = StyleEngine.Attach(control, new[] { CreateStyle("SelectorStateProbe:selected", "selected") });

            Assert.That(control.TooltipText, Is.EqualTo("base"));
            control.SetSelected(true);
            Assert.That(control.TooltipText, Is.EqualTo("selected"));
            control.SetSelected(false);
            Assert.That(control.TooltipText, Is.EqualTo("base"));
        }

        [Test]
        public void Selectors_TrackEffectiveDisabledAndFocusWithinStates()
        {
            using var context = new UIContext();
            var root = new Control { Name = "Root", TooltipText = "base" };
            var button = new Button { FocusMode = FocusMode.All, TooltipText = "base" };
            root.AddChild(button);
            using var attachment = StyleEngine.Attach(root, new[]
            {
                CreateStyle("#Root:focus-within", "focused"),
                CreateStyle("Button:disabled", "disabled"),
            });
            context.Add(root);

            context.SetFocus(button);
            Assert.That(root.TooltipText, Is.EqualTo("focused"));
            root.Enabled = false;
            Assert.That(button.TooltipText, Is.EqualTo("disabled"));
            root.Enabled = true;
            Assert.That(button.TooltipText, Is.EqualTo("base"));
            context.SetFocus(null);
            Assert.That(root.TooltipText, Is.EqualTo("base"));
        }

        [Test]
        public void Styles_TrackTypedAdaptiveConditionsFromContextEvents()
        {
            using var context = new UIContext
            {
                ViewportSize = new Vector2(900, 600),
                DisplayScale = 1,
                ThemeVariant = ThemeVariant.Light,
                InputModality = InputModality.Pointer,
            };
            var control = new Button { TooltipText = "base" };
            var style = CreateStyle("Button", "adaptive");
            style.Condition = new AdaptiveCondition
            {
                MaxViewportWidth = 720,
                DisplayScale = 2,
                ThemeVariant = ThemeVariant.Dark,
                InputModality = InputModality.Touch,
            };
            using var attachment = StyleEngine.Attach(control, new[] { style });
            context.Add(control);

            Assert.That(control.TooltipText, Is.EqualTo("base"));
            context.ViewportSize = new Vector2(720, 600);
            context.DisplayScale = 2;
            context.ThemeVariant = ThemeVariant.Dark;
            context.InputModality = InputModality.Touch;
            Assert.That(control.TooltipText, Is.EqualTo("adaptive"));
            context.ViewportSize = new Vector2(721, 600);
            Assert.That(control.TooltipText, Is.EqualTo("base"));
        }

        [Test]
        public void Selectors_MatchDirectVisualChildrenAndCompoundNegation()
        {
            var root = new Control();
            root.Classes.Add("scope");
            var direct = new Button { TooltipText = "base" };
            var excluded = new Button { TooltipText = "base" };
            excluded.Classes.Add("overflow");
            var wrapper = new Control();
            var nested = new Button { TooltipText = "base" };
            root.AddChild(direct);
            root.AddChild(excluded);
            root.AddChild(wrapper);
            wrapper.AddChild(nested);

            using var attachment = StyleEngine.Attach(root, new[] { CreateStyle("Control.scope > Button:not(.overflow)", "matched") });

            Assert.Multiple(() =>
            {
                Assert.That(direct.TooltipText, Is.EqualTo("matched"));
                Assert.That(excluded.TooltipText, Is.EqualTo("base"));
                Assert.That(nested.TooltipText, Is.EqualTo("base"));
            });
        }

        private static Style CreateStyle(string selector, string value)
        {
            var style = new Style(selector);
            style.Setters.Add(new StyleSetter<string>(TooltipProperty, value));
            return style;
        }

        private sealed class TemplateStyleProbe : TemplatedControl { }
        private sealed class OuterTemplateProbe : TemplatedControl { }
        private sealed class InnerTemplateProbe : TemplatedControl { }
        private sealed class CountingSelectorProbe : Control
        {
            public int PseudoStateQueryCount { get; set; }
            public override bool IsPseudoStateActive(string state)
            {
                PseudoStateQueryCount++;
                return base.IsPseudoStateActive(state);
            }
        }
        private sealed class SelectorStateProbe : Control
        {
            public void SetSelected(bool selected) => SetPseudoState("selected", selected);
        }
    }
}