// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Forma.Xaml
{
    public sealed record XamlMemberRange(int Line, int Column, int EndLine, int EndColumn);

    /// <summary>Revision-scoped compiler identities, not runtime tree ordinals or control names.</summary>
    public sealed record XamlSourceNode(
        string Document, string Revision, int Node, int Scope, string Type,
        string Name, int Line, int Column, Dictionary<string, string> Members)
    {
        public string XmlNamespace { get; init; } = "";
        public string XmlType { get; init; } = "";
        public int MetadataVersion { get; init; } = 1;
        public string RevisionEncoding { get; init; } = "";
        public string Coordinates { get; init; } = "";
        public string ParserRevision { get; init; } = "";
        public int? ParentNode { get; init; }
        public int EndLine { get; init; }
        public int EndColumn { get; init; }
        public Dictionary<string, XamlMemberRange> MemberRanges { get; init; } = [];
        // Only the compiled root carries the complete source tree, including non-instantiated definitions.
        public XamlSourceNode[] DocumentNodes { get; init; } = [];
    }

    public static class XamlSource
    {
        public const int AuthoringMetadataVersion = 3;
        private static readonly ConditionalWeakTable<Control, XamlSourceNode> Nodes = new();
        private static readonly ConditionalWeakTable<object, XamlSourceNode> Properties = new();

        public static void SetMetadata(Control target, string metadata)
        {
            var node = JsonSerializer.Deserialize(metadata, XamlSourceJson.Default.XamlSourceNode)
                ?? throw new ArgumentException("Missing source metadata.", nameof(metadata));
            Nodes.Remove(target);
            Nodes.Add(target, node);
        }

        public static string GetMetadata(Control target) => JsonSerializer.Serialize(Get(target), XamlSourceJson.Default.XamlSourceNode);
        public static XamlSourceNode Get(Control target) =>
            Nodes.TryGetValue(target, out var node) ? node : null;

        public static XamlProperty<T> RegisterProperty<T>(XamlProperty<T> property, string metadata)
        {
            Properties.Add(property, JsonSerializer.Deserialize(metadata, XamlSourceJson.Default.XamlSourceNode)
                ?? throw new ArgumentException("Missing property source metadata.", nameof(metadata)));
            return property;
        }

        public static XamlSourceNode GetProperty<T>(XamlProperty<T> property) =>
            Properties.TryGetValue(property, out var node) ? node : null;
    }

    [JsonSerializable(typeof(XamlSourceNode))]
    internal partial class XamlSourceJson : JsonSerializerContext { }
}
