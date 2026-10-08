// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Forma.Xaml.HotReload;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Forma.Xaml.HotReload.Tests;

public sealed class HotReloadRow : BoxContainer { }

public class FormaXamlHotReloadTest
{
    private string _directory = null!;
    [SetUp] public void SetUp() { _directory = Path.Combine(Path.GetTempPath(), $"forma-hotreload-{Guid.NewGuid():N}"); Directory.CreateDirectory(_directory); }
    [TearDown] public void TearDown() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    [Test]
    public async Task Reload_AppliesAtFrameBoundaryAndPreservesDataContext()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "View.xaml"), "<Control xmlns='https://forma.dev/xaml' Name='New'><Control Name='Child' /></Control>");
        using var context = new UIContext();
        var model = new object();
        Control current = new Control { Name = "Old", DataContext = model };
        context.Add(current);
        using var service = new FormaXamlHotReloadService(context, _directory, watchFiles: false);
        var callbackThread = -1;
        using var registration = service.Register("View.xaml", () => current, (oldValue, newValue) =>
        {
            callbackThread = Environment.CurrentManagedThreadId;
            context.Remove(oldValue);
            context.Add(newValue);
            current = newValue;
        });

        await service.RequestReloadAsync("View.xaml");
        Assert.That(current.Name, Is.EqualTo("Old"));
        var updateThread = Environment.CurrentManagedThreadId;
        context.Update(new GameTime(), new MouseState(), new KeyboardState());
        Assert.Multiple(() =>
        {
            Assert.That(current.Name, Is.EqualTo("New"));
            Assert.That(current.DataContext, Is.SameAs(model));
            Assert.That(NameScope.GetNameScope(current)?.Find<Control>("Child"), Is.SameAs(current.Children[0]));
            Assert.That(callbackThread, Is.EqualTo(updateThread));
        });
    }

    [Test]
    public async Task HtmlReload_ConvertsTheChangedFhtmlAndReloadsTheView_AndInvalidInputLeavesTheTreeIntact()
    {
        const string generated = "obj/html/View.fhtml.xaml";
        Directory.CreateDirectory(Path.Combine(_directory, "obj", "html"));
        await File.WriteAllTextAsync(Path.Combine(_directory, "View.fhtml"), "<div id=\"Root\"><span id=\"Label\">one</span></div>");
        var first = Forma.Xaml.Compiler.Html.FormaHtmlConverter.Convert("<div id=\"Root\"><span id=\"Label\">one</span></div>", "View.fhtml");
        await File.WriteAllTextAsync(Path.Combine(_directory, generated), first.Xaml);
        await File.WriteAllTextAsync(Path.Combine(_directory, generated + ".fhtmlmap"), first.Map.Serialize("View.fhtml"));
        using var context = new UIContext();
        Control current = (Control)Forma.Xaml.Compiler.FormaXamlCompiler.CreateSre().CompileSre(first.Xaml, generated).Build(null);
        context.Add(current);
        using var service = new FormaXamlHotReloadService(context, _directory, watchFiles: false);
        var diagnostics = new List<Forma.Xaml.Compiler.FormaDiagnostic>();
        service.DiagnosticsChanged += batch => diagnostics.AddRange(batch);
        using var registration = service.Register(generated, () => current, (oldValue, newValue) =>
        {
            context.Remove(oldValue);
            context.Add(newValue);
            current = newValue;
        });

        await File.WriteAllTextAsync(Path.Combine(_directory, "View.fhtml"), "<div id=\"Root\"><span id=\"Label\">two</span></div>");
        await service.RequestHtmlReloadAsync("View.fhtml");
        context.Update(new GameTime(), new MouseState(), new KeyboardState());
        Assert.That(((Label)NameScope.GetNameScope(current)!.Find("Label")!).Text, Is.EqualTo("two"));

        var good = current;
        await File.WriteAllTextAsync(Path.Combine(_directory, "View.fhtml"), "<div style=\"position: absolute\"></div>");
        await service.RequestHtmlReloadAsync("View.fhtml");
        context.Update(new GameTime(), new MouseState(), new KeyboardState());

        Assert.That(current, Is.SameAs(good));
        Assert.That(diagnostics.Single().Code, Is.EqualTo("FHTML2005"));
        Assert.That(diagnostics.Single().Location.FilePath, Is.EqualTo("View.fhtml"));
    }

    [Test]
    public async Task Reload_PreservesInheritedDataContextWithoutMakingItLocal()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "View.xaml"), "<Control xmlns='https://forma.dev/xaml' Name='New' />");
        using var context = new UIContext();
        var initialModel = new object();
        var updatedModel = new object();
        var parent = new Control { DataContext = initialModel };
        Control current = new Control { Name = "Old" };
        parent.AddChild(current);
        context.Add(parent);
        using var service = new FormaXamlHotReloadService(context, _directory, watchFiles: false);
        using var registration = service.Register("View.xaml", () => current, (oldValue, replacement) =>
        {
            parent.RemoveChild(oldValue);
            parent.AddChild(replacement);
            current = replacement;
        });

        await service.RequestReloadAsync("View.xaml");
        context.Update(new GameTime(), new MouseState(), new KeyboardState());

        Assert.Multiple(() =>
        {
            Assert.That(current.Name, Is.EqualTo("New"));
            Assert.That(current.HasLocalDataContext, Is.False);
            Assert.That(current.DataContext, Is.SameAs(initialModel));
        });

        parent.DataContext = updatedModel;
        Assert.That(current.DataContext, Is.SameAs(updatedModel));
    }

    [Test]
    public async Task SvgAssetReload_ResolvesRelativeFileAndCreatesNewSourceIdentity()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "Views"));
        Directory.CreateDirectory(Path.Combine(_directory, "Assets"));
        var xamlPath = Path.Combine(_directory, "Views", "View.xaml");
        var svgPath = Path.Combine(_directory, "Assets", "icon.svg");
        await File.WriteAllTextAsync(xamlPath, "<Image xmlns='https://forma.dev/xaml' ScalableSource='../Assets/icon.svg' />");
        await File.WriteAllTextAsync(svgPath, "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='6'><rect width='8' height='6' /></svg>");
        using var context = new UIContext();
        Control current = new Image();
        using var service = new FormaXamlHotReloadService(context, _directory, watchFiles: false);
        using var registration = service.Register("Views/View.xaml", () => current, (_, replacement) => current = replacement);

        await service.RequestReloadAsync("Views/View.xaml");
        context.Update(new GameTime(), new MouseState(), new KeyboardState());
        var first = ((Image)current).ScalableSource as SvgImageSource;
        Assert.That(first?.IntrinsicSize, Is.EqualTo(new Vector2(8, 6)));

        await File.WriteAllTextAsync(svgPath, "<svg xmlns='http://www.w3.org/2000/svg' width='12' height='9'><rect width='12' height='9' /></svg>");
        await service.RequestAssetReloadAsync("Assets/icon.svg");
        context.Update(new GameTime(), new MouseState(), new KeyboardState());
        var second = ((Image)current).ScalableSource as SvgImageSource;

        Assert.Multiple(() =>
        {
            Assert.That(second?.IntrinsicSize, Is.EqualTo(new Vector2(12, 9)));
            Assert.That(second?.ContentIdentity, Is.Not.EqualTo(first?.ContentIdentity));
        });
    }

    [Test]
    public async Task Reload_ReplacesEveryLiveInstanceOfTheSameArtifactAtomically()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "Row.xaml"), """
            <BoxContainer xmlns="https://forma.dev/xaml"
                          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                          xmlns:local="clr-namespace:Forma.Xaml.HotReload.Tests"
                          x:Class="Forma.Xaml.HotReload.Tests.HotReloadRow"
                          Name="New" />
            """);
        using var context = new UIContext();
        var firstModel = new object();
        var secondModel = new object();
        Control first = new HotReloadRow { Name = "First", DataContext = firstModel };
        Control second = new HotReloadRow { Name = "Second", DataContext = secondModel };
        using var service = new FormaXamlHotReloadService(context, _directory, watchFiles: false);
        using var firstRegistration = service.Register("Row.xaml", () => first, (_, replacement) => first = replacement);
        using var secondRegistration = service.Register("Row.xaml", () => second, (_, replacement) => second = replacement);

        await service.RequestReloadAsync("Row.xaml");
        Assert.Multiple(() =>
        {
            Assert.That(first.Name, Is.EqualTo("First"));
            Assert.That(second.Name, Is.EqualTo("Second"));
        });

        context.Update(new GameTime(), new MouseState(), new KeyboardState());
        Assert.Multiple(() =>
        {
            Assert.That(first.Name, Is.EqualTo("New"));
            Assert.That(second.Name, Is.EqualTo("New"));
            Assert.That(first.DataContext, Is.SameAs(firstModel));
            Assert.That(second.DataContext, Is.SameAs(secondModel));
            Assert.That(first, Is.Not.SameAs(second));
        });
    }

    [Test]
    public async Task TargetedResourceReload_UpdatesOnlyItsDynamicConsumers()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "Resources.xaml"), "reloaded");
        using var context = new UIContext();
        var root = new Control();
        var affected = new Control();
        var unaffected = new Control();
        root.AddChild(affected);
        root.AddChild(unaffected);
        root.Resources["Affected"] = "initial";
        root.Resources["Unaffected"] = "steady";
        var tooltip = new XamlProperty<string>(
            nameof(Control.TooltipText),
            target => ((Control)target).TooltipText,
            (target, value) => ((Control)target).TooltipText = value);
        using var affectedBinding = DynamicResource.Attach(root, affected, tooltip, "Affected");
        using var unaffectedBinding = DynamicResource.Attach(root, unaffected, tooltip, "Unaffected");
        var affectedId = new FormaXamlArtifactId("Resources.xaml", "key:Affected", FormaXamlArtifactKind.Resource, "Affected");
        var unaffectedId = new FormaXamlArtifactId("Resources.xaml", "key:Unaffected", FormaXamlArtifactKind.Resource, "Unaffected");
        var unaffectedPreparations = 0;
        using var service = new FormaXamlHotReloadService(context, _directory, watchFiles: false);
        using var affectedRegistration = service.RegisterArtifact(
            affectedId,
            () => (string)root.Resources["Affected"],
            (source, _) => source,
            (_, replacement) => root.Resources["Affected"] = replacement);
        using var unaffectedRegistration = service.RegisterArtifact(
            unaffectedId,
            () => (string)root.Resources["Unaffected"],
            (source, _) => { unaffectedPreparations++; return source; },
            (_, replacement) => root.Resources["Unaffected"] = replacement);

        await service.RequestReloadAsync(affectedId);
        context.Update(new GameTime(), new MouseState(), new KeyboardState());

        Assert.Multiple(() =>
        {
            Assert.That(affected.TooltipText, Is.EqualTo("reloaded"));
            Assert.That(unaffected.TooltipText, Is.EqualTo("steady"));
            Assert.That(unaffectedPreparations, Is.Zero);
        });
    }

    [Test]
    public async Task ArtifactReload_PreparesChangedArtifactsAndTransitiveDependentsBeforeCommit()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "Palette.xaml"), "bright");
        await File.WriteAllTextAsync(Path.Combine(_directory, "ButtonTemplate.xaml"), "outlined");
        using var context = new UIContext();
        var paletteId = new FormaXamlArtifactId("Palette.xaml", "key:Palette", FormaXamlArtifactKind.Resource, "Palette");
        var templateId = new FormaXamlArtifactId("ButtonTemplate.xaml", "key:Button", FormaXamlArtifactKind.ControlTemplate, "Button");
        ArtifactValue palette = new("dark");
        ArtifactValue template = new("filled:dark");
        using var service = new FormaXamlHotReloadService(context, _directory, watchFiles: false);
        using var paletteRegistration = service.RegisterArtifact(
            paletteId,
            () => palette,
            (source, _) => new ArtifactValue(source),
            (_, replacement) => palette = replacement);
        using var templateRegistration = service.RegisterArtifact(
            templateId,
            () => template,
            (preparation, source, _) => new ArtifactValue($"{source}:{preparation.GetPreparedValue<ArtifactValue>(paletteId).Value}"),
            (_, replacement) => template = replacement,
            [paletteId]);

        await service.RequestReloadAsync("Palette.xaml");
        Assert.Multiple(() =>
        {
            Assert.That(palette.Value, Is.EqualTo("dark"));
            Assert.That(template.Value, Is.EqualTo("filled:dark"));
        });
        context.Update(new GameTime(), new MouseState(), new KeyboardState());
        Assert.Multiple(() =>
        {
            Assert.That(palette.Value, Is.EqualTo("bright"));
            Assert.That(template.Value, Is.EqualTo("outlined:bright"));
        });

        await File.WriteAllTextAsync(Path.Combine(_directory, "Palette.xaml"), "invalid");
        using var failedRegistration = service.RegisterArtifact(
            new FormaXamlArtifactId("Palette.xaml", "key:Failure", FormaXamlArtifactKind.Resource, "Failure"),
            () => palette,
            (source, _) => source == "invalid" ? throw new InvalidOperationException("invalid palette") : new ArtifactValue(source),
            (_, replacement) => palette = replacement);
        await service.RequestReloadAsync("Palette.xaml");
        context.Update(new GameTime(), new MouseState(), new KeyboardState());
        Assert.Multiple(() =>
        {
            Assert.That(palette.Value, Is.EqualTo("bright"));
            Assert.That(template.Value, Is.EqualTo("outlined:bright"));
        });
    }

    [Test]
    public async Task ControlTemplateReload_PreservesSemanticOwnerStateAndAccessibilityPeer()
    {
        const string source = """
            <Control xmlns="https://forma.dev/xaml"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Control.Resources>
                    <ResourceDictionary>
                        <ControlTemplate x:Key="Chrome" TargetType="Button">
                            <Border Background="#FF16324A" Padding="8">
                                <TextBlock Text="{Binding Text, RelativeSource=TemplatedParent}" />
                            </Border>
                        </ControlTemplate>
                    </ResourceDictionary>
                </Control.Resources>
            </Control>
            """;
        await File.WriteAllTextAsync(Path.Combine(_directory, "Button.xaml"), source);
        using var context = new UIContext();
        var initialTemplate = new ControlTemplate(typeof(Button), _ => new TextBlock { Text = "Initial" });
        var button = new Button { Text = "Retained", Template = initialTemplate };
        var initialRoot = button.TemplateRoot;
        var initialPeer = button.AccessibilityPeer;
        var initialActions = initialPeer.Actions;
        using var service = new FormaXamlHotReloadService(context, _directory, watchFiles: false);
        using var registration = service.RegisterControlTemplate(
            new FormaXamlArtifactId("Button.xaml", "key:Chrome", FormaXamlArtifactKind.ControlTemplate, "Chrome"),
            button);

        await service.RequestReloadAsync("Button.xaml");
        Assert.That(button.TemplateRoot, Is.SameAs(initialRoot));
        context.Update(new GameTime(), new MouseState(), new KeyboardState());

        Assert.Multiple(() =>
        {
            Assert.That(button.Template, Is.Not.SameAs(initialTemplate));
            Assert.That(button.TemplateRoot, Is.TypeOf<Border>());
            Assert.That(button.Text, Is.EqualTo("Retained"));
            Assert.That(button.AccessibilityPeer, Is.SameAs(initialPeer));
            Assert.That(button.AccessibilityPeer.Actions, Is.EqualTo(initialActions));
        });
    }

    [Test]
    public async Task ThemeControlTemplateReload_ReappliesEveryAffectedControl()
    {
        const string source = """
            <Control xmlns="https://forma.dev/xaml"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Control.Resources>
                    <ResourceDictionary>
                        <ControlTemplate x:Key="Chrome" TargetType="Button">
                            <Border Background="#FF16324A" Padding="8" />
                        </ControlTemplate>
                    </ResourceDictionary>
                </Control.Resources>
            </Control>
            """;
        await File.WriteAllTextAsync(Path.Combine(_directory, "Theme.xaml"), source);
        using var context = new UIContext();
        var initialTemplate = new ControlTemplate(typeof(Button), _ => new TextBlock { Text = "Initial" });
        context.Theme.SetControlTemplate<Button>(initialTemplate);
        var first = new Button { Text = "First" };
        var second = new Button { Text = "Second" };
        context.Add(first);
        context.Add(second);
        context.Layout();
        var firstRoot = first.TemplateRoot;
        var secondRoot = second.TemplateRoot;
        using var service = new FormaXamlHotReloadService(context, _directory, watchFiles: false);
        using var registration = service.RegisterThemeControlTemplate<Button>(
            new FormaXamlArtifactId("Theme.xaml", "key:Chrome", FormaXamlArtifactKind.ControlTemplate, "Chrome"),
            context.Theme);

        await service.RequestReloadAsync("Theme.xaml");
        context.Update(new GameTime(), new MouseState(), new KeyboardState());
        context.Layout();

        Assert.Multiple(() =>
        {
            Assert.That(context.Theme.GetControlTemplate(typeof(Button)), Is.Not.SameAs(initialTemplate));
            Assert.That(first.TemplateRoot, Is.TypeOf<Border>().And.Not.SameAs(firstRoot));
            Assert.That(second.TemplateRoot, Is.TypeOf<Border>().And.Not.SameAs(secondRoot));
            Assert.That(first.Text, Is.EqualTo("First"));
            Assert.That(second.Text, Is.EqualTo("Second"));
        });
        first.Dispose();
        second.Dispose();
    }

    [Test]
    public async Task DataTemplateReload_PreservesOccurrencePeersAndSelectionWhileReplacingRealizedRows()
    {
        const string source = """
            <Control xmlns="https://forma.dev/xaml"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     xmlns:sys="clr-namespace:System;assembly=System.Private.CoreLib">
                <Control.Resources>
                    <ResourceDictionary>
                        <DataTemplate x:Key="Row" x:DataType="sys:String">
                            <TextBlock Text="Reloaded" />
                        </DataTemplate>
                    </ResourceDictionary>
                </Control.Resources>
            </Control>
            """;
        await File.WriteAllTextAsync(Path.Combine(_directory, "Rows.xaml"), source);
        using var context = new UIContext();
        var initialTemplate = DataTemplate.Create<string>((_, item) => new TextBlock { Text = $"Initial {item}" });
        var list = new ListBox
        {
            ItemsPanel = new ItemsPanelTemplate(_ => new StackPanel()),
            ItemTemplate = initialTemplate,
            ItemsSource = new[] { "First", "Second", "Third" },
            SelectedIndex = 1,
        };
        var initialPeers = list.GetAccessibilityChildren().ToArray();
        var initialContainers = Enumerable.Range(0, list.RealizedCount).Select(list.GetRealizedContainer).ToArray();
        using var service = new FormaXamlHotReloadService(context, _directory, watchFiles: false);
        using var registration = service.RegisterDataTemplate(
            new FormaXamlArtifactId("Rows.xaml", "key:Row", FormaXamlArtifactKind.DataTemplate, "Row"),
            list);

        await service.RequestReloadAsync("Rows.xaml");
        context.Update(new GameTime(), new MouseState(), new KeyboardState());

        Assert.Multiple(() =>
        {
            Assert.That(list.ItemTemplate, Is.Not.SameAs(initialTemplate));
            Assert.That(list.SelectedIndex, Is.EqualTo(1));
            Assert.That(list.SelectedItem, Is.EqualTo("Second"));
            Assert.That(list.RealizedCount, Is.EqualTo(3));
            Assert.That(list.GetAccessibilityChildren(), Is.EqualTo(initialPeers));
            Assert.That(Enumerable.Range(0, list.RealizedCount).Select(list.GetRealizedContainer),
                Has.None.Matches<Control>(container => initialContainers.Contains(container)));
        });
        list.Dispose();
    }

    [Test]
    public async Task DataGridColumnTemplateReload_RebuildsRealizedCellsWithoutReplacingRowsOrSelection()
    {
        const string source = """
            <Control xmlns="https://forma.dev/xaml"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     xmlns:sys="clr-namespace:System;assembly=System.Private.CoreLib">
                <Control.Resources>
                    <ResourceDictionary>
                        <DataTemplate x:Key="Cell" x:DataType="sys:Object">
                            <TextBlock Text="Reloaded cell" />
                        </DataTemplate>
                    </ResourceDictionary>
                </Control.Resources>
            </Control>
            """;
        await File.WriteAllTextAsync(Path.Combine(_directory, "Grid.xaml"), source);
        using var context = new UIContext();
        var initialTemplate = DataTemplate.Create<object>((_, _) => new TextBlock { Text = "Initial cell" });
        var column = new DataGridTemplateColumn { Header = "Value", CellTemplate = initialTemplate };
        var grid = new DataGrid
        {
            ItemsSource = new object[] { new(), new() },
            Size = new Vector2(240, 100),
            SelectedIndex = 1,
        };
        grid.Columns.Add(column);
        context.Add(grid);
        context.Layout();
        var firstRow = grid.GetRealizedContainer(0);
        var secondRow = grid.GetRealizedContainer(1);
        using var service = new FormaXamlHotReloadService(context, _directory, watchFiles: false);
        using var registration = service.RegisterDataGridColumnTemplate(
            new FormaXamlArtifactId("Grid.xaml", "key:Cell", FormaXamlArtifactKind.DataGridColumn, "Cell"),
            column);

        await service.RequestReloadAsync("Grid.xaml");
        context.Update(new GameTime(), new MouseState(), new KeyboardState());
        context.Layout();

        Assert.Multiple(() =>
        {
            Assert.That(column.CellTemplate, Is.Not.SameAs(initialTemplate));
            Assert.That(grid.SelectedIndex, Is.EqualTo(1));
            Assert.That(grid.GetRealizedContainer(0), Is.SameAs(firstRow));
            Assert.That(grid.GetRealizedContainer(1), Is.SameAs(secondRow));
            Assert.That(grid.GetCell(0, 0).ContentTemplate, Is.SameAs(column.CellTemplate));
        });
        grid.Dispose();
    }

    [Test]
    public async Task InvalidThenValidReload_LeavesTreeAndRecovers()
    {
        var file = Path.Combine(_directory, "View.xaml");
        await File.WriteAllTextAsync(file, "<Control xmlns='https://forma.dev/xaml'>");
        using var context = new UIContext();
        Control current = new Control { Name = "Old" };
        context.Add(current);
        using var service = new FormaXamlHotReloadService(context, _directory, watchFiles: false);
        var diagnostics = new List<IReadOnlyList<Forma.Xaml.Compiler.FormaDiagnostic>>();
        service.DiagnosticsChanged += value => diagnostics.Add(value);
        using var registration = service.Register("View.xaml", () => current, (oldValue, newValue) => current = newValue);

        await service.RequestReloadAsync("View.xaml");
        context.Update(new GameTime(), new MouseState(), new KeyboardState());
        Assert.That(current.Name, Is.EqualTo("Old"));
        Assert.That(diagnostics.Last(), Is.Not.Empty);
        await File.WriteAllTextAsync(file, "<Control xmlns='https://forma.dev/xaml' Name='Recovered' />");
        await service.RequestReloadAsync("View.xaml");
        context.Update(new GameTime(), new MouseState(), new KeyboardState());
        Assert.That(current.Name, Is.EqualTo("Recovered"));
        Assert.That(diagnostics.Last(), Is.Empty);
    }

    [Test]
    public async Task StaleRootAndDisposedService_DiscardQueuedReplacement()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "View.xaml"), "<Control xmlns='https://forma.dev/xaml' Name='New' />");
        using var context = new UIContext();
        Control current = new Control { Name = "Old" };
        using var service = new FormaXamlHotReloadService(context, _directory, watchFiles: false);
        using var registration = service.Register("View.xaml", () => current, (_, newValue) => current = newValue);
        await service.RequestReloadAsync("View.xaml");
        current = new Control { Name = "External" };
        context.Update(new GameTime(), new MouseState(), new KeyboardState());
        Assert.That(current.Name, Is.EqualTo("External"));
        service.Dispose();
        Assert.That(() => service.RequestReloadAsync("View.xaml"), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public async Task FileWatcher_DebouncesRapidSavesAndAppliesLatest()
    {
        var file = Path.Combine(_directory, "View.xaml");
        await File.WriteAllTextAsync(file, "<Control xmlns='https://forma.dev/xaml' Name='Initial' />");
        using var context = new UIContext();
        Control current = new Control { Name = "Old" };
        using var service = new FormaXamlHotReloadService(context, _directory);
        using var registration = service.Register("View.xaml", () => current, (_, newValue) => current = newValue);
        var compiled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.DiagnosticsChanged += diagnostics => { if (diagnostics.Count == 0) compiled.TrySetResult(); };

        await File.WriteAllTextAsync(file, "<Control xmlns='https://forma.dev/xaml' Name='First' />");
        await File.WriteAllTextAsync(file, "<Control xmlns='https://forma.dev/xaml' Name='Latest' />");
        await compiled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(current.Name, Is.EqualTo("Old"));
        context.Update(new GameTime(), new MouseState(), new KeyboardState());
        Assert.That(current.Name, Is.EqualTo("Latest"));
    }

    private sealed record ArtifactValue(string Value);
}