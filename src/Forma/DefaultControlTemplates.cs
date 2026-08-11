// Copyright (c) 2026 Igor Hipolito Vieira
// SPDX-License-Identifier: MIT

using System;

namespace Forma
{
    internal static class DefaultControlTemplates
    {
        private static readonly object Sync = new object();
        private static bool _registered;

        internal static void EnsureRegistered(DefaultControlTemplateRegistry registry)
        {
            if (_registered) return;
            lock (Sync)
            {
                if (_registered) return;
                registry.Register<ContentControl>((_, owner) => new ContentPresenter
                {
                    Name = ContentControl.ContentPresenterPartName,
                    Content = owner.Content,
                    ContentTemplate = owner.ContentTemplate,
                    HorizontalContentAlignment = owner.HorizontalContentAlignment,
                    VerticalContentAlignment = owner.VerticalContentAlignment,
                }, new[]
                {
                    new TemplatePartMetadata(ContentControl.ContentPresenterPartName, typeof(ContentPresenter), false),
                });
                registry.Register<GroupBox>((_, owner) => new GroupBoxPresenter(owner), new[]
                {
                    new TemplatePartMetadata(GroupBox.HeaderPresenterPartName, typeof(Label), false),
                    new TemplatePartMetadata(ContentControl.ContentPresenterPartName, typeof(ContentPresenter), false),
                });
                registry.Register<BaseButton>((_, owner) => new BaseButtonPresenter(owner), new[]
                {
                    new TemplatePartMetadata(ContentControl.ContentPresenterPartName, typeof(ContentPresenter), false),
                });
                registry.Register<CheckBox>((_, owner) => new CheckBoxPresenter(owner), new[]
                {
                    new TemplatePartMetadata(ContentControl.ContentPresenterPartName, typeof(ContentPresenter), false),
                });
                registry.Register<LinkButton>((_, owner) => new LinkButtonPresenter(owner), new[]
                {
                    new TemplatePartMetadata(ContentControl.ContentPresenterPartName, typeof(ContentPresenter), false),
                });
                registry.Register<TextureButton>((_, owner) => new TextureButtonPresenter(owner), new[]
                {
                    new TemplatePartMetadata(ContentControl.ContentPresenterPartName, typeof(ContentPresenter), false),
                });
                registry.Register<OptionButton>((_, owner) => new OptionButtonPresenter(owner), new[]
                {
                    new TemplatePartMetadata(ContentControl.ContentPresenterPartName, typeof(ContentPresenter), false),
                });
                registry.Register<TabBar>((_, owner) => new TabBarPresenter(owner));
                registry.Register<TabContainer>((_, owner) => new TabContainerPresenter(owner));
                registry.Register<LineEdit>((_, owner) => new LineEditPresenter(owner), new[]
                {
                    new TemplatePartMetadata(LineEdit.EditorPresenterPartName, typeof(LineEditPresenter)),
                });
                registry.Register<TextEdit>((_, owner) => new LineEditPresenter(owner), new[]
                {
                    new TemplatePartMetadata(LineEdit.EditorPresenterPartName, typeof(LineEditPresenter)),
                });
                registry.Register<CodeEdit>((_, owner) => new LineEditPresenter(owner), new[]
                {
                    new TemplatePartMetadata(LineEdit.EditorPresenterPartName, typeof(LineEditPresenter)),
                });
                registry.Register<SpinBox>((context, owner) =>
                {
                    var presenter = new SpinBoxPresenter(owner);
                    context.RegisterLifecycle(presenter.Activate, presenter.Deactivate);
                    return presenter;
                }, new[]
                {
                    new TemplatePartMetadata(SpinBox.EditorPartName, typeof(ContentPresenter)),
                });
                registry.Register<Popup>((_, owner) => new PopupPresenter(owner));
                registry.Register<PopupMenu>((context, owner) =>
                {
                    var presenter = new PopupMenuPresenter(owner);
                    context.NameScope.Register(PopupMenu.ItemsPartName, owner.ItemsControl);
                    return presenter;
                }, new[]
                {
                    new TemplatePartMetadata(PopupMenu.ItemsPartName, typeof(PopupMenuItems)),
                });
                registry.Register<MenuBar>((_, owner) => new MenuBarPresenter(owner));
                registry.Register<AcceptDialog>((_, owner) => new AcceptDialogPresenter(owner), new[]
                {
                    new TemplatePartMetadata(AcceptDialog.TitlePresenterPartName, typeof(Label)),
                    new TemplatePartMetadata(ContentControl.ContentPresenterPartName, typeof(Label)),
                    new TemplatePartMetadata(AcceptDialog.AcceptButtonPartName, typeof(BaseButton)),
                    new TemplatePartMetadata(AcceptDialog.CancelButtonPartName, typeof(BaseButton), false),
                });
                registry.Register<FileDialog>((_, owner) => new AcceptDialogPresenter(owner), new[]
                {
                    new TemplatePartMetadata(AcceptDialog.TitlePresenterPartName, typeof(Label)),
                    new TemplatePartMetadata(ContentControl.ContentPresenterPartName, typeof(Label)),
                    new TemplatePartMetadata(AcceptDialog.AcceptButtonPartName, typeof(BaseButton)),
                    new TemplatePartMetadata(AcceptDialog.CancelButtonPartName, typeof(BaseButton), false),
                });
                registry.Register<ColorPicker>((_, owner) => new ColorFieldPresenter(owner), new[]
                {
                    new TemplatePartMetadata(ColorPicker.ColorFieldPartName, typeof(Control)),
                });
                registry.Register<ColorPickerPopupPanel>((_, owner) => new PopupPresenter(owner));
                registry.Register<ColorPickerDialog>((_, owner) => new AcceptDialogPresenter(owner), new[]
                {
                    new TemplatePartMetadata(AcceptDialog.TitlePresenterPartName, typeof(Label)),
                    new TemplatePartMetadata(ContentControl.ContentPresenterPartName, typeof(Label)),
                    new TemplatePartMetadata(AcceptDialog.AcceptButtonPartName, typeof(BaseButton)),
                    new TemplatePartMetadata(AcceptDialog.CancelButtonPartName, typeof(BaseButton), false),
                });
                registry.Register<GraphElement>((_, owner) => new GraphElementPresenter(owner));
                registry.Register<GraphNode>((_, owner) => new GraphElementPresenter(owner));
                registry.Register<GraphFrame>((_, owner) => new GraphElementPresenter(owner));
                registry.Register<GraphEdit>((_, owner) => new GraphCanvasPresenter(owner), new[]
                {
                    new TemplatePartMetadata(GraphEdit.GraphCanvasPartName, typeof(Container)),
                });
                registry.Register<ItemList>((_, owner) => new ItemListPresenter(owner));
                registry.Register<RichTextLabel>((_, owner) => new RichTextPresenter(owner), new[]
                {
                    new TemplatePartMetadata(RichTextLabel.RichTextPresenterPartName, typeof(Container)),
                });
                registry.Register<RichTextDocument>((_, owner) => new RichTextPresenter(owner), new[]
                {
                    new TemplatePartMetadata(RichTextLabel.RichTextPresenterPartName, typeof(Container)),
                });
                registry.Register<Tree>((_, owner) => new TreePresenter(owner), new[]
                {
                    new TemplatePartMetadata(Tree.TreePresenterPartName, typeof(Container)),
                });
                registry.Register<ColorPresetButton>((_, owner) => new ColorButtonPresenter(owner, () => owner.Color, 4), new[]
                {
                    new TemplatePartMetadata(ContentControl.ContentPresenterPartName, typeof(ContentPresenter), false),
                });
                registry.Register<ColorPickerButton>((_, owner) => new ColorButtonPresenter(owner, () => owner.Color, 5), new[]
                {
                    new TemplatePartMetadata(ContentControl.ContentPresenterPartName, typeof(ContentPresenter), false),
                });
                registry.Register<Slider>((_, owner) => new SliderPresenter(owner));
                registry.Register<ProgressBar>((_, owner) => new ProgressBarPresenter(owner));
                registry.Register<ScrollBar>((_, owner) => new ScrollBarPresenter(owner));
                registry.Register<TextureProgressBar>((_, owner) => new TextureProgressBarPresenter(owner));
                registry.Register<SplitContainer>((_, owner) => new SplitContainerPresenter(owner));
                registry.Register<FoldableContainer>((_, owner) => new FoldableContainerPresenter(owner));
                registry.Register<SubViewportContainer>((_, owner) => new SubViewportPresenter(owner));
                registry.Register<VirtualJoystick>((_, owner) => new VirtualJoystickPresenter(owner));
                registry.Register<ItemsControl>((_, owner) => new ItemsPresenter
                {
                    ItemsPanel = owner.ItemsPanel,
                    Owner = owner,
                });
                registry.Register<ListBox>(CreateListBoxTemplate, new[]
                {
                    new TemplatePartMetadata(ListBox.ScrollPresenterPartName, typeof(ScrollPresenter)),
                    new TemplatePartMetadata(ListBox.ItemsPresenterPartName, typeof(ItemsPresenter)),
                });
                registry.Register<ScrollContainer>((context, owner) =>
                {
                    var presenter = new ScrollContainerChromePresenter(owner);
                    context.RegisterLifecycle(presenter.Activate, presenter.Deactivate);
                    return presenter;
                }, new[]
                {
                    new TemplatePartMetadata(ScrollContainer.ScrollPresenterPartName, typeof(ScrollPresenter)),
                });
                registry.Register<DataGridCell>((_, owner) => new DataGridCellPresenter(owner), new[]
                {
                    new TemplatePartMetadata(ContentControl.ContentPresenterPartName, typeof(ContentPresenter), false),
                });
                registry.Register<DataGridColumnHeader>((_, owner) => new DataGridColumnHeaderPresenter(owner), new[]
                {
                    new TemplatePartMetadata(ContentControl.ContentPresenterPartName, typeof(ContentPresenter), false),
                });
                registry.Register<DataGridRow>((_, owner) => new DataGridRowPresenter(owner), new[]
                {
                    new TemplatePartMetadata(ContentControl.ContentPresenterPartName, typeof(ContentPresenter), false),
                });
                registry.Register<DataGrid>(CreateDataGridTemplate, new[]
                {
                    new TemplatePartMetadata(ListBox.ScrollPresenterPartName, typeof(ScrollPresenter)),
                    new TemplatePartMetadata(ListBox.ItemsPresenterPartName, typeof(ItemsPresenter)),
                    new TemplatePartMetadata(DataGrid.ColumnHeadersPartName, typeof(GridPanel)),
                    new TemplatePartMetadata(DataGrid.HorizontalScrollBarPresenterPartName, typeof(ContentPresenter), false),
                    new TemplatePartMetadata(DataGrid.VerticalScrollBarPresenterPartName, typeof(ContentPresenter), false),
                });
                _registered = true;
            }
        }

        private static Control CreateListBoxTemplate(TemplateBuildContext context, ListBox owner)
        {
            var root = new OverlayPanel();
            var itemsPresenter = new ItemsPresenter
            {
                Name = ListBox.ItemsPresenterPartName,
                ItemsPanel = owner.ItemsPanel,
                Owner = owner,
            };
            var scrollPresenter = new ScrollPresenter
            {
                Name = ListBox.ScrollPresenterPartName,
                Owner = owner,
            };
            root.AddChild(itemsPresenter);
            root.AddChild(scrollPresenter);
            scrollPresenter.Content = itemsPresenter;
            return root;
        }

        private static Control CreateDataGridTemplate(TemplateBuildContext context, DataGrid owner)
        {
            var root = new GridPanel();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridTrackSize.Star() });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridTrackSize.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridTrackSize.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridTrackSize.Star() });
            root.RowDefinitions.Add(new RowDefinition { Height = GridTrackSize.Auto });
            var headers = new GridPanel { Name = DataGrid.ColumnHeadersPartName };
            var items = new ItemsPresenter
            {
                Name = ListBox.ItemsPresenterPartName,
                ItemsPanel = owner.ItemsPanel,
                Owner = owner,
                HorizontalSizeFlags = SizeFlags.Fill | SizeFlags.Expand,
            };
            var scroll = new ScrollPresenter
            {
                Name = ListBox.ScrollPresenterPartName,
                Owner = owner,
            };
            var horizontalScrollBar = new ContentPresenter
            {
                Name = DataGrid.HorizontalScrollBarPresenterPartName,
                Content = owner.HorizontalScrollBar,
                Visible = false,
                HorizontalContentAlignment = HorizontalAlignment.Fill,
                VerticalContentAlignment = VerticalAlignment.Fill,
            };
            var verticalScrollBar = new ContentPresenter
            {
                Name = DataGrid.VerticalScrollBarPresenterPartName,
                Content = owner.VerticalScrollBar,
                Visible = false,
                HorizontalContentAlignment = HorizontalAlignment.Fill,
                VerticalContentAlignment = VerticalAlignment.Fill,
            };
            GridPanel.SetRow(headers, 0);
            GridPanel.SetRow(scroll, 1);
            GridPanel.SetRow(horizontalScrollBar, 2);
            GridPanel.SetColumn(verticalScrollBar, 1);
            GridPanel.SetRow(verticalScrollBar, 1);
            root.AddChild(headers);
            root.AddChild(items);
            root.AddChild(scroll);
            root.AddChild(horizontalScrollBar);
            root.AddChild(verticalScrollBar);
            scroll.Content = items;
            return root;
        }
    }
}