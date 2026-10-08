// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Forma.Xaml.Compiler;

namespace Forma.Xaml.HotReload;

public enum FormaXamlArtifactKind
{
    Root,
    Primitive,
    Style,
    Resource,
    DataTemplate,
    ControlTemplate,
    DataGridColumn,
    Class,
}

public readonly record struct FormaXamlArtifactId(
    string Source,
    string SemanticNodeId,
    FormaXamlArtifactKind Kind,
    string Key = "");

public sealed class FormaXamlArtifactPreparationContext
{
    private readonly IReadOnlyDictionary<FormaXamlArtifactId, IReadOnlyList<object>> _prepared;

    internal FormaXamlArtifactPreparationContext(IReadOnlyDictionary<FormaXamlArtifactId, IReadOnlyList<object>> prepared) =>
        _prepared = prepared;

    public IReadOnlyList<T> GetPreparedValues<T>(FormaXamlArtifactId artifactId) where T : class =>
        _prepared.TryGetValue(artifactId, out var values)
            ? values.Cast<T>().ToArray()
            : Array.Empty<T>();

    public T GetPreparedValue<T>(FormaXamlArtifactId artifactId) where T : class
    {
        var values = GetPreparedValues<T>(artifactId);
        return values.Count == 1
            ? values[0]
            : throw new InvalidOperationException($"Expected one prepared value for hot-reload artifact '{artifactId}', but found {values.Count}.");
    }
}

public sealed class FormaXamlHotReloadService : IDisposable
{
    private readonly UIContext _context;
    private readonly string _developmentRoot;
    private readonly ConcurrentDictionary<FormaXamlArtifactId, ArtifactRegistration> _artifacts = new();
    private readonly ConcurrentQueue<ReplacementSet> _replacements = new();
    private readonly FileSystemWatcher? _watcher;
    private readonly IDisposable _frameCallback;
    private bool _disposed;

    public FormaXamlHotReloadService(UIContext context, string developmentRoot, bool watchFiles = true)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        if (!RuntimeFeature.IsDynamicCodeSupported || !RuntimeFeature.IsDynamicCodeCompiled)
            throw new PlatformNotSupportedException("Forma XAML hot reload requires dynamic code and is unavailable under NativeAOT.");
        if (AppContext.TryGetSwitch("Forma.Xaml.Trimmed", out var trimmed) && trimmed)
            throw new PlatformNotSupportedException("Forma XAML hot reload is disabled for trimmed applications.");
        _developmentRoot = Path.GetFullPath(developmentRoot ?? throw new ArgumentNullException(nameof(developmentRoot)));
        Directory.CreateDirectory(_developmentRoot);
        if (watchFiles)
        {
            _watcher = new FileSystemWatcher(_developmentRoot, "*.*")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += FileChanged;
            _watcher.Created += FileChanged;
            _watcher.Renamed += FileRenamed;
        }
        _frameCallback = context.RegisterFrameBoundaryCallback(_ => DrainReplacements());
    }

    public event Action<IReadOnlyList<FormaDiagnostic>>? DiagnosticsChanged;

    public IDisposable Register<T>(string source, Func<T> current, Action<T, T> replace) where T : Control
    {
        if (current == null) throw new ArgumentNullException(nameof(current));
        if (replace == null) throw new ArgumentNullException(nameof(replace));
        return RegisterCore(source, typeof(T), () => current(), (oldValue, newValue) =>
        {
            if (newValue is not T typed) throw new InvalidOperationException($"Reloaded root '{newValue.GetType().FullName}' is not assignable to '{typeof(T).FullName}'.");
            replace((T)oldValue, typed);
        });
    }

    public IDisposable Register<T>(string source, T current, Action<T, T> replace) where T : Control
    {
        var value = current ?? throw new ArgumentNullException(nameof(current));
        return Register(source, () => value, (oldValue, newValue) => { replace(oldValue, newValue); value = newValue; });
    }

    public IDisposable RegisterArtifact<T>(
        FormaXamlArtifactId artifactId,
        Func<T> current,
        Func<string, T, T> prepare,
        Action<T, T> replace,
        IEnumerable<FormaXamlArtifactId>? dependencies = null) where T : class
        => RegisterArtifact(artifactId, current, (_, source, value) => prepare(source, value), replace, dependencies);

    public IDisposable RegisterArtifact<T>(
        FormaXamlArtifactId artifactId,
        Func<T> current,
        Func<FormaXamlArtifactPreparationContext, string, T, T> prepare,
        Action<T, T> replace,
        IEnumerable<FormaXamlArtifactId>? dependencies = null) where T : class
    {
        if (current == null) throw new ArgumentNullException(nameof(current));
        if (prepare == null) throw new ArgumentNullException(nameof(prepare));
        if (replace == null) throw new ArgumentNullException(nameof(replace));
        var normalizedId = NormalizeArtifactId(artifactId);
        var normalizedDependencies = NormalizeDependencies(normalizedId, dependencies);
        return RegisterArtifactCore(
            normalizedId,
            () => current() ?? throw new InvalidOperationException($"Hot-reload artifact '{normalizedId}' has no current value."),
            (context, xaml, value) => prepare(context, xaml, (T)value) ?? throw new InvalidOperationException($"Preparing hot-reload artifact '{normalizedId}' returned no value."),
            (oldValue, newValue) => replace((T)oldValue, (T)newValue),
            normalizedDependencies);
    }

    public IDisposable RegisterResource<T>(
        FormaXamlArtifactId artifactId,
        ResourceDictionary resources,
        IEnumerable<FormaXamlArtifactId>? dependencies = null) where T : class
    {
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        if (string.IsNullOrWhiteSpace(artifactId.Key)) throw new ArgumentException("A resource artifact requires a resource key.", nameof(artifactId));
        return RegisterArtifact(
            artifactId,
            () => resources.TryFind(artifactId.Key, out var value) && value is T typed
                ? typed
                : throw new InvalidOperationException($"Resource '{artifactId.Key}' is not a {typeof(T).FullName}."),
            (_, source, _) => CompileResource<T>(artifactId, source),
            (_, replacement) => resources[artifactId.Key] = replacement,
            dependencies);
    }

    public IDisposable RegisterControlTemplate(
        FormaXamlArtifactId artifactId,
        TemplatedControl owner,
        IEnumerable<FormaXamlArtifactId>? dependencies = null)
    {
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        return RegisterArtifact(
            artifactId,
            () => owner.Template ?? throw new InvalidOperationException("A live control-template artifact requires an explicit owner template."),
            (_, source, _) => CompileResource<ControlTemplate>(artifactId, source),
            (_, replacement) => owner.Template = replacement,
            dependencies);
    }

    public IDisposable RegisterThemeControlTemplate<TControl>(
        FormaXamlArtifactId artifactId,
        Theme theme,
        IEnumerable<FormaXamlArtifactId>? dependencies = null) where TControl : TemplatedControl
    {
        if (theme == null) throw new ArgumentNullException(nameof(theme));
        return RegisterArtifact(
            artifactId,
            () => theme.GetControlTemplate(typeof(TControl))
                ?? throw new InvalidOperationException($"The theme has no live control template for {typeof(TControl).FullName}."),
            (_, source, _) => CompileResource<ControlTemplate>(artifactId, source),
            (_, replacement) => theme.SetControlTemplate<TControl>(replacement),
            dependencies);
    }

    public IDisposable RegisterDataTemplate(
        FormaXamlArtifactId artifactId,
        ItemsControl owner,
        IEnumerable<FormaXamlArtifactId>? dependencies = null)
    {
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        return RegisterArtifact(
            artifactId,
            () => owner.ItemTemplate ?? throw new InvalidOperationException("A live data-template artifact requires an item template."),
            (_, source, _) => CompileResource<DataTemplate>(artifactId, source),
            (_, replacement) => owner.ItemTemplate = replacement,
            dependencies);
    }

    public IDisposable RegisterDataGridColumnTemplate(
        FormaXamlArtifactId artifactId,
        DataGridColumn column,
        bool headerTemplate = false,
        IEnumerable<FormaXamlArtifactId>? dependencies = null)
    {
        if (column == null) throw new ArgumentNullException(nameof(column));
        return RegisterArtifact(
            artifactId,
            () => (headerTemplate ? column.HeaderTemplate : column.CellTemplate)
                ?? throw new InvalidOperationException("A live data-grid column artifact requires an existing template."),
            (_, source, _) => CompileResource<DataTemplate>(artifactId, source),
            (_, replacement) =>
            {
                if (headerTemplate) column.HeaderTemplate = replacement;
                else column.CellTemplate = replacement;
            },
            dependencies);
    }

    public Task RequestReloadAsync(string source) => RequestReloadAsync(NormalizeSource(source), null, TimeSpan.Zero);

    /// <summary>
    /// Reloads a view authored in the HTML and CSS dialect: converts the changed .fhtml to canonical XAML, rewrites the generated
    /// document the running view was compiled from, and reloads it like any XAML change. Invalid input reports diagnostics at the HTML
    /// location and leaves the live tree untouched.
    /// </summary>
    public async Task RequestHtmlReloadAsync(string source)
    {
        ThrowIfDisposed();
        var normalized = NormalizeSource(source);
        var htmlFile = Path.Combine(_developmentRoot, normalized.Replace('/', Path.DirectorySeparatorChar));
        string html;
        try { html = await File.ReadAllTextAsync(htmlFile).ConfigureAwait(false); }
        catch (IOException) { return; }
        var result = Forma.Xaml.Compiler.Html.FormaHtmlConverter.Convert(html, normalized);
        if (!result.Succeeded)
        {
            DiagnosticsChanged?.Invoke(result.Diagnostics);
            return;
        }

        foreach (var sidecar in Directory.EnumerateFiles(_developmentRoot, "*.fhtmlmap", SearchOption.AllDirectories))
        {
            if (Forma.Xaml.Compiler.Html.FormaHtmlSourceMap.Parse(await File.ReadAllTextAsync(sidecar).ConfigureAwait(false)) is not { } parsed ||
                !string.Equals(NormalizeSource(parsed.HtmlPath), normalized, StringComparison.Ordinal)) continue;
            var xamlPath = sidecar.Substring(0, sidecar.Length - ".fhtmlmap".Length);
            await File.WriteAllTextAsync(sidecar, result.Map.Serialize(parsed.HtmlPath)).ConfigureAwait(false);
            await File.WriteAllTextAsync(xamlPath, result.Xaml).ConfigureAwait(false);
            await RequestReloadAsync(NormalizeSource(Path.GetRelativePath(_developmentRoot, xamlPath))).ConfigureAwait(false);
        }
    }

    public Task RequestReloadAsync(FormaXamlArtifactId artifactId)
    {
        var normalized = NormalizeArtifactId(artifactId);
        return RequestReloadAsync(normalized.Source, normalized, TimeSpan.Zero);
    }

    public Task RequestAssetReloadAsync(string source)
    {
        var normalized = NormalizeSource(source);
        if (!Path.GetExtension(normalized).Equals(".svg", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An SVG development asset path is required.", nameof(source));
        return RequestAllReloadsAsync(TimeSpan.Zero);
    }

    private IDisposable RegisterCore(string source, Type requiredType, Func<Control> current, Action<Control, Control> replace)
    {
        var artifactId = new FormaXamlArtifactId(NormalizeSource(source), "$root", FormaXamlArtifactKind.Root);
        return RegisterArtifactCore(
            artifactId,
            current,
            (_, xaml, expected) =>
            {
                var sourcePath = Path.Combine(_developmentRoot, artifactId.Source.Replace('/', Path.DirectorySeparatorChar));
                var callbacks = FormaXamlCompiler.CreateSre(expected.GetType().Assembly.GetName().Name!).CompileSre(xaml, sourcePath);
                if (callbacks.Build(null) is not Control replacement)
                    throw new InvalidOperationException("Hot-reloaded XAML root is not a Control.");
                if (!requiredType.IsInstanceOfType(replacement))
                    throw new InvalidOperationException($"Reloaded root '{replacement.GetType().FullName}' is not assignable to '{requiredType.FullName}'.");
                return replacement;
            },
            (oldValue, newValue) => replace((Control)oldValue, (Control)newValue),
            Array.Empty<FormaXamlArtifactId>());
    }

    private IDisposable RegisterArtifactCore(
        FormaXamlArtifactId artifactId,
        Func<object> current,
        Func<FormaXamlArtifactPreparationContext, string, object, object> prepare,
        Action<object, object> replace,
        IReadOnlyList<FormaXamlArtifactId> dependencies)
    {
        ThrowIfDisposed();
        var artifact = _artifacts.GetOrAdd(artifactId, id => new ArtifactRegistration(id, dependencies));
        if (!artifact.HasDependencies(dependencies))
            throw new InvalidOperationException($"Hot-reload artifact '{artifactId}' was registered with conflicting dependency edges.");
        var registration = new LiveRegistration(artifact, current, prepare, replace, RemoveRegistration);
        artifact.Add(registration);
        return registration;
    }

    private void FileChanged(object sender, FileSystemEventArgs args)
    {
        var extension = Path.GetExtension(args.FullPath);
        if (extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase))
            _ = RequestReloadAsync(NormalizeSource(Path.GetRelativePath(_developmentRoot, args.FullPath)), null, TimeSpan.FromMilliseconds(150));
        else if (extension.Equals(".fhtml", StringComparison.OrdinalIgnoreCase))
            _ = RequestHtmlReloadAsync(Path.GetRelativePath(_developmentRoot, args.FullPath));
        else if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
            _ = RequestAllReloadsAsync(TimeSpan.FromMilliseconds(150));
    }
    private void FileRenamed(object sender, RenamedEventArgs args) => FileChanged(sender, args);

    private async Task RequestAllReloadsAsync(TimeSpan debounce)
    {
        foreach (var source in _artifacts.Keys.Select(id => id.Source).Distinct(StringComparer.Ordinal))
            await RequestReloadAsync(source, null, debounce).ConfigureAwait(false);
    }

    private async Task RequestReloadAsync(string source, FormaXamlArtifactId? changedArtifact, TimeSpan debounce)
    {
        ThrowIfDisposed();
        var artifacts = GetAffectedArtifacts(source, changedArtifact);
        if (artifacts.Count == 0) return;
        var versions = artifacts.ToDictionary(artifact => artifact, artifact => artifact.BeginReload());
        if (debounce > TimeSpan.Zero) await Task.Delay(debounce).ConfigureAwait(false);
        if (_disposed || versions.Any(entry => entry.Value != entry.Key.Version)) return;
        try
        {
            var sourceTexts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var artifactSource in artifacts.Select(artifact => artifact.Id.Source).Distinct(StringComparer.Ordinal))
            {
                var file = Path.Combine(_developmentRoot, artifactSource.Replace('/', Path.DirectorySeparatorChar));
                sourceTexts.Add(artifactSource, await File.ReadAllTextAsync(file).ConfigureAwait(false));
            }
            var preparedArtifacts = new List<PreparedArtifact>(artifacts.Count);
            var preparedValues = new Dictionary<FormaXamlArtifactId, IReadOnlyList<object>>();
            var preparationContext = new FormaXamlArtifactPreparationContext(preparedValues);
            foreach (var artifact in artifacts)
            {
                var liveRegistrations = artifact.Snapshot();
                var replacements = new List<Replacement>(liveRegistrations.Count);
                foreach (var registration in liveRegistrations)
                {
                    var expected = registration.Current();
                    replacements.Add(new Replacement(registration, expected, registration.Prepare(preparationContext, sourceTexts[artifact.Id.Source], expected)));
                }
                preparedArtifacts.Add(new PreparedArtifact(artifact, versions[artifact], replacements));
                preparedValues.Add(artifact.Id, replacements.Select(replacement => replacement.Value).ToArray());
            }
            if (versions.Any(entry => entry.Value != entry.Key.Version)) return;
            _replacements.Enqueue(new ReplacementSet(preparedArtifacts));
            DiagnosticsChanged?.Invoke(Array.Empty<FormaDiagnostic>());
        }
        catch (FormaXamlCompilationException exception)
        {
            DiagnosticsChanged?.Invoke(exception.Diagnostics);
        }
        catch (Exception exception)
        {
            DiagnosticsChanged?.Invoke(new[] { new FormaDiagnostic(FormaDiagnosticCodes.Emission, FormaDiagnosticSeverity.Error, exception.Message, new FormaSourceLocation(source, 1, 1)) });
        }
    }

    private void DrainReplacements()
    {
        while (_replacements.TryDequeue(out var replacementSet))
        {
            if (replacementSet.Artifacts.Any(prepared =>
                    prepared.Version != prepared.Artifact.Version ||
                    prepared.Replacements.Any(replacement => replacement.Registration.Disposed ||
                        !ReferenceEquals(replacement.Registration.Current(), replacement.Expected))))
                continue;
            foreach (var prepared in replacementSet.Artifacts)
            {
                foreach (var replacement in prepared.Replacements)
                {
                    if (replacement.Expected is Control oldControl && oldControl.HasLocalDataContext && replacement.Value is Control newControl)
                        newControl.DataContext = oldControl.DataContext;
                    try { replacement.Registration.Replace(replacement.Expected, replacement.Value); }
                    catch (Exception exception)
                    {
                        DiagnosticsChanged?.Invoke(new[] { new FormaDiagnostic(FormaDiagnosticCodes.Emission, FormaDiagnosticSeverity.Error, exception.Message, new FormaSourceLocation(prepared.Artifact.Id.Source, 1, 1)) });
                    }
                }
            }
        }
    }

    private IReadOnlyList<ArtifactRegistration> GetAffectedArtifacts(string source, FormaXamlArtifactId? changedArtifact)
    {
        var all = _artifacts.Values.ToArray();
        var affectedIds = new HashSet<FormaXamlArtifactId>();
        var affected = new List<ArtifactRegistration>();
        foreach (var artifact in all)
        {
            if (changedArtifact.HasValue ? artifact.Id != changedArtifact.Value : artifact.Id.Source != source) continue;
            affectedIds.Add(artifact.Id);
            affected.Add(artifact);
        }
        var added = true;
        while (added)
        {
            added = false;
            foreach (var artifact in all)
            {
                if (affectedIds.Contains(artifact.Id) ||
                    !artifact.Dependencies.Any(dependency =>
                        affectedIds.Contains(dependency) || (!changedArtifact.HasValue && dependency.Source == source)))
                    continue;
                affectedIds.Add(artifact.Id);
                affected.Add(artifact);
                added = true;
            }
        }
        var ordered = new List<ArtifactRegistration>(affected.Count);
        var remaining = new HashSet<ArtifactRegistration>(affected);
        while (remaining.Count != 0)
        {
            var ready = remaining
                .Where(artifact => artifact.Dependencies.All(dependency =>
                    !affectedIds.Contains(dependency) || ordered.Any(candidate => candidate.Id == dependency)))
                .OrderBy(artifact => artifact.Id.Source, StringComparer.Ordinal)
                .ThenBy(artifact => artifact.Id.SemanticNodeId, StringComparer.Ordinal)
                .ThenBy(artifact => artifact.Id.Kind)
                .ThenBy(artifact => artifact.Id.Key, StringComparer.Ordinal)
                .ToArray();
            if (ready.Length == 0)
                throw new InvalidOperationException($"Hot-reload artifact dependencies affected by '{source}' contain a cycle.");
            foreach (var artifact in ready)
            {
                remaining.Remove(artifact);
                ordered.Add(artifact);
            }
        }
        return ordered;
    }

    private T CompileResource<T>(FormaXamlArtifactId artifactId, string source) where T : class
    {
        var sourcePath = Path.Combine(_developmentRoot, artifactId.Source.Replace('/', Path.DirectorySeparatorChar));
        var built = FormaXamlCompiler.CreateSre(typeof(T).Assembly.GetName().Name!).CompileSre(source, sourcePath).Build(null);
        if (built is T direct) return direct;
        if (built is ResourceDictionary resources && resources.TryFind(artifactId.Key, out var value) && value is T typed)
            return typed;
        if (built is Control control && control.Resources.TryFind(artifactId.Key, out value) && value is T controlResource)
            return controlResource;
        throw new InvalidOperationException($"Hot-reloaded XAML did not produce {typeof(T).FullName} artifact '{artifactId.Key}'.");
    }

    private string NormalizeSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("A project-relative XAML source path is required.", nameof(source));
        var normalized = source.Replace('\\', '/').TrimStart('/');
        if (Path.IsPathRooted(source) || normalized.Split('/').Any(segment => segment == "..")) throw new ArgumentException("Hot-reload source paths must be project-relative.", nameof(source));
        return normalized;
    }

    private FormaXamlArtifactId NormalizeArtifactId(FormaXamlArtifactId artifactId)
    {
        if (string.IsNullOrWhiteSpace(artifactId.SemanticNodeId))
            throw new ArgumentException("A hot-reload artifact requires a stable semantic node ID.", nameof(artifactId));
        return artifactId with
        {
            Source = NormalizeSource(artifactId.Source),
            SemanticNodeId = artifactId.SemanticNodeId.Trim(),
            Key = artifactId.Key?.Trim() ?? "",
        };
    }

    private IReadOnlyList<FormaXamlArtifactId> NormalizeDependencies(
        FormaXamlArtifactId artifactId,
        IEnumerable<FormaXamlArtifactId>? dependencies)
    {
        var normalized = (dependencies ?? Array.Empty<FormaXamlArtifactId>())
            .Select(NormalizeArtifactId)
            .Distinct()
            .ToArray();
        if (normalized.Contains(artifactId))
            throw new ArgumentException("A hot-reload artifact cannot depend on itself.", nameof(dependencies));
        return normalized;
    }

    private void RemoveRegistration(LiveRegistration registration)
    {
        var artifact = registration.Artifact;
        artifact.Remove(registration);
        if (artifact.IsEmpty)
            _artifacts.TryRemove(new KeyValuePair<FormaXamlArtifactId, ArtifactRegistration>(artifact.Id, artifact));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FormaXamlHotReloadService));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }
        _frameCallback.Dispose();
        foreach (var artifact in _artifacts.Values) artifact.DisposeAll();
        _artifacts.Clear();
        while (_replacements.TryDequeue(out _)) { }
    }

    private sealed class ArtifactRegistration
    {
        private readonly object _gate = new();
        private readonly List<LiveRegistration> _registrations = [];

        public ArtifactRegistration(FormaXamlArtifactId id, IReadOnlyList<FormaXamlArtifactId> dependencies)
        {
            Id = id;
            Dependencies = dependencies.ToArray();
        }
        public FormaXamlArtifactId Id { get; }
        public IReadOnlyList<FormaXamlArtifactId> Dependencies { get; }
        public long Version => Volatile.Read(ref _version);
        public bool IsEmpty { get { lock (_gate) return _registrations.Count == 0; } }
        private long _version;

        public void Add(LiveRegistration registration)
        {
            lock (_gate) _registrations.Add(registration);
            Interlocked.Increment(ref _version);
        }

        public void Remove(LiveRegistration registration)
        {
            lock (_gate) _registrations.Remove(registration);
            Interlocked.Increment(ref _version);
        }

        public long BeginReload() => Interlocked.Increment(ref _version);
        public IReadOnlyList<LiveRegistration> Snapshot() { lock (_gate) return _registrations.ToArray(); }
        public bool HasDependencies(IReadOnlyList<FormaXamlArtifactId> dependencies) =>
            Dependencies.Count == dependencies.Count && Dependencies.All(dependencies.Contains);

        public void DisposeAll()
        {
            LiveRegistration[] registrations;
            lock (_gate)
            {
                registrations = _registrations.ToArray();
                _registrations.Clear();
            }
            Interlocked.Increment(ref _version);
            foreach (var registration in registrations) registration.DisposeFromOwner();
        }
    }

    private sealed class LiveRegistration : IDisposable
    {
        private readonly Action<LiveRegistration> _dispose;
        public LiveRegistration(
            ArtifactRegistration artifact,
            Func<object> current,
            Func<FormaXamlArtifactPreparationContext, string, object, object> prepare,
            Action<object, object> replace,
            Action<LiveRegistration> dispose)
        { Artifact = artifact; Current = current; Prepare = prepare; Replace = replace; _dispose = dispose; }
        public ArtifactRegistration Artifact { get; }
        public Func<object> Current { get; }
        public Func<FormaXamlArtifactPreparationContext, string, object, object> Prepare { get; }
        public Action<object, object> Replace { get; }
        public bool Disposed { get; private set; }
        public void Dispose() { if (Disposed) return; Disposed = true; _dispose(this); }
        public void DisposeFromOwner() => Disposed = true;
    }

    private sealed record Replacement(LiveRegistration Registration, object Expected, object Value);
    private sealed record PreparedArtifact(ArtifactRegistration Artifact, long Version, IReadOnlyList<Replacement> Replacements);
    private sealed record ReplacementSet(IReadOnlyList<PreparedArtifact> Artifacts);
}