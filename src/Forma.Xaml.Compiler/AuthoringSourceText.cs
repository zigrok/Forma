// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using Forma.Xaml;

namespace Forma.Xaml.Compiler;

/// <summary>Exact UTF-8 input and explicit parser/canonical/diagnostic coordinate projections.</summary>
public static class AuthoringSourceText
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static string Decode(ReadOnlySpan<byte> bytes) => Utf8.GetString(bytes);
    public static byte[] Encode(string text) => Utf8.GetBytes(text);
    public static string Revision(string text) => Convert.ToHexString(SHA256.HashData(Encode(text))).ToLowerInvariant();
    public static string ParserText(string text) => text.StartsWith('\uFEFF') ? text[1..] : text;
    public static int CanonicalColumn(string text, int line, int parserColumn) =>
        parserColumn + (line == 1 && text.StartsWith('\uFEFF') ? 1 : 0);

    public static int Utf16Offset(string text, int line, int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(line);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(column);
        var start = 0;
        for (var current = 1; current < line; current++)
        {
            while (start < text.Length && text[start] is not ('\r' or '\n')) start++;
            if (start == text.Length) throw new ArgumentOutOfRangeException(nameof(line));
            if (text[start++] == '\r' && start < text.Length && text[start] == '\n') start++;
        }
        var end = start;
        while (end < text.Length && text[end] is not ('\r' or '\n')) end++;
        if (column - 1 > end - start) throw new ArgumentOutOfRangeException(nameof(column));
        var offset = start + column - 1;
        ValidateBoundary(text, offset);
        return offset;
    }

    public static (int Line, int Column) CodePointPosition(string text, int utf16Offset)
    {
        ValidateBoundary(text, utf16Offset);
        var line = 1;
        var column = 1;
        for (var index = 0; index < utf16Offset; index++)
        {
            if (text[index] is '\r' or '\n')
            {
                if (text[index] == '\r' && index + 1 < utf16Offset && text[index + 1] == '\n') index++;
                line++;
                column = 1;
            }
            else
            {
                if (char.IsHighSurrogate(text[index]) && index + 1 < utf16Offset && char.IsLowSurrogate(text[index + 1])) index++;
                column++;
            }
        }
        return (line, column);
    }

    private static void ValidateBoundary(string text, int offset)
    {
        if (offset < 0 || offset > text.Length || offset > 0 && offset < text.Length &&
            (char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]) ||
             text[offset - 1] == '\r' && text[offset] == '\n'))
            throw new ArgumentOutOfRangeException(nameof(offset), "An offset cannot split a code point or CRLF.");
    }

    public static XamlSourceNode Describe(FormaLoweredDocument document, FormaLoweredNode node, string type)
    {
        var text = document.CanonicalSource;
        var range = node.SourceRange;
        return new(document.SourcePath, Revision(text), node.Id.Value, node.ScopeId, type,
            node.FindDirective("Name") ?? "", range.StartLine, CanonicalColumn(text, range.StartLine, range.StartColumn),
            node.Members.Where(member => !member.IsDirective).ToDictionary(member => member.Name, member => member.Value.RawText))
        {
            MetadataVersion = XamlSource.AuthoringMetadataVersion,
            RevisionEncoding = "sha256-exact-utf8",
            Coordinates = "canonical-utf16-one-based",
            ParserRevision = Revision(document.Source),
            XmlNamespace = node.XmlNamespace, XmlType = node.TypeName, ParentNode = node.ParentId?.Value,
            EndLine = range.EndLine, EndColumn = CanonicalColumn(text, range.EndLine, range.EndColumn),
            MemberRanges = node.Members.Where(member => !member.IsDirective).ToDictionary(member => member.Name, member =>
                new XamlMemberRange(member.SourceRange.StartLine, CanonicalColumn(text, member.SourceRange.StartLine, member.SourceRange.StartColumn),
                    member.SourceRange.EndLine, CanonicalColumn(text, member.SourceRange.EndLine, member.SourceRange.EndColumn)))
        };
    }
}
