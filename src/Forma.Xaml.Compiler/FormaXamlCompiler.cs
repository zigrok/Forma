// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Xml.Linq;
using Forma.Xaml;
using Mono.Cecil;
using XamlX;
using XamlX.Ast;
using XamlX.Emit;
using XamlX.IL;
using XamlX.Parsers;
using XamlX.Transform;
using XamlX.Transform.Transformers;
using XamlX.TypeSystem;

namespace Forma.Xaml.Compiler;

public sealed record FormaCompiledCallbacks(Func<IServiceProvider?, object> Build, Action<IServiceProvider?, object> Populate);

public sealed class FormaXamlCompiler
{
    private readonly IXamlTypeSystem _typeSystem;
    private readonly TransformerConfiguration _configuration;
    private readonly FormaXamlParser _parser = new();
    private readonly string _defaultAssemblyName;
    private readonly string? _projectDirectory;
    private string? _currentSourcePath;
    private bool _useSvgFiles;

    public FormaXamlCompiler(IXamlTypeSystem typeSystem, string defaultAssemblyName, string? projectDirectory = null)
    {
        _typeSystem = typeSystem;
        _defaultAssemblyName = defaultAssemblyName;
        _projectDirectory = projectDirectory == null ? null : Path.GetFullPath(projectDirectory);
        var mappings = new XamlLanguageTypeMappings(typeSystem)
        {
            XmlnsAttributes = { typeSystem.GetType(typeof(XmlnsDefinitionAttribute).FullName!) },
            IAddChild = typeSystem.GetType(typeof(IAddChild).FullName!),
            IAddChildOfT = typeSystem.GetType(typeof(IAddChild<>).FullName!),
        };
        _configuration = new TransformerConfiguration(
            typeSystem,
            typeSystem.FindAssembly(defaultAssemblyName),
            mappings,
            customValueConverter: ConvertValue);
        _configuration.KnownDirectives.Add((XamlNamespaces.Xaml2006, "Class"));
        _configuration.KnownDirectives.Add((XamlNamespaces.Xaml2006, "Name"));
        _configuration.KnownDirectives.Add((XamlNamespaces.Xaml2006, "Key"));
        _configuration.KnownDirectives.Add((XamlNamespaces.Xaml2006, "DataType"));
    }

    public static FormaXamlCompiler CreateSre(string? defaultAssemblyName = null)
    {
        _ = typeof(Control).Assembly;
        _ = typeof(System.ComponentModel.TypeConverterAttribute).Assembly;
        _ = typeof(Uri).Assembly;
        return new FormaXamlCompiler(new SreTypeSystem(), defaultAssemblyName ?? typeof(Control).Assembly.GetName().Name!);
    }

    public FormaXamlParseResult Parse(string source, string sourcePath, FormaXamlParseOptions? options = null) =>
        _parser.Parse(source, sourcePath, options);

    public FormaLoweredDocument Lower(string source, string sourcePath, FormaXamlParseOptions? options = null)
    {
        var result = Parse(source, sourcePath, options);
        if (!result.Success) throw new FormaXamlCompilationException(result.Diagnostics);
        return new FormaXamlLowerer().Lower(source, result.Document!);
    }

    public FormaCompiledCallbacks CompileSre(string source, string sourcePath, FormaXamlParseOptions? options = null)
        => CompileSre(Lower(source, sourcePath, options));

    public FormaCompiledCallbacks CompileSre(FormaLoweredDocument lowered)
    {
        if (_typeSystem is not SreTypeSystem typeSystem) throw new InvalidOperationException("SRE compilation requires SreTypeSystem.");
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"Forma.Xaml.Generated.{Guid.NewGuid():N}"), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("Forma.Xaml.Generated.dll");
        var generated = module.DefineType($"GeneratedView_{Guid.NewGuid():N}", System.Reflection.TypeAttributes.Public);
        var context = module.DefineType($"{generated.Name}Context", System.Reflection.TypeAttributes.NotPublic);
        _currentSourcePath = lowered.SourcePath;
        _useSvgFiles = true;
        var compiler = CreateCompiler();
        var contextType = compiler.CreateContextType(typeSystem.CreateTypeBuilder(context));
        var generatedBuilder = typeSystem.CreateTypeBuilder(generated);
        var source = ProjectForEmission(lowered, null);
        var document = ParseAndTransform(compiler, source);
        compiler.Compile(document, generatedBuilder, contextType, "Populate", "Build", "XamlNamespaceInfo", XamlNamespaces.Forma, new StringFileSource(lowered.SourcePath, lowered.Source));
        CompileSreTemplatePrograms(lowered, typeSystem, generatedBuilder, compiler, contextType);
        var runtimeType = generated.CreateType()!;
        var provider = Expression.Parameter(typeof(IServiceProvider));
        var target = Expression.Parameter(typeof(object));
        var buildMethod = runtimeType.GetMethod("Build")!;
        var populateMethod = runtimeType.GetMethod("Populate")!;
        var emittedBuild = Expression.Lambda<Func<IServiceProvider?, object>>(
            Expression.Convert(Expression.Call(buildMethod, provider), typeof(object)), provider).Compile();
        var emittedPopulate = Expression.Lambda<Action<IServiceProvider?, object>>(
            Expression.Call(populateMethod, provider, Expression.Convert(target, populateMethod.GetParameters()[1].ParameterType)), provider, target).Compile();
        var attach = CreateSreAttachments(lowered, runtimeType);
        object Build(IServiceProvider? serviceProvider)
        {
            var value = emittedBuild(serviceProvider);
            if (value is Control control)
            {
                NameScope.CreateForTree(control);
                attach(control);
            }
            return value;
        }
        void Populate(IServiceProvider? serviceProvider, object value)
        {
            emittedPopulate(serviceProvider, value);
            if (value is Control control)
            {
                NameScope.CreateForTree(control);
                attach(control);
            }
        }
        return new FormaCompiledCallbacks(Build, Populate);
    }

    private static void CompileSreTemplatePrograms(
        FormaLoweredDocument lowered,
        SreTypeSystem typeSystem,
        IXamlTypeBuilder<IXamlILEmitter> generatedBuilder,
        XamlILCompiler compiler,
        IXamlType contextType)
    {
        for (var index = 0; index < lowered.Templates.Count; index++)
        {
            var source = ProjectTemplateForEmission(lowered, lowered.Templates[index]);
            var document = ParseAndTransform(compiler, source);
            compiler.Compile(
                document,
                generatedBuilder,
                contextType,
                $"__TemplatePopulate{index}",
                $"__TemplateBuild{index}",
                $"__TemplateNamespaceInfo{index}",
                XamlNamespaces.Forma,
                new StringFileSource(lowered.SourcePath, lowered.Source));
        }
    }

    public void CompileCecil(string source, string sourcePath, CecilTypeSystem typeSystem, TypeDefinition generatedType, TypeDefinition contextType, FormaXamlParseOptions? options = null, IReadOnlyCollection<string>? eventMemberNames = null)
        => CompileCecil(Lower(source, sourcePath, options), typeSystem, generatedType, contextType, eventMemberNames);

    public void CompileCecil(FormaLoweredDocument lowered, CecilTypeSystem typeSystem, TypeDefinition generatedType, TypeDefinition contextType, IReadOnlyCollection<string>? eventMemberNames = null, IReadOnlyDictionary<int, string>? sourceMetadata = null)
    {
        _currentSourcePath = lowered.SourcePath;
        _useSvgFiles = false;
        var compiler = CreateCompiler();
        var contextBuilder = compiler.CreateContextType(typeSystem.CreateTypeBuilder(contextType));
        var source = ProjectForEmission(lowered, eventMemberNames, sourceMetadata);
        var document = ParseAndTransform(compiler, source);
        compiler.Compile(document, typeSystem.CreateTypeBuilder(generatedType), contextBuilder, "Populate", "Build", "XamlNamespaceInfo", XamlNamespaces.Forma, new StringFileSource(lowered.SourcePath, lowered.Source));
        CompileCecilTemplateFactories(lowered, typeSystem, generatedType, compiler, contextBuilder);
    }

    private static void CompileCecilTemplateFactories(
        FormaLoweredDocument lowered,
        CecilTypeSystem typeSystem,
        TypeDefinition generatedType,
        XamlILCompiler compiler,
        IXamlType contextType)
    {
        for (var index = 0; index < lowered.Templates.Count; index++)
        {
            var template = lowered.Templates[index];
            var source = ProjectTemplateForEmission(lowered, template);
            var document = ParseAndTransform(compiler, source);
            var populateName = $"__TemplatePopulate{index}";
            var buildName = $"__TemplateBuild{index}";
            compiler.Compile(
                document,
                typeSystem.CreateTypeBuilder(generatedType),
                contextType,
                populateName,
                buildName,
                $"__TemplateNamespaceInfo{index}",
                XamlNamespaces.Forma,
                new StringFileSource(lowered.SourcePath, lowered.Source));
            DefineCecilTemplateFactory(lowered, typeSystem, generatedType, template, index, buildName);
        }
    }

    private static void DefineCecilTemplateFactory(
        FormaLoweredDocument lowered,
        CecilTypeSystem typeSystem,
        TypeDefinition generatedType,
        FormaLoweredTemplate template,
        int index,
        string buildName)
    {
        var module = generatedType.Module;
        var formaAssembly = typeSystem.GetAssembly(typeSystem.FindAssembly(typeof(Control).Assembly.GetName().Name!));
        var controlType = module.ImportReference(formaAssembly.MainModule.GetType(typeof(Control).FullName!));
        var containerType = module.ImportReference(formaAssembly.MainModule.GetType(typeof(Container).FullName!));
        var buildContextType = module.ImportReference(formaAssembly.MainModule.GetType(typeof(TemplateBuildContext).FullName!));
        var returnType = template.Kind == FormaXamlTemplateKind.ItemsPanel ? containerType : controlType;
        var factory = new MethodDefinition(
            $"__TemplateFactory{index}",
            Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.HideBySig,
            returnType);
        factory.Parameters.Add(new ParameterDefinition("context", Mono.Cecil.ParameterAttributes.None, buildContextType));
        if (template.Kind == FormaXamlTemplateKind.Data)
            factory.Parameters.Add(new ParameterDefinition("item", Mono.Cecil.ParameterAttributes.None,
                ResolveCecilSymbolType(lowered, typeSystem, module, template.DataTypeSymbolId, module.TypeSystem.Object)));
        else if (template.Kind == FormaXamlTemplateKind.Control)
            factory.Parameters.Add(new ParameterDefinition("owner", Mono.Cecil.ParameterAttributes.None,
                ResolveCecilSymbolType(lowered, typeSystem, module, template.TargetTypeSymbolId, controlType)));
        generatedType.Methods.Add(factory);

        var attach = new MethodDefinition(
            $"__TemplateAttach{index}",
            Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        attach.Parameters.Add(new ParameterDefinition("context", Mono.Cecil.ParameterAttributes.None, buildContextType));
        attach.Parameters.Add(new ParameterDefinition("root", Mono.Cecil.ParameterAttributes.None, controlType));
        attach.Body.Instructions.Add(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ret));
        generatedType.Methods.Add(attach);

        var build = generatedType.Methods.Single(method => method.Name == buildName);
        var root = new Mono.Cecil.Cil.VariableDefinition(returnType);
        factory.Body.Variables.Add(root);
        factory.Body.InitLocals = true;
        var il = factory.Body.GetILProcessor();
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
        il.Emit(Mono.Cecil.Cil.OpCodes.Call, build);
        il.Emit(Mono.Cecil.Cil.OpCodes.Castclass, returnType);
        il.Emit(Mono.Cecil.Cil.OpCodes.Stloc, root);
        if (template.Kind == FormaXamlTemplateKind.Data)
        {
            var itemType = factory.Parameters[1].ParameterType;
            var bindItem = new GenericInstanceMethod(module.ImportReference(formaAssembly.MainModule.GetType(typeof(TemplateBuildContext).FullName!).Methods.Single(method => method.Name == nameof(TemplateBuildContext.BindItem))));
            bindItem.GenericArguments.Add(itemType);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldloc, root);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_1);
            il.Emit(Mono.Cecil.Cil.OpCodes.Callvirt, bindItem);
        }
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloc, root);
        il.Emit(Mono.Cecil.Cil.OpCodes.Call, attach);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldloc, root);
        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
    }

    private static TypeReference ResolveCecilSymbolType(
        FormaLoweredDocument lowered,
        CecilTypeSystem typeSystem,
        ModuleDefinition module,
        FormaSymbolId symbolId,
        TypeReference fallback)
    {
        if (!symbolId.IsResolved) return fallback;
        var symbol = lowered.Symbols[symbolId.Value];
        if (symbol.Namespace == XamlNamespaces.Forma)
        {
            var formaAssembly = typeSystem.GetAssembly(typeSystem.FindAssembly(typeof(Control).Assembly.GetName().Name!));
            return module.ImportReference(formaAssembly.MainModule.GetType($"Forma.{symbol.Name}") ?? formaAssembly.MainModule.GetType($"Forma.Xaml.{symbol.Name}"));
        }
        if (!symbol.Namespace.StartsWith("clr-namespace:", StringComparison.Ordinal)) return fallback;
        var definition = symbol.Namespace.Substring("clr-namespace:".Length).Split(';');
        var assemblyName = definition.Skip(1).FirstOrDefault(value => value.StartsWith("assembly=", StringComparison.Ordinal))?.Substring("assembly=".Length)
            ?? module.Assembly.Name.Name;
        var assembly = assemblyName == module.Assembly.Name.Name
            ? module.Assembly
            : typeSystem.GetAssembly(typeSystem.FindAssembly(assemblyName));
        var type = assembly.MainModule.GetType($"{definition[0]}.{symbol.Name}");
        return type == null ? fallback : module.ImportReference(type);
    }

    private XamlILCompiler CreateCompiler()
    {
        var compiler = new XamlILCompiler(_configuration, new XamlLanguageEmitMappings<IXamlILEmitter, XamlILNodeEmitResult>(), true)
        {
            EnableIlVerification = true,
        };
        var directiveIndex = compiler.Transformers.FindIndex(transformer => transformer is KnownDirectivesTransformer);
        compiler.Transformers.Insert(directiveIndex + 1, new FormaDirectiveTransformer());
        var constructionIndex = compiler.Transformers.FindIndex(transformer => transformer is ConstructableObjectTransformer);
        compiler.Transformers.Insert(constructionIndex, new FormaDirectiveTransformer());
        compiler.SimplificationTransformers.Insert(0, new FormaDirectiveTransformer());
        compiler.Emitters.Insert(0, new FormaDirectiveEmitter());
        return compiler;
    }

    private static XamlDocument ParseAndTransform(XamlILCompiler compiler, string source)
    {
        var document = XDocumentXamlParser.Parse(source);
        compiler.Transform(document);
        document.Root = document.Root.Visit(new RemoveDirectivesVisitor());
        return document;
    }

    private static string ProjectForEmission(FormaLoweredDocument lowered, IReadOnlyCollection<string>? eventMemberNames, IReadOnlyDictionary<int, string>? sourceMetadata = null)
    {
        var xml = XDocument.Parse(lowered.Source, LoadOptions.PreserveWhitespace);
        var elements = xml.Root!.DescendantsAndSelf().ToArray();
        if (elements.Length != lowered.Nodes.Count)
            throw new InvalidOperationException("The semantic and structured XAML trees do not have matching node counts.");

        var deferredNodes = lowered.OwnerScope.Operations
            .Where(operation => operation.Kind is FormaLoweredOperationKind.Style or FormaLoweredOperationKind.Storyboard or
                FormaLoweredOperationKind.Trigger or FormaLoweredOperationKind.Transition or FormaLoweredOperationKind.AdaptiveCondition or
                FormaLoweredOperationKind.Template)
            .Select(operation => operation.NodeId.Value)
            .ToHashSet();
        deferredNodes.UnionWith(lowered.Templates.Select(template => template.NodeId.Value));
        var deferredMembers = lowered.OwnerScope.Operations
            .Where(operation => operation.Kind is FormaLoweredOperationKind.Binding or FormaLoweredOperationKind.ResourceReference)
            .Select(operation => (operation.NodeId.Value, lowered.Symbols[operation.MemberSymbolId.Value].Name))
            .ToHashSet();
        var eventNames = eventMemberNames == null ? null : eventMemberNames.ToHashSet(StringComparer.Ordinal);
        var xamlNamespace = XNamespace.Get(XamlNamespaces.Xaml2006);
        var authoringNamespace = XNamespace.Get("clr-namespace:Forma.Xaml;assembly=Forma");
        if (sourceMetadata != null)
            xml.Root!.SetAttributeValue(XNamespace.Xmlns + "__formaSource", authoringNamespace.NamespaceName);

        for (var index = elements.Length - 1; index >= 0; index--)
        {
            var element = elements[index];
            var node = lowered.Nodes[index];
            if (node.ScopeId != lowered.OwnerScope.ScopeId || deferredNodes.Contains(index))
            {
                element.Remove();
                continue;
            }

            element.Attribute(xamlNamespace + "Class")?.Remove();
            element.Attribute(xamlNamespace + "DataType")?.Remove();
            foreach (var attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration).ToArray())
            {
                if (deferredMembers.Contains((index, attribute.Name.LocalName)) || eventNames?.Contains(attribute.Name.LocalName) == true)
                    attribute.Remove();
            }
            var name = element.Attribute(xamlNamespace + "Name");
            if (name != null)
            {
                element.SetAttributeValue("Name", name.Value);
                name.Remove();
            }
            if (sourceMetadata != null && sourceMetadata.TryGetValue(index, out var metadata))
                element.SetAttributeValue(authoringNamespace + "XamlSource.Metadata", "{}" + metadata);
        }
        return xml.ToString(SaveOptions.DisableFormatting);
    }

    private static string ProjectTemplateForEmission(FormaLoweredDocument lowered, FormaLoweredTemplate template)
    {
        var xml = XDocument.Parse(lowered.Source, LoadOptions.PreserveWhitespace);
        var elements = xml.Root!.DescendantsAndSelf().ToArray();
        if (elements.Length != lowered.Nodes.Count)
            throw new InvalidOperationException("The semantic and structured XAML trees do not have matching node counts.");
        var templateNode = lowered.Nodes[template.NodeId.Value];
        var rootNode = templateNode.Children.Select(child => lowered.Nodes[child.Value])
            .Single(node => !node.TypeName.Contains('.', StringComparison.Ordinal));
        var projectedRoot = new XElement(elements[rootNode.Id.Value]);
        foreach (var declaration in xml.Root.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            if (projectedRoot.Attribute(declaration.Name) == null) projectedRoot.Add(new XAttribute(declaration));

        var nodes = DescendantsAndSelf(lowered, rootNode).ToArray();
        var projectedElements = projectedRoot.DescendantsAndSelf().ToArray();
        if (nodes.Length != projectedElements.Length)
            throw new InvalidOperationException("The lowered template and structured XAML trees do not have matching node counts.");
        var deferredNodes = template.Scope.Operations
            .Where(operation => operation.Kind is FormaLoweredOperationKind.Style or FormaLoweredOperationKind.Storyboard or
                FormaLoweredOperationKind.Trigger or FormaLoweredOperationKind.Transition or FormaLoweredOperationKind.AdaptiveCondition)
            .Select(operation => operation.NodeId)
            .ToHashSet();
        var deferredMembers = template.Scope.Operations
            .Where(operation => operation.Kind is FormaLoweredOperationKind.Binding or FormaLoweredOperationKind.ResourceReference)
            .Select(operation => (operation.NodeId, lowered.Symbols[operation.MemberSymbolId.Value].Name))
            .ToHashSet();
        var xamlNamespace = XNamespace.Get(XamlNamespaces.Xaml2006);

        for (var index = projectedElements.Length - 1; index >= 0; index--)
        {
            var element = projectedElements[index];
            var node = nodes[index];
            if (node.ScopeId != template.Scope.ScopeId || deferredNodes.Contains(node.Id))
            {
                element.Remove();
                continue;
            }
            element.Attribute(xamlNamespace + "Class")?.Remove();
            element.Attribute(xamlNamespace + "DataType")?.Remove();
            foreach (var attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration).ToArray())
                if (deferredMembers.Contains((node.Id, attribute.Name.LocalName))) attribute.Remove();
            var name = element.Attribute(xamlNamespace + "Name");
            if (name != null)
            {
                element.SetAttributeValue("Name", name.Value);
                name.Remove();
            }
        }
        return projectedRoot.ToString(SaveOptions.DisableFormatting);
    }

    private static IEnumerable<FormaLoweredNode> DescendantsAndSelf(FormaLoweredDocument document, FormaLoweredNode root)
    {
        var stack = new Stack<FormaLoweredNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;
            for (var index = current.Children.Count - 1; index >= 0; index--)
                stack.Push(document.Nodes[current.Children[index].Value]);
        }
    }

    private static Action<Control> CreateSreAttachments(FormaLoweredDocument lowered, Type runtimeType)
    {
        var root = Expression.Parameter(typeof(Control), "root");
        var attachments = new List<Expression>();
        var attachTemplates = CreateSreTemplateAttachments(lowered, runtimeType);
        var attachStyles = CreateSreStyleAttachments(lowered);
        var attachStoryboards = CreateSreStoryboardAttachments(lowered);
        var controls = GetSreControls(lowered);
        var sourceType = ResolveSreTypeReference(lowered, lowered.DataType);

        foreach (var operation in lowered.OwnerScope.Operations)
        {
            if (operation.Kind is not FormaLoweredOperationKind.Binding and not FormaLoweredOperationKind.ResourceReference) continue;
            var node = lowered.Nodes[operation.NodeId.Value];
            if (node.TypeName is "Style" or "EventTrigger" or "PropertyTrigger" or "Storyboard" ||
                HasLoweredAncestor(lowered, node, "Style") || HasLoweredAncestor(lowered, node, "EventTrigger") ||
                HasLoweredAncestor(lowered, node, "PropertyTrigger") || HasLoweredAncestor(lowered, node, "Storyboard")) continue;
            var targetType = ResolveSreType(lowered, node);
            var memberName = lowered.Symbols[operation.MemberSymbolId.Value].Name;
            var targetProperty = FindSreProperty(targetType, memberName)
                ?? throw new InvalidOperationException($"Target property '{targetType.FullName}.{memberName}' was not found.");
            var target = FindSreTarget(root, node, controls);

            if (operation.Value is FormaBindingValue binding)
            {
                var bindingSourceType = ResolveSreBindingSourceType(lowered, binding, sourceType)
                    ?? throw new InvalidOperationException("Compiled binding emission requires a resolvable source type.");
                var sourceProperty = FindSreProperty(bindingSourceType, binding.Path)
                    ?? throw new InvalidOperationException($"Binding source property '{bindingSourceType.FullName}.{binding.Path}' was not found.");
                attachments.Add(CreateSreBinding(root, target, bindingSourceType, sourceProperty, targetType, targetProperty, binding));
            }
            else if (operation.Value is FormaResourceValue resource)
                attachments.Add(CreateSreResource(root, target, targetType, targetProperty, resource));
        }

        var attachValues = attachments.Count == 0
            ? _ => { }
            : Expression.Lambda<Action<Control>>(Expression.Block(attachments), root).Compile();
        return value =>
        {
            attachTemplates(value);
            attachStyles(value);
            attachStoryboards(value);
            attachValues(value);
        };
    }

    private static Action<Control> CreateSreTemplateAttachments(FormaLoweredDocument lowered, Type runtimeType)
    {
        if (lowered.Templates.Count == 0) return _ => { };
        var factories = new List<(FormaLoweredTemplate Template, Func<IServiceProvider?, object> Build, Action<Control> Attach)>();
        for (var index = 0; index < lowered.Templates.Count; index++)
        {
            var method = runtimeType.GetMethod($"__TemplateBuild{index}", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException($"Generated template build method {index} was not found.");
            var provider = Expression.Parameter(typeof(IServiceProvider), "provider");
            var build = Expression.Lambda<Func<IServiceProvider?, object>>(
                Expression.Convert(Expression.Call(method, provider), typeof(object)), provider).Compile();
            var template = lowered.Templates[index];
            factories.Add((template, build, CreateSreTemplateScopeAttachments(lowered, template)));
        }
        return root =>
        {
            foreach (var entry in factories)
            {
                var node = lowered.Nodes[entry.Template.NodeId.Value];
                var key = node.FindDirective("Key");
                object template = entry.Template.Kind switch
                {
                    FormaXamlTemplateKind.Data => new DataTemplate(
                        ResolveSreSymbolType(lowered, entry.Template.DataTypeSymbolId, typeof(object)),
                        (context, item) =>
                        {
                            var templateRoot = (Control)entry.Build(null);
                            context.BindItem(templateRoot, item);
                            NameScope.CreateForTree(templateRoot);
                            entry.Attach(templateRoot);
                            return templateRoot;
                        }),
                    FormaXamlTemplateKind.Control => new ControlTemplate(
                        ResolveSreSymbolType(lowered, entry.Template.TargetTypeSymbolId, typeof(TemplatedControl)),
                        (_, _) =>
                        {
                            var templateRoot = (Control)entry.Build(null);
                            NameScope.CreateForTree(templateRoot);
                            entry.Attach(templateRoot);
                            return templateRoot;
                        }),
                    FormaXamlTemplateKind.ItemsPanel => new ItemsPanelTemplate(_ =>
                    {
                        var templateRoot = (Container)entry.Build(null);
                        NameScope.CreateForTree(templateRoot);
                        entry.Attach(templateRoot);
                        return templateRoot;
                    }),
                    _ => throw new InvalidOperationException($"Unknown template kind {entry.Template.Kind}."),
                };
                if (string.IsNullOrWhiteSpace(key))
                {
                    var (ownerNode, propertyName) = ResolveSreTemplatePropertyOwner(lowered, node);
                    CreateSreTemplatePropertySetter(lowered, ownerNode, propertyName)(root, template);
                }
                else
                {
                    var ownerNode = FindLoweredResourceOwner(lowered, node) ?? lowered.Nodes[lowered.RootNodeId.Value];
                    FindSreControl(root, ownerNode, lowered).Resources.Add(key, template);
                }
            }
        };
    }

    private static (FormaLoweredNode Owner, string PropertyName) ResolveSreTemplatePropertyOwner(
        FormaLoweredDocument document,
        FormaLoweredNode template)
    {
        var propertyElement = template.ParentId is { } propertyId ? document.Nodes[propertyId.Value] : null;
        if (propertyElement == null || !propertyElement.TypeName.Contains('.', StringComparison.Ordinal) || propertyElement.ParentId == null)
            throw new InvalidOperationException($"Unkeyed {template.TypeName} must be assigned through a direct property element.");
        var separator = propertyElement.TypeName.LastIndexOf('.');
        return (document.Nodes[propertyElement.ParentId.Value.Value], propertyElement.TypeName.Substring(separator + 1));
    }

    private static Action<Control, object> CreateSreTemplatePropertySetter(
        FormaLoweredDocument document,
        FormaLoweredNode owner,
        string propertyName)
    {
        var root = Expression.Parameter(typeof(Control), "root");
        var value = Expression.Parameter(typeof(object), "value");
        var ownerType = ResolveSreType(document, owner);
        var property = FindSreProperty(ownerType, propertyName)
            ?? throw new InvalidOperationException($"Template property '{ownerType.FullName}.{propertyName}' was not found.");
        if (property.SetMethod == null)
            throw new InvalidOperationException($"Template property '{ownerType.FullName}.{propertyName}' is read-only.");
        var target = FindSreObjectTarget(document, root, owner, GetSreControls(document));
        return Expression.Lambda<Action<Control, object>>(
            Expression.Assign(Expression.Property(Expression.Convert(target, ownerType), property), Expression.Convert(value, property.PropertyType)),
            root,
            value).Compile();
    }

    private static Action<Control> CreateSreTemplateScopeAttachments(FormaLoweredDocument lowered, FormaLoweredTemplate template)
    {
        var root = Expression.Parameter(typeof(Control), "root");
        var attachments = new List<Expression>();
        var controls = GetSreControls(lowered, template.Scope);
        var templateRoot = lowered.Nodes[template.NodeId.Value].Children.Select(child => lowered.Nodes[child.Value])
            .Single(node => !node.TypeName.Contains('.', StringComparison.Ordinal));
        var sourceType = template.Kind == FormaXamlTemplateKind.Data
            ? ResolveSreSymbolType(lowered, template.DataTypeSymbolId, typeof(object))
            : null;
        foreach (var operation in template.Scope.Operations)
        {
            if (operation.Kind is not FormaLoweredOperationKind.Binding and not FormaLoweredOperationKind.ResourceReference) continue;
            var node = lowered.Nodes[operation.NodeId.Value];
            if (node.TypeName is "Style" or "EventTrigger" or "PropertyTrigger" or "Storyboard" ||
                HasLoweredAncestor(lowered, node, "Style") || HasLoweredAncestor(lowered, node, "EventTrigger") ||
                HasLoweredAncestor(lowered, node, "PropertyTrigger") || HasLoweredAncestor(lowered, node, "Storyboard")) continue;
            var targetType = ResolveSreType(lowered, node);
            var memberName = lowered.Symbols[operation.MemberSymbolId.Value].Name;
            var targetProperty = FindSreProperty(targetType, memberName)
                ?? throw new InvalidOperationException($"Target property '{targetType.FullName}.{memberName}' was not found.");
            var target = FindSreTarget(root, node, controls);
            if (operation.Value is FormaBindingValue binding)
            {
                var bindingSourceType = ResolveSreBindingSourceType(lowered, binding, sourceType)
                    ?? throw new InvalidOperationException("Template binding emission requires a resolvable source type.");
                var sourceProperty = FindSreProperty(bindingSourceType, binding.Path)
                    ?? throw new InvalidOperationException($"Binding source property '{bindingSourceType.FullName}.{binding.Path}' was not found.");
                attachments.Add(CreateSreBinding(root, target, bindingSourceType, sourceProperty, targetType, targetProperty, binding));
            }
            else if (operation.Value is FormaResourceValue resource)
                attachments.Add(CreateSreResource(root, target, targetType, targetProperty, resource));
        }
        var attachValues = attachments.Count == 0
            ? _ => { }
            : Expression.Lambda<Action<Control>>(Expression.Block(attachments), root).Compile();
        var attachStyles = CreateSreStyleAttachments(lowered, template.Scope, controls, templateRoot);
        var attachStoryboards = CreateSreStoryboardAttachments(lowered, template.Scope, controls, templateRoot, sourceType);
        return value =>
        {
            attachStyles(value);
            attachStoryboards(value);
            attachValues(value);
        };
    }

    private static Type ResolveSreSymbolType(FormaLoweredDocument lowered, FormaSymbolId symbolId, Type fallback)
    {
        if (!symbolId.IsResolved) return fallback;
        var symbol = lowered.Symbols[symbolId.Value];
        return ResolveSreType(lowered, symbol.Namespace, symbol.Name) ?? fallback;
    }

    private static Type? ResolveSreBindingSourceType(FormaLoweredDocument lowered, FormaBindingValue binding, Type? dataContextType)
    {
        if (binding.Source.Kind == FormaBindingSourceKind.DataContext) return dataContextType;
        if (!binding.Source.TypeSymbolId.IsResolved) return null;
        var symbol = lowered.Symbols[binding.Source.TypeSymbolId.Value];
        return ResolveSreType(lowered, symbol.Namespace, symbol.Name);
    }

    private static Action<Control> CreateSreStoryboardAttachments(FormaLoweredDocument lowered)
        => CreateSreStoryboardAttachments(
            lowered,
            lowered.OwnerScope,
            GetSreControls(lowered),
            lowered.Nodes[lowered.RootNodeId.Value],
            ResolveSreTypeReference(lowered, lowered.DataType));

    private static Action<Control> CreateSreStoryboardAttachments(
        FormaLoweredDocument lowered,
        FormaLoweredScope scope,
        FormaLoweredNode[] controls,
        FormaLoweredNode fallbackOwner,
        Type? sourceType)
    {
        var storyboards = scope.Operations
            .Where(operation => operation.Kind == FormaLoweredOperationKind.Storyboard)
            .Select(operation => lowered.Nodes[operation.NodeId.Value])
            .ToDictionary(
                node => node.FindDirective("Key") ?? throw new InvalidOperationException("Storyboard requires x:Key."),
                node => (Node: node, Owner: FindLoweredResourceOwner(lowered, node) ?? fallbackOwner, Storyboard: CreateSreStoryboard(lowered, node)),
                StringComparer.Ordinal);
        var triggerAttachments = new List<Action<Control>>();
        foreach (var operation in scope.Operations.Where(operation => operation.Kind == FormaLoweredOperationKind.Trigger))
        {
            var trigger = lowered.Nodes[operation.NodeId.Value];
            var action = trigger.Children.Select(child => lowered.Nodes[child.Value])
                .SingleOrDefault(child => child.TypeName is "BeginStoryboard" or "StopStoryboard")
                ?? throw new InvalidOperationException($"{trigger.TypeName} requires BeginStoryboard or StopStoryboard.");
            if (!TryParseSreResource(action.FindMember("Storyboard") ?? string.Empty, out var reference) || reference.IsDynamic ||
                !storyboards.TryGetValue(reference.Key, out var storyboard))
                throw new InvalidOperationException($"Trigger storyboard '{reference.Key}' was not found.");
            triggerAttachments.Add(trigger.TypeName == "PropertyTrigger"
                ? CreateSrePropertyTriggerAttachment(lowered, trigger, storyboard.Storyboard, sourceType)
                : CreateSreEventTriggerAttachment(lowered, trigger, action.TypeName == "StopStoryboard", storyboard.Storyboard, controls));
        }

        if (storyboards.Count == 0 && triggerAttachments.Count == 0) return _ => { };
        return root =>
        {
            foreach (var (key, entry) in storyboards)
                FindSreControl(root, entry.Owner, controls).Resources.Add(key, entry.Storyboard);
            foreach (var attach in triggerAttachments) attach(root);
        };
    }

    private static Storyboard CreateSreStoryboard(FormaLoweredDocument lowered, FormaLoweredNode node)
    {
        var storyboard = new Storyboard();
        if (bool.TryParse(node.FindMember("AutoReverse"), out var autoReverse)) storyboard.AutoReverse = autoReverse;
        if (Enum.TryParse<FillBehavior>(node.FindMember("FillBehavior"), true, out var fillBehavior)) storyboard.FillBehavior = fillBehavior;
        if (node.FindMember("RepeatBehavior") is string repeatBehavior)
        {
            storyboard.RepeatBehavior = string.Equals(repeatBehavior, "Forever", StringComparison.OrdinalIgnoreCase)
                ? RepeatBehavior.ForeverValue
                : RepeatBehavior.ForCount(double.Parse(repeatBehavior, System.Globalization.CultureInfo.InvariantCulture));
        }

        foreach (var timelineId in node.Children)
        {
            var timelineNode = lowered.Nodes[timelineId.Value];
            if (!timelineNode.TypeName.EndsWith("Timeline", StringComparison.Ordinal)) continue;
            var targetName = timelineNode.FindMember("TargetName") ?? throw new InvalidOperationException("Timeline requires TargetName.");
            var propertyName = timelineNode.FindMember("Property") ?? throw new InvalidOperationException("Timeline requires Property.");
            var targetNode = lowered.Nodes.SingleOrDefault(candidate => candidate.ScopeId == node.ScopeId && candidate.FindDirective("Name") == targetName)
                ?? throw new InvalidOperationException($"Storyboard target '{targetName}' was not found.");
            var targetType = ResolveSreType(lowered, targetNode);
            var property = FindSreProperty(targetType, propertyName)
                ?? throw new InvalidOperationException($"Storyboard property '{targetType.FullName}.{propertyName}' was not found.");
            storyboard.AddTimeline(CreateSreTimeline(lowered, timelineNode, targetType, property, targetName, propertyName));
        }
        return storyboard;
    }

    private static IStoryboardTimeline CreateSreTimeline(
        FormaLoweredDocument lowered,
        FormaLoweredNode timelineNode,
        Type targetType,
        PropertyInfo property,
        string targetName,
        string propertyName)
    {
        var create = typeof(FormaXamlCompiler).GetMethod(nameof(CreateSreTimelineCore), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(property.PropertyType);
        var call = Expression.Call(create, Expression.Constant(lowered), Expression.Constant(timelineNode),
            Expression.Constant(targetType), Expression.Constant(property), Expression.Constant(targetName), Expression.Constant(propertyName));
        return Expression.Lambda<Func<IStoryboardTimeline>>(call).Compile()();
    }

    private static IStoryboardTimeline CreateSreTimelineCore<T>(
        FormaLoweredDocument lowered,
        FormaLoweredNode timelineNode,
        Type targetType,
        PropertyInfo property,
        string targetName,
        string propertyName)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(T), "value");
        var targetProperty = Expression.Property(Expression.Convert(target, targetType), property);
        var xamlProperty = new XamlProperty<T>(propertyName,
            Expression.Lambda<Func<object, T>>(targetProperty, target).Compile(),
            Expression.Lambda<Action<object, T>>(Expression.Assign(targetProperty, value), target, value).Compile());
        var timeline = CompiledTimeline.Create(timelineNode.TypeName, targetName, xamlProperty);
        foreach (var keyFrameId in timelineNode.Children)
        {
            var keyFrame = lowered.Nodes[keyFrameId.Value];
            if (keyFrame.TypeName != "KeyFrame") continue;
            var time = TimeSpan.Parse(keyFrame.FindMember("Time") ?? throw new InvalidOperationException("KeyFrame requires Time."), System.Globalization.CultureInfo.InvariantCulture);
            var converted = (T)XamlValueConverter.Convert(keyFrame.FindMember("Value") ?? throw new InvalidOperationException("KeyFrame requires Value."), typeof(T))!;
            var easing = Enum.TryParse<Easing>(keyFrame.FindMember("Easing"), true, out var parsedEasing) ? parsedEasing : Easing.Linear;
            timeline.AddKeyFrame(new KeyFrame<T>(time, converted, easing));
        }
        return timeline;
    }

    private static Action<Control> CreateSrePropertyTriggerAttachment(FormaLoweredDocument lowered, FormaLoweredNode trigger, Storyboard storyboard, Type? sourceType)
    {
        var binding = trigger.Members.SingleOrDefault(member => !member.IsDirective && member.Name == "Binding")?.Value as FormaBindingValue
            ?? throw new InvalidOperationException("PropertyTrigger requires Binding.");
        var resolvedSourceType = sourceType ?? throw new InvalidOperationException("PropertyTrigger requires a resolvable x:DataType.");
        var property = FindSreProperty(resolvedSourceType, binding.Path)
            ?? throw new InvalidOperationException($"PropertyTrigger source property '{resolvedSourceType.FullName}.{binding.Path}' was not found.");
        var source = Expression.Parameter(resolvedSourceType, "source");
        var read = Expression.Lambda(typeof(Func<,>).MakeGenericType(resolvedSourceType, property.PropertyType), Expression.Property(source, property), source);
        var attach = typeof(CompiledStoryboardTrigger).GetMethods().Single(method => method.Name == nameof(CompiledStoryboardTrigger.AttachProperty))
            .MakeGenericMethod(resolvedSourceType, property.PropertyType);
        var root = Expression.Parameter(typeof(Control), "root");
        var expected = XamlValueConverter.Convert(trigger.FindMember("Value") ?? throw new InvalidOperationException("PropertyTrigger requires Value."), property.PropertyType);
        var body = Expression.Block(Expression.Call(attach, root, read, Expression.Constant(binding.Path), Expression.Constant(expected, property.PropertyType), Expression.Constant(storyboard)), Expression.Empty());
        return Expression.Lambda<Action<Control>>(body, root).Compile();
    }

    private static Action<Control> CreateSreEventTriggerAttachment(FormaLoweredDocument lowered, FormaLoweredNode trigger, bool stop, Storyboard storyboard, FormaLoweredNode[] controls)
    {
        var sourceName = trigger.FindMember("SourceName") ?? throw new InvalidOperationException("EventTrigger requires SourceName.");
        var eventName = trigger.FindMember("Event") ?? throw new InvalidOperationException("EventTrigger requires Event.");
        var sourceNode = lowered.Nodes.SingleOrDefault(node => node.ScopeId == trigger.ScopeId && node.FindDirective("Name") == sourceName)
            ?? throw new InvalidOperationException($"EventTrigger source '{sourceName}' was not found.");
        var sourceType = ResolveSreType(lowered, sourceNode);
        var eventInfo = sourceType.GetEvent(eventName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Event '{sourceType.FullName}.{eventName}' was not found.");
        var handlerType = eventInfo.EventHandlerType!;
        var target = Expression.Parameter(sourceType, "target");
        var handler = Expression.Parameter(handlerType, "handler");
        var add = Expression.Lambda(typeof(Action<,>).MakeGenericType(sourceType, handlerType), Expression.Call(target, eventInfo.AddMethod!, handler), target, handler);
        var remove = Expression.Lambda(typeof(Action<,>).MakeGenericType(sourceType, handlerType), Expression.Call(target, eventInfo.RemoveMethod!, handler), target, handler);
        var action = Expression.Parameter(typeof(Action), "action");
        var invoke = handlerType.GetMethod("Invoke")!;
        var parameters = invoke.GetParameters().Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name)).ToArray();
        var eventHandler = Expression.Lambda(handlerType, Expression.Invoke(action), parameters);
        var factory = Expression.Lambda(typeof(Func<,>).MakeGenericType(typeof(Action), handlerType), eventHandler, action);
        var attach = typeof(CompiledStoryboardTrigger).GetMethods().Single(method => method.Name ==
            (stop ? nameof(CompiledStoryboardTrigger.AttachStopEvent) : nameof(CompiledStoryboardTrigger.AttachEvent)))
            .MakeGenericMethod(sourceType, handlerType);
        var root = Expression.Parameter(typeof(Control), "root");
        var targetExpression = Expression.Convert(FindSreTarget(root, sourceNode, controls), sourceType);
        var body = Expression.Block(Expression.Call(attach, root, targetExpression, add, remove, factory, Expression.Constant(storyboard)), Expression.Empty());
        return Expression.Lambda<Action<Control>>(body, root).Compile();
    }

    private static Action<Control> CreateSreStyleAttachments(FormaLoweredDocument lowered)
        => CreateSreStyleAttachments(
            lowered,
            lowered.OwnerScope,
            GetSreControls(lowered),
            lowered.Nodes[lowered.RootNodeId.Value]);

    private static Action<Control> CreateSreStyleAttachments(
        FormaLoweredDocument lowered,
        FormaLoweredScope scope,
        FormaLoweredNode[] controls,
        FormaLoweredNode fallbackOwner)
    {
        var styles = scope.Operations
            .Where(operation => operation.Kind == FormaLoweredOperationKind.Style)
            .Select(operation => lowered.Nodes[operation.NodeId.Value])
            .Select(node => (Node: node, Style: CreateSreStyle(lowered, node), Owner: FindLoweredResourceOwner(lowered, node) ?? fallbackOwner))
            .ToArray();
        if (styles.Length == 0) return _ => { };

        return root =>
        {
            foreach (var entry in styles)
            {
                var owner = FindSreControl(root, entry.Owner, controls);
                var key = entry.Node.FindDirective("Key");
                if (!string.IsNullOrWhiteSpace(key)) owner.Resources.Add(key, entry.Style);
            }
            foreach (var group in styles.GroupBy(entry => entry.Owner.Id))
            {
                var owner = FindSreControl(root, lowered.Nodes[group.Key.Value], controls);
                _ = StyleEngine.Attach(owner, group.Select(entry => entry.Style));
            }
        };
    }

    private static Style CreateSreStyle(FormaLoweredDocument lowered, FormaLoweredNode node)
    {
        var parsedSelector = node.Selector ?? throw new InvalidOperationException("Style requires a lowered Selector.");
        var style = new Style(NormalizeStyleSelector(parsedSelector));
        var adaptiveNode = FindAdaptiveCondition(lowered, node);
        if (adaptiveNode != null) style.Condition = CreateSreAdaptiveCondition(adaptiveNode);
        var subjectTypes = ResolveSreStyleSubjectTypes(lowered, parsedSelector);

        foreach (var setterId in node.Children)
        {
            var setter = lowered.Nodes[setterId.Value];
            if (setter.TypeName != "Setter") continue;
            var propertyName = setter.FindMember("Property") ?? throw new InvalidOperationException("Setter requires Property.");
            var valueText = setter.FindMember("Value") ?? throw new InvalidOperationException("Setter requires Value.");
            var properties = subjectTypes.Select(type => FindSreProperty(type, propertyName)).ToArray();
            if (properties.Any(property => property == null))
                throw new InvalidOperationException($"Style setter property '{propertyName}' is not available on every selector-list subject type.");
            var property = properties[0]!;
            var targetType = property.DeclaringType!;
            if (properties.Any(candidate => candidate!.PropertyType != property.PropertyType || candidate.DeclaringType != targetType) ||
                subjectTypes.Any(type => !targetType.IsAssignableFrom(type)))
                throw new InvalidOperationException($"Style setter property '{propertyName}' does not have one compatible typed target across every selector-list arm.");
            style.AddSetter(CreateSreStyleSetter(targetType, property, valueText));
        }
        foreach (var transition in EnumerateStyleTransitions(lowered, node))
        {
            var propertyName = transition.FindMember("Property") ?? throw new InvalidOperationException($"{transition.TypeName} requires Property.");
            var properties = subjectTypes.Select(type => FindSreProperty(type, propertyName)).ToArray();
            if (properties.Any(property => property == null))
                throw new InvalidOperationException($"Style transition property '{propertyName}' is not available on every selector-list subject type.");
            var property = properties[0]!;
            var targetType = property.DeclaringType!;
            if (properties.Any(candidate => candidate!.PropertyType != property.PropertyType || candidate.DeclaringType != targetType) ||
                subjectTypes.Any(type => !targetType.IsAssignableFrom(type)))
                throw new InvalidOperationException($"Style transition property '{propertyName}' does not have one compatible typed target across every selector-list arm.");
            style.AddTransition(CreateSreStyleTransition(targetType, property, transition));
        }
        return style;
    }

    private static IEnumerable<FormaLoweredNode> EnumerateStyleTransitions(FormaLoweredDocument lowered, FormaLoweredNode styleNode)
    {
        foreach (var childId in styleNode.Children)
        {
            var child = lowered.Nodes[childId.Value];
            if (child.TypeName.EndsWith("Transition", StringComparison.Ordinal) && child.TypeName != "Style.Transitions") yield return child;
            if (child.TypeName != "Style.Transitions") continue;
            foreach (var transitionId in child.Children)
            {
                var transition = lowered.Nodes[transitionId.Value];
                if (transition.TypeName.EndsWith("Transition", StringComparison.Ordinal)) yield return transition;
            }
        }
    }

    private static Type[] ResolveSreStyleSubjectTypes(FormaLoweredDocument lowered, StyleSelector selector) =>
        selector.Arms.Select(arm =>
        {
            var typeName = arm.Subject.TypeName;
            if (string.IsNullOrWhiteSpace(typeName)) return typeof(Control);
            var separator = typeName.IndexOf(':');
            if (separator > 0 && lowered.Namespaces.TryGetValue(typeName.Substring(0, separator), out var xmlNamespace))
                return ResolveSreType(lowered, xmlNamespace, typeName.Substring(separator + 1))
                    ?? throw new InvalidOperationException($"Style selector subject type '{typeName}' was not resolved.");
            return lowered.Nodes.Select(candidate => TryResolveSreType(lowered, candidate)).FirstOrDefault(type => type?.Name == typeName)
                ?? ResolveSreType(lowered, XamlNamespaces.Forma, typeName)
                ?? throw new InvalidOperationException($"Style selector subject type '{typeName}' was not resolved.");
        }).Distinct().ToArray();

    private static StyleSelector NormalizeStyleSelector(StyleSelector selector) => new(selector.Arms.Select(arm =>
        new StyleSelectorArm(arm.Compounds.Select(NormalizeStyleSelectorCompound).ToArray(), arm.Combinators)) .ToArray());

    private static StyleSelectorCompound NormalizeStyleSelectorCompound(StyleSelectorCompound compound) => new(
        compound.TypeName is { } typeName && typeName.Contains(':') ? typeName.Substring(typeName.IndexOf(':') + 1) : compound.TypeName,
        compound.IsUniversal,
        compound.Name,
        compound.Classes,
        compound.PseudoStates,
        compound.Negations.Select(NormalizeStyleSelectorCompound).ToArray());

    private static FormaLoweredNode? FindAdaptiveCondition(FormaLoweredDocument lowered, FormaLoweredNode styleNode)
    {
        foreach (var childId in styleNode.Children)
        {
            var child = lowered.Nodes[childId.Value];
            if (child.TypeName == "AdaptiveCondition") return child;
            if (child.TypeName != "Style.Condition") continue;
            foreach (var conditionId in child.Children)
            {
                var condition = lowered.Nodes[conditionId.Value];
                if (condition.TypeName == "AdaptiveCondition") return condition;
            }
        }
        return null;
    }

    private static AdaptiveCondition CreateSreAdaptiveCondition(FormaLoweredNode node)
    {
        var condition = new AdaptiveCondition();
        foreach (var member in node.Members.Where(member => !member.IsDirective))
        {
            switch (member.Name)
            {
                case nameof(AdaptiveCondition.MinViewportWidth): condition.MinViewportWidth = ParseInvariantSingle(member.Value.RawText); break;
                case nameof(AdaptiveCondition.MaxViewportWidth): condition.MaxViewportWidth = ParseInvariantSingle(member.Value.RawText); break;
                case nameof(AdaptiveCondition.MinViewportHeight): condition.MinViewportHeight = ParseInvariantSingle(member.Value.RawText); break;
                case nameof(AdaptiveCondition.MaxViewportHeight): condition.MaxViewportHeight = ParseInvariantSingle(member.Value.RawText); break;
                case nameof(AdaptiveCondition.DisplayScale): condition.DisplayScale = ParseInvariantSingle(member.Value.RawText); break;
                case nameof(AdaptiveCondition.ThemeVariant): condition.ThemeVariant = Enum.Parse<ThemeVariant>(member.Value.RawText, false); break;
                case nameof(AdaptiveCondition.InputModality): condition.InputModality = Enum.Parse<InputModality>(member.Value.RawText, false); break;
                default: throw new InvalidOperationException($"AdaptiveCondition member '{member.Name}' is not supported.");
            }
        }
        return condition;
    }

    private static float ParseInvariantSingle(string value) =>
        float.Parse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);

    private static IStyleSetter CreateSreStyleSetter(Type targetType, PropertyInfo property, string valueText)
    {
        var create = typeof(FormaXamlCompiler).GetMethod(nameof(CreateSreStyleSetterCore), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(property.PropertyType);
        var call = Expression.Call(create, Expression.Constant(targetType), Expression.Constant(property), Expression.Constant(valueText));
        return Expression.Lambda<Func<IStyleSetter>>(call).Compile()();
    }

    private static IStyleSetter CreateSreStyleSetterCore<T>(Type targetType, PropertyInfo property, string valueText)
    {
        var targetObject = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(T), "value");
        var targetProperty = Expression.Property(Expression.Convert(targetObject, targetType), property);
        var xamlProperty = new XamlProperty<T>(property.Name,
            Expression.Lambda<Func<object, T>>(targetProperty, targetObject).Compile(),
            Expression.Lambda<Action<object, T>>(Expression.Assign(targetProperty, value), targetObject, value).Compile());
        if (TryParseSreResource(valueText, out var resource))
        {
            if (resource.IsDynamic) throw new InvalidOperationException("DynamicResource is not supported in a style setter; use StaticResource or a control-local DynamicResource.");
            return new StyleSetter<T>(xamlProperty, control => StaticResource.Resolve<T>(control, resource.Key));
        }

        var converted = (T)XamlValueConverter.Convert(valueText, typeof(T))!;
        return new StyleSetter<T>(xamlProperty, converted);
    }

    private static IStyleTransition CreateSreStyleTransition(Type targetType, PropertyInfo property, FormaLoweredNode node)
    {
        var create = typeof(FormaXamlCompiler).GetMethod(nameof(CreateSreStyleTransitionCore), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(property.PropertyType);
        return (IStyleTransition)create.Invoke(null, new object[] { targetType, property, node.TypeName,
            node.FindMember("Duration") ?? throw new InvalidOperationException($"{node.TypeName} requires Duration."),
            node.FindMember("Easing") ?? nameof(Easing.Linear) })!;
    }

    private static IStyleTransition CreateSreStyleTransitionCore<T>(
        Type targetType,
        PropertyInfo property,
        string transitionType,
        string durationText,
        string easingText)
    {
        var targetObject = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(T), "value");
        var targetProperty = Expression.Property(Expression.Convert(targetObject, targetType), property);
        var xamlProperty = new XamlProperty<T>(property.Name,
            Expression.Lambda<Func<object, T>>(targetProperty, targetObject).Compile(),
            Expression.Lambda<Action<object, T>>(Expression.Assign(targetProperty, value), targetObject, value).Compile());
        var duration = TimeSpan.Parse(durationText, System.Globalization.CultureInfo.InvariantCulture);
        var easing = Enum.Parse<Easing>(easingText, false);
        var runtimeType = transitionType switch
        {
            nameof(FloatTransition) when typeof(T).FullName == "System.Single" => typeof(FloatTransition),
            nameof(ColorTransition) when typeof(T).FullName == "Microsoft.Xna.Framework.Color" => typeof(ColorTransition),
            nameof(Vector2Transition) when typeof(T).FullName == "Microsoft.Xna.Framework.Vector2" => typeof(Vector2Transition),
            nameof(ThicknessTransition) when typeof(T) == typeof(Thickness) => typeof(ThicknessTransition),
            _ => throw new InvalidOperationException($"{transitionType} cannot animate '{property.DeclaringType!.FullName}.{property.Name}' of type '{typeof(T).FullName}'."),
        };
        return (IStyleTransition)Activator.CreateInstance(runtimeType, xamlProperty, duration, easing)!;
    }

    private static bool TryParseSreResource(string value, out FormaResourceValue resource)
    {
        var dynamic = value.StartsWith("{DynamicResource ", StringComparison.Ordinal);
        var prefix = dynamic ? "{DynamicResource " : "{StaticResource ";
        if (value.StartsWith(prefix, StringComparison.Ordinal) && value.EndsWith('}'))
        {
            resource = new FormaResourceValue(value, value.Substring(prefix.Length, value.Length - prefix.Length - 1).Trim(), dynamic);
            return true;
        }
        resource = null!;
        return false;
    }

    private static FormaLoweredNode FindLoweredResourceOwner(FormaLoweredDocument document, FormaLoweredNode node)
    {
        for (var parentId = node.ParentId; parentId != null; parentId = document.Nodes[parentId.Value.Value].ParentId)
        {
            var parent = document.Nodes[parentId.Value.Value];
            if (parent.TypeName.EndsWith(".Resources", StringComparison.Ordinal) && parent.ParentId != null)
                return document.Nodes[parent.ParentId.Value.Value];
        }
        return document.Nodes[document.RootNodeId.Value];
    }

    private static bool HasLoweredAncestor(FormaLoweredDocument document, FormaLoweredNode node, string typeName)
    {
        for (var parentId = node.ParentId; parentId != null; parentId = document.Nodes[parentId.Value.Value].ParentId)
            if (document.Nodes[parentId.Value.Value].TypeName == typeName) return true;
        return false;
    }

    private static Control FindSreControl(Control root, FormaLoweredNode node, FormaLoweredDocument document)
        => FindSreControl(root, node, GetSreControls(document));

    private static Control FindSreControl(Control root, FormaLoweredNode node, FormaLoweredNode[] controls)
    {
        var name = node.FindDirective("Name");
        if (!string.IsNullOrWhiteSpace(name)) return NameScope.FindControlByName(root, name);
        return NameScope.FindControlByOrdinal(root, Array.IndexOf(controls, node));
    }

    private static FormaLoweredNode[] GetSreControls(FormaLoweredDocument document) => GetSreControls(document, document.OwnerScope);

    private static FormaLoweredNode[] GetSreControls(FormaLoweredDocument document, FormaLoweredScope scope) => scope.Operations
            .Where(operation => operation.Kind is FormaLoweredOperationKind.Construct or FormaLoweredOperationKind.Brush or FormaLoweredOperationKind.Geometry)
            .Select(operation => document.Nodes[operation.NodeId.Value])
            .Where(candidate => TryResolveSreType(document, candidate) is Type type && typeof(Control).IsAssignableFrom(type))
            .DistinctBy(candidate => candidate.Id)
            .ToArray();

    private static Expression CreateSreBinding(
        ParameterExpression root,
        Expression target,
        Type sourceType,
        PropertyInfo sourceProperty,
        Type targetType,
        PropertyInfo targetProperty,
        FormaBindingValue binding)
    {
        var mode = Enum.TryParse<BindingMode>(binding.Options.GetValueOrDefault("Mode"), out var parsedMode)
            ? parsedMode
            : BindingMode.OneWay;
        if (sourceProperty.PropertyType != targetProperty.PropertyType &&
            (mode == BindingMode.TwoWay || !targetProperty.PropertyType.IsAssignableFrom(sourceProperty.PropertyType)))
            throw new InvalidOperationException($"Binding '{binding.Path}' type '{sourceProperty.PropertyType.FullName}' is incompatible with '{targetProperty.Name}' type '{targetProperty.PropertyType.FullName}'.");
        var valueType = targetProperty.PropertyType;
        var source = Expression.Parameter(sourceType, "source");
        var targetObject = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(valueType, "value");
        var sourceValue = Expression.Property(source, sourceProperty);
        var read = Expression.Lambda(typeof(Func<,>).MakeGenericType(sourceType, valueType),
            sourceValue.Type == valueType ? sourceValue : Expression.Convert(sourceValue, valueType), source);
        var getTarget = Expression.Lambda(typeof(Func<,>).MakeGenericType(typeof(object), valueType),
            Expression.Property(Expression.Convert(targetObject, targetType), targetProperty), targetObject);
        if (mode == BindingMode.TwoWay && targetProperty.SetMethod == null)
            throw new InvalidOperationException($"Two-way binding target '{targetType.FullName}.{targetProperty.Name}' is read-only; multi-selection projections cannot be two-way binding targets.");
        var setTarget = Expression.Lambda(typeof(Action<,>).MakeGenericType(typeof(object), valueType),
            Expression.Assign(Expression.Property(Expression.Convert(targetObject, targetType), targetProperty), value), targetObject, value);
        var resolver = CreateSreBindingSourceResolver(sourceType, binding.Source);
        if (mode != BindingMode.TwoWay)
        {
            var parameterCount = resolver == null ? 6 : 7;
            var methodName = mode == BindingMode.OneTime ? nameof(CompiledBinding.AttachOneTime) : nameof(CompiledBinding.AttachOneWay);
            var attach = typeof(CompiledBinding).GetMethods().Single(method => method.Name == methodName && method.IsPublic && method.GetParameters().Length == parameterCount).MakeGenericMethod(sourceType, valueType);
            var arguments = resolver == null
                ? new Expression[] { root, Expression.Convert(target, typeof(object)), read, Expression.Constant(binding.Path), getTarget, setTarget }
                : new Expression[] { root, Expression.Convert(target, typeof(object)), resolver, read, Expression.Constant(binding.Path), getTarget, setTarget };
            return Expression.Block(Expression.Call(attach, arguments), Expression.Empty());
        }

        if (sourceProperty.SetMethod == null) throw new InvalidOperationException($"Two-way binding source '{sourceType.FullName}.{binding.Path}' is read-only.");
        var write = Expression.Lambda(typeof(Action<,>).MakeGenericType(sourceType, valueType),
            Expression.Assign(Expression.Property(source, sourceProperty), value), source, value);
        var adapter = ResolveSreBindingAdapter(targetType, targetProperty)
            ?? throw new InvalidOperationException($"Two-way binding target '{targetType.FullName}.{targetProperty.Name}' is unsupported.");
        var twoWayParameterCount = resolver == null ? 7 : 8;
        var attachTwoWay = typeof(CompiledBinding).GetMethods().Single(method => method.Name == nameof(CompiledBinding.AttachTwoWay) && method.GetParameters().Length == twoWayParameterCount).MakeGenericMethod(sourceType, valueType);
        var trigger = Enum.TryParse<UpdateSourceTrigger>(binding.Options.GetValueOrDefault("UpdateSourceTrigger"), out var parsedTrigger)
            ? parsedTrigger
            : UpdateSourceTrigger.Default;
        var twoWayArguments = resolver == null
            ? new Expression[] { root, Expression.Convert(target, typeof(object)), read, write, Expression.Constant(binding.Path), Expression.Constant(adapter, adapter.GetType()), Expression.Constant(trigger) }
            : new Expression[] { root, Expression.Convert(target, typeof(object)), resolver, read, write, Expression.Constant(binding.Path), Expression.Constant(adapter, adapter.GetType()), Expression.Constant(trigger) };
        return Expression.Block(Expression.Call(attachTwoWay, twoWayArguments), Expression.Empty());
    }

    private static LambdaExpression? CreateSreBindingSourceResolver(Type sourceType, FormaBindingSource source)
    {
        if (source.Kind == FormaBindingSourceKind.DataContext) return null;
        var target = Expression.Parameter(typeof(Control), "target");
        var methodName = source.Kind switch
        {
            FormaBindingSourceKind.Self => nameof(CompiledBindingSource.Self),
            FormaBindingSourceKind.TemplatedParent => nameof(CompiledBindingSource.TemplatedParent),
            FormaBindingSourceKind.FindAncestor => nameof(CompiledBindingSource.FindAncestor),
            _ => throw new InvalidOperationException($"Unsupported binding source '{source.Kind}'."),
        };
        var method = typeof(CompiledBindingSource).GetMethods().Single(candidate => candidate.Name == methodName).MakeGenericMethod(sourceType);
        var call = source.Kind == FormaBindingSourceKind.FindAncestor
            ? Expression.Call(method, target, Expression.Constant(source.AncestorLevel))
            : Expression.Call(method, target);
        return Expression.Lambda(typeof(Func<,>).MakeGenericType(typeof(Control), sourceType), call, target);
    }

    private static Expression CreateSreResource(
        ParameterExpression root,
        Expression target,
        Type targetType,
        PropertyInfo targetProperty,
        FormaResourceValue resource)
    {
        var valueType = targetProperty.PropertyType;
        var targetObject = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(valueType, "value");
        var getTarget = Expression.Lambda(typeof(Func<,>).MakeGenericType(typeof(object), valueType),
            Expression.Property(Expression.Convert(targetObject, targetType), targetProperty), targetObject);
        var setTarget = Expression.Lambda(typeof(Action<,>).MakeGenericType(typeof(object), valueType),
            Expression.Assign(Expression.Property(Expression.Convert(targetObject, targetType), targetProperty), value), targetObject, value);
        if (!resource.IsDynamic)
        {
            var resolve = typeof(StaticResource).GetMethod(nameof(StaticResource.Resolve))!.MakeGenericMethod(valueType);
            return Expression.Assign(Expression.Property(Expression.Convert(target, targetType), targetProperty),
                Expression.Call(resolve, Expression.Convert(target, typeof(Control)), Expression.Constant(resource.Key)));
        }

        var propertyType = typeof(XamlProperty<>).MakeGenericType(valueType);
        var propertyConstructor = propertyType.GetConstructors().Single(constructor => constructor.GetParameters().Length == 3);
        var xamlProperty = Expression.New(propertyConstructor, Expression.Constant(targetProperty.Name), getTarget, setTarget);
        var attach = typeof(DynamicResource).GetMethod(nameof(DynamicResource.Attach))!.MakeGenericMethod(valueType);
        return Expression.Block(Expression.Call(attach, root, Expression.Convert(target, typeof(Control)), xamlProperty,
            Expression.Constant(resource.Key), Expression.Constant(null, typeof(Func<,>).MakeGenericType(typeof(object), valueType)),
            Expression.Constant(XamlValueLayer.Local), Expression.Constant(0L)), Expression.Empty());
    }

    private static Expression FindSreTarget(ParameterExpression root, FormaLoweredNode node, FormaLoweredNode[] controls)
    {
        var name = node.FindDirective("Name");
        return string.IsNullOrWhiteSpace(name)
            ? Expression.Call(typeof(NameScope).GetMethod(nameof(NameScope.FindControlByOrdinal))!, root, Expression.Constant(Array.IndexOf(controls, node)))
            : Expression.Call(typeof(NameScope).GetMethod(nameof(NameScope.FindControlByName))!, root, Expression.Constant(name));
    }

    private static Expression FindSreObjectTarget(
        FormaLoweredDocument document,
        ParameterExpression root,
        FormaLoweredNode node,
        FormaLoweredNode[] controls)
    {
        var nodeType = ResolveSreType(document, node);
        if (typeof(Control).IsAssignableFrom(nodeType)) return FindSreTarget(root, node, controls);
        var propertyElement = node.ParentId is { } parentId ? document.Nodes[parentId.Value] : null;
        if (propertyElement == null || propertyElement.ParentId == null ||
            !propertyElement.TypeName.Contains('.', StringComparison.Ordinal))
            throw new InvalidOperationException($"Nonvisual XAML object '{node.TypeName}' must be owned by a typed property collection.");
        var owner = document.Nodes[propertyElement.ParentId.Value.Value];
        var ownerType = ResolveSreType(document, owner);
        var propertyName = propertyElement.TypeName.Substring(propertyElement.TypeName.LastIndexOf('.') + 1);
        var property = FindSreProperty(ownerType, propertyName)
            ?? throw new InvalidOperationException($"Collection property '{ownerType.FullName}.{propertyName}' was not found.");
        var indexer = property.PropertyType.GetProperty("Item", new[] { typeof(int) })
            ?? throw new InvalidOperationException($"Collection property '{ownerType.FullName}.{propertyName}' is not indexable.");
        var index = propertyElement.Children.Select((child, childIndex) => (child, childIndex))
            .Where(entry => entry.child == node.Id)
            .Select(entry => entry.childIndex)
            .DefaultIfEmpty(-1)
            .Single();
        if (index < 0) throw new InvalidOperationException($"Nonvisual XAML object '{node.TypeName}' was not found in its owner collection.");
        var ownerTarget = FindSreObjectTarget(document, root, owner, controls);
        return Expression.Property(
            Expression.Property(Expression.Convert(ownerTarget, ownerType), property),
            indexer,
            Expression.Constant(index));
    }

    private static Type ResolveSreType(FormaLoweredDocument document, FormaLoweredNode node) =>
        TryResolveSreType(document, node)
        ?? throw new InvalidOperationException($"XAML type '{node.XmlNamespace}:{node.TypeName}' was not resolved.");

    private static Type? TryResolveSreType(FormaLoweredDocument document, FormaLoweredNode node) =>
        node.TypeName.Contains('.', StringComparison.Ordinal) ? null : ResolveSreType(document, node.XmlNamespace, node.TypeName);

    private static Type? ResolveSreTypeReference(FormaLoweredDocument document, string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        var separator = reference.IndexOf(':');
        var prefix = separator < 0 ? string.Empty : reference.Substring(0, separator);
        var name = separator < 0 ? reference : reference.Substring(separator + 1);
        var xmlNamespace = document.Namespaces.TryGetValue(prefix, out var value) ? value : XamlNamespaces.Forma;
        return ResolveSreType(document, xmlNamespace, name);
    }

    private static Type? ResolveSreType(FormaLoweredDocument document, string xmlNamespace, string typeName)
    {
        if (xmlNamespace == XamlNamespaces.Forma)
            return typeof(Control).Assembly.GetType("Forma." + typeName, false) ?? typeof(Control).Assembly.GetType("Forma.Xaml." + typeName, false);
        if (!xmlNamespace.StartsWith("clr-namespace:", StringComparison.Ordinal)) return null;
        var definition = xmlNamespace.Substring("clr-namespace:".Length).Split(';');
        var assemblyName = definition.Skip(1).FirstOrDefault(part => part.StartsWith("assembly=", StringComparison.Ordinal))?.Substring("assembly=".Length);
        var fullName = definition[0] + "." + typeName;
        return assemblyName == null
            ? AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(fullName, false)).FirstOrDefault(type => type != null)
            : Assembly.Load(new AssemblyName(assemblyName)).GetType(fullName, false);
    }

    private static PropertyInfo? FindSreProperty(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var property = current.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (property != null) return property;
        }
        return null;
    }

    private static object? ResolveSreBindingAdapter(Type targetType, PropertyInfo property)
    {
        if (property.Name == "Text" && typeof(LineEdit).IsAssignableFrom(targetType)) return BindingTargetAdapters.LineEditText;
        if (property.Name == "Value" && typeof(Range).IsAssignableFrom(targetType)) return BindingTargetAdapters.RangeValue;
        if (property.Name == "ButtonPressed" && typeof(BaseButton).IsAssignableFrom(targetType)) return BindingTargetAdapters.ButtonPressed;
        if (property.Name == "Checked" && typeof(CheckBox).IsAssignableFrom(targetType)) return BindingTargetAdapters.CheckBoxChecked;
        if (property.Name == "Selected" && typeof(OptionButton).IsAssignableFrom(targetType)) return BindingTargetAdapters.OptionButtonSelected;
        if (property.Name == "SelectedIndex" && typeof(ListBox).IsAssignableFrom(targetType)) return BindingTargetAdapters.ListBoxSelectedIndex;
        if (property.Name == "SelectedItem" && typeof(ListBox).IsAssignableFrom(targetType)) return BindingTargetAdapters.ListBoxSelectedItem;
        return null;
    }

    private bool ConvertValue(AstTransformationContext context, IXamlAstValueNode node, IXamlType type, out IXamlAstValueNode result)
    {
        result = null!;
        if (node is not XamlAstTextNode textNode) return false;
        if (type.FullName is "Forma.SvgImageSource" or "Forma.ScalableImageSource")
        {
            if (string.IsNullOrWhiteSpace(_currentSourcePath)) return false;
            var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(_currentSourcePath))!;
            var resolvedPath = Path.GetFullPath(Path.Combine(sourceDirectory, textNode.Text));
            var svgMethodName = _useSvgFiles ? nameof(XamlValueConverter.ParseSvgFile) : nameof(XamlValueConverter.ParseSvgAsset);
            var argument = _useSvgFiles
                ? resolvedPath
                : SvgAssetLogicalName.Create(_defaultAssemblyName, _projectDirectory ?? sourceDirectory, resolvedPath);
            var svgConverterType = context.Configuration.TypeSystem.GetType(typeof(XamlValueConverter).FullName!);
            var svgMethod = svgConverterType.Methods.First(candidate => candidate.IsPublic && candidate.IsStatic && candidate.Name == svgMethodName);
            var stringType = context.Configuration.TypeSystem.GetType(typeof(string).FullName!);
            result = new XamlStaticOrTargetedReturnMethodCallNode(node, svgMethod, new[] { new XamlAstTextNode(node, argument, true, stringType) });
            return true;
        }
        var methodName = type.FullName switch
        {
            "Microsoft.Xna.Framework.Color" => nameof(XamlValueConverter.ParseColor),
            "Microsoft.Xna.Framework.Vector2" => nameof(XamlValueConverter.ParseVector2),
            "Forma.Thickness" => nameof(XamlValueConverter.ParseThickness),
            "Forma.CornerRadius" => nameof(XamlValueConverter.ParseCornerRadius),
            "Forma.GridTrackSize" => nameof(XamlValueConverter.ParseGridTrackSize),
            "Forma.Brush" => nameof(XamlValueConverter.ParseBrush),
            "Forma.Geometry" => nameof(XamlValueConverter.ParseGeometry),
            _ => null,
        };
        if (methodName == null) return false;
        var converterType = context.Configuration.TypeSystem.GetType(typeof(XamlValueConverter).FullName!);
        var method = converterType.Methods.First(candidate => candidate.IsPublic && candidate.IsStatic && candidate.Name == methodName);
        result = new XamlStaticOrTargetedReturnMethodCallNode(node, method, new[] { node });
        return true;
    }

    private sealed class StringFileSource : IFileSource
    {
        public StringFileSource(string filePath, string source) { FilePath = filePath; FileContents = Encoding.UTF8.GetBytes(source); }
        public string FilePath { get; }
        public byte[] FileContents { get; }
    }

    private sealed class RemoveDirectivesVisitor : IXamlAstVisitor
    {
        public IXamlAstNode Visit(IXamlAstNode node) => node is XamlAstXmlDirective directive ? new XamlManipulationGroupNode(directive) : node;
        public void Push(IXamlAstNode node) { }
        public void Pop() { }
    }
}

public static class SvgAssetLogicalName
{
    public static string Create(string assemblyName, string projectDirectory, string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyName)) throw new ArgumentException("Assembly name must not be empty.", nameof(assemblyName));
        var relativePath = Path.GetRelativePath(Path.GetFullPath(projectDirectory), Path.GetFullPath(assetPath));
        if (relativePath == ".." || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException($"SVG asset '{assetPath}' must be inside the project directory '{projectDirectory}'.");
        return assemblyName + ".FormaSvg." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(relativePath.Replace('\\', '/'))));
    }
}

internal sealed class FormaDirectiveTransformer : IXamlAstTransformer
{
    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal) { "Class", "Name", "Key", "DataType" };

    public IXamlAstNode Transform(AstTransformationContext context, IXamlAstNode node)
    {
        if (node is XamlAstXmlDirective directive && directive.Namespace == XamlNamespaces.Xaml2006 && !Supported.Contains(directive.Name))
            throw new XamlTransformException($"{FormaDiagnosticCodes.UnsupportedDirective}: directive x:{directive.Name} is not supported", directive);
        if (node is XamlAstXmlDirective { Namespace: XamlNamespaces.Xaml2006, Name: "Class" or "DataType" } consumed)
            return new XamlManipulationGroupNode(consumed);
        return node;
    }
}

internal sealed class FormaDirectiveEmitter : IXamlILAstNodeEmitter
{
    public XamlILNodeEmitResult? Emit(IXamlAstNode node, XamlEmitContext<IXamlILEmitter, XamlILNodeEmitResult> context, IXamlILEmitter codeGen) =>
        node is XamlAstXmlDirective ? XamlILNodeEmitResult.Void(0) : null;
}