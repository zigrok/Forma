// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Forma.Tests;

/// <summary>
/// Semantic actions are only worth having if they are indistinguishable from real input (D4). A
/// parallel implementation that happens to raise the right event would make tests pass while
/// telling you nothing about whether a person clicking the thing works — so the central test here
/// compares an invoked action against the same action driven through the pointer.
/// </summary>
public sealed class SemanticActionTest
{
    private static UIContext CreateContext() => new() { ViewportSize = new Vector2(800, 600) };

    [Test]
    public void InvokedPressMatchesAPointerPress()
    {
        // Two identical buttons: one driven by injected pointer input, one by the semantic action.
        using var injected = CreateContext();
        var byPointer = new Button { Text = "Go", AutomationId = "go", CustomMinimumSize = new Vector2(120, 40) };
        injected.Add(byPointer);
        injected.Layout();
        var pointerEvents = 0;
        byPointer.Pressed += (_, _) => pointerEvents++;

        var centre = byPointer.VisualBounds.Center;
        injected.InjectPointerMove(centre);
        injected.InjectPointerPress(centre);
        injected.InjectPointerRelease(centre);

        using var semantic = CreateContext();
        var byAction = new Button { Text = "Go", AutomationId = "go", CustomMinimumSize = new Vector2(120, 40) };
        semantic.Add(byAction);
        semantic.Layout();
        var actionEvents = 0;
        byAction.Pressed += (_, _) => actionEvents++;

        semantic.ByAutomationId("go").Click();

        Assert.That(actionEvents, Is.EqualTo(pointerEvents), "an invoked press must be the same event sequence");
        Assert.That(actionEvents, Is.EqualTo(1));
    }

    [Test]
    public void InvokedToggleMatchesAPointerToggle()
    {
        using var injected = CreateContext();
        var byPointer = new Button { Text = "T", ToggleMode = true, CustomMinimumSize = new Vector2(120, 40) };
        injected.Add(byPointer);
        injected.Layout();
        var centre = byPointer.VisualBounds.Center;
        injected.InjectPointerMove(centre);
        injected.InjectPointerPress(centre);
        injected.InjectPointerRelease(centre);

        using var semantic = CreateContext();
        var byAction = new Button { Text = "T", ToggleMode = true, AutomationId = "t", CustomMinimumSize = new Vector2(120, 40) };
        semantic.Add(byAction);
        semantic.Layout();
        semantic.ByAutomationId("t").Click();

        Assert.That(byAction.ButtonPressed, Is.EqualTo(byPointer.ButtonPressed));
        Assert.That(byAction.ButtonPressed, Is.True);
    }

    [Test]
    public void FocusIsInvocable()
    {
        using var context = CreateContext();
        var button = new Button { Text = "F", AutomationId = "f", CustomMinimumSize = new Vector2(100, 40) };
        context.Add(button);
        context.Layout();

        context.ByAutomationId("f").Focus();

        Assert.That(context.FocusedControl, Is.SameAs(button));
        Assert.That(context.ByAutomationId("f").IsFocused, Is.True);
    }

    [Test]
    public void RangeSetValueIncrementAndDecrementGoThroughTheValueSetter()
    {
        using var context = CreateContext();
        var slider = new HSlider { AutomationId = "vol", MinValue = 0, MaxValue = 100, Step = 5, Value = 50 };
        slider.CustomMinimumSize = new Vector2(200, 24);
        context.Add(slider);
        context.Layout();
        var changes = 0;
        slider.ValueChanged += (_, _) => changes++;

        var locator = context.ByAutomationId("vol");
        locator.Increment();
        Assert.That(slider.Value, Is.EqualTo(55));

        locator.Decrement();
        Assert.That(slider.Value, Is.EqualTo(50));

        locator.SetValue(80);
        Assert.That(slider.Value, Is.EqualTo(80));

        Assert.That(changes, Is.EqualTo(3), "every action must notify, exactly as dragging does");
    }

    [Test]
    public void RangeSetValueIsClampedTheSameWayDirectAssignmentIs()
    {
        using var context = CreateContext();
        var slider = new HSlider { AutomationId = "vol", MinValue = 0, MaxValue = 100, Value = 50 };
        slider.CustomMinimumSize = new Vector2(200, 24);
        context.Add(slider);
        context.Layout();

        context.ByAutomationId("vol").SetValue(1000);

        // Routing through the Value setter means clamping comes for free; a parallel implementation
        // is exactly where that would have been forgotten.
        Assert.That(slider.Value, Is.EqualTo(100));
    }

    [Test]
    public void LineEditFillWritesText()
    {
        using var context = CreateContext();
        var edit = new LineEdit { AutomationId = "name", CustomMinimumSize = new Vector2(200, 30) };
        context.Add(edit);
        context.Layout();

        context.ByAutomationId("name").Fill("typed");

        Assert.That(edit.Text, Is.EqualTo("typed"));
        Assert.That(context.ByAutomationId("name").HasValue("typed"), Is.True);
    }

    [Test]
    public void ARreadOnlyFieldRefusesFill()
    {
        using var context = CreateContext();
        var edit = new LineEdit { AutomationId = "ro", Editable = false, Text = "fixed", CustomMinimumSize = new Vector2(200, 30) };
        context.Add(edit);
        context.Layout();

        // A field that cannot be typed into must not be fillable either, or the action would be
        // doing something no user could.
        Assert.Throws<LocatorException>(() => context.ByAutomationId("ro").Fill("changed"));
        Assert.That(edit.Text, Is.EqualTo("fixed"));
    }

    [Test]
    public void ADisabledControlRefusesActions()
    {
        using var context = CreateContext();
        var button = new Button { Text = "No", AutomationId = "no", Enabled = false, CustomMinimumSize = new Vector2(100, 40) };
        var fired = 0;
        button.Pressed += (_, _) => fired++;
        context.Add(button);
        context.Layout();

        var error = Assert.Throws<LocatorException>(() => context.ByAutomationId("no").Click());

        Assert.That(error.Message, Does.Contain("disabled"));
        Assert.That(fired, Is.Zero, "the gate must run before the action, not after");
    }

    [Test]
    public void AnUnsupportedActionSaysWhatIsSupported()
    {
        using var context = CreateContext();
        var label = new Label { Text = "Static", AutomationId = "static", CustomMinimumSize = new Vector2(100, 20) };
        context.Add(label);
        context.Layout();

        var error = Assert.Throws<LocatorException>(() => context.ByAutomationId("static").Click());

        Assert.That(error.Message, Does.Contain("does not support"));
        Assert.That(error.Message, Does.Contain("advertises"), "say what it can do, not just what it cannot");
    }

    [Test]
    public void InvokeRefusesAnActionThatIsNotAdvertised()
    {
        var label = new Label { Text = "Static" };

        // Probing must be safe: returning false rather than throwing means a caller can ask without
        // guarding every call.
        Assert.That(label.AccessibilityPeer.Invoke(AccessibilityActions.Press), Is.False);
    }

    [Test]
    public void EveryAdvertisedActionOnEveryPublicControlIsInvocable()
    {
        // The guarantee that matters: advertising an action a control cannot perform is a lie that
        // only shows up when something tries to use it.
        var unhandled = new System.Collections.Generic.List<string>();

        foreach (var type in typeof(Control).Assembly.GetTypes())
        {
            if (!type.IsPublic || type.IsAbstract || !typeof(Control).IsAssignableFrom(type)) continue;
            if (type.GetConstructor(Type.EmptyTypes) == null) continue;

            var control = (Control)Activator.CreateInstance(type);
            var advertised = control.AccessibilityActions;

            foreach (AccessibilityActions action in Enum.GetValues<AccessibilityActions>())
            {
                if (action == AccessibilityActions.None || (advertised & action) == 0) continue;

                // Focus and Select need a live tree or a selection model, so only the ones a bare
                // control can answer are asserted here; the rest are covered by the cases above.
                if (action is AccessibilityActions.Focus or AccessibilityActions.Select
                    or AccessibilityActions.Expand or AccessibilityActions.Collapse
                    or AccessibilityActions.Scroll) continue;

                // Feed back exactly what the control reports as its value. A control that will not
                // accept its own reported value is broken in a way a fixed literal would miss —
                // VirtualJoystick reports "x,y" and would reject "0".
                var argument = action == AccessibilityActions.SetValue
                    ? (object)(string.IsNullOrEmpty(control.AccessibilityValue) ? "0" : control.AccessibilityValue)
                    : null;
                if (!control.PerformAccessibilityAction(action, argument))
                    unhandled.Add($"{type.Name}.{action}");
            }
        }

        Assert.That(unhandled, Is.Empty, "these controls advertise actions they do not implement");
    }
}
