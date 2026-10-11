// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Forma.Xaml;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System.Collections.ObjectModel;

namespace Forma.Tests
{
    public sealed class VirtualizingPanelsTest
    {
        [Test]
        public void ItemsControl_VirtualizingPanelRealizesOnlyViewportFromLargeSource()
        {
            using var context = new UIContext();
            var templateCalls = 0;
            var control = new ItemsControl
            {
                ItemsPanel = new ItemsPanelTemplate(_ => new VirtualizingStackPanel { EstimatedItemExtent = 20 }),
                ItemTemplate = DataTemplate.Create<int>((_, item) =>
                {
                    templateCalls++;
                    return new Control { CustomMinimumSize = new Vector2(80, 20), DataContext = item };
                }),
                ItemsSource = Enumerable.Range(0, 100_000).ToArray(),
                Size = new Vector2(100, 60),
            };
            context.Add(control);

            context.Layout();

            var presenter = (ItemsPresenter)control.TemplateRoot;
            var panel = (VirtualizingStackPanel)presenter.Panel;
            Assert.Multiple(() =>
            {
                Assert.That(control.RealizedCount, Is.LessThanOrEqualTo(4));
                Assert.That(panel.RealizedCount, Is.EqualTo(control.RealizedCount));
                Assert.That(templateCalls, Is.EqualTo(control.RealizedCount));
                Assert.That(control.GetRealizedContainer(0).DataContext, Is.EqualTo(0));
                Assert.Throws<InvalidOperationException>(() => control.GetRealizedContainer(50_000));
                Assert.That(panel.GetMinimumSize().Y, Is.EqualTo(2_000_000));
            });
            control.Dispose();
        }

        [Test]
        public void ItemsControl_LargeIndexedSourceScrollsWithoutEnumerationOrAllocationGrowth()
        {
            using var context = new UIContext { ViewportSize = new Vector2(100, 60) };
            var source = new CountingList(100_000);
            var rows = new List<RecyclingRow>();
            var (control, panel, owner, presenter) = CreateVirtualizedItems(context, source, CreateRecyclingTemplate(rows));
            panel.OverscanBefore = 1;
            panel.OverscanAfter = 2;
            panel.QueueLayout();
            context.Layout();
            var initialFactoryCalls = rows.Count;

            foreach (var offset in new[] { 2_000f, 20_000f, 200_000f, 1_000_000f, 400_000f, 40_000f, 2_000f })
            {
                owner.ScrollOffset = new Vector2(0, offset);
                panel.QueueLayout();
                context.Layout();
                Assert.That(control.RealizedCount, Is.LessThanOrEqualTo(7));
            }

            Assert.Multiple(() =>
            {
                Assert.That(rows.Count, Is.LessThanOrEqualTo(initialFactoryCalls + 3));
                Assert.That(control.RealizedCount + control.RecycledCount, Is.LessThanOrEqualTo(7));
                Assert.That(source.EnumerationCount, Is.Zero);
                Assert.That(panel.GetMinimumSize().Y, Is.EqualTo(2_000_000));
            });
            control.Dispose();
            presenter.Dispose();
        }

        [Test]
        public void ItemsControl_VisibleOccurrenceMovedOffscreenRecyclesWithoutGrowingAllocation()
        {
            using var context = new UIContext { ViewportSize = new Vector2(100, 60) };
            var source = new ObservableCollection<int>(Enumerable.Range(0, 100));
            var rows = new List<RecyclingRow>();
            var (control, panel, _, presenter) = CreateVirtualizedItems(context, source, CreateRecyclingTemplate(rows));
            panel.QueueLayout();
            context.Layout();
            var initialFactoryCalls = rows.Count;

            source.Move(2, 90);
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(rows.Count, Is.EqualTo(initialFactoryCalls));
                Assert.That(control.RealizedCount, Is.LessThanOrEqualTo(4));
                Assert.That(control.GetRealizedContainer(0).DataContext, Is.EqualTo(0));
                Assert.Throws<InvalidOperationException>(() => control.GetRealizedContainer(90));
            });
            control.Dispose();
            presenter.Dispose();
        }

        [Test]
        public void ItemsControl_AccessibilityPeersPreserveOccurrenceIdentityAcrossMoveAndRealization()
        {
            using var context = new UIContext { ViewportSize = new Vector2(100, 60) };
            var source = new ObservableCollection<int>(Enumerable.Repeat(7, 100));
            var rows = new List<RecyclingRow>();
            var (control, panel, owner, presenter) = CreateVirtualizedItems(context, source, CreateRecyclingTemplate(rows));
            panel.QueueLayout();
            context.Layout();

            var movedPeer = (ItemAccessibilityPeer)control.AccessibilityPeer.Children[2];
            var duplicatePeer = control.AccessibilityPeer.Children[3];
            Assert.Multiple(() =>
            {
                Assert.That(movedPeer.Item, Is.EqualTo(7));
                Assert.That(movedPeer.IsOffscreen, Is.False);
                Assert.That(movedPeer.Bounds, Is.Not.EqualTo(Rectangle.Empty));
                Assert.That(movedPeer, Is.Not.SameAs(duplicatePeer));
            });

            source.Move(2, 90);
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(control.AccessibilityPeer.Children[90], Is.SameAs(movedPeer));
                Assert.That(movedPeer.Index, Is.EqualTo(90));
                Assert.That(movedPeer.IsOffscreen, Is.True);
                Assert.That(movedPeer.Bounds, Is.EqualTo(Rectangle.Empty));
            });

            owner.ScrollOffset = new Vector2(0, 90 * 20);
            panel.QueueLayout();
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(control.AccessibilityPeer.Children[90], Is.SameAs(movedPeer));
                Assert.That(movedPeer.IsOffscreen, Is.False);
                Assert.That(movedPeer.Bounds, Is.Not.EqualTo(Rectangle.Empty));
            });
            control.Dispose();
            presenter.Dispose();
        }

        [Test]
        public void ItemsControl_VirtualizingPanelConsumesObservableInsertAsIndexedDelta()
        {
            using var context = new UIContext();
            var source = new ObservableCollection<int>(Enumerable.Range(0, 100));
            var control = new ItemsControl
            {
                ItemsPanel = new ItemsPanelTemplate(_ => new VirtualizingStackPanel { EstimatedItemExtent = 20 }),
                ItemTemplate = DataTemplate.Create<int>((_, item) => new Control { CustomMinimumSize = new Vector2(80, 20), DataContext = item }),
                ItemsSource = source,
                Size = new Vector2(100, 60),
            };
            context.Add(control);
            context.Layout();

            source.Insert(0, -1);
            context.Layout();

            var panel = (VirtualizingStackPanel)((ItemsPresenter)control.TemplateRoot).Panel;
            Assert.Multiple(() =>
            {
                Assert.That(control.RealizedCount, Is.LessThanOrEqualTo(4));
                Assert.That(control.GetRealizedContainer(0).DataContext, Is.EqualTo(-1));
                Assert.That(panel.GetMinimumSize().Y, Is.EqualTo(2_020));
            });
            control.Dispose();
        }

        [Test]
        public void VirtualizingGridPanel_RealizesViewportRowsAndMirrorsColumnsInRtl()
        {
            using var context = new UIContext();
            var generator = new CountingGenerator(1_000, _ => new Vector2(80, 20));
            var panel = new VirtualizingGridPanel
            {
                Generator = generator,
                CellWidth = 100,
                CellHeight = 20,
                ColumnGap = 10,
                RowGap = 5,
                OverscanRows = 1,
                Size = new Vector2(220, 100),
            };
            context.Add(panel);
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(panel.ColumnCount, Is.EqualTo(2));
                Assert.That(panel.RealizedCount, Is.EqualTo(12));
                Assert.That(generator.RealizeCount, Is.EqualTo(12));
                Assert.That(panel.GetMinimumSize(), Is.EqualTo(new Vector2(210, 12_495)));
                Assert.That(panel.TryGetIndexBounds(3, out var bounds), Is.True);
                Assert.That(bounds, Is.EqualTo(new Rectangle(110, 25, 100, 20)));
            });

            panel.LayoutDirection = LayoutDirection.RightToLeft;
            context.Layout();
            Assert.That(panel.TryGetIndexBounds(3, out var mirrored), Is.True);
            Assert.That(mirrored.X, Is.Zero);
        }

        [Test]
        public void VirtualizingStackPanel_AppliesGeneratorInsertWithoutRealizingWholeSource()
        {
            using var context = new UIContext();
            var generator = new CountingGenerator(100, _ => new Vector2(40, 20));
            var panel = new VirtualizingStackPanel
            {
                Generator = generator,
                EstimatedItemExtent = 20,
                Size = new Vector2(80, 60),
            };
            context.Add(panel);
            context.Layout();
            var realizedBefore = generator.RealizeCount;

            generator.Insert(10, 2);
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(panel.GetMinimumSize().Y, Is.EqualTo(2_040));
                Assert.That(generator.RealizeCount - realizedBefore, Is.LessThanOrEqualTo(4));
                Assert.That(panel.RealizedCount, Is.LessThanOrEqualTo(4));
            });
        }

        [Test]
        public void VirtualizingPanels_ReuseRollingEstimateWithinGeneratorScope()
        {
            using var context = new UIContext();
            var scope = new object();
            var first = new VirtualizingStackPanel
            {
                Generator = new CountingGenerator(100, _ => new Vector2(40, 48), scope),
                Size = new Vector2(80, 96),
            };
            context.Add(first);
            context.Layout();

            var second = new VirtualizingStackPanel
            {
                Generator = new CountingGenerator(100, _ => new Vector2(40, 48), scope),
            };
            var explicitlyEstimated = new VirtualizingStackPanel
            {
                EstimatedItemExtent = VirtualizingStackPanel.DefaultEstimatedItemExtent,
                Generator = new CountingGenerator(100, _ => new Vector2(40, 48), scope),
            };

            Assert.Multiple(() =>
            {
                Assert.That(second.EstimatedItemExtent, Is.EqualTo(48));
                Assert.That(second.GetMinimumSize().Y, Is.EqualTo(4_800));
                Assert.That(explicitlyEstimated.EstimatedItemExtent, Is.EqualTo(32));
                Assert.That(explicitlyEstimated.GetMinimumSize().Y, Is.EqualTo(3_200));
            });
        }

        [Test]
        public void VirtualizingStackPanel_RealizesOnlyViewportRangeAndReportsVariableBounds()
        {
            using var context = new UIContext();
            var generator = new CountingGenerator(100, index => new Vector2(40, index == 2 ? 30 : 20));
            var panel = new VirtualizingStackPanel
            {
                Generator = generator,
                EstimatedItemExtent = 20,
                OverscanAfter = 1,
                Size = new Vector2(80, 60),
            };
            context.Add(panel);

            context.Layout();
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(panel.RealizedCount, Is.LessThanOrEqualTo(5));
                Assert.That(generator.RealizeCount, Is.EqualTo(panel.RealizedCount));
                Assert.That(generator.RealizeCount, Is.LessThan(generator.Count));
                Assert.That(panel.TryGetIndexBounds(2, out var bounds), Is.True);
                Assert.That(bounds.Y, Is.EqualTo(40));
                Assert.That(bounds.Height, Is.EqualTo(30));
                Assert.That(panel.GetMinimumSize().Y, Is.GreaterThan(2_000));
            });
        }

        [Test]
        public void VirtualizingStackPanel_SupportsHorizontalBoundsAndRtlMirroring()
        {
            using var context = new UIContext();
            var generator = new CountingGenerator(20, _ => new Vector2(25, 18));
            var panel = new VirtualizingStackPanel
            {
                Generator = generator,
                Orientation = Orientation.Horizontal,
                EstimatedItemExtent = 25,
                Gap = 5,
                Size = new Vector2(90, 24),
            };
            context.Add(panel);
            context.Layout();
            context.Layout();

            Assert.That(panel.TryGetIndexBounds(1, out var leftToRight), Is.True);
            Assert.That(leftToRight.X, Is.EqualTo(30));
            panel.LayoutDirection = LayoutDirection.RightToLeft;
            context.Layout();
            Assert.That(panel.TryGetIndexBounds(1, out var rightToLeft), Is.True);
            Assert.That(rightToLeft.X, Is.GreaterThan(leftToRight.X));
        }

        [Test]
        public void VirtualizingStackPanel_PreservesAnchorAcrossMeasuredCorrection()
        {
            using var context = new UIContext { ViewportSize = new Vector2(100, 60) };
            var generator = new CountingGenerator(100, index => new Vector2(80, index == 2 ? 40 : 20));
            var logicalOwner = new Control();
            var panel = new VirtualizingStackPanel { Generator = generator, EstimatedItemExtent = 20 };
            logicalOwner.AddChild(panel);
            var owner = new ScrollOwner { ScrollOffset = new Vector2(0, 45) };
            var presenter = new ScrollPresenter { Size = new Vector2(100, 60), Owner = owner, Content = panel };
            context.Add(logicalOwner);
            context.Add(presenter);

            context.Layout();

            Assert.That(owner.ScrollOffset.Y, Is.GreaterThan(45));
            Assert.That(panel.TryGetIndexBounds(2, out var corrected), Is.True);
            Assert.That(owner.ScrollOffset.Y - corrected.Y, Is.EqualTo(5).Within(1));
        }

        [Test]
        public void ItemsControl_RecyclesRowsAcrossScrollingAndResetsFocusedState()
        {
            using var context = new UIContext { ViewportSize = new Vector2(100, 60) };
            var rows = new List<RecyclingRow>();
            var template = DataTemplate.Create<int>((build, item) =>
            {
                var row = new RecyclingRow { CustomMinimumSize = new Vector2(80, 20) };
                build.BindItem(row, item);
                build.RegisterLifecycle(row.Activate, row.Deactivate);
                rows.Add(row);
                return row;
            });
            var control = new ItemsControl
            {
                ItemsPanel = new ItemsPanelTemplate(_ => new VirtualizingStackPanel { EstimatedItemExtent = 20 }),
                ItemTemplate = template,
                ItemsSource = Enumerable.Range(0, 100_000).ToArray(),
            };
            var logicalOwner = new Control();
            logicalOwner.AddChild(control);
            var owner = new ScrollOwner();
            var presenter = new ScrollPresenter { Owner = owner, Content = control, Size = new Vector2(100, 60) };
            context.Add(logicalOwner);
            context.Add(presenter);
            context.Layout();
            context.Layout();
            var initialFactoryCalls = rows.Count;
            var firstRow = GetRecyclingRow(control.GetRealizedContainer(0));
            context.SetFocus(firstRow);
            var panel = (VirtualizingStackPanel)((ItemsPresenter)control.TemplateRoot).Panel;

            owner.ScrollOffset = new Vector2(0, 400);
            panel.QueueLayout();
            context.Layout();
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(rows, Has.Count.EqualTo(initialFactoryCalls));
                Assert.That(rows.Sum(row => row.RecyclingCalls), Is.EqualTo(initialFactoryCalls));
                Assert.That(rows.Sum(row => row.ReusedCalls), Is.EqualTo(initialFactoryCalls));
                Assert.That(rows.All(row => row.ActivationCalls >= 2), Is.True);
                Assert.That(rows.All(row => row.DeactivationCalls >= 1), Is.True);
                Assert.That(GetRecyclingRow(control.GetRealizedContainer(20)).DataContext, Is.EqualTo(20));
                Assert.That(context.FocusedControl, Is.Null);
                Assert.That(panel.RecycledCount, Is.Zero);
                Assert.That(panel.PinnedCount, Is.Zero);
            });
            control.Dispose();
            presenter.Dispose();
        }

        [Test]
        public void ListBox_RecyclingClearsContainerSelectionAndSelectabilityState()
        {
            using var context = new UIContext { ViewportSize = new Vector2(100, 60) };
            var opacity = new XamlProperty<float>(nameof(Control.Opacity),
                target => ((Control)target).Opacity,
                (target, value) => ((Control)target).Opacity = value);
            var selectedStyle = new Style("ListBoxItem:selected");
            selectedStyle.AddSetter(new StyleSetter<float>(opacity, .4f));
            selectedStyle.AddTransition(new FloatTransition(opacity, TimeSpan.FromSeconds(1)));
            var list = new ListBox
            {
                ItemsPanel = new ItemsPanelTemplate(_ => new VirtualizingStackPanel { EstimatedItemExtent = 20 }),
                ItemTemplate = CreateRecyclingTemplate(new List<RecyclingRow>()),
                ItemContainerStyle = selectedStyle,
                ItemsSource = Enumerable.Range(0, 100).ToArray(),
                Size = new Vector2(100, 60),
            };
            context.Add(list);
            context.Layout();
            context.Layout();
            var panel = (VirtualizingStackPanel)((ItemsPresenter)list.GetTemplateChild(ListBox.ItemsPresenterPartName)).Panel;
            var initialContainers = panel.VisualChildren.Cast<ListBoxItem>().ToArray();
            Assert.That(initialContainers, Is.Not.Empty);
            list.SelectedIndex = 0;
            foreach (var item in initialContainers) item.IsSelectable = false;
            context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), default, new KeyboardState());
            context.Update(new GameTime(TimeSpan.FromSeconds(.5), TimeSpan.FromSeconds(.5)), default, new KeyboardState());
            Assert.That(list.GetRealizedContainer(0).Opacity, Is.EqualTo(.7f).Within(.001f));

            list.ScrollOffset = new Vector2(0, 400);
            context.Layout();
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(list.SelectedIndex, Is.Zero);
                Assert.That(list.CurrentIndex, Is.Zero);
                Assert.That(panel.VisualChildren.Cast<ListBoxItem>().All(initialContainers.Contains), Is.True);
                Assert.That(panel.VisualChildren.Cast<ListBoxItem>().All(item => item.IsSelectable), Is.True);
                Assert.That(panel.VisualChildren.Cast<ListBoxItem>().All(item => !item.IsSelected && !item.IsCurrent), Is.True);
                Assert.That(panel.VisualChildren.Cast<ListBoxItem>().All(item => Math.Abs(item.Opacity - 1f) < .001f), Is.True);
            });
            list.Dispose();
        }

        [Test]
        public void ListBox_ScrollsWithThePointerWheelOverItsRows()
        {
            using var context = new UIContext { ViewportSize = new Vector2(100, 60) };
            var list = new ListBox
            {
                ItemsPanel = new ItemsPanelTemplate(_ => new VirtualizingStackPanel { EstimatedItemExtent = 20 }),
                ItemTemplate = CreateRecyclingTemplate(new List<RecyclingRow>()),
                ItemsSource = Enumerable.Range(0, 100).ToArray(),
                Size = new Vector2(100, 60),
            };
            context.Add(list);
            context.Layout();
            context.Layout();

            Assert.That(context.InjectPointerWheel(new Point(50, 30), -120), Is.True, "a wheel notch over the rows scrolls the list");
            var down = list.ScrollOffset.Y;
            Assert.That(down, Is.GreaterThan(0));
            context.InjectPointerWheel(new Point(50, 30), 120);
            Assert.That(list.ScrollOffset.Y, Is.LessThan(down), "the opposite notch scrolls back");
            for (var notch = 0; notch < 40; notch++) context.InjectPointerWheel(new Point(50, 30), 120);
            Assert.That(list.ScrollOffset.Y, Is.Zero, "scrolling back past the top stops at the top");
            list.Dispose();
        }

        [Test]
        public void ListBox_VirtualizationPreservesDuplicateSlotSelectionAcrossMutations()
        {
            using var context = new UIContext { ViewportSize = new Vector2(100, 60) };
            var duplicate = new object();
            var source = new ObservableCollection<object>(Enumerable.Range(0, 100).Select(index => (object)index));
            source[0] = duplicate;
            source[1] = duplicate;
            var list = new ListBox
            {
                ItemsPanel = new ItemsPanelTemplate(_ => new VirtualizingStackPanel { EstimatedItemExtent = 20 }),
                ItemTemplate = DataTemplate.Create<object>((build, item) =>
                {
                    var row = new RecyclingRow { CustomMinimumSize = new Vector2(80, 20) };
                    build.BindItem(row, item);
                    return row;
                }),
                ItemsSource = source,
                Size = new Vector2(100, 60),
            };
            context.Add(list);
            context.Layout();
            var panel = (VirtualizingStackPanel)((ItemsPresenter)list.GetTemplateChild(ListBox.ItemsPresenterPartName)).Panel;
            list.SelectedIndex = 1;
            list.ScrollOffset = new Vector2(0, 400);
            panel.QueueLayout();
            context.Layout();

            source.Move(1, 30);
            Assert.Multiple(() =>
            {
                Assert.That(list.SelectedIndex, Is.EqualTo(30));
                Assert.That(list.CurrentIndex, Is.EqualTo(30));
                Assert.That(list.SelectedItem, Is.SameAs(duplicate));
                Assert.That(panel.VisualChildren.Cast<ListBoxItem>().All(item => !item.IsSelected && !item.IsCurrent), Is.True);
            });

            source[30] = new object();
            Assert.Multiple(() =>
            {
                Assert.That(list.HasSelection, Is.False);
                Assert.That(list.CurrentIndex, Is.EqualTo(-1));
            });

            list.SelectedIndex = 20;
            source.Clear();
            Assert.That(list.HasSelection, Is.False);
            source.Add(duplicate);
            list.SelectedIndex = 0;
            list.ItemsSource = new object[] { duplicate };
            Assert.Multiple(() =>
            {
                Assert.That(list.HasSelection, Is.False);
                Assert.That(list.CurrentIndex, Is.EqualTo(-1));
            });
            list.Dispose();
        }

        [Test]
        public void ListBox_RestoresFocusedSelectedItemAfterVirtualizedUnrealization()
        {
            using var context = new UIContext { ViewportSize = new Vector2(100, 60) };
            var list = new ListBox
            {
                ItemsPanel = new ItemsPanelTemplate(_ => new VirtualizingStackPanel { EstimatedItemExtent = 20 }),
                ItemTemplate = CreateFocusTemplate(new List<FocusRow>()),
                ItemsSource = Enumerable.Range(0, 100).ToArray(),
                Size = new Vector2(100, 60),
            };
            context.Add(list);
            context.Layout();
            var panel = (VirtualizingStackPanel)((ItemsPresenter)list.GetTemplateChild(ListBox.ItemsPresenterPartName)).Panel;
            list.SelectedIndex = 0;
            context.SetFocus(GetFocusRow(list.GetRealizedContainer(0)).Editor);

            list.ScrollOffset = new Vector2(0, 400);
            panel.QueueLayout();
            context.Layout();
            Assert.Multiple(() =>
            {
                Assert.That(context.FocusedControl, Is.SameAs(list));
                Assert.That(list.SelectedIndex, Is.Zero);
                Assert.That(list.CurrentIndex, Is.Zero);
            });

            list.ScrollOffset = Vector2.Zero;
            panel.QueueLayout();
            context.Layout();
            var restoredItem = (ListBoxItem)list.GetRealizedContainer(0);
            Assert.Multiple(() =>
            {
                Assert.That(context.FocusedControl, Is.SameAs(GetFocusRow(restoredItem).Editor));
                Assert.That(restoredItem.IsSelected, Is.True);
                Assert.That(restoredItem.IsCurrent, Is.True);
            });
            list.Dispose();
        }

        [Test]
        public void ListBox_VirtualizingGridNavigationHonorsRtlAndSelectionState()
        {
            using var context = new UIContext { ViewportSize = new Vector2(120, 60) };
            var list = new ListBox
            {
                LayoutDirection = LayoutDirection.RightToLeft,
                SelectionMode = ItemListSelectionMode.Multi,
                ItemsPanel = new ItemsPanelTemplate(_ => new VirtualizingGridPanel
                {
                    CellWidth = 50,
                    CellHeight = 20,
                }),
                ItemTemplate = DataTemplate.Create<int>((build, item) =>
                {
                    var row = new RecyclingRow { CustomMinimumSize = new Vector2(50, 20) };
                    build.BindItem(row, item);
                    return row;
                }),
                ItemsSource = Enumerable.Range(0, 100).ToArray(),
                Size = new Vector2(120, 60),
            };
            context.Add(list);
            context.Layout();
            var grid = (VirtualizingGridPanel)((ItemsPresenter)list.GetTemplateChild(ListBox.ItemsPresenterPartName)).Panel;
            Assert.That(grid.ColumnCount, Is.EqualTo(2));

            list.CurrentIndex = 1;
            list.KeyPressed(Keys.Left);
            Assert.That(list.CurrentIndex, Is.EqualTo(2));
            list.KeyPressed(Keys.Right);
            Assert.That(list.CurrentIndex, Is.EqualTo(1));
            list.KeyPressed(Keys.Down);
            Assert.That(list.CurrentIndex, Is.EqualTo(3));
            list.KeyPressed(Keys.Space);
            Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 3 }));
            list.Dispose();
        }

        [Test]
        public void ItemsControl_DoesNotPoolApplicationRowsWithoutRecyclingContract()
        {
            using var context = new UIContext { ViewportSize = new Vector2(100, 60) };
            var rows = new List<NonRecyclableRow>();
            var control = new ItemsControl
            {
                ItemsPanel = new ItemsPanelTemplate(_ => new VirtualizingStackPanel { EstimatedItemExtent = 20 }),
                ItemTemplate = DataTemplate.Create<int>((_, item) =>
                {
                    var row = new NonRecyclableRow { CustomMinimumSize = new Vector2(80, 20), DataContext = item };
                    rows.Add(row);
                    return row;
                }),
                ItemsSource = Enumerable.Range(0, 100).ToArray(),
            };
            var logicalOwner = new Control();
            logicalOwner.AddChild(control);
            var owner = new ScrollOwner();
            var presenter = new ScrollPresenter { Owner = owner, Content = control, Size = new Vector2(100, 60) };
            context.Add(logicalOwner);
            context.Add(presenter);
            context.Layout();
            var initialFactoryCalls = rows.Count;
            var panel = (VirtualizingStackPanel)((ItemsPresenter)control.TemplateRoot).Panel;

            owner.ScrollOffset = new Vector2(0, 400);
            panel.QueueLayout();
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(rows.Count, Is.GreaterThan(initialFactoryCalls));
                Assert.That(rows.Take(initialFactoryCalls).All(row => row.IsDisposed), Is.True);
                Assert.That(control.RecycledCount, Is.Zero);
            });
            control.Dispose();
            presenter.Dispose();
        }

        [Test]
        public void ItemsControl_BoundsAndDrainsObsoleteRecyclePoolVersions()
        {
            using var context = new UIContext { ViewportSize = new Vector2(100, 60) };
            var firstRows = new List<RecyclingRow>();
            var firstTemplate = CreateRecyclingTemplate(firstRows);
            var control = new ItemsControl
            {
                RecyclePoolCapacity = 2,
                ItemsPanel = new ItemsPanelTemplate(_ => new VirtualizingStackPanel { EstimatedItemExtent = 20 }),
                ItemTemplate = firstTemplate,
                ItemsSource = Enumerable.Range(0, 100).ToArray(),
                Size = new Vector2(100, 60),
            };
            context.Add(control);
            context.Layout();
            var initiallyRealized = control.RealizedCount;

            control.ItemsSource = Array.Empty<int>();
            var panel = (VirtualizingStackPanel)((ItemsPresenter)control.TemplateRoot).Panel;
            Assert.Multiple(() =>
            {
                Assert.That(control.RecycledCount, Is.EqualTo(2));
                Assert.That(panel.RecycledCount, Is.EqualTo(2));
                Assert.That(firstRows.Count(row => row.IsDisposed), Is.EqualTo(initiallyRealized - 2));
            });

            var secondRows = new List<RecyclingRow>();
            control.ItemTemplate = CreateRecyclingTemplate(secondRows);
            Assert.That(control.RecycledCount, Is.Zero);
            Assert.That(firstRows.All(row => row.IsDisposed), Is.True);

            control.ItemsSource = Enumerable.Range(0, 100).ToArray();
            context.Layout();
            control.ItemsSource = Array.Empty<int>();
            Assert.That(control.RecycledCount, Is.EqualTo(2));
            context.Theme = new Theme();
            Assert.Multiple(() =>
            {
                Assert.That(control.RecycledCount, Is.Zero);
                Assert.That(secondRows.All(row => row.IsDisposed), Is.True);
            });
            control.Dispose();
        }

        [Test]
        public void VirtualizingPanel_RetainsMultipleExplicitPinsUntilReleased()
        {
            using var context = new UIContext();
            var pins = new Dictionary<int, PinnedControl>();
            var generator = new CountingGenerator(100, _ => new Vector2(80, 20), factory: index =>
            {
                var control = new PinnedControl { CustomMinimumSize = new Vector2(80, 20) };
                pins[index] = control;
                return control;
            });
            var panel = new VirtualizingStackPanel
            {
                Generator = generator,
                EstimatedItemExtent = 20,
            };
            var logicalOwner = new Control();
            logicalOwner.AddChild(panel);
            var owner = new ScrollOwner();
            var presenter = new ScrollPresenter { Owner = owner, Content = panel, Size = new Vector2(100, 60) };
            context.Add(logicalOwner);
            context.Add(presenter);
            context.Layout();
            pins[0].IsVirtualizationPinned = true;
            pins[1].IsVirtualizationPinned = true;

            presenter.Size = new Vector2(100, 20);
            owner.ScrollOffset = new Vector2(0, 400);
            panel.QueueLayout();
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(panel.PinnedCount, Is.EqualTo(2));
                Assert.That(panel.RealizedContainers.ContainsKey(0), Is.True);
                Assert.That(panel.RealizedContainers.ContainsKey(1), Is.True);
            });

            pins[0].IsVirtualizationPinned = false;
            pins[1].IsVirtualizationPinned = false;
            panel.QueueLayout();
            context.Layout();
            Assert.Multiple(() =>
            {
                Assert.That(panel.PinnedCount, Is.Zero);
                Assert.That(panel.RealizedContainers.ContainsKey(0), Is.False);
                Assert.That(panel.RealizedContainers.ContainsKey(1), Is.False);
            });
            presenter.Dispose();
        }

        [Test]
        public void VirtualizingPanel_PinsPointerCapturedContainerUntilRelease()
        {
            using var context = new UIContext { ViewportSize = new Vector2(100, 60) };
            var template = DataTemplate.Create<int>((_, item) => new Control
            {
                CustomMinimumSize = new Vector2(80, 20),
                DataContext = item,
            });
            var (control, panel, owner, presenter) = CreateVirtualizedItems(context, Enumerable.Range(0, 100).ToArray(), template);
            var time = new GameTime();
            context.Update(time, new MouseState(10, 10, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());
            context.Update(time, new MouseState(10, 10, 0, ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());

            owner.ScrollOffset = new Vector2(0, 400);
            panel.QueueLayout();
            context.Layout();
            Assert.Multiple(() =>
            {
                Assert.That(panel.PinnedCount, Is.EqualTo(1));
                Assert.That(panel.RealizedContainers.ContainsKey(0), Is.True);
            });

            context.Update(time, new MouseState(10, 10, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());
            panel.QueueLayout();
            context.Layout();
            Assert.Multiple(() =>
            {
                Assert.That(panel.PinnedCount, Is.Zero);
                Assert.That(panel.RealizedContainers.ContainsKey(0), Is.False);
            });
            control.Dispose();
            presenter.Dispose();
        }

        [Test]
        public void VirtualizingStackPanel_PreservesTokenAnchorAcrossIndexedChangesAboveViewport()
        {
            using var context = new UIContext { ViewportSize = new Vector2(100, 60) };
            var source = new ObservableCollection<int>(Enumerable.Range(0, 100));
            var (control, panel, owner, presenter) = CreateVirtualizedItems(context, source, DataTemplate.Create<int>((_, item) =>
                new Control { CustomMinimumSize = new Vector2(80, 20), DataContext = item }));
            owner.ScrollOffset = new Vector2(0, 205);
            panel.QueueLayout();
            context.Layout();

            source.Insert(0, -1);
            Assert.That(owner.ScrollOffset.Y, Is.EqualTo(225));
            panel.QueueLayout();
            context.Layout();
            source.Move(0, 15);
            Assert.That(owner.ScrollOffset.Y, Is.EqualTo(205));
            panel.QueueLayout();
            context.Layout();
            source.RemoveAt(0);

            Assert.Multiple(() =>
            {
                Assert.That(owner.ScrollOffset.Y, Is.EqualTo(185));
                Assert.That(control.RealizedCount, Is.LessThanOrEqualTo(4));
            });
            control.Dispose();
            presenter.Dispose();
        }

        [Test]
        public void VirtualizingStackPanel_UsesConfiguredKeyOnReplacementAndClampsRawResetOffset()
        {
            using var context = new UIContext { ViewportSize = new Vector2(100, 60) };
            var original = Enumerable.Range(0, 100).Select(index => new KeyedRow(index)).ToArray();
            var template = DataTemplate.Create<KeyedRow>((_, item) =>
                new Control { CustomMinimumSize = new Vector2(80, 20), DataContext = item });
            var (control, panel, owner, presenter) = CreateVirtualizedItems(context, original, template);
            control.ItemKeySelector = item => ((KeyedRow)item).Id;
            owner.ScrollOffset = new Vector2(0, 205);
            panel.QueueLayout();
            context.Layout();

            var replacement = Enumerable.Range(100, 20)
                .Select(index => new KeyedRow(index))
                .Concat(new[] { new KeyedRow(10) })
                .Concat(Enumerable.Range(200, 20).Select(index => new KeyedRow(index)))
                .ToArray();
            control.ItemsSource = replacement;
            Assert.That(owner.ScrollOffset.Y, Is.EqualTo(405));

            control.ItemKeySelector = null;
            panel.QueueLayout();
            context.Layout();
            control.ItemsSource = Enumerable.Range(0, 5).Select(index => new KeyedRow(index)).ToArray();
            Assert.That(owner.ScrollOffset.Y, Is.EqualTo(40));
            control.Dispose();
            presenter.Dispose();
        }

        [Test]
        public void VirtualizingGridPanel_PreservesRowAnchorAcrossInsertAboveViewport()
        {
            using var context = new UIContext { ViewportSize = new Vector2(220, 40) };
            var source = new ObservableCollection<int>(Enumerable.Range(0, 100));
            var control = new ItemsControl
            {
                ItemsPanel = new ItemsPanelTemplate(_ => new VirtualizingGridPanel { CellWidth = 100, CellHeight = 20 }),
                ItemTemplate = DataTemplate.Create<int>((_, item) => new Control { DataContext = item }),
                ItemsSource = source,
            };
            var logicalOwner = new Control();
            logicalOwner.AddChild(control);
            var owner = new ScrollOwner { ScrollOffset = new Vector2(0, 45) };
            var presenter = new ScrollPresenter { Owner = owner, Content = control, Size = new Vector2(220, 40) };
            context.Add(logicalOwner);
            context.Add(presenter);
            context.Layout();
            var panel = (VirtualizingGridPanel)((ItemsPresenter)control.TemplateRoot).Panel;
            panel.QueueLayout();
            context.Layout();

            source.Insert(0, -1);
            source.Insert(0, -2);

            Assert.That(owner.ScrollOffset.Y, Is.EqualTo(65));
            control.Dispose();
            presenter.Dispose();
        }

        [Test]
        public void ItemsControl_RestoresFocusBookmarkOnlyWhileOwnerRetainsProxyFocus()
        {
            using var context = new UIContext { ViewportSize = new Vector2(100, 60) };
            var rows = new List<FocusRow>();
            var template = DataTemplate.Create<int>((build, item) =>
            {
                var row = new FocusRow { CustomMinimumSize = new Vector2(80, 20) };
                build.BindItem(row, item);
                rows.Add(row);
                return row;
            });
            var (control, panel, owner, presenter) = CreateVirtualizedItems(context, Enumerable.Range(0, 100).ToArray(), template);
            control.FocusMode = FocusMode.All;
            panel.QueueLayout();
            context.Layout();
            var first = GetFocusRow(control.GetRealizedContainer(0));
            context.SetFocus(first.Editor);

            owner.ScrollOffset = new Vector2(0, 400);
            panel.QueueLayout();
            context.Layout();
            Assert.That(context.FocusedControl, Is.SameAs(control));
            owner.ScrollOffset = Vector2.Zero;
            panel.QueueLayout();
            context.Layout();
            Assert.That(context.FocusedControl, Is.SameAs(GetFocusRow(control.GetRealizedContainer(0)).Editor));

            var external = new Control { FocusMode = FocusMode.All };
            context.Add(external);
            context.SetFocus(GetFocusRow(control.GetRealizedContainer(0)).Editor);
            owner.ScrollOffset = new Vector2(0, 400);
            panel.QueueLayout();
            context.Layout();
            Assert.That(context.FocusedControl, Is.SameAs(control));
            context.SetFocus(external);
            owner.ScrollOffset = Vector2.Zero;
            panel.QueueLayout();
            context.Layout();
            Assert.That(context.FocusedControl, Is.SameAs(external));
            control.Dispose();
            presenter.Dispose();
        }

        [TestCase(FocusCancellation.Remove)]
        [TestCase(FocusCancellation.Disable)]
        [TestCase(FocusCancellation.ReplaceTemplate)]
        [TestCase(FocusCancellation.ReplaceTheme)]
        public void ItemsControl_CancelsStaleFocusBookmarks(FocusCancellation cancellation)
        {
            using var context = new UIContext { ViewportSize = new Vector2(100, 60) };
            var source = new ObservableCollection<int>(Enumerable.Range(0, 100));
            var rows = new List<FocusRow>();
            var template = CreateFocusTemplate(rows);
            var (control, panel, owner, presenter) = CreateVirtualizedItems(context, source, template);
            control.FocusMode = FocusMode.All;
            panel.QueueLayout();
            context.Layout();
            context.SetFocus(GetFocusRow(control.GetRealizedContainer(0)).Editor);
            owner.ScrollOffset = new Vector2(0, 400);
            panel.QueueLayout();
            context.Layout();
            Assert.That(context.FocusedControl, Is.SameAs(control));

            switch (cancellation)
            {
                case FocusCancellation.Remove:
                    source.RemoveAt(0);
                    break;
                case FocusCancellation.Disable:
                    foreach (var row in rows) row.Editor.Enabled = false;
                    break;
                case FocusCancellation.ReplaceTemplate:
                    control.ItemTemplate = CreateFocusTemplate(new List<FocusRow>());
                    break;
                case FocusCancellation.ReplaceTheme:
                    context.Theme = new Theme();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(cancellation));
            }

            owner.ScrollOffset = Vector2.Zero;
            panel.QueueLayout();
            context.Layout();
            var realizedEditor = control.RealizedCount > 0 ? GetFocusRow(control.GetRealizedContainer(0)).Editor : null;
            Assert.That(context.FocusedControl, Is.Not.SameAs(realizedEditor));
            control.Dispose();
            presenter.Dispose();
        }

        [Test]
        public void DynamicExtentIndex_UsesEstimatesAndPreservesMeasuredCorrections()
        {
            var index = new DynamicExtentIndex(100_000, 20);

            index[10] = 35;
            index[50_000] = 10;
            index.SetEstimate(24);

            Assert.Multiple(() =>
            {
                Assert.That(index.Count, Is.EqualTo(100_000));
                Assert.That(index[10], Is.EqualTo(35));
                Assert.That(index[11], Is.EqualTo(24));
                Assert.That(index[50_000], Is.EqualTo(10));
                Assert.That(index.PrefixSum(11), Is.EqualTo(10 * 24 + 35));
                Assert.That(index.Total, Is.EqualTo(100_000 * 24 - 3));
                Assert.That(index.FindIndex(index.PrefixSum(50_000)), Is.EqualTo(50_000));
            });
        }

        [Test]
        public void DynamicExtentIndex_LocalMutationsPreserveValuesAndOrder()
        {
            var index = new DynamicExtentIndex(600, 10);
            index[130] = 25;
            index[131] = 30;

            index.Insert(128, 3);
            Assert.That(index[133], Is.EqualTo(25));
            index.Remove(129, 2);
            Assert.That(index[131], Is.EqualTo(25));
            index.Move(131, 400, 2);

            Assert.Multiple(() =>
            {
                Assert.That(index.Count, Is.EqualTo(601));
                Assert.That(index[400], Is.EqualTo(25));
                Assert.That(index[401], Is.EqualTo(30));
                Assert.That(index.Total, Is.EqualTo(6_045));
                Assert.That(index.FindIndex(index.PrefixSum(400) + 24), Is.EqualTo(400));
            });
        }

        [Test]
        public void DynamicExtentIndex_RejectsNonPositiveOrNonFiniteEstimates()
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new DynamicExtentIndex(1, 0));
                Assert.Throws<ArgumentOutOfRangeException>(() => new DynamicExtentIndex(1, float.NaN));
                Assert.Throws<ArgumentOutOfRangeException>(() => new DynamicExtentIndex(1, float.PositiveInfinity));
            });
        }

        private sealed class CountingGenerator : IItemContainerGenerator
        {
            private readonly Func<int, Vector2> _size;
            private readonly List<object> _tokens;

            private readonly Func<int, Control> _factory;

            public CountingGenerator(int count, Func<int, Vector2> size, object estimateScope = null, Func<int, Control> factory = null)
            {
                _size = size;
                _factory = factory;
                _tokens = Enumerable.Range(0, count).Select(_ => new object()).ToList();
                EstimateScope = estimateScope ?? new object();
            }

            public int Count => _tokens.Count;
            public int RealizeCount { get; private set; }
            public Control ContainerInheritanceParent { get; } = new Control();
            public object EstimateScope { get; }
            public event EventHandler<ItemGeneratorChangedEventArgs> Changed;
            public object GetToken(int index) => _tokens[index];
            public Control Realize(int index)
            {
                RealizeCount++;
                return _factory?.Invoke(index) ?? new Control { CustomMinimumSize = _size(index) };
            }
            public void Recycle(int index, Control container) { }
            public void Insert(int index, int count)
            {
                _tokens.InsertRange(index, Enumerable.Range(0, count).Select(_ => new object()));
                Changed?.Invoke(this, new ItemGeneratorChangedEventArgs(ItemGeneratorChangeAction.Add, -1, index, count));
            }
        }

        private sealed class ScrollOwner : IScrollViewportOwner
        {
            public Vector2 ScrollOffset { get; set; }
            public void OnScrollMetricsChanged(ScrollPresenter presenter, ScrollMetrics metrics) { }
            public void BringIntoView(ScrollPresenter presenter, Control target, Rectangle targetBounds) { }
        }

        private static DataTemplate CreateRecyclingTemplate(List<RecyclingRow> rows) =>
            DataTemplate.Create<int>((build, item) =>
            {
                var row = new RecyclingRow { CustomMinimumSize = new Vector2(80, 20) };
                build.BindItem(row, item);
                rows.Add(row);
                return row;
            });

        private static DataTemplate CreateFocusTemplate(List<FocusRow> rows) =>
            DataTemplate.Create<int>((build, item) =>
            {
                var row = new FocusRow { CustomMinimumSize = new Vector2(80, 20) };
                build.BindItem(row, item);
                rows.Add(row);
                return row;
            });

        private static (ItemsControl Control, VirtualizingStackPanel Panel, ScrollOwner Owner, ScrollPresenter Presenter)
            CreateVirtualizedItems(UIContext context, System.Collections.IEnumerable source, DataTemplate template)
        {
            var control = new ItemsControl
            {
                ItemsPanel = new ItemsPanelTemplate(_ => new VirtualizingStackPanel { EstimatedItemExtent = 20 }),
                ItemTemplate = template,
                ItemsSource = source,
            };
            var logicalOwner = new Control();
            logicalOwner.AddChild(control);
            var owner = new ScrollOwner();
            var presenter = new ScrollPresenter { Owner = owner, Content = control, Size = new Vector2(100, 60) };
            context.Add(logicalOwner);
            context.Add(presenter);
            context.Layout();
            return (control, (VirtualizingStackPanel)((ItemsPresenter)control.TemplateRoot).Panel, owner, presenter);
        }

        private static RecyclingRow GetRecyclingRow(Control container) =>
            (RecyclingRow)((ContentPresenter)((ContentControl)container).TemplateRoot).PresentedControl;

        private sealed class RecyclingRow : Control, IDataTemplateRecyclingState, IDisposable
        {
            public int ActivationCalls { get; private set; }
            public int DeactivationCalls { get; private set; }
            public int RecyclingCalls { get; private set; }
            public int ReusedCalls { get; private set; }
            public bool IsDisposed { get; private set; }

            public RecyclingRow() => FocusMode = FocusMode.All;
            public void Activate() => ActivationCalls++;
            public void Deactivate() => DeactivationCalls++;
            public void OnRecycling() => RecyclingCalls++;
            public void OnReused(object item) => ReusedCalls++;
            public void Dispose() => IsDisposed = true;
        }

        private sealed class NonRecyclableRow : Control, IDisposable
        {
            public bool IsDisposed { get; private set; }
            public void Dispose() => IsDisposed = true;
        }

        private sealed class PinnedControl : Control, IVirtualizationPinState
        {
            public bool IsVirtualizationPinned { get; set; }
        }

        private sealed class KeyedRow
        {
            public KeyedRow(int id) => Id = id;
            public int Id { get; }
        }

        private sealed class FocusRow : Panel, IDataTemplateRecyclingState
        {
            public FocusRow()
            {
                Editor = new Control { FocusMode = FocusMode.All };
                AddChild(Editor);
            }

            public Control Editor { get; }
            public void OnRecycling() { }
            public void OnReused(object item) { }
        }

        private static FocusRow GetFocusRow(Control container) =>
            (FocusRow)((ContentPresenter)((ContentControl)container).TemplateRoot).PresentedControl;

        private sealed class CountingList : System.Collections.IList
        {
            private readonly int _count;

            public CountingList(int count) => _count = count;
            public int EnumerationCount { get; private set; }
            public int Count => _count;
            public bool IsFixedSize => true;
            public bool IsReadOnly => true;
            public bool IsSynchronized => false;
            public object SyncRoot => this;
            public object this[int index]
            {
                get => index >= 0 && index < _count ? index : throw new ArgumentOutOfRangeException(nameof(index));
                set => throw new NotSupportedException();
            }
            public int Add(object value) => throw new NotSupportedException();
            public void Clear() => throw new NotSupportedException();
            public bool Contains(object value) => value is int index && index >= 0 && index < _count;
            public int IndexOf(object value) => Contains(value) ? (int)value : -1;
            public void Insert(int index, object value) => throw new NotSupportedException();
            public void Remove(object value) => throw new NotSupportedException();
            public void RemoveAt(int index) => throw new NotSupportedException();
            public void CopyTo(Array array, int index)
            {
                for (var item = 0; item < _count; item++) array.SetValue(item, index + item);
            }
            public System.Collections.IEnumerator GetEnumerator()
            {
                EnumerationCount++;
                return Enumerable.Range(0, _count).GetEnumerator();
            }
        }

        public enum FocusCancellation
        {
            Remove,
            Disable,
            ReplaceTemplate,
            ReplaceTheme,
        }
    }
}