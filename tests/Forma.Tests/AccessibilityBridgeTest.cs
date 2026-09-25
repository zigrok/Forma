// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using Forma.Accessibility;
using Microsoft.Xna.Framework;

namespace Forma.Tests;

/// <summary>
/// The platform-neutral half of the accessibility bridge, through a recording host.
/// <para>
/// The macOS half cannot be tested here: it needs a window, a native library and a live assistive
/// technology. What can be tested is everything that decides <em>what</em> is published and
/// <em>what happens</em> when the platform asks for something — which is where the behaviour lives.
/// </para>
/// </summary>
public sealed class AccessibilityBridgeTest
{
    private static UIContext CreateContext() => new() { ViewportSize = new Vector2(800, 600) };

    [Test]
    public void AttachingReportsWhetherThePlatformCouldStart()
    {
        using var context = CreateContext();
        var host = new RecordingHost { AttachResult = false };
        using var bridge = new AccessibilityBridge(context, host);

        // False is a normal outcome, not an exception: no window, no adapter, no native library.
        // An application should be able to ask for accessibility unconditionally.
        Assert.That(bridge.Attach(new IntPtr(1)), Is.False);
        Assert.That(bridge.IsAttached, Is.False);
    }

    [Test]
    public void NothingIsPublishedBeforeAttaching()
    {
        using var context = CreateContext();
        var host = new RecordingHost();
        using var bridge = new AccessibilityBridge(context, host);

        bridge.Update();
        bridge.SetWindowFocused(true);

        Assert.That(host.Updates, Is.Empty);
        Assert.That(host.FocusChanges, Is.Empty);
    }

    [Test]
    public void PublishesTheLiveTreeOnEveryUpdate()
    {
        using var context = CreateContext();
        var button = new Button { Text = "Save", AutomationId = "save" };
        context.Add(button);
        context.WaitForSettled();

        var host = new RecordingHost();
        using var bridge = new AccessibilityBridge(context, host);
        bridge.Attach(new IntPtr(1));

        bridge.Update();
        button.Text = "Save as";
        context.WaitForSettled();
        bridge.Update();

        // Captured fresh each time rather than cached: a bridge that published a stale tree would
        // have an assistive technology announcing a label the control no longer has.
        Assert.That(host.Updates, Has.Count.EqualTo(2));
        Assert.That(NamesIn(host.Updates[0]), Does.Contain("Save"));
        Assert.That(NamesIn(host.Updates[1]), Does.Contain("Save as"));
    }

    [Test]
    public void AnActionFromThePlatformReachesTheControl()
    {
        using var context = CreateContext();
        var pressed = 0;
        var button = new Button { Text = "Save", AutomationId = "save" };
        button.Pressed += (_, _) => pressed++;
        context.Add(button);
        context.WaitForSettled();

        var host = new RecordingHost();
        using var bridge = new AccessibilityBridge(context, host);
        bridge.Attach(new IntPtr(1));

        host.RaiseAction(new AccessibilityActionRequest(button.AccessibilityId, AccessibilityActions.Press));

        Assert.That(pressed, Is.EqualTo(1));
    }

    [Test]
    public void AnActionCarriesItsArgument()
    {
        using var context = CreateContext();
        var slider = new HSlider { MinValue = 0, MaxValue = 100, Value = 0, AccessibilityLabel = "Volume" };
        context.Add(slider);
        context.WaitForSettled();

        var host = new RecordingHost();
        using var bridge = new AccessibilityBridge(context, host);
        bridge.Attach(new IntPtr(1));

        // A set-value with no argument would look like it succeeded and change nothing, which is
        // the worst outcome available: the platform believes the value is what it asked for.
        host.RaiseAction(new AccessibilityActionRequest(slider.AccessibilityId, AccessibilityActions.SetValue, 42d));

        Assert.That(slider.Value, Is.EqualTo(42).Within(0.001));
    }

    [Test]
    public void AnActionAControlDoesNotAdvertiseIsRefused()
    {
        using var context = CreateContext();
        var label = new Label { Text = "Read only" };
        context.Add(label);
        context.WaitForSettled();

        // Not a crash and not a silent success. An outside caller must not be able to reach
        // behaviour a person cannot, which is the point of describing the tree at all.
        Assert.That(
            AccessibilityTree.TryPerformAction(context, label.AccessibilityId, AccessibilityActions.Press),
            Is.False);
    }

    [Test]
    public void AnActionForANodeThatHasLeftTheTreeIsRefused()
    {
        using var context = CreateContext();
        var button = new Button { Text = "Save" };
        context.Add(button);
        context.WaitForSettled();
        var id = button.AccessibilityId;

        context.Remove(button);
        context.WaitForSettled();

        // A snapshot node is a value, so it outlives the control. Acting on one that has gone is
        // ordinary timing, not an error.
        Assert.That(AccessibilityTree.TryPerformAction(context, id, AccessibilityActions.Press), Is.False);
    }

    [Test]
    public void TheNullHostAnswersNoToEverythingWithoutThrowing()
    {
        using var context = CreateContext();
        using var host = new NullAccessibilityHost();
        using var bridge = new AccessibilityBridge(context, host);

        Assert.Multiple(() =>
        {
            Assert.That(host.IsSupported, Is.False);
            Assert.That(bridge.Attach(IntPtr.Zero), Is.False);
            Assert.DoesNotThrow(() => bridge.Update());
            Assert.DoesNotThrow(() => bridge.SetWindowFocused(true));
        });
    }

    private static IReadOnlyCollection<string> NamesIn(AccessibilityTreeSnapshot snapshot)
    {
        var names = new List<string>();
        foreach (var node in snapshot.Nodes) names.Add(node.Name);
        return names;
    }

    /// <summary>A host that records what it was told and can play the platform's part.</summary>
    private sealed class RecordingHost : IAccessibilityHost
    {
        private Action<AccessibilityActionRequest> _performAction;

        public bool AttachResult { get; set; } = true;

        public List<AccessibilityTreeSnapshot> Updates { get; } = new();

        public List<bool> FocusChanges { get; } = new();

        public bool IsSupported => true;

        public bool Attach(IntPtr windowHandle, Func<AccessibilityTreeSnapshot> requestTree, Action<AccessibilityActionRequest> performAction)
        {
            _performAction = performAction;
            return AttachResult;
        }

        public void Update(AccessibilityTreeSnapshot snapshot) => Updates.Add(snapshot);

        public void SetWindowFocused(bool focused) => FocusChanges.Add(focused);

        public void RaiseAction(AccessibilityActionRequest request) => _performAction?.Invoke(request);

        public void Dispose() => _performAction = null;
    }
}
