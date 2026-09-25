// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Linq;

namespace Forma.Tests;

/// <summary>
/// Identity is what lets an accessibility tree be diffed instead of re-walked, and what lets a test
/// or an out-of-process client name a node. These pin the two properties that guarantee are worth
/// anything: ids are unique per node, and an automation id is independent of anything user-visible.
/// </summary>
public sealed class AccessibilityIdentityTest
{
    [Test]
    public void EveryControlGetsADistinctId()
    {
        var controls = Enumerable.Range(0, 200).Select(_ => new Button()).ToList();

        var ids = controls.Select(control => control.AccessibilityId).ToList();

        Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count), "ids must be unique per control");
        Assert.That(ids, Has.All.GreaterThan(0), "zero reads as 'unset' and must never be handed out");
    }

    [Test]
    public void IdIsStableForTheLifetimeOfTheControl()
    {
        var button = new Button();
        var first = button.AccessibilityId;

        // Anything that rebuilds visuals or re-reads state must not renumber the control itself.
        button.Text = "changed";
        button.AutomationId = "changed-too";
        _ = button.AccessibilityPeer;

        Assert.That(button.AccessibilityId, Is.EqualTo(first));
    }

    [Test]
    public void PeerReportsItsOwnersId()
    {
        var button = new Button();

        Assert.That(button.AccessibilityPeer.Id, Is.EqualTo(button.AccessibilityId));
    }

    [Test]
    public void AutomationIdIsEmptyUntilSetAndIsIndependentOfName()
    {
        var button = new Button();
        Assert.That(button.AutomationId, Is.Empty);

        button.Name = "visible-name";
        Assert.That(button.AutomationId, Is.Empty, "a name is not an automation id");

        button.AutomationId = "save-button";
        button.Name = "renamed";

        // The whole point: relabelling, including localizing, must not move the target a test binds to.
        Assert.That(button.AutomationId, Is.EqualTo("save-button"));
        Assert.That(button.AccessibilityPeer.AutomationId, Is.EqualTo("save-button"));
    }

    [Test]
    public void AutomationIdDoesNotDisturbTheAccessibleName()
    {
        var button = new Button { Name = "Save", AutomationId = "cmd.save" };

        // AccessibilityName is what a screen reader announces; it must stay the human-facing text.
        Assert.That(button.AccessibilityName, Is.EqualTo("Save"));
    }

    [Test]
    public void AutomationIdRaisesPropertyChanged()
    {
        var button = new Button();
        var changed = new List<string>();
        button.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        button.AutomationId = "first";
        button.AutomationId = "first";

        Assert.That(changed.Count(name => name == nameof(Control.AutomationId)), Is.EqualTo(1),
            "setting the same value again should not notify");
    }

    [Test]
    public void VirtualizedItemPeersDoNotAllShareTheListsId()
    {
        var list = new ListBox
        {
            ItemsSource = new[] { "alpha", "beta", "gamma" },
            Size = new Microsoft.Xna.Framework.Vector2(200, 300),
        };
        using var context = new UIContext();
        context.Add(list);
        context.Layout();

        var peers = list.GetAccessibilityChildren();
        Assume.That(peers.Count, Is.GreaterThan(1), "this asserts on realized item peers");

        var ids = peers.Select(peer => peer.Id).ToList();

        // Inheriting the owner's id would collapse every row onto the list in a snapshot keyed by id.
        Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count));
        Assert.That(ids, Has.None.EqualTo(list.AccessibilityId));
    }
}
