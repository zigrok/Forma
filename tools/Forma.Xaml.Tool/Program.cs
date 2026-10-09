// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Forma.Xaml.Compiler;

namespace Forma.Xaml.Tool;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0) return Usage();
            return args[0] switch
            {
                "validate" => Validate(args.Skip(1).ToArray()),
                "watch" => Watch(args.Skip(1).ToArray()),
                "schema" => Schema(args.Skip(1).ToArray()),
                "lsp" => Lsp(args.Skip(1).ToArray()),
                "format" => Format(args.Skip(1).ToArray()),
                "docs" => Docs(args.Skip(1).ToArray()),
                "preview" => Preview(args.Skip(1).ToArray()),
                "agent" => AgentCommands.Run(args.Skip(1).ToArray()),
                "mcp" => McpServer.Run(),
                "new" => NewCommand.Run(args.Skip(1).ToArray()),
                "--help" or "-h" or "help" => Usage(0),
                _ => Usage(),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static int Validate(string[] args)
    {
        var options = ToolOptions.Parse(args);
        if (options.Paths.Count == 0) return Usage();
        var diagnostics = ValidateFiles(DiscoverFiles(options.Paths), options.RequireCompiledBindings);
        WriteDiagnostics(diagnostics, options.Format);
        return diagnostics.Any(diagnostic => diagnostic.Severity == FormaDiagnosticSeverity.Error) ? 1 : 0;
    }

    private static int Watch(string[] args)
    {
        var options = ToolOptions.Parse(args);
        if (options.Paths.Count == 0) return Usage();
        var files = DiscoverFiles(options.Paths).ToArray();
        WriteDiagnostics(ValidateFiles(files, options.RequireCompiledBindings), options.Format);
        if (options.Once) return 0;
        var directories = files.Select(Path.GetDirectoryName).Where(path => path != null).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        using var finished = new ManualResetEventSlim();
        Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; finished.Set(); };
        using var debounce = new Timer(_ => WriteDiagnostics(ValidateFiles(files, options.RequireCompiledBindings), options.Format));
        var watchers = directories.Select(directory =>
        {
            var watcher = new FileSystemWatcher(directory!, "*.xaml") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName, EnableRaisingEvents = true };
            FileSystemEventHandler changed = (_, _) => debounce.Change(150, Timeout.Infinite);
            RenamedEventHandler renamed = (_, _) => debounce.Change(150, Timeout.Infinite);
            watcher.Changed += changed;
            watcher.Created += changed;
            watcher.Deleted += changed;
            watcher.Renamed += renamed;
            return watcher;
        }).ToArray();
        finished.Wait();
        foreach (var watcher in watchers) watcher.Dispose();
        return 0;
    }

    // forma-xaml preview <View.fhtml> -o out.png [--state hover:#Id] [--lang ja] [--size 1280x720] [--scale 130] [--overlay]
    // Validates the view (stopping at the first error with its help line), then renders it with the project's preview host, named in the
    // nearest forma-preview.json: {"host": "tools/preview.sh"}. The host receives the view path, the output path and the remaining options.
    // Rendering needs the application's renderer, so it lives in the host; the host is a build-host tool and never ships.
    private static int Preview(string[] args)
    {
        var rest = args.ToList();
        var outputIndex = rest.FindIndex(a => a is "-o" or "--output");
        if (rest.Count == 0 || outputIndex < 0 || outputIndex + 1 >= rest.Count) { Console.Error.WriteLine("usage: forma-xaml preview <View.fhtml> -o <out.png> [--state s] [--lang l] [--size WxH] [--scale n] [--overlay]"); return 2; }
        var output = Path.GetFullPath(rest[outputIndex + 1]);
        rest.RemoveRange(outputIndex, 2);
        var view = Path.GetFullPath(rest[0]);
        rest.RemoveAt(0);
        if (!File.Exists(view)) { Console.Error.WriteLine($"{view}: file not found."); return 2; }
        var diagnostics = ValidateFiles(new[] { view }, requireCompiledBindings: false);
        if (diagnostics.Any(d => d.Severity == FormaDiagnosticSeverity.Error))
        {
            WriteDiagnostics(diagnostics, "human");
            return 1;
        }

        string? configPath = null;
        for (var probe = new DirectoryInfo(Path.GetDirectoryName(view)!); probe != null && configPath == null; probe = probe.Parent)
            if (File.Exists(Path.Combine(probe.FullName, "forma-preview.json"))) configPath = Path.Combine(probe.FullName, "forma-preview.json");
        if (configPath == null) { Console.Error.WriteLine("No forma-preview.json found above the view. Help: add {\"host\": \"tools/preview.sh\"} at the project root; the host renders the view with your application's renderer."); return 2; }
        using var config = JsonDocument.Parse(File.ReadAllText(configPath));
        var host = config.RootElement.TryGetProperty("host", out var hostElement) ? hostElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(host)) { Console.Error.WriteLine($"{configPath}: \"host\" is missing. Help: name the script that renders a view."); return 2; }
        var hostPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(configPath)!, host));
        var start = new System.Diagnostics.ProcessStartInfo(hostPath) { WorkingDirectory = Path.GetDirectoryName(configPath)!, UseShellExecute = false };
        start.ArgumentList.Add(view);
        start.ArgumentList.Add(output);
        foreach (var argument in rest) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("The preview host did not start.");
        process.WaitForExit();
        if (process.ExitCode == 0 && !File.Exists(output)) { Console.Error.WriteLine("The preview host succeeded but wrote no image."); return 1; }
        if (process.ExitCode == 0) Console.WriteLine(output);
        return process.ExitCode;
    }

    // forma-xaml docs --out <dir> [--check]: writes (or checks) the generated dialect reference, support matrix, cookbook and editor custom data.
    private static int Docs(string[] args)
    {
        var check = args.Contains("--check");
        var index = Array.IndexOf(args, "--out");
        if (index < 0 || index + 1 >= args.Length) return Usage();
        var directory = args[index + 1];
        var stale = 0;
        foreach (var (relative, content) in AgentKit.DocFiles())
        {
            var path = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
            if (check)
            {
                if (!File.Exists(path) || File.ReadAllText(path) != content) { Console.Error.WriteLine($"{path}: out of date; run forma-xaml docs --out {directory}"); stale++; }
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            Console.WriteLine(path);
        }

        return stale == 0 ? 0 : 1;
    }

    // forma-xaml format [--check] <file|directory...>: formats .fhtml views (the HTML and CSS authoring dialect).
    private static int Format(string[] args)
    {
        var check = args.Contains("--check");
        var paths = args.Where(a => !a.StartsWith('-')).ToList();
        if (paths.Count == 0) return Usage();
        var files = paths.SelectMany(p => Directory.Exists(p)
            ? Directory.EnumerateFiles(p, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".fhtml", StringComparison.Ordinal) || f.EndsWith(".fcss", StringComparison.Ordinal)).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            : new[] { p }).OrderBy(f => f, StringComparer.Ordinal).ToList();
        var failed = 0;
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var formatted = file.EndsWith(".fcss", StringComparison.Ordinal)
                ? Compiler.Html.FormaHtmlFormatter.FormatStylesheet(text, file, out var diagnostics)
                : Compiler.Html.FormaHtmlFormatter.Format(text, file, out diagnostics);
            if (diagnostics.Count > 0)
            {
                foreach (var diagnostic in diagnostics) Console.Error.WriteLine(diagnostic);
                failed++;
                continue;
            }

            if (formatted == text) continue;
            if (check) { Console.Error.WriteLine($"{file}: not formatted"); failed++; }
            else File.WriteAllText(file, formatted);
        }

        Console.WriteLine($"{files.Count - failed} of {files.Count} .fhtml/.fcss files {(check ? "pass the format check" : "formatted")}.");
        return failed == 0 ? 0 : 1;
    }

    private static int Schema(string[] args)
    {
        if (args.Length != 0 && args is not ["--json"]) return Usage();
        var exportedTypes = typeof(Control).Assembly.GetExportedTypes()
            .Where(type => type.Namespace?.StartsWith("Forma", StringComparison.Ordinal) == true)
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();
        var controlTypes = exportedTypes.Where(type => type.IsClass && !type.IsAbstract && typeof(Control).IsAssignableFrom(type)).ToArray();
        var schema = new
        {
            namespaceUri = Forma.Xaml.XamlNamespaces.Forma,
            directives = new[] { "x:Class", "x:Name", "x:Key", "x:DataType" },
            markupExtensions = new[] { "Binding", "StaticResource", "DynamicResource" },
            typeClassifications = new
            {
                foundational = controlTypes.Where(type => !typeof(TemplatedControl).IsAssignableFrom(type)).Select(type => type.Name).ToArray(),
                templated = controlTypes.Where(type => typeof(TemplatedControl).IsAssignableFrom(type)).Select(type => type.Name).ToArray(),
                presenters = controlTypes.Where(type => type.Name.EndsWith("Presenter", StringComparison.Ordinal)).Select(type => type.Name).ToArray(),
            },
            contentModels = new Dictionary<string, string[]>
            {
                ["Container"] = ["Control"],
                ["ContentControl"] = ["Content"],
                ["ResourceDictionary"] = ["keyed resource", "merged dictionary"],
                ["ControlTemplate"] = ["one foundational visual root"],
                ["DataTemplate"] = ["one visual root"],
                ["ItemsPanelTemplate"] = ["one Container root"],
                ["DataGrid"] = ["DataGridColumn"],
            },
            brushes = exportedTypes.Where(type => type.IsClass && !type.IsAbstract && typeof(Brush).IsAssignableFrom(type)).Select(type => type.Name).ToArray(),
            geometry = exportedTypes.Where(type => type.IsClass && !type.IsAbstract && typeof(Geometry).IsAssignableFrom(type)).Select(type => type.Name).ToArray(),
            attachedProperties = exportedTypes.SelectMany(owner => owner.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(method => method.Name.StartsWith("Set", StringComparison.Ordinal) && method.GetParameters().Length == 2 && typeof(Control).IsAssignableFrom(method.GetParameters()[0].ParameterType))
                .Select(method => $"{owner.Name}.{method.Name[3..]}"))
                .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            adaptiveConditions = exportedTypes.Where(type => type.IsClass && !type.IsAbstract && type.Name.EndsWith("Condition", StringComparison.Ordinal)).Select(type => type.Name).ToArray(),
            templates = new[] { "ControlTemplate", "DataTemplate", "ItemsPanelTemplate", "ItemsPresenter", "ContentPresenter", "ScrollPresenter" },
            templateProperties = new[] { "Template", "ItemTemplate", "ItemsPanel", "TargetType", "RelativeSource=TemplatedParent" },
            bindingSources = new[] { "DataContext", "Self", "TemplatedParent", "FindAncestor" },
            selectors = new[] { "type", "*", ".class", "Type.class", "#name", "descendant", "> child", ", union", ":not(...)" },
            selectorBoundaries = new[] { "ordinary visual tree", "template-local visual tree", "projected logical content" },
            pseudoStates = new[] { "hover", "focus", "focus-within", "disabled", "pressed", "checked", "selected", "current", "expanded", "collapsed", "ascending", "descending" },
            templateParts = controlTypes.Where(type => typeof(TemplatedControl).IsAssignableFrom(type))
                .Select(type => new
                {
                    type = type.Name,
                    parts = type.GetCustomAttributes(typeof(TemplatePartAttribute), true).Cast<TemplatePartAttribute>()
                        .Select(part => new { part.Name, partType = part.PartType.Name, part.IsRequired }).ToArray(),
                }).Where(entry => entry.parts.Length != 0).ToArray(),
            dataGridColumns = exportedTypes.Where(type => type.IsClass && !type.IsAbstract && typeof(DataGridColumn).IsAssignableFrom(type)).Select(type => type.Name).ToArray(),
            dataGridBindings = new[] { "Binding", "SortBinding", "Children", "HasChildren", "IsExpanded" },
            dataGridSelection = new[] { "Row", "Cell", "Single", "Multi" },
            timelines = new[] { "FloatTimeline", "ColorTimeline", "Vector2Timeline", "ThicknessTimeline" },
            triggers = new[] { "PropertyTrigger", "EventTrigger" },
        };
        Console.WriteLine(JsonSerializer.Serialize(schema, JsonOptions));
        return 0;
    }

    private static int Lsp(string[] args)
    {
        if (args is not ["--stdio"]) return Usage();
        using var server = new StdioLanguageServer(Console.OpenStandardInput(), Console.OpenStandardOutput());
        server.Run();
        return 0;
    }

    internal static IReadOnlyList<FormaDiagnostic> ValidateFiles(IEnumerable<string> files, bool requireCompiledBindings)
    {
        var parser = new FormaXamlParser();
        var diagnostics = new List<FormaDiagnostic>();
        foreach (var file in files.OrderBy(path => path, StringComparer.Ordinal))
        {
            if (!File.Exists(file))
            {
                diagnostics.Add(new FormaDiagnostic(FormaDiagnosticCodes.XmlSyntax, FormaDiagnosticSeverity.Error, "File does not exist.", new FormaSourceLocation(file, 1, 1)));
                continue;
            }
            var extension = Path.GetExtension(file);
            if (extension.Equals(".fhtml", StringComparison.OrdinalIgnoreCase) || extension.Equals(".fcss", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.AddRange(ValidateHtmlDialect(file, extension.Equals(".fcss", StringComparison.OrdinalIgnoreCase)));
                continue;
            }

            diagnostics.AddRange(parser.Parse(File.ReadAllText(file), file, new FormaXamlParseOptions { RequireCompiledBindings = requireCompiledBindings }).Diagnostics);
        }
        return diagnostics;
    }

    // .fhtml and .fcss are converted with their project (the nearest directory with a .csproj) so links and tokens resolve as in a build;
    // the converter's diagnostics are the validation result, each with a code, a location and a help line.
    private static IReadOnlyList<FormaDiagnostic> ValidateHtmlDialect(string file, bool stylesheet)
    {
        var directory = Path.GetDirectoryName(file)!;
        var root = directory;
        for (var probe = new DirectoryInfo(directory); probe != null; probe = probe.Parent)
            if (probe.EnumerateFiles("*.csproj").Any()) { root = probe.FullName; break; }
        var project = new Forma.Xaml.Compiler.Html.FormaHtmlProject(root);
        var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        var result = stylesheet
            ? project.ConvertSheet(relative)
            : Forma.Xaml.Compiler.Html.FormaHtmlConverter.Convert(File.ReadAllText(file), relative, project);
        return result.Diagnostics;
    }

    internal static IEnumerable<string> DiscoverFiles(IEnumerable<string> paths)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in paths)
        {
            var path = Path.GetFullPath(input);
            if (File.Exists(path) && Path.GetExtension(path) is var fileExtension && (fileExtension.Equals(".xaml", StringComparison.OrdinalIgnoreCase) || fileExtension.Equals(".fhtml", StringComparison.OrdinalIgnoreCase) || fileExtension.Equals(".fcss", StringComparison.OrdinalIgnoreCase))) files.Add(path);
            else
            {
                var directory = Directory.Exists(path) ? path : File.Exists(path) && Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(path)! : null;
                if (directory == null) { files.Add(path); continue; }
                foreach (var file in new[] { "*.xaml", "*.fhtml", "*.fcss" }.SelectMany(pattern => Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)).Where(file => !file.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj"))) files.Add(Path.GetFullPath(file));
            }
        }
        return files;
    }

    private static void WriteDiagnostics(IReadOnlyList<FormaDiagnostic> diagnostics, string format)
    {
        if (format == "human")
        {
            foreach (var diagnostic in diagnostics) Console.WriteLine(diagnostic);
            if (diagnostics.Count == 0) Console.WriteLine("Forma XAML validation succeeded.");
            return;
        }
        if (format == "json")
        {
            Console.WriteLine(JsonSerializer.Serialize(diagnostics, JsonOptions));
            return;
        }
        var results = diagnostics.Select(diagnostic => new
        {
            ruleId = diagnostic.Code,
            level = diagnostic.Severity.ToString().ToLowerInvariant(),
            message = new { text = diagnostic.Message },
            locations = new[] { new { physicalLocation = new { artifactLocation = new { uri = diagnostic.Location.FilePath }, region = new { startLine = diagnostic.Location.Line, startColumn = diagnostic.Location.Column } } } },
        });
        var sarif = new { version = "2.1.0", runs = new[] { new { tool = new { driver = new { name = "Forma.Xaml.Tool" } }, results } } };
        Console.WriteLine(JsonSerializer.Serialize(sarif, JsonOptions));
    }

    private static int Usage(int exitCode = 2)
    {
        Console.Error.WriteLine("Usage: forma-xaml validate [--format human|json|sarif] [--require-compiled-bindings] <project|directory|file...>");
        Console.Error.WriteLine("       forma-xaml watch [--once] [--format human|json|sarif] <project|directory|file...>");
        Console.Error.WriteLine("       forma-xaml schema [--json]");
        Console.Error.WriteLine("       forma-xaml lsp --stdio");
        Console.Error.WriteLine("       forma-xaml format [--check] <directory|file.fhtml...>");
        Console.Error.WriteLine("       forma-xaml preview <View.fhtml> -o <out.png> [--state s] [--lang l] [--size WxH] [--scale n]");
        Console.Error.WriteLine("       forma-xaml docs --out <dir> [--check]");
        Console.Error.WriteLine("       forma-xaml agent install|update|doctor [--target opencode|claude|agents-md|all] [--scope project|user]");
        Console.Error.WriteLine("       forma-xaml mcp");
        Console.Error.WriteLine("       forma-xaml new screen <Name> [--dir <directory>] [--namespace <Namespace>]");
        return exitCode;
    }

    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private sealed class ToolOptions
    {
        public List<string> Paths { get; } = [];
        public string Format { get; private set; } = "human";
        public bool RequireCompiledBindings { get; private set; }
        public bool Once { get; private set; }

        public static ToolOptions Parse(string[] args)
        {
            var result = new ToolOptions();
            for (var index = 0; index < args.Length; index++)
            {
                if (args[index] == "--format" && index + 1 < args.Length) result.Format = args[++index];
                else if (args[index] == "--require-compiled-bindings") result.RequireCompiledBindings = true;
                else if (args[index] == "--once") result.Once = true;
                else if (args[index] is "--configuration" or "--reference") { if (++index >= args.Length) throw new ArgumentException($"{args[index - 1]} requires a value."); }
                else if (args[index].StartsWith('-')) throw new ArgumentException($"Unknown option '{args[index]}'.");
                else result.Paths.Add(args[index]);
            }
            if (result.Format is not ("human" or "json" or "sarif")) throw new ArgumentException($"Unknown output format '{result.Format}'.");
            return result;
        }
    }
}