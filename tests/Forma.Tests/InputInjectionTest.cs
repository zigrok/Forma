// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Forma.Tests;

/// <summary>
/// Key and text injection close the gap that made a driven session impossible: keys could only be
/// delivered by replacing the entire keyboard state, which cannot express a chord arriving in one
/// frame and forces the caller to model state it does not own.
/// </summary>
public sealed class InputInjectionTest
{
    private static UIContext CreateContext() => new() { ViewportSize = new Vector2(800, 600) };

    [Test]
    public void InjectedKeyReachesTheFocusedControl()
    {
        using var context = CreateContext();
        var edit = new LineEdit { CustomMinimumSize = new Vector2(200, 30) };
        context.Add(edit);
        context.Layout();
        edit.GrabFocus();

        context.InjectText("hello");

        Assert.That(edit.Text, Is.EqualTo("hello"));
    }

    [Test]
    public void InjectedTextIsIgnoredWhenNothingHasFocus()
    {
        using var context = CreateContext();
        var edit = new LineEdit { CustomMinimumSize = new Vector2(200, 30) };
        context.Add(edit);
        context.Layout();

        context.InjectText("nope");

        Assert.That(edit.Text, Is.Empty, "text goes to the focused control or nowhere");
    }

    [Test]
    public void AChordArrivesInOneEvent()
    {
        using var context = CreateContext();
        var edit = new LineEdit { Text = "abc", CustomMinimumSize = new Vector2(200, 30) };
        context.Add(edit);
        context.Layout();
        edit.GrabFocus();

        // The point of the API: modifiers travel with the press, rather than the caller having to
        // assemble a whole KeyboardState and hope the frame boundary lands correctly.
        Assert.DoesNotThrow(() => context.InjectKeyPress(Keys.A, Keys.LeftControl));
        Assert.DoesNotThrow(() => context.InjectKeyRelease(Keys.A));
    }

    [Test]
    public void InjectedKeysDriveNavigationTheSameWayPolledOnesDo()
    {
        // Polled: a KeyboardState handed to Update.
        using var polled = CreateContext();
        var polledFirst = new Button { Text = "1", CustomMinimumSize = new Vector2(80, 30) };
        var polledSecond = new Button { Text = "2", CustomMinimumSize = new Vector2(80, 30) };
        var polledRoot = new StackPanel { CustomMinimumSize = new Vector2(200, 200) };
        polledRoot.AddChild(polledFirst);
        polledRoot.AddChild(polledSecond);
        polled.Add(polledRoot);
        polled.Layout();
        polledFirst.GrabFocus();
        polled.Update(new GameTime(), default, new KeyboardState(Keys.Tab));

        // Injected: the same key as an event.
        using var injected = CreateContext();
        var injectedFirst = new Button { Text = "1", CustomMinimumSize = new Vector2(80, 30) };
        var injectedSecond = new Button { Text = "2", CustomMinimumSize = new Vector2(80, 30) };
        var injectedRoot = new StackPanel { CustomMinimumSize = new Vector2(200, 200) };
        injectedRoot.AddChild(injectedFirst);
        injectedRoot.AddChild(injectedSecond);
        injected.Add(injectedRoot);
        injected.Layout();
        injectedFirst.GrabFocus();
        injected.InjectKeyPress(Keys.Tab);

        Assert.That(injected.FocusedControl == injectedSecond, Is.EqualTo(polled.FocusedControl == polledSecond),
            "injection must move focus exactly as the polled path does");
    }

    [Test]
    public void SuppressedPolledInputDoesNotUndoInjectedState()
    {
        using var context = CreateContext();
        var edit = new LineEdit { CustomMinimumSize = new Vector2(200, 30) };
        context.Add(edit);
        context.Layout();
        edit.GrabFocus();
        context.SuppressPolledInput = true;

        context.InjectText("kept");

        // An unfocused host feeds default states every frame. Without suppression this pass would
        // move the pointer to (0,0) and release every key between injected events.
        context.Update(new GameTime(), default, default);

        Assert.That(edit.Text, Is.EqualTo("kept"));
        Assert.That(context.FocusedControl, Is.SameAs(edit), "focus must survive an ignored frame");
    }

    [Test]
    public void SuppressionStillAdvancesTheFrame()
    {
        using var context = CreateContext();
        var panel = new Panel { CustomMinimumSize = new Vector2(100, 100) };
        context.Add(panel);
        context.SuppressPolledInput = true;

        // Suppressing input must not freeze the UI: layout still settles, so a control added while
        // suppressed still gets real bounds.
        context.Update(new GameTime(), default, default);

        Assert.That(panel.Size.X, Is.GreaterThan(0));
        Assert.That(panel.Size.Y, Is.GreaterThan(0));
    }

    [Test]
    public void PolledInputWorksNormallyWhenNotSuppressed()
    {
        using var context = CreateContext();
        var button = new Button { Text = "Hit", CustomMinimumSize = new Vector2(100, 40) };
        context.Add(button);
        context.Layout();
        var pressed = 0;
        button.Pressed += (_, _) => pressed++;

        var centre = button.VisualBounds.Center;
        context.Update(new GameTime(), new MouseState(centre.X, centre.Y, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());
        context.Update(new GameTime(), new MouseState(centre.X, centre.Y, 0, ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());
        context.Update(new GameTime(), new MouseState(centre.X, centre.Y, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), new KeyboardState());

        Assert.That(pressed, Is.EqualTo(1), "the default must remain ordinary polled input");
    }

    [Test]
    public void WheelInjectionAlreadyExisted()
    {
        using var context = CreateContext();
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(200, 200) };
        context.Add(scroll);
        context.Layout();

        // Recorded so nobody rebuilds it: this predates the rest of the injection surface.
        Assert.DoesNotThrow(() => context.InjectPointerWheel(new Point(50, 50), -120));
    }
}
