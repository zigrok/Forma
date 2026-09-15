// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Text.RegularExpressions;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using Forma.Xaml;

namespace Forma.Xaml.Compiler;

public sealed class FormaXamlParser
{
    private static readonly HashSet<string> SupportedDirectives = new(StringComparer.Ordinal)
    {
        "Class", "Name", "Key", "DataType",
    };
    private static readonly HashSet<string> BindingOptions = new(StringComparer.Ordinal)
    {
        "Mode", "FallbackValue", "TargetNullValue", "StringFormat", "Converter", "ConverterParameter", "UpdateSourceTrigger",
        "RelativeSource", "AncestorType", "AncestorLevel",
    };
    private static readonly HashSet<string> SemanticTemplatedTypes = new(StringComparer.Ordinal)
    {
        "AcceptDialog", "BaseButton", "Button", "CheckBox", "CheckButton", "CodeEdit", "ColorPicker",
        "ColorPickerButton", "ColorPickerDialog", "ColorPickerPopupPanel", "ColorPresetButton", "ConfirmationDialog",
        "FileDialog", "FoldableContainer", "GraphEdit", "GraphElement", "GraphFrame", "GraphNode", "HScrollBar",
        "HSplitContainer", "HSlider", "ItemList", "LineEdit", "LinkButton", "MenuBar", "MenuButton", "OptionButton",
        "ItemsControl", "Popup", "PopupMenu", "PopupPanel", "ProgressBar", "RichTextDocument", "RichTextLabel", "ScrollBar",
        "ScrollContainer", "Slider", "SpinBox", "SplitContainer", "SubViewportContainer", "TabBar", "TabContainer",
        "TextEdit", "TextureButton", "TextureProgressBar", "Tree", "VScrollBar", "VirtualJoystick", "VSlider",
        "VSplitContainer",
    };
    private static readonly Regex NamePattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    public FormaXamlParseResult Parse(string source, string sourcePath, FormaXamlParseOptions? options = null)
    {
        options ??= new FormaXamlParseOptions();
        var diagnostics = new List<FormaDiagnostic>();
        XDocument xml;
        try
        {
            xml = XDocument.Parse(source, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
        }
        catch (XmlException exception)
        {
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.XmlSyntax, exception.Message, sourcePath, exception.LineNumber, exception.LinePosition));
            return new FormaXamlParseResult(null, diagnostics);
        }

        if (xml.Root == null)
        {
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.XmlSyntax, "A XAML document requires one root element.", sourcePath, 1, 1));
            return new FormaXamlParseResult(null, diagnostics);
        }

        var namespaces = xml.Root.Attributes().Where(attribute => attribute.IsNamespaceDeclaration)
            .ToDictionary(attribute => attribute.Name.LocalName == "xmlns" ? string.Empty : attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);
        var nextScopeId = 0;
        var root = BuildObject(xml.Root, sourcePath, diagnostics, true, 0, ref nextScopeId);
        var document = new FormaXamlDocument(sourcePath, root, namespaces);
        Validate(document, options, diagnostics);
        return new FormaXamlParseResult(document, diagnostics);
    }

    private static FormaXamlObject BuildObject(
        XElement element,
        string sourcePath,
        List<FormaDiagnostic> diagnostics,
        bool isRoot,
        int inheritedScopeId,
        ref int nextScopeId)
    {
        var location = Location(element, sourcePath);
        var templateKind = GetTemplateKind(element.Name.LocalName);
        var scopeId = templateKind == FormaXamlTemplateKind.None ? inheritedScopeId : ++nextScopeId;
        FormaXamlObject result = templateKind == FormaXamlTemplateKind.None
            ? new FormaXamlObject(element.Name.NamespaceName, element.Name.LocalName, location)
            : new FormaXamlTemplateObject(element.Name.NamespaceName, element.Name.LocalName, location, templateKind);
        result.ScopeId = scopeId;
        foreach (var attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
        {
            var directive = attribute.Name.NamespaceName == XamlNamespaces.Xaml2006;
            result.Members.Add(new FormaXamlMember(attribute.Name.NamespaceName, attribute.Name.LocalName, attribute.Value, Location(attribute, sourcePath), directive));
            if (directive && !SupportedDirectives.Contains(attribute.Name.LocalName))
                diagnostics.Add(Diagnostic(FormaDiagnosticCodes.UnsupportedDirective, $"Directive 'x:{attribute.Name.LocalName}' is not supported in Forma XAML v1.", Location(attribute, sourcePath)));
            if (directive && attribute.Name.LocalName == "Class" && !isRoot)
                diagnostics.Add(Diagnostic(FormaDiagnosticCodes.InvalidDirective, "x:Class is valid only on the document root.", Location(attribute, sourcePath)));
        }
        foreach (var child in element.Elements())
        {
            var childObject = BuildObject(child, sourcePath, diagnostics, false, scopeId, ref nextScopeId);
            childObject.Parent = result;
            result.Children.Add(childObject);
        }
        return result;
    }

    private static void Validate(FormaXamlDocument document, FormaXamlParseOptions options, List<FormaDiagnostic> diagnostics)
    {
        if (document.Root.XmlNamespace != XamlNamespaces.Forma && !document.Root.XmlNamespace.StartsWith("clr-namespace:", StringComparison.Ordinal))
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.RootNamespace, $"Root namespace '{document.Root.XmlNamespace}' is not a Forma or CLR namespace.", document.Root.Location));

        var namesByScope = new Dictionary<int, Dictionary<string, FormaSourceLocation>>();
        ValidateObject(document.Root, document, options, diagnostics, namesByScope, document.DataType);
        ValidateResources(document, diagnostics);

        foreach (var timeline in document.DescendantsAndSelf().Where(node => node.TypeName.EndsWith("Timeline", StringComparison.Ordinal)))
        {
            var target = timeline.FindMember("TargetName");
            var property = timeline.FindMember("Property");
            if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(property))
                diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Storyboard, "A timeline requires TargetName and Property.", timeline.Location));
            else if (!ScopeContainsName(namesByScope, timeline.ScopeId, target))
                diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Storyboard, $"Storyboard target '{target}' was not found in the local namescope.", timeline.Location));
            if (!timeline.Children.Any(child => child.TypeName == "KeyFrame"))
                diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Storyboard, "A timeline requires at least one KeyFrame.", timeline.Location));
        }

        foreach (var trigger in document.DescendantsAndSelf().Where(node => node.TypeName == "EventTrigger"))
        {
            var sourceName = trigger.FindMember("SourceName");
            if (!string.IsNullOrWhiteSpace(sourceName) && !ScopeContainsName(namesByScope, trigger.ScopeId, sourceName))
                diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Trigger, $"Trigger source '{sourceName}' was not found in the local namescope.", trigger.Location));
        }
    }

    private static void ValidateResources(FormaXamlDocument document, List<FormaDiagnostic> diagnostics)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dictionary in document.DescendantsAndSelf().Where(node => node.TypeName == "ResourceDictionary"))
        {
            var localKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in dictionary.Children.Where(child => !child.TypeName.Contains('.')))
            {
                var key = entry.FindDirective("Key");
                if (string.IsNullOrWhiteSpace(key))
                {
                    diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Resource, $"Resource '{entry.TypeName}' requires x:Key.", entry.Location));
                    continue;
                }
                keys.Add(key);
                if (!localKeys.Add(key))
                    diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Resource, $"Resource key '{key}' is duplicated in one dictionary.", entry.Location));
            }
        }

        foreach (var node in document.DescendantsAndSelf())
            foreach (var member in node.Members.Where(member => !member.IsDirective))
            {
                const string prefix = "{StaticResource ";
                if (!member.Value.StartsWith(prefix, StringComparison.Ordinal) || !member.Value.EndsWith('}')) continue;
                var key = member.Value.Substring(prefix.Length, member.Value.Length - prefix.Length - 1).Trim();
                if (key.Length == 0 || !keys.Contains(key))
                    diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Resource, $"Static resource '{key}' was not found in this XAML document.", member.Location));
            }
    }

    private static void ValidateObject(
        FormaXamlObject node,
        FormaXamlDocument document,
        FormaXamlParseOptions options,
        List<FormaDiagnostic> diagnostics,
        Dictionary<int, Dictionary<string, FormaSourceLocation>> namesByScope,
        string? inheritedDataType)
    {
        if (!namesByScope.TryGetValue(node.ScopeId, out var names))
        {
            names = new Dictionary<string, FormaSourceLocation>(StringComparer.Ordinal);
            namesByScope.Add(node.ScopeId, names);
        }
        var name = node.FindDirective("Name");
        if (name != null)
        {
            if (!NamePattern.IsMatch(name)) diagnostics.Add(Diagnostic(FormaDiagnosticCodes.InvalidName, $"'{name}' is not a valid XAML name.", node.Location));
            else if (!names.TryAdd(name, node.Location)) diagnostics.Add(Diagnostic(FormaDiagnosticCodes.DuplicateName, $"Name '{name}' is already registered in this namescope.", node.Location));
        }

        var dataType = node.FindDirective("DataType") ?? (node.TemplateKind == FormaXamlTemplateKind.None ? inheritedDataType : null);
        foreach (var member in node.Members.Where(member => !member.IsDirective && member.Value.StartsWith("{Binding", StringComparison.Ordinal)))
            ValidateBinding(node, member, dataType, document, options, diagnostics);

        if (node is FormaXamlTemplateObject template)
            ValidateTemplate(template, document, options, diagnostics);
        ValidateContentModel(node, document, options, diagnostics);
        ValidateDataGrid(node, options, diagnostics);

        if (node.TypeName == "Style")
        {
            var selectorMember = node.Members.FirstOrDefault(member => !member.IsDirective && member.Name == "Selector");
            var selector = selectorMember?.Value;
            if (string.IsNullOrWhiteSpace(selector)) diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Selector, "Style requires a Selector.", node.Location));
            else
            {
                try
                {
                    var parsed = StyleSelector.Parse(selector);
                    foreach (var arm in parsed.Arms)
                    {
                        foreach (var compound in arm.Compounds)
                        {
                            if (string.IsNullOrWhiteSpace(compound.TypeName)) continue;
                            var candidates = ResolveSelectorTypeReferences(compound.TypeName, document, options);
                            if (candidates.Length > 1)
                                diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Selector,
                                    $"Selector type '{compound.TypeName}' is ambiguous; qualify it with an XML namespace prefix.", selectorMember!.Location));
                            else if (candidates.Length == 0 &&
                                !ShouldDeferSelectorTypeReference(compound.TypeName, document, options))
                                diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Selector,
                                    $"Selector type '{compound.TypeName}' could not be resolved.", selectorMember!.Location));
                        }
                        foreach (var compound in arm.Compounds)
                            ValidateSelectorPseudoStates(compound, typeof(Control), document, options, diagnostics, selectorMember!.Location);
                        for (var index = 0; index < arm.Combinators.Count; index++)
                        {
                            if (arm.Combinators[index] != StyleSelectorCombinator.TemplateChild) continue;
                            var typeName = arm.Compounds[index].TypeName;
                            if (string.IsNullOrWhiteSpace(typeName)) continue;
                            var type = ResolveSelectorTypeReference(typeName, document, options);
                            if (type != null && !typeof(TemplatedControl).IsAssignableFrom(type) && !IsSemanticTemplatedType(typeName))
                                diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Selector,
                                    $"Selector template crossing requires a TemplatedControl on the left, but '{typeName}' is foundational.", selectorMember!.Location));
                        }
                    }
                }
                catch (FormatException exception) { diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Selector, exception.Message, node.Location)); }
            }
        }
        if (node.TypeName == "Setter" &&
            (string.IsNullOrWhiteSpace(node.FindMember("Property")) || string.IsNullOrWhiteSpace(node.FindMember("Value"))))
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Selector, "Setter requires Property and Value.", node.Location));
        if (node.TypeName.EndsWith("Transition", StringComparison.Ordinal) && node.TypeName != "Transition" &&
            (string.IsNullOrWhiteSpace(node.FindMember("Property")) || string.IsNullOrWhiteSpace(node.FindMember("Duration"))))
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Selector, $"{node.TypeName} requires Property and Duration.", node.Location));
        if (node.TypeName == "ItemsControl" && node.FindMember("ItemsSource") != null && node.FindMember("ItemTemplate") == null &&
            !node.Children.Any(child => child.TypeName == "ItemsControl.ItemTemplate"))
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Template,
                "ItemsControl with ItemsSource requires an explicit ItemTemplate property value.", node.Location));

        if (node.TypeName is "PropertyTrigger" && node.FindMember("Binding") == null)
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Trigger, "PropertyTrigger requires a Binding.", node.Location));
        if (node.TypeName is "EventTrigger" && (node.FindMember("Event") == null || node.FindMember("SourceName") == null))
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Trigger, "EventTrigger requires SourceName and Event.", node.Location));

        foreach (var child in node.Children) ValidateObject(child, document, options, diagnostics, namesByScope, dataType);
    }

    private static void ValidateDataGrid(
        FormaXamlObject node,
        FormaXamlParseOptions options,
        List<FormaDiagnostic> diagnostics)
    {
        var gridType = ResolveType(node.XmlNamespace, node.TypeName, options);
        if (gridType == null || !typeof(DataGrid).IsAssignableFrom(gridType)) return;
        var columns = node.Children.FirstOrDefault(child => child.TypeName == "DataGrid.Columns");
        IEnumerable<FormaXamlObject> columnNodes = columns == null ? Array.Empty<FormaXamlObject>() : columns.Children;
        var expanderCount = 0;
        var hasUnresolvedColumn = false;
        foreach (var columnNode in columnNodes)
        {
            var columnType = ResolveType(columnNode.XmlNamespace, columnNode.TypeName, options);
            if (columnType == null)
            {
                hasUnresolvedColumn = true;
                continue;
            }
            if (!typeof(DataGridColumn).IsAssignableFrom(columnType))
            {
                diagnostics.Add(Diagnostic(FormaDiagnosticCodes.DataGrid,
                    $"DataGrid.Columns accepts only DataGridColumn values; '{columnNode.TypeName}' is invalid.", columnNode.Location));
                continue;
            }
            if (typeof(DataGridExpanderColumn).IsAssignableFrom(columnType))
            {
                expanderCount++;
                if (columnType == typeof(DataGridExpanderColumn) && columnNode.FindMember("Children") == null &&
                    !columnNode.Children.Any(child => child.TypeName == "DataGridExpanderColumn.Children"))
                    diagnostics.Add(Diagnostic(FormaDiagnosticCodes.DataGrid,
                        "DataGridExpanderColumn requires a typed Children binding.", columnNode.Location));
            }
            if (columnType.BaseType == typeof(DataGridColumn) && columnType != typeof(DataGridExpanderColumn) &&
                !string.Equals(columnNode.FindMember("CanUserSort"), "False", StringComparison.OrdinalIgnoreCase) &&
                columnNode.FindMember("SortBinding") == null)
                diagnostics.Add(Diagnostic(FormaDiagnosticCodes.DataGrid,
                    $"Sortable {columnNode.TypeName} requires a typed SortBinding; set CanUserSort=False when sorting is disabled.", columnNode.Location));
        }

        var hierarchical = string.Equals(node.FindMember("Mode"), nameof(DataGridMode.Hierarchical), StringComparison.OrdinalIgnoreCase);
        if (hierarchical && !hasUnresolvedColumn && expanderCount != 1)
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.DataGrid,
                "Hierarchical DataGrid requires exactly one DataGridExpanderColumn with a typed Children binding.", node.Location));
        else if (!hierarchical && !hasUnresolvedColumn && expanderCount != 0)
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.DataGrid,
                "DataGridExpanderColumn is valid only when DataGrid Mode is Hierarchical.", node.Location));
    }

    private static void ValidateTemplate(
        FormaXamlTemplateObject template,
        FormaXamlDocument document,
        FormaXamlParseOptions options,
        List<FormaDiagnostic> diagnostics)
    {
        var roots = template.ContentRoots.ToArray();
        if (roots.Length != 1)
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Template, $"{template.TypeName} requires exactly one root element.", template.Location));

        var root = roots.FirstOrDefault();
        var rootType = root == null ? null : ResolveNodeType(root, document, options);
        if (template.TemplateKind == FormaXamlTemplateKind.ItemsPanel)
        {
            if (rootType != null && !typeof(Container).IsAssignableFrom(rootType))
                diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Template, "ItemsPanelTemplate requires a Container root.", root!.Location));
            return;
        }

        if (rootType != null && !typeof(Control).IsAssignableFrom(rootType))
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Template, $"{template.TypeName} requires a Control root.", root!.Location));

        if (template.TemplateKind == FormaXamlTemplateKind.Data)
        {
            var dataTypeName = template.FindDirective("DataType");
            var hasLocalBinding = DescendantsInScope(template).Any(node =>
                node.Members.Any(member => !member.IsDirective && member.Value.StartsWith("{Binding", StringComparison.Ordinal)));
            if (hasLocalBinding && string.IsNullOrWhiteSpace(dataTypeName))
                diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Template, "DataTemplate requires x:DataType when it contains bindings.", template.Location));
            else if (!string.IsNullOrWhiteSpace(dataTypeName) && ResolveTypeReference(dataTypeName, document, options) == null &&
                !ShouldDeferTypeReference(dataTypeName, document, options))
                diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Template, $"DataTemplate x:DataType '{dataTypeName}' could not be resolved.", template.Location));
            foreach (var node in DescendantsInScope(template).Skip(1))
            {
                var nodeType = ResolveNodeType(node, document, options);
                if (nodeType == null) continue;
                foreach (var member in node.Members.Where(member => !member.IsDirective && nodeType.GetEvent(member.Name) != null))
                    diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Template,
                        $"Event attribute '{member.Name}' is not allowed directly inside DataTemplate; instantiate a separate x:Class row control and handle the event there.",
                        member.Location));
            }
            return;
        }

        var targetTypeName = template.FindMember("TargetType");
        if (string.IsNullOrWhiteSpace(targetTypeName))
        {
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Template, "ControlTemplate requires TargetType.", template.Location));
            return;
        }
        var targetType = ResolveTypeReference(targetTypeName, document, options);
        if (targetType == null)
        {
            if (!ShouldDeferTypeReference(targetTypeName, document, options))
                diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Template, $"ControlTemplate TargetType '{targetTypeName}' could not be resolved.", template.Location));
        }
        else if (!typeof(TemplatedControl).IsAssignableFrom(targetType) && !IsSemanticTemplatedType(targetTypeName))
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Template, $"ControlTemplate TargetType '{targetTypeName}' must derive from TemplatedControl.", template.Location));
    }

    private static void ValidateContentModel(
        FormaXamlObject node,
        FormaXamlDocument document,
        FormaXamlParseOptions options,
        List<FormaDiagnostic> diagnostics)
    {
        if (node.IsPropertyElement)
        {
            ValidatePropertyElement(node, document, options, diagnostics);
            return;
        }

        var nodeType = ResolveNodeType(node, document, options);
        if (nodeType == null) return;
        var content = node.Children.Where(child => !child.IsPropertyElement).ToArray();
        if (typeof(Shape).IsAssignableFrom(nodeType) && content.Length != 0)
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.ContentModel, $"{node.TypeName} cannot contain visual children.", content[0].Location));
        else if ((nodeType == typeof(Border) || nodeType == typeof(Viewbox)) && content.Length > 1)
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.ContentModel, $"{node.TypeName} accepts at most one visual child.", content[1].Location));
        else if (typeof(ContentControl).IsAssignableFrom(nodeType) && content.Length > 1)
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.ContentModel, $"{node.TypeName} accepts at most one content child.", content[1].Location));
        else if ((nodeType == typeof(ContentPresenter) || nodeType == typeof(ItemsPresenter) || nodeType == typeof(ScrollPresenter)) && content.Length != 0)
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.ContentModel, $"{node.TypeName} content must be supplied through its typed presenter properties.", content[0].Location));

        foreach (var member in node.Members.Where(member => !member.IsDirective && member.Name.Contains('.', StringComparison.Ordinal)))
            ValidateAttachedProperty(nodeType, member, document, options, diagnostics);
    }

    private static void ValidatePropertyElement(
        FormaXamlObject node,
        FormaXamlDocument document,
        FormaXamlParseOptions options,
        List<FormaDiagnostic> diagnostics)
    {
        var separator = node.TypeName.LastIndexOf('.');
        if (separator <= 0 || separator == node.TypeName.Length - 1 || node.Parent == null) return;
        var ownerName = node.TypeName.Substring(0, separator);
        var propertyName = node.TypeName.Substring(separator + 1);
        var ownerType = ResolveTypeReference(ownerName, document, options);
        var parentType = ResolveNodeType(node.Parent, document, options);
        if (ownerType == null || parentType == null) return;
        if (!ownerType.IsAssignableFrom(parentType))
        {
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.ContentModel, $"Property element '{node.TypeName}' cannot be applied to {node.Parent.TypeName}.", node.Location));
            return;
        }
        var property = ownerType.GetProperty(propertyName);
        if (property == null)
        {
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.ContentModel, $"Property '{node.TypeName}' was not found.", node.Location));
            return;
        }
        // XAML resource entries are values, not the dictionary's CLR KeyValuePair enumerator.
        var elementType = property.PropertyType == typeof(ResourceDictionary) ? typeof(object) : GetCollectionElementType(property.PropertyType);
        if (elementType == null && node.Children.Count > 1)
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.ContentModel, $"Property '{node.TypeName}' accepts one value.", node.Children[1].Location));
        var expectedType = elementType ?? property.PropertyType;
        foreach (var value in node.Children)
        {
            var valueType = ResolveNodeType(value, document, options);
            if (valueType != null && !expectedType.IsAssignableFrom(valueType))
                diagnostics.Add(Diagnostic(FormaDiagnosticCodes.ContentModel, $"{value.TypeName} is not assignable to {node.TypeName}.", value.Location));
        }
    }

    private static void ValidateAttachedProperty(
        Type targetType,
        FormaXamlMember member,
        FormaXamlDocument document,
        FormaXamlParseOptions options,
        List<FormaDiagnostic> diagnostics)
    {
        var separator = member.Name.LastIndexOf('.');
        var ownerName = member.Name.Substring(0, separator);
        var propertyName = member.Name.Substring(separator + 1);
        var ownerType = ResolveTypeReference(ownerName, document, options);
        if (ownerType == null) return;
        var setter = ownerType.GetMethods().FirstOrDefault(method =>
        {
            if (!method.IsPublic || !method.IsStatic || method.Name != "Set" + propertyName) return false;
            var parameters = method.GetParameters();
            return parameters.Length == 2 && parameters[0].ParameterType.IsAssignableFrom(targetType);
        });
        if (setter == null)
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.AttachedProperty, $"Attached property '{member.Name}' cannot be applied to {targetType.Name}.", member.Location));
    }

    private static IEnumerable<FormaXamlObject> DescendantsInScope(FormaXamlObject root)
    {
        var stack = new Stack<FormaXamlObject>();
        stack.Push(root);
        while (stack.Count != 0)
        {
            var current = stack.Pop();
            if (current.ScopeId != root.ScopeId) continue;
            yield return current;
            for (var index = current.Children.Count - 1; index >= 0; index--) stack.Push(current.Children[index]);
        }
    }

    private static Type? ResolveNodeType(FormaXamlObject node, FormaXamlDocument document, FormaXamlParseOptions options) =>
        node.IsPropertyElement ? null : ResolveType(node.XmlNamespace, node.TypeName, options);

    private static Type? ResolveTypeReference(string reference, FormaXamlDocument document, FormaXamlParseOptions options)
    {
        var separator = reference.IndexOf(':');
        var prefix = separator < 0 ? string.Empty : reference.Substring(0, separator);
        var typeName = separator < 0 ? reference : reference.Substring(separator + 1);
        var xmlNamespace = document.Namespaces.TryGetValue(prefix, out var value) ? value : XamlNamespaces.Forma;
        return ResolveType(xmlNamespace, typeName, options);
    }

    private static Type? ResolveSelectorTypeReference(string reference, FormaXamlDocument document, FormaXamlParseOptions options)
        => ResolveSelectorTypeReferences(reference, document, options) is { Length: 1 } candidates ? candidates[0] : null;

    private static Type[] ResolveSelectorTypeReferences(string reference, FormaXamlDocument document, FormaXamlParseOptions options)
    {
        var resolved = ResolveTypeReference(reference, document, options);
        if (reference.Contains(':')) return resolved == null ? [] : [resolved];
        return document.Namespaces.Values.Append(XamlNamespaces.Forma)
            .Select(xmlNamespace => ResolveType(xmlNamespace, reference, options))
            .Where(type => type != null).Cast<Type>().Distinct().ToArray();
    }

    private static void ValidateSelectorPseudoStates(
        StyleSelectorCompound compound,
        Type fallbackType,
        FormaXamlDocument document,
        FormaXamlParseOptions options,
        List<FormaDiagnostic> diagnostics,
        FormaSourceLocation location)
    {
        var candidateType = string.IsNullOrWhiteSpace(compound.TypeName)
            ? fallbackType
            : ResolveSelectorTypeReference(compound.TypeName, document, options);
        if (candidateType != null)
            foreach (var state in compound.PseudoStates)
            {
                var error = ValidatePseudoState(candidateType, state);
                if (error != null) diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Selector, error, location));
            }
        foreach (var negation in compound.Negations)
            ValidateSelectorPseudoStates(negation, candidateType ?? fallbackType, document, options, diagnostics, location);
    }

    private static string? ValidatePseudoState(Type candidateType, string state)
    {
        var standardOwner = state switch
        {
            "hover" or "focus" or "focus-within" or "disabled" or "selected" or "current" => typeof(Control),
            "pressed" or "checked" => typeof(BaseButton),
            _ => null,
        };
        if (standardOwner != null)
            return standardOwner.IsAssignableFrom(candidateType)
                ? null
                : $"Pseudo state ':{state}' is not available on selector type '{candidateType.Name}'.";

        var registrations = candidateType.Assembly.GetCustomAttributesData().Where(attribute =>
            attribute.AttributeType == typeof(PseudoStateAttribute) && attribute.ConstructorArguments.Count == 4 &&
            string.Equals(attribute.ConstructorArguments[0].Value as string, state, StringComparison.Ordinal)).ToArray();
        if (registrations.Length == 0) return $"Pseudo state ':{state}' is not registered for selector type '{candidateType.Name}'.";
        if (registrations.Length > 1) return $"Pseudo state ':{state}' has duplicate registrations in assembly '{candidateType.Assembly.GetName().Name}'.";

        var registration = registrations[0].ConstructorArguments;
        var ownerType = registration[1].Value as Type;
        var inherited = registration[2].Value is true;
        var providerMember = registration[3].Value as string;
        if (ownerType == null || !(inherited ? ownerType.IsAssignableFrom(candidateType) : ownerType == candidateType))
            return $"Pseudo state ':{state}' is not available on selector type '{candidateType.Name}'.";
        var provider = ownerType.GetMethod(providerMember ?? string.Empty,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [typeof(string)], null);
        if (provider == null || provider.ReturnType != typeof(bool) || !typeof(Control).IsAssignableFrom(ownerType))
            return $"Pseudo state ':{state}' metadata does not agree with its runtime provider '{providerMember}'.";
        return null;
    }

    private static bool ShouldDeferSelectorTypeReference(string reference, FormaXamlDocument document, FormaXamlParseOptions options)
    {
        if (ShouldDeferTypeReference(reference, document, options)) return true;
        if (reference.Contains(':') || options.TypeResolver != null) return false;
        return document.Namespaces.Values.Any(xmlNamespace =>
            xmlNamespace.StartsWith("clr-namespace:", StringComparison.Ordinal) &&
                !xmlNamespace.Substring("clr-namespace:".Length).Split(';').Any(part => part.StartsWith("assembly=", StringComparison.Ordinal)));
    }

    private static bool ShouldDeferTypeReference(string reference, FormaXamlDocument document, FormaXamlParseOptions options)
    {
        if (options.TypeResolver != null) return false;
        var separator = reference.IndexOf(':');
        var prefix = separator < 0 ? string.Empty : reference.Substring(0, separator);
        if (!document.Namespaces.TryGetValue(prefix, out var xmlNamespace) || !xmlNamespace.StartsWith("clr-namespace:", StringComparison.Ordinal))
            return false;
        return !xmlNamespace.Substring("clr-namespace:".Length).Split(';')
            .Any(part => part.StartsWith("assembly=", StringComparison.Ordinal));
    }

    private static Type? ResolveType(string xmlNamespace, string typeName, FormaXamlParseOptions options)
    {
        var resolved = options.TypeResolver?.Invoke(xmlNamespace, typeName);
        if (resolved != null) return resolved;
        if (xmlNamespace == XamlNamespaces.Forma) return typeof(Control).Assembly.GetType("Forma." + typeName, false);
        if (!xmlNamespace.StartsWith("clr-namespace:", StringComparison.Ordinal)) return null;
        var definition = xmlNamespace.Substring("clr-namespace:".Length).Split(';');
        var fullName = definition[0] + "." + typeName;
        var assembly = definition.Skip(1).FirstOrDefault(part => part.StartsWith("assembly=", StringComparison.Ordinal));
        return assembly == null ? Type.GetType(fullName, false) : Type.GetType(fullName + ", " + assembly.Substring("assembly=".Length), false);
    }

    private static Type? GetCollectionElementType(Type type)
    {
        if (type.IsArray) return type.GetElementType();
        foreach (var candidate in type.GetInterfaces().Prepend(type))
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() is var definition &&
                (definition == typeof(ICollection<>) || definition == typeof(IList<>) || definition == typeof(IEnumerable<>)))
                return candidate.GetGenericArguments()[0];
        return null;
    }

    private static bool ScopeContainsName(
        IReadOnlyDictionary<int, Dictionary<string, FormaSourceLocation>> namesByScope,
        int scopeId,
        string name) => namesByScope.TryGetValue(scopeId, out var names) && names.ContainsKey(name);

    private static bool IsSemanticTemplatedType(string typeName)
    {
        var separator = typeName.LastIndexOf(':');
        return SemanticTemplatedTypes.Contains(separator < 0 ? typeName : typeName.Substring(separator + 1));
    }

    private static FormaXamlTemplateKind GetTemplateKind(string typeName) => typeName switch
    {
        "DataTemplate" => FormaXamlTemplateKind.Data,
        "ControlTemplate" => FormaXamlTemplateKind.Control,
        "ItemsPanelTemplate" => FormaXamlTemplateKind.ItemsPanel,
        _ => FormaXamlTemplateKind.None,
    };

    private static void ValidateBinding(
        FormaXamlObject node,
        FormaXamlMember member,
        string? dataType,
        FormaXamlDocument document,
        FormaXamlParseOptions options,
        List<FormaDiagnostic> diagnostics)
    {
        var expression = member.Value;
        if (!expression.EndsWith('}'))
        {
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Binding, "Binding markup extension is missing its closing brace.", member.Location));
            return;
        }
        var body = expression.Substring("{Binding".Length, expression.Length - "{Binding".Length - 1).Trim();
        var parts = SplitArguments(body);
        var bindingOptions = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < parts.Count; index++)
        {
            var separator = parts[index].IndexOf('=');
            if (separator <= 0 || !BindingOptions.Contains(parts[index].Substring(0, separator).Trim()))
                diagnostics.Add(Diagnostic(FormaDiagnosticCodes.Binding, $"Unknown or malformed binding option '{parts[index]}'.", member.Location));
            else
                bindingOptions[parts[index].Substring(0, separator).Trim()] = parts[index].Substring(separator + 1).Trim();
        }
        ValidateRelativeSource(node, bindingOptions, member, document, options, diagnostics);
        if (options.RequireCompiledBindings && string.IsNullOrWhiteSpace(dataType) && !bindingOptions.ContainsKey("RelativeSource"))
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.CompiledBinding, "Compiled bindings require x:DataType on this element or an ancestor.", member.Location));
    }

    private static void ValidateRelativeSource(
        FormaXamlObject node,
        IReadOnlyDictionary<string, string> options,
        FormaXamlMember member,
        FormaXamlDocument document,
        FormaXamlParseOptions parseOptions,
        List<FormaDiagnostic> diagnostics)
    {
        options.TryGetValue("RelativeSource", out var relativeSource);
        var hasAncestorType = options.TryGetValue("AncestorType", out var ancestorType);
        var hasAncestorLevel = options.TryGetValue("AncestorLevel", out var ancestorLevel);
        if (relativeSource == null)
        {
            if (hasAncestorType || hasAncestorLevel)
                diagnostics.Add(Diagnostic(FormaDiagnosticCodes.RelativeSource, "AncestorType and AncestorLevel require RelativeSource=FindAncestor.", member.Location));
            return;
        }
        if (relativeSource is not ("Self" or "TemplatedParent" or "FindAncestor"))
        {
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.RelativeSource, $"RelativeSource '{relativeSource}' is not supported; use Self, TemplatedParent, or FindAncestor.", member.Location));
            return;
        }
        if (relativeSource != "FindAncestor")
        {
            if (hasAncestorType || hasAncestorLevel)
                diagnostics.Add(Diagnostic(FormaDiagnosticCodes.RelativeSource, $"RelativeSource={relativeSource} does not accept AncestorType or AncestorLevel.", member.Location));
            if (relativeSource == "TemplatedParent" && !AncestorsAndSelf(node).OfType<FormaXamlTemplateObject>()
                .Any(template => template.TemplateKind == FormaXamlTemplateKind.Control))
                diagnostics.Add(Diagnostic(FormaDiagnosticCodes.RelativeSource, "RelativeSource=TemplatedParent is valid only inside a ControlTemplate.", member.Location));
            return;
        }
        if (!hasAncestorType || string.IsNullOrWhiteSpace(ancestorType))
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.RelativeSource, "RelativeSource=FindAncestor requires AncestorType.", member.Location));
        else
        {
            var resolvedAncestorType = ResolveTypeReference(ancestorType, document, parseOptions);
            if (resolvedAncestorType == null)
                diagnostics.Add(Diagnostic(FormaDiagnosticCodes.RelativeSource, $"AncestorType '{ancestorType}' could not be resolved.", member.Location));
            else if (!typeof(Control).IsAssignableFrom(resolvedAncestorType))
                diagnostics.Add(Diagnostic(FormaDiagnosticCodes.RelativeSource, $"AncestorType '{ancestorType}' must derive from Control.", member.Location));
        }
        if (hasAncestorLevel && (!int.TryParse(ancestorLevel, out var level) || level < 1))
            diagnostics.Add(Diagnostic(FormaDiagnosticCodes.RelativeSource, "AncestorLevel must be a one-based positive integer.", member.Location));
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

    private static FormaDiagnostic Diagnostic(string code, string message, FormaSourceLocation location) =>
        new(code, FormaDiagnosticSeverity.Error, message, location);

    private static FormaDiagnostic Diagnostic(string code, string message, string sourcePath, int line, int column) =>
        Diagnostic(code, message, new FormaSourceLocation(sourcePath, line, column));

    private static FormaSourceLocation Location(XObject value, string sourcePath)
    {
        var info = (IXmlLineInfo)value;
        var line = info.HasLineInfo() ? info.LineNumber : 1;
        var column = info.HasLineInfo() ? info.LinePosition : 1;
        var width = value switch
        {
            XAttribute attribute => attribute.Name.LocalName.Length + attribute.Value.Length + 3,
            XElement element => element.Name.LocalName.Length + 2,
            _ => 1,
        };
        return new FormaSourceLocation(sourcePath, line, column, line, column + width);
    }
}