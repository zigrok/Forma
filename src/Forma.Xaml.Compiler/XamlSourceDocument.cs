// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Xml;
using System.Xml.Linq;
using Forma.Xaml;

namespace Forma.Xaml.Compiler;

/// <summary>A bounded attribute transaction. Untouched bytes, quotes and whitespace are never serialized.</summary>
public sealed class XamlSourceDocument
{
    public const int DefaultMaxDocumentCharacters = 1024 * 1024;
    private readonly Stack<string> _undo = new();
    public string Text { get; private set; }
    public string Path { get; }
    public int MaxDocumentCharacters { get; }
    public string Revision => AuthoringSourceText.Revision(Text);

    public static XamlSourceDocument FromUtf8(string path, ReadOnlySpan<byte> bytes,
        int maxDocumentCharacters = DefaultMaxDocumentCharacters) =>
        new(path, AuthoringSourceText.Decode(bytes), maxDocumentCharacters);

    public XamlSourceDocument(string path, string text, int maxDocumentCharacters = DefaultMaxDocumentCharacters)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDocumentCharacters);
        Path = path;
        Text = text;
        MaxDocumentCharacters = maxDocumentCharacters;
        _ = Parse();
    }

    public XDocument Parse() => Parse(Text);

    private XDocument Parse(string text)
    {
        if (text.Length > MaxDocumentCharacters)
            throw new XmlException($"XAML exceeds the {MaxDocumentCharacters} UTF-16 character document limit.");
        using var input = new StringReader(AuthoringSourceText.ParserText(text));
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            CheckCharacters = true,
            MaxCharactersInDocument = MaxDocumentCharacters
        });
        return XDocument.Load(reader, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
    }

    public IReadOnlyList<XamlSourceNode> Describe(Func<string, string, Type?> resolveType)
    {
        var (lowered, _) = Lower(Parse(), new FormaXamlParseOptions
        {
            RequireCompiledBindings = true,
            TypeResolver = resolveType
        });
        return lowered.Nodes.Select(node => AuthoringSourceText.Describe(lowered, node,
            node.Id == lowered.RootNodeId && lowered.RootClass != null ? lowered.RootClass :
                resolveType(node.XmlNamespace, node.TypeName)?.FullName ?? node.TypeName)).ToArray();
    }

    public void ReplaceAttribute(XamlSourceNode source, string attributeName, string literal, Action<string> validate)
    {
        if (source.Document != Path || source.Revision != Revision)
            throw new InvalidOperationException("Source identity is stale or belongs to another document.");
        var (lowered, nodes) = Lower(Parse());
        if ((uint)source.Node >= nodes.Length) throw new InvalidOperationException("Unknown source node.");
        var node = lowered.Nodes[source.Node];
        if (source.Scope != node.ScopeId || source.Line != node.SourceRange.StartLine ||
            source.Column != AuthoringSourceText.CanonicalColumn(Text, node.SourceRange.StartLine, node.SourceRange.StartColumn) ||
            source.Name != (node.FindDirective("Name") ?? ""))
            throw new InvalidOperationException("Source node identity does not match the lowered document.");
        var isControl = node.XmlNamespace == XamlNamespaces.Forma &&
            typeof(Control).Assembly.GetType("Forma." + node.TypeName) is { } type && typeof(Control).IsAssignableFrom(type);
        var isStyleValue = node.XmlNamespace == XamlNamespaces.Forma && node.TypeName == "Setter" &&
            node.ParentId is { } parent && lowered.Nodes[parent.Value].XmlNamespace == XamlNamespaces.Forma &&
            lowered.Nodes[parent.Value].TypeName == "Style" && attributeName == "Value";
        if (node.ScopeId != lowered.OwnerScope.ScopeId || (!isControl && !isStyleValue))
            throw new InvalidOperationException("This source node is outside the bounded owner-control/style-value editing capability.");
        var attribute = nodes[source.Node].Attribute(attributeName)
            ?? throw new InvalidOperationException("Adding attributes requires a structural transaction.");
        if (!source.Members.TryGetValue(attributeName, out var expected) || attribute.Value != expected)
            throw new InvalidOperationException("Authored value differs from the compiled source.");
        var info = (IXmlLineInfo)attribute;
        var start = AuthoringSourceText.Utf16Offset(Text, info.LineNumber,
            AuthoringSourceText.CanonicalColumn(Text, info.LineNumber, info.LinePosition));
        var equals = Text.IndexOf('=', start);
        var quote = equals + 1;
        while (char.IsWhiteSpace(Text[quote])) quote++;
        if (Text[quote] is not ('\'' or '"')) throw new InvalidOperationException("Unsupported attribute token.");
        var end = Text.IndexOf(Text[quote], quote + 1);
        if (literal.Length > MaxDocumentCharacters)
            throw new XmlException($"Literal exceeds the {MaxDocumentCharacters} UTF-16 character document limit.");
        var escaped = literal.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal)
            .Replace("\r", "&#13;", StringComparison.Ordinal)
            .Replace("\n", "&#10;", StringComparison.Ordinal)
            .Replace("\t", "&#9;", StringComparison.Ordinal);
        var candidate = Text[..(quote + 1)] + escaped + Text[end..];
        _ = Parse(candidate);
        validate(candidate);
        _undo.Push(Text);
        Text = candidate;
    }

    public void Undo(string expectedRevision)
    {
        if (Revision != expectedRevision) throw new InvalidOperationException("Source changed after the transaction.");
        if (_undo.Count == 0) throw new InvalidOperationException("No authoring transaction to undo.");
        Text = _undo.Pop();
    }

    private (FormaLoweredDocument Lowered, XElement[] Elements) Lower(XDocument xml, FormaXamlParseOptions? options = null)
    {
        var parserText = AuthoringSourceText.ParserText(Text);
        var parsed = new FormaXamlParser().Parse(parserText, Path, options);
        if (!parsed.Success) throw new FormaXamlCompilationException(parsed.Diagnostics);
        var lowered = new FormaXamlLowerer().Lower(parserText, parsed.Document!, Text);
        var elements = xml.Root!.DescendantsAndSelf().ToArray();
        if (lowered.Nodes.Count != elements.Length)
            throw new InvalidOperationException("Lowered nodes and XML elements do not have matching source identities.");
        var indices = elements.Select((element, index) => (element, index)).ToDictionary(item => item.element, item => item.index);
        for (var index = 0; index < elements.Length; index++)
        {
            var element = elements[index];
            var node = lowered.Nodes[index];
            var location = (IXmlLineInfo)element;
            int? parent = element.Parent == null ? null : indices[element.Parent];
            if (node.Id.Value != index || node.XmlNamespace != element.Name.NamespaceName ||
                node.TypeName != element.Name.LocalName || node.ParentId?.Value != parent ||
                node.SourceRange.StartLine != location.LineNumber || node.SourceRange.StartColumn != location.LinePosition)
                throw new InvalidOperationException("Lowered nodes and XML elements do not have matching source identities.");
        }
        return (lowered, elements);
    }

}
