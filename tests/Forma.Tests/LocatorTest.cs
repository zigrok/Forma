// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Xna.Framework;

namespace Forma.Tests;

/// <summary>
/// Locators are the piece that removes pixel coordinates from tests, so what they match, how they
/// fail, and when they refuse to act is the contract. The actionability cases matter most: without
/// them a test can "click" something disabled or covered and quietly pass, which is worse than not
/// having the test.
/// </summary>
public sealed class LocatorTest
{
    private static UIContext CreateContext() => new() { ViewportSize = new Vector2(800, 600) };

    private static (UIContext Context, Button Save, Button Cancel) TwoButtons()
    {
        var context = CreateContext();
        var root = new StackPanel { Size = new Vector2(400, 200) };
        var save = new Button { Text = "Save", Name = "Save", AutomationId = "cmd.save", Size = new Vector2(120, 40) };
        var cancel = new Button { Text = "Cancel", Name = "Cancel", Size = new Vector2(120, 40) };
        root.AddChild(save);
        root.AddChild(cancel);
        context.Add(root);
        context.Layout();
        return (context, save, cancel);
    }

    [Test]
    public void ResolvesByRoleAndName()
    {
        var (context, save, _) = TwoButtons();
        using var _context = context;

        var node = context.ByRole(AccessibilityRole.Button).ByName("Save").Resolve();

        Assert.That(node.Id, Is.EqualTo(save.AccessibilityId));
    }

    [Test]
    public void ResolvesByAutomationId()
    {
        var (context, save, _) = TwoButtons();
        using var _context = context;

        Assert.That(context.ByAutomationId("cmd.save").Resolve().Id, Is.EqualTo(save.AccessibilityId));
    }

    [Test]
    public void ByTextMatchesLooselyByDefault()
    {
        var (context, save, _) = TwoButtons();
        using var _context = context;

        Assert.That(context.ByText("sav").Resolve().Id, Is.EqualTo(save.AccessibilityId),
            "substring and case-insensitive, like Playwright's text engine");
    }

    [Test]
    public void LocatorsAreLazyAndSeeLaterChanges()
    {
        using var context = CreateContext();
        var root = new StackPanel { Size = new Vector2(400, 200) };
        context.Add(root);

        // Built before the element exists. A locator describes how to find something, so this is
        // expected to work once the element appears.
        var locator = context.ByRole(AccessibilityRole.Button).ByName("Later");
        Assert.That(locator.Exists, Is.False);

        root.AddChild(new Button { Text = "Later", Name = "Later", Size = new Vector2(100, 30) });
        context.Layout();

        Assert.That(locator.Exists, Is.True);
    }

    [Test]
    public void AmbiguityFailsLoudlyAndListsTheCandidates()
    {
        using var context = CreateContext();
        var root = new StackPanel { Size = new Vector2(400, 200) };
        root.AddChild(new Button { Text = "Same", Name = "Same", Size = new Vector2(100, 30) });
        root.AddChild(new Button { Text = "Same", Name = "Same", Size = new Vector2(100, 30) });
        context.Add(root);
        context.Layout();

        var error = Assert.Throws<LocatorException>(() => context.ByName("Same").Resolve());

        Assert.That(error.Message, Does.Contain("ambiguous"));
        Assert.That(error.Message, Does.Contain("Nth"), "the message should say how to fix it");
        Assert.That(error.Message, Does.Contain("Tree at the time of the failure"),
            "a bare 'not found' is nearly useless in a log");
    }

    [Test]
    public void MissingElementReportsTheQueryAndTheTree()
    {
        var (context, _, _) = TwoButtons();
        using var _context = context;

        var error = Assert.Throws<LocatorException>(() => context.ByName("Nonexistent").Resolve());

        Assert.That(error.Message, Does.Contain("resolved to nothing"));
        Assert.That(error.Message, Does.Contain("Nonexistent"), "the failing query must be in the message");
        Assert.That(error.Message, Does.Contain("Button"), "surrounding tree gives the reader somewhere to look");
    }

    [Test]
    public void NthSelectsAmongMatchesAndLastCountsFromTheEnd()
    {
        using var context = CreateContext();
        var root = new StackPanel { Size = new Vector2(400, 300) };
        var first = new Button { Name = "Same", Size = new Vector2(100, 30) };
        var second = new Button { Name = "Same", Size = new Vector2(100, 30) };
        var third = new Button { Name = "Same", Size = new Vector2(100, 30) };
        root.AddChild(first);
        root.AddChild(second);
        root.AddChild(third);
        context.Add(root);
        context.Layout();

        var locator = context.ByName("Same");

        Assert.That(locator.Count, Is.EqualTo(3));
        Assert.That(locator.First.Resolve().Id, Is.EqualTo(first.AccessibilityId));
        Assert.That(locator.Nth(1).Resolve().Id, Is.EqualTo(second.AccessibilityId));
        Assert.That(locator.Last.Resolve().Id, Is.EqualTo(third.AccessibilityId), "-1 means the last match");
    }

    [Test]
    public void WithinRestrictsToDescendants()
    {
        using var context = CreateContext();
        var left = new StackPanel { Name = "Left", Size = new Vector2(200, 200) };
        var right = new StackPanel { Name = "Right", Size = new Vector2(200, 200) };
        var leftOk = new Button { Name = "Ok", Size = new Vector2(80, 30) };
        var rightOk = new Button { Name = "Ok", Size = new Vector2(80, 30) };
        left.AddChild(leftOk);
        right.AddChild(rightOk);
        var root = new StackPanel { Size = new Vector2(400, 400) };
        root.AddChild(left);
        root.AddChild(right);
        context.Add(root);
        context.Layout();

        // Ambiguous globally, unique within a scope — the usual reason scoping exists.
        Assert.That(context.ByName("Ok").Count, Is.EqualTo(2));
        Assert.That(context.ByName("Right").Within().ByName("Ok").Resolve().Id, Is.EqualTo(rightOk.AccessibilityId));
    }

    [Test]
    public void AssertionHelpersReadState()
    {
        var (context, save, _) = TwoButtons();
        using var _context = context;
        save.GrabFocus();

        var locator = context.ByAutomationId("cmd.save");

        Assert.Multiple(() =>
        {
            Assert.That(locator.IsVisible, Is.True);
            Assert.That(locator.IsEnabled, Is.True);
            Assert.That(locator.IsFocused, Is.True);
            Assert.That(locator.HasText("Save"), Is.True);
        });
    }

    // --- actionability ---------------------------------------------------------------------------

    [Test]
    public void ADisabledElementIsNotActionable()
    {
        var (context, save, _) = TwoButtons();
        using var _context = context;
        save.Enabled = false;
        context.Layout();

        var locator = context.ByAutomationId("cmd.save");

        Assert.That(locator.GetActionability(), Is.EqualTo(ActionabilityFailure.Disabled));
        var error = Assert.Throws<LocatorException>(() => locator.ResolveActionable());
        Assert.That(error.Message, Does.Contain("disabled"));
    }

    [Test]
    public void AZeroSizedElementIsNotActionable()
    {
        using var context = CreateContext();
        // Inside a CanvasPanel a child takes its measured size, and an empty Panel measures to
        // nothing. Setting Size directly would not work: Size is layout's output, not its input.
        var root = new CanvasPanel { CustomMinimumSize = new Vector2(400, 300) };
        root.AddChild(new Panel { Name = "Tiny", AutomationId = "tiny" });
        context.Add(root);
        context.Layout();

        Assert.That(context.ByAutomationId("tiny").GetActionability(), Is.EqualTo(ActionabilityFailure.ZeroSized));
    }

    [Test]
    public void AnElementCoveredByAnotherIsNotActionable()
    {
        using var context = CreateContext();
        var root = new CanvasPanel { CustomMinimumSize = new Vector2(400, 300) };
        // CustomMinimumSize, because Size is what layout produces rather than what it consumes.
        // Both occupy the same rectangle and the overlay is added second, so it draws on top.
        var underneath = new Button { Name = "Under", AutomationId = "under", CustomMinimumSize = new Vector2(200, 100) };
        var over = new Panel { Name = "Over", CustomMinimumSize = new Vector2(200, 100) };
        root.AddChild(underneath);
        root.AddChild(over);
        context.Add(root);
        context.Layout();

        // No state flag reports this; only hit-testing the element's own centre catches it, which is
        // exactly the failure mode a coordinate-free test would otherwise hide.
        Assert.That(context.ByAutomationId("under").GetActionability(), Is.EqualTo(ActionabilityFailure.Obscured));
    }

    [Test]
    public void AnElementBehindAModalIsNotEvenPresent()
    {
        using var context = CreateContext();
        var behind = new Button { Name = "Behind", AutomationId = "behind", Size = new Vector2(100, 40) };
        context.Add(behind);

        var popup = new PopupPanel { Size = new Vector2(200, 100) };
        context.Add(popup);
        popup.PopupAt(new Vector2(10, 10));

        // Gated at the snapshot, not at actionability: a consumer should not see it at all.
        Assert.That(context.ByAutomationId("behind").Exists, Is.False);
        Assert.That(context.ByAutomationId("behind").GetActionability(), Is.EqualTo(ActionabilityFailure.NotPresent));
    }

    [Test]
    public void AnActionableElementPassesTheGate()
    {
        var (context, save, _) = TwoButtons();
        using var _context = context;

        Assert.That(context.ByAutomationId("cmd.save").GetActionability(), Is.EqualTo(ActionabilityFailure.None));
        Assert.That(context.ByAutomationId("cmd.save").ResolveActionable().Id, Is.EqualTo(save.AccessibilityId));
    }

    // --- extensibility ---------------------------------------------------------------------------

    [Test]
    public void WhereIsTheExtensionSeamForAppSpecificLocators()
    {
        var (context, save, _) = TwoButtons();
        using var _context = context;

        // What a downstream package does: its own vocabulary as an extension method over Where,
        // composing with the built-in filters instead of living in a parallel API.
        var node = context.Query().WideEnoughFor(100).ByName("Save").Resolve();

        Assert.That(node.Id, Is.EqualTo(save.AccessibilityId));
        Assert.That(context.Query().WideEnoughFor(100).Description, Does.Contain("width>=100"));
    }
}

/// <summary>Stands in for a downstream app's locator package, proving the seam works.</summary>
internal static class SampleDomainLocators
{
    public static Locator WideEnoughFor(this Locator locator, int width) =>
        locator.Where($"width>={width}", node => node.Bounds.Width >= width);
}
