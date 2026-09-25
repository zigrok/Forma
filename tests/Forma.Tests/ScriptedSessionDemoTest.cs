// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Linq;
using Microsoft.Xna.Framework;

namespace Forma.Tests;

/// <summary>
/// The Track A demo: a session driven entirely by role, name and automation id.
/// <para>
/// There is not a single pixel coordinate or frame count anywhere below, which is the whole point.
/// Compare with what this replaces — deriving physical pixel coordinates by screenshotting the app
/// and measuring the PNG, then advancing a guessed number of frames and hoping.
/// </para>
/// </summary>
public sealed class ScriptedSessionDemoTest
{
    private static UIContext CreateContext() => new() { ViewportSize = new Vector2(900, 700) };

    /// <summary>A small app: a form with fields, a slider and buttons, plus a disabled control.</summary>
    private static UIContext BuildApp(out Button submit)
    {
        var context = CreateContext();
        var root = new StackPanel { CustomMinimumSize = new Vector2(600, 500) };

        var name = new LineEdit { AutomationId = "field.name", CustomMinimumSize = new Vector2(240, 30) };
        var volume = new HSlider { AutomationId = "field.volume", MinValue = 0, MaxValue = 100, Step = 10, Value = 20 };
        volume.CustomMinimumSize = new Vector2(240, 24);

        var agree = new CheckBox { Text = "Agree", Name = "Agree", AutomationId = "field.agree", CustomMinimumSize = new Vector2(120, 30) };
        submit = new Button { Text = "Submit", Name = "Submit", AutomationId = "cmd.submit", CustomMinimumSize = new Vector2(120, 40) };
        var cancel = new Button { Text = "Cancel", Name = "Cancel", AutomationId = "cmd.cancel", Enabled = false, CustomMinimumSize = new Vector2(120, 40) };

        root.AddChild(name);
        root.AddChild(volume);
        root.AddChild(agree);
        root.AddChild(submit);
        root.AddChild(cancel);
        context.Add(root);
        return context;
    }

    [Test]
    public void ASessionRunsEntirelyByNameWithNoCoordinates()
    {
        using var context = BuildApp(out var submit);
        var submitted = 0;
        submit.Pressed += (_, _) => submitted++;

        // Fill a field, set a value, tick a box, submit. No layout call, no frame count, no pixels.
        context.ByAutomationId("field.name").Fill("Ada Lovelace");
        context.ByAutomationId("field.volume").SetValue(70);
        context.ByAutomationId("field.agree").Click();
        context.ByRole(AccessibilityRole.Button).ByName("Submit").Click();

        UiAssert.HasValue(context.ByAutomationId("field.name"), "Ada Lovelace");
        UiAssert.HasValue(context.ByAutomationId("field.volume"), "70");
        Assert.That(submitted, Is.EqualTo(1));
    }

    [Test]
    public void TheDisabledControlIsRefusedRatherThanSilentlyClicked()
    {
        using var context = BuildApp(out _);

        UiAssert.IsVisible(context.ByAutomationId("cmd.cancel"));
        Assert.That(context.ByAutomationId("cmd.cancel").GetActionability(), Is.EqualTo(ActionabilityFailure.Disabled));

        // The value of the gate: without it this would "click" and the test would pass while proving
        // nothing about whether a person could use the control.
        Assert.Throws<LocatorException>(() => context.ByAutomationId("cmd.cancel").Click());
    }

    [Test]
    public void AssertionFailuresCarryTheTree()
    {
        using var context = BuildApp(out _);

        var error = Assert.Throws<UiAssertionException>(
            () => UiAssert.HasText(context.ByAutomationId("cmd.submit"), "Delete"));

        Assert.That(error.Message, Does.Contain("Tree at the time of the failure"));
        Assert.That(error.Message, Does.Contain("cmd.submit"), "the reader should not have to reproduce it locally");
    }

    [Test]
    public void ListsAreNavigatedByRoleAndName()
    {
        using var context = CreateContext();
        var list = new ListBox
        {
            AutomationId = "list.people",
            ItemsSource = new[] { "Ada", "Grace", "Katherine" },
            CustomMinimumSize = new Vector2(240, 200),
        };
        context.Add(list);

        var items = context.ByRole(AccessibilityRole.ListItem).ResolveAll();

        Assert.That(items.Select(item => item.Name), Does.Contain("Grace"));
        UiAssert.IsVisible(context.ByRole(AccessibilityRole.ListItem).ByName("Grace"));
    }

    [Test]
    public void ScopingDisambiguatesRepeatedNames()
    {
        using var context = CreateContext();
        var left = new StackPanel { Name = "Left", AutomationId = "panel.left", CustomMinimumSize = new Vector2(200, 200) };
        var right = new StackPanel { Name = "Right", AutomationId = "panel.right", CustomMinimumSize = new Vector2(200, 200) };
        var leftDelete = new Button { Text = "Delete", Name = "Delete", CustomMinimumSize = new Vector2(100, 30) };
        var rightDelete = new Button { Text = "Delete", Name = "Delete", CustomMinimumSize = new Vector2(100, 30) };
        var leftPressed = 0;
        leftDelete.Pressed += (_, _) => leftPressed++;
        left.AddChild(leftDelete);
        right.AddChild(rightDelete);

        var root = new StackPanel { CustomMinimumSize = new Vector2(500, 500) };
        root.AddChild(left);
        root.AddChild(right);
        context.Add(root);

        // Two identical buttons is the normal case in a real app, and the reason scoping exists.
        UiAssert.HasCount(context.ByName("Delete"), 2);
        context.ByAutomationId("panel.left").Within().ByName("Delete").Click();

        Assert.That(leftPressed, Is.EqualTo(1));
    }
}
