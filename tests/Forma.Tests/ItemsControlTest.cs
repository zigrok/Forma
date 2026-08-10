// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Forma.Xaml;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Forma.Tests
{
    public class ItemsControlTest
    {
        [Test]
        public void ItemsControl_RealizesExplicitTemplatesIntoItsItemsPanel()
        {
            var template = DataTemplate.Create<string>((context, item) =>
            {
                var text = new TextBlock { Text = item };
                context.BindItem(text, item);
                return text;
            });
            var control = new ItemsControl
            {
                AlternationCount = 2,
                ItemTemplate = template,
                ItemsSource = new[] { "first", "second", "third" },
            };

            Assert.That(control.TemplateRoot, Is.TypeOf<ItemsPresenter>());
            var presenter = (ItemsPresenter)control.TemplateRoot;
            Assert.Multiple(() =>
            {
                Assert.That(presenter.Panel, Is.TypeOf<StackPanel>());
                Assert.That(control.RealizedCount, Is.EqualTo(3));
                Assert.That(presenter.Panel.VisualChildren, Has.Count.EqualTo(3));
                Assert.That(presenter.Panel.VisualChildren.Select(child => child.DataContext), Is.EqualTo(new[] { "first", "second", "third" }));
                Assert.That(control.GetRealizedContainer(0), Is.InstanceOf<ContentControl>());
                Assert.That(((ContentControl)control.GetRealizedContainer(0)).TemplateRoot, Is.TypeOf<ContentPresenter>());
                Assert.That(control.RealizationDiagnostics, Is.Empty);
            });

            control.Dispose();
        }

        [Test]
        public void ItemsControl_ReportsMissingExplicitItemTemplate()
        {
            var control = new ItemsControl { ItemsSource = new[] { "item" } };

            Assert.Multiple(() =>
            {
                Assert.That(control.RealizedCount, Is.Zero);
                Assert.That(control.RealizationDiagnostics, Has.One.Items);
                Assert.That(control.RealizationDiagnostics[0].Message, Does.Contain("explicit ItemTemplate"));
            });

            control.Dispose();
        }

        [Test]
        public void ItemsControl_SnapshotsSourcesAndReplacesPanelsStylesAndAlternation()
        {
            var enumerations = 0;
            IEnumerable Source()
            {
                enumerations++;
                yield return "first";
                yield return "second";
            }
            var tooltip = new XamlProperty<string>(nameof(Control.TooltipText),
                target => ((Control)target).TooltipText, (target, value) => ((Control)target).TooltipText = value);
            var style = new Style("ContentControl.item-container");
            style.AddSetter(new StyleSetter<string>(tooltip, "styled"));
            var control = new ItemsControl
            {
                ItemsPanel = new ItemsPanelTemplate(_ => new GridPanel()),
                ItemContainerStyle = style,
                AlternationCount = 2,
                ItemTemplate = DataTemplate.Create<string>((context, item) => new TextBlock { Text = item }),
                ItemsSource = Source(),
            };

            Assert.Multiple(() =>
            {
                Assert.That(enumerations, Is.EqualTo(1));
                Assert.That(((ItemsPresenter)control.TemplateRoot).Panel, Is.TypeOf<GridPanel>());
                Assert.That(control.GetRealizedContainer(0).TooltipText, Is.EqualTo("styled"));
                Assert.That(control.GetAlternationIndex(0), Is.Zero);
                Assert.That(control.GetAlternationIndex(1), Is.EqualTo(1));
            });

            var previousContainer = control.GetRealizedContainer(0);
            var panel = ((ItemsPresenter)control.TemplateRoot).Panel;
            control.ItemsSource = new[] { "replacement" };
            Assert.Multiple(() =>
            {
                Assert.That(control.RealizedCount, Is.EqualTo(1));
                Assert.That(control.GetRealizedContainer(0).DataContext, Is.EqualTo("replacement"));
                Assert.That(panel.VisualChildren, Has.Count.EqualTo(1));
                Assert.That(previousContainer.VisualParent, Is.Null);
                Assert.That(enumerations, Is.EqualTo(1));
            });
            control.Dispose();
        }

        [Test]
        public void ItemsControl_ReportsIncompatibleTemplateItemsWithoutPartialRealization()
        {
            var control = new ItemsControl
            {
                ItemTemplate = DataTemplate.Create<string>((context, item) => new TextBlock { Text = item }),
                ItemsSource = new object[] { "valid", 42 },
            };

            Assert.Multiple(() =>
            {
                Assert.That(control.RealizedCount, Is.Zero);
                Assert.That(control.RealizationDiagnostics, Has.One.Items);
                Assert.That(control.RealizationDiagnostics[0].Index, Is.EqualTo(1));
                Assert.That(control.RealizationDiagnostics[0].Message, Does.Contain("cannot be applied"));
            });
            control.Dispose();
        }

        [Test]
        public void ItemsControl_PreservesOccurrenceContainersAcrossCollectionDeltas()
        {
            var duplicate = new object();
            var tail = new object();
            var source = new ObservableCollection<object> { duplicate, duplicate, tail };
            var control = new ItemsControl
            {
                ItemTemplate = DataTemplate.Create<object>((context, item) => new Control()),
                ItemsSource = source,
            };
            var firstDuplicate = control.GetRealizedContainer(0);
            var secondDuplicate = control.GetRealizedContainer(1);
            var tailContainer = control.GetRealizedContainer(2);

            var inserted = new object();
            source.Insert(1, inserted);
            var insertedContainer = control.GetRealizedContainer(1);
            Assert.Multiple(() =>
            {
                Assert.That(control.GetRealizedContainer(0), Is.SameAs(firstDuplicate));
                Assert.That(control.GetRealizedContainer(2), Is.SameAs(secondDuplicate));
                Assert.That(control.GetRealizedContainer(3), Is.SameAs(tailContainer));
            });

            source.Move(3, 1);
            Assert.Multiple(() =>
            {
                Assert.That(control.GetRealizedContainer(0), Is.SameAs(firstDuplicate));
                Assert.That(control.GetRealizedContainer(1), Is.SameAs(tailContainer));
                Assert.That(control.GetRealizedContainer(2), Is.SameAs(insertedContainer));
                Assert.That(control.GetRealizedContainer(3), Is.SameAs(secondDuplicate));
            });

            source[2] = new object();
            Assert.Multiple(() =>
            {
                Assert.That(control.GetRealizedContainer(0), Is.SameAs(firstDuplicate));
                Assert.That(control.GetRealizedContainer(1), Is.SameAs(tailContainer));
                Assert.That(control.GetRealizedContainer(2), Is.Not.SameAs(insertedContainer));
                Assert.That(control.GetRealizedContainer(3), Is.SameAs(secondDuplicate));
            });

            source.RemoveAt(0);
            Assert.Multiple(() =>
            {
                Assert.That(control.GetRealizedContainer(0), Is.SameAs(tailContainer));
                Assert.That(control.GetRealizedContainer(2), Is.SameAs(secondDuplicate));
            });

            var remaining = source.Select((_, index) => control.GetRealizedContainer(index)).ToArray();
            source.Clear();
            Assert.Multiple(() =>
            {
                Assert.That(control.RealizedCount, Is.Zero);
                Assert.That(remaining.Select(item => item.VisualParent), Is.All.Null);
            });
            control.Dispose();
        }

        [Test]
        public void ListBox_PreservesSelectedOccurrenceAcrossMoveAndClearsItOnRemoval()
        {
            var duplicate = new object();
            var source = new ObservableCollection<object> { duplicate, duplicate, new object() };
            var list = new ListBox
            {
                ItemTemplate = DataTemplate.Create<object>((context, item) => new Control()),
                ItemsSource = source,
            };
            var changes = new List<ListBoxSelectionChangedEventArgs>();
            list.SelectionChanged += (_, args) => changes.Add(args);

            list.SelectedIndex = 1;
            source.Move(1, 0);

            Assert.Multiple(() =>
            {
                Assert.That(list.SelectedIndex, Is.Zero);
                Assert.That(list.CurrentIndex, Is.Zero);
                Assert.That(list.SelectedItem, Is.SameAs(duplicate));
                Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0 }));
                Assert.That(((ListBoxItem)list.GetRealizedContainer(0)).IsSelected, Is.True);
                Assert.That(((ListBoxItem)list.GetRealizedContainer(1)).IsSelected, Is.False);
                Assert.That(changes, Has.Count.EqualTo(1));
            });

            source.RemoveAt(0);

            Assert.Multiple(() =>
            {
                Assert.That(list.HasSelection, Is.False);
                Assert.That(list.SelectedIndex, Is.EqualTo(-1));
                Assert.That(list.CurrentIndex, Is.EqualTo(-1));
                Assert.That(changes, Has.Count.EqualTo(2));
                Assert.That(changes[1].OldIndices, Is.EqualTo(new[] { 0 }));
                Assert.That(changes[1].OldItems, Is.EqualTo(new[] { duplicate }));
            });
            list.Dispose();
        }

        [Test]
        public void ListBox_ExposesSelectionModesCurrentSearchAndActivation()
        {
            var duplicate = new object();
            var source = new ObservableCollection<object> { "alpha", duplicate, "bravo", duplicate };
            var list = new ListBox
            {
                ItemTemplate = DataTemplate.Create<object>((context, item) => new Control()),
                ItemsSource = source,
                SelectionMode = ItemListSelectionMode.Multi,
            };
            ListBoxItemEventArgs activated = null;
            list.ItemActivated += (_, args) => activated = args;

            list.Select(0);
            list.Select(2, additive: true);
            Assert.Multiple(() =>
            {
                Assert.That(list.HasSelection, Is.True);
                Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0, 2 }));
                Assert.That(list.SelectedItems, Is.EqualTo(new object[] { "alpha", "bravo" }));
                Assert.That(list.CurrentIndex, Is.EqualTo(2));
                Assert.That(list.FindNextItem("br", 0), Is.EqualTo(2));
            });

            list.ToggleSelection(0);
            list.CurrentIndex = 3;
            list.SelectedItem = duplicate;
            Assert.Multiple(() =>
            {
                Assert.That(list.SelectedIndex, Is.EqualTo(3));
                Assert.That(list.CurrentIndex, Is.EqualTo(3));
                Assert.That(list.SelectedItem, Is.SameAs(duplicate));
                Assert.That(((ListBoxItem)list.GetRealizedContainer(3)).IsCurrent, Is.True);
            });

            list.Activate(1);
            Assert.Multiple(() =>
            {
                Assert.That(activated, Is.Not.Null);
                Assert.That(activated.Index, Is.EqualTo(1));
                Assert.That(activated.Item, Is.SameAs(duplicate));
            });

            list.ItemsSource = new object[] { "replacement" };
            Assert.Multiple(() =>
            {
                Assert.That(list.HasSelection, Is.False);
                Assert.That(list.CurrentIndex, Is.EqualTo(-1));
                Assert.That(list.SelectedItems, Is.Empty);
            });
            list.Dispose();
        }

        [Test]
        public void ListBox_MapsPointerKeyboardRangeAndDisabledContainerInput()
        {
            var list = new ListBox
            {
                Size = new Vector2(160, 120),
                ItemTemplate = DataTemplate.Create<string>((context, item) => new Control
                {
                    CustomMinimumSize = new Vector2(120, 24),
                }),
                ItemsSource = new[] { "alpha", "beta", "bravo", "charlie" },
                SelectionMode = ItemListSelectionMode.Multi,
            };
            var changes = new List<ListBoxSelectionChangedEventArgs>();
            ListBoxItemEventArgs activated = null;
            list.SelectionChanged += (_, args) => changes.Add(args);
            list.ItemActivated += (_, args) => activated = args;
            using var context = new UIContext();
            context.Add(list);
            context.Layout();

            list.PointerPressed(list.GetRealizedContainer(0).VisualBounds.Center);
            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), default, new KeyboardState(Keys.LeftControl));
            list.PointerPressed(list.GetRealizedContainer(2).VisualBounds.Center);
            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), default, new KeyboardState(Keys.LeftShift));
            list.PointerPressed(list.GetRealizedContainer(3).VisualBounds.Center);

            Assert.Multiple(() =>
            {
                Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 2, 3 }));
                Assert.That(list.CurrentIndex, Is.EqualTo(3));
                Assert.That(changes, Has.Count.EqualTo(3));
                Assert.That(changes[2].OldIndices, Is.EqualTo(new[] { 0, 2 }));
                Assert.That(changes[2].NewIndices, Is.EqualTo(new[] { 2, 3 }));
            });

            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), default, new KeyboardState());
            ((ListBoxItem)list.GetRealizedContainer(1)).IsSelectable = false;
            list.CurrentIndex = 0;
            list.KeyPressed(Keys.Down);
            Assert.That(list.CurrentIndex, Is.EqualTo(2));

            list.KeyPressed(Keys.Space);
            Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 3 }));
            list.KeyPressed(Keys.Enter);
            Assert.Multiple(() =>
            {
                Assert.That(activated, Is.Not.Null);
                Assert.That(activated.Index, Is.EqualTo(2));
                Assert.That(activated.Item, Is.EqualTo("bravo"));
            });

            list.TextInput('c');
            Assert.That(list.CurrentIndex, Is.EqualTo(3));
        }

        [Test]
        public void ListBox_ShiftRangeResetsAnchorOnReleaseAndSkipsUnselectableRows()
        {
            var list = new ListBox
            {
                ItemTemplate = DataTemplate.Create<string>((context, item) => new Control()),
                ItemsSource = new[] { "zero", "one", "two", "three", "four" },
                SelectionMode = ItemListSelectionMode.Multi,
            };
            using var context = new UIContext();
            context.Add(list);
            context.Layout();
            list.Select(1);

            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), default, new KeyboardState(Keys.LeftShift));
            list.KeyPressed(Keys.Down);
            list.KeyPressed(Keys.Down);
            Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 1, 2, 3 }));

            list.KeyPressed(Keys.Up);
            Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 1, 2 }));

            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), default, new KeyboardState());
            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), default, new KeyboardState(Keys.LeftShift));
            list.KeyPressed(Keys.Down);
            Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 2, 3 }));

            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), default, new KeyboardState());
            list.ClearSelection();
            list.CurrentIndex = 0;
            ((ListBoxItem)list.GetRealizedContainer(1)).IsSelectable = false;
            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), default, new KeyboardState(Keys.LeftShift));
            list.KeyPressed(Keys.Down);
            Assert.Multiple(() =>
            {
                Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0, 2 }));
                Assert.That(list.CurrentIndex, Is.EqualTo(2));
            });
            list.Dispose();
        }

        [Test]
        public void ListBox_ToggleCtrlPlainAndKeyboardSelectionMatchItemListModes()
        {
            var list = new ListBox
            {
                Size = new Vector2(100, 100),
                ItemTemplate = DataTemplate.Create<string>((context, item) => new Control
                {
                    CustomMinimumSize = new Vector2(80, 20),
                }),
                ItemsSource = new[] { "a", "b", "c" },
                SelectionMode = ItemListSelectionMode.Toggle,
            };
            using var context = new UIContext();
            context.Add(list);
            context.Layout();
            var first = list.GetRealizedContainer(0).VisualBounds.Center;
            var second = list.GetRealizedContainer(1).VisualBounds.Center;

            list.PointerPressed(first);
            Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0 }));
            list.PointerPressed(first);
            Assert.That(list.SelectedIndices, Is.Empty);

            list.SelectionMode = ItemListSelectionMode.Multi;
            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), default, new KeyboardState(Keys.LeftControl));
            list.PointerPressed(first);
            list.PointerPressed(second);
            Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0, 1 }));
            list.PointerPressed(second);
            Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0 }));

            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), default, new KeyboardState());
            list.PointerPressed(second);
            Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 1 }));

            list.ClearSelection();
            list.CurrentIndex = 0;
            list.KeyPressed(Keys.Down);
            Assert.Multiple(() =>
            {
                Assert.That(list.CurrentIndex, Is.EqualTo(1));
                Assert.That(list.SelectedIndices, Is.Empty);
            });
            list.KeyPressed(Keys.Space);
            Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 1 }));
            list.KeyPressed(Keys.Space);
            Assert.That(list.SelectedIndices, Is.Empty);
            list.Dispose();
        }

        [Test]
        public void ListBox_ActivationRightClickAndReselectHonorInteractionPolicies()
        {
            var list = new ListBox
            {
                Size = new Vector2(100, 100),
                ItemTemplate = DataTemplate.Create<string>((context, item) => new Control
                {
                    CustomMinimumSize = new Vector2(80, 20),
                }),
                ItemsSource = new[] { "a", "b" },
                AllowReselect = true,
            };
            var activated = new List<int>();
            var selectionChanges = 0;
            list.ItemActivated += (_, args) => activated.Add(args.Index);
            list.SelectionChanged += (_, _) => selectionChanges++;
            using var context = new UIContext();
            context.Add(list);
            context.Layout();
            var first = list.GetRealizedContainer(0).VisualBounds.Center;
            var second = list.GetRealizedContainer(1).VisualBounds.Center;

            list.PointerPressed(first);
            list.PointerReleased(first, true);
            Assert.That(activated, Is.Empty);
            list.PointerPressed(first);
            list.PointerReleased(first, true);
            Assert.That(activated, Is.EqualTo(new[] { 0 }));

            list.Select(0);
            list.Select(0);
            Assert.That(selectionChanges, Is.EqualTo(4));

            list.PointerButtonPressed(second, PointerButton.Right);
            Assert.That(list.SelectedIndex, Is.Zero);
            list.AllowRightMouseSelect = true;
            list.PointerButtonPressed(second, PointerButton.Right);
            Assert.That(list.SelectedIndex, Is.EqualTo(1));

            ((ListBoxItem)list.GetRealizedContainer(1)).IsSelectable = false;
            list.CurrentIndex = 1;
            list.KeyPressed(Keys.Enter);
            Assert.That(activated, Is.EqualTo(new[] { 0 }));
            list.Dispose();
        }

        [Test]
        public void ListBox_ActivateOnSingleClickRaisesItemActivatedOnFirstRelease()
        {
            var list = new ListBox
            {
                Size = new Vector2(100, 100),
                ItemTemplate = DataTemplate.Create<string>((context, item) => new Control
                {
                    CustomMinimumSize = new Vector2(80, 20),
                }),
                ItemsSource = new[] { "a", "b" },
                ActivateOnSingleClick = true,
            };
            var activated = new List<int>();
            list.ItemActivated += (_, args) => activated.Add(args.Index);
            using var context = new UIContext();
            context.Add(list);
            context.Layout();
            var first = list.GetRealizedContainer(0).VisualBounds.Center;

            list.PointerPressed(first);
            list.PointerReleased(first, true);

            Assert.That(activated, Is.EqualTo(new[] { 0 }));
            list.Dispose();
        }

        [Test]
        public void ListBox_ItemsPanelReplacementUpdatesNestedPresenter()
        {
            var list = new ListBox
            {
                ItemTemplate = DataTemplate.Create<string>((context, item) => new Control()),
                ItemsSource = new[] { "first", "second" },
                Size = new Vector2(120, 60),
            };
            using var context = new UIContext { ViewportSize = new Vector2(120, 60) };
            context.Add(list);
            context.Layout();
            var presenter = (ItemsPresenter)list.GetTemplateChild(ListBox.ItemsPresenterPartName);
            var replacement = new ItemsPanelTemplate(_ => new VirtualizingStackPanel { EstimatedItemExtent = 20 });

            list.ItemsPanel = replacement;
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(presenter.ItemsPanel, Is.SameAs(replacement));
                Assert.That(presenter.Panel, Is.TypeOf<VirtualizingStackPanel>());
                Assert.That(list.RealizedCount, Is.LessThanOrEqualTo(4));
            });
        }

        [Test]
        public void ListBox_HorizontalNavigationHonorsRtlAndWrapPolicy()
        {
            var list = new ListBox
            {
                LayoutDirection = LayoutDirection.RightToLeft,
                ItemsPanel = new ItemsPanelTemplate(_ => new StackPanel { Orientation = Orientation.Horizontal }),
                ItemTemplate = DataTemplate.Create<string>((context, item) => new Control()),
                ItemsSource = new[] { "first", "second", "third" },
                SelectionMode = ItemListSelectionMode.Multi,
            };

            list.CurrentIndex = 1;
            list.KeyPressed(Keys.Left);
            Assert.That(list.CurrentIndex, Is.EqualTo(2));
            list.KeyPressed(Keys.Left);
            Assert.That(list.CurrentIndex, Is.Zero);

            list.WrapNavigation = false;
            list.KeyPressed(Keys.Right);
            Assert.That(list.CurrentIndex, Is.Zero);
            list.Dispose();
        }

        [Test]
        public void ListBox_CollectionMutationsUseAtomicSlotSelectionSnapshots()
        {
            var source = new ObservableCollection<string> { "a", "b", "c", "d" };
            var list = new ListBox
            {
                ItemTemplate = DataTemplate.Create<string>((context, item) => new Control()),
                ItemsSource = source,
                SelectionMode = ItemListSelectionMode.Multi,
            };
            var changes = new List<ListBoxSelectionChangedEventArgs>();
            list.SelectionChanged += (_, args) => changes.Add(args);
            list.Select(1);
            list.Select(3, additive: true);
            var semanticChangeCount = changes.Count;

            source.Insert(0, "inserted");
            source.Move(4, 0);
            source.RemoveAt(1);
            Assert.Multiple(() =>
            {
                Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0, 2 }));
                Assert.That(list.SelectedItems, Is.EqualTo(new[] { "d", "b" }));
                Assert.That(list.CurrentIndex, Is.Zero);
                Assert.That(changes, Has.Count.EqualTo(semanticChangeCount));
            });

            source[2] = "replacement";
            Assert.Multiple(() =>
            {
                Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0 }));
                Assert.That(changes, Has.Count.EqualTo(semanticChangeCount + 1));
                Assert.That(changes[^1].OldIndices, Is.EqualTo(new[] { 0, 2 }));
                Assert.That(changes[^1].NewIndices, Is.EqualTo(new[] { 0 }));
            });

            source.Clear();
            Assert.Multiple(() =>
            {
                Assert.That(list.HasSelection, Is.False);
                Assert.That(list.CurrentIndex, Is.EqualTo(-1));
                Assert.That(changes, Has.Count.EqualTo(semanticChangeCount + 2));
                Assert.That(changes[^1].OldItems, Is.EqualTo(new[] { "d" }));
                Assert.That(changes[^1].NewItems, Is.Empty);
            });
            list.Dispose();
        }

        [Test]
        public void ListBoxItem_PseudoStatesInvalidateOnlyMatchingTemplateParts()
        {
            var tooltip = new XamlProperty<string>(nameof(Control.TooltipText),
                target => ((Control)target).TooltipText,
                (target, value) => ((Control)target).TooltipText = value);
            var opacity = new XamlProperty<float>(nameof(Control.Opacity),
                target => ((Control)target).Opacity,
                (target, value) => ((Control)target).Opacity = value);
            var selectedStyle = new Style("ListBoxItem:selected >> Border.selection");
            selectedStyle.AddSetter(new StyleSetter<string>(tooltip, "selected"));
            var currentStyle = new Style("ListBoxItem:current >> Border.selection");
            currentStyle.AddSetter(new StyleSetter<float>(opacity, .5f));
            var list = new ListBox
            {
                ItemTemplate = DataTemplate.Create<string>((context, item) => new Control()),
                ItemsSource = new[] { "first", "second" },
            };
            var parts = new Border[2];
            for (var index = 0; index < parts.Length; index++)
            {
                var part = new Border { TooltipText = "base" };
                part.Classes.Add("selection");
                parts[index] = part;
                ((ListBoxItem)list.GetRealizedContainer(index)).Template =
                    new ControlTemplate(typeof(ListBoxItem), _ => part);
            }
            using var firstAttachment = StyleEngine.Attach(list.GetRealizedContainer(0), new[] { selectedStyle, currentStyle });
            using var secondAttachment = StyleEngine.Attach(list.GetRealizedContainer(1), new[] { selectedStyle, currentStyle });

            list.SelectedIndex = 0;
            Assert.Multiple(() =>
            {
                Assert.That(parts[0].TooltipText, Is.EqualTo("selected"));
                Assert.That(parts[0].Opacity, Is.EqualTo(.5f));
                Assert.That(parts[1].TooltipText, Is.EqualTo("base"));
                Assert.That(parts[1].Opacity, Is.EqualTo(1f));
            });

            list.CurrentIndex = 1;
            Assert.Multiple(() =>
            {
                Assert.That(parts[0].TooltipText, Is.EqualTo("selected"));
                Assert.That(parts[0].Opacity, Is.EqualTo(1f));
                Assert.That(parts[1].TooltipText, Is.EqualTo("base"));
                Assert.That(parts[1].Opacity, Is.EqualTo(.5f));
            });
            list.Dispose();
        }

        [Test]
        public void StyleTransition_AnimatesListBoxItemStateEntryAndExit()
        {
            var opacity = new XamlProperty<float>(nameof(Control.Opacity),
                target => ((Control)target).Opacity,
                (target, value) => ((Control)target).Opacity = value);
            var style = new Style("ListBoxItem:selected");
            style.AddSetter(new StyleSetter<float>(opacity, .4f));
            style.AddTransition(new FloatTransition(opacity, TimeSpan.FromSeconds(1)));
            var item = new ListBoxItem();
            using var attachment = StyleEngine.Attach(item, new[] { style });
            using var context = new UIContext();
            context.Add(item);

            item.SetSelectionState(true, false);
            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), default, new KeyboardState());
            context.Update(new GameTime(TimeSpan.FromSeconds(.5), TimeSpan.FromSeconds(.5)), default, new KeyboardState());
            Assert.That(item.Opacity, Is.EqualTo(.7f).Within(.001f));
            context.Update(new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(.5)), default, new KeyboardState());
            Assert.That(item.Opacity, Is.EqualTo(.4f).Within(.001f));

            item.SetSelectionState(false, false);
            context.Update(new GameTime(TimeSpan.FromSeconds(1), TimeSpan.Zero), default, new KeyboardState());
            context.Update(new GameTime(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(.5)), default, new KeyboardState());
            Assert.That(item.Opacity, Is.EqualTo(.7f).Within(.001f));
            context.Update(new GameTime(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(.5)), default, new KeyboardState());
            Assert.That(item.Opacity, Is.EqualTo(1f).Within(.001f));
        }

        [Test]
        public void ListBox_SingleSelectionPropertiesSupportTwoWayBindingAdapters()
        {
            var items = new object[] { "first", "second", "third" };
            var indexModel = new SelectionBindingModel { SelectedIndex = 1 };
            var indexList = new ListBox
            {
                DataContext = indexModel,
                ItemTemplate = DataTemplate.Create<object>((context, item) => new Control()),
                ItemsSource = items,
            };
            using var indexBinding = CompiledBinding.AttachTwoWay<SelectionBindingModel, int>(
                indexList,
                indexList,
                model => model.SelectedIndex,
                (model, value) => model.SelectedIndex = value,
                nameof(SelectionBindingModel.SelectedIndex),
                BindingTargetAdapters.ListBoxSelectedIndex);

            Assert.That(indexList.SelectedIndex, Is.EqualTo(1));
            indexList.SelectedIndex = 2;
            Assert.That(indexModel.SelectedIndex, Is.EqualTo(2));
            indexModel.SelectedIndex = 0;
            Assert.That(indexList.SelectedIndex, Is.Zero);

            var itemModel = new SelectionBindingModel { SelectedItem = items[1] };
            var itemList = new ListBox
            {
                DataContext = itemModel,
                ItemTemplate = DataTemplate.Create<object>((context, item) => new Control()),
                ItemsSource = items,
            };
            using var itemBinding = CompiledBinding.AttachTwoWay<SelectionBindingModel, object>(
                itemList,
                itemList,
                model => model.SelectedItem,
                (model, value) => model.SelectedItem = value,
                nameof(SelectionBindingModel.SelectedItem),
                BindingTargetAdapters.ListBoxSelectedItem);

            Assert.That(itemList.SelectedIndex, Is.EqualTo(1));
            itemList.SelectedIndex = 2;
            Assert.That(itemModel.SelectedItem, Is.SameAs(items[2]));
            itemModel.SelectedItem = items[0];
            Assert.That(itemList.SelectedIndex, Is.Zero);
            indexList.Dispose();
            itemList.Dispose();
        }

        [Test]
        public void ItemsControl_SnapshotsNonListSourcesOnlyOnAssignmentAndReset()
        {
            var source = new ObservableEnumerable("first", "second");
            var control = new ItemsControl
            {
                ItemTemplate = DataTemplate.Create<string>((context, item) => new TextBlock { Text = item }),
                ItemsSource = source,
            };

            source.Add("third");
            Assert.Multiple(() =>
            {
                Assert.That(source.EnumerationCount, Is.EqualTo(1));
                Assert.That(control.RealizedCount, Is.EqualTo(3));
            });

            source.Reset("replacement");
            Assert.Multiple(() =>
            {
                Assert.That(source.EnumerationCount, Is.EqualTo(2));
                Assert.That(control.RealizedCount, Is.EqualTo(1));
                Assert.That(control.GetRealizedContainer(0).DataContext, Is.EqualTo("replacement"));
            });
            control.Dispose();
        }

        [Test]
        public void ItemsControl_RejectsInvalidAndCrossThreadCollectionNotifications()
        {
            var source = new ObservableEnumerable("item");
            var control = new ItemsControl
            {
                ItemTemplate = DataTemplate.Create<string>((context, item) => new TextBlock { Text = item }),
                ItemsSource = source,
            };

            var invalid = Assert.Throws<InvalidOperationException>(() => source.RaiseInvalidRemove());
            Assert.That(invalid.Message, Does.Contain("outside the current item slots"));

            Exception threadFailure = null;
            var thread = new Thread(() =>
            {
                try { source.Add("wrong-thread"); }
                catch (Exception exception) { threadFailure = exception; }
            });
            thread.Start();
            thread.Join();
            Assert.Multiple(() =>
            {
                Assert.That(threadFailure, Is.TypeOf<InvalidOperationException>());
                Assert.That(threadFailure.Message, Does.Contain("thread where the source was attached"));
                Assert.That(control.RealizedCount, Is.EqualTo(1));
            });
            control.Dispose();
        }

        [Test]
        public void ItemsControl_InvokesContainerLifecycleHooks()
        {
            var source = new ObservableCollection<string> { "first", "second" };
            var control = new TrackingItemsControl
            {
                ItemTemplate = DataTemplate.Create<string>((context, item) => new TextBlock { Text = item }),
                ItemsSource = source,
            };

            Assert.That(control.Prepared, Is.EqualTo(new[] { "first", "second" }));
            source.RemoveAt(0);
            Assert.Multiple(() =>
            {
                Assert.That(control.Cleared, Is.EqualTo(new[] { "first" }));
                Assert.That(control.GetRealizedContainer(0).DataContext, Is.EqualTo("second"));
            });
            control.Dispose();
            Assert.That(control.Cleared, Is.EqualTo(new[] { "first", "second" }));
        }

        [Test]
        public void ItemsControl_RealizesNullOccurrencesWithExplicitTemplate()
        {
            var control = new ItemsControl
            {
                ItemTemplate = DataTemplate.Create<object>((context, item) => new TextBlock
                {
                    Text = item?.ToString() ?? "<null>",
                }),
                ItemsSource = new object[] { null, "value", null },
            };

            Assert.Multiple(() =>
            {
                Assert.That(control.RealizedCount, Is.EqualTo(3));
                Assert.That(GetPresentedText(control, 0).Text, Is.EqualTo("<null>"));
                Assert.That(GetPresentedText(control, 1).Text, Is.EqualTo("value"));
                Assert.That(GetPresentedText(control, 2).Text, Is.EqualTo("<null>"));
            });
            control.Dispose();
        }

        [Test]
        public void ItemsControl_PreservesTemplateIsolationInheritanceAndSubscriptions()
        {
            var first = new ObservableItem("first");
            var second = new ObservableItem("second");
            var source = new ObservableCollection<ObservableItem> { first, second };
            var styleBox = new StyleBoxFlat();
            var theme = new Theme();
            theme.SetStyleBox("row", styleBox, nameof(TextBlock));
            var control = new ItemsControl
            {
                ThemeOverride = theme,
                ItemTemplate = DataTemplate.Create<ObservableItem>((context, item) =>
                {
                    var text = new TextBlock { Name = "RowText" };
                    context.BindItem(text, item);
                    context.RegisterAttachment(CompiledBinding.AttachOneWay<ObservableItem, string>(
                        text,
                        text,
                        model => model.Name,
                        nameof(ObservableItem.Name),
                        target => ((TextBlock)target).Text,
                        (target, value) => ((TextBlock)target).Text = value));
                    return text;
                }),
                ItemsSource = source,
            };
            control.Resources["Accent"] = "owner-resource";
            using var context = new UIContext();
            context.Add(control);

            var firstContainer = (ContentControl)control.GetRealizedContainer(0);
            var secondContainer = (ContentControl)control.GetRealizedContainer(1);
            var firstText = GetPresentedText(control, 0);
            var secondText = GetPresentedText(control, 1);
            var firstScope = NameScope.GetNameScope(firstText);
            var secondScope = NameScope.GetNameScope(secondText);
            first.Name = "updated";

            Assert.Multiple(() =>
            {
                Assert.That(firstText.Text, Is.EqualTo("updated"));
                Assert.That(first.SubscriberCount, Is.EqualTo(1));
                Assert.That(second.SubscriberCount, Is.EqualTo(1));
                Assert.That(firstScope, Is.Not.SameAs(secondScope));
                Assert.That(firstScope.Find<TextBlock>("RowText"), Is.SameAs(firstText));
                Assert.That(secondScope.Find<TextBlock>("RowText"), Is.SameAs(secondText));
                Assert.That(firstContainer.Parent, Is.Null);
                Assert.That(firstContainer.InheritanceParent, Is.SameAs(control));
                Assert.That(firstText.Parent, Is.Null);
                Assert.That(firstText.InheritanceParent, Is.TypeOf<ContentPresenter>());
                Assert.That(firstText.TryFindResource("Accent", out var resource), Is.True);
                Assert.That(resource, Is.EqualTo("owner-resource"));
                Assert.That(firstText.GetThemeStyleBox("row"), Is.SameAs(styleBox));
            });

            context.Remove(control);
            Assert.Multiple(() =>
            {
                Assert.That(firstContainer.Context, Is.Null);
                Assert.That(firstText.Context, Is.Null);
            });
            context.Add(control);
            Assert.Multiple(() =>
            {
                Assert.That(control.GetRealizedContainer(0), Is.SameAs(firstContainer));
                Assert.That(firstContainer.Context, Is.SameAs(context));
                Assert.That(firstText.Context, Is.SameAs(context));
            });

            source.RemoveAt(0);
            Assert.Multiple(() =>
            {
                Assert.That(first.SubscriberCount, Is.Zero);
                Assert.That(NameScope.GetNameScope(firstText), Is.Null);
                Assert.That(second.SubscriberCount, Is.EqualTo(1));
                Assert.That(control.GetRealizedContainer(0), Is.SameAs(secondContainer));
            });
            control.Dispose();
            Assert.That(second.SubscriberCount, Is.Zero);
        }

        [Test]
        public void ItemsControl_PreservesSelectedOccurrenceIdentityDuringMutation()
        {
            var source = new ObservableCollection<string> { "first", "selected", "last" };
            var control = new ItemsControl
            {
                ItemTemplate = DataTemplate.Create<string>((context, item) => new TextBlock { Text = item }),
                ItemsSource = source,
            };
            var selectedOccurrence = control.GetRealizedContainer(1);

            source.Insert(0, "inserted");
            Assert.That(control.GetRealizedContainer(2), Is.SameAs(selectedOccurrence));
            source.Move(2, 0);
            Assert.That(control.GetRealizedContainer(0), Is.SameAs(selectedOccurrence));
            source.RemoveAt(0);
            Assert.Multiple(() =>
            {
                Assert.That(selectedOccurrence.VisualParent, Is.Null);
                Assert.That(control.RealizedCount, Is.EqualTo(3));
            });
            control.Dispose();
        }

        private static TextBlock GetPresentedText(ItemsControl control, int index)
        {
            var container = (ContentControl)control.GetRealizedContainer(index);
            return (TextBlock)((ContentPresenter)container.TemplateRoot).PresentedControl;
        }

        private sealed class ObservableEnumerable : IEnumerable, INotifyCollectionChanged
        {
            private readonly List<object> _items = new List<object>();

            public ObservableEnumerable(params object[] items) => _items.AddRange(items);

            public int EnumerationCount { get; private set; }
            public event NotifyCollectionChangedEventHandler CollectionChanged;

            public void Add(object item)
            {
                var index = _items.Count;
                _items.Add(item);
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Add, item, index));
            }

            public void Reset(params object[] items)
            {
                _items.Clear();
                _items.AddRange(items);
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }

            public void RaiseInvalidRemove() => CollectionChanged?.Invoke(this,
                new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, "missing", 7));

            public IEnumerator GetEnumerator()
            {
                EnumerationCount++;
                return _items.GetEnumerator();
            }
        }

        private sealed class TrackingItemsControl : ItemsControl
        {
            public List<object> Prepared { get; } = new List<object>();
            public List<object> Cleared { get; } = new List<object>();

            protected override void PrepareContainerForItem(Control container, object item)
            {
                base.PrepareContainerForItem(container, item);
                Prepared.Add(item);
            }

            protected override void ClearContainerForItem(Control container, object item)
            {
                Cleared.Add(item);
                base.ClearContainerForItem(container, item);
            }
        }

        private sealed class ObservableItem : INotifyPropertyChanged
        {
            private string _name;
            private PropertyChangedEventHandler _propertyChanged;

            public ObservableItem(string name) => _name = name;

            public string Name
            {
                get => _name;
                set
                {
                    if (_name == value) return;
                    _name = value;
                    _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
                }
            }

            public int SubscriberCount { get; private set; }

            public event PropertyChangedEventHandler PropertyChanged
            {
                add { _propertyChanged += value; SubscriberCount++; }
                remove { _propertyChanged -= value; SubscriberCount--; }
            }
        }

        private sealed class SelectionBindingModel : INotifyPropertyChanged
        {
            private int _selectedIndex = -1;
            private object _selectedItem;

            public int SelectedIndex
            {
                get => _selectedIndex;
                set
                {
                    if (_selectedIndex == value) return;
                    _selectedIndex = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedIndex)));
                }
            }

            public object SelectedItem
            {
                get => _selectedItem;
                set
                {
                    if (ReferenceEquals(_selectedItem, value)) return;
                    _selectedItem = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedItem)));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }
    }
}