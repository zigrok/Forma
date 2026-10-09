// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Forma.Xaml.Compiler.Html;

namespace Forma.Xaml.Tool;

// forma-xaml agent install|update|doctor [--target opencode|claude|agents-md|all] [--scope project|user] [--dir <project root>]
internal static class AgentCommands
{
    internal static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("install" or "update" or "doctor")) return Usage();
        var command = args[0];
        var target = Option(args, "--target") ?? "all";
        var scope = Option(args, "--scope") ?? "project";
        var root = Path.GetFullPath(Option(args, "--dir") ?? Directory.GetCurrentDirectory());
        if (target is not ("opencode" or "claude" or "agents-md" or "all") || scope is not ("project" or "user")) return Usage();
        var targets = target == "all" ? new[] { "opencode", "claude", "agents-md" } : new[] { target };
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var problems = new List<string>();
        foreach (var name in targets)
        {
            if (name == "agents-md")
            {
                foreach (var file in new[] { "AGENTS.md", "CLAUDE.md" })
                {
                    var path = Path.Combine(scope == "user" ? home : root, file);
                    if (file == "CLAUDE.md" && !File.Exists(path) && target == "all") continue;
                    if (command == "doctor") CheckBlock(path, problems);
                    else WriteBlock(path);
                }

                continue;
            }

            var skillRoot = Path.Combine(scope == "user" ? home : root, name == "opencode" ? ".opencode" : ".claude", "skills", "forma-ui");
            if (command == "doctor") CheckSkill(skillRoot, problems);
            else WriteSkill(skillRoot);
        }

        if (command != "doctor") return 0;
        CheckTools(root, problems);
        foreach (var problem in problems) Console.Error.WriteLine("agent doctor: " + problem);
        Console.WriteLine(problems.Count == 0 ? "agent doctor: the Forma UI skill and AGENTS.md block are installed, current, and the tools they name exist." : $"agent doctor: {problems.Count} problem(s). Help: run `forma-xaml agent update`.");
        return problems.Count == 0 ? 0 : 1;
    }

    private static void WriteSkill(string skillRoot)
    {
        if (Directory.Exists(skillRoot)) Directory.Delete(skillRoot, recursive: true);
        foreach (var (relative, content) in AgentKit.SkillFiles())
        {
            var path = Path.Combine(skillRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        Console.WriteLine(skillRoot);
    }

    private static void WriteBlock(string path)
    {
        var existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        var block = AgentKit.Block();
        var begin = existing.IndexOf(AgentKit.BlockBegin, StringComparison.Ordinal);
        var end = existing.IndexOf(AgentKit.BlockEnd, StringComparison.Ordinal);
        var updated = begin >= 0 && end > begin
            ? existing.Substring(0, begin) + block + existing.Substring(end + AgentKit.BlockEnd.Length)
            : existing.TrimEnd() + (existing.Length == 0 ? string.Empty : "\n\n") + block + "\n";
        File.WriteAllText(path, updated);
        Console.WriteLine(path);
    }

    private static void CheckSkill(string skillRoot, List<string> problems)
    {
        foreach (var (relative, content) in AgentKit.SkillFiles())
        {
            var path = Path.Combine(skillRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) problems.Add($"{path} is missing.");
            else if (File.ReadAllText(path) != content) problems.Add($"{path} is out of date.");
        }
    }

    private static void CheckBlock(string path, List<string> problems)
    {
        if (!File.Exists(path)) { problems.Add($"{path} is missing."); return; }
        var text = File.ReadAllText(path);
        var begin = text.IndexOf(AgentKit.BlockBegin, StringComparison.Ordinal);
        var end = text.IndexOf(AgentKit.BlockEnd, StringComparison.Ordinal);
        if (begin < 0 || end < begin) problems.Add($"{path} has no Forma block.");
        else if (text.Substring(begin, end + AgentKit.BlockEnd.Length - begin) != AgentKit.Block()) problems.Add($"{path} has an out-of-date Forma block.");
    }

    // The skill names forma-xaml, dotnet and the project's preview host; check they exist.
    private static void CheckTools(string root, List<string> problems)
    {
        if (!OnPath("dotnet")) problems.Add("dotnet is not on PATH.");
        var config = Path.Combine(root, "forma-preview.json");
        if (!File.Exists(config)) { problems.Add($"{config} is missing; `forma-xaml preview` needs a preview host."); return; }
        using var document = JsonDocument.Parse(File.ReadAllText(config));
        var host = document.RootElement.TryGetProperty("host", out var value) ? value.GetString() : null;
        if (string.IsNullOrWhiteSpace(host) || !File.Exists(Path.Combine(root, host))) problems.Add($"the preview host '{host}' named in forma-preview.json does not exist.");
    }

    private static bool OnPath(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator).Any(directory => File.Exists(Path.Combine(directory, program)) || File.Exists(Path.Combine(directory, program + ".exe")));

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: forma-xaml agent install|update|doctor [--target opencode|claude|agents-md|all] [--scope project|user] [--dir <project root>]");
        return 2;
    }
}

// forma-xaml mcp: a stdio MCP server that exposes validate, format and preview as tools and the generated reference as resources.
internal static class McpServer
{
    internal static int Run()
    {
        var input = Console.In;
        var output = Console.Out;
        string? line;
        while ((line = input.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonNode? request;
            try { request = JsonNode.Parse(line); } catch (JsonException) { continue; }
            if (request?["id"] is null) continue; // notifications need no answer
            var response = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = request["id"]!.DeepClone() };
            try { response["result"] = Handle((string?)request["method"] ?? string.Empty, request["params"]?.AsObject()); }
            catch (Exception exception) { response["error"] = new JsonObject { ["code"] = -32603, ["message"] = exception.Message }; }
            output.WriteLine(response.ToJsonString());
            output.Flush();
        }

        return 0;
    }

    internal static JsonNode Handle(string method, JsonObject? parameters) => method switch
    {
        "initialize" => new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject(), ["resources"] = new JsonObject() },
            ["serverInfo"] = new JsonObject { ["name"] = "forma-xaml", ["version"] = "1" },
        },
        "tools/list" => new JsonObject { ["tools"] = new JsonArray(Tool("validate", "Validate .fhtml and .fcss files; returns diagnostics with code, location and help.", "paths"), Tool("format", "Format .fhtml and .fcss files in place (check=true only reports).", "paths"), Tool("preview", "Render one view to a PNG with the project's preview host.", "view", "output")) },
        "tools/call" => CallTool((string)parameters!["name"]!, parameters["arguments"]?.AsObject() ?? new JsonObject()),
        "resources/list" => new JsonObject { ["resources"] = new JsonArray(AgentKit.DocFiles().Where(f => f.Key.EndsWith(".md", StringComparison.Ordinal)).Select(f => (JsonNode)new JsonObject { ["uri"] = "forma-docs://" + f.Key, ["name"] = f.Key, ["mimeType"] = "text/markdown" }).ToArray()) },
        "resources/read" => ReadResource((string)parameters!["uri"]!),
        _ => throw new NotSupportedException($"Unknown method '{method}'."),
    };

    private static JsonObject Tool(string name, string description, params string[] required) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(required.Select(r => KeyValuePair.Create<string, JsonNode?>(r, new JsonObject { ["type"] = r == "paths" ? "array" : "string" }))),
            ["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray()),
        },
    };

    private static JsonNode CallTool(string name, JsonObject arguments)
    {
        var text = name switch
        {
            "validate" => Capture(["validate", "--format", "json", .. Paths(arguments)]),
            "format" => Capture(["format", .. ((bool?)arguments["check"] == true ? new[] { "--check" } : []), .. Paths(arguments)]),
            "preview" => Capture(["preview", (string)arguments["view"]!, "-o", (string)arguments["output"]!, .. Extra(arguments)]),
            _ => throw new NotSupportedException($"Unknown tool '{name}'."),
        };
        return new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }), ["isError"] = false };
    }

    private static IEnumerable<string> Paths(JsonObject arguments) => arguments["paths"] is JsonArray paths ? paths.Select(p => (string)p!) : [(string?)arguments["path"] ?? "."];

    private static IEnumerable<string> Extra(JsonObject arguments) =>
        new[] { "state", "lang", "scale", "size" }.SelectMany(key => arguments[key] is { } value ? new[] { "--" + key, value.ToString() } : []);

    private static string Capture(string[] args)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            var exit = Program.Main(args);
            return $"exit {exit}\n{output}{error}".TrimEnd();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static JsonNode ReadResource(string uri)
    {
        var key = uri.Replace("forma-docs://", string.Empty, StringComparison.Ordinal);
        if (!AgentKit.DocFiles().TryGetValue(key, out var content)) throw new ArgumentException($"No resource '{uri}'.");
        return new JsonObject { ["contents"] = new JsonArray(new JsonObject { ["uri"] = uri, ["mimeType"] = "text/markdown", ["text"] = content }) };
    }
}
