// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Forma.Xaml.Tool;

namespace Forma.Xaml.Tool.Tests;

[NonParallelizable]
public sealed class CommandLineTest
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"forma-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);

    [TestCase("json", "\"code\": \"FHTML2005\"")]
    [TestCase("sarif", "\"ruleId\": \"FHTML2005\"")]
    public void ValidateChecksHtmlDialectFilesAndReportsHelpLines(string format, string expected)
    {
        var view = Path.Combine(_directory, "View.fhtml");
        File.WriteAllText(view, "<div style=\"position: absolute\"></div>");
        var originalOut = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.That(Program.Main(new[] { "validate", "--format", format, view }), Is.EqualTo(1));
        }
        finally { Console.SetOut(originalOut); }

        Assert.That(output.ToString(), Does.Contain(expected));
        Assert.That(output.ToString(), Does.Contain("Help:"));

        var sheet = Path.Combine(_directory, "theme.fcss");
        File.WriteAllText(sheet, "span { color: #000000; }");
        Console.SetOut(TextWriter.Null);
        try { Assert.That(Program.Main(new[] { "validate", sheet }), Is.EqualTo(0)); }
        finally { Console.SetOut(originalOut); }
    }

    [TestCase("human")]
    [TestCase("json")]
    [TestCase("sarif")]
    public void ValidateWritesStableDiagnosticFormat(string format)
    {
        var path = Path.Combine(_directory, "Invalid.xaml");
        File.WriteAllText(path, "<Control xmlns='https://forma.dev/xaml' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' x:Uid='bad' />");
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            Assert.That(Program.Main(new[] { "validate", "--format", format, path }), Is.EqualTo(1));
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        var text = output.ToString();
        Assert.That(error.ToString(), Is.Empty);
        if (format == "human")
        {
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("Invalid.xaml(1,"));
                Assert.That(text, Does.Contain("FXAML1003"));
            });
            return;
        }

        using var document = JsonDocument.Parse(text);
        if (format == "json")
        {
            var diagnostic = document.RootElement[0];
            Assert.Multiple(() =>
            {
                Assert.That(diagnostic.GetProperty("code").GetString(), Is.EqualTo("FXAML1003"));
                Assert.That(diagnostic.GetProperty("location").GetProperty("line").GetInt32(), Is.EqualTo(1));
            });
            return;
        }

        var result = document.RootElement.GetProperty("runs")[0].GetProperty("results")[0];
        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.GetProperty("version").GetString(), Is.EqualTo("2.1.0"));
            Assert.That(result.GetProperty("ruleId").GetString(), Is.EqualTo("FXAML1003"));
            Assert.That(result.GetProperty("locations")[0].GetProperty("physicalLocation").GetProperty("region").GetProperty("startLine").GetInt32(), Is.EqualTo(1));
        });
    }

    [Test]
    public void SchemaDescribesTemplateFirstTypesBindingsSelectorsAndDataGrid()
    {
        var originalOut = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.That(Program.Main(new[] { "schema", "--json" }), Is.Zero);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        static string[] Values(JsonElement element) => element.EnumerateArray().Select(value => value.GetString()!).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(Values(root.GetProperty("typeClassifications").GetProperty("foundational")), Does.Contain("Border"));
            Assert.That(Values(root.GetProperty("typeClassifications").GetProperty("templated")), Does.Contain("Button"));
            Assert.That(Values(root.GetProperty("brushes")), Does.Contain("SolidColorBrush"));
            Assert.That(Values(root.GetProperty("attachedProperties")), Does.Contain("GridPanel.Row"));
            Assert.That(Values(root.GetProperty("bindingSources")), Does.Contain("TemplatedParent"));
            Assert.That(Values(root.GetProperty("pseudoStates")), Does.Contain("ascending"));
            Assert.That(Values(root.GetProperty("dataGridColumns")), Does.Contain("DataGridExpanderColumn"));
            Assert.That(root.GetProperty("templateParts").EnumerateArray().Any(entry => entry.GetProperty("type").GetString() == "DataGrid"), Is.True);
        });
    }
}