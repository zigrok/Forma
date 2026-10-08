// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text;

namespace Forma.Xaml.Compiler.Html;

/// <summary>Maps lines of the generated XAML back to the HTML or CSS location that produced them.</summary>
public sealed class FormaHtmlSourceMap
{
    private readonly SortedDictionary<int, (string? Path, int Line, int Column)> _lines = new();

    /// <summary>Project-relative paths of the stylesheets the view links; a change to one of them reconverts the view.</summary>
    public List<string> Dependencies { get; } = new();

    internal void Add(int generatedLine, int line, int column, string? path = null) => _lines[generatedLine] = (path, line, column);

    public FormaSourceLocation? Map(string htmlPath, int generatedLine)
    {
        (string? Path, int Line, int Column)? best = null;
        foreach (var pair in _lines)
        {
            if (pair.Key > generatedLine) break;
            best = pair.Value;
        }

        return best is { } found ? new FormaSourceLocation(found.Path ?? htmlPath, found.Line, found.Column) : null;
    }

    public string Serialize(string htmlPath) =>
        "source=" + htmlPath + "\n" + string.Concat(Dependencies.Select(d => "dep=" + d + "\n")) +
        string.Join('\n', _lines.Select(p => $"{p.Key}\t{p.Value.Line}\t{p.Value.Column}\t{p.Value.Path}"));

    /// <summary>Reads a sidecar written by <see cref="Serialize"/>; returns null when the text is not a map.</summary>
    public static (string HtmlPath, FormaHtmlSourceMap Map)? Parse(string text)
    {
        var lines = text.Split('\n');
        if (lines.Length == 0 || !lines[0].StartsWith("source=", StringComparison.Ordinal)) return null;
        var map = new FormaHtmlSourceMap();
        foreach (var line in lines.Skip(1))
        {
            if (line.StartsWith("dep=", StringComparison.Ordinal)) { map.Dependencies.Add(line.Substring(4)); continue; }
            var parts = line.Split('\t');
            if (parts.Length >= 3 && int.TryParse(parts[0], out var gen) && int.TryParse(parts[1], out var src) && int.TryParse(parts[2], out var col))
                map.Add(gen, src, col, parts.Length > 3 && parts[3].Length > 0 ? parts[3] : null);
        }

        return (lines[0].Substring("source=".Length), map);
    }

    /// <summary>Points a diagnostic from the generated XAML back at the HTML or CSS that produced it, when a sidecar map exists.</summary>
    public static FormaSourceLocation Remap(FormaSourceLocation location)
    {
        var sidecar = location.FilePath + ".fhtmlmap";
        if (!System.IO.File.Exists(sidecar) || Parse(System.IO.File.ReadAllText(sidecar)) is not { } parsed) return location;
        return parsed.Map.Map(parsed.HtmlPath, location.Line) ?? new FormaSourceLocation(parsed.HtmlPath, 1, 1);
    }
}

public sealed class FormaHtmlResult
{
    public FormaHtmlResult(string xaml, IReadOnlyList<FormaDiagnostic> diagnostics, FormaHtmlSourceMap map)
    {
        Xaml = xaml;
        Diagnostics = diagnostics;
        Map = map;
    }

    public string Xaml { get; }
    public IReadOnlyList<FormaDiagnostic> Diagnostics { get; }
    public FormaHtmlSourceMap Map { get; }
    public bool Succeeded => Diagnostics.All(d => d.Severity != FormaDiagnosticSeverity.Error);
}

/// <summary>A shared stylesheet (.fcss): strict CSS parsed once, linked from any number of views.</summary>
public sealed class FormaFcssSheet
{
    internal FormaFcssSheet(string path, bool exists, List<CssRule> rules, List<FormaDiagnostic> diagnostics)
    {
        Path = path;
        Exists = exists;
        Rules = rules;
        Diagnostics = diagnostics;
    }

    /// <summary>Project-relative path with forward slashes.</summary>
    public string Path { get; }
    public bool Exists { get; }
    public IReadOnlyList<CssRule> Rules { get; }
    public IReadOnlyList<FormaDiagnostic> Diagnostics { get; }
}

/// <summary>The files of one project, so a stylesheet linked from many views is read and parsed once.</summary>
public sealed class FormaHtmlProject
{
    private readonly Dictionary<string, FormaFcssSheet> _sheets = new(StringComparer.Ordinal);

    public FormaHtmlProject(string baseDirectory) => BaseDirectory = System.IO.Path.GetFullPath(baseDirectory);

    public string BaseDirectory { get; }
    public IReadOnlyCollection<FormaFcssSheet> Sheets => _sheets.Values;

    /// <summary>Resolves a link target relative to the linking file; null when it escapes the project.</summary>
    public static string? Resolve(string linkingFile, string href)
    {
        var combined = href.StartsWith('/') ? href.TrimStart('/') : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(linkingFile.Replace('\\', '/')) ?? string.Empty, href);
        var parts = new List<string>();
        foreach (var part in combined.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..") { if (parts.Count == 0) return null; parts.RemoveAt(parts.Count - 1); continue; }
            parts.Add(part);
        }

        return string.Join('/', parts);
    }

    public FormaFcssSheet Load(string relativePath)
    {
        if (_sheets.TryGetValue(relativePath, out var cached)) return cached;
        var full = System.IO.Path.Combine(BaseDirectory, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        var diagnostics = new List<FormaDiagnostic>();
        var rules = new List<CssRule>();
        var exists = File.Exists(full);
        if (exists)
        {
            var text = File.ReadAllText(full);
            rules = new CssParser(text, relativePath, 1, diagnostics).ParseStylesheet();
        }

        var sheet = new FormaFcssSheet(relativePath, exists, rules, diagnostics);
        _sheets[relativePath] = sheet;
        return sheet;
    }

    /// <summary>The sheet as a XAML ResourceDictionary document with a source map into the .fcss file.</summary>
    public FormaHtmlResult ConvertSheet(string relativePath) => new FormaHtmlConverter(relativePath, this).RunSheet(Load(relativePath));
}

/// <summary>
/// Converts the Forma HTML and CSS dialect to canonical XAML. Anything outside the dialect is a diagnostic with a location in the
/// source file; the converter never approximates. It runs on the build host only.
/// </summary>
public sealed class FormaHtmlConverter
{
    private sealed class Attr
    {
        public Attr(string name, string value, int line, int column, string? path = null) { Name = name; Value = value; Line = line; Column = column; Path = path; }
        public string? Path { get; }
        public string Name { get; }
        public string Value { get; set; }
        public int Line { get; }
        public int Column { get; }
    }

    private sealed class XNode
    {
        public string Type = string.Empty;
        public int Line;
        public int Column;
        public List<Attr> Attrs = new();
        public List<XNode> Children = new();
        public List<(string Name, XNode Value)> PropertyElements = new();
        public string? Path;
        public string? Text;
        public IReadOnlyList<(string Text, int Line, int Column)>? RawBlocks;
    }

    private static readonly Dictionary<string, string> TextElements = new(StringComparer.Ordinal)
    {
        ["span"] = "Label", ["p"] = "Label", ["label"] = "Label", ["h1"] = "Label", ["h2"] = "Label", ["h3"] = "Label", ["h4"] = "Label", ["h5"] = "Label", ["h6"] = "Label",
    };

    private static readonly HashSet<string> BoxElements = new(StringComparer.Ordinal) { "div", "section", "nav", "main", "header", "footer", "ul", "ol", "li" };

    // Type selectors that may appear in a style rule, and the Forma type each stands for.
    private static readonly Dictionary<string, string> SelectorTypes = new(StringComparer.Ordinal)
    {
        ["button"] = "Button", ["span"] = "Label", ["p"] = "Label", ["label"] = "Label", ["h1"] = "Label", ["h2"] = "Label", ["h3"] = "Label",
        ["input"] = "LineEdit", ["div"] = "Control", ["f-border"] = "Border", ["f-group-box"] = "GroupBox", ["f-scroll"] = "ScrollContainer",
    };

    private readonly string _path;
    private readonly List<FormaDiagnostic> _diagnostics = new();
    private readonly Dictionary<string, string> _namespaces = new(StringComparer.Ordinal);
    private readonly List<CssRule> _rules = new();
    private sealed class Token
    {
        public string Name = string.Empty;
        public string Value = string.Empty;
        public string Path = string.Empty;
        public int Line;
        public int Column;
        public string Kind = "compound"; // color, single, compound
        public string Key => "Fcss." + Name.Substring(2);
    }

    private readonly Dictionary<string, Token> _tokens = new(StringComparer.Ordinal);

    private readonly FormaHtmlProject? _project;
    private readonly List<FormaFcssSheet> _sheets = new();
    private readonly List<(string Text, int Line, int Column)> _rawResources = new();

    public FormaHtmlConverter(string path, FormaHtmlProject? project = null)
    {
        _path = path;
        _project = project;
    }

    public static FormaHtmlResult Convert(string html, string path, FormaHtmlProject? project = null) => new FormaHtmlConverter(path, project).Run(html);

    private FormaHtmlResult Run(string html)
    {
        var parser = new HtmlParser(html, _path, _diagnostics);
        var roots = parser.Parse();
        foreach (var node in roots.Where(n => !n.IsText && n.Name == "link")) ReadLink(node);
        foreach (var node in roots) PreRead(node);
        CollectTokens(_sheets.SelectMany(sheet => sheet.Rules).Concat(_rules));
        XNode? root = null;
        foreach (var node in roots)
        {
            if (node.IsText) { Error(FormaHtmlDiagnosticCodes.Structure, "Text outside the root element.", node); continue; }
            if (node.Name == "meta") { ReadMeta(node); continue; }
            if (node.Name is "style" or "link") continue;
            if (root != null) { Error(FormaHtmlDiagnosticCodes.Structure, "A view has exactly one root element.", node); continue; }
            root = new XNode { Line = node.Line, Column = node.Column };
            ConvertElement(node, root, parent: null);
        }

        if (root == null)
        {
            if (_diagnostics.Count == 0) _diagnostics.Add(new FormaDiagnostic(FormaHtmlDiagnosticCodes.Structure, FormaDiagnosticSeverity.Error, "The file has no root element.", new FormaSourceLocation(_path, 1, 1)));
            return new FormaHtmlResult(string.Empty, _diagnostics, new FormaHtmlSourceMap());
        }

        var map = new FormaHtmlSourceMap();
        foreach (var sheet in _sheets) map.Dependencies.Add(sheet.Path);
        var resources = BuildResources(_sheets.SelectMany(sheet => sheet.Rules).Concat(_rules).ToList(), _tokens.Values);
        var writer = new Writer(map, _namespaces);
        var xaml = writer.Write(root, resources, _rawResources);
        return new FormaHtmlResult(xaml, _diagnostics, map);
    }

    internal FormaHtmlResult RunSheet(FormaFcssSheet sheet)
    {
        _diagnostics.AddRange(sheet.Diagnostics);
        if (!sheet.Exists)
        {
            _diagnostics.Add(new FormaDiagnostic(FormaHtmlDiagnosticCodes.LinkTarget, FormaDiagnosticSeverity.Error, $"Stylesheet '{sheet.Path}' was not found.", new FormaSourceLocation(sheet.Path, 1, 1)));
            return new FormaHtmlResult(string.Empty, _diagnostics, new FormaHtmlSourceMap());
        }

        CollectTokens(sheet.Rules);
        var map = new FormaHtmlSourceMap();
        var entries = BuildResources(sheet.Rules, _tokens.Values);
        var xaml = new Writer(map, _namespaces).WriteDictionary(entries, sheet.Path);
        return new FormaHtmlResult(xaml, _diagnostics, map);
    }

    private void CollectTokens(IEnumerable<CssRule> rules)
    {
        foreach (var rule in rules.Where(r => r.Selector == ":root"))
            foreach (var d in rule.Declarations.Where(d => d.Name.StartsWith("--", StringComparison.Ordinal)))
            {
                var value = d.Value.Trim();
                var token = new Token { Name = d.Name, Value = value, Path = d.Path.Length > 0 ? d.Path : _path, Line = d.Line, Column = d.Column };
                token.Kind = value.StartsWith('#') ? "color" : IsSingleLength(value) ? "single" : "compound";
                _tokens[d.Name] = token; // a later definition, such as the view's own, shadows an earlier one
            }
    }

    private static bool IsSingleLength(string value)
    {
        var text = value.EndsWith("px", StringComparison.Ordinal) ? value[..^2] : value.EndsWith("rem", StringComparison.Ordinal) ? value[..^3] : value;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    private void ReadLink(HtmlNode node)
    {
        var rel = node.Attr("rel");
        var href = node.Attr("href");
        if (rel != "stylesheet") { Error(FormaHtmlDiagnosticCodes.RejectedConstruct, $"<link rel=\"{rel}\"> is not part of the dialect; only <link rel=\"stylesheet\" href=\"theme.fcss\"> is supported.", node); return; }
        if (string.IsNullOrWhiteSpace(href)) { Error(FormaHtmlDiagnosticCodes.Structure, "<link rel=\"stylesheet\"> needs an href.", node); return; }
        if (!href.EndsWith(".fcss", StringComparison.Ordinal)) { Error(FormaHtmlDiagnosticCodes.RejectedConstruct, $"Stylesheet '{href}' must be a .fcss file; URLs and plain .css files are not part of the dialect.", node); return; }
        if (_project == null) { Error(FormaHtmlDiagnosticCodes.Structure, "A <link> needs a project to resolve files; convert the view through a FormaHtmlProject.", node); return; }
        var resolved = FormaHtmlProject.Resolve(_path, href);
        if (resolved == null) { Error(FormaHtmlDiagnosticCodes.LinkTarget, $"Stylesheet '{href}' is outside the project.", node); return; }
        var sheet = _project.Load(resolved);
        if (!sheet.Exists) { Error(FormaHtmlDiagnosticCodes.LinkTarget, $"Stylesheet '{href}' was not found (looked for '{resolved}').", node); return; }
        if (_sheets.Contains(sheet)) return;
        _diagnostics.AddRange(sheet.Diagnostics);
        _sheets.Add(sheet);
    }

    // Styles and namespaces are collected before conversion so a custom property or class rule may appear after its use.
    private void PreRead(HtmlNode node)
    {
        if (node.IsText) return;
        if (node.Name == "style") { ReadStyle(node); return; }
        if (node.Name == "f-resources") { _rawResources.Add((node.Children.FirstOrDefault()?.Text ?? string.Empty, node.Line, node.Column)); return; }
        foreach (var child in node.Children) PreRead(child);
    }

    private void ReadMeta(HtmlNode node)
    {
        var name = node.Attr("name");
        var content = node.Attr("content") ?? string.Empty;
        if (name != "f-namespace")
        {
            Error(FormaHtmlDiagnosticCodes.UnknownAttribute, $"<meta name=\"{name}\"> is not part of the dialect; use <meta name=\"f-namespace\" content=\"prefix=clr-namespace:...\">.", node);
            return;
        }

        var eq = content.IndexOf('=');
        if (eq <= 0) { Error(FormaHtmlDiagnosticCodes.InvalidValue, "f-namespace content must be 'prefix=clr-namespace:Namespace'.", node); return; }
        _namespaces[content.Substring(0, eq).Trim()] = content.Substring(eq + 1).Trim();
    }

    private void ReadStyle(HtmlNode node)
    {
        var text = node.Children.FirstOrDefault()?.Text ?? string.Empty;
        var first = node.Children.FirstOrDefault();
        var parser = new CssParser(text, _path, first?.Line ?? node.Line, _diagnostics);
        _rules.AddRange(parser.ParseStylesheet());
    }

    // ---------------------------------------------------------------- elements

    private void ConvertElement(HtmlNode element, XNode target, XNode? parent)
    {
        var inline = new List<CssDeclaration>();
        if (element.Attr("style") is { } style)
        {
            var attr = element.Attributes.First(a => a.Name == "style");
            inline = CssParser.ParseDeclarations(style, _path, attr.Line, attr.Column + "style=\"".Length, _diagnostics);
        }

        var declared = inline.ToDictionary(d => d.Name, d => d, StringComparer.Ordinal);
        var parentDirection = parent == null ? null : DirectionOf(parent);
        var type = ResolveType(element, declared);
        target.Type = type;
        target.Line = element.Line;
        target.Column = element.Column;

        foreach (var attr in element.Attributes) ApplyAttribute(element, attr, target, type);
        ApplyDeclarations(inline, type, target, parentDirection, element);
        ApplyElementSpecifics(element, target, type);

        var children = element.Children.Where(c => !(c.IsText && string.IsNullOrWhiteSpace(c.Text))).ToList();
        if (element.Name is "button" or "span" or "p" or "label" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
        {
            var text = string.Join(" ", children.Where(c => c.IsText).Select(c => c.Text));
            if (text.Length > 0 && !target.Attrs.Any(a => a.Name == "Text")) target.Attrs.Add(new Attr("Text", Escape(text), element.Line, element.Column));
            foreach (var nested in children.Where(c => !c.IsText))
                Error(FormaHtmlDiagnosticCodes.Structure, $"<{element.Name}> holds text only; nested <{nested.Name}> is not allowed.", nested);
            return;
        }

        if (type == "GridPanel")
        {
            ConvertGrid(element, target, inline, children);
            return;
        }

        foreach (var child in children)
        {
            if (child.IsText)
            {
                Error(FormaHtmlDiagnosticCodes.Structure, $"Text directly inside <{element.Name}> has no Forma equivalent; wrap it in <span>.", child);
                continue;
            }

            if (child.Name is "style" or "f-resources") continue;
            var node = new XNode();
            ConvertElement(child, node, target);
            if (node.Type.Length > 0) target.Children.Add(node);
        }

        if (type == "Border" && target.Children.Count > 1)
            Error(FormaHtmlDiagnosticCodes.Structure, "An element with padding, border or background lowers to a Border, which holds one child; wrap the children in a flex container.", element);
    }

    // display: grid and table both lower to a GridPanel; children are placed row by row (or by grid-column and grid-row).
    private void ConvertGrid(HtmlNode element, XNode target, List<CssDeclaration> inline, List<HtmlNode> children)
    {
        var cells = new List<(HtmlNode Node, int Column, int Row, int ColumnSpan)>();
        var columnTemplate = inline.FirstOrDefault(d => d.Name == "grid-template-columns");
        var rowTemplate = inline.FirstOrDefault(d => d.Name == "grid-template-rows");
        var columns = columnTemplate == null ? new List<string>() : Tracks(columnTemplate);
        var rows = rowTemplate == null ? new List<string>() : Tracks(rowTemplate);

        if (element.Name is "table" or "tbody" or "thead")
        {
            var rowIndex = 0;
            foreach (var group in children.Where(c => !c.IsText))
            {
                var trs = group.Name == "tr" ? new List<HtmlNode> { group } : group.Children.Where(c => !c.IsText && c.Name == "tr").ToList();
                if (group.Name is not ("tr" or "tbody" or "thead")) { Error(FormaHtmlDiagnosticCodes.Structure, $"<{group.Name}> is not allowed inside <{element.Name}>; use <tr>, <thead> or <tbody>.", group); continue; }
                foreach (var tr in trs)
                {
                    var column = 0;
                    foreach (var cell in tr.Children.Where(c => !c.IsText))
                    {
                        if (cell.Name is not ("td" or "th")) { Error(FormaHtmlDiagnosticCodes.Structure, $"<{cell.Name}> is not allowed inside <tr>; use <td> or <th>.", cell); continue; }
                        var span = int.TryParse(cell.Attr("colspan"), out var cs) && cs > 1 ? cs : 1;
                        cells.Add((cell, column, rowIndex, span));
                        column += span;
                    }

                    rowIndex++;
                }
            }

            if (columns.Count == 0 && cells.Count > 0) columns = Enumerable.Repeat("Auto", cells.Max(c => c.Column + c.ColumnSpan)).ToList();
        }
        else
        {
            if (columns.Count == 0) { Error(FormaHtmlDiagnosticCodes.Structure, "display: grid needs grid-template-columns, for example grid-template-columns: 120px 1fr.", element); columns.Add("*"); }
            var autoColumn = 0;
            var autoRow = 0;
            foreach (var child in children.Where(c => !c.IsText))
            {
                var declarations = child.Attr("style") is { } style ? CssParser.ParseDeclarations(style, _path, child.Line, child.Column, new List<FormaDiagnostic>()) : new List<CssDeclaration>();
                var column = Placement(declarations, "grid-column", out var span) is { } c ? c - 1 : autoColumn;
                var row = Placement(declarations, "grid-row", out _) is { } r ? r - 1 : autoRow;
                cells.Add((child, column, row, span));
                autoColumn = column + span;
                autoRow = row;
                if (autoColumn >= columns.Count) { autoColumn = 0; autoRow = row + 1; }
            }
        }

        var rowCount = cells.Count == 0 ? 0 : cells.Max(c => c.Row) + 1;
        var columnDefinitions = new XNode { Type = "GridPanel.ColumnDefinitions", Line = element.Line, Column = element.Column };
        foreach (var width in columns)
        {
            var definition = new XNode { Type = "ColumnDefinition", Line = element.Line, Column = element.Column };
            definition.Attrs.Add(new Attr("Width", width, element.Line, element.Column));
            columnDefinitions.Children.Add(definition);
        }

        target.PropertyElements.Add((columnDefinitions.Type, columnDefinitions));
        if (rowTemplate != null || element.Name is "table" or "tbody" or "thead")
        {
            var rowDefinitions = new XNode { Type = "GridPanel.RowDefinitions", Line = element.Line, Column = element.Column };
            for (var i = 0; i < Math.Max(rowCount, rows.Count); i++)
            {
                var definition = new XNode { Type = "RowDefinition", Line = element.Line, Column = element.Column };
                definition.Attrs.Add(new Attr("Height", i < rows.Count ? rows[i] : "Auto", element.Line, element.Column));
                rowDefinitions.Children.Add(definition);
            }

            target.PropertyElements.Add((rowDefinitions.Type, rowDefinitions));
        }

        foreach (var (node, column, row, span) in cells)
        {
            var cell = new XNode();
            if (node.Name is "td" or "th")
            {
                ConvertCell(node, cell, target);
            }
            else
            {
                ConvertElement(node, cell, target);
            }

            if (cell.Type.Length == 0) continue;
            cell.Attrs.Add(new Attr("GridPanel.Column", column.ToString(CultureInfo.InvariantCulture), node.Line, node.Column));
            cell.Attrs.Add(new Attr("GridPanel.Row", row.ToString(CultureInfo.InvariantCulture), node.Line, node.Column));
            if (span > 1) cell.Attrs.Add(new Attr("GridPanel.ColumnSpan", span.ToString(CultureInfo.InvariantCulture), node.Line, node.Column));
            target.Children.Add(cell);
        }
    }

    // A table cell holds text (a Label), one element, or several elements (a Container).
    private void ConvertCell(HtmlNode cell, XNode target, XNode parent)
    {
        var content = cell.Children.Where(c => !(c.IsText && string.IsNullOrWhiteSpace(c.Text))).ToList();
        var onlyText = content.All(c => c.IsText);
        if (onlyText)
        {
            target.Type = "Label";
            target.Line = cell.Line;
            target.Column = cell.Column;
            foreach (var attr in cell.Attributes.Where(a => a.Name is "id" or "class")) ApplyAttribute(cell, attr, target, "Label");
            if (cell.Attr("style") is { } style)
                ApplyDeclarations(CssParser.ParseDeclarations(style, _path, cell.Line, cell.Column, _diagnostics), "Label", target, null, cell);
            if (cell.Name == "th" && !target.Attrs.Any(a => a.Name == "FontWeight")) target.Attrs.Add(new Attr("FontWeight", "Bold", cell.Line, cell.Column));
            var text = string.Join(" ", content.Select(c => c.Text));
            if (text.Length > 0) target.Attrs.Add(new Attr("Text", Escape(text), cell.Line, cell.Column));
            return;
        }

        if (content.Count == 1)
        {
            ConvertElement(content[0], target, parent);
            return;
        }

        target.Type = "Container";
        target.Line = cell.Line;
        target.Column = cell.Column;
        foreach (var child in content)
        {
            if (child.IsText) { Error(FormaHtmlDiagnosticCodes.Structure, "Text mixed with elements in a table cell has no Forma equivalent.", child); continue; }
            var node = new XNode();
            ConvertElement(child, node, target);
            if (node.Type.Length > 0) target.Children.Add(node);
        }
    }

    private List<string> Tracks(CssDeclaration declaration)
    {
        var tracks = new List<string>();
        foreach (var part in declaration.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "auto") tracks.Add("Auto");
            else if (part.EndsWith("fr", StringComparison.Ordinal) && double.TryParse(part[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var fr)) tracks.Add(fr == 1 ? "*" : FormatNumber(fr) + "*");
            else tracks.Add(Px(part, declaration));
        }

        return tracks;
    }

    private static int? Placement(List<CssDeclaration> declarations, string name, out int span)
    {
        span = 1;
        var declaration = declarations.FirstOrDefault(d => d.Name == name);
        if (declaration == null) return null;
        var parts = declaration.Value.Split(new[] { ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && int.TryParse(parts[0], out var start))
        {
            if (parts.Length == 3 && parts[1] == "span" && int.TryParse(parts[2], out var count)) span = count;
            return start;
        }

        if (parts.Length == 2 && parts[0] == "span" && int.TryParse(parts[1], out var only)) span = only;
        return null;
    }

    private string ResolveType(HtmlNode element, Dictionary<string, CssDeclaration> declared)
    {
        var name = element.Name;
        if (TextElements.TryGetValue(name, out var text)) return text;
        switch (name)
        {
            case "button": return "Button";
            case "f-border": return "Border";
            case "f-scroll": return "ScrollContainer";
            case "f-group-box": return "GroupBox";
            case "f-control":
                {
                    var type = element.Attr("type");
                    if (string.IsNullOrWhiteSpace(type)) { Error(FormaHtmlDiagnosticCodes.Structure, "<f-control> requires type=\"prefix:TypeName\".", element); return "Control"; }
                    return type;
                }
            case "input":
                return (element.Attr("type") ?? "text") switch
                {
                    "text" => "LineEdit",
                    "checkbox" => "CheckBox",
                    "range" => "HSlider",
                    var other => Reject(element, $"<input type=\"{other}\"> has no Forma control; supported types are text, checkbox and range."),
                };
            case "textarea": return "TextEdit";
            case "select": return "OptionButton";
            case "img": return Reject(element, "<img> has no defined Forma mapping yet; use f-control type=\"…\" for an application image control.");
        }

        if (!BoxElements.Contains(name) && name != "table" && name != "tr" && name != "td" && name != "th" && name != "tbody" && name != "thead")
            return Reject(element, $"<{name}> is not part of the dialect.", FormaHtmlDiagnosticCodes.UnknownElement);

        if (name is "table" or "tbody" or "thead") return "GridPanel";
        if (name is "tr") return "Container";
        if (name is "td" or "th") return "Container";

        var display = declared.TryGetValue("display", out var d) ? d.Value : null;
        var decorated = declared.Keys.Any(k => k is "padding" or "border-width" or "border-radius" or "background-color" or "border-color") && display is not ("flex" or "grid");
        if (decorated) return "Border";
        if (display == "flex")
        {
            var direction = declared.TryGetValue("flex-direction", out var fd) ? fd.Value : "row";
            return direction == "column" ? "VBoxContainer" : "HBoxContainer";
        }

        if (display == "grid") return "GridPanel";
        if (name is "ul" or "ol") return "VBoxContainer";
        var hasChildren = element.Children.Any(c => !c.IsText || !string.IsNullOrWhiteSpace(c.Text));
        return hasChildren ? "Container" : "Control";
    }

    private string Reject(HtmlNode element, string message, string code = FormaHtmlDiagnosticCodes.RejectedConstruct)
    {
        Error(code, message, element);
        return string.Empty;
    }

    private void ApplyAttribute(HtmlNode element, HtmlAttribute attribute, XNode target, string type)
    {
        var name = attribute.Name;
        var value = attribute.Value;
        void Add(string property, string text) => target.Attrs.Add(new Attr(property, text, attribute.Line, attribute.Column));

        switch (name)
        {
            case "style":
            case "data-follow-focus" or "data-horizontal" or "data-vertical" or "data-step-buttons" when element.Name == "f-scroll":
            case "type" when element.Name is "f-control" or "input":
            case "name" when element.Name == "meta":
                return;
            case "id": Add("x:Name", value); return;
            case "class": Add("Classes", value); return;
            case "hidden": Add("Visible", "False"); return;
            case "disabled": Add("Enabled", "False"); return;
            case "aria-label": Add("AccessibilityLabel", Escape(value)); return;
            case "data-automation-id": Add("AutomationId", value); return;
            case "data-class": Add("x:Class", value); return;
            case "data-type": Add("x:DataType", value); return;
            case "tabindex":
                if (value == "-1") Add("FocusMode", "None");
                else if (value == "0") Add("FocusMode", "All");
                else Error(FormaHtmlDiagnosticCodes.InvalidValue, "tabindex must be 0 (focusable) or -1 (not focusable).", attribute);
                return;
            case "title": Add("TooltipText", Escape(value)); return;
            case "checked": Add("Checked", "True"); return;
            case "value" when element.Name == "input": Add(type == "LineEdit" ? "Text" : "Value", value); return;
            case "placeholder": Add("PlaceholderText", Escape(value)); return;
            case "min" when element.Name == "input": Add("MinValue", value); return;
            case "max" when element.Name == "input": Add("MaxValue", value); return;
            case "step" when element.Name == "input": Add("Step", value); return;
            case "alt": Add("AccessibilityLabel", Escape(value)); return;
            case "colspan" or "rowspan": return; // read by the grid placement
        }

        if (name.StartsWith("on", StringComparison.Ordinal) && name.Length > 2)
        {
            if (!IsIdentifier(value)) { Error(FormaHtmlDiagnosticCodes.InvalidValue, $"{name} names a code-behind handler, never an expression: {name}=\"OnSomethingPressed\".", attribute); return; }
            var evt = name switch { "onclick" => "Pressed", "onchange" => "Changed", "onfocus" => "FocusEntered", "onblur" => "FocusExited", _ => string.Empty };
            if (evt.Length == 0) Error(FormaHtmlDiagnosticCodes.UnknownAttribute, $"Event attribute '{name}' is not part of the dialect (onclick, onchange, onfocus, onblur).", attribute);
            else Add(evt, value);
            return;
        }

        if (name.StartsWith("bind:", StringComparison.Ordinal))
        {
            var property = Pascal(name.Substring(5));
            Add(property, BindingText(value, attribute));
            return;
        }

        if (name.StartsWith("f:", StringComparison.Ordinal))
        {
            // Escape hatch: any Forma property, passed through verbatim and validated by the XAML compiler.
            Add(name.Substring(2), value);
            return;
        }

        Error(FormaHtmlDiagnosticCodes.UnknownAttribute, $"Attribute '{name}' is not part of the dialect. Use f:{Pascal(name)}=\"…\" to set a Forma property directly.", attribute);
    }

    private string BindingText(string value, HtmlAttribute attribute)
    {
        var parts = value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !IsPath(parts[0])) { Error(FormaHtmlDiagnosticCodes.InvalidValue, "A binding names a view-model property path: bind:text=\"PlayText\".", attribute); return "{Binding}"; }
        var text = "{Binding " + parts[0];
        foreach (var option in parts.Skip(1))
        {
            var eq = option.IndexOf('=');
            if (eq <= 0) { Error(FormaHtmlDiagnosticCodes.InvalidValue, "Binding options are 'mode=TwoWay'.", attribute); continue; }
            text += ", " + Pascal(option.Substring(0, eq).Trim()) + "=" + option.Substring(eq + 1).Trim();
        }

        return text + "}";
    }

    private void ApplyElementSpecifics(HtmlNode element, XNode target, string type)
    {
        if (element.Name is "f-scroll")
        {
            foreach (var pair in new[] { ("follow-focus", "FollowFocus"), ("horizontal", "HorizontalScrollMode"), ("vertical", "VerticalScrollMode"), ("step-buttons", "ShowVerticalStepButtons") })
            {
                var value = element.Attr("data-" + pair.Item1);
                if (value != null && !target.Attrs.Any(a => a.Name == pair.Item2)) target.Attrs.Add(new Attr(pair.Item2, value, element.Line, element.Column));
            }
        }
    }

    // ---------------------------------------------------------------- css

    private static string? DirectionOf(XNode node) => node.Type switch { "VBoxContainer" => "column", "HBoxContainer" => "row", _ => null };

    private void ApplyDeclarations(IReadOnlyList<CssDeclaration> declarations, string type, XNode target, string? parentDirection, HtmlNode source)
    {
        foreach (var (property, value, declaration) in Lower(declarations, type, parentDirection))
            target.Attrs.Add(new Attr(property, value, declaration.Line, declaration.Column));
    }

    private IEnumerable<(string Property, string Value, CssDeclaration Source)> Lower(IReadOnlyList<CssDeclaration> declarations, string type, string? parentDirection)
    {
        CssDeclaration? minWidth = null, minHeight = null;
        var isBox = type is "VBoxContainer" or "HBoxContainer";
        foreach (var d in declarations)
        {
            if (d.Name.StartsWith("-f-", StringComparison.Ordinal))
            {
                // Escape hatch to any Forma property, like the f: attribute: -f-BackgroundColor: #40040C16 or -f-Template: {StaticResource Key}.
                var raw = ResourceFunction(d.Value) is { } resourceKey ? "{DynamicResource " + resourceKey + "}" : ResolveVar(d.Value, d);
                yield return (d.Name.Substring(3), raw, d);
                continue;
            }

            if (DynamicFor(d, isBox) is { } dynamic)
            {
                yield return dynamic;
                continue;
            }

            var value = ResolveVar(d.Value, d);
            switch (d.Name)
            {
                case "display":
                    if (value is "flex" or "grid" or "block") break;
                    if (value == "none") { yield return ("Visible", "False", d); break; }
                    Error(FormaHtmlDiagnosticCodes.InvalidValue, $"display: {value} is not part of the dialect (flex, grid, block, none).", d);
                    break;
                case "flex-direction":
                    if (value is not ("row" or "column")) Error(FormaHtmlDiagnosticCodes.InvalidValue, "flex-direction supports row and column.", d);
                    break;
                case "gap":
                    if (!isBox) { Error(FormaHtmlDiagnosticCodes.RejectedProperty, "gap applies to display: flex containers.", d); break; }
                    yield return ("Separation", Px(value, d), d);
                    break;
                case "min-width": minWidth = d; break;
                case "min-height": minHeight = d; break;
                case "padding":
                    yield return (isBox ? "Margins" : "Padding", Thickness(value, d), d);
                    break;
                case "border-width": yield return ("BorderThickness", Thickness(value, d), d); break;
                case "border-radius": yield return ("CornerRadius", Thickness(value, d), d); break;
                case "border-color": yield return ("BorderBrush", Color(value, d), d); break;
                case "background-color": yield return ("Background", Color(value, d), d); break;
                case "color": yield return ("FontColor", Color(value, d), d); break;
                case "font-size": yield return ("FontSize", Px(value, d), d); break;
                case "font-weight":
                    yield return ("FontWeight", value switch { "normal" or "400" => "Normal", "bold" or "700" => "Bold", _ => Invalid(d, "font-weight supports normal, bold, 400 and 700.") }, d);
                    break;
                case "opacity": yield return ("Opacity", Number(value, d), d); break;
                case "pointer-events":
                    yield return ("MouseFilter", value switch { "none" => "Ignore", "auto" => "Stop", _ => Invalid(d, "pointer-events supports none and auto.") }, d);
                    break;
                case "text-align":
                    yield return ("HorizontalAlignment", value switch { "left" or "start" => "Left", "center" => "Center", "right" or "end" => "Right", _ => Invalid(d, "text-align supports left, center and right.") }, d);
                    break;
                case "vertical-align":
                    yield return ("VerticalAlignment", value switch { "top" => "Top", "middle" => "Center", "bottom" => "Bottom", _ => Invalid(d, "vertical-align supports top, middle and bottom.") }, d);
                    break;
                case "align-self":
                    {
                        var alignment = value switch { "start" or "flex-start" => "Left", "center" => "Center", "end" or "flex-end" => "Right", "stretch" => "Fill", _ => Invalid(d, "align-self supports start, center, end and stretch.") };
                        if (parentDirection == "row") yield return ("VerticalAlignment", alignment.Replace("Left", "Top").Replace("Right", "Bottom"), d);
                        else yield return ("HorizontalAlignment", alignment, d);
                        break;
                    }
                case "flex-grow":
                    if (value != "0")
                    {
                        if (parentDirection == null) { Error(FormaHtmlDiagnosticCodes.RejectedProperty, "flex-grow needs a display: flex parent.", d); break; }
                        yield return (parentDirection == "row" ? "HorizontalSizeFlags" : "VerticalSizeFlags", "Expand", d);
                    }
                    break;
                case "grid-template-columns" or "grid-template-rows" or "grid-column" or "grid-row":
                    break; // read by the grid lowering
                case "position" or "float" or "filter" or "backdrop-filter" or "box-shadow" or "text-shadow" or "background" or "background-image" or "transform":
                    Error(FormaHtmlDiagnosticCodes.RejectedProperty, $"'{d.Name}' is not supported: {RejectionReason(d.Name)}.", d);
                    break;
                case "transition":
                    break; // lowered to style transitions in a rule
                default:
                    if (d.Name.StartsWith("--", StringComparison.Ordinal)) break;
                    Error(FormaHtmlDiagnosticCodes.UnknownProperty, $"CSS property '{d.Name}' is not part of the dialect.", d);
                    break;
            }
        }

        if (minWidth != null || minHeight != null)
        {
            var width = minWidth == null ? "0" : Px(ResolveVar(minWidth.Value, minWidth), minWidth);
            var height = minHeight == null ? "0" : Px(ResolveVar(minHeight.Value, minHeight), minHeight);
            yield return ("CustomMinimumSize", $"{width},{height}", (minWidth ?? minHeight)!);
        }
    }

    private static string RejectionReason(string property) => property switch
    {
        "position" => "absolute and fixed positioning have no defined overlay mapping",
        "float" => "use display: flex",
        "box-shadow" or "text-shadow" or "filter" or "backdrop-filter" => "the renderer has no such effect",
        "background" or "background-image" => "use background-color; gradients and images are not supported",
        "transform" => "transforms are not part of the dialect",
        _ => "not part of the dialect",
    };

    private string Invalid(CssDeclaration d, string message)
    {
        Error(FormaHtmlDiagnosticCodes.InvalidValue, message, d);
        return string.Empty;
    }

    private string ResolveVar(string value, CssDeclaration at)
    {
        var start = value.IndexOf("var(", StringComparison.Ordinal);
        if (start < 0) return value;
        var end = value.IndexOf(')', start);
        var name = value.Substring(start + 4, end - start - 4).Trim();
        if (_tokens.TryGetValue(name, out var resolved)) return value.Substring(0, start) + resolved.Value + value.Substring(end + 1);
        Error(FormaHtmlDiagnosticCodes.InvalidValue, $"Custom property '{name}' is not defined in a :root rule.", at);
        return value;
    }

    private static string? ResourceFunction(string value)
    {
        value = value.Trim();
        return value.StartsWith("resource(", StringComparison.Ordinal) && value.EndsWith(')') ? value.Substring(9, value.Length - 10).Trim() : null;
    }

    // Properties that can observe a resource take a live {DynamicResource}: a custom property of the matching kind, or resource(Key)
    // for a resource defined elsewhere (for example in XAML). Everything else substitutes the custom property's value at build time.
    private (string Property, string Value, CssDeclaration Source)? DynamicFor(CssDeclaration d, bool isBox)
    {
        (string Property, string Kind)? target = d.Name switch
        {
            "color" => ("FontColor", "color"),
            "background-color" => ("Background", "brush"),
            "border-color" => ("BorderBrush", "brush"),
            "font-size" => ("FontSize", "single"),
            "opacity" => ("Opacity", "single"),
            "gap" when isBox => ("Separation", "single"),
            _ => null,
        };
        var value = d.Value.Trim();
        if (ResourceFunction(value) is { } key)
        {
            if (target == null) { Error(FormaHtmlDiagnosticCodes.RejectedProperty, $"resource() is supported for color, background-color, border-color, font-size, opacity and gap, and for -f-* properties; not '{d.Name}'.", d); return (string.Empty, string.Empty, d); }
            return (target.Value.Property, "{DynamicResource " + key + "}", d);
        }

        if (target != null && value.StartsWith("var(", StringComparison.Ordinal) && value.EndsWith(')') && value.IndexOf("var(", 1, StringComparison.Ordinal) < 0)
        {
            var name = value.Substring(4, value.Length - 5).Trim();
            if (!_tokens.TryGetValue(name, out var token)) return null; // reported by ResolveVar
            var wanted = target.Value.Kind == "single" ? "single" : "color";
            if (token.Kind != wanted)
            {
                Error(FormaHtmlDiagnosticCodes.InvalidValue, $"Custom property '{name}' is a {token.Kind} value and cannot set '{d.Name}'.", d);
                return (string.Empty, string.Empty, d);
            }

            var resource = token.Key + (target.Value.Kind == "color" ? ".color" : string.Empty);
            return (target.Value.Property, "{DynamicResource " + resource + "}", d);
        }

        return null;
    }

    private string Px(string value, CssDeclaration d)
    {
        value = value.Trim();
        if (value.EndsWith("px", StringComparison.Ordinal)) value = value[..^2];
        else if (value == "0") { }
        else if (value.EndsWith("rem", StringComparison.Ordinal) && double.TryParse(value[..^3], NumberStyles.Float, CultureInfo.InvariantCulture, out var rem))
            return FormatNumber(rem * 16);
        else if (value.Any(char.IsLetter) || value.EndsWith('%'))
        {
            Error(FormaHtmlDiagnosticCodes.UnsupportedUnit, $"Unit in '{value}' is not supported; use px or rem.", d);
            return "0";
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) { Error(FormaHtmlDiagnosticCodes.InvalidValue, $"'{value}' is not a length.", d); return "0"; }
        return FormatNumber(number);
    }

    private string Number(string value, CssDeclaration d)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) { Error(FormaHtmlDiagnosticCodes.InvalidValue, $"'{value}' is not a number.", d); return "0"; }
        return FormatNumber(number);
    }

    private string Thickness(string value, CssDeclaration d)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => Px(p, d)).ToArray();
        if (parts.Length is < 1 or > 4) { Error(FormaHtmlDiagnosticCodes.InvalidValue, "Expected one to four lengths.", d); return "0"; }
        // CSS order is top, right, bottom, left; Forma writes left, top, right, bottom.
        string top = parts[0], right = parts.Length > 1 ? parts[1] : parts[0], bottom = parts.Length > 2 ? parts[2] : parts[0], left = parts.Length > 3 ? parts[3] : right;
        if (top == right && right == bottom && bottom == left) return top;
        if (left == right && top == bottom) return $"{left},{top}";
        return $"{left},{top},{right},{bottom}";
    }

    private string Color(string value, CssDeclaration d)
    {
        value = value.Trim();
        if (value.StartsWith('#') && value.Length is 7 or 9 && value.Skip(1).All(Uri.IsHexDigit))
        {
            // CSS hex is #RRGGBB or #RRGGBBAA; Forma hex is #AARRGGBB.
            return value.Length == 7 ? "#FF" + value[1..].ToUpperInvariant() : "#" + value[7..9].ToUpperInvariant() + value[1..7].ToUpperInvariant();
        }

        Error(FormaHtmlDiagnosticCodes.InvalidValue, $"'{value}' is not a color; use #RRGGBB or #RRGGBBAA.", d);
        return "#FF000000";
    }

    private static string FormatNumber(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- rules to styles

    private List<XNode> BuildResources(IReadOnlyList<CssRule> rules, IEnumerable<Token> tokens)
    {
        var styles = new List<XNode>();
        foreach (var token in tokens)
        {
            var path = token.Path;
            XNode Entry(string type, string key, string? text = null, string? color = null)
            {
                var node = new XNode { Type = type, Line = token.Line, Column = token.Column, Path = path, Text = text };
                node.Attrs.Add(new Attr("x:Key", key, token.Line, token.Column, path));
                if (color != null) node.Attrs.Add(new Attr("Color", color, token.Line, token.Column, path));
                return node;
            }

            var probe = new CssDeclaration(token.Name, token.Value, token.Line, token.Column) { Path = path };
            if (token.Kind == "color")
            {
                var hex = Color(token.Value, probe);
                styles.Add(Entry("SolidColorBrush", token.Key, color: hex));
                styles.Add(Entry("xna:Color", token.Key + ".color", text: hex));
            }
            else if (token.Kind == "single")
            {
                styles.Add(Entry("x:Single", token.Key, text: Px(token.Value, probe)));
            }
        }

        foreach (var rule in rules)
        {
            if (rule.Selector == ":root")
            {
                foreach (var d in rule.Declarations.Where(d => !d.Name.StartsWith("--", StringComparison.Ordinal)))
                    Error(FormaHtmlDiagnosticCodes.UnsupportedSelector, "A :root rule only declares custom properties (--name: value).", d);
            }
        }

        var index = 0;
        foreach (var rule in rules.Where(r => r.Selector != ":root"))
        {
            var selector = LowerSelector(rule, out var subjectType);
            if (selector == null) continue;
            var style = new XNode { Type = "Style", Line = rule.Line, Column = rule.Column, Path = rule.Path };
            style.Attrs.Add(new Attr("x:Key", $"FormaHtmlStyle{index++}", rule.Line, rule.Column, rule.Path));
            style.Attrs.Add(new Attr("Selector", selector, rule.Line, rule.Column, rule.Path));
            if (rule.Media != null) AddCondition(style, rule);
            var transitions = new List<(string Property, string Duration)>();
            foreach (var d in rule.Declarations.Where(d => d.Name == "transition"))
            {
                foreach (var part in d.Value.Split(',', StringSplitOptions.TrimEntries))
                {
                    var bits = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (bits.Length < 2) { Error(FormaHtmlDiagnosticCodes.InvalidValue, "transition is 'property duration', for example opacity 150ms.", d); continue; }
                    transitions.Add((TransitionProperty(bits[0], d), Duration(bits[1], d)));
                }
            }

            foreach (var (property, value, source) in Lower(rule.Declarations.Where(d => d.Name != "transition").ToList(), subjectType, parentDirection: null))
            {
                var setter = new XNode { Type = "Setter", Line = source.Line, Column = source.Column, Path = source.Path };
                setter.Attrs.Add(new Attr("Property", property, source.Line, source.Column, source.Path));
                setter.Attrs.Add(new Attr("Value", value, source.Line, source.Column, source.Path));
                style.Children.Add(setter);
            }

            foreach (var (property, duration) in transitions)
            {
                var transition = new XNode { Type = TransitionType(property), Line = rule.Line, Column = rule.Column, Path = rule.Path };
                transition.Attrs.Add(new Attr("Property", property, rule.Line, rule.Column, rule.Path));
                transition.Attrs.Add(new Attr("Duration", duration, rule.Line, rule.Column, rule.Path));
                style.Children.Add(transition);
            }

            styles.Add(style);
        }

        return styles;
    }

    private static string TransitionType(string property) => property switch { "Opacity" => "FloatTransition", "FontSize" => "FloatTransition", "Background" or "BorderBrush" or "FontColor" => "ColorTransition", _ => "FloatTransition" };

    private string TransitionProperty(string name, CssDeclaration d) => name switch
    {
        "opacity" => "Opacity",
        "background-color" => "Background",
        "border-color" => "BorderBrush",
        "color" => "FontColor",
        "font-size" => "FontSize",
        _ => Invalid(d, $"transition: '{name}' cannot be animated; Forma animates opacity, background-color, border-color, color and font-size."),
    };

    private string Duration(string value, CssDeclaration d)
    {
        if (value.EndsWith("ms", StringComparison.Ordinal) && double.TryParse(value[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms))
            return TimeSpan.FromMilliseconds(ms).ToString("c", CultureInfo.InvariantCulture);
        if (value.EndsWith('s') && double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
            return TimeSpan.FromSeconds(s).ToString("c", CultureInfo.InvariantCulture);
        return Invalid(d, "A duration is written in ms or s.");
    }

    private void AddCondition(XNode style, CssRule rule)
    {
        var media = rule.Media!.Trim();
        var inner = media.StartsWith('(') && media.EndsWith(')') ? media[1..^1].Split(':', 2, StringSplitOptions.TrimEntries) : [];
        var condition = new XNode { Type = "Style.Condition", Line = rule.Line, Column = rule.Column };
        var adaptive = new XNode { Type = "AdaptiveCondition", Line = rule.Line, Column = rule.Column };
        if (inner is ["input-modality", var modality] && modality is "pointer" or "keyboard" or "gamepad" or "touch")
            adaptive.Attrs.Add(new Attr("InputModality", char.ToUpperInvariant(modality[0]) + modality[1..], rule.Line, rule.Column));
        else if (inner is ["min-width", var min]) adaptive.Attrs.Add(new Attr("MinViewportWidth", Px(min, new CssDeclaration("min-width", min, rule.Line, rule.Column) { Path = rule.Path }), rule.Line, rule.Column));
        else if (inner is ["max-width", var max]) adaptive.Attrs.Add(new Attr("MaxViewportWidth", Px(max, new CssDeclaration("max-width", max, rule.Line, rule.Column) { Path = rule.Path }), rule.Line, rule.Column));
        else Error(FormaHtmlDiagnosticCodes.UnsupportedSelector, $"@media {media} is not supported; use (input-modality: pointer|keyboard|gamepad|touch), (min-width: Npx) or (max-width: Npx).", rule);
        condition.Children.Add(adaptive);
        style.PropertyElements.Add(("Style.Condition", adaptive));
    }

    private string? LowerSelector(CssRule rule, out string subjectType)
    {
        subjectType = "Control";
        var arms = new List<string>();
        foreach (var arm in rule.Selector.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var lowered = new StringBuilder();
            var tokens = Tokenize(arm);
            var last = string.Empty;
            foreach (var token in tokens)
            {
                if (token is ">" or "+" or "~")
                {
                    if (token != ">") { Error(FormaHtmlDiagnosticCodes.UnsupportedSelector, $"Combinator '{token}' is not supported; use descendant or child (>).", rule); return null; }
                    lowered.Append(" > ");
                    continue;
                }

                if (lowered.Length > 0 && !lowered.ToString().EndsWith("> ", StringComparison.Ordinal)) lowered.Append(' ');
                var compound = LowerCompound(token, rule, out last);
                if (compound == null) return null;
                lowered.Append(compound);
            }

            if (last.Length > 0) subjectType = last;
            arms.Add(lowered.ToString());
        }

        return arms.Count == 0 ? null : string.Join(", ", arms);
    }

    private static List<string> Tokenize(string selector)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var depth = 0;
        foreach (var ch in selector)
        {
            if (ch == '(') depth++;
            if (ch == ')') depth--;
            if (depth == 0 && char.IsWhiteSpace(ch)) { if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); } continue; }
            if (depth == 0 && ch == '>') { if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); } tokens.Add(">"); continue; }
            current.Append(ch);
        }

        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }

    private string? LowerCompound(string compound, CssRule rule, out string type)
    {
        type = string.Empty;
        var output = new StringBuilder();
        var i = 0;
        var typeName = new StringBuilder();
        while (i < compound.Length && (char.IsLetterOrDigit(compound[i]) || compound[i] is '-' or '*'))
            typeName.Append(compound[i++]);
        if (typeName.Length > 0 && typeName.ToString() != "*")
        {
            if (!SelectorTypes.TryGetValue(typeName.ToString(), out var forma)) { Error(FormaHtmlDiagnosticCodes.UnsupportedSelector, $"Type selector '{typeName}' has no Forma type; use a class selector.", rule); return null; }
            type = forma;
            output.Append(forma);
        }

        while (i < compound.Length)
        {
            var kind = compound[i++];
            var start = i;
            if (kind == ':' && i < compound.Length && compound[i] == ':') { Error(FormaHtmlDiagnosticCodes.UnsupportedSelector, "Pseudo-elements are not supported.", rule); return null; }
            while (i < compound.Length && (char.IsLetterOrDigit(compound[i]) || compound[i] is '-' or '_')) i++;
            var name = compound.Substring(start, i - start);
            switch (kind)
            {
                case '.': output.Append('.').Append(name); break;
                case '#': output.Append('#').Append(name); break;
                case ':':
                    if (name == "not" && i < compound.Length && compound[i] == '(')
                    {
                        var close = compound.IndexOf(')', i);
                        var inner = LowerCompound(compound.Substring(i + 1, close - i - 1), rule, out _);
                        if (inner == null) return null;
                        output.Append(":not(").Append(inner).Append(')');
                        i = close + 1;
                    }
                    else if (name is "hover" or "focus" or "disabled" or "checked" or "selected") output.Append(':').Append(name);
                    else if (name == "active") output.Append(":pressed");
                    else { Error(FormaHtmlDiagnosticCodes.UnsupportedSelector, $"Pseudo-class ':{name}' is not supported (hover, active, focus, disabled, checked, selected, not).", rule); return null; }
                    break;
                default:
                    Error(FormaHtmlDiagnosticCodes.UnsupportedSelector, $"Selector part '{compound}' is not supported.", rule);
                    return null;
            }
        }

        return output.ToString();
    }

    // ---------------------------------------------------------------- misc

    private static string Pascal(string name)
    {
        var parts = name.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    private static bool IsIdentifier(string value) => value.Length > 0 && (char.IsLetter(value[0]) || value[0] == '_') && value.All(c => char.IsLetterOrDigit(c) || c == '_');
    private static bool IsPath(string value) => value.Length > 0 && value.All(c => char.IsLetterOrDigit(c) || c is '_' or '.');
    // A value that starts with { would be read as a markup extension; XAML escapes it with a leading {}.
    private static string Escape(string text) => text.StartsWith('{') ? "{}" + text : text;

    private void Error(string code, string message, HtmlNode node) => Error(code, message, node.Line, node.Column);
    private void Error(string code, string message, HtmlAttribute attribute) => Error(code, message, attribute.Line, attribute.Column);
    private void Error(string code, string message, CssDeclaration declaration) =>
        _diagnostics.Add(new FormaDiagnostic(code, FormaDiagnosticSeverity.Error, message, new FormaSourceLocation(declaration.Path.Length > 0 ? declaration.Path : _path, declaration.Line, declaration.Column)));
    private void Error(string code, string message, CssRule rule) =>
        _diagnostics.Add(new FormaDiagnostic(code, FormaDiagnosticSeverity.Error, message, new FormaSourceLocation(rule.Path.Length > 0 ? rule.Path : _path, rule.Line, rule.Column)));
    private void Error(string code, string message, int line, int column) =>
        _diagnostics.Add(new FormaDiagnostic(code, FormaDiagnosticSeverity.Error, message, new FormaSourceLocation(_path, line, column)));

    // ---------------------------------------------------------------- writer

    private sealed class Writer
    {
        private readonly FormaHtmlSourceMap _map;
        private readonly Dictionary<string, string> _namespaces;
        private readonly StringBuilder _out = new();
        private int _line = 1;

        public Writer(FormaHtmlSourceMap map, Dictionary<string, string> namespaces)
        {
            _map = map;
            _namespaces = namespaces;
        }

        private const string XnaNamespace = "clr-namespace:Microsoft.Xna.Framework;assembly=MonoGame.Framework";
        private string? _defaultPath;

        private void Namespaces(XNode root, IEnumerable<XNode> entries)
        {
            root.Attrs.Insert(0, new Attr("xmlns", "https://forma.dev/xaml", root.Line, root.Column));
            root.Attrs.Insert(1, new Attr("xmlns:x", "http://schemas.microsoft.com/winfx/2006/xaml", root.Line, root.Column));
            var position = 2;
            foreach (var pair in _namespaces) root.Attrs.Insert(position++, new Attr("xmlns:" + pair.Key, pair.Value, root.Line, root.Column));
            if (entries.Any(e => e.Type.StartsWith("xna:", StringComparison.Ordinal)) && !_namespaces.ContainsKey("xna"))
                root.Attrs.Insert(position, new Attr("xmlns:xna", XnaNamespace, root.Line, root.Column));
        }

        public string Write(XNode root, List<XNode> styles, IReadOnlyList<(string Text, int Line, int Column)> raw)
        {
            Namespaces(root, styles);
            if (styles.Count > 0 || raw.Count > 0)
            {
                var resources = new XNode { Type = root.Type + ".Resources", Line = root.Line, Column = root.Column };
                var dictionary = new XNode { Type = "ResourceDictionary", Line = root.Line, Column = root.Column };
                dictionary.Children.AddRange(styles);
                dictionary.RawBlocks = raw;
                resources.Children.Add(dictionary);
                root.PropertyElements.Insert(0, (resources.Type, resources));
            }

            WriteNode(root, 0);
            return _out.ToString();
        }

        public string WriteDictionary(List<XNode> entries, string sheetPath)
        {
            _defaultPath = sheetPath;
            var root = new XNode { Type = "ResourceDictionary", Line = 1, Column = 1, Path = sheetPath };
            root.Children.AddRange(entries);
            Namespaces(root, entries);
            WriteNode(root, 0);
            return _out.ToString();
        }

        private void Line(string text, int indent, int srcLine, int srcColumn, string? path = null)
        {
            _out.Append(' ', indent * 4).Append(text).Append('\n');
            if (srcLine > 0) _map.Add(_line, srcLine, srcColumn, path ?? _defaultPath);
            _line++;
        }

        private void WriteNode(XNode node, int indent)
        {
            var children = node.Children;
            var hasBody = children.Count > 0 || node.PropertyElements.Count > 0 || (node.RawBlocks?.Count ?? 0) > 0;
            if (node.Text != null)
            {
                var head = "<" + node.Type + string.Concat(node.Attrs.Select(a => $" {a.Name}=\"{EscapeAttr(a.Value)}\""));
                Line(head + ">" + EscapeText(node.Text) + "</" + node.Type + ">", indent, node.Line, node.Column, node.Path);
                return;
            }

            Line("<" + node.Type, indent, node.Line, node.Column, node.Path);
            for (var i = 0; i < node.Attrs.Count; i++)
            {
                var attr = node.Attrs[i];
                var last = i == node.Attrs.Count - 1;
                var text = $"{attr.Name}=\"{EscapeAttr(attr.Value)}\"" + (last ? (hasBody ? ">" : " />") : string.Empty);
                Line(text, indent + 1, attr.Line, attr.Column, attr.Path ?? node.Path);
            }

            if (node.Attrs.Count == 0) { _out.Length -= 1; _out.Append(hasBody ? ">\n" : " />\n"); }
            if (!hasBody) return;
            foreach (var (name, value) in node.PropertyElements)
            {
                Line("<" + name + ">", indent + 1, value.Line, value.Column, value.Path);
                if (value.Type == name || value.Type.EndsWith(".Resources", StringComparison.Ordinal))
                    foreach (var child in value.Children) WriteNode(child, indent + 2);
                else
                    WriteNode(value, indent + 2);
                Line("</" + name + ">", indent + 1, 0, 0);
            }

            foreach (var child in children) WriteNode(child, indent + 1);
            if (node.RawBlocks != null)
                foreach (var (text, line, column) in node.RawBlocks)
                {
                    var lines = text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
                    var margin = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
                    for (var k = 0; k < lines.Length; k++)
                    {
                        if (lines[k].Trim().Length == 0) continue;
                        Line(lines[k].Substring(Math.Min(margin, lines[k].Length)).TrimEnd(), indent + 1, line + k, 1);
                    }
                }

            Line("</" + node.Type + ">", indent, 0, 0);
        }

        private static string EscapeText(string value) =>
            value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);

        private static string EscapeAttr(string value) =>
            value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);
    }
}
