// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Threading;
using Forma.Xaml;

namespace Forma
{
    public delegate Control FrameworkTemplateFactory(TemplateBuildContext context);
    public delegate Control ControlTemplateFactory<in TControl>(TemplateBuildContext context, TControl owner)
        where TControl : TemplatedControl;
    public delegate Control DataTemplateFactory<in TData>(TemplateBuildContext context, TData item);
    public delegate Container ItemsPanelTemplateFactory(TemplateBuildContext context);

    public enum TemplateAttachmentKind
    {
        Binding,
        Style,
        Resource,
        Trigger,
        Transition,
        Clock,
        Other,
    }

    public enum TemplateInstanceState
    {
        Inactive,
        Active,
        Disposed,
    }

    /// <summary>
    /// Opts an application-defined data-template root into pooling. Implementations clear row-local validation,
    /// local values, and code-behind state while inactive, then prepare that state for the rebound item.
    /// </summary>
    public interface IDataTemplateRecyclingState
    {
        void OnRecycling();
        void OnReused(object item);
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class TemplatePartAttribute : Attribute
    {
        public TemplatePartAttribute(string name, Type partType, bool isRequired = true)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A template part name is required.", nameof(name));
            Name = name;
            PartType = partType ?? throw new ArgumentNullException(nameof(partType));
            IsRequired = isRequired;
        }

        public string Name { get; }
        public Type PartType { get; }
        public bool IsRequired { get; }
    }

    public sealed class TemplatePartMetadata
    {
        public TemplatePartMetadata(string name, Type partType, bool isRequired = true)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A template part name is required.", nameof(name));
            Name = name;
            PartType = partType ?? throw new ArgumentNullException(nameof(partType));
            IsRequired = isRequired;
        }

        public string Name { get; }
        public Type PartType { get; }
        public bool IsRequired { get; }
    }

    public abstract class FrameworkTemplate
    {
        private static long _nextFactoryVersion;
        [ThreadStatic]
        private static List<ControlTemplate> _activeControlBuilds;
        private static readonly object CreatedRootMarker = new object();
        private readonly ConditionalWeakTable<Control, object> _createdRoots = new ConditionalWeakTable<Control, object>();

        protected FrameworkTemplate() => FactoryVersion = Interlocked.Increment(ref _nextFactoryVersion);

        public object FactoryIdentity => this;
        public long FactoryVersion { get; }

        internal TemplateInstance Build(Control host, TemplatedControl templatedParent, object item)
        {
            ValidateBuildContext(templatedParent, item);
            using var buildLease = EnterBuild(this, templatedParent);
            var context = new TemplateBuildContext(this, host, templatedParent, item);
            Control root = null;
            var previousBuildContext = TemplateBuildContext.SetCurrentBuild(context);
            try
            {
                root = BuildRoot(context) ?? throw new InvalidOperationException($"A {GetType().Name} factory returned no root.");
                if (_createdRoots.TryGetValue(root, out _))
                    throw new InvalidOperationException($"A {GetType().Name} must create a fresh root for every instance.");
                _createdRoots.Add(root, CreatedRootMarker);
                if (ReferenceEquals(root, host)) throw new InvalidOperationException("A control cannot be the root of a template it hosts.");
                if (root.Parent != null || root.VisualParent != null)
                    throw new InvalidOperationException("A template factory must return an unattached root.");
                ValidateRoot(root, templatedParent);
                return context.Complete(root);
            }
            catch (Exception exception)
            {
                var failure = ExceptionDispatchInfo.Capture(exception);
                try { context.DisposeFailedBuild(root); }
                catch { }
                try { XamlAttachment.DisposeScope(root); }
                catch { }
                if (root != null && !ReferenceEquals(root, host) && root.Parent == null && root.VisualParent == null && root is IDisposable disposable)
                {
                    try { disposable.Dispose(); }
                    catch { }
                }
                failure.Throw();
                throw;
            }
            finally
            {
                TemplateBuildContext.SetCurrentBuild(previousBuildContext);
            }
        }

        private static IDisposable EnterBuild(FrameworkTemplate template, TemplatedControl templatedParent)
        {
            if (template is not ControlTemplate controlTemplate) return null;
            _activeControlBuilds ??= new List<ControlTemplate>();
            for (var ancestor = templatedParent.VisualParent; ancestor != null; ancestor = ancestor.VisualParent)
            {
                if (ancestor is TemplatedControl templatedAncestor &&
                    ReferenceEquals(templatedAncestor.AppliedTemplate, controlTemplate) &&
                    !IsLogicalDescendantOf(templatedParent, templatedAncestor))
                    throw new InvalidOperationException($"Recursive ControlTemplate application would reuse {controlTemplate.TargetType.FullName} below an ancestor using the same template.");
            }
            if (_activeControlBuilds.Contains(controlTemplate))
            {
                var path = string.Join(" -> ", _activeControlBuilds.Select(active => active.TargetType.FullName).Append(controlTemplate.TargetType.FullName));
                throw new InvalidOperationException($"Recursive ControlTemplate application was detected: {path}.");
            }
            _activeControlBuilds.Add(controlTemplate);
            return new TemplateBuildLease();
        }

        private static bool IsLogicalDescendantOf(Control control, Control ancestor)
        {
            for (var current = control.Parent; current != null; current = current.Parent)
                if (ReferenceEquals(current, ancestor)) return true;
            return false;
        }

        protected static void ValidateControlTemplateGraph(Control root, TemplatedControl templatedParent)
        {
            var forbiddenTemplates = new HashSet<ControlTemplate>(_activeControlBuilds ?? Enumerable.Empty<ControlTemplate>());
            for (var ancestor = templatedParent.VisualParent; ancestor != null; ancestor = ancestor.VisualParent)
            {
                if (ancestor is TemplatedControl templatedAncestor && templatedAncestor.AppliedTemplate != null)
                    forbiddenTemplates.Add(templatedAncestor.AppliedTemplate);
            }
            ValidateControlTemplateGraph(root, root, forbiddenTemplates, templatedParent.GetType());
        }

        private static void ValidateControlTemplateGraph(
            Control control,
            Control root,
            HashSet<ControlTemplate> forbiddenTemplates,
            Type ownerType)
        {
            if (control is TemplatedControl templatedControl)
            {
                var nestedTemplate = templatedControl.ResolveTemplate();
                if (forbiddenTemplates.Contains(nestedTemplate) || (nestedTemplate == null && control.GetType() == ownerType))
                    throw new InvalidOperationException($"Recursive ControlTemplate application would introduce {control.GetType().FullName} into its own visual template graph.");
                return;
            }
            foreach (var child in control.VisualChildren)
            {
                if (!IsFactoryOwned(child, root)) continue;
                ValidateControlTemplateGraph(child, root, forbiddenTemplates, ownerType);
            }
        }

        internal static IReadOnlyList<IDisposable> CollectOwnedControls(Control root)
        {
            if (root == null) return Array.Empty<IDisposable>();
            var ownedControls = new List<IDisposable>();
            if (root is not TemplatedControl) CollectOwnedControls(root, root, ownedControls);
            return ownedControls;
        }

        internal static bool IsFactoryOwned(Control control, Control root)
        {
            if (control.Parent == null) return true;
            for (var parent = control.Parent; parent != null; parent = parent.Parent)
                if (ReferenceEquals(parent, root)) return true;
            return false;
        }

        private static void CollectOwnedControls(Control control, Control root, List<IDisposable> ownedControls)
        {
            foreach (var child in control.VisualChildren)
            {
                if (!IsFactoryOwned(child, root)) continue;
                if (child is TemplatedControl templatedControl)
                {
                    ownedControls.Add(templatedControl);
                    continue;
                }
                CollectOwnedControls(child, root, ownedControls);
            }
        }

        private sealed class TemplateBuildLease : IDisposable
        {
            public void Dispose() => _activeControlBuilds.RemoveAt(_activeControlBuilds.Count - 1);
        }

        protected abstract Control BuildRoot(TemplateBuildContext context);
        internal virtual void ValidateBuildContext(TemplatedControl templatedParent, object item) { }
        internal virtual void ValidateRoot(Control root, TemplatedControl templatedParent) { }
        internal virtual void ValidateNameScope(NameScope nameScope, TemplatedControl templatedParent) { }
    }

    public sealed class TemplateBuildContext
    {
        [ThreadStatic]
        private static TemplateBuildContext _currentBuild;
        private readonly List<TemplateAttachment> _attachments = new List<TemplateAttachment>();
        private readonly List<TemplateLifecycleRegistration> _lifecycle = new List<TemplateLifecycleRegistration>();
        private readonly List<Action<object>> _rebind = new List<Action<object>>();
        private readonly HashSet<Control> _xamlRoots = new HashSet<Control>();
        private bool _completed;

        internal TemplateBuildContext(FrameworkTemplate template, Control host, TemplatedControl templatedParent, object item)
        {
            Template = template;
            Host = host;
            TemplatedParent = templatedParent;
            Item = item;
            NameScope = new NameScope();
        }

        public FrameworkTemplate Template { get; }
        public Control Host { get; }
        public TemplatedControl TemplatedParent { get; }
        public object Item { get; private set; }
        public NameScope NameScope { get; }

        public TItem GetItem<TItem>()
        {
            if (Item is TItem item) return item;
            if (Item == null && default(TItem) == null) return default;
            throw new InvalidOperationException($"The current template item is not assignable to {typeof(TItem).FullName}.");
        }

        public void RegisterAttachment(IDisposable attachment, TemplateAttachmentKind kind = TemplateAttachmentKind.Other)
        {
            ThrowIfCompleted();
            if (attachment == null) throw new ArgumentNullException(nameof(attachment));
            _attachments.Add(new TemplateAttachment(kind, attachment));
        }

        public void RegisterLifecycle(Action activate, Action deactivate)
        {
            ThrowIfCompleted();
            if (activate == null) throw new ArgumentNullException(nameof(activate));
            if (deactivate == null) throw new ArgumentNullException(nameof(deactivate));
            _lifecycle.Add(new TemplateLifecycleRegistration(activate, deactivate));
        }

        public void RegisterRebind<TItem>(Action<TItem> rebind)
        {
            ThrowIfCompleted();
            if (rebind == null) throw new ArgumentNullException(nameof(rebind));
            _rebind.Add(value =>
            {
                if (value is TItem item) rebind(item);
                else if (value == null && default(TItem) == null) rebind(default);
                else throw new InvalidOperationException($"The rebound item is not assignable to {typeof(TItem).FullName}.");
            });
        }

        public void BindItem<TItem>(Control root, TItem item)
        {
            ThrowIfCompleted();
            if (root == null) throw new ArgumentNullException(nameof(root));
            root.DataContext = item;
            RegisterRebind<TItem>(value => root.DataContext = value);
        }

        internal TemplateInstance Complete(Control root)
        {
            ThrowIfCompleted();
            RegisterNames(root, NameScope);
            Template.ValidateNameScope(NameScope, TemplatedParent);
            NameScope.SetNameScope(root, NameScope);
            if (TemplatedParent != null) CompiledBindingSource.SetTemplatedParent(root, TemplatedParent);
            StyleBoundary.Set(root, Template, TemplatedParent);
            var attachmentScope = XamlAttachment.PromoteTemplateScope(root);
            _attachments.Insert(0, new TemplateAttachment(TemplateAttachmentKind.Other, attachmentScope));
            _lifecycle.Insert(0, new TemplateLifecycleRegistration(attachmentScope.Activate, attachmentScope.Deactivate));
            _completed = true;
            var instance = new TemplateInstance(
                this,
                root,
                _attachments.ToArray(),
                _lifecycle.ToArray(),
                _rebind.ToArray(),
                FrameworkTemplate.CollectOwnedControls(root).ToArray());
            attachmentScope.SetOwnerDispose(TemplatedParent != null ? TemplatedParent.Dispose : instance.Dispose);
            return instance;
        }

        internal void SetItem(object item) => Item = item;

        internal static TemplateBuildContext SetCurrentBuild(TemplateBuildContext context)
        {
            var previous = _currentBuild;
            _currentBuild = context;
            return previous;
        }

        internal static void TrackXamlRoot(Control root) => _currentBuild?._xamlRoots.Add(root);

        internal void DisposeFailedBuild(Control returnedRoot = null)
        {
            if (_completed) return;
            _completed = true;
            ExceptionDispatchInfo failure = null;
            var ownedControls = new HashSet<IDisposable>();
            foreach (var ownedControl in FrameworkTemplate.CollectOwnedControls(returnedRoot)) ownedControls.Add(ownedControl);
            foreach (var root in _xamlRoots)
                foreach (var ownedControl in FrameworkTemplate.CollectOwnedControls(root)) ownedControls.Add(ownedControl);
            for (var index = _attachments.Count - 1; index >= 0; index--)
            {
                try { _attachments[index].Attachment.Dispose(); }
                catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            }
            foreach (var ownedControl in ownedControls.Reverse())
            {
                try { ownedControl.Dispose(); }
                catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            }
            foreach (var root in _xamlRoots)
            {
                try { XamlAttachment.DisposeScope(root); }
                catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
                if (!ReferenceEquals(root, returnedRoot) && !ReferenceEquals(root, Host) && root.Parent == null && root.VisualParent == null && root is IDisposable disposable)
                {
                    try { disposable.Dispose(); }
                    catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
                }
            }
            _xamlRoots.Clear();
            failure?.Throw();
        }

        private void ThrowIfCompleted()
        {
            if (_completed) throw new InvalidOperationException("The template build context has already completed.");
        }

        private static void RegisterNames(Control control, NameScope scope)
        {
            RegisterNames(control, control, scope);
        }

        private static void RegisterNames(Control control, Control root, NameScope scope)
        {
            if (!string.IsNullOrEmpty(control.Name))
            {
                var existing = scope.Find(control.Name);
                if (existing == null) scope.Register(control.Name, control);
                else if (!ReferenceEquals(existing, control))
                    throw new InvalidOperationException($"Name '{control.Name}' is already registered in this template namescope.");
            }
            if (control is TemplatedControl) return;
            foreach (var child in control.VisualChildren)
            {
                if (!FrameworkTemplate.IsFactoryOwned(child, root)) continue;
                RegisterNames(child, root, scope);
            }
        }
    }

    /// <summary>A typed factory for one semantic control's visual root.</summary>
    public sealed class ControlTemplate : FrameworkTemplate
    {
        private readonly Func<TemplateBuildContext, TemplatedControl, Control> _factory;
        private readonly ReadOnlyCollection<TemplatePartMetadata> _parts;

        public ControlTemplate(Type targetType, Func<TemplatedControl, Control> factory)
            : this(targetType, (context, owner) => factory(owner), null)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
        }

        public ControlTemplate(
            Type targetType,
            Func<TemplateBuildContext, TemplatedControl, Control> factory,
            IEnumerable<TemplatePartMetadata> parts = null)
        {
            TargetType = targetType ?? throw new ArgumentNullException(nameof(targetType));
            if (!typeof(TemplatedControl).IsAssignableFrom(targetType))
                throw new ArgumentException("A control template target must derive from TemplatedControl.", nameof(targetType));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _parts = new ReadOnlyCollection<TemplatePartMetadata>(ValidateParts(parts));
        }

        public Type TargetType { get; }
        public IReadOnlyList<TemplatePartMetadata> Parts => _parts;

        public static ControlTemplate Create<TControl>(
            ControlTemplateFactory<TControl> factory,
            IEnumerable<TemplatePartMetadata> parts = null)
            where TControl : TemplatedControl
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            return new ControlTemplate(typeof(TControl), (context, owner) => factory(context, (TControl)owner), parts);
        }

        internal TemplateInstance Build(TemplatedControl owner) => Build(owner, owner, null);

        protected override Control BuildRoot(TemplateBuildContext context) => _factory(context, context.TemplatedParent);

        internal override void ValidateBuildContext(TemplatedControl templatedParent, object item)
        {
            if (templatedParent == null) throw new ArgumentNullException(nameof(templatedParent));
            if (!TargetType.IsInstanceOfType(templatedParent))
                throw new InvalidOperationException($"A template targeting {TargetType.FullName} cannot be applied to {templatedParent.GetType().FullName}.");
        }

        internal override void ValidateRoot(Control root, TemplatedControl templatedParent) =>
            ValidateControlTemplateGraph(root, templatedParent);

        internal override void ValidateNameScope(NameScope nameScope, TemplatedControl templatedParent)
        {
            var contracts = GetEffectivePartContracts(templatedParent.GetType());
            foreach (var metadata in _parts)
            {
                if (contracts.TryGetValue(metadata.Name, out var contract) &&
                    (contract.PartType != metadata.PartType || contract.IsRequired != metadata.IsRequired))
                    throw new InvalidOperationException(
                        $"ControlTemplate metadata for {TargetType.FullName} conflicts with the {templatedParent.GetType().FullName} contract for part '{metadata.Name}': " +
                        $"owner expects {contract.PartType.FullName} (required={contract.IsRequired}), template declares {metadata.PartType.FullName} (required={metadata.IsRequired}).");
                contracts[metadata.Name] = metadata;
            }
            foreach (var part in contracts.Values)
            {
                var control = nameScope.Find(part.Name);
                if (control == null)
                {
                    if (part.IsRequired)
                        throw CreatePartException(templatedParent, part, null);
                    continue;
                }
                if (!part.PartType.IsInstanceOfType(control))
                    throw CreatePartException(templatedParent, part, control.GetType());
            }
        }

        private static Dictionary<string, TemplatePartMetadata> GetEffectivePartContracts(Type ownerType)
        {
            var contracts = new Dictionary<string, TemplatePartMetadata>(StringComparer.Ordinal);
            foreach (TemplatePartAttribute attribute in Attribute.GetCustomAttributes(ownerType, typeof(TemplatePartAttribute), true))
            {
                var contract = new TemplatePartMetadata(attribute.Name, attribute.PartType, attribute.IsRequired);
                if (contracts.TryGetValue(contract.Name, out var existing) &&
                    (existing.PartType != contract.PartType || existing.IsRequired != contract.IsRequired))
                    throw new InvalidOperationException($"{ownerType.FullName} inherits conflicting contracts for template part '{contract.Name}'.");
                contracts[contract.Name] = contract;
            }
            return contracts;
        }

        private InvalidOperationException CreatePartException(TemplatedControl owner, TemplatePartMetadata part, Type actualType)
        {
            var actual = actualType == null ? "missing" : actualType.FullName;
            return new InvalidOperationException(
                $"Applying {GetType().FullName} targeting {TargetType.FullName} (factory version {FactoryVersion}) to {owner.GetType().FullName} failed: " +
                $"template part '{part.Name}' expected {part.PartType.FullName}, actual {actual}.");
        }

        private static List<TemplatePartMetadata> ValidateParts(IEnumerable<TemplatePartMetadata> parts)
        {
            var result = new List<TemplatePartMetadata>();
            if (parts == null) return result;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var part in parts)
            {
                if (part == null) throw new ArgumentException("Template part metadata cannot contain null entries.", nameof(parts));
                if (!names.Add(part.Name)) throw new ArgumentException($"Template part '{part.Name}' is declared more than once.", nameof(parts));
                result.Add(part);
            }
            return result;
        }
    }

    public sealed class DataTemplate : FrameworkTemplate
    {
        private readonly Func<TemplateBuildContext, object, Control> _factory;

        public DataTemplate(Type dataType, Func<TemplateBuildContext, object, Control> factory)
        {
            DataType = dataType ?? throw new ArgumentNullException(nameof(dataType));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public Type DataType { get; }

        /// <summary>
        /// When true, the content this template builds is its own style boundary: styles attached outside it do not reach it.
        /// By default a data template is transparent to styles, so rules attached above an items control apply to its rows.
        /// </summary>
        public bool IsolateStyles { get; set; }

        public static DataTemplate Create<TData>(DataTemplateFactory<TData> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            return new DataTemplate(typeof(TData), (context, item) => factory(context, (TData)item));
        }

        public TemplateInstance CreateInstance(object item, Control host = null) => Build(host, null, item);

        protected override Control BuildRoot(TemplateBuildContext context) => _factory(context, context.Item);

        internal override void ValidateBuildContext(TemplatedControl templatedParent, object item)
        {
            if (item == null)
            {
                if (DataType.IsValueType && Nullable.GetUnderlyingType(DataType) == null)
                    throw new InvalidOperationException($"A null item cannot be applied to a data template for {DataType.FullName}.");
                return;
            }
            if (!DataType.IsInstanceOfType(item))
                throw new InvalidOperationException($"An item of type {item.GetType().FullName} cannot be applied to a data template for {DataType.FullName}.");
        }
    }

    public sealed class ItemsPanelTemplate : FrameworkTemplate
    {
        private readonly ItemsPanelTemplateFactory _factory;

        public ItemsPanelTemplate(ItemsPanelTemplateFactory factory) =>
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));

        public TemplateInstance CreateInstance(Control host = null) => Build(host, null, null);

        protected override Control BuildRoot(TemplateBuildContext context)
            => _factory(context) ?? throw new InvalidOperationException("An items-panel template factory returned no panel.");
    }

    /// <summary>Owns one applied template root and its local namescope.</summary>
    public sealed class TemplateInstance : IDisposable
    {
        private readonly TemplateBuildContext _context;
        private readonly TemplateAttachment[] _attachments;
        private readonly TemplateLifecycleRegistration[] _lifecycle;
        private readonly Action<object>[] _rebind;
        private readonly IDisposable[] _ownedControls;
        private TemplateInstanceState _state;

        internal TemplateInstance(
            TemplateBuildContext context,
            Control root,
            TemplateAttachment[] attachments,
            TemplateLifecycleRegistration[] lifecycle,
            Action<object>[] rebind,
            IDisposable[] ownedControls)
        {
            _context = context;
            _attachments = attachments;
            _lifecycle = lifecycle;
            _rebind = rebind;
            _ownedControls = ownedControls;
            Root = root;
        }

        public FrameworkTemplate Template => _context.Template;
        public Control Host => _context.Host;
        public TemplatedControl Owner => _context.TemplatedParent;
        public TemplatedControl TemplatedParent => _context.TemplatedParent;
        public object Item => _context.Item;
        public Control Root { get; }
        public NameScope NameScope => _context.NameScope;
        public TemplateInstanceState State => _state;
        public bool IsActive => _state == TemplateInstanceState.Active;
        public bool IsDisposed => _state == TemplateInstanceState.Disposed;

        public void Activate()
        {
            ThrowIfDisposed();
            if (_state == TemplateInstanceState.Active) return;
            ExceptionDispatchInfo activationFailure = null;
            try
            {
                foreach (var registration in _lifecycle) registration.ActivateIfNeeded();
                _state = TemplateInstanceState.Active;
            }
            catch (Exception exception)
            {
                activationFailure = ExceptionDispatchInfo.Capture(exception);
            }
            if (activationFailure == null) return;
            DeactivateRegistrations();
            activationFailure.Throw();
        }

        public void Deactivate()
        {
            ThrowIfDisposed();
            if (_state != TemplateInstanceState.Active) return;
            _state = TemplateInstanceState.Inactive;
            var failure = DeactivateRegistrations();
            failure?.Throw();
        }

        public void Rebind(object item)
        {
            ThrowIfDisposed();
            if (Template is not DataTemplate dataTemplate)
                throw new InvalidOperationException("Only data-template instances can be rebound to an item.");
            if (_state == TemplateInstanceState.Active)
                throw new InvalidOperationException("A data-template instance must be deactivated before it can be rebound.");
            dataTemplate.ValidateBuildContext(null, item);
            try
            {
                foreach (var callback in _rebind) callback(item);
                _context.SetItem(item);
            }
            catch (Exception exception)
            {
                var failure = ExceptionDispatchInfo.Capture(exception);
                try { Dispose(); }
                catch { }
                failure.Throw();
            }
        }

        public void Dispose()
        {
            if (_state == TemplateInstanceState.Disposed) return;
            ExceptionDispatchInfo failure = null;
            if (_state == TemplateInstanceState.Active)
            {
                _state = TemplateInstanceState.Inactive;
                failure = DeactivateRegistrations();
            }
            _state = TemplateInstanceState.Disposed;
            try { Root.RemoveFromParent(); }
            catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            try { NameScope.SetNameScope(Root, null); }
            catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            try { CompiledBindingSource.ClearTemplatedParent(Root); }
            catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            try { StyleBoundary.Clear(Root); }
            catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            for (var index = _attachments.Length - 1; index >= 0; index--)
            {
                try { _attachments[index].Attachment.Dispose(); }
                catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            }
            for (var index = _ownedControls.Length - 1; index >= 0; index--)
            {
                try { _ownedControls[index].Dispose(); }
                catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            }
            if (Root is IDisposable disposable)
            {
                try { disposable.Dispose(); }
                catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            }
            failure?.Throw();
        }

        private ExceptionDispatchInfo DeactivateRegistrations()
        {
            ExceptionDispatchInfo failure = null;
            for (var index = _lifecycle.Length - 1; index >= 0; index--)
            {
                try { _lifecycle[index].DeactivateIfNeeded(); }
                catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            }
            return failure;
        }

        private void ThrowIfDisposed()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(TemplateInstance));
        }
    }

    public sealed class DefaultControlTemplateRegistry
    {
        private readonly Dictionary<Type, ControlTemplate> _templates = new Dictionary<Type, ControlTemplate>();

        public static DefaultControlTemplateRegistry Shared { get; } = new DefaultControlTemplateRegistry();

        public void Register(ControlTemplate template)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (_templates.ContainsKey(template.TargetType))
                throw new InvalidOperationException($"A default template is already registered for {template.TargetType.FullName}.");
            _templates.Add(template.TargetType, template);
        }

        public void Register<TControl>(
            ControlTemplateFactory<TControl> factory,
            IEnumerable<TemplatePartMetadata> parts = null)
            where TControl : TemplatedControl =>
            Register(ControlTemplate.Create(factory, parts));

        public bool Unregister(Type controlType)
        {
            if (controlType == null) throw new ArgumentNullException(nameof(controlType));
            return _templates.Remove(controlType);
        }

        public bool TryGetTemplate(Type controlType, out ControlTemplate template)
        {
            if (controlType == null) throw new ArgumentNullException(nameof(controlType));
            if (!typeof(TemplatedControl).IsAssignableFrom(controlType))
                throw new ArgumentException("Default templates can only be resolved for TemplatedControl types.", nameof(controlType));
            if (ReferenceEquals(this, Shared)) DefaultControlTemplates.EnsureRegistered(this);
            for (var current = controlType; current != null && typeof(TemplatedControl).IsAssignableFrom(current); current = current.BaseType)
                if (_templates.TryGetValue(current, out template)) return true;
            template = null;
            return false;
        }

        public ControlTemplate GetTemplate(Type controlType) =>
            TryGetTemplate(controlType, out var template) ? template : null;
    }

    internal readonly struct TemplateAttachment
    {
        public TemplateAttachment(TemplateAttachmentKind kind, IDisposable attachment)
        {
            Kind = kind;
            Attachment = attachment;
        }

        public TemplateAttachmentKind Kind { get; }
        public IDisposable Attachment { get; }
    }

    internal sealed class TemplateLifecycleRegistration
    {
        private readonly Action _activate;
        private readonly Action _deactivate;

        public TemplateLifecycleRegistration(Action activate, Action deactivate)
        {
            _activate = activate;
            _deactivate = deactivate;
        }

        public bool IsActive { get; private set; }

        public void ActivateIfNeeded()
        {
            if (IsActive) return;
            IsActive = true;
            _activate();
        }

        public void DeactivateIfNeeded()
        {
            if (!IsActive) return;
            IsActive = false;
            _deactivate();
        }
    }

    /// <summary>A semantic control whose replaceable visual root is supplied by a control template.</summary>
    public class TemplatedControl : Control, IDisposable
    {
        private ControlTemplate _template;
        private TemplateInstance _templateInstance;
        private bool _templateInvalid = true;
        private bool _applyTemplateOnArrange = true;
        private bool _disposed;

        public ControlTemplate Template
        {
            get => _template;
            set
            {
                ThrowIfDisposed();
                if (ReferenceEquals(_template, value)) return;
                var previous = _template;
                _template = value;
                InvalidateTemplate();
                try { ApplyTemplate(); }
                catch
                {
                    if (_templateInvalid)
                    {
                        _template = previous;
                        _templateInvalid = false;
                    }
                    throw;
                }
            }
        }

        public Control TemplateRoot => _templateInstance?.Root;
        internal ControlTemplate AppliedTemplate => _templateInstance?.Template as ControlTemplate;

        internal ControlTemplate ResolveTemplate() =>
            _template ?? ResolveThemeControlTemplate() ?? DefaultControlTemplateRegistry.Shared.GetTemplate(GetType());

        public bool ApplyTemplate()
        {
            ThrowIfDisposed();
            if (!_templateInvalid) return _templateInstance != null;
            var template = ResolveTemplate();
            if (template == null)
            {
                var removedInstance = _templateInstance;
                _templateInstance = null;
                _templateInvalid = false;
                removedInstance?.Dispose();
                return false;
            }

            var instance = template.Build(this);
            var previousInstance = _templateInstance;
            try
            {
                if (previousInstance != null)
                {
                    previousInstance.Deactivate();
                    RemoveVisualChild(previousInstance.Root);
                }
                AddVisualChild(instance.Root);
                instance.Activate();
                _templateInstance = instance;
                OnTemplateApplied();
            }
            catch
            {
                _templateInstance = previousInstance;
                try { instance.Dispose(); }
                catch { }
                if (previousInstance != null)
                {
                    if (previousInstance.Root.VisualParent == null) AddVisualChild(previousInstance.Root);
                    if (!previousInstance.IsActive) previousInstance.Activate();
                }
                throw;
            }
            _templateInvalid = false;
            previousInstance?.Dispose();
            return true;
        }

        public Control GetTemplateChild(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A template child name is required.", nameof(name));
            return _templateInstance?.NameScope.Find<Control>(name);
        }

        public override Microsoft.Xna.Framework.Vector2 GetMinimumSize() =>
            Microsoft.Xna.Framework.Vector2.Max(base.GetMinimumSize(), TemplateRoot?.GetMinimumSize() ?? Microsoft.Xna.Framework.Vector2.Zero);

        protected override void ArrangeChildren()
        {
            if (_applyTemplateOnArrange)
            {
                _applyTemplateOnArrange = false;
                ApplyTemplate();
            }
            if (TemplateRoot == null) return;
            TemplateRoot.Position = Microsoft.Xna.Framework.Vector2.Zero;
            TemplateRoot.Size = Size;
        }

        protected void InvalidateTemplate()
        {
            _templateInvalid = true;
            QueueLayout();
        }

        protected virtual void OnTemplateApplied() { }

        protected override void OnThemeChanged()
        {
            if (_template == null)
            {
                var themeTemplate = ResolveThemeControlTemplate();
                if (_templateInstance == null && themeTemplate == null)
                {
                    base.OnThemeChanged();
                    return;
                }
                var resolved = themeTemplate ?? DefaultControlTemplateRegistry.Shared.GetTemplate(GetType());
                if (!ReferenceEquals(AppliedTemplate, resolved))
                {
                    _applyTemplateOnArrange = true;
                    InvalidateTemplate();
                }
            }
            base.OnThemeChanged();
        }

        protected override void OnContextChanged(UIContext previous, UIContext current)
        {
            OnThemeChanged();
            base.OnContextChanged(previous, current);
        }

        internal void DeactivateTemplateForRecycle() => _templateInstance?.Deactivate();

        internal void ActivateTemplateAfterRecycle() => _templateInstance?.Activate();

        public virtual void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var instance = _templateInstance;
            _templateInstance = null;
            _template = null;
            _templateInvalid = false;
            instance?.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TemplatedControl));
        }
    }
}