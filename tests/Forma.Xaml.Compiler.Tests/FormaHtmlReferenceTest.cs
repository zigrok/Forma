// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Forma.Xaml.Compiler;
using Forma.Xaml.Compiler.Html;
using NUnit.Framework;

namespace Forma.Xaml.Compiler.Tests;

public sealed class FormaHtmlReferenceTest
{
    private static IEnumerable<TestCaseData> Supported() => FormaHtmlReference.Properties.Where(p => p.Supported).Select(p => new TestCaseData(p).SetName("Supported_" + p.Name));
    private static IEnumerable<TestCaseData> Rejected() => FormaHtmlReference.Properties.Where(p => !p.Supported).Select(p => new TestCaseData(p).SetName("Rejected_" + p.Name));
    private static IEnumerable<TestCaseData> Recipes() => FormaHtmlReference.Cookbook.Select(r => new TestCaseData(r).SetName("Recipe_" + r.Name.Replace(' ', '_')));

    [TestCaseSource(nameof(Supported))]
    public void EverySupportedProperty_ConvertsItsExample(FormaHtmlPropertyInfo property)
    {
        var result = FormaHtmlConverter.Convert($"<div style=\"display: flex; flex-direction: column\"><div style=\"{property.Example}\"><span>x</span></div></div>", "t.fhtml");

        Assert.That(result.Succeeded, Is.True, property.Name + ": " + string.Join("\n", result.Diagnostics));
    }

    [TestCaseSource(nameof(Rejected))]
    public void EveryRejectedProperty_FailsWithHelp(FormaHtmlPropertyInfo property)
    {
        var result = FormaHtmlConverter.Convert($"<div style=\"display: flex; flex-direction: column\"><div style=\"{property.Example}\"><span>x</span></div></div>", "t.fhtml");

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == FormaDiagnosticSeverity.Error);
        Assert.That(error, Is.Not.Null, property.Name + " should be rejected");
        Assert.That(error!.Message, Does.Contain("Help:"));
    }

    [TestCaseSource(nameof(Recipes))]
    public void EveryRecipe_ConvertsAndCompiles(FormaHtmlRecipe recipe)
    {
        var result = FormaHtmlConverter.Convert(recipe.Html, "recipe.fhtml");

        Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
        Assert.DoesNotThrow(() => FormaXamlCompiler.CreateSre().CompileSre(result.Xaml, "recipe.fhtml.xaml"));
    }

    private static IEnumerable<TestCaseData> Replacements() => FormaHtmlReference.Replacements.Select(r => new TestCaseData(r).SetName("Replacement_" + r.FProperty.Split(' ')[0] + "_" + r.Spelling.Split(' ')[0].Replace('=', '_').Replace('"', '_').Replace(':', '_')));

    [TestCaseSource(nameof(Replacements))]
    public void EveryReplacement_ConvertsAndProducesTheFormaProperty(FormaHtmlReplacement replacement)
    {
        var result = FormaHtmlConverter.Convert(replacement.Html, "replacement.fhtml");

        Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
        Assert.That(System.Text.RegularExpressions.Regex.Replace(result.Xaml, @"\s+", " "), Does.Contain(replacement.ExpectedXaml));
    }

    [Test]
    public void FPropertyWithASpelling_GetsAnInfoHint_OnlyWhenAsked_AndNeverFailsTheBuild()
    {
        const string html = "<span f:FontColor=\"{Binding NameColor}\" f:Unrelated=\"x\"></span>";
        var project = new FormaHtmlProject(Path.GetTempPath()) { SuggestHtmlSpellings = true };

        var quiet = FormaHtmlConverter.Convert(html, "quiet.fhtml", new FormaHtmlProject(Path.GetTempPath()));
        var hinted = FormaHtmlConverter.Convert(html, "hinted.fhtml", project);

        Assert.Multiple(() =>
        {
            Assert.That(quiet.Diagnostics, Is.Empty);
            Assert.That(hinted.Succeeded, Is.True);
            var hint = hinted.Diagnostics.Single();
            Assert.That(hint.Code, Is.EqualTo(FormaHtmlDiagnosticCodes.PreferHtmlSpelling));
            Assert.That(hint.Severity, Is.EqualTo(FormaDiagnosticSeverity.Info));
            Assert.That(hint.Message, Does.Contain("bind:font-color").And.Contain("Help:"));
        });
    }

    [TestCase("<ul selectable bind:items=\"Rows\" data-activate=\"sometimes\"><template data-type=\"local:RowModel\"><span></span></template></ul>", "data-activate")]
    [TestCase("<div style=\"z-index: high\"></div>", "z-index")]
    [TestCase("<div data-expand=\"diagonal\"></div>", "data-expand")]
    [TestCase("<button data-flat=\"maybe\"></button>", "data-flat")]
    public void NewSpellings_RejectBadValues_WithHelp(string html, string name)
    {
        var result = FormaHtmlConverter.Convert(html, "bad.fhtml");

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == FormaDiagnosticSeverity.Error);
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Message, Does.Contain(name).And.Contain("Help:"));
    }

    [TestCase("pass", "Pass")]
    [TestCase("stop", "Stop")]
    [TestCase("ignore", "Ignore")]
    public void DataMouse_MapsToTheMouseFilter(string value, string filter)
    {
        var result = FormaHtmlConverter.Convert($"<div data-mouse=\"{value}\"></div>", "mouse.fhtml");

        Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
        Assert.That(result.Xaml, Does.Contain($"MouseFilter=\"{filter}\""));
    }

    [Test]
    public void DataMouse_RejectsOtherValues_WithHelp()
    {
        var error = FormaHtmlConverter.Convert("<div data-mouse=\"through\"></div>", "mouse.fhtml").Diagnostics.First(d => d.Severity == FormaDiagnosticSeverity.Error);

        Assert.That(error.Message, Does.Contain("data-mouse").And.Contain("Help:"));
    }

    [TestCase("Pass", "data-mouse")]
    [TestCase("Stop", "pointer-events")]
    public void MouseFilterHint_NamesTheSpellingForItsValue(string value, string spelling)
    {
        var project = new FormaHtmlProject(Path.GetTempPath()) { SuggestHtmlSpellings = true };

        var hint = FormaHtmlConverter.Convert($"<div f:MouseFilter=\"{value}\"></div>", "hint.fhtml", project).Diagnostics.Single();

        Assert.That(hint.Message, Does.Contain(spelling));
    }
}
