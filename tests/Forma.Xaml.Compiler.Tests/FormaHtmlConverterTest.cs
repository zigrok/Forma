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
    public void CustomProperties_AreStaticResourcesInTheDialect()
    {
        var result = Convert("""
            <style>
              :root { --gap: 12px; --accent: #FF8800; }
              button.a { color: var(--accent); }
            </style>
            <div style="display: flex; gap: var(--gap)"><button class="a">A</button></div>
            """);

        Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
        Assert.That(result.Xaml, Does.Contain("Separation=\"12\""));
        Assert.That(result.Xaml, Does.Contain("Value=\"#FFFF8800\""));
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
}
