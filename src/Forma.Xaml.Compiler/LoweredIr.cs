// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Collections.ObjectModel;

namespace Forma.Xaml.Compiler;

public readonly record struct FormaNodeId(int Value);

public readonly record struct FormaSymbolId(int Value)
{
    public static FormaSymbolId None => new(-1);
    public bool IsResolved => Value >= 0;
}

public sealed record FormaSourceRange(
    string FilePath,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);

public enum FormaLoweredSymbolKind
{
    Type,
    Member,
}

public sealed record FormaLoweredSymbol(
    FormaSymbolId Id,
    FormaLoweredSymbolKind Kind,
    string Namespace,
    string Owner,
    string Name);

public abstract record FormaLoweredValue(string RawText);

public sealed record FormaLiteralValue(string Text) : FormaLoweredValue(Text);

public enum FormaBindingSourceKind
{
    DataContext,
    Self,
    TemplatedParent,
    FindAncestor,
}

public sealed record FormaBindingSource(
    FormaBindingSourceKind Kind,
    FormaSymbolId TypeSymbolId,
    int AncestorLevel);

public sealed record FormaBindingValue(
    string RawText,
    string Path,
    IReadOnlyDictionary<string, string> Options,
    FormaBindingSource Source) : FormaLoweredValue(RawText);

public sealed record FormaResourceValue(
    string RawText,
    string Key,
    bool IsDynamic) : FormaLoweredValue(RawText);

public sealed record FormaLoweredMember(
    FormaSymbolId SymbolId,
    string Namespace,
    string Name,
    FormaLoweredValue Value,
    bool IsDirective,
    FormaSourceRange SourceRange);

public sealed class FormaLoweredNode
{
    internal FormaLoweredNode(
        FormaNodeId id,
        FormaNodeId? parentId,
        int scopeId,
        string xmlNamespace,
        string typeName,
        FormaSymbolId typeSymbolId,
        FormaXamlTemplateKind templateKind,
        StyleSelector? selector,
        IEnumerable<FormaLoweredMember> members,
        IEnumerable<FormaNodeId> children,
        FormaSourceRange sourceRange)
    {
        Id = id;
        ParentId = parentId;
        ScopeId = scopeId;
        XmlNamespace = xmlNamespace;
        TypeName = typeName;
        TypeSymbolId = typeSymbolId;
        TemplateKind = templateKind;
        Selector = selector;
        Members = Array.AsReadOnly(members.ToArray());
        Children = Array.AsReadOnly(children.ToArray());
        SourceRange = sourceRange;
    }

    public FormaNodeId Id { get; }
    public FormaNodeId? ParentId { get; }
    public int ScopeId { get; }
    public string XmlNamespace { get; }
    public string TypeName { get; }
    public FormaSymbolId TypeSymbolId { get; }
    public FormaXamlTemplateKind TemplateKind { get; }
    public StyleSelector? Selector { get; }
    public ReadOnlyCollection<FormaLoweredMember> Members { get; }
    public ReadOnlyCollection<FormaNodeId> Children { get; }
    public FormaSourceRange SourceRange { get; }

    public string? FindDirective(string name) =>
        Members.FirstOrDefault(member => member.IsDirective && member.Name == name)?.Value.RawText;

    public string? FindMember(string name) =>
        Members.FirstOrDefault(member => !member.IsDirective && member.Name == name)?.Value.RawText;
}

public enum FormaLoweredOperationKind
{
    Construct,
    SetMember,
    AddChild,
    Binding,
    ResourceReference,
    Style,
    Trigger,
    Storyboard,
    Transition,
    Template,
    Brush,
    Geometry,
    AttachedProperty,
    AdaptiveCondition,
}

public sealed record FormaLoweredOperation(
    FormaLoweredOperationKind Kind,
    FormaNodeId NodeId,
    int ScopeId,
    FormaSymbolId TypeSymbolId,
    FormaSymbolId MemberSymbolId,
    FormaLoweredValue? Value,
    FormaSourceRange SourceRange);

public sealed class FormaLoweredScope
{
    internal FormaLoweredScope(int scopeId, IEnumerable<FormaLoweredOperation> operations)
    {
        ScopeId = scopeId;
        Operations = Array.AsReadOnly(operations.ToArray());
    }

    public int ScopeId { get; }
    public ReadOnlyCollection<FormaLoweredOperation> Operations { get; }
}

public sealed record FormaLoweredTemplate(
    FormaNodeId NodeId,
    FormaXamlTemplateKind Kind,
    FormaSymbolId TargetTypeSymbolId,
    FormaSymbolId DataTypeSymbolId,
    FormaLoweredScope Scope,
    FormaSourceRange SourceRange);

public sealed class FormaLoweredDocument
{
    internal FormaLoweredDocument(
        string source,
        string sourcePath,
        FormaNodeId rootNodeId,
        string? rootClass,
        string? dataType,
        IReadOnlyDictionary<string, string> namespaces,
        IEnumerable<FormaLoweredNode> nodes,
        FormaLoweredScope ownerScope,
        IEnumerable<FormaLoweredTemplate> templates,
        IEnumerable<FormaLoweredSymbol> symbols,
        string canonicalSource)
    {
        Source = source;
        CanonicalSource = canonicalSource;
        SourcePath = sourcePath;
        RootNodeId = rootNodeId;
        RootClass = rootClass;
        DataType = dataType;
        Namespaces = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(namespaces, StringComparer.Ordinal));
        Nodes = Array.AsReadOnly(nodes.ToArray());
        OwnerScope = ownerScope;
        Templates = Array.AsReadOnly(templates.ToArray());
        Symbols = Array.AsReadOnly(symbols.ToArray());
    }

    public string Source { get; }
    public string CanonicalSource { get; }
    public string SourcePath { get; }
    public FormaNodeId RootNodeId { get; }
    public string? RootClass { get; }
    public string? DataType { get; }
    public ReadOnlyDictionary<string, string> Namespaces { get; }
    public ReadOnlyCollection<FormaLoweredNode> Nodes { get; }
    public FormaLoweredScope OwnerScope { get; }
    public ReadOnlyCollection<FormaLoweredTemplate> Templates { get; }
    public ReadOnlyCollection<FormaLoweredSymbol> Symbols { get; }
}

public sealed class FormaXamlLowerer
{
    private readonly Dictionary<(FormaLoweredSymbolKind Kind, string Namespace, string Owner, string Name), FormaSymbolId> _symbolIds = [];
    private readonly List<FormaLoweredSymbol> _symbols = [];
    private readonly Dictionary<FormaXamlObject, FormaNodeId> _nodeIds = [];

    public FormaLoweredDocument Lower(string source, FormaXamlDocument document) => Lower(source, document, source);

    public FormaLoweredDocument Lower(string source, FormaXamlDocument document, string canonicalSource)
    {
        if (AuthoringSourceText.ParserText(canonicalSource) != source)
            throw new ArgumentException("Canonical source must project exactly to the parsed compiler input.", nameof(canonicalSource));
        _symbolIds.Clear();
        _symbols.Clear();
        _nodeIds.Clear();

        var nodes = document.DescendantsAndSelf().ToArray();
        for (var index = 0; index < nodes.Length; index++) _nodeIds.Add(nodes[index], new FormaNodeId(index));

        foreach (var node in nodes)
        {
            _ = Intern(FormaLoweredSymbolKind.Type, node.XmlNamespace, string.Empty, node.TypeName);
            foreach (var member in node.Members)
                _ = Intern(FormaLoweredSymbolKind.Member, node.XmlNamespace, node.TypeName, member.Name);
        }

        var loweredNodes = nodes.Select(node => new FormaLoweredNode(
            _nodeIds[node],
            node.Parent == null ? null : _nodeIds[node.Parent],
            node.ScopeId,
            node.XmlNamespace,
            node.TypeName,
            Intern(FormaLoweredSymbolKind.Type, node.XmlNamespace, string.Empty, node.TypeName),
            node.TemplateKind,
            node.TypeName == "Style" && node.FindMember("Selector") is { } selector ? StyleSelector.Parse(selector) : null,
            node.Members.Select(member => new FormaLoweredMember(
                Intern(FormaLoweredSymbolKind.Member, node.XmlNamespace, node.TypeName, member.Name),
                member.Namespace,
                member.Name,
                ParseValue(member.Value, document, node),
                member.IsDirective,
                Range(member.Location))),
            node.Children.Select(child => _nodeIds[child]),
            Range(node.Location))).ToArray();

        var ownerOperations = LowerScope(document.Root, 0, document);
        var templates = document.Templates.Select(template => new FormaLoweredTemplate(
            _nodeIds[template],
            template.TemplateKind,
            ResolveTypeReference(document, template.FindMember("TargetType")),
            ResolveTypeReference(document, template.FindDirective("DataType")),
            new FormaLoweredScope(template.ScopeId, LowerScope(template, template.ScopeId, document)),
            Range(template.Location))).ToArray();
        return new FormaLoweredDocument(
            source,
            document.SourcePath,
            _nodeIds[document.Root],
            document.RootClass,
            document.DataType,
            document.Namespaces,
            loweredNodes,
            new FormaLoweredScope(0, ownerOperations),
            templates,
            _symbols,
            canonicalSource);
    }

    private IEnumerable<FormaLoweredOperation> LowerScope(FormaXamlObject root, int scopeId, FormaXamlDocument document)
    {
        var operations = new List<FormaLoweredOperation>();
        var stack = new Stack<FormaXamlObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.ScopeId != scopeId) continue;
            var nodeId = _nodeIds[node];
            var typeSymbol = Intern(FormaLoweredSymbolKind.Type, node.XmlNamespace, string.Empty, node.TypeName);
            operations.Add(new FormaLoweredOperation(ClassifyNode(node), nodeId, scopeId, typeSymbol, FormaSymbolId.None, null, Range(node.Location)));
            foreach (var member in node.Members.Where(member => !member.IsDirective))
            {
                var memberSymbol = Intern(FormaLoweredSymbolKind.Member, node.XmlNamespace, node.TypeName, member.Name);
                operations.Add(new FormaLoweredOperation(ClassifyMember(member), nodeId, scopeId, typeSymbol, memberSymbol, ParseValue(member.Value, document, node), Range(member.Location)));
            }
            foreach (var child in node.Children.Where(child => child.ScopeId == scopeId))
                operations.Add(new FormaLoweredOperation(FormaLoweredOperationKind.AddChild, _nodeIds[child], scopeId, typeSymbol, FormaSymbolId.None, null, Range(child.Location)));
            for (var index = node.Children.Count - 1; index >= 0; index--) stack.Push(node.Children[index]);
        }
        return operations;
    }

    private FormaSymbolId ResolveTypeReference(FormaXamlDocument document, string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return FormaSymbolId.None;
        var separator = reference.IndexOf(':');
        var prefix = separator < 0 ? string.Empty : reference.Substring(0, separator);
        var name = separator < 0 ? reference : reference.Substring(separator + 1);
        var xmlNamespace = document.Namespaces.TryGetValue(prefix, out var value) ? value : Forma.Xaml.XamlNamespaces.Forma;
        return Intern(FormaLoweredSymbolKind.Type, xmlNamespace, string.Empty, name);
    }

    private FormaSymbolId Intern(FormaLoweredSymbolKind kind, string xmlNamespace, string owner, string name)
    {
        var key = (kind, xmlNamespace, owner, name);
        if (_symbolIds.TryGetValue(key, out var id)) return id;
        id = new FormaSymbolId(_symbols.Count);
        _symbolIds.Add(key, id);
        _symbols.Add(new FormaLoweredSymbol(id, kind, xmlNamespace, owner, name));
        return id;
    }

    private static FormaLoweredOperationKind ClassifyNode(FormaXamlObject node) => node.TypeName switch
    {
        "Style" => FormaLoweredOperationKind.Style,
        "Storyboard" => FormaLoweredOperationKind.Storyboard,
        "EventTrigger" or "PropertyTrigger" => FormaLoweredOperationKind.Trigger,
        "Transition" => FormaLoweredOperationKind.Transition,
        _ when node.TypeName.EndsWith("Transition", StringComparison.Ordinal) => FormaLoweredOperationKind.Transition,
        "DataTemplate" or "ControlTemplate" or "ItemsPanelTemplate" => FormaLoweredOperationKind.Template,
        "AdaptiveCondition" => FormaLoweredOperationKind.AdaptiveCondition,
        _ when node.TypeName.EndsWith("Brush", StringComparison.Ordinal) => FormaLoweredOperationKind.Brush,
        _ when node.TypeName.EndsWith("Geometry", StringComparison.Ordinal) => FormaLoweredOperationKind.Geometry,
        _ => FormaLoweredOperationKind.Construct,
    };

    private static FormaLoweredOperationKind ClassifyMember(FormaXamlMember member)
    {
        if (member.Value.StartsWith("{Binding", StringComparison.Ordinal)) return FormaLoweredOperationKind.Binding;
        if (member.Value.StartsWith("{StaticResource", StringComparison.Ordinal) || member.Value.StartsWith("{DynamicResource", StringComparison.Ordinal))
            return FormaLoweredOperationKind.ResourceReference;
        return member.Name.Contains('.', StringComparison.Ordinal)
            ? FormaLoweredOperationKind.AttachedProperty
            : FormaLoweredOperationKind.SetMember;
    }

    private FormaLoweredValue ParseValue(string value, FormaXamlDocument document, FormaXamlObject node)
    {
        if (value.StartsWith("{Binding", StringComparison.Ordinal) && value.EndsWith('}'))
        {
            var body = value.Substring("{Binding".Length, value.Length - "{Binding".Length - 1).Trim();
            var parts = SplitArguments(body);
            var path = parts.Count == 0 ? string.Empty : parts[0];
            if (path.StartsWith("Path=", StringComparison.Ordinal)) path = path.Substring("Path=".Length).Trim();
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var part in parts.Skip(1))
            {
                var separator = part.IndexOf('=');
                if (separator > 0) options[part.Substring(0, separator).Trim()] = part.Substring(separator + 1).Trim();
            }
            return new FormaBindingValue(value, path, new ReadOnlyDictionary<string, string>(options), ResolveBindingSource(document, node, options));
        }

        var dynamic = value.StartsWith("{DynamicResource ", StringComparison.Ordinal);
        var prefix = dynamic ? "{DynamicResource " : "{StaticResource ";
        if (value.StartsWith(prefix, StringComparison.Ordinal) && value.EndsWith('}'))
            return new FormaResourceValue(value, value.Substring(prefix.Length, value.Length - prefix.Length - 1).Trim(), dynamic);
        return new FormaLiteralValue(value);
    }

    private FormaBindingSource ResolveBindingSource(
        FormaXamlDocument document,
        FormaXamlObject node,
        IReadOnlyDictionary<string, string> options)
    {
        var source = options.GetValueOrDefault("RelativeSource");
        if (source == "Self")
            return new FormaBindingSource(FormaBindingSourceKind.Self,
                Intern(FormaLoweredSymbolKind.Type, node.XmlNamespace, string.Empty, node.TypeName), 1);
        if (source == "TemplatedParent")
        {
            var template = AncestorsAndSelf(node).OfType<FormaXamlTemplateObject>()
                .FirstOrDefault(candidate => candidate.TemplateKind == FormaXamlTemplateKind.Control);
            return new FormaBindingSource(FormaBindingSourceKind.TemplatedParent,
                template == null ? FormaSymbolId.None : ResolveTypeReference(document, template.FindMember("TargetType")), 1);
        }
        if (source == "FindAncestor")
            return new FormaBindingSource(FormaBindingSourceKind.FindAncestor,
                ResolveTypeReference(document, options.GetValueOrDefault("AncestorType")),
                int.TryParse(options.GetValueOrDefault("AncestorLevel"), out var level) ? level : 1);
        return new FormaBindingSource(FormaBindingSourceKind.DataContext, FormaSymbolId.None, 1);
    }

    private static IEnumerable<FormaXamlObject> AncestorsAndSelf(FormaXamlObject node)
    {
        for (var current = node; current != null; current = current.Parent) yield return current;
    }

    private static List<string> SplitArguments(string value)
    {
        var result = new List<string>();
        var start = 0;
        var quote = '\0';
        var depth = 0;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (quote != '\0') { if (character == quote) quote = '\0'; continue; }
            if (character is '\'' or '"') { quote = character; continue; }
            if (character == '{') depth++;
            else if (character == '}') depth--;
            else if (character == ',' && depth == 0) { result.Add(value.Substring(start, index - start).Trim()); start = index + 1; }
        }
        result.Add(value.Substring(start).Trim());
        return result;
    }

    private static FormaSourceRange Range(FormaSourceLocation location) => new(
        location.FilePath,
        location.Line,
        location.Column,
        location.EndLine == 0 ? location.Line : location.EndLine,
        location.EndColumn == 0 ? location.Column : location.EndColumn);
}