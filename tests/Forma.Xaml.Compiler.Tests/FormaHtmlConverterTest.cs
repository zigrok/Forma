// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Forma.Xaml.Compiler;
using Forma.Xaml.Compiler.Html;
using NUnit.Framework;

namespace Forma.Xaml.Compiler.Tests;

public sealed class FormaHtmlConverterTest
{
    private const string Menu = """
        <style>
          button.primary { min-width: 120px; min-height: 40px; }
          button.primary:hover { font-weight: bold; }
        </style>
        <div id="Root">
          <div id="Panel" class="panel" style="border-width: 1px; padding: 16px 18px; border-radius: 3px; min-width: 380px">
            <div style="display: flex; flex-direction: column; gap: 10px">
              <button id="Play" class="primary" tabindex="-1" aria-label="Start" onclick="OnPlay">Play</button>
              <span class="hint" style="flex-grow: 1"></span>
            </div>
          </div>
        </div>
        """;

    private static FormaHtmlResult Convert(string html) => FormaHtmlConverter.Convert(html, "view.fhtml");

    private static string Squash(string xaml) => System.Text.RegularExpressions.Regex.Replace(xaml, @"\s+", " ").Replace("\" />", "\"/>").Replace("\">", "\" >").Trim();

    [Test]
    public void Spike_ConvertsContainersButtonsLabelsStylesAndFlexToCanonicalXaml()
    {
        var result = Convert(Menu);

        Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
        var xaml = Squash(result.Xaml);
        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("<Container xmlns=\"https://forma.dev/xaml\""));
            Assert.That(xaml, Does.Contain("<Border x:Name=\"Panel\" Classes=\"panel\" BorderThickness=\"1\" Padding=\"18,16\" CornerRadius=\"3\" CustomMinimumSize=\"380,0\" >"));
            Assert.That(xaml, Does.Contain("<VBoxContainer Separation=\"10\" >"));
            Assert.That(xaml, Does.Contain("<Button x:Name=\"Play\" Classes=\"primary\" FocusMode=\"None\" AccessibilityLabel=\"Start\" Pressed=\"OnPlay\" Text=\"Play\"/>"));
            Assert.That(xaml, Does.Contain("<Label Classes=\"hint\" VerticalSizeFlags=\"Expand\"/>"));
            Assert.That(xaml, Does.Contain("Selector=\"Button.primary\""));
            Assert.That(xaml, Does.Contain("Selector=\"Button.primary:hover\""));
            Assert.That(xaml, Does.Contain("<Setter Property=\"CustomMinimumSize\" Value=\"120,40\"/>"));
            Assert.That(xaml, Does.Contain("<Setter Property=\"FontWeight\" Value=\"Bold\"/>"));
        });
    }

    [Test]
    public void ConvertedXaml_IsAValidView_AndStylesApply()
    {
        var result = Convert(Menu.Replace(" onclick=\"OnPlay\"", string.Empty));

        var root = (Control)FormaXamlCompiler.CreateSre().CompileSre(result.Xaml, "view.fhtml.xaml").Build(null);
        var play = (Button)NameScope.GetNameScope(root)!.Find("Play")!;

        Assert.That(play.Text, Is.EqualTo("Play"));
        Assert.That(play.FocusMode, Is.EqualTo(FocusMode.None));
        Assert.That(play.CustomMinimumSize, Is.EqualTo(new Microsoft.Xna.Framework.Vector2(120, 40)));
    }

    [Test]
    public void CssColors_AreConvertedFromRrggbbaaToForma()
    {
        var result = Convert("""<div style="background-color: #33CDFF80; padding: 4px"><span>x</span></div>""");

        Assert.That(result.Xaml, Does.Contain("Background=\"#8033CDFF\""));
    }

    [Test]
    public void Bindings_NeedAPath_AndSupportAMode()
    {
        var ok = Convert("""<div><span bind:text="Name; mode=TwoWay"></span></div>""");
        var bad = Convert("""<div><span bind:text="1 + 2"></span></div>""");

        Assert.That(ok.Xaml, Does.Contain("Text=\"{Binding Name, Mode=TwoWay}\""));
        Assert.That(bad.Diagnostics.Single().Code, Is.EqualTo(FormaHtmlDiagnosticCodes.InvalidValue));
    }

    [Test]
    public void Media_LowersToAnAdaptiveCondition_AndTransitionsToStyleTransitions()
    {
        var result = Convert("""
            <style>
              @media (input-modality: pointer) { button.a:hover { opacity: 0.5; } }
              button.b { transition: opacity 150ms; }
            </style>
            <div><button class="a">A</button><button class="b">B</button></div>
            """);

        Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
        Assert.That(result.Xaml, Does.Contain("<AdaptiveCondition"));
        Assert.That(result.Xaml, Does.Contain("InputModality=\"Pointer\""));
        Assert.That(result.Xaml, Does.Contain("<FloatTransition"));
        Assert.That(result.Xaml, Does.Contain("Duration=\"00:00:00.1500000\""));
    }

    [Test]
    public void CustomProperties_LowerToLiveResourcesWhereThePropertyCanObserveThem()
    {
        var result = Convert("""
            <style>
              :root { --gap: 12px; --accent: #FF8800; }
              button.a { color: var(--accent); }
            </style>
            <div style="display: flex; gap: var(--gap)"><button class="a">A</button></div>
            """);

        Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
        Assert.That(result.Xaml, Does.Contain("Separation=\"{DynamicResource Fcss.gap}\""));
        Assert.That(result.Xaml, Does.Contain("Value=\"{DynamicResource Fcss.accent.color}\""));
        Assert.That(result.Xaml, Does.Contain("<x:Single x:Key=\"Fcss.gap\">12</x:Single>"));
    }

    [TestCase("""<div><script>alert(1)</script></div>""", FormaHtmlDiagnosticCodes.UnknownElement, 1, 6)]
    [TestCase("""<div style="position: absolute"></div>""", FormaHtmlDiagnosticCodes.RejectedProperty, 1, 13)]
    [TestCase("""<div style="width: calc(1px + 2px)"></div>""", FormaHtmlDiagnosticCodes.UnknownProperty, 1, 13)]
    [TestCase("""<div style="min-width: 2em"></div>""", FormaHtmlDiagnosticCodes.UnsupportedUnit, 1, 13)]
    [TestCase("""<div style="box-shadow: 0 0 4px #000"></div>""", FormaHtmlDiagnosticCodes.RejectedProperty, 1, 13)]
    [TestCase("""<div style="background: linear-gradient(red, blue)"></div>""", FormaHtmlDiagnosticCodes.RejectedProperty, 1, 13)]
    [TestCase("""<div foo="bar"></div>""", FormaHtmlDiagnosticCodes.UnknownAttribute, 1, 6)]
    [TestCase("""<div><button onclick="doIt()"></button></div>""", FormaHtmlDiagnosticCodes.InvalidValue, 1, 14)]
    [TestCase("""<div><blink></blink></div>""", FormaHtmlDiagnosticCodes.UnknownElement, 1, 6)]
    [TestCase("<div>\n<style>\n a b + c { color: #000000 }\n</style></div>", FormaHtmlDiagnosticCodes.UnsupportedSelector, 3, 2)]
    [TestCase("<div style=\"float: left\"></div>", FormaHtmlDiagnosticCodes.RejectedProperty, 1, 13)]
    public void RejectedConstructs_FailWithAStableCodeAndAnHtmlLocation(string html, string code, int line, int column)
    {
        var result = Convert(html);

        Assert.That(result.Succeeded, Is.False);
        var error = result.Diagnostics.First(d => d.Code == code);
        Assert.Multiple(() =>
        {
            Assert.That(error.Location.FilePath, Is.EqualTo("view.fhtml"));
            Assert.That(error.Location.Line, Is.EqualTo(line));
            Assert.That(error.Location.Column, Is.EqualTo(column));
            Assert.That(error.Message, Is.Not.Empty);
        });
    }

    [Test]
    public void AnErrorInALaterStage_IsMappedBackToTheHtmlLine()
    {
        var html = "<div>\n  <button id=\"A\">One</button>\n  <button\n          id=\"A\">Two</button>\n</div>";
        var result = Convert(html);
        Assert.That(result.Succeeded, Is.True);

        var parsed = new FormaXamlParser().Parse(result.Xaml, "view.fhtml.xaml", new FormaXamlParseOptions());
        var duplicate = parsed.Diagnostics.First(d => d.Code == FormaDiagnosticCodes.DuplicateName);
        var mapped = result.Map.Map("view.fhtml", duplicate.Location.Line);

        Assert.That(mapped, Is.Not.Null);
        Assert.That(mapped!.Line, Is.EqualTo(3)); // the second <button> starts on line 3
    }

    [Test]
    public void SourceMapSidecar_RoundTripsAndRemapsDiagnostics()
    {
        var result = Convert("<div>\n<button f:Bad=\"1\">Go</button></div>");
        var dir = Path.Combine(Path.GetTempPath(), "forma-html-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var xamlPath = Path.Combine(dir, "view.fhtml.xaml");
            File.WriteAllText(xamlPath, result.Xaml);
            File.WriteAllText(xamlPath + ".fhtmlmap", result.Map.Serialize("src/view.fhtml"));
            var badLine = result.Xaml.Split('\n').ToList().FindIndex(l => l.Contains("Bad=")) + 1;

            var remapped = FormaHtmlSourceMap.Remap(new FormaSourceLocation(xamlPath, badLine, 1));

            Assert.That(remapped.FilePath, Is.EqualTo("src/view.fhtml"));
            Assert.That(remapped.Line, Is.EqualTo(2));
        }
        finally { Directory.Delete(dir, true); }
    }

    private static Control Build(string html)
    {
        var result = Convert(html);
        Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
        return (Control)FormaXamlCompiler.CreateSre().CompileSre(result.Xaml, "view.fhtml.xaml").Build(null);
    }

    private static T Find<T>(Control root, string name) where T : Control => (T)NameScope.GetNameScope(root)!.Find(name)!;

    [Test]
    public void Grid_LowersToAGridPanelWithTracksAndPlacement()
    {
        var root = Build("""
            <div style="display: grid; grid-template-columns: 120px 1fr 2fr" id="G">
              <span id="A">a</span><span id="B">b</span><span id="C">c</span>
              <span id="D" style="grid-column: 2 / span 2">d</span>
            </div>
            """);

        var grid = (GridPanel)root;
        Assert.That(grid.ColumnDefinitions.Count, Is.EqualTo(3));
        Assert.That(GridPanel.GetColumn(Find<Label>(root, "D")), Is.EqualTo(1));
        Assert.That(GridPanel.GetRow(Find<Label>(root, "D")), Is.EqualTo(1));
        Assert.That(GridPanel.GetColumnSpan(Find<Label>(root, "D")), Is.EqualTo(2));
    }

    [Test]
    public void Table_LowersToAGridPanelByRowAndCell()
    {
        var root = Build("""
            <table>
              <thead><tr><th>Name</th><th>Score</th></tr></thead>
              <tbody><tr><td id="N">Ana</td><td id="S">10</td></tr></tbody>
            </table>
            """);

        var table = (GridPanel)root;
        Assert.That(table.ColumnDefinitions.Count, Is.EqualTo(2));
        Assert.That(GridPanel.GetRow(Find<Label>(root, "S")), Is.EqualTo(1));
        Assert.That(GridPanel.GetColumn(Find<Label>(root, "S")), Is.EqualTo(1));
        Assert.That(Find<Label>(root, "N").Text, Is.EqualTo("Ana"));
    }

    [Test]
    public void Forms_MapToTheMatchingControls()
    {
        var root = Build("""
            <div style="display: flex; flex-direction: column">
              <input id="T" type="text" value="hi" placeholder="Name">
              <input id="C" type="checkbox" checked>
              <input id="R" type="range" min="0" max="10" step="1" value="3">
            </div>
            """);

        Assert.That(Find<LineEdit>(root, "T").Text, Is.EqualTo("hi"));
        Assert.That(Find<LineEdit>(root, "T").PlaceholderText, Is.EqualTo("Name"));
        Assert.That(Find<CheckBox>(root, "C").Checked, Is.True);
        Assert.That(Find<HSlider>(root, "R").Value, Is.EqualTo(3f));
        Assert.That(Find<HSlider>(root, "R").MaxValue, Is.EqualTo(10f));
    }

    [Test]
    public void Lists_LowerToAColumnOfItems()
    {
        var root = Build("""<ul id="L" style="gap: 4px"><li><span id="A">one</span></li><li><span id="B">two</span></li></ul>""");

        var list = (BoxContainer)root;
        Assert.That(list.Children.Count, Is.EqualTo(2));
    }

    [Test]
    public void Scroll_UsesDataAttributesForScrollModes()
    {
        var root = Build("""<f-scroll id="S" data-vertical="Always" data-horizontal="Disabled" style="min-width: 200px; min-height: 100px"><span>x</span></f-scroll>""");

        Assert.That(Find<ScrollContainer>(root, "S").CustomMinimumSize, Is.EqualTo(new Microsoft.Xna.Framework.Vector2(200, 100)));
    }

    [Test]
    public void Formatter_IsDeterministic_Idempotent_AndKeepsCommentsAndTheConvertedMeaning()
    {
        const string messy = "<!-- keep me -->\n<style>\nbutton.a{min-width:10px;min-height:20px}\n</style>\n<div   id='Root'   style=\"padding:4px 8px;border-width:1px\">\n<button class=\"a\"   onclick=\"OnGo\">Go</button>\n</div>";

        var once = FormaHtmlFormatter.Format(messy, "v.fhtml", out var diagnostics);
        var twice = FormaHtmlFormatter.Format(once, "v.fhtml", out _);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(twice, Is.EqualTo(once));
        Assert.That(once, Does.Contain("<!-- keep me -->"));
        Assert.That(once, Does.Contain("style=\"padding: 4px 8px; border-width: 1px\""));
        Assert.That(once, Does.Contain("button.a {\n    min-width: 10px;\n    min-height: 20px;\n  }"));
        Assert.That(Convert(once).Xaml, Is.EqualTo(Convert(messy.Replace("'Root'", "\"Root\"")).Xaml));
    }

    [Test]
    public void Formatter_LeavesInvalidInputAlone()
    {
        const string broken = "<div><span></div>";

        var result = FormaHtmlFormatter.Format(broken, "v.fhtml", out var diagnostics);

        Assert.That(result, Is.EqualTo(broken));
        Assert.That(diagnostics, Is.Not.Empty);
    }

    [Test]
    public void EveryCatalogEntry_ConvertsToItsFormaTypeAndCompilesAsAView()
    {
        Assert.That(FormaHtmlDialect.Catalog, Is.Not.Empty);
        foreach (var entry in FormaHtmlDialect.Catalog)
        {
            var result = Convert(entry.Html);
            Assert.That(result.Succeeded, Is.True, $"{entry.Name}: {string.Join("; ", result.Diagnostics)}");
            Assert.That(System.Xml.Linq.XDocument.Parse(result.Xaml).Root!.Name.LocalName, Is.EqualTo(entry.FormaType), entry.Name);
            Assert.DoesNotThrow(() => FormaXamlCompiler.CreateSre().CompileSre(result.Xaml, "catalog.fhtml.xaml").Build(null), entry.Name);
        }
    }

    [Test]
    public void Formatter_KeepsRawXamlResourcesVerbatim()
    {
        const string source = "<div>\n<f-resources>\n      <Style x:Key=\"A\" Selector=\"Button.a &gt;&gt; Border.b\">\n        <Setter Property=\"Opacity\" Value=\"1\" />\n      </Style>\n</f-resources>\n</div>";

        var once = FormaHtmlFormatter.Format(source, "v.fhtml", out _);
        var twice = FormaHtmlFormatter.Format(once, "v.fhtml", out _);

        Assert.That(twice, Is.EqualTo(once));
        Assert.That(once, Does.Contain("Selector=\"Button.a &gt;&gt; Border.b\""));
        Assert.That(once, Does.Contain("<Setter Property=\"Opacity\" Value=\"1\" />"));
    }

    [Test]
    public void PartSelector_LowersToTheTemplateChildCombinator_AndPartMarksTheElement()
    {
        var result = Convert("<style>button.x:hover::part(chrome) { opacity: 0.5; }</style><div><span part=\"chrome\" class=\"a\">t</span></div>");

        Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
        Assert.That(result.Xaml, Does.Contain("Selector=\"Button.x:hover &gt;&gt; Control.part-chrome\""));
        Assert.That(result.Xaml, Does.Contain("Classes=\"part-chrome a\""));
        Assert.That(result.Xaml, Does.Contain("x:Name=\"PART_Chrome\""));
    }

    [Test]
    public void HostAndAttributeSelectors_LowerToTypedSelectors()
    {
        var result = Convert("<style>:host(.k) { opacity: 1; } input[type=checkbox].c { opacity: 0.5; } table.t { opacity: 1; } [role=tablist] { opacity: 1; } select { opacity: 1; }</style><div></div>");

        Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
        foreach (var selector in new[] { "Selector=\".k\"", "Selector=\"CheckBox.c\"", "Selector=\"DataGrid.t\"", "Selector=\"TabContainer\"", "Selector=\"OptionButton\"" })
            Assert.That(result.Xaml, Does.Contain(selector));
    }

    [TestCase("<style>button.x >> f-border.y { opacity: 1; }</style><div></div>", FormaHtmlDiagnosticCodes.UnsupportedSelector, "::part(")]
    [TestCase("<style>button::before { opacity: 1; }</style><div></div>", FormaHtmlDiagnosticCodes.UnsupportedSelector, "Help:")]
    [TestCase("<style>[data-x=y] { opacity: 1; }</style><div></div>", FormaHtmlDiagnosticCodes.UnsupportedSelector, "Help:")]
    [TestCase("<style>button::part() { opacity: 1; }</style><div></div>", FormaHtmlDiagnosticCodes.UnsupportedSelector, "Help:")]
    public void PartAndSelectorRejections_CarryACodeAndAHelpLine(string html, string code, string fragment)
    {
        var result = Convert(html);

        var error = result.Diagnostics.First(d => d.Code == code);
        Assert.That(error.Message, Does.Contain(fragment));
        Assert.That(error.Location.Line, Is.EqualTo(1));
    }

    [Test]
    public void PartSelector_AppliesToTheNamedPartOfATemplate()
    {
        const string xaml = """
            <Control xmlns="https://forma.dev/xaml" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Control.Resources><ResourceDictionary>
                <ControlTemplate x:Key="T" TargetType="Button"><Border x:Name="PART_Chrome" Classes="part-chrome" /></ControlTemplate>
              </ResourceDictionary></Control.Resources>
              <Button x:Name="B" Classes="x" Template="{StaticResource T}" />
            </Control>
            """;
        var css = Convert("<style>button.x::part(chrome) { opacity: 0.4; }</style><div></div>");
        var style = System.Xml.Linq.XDocument.Parse(css.Xaml).Descendants().First(e => e.Name.LocalName == "Style");
        var combined = xaml.Replace("</Control.Resources>", style.ToString().Replace(" xmlns=\"https://forma.dev/xaml\"", string.Empty).Replace("<Style", "<Style").Replace("Style>", "Style>") + "</Control.Resources>");
        combined = combined.Replace("</ResourceDictionary></Control.Resources>", "</ResourceDictionary></Control.Resources>");

        var root = (Control)FormaXamlCompiler.CreateSre().CompileSre(xaml.Replace("</ResourceDictionary>", style.ToString().Replace(" xmlns=\"https://forma.dev/xaml\"", string.Empty) + "</ResourceDictionary>"), "t.xaml").Build(null);
        var button = (Button)NameScope.GetNameScope(root)!.Find("B")!;
        var chrome = (Border)button.GetTemplateChild("PART_Chrome")!;

        Assert.That(chrome.Opacity, Is.EqualTo(0.4f));
    }
}
