// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Forma.Xaml;
using Forma.Xaml.Build.Integration.Views;
using Microsoft.Xna.Framework;
using System.ComponentModel;

namespace Forma.Xaml.Build.Integration;

public sealed class HudView : Control
{
    public int AttachedHandlerCalls { get; private set; }
    public int RowHandlerCalls { get; private set; }

    public HudView()
    {
        FormaXamlLoader.Load(this);
    }

    private void OnStyleTargetAttached(object? sender, EventArgs args) => AttachedHandlerCalls++;
    private void OnRowStopRequested(object? sender, EventArgs args) => RowHandlerCalls++;
}

internal static class Program
{
    private static int Main()
    {
        var classlessButton = FormaXamlLoader.Load<Button>();
        if (classlessButton.Name != "ClasslessButton" || classlessButton.Text != "Compiled factory" || classlessButton.CustomMinimumSize != new Vector2(180, 40))
            throw new InvalidOperationException("Classless compiled Forma XAML factory was not registered.");
        if (FormaXamlLoader.Load(typeof(Button)) is not Button runtimeTypeButton || runtimeTypeButton.Text != "Compiled factory")
            throw new InvalidOperationException("Runtime-type compiled Forma XAML factory lookup failed.");
        var svgView = new SvgAssetView();
        var svgImage = NameScope.GetNameScope(svgView)?.Find<Image>("RuntimeSvg")
            ?? throw new InvalidOperationException("Compiled SVG image was not registered.");
        var embeddedSvgNames = typeof(SvgAssetView).Assembly.GetManifestResourceNames().Where(name => name.Contains(".FormaSvg.", StringComparison.Ordinal)).ToArray();
        if (svgImage.ScalableSource is not SvgImageSource compiledSvg || compiledSvg.IntrinsicSize != new Vector2(18, 12) || compiledSvg.SourceLength == 0 || embeddedSvgNames.Length != 1)
            throw new InvalidOperationException("Compiled SVG asset did not preserve source-relative bytes and intrinsic metadata.");
        using var compiledSvgBytes = compiledSvg.OpenStream();
        var csharpSvg = SvgImageSource.FromStream(compiledSvgBytes);
        if (csharpSvg.ContentIdentity != compiledSvg.ContentIdentity || csharpSvg.IntrinsicSize != compiledSvg.IntrinsicSize)
            throw new InvalidOperationException("C# and compiled-XAML SVG sources did not produce equivalent identities and metadata.");
        if (Environment.GetEnvironmentVariable("FORMA_SVG_ASSET_ONLY") == "1") return 0;
        var view = new HudView();
        var model = new HudViewModel { Message = "Ready" };
        view.DataContext = model;
        var scope = NameScope.GetNameScope(view) ?? throw new InvalidOperationException("Compiled Forma XAML did not create a root namescope.");
        var label = scope.Find<Label>("Child");
        var selfLabel = scope.Find<Label>("SelfLabel");
        var ancestorLabel = scope.Find<Label>("AncestorLabel");
        var oneTimeAncestorLabel = scope.Find<Label>("OneTimeAncestorLabel");
        var ancestorEditor = scope.Find<LineEdit>("AncestorEditor");
        var editor = scope.Find<LineEdit>("Editor");
        if (view.Name != "HudRoot" || view.Position != new Vector2(3, 4) || view.Children.Count != 21 || label?.Text != "Ready" ||
            selfLabel?.Text != "SelfLabel" || ancestorLabel?.Text != "HudRoot" || oneTimeAncestorLabel?.Text != "HudRoot" ||
            ancestorEditor?.Text != "HudRoot" || editor?.Text != "Ready")
            throw new InvalidOperationException("Compiled Forma XAML did not populate the code-behind root.");
        var compiledList = scope.Find<ListBox>("CompiledList")
            ?? throw new InvalidOperationException("Compiled ListBox was not registered.");
        if (compiledList.SelectedIndex != 1)
            throw new InvalidOperationException("Compiled ListBox selection did not initialize from its two-way binding.");
        compiledList.SelectedIndex = 0;
        if (model.SelectedIndex != 0)
            throw new InvalidOperationException("Compiled ListBox selection did not update its two-way binding source.");
        var compiledDataGrid = scope.Find<DataGrid>("CompiledDataGrid")
            ?? throw new InvalidOperationException("Compiled flat DataGrid was not registered.");
        var compiledTreeGrid = scope.Find<DataGrid>("CompiledTreeGrid")
            ?? throw new InvalidOperationException("Compiled hierarchical DataGrid was not registered.");
        if (compiledDataGrid.Columns.Count != 2 || compiledDataGrid.Columns[0] is not HudNameColumn ||
            compiledDataGrid.Columns[0].CellTemplate is not DataTemplate ||
            compiledDataGrid.SelectionUnit != DataGridSelectionUnit.Cell ||
            compiledDataGrid.SelectionMode != ItemListSelectionMode.Multi ||
            compiledTreeGrid.Mode != DataGridMode.Hierarchical || compiledTreeGrid.Columns.Single() is not HudTreeColumn)
            throw new InvalidOperationException("Compiled DataGrid properties, columns, or typed cell template were not emitted.");
        var contentButton = scope.Find<Button>("CompiledContentButton")
            ?? throw new InvalidOperationException("Compiled content button was not registered.");
        var buttonContent = scope.Find<Border>("CompiledButtonContent")
            ?? throw new InvalidOperationException("Compiled button content was not registered.");
        var contentPresenter = contentButton.GetTemplateChild(ContentControl.ContentPresenterPartName) as ContentPresenter;
        if (contentPresenter == null ||
            !ReferenceEquals(contentButton.Content, buttonContent) ||
            !ReferenceEquals(buttonContent.Parent, contentButton) ||
            !ReferenceEquals(contentPresenter.PresentedControl, buttonContent) ||
            contentPresenter.HorizontalContentAlignment != HorizontalAlignment.Center)
            throw new InvalidOperationException(
                $"Compiled Button content projection failed: hasTemplate={contentButton.TemplateRoot != null}, " +
                $"content={ReferenceEquals(contentButton.Content, buttonContent)}, parent={ReferenceEquals(buttonContent.Parent, contentButton)}, " +
                $"hasPresentedContent={contentPresenter?.PresentedControl != null}, alignment={contentPresenter?.HorizontalContentAlignment}.");
        ancestorEditor.Text = "RenamedRoot";
        if (view.Name != "RenamedRoot" || ancestorLabel.Text != "RenamedRoot" || oneTimeAncestorLabel.Text != "HudRoot")
            throw new InvalidOperationException("Compiled relative binding modes did not preserve typed update semantics.");
        if (!view.Resources.ContainsKey("LocalPalette") || !view.Resources.TryFind("MergedPalette", out _))
            throw new InvalidOperationException("Compiled Forma XAML did not populate local and merged resources.");
        var winner = (ResourceDictionary)view.Resources["Winner"];
        if (!winner.ContainsKey("LocalMarker") || winner.ContainsKey("MergedMarker"))
            throw new InvalidOperationException("Compiled Forma XAML did not preserve local-over-merged resource precedence.");
        var rowTemplate = (DataTemplate)view.Resources["RowTemplate"];
        var eventfulRowTemplate = (DataTemplate)view.Resources["EventfulRowTemplate"];
        var panelTemplate = (ItemsPanelTemplate)view.Resources["ItemsPanel"];
        var firstRowModel = new RowModel { Name = "First" };
        var secondRowModel = new RowModel { Name = "Second" };
        using var firstRow = rowTemplate.CreateInstance(firstRowModel);
        using var secondRow = rowTemplate.CreateInstance(secondRowModel);
        using var eventfulRowInstance = eventfulRowTemplate.CreateInstance(firstRowModel);
        using var firstPanel = panelTemplate.CreateInstance();
        using var secondPanel = panelTemplate.CreateInstance();
        firstRow.Activate();
        eventfulRowInstance.Activate();
        var eventfulRow = eventfulRowInstance.Root as EventfulRow
            ?? throw new InvalidOperationException("Compiled data template did not instantiate the eventful row control.");
        var eventfulRowScope = NameScope.GetNameScope(eventfulRow)
            ?? throw new InvalidOperationException("Compiled eventful row did not retain its local namescope.");
        var rowEventTarget = eventfulRowScope.Find<ResourceTarget>("RowEventTarget")
            ?? throw new InvalidOperationException("Compiled eventful row target was not found in its local namescope.");
        var rowEventText = eventfulRowScope.Find<TextBlock>("RowEventText")
            ?? throw new InvalidOperationException("Compiled eventful row binding target was not found in its local namescope.");
        rowEventTarget.RaiseStopRequested();
        if (eventfulRow.HandlerCalls != 1 || view.RowHandlerCalls != 0 || rowEventText.Text != "First")
            throw new InvalidOperationException("Data-template row events were not isolated to the row code-behind.");
        eventfulRowInstance.Deactivate();
        rowEventTarget.RaiseStopRequested();
        if (eventfulRow.HandlerCalls != 1)
            throw new InvalidOperationException("Pooled eventful row retained its code-behind event subscription.");
        eventfulRowInstance.Rebind(secondRowModel);
        eventfulRowInstance.Activate();
        rowEventTarget.RaiseStopRequested();
        if (eventfulRow.HandlerCalls != 2 || view.RowHandlerCalls != 0 || rowEventText.Text != "Second")
            throw new InvalidOperationException("Reactivated eventful row did not rebind and restore its row-local handler.");
        var rowTarget = firstRow.NameScope.Find<ResourceTarget>("RowTarget");
        var rowText = firstRow.NameScope.Find<TextBlock>("RowText");
        var rowEditor = firstRow.NameScope.Find<LineEdit>("RowEditor");
        if (firstRow.Root is not ResourceTarget || ReferenceEquals(firstRow.Root, secondRow.Root) || firstRow.Root.Parent != null ||
            rowTarget != firstRow.Root || rowText?.Text != "First" || rowEditor?.Text != "First" || rowTarget.Value.Name != "Initial" ||
            rowTarget.TooltipText != "TemplateStyled" || ReferenceEquals(firstRow.NameScope, secondRow.NameScope))
            throw new InvalidOperationException("Compiled data template did not create fresh detached roots with isolated namescopes.");
        rowEditor.Text = "EditedFirst";
        if (firstRowModel.Name != "EditedFirst")
            throw new InvalidOperationException("Compiled data-template two-way binding did not write to its typed item.");
        firstRowModel.Name = "Updated";
        if (rowText.Text != "Updated" || rowEditor.Text != "Updated")
            throw new InvalidOperationException("Compiled data-template binding did not observe its typed item.");
        rowTarget.Resources["RowValue"] = new ResourceValue { Name = "Active" };
        if (rowTarget.Value.Name != "Active")
            throw new InvalidOperationException("Compiled template dynamic resource did not observe replacement.");
        rowTarget.Classes.Remove("templated");
        if (!string.IsNullOrEmpty(rowTarget.TooltipText))
            throw new InvalidOperationException("Compiled template style did not restore its underlying value.");
        rowTarget.Classes.Add("templated");
        if (rowTarget.StopRequestedSubscriberCount != 1)
            throw new InvalidOperationException("Compiled template event trigger was not attached.");
        rowTarget.RaiseStopRequested();
        if (rowTarget.CustomMinimumSize != new Vector2(4, 5))
            throw new InvalidOperationException("Compiled template event trigger did not start its storyboard.");
        firstRow.Deactivate();
        rowEditor.Text = "PooledEdit";
        rowTarget.Resources["RowValue"] = new ResourceValue { Name = "Pooled" };
        if (rowTarget.Value.Name != "Underlying" || !string.IsNullOrEmpty(rowTarget.TooltipText) ||
            rowTarget.CustomMinimumSize != Vector2.Zero || rowTarget.StopRequestedSubscriberCount != 0 || firstRowModel.Name == "PooledEdit")
            throw new InvalidOperationException("Compiled template attachments remained active while pooled.");
        firstRow.Rebind(secondRowModel);
        firstRow.Activate();
        if (rowText.Text != "Second" || rowEditor.Text != "Second" || rowTarget.Value.Name != "Pooled" || rowTarget.TooltipText != "TemplateStyled" ||
            rowTarget.StopRequestedSubscriberCount != 1)
            throw new InvalidOperationException("Compiled data-template binding did not rebind after pooling.");
        rowEditor.Text = "EditedSecond";
        if (secondRowModel.Name != "EditedSecond" || firstRowModel.Name == "EditedSecond")
            throw new InvalidOperationException("Compiled data-template two-way binding did not target the rebound item.");
        if (firstPanel.Root is not StackPanel || ReferenceEquals(firstPanel.Root, secondPanel.Root) || firstPanel.Root.Parent != null ||
            firstPanel.NameScope.Find<StackPanel>("ItemsStack") != firstPanel.Root)
            throw new InvalidOperationException("Compiled items-panel template did not create a fresh compatible panel.");
        var probe = new HudTemplateProbe { Name = "ProbeOwner", Template = (ControlTemplate)view.Resources["ProbeTemplate"] };
        probe.ApplyTemplate();
        var probeScope = NameScope.GetNameScope(probe.TemplateRoot);
        if (probe.TemplateRoot is not Border || probeScope?.Find<Border>("ProbeBorder") != probe.TemplateRoot ||
            probeScope.Find<TextBlock>("ProbeText")?.Text != "ProbeOwner")
            throw new InvalidOperationException("Compiled control template did not apply with its local namescope.");
        probe.Name = "RenamedProbe";
        if (probeScope.Find<TextBlock>("ProbeText")?.Text != "RenamedProbe")
            throw new InvalidOperationException("Compiled TemplatedParent binding did not observe owner changes.");
        probe.Dispose();
        var keyedProbe = scope.Find<HudTemplateProbe>("KeyedProbe")!;
        var inlineProbe = scope.Find<HudTemplateProbe>("InlineProbe")!;
        if (keyedProbe.TemplateRoot is not Border || NameScope.GetNameScope(keyedProbe.TemplateRoot)?.Find<TextBlock>("ProbeText")?.Text != "KeyedProbe")
            throw new InvalidOperationException("A keyed ControlTemplate was not assigned through StaticResource.");
        var inlineText = NameScope.GetNameScope(inlineProbe.TemplateRoot)?.Find<TextBlock>("InlineText")?.Text;
        if (inlineProbe.TemplateRoot is not Border || inlineText != "InlineProbe")
            throw new InvalidOperationException("An inline ControlTemplate was not assigned through its property element.");
        if (scope.Find<ContentPresenter>("InlineContentPresenter")?.ContentTemplate is not DataTemplate)
            throw new InvalidOperationException("An inline DataTemplate was not assigned through its property element.");
        var inlineItemsPanel = scope.Find<ItemsPresenter>("InlineItemsPresenter")?.ItemsPanel;
        using var inlinePanelInstance = inlineItemsPanel?.CreateInstance();
        if (inlinePanelInstance?.NameScope.Find<StackPanel>("InlineItemsPanel") != inlinePanelInstance?.Root)
            throw new InvalidOperationException("An inline ItemsPanelTemplate was not assigned through its property element.");
        if (!ReferenceEquals(scope.Find<ContentPresenter>("KeyedContentPresenter")?.ContentTemplate, rowTemplate) ||
            !ReferenceEquals(scope.Find<ItemsPresenter>("KeyedItemsPresenter")?.ItemsPanel, panelTemplate))
            throw new InvalidOperationException("Keyed templates were not assigned through StaticResource.");
        var compiledItems = scope.Find<ItemsControl>("CompiledItems")
            ?? throw new InvalidOperationException("Compiled ItemsControl was not registered in the owner namescope.");
        var compiledItemsPresenter = compiledItems.TemplateRoot as ItemsPresenter;
        if (compiledItemsPresenter?.Panel is not StackPanel || compiledItems.RealizedCount != 2 ||
            compiledItems.RealizationDiagnostics.Count != 0 || compiledItems.GetAlternationIndex(0) != 0 ||
            compiledItems.GetAlternationIndex(1) != 1 || compiledItems.GetRealizedContainer(0).TooltipText != "ItemContainer" ||
            compiledItems.GetRealizedContainer(1).TooltipText != "ItemContainer" ||
            !ReferenceEquals(compiledItems.GetRealizedContainer(0).DataContext, model.Rows[0]) ||
            !ReferenceEquals(compiledItems.GetRealizedContainer(1).DataContext, model.Rows[1]))
            throw new InvalidOperationException("Compiled ItemsControl did not realize keyed templates, panel, style, and bound items.");
        if (view.Resources["TargetStyle"] is not Style)
            throw new InvalidOperationException("Compiled style was not registered as a keyed resource.");
        if (scope.Find<ResourceTarget>("StaticTarget")?.Value.Name != "Static")
            throw new InvalidOperationException("Compiled static resource did not resolve its typed target value.");
        var dynamicTarget = scope?.Find<ResourceTarget>("DynamicTarget");
        if (dynamicTarget?.Value.Name != "Dynamic")
            throw new InvalidOperationException("Compiled dynamic resource did not resolve its initial value.");
        var styleTarget = scope?.Find<ResourceTarget>("StyleTarget");
        if (styleTarget?.TooltipText != "Styled" || styleTarget.Margins != new Thickness(1, 2, 3, 4) ||
            styleTarget.Background is not SolidColorBrush { Color: var background } || background != new Color(22, 70, 92) ||
            styleTarget.Value.Name != "Static")
            throw new InvalidOperationException("Compiled selector style did not apply its typed setter.");
        styleTarget.Classes.Remove("styled");
        if (styleTarget.TooltipText != "Underlying" || styleTarget.Margins != new Thickness(0) || styleTarget.Value.Name != "Underlying")
            throw new InvalidOperationException("Compiled selector style did not restore the underlying value.");
        styleTarget.Classes.Add("styled");
        var adaptiveButton = scope?.Find<Button>("AdaptiveButton");
        if (adaptiveButton?.Text != "Underlying")
            throw new InvalidOperationException("Compiled adaptive style applied without a matching context.");
        using var context = new UIContext { ViewportSize = new Vector2(900, 600), InputModality = InputModality.Pointer };
        context.Add(view);
        context.Layout();
        compiledDataGrid.ActivateColumnHeader(0);
        context.Layout();
        compiledDataGrid.SelectCell(new CellIndex(compiledDataGrid.GetRowPath(0), 1));
        if (compiledDataGrid.GetRealizedContainer(0).DataContext != model.Rows[0] ||
            compiledDataGrid.SelectedCells.Count != 1 ||
            ((TextBlock)((ContentPresenter)compiledDataGrid.GetCell(0, 0).GetTemplateChild(ContentControl.ContentPresenterPartName)).PresentedControl).Text != "CompiledFirst" ||
            compiledTreeGrid.HierarchySource == null || compiledTreeGrid.HierarchySource.IndexOfPath(compiledTreeGrid.GetRowPath(1)) != 1)
            throw new InvalidOperationException("Compiled DataGrid sorting, cell template, selection, or hierarchy behavior failed.");
        if (adaptiveButton.Text != "Underlying")
            throw new InvalidOperationException("Compiled adaptive style ignored its initial condition.");
        context.ViewportSize = new Vector2(720, 600);
        context.InputModality = InputModality.Touch;
        if (adaptiveButton.Text != "Adaptive")
            throw new InvalidOperationException("Compiled adaptive style did not activate from context events.");
        context.ViewportSize = new Vector2(721, 600);
        if (adaptiveButton.Text != "Underlying")
            throw new InvalidOperationException("Compiled adaptive style did not restore its underlying value.");
        if (view.AttachedHandlerCalls != 1)
            throw new InvalidOperationException("Compiled event hookup did not invoke the code-behind handler.");
        if (styleTarget.CustomMinimumSize != new Vector2(2, 3))
            throw new InvalidOperationException("Compiled event trigger did not start its storyboard.");
        context.Update(new GameTime(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100)));
        if (styleTarget.CustomMinimumSize != new Vector2(8, 9))
            throw new InvalidOperationException("Compiled storyboard did not advance to its deterministic final value.");
        model.IsActive = true;
        if (dynamicTarget.CustomMinimumSize != new Vector2(1, 2))
            throw new InvalidOperationException("Compiled property trigger did not start its storyboard.");
        context.Update(new GameTime(TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(50)));
        if (dynamicTarget.CustomMinimumSize != new Vector2(1, 2))
            throw new InvalidOperationException("Compiled storyboard did not honor its repeat behavior.");
        dynamicTarget.RaiseStopRequested();
        if (dynamicTarget.CustomMinimumSize != Vector2.Zero)
            throw new InvalidOperationException("Compiled StopStoryboard did not restore the underlying value.");
        model.IsActive = false;
        if (dynamicTarget.CustomMinimumSize != Vector2.Zero)
            throw new InvalidOperationException("Compiled property trigger did not stop and restore its storyboard.");
        view.Resources["DynamicValue"] = new ResourceValue { Name = "Replaced" };
        if (dynamicTarget?.Value.Name != "Replaced")
            throw new InvalidOperationException("Compiled dynamic resource did not observe replacement values.");
        model.Message = "Updated";
        if (label.Text != "Updated") throw new InvalidOperationException("Compiled one-way binding did not observe its typed source property.");
        editor.Text = "Edited";
        if (model.Message != "Edited") throw new InvalidOperationException("Compiled two-way binding did not write through its typed source property.");
        context.Remove(view);
        if (styleTarget.CustomMinimumSize != Vector2.Zero)
            throw new InvalidOperationException("Compiled storyboard clock did not restore its underlying value on detach.");
        if (dynamicTarget.StopRequestedSubscriberCount != 0)
            throw new InvalidOperationException("Compiled event trigger remained subscribed after detach.");
        if (styleTarget.TooltipText != "Underlying" || styleTarget.Margins != new Thickness(0) || styleTarget.Value.Name != "Underlying")
            throw new InvalidOperationException("Compiled selector style did not dispose on detach.");
        if (dynamicTarget?.Value.Name != "Underlying")
            throw new InvalidOperationException("Compiled dynamic resource did not restore its underlying value on detach.");
        view.Resources["DynamicValue"] = new ResourceValue { Name = "AfterDetach" };
        if (dynamicTarget?.Value.Name != "Underlying")
            throw new InvalidOperationException("Compiled dynamic resource remained subscribed after detach.");
        var detachedLabelText = label.Text;
        model.Message = "AfterDetach";
        if (label.Text != detachedLabelText)
            throw new InvalidOperationException("Compiled one-way binding remained subscribed after detach.");
        editor.Text = "DetachedEdit";
        if (model.Message == "DetachedEdit")
            throw new InvalidOperationException("Compiled two-way binding remained subscribed after detach.");
        model.IsActive = true;
        if (dynamicTarget.CustomMinimumSize != Vector2.Zero)
            throw new InvalidOperationException("Compiled property trigger remained subscribed after detach.");
        context.Add(view);
        if (view.AttachedHandlerCalls != 1)
            throw new InvalidOperationException("Compiled event hookup remained subscribed after detach.");
        Console.WriteLine("Forma XAML build integration: PASS");
        return 0;
    }
}

public sealed class ResourceValue
{
    public string Name { get; set; } = string.Empty;
}

public sealed class RowModel : INotifyPropertyChanged
{
    private string _name = string.Empty;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }
}

public sealed class TreeRowModel
{
    public string Name { get; init; } = string.Empty;
    public TreeRowModel[] Children { get; init; } = Array.Empty<TreeRowModel>();
    public bool IsExpanded { get; set; }
}

public sealed class HudNameColumn : DataGridTextColumn
{
    public HudNameColumn()
    {
        Binding = DataGridBinding<string>.Create<RowModel>(row => row.Name);
        SortBinding = DataGridSortBinding.Create<RowModel, string>(row => row.Name);
    }
}

public sealed class HudLengthColumn : DataGridTextColumn
{
    public HudLengthColumn()
    {
        Binding = DataGridBinding<string>.Create<RowModel>(row => row.Name.Length.ToString());
        SortBinding = DataGridSortBinding.Create<RowModel, int>(row => row.Name.Length);
    }
}

public sealed class HudTreeColumn : DataGridExpanderColumn
{
    public HudTreeColumn()
    {
        Children = DataGridBinding<System.Collections.IEnumerable>.Create<TreeRowModel>(row => row.Children);
        HasChildren = DataGridBinding<bool>.Create<TreeRowModel>(row => row.Children.Length != 0);
        IsExpanded = DataGridBinding<bool>.Create<TreeRowModel>(row => row.IsExpanded, write: (row, value) => row.IsExpanded = value);
        Column = new DataGridTextColumn
        {
            Binding = DataGridBinding<string>.Create<TreeRowModel>(row => row.Name),
            SortBinding = DataGridSortBinding.Create<TreeRowModel, string>(row => row.Name),
        };
        SortBinding = DataGridSortBinding.Create<TreeRowModel, string>(row => row.Name);
    }
}

public sealed class HudTemplateProbe : TemplatedControl;

public sealed class ResourceTarget : Control
{
    private EventHandler? _stopRequested;
    public int StopRequestedSubscriberCount { get; private set; }
    public event EventHandler? StopRequested
    {
        add { _stopRequested += value; StopRequestedSubscriberCount++; }
        remove { _stopRequested -= value; StopRequestedSubscriberCount--; }
    }
    public Brush? Background { get; set; }
    public ResourceValue Value { get; set; } = new ResourceValue { Name = "Underlying" };
    public void RaiseStopRequested() => _stopRequested?.Invoke(this, EventArgs.Empty);
}

public sealed class HudViewModel : INotifyPropertyChanged
{
    private string _message = string.Empty;
    private bool _isActive;
    private int _selectedIndex = 1;
    public event PropertyChangedEventHandler? PropertyChanged;
    public RowModel[] Rows { get; } =
    {
        new RowModel { Name = "CompiledFirst" },
        new RowModel { Name = "CompiledSecond" },
    };
    public TreeRowModel[] TreeRows { get; } =
    {
        new TreeRowModel
        {
            Name = "Root",
            IsExpanded = true,
            Children = new[] { new TreeRowModel { Name = "Child" } },
        },
    };
    public string Message
    {
        get => _message;
        set
        {
            if (_message == value) return;
            _message = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Message)));
        }
    }
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        }
    }
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
}