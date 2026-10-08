// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Forma.Xaml.Compiler;
using Forma.Xaml.Compiler.Html;
using Microsoft.Xna.Framework;
using NUnit.Framework;

namespace Forma.Xaml.Compiler.Tests;

public sealed class FormaFcssTest
{
    private string _directory = null!;

    [SetUp] public void SetUp() { _directory = Path.Combine(Path.GetTempPath(), "forma-fcss-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_directory); }
    [TearDown] public void TearDown() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private void Write(string name, string content)
    {
        var path = Path.Combine(_directory, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private FormaHtmlResult Convert(string name, FormaHtmlProject project) =>
        FormaHtmlConverter.Convert(File.ReadAllText(Path.Combine(_directory, name.Replace('/', Path.DirectorySeparatorChar))), name, project);

    private static Control Build(FormaHtmlResult result)
    {
        Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
        return (Control)FormaXamlCompiler.CreateSre().CompileSre(result.Xaml, "view.fhtml.xaml").Build(null);
    }

    private static T Find<T>(Control root, string name) where T : Control => (T)NameScope.GetNameScope(root)!.Find(name)!;

    private const string Theme = """
        :root { --accent: #FF8800; --dim: 0.5; --gap: 12px; }
        button.primary { min-width: 100px; min-height: 40px; }
        span.tint { color: var(--accent); opacity: var(--dim); }
        f-border.card { background-color: var(--accent); }
        """;

    private const string TintedView = "<link rel=\"stylesheet\" href=\"theme.fcss\">\n<div><span id=\"L\" class=\"tint\">x</span><f-border id=\"C\" class=\"card\"><span>y</span></f-border></div>";

    [Test]
    public void ALinkedSheet_AppliesInEveryViewThatLinksIt_AndIsConvertedOnce()
    {
        Write("theme.fcss", Theme);
        Write("a.fhtml", "<link rel=\"stylesheet\" href=\"theme.fcss\">\n<div><button id=\"B\" class=\"primary\">A</button></div>");
        Write("views/b.fhtml", "<link rel=\"stylesheet\" href=\"../theme.fcss\">\n<div><button id=\"B\" class=\"primary\">B</button></div>");
        var project = new FormaHtmlProject(_directory);

        var a = Build(Convert("a.fhtml", project));
        var b = Build(Convert("views/b.fhtml", project));

        Assert.That(Find<Button>(a, "B").CustomMinimumSize, Is.EqualTo(new Vector2(100, 40)));
        Assert.That(Find<Button>(b, "B").CustomMinimumSize, Is.EqualTo(new Vector2(100, 40)));
        Assert.That(project.Sheets.Count, Is.EqualTo(1));
        var dictionary = project.ConvertSheet("theme.fcss");
        Assert.That(dictionary.Succeeded, Is.True, string.Join("\n", dictionary.Diagnostics));
        Assert.That(dictionary.Xaml, Does.StartWith("<ResourceDictionary"));
        Assert.That(dictionary.Xaml, Does.Contain("Selector=\"Button.primary\""));
    }

    [Test]
    public void AViewsOwnStyle_ComesAfterTheLinkedSheetAndWins()
    {
        Write("theme.fcss", "button.primary { min-width: 100px; min-height: 40px; }");
        Write("a.fhtml", "<link rel=\"stylesheet\" href=\"theme.fcss\">\n<style>button.primary { min-width: 200px; min-height: 40px; }</style>\n<div><button id=\"B\" class=\"primary\">A</button></div>");

        var view = Build(Convert("a.fhtml", new FormaHtmlProject(_directory)));

        Assert.That(Find<Button>(view, "B").CustomMinimumSize, Is.EqualTo(new Vector2(200, 40)));
    }

    [Test]
    public void Dependencies_AreRecordedInTheSourceMap()
    {
        Write("theme.fcss", Theme);
        Write("a.fhtml", "<link rel=\"stylesheet\" href=\"theme.fcss\"><div></div>");

        var result = Convert("a.fhtml", new FormaHtmlProject(_directory));

        Assert.That(result.Map.Dependencies, Is.EqualTo(new[] { "theme.fcss" }));
        Assert.That(FormaHtmlSourceMap.Parse(result.Map.Serialize("a.fhtml"))!.Value.Map.Dependencies, Is.EqualTo(new[] { "theme.fcss" }));
    }

    [Test]
    public void CustomProperties_BecomeTypedResources_ColorAsBrushAndColor_NumbersAsSingles()
    {
        Write("theme.fcss", Theme);
        Write("a.fhtml", TintedView);

        var view = Build(Convert("a.fhtml", new FormaHtmlProject(_directory)));

        view.Resources.TryFind("accent", out var brush);
        view.Resources.TryFind("accent.color", out var color);
        view.Resources.TryFind("dim", out var dim);
        Assert.That(brush, Is.TypeOf<SolidColorBrush>());
        Assert.That(((SolidColorBrush)brush!).Color, Is.EqualTo(new Color(255, 136, 0)));
        Assert.That(color, Is.EqualTo(new Color(255, 136, 0)));
        Assert.That(dim, Is.TypeOf<float>());
        Assert.That(Find<Label>(view, "L").FontColor, Is.EqualTo(new Color(255, 136, 0)));
        Assert.That(Find<Label>(view, "L").Opacity, Is.EqualTo(0.5f));
        Assert.That(((SolidColorBrush)Find<Border>(view, "C").Background!).Color, Is.EqualTo(new Color(255, 136, 0)));
    }

    [Test]
    public void ChangingATokenAtRuntime_UpdatesStyledControlsWithoutAReapply()
    {
        Write("theme.fcss", Theme);
        Write("a.fhtml", TintedView);
        var view = Build(Convert("a.fhtml", new FormaHtmlProject(_directory)));
        var label = Find<Label>(view, "L");
        var border = Find<Border>(view, "C");

        view.Resources["accent.color"] = new Color(1, 2, 3);
        view.Resources["accent"] = new SolidColorBrush(new Color(1, 2, 3));
        view.Resources["dim"] = 0.25f;

        Assert.That(label.FontColor, Is.EqualTo(new Color(1, 2, 3)));
        Assert.That(label.Opacity, Is.EqualTo(0.25f));
        Assert.That(((SolidColorBrush)border.Background!).Color, Is.EqualTo(new Color(1, 2, 3)));
    }

    [Test]
    public void AViewLocalToken_ShadowsTheSharedOne()
    {
        Write("theme.fcss", Theme);
        Write("a.fhtml", "<link rel=\"stylesheet\" href=\"theme.fcss\">\n<style>:root { --accent: #00FF00; }</style>\n<div><span id=\"L\" class=\"tint\">x</span></div>");

        var view = Build(Convert("a.fhtml", new FormaHtmlProject(_directory)));

        Assert.That(Find<Label>(view, "L").FontColor, Is.EqualTo(new Color(0, 255, 0)));
    }

    [Test]
    public void ATokenOfTheWrongKind_IsRejected_AndCompoundTokensSubstituteStatically()
    {
        Write("theme.fcss", ":root { --gap: 12px; --pad: 4px 8px; }");
        Write("bad.fhtml", "<link rel=\"stylesheet\" href=\"theme.fcss\">\n<style>span.x { color: var(--gap); }</style>\n<div><span class=\"x\">x</span></div>");
        Write("ok.fhtml", "<link rel=\"stylesheet\" href=\"theme.fcss\">\n<div id=\"D\" style=\"padding: var(--pad); border-width: 1px\"><span>x</span></div>");
        var project = new FormaHtmlProject(_directory);

        var bad = Convert("bad.fhtml", project);
        var ok = Convert("ok.fhtml", project);

        Assert.That(bad.Diagnostics.Any(d => d.Code == FormaHtmlDiagnosticCodes.InvalidValue && d.Message.Contains("--gap")), Is.True);
        Assert.That(ok.Xaml, Does.Contain("Padding=\"8,4\""));
    }

    [Test]
    public void ResourceFunction_PassthroughAndRawResources_BridgeToXaml()
    {
        Write("a.fhtml", """
            <meta name="f-namespace" content="sys=clr-namespace:System;assembly=System.Runtime">
            <style>span.x { color: resource(External.Color); -f-Opacity: 0.5; }</style>
            <div>
              <f-resources>
                <sys:Single x:Key="Raw.Value">7</sys:Single>
              </f-resources>
              <span id="L" class="x">x</span>
            </div>
            """);
        var result = Convert("a.fhtml", new FormaHtmlProject(_directory));

        Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
        Assert.That(result.Xaml, Does.Contain("{DynamicResource External.Color}"));
        Assert.That(result.Xaml, Does.Contain("<sys:Single x:Key=\"Raw.Value\">7</sys:Single>"));
        var view = (Control)FormaXamlCompiler.CreateSre().CompileSre(result.Xaml, "a.fhtml.xaml").Build(null);
        view.Resources.TryFind("Raw.Value", out var raw);
        Assert.That(raw, Is.EqualTo(7f));
        Assert.That(Find<Label>(view, "L").Opacity, Is.EqualTo(0.5f));
    }

    [Test]
    public void LinkErrors_AreRejectedWithCodesAndLocations()
    {
        Write("theme.fcss", "span { color: #000000; }");
        Write("broken.fcss", "span {\n  outline: 1px solid #000000;\n}");
        var project = new FormaHtmlProject(_directory);

        FormaDiagnostic First(string html, string code) => FormaHtmlConverter.Convert(html, "a.fhtml", project).Diagnostics.First(d => d.Code == code);

        var missing = First("<link rel=\"stylesheet\" href=\"nope.fcss\"><div></div>", FormaHtmlDiagnosticCodes.LinkTarget);
        Assert.That((missing.Location.FilePath, missing.Location.Line), Is.EqualTo(("a.fhtml", 1)));
        Assert.That(First("<link rel=\"stylesheet\" href=\"https://x.test/a.css\"><div></div>", FormaHtmlDiagnosticCodes.RejectedConstruct), Is.Not.Null);
        Assert.That(First("<link rel=\"stylesheet\" href=\"plain.css\"><div></div>", FormaHtmlDiagnosticCodes.RejectedConstruct), Is.Not.Null);
        Assert.That(First("<link rel=\"icon\" href=\"theme.fcss\"><div></div>", FormaHtmlDiagnosticCodes.RejectedConstruct), Is.Not.Null);
        Assert.That(First("<link rel=\"stylesheet\" href=\"../outside.fcss\"><div></div>", FormaHtmlDiagnosticCodes.LinkTarget), Is.Not.Null);

        var brokenSheet = FormaHtmlConverter.Convert("<link rel=\"stylesheet\" href=\"broken.fcss\"><div><span class=\"x\"></span></div>", "a.fhtml", project);
        var shadow = brokenSheet.Diagnostics.First(d => d.Code == FormaHtmlDiagnosticCodes.RejectedProperty);
        Assert.That((shadow.Location.FilePath, shadow.Location.Line), Is.EqualTo(("broken.fcss", 2)));
    }

    [Test]
    public void ASheetsStyleLocations_MapBackToTheFcssLine()
    {
        Write("theme.fcss", "\n\nbutton.primary {\n  min-width: 100px;\n}");
        Write("a.fhtml", "<link rel=\"stylesheet\" href=\"theme.fcss\"><div></div>");
        var result = Convert("a.fhtml", new FormaHtmlProject(_directory));
        var line = result.Xaml.Split('\n').ToList().FindIndex(l => l.Contains("Selector=\"Button.primary\"")) + 1;

        var mapped = result.Map.Map("a.fhtml", line);

        Assert.That((mapped!.FilePath, mapped.Line), Is.EqualTo(("theme.fcss", 3)));
    }

    [Test]
    public void ABraceInsideADeclarationValue_BelongsToTheValue()
    {
        Write("a.fhtml", "<style>button.x { -f-Template: {StaticResource Missing}; font-weight: bold; }\nbutton.y { opacity: 0.5; }</style><div><button class=\"x\">a</button></div>");

        var result = Convert("a.fhtml", new FormaHtmlProject(_directory));

        Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
        Assert.That(result.Xaml, Does.Contain("Value=\"{StaticResource Missing}\""));
        Assert.That(result.Xaml, Does.Contain("Selector=\"Button.y\""));
    }

    [Test]
    public void ALaterStageDiagnosticInASheetsRule_IsRemappedToTheFcssLineThroughTheSidecar()
    {
        Write("theme.fcss", "\n\nbutton.primary {\n  min-width: 100px;\n}");
        Write("a.fhtml", "<link rel=\"stylesheet\" href=\"theme.fcss\"><div></div>");
        var result = Convert("a.fhtml", new FormaHtmlProject(_directory));
        var xamlPath = Path.Combine(_directory, "obj", "a.fhtml.xaml");
        Directory.CreateDirectory(Path.GetDirectoryName(xamlPath)!);
        File.WriteAllText(xamlPath, result.Xaml);
        File.WriteAllText(xamlPath + ".fhtmlmap", result.Map.Serialize("a.fhtml"));
        var line = result.Xaml.Split('\n').ToList().FindIndex(l => l.Contains("Property=\"CustomMinimumSize\"")) + 1;

        var mapped = FormaHtmlSourceMap.Remap(new FormaSourceLocation(xamlPath, line, 1));

        Assert.That((mapped.FilePath, mapped.Line), Is.EqualTo(("theme.fcss", 4)));
    }

    [Test]
    public void TheDocumentedDesignSystemSheet_ConvertsAndStylesAComponentView()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "docs", "examples", "design-system.fcss"))) root = root.Parent;
        Assert.That(root, Is.Not.Null, "docs/examples/design-system.fcss was not found");
        File.Copy(Path.Combine(root!.FullName, "docs", "examples", "design-system.fcss"), Path.Combine(_directory, "design-system.fcss"));
        Write("a.fhtml", "<link rel=\"stylesheet\" href=\"design-system.fcss\">\n<div id=\"Root\" class=\"gap-3\" style=\"display: flex; flex-direction: column\"><f-border class=\"card\"><span id=\"T\" class=\"title\">Hi</span></f-border><button id=\"B\" class=\"btn btn-primary\">Go</button></div>");

        var view = Build(Convert("a.fhtml", new FormaHtmlProject(_directory)));

        Assert.That(Find<Button>(view, "B").CustomMinimumSize, Is.EqualTo(new Vector2(120, 40)));
        Assert.That(Find<Label>(view, "T").FontSize, Is.EqualTo(28f));
        Assert.That(((SolidColorBrush)((Border)view.Children[0]).Background!).Color, Is.EqualTo(new Color(0x14, 0x21, 0x3D)));
    }

    [Test]
    public void ThemeOverrides_AreSwappedByThemeResourcesApply_AndLiveTokensFollow()
    {
        Write("theme.fcss", """
            :root { --bg: #112233; --dim: 1; }
            :root[data-theme="dark"] { --bg: #000000; --dim: 0.5; }
            @media (prefers-color-scheme: light) { :root { --bg: #FFFFFF; } }
            f-border.card { background-color: var(--bg); }
            span.t { opacity: var(--dim); }
            span[data-x] { }
            """.Replace("span[data-x] { }", string.Empty));
        Write("a.fhtml", "<link rel=\"stylesheet\" href=\"theme.fcss\">\n<div><f-border id=\"C\" class=\"card\"><span id=\"L\" class=\"t\">x</span></f-border></div>");
        var view = Build(Convert("a.fhtml", new FormaHtmlProject(_directory)));
        var border = Find<Border>(view, "C");
        var label = Find<Label>(view, "L");
        Assert.That(((SolidColorBrush)border.Background!).Color, Is.EqualTo(new Color(0x11, 0x22, 0x33)));

        Forma.Xaml.ThemeResources.Apply(view, "dark");
        Assert.That(((SolidColorBrush)border.Background!).Color, Is.EqualTo(Color.Black));
        Assert.That(label.Opacity, Is.EqualTo(0.5f));
        Assert.That(view.GetData("theme"), Is.EqualTo("dark"));

        Forma.Xaml.ThemeResources.Apply(view, "light");
        Assert.That(((SolidColorBrush)border.Background!).Color, Is.EqualTo(Color.White));

        Forma.Xaml.ThemeResources.Apply(view, null);
        Assert.That(((SolidColorBrush)border.Background!).Color, Is.EqualTo(new Color(0x11, 0x22, 0x33)));
        Assert.That(view.GetData("theme"), Is.Null);
    }

    [Test]
    public void DataI18nAndDir_LowerToDataAttributesAndLayoutDirection_AndKeysAreChecked()
    {
        Write("en.json", "{ \"menu.play\": \"Play\", \"menu.quit\": \"Quit\" }");
        Write("a.fhtml", "<meta name=\"f-i18n-keys\" content=\"en.json\">\n<div dir=\"rtl\" lang=\"ar\"><button id=\"P\" data-i18n=\"menu.play\"></button><span id=\"Q\" data-i18n=\"menu.quit\"></span></div>");
        var result = Convert("a.fhtml", new FormaHtmlProject(_directory));
        Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
        Assert.That(result.Xaml, Does.Contain("LayoutDirection=\"RightToLeft\""));

        var view = Build(result);
        Assert.That(view.GetData("dir"), Is.EqualTo("rtl"));
        Assert.That(view.GetData("lang"), Is.EqualTo("ar"));
        Forma.Xaml.Localization.Apply(view, key => key == "menu.play" ? "Jogar" : "Sair");
        Assert.That(Find<Button>(view, "P").Text, Is.EqualTo("Jogar"));
        Assert.That(Find<Label>(view, "Q").Text, Is.EqualTo("Sair"));

        Write("b.fhtml", "<meta name=\"f-i18n-keys\" content=\"en.json\">\n<div><button data-i18n=\"menu.plya\"></button></div>");
        var bad = Convert("b.fhtml", new FormaHtmlProject(_directory)).Diagnostics.First(d => d.Code == FormaHtmlDiagnosticCodes.InvalidValue);
        Assert.That(bad.Message, Does.Contain("Help:"));
        Assert.That(bad.Message, Does.Contain("menu.play"));
        Assert.That(bad.Location.Line, Is.EqualTo(2));
    }

    [Test]
    public void LogicalSpacingProperties_AreRejectedWithTheirPhysicalAlternative()
    {
        Write("a.fhtml", "<div style=\"margin-inline-start: 4px\"></div>");

        var error = Convert("a.fhtml", new FormaHtmlProject(_directory)).Diagnostics.First(d => d.Code == FormaHtmlDiagnosticCodes.RejectedProperty);

        Assert.That(error.Message, Does.Contain("Help:"));
        Assert.That(error.Message, Does.Contain("margin-left"));
    }

    [Test]
    public void TheInspector_ReportsTheFcssRuleAndLineAsTheSourceOfAWinningValue()
    {
        Write("theme.fcss", "\n\nbutton.primary {\n  min-width: 100px;\n  min-height: 40px;\n}");
        Write("a.fhtml", "<link rel=\"stylesheet\" href=\"theme.fcss\">\n<div><button id=\"B\" class=\"primary\">A</button></div>");
        var view = Build(Convert("a.fhtml", new FormaHtmlProject(_directory)));

        var inspection = StyleInspector.Inspect(Find<Button>(view, "B"));

        var winner = inspection.Winners.Single(w => w.Property == "CustomMinimumSize");
        Assert.That(winner.Source, Is.EqualTo("Button.primary @ theme.fcss:3"));
        Assert.That(inspection.Rules.Single(r => r.Matched).Origin, Is.EqualTo("theme.fcss:3"));
    }

    [Test]
    public void TheStylesheetFormatter_IsDeterministicIdempotentAndKeepsComments()
    {
        const string messy = "/* theme */\n:root{--a:#112233}\nbutton.x{min-width:10px;min-height:20px}\n@media (input-modality: pointer){button.x:hover{opacity:.5}}";

        var once = FormaHtmlFormatter.FormatStylesheet(messy, "t.fcss", out var diagnostics);
        var twice = FormaHtmlFormatter.FormatStylesheet(once, "t.fcss", out _);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(twice, Is.EqualTo(once));
        Assert.That(once, Does.StartWith("/* theme */\n\n:root {\n  --a: #112233;\n}\n\nbutton.x {\n  min-width: 10px;"));
        Assert.That(once, Does.Contain("@media (input-modality: pointer) {\n  button.x:hover {\n    opacity: .5;\n  }\n}"));
        Assert.That(FormaHtmlFormatter.FormatStylesheet("button {", "t.fcss", out var broken), Is.EqualTo("button {"));
        Assert.That(broken, Is.Not.Empty);
    }
}

public sealed class FormaFcssBuildTaskTest
{
    private string _directory = null!;

    [SetUp] public void SetUp() { _directory = Path.Combine(Path.GetTempPath(), "forma-fcss-task-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_directory); }
    [TearDown] public void TearDown() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private (bool Success, RecordingBuildEngine Engine) Run()
    {
        var engine = new RecordingBuildEngine();
        var task = new Forma.Xaml.Build.ConvertFormaHtml
        {
            BuildEngine = engine,
            HtmlFiles = Directory.GetFiles(_directory, "*.fhtml").Select(f => (Microsoft.Build.Framework.ITaskItem)new Microsoft.Build.Utilities.TaskItem(f)).ToArray(),
            StylesheetFiles = Directory.GetFiles(_directory, "*.fcss").Select(f => (Microsoft.Build.Framework.ITaskItem)new Microsoft.Build.Utilities.TaskItem(f)).ToArray(),
            OutputDirectory = Path.Combine(_directory, "obj"),
            ProjectDirectory = _directory,
        };
        return (task.Execute(), engine);
    }

    [Test]
    public void ChangingASheet_RewritesOnlyTheViewsThatLinkIt_AndWritesTheDictionaryOnce()
    {
        File.WriteAllText(Path.Combine(_directory, "theme.fcss"), "button.x { opacity: 0.5; }");
        File.WriteAllText(Path.Combine(_directory, "linked.fhtml"), "<link rel=\"stylesheet\" href=\"theme.fcss\"><div><button class=\"x\">a</button></div>");
        File.WriteAllText(Path.Combine(_directory, "plain.fhtml"), "<div><button>b</button></div>");
        Assert.That(Run().Success, Is.True);
        var linked = Path.Combine(_directory, "obj", "linked.fhtml.xaml");
        var plain = Path.Combine(_directory, "obj", "plain.fhtml.xaml");
        var dictionary = Path.Combine(_directory, "obj", "theme.fcss.dict");
        Assert.That(File.Exists(dictionary), Is.True);
        var old = DateTime.UtcNow.AddHours(-1);
        foreach (var file in new[] { linked, plain, dictionary }) File.SetLastWriteTimeUtc(file, old);

        File.WriteAllText(Path.Combine(_directory, "theme.fcss"), "button.x { opacity: 0.25; }");
        Assert.That(Run().Success, Is.True);

        Assert.That(File.GetLastWriteTimeUtc(linked), Is.GreaterThan(old), "the linking view is regenerated");
        Assert.That(File.GetLastWriteTimeUtc(dictionary), Is.GreaterThan(old), "the sheet's dictionary is regenerated");
        Assert.That(File.GetLastWriteTimeUtc(plain), Is.EqualTo(old), "a view that does not link the sheet is untouched");
    }

    [Test]
    public void ABrokenSheetAndAMissingLinkTarget_FailTheTaskWithFcssAndFhtmlLocations()
    {
        File.WriteAllText(Path.Combine(_directory, "broken.fcss"), "span {\n  outline: 1px solid #000000;\n}");
        File.WriteAllText(Path.Combine(_directory, "a.fhtml"), "<link rel=\"stylesheet\" href=\"broken.fcss\">\n<link rel=\"stylesheet\" href=\"nope.fcss\">\n<div></div>");

        var (success, engine) = Run();

        Assert.That(success, Is.False);
        var errors = engine.Errors.Select(e => $"{e.File}({e.LineNumber}) {e.Code}").ToArray();
        Assert.That(errors, Does.Contain("broken.fcss(2) FHTML2005"));
        Assert.That(errors, Does.Contain("a.fhtml(2) FHTML1006"));
    }

    private sealed class RecordingBuildEngine : Microsoft.Build.Framework.IBuildEngine
    {
        public List<Microsoft.Build.Framework.BuildErrorEventArgs> Errors { get; } = [];
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "Fixture.csproj";
        public void LogErrorEvent(Microsoft.Build.Framework.BuildErrorEventArgs args) => Errors.Add(args);
        public void LogWarningEvent(Microsoft.Build.Framework.BuildWarningEventArgs args) { }
        public void LogMessageEvent(Microsoft.Build.Framework.BuildMessageEventArgs args) { }
        public void LogCustomEvent(Microsoft.Build.Framework.CustomBuildEventArgs args) { }
        public bool BuildProjectFile(string projectFileName, string[] targetNames, System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs) => false;
    }
}
