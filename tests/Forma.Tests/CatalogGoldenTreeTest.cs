// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Forma.Catalog;
using Microsoft.Xna.Framework;

namespace Forma.Tests;

/// <summary>
/// Golden accessibility trees for every Catalog story. This is both the Track A demo and the
/// regression guard it was meant to be.
/// <para>
/// It catches the class of bug unit tests structurally cannot: a control is fine in isolation, but
/// composed into a real tree it renders blank, collapses to its minimum size, or loses its role.
/// Those are composition failures, and the only thing that sees them is the composed result.
/// </para>
/// </summary>
public sealed class CatalogGoldenTreeTest
{
    /// <summary>
    /// Resolved by walking up to the project directory rather than by counting "..", so it keeps
    /// working if the build layout changes. A golden has to live in source control; landing it under
    /// bin/ would mean it silently disappears on a clean and the test starts passing vacuously.
    /// </summary>
    private static string GoldenPath
    {
        get
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Forma.Tests.csproj")))
                directory = directory.Parent;

            if (directory == null)
                throw new InvalidOperationException("Could not locate the Forma.Tests project directory.");

            return Path.Combine(directory.FullName, "Goldens", "catalog-trees.txt");
        }
    }

    private static string Capture()
    {
        var builder = new StringBuilder();

        foreach (var story in StoryCatalog.Create(null).OrderBy(story => story.Name, StringComparer.Ordinal))
        {
            builder.Append("=== ").Append(story.Name).Append('\n');

            string text;
            try
            {
                using var context = new UIContext { ViewportSize = new Vector2(800, 600) };
                var control = story.Factory();
                context.Add(control);
                TryAttach(story, control);
                text = AccessibilityTree.Capture(context).ToText();
            }
            catch (Exception exception)
            {
                // Recorded rather than thrown: a story that cannot be built is itself a regression
                // worth seeing in the diff, and one bad story should not hide the other 137.
                text = $"<threw {exception.GetType().Name}>\n";
            }

            builder.Append(Cap(text));
        }

        return builder.ToString();
    }

    [Test]
    public void EveryStoryMatchesItsGoldenTree()
    {
        var actual = Capture();
        var path = Path.GetFullPath(GoldenPath);

        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            Assert.Inconclusive($"Golden file created at {path}. Review it and commit.");
            return;
        }

        var expected = File.ReadAllText(path);
        if (string.Equals(expected, actual, StringComparison.Ordinal)) return;

        // Written beside the golden so the diff can be inspected directly instead of reconstructed
        // from an assertion message.
        var actualPath = path + ".actual";
        File.WriteAllText(actualPath, actual);

        Assert.Fail(
            $"Catalog accessibility trees changed.\n{FirstDifference(expected, actual)}\n"
            + $"Full output written to {actualPath}.\n"
            + "If the change is intended, replace the golden with it.");
    }

    [Test]
    public void EveryStoryProducesANonEmptyTree()
    {
        var empty = new List<string>();

        foreach (var story in StoryCatalog.Create(null))
        {
            using var context = new UIContext { ViewportSize = new Vector2(800, 600) };
            try
            {
                var control = story.Factory();
                context.Add(control);
                TryAttach(story, control);

                if (AccessibilityTree.Capture(context).Nodes.Count == 0) empty.Add(story.Name);
            }
            catch (Exception exception)
            {
                empty.Add($"{story.Name} (threw {exception.GetType().Name})");
            }
        }

        // A story that snapshots to nothing is invisible to every consumer downstream - tests, the
        // OS bridge, an agent - so it is worth failing on directly rather than only as a golden diff.
        Assert.That(empty, Is.Empty, "these stories produce no accessibility tree at all");
    }

    /// <summary>
    /// Lines kept per story. "Collection Systems" alone produced 30,361 of the first capture's
    /// 37,791 lines — 80% of the file — because a virtualized collection lists every realized row.
    /// Golden-diffing thousands of near-identical rows costs a multi-megabyte file and unreadable
    /// diffs while adding almost nothing: the structure worth guarding is the roles and hierarchy,
    /// which the first rows already show. A regression in row 900 that row 1 does not also show is
    /// not something this test was ever going to catch legibly.
    /// </summary>
    private const int MaxLinesPerStory = 200;

    private static string Cap(string text)
    {
        var lines = text.Split('\n');
        if (lines.Length <= MaxLinesPerStory) return text;

        return string.Join('\n', lines.Take(MaxLinesPerStory))
            + $"\n... {lines.Length - MaxLinesPerStory} more line(s) not recorded (capped at {MaxLinesPerStory})\n";
    }

    /// <summary>
    /// Runs a story's attach step, tolerating the ones that need a graphics device.
    /// <para>
    /// Stories are built here with a null texture, because there is no device in a headless run.
    /// A few decorate themselves with one ("Theme icons" assigns a custom icon), and those throw at
    /// attach. The control itself builds fine, so its tree is still worth capturing — skipping the
    /// whole story would lose real coverage over a step that only affects appearance.
    /// </para>
    /// </summary>
    private static void TryAttach(ComponentStory story, Control control)
    {
        try
        {
            story.Attached?.Invoke(control);
        }
        catch (NullReferenceException)
        {
            // The texture this story wanted is unavailable headlessly.
        }
    }

    private static string FirstDifference(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');

        for (var index = 0; index < Math.Max(expectedLines.Length, actualLines.Length); index++)
        {
            var left = index < expectedLines.Length ? expectedLines[index] : "<end of file>";
            var right = index < actualLines.Length ? actualLines[index] : "<end of file>";
            if (string.Equals(left, right, StringComparison.Ordinal)) continue;

            return $"First difference at line {index + 1}:\n  golden: {left}\n  actual: {right}";
        }

        return "Files differ only in trailing content.";
    }
}
