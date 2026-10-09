// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Forma.Xaml.Compiler;
using Forma.Xaml.Compiler.Html;
using NUnit.Framework;

namespace Forma.Xaml.Compiler.Tests;

public sealed class FormaHtmlReferenceTest
{
    private static string DocsDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "docs", "html-css-dialect.md"))) return Path.Combine(directory.FullName, "docs");
        throw new InvalidOperationException("The Forma docs directory was not found.");
    }

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

    [Test]
    public void TheCommittedReferenceDocs_MatchWhatTheGeneratorWrites()
    {
        foreach (var (relative, content) in FormaHtmlReference.Files())
        {
            var path = Path.Combine(DocsDirectory(), relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(File.Exists(path), Is.True, $"{relative} is missing; run: forma-xaml docs --out docs");
            Assert.That(File.ReadAllText(path), Is.EqualTo(content), $"{relative} is out of date; run: forma-xaml docs --out docs");
        }
    }
}
