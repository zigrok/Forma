// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
using Forma.Catalog;
using Forma.Xaml;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Forma.Tests;

public sealed class CatalogInventoryTest
{
    private sealed class TestClipboard : IClipboard
    {
        public string Text { get; private set; }
        public string GetText() => Text;
        public bool SetText(string text) { Text = text; return true; }
    }

    [Test]
    public void CatalogWindowTitleIdentifiesTheRuntimeBackend()
    {
        Assert.That(CatalogBackend.WindowTitle, Is.EqualTo($"Forma Catalog [{CatalogBackend.Name}]"));
        Assert.That(CatalogBackend.Name, Is.AnyOf("MonoGame", "FNA"));
    }

    [Test]
    public void StoryCatalogIncludesEveryConstructiblePublicControl()
    {
        var explicitlyConstructedTypes = new[]
        {
            typeof(BoxContainer),
            typeof(FlowContainer),
            typeof(ScrollBar),
            typeof(Slider),
            typeof(SplitContainer),
        };
        var expectedTypes = new[] { typeof(Control).Assembly, typeof(VideoStreamPlayer).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsPublic && !type.IsAbstract && typeof(Control).IsAssignableFrom(type))
            .Where(type => type.GetConstructor(Type.EmptyTypes) != null || explicitlyConstructedTypes.Contains(type))
            .Select(type => type.Name)
            .OrderBy(name => name)
            .ToArray();

        var storyNames = StoryCatalog.Create(null)
            .Select(story => story.Name)
            .OrderBy(name => name)
            .ToArray();
        var controlStoryNames = storyNames.Intersect(expectedTypes).OrderBy(name => name).ToArray();

        Assert.That(storyNames, Is.Unique);
        Assert.That(controlStoryNames, Is.EqualTo(expectedTypes));
        Assert.That(storyNames, Does.Contain("Complete icon inventory"));
        Assert.That(storyNames, Does.Contain("Override and suppression"));
        Assert.That(storyNames, Does.Contain("Atlas diagnostics"));
        Assert.That(storyNames, Does.Contain("Runtime SVG"));
        Assert.That(storyNames, Does.Contain("Dynamic Sizes"));
        Assert.That(storyNames, Does.Contain("Display Density"));
        Assert.That(storyNames, Does.Contain("Fallback Chain"));
        Assert.That(storyNames, Does.Contain("Shaping and Features"));
        Assert.That(storyNames, Does.Contain("Bidirectional Text"));
        Assert.That(storyNames, Does.Contain("Wrapping and Selection"));
        Assert.That(storyNames, Does.Contain("SpriteFont Compatibility"));
        Assert.That(storyNames, Does.Contain("Atlas Inspector"));
        Assert.That(storyNames, Does.Contain("Failure States"));
        Assert.That(storyNames, Does.Contain("Selector Styles"));
        Assert.That(storyNames, Does.Contain("Template Systems"));
        Assert.That(storyNames, Does.Contain("Composition Systems"));
        Assert.That(storyNames, Does.Contain("Collection Systems"));
        Assert.That(storyNames, Does.Contain("Storyboards and Triggers"));
        Assert.That(storyNames, Does.Contain("Compiled Data Binding"));
        Assert.That(storyNames, Does.Contain("Flat Data Grid"));
        Assert.That(storyNames, Does.Contain("Hierarchical Data Grid"));
    }

    [Test]
    public void EveryCatalogStoryHasStableDocumentationMetadata()
    {
        var stories = StoryCatalog.Create(null);

        Assert.That(stories.Select(story => story.DocumentationId), Is.Unique);
        foreach (var story in stories)
        {
            Assert.That(story.DocumentationId, Does.StartWith("catalog-"), story.Name);
            Assert.That(story.DocumentationId, Does.Not.Contain(" "), story.Name);
            Assert.That(Uri.TryCreate(story.ReferenceUrl, UriKind.Absolute, out var reference), Is.True, story.Name);
            Assert.That(reference.Scheme, Is.EqualTo(Uri.UriSchemeHttps), story.Name);
            Assert.That(reference.Host, Is.EqualTo("zigrok.github.io"), story.Name);
        }
    }

    [TestCase(nameof(Viewbox))]
    [TestCase(nameof(Border))]
    public void SingleChildContainerStoriesCanBeConstructed(string storyName)
    {
        var story = StoryCatalog.Create(null).Single(item => item.Name == storyName);

        var root = story.Factory();

        Assert.That(root.VisualChildren, Has.Count.EqualTo(1));
    }

    [Test]
    public void EveryCatalogStoryCanBeConstructed()
    {
        foreach (var story in StoryCatalog.Create(null))
        {
            Control root = null;
            Assert.DoesNotThrow(() => root = story.Factory(), $"{story.Category} / {story.Name}");
            (root as IDisposable)?.Dispose();
        }
    }

    [Test]
    public void EveryReflectedControlStoryCanAttachRuntimeData()
    {
        foreach (var story in StoryCatalog.Create(null).Where(item => item.XamlPath.StartsWith("Stories/Controls/", StringComparison.Ordinal)))
        {
            var root = story.Factory();
            Assert.DoesNotThrow(() => story.Attached?.Invoke(root), $"{story.Category} / {story.Name}");
            (root as IDisposable)?.Dispose();
        }
    }

    [Test]
    public void FileDialogStoryStartsWithBrowsableFilteredProjectContent()
    {
        var story = StoryCatalog.Create(null).Single(item => item.Name == nameof(FileDialog));
        var dialog = (FileDialog)story.Factory();
        story.Attached(dialog);

        Assert.Multiple(() =>
        {
            Assert.That(dialog.CurrentPath, Does.EndWith("Forma Project"));
            Assert.That(dialog.Entries.Select(Path.GetFileName), Is.EqualTo(new[] { "Assets", "Scenes", "Scripts", "project.json" }));
            Assert.That(dialog.Title, Is.EqualTo("Open project asset"));
            Assert.That(dialog.DialogText, Is.Empty);
        });

        dialog.ActivateEntry(Array.FindIndex(dialog.Entries.ToArray(), entry => Path.GetFileName(entry) == "Scripts"));
        Assert.That(dialog.Entries.Select(Path.GetFileName), Is.EqualTo(new[] { "PlayerController.cs", "WorldLoader.cs" }));
    }

    /// <summary>
    /// Found by walking up to the repository rather than by counting "..". The output path carries
    /// the runtime, the runtime source and the configuration, so a fixed depth breaks whenever any
    /// of those gains a segment - and it breaks as a missing-file assertion listing every story,
    /// which reads like a catastrophe rather than a moved directory.
    /// </summary>
    private static string CatalogDirectory()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "samples", "Forma.Catalog")))
            directory = directory.Parent;

        if (directory == null)
            throw new InvalidOperationException("Could not locate samples/Forma.Catalog above the test directory.");

        return Path.Combine(directory.FullName, "samples", "Forma.Catalog");
    }

    [Test]
    public void EveryCatalogStoryHasItsOwnXamlFile()
    {
        var stories = StoryCatalog.Create(null);
        var catalogDirectory = CatalogDirectory();
        var missingPaths = stories
            .Where(story => string.IsNullOrWhiteSpace(story.XamlPath))
            .Select(story => $"{story.Category} / {story.Name}")
            .ToArray();
        var duplicatePaths = stories
            .Where(story => !string.IsNullOrWhiteSpace(story.XamlPath))
            .GroupBy(story => story.XamlPath)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        var missingFiles = stories
            .Where(story => !string.IsNullOrWhiteSpace(story.XamlPath))
            .Where(story => !File.Exists(Path.Combine(catalogDirectory, story.XamlPath)))
            .Select(story => $"{story.Category} / {story.Name}: {story.XamlPath}")
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(missingPaths, Is.Empty, $"Stories without XAML:{Environment.NewLine}{string.Join(Environment.NewLine, missingPaths)}");
            Assert.That(duplicatePaths, Is.Empty, $"XAML files shared by multiple stories:{Environment.NewLine}{string.Join(Environment.NewLine, duplicatePaths)}");
            Assert.That(missingFiles, Is.Empty, $"Missing XAML files:{Environment.NewLine}{string.Join(Environment.NewLine, missingFiles)}");
        });
    }

    [Test]
    public void TemplateSystemsStoryUsesCompiledXamlChromeAndPreservesOwnerBehavior()
    {
        var story = StoryCatalog.Create(null).Single(item => item.Name == "Template Systems");
        var gallery = (TemplateGalleryStoryView)story.Factory();
        gallery.Size = new Vector2(720, 800);
        using var context = new UIContext { ViewportSize = new Vector2(720, 800) };
        context.Add(gallery);
        context.Layout();
        var scope = NameScope.GetNameScope(gallery);
        var button = scope.Find<Button>("ReferenceButton");
        var alternateButton = scope.Find<Button>("ReferenceButtonAlternate");
        var list = scope.Find<ListBox>("ReferenceList");
        var grid = scope.Find<DataGrid>("ReferenceGrid");
        var tree = scope.Find<Tree>("ReferenceTree");
        var editor = scope.Find<LineEdit>("ReferenceEditor");
        var dialog = scope.Find<ConfirmationDialog>("ReferenceDialog");
        var navigation = scope.Find<ListBox>("ReferenceNavigation");
        var confirmed = 0;
        var canceled = 0;
        var buttonActivations = 0;
        button.Pressed += (_, _) => buttonActivations++;
        alternateButton.Pressed += (_, _) => buttonActivations++;
        dialog.Confirmed += (_, _) => confirmed++;
        dialog.Canceled += (_, _) => canceled++;

        var accept = (BaseButton)dialog.GetTemplateChild(AcceptDialog.AcceptButtonPartName);
        var cancel = (BaseButton)dialog.GetTemplateChild(AcceptDialog.CancelButtonPartName);
        accept.KeyPressed(Microsoft.Xna.Framework.Input.Keys.Enter);
        accept.KeyReleased(Microsoft.Xna.Framework.Input.Keys.Enter);
        cancel.KeyPressed(Microsoft.Xna.Framework.Input.Keys.Enter);
        cancel.KeyReleased(Microsoft.Xna.Framework.Input.Keys.Enter);
        button.KeyPressed(Microsoft.Xna.Framework.Input.Keys.Enter);
        button.KeyReleased(Microsoft.Xna.Framework.Input.Keys.Enter);
        alternateButton.KeyPressed(Microsoft.Xna.Framework.Input.Keys.Enter);
        alternateButton.KeyReleased(Microsoft.Xna.Framework.Input.Keys.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(story.XamlPath, Is.EqualTo("TemplateGalleryStoryView.xaml"));
            Assert.That(button.TemplateRoot, Is.TypeOf<Border>());
            Assert.That(alternateButton.TemplateRoot, Is.TypeOf<OverlayPanel>());
            Assert.That(buttonActivations, Is.EqualTo(2));
            Assert.That(list.GetTemplateChild(ListBox.ItemsPresenterPartName), Is.TypeOf<ItemsPresenter>());
            Assert.That(grid.GetTemplateChild(DataGrid.ColumnHeadersPartName), Is.TypeOf<GridPanel>());
            Assert.That(tree.GetTemplateChild(Tree.TreePresenterPartName), Is.TypeOf<TreePresenter>());
            Assert.That(editor.GetTemplateChild(LineEdit.EditorPresenterPartName), Is.TypeOf<LineEditPresenter>());
            Assert.That(navigation.ItemsPanel.CreateInstance().Root, Is.TypeOf<StackPanel>());
            Assert.That(confirmed, Is.EqualTo(1));
            Assert.That(canceled, Is.EqualTo(1));
            Assert.That(typeof(TemplateGalleryStoryView).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly), Is.Null);
        });
    }

    [Test]
    public void CompositionSystemsStoryExercisesFoundationsSelectorsBindingsAndThemeReplacement()
    {
        var story = StoryCatalog.Create(null).Single(item => item.Name == "Composition Systems");
        var composition = (CompositionSystemsStoryView)story.Factory();
        composition.Size = new Vector2(720, 620);
        using var context = new UIContext { ViewportSize = new Vector2(900, 700) };
        context.Add(composition);
        context.Layout();
        var scope = NameScope.GetNameScope(composition);
        var gradient = scope.Find<Border>("GradientSurface");
        var shape = scope.Find<PathShape>("EffectShape");
        var grid = scope.Find<GridPanel>("ResponsiveGrid");
        var partButton = scope.Find<Button>("PartSelectorProbe");
        var part = (Border)partButton.TemplateRoot;
        var themeProbe = scope.Find<CatalogThemeButton>("ThemeProbe");
        var applyTheme = scope.Find<Button>("ApplyThemeTemplate");

        applyTheme.KeyPressed(Microsoft.Xna.Framework.Input.Keys.Enter);
        applyTheme.KeyReleased(Microsoft.Xna.Framework.Input.Keys.Enter);
        context.Layout();

        Assert.Multiple(() =>
        {
            Assert.That(story.XamlPath, Is.EqualTo("CompositionSystemsStoryView.xaml"));
            Assert.That(gradient.Background, Is.TypeOf<LinearGradientBrush>());
            Assert.That(shape.Effect, Is.TypeOf<DropShadowEffect>());
            Assert.That(grid.ColumnDefinitions, Has.Count.EqualTo(2));
            Assert.That(GridPanel.GetColumn(grid.Children[1]), Is.EqualTo(1));
            Assert.That(scope.Find<Label>("SelfRelative").Text, Is.EqualTo("SelfRelative"));
            Assert.That(scope.Find<Label>("AncestorRelative").Text, Is.EqualTo("CompositionRoot"));
            Assert.That(part.BorderThickness, Is.EqualTo(new Thickness(2)));
            Assert.That(part.Opacity, Is.EqualTo(.86f));
            Assert.That(themeProbe.TemplateRoot.Name, Is.EqualTo("ThemeReplacementChrome"));
            Assert.That(composition.ThemeReplacementCount, Is.EqualTo(1));
            Assert.That(typeof(CompositionSystemsStoryView).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly), Is.Null);
        });
    }

    [Test]
    public void CollectionSystemsStoryExercisesDeltasSelectionEventRowsAndVirtualization()
    {
        var story = StoryCatalog.Create(null).Single(item => item.Name == "Collection Systems");
        var collection = (CollectionSystemsStoryView)story.Factory();
        collection.Size = new Vector2(720, 920);
        using var context = new UIContext { ViewportSize = new Vector2(1200, 1000) };
        context.Add(collection);
        context.Layout();
        var scope = NameScope.GetNameScope(collection);
        var actions = new List<NotifyCollectionChangedAction>();
        collection.ViewModel.MutableItems.CollectionChanged += (_, args) => actions.Add(args.Action);

        static void Press(Button button)
        {
            button.KeyPressed(Microsoft.Xna.Framework.Input.Keys.Enter);
            button.KeyReleased(Microsoft.Xna.Framework.Input.Keys.Enter);
        }

        Press(scope.Find<Button>("AddItem"));
        Press(scope.Find<Button>("RemoveItem"));
        Press(scope.Find<Button>("MoveItem"));
        Press(scope.Find<Button>("ReplaceItem"));
        Press(scope.Find<Button>("ResetItems"));
        context.Layout();

        var single = scope.Find<ListBox>("SingleSelection");
        var multi = scope.Find<ListBox>("MultiSelection");
        var toggle = scope.Find<ListBox>("ToggleSelection");
        single.SelectedIndex = 0;
        multi.SelectedIndex = 0;
        toggle.SelectedIndex = 0;

        var eventTemplate = (DataTemplate)collection.Resources["EventfulRowTemplate"];
        using var eventInstance = eventTemplate.CreateInstance(new CatalogCollectionItem("Row action"));
        eventInstance.Activate();
        var eventRow = (CatalogEventfulRow)eventInstance.Root;
        Press(NameScope.GetNameScope(eventRow).Find<Button>("RowAction"));

        var vertical = scope.Find<ListBox>("VerticalVirtualList");
        var horizontal = scope.Find<ListBox>("HorizontalVirtualList");
        var grid = scope.Find<ListBox>("GridVirtualList");
        var verticalPanel = (VirtualizingStackPanel)((ItemsPresenter)vertical.GetTemplateChild(ListBox.ItemsPresenterPartName)).Panel;
        var horizontalPanel = (VirtualizingStackPanel)((ItemsPresenter)horizontal.GetTemplateChild(ListBox.ItemsPresenterPartName)).Panel;
        var gridPanel = (VirtualizingGridPanel)((ItemsPresenter)grid.GetTemplateChild(ListBox.ItemsPresenterPartName)).Panel;
        var initialContainers = verticalPanel.RealizedContainers.ToDictionary(pair => pair.Value, pair => pair.Key);
        foreach (var container in initialContainers.Keys.Cast<ListBoxItem>()) container.IsSelectable = false;
        vertical.ScrollOffset = new Vector2(0, 3400);
        horizontal.ScrollOffset = new Vector2(13_200, 0);
        grid.ScrollOffset = new Vector2(0, 3600);
        context.Layout();
        var reusedContainers = verticalPanel.RealizedContainers
            .Where(pair => initialContainers.TryGetValue(pair.Value, out var previousIndex) && previousIndex != pair.Key)
            .Select(pair => (ListBoxItem)pair.Value)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(story.XamlPath, Is.EqualTo("CollectionSystemsStoryView.xaml"));
            Assert.That(actions, Does.Contain(NotifyCollectionChangedAction.Add));
            Assert.That(actions, Does.Contain(NotifyCollectionChangedAction.Remove));
            Assert.That(actions, Does.Contain(NotifyCollectionChangedAction.Move));
            Assert.That(actions, Does.Contain(NotifyCollectionChangedAction.Replace));
            Assert.That(actions, Does.Contain(NotifyCollectionChangedAction.Reset));
            Assert.That(scope.Find<Label>("MutationStatus").Text, Is.EqualTo("Reset"));
            Assert.That(single.SelectionMode, Is.EqualTo(ItemListSelectionMode.Single));
            Assert.That(multi.SelectionMode, Is.EqualTo(ItemListSelectionMode.Multi));
            Assert.That(toggle.SelectionMode, Is.EqualTo(ItemListSelectionMode.Toggle));
            Assert.That(single.HasSelection && multi.HasSelection && toggle.HasSelection, Is.True);
            Assert.That(eventRow.HandlerCalls, Is.EqualTo(1));
            Assert.That(verticalPanel.RealizedCount, Is.LessThan(64));
            Assert.That(horizontalPanel.RealizedCount, Is.LessThan(64));
            Assert.That(gridPanel.RealizedCount, Is.LessThan(64));
            Assert.That(vertical.ScrollOffset.Y, Is.GreaterThan(0));
            Assert.That(horizontal.ScrollOffset.X, Is.GreaterThan(0));
            Assert.That(grid.ScrollOffset.Y, Is.GreaterThan(0));
            Assert.That(verticalPanel.RealizedContainers.Keys, Has.None.Matches<int>(initialContainers.ContainsValue));
            Assert.That(reusedContainers, Is.Not.Empty);
            Assert.That(reusedContainers, Has.All.Matches<ListBoxItem>(container => container.IsSelectable));
            Assert.That(vertical.RealizationDiagnostics, Is.Empty);
            Assert.That(horizontal.RealizationDiagnostics, Is.Empty);
            Assert.That(grid.RealizationDiagnostics, Is.Empty);
        });
    }

    [Test]
    public void XamlFeatureStoriesExposeStylesAnimationsAndTypedBindings()
    {
        var stories = StoryCatalog.Create(null);
        var styleStory = stories.Single(story => story.Name == "Selector Styles");
        var animationStory = stories.Single(story => story.Name == "Storyboards and Triggers");
        var bindingStory = stories.Single(story => story.Name == "Compiled Data Binding");
        var styleRoot = styleStory.Factory();
        var animationRoot = (AnimationsStoryView)animationStory.Factory();
        var bindingRoot = (DataBindingStoryView)bindingStory.Factory();
        using var context = new UIContext();
        context.Add(styleRoot);
        context.Add(animationRoot);
        context.Add(bindingRoot);

        var styleScope = NameScope.GetNameScope(styleRoot);
        var selectedCard = styleScope.Find<ColorRect>("SelectedCard");
        var bindingScope = NameScope.GetNameScope(bindingRoot);
        bindingScope.Find<LineEdit>("ProjectNameEditor").Text = "Atlas Tools";
        bindingScope.Find<HSlider>("CompletionSlider").Value = 82;
        var animationScope = NameScope.GetNameScope(animationRoot);
        var loopTarget = animationScope.Find<ColorRect>("LoopTarget");
        var initialLoopColor = loopTarget.Color;
        animationRoot.ViewModel.IsLooping = true;
        context.Update(
            new GameTime(TimeSpan.FromSeconds(.4), TimeSpan.FromSeconds(.4)),
            new Microsoft.Xna.Framework.Input.MouseState(),
            new Microsoft.Xna.Framework.Input.KeyboardState());

        Assert.Multiple(() =>
        {
            Assert.That(styleStory.XamlPath, Is.EqualTo("StylesStoryView.xaml"));
            Assert.That(animationStory.XamlPath, Is.EqualTo("AnimationsStoryView.xaml"));
            Assert.That(bindingStory.XamlPath, Is.EqualTo("DataBindingStoryView.xaml"));
            Assert.That(selectedCard.Color, Is.EqualTo(new Color(246, 185, 73)));
            Assert.That(bindingRoot.ViewModel.ProjectName, Is.EqualTo("Atlas Tools"));
            Assert.That(bindingRoot.ViewModel.Completion, Is.EqualTo(82));
            Assert.That(bindingScope.Find<Label>("BindingSummary").Text, Does.Contain("Atlas Tools is 82% complete"));
            Assert.That(loopTarget.Color, Is.Not.EqualTo(initialLoopColor));
        });
    }

    [Test]
    public void CatalogInspectorEditsTheBindingStoryViewModel()
    {
        var font = CreateTestFont();
        var shell = new CatalogShell(StoryCatalog.Create(null), font, font);
        using var context = new UIContext();
        context.Add(shell);
        Assert.That(shell.SelectStory("Compiled Data Binding"), Is.True);

        var bindingRoot = (DataBindingStoryView)shell.ActiveStoryControl;
        var bindingScope = NameScope.GetNameScope(bindingRoot);
        var inspector = NameScope.GetNameScope(shell).Find<VBoxContainer>("Inspector");
        var sections = inspector.Children.Cast<VBoxContainer>()
            .ToDictionary(section => ((Label)section.Children[0]).Text, section => section.Children[1]);
        var projectName = (LineEdit)sections["Project Name"];
        var completionRow = (HBoxContainer)sections["Completion"];
        var completion = (HSlider)completionRow.Children[0];
        var autoSave = (CheckBox)sections["Auto Save Enabled"];

        projectName.Text = "Atlas Tools";
        completion.Value = 82;
        autoSave.ButtonPressed = false;
        bindingRoot.ViewModel.ProjectName = "Forma Studio";

        Assert.Multiple(() =>
        {
            Assert.That(bindingRoot.ViewModel.ProjectName, Is.EqualTo("Forma Studio"));
            Assert.That(bindingRoot.ViewModel.Completion, Is.EqualTo(82));
            Assert.That(bindingRoot.ViewModel.AutoSaveEnabled, Is.False);
            Assert.That(projectName.Text, Is.EqualTo("Forma Studio"));
            Assert.That(completion.MinValue, Is.Zero);
            Assert.That(completion.MaxValue, Is.EqualTo(100));
            Assert.That(bindingScope.Find<Label>("BindingSummary").Text, Is.EqualTo("Forma Studio is 82% complete · autosave off"));
        });
    }

    [Test]
    public void LineEditStoryExposesTextAndVerticalAlignmentInTheInspector()
    {
        var font = CreateTestFont();
        var shell = new CatalogShell(StoryCatalog.Create(null), font, font);
        using var context = new UIContext();
        context.Add(shell);
        Assert.That(shell.SelectStory(nameof(LineEdit)), Is.True);

        var lineEdit = (LineEdit)shell.ActiveStoryControl;
        var inspector = NameScope.GetNameScope(shell).Find<VBoxContainer>("Inspector");
        var sections = inspector.Children.Cast<VBoxContainer>()
            .ToDictionary(section => ((Label)section.Children[0]).Text, section => section.Children[1]);
        var text = (LineEdit)sections["Text"];
        var alignment = (OptionButton)sections["Text Vertical Alignment"];
        var bottomIndex = alignment.Items.ToList().IndexOf(nameof(VerticalAlignment.Bottom));

        text.Text = "Inspector value";
        alignment.Select(bottomIndex, emitSignal: true);

        Assert.Multiple(() =>
        {
            Assert.That(lineEdit.CustomMinimumSize, Is.EqualTo(new Vector2(300, 84)));
            Assert.That(lineEdit.Text, Is.EqualTo("Inspector value"));
            Assert.That(lineEdit.TextVerticalAlignment, Is.EqualTo(VerticalAlignment.Bottom));
            Assert.That(alignment.Selected, Is.EqualTo(bottomIndex));
        });
    }

    [Test]
    public void CatalogInspectorOnlyShowsApplicableButtonSettings()
    {
        var font = CreateTestFont();
        var shell = new CatalogShell(StoryCatalog.Create(null), font, font);
        using var context = new UIContext();
        context.Add(shell);
        Assert.That(shell.SelectStory(nameof(Button)), Is.True);

        var button = (Button)shell.ActiveStoryControl;
        var inspector = NameScope.GetNameScope(shell).Find<VBoxContainer>("Inspector");
        var buttonSections = inspector.Children.Cast<VBoxContainer>()
            .ToDictionary(section => ((Label)section.Children[0]).Text, section => section.Children[1]);
        ((LineEdit)buttonSections["Text"]).Text = "Run build";
        ((CheckBox)buttonSections["Flat"]).ButtonPressed = true;
        var opacity = ((HBoxContainer)buttonSections["Opacity"]).Children.OfType<HSlider>().Single();
        opacity.Value = .5f;

        Assert.Multiple(() =>
        {
            Assert.That(button.Text, Is.EqualTo("Run build"));
            Assert.That(button.Flat, Is.True);
            Assert.That(button.Opacity, Is.EqualTo(.5f));
            Assert.That(opacity.MinValue, Is.Zero);
            Assert.That(opacity.MaxValue, Is.EqualTo(1));
            Assert.That(buttonSections.Keys, Does.Not.Contain("Button Pressed"));
            Assert.That(buttonSections.Keys, Does.Not.Contain("Action Mode"));
            Assert.That(buttonSections.Keys, Does.Not.Contain("Button Mask"));
            Assert.That(buttonSections.Keys, Does.Not.Contain("Expand Icon"));
            Assert.That(buttonSections.Keys, Does.Not.Contain("Shortcut Feedback"));
            Assert.That(buttonSections.Keys, Does.Not.Contain("Width"));
        });

        Assert.That(shell.SelectStory(nameof(CheckBox)), Is.True);
        var checkBox = (CheckBox)shell.ActiveStoryControl;
        inspector = NameScope.GetNameScope(shell).Find<VBoxContainer>("Inspector");
        var checkBoxSections = inspector.Children.Cast<VBoxContainer>()
            .ToDictionary(section => ((Label)section.Children[0]).Text, section => section.Children[1]);
        ((CheckBox)checkBoxSections["Checked"]).ButtonPressed = false;

        Assert.Multiple(() =>
        {
            Assert.That(checkBox.Checked, Is.False);
            Assert.That(checkBoxSections.Keys, Does.Contain("Checked"));
            Assert.That(checkBoxSections.Keys, Does.Not.Contain("Button Pressed"));
            Assert.That(checkBoxSections.Keys, Does.Not.Contain("Toggle Mode"));
        });

        Assert.That(shell.SelectStory(nameof(GridContainer)), Is.True);
        var grid = (GridContainer)shell.ActiveStoryControl;
        context.Layout();
        var initialThirdItemPosition = grid.Children[2].Position;
        inspector = NameScope.GetNameScope(shell).Find<VBoxContainer>("Inspector");
        var gridSections = inspector.Children.Cast<VBoxContainer>()
            .ToDictionary(section => ((Label)section.Children[0]).Text, section => section.Children[1]);
        var columns = ((HBoxContainer)gridSections["Columns"]).Children.OfType<HSlider>().Single();
        var horizontalSeparation = ((HBoxContainer)gridSections["Horizontal Separation"]).Children.OfType<HSlider>().Single();
        columns.Value = 2;
        horizontalSeparation.Value = 12;
        context.Layout();

        Assert.Multiple(() =>
        {
            Assert.That(grid.Columns, Is.EqualTo(2));
            Assert.That(grid.HorizontalSeparation, Is.EqualTo(12));
            Assert.That(grid.Children[2].Position, Is.Not.EqualTo(initialThirdItemPosition));
            Assert.That(columns.MinValue, Is.Zero);
            Assert.That(float.IsFinite(horizontalSeparation.Value), Is.True);
        });
    }

    [Test]
    public void EveryCatalogInspectorBuildsFiniteEditors()
    {
        var font = CreateTestFont();
        var stories = StoryCatalog.Create(null);
        var shell = new CatalogShell(stories, font, font);
        using var context = new UIContext();
        context.Add(shell);

        foreach (var story in stories.Where(story => story.Name != "Override and suppression"))
        {
            Assert.That(shell.SelectStory(story.Name), Is.True, story.Name);
            var inspector = NameScope.GetNameScope(shell).Find<VBoxContainer>("Inspector");
            foreach (var slider in inspector.Children.SelectMany(section => section.Children).SelectMany(Flatten).OfType<HSlider>())
            {
                Assert.That(float.IsFinite(slider.MinValue), Is.True, $"{story.Name} has a non-finite inspector minimum.");
                Assert.That(float.IsFinite(slider.MaxValue), Is.True, $"{story.Name} has a non-finite inspector maximum.");
                Assert.That(float.IsFinite(slider.Value), Is.True, $"{story.Name} has a non-finite inspector value.");
            }
        }
    }

    [Test]
    public void DataGridStoriesExerciseCompiledFlatAndHierarchicalWorkflows()
    {
        var stories = StoryCatalog.Create(null);
        var flatStory = stories.Single(story => story.Name == "Flat Data Grid");
        var hierarchyStory = stories.Single(story => story.Name == "Hierarchical Data Grid");
        var flat = (FlatDataGridStoryView)flatStory.Factory();
        var hierarchy = (HierarchicalDataGridStoryView)hierarchyStory.Factory();
        flat.Size = new Vector2(760, 480);
        hierarchy.Size = new Vector2(760, 480);
        using var context = new UIContext { ViewportSize = new Vector2(1600, 900) };
        context.Add(flat);
        context.Add(hierarchy);
        context.Layout();
        var flatScope = NameScope.GetNameScope(flat);
        var flatGrid = flatScope.Find<DataGrid>("FlatGrid");
        var filter = flatScope.Find<LineEdit>("GridFilter");

        flatGrid.ActivateColumnHeader(2);
        filter.Text = "Contributor 0001";
        context.Layout();
        flatGrid.SelectCell(new CellIndex(flatGrid.GetRowPath(0), 0));
        Assert.Multiple(() =>
        {
            Assert.That(flatStory.XamlPath, Is.EqualTo("FlatDataGridStoryView.xaml"));
            Assert.That(flatGrid.SortDescriptions, Has.Count.EqualTo(1));
            Assert.That(flatGrid.RealizedCount, Is.EqualTo(1));
            Assert.That(flatGrid.SelectedCells, Has.Count.EqualTo(1));
            Assert.That(flatGrid.Columns[0].CellTemplate, Is.TypeOf<DataTemplate>());
            Assert.That(flatGrid.Viewport.X, Is.GreaterThan(0));
            Assert.That(flatGrid.Viewport.Y, Is.GreaterThan(0));
            Assert.That(flatGrid.GetRealizedContainer(0).Bounds.Width, Is.GreaterThan(0));
            Assert.That(flatGrid.GetRealizedContainer(0).Bounds.Height, Is.GreaterThan(0));
            Assert.That(flatGrid.GetCell(0, 0).Bounds.Width, Is.GreaterThan(0));
            Assert.That(flatGrid.GetCell(0, 0).Bounds.Height, Is.GreaterThan(0));
        });

        var hierarchyScope = NameScope.GetNameScope(hierarchy);
        var treeGrid = hierarchyScope.Find<DataGrid>("TreeGrid");
        var treeFilter = hierarchyScope.Find<LineEdit>("TreeFilter");
        var addChild = Flatten(hierarchy).OfType<Button>().Single(button => button.Text == "Add child");
        var rootPath = treeGrid.GetRowPath(0);
        var childCount = hierarchy.ViewModel.Roots[0].Children.Count;
        treeGrid.ActivateColumnHeader(1);
        addChild.KeyPressed(Microsoft.Xna.Framework.Input.Keys.Enter);
        addChild.KeyReleased(Microsoft.Xna.Framework.Input.Keys.Enter);
        treeFilter.Text = "Live document 1";
        context.Layout();
        treeGrid.SelectRowHeader(rootPath);
        Assert.Multiple(() =>
        {
            Assert.That(hierarchyStory.XamlPath, Is.EqualTo("HierarchicalDataGridStoryView.xaml"));
            Assert.That(hierarchy.ViewModel.Roots[0].Children, Has.Count.EqualTo(childCount + 1));
            Assert.That(treeGrid.SortDescriptions, Has.Count.EqualTo(1));
            Assert.That(treeGrid.FilterMode, Is.EqualTo(DataGridFilterMode.IncludeAncestorsOfMatches));
            Assert.That(treeGrid.HierarchySource.IndexOfPath(rootPath), Is.GreaterThanOrEqualTo(0));
            Assert.That(treeGrid.HierarchySource.IsExpanded(rootPath), Is.True);
            Assert.That(treeGrid.SelectedRowPaths, Does.Contain(rootPath));
            Assert.That(treeGrid.RealizedCount, Is.LessThan(4));
            Assert.That(treeGrid.RealizedCellCount, Is.EqualTo(treeGrid.RealizedCount * 2));
            Assert.That(treeGrid.Viewport.X, Is.GreaterThan(0));
            Assert.That(treeGrid.Viewport.Y, Is.GreaterThan(0));
            Assert.That(treeGrid.GetRealizedContainer(0).Bounds.Width, Is.GreaterThan(0));
            Assert.That(treeGrid.GetRealizedContainer(0).Bounds.Height, Is.GreaterThan(0));
            Assert.That(treeGrid.GetCell(0, 0).Bounds.Width, Is.GreaterThan(0));
            Assert.That(treeGrid.GetCell(0, 0).Bounds.Height, Is.GreaterThan(0));
        });
    }

    [TestCase("Flat Data Grid", "FlatGrid")]
    [TestCase("Hierarchical Data Grid", "TreeGrid")]
    public void DataGridStoriesRemainVisibleWhenHostedInCatalogShell(string storyName, string gridName)
    {
        var font = CreateTestFont();
        var uiFont = new SpriteFontAdapter(font);
        var shell = new CatalogShell(StoryCatalog.Create(null), uiFont, uiFont, font) { Size = new Vector2(1440, 720) };
        using var context = new UIContext { ViewportSize = shell.Size };
        context.Theme.FontFamily = new UIFontFamily(new UIFont[] { uiFont });
        context.Add(shell);
        Assert.That(shell.SelectStory(storyName), Is.True);
        context.Layout();
        context.Layout();

        var grid = NameScope.GetNameScope(shell.ActiveStoryControl).Find<DataGrid>(gridName);
        var preview = NameScope.GetNameScope(shell).Find<CatalogPreviewContainer>("Preview");
        var cell = grid.GetCell(0, 0);
        var row = (DataGridRow)grid.GetRealizedContainer(0);
        var rowContent = (ContentPresenter)row.GetTemplateChild(ContentControl.ContentPresenterPartName);
        var text = cell.ContentTemplate == null
            ? Flatten(cell).OfType<TextBlock>().First()
            : (TextBlock)((ContentPresenter)cell.GetTemplateChild(ContentControl.ContentPresenterPartName)).PresentedControl;
        var scrollPresenter = (ScrollPresenter)grid.GetTemplateChild(ListBox.ScrollPresenterPartName);
        var visibleBounds = Rectangle.Intersect(cell.VisualBounds, scrollPresenter.Bounds);
        var hit = context.HitTest(visibleBounds.Center);
        var hitInsideCell = false;
        var hitPath = new List<string>();
        for (var control = hit; control != null; control = control.VisualParent)
        {
            hitPath.Add(control.GetType().Name);
            if (!ReferenceEquals(control, cell)) continue;
            hitInsideCell = true;
            break;
        }

        Assert.Multiple(() =>
        {
            Assert.That(grid.RealizedCount, Is.GreaterThan(0));
            Assert.That(text.Text, Is.Not.Empty);
            Assert.That(text.EffectiveUIFont, Is.Not.Null);
            Assert.That(visibleBounds.Width, Is.GreaterThan(0));
            Assert.That(visibleBounds.Height, Is.GreaterThan(0));
            Assert.That(hit, Is.Not.Null);
            Assert.That(hitInsideCell, Is.True,
                $"{string.Join(" -> ", hitPath)}; point={visibleBounds.Center}; cell={cell.VisualBounds}; row={row.VisualBounds}; rowContent={rowContent.VisualBounds}; presented={rowContent.PresentedControl?.VisualBounds}; hitTest={rowContent.IsHitTestVisible}");
            Assert.That(shell.ActiveStoryControl.VisualBounds.Bottom, Is.LessThanOrEqualTo(preview.VisualBounds.Bottom),
                $"story={shell.ActiveStoryControl.VisualBounds}; preview={preview.VisualBounds}");
            Assert.That(grid.VisualBounds.Bottom, Is.LessThanOrEqualTo(preview.VisualBounds.Bottom),
                $"grid={grid.VisualBounds}; preview={preview.VisualBounds}");
            Assert.That(scrollPresenter.VisualBounds.Bottom, Is.LessThanOrEqualTo(preview.VisualBounds.Bottom),
                $"scroll={scrollPresenter.VisualBounds}; preview={preview.VisualBounds}");
            if (grid.VerticalScrollBar.Visible)
                Assert.That(grid.VerticalScrollBar.VisualBounds.Bottom, Is.LessThanOrEqualTo(preview.VisualBounds.Bottom),
                    $"bar={grid.VerticalScrollBar.VisualBounds}; preview={preview.VisualBounds}");
        });
    }

    [Test]
    public void CatalogNavigationScrollsWhenStoriesExceedTheSidebarViewport()
    {
        var font = CreateTestFont();
        var shell = new CatalogShell(StoryCatalog.Create(null), font, font) { Size = new Vector2(1280, 720) };
        using var context = new UIContext { ViewportSize = shell.Size };
        context.Add(shell);
        context.Layout();
        var navigation = NameScope.GetNameScope(shell).Find<Tree>("Navigation");

        Assert.Multiple(() =>
        {
            Assert.That(navigation, Is.Not.Null);
            Assert.That(navigation.IsVerticalScrollBarVisible, Is.True);
            Assert.That(navigation.GetVScrollBar().MaxValue, Is.GreaterThan(0));
        });

        var navigationPosition = navigation.Position;
        var point = new Point(navigation.Bounds.Center.X, navigation.Bounds.Top + 10);
        context.Update(new GameTime(), new Microsoft.Xna.Framework.Input.MouseState(
            point.X, point.Y, 0,
            Microsoft.Xna.Framework.Input.ButtonState.Released,
            Microsoft.Xna.Framework.Input.ButtonState.Released,
            Microsoft.Xna.Framework.Input.ButtonState.Released,
            Microsoft.Xna.Framework.Input.ButtonState.Released,
            Microsoft.Xna.Framework.Input.ButtonState.Released),
            new Microsoft.Xna.Framework.Input.KeyboardState());
        context.Update(new GameTime(), new Microsoft.Xna.Framework.Input.MouseState(
            point.X, point.Y, -120,
            Microsoft.Xna.Framework.Input.ButtonState.Released,
            Microsoft.Xna.Framework.Input.ButtonState.Released,
            Microsoft.Xna.Framework.Input.ButtonState.Released,
            Microsoft.Xna.Framework.Input.ButtonState.Released,
            Microsoft.Xna.Framework.Input.ButtonState.Released),
            new Microsoft.Xna.Framework.Input.KeyboardState());
        context.Layout();

        Assert.Multiple(() =>
        {
            Assert.That(navigation.GetScroll().Y, Is.GreaterThan(0));
            Assert.That(navigation.Position, Is.EqualTo(navigationPosition));
        });
    }

    [Test]
    public void CatalogNavigationGroupsStoriesBySlashDelimitedCategoryInTree()
    {
        var font = CreateTestFont();
        var stories = new[]
        {
            new ComponentStory("Inputs", "Button", string.Empty, () => new Button()),
            new ComponentStory("Data / Collections", "Tree", string.Empty, () => new Tree()),
            new ComponentStory("Data / Collections", "ItemList", string.Empty, () => new ItemList()),
        };
        var shell = new CatalogShell(stories, font, font) { Size = new Vector2(1280, 720) };
        using var context = new UIContext { ViewportSize = shell.Size };
        context.Add(shell);
        context.Layout();
        var scope = NameScope.GetNameScope(shell);
        var navigation = scope.Find<Tree>("Navigation");
        var root = navigation.GetRoot();

        Assert.Multiple(() =>
        {
            Assert.That(navigation.HideRoot, Is.True);
            Assert.That(root.Selectable, Is.False);
            Assert.That(root.Children.Select(item => item.Text), Is.EqualTo(new[] { "Inputs", "Data" }));
            Assert.That(root.Children[1].Children.Single().Text, Is.EqualTo("Collections"));
            Assert.That(root.Children[1].Children.Single().Children.Select(item => item.Text), Is.EqualTo(new[] { "Tree", "ItemList" }));
            Assert.That(root.Children[1].Children.Single().Children, Has.All.Property(nameof(TreeItem.Metadata)).TypeOf<ComponentStory>());
        });

        var treeLeaf = root.Children[1].Children.Single().Children[0];
        navigation.Select(treeLeaf);
        Assert.That(shell.ActiveStory.Name, Is.EqualTo("Tree"));

        scope.Find<LineEdit>("Search").Text = "ItemList";
        context.Layout();
        root = navigation.GetRoot();
        Assert.Multiple(() =>
        {
            Assert.That(root.Children.Select(item => item.Text), Is.EqualTo(new[] { "Data" }));
            Assert.That(root.Children.Single().Children.Single().Children.Select(item => item.Text), Is.EqualTo(new[] { "ItemList" }));
        });
    }

    [Test]
    public void CatalogWorkspaceFillsAvailableWidthAfterWindowResize()
    {
        var font = CreateTestFont();
        var shell = new CatalogShell(StoryCatalog.Create(null), font, font) { Size = new Vector2(1000, 720) };
        using var context = new UIContext { ViewportSize = shell.Size };
        context.Add(shell);
        context.Layout();
        var scope = NameScope.GetNameScope(shell);
        var workspace = scope.Find<PanelContainer>("NavigationPanel").Parent as BoxContainer;
        var previewPanel = scope.Find<PanelContainer>("PreviewPanel");
        var initialPreviewWidth = previewPanel.Size.X;

        shell.Size = new Vector2(1280, 720);
        context.ViewportSize = shell.Size;
        context.Layout();

        Assert.Multiple(() =>
        {
            Assert.That(workspace.Size.X, Is.EqualTo(shell.Size.X));
            Assert.That(previewPanel.Size.X, Is.EqualTo(initialPreviewWidth + 280));
            Assert.That(workspace.Bounds.Right, Is.EqualTo(shell.Bounds.Right));
        });
    }

    [Test]
    public void CatalogPreviewScrollsStoriesThatExceedTheWindowHeight()
    {
        var font = CreateTestFont();
        var shell = new CatalogShell(StoryCatalog.Create(null), font, font) { Size = new Vector2(1280, 520) };
        using var context = new UIContext { ViewportSize = shell.Size };
        context.Add(shell);
        Assert.That(shell.SelectStory("Letter Spacing"), Is.True);
        context.Layout();
        context.Layout();

        var scope = NameScope.GetNameScope(shell);
        var preview = scope.Find<CatalogPreviewContainer>("Preview");
        ScrollContainer scroll = null;
        for (var parent = preview.VisualParent; parent != null; parent = parent.VisualParent)
        {
            if (parent is not ScrollContainer candidate) continue;
            scroll = candidate;
            break;
        }

        Assert.Multiple(() =>
        {
            Assert.That(scroll, Is.Not.Null);
            Assert.That(scroll.ClipContents, Is.True);
            Assert.That(scroll.VisualBounds.Bottom, Is.LessThanOrEqualTo(shell.VisualBounds.Bottom));
            Assert.That(preview.GetMinimumSize().Y, Is.GreaterThan(scroll.Viewport.Y));
            Assert.That(scroll.MaxScrollOffset.Y, Is.GreaterThan(0));
            Assert.That(scroll.HorizontalScrollBar.Visible, Is.False);
        });

        Assert.That(scroll.PointerWheel(-120), Is.True);
        Assert.That(scroll.VerticalScroll, Is.GreaterThan(0));
    }

    [Test]
    public void CatalogInteractionExceptionsIdentifyTheStoryAndHitControl()
    {
        var story = new ComponentStory("Collections", "Broken Grid", "Test", () => new DataGrid());
        var grid = new DataGrid { Name = "ExampleGrid", ItemsSource = new[] { "First" }, Size = new Vector2(320, 120) };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Name",
            Binding = DataGridBinding<string>.Create<string>(value => value),
        });
        using var context = new UIContext { ViewportSize = grid.Size };
        context.Add(grid);
        context.Layout();
        var header = grid.GetColumnHeader(0);
        header.Name = "NameHeader";
        var cause = new InvalidOperationException("Missing typed binding.");

        var exception = CatalogGame.CreateInteractionException(story, header, cause);

        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("Collections / Broken Grid"));
            Assert.That(exception.Message, Does.Contain("DataGridColumnHeader#NameHeader"));
            Assert.That(exception.Message, Does.Contain("DataGrid#ExampleGrid"));
            Assert.That(exception.Message, Does.Contain("Missing typed binding."));
            Assert.That(exception.InnerException, Is.SameAs(cause));
        });
    }

    [Test]
    public void CatalogDrawExceptionsIdentifyTheStory()
    {
        var story = new ComponentStory("Composition", "Broken Clip", "Test", () => new Border());
        var cause = new InvalidOperationException("Capture failed.");

        var exception = CatalogGame.CreateDrawException(story, cause);

        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("Composition / Broken Clip"));
            Assert.That(exception.Message, Does.Contain("Capture failed."));
            Assert.That(exception.InnerException, Is.SameAs(cause));
        });
    }

    [TestCase("Display Density", "densityStatus")]
    [TestCase("Fallback Chain", "fallbackPreview")]
    [TestCase("Wrapping and Selection", "wrappingDiagnostics")]
    [TestCase("Atlas Inspector", "atlasStatus")]
    public void TypographyDiagnosticStoriesExposeTheirInteractiveSurface(string storyName, string expectedControlName)
    {
        var story = StoryCatalog.Create(null).Single(story => story.Name == storyName);
        var root = story.Factory();

        Assert.Multiple(() =>
        {
            Assert.That(story.Category, Is.EqualTo("Typography"));
            Assert.That(root.Children.SelectMany(Flatten).Select(control => control.Name), Does.Contain(expectedControlName));
        });
    }

    [Test]
    public void RuntimeSvgStoryExposesCompiledFilePolicyAndFailureSurfaces()
    {
        var story = StoryCatalog.Create(null).Single(item => item.Name == "Runtime SVG");
        var root = story.Factory();
        var names = root.Children.SelectMany(Flatten).Select(control => control.Name).ToArray();
        root.Size = new Vector2(640, 320);
        using var context = new UIContext();
        context.Add(root);
        context.Layout();
        var scroll = root.Children.OfType<ScrollContainer>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(story.Category, Is.EqualTo("Theme icons"));
            Assert.That(story.XamlPath, Is.EqualTo("RuntimeSvgStoryView.xaml"));
            Assert.That(names, Does.Contain("compiledSvg"));
            Assert.That(names, Does.Contain("fileSvg"));
            Assert.That(names, Does.Contain("runtimePolicy"));
            Assert.That(names, Does.Contain("bitmapPolicy"));
            Assert.That(names, Does.Contain("invalidStatus"));
            Assert.That(scroll.Size.Y, Is.LessThanOrEqualTo(root.Size.Y));
            Assert.That(scroll.MaxScrollOffset.Y, Is.GreaterThan(0));
            Assert.That(scroll.ClipContents, Is.True);
        });
    }

    [Test]
    public void GraphEditStoryFillsTheAvailablePreviewSurface()
    {
        var font = CreateTestFont();
        var stories = StoryCatalog.Create(null);
        var shell = new CatalogShell(stories, font, font) { Size = new Vector2(1600, 900) };
        using var context = new UIContext();
        context.Add(shell);
        Assert.That(shell.SelectStory(nameof(GraphEdit)), Is.True);

        context.Layout();

        var graphEdit = Flatten(shell).OfType<GraphEdit>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(graphEdit.Position, Is.EqualTo(Vector2.Zero));
            Assert.That(graphEdit.Size, Is.EqualTo(graphEdit.Parent.Size));
            Assert.That(graphEdit.Size.X, Is.GreaterThan(graphEdit.CustomMinimumSize.X));
            Assert.That(graphEdit.Size.Y, Is.GreaterThan(graphEdit.CustomMinimumSize.Y));
        });
    }

    [Test]
    public void CatalogHotReloadCallbacksRetainActiveStoryModelAndPreviewSlot()
    {
        var font = CreateTestFont();
        var stories = StoryCatalog.Create(null);
        var shell = new CatalogShell(stories, font, font);
        var clipboard = new TestClipboard();
        using var context = new UIContext { Clipboard = clipboard };
        context.Add(shell);
        Assert.That(shell.SelectStory("Dynamic Sizes"), Is.True);
        var retainedModel = shell.ActiveStoryControl.DataContext;
        var replacementStory = new DynamicSizesStoryView { DataContext = retainedModel };

        shell.ReplaceActiveStory(shell.ActiveStoryControl, replacementStory);
        var sourceRoot = typeof(CatalogShell).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(metadata => metadata.Key == "FormaCatalogXamlRoot")?.Value;
        if (sourceRoot == null) Assert.Ignore("Catalog XAML source metadata is intentionally Debug-only.");
        var sourcePath = Path.Combine(sourceRoot, "CatalogShell.xaml");
        shell.ReportHotReloadDiagnostics(0, null);
        Assert.That(NameScope.GetNameScope(shell).Find<Label>("HotReloadStatus").Text, Is.EqualTo("XAML hot reload ready"));
        var diagnostics = "FXAML2501 invalid template part\n/Projects/Forma/CatalogShell.xaml:42\n\nFXAML2502 missing target";
        shell.ReportHotReloadDiagnostics(2, diagnostics);
        var replacementShellTree = (BoxContainer)Forma.Xaml.Compiler.FormaXamlCompiler.CreateSre(typeof(CatalogShell).Assembly.GetName().Name)
            .CompileSre(File.ReadAllText(sourcePath), "CatalogShell.xaml").Build(null);
        replacementShellTree.DataContext = shell.DataContext;
        shell.ApplyHotReloadedTree(replacementShellTree);
        var diagnosticScope = NameScope.GetNameScope(shell);
        var details = diagnosticScope.Find<TextEdit>("HotReloadDetails");
        var copyDetails = diagnosticScope.Find<Button>("CopyHotReloadDetails");
        copyDetails.KeyPressed(Microsoft.Xna.Framework.Input.Keys.Enter);
        copyDetails.KeyReleased(Microsoft.Xna.Framework.Input.Keys.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(shell.ActiveStory.Name, Is.EqualTo("Dynamic Sizes"));
            Assert.That(shell.ActiveStory.XamlPath, Is.EqualTo("DynamicSizesStoryView.xaml"));
            Assert.That(shell.ActiveStoryControl.DataContext, Is.SameAs(retainedModel));
            Assert.That(shell.ActiveStoryControl.Parent.Name, Is.EqualTo("Preview"));
            Assert.That(diagnosticScope.Find<Label>("HotReloadStatus").Text, Is.EqualTo("XAML: 2 issues"));
            Assert.That(diagnosticScope.Find<PanelContainer>("HotReloadDiagnosticsPanel").Visible, Is.True);
            Assert.That(details.Text, Is.EqualTo(diagnostics));
            Assert.That(details.Editable, Is.False);
            Assert.That(details.ContextMenuEnabled, Is.True);
            Assert.That(clipboard.Text, Is.EqualTo(diagnostics));
        });

        shell.ReportHotReloadDiagnostics(0, null);
        Assert.Multiple(() =>
        {
            Assert.That(diagnosticScope.Find<Label>("HotReloadStatus").Text, Is.EqualTo("XAML hot reload ready"));
            Assert.That(diagnosticScope.Find<PanelContainer>("HotReloadDiagnosticsPanel").Visible, Is.False);
            Assert.That(details.Text, Is.Empty);
        });
    }

    [Test]
    public void TypographyStoriesUseRealFallbackRunsAndKeepLogicalBoundsAcrossDensityChanges()
    {
        using var interFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
        using var arabicFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansArabic_Variable.ttf");
        using var devanagariFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansDevanagari_Subset.ttf");
        using var hebrewFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansHebrew_Subset.ttf");
        using var cjkFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansCJK_Subset.ttf");
        using var emojiFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoEmoji_Subset.ttf");
        using var ui = new UIContext();
        var selectedDensity = 1f;
        UIFont CreateFont(string family, float size, System.Collections.Generic.IReadOnlyList<UIFontVariationCoordinate> variations)
        {
            variations ??= Array.Empty<UIFontVariationCoordinate>();
            if (family == "Noto Sans Arabic") return new DynamicUIFont(arabicFace, size, UIFontHinting.Default, variations, interFace, hebrewFace, devanagariFace, cjkFace, emojiFace);
            if (family == "Noto Sans SC") return new DynamicUIFont(cjkFace, size, UIFontHinting.Default, variations, interFace, arabicFace, hebrewFace, devanagariFace, emojiFace);
            return new DynamicUIFont(interFace, size, UIFontHinting.Default, variations, arabicFace, hebrewFace, devanagariFace, cjkFace, emojiFace);
        }
        var stories = StoryCatalog.Create(null, scale => { selectedDensity = scale; ui.DisplayScale = scale; }, CreateFont);
        var fallbackStory = stories.Single(story => story.Name == "Fallback Chain");
        var fallbackRoot = fallbackStory.Factory();
        ui.Add(fallbackRoot);
        fallbackStory.Attached(fallbackRoot);
        var diagnostics = (Label)fallbackRoot.Children[2];
        var densityStory = stories.Single(story => story.Name == "Display Density");
        var densityRoot = densityStory.Factory();
        ui.Add(densityRoot);
        densityStory.Attached(densityRoot);
        var sample = (Label)densityRoot.Children[2].Children[0];
        var logicalSizeAtOneX = sample.GetMinimumSize();
        ui.DisplayScale = 2;
        selectedDensity = 2;
        var shapingStory = stories.Single(story => story.Name == "Shaping and Features");
        var shapingRoot = shapingStory.Factory();
        ui.Add(shapingRoot);
        shapingStory.Attached(shapingRoot);
        var shapingControls = shapingRoot.Children[0];
        var shapingDiagnostics = (Label)shapingRoot.Children[3];
        var enabledFeatureDiagnostics = shapingDiagnostics.Text;
        ((CheckBox)shapingControls.Children[0]).SetPressed(false);
        ((Slider)shapingControls.Children[2]).Value = 800;
        var bidiStory = stories.Single(story => story.Name == "Bidirectional Text");
        var bidiRoot = bidiStory.Factory();
        ui.Add(bidiRoot);
        bidiStory.Attached(bidiRoot);
        var bidiDiagnostics = (Label)bidiRoot.Children[2];
        var compatibilityStory = stories.Single(story => story.Name == "SpriteFont Compatibility");
        var compatibilityRoot = compatibilityStory.Factory();
        ui.Add(compatibilityRoot);
        var compatibilityFont = CreateTestFont();
        FontApplicator.Apply(compatibilityRoot, compatibilityFont, compatibilityFont);
        compatibilityStory.Attached(compatibilityRoot);
        var compatibilityDiagnostics = (Label)compatibilityRoot.Children[3];
        var failureStory = stories.Single(story => story.Name == "Failure States");
        var failureRoot = failureStory.Factory();
        ui.Add(failureRoot);
        failureStory.Attached(failureRoot);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Text, Does.Contain("Noto Sans Arabic"));
            Assert.That(diagnostics.Text, Does.Contain("Noto Sans Devanagari"));
            Assert.That(diagnostics.Text, Does.Contain("Noto Sans SC"));
            Assert.That(selectedDensity, Is.EqualTo(2));
            Assert.That(sample.GetMinimumSize(), Is.EqualTo(logicalSizeAtOneX));
            Assert.That(shapingDiagnostics.Text, Is.Not.EqualTo(enabledFeatureDiagnostics));
            Assert.That(shapingDiagnostics.Text, Does.Contain("wght 800"));
            Assert.That(bidiDiagnostics.Text, Does.Contain("Logical"));
            Assert.That(bidiDiagnostics.Text, Does.Contain("Visual"));
            Assert.That(bidiDiagnostics.Text, Does.Contain("RightToLeft"));
            Assert.That(compatibilityDiagnostics.Text, Does.Contain("metric differences intentional"));
            Assert.That(((Label)failureRoot.Children[1]).Text, Does.Contain("Missing face"));
            Assert.That(((Label)failureRoot.Children[2]).Text, Does.Contain("Malformed asset"));
            Assert.That(((Label)failureRoot.Children[3]).Text, Does.Contain(".notdef"));
            Assert.That(((Label)failureRoot.Children[4]).Text, Does.Contain("Atlas budget"));
        });
    }

    [Test]
    public void EveryTypographyStoryIsFocusableAndStableAcrossResizeAndDisplayScale()
    {
        using var interFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
        using var arabicFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansArabic_Variable.ttf");
        using var devanagariFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansDevanagari_Subset.ttf");
        using var hebrewFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansHebrew_Subset.ttf");
        using var cjkFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansCJK_Subset.ttf");
        using var emojiFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoEmoji_Subset.ttf");
        var compatibilityFont = CreateTestFont();
        UIFont CreateFont(string family, float size, System.Collections.Generic.IReadOnlyList<UIFontVariationCoordinate> variations)
        {
            variations ??= Array.Empty<UIFontVariationCoordinate>();
            if (family == "Noto Sans Arabic") return new DynamicUIFont(arabicFace, size, UIFontHinting.Default, variations, interFace, hebrewFace, devanagariFace, cjkFace, emojiFace);
            if (family == "Noto Sans SC") return new DynamicUIFont(cjkFace, size, UIFontHinting.Default, variations, interFace, arabicFace, hebrewFace, devanagariFace, emojiFace);
            return new DynamicUIFont(interFace, size, UIFontHinting.Default, variations, arabicFace, hebrewFace, devanagariFace, cjkFace, emojiFace);
        }
        var stories = StoryCatalog.Create(null, createDynamicFont: CreateFont).Where(story => story.Category == "Typography").ToArray();

        Assert.That(stories, Has.Length.EqualTo(10));
        foreach (var story in stories)
        {
            using var context = new UIContext { ViewportSize = new Vector2(720, 450), DisplayScale = 1 };
            var root = story.Factory();
            FontApplicator.Apply(root, CreateFont("Inter", 16, null), CreateFont("Inter", 15, null), compatibilityFont);
            context.Add(root);
            story.Attached(root);
            context.Layout();
            var minimumAtOneX = root.GetMinimumSize();

            context.ViewportSize = new Vector2(420, 720);
            root.Size = context.ViewportSize;
            context.DisplayScale = 2;
            context.Layout();

            Assert.Multiple(() =>
            {
                Assert.That(Flatten(root).Any(control => control.FocusMode != FocusMode.None), Is.True, $"{story.Name} must expose keyboard-focusable interaction.");
                Assert.That(root.GetMinimumSize(), Is.EqualTo(minimumAtOneX), $"{story.Name} logical minimum size must not depend on display scale.");
                Assert.That(root.Size.X, Is.GreaterThan(0), $"{story.Name} must remain laid out in a narrow viewport.");
                Assert.That(root.Size.Y, Is.GreaterThan(0), $"{story.Name} must remain laid out in a narrow viewport.");
            });
        }
    }

    private static System.Collections.Generic.IEnumerable<Control> Flatten(Control control)
    {
        yield return control;
        foreach (var child in control.Children.SelectMany(Flatten)) yield return child;
    }

    private static SpriteFont CreateTestFont()
    {
        var characters = Enumerable.Range(32, 95).Select(value => (char)value).ToList();
        characters.Add('·');
        var bounds = characters.Select(_ => new Rectangle(0, 0, 8, 16)).ToList();
        var cropping = characters.Select(_ => new Rectangle(0, 0, 8, 16)).ToList();
        var kerning = characters.Select(_ => new Vector3(0, 8, 0)).ToList();
        return (SpriteFont)Activator.CreateInstance(
            typeof(SpriteFont),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { null, bounds, cropping, characters, 16, 0f, kerning, null },
            null);
    }

    [Test]
    public void DynamicSizesStoryExposesLiveTextFamilyAndSizeControls()
    {
        var story = StoryCatalog.Create(null).Single(story => story.Name == "Dynamic Sizes");
        var root = story.Factory();
        story.Attached(root);
        var controls = root.Children[0];

        Assert.Multiple(() =>
        {
            Assert.That(story.Category, Is.EqualTo("Typography"));
            Assert.That(controls.Children.Select(control => control.Name), Is.EqualTo(new[] { "liveText", "fontFamily", "fontSize" }));
            Assert.That(((Slider)controls.Children[2]).MinValue, Is.EqualTo(8));
            Assert.That(((Slider)controls.Children[2]).MaxValue, Is.EqualTo(96));
            Assert.That(root.Children[2].Name, Is.EqualTo("preview"));
        });
    }

    [Test]
    public void LetterSpacingStoryEditsTracksAndResetsRichText()
    {
        using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
        UIFont CreateFont(string _, float size, System.Collections.Generic.IReadOnlyList<UIFontVariationCoordinate> variations) =>
            new DynamicUIFont(face, size, UIFontHinting.Light, variations ?? Array.Empty<UIFontVariationCoordinate>());
        var story = StoryCatalog.Create(null, createDynamicFont: CreateFont).Single(item => item.Name == "Letter Spacing");
        var root = story.Factory();
        story.Attached(root);
        var input = (LineEdit)root.Children[0];
        var controls = root.Children[1];
        var spacing = (Slider)controls.Children[0];
        var reset = (Button)controls.Children[1];
        var status = (Label)root.Children[2];
        var natural = (RichTextLabel)root.Children[3];
        var tracked = (RichTextLabel)root.Children[4];

        Assert.Multiple(() =>
        {
            Assert.That(story.Category, Is.EqualTo("Typography"));
            Assert.That(spacing.Value, Is.EqualTo(.25f));
            Assert.That(tracked.LetterSpacing, Is.EqualTo(.25f));
            Assert.That(status.Text, Does.Contain("Tracking +0.25 px"));
            Assert.That(status.Text, Does.Contain("Delta +"));
        });

        input.Text = "runtime.";
        spacing.Value = -.5f;
        Assert.Multiple(() =>
        {
            Assert.That(natural.Text, Is.EqualTo("runtime."));
            Assert.That(tracked.Text, Is.EqualTo("runtime."));
            Assert.That(tracked.LetterSpacing, Is.EqualTo(-.5f));
            Assert.That(status.Text, Does.Contain("Tracking -0.50 px"));
            Assert.That(status.Text, Does.Contain("Delta -"));
        });

        reset.KeyPressed(Microsoft.Xna.Framework.Input.Keys.Enter);
        reset.KeyReleased(Microsoft.Xna.Framework.Input.Keys.Enter);
        Assert.Multiple(() =>
        {
            Assert.That(spacing.Value, Is.Zero);
            Assert.That(tracked.LetterSpacing, Is.Zero);
            Assert.That(status.Text, Does.Contain("Tracking 0.00 px"));
            Assert.That(status.Text, Does.Contain("Delta 0.00 px"));
        });
    }

    [Test]
    public void WrappingSelectionStoryProjectsEditorSelectionThroughRetainedLayout()
    {
        using var interFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
        using var arabicFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansArabic_Variable.ttf");
        using var devanagariFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansDevanagari_Subset.ttf");
        using var hebrewFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansHebrew_Subset.ttf");
        using var cjkFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansCJK_Subset.ttf");
        using var emojiFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoEmoji_Subset.ttf");
        using var ui = new UIContext();
        UIFont CreateFont(string _, float size, System.Collections.Generic.IReadOnlyList<UIFontVariationCoordinate> variations) =>
            new DynamicUIFont(interFace, size, UIFontHinting.Default, variations ?? Array.Empty<UIFontVariationCoordinate>(), arabicFace, hebrewFace, devanagariFace, cjkFace, emojiFace);
        var story = StoryCatalog.Create(null, createDynamicFont: CreateFont).Single(item => item.Name == "Wrapping and Selection");
        var root = story.Factory();
        ui.Add(root);
        story.Attached(root);
        var editor = (TextEdit)root.Children[0];
        editor.Select(0, 5);
        ((Slider)root.Children[1].Children[0]).Value = 440;
        var preview = (Label)root.Children[3];
        var diagnostics = (Label)root.Children[4];

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Text, Does.Contain("selection 0..5"));
            Assert.That(diagnostics.Text, Does.Contain("range"));
            Assert.That(preview.Children.OfType<ColorRect>().Count(overlay => overlay.Name.StartsWith("selectionOverlay") && overlay.Visible), Is.GreaterThan(0));
            Assert.That(preview.Children.Single(overlay => overlay.Name == "caretOverlay").Visible, Is.True);
        });
    }

    [Test]
    public void CatalogShellModeToggleSelectsDynamicAndCompatibilityServices()
    {
        using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
        var selected = true;
        var font = new DynamicUIFont(face, 16);
        var codeFont = new DynamicUIFont(face, 15);
        var compatibilityFont = CreateTestFont();
        var compatibilityCodeFont = CreateTestFont();
        var shell = new CatalogShell(
            new[] { new ComponentStory("Test", "Mode", "Mode", () =>
            {
                var root = new VBoxContainer();
                root.AddChild(new TextEdit { Text = "Text" });
                root.AddChild(new CodeEdit { Text = "Code" });
                return root;
            }) },
            font,
            codeFont,
            compatibilityFont,
            compatibilityCodeFont,
            enabled => selected = enabled);
        var scope = NameScope.GetNameScope(shell);
        var toggle = scope.Find<CheckBox>("dynamicTextMode");
        var description = scope.Find<RichTextLabel>("Description");
        var inspector = scope.Find<VBoxContainer>("Inspector");

        Assert.Multiple(() =>
        {
            Assert.That(((TextEdit)shell.ActiveStoryControl.Children[0]).UIFont, Is.SameAs(font));
            Assert.That(((CodeEdit)shell.ActiveStoryControl.Children[1]).UIFont, Is.SameAs(codeFont));
            Assert.That(description.UIFont, Is.SameAs(font));
            Assert.That(inspector.Children.SelectMany(Flatten).OfType<Label>().All(label => ReferenceEquals(label.UIFont, font)), Is.True);
        });

        toggle.SetPressed(false);

        Assert.Multiple(() =>
        {
            Assert.That(shell.DynamicTextEnabled, Is.False);
            Assert.That(selected, Is.False);
            Assert.That(((TextEdit)shell.ActiveStoryControl.Children[0]).UIFont, Is.TypeOf<SpriteFontAdapter>());
            Assert.That(((SpriteFontAdapter)((CodeEdit)shell.ActiveStoryControl.Children[1]).UIFont).SpriteFont, Is.SameAs(compatibilityCodeFont));
            Assert.That(description.UIFont, Is.TypeOf<SpriteFontAdapter>());
            Assert.That(inspector.Children.SelectMany(Flatten).OfType<Label>().All(label => label.UIFont is SpriteFontAdapter), Is.True);
            Assert.That(inspector.Children.SelectMany(Flatten).OfType<BaseButton>().All(button => button.UIFont is SpriteFontAdapter), Is.True);
        });

        toggle.SetPressed(true);

        Assert.Multiple(() =>
        {
            Assert.That(((TextEdit)shell.ActiveStoryControl.Children[0]).UIFont, Is.SameAs(font));
            Assert.That(((CodeEdit)shell.ActiveStoryControl.Children[1]).UIFont, Is.SameAs(codeFont));
            Assert.That(description.UIFont, Is.SameAs(font));
            Assert.That(inspector.Children.SelectMany(Flatten).OfType<Label>().All(label => ReferenceEquals(label.UIFont, font)), Is.True);
        });
    }

    [Test]
    public void CatalogDescriptionUsesReadableSmallTextTracking()
    {
        var font = CreateTestFont();
        var shell = new CatalogShell(
            new[] { new ComponentStory("Test", "Description", "runtime.", () => new Label { Text = "Preview" }) },
            font,
            font);
        var description = NameScope.GetNameScope(shell).Find<RichTextLabel>("Description");

        Assert.That(description.LetterSpacing, Is.EqualTo(.25f));
    }

    [Test]
    public void CatalogShellActivatesAuthoredStyleAndStoryboard()
    {
        var font = CreateTestFont();
        var shell = new CatalogShell(
            new[] { new ComponentStory("Test", "Visuals", "Visuals", () => new Label { Text = "Visuals" }) },
            font,
            font);
        var scope = NameScope.GetNameScope(shell);
        var toggle = scope.Find<CheckBox>("dynamicTextMode");
        var accent = scope.Find<ColorRect>("HeaderAccent");
        using var context = new UIContext();
        context.Add(shell);

        context.Update(
            new GameTime(TimeSpan.FromSeconds(.4), TimeSpan.FromSeconds(.4)),
            new Microsoft.Xna.Framework.Input.MouseState(),
            new Microsoft.Xna.Framework.Input.KeyboardState());

        Assert.Multiple(() =>
        {
            Assert.That(toggle.Margins.Left, Is.EqualTo(2));
            Assert.That(accent.CustomMinimumSize.X, Is.GreaterThan(6));
            Assert.That(accent.CustomMinimumSize.X, Is.LessThan(10));
        });
    }

    [Test]
    public void VideoStreamPlayerStoryUsesOptionalMediaControl()
    {
        var story = StoryCatalog.Create(null).Single(story => story.Name == nameof(VideoStreamPlayer));

        using var player = (VideoStreamPlayer)story.Factory();

        Assert.That(story.Category, Is.EqualTo("Media"));
        Assert.That(player.Autoplay, Is.True);
        Assert.That(player.Loop, Is.True);
        Assert.That(player.Expand, Is.True);
        Assert.That(player.Volume, Is.EqualTo(.75f));
        Assert.That(player.Stream, Is.Null);
    }

    [Test]
    public void MetricsOptionsPreserveScaleAndWatchedEffectArguments()
    {
        var effectPath = Path.GetTempFileName();
        try
        {
            var options = CatalogMetricsOptions.Parse([
                "--metrics", "catalog.json",
                "--frames", "3",
                "--display-scale", "2",
                "--story", "Complete icon inventory",
                "--viewport-width", "720",
                "--viewport-height", "480",
                "--theme-icon-policy", "BitmapAtlas",
                "--layout-direction", "RTL",
                "--watch-effect", effectPath,
            ]);

            Assert.That(options.OutputPath, Is.EqualTo("catalog.json"));
            Assert.That(options.FrameCount, Is.EqualTo(3));
            Assert.That(options.DisplayScale, Is.EqualTo(2));
            Assert.That(options.StoryName, Is.EqualTo("Complete icon inventory"));
            Assert.That(options.ViewportWidth, Is.EqualTo(720));
            Assert.That(options.ViewportHeight, Is.EqualTo(480));
            Assert.That(options.ThemeIconPolicy, Is.EqualTo(ThemeIconRenderingPolicy.BitmapAtlas));
            Assert.That(options.LayoutDirection, Is.EqualTo(LayoutDirection.RightToLeft));
            Assert.That(options.WatchedEffectPath, Is.EqualTo(effectPath));
        }
        finally
        {
            File.Delete(effectPath);
        }
    }

    [Test]
    public void MetricsOptionsRejectMissingWatchedEffect()
    {
        Assert.Throws<ArgumentException>(() =>
            CatalogMetricsOptions.Parse(["--watch-effect", Path.GetRandomFileName()]));
    }
}