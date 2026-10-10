// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text.RegularExpressions;
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
    /// <summary>Whether generated styles carry their source location (theme.fcss:12) for the inspector. Debug builds only; release artifacts omit it.</summary>
    public bool EmitOrigins { get; set; } = true;
    /// <summary>Whether an <c>f:Property</c> that has an HTML or CSS spelling gets an <c>FHTML3001</c> info diagnostic naming it. Off by default so a build is unchanged; <c>forma-xaml validate</c> turns it on.</summary>
    public bool SuggestHtmlSpellings { get; set; }
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
        ["input"] = "LineEdit", ["div"] = "Control", ["f-border"] = "Border", ["f-group-box"] = "GroupBox", ["f-scroll"] = "ScrollContainer", ["f-hbox"] = "HBoxContainer", ["f-vbox"] = "VBoxContainer",
        ["select"] = "OptionButton", ["li"] = "ListBoxItem", ["tr"] = "DataGridRow", ["textarea"] = "TextEdit", ["ul"] = "ItemsControl", ["ol"] = "ItemsControl", ["table"] = "DataGrid",
        ["f-color-rect"] = "ColorRect", ["progress"] = "ProgressBar", ["details"] = "FoldableContainer", ["hr"] = "ColorRect", ["f-tab-container"] = "TabContainer",
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
        public string Key => Name.Substring(2);
        public string? Theme;
        public string EntryKey(string suffix) => Theme == null ? Key + suffix : Key + suffix + "@" + Theme;
    }

    private readonly Dictionary<string, Token> _tokens = new(StringComparer.Ordinal);

    private readonly FormaHtmlProject? _project;
    private readonly List<FormaFcssSheet> _sheets = new();
    private readonly List<(string Text, int Line, int Column)> _rawResources = new();
    private readonly List<HtmlNode> _templates = new();
    private readonly Dictionary<string, string> _elements = new(StringComparer.Ordinal);
    private HashSet<string>? _i18nKeys;
    private string? _templateFor;
    private readonly List<XNode> _itemResources = new();
    private int _itemTemplateCount;

    public FormaHtmlConverter(string path, FormaHtmlProject? project = null)
    {
        _path = path;
        _project = project;
    }

    public static FormaHtmlResult Convert(string html, string path, FormaHtmlProject? project = null) => new FormaHtmlConverter(path, project).Run(html);

    /// <summary>
    /// Lowers an HTML-flavored CSS selector (<c>button.trace-button:hover</c>, <c>ul.list li:selected</c>, <c>[data-state=open]</c>) to the
    /// selector Forma's style engine and <c>StyleQuery</c> understand. Returns false with a message and help line when the selector is
    /// outside the dialect.
    /// </summary>
    public static bool TryLowerSelector(string htmlSelector, out string formaSelector, out string error)
    {
        var converter = new FormaHtmlConverter("selector", null);
        var rule = new CssRule { Selector = htmlSelector, Path = "selector", Line = 1, Column = 1 };
        var lowered = converter.LowerSelector(rule, out _);
        formaSelector = lowered ?? string.Empty;
        error = string.Join(" ", WithHelp(converter._diagnostics).Select(d => d.Message));
        return lowered != null;
    }

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
            if (node.Name is "style" or "link" || (node.Name == "template" && node.Attr("for") != null)) continue;
            if (root != null) { Error(FormaHtmlDiagnosticCodes.Structure, "A view has exactly one root element.", node); continue; }
            root = new XNode { Line = node.Line, Column = node.Column };
            ConvertElement(node, root, parent: null);
        }

        if (root == null)
        {
            if (_diagnostics.Count == 0) _diagnostics.Add(new FormaDiagnostic(FormaHtmlDiagnosticCodes.Structure, FormaDiagnosticSeverity.Error, "The file has no root element.", new FormaSourceLocation(_path, 1, 1)));
            return new FormaHtmlResult(string.Empty, WithHelp(_diagnostics), new FormaHtmlSourceMap());
        }

        var map = new FormaHtmlSourceMap();
        foreach (var sheet in _sheets) map.Dependencies.Add(sheet.Path);
        var resources = BuildResources(_sheets.SelectMany(sheet => sheet.Rules).Concat(_rules).ToList(), _tokens.Values);
        var writer = new Writer(map, _namespaces);
        var xaml = writer.Write(root, resources, _rawResources);
        return new FormaHtmlResult(xaml, WithHelp(_diagnostics), map);
    }

    internal FormaHtmlResult RunSheet(FormaFcssSheet sheet)
    {
        _diagnostics.AddRange(sheet.Diagnostics);
        if (!sheet.Exists)
        {
            _diagnostics.Add(new FormaDiagnostic(FormaHtmlDiagnosticCodes.LinkTarget, FormaDiagnosticSeverity.Error, $"Stylesheet '{sheet.Path}' was not found.", new FormaSourceLocation(sheet.Path, 1, 1)));
            return new FormaHtmlResult(string.Empty, WithHelp(_diagnostics), new FormaHtmlSourceMap());
        }

        CollectTokens(sheet.Rules);
        var map = new FormaHtmlSourceMap();
        var entries = BuildResources(sheet.Rules, _tokens.Values);
        var xaml = new Writer(map, _namespaces).WriteDictionary(entries, sheet.Path);
        return new FormaHtmlResult(xaml, WithHelp(_diagnostics), map);
    }

    // :root declares tokens. :root[data-theme="dark"] and :root inside @media (prefers-color-scheme: dark) declare a theme override of the
    // same tokens, emitted as resources named token@theme that ThemeResources.Apply copies over the base keys at runtime.
    private static bool IsRootRule(CssRule rule, out string? theme)
    {
        theme = null;
        if (rule.Selector == ":root" && rule.Media == null) return true;
        if (rule.Selector == ":root" && rule.Media is { } media && media.Replace(" ", string.Empty).StartsWith("(prefers-color-scheme:", StringComparison.Ordinal))
        {
            theme = media.Replace(" ", string.Empty).Substring("(prefers-color-scheme:".Length).TrimEnd(')');
            return theme is "dark" or "light";
        }

        var match = Regex.Match(rule.Selector, "^:root\\[data-theme=[\"']?([A-Za-z0-9_-]+)[\"']?\\]$");
        if (match.Success) { theme = match.Groups[1].Value; return true; }
        return false;
    }

    private readonly List<Token> _themeTokens = new();

    private void CollectTokens(IEnumerable<CssRule> rules)
    {
        foreach (var rule in rules)
        {
            if (!IsRootRule(rule, out var theme)) continue;
            foreach (var d in rule.Declarations.Where(d => d.Name.StartsWith("--", StringComparison.Ordinal)))
            {
                var value = d.Value.Trim();
                var token = new Token { Name = d.Name, Value = value, Path = d.Path.Length > 0 ? d.Path : _path, Line = d.Line, Column = d.Column, Theme = theme };
                token.Kind = value.StartsWith('#') ? "color" : IsSingleLength(value) ? "single" : "compound";
                if (theme == null) _tokens[d.Name] = token; // a later definition, such as the view's own, shadows an earlier one
                else
                {
                    _themeTokens.RemoveAll(t => t.Name == d.Name && t.Theme == theme);
                    _themeTokens.Add(token);
                }
            }
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
        if (node.Name == "template" && node.Attr("for") != null) { _templates.Add(node); return; }
        if (node.Name == "f-resources") { _rawResources.Add((node.Children.FirstOrDefault()?.Text ?? string.Empty, node.Line, node.Column)); return; }
        foreach (var child in node.Children) PreRead(child);
    }

    private void ReadMeta(HtmlNode node)
    {
        var name = node.Attr("name");
        var content = node.Attr("content") ?? string.Empty;
        if (name == "f-i18n-keys")
        {
            if (_project == null) { Error(FormaHtmlDiagnosticCodes.Structure, "<meta name=\"f-i18n-keys\"> needs a project to resolve the key file.", node); return; }
            var keyFile = Path.GetFullPath(Path.Combine(_project.BaseDirectory, content.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(keyFile)) { Error(FormaHtmlDiagnosticCodes.LinkTarget, $"Localization key file '{content}' was not found. Help: the path is relative to the project directory.", node); return; }
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(keyFile), new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
                _i18nKeys = document.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            }
            catch (System.Text.Json.JsonException exception) { Error(FormaHtmlDiagnosticCodes.InvalidValue, $"Localization key file '{content}' is not valid JSON: {exception.Message}", node); }
            return;
        }

        if (name == "f-element")
        {
            var eq2 = content.IndexOf('=');
            if (eq2 <= 0 || !content.Substring(0, eq2).Contains('-')) { Error(FormaHtmlDiagnosticCodes.InvalidValue, "f-element content must be 'tag-name=prefix:Type' with a hyphenated tag name.", node); return; }
            _elements[content.Substring(0, eq2).Trim()] = content.Substring(eq2 + 1).Trim();
            return;
        }

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
        // CSS box semantics: padding, border and background on a flex or grid container belong to a box around it, so the container is
        // wrapped in a Border that takes the decoration, sizing and margin, and the inner container keeps the layout properties.
        if (declared.TryGetValue("display", out var layoutDisplay) && layoutDisplay.Value is "flex" or "grid" &&
            declared.Keys.Any(k => k is "padding" or "border-width" or "border-radius" or "background-color" or "border-color" or "box-shadow" or "background"))
        {
            var innerNames = new HashSet<string>(StringComparer.Ordinal) { "display", "flex-direction", "gap", "justify-content", "align-items", "flex-wrap", "grid-template-columns", "grid-template-rows" };
            var outer = new HtmlNode { Name = "div", Line = element.Line, Column = element.Column };
            foreach (var attribute in element.Attributes.Where(a => a.Name != "style")) outer.Attributes.Add(attribute);
            var styleAttr = element.Attributes.First(a => a.Name == "style");
            outer.Attributes.Add(new HtmlAttribute("style", string.Join("; ", inline.Where(d => !innerNames.Contains(d.Name)).Select(d => d.Name + ": " + d.Value)), styleAttr.Line, styleAttr.Column));
            var innerNode = new HtmlNode { Name = "div", Line = element.Line, Column = element.Column };
            innerNode.Attributes.Add(new HtmlAttribute("style", string.Join("; ", inline.Where(d => innerNames.Contains(d.Name)).Select(d => d.Name + ": " + d.Value)), styleAttr.Line, styleAttr.Column));
            innerNode.Children.AddRange(element.Children);
            outer.Children.Add(innerNode);
            ConvertElement(outer, target, parent);
            return;
        }

        var parentDirection = parent == null ? null : DirectionOf(parent);
        var type = ResolveType(element, declared);
        target.Type = type;
        target.Line = element.Line;
        target.Column = element.Column;

        foreach (var attr in element.Attributes) ApplyAttribute(element, attr, target, type);
        if (element.Name == "slot" && _templateFor == "Button")
        {
            target.Attrs.Insert(0, new Attr("x:Name", "PART_ButtonText", element.Line, element.Column));
            target.Attrs.Add(new Attr("Text", "{Binding Text, RelativeSource=TemplatedParent}", element.Line, element.Column));
            if (!target.Attrs.Any(x => x.Name == "VerticalAlignment")) target.Attrs.Add(new Attr("VerticalAlignment", "Center", element.Line, element.Column));
        }
        XNode? itemsPanel = null;
        if (type is "ItemsControl" or "ListBox")
        {
            itemsPanel = BuildItemsPanel(element, inline);
            inline = inline.Where(d => d.Name is not ("display" or "flex-direction" or "gap")).ToList();
        }

        ApplyDeclarations(inline, type, target, parentDirection, element);
        ApplyElementSpecifics(element, target, type);

        // Forma boxes give a child that fills its slot the whole rectangle, so a fixed width in a column (or height in a row) only holds when
        // the child does not fill: it is anchored at the start of the cross axis, or where align-self says.
        if (declared.ContainsKey("width") && parentDirection != "row" && !target.Attrs.Any(a => a.Name == "HorizontalSizeFlags"))
        {
            var anchor = declared.TryGetValue("align-self", out var selfAlign) ? selfAlign.Value : "start";
            target.Attrs.Add(new Attr("HorizontalSizeFlags", anchor switch { "center" => "ShrinkCenter", "end" or "flex-end" => "ShrinkEnd", _ => "ShrinkBegin" }, element.Line, element.Column));
        }

        if (declared.ContainsKey("height") && parentDirection == "row" && !target.Attrs.Any(a => a.Name == "VerticalSizeFlags"))
        {
            var anchor = declared.TryGetValue("align-self", out var selfAlign) ? selfAlign.Value : "start";
            target.Attrs.Add(new Attr("VerticalSizeFlags", anchor switch { "center" => "ShrinkCenter", "end" or "flex-end" => "ShrinkEnd", _ => "ShrinkBegin" }, element.Line, element.Column));
        }

        var children = element.Children.Where(c => !(c.IsText && string.IsNullOrWhiteSpace(c.Text))).ToList();
        if (element.Name is "button" or "span" or "p" or "label" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
        {
            var text = string.Join(" ", children.Where(c => c.IsText).Select(c => c.Text));
            if (text.Length == 0 && element.Attr("data-i18n") is { } i18nKey) text = i18nKey;
            if (text.Length > 0 && !target.Attrs.Any(a => a.Name == "Text")) target.Attrs.Add(new Attr("Text", Escape(text), element.Line, element.Column));
            foreach (var nested in children.Where(c => !c.IsText))
                Error(FormaHtmlDiagnosticCodes.Structure, $"<{element.Name}> holds text only; nested <{nested.Name}> is not allowed.", nested);
            return;
        }

        if (type is "ItemsControl" or "ListBox")
        {
            ConvertItems(element, target, children, itemsPanel);
            return;
        }

        if (type == "DataGrid")
        {
            ConvertDataGrid(element, target, children);
            return;
        }

        if (type == "FoldableContainer")
        {
            // <details> is collapsed unless it has the open attribute, as in a browser.
            if (element.Attr("open") == null) target.Attrs.Add(new Attr("Folded", "True", element.Line, element.Column));
            var summary = children.FirstOrDefault(c => !c.IsText && c.Name == "summary");
            var title = summary == null ? string.Empty : string.Join(" ", summary.Children.Where(c => c.IsText).Select(c => c.Text));
            if (title.Length > 0) target.Attrs.Add(new Attr("Title", Escape(title), summary!.Line, summary.Column));
            foreach (var child in children.Where(c => c != summary))
            {
                if (child.IsText) { Error(FormaHtmlDiagnosticCodes.Structure, "Text directly inside <details> has no Forma equivalent. Help: wrap it in <span>.", child); continue; }
                var node = new XNode();
                ConvertElement(child, node, target);
                if (node.Type.Length > 0) target.Children.Add(node);
            }

            return;
        }

        if (type == "TabContainer")
        {
            foreach (var panel in children.Where(c => !c.IsText && c.Attr("role") == "tabpanel"))
            {
                var tab = new XNode();
                ConvertElement(panel, tab, target);
                if (tab.Type.Length > 0) target.Children.Add(tab);
            }

            return;
        }

        if (element.Name == "dialog")
        {
            ConvertDialog(element, target, children);
            return;
        }

        if (type == "GridPanel")
        {
            ConvertGrid(element, target, inline, children);
            return;
        }

        var alignItems = declared.TryGetValue("align-items", out var ai) ? ai : null;
        foreach (var child in children)
        {
            if (child.IsText)
            {
                Error(FormaHtmlDiagnosticCodes.Structure, $"Text directly inside <{element.Name}> has no Forma equivalent; wrap it in <span>.", child);
                continue;
            }

            if (child.Name is "style" or "f-resources" || (child.Name == "template" && child.Attr("for") != null)) continue;
            var node = new XNode();
            ConvertElement(child, node, target);
            if (node.Type.Length > 0)
            {
                if (alignItems != null && DirectionOf(target) is { } flexDirection)
                {
                    // align-items is the default cross-axis alignment of the children; a child's own align-self wins.
                    var crossProperty = flexDirection == "row" ? "VerticalAlignment" : "HorizontalAlignment";
                    if (!node.Attrs.Any(a => a.Name == crossProperty))
                    {
                        var aligned = alignItems.Value switch
                        {
                            "flex-start" or "start" => flexDirection == "row" ? "Top" : "Left",
                            "center" => "Center",
                            "flex-end" or "end" => flexDirection == "row" ? "Bottom" : "Right",
                            "stretch" => "Fill",
                            _ => Invalid(alignItems, "align-items supports start, center, end and stretch."),
                        };
                        if (aligned.Length > 0) node.Attrs.Add(new Attr(crossProperty, aligned, alignItems.Line, alignItems.Column));
                    }
                }

                target.Children.Add(node);
            }
        }

        if (type == "Border" && target.Children.Count > 1)
            Error(FormaHtmlDiagnosticCodes.Structure, "An element with padding, border or background lowers to a Border, which holds one child; wrap the children in a flex container.", element);
    }

    private XNode? BuildItemsPanel(HtmlNode element, List<CssDeclaration> inline)
    {
        var display = inline.FirstOrDefault(d => d.Name == "display");
        var gap = inline.FirstOrDefault(d => d.Name == "gap");
        if (display == null && gap == null) return null;
        var direction = inline.FirstOrDefault(d => d.Name == "flex-direction")?.Value ?? "column";
        var panel = new XNode { Type = "ItemsPanelTemplate", Line = element.Line, Column = element.Column };
        var key = (element.Attr("id") ?? "items" + (++_itemTemplateCount)) + "-panel";
        panel.Attrs.Add(new Attr("x:Key", key, element.Line, element.Column));
        var box = new XNode { Type = direction == "row" ? "HBoxContainer" : "VBoxContainer", Line = element.Line, Column = element.Column };
        if (gap != null) box.Attrs.Add(new Attr("Separation", Px(ResolveVar(gap.Value, gap), gap), gap.Line, gap.Column));
        panel.Children.Add(box);
        _itemResources.Add(panel);
        return panel;
    }

    // The item template of a bound list or grid: inline <template data-type=...> content, or <template src="Row.fhtml" data-type=...>.
    private XNode? BuildItemTemplate(HtmlNode owner, HtmlNode template, string keyHint)
    {
        var dataType = template.Attr("data-type");
        if (string.IsNullOrWhiteSpace(dataType)) { Error(FormaHtmlDiagnosticCodes.Structure, "An item <template> needs data-type. Help: <template data-type=\"local:RowViewModel\">.", template); return null; }
        var node = new XNode { Type = "DataTemplate", Line = template.Line, Column = template.Column };
        node.Attrs.Add(new Attr("x:Key", keyHint, template.Line, template.Column));
        node.Attrs.Add(new Attr("x:DataType", dataType, template.Line, template.Column));
        var row = new XNode();
        if (template.Attr("src") is { } src)
        {
            if (_project == null) { Error(FormaHtmlDiagnosticCodes.Structure, "<template src> needs a project to resolve files.", template); return null; }
            var resolved = FormaHtmlProject.Resolve(_path, src);
            var full = resolved == null ? null : System.IO.Path.Combine(_project.BaseDirectory, resolved.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (full == null || !File.Exists(full)) { Error(FormaHtmlDiagnosticCodes.LinkTarget, $"Row view '{src}' was not found. Help: the path is relative to the linking file.", template); return null; }
            var dataClass = new Regex("data-class=\"([^\"]+)\"").Match(File.ReadAllText(full)).Groups[1].Value;
            if (dataClass.Length == 0) { Error(FormaHtmlDiagnosticCodes.Structure, $"Row view '{src}' declares no data-class. Help: add data-class=\"Namespace.RowView\" to its root element.", template); return null; }
            var dot = dataClass.LastIndexOf('.');
            var rowNamespace = dot < 0 ? string.Empty : dataClass.Substring(0, dot);
            var rowPrefix = _namespaces.FirstOrDefault(pair => pair.Value == "clr-namespace:" + rowNamespace || pair.Value.StartsWith("clr-namespace:" + rowNamespace + ";", StringComparison.Ordinal)).Key;
            if (rowPrefix == null && rowNamespace.Length > 0) { rowPrefix = "row" + _namespaces.Count; _namespaces[rowPrefix] = "clr-namespace:" + rowNamespace; }
            row.Type = rowPrefix == null ? dataClass : rowPrefix + ":" + dataClass.Substring(dot + 1);
            row.Line = template.Line;
            row.Column = template.Column;
            row.Attrs.Clear();
            node.Children.Add(row);
            _rowViewTypes.Add(dataClass);
            return node;
        }

        var content = template.Children.Where(c => !(c.IsText && string.IsNullOrWhiteSpace(c.Text))).ToList();
        if (content.Count != 1 || content[0].IsText) { Error(FormaHtmlDiagnosticCodes.Structure, "An item <template> has exactly one root element. Help: wrap the row in a <div>.", template); return null; }
        ConvertElement(content[0], row, parent: null);
        if (row.Type.Length > 0) node.Children.Add(row);
        return node;
    }

    private readonly HashSet<string> _rowViewTypes = new(StringComparer.Ordinal);

    private void ConvertItems(HtmlNode element, XNode target, List<HtmlNode> children, XNode? panel)
    {
        var template = children.FirstOrDefault(c => !c.IsText && c.Name == "template");
        foreach (var other in children.Where(c => c != template)) Error(FormaHtmlDiagnosticCodes.Structure, "A bound list holds only its item <template>. Help: remove static items or drop bind:items.", other);
        if (template == null) { Error(FormaHtmlDiagnosticCodes.Structure, "A list with bind:items needs an item <template data-type=\"…\">.", element); return; }
        var key = (element.Attr("id") ?? "items" + (++_itemTemplateCount)) + "-item";
        var item = BuildItemTemplate(element, template, key);
        if (item == null) return;
        _itemResources.Add(item);
        target.Attrs.Add(new Attr("ItemTemplate", "{StaticResource " + key + "}", template.Line, template.Column));
        if (panel != null) target.Attrs.Add(new Attr("ItemsPanel", "{StaticResource " + panel.Attrs.First(a => a.Name == "x:Key").Value + "}", element.Line, element.Column));
    }

    private void ConvertDataGrid(HtmlNode element, XNode target, List<HtmlNode> children)
    {
        var head = children.FirstOrDefault(c => !c.IsText && c.Name == "thead");
        var body = children.FirstOrDefault(c => !c.IsText && c.Name == "tbody");
        var headers = head?.Children.FirstOrDefault(c => !c.IsText && c.Name == "tr")?.Children.Where(c => !c.IsText && c.Name == "th").ToList() ?? new List<HtmlNode>();
        var template = body?.Children.FirstOrDefault(c => !c.IsText && c.Name == "template");
        if (template == null) { Error(FormaHtmlDiagnosticCodes.Structure, "A table with bind:items needs <tbody><template data-type=\"…\"><tr>…</tr></template></tbody>.", element); return; }
        var row = template.Children.FirstOrDefault(c => !c.IsText && c.Name == "tr");
        if (row == null) { Error(FormaHtmlDiagnosticCodes.Structure, "The row <template> of a data grid holds one <tr> of <td> cells.", template); return; }
        var dataType = template.Attr("data-type");
        if (string.IsNullOrWhiteSpace(dataType)) { Error(FormaHtmlDiagnosticCodes.Structure, "A row <template> needs data-type. Help: <template data-type=\"local:RowViewModel\">.", template); return; }
        var columns = new XNode { Type = "DataGrid.Columns", Line = element.Line, Column = element.Column };
        var cells = row.Children.Where(c => !c.IsText && c.Name == "td").ToList();
        for (var index = 0; index < cells.Count; index++)
        {
            var header = index < headers.Count ? headers[index] : null;
            var column = new XNode { Type = "DataGridTemplateColumn", Line = cells[index].Line, Column = cells[index].Column };
            column.Attrs.Add(new Attr("Width", header?.Attr("width") ?? "*", header?.Line ?? cells[index].Line, header?.Column ?? cells[index].Column));
            if (header?.Attr("data-sort-by") != null) Error(FormaHtmlDiagnosticCodes.RejectedConstruct, "Column sorting needs a typed sort binding, which the dialect does not support yet. Help: sort the items source in the view model.", header);
            column.Attrs.Add(new Attr("CanUserSort", "False", cells[index].Line, cells[index].Column));
            var headerText = header == null ? string.Empty : string.Join(" ", header.Children.Where(c => c.IsText).Select(c => c.Text));
            if (headerText.Length > 0) column.Attrs.Add(new Attr("Header", Escape(headerText), header!.Line, header.Column));
            var cellTemplate = new XNode { Type = "DataTemplate", Line = cells[index].Line, Column = cells[index].Column };
            cellTemplate.Attrs.Add(new Attr("x:DataType", dataType, cells[index].Line, cells[index].Column));
            var cell = new XNode();
            ConvertCell(cells[index], cell, parent: null);
            if (cell.Type.Length > 0) cellTemplate.Children.Add(cell);
            column.PropertyElements.Add(("DataGridColumn.CellTemplate", cellTemplate));
            columns.Children.Add(column);
        }

        target.PropertyElements.Add((columns.Type, columns));
    }

    // <dialog> is a modal surface: an optional backdrop (backdrop="class") under the panel, both shown by the same visibility binding.
    private void ConvertDialog(HtmlNode element, XNode target, List<HtmlNode> children)
    {
        target.Type = "Container";
        target.Attrs.Clear();
        if (element.Attr("backdrop") is { } backdropClass)
        {
            var backdrop = new XNode { Type = "Border", Line = element.Line, Column = element.Column };
            backdrop.Attrs.Add(new Attr("Classes", backdropClass, element.Line, element.Column));
            backdrop.Attrs.Add(new Attr("MouseFilter", "Stop", element.Line, element.Column));
            if (element.Attr("bind:visible") is { } v) backdrop.Attrs.Add(new Attr("Visible", BindingText(v, element.Attributes.First(a => a.Name == "bind:visible")), element.Line, element.Column));
            target.Children.Add(backdrop);
        }

        var panel = new XNode();
        var inner = new HtmlNode { Name = "div", Line = element.Line, Column = element.Column };
        foreach (var attribute in element.Attributes.Where(a => a.Name is not ("backdrop" or "open"))) inner.Attributes.Add(attribute);
        inner.Children.AddRange(children);
        ConvertElement(inner, panel, parent: null);
        if (panel.Type.Length > 0) target.Children.Add(panel);
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
    private void ConvertCell(HtmlNode cell, XNode target, XNode? parent)
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
        if (_elements.TryGetValue(name, out var custom)) return custom;
        if (TextElements.TryGetValue(name, out var text)) return text;
        switch (name)
        {
            case "button": return "Button";
            case "f-border": return "Border";
            case "f-scroll": return "ScrollContainer";
            case "f-group-box": return "GroupBox";
            case "progress": return "ProgressBar";
            case "details": return "FoldableContainer";
            case "hr": return "ColorRect";
            case "f-color-rect": return "ColorRect";
            case "slot": return _templateFor == "Button" ? "TextBlock" : "ContentPresenter";
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
            case "img":
                if (element.Attr("src") is not { } imgSrc || !imgSrc.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                    return Reject(element, "<img> needs src=\"file.svg\": Forma draws SVG images through its asset pipeline. Help: convert the image to SVG, or use f-control type=\"prefix:Type\" for an application image control.");
                return "Image";
            case "svg": return Reject(element, "Inline <svg> is not supported. Help: save the drawing as a .svg file and reference it with <img src=\"icon.svg\" alt=\"…\">.");
        }

        if (name is "ul" or "ol" && element.Attr("bind:items") != null) return element.Attributes.Any(a => a.Name == "selectable") ? "ListBox" : "ItemsControl";
        if (name == "table" && element.Attr("bind:items") != null) return "DataGrid";
        if (name == "div" && element.Attr("role") == "tablist") return "TabContainer";
        if (name == "dialog") return "Container";
        if (!BoxElements.Contains(name) && name != "dialog" && name != "table" && name != "tr" && name != "td" && name != "th" && name != "tbody" && name != "thead")
            return Reject(element, name.Contains('-') ? $"<{name}> is not a registered element. Help: register it with <meta name=\"f-element\" content=\"{name}=prefix:Type\"> or use <f-control type=\"prefix:Type\">." : $"<{name}> is not part of the dialect. Help: see the support matrix for the nearest supported element.", FormaHtmlDiagnosticCodes.UnknownElement);

        if (name is "table" or "tbody" or "thead") return "GridPanel";
        if (name is "tr") return "Container";
        if (name is "td" or "th") return "Container";

        if (name == "div" && !element.Children.Any(c => !c.IsText || !string.IsNullOrWhiteSpace(c.Text)) && declared.ContainsKey("background-color") &&
            declared.Keys.All(k => k is "background-color" or "min-width" or "min-height" or "opacity" or "vertical-align" or "align-self" or "flex-grow" or "pointer-events")) return "ColorRect";
        var display = declared.TryGetValue("display", out var d) ? d.Value : null;
        var decorated = declared.Keys.Any(k => k is "padding" or "border-width" or "border-radius" or "background-color" or "border-color" or "box-shadow" or "background") && display is not ("flex" or "grid");
        if (decorated) return "Border";
        if (display == "flex")
        {
            if (declared.TryGetValue("flex-wrap", out var wrap) && wrap.Value == "wrap")
                return (declared.TryGetValue("flex-direction", out var wd) ? wd.Value : "row") == "column" ? "VFlowContainer" : "HFlowContainer";
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
            case "class": AddClass(target, value, attribute); return;
            case "part":
                if (value.Length == 0 || !value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_')) { Error(FormaHtmlDiagnosticCodes.InvalidValue, "part names a template part: part=\"chrome\". Help: use letters, digits, '-' and '_'.", attribute); return; }
                AddClass(target, "part-" + value, attribute);
                if (!target.Attrs.Any(a => a.Name == "x:Name")) Add("x:Name", "PART_" + Pascal(value));
                return;
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
            case "src" when element.Name == "img": Add("ScalableSource", value); return;
            case "alt" when element.Name == "img": Add("AccessibilityLabel", Escape(value)); return;
            case "template": Add("Template", "{StaticResource " + value + "}"); return;
            case "selectable": Add("SelectionMode", "Single"); return;
            case "role" when value is "tablist" or "tabpanel" or "tab": return;
            case "bind:items": Add("ItemsSource", BindingText(value, attribute)); return;
            case "backdrop" when element.Name == "dialog": return;
            case "open" when element.Name == "dialog": return;
            case "checked": Add("Checked", "True"); return;
            case "value" when element.Name is "input" or "progress": Add(type == "LineEdit" ? "Text" : "Value", value); return;
            case "max" when element.Name == "progress": Add("MaxValue", value); return;
            case "open" when element.Name == "details": return; // open is the absence of Folded, set below for every other <details>
            case "placeholder": Add("PlaceholderText", Escape(value)); return;
            case "min" when element.Name == "input": Add("MinValue", value); return;
            case "max" when element.Name == "input": Add("MaxValue", value); return;
            case "step" when element.Name == "input": Add("Step", value); return;
            case "alt": Add("AccessibilityLabel", Escape(value)); return;
            case "colspan" or "rowspan": return; // read by the grid placement
            case "data-activate" when element.Name is "ul" or "ol" or "table":
                if (value is "click" or "double-click") Add("ActivateOnSingleClick", value == "click" ? "True" : "False");
                else Error(FormaHtmlDiagnosticCodes.InvalidValue, "data-activate is click or double-click.", attribute);
                return;
            case "data-selection-unit" when element.Name == "table":
                if (value is "row" or "cell") Add("SelectionUnit", value == "row" ? "Row" : "Cell");
                else Error(FormaHtmlDiagnosticCodes.InvalidValue, "data-selection-unit is row or cell.", attribute);
                return;
            case "data-sortable" or "data-resizable" when element.Name == "table":
                if (value is "true" or "false") Add(name == "data-sortable" ? "CanUserSortColumns" : "CanUserResizeColumns", value == "true" ? "True" : "False");
                else Error(FormaHtmlDiagnosticCodes.InvalidValue, $"{name} is true or false.", attribute);
                return;
            case "data-flat" when element.Name is "button" or "input":
                if (value is "" or "true" or "false") Add("Flat", value == "false" ? "False" : "True");
                else Error(FormaHtmlDiagnosticCodes.InvalidValue, "data-flat is true or false, or just the attribute.", attribute);
                return;
            case "data-expand":
                if (value is "horizontal" or "vertical" or "both")
                {
                    if (value is "horizontal" or "both") Add("HorizontalSizeFlags", "Expand,Fill");
                    if (value is "vertical" or "both") Add("VerticalSizeFlags", "Expand,Fill");
                }
                else Error(FormaHtmlDiagnosticCodes.InvalidValue, "data-expand is horizontal, vertical or both.", attribute);
                return;
        }

        if (name is "lang")
        {
            var langExisting = target.Attrs.FirstOrDefault(a => a.Name == "DataSet");
            if (langExisting == null) target.Attrs.Add(new Attr("DataSet", "lang=" + value, attribute.Line, attribute.Column)); else langExisting.Value += ";lang=" + value;
            return;
        }

        if (name == "dir")
        {
            if (value is not ("ltr" or "rtl" or "auto")) { Error(FormaHtmlDiagnosticCodes.InvalidValue, "dir supports ltr, rtl and auto.", attribute); return; }
            if (value != "auto") { Add("LayoutDirection", value == "rtl" ? "RightToLeft" : "LeftToRight"); }
            var dirExisting = target.Attrs.FirstOrDefault(a => a.Name == "DataSet");
            if (dirExisting == null) target.Attrs.Add(new Attr("DataSet", "dir=" + value, attribute.Line, attribute.Column)); else dirExisting.Value += ";dir=" + value;
            return;
        }

        if (name.StartsWith("data-i18n", StringComparison.Ordinal) && _i18nKeys != null && !_i18nKeys.Contains(value))
            Error(FormaHtmlDiagnosticCodes.InvalidValue, $"Localization key '{value}' is not in the declared key file. Help: add it to the locale file, or fix the spelling (nearest: {Nearest(value)}).", attribute);

        if (name.StartsWith("data-", StringComparison.Ordinal) && name.Length > 5)
        {
            var existing = target.Attrs.FirstOrDefault(a => a.Name == "DataSet");
            if (existing == null) target.Attrs.Add(new Attr("DataSet", name.Substring(5) + "=" + value, attribute.Line, attribute.Column));
            else existing.Value += ";" + name.Substring(5) + "=" + value;
            return;
        }

        if (name.StartsWith("on", StringComparison.Ordinal) && name.Length > 2)
        {
            if (!IsIdentifier(value)) { Error(FormaHtmlDiagnosticCodes.InvalidValue, $"{name} names a code-behind handler, never an expression: {name}=\"OnSomethingPressed\".", attribute); return; }
            var evt = name switch { "onclick" => "Pressed", "onitemactivated" => "ItemActivated", "onchange" => type is "LineEdit" or "TextEdit" ? "TextChanged" : "Changed", "onfocus" => "FocusEntered", "onblur" => "FocusExited", _ => string.Empty };
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
            if (_project?.SuggestHtmlSpellings == true && FormaHtmlReference.Replacements.FirstOrDefault(r => r.FProperty.Split(" / ").Any(p => p.Split(' ')[0] == name.Substring(2))) is { } replacement)
                _diagnostics.Add(new FormaDiagnostic(FormaHtmlDiagnosticCodes.PreferHtmlSpelling, FormaDiagnosticSeverity.Info,
                    $"{name} has an HTML or CSS spelling: {replacement.Spelling}. Help: docs/html-css-support-matrix.md, \"Replacing f: properties\".", new FormaSourceLocation(_path, attribute.Line, attribute.Column)));
            return;
        }

        Error(FormaHtmlDiagnosticCodes.UnknownAttribute, $"Attribute '{name}' is not part of the dialect. Use f:{Pascal(name)}=\"…\" to set a Forma property directly.", attribute);
    }

    private string Nearest(string key) =>
        _i18nKeys!.OrderBy(k => Distance(k, key)).FirstOrDefault() ?? "none";

    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    private static void AddClass(XNode target, string value, HtmlAttribute attribute)
    {
        var existing = target.Attrs.FirstOrDefault(a => a.Name == "Classes");
        if (existing == null) target.Attrs.Add(new Attr("Classes", value, attribute.Line, attribute.Column));
        else existing.Value += " " + value;
    }

    private string BindingText(string value, HtmlAttribute attribute)
    {
        var parts = value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && parts[0].StartsWith("host.", StringComparison.Ordinal) && IsPath(parts[0]))
            return "{Binding " + parts[0].Substring(5) + ", RelativeSource=TemplatedParent}";
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

    private static string? DirectionOf(XNode node) => node.Type switch { "VBoxContainer" or "VFlowContainer" => "column", "HBoxContainer" or "HFlowContainer" => "row", _ => null };

    private void ApplyDeclarations(IReadOnlyList<CssDeclaration> declarations, string type, XNode target, string? parentDirection, HtmlNode source)
    {
        foreach (var (property, value, declaration) in Lower(declarations, type, parentDirection))
        {
            if (property == "Background.Gradient") { AddGradient(target, value, declaration); continue; }
            if (property == "RenderTransform.Spec") { AddTransform(target, value, declaration); continue; }
            target.Attrs.Add(new Attr(property, value, declaration.Line, declaration.Column));
        }
    }

    private IEnumerable<(string Property, string Value, CssDeclaration Source)> Lower(IReadOnlyList<CssDeclaration> declarations, string type, string? parentDirection)
    {
        CssDeclaration? minWidth = null, minHeight = null, maxWidth = null, maxHeight = null, width = null, height = null;
        var marginSides = new Dictionary<string, CssDeclaration>(StringComparer.Ordinal);
        var isBox = type is "VBoxContainer" or "HBoxContainer" or "VFlowContainer" or "HFlowContainer";
        foreach (var d in declarations)
        {
            if (d.Name.StartsWith("-f-", StringComparison.Ordinal))
            {
                // Escape hatch to any Forma property, like the f: attribute: -f-BackgroundColor: #40040C16 or -f-Template: {StaticResource Key}.
                var raw = TemplateFunction(d.Value) is { } templateKey ? "{StaticResource " + templateKey + "}" : ResourceFunction(d.Value) is { } resourceKey ? "{DynamicResource " + resourceKey + "}" : ResolveVar(d.Value, d);
                yield return (d.Name.Substring(3), raw, d);
                continue;
            }

            if (DynamicFor(d, isBox, type == "ColorRect") is { } dynamic)
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
                case "max-width": maxWidth = d; break;
                case "max-height": maxHeight = d; break;
                case "width": width = d; break;
                case "height": height = d; break;
                case "margin": yield return ("Margins", Thickness(value, d), d); break;
                case "margin-inline" or "margin-inline-start" or "margin-inline-end" or "padding-inline" or "padding-inline-start" or "padding-inline-end" or "inset-inline-start" or "inset-inline-end":
                    Error(FormaHtmlDiagnosticCodes.RejectedProperty, $"'{d.Name}' cannot flip at runtime: Forma thicknesses are physical. Help: use margin-left/margin-right (or padding-left/right); containers and text already mirror under dir=\"rtl\".", d);
                    break;
                case "margin-top" or "margin-right" or "margin-bottom" or "margin-left":
                    marginSides[d.Name.Substring(7)] = d;
                    break;
                case "padding":
                    yield return ("Padding", Thickness(value, d), d);
                    break;
                case "box-sizing":
                    if (value != "border-box") Error(FormaHtmlDiagnosticCodes.InvalidValue, "Forma sizes include padding and border, so only box-sizing: border-box exists. Help: remove box-sizing or use border-box.", d);
                    break;
                case "overflow":
                    if (value == "hidden") yield return ("ClipContents", "True", d);
                    else if (value is "visible") { }
                    else Error(FormaHtmlDiagnosticCodes.RejectedProperty, $"overflow: {value} is not supported. Help: wrap the content in <f-scroll> for scrolling, or use overflow: hidden to clip.", d);
                    break;
                case "justify-content":
                    if (!isBox) { Error(FormaHtmlDiagnosticCodes.RejectedProperty, "justify-content applies to display: flex containers.", d); break; }
                    yield return ("Alignment", value switch { "flex-start" or "start" => "Begin", "center" => "Center", "flex-end" or "end" => "End", _ => Invalid(d, "justify-content supports start, center and end. Help: for space-between use flex-grow spacers.") }, d);
                    break;
                case "align-items":
                    if (!isBox) { Error(FormaHtmlDiagnosticCodes.RejectedProperty, "align-items applies to display: flex containers.", d); break; }
                    break; // applied to the children by ConvertElement
                case "flex-wrap":
                    if (value is not ("nowrap" or "wrap")) Error(FormaHtmlDiagnosticCodes.InvalidValue, "flex-wrap supports nowrap and wrap.", d);
                    break; // wrap changes the container type in ResolveType
                case "flex":
                    if (value is "1" or "auto" or "1 1 0%" or "1 1 auto")
                    {
                        if (parentDirection == null) { Error(FormaHtmlDiagnosticCodes.RejectedProperty, "flex needs a display: flex parent.", d); break; }
                        yield return (parentDirection == "row" ? "HorizontalSizeFlags" : "VerticalSizeFlags", "Expand", d);
                    }
                    else if (value != "0" && value != "none") Error(FormaHtmlDiagnosticCodes.InvalidValue, "flex supports 1, auto, 0 and none. Help: use flex-grow: 1 to share space.", d);
                    break;
                case "flex-shrink" or "flex-basis" or "order":
                    Error(FormaHtmlDiagnosticCodes.RejectedProperty, $"'{d.Name}' is not supported: Forma boxes never shrink below their minimum size. Help: set min-width/min-height, and reorder the markup instead of using order.", d);
                    break;
                case "border-width": yield return ("BorderThickness", Thickness(value, d), d); break;
                case "border-radius": yield return ("CornerRadius", Radius(value, d), d); break;
                case "box-shadow":
                    if (type is not ("Border" or "Control" or ""))
                    {
                        Error(FormaHtmlDiagnosticCodes.RejectedProperty, $"box-shadow is drawn by boxes only, not by a {type}. Help: put the shadow on an element with padding, border or background (a div or f-border) that wraps it.", d);
                        break;
                    }

                    yield return ("ShadowsText", BoxShadow(value, d), d);
                    break;
                case "background" or "background-image":
                    if (value.StartsWith("linear-gradient(", StringComparison.Ordinal) || value.StartsWith("radial-gradient(", StringComparison.Ordinal))
                    {
                        if (type is not ("Border" or "Control" or "")) Error(FormaHtmlDiagnosticCodes.RejectedProperty, $"A gradient background is drawn by boxes only, not by a {type}. Help: put the gradient on an element with padding, border or background (a div or f-border) that wraps it.", d);
                        else yield return ("Background.Gradient", value, d);
                    }
                    else if (value.StartsWith('#')) yield return (type == "ColorRect" ? "Color" : "Background", Color(value, d), d);
                    else Error(FormaHtmlDiagnosticCodes.RejectedProperty, $"'{d.Name}: {value}' is not supported. Help: use background-color or linear-gradient()/radial-gradient(); images use <img> or <svg>.", d);
                    break;
                case "transform": yield return ("RenderTransform.Spec", value, d); break;
                case "transform-origin": yield return ("TransformOrigin", TransformOrigin(value, d), d); break;
                case "text-shadow":
                    {
                        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 3 || !parts[^1].StartsWith('#')) { Error(FormaHtmlDiagnosticCodes.InvalidValue, "text-shadow is 'x y [blur] #color'. Help: example text-shadow: 1px 1px #000000 (blur is not drawn).", d); break; }
                        yield return ("TextShadowOffset", $"{Px(parts[0], d)},{Px(parts[1], d)}", d);
                        yield return ("TextShadowColor", Color(parts[^1], d), d);
                    }
                    break;
                case "text-transform":
                    if (value == "uppercase") yield return ("Uppercase", "True", d);
                    else if (value != "none") Error(FormaHtmlDiagnosticCodes.RejectedProperty, $"text-transform: {value} is not supported. Help: only uppercase and none exist; write the text in the wanted case.", d);
                    break;
                case "text-overflow":
                    if (value == "ellipsis") yield return ("TextOverrunBehavior", "Ellipsis", d);
                    else if (value != "clip") Error(FormaHtmlDiagnosticCodes.InvalidValue, "text-overflow supports ellipsis and clip.", d);
                    break;
                case "white-space":
                    if (value == "nowrap") yield return ("Autowrap", "False", d);
                    else if (value == "normal") yield return ("Autowrap", "True", d);
                    else Error(FormaHtmlDiagnosticCodes.RejectedProperty, $"white-space: {value} is not supported. Help: use nowrap or normal.", d);
                    break;
                case "font-style":
                    yield return ("FontStyle", value switch { "normal" => "Normal", "italic" => "Italic", "oblique" => "Oblique", _ => Invalid(d, "font-style supports normal, italic and oblique.") }, d);
                    break;
                case "font-family":
                    Error(FormaHtmlDiagnosticCodes.RejectedProperty, "font-family is not supported: faces come from the theme. Help: set -f-FontFamily: resource(Key) to a registered face, or leave the theme font.", d);
                    break;
                case "outline" or "outline-offset" or "outline-width" or "outline-color" or "mix-blend-mode" or "clip-path" or "mask" or "line-height" or "letter-spacing" or "text-decoration" or "word-spacing" or "cursor" when true:
                    Error(FormaHtmlDiagnosticCodes.RejectedProperty, $"'{d.Name}' is not supported: {UnsupportedEffect(d.Name)}", d);
                    break;
                case "border-color": yield return ("BorderBrush", Color(value, d), d); break;
                case "background-color": yield return (type == "ColorRect" ? "Color" : "Background", Color(value, d), d); break;
                case "color": yield return ("FontColor", Color(value, d), d); break;
                case "font-size": yield return ("FontSize", Px(value, d), d); break;
                case "font-weight":
                    yield return ("FontWeight", value switch { "normal" or "400" => "Normal", "bold" or "700" => "Bold", _ => Invalid(d, "font-weight supports normal, bold, 400 and 700.") }, d);
                    break;
                case "opacity": yield return ("Opacity", Number(value, d), d); break;
                case "z-index":
                    yield return ("ZIndex", int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zIndex) ? zIndex.ToString(CultureInfo.InvariantCulture) : Invalid(d, "z-index is an integer."), d);
                    break;
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
                case "position" or "float" or "filter" or "backdrop-filter":
                    Error(FormaHtmlDiagnosticCodes.RejectedProperty, $"'{d.Name}' is not supported: {RejectionReason(d.Name)}.{(RejectionReason(d.Name).Contains("Help:") ? string.Empty : " Help: see the support matrix for the nearest supported alternative.")}", d);
                    break;
                case "transition":
                    break; // lowered to style transitions in a rule
                case "animation" or "animation-name" or "animation-duration" or "animation-timing-function" or "animation-iteration-count" or "animation-direction" or "animation-fill-mode":
                    break; // lowered to a Storyboard by BuildStoryboards
                default:
                    if (d.Name.StartsWith("--", StringComparison.Ordinal)) break;
                    Error(FormaHtmlDiagnosticCodes.UnknownProperty, $"CSS property '{d.Name}' is not part of the dialect.", d);
                    break;
            }
        }

        string Len(CssDeclaration? declaration, string fallback) => declaration == null ? fallback : Px(ResolveVar(declaration.Value, declaration), declaration);
        if (marginSides.Count > 0)
            yield return ("Margins", $"{Len(marginSides.GetValueOrDefault("left"), "0")},{Len(marginSides.GetValueOrDefault("top"), "0")},{Len(marginSides.GetValueOrDefault("right"), "0")},{Len(marginSides.GetValueOrDefault("bottom"), "0")}", marginSides.Values.First());
        if (type == "ProgressBar" && (height ?? minHeight) is { } progressHeight && double.TryParse(Px(ResolveVar(progressHeight.Value, progressHeight), progressHeight).Replace("px", string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out var progressPixels) && progressPixels < 20)
            Error(FormaHtmlDiagnosticCodes.InvalidValue, "A <progress> is at least 20px high: Forma's ProgressBar has that minimum size. Help: use height: 20px or more, or draw a thin bar with an f-color-rect whose width you set.", progressHeight);
        if (minWidth != null || minHeight != null || width != null || height != null)
            yield return ("CustomMinimumSize", $"{Len(width ?? minWidth, "0")},{Len(height ?? minHeight, "0")}", (width ?? height ?? minWidth ?? minHeight)!);
        if (maxWidth != null || maxHeight != null || width != null || height != null)
            yield return ("CustomMaximumSize", $"{Len(width ?? maxWidth, "-1")},{Len(height ?? maxHeight, "-1")}", (width ?? height ?? maxWidth ?? maxHeight)!);

    }

    private static string UnsupportedEffect(string property) => property switch
    {
        "outline" or "outline-offset" or "outline-width" or "outline-color" => "the renderer has no outline. Help: use border-width and border-color; focus rings are drawn by the :focus style.",
        "mix-blend-mode" or "clip-path" or "mask" => "the renderer has no blending or masking. Help: use opacity, or overflow: hidden to clip to the box.",
        "line-height" or "letter-spacing" or "word-spacing" or "text-decoration" => "the text engine has no such control. Help: size the box with min-height and font-size instead.",
        "cursor" => "cursors are set by the control. Help: remove cursor.",
        _ => "not part of the dialect. Help: see the support matrix.",
    };

    private string Radius(string value, CssDeclaration d)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => Px(p, d)).ToArray();
        if (parts.Length is < 1 or > 4) { Error(FormaHtmlDiagnosticCodes.InvalidValue, "Expected one to four radii.", d); return "0"; }
        // CSS order is top-left, top-right, bottom-right, bottom-left, the same as Forma's CornerRadius.
        string tl = parts[0], tr = parts.Length > 1 ? parts[1] : parts[0], br = parts.Length > 2 ? parts[2] : parts[0], bl = parts.Length > 3 ? parts[3] : tr;
        return tl == tr && tr == br && br == bl ? tl : $"{tl},{tr},{br},{bl}";
    }

    private string BoxShadow(string value, CssDeclaration d)
    {
        var entries = new List<string>();
        foreach (var layer in SplitTopLevel(value, ','))
        {
            var lengths = new List<string>();
            var color = "#FF000000";
            var inset = false;
            foreach (var part in layer.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == "inset") inset = true;
                else if (part.StartsWith('#')) color = Color(part, d);
                else lengths.Add(Px(part, d));
            }

            if (lengths.Count is < 2 or > 4) { Error(FormaHtmlDiagnosticCodes.InvalidValue, "box-shadow is 'x y [blur [spread]] #color [inset]'.", d); continue; }
            while (lengths.Count < 4) lengths.Add("0");
            entries.Add(string.Join(",", lengths) + "," + color + (inset ? ",inset" : string.Empty));
        }

        return string.Join(";", entries);
    }

    private static IEnumerable<string> SplitTopLevel(string value, char separator)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '(') depth++;
            else if (value[i] == ')') depth--;
            else if (value[i] == separator && depth == 0) { yield return value.Substring(start, i - start).Trim(); start = i + 1; }
        }

        yield return value.Substring(start).Trim();
    }

    private string TransformOrigin(string value, CssDeclaration d)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        double Axis(string part, bool horizontal) => part switch
        {
            "left" or "top" => 0, "center" => 0.5, "right" or "bottom" => 1,
            var pct when pct.EndsWith('%') && double.TryParse(pct[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) => v / 100.0,
            _ => double.NaN,
        };
        var x = Axis(parts.ElementAtOrDefault(0) ?? "center", true);
        var y = Axis(parts.ElementAtOrDefault(1) ?? "center", false);
        if (double.IsNaN(x) || double.IsNaN(y)) { Error(FormaHtmlDiagnosticCodes.InvalidValue, "transform-origin supports left/center/right, top/center/bottom and percentages. Help: example transform-origin: 50% 50%.", d); return "0.5,0.5"; }
        return $"{FormatNumber(x)},{FormatNumber(y)}";
    }

    private void AddTransform(XNode target, string spec, CssDeclaration d)
    {
        var transforms = new List<XNode>();
        foreach (Match m in Regex.Matches(spec, "([a-z]+)\\(([^)]*)\\)"))
        {
            var args = m.Groups[2].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var node = new XNode { Line = d.Line, Column = d.Column };
            switch (m.Groups[1].Value)
            {
                case "translate":
                    node.Type = "TranslateTransform";
                    node.Attrs.Add(new Attr("X", Px(args.ElementAtOrDefault(0) ?? "0", d), d.Line, d.Column));
                    node.Attrs.Add(new Attr("Y", Px(args.ElementAtOrDefault(1) ?? "0", d), d.Line, d.Column));
                    break;
                case "scale":
                    node.Type = "ScaleTransform";
                    node.Attrs.Add(new Attr("ScaleX", Number(args.ElementAtOrDefault(0) ?? "1", d), d.Line, d.Column));
                    node.Attrs.Add(new Attr("ScaleY", Number(args.ElementAtOrDefault(1) ?? args.ElementAtOrDefault(0) ?? "1", d), d.Line, d.Column));
                    break;
                case "rotate":
                    node.Type = "RotateTransform";
                    node.Attrs.Add(new Attr("Angle", Number((args.ElementAtOrDefault(0) ?? "0").Replace("deg", string.Empty, StringComparison.Ordinal), d), d.Line, d.Column));
                    break;
                default:
                    Error(FormaHtmlDiagnosticCodes.RejectedProperty, $"transform function '{m.Groups[1].Value}' is not supported. Help: use translate(), scale() or rotate().", d);
                    continue;
            }

            transforms.Add(node);
        }

        if (transforms.Count == 0) { Error(FormaHtmlDiagnosticCodes.InvalidValue, $"'{spec}' is not a transform. Help: example transform: translate(4px, 0) scale(1.1) rotate(10deg).", d); return; }
        XNode value;
        if (transforms.Count == 1) value = transforms[0];
        else { value = new XNode { Type = "TransformGroup", Line = d.Line, Column = d.Column }; value.PropertyElements.Add(("TransformGroup.Children", new XNode { Type = "TransformGroup.Children", Children = transforms })); }
        target.PropertyElements.Add(("Control.RenderTransform", value));
    }

    private void AddGradient(XNode target, string spec, CssDeclaration d)
    {
        var radial = spec.StartsWith("radial-gradient(", StringComparison.Ordinal);
        var inner = spec.Substring(spec.IndexOf('(') + 1).TrimEnd(')');
        var parts = SplitTopLevel(inner, ',').ToList();
        var brush = new XNode { Type = radial ? "RadialGradientBrush" : "LinearGradientBrush", Line = d.Line, Column = d.Column };
        if (!radial)
        {
            var angle = 180.0;
            if (parts[0].EndsWith("deg", StringComparison.Ordinal) && double.TryParse(parts[0][..^3], NumberStyles.Float, CultureInfo.InvariantCulture, out var degrees)) { angle = degrees; parts.RemoveAt(0); }
            else if (parts[0].StartsWith("to ", StringComparison.Ordinal))
            {
                angle = parts[0] switch { "to top" => 0, "to right" => 90, "to bottom" => 180, "to left" => 270, _ => double.NaN };
                if (double.IsNaN(angle)) { Error(FormaHtmlDiagnosticCodes.InvalidValue, "linear-gradient direction supports to top|right|bottom|left and an angle. Help: example linear-gradient(180deg, #000000, #FFFFFF).", d); return; }
                parts.RemoveAt(0);
            }

            var radians = angle * Math.PI / 180.0;
            double vx = Math.Sin(radians), vy = -Math.Cos(radians);
            brush.Attrs.Add(new Attr("StartPoint", $"{FormatNumber(0.5 - vx / 2)},{FormatNumber(0.5 - vy / 2)}", d.Line, d.Column));
            brush.Attrs.Add(new Attr("EndPoint", $"{FormatNumber(0.5 + vx / 2)},{FormatNumber(0.5 + vy / 2)}", d.Line, d.Column));
        }
        else if (parts.Count > 0 && (parts[0] is "circle" or "ellipse" || parts[0].StartsWith("circle ", StringComparison.Ordinal))) parts.RemoveAt(0);

        var stops = new List<(string Color, double? Offset)>();
        foreach (var part in parts)
        {
            var bits = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (bits.Length == 0 || !bits[0].StartsWith('#')) { Error(FormaHtmlDiagnosticCodes.InvalidValue, $"Gradient stop '{part}' must start with a #RRGGBB color. Help: example linear-gradient(#000000, #FFFFFF 80%).", d); return; }
            double? offset = bits.Length > 1 && bits[1].EndsWith('%') && double.TryParse(bits[1][..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct) ? pct / 100.0 : null;
            stops.Add((Color(bits[0], d), offset));
        }

        if (stops.Count < 2) { Error(FormaHtmlDiagnosticCodes.InvalidValue, "A gradient needs at least two color stops.", d); return; }
        var text = string.Join(";", stops.Select((stop, index) => FormatNumber(stop.Offset ?? index / (double)(stops.Count - 1)) + ":" + stop.Color));
        brush.Attrs.Add(new Attr("StopsText", text, d.Line, d.Column));
        target.PropertyElements.Add(("Border.Background", brush));
    }

    private static string RejectionReason(string property) => property switch
    {
        "position" => "absolute and fixed positioning have no defined overlay mapping yet. Help: use display: flex or grid; for overlays use <dialog> or f-control type=\"OverlayPanel\"",
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

    private static string? TemplateFunction(string value)
    {
        value = value.Trim();
        return value.StartsWith("template(", StringComparison.Ordinal) && value.EndsWith(')') ? value.Substring(9, value.Length - 10).Trim() : null;
    }

    private static string? ResourceFunction(string value)
    {
        value = value.Trim();
        return value.StartsWith("resource(", StringComparison.Ordinal) && value.EndsWith(')') ? value.Substring(9, value.Length - 10).Trim() : null;
    }

    // Properties that can observe a resource take a live {DynamicResource}: a custom property of the matching kind, or resource(Key)
    // for a resource defined elsewhere (for example in XAML). Everything else substitutes the custom property's value at build time.
    // :focus-visible is focus reached by keyboard or gamepad: the rule becomes two, each under an input-modality condition.
    private IEnumerable<CssRule> ExpandFocusVisible(IReadOnlyList<CssRule> rules)
    {
        foreach (var rule in rules)
        {
            if (!rule.Selector.Contains(":focus-visible", StringComparison.Ordinal)) { yield return rule; continue; }
            if (rule.Media != null) { Error(FormaHtmlDiagnosticCodes.UnsupportedSelector, ":focus-visible cannot be combined with another @media condition. Help: move the rule out of the @media block; :focus-visible already depends on the input modality.", rule); continue; }
            foreach (var modality in new[] { "keyboard", "gamepad" })
            {
                var clone = new CssRule { Selector = rule.Selector.Replace(":focus-visible", ":focus", StringComparison.Ordinal), Path = rule.Path, Line = rule.Line, Column = rule.Column, Media = "(input-modality: " + modality + ")" };
                clone.Declarations.AddRange(rule.Declarations);
                yield return clone;
            }
        }
    }

    private void BuildControlTemplate(HtmlNode template, List<XNode> into)
    {
        var id = template.Attr("id");
        var target = template.Attr("for");
        var targetType = target switch { "button" => "Button", "input" or "text" => "LineEdit", "checkbox" => "CheckBox", _ => string.Empty };
        if (string.IsNullOrWhiteSpace(id)) { Error(FormaHtmlDiagnosticCodes.Structure, "A control template needs an id. Help: <template for=\"button\" id=\"my-button\">.", template); return; }
        if (targetType.Length == 0) { Error(FormaHtmlDiagnosticCodes.InvalidValue, $"<template for=\"{target}\"> names no templatable control. Help: use button, input or checkbox.", template); return; }
        var content = template.Children.Where(c => !c.IsText).ToList();
        if (content.Count != 1) { Error(FormaHtmlDiagnosticCodes.Structure, "A control template has exactly one root element.", template); return; }
        var node = new XNode { Type = "ControlTemplate", Line = template.Line, Column = template.Column };
        node.Attrs.Add(new Attr("x:Key", id, template.Line, template.Column));
        node.Attrs.Add(new Attr("TargetType", targetType, template.Line, template.Column));
        var previous = _templateFor;
        _templateFor = targetType;
        var root = new XNode();
        ConvertElement(content[0], root, parent: null);
        _templateFor = previous;
        if (root.Type.Length > 0) node.Children.Add(root);
        into.Add(node);
    }

    // @keyframes plus an animation on a #id rule lower to a Storyboard keyed by the animation name, targeting that element. Starting it is
    // code-behind: storyboard.Begin(view) with the resource named after the animation.
    private void BuildStoryboards(IReadOnlyList<CssRule> rules, List<XNode> into)
    {
        var frames = rules.Where(r => r.Keyframes != null).GroupBy(r => r.Keyframes!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var animated = new List<CssRule>();
        foreach (var source in rules.Where(r => r.Keyframes == null && !IsRootRule(r, out _) && r.Declarations.Any(d => d.Name.StartsWith("animation", StringComparison.Ordinal))))
        {
            // animation: a 1s, b 2s runs several animations; each becomes its own storyboard.
            var list = source.Declarations.FirstOrDefault(d => d.Name == "animation");
            var entries = list == null ? new List<string>() : SplitTopLevel(list.Value, ',').ToList();
            if (entries.Count < 2) { animated.Add(source); continue; }
            foreach (var entry in entries)
            {
                var clone = new CssRule { Selector = source.Selector, Path = source.Path, Line = source.Line, Column = source.Column, Media = source.Media };
                clone.Declarations.Add(new CssDeclaration("animation", entry, list!.Line, list.Column) { Path = list.Path });
                animated.Add(clone);
            }
        }

        foreach (var rule in animated.Where(r => !r.Declarations.Any(d => d.Name is "animation" or "animation-name" && d.Value.Trim() == "none")))
        {
            var longhand = rule.Declarations.Where(d => d.Name.StartsWith("animation", StringComparison.Ordinal)).ToDictionary(d => d.Name, d => d, StringComparer.Ordinal);
            string? name = null, duration = null, iteration = null, direction = null, fill = null, timing = null;
            var anchor = longhand.Values.First();
            if (longhand.TryGetValue("animation", out var shorthand))
            {
                foreach (var part in shorthand.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (part.EndsWith("ms", StringComparison.Ordinal) || (part.EndsWith('s') && double.TryParse(part[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out _))) duration = part;
                    else if (part is "infinite") iteration = part;
                    else if (part is "alternate" or "normal") direction = part;
                    else if (part is "forwards" or "both" or "none" or "backwards") fill = part;
                    else if (part is "linear" or "ease" or "ease-in" or "ease-out" or "ease-in-out") timing = part;
                    else name ??= part;
                }
            }

            if (longhand.TryGetValue("animation-name", out var n)) name = n.Value.Trim();
            if (longhand.TryGetValue("animation-duration", out var du)) duration = du.Value.Trim();
            if (longhand.TryGetValue("animation-iteration-count", out var it)) iteration = it.Value.Trim();
            if (longhand.TryGetValue("animation-direction", out var di)) direction = di.Value.Trim();
            if (longhand.TryGetValue("animation-fill-mode", out var fi)) fill = fi.Value.Trim();
            if (longhand.TryGetValue("animation-timing-function", out var ti)) timing = ti.Value.Trim();
            if (name == null || !frames.TryGetValue(name, out var keyframes)) { Error(FormaHtmlDiagnosticCodes.InvalidValue, $"animation names '{name}', which no @keyframes block defines. Help: add @keyframes {name} {{ from {{ … }} to {{ … }} }}.", anchor); continue; }
            if (!rule.Selector.StartsWith('#') || rule.Selector.IndexOfAny(new[] { ' ', '.', ':', '>', ',', '[' }) >= 0) { Error(FormaHtmlDiagnosticCodes.UnsupportedSelector, "An animation targets one named element. Help: put animation on an #id rule so the storyboard can name its target.", rule); continue; }
            if (iteration is not (null or "1" or "infinite")) { Error(FormaHtmlDiagnosticCodes.InvalidValue, "animation-iteration-count supports 1 and infinite. Help: use infinite with a stop from code-behind.", anchor); continue; }
            var seconds = ParseSeconds(duration, anchor);
            var story = new XNode { Type = "Storyboard", Line = rule.Line, Column = rule.Column, Path = rule.Path };
            story.Attrs.Add(new Attr("x:Key", name, rule.Line, rule.Column, rule.Path));
            if (direction == "alternate") story.Attrs.Add(new Attr("AutoReverse", "True", anchor.Line, anchor.Column, rule.Path));
            story.Attrs.Add(new Attr("FillBehavior", fill is "forwards" or "both" ? "HoldEnd" : "Stop", anchor.Line, anchor.Column, rule.Path));
            if (iteration == "infinite") story.Attrs.Add(new Attr("RepeatBehavior", "Forever", anchor.Line, anchor.Column, rule.Path));
            var easing = EasingFor(timing, anchor);
            var byProperty = new Dictionary<string, List<(double Offset, string Value, CssDeclaration Source)>>(StringComparer.Ordinal);
            foreach (var frame in keyframes)
            {
                var offset = frame.Selector.Trim() switch { "from" => 0.0, "to" => 1.0, var pct when pct.EndsWith('%') && double.TryParse(pct[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) => v / 100.0, _ => -1.0 };
                if (offset < 0) { Error(FormaHtmlDiagnosticCodes.InvalidValue, $"'{frame.Selector}' is not a keyframe selector. Help: use from, to or a percentage.", frame); continue; }
                foreach (var d in frame.Declarations)
                {
                    var property = d.Name == "opacity" ? "Opacity" : d.Name.StartsWith("-f-", StringComparison.Ordinal) ? d.Name.Substring(3) : null;
                    if (property == null) { Error(FormaHtmlDiagnosticCodes.RejectedProperty, $"'{d.Name}' cannot be animated by a storyboard. Help: animate opacity, or a numeric Forma property with -f-Property.", d); continue; }
                    if (!byProperty.TryGetValue(property, out var list)) byProperty[property] = list = new();
                    list.Add((offset, Number(d.Value, d), d));
                }
            }

            foreach (var (property, list) in byProperty)
            {
                var timeline = new XNode { Type = "FloatTimeline", Line = rule.Line, Column = rule.Column, Path = rule.Path };
                timeline.Attrs.Add(new Attr("TargetName", rule.Selector.Substring(1), rule.Line, rule.Column, rule.Path));
                timeline.Attrs.Add(new Attr("Property", property, rule.Line, rule.Column, rule.Path));
                foreach (var (offset, value, source) in list.OrderBy(x => x.Offset))
                {
                    var key = new XNode { Type = "KeyFrame", Line = source.Line, Column = source.Column, Path = rule.Path };
                    key.Attrs.Add(new Attr("Time", TimeSpan.FromSeconds(offset * seconds).ToString("c", CultureInfo.InvariantCulture), source.Line, source.Column, rule.Path));
                    key.Attrs.Add(new Attr("Value", value, source.Line, source.Column, rule.Path));
                    if (offset > 0 && easing != "Linear") key.Attrs.Add(new Attr("Easing", easing, source.Line, source.Column, rule.Path));
                    timeline.Children.Add(key);
                }

                story.Children.Add(timeline);
            }

            into.Add(story);
        }
    }

    private double ParseSeconds(string? value, CssDeclaration at)
    {
        if (value == null) { Error(FormaHtmlDiagnosticCodes.InvalidValue, "An animation needs a duration. Help: animation-duration: 200ms.", at); return 0; }
        if (value.EndsWith("ms", StringComparison.Ordinal) && double.TryParse(value[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)) return ms / 1000.0;
        if (value.EndsWith('s') && double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) return s;
        Error(FormaHtmlDiagnosticCodes.InvalidValue, $"'{value}' is not a duration. Help: write 200ms or 0.2s.", at);
        return 0;
    }

    private string EasingFor(string? timing, CssDeclaration at) => timing switch
    {
        null or "linear" => "Linear",
        "ease-in" => "CubicIn",
        "ease-out" => "CubicOut",
        "ease" or "ease-in-out" => "CubicInOut",
        _ => Invalid(at, $"animation-timing-function '{timing}' is not supported. Help: use linear, ease, ease-in, ease-out or ease-in-out."),
    };

    private (string Property, string Value, CssDeclaration Source)? DynamicFor(CssDeclaration d, bool isBox, bool isColorRect = false)
    {
        (string Property, string Kind)? target = d.Name switch
        {
            "color" => ("FontColor", "color"),
            "background-color" => (isColorRect ? "Color" : "Background", isColorRect ? "color" : "brush"),
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
            Error(FormaHtmlDiagnosticCodes.UnsupportedUnit, $"Unit in '{value}' is not supported. Help: use px or rem; for proportional sizes use flex-grow in a flex parent or fr tracks in a grid.", d);
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
        foreach (var template in _templates) BuildControlTemplate(template, styles);
        styles.AddRange(_itemResources);
        var themed = _themeTokens.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var emit = tokens.ToList();
        emit.AddRange(tokens.Where(t => themed.Contains(t.Name)).Select(t => new Token { Name = t.Name, Value = t.Value, Path = t.Path, Line = t.Line, Column = t.Column, Kind = t.Kind, Theme = "default" }));
        emit.AddRange(_themeTokens);
        foreach (var token in emit)
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
                styles.Add(Entry("SolidColorBrush", token.EntryKey(string.Empty), color: hex));
                styles.Add(Entry("xna:Color", token.EntryKey(".color"), text: hex));
            }
            else if (token.Kind == "single")
            {
                styles.Add(Entry("x:Single", token.EntryKey(string.Empty), text: Px(token.Value, probe)));
            }
        }

        foreach (var rule in rules)
        {
            if (IsRootRule(rule, out _))
            {
                foreach (var d in rule.Declarations.Where(d => !d.Name.StartsWith("--", StringComparison.Ordinal)))
                    Error(FormaHtmlDiagnosticCodes.UnsupportedSelector, "A :root rule only declares custom properties (--name: value).", d);
            }
        }

        BuildStoryboards(rules, styles);

        var index = 0;
        foreach (var rule in ExpandFocusVisible(rules).Where(r => !IsRootRule(r, out _) && r.Keyframes == null))
        {
            var selector = LowerSelector(rule, out var subjectType);
            if (selector == null) continue;
            var style = new XNode { Type = "Style", Line = rule.Line, Column = rule.Column, Path = rule.Path };
            style.Attrs.Add(new Attr("x:Key", $"FormaHtmlStyle{index++}", rule.Line, rule.Column, rule.Path));
            style.Attrs.Add(new Attr("Selector", selector, rule.Line, rule.Column, rule.Path));
            if (_project?.EmitOrigins ?? true) style.Attrs.Add(new Attr("Origin", $"{rule.Path}:{rule.Line}", rule.Line, rule.Column, rule.Path));
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
                if (property is "Background.Gradient" or "RenderTransform.Spec")
                {
                    Error(FormaHtmlDiagnosticCodes.RejectedProperty, $"{(property == "Background.Gradient" ? "A gradient" : "transform")} cannot be set from a style rule. Help: put it in the element's style attribute.", source);
                    continue;
                }

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
        else if (inner is ["prefers-reduced-motion", var motion] && motion is "reduce" or "no-preference")
            adaptive.Attrs.Add(new Attr("ReducedMotion", motion == "reduce" ? "True" : "False", rule.Line, rule.Column, rule.Path));
        else if (inner is ["prefers-color-scheme", var scheme] && scheme is "light" or "dark")
            adaptive.Attrs.Add(new Attr("ThemeVariant", scheme == "dark" ? "Dark" : "Light", rule.Line, rule.Column, rule.Path));
        else if (inner is ["min-width", var min]) adaptive.Attrs.Add(new Attr("MinViewportWidth", Px(min, new CssDeclaration("min-width", min, rule.Line, rule.Column) { Path = rule.Path }), rule.Line, rule.Column));
        else if (inner is ["max-width", var max]) adaptive.Attrs.Add(new Attr("MaxViewportWidth", Px(max, new CssDeclaration("max-width", max, rule.Line, rule.Column) { Path = rule.Path }), rule.Line, rule.Column));
        else Error(FormaHtmlDiagnosticCodes.UnsupportedSelector, $"@media {media} is not supported; Help: use (input-modality: pointer|keyboard|gamepad|touch), (prefers-reduced-motion: reduce), (prefers-color-scheme: light|dark), (min-width: Npx) or (max-width: Npx).", rule);
        condition.Children.Add(adaptive);
        style.PropertyElements.Add(("Style.Condition", adaptive));
    }

    private string? LowerSelector(CssRule rule, out string subjectType)
    {
        subjectType = "Control";
        var arms = new List<string>();
        foreach (var arm in ExpandIs(rule.Selector).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var lowered = new StringBuilder();
            var tokens = Tokenize(arm);
            var last = string.Empty;
            foreach (var token in tokens)
            {
                if (token == ">>")
                {
                    Error(FormaHtmlDiagnosticCodes.UnsupportedSelector, "The template-child combinator '>>' is XAML syntax. Help: mark the template element with part=\"name\" and select it with ::part(name), for example button.x:hover::part(chrome).", rule);
                    return null;
                }

                if (token is ">" or "+" or "~")
                {
                    if (token != ">") { Error(FormaHtmlDiagnosticCodes.UnsupportedSelector, $"Sibling combinator '{token}' is not supported yet. Help: use :first-child, :nth-child() or a class on the sibling; descendant and child (>) combinators work.", rule); return null; }
                    lowered.Append(" > ");
                    continue;
                }

                if (lowered.Length > 0 && !lowered.ToString().EndsWith("> ", StringComparison.Ordinal)) lowered.Append(' ');
                // ::part(name) selects a part inside the matched control's template: it lowers to Forma's template-child combinator.
                var partIndex = token.IndexOf("::part(", StringComparison.Ordinal);
                string? partName = null;
                var host = token;
                if (partIndex >= 0)
                {
                    var close = token.IndexOf(')', partIndex);
                    if (close < 0 || close != token.Length - 1) { Error(FormaHtmlDiagnosticCodes.UnsupportedSelector, "::part(name) must end the compound selector. Help: write button.x:hover::part(chrome), with the part name last.", rule); return null; }
                    partName = token.Substring(partIndex + 7, close - partIndex - 7).Trim();
                    if (partName.Length == 0 || !partName.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_')) { Error(FormaHtmlDiagnosticCodes.UnsupportedSelector, $"'{partName}' is not a part name. Help: use the value of the part attribute, for example ::part(chrome).", rule); return null; }
                    host = token.Substring(0, partIndex);
                }

                var compound = LowerCompound(host, rule, out last);
                if (compound == null) return null;
                // li and tr are the realized items of a list or grid: "ul.x li:hover" selects a template child, so it lowers to >>.
                if (last is "ListBoxItem" or "DataGridRow" && lowered.Length > 0 && lowered[^1] == ' ' && !lowered.ToString().EndsWith("> ", StringComparison.Ordinal))
                    lowered.Append(">> ");
                lowered.Append(compound);
                if (partName != null)
                {
                    // The part's element type comes from the <template> that declares it, so type-specific properties can be set.
                    var partType = PartType(partName);
                    lowered.Append(" >> ").Append(partType).Append(".part-").Append(partName);
                    last = partType;
                }
            }

            if (last.Length > 0) subjectType = last;
            arms.Add(lowered.ToString());
        }

        return arms.Count == 0 ? null : string.Join(", ", arms);
    }

    private string PartType(string partName)
    {
        foreach (var template in _templates)
        {
            var stack = new Stack<HtmlNode>(template.Children);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node.IsText) continue;
                if (node.Attr("part") == partName)
                    return node.Name == "slot" ? "TextBlock" : SelectorTypes.TryGetValue(node.Name, out var type) ? type : TextElements.TryGetValue(node.Name, out var text) ? text : "Control";
                foreach (var child in node.Children) stack.Push(child);
            }
        }

        return "Control";
    }

    // :is(a, b) and :where(a, b) are alternatives: expand them to a selector list before lowering (:where has zero specificity in
    // browsers; Forma has no zero-specificity form, so it behaves as :is and the difference is documented in the support matrix).
    private static string ExpandIs(string selector)
    {
        for (var guard = 0; guard < 16; guard++)
        {
            var start = selector.IndexOf(":is(", StringComparison.Ordinal);
            var length = 4;
            var where = selector.IndexOf(":where(", StringComparison.Ordinal);
            if (where >= 0 && (start < 0 || where < start)) { start = where; length = 7; }
            if (start < 0) return selector;
            var depth = 1;
            var end = start + length;
            for (; end < selector.Length && depth > 0; end++)
            {
                if (selector[end] == '(') depth++;
                else if (selector[end] == ')') depth--;
            }

            var inner = selector.Substring(start + length, end - start - length - 1);
            var comma = selector.LastIndexOf(',', start);
            var armStart = comma < 0 ? 0 : comma + 1;
            var armEndComma = selector.IndexOf(',', end);
            var armEnd = armEndComma < 0 ? selector.Length : armEndComma;
            var prefix = selector.Substring(armStart, start - armStart);
            var suffix = selector.Substring(end, armEnd - end);
            var alternatives = inner.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(a => prefix + a + suffix);
            selector = selector.Substring(0, armStart) + string.Join(", ", alternatives) + selector.Substring(armEnd);
        }

        return selector;
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
            if (depth == 0 && ch == '>')
            {
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
                if (tokens.Count > 0 && tokens[^1] == ">") tokens[^1] = ">>"; else tokens.Add(">");
                continue;
            }
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
        if (typeName.ToString() == "*") output.Append('*');
        else if (typeName.Length > 0)
        {
            if (!SelectorTypes.TryGetValue(typeName.ToString(), out var forma)) { Error(FormaHtmlDiagnosticCodes.UnsupportedSelector, $"Type selector '{typeName}' has no Forma type; use a class selector.", rule); return null; }
            type = forma;
            output.Append(forma);
        }

        while (i < compound.Length)
        {
            var kind = compound[i++];
            var start = i;
            if (kind == ':' && i < compound.Length && compound[i] == ':') { Error(FormaHtmlDiagnosticCodes.UnsupportedSelector, "Pseudo-elements other than ::part() are not supported. Help: style the element itself, or mark a template element with part=\"name\" and use ::part(name).", rule); return null; }
            if (kind == '[')
            {
                var close = compound.IndexOf(']', i);
                if (close < 0) { Error(FormaHtmlDiagnosticCodes.UnsupportedSelector, "Unterminated attribute selector.", rule); return null; }
                var attribute = compound.Substring(i, close - i).Replace("\"", string.Empty, StringComparison.Ordinal).Replace("'", string.Empty, StringComparison.Ordinal);
                i = close + 1;
                var mapped = attribute switch
                {
                    "type=checkbox" => "CheckBox",
                    "type=range" => "HSlider",
                    "type=text" => "LineEdit",
                    "role=tablist" => "TabContainer",
                    _ => null,
                };
                if (mapped == null && attribute.StartsWith("data-", StringComparison.Ordinal)) { output.Append('[').Append(attribute).Append(']'); continue; }
                if (mapped == null && attribute == "disabled") { output.Append(":disabled"); continue; }
                if (mapped == null && attribute == "checked") { output.Append(":checked"); continue; }
                if (mapped == null) { Error(FormaHtmlDiagnosticCodes.UnsupportedSelector, $"Attribute selector [{attribute}] is not supported. Help: supported are [data-*], [disabled], [checked], [type=checkbox], [type=range], [type=text] and [role=tablist]; use a data-* attribute or a class for other state.", rule); return null; }
                if (output.Length > 0 && type is not ("LineEdit" or "Control" or "")) { Error(FormaHtmlDiagnosticCodes.UnsupportedSelector, $"[{attribute}] cannot be combined with the '{type}' type selector.", rule); return null; }
                var previous = type;
                var text = output.ToString();
                var tail = previous.Length > 0 && text.StartsWith(previous, StringComparison.Ordinal) ? text.Substring(previous.Length) : text;
                type = mapped;
                output.Clear().Append(mapped).Append(tail);
                continue;
            }
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
                    else if (name == "host") { if (i < compound.Length && compound[i] == '(') { var close = compound.IndexOf(')', i); var inner = LowerCompound(compound.Substring(i + 1, close - i - 1), rule, out _); if (inner == null) return null; output.Append(inner); i = close + 1; } }
                    else if (name is "hover" or "focus" or "disabled" or "checked" or "selected" or "focus-within") output.Append(':').Append(name);
                    else if (name is "first-child" or "last-child" or "only-child" or "empty") output.Append(':').Append(name);
                    else if (name == "nth-child" && i < compound.Length && compound[i] == '(')
                    {
                        var close = compound.IndexOf(')', i);
                        output.Append(":nth-child").Append(compound, i, close - i + 1);
                        i = close + 1;
                    }
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
    // Every error says what to do next. Messages that already name the nearest supported alternative end in "Help: …"; the others get the
    // default for their code, which points at the generated support matrix and cookbook.
    private static List<FormaDiagnostic> WithHelp(List<FormaDiagnostic> diagnostics)
    {
        var result = new List<FormaDiagnostic>(diagnostics.Count);
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity != FormaDiagnosticSeverity.Error || diagnostic.Message.Contains("Help:", StringComparison.Ordinal)) { result.Add(diagnostic); continue; }
            result.Add(diagnostic with { Message = diagnostic.Message.TrimEnd() + (diagnostic.Message.TrimEnd().EndsWith('.') ? " " : ". ") + DefaultHelp(diagnostic.Code) });
        }

        return result;
    }

    private static string DefaultHelp(string code) => code switch
    {
        FormaHtmlDiagnosticCodes.Syntax => "Help: check the syntax at this location; every tag needs its closing tag and every rule is 'selector { property: value; }'.",
        FormaHtmlDiagnosticCodes.UnknownElement => "Help: see docs/html-css-support-matrix.md for the supported elements, or register an application element with <meta name=\"f-element\" content=\"tag-name=prefix:Type\">.",
        FormaHtmlDiagnosticCodes.UnknownAttribute => "Help: use a supported attribute (docs/html-css-support-matrix.md), or set a Forma property directly with f:Property=\"value\".",
        FormaHtmlDiagnosticCodes.RejectedConstruct => "Help: docs/html-css-support-matrix.md lists the nearest supported construct for what is rejected.",
        FormaHtmlDiagnosticCodes.Structure => "Help: compare the nesting with the examples in docs/html-css-cookbook.md.",
        FormaHtmlDiagnosticCodes.LinkTarget => "Help: the path is relative to the linking file and the file must exist in the project.",
        FormaHtmlDiagnosticCodes.UnknownProperty => "Help: docs/html-css-support-matrix.md lists the supported CSS properties; use -f-Property for a Forma property.",
        FormaHtmlDiagnosticCodes.UnsupportedUnit => "Help: use px or rem; for proportional sizes use flex-grow or fr tracks.",
        FormaHtmlDiagnosticCodes.InvalidValue => "Help: docs/html-css-support-matrix.md lists the accepted values of this property.",
        FormaHtmlDiagnosticCodes.UnsupportedSelector => "Help: supported selectors are type, class, id, [data-*], pseudo-classes, :is(), :not(), ::part() and the descendant and child combinators.",
        FormaHtmlDiagnosticCodes.RejectedProperty => "Help: docs/html-css-support-matrix.md lists the nearest supported property.",
        _ => "Help: see docs/html-css-dialect.md.",
    };

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

        #if FORMA_FNA
        private const string XnaNamespace = "clr-namespace:Microsoft.Xna.Framework;assembly=FNA.NET";
#else
        private const string XnaNamespace = "clr-namespace:Microsoft.Xna.Framework;assembly=MonoGame.Framework";
#endif
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
