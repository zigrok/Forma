// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.ComponentModel;
using Forma.Xaml;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Forma.Tests
{
    public class XamlRuntimeTest
    {
        [Test]
        public void Control_ImplementsContentAndInheritedDataContextContracts()
        {
            var root = new Control();
            var child = new Control();
            var grandchild = new Control();
            ((IAddChild<Control>)root).AddChild(child);
            child.AddChild(grandchild);

            var inheritedChanges = 0;
            grandchild.DataContextChanged += (_, _) => inheritedChanges++;
            var first = new object();
            root.DataContext = first;

            Assert.That(child.DataContext, Is.SameAs(first));
            Assert.That(grandchild.DataContext, Is.SameAs(first));
            Assert.That(inheritedChanges, Is.EqualTo(1));

            var local = new object();
            child.DataContext = local;
            root.DataContext = new object();
            Assert.That(grandchild.DataContext, Is.SameAs(local));

            child.ClearDataContext();
            Assert.That(child.DataContext, Is.SameAs(root.DataContext));
            Assert.That(grandchild.DataContext, Is.SameAs(root.DataContext));
        }

        [Test]
        public void Control_AttachDetachEventsFollowContextTransitions()
        {
            using var first = new UIContext();
            using var second = new UIContext();
            var root = new Control();
            var child = new Control();
            root.AddChild(child);
            var attached = 0;
            var detached = 0;
            child.Attached += (_, _) => attached++;
            child.Detached += (_, _) => detached++;

            first.Add(root);
            first.Remove(root);
            second.Add(root);

            Assert.That(attached, Is.EqualTo(2));
            Assert.That(detached, Is.EqualTo(1));
            Assert.That(child.Context, Is.SameAs(second));
        }

        [Test]
        public void ResourcesClassesAndNamescopeUseDeterministicLookup()
        {
            var merged = new ResourceDictionary { ["accent"] = "merged" };
            var root = new Control();
            root.Resources.MergedDictionaries.Add(merged);
            root.Resources["local"] = 42;
            var child = new Control();
            root.AddChild(child);

            Assert.That(child.TryFindResource("accent", out var accent), Is.True);
            Assert.That(accent, Is.EqualTo("merged"));
            Assert.That(child.TryFindResource("local", out var local), Is.True);
            Assert.That(local, Is.EqualTo(42));

            var classChanges = 0;
            child.Classes.Changed += (_, _) => classChanges++;
            child.Classes.Set("primary primary compact");
            Assert.That(child.Classes, Is.EquivalentTo(new[] { "primary", "compact" }));
            Assert.That(classChanges, Is.EqualTo(1));

            var scope = new NameScope();
            scope.Register("Child", child);
            NameScope.SetNameScope(root, scope);
            Assert.That(root.FindName<Control>("Child"), Is.SameAs(child));
            Assert.Throws<InvalidOperationException>(() => scope.Register("Child", new Control()));
        }

        [Test]
        public void FindControlByOrdinal_SkipsScrollContainerInfrastructure()
        {
            var root = new Container();
            var scroll = new ScrollContainer();
            var content = new VBoxContainer();
            var label = new Label();
            content.AddChild(label);
            scroll.AddChild(content);
            root.AddChild(scroll);

            Assert.That(NameScope.FindControlByOrdinal(root, 0), Is.SameAs(root));
            Assert.That(NameScope.FindControlByOrdinal(root, 1), Is.SameAs(scroll));
            Assert.That(NameScope.FindControlByOrdinal(root, 2), Is.SameAs(content));
            Assert.That(NameScope.FindControlByOrdinal(root, 3), Is.SameAs(label));
        }

        [Test]
        public void FormaXamlLoader_UsesExplicitTypedRegistration()
        {
            FormaXamlLoader.Register<RegisteredView>(
                _ => new RegisteredView { Value = "built" },
                (_, view) => view.Value = "populated");

            Assert.That(FormaXamlLoader.Load<RegisteredView>().Value, Is.EqualTo("built"));
            var existing = new RegisteredView();
            FormaXamlLoader.Load(existing);
            Assert.That(existing.Value, Is.EqualTo("populated"));
        }

        [Test]
        public void XamlValueConverter_HandlesFormaLiteralTypes()
        {
            Assert.That(XamlValueConverter.Convert("Fill, Expand", typeof(SizeFlags)), Is.EqualTo(SizeFlags.Fill | SizeFlags.Expand));
            Assert.That(XamlValueConverter.Convert("#80402010", typeof(Color)), Is.EqualTo(new Color(0x40, 0x20, 0x10, 0x80)));
            Assert.That(XamlValueConverter.Convert("CornflowerBlue", typeof(Color)), Is.EqualTo(new Color(100, 149, 237)));
            Assert.That(XamlValueConverter.Convert("2.5,4", typeof(Vector2)), Is.EqualTo(new Vector2(2.5f, 4f)));
            Assert.That(XamlValueConverter.Convert("1,2,3,4", typeof(Thickness)), Is.EqualTo(new Thickness(1, 2, 3, 4)));
            Assert.That(XamlValueConverter.Convert("1,2,3,4", typeof(CornerRadius)), Is.EqualTo(new CornerRadius(1, 2, 3, 4)));
            Assert.That(((SolidColorBrush)XamlValueConverter.Convert("#80402010", typeof(Brush))).Color, Is.EqualTo(new Color(0x40, 0x20, 0x10, 0x80)));
            Assert.That(XamlValueConverter.Convert("M0 0 H10 V10 Z", typeof(Geometry)), Is.TypeOf<PathGeometry>());
            Assert.That(XamlValueConverter.Convert("0:0:0.35", typeof(TimeSpan)), Is.EqualTo(TimeSpan.FromMilliseconds(350)));
            Assert.That(XamlValueConverter.Convert("", typeof(float?)), Is.Null);
            Assert.That(XamlValueConverter.TryConvert("not-a-color", typeof(Color), out _), Is.False);
        }

        [Test]
        public void ThemeAndStyleBoxesRemainDirectlyDeclarative()
        {
            var theme = new Theme { AccentColor = (Color)XamlValueConverter.Convert("#FF56C596", typeof(Color)) };
            var style = new StyleBoxFlat
            {
                BackgroundColor = (Color)XamlValueConverter.Convert("#E6202630", typeof(Color)),
                ContentMargin = (Thickness)XamlValueConverter.Convert("16", typeof(Thickness)),
            };
            var control = new Control { ThemeOverride = theme };
            control.ThemeStyleOverrides["panel"] = style;

            Assert.That(control.ThemeOverride.AccentColor, Is.EqualTo(new Color(0x56, 0xC5, 0x96, 0xFF)));
            Assert.That(control.GetThemeStyleBox("panel"), Is.SameAs(style));
        }

        [Test]
        public void XamlAttachment_UpdatesWhileAttachedAndDisposesOnDetach()
        {
            using var context = new UIContext();
            var root = new Control();
            var participant = new AttachmentProbe();
            XamlAttachment.RegisterDisposable(root, participant);
            XamlAttachment.RegisterUpdateParticipant(root, participant);

            context.Add(root);
            context.Update(new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)), new MouseState(), new KeyboardState());
            Assert.That(participant.UpdateCount, Is.EqualTo(1));

            context.Remove(root);
            context.Update(new GameTime(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)), new MouseState(), new KeyboardState());
            Assert.That(participant.UpdateCount, Is.EqualTo(1));
            Assert.That(participant.DisposeCount, Is.EqualTo(1));

            context.Add(root);
            context.Update(new GameTime(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1)), new MouseState(), new KeyboardState());
            Assert.That(participant.UpdateCount, Is.EqualTo(1));
            Assert.That(participant.DisposeCount, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => XamlAttachment.RegisterDisposable(root, new AttachmentProbe()));
        }

        [Test]
        public void TemplateXamlAttachment_DeactivatesRebindsAndReactivatesWithoutDisposal()
        {
            using var context = new UIContext();
            var participant = new AttachmentProbe();
            var template = DataTemplate.Create<string>((buildContext, item) =>
            {
                var root = new Control { Name = item };
                XamlAttachment.RegisterDisposable(root, participant);
                XamlAttachment.RegisterUpdateParticipant(root, participant);
                buildContext.RegisterRebind<string>(value => root.Name = value);
                return root;
            });
            var instance = template.CreateInstance("first");

            context.Add(instance.Root);
            instance.Activate();
            context.Update(new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)), new MouseState(), new KeyboardState());
            Assert.That(participant.UpdateCount, Is.EqualTo(1));

            instance.Deactivate();
            context.Remove(instance.Root);
            instance.Rebind("second");
            context.Update(new GameTime(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)), new MouseState(), new KeyboardState());
            Assert.That(participant.UpdateCount, Is.EqualTo(1));
            Assert.That(participant.DisposeCount, Is.Zero);
            Assert.That(instance.Root.Name, Is.EqualTo("second"));

            context.Add(instance.Root);
            instance.Activate();
            context.Update(new GameTime(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1)), new MouseState(), new KeyboardState());
            Assert.That(participant.UpdateCount, Is.EqualTo(2));

            instance.Deactivate();
            context.Remove(instance.Root);
            instance.Dispose();
            instance.Dispose();
            Assert.That(participant.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void TemplateXamlAttachment_ContextDisposalCleansDetachedPooledScope()
        {
            var context = new UIContext();
            var participant = new AttachmentProbe();
            var template = DataTemplate.Create<string>((_, _) =>
            {
                var root = new Control();
                XamlAttachment.RegisterDisposable(root, participant);
                XamlAttachment.RegisterUpdateParticipant(root, participant);
                return root;
            });
            var instance = template.CreateInstance("item");
            context.Add(instance.Root);
            instance.Activate();
            instance.Deactivate();
            context.Remove(instance.Root);

            context.Dispose();

            Assert.That(participant.DisposeCount, Is.EqualTo(1));
            instance.Dispose();
            Assert.That(participant.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void TemplateXamlAttachment_RecreatesSubscriptionsAfterRebind()
        {
            var currentItem = "first";
            var attachedItems = new System.Collections.Generic.List<string>();
            var detachCount = 0;
            var template = DataTemplate.Create<string>((buildContext, _) =>
            {
                var root = new Control();
                XamlAttachment.RegisterReactivatable(root, () =>
                {
                    attachedItems.Add(currentItem);
                    return new CallbackDisposable(() => detachCount++);
                });
                buildContext.RegisterRebind<string>(value => currentItem = value);
                return root;
            });
            var instance = template.CreateInstance("first");

            Assert.That(attachedItems, Is.EqualTo(new[] { "first" }));
            Assert.That(detachCount, Is.EqualTo(1));
            instance.Activate();
            instance.Deactivate();
            instance.Rebind("second");
            instance.Activate();

            Assert.That(attachedItems, Is.EqualTo(new[] { "first", "first", "second" }));
            Assert.That(detachCount, Is.EqualTo(2));
            instance.Dispose();
            Assert.That(detachCount, Is.EqualTo(3));
        }

        [Test]
        public void TemplateXamlAttachment_DisposeContinuesAfterAttachmentFailure()
        {
            var disposed = new System.Collections.Generic.List<string>();
            var template = DataTemplate.Create<string>((_, _) =>
            {
                var root = new Control();
                XamlAttachment.RegisterDisposable(root, new CallbackDisposable(() => disposed.Add("first")));
                XamlAttachment.RegisterDisposable(root, new CallbackDisposable(() =>
                {
                    disposed.Add("throwing");
                    throw new InvalidOperationException("Dispose failed.");
                }));
                XamlAttachment.RegisterDisposable(root, new CallbackDisposable(() => disposed.Add("last")));
                return root;
            });
            var instance = template.CreateInstance("item");

            Assert.Throws<InvalidOperationException>(() => instance.Dispose());

            Assert.That(disposed, Is.EqualTo(new[] { "last", "throwing", "first" }));
            Assert.That(instance.IsDisposed, Is.True);
            Assert.DoesNotThrow(() => instance.Dispose());
        }

        [Test]
        public void TemplateXamlAttachment_CompiledBindingUnsubscribesAndRebindsWithoutGrowth()
        {
            var first = new BindingModel { Value = "first" };
            var second = new BindingModel { Value = "second" };
            var template = DataTemplate.Create<BindingModel>((buildContext, item) =>
            {
                var root = new Label { DataContext = item };
                CompiledBinding.AttachOneWay<BindingModel, string>(
                    root,
                    root,
                    value => value.Value,
                    nameof(BindingModel.Value),
                    target => ((Label)target).Text,
                    (target, value) => ((Label)target).Text = value);
                buildContext.RegisterRebind<BindingModel>(value => root.DataContext = value);
                return root;
            });
            var instance = template.CreateInstance(first);
            var label = (Label)instance.Root;

            instance.Activate();
            Assert.That(label.Text, Is.EqualTo("first"));
            Assert.That(first.SubscriberCount, Is.EqualTo(1));
            instance.Deactivate();
            Assert.That(first.SubscriberCount, Is.Zero);

            instance.Rebind(second);
            instance.Activate();
            Assert.That(label.Text, Is.EqualTo("second"));
            Assert.That(first.SubscriberCount, Is.Zero);
            Assert.That(second.SubscriberCount, Is.EqualTo(1));
            instance.Deactivate();
            instance.Rebind(first);
            instance.Activate();
            Assert.That(first.SubscriberCount, Is.EqualTo(1));
            Assert.That(second.SubscriberCount, Is.Zero);

            instance.Dispose();
            Assert.That(first.SubscriberCount, Is.Zero);
        }

        [Test]
        public void CompiledBinding_FindAncestorRebindsOnVisualReparenting()
        {
            var first = new BindingAncestor { Value = "first" };
            var second = new BindingAncestor { Value = "second" };
            var target = new Label();
            first.AddChild(target);
            var path = new CompiledBindingPath<BindingAncestor, string>(
                source => BindingValue<string>.FromValue(source.Value),
                (source, update) => BindingSubscriptions.PropertyChanged(source, nameof(BindingAncestor.Value), update));
            var property = new XamlProperty<string>(nameof(Label.Text), value => ((Label)value).Text, (value, text) => ((Label)value).Text = text);
            using var binding = CompiledBinding.Attach(
                target,
                target,
                control => CompiledBindingSource.FindAncestor<BindingAncestor>(control),
                path,
                new BindingTargetAdapter<string>(property),
                value => value);

            Assert.That(target.Text, Is.EqualTo("first"));
            second.AddChild(target);
            Assert.That(target.Text, Is.EqualTo("second"));
            first.Value = "old-source";
            Assert.That(target.Text, Is.EqualTo("second"));
            second.Value = "new-source";
            Assert.That(target.Text, Is.EqualTo("new-source"));
        }

        [Test]
        public void TemplateXamlAttachment_ContextDisposalDisposesEntirePooledInstance()
        {
            var context = new UIContext();
            var root = new DisposableControl();
            var template = DataTemplate.Create<string>((_, _) => root);
            var instance = template.CreateInstance("item");
            context.Add(root);
            instance.Activate();
            instance.Deactivate();
            context.Remove(root);

            context.Dispose();

            Assert.That(instance.IsDisposed, Is.True);
            Assert.That(root.IsDisposed, Is.True);
            Assert.That(NameScope.GetNameScope(root), Is.Null);
        }

        [Test]
        public void TemplateXamlAttachment_ContextDisposalDisposesActiveInstance()
        {
            var context = new UIContext();
            var root = new DisposableControl();
            var template = DataTemplate.Create<string>((_, _) => root);
            var instance = template.CreateInstance("item");
            context.Add(root);
            instance.Activate();

            context.Dispose();

            Assert.That(instance.IsDisposed, Is.True);
            Assert.That(root.IsDisposed, Is.True);
        }

        [Test]
        public void TemplatedControl_DisposeDestroysDetachedOwnedTemplate()
        {
            var root = new DisposableControl();
            var control = new TemplateProbe
            {
                Template = ControlTemplate.Create<TemplateProbe>((_, _) => root),
            };

            control.Dispose();
            control.Dispose();

            Assert.That(control.TemplateRoot, Is.Null);
            Assert.That(root.IsDisposed, Is.True);
            Assert.That(root.VisualParent, Is.Null);
            Assert.Throws<ObjectDisposedException>(() => control.ApplyTemplate());
        }

        [Test]
        public void FrameworkTemplate_RejectsReusedMutableRootsAcrossAllTemplateKinds()
        {
            var dataRoot = new Control();
            var dataTemplate = DataTemplate.Create<string>((_, _) => dataRoot);
            using var dataInstance = dataTemplate.CreateInstance("first");
            Assert.Throws<InvalidOperationException>(() => dataTemplate.CreateInstance("second"));

            var owner = new TemplateProbe();
            var controlRoot = new Control();
            var controlTemplate = ControlTemplate.Create<TemplateProbe>((_, _) => controlRoot);
            owner.Template = controlTemplate;
            owner.Template = null;
            Assert.Throws<InvalidOperationException>(() => owner.Template = controlTemplate);
            owner.Dispose();
        }

        [Test]
        public void FrameworkTemplate_FailedBuildContinuesAfterCleanupFailure()
        {
            var scopeAttachment = new AttachmentProbe();
            var root = new DisposableControl { Name = "Duplicate" };
            root.AddChild(new Control { Name = "Duplicate" });
            var template = DataTemplate.Create<string>((buildContext, _) =>
            {
                buildContext.RegisterAttachment(new CallbackDisposable(() => throw new InvalidOperationException("Cleanup failed.")));
                XamlAttachment.RegisterDisposable(root, scopeAttachment);
                return root;
            });

            Assert.Throws<InvalidOperationException>(() => template.CreateInstance("item"));

            Assert.That(scopeAttachment.DisposeCount, Is.EqualTo(1));
            Assert.That(root.IsDisposed, Is.True);
        }

        [Test]
        public void FrameworkTemplate_FactoryThrowBeforeReturnDisposesTrackedRootScope()
        {
            var participant = new AttachmentProbe();
            var root = new DisposableControl();
            var template = DataTemplate.Create<string>((_, _) =>
            {
                XamlAttachment.RegisterDisposable(root, participant);
                throw new InvalidOperationException("Factory failed.");
            });

            Assert.Throws<InvalidOperationException>(() => template.CreateInstance("item"));

            Assert.That(participant.DisposeCount, Is.EqualTo(1));
            Assert.That(root.IsDisposed, Is.True);
        }

        [Test]
        public void FrameworkTemplate_FactoryThrowBeforeReturnDisposesNestedSemanticControl()
        {
            var root = new Control();
            var nested = new DisposableTemplateProbe();
            root.AddChild(nested);
            var template = DataTemplate.Create<string>((_, _) =>
            {
                XamlAttachment.RegisterDisposable(root, new AttachmentProbe());
                throw new InvalidOperationException("Factory failed.");
            });

            Assert.Throws<InvalidOperationException>(() => template.CreateInstance("item"));

            Assert.That(nested.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void XamlAttachment_ThrowingDetachedCallbackCannotSkipOrdinaryScopeDisposal()
        {
            using var context = new UIContext();
            var root = new Control();
            var participant = new AttachmentProbe();
            var childParticipant = new AttachmentProbe();
            var child = new Control();
            root.AddChild(child);
            XamlAttachment.RegisterDisposable(root, participant);
            XamlAttachment.RegisterDisposable(child, childParticipant);
            root.Detached += (_, _) => throw new InvalidOperationException("Detached failed.");
            context.Add(root);

            Assert.Throws<InvalidOperationException>(() => context.Remove(root));

            Assert.That(participant.DisposeCount, Is.EqualTo(1));
            Assert.That(childParticipant.DisposeCount, Is.EqualTo(1));
            Assert.That(child.Context, Is.Null);
        }

        [Test]
        public void TemplatedControl_ContextDisposalDisposesSemanticOwner()
        {
            var context = new UIContext();
            var root = new DisposableControl();
            var control = new TemplateProbe
            {
                Template = ControlTemplate.Create<TemplateProbe>((_, _) => root),
            };
            context.Add(control);

            context.Dispose();

            Assert.That(control.TemplateRoot, Is.Null);
            Assert.That(root.IsDisposed, Is.True);
            Assert.Throws<ObjectDisposedException>(() => control.ApplyTemplate());
        }

        [Test]
        public void TemplateInstance_FailedRebindDisposesPartialInstance()
        {
            var firstCallbackValue = "first";
            var root = new DisposableControl();
            var template = DataTemplate.Create<string>((buildContext, _) =>
            {
                buildContext.RegisterRebind<string>(value => firstCallbackValue = value);
                buildContext.RegisterRebind<string>(_ => throw new InvalidOperationException("Rebind failed."));
                return root;
            });
            var instance = template.CreateInstance("first");

            Assert.Throws<InvalidOperationException>(() => instance.Rebind("second"));

            Assert.That(firstCallbackValue, Is.EqualTo("second"));
            Assert.That(instance.IsDisposed, Is.True);
            Assert.That(root.IsDisposed, Is.True);
            Assert.Throws<ObjectDisposedException>(() => instance.Activate());
        }

        [Test]
        public void TemplateInstance_PartialActivationFailureRollsBackFailingRegistration()
        {
            var acquired = false;
            var template = DataTemplate.Create<string>((buildContext, _) =>
            {
                buildContext.RegisterLifecycle(
                    () =>
                    {
                        acquired = true;
                        throw new InvalidOperationException("Activation failed.");
                    },
                    () => acquired = false);
                return new Control();
            });
            using var instance = template.CreateInstance("item");

            Assert.Throws<InvalidOperationException>(() => instance.Activate());

            Assert.That(acquired, Is.False);
            Assert.That(instance.State, Is.EqualTo(TemplateInstanceState.Inactive));
        }

        [Test]
        public void FrameworkTemplate_FailedNamescopeBuildDisposesXamlScope()
        {
            var participant = new AttachmentProbe();
            var template = DataTemplate.Create<string>((_, _) =>
            {
                var root = new Control { Name = "Duplicate" };
                root.AddChild(new Control { Name = "Duplicate" });
                XamlAttachment.RegisterDisposable(root, participant);
                return root;
            });

            Assert.Throws<InvalidOperationException>(() => template.CreateInstance("item"));

            Assert.That(participant.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void TemplatedControl_PreviousCleanupFailureKeepsNewTemplateCommitted()
        {
            var control = new TemplateProbe();
            var firstRoot = new DisposableControl();
            var secondRoot = new DisposableControl();
            control.Template = ControlTemplate.Create<TemplateProbe>((buildContext, _) =>
            {
                buildContext.RegisterAttachment(new CallbackDisposable(() => throw new InvalidOperationException("Dispose failed.")));
                return firstRoot;
            });
            var secondTemplate = ControlTemplate.Create<TemplateProbe>((_, _) => secondRoot);

            Assert.Throws<InvalidOperationException>(() => control.Template = secondTemplate);

            Assert.That(control.Template, Is.SameAs(secondTemplate));
            Assert.That(control.TemplateRoot, Is.SameAs(secondRoot));
            Assert.That(secondRoot.VisualParent, Is.SameAs(control));
            Assert.That(secondRoot.IsDisposed, Is.False);
            Assert.That(firstRoot.IsDisposed, Is.True);
            control.Template = null;
        }

        [Test]
        public void TemplatedControl_ReplacesOwnedRootAndLocalNamescope()
        {
            using var context = new UIContext();
            var model = new object();
            var control = new TemplateProbe { DataContext = model, FontSize = 18 };
            control.Resources["Accent"] = Color.CornflowerBlue;
            context.Add(control);
            control.Size = new Vector2(120, 40);
            var firstRoot = new DisposableControl { Name = "FirstRoot", MouseFilter = MouseFilter.Ignore };
            var firstPart = new Control { Name = "Part", Size = new Vector2(120, 40), FocusMode = FocusMode.All };
            firstRoot.AddChild(firstPart);
            var parentChanges = new List<ControlParentChangedEventArgs>();
            firstRoot.ParentChanged += (_, args) => parentChanges.Add(args);
            control.Template = new ControlTemplate(typeof(TemplateProbe), _ => firstRoot);

            Assert.That(control.TemplateRoot, Is.SameAs(firstRoot));
            Assert.That(control.GetTemplateChild("Part"), Is.SameAs(firstRoot.Children[0]));
            Assert.That(control.AppliedCount, Is.EqualTo(1));
            Assert.That(control.Children, Is.Empty);
            Assert.That(firstRoot.Parent, Is.Null);
            Assert.That(firstRoot.VisualParent, Is.SameAs(control));
            Assert.That(firstRoot.InheritanceParent, Is.SameAs(control));
            Assert.That(firstRoot.DataContext, Is.SameAs(model));
            Assert.That(firstRoot.FontSize, Is.EqualTo(18));
            Assert.That(firstRoot.TryFindResource("Accent", out var accent), Is.True);
            Assert.That(accent, Is.EqualTo(Color.CornflowerBlue));
            Assert.That(firstRoot.Context, Is.SameAs(context));
            context.Layout();
            Assert.That(firstRoot.Size, Is.EqualTo(control.Size));
            Assert.That(context.HitTest(new Point(10, 10)), Is.SameAs(firstPart));
            context.Update(new GameTime(), new MouseState(), new KeyboardState(Keys.Tab));
            Assert.That(context.FocusedControl, Is.SameAs(firstPart));
            Assert.That(parentChanges, Has.Count.EqualTo(1));
            Assert.That(parentChanges[0].VisualParent, Is.SameAs(control));
            Assert.That(parentChanges[0].InheritanceParent, Is.SameAs(control));

            var secondRoot = new DisposableControl { Name = "SecondRoot" };
            control.Template = new ControlTemplate(typeof(TemplateProbe), _ => secondRoot);

            Assert.That(firstRoot.IsDisposed, Is.True);
            Assert.That(firstRoot.Parent, Is.Null);
            Assert.That(firstRoot.VisualParent, Is.Null);
            Assert.That(firstRoot.InheritanceParent, Is.Null);
            Assert.That(parentChanges, Has.Count.EqualTo(2));
            Assert.That(parentChanges[1].PreviousVisualParent, Is.SameAs(control));
            Assert.That(parentChanges[1].VisualParent, Is.Null);
            Assert.That(control.TemplateRoot, Is.SameAs(secondRoot));
            Assert.That(control.Children, Is.Empty);
            Assert.That(control.AppliedCount, Is.EqualTo(2));

            control.Template = null;
            Assert.That(secondRoot.IsDisposed, Is.True);
            Assert.That(control.TemplateRoot, Is.Null);
            Assert.That(control.Children, Is.Empty);
        }

        [Test]
        public void ControlTemplate_RejectsFoundationTargetsAndRecursiveRoots()
        {
            Assert.Throws<ArgumentException>(() => new ControlTemplate(typeof(Control), _ => new Control()));
            var control = new TemplateProbe();
            var valid = new ControlTemplate(typeof(TemplateProbe), _ => new Control());
            control.Template = valid;
            var validRoot = control.TemplateRoot;
            Assert.Throws<InvalidOperationException>(() =>
                control.Template = new ControlTemplate(typeof(TemplateProbe), _ => control));
            Assert.That(control.Template, Is.SameAs(valid));
            Assert.That(control.TemplateRoot, Is.SameAs(validRoot));
        }

        [Test]
        public void ControlTemplate_RejectsSameSemanticRootAndAcceptsDeliberateNestedWidget()
        {
            var owner = new TemplateProbe();
            var invalidRoot = new TemplateProbe();
            var disposedAttachments = 0;
            var error = Assert.Throws<InvalidOperationException>(() =>
                owner.Template = ControlTemplate.Create<TemplateProbe>((context, _) =>
                {
                    context.RegisterAttachment(new CallbackDisposable(() => disposedAttachments++));
                    return invalidRoot;
                }));
            Assert.That(error.Message, Does.Contain(typeof(TemplateProbe).FullName));
            Assert.That(disposedAttachments, Is.EqualTo(1));

            var nestedRoot = new NestedTemplateProbe();
            owner.Template = ControlTemplate.Create<TemplateProbe>((_, _) => nestedRoot);

            Assert.That(owner.TemplateRoot, Is.SameAs(nestedRoot));
            owner.Dispose();
        }

        [Test]
        public void ControlTemplate_AllowsBaseTargetAcrossDistinctDerivedWidgets()
        {
            var owner = new DerivedTemplateProbe();
            var nestedRoot = new SiblingTemplateProbe();
            owner.Template = new ControlTemplate(typeof(TemplateProbe), _ => nestedRoot);

            Assert.That(owner.TemplateRoot, Is.SameAs(nestedRoot));
            owner.Dispose();
        }

        [Test]
        public void ControlTemplate_RejectsSameSemanticWidgetBelowFoundationalRoot()
        {
            var owner = new TemplateProbe();
            var root = new Control();
            root.AddChild(new TemplateProbe());

            var error = Assert.Throws<InvalidOperationException>(() =>
                owner.Template = ControlTemplate.Create<TemplateProbe>((_, _) => root));

            Assert.That(error.Message, Does.Contain(typeof(TemplateProbe).FullName));
            owner.Template = ControlTemplate.Create<TemplateProbe>((_, _) => new Control());
            Assert.That(owner.TemplateRoot, Is.Not.Null);
            owner.Dispose();
        }

        [Test]
        public void ControlTemplate_AllowsSameSemanticWidgetWithTerminatingTemplate()
        {
            var terminatingTemplate = ControlTemplate.Create<TemplateProbe>((_, _) => new Control());
            var nested = new DisposableTemplateProbe { Template = terminatingTemplate };
            var owner = new TemplateProbe
            {
                Template = ControlTemplate.Create<TemplateProbe>((_, _) =>
                {
                    var root = new Control();
                    root.AddChild(nested);
                    return root;
                }),
            };

            Assert.That(owner.TemplateRoot.VisualChildren, Does.Contain(nested));
            owner.Dispose();
            Assert.That(nested.IsDisposed, Is.True);
        }

        [Test]
        public void ControlTemplate_DoesNotDisposeProjectedSemanticContent()
        {
            var logicalOwner = new Control();
            var projected = new DisposableTemplateProbe();
            logicalOwner.AddChild(projected);
            var owner = new NestedTemplateProbe
            {
                Template = ControlTemplate.Create<NestedTemplateProbe>((_, _) =>
                {
                    var root = new Control();
                    root.ProjectVisualChild(projected);
                    return root;
                }),
            };

            owner.Dispose();

            Assert.That(projected.IsDisposed, Is.False);
            Assert.That(projected.Parent, Is.SameAs(logicalOwner));
        }

        [Test]
        public void ControlTemplate_ProjectedSameTypeContentDoesNotEnterTemplateGraphOrNamescope()
        {
            var logicalOwner = new Control();
            var projected = new TemplateProbe { Name = "Shared" };
            logicalOwner.AddChild(projected);
            var owner = new TemplateProbe
            {
                Template = ControlTemplate.Create<TemplateProbe>((_, _) =>
                {
                    var root = new Control { Name = "Shared" };
                    root.ProjectVisualChild(projected);
                    return root;
                }),
            };

            Assert.That(owner.TemplateRoot, Is.Not.Null);
            Assert.That(owner.GetTemplateChild("Shared"), Is.SameAs(owner.TemplateRoot));
            owner.Dispose();
        }

        [Test]
        public void ControlTemplate_NestedWidgetRetainsPrivateNamescopeAndDisposesOnce()
        {
            var nested = new DisposableTemplateProbe
            {
                Name = "Nested",
                Template = ControlTemplate.Create<TemplateProbe>((_, _) => new Control { Name = "Shared" }),
            };
            var owner = new NestedTemplateProbe
            {
                Template = ControlTemplate.Create<NestedTemplateProbe>((_, _) =>
                {
                    var root = new Control { Name = "Shared" };
                    root.AddChild(nested);
                    return root;
                }),
            };

            Assert.That(owner.GetTemplateChild("Shared"), Is.SameAs(owner.TemplateRoot));
            Assert.That(nested.GetTemplateChild("Shared"), Is.SameAs(nested.TemplateRoot));
            owner.Dispose();
            Assert.That(nested.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void ControlTemplate_SemanticRootRetainsPrivateNamescope()
        {
            var nestedTemplate = ControlTemplate.Create<TemplateProbe>((_, _) => new Control { Name = "Shared" });
            var nestedRoot = new TemplateProbe { Name = "Shared", Template = nestedTemplate };
            var owner = new NestedTemplateProbe
            {
                Template = ControlTemplate.Create<NestedTemplateProbe>((_, _) => nestedRoot),
            };

            Assert.That(owner.GetTemplateChild("Shared"), Is.SameAs(nestedRoot));
            Assert.That(nestedRoot.GetTemplateChild("Shared"), Is.SameAs(nestedRoot.TemplateRoot));
            owner.Dispose();
        }

        [Test]
        public void ControlTemplate_RejectsDelayedReuseBelowAttachedAncestor()
        {
            var template = new ControlTemplate(typeof(TemplatedControl), _ => new Control());
            var owner = new NestedTemplateProbe { Template = template };
            var descendant = new TemplateProbe();
            owner.TemplateRoot.AddChild(descendant);

            var error = Assert.Throws<InvalidOperationException>(() => descendant.Template = template);

            Assert.That(error.Message, Does.Contain("ancestor using the same template"));
            owner.Dispose();
        }

        [Test]
        public void ControlTemplate_RejectsRecursiveReentryThroughDataTemplate()
        {
            ControlTemplate template = null;
            var dataTemplate = DataTemplate.Create<string>((_, _) => new TemplateProbe { Template = template });
            template = ControlTemplate.Create<TemplateProbe>((_, _) => dataTemplate.CreateInstance("item").Root);
            var owner = new TemplateProbe();

            var error = Assert.Throws<InvalidOperationException>(() => owner.Template = template);

            Assert.That(error.Message, Does.Contain("Recursive ControlTemplate"));
            Assert.That(owner.TemplateRoot, Is.Null);
        }

        [Test]
        public void ControlTemplate_RejectsIndirectRecursiveTemplateGraph()
        {
            ControlTemplate firstTemplate = null;
            ControlTemplate secondTemplate = null;
            firstTemplate = ControlTemplate.Create<TemplateProbe>((_, _) =>
                new NestedTemplateProbe { Template = secondTemplate });
            secondTemplate = ControlTemplate.Create<NestedTemplateProbe>((_, _) =>
                new TemplateProbe { Template = firstTemplate });
            var owner = new TemplateProbe();

            var error = Assert.Throws<InvalidOperationException>(() => owner.Template = firstTemplate);

            Assert.That(error.Message, Does.Contain("Recursive ControlTemplate"));
            Assert.That(owner.TemplateRoot, Is.Null);
        }

        [Test]
        public void Control_ProjectsLogicalChildWithoutChangingOwnershipOrInheritance()
        {
            using var context = new UIContext { ViewportSize = new Vector2(100, 60) };
            var model = new object();
            var root = new Control { Size = new Vector2(100, 60) };
            var owner = new Control { DataContext = model, Enabled = false };
            owner.Resources["OwnerResource"] = "inherited";
            var host = new Control { Position = new Vector2(40, 10), Size = new Vector2(30, 20), MouseFilter = MouseFilter.Ignore };
            var child = new Control { Position = new Vector2(3, 4), Size = new Vector2(10, 8) };
            owner.AddChild(child);
            root.AddChild(owner);
            root.AddChild(host);
            context.Add(root);

            host.ProjectVisualChild(child);

            Assert.That(child.Parent, Is.SameAs(owner));
            Assert.That(owner.Children, Does.Contain(child));
            Assert.That(child.VisualParent, Is.SameAs(host));
            Assert.That(child.InheritanceParent, Is.SameAs(owner));
            Assert.That(child.DataContext, Is.SameAs(model));
            Assert.That(child.IsEffectivelyEnabled, Is.False);
            Assert.That(child.TryFindResource("OwnerResource", out var resource), Is.True);
            Assert.That(resource, Is.EqualTo("inherited"));
            Assert.That(child.GlobalPosition, Is.EqualTo(new Vector2(43, 14)));

            owner.Enabled = true;
            Assert.That(context.HitTest(new Point(45, 16)), Is.SameAs(child));

            Assert.That(host.RemoveVisualChild(child), Is.True);
            Assert.That(child.Parent, Is.SameAs(owner));
            Assert.That(child.VisualParent, Is.Null);
            Assert.That(child.InheritanceParent, Is.Null);
            owner.ProjectVisualChild(child);
            Assert.That(child.VisualParent, Is.SameAs(owner));
            Assert.That(child.InheritanceParent, Is.SameAs(owner));
        }

        [Test]
        public void Control_ProjectedChildReceivesOwnerAncestryInvalidationAndHostLayout()
        {
            using var context = new UIContext { ViewportSize = new Vector2(100, 60) };
            var root = new Control { Size = new Vector2(100, 60) };
            var owner = new Control { LayoutDirection = LayoutDirection.RightToLeft };
            var host = new StackPanel { Position = new Vector2(20, 10), Size = new Vector2(50, 30) };
            var child = new Control { CustomMinimumSize = new Vector2(12, 8) };
            var ancestryInvalidations = 0;
            child.ParentChanged += (_, args) => { if (args.IsAncestryInvalidation) ancestryInvalidations++; };
            owner.AddChild(child);
            root.AddChild(owner);
            root.AddChild(host);
            context.Add(root);

            host.ProjectVisualChild(child);
            context.Layout();
            Assert.That(child.Size.Y, Is.EqualTo(8));
            Assert.That(child.VisualParent, Is.SameAs(host));
            Assert.That(child.EffectiveLayoutDirection, Is.EqualTo(LayoutDirection.RightToLeft));

            var outer = new Control();
            outer.AddChild(owner);
            Assert.That(ancestryInvalidations, Is.GreaterThan(0));
            Assert.That(child.InheritanceParent, Is.SameAs(owner));
        }

        [Test]
        public void TemplatedControl_FailedReplacementPreservesPreviousInstance()
        {
            var control = new FailingTemplateProbe();
            var firstRoot = new DisposableControl();
            control.Template = new ControlTemplate(typeof(FailingTemplateProbe), _ => firstRoot);
            control.FailNextApply = true;
            var failedRoot = new DisposableControl();

            Assert.Throws<InvalidOperationException>(() =>
                control.Template = new ControlTemplate(typeof(FailingTemplateProbe), _ => failedRoot));

            Assert.That(control.TemplateRoot, Is.SameAs(firstRoot));
            Assert.That(firstRoot.IsDisposed, Is.False);
            Assert.That(firstRoot.VisualParent, Is.SameAs(control));
            Assert.That(failedRoot.IsDisposed, Is.True);
            Assert.That(failedRoot.VisualParent, Is.Null);
        }

        [Test]
        public void Control_ExposesOnlyRequiredStateAndTreeTransitions()
        {
            var parent = new Control();
            var child = new Control();
            var enabledChanges = 0;
            var added = 0;
            var removed = 0;
            child.EnabledChanged += (_, _) => enabledChanges++;
            parent.ChildAdded += (_, value) => { if (value == child) added++; };
            parent.ChildRemoved += (_, value) => { if (value == child) removed++; };

            child.Enabled = false;
            child.Enabled = false;
            parent.AddChild(child);
            parent.RemoveChild(child);

            Assert.That(enabledChanges, Is.EqualTo(1));
            Assert.That(added, Is.EqualTo(1));
            Assert.That(removed, Is.EqualTo(1));
        }

        [Test]
        public void DataTemplate_OwnsTypedFactoryLifecycleAttachmentsAndRebind()
        {
            var attachment = new AttachmentProbe();
            var activations = 0;
            var deactivations = 0;
            string rebound = null;
            var template = DataTemplate.Create<string>((context, item) =>
            {
                context.RegisterAttachment(attachment, TemplateAttachmentKind.Binding);
                context.RegisterLifecycle(() => activations++, () => deactivations++);
                context.RegisterRebind<string>(value => rebound = value);
                return new DisposableControl { Name = item };
            });

            var instance = template.CreateInstance("first");

            Assert.That(instance.Root.Name, Is.EqualTo("first"));
            Assert.That(instance.Item, Is.EqualTo("first"));
            Assert.That(instance.NameScope.Find<Control>("first"), Is.SameAs(instance.Root));
            Assert.That(instance.State, Is.EqualTo(TemplateInstanceState.Inactive));
            instance.Activate();
            instance.Activate();
            Assert.That(activations, Is.EqualTo(1));

            Assert.Throws<InvalidOperationException>(() => instance.Rebind("second"));
            instance.Deactivate();
            instance.Rebind("second");
            Assert.That(instance.Item, Is.EqualTo("second"));
            Assert.That(rebound, Is.EqualTo("second"));
            Assert.Throws<InvalidOperationException>(() => instance.Rebind(42));

            instance.Deactivate();
            Assert.That(deactivations, Is.EqualTo(1));
            instance.Dispose();
            Assert.That(attachment.DisposeCount, Is.EqualTo(1));
            Assert.That(((DisposableControl)instance.Root).IsDisposed, Is.True);
            Assert.That(instance.State, Is.EqualTo(TemplateInstanceState.Disposed));
            Assert.Throws<ObjectDisposedException>(() => instance.Activate());
        }

        [Test]
        public void ItemsPanelTemplate_RequiresFreshCompatiblePanels()
        {
            var created = 0;
            var template = new ItemsPanelTemplate(_ =>
            {
                created++;
                return new StackPanel();
            });

            using var first = template.CreateInstance();
            using var second = template.CreateInstance();

            Assert.That(first.Root, Is.TypeOf<StackPanel>());
            Assert.That(second.Root, Is.TypeOf<StackPanel>());
            Assert.That(second.Root, Is.Not.SameAs(first.Root));
            Assert.That(created, Is.EqualTo(2));

            var shared = new StackPanel();
            var invalid = new ItemsPanelTemplate(_ => shared);
            using var validInstance = invalid.CreateInstance();
            Assert.Throws<InvalidOperationException>(() => invalid.CreateInstance());
        }

        [Test]
        public void ControlTemplate_ExposesTypedFactoryAndValidatedPartMetadata()
        {
            var parts = new[] { new TemplatePartMetadata("PART_Content", typeof(Control)) };
            var template = ControlTemplate.Create<TemplateProbe>((context, owner) =>
            {
                Assert.That(context.TemplatedParent, Is.SameAs(owner));
                return new Control { Name = "PART_Content" };
            }, parts);
            var owner = new TemplateProbe { Template = template };

            Assert.That(template.TargetType, Is.EqualTo(typeof(TemplateProbe)));
            Assert.That(template.Parts, Has.Count.EqualTo(1));
            Assert.That(owner.GetTemplateChild("PART_Content"), Is.SameAs(owner.TemplateRoot));
            var missingPart = ControlTemplate.Create<TemplateProbe>(
                (_, _) => new Control(),
                new[] { new TemplatePartMetadata("PART_Required", typeof(Control)) });
            var wrongPartType = ControlTemplate.Create<TemplateProbe>(
                (_, _) => new Label { Name = "PART_Required" },
                new[] { new TemplatePartMetadata("PART_Required", typeof(Border)) });
            Assert.Throws<InvalidOperationException>(() => new TemplateProbe { Template = missingPart });
            Assert.Throws<InvalidOperationException>(() => new TemplateProbe { Template = wrongPartType });
            Assert.Throws<ArgumentException>(() => ControlTemplate.Create<TemplateProbe>(
                (_, _) => new Control(),
                new[]
                {
                    new TemplatePartMetadata("Duplicate", typeof(Control)),
                    new TemplatePartMetadata("Duplicate", typeof(Control)),
                }));
        }

        [Test]
        public void ControlTemplate_EnforcesInheritedOwnerPartContractsAndPreservesPreviousTemplateOnFailure()
        {
            var validRoot = new Border { Name = ContractTemplateProbe.RequiredPartName };
            var validTemplate = ControlTemplate.Create<ContractTemplateProbe>((_, _) => validRoot);
            var owner = new DerivedContractTemplateProbe { Template = validTemplate };

            Assert.That(owner.TemplateRoot, Is.SameAs(validRoot));

            var missingTemplate = ControlTemplate.Create<ContractTemplateProbe>((_, _) => new Control());
            var missing = Assert.Throws<InvalidOperationException>(() => owner.Template = missingTemplate);
            Assert.That(missing.Message, Does.Contain(typeof(DerivedContractTemplateProbe).FullName));
            Assert.That(missing.Message, Does.Contain(typeof(ContractTemplateProbe).FullName));
            Assert.That(missing.Message, Does.Contain(missingTemplate.GetType().FullName));
            Assert.That(missing.Message, Does.Contain($"factory version {missingTemplate.FactoryVersion}"));
            Assert.That(missing.Message, Does.Contain(ContractTemplateProbe.RequiredPartName));
            Assert.That(missing.Message, Does.Contain(typeof(Border).FullName));
            Assert.That(missing.Message, Does.Contain("actual missing"));
            Assert.That(owner.TemplateRoot, Is.SameAs(validRoot));

            var optionalWrongRoot = new StackPanel();
            optionalWrongRoot.AddChild(new Border { Name = ContractTemplateProbe.RequiredPartName });
            optionalWrongRoot.AddChild(new Control { Name = ContractTemplateProbe.OptionalPartName });
            var optionalWrongTemplate = ControlTemplate.Create<ContractTemplateProbe>((_, _) => optionalWrongRoot);
            var optionalWrong = Assert.Throws<InvalidOperationException>(() => owner.Template = optionalWrongTemplate);
            Assert.That(optionalWrong.Message, Does.Contain(ContractTemplateProbe.OptionalPartName));
            Assert.That(optionalWrong.Message, Does.Contain(typeof(Label).FullName));
            Assert.That(optionalWrong.Message, Does.Contain(typeof(Control).FullName));

            var conflictingMetadata = ControlTemplate.Create<ContractTemplateProbe>(
                (_, _) => new Border { Name = ContractTemplateProbe.RequiredPartName },
                new[] { new TemplatePartMetadata(ContractTemplateProbe.RequiredPartName, typeof(Control)) });
            var conflict = Assert.Throws<InvalidOperationException>(() => owner.Template = conflictingMetadata);
            Assert.That(conflict.Message, Does.Contain("conflicts"));
            Assert.That(owner.TemplateRoot, Is.SameAs(validRoot));
        }

        [Test]
        public void DefaultControlTemplateRegistry_UsesNearestRegisteredBaseType()
        {
            var registry = new DefaultControlTemplateRegistry();
            var baseTemplate = ControlTemplate.Create<TemplateProbe>((_, _) => new Control());
            var derivedTemplate = ControlTemplate.Create<DerivedTemplateProbe>((_, _) => new Control());
            registry.Register(baseTemplate);

            Assert.That(registry.GetTemplate(typeof(DerivedTemplateProbe)), Is.SameAs(baseTemplate));
            registry.Register(derivedTemplate);
            Assert.That(registry.GetTemplate(typeof(DerivedTemplateProbe)), Is.SameAs(derivedTemplate));
            Assert.Throws<InvalidOperationException>(() => registry.Register(derivedTemplate));
            Assert.That(registry.Unregister(typeof(DerivedTemplateProbe)), Is.True);
            Assert.That(registry.GetTemplate(typeof(DerivedTemplateProbe)), Is.SameAs(baseTemplate));
            Assert.Throws<ArgumentException>(() => registry.GetTemplate(typeof(Control)));
        }

        [Test]
        public void PackagedDefaultControlTemplatesResolveTypedFoundationalCompositions()
        {
            var registry = DefaultControlTemplateRegistry.Shared;
            var expectedTargets = new[]
            {
                typeof(ContentControl),
                typeof(ItemsControl),
                typeof(ListBox),
                typeof(ScrollContainer),
                typeof(DataGrid),
            };
            Assert.That(expectedTargets.Select(type => registry.GetTemplate(type)?.TargetType), Is.EqualTo(expectedTargets));

            using var content = new ContentControl { Template = registry.GetTemplate(typeof(ContentControl)) };
            using var items = new ItemsControl { Template = registry.GetTemplate(typeof(ItemsControl)) };
            using var list = new ListBox { Template = registry.GetTemplate(typeof(ListBox)) };
            using var scroll = new ScrollContainer();
            using var grid = new DataGrid { Template = registry.GetTemplate(typeof(DataGrid)) };

            Assert.Multiple(() =>
            {
                Assert.That(content.TemplateRoot, Is.TypeOf<ContentPresenter>());
                Assert.That(items.TemplateRoot, Is.TypeOf<ItemsPresenter>());
                Assert.That(list.TemplateRoot, Is.TypeOf<OverlayPanel>());
                Assert.That(list.GetTemplateChild(ListBox.ScrollPresenterPartName), Is.TypeOf<ScrollPresenter>());
                Assert.That(list.GetTemplateChild(ListBox.ItemsPresenterPartName), Is.TypeOf<ItemsPresenter>());
                Assert.That(scroll.TemplateRoot, Is.TypeOf<ScrollContainerChromePresenter>());
                Assert.That(scroll.GetTemplateChild(ScrollContainer.ScrollPresenterPartName), Is.TypeOf<ScrollPresenter>());
                Assert.That(grid.TemplateRoot, Is.TypeOf<GridPanel>());
                Assert.That(grid.GetTemplateChild(DataGrid.ColumnHeadersPartName), Is.TypeOf<GridPanel>());
            });
        }

        [Test]
        public void SemanticWidgetManifestRowsAreTemplatedAndHaveNoOwnerDrawPath()
        {
            var semanticWidgets = new[]
            {
                typeof(GroupBox), typeof(BaseButton), typeof(Button), typeof(CheckBox), typeof(CheckButton), typeof(LinkButton),
                typeof(TextureButton), typeof(ColorPresetButton), typeof(ColorPickerButton), typeof(Slider),
                typeof(HSlider), typeof(VSlider), typeof(ProgressBar), typeof(TextureProgressBar), typeof(ScrollBar),
                typeof(HScrollBar), typeof(VScrollBar), typeof(SplitContainer), typeof(HSplitContainer),
                typeof(VSplitContainer), typeof(LineEdit), typeof(TextEdit), typeof(CodeEdit), typeof(SpinBox),
                typeof(OptionButton), typeof(TabBar), typeof(TabContainer), typeof(ScrollContainer), typeof(Popup),
                typeof(PopupPanel), typeof(PopupMenu), typeof(MenuButton), typeof(MenuBar), typeof(AcceptDialog),
                typeof(ConfirmationDialog), typeof(FileDialog), typeof(ColorPicker), typeof(ColorPickerPopupPanel),
                typeof(ColorPickerDialog), typeof(FoldableContainer), typeof(GraphElement), typeof(GraphNode),
                typeof(GraphFrame), typeof(GraphEdit), typeof(ItemList), typeof(RichTextLabel),
                typeof(RichTextDocument), typeof(Tree), typeof(SubViewportContainer), typeof(VirtualJoystick),
            };

            Assert.That(semanticWidgets, Has.Length.EqualTo(50));
            foreach (var widgetType in semanticWidgets)
            {
                Assert.That(typeof(TemplatedControl).IsAssignableFrom(widgetType), Is.True, $"{widgetType.Name} must be a templated semantic owner.");
                Assert.That(DefaultControlTemplateRegistry.Shared.GetTemplate(widgetType), Is.Not.Null, $"{widgetType.Name} must resolve a packaged default template.");
                Assert.That(widgetType.GetMethod("Draw", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly), Is.Null, $"{widgetType.Name} must not retain an owner-level Draw override.");
                if (widgetType.GetConstructor(Type.EmptyTypes) == null) continue;
                using var widget = (TemplatedControl)Activator.CreateInstance(widgetType);
                Assert.That(widget.ApplyTemplate(), Is.True, $"{widgetType.Name} packaged template must apply successfully.");
                Assert.That(widget.TemplateRoot, Is.Not.Null);
                var peer = widget.AccessibilityPeer;
                var semantics = (peer.Role, peer.Name, peer.Value, peer.Actions, peer.States);
                Assert.That(peer.Role, Is.Not.EqualTo(AccessibilityRole.Generic), $"{widgetType.Name} must expose a semantic accessibility role.");

                widget.Template = DefaultControlTemplateRegistry.Shared.GetTemplate(widgetType);

                Assert.Multiple(() =>
                {
                    Assert.That(widget.AccessibilityPeer, Is.SameAs(peer), $"{widgetType.Name} template replacement must preserve peer identity.");
                    Assert.That((peer.Role, peer.Name, peer.Value, peer.Actions, peer.States), Is.EqualTo(semantics), $"{widgetType.Name} template replacement must preserve owner accessibility semantics.");
                });
            }
        }

        [Test]
        public void TemplatedControl_UsesRegisteredDefaultTemplateWhenNoExplicitTemplateExists()
        {
            var template = ControlTemplate.Create<DefaultTemplateProbe>((_, _) => new Control { Name = "DefaultRoot" });
            DefaultControlTemplateRegistry.Shared.Register(template);
            try
            {
                var control = new DefaultTemplateProbe();

                Assert.That(control.ApplyTemplate(), Is.True);
                Assert.That(control.Template, Is.Null);
                Assert.That(control.TemplateRoot.Name, Is.EqualTo("DefaultRoot"));
                Assert.That(control.GetTemplateChild("DefaultRoot"), Is.SameAs(control.TemplateRoot));
            }
            finally
            {
                DefaultControlTemplateRegistry.Shared.Unregister(typeof(DefaultTemplateProbe));
            }
        }

        [Test]
        public void Theme_ControlTemplatesUseNearestTypeParentFallbackAndVersioning()
        {
            var parent = new Theme();
            var theme = new Theme { Parent = parent };
            var baseTemplate = ControlTemplate.Create<ThemeTemplateProbe>((_, _) => new Control());
            var derivedTemplate = ControlTemplate.Create<DerivedThemeTemplateProbe>((_, _) => new Control());
            var initialVersion = theme.Version;

            parent.SetControlTemplate<ThemeTemplateProbe>(baseTemplate);
            Assert.That(theme.Version, Is.GreaterThan(initialVersion));
            var resourceVersion = theme.Version;
            theme.AccentColor = Color.CornflowerBlue;
            theme.SetStyleBox("panel", new StyleBoxEmpty());
            Assert.That(theme.Version, Is.EqualTo(resourceVersion + 2));
            var inheritedVersion = theme.Version;
            theme.SetControlTemplate<DerivedThemeTemplateProbe>(derivedTemplate);

            Assert.Multiple(() =>
            {
                Assert.That(theme.Version, Is.GreaterThan(inheritedVersion));
                Assert.That(theme.GetControlTemplate(typeof(DerivedThemeTemplateProbe)), Is.SameAs(derivedTemplate));
                Assert.That(theme.GetControlTemplate(typeof(ThemeTemplateProbe)), Is.SameAs(baseTemplate));
                Assert.Throws<ArgumentException>(() => theme.GetControlTemplate(typeof(Control)));
                Assert.Throws<ArgumentException>(() => theme.SetControlTemplate(typeof(ThemeTemplateProbe), derivedTemplate));
            });
            Assert.That(theme.RemoveControlTemplate(typeof(DerivedThemeTemplateProbe)), Is.True);
            Assert.That(theme.GetControlTemplate(typeof(DerivedThemeTemplateProbe)), Is.SameAs(baseTemplate));
        }

        [Test]
        public void TemplatedControl_UsesExplicitStyleThemeAndDefaultPrecedenceAtFrameBoundary()
        {
            var defaultTemplate = ControlTemplate.Create<ThemeTemplateProbe>((_, _) => new Control { Name = "Default" });
            var themeTemplate = ControlTemplate.Create<ThemeTemplateProbe>((_, _) => new Control { Name = "Theme" });
            var replacementTemplate = ControlTemplate.Create<DerivedThemeTemplateProbe>((_, _) => new Control { Name = "Replacement" });
            var styleTemplate = ControlTemplate.Create<DerivedThemeTemplateProbe>((_, _) => new Control { Name = "Style" });
            var localTemplate = ControlTemplate.Create<DerivedThemeTemplateProbe>((_, _) => new Control { Name = "Local" });
            var theme = new Theme();
            theme.SetControlTemplate<ThemeTemplateProbe>(themeTemplate);
            DefaultControlTemplateRegistry.Shared.Register(defaultTemplate);
            try
            {
                using var context = new UIContext { Theme = theme };
                var control = new DerivedThemeTemplateProbe { Size = new Vector2(120, 40) };
                context.Add(control);
                context.Layout();
                Assert.That(control.TemplateRoot.Name, Is.EqualTo("Theme"));

                theme.SetControlTemplate<DerivedThemeTemplateProbe>(replacementTemplate);
                Assert.That(control.TemplateRoot.Name, Is.EqualTo("Theme"));
                context.Layout();
                Assert.That(control.TemplateRoot.Name, Is.EqualTo("Replacement"));

                var templateProperty = new XamlProperty<ControlTemplate>(nameof(TemplatedControl.Template),
                    target => ((TemplatedControl)target).Template,
                    (target, value) => ((TemplatedControl)target).Template = value);
                using (var style = XamlValues.Set(control, templateProperty, XamlValueLayer.Style, styleTemplate))
                {
                    Assert.That(control.TemplateRoot.Name, Is.EqualTo("Style"));
                    using (var local = XamlValues.Set(control, templateProperty, XamlValueLayer.Local, localTemplate))
                        Assert.That(control.TemplateRoot.Name, Is.EqualTo("Local"));
                    Assert.That(control.TemplateRoot.Name, Is.EqualTo("Style"));
                }
                Assert.That(control.TemplateRoot.Name, Is.EqualTo("Replacement"));

                theme.RemoveControlTemplate(typeof(DerivedThemeTemplateProbe));
                theme.RemoveControlTemplate(typeof(ThemeTemplateProbe));
                context.Layout();
                Assert.That(control.TemplateRoot.Name, Is.EqualTo("Default"));
            }
            finally
            {
                DefaultControlTemplateRegistry.Shared.Unregister(typeof(ThemeTemplateProbe));
            }
        }

        [Test]
        public void FailedTemplateBuildDisposesAttachmentsAndReturnedRoot()
        {
            var attachment = new AttachmentProbe();
            var duplicateRoot = new DisposableControl { Name = "Duplicate" };
            duplicateRoot.AddChild(new Control { Name = "Duplicate" });
            var template = DataTemplate.Create<string>((context, _) =>
            {
                context.RegisterAttachment(attachment);
                return duplicateRoot;
            });

            Assert.Throws<InvalidOperationException>(() => template.CreateInstance("item"));
            Assert.That(attachment.DisposeCount, Is.EqualTo(1));
            Assert.That(duplicateRoot.IsDisposed, Is.True);
        }

        [Test]
        public void TemplateInstance_PartialActivationRollsBackAndCanRetry()
        {
            var firstActive = false;
            var failSecond = true;
            var template = DataTemplate.Create<string>((context, _) =>
            {
                context.RegisterLifecycle(() => firstActive = true, () => firstActive = false);
                context.RegisterLifecycle(
                    () =>
                    {
                        if (failSecond) throw new InvalidOperationException("Activation failed.");
                    },
                    () => { });
                return new Control();
            });
            using var instance = template.CreateInstance("item");

            Assert.Throws<InvalidOperationException>(() => instance.Activate());
            Assert.That(firstActive, Is.False);
            Assert.That(instance.State, Is.EqualTo(TemplateInstanceState.Inactive));

            failSecond = false;
            instance.Activate();
            Assert.That(firstActive, Is.True);
            Assert.That(instance.State, Is.EqualTo(TemplateInstanceState.Active));
        }

        [Test]
        public void TemplatedControl_ThrowingDeactivationPreservesPreviousRootAndDisposesCandidate()
        {
            var control = new TemplateProbe();
            var failDeactivation = true;
            var lifecycleActive = false;
            var firstRoot = new DisposableControl();
            control.Template = ControlTemplate.Create<TemplateProbe>((context, _) =>
            {
                context.RegisterLifecycle(() => lifecycleActive = true, () =>
                {
                    lifecycleActive = false;
                    if (failDeactivation) throw new InvalidOperationException("Deactivation failed.");
                });
                return firstRoot;
            });
            var failedRoot = new DisposableControl();

            Assert.Throws<InvalidOperationException>(() =>
                control.Template = ControlTemplate.Create<TemplateProbe>((_, _) => failedRoot));

            Assert.That(control.TemplateRoot, Is.SameAs(firstRoot));
            Assert.That(firstRoot.VisualParent, Is.SameAs(control));
            Assert.That(firstRoot.IsDisposed, Is.False);
            Assert.That(failedRoot.IsDisposed, Is.True);
            Assert.That(lifecycleActive, Is.True);
            failDeactivation = false;
            control.Template = null;
        }

        [Test]
        public void FrameworkTemplate_RejectsOwnedRootsWithoutStealingOrDisposingThem()
        {
            var owner = new Control();
            var ownedRoot = new DisposableControl();
            owner.AddChild(ownedRoot);
            var template = DataTemplate.Create<string>((_, _) => ownedRoot);

            Assert.Throws<InvalidOperationException>(() => template.CreateInstance("item"));
            Assert.That(ownedRoot.Parent, Is.SameAs(owner));
            Assert.That(ownedRoot.VisualParent, Is.SameAs(owner));
            Assert.That(ownedRoot.IsDisposed, Is.False);
        }

        [Test]
        public void TemplateInstance_DisposeCompletesOwnedCleanupWhenLifecycleThrows()
        {
            var attachment = new AttachmentProbe();
            var root = new DisposableControl();
            var template = DataTemplate.Create<string>((context, _) =>
            {
                context.RegisterAttachment(attachment);
                context.RegisterLifecycle(() => { }, () => throw new InvalidOperationException("Deactivation failed."));
                return root;
            });
            var instance = template.CreateInstance("item");
            instance.Activate();

            Assert.Throws<InvalidOperationException>(() => instance.Dispose());
            Assert.That(instance.IsDisposed, Is.True);
            Assert.That(attachment.DisposeCount, Is.EqualTo(1));
            Assert.That(root.IsDisposed, Is.True);
        }

        [Test]
        public void XamlValues_ResolvePrecedenceAndRestoreUnderlyingValues()
        {
            var target = new ValueTarget { Value = 10 };
            var property = new XamlProperty<int>(nameof(ValueTarget.Value), value => ((ValueTarget)value).Value, (value, current) => ((ValueTarget)value).Value = current);
            using var style = XamlValues.Set(target, property, XamlValueLayer.Style, 20);
            using var local = XamlValues.Set(target, property, XamlValueLayer.Local, 30);
            using (var animation = XamlValues.Set(target, property, XamlValueLayer.Animation, 40))
            {
                Assert.That(target.Value, Is.EqualTo(40));
                animation.Value = 41;
                Assert.That(target.Value, Is.EqualTo(41));
            }
            Assert.That(target.Value, Is.EqualTo(30));
            local.Dispose();
            Assert.That(target.Value, Is.EqualTo(20));
            style.Dispose();
            Assert.That(target.Value, Is.EqualTo(10));
        }

        [Test]
        public void XamlValues_UsePriorityThenDeclarationOrderAndExplicitBaseRefresh()
        {
            var target = new ValueTarget { Value = 1 };
            var property = new XamlProperty<int>(nameof(ValueTarget.Value), value => ((ValueTarget)value).Value, (value, current) => ((ValueTarget)value).Value = current);
            using var earlier = XamlValues.Set(target, property, XamlValueLayer.Style, 2, priority: 10);
            using var later = XamlValues.Set(target, property, XamlValueLayer.Style, 3, priority: 10);
            using var specific = XamlValues.Set(target, property, XamlValueLayer.Style, 4, priority: 20);
            Assert.That(target.Value, Is.EqualTo(4));
            specific.Dispose();
            Assert.That(target.Value, Is.EqualTo(3));

            target.Value = 9;
            XamlValues.RefreshBaseValue(target, property);
            Assert.That(target.Value, Is.EqualTo(3));
            later.Dispose();
            earlier.Dispose();
            Assert.That(target.Value, Is.EqualTo(9));
        }

        private sealed class RegisteredView
        {
            public string Value { get; set; }
        }

        private sealed class AttachmentProbe : IDisposable, IXamlUpdateParticipant
        {
            public int UpdateCount { get; private set; }
            public int DisposeCount { get; private set; }
            public void Update(GameTime gameTime) => UpdateCount++;
            public void Dispose() => DisposeCount++;
        }

        private sealed class CallbackDisposable : IDisposable
        {
            private Action _dispose;
            public CallbackDisposable(Action dispose) => _dispose = dispose;
            public void Dispose()
            {
                var dispose = _dispose;
                _dispose = null;
                dispose?.Invoke();
            }
        }

        private sealed class BindingModel : INotifyPropertyChanged
        {
            private PropertyChangedEventHandler _propertyChanged;
            public event PropertyChangedEventHandler PropertyChanged
            {
                add { _propertyChanged += value; SubscriberCount++; }
                remove { _propertyChanged -= value; SubscriberCount--; }
            }
            public int SubscriberCount { get; private set; }
            public string Value { get; set; }
        }

        private sealed class BindingAncestor : Control
        {
            private string _value;
            public string Value
            {
                get => _value;
                set
                {
                    if (_value == value) return;
                    _value = value;
                    OnPropertyChanged(nameof(Value));
                }
            }
        }

        [TemplatePart(RequiredPartName, typeof(Border))]
        [TemplatePart(OptionalPartName, typeof(Label), false)]
        private class ContractTemplateProbe : TemplatedControl
        {
            public const string RequiredPartName = "PART_Required";
            public const string OptionalPartName = "PART_Optional";
        }

        private sealed class DerivedContractTemplateProbe : ContractTemplateProbe { }

        private class TemplateProbe : TemplatedControl
        {
            public int AppliedCount { get; private set; }
            protected override void OnTemplateApplied() => AppliedCount++;
        }

        private sealed class DerivedTemplateProbe : TemplateProbe { }
        private sealed class SiblingTemplateProbe : TemplateProbe { }
        private sealed class NestedTemplateProbe : TemplatedControl { }
        private sealed class DisposableTemplateProbe : TemplateProbe
        {
            public int DisposeCount { get; private set; }
            public bool IsDisposed => DisposeCount > 0;
            public override void Dispose()
            {
                base.Dispose();
                DisposeCount++;
            }
        }
        private sealed class DefaultTemplateProbe : TemplatedControl { }
        private class ThemeTemplateProbe : TemplatedControl { }
        private sealed class DerivedThemeTemplateProbe : ThemeTemplateProbe { }

        private sealed class FailingTemplateProbe : TemplatedControl
        {
            public bool FailNextApply { get; set; }
            protected override void OnTemplateApplied()
            {
                if (!FailNextApply) return;
                FailNextApply = false;
                throw new InvalidOperationException("Template application failed.");
            }
        }

        private sealed class DisposableControl : Control, IDisposable
        {
            public bool IsDisposed { get; private set; }
            public void Dispose() => IsDisposed = true;
        }

        private sealed class ValueTarget
        {
            public int Value { get; set; }
        }
    }
}