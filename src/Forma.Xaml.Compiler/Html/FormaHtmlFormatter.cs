// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Text;

namespace Forma.Xaml.Compiler.Html;

/// <summary>
/// A deterministic formatter for .fhtml files: two-space indentation, one element per line, attributes in source order (wrapped
/// one per line when the tag would exceed the width), normalized inline styles and style blocks, comments preserved. Formatting an
/// already formatted file returns it unchanged.
/// </summary>
public static class FormaHtmlFormatter
{
    private const int Width = 110;
    private const string Indent = "  ";

    public static string Format(string html, string path, out IReadOnlyList<FormaDiagnostic> diagnostics)
    {
        var list = new List<FormaDiagnostic>();
        var roots = new HtmlParser(html, path, list, keepComments: true).Parse();
        diagnostics = list;
        if (list.Count > 0) return html;
        var output = new StringBuilder();
        for (var i = 0; i < roots.Count; i++)
        {
            if (i > 0 && NeedsBlankLine(roots[i - 1], roots[i])) output.Append('\n');
            WriteNode(roots[i], 0, output);
        }

        return output.ToString();
    }

    /// <summary>
    /// Formats a .fcss file: one rule per block with two-space declarations, rules separated by one blank line, top-level comments
    /// kept as written. A rule that contains a comment is kept verbatim. Idempotent.
    /// </summary>
    public static string FormatStylesheet(string css, string path, out IReadOnlyList<FormaDiagnostic> diagnostics)
    {
        var list = new List<FormaDiagnostic>();
        diagnostics = list;
        var segments = new List<string>();
        var index = 0;
        while (index < css.Length)
        {
            if (char.IsWhiteSpace(css[index])) { index++; continue; }
            if (string.CompareOrdinal(css, index, "/*", 0, 2) == 0)
            {
                var end = css.IndexOf("*/", index + 2, StringComparison.Ordinal);
                end = end < 0 ? css.Length : end + 2;
                segments.Add(css.Substring(index, end - index));
                index = end;
                continue;
            }

            var start = index;
            var depth = 0;
            for (; index < css.Length; index++)
            {
                if (css[index] == '{') depth++;
                else if (css[index] == '}' && --depth <= 0) { index++; break; }
            }

            var chunk = css.Substring(start, index - start);
            if (chunk.Contains("/*", StringComparison.Ordinal)) { segments.Add(chunk.Trim()); continue; }
            var rules = new CssParser(chunk, path, 1, list).ParseStylesheet();
            var builder = new StringBuilder();
            WriteRules(rules.Where(r => r.Media == null), string.Empty, builder);
            foreach (var media in rules.Where(r => r.Media != null).GroupBy(r => r.Media))
            {
                builder.Append("@media ").Append(media.Key).Append(" {\n");
                WriteRules(media, Indent, builder);
                builder.Append("}\n");
            }

            segments.Add(builder.ToString().TrimEnd('\n'));
        }

        return list.Count > 0 ? css : string.Join("\n\n", segments) + "\n";
    }

    private static bool NeedsBlankLine(HtmlNode previous, HtmlNode next) =>
        !(previous.Name == "meta" && next.Name == "meta") && !(previous.IsComment && !next.IsComment);

    private static void WriteNode(HtmlNode node, int depth, StringBuilder output)
    {
        var pad = string.Concat(Enumerable.Repeat(Indent, depth));
        if (node.IsComment)
        {
            output.Append(pad).Append("<!--").Append(node.Text).Append("-->\n");
            return;
        }

        if (node.IsText)
        {
            output.Append(pad).Append(Escape(node.Text)).Append('\n');
            return;
        }

        if (node.Name == "style")
        {
            WriteStyle(node, depth, output);
            return;
        }

        if (node.Name == "f-resources")
        {
            // Raw XAML resources: re-indented as a block, never rewritten.
            var raw = (node.Children.FirstOrDefault()?.Text ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
            var margin = raw.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
            output.Append(OpenTag(node, pad, out _)).Append(">\n");
            foreach (var line in raw)
                if (line.Trim().Length > 0) output.Append(pad).Append(Indent).Append(line.Substring(Math.Min(margin, line.Length)).TrimEnd()).Append('\n');
            output.Append(pad).Append("</f-resources>\n");
            return;
        }

        var open = OpenTag(node, pad, out var multiline);
        var children = node.Children;
        var isVoid = node.Name is "br" or "hr" or "img" or "input" or "meta" or "link";
        if (isVoid)
        {
            output.Append(open).Append(">\n");
            return;
        }

        if (children.Count == 0)
        {
            output.Append(open).Append("></").Append(node.Name).Append(">\n");
            return;
        }

        if (!multiline && children.All(c => c.IsText) && open.Length + children.Sum(c => c.Text.Length) + node.Name.Length + 3 <= Width)
        {
            output.Append(open).Append('>').Append(string.Join(" ", children.Select(c => Escape(c.Text)))).Append("</").Append(node.Name).Append(">\n");
            return;
        }

        output.Append(open).Append(">\n");
        foreach (var child in children) WriteNode(child, depth + 1, output);
        output.Append(pad).Append("</").Append(node.Name).Append(">\n");
    }

    private static string OpenTag(HtmlNode node, string pad, out bool multiline)
    {
        var attributes = node.Attributes.Select(FormatAttribute).ToList();
        var single = pad + "<" + node.Name + string.Concat(attributes.Select(a => " " + a));
        multiline = false;
        if (single.Length <= Width || attributes.Count <= 1) return single;
        multiline = true;
        var continuation = pad + new string(' ', node.Name.Length + 2);
        var builder = new StringBuilder(pad).Append('<').Append(node.Name).Append(' ').Append(attributes[0]);
        foreach (var attribute in attributes.Skip(1)) builder.Append('\n').Append(continuation).Append(attribute);
        return builder.ToString();
    }

    private static string FormatAttribute(HtmlAttribute attribute)
    {
        if (attribute.Name == "style") return $"style=\"{FormatInlineStyle(attribute.Value)}\"";
        var value = attribute.Value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal);
        return value.Length == 0 && attribute.Name is "checked" or "disabled" or "hidden" ? attribute.Name : $"{attribute.Name}=\"{value}\"";
    }

    private static string FormatInlineStyle(string style)
    {
        var declarations = style.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(d => { var i = d.IndexOf(':'); return i < 0 ? d : d.Substring(0, i).Trim() + ": " + d.Substring(i + 1).Trim(); });
        return string.Join("; ", declarations);
    }

    private static void WriteStyle(HtmlNode node, int depth, StringBuilder output)
    {
        var pad = string.Concat(Enumerable.Repeat(Indent, depth));
        var text = node.Children.FirstOrDefault()?.Text ?? string.Empty;
        output.Append(pad).Append("<style>\n");
        if (text.Contains("/*", StringComparison.Ordinal))
        {
            // CSS comments are not modeled, so a block that has them is only re-indented, never rewritten.
            foreach (var line in text.Trim('\n').Split('\n')) output.Append(line.Trim().Length == 0 ? string.Empty : pad + Indent + line.Trim()).Append('\n');
        }
        else
        {
            var diagnostics = new List<FormaDiagnostic>();
            var rules = new CssParser(text, "style", 1, diagnostics).ParseStylesheet();
            WriteRules(rules.Where(r => r.Media == null), pad + Indent, output);
            foreach (var media in rules.Where(r => r.Media != null).GroupBy(r => r.Media))
            {
                if (output[output.Length - 2] != '>') output.Append('\n');
                output.Append(pad).Append(Indent).Append("@media ").Append(media.Key).Append(" {\n");
                WriteRules(media, pad + Indent + Indent, output);
                output.Append(pad).Append(Indent).Append("}\n");
            }
        }

        output.Append(pad).Append("</style>\n");
    }

    private static void WriteRules(IEnumerable<CssRule> rules, string pad, StringBuilder output)
    {
        var first = true;
        foreach (var rule in rules)
        {
            if (!first) output.Append('\n');
            first = false;
            output.Append(pad).Append(rule.Selector).Append(" {\n");
            foreach (var d in rule.Declarations) output.Append(pad).Append(Indent).Append(d.Name).Append(": ").Append(d.Value).Append(";\n");
            output.Append(pad).Append("}\n");
        }
    }

    private static string Escape(string text) => text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal);
}
