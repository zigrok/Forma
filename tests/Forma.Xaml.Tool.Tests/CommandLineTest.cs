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

    private static int Quiet(params string[] args)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
            return Program.Main(args);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Test]
    public void AgentInstall_WritesTheSkillAndTheMarkedBlock_DoctorChecksThem_UpdateRepairsThem()
    {
        File.WriteAllText(Path.Combine(_directory, "AGENTS.md"), "# Project\n\nKeep this.\n");
        File.WriteAllText(Path.Combine(_directory, "forma-preview.json"), "{ \"host\": \"host.sh\" }");
        File.WriteAllText(Path.Combine(_directory, "host.sh"), "#!/bin/sh\n");

        Assert.That(Quiet("agent", "doctor", "--dir", _directory), Is.EqualTo(1), "nothing is installed yet");
        Assert.That(Quiet("agent", "install", "--dir", _directory), Is.EqualTo(0));
        Assert.That(File.ReadAllText(Path.Combine(_directory, ".opencode", "skills", "forma-ui", "SKILL.md")), Does.StartWith("---\nname: forma-ui"));
        Assert.That(File.Exists(Path.Combine(_directory, ".claude", "skills", "forma-ui", "references", "support-matrix.md")), Is.True);
        var agents = File.ReadAllText(Path.Combine(_directory, "AGENTS.md"));
        Assert.That(agents, Does.Contain("Keep this."));
        Assert.That(agents, Does.Contain("forma-agent:begin"));
        Assert.That(Quiet("agent", "doctor", "--dir", _directory), Is.EqualTo(0));

        File.AppendAllText(Path.Combine(_directory, ".opencode", "skills", "forma-ui", "SKILL.md"), "tampered");
        Assert.That(Quiet("agent", "doctor", "--dir", _directory), Is.EqualTo(1), "an edited skill is out of date");
        Assert.That(Quiet("agent", "update", "--dir", _directory), Is.EqualTo(0));
        Assert.That(Quiet("agent", "doctor", "--dir", _directory), Is.EqualTo(0));
        Assert.That(Quiet("agent", "install", "--dir", _directory), Is.EqualTo(0));
        Assert.That(File.ReadAllText(Path.Combine(_directory, "AGENTS.md")).Split("forma-agent:begin").Length, Is.EqualTo(2), "the block is replaced, not duplicated");
    }

    private static string DocsDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "docs", "html-css-dialect.md"))) return Path.Combine(directory.FullName, "docs");
        throw new InvalidOperationException("The Forma docs directory was not found.");
    }

    [Test]
    public void TheCommittedGeneratedDocs_MatchTheGenerator_AndEveryLinkInLlmsTxtResolves()
    {
        var docs = DocsDirectory();
        foreach (var (relative, content) in AgentKit.DocFiles())
        {
            var path = Path.Combine(docs, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(File.Exists(path), Is.True, $"{relative} is missing; run: forma-xaml docs --out docs");
            Assert.That(File.ReadAllText(path), Is.EqualTo(content), $"{relative} is out of date; run: forma-xaml docs --out docs");
        }

        foreach (System.Text.RegularExpressions.Match link in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(Path.Combine(docs, "llms.txt")), @"\]\(([^)]+)\)"))
            Assert.That(File.Exists(Path.Combine(docs, link.Groups[1].Value)), Is.True, $"llms.txt links to {link.Groups[1].Value}, which does not exist.");
    }

    [Test]
    public void NewScreen_WritesAViewThatValidates_AndRefusesToOverwrite()
    {
        Assert.That(Quiet("new", "screen", "Pause", "--dir", _directory, "--namespace", "Demo"), Is.EqualTo(0));
        Assert.That(File.Exists(Path.Combine(_directory, "PauseViewModel.cs")), Is.True);
        Assert.That(Quiet("validate", Path.Combine(_directory, "PauseView.fhtml")), Is.EqualTo(0));
        Assert.That(Quiet("new", "screen", "Pause", "--dir", _directory), Is.EqualTo(1));
        Assert.That(Quiet("new", "screen", "pause", "--dir", _directory), Is.EqualTo(2));
    }

    [Test]
    public void TheMcpServer_ListsToolsAndRunsValidateAndServesTheReference()
    {
        var tools = (System.Text.Json.Nodes.JsonObject)McpServer.Handle("tools/list", null);
        Assert.That(tools["tools"]!.AsArray().Select(t => (string)t!["name"]!), Is.EquivalentTo(new[] { "validate", "format", "preview" }));

        var view = Path.Combine(_directory, "View.fhtml");
        File.WriteAllText(view, "<div style=\"position: absolute\"></div>");
        var call = new System.Text.Json.Nodes.JsonObject { ["name"] = "validate", ["arguments"] = new System.Text.Json.Nodes.JsonObject { ["paths"] = new System.Text.Json.Nodes.JsonArray(view) } };
        var text = (string)((System.Text.Json.Nodes.JsonObject)McpServer.Handle("tools/call", call))["content"]![0]!["text"]!;
        Assert.That(text, Does.Contain("FHTML2005"));
        Assert.That(text, Does.Contain("Help:"));

        var read = new System.Text.Json.Nodes.JsonObject { ["uri"] = "forma-docs://html-css-support-matrix.md" };
        Assert.That((string)((System.Text.Json.Nodes.JsonObject)McpServer.Handle("resources/read", read))["contents"]![0]!["text"]!, Does.Contain("support matrix"));
    }

    [Test]
    public void Preview_ValidatesThenRunsTheProjectHostWithTheViewOutputAndOptions()
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("The preview host is a /bin/sh script and needs Unix file modes.");
        File.WriteAllText(Path.Combine(_directory, "forma-preview.json"), "{ \"host\": \"host.sh\" }");
        var host = Path.Combine(_directory, "host.sh");
        File.WriteAllText(host, "#!/bin/sh\necho \"$@\" > \"$(dirname \"$2\")/args.txt\"\necho png > \"$2\"\n");
        File.SetUnixFileMode(host, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var view = Path.Combine(_directory, "View.fhtml");
        File.WriteAllText(view, "<div><span>x</span></div>");
        var picture = Path.Combine(_directory, "out.png");
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(TextWriter.Null);
            Assert.That(Program.Main(new[] { "preview", view, "-o", picture, "--state", "hover:#Play", "--lang", "ja" }), Is.EqualTo(0));
            Assert.That(File.Exists(picture), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(_directory, "args.txt")), Does.Contain("--state hover:#Play --lang ja"));

            File.WriteAllText(view, "<div style=\"position: absolute\"></div>");
            File.Delete(picture);
            Assert.That(Program.Main(new[] { "preview", view, "-o", picture }), Is.EqualTo(1), "an invalid view is reported, not rendered");
            Assert.That(File.Exists(picture), Is.False);
        }
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