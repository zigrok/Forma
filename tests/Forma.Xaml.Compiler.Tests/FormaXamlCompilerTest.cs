// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

#nullable enable

using Forma.Xaml.Compiler;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Mono.Cecil;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using XamlX.TypeSystem;

[assembly: Forma.Xaml.PseudoState("custom", typeof(Forma.Xaml.Compiler.Tests.PseudoStateProbe), true, nameof(Forma.Control.IsPseudoStateActive))]
[assembly: Forma.Xaml.PseudoState("broken", typeof(Forma.Xaml.Compiler.Tests.PseudoStateProbe), true, "MissingProvider")]

namespace Forma.Xaml.Compiler.Tests;

public sealed class PseudoStateProbe : Control { }

public class FormaXamlCompilerTest
{
    [Test]
    public void Parser_AcceptsTypedResourceValuesInsteadOfRequiringKeyValuePairs()
    {
        const string source = """
            <Control xmlns="https://forma.dev/xaml" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Control.Resources>
                <ResourceDictionary>
                  <Style x:Key="caption" Selector="Label">
                    <Setter Property="FontColor" Value="Red"/>
                  </Style>
                </ResourceDictionary>
              </Control.Resources>
            </Control>
            """;
        var result = new FormaXamlParser().Parse(source, "resources.xaml", new FormaXamlParseOptions
        {
            TypeResolver = (ns, name) => ns == Forma.Xaml.XamlNamespaces.Forma
                ? typeof(Control).Assembly.GetType("Forma." + name) ?? typeof(Control).Assembly.GetType("Forma.Xaml." + name) : null
        });
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Parser_ProducesNormalizedSemanticDocument()
    {
        const string source = """
            <Control xmlns="https://forma.dev/xaml"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     x:Class="Game.Hud"
                     x:DataType="Game.HudModel"
                     x:Name="Root"
                     Position="1,2" />
            """;
        var result = new FormaXamlParser().Parse(source, "Views/Hud.xaml", new FormaXamlParseOptions { RequireCompiledBindings = true });
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Document!.RootClass, Is.EqualTo("Game.Hud"));
            Assert.That(result.Document.Root.FindDirective("Name"), Is.EqualTo("Root"));
            Assert.That(result.Document.Root.FindMember("Position"), Is.EqualTo("1,2"));
        });
    }

    [Test]
    public void Parser_ReportsStableSemanticDiagnostics()
    {
        const string source = """
            <Control xmlns="https://forma.dev/xaml" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Text="{Binding Value}">
                <Control x:Name="Duplicate" />
                <Control x:Name="Duplicate" x:Uid="unsupported" />
                <Style Selector="Control + Control" />
                <Vector2Timeline TargetName="Missing" Property="Position" />
            </Control>
            """;
        var result = new FormaXamlParser().Parse(source, "Invalid.xaml", new FormaXamlParseOptions { RequireCompiledBindings = true });
        var codes = result.Diagnostics.Select(diagnostic => diagnostic.Code).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(codes, Does.Contain(FormaDiagnosticCodes.UnsupportedDirective));
            Assert.That(codes, Does.Contain(FormaDiagnosticCodes.DuplicateName));
            Assert.That(codes, Does.Contain(FormaDiagnosticCodes.CompiledBinding));
            Assert.That(codes, Does.Contain(FormaDiagnosticCodes.Selector));
            Assert.That(codes, Does.Contain(FormaDiagnosticCodes.Storyboard));
            Assert.That(result.Diagnostics, Has.All.Matches<FormaDiagnostic>(diagnostic => diagnostic.Location.Line > 0 && diagnostic.Location.Column > 0));
        });
    }

    [Test]
    public void Parser_ReportsStableDataGridDiagnostics()
    {
        const string source = """
            <DataGrid xmlns="https://forma.dev/xaml" Mode="Hierarchical">
                <DataGrid.Columns>
                    <Label />
                    <DataGridTextColumn Header="Name" />
                    <DataGridExpanderColumn CanUserSort="False" />
                    <DataGridExpanderColumn CanUserSort="False" />
                </DataGrid.Columns>
            </DataGrid>
            """;
        var diagnostics = new FormaXamlParser().Parse(source, "InvalidDataGrid.xaml").Diagnostics
            .Where(diagnostic => diagnostic.Code == FormaDiagnosticCodes.DataGrid)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Has.Length.EqualTo(5));
            Assert.That(diagnostics, Has.Some.Matches<FormaDiagnostic>(diagnostic => diagnostic.Message.Contains("DataGrid.Columns", StringComparison.Ordinal)));
            Assert.That(diagnostics, Has.Some.Matches<FormaDiagnostic>(diagnostic => diagnostic.Message.Contains("SortBinding", StringComparison.Ordinal)));
            Assert.That(diagnostics.Count(diagnostic => diagnostic.Message.Contains("Children binding", StringComparison.Ordinal)), Is.EqualTo(3));
            Assert.That(diagnostics, Has.All.Matches<FormaDiagnostic>(diagnostic => diagnostic.Location.Line > 0 && diagnostic.Location.Column > 0));
        });
    }

    [Test]
    public void Parser_ValidatesPseudoStateAvailability()
    {
        const string source = """
            <Control xmlns="https://forma.dev/xaml">
                <Style Selector="Button:unregistered" />
                <Style Selector="Control:pressed" />
                <Style Selector="Button:pressed" />
            </Control>
            """;

        var result = new FormaXamlParser().Parse(source, "PseudoStates.xaml");
        var diagnostics = result.Diagnostics.Where(diagnostic => diagnostic.Code == FormaDiagnosticCodes.Selector).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Has.Length.EqualTo(2));
            Assert.That(diagnostics, Has.Some.Matches<FormaDiagnostic>(diagnostic => diagnostic.Message.Contains(":unregistered", StringComparison.Ordinal)));
            Assert.That(diagnostics, Has.Some.Matches<FormaDiagnostic>(diagnostic => diagnostic.Message.Contains(":pressed", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void Parser_ValidatesCustomPseudoStateMetadataProviderAgreement()
    {
        const string source = """
            <local:PseudoStateProbe xmlns="https://forma.dev/xaml"
                                    xmlns:local="clr-namespace:Forma.Xaml.Compiler.Tests;assembly=Forma.Xaml.Compiler.Tests">
                <Style Selector="local:PseudoStateProbe:custom" />
                <Style Selector="local:PseudoStateProbe:broken" />
            </local:PseudoStateProbe>
            """;
        var result = new FormaXamlParser().Parse(source, "CustomPseudoStates.xaml", new FormaXamlParseOptions
        {
            TypeResolver = (xmlNamespace, typeName) => typeName == nameof(PseudoStateProbe) ? typeof(PseudoStateProbe) : null,
        });
        var diagnostics = result.Diagnostics.Where(diagnostic => diagnostic.Code == FormaDiagnosticCodes.Selector).ToArray();

        Assert.That(diagnostics, Has.One.Matches<FormaDiagnostic>(diagnostic =>
            diagnostic.Message.Contains(":broken", StringComparison.Ordinal) &&
            diagnostic.Message.Contains("MissingProvider", StringComparison.Ordinal)));
    }

    [Test]
    public void Parser_RejectsAmbiguousUnqualifiedSelectorTypes()
    {
        const string source = """
            <Control xmlns="https://forma.dev/xaml"
                     xmlns:first="clr-namespace:First"
                     xmlns:second="clr-namespace:Second">
                <Style Selector="Range" />
            </Control>
            """;
        var result = new FormaXamlParser().Parse(source, "AmbiguousSelector.xaml", new FormaXamlParseOptions
        {
            TypeResolver = (xmlNamespace, typeName) => (xmlNamespace, typeName) switch
            {
                ("clr-namespace:First", "Range") => typeof(Forma.Range),
                ("clr-namespace:Second", "Range") => typeof(global::System.Range),
                _ => null,
            },
        });

        Assert.That(result.Diagnostics, Has.Some.Matches<FormaDiagnostic>(diagnostic =>
            diagnostic.Code == FormaDiagnosticCodes.Selector && diagnostic.Message.Contains("ambiguous", StringComparison.OrdinalIgnoreCase)));
    }

    [Test]
    public void Lowerer_ResolvesCanonicalRelativeBindingSourcesAndDiagnostics()
    {
        const string source = """
            <Control xmlns="https://forma.dev/xaml" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <ControlTemplate x:Key="Probe" TargetType="TemplatedControl">
                    <Control>
                        <Label Text="{Binding Name, RelativeSource=Self}" />
                        <Label Text="{Binding Name, RelativeSource=TemplatedParent}" />
                        <Label Text="{Binding Name, RelativeSource=FindAncestor, AncestorType=Control, AncestorLevel=2}" />
                    </Control>
                </ControlTemplate>
            </Control>
            """;
        var lowered = FormaXamlCompiler.CreateSre().Lower(source, "RelativeSources.xaml");
        var bindings = lowered.Templates.Single().Scope.Operations
            .Where(operation => operation.Kind == FormaLoweredOperationKind.Binding)
            .Select(operation => (FormaBindingValue)operation.Value!)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(bindings.Select(binding => binding.Source.Kind), Is.EqualTo(new[]
            {
                FormaBindingSourceKind.Self,
                FormaBindingSourceKind.TemplatedParent,
                FormaBindingSourceKind.FindAncestor,
            }));
            Assert.That(lowered.Symbols[bindings[0].Source.TypeSymbolId.Value].Name, Is.EqualTo("Label"));
            Assert.That(lowered.Symbols[bindings[1].Source.TypeSymbolId.Value].Name, Is.EqualTo("TemplatedControl"));
            Assert.That(lowered.Symbols[bindings[2].Source.TypeSymbolId.Value].Name, Is.EqualTo("Control"));
            Assert.That(bindings[2].Source.AncestorLevel, Is.EqualTo(2));
        });

        const string invalid = """
            <Control xmlns="https://forma.dev/xaml">
                <Label Text="{Binding Name, RelativeSource=FindAncestor, AncestorLevel=0}" />
                <Label Text="{Binding Name, RelativeSource=TemplatedParent}" />
                <Label Text="{Binding Length, RelativeSource=FindAncestor, AncestorType=String}" />
            </Control>
            """;
        var diagnostics = new FormaXamlParser().Parse(invalid, "InvalidRelativeSource.xaml").Diagnostics;
        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Count(diagnostic => diagnostic.Code == FormaDiagnosticCodes.RelativeSource), Is.EqualTo(4));
            Assert.That(diagnostics.Where(diagnostic => diagnostic.Code == FormaDiagnosticCodes.RelativeSource),
                Has.All.Matches<FormaDiagnostic>(diagnostic => diagnostic.Location.Line > 1 && diagnostic.Location.Column > 0));
        });
    }

    [Test]
    public void SreEmitter_ExecutesSelfAndAncestorBindingsAcrossReparenting()
    {
        const string source = """
            <Control xmlns="https://forma.dev/xaml" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Name="First">
                <Control>
                    <Label x:Name="SelfTarget" Text="{Binding Name, RelativeSource=Self}" />
                    <Label x:Name="AncestorTarget" Text="{Binding Name, RelativeSource=FindAncestor, AncestorType=Control, AncestorLevel=2}" />
                    <Label x:Name="OneTimeTarget" Text="{Binding Name, Mode=OneTime, RelativeSource=FindAncestor, AncestorType=Control, AncestorLevel=2}" />
                    <LineEdit x:Name="AncestorEditor" Text="{Binding Name, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged, RelativeSource=FindAncestor, AncestorType=Control, AncestorLevel=2}" />
                </Control>
            </Control>
            """;
        var root = (Control)FormaXamlCompiler.CreateSre().CompileSre(source, "SreRelativeSources.xaml").Build(null);
        var scope = NameScope.GetNameScope(root)!;
        var selfTarget = scope.Find<Label>("SelfTarget")!;
        var ancestorTarget = scope.Find<Label>("AncestorTarget")!;
        var oneTimeTarget = scope.Find<Label>("OneTimeTarget")!;
        var ancestorEditor = scope.Find<LineEdit>("AncestorEditor")!;
        Assert.Multiple(() =>
        {
            Assert.That(selfTarget.Text, Is.EqualTo("SelfTarget"));
            Assert.That(ancestorTarget.Text, Is.EqualTo("First"));
            Assert.That(oneTimeTarget.Text, Is.EqualTo("First"));
            Assert.That(ancestorEditor.Text, Is.EqualTo("First"));
        });
        ancestorEditor.Text = "RenamedFirst";
        Assert.Multiple(() =>
        {
            Assert.That(root.Name, Is.EqualTo("RenamedFirst"));
            Assert.That(ancestorTarget.Text, Is.EqualTo("RenamedFirst"));
            Assert.That(oneTimeTarget.Text, Is.EqualTo("First"));
        });

        var second = new Control { Name = "Second" };
        second.AddChild(ancestorTarget);
        Assert.That(ancestorTarget.Text, Is.EqualTo("RenamedFirst"));
        var wrapper = new Control();
        second.RemoveChild(ancestorTarget);
        wrapper.AddChild(ancestorTarget);
        second.AddChild(wrapper);
        Assert.That(ancestorTarget.Text, Is.EqualTo("Second"));
    }

    [Test]
    public void SreEmitter_AssignsKeyedAndInlineControlTemplates()
    {
        var source = $$"""
            <Control xmlns="https://forma.dev/xaml"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     xmlns:local="clr-namespace:Forma.Xaml.Compiler.Tests;assembly={{typeof(CompilerBindingModel).Assembly.GetName().Name}}">
                <Control.Resources>
                    <ResourceDictionary>
                        <ControlTemplate x:Key="Shared" TargetType="TemplatedControl">
                            <Border x:Name="SharedRoot" />
                        </ControlTemplate>
                    </ResourceDictionary>
                </Control.Resources>
                <TemplatedControl x:Name="Keyed" Template="{StaticResource Shared}" />
                <TemplatedControl x:Name="Inline">
                    <TemplatedControl.Template>
                        <ControlTemplate TargetType="TemplatedControl">
                            <Border x:Name="InlineRoot" />
                        </ControlTemplate>
                    </TemplatedControl.Template>
                </TemplatedControl>
                <ContentPresenter x:Name="ContentHost">
                    <ContentPresenter.ContentTemplate>
                        <DataTemplate x:DataType="local:CompilerBindingModel"><TextBlock /></DataTemplate>
                    </ContentPresenter.ContentTemplate>
                </ContentPresenter>
                <ItemsPresenter x:Name="ItemsHost">
                    <ItemsPresenter.ItemsPanel>
                        <ItemsPanelTemplate><StackPanel x:Name="InlinePanel" /></ItemsPanelTemplate>
                    </ItemsPresenter.ItemsPanel>
                </ItemsPresenter>
            </Control>
            """;

        var root = (Control)FormaXamlCompiler.CreateSre().CompileSre(source, "SreInlineTemplates.xaml").Build(null);
        var scope = NameScope.GetNameScope(root)!;
        var keyed = scope.Find<TemplatedControl>("Keyed")!;
        var inline = scope.Find<TemplatedControl>("Inline")!;
        var contentHost = scope.Find<ContentPresenter>("ContentHost")!;
        var itemsHost = scope.Find<ItemsPresenter>("ItemsHost")!;
        using var panelInstance = itemsHost.ItemsPanel.CreateInstance();

        Assert.Multiple(() =>
        {
            Assert.That(NameScope.GetNameScope(keyed.TemplateRoot)?.Find<Border>("SharedRoot"), Is.SameAs(keyed.TemplateRoot));
            Assert.That(NameScope.GetNameScope(inline.TemplateRoot)?.Find<Border>("InlineRoot"), Is.SameAs(inline.TemplateRoot));
            Assert.That(keyed.Template, Is.SameAs(root.Resources["Shared"]));
            Assert.That(contentHost.ContentTemplate, Is.TypeOf<DataTemplate>());
            Assert.That(panelInstance.NameScope.Find<StackPanel>("InlinePanel"), Is.SameAs(panelInstance.Root));
        });
    }

    [Test]
    public void Parser_NormalizesCanonicalBindingStyleTriggerAndStoryboardShapes()
    {
        const string source = """
            <Control xmlns="https://forma.dev/xaml"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     x:Class="Game.Hud"
                     x:DataType="Game.HudModel"
                     x:Name="Root">
                <ResourceDictionary>
                    <Style x:Key="TimerStyle" Selector="Control.low-time" />
                    <Storyboard x:Key="Pulse" AutoReverse="True" RepeatBehavior="Forever">
                        <Vector2Timeline TargetName="Timer" Property="Position">
                            <KeyFrame Time="0:0:0" Value="0,0" />
                            <KeyFrame Time="0:0:1" Value="10,0" Easing="CubicInOut" />
                        </Vector2Timeline>
                    </Storyboard>
                </ResourceDictionary>
                <Control x:Name="Timer" Position="{Binding Offset, Mode=OneWay}" />
                <PropertyTrigger Binding="{Binding IsLowTime}" Value="True" />
                <EventTrigger SourceName="Root" Event="Attached" />
            </Control>
            """;
        var result = new FormaXamlParser().Parse(source, "Views/Hud.xaml", new FormaXamlParseOptions { RequireCompiledBindings = true });
        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Document!.DescendantsAndSelf().Any(node => node.TypeName == "Style"), Is.True);
            Assert.That(result.Document.DescendantsAndSelf().Any(node => node.TypeName == "Storyboard"), Is.True);
            Assert.That(result.Document.DescendantsAndSelf().Any(node => node.TypeName == "PropertyTrigger"), Is.True);
        });
    }

    [Test]
    public void Parser_ReportsDuplicateAndMissingStaticResources()
    {
        const string source = """
            <Control xmlns="https://forma.dev/xaml" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Control.Resources>
                    <ResourceDictionary>
                        <ResourceDictionary x:Key="Duplicate" />
                        <ResourceDictionary x:Key="Duplicate" />
                    </ResourceDictionary>
                </Control.Resources>
                <Control DataContext="{StaticResource Missing}" />
            </Control>
            """;
        var result = new FormaXamlParser().Parse(source, "InvalidResources.xaml");
        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics.Count(diagnostic => diagnostic.Code == FormaDiagnosticCodes.Resource), Is.EqualTo(2));
            Assert.That(result.Diagnostics, Has.Some.Matches<FormaDiagnostic>(diagnostic => diagnostic.Message.Contains("duplicated", StringComparison.Ordinal)));
            Assert.That(result.Diagnostics, Has.Some.Matches<FormaDiagnostic>(diagnostic => diagnostic.Message.Contains("was not found", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void Parser_ReportsMalformedStyleSetter()
    {
        const string source = """
            <Control xmlns="https://forma.dev/xaml">
                <Style Selector="Control"><Setter Property="TooltipText" /></Style>
            </Control>
            """;
        var result = new FormaXamlParser().Parse(source, "InvalidStyle.xaml");
        Assert.That(result.Diagnostics, Has.Some.Matches<FormaDiagnostic>(diagnostic =>
            diagnostic.Code == FormaDiagnosticCodes.Selector && diagnostic.Message.Contains("Property and Value", StringComparison.Ordinal)));
    }

    [Test]
    public void Parser_CreatesLexicalTemplateScopesAndKeepsNamesLocal()
    {
        const string source = """
            <Control xmlns="https://forma.dev/xaml"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     xmlns:sys="clr-namespace:System;assembly=System.Private.CoreLib"
                     x:Name="Shared">
                <DataTemplate x:Key="Row" x:DataType="sys:String">
                    <Control x:Name="Shared">
                        <Vector2Timeline TargetName="Shared" Property="Position"><KeyFrame Time="0:0:0" Value="0,0" /></Vector2Timeline>
                    </Control>
                </DataTemplate>
                <ControlTemplate x:Key="ButtonTemplate" TargetType="Button">
                    <Border><Control x:Name="Shared" /></Border>
                </ControlTemplate>
            </Control>
            """;

        var result = new FormaXamlParser().Parse(source, "TemplateScopes.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Document!.Templates.Select(template => template.TemplateKind),
                Is.EqualTo(new[] { FormaXamlTemplateKind.Data, FormaXamlTemplateKind.Control }));
            Assert.That(result.Document.Templates.Select(template => template.ScopeId).Distinct().ToArray(), Has.Length.EqualTo(2));
            Assert.That(result.Diagnostics, Has.None.Matches<FormaDiagnostic>(diagnostic => diagnostic.Code == FormaDiagnosticCodes.DuplicateName));
        });
    }

    [Test]
    public void Parser_RejectsInvalidTemplateRootsTargetsAndCrossScopeNames()
    {
        const string source = """
            <Control xmlns="https://forma.dev/xaml" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Name="Outer">
                <DataTemplate><TextBlock Text="{Binding Name}" /><Control /></DataTemplate>
                <ControlTemplate TargetType="Border"><Border /></ControlTemplate>
                <ItemsPanelTemplate><Control /></ItemsPanelTemplate>
                <ControlTemplate TargetType="Button">
                    <Border x:Class="Game.NestedTemplateRoot"><EventTrigger SourceName="Outer" Event="Attached" /></Border>
                </ControlTemplate>
            </Control>
            """;

        var result = new FormaXamlParser().Parse(source, "InvalidTemplates.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics.Count(diagnostic => diagnostic.Code == FormaDiagnosticCodes.Template), Is.GreaterThanOrEqualTo(4));
            Assert.That(result.Diagnostics, Has.Some.Matches<FormaDiagnostic>(diagnostic =>
                diagnostic.Code == FormaDiagnosticCodes.Trigger && diagnostic.Message.Contains("local namescope", StringComparison.Ordinal)));
            Assert.That(result.Diagnostics, Has.Some.Matches<FormaDiagnostic>(diagnostic =>
                diagnostic.Code == FormaDiagnosticCodes.InvalidDirective && diagnostic.Message.Contains("document root", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void Parser_RejectsDirectDataTemplateEventAttributesWithRowControlGuidance()
    {
        const string source = """
            <Control xmlns="https://forma.dev/xaml" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <DataTemplate x:Key="Row" x:DataType="System.String">
                    <Button Pressed="OnPressed" />
                </DataTemplate>
            </Control>
            """;

        var result = new FormaXamlParser().Parse(source, "EventfulRow.xaml");

        Assert.That(result.Diagnostics, Has.Some.Matches<FormaDiagnostic>(diagnostic =>
            diagnostic.Code == FormaDiagnosticCodes.Template &&
            diagnostic.Message.Contains("separate x:Class row control", StringComparison.Ordinal) &&
            diagnostic.Location.Line == 3 && diagnostic.Location.Column > 0));
    }

    [Test]
    public void Parser_RequiresExplicitItemsControlItemTemplate()
    {
        const string source = """
            <ItemsControl xmlns="https://forma.dev/xaml" ItemsSource="{Binding Rows}" />
            """;

        var result = new FormaXamlParser().Parse(source, "MissingItemTemplate.xaml");

        Assert.That(result.Diagnostics, Has.Some.Matches<FormaDiagnostic>(diagnostic =>
            diagnostic.Code == FormaDiagnosticCodes.Template &&
            diagnostic.Message.Contains("explicit ItemTemplate", StringComparison.Ordinal)));
    }

    [Test]
    public void Parser_ValidatesPropertyElementsPrimitiveContentAndAttachedLayoutProperties()
    {
        const string source = """
            <GridPanel xmlns="https://forma.dev/xaml">
                <GridPanel.ColumnDefinitions><ColumnDefinition /><RowDefinition /></GridPanel.ColumnDefinitions>
                <PathShape GridPanel.Column="1" GridPanel.Unknown="2"><Control /></PathShape>
                <Border><Control /><Control /></Border>
                <Button><Control /><Control /></Button>
            </GridPanel>
            """;

        var result = new FormaXamlParser().Parse(source, "InvalidContent.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics, Has.Some.Matches<FormaDiagnostic>(diagnostic => diagnostic.Code == FormaDiagnosticCodes.AttachedProperty));
            Assert.That(result.Diagnostics.Count(diagnostic => diagnostic.Code == FormaDiagnosticCodes.ContentModel), Is.EqualTo(4));
        });
    }

    [Test]
    public void Parser_AcceptsTypedPanelBrushGeometryAndPresenterPropertyModels()
    {
        const string source = """
            <GridPanel xmlns="https://forma.dev/xaml">
                <GridPanel.ColumnDefinitions><ColumnDefinition /><ColumnDefinition /></GridPanel.ColumnDefinitions>
                <Border GridPanel.Column="1">
                    <Border.Background><LinearGradientBrush /></Border.Background>
                    <PathShape><PathShape.Data><PathGeometry Data="M0 0 H10 V10 Z" /></PathShape.Data></PathShape>
                </Border>
                <ContentPresenter><ContentPresenter.Content><TextBlock Text="projected" /></ContentPresenter.Content></ContentPresenter>
            </GridPanel>
            """;

        var result = new FormaXamlParser().Parse(source, "TypedContent.xaml");

        Assert.That(result.Diagnostics, Has.None.Matches<FormaDiagnostic>(diagnostic =>
            diagnostic.Code is FormaDiagnosticCodes.ContentModel or FormaDiagnosticCodes.AttachedProperty));
    }

    [Test]
    public void Lowerer_AssignsSymbolsAndSeparatesTemplateProgramsFromOwnerConstruction()
    {
        const string source = """
            <Control xmlns="https://forma.dev/xaml"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     xmlns:sys="clr-namespace:System;assembly=System.Private.CoreLib"
                     x:Name="Owner">
                <Border Background="{DynamicResource Surface}" />
                <DataTemplate x:Key="Row" x:DataType="sys:String">
                    <Border x:Name="TemplateRoot"><TextBlock Text="{Binding Name}" /></Border>
                </DataTemplate>
                <ItemsPanelTemplate x:Key="Panel"><StackPanel /></ItemsPanelTemplate>
            </Control>
            """;

        var lowered = FormaXamlCompiler.CreateSre().Lower(source, "Lowered.xaml");
        var templateNodeIds = lowered.Templates.SelectMany(template => template.Scope.Operations).Select(operation => operation.NodeId).ToHashSet();

        Assert.Multiple(() =>
        {
            Assert.That(lowered.OwnerScope.ScopeId, Is.Zero);
            Assert.That(lowered.Templates.Select(template => template.Kind),
                Is.EqualTo(new[] { FormaXamlTemplateKind.Data, FormaXamlTemplateKind.ItemsPanel }));
            Assert.That(lowered.OwnerScope.Operations.Select(operation => operation.NodeId), Has.None.Matches<FormaNodeId>(templateNodeIds.Contains));
            Assert.That(lowered.Templates.SelectMany(template => template.Scope.Operations),
                Has.Some.Matches<FormaLoweredOperation>(operation => operation.Kind == FormaLoweredOperationKind.Binding));
            Assert.That(lowered.OwnerScope.Operations,
                Has.Some.Matches<FormaLoweredOperation>(operation => operation.Kind == FormaLoweredOperationKind.ResourceReference));
            Assert.That(lowered.OwnerScope.Operations.Concat(lowered.Templates.SelectMany(template => template.Scope.Operations)),
                Has.All.Matches<FormaLoweredOperation>(operation => operation.TypeSymbolId.IsResolved && operation.SourceRange.EndLine > 0));
            Assert.That(lowered.Symbols.Select(symbol => symbol.Id), Is.Unique);
        });
    }

    [Test]
    public void OwnerBuild_DoesNotInstantiateTemplateContent()
    {
        TemplateProbe.ConstructionCount = 0;
        var namespaceName = typeof(TemplateProbe).Namespace;
        var assemblyName = typeof(TemplateProbe).Assembly.GetName().Name;
        var source = $"""
            <Control xmlns="https://forma.dev/xaml"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     xmlns:local="clr-namespace:{namespaceName};assembly={assemblyName}">
                <DataTemplate x:Key="Probe" x:DataType="local:TemplateProbe">
                    <local:TemplateProbe />
                </DataTemplate>
            </Control>
            """;

        var options = new FormaXamlParseOptions
        {
            TypeResolver = (xmlNamespace, typeName) =>
                xmlNamespace.Contains(namespaceName!, StringComparison.Ordinal) && typeName == nameof(TemplateProbe) ? typeof(TemplateProbe) : null,
        };
        var compiler = FormaXamlCompiler.CreateSre(assemblyName);
        var lowered = compiler.Lower(source, "DeferredTemplate.xaml", options);
        var root = compiler.CompileSre(lowered).Build(null);

        Assert.Multiple(() =>
        {
            Assert.That(root, Is.TypeOf<Control>());
            Assert.That(TemplateProbe.ConstructionCount, Is.Zero);
            Assert.That(lowered.Templates, Has.Count.EqualTo(1));
            Assert.That(lowered.Templates[0].Scope.Operations,
                Has.Some.Matches<FormaLoweredOperation>(operation => operation.Kind == FormaLoweredOperationKind.Construct));
        });
    }

    [Test]
    public void SreEmitter_AttachesTypedBindingFromLoweredIr()
    {
        var namespaceName = typeof(CompilerBindingModel).Namespace;
        var assemblyName = typeof(CompilerBindingModel).Assembly.GetName().Name;
        var source = $$"""
            <Control xmlns="https://forma.dev/xaml"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     xmlns:local="clr-namespace:{{namespaceName}};assembly={{assemblyName}}"
                     x:DataType="local:CompilerBindingModel">
                <TextBlock x:Name="Value" Text="{Binding Name}" />
            </Control>
            """;
        var compiler = FormaXamlCompiler.CreateSre(assemblyName);
        var root = (Control)compiler.CompileSre(source, "SreBinding.xaml").Build(null);
        var text = (TextBlock)NameScope.GetNameScope(root)!.Find("Value")!;
        var model = new CompilerBindingModel { Name = "first" };

        root.DataContext = model;
        model.Name = "second";

        Assert.That(text.Text, Is.EqualTo("second"));
    }

    [Test]
    public void SreEmitter_AttachesTypedStyleFromLoweredIr()
    {
        const string source = """
            <Control xmlns="https://forma.dev/xaml" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Control.Resources>
                    <ResourceDictionary>
                        <Style x:Key="Styled" Selector="Control.styled">
                            <Setter Property="TooltipText" Value="FromStyle" />
                            <Setter Property="CustomMinimumSize" Value="12,34" />
                        </Style>
                    </ResourceDictionary>
                </Control.Resources>
                <Control x:Name="Target" Classes="styled" TooltipText="Underlying" />
            </Control>
            """;
        var root = (Control)FormaXamlCompiler.CreateSre().CompileSre(source, "SreStyle.xaml").Build(null);
        var target = (Control)NameScope.GetNameScope(root)!.Find("Target")!;

        Assert.Multiple(() =>
        {
            Assert.That(target.TooltipText, Is.EqualTo("FromStyle"));
            Assert.That(target.CustomMinimumSize, Is.EqualTo(new Vector2(12, 34)));
            Assert.That(root.Resources["Styled"], Is.TypeOf<Style>());
        });
    }

    [Test]
    public void SreEmitter_AttachesComplexAdaptiveStyleFromLoweredIr()
    {
        const string source = """
            <Control xmlns="https://forma.dev/xaml" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Name="Root" Classes="scope">
                <Control.Resources>
                    <ResourceDictionary>
                        <Style x:Key="AdaptiveStyle" Selector="Control.scope > Button:not(.overflow), Button#Special">
                            <Style.Condition>
                                <AdaptiveCondition MaxViewportWidth="720" InputModality="Touch" />
                            </Style.Condition>
                            <Setter Property="TooltipText" Value="Adaptive" />
                            <Setter Property="Text" Value="Narrow" />
                        </Style>
                    </ResourceDictionary>
                </Control.Resources>
                <Button x:Name="Target" TooltipText="Underlying" />
            </Control>
            """;
        var root = (Control)FormaXamlCompiler.CreateSre().CompileSre(source, "SreAdaptiveStyle.xaml").Build(null);
        var target = (Button)NameScope.GetNameScope(root)!.Find("Target")!;
        using var context = new UIContext { ViewportSize = new Vector2(900, 600), InputModality = InputModality.Pointer };
        context.Add(root);

        Assert.That(target.TooltipText, Is.EqualTo("Underlying"));
        context.ViewportSize = new Vector2(720, 600);
        context.InputModality = InputModality.Touch;
        Assert.That(target.TooltipText, Is.EqualTo("Adaptive"));
        Assert.That(target.Text, Is.EqualTo("Narrow"));
    }

    [Test]
    public void SreEmitter_AttachesTypedStyleTransitionsFromLoweredIr()
    {
        const string source = """
            <Control xmlns="https://forma.dev/xaml" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Control.Resources>
                    <ResourceDictionary>
                        <Style x:Key="Dimmed" Selector="Control.dimmed">
                            <Setter Property="Opacity" Value="0.4" />
                            <Style.Transitions>
                                <FloatTransition Property="Opacity" Duration="00:00:01" Easing="Linear" />
                            </Style.Transitions>
                        </Style>
                    </ResourceDictionary>
                </Control.Resources>
                <Control x:Name="Target" Classes="dimmed" />
            </Control>
            """;
        var root = (Control)FormaXamlCompiler.CreateSre().CompileSre(source, "SreStyleTransition.xaml").Build(null);
        var target = (Control)NameScope.GetNameScope(root)!.Find("Target")!;
        using var context = new UIContext();
        context.Add(root);

        context.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero), default, new KeyboardState());
        context.Update(new GameTime(TimeSpan.FromSeconds(.5), TimeSpan.FromSeconds(.5)), default, new KeyboardState());
        Assert.That(target.Opacity, Is.EqualTo(.7f).Within(.001f));
        context.Update(new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(.5)), default, new KeyboardState());
        Assert.That(target.Opacity, Is.EqualTo(.4f).Within(.001f));
    }

    [Test]
    public void SreEmitter_RealizesItemsControlFromKeyedTemplatesAndAssignableSource()
    {
        var namespaceName = typeof(CompilerItemsModel).Namespace;
        var assemblyName = typeof(CompilerItemsModel).Assembly.GetName().Name;
        var source = $$"""
            <Control xmlns="https://forma.dev/xaml"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     xmlns:local="clr-namespace:{{namespaceName}};assembly={{assemblyName}}"
                     xmlns:sys="clr-namespace:System;assembly=System.Private.CoreLib"
                     x:DataType="local:CompilerItemsModel">
                <Control.Resources>
                    <ResourceDictionary>
                        <DataTemplate x:Key="Row" x:DataType="sys:String"><TextBlock /></DataTemplate>
                        <ItemsPanelTemplate x:Key="Panel"><StackPanel /></ItemsPanelTemplate>
                    </ResourceDictionary>
                </Control.Resources>
                <ItemsControl x:Name="Items"
                              ItemTemplate="{StaticResource Row}"
                              ItemsPanel="{StaticResource Panel}"
                              RecyclePoolCapacity="8"
                              ItemsSource="{Binding Rows}" />
            </Control>
            """;
        var model = new CompilerItemsModel { Rows = new[] { "first", "second" } };

        var root = (Control)FormaXamlCompiler.CreateSre().CompileSre(source, "SreItems.xaml").Build(null);
        root.DataContext = model;
        var items = NameScope.GetNameScope(root)!.Find<ItemsControl>("Items")!;

        Assert.Multiple(() =>
        {
            Assert.That(items.RealizedCount, Is.EqualTo(2));
            Assert.That(items.RecyclePoolCapacity, Is.EqualTo(8));
            Assert.That(items.GetRealizedContainer(0).DataContext, Is.EqualTo("first"));
            Assert.That(((ItemsPresenter)items.TemplateRoot).Panel, Is.TypeOf<StackPanel>());
        });
        items.Dispose();
    }

    [Test]
    public void SreEmitter_BindsListBoxSingleSelectionAndRejectsMultiProjectionTwoWayBinding()
    {
        var namespaceName = typeof(CompilerListSelectionModel).Namespace;
        var assemblyName = typeof(CompilerListSelectionModel).Assembly.GetName().Name;
        var source = $$"""
            <Control xmlns="https://forma.dev/xaml"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     xmlns:local="clr-namespace:{{namespaceName}};assembly={{assemblyName}}"
                     xmlns:sys="clr-namespace:System;assembly=System.Private.CoreLib"
                     x:DataType="local:CompilerListSelectionModel">
                <ListBox x:Name="List" ItemsSource="{Binding Rows}" SelectedIndex="{Binding SelectedIndex, Mode=TwoWay}">
                    <ListBox.ItemTemplate>
                        <DataTemplate x:DataType="sys:String"><TextBlock /></DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
            </Control>
            """;
        var root = (Control)FormaXamlCompiler.CreateSre(assemblyName).CompileSre(source, "ListBoxBinding.xaml").Build(null);
        var list = (ListBox)NameScope.GetNameScope(root)!.Find("List")!;
        var model = new CompilerListSelectionModel { Rows = new[] { "first", "second", "third" }, SelectedIndex = 1 };

        root.DataContext = model;
        Assert.That(list.SelectedIndex, Is.EqualTo(1));
        list.SelectedIndex = 2;
        Assert.That(model.SelectedIndex, Is.EqualTo(2));
        model.SelectedIndex = 0;
        Assert.That(list.SelectedIndex, Is.Zero);

        var invalid = $$"""
            <ListBox xmlns="https://forma.dev/xaml"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     xmlns:local="clr-namespace:{{namespaceName}};assembly={{assemblyName}}"
                     x:DataType="local:CompilerListSelectionModel"
                     SelectedIndices="{Binding SelectedIndices, Mode=TwoWay}" />
            """;
        var exception = Assert.Throws<InvalidOperationException>(() =>
            FormaXamlCompiler.CreateSre(assemblyName).CompileSre(invalid, "InvalidListBoxBinding.xaml"));
        Assert.That(exception!.Message, Does.Contain("multi-selection projections"));
    }

    [Test]
    public void SreAndCecilEmitters_CreateTypedDataGridColumnsTemplatesSortingAndSelection()
    {
        var namespaceName = typeof(CompilerDataGridModel).Namespace;
        var assemblyName = typeof(CompilerDataGridModel).Assembly.GetName().Name;
        var source = $$"""
            <DataGrid xmlns="https://forma.dev/xaml"
                      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                      xmlns:local="clr-namespace:{{namespaceName}};assembly={{assemblyName}}"
                      x:Name="Grid"
                      x:DataType="local:CompilerDataGridModel"
                      ItemsSource="{Binding Rows}"
                      SelectionUnit="Cell"
                      SelectionMode="Multi"
                      Width="240"
                      Height="100">
                <DataGrid.Columns>
                    <local:CompilerDataGridNameColumn Header="Name">
                        <DataGridColumn.CellTemplate>
                            <DataTemplate x:DataType="local:CompilerDataGridRow">
                                <TextBlock Text="{Binding Name}" />
                            </DataTemplate>
                        </DataGridColumn.CellTemplate>
                    </local:CompilerDataGridNameColumn>
                    <local:CompilerDataGridOrderColumn Header="Order" />
                </DataGrid.Columns>
            </DataGrid>
            """;
        var model = new CompilerDataGridModel
        {
            Rows = new[] { new CompilerDataGridRow("b", 2), new CompilerDataGridRow("a", 1) },
        };
        var grid = (DataGrid)FormaXamlCompiler.CreateSre(assemblyName).CompileSre(source, "DataGrid.xaml").Build(null);
        grid.DataContext = model;
        using var context = new UIContext { ViewportSize = new Vector2(240, 100) };
        context.Add(grid);
        context.Layout();

        Assert.Multiple(() =>
        {
            Assert.That(grid.Columns, Has.Count.EqualTo(2));
            Assert.That(grid.Columns[0], Is.TypeOf<CompilerDataGridNameColumn>());
            Assert.That(grid.Columns[0].CellTemplate, Is.TypeOf<DataTemplate>());
            Assert.That(grid.SelectionUnit, Is.EqualTo(DataGridSelectionUnit.Cell));
            Assert.That(grid.SelectionMode, Is.EqualTo(ItemListSelectionMode.Multi));
            Assert.That(((TextBlock)((ContentPresenter)grid.GetCell(0, 0).GetTemplateChild(ContentControl.ContentPresenterPartName)).PresentedControl).Text, Is.EqualTo("b"));
        });
        grid.ActivateColumnHeader(0);
        context.Layout();
        Assert.That(grid.GetRealizedContainer(0).DataContext, Is.SameAs(model.Rows[1]));
        grid.SelectCell(new CellIndex(grid.GetRowPath(0), 1));
        Assert.That(grid.SelectedCells, Has.Count.EqualTo(1));
        grid.Dispose();

        var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!
            .Split(Path.PathSeparator)
            .Append(typeof(Control).Assembly.Location)
            .Append(typeof(CompilerDataGridModel).Assembly.Location)
            .Distinct();
        using var typeSystem = new CecilTypeSystem(references, null);
        var emittedAssembly = typeSystem.CreateAndRegisterAssembly("Forma.Xaml.Compiler.DataGridOutput", new Version(1, 0), ModuleKind.Dll);
        var generated = new TypeDefinition("Generated", "DataGridView", TypeAttributes.Class | TypeAttributes.Public, emittedAssembly.MainModule.TypeSystem.Object);
        var compilerContext = new TypeDefinition("Generated", "DataGridContext", TypeAttributes.Class | TypeAttributes.NotPublic, emittedAssembly.MainModule.TypeSystem.Object);
        emittedAssembly.MainModule.Types.Add(generated);
        emittedAssembly.MainModule.Types.Add(compilerContext);

        new FormaXamlCompiler(typeSystem, typeof(Control).Assembly.GetName().Name!)
            .CompileCecil(source, "DataGrid.xaml", typeSystem, generated, compilerContext);
        var calls = emittedAssembly.MainModule.Types
            .SelectMany(AllTypes)
            .SelectMany(type => type.Methods)
            .Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions)
            .Select(instruction => instruction.Operand)
            .OfType<MethodReference>()
            .Select(method => $"{method.DeclaringType.FullName}.{method.Name}")
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(calls, Does.Contain("Forma.Xaml.Compiler.Tests.CompilerDataGridNameColumn..ctor"));
            Assert.That(calls, Does.Contain("Forma.DataGrid.get_Columns"));
            Assert.That(calls, Does.Contain("System.Collections.ObjectModel.Collection`1<Forma.DataGridColumn>.Add"));
            Assert.That(calls, Does.Contain("Forma.DataGrid.set_SelectionUnit"));
            Assert.That(calls, Does.Contain("Forma.TemplateBuildContext.BindItem"));
            Assert.That(generated.Methods.Select(method => method.Name), Has.Some.StartsWith("__TemplateFactory"));
            Assert.That(calls, Has.None.StartsWith("System.Reflection."));
            Assert.That(calls, Has.None.StartsWith("System.Activator."));
            Assert.That(calls, Has.None.EqualTo("System.Type.GetType"));
        });
    }

    [Test]
    public void SreEmitter_BindsSiblingAfterDataGridWithoutCountingInfrastructureChildren()
    {
        var namespaceName = typeof(CompilerDataGridModel).Namespace;
        var assemblyName = typeof(CompilerDataGridModel).Assembly.GetName().Name;
        var source = $$"""
            <VBoxContainer xmlns="https://forma.dev/xaml"
                           xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                           xmlns:local="clr-namespace:{{namespaceName}};assembly={{assemblyName}}"
                           x:DataType="local:CompilerDataGridModel">
                <DataGrid ItemsSource="{Binding Rows}" />
                <HBoxContainer Visible="{Binding ActionsVisible}" />
            </VBoxContainer>
            """;
        var model = new CompilerDataGridModel { ActionsVisible = false };

        var root = (VBoxContainer)FormaXamlCompiler.CreateSre(assemblyName).CompileSre(source, "DataGridSibling.xaml").Build(null);
        root.DataContext = model;

        Assert.That(root.Children[1], Is.TypeOf<HBoxContainer>());
        Assert.That(root.Children[1].Visible, Is.False);
    }

    [Test]
    public void SreEmitter_UpdatesTemplatedButtonTextAfterConstruction()
    {
        const string source = """
            <Button xmlns="https://forma.dev/xaml"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    Text="Before">
                <Button.Template>
                    <ControlTemplate TargetType="Button">
                        <TextBlock Text="{Binding Text, RelativeSource=TemplatedParent}" />
                    </ControlTemplate>
                </Button.Template>
            </Button>
            """;
        var button = (Button)FormaXamlCompiler.CreateSre().CompileSre(source, "DynamicButtonText.xaml").Build(null);
        var text = (TextBlock)button.TemplateRoot;

        button.Text = "After";

        Assert.That(text.Text, Is.EqualTo("After"));
    }

    [Test]
    public void SreEmitter_CreatesVirtualizingItemsPanelsWithCanonicalProperties()
    {
        const string source = """
            <Control xmlns="https://forma.dev/xaml"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Control.Resources>
                    <ResourceDictionary>
                        <ItemsPanelTemplate x:Key="StackPanel">
                            <VirtualizingStackPanel Orientation="Horizontal"
                                                    EstimatedItemExtent="280"
                                                    Gap="6"
                                                    OverscanBefore="1"
                                                    OverscanAfter="2" />
                        </ItemsPanelTemplate>
                        <ItemsPanelTemplate x:Key="GridPanel">
                            <VirtualizingGridPanel CellWidth="260"
                                                   EstimatedCellHeight="84"
                                                   ColumnGap="12"
                                                   RowGap="12"
                                                   OverscanRows="1" />
                        </ItemsPanelTemplate>
                    </ResourceDictionary>
                </Control.Resources>
            </Control>
            """;

        var root = (Control)FormaXamlCompiler.CreateSre().CompileSre(source, "SreVirtualizingPanels.xaml").Build(null);
        using var stackInstance = ((ItemsPanelTemplate)root.Resources["StackPanel"]).CreateInstance();
        using var gridInstance = ((ItemsPanelTemplate)root.Resources["GridPanel"]).CreateInstance();
        var stack = (VirtualizingStackPanel)stackInstance.Root;
        var grid = (VirtualizingGridPanel)gridInstance.Root;

        Assert.Multiple(() =>
        {
            Assert.That(stack.Orientation, Is.EqualTo(Orientation.Horizontal));
            Assert.That(stack.EstimatedItemExtent, Is.EqualTo(280));
            Assert.That(stack.Gap, Is.EqualTo(6));
            Assert.That(stack.OverscanBefore, Is.EqualTo(1));
            Assert.That(stack.OverscanAfter, Is.EqualTo(2));
            Assert.That(grid.CellWidth, Is.EqualTo(260));
            Assert.That(grid.EstimatedCellHeight, Is.EqualTo(84));
            Assert.That(grid.ColumnGap, Is.EqualTo(12));
            Assert.That(grid.RowGap, Is.EqualTo(12));
            Assert.That(grid.OverscanRows, Is.EqualTo(1));
        });
    }

    [Test]
    public void SreEmitter_ProjectsInlineButtonContent()
    {
        const string source = """
            <Control xmlns="https://forma.dev/xaml"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Button x:Name="Button" HorizontalContentAlignment="Center">
                    <Border x:Name="Content" />
                </Button>
            </Control>
            """;

        var root = (Control)FormaXamlCompiler.CreateSre().CompileSre(source, "SreContentControl.xaml").Build(null);
        var scope = NameScope.GetNameScope(root)!;
        var button = scope.Find<Button>("Button")!;
        var content = scope.Find<Border>("Content")!;
        var presenter = (ContentPresenter)button.GetTemplateChild(ContentControl.ContentPresenterPartName)!;

        Assert.Multiple(() =>
        {
            Assert.That(button.Content, Is.SameAs(content));
            Assert.That(content.Parent, Is.SameAs(button));
            Assert.That(presenter.PresentedControl, Is.SameAs(content));
            Assert.That(presenter.HorizontalContentAlignment, Is.EqualTo(HorizontalAlignment.Center));
        });
        button.Dispose();
    }

    [Test]
    public void SreEmitter_AttachesTypedStoryboardTriggersFromLoweredIr()
    {
        var namespaceName = typeof(CompilerBindingModel).Namespace;
        var assemblyName = typeof(CompilerBindingModel).Assembly.GetName().Name;
        var source = $$"""
            <Control xmlns="https://forma.dev/xaml"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     xmlns:local="clr-namespace:{{namespaceName}};assembly={{assemblyName}}"
                     x:DataType="local:CompilerBindingModel"
                     x:Name="Root">
                <Control.Resources>
                    <ResourceDictionary>
                        <Storyboard x:Key="EventPulse"><Vector2Timeline TargetName="EventTarget" Property="CustomMinimumSize"><KeyFrame Time="0:0:0" Value="3,4" /></Vector2Timeline></Storyboard>
                        <Storyboard x:Key="PropertyPulse"><Vector2Timeline TargetName="PropertyTarget" Property="CustomMinimumSize"><KeyFrame Time="0:0:0" Value="5,6" /></Vector2Timeline></Storyboard>
                    </ResourceDictionary>
                </Control.Resources>
                <EventTrigger SourceName="Source" Event="Attached"><BeginStoryboard Storyboard="{StaticResource EventPulse}" /></EventTrigger>
                <PropertyTrigger Binding="{Binding IsActive}" Value="True"><BeginStoryboard Storyboard="{StaticResource PropertyPulse}" /></PropertyTrigger>
                <Control x:Name="Source" />
                <Control x:Name="EventTarget" />
                <Control x:Name="PropertyTarget" />
            </Control>
            """;
        var root = (Control)FormaXamlCompiler.CreateSre(assemblyName).CompileSre(source, "SreTriggers.xaml").Build(null);
        var scope = NameScope.GetNameScope(root)!;
        var eventTarget = (Control)scope.Find("EventTarget")!;
        var propertyTarget = (Control)scope.Find("PropertyTarget")!;
        var model = new CompilerBindingModel();
        root.DataContext = model;

        using var context = new UIContext();
        context.Add(root);
        model.IsActive = true;

        Assert.Multiple(() =>
        {
            Assert.That(eventTarget.CustomMinimumSize, Is.EqualTo(new Vector2(3, 4)));
            Assert.That(propertyTarget.CustomMinimumSize, Is.EqualTo(new Vector2(5, 6)));
        });
    }

    [Test]
    public void SreEmitter_AttachesTypedTemplateResourcesWithFreshDetachedRoots()
    {
        var namespaceName = typeof(CompilerBindingModel).Namespace;
        var assemblyName = typeof(CompilerBindingModel).Assembly.GetName().Name;
        var source = $$"""
            <Control xmlns="https://forma.dev/xaml"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     xmlns:local="clr-namespace:{{namespaceName}};assembly={{assemblyName}}">
                <Control.Resources>
                    <ResourceDictionary>
                        <DataTemplate x:Key="Row" x:DataType="local:CompilerBindingModel"><LineEdit Text="{Binding Name, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" /></DataTemplate>
                        <ControlTemplate x:Key="Probe" TargetType="local:TemplateProbe"><TextBlock Text="{Binding Name, RelativeSource=TemplatedParent}" /></ControlTemplate>
                        <ItemsPanelTemplate x:Key="Panel"><StackPanel /></ItemsPanelTemplate>
                    </ResourceDictionary>
                </Control.Resources>
            </Control>
            """;
        var root = (Control)FormaXamlCompiler.CreateSre(assemblyName).CompileSre(source, "SreTemplates.xaml").Build(null);
        var dataTemplate = (DataTemplate)root.Resources["Row"];
        var controlTemplate = (ControlTemplate)root.Resources["Probe"];
        var itemsPanelTemplate = (ItemsPanelTemplate)root.Resources["Panel"];

        var firstModel = new CompilerBindingModel { Name = "first" };
        var secondModel = new CompilerBindingModel { Name = "second" };
        using var firstData = dataTemplate.CreateInstance(firstModel);
        using var secondData = dataTemplate.CreateInstance(secondModel);
        using var firstPanel = itemsPanelTemplate.CreateInstance();
        using var secondPanel = itemsPanelTemplate.CreateInstance();
        firstData.Activate();
        var probe = new TemplateProbe { Name = "ProbeOwner", Template = controlTemplate };
        probe.ApplyTemplate();

        Assert.Multiple(() =>
        {
            Assert.That(firstData.Root, Is.TypeOf<LineEdit>());
            Assert.That(firstData.Root, Is.Not.SameAs(secondData.Root));
            Assert.That(firstData.Root.Parent, Is.Null);
            Assert.That(((LineEdit)firstData.Root).Text, Is.EqualTo("first"));
            Assert.That(firstPanel.Root, Is.TypeOf<StackPanel>());
            Assert.That(firstPanel.Root, Is.Not.SameAs(secondPanel.Root));
            Assert.That(controlTemplate.TargetType, Is.EqualTo(typeof(TemplateProbe)));
            Assert.That(((TextBlock)probe.TemplateRoot!).Text, Is.EqualTo("ProbeOwner"));
        });
        ((LineEdit)firstData.Root).Text = "edited-first";
        Assert.That(firstModel.Name, Is.EqualTo("edited-first"));
        firstModel.Name = "updated";
        Assert.That(((LineEdit)firstData.Root).Text, Is.EqualTo("updated"));
        firstData.Deactivate();
        ((LineEdit)firstData.Root).Text = "pooled";
        Assert.That(firstModel.Name, Is.EqualTo("updated"));
        firstData.Rebind(secondModel);
        firstData.Activate();
        Assert.That(((LineEdit)firstData.Root).Text, Is.EqualTo("second"));
        ((LineEdit)firstData.Root).Text = "edited-second";
        Assert.Multiple(() =>
        {
            Assert.That(secondModel.Name, Is.EqualTo("edited-second"));
            Assert.That(firstModel.Name, Is.EqualTo("updated"));
        });
        probe.Dispose();
    }

    [Test]
    public void SreEmitter_CompilesTemplatesBeforeMergedResourceDictionaries()
    {
        var namespaceName = typeof(CompilerBindingModel).Namespace;
        var assemblyName = typeof(CompilerBindingModel).Assembly.GetName().Name;
        var source = $$"""
            <Control xmlns="https://forma.dev/xaml"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     xmlns:local="clr-namespace:{{namespaceName}};assembly={{assemblyName}}">
                <Control.Resources>
                    <ResourceDictionary>
                        <ResourceDictionary x:Key="Before" />
                        <DataTemplate x:Key="Row" x:DataType="local:CompilerBindingModel"><TextBlock /></DataTemplate>
                        <ResourceDictionary.MergedDictionaries>
                            <ResourceDictionary><ResourceDictionary x:Key="Merged" /></ResourceDictionary>
                        </ResourceDictionary.MergedDictionaries>
                    </ResourceDictionary>
                </Control.Resources>
            </Control>
            """;

        var root = (Control)FormaXamlCompiler.CreateSre(assemblyName).CompileSre(source, "TemplateDictionaryOrdering.xaml").Build(null);

        Assert.Multiple(() =>
        {
            Assert.That(root.Resources["Row"], Is.TypeOf<DataTemplate>());
            Assert.That(root.Resources.TryFind("Merged", out _), Is.True);
        });
    }

    [Test]
    public void SreCompiler_BuildsFormaTreeAndUsesTypedConverter()
    {
        const string source = "<Control xmlns='https://forma.dev/xaml' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' x:Class='Game.View' Position='12,34'><Control Name='Child' /></Control>";
        var callbacks = FormaXamlCompiler.CreateSre().CompileSre(source, "SreView.xaml");
        var root = (Control)callbacks.Build(null);
        Assert.Multiple(() =>
        {
            Assert.That(root.Position, Is.EqualTo(new Vector2(12, 34)));
            Assert.That(root.Children, Has.Count.EqualTo(1));
            Assert.That(root.Children[0].Name, Is.EqualTo("Child"));
        });
    }

    [Test]
    public void SreCompiler_AssignsLineEditTextVerticalAlignment()
    {
        const string source = "<LineEdit xmlns='https://forma.dev/xaml' TextVerticalAlignment='Bottom' />";
        var lineEdit = (LineEdit)FormaXamlCompiler.CreateSre().CompileSre(source, "LineEditAlignment.xaml").Build(null);

        Assert.That(lineEdit.TextVerticalAlignment, Is.EqualTo(VerticalAlignment.Bottom));
    }

    [Test]
    public void SreCompiler_BuildsFoundationalPrimitiveResourceGraph()
    {
        const string source = "<Border xmlns='https://forma.dev/xaml' Background='#80402010' CornerRadius='1,2,3,4'><PathShape Data='M0 0 H10 V10 H0 Z' Fill='#FF56C596' /></Border>";
        var callbacks = FormaXamlCompiler.CreateSre().CompileSre(source, "FoundationalPrimitive.xaml");
        var border = (Border)callbacks.Build(null);
        var shape = (PathShape)border.Children.Single();
        Assert.Multiple(() =>
        {
            Assert.That(border.Background, Is.TypeOf<SolidColorBrush>());
            Assert.That(((SolidColorBrush)border.Background).Color, Is.EqualTo(new Color(0x40, 0x20, 0x10, 0x80)));
            Assert.That(border.CornerRadius, Is.EqualTo(new CornerRadius(1, 2, 3, 4)));
            Assert.That(shape.Data, Is.TypeOf<PathGeometry>());
            Assert.That(shape.Fill, Is.TypeOf<SolidColorBrush>());
        });
    }

        [Test]
        public void SreCompiler_BuildsStructuredFoundationalResourceGraph()
        {
                const string source = """
                        <Control xmlns="https://forma.dev/xaml">
                            <TextBlock FontSize="18" FontWeight="SemiBold" FontStyle="Italic" FontStretch="Condensed">
                                <TextBlock.Inlines>
                                    <Run Text="A" />
                                    <Span>
                                        <Span.Inlines><Run Text="B" /></Span.Inlines>
                                    </Span>
                                </TextBlock.Inlines>
                            </TextBlock>
                            <PathShape Data="M0 0 H10 V10 H0 Z">
                                <PathShape.Fill><RadialGradientBrush Radius="0.5" /></PathShape.Fill>
                                <PathShape.GeometryTransform>
                                    <TransformGroup>
                                        <TransformGroup.Children><TranslateTransform X="2" /><ScaleTransform ScaleX="3" ScaleY="4" /></TransformGroup.Children>
                                    </TransformGroup>
                                </PathShape.GeometryTransform>
                            </PathShape>
                            <Image>
                                <Image.VectorSource>
                                    <DrawingImage IntrinsicSize="10,10">
                                        <DrawingImage.Drawing>
                                            <DrawingGroup>
                                                <DrawingGroup.Children>
                                                    <GeometryDrawing Geometry="M0 0 H10 V10 H0 Z" Fill="#FFFF0000">
                                                        <GeometryDrawing.Effect>
                                                            <EffectGroup>
                                                                <EffectGroup.Children><BlurEffect Radius="2" /><ColorMatrixEffect /></EffectGroup.Children>
                                                            </EffectGroup>
                                                        </GeometryDrawing.Effect>
                                                    </GeometryDrawing>
                                                </DrawingGroup.Children>
                                            </DrawingGroup>
                                        </DrawingImage.Drawing>
                                    </DrawingImage>
                                </Image.VectorSource>
                            </Image>
                        </Control>
                        """;

                var callbacks = FormaXamlCompiler.CreateSre().CompileSre(source, "StructuredFoundationalResources.xaml");
                    var root = (Control)callbacks.Build(null);
                    var text = (TextBlock)root.Children[0];
                    var shape = (PathShape)root.Children[1];
                    var image = (Image)root.Children[2];
                var transforms = (TransformGroup)shape.GeometryTransform;
                var drawing = (DrawingGroup)image.VectorSource.Drawing;
                var geometry = (GeometryDrawing)drawing.Children.Single();
                Assert.Multiple(() =>
                {
                        Assert.That(text.Inlines, Has.Count.EqualTo(2));
                        Assert.That(text.FontSize, Is.EqualTo(18));
                        Assert.That(text.FontWeight, Is.EqualTo(UIFontWeight.SemiBold));
                        Assert.That(text.FontStyle, Is.EqualTo(UIFontStyle.Italic));
                        Assert.That(text.FontStretch, Is.EqualTo(UIFontStretch.Condensed));
                        Assert.That(((Span)text.Inlines[1]).Inlines, Has.Count.EqualTo(1));
                        Assert.That(shape.Fill, Is.TypeOf<RadialGradientBrush>());
                        Assert.That(transforms.Children, Has.Count.EqualTo(2));
                        Assert.That(drawing.Children, Has.Count.EqualTo(1));
                        Assert.That(((EffectGroup)geometry.Effect).Children, Has.Count.EqualTo(2));
                });
        }

    [Test]
    public void CecilCompiler_EmitsSourceIdentityOnActualUnnamedObjects()
    {
        var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!.Split(Path.PathSeparator).Append(typeof(Control).Assembly.Location);
        using var typeSystem = new CecilTypeSystem(references, null);
        var assembly = typeSystem.CreateAndRegisterAssembly("Forma.Xaml.Compiler.SourceOutput", new Version(1, 0), ModuleKind.Dll);
        var generated = new TypeDefinition("Generated", "SourceView", TypeAttributes.Class | TypeAttributes.Public, assembly.MainModule.TypeSystem.Object);
        var context = new TypeDefinition("Generated", "SourceContext", TypeAttributes.Class | TypeAttributes.NotPublic, assembly.MainModule.TypeSystem.Object);
        assembly.MainModule.Types.Add(generated);
        assembly.MainModule.Types.Add(context);
        const string source = "<VBoxContainer xmlns='https://forma.dev/xaml'><VBoxContainer><Label Text='unnamed'/></VBoxContainer></VBoxContainer>";
        var compiler = new FormaXamlCompiler(typeSystem, typeof(Control).Assembly.GetName().Name!);
        var lowered = compiler.Lower(source, "source.xaml");
        var metadata = new XamlSourceDocument("source.xaml", source).Describe((ns, name) => typeof(Control).Assembly.GetType("Forma." + name))
            .ToDictionary(node => node.Node, node => System.Text.Json.JsonSerializer.Serialize(node));
        compiler.CompileCecil(lowered, typeSystem, generated, context, sourceMetadata: metadata);
        var calls = generated.Methods.Single(method => method.Name == "Populate").Body.Instructions
            .Select(instruction => instruction.Operand).OfType<MethodReference>().ToArray();
        Assert.That(calls.Count(method => method.Name == "SetMetadata"), Is.EqualTo(3));
        Assert.That(calls.Any(method => method.Name == "FindControlByOrdinal"), Is.False);
    }

    [Test]
    public void CecilCompiler_ConsumesClassDirectiveAndEmitsMethods()
    {
        var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!.Split(Path.PathSeparator).Append(typeof(Control).Assembly.Location);
        using var typeSystem = new CecilTypeSystem(references, null);
        var assembly = typeSystem.CreateAndRegisterAssembly("Forma.Xaml.Compiler.TestOutput", new Version(1, 0), ModuleKind.Dll);
        var generated = new TypeDefinition("Generated", "View", TypeAttributes.Class | TypeAttributes.Public, assembly.MainModule.TypeSystem.Object);
        var context = new TypeDefinition("Generated", "ViewContext", TypeAttributes.Class | TypeAttributes.NotPublic, assembly.MainModule.TypeSystem.Object);
        assembly.MainModule.Types.Add(generated);
        assembly.MainModule.Types.Add(context);
        const string source = "<Control xmlns='https://forma.dev/xaml' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' x:Class='Game.View' Position='12,34' />";
        new FormaXamlCompiler(typeSystem, typeof(Control).Assembly.GetName().Name!).CompileCecil(source, "CecilView.xaml", typeSystem, generated, context);
        Assert.That(generated.Methods.Select(method => method.Name), Does.Contain("Populate"));
    }

    [Test]
    public void CecilCompiler_EmitsOneClosedTypedFactoryPerTemplate()
    {
        var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!.Split(Path.PathSeparator).Append(typeof(Control).Assembly.Location);
        using var typeSystem = new CecilTypeSystem(references, null);
        var assembly = typeSystem.CreateAndRegisterAssembly("Forma.Xaml.Compiler.TemplateOutput", new Version(1, 0), ModuleKind.Dll);
        var generated = new TypeDefinition("Generated", "TemplateView", TypeAttributes.Class | TypeAttributes.Public, assembly.MainModule.TypeSystem.Object);
        var context = new TypeDefinition("Generated", "TemplateContext", TypeAttributes.Class | TypeAttributes.NotPublic, assembly.MainModule.TypeSystem.Object);
        assembly.MainModule.Types.Add(generated);
        assembly.MainModule.Types.Add(context);
        var namespaceName = typeof(CompilerBindingModel).Namespace;
        var source = $$"""
            <Control xmlns="https://forma.dev/xaml"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     xmlns:local="clr-namespace:{{namespaceName}};assembly={{typeof(CompilerBindingModel).Assembly.GetName().Name}}">
                <DataTemplate x:Key="Row" x:DataType="local:CompilerBindingModel"><TextBlock /></DataTemplate>
                <ControlTemplate x:Key="Probe" TargetType="local:TemplateProbe"><Border /></ControlTemplate>
                <ItemsPanelTemplate x:Key="Panel"><StackPanel /></ItemsPanelTemplate>
            </Control>
            """;

        new FormaXamlCompiler(typeSystem, typeof(Control).Assembly.GetName().Name!).CompileCecil(source, "Templates.xaml", typeSystem, generated, context);
        var factories = generated.Methods.Where(method => method.Name.StartsWith("__TemplateFactory", StringComparison.Ordinal)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(factories, Has.Length.EqualTo(3));
            Assert.That(factories[0].Parameters.Select(parameter => parameter.ParameterType.FullName),
                Is.EqualTo(new[] { typeof(TemplateBuildContext).FullName, typeof(CompilerBindingModel).FullName }));
            Assert.That(factories[0].ReturnType.FullName, Is.EqualTo(typeof(Control).FullName));
            Assert.That(factories[1].Parameters.Select(parameter => parameter.ParameterType.FullName),
                Is.EqualTo(new[] { typeof(TemplateBuildContext).FullName, typeof(TemplateProbe).FullName }));
            Assert.That(factories[1].ReturnType.FullName, Is.EqualTo(typeof(Control).FullName));
            Assert.That(factories[2].Parameters.Select(parameter => parameter.ParameterType.FullName),
                Is.EqualTo(new[] { typeof(TemplateBuildContext).FullName }));
            Assert.That(factories[2].ReturnType.FullName, Is.EqualTo(typeof(Container).FullName));
        });
    }

    [Test]
    public void CecilCompiler_EmitsTypedFoundationalGraphWithoutReflection()
    {
        var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!.ToString()!.Split(Path.PathSeparator).Append(typeof(Control).Assembly.Location);
        using var typeSystem = new CecilTypeSystem(references, null);
        var assembly = typeSystem.CreateAndRegisterAssembly("Forma.Xaml.Compiler.FoundationOutput", new Version(1, 0), ModuleKind.Dll);
        var generated = new TypeDefinition("Generated", "FoundationView", TypeAttributes.Class | TypeAttributes.Public, assembly.MainModule.TypeSystem.Object);
        var context = new TypeDefinition("Generated", "FoundationContext", TypeAttributes.Class | TypeAttributes.NotPublic, assembly.MainModule.TypeSystem.Object);
        assembly.MainModule.Types.Add(generated);
        assembly.MainModule.Types.Add(context);
        const string source = "<Border xmlns='https://forma.dev/xaml' Background='#80402010' CornerRadius='1,2,3,4'><PathShape Data='M0 0 H10 V10 H0 Z' Fill='#FF56C596' /></Border>";

        new FormaXamlCompiler(typeSystem, typeof(Control).Assembly.GetName().Name!).CompileCecil(source, "FoundationCecil.xaml", typeSystem, generated, context);
        var calls = generated.Methods
            .Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions)
            .Select(instruction => instruction.Operand)
            .OfType<MethodReference>()
            .Select(method => $"{method.DeclaringType.FullName}.{method.Name}")
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(calls, Does.Contain("Forma.Xaml.XamlValueConverter.ParseCornerRadius"));
            Assert.That(calls, Does.Contain("Forma.Xaml.XamlValueConverter.ParseBrush"));
            Assert.That(calls, Does.Contain("Forma.Xaml.XamlValueConverter.ParseGeometry"));
            Assert.That(calls, Has.None.StartsWith("System.Reflection."));
        });
    }

    [Test]
    public void BuildTask_EmitsTypedAdvancedConstructCallsWithoutReflectionFallback()
    {
        var testDirectory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        var configuration = testDirectory.Parent!.Name;
        var runtime = testDirectory.Parent.Parent!.Name;
        var repository = testDirectory;
        while (repository != null && !File.Exists(Path.Combine(repository.FullName, "Directory.Build.props")))
            repository = repository.Parent;
        Assert.That(repository, Is.Not.Null, "Could not locate the Forma repository root.");
        var fixturePath = Path.Combine(
            repository!.FullName,
            "tests",
            "Forma.Xaml.Build.Integration",
            "bin",
            runtime,
            configuration,
            "net10.0",
            "Forma.Xaml.Build.Integration.dll");
        Assert.That(File.Exists(fixturePath), Is.True, $"Injected fixture was not built at '{fixturePath}'.");

        using var fixture = AssemblyDefinition.ReadAssembly(fixturePath);
        var calls = fixture.MainModule.Types
            .SelectMany(AllTypes)
            .SelectMany(type => type.Methods)
            .Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions)
            .Select(instruction => instruction.Operand)
            .OfType<MethodReference>()
            .Select(method => $"{method.DeclaringType.FullName}.{method.Name}")
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                fixture.MainModule.Resources.Any(resource => resource.Name.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)),
                Is.False);
            Assert.That(calls, Does.Contain("Forma.Border..ctor"));
            Assert.That(calls, Does.Contain("Forma.StackPanel..ctor"));
            Assert.That(calls, Does.Contain("Forma.ContentPresenter..ctor"));
            Assert.That(calls, Does.Contain("Forma.ItemsPresenter..ctor"));
            Assert.That(calls, Does.Contain("Forma.Xaml.ResourceDictionary.Add"));
            Assert.That(calls, Does.Contain("Forma.Xaml.Style.AddSetter"));
            Assert.That(calls, Does.Contain("Forma.Xaml.CompiledBinding.AttachOneWay"));
            Assert.That(calls, Does.Contain("Forma.Xaml.CompiledBinding.AttachTwoWay"));
            Assert.That(calls, Does.Contain("Forma.Xaml.CompiledEvent.Attach"));
            Assert.That(calls, Does.Contain("Forma.Xaml.Storyboard.AddTimeline"));
            Assert.That(calls, Does.Contain("Forma.Xaml.CompiledStoryboardTrigger.AttachEvent"));
            Assert.That(calls, Does.Contain("Forma.Xaml.CompiledStoryboardTrigger.AttachProperty"));
            Assert.That(calls, Does.Contain("Forma.Xaml.CompiledStoryboardTrigger.AttachStopEvent"));
            Assert.That(calls, Does.Contain("Forma.DataGrid.get_Columns"));
            Assert.That(calls, Does.Contain("Forma.DataGridColumn.set_CellTemplate"));
            Assert.That(calls, Does.Contain("Forma.DataGrid.set_SelectionUnit"));
            Assert.That(calls, Does.Contain("Forma.ItemsControl.set_ItemTemplate"));
            Assert.That(
                calls.Where(call => call.StartsWith("System.Reflection.", StringComparison.Ordinal)),
                Is.EqualTo(new[] { "System.Reflection.Assembly.GetManifestResourceNames" }));
            Assert.That(calls, Has.None.StartsWith("System.Activator."));
            Assert.That(calls, Has.None.EqualTo("System.Type.GetType"));
        });
    }

    private static IEnumerable<TypeDefinition> AllTypes(TypeDefinition type)
    {
        yield return type;
        foreach (var nested in type.NestedTypes.SelectMany(AllTypes)) yield return nested;
    }
}

public sealed class TemplateProbe : TemplatedControl
{
    public TemplateProbe() => ConstructionCount++;

    public static int ConstructionCount { get; set; }
}

public sealed class CompilerBindingModel : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private bool _isActive;

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

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class CompilerItemsModel
{
    public string[] Rows { get; set; } = Array.Empty<string>();
}

public sealed class CompilerListSelectionModel : INotifyPropertyChanged
{
    private int _selectedIndex = -1;

    public string[] Rows { get; set; } = Array.Empty<string>();
    public IReadOnlyList<int> SelectedIndices { get; set; } = Array.Empty<int>();
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

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record CompilerDataGridRow(string Name, int Order);

public sealed class CompilerDataGridModel
{
    public CompilerDataGridRow[] Rows { get; set; } = Array.Empty<CompilerDataGridRow>();
    public bool ActionsVisible { get; set; } = true;
}

public sealed class CompilerDataGridNameColumn : DataGridTextColumn
{
    public CompilerDataGridNameColumn()
    {
        Binding = DataGridBinding<string>.Create<CompilerDataGridRow>(row => row.Name);
        SortBinding = DataGridSortBinding.Create<CompilerDataGridRow, string>(row => row.Name);
    }
}

public sealed class CompilerDataGridOrderColumn : DataGridTextColumn
{
    public CompilerDataGridOrderColumn()
    {
        Binding = DataGridBinding<string>.Create<CompilerDataGridRow>(row => row.Order.ToString());
        SortBinding = DataGridSortBinding.Create<CompilerDataGridRow, int>(row => row.Order);
    }
}