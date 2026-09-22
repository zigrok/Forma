// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;

[assembly: Forma.Xaml.XmlnsDefinition(Forma.Xaml.XamlNamespaces.Forma, "Forma")]
[assembly: Forma.Xaml.XmlnsDefinition(Forma.Xaml.XamlNamespaces.Forma, "Forma.Xaml")]
[assembly: Forma.Xaml.XmlnsPrefix(Forma.Xaml.XamlNamespaces.Forma, "forma")]

namespace Forma.Xaml
{
    public static class XamlNamespaces
    {
        public const string Forma = "https://forma.dev/xaml";
        public const string Xaml2006 = "http://schemas.microsoft.com/winfx/2006/xaml";
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class XmlnsDefinitionAttribute : Attribute
    {
        public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace)
        {
            XmlNamespace = xmlNamespace ?? throw new ArgumentNullException(nameof(xmlNamespace));
            ClrNamespace = clrNamespace ?? throw new ArgumentNullException(nameof(clrNamespace));
        }

        public string XmlNamespace { get; }
        public string ClrNamespace { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class XmlnsPrefixAttribute : Attribute
    {
        public XmlnsPrefixAttribute(string xmlNamespace, string prefix)
        {
            XmlNamespace = xmlNamespace ?? throw new ArgumentNullException(nameof(xmlNamespace));
            Prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
        }

        public string XmlNamespace { get; }
        public string Prefix { get; }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class ContentAttribute : Attribute
    {
        public ContentAttribute(string propertyName) => PropertyName = propertyName;
        public string PropertyName { get; }
    }

    public interface IAddChild
    {
        void AddChild(object child);
    }

    public interface IAddChild<in T> : IAddChild
    {
        void AddChild(T child);
    }

    public interface IValueConverter
    {
        object Convert(object value, Type targetType, object parameter, CultureInfo culture);
        object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture);
    }

    public static class XamlValueConverter
    {
        private static readonly Dictionary<string, Color> NamedColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            ["Transparent"] = new Color(0, 0, 0, 0),
            ["Black"] = new Color(0, 0, 0),
            ["White"] = new Color(255, 255, 255),
            ["Red"] = new Color(255, 0, 0),
            ["Green"] = new Color(0, 128, 0),
            ["Blue"] = new Color(0, 0, 255),
            ["Yellow"] = new Color(255, 255, 0),
            ["Gray"] = new Color(128, 128, 128),
            ["Grey"] = new Color(128, 128, 128),
            ["LightGray"] = new Color(211, 211, 211),
            ["DarkGray"] = new Color(169, 169, 169),
            ["CornflowerBlue"] = new Color(100, 149, 237),
            ["Orange"] = new Color(255, 165, 0),
            ["Purple"] = new Color(128, 0, 128),
            ["Pink"] = new Color(255, 192, 203),
            ["Cyan"] = new Color(0, 255, 255),
            ["Magenta"] = new Color(255, 0, 255),
            ["Lime"] = new Color(0, 255, 0),
            ["Brown"] = new Color(165, 42, 42),
            ["Gold"] = new Color(255, 215, 0),
            ["Silver"] = new Color(192, 192, 192),
            ["Navy"] = new Color(0, 0, 128),
            ["Teal"] = new Color(0, 128, 128),
            ["Olive"] = new Color(128, 128, 0),
            ["Maroon"] = new Color(128, 0, 0),
        };

        public static object Convert(string text, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] Type targetType)
        {
            if (targetType == null) throw new ArgumentNullException(nameof(targetType));
            if (targetType == typeof(string)) return text;

            var nullableType = Nullable.GetUnderlyingType(targetType);
            if (nullableType != null)
            {
                if (string.IsNullOrWhiteSpace(text)) return null;
                targetType = nullableType;
            }

            if (targetType.IsEnum) return Enum.Parse(targetType, text, true);
            if (targetType == typeof(Color)) return ParseColor(text);
            if (targetType == typeof(Vector2))
            {
                var components = ParseComponents(text, 2);
                return new Vector2(components[0], components[1]);
            }
            if (targetType == typeof(Thickness)) return ParseThickness(text);
            if (targetType == typeof(CornerRadius)) return ParseCornerRadius(text);
            if (targetType == typeof(GridTrackSize)) return ParseGridTrackSize(text);
            if (targetType == typeof(Brush)) return ParseBrush(text);
            if (targetType == typeof(Geometry)) return ParseGeometry(text);
            if (targetType == typeof(TimeSpan)) return TimeSpan.Parse(text, CultureInfo.InvariantCulture);
            if (targetType == typeof(char))
            {
                if (text?.Length == 1) return text[0];
                throw new FormatException("A character value must contain exactly one character.");
            }

            return System.Convert.ChangeType(text, targetType, CultureInfo.InvariantCulture);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [RequiresDynamicCode("Calling-assembly inference is not supported by NativeAOT. Use the explicit assembly overload.")]
        public static SvgImageSource ParseSvgAsset(string logicalName) =>
            ParseSvgAsset(logicalName, Assembly.GetCallingAssembly());

        public static SvgImageSource ParseSvgAsset(string logicalName, Assembly assembly) =>
            SvgImageSource.FromManifestResource(assembly, logicalName);

        public static SvgImageSource ParseSvgFile(string path) => SvgImageSource.FromFile(path);

        public static bool TryConvert(string text, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] Type targetType, out object value)
        {
            try
            {
                value = Convert(text, targetType);
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidCastException or OverflowException)
            {
                value = null;
                return false;
            }
        }

        public static Color ParseColor(string text)
        {
            if (text != null && NamedColors.TryGetValue(text.Trim(), out var named)) return named;
            if (string.IsNullOrWhiteSpace(text) || text[0] != '#')
                throw new FormatException($"'{text}' is not a named or hexadecimal color.");

            var hex = text.Substring(1);
            if (hex.Length == 3)
                return new Color(ExpandNibble(hex[0]), ExpandNibble(hex[1]), ExpandNibble(hex[2]));
            if (hex.Length == 4)
                return new Color(ExpandNibble(hex[1]), ExpandNibble(hex[2]), ExpandNibble(hex[3]), ExpandNibble(hex[0]));
            if (hex.Length == 6)
                return new Color(ParseByte(hex, 0), ParseByte(hex, 2), ParseByte(hex, 4));
            if (hex.Length == 8)
                return new Color(ParseByte(hex, 2), ParseByte(hex, 4), ParseByte(hex, 6), ParseByte(hex, 0));
            throw new FormatException($"'{text}' must use #RGB, #ARGB, #RRGGBB, or #AARRGGBB.");
        }

        public static Vector2 ParseVector2(string text)
        {
            var components = ParseComponents(text, 2);
            return new Vector2(components[0], components[1]);
        }

        public static GridTrackSize ParseGridTrackSize(string text)
        {
            var value = text?.Trim() ?? throw new ArgumentNullException(nameof(text));
            if (value.Equals("Auto", StringComparison.OrdinalIgnoreCase)) return GridTrackSize.Auto;
            if (value.EndsWith("*", StringComparison.Ordinal))
            {
                var weight = value.Length == 1
                    ? 1
                    : float.Parse(value.Substring(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture);
                return GridTrackSize.Star(weight);
            }
            if (value.EndsWith("%", StringComparison.Ordinal))
                return GridTrackSize.Percent(float.Parse(value.Substring(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture));
            return GridTrackSize.Pixels(float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture));
        }

        public static Thickness ParseThickness(string text)
        {
            var parts = text?.Split(',') ?? Array.Empty<string>();
            var values = new float[parts.Length];
            for (var index = 0; index < parts.Length; index++)
                values[index] = float.Parse(parts[index].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
            return values.Length switch
            {
                1 => new Thickness(values[0]),
                2 => new Thickness(values[0], values[1], values[0], values[1]),
                4 => new Thickness(values[0], values[1], values[2], values[3]),
                _ => throw new FormatException("Thickness requires one, two, or four comma-separated values."),
            };
        }

        public static CornerRadius ParseCornerRadius(string text)
        {
            var parts = ParseVariableComponents(text);
            return parts.Length switch
            {
                1 => new CornerRadius(parts[0]),
                4 => new CornerRadius(parts[0], parts[1], parts[2], parts[3]),
                _ => throw new FormatException("CornerRadius requires one or four comma-separated values."),
            };
        }

        public static Brush ParseBrush(string text) => new SolidColorBrush(ParseColor(text));

        public static Geometry ParseGeometry(string text) => new PathGeometry(DrawingPath.Parse(text));

        private static float[] ParseComponents(string text, int count)
        {
            var parts = text?.Split(',') ?? Array.Empty<string>();
            if (parts.Length != count) throw new FormatException($"Expected {count} comma-separated values.");
            var values = new float[count];
            for (var index = 0; index < count; index++)
                values[index] = float.Parse(parts[index].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
            return values;
        }

        private static float[] ParseVariableComponents(string text)
        {
            var parts = text?.Split(',') ?? Array.Empty<string>();
            var values = new float[parts.Length];
            for (var index = 0; index < parts.Length; index++)
                values[index] = float.Parse(parts[index].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
            return values;
        }

        private static byte ExpandNibble(char value)
        {
            var nibble = byte.Parse(value.ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return (byte)(nibble * 17);
        }

        private static byte ParseByte(string value, int index) =>
            byte.Parse(value.Substring(index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    public sealed class DataContextChangedEventArgs : EventArgs
    {
        public DataContextChangedEventArgs(object previousValue, object currentValue)
        {
            PreviousValue = previousValue;
            CurrentValue = currentValue;
        }

        public object PreviousValue { get; }
        public object CurrentValue { get; }
    }

    public sealed class ControlClassList : ICollection<string>
    {
        private readonly HashSet<string> _classes = new HashSet<string>(StringComparer.Ordinal);

        public event EventHandler Changed;
        public int Count => _classes.Count;
        public bool IsReadOnly => false;

        public bool Add(string item)
        {
            Validate(item);
            if (!_classes.Add(item)) return false;
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        void ICollection<string>.Add(string item) => Add(item);

        public bool Remove(string item)
        {
            if (!_classes.Remove(item)) return false;
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void Clear()
        {
            if (_classes.Count == 0) return;
            _classes.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public bool Contains(string item) => _classes.Contains(item);
        public void CopyTo(string[] array, int arrayIndex) => _classes.CopyTo(array, arrayIndex);
        public IEnumerator<string> GetEnumerator() => _classes.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Set(string classes)
        {
            var replacement = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(classes))
                foreach (var item in classes.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                {
                    Validate(item);
                    replacement.Add(item);
                }
            if (_classes.SetEquals(replacement)) return;
            _classes.Clear();
            _classes.UnionWith(replacement);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private static void Validate(string item)
        {
            if (string.IsNullOrWhiteSpace(item) || item.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0)
                throw new ArgumentException("A class name must be a non-empty token.", nameof(item));
        }
    }

    public sealed class ResourceDictionary : IDictionary<string, object>
    {
        private sealed class MergeCollection : Collection<ResourceDictionary>
        {
            private readonly ResourceDictionary _owner;

            public MergeCollection(ResourceDictionary owner) => _owner = owner;

            protected override void InsertItem(int index, ResourceDictionary item)
            {
                if (item == null) throw new ArgumentNullException(nameof(item));
                base.InsertItem(index, item);
                item.Changed += _owner.MergedDictionaryChanged;
                _owner.OnChanged();
            }

            protected override void SetItem(int index, ResourceDictionary item)
            {
                if (item == null) throw new ArgumentNullException(nameof(item));
                this[index].Changed -= _owner.MergedDictionaryChanged;
                base.SetItem(index, item);
                item.Changed += _owner.MergedDictionaryChanged;
                _owner.OnChanged();
            }

            protected override void RemoveItem(int index)
            {
                this[index].Changed -= _owner.MergedDictionaryChanged;
                base.RemoveItem(index);
                _owner.OnChanged();
            }

            protected override void ClearItems()
            {
                foreach (var dictionary in this) dictionary.Changed -= _owner.MergedDictionaryChanged;
                base.ClearItems();
                _owner.OnChanged();
            }
        }

        private readonly Dictionary<string, object> _values = new Dictionary<string, object>(StringComparer.Ordinal);

        public ResourceDictionary() => MergedDictionaries = new MergeCollection(this);

        public event EventHandler Changed;
        public Collection<ResourceDictionary> MergedDictionaries { get; }
        public ICollection<string> Keys => _values.Keys;
        public ICollection<object> Values => _values.Values;
        public int Count => _values.Count;
        public bool IsReadOnly => false;

        public object this[string key]
        {
            get
            {
                if (TryFind(key, out var value)) return value;
                throw new KeyNotFoundException($"Resource '{key}' was not found.");
            }
            set
            {
                ValidateKey(key);
                _values[key] = value;
                OnChanged();
            }
        }

        public void Add(string key, object value)
        {
            ValidateKey(key);
            _values.Add(key, value);
            OnChanged();
        }

        public void Add(ResourceDictionary dictionary)
        {
            if (dictionary == null) throw new ArgumentNullException(nameof(dictionary));
            foreach (var item in dictionary._values) Add(item.Key, item.Value);
            foreach (var merged in dictionary.MergedDictionaries) MergedDictionaries.Add(merged);
        }

        public bool TryFind(string key, out object value)
        {
            if (_values.TryGetValue(key, out value)) return true;
            for (var index = MergedDictionaries.Count - 1; index >= 0; index--)
                if (MergedDictionaries[index].TryFind(key, out value)) return true;
            value = null;
            return false;
        }

        public bool ContainsKey(string key) => _values.ContainsKey(key);
        public bool Remove(string key)
        {
            if (!_values.Remove(key)) return false;
            OnChanged();
            return true;
        }
        public bool TryGetValue(string key, out object value) => _values.TryGetValue(key, out value);
        public void Add(KeyValuePair<string, object> item) => Add(item.Key, item.Value);
        public void Clear()
        {
            if (_values.Count == 0) return;
            _values.Clear();
            OnChanged();
        }
        public bool Contains(KeyValuePair<string, object> item) => ((ICollection<KeyValuePair<string, object>>)_values).Contains(item);
        public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) => ((ICollection<KeyValuePair<string, object>>)_values).CopyTo(array, arrayIndex);
        public bool Remove(KeyValuePair<string, object> item)
        {
            if (!((ICollection<KeyValuePair<string, object>>)_values).Remove(item)) return false;
            OnChanged();
            return true;
        }
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private void MergedDictionaryChanged(object sender, EventArgs args) => OnChanged();
        private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A resource key cannot be null or empty.", nameof(key));
        }
    }

    public sealed class NameScope
    {
        private static readonly ConditionalWeakTable<object, NameScope> Scopes = new ConditionalWeakTable<object, NameScope>();
        private readonly Dictionary<string, object> _names = new Dictionary<string, object>(StringComparer.Ordinal);

        public static void SetNameScope(object owner, NameScope scope)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            Scopes.Remove(owner);
            if (scope != null) Scopes.Add(owner, scope);
        }

        public static NameScope GetNameScope(object owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            return Scopes.TryGetValue(owner, out var scope) ? scope : null;
        }

        public void Register(string name, object value)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A name cannot be null or empty.", nameof(name));
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (_names.ContainsKey(name)) throw new InvalidOperationException($"Name '{name}' is already registered in this namescope.");
            _names.Add(name, value);
        }

        public bool Unregister(string name) => _names.Remove(name);
        public object Find(string name) => _names.TryGetValue(name, out var value) ? value : null;
        public T Find<T>(string name) where T : class => Find(name) as T;

        public static NameScope CreateForTree(Control root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var scope = new NameScope();
            RegisterTree(scope, root);
            SetNameScope(root, scope);
            return scope;
        }

        private static void RegisterTree(NameScope scope, Control control)
        {
            if (!string.IsNullOrWhiteSpace(control.Name)) scope.Register(control.Name, control);
            foreach (var child in control.Children) RegisterTree(scope, child);
        }

        public static Control FindControlByOrdinal(Control root, int ordinal)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
            var current = 0;
            return FindControlByOrdinal(root, ordinal, ref current)
                ?? throw new InvalidOperationException($"Control ordinal {ordinal} is outside the populated XAML tree.");
        }

            public static Control FindControlByName(Control root, string name)
            {
                if (root == null) throw new ArgumentNullException(nameof(root));
                if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A name is required.", nameof(name));
                var scope = GetNameScope(root) ?? throw new InvalidOperationException("The populated XAML tree has no namescope.");
                return scope.Find(name) as Control ?? throw new InvalidOperationException($"Control '{name}' was not found in the populated XAML namescope.");
            }

        private static Control FindControlByOrdinal(Control control, int ordinal, ref int current)
        {
            if (control.IsXamlInfrastructure) return null;
            if (current++ == ordinal) return control;
            foreach (var child in control.Children)
            {
                var found = FindControlByOrdinal(child, ordinal, ref current);
                if (found != null) return found;
            }
            return null;
        }
    }

    public static class FormaXamlLoader
    {
        private static readonly Dictionary<Type, Func<IServiceProvider, object>> Factories = new();
        private static class Registry<T> where T : class
        {
            public static Func<IServiceProvider, T> Build;
            public static Action<IServiceProvider, T> Populate;
        }

        public static void Register<T>(Func<IServiceProvider, T> build, Action<IServiceProvider, T> populate) where T : class
        {
            Registry<T>.Build = build ?? throw new ArgumentNullException(nameof(build));
            Registry<T>.Populate = populate ?? throw new ArgumentNullException(nameof(populate));
            lock (Factories) Factories[typeof(T)] = serviceProvider => build(serviceProvider);
        }

        public static void RegisterPopulate<T>(Action<IServiceProvider, T> populate) where T : class
        {
            Registry<T>.Populate = populate ?? throw new ArgumentNullException(nameof(populate));
        }

        public static T Load<T>(IServiceProvider serviceProvider = null) where T : class
        {
            var build = Registry<T>.Build;
            if (build == null) throw new InvalidOperationException($"No compiled Forma XAML factory is registered for {typeof(T).FullName}.");
            return build(serviceProvider);
        }

        public static object Load(Type type, IServiceProvider serviceProvider = null)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            Func<IServiceProvider, object> build;
            lock (Factories)
                if (!Factories.TryGetValue(type, out build))
                    throw new InvalidOperationException($"No compiled Forma XAML factory is registered for {type.FullName}.");
            return build(serviceProvider);
        }

        public static void Load<T>(T instance, IServiceProvider serviceProvider = null) where T : class
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            var populate = Registry<T>.Populate;
            if (populate == null) throw new InvalidOperationException($"No compiled Forma XAML populate method is registered for {typeof(T).FullName}.");
            populate(serviceProvider, instance);
        }
    }
}