// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Linq;

namespace Forma.Xaml
{
    public sealed record XamlLiteralCapability<T>(XamlProperty<T> Property, Action<T> Validate);

    public sealed class XamlValueEdit<T> : IDisposable
    {
        private XamlValueContribution<T> _contribution;
        private readonly T _before;
        private readonly Control _target;
        private readonly Action<T> _validate;
        private readonly Control _control;
        private bool _changed;
        private bool _disposed;
        public XamlSourceNode Source { get; }
        public string Attribute { get; }
        public XamlProperty<T> Property { get; }

        internal XamlValueEdit(Control control, XamlSourceNode source, string attribute, XamlProperty<T> property,
            XamlValueContribution<T> contribution)
        {
            _control = control;
            Source = source;
            Attribute = attribute;
            Property = property;
            _contribution = contribution;
            _before = contribution.Value;
        }

        internal XamlValueEdit(Control target, XamlSourceNode source, XamlLiteralCapability<T> capability)
        {
            Source = source;
            Attribute = capability.Property.Name;
            Property = capability.Property;
            _target = target;
            _control = target;
            _validate = capability.Validate;
            _before = Property.GetValue(target);
        }

        public T AuthoredValue => _contribution == null ? _before : _contribution.Value;
        public T EffectiveValue => Property.GetValue(_control);
        public void ValidateCurrent()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var source = XamlSource.Get(_control);
            if (source == null || source.Document != Source.Document || source.Revision != Source.Revision)
                throw new InvalidOperationException("The compiled source identity changed.");
            var handle = XamlValues.Inspect<T>(_control, Property.Name);
            if (_contribution != null && !_contribution.IsAttached)
                throw new InvalidOperationException("The edited contribution was detached.");
            if (handle != null && (handle.Property != Property ||
                (_contribution != null && !handle.Contributions.Contains(_contribution)) ||
                handle.Contributions.Any(value => value != _contribution &&
                    value.Layer == (_target == null ? XamlValueLayer.Style : XamlValueLayer.Local))))
                throw new InvalidOperationException("The authored descriptor or contribution is no longer owned by this edit.");
        }
        public void Apply(T value)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _validate?.Invoke(value);
            ValidateCurrent();
            _changed = true;
            if (_contribution == null) _contribution = XamlValues.Set(_target, Property, XamlValueLayer.Local, value);
            else _contribution.Value = value;
        }

        public void Undo()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_changed) return;
            if (_target == null)
            {
                if (_contribution.IsAttached) _contribution.Value = _before;
            }
            else { _contribution?.Dispose(); _contribution = null; }
            _changed = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            Undo();
            _disposed = true;
        }
    }

    /// <summary>Explicit typed capabilities; ambiguity and expressions require a rebuild, never a guessed setter.</summary>
    public static class XamlAuthoring
    {
        public static System.Collections.Generic.IReadOnlyList<Control> VisualChildren(Control control) =>
            control.VisualChildren.ToArray();

        public static XamlValueEdit<T> Style<T>(Control view, Control target, string propertyName, string revision)
        {
            var controls = Descendants(view).ToArray();
            if (!controls.Contains(target)) throw new InvalidOperationException("Target is outside the isolated view.");
            var targetSource = RequireSource(target, revision);
            var handle = XamlValues.Inspect<T>(target, propertyName)
                ?? throw new InvalidOperationException("No compiled value descriptor is attached.");
            var source = XamlSource.GetProperty(handle.Property)
                ?? throw new InvalidOperationException("This descriptor has no authored source; rebuild with authoring metadata.");
            if (source.Document != targetSource.Document || source.Revision != revision ||
                !source.Members.TryGetValue("Value", out var raw) || raw.StartsWith("{", StringComparison.Ordinal))
                throw new InvalidOperationException("Stale or expression-valued style; compilation is required.");
            var contributions = handle.Contributions.Where(value => value.Layer == XamlValueLayer.Style).ToArray();
            if (contributions.Length != 1)
                throw new InvalidOperationException("Select an unambiguous authored style contribution.");
            if (controls.Count(control => XamlValues.Inspect<T>(control, propertyName)?.Property == handle.Property) != 1)
                throw new InvalidOperationException("Multi-target style editing requires a definition-wide transaction; rebuild this view instead.");
            return new XamlValueEdit<T>(target, source, "Value", handle.Property, contributions[0]);
        }

        private static System.Collections.Generic.IEnumerable<Control> Descendants(Control control)
        {
            yield return control;
            foreach (var child in control.Children)
                foreach (var descendant in Descendants(child)) yield return descendant;
        }

        public static XamlValueEdit<T> Literal<T>(Control target, XamlLiteralCapability<T> capability, string revision)
        {
            var source = RequireSource(target, revision);
            if (!source.Members.TryGetValue(capability.Property.Name, out var raw) || raw.StartsWith("{", StringComparison.Ordinal))
                throw new InvalidOperationException("Only existing local literals are supported; expressions and new overrides require compilation.");
            var handle = XamlValues.Inspect<T>(target, capability.Property.Name);
            if (handle != null && (handle.Property != capability.Property || handle.Contributions.Count != 0))
                throw new InvalidOperationException("An existing value-layer descriptor owns this property; do not replace it with a catalog setter.");
            return new XamlValueEdit<T>(target, source, capability);
        }

        private static XamlSourceNode RequireSource(Control target, string revision)
        {
            var source = XamlSource.Get(target)
                ?? throw new InvalidOperationException("Control has no compiled authoring source.");
            if (source.Revision != revision)
                throw new InvalidOperationException("Source revision is stale; rebuild before editing.");
            return source;
        }
    }
}
