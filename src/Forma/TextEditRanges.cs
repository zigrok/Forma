// SPDX-License-Identifier: MIT

using System.Text;

namespace Forma;

internal readonly record struct TextCaretRange(int Anchor, int Index);

internal static class TextEditRanges
{
    internal static (string Text, int[] Carets) Delete(string text, IReadOnlyList<(int Start, int End)> ranges)
    {
        var merged = new List<(int Start, int End)>();
        foreach (var range in ranges.OrderBy(range => range.Start))
        {
            if (range.Start < 0 || range.End < range.Start || range.End > text.Length)
                throw new ArgumentOutOfRangeException(nameof(ranges));
            if (merged.Count > 0 && range.Start <= merged[^1].End)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, range.End));
            else merged.Add(range);
        }
        var result = new StringBuilder(text.Length);
        var cursor = 0;
        foreach (var range in merged)
        {
            result.Append(text, cursor, range.Start - cursor);
            cursor = range.End;
        }
        result.Append(text, cursor, text.Length - cursor);
        var carets = new int[ranges.Count];
        for (var caret = 0; caret < ranges.Count; caret++)
        {
            var original = ranges[caret].Start;
            var removed = 0;
            foreach (var range in merged)
            {
                if (original <= range.Start) break;
                removed += Math.Min(original, range.End) - range.Start;
                if (original < range.End) break;
            }
            carets[caret] = original - removed;
        }
        return (result.ToString(), carets);
    }

    internal static IReadOnlyList<TextCaretRange> Merge(IReadOnlyList<TextCaretRange> carets)
    {
        var sorted = Enumerable.Range(0, carets.Count)
            .OrderBy(index => Math.Min(carets[index].Anchor, carets[index].Index)).ToArray();
        var groups = new List<(bool Primary, TextCaretRange Caret)>();
        for (var index = 0; index < sorted.Length;)
        {
            var owner = sorted[index++];
            var from = Math.Min(carets[owner].Anchor, carets[owner].Index);
            var to = Math.Max(carets[owner].Anchor, carets[owner].Index);
            var primary = owner == 0;
            while (index < sorted.Length && Math.Min(carets[sorted[index]].Anchor, carets[sorted[index]].Index) <= to)
            {
                var next = sorted[index++];
                to = Math.Max(to, Math.Max(carets[next].Anchor, carets[next].Index));
                if (next == 0) { primary = true; owner = 0; }
            }
            var backward = carets[owner].Index < carets[owner].Anchor;
            groups.Add((primary, backward ? new TextCaretRange(to, from) : new TextCaretRange(from, to)));
        }
        return groups.OrderByDescending(group => group.Primary).Select(group => group.Caret).ToArray();
    }
}
