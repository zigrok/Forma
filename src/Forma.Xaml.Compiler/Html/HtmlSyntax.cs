// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Text;

namespace Forma.Xaml.Compiler.Html;

/// <summary>Stable diagnostic codes of the HTML and CSS authoring front end.</summary>
public static class FormaHtmlDiagnosticCodes
{
    public const string Syntax = "FHTML1001";
    public const string UnknownElement = "FHTML1002";
    public const string UnknownAttribute = "FHTML1003";
    public const string RejectedConstruct = "FHTML1004";
    public const string Structure = "FHTML1005";
    public const string UnknownProperty = "FHTML2001";
    public const string UnsupportedUnit = "FHTML2002";
    public const string InvalidValue = "FHTML2003";
    public const string UnsupportedSelector = "FHTML2004";
    public const string RejectedProperty = "FHTML2005";
}

public sealed class HtmlAttribute
{
    public HtmlAttribute(string name, string value, int line, int column)
    {
        Name = name;
        Value = value;
        Line = line;
        Column = column;
    }

    public string Name { get; }
    public string Value { get; }
    public int Line { get; }
    public int Column { get; }
}

public sealed class HtmlNode
{
    public string Name { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public bool IsText { get; init; }
    public bool IsComment { get; init; }
    public int Line { get; init; }
    public int Column { get; init; }
    public List<HtmlAttribute> Attributes { get; } = new();
    public List<HtmlNode> Children { get; } = new();

    public string? Attr(string name) => Attributes.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal))?.Value;
}

public sealed class CssDeclaration
{
    public CssDeclaration(string name, string value, int line, int column)
    {
        Name = name;
        Value = value;
        Line = line;
        Column = column;
    }

    public string Name { get; }
    public string Value { get; }
    public int Line { get; }
    public int Column { get; }
}

public sealed class CssRule
{
    public string Selector { get; init; } = string.Empty;
    public int Line { get; init; }
    public int Column { get; init; }
    public List<CssDeclaration> Declarations { get; } = new();
    public string? Media { get; init; }
}

/// <summary>A small strict HTML parser: elements, attributes, text, comments and one style block. Anything else is an error.</summary>
public sealed class HtmlParser
{
    private static readonly HashSet<string> VoidElements = new(StringComparer.Ordinal) { "br", "hr", "img", "input", "meta", "link" };

    private readonly bool _keepComments;
    private readonly string _source;
    private readonly string _path;
    private readonly List<FormaDiagnostic> _diagnostics;
    private int _index;
    private int _line = 1;
    private int _column = 1;

    public HtmlParser(string source, string path, List<FormaDiagnostic> diagnostics, bool keepComments = false)
    {
        _keepComments = keepComments;
        _source = source;
        _path = path;
        _diagnostics = diagnostics;
    }

    public List<HtmlNode> Parse()
    {
        var roots = new List<HtmlNode>();
        ParseChildren(null, roots);
        return roots;
    }

    private void ParseChildren(string? closing, List<HtmlNode> into)
    {
        while (_index < _source.Length)
        {
            if (Peek("<!--"))
            {
                var end = _source.IndexOf("-->", _index, StringComparison.Ordinal);
                if (end < 0) { Error(FormaHtmlDiagnosticCodes.Syntax, "Unterminated comment."); _index = _source.Length; return; }
                if (_keepComments) into.Add(new HtmlNode { IsComment = true, Text = _source.Substring(_index + 4, end - _index - 4), Line = _line, Column = _column });
                Advance(end + 3 - _index);
                continue;
            }

            if (Peek("<!"))
            {
                var end = _source.IndexOf('>', _index);
                Advance((end < 0 ? _source.Length : end + 1) - _index);
                continue;
            }

            if (Peek("</"))
            {
                var line = _line;
                var column = _column;
                Advance(2);
                var name = ReadName();
                SkipSpace();
                if (!Take('>')) Error(FormaHtmlDiagnosticCodes.Syntax, "Expected '>' to end the closing tag.");
                if (closing == null || !string.Equals(name, closing, StringComparison.Ordinal))
                    _diagnostics.Add(Diagnostic(FormaHtmlDiagnosticCodes.Syntax, $"Unexpected closing tag </{name}>.", line, column));
                return;
            }

            if (Peek("<"))
            {
                var element = ParseElement();
                if (element != null) into.Add(element);
                continue;
            }

            var textLine = _line;
            var textColumn = _column;
            var start = _index;
            while (_index < _source.Length && _source[_index] != '<') Advance(1);
            var text = CollapseWhitespace(_source.Substring(start, _index - start));
            if (text.Length > 0) into.Add(new HtmlNode { IsText = true, Text = text, Line = textLine, Column = textColumn });
        }

        if (closing != null) Error(FormaHtmlDiagnosticCodes.Syntax, $"Missing closing tag </{closing}>.");
    }

    private HtmlNode? ParseElement()
    {
        var line = _line;
        var column = _column;
        Advance(1);
        var name = ReadName();
        if (name.Length == 0)
        {
            _diagnostics.Add(Diagnostic(FormaHtmlDiagnosticCodes.Syntax, "Expected an element name after '<'.", line, column));
            return null;
        }

        var node = new HtmlNode { Name = name, Line = line, Column = column };
        var selfClosed = false;
        while (true)
        {
            SkipSpace();
            if (_index >= _source.Length) { Error(FormaHtmlDiagnosticCodes.Syntax, $"Unterminated tag <{name}>."); return node; }
            if (Take('>')) break;
            if (Peek("/>")) { Advance(2); selfClosed = true; break; }
            var attrLine = _line;
            var attrColumn = _column;
            var attrName = ReadAttributeName();
            if (attrName.Length == 0) { Error(FormaHtmlDiagnosticCodes.Syntax, "Expected an attribute name."); Advance(1); continue; }
            var value = string.Empty;
            SkipSpace();
            if (Take('='))
            {
                SkipSpace();
                if (_index < _source.Length && (_source[_index] == '"' || _source[_index] == '\''))
                {
                    var quote = _source[_index];
                    Advance(1);
                    var start = _index;
                    while (_index < _source.Length && _source[_index] != quote) Advance(1);
                    value = DecodeEntities(_source.Substring(start, _index - start));
                    if (!Take(quote)) Error(FormaHtmlDiagnosticCodes.Syntax, $"Unterminated value of attribute '{attrName}'.");
                }
                else
                {
                    Error(FormaHtmlDiagnosticCodes.Syntax, $"The value of attribute '{attrName}' must be quoted.");
                }
            }

            node.Attributes.Add(new HtmlAttribute(attrName, value, attrLine, attrColumn));
        }

        if (selfClosed || VoidElements.Contains(name)) return node;
        if (name == "style")
        {
            var end = _source.IndexOf("</style>", _index, StringComparison.Ordinal);
            if (end < 0) { Error(FormaHtmlDiagnosticCodes.Syntax, "Missing closing tag </style>."); _index = _source.Length; return node; }
            var textLine = _line;
            var textColumn = _column;
            var text = _source.Substring(_index, end - _index);
            Advance(end + "</style>".Length - _index);
            node.Children.Add(new HtmlNode { IsText = true, Text = text, Line = textLine, Column = textColumn });
            return node;
        }

        ParseChildren(name, node.Children);
        return node;
    }

    private string ReadName()
    {
        var start = _index;
        while (_index < _source.Length && (char.IsLetterOrDigit(_source[_index]) || _source[_index] is '-' or '_' or ':' or '.')) Advance(1);
        return _source.Substring(start, _index - start);
    }

    private string ReadAttributeName()
    {
        var start = _index;
        while (_index < _source.Length && !char.IsWhiteSpace(_source[_index]) && _source[_index] is not ('=' or '>' or '/' or '"' or '\'')) Advance(1);
        return _source.Substring(start, _index - start);
    }

    private void SkipSpace() { while (_index < _source.Length && char.IsWhiteSpace(_source[_index])) Advance(1); }
    private bool Peek(string value) => string.CompareOrdinal(_source, _index, value, 0, value.Length) == 0;
    private bool Take(char value) { if (_index < _source.Length && _source[_index] == value) { Advance(1); return true; } return false; }

    private void Advance(int count)
    {
        for (var i = 0; i < count && _index < _source.Length; i++)
        {
            if (_source[_index] == '\n') { _line++; _column = 1; } else _column++;
            _index++;
        }
    }

    private void Error(string code, string message) => _diagnostics.Add(Diagnostic(code, message, _line, _column));

    private FormaDiagnostic Diagnostic(string code, string message, int line, int column) =>
        new(code, FormaDiagnosticSeverity.Error, message, new FormaSourceLocation(_path, line, column));

    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder();
        var space = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch)) { space = true; continue; }
            if (space && builder.Length > 0) builder.Append(' ');
            space = false;
            builder.Append(ch);
        }
        return DecodeEntities(builder.ToString());
    }

    private static string DecodeEntities(string text) =>
        text.Replace("&lt;", "<", StringComparison.Ordinal).Replace("&gt;", ">", StringComparison.Ordinal)
            .Replace("&quot;", "\"", StringComparison.Ordinal).Replace("&apos;", "'", StringComparison.Ordinal)
            .Replace("&amp;", "&", StringComparison.Ordinal);
}

/// <summary>A strict CSS parser: rules with declarations. At-rules other than a single supported @media form are rejected.</summary>
public sealed class CssParser
{
    private readonly string _source;
    private readonly string _path;
    private readonly int _lineOffset;
    private readonly List<FormaDiagnostic> _diagnostics;
    private int _index;

    public CssParser(string source, string path, int firstLine, List<FormaDiagnostic> diagnostics)
    {
        _source = source;
        _path = path;
        _lineOffset = firstLine - 1;
        _diagnostics = diagnostics;
    }

    public List<CssRule> ParseStylesheet()
    {
        var rules = new List<CssRule>();
        ParseRules(rules, null);
        return rules;
    }

    public static List<CssDeclaration> ParseDeclarations(string text, string path, int line, int column, List<FormaDiagnostic> diagnostics)
    {
        var parser = new CssParser(text, path, line, diagnostics) { _baseColumn = column };
        var list = new List<CssDeclaration>();
        parser.ParseDeclarationList(list, topLevel: true);
        return list;
    }

    private int _baseColumn = 1;

    private void ParseRules(List<CssRule> rules, string? media)
    {
        while (true)
        {
            SkipTrivia();
            if (_index >= _source.Length || _source[_index] == '}') return;
            var start = _index;
            if (_source[_index] == '@')
            {
                var open = _source.IndexOf('{', _index);
                var header = open < 0 ? string.Empty : _source.Substring(_index, open - _index).Trim();
                if (!header.StartsWith("@media", StringComparison.Ordinal) || media != null)
                {
                    Error(FormaHtmlDiagnosticCodes.RejectedConstruct, $"At-rule '{(header.Length == 0 ? "@" : header.Split(' ')[0])}' is not part of the dialect; only a single @media block on input modality is supported.", start);
                    SkipBlock();
                    continue;
                }

                _index = open + 1;
                ParseRules(rules, header.Substring("@media".Length).Trim());
                if (_index < _source.Length && _source[_index] == '}') _index++;
                continue;
            }

            var braceIndex = _source.IndexOf('{', _index);
            if (braceIndex < 0) { Error(FormaHtmlDiagnosticCodes.Syntax, "Expected '{' after a selector.", start); return; }
            var selector = _source.Substring(_index, braceIndex - _index).Trim();
            var rule = new CssRule { Selector = selector, Line = LineOf(start), Column = ColumnOf(start), Media = media };
            _index = braceIndex + 1;
            ParseDeclarationList(rule.Declarations, topLevel: false);
            if (_index < _source.Length && _source[_index] == '}') _index++;
            else Error(FormaHtmlDiagnosticCodes.Syntax, "Missing '}' at the end of a rule.", _index);
            rules.Add(rule);
        }
    }

    private void ParseDeclarationList(List<CssDeclaration> into, bool topLevel)
    {
        while (true)
        {
            SkipTrivia();
            if (_index >= _source.Length || (!topLevel && _source[_index] == '}')) return;
            var start = _index;
            var colon = _source.IndexOf(':', _index);
            var semi = _source.IndexOf(';', _index);
            var close = topLevel ? -1 : _source.IndexOf('}', _index);
            var end = new[] { semi, close }.Where(i => i >= 0).DefaultIfEmpty(_source.Length).Min();
            if (colon < 0 || colon > end)
            {
                Error(FormaHtmlDiagnosticCodes.Syntax, "Expected 'property: value'.", start);
                _index = Math.Min(_source.Length, end + (end < _source.Length && _source[end] == ';' ? 1 : 0));
                if (end >= _source.Length) return;
                continue;
            }

            var name = _source.Substring(_index, colon - _index).Trim();
            var value = _source.Substring(colon + 1, end - colon - 1).Trim();
            if (value.EndsWith("!important", StringComparison.Ordinal))
                Error(FormaHtmlDiagnosticCodes.RejectedConstruct, "!important is not part of the dialect; Forma orders rules by specificity and declaration order.", start);
            into.Add(new CssDeclaration(name, value, LineOf(start), ColumnOf(start)));
            _index = end;
            if (_index < _source.Length && _source[_index] == ';') _index++;
        }
    }

    private void SkipBlock()
    {
        var depth = 0;
        while (_index < _source.Length)
        {
            if (_source[_index] == '{') depth++;
            else if (_source[_index] == '}') { depth--; if (depth <= 0) { _index++; return; } }
            _index++;
        }
    }

    private void SkipTrivia()
    {
        while (_index < _source.Length)
        {
            if (char.IsWhiteSpace(_source[_index])) { _index++; continue; }
            if (string.CompareOrdinal(_source, _index, "/*", 0, 2) == 0)
            {
                var end = _source.IndexOf("*/", _index + 2, StringComparison.Ordinal);
                _index = end < 0 ? _source.Length : end + 2;
                continue;
            }
            return;
        }
    }

    private int LineOf(int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < _source.Length; i++) if (_source[i] == '\n') line++;
        return line + _lineOffset;
    }

    private int ColumnOf(int index)
    {
        var lineStart = _source.LastIndexOf('\n', Math.Max(0, Math.Min(index, _source.Length - 1)));
        var column = index - (lineStart + 1) + 1;
        return LineOf(index) == _lineOffset + 1 ? column + _baseColumn - 1 : column;
    }

    private void Error(string code, string message, int index) =>
        _diagnostics.Add(new FormaDiagnostic(code, FormaDiagnosticSeverity.Error, message, new FormaSourceLocation(_path, LineOf(index), ColumnOf(index))));
}
