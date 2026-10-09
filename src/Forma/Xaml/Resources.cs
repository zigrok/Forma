// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

namespace Forma.Xaml
{
    /// <summary>The control a style set was attached to while its setters apply, so a setter's StaticResource also resolves from where the style was defined.</summary>
    internal static class StyleApplicationScope
    {
        [ThreadStatic] private static Control _current;
        public static Control Current => _current;
        public static IDisposable Enter(Control scope)
        {
            var previous = _current;
            _current = scope;
            return new Restore(previous);
        }

        private sealed class Restore : IDisposable
        {
            private readonly Control _previous;
            public Restore(Control previous) => _previous = previous;
            public void Dispose() => _current = _previous;
        }
    }

    public static class StaticResource
    {
        public static T Resolve<T>(Control target, string key)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            object value;
            if (!target.TryFindResource(key, out value) && !(StyleApplicationScope.Current?.TryFindResource(key, out value) ?? false))
                throw new KeyNotFoundException($"Resource '{key}' was not found.");
            if (value is T typed) return typed;
            throw new InvalidCastException($"Resource '{key}' is {value?.GetType().FullName ?? "null"}, not {typeof(T).FullName}.");
        }
    }

    public static class DynamicResource
    {
        public static IDisposable Attach<T>(
            Control root,
            Control target,
            XamlProperty<T> property,
            string key,
            Func<object, T> convert = null,
            XamlValueLayer layer = XamlValueLayer.Local,
            long priority = 0)
        {
            return XamlAttachment.RegisterReactivatable(root, () =>
                new DynamicResourceExpression<T>(target, property, key, convert, layer, priority));
        }
    }

    /// <summary>A style setter whose value follows a resource: when the resource changes, controls the style already applied to update.</summary>
    public sealed class DynamicStyleSetter<T> : IStyleSetter, IStyleSetterInfo
    {
        private readonly XamlProperty<T> _property;
        private readonly string _key;
        private readonly Func<object, T> _convert;

        public DynamicStyleSetter(XamlProperty<T> property, string resourceKey, Func<object, T> convert = null)
        {
            _property = property ?? throw new ArgumentNullException(nameof(property));
            _key = string.IsNullOrEmpty(resourceKey) ? throw new ArgumentException("A resource key is required.", nameof(resourceKey)) : resourceKey;
            _convert = convert;
        }

        // The inspector shows a var(--token) setter like any other: the property, and the value the token currently has for the control.
        string IStyleSetterInfo.PropertyName => _property.Name;
        object IStyleSetterInfo.ValueFor(Control control)
        {
            var scope = StyleApplicationScope.Current;
            if (control.TryFindResource(_key, out var found) || (scope?.TryFindResource(_key, out found) ?? false))
                return _convert == null ? found : _convert(found);
            return "{DynamicResource " + _key + "} (unresolved)";
        }

        public IDisposable Apply(Control control, long priority) =>
            new DynamicResourceExpression<T>(control, _property, _key, _convert, XamlValueLayer.Style, priority, StyleApplicationScope.Current);
    }

    internal sealed class DynamicResourceExpression<T> : IDisposable
    {
        private readonly Control _target;
        private readonly XamlProperty<T> _property;
        private readonly string _key;
        private readonly Func<object, T> _convert;
        private readonly XamlValueLayer _layer;
        private readonly long _priority;
        private readonly List<ResourceDictionary> _dictionaries = new List<ResourceDictionary>();
        private XamlValueContribution<T> _value;
        private bool _disposed;

        private readonly Control _fallbackScope;

        public DynamicResourceExpression(Control target, XamlProperty<T> property, string key, Func<object, T> convert, XamlValueLayer layer, long priority, Control fallbackScope = null)
        {
            _fallbackScope = fallbackScope;
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _property = property ?? throw new ArgumentNullException(nameof(property));
            _key = string.IsNullOrEmpty(key) ? throw new ArgumentException("A resource key is required.", nameof(key)) : key;
            _convert = convert ?? (value => (T)value);
            _layer = layer;
            _priority = priority;
            try
            {
                target.Attached += TargetContextChanged;
                target.Detached += TargetContextChanged;
                target.ParentChanged += TargetParentChanged;
                Subscribe();
                Update();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void TargetContextChanged(object sender, EventArgs args)
        {
            Subscribe();
            Update();
        }

        private void TargetParentChanged(object sender, ControlParentChangedEventArgs args) => TargetContextChanged(sender, args);

        private void Subscribe()
        {
            foreach (var dictionary in _dictionaries) dictionary.Changed -= ResourceChanged;
            _dictionaries.Clear();
            for (var control = _target; control != null; control = control.InheritanceParent)
            {
                _dictionaries.Add(control.Resources);
                control.Resources.Changed += ResourceChanged;
            }
            if (_target.Context != null)
            {
                _dictionaries.Add(_target.Context.Resources);
                _target.Context.Resources.Changed += ResourceChanged;
            }
            for (var control = _fallbackScope; control != null; control = control.InheritanceParent)
            {
                if (_dictionaries.Contains(control.Resources)) break;
                _dictionaries.Add(control.Resources);
                control.Resources.Changed += ResourceChanged;
            }
        }

        private void ResourceChanged(object sender, EventArgs args) => Update();

        private void Update()
        {
            object found = null;
            if (_target.TryFindResource(_key, out found) || (_fallbackScope?.TryFindResource(_key, out found) ?? false))
            {
                var converted = _convert(found);
                if (_value == null) _value = XamlValues.Set(_target, _property, _layer, converted, _priority);
                else _value.Value = converted;
            }
            else
            {
                _value?.Dispose();
                _value = null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _target.Attached -= TargetContextChanged;
            _target.Detached -= TargetContextChanged;
            _target.ParentChanged -= TargetParentChanged;
            foreach (var dictionary in _dictionaries) dictionary.Changed -= ResourceChanged;
            _dictionaries.Clear();
            _value?.Dispose();
            _value = null;
        }
    }
}