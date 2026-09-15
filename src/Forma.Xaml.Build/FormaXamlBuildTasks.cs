// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Forma.Xaml.Compiler;
using System.Globalization;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Pdb;
using XamlX.TypeSystem;

namespace Forma.Xaml.Build;

public abstract class FormaXamlTask : Microsoft.Build.Utilities.Task
{
    protected bool LogDiagnostics(IEnumerable<FormaDiagnostic> diagnostics)
    {
        var success = true;
        foreach (var diagnostic in diagnostics)
        {
            var location = diagnostic.Location;
            if (diagnostic.Severity == FormaDiagnosticSeverity.Error)
            {
                Log.LogError("Forma XAML", diagnostic.Code, null, location.FilePath, location.Line, location.Column, location.EndLine, location.EndColumn, diagnostic.Message);
                success = false;
            }
            else if (diagnostic.Severity == FormaDiagnosticSeverity.Warning)
                Log.LogWarning("Forma XAML", diagnostic.Code, null, location.FilePath, location.Line, location.Column, location.EndLine, location.EndColumn, diagnostic.Message);
            else
                Log.LogMessage(MessageImportance.Normal, diagnostic.ToString());
        }
        return success;
    }

    protected static string ReadSource(ITaskItem item, bool authoring = false) => authoring
        ? AuthoringSourceText.Decode(File.ReadAllBytes(item.GetMetadata("FullPath")))
        : File.ReadAllText(item.GetMetadata("FullPath"));
}

public sealed class DiscoverFormaXaml : FormaXamlTask
{
    [Required] public ITaskItem[] XamlFiles { get; set; } = [];
    public bool RequireCompiledBindings { get; set; }
    public bool Authoring { get; set; }
    [Output] public ITaskItem[] Roots { get; private set; } = [];

    public override bool Execute()
    {
        var parser = new FormaXamlParser();
        var roots = new List<ITaskItem>();
        var success = true;
        foreach (var file in XamlFiles)
        {
            var source = ReadSource(file, Authoring);
            if (Authoring)
            {
                try { _ = new XamlSourceDocument(file.ItemSpec, source); }
                catch (System.Xml.XmlException error) { Log.LogError(error.Message); return false; }
            }
            var result = parser.Parse(Authoring ? AuthoringSourceText.ParserText(source) : source, file.ItemSpec,
                new FormaXamlParseOptions { RequireCompiledBindings = RequireCompiledBindings });
            success &= LogDiagnostics(result.Diagnostics);
            if (result.Document == null) continue;
            var root = new TaskItem(file);
            root.SetMetadata("RootClass", result.Document.RootClass ?? string.Empty);
            roots.Add(root);
        }

        foreach (var duplicate in roots.Where(root => !string.IsNullOrWhiteSpace(root.GetMetadata("RootClass"))).GroupBy(root => root.GetMetadata("RootClass"), StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            foreach (var item in duplicate)
                Log.LogError("Forma XAML", FormaDiagnosticCodes.DuplicateRootClass, null, item.ItemSpec, 1, 1, 0, 0, $"Multiple implicit XAML files target root class '{duplicate.Key}'.");
            success = false;
        }
        Roots = roots.ToArray();
        return success;
    }
}

public sealed class CompileFormaXaml : FormaXamlTask
{
    [Required] public ITaskItem[] XamlFiles { get; set; } = [];
    [Required] public string TargetAssembly { get; set; } = string.Empty;
    [Required] public string ProjectDirectory { get; set; } = string.Empty;
    public string? TargetPdb { get; set; }
    public ITaskItem[] References { get; set; } = [];
    public bool RequireCompiledBindings { get; set; }
    public bool ValidateOnly { get; set; }
    public bool Authoring { get; set; }
    public string? AuthoringSourceRoot { get; set; }

    public override bool Execute()
    {
        if (Authoring && (string.IsNullOrWhiteSpace(AuthoringSourceRoot) || !Path.IsPathFullyQualified(AuthoringSourceRoot)))
        {
            Log.LogError("Authoring requires an explicit absolute FormaXamlSourceRoot.");
            return false;
        }
        var parser = new FormaXamlParser();
        var documents = new List<(ITaskItem Item, string Source, FormaXamlDocument Document)>();
        var success = true;
        foreach (var file in XamlFiles)
        {
            var source = ReadSource(file, Authoring);
            var sourceIdentity = file.ItemSpec;
            if (Authoring)
            {
                try
                {
                    sourceIdentity = AuthoringSourceIdentity.RelativeDocument(AuthoringSourceRoot!, Path.GetFullPath(file.ItemSpec, ProjectDirectory));
                    _ = new XamlSourceDocument(sourceIdentity, source);
                }
                catch (Exception error) when (error is ArgumentException or System.Xml.XmlException)
                {
                    Log.LogError(error.Message);
                    return false;
                }
            }
            var result = parser.Parse(Authoring ? AuthoringSourceText.ParserText(source) : source, sourceIdentity,
                new FormaXamlParseOptions { RequireCompiledBindings = RequireCompiledBindings });
            success &= LogDiagnostics(result.Diagnostics);
            if (result.Document != null) documents.Add((file, source, result.Document));
        }
        foreach (var duplicate in documents.Where(item => item.Document.RootClass != null).GroupBy(item => item.Document.RootClass!, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            foreach (var item in duplicate)
                Log.LogError("Forma XAML", FormaDiagnosticCodes.DuplicateRootClass, null, item.Item.ItemSpec, 1, 1, 0, 0, $"Multiple implicit XAML files target root class '{duplicate.Key}'.");
            success = false;
        }
        if (!success || ValidateOnly || documents.Count == 0) return success;

        try
        {
            var referencePaths = References.Select(reference => reference.GetMetadata("FullPath"))
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Append(TargetAssembly)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            using var typeSystem = new CecilTypeSystem(referencePaths, TargetAssembly);
            var assembly = typeSystem.TargetAssemblyDefinition;
            var module = assembly.MainModule;
            var compiler = new FormaXamlCompiler(typeSystem, assembly.Name.Name, ProjectDirectory);
            foreach (var item in documents)
            {
                var lowered = new FormaXamlLowerer().Lower(
                    Authoring ? AuthoringSourceText.ParserText(item.Source) : item.Source, item.Document, item.Source);
                CompileDocument(compiler, typeSystem, module, item.Item.ItemSpec, lowered, Authoring);
            }
            var writeSymbols = !string.IsNullOrWhiteSpace(TargetPdb) && File.Exists(TargetPdb);
            assembly.Write(TargetAssembly, new WriterParameters
            {
                WriteSymbols = writeSymbols,
                SymbolWriterProvider = writeSymbols ? new PortablePdbWriterProvider() : null,
            });
            Log.LogMessage(MessageImportance.High, $"Compiled {documents.Count} Forma XAML document(s) into {TargetAssembly}.");
            return true;
        }
        catch (FormaXamlCompilationException exception)
        {
            return LogDiagnostics(exception.Diagnostics);
        }
        catch (Exception exception)
        {
            Log.LogError("Forma XAML", FormaDiagnosticCodes.Emission, null, TargetAssembly, 1, 1, 0, 0, exception.ToString());
            return false;
        }
    }

    private static void CompileDocument(FormaXamlCompiler compiler, CecilTypeSystem typeSystem, ModuleDefinition module, string sourcePath, FormaLoweredDocument lowered, bool authoring)
    {
        var suffix = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sourcePath))).Substring(0, 16);
        var generatedType = new TypeDefinition("Forma.Xaml.Generated", $"View_{suffix}", TypeAttributes.Class | TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
        var contextType = new TypeDefinition("Forma.Xaml.Generated", $"View_{suffix}Context", TypeAttributes.Class | TypeAttributes.NotPublic, module.TypeSystem.Object);
        module.Types.Add(generatedType);
        module.Types.Add(contextType);
        var rootType = lowered.RootClass == null ? null : FindType(module, lowered.RootClass)
            ?? throw new InvalidOperationException($"x:Class type '{lowered.RootClass}' was not found in {module.Assembly.Name.Name}.");
        if (authoring && rootType == null)
            throw new InvalidOperationException("Authoring metadata currently requires an x:Class view; standalone resources remain source-only.");
        ValidateTemplates(typeSystem, module, lowered);
        var eventMemberNames = FindEventMemberNames(typeSystem, module, lowered);
        Dictionary<int, string>? sourceMetadata = null;
        if (authoring)
        {
            var forma = typeSystem.Resolve(module.AssemblyReferences.Single(reference => reference.Name == "Forma"));
            var control = forma.MainModule.GetType("Forma.Control");
            sourceMetadata = OwnerNodes(lowered)
                .Where(node => IsControl(ResolveObjectType(typeSystem, module, node, lowered), control))
                .ToDictionary(node => node.Id.Value, node => SourceMetadata(lowered, node,
                    node.Id == lowered.RootNodeId ? rootType!.FullName : ResolveObjectType(typeSystem, module, node, lowered)!.FullName));
            var manifest = lowered.Nodes.Select(node => SourceNode(lowered, node,
                node.Id == lowered.RootNodeId ? rootType!.FullName :
                ResolveObjectType(typeSystem, module, node, lowered)?.FullName ?? node.TypeName)).ToArray();
            sourceMetadata[lowered.RootNodeId.Value] = System.Text.Json.JsonSerializer.Serialize(
                manifest[lowered.RootNodeId.Value] with { DocumentNodes = manifest });
        }
        compiler.CompileCecil(lowered, typeSystem, generatedType, contextType, eventMemberNames, sourceMetadata);
        if (rootType == null)
        {
            rootType = ResolveObjectType(typeSystem, module, lowered.Nodes[lowered.RootNodeId.Value], lowered)
                ?? throw new InvalidOperationException($"XAML root type '{lowered.Nodes[lowered.RootNodeId.Value].TypeName}' could not be resolved.");
            RegisterFactory(typeSystem, module, generatedType, rootType);
            return;
        }
        RegisterPopulate(typeSystem, module, generatedType, rootType, lowered, authoring);
    }

    private static string SourceMetadata(FormaLoweredDocument document, FormaLoweredNode node, string type) =>
        System.Text.Json.JsonSerializer.Serialize(SourceNode(document, node, type));

    private static XamlSourceNode SourceNode(FormaLoweredDocument document, FormaLoweredNode node, string type) =>
        AuthoringSourceText.Describe(document, node, type);

    private static void RegisterFactory(CecilTypeSystem typeSystem, ModuleDefinition module, TypeDefinition generatedType, TypeDefinition rootType)
    {
        var formaReference = module.AssemblyReferences.Single(reference => reference.Name == "Forma");
        var formaAssembly = typeSystem.Resolve(formaReference);
        var loaderType = formaAssembly.MainModule.GetType("Forma.Xaml.FormaXamlLoader");
        var serviceProvider = module.ImportReference(typeof(IServiceProvider));
        var rootReference = module.ImportReference(rootType);
        var build = generatedType.Methods.Single(method => method.Name == "Build");
        var populate = generatedType.Methods.Single(method => method.Name == "Populate");

        var funcDefinition = module.ImportReference(typeof(Func<,>));
        var funcType = new GenericInstanceType(funcDefinition);
        funcType.GenericArguments.Add(serviceProvider);
        funcType.GenericArguments.Add(rootReference);
        var funcConstructor = new MethodReference(".ctor", module.TypeSystem.Void, funcType) { HasThis = true };
        funcConstructor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
        funcConstructor.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));

        var actionDefinition = module.ImportReference(typeof(Action<,>));
        var actionType = new GenericInstanceType(actionDefinition);
        actionType.GenericArguments.Add(serviceProvider);
        actionType.GenericArguments.Add(rootReference);
        var actionConstructor = new MethodReference(".ctor", module.TypeSystem.Void, actionType) { HasThis = true };
        actionConstructor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
        actionConstructor.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));

        var register = new GenericInstanceMethod(module.ImportReference(loaderType.Methods.Single(method => method.Name == "Register")));
        register.GenericArguments.Add(rootReference);
        var initializer = GetOrCreateModuleInitializer(module);
        var processor = initializer.Body.GetILProcessor();
        var insertion = initializer.Body.Instructions[0];
        processor.InsertBefore(insertion, Instruction.Create(OpCodes.Ldnull));
        processor.InsertBefore(insertion, Instruction.Create(OpCodes.Ldftn, build));
        processor.InsertBefore(insertion, Instruction.Create(OpCodes.Newobj, funcConstructor));
        processor.InsertBefore(insertion, Instruction.Create(OpCodes.Ldnull));
        processor.InsertBefore(insertion, Instruction.Create(OpCodes.Ldftn, populate));
        processor.InsertBefore(insertion, Instruction.Create(OpCodes.Newobj, actionConstructor));
        processor.InsertBefore(insertion, Instruction.Create(OpCodes.Call, register));
    }

    private static MethodDefinition GetOrCreateModuleInitializer(ModuleDefinition module)
    {
        var moduleType = module.Types.Single(type => type.Name == "<Module>");
        var initializer = moduleType.Methods.FirstOrDefault(method => method.Name == ".cctor");
        if (initializer != null) return initializer;
        initializer = new MethodDefinition(".cctor", MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
        initializer.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        moduleType.Methods.Add(initializer);
        return initializer;
    }

    private static IReadOnlyCollection<string> FindEventMemberNames(CecilTypeSystem typeSystem, ModuleDefinition module, FormaLoweredDocument lowered)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in OwnerNodes(lowered))
        {
            var type = ResolveObjectType(typeSystem, module, node, lowered);
            if (type == null) continue;
            foreach (var member in node.Members.Where(member => !member.IsDirective))
                if (FindEvent(type, member.Name) != null) names.Add(member.Name);
        }
        return names;
    }

    private static void ValidateTemplates(CecilTypeSystem typeSystem, ModuleDefinition module, FormaLoweredDocument lowered)
    {
        var diagnostics = new List<FormaDiagnostic>();
        var formaAssembly = typeSystem.Resolve(module.AssemblyReferences.Single(reference => reference.Name == "Forma"));
        var controlType = formaAssembly.MainModule.GetType("Forma.Control");
        var templatedControlType = formaAssembly.MainModule.GetType("Forma.TemplatedControl");
        var keyedTemplates = lowered.Templates
            .Select(template => (Template: template, Node: lowered.Nodes[template.NodeId.Value]))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Node.FindDirective("Key")))
            .GroupBy(entry => entry.Node.FindDirective("Key")!, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Template, StringComparer.Ordinal);
        foreach (var template in lowered.Templates)
        {
            var node = lowered.Nodes[template.NodeId.Value];
            if (template.Kind == FormaXamlTemplateKind.Data)
            {
                if (template.DataTypeSymbolId.IsResolved && ResolveSymbolType(typeSystem, module, lowered, template.DataTypeSymbolId) == null)
                    diagnostics.Add(TemplateDiagnostic(node.SourceRange, $"DataTemplate x:DataType '{node.FindDirective("DataType")}' could not be resolved."));
                foreach (var candidate in ScopeNodes(lowered, template.Scope))
                {
                    var candidateType = ResolveObjectType(typeSystem, module, candidate, lowered);
                    if (candidateType == null) continue;
                    foreach (var member in candidate.Members.Where(member => !member.IsDirective && FindEvent(candidateType, member.Name) != null))
                        diagnostics.Add(TemplateDiagnostic(member.SourceRange,
                            $"Event attribute '{member.Name}' is not allowed directly inside DataTemplate; instantiate a separate x:Class row control and handle the event there."));
                }
            }
            else if (template.Kind == FormaXamlTemplateKind.Control)
            {
                var targetType = ResolveSymbolType(typeSystem, module, lowered, template.TargetTypeSymbolId);
                if (targetType == null)
                    diagnostics.Add(TemplateDiagnostic(node.SourceRange, $"ControlTemplate TargetType '{node.FindMember("TargetType")}' could not be resolved."));
                else if (!IsControl(targetType, templatedControlType))
                    diagnostics.Add(TemplateDiagnostic(node.SourceRange, $"ControlTemplate TargetType '{node.FindMember("TargetType")}' must derive from TemplatedControl."));
            }

            if (string.IsNullOrWhiteSpace(node.FindDirective("Key")))
            {
                try
                {
                    var (owner, propertyName) = ResolveTemplatePropertyOwner(lowered, node);
                    var ownerType = ResolveObjectType(typeSystem, module, owner, lowered);
                    var property = ownerType == null ? null : FindProperty(ownerType, propertyName);
                    var templateTypeName = template.Kind switch
                    {
                        FormaXamlTemplateKind.Data => "Forma.DataTemplate",
                        FormaXamlTemplateKind.Control => "Forma.ControlTemplate",
                        FormaXamlTemplateKind.ItemsPanel => "Forma.ItemsPanelTemplate",
                        _ => string.Empty,
                    };
                    if (property == null || property.SetMethod == null || property.PropertyType.FullName != templateTypeName)
                        diagnostics.Add(TemplateDiagnostic(node.SourceRange,
                            $"{node.TypeName} is not assignable to the direct property element '{owner.TypeName}.{propertyName}'."));
                }
                catch (InvalidOperationException exception)
                {
                    diagnostics.Add(TemplateDiagnostic(node.SourceRange, exception.Message));
                }
            }
        }
        foreach (var node in lowered.Nodes)
        {
            var nodeType = ResolveObjectType(typeSystem, module, node, lowered);
            if (nodeType == null) continue;
            foreach (var member in node.Members.Where(member => !member.IsDirective && member.Value is FormaResourceValue { IsDynamic: false }))
            {
                var resource = (FormaResourceValue)member.Value;
                if (!keyedTemplates.TryGetValue(resource.Key, out var template)) continue;
                var property = FindProperty(nodeType, member.Name);
                if (property == null) continue;
                var templateTypeName = TemplateTypeName(template.Kind);
                if (property.PropertyType.FullName != templateTypeName)
                    diagnostics.Add(TemplateDiagnostic(member.SourceRange,
                        $"Template resource '{resource.Key}' has type {templateTypeName} and is not assignable to '{nodeType.FullName}.{member.Name}' of type {property.PropertyType.FullName}."));
            }
        }
        foreach (var styleNode in lowered.Nodes.Where(node => node.TypeName == "Style"))
        {
            var selectorMember = styleNode.Members.FirstOrDefault(member => !member.IsDirective && member.Name == "Selector");
            if (selectorMember == null) continue;
            try
            {
                var selector = styleNode.Selector ?? throw new InvalidOperationException("Style requires a lowered Selector.");
                foreach (var arm in selector.Arms)
                {
                    foreach (var compound in arm.Compounds.Where(compound => !string.IsNullOrWhiteSpace(compound.TypeName)))
                        _ = ResolveStyleSelectorType(typeSystem, module, lowered, compound.TypeName!, templatedControlType);
                    foreach (var compound in arm.Compounds)
                        ValidateStylePseudoStates(typeSystem, module, lowered, compound, controlType);
                    for (var index = 0; index < arm.Combinators.Count; index++)
                    {
                        if (arm.Combinators[index] != StyleSelectorCombinator.TemplateChild) continue;
                        var typeName = arm.Compounds[index].TypeName;
                        if (string.IsNullOrWhiteSpace(typeName)) continue;
                        var type = ResolveStyleSelectorType(typeSystem, module, lowered, typeName, templatedControlType);
                        if (!DerivesFrom(type, templatedControlType))
                            throw new InvalidOperationException($"Selector template crossing requires a TemplatedControl on the left, but '{typeName}' is foundational.");
                    }
                }
                foreach (var setter in styleNode.Children.Select(child => lowered.Nodes[child.Value]).Where(child => child.TypeName == "Setter"))
                {
                    var propertyName = setter.FindMember("Property");
                    if (propertyName == null) continue;
                    _ = ResolveStyleTargetProperty(typeSystem, module, lowered, selector, propertyName, templatedControlType);
                }
                var condition = FindAdaptiveCondition(lowered, styleNode);
                if (condition != null)
                {
                    var validation = new AdaptiveCondition();
                    foreach (var member in condition.Members.Where(member => !member.IsDirective))
                        validation.SetCompiledValue(member.Name, member.Value.RawText);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException)
            {
                diagnostics.Add(StyleDiagnostic(selectorMember.SourceRange, exception.Message));
            }
        }
        if (diagnostics.Count != 0) throw new FormaXamlCompilationException(diagnostics);
    }

    private static string TemplateTypeName(FormaXamlTemplateKind kind) => kind switch
    {
        FormaXamlTemplateKind.Data => "Forma.DataTemplate",
        FormaXamlTemplateKind.Control => "Forma.ControlTemplate",
        FormaXamlTemplateKind.ItemsPanel => "Forma.ItemsPanelTemplate",
        _ => string.Empty,
    };

    private static FormaDiagnostic TemplateDiagnostic(FormaSourceRange range, string message) =>
        new(FormaDiagnosticCodes.Template, FormaDiagnosticSeverity.Error, message,
            new FormaSourceLocation(range.FilePath, range.StartLine, range.StartColumn, range.EndLine, range.EndColumn));

    private static FormaDiagnostic StyleDiagnostic(FormaSourceRange range, string message) =>
        new(FormaDiagnosticCodes.Selector, FormaDiagnosticSeverity.Error, message,
            new FormaSourceLocation(range.FilePath, range.StartLine, range.StartColumn, range.EndLine, range.EndColumn));

    private static TypeDefinition? FindType(ModuleDefinition module, string fullName) =>
        module.Types.SelectMany(AllTypes).FirstOrDefault(type => type.FullName.Replace('/', '+') == fullName || type.FullName == fullName);

    private static IEnumerable<TypeDefinition> AllTypes(TypeDefinition type)
    {
        yield return type;
        foreach (var nested in type.NestedTypes.SelectMany(AllTypes)) yield return nested;
    }

    private static void RegisterPopulate(CecilTypeSystem typeSystem, ModuleDefinition module, TypeDefinition generatedType, TypeDefinition rootType, FormaLoweredDocument lowered, bool authoring)
    {
        var formaReference = module.AssemblyReferences.Single(reference => reference.Name == "Forma");
        var formaAssembly = typeSystem.Resolve(formaReference);
        var nameScopeType = formaAssembly.MainModule.GetType("Forma.Xaml.NameScope");
        var loaderType = formaAssembly.MainModule.GetType("Forma.Xaml.FormaXamlLoader");
        var generatedPopulate = generatedType.Methods.Single(method => method.Name == "Populate");
        var serviceProvider = module.ImportReference(typeof(IServiceProvider));
        var wrapper = new MethodDefinition("__Populate", MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.Void);
        wrapper.Parameters.Add(new ParameterDefinition("serviceProvider", ParameterAttributes.None, serviceProvider));
        wrapper.Parameters.Add(new ParameterDefinition("instance", ParameterAttributes.None, rootType));
        generatedType.Methods.Add(wrapper);
        var il = wrapper.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, generatedPopulate);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, module.ImportReference(nameScopeType.Methods.Single(method => method.Name == "CreateForTree")));
        il.Emit(OpCodes.Pop);
        EmitTemplateScopeAttachments(typeSystem, module, generatedType, lowered);
        EmitTemplates(typeSystem, module, generatedType, wrapper, lowered);
        EmitStyles(typeSystem, module, generatedType, wrapper, lowered, authoring);
        EmitStoryboardsAndTriggers(typeSystem, module, generatedType, wrapper, lowered);
        EmitResourceReferences(typeSystem, module, generatedType, wrapper, lowered);
        EmitEvents(typeSystem, module, generatedType, wrapper, rootType, lowered);
        EmitBindings(typeSystem, module, generatedType, wrapper, rootType, lowered);
        il.Emit(OpCodes.Ret);

        var actionDefinition = module.ImportReference(typeof(Action<,>));
        var actionType = new GenericInstanceType(actionDefinition);
        actionType.GenericArguments.Add(serviceProvider);
        actionType.GenericArguments.Add(rootType);
        var actionConstructor = new MethodReference(".ctor", module.TypeSystem.Void, actionType) { HasThis = true };
        actionConstructor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
        actionConstructor.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));
        var register = new GenericInstanceMethod(module.ImportReference(loaderType.Methods.Single(method => method.Name == "RegisterPopulate")));
        register.GenericArguments.Add(rootType);

        var initializer = GetOrCreateModuleInitializer(module);
        var processor = initializer.Body.GetILProcessor();
        var insertion = initializer.Body.Instructions[0];
        processor.InsertBefore(insertion, Instruction.Create(OpCodes.Ldnull));
        processor.InsertBefore(insertion, Instruction.Create(OpCodes.Ldftn, wrapper));
        processor.InsertBefore(insertion, Instruction.Create(OpCodes.Newobj, actionConstructor));
        processor.InsertBefore(insertion, Instruction.Create(OpCodes.Call, register));
    }

    private static void EmitTemplateScopeAttachments(CecilTypeSystem typeSystem, ModuleDefinition module, TypeDefinition generatedType, FormaLoweredDocument lowered)
    {
        if (lowered.Templates.Count == 0) return;
        var formaAssembly = typeSystem.Resolve(module.AssemblyReferences.Single(reference => reference.Name == "Forma"));
        var controlType = formaAssembly.MainModule.GetType("Forma.Control");
        var nameScopeType = formaAssembly.MainModule.GetType("Forma.Xaml.NameScope");
        var compiledBindingType = formaAssembly.MainModule.GetType("Forma.Xaml.CompiledBinding");
        var compiledBindingSourceType = formaAssembly.MainModule.GetType("Forma.Xaml.CompiledBindingSource");
        var staticResourceType = formaAssembly.MainModule.GetType("Forma.Xaml.StaticResource");
        var dynamicResourceType = formaAssembly.MainModule.GetType("Forma.Xaml.DynamicResource");
        var xamlPropertyDefinition = formaAssembly.MainModule.GetType("Forma.Xaml.XamlProperty`1");
        var resourceDictionaryType = formaAssembly.MainModule.GetType("Forma.Xaml.ResourceDictionary");
        var styleType = formaAssembly.MainModule.GetType("Forma.Xaml.Style");
        var styleSetterDefinition = formaAssembly.MainModule.GetType("Forma.Xaml.StyleSetter`1");
        var styleEngineType = formaAssembly.MainModule.GetType("Forma.Xaml.StyleEngine");
        var bindingAdaptersType = formaAssembly.MainModule.GetType("Forma.Xaml.BindingTargetAdapters");
        var createNameScope = module.ImportReference(nameScopeType.Methods.Single(method => method.Name == "CreateForTree"));
        var findOrdinal = module.ImportReference(nameScopeType.Methods.Single(method => method.Name == "FindControlByOrdinal" && method.IsPublic && method.Parameters.Count == 2));
        var findName = module.ImportReference(nameScopeType.Methods.Single(method => method.Name == "FindControlByName" && method.IsPublic && method.Parameters.Count == 2));
        var attachDefinition = module.ImportReference(compiledBindingType.Methods.Single(method => method.Name == "AttachOneWay" && method.Parameters.Count == 6));
        var attachTwoWayDefinition = module.ImportReference(compiledBindingType.Methods.Single(method => method.Name == "AttachTwoWay" && method.Parameters.Count == 7));
        var attachRelativeDefinition = module.ImportReference(compiledBindingType.Methods.Single(method => method.Name == "AttachOneWay" && method.Parameters.Count == 7));
        var attachRelativeTwoWayDefinition = module.ImportReference(compiledBindingType.Methods.Single(method => method.Name == "AttachTwoWay" && method.Parameters.Count == 8));
        var attachOneTimeDefinition = module.ImportReference(compiledBindingType.Methods.Single(method => method.Name == "AttachOneTime" && method.IsPublic && method.Parameters.Count == 6));
        var attachRelativeOneTimeDefinition = module.ImportReference(compiledBindingType.Methods.Single(method => method.Name == "AttachOneTime" && method.IsPublic && method.Parameters.Count == 7));
        var resolveResourceDefinition = module.ImportReference(staticResourceType.Methods.Single(method => method.Name == "Resolve"));
        var attachResourceDefinition = module.ImportReference(dynamicResourceType.Methods.Single(method => method.Name == "Attach"));
        var styleConstructor = module.ImportReference(styleType.Methods.Single(method => method.IsConstructor && method.Parameters.Count == 1 &&
            method.Parameters[0].ParameterType.FullName == "Forma.Xaml.StyleSelector"));
        var addSetter = module.ImportReference(styleType.Methods.Single(method => method.Name == "AddSetter"));
        var resourcesGetter = module.ImportReference(FindProperty(controlType, "Resources")!.GetMethod);
        var addResource = module.ImportReference(resourceDictionaryType.Methods.Single(method => method.Name == "Add" && method.Parameters.Count == 2 && method.Parameters[0].ParameterType.FullName == "System.String"));
        var attachStyles = module.ImportReference(styleEngineType.Methods.Single(method => method.Name == "Attach"));

        for (var templateIndex = 0; templateIndex < lowered.Templates.Count; templateIndex++)
        {
            var template = lowered.Templates[templateIndex];
            var hook = generatedType.Methods.Single(method => method.Name == $"__TemplateAttach{templateIndex}");
            hook.Body.Instructions.Clear();
            var body = hook.Body.GetILProcessor();
            body.Emit(OpCodes.Ldarg_1);
            body.Emit(OpCodes.Call, createNameScope);
            body.Emit(OpCodes.Pop);
            var controls = ScopeNodes(lowered, template.Scope)
                .Where(node => IsControl(ResolveObjectType(typeSystem, module, node, lowered), controlType))
                .ToArray();
            var dataContextType = template.Kind == FormaXamlTemplateKind.Data
                ? ResolveSymbolType(typeSystem, module, lowered, template.DataTypeSymbolId)
                    ?? throw new InvalidOperationException("Compiled data-template binding emission requires a resolvable x:DataType.")
                : null;
            var bindings = template.Scope.Operations
                .Where(operation => operation.Kind == FormaLoweredOperationKind.Binding)
                .Select(operation => (Node: NodeForId(lowered, operation.NodeId), Member: MemberForOperation(lowered, operation)))
                .Where(binding => !HasAncestor(lowered, binding.Node, "Style") && !HasAncestor(lowered, binding.Node, "EventTrigger") &&
                    !HasAncestor(lowered, binding.Node, "PropertyTrigger") && !HasAncestor(lowered, binding.Node, "Storyboard"))
                .ToArray();
            for (var bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
            {
                    var binding = bindings[bindingIndex];
                    var targetType = ResolveObjectType(typeSystem, module, binding.Node, lowered)
                        ?? throw new InvalidOperationException($"Template binding target type '{binding.Node.TypeName}' was not resolved.");
                    var targetProperty = FindProperty(targetType, binding.Member.Name)
                        ?? throw new InvalidOperationException($"Template binding target property '{targetType.FullName}.{binding.Member.Name}' was not found.");
                    var bindingValue = (FormaBindingValue)binding.Member.Value;
                    var sourceType = ResolveRequiredBindingSourceType(typeSystem, module, lowered, binding.Member, bindingValue, dataContextType);
                    var sourceProperty = FindProperty(sourceType, bindingValue.Path)
                        ?? throw new InvalidOperationException($"Template binding source property '{sourceType.FullName}.{bindingValue.Path}' was not found.");
                    if (sourceProperty.PropertyType.FullName != targetProperty.PropertyType.FullName)
                        throw new InvalidOperationException($"Template binding '{bindingValue.Path}' is incompatible with '{binding.Member.Name}'.");
                    if (sourceProperty.GetMethod == null || targetProperty.GetMethod == null || targetProperty.SetMethod == null)
                        throw new InvalidOperationException($"Template binding '{bindingValue.Path}' requires readable source and readable/writable target properties.");
                    var valueType = module.ImportReference(sourceProperty.PropertyType);
                    var prefix = $"__TemplateBinding{templateIndex}_{bindingIndex}";
                    var read = DefineSourceGetter(module, generatedType, $"{prefix}Read", sourceType, sourceProperty, valueType);
                    var getTarget = DefineTargetGetter(module, generatedType, $"{prefix}GetTarget", targetType, targetProperty, valueType);
                    var setTarget = DefineTargetSetter(module, generatedType, $"{prefix}SetTarget", targetType, targetProperty, valueType);
                    var funcSource = MakeDelegateType(module, typeof(Func<,>), module.ImportReference(sourceType), valueType);
                    var funcTarget = MakeDelegateType(module, typeof(Func<,>), module.TypeSystem.Object, valueType);
                    var actionTarget = MakeDelegateType(module, typeof(Action<,>), module.TypeSystem.Object, valueType);
                    var mode = Enum.TryParse<BindingMode>(bindingValue.Options.GetValueOrDefault("Mode"), out var parsedMode) ? parsedMode : BindingMode.OneWay;
                    var twoWay = mode == BindingMode.TwoWay;
                    var relative = bindingValue.Source.Kind != FormaBindingSourceKind.DataContext;
                    var attach = new GenericInstanceMethod(mode switch
                    {
                        BindingMode.OneTime => relative ? attachRelativeOneTimeDefinition : attachOneTimeDefinition,
                        BindingMode.TwoWay => relative ? attachRelativeTwoWayDefinition : attachTwoWayDefinition,
                        _ => relative ? attachRelativeDefinition : attachDefinition,
                    });
                    attach.GenericArguments.Add(module.ImportReference(sourceType));
                    attach.GenericArguments.Add(valueType);
                    body.Emit(OpCodes.Ldarg_1);
                    EmitFindTemplateControl(body, binding.Node, Array.IndexOf(controls, binding.Node), findOrdinal, findName);
                    if (relative)
                    {
                        var resolver = DefineBindingSourceResolver(module, generatedType, $"{prefix}Resolve", sourceType,
                            bindingValue.Source, compiledBindingSourceType, controlType);
                        var funcResolver = MakeDelegateType(module, typeof(Func<,>), module.ImportReference(controlType), module.ImportReference(sourceType));
                        EmitDelegate(body, funcResolver, resolver);
                    }
                    EmitDelegate(body, funcSource, read);
                    if (twoWay)
                    {
                        if (sourceProperty.SetMethod == null) throw new InvalidOperationException($"Two-way template binding source '{sourceType.FullName}.{bindingValue.Path}' is read-only.");
                        var adapterName = ResolveBindingAdapter(targetType, targetProperty)
                            ?? throw new InvalidOperationException($"Two-way template binding target '{targetType.FullName}.{targetProperty.Name}' is unsupported.");
                        var write = DefineSourceSetter(module, generatedType, $"{prefix}Write", sourceType, sourceProperty, valueType);
                        var actionSource = MakeDelegateType(module, typeof(Action<,>), module.ImportReference(sourceType), valueType);
                        EmitDelegate(body, actionSource, write);
                        body.Emit(OpCodes.Ldstr, bindingValue.Path);
                        body.Emit(OpCodes.Ldsfld, module.ImportReference(bindingAdaptersType.Fields.Single(field => field.Name == adapterName)));
                        body.Emit(OpCodes.Ldc_I4, (int)ParseUpdateSourceTrigger(bindingValue));
                    }
                    else
                    {
                        body.Emit(OpCodes.Ldstr, bindingValue.Path);
                        EmitDelegate(body, funcTarget, getTarget);
                        EmitDelegate(body, actionTarget, setTarget);
                    }
                    body.Emit(OpCodes.Call, attach);
                    body.Emit(OpCodes.Pop);
            }
            var resources = template.Scope.Operations
                .Where(operation => operation.Kind == FormaLoweredOperationKind.ResourceReference)
                .Select(operation => (Node: NodeForId(lowered, operation.NodeId), Member: MemberForOperation(lowered, operation)))
                .Where(reference => !HasAncestor(lowered, reference.Node, "Style") && !HasAncestor(lowered, reference.Node, "EventTrigger") &&
                    !HasAncestor(lowered, reference.Node, "PropertyTrigger") && !HasAncestor(lowered, reference.Node, "Storyboard"))
                .ToArray();
            for (var resourceIndex = 0; resourceIndex < resources.Length; resourceIndex++)
            {
                var reference = resources[resourceIndex];
                var resource = (FormaResourceValue)reference.Member.Value;
                var targetType = ResolveObjectType(typeSystem, module, reference.Node, lowered)
                    ?? throw new InvalidOperationException($"Template resource target type '{reference.Node.TypeName}' was not resolved.");
                var targetProperty = FindProperty(targetType, reference.Member.Name)
                    ?? throw new InvalidOperationException($"Template resource target property '{targetType.FullName}.{reference.Member.Name}' was not found.");
                var valueType = module.ImportReference(targetProperty.PropertyType);
                var targetVariable = new VariableDefinition(module.ImportReference(controlType));
                hook.Body.Variables.Add(targetVariable);
                hook.Body.InitLocals = true;
                EmitFindTemplateControl(body, reference.Node, Array.IndexOf(controls, reference.Node), findOrdinal, findName);
                body.Emit(OpCodes.Stloc, targetVariable);
                if (!resource.IsDynamic)
                {
                    var resolve = new GenericInstanceMethod(resolveResourceDefinition);
                    resolve.GenericArguments.Add(valueType);
                    body.Emit(OpCodes.Ldloc, targetVariable);
                    body.Emit(OpCodes.Castclass, module.ImportReference(targetType));
                    body.Emit(OpCodes.Ldloc, targetVariable);
                    body.Emit(OpCodes.Ldstr, resource.Key);
                    body.Emit(OpCodes.Call, resolve);
                    body.Emit(OpCodes.Callvirt, module.ImportReference(targetProperty.SetMethod));
                    continue;
                }
                var prefix = $"__TemplateResource{templateIndex}_{resourceIndex}";
                var getTarget = DefineTargetGetter(module, generatedType, $"{prefix}GetTarget", targetType, targetProperty, valueType);
                var setTarget = DefineTargetSetter(module, generatedType, $"{prefix}SetTarget", targetType, targetProperty, valueType);
                var funcTarget = MakeDelegateType(module, typeof(Func<,>), module.TypeSystem.Object, valueType);
                var actionTarget = MakeDelegateType(module, typeof(Action<,>), module.TypeSystem.Object, valueType);
                var xamlPropertyType = new GenericInstanceType(module.ImportReference(xamlPropertyDefinition));
                xamlPropertyType.GenericArguments.Add(valueType);
                var propertyConstructor = MakeClosedMethod(module, xamlPropertyDefinition.Methods.Single(method => method.IsConstructor && !method.IsStatic), xamlPropertyType);
                var attachResource = new GenericInstanceMethod(attachResourceDefinition);
                attachResource.GenericArguments.Add(valueType);
                body.Emit(OpCodes.Ldarg_1);
                body.Emit(OpCodes.Ldloc, targetVariable);
                body.Emit(OpCodes.Ldstr, targetProperty.Name);
                EmitDelegate(body, funcTarget, getTarget);
                EmitDelegate(body, actionTarget, setTarget);
                body.Emit(OpCodes.Newobj, propertyConstructor);
                body.Emit(OpCodes.Ldstr, resource.Key);
                body.Emit(OpCodes.Ldnull);
                body.Emit(OpCodes.Ldc_I4, (int)XamlValueLayer.Local);
                body.Emit(OpCodes.Ldc_I8, 0L);
                body.Emit(OpCodes.Call, attachResource);
                body.Emit(OpCodes.Pop);
            }
            var styleNodes = template.Scope.Operations
                .Where(operation => operation.Kind == FormaLoweredOperationKind.Style)
                .Select(operation => lowered.Nodes[operation.NodeId.Value])
                .ToArray();
            var emittedStyles = new List<(FormaLoweredNode Owner, VariableDefinition Style)>();
            for (var styleIndex = 0; styleIndex < styleNodes.Length; styleIndex++)
            {
                var styleNode = styleNodes[styleIndex];
                var selector = styleNode.Selector ?? throw new InvalidOperationException("Style requires a lowered Selector.");
                var owner = FindResourceOwner(lowered, styleNode) ?? lowered.Nodes[template.NodeId.Value].Children.Select(id => lowered.Nodes[id.Value]).Single(node => !node.TypeName.Contains('.'));
                var styleVariable = new VariableDefinition(module.ImportReference(styleType));
                hook.Body.Variables.Add(styleVariable);
                hook.Body.InitLocals = true;
                EmitStyleSelector(body, module, formaAssembly, selector);
                body.Emit(OpCodes.Newobj, styleConstructor);
                body.Emit(OpCodes.Stloc, styleVariable);
                EmitAdaptiveCondition(body, module, formaAssembly, hook, styleVariable, lowered, styleNode);
                var setters = styleNode.Children.Select(child => lowered.Nodes[child.Value]).Where(child => child.TypeName == "Setter").ToArray();
                for (var setterIndex = 0; setterIndex < setters.Length; setterIndex++)
                {
                    var setter = setters[setterIndex];
                    var propertyName = setter.FindMember("Property") ?? throw new InvalidOperationException("Setter requires Property.");
                    var valueMember = setter.Members.SingleOrDefault(member => !member.IsDirective && member.Name == "Value")
                        ?? throw new InvalidOperationException("Setter requires Value.");
                    var (targetType, property) = ResolveStyleTargetProperty(typeSystem, module, lowered, selector, propertyName, controlType);
                    var valueType = module.ImportReference(property.PropertyType);
                    var prefix = $"__TemplateStyle{templateIndex}_{styleIndex}_{setterIndex}";
                    var getTarget = DefineTargetGetter(module, generatedType, $"{prefix}GetTarget", targetType, property, valueType);
                    var setTarget = DefineTargetSetter(module, generatedType, $"{prefix}SetTarget", targetType, property, valueType);
                    var funcTarget = MakeDelegateType(module, typeof(Func<,>), module.TypeSystem.Object, valueType);
                    var actionTarget = MakeDelegateType(module, typeof(Action<,>), module.TypeSystem.Object, valueType);
                    var xamlPropertyType = new GenericInstanceType(module.ImportReference(xamlPropertyDefinition));
                    xamlPropertyType.GenericArguments.Add(valueType);
                    var xamlPropertyConstructor = MakeClosedMethod(module, xamlPropertyDefinition.Methods.Single(method => method.IsConstructor && !method.IsStatic), xamlPropertyType);
                    var styleSetterType = new GenericInstanceType(module.ImportReference(styleSetterDefinition));
                    styleSetterType.GenericArguments.Add(valueType);
                    body.Emit(OpCodes.Ldloc, styleVariable);
                    body.Emit(OpCodes.Ldstr, propertyName);
                    EmitDelegate(body, funcTarget, getTarget);
                    EmitDelegate(body, actionTarget, setTarget);
                    body.Emit(OpCodes.Newobj, xamlPropertyConstructor);
                    EmitStyleValue(body, module, formaAssembly, valueType, valueMember.Value.RawText);
                    var setterConstructor = styleSetterDefinition.Methods.Single(method => method.IsConstructor && method.Parameters.Count == 2 && method.Parameters[1].ParameterType is GenericParameter);
                    body.Emit(OpCodes.Newobj, MakeClosedMethod(module, setterConstructor, styleSetterType));
                    body.Emit(OpCodes.Callvirt, addSetter);
                }
                var key = styleNode.FindDirective("Key");
                if (!string.IsNullOrWhiteSpace(key))
                {
                    EmitFindTemplateControl(body, owner, Array.IndexOf(controls, owner), findOrdinal, findName);
                    body.Emit(OpCodes.Callvirt, resourcesGetter);
                    body.Emit(OpCodes.Ldstr, key);
                    body.Emit(OpCodes.Ldloc, styleVariable);
                    body.Emit(OpCodes.Callvirt, addResource);
                }
                emittedStyles.Add((owner, styleVariable));
            }
            foreach (var group in emittedStyles.GroupBy(item => item.Owner))
            {
                EmitFindTemplateControl(body, group.Key, Array.IndexOf(controls, group.Key), findOrdinal, findName);
                body.Emit(OpCodes.Ldc_I4, group.Count());
                body.Emit(OpCodes.Newarr, module.ImportReference(styleType));
                var styleArrayIndex = 0;
                foreach (var item in group)
                {
                    body.Emit(OpCodes.Dup);
                    body.Emit(OpCodes.Ldc_I4, styleArrayIndex++);
                    body.Emit(OpCodes.Ldloc, item.Style);
                    body.Emit(OpCodes.Stelem_Ref);
                }
                body.Emit(OpCodes.Call, attachStyles);
                body.Emit(OpCodes.Pop);
            }
            var triggerSourceType = template.Kind == FormaXamlTemplateKind.Data
                ? ResolveSymbolType(typeSystem, module, lowered, template.DataTypeSymbolId)
                : null;
            EmitStoryboardsAndTriggers(
                typeSystem,
                module,
                generatedType,
                hook,
                lowered,
                template.Scope,
                controls,
                lowered.Nodes[template.NodeId.Value].Children.Select(id => lowered.Nodes[id.Value]).Single(node => !node.TypeName.Contains('.')),
                triggerSourceType,
                $"Template{templateIndex}_");
            body.Emit(OpCodes.Ret);
        }
    }

    private static void EmitTemplates(CecilTypeSystem typeSystem, ModuleDefinition module, TypeDefinition generatedType, MethodDefinition wrapper, FormaLoweredDocument lowered)
    {
        if (lowered.Templates.Count == 0) return;
        var formaAssembly = typeSystem.Resolve(module.AssemblyReferences.Single(reference => reference.Name == "Forma"));
        var controlType = formaAssembly.MainModule.GetType("Forma.Control");
        var resourceDictionaryType = formaAssembly.MainModule.GetType("Forma.Xaml.ResourceDictionary");
        var dataTemplateType = formaAssembly.MainModule.GetType("Forma.DataTemplate");
        var controlTemplateType = formaAssembly.MainModule.GetType("Forma.ControlTemplate");
        var itemsPanelTemplateType = formaAssembly.MainModule.GetType("Forma.ItemsPanelTemplate");
        var dataFactoryDefinition = formaAssembly.MainModule.GetType("Forma.DataTemplateFactory`1");
        var controlFactoryDefinition = formaAssembly.MainModule.GetType("Forma.ControlTemplateFactory`1");
        var itemsFactoryType = formaAssembly.MainModule.GetType("Forma.ItemsPanelTemplateFactory");
        var resourcesGetter = module.ImportReference(FindProperty(controlType, "Resources")!.GetMethod);
        var addResource = module.ImportReference(resourceDictionaryType.Methods.Single(method => method.Name == "Add" && method.Parameters.Count == 2 && method.Parameters[0].ParameterType.FullName == "System.String"));
        var nameScopeType = formaAssembly.MainModule.GetType("Forma.Xaml.NameScope");
        var findOrdinal = module.ImportReference(nameScopeType.Methods.Single(method => method.Name == "FindControlByOrdinal" && method.IsPublic && method.Parameters.Count == 2));
        var findName = module.ImportReference(nameScopeType.Methods.Single(method => method.Name == "FindControlByName" && method.IsPublic && method.Parameters.Count == 2));
        var controls = OwnerNodes(lowered).Where(node => IsControl(ResolveObjectType(typeSystem, module, node, lowered), controlType)).ToArray();
        var body = wrapper.Body.GetILProcessor();

        for (var index = 0; index < lowered.Templates.Count; index++)
        {
            var template = lowered.Templates[index];
            var node = lowered.Nodes[template.NodeId.Value];
            var key = node.FindDirective("Key");
            PropertyDefinition? targetProperty = null;
            if (string.IsNullOrWhiteSpace(key))
            {
                var (owner, propertyName) = ResolveTemplatePropertyOwner(lowered, node);
                var ownerType = ResolveObjectType(typeSystem, module, owner, lowered)
                    ?? throw new InvalidOperationException($"Template property owner type '{owner.TypeName}' was not resolved.");
                targetProperty = FindProperty(ownerType, propertyName)
                    ?? throw new InvalidOperationException($"Template property '{ownerType.FullName}.{propertyName}' was not found.");
                if (targetProperty.SetMethod == null)
                    throw new InvalidOperationException($"Template property '{ownerType.FullName}.{propertyName}' is read-only.");
                EmitFindObject(typeSystem, module, body, lowered, owner, controls, controlType, findOrdinal, findName);
                body.Emit(OpCodes.Castclass, module.ImportReference(ownerType));
            }
            else
            {
                var owner = FindResourceOwner(lowered, node) ?? lowered.Nodes[lowered.RootNodeId.Value];
                EmitFindControl(body, owner, Array.IndexOf(controls, owner), findOrdinal, findName);
                body.Emit(OpCodes.Callvirt, resourcesGetter);
                body.Emit(OpCodes.Ldstr, key);
            }
            var factory = generatedType.Methods.Single(method => method.Name == $"__TemplateFactory{index}");

            if (template.Kind == FormaXamlTemplateKind.Data)
            {
                var dataType = ResolveSymbolType(typeSystem, module, lowered, template.DataTypeSymbolId)
                    ?? throw new InvalidOperationException("A DataTemplate requires a resolvable x:DataType.");
                var delegateType = new GenericInstanceType(module.ImportReference(dataFactoryDefinition));
                delegateType.GenericArguments.Add(module.ImportReference(dataType));
                EmitDelegate(body, delegateType, factory);
                var create = new GenericInstanceMethod(module.ImportReference(dataTemplateType.Methods.Single(method => method.Name == "Create")));
                create.GenericArguments.Add(module.ImportReference(dataType));
                body.Emit(OpCodes.Call, create);
            }
            else if (template.Kind == FormaXamlTemplateKind.Control)
            {
                var targetType = ResolveSymbolType(typeSystem, module, lowered, template.TargetTypeSymbolId)
                    ?? throw new InvalidOperationException("A ControlTemplate requires a resolvable TargetType.");
                var delegateType = new GenericInstanceType(module.ImportReference(controlFactoryDefinition));
                delegateType.GenericArguments.Add(module.ImportReference(targetType));
                EmitDelegate(body, delegateType, factory);
                body.Emit(OpCodes.Ldnull);
                var create = new GenericInstanceMethod(module.ImportReference(controlTemplateType.Methods.Single(method => method.Name == "Create")));
                create.GenericArguments.Add(module.ImportReference(targetType));
                body.Emit(OpCodes.Call, create);
            }
            else
            {
                EmitDelegate(body, module.ImportReference(itemsFactoryType), factory);
                body.Emit(OpCodes.Newobj, module.ImportReference(itemsPanelTemplateType.Methods.Single(method =>
                    method.IsConstructor && method.IsPublic && method.Parameters.Count == 1 &&
                    method.Parameters[0].ParameterType.FullName == itemsFactoryType.FullName)));
            }
            body.Emit(OpCodes.Callvirt, targetProperty == null ? addResource : module.ImportReference(targetProperty.SetMethod));
        }
    }

    private static (FormaLoweredNode Owner, string PropertyName) ResolveTemplatePropertyOwner(
        FormaLoweredDocument document,
        FormaLoweredNode template)
    {
        var propertyElement = template.ParentId is { } propertyId ? document.Nodes[propertyId.Value] : null;
        if (propertyElement == null || !propertyElement.TypeName.Contains('.', StringComparison.Ordinal) || propertyElement.ParentId == null)
            throw new InvalidOperationException($"Unkeyed {template.TypeName} must be assigned through a direct property element.");
        var separator = propertyElement.TypeName.LastIndexOf('.');
        return (document.Nodes[propertyElement.ParentId.Value.Value], propertyElement.TypeName.Substring(separator + 1));
    }

    private static void EmitFindObject(
        CecilTypeSystem typeSystem,
        ModuleDefinition module,
        ILProcessor body,
        FormaLoweredDocument lowered,
        FormaLoweredNode node,
        FormaLoweredNode[] controls,
        TypeDefinition controlType,
        MethodReference findOrdinal,
        MethodReference findName)
    {
        var nodeType = ResolveObjectType(typeSystem, module, node, lowered)
            ?? throw new InvalidOperationException($"XAML object type '{node.TypeName}' was not resolved.");
        if (IsControl(nodeType, controlType))
        {
            EmitFindControl(body, node, Array.IndexOf(controls, node), findOrdinal, findName);
            return;
        }
        var propertyElement = node.ParentId is { } parentId ? lowered.Nodes[parentId.Value] : null;
        if (propertyElement == null || propertyElement.ParentId == null ||
            !propertyElement.TypeName.Contains('.', StringComparison.Ordinal))
            throw new InvalidOperationException($"Nonvisual XAML object '{node.TypeName}' must be owned by a typed property collection.");
        var owner = lowered.Nodes[propertyElement.ParentId.Value.Value];
        var ownerType = ResolveObjectType(typeSystem, module, owner, lowered)
            ?? throw new InvalidOperationException($"Collection owner type '{owner.TypeName}' was not resolved.");
        var propertyName = propertyElement.TypeName.Substring(propertyElement.TypeName.LastIndexOf('.') + 1);
        var property = FindProperty(ownerType, propertyName)
            ?? throw new InvalidOperationException($"Collection property '{ownerType.FullName}.{propertyName}' was not found.");
        var index = propertyElement.Children.Select((child, childIndex) => (child, childIndex))
            .Where(entry => entry.child == node.Id)
            .Select(entry => entry.childIndex)
            .DefaultIfEmpty(-1)
            .Single();
        if (index < 0) throw new InvalidOperationException($"Nonvisual XAML object '{node.TypeName}' was not found in its owner collection.");
        EmitFindObject(typeSystem, module, body, lowered, owner, controls, controlType, findOrdinal, findName);
        body.Emit(OpCodes.Castclass, module.ImportReference(ownerType));
        body.Emit(OpCodes.Callvirt, module.ImportReference(property.GetMethod));
        body.Emit(OpCodes.Castclass, module.ImportReference(typeof(System.Collections.IList)));
        body.Emit(OpCodes.Ldc_I4, index);
        body.Emit(OpCodes.Callvirt, module.ImportReference(typeof(System.Collections.IList).GetProperty("Item")!.GetMethod!));
    }

    private static void EmitStoryboardsAndTriggers(CecilTypeSystem typeSystem, ModuleDefinition module, TypeDefinition generatedType, MethodDefinition wrapper, FormaLoweredDocument lowered)
    {
        var formaAssembly = typeSystem.Resolve(module.AssemblyReferences.Single(reference => reference.Name == "Forma"));
        var controlType = formaAssembly.MainModule.GetType("Forma.Control");
        EmitStoryboardsAndTriggers(
            typeSystem,
            module,
            generatedType,
            wrapper,
            lowered,
            lowered.OwnerScope,
            OwnerNodes(lowered).Where(node => IsControl(ResolveObjectType(typeSystem, module, node, lowered), controlType)).ToArray(),
            lowered.Nodes[lowered.RootNodeId.Value],
            ResolveDataType(typeSystem, module, lowered),
            string.Empty);
    }

    private static void EmitStoryboardsAndTriggers(
        CecilTypeSystem typeSystem,
        ModuleDefinition module,
        TypeDefinition generatedType,
        MethodDefinition wrapper,
        FormaLoweredDocument lowered,
        FormaLoweredScope scope,
        FormaLoweredNode[] controls,
        FormaLoweredNode fallbackOwner,
        TypeDefinition? dataType,
        string helperPrefix)
    {
        var storyboardNodes = scope.Operations.Where(operation => operation.Kind == FormaLoweredOperationKind.Storyboard).Select(operation => lowered.Nodes[operation.NodeId.Value]).ToArray();
        var triggerNodes = scope.Operations.Where(operation => operation.Kind == FormaLoweredOperationKind.Trigger).Select(operation => lowered.Nodes[operation.NodeId.Value]).Where(node => node.TypeName == "EventTrigger").ToArray();
        var propertyTriggerNodes = scope.Operations.Where(operation => operation.Kind == FormaLoweredOperationKind.Trigger).Select(operation => lowered.Nodes[operation.NodeId.Value]).Where(node => node.TypeName == "PropertyTrigger").ToArray();
        if (storyboardNodes.Length == 0 && triggerNodes.Length == 0 && propertyTriggerNodes.Length == 0) return;
        var formaAssembly = typeSystem.Resolve(module.AssemblyReferences.Single(reference => reference.Name == "Forma"));
        var controlType = formaAssembly.MainModule.GetType("Forma.Control");
        var resourceDictionaryType = formaAssembly.MainModule.GetType("Forma.Xaml.ResourceDictionary");
        var storyboardType = formaAssembly.MainModule.GetType("Forma.Xaml.Storyboard");
        var repeatBehaviorType = formaAssembly.MainModule.GetType("Forma.Xaml.RepeatBehavior");
        var xamlPropertyDefinition = formaAssembly.MainModule.GetType("Forma.Xaml.XamlProperty`1");
        var keyFrameDefinition = formaAssembly.MainModule.GetType("Forma.Xaml.KeyFrame`1");
        var timelineDefinition = formaAssembly.MainModule.GetType("Forma.Xaml.Timeline`1");
        var compiledTriggerType = formaAssembly.MainModule.GetType("Forma.Xaml.CompiledStoryboardTrigger");
        var nameScopeType = formaAssembly.MainModule.GetType("Forma.Xaml.NameScope");
        var findOrdinal = module.ImportReference(nameScopeType.Methods.Single(method => method.Name == "FindControlByOrdinal" && method.IsPublic && method.Parameters.Count == 2));
        var findName = module.ImportReference(nameScopeType.Methods.Single(method => method.Name == "FindControlByName" && method.IsPublic && method.Parameters.Count == 2));
        var storyboardConstructor = module.ImportReference(storyboardType.Methods.Single(method => method.IsConstructor));
        var addTimeline = module.ImportReference(storyboardType.Methods.Single(method => method.Name == "AddTimeline"));
        var resourcesGetter = module.ImportReference(FindProperty(controlType, "Resources")!.GetMethod);
        var addResource = module.ImportReference(resourceDictionaryType.Methods.Single(method => method.Name == "Add" && method.Parameters.Count == 2 && method.Parameters[0].ParameterType.FullName == "System.String"));
        var attachTriggerDefinition = module.ImportReference(compiledTriggerType.Methods.Single(method => method.Name == "AttachEvent"));
        var attachStopTriggerDefinition = module.ImportReference(compiledTriggerType.Methods.Single(method => method.Name == "AttachStopEvent"));
        var attachPropertyTriggerDefinition = module.ImportReference(compiledTriggerType.Methods.Single(method => method.Name == "AttachProperty"));
        var body = wrapper.Body.GetILProcessor();
        var storyboards = new Dictionary<string, VariableDefinition>(StringComparer.Ordinal);
        wrapper.Body.InitLocals = true;

        for (var storyboardIndex = 0; storyboardIndex < storyboardNodes.Length; storyboardIndex++)
        {
            var storyboardNode = storyboardNodes[storyboardIndex];
            var key = storyboardNode.FindDirective("Key") ?? throw new InvalidOperationException("Storyboard requires x:Key.");
            var owner = FindResourceOwner(lowered, storyboardNode) ?? fallbackOwner;
            var storyboardVariable = new VariableDefinition(module.ImportReference(storyboardType));
            wrapper.Body.Variables.Add(storyboardVariable);
            body.Emit(OpCodes.Newobj, storyboardConstructor);
            body.Emit(OpCodes.Stloc, storyboardVariable);
            if (bool.TryParse(storyboardNode.FindMember("AutoReverse"), out var autoReverse))
            {
                body.Emit(OpCodes.Ldloc, storyboardVariable);
                body.Emit(autoReverse ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                body.Emit(OpCodes.Callvirt, module.ImportReference(FindProperty(storyboardType, "AutoReverse")!.SetMethod));
            }
            if (Enum.TryParse<FillBehavior>(storyboardNode.FindMember("FillBehavior"), true, out var fillBehavior))
            {
                body.Emit(OpCodes.Ldloc, storyboardVariable);
                body.Emit(OpCodes.Ldc_I4, (int)fillBehavior);
                body.Emit(OpCodes.Callvirt, module.ImportReference(FindProperty(storyboardType, "FillBehavior")!.SetMethod));
            }
            if (storyboardNode.FindMember("RepeatBehavior") is string repeatBehavior)
            {
                body.Emit(OpCodes.Ldloc, storyboardVariable);
                if (string.Equals(repeatBehavior, "Forever", StringComparison.OrdinalIgnoreCase))
                    body.Emit(OpCodes.Call, module.ImportReference(FindProperty(repeatBehaviorType, "ForeverValue")!.GetMethod));
                else if (double.TryParse(repeatBehavior, NumberStyles.Float, CultureInfo.InvariantCulture, out var repeatCount) && repeatCount > 0)
                {
                    body.Emit(OpCodes.Ldc_R8, repeatCount);
                    body.Emit(OpCodes.Call, module.ImportReference(repeatBehaviorType.Methods.Single(method => method.Name == "ForCount")));
                }
                else
                    throw new InvalidOperationException($"Storyboard RepeatBehavior '{repeatBehavior}' must be a positive count or Forever.");
                body.Emit(OpCodes.Callvirt, module.ImportReference(FindProperty(storyboardType, "RepeatBehavior")!.SetMethod));
            }

            var timelines = storyboardNode.Children.Select(child => lowered.Nodes[child.Value]).Where(child => child.TypeName.EndsWith("Timeline", StringComparison.Ordinal)).ToArray();
            for (var timelineIndex = 0; timelineIndex < timelines.Length; timelineIndex++)
            {
                var timelineNode = timelines[timelineIndex];
                var targetName = timelineNode.FindMember("TargetName") ?? throw new InvalidOperationException("Timeline requires TargetName.");
                var propertyName = timelineNode.FindMember("Property") ?? throw new InvalidOperationException("Timeline requires Property.");
                var targetNode = ScopeNodes(lowered, scope).SingleOrDefault(node => node.FindDirective("Name") == targetName)
                    ?? throw new InvalidOperationException($"Storyboard target '{targetName}' was not found.");
                var targetType = ResolveObjectType(typeSystem, module, targetNode, lowered)
                    ?? throw new InvalidOperationException($"Storyboard target type '{targetNode.TypeName}' was not resolved.");
                var property = FindProperty(targetType, propertyName)
                    ?? throw new InvalidOperationException($"Storyboard property '{targetType.FullName}.{propertyName}' was not found.");
                if (property.GetMethod == null || property.SetMethod == null)
                    throw new InvalidOperationException($"Storyboard property '{targetType.FullName}.{propertyName}' must be readable and writable.");
                var valueType = module.ImportReference(property.PropertyType);
                ValidateTimelineValueType(timelineNode.TypeName, valueType);
                var getTarget = DefineTargetGetter(module, generatedType, $"__{helperPrefix}TimelineGetTarget{storyboardIndex}_{timelineIndex}", targetType, property, valueType);
                var setTarget = DefineTargetSetter(module, generatedType, $"__{helperPrefix}TimelineSetTarget{storyboardIndex}_{timelineIndex}", targetType, property, valueType);
                var funcTarget = MakeDelegateType(module, typeof(Func<,>), module.TypeSystem.Object, valueType);
                var actionTarget = MakeDelegateType(module, typeof(Action<,>), module.TypeSystem.Object, valueType);
                var xamlPropertyType = new GenericInstanceType(module.ImportReference(xamlPropertyDefinition));
                xamlPropertyType.GenericArguments.Add(valueType);
                var xamlPropertyConstructor = MakeClosedMethod(module, xamlPropertyDefinition.Methods.Single(method => method.IsConstructor), xamlPropertyType);
                var timelineType = formaAssembly.MainModule.GetType($"Forma.Xaml.{timelineNode.TypeName}");
                var timelineVariable = new VariableDefinition(module.ImportReference(timelineType));
                wrapper.Body.Variables.Add(timelineVariable);
                body.Emit(OpCodes.Ldstr, targetName);
                body.Emit(OpCodes.Ldstr, propertyName);
                EmitDelegate(body, funcTarget, getTarget);
                EmitDelegate(body, actionTarget, setTarget);
                body.Emit(OpCodes.Newobj, xamlPropertyConstructor);
                body.Emit(OpCodes.Newobj, module.ImportReference(timelineType.Methods.Single(method => method.IsConstructor)));
                body.Emit(OpCodes.Stloc, timelineVariable);

                var keyFrameType = new GenericInstanceType(module.ImportReference(keyFrameDefinition));
                keyFrameType.GenericArguments.Add(valueType);
                var keyFrameConstructor = MakeClosedMethod(module, keyFrameDefinition.Methods.Single(method => method.IsConstructor), keyFrameType);
                var closedTimelineType = new GenericInstanceType(module.ImportReference(timelineDefinition));
                closedTimelineType.GenericArguments.Add(valueType);
                var addKeyFrame = MakeClosedMethod(module, timelineDefinition.Methods.Single(method => method.Name == "AddKeyFrame"), closedTimelineType);
                foreach (var keyFrame in timelineNode.Children.Select(child => lowered.Nodes[child.Value]).Where(child => child.TypeName == "KeyFrame"))
                {
                    body.Emit(OpCodes.Ldloc, timelineVariable);
                    EmitTimeSpan(body, module, keyFrame.FindMember("Time") ?? throw new InvalidOperationException("KeyFrame requires Time."));
                    EmitStyleValue(body, module, formaAssembly, valueType, keyFrame.FindMember("Value") ?? throw new InvalidOperationException("KeyFrame requires Value."));
                    var easing = Enum.TryParse<Easing>(keyFrame.FindMember("Easing"), true, out var parsedEasing) ? parsedEasing : Easing.Linear;
                    body.Emit(OpCodes.Ldc_I4, (int)easing);
                    body.Emit(OpCodes.Newobj, keyFrameConstructor);
                    body.Emit(OpCodes.Callvirt, addKeyFrame);
                }
                body.Emit(OpCodes.Ldloc, storyboardVariable);
                body.Emit(OpCodes.Ldloc, timelineVariable);
                body.Emit(OpCodes.Callvirt, addTimeline);
            }

            EmitFindControl(body, owner, Array.IndexOf(controls, owner), findOrdinal, findName);
            body.Emit(OpCodes.Callvirt, resourcesGetter);
            body.Emit(OpCodes.Ldstr, key);
            body.Emit(OpCodes.Ldloc, storyboardVariable);
            body.Emit(OpCodes.Callvirt, addResource);
            storyboards.Add(key, storyboardVariable);
        }

        for (var triggerIndex = 0; triggerIndex < triggerNodes.Length; triggerIndex++)
        {
            var trigger = triggerNodes[triggerIndex];
            var sourceName = trigger.FindMember("SourceName") ?? throw new InvalidOperationException("EventTrigger requires SourceName.");
            var eventName = trigger.FindMember("Event") ?? throw new InvalidOperationException("EventTrigger requires Event.");
            var action = trigger.Children.Select(child => lowered.Nodes[child.Value]).SingleOrDefault(child => child.TypeName is "BeginStoryboard" or "StopStoryboard")
                ?? throw new InvalidOperationException("EventTrigger requires BeginStoryboard or StopStoryboard.");
            var storyboardReference = action.Members.SingleOrDefault(member => !member.IsDirective && member.Name == "Storyboard")?.Value as FormaResourceValue
                ?? throw new InvalidOperationException("Storyboard action requires a static resource reference.");
            if (storyboardReference.IsDynamic || !storyboards.TryGetValue(storyboardReference.Key, out var storyboardVariable))
                throw new InvalidOperationException($"EventTrigger storyboard '{storyboardReference.Key}' was not found.");
            var sourceNode = ScopeNodes(lowered, scope).SingleOrDefault(node => node.FindDirective("Name") == sourceName)
                ?? throw new InvalidOperationException($"EventTrigger source '{sourceName}' was not found.");
            var sourceType = ResolveObjectType(typeSystem, module, sourceNode, lowered)
                ?? throw new InvalidOperationException($"EventTrigger source type '{sourceNode.TypeName}' was not resolved.");
            var eventDefinition = FindEvent(sourceType, eventName)
                ?? throw new InvalidOperationException($"Event '{sourceType.FullName}.{eventName}' was not found.");
            var handlerType = module.ImportReference(eventDefinition.EventType);
            var helperId = $"{helperPrefix}{triggerIndex}";
            var add = DefineTriggerEventAccessor(module, generatedType, helperId, sourceType, eventDefinition, handlerType, true);
            var remove = DefineTriggerEventAccessor(module, generatedType, helperId, sourceType, eventDefinition, handlerType, false);
            var factory = DefineTriggerHandlerFactory(module, generatedType, helperId, eventDefinition, handlerType);
            var actionType = MakeDelegateType(module, typeof(Action<,>), module.ImportReference(sourceType), handlerType);
            var factoryType = MakeDelegateType(module, typeof(Func<,>), module.ImportReference(typeof(Action)), handlerType);
            var attach = new GenericInstanceMethod(action.TypeName == "StopStoryboard" ? attachStopTriggerDefinition : attachTriggerDefinition);
            attach.GenericArguments.Add(module.ImportReference(sourceType));
            attach.GenericArguments.Add(handlerType);
            body.Emit(OpCodes.Ldarg_1);
            EmitFindControl(body, sourceNode, Array.IndexOf(controls, sourceNode), findOrdinal, findName);
            body.Emit(OpCodes.Castclass, module.ImportReference(sourceType));
            EmitDelegate(body, actionType, add);
            EmitDelegate(body, actionType, remove);
            EmitDelegate(body, factoryType, factory);
            body.Emit(OpCodes.Ldloc, storyboardVariable);
            body.Emit(OpCodes.Call, attach);
            body.Emit(OpCodes.Pop);
        }

        if (propertyTriggerNodes.Length > 0 && dataType == null)
            throw new InvalidOperationException("PropertyTrigger requires a resolvable x:DataType.");
        for (var triggerIndex = 0; triggerIndex < propertyTriggerNodes.Length; triggerIndex++)
        {
            var sourceType = dataType!;
            var trigger = propertyTriggerNodes[triggerIndex];
            var binding = trigger.Members.SingleOrDefault(member => !member.IsDirective && member.Name == "Binding")?.Value as FormaBindingValue
                ?? throw new InvalidOperationException("PropertyTrigger requires Binding.");
            var path = binding.Path;
            var sourceProperty = FindProperty(sourceType, path)
                ?? throw new InvalidOperationException($"PropertyTrigger source property '{sourceType.FullName}.{path}' was not found.");
            if (sourceProperty.GetMethod == null)
                throw new InvalidOperationException($"PropertyTrigger source property '{sourceType.FullName}.{path}' must be readable.");
            var action = trigger.Children.Select(child => lowered.Nodes[child.Value]).SingleOrDefault(child => child.TypeName == "BeginStoryboard")
                ?? throw new InvalidOperationException("PropertyTrigger requires BeginStoryboard.");
            var storyboardReference = action.Members.SingleOrDefault(member => !member.IsDirective && member.Name == "Storyboard")?.Value as FormaResourceValue
                ?? throw new InvalidOperationException("Storyboard action requires a static resource reference.");
            if (storyboardReference.IsDynamic || !storyboards.TryGetValue(storyboardReference.Key, out var storyboardVariable))
                throw new InvalidOperationException($"PropertyTrigger storyboard '{storyboardReference.Key}' was not found.");
            var valueType = module.ImportReference(sourceProperty.PropertyType);
            var read = DefineSourceGetter(module, generatedType, $"__{helperPrefix}TriggerRead{triggerIndex}", sourceType, sourceProperty, valueType);
            var funcSource = MakeDelegateType(module, typeof(Func<,>), module.ImportReference(sourceType), valueType);
            var attach = new GenericInstanceMethod(attachPropertyTriggerDefinition);
            attach.GenericArguments.Add(module.ImportReference(sourceType));
            attach.GenericArguments.Add(valueType);
            body.Emit(OpCodes.Ldarg_1);
            EmitDelegate(body, funcSource, read);
            body.Emit(OpCodes.Ldstr, path);
            EmitStyleValue(body, module, formaAssembly, valueType, trigger.FindMember("Value") ?? throw new InvalidOperationException("PropertyTrigger requires Value."));
            body.Emit(OpCodes.Ldloc, storyboardVariable);
            body.Emit(OpCodes.Call, attach);
            body.Emit(OpCodes.Pop);
        }
    }

    private static void EmitEvents(CecilTypeSystem typeSystem, ModuleDefinition module, TypeDefinition generatedType, MethodDefinition wrapper, TypeDefinition rootType, FormaLoweredDocument lowered)
    {
        var formaAssembly = typeSystem.Resolve(module.AssemblyReferences.Single(reference => reference.Name == "Forma"));
        var controlType = formaAssembly.MainModule.GetType("Forma.Control");
        var compiledEventType = formaAssembly.MainModule.GetType("Forma.Xaml.CompiledEvent");
        var nameScopeType = formaAssembly.MainModule.GetType("Forma.Xaml.NameScope");
        var findOrdinal = module.ImportReference(nameScopeType.Methods.Single(method => method.Name == "FindControlByOrdinal" && method.IsPublic && method.Parameters.Count == 2));
        var findName = module.ImportReference(nameScopeType.Methods.Single(method => method.Name == "FindControlByName" && method.IsPublic && method.Parameters.Count == 2));
        var attachDefinition = module.ImportReference(compiledEventType.Methods.Single(method => method.Name == "Attach"));
        var controls = OwnerNodes(lowered).Where(node => IsControl(ResolveObjectType(typeSystem, module, node, lowered), controlType)).ToArray();
        var body = wrapper.Body.GetILProcessor();
        var eventIndex = 0;

        foreach (var node in OwnerNodes(lowered))
        {
            var targetType = ResolveObjectType(typeSystem, module, node, lowered);
            if (targetType == null || !IsControl(targetType, controlType)) continue;
            foreach (var member in node.Members.Where(member => !member.IsDirective))
            {
                var eventDefinition = FindEvent(targetType, member.Name);
                if (eventDefinition == null) continue;
                var handler = FindMethod(rootType, member.Value.RawText)
                    ?? throw new InvalidOperationException($"Event handler '{rootType.FullName}.{member.Value.RawText}' was not found.");
                ValidateEventHandler(eventDefinition, handler);
                var handlerBridge = DefineEventHandlerBridge(module, rootType, eventIndex, handler);
                var handlerType = module.ImportReference(eventDefinition.EventType);
                var add = DefineEventAccessor(module, generatedType, eventIndex, targetType, eventDefinition, handlerType, true);
                var remove = DefineEventAccessor(module, generatedType, eventIndex, targetType, eventDefinition, handlerType, false);
                var actionType = MakeDelegateType(module, typeof(Action<,>), module.ImportReference(targetType), handlerType);
                var handlerConstructor = new MethodReference(".ctor", module.TypeSystem.Void, handlerType) { HasThis = true };
                handlerConstructor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
                handlerConstructor.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));
                var attach = new GenericInstanceMethod(attachDefinition);
                attach.GenericArguments.Add(module.ImportReference(targetType));
                attach.GenericArguments.Add(handlerType);
                body.Emit(OpCodes.Ldarg_1);
                EmitFindControl(body, node, Array.IndexOf(controls, node), findOrdinal, findName);
                body.Emit(OpCodes.Castclass, module.ImportReference(targetType));
                body.Emit(OpCodes.Ldarg_1);
                body.Emit(OpCodes.Ldftn, handlerBridge);
                body.Emit(OpCodes.Newobj, handlerConstructor);
                EmitDelegate(body, actionType, add);
                EmitDelegate(body, actionType, remove);
                body.Emit(OpCodes.Call, attach);
                body.Emit(OpCodes.Pop);
                eventIndex++;
            }
        }
    }

    private static void EmitResourceReferences(CecilTypeSystem typeSystem, ModuleDefinition module, TypeDefinition generatedType, MethodDefinition wrapper, FormaLoweredDocument lowered)
    {
        var references = lowered.OwnerScope.Operations
            .Where(operation => operation.Kind == FormaLoweredOperationKind.ResourceReference)
            .Select(operation => (Node: NodeForId(lowered, operation.NodeId), Member: MemberForOperation(lowered, operation)))
            .Where(reference => !HasAncestor(lowered, reference.Node, "Style") && !HasAncestor(lowered, reference.Node, "EventTrigger") && !HasAncestor(lowered, reference.Node, "PropertyTrigger"))
            .ToArray();
        if (references.Length == 0) return;
        var formaAssembly = typeSystem.Resolve(module.AssemblyReferences.Single(reference => reference.Name == "Forma"));
        var controlType = formaAssembly.MainModule.GetType("Forma.Control");
        var nameScopeType = formaAssembly.MainModule.GetType("Forma.Xaml.NameScope");
        var staticResourceType = formaAssembly.MainModule.GetType("Forma.Xaml.StaticResource");
        var dynamicResourceType = formaAssembly.MainModule.GetType("Forma.Xaml.DynamicResource");
        var xamlPropertyDefinition = formaAssembly.MainModule.GetType("Forma.Xaml.XamlProperty`1");
        var findOrdinal = module.ImportReference(nameScopeType.Methods.Single(method => method.Name == "FindControlByOrdinal" && method.IsPublic && method.Parameters.Count == 2));
        var findName = module.ImportReference(nameScopeType.Methods.Single(method => method.Name == "FindControlByName" && method.IsPublic && method.Parameters.Count == 2));
        var resolveDefinition = module.ImportReference(staticResourceType.Methods.Single(method => method.Name == "Resolve"));
        var attachDefinition = module.ImportReference(dynamicResourceType.Methods.Single(method => method.Name == "Attach"));
        var controls = OwnerNodes(lowered).Where(node => IsControl(ResolveObjectType(typeSystem, module, node, lowered), controlType)).ToArray();
        var body = wrapper.Body.GetILProcessor();
        wrapper.Body.InitLocals = true;

        for (var index = 0; index < references.Length; index++)
        {
            var reference = references[index];
            var resource = (FormaResourceValue)reference.Member.Value;
            var dynamic = resource.IsDynamic;
            var key = resource.Key;
            var targetOrdinal = Array.IndexOf(controls, reference.Node);
            if (targetOrdinal < 0) throw new InvalidOperationException($"Resource target '{reference.Node.TypeName}' is not a Control.");
            var targetType = ResolveObjectType(typeSystem, module, reference.Node, lowered)
                ?? throw new InvalidOperationException($"Resource target type '{reference.Node.TypeName}' was not resolved.");
            var targetProperty = FindProperty(targetType, reference.Member.Name)
                ?? throw new InvalidOperationException($"Resource target property '{targetType.FullName}.{reference.Member.Name}' was not found.");
            if (targetProperty.GetMethod == null || targetProperty.SetMethod == null)
                throw new InvalidOperationException($"Resource target property '{targetType.FullName}.{targetProperty.Name}' must be readable and writable.");
            var valueType = module.ImportReference(targetProperty.PropertyType);
            var targetVariable = new VariableDefinition(module.ImportReference(controlType));
            wrapper.Body.Variables.Add(targetVariable);
            EmitFindControl(body, reference.Node, targetOrdinal, findOrdinal, findName);
            body.Emit(OpCodes.Stloc, targetVariable);

            if (!dynamic)
            {
                var resolve = new GenericInstanceMethod(resolveDefinition);
                resolve.GenericArguments.Add(valueType);
                body.Emit(OpCodes.Ldloc, targetVariable);
                body.Emit(OpCodes.Castclass, module.ImportReference(targetType));
                body.Emit(OpCodes.Ldloc, targetVariable);
                body.Emit(OpCodes.Ldstr, key);
                body.Emit(OpCodes.Call, resolve);
                body.Emit(OpCodes.Callvirt, module.ImportReference(targetProperty.SetMethod));
                continue;
            }

            var getTarget = DefineTargetGetter(module, generatedType, $"__ResourceGetTarget{index}", targetType, targetProperty, valueType);
            var setTarget = DefineTargetSetter(module, generatedType, $"__ResourceSetTarget{index}", targetType, targetProperty, valueType);
            var funcTarget = MakeDelegateType(module, typeof(Func<,>), module.TypeSystem.Object, valueType);
            var actionTarget = MakeDelegateType(module, typeof(Action<,>), module.TypeSystem.Object, valueType);
            var xamlPropertyType = new GenericInstanceType(module.ImportReference(xamlPropertyDefinition));
            xamlPropertyType.GenericArguments.Add(valueType);
            var propertyConstructorDefinition = xamlPropertyDefinition.Methods.Single(method => method.IsConstructor && !method.IsStatic);
            var propertyConstructor = new MethodReference(".ctor", module.TypeSystem.Void, xamlPropertyType) { HasThis = true };
            foreach (var parameter in propertyConstructorDefinition.Parameters)
                propertyConstructor.Parameters.Add(new ParameterDefinition(module.ImportReference(parameter.ParameterType, propertyConstructor)));
            var attach = new GenericInstanceMethod(attachDefinition);
            attach.GenericArguments.Add(valueType);
            body.Emit(OpCodes.Ldarg_1);
            body.Emit(OpCodes.Ldloc, targetVariable);
            body.Emit(OpCodes.Ldstr, targetProperty.Name);
            EmitDelegate(body, funcTarget, getTarget);
            EmitDelegate(body, actionTarget, setTarget);
            body.Emit(OpCodes.Newobj, propertyConstructor);
            body.Emit(OpCodes.Ldstr, key);
            body.Emit(OpCodes.Ldnull);
            body.Emit(OpCodes.Ldc_I4, (int)XamlValueLayer.Local);
            body.Emit(OpCodes.Ldc_I8, 0L);
            body.Emit(OpCodes.Call, attach);
            body.Emit(OpCodes.Pop);
        }
    }

    private static void EmitBindings(CecilTypeSystem typeSystem, ModuleDefinition module, TypeDefinition generatedType, MethodDefinition wrapper, TypeDefinition rootType, FormaLoweredDocument lowered)
    {
        var bindingNodes = lowered.OwnerScope.Operations
            .Where(operation => operation.Kind == FormaLoweredOperationKind.Binding)
            .Select(operation => (Node: NodeForId(lowered, operation.NodeId), Member: MemberForOperation(lowered, operation)))
            .Where(binding => binding.Node.TypeName != "PropertyTrigger" && !HasAncestor(lowered, binding.Node, "PropertyTrigger"))
            .ToArray();
        if (bindingNodes.Length == 0) return;
        var dataContextType = ResolveDataType(typeSystem, module, lowered);
        var formaAssembly = typeSystem.Resolve(module.AssemblyReferences.Single(reference => reference.Name == "Forma"));
        var controlType = formaAssembly.MainModule.GetType("Forma.Control");
        var nameScopeType = formaAssembly.MainModule.GetType("Forma.Xaml.NameScope");
        var compiledBindingType = formaAssembly.MainModule.GetType("Forma.Xaml.CompiledBinding");
        var compiledBindingSourceType = formaAssembly.MainModule.GetType("Forma.Xaml.CompiledBindingSource");
        var bindingAdaptersType = formaAssembly.MainModule.GetType("Forma.Xaml.BindingTargetAdapters");
        var findOrdinal = module.ImportReference(nameScopeType.Methods.Single(method => method.Name == "FindControlByOrdinal" && method.IsPublic && method.Parameters.Count == 2));
        var findName = module.ImportReference(nameScopeType.Methods.Single(method => method.Name == "FindControlByName" && method.IsPublic && method.Parameters.Count == 2));
        var attachDefinition = module.ImportReference(compiledBindingType.Methods.Single(method => method.Name == "AttachOneWay" && method.Parameters.Count == 6));
        var attachTwoWayDefinition = module.ImportReference(compiledBindingType.Methods.Single(method => method.Name == "AttachTwoWay" && method.Parameters.Count == 7));
        var attachRelativeDefinition = module.ImportReference(compiledBindingType.Methods.Single(method => method.Name == "AttachOneWay" && method.Parameters.Count == 7));
        var attachRelativeTwoWayDefinition = module.ImportReference(compiledBindingType.Methods.Single(method => method.Name == "AttachTwoWay" && method.Parameters.Count == 8));
        var attachOneTimeDefinition = module.ImportReference(compiledBindingType.Methods.Single(method => method.Name == "AttachOneTime" && method.IsPublic && method.Parameters.Count == 6));
        var attachRelativeOneTimeDefinition = module.ImportReference(compiledBindingType.Methods.Single(method => method.Name == "AttachOneTime" && method.IsPublic && method.Parameters.Count == 7));
        var controls = OwnerNodes(lowered).Where(node => IsControl(ResolveObjectType(typeSystem, module, node, lowered), controlType)).ToArray();
        var body = wrapper.Body.GetILProcessor();

        for (var index = 0; index < bindingNodes.Length; index++)
        {
            var binding = bindingNodes[index];
            var targetOrdinal = Array.IndexOf(controls, binding.Node);
            if (targetOrdinal < 0) throw new InvalidOperationException($"Binding target '{binding.Node.TypeName}' is not a Control.");
            var targetType = ResolveObjectType(typeSystem, module, binding.Node, lowered)
                ?? throw new InvalidOperationException($"Binding target type '{binding.Node.TypeName}' was not resolved.");
            var targetProperty = FindProperty(targetType, binding.Member.Name)
                ?? throw new InvalidOperationException($"Binding target property '{targetType.FullName}.{binding.Member.Name}' was not found.");
            var bindingValue = (FormaBindingValue)binding.Member.Value;
            var path = bindingValue.Path;
            var sourceType = ResolveRequiredBindingSourceType(typeSystem, module, lowered, binding.Member, bindingValue, dataContextType);
            var sourceProperty = FindProperty(sourceType, path)
                ?? throw new InvalidOperationException($"Binding source property '{sourceType.FullName}.{path}' was not found.");
            var mode = Enum.TryParse<BindingMode>(bindingValue.Options.GetValueOrDefault("Mode"), out var parsedMode) ? parsedMode : BindingMode.OneWay;
            var twoWay = mode == BindingMode.TwoWay;
            if (sourceProperty.PropertyType.FullName != targetProperty.PropertyType.FullName &&
                (twoWay || !IsAssignableTo(sourceProperty.PropertyType, targetProperty.PropertyType)))
                throw new InvalidOperationException($"Binding '{path}' type '{sourceProperty.PropertyType.FullName}' is incompatible with '{binding.Member.Name}' type '{targetProperty.PropertyType.FullName}'.");
            if (sourceProperty.GetMethod == null || targetProperty.GetMethod == null || targetProperty.SetMethod == null)
                throw new InvalidOperationException($"Binding '{path}' requires readable source and readable/writable target properties.");

            var valueType = module.ImportReference(targetProperty.PropertyType);
            var read = DefineSourceGetter(module, generatedType, index, sourceType, sourceProperty, valueType);
            var getTarget = DefineTargetGetter(module, generatedType, index, targetType, targetProperty, valueType);
            var setTarget = DefineTargetSetter(module, generatedType, index, targetType, targetProperty, valueType);
            var funcSource = MakeDelegateType(module, typeof(Func<,>), module.ImportReference(sourceType), valueType);
            var funcTarget = MakeDelegateType(module, typeof(Func<,>), module.TypeSystem.Object, valueType);
            var actionTarget = MakeDelegateType(module, typeof(Action<,>), module.TypeSystem.Object, valueType);
            var relative = bindingValue.Source.Kind != FormaBindingSourceKind.DataContext;
            var attach = new GenericInstanceMethod(mode switch
            {
                BindingMode.OneTime => relative ? attachRelativeOneTimeDefinition : attachOneTimeDefinition,
                BindingMode.TwoWay => relative ? attachRelativeTwoWayDefinition : attachTwoWayDefinition,
                _ => relative ? attachRelativeDefinition : attachDefinition,
            });
            attach.GenericArguments.Add(module.ImportReference(sourceType));
            attach.GenericArguments.Add(valueType);

            body.Emit(OpCodes.Ldarg_1);
            EmitFindControl(body, binding.Node, targetOrdinal, findOrdinal, findName);
            if (relative)
            {
                var resolver = DefineBindingSourceResolver(module, generatedType, $"__BindingResolve{index}", sourceType,
                    bindingValue.Source, compiledBindingSourceType, controlType);
                var funcResolver = MakeDelegateType(module, typeof(Func<,>), module.ImportReference(controlType), module.ImportReference(sourceType));
                EmitDelegate(body, funcResolver, resolver);
            }
            EmitDelegate(body, funcSource, read);
            if (twoWay)
            {
                if (sourceProperty.SetMethod == null) throw new InvalidOperationException($"Two-way binding source '{sourceType.FullName}.{path}' is read-only.");
                var adapterName = ResolveBindingAdapter(targetType, targetProperty)
                    ?? throw new InvalidOperationException($"Two-way binding target '{targetType.FullName}.{targetProperty.Name}' is unsupported.");
                var write = DefineSourceSetter(module, generatedType, index, sourceType, sourceProperty, valueType);
                var actionSource = MakeDelegateType(module, typeof(Action<,>), module.ImportReference(sourceType), valueType);
                EmitDelegate(body, actionSource, write);
                body.Emit(OpCodes.Ldstr, path);
                body.Emit(OpCodes.Ldsfld, module.ImportReference(bindingAdaptersType.Fields.Single(field => field.Name == adapterName)));
                body.Emit(OpCodes.Ldc_I4, (int)ParseUpdateSourceTrigger(bindingValue));
            }
            else
            {
                body.Emit(OpCodes.Ldstr, path);
                EmitDelegate(body, funcTarget, getTarget);
                EmitDelegate(body, actionTarget, setTarget);
            }
            body.Emit(OpCodes.Call, attach);
            body.Emit(OpCodes.Pop);
        }
    }

    private static TypeDefinition? ResolveDataType(CecilTypeSystem typeSystem, ModuleDefinition module, FormaLoweredDocument document)
    {
        var dataType = document.DataType;
        if (string.IsNullOrWhiteSpace(dataType)) return null;
        var parts = dataType.Split(':', 2);
        if (parts.Length == 1) return FindType(module, parts[0]);
        if (!document.Namespaces.TryGetValue(parts[0], out var xmlNamespace) || !xmlNamespace.StartsWith("clr-namespace:", StringComparison.Ordinal)) return null;
        var definition = xmlNamespace.Substring("clr-namespace:".Length).Split(';');
        var clrNamespace = definition[0];
        var assemblyName = definition.Skip(1).FirstOrDefault(value => value.StartsWith("assembly=", StringComparison.Ordinal))?.Substring("assembly=".Length) ?? module.Assembly.Name.Name;
        var assembly = assemblyName == module.Assembly.Name.Name ? module.Assembly : typeSystem.Resolve(module.AssemblyReferences.Single(reference => reference.Name == assemblyName));
        return assembly.MainModule.GetType($"{clrNamespace}.{parts[1]}");
    }

    private static TypeDefinition? ResolveObjectType(CecilTypeSystem typeSystem, ModuleDefinition module, FormaLoweredNode node, FormaLoweredDocument document)
    {
        if (node.TypeName.Contains('.')) return null;
        if (node.XmlNamespace == Forma.Xaml.XamlNamespaces.Forma)
        {
            var forma = typeSystem.Resolve(module.AssemblyReferences.Single(reference => reference.Name == "Forma"));
            return forma.MainModule.GetType($"Forma.{node.TypeName}") ?? forma.MainModule.GetType($"Forma.Xaml.{node.TypeName}");
        }
        if (!node.XmlNamespace.StartsWith("clr-namespace:", StringComparison.Ordinal)) return null;
        var definition = node.XmlNamespace.Substring("clr-namespace:".Length).Split(';');
        var assemblyName = definition.Skip(1).FirstOrDefault(value => value.StartsWith("assembly=", StringComparison.Ordinal))?.Substring("assembly=".Length) ?? module.Assembly.Name.Name;
        var assembly = assemblyName == module.Assembly.Name.Name ? module.Assembly : typeSystem.Resolve(module.AssemblyReferences.Single(reference => reference.Name == assemblyName));
        return assembly.MainModule.GetType($"{definition[0]}.{node.TypeName}");
    }

    private static TypeDefinition? ResolveSymbolType(CecilTypeSystem typeSystem, ModuleDefinition module, FormaLoweredDocument document, FormaSymbolId symbolId)
    {
        if (!symbolId.IsResolved) return null;
        var symbol = document.Symbols[symbolId.Value];
        if (symbol.Namespace == Forma.Xaml.XamlNamespaces.Forma)
        {
            var forma = typeSystem.Resolve(module.AssemblyReferences.Single(reference => reference.Name == "Forma"));
            return forma.MainModule.GetType($"Forma.{symbol.Name}") ?? forma.MainModule.GetType($"Forma.Xaml.{symbol.Name}");
        }
        if (!symbol.Namespace.StartsWith("clr-namespace:", StringComparison.Ordinal)) return null;
        var definition = symbol.Namespace.Substring("clr-namespace:".Length).Split(';');
        var assemblyName = definition.Skip(1).FirstOrDefault(value => value.StartsWith("assembly=", StringComparison.Ordinal))?.Substring("assembly=".Length) ?? module.Assembly.Name.Name;
        var assembly = assemblyName == module.Assembly.Name.Name ? module.Assembly : typeSystem.Resolve(module.AssemblyReferences.Single(reference => reference.Name == assemblyName));
        return assembly.MainModule.GetType($"{definition[0]}.{symbol.Name}");
    }

    private static TypeDefinition? ResolveBindingSourceType(
        CecilTypeSystem typeSystem,
        ModuleDefinition module,
        FormaLoweredDocument document,
        FormaBindingValue binding,
        TypeDefinition? dataContextType) =>
        binding.Source.Kind == FormaBindingSourceKind.DataContext
            ? dataContextType
            : ResolveSymbolType(typeSystem, module, document, binding.Source.TypeSymbolId);

    private static TypeDefinition ResolveRequiredBindingSourceType(
        CecilTypeSystem typeSystem,
        ModuleDefinition module,
        FormaLoweredDocument document,
        FormaLoweredMember member,
        FormaBindingValue binding,
        TypeDefinition? dataContextType)
    {
        var sourceType = ResolveBindingSourceType(typeSystem, module, document, binding, dataContextType);
        if (sourceType != null) return sourceType;
        if (binding.Source.Kind == FormaBindingSourceKind.DataContext)
            throw new InvalidOperationException("Compiled binding emission requires a resolvable source type.");

        var range = member.SourceRange;
        throw new FormaXamlCompilationException([
            new FormaDiagnostic(
                FormaDiagnosticCodes.RelativeSource,
                FormaDiagnosticSeverity.Error,
                $"RelativeSource '{binding.Source.Kind}' requires a resolvable source type.",
                new FormaSourceLocation(range.FilePath, range.StartLine, range.StartColumn, range.EndLine, range.EndColumn)),
        ]);
    }

    private static bool IsControl(TypeDefinition? type, TypeDefinition controlType)
    {
        for (var current = type; current != null;)
        {
            if (current.FullName == controlType.FullName || current.BaseType?.FullName == controlType.FullName) return true;
            current = current.BaseType?.Resolve();
        }
        return false;
    }

    private static PropertyDefinition? FindProperty(TypeDefinition type, string name)
    {
        for (var current = type; current != null; current = current.BaseType?.Resolve())
        {
            var property = current.Properties.FirstOrDefault(candidate => candidate.Name == name);
            if (property != null) return property;
        }
        return null;
    }

    private static EventDefinition? FindEvent(TypeDefinition type, string name)
    {
        for (var current = type; current != null; current = current.BaseType?.Resolve())
        {
            var eventDefinition = current.Events.FirstOrDefault(candidate => candidate.Name == name);
            if (eventDefinition != null) return eventDefinition;
        }
        return null;
    }

    private static MethodDefinition? FindMethod(TypeDefinition type, string name)
    {
        for (var current = type; current != null; current = current.BaseType?.Resolve())
        {
            var method = current.Methods.FirstOrDefault(candidate => candidate.Name == name && !candidate.IsStatic);
            if (method != null) return method;
        }
        return null;
    }

    private static void ValidateEventHandler(EventDefinition eventDefinition, MethodDefinition handler)
    {
        var invoke = eventDefinition.EventType.Resolve().Methods.Single(method => method.Name == "Invoke");
        if (handler.ReturnType.FullName != invoke.ReturnType.FullName || handler.Parameters.Count != invoke.Parameters.Count ||
            handler.Parameters.Where((parameter, index) => parameter.ParameterType.FullName !=
                ResolveDelegateParameterType(eventDefinition.EventType, invoke.Parameters[index].ParameterType).FullName).Any())
            throw new InvalidOperationException($"Event handler '{handler.FullName}' is incompatible with '{eventDefinition.EventType.FullName}'.");
    }

    private static TypeReference ResolveDelegateParameterType(TypeReference eventType, TypeReference parameterType)
    {
        if (parameterType is GenericParameter parameter &&
            parameter.Type == GenericParameterType.Type &&
            eventType is GenericInstanceType genericEvent &&
            parameter.Position < genericEvent.GenericArguments.Count)
            return genericEvent.GenericArguments[parameter.Position];
        return parameterType;
    }

    private static MethodDefinition DefineEventAccessor(ModuleDefinition module, TypeDefinition owner, int index, TypeDefinition targetType, EventDefinition eventDefinition, TypeReference handlerType, bool add)
    {
        var method = new MethodDefinition($"__Event{(add ? "Add" : "Remove")}{index}", MethodAttributes.Private | MethodAttributes.Static, module.TypeSystem.Void);
        method.Parameters.Add(new ParameterDefinition("target", ParameterAttributes.None, module.ImportReference(targetType)));
        method.Parameters.Add(new ParameterDefinition("handler", ParameterAttributes.None, handlerType));
        owner.Methods.Add(method);
        var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, module.ImportReference(add ? eventDefinition.AddMethod : eventDefinition.RemoveMethod));
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodDefinition DefineEventHandlerBridge(ModuleDefinition module, TypeDefinition rootType, int index, MethodDefinition handler)
    {
        var method = new MethodDefinition($"__FormaEventHandler{index}", MethodAttributes.Assembly | MethodAttributes.HideBySig, module.ImportReference(handler.ReturnType));
        foreach (var parameter in handler.Parameters)
            method.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, module.ImportReference(parameter.ParameterType)));
        rootType.Methods.Add(method);
        var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        for (var parameterIndex = 0; parameterIndex < method.Parameters.Count; parameterIndex++)
            il.Emit(OpCodes.Ldarg, parameterIndex + 1);
        il.Emit(OpCodes.Call, module.ImportReference(handler));
        il.Emit(OpCodes.Ret);
        return method;
    }

        private static void EmitStyles(CecilTypeSystem typeSystem, ModuleDefinition module, TypeDefinition generatedType, MethodDefinition wrapper, FormaLoweredDocument lowered, bool authoring = false)
    {
            var styleNodes = NodesForOperations(lowered, FormaLoweredOperationKind.Style).ToArray();
            if (styleNodes.Length == 0) return;
            var formaAssembly = typeSystem.Resolve(module.AssemblyReferences.Single(reference => reference.Name == "Forma"));
            var controlType = formaAssembly.MainModule.GetType("Forma.Control");
            var resourceDictionaryType = formaAssembly.MainModule.GetType("Forma.Xaml.ResourceDictionary");
            var styleType = formaAssembly.MainModule.GetType("Forma.Xaml.Style");
            var styleSetterDefinition = formaAssembly.MainModule.GetType("Forma.Xaml.StyleSetter`1");
            var xamlPropertyDefinition = formaAssembly.MainModule.GetType("Forma.Xaml.XamlProperty`1");
            var styleEngineType = formaAssembly.MainModule.GetType("Forma.Xaml.StyleEngine");
            var staticResourceType = formaAssembly.MainModule.GetType("Forma.Xaml.StaticResource");
            var nameScopeType = formaAssembly.MainModule.GetType("Forma.Xaml.NameScope");
            var findOrdinal = module.ImportReference(nameScopeType.Methods.Single(method => method.Name == "FindControlByOrdinal" && method.IsPublic && method.Parameters.Count == 2));
            var findName = module.ImportReference(nameScopeType.Methods.Single(method => method.Name == "FindControlByName" && method.IsPublic && method.Parameters.Count == 2));
            var styleConstructor = module.ImportReference(styleType.Methods.Single(method => method.IsConstructor && method.Parameters.Count == 1 &&
                method.Parameters[0].ParameterType.FullName == "Forma.Xaml.StyleSelector"));
            var addSetter = module.ImportReference(styleType.Methods.Single(method => method.Name == "AddSetter"));
            var addTransition = module.ImportReference(styleType.Methods.Single(method => method.Name == "AddTransition"));
            var resourcesGetter = module.ImportReference(FindProperty(controlType, "Resources")!.GetMethod);
            var addResource = module.ImportReference(resourceDictionaryType.Methods.Single(method => method.Name == "Add" && method.Parameters.Count == 2 && method.Parameters[0].ParameterType.FullName == "System.String"));
            var attach = module.ImportReference(styleEngineType.Methods.Single(method => method.Name == "Attach"));
            var controls = OwnerNodes(lowered).Where(node => IsControl(ResolveObjectType(typeSystem, module, node, lowered), controlType)).ToArray();
            var emitted = new List<(FormaLoweredNode Owner, VariableDefinition Style)>();
            var body = wrapper.Body.GetILProcessor();
            wrapper.Body.InitLocals = true;

            for (var styleIndex = 0; styleIndex < styleNodes.Length; styleIndex++)
            {
                var styleNode = styleNodes[styleIndex];
                var selector = styleNode.Selector ?? throw new InvalidOperationException("Style requires a lowered Selector.");
                var owner = FindResourceOwner(lowered, styleNode) ?? lowered.Nodes[lowered.RootNodeId.Value];
                var styleVariable = new VariableDefinition(module.ImportReference(styleType));
                wrapper.Body.Variables.Add(styleVariable);
                EmitStyleSelector(body, module, formaAssembly, selector);
                body.Emit(OpCodes.Newobj, styleConstructor);
                body.Emit(OpCodes.Stloc, styleVariable);
                EmitAdaptiveCondition(body, module, formaAssembly, wrapper, styleVariable, lowered, styleNode);
                var setters = styleNode.Children.Select(child => lowered.Nodes[child.Value]).Where(child => child.TypeName == "Setter").ToArray();
                for (var setterIndex = 0; setterIndex < setters.Length; setterIndex++)
                {
                    var setter = setters[setterIndex];
                    var propertyName = setter.FindMember("Property") ?? throw new InvalidOperationException("Setter requires Property.");
                    var valueMember = setter.Members.SingleOrDefault(member => !member.IsDirective && member.Name == "Value")
                        ?? throw new InvalidOperationException("Setter requires Value.");
                    var value = valueMember.Value.RawText;
                    var (targetType, property) = ResolveStyleTargetProperty(typeSystem, module, lowered, selector, propertyName, controlType);
                    if (property.GetMethod == null || property.SetMethod == null)
                        throw new InvalidOperationException($"Style setter property '{targetType.FullName}.{propertyName}' must be readable and writable.");
                    var valueType = module.ImportReference(property.PropertyType);
                    var getTarget = DefineTargetGetter(module, generatedType, $"__StyleGetTarget{styleIndex}_{setterIndex}", targetType, property, valueType);
                    var setTarget = DefineTargetSetter(module, generatedType, $"__StyleSetTarget{styleIndex}_{setterIndex}", targetType, property, valueType);
                    var funcTarget = MakeDelegateType(module, typeof(Func<,>), module.TypeSystem.Object, valueType);
                    var actionTarget = MakeDelegateType(module, typeof(Action<,>), module.TypeSystem.Object, valueType);
                    var xamlPropertyType = new GenericInstanceType(module.ImportReference(xamlPropertyDefinition));
                    xamlPropertyType.GenericArguments.Add(valueType);
                    var xamlPropertyConstructor = MakeClosedMethod(module, xamlPropertyDefinition.Methods.Single(method => method.IsConstructor && !method.IsStatic), xamlPropertyType);
                    var styleSetterType = new GenericInstanceType(module.ImportReference(styleSetterDefinition));
                    styleSetterType.GenericArguments.Add(valueType);
                    body.Emit(OpCodes.Ldloc, styleVariable);
                    body.Emit(OpCodes.Ldstr, propertyName);
                    EmitDelegate(body, funcTarget, getTarget);
                    EmitDelegate(body, actionTarget, setTarget);
                    body.Emit(OpCodes.Newobj, xamlPropertyConstructor);
                    if (authoring)
                    {
                        var sourceType = formaAssembly.MainModule.GetType("Forma.Xaml.XamlSource");
                        var register = new GenericInstanceMethod(module.ImportReference(sourceType.Methods.Single(method => method.Name == "RegisterProperty")));
                        register.GenericArguments.Add(valueType);
                        body.Emit(OpCodes.Ldstr, SourceMetadata(lowered, setter, "Forma.Xaml.Setter"));
                        body.Emit(OpCodes.Call, register);
                    }
                    MethodDefinition styleSetterConstructorDefinition;
                    if (valueMember.Value is FormaResourceValue resource)
                    {
                        if (resource.IsDynamic) throw new InvalidOperationException("DynamicResource is not supported in a style setter; use StaticResource or a control-local DynamicResource.");
                        var resolve = new GenericInstanceMethod(module.ImportReference(staticResourceType.Methods.Single(method => method.Name == "Resolve")));
                        resolve.GenericArguments.Add(valueType);
                        var resourceGetter = DefineStyleResourceGetter(module, generatedType, styleIndex, setterIndex, controlType, valueType, resource.Key, resolve);
                        var funcControl = MakeDelegateType(module, typeof(Func<,>), module.ImportReference(controlType), valueType);
                        EmitDelegate(body, funcControl, resourceGetter);
                        styleSetterConstructorDefinition = styleSetterDefinition.Methods.Single(method => method.IsConstructor && method.Parameters.Count == 2 && method.Parameters[1].ParameterType is GenericInstanceType);
                    }
                    else
                    {
                        EmitStyleValue(body, module, formaAssembly, valueType, value);
                        styleSetterConstructorDefinition = styleSetterDefinition.Methods.Single(method => method.IsConstructor && method.Parameters.Count == 2 && method.Parameters[1].ParameterType is GenericParameter);
                    }
                    var styleSetterConstructor = MakeClosedMethod(module, styleSetterConstructorDefinition, styleSetterType);
                    body.Emit(OpCodes.Newobj, styleSetterConstructor);
                    body.Emit(OpCodes.Callvirt, addSetter);
                }
                var transitions = EnumerateStyleTransitions(lowered, styleNode).ToArray();
                for (var transitionIndex = 0; transitionIndex < transitions.Length; transitionIndex++)
                {
                    var transition = transitions[transitionIndex];
                    var propertyName = transition.FindMember("Property") ?? throw new InvalidOperationException($"{transition.TypeName} requires Property.");
                    var duration = transition.FindMember("Duration") ?? throw new InvalidOperationException($"{transition.TypeName} requires Duration.");
                    var (targetType, property) = ResolveStyleTargetProperty(typeSystem, module, lowered, selector, propertyName, controlType);
                    if (property.GetMethod == null || property.SetMethod == null)
                        throw new InvalidOperationException($"Style transition property '{targetType.FullName}.{propertyName}' must be readable and writable.");
                    ValidateStyleTransitionValueType(transition.TypeName, property.PropertyType);
                    var valueType = module.ImportReference(property.PropertyType);
                    var getTarget = DefineTargetGetter(module, generatedType, $"__StyleTransitionGetTarget{styleIndex}_{transitionIndex}", targetType, property, valueType);
                    var setTarget = DefineTargetSetter(module, generatedType, $"__StyleTransitionSetTarget{styleIndex}_{transitionIndex}", targetType, property, valueType);
                    var funcTarget = MakeDelegateType(module, typeof(Func<,>), module.TypeSystem.Object, valueType);
                    var actionTarget = MakeDelegateType(module, typeof(Action<,>), module.TypeSystem.Object, valueType);
                    var xamlPropertyType = new GenericInstanceType(module.ImportReference(xamlPropertyDefinition));
                    xamlPropertyType.GenericArguments.Add(valueType);
                    var xamlPropertyConstructor = MakeClosedMethod(module, xamlPropertyDefinition.Methods.Single(method => method.IsConstructor && !method.IsStatic), xamlPropertyType);
                    var transitionType = formaAssembly.MainModule.GetType("Forma.Xaml." + transition.TypeName)
                        ?? throw new InvalidOperationException($"Style transition type '{transition.TypeName}' was not found.");
                    var transitionConstructor = module.ImportReference(transitionType.Methods.Single(method => method.IsConstructor && method.Parameters.Count == 3));
                    body.Emit(OpCodes.Ldloc, styleVariable);
                    body.Emit(OpCodes.Ldstr, propertyName);
                    EmitDelegate(body, funcTarget, getTarget);
                    EmitDelegate(body, actionTarget, setTarget);
                    body.Emit(OpCodes.Newobj, xamlPropertyConstructor);
                    EmitTimeSpan(body, module, duration);
                    var easing = Enum.TryParse<Easing>(transition.FindMember("Easing"), true, out var parsedEasing) ? parsedEasing : Easing.Linear;
                    body.Emit(OpCodes.Ldc_I4, (int)easing);
                    body.Emit(OpCodes.Newobj, transitionConstructor);
                    body.Emit(OpCodes.Callvirt, addTransition);
                }

                var key = styleNode.FindDirective("Key");
                if (!string.IsNullOrWhiteSpace(key))
                {
                    EmitFindControl(body, owner, Array.IndexOf(controls, owner), findOrdinal, findName);
                    body.Emit(OpCodes.Callvirt, resourcesGetter);
                    body.Emit(OpCodes.Ldstr, key);
                    body.Emit(OpCodes.Ldloc, styleVariable);
                    body.Emit(OpCodes.Callvirt, addResource);
                }
                emitted.Add((owner, styleVariable));
            }

            foreach (var group in emitted.GroupBy(item => item.Owner))
            {
                EmitFindControl(body, group.Key, Array.IndexOf(controls, group.Key), findOrdinal, findName);
                body.Emit(OpCodes.Ldc_I4, group.Count());
                body.Emit(OpCodes.Newarr, module.ImportReference(styleType));
                var index = 0;
                foreach (var item in group)
                {
                    body.Emit(OpCodes.Dup);
                    body.Emit(OpCodes.Ldc_I4, index++);
                    body.Emit(OpCodes.Ldloc, item.Style);
                    body.Emit(OpCodes.Stelem_Ref);
                }
                body.Emit(OpCodes.Call, attach);
                body.Emit(OpCodes.Pop);
            }
    }

    private static FormaLoweredNode? FindResourceOwner(FormaLoweredDocument document, FormaLoweredNode node)
    {
        for (var parentId = node.ParentId; parentId != null; parentId = document.Nodes[parentId.Value.Value].ParentId)
        {
            var current = document.Nodes[parentId.Value.Value];
            if (current.TypeName.EndsWith(".Resources", StringComparison.Ordinal) && current.ParentId != null)
                return document.Nodes[current.ParentId.Value.Value];
        }
        return null;
    }

    private static bool HasAncestor(FormaLoweredDocument document, FormaLoweredNode node, string typeName)
    {
        for (var parentId = node.ParentId; parentId != null; parentId = document.Nodes[parentId.Value.Value].ParentId)
            if (document.Nodes[parentId.Value.Value].TypeName == typeName) return true;
        return false;
    }

    private static IEnumerable<FormaLoweredNode> OwnerNodes(FormaLoweredDocument lowered)
    {
        return ScopeNodes(lowered, lowered.OwnerScope);
    }

    private static IEnumerable<FormaLoweredNode> ScopeNodes(FormaLoweredDocument lowered, FormaLoweredScope scope)
    {
        return scope.Operations
            .Where(operation => operation.Kind is not FormaLoweredOperationKind.AddChild and not FormaLoweredOperationKind.SetMember and
                not FormaLoweredOperationKind.Binding and not FormaLoweredOperationKind.ResourceReference and not FormaLoweredOperationKind.AttachedProperty)
            .Select(operation => lowered.Nodes[operation.NodeId.Value])
            .Distinct();
    }

    private static IEnumerable<FormaLoweredNode> NodesForOperations(FormaLoweredDocument lowered, FormaLoweredOperationKind kind)
    {
        return lowered.OwnerScope.Operations.Where(operation => operation.Kind == kind).Select(operation => lowered.Nodes[operation.NodeId.Value]);
    }

    private static FormaLoweredNode NodeForId(FormaLoweredDocument lowered, FormaNodeId id) => lowered.Nodes[id.Value];

    private static FormaLoweredMember MemberForOperation(FormaLoweredDocument lowered, FormaLoweredOperation operation)
    {
        var node = NodeForId(lowered, operation.NodeId);
        return node.Members.Single(member => !member.IsDirective && member.SymbolId == operation.MemberSymbolId);
    }

    private static (TypeDefinition TargetType, PropertyDefinition Property) ResolveStyleTargetProperty(
        CecilTypeSystem typeSystem,
        ModuleDefinition module,
        FormaLoweredDocument document,
        StyleSelector selector,
        string propertyName,
        TypeDefinition controlType)
    {
        var subjects = selector.Arms.Select(arm =>
        {
            var typeName = arm.Subject.TypeName;
            if (string.IsNullOrWhiteSpace(typeName)) return controlType;
            return ResolveStyleSelectorType(typeSystem, module, document, typeName, controlType);
        }).DistinctBy(type => type.FullName).ToArray();
        var properties = subjects.Select(subject => FindProperty(subject, propertyName)).ToArray();
        if (properties.Any(property => property == null))
            throw new InvalidOperationException($"Style setter property '{propertyName}' is not available on every selector-list subject type.");
        var property = properties[0]!;
        var targetType = property.DeclaringType;
        if (properties.Any(candidate => candidate!.PropertyType.FullName != property.PropertyType.FullName || candidate.DeclaringType.FullName != targetType.FullName) ||
            subjects.Any(subject => !DerivesFrom(subject, targetType)))
            throw new InvalidOperationException($"Style setter property '{propertyName}' does not have one compatible typed target across every selector-list arm.");
        return (targetType, property);
    }

    private static TypeDefinition ResolveStyleSelectorType(
        CecilTypeSystem typeSystem,
        ModuleDefinition module,
        FormaLoweredDocument document,
        string typeName,
        TypeDefinition controlType)
    {
        var separator = typeName.IndexOf(':');
        if (separator > 0 && document.Namespaces.TryGetValue(typeName.Substring(0, separator), out var xmlNamespace))
            return ResolveSelectorType(typeSystem, module, xmlNamespace, typeName.Substring(separator + 1))
                ?? throw new InvalidOperationException($"Style selector type '{typeName}' was not resolved.");
        var forma = typeSystem.Resolve(module.AssemblyReferences.Single(reference => reference.Name == "Forma"));
        var candidates = module.Types.SelectMany(AllTypes).Where(type => type.Name == typeName)
            .Concat(forma.MainModule.Types.SelectMany(AllTypes).Where(type => type.Name == typeName))
            .Concat(document.Namespaces.Values.Distinct(StringComparer.Ordinal)
                .Select(xmlNamespace => ResolveSelectorType(typeSystem, module, xmlNamespace, typeName))
                .OfType<TypeDefinition>())
            .DistinctBy(type => type.Module.Assembly.Name.Name + ":" + type.FullName)
            .ToArray();
        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException($"Style selector type '{typeName}' was not resolved."),
            _ => throw new InvalidOperationException($"Style selector type '{typeName}' is ambiguous; qualify it with an XML namespace prefix."),
        };
    }

    private static void ValidateStylePseudoStates(
        CecilTypeSystem typeSystem,
        ModuleDefinition module,
        FormaLoweredDocument document,
        StyleSelectorCompound compound,
        TypeDefinition fallbackType)
    {
        var candidateType = string.IsNullOrWhiteSpace(compound.TypeName)
            ? fallbackType
            : ResolveStyleSelectorType(typeSystem, module, document, compound.TypeName, fallbackType);
        foreach (var state in compound.PseudoStates)
        {
            var standardOwner = state switch
            {
                "hover" or "focus" or "focus-within" or "disabled" or "selected" or "current" => "Forma.Control",
                "pressed" or "checked" => "Forma.BaseButton",
                _ => null,
            };
            if (standardOwner != null)
            {
                var forma = typeSystem.Resolve(module.AssemblyReferences.Single(reference => reference.Name == "Forma"));
                var owner = forma.MainModule.GetType(standardOwner);
                if (!DerivesFrom(candidateType, owner))
                    throw new InvalidOperationException($"Pseudo state ':{state}' is not available on selector type '{candidateType.Name}'.");
                continue;
            }

            var registrations = candidateType.Module.Assembly.CustomAttributes.Where(attribute =>
                attribute.AttributeType.FullName == "Forma.Xaml.PseudoStateAttribute" && attribute.ConstructorArguments.Count == 4 &&
                string.Equals(attribute.ConstructorArguments[0].Value as string, state, StringComparison.Ordinal)).ToArray();
            if (registrations.Length == 0)
                throw new InvalidOperationException($"Pseudo state ':{state}' is not registered for selector type '{candidateType.Name}'.");
            if (registrations.Length > 1)
                throw new InvalidOperationException($"Pseudo state ':{state}' has duplicate registrations in assembly '{candidateType.Module.Assembly.Name.Name}'.");

            var arguments = registrations[0].ConstructorArguments;
            var ownerType = (arguments[1].Value as TypeReference)?.Resolve();
            var inherited = arguments[2].Value is true;
            var providerMember = arguments[3].Value as string;
            var available = ownerType != null && (inherited ? DerivesFrom(candidateType, ownerType) : candidateType.FullName == ownerType.FullName);
            var provider = ownerType == null ? null : FindMethod(ownerType, providerMember ?? string.Empty);
            if (!available)
                throw new InvalidOperationException($"Pseudo state ':{state}' is not available on selector type '{candidateType.Name}'.");
            if (provider == null || provider.ReturnType.FullName != "System.Boolean" || provider.Parameters.Count != 1 ||
                provider.Parameters[0].ParameterType.FullName != "System.String" || !DerivesFrom(ownerType!, fallbackType))
                throw new InvalidOperationException($"Pseudo state ':{state}' metadata does not agree with its runtime provider '{providerMember}'.");
        }
        foreach (var negation in compound.Negations)
            ValidateStylePseudoStates(typeSystem, module, document, negation, candidateType);
    }

    private static TypeDefinition? ResolveSelectorType(CecilTypeSystem typeSystem, ModuleDefinition module, string xmlNamespace, string typeName)
    {
        if (xmlNamespace == XamlNamespaces.Forma)
        {
            var forma = typeSystem.Resolve(module.AssemblyReferences.Single(reference => reference.Name == "Forma"));
            return forma.MainModule.Types.SelectMany(AllTypes).FirstOrDefault(type => type.Name == typeName);
        }
        if (!xmlNamespace.StartsWith("clr-namespace:", StringComparison.Ordinal)) return null;
        var parts = xmlNamespace.Substring("clr-namespace:".Length).Split(';');
        var fullName = parts[0] + "." + typeName;
        var assemblyName = parts.Skip(1).FirstOrDefault(part => part.StartsWith("assembly=", StringComparison.Ordinal))?.Substring("assembly=".Length);
        if (string.IsNullOrWhiteSpace(assemblyName) || assemblyName == module.Assembly.Name.Name) return FindType(module, fullName);
        var reference = module.AssemblyReferences.FirstOrDefault(candidate => candidate.Name == assemblyName);
        return reference == null ? null : typeSystem.Resolve(reference).MainModule.GetType(fullName);
    }

    private static void EmitStyleSelector(ILProcessor body, ModuleDefinition module, AssemblyDefinition formaAssembly, StyleSelector selector)
    {
        var selectorType = formaAssembly.MainModule.GetType("Forma.Xaml.StyleSelector");
        var armType = formaAssembly.MainModule.GetType("Forma.Xaml.StyleSelectorArm");
        EmitReferenceArray(body, module, module.ImportReference(armType), selector.Arms, arm =>
            EmitStyleSelectorArm(body, module, formaAssembly, arm));
        body.Emit(OpCodes.Newobj, module.ImportReference(selectorType.Methods.Single(method => method.IsConstructor && !method.IsStatic)));
    }

    private static void EmitStyleSelectorArm(ILProcessor body, ModuleDefinition module, AssemblyDefinition formaAssembly, StyleSelectorArm arm)
    {
        var compoundType = formaAssembly.MainModule.GetType("Forma.Xaml.StyleSelectorCompound");
        var combinatorType = formaAssembly.MainModule.GetType("Forma.Xaml.StyleSelectorCombinator");
        var armType = formaAssembly.MainModule.GetType("Forma.Xaml.StyleSelectorArm");
        EmitReferenceArray(body, module, module.ImportReference(compoundType), arm.Compounds, compound =>
            EmitStyleSelectorCompound(body, module, formaAssembly, compound));
        body.Emit(OpCodes.Ldc_I4, arm.Combinators.Count);
        body.Emit(OpCodes.Newarr, module.ImportReference(combinatorType));
        for (var index = 0; index < arm.Combinators.Count; index++)
        {
            body.Emit(OpCodes.Dup);
            body.Emit(OpCodes.Ldc_I4, index);
            body.Emit(OpCodes.Ldc_I4, (int)arm.Combinators[index]);
            body.Emit(OpCodes.Stelem_I4);
        }
        body.Emit(OpCodes.Newobj, module.ImportReference(armType.Methods.Single(method => method.IsConstructor && !method.IsStatic)));
    }

    private static void EmitStyleSelectorCompound(ILProcessor body, ModuleDefinition module, AssemblyDefinition formaAssembly, StyleSelectorCompound compound)
    {
        var compoundType = formaAssembly.MainModule.GetType("Forma.Xaml.StyleSelectorCompound");
        EmitNullableString(body, NormalizeSelectorTypeName(compound.TypeName));
        body.Emit(compound.IsUniversal ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        EmitNullableString(body, compound.Name);
        EmitStringArray(body, module, compound.Classes);
        EmitStringArray(body, module, compound.PseudoStates);
        EmitReferenceArray(body, module, module.ImportReference(compoundType), compound.Negations, negation =>
            EmitStyleSelectorCompound(body, module, formaAssembly, negation));
        body.Emit(OpCodes.Newobj, module.ImportReference(compoundType.Methods.Single(method => method.IsConstructor && !method.IsStatic)));
    }

    private static void EmitReferenceArray<T>(ILProcessor body, ModuleDefinition module, TypeReference elementType, IReadOnlyList<T> values, Action<T> emitValue)
    {
        body.Emit(OpCodes.Ldc_I4, values.Count);
        body.Emit(OpCodes.Newarr, elementType);
        for (var index = 0; index < values.Count; index++)
        {
            body.Emit(OpCodes.Dup);
            body.Emit(OpCodes.Ldc_I4, index);
            emitValue(values[index]);
            body.Emit(OpCodes.Stelem_Ref);
        }
    }

    private static void EmitStringArray(ILProcessor body, ModuleDefinition module, IReadOnlyList<string> values) =>
        EmitReferenceArray(body, module, module.TypeSystem.String, values, value => body.Emit(OpCodes.Ldstr, value));

    private static void EmitNullableString(ILProcessor body, string? value)
    {
        if (value == null) body.Emit(OpCodes.Ldnull);
        else body.Emit(OpCodes.Ldstr, value);
    }

    private static string? NormalizeSelectorTypeName(string? typeName) =>
        typeName is { } value && value.Contains(':') ? value.Substring(value.IndexOf(':') + 1) : typeName;

    private static bool DerivesFrom(TypeDefinition type, TypeDefinition expected)
    {
        for (var current = type; current != null;)
        {
            if (current.FullName == expected.FullName) return true;
            if (current.BaseType == null) return false;
            try { current = current.BaseType.Resolve(); }
            catch { return false; }
        }
        return false;
    }

    private static bool IsAssignableTo(TypeReference source, TypeReference target)
    {
        if (source.FullName == target.FullName || target.FullName == "System.Object") return true;
        if (source is ArrayType array)
        {
            if (target.FullName is "System.Array" or "System.Collections.IEnumerable" or "System.Collections.ICollection" or "System.Collections.IList")
                return true;
            if (target is GenericInstanceType generic && generic.GenericArguments.Count == 1 &&
                generic.GenericArguments[0].FullName == array.ElementType.FullName &&
                generic.ElementType.FullName is "System.Collections.Generic.IEnumerable`1" or "System.Collections.Generic.ICollection`1" or "System.Collections.Generic.IList`1" or "System.Collections.Generic.IReadOnlyCollection`1" or "System.Collections.Generic.IReadOnlyList`1")
                return true;
        }
        TypeDefinition definition;
        try { definition = source.Resolve(); }
        catch { return false; }
        for (var current = definition; current != null;)
        {
            if (current.FullName == target.FullName || current.Interfaces.Any(implementation => IsAssignableTo(implementation.InterfaceType, target)))
                return true;
            if (current.BaseType == null) break;
            try { current = current.BaseType.Resolve(); }
            catch { break; }
        }
        return false;
    }

    private static void EmitAdaptiveCondition(
        ILProcessor body,
        ModuleDefinition module,
        AssemblyDefinition formaAssembly,
        MethodDefinition method,
        VariableDefinition styleVariable,
        FormaLoweredDocument lowered,
        FormaLoweredNode styleNode)
    {
        var conditionNode = FindAdaptiveCondition(lowered, styleNode);
        if (conditionNode == null) return;
        var adaptiveType = formaAssembly.MainModule.GetType("Forma.Xaml.AdaptiveCondition");
        var styleType = formaAssembly.MainModule.GetType("Forma.Xaml.Style");
        var conditionVariable = new VariableDefinition(module.ImportReference(adaptiveType));
        method.Body.Variables.Add(conditionVariable);
        method.Body.InitLocals = true;
        body.Emit(OpCodes.Newobj, module.ImportReference(adaptiveType.Methods.Single(candidate => candidate.IsConstructor && !candidate.IsStatic)));
        body.Emit(OpCodes.Stloc, conditionVariable);
        var setCompiledValue = module.ImportReference(adaptiveType.Methods.Single(candidate => candidate.Name == "SetCompiledValue"));
        foreach (var member in conditionNode.Members.Where(member => !member.IsDirective))
        {
            body.Emit(OpCodes.Ldloc, conditionVariable);
            body.Emit(OpCodes.Ldstr, member.Name);
            body.Emit(OpCodes.Ldstr, member.Value.RawText);
            body.Emit(OpCodes.Callvirt, setCompiledValue);
        }
        body.Emit(OpCodes.Ldloc, styleVariable);
        body.Emit(OpCodes.Ldloc, conditionVariable);
        body.Emit(OpCodes.Callvirt, module.ImportReference(FindProperty(styleType, "Condition")!.SetMethod));
    }

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

    private static void ValidateStyleTransitionValueType(string transitionTypeName, TypeReference valueType)
    {
        var expected = transitionTypeName switch
        {
            "FloatTransition" => "System.Single",
            "ColorTransition" => "Microsoft.Xna.Framework.Color",
            "Vector2Transition" => "Microsoft.Xna.Framework.Vector2",
            "ThicknessTransition" => "Forma.Thickness",
            _ => throw new InvalidOperationException($"Style transition type '{transitionTypeName}' is not supported."),
        };
        if (valueType.FullName != expected)
            throw new InvalidOperationException($"Style transition '{transitionTypeName}' cannot animate '{valueType.FullName}'.");
    }

    private static void EmitStyleValue(ILProcessor body, ModuleDefinition module, AssemblyDefinition formaAssembly, TypeReference valueType, string value)
    {
        if (valueType is GenericInstanceType nullable && nullable.ElementType.FullName == "System.Nullable`1")
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                var empty = new VariableDefinition(valueType);
                body.Body.Variables.Add(empty);
                body.Body.InitLocals = true;
                body.Emit(OpCodes.Ldloca, empty);
                body.Emit(OpCodes.Initobj, valueType);
                body.Emit(OpCodes.Ldloc, empty);
            }
            else
            {
                EmitStyleValue(body, module, formaAssembly, nullable.GenericArguments[0], value);
                body.Emit(OpCodes.Newobj, MakeClosedMethod(module,
                    nullable.Resolve().Methods.Single(method => method.IsConstructor && !method.IsStatic && method.Parameters.Count == 1), nullable));
            }
            return;
        }
        switch (valueType.FullName)
        {
            case "System.String": body.Emit(OpCodes.Ldstr, value); return;
            case "System.Boolean": body.Emit(bool.Parse(value) ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0); return;
            case "System.Int32": body.Emit(OpCodes.Ldc_I4, int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)); return;
            case "System.Single": body.Emit(OpCodes.Ldc_R4, float.Parse(value, System.Globalization.CultureInfo.InvariantCulture)); return;
            case "System.Double": body.Emit(OpCodes.Ldc_R8, double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)); return;
        }
        var converterType = formaAssembly.MainModule.GetType("Forma.Xaml.XamlValueConverter");
        var methodName = valueType.FullName switch
        {
            "Microsoft.Xna.Framework.Color" => "ParseColor",
            "Microsoft.Xna.Framework.Vector2" => "ParseVector2",
            "Forma.Brush" => "ParseBrush",
            "Forma.Thickness" => "ParseThickness",
            _ => null,
        };
        if (methodName == null) throw new InvalidOperationException($"Style value type '{valueType.FullName}' is not supported by compiled setters.");
        body.Emit(OpCodes.Ldstr, value);
        body.Emit(OpCodes.Call, module.ImportReference(converterType.Methods.Single(method => method.Name == methodName)));
    }

    private static void ValidateTimelineValueType(string timelineTypeName, TypeReference valueType)
    {
        var expected = timelineTypeName switch
        {
            "FloatTimeline" => "System.Single",
            "ColorTimeline" => "Microsoft.Xna.Framework.Color",
            "Vector2Timeline" => "Microsoft.Xna.Framework.Vector2",
            "ThicknessTimeline" => "Forma.Thickness",
            _ => throw new InvalidOperationException($"Timeline type '{timelineTypeName}' is not supported."),
        };
        if (valueType.FullName != expected)
            throw new InvalidOperationException($"Timeline '{timelineTypeName}' cannot animate '{valueType.FullName}'.");
    }

    private static void EmitTimeSpan(ILProcessor body, ModuleDefinition module, string value)
    {
        var time = TimeSpan.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        body.Emit(OpCodes.Ldc_I8, time.Ticks);
        body.Emit(OpCodes.Newobj, module.ImportReference(typeof(TimeSpan).GetConstructor([typeof(long)])!));
    }

    private static MethodDefinition DefineTriggerEventAccessor(ModuleDefinition module, TypeDefinition owner, string id, TypeDefinition targetType, EventDefinition eventDefinition, TypeReference handlerType, bool add)
    {
        var method = new MethodDefinition($"__TriggerEvent{(add ? "Add" : "Remove")}{id}", MethodAttributes.Private | MethodAttributes.Static, module.TypeSystem.Void);
        method.Parameters.Add(new ParameterDefinition("target", ParameterAttributes.None, module.ImportReference(targetType)));
        method.Parameters.Add(new ParameterDefinition("handler", ParameterAttributes.None, handlerType));
        owner.Methods.Add(method);
        var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, module.ImportReference(add ? eventDefinition.AddMethod : eventDefinition.RemoveMethod));
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodDefinition DefineTriggerHandlerFactory(ModuleDefinition module, TypeDefinition owner, string id, EventDefinition eventDefinition, TypeReference handlerType)
    {
        var invoke = eventDefinition.EventType.Resolve().Methods.Single(method => method.Name == "Invoke");
        if (invoke.ReturnType.FullName != "System.Void")
            throw new InvalidOperationException($"Event trigger '{eventDefinition.FullName}' must use a void delegate.");
        var handler = new MethodDefinition($"__TriggerEventHandler{id}", MethodAttributes.Private | MethodAttributes.Static, module.TypeSystem.Void);
        handler.Parameters.Add(new ParameterDefinition("action", ParameterAttributes.None, module.ImportReference(typeof(Action))));
        foreach (var parameter in invoke.Parameters)
            handler.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, module.ImportReference(parameter.ParameterType)));
        owner.Methods.Add(handler);
        var handlerIl = handler.Body.GetILProcessor();
        handlerIl.Emit(OpCodes.Ldarg_0);
        handlerIl.Emit(OpCodes.Callvirt, module.ImportReference(typeof(Action).GetMethod(nameof(Action.Invoke))!));
        handlerIl.Emit(OpCodes.Ret);

        var factory = new MethodDefinition($"__TriggerEventFactory{id}", MethodAttributes.Private | MethodAttributes.Static, handlerType);
        factory.Parameters.Add(new ParameterDefinition("action", ParameterAttributes.None, module.ImportReference(typeof(Action))));
        owner.Methods.Add(factory);
        var handlerConstructor = new MethodReference(".ctor", module.TypeSystem.Void, handlerType) { HasThis = true };
        handlerConstructor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
        handlerConstructor.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));
        var factoryIl = factory.Body.GetILProcessor();
        factoryIl.Emit(OpCodes.Ldarg_0);
        factoryIl.Emit(OpCodes.Ldftn, handler);
        factoryIl.Emit(OpCodes.Newobj, handlerConstructor);
        factoryIl.Emit(OpCodes.Ret);
        return factory;
    }

    private static MethodReference MakeClosedMethod(ModuleDefinition module, MethodDefinition definition, GenericInstanceType declaringType)
    {
        var method = new MethodReference(definition.Name, module.ImportReference(definition.ReturnType), declaringType)
        {
            HasThis = definition.HasThis,
            ExplicitThis = definition.ExplicitThis,
            CallingConvention = definition.CallingConvention,
        };
        foreach (var parameter in definition.Parameters)
            method.Parameters.Add(new ParameterDefinition(module.ImportReference(parameter.ParameterType, method)));
        return method;
    }

    private static MethodDefinition DefineStyleResourceGetter(ModuleDefinition module, TypeDefinition owner, int styleIndex, int setterIndex, TypeDefinition controlType, TypeReference valueType, string key, MethodReference resolve)
    {
        var method = new MethodDefinition($"__StyleResourceValue{styleIndex}_{setterIndex}", MethodAttributes.Private | MethodAttributes.Static, valueType);
        method.Parameters.Add(new ParameterDefinition("target", ParameterAttributes.None, module.ImportReference(controlType)));
        owner.Methods.Add(method);
        var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, key);
        il.Emit(OpCodes.Call, resolve);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static void EmitFindControl(ILProcessor body, FormaLoweredNode node, int ordinal, MethodReference findOrdinal, MethodReference findName)
    {
        body.Emit(OpCodes.Ldarg_1);
        EmitFindControlCore(body, node, ordinal, findOrdinal, findName);
    }

    private static void EmitFindTemplateControl(ILProcessor body, FormaLoweredNode node, int ordinal, MethodReference findOrdinal, MethodReference findName)
    {
        body.Emit(OpCodes.Ldarg_1);
        EmitFindControlCore(body, node, ordinal, findOrdinal, findName);
    }

    private static void EmitFindControlCore(ILProcessor body, FormaLoweredNode node, int ordinal, MethodReference findOrdinal, MethodReference findName)
    {
        var targetName = node.FindDirective("Name");
        if (string.IsNullOrWhiteSpace(targetName))
        {
            body.Emit(OpCodes.Ldc_I4, ordinal);
            body.Emit(OpCodes.Call, findOrdinal);
        }
        else
        {
            body.Emit(OpCodes.Ldstr, targetName);
            body.Emit(OpCodes.Call, findName);
        }
    }

    private static UpdateSourceTrigger ParseUpdateSourceTrigger(FormaBindingValue binding) =>
        Enum.TryParse<UpdateSourceTrigger>(binding.Options.GetValueOrDefault("UpdateSourceTrigger"), out var trigger) ? trigger : UpdateSourceTrigger.Default;

    private static string? ResolveBindingAdapter(TypeDefinition targetType, PropertyDefinition property)
    {
        if (property.Name == "Text" && IsType(targetType, "Forma.LineEdit")) return "LineEditText";
        if (property.Name == "Value" && IsType(targetType, "Forma.Range")) return "RangeValue";
        if (property.Name == "ButtonPressed" && IsType(targetType, "Forma.BaseButton")) return "ButtonPressed";
        if (property.Name == "Checked" && IsType(targetType, "Forma.CheckBox")) return "CheckBoxChecked";
        if (property.Name == "Selected" && IsType(targetType, "Forma.OptionButton")) return "OptionButtonSelected";
        if (property.Name == "SelectedIndex" && IsType(targetType, "Forma.ListBox")) return "ListBoxSelectedIndex";
        if (property.Name == "SelectedItem" && IsType(targetType, "Forma.ListBox")) return "ListBoxSelectedItem";
        return null;
    }

    private static bool IsType(TypeDefinition type, string fullName)
    {
        for (var current = type; current != null; current = current.BaseType?.Resolve()) if (current.FullName == fullName) return true;
        return false;
    }

    private static MethodDefinition DefineSourceGetter(ModuleDefinition module, TypeDefinition owner, int index, TypeDefinition sourceType, PropertyDefinition property, TypeReference valueType)
        => DefineSourceGetter(module, owner, $"__BindingRead{index}", sourceType, property, valueType);

    private static MethodDefinition DefineSourceGetter(ModuleDefinition module, TypeDefinition owner, string name, TypeDefinition sourceType, PropertyDefinition property, TypeReference valueType)
    {
        var method = new MethodDefinition(name, MethodAttributes.Private | MethodAttributes.Static, valueType);
        method.Parameters.Add(new ParameterDefinition("source", ParameterAttributes.None, module.ImportReference(sourceType)));
        owner.Methods.Add(method);
        var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, module.ImportReference(property.GetMethod));
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodDefinition DefineBindingSourceResolver(
        ModuleDefinition module,
        TypeDefinition owner,
        string name,
        TypeDefinition sourceType,
        FormaBindingSource source,
        TypeDefinition compiledBindingSourceType,
        TypeDefinition controlType)
    {
        var resolverName = source.Kind switch
        {
            FormaBindingSourceKind.Self => "Self",
            FormaBindingSourceKind.TemplatedParent => "TemplatedParent",
            FormaBindingSourceKind.FindAncestor => "FindAncestor",
            _ => throw new InvalidOperationException($"Unsupported binding source '{source.Kind}'."),
        };
        var resolver = new GenericInstanceMethod(module.ImportReference(compiledBindingSourceType.Methods.Single(candidate => candidate.Name == resolverName)));
        resolver.GenericArguments.Add(module.ImportReference(sourceType));
        var method = new MethodDefinition(name, MethodAttributes.Private | MethodAttributes.Static, module.ImportReference(sourceType));
        method.Parameters.Add(new ParameterDefinition("target", ParameterAttributes.None, module.ImportReference(controlType)));
        owner.Methods.Add(method);
        var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        if (source.Kind == FormaBindingSourceKind.FindAncestor) il.Emit(OpCodes.Ldc_I4, source.AncestorLevel);
        il.Emit(OpCodes.Call, resolver);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodDefinition DefineSourceSetter(ModuleDefinition module, TypeDefinition owner, int index, TypeDefinition sourceType, PropertyDefinition property, TypeReference valueType)
        => DefineSourceSetter(module, owner, $"__BindingWrite{index}", sourceType, property, valueType);

    private static MethodDefinition DefineSourceSetter(ModuleDefinition module, TypeDefinition owner, string name, TypeDefinition sourceType, PropertyDefinition property, TypeReference valueType)
    {
        var method = new MethodDefinition(name, MethodAttributes.Private | MethodAttributes.Static, module.TypeSystem.Void);
        method.Parameters.Add(new ParameterDefinition("source", ParameterAttributes.None, module.ImportReference(sourceType)));
        method.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, valueType));
        owner.Methods.Add(method);
        var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, module.ImportReference(property.SetMethod));
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodDefinition DefineTargetGetter(ModuleDefinition module, TypeDefinition owner, int index, TypeDefinition targetType, PropertyDefinition property, TypeReference valueType)
        => DefineTargetGetter(module, owner, $"__BindingGetTarget{index}", targetType, property, valueType);

    private static MethodDefinition DefineTargetGetter(ModuleDefinition module, TypeDefinition owner, string name, TypeDefinition targetType, PropertyDefinition property, TypeReference valueType)
    {
        var method = new MethodDefinition(name, MethodAttributes.Private | MethodAttributes.Static, valueType);
        method.Parameters.Add(new ParameterDefinition("target", ParameterAttributes.None, module.TypeSystem.Object));
        owner.Methods.Add(method);
        var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, module.ImportReference(targetType));
        il.Emit(OpCodes.Callvirt, module.ImportReference(property.GetMethod));
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodDefinition DefineTargetSetter(ModuleDefinition module, TypeDefinition owner, int index, TypeDefinition targetType, PropertyDefinition property, TypeReference valueType)
        => DefineTargetSetter(module, owner, $"__BindingSetTarget{index}", targetType, property, valueType);

    private static MethodDefinition DefineTargetSetter(ModuleDefinition module, TypeDefinition owner, string name, TypeDefinition targetType, PropertyDefinition property, TypeReference valueType)
    {
        var method = new MethodDefinition(name, MethodAttributes.Private | MethodAttributes.Static, module.TypeSystem.Void);
        method.Parameters.Add(new ParameterDefinition("target", ParameterAttributes.None, module.TypeSystem.Object));
        method.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, valueType));
        owner.Methods.Add(method);
        var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, module.ImportReference(targetType));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, module.ImportReference(property.SetMethod));
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static GenericInstanceType MakeDelegateType(ModuleDefinition module, Type openType, params TypeReference[] arguments)
    {
        var type = new GenericInstanceType(module.ImportReference(openType));
        foreach (var argument in arguments) type.GenericArguments.Add(argument);
        return type;
    }

    private static void EmitDelegate(ILProcessor il, TypeReference delegateType, MethodDefinition method)
    {
        var constructor = new MethodReference(".ctor", method.Module.TypeSystem.Void, delegateType) { HasThis = true };
        constructor.Parameters.Add(new ParameterDefinition(method.Module.TypeSystem.Object));
        constructor.Parameters.Add(new ParameterDefinition(method.Module.TypeSystem.IntPtr));
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldftn, method);
        il.Emit(OpCodes.Newobj, constructor);
    }
}

public sealed class ValidateFormaSvg : FormaXamlTask
{
    [Required] public ITaskItem[] SvgFiles { get; set; } = [];
    [Required] public string AssemblyName { get; set; } = string.Empty;
    [Required] public string ProjectDirectory { get; set; } = string.Empty;
    [Output] public ITaskItem[] ValidatedSvgFiles { get; private set; } = [];

    public override bool Execute()
    {
        var validated = new List<ITaskItem>();
        var logicalNames = new Dictionary<string, ITaskItem>(StringComparer.Ordinal);
        var success = true;
        foreach (var file in SvgFiles)
        {
            var fullPath = file.GetMetadata("FullPath");
            if (string.IsNullOrWhiteSpace(fullPath)) fullPath = Path.GetFullPath(file.ItemSpec);
            if (!File.Exists(fullPath))
            {
                Log.LogError("Forma XAML", FormaDiagnosticCodes.SvgAssetMissing, null, file.ItemSpec, 1, 1, 0, 0, $"SVG asset '{file.ItemSpec}' was not found.");
                success = false;
                continue;
            }
            string logicalName;
            try
            {
                _ = SvgImageSource.FromFile(fullPath);
                logicalName = SvgAssetLogicalName.Create(AssemblyName, ProjectDirectory, fullPath);
            }
            catch (Exception exception) when (exception is SvgLoadException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Log.LogError("Forma XAML", FormaDiagnosticCodes.SvgAssetInvalid, null, file.ItemSpec, 1, 1, 0, 0, $"SVG asset '{file.ItemSpec}' is invalid: {exception.Message}");
                success = false;
                continue;
            }
            if (logicalNames.TryGetValue(logicalName, out var existing))
            {
                Log.LogError("Forma XAML", FormaDiagnosticCodes.SvgAssetDuplicate, null, file.ItemSpec, 1, 1, 0, 0, $"SVG asset '{file.ItemSpec}' duplicates '{existing.ItemSpec}'.");
                success = false;
                continue;
            }
            var item = new TaskItem(file);
            item.SetMetadata("LogicalName", logicalName);
            logicalNames.Add(logicalName, item);
            validated.Add(item);
        }
        ValidatedSvgFiles = validated.ToArray();
        return success;
    }
}